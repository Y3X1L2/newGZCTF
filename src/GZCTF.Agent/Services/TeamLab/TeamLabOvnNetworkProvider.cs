using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GZCTF.Agent.Models;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabOvnNetworkProvider(
    OvsdbJsonRpcClient ovsdb,
    IOptions<AgentTeamLabConfig> options,
    ILogger<TeamLabOvnNetworkProvider> logger)
{
    readonly AgentTeamLabConfig config = options.Value;

    public async Task<TeamLabOvnApplyResult> ApplyAsync(
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        if (!plan.IsValid(out var validationError))
            return TeamLabOvnApplyResult.Failed("validation", validationError!);
        if (string.IsNullOrWhiteSpace(config.OvnNorthboundEndpoint))
            return TeamLabOvnApplyResult.Failed("capacity", "OVN Northbound endpoint is not configured.");

        if (plan.Networks.Count == 0)
            return new TeamLabOvnApplyResult(true, false, plan.PlanDigest, "network", "No network intent was requested.");

        try
        {
            var existingCount = 0;
            foreach (var network in plan.Networks)
            {
                var rows = await ovsdb.SelectAsync(config.OvnNorthboundEndpoint, config.OvnNorthboundDatabase,
                    "Logical_Switch", Where("name", TeamLabOvnNaming.LogicalNetworkName(plan, network.Key)), cancellationToken);
                if (rows.Count == 0) continue;
                existingCount++;
                var row = rows[0] as JsonObject;
                var ids = row?["external_ids"] as JsonObject;
                if (!string.Equals(ids?["gzctf-plan-digest"]?.GetValue<string>(), plan.PlanDigest, StringComparison.Ordinal))
                    return TeamLabOvnApplyResult.Failed("network", "Existing OVN network identity conflicts with the requested plan.");
            }
            if (existingCount == plan.Networks.Count)
            {
                var portsPresent = 0;
                foreach (var network in plan.Networks)
                foreach (var port in network.Ports)
                {
                    var rows = await ovsdb.SelectAsync(config.OvnNorthboundEndpoint, config.OvnNorthboundDatabase,
                        "Logical_Switch_Port", Where("name", TeamLabOvnNaming.LogicalPortName(plan, network.Key, port.Key)),
                        cancellationToken);
                    portsPresent += rows.Count;
                }
                if (portsPresent == plan.Networks.Sum(item => item.Ports.Count))
                    return new TeamLabOvnApplyResult(true, true, plan.PlanDigest, "network", "Network intent was already applied.");
                return TeamLabOvnApplyResult.Failed("network", "OVN contains a partial execution plan; cleanup is required before reapply.");
            }
            if (existingCount > 0)
                return TeamLabOvnApplyResult.Failed("network", "OVN contains a partial execution plan; cleanup is required before reapply.");

            var operations = new List<JsonObject>();
            var control = plan.NetworkControl;
            foreach (var router in control?.Routers ?? [])
                operations.Add(MutateRouter(plan, router, plan.Networks, control));
            foreach (var network in plan.Networks)
            {
                if (network.DhcpLeases is { Count: > 0 })
                    operations.Add(MutateDhcpOptions(plan, network));
                var switchUuid = StableName(plan, "switch", network.Key);
                operations.Add(MutateNetwork(plan, network, switchUuid, control));
                foreach (var port in network.Ports)
                    operations.Add(MutatePort(plan, network, port, switchUuid));
                if (RouterFor(network, control) is { } router)
                {
                    operations.Add(MutateRouterPort(plan, network, router));
                    operations.Add(MutateRouterSwitchPort(plan, network, router));
                }
                foreach (var policy in network.Policies)
                    operations.Add(MutateAcl(plan, network, policy));
            }

            if (control is not null)
            {
                foreach (var router in control.Routers)
                foreach (var networkKey in router.NetworkKeys)
                {
                    var network = plan.Networks.First(item => item.Key == networkKey);
                    foreach (var route in network.Routes)
                        operations.Add(MutateStaticRoute(plan, router, network, route));
                }
                foreach (var policy in control.ForwardPolicies)
                    foreach (var router in control.Routers.Take(1))
                        operations.Add(MutateRouterPolicy(plan, router, policy));
            }

            await ovsdb.TransactAsync(
                config.OvnNorthboundEndpoint,
                config.OvnNorthboundDatabase,
                operations,
                cancellationToken);
            return new TeamLabOvnApplyResult(true, false, plan.PlanDigest, "network", "Network intent applied.");
        }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(exception,
                "TeamLab OVN transaction failed for runtime {RuntimeId}, generation {Generation}",
                plan.RuntimeId, plan.Generation);
            return TeamLabOvnApplyResult.Failed("network", exception.Message);
        }
    }

    public async Task<TeamLabOvnApplyResult> RemoveAsync(
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        if (!plan.IsValid(out var validationError))
            return TeamLabOvnApplyResult.Failed("validation", validationError!);
        try
        {
            var operations = new List<JsonObject>();
            var control = plan.NetworkControl;
            foreach (var router in control?.Routers ?? [])
            {
                foreach (var policy in control?.ForwardPolicies ?? [])
                    operations.Add(Delete("Logical_Router_Policy", RouterPolicyName(plan, router, policy)));
                foreach (var networkKey in router.NetworkKeys)
                    foreach (var route in plan.Networks.First(item => item.Key == networkKey).Routes
                                 .Where(route => !string.IsNullOrWhiteSpace(route.NextHop)))
                        operations.Add(Delete("Logical_Router_Static_Route", StaticRouteName(plan, router, networkKey, route)));
            }
            foreach (var network in plan.Networks)
            {
                if (RouterFor(network, control) is { } router)
                {
                    operations.Add(new JsonObject
                    {
                        ["op"] = "delete",
                        ["table"] = "Logical_Switch_Port",
                        ["where"] = Where("name", RouterSwitchPortName(plan, network, router))
                    });
                    operations.Add(new JsonObject
                    {
                        ["op"] = "delete",
                        ["table"] = "Logical_Router_Port",
                        ["where"] = Where("name", RouterPortName(plan, network, router))
                    });
                }
                foreach (var policy in network.Policies)
                    operations.Add(Delete("ACL", AclName(plan, network, policy)));
                if (network.DhcpLeases is { Count: > 0 })
                    operations.Add(Delete("DHCP_Options", DhcpName(plan, network)));
                foreach (var port in network.Ports)
                    operations.Add(Delete("Logical_Switch_Port",
                        TeamLabOvnNaming.LogicalPortName(plan, network.Key, port.Key)));
                operations.Add(Delete("Logical_Switch", TeamLabOvnNaming.LogicalNetworkName(plan, network.Key)));
            }
            foreach (var router in control?.Routers ?? [])
            {
                operations.Add(Delete("Logical_Router", RouterName(plan, router)));
            }
            if (operations.Count > 0)
                await ovsdb.TransactAsync(config.OvnNorthboundEndpoint, config.OvnNorthboundDatabase,
                    operations, cancellationToken);
            return new TeamLabOvnApplyResult(true, false, plan.PlanDigest, "cleanup", "Network intent removed.");
        }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(exception, "TeamLab OVN cleanup failed for runtime {RuntimeId}, generation {Generation}",
                plan.RuntimeId, plan.Generation);
            return TeamLabOvnApplyResult.Failed("cleanup", exception.Message);
        }
    }

    static JsonObject MutateNetwork(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, string uuid,
        TeamLabNetworkControlIntentV2? control) => new()
    {
        ["op"] = "insert",
        ["table"] = "Logical_Switch",
        ["uuid-name"] = uuid,
        ["row"] = new JsonObject
        {
            ["name"] = TeamLabOvnNaming.LogicalNetworkName(plan, network.Key),
            ["ports"] = References(network.Ports.Select(port => StableName(plan, "port", $"{network.Key}:{port.Key}"))
                .Concat(RouterFor(network, control) is { } router
                    ? [RouterSwitchPortNamedUuid(plan, network, router)]
                    : [])),
            ["acls"] = References(network.Policies.Select(policy => AclName(plan, network, policy))),
            ["external_ids"] = new JsonObject
            {
                ["gzctf-network-key"] = network.Key,
                ["gzctf-cidr"] = network.Cidr,
                ["gzctf-runtime"] = plan.RuntimePublicId.ToString("D"),
                ["gzctf-generation"] = plan.Generation.ToString(),
                ["gzctf-plan-digest"] = plan.PlanDigest
            }
        }
    };

    static JsonObject MutatePort(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network,
        TeamLabNetworkPortV2 port, string switchUuid) => new()
    {
        ["op"] = "insert",
        ["table"] = "Logical_Switch_Port",
        ["uuid-name"] = StableName(plan, "port", $"{network.Key}:{port.Key}"),
            ["row"] = new JsonObject
            {
                ["name"] = TeamLabOvnNaming.LogicalPortName(plan, network.Key, port.Key),
                ["switch"] = new JsonArray { "named-uuid", switchUuid },
            ["addresses"] = Set([ $"{port.MacAddress} {port.IpAddress ?? ""}".Trim() ]),
            ["dhcpv4_options"] = network.DhcpLeases is { Count: > 0 }
                ? NamedUuid(DhcpName(plan, network))
                : null,
            ["external_ids"] = new JsonObject
            {
                ["gzctf-runtime"] = plan.RuntimePublicId.ToString("D"),
                ["gzctf-generation"] = plan.Generation.ToString(),
                ["gzctf-asset-key"] = port.AssetKey,
                ["gzctf-network-key"] = network.Key
            }
        }
    };

    static JsonObject MutateDhcpOptions(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network) => new()
    {
        ["op"] = "insert",
        ["table"] = "DHCP_Options",
        ["uuid-name"] = DhcpName(plan, network),
        ["row"] = new JsonObject
        {
            ["cidr"] = network.Cidr,
            ["options"] = new JsonObject
            {
                ["server_id"] = network.GatewayIp,
                ["router"] = network.GatewayIp,
                ["lease_time"] = "3600"
            },
            ["external_ids"] = Identity(plan, network.Key)
        }
    };

    static JsonObject MutateRouter(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router,
        IReadOnlyList<TeamLabNetworkIntentV2> networks, TeamLabNetworkControlIntentV2? control) => new()
    {
        ["op"] = "insert",
        ["table"] = "Logical_Router",
        ["uuid-name"] = RouterName(plan, router),
        ["row"] = new JsonObject
        {
            ["name"] = RouterName(plan, router),
            ["ports"] = References(router.NetworkKeys.Select(networkKey =>
                RouterPortNamedUuid(plan, networks.First(network => network.Key == networkKey), router))),
            ["static_routes"] = References(router.NetworkKeys.SelectMany(networkKey =>
                networks.First(network => network.Key == networkKey).Routes
                    .Where(route => !string.IsNullOrWhiteSpace(route.NextHop)).Select(route =>
                    StaticRouteName(plan, router, networkKey, route)))),
            ["policies"] = References((control?.ForwardPolicies ?? []).Select(policy =>
                RouterPolicyName(plan, router, policy))),
            ["external_ids"] = Identity(plan, router.Key)
        }
    };

    static JsonObject MutateRouterPort(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network,
        TeamLabRouterIntentV2 router) => new()
    {
        ["op"] = "insert",
        ["table"] = "Logical_Router_Port",
        ["uuid-name"] = RouterPortNamedUuid(plan, network, router),
        ["row"] = new JsonObject
        {
            ["name"] = RouterPortName(plan, network, router),
            ["mac"] = RouterMac(plan, network, router),
            ["networks"] = Set([$"{network.GatewayIp}/{PrefixLength(network.Cidr)}"]),
            ["external_ids"] = Identity(plan, $"{router.Key}:{network.Key}")
        }
    };

    static JsonObject MutateRouterSwitchPort(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network,
        TeamLabRouterIntentV2 router) => new()
    {
        ["op"] = "insert",
        ["table"] = "Logical_Switch_Port",
        ["uuid-name"] = RouterSwitchPortNamedUuid(plan, network, router),
        ["row"] = new JsonObject
        {
            ["name"] = RouterSwitchPortName(plan, network, router),
            ["type"] = "router",
            ["options"] = new JsonObject { ["router-port"] = RouterPortName(plan, network, router) },
            ["addresses"] = Set([RouterMac(plan, network, router)]),
            ["dhcpv4_options"] = network.DhcpLeases is { Count: > 0 }
                ? NamedUuid(DhcpName(plan, network))
                : null,
            ["external_ids"] = Identity(plan, $"{router.Key}:{network.Key}:router")
        }
    };

    static JsonObject MutateAcl(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network,
        TeamLabNetworkPolicyV2 policy) => new()
    {
        ["op"] = "insert",
        ["table"] = "ACL",
        ["uuid-name"] = AclName(plan, network, policy),
        ["row"] = new JsonObject
        {
            ["direction"] = "to-lport",
            ["priority"] = 1000,
            ["match"] = AclMatch(policy),
            ["action"] = policy.Allow ? "allow" : "drop",
            ["external_ids"] = Identity(plan, $"{network.Key}:{AclName(plan, network, policy)}")
        }
    };

    static JsonObject MutateStaticRoute(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router,
        TeamLabNetworkIntentV2 network, TeamLabNetworkRouteV2 route) => new()
    {
        ["op"] = "insert",
        ["table"] = "Logical_Router_Static_Route",
        ["uuid-name"] = StaticRouteName(plan, router, network.Key, route),
        ["row"] = new JsonObject
        {
            ["ip_prefix"] = route.DestinationCidr,
            ["nexthop"] = route.NextHop,
            ["output_port"] = NamedUuid(RouterPortNamedUuid(plan, network, router)),
            ["external_ids"] = Identity(plan, $"{router.Key}:{network.Key}:{route.DestinationCidr}")
        }
    };

    static JsonObject MutateRouterPolicy(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router,
        TeamLabForwardPolicyV2 policy) => new()
    {
        ["op"] = "insert",
        ["table"] = "Logical_Router_Policy",
        ["uuid-name"] = RouterPolicyName(plan, router, policy),
        ["row"] = new JsonObject
        {
            ["priority"] = 1000,
            ["match"] = $"ip4.src == {policy.SourceCidr} && ip4.dst == {policy.DestinationCidr}",
            ["action"] = policy.Allow ? "allow" : "drop",
            ["external_ids"] = Identity(plan, $"{router.Key}:policy")
        }
    };

    static string AclMatch(TeamLabNetworkPolicyV2 policy)
    {
        var protocol = string.IsNullOrWhiteSpace(policy.Protocol) ||
                       policy.Protocol.Equals("any", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" && ip4.proto == {policy.Protocol.ToLowerInvariant()}";
        var port = policy.Port is { } value &&
                   policy.Protocol is not null &&
                   (policy.Protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase) ||
                    policy.Protocol.Equals("udp", StringComparison.OrdinalIgnoreCase))
            ? $" && {policy.Protocol.ToLowerInvariant()}.dst == {value}"
            : string.Empty;
        return $"ip4.src == {policy.SourceCidr} && ip4.dst == {policy.DestinationCidr}{protocol}{port}";
    }

    static TeamLabRouterIntentV2? RouterFor(TeamLabNetworkIntentV2 network,
        TeamLabNetworkControlIntentV2? control) =>
        control?.Routers.FirstOrDefault(router => router.NetworkKeys.Contains(network.Key, StringComparer.Ordinal));

    static JsonObject Identity(TeamLabExecutionPlanV2 plan, string key) => new()
    {
        ["gzctf-runtime"] = plan.RuntimePublicId.ToString("D"),
        ["gzctf-generation"] = plan.Generation.ToString(),
        ["gzctf-key"] = key,
        ["gzctf-plan-digest"] = plan.PlanDigest
    };

    static JsonArray Set(IEnumerable<string> values) => new() { "set", new JsonArray(values.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()) };
    static JsonArray References(IEnumerable<string> values) => new()
    {
        "set", new JsonArray(values.Select(value => (JsonNode)NamedUuid(value)).ToArray())
    };
    static JsonArray NamedUuid(string value) => new() { "named-uuid", value };
    static JsonObject Delete(string table, string name) => new() { ["op"] = "delete", ["table"] = table, ["where"] = Where("name", name) };
    static string RouterName(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router) => StableName(plan, "router", router.Key);
    static string RouterPortName(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) => StableName(plan, "router-port", $"{router.Key}:{network.Key}");
    static string RouterPortNamedUuid(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) => StableName(plan, "router-port-row", $"{router.Key}:{network.Key}");
    static string RouterSwitchPortNamedUuid(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) => StableName(plan, "router-switch-port-row", $"{router.Key}:{network.Key}");
    static string RouterSwitchPortName(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) => StableName(plan, "router-switch-port", $"{router.Key}:{network.Key}");
    static string DhcpName(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network) => StableName(plan, "dhcp", network.Key);
    static string AclName(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabNetworkPolicyV2 policy) => StableName(plan, "acl", $"{network.Key}:{policy.SourceCidr}:{policy.DestinationCidr}:{policy.Protocol}:{policy.Port}:{policy.Allow}");
    static string StaticRouteName(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router, string networkKey, TeamLabNetworkRouteV2 route) => StableName(plan, "route", $"{router.Key}:{networkKey}:{route.DestinationCidr}:{route.NextHop}");
    static string RouterPolicyName(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router, TeamLabForwardPolicyV2 policy) => StableName(plan, "router-policy", $"{router.Key}:{policy.SourceCidr}:{policy.DestinationCidr}:{policy.Allow}");
    static string RouterMac(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) =>
        $"02:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{plan.RuntimePublicId:D}:{plan.Generation}:{router.Key}:{network.Key}")))[..10].ToLowerInvariant().Insert(2, ":").Insert(5, ":").Insert(8, ":").Insert(11, ":")}";

    static JsonArray Where(string column, string value) =>
        new() { new JsonArray { column, "==", value } };

    static int PrefixLength(string cidr)
    {
        var separator = cidr.LastIndexOf('/');
        return separator > 0 && int.TryParse(cidr[(separator + 1)..], out var prefix)
            ? prefix
            : throw new InvalidOperationException($"Network CIDR has no valid prefix: {cidr}");
    }

    static string StableName(TeamLabExecutionPlanV2 plan, string kind, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"gzctf:teamlab:{plan.RuntimePublicId:D}:{plan.Generation}:{kind}:{key}"));
        var guid = new Guid(bytes[..16]);
        return $"gzctf_{kind}_{guid:N}";
    }
}

public sealed record TeamLabOvnApplyResult(
    bool Success,
    bool AlreadyApplied,
    string? Digest,
    string Stage,
    string Message)
{
    public static TeamLabOvnApplyResult Failed(string stage, string message) =>
        new(false, false, null, stage, message);
}
