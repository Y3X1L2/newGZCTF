using System.Security.Cryptography;
using System.Text;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.TeamLab;
using GZCTF.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeCleanupService(
    AppDbContext context,
    ITeamLabNodeExecutor executor,
    TeamLabTrafficApplicationService traffic,
    ITeamLabCaptureCleanup captureCleanup,
    IPublicUdpGatewayProvider publicGateway,
    TeamLabEventRecorder eventRecorder,
    ITeamLabRemoteAccessService remoteAccess,
    TeamLabRemoteCredentialService remoteCredentials)
{
    public Task<TeamLabNodeResult> CleanupAsync(
        TeamLabRuntime runtime,
        CancellationToken cancellationToken) =>
        CleanupAsync(runtime, markDestroyedOnSuccess: false, cancellationToken);

    public async Task<TeamLabNodeResult> CleanupAsync(
        TeamLabRuntime runtime,
        bool markDestroyedOnSuccess,
        CancellationToken cancellationToken)
    {
        var generation = runtime.Generation;
        await remoteAccess.EndRuntimeSessionsAsync(runtime.Id, generation, "runtime_cleanup", cancellationToken);
        await remoteCredentials.RevokeGenerationAsync(runtime.Id, generation, cancellationToken);
        if (runtime.Status != TeamLabRuntimeStatus.Destroying)
            runtime.Status = TeamLabRuntimeStatus.CleanupPending;
        runtime.IsOpenToPlayers = false;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        eventRecorder.Record(
            runtime,
            "cleanup",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.CleanupStarted,
            OperationalEventOutcome.Started,
            "Runtime resource cleanup started.");
        await context.SaveChangesAsync(cancellationToken);
        var shards = runtime.Shards.Where(item => item.Generation == generation).ToArray();
        await traffic.StopCollectorsAsync(runtime, cancellationToken);
        var errors = (await captureCleanup.ExpireGenerationAsync(
            runtime.Id, generation, cancellationToken)).ToList();
        var results = await Task.WhenAll(shards.Select(async shard =>
        {
            try
            {
                var inventory = await executor.GetRuntimeInventoryAsync(shard.WorkerNodeId, cancellationToken);
                return await executor.CleanupShardAsync(
                    shard.WorkerNodeId,
                    BuildCleanupRequest(runtime, shard, inventory),
                    cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                              exception is IOperationalFailureException or HttpRequestException or TaskCanceledException)
            {
                return TeamLabNodeResult.Failed(
                    $"Failed to clean TeamLab runtime resources on node {shard.WorkerNodeId}: {exception.Message}");
            }
        }));
        errors.AddRange(results.Where(item => !item.Success).Select(item => item.Message));
        if (runtime.PublicUdpMapping is not null)
        {
            var gateway = await publicGateway.RemoveMappingAsync(runtime.PublicUdpMapping, cancellationToken);
            if (!gateway.Success) errors.Add(gateway.Message);
        }
        if (errors.Count > 0)
        {
            runtime.Status = TeamLabRuntimeStatus.CleanupPending;
            runtime.IsOpenToPlayers = false;
            runtime.LastError = Trim(string.Join("; ", errors));
            runtime.UpdatedAt = DateTimeOffset.UtcNow;
            eventRecorder.Record(
                runtime,
                "cleanup",
                TeamLabEventLevel.Error,
                OperationalEventCodes.TeamLab.CleanupFailed,
                OperationalEventOutcome.Failed,
                "Runtime resource cleanup failed.",
                new OperationalError(
                    OperationalErrorCategory.Network,
                    OperationalErrorCodes.NetworkOperationFailed,
                    "TeamLab cleanup failed.",
                    true,
                    Operation: "teamlab.cleanup"));
            await context.SaveChangesAsync(cancellationToken);
            return TeamLabNodeResult.Failed(runtime.LastError);
        }

        var fabricLeaseCount = await context.TeamLabFabricLinkLeases
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == generation && item.ReleasedAt == null)
            .CountAsync(cancellationToken);
        await FinalizeGenerationAsync(
            context, runtime, generation, markDestroyedOnSuccess, cancellationToken);
        if (fabricLeaseCount > 0)
        {
            eventRecorder.Record(
                runtime,
                "fabric",
                TeamLabEventLevel.Info,
                OperationalEventCodes.TeamLab.FabricLeaseReleased,
                OperationalEventOutcome.Succeeded,
                "Fabric link leases were released during runtime cleanup.",
                detail: new Dictionary<string, object?>
                {
                    ["generation"] = generation,
                    ["stage"] = "fabric",
                    ["leaseCount"] = fabricLeaseCount
                });
            PlatformTelemetry.RecordTeamLabInfrastructure("released", "fabric-link");
        }
        eventRecorder.Record(
            runtime,
            "cleanup",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.CleanupSucceeded,
            OperationalEventOutcome.Succeeded,
            "Runtime resources were cleaned successfully.");
        await context.SaveChangesAsync(cancellationToken);
        return TeamLabNodeResult.Ok("Runtime resources cleaned.");
    }

    internal static async Task<bool> HasPendingSideEffectsAsync(
        AppDbContext context,
        TeamLabRuntime runtime,
        int generation,
        CancellationToken cancellationToken)
    {
        if (runtime.Shards.Any(item => item.Generation == generation &&
                                      item.Status != TeamLabRuntimeStatus.Destroyed) ||
            runtime.Assets.Any(item => item.Generation == generation &&
                                      item.Status != TeamLabRuntimeStatus.Destroyed) ||
            runtime.Infrastructure.Any(item => item.Generation == generation &&
                (item.Status != TeamLabRuntimeStatus.Destroyed ||
                 item.Fragments.Any(fragment => fragment.Status != TeamLabRuntimeStatus.Destroyed))) ||
            runtime.ObservationPoints.Any(item => item.Generation == generation && item.Enabled) ||
            runtime.AccessGrants.Any(item => item.Generation == generation && !item.Revoked) ||
            runtime.VpnPeers.Any(item => !item.Revoked) ||
            runtime.SecretEnvelopes.Any(item => item.Generation == generation && item.ConsumedAt == null) ||
            runtime.PublicUdpMapping is { IsSynced: true } mapping && mapping.Generation == generation ||
            runtime.TrafficCaptureJobs.Any(item => item.Generation == generation &&
                (item.Status is TeamLabTrafficCaptureStatus.Pending or
                    TeamLabTrafficCaptureStatus.Running or
                    TeamLabTrafficCaptureStatus.Stopping ||
                 item.Segments.Any(IsActiveCaptureSegment))))
            return true;

        return await context.TeamLabNetworkLeases.AsNoTracking()
                   .AnyAsync(item => item.RuntimeId == runtime.Id && item.Generation == generation &&
                                     item.ReleasedAt == null, cancellationToken) ||
               await context.TeamLabFabricLinkLeases.AsNoTracking()
                   .AnyAsync(item => item.RuntimeId == runtime.Id && item.Generation == generation &&
                                     item.ReleasedAt == null, cancellationToken) ||
               await context.ImageDistributionReferences.AsNoTracking()
                   .AnyAsync(item => item.Kind == ImageDistributionReferenceKind.TeamLabRuntime &&
                                     item.ResourceId == runtime.Id, cancellationToken);
    }

    internal static async Task FinalizeGenerationAsync(
        AppDbContext context,
        TeamLabRuntime runtime,
        int generation,
        bool markRuntimeDestroyed,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var shard in runtime.Shards.Where(item => item.Generation == generation))
        {
            shard.Status = TeamLabRuntimeStatus.Destroyed;
            shard.LastError = null;
            shard.UpdatedAt = now;
        }
        foreach (var asset in runtime.Assets.Where(item => item.Generation == generation))
        {
            asset.Status = TeamLabRuntimeStatus.Destroyed;
            asset.LastError = null;
            asset.ExecutionUpdatedAt = now;
        }
        foreach (var infrastructure in runtime.Infrastructure.Where(item => item.Generation == generation))
        {
            infrastructure.Status = TeamLabRuntimeStatus.Destroyed;
            infrastructure.LastError = null;
            infrastructure.UpdatedAt = now;
            foreach (var fragment in infrastructure.Fragments)
            {
                fragment.Status = TeamLabRuntimeStatus.Destroyed;
                fragment.LastError = null;
                fragment.UpdatedAt = now;
            }
        }
        foreach (var observation in runtime.ObservationPoints.Where(item => item.Generation == generation))
        {
            observation.Enabled = false;
            observation.UpdatedAt = now;
        }
        foreach (var grant in runtime.AccessGrants.Where(item => item.Generation == generation && !item.Revoked))
        {
            grant.Revoked = true;
            grant.RevokedAt = now;
        }
        foreach (var peer in runtime.VpnPeers.Where(item => !item.Revoked))
            peer.Revoked = true;
        foreach (var envelope in runtime.SecretEnvelopes.Where(item => item.Generation == generation))
            TeamLabRuntimeOverlayService.Consume(envelope);
        if (runtime.PublicUdpMapping is { } mapping && mapping.Generation == generation)
            mapping.IsSynced = false;
        var leases = await context.TeamLabNetworkLeases
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == generation && item.ReleasedAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var lease in leases)
            lease.ReleasedAt = now;
        var fabricLeases = await context.TeamLabFabricLinkLeases
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == generation && item.ReleasedAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var lease in fabricLeases)
            lease.ReleasedAt = now;

        if (markRuntimeDestroyed)
            runtime.Status = TeamLabRuntimeStatus.Destroyed;
        runtime.IsOpenToPlayers = false;
        runtime.LastError = null;
        runtime.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool IsActiveCaptureSegment(TeamLabTrafficCaptureSegment item) =>
        item.Status is TeamLabTrafficCaptureSegmentStatus.Pending or
            TeamLabTrafficCaptureSegmentStatus.Running or
            TeamLabTrafficCaptureSegmentStatus.Stopping or
            TeamLabTrafficCaptureSegmentStatus.Captured or
            TeamLabTrafficCaptureSegmentStatus.Uploading;

    internal static TeamLabNodeCleanupRequest BuildCleanupRequest(
        TeamLabRuntime runtime,
        TeamLabRuntimeShard shard,
        TeamLabNodeRuntimeInventory inventory)
    {
        var assets = runtime.Assets.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id).ToArray();
        var containerPrefixes = assets.Where(item => item.Kind == TeamLabResourceKind.Docker)
            .Select(item => ContainerNamePrefix(runtime.Id, item.TopologyKey))
            .ToArray();
        var deterministicVmNames = assets.Where(item => item.Kind == TeamLabResourceKind.Vm)
            .Select(item => TeamLabResourceNameFactory.LinuxName($"tl{runtime.Id}-{item.TopologyKey}"))
            .ToHashSet(StringComparer.Ordinal);
        var inventoryContainers = inventory.Containers.Where(item =>
                item.Generation == runtime.Generation &&
                (item.RuntimeId == runtime.Id || containerPrefixes.Any(prefix =>
                    item.StableName.StartsWith(prefix, StringComparison.Ordinal))))
            .Select(item => item.NativeId);
        var inventoryVms = inventory.Vms.Where(item =>
                item.Generation == runtime.Generation &&
                (item.RuntimeId == runtime.Id || deterministicVmNames.Contains(item.StableName)))
            .Select(item => item.StableName);
        var resourceNames = runtime.Networks.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
            .Select(item => item.BridgeName)
            .Append(TeamLabResourceNameFactory.RouterNamespace(runtime.Id, shard.Id))
            .Append(TeamLabResourceNameFactory.WireGuardInterface(runtime.Id))
            .Append(TeamLabResourceNameFactory.FabricHostInterface(runtime.Id))
            .Append(TeamLabResourceNameFactory.FabricNamespaceInterface(runtime.Id))
            .Concat(runtime.Networks.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
                .Select(item => TeamLabResourceNameFactory.DhcpDnsService(runtime.Id, item.TopologyKey)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new TeamLabNodeCleanupRequest(
            runtime.Id,
            runtime.Generation,
            TeamLabResourceNameFactory.RouterNamespace(runtime.Id, shard.Id),
            resourceNames,
            assets.Where(item => item.Kind == TeamLabResourceKind.Docker && !string.IsNullOrWhiteSpace(item.RuntimeResourceId))
                .Select(item => item.RuntimeResourceId!)
                .Concat(inventoryContainers)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            assets.Where(item => item.Kind == TeamLabResourceKind.Vm && !string.IsNullOrWhiteSpace(item.RuntimeResourceId))
                .Select(item => item.RuntimeResourceId!)
                .Concat(deterministicVmNames)
                .Concat(inventoryVms)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            assets.Where(item => item.EndpointObservation != TeamLabEndpointObservationMode.Disabled)
                .Select(item => item.TopologyKey).ToArray(),
            runtime.Networks
                .Where(item => item.Generation == runtime.Generation && item.ShardId != shard.Id)
                .Select(item => item.Cidr)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
    }

    private static string ContainerNamePrefix(int runtimeId, string assetKey)
    {
        var stableId = BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(assetKey)), 0) & int.MaxValue;
        return $"gzctf_c{stableId}_tteamlab-{runtimeId}_";
    }

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1024];
}
