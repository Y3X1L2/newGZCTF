using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using GZCTF.Agent.Models;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

/// <summary>
/// Data-plane executor for TeamLab link policies. The declared damage is made
/// physically real on the host-side veth of the runtime asset's link (the same
/// deterministic device name the execution plan creates), using tc netem/tbf
/// and ip-link control. Recovery removes the root qdisc / restores the link.
/// </summary>
public sealed class TeamLabLinkPolicyService(
    TeamLabCommandExecutor executor,
    TeamLabCommandRunner runner,
    IOptions<AgentTeamLabConfig> options,
    ILogger<TeamLabLinkPolicyService> logger)
{
    private readonly AgentTeamLabConfig _config = options.Value;
    private static readonly string[] SupportedKinds =
    [
        "latency", "jitter", "packet-loss", "duplication", "bandwidth-limit", "link-break", "access-rule", "nat"
    ];

    public async Task<TeamLabLinkPolicyResponse> ApplyAsync(
        TeamLabLinkPolicyApplyRequest request,
        CancellationToken token)
    {
        if (request.RuntimePublicId == Guid.Empty || request.Generation <= 0)
            return Fail("validate", "Runtime identity is invalid.", "", "");
        if (string.IsNullOrWhiteSpace(request.NetworkKey) || string.IsNullOrWhiteSpace(request.AssetKey))
            return Fail("validate", "Network key and asset key are required to resolve the managed link.", "", "");
        if (!SupportedKinds.Contains(request.Kind, StringComparer.Ordinal))
            return Fail("unsupported", $"Link policy kind '{request.Kind}' is not realized by the Agent executor.", "", "");
        if (string.Equals(request.Kind, "nat", StringComparison.Ordinal))
            return await ApplyNatAsync(request, token);

        var iface = TeamLabExecutionIdentityV2.WorkloadHostInterface(
            request.RuntimePublicId, request.Generation, request.AssetKey, request.NetworkKey);
        var commands = BuildCommands(iface, request.Kind, request.ParametersJson, out var error);
        if (error is not null)
            return Fail("validate", error, iface, "");

        if (request.DryRun)
            return new TeamLabLinkPolicyResponse(true, true, iface, string.Join(" && ", commands),
                "Command plan returned without execution.");

        var preflight = await RunProbeAsync(
            $"ip link show {TeamLabNetworkPrimitives.ShellQuote(iface)} >/dev/null 2>&1", token);
        if (!preflight)
            return Fail("link_not_found",
                $"Managed link '{iface}' does not exist on this WorkerNode; is the runtime deployed here?",
                iface, "");

        var response = await executor.ExecuteAsync(commands, requestDryRun: false, token);
        var state = await ReadStateAsync(iface, token);
        logger.LogInformation("Link policy {Kind} applied on {Interface}: {Message}",
            request.Kind, iface, response.Message);
        return new TeamLabLinkPolicyResponse(response.Success, false, iface, state, response.Message);
    }

    /// <summary>
    /// NAT is realized on the OVN logical router (the data plane that actually
    /// routes between runtime networks). The Agent ensures the shared logical
    /// router has a gateway chassis (so OVN instantiates the dnat_and_snat
    /// flows), then adds/removes the runtime-scoped NAT row via ovn-nbctl.
    /// </summary>
    private async Task<TeamLabLinkPolicyResponse> ApplyNatAsync(
        TeamLabLinkPolicyApplyRequest request,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.NetworkCidr) || string.IsNullOrWhiteSpace(request.GatewayIp))
            return Fail("validate", "NAT requires the runtime network cidr and gateway ip.", "", "");
        var (nb, lr, chassis) = await ResolveOvnNatContextAsync(request.RuntimePublicId, request.Generation, request.NetworkCidr, request.GatewayIp, request.NetworkDigest, token);
        if (lr is null || chassis is null)
            return Fail("link_not_found", "Cannot uniquely identify the runtime OVN router and local chassis; no NAT rule was changed.", "", "");
        var commands = BuildNatCommands(
            nb,
            lr,
            chassis,
            request.NetworkCidr,
            request.GatewayIp,
            request.ParametersJson,
            out var error, request);
        if (error is not null)
            return Fail("validate", error, lr, "");

        if (request.DryRun)
            return new TeamLabLinkPolicyResponse(true, true, lr,
                string.Join(" && ", commands), "Command plan returned without execution.");

        var response = await executor.ExecuteAsync(commands, requestDryRun: false, token);
        var state = await ReadNatStateAsync(nb, lr, token);
        logger.LogInformation("Link policy {Kind} applied on OVN router {Router}: {Message}",
            request.Kind, lr, response.Message);
        return new TeamLabLinkPolicyResponse(response.Success, false, lr, state, response.Message);
    }

    /// <summary>
    /// Resolves the OVN NB db address, the (shared) logical router name and the
    /// local chassis system-id for centralized NAT.
    /// </summary>
    private async Task<(string Nb, string? Router, string? Chassis)> ResolveOvnNatContextAsync(
        Guid runtimeId, int generation, string? networkCidr, string? gatewayIp, string? networkDigest,
        CancellationToken token)
    {
        var nb = _config.OvnNorthboundEndpoint;
        if (string.IsNullOrWhiteSpace(nb) || !IPNetwork.TryParse(networkCidr, out var network) || !Ipv4(gatewayIp) ||
            networkDigest is null || networkDigest.Length != 71 || !networkDigest.StartsWith("sha256:", StringComparison.Ordinal) || networkDigest[7..].Any(character => !Uri.IsHexDigit(character)))
            return (string.Empty, null, null);
        var digestCondition = TeamLabNetworkPrimitives.ShellQuote($"external_ids:gzctf-network-digest={networkDigest}");
        var addressCondition = TeamLabNetworkPrimitives.ShellQuote($"networks{{>=}}{gatewayIp}/{network.PrefixLength}");
        var (portOk, portOutput) = await runner.RunAsync(
            $"ovn-nbctl --timeout=15 --db={TeamLabNetworkPrimitives.ShellQuote(nb)} --data=bare --no-heading --columns=_uuid find Logical_Router_Port external_ids:gzctf-runtime={runtimeId:D} external_ids:gzctf-generation={generation} {addressCondition} {digestCondition}", token);
        var portId = portOk ? UniqueIdentifier(portOutput) : null;
        if (portId is null) return (nb, null, null);
        var (lrOk, lrOut) = await runner.RunAsync(
            BuildRouterLookupCommand(nb, runtimeId, generation) + " " + TeamLabNetworkPrimitives.ShellQuote($"ports{{>=}}{portId}") + " " + digestCondition, token);
        var lr = lrOk ? UniqueIdentifier(lrOut) : null;
        if (lr is null) return (nb, null, null);
        var (chassisOk, chassisOut) = await runner.RunAsync(
            "ovs-vsctl --timeout=15 get Open_vSwitch . external_ids:system-id", token);
        var chassis = chassisOk ? UniqueIdentifier(chassisOut) : null;
        return (nb, lr, chassis);
    }

    internal static string BuildRouterLookupCommand(string nb, Guid runtimeId, int generation) =>
        $"ovn-nbctl --timeout=15 --db={TeamLabNetworkPrimitives.ShellQuote(nb)} --data=bare --no-heading --columns=name find Logical_Router external_ids:gzctf-runtime={runtimeId:D} external_ids:gzctf-generation={generation}";

    internal static string? UniqueIdentifier(string output)
    {
        var values = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length != 1) return null;
        var value = values[0].Trim('"');
        return value.Length is > 0 and <= 128 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            ? value : null;
    }

    public async Task<TeamLabLinkPolicyResponse> RecoverAsync(
        TeamLabLinkPolicyRecoverRequest request,
        CancellationToken token)
    {
        if (request.RuntimePublicId == Guid.Empty || request.Generation <= 0)
            return Fail("validate", "Runtime identity is invalid.", "", "");
        if (string.IsNullOrWhiteSpace(request.NetworkKey) || string.IsNullOrWhiteSpace(request.AssetKey))
            return Fail("validate", "Network key and asset key are required to resolve the managed link.", "", "");

        var iface = TeamLabExecutionIdentityV2.WorkloadHostInterface(
            request.RuntimePublicId, request.Generation, request.AssetKey, request.NetworkKey);
        string[] commands;
        if (string.Equals(request.Kind, "nat", StringComparison.Ordinal))
        {
            var (nb, lr, _) = await ResolveOvnNatContextAsync(request.RuntimePublicId, request.Generation, request.NetworkCidr, request.GatewayIp, request.NetworkDigest, token);
            if (lr is null)
                return Fail("link_not_found", "No OVN logical router is available for NAT recovery.", "", "");
            commands = BuildNatRecoverCommands(nb, lr, request.ParametersJson, request.GatewayIp, out var natError, request.NetworkCidr);
            if (natError is not null)
                return Fail("validate", natError, lr, "");
        }
        else if (string.Equals(request.Kind, "access-rule", StringComparison.Ordinal))
        {
            commands =
            [
                $"tc qdisc del dev {TeamLabNetworkPrimitives.ShellQuote(iface)} clsact 2>/dev/null || true",
                $"ip link show {TeamLabNetworkPrimitives.ShellQuote(iface)} >/dev/null 2>&1"
            ];
        }
        else
        {
            commands =
            [
                $"tc qdisc del dev {TeamLabNetworkPrimitives.ShellQuote(iface)} root 2>/dev/null || true",
                $"ip link set {TeamLabNetworkPrimitives.ShellQuote(iface)} up 2>/dev/null || true",
                $"ip link show {TeamLabNetworkPrimitives.ShellQuote(iface)} >/dev/null 2>&1"
            ];
        }
        if (request.DryRun)
            return new TeamLabLinkPolicyResponse(true, true, iface, string.Join(" && ", commands),
                "Command plan returned without execution.");

        var response = await executor.ExecuteAsync(commands, requestDryRun: false, token);
        var state = await ReadStateAsync(iface, token);
        return new TeamLabLinkPolicyResponse(response.Success, false, iface, state, response.Message);
    }

    internal static string[] BuildNatCommands(
        string nb,
        string router,
        string? chassis,
        string networkCidr,
        string gatewayIp,
        string parametersJson,
        out string? error,
        TeamLabLinkPolicyApplyRequest? owner = null)
    {
        error = null;
        var db = TeamLabNetworkPrimitives.ShellQuote(nb);
        var lr = TeamLabNetworkPrimitives.ShellQuote(router);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson);
            var root = document.RootElement;
            var mode = RequiredString(root, "mode", out error);
            if (error is not null) return [];
            // Centralized NAT requires the logical router to have a gateway chassis.
            var chassisCmd = string.IsNullOrWhiteSpace(chassis)
                ? null
                : $"ovn-nbctl --db={db} --if-exists set Logical_Router {lr} options:chassis={TeamLabNetworkPrimitives.ShellQuote(chassis)}";
            return mode switch
            {
                "snat" => BuildSnatCommands(db, lr, chassisCmd, networkCidr, root, out error),
                "dnat" => BuildDnatCommands(db, lr, chassisCmd, gatewayIp, root, out error, owner),
                _ => throw new InvalidOperationException("invalid mode")
            };
        }
        catch (JsonException)
        {
            error = "Link policy parameters are not valid JSON.";
            return [];
        }
        catch (InvalidOperationException)
        {
            error = error ?? "Link policy parameter 'mode' must be snat or dnat.";
            return [];
        }
    }

    private static string[] BuildSnatCommands(
        string db,
        string lr,
        string? chassisCmd,
        string networkCidr,
        JsonElement root,
        out string? error)
    {
        error = null;
        var address = RequiredString(root, "translatedAddress", out error);
        if (error is not null) return [];
        var commands = new List<string>();
        if (chassisCmd is not null) commands.Add(chassisCmd);
        if (!Ipv4(address) || !IPNetwork.TryParse(networkCidr, out var network) || network.BaseAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            error = "SNAT requires valid IPv4 addresses.";
            return [];
        }
        commands.Add($"ovn-nbctl --timeout=15 --db={db} --if-exists lr-nat-del {lr} snat {TeamLabNetworkPrimitives.ShellQuote(networkCidr)}");
        commands.Add($"ovn-nbctl --timeout=15 --db={db} lr-nat-add {lr} snat {TeamLabNetworkPrimitives.ShellQuote(address)} {TeamLabNetworkPrimitives.ShellQuote(networkCidr)}");
        return commands.ToArray();
    }

    private static string[] BuildDnatCommands(
        string db,
        string lr,
        string? chassisCmd,
        string gatewayIp,
        JsonElement root,
        out string? error,
        TeamLabLinkPolicyApplyRequest? owner)
    {
        error = null;
        var externalPort = Number(root, "externalPort", out error);
        if (error is not null) return [];
        var internalAddress = RequiredString(root, "internalAddress", out error);
        if (error is not null) return [];
        var internalPort = OptionalNumber(root, "internalPort", externalPort, out error);
        if (error is not null) return [];
        var externalAddress = OptionalString(root, "externalAddress") ?? gatewayIp;
        var protocol = OptionalString(root, "protocol") ?? "tcp";
        if (!Ipv4(externalAddress) || !Ipv4(internalAddress) || protocol is not ("tcp" or "udp") ||
            externalPort is < 1 or > 65535 || externalPort != Math.Floor(externalPort) ||
            internalPort is < 1 or > 65535 || internalPort != Math.Floor(internalPort))
        {
            error = "Port mappings require IPv4 addresses, TCP/UDP and integer ports between 1 and 65535.";
            return [];
        }
        var port = (int)externalPort;
        var iport = (int)internalPort;
        var lb = TeamLabNetworkPrimitives.ShellQuote(PortMappingName(lr, externalAddress, port, protocol));
        var commands = new List<string>();
        if (chassisCmd is not null) commands.Add(chassisCmd);
        // OVN NAT rows translate addresses; VIP load balancers provide protocol/port translation.
        var identity = owner is null ? string.Empty : $" -- set Load_Balancer {lb} external_ids:gzctf-runtime={owner.RuntimePublicId:D} external_ids:gzctf-generation={owner.Generation} {TeamLabNetworkPrimitives.ShellQuote($"external_ids:gzctf-network-digest={owner.NetworkDigest}")}";
        commands.Add($"ovn-nbctl --timeout=15 --db={db} --may-exist lb-add {lb} {TeamLabNetworkPrimitives.ShellQuote($"{externalAddress}:{port}")} {TeamLabNetworkPrimitives.ShellQuote($"{internalAddress}:{iport}")} {protocol} -- --may-exist lr-lb-add {lr} {lb}{identity}");
        return commands.ToArray();
    }

    private static bool Ipv4(string? value) => IPAddress.TryParse(value, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork;
    private static string PortMappingName(string router, string address, int port, string protocol) =>
        "gzctf_map_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{router}|{address}|{port}|{protocol}")))[..24];

    internal static string[] BuildNatRecoverCommands(
        string nb,
        string router,
        string? parametersJson,
        string? gatewayIp,
        out string? error,
        string? networkCidr = null)
    {
        error = null;
        var db = TeamLabNetworkPrimitives.ShellQuote(nb);
        var lr = TeamLabNetworkPrimitives.ShellQuote(router);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson);
            var root = document.RootElement;
            var mode = RequiredString(root, "mode", out error);
            if (error is not null) return [];
            if (mode == "snat")
            {
                if (!IPNetwork.TryParse(networkCidr, out _))
                {
                    error = "SNAT recovery requires the original network CIDR.";
                    return [];
                }
                return [$"ovn-nbctl --timeout=15 --db={db} --if-exists lr-nat-del {lr} snat {TeamLabNetworkPrimitives.ShellQuote(networkCidr!)}"];
            }
            var externalAddress = OptionalString(root, "externalAddress") ?? gatewayIp;
            var port = Number(root, "externalPort", out error);
            var protocol = OptionalString(root, "protocol") ?? "tcp";
            if (mode != "dnat" || !Ipv4(externalAddress) || error is not null ||
                port is < 1 or > 65535 || port != Math.Floor(port) || protocol is not ("tcp" or "udp"))
            {
                error = "NAT recovery requires an external address.";
                return [];
            }
            var lb = TeamLabNetworkPrimitives.ShellQuote(PortMappingName(lr, externalAddress!, (int)port, protocol));
            return [$"ovn-nbctl --timeout=15 --db={db} --if-exists lr-lb-del {lr} {lb} -- --if-exists lb-del {lb}"];
        }
        catch (JsonException)
        {
            error = "Link policy parameters are not valid JSON.";
            return [];
        }
    }

    private async Task<string> ReadNatStateAsync(string nb, string router, CancellationToken token)
    {
        try
        {
            var result = await executor.ExecuteAsync(
                [$"ovn-nbctl --db={TeamLabNetworkPrimitives.ShellQuote(nb)} lr-nat-list {TeamLabNetworkPrimitives.ShellQuote(router)} 2>/dev/null"],
                false,
                token);
            return result.Message;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to read NAT state for router {Router}", router);
            return string.Empty;
        }
    }

    internal static string[] BuildCommands(string iface, string kind, string parametersJson, out string? error)
    {
        error = null;
        var quoted = TeamLabNetworkPrimitives.ShellQuote(iface);
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(parametersJson) ? "{}" : parametersJson);
            var root = document.RootElement;
            switch (kind)
            {
                case "latency":
                {
                    var millis = Number(root, "delayMillis", out error);
                    if (error is not null) return [];
                    return [$"tc qdisc replace dev {quoted} root handle 1: netem delay {millis}ms"];
                }
                case "jitter":
                {
                    var millis = Number(root, "jitterMillis", out error);
                    if (error is not null) return [];
                    var baseDelay = Math.Max(1, millis / 2);
                    return [$"tc qdisc replace dev {quoted} root handle 1: netem delay {baseDelay}ms {millis}ms"];
                }
                case "packet-loss":
                {
                    var percent = Number(root, "lossPercent", out error);
                    if (error is not null) return [];
                    return [$"tc qdisc replace dev {quoted} root handle 1: netem loss {percent}%"];
                }
                case "duplication":
                {
                    var percent = Number(root, "duplicatePercent", out error);
                    if (error is not null) return [];
                    return [$"tc qdisc replace dev {quoted} root handle 1: netem duplicate {percent}%"];
                }
                case "bandwidth-limit":
                {
                    var rate = Number(root, "rateMbps", out error);
                    if (error is not null) return [];
                    var burst = root.TryGetProperty("burstKilobytes", out var burstElement) &&
                                burstElement.ValueKind == JsonValueKind.Number
                        ? burstElement.GetDouble()
                        : Math.Max(16, rate / 2);
                    return [$"tc qdisc replace dev {quoted} root handle 1: tbf rate {rate}mbit burst {burst}kbit latency 200ms"];
                }
                case "link-break":
                    return [$"ip link set {quoted} down"];
                case "access-rule":
                {
                    // Access rules are enforced on the same host-side veth the netem policies
                    // use, via tc clsact (ingress/egress) u32 filters. This is in the packet
                    // path for OVS-managed veths (same as tc qdisc netem, which is verified to
                    // affect fast-path traffic), unlike host netfilter which the OVS kernel
                    // datapath bypasses. Direction is relative to the runtime asset:
                    //   inbound  -> egress qdisc (packets entering the asset from the host)
                    //   outbound -> ingress qdisc (packets leaving the asset into the host)
                    var direction = RequiredString(root, "direction", out error);
                    if (error is not null) return [];
                    var action = RequiredString(root, "action", out error);
                    if (error is not null) return [];
                    if (action is not ("allow" or "deny"))
                    {
                        error = "Link policy parameter 'action' must be 'allow' or 'deny'.";
                        return [];
                    }
                    var protocol = OptionalString(root, "protocol") ?? "any";
                    var sourceCidr = OptionalString(root, "sourceCidr");
                    var destinationCidr = OptionalString(root, "destinationCidr");
                    var priority = OptionalNumber(root, "priority", 10, out error);
                    if (error is not null) return [];
                    var pref = Math.Clamp((int)priority, 0, 1000) + 1;
                    var matches = new List<string>();
                    switch (protocol)
                    {
                        case "tcp": matches.Add("match ip protocol 6 0xff"); break;
                        case "udp": matches.Add("match ip protocol 17 0xff"); break;
                        case "icmp": matches.Add("match ip protocol 1 0xff"); break;
                        case "any": break;
                        default:
                            error = "Link policy parameter 'protocol' must be tcp, udp, icmp or any.";
                            return [];
                    }
                    if (sourceCidr is not null) matches.Add($"match ip src {sourceCidr}");
                    if (destinationCidr is not null) matches.Add($"match ip dst {destinationCidr}");
                    var matchClause = string.Join(" ", matches);
                    var tcAction = action == "deny" ? "drop" : "ok";
                    var commands = new List<string> { $"tc qdisc add dev {quoted} clsact 2>/dev/null || true" };
                    if (direction is "inbound" or "both")
                        commands.Add($"tc filter add dev {quoted} egress protocol ip pref {pref} u32 {matchClause} action {tcAction}");
                    if (direction is "outbound" or "both")
                        commands.Add($"tc filter add dev {quoted} ingress protocol ip pref {pref} u32 {matchClause} action {tcAction}");
                    if (direction is not ("inbound" or "outbound" or "both"))
                    {
                        error = "Link policy parameter 'direction' must be inbound, outbound or both.";
                        return [];
                    }
                    return commands.ToArray();
                }
                default:
                    error = $"Link policy kind '{kind}' is not supported by the Agent executor.";
                    return [];
            }
        }
        catch (JsonException)
        {
            error = "Link policy parameters are not valid JSON.";
            return [];
        }
    }

    private static double Number(JsonElement root, string key, out string? error)
    {
        error = null;
        if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value))
        {
            error = $"Link policy parameter '{key}' is missing or not numeric.";
            return 0;
        }
        return value;
    }

    private static string RequiredString(JsonElement root, string key, out string? error)
    {
        error = null;
        if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            error = $"Link policy parameter '{key}' is missing or not a string.";
            return string.Empty;
        }
        return element.GetString()!;
    }

    private static string? OptionalString(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.String)
            return null;
        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static double OptionalNumber(JsonElement root, string key, double fallback, out string? error)
    {
        error = null;
        if (!root.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.Number)
            return fallback;
        if (!element.TryGetDouble(out var value))
        {
            error = $"Link policy parameter '{key}' is not numeric.";
            return fallback;
        }
        return value;
    }

    private async Task<bool> RunProbeAsync(string command, CancellationToken token)
    {
        var result = await executor.ExecuteAsync([command], requestDryRun: false, token);
        return result.Success;
    }

    private async Task<string> ReadStateAsync(string iface, CancellationToken token)
    {
        try
        {
            var result = await executor.ExecuteAsync(
                [$"tc -s qdisc show dev {TeamLabNetworkPrimitives.ShellQuote(iface)} 2>/dev/null"], false, token);
            return result.Message;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to read qdisc state for {Interface}", iface);
            return string.Empty;
        }
    }

    private static TeamLabLinkPolicyResponse Fail(string code, string message, string iface, string state) =>
        new(false, false, iface, state, $"{code}: {message}");
}
