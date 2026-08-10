using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Queues release images ahead of runtime creation and keeps them while the release
/// exists. The claim attached here is the topology-scoped preparation claim: topology
/// deletion is blocked while any release exists (<c>release_immutable</c>), so the
/// claim lifetime equals the release lifetime. Runtimes and rollouts attach their own
/// claims when they consume the release, so a shared image survives until every
/// dependent resource is terminally cleaned.
/// </summary>
public sealed class TeamLabReleaseImagePreparationService(
    AppDbContext context,
    ImageDistributionService distribution)
{
    public async Task QueueAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == releaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);
        var execution = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        var reference = ImageDistributionReferenceKey.TeamLabRelease(release.Id);

        foreach (var templateId in execution.Assets
                     .Select(item => item.ImageTemplateId)
                     .Distinct()
                     .OrderBy(item => item))
            await distribution.DistributeTemplateAsync(templateId, reference, cancellationToken);
    }

    public Task ReleaseAsync(Guid releaseId, CancellationToken cancellationToken) =>
        distribution.ReleaseTeamLabReleaseReferencesAsync(releaseId, cancellationToken);

    public async Task ReleaseScopeAsync(Guid scopeId, CancellationToken cancellationToken)
    {
        var releaseIds = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(item => item.ControlScopeId == scopeId)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var releaseId in releaseIds)
            await ReleaseAsync(releaseId, cancellationToken);
    }

    /// <summary>
    /// External readiness projection. No worker address, ticket or Agent detail crosses
    /// the boundary; callers observe only per-template counts against eligible nodes.
    /// </summary>
    public async Task<TeamLabReleasePreparationModel> GetPreparationAsync(
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == releaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);
        var execution = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        var requirements = execution.Assets
            .GroupBy(item => item.ImageTemplateId)
            .Select(group => new
            {
                Id = group.Key,
                Kind = group.First().Kind,
                Digest = group.Select(item => item.ImageDigest)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            })
            .ToArray();
        var templateIds = requirements.Select(item => item.Id).ToArray();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(item => templateIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Name, item.ImageType, item.ImageHash })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var nodes = (await context.WorkerNodes.AsNoTracking()
            .Where(item => item.IsSchedulable && item.TeamLabNetworkEnabled &&
                           item.TeamLabTunnelStatus == TeamLabTunnelStatus.Healthy)
            .Select(item => new { item.Id, item.Capabilities, item.Status, item.IsLocal, item.LastHeartbeat })
            .ToArrayAsync(cancellationToken))
            .Where(item => EffectiveStatus(item.Status, item.IsLocal, item.LastHeartbeat, now) == NodeStatus.Online)
            .Select(item => new { item.Id, item.Capabilities })
            .ToArray();
        var records = await context.ImageDistributionRecords.AsNoTracking()
            .Where(item => templateIds.Contains(item.ImageTemplateId))
            .Select(item => new
            {
                item.ImageTemplateId,
                item.WorkerNodeId,
                item.ImageHash,
                item.Status,
                item.Retryable,
                item.ErrorMessage
            })
            .ToArrayAsync(cancellationToken);

        var blockers = new List<string>();
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
            var ready = matching.Count(item => item.Status == ImageDistributionStatus.Ready);
            var preparing = matching.Count(item => item.Status is
                ImageDistributionStatus.Pending or ImageDistributionStatus.Pulling);
            var failed = matching.Where(item => item.Status == ImageDistributionStatus.Failed).ToArray();
            return new TeamLabReleaseImagePreparationModel(
                requirement.Id,
                template?.Name ?? $"模板 {requirement.Id}",
                (template?.ImageType ?? (requirement.Kind == TeamLabAssetKind.Docker ? ImageType.Docker : ImageType.Qcow2)).ToString(),
                eligible.Count,
                ready,
                preparing,
                failed.Length,
                failed.Length == 0
                    ? null
                    : new OpenTeamLabFailureModel(
                        "image_distribution_failed", "distribution",
                        failed.Any(item => item.Retryable)));
        }).OrderBy(item => item.TemplateId).ToArray();

        var planAvailable = images.All(item => item.EligibleNodeCount > 0);
        if (!planAvailable)
            blockers.AddRange(images.Where(item => item.EligibleNodeCount == 0)
                .Select(item => $"{item.TemplateName} 没有具备对应能力的可调度节点。"));
        var failedImages = images.Where(item => item.Failure is not null).ToArray();
        if (failedImages.Length > 0)
            blockers.AddRange(failedImages.Select(item => $"{item.TemplateName} 镜像准备失败。"));
        var started = images.Any(item => item.ReadyNodeCount > 0 || item.PreparingNodeCount > 0 || item.FailedNodeCount > 0);
        var readyToStart = planAvailable && failedImages.Length == 0 &&
                           images.All(item => item.ReadyNodeCount > 0);
        var preparing = planAvailable && failedImages.Length == 0 &&
                        images.Any(item => item.PreparingNodeCount > 0) && !readyToStart;
        var state = !planAvailable || failedImages.Length > 0
            ? "blocked"
            : readyToStart ? "readyToStart" : preparing ? "preparing" : "notStarted";
        if (state == "notStarted")
            blockers.Add("镜像准备尚未开始，请提交 POST /api/open/v1/teamlab/preparations/releases/{releaseId} 触发预分发。");
        return new TeamLabReleasePreparationModel(
            releaseId,
            state,
            planAvailable,
            readyToStart,
            blockers.ToArray(),
            images);
    }

    private static NodeStatus EffectiveStatus(
        NodeStatus status,
        bool isLocal,
        DateTimeOffset? lastHeartbeat,
        DateTimeOffset utcNow) =>
        status != NodeStatus.Online || isLocal
            ? status
            : lastHeartbeat is not { } heartbeat || heartbeat < utcNow - WorkerNode.DefaultHeartbeatTimeout
                ? NodeStatus.Offline
                : status;
}
