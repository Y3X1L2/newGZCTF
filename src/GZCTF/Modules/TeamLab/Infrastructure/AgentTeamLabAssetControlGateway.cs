using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class AgentTeamLabAssetControlGateway(AgentClient agent) : ITeamLabAssetControlGateway
{
    public Task<TeamLabAssetControlResult> ExecuteAsync(Guid nodeId, TeamLabAssetControlRequest request, CancellationToken token) =>
        agent.ControlTeamLabAssetAsync(nodeId, request, token);
}
