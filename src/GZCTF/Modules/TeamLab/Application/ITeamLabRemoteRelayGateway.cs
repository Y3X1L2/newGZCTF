using System.Net.WebSockets;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record TeamLabRemoteRelayRequest(
    Guid SessionId,
    int RuntimeId,
    int Generation,
    string RuntimeResourceId,
    string NativeIdentity,
    string TargetAddress,
    int TargetPort,
    DateTimeOffset ExpiresAt);

public sealed record TeamLabRemoteRelayResult(int Port, DateTimeOffset ExpiresAt);

public interface ITeamLabRemoteRelayGateway
{
    Task<TeamLabRemoteRelayResult> CreateAsync(
        Guid workerNodeId,
        TeamLabRemoteRelayRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid workerNodeId,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task ProxyTerminalAsync(
        Guid workerNodeId,
        Guid sessionId,
        int runtimeId,
        int generation,
        string runtimeResourceId,
        DateTimeOffset expiresAt,
        WebSocket socket,
        CancellationToken cancellationToken);
}
