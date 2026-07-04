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

public sealed record TeamLabDeploymentResult(bool Success, string Message, [property: JsonIgnore] TeamLabRuntime? Runtime)
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
    IPublicUdpGatewayProvider publicUdpGatewayProvider,
    IOptions<TeamLabNetworkConfig> options,
    ILogger<TeamLabDeploymentService> logger)
{
    private const int RuntimeErrorMaxLength = 1024;
    private const int EventMessageMaxLength = 256;
    private const int EventDetailMaxLength = 1024;
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
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return networkResources;
    }

    public static string[] BuildNativeCleanupResourceNames(TeamLabResourceNames names,
        IReadOnlyList<TeamLabRuntimeNetworkSpec> networks) =>
        networks
            .Select(network => network.BridgeName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Concat(networks.Select(network =>
                BuildDhcpDnsServiceName(ParseRuntimeId(names.RouterNamespace), network.TopologyKey)))
            .Append(names.RouterNamespace)
            .Append(names.WireGuardInterface)
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

    public static ContainerConfig BuildNativeDockerContainerConfig(TeamLabAssetSpec spec, int teamId,
        Guid workerNodeId, string? flag)
    {
        if (spec.Kind != TeamLabAssetSpecKind.Docker)
            throw new ArgumentException("TeamLab native Docker config requires a Docker asset spec.", nameof(spec));

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
            PreferredNodeId = workerNodeId
        };
    }

    public static async Task<ContainerConfig> BuildResolvedNativeDockerContainerConfigAsync(TeamLabAssetSpec spec,
        int teamId, Guid workerNodeId, string? flag, DockerImageRegistryService dockerRegistry,
        CancellationToken token)
    {
        await Task.CompletedTask;
        var config = BuildNativeDockerContainerConfig(spec, teamId, workerNodeId, flag);
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

        return new AgentCreateVmRequest
        {
            TemplateId = spec.SourceTemplateId,
            VmName = TrimLinuxName($"{TeamLabPlanService.BuildRuntimeResourcePrefix(runtimeId)}-{NormalizeResourceToken(spec.TopologyKey)}"),
            Memory = spec.MemoryLimit,
            Cpu = spec.CpuCount,
            Flag = flag,
            Interfaces = spec.Interfaces.Select(TeamLabAssetPlanService.ToVmInterfaceRequest).ToList()
        };
    }

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
                $"VM {spec.Name} is not ready. Current status: {response.Status}.");

        if (!string.Equals(response.IpAddress, expectedIp, StringComparison.Ordinal))
            return new TeamLabVmReadyValidationResult(false,
                $"VM {spec.Name} acquired IP {response.IpAddress ?? "<none>"} but expected {expectedIp}.");

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

        return await DeployNativeRuntimeAsync(runtime, gameId, teamId, penetrationEnvironment, token);
    }

    private async Task<TeamLabDeploymentResult> DeployNativeRuntimeAsync(TeamLabRuntime runtime, int gameId, int teamId,
        PenetrationTeamEnvironment? existingEnvironment, CancellationToken token)
    {
        runtime.Status = TeamLabRuntimeStatus.Deploying;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "deploy", TeamLabEventLevel.Info, "Starting native TeamLab deployment from published topology.");
        await context.SaveChangesAsync(token);

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

        var names = BuildResourceNames(runtime.Id, plan.Networks.Select(n => n.TopologyKey).ToArray());
        var vpnServerAddress = LastHost(runtime.NetworkPrefix);
        var vpnClientAddress = SecondHost(runtime.NetworkPrefix);
        if (string.IsNullOrWhiteSpace(vpnServerAddress) || string.IsNullOrWhiteSpace(vpnClientAddress))
            return await FailAsync(runtime, "TeamLab runtime network prefix is invalid.", token);

        var allowedIps = string.Join(',', plan.Networks.Select(n => n.Cidr).Distinct(StringComparer.Ordinal));
        TeamLabPeerMaterial peer;
        try
        {
            peer = wireGuardService.EnsurePeer(runtime, $"{vpnClientAddress}/32", allowedIps, string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            return await FailAsync(runtime, ex.Message, token);
        }

        foreach (var network in plan.Networks)
        {
            var bridge = await agentClient.CreateTeamLabBridgeAsync(runtime.WorkerNodeId!.Value,
                new TeamLabBridgeRequest(runtime.Id, network.BridgeName, network.Cidr, _config.DryRun), token);
            if (bridge is not { Success: true })
                return await FailAsync(runtime, bridge?.Message ?? $"Failed to create TeamLab bridge {network.Name}.", token);
            if (bridge.DryRun)
                return await FailAsync(runtime, bridge.Message, token);
        }

        var router = await agentClient.CreateTeamLabRouterAsync(runtime.WorkerNodeId!.Value,
            new TeamLabRouterRequest(runtime.Id, names.RouterNamespace,
                plan.Networks.Select(network =>
                    new TeamLabRouterInterfaceRequest(network.BridgeName,
                        $"{network.GatewayIp}/{PrefixLength(network.Cidr)}")).ToArray(),
                [],
                _config.DryRun), token);
        if (router is not { Success: true })
            return await FailAsync(runtime, router?.Message ?? "Failed to create TeamLab router namespace.", token);
        if (router.DryRun)
            return await FailAsync(runtime, router.Message, token);

        var wg = await agentClient.ConfigureTeamLabWireGuardAsync(runtime.WorkerNodeId.Value,
            new TeamLabWireGuardRequest(runtime.Id, names.RouterNamespace, names.WireGuardInterface,
                runtime.PublicUdpMapping!.WorkerWireGuardPort,
                $"{vpnServerAddress}/32",
                peer.ServerPrivateKey,
                peer.Peer.PublicKey,
                peer.Peer.ClientAddress,
                peer.Peer.AllowedIPs,
                _config.DryRun), token);
        if (wg is not { Success: true })
            return await FailAsync(runtime, wg?.Message ?? "Failed to configure TeamLab WireGuard endpoint.", token);
        if (wg.DryRun)
            return await FailAsync(runtime, wg.Message, token);

        var createdContainers = new List<string>();
        var createdVms = new List<string>();

        var dhcpDnsRequests = BuildDhcpDnsRequests(runtime.Id, names.RouterNamespace, plan.Networks, plan.Assets,
            _config.DryRun);
        foreach (var dhcpDnsRequest in dhcpDnsRequests)
        {
            var dhcpDns = await agentClient.ConfigureTeamLabDhcpDnsAsync(runtime.WorkerNodeId.Value,
                dhcpDnsRequest, token);
            if (dhcpDns is not { Success: true })
                return await FailNativeDeploymentAsync(runtime,
                    dhcpDns?.Message ?? $"Failed to configure TeamLab DHCP/DNS service {dhcpDnsRequest.ServiceName}.",
                    names, plan.Networks, createdContainers, createdVms, token);
            if (dhcpDns.DryRun)
                return await FailNativeDeploymentAsync(runtime, dhcpDns.Message, names, plan.Networks,
                    createdContainers, createdVms, token);

            var probeName = dhcpDnsRequest.DnsRecords.FirstOrDefault()?.Hostname;
            if (!string.IsNullOrWhiteSpace(probeName))
            {
                var dnsProbe = await agentClient.ProbeTeamLabDhcpDnsAsync(runtime.WorkerNodeId.Value,
                    new TeamLabDhcpDnsProbeRequest(runtime.Id, names.RouterNamespace, dhcpDnsRequest.GatewayIp,
                        $"{probeName}.{dhcpDnsRequest.Domain}", _config.DryRun), token);
                if (dnsProbe is not { Success: true })
                    return await FailNativeDeploymentAsync(runtime,
                        dnsProbe?.Message ?? $"Failed to probe TeamLab DHCP/DNS service {dhcpDnsRequest.ServiceName}.",
                        names, plan.Networks, createdContainers, createdVms, token);
                if (dnsProbe.DryRun)
                    return await FailNativeDeploymentAsync(runtime, dnsProbe.Message, names, plan.Networks,
                        createdContainers, createdVms, token);
            }
        }

        try
        {
            foreach (var asset in plan.Assets)
            {
                var flag = BuildPrimaryFlag(topology.Config, asset.TopologyKey, gameId, teamId, runtime.PublishedVersion);
                if (asset.Kind == TeamLabAssetSpecKind.Docker)
                {
                    var config = await BuildResolvedNativeDockerContainerConfigAsync(asset, teamId,
                        runtime.WorkerNodeId.Value, flag, dockerRegistry, token);
                    config.EnvironmentVariables = BuildNativeEnvironmentVariables(topology.Config, asset.TopologyKey,
                        plan, gameId, teamId, runtime.PublishedVersion);
                    var container = await agentClient.CreateContainerOrThrowAsync(runtime.WorkerNodeId.Value, config, token);
                    createdContainers.Add(container.ContainerId);

                    var attachRequests = BuildNativeContainerAttachRequests(runtime.Id, container.ContainerId, asset,
                        plan.Networks, $"{vpnClientAddress}/32",
                        routeMatrix.AllowedCidrsByNetworkKey,
                        plan.Networks.Select(network => network.GatewayIp).ToArray(), _config.DryRun);
                    foreach (var attachRequest in attachRequests)
                    {
                        var attach = await agentClient.AttachTeamLabContainerAsync(runtime.WorkerNodeId.Value,
                            attachRequest, token);
                        if (attach is not { Success: true })
                            throw new InvalidOperationException(attach?.Message ??
                                                                $"Failed to attach container {asset.Name} to TeamLab network.");
                        if (attach.DryRun)
                            throw new InvalidOperationException(attach.Message);
                    }

                    RecordRuntimeAsset(runtime, BuildRuntimeAssetRecord(asset, container.ContainerId));
                }
                else
                {
                    var vmRequest = BuildNativeVmRequest(runtime.Id, asset, flag);
                    var vm = await agentClient.CreateVmAsync(runtime.WorkerNodeId.Value, vmRequest, token);
                    if (vm is null || string.IsNullOrWhiteSpace(vm.VmName))
                        throw new InvalidOperationException($"Failed to create TeamLab VM {asset.Name}.");

                    createdVms.Add(vm.VmName);
                    var vmIp = await agentClient.GetVmIpAsync(runtime.WorkerNodeId.Value, vm.VmName,
                        vmRequest.Interfaces, token);
                    var vmReady = ValidateNativeVmReady(asset, vmIp);
                    if (!vmReady.Success)
                        throw new InvalidOperationException(vmReady.Message);

                    RecordRuntimeAsset(runtime, BuildRuntimeAssetRecord(asset, vm.VmName));
                }

                await context.SaveChangesAsync(token);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or AgentClientException or HttpRequestException or TaskCanceledException)
        {
            return await FailNativeDeploymentAsync(runtime, ex.Message, names, plan.Networks,
                createdContainers, createdVms, token);
        }

        var gateway = await publicUdpGatewayProvider.SyncMappingAsync(runtime.PublicUdpMapping, token);
        if (!gateway.Success)
            return await FailNativeDeploymentAsync(runtime, gateway.Message, names, plan.Networks,
                createdContainers, createdVms, token, removePublicMapping: false);

        RecordNativeRuntimeFacts(runtime, names, plan.Networks);

        runtime.Status = TeamLabRuntimeStatus.Probing;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "probe", TeamLabEventLevel.Info, "Native TeamLab assets created; starting runtime connectivity probe.");
        await context.SaveChangesAsync(token);

        var probeTarget = plan.Assets.SelectMany(a => a.Interfaces)
            .OrderByDescending(i => i.IsPrimary)
            .Select(i => i.IpAddress)
            .FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip));
        if (string.IsNullOrWhiteSpace(probeTarget))
            return await FailAsync(runtime, "TeamLab probe target is unavailable in the native asset plan.", token);

        var probe = await agentClient.ProbeTeamLabAsync(runtime.WorkerNodeId.Value,
            new TeamLabProbeRequest(runtime.Id, names.RouterNamespace, probeTarget, _config.DryRun), token);
        if (probe is not { Success: true })
            return await FailNativeDeploymentAsync(runtime, probe?.Message ?? "TeamLab runtime connectivity probe failed.",
                names, plan.Networks, createdContainers, createdVms, token);
        if (probe.DryRun)
            return await FailNativeDeploymentAsync(runtime, probe.Message, names, plan.Networks,
                createdContainers, createdVms, token);

        await SyncCompatibilityEnvironmentAsync(runtime, topology.Config, plan, teamIndex, token);

        return await MarkRuntimeRunningAsync(runtime, "Native TeamLab runtime deployment reached running state.", token);
    }

    public async Task<TeamLabDeploymentResult> DestroyRuntimeAsync(int gameId, int teamId, CancellationToken token)
    {
        var runtime = await LoadRuntimeAsync(gameId, teamId, token);
        if (runtime is null)
            return new TeamLabDeploymentResult(false, "TeamLab runtime was not found.", null);

        if (!TeamLabStateMachine.CanTransition(runtime.Status, TeamLabRuntimeStatus.Destroying))
            return new TeamLabDeploymentResult(false, $"Cannot destroy TeamLab runtime from status {runtime.Status}.", runtime);

        runtime.Status = TeamLabRuntimeStatus.Destroying;
        runtime.IsOpenToPlayers = false;
        AddEvent(runtime, "destroy", TeamLabEventLevel.Info, "Destroying TeamLab runtime resources.");
        await context.SaveChangesAsync(token);

        var cleanupErrors = new List<string>();

        if (runtime.WorkerNodeId.HasValue)
        {
            var assetCleanup = await CleanupTrackedNativeAssetsAsync(runtime.WorkerNodeId.Value,
                BuildNativeAssetCleanupPlan(runtime), token);
            cleanupErrors.AddRange(assetCleanup);

            var resourceNames = await BuildDestroyResourceNamesAsync(runtime, gameId, token);
            var cleanup = await agentClient.CleanupTeamLabAsync(runtime.WorkerNodeId.Value,
                new TeamLabCleanupRequest(runtime.Id, resourceNames, _config.DryRun), token);
            if (cleanup is not { Success: true })
                cleanupErrors.Add(cleanup?.Message ?? "TeamLab WorkerNode cleanup failed.");
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
            return new TeamLabDeploymentResult(false, runtime.LastError, runtime);
        }

        runtime.Status = TeamLabRuntimeStatus.Destroyed;
        MarkRuntimeFactsDestroyed(runtime);
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "destroy", TeamLabEventLevel.Success, "TeamLab runtime destroyed.");
        await SyncCompatibilityEnvironmentStatusAsync(runtime, PenetrationRuntimeStatus.Stopped, null, token);
        await context.SaveChangesAsync(token);

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
        TeamLabResourceNames names, IReadOnlyList<TeamLabRuntimeNetworkSpec> networks, IEnumerable<string> containers,
        IEnumerable<string> vms, CancellationToken token, bool removePublicMapping = true)
    {
        var cleanupErrors = new List<string>();
        await CleanupCreatedNativeAssetsAsync(runtime.WorkerNodeId!.Value, containers, vms, token);

        var resourceCleanup = await agentClient.CleanupTeamLabAsync(runtime.WorkerNodeId.Value,
            new TeamLabCleanupRequest(runtime.Id, BuildNativeCleanupResourceNames(names, networks), _config.DryRun),
            token);
        if (resourceCleanup is not { Success: true })
            cleanupErrors.Add(resourceCleanup?.Message ?? "TeamLab WorkerNode cleanup failed after native deployment failure.");
        else if (resourceCleanup.DryRun)
            cleanupErrors.Add(resourceCleanup.Message);

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

        return await FailAsync(runtime, message, token);
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

    private static void UpsertNetwork(TeamLabRuntime runtime, string topologyKey, string name, string cidr,
        string gatewayIp, string bridgeName)
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
    }

    private static void UpsertAsset(TeamLabRuntime runtime, TeamLabResourceKind kind, string topologyKey,
        string runtimeResourceId)
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

    private static string TrimLinuxName(string value) => value.Length <= 15 ? value : value[..15];

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
