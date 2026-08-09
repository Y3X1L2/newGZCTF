using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.TeamLab.Domain;

public enum TeamLabTrafficCaptureSegmentStatus : byte
{
    Pending = 0,
    Running = 1,
    Stopping = 2,
    Captured = 3,
    Uploading = 4,
    Uploaded = 5,
    Failed = 6,
    Expired = 7,
    CleanupPending = 8
}

public sealed class TeamLabTrafficCaptureJob
{
    [Key] public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public Guid? ApiOperationId { get; set; }
    public TeamLabTrafficCaptureStatus Status { get; set; } = TeamLabTrafficCaptureStatus.Pending;
    [MaxLength(80)] public string Scope { get; set; } = string.Empty;
    [MaxLength(64)] public string? NetworkKey { get; set; }
    public long MaxBytes { get; set; }
    public int MaxSeconds { get; set; }
    public long CapturedBytes { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
    public ApiOperation? ApiOperation { get; set; }
    public List<TeamLabTrafficCaptureSegment> Segments { get; set; } = [];
}

public sealed class TeamLabTrafficCaptureSegment
{
    [Key] public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int CaptureJobId { get; set; }
    public Guid WorkerNodeId { get; set; }
    public int ObservationPointId { get; set; }
    public TeamLabTrafficCaptureSegmentStatus Status { get; set; } = TeamLabTrafficCaptureSegmentStatus.Pending;
    [MaxLength(512)] public string? ObjectPath { get; set; }
    [MaxLength(64)] public string? Sha256 { get; set; }
    public long MaxBytes { get; set; }
    public long CapturedBytes { get; set; }
    public long UploadedBytes { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? UploadedAt { get; set; }
    public TeamLabTrafficCaptureJob CaptureJob { get; set; } = null!;
    public WorkerNode WorkerNode { get; set; } = null!;
    public TeamLabObservationPoint ObservationPoint { get; set; } = null!;
}
