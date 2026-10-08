using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.TeamLab.Application;
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
                      ticket.Status == DeploymentQueueTicketStatus.Running))))
            .Select(asset => new ImageTemplateReference(
                Module, "runtime-asset", asset.Id.ToString(), asset.Name))
            .ToArrayAsync(cancellationToken);
        var releases = await context.TeamLabTopologyReleases.AsNoTracking()
            .Select(release => new { release.Id, release.Version, release.SchemaVersion, release.CanonicalJson })
            .ToArrayAsync(cancellationToken);
        var releaseReferences = releases
            .Where(release => TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson).Assets
                .Any(asset => asset.ImageTemplateId == imageTemplateId))
            .Select(release => new ImageTemplateReference(
                Module, "topology-release", release.Id.ToString("D"), $"TeamLab release v{release.Version}"));
        return draftReferences.Concat(runtimeReferences).Concat(releaseReferences)
            .DistinctBy(reference => (reference.ResourceType, reference.ResourceId))
            .OrderBy(reference => reference.ResourceType, StringComparer.Ordinal)
            .ThenBy(reference => reference.ResourceId, StringComparer.Ordinal)
            .ToArray();
    }
}
