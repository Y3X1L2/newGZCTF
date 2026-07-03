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
        var available = hasIp && hasWg;
        var message = available ? null : "ip or WireGuard command is missing on this WorkerNode.";

        return Task.FromResult(new TeamLabStatusResponse(
            available,
            _config.Enable,
            _config.DryRun,
            hasIp,
            hasWg,
            DateTimeOffset.UtcNow,
            message));
    }

    public async Task<TeamLabDryRunResponse> CreateBridgeAsync(TeamLabBridgeRequest request, CancellationToken token)
    {
        var validation = ValidateLinuxName(request.BridgeName, nameof(request.BridgeName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateCidr(request.Cidr, nameof(request.Cidr));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateIp(request.GatewayIp, nameof(request.GatewayIp));
        if (validation is not null) return Failure(validation, request.DryRun);

        var prefix = request.Cidr.Split('/')[1];
        var commands = new[]
        {
            $"ip link add {request.BridgeName} type bridge",
            $"ip addr add {request.GatewayIp}/{prefix} dev {request.BridgeName}",
            $"ip link set {request.BridgeName} up"
        };

        return await ExecuteOrPlanAsync(commands.ToArray(), request.DryRun, token);
    }

    public async Task<TeamLabDryRunResponse> CreateRouterAsync(TeamLabRouterRequest request, CancellationToken token)
    {
        var validation = ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        foreach (var bridge in request.BridgeNames)
        {
            validation = ValidateLinuxName(bridge, nameof(request.BridgeNames));
            if (validation is not null) return Failure(validation, request.DryRun);
        }

        var commands = new List<string> { $"ip netns add {request.NamespaceName}" };
        for (var i = 0; i < request.BridgeNames.Length; i++)
        {
            var bridge = request.BridgeNames[i];
            var hostIf = TrimInterfaceName($"{request.NamespaceName}h{i}");
            var nsIf = TrimInterfaceName($"{request.NamespaceName}n{i}");
            commands.Add($"ip link add {hostIf} type veth peer name {nsIf}");
            commands.Add($"ip link set {hostIf} master {bridge}");
            commands.Add($"ip link set {hostIf} up");
            commands.Add($"ip link set {nsIf} netns {request.NamespaceName}");
            commands.Add($"ip netns exec {request.NamespaceName} ip link set {nsIf} up");
        }

        commands.Add($"ip netns exec {request.NamespaceName} sysctl -w net.ipv4.ip_forward=1");
        return await ExecuteOrPlanAsync(commands.ToArray(), request.DryRun, token);
    }

    public async Task<TeamLabDryRunResponse> ConfigureWireGuardAsync(TeamLabWireGuardRequest request, CancellationToken token)
    {
        var validation = ValidateLinuxName(request.InterfaceName, nameof(request.InterfaceName));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidatePort(request.ListenPort, nameof(request.ListenPort));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateCidr(request.AddressCidr, nameof(request.AddressCidr));
        if (validation is not null) return Failure(validation, request.DryRun);

        validation = ValidateAllowedIps(request.PeerAllowedIps, nameof(request.PeerAllowedIps));
        if (validation is not null) return Failure(validation, request.DryRun);

        if (!TokenRegex().IsMatch(request.PeerPublicKey))
            return Failure("Invalid PeerPublicKey.", request.DryRun);

        var commands = new[]
        {
            $"ip link add {request.InterfaceName} type wireguard",
            $"ip addr add {request.AddressCidr} dev {request.InterfaceName}",
            $"wg set {request.InterfaceName} listen-port {request.ListenPort} peer {request.PeerPublicKey} allowed-ips {request.PeerAllowedIps}",
            $"ip link set {request.InterfaceName} up"
        };

        return await ExecuteOrPlanAsync(commands, request.DryRun, token);
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
                $"ip link delete {name} 2>/dev/null || true",
                $"ip netns delete {name} 2>/dev/null || true"
            })
            .ToArray();

        return await ExecuteOrPlanAsync(commands, request.DryRun, token);
    }

    private async Task<TeamLabDryRunResponse> ExecuteOrPlanAsync(string[] commands, bool requestDryRun, CancellationToken token)
    {
        var dryRun = _config.DryRun || requestDryRun || !_config.Enable;
        if (dryRun)
            return new TeamLabDryRunResponse(true, true, "Dry-run command plan generated.", commands);

        foreach (var command in commands)
        {
            var result = await runner.RunAsync(command, token);
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

    [GeneratedRegex("^[a-zA-Z0-9_.-]+$")]
    private static partial Regex LinuxNameRegex();

    [GeneratedRegex("^[a-zA-Z0-9+/=_.:-]+$")]
    private static partial Regex TokenRegex();
}
