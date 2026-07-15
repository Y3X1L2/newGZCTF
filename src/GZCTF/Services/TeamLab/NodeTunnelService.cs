using System.Globalization;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Services.Fleet;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabNodeProbeResult(bool Success, string Message, TeamLabStatusResponse? Status);
public sealed record TeamLabNodeEnableResult(bool Success, string Message, string[] Commands);

public class NodeTunnelService(
    AgentClient agentClient,
    AppDbContext context,
    IOperationalEventWriter events,
    ILogger<NodeTunnelService> logger)
{
    public async Task<TeamLabNodeProbeResult> ProbeNodeAsync(WorkerNode node, CancellationToken token)
    {
        var status = await agentClient.GetTeamLabStatusAsync(node.Id, token);
        if (status is null)
            return new TeamLabNodeProbeResult(false, "WorkerNode TeamLab status endpoint did not respond.", null);

        var previousStatus = node.TeamLabTunnelStatus;
        node.TeamLabTunnelLastHandshake = DateTimeOffset.UtcNow;
        node.TeamLabTunnelLastError = status.Available ? null : status.Message;
        node.TeamLabTunnelStatus = status.Available ? TeamLabTunnelStatus.Probing : TeamLabTunnelStatus.Error;
        if (node.TeamLabTunnelStatus == TeamLabTunnelStatus.Error && previousStatus != TeamLabTunnelStatus.Error)
            events.Append(NodeOperationalEvents.Create(
                node,
                OperationalEventCodes.Node.HealthDegraded,
                OperationalEventOutcome.Observed,
                "Worker node TeamLab network health degraded.",
                OperationalEventSeverity.Warning,
                detail: new Dictionary<string, object?>
                {
                    ["previousStatus"] = previousStatus.ToString(),
                    ["currentStatus"] = node.TeamLabTunnelStatus.ToString(),
                    ["reasonCode"] = "teamlab_probe_failed"
                }));
        await context.SaveChangesAsync(token);

        return new TeamLabNodeProbeResult(status.Available, status.Message ?? "TeamLab network probe completed.", status);
    }

    public async Task<TeamLabNodeEnableResult> EnableDryRunAsync(WorkerNode node, CancellationToken token)
    {
        var probe = await ProbeNodeAsync(node, token);
        if (!probe.Success)
            return new TeamLabNodeEnableResult(false, probe.Message, []);

        ApplyDryRunProbeResult(node);
        await context.SaveChangesAsync(token);

        return new TeamLabNodeEnableResult(true,
            node.TeamLabNetworkEnabled
                ? "TeamLab network components are healthy; scheduling remains enabled."
                : node.TeamLabTunnelLastError ?? "TeamLab network probe completed.",
            []);
    }

    public async Task<TeamLabNodeEnableResult> MarkHealthyAsync(WorkerNode node, string tunnelIp, CancellationToken token)
    {
        var normalizedTunnelIp = tunnelIp.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTunnelIp))
            return new TeamLabNodeEnableResult(false, "Tunnel IP is required before enabling TeamLab scheduling.", []);

        if (!IsValidIpv4Address(normalizedTunnelIp))
            return new TeamLabNodeEnableResult(false, "Tunnel IP must be a valid IPv4 address.", []);

        var probe = await ProbeNodeAsync(node, token);
        if (!probe.Success)
            return new TeamLabNodeEnableResult(false, probe.Message, []);

        var previousStatus = node.TeamLabTunnelStatus;
        node.TeamLabNetworkEnabled = true;
        node.TeamLabTunnelIp = normalizedTunnelIp;
        node.TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy;
        node.TeamLabTunnelLastError = null;
        node.TeamLabTunnelConfigVersion++;
        if (previousStatus != TeamLabTunnelStatus.Healthy)
            events.Append(NodeOperationalEvents.Create(
                node,
                OperationalEventCodes.Node.HealthRecovered,
                OperationalEventOutcome.Recovered,
                "Worker node TeamLab network health recovered.",
                detail: new Dictionary<string, object?>
                {
                    ["previousStatus"] = previousStatus.ToString(),
                    ["currentStatus"] = TeamLabTunnelStatus.Healthy.ToString(),
                    ["reasonCode"] = "teamlab_probe_succeeded"
                }));
        await context.SaveChangesAsync(token);

        logger.LogInformation("WorkerNode {NodeId} marked healthy for TeamLab scheduling with tunnel IP {TunnelIp}.",
            node.Id, node.TeamLabTunnelIp);
        return new TeamLabNodeEnableResult(true, "TeamLabNetwork scheduling enabled for this node.", []);
    }

    private static bool IsValidIpv4Address(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(part =>
            part.Length > 0 &&
            int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet) &&
            octet is >= 0 and <= 255);
    }

    internal static void ApplyDryRunProbeResult(WorkerNode node)
    {
        if (node.TeamLabNetworkEnabled && node.TeamLabTunnelStatus == TeamLabTunnelStatus.Healthy &&
            !string.IsNullOrWhiteSpace(node.TeamLabTunnelIp))
        {
            node.TeamLabTunnelLastError = null;
            return;
        }

        node.TeamLabNetworkEnabled = false;
        node.TeamLabTunnelStatus = TeamLabTunnelStatus.Probing;
        node.TeamLabTunnelLastError =
            "Network components are detected. Configure a tunnel IP before enabling TeamLab scheduling.";
    }
}
