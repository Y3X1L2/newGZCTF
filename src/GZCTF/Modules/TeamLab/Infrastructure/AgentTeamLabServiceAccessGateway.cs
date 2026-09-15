using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class AgentTeamLabServiceAccessGateway(AgentClient agent) : ITeamLabServiceAccessGateway
{
    public Task ApplyAsync(Guid nodeId, TeamLabServiceForwardRequest request, CancellationToken token) =>
        agent.ApplyTeamLabServiceAccessAsync(nodeId, request, token);
    public Task RemoveAsync(Guid nodeId, TeamLabServiceForwardRequest request, CancellationToken token) =>
        agent.RemoveTeamLabServiceAccessAsync(nodeId, request, token);
}
