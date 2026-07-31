using System.Security.Cryptography;
using System.Data;
using System.Text;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabReleaseService(
    AppDbContext context,
    TeamLabTopologyValidator validator,
    BootstrapProfileCompatibilityService bootstrapCompatibility,
    IBootstrapProfileDistributionService bootstrapDistribution)
{
    public async Task<TeamLabReleaseModel> PublishAsync(
        TeamLabTopology topology,
        int expectedRevision,
        Guid actorUserId,
        Guid? operationId,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? scenarioOverlays,
        CancellationToken cancellationToken)
    {
        if (operationId is { } operation)
        {
            var applied = await context.TeamLabTopologyReleases.AsNoTracking()
                .SingleOrDefaultAsync(item => item.ApiOperationId == operation, cancellationToken);
            if (applied is not null)
                return ToModel(applied, topology.PublicId);
        }
        if (topology.Revision != expectedRevision)
            throw new TeamLabApiContractException(
                "topology_revision_conflict",
                $"Topology revision is {topology.Revision}, not {expectedRevision}.",
                409);

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        if (string.Equals(
                context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
        {
            var releaseLock = $"teamlab:topology-release:{topology.Id}";
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({releaseLock}, 0))",
                cancellationToken);
        }
        var persistedRevision = await context.TeamLabTopologies.AsNoTracking()
            .Where(item => item.Id == topology.Id)
            .Select(item => (int?)item.Revision)
            .SingleOrDefaultAsync(cancellationToken);
        if (persistedRevision != expectedRevision)
            throw new TeamLabApiContractException(
                "topology_revision_conflict",
                $"Topology revision is {persistedRevision?.ToString() ?? "unavailable"}, not {expectedRevision}.",
                409);

        var definition = TeamLabTopologyApplicationService.ToDefinition(topology);
        var validation = validator.Validate(definition, topology.SchemaVersion);
        if (!validation.Valid)
            throw TeamLabTopologyApplicationService.InvalidTopology(validation);
        await TeamLabTopologyApplicationService.ValidateImageTemplatesAsync(context, definition, cancellationToken);
        definition = await BindImageDigestsAsync(definition, cancellationToken);
        var canonicalJson = TeamLabReleaseCodec.Encode(topology.SchemaVersion, definition);
        var bootstrapVersions = await bootstrapCompatibility.ValidateReleaseAsync(
            TeamLabReleaseCodec.DecodeExecution(topology.SchemaVersion, canonicalJson),
            cancellationToken);

        var contentHash = TeamLabReleaseCodec.ComputeContentHash(topology.SchemaVersion, canonicalJson);
        var scenarioInputDigest = ComputeScenarioInputDigest(scenarioOverlays);
        if (scenarioInputDigest is not null)
            contentHash = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{contentHash}:scenario:{scenarioInputDigest}")))}";
        var existing = await context.TeamLabTopologyReleases
            .AsNoTracking()
            .FirstOrDefaultAsync(item =>
                item.TopologyId == topology.Id &&
                item.SourceRevision == topology.Revision &&
                item.ContentHash == contentHash,
                cancellationToken);
        if (existing is not null)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            foreach (var version in bootstrapVersions)
                await bootstrapDistribution.QueueAndDistributeAsync(version.Id, cancellationToken);
            return ToModel(existing, topology.PublicId);
        }

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
            PublishedById = actorUserId,
            ApiOperationId = operationId
        };
        context.TeamLabTopologyReleases.Add(release);
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        foreach (var version in bootstrapVersions)
            await bootstrapDistribution.QueueAndDistributeAsync(version.Id, cancellationToken);
        return ToModel(release, topology.PublicId);
    }

    private static string? ComputeScenarioInputDigest(IReadOnlyList<TeamLabRuntimeOverlayModel>? overlays)
    {
        if (overlays is null || overlays.Count == 0) return null;
        var canonical = overlays
            .OrderBy(item => item.AssetKey, StringComparer.Ordinal)
            .Select(item => new
            {
                item.AssetKey,
                Environment = (item.Environment ?? new Dictionary<string, string>())
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new[] { pair.Key, pair.Value })
                    .ToArray(),
                Secrets = (item.Secrets ?? new Dictionary<string, string>())
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new[] { pair.Key, pair.Value })
                    .ToArray()
            })
            .ToArray();
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
    }

    private async Task<TeamLabTopologyDefinitionModel> BindImageDigestsAsync(
        TeamLabTopologyDefinitionModel definition,
        CancellationToken cancellationToken)
    {
        var templateIds = definition.Assets.Select(item => item.ImageTemplateId).Distinct().ToArray();
        var digests = await context.ImageTemplates.AsNoTracking()
            .Where(item => templateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.ImageHash, cancellationToken);
        return definition with
        {
            Assets = definition.Assets.Select(asset => asset with
            {
                ImageDigest = digests.GetValueOrDefault(asset.ImageTemplateId)
            }).ToArray()
        };
    }

    public static TeamLabReleaseModel ToModel(TeamLabTopologyRelease release, Guid topologyPublicId) =>
        new(release.Id, topologyPublicId, release.Version, release.SourceRevision, release.SchemaVersion,
            release.ContentHash, release.PublishedById, release.PublishedAt);
}
