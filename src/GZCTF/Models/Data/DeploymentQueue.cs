using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(Status))]
public class DeploymentQueue
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    [Required] public Guid TargetNodeId { get; set; }
    public DeploymentQueueStatus Status { get; set; } = DeploymentQueueStatus.Queued;
    public int Position { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public enum DeploymentQueueStatus : byte { Queued = 0, Deploying = 1, Completed = 2, Failed = 3 }
