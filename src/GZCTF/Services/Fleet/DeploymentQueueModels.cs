using GZCTF.Models.Data;

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
    Guid? ApiOperationId = null,
    string? SubjectType = null,
    string? SubjectPublicId = null,
    string? SubjectDisplayName = null,
    string? ResourceDisplayName = null)
{
    public static DeploymentQueueRequest GameContainer(int gameId, int teamId, int challengeId) =>
        new(DeploymentQueueKind.GameContainer, teamId, null, gameId, challengeId,
            null, null, 1, 0);

    public static DeploymentQueueRequest ExerciseContainer(Guid userId, int challengeId) =>
        new(DeploymentQueueKind.ExerciseContainer, null, userId, null, challengeId,
            null, null, 1, 0);

    public static DeploymentQueueRequest Vm(int gameId, Guid userId, int challengeId, Guid vmInstanceId) =>
        new(DeploymentQueueKind.Vm, null, userId, gameId, challengeId,
            vmInstanceId, null, 0, 1);

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
            null, runtimeId, dockerSlots, vmSlots, apiOperationId,
            "teamlab-runtime", runtimePublicId?.ToString("D"), subjectDisplayName, resourceDisplayName);
}

public sealed record DeploymentQueueStatusModel(
    Guid TicketId,
    DeploymentQueueKind Kind,
    DeploymentQueueTicketStatus Status,
    Guid? TargetNodeId,
    string? TargetNodeName,
    int QueuePosition,
    int PeopleAhead,
    string? ErrorMessage,
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
            ticket.TargetNodeId,
            ticket.TargetNode?.Name,
            normalizedPosition,
            Math.Max(0, normalizedPosition - 1),
            ticket.ErrorMessage,
            ticket.CreatedAt,
            ticket.StartedAt,
            ticket.CompletedAt);
    }
}
