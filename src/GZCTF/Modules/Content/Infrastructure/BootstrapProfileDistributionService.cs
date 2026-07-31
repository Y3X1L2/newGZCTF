using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class BootstrapProfileDistributionService(
    AppDbContext context,
    IServiceScopeFactory scopeFactory) : IBootstrapProfileDistributionService
{
    public async Task<IReadOnlyList<BootstrapProfileDistribution>> QueueAndDistributeAsync(
        long profileVersionId,
        CancellationToken cancellationToken)
    {
        var version = await context.BootstrapProfileVersions.Include(item => item.Profile)
            .SingleAsync(item => item.Id == profileVersionId, cancellationToken);
        if (version.Status != BootstrapProfileVersionStatus.Ready)
            throw new InvalidOperationException("Only ready bootstrap profile versions can be distributed.");
        var manifest = BootstrapProfileApplicationService.ParseAndValidateManifest(version.ManifestJson);
        var nodes = (await context.WorkerNodes.AsNoTracking()
                .Where(item => item.Status == NodeStatus.Online && item.IsSchedulable)
                .OrderBy(item => item.Name).ThenBy(item => item.Id)
                .ToArrayAsync(cancellationToken))
            .Where(node => AgentCapabilityEvaluator.Supports(node, AgentFeatureIds.BootstrapArtifactPull) &&
                           SupportsAssetKinds(node, manifest.AssetKinds))
            .ToArray();
        var existing = await context.BootstrapProfileDistributions
            .Where(item => item.ProfileVersionId == profileVersionId)
            .ToDictionaryAsync(item => item.WorkerNodeId, cancellationToken);
        foreach (var node in nodes)
        {
            if (existing.TryGetValue(node.Id, out var current) &&
                string.Equals(current.ArtifactDigest, version.ArtifactDigest, StringComparison.Ordinal) &&
                current.Status == BootstrapProfileDistributionStatus.Ready)
                continue;
            if (current is null)
            {
                current = new BootstrapProfileDistribution
                {
                    ProfileVersionId = version.Id,
                    WorkerNodeId = node.Id,
                    ArtifactDigest = version.ArtifactDigest
                };
                context.BootstrapProfileDistributions.Add(current);
                existing[node.Id] = current;
            }
            else
            {
                current.ArtifactDigest = version.ArtifactDigest;
                current.Status = BootstrapProfileDistributionStatus.Pending;
                current.ErrorMessage = null;
            }
        }
        await context.SaveChangesAsync(cancellationToken);

        var ids = existing.Values
            .Where(item => nodes.Any(node => node.Id == item.WorkerNodeId) &&
                           item.Status != BootstrapProfileDistributionStatus.Ready)
            .Select(item => item.Id)
            .ToArray();
        await Task.WhenAll(ids.Select(id => ProcessAsync(id, cancellationToken)));
        return await context.BootstrapProfileDistributions.AsNoTracking()
            .Where(item => item.ProfileVersionId == profileVersionId)
            .OrderBy(item => item.WorkerNode.Name).ThenBy(item => item.WorkerNodeId)
            .ToArrayAsync(cancellationToken);
    }

    public async Task DeleteVersionCachesAsync(
        BootstrapProfileVersion version,
        CancellationToken cancellationToken)
    {
        var records = await context.BootstrapProfileDistributions
            .Where(item => item.ProfileVersionId == version.Id)
            .Select(item => new { item.Id, item.WorkerNodeId })
            .ToArrayAsync(cancellationToken);
        await Task.WhenAll(records.Select(record => DeleteCacheAsync(
            record.Id, record.WorkerNodeId, version.Profile.PublicId, version.Version, cancellationToken)));
    }

    private async Task ProcessAsync(Guid recordId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = scope.ServiceProvider.GetRequiredService<AgentClient>();
        var record = await db.BootstrapProfileDistributions
            .Include(item => item.ProfileVersion).ThenInclude(item => item.Profile)
            .SingleAsync(item => item.Id == recordId, cancellationToken);
        if (record.Status == BootstrapProfileDistributionStatus.Ready) return;
        record.Status = BootstrapProfileDistributionStatus.Pulling;
        record.AttemptCount++;
        record.LastCheckedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var version = record.ProfileVersion;
            var result = await agent.DownloadBootstrapArtifactAsync(
                record.WorkerNodeId,
                new AgentBootstrapArtifactDownloadRequest(
                    version.Profile.PublicId,
                    version.Version,
                    version.RegistryAddress,
                    version.RegistryRepository,
                    $"sha256:{version.ArtifactDigest}",
                    version.ArtifactSize),
                cancellationToken);
            if (!result.Success || !result.Verified ||
                !string.Equals(
                    OciArtifactRegistryClient.NormalizeDigest(result.Digest),
                    OciArtifactRegistryClient.NormalizeDigest(version.ArtifactDigest),
                    StringComparison.Ordinal))
                throw new InvalidOperationException(result.Message);
            record.Status = BootstrapProfileDistributionStatus.Ready;
            record.LocalPath = result.LocalPath;
            record.ErrorMessage = null;
        }
        catch (Exception exception) when (exception is AgentClientException or HttpRequestException or
                                                   InvalidOperationException or TaskCanceledException)
        {
            record.Status = BootstrapProfileDistributionStatus.Failed;
            record.ErrorMessage = Trim(exception.Message);
        }
        record.LastCheckedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task DeleteCacheAsync(
        Guid recordId,
        Guid workerNodeId,
        Guid profileId,
        int version,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = scope.ServiceProvider.GetRequiredService<AgentClient>();
        try
        {
            await agent.DeleteBootstrapArtifactAsync(workerNodeId, profileId, version, cancellationToken);
            var record = await db.BootstrapProfileDistributions.SingleOrDefaultAsync(
                item => item.Id == recordId, cancellationToken);
            if (record is not null)
            {
                db.BootstrapProfileDistributions.Remove(record);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is AgentClientException or HttpRequestException or TaskCanceledException)
        {
            var record = await db.BootstrapProfileDistributions.SingleOrDefaultAsync(
                item => item.Id == recordId, CancellationToken.None);
            if (record is null) return;
            record.Status = BootstrapProfileDistributionStatus.Failed;
            record.ErrorMessage = Trim(exception.Message);
            record.LastCheckedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static bool SupportsAssetKinds(WorkerNode node, IReadOnlySet<TeamLabAssetKind> kinds) =>
        kinds.Any(kind => kind switch
        {
            TeamLabAssetKind.Docker => (node.Capabilities & NodeCapability.Docker) != 0,
            TeamLabAssetKind.Vm => (node.Capabilities & NodeCapability.Kvm) != 0,
            _ => false
        });

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1024];
}
