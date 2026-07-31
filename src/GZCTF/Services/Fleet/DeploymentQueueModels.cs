using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;

namespace GZCTF.Services.Fleet;

public sealed record TeamLabAssetSlotCount(int DockerSlots, int VmSlots);
public sealed record TeamLabShardSlotCount(Guid WorkerNodeId, int DockerSlots, int VmSlots);

public sealed record DeploymentQueueRequest(
    DeploymentQueueKind Kind,
    int? OwnerTeamId,
    Guid? OwnerUserId,
    int? GameId,
    int? ChallengeId,
    Guid? VmInstanceId,
    int? TeamLabRuntimeId,
    int DockerSlots,
    int VmSlots,
    WorkloadSchedulingIdentity Identity,
    Guid? ApiOperationId = null,
    string? SubjectType = null,
    string? SubjectPublicId = null,
    string? SubjectDisplayName = null,
    string? ResourceDisplayName = null,
    RuntimeOperationKind Operation = RuntimeOperationKind.Create,
    int Generation = 1,
    int? AwdpServiceInstanceId = null,
    Guid? TargetNodeId = null,
    int? ExtensionSeconds = null,
    string? ProtectedPayload = null,
    string? PayloadHash = null)
{
    public static DeploymentQueueRequest GameContainer(int gameId, int teamId, int challengeId) =>
        new(DeploymentQueueKind.GameContainer, teamId, null, gameId, challengeId,
            null, null, 1, 0,
            WorkloadSchedulingIdentity.ForCompetitionTeam(gameId, teamId,
                $"game-container:{gameId}:{teamId}:{challengeId}"));

    public static DeploymentQueueRequest ExerciseContainer(Guid userId, int challengeId) =>
        new(DeploymentQueueKind.ExerciseContainer, null, userId, null, challengeId,
            null, null, 1, 0,
            WorkloadSchedulingIdentity.ForUser(userId, $"exercise-container:{userId:D}:{challengeId}"));

    public static DeploymentQueueRequest TrainingContainer(Guid userId, int challengeId) =>
        new(DeploymentQueueKind.TrainingContainer, null, userId, null, challengeId,
            null, null, 1, 0,
            WorkloadSchedulingIdentity.ForUser(userId, $"training-container:{userId:D}:{challengeId}"));

    public static DeploymentQueueRequest AwdpContainer(int teamId, int instanceId) =>
        new(DeploymentQueueKind.AwdpContainer, teamId, null, null, null,
            null, null, 1, 0,
            WorkloadSchedulingIdentity.ForTeam(teamId, $"awdp-container:{teamId}:{instanceId}"),
            AwdpServiceInstanceId: instanceId);

    public static DeploymentQueueRequest ChallengeTestContainer(
        int gameId,
        int challengeId,
        Guid userId,
        RuntimeOperationKind operation = RuntimeOperationKind.Create,
        Guid? targetNodeId = null,
        string? resourceDisplayName = null,
        int generation = 1) =>
        new(DeploymentQueueKind.ChallengeTestContainer, null, userId, gameId, challengeId,
            null, null, operation == RuntimeOperationKind.Create ? 1 : 0, 0,
            WorkloadSchedulingIdentity.ForUser(userId,
                $"challenge-test-container:{gameId}:{challengeId}", gameId),
            SubjectType: "challenge-test-container",
            SubjectPublicId: $"{gameId}:{challengeId}",
            SubjectDisplayName: "Challenge test runtime",
            ResourceDisplayName: resourceDisplayName,
            Operation: operation,
            Generation: generation,
            TargetNodeId: targetNodeId);

    public static DeploymentQueueRequest MaintenanceContainer(
        Guid containerId,
        Guid? nodeId,
        string? displayName = null,
        int generation = 1) =>
        new(DeploymentQueueKind.ChallengeTestContainer, null, null, null, null,
            null, null, 0, 0,
            WorkloadSchedulingIdentity.ForSystem($"runtime-container:{containerId:D}"),
            SubjectType: "runtime-container",
            SubjectPublicId: containerId.ToString("D"),
            SubjectDisplayName: "System maintenance",
            ResourceDisplayName: displayName,
            Operation: RuntimeOperationKind.Destroy,
            Generation: generation,
            TargetNodeId: nodeId);

    public static DeploymentQueueRequest Vm(int gameId, Guid userId, int challengeId, Guid vmInstanceId) =>
        new(DeploymentQueueKind.VirtualMachine, null, userId, gameId, challengeId,
            vmInstanceId, null, 0, 1,
            WorkloadSchedulingIdentity.ForUser(userId,
                $"vm:{gameId}:{userId:D}:{challengeId}:{vmInstanceId:D}", gameId));

    public static DeploymentQueueRequest TeamLab(
        int runtimeId,
        int dockerSlots,
        int vmSlots,
        Guid? ownerUserId = null,
        Guid? apiOperationId = null,
        Guid? runtimePublicId = null,
        string? subjectDisplayName = null,
        string? resourceDisplayName = null) =>
        new(DeploymentQueueKind.TeamLabRuntime, null, ownerUserId, null, null,
            null, runtimeId, dockerSlots, vmSlots,
            WorkloadSchedulingIdentity.ForRuntime(runtimeId, $"teamlab-runtime:{runtimeId}", ownerUserId),
            apiOperationId,
            "teamlab-runtime", runtimePublicId?.ToString("D"), subjectDisplayName, resourceDisplayName);
}

public sealed record DeploymentQueueStatusModel(
    Guid TicketId,
    DeploymentQueueKind Kind,
    DeploymentQueueTicketStatus Status,
    RuntimeOperationKind Operation,
    DeploymentStage Stage,
    Guid? TargetNodeId,
    string? TargetNodeName,
    int QueuePosition,
    int PeopleAhead,
    string? ErrorMessage,
    string? BlockedReasonCode,
    string? StageMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public static DeploymentQueueStatusModel FromTicket(DeploymentQueueTicket ticket, int queuePosition)
    {
        var normalizedPosition = Math.Max(0, queuePosition);

        return new DeploymentQueueStatusModel(
            ticket.Id,
            ticket.Kind,
            ticket.Status,
            ticket.Operation,
            ticket.Stage,
            ticket.TargetNodeId,
            ticket.TargetNode?.Name,
            normalizedPosition,
            Math.Max(0, normalizedPosition - 1),
            ticket.ErrorMessage,
            ticket.BlockedReasonCode,
            ticket.StageMessage,
            ticket.CreatedAt,
            ticket.StartedAt,
            ticket.CompletedAt);
    }
}
