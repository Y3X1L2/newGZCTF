using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabTrafficFlowModel(
    int? ShardId,
    int? NetworkId,
    Guid? WorkerNodeId,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    long Bytes,
    DateTimeOffset CapturedAt);

public sealed record TeamLabTrafficFlowRefreshResult(bool Success, string Message, int ImportedCount);

public class TeamLabTrafficFlowService(AppDbContext context, AgentClient agentClient,
    ILogger<TeamLabTrafficFlowService> logger)
{
    private const int MaxStoredSamplesPerRuntime = 5000;

    public async Task<TeamLabTrafficFlowRefreshResult> StartCollectorsAsync(TeamLabRuntime runtime,
        IReadOnlyList<TeamLabRuntimeNetwork> networks, CancellationToken token)
    {
        var started = 0;
        foreach (var network in networks.Where(network =>
                     network.WorkerNodeId is not null && !string.IsNullOrWhiteSpace(network.BridgeName)))
        {
            var response = await agentClient.StartTeamLabFlowMetadataAsync(network.WorkerNodeId!.Value,
                new TeamLabFlowStartRequest(runtime.Id, network.ShardId, network.Id == 0 ? null : network.Id,
                    network.TopologyKey, network.BridgeName, false), token);
            if (response is not { Success: true })
            {
                var message = response?.Message ?? $"Failed to start TeamLab flow metadata for {network.TopologyKey}.";
                logger.LogWarning("TeamLab flow metadata start failed for runtime {RuntimeId}, network {Network}: {Message}",
                    runtime.Id, network.TopologyKey, message);
                return new TeamLabTrafficFlowRefreshResult(false, message, started);
            }

            started++;
        }

        if (started > 0)
            runtime.Events.Add(TeamLabDeploymentService.BuildRuntimeEvent("traffic", TeamLabEventLevel.Success,
                $"Started TeamLab flow metadata collectors for {started} network(s)."));

        return new TeamLabTrafficFlowRefreshResult(true, "TeamLab flow metadata collectors started.", started);
    }

    public async Task<TeamLabTrafficFlowRefreshResult> StopCollectorsAsync(TeamLabRuntime runtime,
        IReadOnlyList<TeamLabRuntimeNetwork> networks, CancellationToken token)
    {
        var stopped = 0;
        var errors = new List<string>();
        foreach (var network in networks.Where(network => network.WorkerNodeId is not null))
        {
            var response = await agentClient.StopTeamLabFlowMetadataAsync(network.WorkerNodeId!.Value,
                new TeamLabFlowStopRequest(runtime.Id, network.TopologyKey, false), token);
            if (response is not { Success: true })
            {
                var message = response?.Message ?? "No response";
                logger.LogWarning("TeamLab flow metadata stop failed for runtime {RuntimeId}, network {Network}: {Message}",
                    runtime.Id, network.TopologyKey, message);
                errors.Add($"{network.TopologyKey}: {message}");
            }
            else
                stopped++;
        }

        if (errors.Count > 0)
            return new TeamLabTrafficFlowRefreshResult(false,
                $"TeamLab flow metadata cleanup failed: {string.Join("; ", errors)}", stopped);

        return new TeamLabTrafficFlowRefreshResult(true, "TeamLab flow metadata collectors stopped.", stopped);
    }

    public async Task<TeamLabTrafficFlowRefreshResult> RefreshRuntimeAsync(int gameId, int teamId,
        CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes
            .Include(r => r.Networks)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);
        if (runtime is null)
            return new TeamLabTrafficFlowRefreshResult(false, "TeamLab runtime was not found.", 0);

        return await RefreshRuntimeAsync(runtime, token);
    }

    public async Task<TeamLabTrafficFlowRefreshResult> RefreshRuntimeAsync(TeamLabRuntime runtime,
        CancellationToken token)
    {
        if (runtime.Status != TeamLabRuntimeStatus.Running)
            return new TeamLabTrafficFlowRefreshResult(false, "TeamLab runtime is not running.", 0);

        var imported = 0;
        foreach (var network in runtime.Networks.Where(network => network.WorkerNodeId is not null))
        {
            var response = await agentClient.GetTeamLabFlowMetadataSnapshotAsync(network.WorkerNodeId!.Value,
                new TeamLabFlowSnapshotRequest(runtime.Id, network.TopologyKey, false), token);
            if (response is not { Success: true })
            {
                logger.LogWarning("TeamLab flow metadata snapshot failed for runtime {RuntimeId}, network {Network}: {Message}",
                    runtime.Id, network.TopologyKey, response?.Message ?? "No response");
                continue;
            }

            imported += ImportSamples(runtime, network, response.Samples);
        }

        if (imported > 0)
        {
            await TrimOldSamplesAsync(runtime.Id, token);
            runtime.Events.Add(TeamLabDeploymentService.BuildRuntimeEvent("traffic", TeamLabEventLevel.Info,
                $"Imported {imported} TeamLab flow metadata sample(s)."));
            await context.SaveChangesAsync(token);
        }

        return new TeamLabTrafficFlowRefreshResult(true, "TeamLab flow metadata refreshed.", imported);
    }

    public async Task<IReadOnlyList<TeamLabTrafficFlowModel>> GetRecentFlowsAsync(int gameId, int teamId,
        int count, CancellationToken token)
    {
        count = Math.Clamp(count, 1, 200);
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);
        if (runtime is null)
            return [];

        return await context.TeamLabTrafficFlows.AsNoTracking()
            .Where(flow => flow.RuntimeId == runtime.Id)
            .OrderByDescending(flow => flow.CapturedAt)
            .Take(count)
            .Select(flow => new TeamLabTrafficFlowModel(flow.ShardId, flow.NetworkId, flow.WorkerNodeId,
                flow.SourceIp, flow.SourcePort, flow.DestinationIp, flow.DestinationPort, flow.Protocol,
                flow.Bytes, flow.CapturedAt))
            .ToListAsync(token);
    }

    private int ImportSamples(TeamLabRuntime runtime, TeamLabRuntimeNetwork network, TeamLabFlowSample[] samples)
    {
        var imported = 0;
        foreach (var sample in samples.Where(IsUsableSample))
        {
            var exists = context.TeamLabTrafficFlows.Any(flow =>
                flow.RuntimeId == runtime.Id &&
                flow.NetworkId == network.Id &&
                flow.CapturedAt == sample.CapturedAt &&
                flow.SourceIp == sample.SourceIp &&
                flow.SourcePort == sample.SourcePort &&
                flow.DestinationIp == sample.DestinationIp &&
                flow.DestinationPort == sample.DestinationPort &&
                flow.Protocol == sample.Protocol &&
                flow.Bytes == sample.Bytes);
            if (exists)
                continue;

            context.TeamLabTrafficFlows.Add(new TeamLabTrafficFlow
            {
                RuntimeId = runtime.Id,
                ShardId = network.ShardId,
                NetworkId = network.Id == 0 ? null : network.Id,
                WorkerNodeId = network.WorkerNodeId,
                SourceIp = sample.SourceIp,
                SourcePort = sample.SourcePort,
                DestinationIp = sample.DestinationIp,
                DestinationPort = sample.DestinationPort,
                Protocol = sample.Protocol,
                Bytes = Math.Max(0, sample.Bytes),
                CapturedAt = sample.CapturedAt
            });
            imported++;
        }

        return imported;
    }

    private static bool IsUsableSample(TeamLabFlowSample sample) =>
        !string.IsNullOrWhiteSpace(sample.SourceIp) &&
        !string.IsNullOrWhiteSpace(sample.DestinationIp) &&
        !string.IsNullOrWhiteSpace(sample.Protocol);

    private async Task TrimOldSamplesAsync(int runtimeId, CancellationToken token)
    {
        var idsToDelete = await context.TeamLabTrafficFlows.AsNoTracking()
            .Where(flow => flow.RuntimeId == runtimeId)
            .OrderByDescending(flow => flow.CapturedAt)
            .Skip(MaxStoredSamplesPerRuntime)
            .Select(flow => flow.Id)
            .ToArrayAsync(token);
        if (idsToDelete.Length == 0)
            return;

        await context.TeamLabTrafficFlows
            .Where(flow => idsToDelete.Contains(flow.Id))
            .ExecuteDeleteAsync(token);
    }
}
