using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Repositories.Interface;
using GZCTF.Infrastructure.Cache;
using GZCTF.Infrastructure.Concurrency;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Penetration.Application;

public sealed class PenetrationObjectiveService(
    AppDbContext context,
    IPlatformCache cache,
    IDistributedLeaseProvider locks,
    IGameEventRepository events,
    IHubContext<UserHub, IUserClient> hub,
    ILogger<PenetrationObjectiveService> logger)
{
    private static readonly TimeSpan SubmitRateWindow = TimeSpan.FromMinutes(1);
    private const int SubmitRateLimit = 5;

    public async Task<IReadOnlyList<PenetrationObjectiveModel>> ListAsync(
        int gameId,
        CancellationToken cancellationToken) =>
        (await context.PenetrationObjectives.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken))
        .Select(ToModel)
        .ToArray();

    public async Task<IReadOnlyList<PenetrationObjectiveModel>> ReplaceAsync(
        int gameId,
        ReplacePenetrationObjectivesModel model,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationGameLabBindings
            .SingleOrDefaultAsync(item => item.GameId == gameId, cancellationToken)
            ?? throw new InvalidOperationException("The game has no TeamLab topology binding.");
        var assetKeys = await context.TeamLabTopologyAssets.AsNoTracking()
            .Where(item => item.TopologyId == binding.TopologyId)
            .Select(item => item.Key)
            .ToHashSetAsync(cancellationToken);
        Validate(model.Objectives, assetKeys);

        var existing = await context.PenetrationObjectives
            .Where(item => item.GameId == gameId)
            .ToArrayAsync(cancellationToken);
        context.PenetrationObjectives.RemoveRange(existing);
        context.PenetrationObjectives.AddRange(model.Objectives.Select(item => new PenetrationObjective
        {
            GameId = gameId,
            Key = item.Key.Trim(),
            TopologyAssetKey = item.AssetKey.Trim(),
            Title = item.Title.Trim(),
            Description = Clean(item.Description),
            Category = string.IsNullOrWhiteSpace(item.Category) ? "General" : item.Category.Trim(),
            Score = item.Score,
            IsDynamic = item.Dynamic,
            StaticFlag = item.Dynamic ? null : Clean(item.StaticFlag),
            FlagTemplate = item.Dynamic ? Clean(item.FlagTemplate) : null,
            MaxAttempts = item.MaxAttempts,
            IsVisible = item.Visible,
            IsCheckpoint = item.Checkpoint,
            PrerequisiteObjectiveKeysJson = JsonSerializer.Serialize(item.PrerequisiteKeys ?? []),
            OrderIndex = item.OrderIndex
        }));
        binding.MaxResetCount = Math.Clamp(model.MaxResetCount, 0, 100);
        binding.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return await ListAsync(gameId, cancellationToken);
    }

    public async Task<IReadOnlyList<TeamLabRuntimeOverlayModel>> BuildOverlaysAsync(
        int gameId,
        int teamId,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var version = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.Id == releaseId)
            .Select(item => item.Version)
            .SingleAsync(cancellationToken);
        var objectives = await context.PenetrationObjectives.AsNoTracking()
            .Where(item => item.GameId == gameId)
            .OrderBy(item => item.OrderIndex)
            .ToArrayAsync(cancellationToken);
        return objectives.GroupBy(item => item.TopologyAssetKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var values = group.ToDictionary(
                    item => $"GZCTF_FLAG_{NormalizeEnvironmentKey(item.Key)}",
                    item => BuildFlag(item, gameId, teamId, version),
                    StringComparer.Ordinal);
                if (values.Count > 0) values["GZCTF_FLAG"] = values.Values.First();
                return new TeamLabRuntimeOverlayModel(group.Key, null, values);
            }).ToArray();
    }

    public async Task<PenetrationSubmitResultModel> SubmitAsync(
        int gameId,
        int teamId,
        int participationId,
        Guid userId,
        PenetrationSubmitModel model,
        CancellationToken cancellationToken)
    {
        var binding = await context.PenetrationTeamRuntimeBindings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.GameId == gameId && item.TeamId == teamId, cancellationToken);
        if (binding is null)
            return new(false, 0, "本队渗透环境尚未创建。");
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == binding.RuntimeId, cancellationToken);
        if (runtime is null || runtime.Status != TeamLabRuntimeStatus.Running || runtime.TopologyReleaseId == Guid.Empty)
            return new(false, 0, "本队渗透环境尚未运行。");
        var objective = await context.PenetrationObjectives.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == model.ObjectiveId && item.GameId == gameId, cancellationToken);
        if (objective is null || !objective.IsVisible)
            return new(false, 0, "得分目标不存在或不可提交。");

        var releaseVersion = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.Id == runtime.TopologyReleaseId)
            .Select(item => item.Version)
            .SingleAsync(cancellationToken);
        await using var submissionLease = await locks.AcquireAsync(
            $"penetration:submit:{gameId}:{teamId}:{objective.Id}",
            TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, submissionLease.LeaseLost);
        cancellationToken = leaseCancellation.Token;
        {
            var alreadySolved = await context.PenetrationSubmissions.AnyAsync(item =>
                item.GameId == gameId && item.TeamId == teamId && item.ObjectiveId == objective.Id &&
                item.Status == AnswerResult.Accepted, cancellationToken);
            if (alreadySolved) return new(false, 0, "该得分目标已完成。");

            var prerequisiteKeys = DeserializeKeys(objective.PrerequisiteObjectiveKeysJson);
            if (prerequisiteKeys.Count > 0)
            {
                var prerequisiteIds = await context.PenetrationObjectives.AsNoTracking()
                    .Where(item => item.GameId == gameId && prerequisiteKeys.Contains(item.Key))
                    .Select(item => item.Id)
                    .ToArrayAsync(cancellationToken);
                var solvedCount = await context.PenetrationSubmissions.AsNoTracking()
                    .Where(item => item.GameId == gameId && item.TeamId == teamId &&
                                   item.Status == AnswerResult.Accepted && prerequisiteIds.Contains(item.ObjectiveId))
                    .Select(item => item.ObjectiveId)
                    .Distinct()
                    .CountAsync(cancellationToken);
                if (solvedCount != prerequisiteIds.Length || prerequisiteIds.Length != prerequisiteKeys.Count)
                    return new(false, 0, "请先完成前置得分目标。");
            }

            var attempts = await context.PenetrationSubmissions.CountAsync(item =>
                item.GameId == gameId && item.TeamId == teamId && item.ObjectiveId == objective.Id,
                cancellationToken);
            if (objective.MaxAttempts > 0 && attempts >= objective.MaxAttempts)
                return new(false, 0, "提交次数已达到上限。");
            var recent = await context.PenetrationSubmissions.CountAsync(item =>
                item.GameId == gameId && item.TeamId == teamId && item.ObjectiveId == objective.Id &&
                item.SubmittedAt >= DateTimeOffset.UtcNow - SubmitRateWindow, cancellationToken);
            if (recent >= SubmitRateLimit) return new(false, 0, "提交过于频繁，请稍后再试。");

            var accepted = string.Equals(
                model.Flag.Trim(), BuildFlag(objective, gameId, teamId, releaseVersion), StringComparison.Ordinal);
            var submission = new PenetrationSubmission
            {
                GameId = gameId,
                TeamId = teamId,
                ParticipationId = participationId,
                UserId = userId,
                ObjectiveId = objective.Id,
                Answer = model.Flag.Trim(),
                Status = accepted ? AnswerResult.Accepted : AnswerResult.WrongAnswer,
                Score = accepted ? objective.Score : 0,
                SubmittedAt = DateTimeOffset.UtcNow
            };
            context.PenetrationSubmissions.Add(submission);
            await context.SaveChangesAsync(cancellationToken);
            if (accepted)
                await cache.InvalidateAsync(CachePolicyCatalog.Scoreboard, gameId.ToString(), cancellationToken);
            await PublishSideEffectsAsync(submission, objective, runtime.PublicId, cancellationToken);
            return new(accepted, submission.Score, accepted ? "Flag 正确。" : "Flag 错误。");
        }
    }

    public async Task<IReadOnlyList<PenetrationScoreboardItemModel>> GetScoreboardAsync(
        int gameId,
        CancellationToken cancellationToken)
    {
        var teams = await context.Participations.AsNoTracking()
            .Where(item => item.GameId == gameId && item.Status == ParticipationStatus.Accepted)
            .Select(item => new { item.TeamId, item.Team.Name })
            .ToArrayAsync(cancellationToken);
        var solves = await context.PenetrationSubmissions.AsNoTracking()
            .Where(item => item.GameId == gameId && item.Status == AnswerResult.Accepted)
            .GroupBy(item => new { item.TeamId, item.ObjectiveId })
            .Select(group => group.OrderBy(item => item.SubmittedAt).Select(item => new
            {
                item.TeamId, item.ObjectiveId, item.Score, item.SubmittedAt
            }).First())
            .ToArrayAsync(cancellationToken);
        var rows = teams.Select(team =>
        {
            var teamSolves = solves.Where(item => item.TeamId == team.TeamId).ToArray();
            return new PenetrationScoreboardItemModel(0, team.TeamId, team.Name,
                teamSolves.Sum(item => item.Score), teamSolves.Length,
                teamSolves.Length == 0 ? DateTimeOffset.MinValue : teamSolves.Max(item => item.SubmittedAt));
        }).OrderByDescending(item => item.Score)
            .ThenBy(item => item.LastSubmissionTime == DateTimeOffset.MinValue ? DateTimeOffset.MaxValue : item.LastSubmissionTime)
            .ThenBy(item => item.TeamName, StringComparer.Ordinal)
            .Select((item, index) => item with { Rank = index + 1 })
            .ToArray();
        return rows;
    }

    public async Task<PenetrationSubmissionPageModel> GetSubmissionLogsAsync(
        int gameId,
        int count,
        int skip,
        CancellationToken cancellationToken)
    {
        var query = context.PenetrationSubmissions.AsNoTracking().Where(item => item.GameId == gameId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(item => item.SubmittedAt)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(count, 1, 100))
            .Select(item => new PenetrationSubmissionLogModel(
                item.Id, item.SubmittedAt, item.TeamId, item.Team.Name, item.User.UserName ?? string.Empty,
                item.Objective.TopologyAssetKey, item.Objective.Title, item.Objective.Category, item.Score, item.Status))
            .ToArrayAsync(cancellationToken);
        return new(rows, total);
    }

    internal static string BuildFlag(PenetrationObjective objective, int gameId, int teamId, int releaseVersion)
    {
        if (!objective.IsDynamic) return objective.StaticFlag ?? string.Empty;
        var material = $"{gameId}:{teamId}:{objective.TopologyAssetKey}:{objective.Key}:{releaseVersion}";
        var token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()[..16];
        var template = string.IsNullOrWhiteSpace(objective.FlagTemplate) ? "flag{[TEAM_HASH]}" : objective.FlagTemplate;
        return template.Replace("[TEAM_HASH]", token, StringComparison.OrdinalIgnoreCase)
            .Replace("[TOKEN]", token, StringComparison.OrdinalIgnoreCase);
    }

    private async Task PublishSideEffectsAsync(
        PenetrationSubmission submission,
        PenetrationObjective objective,
        Guid runtimeId,
        CancellationToken cancellationToken)
    {
        try
        {
            await events.AddEvent(new GameEvent
            {
                TeamId = submission.TeamId,
                UserId = submission.UserId,
                GameId = submission.GameId,
                Type = EventType.FlagSubmit,
                Values = [submission.Status.ToString(), submission.Answer, $"[渗透] {objective.Title}", objective.Id.ToString()]
            }, cancellationToken);
            await hub.Clients.Group(UserHub.PenetrationTeamGroupName(submission.GameId, submission.TeamId))
                .ReceivedPenetrationWorkspaceUpdate(new PenetrationWorkspaceUpdateModel(
                    submission.GameId, submission.TeamId, runtimeId, DateTimeOffset.UtcNow));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to publish penetration submission side effects.");
        }
    }

    private static PenetrationObjectiveModel ToModel(PenetrationObjective item) => new(
        item.Id, item.Key, item.TopologyAssetKey, item.Title, item.Description, item.Category, item.Score,
        item.IsDynamic, item.MaxAttempts, item.IsVisible, item.IsCheckpoint,
        DeserializeKeys(item.PrerequisiteObjectiveKeysJson), item.OrderIndex);

    private static void Validate(IReadOnlyList<PenetrationObjectiveWriteModel> objectives, IReadOnlySet<string> assetKeys)
    {
        if (objectives.Count > 256) throw new InvalidOperationException("A game cannot contain more than 256 objectives.");
        var keys = objectives.Select(item => item.Key.Trim()).ToHashSet(StringComparer.Ordinal);
        if (keys.Count != objectives.Count || keys.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Objective keys must be non-empty and unique.");
        foreach (var item in objectives)
        {
            if (!assetKeys.Contains(item.AssetKey.Trim())) throw new InvalidOperationException($"Asset '{item.AssetKey}' does not exist.");
            if (string.IsNullOrWhiteSpace(item.Title) || item.Score < 0 || item.MaxAttempts < 0)
                throw new InvalidOperationException($"Objective '{item.Key}' has invalid title, score, or attempt limit.");
            if (!item.Dynamic && string.IsNullOrWhiteSpace(item.StaticFlag))
                throw new InvalidOperationException($"Objective '{item.Key}' requires a static flag.");
            if ((item.PrerequisiteKeys ?? []).Any(key => !keys.Contains(key) || key == item.Key))
                throw new InvalidOperationException($"Objective '{item.Key}' has an invalid prerequisite.");
        }
    }

    internal static IReadOnlyList<string> DeserializeKeys(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string NormalizeEnvironmentKey(string key) =>
        new(key.Trim().ToUpperInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
