using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Observation;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Agent.Services.Vm;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public partial class TeamLabNetworkService(
    IOptions<AgentTeamLabConfig> options,
    TeamLabCommandRunner runner,
    TeamLabBridgeService bridgeService,
    TeamLabRouterService routerService,
    TeamLabFabricService fabricService,
    TeamLabFirewallService firewallService,
    ObservationPointRegistry observationRegistry,
    ObservationBatchSpool observationSpool,
    EndpointSensorChannelService endpointSensors,
    TeamLabPcapService pcapService,
    VmBootstrapService bootstrapService,
    TeamLabRuntimeGenerationStore generationStore,
    AgentResourceLock resourceLock,
    ILogger<TeamLabNetworkService> logger)
{
    private readonly AgentTeamLabConfig _config = options.Value;

    public Task<TeamLabStatusResponse> GetStatusAsync(CancellationToken token)
    {
        var hasIp = HasCommand("ip");
        var hasDocker = HasCommand("docker");
        var hasKvm = HasCommand("virsh");
        var hasKvmDevice = File.Exists("/dev/kvm");
        var hasCpuVirtualization = HasCpuVirtualizationFlag();
        var hasWg = HasCommand("wg");
        var hasIptables = HasCommand("iptables");
        var hasNft = HasCommand("nft");
        var hasTcpdump = HasCommand("tcpdump");
        var hasDumpcap = HasCommand("dumpcap");
        var hasDnsProbe = HasCommand("dig");
        var fabric = ResolveFabricInterface();
        var available = hasIp && hasWg && (hasIptables || hasNft);
        var missing = new List<string>();
        if (!hasIp) missing.Add("iproute2/ip");
        if (!hasWg) missing.Add("wireguard-tools/wg");
        if (!hasIptables && !hasNft) missing.Add("iptables or nftables");
        var message = available ? null : $"Missing TeamLab network dependency: {string.Join(", ", missing)}.";
        var capabilities = new TeamLabToolCapabilityReport(
            hasDocker,
            hasKvm,
            hasKvmDevice,
            hasCpuVirtualization,
            hasWg,
            hasIptables,
            hasNft,
            hasTcpdump,
            hasDumpcap,
            hasDnsProbe);

        return Task.FromResult(new TeamLabStatusResponse(
            available,
            _config.Enable,
            _config.DryRun,
            hasIp,
            hasDocker,
            hasKvm,
            hasWg,
            hasIptables,
            hasNft,
            hasTcpdump,
            hasDumpcap,
            capabilities,
            DateTimeOffset.UtcNow,
            message,
            fabric.InterfaceName,
            fabric.IpAddress,
            fabric.Ready));
    }

    private (string InterfaceName, string? IpAddress, bool Ready) ResolveFabricInterface()
    {
        var interfaceName = _config.FabricInterfaceName.Trim();
        if (string.IsNullOrWhiteSpace(interfaceName))
            return (string.Empty, null, false);
        var network = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(item => string.Equals(item.Name, interfaceName, StringComparison.Ordinal));
        var address = network?.GetIPProperties().UnicastAddresses
            .Select(item => item.Address)
            .FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(item));
        return (interfaceName, address?.ToString(),
            network?.OperationalStatus == OperationalStatus.Up && address is not null);
    }

    public async Task<TeamLabInfrastructureApplyResponse> ApplyInfrastructureAsync(
        TeamLabInfrastructureApplyRequest request,
        CancellationToken token)
    {
        var validation = ValidateInfrastructureRequest(request);
        if (validation is not null)
            return InfrastructureFailure(validation, request.DryRun);

        await using var runtimeLock = await resourceLock.AcquireAsync(RuntimeLockKey(request.RuntimeId), token);
        TeamLabActiveGeneration? activeGeneration;
        try
        {
            activeGeneration = await generationStore.ReadAsync(request.RuntimeId, token);
        }
        catch (InvalidDataException exception)
        {
            return InfrastructureFailure(exception.Message, request.DryRun);
        }
        if (activeGeneration?.Generation > request.Generation)
            return InfrastructureFailure(
                $"Stale TeamLab infrastructure generation {request.Generation}; active generation is {activeGeneration.Generation}.",
                request.DryRun);

        var normalized = NormalizeInfrastructureRequest(request);
        var fabricRequest = BuildFabricRequest(normalized, request.DryRun);
        var digest = ComputeDesiredStateDigest(normalized);
        var resources = BuildInfrastructureFacts(normalized);
        var statePath = ResolveDesiredStatePath(request.RuntimeId, request.Generation);
        if (!_config.DryRun && !request.DryRun && _config.Enable)
        {
            var stateDigestMatches = await DesiredStateMatchesAsync(statePath, digest, token);
            var liveState = await ProbeInfrastructureFactsAsync(normalized, token);
            if (liveState.Success)
            {
                var peerRoutes = await fabricService.EnsurePeerRoutesAsync(fabricRequest, token);
                if (!peerRoutes.Success)
                    return InfrastructureFailure(peerRoutes.Message, request.DryRun, [peerRoutes]);
                if (!stateDigestMatches)
                    await WriteDesiredStateAsync(statePath, normalized, digest, resources, token);
                observationSpool.Activate(request.RuntimeId, request.Generation);
                await observationRegistry.ApplyAsync(normalized, token);
                await generationStore.WriteAsync(request.RuntimeId, request.Generation, token);
                return new TeamLabInfrastructureApplyResponse(
                    true, false, "Infrastructure live facts match desired state.", digest, true, resources, []);
            }
            if (stateDigestMatches)
                logger.LogWarning(
                    "TeamLab desired state digest matched but live infrastructure drifted: runtime={RuntimeId}, generation={Generation}, detail={Detail}",
                    request.RuntimeId, request.Generation, liveState.Output);
        }

        // Claim ownership before the first mutating command. The commands below create bridges,
        // namespaces, veth pairs and dnsmasq processes; if one of them fails we return early and
        // never reach the success-path write at the end of this method. ResolveCleanupOwnership
        // decides what cleanup may delete from this marker, so registering it only on full success
        // makes a half-applied generation look like an unowned leftover — cleanup then skips every
        // deletion and still reports success, leaking resources no inventory can see. Writing it
        // first also guarantees the marker exists before any resource whose name is shared across
        // generations, which is what lets the fencing check below be trusted.
        var executing = !_config.DryRun && !request.DryRun && _config.Enable;
        if (executing)
            await generationStore.WriteAsync(request.RuntimeId, request.Generation, token);

        var responses = new List<TeamLabDryRunResponse>();
        foreach (var item in normalized.Switches)
        {
            responses.Add(await bridgeService.ApplyAsync(
                new TeamLabBridgeRequest(request.RuntimeId, item.BridgeName, item.Cidr, request.DryRun), token));
        }
        var router = await routerService.ApplyAsync(new TeamLabRouterRequest(
            request.RuntimeId,
            normalized.RouterNamespace,
            normalized.Switches.Select(item => new TeamLabRouterInterfaceRequest(
                item.BridgeName, $"{item.GatewayIp}/{PrefixLength(item.Cidr)}")).ToArray(),
            [],
            request.DryRun), token);
        responses.Add(router);
        if (!router.Success) return InfrastructureFailure(router.Message, request.DryRun, responses);

        var inputPolicy = await firewallService.ApplyRouterInputPoliciesAsync(
            request.RuntimeId,
            request.Generation,
            normalized.RouterNamespace,
            normalized.Switches.Select((_, index) =>
                TrimInterfaceName($"{normalized.RouterNamespace}n{index}")).ToArray(),
            request.DryRun,
            token);
        responses.Add(inputPolicy);
        if (!inputPolicy.Success)
            return InfrastructureFailure(inputPolicy.Message, request.DryRun, responses);

        foreach (var (item, index) in normalized.Switches.Select((value, index) => (value, index)))
        {
            responses.Add(await bridgeService.ApplyDhcpDnsAsync(new TeamLabDhcpDnsRequest(
                request.RuntimeId,
                item.DhcpDnsServiceName,
                normalized.RouterNamespace,
                item.BridgeName,
                TrimInterfaceName($"{normalized.RouterNamespace}n{index}"),
                item.GatewayIp,
                item.Cidr,
                $"teamlab{request.RuntimeId}.local",
                item.Records,
                item.DnsRecords ?? [],
                request.DryRun,
                request.Generation), token));
        }
        var fabric = await fabricService.ApplyAsync(fabricRequest, token);
        responses.Add(fabric);
        var failed = responses.FirstOrDefault(item => !item.Success);
        if (failed is not null)
            return InfrastructureFailure(failed.Message, request.DryRun, responses);

        var effectiveDryRun = responses.Any(item => item.DryRun);
        if (!effectiveDryRun)
        {
            await WriteDesiredStateAsync(statePath, normalized, digest, resources, token);
            observationSpool.Activate(request.RuntimeId, request.Generation);
            await observationRegistry.ApplyAsync(normalized, token);
            await generationStore.WriteAsync(request.RuntimeId, request.Generation, token);
        }
        return new TeamLabInfrastructureApplyResponse(
            true,
            effectiveDryRun,
            effectiveDryRun ? "Infrastructure command plan returned without execution." : "Infrastructure applied.",
            digest,
            false,
            resources,
            responses.SelectMany(item => item.Commands).ToArray());
    }

    internal static string ResolveDesiredStateDirectory(int runtimeId, int generation) =>
        $"/run/gzctf-teamlab/runtime-{runtimeId}/generation-{generation}";

    internal static string RuntimeLockKey(int runtimeId) => $"teamlab-runtime:{runtimeId}";

    /// <summary>
    /// Decides whether a cleanup request may remove resources whose names are shared across
    /// generations of the same runtime (bridges, router namespace, veth pairs, dnsmasq).
    /// </summary>
    /// <param name="activeGeneration">
    /// Generation recorded on the node, or <c>null</c> when no marker exists.
    /// </param>
    internal static TeamLabCleanupOwnership ResolveCleanupOwnership(
        int? activeGeneration,
        int requestGeneration,
        bool desiredStateExists,
        bool dryRun)
    {
        if (activeGeneration is null)
            // Desired state without a marker means the marker was lost rather than never written,
            // so fail closed instead of guessing.
            return desiredStateExists && !dryRun
                ? TeamLabCleanupOwnership.Refuse
                // No marker and no desired state: ownership is unproven. Shared names are reused
                // across generations of a runtime, so removing them here could destroy resources a
                // concurrent generation is using. Apply now claims the marker before its first
                // mutating command, which is what makes a half-applied generation provably ours
                // rather than something this branch has to infer.
                : TeamLabCleanupOwnership.SharedResourcesNotOwned;

        return activeGeneration == requestGeneration
            ? TeamLabCleanupOwnership.OwnsSharedResources
            // The marker names another generation. This is the fencing token: a late cleanup must
            // never delete resources a newer generation is now using under the same names.
            : TeamLabCleanupOwnership.SharedResourcesNotOwned;
    }

    internal static string ResolveDesiredStatePath(int runtimeId, int generation) =>
        $"{ResolveDesiredStateDirectory(runtimeId, generation)}/state.json";

    public async Task<TeamLabInfrastructureStateResponse> GetInfrastructureStateAsync(
        int runtimeId,
        int generation,
        CancellationToken token)
    {
        if (runtimeId <= 0 || generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeId));
        var path = ResolveDesiredStatePath(runtimeId, generation);
        if (!File.Exists(path))
            return new TeamLabInfrastructureStateResponse(
                false, runtimeId, generation, 0, null, [], null);
        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<TeamLabDesiredStateFile>(
                stream, cancellationToken: token);
            return state is null
                ? new TeamLabInfrastructureStateResponse(
                    false, runtimeId, generation, 0, null, [], null)
                : new TeamLabInfrastructureStateResponse(
                    true,
                    state.RuntimeId,
                    state.Generation,
                    state.RouteVersion,
                    state.DesiredStateDigest,
                    state.Resources,
                    state.AppliedAt);
        }
        catch (JsonException)
        {
            return new TeamLabInfrastructureStateResponse(
                false, runtimeId, generation, 0, null, [], null);
        }
    }

    public async Task<IReadOnlyList<RuntimeInventoryResource>> GetManagedRuntimeInventoryAsync(
        CancellationToken token)
    {
        const string root = "/run/gzctf-teamlab";
        List<RuntimeInventoryResource> resources = [];
        if (Directory.Exists(root))
        {
            foreach (var path in Directory.EnumerateFiles(root, "state.json", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = File.OpenRead(path);
                    var state = await JsonSerializer.DeserializeAsync<TeamLabDesiredStateFile>(
                        stream, cancellationToken: token);
                    if (state is null) continue;
                    resources.AddRange(state.Resources.Select(item => new RuntimeInventoryResource(
                        item.NativeIdentity,
                        item.Key,
                        state.Generation,
                        item.Status,
                        null,
                        item.Kind,
                        state.RuntimeId,
                        state.DesiredStateDigest)));
                }
                catch (Exception exception) when (
                    exception is IOException or JsonException or UnauthorizedAccessException)
                {
                    logger.LogWarning(exception, "Failed to read TeamLab runtime inventory file {Path}.", path);
                }
            }
        }
        resources.AddRange(endpointSensors.SnapshotInventory());
        resources.AddRange(await pcapService.SnapshotInventoryAsync(token));
        resources.AddRange(await bootstrapService.SnapshotInventoryAsync(token));
        return resources;
    }

    private static TeamLabInfrastructureApplyRequest NormalizeInfrastructureRequest(
        TeamLabInfrastructureApplyRequest request) =>
        request with
        {
            Switches = request.Switches.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item with
                {
                    Records = item.Records.OrderBy(record => record.Hostname, StringComparer.Ordinal)
                        .ThenBy(record => record.IpAddress, StringComparer.Ordinal)
                        .ToArray(),
                    DnsRecords = (item.DnsRecords ?? item.Records.Select(record =>
                            new TeamLabDnsRecordRequest(record.Hostname, record.IpAddress)).ToArray())
                        .OrderBy(record => record.Hostname, StringComparer.Ordinal)
                        .ThenBy(record => record.IpAddress, StringComparer.Ordinal)
                        .ToArray()
                }).ToArray(),
            Routers = request.Routers.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item with
                {
                    NetworkKeys = item.NetworkKeys.Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal).ToArray()
                }).ToArray(),
            Fabric = request.Fabric with
            {
                LocalRoutes = NormalizeRoutes(request.Fabric.LocalRoutes),
                RemoteRoutes = NormalizeRoutes(request.Fabric.RemoteRoutes)
            },
            ForwardPolicies = request.ForwardPolicies
                .OrderBy(item => item.SourceCidr, StringComparer.Ordinal)
                .ThenBy(item => item.DestinationCidr, StringComparer.Ordinal)
                .ThenByDescending(item => item.Allow)
                .ToArray(),
            ObservationPoints = request.ObservationPoints.OrderBy(item => item.PublicId).ToArray(),
            DryRun = false
        };

    private static TeamLabFabricApplyRequest BuildFabricRequest(
        TeamLabInfrastructureApplyRequest request,
        bool dryRun) => new(
        request.RuntimeId,
        request.Generation,
        request.RouteVersion,
        request.Fabric.FabricIp,
        request.RouterNamespace,
        request.Fabric.HubAddressCidr,
        request.Fabric.NodeAddressCidr,
        request.Fabric.HostInterfaceName,
        request.Fabric.NamespaceInterfaceName,
        request.Fabric.LocalRoutes,
        request.Fabric.RemoteRoutes,
        request.ForwardPolicies,
        dryRun);

    private static string ComputeDesiredStateDigest(TeamLabInfrastructureApplyRequest request)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(payload))}";
    }

    private static TeamLabInfrastructureResourceFact[] BuildInfrastructureFacts(
        TeamLabInfrastructureApplyRequest request) =>
        request.Switches.Select(item => new TeamLabInfrastructureResourceFact(
                "managed-switch", item.Key, item.BridgeName, "ready"))
            .Concat(request.Routers.Select(item => new TeamLabInfrastructureResourceFact(
                "managed-router-fragment", item.Key, request.RouterNamespace, "ready")))
            .Concat(request.Switches.Select(item => new TeamLabInfrastructureResourceFact(
                "dhcp-dns", item.Key, item.DhcpDnsServiceName, "ready")))
            .Append(new TeamLabInfrastructureResourceFact(
                "fabric-uplink", "fabric", request.Fabric.HostInterfaceName, "ready"))
            .Concat(request.ObservationPoints.Select(item => new TeamLabInfrastructureResourceFact(
                "observation-point", item.PublicId.ToString("D"), ResolveObservationNativeIdentity(request, item), "ready")))
            .ToArray();

    private static string ResolveObservationNativeIdentity(
        TeamLabInfrastructureApplyRequest request,
        TeamLabObservationPointIntent point) => point.Kind switch
    {
        0 => request.Switches.Single(item => item.Key == point.TopologyKey).BridgeName,
        1 => request.RouterNamespace,
        2 => request.Fabric.HostInterfaceName,
        _ => point.InterfaceToken
    };

    private static async Task<bool> DesiredStateMatchesAsync(
        string path,
        string digest,
        CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        try
        {
            await using var stream = File.OpenRead(path);
            var state = await JsonSerializer.DeserializeAsync<TeamLabDesiredStateFile>(stream,
                cancellationToken: token);
            return string.Equals(state?.DesiredStateDigest, digest, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<(bool Success, string Output)> ProbeInfrastructureFactsAsync(
        TeamLabInfrastructureApplyRequest request,
        CancellationToken token) =>
        await runner.RunAsync(BuildInfrastructureFactProbeCommand(
            request, Math.Clamp(_config.FabricMtu - 40, 536, 8960)), token);

    internal static string BuildInfrastructureFactProbeCommand(TeamLabInfrastructureApplyRequest request) =>
        BuildInfrastructureFactProbeCommand(request, 1380);

    private static string BuildInfrastructureFactProbeCommand(
        TeamLabInfrastructureApplyRequest request,
        int fabricMss)
    {
        var namespaceName = request.RouterNamespace;
        var commands = new List<string>
        {
            "set -e",
            "command -v ss >/dev/null 2>&1",
            $"ip netns exec {namespaceName} true"
        };
        foreach (var (item, index) in request.Switches.Select((value, index) => (value, index)))
        {
            var hostInterface = TrimInterfaceName($"{namespaceName}h{index}");
            var namespaceInterface = TrimInterfaceName($"{namespaceName}n{index}");
            var pidFile =
                $"{ResolveDesiredStateDirectory(request.RuntimeId, request.Generation)}/dns/{item.DhcpDnsServiceName}/dnsmasq.pid";
            commands.Add($"ip link show {item.BridgeName} >/dev/null");
            commands.Add($"ip link show {hostInterface} >/dev/null");
            commands.Add($"ip -o link show {hostInterface} | grep -F 'master {item.BridgeName}' >/dev/null");
            commands.Add($"ip netns exec {namespaceName} ip link show {namespaceInterface} >/dev/null");
            commands.Add(
                $"ip netns exec {namespaceName} ip -o -4 addr show dev {namespaceInterface} | grep -F '{item.GatewayIp}/{PrefixLength(item.Cidr)}' >/dev/null");
            commands.Add($"test -s {ShellQuote(pidFile)}");
            commands.Add($"dnsmasq_pid=$(cat {ShellQuote(pidFile)})");
            commands.Add("kill -0 \"$dnsmasq_pid\" 2>/dev/null");
            commands.Add(
                $"tr '\\0' ' ' < /proc/$dnsmasq_pid/cmdline | grep -F -- '--interface={namespaceInterface}' >/dev/null");
            commands.Add($"dnsmasq_sockets=$(ip netns exec {namespaceName} ss -H -lunp)");
            commands.Add(
                "printf '%s\n' \"$dnsmasq_sockets\" | grep -F \"pid=$dnsmasq_pid,\" | grep -E '(:|])53[[:space:]]' >/dev/null");
            commands.Add(
                "printf '%s\n' \"$dnsmasq_sockets\" | grep -F \"pid=$dnsmasq_pid,\" | grep -E '(:|])67[[:space:]]' >/dev/null");
        }

        commands.Add($"ip link show {request.Fabric.HostInterfaceName} >/dev/null");
        commands.Add(
            $"ip -o -4 addr show dev {request.Fabric.HostInterfaceName} | grep -F '{request.Fabric.HubAddressCidr}' >/dev/null");
        commands.Add(
            $"ip netns exec {namespaceName} ip link show {request.Fabric.NamespaceInterfaceName} >/dev/null");
        commands.Add(
            $"ip netns exec {namespaceName} ip -o -4 addr show dev {request.Fabric.NamespaceInterfaceName} | grep -F '{request.Fabric.NodeAddressCidr}' >/dev/null");
        foreach (var route in request.Fabric.LocalRoutes)
            commands.Add(
                $"ip route show exact {route.TargetCidr} | grep -F 'via {route.GatewayIp}' >/dev/null");
        var namespaceGateway = AddressFromCidr(request.Fabric.HubAddressCidr);
        foreach (var route in request.Fabric.RemoteRoutes)
        {
            commands.Add(
                $"ip route show exact {route.TargetCidr} | grep -F 'via {route.GatewayIp}' >/dev/null");
            commands.Add(
                $"ip netns exec {namespaceName} ip route show exact {route.TargetCidr} | grep -F 'via {namespaceGateway}' | grep -F 'dev {request.Fabric.NamespaceInterfaceName}' >/dev/null");
        }

        var runtimeChain = $"TLR{request.RuntimeId:X}G{request.Generation:X}";
        var accessChain = $"TLA{request.RuntimeId:X}G{request.Generation:X}";
        var inputChain = $"TLI{request.RuntimeId:X}G{request.Generation:X}";
        var mssChain = $"TLM{request.RuntimeId:X}G{request.Generation:X}";
        var fabricChain = $"TLF{request.RuntimeId:X}G{request.Generation:X}";
        var nftChecks = new List<string>
        {
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {inputChain} | grep -F 'hook input' >/dev/null",
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {inputChain} | grep -F 'policy drop' >/dev/null",
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {inputChain} | grep -F 'iifname \"lo\" accept' >/dev/null",
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {inputChain} | grep -F 'ct state established,related accept' >/dev/null",
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {accessChain} >/dev/null",
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {runtimeChain} | grep -F 'hook forward' >/dev/null",
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {runtimeChain} | grep -F 'policy drop' >/dev/null",
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {runtimeChain} | grep -F 'ct state established,related accept' >/dev/null",
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {runtimeChain} | grep -F 'jump {accessChain}' >/dev/null",
            $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {mssChain} | grep -F 'tcp option maxseg size set {fabricMss}' >/dev/null",
            $"nft list chain inet gzctf_teamlab {fabricChain} | grep -F 'hook forward' >/dev/null"
        };
        var iptablesChecks = new List<string>
        {
            $"ip netns exec {namespaceName} iptables -C INPUT -j {inputChain}",
            $"ip netns exec {namespaceName} iptables -C {inputChain} -i lo -j ACCEPT",
            $"ip netns exec {namespaceName} iptables -C {inputChain} -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT",
            $"ip netns exec {namespaceName} iptables -C {inputChain} -j REJECT",
            $"ip netns exec {namespaceName} iptables -S {accessChain} >/dev/null",
            $"ip netns exec {namespaceName} iptables -C FORWARD -j {runtimeChain}",
            $"ip netns exec {namespaceName} iptables -C {runtimeChain} -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT",
            $"ip netns exec {namespaceName} iptables -C {runtimeChain} -j {accessChain}",
            $"ip netns exec {namespaceName} iptables -C {runtimeChain} -j REJECT",
            $"ip netns exec {namespaceName} iptables -t mangle -C FORWARD -j {mssChain}",
            $"ip netns exec {namespaceName} iptables -t mangle -C {mssChain} -o {request.Fabric.NamespaceInterfaceName} -p tcp --tcp-flags SYN,RST SYN -j TCPMSS --set-mss {fabricMss}",
            $"iptables -C FORWARD -j {fabricChain}"
        };
        var playerInterface = TrimInterfaceName($"tlwg{request.RuntimeId}");
        foreach (var index in Enumerable.Range(0, request.Switches.Length))
        {
            var routerInterface = TrimInterfaceName($"{namespaceName}n{index}");
            nftChecks.Add($"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {inputChain} | grep -F 'iifname \"{routerInterface}\" udp dport 53 accept' >/dev/null");
            nftChecks.Add($"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {inputChain} | grep -F 'iifname \"{routerInterface}\" udp dport 67 accept' >/dev/null");
            iptablesChecks.Add($"ip netns exec {namespaceName} iptables -C {inputChain} -i {routerInterface} -p udp --dport 53 -j ACCEPT");
            iptablesChecks.Add($"ip netns exec {namespaceName} iptables -C {inputChain} -i {routerInterface} -p udp --dport 67 -j ACCEPT");
        }
        nftChecks.Add($"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {inputChain} | grep -F 'iifname \"{playerInterface}\" udp dport 53 accept' >/dev/null");
        iptablesChecks.Add($"ip netns exec {namespaceName} iptables -C {inputChain} -i {playerInterface} -p udp --dport 53 -j ACCEPT");
        foreach (var policy in request.ForwardPolicies)
        {
            var action = policy.Allow ? "accept" : "reject";
            nftChecks.Add(
                $"ip netns exec {namespaceName} nft list chain inet gzctf_teamlab {runtimeChain} | grep -F 'ip saddr {policy.SourceCidr} ip daddr {policy.DestinationCidr} {action}' >/dev/null");
            iptablesChecks.Add(
                $"ip netns exec {namespaceName} iptables -C {runtimeChain} -s {policy.SourceCidr} -d {policy.DestinationCidr} -j {(policy.Allow ? "ACCEPT" : "REJECT")}");
        }
        foreach (var cidr in request.Fabric.LocalRoutes.Concat(request.Fabric.RemoteRoutes)
                     .Select(item => item.TargetCidr)
                     .Distinct(StringComparer.Ordinal))
        {
            nftChecks.Add(
                $"nft list chain inet gzctf_teamlab {fabricChain} | grep -F 'iifname \"{request.Fabric.HostInterfaceName}\"' | grep -F 'ip daddr {cidr}' | grep -F 'accept' >/dev/null");
            nftChecks.Add(
                $"nft list chain inet gzctf_teamlab {fabricChain} | grep -F 'oifname \"{request.Fabric.HostInterfaceName}\"' | grep -F 'ip saddr {cidr}' | grep -F 'accept' >/dev/null");
            iptablesChecks.Add(
                $"iptables -C {fabricChain} -i {request.Fabric.HostInterfaceName} -d {cidr} -j ACCEPT");
            iptablesChecks.Add(
                $"iptables -C {fabricChain} -o {request.Fabric.HostInterfaceName} -s {cidr} -j ACCEPT");
        }
        iptablesChecks.Add($"iptables -C {fabricChain} -j RETURN");
        commands.Add(
            $"if command -v nft >/dev/null 2>&1; then {string.Join(" && ", nftChecks)}; " +
            $"else {string.Join(" && ", iptablesChecks)}; fi");
        return string.Join("; ", commands);
    }

    private static async Task WriteDesiredStateAsync(
        string path,
        TeamLabInfrastructureApplyRequest request,
        string digest,
        TeamLabInfrastructureResourceFact[] resources,
        CancellationToken token)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream,
                    new TeamLabDesiredStateFile(
                        request.RuntimeId,
                        request.Generation,
                        request.RouteVersion,
                        digest,
                        resources,
                        DateTimeOffset.UtcNow),
                    cancellationToken: token);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static int PrefixLength(string cidr) => int.Parse(cidr[(cidr.IndexOf('/') + 1)..]);

    private static bool IsStrictIpv4(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(part =>
            part.Length > 0 && int.TryParse(part, out var octet) && octet is >= 0 and <= 255);
    }

    private static TeamLabStaticRouteRequest[] NormalizeRoutes(IEnumerable<TeamLabStaticRouteRequest> routes) =>
        routes.GroupBy(route => route.TargetCidr, StringComparer.Ordinal)
            .Select(group => group.OrderBy(route => route.GatewayIp, StringComparer.Ordinal).First())
            .OrderBy(route => route.TargetCidr, StringComparer.Ordinal)
            .ToArray();

    private static string AddressFromCidr(string cidr)
    {
        var index = cidr.IndexOf('/');
        return index > 0 ? cidr[..index] : cidr;
    }

    private static bool HasCommand(string command)
    {
        var paths = new[]
        {
            "/sbin",
            "/usr/sbin",
            "/bin",
            "/usr/bin",
            "/usr/local/bin"
        };

        return paths.Any(path => File.Exists(Path.Combine(path, command)));
    }

    private static bool HasCpuVirtualizationFlag()
    {
        try
        {
            return File.ReadLines("/proc/cpuinfo")
                .Any(line => line.StartsWith("flags", StringComparison.OrdinalIgnoreCase)
                             && (line.Contains(" vmx ", StringComparison.Ordinal)
                                 || line.EndsWith(" vmx", StringComparison.Ordinal)
                                 || line.Contains(" svm ", StringComparison.Ordinal)
                                 || line.EndsWith(" svm", StringComparison.Ordinal)));
        }
        catch
        {
            return false;
        }
    }

    public async Task<TeamLabDryRunResponse> ConfigureWireGuardAsync(TeamLabWireGuardRequest request, CancellationToken token)
    {
        if (request.RuntimeId <= 0) return Failure("Invalid RuntimeId.", request.DryRun);
        if (request.Generation <= 0) return Failure("Invalid Generation.", request.DryRun);
        var validation = ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateLinuxName(request.InterfaceName, nameof(request.InterfaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidatePort(request.ListenPort, nameof(request.ListenPort));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateCidr(request.AddressCidr, nameof(request.AddressCidr));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateCidr(request.PeerClientAddress, nameof(request.PeerClientAddress));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateAllowedIps(request.PeerAllowedIps, nameof(request.PeerAllowedIps));
        if (validation is not null) return Failure(validation, request.DryRun);

        foreach (var cidr in request.PlayerAllowedCidrs)
        {
            validation = ValidateCidr(cidr, nameof(request.PlayerAllowedCidrs));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        foreach (var cidr in request.PlayerBlockedCidrs)
        {
            validation = ValidateCidr(cidr, nameof(request.PlayerBlockedCidrs));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        validation = ValidateWireGuardKey(request.InterfacePrivateKey, nameof(request.InterfacePrivateKey));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateWireGuardKey(request.PeerPublicKey, nameof(request.PeerPublicKey));
        if (validation is not null) return Failure(validation, request.DryRun);

        await using var runtimeLock = await resourceLock.AcquireAsync(RuntimeLockKey(request.RuntimeId), token);
        if (!_config.DryRun && !request.DryRun && _config.Enable)
        {
            TeamLabActiveGeneration? activeGeneration;
            try
            {
                activeGeneration = await generationStore.ReadAsync(request.RuntimeId, token);
            }
            catch (InvalidDataException exception)
            {
                return Failure(exception.Message, request.DryRun);
            }
            if (activeGeneration?.Generation != request.Generation)
                return Failure(
                    $"WireGuard access generation {request.Generation} is not active for runtime {request.RuntimeId}.",
                    request.DryRun);
        }

        var commands = new[]
        {
            "printf '<redacted>' | wg set <interface> private-key /dev/stdin",
            $"if ip netns exec {request.NamespaceName} ip link show dev {request.InterfaceName} >/dev/null 2>&1; then " +
            $"ip netns exec {request.NamespaceName} wg set {request.InterfaceName} private-key /dev/stdin listen-port {request.ListenPort} peer {request.PeerPublicKey} allowed-ips {request.PeerClientAddress}; " +
            $"else ip link delete {request.InterfaceName} 2>/dev/null || true; ip link add {request.InterfaceName} type wireguard; " +
            $"wg set {request.InterfaceName} private-key /dev/stdin listen-port {request.ListenPort} peer {request.PeerPublicKey} allowed-ips {request.PeerClientAddress}; " +
            $"ip link set {request.InterfaceName} netns {request.NamespaceName}; fi",
            $"for existing_peer in $(ip netns exec {request.NamespaceName} wg show {request.InterfaceName} peers); do " +
            $"test \"$existing_peer\" = {ShellQuote(request.PeerPublicKey)} || ip netns exec {request.NamespaceName} wg set {request.InterfaceName} peer \"$existing_peer\" remove; done",
            TeamLabNetworkPrimitives.BuildNamespaceIpv4AddressConvergenceCommand(
                request.NamespaceName, request.InterfaceName, request.AddressCidr),
            $"ip netns exec {request.NamespaceName} ip link set {request.InterfaceName} up"
        };

        commands = commands.Concat(BuildPeerRouteCommands(request.NamespaceName, request.InterfaceName, request.PeerClientAddress))
            .Concat(BuildPlayerNatCommands(request.NamespaceName, request.InterfaceName,
                request.PeerClientAddress, request.PlayerAllowedCidrs))
            .Concat(BuildPlayerAccessCommands(request.RuntimeId, request.Generation, request.NamespaceName,
                request.InterfaceName, request.PeerClientAddress, request.PlayerAllowedCidrs,
                request.PlayerBlockedCidrs))
            .ToArray();

        return await ExecuteOrPlanAsync(commands, request.DryRun, token, request.InterfacePrivateKey);
    }

    public async Task<TeamLabDryRunResponse> CleanupWireGuardAsync(
        TeamLabWireGuardCleanupRequest request,
        CancellationToken token)
    {
        if (request.RuntimeId <= 0) return Failure("Invalid RuntimeId.", request.DryRun);
        if (request.Generation <= 0) return Failure("Invalid Generation.", request.DryRun);
        var validation = ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return Failure(validation, request.DryRun);
        validation = ValidateLinuxName(request.InterfaceName, nameof(request.InterfaceName));
        if (validation is not null) return Failure(validation, request.DryRun);
        await using var runtimeLock = await resourceLock.AcquireAsync(RuntimeLockKey(request.RuntimeId), token);
        if (!_config.DryRun && !request.DryRun && _config.Enable)
        {
            TeamLabActiveGeneration? activeGeneration;
            try
            {
                activeGeneration = await generationStore.ReadAsync(request.RuntimeId, token);
            }
            catch (InvalidDataException exception)
            {
                return Failure(exception.Message, request.DryRun);
            }
            if (activeGeneration?.Generation != request.Generation)
                return Failure(
                    $"WireGuard cleanup generation {request.Generation} is not active for runtime {request.RuntimeId}.",
                    request.DryRun);
        }
        var commands = BuildPlayerAccessCleanupCommands(request.RuntimeId, request.Generation,
                request.NamespaceName, request.InterfaceName)
            .Concat([
                $"ip netns exec {request.NamespaceName} ip link delete {request.InterfaceName} 2>/dev/null || true",
                $"ip link delete {request.InterfaceName} 2>/dev/null || true"
            ]).ToArray();
        return await ExecuteOrPlanAsync(commands, request.DryRun, token);
    }

    public async Task<TeamLabDryRunResponse> CleanupAsync(TeamLabCleanupRequest request, CancellationToken token)
    {
        if (request.RuntimeId <= 0)
            return Failure("Invalid RuntimeId.", request.DryRun);
        if (request.Generation <= 0)
            return Failure("Invalid Generation.", request.DryRun);
        var routerValidation = ValidateLinuxName(request.RouterNamespace, nameof(request.RouterNamespace));
        if (routerValidation is not null)
            return Failure(routerValidation, request.DryRun);

        foreach (var name in request.ResourceNames)
        {
            var validation = ValidateLinuxName(name, nameof(request.ResourceNames));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        await using var runtimeLock = await resourceLock.AcquireAsync(RuntimeLockKey(request.RuntimeId), token);
        TeamLabActiveGeneration? activeGeneration;
        try
        {
            activeGeneration = await generationStore.ReadAsync(request.RuntimeId, token);
        }
        catch (InvalidDataException exception)
        {
            return Failure(exception.Message, request.DryRun);
        }
        var generationDirectory = ResolveDesiredStateDirectory(request.RuntimeId, request.Generation);
        var runtimeDirectory = Path.GetDirectoryName(generationDirectory)!;
        var desiredStateExists = File.Exists(ResolveDesiredStatePath(request.RuntimeId, request.Generation));
        var ownership = ResolveCleanupOwnership(
            activeGeneration?.Generation, request.Generation, desiredStateExists, request.DryRun);
        if (ownership == TeamLabCleanupOwnership.Refuse)
            return Failure(
                $"Active generation state is unavailable for runtime {request.RuntimeId}; refusing shared resource cleanup.",
                request.DryRun);
        var ownsSharedResources = ownership == TeamLabCleanupOwnership.OwnsSharedResources;
        var commands = new List<string>();
        if (ownsSharedResources)
        {
            commands.Add(
                $"if test -d {ShellQuote(generationDirectory)}; then find {ShellQuote(generationDirectory)} -type f -name dnsmasq.pid -exec sh -c 'pid=$(cat \"$1\"); if kill -0 \"$pid\" 2>/dev/null; then kill \"$pid\"; fi' sh {{}} \\;; fi");
            commands.AddRange(request.ResourceNames
                .SelectMany(name => new[]
                {
                    $"if ip netns list | awk '{{print $1}}' | grep -Fx {ShellQuote(name)} >/dev/null; then pids=$(ip netns pids {ShellQuote(name)}); test -z \"$pids\" || kill $pids; ip netns delete {ShellQuote(name)}; fi",
                    $"if ip link show dev {ShellQuote(name)} >/dev/null 2>&1; then ip link delete {ShellQuote(name)}; fi"
                }));
        }
        commands.Add($"rm -rf {ShellQuote(generationDirectory)}");
        commands.Add($"if test -d {ShellQuote(runtimeDirectory)}; then rmdir {ShellQuote(runtimeDirectory)} 2>/dev/null || test -n \"$(find {ShellQuote(runtimeDirectory)} -mindepth 1 -maxdepth 1 -print -quit)\"; fi");

        var runtimePolicies = await firewallService.RemoveRuntimePoliciesAsync(
            request.RuntimeId,
            request.Generation,
            request.RouterNamespace,
            request.DryRun,
            token);
        var fabricPolicies = await firewallService.RemoveFabricPoliciesAsync(
            request.RuntimeId,
            request.Generation,
            request.DryRun,
            token);
        var resources = await ExecuteOrPlanAsync(commands.ToArray(), request.DryRun, token);
        var responses = new List<TeamLabDryRunResponse> { runtimePolicies, fabricPolicies, resources };
        if (ownsSharedResources)
        {
            responses.Add(await fabricService.RemovePeerRoutesAsync(
                request.RuntimeId,
                request.Generation,
                request.FabricRemoteCidrs,
                request.DryRun,
                token));
        }
        responses.Add(await firewallService.VerifyPoliciesRemovedAsync(
            request.RuntimeId,
            request.Generation,
            request.RouterNamespace,
            request.DryRun,
            token));
        var postconditions = new List<string>
        {
            $"test ! -e {ShellQuote(generationDirectory)}"
        };
        if (ownsSharedResources)
            postconditions.AddRange(request.ResourceNames.Select(name =>
                $"! ip link show dev {ShellQuote(name)} >/dev/null 2>&1 && ! ip netns list | awk '{{print $1}}' | grep -Fx {ShellQuote(name)} >/dev/null"));
        responses.Add(await ExecuteOrPlanAsync(postconditions.ToArray(), request.DryRun, token));
        var failed = responses.FirstOrDefault(item => !item.Success);
        if (failed is null && responses.All(item => !item.DryRun))
        {
            await pcapService.CleanupGenerationAsync(request.RuntimeId, request.Generation, token);
            await bootstrapService.CleanupGenerationAsync(request.RuntimeId, request.Generation, token);
            foreach (var assetKey in request.SensorAssetKeys.Distinct(StringComparer.Ordinal))
                endpointSensors.Remove(request.RuntimeId, request.Generation, assetKey);
            await observationRegistry.RemoveAsync(request.RuntimeId, request.Generation);
            observationSpool.Remove(request.RuntimeId, request.Generation);
            if (ownsSharedResources)
                await generationStore.ClearIfActiveAsync(request.RuntimeId, request.Generation, token);
        }
        return new TeamLabDryRunResponse(
            failed is null,
            responses.Any(item => item.DryRun),
            failed?.Message ?? (ownsSharedResources
                ? "Active TeamLab generation resources cleaned."
                : "Stale TeamLab generation-specific state cleaned; shared runtime resources were preserved."),
            responses.SelectMany(item => item.Commands).ToArray());
    }

    public async Task<TeamLabDryRunResponse> ProbeAsync(TeamLabProbeRequest request, CancellationToken token)
    {
        if (request.RuntimeId <= 0) return Failure("Invalid RuntimeId.", request.DryRun);
        var validation = ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateIp(request.TargetIp, nameof(request.TargetIp));
        if (validation is not null) return Failure(validation, request.DryRun);

        var kind = request.Kind is null ? "ping" : request.Kind.Trim().ToLowerInvariant();
        if (kind is not ("ping" or "tcp" or "http"))
            return Failure("Invalid probe Kind.", request.DryRun);
        if (kind is "tcp" or "http" &&
            (request.Port is not int port || port is < 1 or > ushort.MaxValue))
            return Failure("A valid Port is required for tcp and http probes.", request.DryRun);

        var command = kind switch
        {
            "tcp" =>
                $"command -v timeout >/dev/null 2>&1 && command -v bash >/dev/null 2>&1 && ip netns exec {request.NamespaceName} timeout 3 bash -c ': >/dev/tcp/{request.TargetIp}/{request.Port!.Value}'",
            "http" =>
                $"command -v curl >/dev/null 2>&1 && ip netns exec {request.NamespaceName} curl --fail --silent --show-error --connect-timeout 2 --max-time 5 --output /dev/null http://{request.TargetIp}:{request.Port!.Value}/",
            _ => $"ip netns exec {request.NamespaceName} ping -c 1 -W 2 {request.TargetIp}"
        };

        return await ExecuteOrPlanAsync([command], request.DryRun, token);
    }

    public async Task<TeamLabDryRunResponse> AttachContainerAsync(TeamLabContainerAttachRequest request,
        CancellationToken token)
    {
        var validation = ValidateLinuxName(request.BridgeName, nameof(request.BridgeName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateLinuxName(request.HostInterfaceName, nameof(request.HostInterfaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateLinuxName(request.ContainerInterfaceName, nameof(request.ContainerInterfaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateCidr(request.AddressCidr, nameof(request.AddressCidr));
        if (validation is not null) return Failure(validation, request.DryRun);

        if (string.IsNullOrWhiteSpace(request.ContainerId) || request.ContainerId.Any(char.IsWhiteSpace))
            return Failure("Invalid container id.", request.DryRun);

        if (!string.IsNullOrWhiteSpace(request.MacAddress) && !MacRegex().IsMatch(request.MacAddress))
            return Failure("Invalid MAC address.", request.DryRun);

        if (!string.IsNullOrWhiteSpace(request.GatewayIp))
        {
            validation = ValidateIp(request.GatewayIp, nameof(request.GatewayIp));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        foreach (var route in request.StaticRoutes)
        {
            validation = ValidateCidr(route, nameof(request.StaticRoutes));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        foreach (var dns in request.DnsServers)
        {
            validation = ValidateIp(dns, nameof(request.DnsServers));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        var hostIf = ShellQuote(request.HostInterfaceName);
        var peerIf = ShellQuote(TrimInterfaceName($"p{request.HostInterfaceName}"));
        var bridge = ShellQuote(request.BridgeName);
        var containerIf = ShellQuote(request.ContainerInterfaceName);
        var address = ShellQuote(request.AddressCidr);
        var containerId = ShellQuote(request.ContainerId);
        var macCommand = string.IsNullOrWhiteSpace(request.MacAddress)
            ? string.Empty
            : $"nsenter -t $pid -n ip link set dev {containerIf} address {ShellQuote(request.MacAddress)};";
        var routeCommand = request.RemoveDefaultRoute
            ? $"nsenter -t $pid -n ip route del default 2>/dev/null || true;"
            : string.Empty;
        var gateway = string.IsNullOrWhiteSpace(request.GatewayIp) ? null : ShellQuote(request.GatewayIp);
        var staticRouteCommands = gateway is null
            ? string.Empty
            : string.Join(' ', request.StaticRoutes.Select(route =>
                $"nsenter -t $pid -n ip route replace {ShellQuote(route)} via {gateway} dev {containerIf};"));
        var command = string.Join(' ',
        [
            "set -e;",
            "command -v docker >/dev/null 2>&1 || { echo 'missing docker command'; exit 127; };",
            "command -v ip >/dev/null 2>&1 || { echo 'missing ip command'; exit 127; };",
            "command -v nsenter >/dev/null 2>&1 || { echo 'missing nsenter command'; exit 127; };",
            $"pid=$(docker inspect -f '{{{{.State.Pid}}}}' {containerId});",
            "test \"$pid\" -gt 0;",
            $"ip link del {hostIf} 2>/dev/null || true;",
            $"nsenter -t $pid -n ip link del {containerIf} 2>/dev/null || true;",
            $"ip link add {hostIf} type veth peer name {peerIf};",
            $"ip link set {hostIf} master {bridge};",
            $"ip link set {hostIf} up;",
            $"ip link set {peerIf} netns $pid;",
            $"nsenter -t $pid -n ip link set {peerIf} name {containerIf};",
            $"nsenter -t $pid -n ip addr flush dev {containerIf};",
            macCommand,
            $"nsenter -t $pid -n ip addr add {address} dev {containerIf};",
            $"nsenter -t $pid -n ip link set {containerIf} up;",
            routeCommand,
            staticRouteCommands,
            $"nsenter -t $pid -n ip route show"
        ]);

        return await ExecuteOrPlanAsync([command], request.DryRun, token);
    }

    private async Task<TeamLabDryRunResponse> ExecuteOrPlanAsync(string[] commands, bool requestDryRun,
        CancellationToken token, string? wireGuardPrivateKey = null)
    {
        if (!_config.Enable)
            return new TeamLabDryRunResponse(true, true,
                "TeamLab network mutation is disabled on this WorkerNode. Command plan returned without execution.", commands);

        if (_config.DryRun || requestDryRun)
            return new TeamLabDryRunResponse(true, true,
                "TeamLab command plan returned without execution.", commands);

        foreach (var command in commands)
        {
            if (command.Contains("<redacted>", StringComparison.Ordinal))
                continue;

            var standardInput = command.Contains("private-key /dev/stdin", StringComparison.Ordinal)
                ? wireGuardPrivateKey
                : null;
            var result = await runner.RunAsync(command, standardInput, token);
            if (!result.Success)
                return new TeamLabDryRunResponse(false, false, result.Output, commands);
        }

        logger.LogInformation("Executed {Count} TeamLab network commands.", commands.Length);
        return new TeamLabDryRunResponse(true, false, "Commands executed.", commands);
    }

    private TeamLabDryRunResponse Failure(string message, bool requestDryRun) =>
        new(false, _config.DryRun || requestDryRun || !_config.Enable, message, []);

    private TeamLabInfrastructureApplyResponse InfrastructureFailure(
        string message,
        bool requestDryRun,
        IEnumerable<TeamLabDryRunResponse>? responses = null)
    {
        var items = responses?.ToArray() ?? [];
        return new TeamLabInfrastructureApplyResponse(
            false,
            _config.DryRun || requestDryRun || !_config.Enable || items.Any(item => item.DryRun),
            message,
            null,
            false,
            [],
            items.SelectMany(item => item.Commands).ToArray());
    }

    private static string? ValidateInfrastructureRequest(TeamLabInfrastructureApplyRequest request)
    {
        if (request.RuntimeId <= 0) return "Invalid RuntimeId.";
        if (request.Generation <= 0) return "Invalid Generation.";
        if (request.RouteVersion <= 0) return "Invalid RouteVersion.";
        var validation = ValidateLinuxName(request.RouterNamespace, nameof(request.RouterNamespace));
        if (validation is not null) return validation;
        if (request.Switches.Length == 0) return "At least one managed switch is required.";
        if (request.Switches.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != request.Switches.Length)
            return "Managed switch keys must be unique.";
        if (request.Switches.Select(item => item.BridgeName).Distinct(StringComparer.Ordinal).Count() != request.Switches.Length)
            return "Managed switch bridge names must be unique.";

        var switchKeys = request.Switches.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in request.Switches)
        {
            validation = ValidateResourceToken(item.Key, nameof(item.Key));
            if (validation is not null) return validation;
            validation = ValidateLinuxName(item.BridgeName, nameof(item.BridgeName));
            if (validation is not null) return validation;
            validation = ValidateLinuxName(item.DhcpDnsServiceName, nameof(item.DhcpDnsServiceName));
            if (validation is not null) return validation;
            validation = ValidateCidr(item.Cidr, nameof(item.Cidr));
            if (validation is not null) return validation;
            validation = ValidateIp(item.GatewayIp, nameof(item.GatewayIp));
            if (validation is not null) return validation;
            if (!CidrContains(item.Cidr, item.GatewayIp))
                return $"GatewayIp for managed switch '{item.Key}' is outside its CIDR.";
            if (item.Records.Select(record => record.MacAddress).Distinct(StringComparer.OrdinalIgnoreCase).Count() != item.Records.Length ||
                item.Records.Select(record => record.IpAddress).Distinct(StringComparer.Ordinal).Count() != item.Records.Length)
                return $"Managed switch '{item.Key}' has duplicate DHCP records.";
            foreach (var record in item.Records)
            {
                if (!MacRegex().IsMatch(record.MacAddress)) return "Invalid DHCP lease MAC address.";
                validation = ValidateIp(record.IpAddress, nameof(record.IpAddress));
                if (validation is not null) return validation;
                if (!CidrContains(item.Cidr, record.IpAddress))
                    return $"DHCP record for '{record.Hostname}' is outside managed switch '{item.Key}'.";
                validation = ValidateHostname(record.Hostname, nameof(record.Hostname));
                if (validation is not null) return validation;
            }
            foreach (var record in item.DnsRecords ?? [])
            {
                validation = ValidateIp(record.IpAddress, nameof(record.IpAddress));
                if (validation is not null) return validation;
                validation = ValidateHostname(record.Hostname, nameof(record.Hostname));
                if (validation is not null) return validation;
            }
        }

        if (request.Routers.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != request.Routers.Length)
            return "Managed router fragment keys must be unique.";
        foreach (var item in request.Routers)
        {
            validation = ValidateResourceToken(item.Key, nameof(item.Key));
            if (validation is not null) return validation;
            if (item.NetworkKeys.Length == 0) return $"Managed router fragment '{item.Key}' has no network.";
            if (item.NetworkKeys.Distinct(StringComparer.Ordinal).Count() != item.NetworkKeys.Length ||
                item.NetworkKeys.Any(key => !switchKeys.Contains(key)))
                return $"Managed router fragment '{item.Key}' references an invalid network.";
        }

        validation = ValidateIp(request.Fabric.FabricIp, nameof(request.Fabric.FabricIp));
        if (validation is not null) return validation;
        validation = ValidateCidr(request.Fabric.HubAddressCidr, nameof(request.Fabric.HubAddressCidr));
        if (validation is not null) return validation;
        validation = ValidateCidr(request.Fabric.NodeAddressCidr, nameof(request.Fabric.NodeAddressCidr));
        if (validation is not null) return validation;
        validation = ValidateLinuxName(request.Fabric.HostInterfaceName, nameof(request.Fabric.HostInterfaceName));
        if (validation is not null) return validation;
        validation = ValidateLinuxName(request.Fabric.NamespaceInterfaceName, nameof(request.Fabric.NamespaceInterfaceName));
        if (validation is not null) return validation;
        if (PrefixLength(request.Fabric.HubAddressCidr) != 30 || PrefixLength(request.Fabric.NodeAddressCidr) != 30 ||
            !SameCidrNetwork(request.Fabric.HubAddressCidr, request.Fabric.NodeAddressCidr) ||
            string.Equals(AddressFromCidr(request.Fabric.HubAddressCidr), AddressFromCidr(request.Fabric.NodeAddressCidr), StringComparison.Ordinal))
            return "Fabric link addresses must be distinct members of the same IPv4 /30 lease.";
        foreach (var route in request.Fabric.LocalRoutes.Concat(request.Fabric.RemoteRoutes))
        {
            validation = ValidateCidr(route.TargetCidr, nameof(route.TargetCidr));
            if (validation is not null) return validation;
            validation = ValidateIp(route.GatewayIp, nameof(route.GatewayIp));
            if (validation is not null) return validation;
            if (!string.IsNullOrWhiteSpace(route.SourceIp))
            {
                validation = ValidateIp(route.SourceIp, nameof(route.SourceIp));
                if (validation is not null) return validation;
            }
        }
        foreach (var policy in request.ForwardPolicies)
        {
            validation = ValidateCidr(policy.SourceCidr, nameof(policy.SourceCidr));
            if (validation is not null) return validation;
            validation = ValidateCidr(policy.DestinationCidr, nameof(policy.DestinationCidr));
            if (validation is not null) return validation;
        }
        if (request.ForwardPolicies
                .GroupBy(item => (item.SourceCidr, item.DestinationCidr))
                .Any(group => group.Count() > 1))
            return "Forward policies must contain exactly one decision per directed network pair.";
        if (request.ObservationPoints.Select(item => item.PublicId).Distinct().Count() != request.ObservationPoints.Length)
            return "Observation point ids must be unique.";
        foreach (var point in request.ObservationPoints)
        {
            if (point.PublicId == Guid.Empty) return "Observation point id is required.";
            validation = ValidateResourceToken(point.TopologyKey, nameof(point.TopologyKey));
            if (validation is not null) return validation;
            validation = ValidateResourceToken(point.InterfaceToken, nameof(point.InterfaceToken));
            if (validation is not null) return validation;
            if (point.Kind > 3) return "Invalid observation point kind.";
            if (point.Kind == 0 && !switchKeys.Contains(point.TopologyKey))
                return $"Network observation point '{point.PublicId}' references an invalid managed switch.";
            if (point.Kind == 1 && !request.Routers.Any(item => item.Key == point.TopologyKey))
                return $"Router observation point '{point.PublicId}' references an invalid router fragment.";
        }
        return null;
    }

    private static bool SameCidrNetwork(string left, string right)
    {
        var leftParts = left.Split('/');
        var rightParts = right.Split('/');
        if (leftParts.Length != 2 || rightParts.Length != 2 || leftParts[1] != rightParts[1]) return false;
        var prefix = int.Parse(leftParts[1]);
        return NetworkValue(leftParts[0], prefix) == NetworkValue(rightParts[0], prefix);
    }

    private static bool CidrContains(string cidr, string address)
    {
        var parts = cidr.Split('/');
        return parts.Length == 2 && int.TryParse(parts[1], out var prefix) &&
               NetworkValue(parts[0], prefix) == NetworkValue(address, prefix);
    }

    private static uint NetworkValue(string address, int prefix)
    {
        var bytes = IPAddress.Parse(address).GetAddressBytes();
        var value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return value & mask;
    }

    private static string? ValidateLinuxName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 15 || !LinuxNameRegex().IsMatch(value))
            return $"Invalid {field}.";

        return null;
    }

    private static string? ValidateCidr(string value, string field)
    {
        var parts = value.Split('/');
        if (parts.Length != 2 || !IsStrictIpv4(parts[0]) ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 1 or > 32)
            return $"Invalid {field}.";

        return null;
    }

    private static string? ValidateIp(string value, string field) =>
        IsStrictIpv4(value) ? null : $"Invalid {field}.";

    private static string? ValidateHostname(string value, string field) =>
        string.IsNullOrWhiteSpace(value) || !HostnameRegex().IsMatch(value)
            ? $"Invalid {field}."
            : null;

    private static string? ValidateResourceToken(string value, string field) =>
        string.IsNullOrWhiteSpace(value) || !ResourceTokenRegex().IsMatch(value) || value.Length > 64
            ? $"Invalid {field}."
            : null;

    private static string? ValidatePort(int value, string field) =>
        value is > 0 and <= ushort.MaxValue ? null : $"Invalid {field}.";

    private static string? ValidateAllowedIps(string value, string field)
    {
        foreach (var cidr in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var validation = ValidateCidr(cidr, field);
            if (validation is not null) return validation;
        }

        return string.IsNullOrWhiteSpace(value) ? $"Invalid {field}." : null;
    }

    // Single source of truth: a second truncation here would silently diverge from the names the
    // router and observation registry derive for the same interfaces.
    private static string TrimInterfaceName(string value) =>
        TeamLabNetworkPrimitives.TrimInterfaceName(value);

    private static string[] BuildPeerRouteCommands(string namespaceName, string interfaceName, string peerAllowedIps) =>
        peerAllowedIps.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(cidr => $"ip netns exec {namespaceName} ip route replace {cidr} dev {interfaceName}")
            .ToArray();

    private static string[] BuildPlayerNatCommands(string namespaceName, string interfaceName,
        string peerClientAddress, IEnumerable<string> allowedCidrs)
    {
        var chain = PlayerNatChain(interfaceName);
        var cidrs = allowedCidrs
            .Where(cidr => !string.IsNullOrWhiteSpace(cidr))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (HasCommand("nft"))
        {
            return new[]
            {
                $"ip netns exec {namespaceName} nft add table ip gzctf_teamlab_nat 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft {ShellQuote("add chain ip gzctf_teamlab_nat postrouting { type nat hook postrouting priority srcnat; policy accept; }")} 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft add chain ip gzctf_teamlab_nat {chain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft flush chain ip gzctf_teamlab_nat postrouting",
                $"ip netns exec {namespaceName} nft flush chain ip gzctf_teamlab_nat {chain}",
                $"ip netns exec {namespaceName} nft add rule ip gzctf_teamlab_nat postrouting jump {chain}"
            }
            .Concat(cidrs
                .Select(cidr =>
                    $"ip netns exec {namespaceName} nft add rule ip gzctf_teamlab_nat {chain} ip saddr {peerClientAddress} ip daddr {cidr} masquerade"))
            .ToArray();
        }

        return new[]
            {
                $"ip netns exec {namespaceName} iptables -t nat -N {chain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} iptables -t nat -F {chain}",
                $"ip netns exec {namespaceName} iptables -t nat -C POSTROUTING -j {chain} 2>/dev/null || ip netns exec {namespaceName} iptables -t nat -A POSTROUTING -j {chain}"
            }
            .Concat(cidrs.Select(cidr =>
                $"ip netns exec {namespaceName} iptables -t nat -A {chain} -s {peerClientAddress} -d {cidr} -j MASQUERADE"))
            .ToArray();
    }

    private static string PlayerNatChain(string interfaceName) => $"TLN{interfaceName}";

    private static string[] BuildPlayerAccessCommands(int runtimeId, int generation, string namespaceName,
        string interfaceName, string peerClientAddress, IEnumerable<string> allowedCidrs,
        IEnumerable<string> blockedCidrs)
    {
        var chain = TeamLabFirewallService.AccessChainName(runtimeId, generation);
        var allowed = allowedCidrs
                     .Where(cidr => !string.IsNullOrWhiteSpace(cidr))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal)
                     .ToArray();
        var blocked = blockedCidrs
                     .Where(cidr => !string.IsNullOrWhiteSpace(cidr))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal)
                     .ToArray();
        if (HasCommand("nft"))
        {
            var commands = new List<string>
            {
                $"ip netns exec {namespaceName} nft flush chain inet gzctf_teamlab {chain}"
            };
            commands.AddRange(blocked.Select(cidr =>
                $"ip netns exec {namespaceName} nft add rule inet gzctf_teamlab {chain} iifname {ShellQuote(interfaceName)} ip saddr {peerClientAddress} ip daddr {cidr} reject"));
            commands.AddRange(allowed.Select(cidr =>
                $"ip netns exec {namespaceName} nft add rule inet gzctf_teamlab {chain} iifname {ShellQuote(interfaceName)} ip saddr {peerClientAddress} ip daddr {cidr} accept"));
            commands.Add($"ip netns exec {namespaceName} nft add rule inet gzctf_teamlab {chain} return");
            return commands.ToArray();
        }

        var iptables = new List<string>
        {
            $"ip netns exec {namespaceName} iptables -F {chain}"
        };
        iptables.AddRange(blocked.Select(cidr =>
            $"ip netns exec {namespaceName} iptables -A {chain} -i {interfaceName} -s {peerClientAddress} -d {cidr} -j REJECT"));
        iptables.AddRange(allowed.Select(cidr =>
            $"ip netns exec {namespaceName} iptables -A {chain} -i {interfaceName} -s {peerClientAddress} -d {cidr} -j ACCEPT"));
        iptables.Add($"ip netns exec {namespaceName} iptables -A {chain} -j RETURN");
        return iptables.ToArray();
    }

    private static string[] BuildPlayerAccessCleanupCommands(int runtimeId, int generation,
        string namespaceName, string interfaceName)
    {
        var accessChain = TeamLabFirewallService.AccessChainName(runtimeId, generation);
        if (HasCommand("nft"))
            return [
                $"ip netns exec {namespaceName} nft flush chain inet gzctf_teamlab {accessChain} 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft add rule inet gzctf_teamlab {accessChain} return 2>/dev/null || true",
                $"ip netns exec {namespaceName} nft delete table ip gzctf_teamlab_nat 2>/dev/null || true"
            ];

        var natChain = PlayerNatChain(interfaceName);
        return [
            $"ip netns exec {namespaceName} iptables -F {accessChain} 2>/dev/null || true",
            $"ip netns exec {namespaceName} iptables -A {accessChain} -j RETURN 2>/dev/null || true",
            $"ip netns exec {namespaceName} iptables -t nat -D POSTROUTING -j {natChain} 2>/dev/null || true",
            $"ip netns exec {namespaceName} iptables -t nat -F {natChain} 2>/dev/null || true",
            $"ip netns exec {namespaceName} iptables -t nat -X {natChain} 2>/dev/null || true"
        ];
    }

    [GeneratedRegex("^[a-zA-Z0-9_.-]+$")]
    private static partial Regex LinuxNameRegex();

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9.-]{0,62}$")]
    private static partial Regex HostnameRegex();

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,63}$")]
    private static partial Regex ResourceTokenRegex();

    [GeneratedRegex("^([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$")]
    private static partial Regex MacRegex();

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    private static string? ValidateWireGuardKey(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return $"Invalid WireGuard {field}.";

        try
        {
            return Convert.FromBase64String(value).Length == 32
                ? null
                : $"Invalid WireGuard {field}.";
        }
        catch (FormatException)
        {
            return $"Invalid WireGuard {field}.";
        }
    }

    private sealed record TeamLabDesiredStateFile(
        int RuntimeId,
        int Generation,
        int RouteVersion,
        string DesiredStateDigest,
        TeamLabInfrastructureResourceFact[] Resources,
        DateTimeOffset AppliedAt);
}
