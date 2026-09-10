using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class AgentTeamLabAssetDiagnosticsGateway(AgentClient agent) : ITeamLabAssetDiagnosticsGateway
{
    public Task<TeamLabVmDiagnostics> ReadVmAsync(Guid nodeId, TeamLabVmDiagnosticsRequest request, CancellationToken token) =>
        agent.GetTeamLabVmDiagnosticsAsync(nodeId, request, token);
    public Task<TeamLabContainerDiagnostics> ReadAsync(Guid nodeId, TeamLabContainerDiagnosticsRequest request, CancellationToken token) =>
        agent.GetTeamLabContainerDiagnosticsAsync(nodeId, request, token);
}
