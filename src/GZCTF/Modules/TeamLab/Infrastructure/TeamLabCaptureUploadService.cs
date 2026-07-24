using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed record TeamLabCaptureSegmentUploadCommand(
    Guid CaptureId,
    Guid SegmentId,
    Guid WorkerNodeId,
    string Token,
    string Sha256,
    long? ContentLength,
    Stream Content);

public sealed record TeamLabCaptureUploadResult(
    int StatusCode,
    string? Code = null,
    bool AlreadyExists = false);

public sealed class TeamLabCaptureUploadService(
    AppDbContext context,
    TeamLabCaptureUploadTokenService tokens,
    TeamLabCaptureArtifactStore artifacts,
    IDistributedLeaseProvider locks)
{
    public async Task<TeamLabCaptureUploadResult> UploadAsync(
        TeamLabCaptureSegmentUploadCommand request,
        CancellationToken cancellationToken)
    {
        if (!tokens.TryValidate(request.Token, out var grant) ||
            grant.CaptureId != request.CaptureId || grant.SegmentId != request.SegmentId ||
            grant.WorkerNodeId != request.WorkerNodeId ||
            !TryNormalizeSha256(request.Sha256, out var requestDigest) ||
            !FixedTimeEquals(requestDigest, grant.ExpectedSha256))
            return new(StatusCodes.Status401Unauthorized);
        if (request.ContentLength is not { } contentLength || contentLength != grant.ExpectedBytes ||
            contentLength > grant.MaxBytes)
            return new(StatusCodes.Status400BadRequest, "capture_upload_size_invalid");

        IDistributedLease uploadLease;
        try
        {
            uploadLease = await locks.AcquireAsync(
                $"teamlab:capture-upload:{request.SegmentId:N}",
                TimeSpan.FromSeconds(2),
                TimeSpan.FromMinutes(15),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            return new(StatusCodes.Status409Conflict, "capture_upload_in_progress");
        }

        await using (uploadLease)
        {
            using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, uploadLease.LeaseLost);
            cancellationToken = leaseCancellation.Token;
            var segment = await context.TeamLabTrafficCaptureSegments
                .Include(item => item.CaptureJob)
                .ThenInclude(item => item.Runtime)
                .Include(item => item.CaptureJob)
                .ThenInclude(item => item.Segments)
                .SingleOrDefaultAsync(item => item.PublicId == request.SegmentId &&
                                              item.CaptureJob.PublicId == request.CaptureId,
                    cancellationToken);
            if (segment is null || segment.WorkerNodeId != request.WorkerNodeId ||
                segment.CaptureJob.ExpiresAt <= DateTimeOffset.UtcNow)
                return new(StatusCodes.Status404NotFound, "capture_segment_not_found");
            if (!FixedTimeEquals(segment.Sha256, grant.ExpectedSha256) ||
                segment.CapturedBytes != grant.ExpectedBytes)
                return new(StatusCodes.Status409Conflict, "capture_upload_state_changed");
            if (segment.Status == TeamLabTrafficCaptureSegmentStatus.Uploaded &&
                !string.IsNullOrWhiteSpace(segment.ObjectPath) &&
                await artifacts.ExistsAsync(segment.ObjectPath, cancellationToken))
                return new(StatusCodes.Status200OK, AlreadyExists: true);
            if (segment.Status is not (TeamLabTrafficCaptureSegmentStatus.Captured or
                TeamLabTrafficCaptureSegmentStatus.Uploading))
                return new(StatusCodes.Status409Conflict, "capture_segment_not_ready");

            segment.Status = TeamLabTrafficCaptureSegmentStatus.Uploading;
            segment.UpdatedAt = DateTimeOffset.UtcNow;
            segment.LastError = null;
            await context.SaveChangesAsync(cancellationToken);

            var job = segment.CaptureJob;
            var objectPath = TeamLabCaptureArtifactStore.BuildObjectPath(
                job.Runtime.PublicId, job.Generation, job.PublicId, segment.PublicId);
            var write = await artifacts.WriteSegmentAsync(
                objectPath,
                request.Content,
                grant.ExpectedBytes,
                grant.MaxBytes,
                grant.ExpectedSha256,
                cancellationToken);
            if (!write.Success)
            {
                segment.Status = TeamLabTrafficCaptureSegmentStatus.Captured;
                segment.LastError = write.Message;
                segment.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
                return new(StatusCodes.Status400BadRequest, "capture_upload_validation_failed");
            }

            var now = DateTimeOffset.UtcNow;
            segment.Status = TeamLabTrafficCaptureSegmentStatus.Uploaded;
            segment.ObjectPath = objectPath;
            segment.Sha256 = write.Sha256;
            segment.UploadedBytes = write.Bytes;
            segment.UploadedAt = now;
            segment.UpdatedAt = now;
            segment.LastError = null;
            RefreshAggregate(job, now);
            await context.SaveChangesAsync(cancellationToken);
            return new(StatusCodes.Status200OK);
        }
    }

    private static void RefreshAggregate(TeamLabTrafficCaptureJob job, DateTimeOffset now)
    {
        job.CapturedBytes = job.Segments.Sum(item => item.CapturedBytes);
        if (job.Segments.Count == 0 || job.Segments.Any(item =>
                item.Status != TeamLabTrafficCaptureSegmentStatus.Uploaded))
            return;
        job.Status = TeamLabTrafficCaptureStatus.Completed;
        job.CompletedAt ??= now;
        job.LastError = null;
    }

    private static bool TryNormalizeSha256(string value, out string digest)
    {
        digest = value.Trim().ToLowerInvariant();
        return digest.Length == 64 && digest.All(Uri.IsHexDigit);
    }

    private static bool FixedTimeEquals(string? left, string right)
    {
        if (!TryNormalizeSha256(left ?? string.Empty, out var normalized)) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(normalized),
            Convert.FromHexString(right));
    }
}
