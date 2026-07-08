using System.Text.Json.Serialization;
using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.TeamLab;

public sealed record TeamLabCaptureStartModel(
    string? NetworkTopologyKey,
    int? ShardId,
    int MaxSeconds,
    long MaxBytes,
    int RetentionSeconds);

public sealed record TeamLabTrafficCaptureResult(
    bool Success,
    string Message,
    [property: JsonIgnore] TeamLabTrafficCaptureJob? Job)
{
    [JsonPropertyName("job")]
    public TeamLabTrafficCaptureJobModel? JobModel => Job is null ? null : TeamLabTrafficCaptureJobModel.FromJob(Job);
}

public sealed record TeamLabTrafficCaptureDownloadResult(
    bool Success,
    string Message,
    Stream? Stream,
    string FileName,
    string ContentType,
    long? Length,
    IDisposable? Owner);

public sealed record TeamLabTrafficCaptureJobModel(
    [property: JsonPropertyName("id")]
    int Id,
    [property: JsonPropertyName("runtimeId")]
    int RuntimeId,
    [property: JsonPropertyName("shardId")]
    int? ShardId,
    [property: JsonPropertyName("networkId")]
    int? NetworkId,
    [property: JsonPropertyName("workerNodeId")]
    Guid? WorkerNodeId,
    [property: JsonPropertyName("status")]
    TeamLabTrafficCaptureStatus Status,
    [property: JsonPropertyName("scope")]
    string Scope,
    [property: JsonPropertyName("filePath")]
    string? FilePath,
    [property: JsonPropertyName("maxBytes")]
    long MaxBytes,
    [property: JsonPropertyName("maxSeconds")]
    int MaxSeconds,
    [property: JsonPropertyName("capturedBytes")]
    long CapturedBytes,
    [property: JsonPropertyName("lastError")]
    string? LastError,
    [property: JsonPropertyName("createdAt")]
    DateTimeOffset CreatedAt,
    [property: JsonPropertyName("startedAt")]
    DateTimeOffset? StartedAt,
    [property: JsonPropertyName("completedAt")]
    DateTimeOffset? CompletedAt,
    [property: JsonPropertyName("expiresAt")]
    DateTimeOffset? ExpiresAt)
{
    public static TeamLabTrafficCaptureJobModel FromJob(TeamLabTrafficCaptureJob job) => new(
        job.Id,
        job.RuntimeId,
        job.ShardId,
        job.NetworkId,
        job.WorkerNodeId,
        job.Status,
        job.Scope,
        job.FilePath,
        job.MaxBytes,
        job.MaxSeconds,
        job.CapturedBytes,
        job.LastError,
        job.CreatedAt,
        job.StartedAt,
        job.CompletedAt,
        job.ExpiresAt);
}

