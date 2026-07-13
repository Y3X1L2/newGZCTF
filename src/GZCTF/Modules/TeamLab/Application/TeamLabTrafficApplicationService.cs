using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Infrastructure.Concurrency;
using Microsoft.EntityFrameworkCore;
using GZCTF.Infrastructure.Persistence.Queries;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabTrafficApplicationService(
    AppDbContext context,
    ITeamLabNodeExecutor executor,
    IDistributedLeaseProvider locks,
    ITeamLabTrafficIngestor ingestor,
    TeamLabEventRecorder eventRecorder)
{
    public async Task StartCollectorsAsync(TeamLabRuntime runtime, CancellationToken cancellationToken)
    {
        var networks = runtime.Networks.Where(item => item.Generation == runtime.Generation && item.WorkerNodeId != null).ToArray();
        var results = await Task.WhenAll(networks.Select(network => executor.StartFlowAsync(
            network.WorkerNodeId!.Value, runtime.Id, network.ShardId ?? 0, network.Id,
            network.TopologyKey, network.BridgeName, cancellationToken)));
        var failed = results.FirstOrDefault(item => !item.Success);
        if (failed is not null) throw new TeamLabRuntimeExecutionException(failed.Message);
    }

    public async Task StopCollectorsAsync(TeamLabRuntime runtime, CancellationToken cancellationToken)
    {
        var networks = runtime.Networks.Where(item => item.Generation == runtime.Generation && item.WorkerNodeId != null).ToArray();
        await Task.WhenAll(networks.Select(network => executor.StopFlowAsync(
            network.WorkerNodeId!.Value, runtime.Id, network.TopologyKey, cancellationToken)));
    }

    public async Task<TeamLabTrafficFlowPageModel> GetFlowsAsync(
        Guid runtimePublicId,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        var cursor = DecodeCursor(after);
        var take = Math.Clamp(limit, 1, 200);
        var query = context.TeamLabTrafficFlows.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation);
        if (cursor is { } decoded)
            query = query.Where(item => item.CapturedAt > decoded.Time ||
                                        item.CapturedAt == decoded.Time && item.Id > decoded.Id);
        var rows = await query.OrderBy(item => item.CapturedAt).ThenBy(item => item.Id)
            .Take(take + 1)
            .Select(item => new
            {
                item.Id, item.ShardId, item.NetworkId, item.SourceIp, item.SourcePort, item.DestinationIp,
                item.DestinationPort, item.Protocol, item.Bytes, item.Packets, item.FirstSeenAt, item.LastSeenAt,
                item.CapturedAt
            }).ToArrayAsync(cancellationToken);
        var shardPublicIds = runtime.Shards.ToDictionary(item => item.Id, item => item.PublicId);
        var networkKeys = runtime.Networks.ToDictionary(item => item.Id, item => item.TopologyKey);
        var page = rows.Take(take).Select(item => new TeamLabTrafficFlowProjectionModel(
            new TimeCursor(item.CapturedAt, item.Id).Encode(),
            item.ShardId is { } shardId ? shardPublicIds.GetValueOrDefault(shardId) : Guid.Empty,
            item.NetworkId is { } networkId ? networkKeys.GetValueOrDefault(networkId) ?? string.Empty : string.Empty,
            item.SourceIp, item.SourcePort, item.DestinationIp, item.DestinationPort, item.Protocol,
            item.Bytes, item.Packets, item.FirstSeenAt, item.LastSeenAt)).ToArray();
        return new TeamLabTrafficFlowPageModel(page,
            rows.Length > take && page.Length > 0
                ? new TimeCursor(rows[take - 1].CapturedAt, rows[take - 1].Id).Encode()
                : null);
    }

    public async Task<TeamLabCaptureModel> StartCaptureAsync(
        Guid runtimePublicId,
        CreateTeamLabCaptureModel model,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (model.MaxSeconds is < 1 or > 86400 || model.MaxBytes is < 1024 or > 10L * 1024 * 1024 * 1024 ||
            model.ExpiresInSeconds is < 60 or > 604800)
            throw new TeamLabApiContractException("capture_limit_exceeded", "Capture limits exceed the platform policy.", 422);
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        if (runtime.Status != TeamLabRuntimeStatus.Running)
            throw new TeamLabApiContractException("runtime_not_ready", "The runtime is not ready for capture.", 409);
        var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
        var keyHash = normalizedKey is null ? null : Hash(normalizedKey);
        var requestHash = keyHash is null ? null : Hash(JsonSerializer.Serialize(model));
        await using var idempotencyLock = keyHash is null
            ? null
            : await locks.AcquireAsync($"teamlab:capture:{runtime.Id}:{runtime.Generation}:{keyHash}",
                TimeSpan.FromSeconds(10), cancellationToken: cancellationToken);
        using var leaseCancellation = idempotencyLock is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idempotencyLock.LeaseLost);
        cancellationToken = leaseCancellation?.Token ?? cancellationToken;
        if (keyHash is not null)
        {
            var existing = await context.TeamLabTrafficCaptureJobs.Include(item => item.Network)
                .SingleOrDefaultAsync(item =>
                    item.RuntimeId == runtime.Id &&
                    item.Generation == runtime.Generation &&
                    item.IdempotencyKeyHash == keyHash,
                    cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                    throw new TeamLabApiContractException(
                        "idempotency_key_reused",
                        "The Idempotency-Key was already used with a different capture request.",
                        409);
                if (existing.Status != TeamLabTrafficCaptureStatus.Pending)
                    return ToModel(existing, existing.Network?.TopologyKey);
                return await StartCaptureJobAsync(runtime, existing, existing.Network!, cancellationToken);
            }
        }
        var network = string.IsNullOrWhiteSpace(model.NetworkKey)
            ? runtime.Networks.FirstOrDefault(item => item.Generation == runtime.Generation && item.ShardId == runtime.EntryShardId)
            : runtime.Networks.FirstOrDefault(item => item.Generation == runtime.Generation && item.TopologyKey == model.NetworkKey);
        if (network?.WorkerNodeId is null)
            throw new TeamLabApiContractException("topology_invalid", "The capture network was not found.", 422);
        var job = new TeamLabTrafficCaptureJob
        {
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            ShardId = network.ShardId,
            NetworkId = network.Id,
            WorkerNodeId = network.WorkerNodeId,
            Status = TeamLabTrafficCaptureStatus.Pending,
            Scope = model.Scope.Trim(),
            IdempotencyKeyHash = keyHash,
            RequestHash = requestHash,
            MaxSeconds = model.MaxSeconds,
            MaxBytes = model.MaxBytes,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(model.ExpiresInSeconds)
        };
        context.TeamLabTrafficCaptureJobs.Add(job);
        await context.SaveChangesAsync(cancellationToken);
        return await StartCaptureJobAsync(runtime, job, network, cancellationToken);
    }

    private async Task<TeamLabCaptureModel> StartCaptureJobAsync(
        TeamLabRuntime runtime,
        TeamLabTrafficCaptureJob job,
        TeamLabRuntimeNetwork network,
        CancellationToken cancellationToken)
    {
        if (network.WorkerNodeId is not { } workerNodeId)
            throw new TeamLabApiContractException("runtime_invalid", "Capture node is missing.", 500);
        var result = await executor.StartCaptureAsync(workerNodeId,
            new TeamLabNodeCaptureStartRequest(runtime.Id, job.Id, job.Scope, network.BridgeName, job.MaxSeconds, job.MaxBytes),
            cancellationToken);
        job.Status = result.Success
            ? result.Running ? TeamLabTrafficCaptureStatus.Running : TeamLabTrafficCaptureStatus.Completed
            : TeamLabTrafficCaptureStatus.Failed;
        job.StartedAt = result.Success ? DateTimeOffset.UtcNow : null;
        job.CompletedAt = result.Success && !result.Running ? DateTimeOffset.UtcNow : null;
        job.FilePath = result.FilePath;
        job.CapturedBytes = result.CapturedBytes;
        job.LastError = result.Success ? null : result.Message;
        eventRecorder.Record(
            runtime,
            "capture",
            result.Success ? TeamLabEventLevel.Success : TeamLabEventLevel.Error,
            result.Success
                ? OperationalEventCodes.TeamLab.CaptureStarted
                : OperationalEventCodes.TeamLab.CaptureFailed,
            result.Success ? OperationalEventOutcome.Started : OperationalEventOutcome.Failed,
            result.Success ? "Traffic capture started." : "Traffic capture failed to start.",
            result.Success ? null : CaptureError(workerNodeId),
            workerNodeId);
        await context.SaveChangesAsync(cancellationToken);
        if (!result.Success) throw new TeamLabApiContractException("operation_failed", result.Message, 500);
        return ToModel(job, network.TopologyKey);
    }

    public async Task<TeamLabCaptureModel> GetCaptureAsync(
        Guid runtimePublicId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        var (runtime, job) = await LoadCaptureAsync(runtimePublicId, captureId, cancellationToken);
        if (job.Status is TeamLabTrafficCaptureStatus.Running or TeamLabTrafficCaptureStatus.Stopping && job.WorkerNodeId is { } nodeId)
        {
            var result = await executor.GetCaptureStatusAsync(nodeId, runtime.Id, job.Id, cancellationToken);
            job.CapturedBytes = result.CapturedBytes;
            job.FilePath = result.FilePath ?? job.FilePath;
            if (!result.Success)
            {
                job.Status = TeamLabTrafficCaptureStatus.Failed;
                job.LastError = result.Message;
                job.CompletedAt = DateTimeOffset.UtcNow;
                eventRecorder.Record(
                    runtime,
                    "capture",
                    TeamLabEventLevel.Error,
                    OperationalEventCodes.TeamLab.CaptureFailed,
                    OperationalEventOutcome.Failed,
                    "Traffic capture status check failed.",
                    CaptureError(nodeId),
                    nodeId);
            }
            else if (!result.Running)
            {
                job.Status = TeamLabTrafficCaptureStatus.Completed;
                job.LastError = null;
                job.CompletedAt ??= DateTimeOffset.UtcNow;
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        return ToModel(job, job.Network?.TopologyKey);
    }

    public async Task<TeamLabCaptureModel> StopCaptureAsync(
        Guid runtimePublicId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        var (runtime, job) = await LoadCaptureAsync(runtimePublicId, captureId, cancellationToken);
        if (job.WorkerNodeId is null) throw new TeamLabApiContractException("runtime_invalid", "Capture node is missing.", 500);
        if (job.Status is TeamLabTrafficCaptureStatus.Completed or TeamLabTrafficCaptureStatus.Failed)
            return ToModel(job, job.Network?.TopologyKey);
        job.Status = TeamLabTrafficCaptureStatus.Stopping;
        await context.SaveChangesAsync(cancellationToken);
        var result = await executor.StopCaptureAsync(job.WorkerNodeId.Value, runtime.Id, job.Id, cancellationToken);
        job.Status = result.Success ? TeamLabTrafficCaptureStatus.Completed : TeamLabTrafficCaptureStatus.Failed;
        job.CapturedBytes = result.CapturedBytes;
        job.FilePath = result.FilePath ?? job.FilePath;
        job.LastError = result.Success ? null : result.Message;
        job.CompletedAt = DateTimeOffset.UtcNow;
        eventRecorder.Record(
            runtime,
            "capture",
            result.Success ? TeamLabEventLevel.Success : TeamLabEventLevel.Error,
            result.Success
                ? OperationalEventCodes.TeamLab.CaptureStopped
                : OperationalEventCodes.TeamLab.CaptureFailed,
            result.Success ? OperationalEventOutcome.Succeeded : OperationalEventOutcome.Failed,
            result.Success ? "Traffic capture stopped." : "Traffic capture failed to stop.",
            result.Success ? null : CaptureError(job.WorkerNodeId.Value),
            job.WorkerNodeId.Value);
        await context.SaveChangesAsync(cancellationToken);
        return ToModel(job, job.Network?.TopologyKey);
    }

    public async Task<TeamLabNodeCaptureDownload> DownloadCaptureAsync(
        Guid runtimePublicId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        var (runtime, job) = await LoadCaptureAsync(runtimePublicId, captureId, cancellationToken);
        if (job.WorkerNodeId is null || job.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new TeamLabApiContractException("capture_not_found", "The capture is unavailable or expired.", 404);
        return await executor.DownloadCaptureAsync(job.WorkerNodeId.Value, runtime.Id, job.Id, cancellationToken);
    }

    internal async Task CollectAvailableFlowsAsync(CancellationToken cancellationToken)
    {
        var networkIds = await context.TeamLabRuntimeNetworks.AsNoTracking()
            .Where(item => item.WorkerNodeId != null &&
                           item.Generation == item.Runtime.Generation &&
                           item.Runtime.Status == TeamLabRuntimeStatus.Running)
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);

        foreach (var networkId in networkIds)
        {
            try
            {
                await CollectNetworkFlowsAsync(networkId, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Another application instance owns this collector lease.
            }
        }
    }

    private async Task CollectNetworkFlowsAsync(int networkId, CancellationToken cancellationToken)
    {
        await using var flowLock = await locks.AcquireAsync(
            $"teamlab:flow:network:{networkId}",
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(15),
            cancellationToken);
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, flowLock.LeaseLost);
        cancellationToken = leaseCancellation.Token;
        var network = await context.TeamLabRuntimeNetworks.AsNoTracking()
            .Where(item => item.Id == networkId && item.WorkerNodeId != null &&
                           item.Generation == item.Runtime.Generation &&
                           item.Runtime.Status == TeamLabRuntimeStatus.Running)
            .Select(item => new
            {
                item.Id,
                item.RuntimeId,
                item.Generation,
                item.ShardId,
                item.WorkerNodeId,
                item.TopologyKey,
                item.FlowCursor
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (network?.WorkerNodeId is not { } workerNodeId)
            return;

        var result = await executor.GetFlowSnapshotAsync(
            workerNodeId,
            network.RuntimeId,
            network.TopologyKey,
            network.FlowCursor,
            cancellationToken);
        if (!result.Success)
            return;

        var envelopes = result.Samples
            .Where(item => item.Cursor > network.FlowCursor &&
                           !string.IsNullOrWhiteSpace(item.SourceIp) &&
                           !string.IsNullOrWhiteSpace(item.DestinationIp))
            .Select(item => TeamLabTrafficEnvelope.Create(
                network.RuntimeId,
                network.Generation,
                network.ShardId,
                network.Id,
                network.WorkerNodeId,
                item))
            .ToArray();
        if (envelopes.Length > 0)
            await ingestor.EnqueueAsync(envelopes, cancellationToken);

        var nextCursor = Math.Max(network.FlowCursor, result.NextCursor);
        if (nextCursor > network.FlowCursor)
            await context.TeamLabRuntimeNetworks
                .Where(item => item.Id == network.Id && item.Generation == network.Generation &&
                               item.FlowCursor == network.FlowCursor)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.FlowCursor, nextCursor),
                    cancellationToken);
    }

    private async Task<TeamLabRuntime> LoadRuntimeAsync(Guid runtimePublicId, CancellationToken cancellationToken) =>
        await context.TeamLabRuntimes.Include(item => item.Shards).Include(item => item.Networks)
            .SingleOrDefaultAsync(item => item.PublicId == runtimePublicId, cancellationToken)
        ?? throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);

    private async Task<(TeamLabRuntime Runtime, TeamLabTrafficCaptureJob Job)> LoadCaptureAsync(
        Guid runtimePublicId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == runtimePublicId, cancellationToken)
            ?? throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);
        var job = await context.TeamLabTrafficCaptureJobs.Include(item => item.Network)
            .SingleOrDefaultAsync(item => item.RuntimeId == runtime.Id && item.PublicId == captureId &&
                                          item.Generation == runtime.Generation, cancellationToken)
            ?? throw new TeamLabApiContractException("capture_not_found", "The capture was not found.", 404);
        return (runtime, job);
    }

    private static TeamLabCaptureModel ToModel(TeamLabTrafficCaptureJob job, string? networkKey) =>
        new(job.PublicId, job.Status, job.Scope, networkKey, job.MaxBytes, job.MaxSeconds, job.CapturedBytes,
            job.CreatedAt, job.StartedAt, job.CompletedAt, job.ExpiresAt, job.LastError);

    private static OperationalError CaptureError(Guid workerNodeId) =>
        new(
            OperationalErrorCategory.Network,
            OperationalErrorCodes.NetworkOperationFailed,
            "TeamLab traffic capture operation failed.",
            true,
            WorkerNodeId: workerNodeId,
            Operation: "teamlab.capture");

    private static TimeCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            return TimeCursor.Decode(cursor);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException("traffic_cursor_invalid", "The traffic cursor is invalid.", 400);
        }
    }

    private static string? NormalizeIdempotencyKey(string? value)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length is < 1 or > 128 || normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new TeamLabApiContractException(
                "idempotency_key_invalid",
                "Idempotency-Key must contain 1-128 ASCII letters, digits, '-', '_' or '.'.",
                400);
        return normalized;
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

}
