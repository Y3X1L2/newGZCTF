using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabRouterService(TeamLabCommandExecutor executor)
{
    public Task<TeamLabDryRunResponse> ApplyAsync(TeamLabRouterRequest request, CancellationToken token)
    {
        var validation = Validate(request);
        if (validation is not null) return Task.FromResult(new TeamLabDryRunResponse(false, request.DryRun, validation, []));

        var commands = new List<string>
        {
            $"ip netns pids {request.NamespaceName} 2>/dev/null | xargs -r kill 2>/dev/null || true",
            $"ip netns delete {request.NamespaceName} 2>/dev/null || true",
            $"ip netns add {request.NamespaceName}",
            $"ip netns exec {request.NamespaceName} ip link set lo up"
        };
        for (var index = 0; index < request.Interfaces.Length; index++)
        {
            var item = request.Interfaces[index];
            var host = TeamLabNetworkPrimitives.TrimInterfaceName($"{request.NamespaceName}h{index}");
            var peer = TeamLabNetworkPrimitives.TrimInterfaceName($"{request.NamespaceName}n{index}");
            commands.Add($"ip link delete {host} 2>/dev/null || true");
            commands.Add($"ip link add {host} type veth peer name {peer}");
            commands.Add($"ip link set {host} master {item.BridgeName}");
            commands.Add($"ip link set {host} up");
            commands.Add($"ip link set {peer} netns {request.NamespaceName}");
            commands.Add($"ip netns exec {request.NamespaceName} ip addr flush dev {peer}");
            commands.Add($"ip netns exec {request.NamespaceName} ip addr add {item.GatewayAddressCidr} dev {peer}");
            commands.Add($"ip netns exec {request.NamespaceName} ip link set {peer} up");
        }
        commands.Add($"ip netns exec {request.NamespaceName} sysctl -w net.ipv4.ip_forward=1");
        commands.AddRange(request.Routes.Select(route =>
            $"ip netns exec {request.NamespaceName} ip route replace {route.TargetCidr} via {route.GatewayIp}"));
        return executor.ExecuteAsync(commands, request.DryRun, token);
    }

    private static string? Validate(TeamLabRouterRequest request)
    {
        var validation = TeamLabNetworkPrimitives.ValidateLinuxName(request.NamespaceName, nameof(request.NamespaceName));
        if (validation is not null) return validation;
        if (request.Interfaces.Length == 0) return "At least one router interface is required.";
        foreach (var item in request.Interfaces)
        {
            validation = TeamLabNetworkPrimitives.ValidateLinuxName(item.BridgeName, nameof(request.Interfaces));
            if (validation is not null) return validation;
            validation = TeamLabNetworkPrimitives.ValidateCidr(item.GatewayAddressCidr, nameof(item.GatewayAddressCidr));
            if (validation is not null) return validation;
        }
        foreach (var route in request.Routes)
        {
            validation = TeamLabNetworkPrimitives.ValidateCidr(route.TargetCidr, nameof(route.TargetCidr));
            if (validation is not null) return validation;
            validation = TeamLabNetworkPrimitives.ValidateIp(route.GatewayIp, nameof(route.GatewayIp));
            if (validation is not null) return validation;
        }
        return null;
    }
}
