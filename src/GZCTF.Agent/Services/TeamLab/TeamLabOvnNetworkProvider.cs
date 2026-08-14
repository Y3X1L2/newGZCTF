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
            return new TeamLabOvnApplyResult(true, false, "network", "No network intent was requested.");
        if (!TryValidateDnsHostnames(plan.Networks, out var dnsValidationError))
            return TeamLabOvnApplyResult.Failed("validation", dnsValidationError!);

        try
        {
            var switches = await ovsdb.SelectAsync(config.OvnNorthboundEndpoint, config.OvnNorthboundDatabase,
                plan.Networks.Select(network => ("Logical_Switch",
                    Where("name", TeamLabOvnNaming.LogicalNetworkName(plan, network.Key)))).ToArray(), cancellationToken);
            var existingCount = 0;
            foreach (var rows in switches)
            {
                if (rows.Count == 0) continue;
                existingCount++;
                var row = rows[0] as JsonObject;
                var digest = OvsdbJsonCodec.GetMapValue(row?["external_ids"], "gzctf-plan-digest");
                if (!string.Equals(digest, plan.PlanDigest, StringComparison.Ordinal))
                    return TeamLabOvnApplyResult.Failed("network", "Existing OVN network identity conflicts with the requested plan.");
            }
            if (existingCount == plan.Networks.Count)
            {
                if (await AllResourcesPresentAsync(plan, cancellationToken))
                    return new TeamLabOvnApplyResult(true, true, "network", "Network intent was already applied.");
                return TeamLabOvnApplyResult.Failed("network", "OVN contains a partial execution plan; cleanup is required before reapply.");
            }
            if (existingCount > 0)
                return TeamLabOvnApplyResult.Failed("network", "OVN contains a partial execution plan; cleanup is required before reapply.");

            await ovsdb.TransactAsync(
                config.OvnNorthboundEndpoint,
                config.OvnNorthboundDatabase,
                BuildApplyOperations(plan),
                cancellationToken);
            return new TeamLabOvnApplyResult(true, false, "network", "Network intent applied.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception,
                "TeamLab OVN transaction timed out for runtime {RuntimeId}, generation {Generation}",
                plan.RuntimeId, plan.Generation);
            return TeamLabOvnApplyResult.Failed("network", "OVN transaction timed out.");
        }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(exception,
                "TeamLab OVN transaction failed for runtime {RuntimeId}, generation {Generation}",
                plan.RuntimeId, plan.Generation);
            return TeamLabOvnApplyResult.Failed("network", $"OVN transaction failed: {Trim(exception.Message)}");
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
            var operations = BuildRemoveOperations(plan);
            if (operations.Count > 0)
                await ovsdb.TransactAsync(config.OvnNorthboundEndpoint, config.OvnNorthboundDatabase,
                    operations, cancellationToken);
            return new TeamLabOvnApplyResult(true, false, "cleanup", "Network intent removed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(exception, "TeamLab OVN cleanup timed out for runtime {RuntimeId}, generation {Generation}",
                plan.RuntimeId, plan.Generation);
            return TeamLabOvnApplyResult.Failed("cleanup", "OVN cleanup timed out.");
        }
        catch (Exception exception) when (exception is SocketException or IOException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(exception, "TeamLab OVN cleanup failed for runtime {RuntimeId}, generation {Generation}",
                plan.RuntimeId, plan.Generation);
            return TeamLabOvnApplyResult.Failed("cleanup", $"OVN cleanup transaction failed: {Trim(exception.Message)}");
        }
    }

    internal IReadOnlyList<JsonObject> BuildApplyOperations(TeamLabExecutionPlanV2 plan)
    {
        var operations = new List<JsonObject>();
        var control = plan.NetworkControl;
        foreach (var router in control?.Routers ?? [])
            operations.Add(MutateRouter(plan, router, plan.Networks, control));
        foreach (var network in plan.Networks)
        {
            if (network.DhcpLeases is { Count: > 0 })
                operations.Add(MutateDhcpOptions(plan, network));
            if (network.DnsRecords is { Count: > 0 })
                operations.Add(MutateDns(plan, network));
            var switchUuid = StableUuid(plan, "switch", network.Key);
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
                foreach (var router in control.Routers)
                    operations.Add(MutateRouterPolicy(plan, router, policy));
        }

        return operations;
    }

    internal static IReadOnlyList<JsonObject> BuildRemoveOperations(TeamLabExecutionPlanV2 plan)
    {
        string[] tables =
        [
            "Logical_Router_Policy",
            "Logical_Router_Static_Route",
            "Logical_Router_Port",
            "Logical_Router",
            "ACL",
            "DNS",
            "DHCP_Options",
            "Logical_Switch_Port",
            "Logical_Switch"
        ];
        return tables.Select(table => DeleteOwned(table, plan)).ToArray();
    }

    static JsonObject DeleteOwned(string table, TeamLabExecutionPlanV2 plan) => new()
    {
        ["op"] = "delete",
        ["table"] = table,
        ["where"] = OvsdbJsonCodec.OwnedWhere(plan)
    };

    async Task<bool> AllResourcesPresentAsync(TeamLabExecutionPlanV2 plan, CancellationToken cancellationToken)
    {
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        var control = plan.NetworkControl;
        foreach (var network in plan.Networks)
        {
            AddExpected(expected, "Logical_Switch", 1);
            AddExpected(expected, "Logical_Switch_Port", network.Ports.Count +
                (RouterFor(network, control) is null ? 0 : 1));
            if (network.DhcpLeases is { Count: > 0 }) AddExpected(expected, "DHCP_Options", 1);
            if (network.DnsRecords is { Count: > 0 }) AddExpected(expected, "DNS", 1);
            AddExpected(expected, "ACL", network.Policies.Count);
            if (RouterFor(network, control) is not null)
                AddExpected(expected, "Logical_Router_Port", 1);
        }
        foreach (var router in control?.Routers ?? [])
        {
            AddExpected(expected, "Logical_Router", 1);
            AddExpected(expected, "Logical_Router_Static_Route", router.NetworkKeys
                .Select(networkKey => plan.Networks.Single(network => network.Key == networkKey).Routes
                    .Count(route => !string.IsNullOrWhiteSpace(route.NextHop))).Sum());
            AddExpected(expected, "Logical_Router_Policy", control?.ForwardPolicies.Count ?? 0);
        }

        var tables = expected.Keys.Order(StringComparer.Ordinal).ToArray();
        var rows = await ovsdb.SelectAsync(config.OvnNorthboundEndpoint, config.OvnNorthboundDatabase,
            tables.Select(table => (table, OvsdbJsonCodec.OwnedWhere(plan))).ToArray(), cancellationToken);
        for (var index = 0; index < tables.Length; index++)
        {
            var tableRows = rows[index];
            if (tableRows.Count != expected[tables[index]] ||
                tableRows.Any(row => row is not JsonObject json ||
                    !string.Equals(OvsdbJsonCodec.GetMapValue(json["external_ids"], "gzctf-plan-digest"),
                        plan.PlanDigest, StringComparison.Ordinal)))
                return false;
        }
        return true;
    }

    static void AddExpected(Dictionary<string, int> expected, string table, int count)
    {
        if (count <= 0) return;
        expected[table] = expected.GetValueOrDefault(table) + count;
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
            ["ports"] = References(network.Ports.Select(port => StableUuid(plan, "port", $"{network.Key}:{port.Key}"))
                .Concat(RouterFor(network, control) is { } router
                    ? [RouterSwitchPortNamedUuid(plan, network, router)]
                    : [])),
            ["acls"] = References(network.Policies.Select(policy => AclUuid(plan, network, policy))),
            ["dns_records"] = References(network.DnsRecords is { Count: > 0 }
                ? [DnsUuid(plan, network)]
                : []),
            ["external_ids"] = OvsdbJsonCodec.Map(
                ("gzctf-network-key", network.Key),
                ("gzctf-cidr", network.Cidr),
                ("gzctf-runtime", plan.RuntimePublicId.ToString("D")),
                ("gzctf-generation", plan.Generation.ToString()),
                ("gzctf-plan-digest", plan.PlanDigest))
        }
    };

    static JsonObject MutatePort(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network,
        TeamLabNetworkPortV2 port, string switchUuid) => new()
    {
        ["op"] = "insert",
        ["table"] = "Logical_Switch_Port",
        ["uuid-name"] = StableUuid(plan, "port", $"{network.Key}:{port.Key}"),
            ["row"] = new JsonObject
            {
                ["name"] = TeamLabOvnNaming.LogicalPortName(plan, network.Key, port.Key),
                ["switch"] = new JsonArray { "named-uuid", switchUuid },
            ["addresses"] = Set([ $"{port.MacAddress} {port.IpAddress ?? ""}".Trim() ]),
            ["dhcpv4_options"] = network.DhcpLeases is { Count: > 0 }
                ? NamedUuid(DhcpUuid(plan, network))
                : null,
            ["external_ids"] = OvsdbJsonCodec.Map(
                ("gzctf-runtime", plan.RuntimePublicId.ToString("D")),
                ("gzctf-generation", plan.Generation.ToString()),
                ("gzctf-asset-key", port.AssetKey),
                ("gzctf-network-key", network.Key),
                ("gzctf-plan-digest", plan.PlanDigest))
        }
    };

    JsonObject MutateDhcpOptions(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network) => new()
    {
        ["op"] = "insert",
        ["table"] = "DHCP_Options",
        ["uuid-name"] = DhcpUuid(plan, network),
        ["row"] = new JsonObject
        {
            ["cidr"] = network.Cidr,
            ["options"] = OvsdbJsonCodec.Map(
                ("server_id", network.GatewayIp ?? string.Empty),
                ("server_mac", DhcpServerMac(plan, network)),
                ("router", network.GatewayIp ?? string.Empty),
                ("lease_time", Math.Clamp(config.ManagedDhcpLeaseSeconds, 60, 86_400).ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ["external_ids"] = Identity(plan, network.Key)
        }
    };

    internal static bool TryValidateDnsHostnames(IReadOnlyList<TeamLabNetworkIntentV2> networks, out string? error)
    {
        foreach (var network in networks)
        foreach (var records in (network.DnsRecords ?? []).GroupBy(record => record.Hostname,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (records.Select(record => record.IpAddress).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            {
                error = $"DNS hostname '{records.Key}' resolves to multiple addresses in network '{network.Key}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    internal static JsonArray BuildDnsRecords(IReadOnlyList<TeamLabDnsRecordV2> records) =>
        OvsdbJsonCodec.Map(records
            .GroupBy(record => record.Hostname, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, group.First().IpAddress))
            .ToArray());

    static JsonObject MutateDns(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network) => new()
    {
        ["op"] = "insert",
        ["table"] = "DNS",
        ["uuid-name"] = DnsUuid(plan, network),
        ["row"] = new JsonObject
        {
            ["records"] = BuildDnsRecords(network.DnsRecords!),
            ["external_ids"] = Identity(plan, $"{network.Key}:dns")
        }
    };

    static JsonObject MutateRouter(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router,
        IReadOnlyList<TeamLabNetworkIntentV2> networks, TeamLabNetworkControlIntentV2? control) => new()
    {
        ["op"] = "insert",
        ["table"] = "Logical_Router",
        ["uuid-name"] = RouterUuid(plan, router),
        ["row"] = new JsonObject
        {
            ["name"] = RouterName(plan, router),
            ["ports"] = References(router.NetworkKeys.Select(networkKey =>
                RouterPortNamedUuid(plan, networks.First(network => network.Key == networkKey), router))),
            ["static_routes"] = References(router.NetworkKeys.SelectMany(networkKey =>
                networks.First(network => network.Key == networkKey).Routes
                    .Where(route => !string.IsNullOrWhiteSpace(route.NextHop)).Select(route =>
                    StaticRouteUuid(plan, router, networkKey, route)))),
            ["policies"] = References((control?.ForwardPolicies ?? []).Select(policy =>
                RouterPolicyUuid(plan, router, policy))),
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
            ["options"] = OvsdbJsonCodec.Map(("router-port", RouterPortName(plan, network, router))),
            ["addresses"] = Set([RouterMac(plan, network, router)]),
            ["dhcpv4_options"] = network.DhcpLeases is { Count: > 0 }
                ? NamedUuid(DhcpUuid(plan, network))
                : null,
            ["external_ids"] = Identity(plan, $"{router.Key}:{network.Key}:router")
        }
    };

    static JsonObject MutateAcl(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network,
        TeamLabNetworkPolicyV2 policy) => new()
    {
        ["op"] = "insert",
        ["table"] = "ACL",
        ["uuid-name"] = AclUuid(plan, network, policy),
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
        ["uuid-name"] = StaticRouteUuid(plan, router, network.Key, route),
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
        ["uuid-name"] = RouterPolicyUuid(plan, router, policy),
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

    static JsonArray Identity(TeamLabExecutionPlanV2 plan, string key) =>
        OvsdbJsonCodec.Map(
            ("gzctf-runtime", plan.RuntimePublicId.ToString("D")),
            ("gzctf-generation", plan.Generation.ToString()),
            ("gzctf-key", key),
            ("gzctf-plan-digest", plan.PlanDigest));

    static JsonArray Set(IEnumerable<string> values) => new() { "set", new JsonArray(values.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()) };
    static JsonArray References(IEnumerable<string> values) => new()
    {
        "set", new JsonArray(values.Select(value => (JsonNode)NamedUuid(value)).ToArray())
    };
    static JsonArray NamedUuid(string value) => new() { "named-uuid", value };
    static string RouterName(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router) => StableName(plan, "router", router.Key);
    static string RouterPortName(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) => StableName(plan, "router-port", $"{router.Key}:{network.Key}");
    static string RouterPortNamedUuid(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) => StableUuid(plan, "router-port-row", $"{router.Key}:{network.Key}");
    static string RouterSwitchPortNamedUuid(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) => StableUuid(plan, "router-switch-port-row", $"{router.Key}:{network.Key}");
    static string RouterSwitchPortName(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) => StableName(plan, "router-switch-port", $"{router.Key}:{network.Key}");
    static string RouterUuid(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router) => StableUuid(plan, "router", router.Key);
    static string DhcpUuid(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network) => StableUuid(plan, "dhcp", network.Key);
    static string DnsUuid(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network) => StableUuid(plan, "dns", network.Key);
    static string AclName(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabNetworkPolicyV2 policy) => StableName(plan, "acl", $"{network.Key}:{policy.SourceCidr}:{policy.DestinationCidr}:{policy.Protocol}:{policy.Port}:{policy.Allow}");
    static string AclUuid(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabNetworkPolicyV2 policy) => StableUuid(plan, "acl", $"{network.Key}:{policy.SourceCidr}:{policy.DestinationCidr}:{policy.Protocol}:{policy.Port}:{policy.Allow}");
    static string StaticRouteUuid(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router, string networkKey, TeamLabNetworkRouteV2 route) => StableUuid(plan, "route", $"{router.Key}:{networkKey}:{route.DestinationCidr}:{route.NextHop}");
    static string RouterPolicyUuid(TeamLabExecutionPlanV2 plan, TeamLabRouterIntentV2 router, TeamLabForwardPolicyV2 policy) => StableUuid(plan, "router-policy", $"{router.Key}:{policy.SourceCidr}:{policy.DestinationCidr}:{policy.Allow}");
    static string RouterMac(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network, TeamLabRouterIntentV2 router) =>
        $"02:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{plan.RuntimePublicId:D}:{plan.Generation}:{router.Key}:{network.Key}")))[..10].ToLowerInvariant().Insert(2, ":").Insert(5, ":").Insert(8, ":").Insert(11, ":")}";
    static string DhcpServerMac(TeamLabExecutionPlanV2 plan, TeamLabNetworkIntentV2 network) =>
        $"02:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{plan.RuntimePublicId:D}:{plan.Generation}:dhcp:{network.Key}")))[..10].ToLowerInvariant().Insert(2, ":").Insert(5, ":").Insert(8, ":").Insert(11, ":")}";

    static JsonArray Where(string column, string value) =>
        new() { new JsonArray { column, "==", value } };

    static string Trim(string value) => value.Length <= 512 ? value : value[..512];

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

    static string StableUuid(TeamLabExecutionPlanV2 plan, string kind, string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"gzctf:teamlab:{plan.RuntimePublicId:D}:{plan.Generation}:{kind}:{key}"));
        return $"gzctf_{kind.Replace('-', '_')}_{new Guid(bytes[..16]):N}";
    }
}

public sealed record TeamLabOvnApplyResult(
    bool Success,
    bool AlreadyApplied,
    string Stage,
    string Message)
{
    public static TeamLabOvnApplyResult Failed(string stage, string message) =>
        new(false, false, stage, message);
}
