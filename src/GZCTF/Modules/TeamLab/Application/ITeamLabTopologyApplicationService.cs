using GZCTF.Modules.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabTopologyApplicationService
{
    TeamLabCapabilitiesModel GetCapabilities();
    Task<TeamLabTopologyDetailModel> CreateAsync(CreateTeamLabTopologyModel model, Guid actorUserId, CancellationToken cancellationToken);
    Task<TeamLabTopologyDetailModel> CreateDraftAsync(CreateTeamLabTopologyModel model, Guid actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamLabTopologySummaryModel>> ListAsync(Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<OpenTeamLabTopologyPageModel> ListPageAsync(Guid actorUserId, bool includeAll, int limit, string? after, CancellationToken cancellationToken);
    Task<TeamLabTopologyDetailModel> GetAsync(Guid topologyId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabTopologyDetailModel> UpdateAsync(Guid topologyId, UpdateTeamLabTopologyModel model, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabTopologyDetailModel> UpdateDraftAsync(Guid topologyId, UpdateTeamLabTopologyModel model, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task DeleteAsync(Guid topologyId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabValidationResultModel> ValidateAsync(Guid topologyId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabReleaseModel> PublishAsync(Guid topologyId, int revision, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamLabReleaseModel>> ListReleasesAsync(Guid topologyId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<OpenTeamLabReleasePageModel> ListReleasesPageAsync(Guid topologyId, Guid actorUserId, bool includeAll, int limit, string? after, CancellationToken cancellationToken);
    Task<TeamLabReleaseModel> GetReleaseAsync(Guid topologyId, Guid releaseId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabPlanModel> PlanAsync(Guid topologyId, Guid releaseId, Guid actorUserId, bool includeAll, CancellationToken cancellationToken);
    Task<TeamLabTopologyDetailModel> CreateForOperationAsync(CreateTeamLabTopologyModel model, Guid actorUserId, Guid operationId, CancellationToken cancellationToken);
    Task<TeamLabTopologyDetailModel> UpdateForOperationAsync(Guid topologyId, UpdateTeamLabTopologyModel model, Guid actorUserId, bool includeAll, Guid operationId, CancellationToken cancellationToken);
    Task DeleteForOperationAsync(Guid topologyId, Guid actorUserId, bool includeAll, Guid operationId, CancellationToken cancellationToken);
    Task<TeamLabReleaseModel> PublishForOperationAsync(Guid topologyId, int revision, Guid actorUserId, bool includeAll, Guid operationId, IReadOnlyList<TeamLabRuntimeOverlayModel>? scenarioOverlays, CancellationToken cancellationToken);
}
