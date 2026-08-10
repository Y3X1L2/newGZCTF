using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabRuntimeApplicationService
{
    Task<TeamLabRuntimeCreateResult> PlanAndEnqueueAsync(
        CreateTeamLabRuntimeModel command,
        Guid actorUserId,
        Guid runtimeOwnerUserId,
        string requestHash,
        string? creationIdempotencyKey,
        Guid? operationId,
        string? subjectDisplayName,
        CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> GetAsync(Guid runtimeId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> GetByStorageIdAsync(int runtimeId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> PauseAsync(Guid runtimeId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> ResumeAsync(Guid runtimeId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> PauseRolloutTargetAsync(Guid runtimeId, int rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> ResumeRolloutTargetAsync(Guid runtimeId, int rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> ResetRolloutTargetAsync(Guid runtimeId, int rolloutId, Guid? operationId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeCreateResult> ResetRolloutTargetAndEnqueueAsync(
        Guid runtimeId,
        int rolloutId,
        ResetTeamLabRuntimeModel command,
        Guid? operationId,
        CancellationToken cancellationToken);
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
    Task<TeamLabQueueTicketResult> EnqueuePlannedRuntimeAsync(
        Guid runtimeId,
        Guid actorUserId,
        Guid operationId,
        string? subjectDisplayName,
        CancellationToken cancellationToken);
    Task<TeamLabQueueTicketResult> DestroyAndEnqueueAsync(
        Guid runtimeId,
        Guid? operationId,
        Guid? actorUserId,
        CancellationToken cancellationToken);
    Task<TeamLabQueueTicketResult> DestroyRolloutTargetAndEnqueueAsync(
        Guid runtimeId,
        int rolloutId,
        Guid? operationId,
        Guid actorUserId,
        CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ExecuteQueuedDestroyAsync(int runtimeId, CancellationToken cancellationToken);
    Task<TeamLabRuntimeProjectionModel> DestroyAsync(Guid runtimeId, CancellationToken cancellationToken);
}
