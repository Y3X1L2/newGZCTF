using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeProjectionService(AppDbContext context)
{
    public async Task<TeamLabRuntimeProjectionModel> GetAsync(Guid runtimePublicId, CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Include(item => item.Shards)
            .ThenInclude(item => item.WorkerNode)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.PublicId == runtimePublicId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        var releaseId = runtime.TopologyReleaseId;
        if (releaseId == Guid.Empty)
            throw new TeamLabApiContractException("runtime_invalid", "TeamLab 运行时未关联拓扑版本", 500);
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.Id == releaseId)
            .Select(item => new { item.Version, item.ControlScopeId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到运行时关联的发布版本", 500);
        var ticket = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(item => item.TeamLabRuntimeId == runtime.Id && item.Generation == runtime.Generation)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var currentOperationId = ticket?.ApiOperationId;
        if (currentOperationId is null)
        {
            currentOperationId = await context.ApiOperations.AsNoTracking()
                .Where(item => item.ResourceType == "teamlab-runtime" &&
                               item.ResourceId == runtime.PublicId.ToString("D"))
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        var runtimeFailure = TeamLabFailurePresentation.ForRuntime(runtime.Status, ticket, runtime.PublicId);
        var subStages = ticket is null
            ? [new TeamLabRuntimeSubStageProjectionModel(Stage(runtime.Status), RuntimeStatus(runtime.Status), null)]
            : new[]
            {
                new TeamLabRuntimeSubStageProjectionModel(
                    TeamLabFailurePresentation.Stage(ticket.Stage),
                    QueueStatus(ticket.Status),
                    SafeStageMessage(ticket.StageMessage))
            };
        return new TeamLabRuntimeProjectionModel(
            runtime.PublicId,
            releaseId,
            runtime.Generation,
            runtime.ExecutionModel,
            runtime.Status,
            Stage(runtime.Status),
            runtime.IsOpenToPlayers,
            runtime.Shards.Where(item => item.Generation == runtime.Generation)
                .OrderBy(item => item.PublicId)
                .Select(shard => new TeamLabRuntimeShardProjectionModel(
                    shard.PublicId,
                    shard.WorkerNodeId,
                    shard.WorkerNode.Name,
                    shard.Status,
                    runtime.Networks.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
                        .Select(item => item.TopologyKey).Order(StringComparer.Ordinal).ToArray(),
                    runtime.Assets.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
                        .Select(item => item.TopologyKey).Order(StringComparer.Ordinal).ToArray(),
                    shard.LastError,
                    TeamLabFailurePresentation.ForResource(
                        shard.Status, "shard", "shard_deployment_failed",
                        "shard", shard.PublicId.ToString("D")))).ToArray(),
            runtime.Networks.Where(item => item.Generation == runtime.Generation)
                .OrderBy(item => item.TopologyKey, StringComparer.Ordinal)
                .Select(item => new TeamLabRuntimeNetworkProjectionModel(
                    item.TopologyKey, item.Name, item.Cidr, item.GatewayIp)).ToArray(),
            runtime.Assets.Where(item => item.Generation == runtime.Generation &&
                                         item.Kind is TeamLabResourceKind.Docker or TeamLabResourceKind.Vm)
                .OrderBy(item => item.TopologyKey, StringComparer.Ordinal)
                .Select(item => new TeamLabRuntimeAssetProjectionModel(
                    item.Id,
                    item.TopologyKey,
                    item.Name,
                    item.Kind == TeamLabResourceKind.Docker ? TeamLabAssetKind.Docker : TeamLabAssetKind.Vm,
                    item.RuntimeResourceId,
                    item.IpAddress,
                    EffectiveAssetStatus(runtime.Status, item),
                    item.LastError,
                    TeamLabFailurePresentation.ForResource(
                        item.Status, "asset", "asset_deployment_failed",
                        "asset", item.TopologyKey))).ToArray(),
            runtime.CreatedAt,
            runtime.UpdatedAt,
            runtime.LastError,
            currentOperationId,
            ticket?.Id,
            ticket?.Status,
            subStages,
            runtime.ControlScopeId ?? release.ControlScopeId,
            release.Version,
            TeamLabFailurePresentation.RecoveryActions(runtime.Status, runtimeFailure),
            runtimeFailure);
    }

    private static TeamLabRuntimeStatus EffectiveAssetStatus(
        TeamLabRuntimeStatus runtimeStatus,
        TeamLabRuntimeAsset asset) =>
        runtimeStatus == TeamLabRuntimeStatus.Running &&
        asset.Status != TeamLabRuntimeStatus.Failed &&
        !string.IsNullOrWhiteSpace(asset.RuntimeResourceId)
            ? TeamLabRuntimeStatus.Running
            : asset.Status;

    public async Task<IReadOnlyList<TeamLabRuntimeEventModel>> GetEventsAsync(
        Guid runtimePublicId,
        long after,
        int limit,
        int? generation,
        string? stage,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimePublicId)
            .Select(item => new { item.Id, item.Generation })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时。", 404);
        var take = Math.Clamp(limit, 1, 200);
        var query = context.TeamLabEvents.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id &&
                           item.Generation == (generation ?? runtime.Generation) && item.Id > after);
        if (!string.IsNullOrWhiteSpace(stage))
        {
            var normalizedStage = stage.Trim();
            query = query.Where(item => item.Stage == normalizedStage);
        }
        return await query
            .OrderBy(item => item.Id)
            .Take(take)
            .Select(item => new TeamLabRuntimeEventModel(
                item.Id, item.Generation, item.Stage, item.Level, item.Message,
                item.ObjectType, item.ObjectId, item.CreatedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<OpenTeamLabRuntimeEventPageModel> GetEventsAsync(
        Guid runtimePublicId,
        string? after,
        int limit,
        int? generation,
        string? stage,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(item => item.PublicId == runtimePublicId)
            .Select(item => new { item.Id, item.Generation })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时。", 404);
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        var cursor = DecodeEventCursor(after);
        var query = context.TeamLabEvents.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id &&
                           item.Generation == (generation ?? runtime.Generation));
        if (cursor is { } value)
            query = query.Where(item => item.CreatedAt > value.Time ||
                                        item.CreatedAt == value.Time && item.Id > value.Id);
        if (!string.IsNullOrWhiteSpace(stage))
        {
            var normalizedStage = stage.Trim();
            query = query.Where(item => item.Stage == normalizedStage);
        }
        var rows = await query
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(normalizedLimit + 1)
            .Select(item => new TeamLabRuntimeEventModel(
                item.Id, item.Generation, item.Stage, item.Level, item.Message,
                item.ObjectType, item.ObjectId, item.CreatedAt))
            .ToArrayAsync(cancellationToken);
        var page = rows.Take(normalizedLimit).ToArray();
        var nextCursor = rows.Length > normalizedLimit
            ? new TimeCursor(page[^1].CreatedAt, page[^1].Cursor).Encode()
            : null;
        return new OpenTeamLabRuntimeEventPageModel(page, nextCursor);
    }

    private static TimeCursor? DecodeEventCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return TimeCursor.Decode(value);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException(
                "runtime_event_cursor_invalid", "分页游标无效", 400);
        }
    }

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
        TeamLabRuntimeStatus.Destroying => "destroying",
        TeamLabRuntimeStatus.Destroyed => "destroyed",
        TeamLabRuntimeStatus.Paused => "paused",
        _ => "unknown"
    };

    private static string RuntimeStatus(TeamLabRuntimeStatus status) => status switch
    {
        TeamLabRuntimeStatus.Running or TeamLabRuntimeStatus.Destroyed => "succeeded",
        TeamLabRuntimeStatus.Failed or TeamLabRuntimeStatus.CleanupPending => "failed",
        TeamLabRuntimeStatus.Pending or TeamLabRuntimeStatus.Paused => "waiting",
        _ => "running"
    };

    private static string QueueStatus(DeploymentQueueTicketStatus status) => status switch
    {
        DeploymentQueueTicketStatus.Pending or DeploymentQueueTicketStatus.Scheduling or
            DeploymentQueueTicketStatus.Scheduled => "waiting",
        DeploymentQueueTicketStatus.Running => "running",
        DeploymentQueueTicketStatus.Succeeded => "succeeded",
        DeploymentQueueTicketStatus.Failed => "failed",
        DeploymentQueueTicketStatus.Cancelled => "cancelled",
        _ => "unknown"
    };

    private static string? SafeStageMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var normalized = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 256 ? normalized : normalized[..256];
    }
}
