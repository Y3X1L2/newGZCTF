using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabDeploymentResult(
    bool Success,
    string Message,
    [property: JsonIgnore] TeamLabRuntime? Runtime,
    DeploymentQueueStatusModel? Queue = null)
{
    [JsonPropertyName("runtime")]
    public TeamLabRuntimeModel? RuntimeModel => TeamLabRuntimeModel.FromRuntime(Runtime);
}
public sealed record TeamLabResourceNames(string[] Bridges, string RouterNamespace, string WireGuardInterface);
public sealed record TeamLabNativeAssetCleanupPlan(string[] ContainerIds, string[] VmNames);
public sealed record TeamLabRuntimeRouteMatrix(
    IReadOnlyDictionary<string, string[]> AllowedCidrsByNetworkKey)
{
    public static TeamLabRuntimeRouteMatrix Empty { get; } =
        new(new Dictionary<string, string[]>(StringComparer.Ordinal));
}
public sealed record TeamLabPlayerNetworkAccess(string[] AllowedCidrs, string[] BlockedCidrs);
public sealed record TeamLabAssetSlotCount(int DockerSlots, int VmSlots);
public sealed record TeamLabRuntimeAssetSpec(
    TeamLabResourceKind Kind,
    string TopologyKey,
    string Name,
    string? RuntimeResourceId,
    int? SourceTemplateId,
    string? Image,
    string? NetworkKey,
    string? IpAddress,
    string? MacAddress,
    string InterfaceSummaryJson = "[]");
public sealed record TeamLabVmReadyValidationResult(bool Success, string Message);
public sealed record TeamLabShardSlotCount(Guid WorkerNodeId, int DockerSlots, int VmSlots);

public enum TeamLabDeploymentMode
{
    NativePublishedTopology
}

