using System.Net.WebSockets;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class AgentTeamLabRemoteRelayGateway(AgentClient agent) : ITeamLabRemoteRelayGateway
{
    public async Task<TeamLabRemoteRelayResult> CreateAsync(
        Guid workerNodeId,
        TeamLabRemoteRelayRequest request,
        CancellationToken cancellationToken)
    {
        var relay = await agent.CreateRemoteRelayAsync(workerNodeId, new AgentRemoteRelayRequest(
            request.SessionId,
            request.RuntimeId,
            request.Generation,
            request.RuntimeResourceId,
            request.NativeIdentity,
            request.TargetAddress,
            request.TargetPort,
            request.ExpiresAt), cancellationToken);
        return new TeamLabRemoteRelayResult(relay.Port, relay.ExpiresAt);
    }

    public Task DeleteAsync(
        Guid workerNodeId,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        agent.DeleteRemoteRelayAsync(workerNodeId, sessionId, cancellationToken);

    public Task CancelTerminalAsync(
        Guid workerNodeId,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        agent.CancelRemoteTerminalAsync(workerNodeId, sessionId, cancellationToken);

    public Task ProxyTerminalAsync(
        Guid workerNodeId,
        Guid sessionId,
        int runtimeId,
        int generation,
        string runtimeResourceId,
        DateTimeOffset expiresAt,
        WebSocket socket,
        CancellationToken cancellationToken) =>
        agent.ProxyRemoteTerminalAsync(workerNodeId, sessionId, runtimeId, generation,
            runtimeResourceId, expiresAt, socket, cancellationToken);
}
