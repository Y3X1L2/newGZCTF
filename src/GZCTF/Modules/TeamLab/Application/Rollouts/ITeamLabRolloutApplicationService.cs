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
    Task<TeamLabRolloutModel> EnsureAsync(
        Guid releaseId,
        Guid ownerUserId,
        Guid createdByUserId,
        string adapterKind,
        string externalReference,
        CancellationToken cancellationToken);
    Task<TeamLabRolloutModel?> GetAsync(Guid rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestPreparationAsync(Guid rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> SetAccessAsync(Guid rolloutId, bool open, CancellationToken cancellationToken);
    Task<TeamLabRolloutModel> RequestDrainAsync(Guid rolloutId, CancellationToken cancellationToken);
    Task<TeamLabRolloutTargetPageModel> ListTargetsAsync(
        Guid rolloutId,
        string? after,
        int limit,
        CancellationToken cancellationToken);
}
