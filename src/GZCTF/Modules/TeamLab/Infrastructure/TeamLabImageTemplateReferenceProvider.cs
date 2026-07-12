using GZCTF.Models;
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
        var releases = await context.TeamLabTopologyReleases.AsNoTracking()
            .Select(release => new { release.Id, release.Version, release.CanonicalJson })
            .ToArrayAsync(cancellationToken);
        var releaseReferences = releases
            .Where(release => TeamLabReleaseCodec.Decode(release.CanonicalJson).Assets
                .Any(asset => asset.ImageTemplateId == imageTemplateId))
            .Select(release => new ImageTemplateReference(
                Module, "topology-release", release.Id.ToString("D"), $"TeamLab release v{release.Version}"));
        return draftReferences.Concat(releaseReferences)
            .DistinctBy(reference => (reference.ResourceType, reference.ResourceId))
            .OrderBy(reference => reference.ResourceType, StringComparer.Ordinal)
            .ThenBy(reference => reference.ResourceId, StringComparer.Ordinal)
            .ToArray();
    }
}
