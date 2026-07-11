using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabTopologyApplicationService
{
    TeamLabCapabilitiesModel GetCapabilities();
    Task<TeamLabTopologyDetailModel> CreateAsync(CreateTeamLabTopologyModel model, Guid actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamLabTopologySummaryModel>> ListAsync(Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabTopologyDetailModel> GetAsync(Guid topologyId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabTopologyDetailModel> UpdateAsync(Guid topologyId, UpdateTeamLabTopologyModel model, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task DeleteAsync(Guid topologyId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabValidationResultModel> ValidateAsync(Guid topologyId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabReleaseModel> PublishAsync(Guid topologyId, int revision, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamLabReleaseModel>> ListReleasesAsync(Guid topologyId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabReleaseModel> GetReleaseAsync(Guid topologyId, Guid releaseId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabPlanModel> PlanAsync(Guid topologyId, Guid releaseId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
}
