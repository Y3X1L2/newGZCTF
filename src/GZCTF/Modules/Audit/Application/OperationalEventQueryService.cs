using System.Text.Json;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Models;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Audit.Application;

public sealed class OperationalEventQueryService(AppDbContext context)
{
    public Task<OperationalEventViewPageModel> QueryAsync(
        OperationalEventQueryModel query,
        CancellationToken token) => QueryCoreAsync(query, recoveryOnly: false, token);

    public Task<OperationalEventViewPageModel> QueryRecoveryAsync(
        OperationalEventQueryModel query,
        CancellationToken token) => QueryCoreAsync(query, recoveryOnly: true, token);

    public async Task<OperationalCorrelationSummaryModel?> GetCorrelationAsync(
        Guid correlationId,
        CancellationToken token)
    {
        var facts = await context.OperationalEvents.AsNoTracking()
            .Where(item => item.CorrelationId == correlationId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                StartedAt = group.Min(item => item.OccurredAt),
                CompletedAt = group.Max(item => item.OccurredAt),
                HasFailure = group.Any(item => item.Outcome == OperationalEventOutcome.Failed)
            })
            .SingleOrDefaultAsync(token);
        if (facts is null)
            return null;

        var timeline = await QueryCoreAsync(new OperationalEventQueryModel
        {
            CorrelationId = correlationId,
            Count = 200
        }, recoveryOnly: false, token);
        var latest = timeline.Items.First();
        var failure = timeline.Items.FirstOrDefault(item => item.Event.Outcome == OperationalEventOutcome.Failed);
        var outcome = facts.HasFailure ? OperationalEventOutcome.Failed : latest.Event.Outcome;
        var chronologicalTimeline = new OperationalEventViewPageModel(
            timeline.Items
                .OrderBy(item => item.Event.OccurredAt)
                .ThenBy(item => item.Event.Id)
                .ToArray(),
            null);
        return new OperationalCorrelationSummaryModel(
            correlationId,
            facts.StartedAt,
            facts.CompletedAt,
            outcome,
            failure?.Event.ErrorCategory,
            failure?.Event.ErrorCode,
            facts.Count,
            timeline.Items.Select(item => item.Domain).Distinct(StringComparer.Ordinal).ToArray(),
            timeline.Items.Select(item => item.Labels.WorkerNode).OfType<string>()
                .Distinct(StringComparer.Ordinal).ToArray(),
            timeline.Items.Select(item => item.Labels.Subject).FirstOrDefault(item => item is not null),
            timeline.Items.Select(item => item.Labels.Resource).FirstOrDefault(item => item is not null),
            chronologicalTimeline);
    }

    private async Task<OperationalEventViewPageModel> QueryCoreAsync(
        OperationalEventQueryModel query,
        bool recoveryOnly,
        CancellationToken token)
    {
        var take = Math.Clamp(query.Count, 1, 200);
        IQueryable<OperationalEvent> data = context.OperationalEvents.AsNoTracking();
        if (query.CorrelationId is { } correlationId)
            data = data.Where(item => item.CorrelationId == correlationId);
        if (query.From is { } from)
            data = data.Where(item => item.OccurredAt >= from);
        if (query.To is { } to)
            data = data.Where(item => item.OccurredAt <= to);
        if (!string.IsNullOrWhiteSpace(query.Domain))
        {
            var prefix = NormalizeDomain(query.Domain);
            data = data.Where(item => item.EventCode.StartsWith(prefix));
        }
        if (recoveryOnly)
            data = data.Where(item => item.EventCode.StartsWith("recovery.") ||
                                      item.EventCode == OperationalEventCodes.Agent.InventoryUnavailable);
        if (!string.IsNullOrWhiteSpace(query.EventCode))
            data = data.Where(item => item.EventCode == query.EventCode.Trim());
        if (query.Outcome is { } outcome)
            data = data.Where(item => item.Outcome == outcome);
        if (query.ErrorCategory is { } errorCategory)
            data = data.Where(item => item.ErrorCategory == errorCategory);
        if (query.ActorUserId is { } actorUserId)
            data = data.Where(item => item.ActorUserId == actorUserId);
        if (query.OwnerUserId is { } ownerUserId)
            data = data.Where(item => item.OwnerUserId == ownerUserId);
        if (query.OwnerTeamId is { } ownerTeamId)
            data = data.Where(item => item.OwnerTeamId == ownerTeamId);
        if (query.GameId is { } gameId)
            data = data.Where(item => item.GameId == gameId);
        if (query.CourseId is { } courseId)
            data = data.Where(item => item.CourseId == courseId);
        if (query.ChallengeId is { } challengeId)
            data = data.Where(item => item.ChallengeId == challengeId);
        if (query.ImageTemplateId is { } imageTemplateId)
            data = data.Where(item => item.ImageTemplateId == imageTemplateId);
        if (query.WorkerNodeId is { } workerNodeId)
            data = data.Where(item => item.WorkerNodeId == workerNodeId);
        if (query.DeploymentTicketId is { } deploymentTicketId)
            data = data.Where(item => item.DeploymentTicketId == deploymentTicketId);
        if (query.TeamLabRuntimeId is { } teamLabRuntimeId)
            data = data.Where(item => item.TeamLabRuntimeId == teamLabRuntimeId);
        if (query.VmInstanceId is { } vmInstanceId)
            data = data.Where(item => item.VmInstanceId == vmInstanceId);
        if (!string.IsNullOrWhiteSpace(query.SubjectType))
            data = data.Where(item => item.SubjectType == query.SubjectType.Trim());
        if (!string.IsNullOrWhiteSpace(query.SubjectId))
            data = data.Where(item => item.SubjectId == query.SubjectId.Trim());
        if (!string.IsNullOrWhiteSpace(query.ResourceType))
            data = data.Where(item => item.ResourceType == query.ResourceType.Trim());
        if (!string.IsNullOrWhiteSpace(query.ResourceId))
            data = data.Where(item => item.ResourceId == query.ResourceId.Trim());
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var decoded = TimeCursor.Decode(query.Cursor);
            data = data.Where(item => item.OccurredAt < decoded.Time ||
                                      item.OccurredAt == decoded.Time && item.Id < decoded.Id);
        }

        var rows = await data.OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id)
            .Take(take + 1).ToArrayAsync(token);
        var page = rows.Take(take).ToArray();
        var labels = await LoadLabelsAsync(page, token);
        var items = page.Select(item => new OperationalEventViewModel(
            ToModel(item),
            Domain(item.EventCode),
            labels.For(item))).ToArray();
        var next = rows.Length > take && page.Length > 0
            ? new TimeCursor(page[^1].OccurredAt, page[^1].Id).Encode()
            : null;
        return new OperationalEventViewPageModel(items, next);
    }

    private async Task<EventLabelLookup> LoadLabelsAsync(
        IReadOnlyCollection<OperationalEvent> rows,
        CancellationToken token)
    {
        var userIds = rows.SelectMany(item => new[] { item.ActorUserId, item.OwnerUserId })
            .OfType<Guid>().Distinct().ToArray();
        var teamIds = rows.Select(item => item.OwnerTeamId).OfType<int>().Distinct().ToArray();
        var gameIds = rows.Select(item => item.GameId).OfType<int>().Distinct().ToArray();
        var courseIds = rows.Select(item => item.CourseId).OfType<int>().Distinct().ToArray();
        var challengeIds = rows.Select(item => item.ChallengeId).OfType<int>().Distinct().ToArray();
        var templateIds = rows.Select(item => item.ImageTemplateId).OfType<int>().Distinct().ToArray();
        var nodeIds = rows.Select(item => item.WorkerNodeId).OfType<Guid>().Distinct().ToArray();
        var ticketIds = rows.Select(item => item.DeploymentTicketId).OfType<Guid>().Distinct().ToArray();
        var runtimeIds = rows.Select(item => item.TeamLabRuntimeId).OfType<int>().Distinct().ToArray();
        var vmIds = rows.Select(item => item.VmInstanceId).OfType<Guid>().Distinct().ToArray();

        var users = await context.Users.AsNoTracking().Where(item => userIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.UserName ?? item.Id.ToString(), token);
        var teams = await context.Teams.AsNoTracking().Where(item => teamIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var games = await context.Games.AsNoTracking().Where(item => gameIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Title, token);
        var courses = await context.TrainingCourses.AsNoTracking().Where(item => courseIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Title, token);
        var gameChallenges = await context.GameChallenges.AsNoTracking()
            .Where(item => challengeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Title, token);
        var exerciseChallenges = await context.ExerciseChallenges.AsNoTracking()
            .Where(item => challengeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Title, token);
        var templates = await context.ImageTemplates.AsNoTracking().Where(item => templateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var nodes = await context.WorkerNodes.AsNoTracking().Where(item => nodeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var tickets = await context.DeploymentQueueTickets.AsNoTracking().Where(item => ticketIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id,
                item => item.SubjectDisplayName ?? item.ResourceDisplayName ?? item.Id.ToString(), token);
        var runtimes = await context.TeamLabRuntimes.AsNoTracking().Where(item => runtimeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id,
                item => item.ExternalReference ?? item.PublicId.ToString(), token);
        var vms = await context.VmInstances.AsNoTracking().Where(item => vmIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.VmName, token);
        return new EventLabelLookup(users, teams, games, courses, gameChallenges, exerciseChallenges,
            templates, nodes, tickets, runtimes, vms);
    }

    private static OperationalEventModel ToModel(OperationalEvent item) => new(
        item.Id,
        item.OccurredAt,
        item.CorrelationId,
        item.TraceId,
        item.EventCode,
        item.Severity,
        item.Outcome,
        item.ErrorCategory,
        item.ErrorCode,
        item.Retryable,
        item.Message,
        ParseDetail(item.DetailJson),
        item.ActorUserId,
        item.OwnerUserId,
        item.OwnerTeamId,
        item.GameId,
        item.CourseId,
        item.ChallengeId,
        item.ImageTemplateId,
        item.WorkerNodeId,
        item.DeploymentTicketId,
        item.TeamLabRuntimeId,
        item.VmInstanceId,
        item.SubjectType,
        item.SubjectId,
        item.SubjectDisplayName,
        item.ResourceType,
        item.ResourceId,
        item.ResourceDisplayName);

    private static IReadOnlyDictionary<string, object?>? ParseDetail(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => ToValue(property.Value),
            StringComparer.Ordinal);
    }

    private static object? ToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var number) => number,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };

    private static string NormalizeDomain(string value)
    {
        var domain = value.Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.Length is < 1 or > 64 || domain.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Operational event domain is invalid.", nameof(value));
        return $"{domain}.";
    }

    private static string Domain(string eventCode)
    {
        var separator = eventCode.IndexOf('.');
        return separator > 0 ? eventCode[..separator] : eventCode;
    }

    private sealed record EventLabelLookup(
        IReadOnlyDictionary<Guid, string> Users,
        IReadOnlyDictionary<int, string> Teams,
        IReadOnlyDictionary<int, string> Games,
        IReadOnlyDictionary<int, string> Courses,
        IReadOnlyDictionary<int, string> GameChallenges,
        IReadOnlyDictionary<int, string> ExerciseChallenges,
        IReadOnlyDictionary<int, string> Templates,
        IReadOnlyDictionary<Guid, string> Nodes,
        IReadOnlyDictionary<Guid, string> Tickets,
        IReadOnlyDictionary<int, string> Runtimes,
        IReadOnlyDictionary<Guid, string> Vms)
    {
        public OperationalEventLabels For(OperationalEvent item)
        {
            var challenge = ResolveChallenge(item);
            return new OperationalEventLabels(
                item.ActorUserId is { } actorId ? Users.GetValueOrDefault(actorId) : null,
                item.OwnerUserId is { } ownerId ? Users.GetValueOrDefault(ownerId) : null,
                item.OwnerTeamId is { } teamId ? Teams.GetValueOrDefault(teamId) : null,
                item.GameId is { } gameId ? Games.GetValueOrDefault(gameId) : null,
                item.CourseId is { } courseId ? Courses.GetValueOrDefault(courseId) : null,
                challenge,
                item.ImageTemplateId is { } templateId ? Templates.GetValueOrDefault(templateId) : null,
                item.WorkerNodeId is { } nodeId ? Nodes.GetValueOrDefault(nodeId) : null,
                item.DeploymentTicketId is { } ticketId ? Tickets.GetValueOrDefault(ticketId) : null,
                item.TeamLabRuntimeId is { } runtimeId ? Runtimes.GetValueOrDefault(runtimeId) : null,
                item.VmInstanceId is { } vmId ? Vms.GetValueOrDefault(vmId) : null,
                item.SubjectDisplayName ?? item.SubjectId,
                item.ResourceDisplayName ?? item.ResourceId);
        }

        private string? ResolveChallenge(OperationalEvent item)
        {
            if (item.ChallengeId is not { } challengeId)
                return null;
            if (item.GameId is not null ||
                item.SubjectType is "game-container" or "challenge-test-container")
                return GameChallenges.GetValueOrDefault(challengeId);
            if (item.CourseId is not null ||
                item.SubjectType is "exercise-container" or "training-container" or "exercise-challenge")
                return ExerciseChallenges.GetValueOrDefault(challengeId);
            return GameChallenges.GetValueOrDefault(challengeId) ??
                   ExerciseChallenges.GetValueOrDefault(challengeId);
        }
    }
}
