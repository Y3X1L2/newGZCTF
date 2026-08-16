using GZCTF.Models;
using GZCTF.Models.Data;
using System.Net;
using System.Text.Json;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Infrastructure.Concurrency;
using Microsoft.EntityFrameworkCore;
using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Infrastructure.Telemetry;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabTrafficApplicationService(
    AppDbContext context,
    ITeamLabNodeExecutor executor,
    IDistributedLeaseProvider locks,
    ITeamLabTrafficIngestor ingestor,
    TeamLabEventRecorder eventRecorder,
    ILogger<TeamLabTrafficApplicationService> logger)
{
    public Task StartCollectorsAsync(TeamLabRuntime runtime, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task StopCollectorsAsync(TeamLabRuntime runtime, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<TeamLabTrafficFlowPageModel> GetFlowsAsync(
        Guid runtimePublicId,
        string? after,
        int limit,
        string? queryText,
        string? protocol,
        string? networkKey,
        int? port,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        var cursor = DecodeCursor(after);
        var take = Math.Clamp(limit, 1, 200);
        var query = context.TeamLabTrafficFlows.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation);
        if (!string.IsNullOrWhiteSpace(queryText))
        {
            var term = queryText.Trim();
            query = query.Where(item => item.SourceIp.Contains(term) || item.DestinationIp.Contains(term));
        }
        if (port is { } portValue)
            query = query.Where(item => item.SourcePort == portValue || item.DestinationPort == portValue);
        if (!string.IsNullOrWhiteSpace(protocol))
        {
            var normalizedProtocol = protocol.Trim().ToUpperInvariant();
            query = query.Where(item => item.Protocol == normalizedProtocol);
        }
        if (!string.IsNullOrWhiteSpace(networkKey))
        {
            var normalizedNetworkKey = networkKey.Trim();
            var networkIds = runtime.Networks
                .Where(item => item.TopologyKey == normalizedNetworkKey)
                .Select(item => item.Id)
                .ToArray();
            query = query.Where(item => item.NetworkId != null && networkIds.Contains(item.NetworkId.Value));
        }
        if (cursor is { } decoded)
            query = query.Where(item => item.CapturedAt < decoded.Time ||
                                        item.CapturedAt == decoded.Time && item.Id < decoded.Id);
        var rows = await query.OrderByDescending(item => item.CapturedAt).ThenByDescending(item => item.Id)
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
                : null,
            await GetCompletenessAsync(runtime, cancellationToken));
    }

    public async Task<TeamLabTrafficPathPageModel> GetPathsAsync(
        Guid runtimePublicId,
        string? after,
        int limit,
        string? queryText,
        string? protocol,
        TeamLabPathConfidence? confidence,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        var cursor = DecodeCursor(after);
        var take = Math.Clamp(limit, 1, 100);
        var query = context.TeamLabTrafficPaths.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation);
        if (!string.IsNullOrWhiteSpace(queryText))
        {
            var term = queryText.Trim();
            query = query.Where(item => item.SourceIp.Contains(term) || item.DestinationIp.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(protocol))
        {
            var normalizedProtocol = protocol.Trim().ToUpperInvariant();
            query = query.Where(item => item.Protocol == normalizedProtocol);
        }
        if (confidence is { } expectedConfidence)
            query = query.Where(item => item.Confidence == expectedConfidence);
        if (cursor is { } decoded)
            query = query.Where(item => item.StartedAt < decoded.Time ||
                                        item.StartedAt == decoded.Time && item.Id < decoded.Id);
        var rows = await query.OrderByDescending(item => item.StartedAt).ThenByDescending(item => item.Id)
            .Take(take + 1)
            .Select(item => new
            {
                item.Id,
                item.PublicId,
                item.Confidence,
                item.SourceIp,
                item.SourcePort,
                item.DestinationIp,
                item.DestinationPort,
                item.Protocol,
                item.StartedAt,
                item.EndedAt,
                HopCount = item.Hops.Count
            })
            .ToArrayAsync(cancellationToken);
        var page = rows.Take(take).Select(item => new TeamLabTrafficPathSummaryModel(
            new TimeCursor(item.StartedAt, item.Id).Encode(),
            item.PublicId,
            item.Confidence,
            item.SourceIp,
            item.SourcePort,
            item.DestinationIp,
            item.DestinationPort,
            item.Protocol,
            item.StartedAt,
            item.EndedAt,
            item.HopCount)).ToArray();
        return new TeamLabTrafficPathPageModel(
            page,
            rows.Length > take && page.Length > 0
                ? new TimeCursor(rows[take - 1].StartedAt, rows[take - 1].Id).Encode()
                : null,
            await GetCompletenessAsync(runtime, cancellationToken));
    }

    public async Task<TeamLabTrafficPathModel> GetPathAsync(
        Guid runtimePublicId,
        Guid pathPublicId,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        var path = await context.TeamLabTrafficPaths.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation &&
                           item.PublicId == pathPublicId)
            .Select(item => new
            {
                item.Id,
                item.PublicId,
                item.Confidence,
                item.SourceIp,
                item.SourcePort,
                item.DestinationIp,
                item.DestinationPort,
                item.Protocol,
                item.StartedAt,
                item.EndedAt
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new TeamLabApiContractException("traffic_path_not_found", "未找到该流量路径", 404);
        var hops = await context.TeamLabTrafficPathHops.AsNoTracking()
            .Where(item => item.PathId == path.Id)
            .OrderBy(item => item.Ordinal)
            .Select(item => new TeamLabTrafficPathHopModel(
                item.Ordinal,
                item.ObservedAt,
                item.EvidenceKind,
                item.ObservationPoint.Kind,
                item.ObservationPoint.Shard == null
                    ? null
                    : item.ObservationPoint.Shard.PublicId,
                item.ObservationPoint.Network == null
                    ? null
                    : item.ObservationPoint.Network.TopologyKey,
                item.ObservationPoint.InfrastructureFragment == null
                    ? null
                    : item.ObservationPoint.InfrastructureFragment.Infrastructure.TopologyKey,
                item.ObservationPoint.Asset == null
                    ? null
                    : item.ObservationPoint.Asset.TopologyKey,
                item.Direction,
                item.SourceIp,
                item.SourcePort,
                item.DestinationIp,
                item.DestinationPort,
                item.Protocol))
            .ToArrayAsync(cancellationToken);
        return new TeamLabTrafficPathModel(
            path.PublicId,
            path.Confidence,
            path.SourceIp,
            path.SourcePort,
            path.DestinationIp,
            path.DestinationPort,
            path.Protocol,
            path.StartedAt,
            path.EndedAt,
            hops);
    }

    public async Task<TeamLabCaptureModel> StartCaptureAsync(
        Guid runtimePublicId,
        CreateTeamLabCaptureModel model,
        CancellationToken cancellationToken) =>
        await StartCaptureCoreAsync(runtimePublicId, model, null, cancellationToken);

    public async Task<TeamLabCaptureModel> StartCaptureForOperationAsync(
        Guid runtimePublicId,
        CreateTeamLabCaptureModel model,
        Guid operationId,
        CancellationToken cancellationToken) =>
        await StartCaptureCoreAsync(runtimePublicId, model, operationId, cancellationToken);

    private async Task<TeamLabCaptureModel> StartCaptureCoreAsync(
        Guid runtimePublicId,
        CreateTeamLabCaptureModel model,
        Guid? operationId,
        CancellationToken cancellationToken)
    {
        if (model.MaxSeconds is < 1 or > 86400 || model.MaxBytes is < 1024 or > 10L * 1024 * 1024 * 1024 ||
            model.ExpiresInSeconds is < 60 or > 604800)
            throw new TeamLabApiContractException("capture_limit_exceeded", "抓包限制超出平台策略", 422);
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        if (runtime.Status != TeamLabRuntimeStatus.Running)
            throw new TeamLabApiContractException("runtime_not_ready", "运行时尚未就绪，无法抓包", 409);

        await using var captureLock = await locks.AcquireAsync(
            $"teamlab:capture-start:{runtime.Id}:{runtime.Generation}",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(2),
            cancellationToken);
        if (operationId is { } operation)
        {
            var existing = await CaptureQuery()
                .SingleOrDefaultAsync(item => item.ApiOperationId == operation, cancellationToken);
            if (existing is not null)
            {
                if (existing.Status != TeamLabTrafficCaptureStatus.Pending)
                    return ToModel(existing);
                if (existing.Segments.Count == 0)
                {
                    var recoveredPoints = await CompileCaptureScopeAsync(runtime, model, cancellationToken);
                    existing.Segments.AddRange(recoveredPoints.Select(ToSegment));
                    await context.SaveChangesAsync(cancellationToken);
                }
                return await StartCaptureJobAsync(runtime, existing, cancellationToken);
            }
        }
        var scope = NormalizeCaptureScope(model.Scope);
        var points = await CompileCaptureScopeAsync(runtime, model with { Scope = scope }, cancellationToken);
        var job = new TeamLabTrafficCaptureJob
        {
            RuntimeId = runtime.Id,
            Generation = runtime.Generation,
            ApiOperationId = operationId,
            Status = TeamLabTrafficCaptureStatus.Pending,
            Scope = scope,
            NetworkKey = string.Equals(scope, "network", StringComparison.Ordinal) ? model.NetworkKey?.Trim() : null,
            MaxSeconds = model.MaxSeconds,
            MaxBytes = model.MaxBytes,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(model.ExpiresInSeconds),
            Segments = points.Select(ToSegment).ToList()
        };
        context.TeamLabTrafficCaptureJobs.Add(job);
        await context.SaveChangesAsync(cancellationToken);
        return await StartCaptureJobAsync(runtime, job, cancellationToken);
    }

    private async Task<TeamLabCaptureModel> StartCaptureJobAsync(
        TeamLabRuntime runtime,
        TeamLabTrafficCaptureJob job,
        CancellationToken cancellationToken)
    {
        if (job.Segments.Count == 0)
            throw new TeamLabApiContractException("capture_scope_empty", "抓包范围没有观测点", 422);
        if (AssignSegmentBudgets(job))
            await context.SaveChangesAsync(cancellationToken);

        var results = await ExecuteByNodeAsync(
            job.Segments,
            (segment, token) => executor.StartCaptureAsync(
                segment.WorkerNodeId,
                new TeamLabNodeCaptureStartRequest(
                    runtime.Id,
                    job.Generation,
                    job.PublicId,
                    segment.PublicId,
                    segment.ObservationPoint.PublicId,
                    segment.ObservationPoint.InterfaceToken,
                    job.MaxSeconds,
                    segment.MaxBytes),
                token),
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var (segment, result) in results)
            ApplyNodeResult(segment, result, now);
        var failed = results.Where(item => !item.Result.Success).ToArray();
        if (failed.Length > 0)
        {
            await StopStartedSegmentsAsync(runtime, job, results, cancellationToken);
            job.Status = TeamLabTrafficCaptureStatus.Failed;
            job.CompletedAt = now;
            job.LastError = $"{failed.Length} 个抓包分片启动失败";
        }
        else
        {
            job.Status = TeamLabTrafficCaptureStatus.Running;
            job.StartedAt ??= now;
            job.LastError = null;
        }
        job.CapturedBytes = job.Segments.Sum(item => item.CapturedBytes);
        eventRecorder.Record(
            runtime,
            "capture",
            failed.Length == 0 ? TeamLabEventLevel.Success : TeamLabEventLevel.Error,
            failed.Length == 0
                ? OperationalEventCodes.TeamLab.CaptureStarted
                : OperationalEventCodes.TeamLab.CaptureFailed,
            failed.Length == 0 ? OperationalEventOutcome.Started : OperationalEventOutcome.Failed,
            failed.Length == 0 ? "Traffic capture started." : "Traffic capture failed to start.",
            failed.Length == 0 ? null : CaptureError(failed[0].Segment.WorkerNodeId),
            failed.Length == 0 ? null : failed[0].Segment.WorkerNodeId,
            new Dictionary<string, object?>
            {
                ["captureScope"] = job.Scope,
                ["captureSegmentCount"] = job.Segments.Count,
                ["captureWorkerCount"] = job.Segments.Select(item => item.WorkerNodeId).Distinct().Count()
            });
        PlatformTelemetry.RecordTeamLabCapture(
            "start", job.Scope, failed.Length == 0 ? "success" : "failure");
        await context.SaveChangesAsync(cancellationToken);
        if (failed.Length > 0)
            throw new TeamLabApiContractException(
                "operation_failed", "流量抓包无法启动", 500);
        return ToModel(job);
    }

    internal static bool AssignSegmentBudgets(TeamLabTrafficCaptureJob job)
    {
        if (job.Segments.Count == 0) return false;
        if (job.MaxBytes < job.Segments.Count)
            throw new TeamLabApiContractException(
                "capture_budget_too_small",
                "抓包 MaxBytes 必须允许每个观测分片至少一个字节",
                422);

        var ordered = job.Segments.OrderBy(item => item.PublicId).ToArray();
        var baseline = job.MaxBytes / ordered.Length;
        var remainder = job.MaxBytes % ordered.Length;
        var changed = false;
        for (var index = 0; index < ordered.Length; index++)
        {
            var budget = baseline + (index < remainder ? 1 : 0);
            if (ordered[index].MaxBytes == budget) continue;
            ordered[index].MaxBytes = budget;
            changed = true;
        }
        return changed;
    }

    public async Task<TeamLabCapturePageModel> ListCapturesAsync(
        Guid runtimePublicId,
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimePublicId, cancellationToken);
        var take = Math.Clamp(limit, 1, 100);
        var cursor = DecodeIntCursor(after);
        var query = context.TeamLabTrafficCaptureJobs
            .Include(item => item.Segments)
            .ThenInclude(item => item.ObservationPoint.Network)
            .Include(item => item.Segments)
            .ThenInclude(item => item.ObservationPoint.InfrastructureFragment).ThenInclude(item => item.Infrastructure)
            .Include(item => item.Segments)
            .ThenInclude(item => item.ObservationPoint.Asset)
            .Where(item => item.RuntimeId == runtime.Id);
        if (cursor is not null)
            query = query.Where(item => item.Id < cursor);
        var rows = await query.OrderByDescending(item => item.Id)
            .Take(take + 1)
            .ToArrayAsync(cancellationToken);
        return new TeamLabCapturePageModel(
            rows.Take(take).Select(ToModel).ToArray(),
            rows.Length > take ? EncodeIntCursor(rows[take - 1].Id) : null);
    }

    public async Task<TeamLabCaptureModel> GetCaptureAsync(
        Guid runtimePublicId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        var (_, job) = await LoadCaptureAsync(runtimePublicId, captureId, cancellationToken);
        RefreshAggregate(job, DateTimeOffset.UtcNow);
        return ToModel(job);
    }

    public async Task<TeamLabCaptureModel> StopCaptureAsync(
        Guid runtimePublicId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        var (runtime, job) = await LoadCaptureAsync(runtimePublicId, captureId, cancellationToken);
        if (job.Status is TeamLabTrafficCaptureStatus.Completed or TeamLabTrafficCaptureStatus.Failed or
            TeamLabTrafficCaptureStatus.Expired)
            return ToModel(job);
        var active = job.Segments.Where(item => item.Status is
            TeamLabTrafficCaptureSegmentStatus.Running or TeamLabTrafficCaptureSegmentStatus.Stopping).ToArray();
        if (active.Length == 0)
        {
            RefreshAggregate(job, DateTimeOffset.UtcNow);
            return ToModel(job);
        }
        job.Status = TeamLabTrafficCaptureStatus.Stopping;
        foreach (var segment in active)
            segment.Status = TeamLabTrafficCaptureSegmentStatus.Stopping;
        await context.SaveChangesAsync(cancellationToken);
        var results = await ExecuteByNodeAsync(
            active,
            (segment, token) => executor.StopCaptureAsync(
                segment.WorkerNodeId,
                runtime.Id,
                job.Generation,
                job.PublicId,
                segment.PublicId,
                token),
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var (segment, result) in results)
            ApplyNodeResult(segment, result, now);
        var failed = results.Where(item => !item.Result.Success).ToArray();
        job.CapturedBytes = job.Segments.Sum(item => item.CapturedBytes);
        job.Status = failed.Length == 0 ? TeamLabTrafficCaptureStatus.Stopping : TeamLabTrafficCaptureStatus.Failed;
        job.LastError = failed.Length == 0 ? null : $"{failed.Length} 个抓包分片停止失败";
        if (failed.Length > 0) job.CompletedAt = now;
        eventRecorder.Record(
            runtime,
            "capture",
            failed.Length == 0 ? TeamLabEventLevel.Success : TeamLabEventLevel.Error,
            failed.Length == 0
                ? OperationalEventCodes.TeamLab.CaptureStopped
                : OperationalEventCodes.TeamLab.CaptureFailed,
            failed.Length == 0 ? OperationalEventOutcome.Succeeded : OperationalEventOutcome.Failed,
            failed.Length == 0 ? "Traffic capture stop requested." : "Traffic capture failed to stop.",
            failed.Length == 0 ? null : CaptureError(failed[0].Segment.WorkerNodeId),
            failed.Length == 0 ? null : failed[0].Segment.WorkerNodeId,
            new Dictionary<string, object?>
            {
                ["captureSegmentCount"] = active.Length
            });
        PlatformTelemetry.RecordTeamLabCapture(
            "stop", job.Scope, failed.Length == 0 ? "success" : "failure");
        await context.SaveChangesAsync(cancellationToken);
        if (failed.Length > 0)
            throw new TeamLabApiContractException("operation_failed", "流量抓包无法停止", 500);
        return ToModel(job);
    }

    public async Task<TeamLabCaptureArchiveDescriptor> DownloadCaptureAsync(
        Guid runtimePublicId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        var (runtime, job) = await LoadCaptureAsync(runtimePublicId, captureId, cancellationToken);
        var segments = job.Segments
            .Where(item => item.Status == TeamLabTrafficCaptureSegmentStatus.Uploaded &&
                           !string.IsNullOrWhiteSpace(item.ObjectPath) &&
                           !string.IsNullOrWhiteSpace(item.Sha256) && item.UploadedBytes > 0)
            .OrderBy(item => item.ObservationPoint.Kind)
            .ThenBy(item => item.ObservationPoint.PublicId)
            .ToArray();
        if (job.ExpiresAt <= DateTimeOffset.UtcNow || segments.Length == 0)
            throw new TeamLabApiContractException("capture_not_found", "抓包不可用或已过期", 404);
        eventRecorder.Record(
            runtime,
            "capture-download",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.CaptureDownloaded,
            OperationalEventOutcome.Succeeded,
            "Traffic capture archive downloaded.",
            detail: new Dictionary<string, object?>
            {
                ["captureSegmentCount"] = segments.Length,
                ["sizeBytes"] = segments.Sum(item => item.UploadedBytes)
            });
        PlatformTelemetry.RecordTeamLabCapture("download", job.Scope, "success");
        await context.SaveChangesAsync(cancellationToken);
        return new TeamLabCaptureArchiveDescriptor(
            runtime.PublicId,
            job.Generation,
            job.PublicId,
            job.Scope,
            job.NetworkKey,
            job.CreatedAt,
            job.CompletedAt,
            job.ExpiresAt,
            segments.Select(item => new TeamLabCaptureArchiveSegment(
                item.PublicId,
                item.ObservationPoint.PublicId,
                item.ObservationPoint.Kind,
                item.ObservationPoint.Network?.TopologyKey,
                item.ObservationPoint.InfrastructureFragment?.Infrastructure.TopologyKey,
                item.ObservationPoint.Asset?.TopologyKey,
                item.ObjectPath!,
                item.UploadedBytes,
                item.Sha256!,
                item.CompletedAt,
                item.UploadedAt)).ToArray());
    }

    internal async Task CollectAvailableFlowsAsync(CancellationToken cancellationToken)
    {
        var sourceRows = await context.TeamLabObservationPoints.AsNoTracking()
            .Where(item => item.Enabled &&
                           item.Generation == item.Runtime.Generation &&
                           item.Runtime.Status == TeamLabRuntimeStatus.Running)
            .Select(item => new { item.RuntimeId, item.Generation, item.WorkerNodeId })
            .Distinct()
            .OrderBy(item => item.RuntimeId)
            .ThenBy(item => item.WorkerNodeId)
            .ToArrayAsync(cancellationToken);
        var sources = sourceRows
            .Select(item => new ObservationSource(item.RuntimeId, item.Generation, item.WorkerNodeId))
            .ToArray();

        foreach (var source in sources)
        {
            try
            {
                await CollectNodeObservationsAsync(source, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Another application instance owns this node collector lease.
            }
        }
    }

    private async Task CollectNodeObservationsAsync(
        ObservationSource source,
        CancellationToken cancellationToken)
    {
        await using var observationLock = await locks.AcquireAsync(
            $"teamlab:observation:{source.RuntimeId}:{source.Generation}:{source.WorkerNodeId:N}",
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(15),
            cancellationToken);
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, observationLock.LeaseLost);
        cancellationToken = leaseCancellation.Token;

        var runtime = await context.TeamLabRuntimes.SingleOrDefaultAsync(
            item => item.Id == source.RuntimeId && item.Generation == source.Generation &&
                    item.Status == TeamLabRuntimeStatus.Running,
            cancellationToken);
        if (runtime is null)
            return;

        var points = await context.TeamLabObservationPoints
            .Where(item => item.RuntimeId == source.RuntimeId && item.Generation == source.Generation &&
                           item.WorkerNodeId == source.WorkerNodeId && item.Enabled)
            .OrderBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        if (points.Length == 0)
            return;

        var cursor = await context.TeamLabObservationCursors.SingleOrDefaultAsync(
            item => item.RuntimeId == source.RuntimeId && item.Generation == source.Generation &&
                    item.WorkerNodeId == source.WorkerNodeId,
            cancellationToken);
        if (cursor is null)
        {
            cursor = new TeamLabObservationCursor
            {
                RuntimeId = source.RuntimeId,
                Generation = source.Generation,
                WorkerNodeId = source.WorkerNodeId
            };
            context.TeamLabObservationCursors.Add(cursor);
            await context.SaveChangesAsync(cancellationToken);
        }

        var result = await executor.ReadObservationsAsync(
            source.WorkerNodeId,
            source.RuntimeId,
            source.Generation,
            cursor.LastSequence,
            null,
            500,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var hadError = points.Any(item => !string.IsNullOrWhiteSpace(item.LastError));
        if (!result.Success)
        {
            foreach (var point in points)
            {
                point.LastError = result.Message;
                point.UpdatedAt = now;
            }
            if (!hadError)
                eventRecorder.Record(
                    runtime,
                    "observation",
                    TeamLabEventLevel.Warning,
                    OperationalEventCodes.TeamLab.ObservationDegraded,
                    OperationalEventOutcome.Blocked,
                    "Traffic observation became unavailable on a runtime shard.",
                    new OperationalError(
                        OperationalErrorCategory.AgentTransport,
                        OperationalErrorCodes.ObservationUnavailable,
                        "流量观测读取失败",
                        true,
                        WorkerNodeId: source.WorkerNodeId,
                        Operation: "teamlab.observation.read"),
                    source.WorkerNodeId,
                    ObservationDetail(runtime, "degraded"));
            PlatformTelemetry.RecordTeamLabObservation("degraded", "mixed");
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var pointsByPublicId = points.ToDictionary(item => item.PublicId);
        var endpointPoints = points
            .Where(item => item.Kind == TeamLabObservationPointKind.WorkloadEndpoint)
            .GroupBy(item => item.TopologyKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var networkCidrs = await context.TeamLabRuntimeNetworks.AsNoTracking()
            .Where(item => item.RuntimeId == source.RuntimeId && item.Generation == source.Generation)
            .Select(item => new { item.Id, item.Cidr })
            .ToDictionaryAsync(item => item.Id, item => IPNetwork.Parse(item.Cidr), cancellationToken);
        var prepared = PrepareObservationBatch(
            result.Records,
            cursor.LastSequence,
            result.NextSequence,
            item => !string.IsNullOrWhiteSpace(item.SourceIp) &&
                    !string.IsNullOrWhiteSpace(item.DestinationIp) &&
                    ResolveObservationPoint(item, pointsByPublicId, endpointPoints, networkCidrs) is { } point
                ? TeamLabTrafficEnvelope.Create(
                    source.RuntimeId,
                    source.Generation,
                    point.ShardId,
                    point.NetworkId,
                    point.Id,
                    point.Kind,
                    point.AssetId,
                    source.WorkerNodeId,
                    item)
                : null);
        var envelopes = prepared.Envelopes;
        var enqueue = envelopes.Length == 0
            ? new TeamLabTrafficEnqueueResult(0, 0, 0, false)
            : await ingestor.EnqueueAsync(envelopes, cancellationToken);

        var previousDropped = cursor.DroppedCount;
        var previousRejected = cursor.SensorRejectedCount;
        cursor.LastSequence = Math.Max(cursor.LastSequence, prepared.NextSequence);
        cursor.DroppedCount = Math.Max(cursor.DroppedCount, result.DroppedCount) + enqueue.DroppedCount;
        cursor.SensorRejectedCount = Math.Max(cursor.SensorRejectedCount, result.Health.SensorRejectedCount);
        cursor.LastSensorErrorCode = result.Health.LastSensorErrorCode;
        cursor.UpdatedAt = now;
        foreach (var point in points)
        {
            point.LastSequence = cursor.LastSequence;
            point.DroppedPackets = cursor.DroppedCount;
            point.LastError = result.Health.LastError;
            point.UpdatedAt = now;
        }
        if (prepared.BlockedByUnresolvedRecord)
            logger.LogWarning(
                "TeamLab 观测游标在序列 {Sequence} 停止，因为下一条记录无法解析：runtime={RuntimeId}、generation={Generation}、node={WorkerNodeId}",
                cursor.LastSequence, source.RuntimeId, source.Generation, source.WorkerNodeId);
        var droppedDelta = cursor.DroppedCount - previousDropped;
        if (droppedDelta > 0)
        {
            eventRecorder.Record(
                runtime,
                "observation",
                TeamLabEventLevel.Warning,
                OperationalEventCodes.TeamLab.ObservationDropped,
                OperationalEventOutcome.Observed,
                "Traffic observation dropped records because of backpressure or local capture loss.",
                workerNodeId: source.WorkerNodeId,
                detail: ObservationDetail(runtime, "dropped", droppedDelta));
            PlatformTelemetry.RecordTeamLabObservation("dropped", "mixed", droppedDelta);
        }
        var rejectedDelta = cursor.SensorRejectedCount - previousRejected;
        if (rejectedDelta > 0)
        {
            eventRecorder.Record(
                runtime,
                "sensor-authentication",
                TeamLabEventLevel.Warning,
                OperationalEventCodes.TeamLab.SensorAuthenticationDegraded,
                OperationalEventOutcome.Observed,
                "Endpoint sensor events were rejected by authentication or replay validation.",
                new OperationalError(
                    OperationalErrorCategory.Authorization,
                    OperationalErrorCodes.SensorAuthenticationFailed,
                    "终端传感器事件校验拒绝了一条或多条记录",
                    false,
                    WorkerNodeId: source.WorkerNodeId,
                    Operation: "teamlab.sensor.verify"),
                source.WorkerNodeId,
                new Dictionary<string, object?>
                {
                    ["generation"] = runtime.Generation,
                    ["stage"] = "sensor-authentication",
                    ["rejectedCount"] = rejectedDelta,
                    ["errorCode"] = cursor.LastSensorErrorCode
                });
            PlatformTelemetry.RecordTeamLabObservation("rejected", "endpoint-process", rejectedDelta);
        }
        if (hadError && string.IsNullOrWhiteSpace(result.Health.LastError))
        {
            eventRecorder.Record(
                runtime,
                "observation",
                TeamLabEventLevel.Success,
                OperationalEventCodes.TeamLab.ObservationRecovered,
                OperationalEventOutcome.Recovered,
                "Traffic observation recovered on the runtime shard.",
                workerNodeId: source.WorkerNodeId,
                detail: ObservationDetail(runtime, "recovered"));
            PlatformTelemetry.RecordTeamLabObservation("recovered", "mixed");
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string, object?> ObservationDetail(
        TeamLabRuntime runtime,
        string result,
        long? count = null) => new Dictionary<string, object?>
    {
        ["generation"] = runtime.Generation,
        ["stage"] = "observation",
        ["result"] = result,
        ["count"] = count
    };

    private static TeamLabObservationPoint? ResolveObservationPoint(
        TeamLabNodeObservationRecord record,
        IReadOnlyDictionary<Guid, TeamLabObservationPoint> pointsByPublicId,
        IReadOnlyDictionary<string, TeamLabObservationPoint[]> endpointPoints,
        IReadOnlyDictionary<int, IPNetwork> networkCidrs)
    {
        if (record.ObservationPointId is { } publicId)
            return pointsByPublicId.GetValueOrDefault(publicId);
        if (string.IsNullOrWhiteSpace(record.AssetKey) ||
            !endpointPoints.TryGetValue(record.AssetKey, out var candidates))
            return null;
        if (candidates.Length == 1)
            return candidates[0];
        if (IPAddress.TryParse(record.SourceIp, out var localAddress))
            return candidates.FirstOrDefault(item => item.NetworkId is { } networkId &&
                                                       networkCidrs.TryGetValue(networkId, out var network) &&
                                                       network.Contains(localAddress))
                   ?? candidates[0];
        return candidates[0];
    }

    internal static PreparedObservationBatch PrepareObservationBatch(
        IReadOnlyCollection<TeamLabNodeObservationRecord> records,
        long lastSequence,
        long nextSequence,
        Func<TeamLabNodeObservationRecord, TeamLabTrafficEnvelope?> envelopeFactory)
    {
        var envelopes = new List<TeamLabTrafficEnvelope>();
        var committedSequence = lastSequence;
        foreach (var record in records.Where(item => item.Sequence > lastSequence).OrderBy(item => item.Sequence))
        {
            var envelope = envelopeFactory(record);
            if (envelope is null)
                return new PreparedObservationBatch(envelopes.ToArray(), committedSequence, true);
            envelopes.Add(envelope);
            committedSequence = record.Sequence;
        }
        return new PreparedObservationBatch(
            envelopes.ToArray(),
            Math.Max(committedSequence, nextSequence),
            false);
    }

    internal sealed record PreparedObservationBatch(
        TeamLabTrafficEnvelope[] Envelopes,
        long NextSequence,
        bool BlockedByUnresolvedRecord);

    private sealed record ObservationSource(int RuntimeId, int Generation, Guid WorkerNodeId);

    private async Task<TeamLabObservationPoint[]> CompileCaptureScopeAsync(
        TeamLabRuntime runtime,
        CreateTeamLabCaptureModel model,
        CancellationToken cancellationToken)
    {
        var scope = NormalizeCaptureScope(model.Scope);
        var points = await context.TeamLabObservationPoints
            .Include(item => item.Network)
            .Include(item => item.InfrastructureFragment)
            .ThenInclude(item => item!.Infrastructure)
            .Include(item => item.Asset)
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation && item.Enabled)
            .OrderBy(item => item.WorkerNodeId)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.PublicId)
            .ToArrayAsync(cancellationToken);
        TeamLabObservationPoint[] selected;
        if (scope == "runtime")
        {
            selected = points;
        }
        else if (scope == "network")
        {
            var networkKey = model.NetworkKey?.Trim();
            var network = runtime.Networks.SingleOrDefault(item => item.Generation == runtime.Generation &&
                                                                   item.TopologyKey == networkKey)
                          ?? throw new TeamLabApiContractException(
                              "topology_invalid", "未找到该抓包网络", 422);
            selected = points.Where(item =>
                    item.NetworkId == network.Id ||
                    item.Kind == TeamLabObservationPointKind.RouterFragment &&
                    item.InfrastructureFragment is not null &&
                    FragmentContainsNetwork(item.InfrastructureFragment.InterfaceSummaryJson, network.TopologyKey) ||
                    item.Kind == TeamLabObservationPointKind.FabricUplink && item.ShardId == network.ShardId)
                .ToArray();
        }
        else if (scope.StartsWith("path:", StringComparison.Ordinal))
        {
            var pathId = Guid.Parse(scope[5..]);
            var pointIds = await context.TeamLabTrafficPathHops.AsNoTracking()
                .Where(item => item.Path.RuntimeId == runtime.Id && item.Path.Generation == runtime.Generation &&
                               item.Path.PublicId == pathId)
                .Select(item => item.ObservationPointId)
                .Distinct()
                .ToArrayAsync(cancellationToken);
            if (pointIds.Length == 0)
                throw new TeamLabApiContractException("traffic_path_not_found", "未找到该流量路径", 404);
            selected = points.Where(item => pointIds.Contains(item.Id)).ToArray();
        }
        else
        {
            var assetKey = scope[6..];
            var endpoints = points.Where(item => item.Kind == TeamLabObservationPointKind.WorkloadEndpoint &&
                                                 item.Asset?.TopologyKey == assetKey).ToArray();
            if (endpoints.Length == 0)
                throw new TeamLabApiContractException("topology_invalid", "未找到该抓包资产", 422);
            var networkIds = endpoints.Where(item => item.NetworkId.HasValue)
                .Select(item => item.NetworkId!.Value).ToHashSet();
            var shardIds = endpoints.Where(item => item.ShardId.HasValue)
                .Select(item => item.ShardId!.Value).ToHashSet();
            selected = points.Where(item =>
                    endpoints.Contains(item) ||
                    item.NetworkId is { } networkId && networkIds.Contains(networkId) ||
                    item.ShardId is { } shardId && shardIds.Contains(shardId) &&
                    item.Kind is TeamLabObservationPointKind.RouterFragment or TeamLabObservationPointKind.FabricUplink)
                .ToArray();
        }

        selected = selected.DistinctBy(item => item.Id)
            .OrderBy(item => item.WorkerNodeId)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.PublicId)
            .ToArray();
        if (selected.Length == 0)
            throw new TeamLabApiContractException(
                "capture_scope_empty", "抓包范围没有启用的观测点", 422);
        return selected;
    }

    private async Task<IReadOnlyList<CaptureNodeResult>> ExecuteByNodeAsync(
        IReadOnlyCollection<TeamLabTrafficCaptureSegment> segments,
        Func<TeamLabTrafficCaptureSegment, CancellationToken, Task<TeamLabNodeCaptureResult>> action,
        CancellationToken cancellationToken)
    {
        var nodeTasks = segments.GroupBy(item => item.WorkerNodeId)
            .Select(async group =>
            {
                var results = new List<CaptureNodeResult>();
                foreach (var segment in group.OrderBy(item => item.PublicId))
                {
                    try
                    {
                        results.Add(new CaptureNodeResult(segment, await action(segment, cancellationToken)));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(exception,
                            "TeamLab 抓包请求失败，分片 {SegmentId}，节点 {WorkerNodeId}",
                            segment.PublicId, segment.WorkerNodeId);
                        results.Add(new CaptureNodeResult(segment,
                            new TeamLabNodeCaptureResult(
                                false,
                                "Agent 抓包请求失败",
                                segment.PublicId,
                                segment.CapturedBytes,
                                false,
                                segment.Sha256,
                                false)));
                    }
                }
                return results;
            });
        return (await Task.WhenAll(nodeTasks)).SelectMany(item => item).ToArray();
    }

    private async Task StopStartedSegmentsAsync(
        TeamLabRuntime runtime,
        TeamLabTrafficCaptureJob job,
        IReadOnlyList<CaptureNodeResult> startResults,
        CancellationToken cancellationToken)
    {
        var started = startResults.Where(item => item.Result.Success && item.Result.Running)
            .Select(item => item.Segment)
            .ToArray();
        if (started.Length == 0) return;
        var stopResults = await ExecuteByNodeAsync(
            started,
            (segment, token) => executor.StopCaptureAsync(
                segment.WorkerNodeId,
                runtime.Id,
                job.Generation,
                job.PublicId,
                segment.PublicId,
                token),
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var (segment, result) in stopResults)
            ApplyNodeResult(segment, result, now);
    }

    internal static void ApplyNodeResult(
        TeamLabTrafficCaptureSegment segment,
        TeamLabNodeCaptureResult result,
        DateTimeOffset now)
    {
        segment.CapturedBytes = result.CapturedBytes;
        segment.Sha256 = string.IsNullOrWhiteSpace(result.Sha256) ? segment.Sha256 : result.Sha256.ToLowerInvariant();
        segment.UpdatedAt = now;
        if (!result.Success)
        {
            segment.Status = TeamLabTrafficCaptureSegmentStatus.Failed;
            segment.LastError = result.Message;
            segment.CompletedAt ??= now;
            return;
        }
        segment.LastError = null;
        segment.StartedAt ??= now;
        if (result.Uploaded)
        {
            segment.Status = TeamLabTrafficCaptureSegmentStatus.Uploaded;
            segment.UploadedBytes = Math.Max(segment.UploadedBytes, result.CapturedBytes);
            segment.UploadedAt ??= now;
            segment.CompletedAt ??= now;
        }
        else if (result.Running)
        {
            segment.Status = TeamLabTrafficCaptureSegmentStatus.Running;
        }
        else if (result.CapturedBytes > 0 && !string.IsNullOrWhiteSpace(result.Sha256))
        {
            segment.Status = TeamLabTrafficCaptureSegmentStatus.Captured;
            segment.CompletedAt ??= now;
        }
        else
        {
            segment.Status = TeamLabTrafficCaptureSegmentStatus.Failed;
            segment.LastError = "抓包分片完成但缺少已验证产物";
            segment.CompletedAt ??= now;
        }
    }

    internal static void RefreshAggregate(TeamLabTrafficCaptureJob job, DateTimeOffset now)
    {
        job.CapturedBytes = job.Segments.Sum(item => item.CapturedBytes);
        if (job.Segments.Count == 0) return;
        if (job.Segments.All(item => item.Status == TeamLabTrafficCaptureSegmentStatus.Uploaded))
        {
            job.Status = TeamLabTrafficCaptureStatus.Completed;
            job.CompletedAt ??= now;
            job.LastError = null;
        }
        else if (job.Segments.Any(item => item.Status == TeamLabTrafficCaptureSegmentStatus.Failed) &&
                 job.Segments.All(item => item.Status is TeamLabTrafficCaptureSegmentStatus.Failed or
                     TeamLabTrafficCaptureSegmentStatus.Uploaded or TeamLabTrafficCaptureSegmentStatus.Expired))
        {
            job.Status = TeamLabTrafficCaptureStatus.Failed;
            job.CompletedAt ??= now;
            job.LastError ??= "一个或多个抓包分片失败";
        }
        else if (job.Segments.Any(item => item.Status is TeamLabTrafficCaptureSegmentStatus.Stopping or
                     TeamLabTrafficCaptureSegmentStatus.Captured or TeamLabTrafficCaptureSegmentStatus.Uploading))
        {
            job.Status = TeamLabTrafficCaptureStatus.Stopping;
        }
        else if (job.Segments.Any(item => item.Status == TeamLabTrafficCaptureSegmentStatus.Running))
        {
            job.Status = TeamLabTrafficCaptureStatus.Running;
        }
    }

    private static TeamLabTrafficCaptureSegment ToSegment(TeamLabObservationPoint point) => new()
    {
        WorkerNodeId = point.WorkerNodeId,
        ObservationPointId = point.Id,
        ObservationPoint = point
    };

    private static string NormalizeCaptureScope(string value)
    {
        var scope = value.Trim();
        if (scope.Equals("runtime", StringComparison.OrdinalIgnoreCase)) return "runtime";
        if (scope.Equals("network", StringComparison.OrdinalIgnoreCase)) return "network";
        if (scope.StartsWith("path:", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(scope[5..], out var pathId))
            return $"path:{pathId:D}";
        if (scope.StartsWith("asset:", StringComparison.OrdinalIgnoreCase))
        {
            var assetKey = scope[6..].Trim();
            if (assetKey.Length is > 0 and <= 63 && assetKey[0] is >= 'a' and <= 'z' &&
                assetKey.Skip(1).All(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                return $"asset:{assetKey}";
        }
        throw new TeamLabApiContractException(
            "capture_scope_invalid", "抓包范围必须是 runtime、network、path:{id} 或 asset:{key}", 422);
    }

    private static bool FragmentContainsNetwork(string json, string networkKey)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array &&
                   document.RootElement.EnumerateArray().Any(item =>
                       item.TryGetProperty("NetworkKey", out var value) && value.GetString() == networkKey);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record CaptureNodeResult(
        TeamLabTrafficCaptureSegment Segment,
        TeamLabNodeCaptureResult Result);

    private async Task<TeamLabRuntime> LoadRuntimeAsync(Guid runtimePublicId, CancellationToken cancellationToken) =>
        await context.TeamLabRuntimes.Include(item => item.Shards).Include(item => item.Networks)
            .SingleOrDefaultAsync(item => item.PublicId == runtimePublicId, cancellationToken)
        ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);

    private async Task<TeamLabTrafficCompletenessModel> GetCompletenessAsync(
        TeamLabRuntime runtime, CancellationToken cancellationToken)
    {
        var dropped = await context.TeamLabObservationPoints.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation)
            .SumAsync(item => (long?)item.DroppedPackets, cancellationToken) ?? 0;
        return new TeamLabTrafficCompletenessModel(dropped == 0, dropped);
    }

    private async Task<(TeamLabRuntime Runtime, TeamLabTrafficCaptureJob Job)> LoadCaptureAsync(
        Guid runtimePublicId,
        Guid captureId,
        CancellationToken cancellationToken)
    {
        var job = await CaptureQuery()
            .SingleOrDefaultAsync(item => item.Runtime.PublicId == runtimePublicId && item.PublicId == captureId &&
                                          item.Generation == item.Runtime.Generation, cancellationToken)
            ?? throw new TeamLabApiContractException("capture_not_found", "未找到该抓包", 404);
        return (job.Runtime, job);
    }

    private IQueryable<TeamLabTrafficCaptureJob> CaptureQuery() =>
        context.TeamLabTrafficCaptureJobs
            .Include(item => item.Runtime)
            .Include(item => item.Segments)
            .ThenInclude(item => item.ObservationPoint)
            .ThenInclude(item => item.Network)
            .Include(item => item.Segments)
            .ThenInclude(item => item.ObservationPoint)
            .ThenInclude(item => item.InfrastructureFragment)
            .ThenInclude(item => item!.Infrastructure)
            .Include(item => item.Segments)
            .ThenInclude(item => item.ObservationPoint)
            .ThenInclude(item => item.Asset);

    private static TeamLabCaptureModel ToModel(TeamLabTrafficCaptureJob job) =>
        new(job.PublicId, job.Status, job.Scope, job.NetworkKey, job.MaxBytes, job.MaxSeconds, job.CapturedBytes,
            job.CreatedAt, job.StartedAt, job.CompletedAt, job.ExpiresAt,
            job.Segments.OrderBy(item => item.ObservationPoint.Kind)
                .ThenBy(item => item.ObservationPoint.PublicId)
                .Select(item => new TeamLabCaptureSegmentModel(
                    item.PublicId,
                    item.Status,
                    item.ObservationPoint.PublicId,
                    item.ObservationPoint.Kind,
                    item.ObservationPoint.Network?.TopologyKey,
                    item.ObservationPoint.InfrastructureFragment?.Infrastructure.TopologyKey,
                    item.ObservationPoint.Asset?.TopologyKey,
                    item.CapturedBytes,
                    item.UploadedBytes,
                    item.Sha256,
                    item.LastError)).ToArray(),
            job.LastError);

    private static OperationalError CaptureError(Guid workerNodeId) =>
        new(
            OperationalErrorCategory.Network,
            OperationalErrorCodes.NetworkOperationFailed,
            "TeamLab 流量抓包操作失败",
            true,
            WorkerNodeId: workerNodeId,
            Operation: "teamlab.capture");

    private static int? DecodeIntCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return int.TryParse(decoded, out var id) && id > 0 ? id : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new TeamLabApiContractException("capture_cursor_invalid", "抓包 cursor 无效", 400);
        }
    }

    private static string EncodeIntCursor(int id) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(id.ToString()));

    private static TimeCursor? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            return TimeCursor.Decode(cursor);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException("traffic_cursor_invalid", "流量游标无效", 400);
        }
    }

}
