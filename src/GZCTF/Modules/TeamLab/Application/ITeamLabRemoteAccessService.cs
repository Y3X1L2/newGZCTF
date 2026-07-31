using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using System.Net.WebSockets;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabRemoteAccessService
{
    Task<TeamLabRemoteAccessAvailabilityModel> GetAvailabilityAsync(Guid runtimeId, int assetId, Guid actorId, bool administrator, CancellationToken cancellationToken);
    Task<TeamLabRemoteSessionModel> CreateAsync(Guid runtimeId, int assetId, Guid actorId, bool administrator, string reason, CancellationToken cancellationToken);
    Task<TeamLabRemoteSessionModel> GetAsync(Guid sessionId, Guid actorId, bool administrator, CancellationToken cancellationToken);
    Task<TeamLabRemoteConnectModel> ConnectAsync(Guid sessionId, Guid actorId, bool administrator, CancellationToken cancellationToken);
    Task ProxyTerminalAsync(Guid sessionId, Guid actorId, bool administrator, WebSocket socket, CancellationToken cancellationToken);
    Task EndAsync(Guid sessionId, Guid actorId, bool administrator, string reason, CancellationToken cancellationToken);
    Task ExpireAsync(CancellationToken cancellationToken);
    Task EndRuntimeSessionsAsync(int runtimeId, int generation, string reason, CancellationToken cancellationToken);
}
