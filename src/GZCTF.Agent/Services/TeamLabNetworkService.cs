using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public partial class TeamLabNetworkService(
    IOptions<AgentTeamLabConfig> options,
    TeamLabCommandRunner runner,
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
        var available = hasIp && hasWg && (hasIptables || hasNft);
        var missing = new List<string>();
        if (!hasIp) missing.Add("iproute2/ip");
        if (!hasWg) missing.Add("wireguard-tools/wg");
        if (!hasIptables && !hasNft) missing.Add("iptables or nftables");
        var message = available ? null : $"Missing TeamLab network dependency: {string.Join(", ", missing)}.";
        var agentVersion = typeof(TeamLabNetworkService).Assembly.GetName().Version?.ToString() ?? "unknown";
        var capabilities = new TeamLabToolCapabilityReport(
            hasDocker,
            hasKvm,
            hasKvmDevice,
            hasCpuVirtualization,
            hasWg,
            hasIptables,
            hasNft,
            hasTcpdump,
            hasDumpcap);

        return Task.FromResult(new TeamLabStatusResponse(
            available,
            _config.Enable,
            _config.DryRun,
            agentVersion,
            ProtocolVersion: 3,
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
            message));
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

    public async Task<TeamLabDryRunResponse> CreateBridgeAsync(TeamLabBridgeRequest request, CancellationToken token)
    {
        var validation = ValidateLinuxName(request.BridgeName, nameof(request.BridgeName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateCidr(request.Cidr, nameof(request.Cidr));
        if (validation is not null) return Failure(validation, request.DryRun);

        var commands = new[]
        {
            $"ip link delete {request.BridgeName} 2>/dev/null || true",
            $"ip link add {request.BridgeName} type bridge",
            $"ip link set {request.BridgeName} up"
        };

        return await ExecuteOrPlanAsync(commands.ToArray(), request.DryRun, token);
    }

    public async Task<TeamLabDryRunResponse> CreateRouterAsync(TeamLabRouterRequest request, CancellationToken token)
    {
        var validation = ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        if (request.Interfaces.Length == 0)
            return Failure("At least one router interface is required.", request.DryRun);

        foreach (var iface in request.Interfaces)
        {
            validation = ValidateLinuxName(iface.BridgeName, nameof(request.Interfaces));
            if (validation is not null) return Failure(validation, request.DryRun);

            validation = ValidateCidr(iface.GatewayAddressCidr, nameof(iface.GatewayAddressCidr));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        foreach (var route in request.Routes)
        {
            validation = ValidateCidr(route.TargetCidr, nameof(route.TargetCidr));
            if (validation is not null) return Failure(validation, request.DryRun);

            validation = ValidateIp(route.GatewayIp, nameof(route.GatewayIp));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        var commands = new List<string>
        {
            $"ip netns pids {request.NamespaceName} 2>/dev/null | xargs -r kill 2>/dev/null || true",
            $"ip netns delete {request.NamespaceName} 2>/dev/null || true",
            $"ip netns add {request.NamespaceName}",
            $"ip netns exec {request.NamespaceName} ip link set lo up"
        };
        for (var i = 0; i < request.Interfaces.Length; i++)
        {
            var iface = request.Interfaces[i];
            var hostIf = TrimInterfaceName($"{request.NamespaceName}h{i}");
            var nsIf = TrimInterfaceName($"{request.NamespaceName}n{i}");
            commands.Add($"ip link delete {hostIf} 2>/dev/null || true");
            commands.Add($"ip link add {hostIf} type veth peer name {nsIf}");
            commands.Add($"ip link set {hostIf} master {iface.BridgeName}");
            commands.Add($"ip link set {hostIf} up");
            commands.Add($"ip link set {nsIf} netns {request.NamespaceName}");
            commands.Add($"ip netns exec {request.NamespaceName} ip addr flush dev {nsIf}");
            commands.Add($"ip netns exec {request.NamespaceName} ip addr add {iface.GatewayAddressCidr} dev {nsIf}");
            commands.Add($"ip netns exec {request.NamespaceName} ip link set {nsIf} up");
        }

        commands.Add($"ip netns exec {request.NamespaceName} sysctl -w net.ipv4.ip_forward=1");
        commands.AddRange(request.Routes.Select(route =>
            $"ip netns exec {request.NamespaceName} ip route replace {route.TargetCidr} via {route.GatewayIp}"));
        return await ExecuteOrPlanAsync(commands.ToArray(), request.DryRun, token);
    }

    public async Task<TeamLabDryRunResponse> ConfigureWireGuardAsync(TeamLabWireGuardRequest request, CancellationToken token)
    {
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

        var commands = new[]
        {
            "printf '<redacted>' | wg set <interface> private-key /dev/stdin",
            $"ip netns exec {request.NamespaceName} ip link delete {request.InterfaceName} 2>/dev/null || true",
            $"ip link delete {request.InterfaceName} 2>/dev/null || true",
            $"ip link add {request.InterfaceName} type wireguard",
            $"ip link set {request.InterfaceName} netns {request.NamespaceName}",
            $"ip netns exec {request.NamespaceName} ip addr flush dev {request.InterfaceName}",
            $"ip netns exec {request.NamespaceName} ip addr add {request.AddressCidr} dev {request.InterfaceName}",
            $"ip netns exec {request.NamespaceName} wg set {request.InterfaceName} private-key /dev/stdin listen-port {request.ListenPort} peer {request.PeerPublicKey} allowed-ips {request.PeerClientAddress}",
            $"ip netns exec {request.NamespaceName} ip link set {request.InterfaceName} up"
        };

        commands = commands.Concat(BuildPeerRouteCommands(request.NamespaceName, request.InterfaceName, request.PeerClientAddress))
            .Concat(BuildPlayerForwardAclCommands(request.NamespaceName, request.InterfaceName,
                request.PlayerAllowedCidrs, request.PlayerBlockedCidrs))
            .ToArray();

        return await ExecuteOrPlanAsync(commands, request.DryRun, token, request.InterfacePrivateKey);
    }

    public async Task<TeamLabDryRunResponse> CleanupAsync(TeamLabCleanupRequest request, CancellationToken token)
    {
        foreach (var name in request.ResourceNames)
        {
            var validation = ValidateLinuxName(name, nameof(request.ResourceNames));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        var commands = request.ResourceNames
            .SelectMany(name => new[]
            {
                $"test ! -f /run/gzctf-teamlab/{name}/dnsmasq.pid || kill $(cat /run/gzctf-teamlab/{name}/dnsmasq.pid) 2>/dev/null || true",
                $"rm -rf /run/gzctf-teamlab/{name} 2>/dev/null || true",
                $"ip netns pids {name} 2>/dev/null | xargs -r kill 2>/dev/null || true",
                BuildDeleteRuntimeForwardRulesCommand(request.RuntimeId),
                $"ip link delete {name} 2>/dev/null || true",
                $"ip netns delete {name} 2>/dev/null || true"
            })
            .ToArray();

        return await ExecuteOrPlanAsync(commands, request.DryRun, token);
    }

    public async Task<TeamLabDryRunResponse> ProbeAsync(TeamLabProbeRequest request, CancellationToken token)
    {
        var validation = ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateIp(request.TargetIp, nameof(request.TargetIp));
        if (validation is not null) return Failure(validation, request.DryRun);

        var commands = new[]
        {
            $"ip netns exec {request.NamespaceName} ping -c 1 -W 2 {request.TargetIp}"
        };

        return await ExecuteOrPlanAsync(commands, request.DryRun, token);
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

    public async Task<TeamLabDryRunResponse> ConfigureDhcpDnsAsync(TeamLabDhcpDnsRequest request,
        CancellationToken token)
    {
        var validation = ValidateLinuxName(request.ServiceName, nameof(request.ServiceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateLinuxName(request.BridgeName, nameof(request.BridgeName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateLinuxName(request.InterfaceName, nameof(request.InterfaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateIp(request.GatewayIp, nameof(request.GatewayIp));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateCidr(request.Cidr, nameof(request.Cidr));
        if (validation is not null) return Failure(validation, request.DryRun);

        if (string.IsNullOrWhiteSpace(request.Domain) || request.Domain.Any(char.IsWhiteSpace))
            return Failure("Invalid Domain.", request.DryRun);

        foreach (var lease in request.Leases)
        {
            if (!MacRegex().IsMatch(lease.MacAddress))
                return Failure("Invalid DHCP lease MAC address.", request.DryRun);

            validation = ValidateIp(lease.IpAddress, nameof(request.Leases));
            if (validation is not null) return Failure(validation, request.DryRun);

            validation = ValidateHostname(lease.Hostname, nameof(request.Leases));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        foreach (var record in request.DnsRecords)
        {
            validation = ValidateHostname(record.Hostname, nameof(request.DnsRecords));
            if (validation is not null) return Failure(validation, request.DryRun);

            validation = ValidateIp(record.IpAddress, nameof(request.DnsRecords));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        var directory = $"/run/gzctf-teamlab/{request.ServiceName}";
        var hostsFile = $"{directory}/hosts";
        var leasesFile = $"{directory}/dhcp-hosts";
        var dhcpLeaseFile = $"{directory}/leases";
        var pidFile = $"{directory}/dnsmasq.pid";
        var netmask = NetmaskFromCidr(request.Cidr);
        if (string.IsNullOrWhiteSpace(netmask))
            return Failure("Invalid Cidr.", request.DryRun);

        var hostsContent = string.Join("\\n", request.DnsRecords
            .Select(record => $"{record.IpAddress} {record.Hostname}.{request.Domain} {record.Hostname}"));
        var leasesContent = string.Join("\\n", request.Leases
            .Select(lease => $"{lease.MacAddress.ToLowerInvariant()},{lease.IpAddress},{lease.Hostname}"));
        var addressOptions = string.Join(' ', request.DnsRecords.Select(record =>
            $"--address=/{record.Hostname}.{request.Domain}/{record.IpAddress}"));

        var commands = new[]
        {
            $"mkdir -p {ShellQuote(directory)}",
            $"printf {ShellQuote(hostsContent + "\\n")} > {ShellQuote(hostsFile)}",
            $"printf {ShellQuote(leasesContent + "\\n")} > {ShellQuote(leasesFile)}",
            $"test ! -f {ShellQuote(pidFile)} || kill $(cat {ShellQuote(pidFile)}) 2>/dev/null || true",
            $"rm -f {ShellQuote(dhcpLeaseFile)}",
            $"ip netns exec {request.NamespaceName} dnsmasq --keep-in-foreground --user=root --group=root --bind-interfaces --except-interface=lo --interface={request.InterfaceName} --listen-address={request.GatewayIp} --dhcp-authoritative --dhcp-range={request.GatewayIp},static,{netmask} --dhcp-hostsfile={leasesFile} --dhcp-leasefile={dhcpLeaseFile} --addn-hosts={hostsFile} {addressOptions} --domain={request.Domain} --pid-file={pidFile} --log-facility=- >/dev/null 2>&1 &"
        };

        return await ExecuteOrPlanAsync(commands, request.DryRun, token);
    }

    public async Task<TeamLabDryRunResponse> ProbeDhcpDnsAsync(TeamLabDhcpDnsProbeRequest request,
        CancellationToken token)
    {
        var validation = ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateIp(request.GatewayIp, nameof(request.GatewayIp));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateHostname(request.Hostname, nameof(request.Hostname));
        if (validation is not null) return Failure(validation, request.DryRun);

        var commands = new[]
        {
            $"for i in $(seq 1 10); do ip netns exec {request.NamespaceName} nslookup {request.Hostname} {request.GatewayIp} >/dev/null 2>&1 && exit 0; sleep 1; done; ip netns exec {request.NamespaceName} nslookup {request.Hostname} {request.GatewayIp}"
        };

        return await ExecuteOrPlanAsync(commands, request.DryRun, token);
    }

    public async Task<TeamLabDryRunResponse> ApplyFabricAsync(TeamLabFabricApplyRequest request,
        CancellationToken token)
    {
        if (request.RuntimeId <= 0)
            return Failure("Invalid RuntimeId.", request.DryRun);

        var validation = ValidateIp(request.FabricIp, nameof(request.FabricIp));
        if (validation is not null) return Failure(validation, request.DryRun);

        var localRoutes = request.LocalRoutes ?? [];
        var routes = request.Routes ?? [];
        var forwardPolicies = request.ForwardPolicies ?? [];
        foreach (var route in localRoutes.Concat(routes))
        {
            validation = ValidateCidr(route.TargetCidr, nameof(route.TargetCidr));
            if (validation is not null) return Failure(validation, request.DryRun);

            validation = ValidateIp(route.GatewayIp, nameof(route.GatewayIp));
            if (validation is not null) return Failure(validation, request.DryRun);

            if (!string.IsNullOrWhiteSpace(route.SourceIp))
            {
                validation = ValidateIp(route.SourceIp, nameof(route.SourceIp));
                if (validation is not null) return Failure(validation, request.DryRun);
            }
        }
        foreach (var policy in forwardPolicies)
        {
            validation = ValidateCidr(policy.SourceCidr, nameof(policy.SourceCidr));
            if (validation is not null) return Failure(validation, request.DryRun);
            validation = ValidateCidr(policy.DestinationCidr, nameof(policy.DestinationCidr));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        var hasNamespaceUplink = !string.IsNullOrWhiteSpace(request.NamespaceName);
        if (hasNamespaceUplink)
        {
            validation = ValidateLinuxName(request.NamespaceName!, nameof(request.NamespaceName));
            if (validation is not null) return Failure(validation, request.DryRun);

            validation = ValidateCidr(request.NamespaceHostAddressCidr, nameof(request.NamespaceHostAddressCidr));
            if (validation is not null) return Failure(validation, request.DryRun);

            validation = ValidateCidr(request.NamespacePeerAddressCidr, nameof(request.NamespacePeerAddressCidr));
            if (validation is not null) return Failure(validation, request.DryRun);
        }
        else if (localRoutes.Length > 0 ||
                 !string.IsNullOrWhiteSpace(request.NamespaceHostAddressCidr) ||
                 !string.IsNullOrWhiteSpace(request.NamespacePeerAddressCidr))
        {
            return Failure("NamespaceName is required when namespace Fabric routes are provided.", request.DryRun);
        }

        var commands = new List<string>();
        if (hasNamespaceUplink)
        {
            var hostInterface = BuildFabricUplinkHostInterfaceName(request.RuntimeId);
            var namespaceInterface = BuildFabricUplinkNamespaceInterfaceName(request.RuntimeId);
            var namespaceName = request.NamespaceName!;
            var hostAddress = AddressFromCidr(request.NamespaceHostAddressCidr);

            commands.Add($"ip netns exec {namespaceName} ip link delete {namespaceInterface} 2>/dev/null || true");
            commands.Add($"ip link delete {hostInterface} 2>/dev/null || true");
            commands.Add($"ip link add {hostInterface} type veth peer name {namespaceInterface}");
            commands.Add($"ip addr flush dev {hostInterface}");
            commands.Add($"ip addr add {request.NamespaceHostAddressCidr} dev {hostInterface}");
            commands.Add($"ip link set {hostInterface} up");
            commands.Add($"ip link set {namespaceInterface} netns {namespaceName}");
            commands.Add($"ip netns exec {namespaceName} ip addr flush dev {namespaceInterface}");
            commands.Add($"ip netns exec {namespaceName} ip addr add {request.NamespacePeerAddressCidr} dev {namespaceInterface}");
            commands.Add($"ip netns exec {namespaceName} ip link set {namespaceInterface} up");
            commands.Add("sysctl -w net.ipv4.ip_forward=1");
            commands.Add($"ip netns exec {namespaceName} sysctl -w net.ipv4.ip_forward=1");
            commands.AddRange(BuildNamespaceForwardPolicyCommands(namespaceName, forwardPolicies));
            commands.AddRange(BuildFabricForwardAclCommands(request.RuntimeId, hostInterface, localRoutes, routes));
            commands.AddRange(NormalizeRoutes(localRoutes)
                .Select(route => $"ip route replace {route.TargetCidr} via {route.GatewayIp} dev {hostInterface}"));
            commands.AddRange(NormalizeRoutes(routes)
                .Select(route =>
                    $"ip netns exec {namespaceName} ip route replace {route.TargetCidr} via {hostAddress} dev {namespaceInterface}{BuildRouteSourceClause(route)}"));
        }

        commands.AddRange(NormalizeRoutes(routes)
            .Select(route => $"ip route replace {route.TargetCidr} via {route.GatewayIp}"));

        return await ExecuteOrPlanAsync(commands.ToArray(), request.DryRun, token);
    }

    private static TeamLabStaticRouteRequest[] NormalizeRoutes(IEnumerable<TeamLabStaticRouteRequest> routes) =>
        routes
            .GroupBy(route => route.TargetCidr, StringComparer.Ordinal)
            .Select(group => group.OrderBy(route => route.GatewayIp, StringComparer.Ordinal).First())
            .OrderBy(route => route.TargetCidr, StringComparer.Ordinal)
            .ToArray();

    private static string BuildRouteSourceClause(TeamLabStaticRouteRequest route) =>
        string.IsNullOrWhiteSpace(route.SourceIp) ? "" : $" src {route.SourceIp}";

    private static IEnumerable<string> BuildNamespaceForwardPolicyCommands(
        string namespaceName,
        IEnumerable<TeamLabForwardPolicyRequest> policies)
    {
        const string chain = "TEAMLAB-POLICY";
        yield return $"ip netns exec {namespaceName} iptables -N {chain} 2>/dev/null || true";
        yield return $"ip netns exec {namespaceName} iptables -F {chain}";
        yield return $"ip netns exec {namespaceName} iptables -C FORWARD -j {chain} 2>/dev/null || ip netns exec {namespaceName} iptables -I FORWARD 1 -j {chain}";
        yield return $"ip netns exec {namespaceName} iptables -A {chain} -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT";

        foreach (var policy in policies
                     .OrderBy(item => item.Allow ? 0 : 1)
                     .ThenBy(item => item.SourceCidr, StringComparer.Ordinal)
                     .ThenBy(item => item.DestinationCidr, StringComparer.Ordinal))
            yield return $"ip netns exec {namespaceName} iptables -A {chain} -s {policy.SourceCidr} -d {policy.DestinationCidr} -j {(policy.Allow ? "ACCEPT" : "REJECT")}";
    }

    private static IEnumerable<string> BuildFabricForwardAclCommands(int runtimeId, string hostInterface,
        TeamLabStaticRouteRequest[] localRoutes, TeamLabStaticRouteRequest[] remoteRoutes)
    {
        var comment = BuildFabricForwardRuleComment(runtimeId);
        yield return "iptables -N TEAMLAB-FABRIC 2>/dev/null || true";
        yield return "iptables -C FORWARD -j TEAMLAB-FABRIC 2>/dev/null || iptables -I FORWARD 1 -j TEAMLAB-FABRIC";
        yield return BuildDeleteRuntimeForwardRulesCommand(runtimeId);

        foreach (var route in NormalizeRoutes(remoteRoutes))
        {
            yield return $"iptables -A TEAMLAB-FABRIC -m comment --comment {comment} -i {hostInterface} -d {route.TargetCidr} -j ACCEPT";
            yield return $"iptables -A TEAMLAB-FABRIC -m comment --comment {comment} -o {hostInterface} -s {route.TargetCidr} -j ACCEPT";
        }

        foreach (var route in NormalizeRoutes(localRoutes))
        {
            yield return $"iptables -A TEAMLAB-FABRIC -m comment --comment {comment} -i {hostInterface} -d {route.TargetCidr} -j ACCEPT";
            yield return $"iptables -A TEAMLAB-FABRIC -m comment --comment {comment} -o {hostInterface} -s {route.TargetCidr} -j ACCEPT";
        }
    }

    private static string BuildDeleteRuntimeForwardRulesCommand(int runtimeId)
    {
        var comment = BuildFabricForwardRuleComment(runtimeId);
        return $"while iptables -S TEAMLAB-FABRIC 2>/dev/null | grep -F -- '--comment {comment}' >/dev/null; do rule=$(iptables -S TEAMLAB-FABRIC | grep -F -- '--comment {comment}' | head -n1 | sed 's/^-A TEAMLAB-FABRIC /-D TEAMLAB-FABRIC /'); iptables $rule || break; done";
    }

    private static string BuildFabricForwardRuleComment(int runtimeId) =>
        $"gzctf-teamlab-runtime-{runtimeId}";

    private static string BuildFabricUplinkHostInterfaceName(int runtimeId) => TrimInterfaceName($"tlrf{runtimeId}");

    private static string BuildFabricUplinkNamespaceInterfaceName(int runtimeId) =>
        TrimInterfaceName($"tlrf{runtimeId}n");

    private static string AddressFromCidr(string cidr)
    {
        var index = cidr.IndexOf('/');
        return index > 0 ? cidr[..index] : cidr;
    }

    public async Task<TeamLabCaptureResponse> StartCaptureAsync(TeamLabCaptureStartRequest request,
        CancellationToken token)
    {
        var validation = ValidateLinuxName(request.InterfaceName, nameof(request.InterfaceName));
        if (validation is not null) return CaptureFailure(validation, request.DryRun);

        if (request.RuntimeId <= 0 || request.JobId <= 0)
            return CaptureFailure("Invalid capture runtime or job id.", request.DryRun);

        if (request.MaxSeconds is <= 0 or > 86400)
            return CaptureFailure("Invalid capture max seconds.", request.DryRun);

        if (request.MaxBytes is <= 0 or > 10L * 1024 * 1024 * 1024)
            return CaptureFailure("Invalid capture max bytes.", request.DryRun);

        var directory = ResolveCaptureDirectory(request.RuntimeId, request.JobId);
        var filePath = ResolveCaptureFilePath(request.RuntimeId, request.JobId);
        var pidFile = $"{directory}/capture.pid";
        var sizeKb = Math.Max(1, (request.MaxBytes + 1024 - 1) / 1024);
        var sizeMb = Math.Max(1, (request.MaxBytes + 1024 * 1024 - 1) / (1024 * 1024));
        var captureProcess = HasCommand("dumpcap")
            ? $"dumpcap -q -i {ShellQuote(request.InterfaceName)} -a duration:{request.MaxSeconds} -a filesize:{sizeKb} -w {ShellQuote(filePath)} >/dev/null 2>&1"
            : $"timeout {request.MaxSeconds} tcpdump -i {ShellQuote(request.InterfaceName)} -s 0 -U -w {ShellQuote(filePath)} -C {sizeMb} -W 1 >/dev/null 2>&1";
        var captureCommand =
            $"({captureProcess}; rm -f {ShellQuote(pidFile)}) & echo $! > {ShellQuote(pidFile)}";
        var commands = new[]
        {
            $"mkdir -p {ShellQuote(directory)}",
            $"test ! -f {ShellQuote(pidFile)} || kill $(cat {ShellQuote(pidFile)}) 2>/dev/null || true",
            $"rm -f {ShellQuote(filePath)}",
            captureCommand
        };

        var result = await ExecuteOrPlanAsync(commands, request.DryRun, token);
        return new TeamLabCaptureResponse(result.Success, result.DryRun, result.Message, filePath, 0,
            result.Success && !result.DryRun,
            result.Commands);
    }

    public async Task<TeamLabCaptureResponse> StopCaptureAsync(TeamLabCaptureStopRequest request,
        CancellationToken token)
    {
        if (request.RuntimeId <= 0 || request.JobId <= 0)
            return CaptureFailure("Invalid capture runtime or job id.", request.DryRun);

        var directory = ResolveCaptureDirectory(request.RuntimeId, request.JobId);
        var filePath = ResolveCaptureFilePath(request.RuntimeId, request.JobId);
        var pidFile = $"{directory}/capture.pid";
        var commands = new[]
        {
            $"test ! -f {ShellQuote(pidFile)} || kill $(cat {ShellQuote(pidFile)}) 2>/dev/null || true",
            $"test ! -f {ShellQuote(pidFile)} || rm -f {ShellQuote(pidFile)}"
        };
        var result = await ExecuteOrPlanAsync(commands, request.DryRun, token);
        var capturedBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
        return new TeamLabCaptureResponse(result.Success, result.DryRun, result.Message, filePath, capturedBytes, false,
            result.Commands);
    }

    public Task<TeamLabCaptureResponse> GetCaptureStatusAsync(TeamLabCaptureStatusRequest request)
    {
        if (request.RuntimeId <= 0 || request.JobId <= 0)
            return Task.FromResult(CaptureFailure("Invalid capture runtime or job id.", request.DryRun));

        var directory = ResolveCaptureDirectory(request.RuntimeId, request.JobId);
        var filePath = ResolveCaptureFilePath(request.RuntimeId, request.JobId);
        var pidFile = $"{directory}/capture.pid";
        var running = IsCaptureProcessRunning(pidFile);
        if (!running && File.Exists(pidFile)) File.Delete(pidFile);
        var capturedBytes = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
        return Task.FromResult(new TeamLabCaptureResponse(true, _config.DryRun || request.DryRun || !_config.Enable,
            running ? "Capture is running." : "Capture is complete.", filePath, capturedBytes, running, []));
    }

    public static string ResolveCaptureFilePath(int runtimeId, int jobId)
    {
        return $"{ResolveCaptureDirectory(runtimeId, jobId)}/capture.pcap";
    }

    private static string ResolveCaptureDirectory(int runtimeId, int jobId)
    {
        if (runtimeId <= 0) throw new ArgumentOutOfRangeException(nameof(runtimeId));
        if (jobId <= 0) throw new ArgumentOutOfRangeException(nameof(jobId));
        return $"/run/gzctf-teamlab/capture-{runtimeId}-{jobId}";
    }

    public async Task<TeamLabFlowResponse> StartFlowMetadataAsync(TeamLabFlowStartRequest request,
        CancellationToken token)
    {
        var validation = ValidateLinuxName(request.InterfaceName, nameof(request.InterfaceName));
        if (validation is not null) return FlowFailure(validation, request.DryRun);

        validation = ValidateResourceToken(request.NetworkKey, nameof(request.NetworkKey));
        if (validation is not null) return FlowFailure(validation, request.DryRun);

        if (request.RuntimeId <= 0)
            return FlowFailure("Invalid flow metadata runtime id.", request.DryRun);

        var directory = ResolveFlowDirectory(request.RuntimeId, request.NetworkKey);
        var logFile = ResolveFlowLogPath(request.RuntimeId, request.NetworkKey);
        var pidFile = ResolveFlowPidPath(request.RuntimeId, request.NetworkKey);
        if (!_config.DryRun && !request.DryRun && _config.Enable && !HasCommand("tcpdump"))
            return FlowFailure("tcpdump command is missing on this WorkerNode.", request.DryRun);

        var captureCommand =
            $"nohup tcpdump -l -tttt -nn -q -i {ShellQuote(request.InterfaceName)} ip 2>>{ShellQuote($"{directory}/flow.err")} >>{ShellQuote(logFile)} & echo $! > {ShellQuote(pidFile)}";

        var commands = new[]
        {
            $"mkdir -p {ShellQuote(directory)}",
            $"test ! -f {ShellQuote(pidFile)} || kill $(cat {ShellQuote(pidFile)}) 2>/dev/null || true",
            $"touch {ShellQuote(logFile)}",
            captureCommand
        };
        var result = await ExecuteOrPlanAsync(commands, request.DryRun, token);
        return new TeamLabFlowResponse(result.Success, result.DryRun, result.Message, 0, [], result.Commands);
    }

    public async Task<TeamLabFlowResponse> StopFlowMetadataAsync(TeamLabFlowStopRequest request,
        CancellationToken token)
    {
        var validation = ValidateResourceToken(request.NetworkKey, nameof(request.NetworkKey));
        if (validation is not null) return FlowFailure(validation, request.DryRun);

        if (request.RuntimeId <= 0)
            return FlowFailure("Invalid flow metadata runtime id.", request.DryRun);

        var directory = ResolveFlowDirectory(request.RuntimeId, request.NetworkKey);
        var pidFile = ResolveFlowPidPath(request.RuntimeId, request.NetworkKey);
        var commands = new[]
        {
            $"test ! -f {ShellQuote(pidFile)} || kill $(cat {ShellQuote(pidFile)}) 2>/dev/null || true",
            $"rm -rf {ShellQuote(directory)} 2>/dev/null || true"
        };
        var result = await ExecuteOrPlanAsync(commands, request.DryRun, token);
        return new TeamLabFlowResponse(result.Success, result.DryRun, result.Message, 0, [], result.Commands);
    }

    public Task<TeamLabFlowResponse> GetFlowMetadataSnapshotAsync(TeamLabFlowSnapshotRequest request,
        CancellationToken token)
    {
        var validation = ValidateResourceToken(request.NetworkKey, nameof(request.NetworkKey));
        if (validation is not null)
            return Task.FromResult(FlowFailure(validation, request.DryRun));

        if (request.RuntimeId <= 0)
            return Task.FromResult(FlowFailure("Invalid flow metadata runtime id.", request.DryRun));

        var logFile = ResolveFlowLogPath(request.RuntimeId, request.NetworkKey);
        if (!File.Exists(logFile))
            return Task.FromResult(new TeamLabFlowResponse(true, _config.DryRun || request.DryRun || !_config.Enable,
                "Flow metadata log does not exist yet.", request.AfterCursor, [], []));

        var (nextCursor, samples) = ReadFlowSamples(logFile, request.AfterCursor, token);
        return Task.FromResult(new TeamLabFlowResponse(true, _config.DryRun || request.DryRun || !_config.Enable,
            $"Loaded {samples.Length} flow metadata sample(s).", nextCursor, samples, []));
    }

    public static string ResolveFlowLogPath(int runtimeId, string networkKey) =>
        $"{ResolveFlowDirectory(runtimeId, networkKey)}/flow.log";

    private static string ResolveFlowPidPath(int runtimeId, string networkKey) =>
        $"{ResolveFlowDirectory(runtimeId, networkKey)}/flow.pid";

    private static string ResolveFlowDirectory(int runtimeId, string networkKey)
    {
        if (runtimeId <= 0) throw new ArgumentOutOfRangeException(nameof(runtimeId));
        var validation = ValidateResourceToken(networkKey, nameof(networkKey));
        if (validation is not null) throw new ArgumentException(validation, nameof(networkKey));
        return $"/run/gzctf-teamlab/flow-{runtimeId}-{networkKey}";
    }

    private static (long NextCursor, TeamLabFlowSample[] Samples) ReadFlowSamples(
        string logFile,
        long afterCursor,
        CancellationToken token)
    {
        const int maxLines = 500;
        const int maxLineBytes = 64 * 1024;
        using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var start = afterCursor >= 0 && afterCursor <= stream.Length ? afterCursor : 0;
        stream.Seek(start, SeekOrigin.Begin);
        var cursor = start;
        var nextCursor = start;
        var buffer = new byte[8192];
        var line = new List<byte>(512);
        var samples = new List<TeamLabFlowSample>(maxLines);
        while (samples.Count < maxLines)
        {
            token.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            for (var index = 0; index < read && samples.Count < maxLines; index++)
            {
                cursor++;
                if (buffer[index] == (byte)'\n')
                {
                    var text = Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
                    if (TryParseTcpdumpFlowLine(text, out var sample))
                        samples.Add(sample with { Cursor = cursor });
                    line.Clear();
                    nextCursor = cursor;
                }
                else if (line.Count < maxLineBytes)
                {
                    line.Add(buffer[index]);
                }
            }
        }
        return (nextCursor, samples.ToArray());
    }

    public static bool TryParseTcpdumpFlowLine(string line, out TeamLabFlowSample sample)
    {
        sample = default!;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var normalized = line.Trim();
        var ipIndex = normalized.IndexOf(" IP ", StringComparison.Ordinal);
        if (ipIndex < 0)
            return false;

        var timestampText = normalized[..ipIndex].Trim();
        if (!DateTimeOffset.TryParse(timestampText, out var capturedAt))
            capturedAt = DateTimeOffset.UtcNow;

        var body = normalized[(ipIndex + 4)..];
        var marker = body.IndexOf(" > ", StringComparison.Ordinal);
        if (marker <= 0)
            return false;

        var source = body[..marker].Trim();
        var rest = body[(marker + 3)..];
        var colon = rest.IndexOf(':');
        if (colon <= 0)
            return false;

        var destination = rest[..colon].Trim();
        var payload = rest[(colon + 1)..];
        var protocol = ResolveTcpdumpProtocol(payload);
        var bytes = ResolveTcpdumpLength(payload);
        SplitEndpoint(source, out var sourceIp, out var sourcePort);
        SplitEndpoint(destination, out var destinationIp, out var destinationPort);

        if (!IsStrictIpv4(sourceIp) || !IsStrictIpv4(destinationIp))
            return false;

        sample = new TeamLabFlowSample(0, capturedAt.ToUniversalTime(), sourceIp, sourcePort, destinationIp,
            destinationPort, protocol, bytes);
        return true;
    }

    private static string ResolveTcpdumpProtocol(string payload)
    {
        var value = payload.TrimStart();
        if (value.StartsWith("tcp", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Flags", StringComparison.OrdinalIgnoreCase))
            return "TCP";
        if (value.StartsWith("UDP", StringComparison.OrdinalIgnoreCase))
            return "UDP";
        if (value.StartsWith("ICMP", StringComparison.OrdinalIgnoreCase))
            return "ICMP";
        return "IP";
    }

    private static long ResolveTcpdumpLength(string payload)
    {
        const string lengthMarker = " length ";
        var index = payload.LastIndexOf(lengthMarker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return 0;

        var start = index + lengthMarker.Length;
        var end = start;
        while (end < payload.Length && char.IsDigit(payload[end]))
            end++;

        return long.TryParse(payload[start..end], out var value) ? value : 0;
    }

    private static void SplitEndpoint(string endpoint, out string ip, out int? port)
    {
        ip = endpoint;
        port = null;
        var index = endpoint.LastIndexOf('.');
        if (index <= 0 || index >= endpoint.Length - 1)
            return;

        var candidatePort = endpoint[(index + 1)..];
        if (!int.TryParse(candidatePort, out var parsedPort) || parsedPort is <= 0 or > ushort.MaxValue)
            return;

        var candidateIp = endpoint[..index];
        if (!IsStrictIpv4(candidateIp))
            return;

        ip = candidateIp;
        port = parsedPort;
    }

    private static bool IsStrictIpv4(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 4 &&
               parts.All(part => part.Length > 0 &&
                                 int.TryParse(part, out var octet) &&
                                 octet is >= 0 and <= 255);
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

    private TeamLabCaptureResponse CaptureFailure(string message, bool requestDryRun) =>
        new(false, _config.DryRun || requestDryRun || !_config.Enable, message, null, 0, false, []);

    private TeamLabFlowResponse FlowFailure(string message, bool requestDryRun) =>
        new(false, _config.DryRun || requestDryRun || !_config.Enable, message, 0, [], []);

    private static bool IsCaptureProcessRunning(string pidFile)
    {
        if (!File.Exists(pidFile) ||
            !int.TryParse(File.ReadAllText(pidFile).Trim(), out var processId) ||
            processId <= 0)
            return false;
        return Directory.Exists($"/proc/{processId}");
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
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out _) ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 1 or > 32)
            return $"Invalid {field}.";

        return null;
    }

    private static string? ValidateIp(string value, string field) =>
        IPAddress.TryParse(value, out _) ? null : $"Invalid {field}.";

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

    private static string TrimInterfaceName(string value) => value.Length <= 15 ? value : value[..15];

    private static string[] BuildPeerRouteCommands(string namespaceName, string interfaceName, string peerAllowedIps) =>
        peerAllowedIps.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(cidr => $"ip netns exec {namespaceName} ip route replace {cidr} dev {interfaceName}")
            .ToArray();

    private static string[] BuildPlayerForwardAclCommands(string namespaceName, string interfaceName,
        IEnumerable<string> allowedCidrs, IEnumerable<string> blockedCidrs)
    {
        var commands = new List<string>();
        foreach (var cidr in allowedCidrs
                     .Where(cidr => !string.IsNullOrWhiteSpace(cidr))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
            commands.Add(
                $"ip netns exec {namespaceName} iptables -I FORWARD 1 -i {interfaceName} -d {cidr} -j ACCEPT");

        foreach (var cidr in blockedCidrs
                     .Where(cidr => !string.IsNullOrWhiteSpace(cidr))
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
            commands.Add($"ip netns exec {namespaceName} iptables -A FORWARD -i {interfaceName} -d {cidr} -j REJECT");

        return commands.ToArray();
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

    private static string? NetmaskFromCidr(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix) || prefix is < 1 or > 32)
            return null;

        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return new IPAddress([
            (byte)(mask >> 24),
            (byte)(mask >> 16),
            (byte)(mask >> 8),
            (byte)mask
        ]).ToString();
    }

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
}
