using System.Security.Cryptography;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Webhook subscription lifecycle and at-least-once delivery. Delivery never mutates
/// runtime or rollout state; it advances only the subscription delivery cursor.
/// </summary>
public sealed class TeamLabWebhookService(
    AppDbContext context,
    IDataProtectionProvider protection,
    ITeamLabWebhookDeliverer deliverer)
{
    private const string SecretPurpose = "GZCTF.TeamLab.Webhook.v1";
    private const int MaxRecordedFailures = 20;
    private const int DeliveryChunkSize = 100;

    public async Task<TeamLabWebhookModel> CreateForOperationAsync(
        CreateTeamLabWebhookModel model,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        TeamLabWebhookDelivery.ValidateEventTypes(model.EventTypes);
        var endpoint = await TeamLabWebhookEndpointValidator.ValidateAndNormalizeAsync(
            model.EndpointUrl, cancellationToken);
        var existing = await context.TeamLabWebhookSubscriptions.AsNoTracking()
            .Include(item => item.Failures)
            .SingleOrDefaultAsync(item => item.ApiOperationId == operationId, cancellationToken);
        if (existing is not null)
            return ToModel(existing);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var subscription = new TeamLabWebhookSubscription
        {
            ControlScopeId = model.ControlScopeId,
            EndpointUrl = endpoint,
            EventTypesJson = TeamLabWebhookDelivery.SerializeEventTypes(model.EventTypes),
            SigningSecretEncrypted = protection.CreateProtector(SecretPurpose).Protect(secret),
            Active = model.Enabled,
            CreatedById = actorUserId,
            ApiOperationId = operationId,
            DeliveryCursor = await ResolveInitialCursorAsync(model.ControlScopeId, model.FromEventId, cancellationToken)
        };
        context.TeamLabWebhookSubscriptions.Add(subscription);
        await context.SaveChangesAsync(cancellationToken);
        return await RequireModelAsync(subscription.PublicId, cancellationToken);
    }

    /// <summary>
    /// New subscriptions start from the explicit event id when provided, otherwise from
    /// the newest event in the scope ("从现在开始"), so late subscribers never replay
    /// unbounded history.
    /// </summary>
    private async Task<long> ResolveInitialCursorAsync(
        Guid scopeId,
        long? fromEventId,
        CancellationToken cancellationToken)
    {
        if (fromEventId is { } explicitId)
            return Math.Max(0, explicitId - 1);
        return await context.TeamLabEvents.AsNoTracking()
            .Where(item => item.ControlScopeId == scopeId)
            .MaxAsync(item => (long?)item.Id, cancellationToken) ?? 0;
    }

    public async Task<TeamLabWebhookModel> GetAsync(Guid publicId, CancellationToken cancellationToken) =>
        await RequireModelAsync(publicId, cancellationToken);

    public async Task<TeamLabWebhookPageModel> ListAsync(
        Guid scopeId,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, 100);
        var cursor = DecodeCursor(after);
        var query = context.TeamLabWebhookSubscriptions.AsNoTracking()
            .Where(item => item.ControlScopeId == scopeId);
        if (cursor is { } value)
            query = query.Where(item => item.CreatedAt < value.Time ||
                                        item.CreatedAt == value.Time && item.PublicId.CompareTo(value.Id) < 0);
        var rows = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.PublicId)
            .Take(take + 1)
            .ToArrayAsync(cancellationToken);
        var page = rows.Take(take).Select(ToModel).ToArray();
        var next = rows.Length > take
            ? new GuidTimeCursor(page[^1].CreatedAt, page[^1].Id).Encode()
            : null;
        return new TeamLabWebhookPageModel(page, next);
    }

    public async Task RevokeAsync(Guid publicId, CancellationToken cancellationToken)
    {
        var subscription = await RequireAsync(publicId, cancellationToken);
        if (!subscription.Active)
            return;
        subscription.Active = false;
        subscription.RevokedAt = DateTimeOffset.UtcNow;
        subscription.NextDeliveryAt = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Re-delivers events from the given event id without advancing the delivery
    /// cursor. Bounded by <see cref="TeamLabWebhookDelivery.MaxReplayEvents"/>;
    /// delivery failures are recorded but never affect runtime or rollout state.
    /// </summary>
    public async Task<TeamLabWebhookReplayResult> ReplayAsync(
        Guid publicId,
        long? fromEventId,
        CancellationToken cancellationToken)
    {
        var subscription = await RequireAsync(publicId, cancellationToken);
        if (!subscription.Active)
            throw new TeamLabApiContractException("webhook_subscription_revoked", "订阅已撤销，无法重放。", 409);
        var events = await LoadScopeEventsAsync(
            subscription.ControlScopeId,
            Math.Max(1, fromEventId ?? 1),
            TeamLabWebhookDelivery.MaxReplayEvents,
            cancellationToken);
        var delivered = 0;
        var failed = 0;
        var eventTypes = TeamLabWebhookDelivery.ParseEventTypes(subscription.EventTypesJson);
        foreach (var localEvent in events)
        {
            if (!TeamLabWebhookDelivery.MatchesEventType(eventTypes, localEvent.Stage))
                continue;
            var result = await DeliverOneAsync(subscription, localEvent, cancellationToken);
            if (result.Succeeded)
            {
                delivered++;
            }
            else
            {
                failed++;
                AppendFailure(subscription, localEvent, result.Error);
            }
        }
        await SaveChangesAsync(cancellationToken);
        if (failed > 0)
        {
            await TrimFailureRecordsAsync(subscription, cancellationToken);
            await SaveChangesAsync(cancellationToken);
        }
        return new TeamLabWebhookReplayResult(delivered, failed);
    }

    /// <summary>Advances at-least-once delivery for active subscriptions. Called by the worker.</summary>
    public async Task<int> DeliverPendingAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var subscriptionIds = await context.TeamLabWebhookSubscriptions.AsNoTracking()
            .Where(item => item.Active &&
                           (item.NextDeliveryAt == null || item.NextDeliveryAt <= now))
            .OrderBy(item => item.NextDeliveryAt == null ? 0 : 1)
            .ThenBy(item => item.CreatedAt)
            .Take(8)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var processed = 0;
        foreach (var subscriptionId in subscriptionIds)
        {
            processed += await DeliverSubscriptionAsync(subscriptionId, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                break;
        }
        return processed;
    }

    private async Task<int> DeliverSubscriptionAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await AcquireSubscriptionLockAsync(subscriptionId, cancellationToken);
        var subscription = await context.TeamLabWebhookSubscriptions
            .Include(item => item.Failures)
            .SingleAsync(item => item.Id == subscriptionId, cancellationToken);
        if (!subscription.Active || subscription.NextDeliveryAt > DateTimeOffset.UtcNow)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return 0;
        }
        var chunk = await LoadScopeEventsAsync(
            subscription.ControlScopeId,
            subscription.DeliveryCursor + 1,
            DeliveryChunkSize,
            cancellationToken);
        if (chunk.Count == 0)
        {
            subscription.NextDeliveryAt = null;
            await SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return 0;
        }
        var eventTypes = TeamLabWebhookDelivery.ParseEventTypes(subscription.EventTypesJson);
        var delivered = 0;
        foreach (var localEvent in chunk)
        {
            if (!TeamLabWebhookDelivery.MatchesEventType(eventTypes, localEvent.Stage))
            {
                subscription.DeliveryCursor = localEvent.Id;
                continue;
            }
            var result = await DeliverOneAsync(subscription, localEvent, cancellationToken);
            if (!result.Succeeded)
            {
                subscription.ConsecutiveFailures += 1;
                if (subscription.ConsecutiveFailures >= TeamLabWebhookDelivery.MaxConsecutiveFailures)
                {
                    subscription.Active = false;
                    subscription.NextDeliveryAt = null;
                }
                else
                {
                    subscription.NextDeliveryAt = DateTimeOffset.UtcNow.Add(
                        TeamLabWebhookDelivery.RetryDelay(subscription.ConsecutiveFailures));
                }
                AppendFailure(subscription, localEvent, result.Error);
                await SaveChangesAsync(cancellationToken);
                await TrimFailureRecordsAsync(subscription, cancellationToken);
                await SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return delivered;
            }
            subscription.DeliveryCursor = localEvent.Id;
            subscription.ConsecutiveFailures = 0;
            subscription.NextDeliveryAt = null;
            delivered++;
        }
        await SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return delivered;
    }

    /// <summary>
    /// Serializes the per-subscription delivery pass with a PostgreSQL advisory
    /// transaction lock so concurrent worker instances never regress the cursor.
    /// In-memory providers skip the lock; the unique API operation constraint and
    /// at-least-once semantics remain the final guard.
    /// </summary>
    private async Task AcquireSubscriptionLockAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
            return;
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Webhook advisory lock requires an explicit transaction.");
        var lockKey = $"teamlab:webhook:{subscriptionId:N}";
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }

    private async Task<TeamLabWebhookDeliveryResult> DeliverOneAsync(
        TeamLabWebhookSubscription subscription,
        TeamLabEvent localEvent,
        CancellationToken cancellationToken)
    {
        var envelope = TeamLabWebhookDelivery.BuildEnvelope(localEvent, subscription.ControlScopeId);
        var body = TeamLabWebhookDelivery.SerializeEnvelope(envelope);
        string secret;
        try
        {
            secret = protection.CreateProtector(SecretPurpose).Unprotect(subscription.SigningSecretEncrypted);
        }
        catch (CryptographicException)
        {
            return new TeamLabWebhookDeliveryResult(false, "webhook_secret_unreadable");
        }
        var signature = TeamLabWebhookDelivery.ComputeSignature(secret, body);
        var view = new TeamLabWebhookSubscriptionView(subscription.Id, subscription.EndpointUrl, secret);
        return await deliverer.DeliverAsync(view, envelope, body, signature, cancellationToken);
    }

    private async Task<List<TeamLabEvent>> LoadScopeEventsAsync(
        Guid scopeId,
        long fromEventId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await context.TeamLabEvents.AsNoTracking()
            .Where(item => item.Id >= fromEventId &&
                           item.ControlScopeId == scopeId &&
                           item.ResourcePublicId != null)
            .OrderBy(item => item.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private void AppendFailure(
        TeamLabWebhookSubscription subscription,
        TeamLabEvent localEvent,
        string error)
    {
        subscription.Failures.Add(new TeamLabWebhookDeliveryFailure
        {
            SubscriptionId = subscription.Id,
            EventId = localEvent.Id,
            EventStage = localEvent.Stage,
            Error = error[..Math.Min(1024, error.Length)],
            OccurredAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Bounds recorded failures per subscription after persistence. Trimming happens
    /// after the insert is saved so the newest failure is never selected for removal.
    /// </summary>
    private async Task TrimFailureRecordsAsync(
        TeamLabWebhookSubscription subscription,
        CancellationToken cancellationToken)
    {
        var current = await context.TeamLabWebhookDeliveryFailures.AsNoTracking()
            .CountAsync(item => item.SubscriptionId == subscription.Id, cancellationToken);
        var overflow = current - MaxRecordedFailures;
        if (overflow <= 0)
            return;
        var oldest = await context.TeamLabWebhookDeliveryFailures.AsNoTracking()
            .Where(item => item.SubscriptionId == subscription.Id)
            .OrderBy(item => item.Id)
            .Take(overflow)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        context.TeamLabWebhookDeliveryFailures.RemoveRange(
            oldest.Select(id => new TeamLabWebhookDeliveryFailure { Id = id }));
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);

    private async Task<TeamLabWebhookSubscription> RequireAsync(
        Guid publicId,
        CancellationToken cancellationToken) =>
        await context.TeamLabWebhookSubscriptions
            .Include(item => item.Failures)
            .SingleOrDefaultAsync(item => item.PublicId == publicId, cancellationToken)
        ?? throw new TeamLabApiContractException(
            TeamLabWebhookErrorCodes.SubscriptionNotFound, "未找到 webhook 订阅。", 404);

    private async Task<TeamLabWebhookModel> RequireModelAsync(
        Guid publicId,
        CancellationToken cancellationToken) =>
        ToModel(await RequireAsync(publicId, cancellationToken));

    private static TeamLabWebhookModel ToModel(TeamLabWebhookSubscription subscription) => new(
        subscription.PublicId,
        subscription.ControlScopeId,
        subscription.EndpointUrl,
        TeamLabWebhookDelivery.ParseEventTypes(subscription.EventTypesJson),
        subscription.Active,
        subscription.DeliveryCursor,
        subscription.ConsecutiveFailures,
        subscription.NextDeliveryAt,
        subscription.CreatedAt,
        subscription.RevokedAt,
        subscription.Failures
            .OrderByDescending(item => item.Id)
            .Take(MaxRecordedFailures)
            .Select(item => new TeamLabWebhookFailureModel(
                item.Id, item.EventId, item.EventStage, item.Error, item.OccurredAt))
            .ToArray());

    private static GuidTimeCursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return GuidTimeCursor.Decode(value);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException("webhook_cursor_invalid", "webhook 分页游标无效。", 400);
        }
    }
}