public class TeamLabDeploymentService(
    AppDbContext context,
    TeamLabPlanService planService,
    AgentClient agentClient,
    DockerImageRegistryService dockerRegistry,
    TeamLabWireGuardService wireGuardService,
    TeamLabTrafficFlowService trafficFlowService,
    IPublicUdpGatewayProvider publicUdpGatewayProvider,
    IOptions<TeamLabNetworkConfig> options,
    FleetCapacityReservationService capacityReservation,
    DeploymentQueueService deploymentQueue,
    ILogger<TeamLabDeploymentService> logger)
{
    private sealed record TeamLabNativeAssetCreationResult(
        TeamLabRuntimeAssetSpec RuntimeAsset,
        string? ContainerId,
        string? VmName);

    private sealed record TeamLabRuntimeShardDeployment(
        TeamLabRuntimeShard Shard,
        Guid WorkerNodeId,
        TeamLabResourceNames Names,
        IReadOnlyList<TeamLabRuntimeNetworkSpec> Networks,
        IReadOnlyList<TeamLabAssetSpec> Assets);

    private sealed record TeamLabCreatedShardAssets(Guid WorkerNodeId, List<string> Containers, List<string> Vms);
    private sealed record TeamLabShardFabricRoutePlan(
        TeamLabRuntimeShardDeployment Shard,
        TeamLabStaticRouteRequest[] Routes);

    private const int RuntimeErrorMaxLength = 1024;
    private const int EventMessageMaxLength = 256;
    private const int EventDetailMaxLength = 1024;
    private const int NativeVmReadyProbeAttempts = 24;
    private const int NativeRuntimeConnectivityProbeAttempts = 12;
    private static readonly TimeSpan NativeVmReadyProbeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NativeRuntimeConnectivityProbeInterval = TimeSpan.FromSeconds(5);
    private readonly TeamLabNetworkConfig _config = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static TeamLabResourceNames BuildResourceNames(int runtimeId, IReadOnlyList<string> networkKeys)
    {
        var prefix = TeamLabPlanService.BuildRuntimeResourcePrefix(runtimeId);
        var bridges = networkKeys.Select(key => TrimLinuxName($"{prefix}-{key}")).ToArray();
        return new TeamLabResourceNames(
            bridges,
            TrimLinuxName($"tlr{runtimeId}"),
            TrimLinuxName($"tlwg{runtimeId}"));
    }

    public static string[] BuildNativeCleanupResourceNames(TeamLabRuntime runtime)
    {
        var names = BuildResourceNames(runtime.Id, []);
        var networkResources = runtime.Networks
            .Select(network => network.BridgeName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Concat(runtime.Assets
                .Where(asset => asset.Kind == TeamLabResourceKind.DhcpDnsService)
                .Select(asset => asset.RuntimeResourceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>())
            .Append(names.RouterNamespace)
            .Append(names.WireGuardInterface)
            .Append(BuildFabricUplinkHostInterfaceName(runtime.Id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return networkResources;
    }

    private static IReadOnlyList<TeamLabRuntimeShardDeployment> BuildShardDeployments(TeamLabRuntime runtime,
        TeamLabPublishedAssetPlanResult plan)
    {
        var networksByKey = plan.Networks.ToDictionary(network => network.TopologyKey, StringComparer.Ordinal);
        var assetsByKey = plan.Assets.ToDictionary(asset => asset.TopologyKey, StringComparer.Ordinal);
        if (runtime.Shards.Count == 0)
        {
            if (runtime.WorkerNodeId is not { } nodeId)
                return [];

            var names = BuildResourceNames(runtime.Id, plan.Networks.Select(network => network.TopologyKey).ToArray());
            return
            [
                new TeamLabRuntimeShardDeployment(
                    new TeamLabRuntimeShard
                    {
                        Runtime = runtime,
                        RuntimeId = runtime.Id,
                        WorkerNodeId = nodeId,
                        Status = runtime.Status
                    },
                    nodeId,
                    names,
                    plan.Networks,
                    plan.Assets)
            ];
        }

        return runtime.Shards
            .Select(shard =>
            {
                var networkKeys = shard.Networks
                    .Select(network => network.TopologyKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var assetKeys = shard.Assets
                    .Select(asset => asset.TopologyKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal)
                    .ToHashSet(StringComparer.Ordinal);
                var networks = networkKeys
                    .Where(networksByKey.ContainsKey)
                    .Select(key => networksByKey[key])
                    .OrderBy(network => network.TopologyKey, StringComparer.Ordinal)
                    .ToArray();
                var assets = plan.Assets
                    .Where(asset => assetKeys.Contains(asset.TopologyKey) ||
                                    asset.Interfaces.Any(iface => networkKeys.Contains(iface.NetworkKey,
                                        StringComparer.Ordinal)))
                    .OrderBy(asset => asset.StartPriority)
                    .ThenBy(asset => asset.TopologyKey, StringComparer.Ordinal)
                    .ToArray();
                var names = BuildResourceNames(runtime.Id, networks.Select(network => network.TopologyKey).ToArray());
                return new TeamLabRuntimeShardDeployment(shard, shard.WorkerNodeId, names, networks, assets);
            })
            .Where(shard => shard.Networks.Count > 0)
            .OrderBy(shard => shard.Networks.Any(network => string.Equals(network.TopologyKey, "entry",
                StringComparison.Ordinal)) ? 0 : 1)
            .ThenBy(shard => shard.WorkerNodeId)
            .ToArray();
    }

    private static TeamLabRuntimeShardDeployment? FindEntryShard(
        IReadOnlyList<TeamLabRuntimeShardDeployment> shards) =>
        shards.FirstOrDefault(shard => shard.Networks.Any(network =>
            string.Equals(network.TopologyKey, "entry", StringComparison.OrdinalIgnoreCase) ||
            network.Name.Contains("entry", StringComparison.OrdinalIgnoreCase) ||
            network.Name.Contains("入口", StringComparison.Ordinal)));

    private static TeamLabRuntimeShardDeployment ResolveAssetShard(TeamLabAssetSpec asset,
        IReadOnlyList<TeamLabRuntimeShardDeployment> shards) =>
        shards.FirstOrDefault(shard => shard.Assets.Any(shardAsset =>
            string.Equals(shardAsset.TopologyKey, asset.TopologyKey, StringComparison.Ordinal))) ??
        shards.FirstOrDefault(shard => asset.Interfaces.Any(iface =>
            shard.Networks.Any(network => string.Equals(network.TopologyKey, iface.NetworkKey,
                StringComparison.Ordinal)))) ??
        shards[0];

    private static IReadOnlyList<TeamLabShardFabricRoutePlan> BuildShardFabricRoutePlans(
        IReadOnlyList<TeamLabRuntimeShardDeployment> shards, IReadOnlyDictionary<Guid, WorkerNode> workerNodes)
    {
        if (shards.Count <= 1)
            return [];

        return shards
            .Select(shard =>
            {
                var routes = shards
                    .Where(remote => remote.WorkerNodeId != shard.WorkerNodeId)
                    .SelectMany(remote =>
                    {
                        var fabricIp = ResolveWorkerFabricGateway(workerNodes, remote.WorkerNodeId);
                        var sourceIp = ResolveNamespaceRouteSourceIp(shard.Networks);
                        return string.IsNullOrWhiteSpace(fabricIp)
                            ? []
                            : remote.Networks.Select(network =>
                                new TeamLabStaticRouteRequest(network.Cidr, fabricIp, sourceIp));
                    })
                    .GroupBy(route => route.TargetCidr, StringComparer.Ordinal)
                    .Select(group => group.OrderBy(route => route.GatewayIp, StringComparer.Ordinal).First())
                    .OrderBy(route => route.TargetCidr, StringComparer.Ordinal)
                    .ToArray();
                return new TeamLabShardFabricRoutePlan(shard, routes);
            })
            .Where(plan => plan.Routes.Length > 0)
            .ToArray();
    }

    private static string? ResolveWorkerFabricGateway(IReadOnlyDictionary<Guid, WorkerNode> workerNodes,
        Guid workerNodeId)
    {
        if (!workerNodes.TryGetValue(workerNodeId, out var node))
            return null;

        return !string.IsNullOrWhiteSpace(node.TeamLabFabricIp)
            ? node.TeamLabFabricIp
            : node.TeamLabTunnelIp;
    }

    public static string[] BuildNativeProbeTargets(IReadOnlyList<TeamLabAssetSpec> assets) =>
        assets
            .Where(asset => asset.Kind != TeamLabAssetSpecKind.Vm || asset.OSType != OSType.Windows)
            .SelectMany(asset => asset.Interfaces)
            .Select(iface => iface.IpAddress)
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();

    public static bool ShouldRunNativeConnectivityProbe(IReadOnlyList<TeamLabAssetSpec> assets) =>
        assets.Any(asset => asset.Kind != TeamLabAssetSpecKind.Vm || asset.OSType != OSType.Windows);

    public static TeamLabAssetSlotCount CountAssetSlots(IReadOnlyList<TeamLabAssetSpec> assets) =>
        new(
            assets.Count(asset => asset.Kind == TeamLabAssetSpecKind.Docker),
            assets.Count(asset => asset.Kind == TeamLabAssetSpecKind.Vm));

    public static string[] BuildNativeCleanupResourceNames(TeamLabResourceNames names,
        IReadOnlyList<TeamLabRuntimeNetworkSpec> networks) =>
        networks
            .Select(network => network.BridgeName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Concat(networks.Select(network =>
                BuildDhcpDnsServiceName(ParseRuntimeId(names.RouterNamespace), network.TopologyKey)))
            .Append(names.RouterNamespace)
            .Append(names.WireGuardInterface)
            .Append(BuildFabricUplinkHostInterfaceName(ParseRuntimeId(names.RouterNamespace)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public static TeamLabNativeAssetCleanupPlan BuildNativeAssetCleanupPlan(TeamLabRuntime runtime) =>
        new(
            runtime.Assets
                .Where(asset => asset.Kind == TeamLabResourceKind.Docker)
                .Where(asset => asset.Status != TeamLabRuntimeStatus.Destroyed)
                .Select(asset => asset.RuntimeResourceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Cast<string>()
                .ToArray(),
            runtime.Assets
                .Where(asset => asset.Kind == TeamLabResourceKind.Vm)
                .Where(asset => asset.Status != TeamLabRuntimeStatus.Destroyed)
                .Select(asset => asset.RuntimeResourceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Cast<string>()
                .ToArray());

    public static bool CanOpenToPlayers(PenetrationRuntimeStatus penetrationEnvironmentStatus) =>
        penetrationEnvironmentStatus == PenetrationRuntimeStatus.Running;

    public static TeamLabDeploymentMode ResolveDeploymentMode(PenetrationTeamEnvironment? _) =>
        TeamLabDeploymentMode.NativePublishedTopology;

    public static int ResolveNativeVmReadyProbeAttempts(OSType osType) =>
        osType == OSType.Windows ? 72 : NativeVmReadyProbeAttempts;

    public static ContainerConfig BuildNativeDockerContainerConfig(TeamLabAssetSpec spec, int teamId,
        Guid workerNodeId, string? flag, IReadOnlyList<TeamLabRuntimeNetworkSpec>? networks = null)
    {
        if (spec.Kind != TeamLabAssetSpecKind.Docker)
            throw new ArgumentException("TeamLab native Docker config requires a Docker asset spec.", nameof(spec));

        var attachedNetworkKeys = spec.Interfaces
            .Select(iface => iface.NetworkKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        var dnsServers = networks?
            .Where(network => attachedNetworkKeys.Contains(network.TopologyKey))
            .Select(network => network.GatewayIp)
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];

        return new ContainerConfig
        {
            Image = spec.Image,
            TeamId = teamId.ToString(),
            ChallengeId = StableChallengeId(spec.TopologyKey),
            UserId = Guid.Empty,
            ExposedPort = spec.ExposePort,
            Flag = flag,
            CPUCount = spec.CpuCount,
            MemoryLimit = spec.MemoryLimit,
            StorageLimit = spec.StorageLimit,
            NetworkMode = NetworkMode.Custom,
            PublishPort = false,
            BypassPublicProxy = true,
            UsePenetrationFabric = false,
            UseHostNetworkNone = true,
            EnableNetworkAdmin = IsRoutingAsset(spec),
            RemoveDefaultRoute = false,
            EnableIpForwarding = IsRoutingAsset(spec),
            PreferredNodeId = workerNodeId,
            DnsServers = dnsServers
        };
    }

    public static async Task<ContainerConfig> BuildResolvedNativeDockerContainerConfigAsync(TeamLabAssetSpec spec,
        int teamId, Guid workerNodeId, string? flag, DockerImageRegistryService dockerRegistry,
        CancellationToken token, IReadOnlyList<TeamLabRuntimeNetworkSpec>? networks = null)
    {
        await Task.CompletedTask;
        var config = BuildNativeDockerContainerConfig(spec, teamId, workerNodeId, flag, networks);
        config.Image = dockerRegistry.ResolveInternalImageReferenceForConfiguredRegistry(config.Image);
        return config;
    }

    public static TeamLabRuntimeAssetSpec BuildRuntimeAssetRecord(TeamLabAssetSpec spec, string runtimeResourceId)
    {
        var primary = spec.Interfaces.FirstOrDefault(i => i.IsPrimary) ?? spec.Interfaces.FirstOrDefault();
        var kind = spec.Kind == TeamLabAssetSpecKind.Docker
            ? TeamLabResourceKind.Docker
            : TeamLabResourceKind.Vm;
        return new TeamLabRuntimeAssetSpec(
            kind,
            spec.TopologyKey,
            spec.Name,
            runtimeResourceId,
            spec.SourceTemplateId,
            spec.Image,
            primary?.NetworkKey,
            primary?.IpAddress,
            primary?.MacAddress,
            JsonSerializer.Serialize(spec.Interfaces.Select(TeamLabRuntimeInterfaceFact.FromSpec), JsonOptions));
    }

    public static bool IsRoutingAsset(TeamLabAssetSpec spec) =>
        spec.Interfaces.Select(i => i.NetworkKey).Distinct(StringComparer.Ordinal).Count() > 1 ||
        string.Equals(spec.InfrastructureRole, "Router", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(spec.InfrastructureRole, "Firewall", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(spec.InfrastructureRole, "Bastion", StringComparison.OrdinalIgnoreCase);

    public static TeamLabRuntimeRouteMatrix BuildRuntimeRouteMatrix(PenetrationConfig config,
        IReadOnlyList<TeamLabRuntimeNetworkSpec> networks, IReadOnlyList<TeamLabAssetSpec> assets)
    {
        if (config.Edges.Count == 0 || networks.Count == 0 || assets.Count == 0)
            return TeamLabRuntimeRouteMatrix.Empty;

        var networkByKey = networks.ToDictionary(n => n.TopologyKey, StringComparer.Ordinal);
        var networkKeyByNodeId = assets
            .Select(asset => new
            {
                Node = config.Nodes.FirstOrDefault(node =>
                    string.Equals(node.TopologyKey, asset.TopologyKey, StringComparison.Ordinal)),
                Primary = asset.Interfaces.FirstOrDefault(iface => iface.IsPrimary) ?? asset.Interfaces.FirstOrDefault()
            })
            .Where(item => item.Node is not null && item.Primary is not null)
            .ToDictionary(item => item.Node!.Id, item => item.Primary!.NetworkKey);
        var assetByNodeId = assets
            .Select(asset => new
            {
                Node = config.Nodes.FirstOrDefault(node =>
                    string.Equals(node.TopologyKey, asset.TopologyKey, StringComparison.Ordinal)),
                Asset = asset
            })
            .Where(item => item.Node is not null)
            .ToDictionary(item => item.Node!.Id, item => item.Asset);
        var allowed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void AddAllowed(string sourceNetworkKey, string targetNetworkKey)
        {
            if (string.Equals(sourceNetworkKey, targetNetworkKey, StringComparison.Ordinal) ||
                !networkByKey.TryGetValue(targetNetworkKey, out var targetNetwork))
                return;

            if (!allowed.TryGetValue(sourceNetworkKey, out var routes))
            {
                routes = new HashSet<string>(StringComparer.Ordinal);
                allowed[sourceNetworkKey] = routes;
            }

            routes.Add(targetNetwork.Cidr);
        }

        foreach (var asset in assets.Where(IsRoutingAsset))
        {
            var assetNetworkKeys = asset.Interfaces
                .Select(iface => iface.NetworkKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (var source in assetNetworkKeys)
            foreach (var target in assetNetworkKeys)
                AddAllowed(source, target);
        }

        foreach (var edge in config.Edges.Where(edge =>
                     edge.PolicyAction == PenetrationPolicyAction.Allow &&
                     edge.EnforcementMode is PenetrationEnforcementMode.RuntimeRoute or PenetrationEnforcementMode.Both))
        {
            var sourceNetworkKeys = ResolveEdgeNetworkKeys(config, edge.SourceKind, edge.SourceId,
                edge.SourceNodeId, networkKeyByNodeId, assetByNodeId);
            var targetNetworkKeys = ResolveEdgeNetworkKeys(config, edge.TargetKind, edge.TargetId,
                edge.TargetNodeId, networkKeyByNodeId, assetByNodeId);

            foreach (var source in sourceNetworkKeys)
            foreach (var target in targetNetworkKeys)
            {
                AddAllowed(source, target);
                AddAllowed(target, source);
            }
        }

        return new TeamLabRuntimeRouteMatrix(
            allowed.ToDictionary(
                item => item.Key,
                item => item.Value.Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal));
    }

    public static TeamLabPlayerNetworkAccess BuildPlayerNetworkAccess(PenetrationConfig config,
        IReadOnlyList<TeamLabRuntimeNetworkSpec> networks)
    {
        if (networks.Count == 0)
            return new TeamLabPlayerNetworkAccess([], []);

        var entryKeys = config.Networks
            .Where(network => network.IsEntry)
            .OrderBy(network => network.OrderIndex)
            .ThenBy(network => network.TopologyKey, StringComparer.Ordinal)
            .Select(network => network.TopologyKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);

        if (entryKeys.Count == 0)
        {
            var fallbackEntryKey = config.Networks
                .OrderBy(network => network.OrderIndex)
                .ThenBy(network => network.TopologyKey, StringComparer.Ordinal)
                .Select(network => network.TopologyKey)
                .FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
            if (!string.IsNullOrWhiteSpace(fallbackEntryKey))
                entryKeys.Add(fallbackEntryKey);
        }

        if (entryKeys.Count == 0)
            entryKeys.Add(networks[0].TopologyKey);

        var allowed = networks
            .Where(network => entryKeys.Contains(network.TopologyKey))
            .Select(network => network.Cidr)
            .Where(cidr => !string.IsNullOrWhiteSpace(cidr))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (allowed.Length == 0)
            allowed = [networks[0].Cidr];

        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var blocked = networks
            .Select(network => network.Cidr)
            .Where(cidr => !string.IsNullOrWhiteSpace(cidr) && !allowedSet.Contains(cidr))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new TeamLabPlayerNetworkAccess(allowed, blocked);
    }

    private static string[] ResolveEdgeNetworkKeys(PenetrationConfig config, PenetrationPolicyScope kind, int id,
        int fallbackNodeId, IReadOnlyDictionary<int, string> primaryNetworkKeyByNodeId,
        IReadOnlyDictionary<int, TeamLabAssetSpec> assetByNodeId)
    {
        if (kind == PenetrationPolicyScope.Network)
            return config.Networks
                .Where(network => network.Id == id)
                .Select(network => network.TopologyKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        var nodeId = id > 0 ? id : fallbackNodeId;
        if (assetByNodeId.TryGetValue(nodeId, out var asset) && IsRoutingAsset(asset))
            return asset.Interfaces
                .Select(iface => iface.NetworkKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        return primaryNetworkKeyByNodeId.TryGetValue(nodeId, out var networkKey) &&
               !string.IsNullOrWhiteSpace(networkKey)
            ? [networkKey]
            : [];
    }

    public static AgentCreateVmRequest BuildNativeVmRequest(int runtimeId, TeamLabAssetSpec spec, string? flag)
    {
        if (spec.Kind != TeamLabAssetSpecKind.Vm)
            throw new ArgumentException("TeamLab native VM request requires a VM asset spec.", nameof(spec));

        var vmName = TrimLinuxName($"{TeamLabPlanService.BuildRuntimeResourcePrefix(runtimeId)}-{NormalizeResourceToken(spec.TopologyKey)}");
        var interfaces = spec.Interfaces
            .Select(iface => TeamLabAssetPlanService.ToVmInterfaceRequest(iface, spec.OSType))
            .ToList();

        return new AgentCreateVmRequest
        {
            TemplateId = spec.SourceTemplateId,
            TemplatePath = spec.Image,
            VmName = vmName,
            Memory = spec.MemoryLimit,
            Cpu = spec.CpuCount,
            Flag = flag,
            Interfaces = interfaces,
            CloudInit = BuildVmInitConfig(runtimeId, spec, vmName, interfaces, flag)
        };
    }

    public static AgentVmInitConfig BuildVmInitConfig(int runtimeId, TeamLabAssetSpec spec, string vmName,
        IReadOnlyList<AgentVmNetworkInterfaceRequest> interfaces, string? flag)
    {
        if (spec.OSType != OSType.Linux)
        {
            return new AgentVmInitConfig
            {
                Enabled = false,
                OsType = spec.OSType,
                Hostname = vmName,
                InstanceId = $"teamlab-{runtimeId}-{NormalizeResourceToken(spec.TopologyKey)}"
            };
        }

        var instanceId = $"teamlab-{runtimeId}-{NormalizeResourceToken(spec.TopologyKey)}";
        var metaData = BuildCloudInitMetaData(instanceId, vmName);
        var networkConfig = BuildCloudInitNetworkConfig(interfaces);
        var userData = BuildCloudInitUserData(runtimeId, spec, vmName, flag);

        return new AgentVmInitConfig
        {
            Enabled = true,
            OsType = OSType.Linux,
            Hostname = vmName,
            InstanceId = instanceId,
            UserData = userData,
            MetaData = metaData,
            NetworkConfig = networkConfig,
            SensitiveKeys = ["flag", "GZCTF_FLAG", "user-data"]
        };
    }

    private static string BuildCloudInitMetaData(string instanceId, string hostname) =>
        $"instance-id: {YamlScalar(instanceId)}\nlocal-hostname: {YamlScalar(hostname)}\n";

    private static string BuildCloudInitUserData(int runtimeId, TeamLabAssetSpec spec, string hostname, string? flag)
    {
        var escapedFlag = EscapeSingleQuotedShell(flag ?? string.Empty);
        var escapedTopology = EscapeSingleQuotedShell(spec.TopologyKey);
        var escapedName = EscapeSingleQuotedShell(spec.Name);
        var builder = new StringBuilder();
        builder.AppendLine("#cloud-config");
        builder.AppendLine($"hostname: {YamlScalar(hostname)}");
        builder.AppendLine("manage_etc_hosts: true");
        builder.AppendLine("write_files:");
        builder.AppendLine("  - path: /opt/gzctf/runtime/env");
        builder.AppendLine("    owner: root:root");
        builder.AppendLine("    permissions: '0600'");
        builder.AppendLine("    content: |");
        builder.AppendLine($"      GZCTF_RUNTIME_ID='{runtimeId}'");
        builder.AppendLine($"      GZCTF_TOPOLOGY_KEY='{escapedTopology}'");
        builder.AppendLine($"      GZCTF_NODE_NAME='{escapedName}'");
        builder.AppendLine($"      GZCTF_FLAG='{escapedFlag}'");
        builder.AppendLine("  - path: /opt/gzctf/runtime/flag");
        builder.AppendLine("    owner: root:root");
        builder.AppendLine("    permissions: '0600'");
        builder.AppendLine("    content: |");
        AppendIndentedLiteral(builder, flag ?? string.Empty, "      ");
        builder.AppendLine("runcmd:");
        builder.AppendLine("  - [ bash, -lc, 'systemctl daemon-reload || true' ]");
        builder.AppendLine("  - [ bash, -lc, 'test ! -x /opt/gzctf/bin/firstboot || /opt/gzctf/bin/firstboot' ]");
        builder.AppendLine("  - [ bash, -lc, 'systemctl restart gzctf-runtime.service 2>/dev/null || true' ]");
        return builder.ToString();
    }

    public static string BuildCloudInitNetworkConfig(IReadOnlyList<AgentVmNetworkInterfaceRequest> interfaces)
    {
        var builder = new StringBuilder();
        builder.AppendLine("version: 2");
        builder.AppendLine("ethernets:");

        var index = 0;
        foreach (var iface in interfaces.Where(HasStaticCloudInitNetworkIntent))
        {
            var name = string.IsNullOrWhiteSpace(iface.InterfaceName) ? $"eth{index}" : iface.InterfaceName.Trim();
            if (!IsValidIpv4(iface.IpAddress))
                throw new ArgumentException("Invalid VM IP address.", nameof(iface.IpAddress));

            if (iface.PrefixLength is < 1 or > 32)
                throw new ArgumentException("Invalid VM IP prefix length.", nameof(iface.PrefixLength));

            builder.AppendLine($"  {YamlKey(name)}:");
            builder.AppendLine("    match:");
            builder.AppendLine($"      macaddress: \"{iface.MacAddress!.ToLowerInvariant()}\"");
            builder.AppendLine($"    set-name: {YamlScalar(name)}");
            builder.AppendLine("    dhcp4: false");
            builder.AppendLine("    dhcp6: false");
            builder.AppendLine($"    addresses: [{iface.IpAddress}/{iface.PrefixLength}]");

            if (!string.IsNullOrWhiteSpace(iface.Gateway))
            {
                if (!IsValidIpv4(iface.Gateway))
                    throw new ArgumentException("Invalid VM gateway address.", nameof(iface.Gateway));
                builder.AppendLine($"    gateway4: {iface.Gateway}");
            }

            var dnsServers = iface.DnsServers
                .Where(server => !string.IsNullOrWhiteSpace(server))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (dnsServers.Any(server => !IsValidIpv4(server)))
                throw new ArgumentException("Invalid VM DNS server address.", nameof(iface.DnsServers));

            if (dnsServers.Length > 0)
            {
                builder.AppendLine("    nameservers:");
                builder.AppendLine($"      addresses: [{string.Join(", ", dnsServers)}]");
            }

            var routes = iface.Routes
                .Select(ParseRoute)
                .Cast<(string To, string Via)>()
                .ToArray();
            if (routes.Length > 0)
            {
                builder.AppendLine("    routes:");
                foreach (var route in routes)
                {
                    builder.AppendLine($"      - to: {route.To}");
                    builder.AppendLine($"        via: {route.Via}");
                }
            }

            index++;
        }

        return builder.ToString();
    }

    private static (string To, string Via)? ParseRoute(string route)
    {
        var parts = route.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !string.Equals(parts[1], "via", StringComparison.OrdinalIgnoreCase) ||
            !IsValidIpv4Cidr(parts[0]) || !IsValidIpv4(parts[2]))
            throw new ArgumentException("Invalid VM static route.", nameof(route));

        return (parts[0], parts[2]);
    }

    private static string YamlKey(string value) => YamlScalar(value).Trim('"');

    private static bool HasStaticCloudInitNetworkIntent(AgentVmNetworkInterfaceRequest iface) =>
        !string.IsNullOrWhiteSpace(iface.MacAddress) ||
        !string.IsNullOrWhiteSpace(iface.IpAddress) ||
        iface.PrefixLength.HasValue ||
        !string.IsNullOrWhiteSpace(iface.Gateway) ||
        iface.DnsServers.Count > 0 ||
        iface.Routes.Count > 0;

    private static bool IsValidIpv4(string? value) =>
        System.Net.IPAddress.TryParse(value, out var address) &&
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    private static bool IsValidIpv4Cidr(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 &&
               IsValidIpv4(parts[0]) &&
               int.TryParse(parts[1], out var prefix) &&
               prefix is >= 1 and <= 32;
    }

    private static void AppendIndentedLiteral(StringBuilder builder, string value, string indent)
    {
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
            builder.AppendLine($"{indent}{line}");
    }

    private static string YamlScalar(string value)
    {
        if (value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            return value;
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static string EscapeSingleQuotedShell(string value) => value.Replace("'", "'\"'\"'");

    public static TeamLabVmReadyValidationResult ValidateNativeVmReady(TeamLabAssetSpec spec,
        AgentVmIpResponse? response)
    {
        if (spec.Kind != TeamLabAssetSpecKind.Vm)
            throw new ArgumentException("TeamLab native VM readiness requires a VM asset spec.", nameof(spec));

        var expectedIp = (spec.Interfaces.FirstOrDefault(i => i.IsPrimary) ?? spec.Interfaces.FirstOrDefault())?.IpAddress;
        if (string.IsNullOrWhiteSpace(expectedIp))
            return new TeamLabVmReadyValidationResult(false, $"VM {spec.Name} has no planned primary IP.");

        if (response is null)
            return new TeamLabVmReadyValidationResult(false, $"VM {spec.Name} readiness probe returned no response.");

        if (!string.Equals(response.Status, "Ready", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(response.Status, "Running", StringComparison.OrdinalIgnoreCase))
            return new TeamLabVmReadyValidationResult(false,
                $"VM {spec.Name} is not ready. Current status: {response.Status}. {response.Diagnostic}");

        if (!string.Equals(response.IpAddress, expectedIp, StringComparison.Ordinal))
            return new TeamLabVmReadyValidationResult(false,
                $"VM {spec.Name} acquired IP {response.IpAddress ?? "<none>"} but expected {expectedIp}. {response.Diagnostic}");

        return new TeamLabVmReadyValidationResult(true, $"VM {spec.Name} is ready at {expectedIp}.");
    }

    public static TeamLabContainerAttachRequest[] BuildNativeContainerAttachRequests(int runtimeId,
        string containerId, TeamLabAssetSpec spec, IReadOnlyList<TeamLabRuntimeNetworkSpec> networks,
        string vpnClientCidr, IReadOnlyDictionary<string, string[]> allowedNetworkCidrsByNetworkKey,
        string[] dnsServers, bool dryRun)
    {
        if (spec.Kind != TeamLabAssetSpecKind.Docker)
            throw new ArgumentException("TeamLab native container attach requires a Docker asset spec.", nameof(spec));

        var networkByKey = networks.ToDictionary(n => n.TopologyKey, StringComparer.Ordinal);
        return spec.Interfaces.Select(iface =>
        {
            networkByKey.TryGetValue(iface.NetworkKey, out var currentNetwork);
            var gateway = currentNetwork?.GatewayIp;
            allowedNetworkCidrsByNetworkKey.TryGetValue(iface.NetworkKey, out var allowedCidrs);
            allowedCidrs ??= [];
            var staticRoutes = allowedCidrs
                .Append(vpnClientCidr)
                .Where(route => !string.IsNullOrWhiteSpace(route))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return TeamLabAssetPlanService.BuildContainerAttachRequest(runtimeId, containerId, iface, dryRun,
                gateway, staticRoutes, dnsServers);
        }).ToArray();
    }

    public static TeamLabDhcpDnsRequest[] BuildDhcpDnsRequests(int runtimeId, string routerNamespace,
        IReadOnlyList<TeamLabRuntimeNetworkSpec> networks, IReadOnlyList<TeamLabAssetSpec> assets, bool dryRun)
    {
        var assetsByNetwork = assets
            .SelectMany(asset => asset.Interfaces.Select(iface => new { Asset = asset, Interface = iface }))
            .GroupBy(item => item.Interface.NetworkKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        return networks.Select((network, index) =>
        {
            assetsByNetwork.TryGetValue(network.TopologyKey, out var currentAssets);
            currentAssets ??= [];
            var leases = currentAssets
                .Where(item => !string.IsNullOrWhiteSpace(item.Interface.MacAddress) &&
                               !string.IsNullOrWhiteSpace(item.Interface.IpAddress))
                .Select(item => new TeamLabDhcpLeaseRequest(
                    item.Interface.MacAddress,
                    item.Interface.IpAddress,
                    NormalizeResourceToken(item.Asset.TopologyKey)))
                .ToArray();
            var records = currentAssets
                .Where(item => !string.IsNullOrWhiteSpace(item.Interface.IpAddress))
                .Select(item => new TeamLabDnsRecordRequest(
                    NormalizeResourceToken(item.Asset.TopologyKey),
                    item.Interface.IpAddress))
                .DistinctBy(record => record.Hostname)
                .ToArray();

            return new TeamLabDhcpDnsRequest(
                runtimeId,
                BuildDhcpDnsServiceName(runtimeId, network.TopologyKey),
                routerNamespace,
                network.BridgeName,
                BuildRouterNamespaceInterfaceName(routerNamespace, index),
                network.GatewayIp,
                network.Cidr,
                $"teamlab{runtimeId}.local",
                leases,
                records,
                dryRun);
        }).ToArray();
    }

    public async Task<TeamLabDeploymentResult> DeployRuntimeAsync(int gameId, int teamId, CancellationToken token)
        => await DeployRuntimeAsync(gameId, teamId, capacityAlreadyReserved: false, token);

    public async Task<TeamLabDeploymentResult> DeployQueuedRuntimeAsync(int runtimeId, CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Where(r => r.Id == runtimeId)
            .Select(r => new { r.GameId, r.TeamId })
            .SingleOrDefaultAsync(token);

        return runtime is null
            ? new TeamLabDeploymentResult(false, $"TeamLab runtime {runtimeId} was not found.", null)
            : await DeployRuntimeAsync(runtime.GameId, runtime.TeamId, capacityAlreadyReserved: true, token);
    }

    async Task<TeamLabDeploymentResult> DeployRuntimeAsync(int gameId, int teamId, bool capacityAlreadyReserved,
        CancellationToken token)
    {
        var planned = await planService.PlanRuntimeAsync(gameId, teamId, token);
        if (!planned.Success || planned.Runtime is null)
            return new TeamLabDeploymentResult(false, planned.Message, planned.Runtime);

        var runtime = await LoadRuntimeAsync(gameId, teamId, token);
        if (runtime is null)
            return new TeamLabDeploymentResult(false, "TeamLab runtime was not found after planning.", null);

        if (!TeamLabStateMachine.CanTransition(runtime.Status, TeamLabRuntimeStatus.Deploying))
            return new TeamLabDeploymentResult(false, $"Cannot deploy TeamLab runtime from status {runtime.Status}.", runtime);

        if (runtime.WorkerNodeId is null || runtime.WorkerNode is null || runtime.PublicUdpMapping is null)
            return await FailAsync(runtime, "TeamLab runtime has no planned WorkerNode or UDP mapping.", token);

        var penetrationEnvironment = await context.PenetrationTeamEnvironments
            .FirstOrDefaultAsync(e => e.GameId == gameId && e.TeamId == teamId, token);

        return await DeployNativeRuntimeAsync(runtime, gameId, teamId, penetrationEnvironment,
            capacityAlreadyReserved, token);
    }

    private async Task<AgentVmIpResponse?> WaitForNativeVmReadyAsync(Guid workerNodeId, TeamLabAssetSpec asset,
        AgentCreateVmRequest vmRequest, string vmName, CancellationToken token)
    {
        AgentVmIpResponse? lastResponse = null;
        var maxAttempts = ResolveNativeVmReadyProbeAttempts(asset.OSType);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            lastResponse = await agentClient.GetVmIpAsync(workerNodeId, vmName, vmRequest.Interfaces, token);
            var validation = ValidateNativeVmReady(asset, lastResponse);
            if (validation.Success)
            {
                if (attempt > 1)
                    logger.LogInformation("TeamLab VM {VmName} became ready after {Attempt} probes.", vmName, attempt);

                return lastResponse;
            }

            if (attempt == maxAttempts)
                break;

            logger.LogDebug("TeamLab VM {VmName} readiness probe {Attempt}/{MaxAttempts} pending: {Message}",
                vmName, attempt, maxAttempts, validation.Message);
            await Task.Delay(NativeVmReadyProbeInterval, token);
        }

        return lastResponse;
    }

    private async Task<TeamLabDryRunResponse?> WaitForNativeProbeTargetReadyAsync(Guid workerNodeId, int runtimeId,
        string routerNamespace, string targetIp, CancellationToken token)
    {
        TeamLabDryRunResponse? lastResponse = null;

        for (var attempt = 1; attempt <= NativeRuntimeConnectivityProbeAttempts; attempt++)
        {
            lastResponse = await agentClient.ProbeTeamLabAsync(workerNodeId,
                new TeamLabProbeRequest(runtimeId, routerNamespace, targetIp, _config.DryRun), token);
            if (lastResponse is { Success: true })
            {
                if (attempt > 1)
                    logger.LogInformation("TeamLab probe target {TargetIp} became reachable after {Attempt} probes.",
                        targetIp, attempt);

                return lastResponse;
            }

            if (attempt == NativeRuntimeConnectivityProbeAttempts)
                break;

            logger.LogDebug("TeamLab probe target {TargetIp} readiness probe {Attempt}/{MaxAttempts} pending: {Message}",
                targetIp, attempt, NativeRuntimeConnectivityProbeAttempts,
                lastResponse?.Message ?? "Agent TeamLab probe did not return a successful response.");
            await Task.Delay(NativeRuntimeConnectivityProbeInterval, token);
        }

        return lastResponse;
    }

    async Task<TeamLabNativeAssetCreationResult> CreateNativeAssetAsync(Guid workerNodeId, int runtimeId,
        PenetrationConfig config, TeamLabPublishedAssetPlanResult plan, TeamLabRuntimeRouteMatrix routeMatrix,
        TeamLabAssetSpec asset, int gameId, int teamId, int publishedVersion, string vpnClientAddress,
        Action<string> trackCreatedContainer, Action<string> trackCreatedVm, CancellationToken token)
    {
        var flag = BuildPrimaryFlag(config, asset.TopologyKey, gameId, teamId, publishedVersion);
        if (asset.Kind == TeamLabAssetSpecKind.Docker)
        {
            var containerConfig = await BuildResolvedNativeDockerContainerConfigAsync(asset, teamId,
                workerNodeId, flag, dockerRegistry, token, plan.Networks);
            containerConfig.EnvironmentVariables = BuildNativeEnvironmentVariables(config, asset.TopologyKey,
                plan, gameId, teamId, publishedVersion);
            var container = await agentClient.CreateContainerOrThrowAsync(workerNodeId, containerConfig, token);
            trackCreatedContainer(container.ContainerId);

            var attachRequests = BuildNativeContainerAttachRequests(runtimeId, container.ContainerId, asset,
                plan.Networks, $"{vpnClientAddress}/32",
                routeMatrix.AllowedCidrsByNetworkKey,
                plan.Networks.Select(network => network.GatewayIp).ToArray(), _config.DryRun);
            foreach (var attachRequest in attachRequests)
            {
                var attach = await agentClient.AttachTeamLabContainerAsync(workerNodeId, attachRequest, token);
                if (attach is not { Success: true })
                    throw new InvalidOperationException(attach?.Message ??
                                                        $"Failed to attach container {asset.Name} to TeamLab network.");
                if (attach.DryRun)
                    throw new InvalidOperationException(attach.Message);
            }

            return new TeamLabNativeAssetCreationResult(
                BuildRuntimeAssetRecord(asset, container.ContainerId),
                container.ContainerId,
                null);
        }

        var vmRequest = BuildNativeVmRequest(runtimeId, asset, flag);
        logger.LogInformation("TeamLab VM {VmName} init config prepared: cloudInit={CloudInitEnabled}, os={OsType}, interfaces={InterfaceCount}.",
            vmRequest.VmName, vmRequest.CloudInit?.Enabled == true, vmRequest.CloudInit?.OsType, vmRequest.Interfaces.Count);
        var vm = await agentClient.CreateVmAsync(workerNodeId, vmRequest, token);
        if (vm is null || string.IsNullOrWhiteSpace(vm.VmName))
            throw new InvalidOperationException($"Failed to create TeamLab VM {asset.Name}.");
        trackCreatedVm(vm.VmName);

        logger.LogInformation("TeamLab VM {VmName} created; waiting for guest/network readiness.", vm.VmName);
        var vmIp = await WaitForNativeVmReadyAsync(workerNodeId, asset, vmRequest, vm.VmName, token);
        var vmReady = ValidateNativeVmReady(asset, vmIp);
        if (!vmReady.Success)
            throw new InvalidOperationException(vmReady.Message);

        return new TeamLabNativeAssetCreationResult(
            BuildRuntimeAssetRecord(asset, vm.VmName),
            null,
            vm.VmName);
    }

    private async Task<TeamLabDeploymentResult> DeployNativeRuntimeAsync(TeamLabRuntime runtime, int gameId, int teamId,
        PenetrationTeamEnvironment? existingEnvironment, bool capacityAlreadyReserved, CancellationToken token)
    {
        runtime.Status = TeamLabRuntimeStatus.Deploying;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "deploy", TeamLabEventLevel.Info, "Starting native TeamLab deployment from published topology.");
        await context.SaveChangesAsync(token);
        logger.SystemLog($"TeamLab runtime deployment started: game={gameId}, team={teamId}, runtime={runtime.Id}, node={runtime.WorkerNodeId}.",
            TaskStatus.Pending, LogLevel.Information);

        var topology = await LoadPublishedTopologyAsync(runtime, gameId, token);
        if (!topology.Success || topology.Config is null)
            return await FailAsync(runtime, topology.Message, token);

        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(t => topology.Config.Nodes.Select(n => n.ImageTemplateId).Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, token);

        var teamIndex = await ResolveTeamIndexAsync(gameId, teamId, existingEnvironment, token);
        var plan = TeamLabAssetPlanService.BuildPublishedAssetPlan(topology.Config, runtime.Id, teamIndex,
            templates, runtime.NetworkPrefix);
        if (!plan.Success)
            return await FailAsync(runtime, plan.Message, token);
        var routeMatrix = BuildRuntimeRouteMatrix(topology.Config, plan.Networks, plan.Assets);
        var slots = CountAssetSlots(plan.Assets);
        if (!capacityAlreadyReserved)
        {
            var reservation = await TryReserveTeamLabCapacityAsync(runtime, slots, token);
            if (!reservation.Success)
            {
                var queue = await TryQueueTeamLabRuntimeAsync(runtime, slots, reservation.Message, token);
                return new TeamLabDeploymentResult(false, reservation.Message, runtime, queue);
            }
        }

        var shardDeployments = BuildShardDeployments(runtime, plan);
        if (shardDeployments.Count == 0)
            return await FailAsync(runtime, "TeamLab runtime has no planned shard deployment.", token);

        var entryShard = FindEntryShard(shardDeployments) ?? shardDeployments[0];
        var names = entryShard.Names;
        var vpnServerAddress = LastHost(runtime.NetworkPrefix);
        var vpnClientAddress = SecondHost(runtime.NetworkPrefix);
        if (string.IsNullOrWhiteSpace(vpnServerAddress) || string.IsNullOrWhiteSpace(vpnClientAddress))
            return await FailNativeDeploymentAsync(runtime, "TeamLab runtime network prefix is invalid.",
                shardDeployments, [], slots, capacityAlreadyReserved, token);

        var playerAccess = BuildPlayerNetworkAccess(topology.Config, plan.Networks);
        var allowedIps = string.Join(',', playerAccess.AllowedCidrs);
        TeamLabPeerMaterial peer;
        try
        {
            peer = wireGuardService.EnsurePeer(runtime, $"{vpnClientAddress}/32", allowedIps, string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            return await FailNativeDeploymentAsync(runtime, ex.Message, shardDeployments, [], slots,
                capacityAlreadyReserved, token);
        }

        foreach (var shard in runtime.Shards)
        {
            shard.Status = TeamLabRuntimeStatus.Deploying;
            shard.LastError = null;
            shard.UpdatedAt = DateTimeOffset.UtcNow;
        }

        foreach (var shard in shardDeployments)
        {
            foreach (var network in shard.Networks)
            {
                var bridge = await agentClient.CreateTeamLabBridgeAsync(shard.WorkerNodeId,
                    new TeamLabBridgeRequest(runtime.Id, network.BridgeName, network.Cidr, _config.DryRun), token);
                if (bridge is not { Success: true })
                    return await FailNativeDeploymentAsync(runtime,
                        bridge?.Message ?? $"Failed to create TeamLab bridge {network.Name}.",
                        shardDeployments, [], slots, capacityAlreadyReserved, token);
                if (bridge.DryRun)
                    return await FailNativeDeploymentAsync(runtime, bridge.Message, shardDeployments, [], slots,
                        capacityAlreadyReserved, token);
            }
        }

        foreach (var shard in shardDeployments)
        {
            var router = await agentClient.CreateTeamLabRouterAsync(shard.WorkerNodeId,
                new TeamLabRouterRequest(runtime.Id, shard.Names.RouterNamespace,
                    shard.Networks.Select(network =>
                        new TeamLabRouterInterfaceRequest(network.BridgeName,
                            $"{network.GatewayIp}/{PrefixLength(network.Cidr)}")).ToArray(),
                    [],
                    _config.DryRun), token);
            if (router is not { Success: true })
                return await FailNativeDeploymentAsync(runtime,
                    router?.Message ?? "Failed to create TeamLab router namespace.",
                    shardDeployments, [], slots, capacityAlreadyReserved, token);
            if (router.DryRun)
                return await FailNativeDeploymentAsync(runtime, router.Message, shardDeployments, [], slots,
                    capacityAlreadyReserved, token);
        }

        var workerNodeIds = shardDeployments.Select(shard => shard.WorkerNodeId).Distinct().ToArray();
        var workerNodes = await context.WorkerNodes.AsNoTracking()
            .Where(node => workerNodeIds.Contains(node.Id))
            .ToDictionaryAsync(node => node.Id, token);
        var workerOrdinalById = workerNodeIds
            .OrderBy(id => id)
            .Select((id, index) => new { id, index })
            .ToDictionary(item => item.id, item => item.index);
        var fabricRoutePlans = BuildShardFabricRoutePlans(shardDeployments, workerNodes);
        var routeVersion = runtime.PublishedVersion <= 0 ? 1 : runtime.PublishedVersion;
        foreach (var planItem in fabricRoutePlans)
        {
            var shardOrdinal = workerOrdinalById[planItem.Shard.WorkerNodeId];
            var fabricGateway = ResolveWorkerFabricGateway(workerNodes, planItem.Shard.WorkerNodeId);
            if (string.IsNullOrWhiteSpace(fabricGateway))
                return await FailNativeDeploymentAsync(runtime,
                    $"TeamLab WorkerNode {planItem.Shard.WorkerNodeId} has no Fabric IP.",
                    shardDeployments, [], slots, capacityAlreadyReserved, token);

            var fabric = await agentClient.ApplyTeamLabFabricAsync(planItem.Shard.WorkerNodeId,
                new TeamLabFabricApplyRequest(
                    runtime.Id,
                    routeVersion,
                    fabricGateway,
                    planItem.Shard.Names.RouterNamespace,
                    BuildFabricUplinkHostAddressCidr(runtime.Id, shardOrdinal),
                    BuildFabricUplinkPeerAddressCidr(runtime.Id, shardOrdinal),
                    BuildLocalFabricRoutes(planItem.Shard.Networks, runtime.Id, shardOrdinal),
                    planItem.Routes,
                    _config.DryRun), token);
            if (fabric is not { Success: true })
                return await FailNativeDeploymentAsync(runtime,
                    fabric?.Message ?? "Failed to apply TeamLab Fabric routes.",
                    shardDeployments, [], slots, capacityAlreadyReserved, token);
            if (fabric.DryRun)
                return await FailNativeDeploymentAsync(runtime, fabric.Message, shardDeployments, [], slots,
                    capacityAlreadyReserved, token);

            planItem.Shard.Shard.RouteVersion = routeVersion;
            planItem.Shard.Shard.UpdatedAt = DateTimeOffset.UtcNow;
        }
        if (fabricRoutePlans.Count > 0)
        {
            AddEvent(runtime, "fabric", TeamLabEventLevel.Success,
                $"Applied TeamLab Fabric routes for {fabricRoutePlans.Count} shard(s).");
            await context.SaveChangesAsync(token);
        }

        var wg = await agentClient.ConfigureTeamLabWireGuardAsync(entryShard.WorkerNodeId,
            new TeamLabWireGuardRequest(runtime.Id, entryShard.Names.RouterNamespace, entryShard.Names.WireGuardInterface,
                runtime.PublicUdpMapping!.WorkerWireGuardPort,
                $"{vpnServerAddress}/32",
                peer.ServerPrivateKey,
                peer.Peer.PublicKey,
                peer.Peer.ClientAddress,
                peer.Peer.AllowedIPs,
                playerAccess.AllowedCidrs,
                playerAccess.BlockedCidrs,
                _config.DryRun), token);
        if (wg is not { Success: true })
            return await FailNativeDeploymentAsync(runtime,
                wg?.Message ?? "Failed to configure TeamLab WireGuard endpoint.",
                shardDeployments, [], slots, capacityAlreadyReserved, token);
        if (wg.DryRun)
            return await FailNativeDeploymentAsync(runtime, wg.Message, shardDeployments, [], slots,
                capacityAlreadyReserved, token);

        var createdAssets = shardDeployments
            .ToDictionary(shard => shard.WorkerNodeId,
                shard => new TeamLabCreatedShardAssets(shard.WorkerNodeId, [], []));
        var createdAssetLock = new object();

        void TrackCreatedContainer(Guid workerNodeId, string containerId)
        {
            lock (createdAssetLock)
                createdAssets[workerNodeId].Containers.Add(containerId);
        }

        void TrackCreatedVm(Guid workerNodeId, string vmName)
        {
            lock (createdAssetLock)
                createdAssets[workerNodeId].Vms.Add(vmName);
        }

        foreach (var shard in shardDeployments)
        {
            var dhcpDnsRequests = BuildDhcpDnsRequests(runtime.Id, shard.Names.RouterNamespace, shard.Networks,
                shard.Assets, _config.DryRun);
            foreach (var dhcpDnsRequest in dhcpDnsRequests)
            {
                var dhcpDns = await agentClient.ConfigureTeamLabDhcpDnsAsync(shard.WorkerNodeId,
                    dhcpDnsRequest, token);
                if (dhcpDns is not { Success: true })
                    return await FailNativeDeploymentAsync(runtime,
                        dhcpDns?.Message ?? $"Failed to configure TeamLab DHCP/DNS service {dhcpDnsRequest.ServiceName}.",
                        shardDeployments, createdAssets.Values, slots, capacityAlreadyReserved, token);
                if (dhcpDns.DryRun)
                    return await FailNativeDeploymentAsync(runtime, dhcpDns.Message, shardDeployments,
                        createdAssets.Values, slots, capacityAlreadyReserved, token);

                var probeName = dhcpDnsRequest.DnsRecords.FirstOrDefault()?.Hostname;
                if (!string.IsNullOrWhiteSpace(probeName))
                {
                    var dnsProbe = await agentClient.ProbeTeamLabDhcpDnsAsync(shard.WorkerNodeId,
                        new TeamLabDhcpDnsProbeRequest(runtime.Id, shard.Names.RouterNamespace, dhcpDnsRequest.GatewayIp,
                            $"{probeName}.{dhcpDnsRequest.Domain}", _config.DryRun), token);
                    if (dnsProbe is not { Success: true })
                        return await FailNativeDeploymentAsync(runtime,
                            dnsProbe?.Message ?? $"Failed to probe TeamLab DHCP/DNS service {dhcpDnsRequest.ServiceName}.",
                            shardDeployments, createdAssets.Values, slots, capacityAlreadyReserved, token);
                    if (dnsProbe.DryRun)
                        return await FailNativeDeploymentAsync(runtime, dnsProbe.Message, shardDeployments,
                            createdAssets.Values, slots, capacityAlreadyReserved, token);
                }
            }
        }

        try
        {
            foreach (var assetGroup in plan.Assets.GroupBy(asset => asset.StartPriority).OrderBy(group => group.Key))
            {
                var assetResults = await Task.WhenAll(assetGroup
                    .OrderBy(asset => asset.TopologyKey, StringComparer.Ordinal)
                    .Select(asset =>
                    {
                        var shard = ResolveAssetShard(asset, shardDeployments);
                        return CreateNativeAssetAsync(shard.WorkerNodeId, runtime.Id,
                        topology.Config, plan, routeMatrix, asset, gameId, teamId, runtime.PublishedVersion,
                        vpnClientAddress,
                        containerId => TrackCreatedContainer(shard.WorkerNodeId, containerId),
                        vmName => TrackCreatedVm(shard.WorkerNodeId, vmName), token);
                    }));

                foreach (var result in assetResults)
                    RecordRuntimeAsset(runtime, result.RuntimeAsset);

                await context.SaveChangesAsync(token);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or AgentClientException or HttpRequestException or TaskCanceledException)
        {
            return await FailNativeDeploymentAsync(runtime, ex.Message, shardDeployments,
                createdAssets.Values, slots, capacityAlreadyReserved, token);
        }

        var gateway = await publicUdpGatewayProvider.SyncMappingAsync(runtime.PublicUdpMapping, token);
        if (!gateway.Success)
            return await FailNativeDeploymentAsync(runtime, gateway.Message, shardDeployments,
                createdAssets.Values, slots, capacityAlreadyReserved, token, removePublicMapping: false);

        foreach (var shard in shardDeployments)
            RecordNativeShardRuntimeFacts(runtime, shard);

        var flowStart = await trafficFlowService.StartCollectorsAsync(runtime, runtime.Networks, token);
        if (!flowStart.Success)
            return await FailNativeDeploymentAsync(runtime, flowStart.Message, shardDeployments,
                createdAssets.Values, slots, capacityAlreadyReserved, token);

        runtime.Status = TeamLabRuntimeStatus.Probing;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "probe", TeamLabEventLevel.Info, "Native TeamLab assets created; starting runtime connectivity probe.");
        await context.SaveChangesAsync(token);

        var probeTargets = BuildNativeProbeTargets(plan.Assets);
        var shouldRunConnectivityProbe = ShouldRunNativeConnectivityProbe(plan.Assets);
        if (shouldRunConnectivityProbe && probeTargets.Length == 0)
            return await FailAsync(runtime, "TeamLab probe target is unavailable in the native asset plan.", token);

        if (!shouldRunConnectivityProbe)
        {
            AddEvent(runtime, "probe", TeamLabEventLevel.Info,
                "Native TeamLab runtime has only Windows VM assets; skipping ICMP connectivity probe after VM DHCP readiness.");
            await context.SaveChangesAsync(token);
        }

        foreach (var probeTarget in probeTargets)
        {
            var asset = plan.Assets.FirstOrDefault(asset => asset.Interfaces.Any(iface =>
                string.Equals(iface.IpAddress, probeTarget, StringComparison.Ordinal)));
            var shard = asset is null ? entryShard : ResolveAssetShard(asset, shardDeployments);
            var probe = await WaitForNativeProbeTargetReadyAsync(shard.WorkerNodeId, runtime.Id,
                shard.Names.RouterNamespace, probeTarget, token);
            if (probe is not { Success: true })
                return await FailNativeDeploymentAsync(runtime,
                    probe?.Message ?? $"TeamLab runtime connectivity probe failed for {probeTarget}.",
                    shardDeployments, createdAssets.Values, slots, capacityAlreadyReserved, token);
            if (probe.DryRun)
                return await FailNativeDeploymentAsync(runtime, probe.Message, shardDeployments,
                    createdAssets.Values, slots, capacityAlreadyReserved, token);
        }

        await SyncCompatibilityEnvironmentAsync(runtime, topology.Config, plan, teamIndex, token);
        if (!capacityAlreadyReserved)
            await ConfirmTeamLabCapacityAsync(runtime, slots, token);

        return await MarkRuntimeRunningAsync(runtime, "Native TeamLab runtime deployment reached running state.", token);
    }

    internal async Task<FleetCapacityReservationResult> TryReserveTeamLabCapacityAsync(TeamLabRuntime runtime,
        TeamLabAssetSlotCount slots, CancellationToken token)
    {
        var shardSlots = CountShardSlots(runtime, slots);
        if (shardSlots.Count == 0)
            return FleetCapacityReservationResult.Failed("TeamLab runtime has no planned WorkerNode.");

        var reservation = await capacityReservation.TryReserveBatchAsync(
            shardSlots.Select(slot => new FleetCapacityBatchItem(slot.WorkerNodeId, slot.DockerSlots, slot.VmSlots))
                .ToArray(),
            requireTeamLab: true,
            token);

        if (!reservation.Success)
            return FleetCapacityReservationResult.Failed(reservation.Message);

        var primary = reservation.Reservations.FirstOrDefault(r => r.NodeId == runtime.WorkerNodeId) ??
                      reservation.Reservations.FirstOrDefault();
        return primary ?? FleetCapacityReservationResult.Failed("TeamLab capacity reservation returned no node.");
    }

    internal async Task<DeploymentQueueStatusModel?> TryQueueTeamLabRuntimeAsync(TeamLabRuntime runtime,
        TeamLabAssetSlotCount slots, string reason, CancellationToken token)
    {
        if (runtime.Id <= 0 || runtime.GameId <= 0 || runtime.TeamId <= 0)
            return null;

        var result = await deploymentQueue.EnqueueAsync(
            DeploymentQueueRequest.TeamLab(runtime.GameId, runtime.TeamId, runtime.Id,
                slots.DockerSlots, slots.VmSlots), token);
        var status = await deploymentQueue.GetStatusAsync(result.TicketId, token);
        runtime.LastError = NormalizeRuntimeError($"Waiting in deployment queue. {reason}");
        runtime.Status = TeamLabRuntimeStatus.Scheduled;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "queue", TeamLabEventLevel.Warning,
            $"TeamLab runtime queued because capacity is unavailable. People ahead: {status?.PeopleAhead ?? result.PeopleAhead}.");
        await context.SaveChangesAsync(token);
        return status;
    }

    public async Task<TeamLabDeploymentResult> DestroyRuntimeAsync(int gameId, int teamId, CancellationToken token)
    {
        var runtime = await LoadRuntimeAsync(gameId, teamId, token);
        if (runtime is null)
            return new TeamLabDeploymentResult(false, "TeamLab runtime was not found.", null);

        if (!TeamLabStateMachine.CanTransition(runtime.Status, TeamLabRuntimeStatus.Destroying))
            return new TeamLabDeploymentResult(false, $"Cannot destroy TeamLab runtime from status {runtime.Status}.", runtime);

        await deploymentQueue.CancelTeamLabRuntimeAsync(runtime.Id, "TeamLab runtime was destroyed.", token);

        runtime.Status = TeamLabRuntimeStatus.Destroying;
        runtime.IsOpenToPlayers = false;
        AddEvent(runtime, "destroy", TeamLabEventLevel.Info, "Destroying TeamLab runtime resources.");
        await context.SaveChangesAsync(token);
        logger.SystemLog($"TeamLab runtime destroy started: game={gameId}, team={teamId}, runtime={runtime.Id}, node={runtime.WorkerNodeId}.",
            TaskStatus.Pending, LogLevel.Information);

        var cleanupErrors = new List<string>();
        var flowCleanup = await trafficFlowService.StopCollectorsAsync(runtime, runtime.Networks, token);
        if (!flowCleanup.Success)
            cleanupErrors.Add(flowCleanup.Message);

        foreach (var nodeCleanup in await BuildDestroyNodeCleanupPlansAsync(runtime, gameId, token))
        {
            var assetCleanup = await CleanupTrackedNativeAssetsAsync(nodeCleanup.WorkerNodeId,
                nodeCleanup.AssetCleanup, token);
            cleanupErrors.AddRange(assetCleanup);

            var cleanup = await agentClient.CleanupTeamLabAsync(nodeCleanup.WorkerNodeId,
                new TeamLabCleanupRequest(runtime.Id, nodeCleanup.ResourceNames, _config.DryRun), token);
            if (cleanup is not { Success: true })
                cleanupErrors.Add(cleanup?.Message ?? $"TeamLab WorkerNode {nodeCleanup.WorkerNodeId} cleanup failed.");
            else if (cleanup.DryRun)
                cleanupErrors.Add(cleanup.Message);
        }

        if (runtime.PublicUdpMapping is not null)
        {
            var gatewayCleanup = await publicUdpGatewayProvider.RemoveMappingAsync(runtime.PublicUdpMapping, token);
            if (!gatewayCleanup.Success)
                cleanupErrors.Add(gatewayCleanup.Message);
        }

        if (cleanupErrors.Count > 0)
        {
            runtime.Status = TeamLabRuntimeStatus.CleanupPending;
            runtime.LastError = NormalizeRuntimeError(string.Join('\n', cleanupErrors));
            runtime.UpdatedAt = DateTimeOffset.UtcNow;
            AddEvent(runtime, "destroy", TeamLabEventLevel.Error,
                $"TeamLab runtime cleanup is incomplete: {runtime.LastError}");
            await SyncCompatibilityEnvironmentStatusAsync(runtime, PenetrationRuntimeStatus.CleanupPending,
                runtime.LastError, token);
            await context.SaveChangesAsync(token);
            logger.SystemLog($"TeamLab runtime cleanup pending: game={gameId}, team={teamId}, runtime={runtime.Id}, error={runtime.LastError}",
                TaskStatus.Degraded, LogLevel.Warning);
            return new TeamLabDeploymentResult(false, runtime.LastError, runtime);
        }

        await ReleaseTeamLabCapacityAsync(runtime, token);
        runtime.Status = TeamLabRuntimeStatus.Destroyed;
        MarkRuntimeFactsDestroyed(runtime);
        foreach (var shard in runtime.Shards)
        {
            shard.Status = TeamLabRuntimeStatus.Destroyed;
            shard.LastError = null;
            shard.UpdatedAt = DateTimeOffset.UtcNow;
        }
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "destroy", TeamLabEventLevel.Success, "TeamLab runtime destroyed.");
        await SyncCompatibilityEnvironmentStatusAsync(runtime, PenetrationRuntimeStatus.Stopped, null, token);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"TeamLab runtime destroyed: game={gameId}, team={teamId}, runtime={runtime.Id}.",
            TaskStatus.Success, LogLevel.Information);

        return new TeamLabDeploymentResult(true, "TeamLab runtime destroyed.", runtime);
    }

    public async Task<IReadOnlyList<TeamLabEvent>> GetEventsAsync(int gameId, int teamId, CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);
        if (runtime is null) return [];

        return await context.TeamLabEvents.AsNoTracking()
            .Where(e => e.RuntimeId == runtime.Id)
            .OrderByDescending(e => e.CreatedAt)
            .Take(100)
            .ToListAsync(token);
    }

    private async Task<TeamLabRuntime?> LoadRuntimeAsync(int gameId, int teamId, CancellationToken token) =>
        await context.TeamLabRuntimes
            .Include(r => r.WorkerNode)
            .Include(r => r.Team)
            .Include(r => r.PublicUdpMapping)
            .Include(r => r.Shards).ThenInclude(s => s.Networks)
            .Include(r => r.Shards).ThenInclude(s => s.Assets)
            .Include(r => r.Networks)
            .Include(r => r.Assets)
            .Include(r => r.Events)
            .Include(r => r.VpnPeers)
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);

    private async Task<string[]> BuildDestroyResourceNamesAsync(TeamLabRuntime runtime, int gameId,
        CancellationToken token)
    {
        var names = new HashSet<string>(BuildNativeCleanupResourceNames(runtime), StringComparer.Ordinal);
        var topology = await LoadPublishedTopologyAsync(runtime, gameId, token);
        if (topology is not { Success: true, Config: not null } || runtime.Id <= 0)
            return names.ToArray();

        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(t => topology.Config.Nodes.Select(n => n.ImageTemplateId).Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, token);
        var plan = TeamLabAssetPlanService.BuildPublishedAssetPlan(topology.Config, runtime.Id, 0,
            templates, runtime.NetworkPrefix);
        if (!plan.Success)
            return names.ToArray();

        var plannedNames = BuildResourceNames(runtime.Id, plan.Networks.Select(n => n.TopologyKey).ToArray());
        foreach (var name in BuildNativeCleanupResourceNames(plannedNames, plan.Networks))
            names.Add(name);

        return names.ToArray();
    }

    private sealed record TeamLabNodeCleanupPlan(
        Guid WorkerNodeId,
        TeamLabNativeAssetCleanupPlan AssetCleanup,
        string[] ResourceNames);

    private async Task<IReadOnlyList<TeamLabNodeCleanupPlan>> BuildDestroyNodeCleanupPlansAsync(TeamLabRuntime runtime,
        int gameId, CancellationToken token)
    {
        var nodeIds = runtime.Shards
            .Select(shard => (Guid?)shard.WorkerNodeId)
            .Concat(runtime.Networks.Select(network => network.WorkerNodeId))
            .Concat(runtime.Assets.Select(asset => asset.WorkerNodeId))
            .Append(runtime.WorkerNodeId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (nodeIds.Length == 0)
            return [];

        var plannedShards = await BuildPlannedShardDeploymentsForCleanupAsync(runtime, gameId, token);
        return nodeIds.Select(nodeId =>
        {
            var assetCleanup = new TeamLabNativeAssetCleanupPlan(
                runtime.Assets
                    .Where(asset => asset.WorkerNodeId == nodeId ||
                                    (asset.WorkerNodeId is null && runtime.WorkerNodeId == nodeId))
                    .Where(asset => asset.Kind == TeamLabResourceKind.Docker)
                    .Where(asset => asset.Status != TeamLabRuntimeStatus.Destroyed)
                    .Select(asset => asset.RuntimeResourceId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .Cast<string>()
                    .ToArray(),
                runtime.Assets
                    .Where(asset => asset.WorkerNodeId == nodeId ||
                                    (asset.WorkerNodeId is null && runtime.WorkerNodeId == nodeId))
                    .Where(asset => asset.Kind == TeamLabResourceKind.Vm)
                    .Where(asset => asset.Status != TeamLabRuntimeStatus.Destroyed)
                    .Select(asset => asset.RuntimeResourceId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .Cast<string>()
                    .ToArray());

            var resourceNames = new HashSet<string>(
                runtime.Networks
                    .Where(network => network.WorkerNodeId == nodeId ||
                                      (network.WorkerNodeId is null && runtime.WorkerNodeId == nodeId))
                    .Select(network => network.BridgeName)
                    .Concat(runtime.Assets
                        .Where(asset => asset.WorkerNodeId == nodeId ||
                                        (asset.WorkerNodeId is null && runtime.WorkerNodeId == nodeId))
                        .Where(asset => asset.Kind is TeamLabResourceKind.DhcpDnsService
                            or TeamLabResourceKind.RouterNamespace
                            or TeamLabResourceKind.WireGuard)
                        .Select(asset => asset.RuntimeResourceId))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>(),
                StringComparer.Ordinal);

            foreach (var planned in plannedShards.Where(shard => shard.WorkerNodeId == nodeId))
            foreach (var name in BuildNativeCleanupResourceNames(planned.Names, planned.Networks))
                resourceNames.Add(name);

            return new TeamLabNodeCleanupPlan(nodeId, assetCleanup, resourceNames.ToArray());
        }).ToArray();
    }

    private async Task<IReadOnlyList<TeamLabRuntimeShardDeployment>> BuildPlannedShardDeploymentsForCleanupAsync(
        TeamLabRuntime runtime, int gameId, CancellationToken token)
    {
        var topology = await LoadPublishedTopologyAsync(runtime, gameId, token);
        if (topology is not { Success: true, Config: not null } || runtime.Id <= 0)
            return [];

        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(t => topology.Config.Nodes.Select(n => n.ImageTemplateId).Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, token);
        var plan = TeamLabAssetPlanService.BuildPublishedAssetPlan(topology.Config, runtime.Id, 0,
            templates, runtime.NetworkPrefix);
        return !plan.Success ? [] : BuildShardDeployments(runtime, plan);
    }

    private async Task<TeamLabPublishedTopologyResult> LoadPublishedTopologyAsync(TeamLabRuntime runtime, int gameId,
        CancellationToken token)
    {
        var snapshot = await context.PenetrationPublishedSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.GameId == gameId && s.PublishedVersion == runtime.PublishedVersion, token);
        if (snapshot is null)
            return TeamLabPublishedTopologyResult.Failed("TeamLab published topology snapshot was not found.");

        var templateIds = ExtractTemplateIds(snapshot.SnapshotJson);
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, token);

        return TeamLabPublishedTopologyService.ParsePublishedSnapshot(gameId, runtime.PublishedVersion,
            snapshot.SnapshotJson, templates);
    }

    private async Task<TeamLabDeploymentResult> FailAsync(TeamLabRuntime runtime, string message, CancellationToken token)
    {
        var normalized = NormalizeRuntimeError(message);
        runtime.Status = TeamLabRuntimeStatus.Failed;
        runtime.LastError = normalized;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "deploy", TeamLabEventLevel.Error, normalized);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"TeamLab runtime deployment failed: game={runtime.GameId}, team={runtime.TeamId}, runtime={runtime.Id}, error={normalized}",
            TaskStatus.Failed, LogLevel.Warning);
        return new TeamLabDeploymentResult(false, normalized, runtime);
    }

    private async Task<TeamLabDeploymentResult> MarkRuntimeRunningAsync(TeamLabRuntime runtime, string message,
        CancellationToken token)
    {
        AddEvent(runtime, "probe", TeamLabEventLevel.Success, "TeamLab runtime connectivity probe passed.");

        if (!TeamLabStateMachine.CanTransition(runtime.Status, TeamLabRuntimeStatus.Running))
            return await FailAsync(runtime, "Invalid TeamLab runtime transition to Running.", token);

        runtime.Status = TeamLabRuntimeStatus.Running;
        runtime.IsOpenToPlayers = true;
        runtime.LastError = null;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "deploy", TeamLabEventLevel.Success, message);
        await context.SaveChangesAsync(token);

        logger.LogInformation("TeamLab runtime {RuntimeId} reached Running state.", runtime.Id);
        logger.SystemLog($"TeamLab runtime deployed: game={runtime.GameId}, team={runtime.TeamId}, runtime={runtime.Id}, node={runtime.WorkerNodeId}.",
            TaskStatus.Success, LogLevel.Information);
        return new TeamLabDeploymentResult(true, "TeamLab runtime deployed.", runtime);
    }

    private async Task<int> ResolveTeamIndexAsync(int gameId, int teamId, PenetrationTeamEnvironment? environment,
        CancellationToken token)
    {
        if (environment is not null && (environment.TeamIndex > 0 || !string.IsNullOrWhiteSpace(environment.NetworkPrefix)))
            return environment.TeamIndex;

        var teamIds = await context.Participations.AsNoTracking()
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .OrderBy(p => p.TeamId)
            .Select(p => p.TeamId)
            .ToArrayAsync(token);
        var index = Array.IndexOf(teamIds, teamId);
        return Math.Max(0, index);
    }

    private async Task CleanupCreatedNativeAssetsAsync(Guid workerNodeId, IEnumerable<string> containers,
        IEnumerable<string> vms, CancellationToken token)
    {
        foreach (var containerId in containers.Reverse())
        {
            try
            {
                await agentClient.DestroyContainerAsync(workerNodeId, containerId, token);
            }
            catch (Exception ex) when (ex is InvalidOperationException or AgentClientException or HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Failed to cleanup TeamLab native container {ContainerId}.", containerId);
            }
        }

        foreach (var vmName in vms.Reverse())
        {
            try
            {
                await agentClient.DestroyVmAsync(workerNodeId, vmName, token);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Failed to cleanup TeamLab native VM {VmName}.", vmName);
            }
        }
    }

    private async Task<TeamLabDeploymentResult> FailNativeDeploymentAsync(TeamLabRuntime runtime, string message,
        IReadOnlyList<TeamLabRuntimeShardDeployment> shards, IEnumerable<TeamLabCreatedShardAssets> createdAssets,
        TeamLabAssetSlotCount reservedSlots, bool capacityAlreadyReserved,
        CancellationToken token, bool removePublicMapping = true)
    {
        var cleanupErrors = new List<string>();
        foreach (var created in createdAssets)
            await CleanupCreatedNativeAssetsAsync(created.WorkerNodeId, created.Containers, created.Vms, token);

        foreach (var shard in shards)
        {
            var resourceCleanup = await agentClient.CleanupTeamLabAsync(shard.WorkerNodeId,
                new TeamLabCleanupRequest(runtime.Id,
                    BuildNativeCleanupResourceNames(shard.Names, shard.Networks), _config.DryRun),
                token);
            if (resourceCleanup is not { Success: true })
                cleanupErrors.Add(resourceCleanup?.Message ??
                                  $"TeamLab WorkerNode {shard.WorkerNodeId} cleanup failed after native deployment failure.");
            else if (resourceCleanup.DryRun)
                cleanupErrors.Add(resourceCleanup.Message);

            shard.Shard.Status = cleanupErrors.Count == 0
                ? TeamLabRuntimeStatus.Failed
                : TeamLabRuntimeStatus.CleanupPending;
            shard.Shard.LastError = cleanupErrors.Count == 0 ? null : NormalizeRuntimeError(string.Join('\n', cleanupErrors));
            shard.Shard.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (removePublicMapping && runtime.PublicUdpMapping is not null)
        {
            var gatewayCleanup = await publicUdpGatewayProvider.RemoveMappingAsync(runtime.PublicUdpMapping, token);
            if (!gatewayCleanup.Success)
                cleanupErrors.Add(gatewayCleanup.Message);
        }

        if (cleanupErrors.Count > 0)
        {
            runtime.Status = TeamLabRuntimeStatus.CleanupPending;
            runtime.IsOpenToPlayers = false;
            runtime.LastError = NormalizeRuntimeError($"{message}\nCleanup incomplete: {string.Join('\n', cleanupErrors)}");
            runtime.UpdatedAt = DateTimeOffset.UtcNow;
            AddEvent(runtime, "deploy", TeamLabEventLevel.Error, message);
            AddEvent(runtime, "cleanup", TeamLabEventLevel.Error, runtime.LastError);
            await context.SaveChangesAsync(token);
            return new TeamLabDeploymentResult(false, runtime.LastError, runtime);
        }

        if (!capacityAlreadyReserved)
            await ReleaseReservedTeamLabCapacityAsync(runtime, reservedSlots, token);

        return await FailAsync(runtime, message, token);
    }

    internal async Task ReleaseTeamLabCapacityAsync(TeamLabRuntime runtime, CancellationToken token)
    {
        var slots = CountRuntimeAssetSlots(runtime);
        await ReleaseTeamLabCapacityAsync(runtime, slots, token);
    }

    internal async Task ReleaseTeamLabCapacityAsync(TeamLabRuntime runtime, TeamLabAssetSlotCount slots,
        CancellationToken token)
    {
        foreach (var shardSlots in CountShardSlots(runtime, slots))
            await capacityReservation.ReleaseActiveAsync(shardSlots.WorkerNodeId, shardSlots.DockerSlots,
                shardSlots.VmSlots, token);
    }

    internal async Task ReleaseReservedTeamLabCapacityAsync(TeamLabRuntime runtime, TeamLabAssetSlotCount slots,
        CancellationToken token)
    {
        foreach (var shardSlots in CountShardSlots(runtime, slots))
            await capacityReservation.ReleaseAsync(shardSlots.WorkerNodeId, shardSlots.DockerSlots,
                shardSlots.VmSlots, token);
    }

    internal async Task ConfirmTeamLabCapacityAsync(TeamLabRuntime runtime, TeamLabAssetSlotCount slots,
        CancellationToken token)
    {
        foreach (var shardSlots in CountShardSlots(runtime, slots))
            await capacityReservation.ConfirmAsync(shardSlots.WorkerNodeId, shardSlots.DockerSlots,
                shardSlots.VmSlots, token);
    }

    private async Task<string[]> CleanupTrackedNativeAssetsAsync(Guid workerNodeId,
        TeamLabNativeAssetCleanupPlan plan, CancellationToken token)
    {
        var errors = new List<string>();
        foreach (var containerId in plan.ContainerIds.Reverse())
        {
            try
            {
                await agentClient.DestroyContainerAsync(workerNodeId, containerId, token);
            }
            catch (Exception ex) when (ex is InvalidOperationException or AgentClientException or HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Failed to destroy tracked TeamLab native container {ContainerId}.", containerId);
                errors.Add($"Container {containerId}: {ex.Message}");
            }
        }

        foreach (var vmName in plan.VmNames.Reverse())
        {
            try
            {
                await agentClient.DestroyVmAsync(workerNodeId, vmName, token);
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Failed to destroy tracked TeamLab native VM {VmName}.", vmName);
                errors.Add($"VM {vmName}: {ex.Message}");
            }
        }

        return errors.ToArray();
    }

    private async Task SyncCompatibilityEnvironmentAsync(TeamLabRuntime runtime, PenetrationConfig config,
        TeamLabPublishedAssetPlanResult plan, int teamIndex, CancellationToken token)
    {
        var environment = await context.PenetrationTeamEnvironments
            .Include(e => e.RuntimeNodes)
            .Include(e => e.RuntimeRoutes)
            .FirstOrDefaultAsync(e => e.GameId == runtime.GameId && e.TeamId == runtime.TeamId, token);
        if (environment is null)
        {
            environment = new PenetrationTeamEnvironment
            {
                GameId = runtime.GameId,
                TeamId = runtime.TeamId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.PenetrationTeamEnvironments.Add(environment);
        }

        environment.NodeId = runtime.WorkerNodeId;
        environment.NetworkPrefix = runtime.NetworkPrefix;
        environment.TeamIndex = teamIndex;
        environment.PublishedVersion = runtime.PublishedVersion;
        environment.Status = PenetrationRuntimeStatus.Running;
        environment.LastError = null;
        environment.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (var node in config.Nodes)
        {
            var asset = plan.Assets.FirstOrDefault(a => a.TopologyKey == node.TopologyKey);
            if (asset is null) continue;

            var runtimeNode = environment.RuntimeNodes.FirstOrDefault(r =>
                string.Equals(r.TopologyNodeKey, node.TopologyKey, StringComparison.Ordinal));
            if (runtimeNode is null)
            {
                runtimeNode = new PenetrationRuntimeNode
                {
                    TopologyNodeId = node.Id,
                    TopologyNodeKey = node.TopologyKey,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                environment.RuntimeNodes.Add(runtimeNode);
            }

            var primary = asset.Interfaces.FirstOrDefault(i => i.IsPrimary) ?? asset.Interfaces.FirstOrDefault();
            runtimeNode.TopologyNodeId = node.Id;
            runtimeNode.TopologyNodeKey = node.TopologyKey;
            runtimeNode.NetworkName = primary?.NetworkKey ?? string.Empty;
            runtimeNode.IpAddress = primary?.IpAddress ?? string.Empty;
            runtimeNode.InterfaceSummary = JsonSerializer.Serialize(
                asset.Interfaces.Select(iface =>
                {
                    var network = config.Networks.FirstOrDefault(n =>
                        string.Equals(n.TopologyKey, iface.NetworkKey, StringComparison.Ordinal));
                    return new RuntimeInterfaceSummary(network?.Id ?? 0, iface.NetworkKey,
                        $"{NetworkAddress(iface.IpAddress, iface.PrefixLength)}/{iface.PrefixLength}",
                        iface.IpAddress, iface.IsPrimary);
                }), JsonOptions);
            runtimeNode.PublicPort = null;
            runtimeNode.Status = PenetrationRuntimeStatus.Running;
            runtimeNode.AdminAccessUrl = null;
        }

        environment.RuntimeRoutes.Clear();
    }

    private async Task SyncCompatibilityEnvironmentStatusAsync(TeamLabRuntime runtime, PenetrationRuntimeStatus status,
        string? error, CancellationToken token)
    {
        var environment = await context.PenetrationTeamEnvironments
            .Include(e => e.RuntimeNodes)
            .Include(e => e.RuntimeRoutes)
            .FirstOrDefaultAsync(e => e.GameId == runtime.GameId && e.TeamId == runtime.TeamId, token);
        if (environment is null)
            return;

        environment.Status = status;
        environment.LastError = error;
        environment.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (var runtimeNode in environment.RuntimeNodes)
        {
            runtimeNode.Status = status is PenetrationRuntimeStatus.Stopped
                ? PenetrationRuntimeStatus.Stopped
                : PenetrationRuntimeStatus.CleanupPending;
            runtimeNode.ContainerId = null;
            runtimeNode.PublicPort = null;
            runtimeNode.AdminAccessUrl = null;
        }

        if (status == PenetrationRuntimeStatus.Stopped)
        {
            context.PenetrationRuntimeRoutes.RemoveRange(environment.RuntimeRoutes);
            context.PenetrationRuntimeNodes.RemoveRange(environment.RuntimeNodes);
            environment.CleanupRetryCount = 0;
            environment.NextCleanupAt = null;
            environment.LastCleanupAttemptAt = DateTimeOffset.UtcNow;
        }
        else
        {
            environment.CleanupRetryCount++;
            environment.LastCleanupAttemptAt = DateTimeOffset.UtcNow;
        }
    }

    private static void AddEvent(TeamLabRuntime runtime, string stage, TeamLabEventLevel level, string message) =>
        runtime.Events.Add(BuildRuntimeEvent(stage, level, message));

    public static TeamLabEvent BuildRuntimeEvent(string stage, TeamLabEventLevel level, string message) =>
        new()
        {
            Stage = TrimForColumn(stage, 64),
            Level = level,
            Message = TrimForColumn(message, EventMessageMaxLength),
            Detail = message.Length > EventMessageMaxLength
                ? TrimForColumn(message, EventDetailMaxLength)
                : null
        };

    public static string NormalizeRuntimeError(string? message) =>
        TrimForColumn(string.IsNullOrWhiteSpace(message) ? "TeamLab runtime operation failed." : message,
            RuntimeErrorMaxLength);

    private static string TrimForColumn(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        const string suffix = "...";
        if (maxLength <= suffix.Length)
            return value[..maxLength];

        return value[..(maxLength - suffix.Length)] + suffix;
    }

    public static void RecordRuntimeFacts(TeamLabRuntime runtime, TeamLabResourceNames names, string entryCidr,
        string entryGateway, string labCidr, string labGateway)
    {
        UpsertNetwork(runtime, "entry", "VPN entry network", entryCidr, entryGateway, names.Bridges[0]);
        UpsertNetwork(runtime, "lab", "Lab internal network", labCidr, labGateway, names.Bridges[1]);
        UpsertAsset(runtime, TeamLabResourceKind.RouterNamespace, "router", names.RouterNamespace);
        UpsertAsset(runtime, TeamLabResourceKind.WireGuard, "wireguard", names.WireGuardInterface);
        if (runtime.PublicUdpMapping is not null)
            UpsertAsset(runtime, TeamLabResourceKind.PublicUdpMapping, "public-udp",
                runtime.PublicUdpMapping.PublicUdpPort.ToString());
    }

    public static void RecordNativeRuntimeFacts(TeamLabRuntime runtime, TeamLabResourceNames names,
        IReadOnlyList<TeamLabRuntimeNetworkSpec> networks)
    {
        foreach (var network in networks)
            UpsertNetwork(runtime, network.TopologyKey, network.Name, network.Cidr, network.GatewayIp, network.BridgeName);

        foreach (var request in BuildDhcpDnsRequests(runtime.Id, names.RouterNamespace, networks, [], false))
            UpsertAsset(runtime, TeamLabResourceKind.DhcpDnsService, request.ServiceName, request.ServiceName);

        UpsertAsset(runtime, TeamLabResourceKind.RouterNamespace, "router", names.RouterNamespace);
        UpsertAsset(runtime, TeamLabResourceKind.WireGuard, "wireguard", names.WireGuardInterface);
        if (runtime.PublicUdpMapping is not null)
            UpsertAsset(runtime, TeamLabResourceKind.PublicUdpMapping, "public-udp",
                runtime.PublicUdpMapping.PublicUdpPort.ToString());
    }

    private static void RecordNativeShardRuntimeFacts(TeamLabRuntime runtime, TeamLabRuntimeShardDeployment shard)
    {
        foreach (var network in shard.Networks)
            UpsertNetwork(runtime, network.TopologyKey, network.Name, network.Cidr, network.GatewayIp,
                network.BridgeName, shard.Shard, shard.WorkerNodeId);

        foreach (var request in BuildDhcpDnsRequests(runtime.Id, shard.Names.RouterNamespace, shard.Networks,
                     shard.Assets, false))
            UpsertAsset(runtime, TeamLabResourceKind.DhcpDnsService, request.ServiceName, request.ServiceName,
                shard.Shard, shard.WorkerNodeId);

        UpsertAsset(runtime, TeamLabResourceKind.RouterNamespace, $"router-{shard.WorkerNodeId:N}",
            shard.Names.RouterNamespace, shard.Shard, shard.WorkerNodeId);
        if (FindEntryShard([shard]) is not null)
            UpsertAsset(runtime, TeamLabResourceKind.WireGuard, "wireguard", shard.Names.WireGuardInterface,
                shard.Shard, shard.WorkerNodeId);
        if (runtime.PublicUdpMapping is not null && runtime.WorkerNodeId == shard.WorkerNodeId)
            UpsertAsset(runtime, TeamLabResourceKind.PublicUdpMapping, "public-udp",
                runtime.PublicUdpMapping.PublicUdpPort.ToString(), shard.Shard, shard.WorkerNodeId);

        shard.Shard.Status = TeamLabRuntimeStatus.Running;
        shard.Shard.LastError = null;
        shard.Shard.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void UpsertNetwork(TeamLabRuntime runtime, string topologyKey, string name, string cidr,
        string gatewayIp, string bridgeName, TeamLabRuntimeShard? shard = null, Guid? workerNodeId = null)
    {
        var network = runtime.Networks.FirstOrDefault(n => n.TopologyKey == topologyKey);
        if (network is null)
        {
            network = new TeamLabRuntimeNetwork { TopologyKey = topologyKey };
            runtime.Networks.Add(network);
        }

        network.Name = name;
        network.Cidr = cidr;
        network.GatewayIp = gatewayIp;
        network.BridgeName = bridgeName;
        if (shard is not null)
            network.Shard = shard;
        network.WorkerNodeId = workerNodeId ?? network.WorkerNodeId;
    }

    private static void UpsertAsset(TeamLabRuntime runtime, TeamLabResourceKind kind, string topologyKey,
        string runtimeResourceId, TeamLabRuntimeShard? shard = null, Guid? workerNodeId = null)
    {
        var asset = runtime.Assets.FirstOrDefault(a => a.Kind == kind && a.TopologyKey == topologyKey);
        if (asset is null)
        {
            asset = new TeamLabRuntimeAsset { Kind = kind, TopologyKey = topologyKey };
            runtime.Assets.Add(asset);
        }

        asset.Name = topologyKey;
        asset.RuntimeResourceId = runtimeResourceId;
        asset.Status = TeamLabRuntimeStatus.Running;
        asset.LastError = null;
        if (shard is not null)
            asset.Shard = shard;
        asset.WorkerNodeId = workerNodeId ?? asset.WorkerNodeId;
    }

    public static void RecordRuntimeAsset(TeamLabRuntime runtime, TeamLabRuntimeAssetSpec spec)
    {
        var asset = runtime.Assets.FirstOrDefault(a => a.Kind == spec.Kind && a.TopologyKey == spec.TopologyKey);
        if (asset is null)
        {
            asset = new TeamLabRuntimeAsset { Kind = spec.Kind, TopologyKey = spec.TopologyKey };
            runtime.Assets.Add(asset);
        }

        asset.Name = spec.Name;
        asset.RuntimeResourceId = spec.RuntimeResourceId;
        asset.SourceTemplateId = spec.SourceTemplateId;
        asset.Image = spec.Image;
        asset.NetworkKey = spec.NetworkKey;
        asset.IpAddress = spec.IpAddress;
        asset.MacAddress = spec.MacAddress;
        asset.InterfaceSummaryJson = string.IsNullOrWhiteSpace(spec.InterfaceSummaryJson)
            ? "[]"
            : spec.InterfaceSummaryJson;
        asset.Status = TeamLabRuntimeStatus.Running;
        asset.LastError = null;
    }

    public static void MarkRuntimeFactsDestroyed(TeamLabRuntime runtime)
    {
        foreach (var asset in runtime.Assets)
        {
            asset.Status = TeamLabRuntimeStatus.Destroyed;
            asset.LastError = null;
        }
    }

    public static TeamLabAssetSlotCount CountRuntimeAssetSlots(TeamLabRuntime runtime) =>
        new(
            runtime.Assets.Count(asset => asset.Kind == TeamLabResourceKind.Docker &&
                                          asset.Status != TeamLabRuntimeStatus.Destroyed),
            runtime.Assets.Count(asset => asset.Kind == TeamLabResourceKind.Vm &&
                                          asset.Status != TeamLabRuntimeStatus.Destroyed));

    public static IReadOnlyList<TeamLabShardSlotCount> CountShardSlots(TeamLabRuntime runtime,
        TeamLabAssetSlotCount fallbackSlots)
    {
        var shardSlots = runtime.Shards
            .Where(shard => shard.Assets.Count > 0)
            .Select(shard => new TeamLabShardSlotCount(
                shard.WorkerNodeId,
                shard.Assets.Count(asset => asset.Kind == TeamLabResourceKind.Docker &&
                                            asset.Status != TeamLabRuntimeStatus.Destroyed),
                shard.Assets.Count(asset => asset.Kind == TeamLabResourceKind.Vm &&
                                            asset.Status != TeamLabRuntimeStatus.Destroyed)))
            .Where(slots => slots.DockerSlots > 0 || slots.VmSlots > 0)
            .GroupBy(slots => slots.WorkerNodeId)
            .Select(group => new TeamLabShardSlotCount(
                group.Key,
                group.Sum(slots => slots.DockerSlots),
                group.Sum(slots => slots.VmSlots)))
            .OrderBy(slots => slots.WorkerNodeId)
            .ToArray();

        if (shardSlots.Length > 0)
            return shardSlots;

        return runtime.WorkerNodeId is { } nodeId &&
               (fallbackSlots.DockerSlots > 0 || fallbackSlots.VmSlots > 0)
            ? [new TeamLabShardSlotCount(nodeId, fallbackSlots.DockerSlots, fallbackSlots.VmSlots)]
            : [];
    }

    private static string TrimLinuxName(string value) => value.Length <= 15 ? value : value[..15];

    private static string BuildFabricUplinkHostInterfaceName(int runtimeId) => TrimLinuxName($"tlrf{runtimeId}");

    private static string BuildFabricUplinkHostAddressCidr(int runtimeId, int shardOrdinal) =>
        $"{BuildFabricUplinkAddress(runtimeId, shardOrdinal, 1)}/30";

    private static string BuildFabricUplinkPeerAddressCidr(int runtimeId, int shardOrdinal) =>
        $"{BuildFabricUplinkAddress(runtimeId, shardOrdinal, 2)}/30";

    private static string BuildFabricUplinkPeerAddress(int runtimeId, int shardOrdinal) =>
        BuildFabricUplinkAddress(runtimeId, shardOrdinal, 2);

    private static string BuildFabricUplinkAddress(int runtimeId, int shardOrdinal, int hostOffset)
    {
        const int blocksPerRuntime = 32;
        const int totalBlocks = 16384;
        var runtimeBucket = Math.Abs(runtimeId % (totalBlocks / blocksPerRuntime));
        var shardBucket = Math.Abs(shardOrdinal % blocksPerRuntime);
        var normalized = runtimeBucket * blocksPerRuntime + shardBucket;
        var thirdOctet = normalized / 64;
        var fourthOctet = (normalized % 64) * 4 + hostOffset;
        return $"169.254.{thirdOctet}.{fourthOctet}";
    }

    private static TeamLabStaticRouteRequest[] BuildLocalFabricRoutes(
        IReadOnlyList<TeamLabRuntimeNetworkSpec> networks, int runtimeId, int shardOrdinal) =>
        networks
            .Select(network => new TeamLabStaticRouteRequest(network.Cidr,
                BuildFabricUplinkPeerAddress(runtimeId, shardOrdinal)))
            .GroupBy(route => route.TargetCidr, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(route => route.TargetCidr, StringComparer.Ordinal)
            .ToArray();

    private static string ResolveNamespaceRouteSourceIp(IReadOnlyList<TeamLabRuntimeNetworkSpec> networks) =>
        networks
            .OrderBy(network => string.Equals(network.TopologyKey, "entry", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(network => network.TopologyKey, StringComparer.Ordinal)
            .Select(network => network.GatewayIp)
            .FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip)) ?? "";

    private static string BuildRouterNamespaceInterfaceName(string routerNamespace, int index) =>
        TrimLinuxName($"{routerNamespace}n{index}");

    private static string BuildDhcpDnsServiceName(int runtimeId, string networkKey) =>
        TrimLinuxName($"tldns{runtimeId}{NormalizeResourceToken(networkKey)}");

    private static int ParseRuntimeId(string routerNamespace)
    {
        const string prefix = "tlr";
        return routerNamespace.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(routerNamespace[prefix.Length..], out var runtimeId)
            ? runtimeId
            : 0;
    }

    private static string NormalizeResourceToken(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var token = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(token) ? "asset" : token;
    }

    private static HashSet<int> ExtractTemplateIds(string snapshotJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes) ||
                nodes.ValueKind != JsonValueKind.Array)
                return [];

            return nodes.EnumerateArray()
                .Select(node => node.TryGetProperty("imageTemplateId", out var prop) &&
                                prop.ValueKind == JsonValueKind.Number &&
                                prop.TryGetInt32(out var id)
                    ? id
                    : 0)
                .Where(id => id > 0)
                .ToHashSet();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, string> BuildNativeEnvironmentVariables(PenetrationConfig config,
        string topologyKey, TeamLabPublishedAssetPlanResult plan, int gameId, int teamId, int version)
    {
        var node = config.Nodes.FirstOrDefault(n => string.Equals(n.TopologyKey, topologyKey, StringComparison.Ordinal));
        if (node is null)
            return [];

        var envVars = DeserializeDictionary(node.EnvironmentVariables);
        ResolveNativeEnvironmentPlaceholders(envVars, plan);
        var flagMap = BuildFlagMap(node, gameId, teamId, version);
        InjectFlagEnvironmentVariables(envVars, node.ScoreItems, flagMap);
        return envVars;
    }

    private static string? BuildPrimaryFlag(PenetrationConfig config, string topologyKey, int gameId, int teamId,
        int version)
    {
        var node = config.Nodes.FirstOrDefault(n => string.Equals(n.TopologyKey, topologyKey, StringComparison.Ordinal));
        if (node is null)
            return null;

        return BuildFlagMap(node, gameId, teamId, version).Values.FirstOrDefault();
    }

    private static Dictionary<int, string> BuildFlagMap(PenetrationNode node, int gameId, int teamId, int version) =>
        node.ScoreItems
            .Where(i => i.IsDynamic || !string.IsNullOrWhiteSpace(i.StaticFlag))
            .OrderBy(i => i.OrderIndex)
            .ToDictionary(i => i.Id, i => BuildFlag(i, gameId, teamId, version));

    private static string BuildFlag(PenetrationScoreItem item, int gameId, int teamId, int version)
    {
        if (!item.IsDynamic)
            return item.StaticFlag ?? string.Empty;

        var nodeKey = string.IsNullOrWhiteSpace(item.Node?.TopologyKey) ? item.NodeId.ToString() : item.Node.TopologyKey;
        var scoreKey = string.IsNullOrWhiteSpace(item.TopologyKey) ? item.Id.ToString() : item.TopologyKey;
        var token = $"{gameId}:{teamId}:{nodeKey}:{scoreKey}:{version}".ToSHA256String()[..16];
        var template = string.IsNullOrWhiteSpace(item.FlagTemplate) ? "flag{[TEAM_HASH]}" : item.FlagTemplate;
        return template.Replace("[TEAM_HASH]", token, StringComparison.OrdinalIgnoreCase)
            .Replace("[TOKEN]", token, StringComparison.OrdinalIgnoreCase);
    }

    private static void InjectFlagEnvironmentVariables(Dictionary<string, string> envVars,
        IEnumerable<PenetrationScoreItem> scoreItems, Dictionary<int, string> flagMap)
    {
        if (flagMap.Count == 0)
            return;

        envVars["GZCTF_FLAG"] = flagMap.Values.First();

        var items = scoreItems.ToDictionary(i => i.Id);
        foreach (var (itemId, flag) in flagMap)
        {
            envVars[$"GZCTF_FLAG_{itemId}"] = flag;
            if (!items.TryGetValue(itemId, out var item))
                continue;

            var key = ToEnvKey(item.Title);
            if (!string.IsNullOrWhiteSpace(key))
                envVars[$"GZCTF_FLAG_{key}"] = flag;
        }
    }

    private static Dictionary<string, string> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void ResolveNativeEnvironmentPlaceholders(Dictionary<string, string> envVars,
        TeamLabPublishedAssetPlanResult plan)
    {
        if (envVars.Count == 0)
            return;

        var assetsByKey = plan.Assets
            .Where(a => !string.IsNullOrWhiteSpace(a.TopologyKey))
            .ToDictionary(a => a.TopologyKey, StringComparer.Ordinal);

        foreach (var key in envVars.Keys.ToArray())
            envVars[key] = ReplaceNativeEnvironmentPlaceholders(envVars[key], assetsByKey);
    }

    private static string ReplaceNativeEnvironmentPlaceholders(string value,
        IReadOnlyDictionary<string, TeamLabAssetSpec> assetsByKey)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            (!value.Contains("{{asset:", StringComparison.Ordinal) &&
             !value.Contains("{{node:", StringComparison.Ordinal)))
            return value;

        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var (start, tokenLength) = FindNextAssetPlaceholder(value, index);
            if (start < 0)
            {
                builder.Append(value, index, value.Length - index);
                break;
            }

            builder.Append(value, index, start - index);
            var end = value.IndexOf("}}", start + tokenLength, StringComparison.Ordinal);
            if (end < 0)
            {
                builder.Append(value, start, value.Length - start);
                break;
            }

            var expression = value[(start + tokenLength)..end];
            builder.Append(ResolveNativeNodePlaceholder(expression, assetsByKey) ?? value[start..(end + 2)]);
            index = end + 2;
        }

        return builder.ToString();
    }

    private static (int Start, int TokenLength) FindNextAssetPlaceholder(string value, int index)
    {
        var assetStart = value.IndexOf("{{asset:", index, StringComparison.Ordinal);
        var legacyNodeStart = value.IndexOf("{{node:", index, StringComparison.Ordinal);
        if (assetStart < 0)
            return legacyNodeStart < 0 ? (-1, 0) : (legacyNodeStart, "{{node:".Length);
        if (legacyNodeStart < 0 || assetStart < legacyNodeStart)
            return (assetStart, "{{asset:".Length);
        return (legacyNodeStart, "{{node:".Length);
    }

    private static string? ResolveNativeNodePlaceholder(string expression,
        IReadOnlyDictionary<string, TeamLabAssetSpec> assetsByKey)
    {
        var parts = expression.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;

        if (!assetsByKey.TryGetValue(parts[0], out var asset))
            return null;

        var primary = asset.Interfaces.FirstOrDefault(i => i.IsPrimary) ?? asset.Interfaces.FirstOrDefault();
        if (primary is null || string.IsNullOrWhiteSpace(primary.IpAddress))
            return null;

        return parts[1].ToLowerInvariant() switch
        {
            "host" or "ip" => primary.IpAddress,
            "url" => BuildInternalNodeUrl(primary.IpAddress,
                parts.Length >= 3 && int.TryParse(parts[2], out var explicitPort)
                    ? explicitPort
                    : asset.ExposePort),
            "port" => asset.ExposePort.ToString(),
            _ => null
        };
    }

    private static string BuildInternalNodeUrl(string host, int port)
    {
        var scheme = port == 443 ? "https" : "http";
        return $"{scheme}://{host}:{port}";
    }

    private static string ToEnvKey(string value)
    {
        var chars = value.Trim().ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }

    private static string NetworkAddress(string ipAddress, int prefixLength)
    {
        if (!System.Net.IPAddress.TryParse(ipAddress, out var ip) || prefixLength is < 1 or > 32)
            return ipAddress;

        var value = ToUInt32(ip);
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        return new System.Net.IPAddress(FromUInt32(value & mask)).ToString();
    }

    private static int StableChallengeId(string topologyKey)
    {
        unchecked
        {
            var hash = 17;
            foreach (var ch in topologyKey)
                hash = hash * 31 + ch;

            return Math.Abs(hash == int.MinValue ? 0 : hash);
        }
    }

    private sealed record TeamLabRuntimeInterfaceFact(
        [property: JsonPropertyName("networkKey")] string NetworkKey,
        [property: JsonPropertyName("bridgeName")] string BridgeName,
        [property: JsonPropertyName("interfaceName")] string InterfaceName,
        [property: JsonPropertyName("ipAddress")] string IpAddress,
        [property: JsonPropertyName("prefixLength")] int PrefixLength,
        [property: JsonPropertyName("macAddress")] string MacAddress,
        [property: JsonPropertyName("isPrimary")] bool IsPrimary)
    {
        public static TeamLabRuntimeInterfaceFact FromSpec(TeamLabAssetInterfaceSpec spec) =>
            new(spec.NetworkKey, spec.BridgeName, spec.InterfaceName, spec.IpAddress, spec.PrefixLength,
                spec.MacAddress, spec.IsPrimary);
    }

    private static string PrefixLength(string cidr)
    {
        var parts = cidr.Split('/');
        return parts.Length == 2 ? parts[1] : string.Empty;
    }

    private static string SecondHost(string cidr) => HostAt(cidr, 2);

    private static string LastHost(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var ip) ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 1 or > 30)
            return string.Empty;

        var network = ToUInt32(ip);
        var size = 1u << (32 - prefix);
        return new System.Net.IPAddress(FromUInt32(network + size - 2)).ToString();
    }

    private static string HostAt(string cidr, uint offset)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var ip) ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 1 or > 30)
            return string.Empty;

        var size = 1u << (32 - prefix);
        if (offset == 0 || offset >= size - 1)
            return string.Empty;

        return new System.Net.IPAddress(FromUInt32(ToUInt32(ip) + offset)).ToString();
    }

    private static uint ToUInt32(System.Net.IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            ? ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3]
            : 0;
    }

    private static byte[] FromUInt32(uint value) =>
    [
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    ];

    private sealed record RuntimeInterfaceSummary(
        int NetworkId,
        string NetworkName,
        string Cidr,
        string IpAddress,
        bool IsPrimary);
}
