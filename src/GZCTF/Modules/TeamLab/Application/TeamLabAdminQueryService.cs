using System.Text.Json;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabAdminQueryService(
    AppDbContext context,
    ITeamLabTopologyApplicationService topologies,
    NodeCapacitySnapshotService capacitySnapshots,
    ITeamLabUsageProjectionProvider usage)
{
    public async Task<TeamLabAdminScenePageModel> ListScenesAsync(
        Guid actorUserId,
        bool administrator,
        string? search,
        string? owner,
        Guid? ownerId,
        string? status,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, 100);
        var cursor = DecodeCursor(after, "teamlab_scene_cursor_invalid");
        var query = context.TeamLabTopologies.AsNoTracking();
        if (!administrator || string.Equals(owner, "mine", StringComparison.OrdinalIgnoreCase))
            query = query.Where(item => item.OwnerUserId == actorUserId);
        else if (ownerId is { } requestedOwner)
            query = query.Where(item => item.OwnerUserId == requestedOwner);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item => EF.Functions.ILike(item.Name, $"%{term}%"));
        }

        query = ApplyStatusFilter(query, status, await usage.GetGameBoundRuntimeIdsAsync(cancellationToken));
        if (cursor is { } value)
            query = query.Where(item => item.UpdatedAt < value.Time ||
                                        item.UpdatedAt == value.Time && item.PublicId.CompareTo(value.Id) < 0);

        var rows = await query
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.PublicId)
            .Take(take + 1)
            .Select(item => new SceneRow(
                item.Id,
                item.PublicId,
                item.Name,
                item.OwnerUserId,
                item.Revision,
                item.SchemaVersion,
                item.Networks.Count,
                item.Assets.Count,
                item.InfrastructureJson,
                item.CreatedAt,
                item.UpdatedAt))
            .ToArrayAsync(cancellationToken);
        var page = rows.Take(take).ToArray();
        if (page.Length == 0)
            return new TeamLabAdminScenePageModel([], null);

        var topologyIds = page.Select(item => item.Id).ToArray();
        var ownerIds = page.Where(item => item.OwnerId.HasValue).Select(item => item.OwnerId!.Value).Distinct().ToArray();
        var owners = await context.Users.AsNoTracking()
            .Where(item => ownerIds.Contains(item.Id))
            .Select(item => new { item.Id, item.UserName, item.RealName })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var latestReleases = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => topologyIds.Contains(item.TopologyId))
            .GroupBy(item => item.TopologyId)
            .Select(group => group.OrderByDescending(item => item.Version).First())
            .ToDictionaryAsync(item => item.TopologyId, cancellationToken);
        var gameReferences = await usage.GetGameBindingCountsAsync(topologyIds, cancellationToken);
        var gameBoundRuntimeIds = await usage.GetGameBoundRuntimeIdsAsync(cancellationToken);
        var trialRuntimes = await (
            from runtime in context.TeamLabRuntimes.AsNoTracking()
            join release in context.TeamLabTopologyReleases.AsNoTracking()
                on runtime.TopologyReleaseId equals release.Id
            where topologyIds.Contains(release.TopologyId) &&
                  !gameBoundRuntimeIds.Contains(runtime.Id)
            group runtime by release.TopologyId
            into runtimes
            select runtimes.OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.PublicId)
                .Select(item => new TrialRuntimeRow(
                    runtimes.Key,
                    item.PublicId,
                    item.TopologyReleaseId,
                    item.Status,
                    item.IsOpenToPlayers,
                    item.CreatedAt,
                    item.UpdatedAt,
                    item.LastError))
                .First())
            .ToDictionaryAsync(item => item.TopologyId, cancellationToken);

        var items = page.Select(row =>
        {
            latestReleases.TryGetValue(row.Id, out var release);
            trialRuntimes.TryGetValue(row.Id, out var runtime);
            var ownerDisplay = row.OwnerId is { } id && owners.TryGetValue(id, out var user)
                ? DisplayName(user.RealName, user.UserName)
                : "未指定";
            return new TeamLabAdminSceneSummaryModel(
                row.PublicId,
                row.Name,
                row.OwnerId,
                ownerDisplay,
                row.Revision,
                row.SchemaVersion,
                row.NetworkCount,
                row.AssetCount,
                CountInfrastructure(row.InfrastructureJson),
                release is null ? null : ToRelease(release),
                release is not null && release.SourceRevision == row.Revision
                    ? new TeamLabAdminValidationSummaryModel(
                        row.Revision, true, 0, release.PublishedAt)
                    : null,
                runtime is null ? null : ToRuntime(runtime),
                gameReferences.GetValueOrDefault(row.Id),
                row.CreatedAt,
                row.UpdatedAt);
        }).ToArray();
        var next = rows.Length > take
            ? new GuidTimeCursor(items[^1].UpdatedAt, items[^1].Id).Encode()
            : null;
        return new TeamLabAdminScenePageModel(items, next);
    }

    public async Task<TeamLabAdminReleaseReadinessModel> GetReleaseReadinessAsync(
        Guid topologyId,
        Guid releaseId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .Include(item => item.Topology)
            .SingleOrDefaultAsync(item => item.Id == releaseId && item.Topology.PublicId == topologyId &&
                                          (administrator || item.Topology.OwnerUserId == actorUserId),
                cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);
        var execution = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        TeamLabPlanModel? plan = null;
        string? planningBlocker = null;
        try
        {
            plan = await topologies.PlanAsync(
                topologyId, releaseId, actorUserId, administrator, cancellationToken);
        }
        catch (TeamLabApiContractException exception) when (exception.Code == "capability_unavailable")
        {
            planningBlocker = DescribePlanningBlocker(execution, await LoadPlanningNodesAsync(cancellationToken));
        }
        var requirements = execution.Assets
            .GroupBy(item => item.ImageTemplateId)
            .Select(group => new
            {
                Id = group.Key,
                Kind = group.First().Kind,
                Digest = group.Select(item => item.ImageDigest).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            })
            .ToArray();
        var templateIds = requirements.Select(item => item.Id).ToArray();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(item => templateIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Name, item.ImageType, item.ImageHash })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(item => item.IsSchedulable && item.TeamLabNetworkEnabled &&
                           item.TeamLabTunnelStatus == TeamLabTunnelStatus.Healthy)
            .Select(item => new { item.Id, item.Capabilities })
            .ToArrayAsync(cancellationToken);
        var records = await context.ImageDistributionRecords.AsNoTracking()
            .Where(item => templateIds.Contains(item.ImageTemplateId))
            .Select(item => new { item.ImageTemplateId, item.WorkerNodeId, item.ImageHash, item.Status })
            .ToArrayAsync(cancellationToken);

        var images = requirements.Select(requirement =>
        {
            templates.TryGetValue(requirement.Id, out var template);
            var digest = requirement.Digest ?? template?.ImageHash ?? string.Empty;
            var requiredCapability = requirement.Kind == TeamLabAssetKind.Docker
                ? NodeCapability.Docker
                : NodeCapability.Kvm;
            var eligible = nodes.Where(item => (item.Capabilities & requiredCapability) != 0)
                .Select(item => item.Id)
                .ToHashSet();
            var matching = records.Where(item => item.ImageTemplateId == requirement.Id &&
                                                 eligible.Contains(item.WorkerNodeId) &&
                                                 string.Equals(item.ImageHash, digest, StringComparison.Ordinal))
                .ToArray();
            return new TeamLabAdminImageReadinessModel(
                requirement.Id,
                template?.Name ?? $"模板 {requirement.Id}",
                template?.ImageType ?? (requirement.Kind == TeamLabAssetKind.Docker ? ImageType.Docker : ImageType.Qcow2),
                eligible.Count,
                matching.Count(item => item.Status == ImageDistributionStatus.Ready),
                matching.Count(item => item.Status is ImageDistributionStatus.Pending or ImageDistributionStatus.Pulling),
                matching.Count(item => item.Status == ImageDistributionStatus.Failed));
        }).OrderBy(item => item.ImageTemplateId).ToArray();
        var gameBoundRuntimeIds = await usage.GetGameBoundRuntimeIdsAsync(cancellationToken);
        var latestTrial = await (
            from runtime in context.TeamLabRuntimes.AsNoTracking()
            join candidateRelease in context.TeamLabTopologyReleases.AsNoTracking()
                on runtime.TopologyReleaseId equals candidateRelease.Id
            where candidateRelease.Id == releaseId &&
                  !gameBoundRuntimeIds.Contains(runtime.Id)
            orderby runtime.CreatedAt descending, runtime.PublicId descending
            select new TrialRuntimeRow(
                candidateRelease.TopologyId,
                runtime.PublicId,
                runtime.TopologyReleaseId,
                runtime.Status,
                runtime.IsOpenToPlayers,
                runtime.CreatedAt,
                runtime.UpdatedAt,
                runtime.LastError))
            .FirstOrDefaultAsync(cancellationToken);
        var blockers = new List<string>();
        if (planningBlocker is not null)
            blockers.Add(planningBlocker);
        foreach (var image in images)
        {
            if (image.EligibleNodeCount == 0)
                blockers.Add($"{image.Name} 没有具备对应能力的可调度节点。");
        }
        if (plan is not null)
            blockers.AddRange(plan.Warnings);
        return new TeamLabAdminReleaseReadinessModel(
            topologyId,
            releaseId,
            blockers.Count == 0,
            plan,
            images,
            latestTrial is null ? null : ToRuntime(latestTrial),
            blockers);
    }

    private async Task<IReadOnlyList<TeamLabPlanningNodeSnapshot>> LoadPlanningNodesAsync(
        CancellationToken cancellationToken) =>
        (await capacitySnapshots.LoadAsync(cancellationToken))
        .Where(item => item.Node.IsSchedulable && item.Node.TeamLabNetworkEnabled &&
                       item.Node.TeamLabTunnelStatus == TeamLabTunnelStatus.Healthy &&
                       item.Node.GetEffectiveStatus(DateTimeOffset.UtcNow) == NodeStatus.Online)
        .Select(item => new TeamLabPlanningNodeSnapshot(
            item.Node.Id,
            item.Node.Name,
            (item.Node.Capabilities & NodeCapability.Docker) != 0,
            (item.Node.Capabilities & NodeCapability.Kvm) != 0,
            item.AvailableDocker,
            item.AvailableVm,
            item.Node.CpuLoad,
            item.Node.MemoryLoad,
            item.Available))
        .ToArray();

    private static string DescribePlanningBlocker(
        TeamLabExecutionTopology execution,
        IReadOnlyList<TeamLabPlanningNodeSnapshot> nodes)
    {
        var docker = execution.Assets.Count(item => item.Kind == TeamLabAssetKind.Docker);
        var vm = execution.Assets.Count(item => item.Kind == TeamLabAssetKind.Vm);
        var cpu = execution.Assets.Sum(item => item.CpuUnits);
        var memory = execution.Assets.Sum(item => item.MemoryMiB);
        var availableDocker = nodes.Where(item => item.SupportsDocker).Sum(item => item.AvailableDockerSlots);
        var availableVm = nodes.Where(item => item.SupportsVm).Sum(item => item.AvailableVmSlots);
        var availableCpu = nodes.Sum(item => item.AvailableResources.CpuUnits);
        var availableMemory = nodes.Sum(item => item.AvailableResources.MemoryMiB);
        var nodeSummary = nodes.Count == 0
            ? "当前没有已接入组网的在线可调度节点"
            : $"当前可用：Docker {availableDocker} 个、VM {availableVm} 个、CPU {availableCpu}、内存 {availableMemory} MiB";
        return $"资源不足，无法放置该版本。需求：Docker {docker} 个、VM {vm} 个、CPU {cpu}、内存 {memory} MiB；{nodeSummary}。";
    }

    public async Task<TeamLabAdminRuntimePageModel> ListTrialRuntimesAsync(
        Guid? topologyId,
        Guid actorUserId,
        bool administrator,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, 100);
        var cursor = DecodeCursor(after, "teamlab_runtime_cursor_invalid");
        var gameBoundRuntimeIds = await usage.GetGameBoundRuntimeIdsAsync(cancellationToken);
        var rows = await (
            from runtime in context.TeamLabRuntimes.AsNoTracking()
            join release in context.TeamLabTopologyReleases.AsNoTracking()
                on runtime.TopologyReleaseId equals release.Id
            join topology in context.TeamLabTopologies.AsNoTracking()
                on release.TopologyId equals topology.Id
            where (administrator || topology.OwnerUserId == actorUserId) &&
                  (!topologyId.HasValue || topology.PublicId == topologyId.Value) &&
                  (!cursor.HasValue || runtime.CreatedAt < cursor.Value.Time ||
                   runtime.CreatedAt == cursor.Value.Time && runtime.PublicId.CompareTo(cursor.Value.Id) < 0) &&
                  !gameBoundRuntimeIds.Contains(runtime.Id)
            orderby runtime.CreatedAt descending, runtime.PublicId descending
            select new TrialRuntimeRow(
                release.TopologyId,
                runtime.PublicId,
                runtime.TopologyReleaseId,
                runtime.Status,
                runtime.IsOpenToPlayers,
                runtime.CreatedAt,
                runtime.UpdatedAt,
                runtime.LastError))
            .Take(take + 1)
            .ToArrayAsync(cancellationToken);
        var page = rows.Take(take).Select(ToRuntime).ToArray();
        var next = rows.Length > take
            ? new GuidTimeCursor(page[^1].CreatedAt, page[^1].Id).Encode()
            : null;
        return new TeamLabAdminRuntimePageModel(page, next);
    }

    public async Task<Guid> RequireReleaseOwnerAsync(
        Guid releaseId,
        Guid actorUserId,
        bool administrator,
        CancellationToken cancellationToken)
    {
        var owner = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.Id == releaseId)
            .Select(item => item.Topology.OwnerUserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (owner is null)
            throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);
        if (!administrator && owner != actorUserId)
            throw new TeamLabApiContractException("insufficient_permission", "该版本不受当前操作者管理", 403);
        return owner.Value;
    }

    private IQueryable<TeamLabTopology> ApplyStatusFilter(
        IQueryable<TeamLabTopology> query,
        string? status,
        IReadOnlySet<int> gameBoundRuntimeIds) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "draft" => query.Where(item => !item.Releases.Any()),
            "published" => query.Where(item => item.Releases.Any()),
            "running" => query.Where(item => item.Releases.Any(release =>
                context.TeamLabRuntimes.Any(runtime => runtime.TopologyReleaseId == release.Id &&
                    runtime.Status == TeamLabRuntimeStatus.Running &&
                    !gameBoundRuntimeIds.Contains(runtime.Id)))),
            "failed" => query.Where(item => item.Releases.Any(release =>
                context.TeamLabRuntimes.Any(runtime => runtime.TopologyReleaseId == release.Id &&
                    runtime.Status == TeamLabRuntimeStatus.Failed &&
                    !gameBoundRuntimeIds.Contains(runtime.Id)))),
            _ => query
        };

    private static TeamLabAdminReleaseSummaryModel ToRelease(TeamLabTopologyRelease release) =>
        new(release.Id, release.Version, release.SourceRevision, release.ContentHash, release.PublishedAt);

    private static TeamLabAdminRuntimeSummaryModel ToRuntime(TrialRuntimeRow runtime) =>
        new(runtime.Id, runtime.ReleaseId, runtime.Status, Stage(runtime.Status), runtime.OpenForAccess,
            runtime.CreatedAt, runtime.UpdatedAt, runtime.Error);

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

    private static int CountInfrastructure(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string DisplayName(string? realName, string? userName) =>
        !string.IsNullOrWhiteSpace(realName) ? realName : userName ?? "未命名用户";

    private static GuidTimeCursor? DecodeCursor(string? value, string code)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return GuidTimeCursor.Decode(value);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException(code, "分页游标无效", 400);
        }
    }

    private sealed record SceneRow(
        int Id,
        Guid PublicId,
        string Name,
        Guid? OwnerId,
        int Revision,
        int SchemaVersion,
        int NetworkCount,
        int AssetCount,
        string InfrastructureJson,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record TrialRuntimeRow(
        int TopologyId,
        Guid Id,
        Guid ReleaseId,
        TeamLabRuntimeStatus Status,
        bool OpenForAccess,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt,
        string? Error);
}
