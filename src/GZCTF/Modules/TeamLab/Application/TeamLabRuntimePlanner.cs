using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimePlanner(
    AppDbContext context,
    TeamLabRuntimeOverlayService overlayService,
    TeamLabEventRecorder eventRecorder)
{
    public async Task<TeamLabRuntimeCreateResult> CreateAsync(
        CreateTeamLabRuntimeModel command,
        Guid runtimeOwnerUserId,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .Include(item => item.Topology)
            .SingleOrDefaultAsync(item => item.Id == command.ReleaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "The topology release was not found.", 404);
        if (release.Topology.OwnerUserId != runtimeOwnerUserId)
            throw new TeamLabApiContractException(
                "insufficient_permission",
                "The topology release is not owned by the runtime owner.",
                403);

        var externalReference = NormalizeExternalReference(command.ExternalReference);
        if (externalReference is not null)
        {
            var existing = await context.TeamLabRuntimes.AsNoTracking()
                .SingleOrDefaultAsync(item => item.CreatedById == runtimeOwnerUserId && item.ExternalReference == externalReference,
                    cancellationToken);
            if (existing is not null)
            {
                if (existing.Status == TeamLabRuntimeStatus.Destroyed)
                    return await ResetAsync(
                        existing.PublicId,
                        command.Overlays,
                        command.ReleaseId,
                        requestHash,
                        cancellationToken);
                if (!string.Equals(existing.CreateRequestHash, requestHash, StringComparison.Ordinal))
                    throw new TeamLabApiContractException(
                        "external_reference_conflict",
                        "The external reference is already used by a different runtime request.",
                        409);
                return new TeamLabRuntimeCreateResult(existing.Id, existing.PublicId, true);
            }
        }

        var definition = TeamLabReleaseCodec.Decode(release.CanonicalJson);
        await TeamLabTopologyApplicationService.ValidateImageTemplatesAsync(context, definition, cancellationToken);
        var topologyNetworks = await context.TeamLabTopologyNetworks.AsNoTracking()
            .Where(item => item.TopologyId == release.TopologyId)
            .ToDictionaryAsync(item => item.Key, StringComparer.Ordinal, cancellationToken);
        if (topologyNetworks.Count != definition.Networks.Count)
            throw new TeamLabApiContractException("release_invalid", "The release network catalog is incomplete.", 500);

        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var runtime = new TeamLabRuntime
            {
                TopologyReleaseId = release.Id,
                CreatedById = runtimeOwnerUserId,
                ExternalReference = externalReference,
                CreateRequestHash = requestHash,
                Generation = 1,
                Status = TeamLabRuntimeStatus.Planning,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            context.TeamLabRuntimes.Add(runtime);
            await context.SaveChangesAsync(cancellationToken);

            await PlanGenerationAsync(runtime, definition, topologyNetworks, command.Overlays, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TeamLabRuntimeCreateResult(runtime.Id, runtime.PublicId, false);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            throw new TeamLabApiContractException(
                "address_pool_exhausted", "Concurrent address allocation exhausted an address pool.", 409);
        }
    }

    public async Task<TeamLabRuntimeCreateResult> ResetAsync(
        Guid runtimePublicId,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? runtimeOverlays,
        Guid? targetReleaseId,
        string? createRequestHash,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes
            .Include(item => item.PublicUdpMapping)
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .Include(item => item.SecretEnvelopes)
            .Include(item => item.Events)
            .SingleOrDefaultAsync(item => item.PublicId == runtimePublicId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);
        if (runtime.Status != TeamLabRuntimeStatus.Destroyed)
            throw new TeamLabApiContractException("runtime_cleanup_pending", "The current runtime generation is not fully cleaned.", 409);
        var releaseId = targetReleaseId ?? runtime.TopologyReleaseId;
        if (releaseId == Guid.Empty)
            throw new TeamLabApiContractException("runtime_invalid", "The runtime has no topology release.", 500);
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .Include(item => item.Topology)
            .SingleOrDefaultAsync(item => item.Id == releaseId, cancellationToken)
            ?? throw new TeamLabApiContractException("release_not_found", "The topology release was not found.", 404);
        if (release.Topology.OwnerUserId != runtime.CreatedById)
            throw new TeamLabApiContractException(
                "insufficient_permission",
                "The target topology release is not owned by the runtime owner.",
                403);
        var definition = TeamLabReleaseCodec.Decode(release.CanonicalJson);
        await TeamLabTopologyApplicationService.ValidateImageTemplatesAsync(context, definition, cancellationToken);
        var topologyNetworks = await context.TeamLabTopologyNetworks.AsNoTracking()
            .Where(item => item.TopologyId == release.TopologyId)
            .ToDictionaryAsync(item => item.Key, StringComparer.Ordinal, cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        runtime.Generation++;
        runtime.TopologyReleaseId = release.Id;
        if (createRequestHash is not null) runtime.CreateRequestHash = createRequestHash;
        runtime.EntryShardId = null;
        runtime.Status = TeamLabRuntimeStatus.Planning;
        runtime.LastError = null;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        await PlanGenerationAsync(runtime, definition, topologyNetworks, runtimeOverlays, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TeamLabRuntimeCreateResult(runtime.Id, runtime.PublicId, false);
    }

    private async Task PlanGenerationAsync(
        TeamLabRuntime runtime,
        TeamLabTopologyDefinitionModel definition,
        IReadOnlyDictionary<string, TeamLabTopologyNetwork> topologyNetworks,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? runtimeOverlays,
        CancellationToken cancellationToken)
    {
        using var activity = PlatformTelemetry.TeamLabActivitySource.StartActivity(
            "teamlab.plan", ActivityKind.Internal);
        activity?.SetTag("gzctf.teamlab_runtime_id", runtime.Id);
        activity?.SetTag("teamlab.generation", runtime.Generation);
        eventRecorder.Record(
            runtime,
            "planning",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.PlanStarted,
            OperationalEventOutcome.Started,
            "TeamLab runtime planning started.");
        var groups = TeamLabAssetPlanner.BuildGroups(definition);
        var groupByNetwork = groups.SelectMany(group => group.NetworkKeys.Select(key => (key, group.Key)))
            .ToDictionary(item => item.key, item => item.Key, StringComparer.Ordinal);
        var groupByAsset = groups.SelectMany(group => group.AssetKeys.Select(key => (key, group.Key)))
            .ToDictionary(item => item.key, item => item.Key, StringComparer.Ordinal);
        var usedCidrs = await context.TeamLabNetworkLeases.AsNoTracking()
            .Where(item => item.ReleasedAt == null)
            .Select(item => item.AllocatedCidr)
            .ToArrayAsync(cancellationToken);
        var allocated = new List<IPNetwork>(definition.Networks.Count);
        var runtimeNetworkByKey = new Dictionary<string, TeamLabRuntimeNetwork>(StringComparer.Ordinal);
        foreach (var network in definition.Networks.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var cidr = Allocate(network.AddressPool.PoolCidr, network.AddressPool.RuntimePrefixLength, usedCidrs.Concat(allocated));
            if (cidr is null)
                throw new TeamLabApiContractException("address_pool_exhausted", $"Address pool for network '{network.Key}' is exhausted.", 409);
            allocated.Add(cidr.Value);
            var runtimeNetwork = new TeamLabRuntimeNetwork
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                PlacementGroupKey = groupByNetwork[network.Key],
                IsEntry = network.IsEntry,
                TopologyKey = network.Key,
                Name = network.Name,
                Cidr = cidr.Value.ToString(),
                GatewayIp = HostAt(cidr.Value, 1),
                BridgeName = LinuxName($"tl{runtime.Id}-{network.Key}"),
                NetworkLease = new TeamLabNetworkLease
                {
                    RuntimeId = runtime.Id,
                    Generation = runtime.Generation,
                    TopologyNetworkId = topologyNetworks[network.Key].Id,
                    AllocatedCidr = cidr.Value
                }
            };
            runtime.Networks.Add(runtimeNetwork);
            runtimeNetworkByKey[network.Key] = runtimeNetwork;
        }
        foreach (var asset in definition.Assets.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var interfaces = asset.Interfaces.OrderBy(item => item.OrderIndex).Select(iface =>
            {
                var network = runtimeNetworkByKey[iface.NetworkKey];
                var parsed = ParseNetwork(network.Cidr);
                return new RuntimeInterfaceIntent(iface.Key, iface.NetworkKey, HostAt(parsed, iface.HostOffset),
                    parsed.PrefixLength, MacAddress(runtime.Id, asset.Key, iface.Key), iface.Primary);
            }).ToArray();
            var primary = interfaces.Single(item => item.Primary);
            runtime.Assets.Add(new TeamLabRuntimeAsset
            {
                RuntimeId = runtime.Id,
                Generation = runtime.Generation,
                PlacementGroupKey = groupByAsset[asset.Key],
                Kind = asset.Kind == TeamLabAssetKind.Docker ? TeamLabResourceKind.Docker : TeamLabResourceKind.Vm,
                TopologyKey = asset.Key,
                Name = asset.Name,
                SourceTemplateId = asset.ImageTemplateId,
                NetworkKey = primary.NetworkKey,
                IpAddress = primary.IpAddress,
                MacAddress = primary.MacAddress,
                InterfaceSummaryJson = JsonSerializer.Serialize(interfaces),
                Status = TeamLabRuntimeStatus.Pending
            });
        }
        var envelope = overlayService.Protect(runtime.Id, runtime.Generation, runtimeOverlays,
            definition.Assets.Select(item => item.Key).ToHashSet(StringComparer.Ordinal));
        if (envelope is not null) runtime.SecretEnvelopes.Add(envelope);
        runtime.Status = TeamLabRuntimeStatus.Scheduled;
        eventRecorder.Record(
            runtime,
            "planning",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.PlanSucceeded,
            OperationalEventOutcome.Succeeded,
            $"Runtime generation {runtime.Generation} logical network groups compiled; physical nodes will be assigned by the scheduler.");
        await context.SaveChangesAsync(cancellationToken);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    private static IPNetwork? Allocate(string poolCidr, int runtimePrefix, IEnumerable<IPNetwork> unavailable)
    {
        var pool = ParseNetwork(poolCidr);
        var used = unavailable.Select(ToRange).ToArray();
        var poolRange = ToRange(pool);
        var size = 1u << (32 - runtimePrefix);
        for (var start = poolRange.Start; start <= poolRange.End - size + 1; start += size)
        {
            var end = start + size - 1;
            if (used.All(item => end < item.Start || item.End < start))
                return new IPNetwork(FromUInt32(start), runtimePrefix);
            if (uint.MaxValue - start < size) break;
        }
        return null;
    }

    private static IPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork || !int.TryParse(parts[1], out var prefix))
            throw new TeamLabApiContractException("release_invalid", $"CIDR '{cidr}' is invalid.", 500);
        return new IPNetwork(address, prefix);
    }

    private static (uint Start, uint End) ToRange(IPNetwork network)
    {
        var start = ToUInt32(network.BaseAddress);
        var size = 1u << (32 - network.PrefixLength);
        return (start, start + size - 1);
    }

    private static string HostAt(IPNetwork network, int offset)
    {
        var range = ToRange(network);
        if (offset <= 0 || (uint)offset >= range.End - range.Start)
            throw new TeamLabApiContractException("topology_invalid", "A host offset is outside its runtime network.", 422);
        return FromUInt32(range.Start + (uint)offset).ToString();
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new([
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    ]);

    private static string MacAddress(int runtimeId, string assetKey, string interfaceKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{runtimeId}:{assetKey}:{interfaceKey}"));
        return $"02:42:{hash[0]:x2}:{hash[1]:x2}:{hash[2]:x2}:{hash[3]:x2}";
    }

    private static string LinuxName(string value) => value.Length <= 15 ? value : value[..15];

    private static string? NormalizeExternalReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 256)
            throw new TeamLabApiContractException("topology_invalid", "External reference cannot exceed 256 characters.", 422);
        return normalized;
    }

    private sealed record RuntimeInterfaceIntent(
        string Key,
        string NetworkKey,
        string IpAddress,
        int PrefixLength,
        string MacAddress,
        bool Primary);
}
