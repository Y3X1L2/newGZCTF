using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

public enum DockerRegistryMigrationStatus : byte
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

[Index(nameof(Status))]
[Index(nameof(TargetNodeId))]
public class DockerRegistryMigrationTask
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TargetNodeId { get; set; }

    [MaxLength(256)] public string SourceRegistry { get; set; } = string.Empty;

    [MaxLength(256)] public string TargetRegistry { get; set; } = string.Empty;

    public DockerRegistryMigrationStatus Status { get; set; } = DockerRegistryMigrationStatus.Pending;

    public int TotalItems { get; set; }

    public int CompletedItems { get; set; }

    public int FailedItems { get; set; }

    [MaxLength(1024)] public string? Message { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey(nameof(TargetNodeId))]
    public WorkerNode? TargetNode { get; set; }

    public List<DockerRegistryMigrationItem> Items { get; set; } = [];
}

[Index(nameof(TaskId))]
[Index(nameof(ImageTemplateId))]
[Index(nameof(Status))]
public class DockerRegistryMigrationItem
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskId { get; set; }

    public int ImageTemplateId { get; set; }

    [MaxLength(512)] public string SourceImage { get; set; } = string.Empty;

    [MaxLength(512)] public string TargetImage { get; set; } = string.Empty;

    [MaxLength(128)] public string? SourceDigest { get; set; }

    [MaxLength(128)] public string? TargetDigest { get; set; }

    public DockerRegistryMigrationStatus Status { get; set; } = DockerRegistryMigrationStatus.Pending;

    public long BytesTransferred { get; set; }

    public long TotalBytes { get; set; }

    public int RetryCount { get; set; }

    [MaxLength(1024)] public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey(nameof(TaskId))]
    public DockerRegistryMigrationTask? Task { get; set; }

    [ForeignKey(nameof(ImageTemplateId))]
    public ImageTemplate? ImageTemplate { get; set; }
}
