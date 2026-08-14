using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application.Validation;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRouteApplicationService(
    AppDbContext context,
    ITeamLabNodeExecutor executor,
    TeamLabEventRecorder eventRecorder)
{
    public async Task ApplyAsync(
        TeamLabRuntime runtime,
        TeamLabExecutionTopology definition,
        CancellationToken cancellationToken)
    {
        var shards = runtime.Shards.Where(item => item.Generation == runtime.Generation)
            .OrderBy(item => item.WorkerNodeId).ToArray();
        if (shards.Length == 0)
            throw new TeamLabRuntimeExecutionException("Runtime has no current shard.");
        var nodeIds = shards.Select(item => item.WorkerNodeId).ToArray();
        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(item => nodeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var links = await context.TeamLabFabricLinkLeases.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation &&
                           item.ReleasedAt == null)
            .ToDictionaryAsync(item => item.ShardId, cancellationToken);
        if (links.Count != shards.Length)
            throw new TeamLabRuntimeExecutionException("Runtime Fabric link leases are incomplete.");
        var templateIds = runtime.Assets
            .Where(asset => asset.Generation == runtime.Generation && asset.SourceTemplateId.HasValue)
            .Select(asset => asset.SourceTemplateId!.Value)
            .Distinct()
            .ToArray();
        var networkModes = await context.ImageTemplates.AsNoTracking()
            .Where(template => templateIds.Contains(template.Id))
            .ToDictionaryAsync(template => template.Id, template => template.VmNetworkMode, cancellationToken);
        eventRecorder.Record(
            runtime,
            "infrastructure",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.InfrastructureApplyStarted,
            OperationalEventOutcome.Started,
            "Applying TeamLab infrastructure desired state.",
            detail: InfrastructureDetail(runtime, shards.Length));
        await context.SaveChangesAsync(cancellationToken);
        var allowedPairs = TeamLabReachabilityCompiler.Compile(definition);
        var routedPairs = TeamLabReachabilityCompiler.CompileRouting(definition);
        TeamLabNodeInfrastructureResult[] results;
        try
        {
            var tasks = shards.Select(shard => ApplyShardInfrastructureAsync(
                runtime, shard, links[shard.Id], shards, nodes, networkModes,
                allowedPairs, routedPairs, cancellationToken));
            results = await Task.WhenAll(tasks);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            eventRecorder.Record(
                runtime,
                "infrastructure",
                TeamLabEventLevel.Error,
                OperationalEventCodes.TeamLab.InfrastructureApplyFailed,
                OperationalEventOutcome.Failed,
                "TeamLab infrastructure desired-state application failed.",
                OperationalErrorClassifier.FromException(exception, "teamlab.infrastructure.apply"),
                detail: InfrastructureDetail(runtime, shards.Length));
            PlatformTelemetry.RecordTeamLabInfrastructure("failure", "mixed");
            await context.SaveChangesAsync(cancellationToken);
            throw;
        }
        var error = results.FirstOrDefault(item => !item.Success);
        if (error is not null)
        {
            eventRecorder.Record(
                runtime,
                "infrastructure",
                TeamLabEventLevel.Error,
                OperationalEventCodes.TeamLab.InfrastructureApplyFailed,
                OperationalEventOutcome.Failed,
                "TeamLab infrastructure desired-state application failed.",
                new OperationalError(
                    OperationalErrorCategory.Network,
                    OperationalErrorCodes.NetworkOperationFailed,
                    "A WorkerNode rejected the infrastructure desired state.",
                    true,
                    Operation: "teamlab.infrastructure.apply"),
                detail: InfrastructureDetail(runtime, shards.Length));
            PlatformTelemetry.RecordTeamLabInfrastructure("failure", "mixed");
            await context.SaveChangesAsync(cancellationToken);
            throw new TeamLabRuntimeExecutionException(error.Message);
        }
        foreach (var (shard, result) in shards.Zip(results))
        {
            shard.RouteVersion = runtime.Generation;
            shard.UpdatedAt = DateTimeOffset.UtcNow;
            foreach (var fragment in runtime.Infrastructure
                         .Where(item => item.Generation == runtime.Generation)
                         .SelectMany(item => item.Fragments)
                         .Where(item => item.ShardId == shard.Id))
            {
                var infrastructureKey = fragment.Infrastructure.Kind == TeamLabInfrastructureKind.ManagedSwitch
                    ? fragment.Infrastructure.NetworkKey
                    : fragment.Infrastructure.TopologyKey;
                var resource = result.Resources.SingleOrDefault(item =>
                    item.Kind == (fragment.Infrastructure.Kind == TeamLabInfrastructureKind.ManagedSwitch
                        ? "managed-switch"
                        : "managed-router-fragment") &&
                    string.Equals(item.Key, infrastructureKey, StringComparison.Ordinal));
                fragment.Status = TeamLabRuntimeStatus.Running;
                fragment.NativeResourceId = resource?.NativeIdentity;
                fragment.DesiredStateDigest = result.DesiredStateDigest;
                fragment.LastError = null;
                fragment.UpdatedAt = DateTimeOffset.UtcNow;
            }
            foreach (var observation in runtime.ObservationPoints
                         .Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id))
            {
                var resource = result.Resources.SingleOrDefault(item =>
                    item.Kind == "observation-point" &&
                    string.Equals(item.Key, observation.PublicId.ToString("D"), StringComparison.Ordinal));
                if (resource is not null)
                    observation.InterfaceToken = resource.NativeIdentity;
                observation.DesiredStateDigest = result.DesiredStateDigest;
                observation.LastError = null;
                observation.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        var replayed = results.All(item => item.AlreadyApplied);
        eventRecorder.Record(
            runtime,
            "infrastructure",
            TeamLabEventLevel.Success,
            replayed
                ? OperationalEventCodes.TeamLab.InfrastructureReplayed
                : OperationalEventCodes.TeamLab.InfrastructureApplied,
            replayed ? OperationalEventOutcome.Recovered : OperationalEventOutcome.Succeeded,
            replayed
                ? "TeamLab infrastructure already matched the persisted desired state."
                : "TeamLab infrastructure desired state was applied.",
            detail: InfrastructureDetail(runtime, shards.Length));
        foreach (var kind in runtime.Infrastructure.Where(item => item.Generation == runtime.Generation)
                     .Select(item => item.Kind.ToString()).Distinct(StringComparer.Ordinal))
            PlatformTelemetry.RecordTeamLabInfrastructure(replayed ? "replayed" : "success", kind);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, TeamLabNodeInfrastructureApplyRequest>>
        BuildInfrastructureRequestsAsync(
            TeamLabRuntime runtime,
            TeamLabExecutionTopology definition,
            CancellationToken cancellationToken)
    {
        var shards = runtime.Shards.Where(item => item.Generation == runtime.Generation)
            .OrderBy(item => item.WorkerNodeId).ToArray();
        if (shards.Length == 0)
            throw new TeamLabRuntimeExecutionException("Runtime has no current shard.");
        var nodeIds = shards.Select(item => item.WorkerNodeId).ToArray();
        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(item => nodeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var links = await context.TeamLabFabricLinkLeases.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation &&
                           item.ReleasedAt == null)
            .ToDictionaryAsync(item => item.ShardId, cancellationToken);
        if (links.Count != shards.Length)
            throw new TeamLabRuntimeExecutionException("Runtime Fabric link leases are incomplete.");
        var templateIds = runtime.Assets
            .Where(asset => asset.Generation == runtime.Generation && asset.SourceTemplateId.HasValue)
            .Select(asset => asset.SourceTemplateId!.Value)
            .Distinct()
            .ToArray();
        var networkModes = await context.ImageTemplates.AsNoTracking()
            .Where(template => templateIds.Contains(template.Id))
            .ToDictionaryAsync(item => item.Id, item => item.VmNetworkMode, cancellationToken);
        var allowedPairs = TeamLabReachabilityCompiler.Compile(definition);
        var routedPairs = TeamLabReachabilityCompiler.CompileRouting(definition);
        var requests = new Dictionary<int, TeamLabNodeInfrastructureApplyRequest>();
        foreach (var shard in shards)
        {
            TeamLabNodeInfrastructureApplyRequest? request = null;
            var result = await ApplyShardInfrastructureAsync(
                runtime, shard, links[shard.Id], shards, nodes, networkModes,
                allowedPairs, routedPairs, cancellationToken, execute: false,
                requestBuilt: built => request = built);
            if (!result.Success || request is null)
                throw new TeamLabRuntimeExecutionException(result.Message);
            requests.Add(shard.Id, request);
        }
        return requests;
    }

    private async Task<TeamLabNodeInfrastructureResult> ApplyShardInfrastructureAsync(
        TeamLabRuntime runtime,
        TeamLabRuntimeShard shard,
        TeamLabFabricLinkLease link,
        IReadOnlyList<TeamLabRuntimeShard> shards,
        IReadOnlyDictionary<Guid, WorkerNode> nodes,
        IReadOnlyDictionary<int, VmNetworkMode> networkModes,
        IReadOnlySet<string> allowedPairs,
        IReadOnlySet<string> routedPairs,
        CancellationToken cancellationToken,
        bool execute = true,
        Action<TeamLabNodeInfrastructureApplyRequest>? requestBuilt = null)
    {
        if (!nodes.TryGetValue(shard.WorkerNodeId, out var node))
            return TeamLabNodeInfrastructureResult.Failed($"WorkerNode {shard.WorkerNodeId} was not found.");
        var fabricIp = node.TeamLabFabricIp;
        if (string.IsNullOrWhiteSpace(fabricIp))
            return TeamLabNodeInfrastructureResult.Failed($"WorkerNode '{node.Name}' has no Fabric IP.");
        var networks = runtime.Networks.Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id)
            .OrderBy(item => item.TopologyKey, StringComparer.Ordinal).ToArray();
        var allNetworks = runtime.Networks.Where(item => item.Generation == runtime.Generation)
            .OrderBy(item => item.TopologyKey, StringComparer.Ordinal).ToArray();
        var remoteRoutes = new List<TeamLabNodeRouteIntent>();
        foreach (var source in networks)
        foreach (var target in allNetworks.Where(item => item.ShardId != shard.Id))
        {
            if (!routedPairs.Contains(TeamLabReachabilityCompiler.Pair(source.TopologyKey, target.TopologyKey))) continue;
            var remoteShard = shards.Single(item => item.Id == target.ShardId);
            var remoteNode = nodes[remoteShard.WorkerNodeId];
            var gateway = remoteNode.TeamLabFabricIp;
            if (!string.IsNullOrWhiteSpace(gateway))
                remoteRoutes.Add(new TeamLabNodeRouteIntent(target.Cidr, gateway, source.GatewayIp));
        }
        var localRoutes = networks.Select(item => new TeamLabNodeRouteIntent(
            item.Cidr, link.NodeAddress)).ToArray();
        var policies = BuildForwardPolicies(allNetworks, shard.Id, allowedPairs);
        var dnsRecords = runtime.Assets
            .Where(asset => asset.Generation == runtime.Generation)
            .SelectMany(asset => ParseInterfaces(asset)
                .Select(iface => new TeamLabNodeDnsRecord(
                    asset.TopologyKey, iface.IpAddress, iface.MacAddress, iface.Primary)))
            .GroupBy(item => (item.Hostname, item.IpAddress))
            .Select(group => group.First())
            .OrderBy(item => item.Hostname, StringComparer.Ordinal)
            .ThenBy(item => item.IpAddress, StringComparer.Ordinal)
            .ToArray();
        var recordsByNetwork = networks.ToDictionary(
            network => network.TopologyKey,
            network => (IReadOnlyList<TeamLabNodeDnsRecord>)runtime.Assets
                .Where(asset => asset.Generation == runtime.Generation &&
                                (asset.Kind != TeamLabResourceKind.Vm ||
                                 !asset.SourceTemplateId.HasValue ||
                                 networkModes.GetValueOrDefault(asset.SourceTemplateId.Value) !=
                                 VmNetworkMode.Preconfigured))
                .SelectMany(asset => ParseInterfaces(asset)
                    .Where(iface => string.Equals(iface.NetworkKey, network.TopologyKey, StringComparison.Ordinal))
                    .Select(iface => new TeamLabNodeDnsRecord(
                        asset.TopologyKey, iface.IpAddress, iface.MacAddress, iface.Primary)))
                .OrderBy(item => item.Hostname, StringComparer.Ordinal)
                .ThenBy(item => item.IpAddress, StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        var switches = networks.Select(network => new TeamLabNodeManagedSwitchIntent(
            new TeamLabNodeNetworkIntent(
                network.TopologyKey,
                network.Name,
                network.Cidr,
                network.GatewayIp,
                network.BridgeName),
            TeamLabResourceNameFactory.DhcpDnsService(runtime.Id, network.TopologyKey),
            recordsByNetwork[network.TopologyKey],
            dnsRecords)).ToArray();
        var routers = runtime.Infrastructure
            .Where(item => item.Generation == runtime.Generation && item.Kind == TeamLabInfrastructureKind.ManagedRouter)
            .SelectMany(item => item.Fragments
                .Where(fragment => fragment.ShardId == shard.Id)
                .Select(fragment => new TeamLabNodeManagedRouterFragmentIntent(
                    item.TopologyKey,
                    Deserialize<TeamLabRuntimeInfrastructureInterfaceIntent>(fragment.InterfaceSummaryJson)
                        .Select(iface => iface.NetworkKey)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(key => key, StringComparer.Ordinal)
                        .ToArray())))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        var observations = runtime.ObservationPoints
            .Where(item => item.Generation == runtime.Generation && item.ShardId == shard.Id && item.Enabled)
            .OrderBy(item => item.PublicId)
            .Select(item => new TeamLabNodeObservationPointIntent(
                item.PublicId, item.TopologyKey, item.Kind, item.InterfaceToken))
            .ToArray();
        var request = new TeamLabNodeInfrastructureApplyRequest(
                runtime.Id,
                runtime.Generation,
                runtime.Generation,
                TeamLabResourceNameFactory.RouterNamespace(runtime.Id, shard.Id),
                switches,
                routers,
                new TeamLabNodeFabricIntent(
                    fabricIp,
                    $"{link.HubAddress}/30",
                    $"{link.NodeAddress}/30",
                    TeamLabResourceNameFactory.FabricHostInterface(runtime.Id),
                    TeamLabResourceNameFactory.FabricNamespaceInterface(runtime.Id),
                    localRoutes,
                    remoteRoutes),
                policies,
                observations);
        requestBuilt?.Invoke(request);
        return execute
            ? await executor.ApplyInfrastructureAsync(shard.WorkerNodeId, request, cancellationToken)
            : TeamLabNodeInfrastructureResult.Applied("Infrastructure request compiled.");
    }

    private static T[] Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T[]>(json) ?? [];

    internal static TeamLabNodeForwardPolicy[] BuildForwardPolicies(
        IReadOnlyList<TeamLabRuntimeNetwork> allNetworks,
        int shardId,
        IReadOnlySet<string> allowedPairs) => allNetworks
        .SelectMany(source => allNetworks
            .Where(target => target.Id != source.Id &&
                             (source.ShardId == shardId || target.ShardId == shardId))
            .Select(target => new TeamLabNodeForwardPolicy(
                source.Cidr,
                target.Cidr,
                allowedPairs.Contains(TeamLabReachabilityCompiler.Pair(source.TopologyKey, target.TopologyKey)))))
        .ToArray();

    private static RuntimeInterfaceIntent[] ParseInterfaces(TeamLabRuntimeAsset asset) =>
        JsonSerializer.Deserialize<RuntimeInterfaceIntent[]>(asset.InterfaceSummaryJson) ?? [];

    private static IReadOnlyDictionary<string, object?> InfrastructureDetail(
        TeamLabRuntime runtime,
        int shardCount) => new Dictionary<string, object?>
    {
        ["generation"] = runtime.Generation,
        ["stage"] = "infrastructure",
        ["shardCount"] = shardCount,
        ["infrastructureCount"] = runtime.Infrastructure.Count(item => item.Generation == runtime.Generation)
    };

    private sealed record RuntimeInterfaceIntent(
        string Key,
        string NetworkKey,
        string IpAddress,
        int PrefixLength,
        string MacAddress,
        bool Primary);
}

public class TeamLabRuntimeExecutionException(string message) : Exception(message);

public sealed class TeamLabRuntimeIdentityConflictException(string message)
    : TeamLabRuntimeExecutionException(message), IOperationalFailureException
{
    public OperationalError Error { get; } = new(
        OperationalErrorCategory.Validation,
        OperationalErrorCodes.RuntimeIdentityConflict,
        "The execution identity conflicts with an existing runtime generation.",
        false,
        Operation: "teamlab.execution-plan");
}