public class TeamLabTrafficCaptureService(
    AppDbContext context,
    AgentClient agentClient,
    ILogger<TeamLabTrafficCaptureService> logger)
{
    private const int DefaultRetentionSeconds = 86400;
    private const int MaxCaptureSeconds = 86400;
    private const long MaxCaptureBytes = 10L * 1024 * 1024 * 1024;

    public async Task<TeamLabTrafficCaptureResult> StartCaptureAsync(int gameId, int teamId,
        TeamLabCaptureStartModel model, CancellationToken token)
    {
        var runtime = await LoadRuntimeAsync(gameId, teamId, token);
        if (runtime is null)
            return new TeamLabTrafficCaptureResult(false, "TeamLab runtime was not found.", null);

        if (runtime.Status != TeamLabRuntimeStatus.Running)
            return new TeamLabTrafficCaptureResult(false, "TeamLab runtime is not running.", null);

        var validation = ValidateStartModel(model);
        if (validation is not null)
            return new TeamLabTrafficCaptureResult(false, validation, null);

        var target = ResolveCaptureTarget(runtime, model);
        if (!target.Success)
            return new TeamLabTrafficCaptureResult(false, target.Message, null);

        var now = DateTimeOffset.UtcNow;
        var job = new TeamLabTrafficCaptureJob
        {
            Runtime = runtime,
            RuntimeId = runtime.Id,
            ShardId = target.ShardId,
            NetworkId = target.NetworkId,
            WorkerNodeId = target.WorkerNodeId,
            Scope = target.Scope,
            Status = TeamLabTrafficCaptureStatus.Pending,
            MaxSeconds = model.MaxSeconds,
            MaxBytes = model.MaxBytes,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(Math.Max(1, model.RetentionSeconds == 0
                ? DefaultRetentionSeconds
                : model.RetentionSeconds))
        };
        runtime.TrafficCaptureJobs.Add(job);
        AddEvent(runtime, TeamLabEventLevel.Info, $"Starting TeamLab capture: {job.Scope}.");
        await context.SaveChangesAsync(token);
        logger.LogInformation("Starting TeamLab capture job {JobId} for runtime {RuntimeId} on node {NodeId}.",
            job.Id, runtime.Id, target.WorkerNodeId);

        var response = await agentClient.StartTeamLabCaptureAsync(target.WorkerNodeId!.Value,
            new TeamLabCaptureStartRequest(runtime.Id, job.Id, job.Scope, target.InterfaceName,
                job.MaxSeconds, job.MaxBytes, false), token);
        if (response is not { Success: true })
        {
            job.Status = TeamLabTrafficCaptureStatus.Failed;
            job.LastError = NormalizeMessage(response?.Message ?? "Agent capture start failed.");
            job.CompletedAt = DateTimeOffset.UtcNow;
            AddEvent(runtime, TeamLabEventLevel.Error, $"TeamLab capture failed: {job.LastError}");
            logger.LogWarning("TeamLab capture job {JobId} failed to start: {Message}.", job.Id, job.LastError);
            await context.SaveChangesAsync(token);
            return new TeamLabTrafficCaptureResult(false, job.LastError, job);
        }

        job.Status = TeamLabTrafficCaptureStatus.Running;
        job.FilePath = response.FilePath;
        job.CapturedBytes = response.CapturedBytes;
        job.StartedAt = DateTimeOffset.UtcNow;
        job.LastError = null;
        AddEvent(runtime, TeamLabEventLevel.Success, $"TeamLab capture started: {job.Scope}.");
        logger.LogInformation("TeamLab capture job {JobId} started on node {NodeId}.", job.Id, job.WorkerNodeId);
        await context.SaveChangesAsync(token);
        return new TeamLabTrafficCaptureResult(true, "TeamLab capture started.", job);
    }

    public async Task<TeamLabTrafficCaptureResult> StopCaptureAsync(int gameId, int teamId, int jobId,
        CancellationToken token)
    {
        var job = await LoadJobAsync(gameId, teamId, jobId, token);
        if (job is null)
            return new TeamLabTrafficCaptureResult(false, "TeamLab capture job was not found.", null);

        if (job.WorkerNodeId is null)
            return await MarkJobFailedAsync(job, "TeamLab capture job has no WorkerNode.", token);

        job.Status = TeamLabTrafficCaptureStatus.Stopping;
        await context.SaveChangesAsync(token);

        var response = await agentClient.StopTeamLabCaptureAsync(job.WorkerNodeId.Value,
            new TeamLabCaptureStopRequest(job.RuntimeId, job.Id, false), token);
        if (response is not { Success: true })
            return await MarkJobFailedAsync(job, response?.Message ?? "Agent capture stop failed.", token);

        job.Status = TeamLabTrafficCaptureStatus.Completed;
        job.FilePath = response.FilePath ?? job.FilePath;
        job.CapturedBytes = response.CapturedBytes;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.LastError = null;
        AddEvent(job.Runtime, TeamLabEventLevel.Success, $"TeamLab capture completed: {job.Scope}.");
        logger.LogInformation("TeamLab capture job {JobId} completed with {CapturedBytes} bytes.",
            job.Id, job.CapturedBytes);
        await context.SaveChangesAsync(token);
        return new TeamLabTrafficCaptureResult(true, "TeamLab capture stopped.", job);
    }

    public async Task<TeamLabTrafficCaptureResult> RefreshCaptureStatusAsync(int gameId, int teamId, int jobId,
        CancellationToken token)
    {
        var job = await LoadJobAsync(gameId, teamId, jobId, token);
        if (job is null)
            return new TeamLabTrafficCaptureResult(false, "TeamLab capture job was not found.", null);

        if (job.Status is TeamLabTrafficCaptureStatus.Completed or TeamLabTrafficCaptureStatus.Failed
            or TeamLabTrafficCaptureStatus.Expired)
            return new TeamLabTrafficCaptureResult(true, "TeamLab capture job is already closed.", job);

        if (job.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            job.Status = TeamLabTrafficCaptureStatus.Expired;
            job.CompletedAt = DateTimeOffset.UtcNow;
            AddEvent(job.Runtime, TeamLabEventLevel.Warning, $"TeamLab capture expired: {job.Scope}.");
            await context.SaveChangesAsync(token);
            return new TeamLabTrafficCaptureResult(true, "TeamLab capture expired.", job);
        }

        if (job.WorkerNodeId is null)
            return await MarkJobFailedAsync(job, "TeamLab capture job has no WorkerNode.", token);

        var response = await agentClient.GetTeamLabCaptureStatusAsync(job.WorkerNodeId.Value,
            new TeamLabCaptureStatusRequest(job.RuntimeId, job.Id, false), token);
        if (response is not { Success: true })
            return await MarkJobFailedAsync(job, response?.Message ?? "Agent capture status failed.", token);

        job.FilePath = response.FilePath ?? job.FilePath;
        job.CapturedBytes = response.CapturedBytes;
        await context.SaveChangesAsync(token);
        return new TeamLabTrafficCaptureResult(true, "TeamLab capture status refreshed.", job);
    }

    public async Task<IReadOnlyList<TeamLabTrafficCaptureJobModel>> ListJobsAsync(int gameId, int teamId,
        CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);
        if (runtime is null)
            return [];

        return await context.TeamLabTrafficCaptureJobs.AsNoTracking()
            .Where(job => job.RuntimeId == runtime.Id)
            .OrderByDescending(job => job.CreatedAt)
            .Take(100)
            .Select(job => TeamLabTrafficCaptureJobModel.FromJob(job))
            .ToListAsync(token);
    }

    public async Task<TeamLabTrafficCaptureDownloadResult> DownloadCaptureAsync(int gameId, int teamId, int jobId,
        CancellationToken token)
    {
        var job = await LoadJobAsync(gameId, teamId, jobId, token);
        if (job is null)
            return new TeamLabTrafficCaptureDownloadResult(false, "TeamLab capture job was not found.", null,
                string.Empty, "application/octet-stream", null, null);

        if (job.WorkerNodeId is null)
            return new TeamLabTrafficCaptureDownloadResult(false, "TeamLab capture job has no WorkerNode.", null,
                string.Empty, "application/octet-stream", null, null);

        var response = await agentClient.DownloadTeamLabCaptureAsync(job.WorkerNodeId.Value, job.RuntimeId, job.Id,
            token);
        if (response is not { Success: true, Stream: not null })
            return new TeamLabTrafficCaptureDownloadResult(false,
                response?.Message ?? "Agent capture download failed.", null, string.Empty,
                "application/octet-stream", null, response?.Owner);

        var fileName = $"teamlab-game-{gameId}-team-{teamId}-capture-{job.Id}.pcap";
        return new TeamLabTrafficCaptureDownloadResult(true, string.Empty, response.Stream, fileName,
            response.ContentType, response.Length, response.Owner);
    }

    private async Task<TeamLabRuntime?> LoadRuntimeAsync(int gameId, int teamId, CancellationToken token) =>
        await context.TeamLabRuntimes
            .Include(r => r.Shards)
            .Include(r => r.Networks)
            .Include(r => r.TrafficCaptureJobs)
            .Include(r => r.Events)
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);

    private async Task<TeamLabTrafficCaptureJob?> LoadJobAsync(int gameId, int teamId, int jobId,
        CancellationToken token) =>
        await context.TeamLabTrafficCaptureJobs
            .Include(job => job.Runtime).ThenInclude(runtime => runtime.Events)
            .FirstOrDefaultAsync(job => job.Id == jobId &&
                                        job.Runtime.GameId == gameId &&
                                        job.Runtime.TeamId == teamId, token);

    private static string? ValidateStartModel(TeamLabCaptureStartModel model)
    {
        if (model.MaxSeconds is <= 0 or > MaxCaptureSeconds)
            return "Invalid TeamLab capture duration.";

        if (model.MaxBytes is <= 0 or > MaxCaptureBytes)
            return "Invalid TeamLab capture size limit.";

        if (string.IsNullOrWhiteSpace(model.NetworkTopologyKey) && model.ShardId is null)
            return "TeamLab capture requires a network or shard target.";

        return null;
    }

    private static CaptureTarget ResolveCaptureTarget(TeamLabRuntime runtime, TeamLabCaptureStartModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.NetworkTopologyKey))
        {
            var network = runtime.Networks.FirstOrDefault(n =>
                string.Equals(n.TopologyKey, model.NetworkTopologyKey, StringComparison.Ordinal));
            if (network is null)
                return CaptureTarget.Failed($"TeamLab network {model.NetworkTopologyKey} was not found.");

            if (network.WorkerNodeId is null)
                return CaptureTarget.Failed("TeamLab network has no WorkerNode.");

            return CaptureTarget.Succeeded(
                network.WorkerNodeId.Value,
                network.ShardId,
                network.Id == 0 ? null : network.Id,
                $"network:{network.TopologyKey}",
                network.BridgeName);
        }

        var shard = runtime.Shards.FirstOrDefault(s => s.Id == model.ShardId);
        if (shard is null)
            return CaptureTarget.Failed($"TeamLab shard {model.ShardId} was not found.");

        return CaptureTarget.Succeeded(
            shard.WorkerNodeId,
            shard.Id,
            null,
            $"shard:{shard.Id}",
            TeamLabDeploymentService.BuildResourceNames(runtime.Id,
                shard.Networks.Select(network => network.TopologyKey).ToArray()).RouterNamespace);
    }

    private async Task<TeamLabTrafficCaptureResult> MarkJobFailedAsync(TeamLabTrafficCaptureJob job,
        string message, CancellationToken token)
    {
        job.Status = TeamLabTrafficCaptureStatus.Failed;
        job.LastError = NormalizeMessage(message);
        job.CompletedAt = DateTimeOffset.UtcNow;
        AddEvent(job.Runtime, TeamLabEventLevel.Error, $"TeamLab capture failed: {job.LastError}");
        await context.SaveChangesAsync(token);
        return new TeamLabTrafficCaptureResult(false, job.LastError, job);
    }

    private static void AddEvent(TeamLabRuntime runtime, TeamLabEventLevel level, string message) =>
        runtime.Events.Add(TeamLabDeploymentService.BuildRuntimeEvent("capture", level, message));

    private static string NormalizeMessage(string message) => TeamLabDeploymentService.NormalizeRuntimeError(message);

    private sealed record CaptureTarget(
        bool Success,
        string Message,
        Guid? WorkerNodeId,
        int? ShardId,
        int? NetworkId,
        string Scope,
        string InterfaceName)
    {
        public static CaptureTarget Succeeded(Guid workerNodeId, int? shardId, int? networkId, string scope,
            string interfaceName) =>
            new(true, string.Empty, workerNodeId, shardId, networkId, scope, interfaceName);

        public static CaptureTarget Failed(string message) =>
            new(false, message, null, null, null, string.Empty, string.Empty);
    }
}
