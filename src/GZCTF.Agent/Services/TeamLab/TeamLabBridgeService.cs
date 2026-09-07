using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;

namespace GZCTF.Agent.Services.TeamLab;

public sealed partial class TeamLabBridgeService(TeamLabCommandExecutor executor)
{
    internal string DesiredStateRoot { get; init; } = TeamLabNetworkService.DefaultDesiredStateRoot;
    public Task<TeamLabDryRunResponse> ApplyAsync(
        TeamLabBridgeRequest request,
        CancellationToken token)
    {
        var validation = TeamLabNetworkPrimitives.ValidateLinuxName(request.BridgeName, nameof(request.BridgeName));
        if (validation is not null) return Task.FromResult(Failure(validation, request.DryRun));
        validation = TeamLabNetworkPrimitives.ValidateCidr(request.Cidr, nameof(request.Cidr));
        if (validation is not null) return Task.FromResult(Failure(validation, request.DryRun));

        return executor.ExecuteAsync([
            $"ip link show {request.BridgeName} >/dev/null 2>&1 || ip link add {request.BridgeName} type bridge",
            $"ip link set {request.BridgeName} up"
        ], request.DryRun, token);
    }

    public Task<TeamLabDryRunResponse> ApplyDhcpDnsAsync(
        TeamLabDhcpDnsRequest request,
        CancellationToken token)
    {
        var validation = Validate(request);
        if (validation is not null) return Task.FromResult(Failure(validation, request.DryRun));

        var directory = request.Generation > 0
            ? $"{TeamLabNetworkService.ResolveDesiredStateDirectory(request.RuntimeId, request.Generation, DesiredStateRoot)}/dns/{request.ServiceName}"
            : $"{DesiredStateRoot}/{request.ServiceName}";
        var hostsFile = $"{directory}/hosts";
        var leasesFile = $"{directory}/dhcp-hosts";
        var dhcpLeaseFile = $"{directory}/leases";
        var pidFile = $"{directory}/dnsmasq.pid";
        var logFile = $"{directory}/dnsmasq.log";
        var netmask = TeamLabNetworkPrimitives.NetmaskFromCidr(request.Cidr)!;
        var hostsContent = string.Join("\\n", request.DnsRecords
            .Select(record => $"{record.IpAddress} {record.Hostname}.{request.Domain} {record.Hostname}"));
        var leasesContent = string.Join("\\n", request.Leases
            .Select(lease =>
                $"{lease.MacAddress.ToLowerInvariant()},set:{(lease.IsPrimary ? "primary" : "secondary")},{lease.IpAddress},{lease.Hostname}"));
        var addressOptions = string.Join(' ', request.DnsRecords.Select(record =>
            $"--address=/{record.Hostname}.{request.Domain}/{record.IpAddress}"));

        return executor.ExecuteAsync([
            $"mkdir -p {TeamLabNetworkPrimitives.ShellQuote(directory)}",
            $"printf {TeamLabNetworkPrimitives.ShellQuote(hostsContent + "\\n")} > {TeamLabNetworkPrimitives.ShellQuote(hostsFile)}",
            $"printf {TeamLabNetworkPrimitives.ShellQuote(leasesContent + "\\n")} > {TeamLabNetworkPrimitives.ShellQuote(leasesFile)}",
            $"test ! -f {TeamLabNetworkPrimitives.ShellQuote(pidFile)} || kill $(cat {TeamLabNetworkPrimitives.ShellQuote(pidFile)}) 2>/dev/null || true",
            $"rm -f {TeamLabNetworkPrimitives.ShellQuote(pidFile)} {TeamLabNetworkPrimitives.ShellQuote(dhcpLeaseFile)} {TeamLabNetworkPrimitives.ShellQuote(logFile)}",
            $"ip netns exec {request.NamespaceName} dnsmasq --keep-in-foreground --user=root --group=root --bind-interfaces --except-interface=lo --interface={request.InterfaceName} --listen-address={request.GatewayIp} --dhcp-authoritative --dhcp-range={request.GatewayIp},static,{netmask} --dhcp-hostsfile={leasesFile} --dhcp-option=tag:secondary,option:router --dhcp-leasefile={dhcpLeaseFile} --addn-hosts={hostsFile} {addressOptions} --domain={request.Domain} --local=/{request.Domain}/ --pid-file={pidFile} --log-facility=- >{TeamLabNetworkPrimitives.ShellQuote(logFile)} 2>&1 &",
            BuildDnsmasqReadinessCommand(request.NamespaceName, pidFile, logFile)
        ], request.DryRun, token);
    }

