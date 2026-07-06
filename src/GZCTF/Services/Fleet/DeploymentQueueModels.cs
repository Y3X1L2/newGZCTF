using GZCTF.Models.Data;

namespace GZCTF.Services.Fleet;

public sealed record DeploymentQueueRequest(
    DeploymentQueueKind Kind,
    int? OwnerTeamId,
    Guid? OwnerUserId,
    int? GameId,
    int? ChallengeId,
    Guid? VmInstanceId,
    int? TeamLabRuntimeId,
    int DockerSlots,
    int VmSlots)
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

    public static DeploymentQueueRequest TeamLab(int gameId, int teamId, int runtimeId, int dockerSlots, int vmSlots) =>
        new(DeploymentQueueKind.TeamLabRuntime, teamId, null, gameId, null,
            null, runtimeId, dockerSlots, vmSlots);
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
