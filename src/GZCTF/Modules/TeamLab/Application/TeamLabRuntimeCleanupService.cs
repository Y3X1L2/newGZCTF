using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeCleanupService(
    AppDbContext context,
    ITeamLabNodeExecutor executor,
    TeamLabTrafficApplicationService traffic,
    FleetCapacityReservationService capacity,
    IPublicUdpGatewayProvider publicGateway)
{
    public async Task<TeamLabNodeResult> CleanupAsync(
        TeamLabRuntime runtime,
        bool releaseActiveCapacity,
        CancellationToken cancellationToken)
    {
        var generation = runtime.Generation;
        var shards = runtime.Shards.Where(item => item.Generation == generation).ToArray();
        await traffic.StopCollectorsAsync(runtime, cancellationToken);
        var results = await Task.WhenAll(shards.Select(shard => executor.CleanupShardAsync(
            shard.WorkerNodeId,
            BuildCleanupRequest(runtime, shard),
            cancellationToken)));
        var errors = results.Where(item => !item.Success).Select(item => item.Message).ToList();
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
            runtime.Events.Add(Event(runtime, "cleanup", TeamLabEventLevel.Error, runtime.LastError));
            await context.SaveChangesAsync(cancellationToken);
            return TeamLabNodeResult.Failed(runtime.LastError);
        }

        if (releaseActiveCapacity)
        {
            foreach (var shard in shards)
            {
                var docker = runtime.Assets.Count(item => item.Generation == generation && item.ShardId == shard.Id && item.Kind == TeamLabResourceKind.Docker);
                var vm = runtime.Assets.Count(item => item.Generation == generation && item.ShardId == shard.Id && item.Kind == TeamLabResourceKind.Vm);
                await capacity.ReleaseActiveAsync(shard.WorkerNodeId, docker, vm, cancellationToken);
            }
        }

        foreach (var shard in shards)
        {
            shard.Status = TeamLabRuntimeStatus.Destroyed;
            shard.LastError = null;
            shard.UpdatedAt = DateTimeOffset.UtcNow;
        }
        foreach (var asset in runtime.Assets.Where(item => item.Generation == generation))
        {
            asset.Status = TeamLabRuntimeStatus.Destroyed;
            asset.LastError = null;
        }
        foreach (var grant in runtime.AccessGrants.Where(item => item.Generation == generation && !item.Revoked))
        {
            grant.Revoked = true;
            grant.RevokedAt = DateTimeOffset.UtcNow;
        }
        foreach (var peer in runtime.VpnPeers.Where(item => !item.Revoked)) peer.Revoked = true;
        foreach (var capture in runtime.TrafficCaptureJobs.Where(item => item.Generation == generation &&
                                                                         item.Status is TeamLabTrafficCaptureStatus.Pending or TeamLabTrafficCaptureStatus.Running or TeamLabTrafficCaptureStatus.Stopping))
        {
            capture.Status = TeamLabTrafficCaptureStatus.Failed;
            capture.LastError = "Runtime cleanup stopped the capture.";
            capture.CompletedAt = DateTimeOffset.UtcNow;
        }
        var leases = await context.TeamLabNetworkLeases
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == generation && item.ReleasedAt == null)
            .ToArrayAsync(cancellationToken);
        foreach (var lease in leases) lease.ReleasedAt = DateTimeOffset.UtcNow;
        runtime.IsOpenToPlayers = false;
        runtime.LastError = null;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return TeamLabNodeResult.Ok("Runtime resources cleaned.");
    }

    private static TeamLabNodeCleanupRequest BuildCleanupRequest(TeamLabRuntime runtime, TeamLabRuntimeShard shard)
    {
        var assets = runtime.Assets.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id).ToArray();
        var resourceNames = runtime.Networks.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
            .Select(item => item.BridgeName)
            .Append(TeamLabRouteApplicationService.RouterName(runtime.Id, shard.Id))
            .Append(TeamLabRouteApplicationService.WireGuardName(runtime.Id))
            .Concat(runtime.Networks.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
                .Select(item => TeamLabRouteApplicationService.LinuxName($"tld{runtime.Id}-{item.TopologyKey}")))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new TeamLabNodeCleanupRequest(
            runtime.Id,
            runtime.Generation,
            resourceNames,
            assets.Where(item => item.Kind == TeamLabResourceKind.Docker && !string.IsNullOrWhiteSpace(item.RuntimeResourceId))
                .Select(item => item.RuntimeResourceId!).ToArray(),
            assets.Where(item => item.Kind == TeamLabResourceKind.Vm && !string.IsNullOrWhiteSpace(item.RuntimeResourceId))
                .Select(item => item.RuntimeResourceId!).ToArray());
    }

    private static TeamLabEvent Event(TeamLabRuntime runtime, string stage, TeamLabEventLevel level, string message) => new()
    {
        RuntimeId = runtime.Id,
        Generation = runtime.Generation,
        Stage = stage,
        Level = level,
        Message = Trim(message),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1024];
}
