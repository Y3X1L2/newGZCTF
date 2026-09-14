using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class AgentTeamLabConnectorNodeGateway(AgentClient agent) : ITeamLabConnectorNodeGateway
{
    public Task<IReadOnlyList<TeamLabHostInterface>> GetInterfacesAsync(Guid nodeId, CancellationToken token) =>
        agent.GetTeamLabHostInterfacesAsync(nodeId, token);
}
