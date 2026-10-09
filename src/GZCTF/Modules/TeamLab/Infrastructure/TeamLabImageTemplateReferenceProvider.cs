using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabImageTemplateReferenceProvider(AppDbContext context)
    : IImageTemplateReferenceProvider
{
    public string Module => "TeamLab";

    public async Task<IReadOnlyList<ImageTemplateReference>> GetReferencesAsync(
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var draftReferences = await context.TeamLabTopologyAssets.AsNoTracking()
            .Where(asset => asset.ImageTemplateId == imageTemplateId)
            .Select(asset => new ImageTemplateReference(
                Module, "topology-asset", asset.Id.ToString(), asset.Name))
            .ToArrayAsync(cancellationToken);
        // Hot-added assets need not appear in the frozen release or current draft. Their
        // canonical source remains protected even when node cache demand is being reconciled.
        var runtimeReferences = await context.TeamLabRuntimeAssets.AsNoTracking()
            .Where(asset => asset.SourceTemplateId == imageTemplateId &&
                (asset.Status != TeamLabRuntimeStatus.Destroyed &&
                 asset.Runtime.Status != TeamLabRuntimeStatus.Destroyed ||
                 context.DeploymentQueueTickets.Any(ticket =>
                     ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.TeamLabRuntimeId == asset.RuntimeId &&
                     ticket.Operation == RuntimeOperationKind.Reset &&
                     (ticket.Status == DeploymentQueueTicketStatus.Pending ||
                      ticket.Status == DeploymentQueueTicketStatus.Scheduling ||
                      ticket.Status == DeploymentQueueTicketStatus.Scheduled ||
                      ticket.Status == DeploymentQueueTicketStatus.Running ||
                      ticket.ClaimOwner != null && ticket.ClaimExpiresAt > now))))
            .Select(asset => new ImageTemplateReference(
                Module, "runtime-asset", asset.Id.ToString(), asset.Name))
            .ToArrayAsync(cancellationToken);
        var rollouts = await context.TeamLabRollouts.AsNoTracking()
            .Where(rollout => rollout.Status != TeamLabRolloutStatus.Completed &&
                             rollout.Status != TeamLabRolloutStatus.Archived)
            .Select(rollout => new { rollout.PublicId, rollout.ReleaseId })
            .ToArrayAsync(cancellationToken);
        var rolloutReleaseIds = rollouts.Select(rollout => rollout.ReleaseId).Distinct().ToArray();
        var releases = await context.TeamLabTopologyReleases.AsNoTracking()
            .Where(release => !release.IsArchived || rolloutReleaseIds.Contains(release.Id))
            .Select(release => new { release.Id, release.Version, release.SchemaVersion, release.CanonicalJson, release.IsArchived })
            .ToArrayAsync(cancellationToken);
        var consumingReleases = releases
            .Where(release => TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson).Assets
                .Any(asset => asset.ImageTemplateId == imageTemplateId))
            .ToArray();
        var releaseReferences = consumingReleases.Where(release => !release.IsArchived)
            .Select(release => new ImageTemplateReference(
                Module, "topology-release", release.Id.ToString("D"), $"TeamLab release v{release.Version}"));
        var consumingReleaseIds = consumingReleases.Select(release => release.Id).ToHashSet();
        var rolloutReferences = rollouts.Where(rollout => consumingReleaseIds.Contains(rollout.ReleaseId))
            .Select(rollout => new ImageTemplateReference(
                Module, "rollout", rollout.PublicId.ToString("D"), $"TeamLab rollout {rollout.PublicId:D}"));
        return draftReferences.Concat(runtimeReferences).Concat(releaseReferences).Concat(rolloutReferences)
            .DistinctBy(reference => (reference.ResourceType, reference.ResourceId))
            .OrderBy(reference => reference.ResourceType, StringComparer.Ordinal)
            .ThenBy(reference => reference.ResourceId, StringComparer.Ordinal)
            .ToArray();
    }
}
