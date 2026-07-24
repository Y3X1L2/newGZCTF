using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabRuntimeApplicationService
{
    Task<TeamLabRuntimeCreateResult> PlanAndEnqueueAsync(
        CreateTeamLabRuntimeModel command,
        Guid actorUserId,
        Guid runtimeOwnerUserId,
        string requestHash,
        Guid? operationId,
        string? subjectDisplayName,
        CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> GetAsync(Guid runtimeId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeCreateResult> ResetAndEnqueueAsync(
        Guid runtimeId,
        ResetTeamLabRuntimeModel command,
        Guid? operationId,
        CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ExecuteQueuedAsync(int runtimeId, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ExecuteQueuedResetAsync(
        int runtimeId,
        Guid ticketId,
        string? protectedPayload,
        CancellationToken cancellationToken);
    Task<TeamLabQueueTicketResult> DestroyAndEnqueueAsync(
        Guid runtimeId,
        Guid? operationId,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ExecuteQueuedDestroyAsync(int runtimeId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> DestroyAsync(Guid runtimeId, CancellationToken cancellationToken);
}