    internal static string BuildDnsmasqReadinessCommand(
        string namespaceName,
        string pidFile,
        string logFile) =>
        $"command -v ss >/dev/null 2>&1 || {{ echo 'missing ss command' >&2; exit 127; }}; " +
        "attempt=0; ready=0; " +
        $"while test \"$attempt\" -lt 150; do attempt=$((attempt + 1)); if test -s {TeamLabNetworkPrimitives.ShellQuote(pidFile)}; then " +
        $"pid=$(cat {TeamLabNetworkPrimitives.ShellQuote(pidFile)}); if kill -0 \"$pid\" 2>/dev/null; then " +
        $"sockets=$(ip netns exec {namespaceName} ss -H -lunp 2>/dev/null || true); " +
        "dns_port_ready=$(printf '%s\n' \"$sockets\" | grep -F 'users:((\"dnsmasq\",' | grep -E '(:|])53[[:space:]]' || true); " +
        "dhcp_port_ready=$(printf '%s\n' \"$sockets\" | grep -F 'users:((\"dnsmasq\",' | grep -E '(:|])67[[:space:]]' || true); " +
        "if test -n \"$dns_port_ready\" && test -n \"$dhcp_port_ready\"; then ready=1; break; fi; fi; fi; sleep 0.1; done; " +
        $"if test \"$ready\" -ne 1; then echo 'dnsmasq failed readiness checks for namespace {namespaceName}' >&2; " +
        $"test ! -f {TeamLabNetworkPrimitives.ShellQuote(logFile)} || cat {TeamLabNetworkPrimitives.ShellQuote(logFile)} >&2; exit 1; fi";

    private static string? Validate(TeamLabDhcpDnsRequest request)
    {
        if (request.RuntimeId <= 0) return "Invalid RuntimeId.";
        if (request.Generation < 0) return "Invalid Generation.";
        var validation = TeamLabNetworkPrimitives.ValidateLinuxName(request.ServiceName, nameof(request.ServiceName));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateLinuxName(request.BridgeName, nameof(request.BridgeName));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateLinuxName(request.InterfaceName, nameof(request.InterfaceName));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateIp(request.GatewayIp, nameof(request.GatewayIp));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateCidr(request.Cidr, nameof(request.Cidr));
        if (validation is not null) return validation;
        validation = TeamLabNetworkPrimitives.ValidateHostname(request.Domain, nameof(request.Domain));
        if (validation is not null) return validation;
        foreach (var lease in request.Leases)
        {
            if (!MacRegex().IsMatch(lease.MacAddress)) return "Invalid DHCP lease MAC address.";
            validation = TeamLabNetworkPrimitives.ValidateIp(lease.IpAddress, nameof(request.Leases));
            if (validation is not null) return validation;
            validation = TeamLabNetworkPrimitives.ValidateHostname(lease.Hostname, nameof(request.Leases));
            if (validation is not null) return validation;
        }
        foreach (var record in request.DnsRecords)
        {
            validation = TeamLabNetworkPrimitives.ValidateHostname(record.Hostname, nameof(request.DnsRecords));
            if (validation is not null) return validation;
            validation = TeamLabNetworkPrimitives.ValidateIp(record.IpAddress, nameof(request.DnsRecords));
            if (validation is not null) return validation;
        }
        return null;
    }

    private static TeamLabDryRunResponse Failure(string message, bool dryRun) => new(false, dryRun, message, []);

    [GeneratedRegex("^([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$")]
    private static partial Regex MacRegex();
}
