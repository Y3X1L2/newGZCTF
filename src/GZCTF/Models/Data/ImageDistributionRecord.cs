using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Models.Data;

public class ImageDistributionRecord
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int ImageTemplateId { get; set; }

    public Guid WorkerNodeId { get; set; }

    [MaxLength(128)]
    public string ImageHash { get; set; } = string.Empty;

    public ImageType ImageType { get; set; }

    public ImageDistributionStatus Status { get; set; } = ImageDistributionStatus.Pending;

    public ImageDistributionOperation Operation { get; set; } = ImageDistributionOperation.Distribute;

    public ImageDistributionStage Stage { get; set; } = ImageDistributionStage.Queued;

    public int AttemptCount { get; set; }

    [MaxLength(256)]
    public string? ClaimOwner { get; set; }

    public DateTimeOffset? ClaimExpiresAt { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? ProgressUpdatedAt { get; set; }

    [MaxLength(128)]
    public string? LastErrorCode { get; set; }

    public OperationalErrorCategory? ErrorCategory { get; set; }

    public bool Retryable { get; set; }

    public Guid? LastCorrelationId { get; set; }

    public ICollection<ImageDistributionReference> References { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastCheckedAt { get; set; }

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }

    [ForeignKey(nameof(ImageTemplateId))]
    [JsonIgnore]
    public ImageTemplate? ImageTemplate { get; set; }

    [ForeignKey(nameof(WorkerNodeId))]
    [JsonIgnore]
    public WorkerNode? WorkerNode { get; set; }
}

public enum ImageDistributionStatus : byte
{
    Pending = 0,
    Pulling = 1,
    Ready = 2,
    Failed = 3,
    CleanupPending = 4
}

public enum ImageDistributionOperation : byte
{
    Distribute = 0,
    Cleanup = 1
}

public enum ImageDistributionStage : byte
{
    None = 0,
    Queued = 1,
    Preparing = 2,
    Pulling = 3,
    Verifying = 4,
    Cleaning = 5
}
