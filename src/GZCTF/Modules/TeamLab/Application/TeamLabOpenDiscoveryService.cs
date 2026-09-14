using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabOpenDiscoveryService(
    AppDbContext context,
    TeamLabScopeAuthorizationService scopeAuthorization)
{
    public async Task<OpenTeamLabRuntimePageModel> ListRuntimesAsync(
        Guid apiTokenId,
        bool hasWildcardScopeGrant,
        Guid? controlScopeId,
        string? externalReference,
        TeamLabRuntimeStatus? status,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        if (apiTokenId == Guid.Empty || limit is < 1 or > 100 ||
            status.HasValue && !Enum.IsDefined(status.Value))
            throw new TeamLabApiContractException(
                "runtime_filter_invalid", "运行时筛选条件无效。", 400);

        var normalizedReference = string.IsNullOrWhiteSpace(externalReference)
            ? null
            : externalReference.Trim();
        if (normalizedReference?.Length > 256)
            throw new TeamLabApiContractException(
                "runtime_filter_invalid", "运行时外部引用筛选条件无效。", 400);

        var cursor = DecodeCursor(after, "runtime_cursor_invalid");
        IReadOnlySet<Guid> readableScopes;
        if (controlScopeId is { } requestedScope)
        {
            await scopeAuthorization.RequireReadableAsync(
                requestedScope, apiTokenId, hasWildcardScopeGrant, cancellationToken);
            readableScopes = new HashSet<Guid> { requestedScope };
        }
        else
        {
            readableScopes = await scopeAuthorization.ListReadableScopesAsync(
                apiTokenId, hasWildcardScopeGrant, cancellationToken);
        }

        var scopeIds = readableScopes.ToArray();
        var query = context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.ControlScopeId.HasValue && scopeIds.Contains(item.ControlScopeId.Value));
        if (normalizedReference is not null)
            query = query.Where(item => item.ExternalReference == normalizedReference);
        if (status is { } requestedStatus)
            query = query.Where(item => item.Status == requestedStatus);
        if (cursor is { } value)
            query = query.Where(item => item.CreatedAt < value.Time ||
                item.CreatedAt == value.Time && item.PublicId.CompareTo(value.Id) < 0);

        var rows = await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.PublicId)
            .Take(limit + 1)
            .Select(item => new RuntimeRow(
                item.PublicId,
                item.TopologyReleaseId,
                item.ControlScopeId!.Value,
                item.ExternalReference,
                item.Generation,
                item.ExecutionModel,
                item.Status,
                item.IsOpenToPlayers,
                item.CreatedAt,
                item.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        var items = rows.Take(limit).Select(ToModel).ToArray();
        var nextCursor = rows.Length > limit
            ? new GuidTimeCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;
        return new OpenTeamLabRuntimePageModel(items, nextCursor);
    }

    public async Task<IReadOnlyList<OpenTeamLabAccessGrantMetadataModel>> ListAccessGrantsAsync(
        Guid runtimeId,
        Guid apiTokenId,
        bool hasWildcardScopeGrant,
        CancellationToken cancellationToken)
    {
        await scopeAuthorization.RequireRuntimeScopeAsync(
            runtimeId, apiTokenId, hasWildcardScopeGrant, writable: false, cancellationToken);
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimeId)
            .Select(item => new { item.Id, item.Generation })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时。", 404);
        var rows = await context.TeamLabAccessGrants.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id &&
                           item.Generation == runtime.Generation &&
                           !item.Revoked)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.PublicId)
            .Select(item => new AccessGrantRow(
                item.PublicId,
                item.Generation,
                item.Type,
                item.ClientAddress,
                item.Endpoint,
                item.AllowedIps,
                item.Dns,
                item.CreatedAt,
                item.AppliedAt,
                item.ExpiresAt,
                item.ConfigurationConsumedAt))
            .ToArrayAsync(cancellationToken);
        return rows.Select(item => new OpenTeamLabAccessGrantMetadataModel(
            item.Id,
            item.Generation,
            item.Type.ToString(),
            item.ClientAddress,
            item.Endpoint,
            item.AllowedIps,
            item.Dns,
            item.CreatedAt,
            item.AppliedAt,
            item.ExpiresAt,
            item.ConfigurationConsumedAt)).ToArray();
    }

    private static OpenTeamLabRuntimeSummaryModel ToModel(RuntimeRow item) => new(
        item.Id,
        item.ReleaseId,
        item.ControlScopeId,
        item.ExternalReference,
        item.Generation,
        item.ExecutionModel,
        item.Status,
        Stage(item.Status),
        item.OpenForAccess,
        item.CreatedAt,
        item.UpdatedAt);

    private static string Stage(TeamLabRuntimeStatus status) => status switch
    {
        TeamLabRuntimeStatus.Pending => "pending",
        TeamLabRuntimeStatus.Planning => "planning",
        TeamLabRuntimeStatus.Scheduled => "queued",
        TeamLabRuntimeStatus.Deploying => "deploying",
        TeamLabRuntimeStatus.Probing => "probing",
        TeamLabRuntimeStatus.Running => "ready",
        TeamLabRuntimeStatus.Failed => "failed",
        TeamLabRuntimeStatus.CleanupPending => "cleanup-pending",
        TeamLabRuntimeStatus.Paused => "paused",
        TeamLabRuntimeStatus.Destroying => "destroying",
        TeamLabRuntimeStatus.Destroyed => "destroyed",
        _ => "unknown"
    };

    private static GuidTimeCursor? DecodeCursor(string? value, string code)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return GuidTimeCursor.Decode(value);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException(code, "分页游标无效。", 400);
        }
    }

    private sealed record RuntimeRow(
        Guid Id,
        Guid ReleaseId,
        Guid ControlScopeId,
        string? ExternalReference,
        int Generation,
        TeamLabExecutionModel ExecutionModel,
        TeamLabRuntimeStatus Status,
        bool OpenForAccess,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt);

    private sealed record AccessGrantRow(
        Guid Id,
        int Generation,
        TeamLabAccessGrantType Type,
        string ClientAddress,
        string Endpoint,
        string AllowedIps,
        string Dns,
        DateTimeOffset CreatedAt,
        DateTimeOffset? AppliedAt,
        DateTimeOffset? ExpiresAt,
        DateTimeOffset? ConfigurationConsumedAt);
}
