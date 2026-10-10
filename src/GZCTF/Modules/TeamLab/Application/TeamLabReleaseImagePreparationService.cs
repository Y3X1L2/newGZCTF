using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Explicit administrator prewarming and release preparation diagnostics.
/// Publishing and creating a runtime do not call this prewarming path; deployment
/// prepares the required images only on its selected Workers with runtime claims.
/// </summary>
public sealed class TeamLabReleaseImagePreparationService(
    AppDbContext context,
    ImageDistributionService distribution)
{
    public async Task<TeamLabTemplatePreparationResultModel> QueueTemplatesAsync(
        PrepareTeamLabTemplatesModel command,
        CancellationToken cancellationToken)
    {
        var ids = command.TemplateIds.Distinct().OrderBy(item => item).ToArray();
        if (ids.Length == 0)
            throw new TeamLabApiContractException("image_templates_required", "请至少选择一个镜像模板。", 422);
        var ready = await context.ImageTemplates.AsNoTracking()
            .Where(item => ids.Contains(item.Id) && item.Status == ImageStatus.Ready && item.ImageHash != null)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        if (ready.Length != ids.Length)
            throw new TeamLabApiContractException("image_template_unavailable", "所选镜像模板中存在未就绪项。", 422);
        var records = await distribution.DistributeTemplatesAsync(
            ids, ImageDistributionReferenceKey.TeamLabTemplatePreparation(), cancellationToken);
        return new(ids.Length, records.Count);
    }

    public async Task QueueAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await TeamLabReleaseLifecycle.LockAsync(context, releaseId, cancellationToken);
        await TeamLabReleaseLifecycle.RequireStartableAsync(context, releaseId, cancellationToken);
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == releaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);
        var execution = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        var reference = ImageDistributionReferenceKey.TeamLabRelease(release.Id);

        var templateIds = execution.Assets.Select(item => item.ImageTemplateId)
            .Distinct().OrderBy(item => item).ToArray();
        await distribution.DistributeTemplatesAsync(templateIds, reference, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public Task ReleaseAsync(Guid releaseId, CancellationToken cancellationToken) =>
        distribution.ReleaseTeamLabReleaseReferencesAsync(releaseId, cancellationToken);

    public Task ReleaseTopologyAsync(int topologyId, CancellationToken cancellationToken) =>
        distribution.ReleaseTeamLabTopologyReferencesAsync(topologyId, cancellationToken);

    public Task ReleaseBeforeAsync(Guid releaseId, DateTimeOffset destroyedAt, CancellationToken cancellationToken) =>
        distribution.ReleaseTeamLabReleaseReferencesBeforeAsync(releaseId, destroyedAt, cancellationToken);

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
    /// Artifact/capability admission with independent cache diagnostics. ReadyToStart
    /// permits on-demand downloads; cache counts are not a placement guarantee.
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
            .Select(item => new { item.Id, item.Name, item.ImageType, item.ImageHash, item.Status })
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
            var kindMatches = requirement.Kind == TeamLabAssetKind.Docker
                ? template?.ImageType == ImageType.Docker
                : template is not null && template.ImageType != ImageType.Docker;
            if (template is null || template.Status != ImageStatus.Ready ||
                string.IsNullOrWhiteSpace(template.ImageHash) || !kindMatches)
                blockers.Add($"{template?.Name ?? $"模板 {requirement.Id}"} 的源制品未就绪。");
            else if (!string.Equals(digest, template.ImageHash, StringComparison.Ordinal))
                blockers.Add($"{template.Name} 的源制品不再匹配已发布摘要。");
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

        if (release.IsArchived)
            blockers.Add("拓扑版本已归档，无法创建新的运行环境。");
        var planAvailable = !release.IsArchived && images.All(item => item.EligibleNodeCount > 0);
        if (!planAvailable)
            blockers.AddRange(images.Where(item => item.EligibleNodeCount == 0)
                .Select(item => $"{item.TemplateName} 没有具备对应能力的可调度节点。"));
        // Failed copies on unrelated Workers do not block on-demand deployment.
        // The selected-node Ensure path still reports a real transfer failure on its ticket.
        var readyToStart = planAvailable && blockers.Count == 0;
        var cacheReady = images.All(item => item.ReadyNodeCount > 0);
        var state = !readyToStart ? "blocked" : cacheReady ? "readyToStart" : "onDemand";
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
