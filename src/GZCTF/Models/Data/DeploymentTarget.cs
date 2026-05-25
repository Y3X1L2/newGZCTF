using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(TargetNodeId))]
[Index(nameof(Status))]
public class DeploymentTarget
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TargetNodeId { get; set; }
    public TargetType Type { get; set; } = TargetType.Docker;
    public TargetAction Action { get; set; } = TargetAction.Create;
    [MaxLength(4096)] public string Payload { get; set; } = "{}";
    public TargetStatus Status { get; set; } = TargetStatus.Pending;
    public int? ResultPort { get; set; }
    [MaxLength(256)] public string? ResultHost { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    [MaxLength(1024)] public string? ErrorMessage { get; set; }

    [ForeignKey(nameof(TargetNodeId))]
    public WorkerNode? TargetNode { get; set; }
}

public enum TargetType : byte { Docker = 0, Vm = 1 }
public enum TargetAction : byte { Create = 0, Start = 1, Destroy = 2, SnapshotRevert = 3 }
public enum TargetStatus : byte { Pending = 0, Running = 1, Completed = 2, Failed = 3, Cancelled = 4 }
