using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabDeploymentResult(bool Success, string Message, TeamLabRuntime? Runtime);
public sealed record TeamLabResourceNames(string[] Bridges, string RouterNamespace, string WireGuardInterface);

public class TeamLabDeploymentService(
    AppDbContext context,
    TeamLabPlanService planService,
    AgentClient agentClient,
    IPublicUdpGatewayProvider publicUdpGatewayProvider,
    IOptions<TeamLabNetworkConfig> options,
    ILogger<TeamLabDeploymentService> logger)
{
    private readonly TeamLabNetworkConfig _config = options.Value;

    public static TeamLabResourceNames BuildResourceNames(int runtimeId, IReadOnlyList<string> networkKeys)
    {
        var prefix = TeamLabPlanService.BuildRuntimeResourcePrefix(runtimeId);
        var bridges = networkKeys.Select(key => TrimLinuxName($"{prefix}-{key}")).ToArray();
        return new TeamLabResourceNames(
            bridges,
            TrimLinuxName($"tlr{runtimeId}"),
            TrimLinuxName($"tlwg{runtimeId}"));
    }

    public async Task<TeamLabDeploymentResult> DeployRuntimeAsync(int gameId, int teamId, CancellationToken token)
    {
        var planned = await planService.PlanRuntimeAsync(gameId, teamId, token);
        if (!planned.Success || planned.Runtime is null)
            return new TeamLabDeploymentResult(false, planned.Message, planned.Runtime);

        var runtime = await LoadRuntimeAsync(gameId, teamId, token);
        if (runtime is null)
            return new TeamLabDeploymentResult(false, "TeamLab runtime was not found after planning.", null);

        if (!TeamLabStateMachine.CanTransition(runtime.Status, TeamLabRuntimeStatus.Deploying))
            return new TeamLabDeploymentResult(false, $"Cannot deploy TeamLab runtime from status {runtime.Status}.", runtime);

        if (runtime.WorkerNodeId is null || runtime.WorkerNode is null || runtime.PublicUdpMapping is null)
            return await FailAsync(runtime, "TeamLab runtime has no planned WorkerNode or UDP mapping.", token);

        runtime.Status = TeamLabRuntimeStatus.Deploying;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "deploy", TeamLabEventLevel.Info, "Starting TeamLab dry-run deployment.");
        await context.SaveChangesAsync(token);

        var names = BuildResourceNames(runtime.Id, ["entry", "lab"]);
        var entryCidr = runtime.NetworkPrefix;
        var labCidr = BuildAdjacentCidr(runtime.NetworkPrefix);

        var entryGateway = FirstHost(entryCidr);
        var labGateway = FirstHost(labCidr);
        if (string.IsNullOrWhiteSpace(entryGateway) || string.IsNullOrWhiteSpace(labGateway))
            return await FailAsync(runtime, "TeamLab runtime network prefix is invalid.", token);

        var bridge1 = await agentClient.CreateTeamLabBridgeAsync(runtime.WorkerNodeId.Value,
            new TeamLabBridgeRequest(runtime.Id, names.Bridges[0], entryCidr, entryGateway, _config.DryRun), token);
        if (bridge1 is not { Success: true })
            return await FailAsync(runtime, bridge1?.Message ?? "Failed to create TeamLab entry bridge.", token);

        var bridge2 = await agentClient.CreateTeamLabBridgeAsync(runtime.WorkerNodeId.Value,
            new TeamLabBridgeRequest(runtime.Id, names.Bridges[1], labCidr, labGateway, _config.DryRun), token);
        if (bridge2 is not { Success: true })
            return await FailAsync(runtime, bridge2?.Message ?? "Failed to create TeamLab lab bridge.", token);

        var router = await agentClient.CreateTeamLabRouterAsync(runtime.WorkerNodeId.Value,
            new TeamLabRouterRequest(runtime.Id, names.RouterNamespace, names.Bridges, _config.DryRun), token);
        if (router is not { Success: true })
            return await FailAsync(runtime, router?.Message ?? "Failed to create TeamLab router namespace.", token);

        var wg = await agentClient.ConfigureTeamLabWireGuardAsync(runtime.WorkerNodeId.Value,
            new TeamLabWireGuardRequest(runtime.Id, names.WireGuardInterface,
                runtime.PublicUdpMapping.WorkerWireGuardPort,
                $"{runtime.WorkerNode.TeamLabTunnelIp}/32",
                "dry-run-peer-key",
                FirstClientAddress(entryCidr),
                _config.DryRun), token);
        if (wg is not { Success: true })
            return await FailAsync(runtime, wg?.Message ?? "Failed to configure TeamLab WireGuard endpoint.", token);

        var gateway = await publicUdpGatewayProvider.SyncMappingAsync(runtime.PublicUdpMapping, token);
        if (!gateway.Success)
            return await FailAsync(runtime, gateway.Message, token);

        runtime.Status = TeamLabRuntimeStatus.Probing;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "probe", TeamLabEventLevel.Info, "TeamLab deployment command plan generated; probe skipped in dry-run phase.");

        if (!TeamLabStateMachine.CanTransition(runtime.Status, TeamLabRuntimeStatus.Running))
            return await FailAsync(runtime, "Invalid TeamLab runtime transition to Running.", token);

        runtime.Status = TeamLabRuntimeStatus.Running;
        runtime.IsOpenToPlayers = true;
        runtime.LastError = null;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "deploy", TeamLabEventLevel.Success, "TeamLab runtime deployment reached dry-run running state.");
        await context.SaveChangesAsync(token);

        logger.LogInformation("TeamLab runtime {RuntimeId} reached Running dry-run state.", runtime.Id);
        return new TeamLabDeploymentResult(true, "TeamLab runtime deployed in dry-run mode.", runtime);
    }

    public async Task<TeamLabDeploymentResult> DestroyRuntimeAsync(int gameId, int teamId, CancellationToken token)
    {
        var runtime = await LoadRuntimeAsync(gameId, teamId, token);
        if (runtime is null)
            return new TeamLabDeploymentResult(false, "TeamLab runtime was not found.", null);

        if (!TeamLabStateMachine.CanTransition(runtime.Status, TeamLabRuntimeStatus.Destroying))
            return new TeamLabDeploymentResult(false, $"Cannot destroy TeamLab runtime from status {runtime.Status}.", runtime);

        runtime.Status = TeamLabRuntimeStatus.Destroying;
        runtime.IsOpenToPlayers = false;
        AddEvent(runtime, "destroy", TeamLabEventLevel.Info, "Destroying TeamLab runtime resources.");
        await context.SaveChangesAsync(token);

        if (runtime.WorkerNodeId.HasValue)
        {
            var names = BuildResourceNames(runtime.Id, ["entry", "lab"]);
            var resourceNames = names.Bridges.Append(names.RouterNamespace).Append(names.WireGuardInterface).ToArray();
            await agentClient.CleanupTeamLabAsync(runtime.WorkerNodeId.Value,
                new TeamLabCleanupRequest(runtime.Id, resourceNames, _config.DryRun), token);
        }

        if (runtime.PublicUdpMapping is not null)
            await publicUdpGatewayProvider.RemoveMappingAsync(runtime.PublicUdpMapping, token);

        runtime.Status = TeamLabRuntimeStatus.Destroyed;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "destroy", TeamLabEventLevel.Success, "TeamLab runtime destroyed.");
        await context.SaveChangesAsync(token);

        return new TeamLabDeploymentResult(true, "TeamLab runtime destroyed.", runtime);
    }

    public async Task<IReadOnlyList<TeamLabEvent>> GetEventsAsync(int gameId, int teamId, CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);
        if (runtime is null) return [];

        return await context.TeamLabEvents.AsNoTracking()
            .Where(e => e.RuntimeId == runtime.Id)
            .OrderByDescending(e => e.CreatedAt)
            .Take(100)
            .ToListAsync(token);
    }

    private async Task<TeamLabRuntime?> LoadRuntimeAsync(int gameId, int teamId, CancellationToken token) =>
        await context.TeamLabRuntimes
            .Include(r => r.WorkerNode)
            .Include(r => r.PublicUdpMapping)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);

    private async Task<TeamLabDeploymentResult> FailAsync(TeamLabRuntime runtime, string message, CancellationToken token)
    {
        runtime.Status = TeamLabRuntimeStatus.Failed;
        runtime.LastError = message;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        AddEvent(runtime, "deploy", TeamLabEventLevel.Error, message);
        await context.SaveChangesAsync(token);
        return new TeamLabDeploymentResult(false, message, runtime);
    }

    private static void AddEvent(TeamLabRuntime runtime, string stage, TeamLabEventLevel level, string message) =>
        runtime.Events.Add(new TeamLabEvent { Stage = stage, Level = level, Message = message });

    private static string TrimLinuxName(string value) => value.Length <= 15 ? value : value[..15];

    private static string BuildAdjacentCidr(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var ip))
            return cidr;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return cidr;
        bytes[2] = (byte)Math.Min(255, bytes[2] + 1);
        return $"{new System.Net.IPAddress(bytes)}/{parts[1]}";
    }

    private static string FirstHost(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var ip))
            return string.Empty;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return string.Empty;
        bytes[3] = 1;
        return new System.Net.IPAddress(bytes).ToString();
    }

    private static string FirstClientAddress(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var ip))
            return cidr;

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return cidr;
        bytes[3] = 2;
        return $"{new System.Net.IPAddress(bytes)}/32";
    }
}
