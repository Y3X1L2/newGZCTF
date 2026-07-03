using GZCTF.Models.Data;
using GZCTF.Services.Fleet;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabNodeProbeResult(bool Success, string Message, TeamLabStatusResponse? Status);
public sealed record TeamLabNodeEnableResult(bool Success, string Message, string[] Commands);

public class NodeTunnelService(AgentClient agentClient, AppDbContext context, ILogger<NodeTunnelService> logger)
{
    public async Task<TeamLabNodeProbeResult> ProbeNodeAsync(WorkerNode node, CancellationToken token)
    {
        var status = await agentClient.GetTeamLabStatusAsync(node.Id, token);
        if (status is null)
            return new TeamLabNodeProbeResult(false, "WorkerNode TeamLab status endpoint did not respond.", null);

        node.TeamLabTunnelLastHandshake = DateTimeOffset.UtcNow;
        node.TeamLabTunnelLastError = status.Available ? null : status.Message;
        node.TeamLabTunnelStatus = status.Available ? TeamLabTunnelStatus.Probing : TeamLabTunnelStatus.Error;
        await context.SaveChangesAsync(token);

        return new TeamLabNodeProbeResult(status.Available, status.Message ?? "TeamLab dry-run probe completed.", status);
    }

    public async Task<TeamLabNodeEnableResult> EnableDryRunAsync(WorkerNode node, CancellationToken token)
    {
        var probe = await ProbeNodeAsync(node, token);
        if (!probe.Success)
            return new TeamLabNodeEnableResult(false, probe.Message, []);

        node.TeamLabNetworkEnabled = false;
        node.TeamLabTunnelStatus = TeamLabTunnelStatus.Probing;
        node.TeamLabTunnelLastError = "Dry-run only. Mark healthy with a tunnel IP after infrastructure WireGuard is validated.";
        await context.SaveChangesAsync(token);

        return new TeamLabNodeEnableResult(true, node.TeamLabTunnelLastError, []);
    }

    public async Task<TeamLabNodeEnableResult> MarkHealthyAsync(WorkerNode node, string tunnelIp, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(tunnelIp))
            return new TeamLabNodeEnableResult(false, "Tunnel IP is required before enabling TeamLab scheduling.", []);

        var probe = await ProbeNodeAsync(node, token);
        if (!probe.Success)
            return new TeamLabNodeEnableResult(false, probe.Message, []);

        node.TeamLabNetworkEnabled = true;
        node.TeamLabTunnelIp = tunnelIp.Trim();
        node.TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy;
        node.TeamLabTunnelLastError = null;
        node.TeamLabTunnelConfigVersion++;
        await context.SaveChangesAsync(token);

        logger.LogInformation("WorkerNode {NodeId} marked healthy for TeamLab scheduling with tunnel IP {TunnelIp}.",
            node.Id, node.TeamLabTunnelIp);
        return new TeamLabNodeEnableResult(true, "TeamLabNetwork scheduling enabled for this node.", []);
    }
}
