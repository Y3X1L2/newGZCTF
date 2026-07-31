using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabBootstrapProfileReferenceProvider(AppDbContext context)
    : IBootstrapProfileReferenceProvider
{
    public string Module => "TeamLab";

    public async Task<IReadOnlyList<BootstrapProfileReference>> GetReferencesAsync(
        Guid profileId,
        CancellationToken cancellationToken)
    {
        var drafts = await context.TeamLabTopologyAssets.AsNoTracking()
            .Where(item => item.BootstrapJson != null)
            .Select(item => new { item.Id, item.Name, item.BootstrapJson })
            .ToArrayAsync(cancellationToken);
        var draftReferences = drafts.Where(item => Matches(item.BootstrapJson, profileId))
            .Select(item => new BootstrapProfileReference(
                Module, "topology-asset", item.Id.ToString(), item.Name));
        var releases = await context.TeamLabTopologyReleases.AsNoTracking()
            .Select(item => new { item.Id, item.Version, item.SchemaVersion, item.CanonicalJson })
            .ToArrayAsync(cancellationToken);
        var releaseReferences = releases
            .Where(item => TeamLabReleaseCodec.DecodeExecution(item.SchemaVersion, item.CanonicalJson).Assets
                .Any(asset => asset.Bootstrap?.ProfileId == profileId))
            .Select(item => new BootstrapProfileReference(
                Module, "topology-release", item.Id.ToString("D"), $"TeamLab release v{item.Version}"));
        return draftReferences.Concat(releaseReferences)
            .DistinctBy(item => (item.ResourceType, item.ResourceId))
            .OrderBy(item => item.ResourceType, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool Matches(string? json, Guid profileId)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            return JsonSerializer.Deserialize<TeamLabBootstrapReferenceModel>(json)?.ProfileId == profileId;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
