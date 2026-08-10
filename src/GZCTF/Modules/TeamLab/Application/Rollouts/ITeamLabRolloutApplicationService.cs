using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Application.Rollouts;

public sealed record TeamLabRolloutProvisionResult(int RuntimeId, Guid RuntimePublicId, Guid? OperationId);

public interface ITeamLabRolloutTargetProvider
{
    string AdapterKind { get; }
    Task SynchronizeTargetsAsync(TeamLabRollout rollout, CancellationToken cancellationToken);
    Task<TeamLabRolloutProvisionResult> ProvisionAsync(
        TeamLabRollout rollout,
        TeamLabRolloutTarget target,
        CancellationToken cancellationToken);
}

public interface ITeamLabRolloutApplicationService
{
    Task<TeamLabRolloutModel> CreateExternalAsync(
        CreateTeamLabRolloutModel command,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> ReplaceTargetsAsync(
        Guid rolloutId,
        ReplaceTeamLabRolloutTargetsModel command,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestRebuildAsync(
        Guid rolloutId,
        Guid targetId,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> ArchiveAsync(
        Guid rolloutId,
        Guid actorUserId,
        Guid operationId,
        CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> EnsureAsync(
        Guid releaseId,
        Guid ownerUserId,
        Guid createdByUserId,
        string adapterKind,
        string externalReference,
        CancellationToken cancellationToken);
    Task<TeamLabRolloutModel?> GetAsync(Guid rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel?> GetByStorageIdAsync(int rolloutId, CancellationToken cancellationToken);
    Task<int> GetStorageIdAsync(Guid rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRolloutPageModel> ListExternalAsync(
        Guid controlScopeId,
        string? after,
        int limit,
        CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestPreparationAsync(Guid rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestPreparationForOperationAsync(Guid rolloutId, Guid operationId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> SetAccessAsync(Guid rolloutId, bool open, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> SetAccessForOperationAsync(Guid rolloutId, bool open, Guid operationId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestDrainAsync(Guid rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestDrainForOperationAsync(Guid rolloutId, Guid operationId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestPauseAsync(Guid rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestPauseForOperationAsync(Guid rolloutId, Guid operationId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestResumeAsync(Guid rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestResumeForOperationAsync(Guid rolloutId, Guid operationId, CancellationToken cancellationToken);
    Task<TeamLabRolloutTargetPageModel> ListTargetsAsync(
        Guid rolloutId,
        string? after,
        int limit,
        CancellationToken cancellationToken);
    Task<TeamLabRolloutTargetModel?> GetTargetAsync(
        Guid rolloutId,
        Guid targetId,
        CancellationToken cancellationToken);
}
