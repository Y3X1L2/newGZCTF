using System.Net;
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
        var hasIp = File.Exists("/sbin/ip") || File.Exists("/usr/sbin/ip") || File.Exists("/bin/ip") || File.Exists("/usr/bin/ip");
        var hasWg = File.Exists("/sbin/wg") || File.Exists("/usr/sbin/wg") || File.Exists("/bin/wg") || File.Exists("/usr/bin/wg");
        var hasIptables = File.Exists("/sbin/iptables") || File.Exists("/usr/sbin/iptables") ||
                          File.Exists("/bin/iptables") || File.Exists("/usr/bin/iptables");
        var available = hasIp && hasWg && hasIptables;
        var message = available ? null : "ip, iptables, or WireGuard command is missing on this WorkerNode.";

        return Task.FromResult(new TeamLabStatusResponse(
            available,
            _config.Enable,
            _config.DryRun,
            hasIp,
            hasWg,
            hasIptables,
            DateTimeOffset.UtcNow,
            message));
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
