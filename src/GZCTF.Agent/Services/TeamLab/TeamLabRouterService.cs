using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabRouterService(TeamLabCommandExecutor executor)
{
    private const int ManagedRouteProtocol = 186;

    public Task<TeamLabDryRunResponse> ApplyAsync(TeamLabRouterRequest request, CancellationToken token)
    {
        var validation = Validate(request);
        if (validation is not null) return Task.FromResult(new TeamLabDryRunResponse(false, request.DryRun, validation, []));

        var interfaces = request.Interfaces
            .Select((item, index) => new RouterInterface(
                item,
                TeamLabNetworkPrimitives.TrimInterfaceName($"{request.NamespaceName}h{index}"),
                TeamLabNetworkPrimitives.TrimInterfaceName($"{request.NamespaceName}n{index}")))
            .ToArray();
        var owner = $"gzctf-teamlab-router:{request.RuntimeId}";
        var commands = new List<string>
        {
            $"ip netns list | awk '{{print $1}}' | grep -Fx {TeamLabNetworkPrimitives.ShellQuote(request.NamespaceName)} >/dev/null || ip netns add {request.NamespaceName}",
            $"ip netns exec {request.NamespaceName} ip link set lo up",
            BuildStaleInterfaceCleanup(owner, interfaces.Select(item => item.HostName))
        };
        foreach (var item in interfaces)
        {
            commands.Add(TeamLabNetworkPrimitives.BuildEnsureVethPairCommand(
                request.NamespaceName, item.HostName, item.PeerName));
            commands.Add($"ip link set {item.HostName} alias {TeamLabNetworkPrimitives.ShellQuote(owner)}");
            commands.Add($"ip link set {item.HostName} master {item.Request.BridgeName}");
            commands.Add($"ip link set {item.HostName} up");
            commands.Add(TeamLabNetworkPrimitives.BuildNamespaceIpv4AddressConvergenceCommand(
                request.NamespaceName, item.PeerName, item.Request.GatewayAddressCidr));
            commands.Add($"ip netns exec {request.NamespaceName} ip link set {item.PeerName} up");
        }
        commands.Add($"ip netns exec {request.NamespaceName} sysctl -w net.ipv4.ip_forward=1");
        commands.Add(BuildStaleRouteCleanup(request.NamespaceName, request.Routes.Select(route => route.TargetCidr)));
        commands.AddRange(request.Routes.Select(route =>
            $"ip netns exec {request.NamespaceName} ip route replace {route.TargetCidr} via {route.GatewayIp} proto {ManagedRouteProtocol}"));
        return executor.ExecuteAsync(commands, request.DryRun, token);
    }

    private static string BuildStaleInterfaceCleanup(string owner, IEnumerable<string> desiredNames)
    {
        var desired = TeamLabNetworkPrimitives.ShellQuote(
            $" {string.Join(' ', desiredNames.Order(StringComparer.Ordinal))} ");
        return $"managed={desired}; for alias_path in /sys/class/net/*/ifalias; do " +
               "test -r \"$alias_path\" || continue; " +
               $"test \"$(cat \"$alias_path\")\" = {TeamLabNetworkPrimitives.ShellQuote(owner)} || continue; " +
               "managed_if=$(basename \"$(dirname \"$alias_path\")\"); " +
               "case \"$managed\" in *\" $managed_if \"*) ;; *) ip link delete \"$managed_if\" ;; esac; done";
    }

    private static string BuildStaleRouteCleanup(string namespaceName, IEnumerable<string> desiredCidrs)
    {
        var desired = TeamLabNetworkPrimitives.ShellQuote(
            $" {string.Join(' ', desiredCidrs.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))} ");
        return $"managed={desired}; ip netns exec {namespaceName} ip -o route show proto {ManagedRouteProtocol} | " +
               "awk '{print $1}' | while read managed_route; do " +
               "case \"$managed\" in *\" $managed_route \"*) ;; *) " +
               $"ip netns exec {namespaceName} ip route del \"$managed_route\" proto {ManagedRouteProtocol} ;; esac; done";
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

        // Two interfaces sharing a name would make "ip link delete" for a later one tear down an
        // earlier veth pair, wiring a segment to the wrong bridge. Fail the request instead of
        // letting the kernel act on ambiguous names.
        var derived = Enumerable.Range(0, request.Interfaces.Length)
            .SelectMany(index => new[]
            {
                TeamLabNetworkPrimitives.TrimInterfaceName($"{request.NamespaceName}h{index}"),
                TeamLabNetworkPrimitives.TrimInterfaceName($"{request.NamespaceName}n{index}")
            })
            .ToArray();
        if (derived.Distinct(StringComparer.Ordinal).Count() != derived.Length)
            return "Router interface names collide; refusing to apply an ambiguous topology.";

        return null;
    }

    private sealed record RouterInterface(
        TeamLabRouterInterfaceRequest Request,
        string HostName,
        string PeerName);
}
