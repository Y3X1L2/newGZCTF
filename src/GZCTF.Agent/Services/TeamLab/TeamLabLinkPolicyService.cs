using System.Text.Json;
using GZCTF.Agent.Models;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

/// <summary>
/// Data-plane executor for TeamLab link policies. The declared damage is made
/// physically real on the host-side veth of the runtime asset's link (the same
/// deterministic device name the execution plan creates), using tc netem/tbf
/// and ip-link control. Recovery removes the root qdisc / restores the link.
/// </summary>
public sealed class TeamLabLinkPolicyService(
    TeamLabCommandExecutor executor,
    ILogger<TeamLabLinkPolicyService> logger)
{
    private static readonly string[] SupportedKinds =
    [
        "latency", "jitter", "packet-loss", "duplication", "bandwidth-limit", "link-break"
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
            return Fail("unsupported", $"Link policy kind '{request.Kind}' is not realized by the Agent netem executor.", "", "");

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
        var commands = new[]
        {
            $"tc qdisc del dev {TeamLabNetworkPrimitives.ShellQuote(iface)} root 2>/dev/null || true",
            $"ip link set {TeamLabNetworkPrimitives.ShellQuote(iface)} up 2>/dev/null || true",
            $"ip link show {TeamLabNetworkPrimitives.ShellQuote(iface)} >/dev/null 2>&1"
        };
        if (request.DryRun)
            return new TeamLabLinkPolicyResponse(true, true, iface, string.Join(" && ", commands),
                "Command plan returned without execution.");

        var response = await executor.ExecuteAsync(commands, requestDryRun: false, token);
        var state = await ReadStateAsync(iface, token);
        return new TeamLabLinkPolicyResponse(response.Success, false, iface, state, response.Message);
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
