using GZCTF.Models;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabReleaseService(AppDbContext context, TeamLabTopologyValidator validator)
{
    public async Task<TeamLabReleaseModel> PublishAsync(
        TeamLabTopology topology,
        int expectedRevision,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (topology.Revision != expectedRevision)
            throw new TeamLabApiContractException(
                "topology_revision_conflict",
                $"Topology revision is {topology.Revision}, not {expectedRevision}.",
                409);

        var definition = TeamLabTopologyApplicationService.ToDefinition(topology);
        var validation = validator.Validate(definition);
        if (!validation.Valid)
            throw TeamLabTopologyApplicationService.InvalidTopology(validation);
        await TeamLabTopologyApplicationService.ValidateImageTemplatesAsync(context, definition, cancellationToken);

        var canonicalJson = TeamLabReleaseCodec.Encode(definition);
        var contentHash = TeamLabReleaseCodec.ComputeContentHash(topology.SchemaVersion, canonicalJson);
        var existing = await context.TeamLabTopologyReleases
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.TopologyId == topology.Id &&
                item.SourceRevision == topology.Revision &&
                item.ContentHash == contentHash,
                cancellationToken);
        if (existing is not null)
            return ToModel(existing, topology.PublicId);

        var nextVersion = (await context.TeamLabTopologyReleases
            .Where(item => item.TopologyId == topology.Id)
            .MaxAsync(item => (int?)item.Version, cancellationToken) ?? 0) + 1;
        var release = new TeamLabTopologyRelease
        {
            TopologyId = topology.Id,
            Version = nextVersion,
            SourceRevision = topology.Revision,
            SchemaVersion = topology.SchemaVersion,
            CanonicalJson = canonicalJson,
            ContentHash = contentHash,
            PublishedById = actorUserId
        };
        context.TeamLabTopologyReleases.Add(release);
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(release, topology.PublicId);
    }

    public static TeamLabReleaseModel ToModel(TeamLabTopologyRelease release, Guid topologyPublicId) =>
        new(release.Id, topologyPublicId, release.Version, release.SourceRevision, release.SchemaVersion,
            release.ContentHash, release.PublishedById, release.PublishedAt);
}
