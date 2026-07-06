using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(Status), nameof(CreatedAt))]
[Index(nameof(TargetNodeId), nameof(Status))]
public class DeploymentQueueTicket
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public DeploymentQueueKind Kind { get; set; }
    public DeploymentQueueTicketStatus Status { get; set; } = DeploymentQueueTicketStatus.Pending;
    public Guid? DeploymentTargetId { get; set; }
    public Guid? TargetNodeId { get; set; }
    public int? OwnerTeamId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public int? GameId { get; set; }
    public int? ChallengeId { get; set; }
    public Guid? VmInstanceId { get; set; }
    public int? TeamLabRuntimeId { get; set; }
    public int DockerSlots { get; set; }
    public int VmSlots { get; set; }
    [MaxLength(256)] public string ActiveIdentity { get; set; } = string.Empty;
    [MaxLength(1024)] public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey(nameof(DeploymentTargetId))]
    public DeploymentTarget? DeploymentTarget { get; set; }

    [ForeignKey(nameof(TargetNodeId))]
    public WorkerNode? TargetNode { get; set; }

    public static DeploymentQueueTicket Create(DeploymentQueueRequest request) => new()
    {
        Kind = request.Kind,
        OwnerTeamId = request.OwnerTeamId,
        OwnerUserId = request.OwnerUserId,
        GameId = request.GameId,
        ChallengeId = request.ChallengeId,
        VmInstanceId = request.VmInstanceId,
        TeamLabRuntimeId = request.TeamLabRuntimeId,
        DockerSlots = Math.Max(0, request.DockerSlots),
        VmSlots = Math.Max(0, request.VmSlots),
        ActiveIdentity = BuildActiveIdentity(request)
    };

    public static string BuildActiveIdentity(DeploymentQueueRequest request) => request.Kind switch
    {
        DeploymentQueueKind.GameContainer =>
            $"game-container:{Required(request.GameId)}:{Required(request.OwnerTeamId)}:{Required(request.ChallengeId)}",
        DeploymentQueueKind.ExerciseContainer =>
            $"exercise-container:{Required(request.OwnerUserId)}:{Required(request.ChallengeId)}",
        DeploymentQueueKind.Vm =>
            $"vm:{Required(request.GameId)}:{Required(request.OwnerUserId)}:{Required(request.ChallengeId)}:{Required(request.VmInstanceId)}",
        DeploymentQueueKind.TeamLabRuntime =>
            $"teamlab-runtime:{Required(request.GameId)}:{Required(request.OwnerTeamId)}:{Required(request.TeamLabRuntimeId)}",
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown deployment queue kind.")
    };

    static T Required<T>(T? value) where T : struct =>
        value ?? throw new InvalidOperationException("Deployment queue request is missing a required identity field.");
}

public enum DeploymentQueueKind : byte
{
    GameContainer = 1,
    ExerciseContainer = 2,
    Vm = 3,
    TeamLabRuntime = 4
}

public enum DeploymentQueueTicketStatus : byte
{
    Pending = 0,
    Assigned = 1,
    Creating = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}
