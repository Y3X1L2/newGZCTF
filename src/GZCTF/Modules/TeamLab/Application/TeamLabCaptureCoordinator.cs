using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabCaptureCoordinator(
    AppDbContext context,
    ITeamLabNodeExecutor executor,
    TeamLabCaptureUploadTokenService tokens,
    TeamLabCaptureArtifactStore artifacts,
    IDistributedLeaseProvider locks,
    TeamLabEventRecorder eventRecorder,
    ILogger<TeamLabCaptureCoordinator> logger) : ITeamLabCaptureCleanup
{
    public async Task<IReadOnlyList<string>> ExpireGenerationAsync(
        int runtimeId,
        int generation,
        CancellationToken cancellationToken)
    {
        var jobIds = await context.TeamLabTrafficCaptureJobs.AsNoTracking()
            .Where(item => item.RuntimeId == runtimeId && item.Generation == generation &&
                           item.Status != TeamLabTrafficCaptureStatus.Expired)
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(cancellationToken);
        List<string> errors = [];
        foreach (var jobId in jobIds)
        {
            try
            {
                await using var jobLease = await locks.AcquireAsync(
                    $"teamlab:capture-job:{jobId}",
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromMinutes(2),
                    cancellationToken);
                var job = await CaptureQuery().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
                if (job is null || job.Status == TeamLabTrafficCaptureStatus.Expired) continue;
                await ExpireAsync(job, DateTimeOffset.UtcNow, cancellationToken);
                if (job.Status != TeamLabTrafficCaptureStatus.Expired)
                    errors.Add(job.LastError ?? $"抓包任务 {job.PublicId:D} 清理待处理");
            }
            catch (TimeoutException)
            {
                errors.Add($"抓包任务 {jobId} 当前由其他清理操作持有");
            }
        }
        return errors;
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var jobIds = await context.TeamLabTrafficCaptureJobs.AsNoTracking()
            .Where(item => item.Status != TeamLabTrafficCaptureStatus.Expired &&
                           (item.ExpiresAt <= now ||
                            item.Status == TeamLabTrafficCaptureStatus.Running ||
                            item.Status == TeamLabTrafficCaptureStatus.Stopping ||
                            item.Segments.Any(segment =>
                                segment.Status == TeamLabTrafficCaptureSegmentStatus.Captured ||
                                segment.Status == TeamLabTrafficCaptureSegmentStatus.Uploading)))
            .OrderBy(item => item.Id)
            .Select(item => item.Id)
            .Take(50)
            .ToArrayAsync(cancellationToken);
        foreach (var jobId in jobIds)
        {
            try
            {
                await ProcessJobAsync(jobId, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Another application instance owns this capture job.
            }
        }
    }

    private async Task ProcessJobAsync(int jobId, CancellationToken cancellationToken)
    {
        await using var jobLease = await locks.AcquireAsync(
            $"teamlab:capture-job:{jobId}",
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMinutes(2),
            cancellationToken);
        var job = await CaptureQuery().SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null || job.Status == TeamLabTrafficCaptureStatus.Expired) return;
        var now = DateTimeOffset.UtcNow;
        if (job.ExpiresAt <= now)
        {
            await ExpireAsync(job, now, cancellationToken);
            return;
        }

        var active = job.Segments.Where(item => item.Status is
            TeamLabTrafficCaptureSegmentStatus.Running or TeamLabTrafficCaptureSegmentStatus.Stopping).ToArray();
        if (active.Length > 0)
        {
            var statusResults = await ExecuteByNodeAsync(
                active,
                (segment, token) => segment.Status == TeamLabTrafficCaptureSegmentStatus.Stopping
                    ? executor.StopCaptureAsync(
                        segment.WorkerNodeId,
                        job.RuntimeId,
                        job.Generation,
                        job.PublicId,
                        segment.PublicId,
                        token)
                    : executor.GetCaptureStatusAsync(
                        segment.WorkerNodeId,
                        job.RuntimeId,
                        job.Generation,
                        job.PublicId,
                        segment.PublicId,
                        token),
                cancellationToken);
            now = DateTimeOffset.UtcNow;
            foreach (var (segment, result) in statusResults)
                TeamLabTrafficApplicationService.ApplyNodeResult(segment, result, now);
            TeamLabTrafficApplicationService.RefreshAggregate(job, now);
            await context.SaveChangesAsync(cancellationToken);
        }

        var ready = job.Segments.Where(item =>
                item.Status is TeamLabTrafficCaptureSegmentStatus.Captured or
                    TeamLabTrafficCaptureSegmentStatus.Uploading &&
                item.CapturedBytes > 0 && !string.IsNullOrWhiteSpace(item.Sha256))
            .ToArray();
        if (ready.Length == 0) return;

        foreach (var segment in ready)
        {
            segment.Status = TeamLabTrafficCaptureSegmentStatus.Uploading;
            segment.UpdatedAt = DateTimeOffset.UtcNow;
            segment.LastError = null;
        }
        await context.SaveChangesAsync(cancellationToken);
        var uploadResults = await ExecuteByNodeAsync(
            ready,
            (segment, token) => executor.UploadCaptureAsync(
                segment.WorkerNodeId,
                new TeamLabNodeCaptureUploadRequest(
                    job.RuntimeId,
                    job.Generation,
                    job.PublicId,
                    segment.PublicId,
                    $"/api/internal/teamlab/captures/{job.PublicId:D}/segments/{segment.PublicId:D}",
                    tokens.Issue(new TeamLabCaptureUploadGrant(
                        job.PublicId,
                        segment.PublicId,
                        segment.WorkerNodeId,
                        segment.CapturedBytes,
                        segment.MaxBytes,
                    segment.Sha256!), CaptureUploadTokenLifetime(segment.CapturedBytes)),
                    segment.MaxBytes),
                token),
            cancellationToken);
        foreach (var (segment, result) in uploadResults)
        {
            var previousStatus = segment.Status;
            await context.Entry(segment).ReloadAsync(cancellationToken);
            if (segment.Status != TeamLabTrafficCaptureSegmentStatus.Uploaded)
            {
                segment.Status = TeamLabTrafficCaptureSegmentStatus.Captured;
                segment.LastError = result.Success && result.Uploaded
                    ? "抓包上传已完成但持久化状态未确认"
                    : result.Message;
                segment.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else if (previousStatus != TeamLabTrafficCaptureSegmentStatus.Uploaded)
            {
                eventRecorder.Record(
                    job.Runtime,
                    "capture-upload",
                    TeamLabEventLevel.Success,
                    OperationalEventCodes.TeamLab.CaptureSegmentUploaded,
                    OperationalEventOutcome.Succeeded,
                    "Traffic capture segment uploaded.",
                    workerNodeId: segment.WorkerNodeId,
                    detail: new Dictionary<string, object?>
                    {
                        ["sizeBytes"] = segment.UploadedBytes
                    });
                PlatformTelemetry.RecordTeamLabCapture("upload", job.Scope, "success");
            }
        }
        TeamLabTrafficApplicationService.RefreshAggregate(job, DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ExpireAsync(
        TeamLabTrafficCaptureJob job,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pendingSegments = job.Segments
            .Where(item => item.Status != TeamLabTrafficCaptureSegmentStatus.Expired)
            .ToArray();
        var cleanupResults = await ExecuteByNodeAsync(
            pendingSegments,
            (segment, token) => executor.DeleteCaptureAsync(
                segment.WorkerNodeId,
                job.RuntimeId,
                job.Generation,
                job.PublicId,
                segment.PublicId,
                token),
            cancellationToken);
        foreach (var segment in pendingSegments)
        {
            var agentDeleted = cleanupResults
                .FirstOrDefault(item => item.Segment == segment)?.Result.Success == true;
            var objectDeleted = true;
            if (!string.IsNullOrWhiteSpace(segment.ObjectPath))
            {
                try
                {
                    await artifacts.DeleteAsync(segment.ObjectPath, cancellationToken);
                    segment.ObjectPath = null;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    objectDeleted = false;
                    logger.LogWarning(exception,
                        "删除已过期 TeamLab 抓包对象失败，分片 {SegmentId}", segment.PublicId);
                }
            }
            segment.Status = agentDeleted && objectDeleted
                ? TeamLabTrafficCaptureSegmentStatus.Expired
                : TeamLabTrafficCaptureSegmentStatus.CleanupPending;
            segment.UpdatedAt = now;
            segment.LastError = segment.Status == TeamLabTrafficCaptureSegmentStatus.Expired
                ? null
                : !agentDeleted && !objectDeleted
                    ? "Agent 与对象存储抓包清理待处理"
                    : !agentDeleted
                        ? "Agent 抓包清理待处理"
                        : "对象存储抓包清理待处理";
        }
        if (job.Segments.Any(item => item.Status != TeamLabTrafficCaptureSegmentStatus.Expired))
        {
            job.Status = TeamLabTrafficCaptureStatus.CleanupPending;
            job.LastError = "抓包产物清理待处理，将重试";
            await context.SaveChangesAsync(cancellationToken);
            return;
        }
        job.Status = TeamLabTrafficCaptureStatus.Expired;
        job.CompletedAt ??= now;
        job.LastError = null;
        eventRecorder.Record(
            job.Runtime,
            "capture-expiry",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.CaptureExpired,
            OperationalEventOutcome.Succeeded,
            "Traffic capture expired and retained artifacts were deleted.",
            detail: new Dictionary<string, object?>
            {
                ["captureSegmentCount"] = job.Segments.Count
            });
        PlatformTelemetry.RecordTeamLabCapture("expiry", job.Scope, "success");
        await context.SaveChangesAsync(cancellationToken);
    }

    internal static TimeSpan CaptureUploadTokenLifetime(long bytes)
    {
        const long bytesPerAdditionalMinute = 256L * 1024 * 1024;
        var additionalMinutes = Math.Max(0, bytes) / bytesPerAdditionalMinute;
        return TimeSpan.FromMinutes(Math.Clamp(10 + additionalMinutes, 10, 120));
    }

    private async Task<IReadOnlyList<CaptureNodeResult>> ExecuteByNodeAsync(
        IReadOnlyCollection<TeamLabTrafficCaptureSegment> segments,
        Func<TeamLabTrafficCaptureSegment, CancellationToken, Task<TeamLabNodeCaptureResult>> action,
        CancellationToken cancellationToken)
    {
        var tasks = segments.GroupBy(item => item.WorkerNodeId).Select(async group =>
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
                        "TeamLab 抓包协调失败，分片 {SegmentId}，节点 {WorkerNodeId}",
                        segment.PublicId, segment.WorkerNodeId);
                    results.Add(new CaptureNodeResult(segment,
                        new TeamLabNodeCaptureResult(
                            false,
                            "Agent 抓包协调失败",
                            segment.PublicId,
                            segment.CapturedBytes,
                            false,
                            segment.Sha256,
                            false)));
                }
            }
            return results;
        });
        return (await Task.WhenAll(tasks)).SelectMany(item => item).ToArray();
    }

    private IQueryable<TeamLabTrafficCaptureJob> CaptureQuery() =>
        context.TeamLabTrafficCaptureJobs
            .Include(item => item.Runtime)
            .Include(item => item.Segments)
            .ThenInclude(item => item.ObservationPoint);

    private sealed record CaptureNodeResult(
        TeamLabTrafficCaptureSegment Segment,
        TeamLabNodeCaptureResult Result);
}
