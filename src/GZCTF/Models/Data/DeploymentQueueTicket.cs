using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using GZCTF.Services.Fleet;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Models.Data;

public class DeploymentQueueTicket
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public DeploymentQueueKind Kind { get; set; }
    public RuntimeOperationKind Operation { get; set; } = RuntimeOperationKind.Create;
    public DeploymentQueueTicketStatus Status { get; set; } = DeploymentQueueTicketStatus.Pending;
    public DeploymentStage Stage { get; set; } = DeploymentStage.Queued;
    public Guid? TargetNodeId { get; set; }
    public int? OwnerTeamId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public int? GameId { get; set; }
    public int? ChallengeId { get; set; }
    public Guid? VmInstanceId { get; set; }
    public int? TeamLabRuntimeId { get; set; }
    public int? AwdpServiceInstanceId { get; set; }
    public int? ExtensionSeconds { get; set; }
    public Guid? ApiOperationId { get; set; }
    public int DockerSlots { get; set; }
    public int VmSlots { get; set; }
    public int Generation { get; set; } = 1;
    [MaxLength(256)] public string ActiveIdentity { get; set; } = string.Empty;
    [MaxLength(256)] public string SubjectConcurrencyKey { get; set; } = string.Empty;
    [MaxLength(64)] public string? SubjectType { get; set; }
    [MaxLength(128)] public string? SubjectPublicId { get; set; }
    [MaxLength(256)] public string? SubjectDisplayName { get; set; }
    [MaxLength(256)] public string? ResourceDisplayName { get; set; }
    public string? ProtectedPayload { get; set; }
    [MaxLength(128)] public string? PayloadHash { get; set; }
    [MaxLength(64)] public string? BlockedReasonCode { get; set; }
    [MaxLength(512)] public string? StageMessage { get; set; }
    [MaxLength(1024)] public string? ErrorMessage { get; set; }
    public OperationalErrorCategory? ErrorCategory { get; set; }
    [MaxLength(128)] public string? ErrorCode { get; set; }
    public bool Retryable { get; set; }
    [MaxLength(128)] public string? TraceParent { get; set; }
    [MaxLength(512)] public string? TraceState { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NotBeforeAt { get; set; }
    [MaxLength(128)] public string? ClaimOwner { get; set; }
    public DateTimeOffset? ClaimExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    [ForeignKey(nameof(TargetNodeId))]
    public WorkerNode? TargetNode { get; set; }

    public static DeploymentQueueTicket Create(DeploymentQueueRequest request)
    {
        var activity = Activity.Current;
        return new DeploymentQueueTicket
        {
        Kind = request.Kind,
        Operation = request.Operation,
        OwnerTeamId = request.OwnerTeamId,
        OwnerUserId = request.OwnerUserId,
        GameId = request.GameId,
        ChallengeId = request.ChallengeId,
        VmInstanceId = request.VmInstanceId,
        TeamLabRuntimeId = request.TeamLabRuntimeId,
        AwdpServiceInstanceId = request.AwdpServiceInstanceId,
        TargetNodeId = request.TargetNodeId,
        ExtensionSeconds = request.ExtensionSeconds,
        ApiOperationId = request.ApiOperationId,
        DockerSlots = Math.Max(0, request.DockerSlots),
        VmSlots = Math.Max(0, request.VmSlots),
        Generation = Math.Max(1, request.Generation),
        ActiveIdentity = BuildActiveIdentity(request),
        SubjectConcurrencyKey = BuildSubjectConcurrencyKey(request),
        SubjectType = request.SubjectType,
        SubjectPublicId = request.SubjectPublicId,
        SubjectDisplayName = request.SubjectDisplayName,
        ResourceDisplayName = request.ResourceDisplayName,
        ProtectedPayload = request.ProtectedPayload,
            PayloadHash = request.PayloadHash,
            TraceParent = activity?.Id,
            TraceState = activity?.TraceStateString
        };
    }

    public static string BuildActiveIdentity(DeploymentQueueRequest request) =>
        $"{request.Operation}:{BuildSubjectConcurrencyKey(request)}:{Math.Max(1, request.Generation)}";

    public static string BuildSubjectConcurrencyKey(DeploymentQueueRequest request) => request.Kind switch
    {
        DeploymentQueueKind.GameContainer =>
            $"game-container:{Required(request.GameId)}:{Required(request.OwnerTeamId)}:{Required(request.ChallengeId)}",
        DeploymentQueueKind.ExerciseContainer =>
            $"exercise-container:{Required(request.OwnerUserId)}:{Required(request.ChallengeId)}",
        DeploymentQueueKind.TrainingContainer =>
            $"training-container:{Required(request.OwnerUserId)}:{Required(request.ChallengeId)}",
        DeploymentQueueKind.AwdpContainer =>
            $"awdp-container:{Required(request.OwnerTeamId)}:{Required(request.AwdpServiceInstanceId)}",
        DeploymentQueueKind.ChallengeTestContainer =>
            request.SubjectType == "challenge-test-container"
                ? $"challenge-test-container:{Required(request.GameId)}:{Required(request.ChallengeId)}"
                : $"runtime-container:{RequiredText(request.SubjectPublicId)}",
        DeploymentQueueKind.VirtualMachine =>
            $"vm:{Required(request.GameId)}:{Required(request.OwnerUserId)}:{Required(request.ChallengeId)}:{Required(request.VmInstanceId)}",
        DeploymentQueueKind.TeamLabRuntime =>
            $"teamlab-runtime:{Required(request.TeamLabRuntimeId)}",
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unknown deployment queue kind.")
    };

    static T Required<T>(T? value) where T : struct =>
        value ?? throw new InvalidOperationException("Deployment queue request is missing a required identity field.");

    static string RequiredText(string? value) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException("Deployment queue request is missing a required text identity field.");
}

public enum DeploymentQueueKind : byte
{
    GameContainer = 1,
    ExerciseContainer = 2,
    TrainingContainer = 3,
    AwdpContainer = 4,
    ChallengeTestContainer = 5,
    VirtualMachine = 6,
    TeamLabRuntime = 7
}

public enum DeploymentQueueTicketStatus : byte
{
    Pending = 0,
    Scheduling = 1,
    Scheduled = 2,
    Running = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6
}

public enum RuntimeOperationKind : byte
{
    Create = 1,
    Extend = 2,
    Stop = 3,
    Reset = 4,
    Destroy = 5
}

public enum DeploymentStage : byte
{
    Queued = 0,
    AdmissionChecking = 1,
    CapacityWaiting = 2,
    ImagePreparing = 3,
    ImagePulling = 4,
    ImageVerifying = 5,
    NodeExecutionWaiting = 6,
    ContainerCreating = 7,
    VmCreating = 8,
    RuntimeNetworkApplying = 9,
    RuntimeAssetsCreating = 10,
    BootProbing = 11,
    AccessOpening = 12,
    Extending = 13,
    Stopping = 14,
    Destroying = 15,
    RollingBack = 16,
    Ready = 17,
    Failed = 18,
    Cancelled = 19
}
