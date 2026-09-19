using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using System.Net.WebSockets;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabRemoteAccessService
{
    Task<TeamLabRemoteSessionModel> CreateConsoleAsync(Guid runtimeId, int assetId, Guid actorId, bool administrator, string reason, CancellationToken cancellationToken);
    Task<TeamLabRemoteSessionPage> ListAsync(Guid actorId, bool administrator, Guid? runtimeId,
        string? query, TeamLabRemoteProtocol? protocol, bool abnormalOnly, TeamLabRemoteSessionStatus? status,
        long? after, int limit, CancellationToken cancellationToken);
    Task<OpenTeamLabRemoteSessionPageModel> ListApiAsync(Guid apiTokenId, Guid actorUserId, bool hasWildcardScopeGrant,
        Guid? runtimeId, string? query, TeamLabRemoteProtocol? protocol, bool abnormalOnly,
        TeamLabRemoteSessionStatus? status, long? after, int limit, CancellationToken cancellationToken);
    Task<TeamLabRemoteAccessAvailabilityModel> GetAvailabilityAsync(Guid runtimeId, int assetId, Guid actorId, bool administrator, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamLabRemoteAccessAvailabilityModel>> GetAvailabilityBatchAsync(Guid runtimeId, Guid actorId, bool administrator, CancellationToken cancellationToken);
    Task<IReadOnlyList<TeamLabRemoteAccessAvailabilityModel>> GetAvailabilityBatchApiAsync(Guid runtimeId, Guid actorUserId, Guid apiTokenId, CancellationToken cancellationToken);
    Task<TeamLabRemoteSessionModel> CreateAsync(Guid runtimeId, int assetId, Guid actorId, bool administrator, string reason, CancellationToken cancellationToken);
    Task<TeamLabRemoteSessionModel> CreateForOperationAsync(Guid runtimeId, int assetId, Guid actorId,
        string reason, Guid operationId, CancellationToken cancellationToken, bool vncConsole = false);
    Task<TeamLabRemoteSessionModel> CreateForApiOperationAsync(Guid runtimeId, int assetId, Guid actorId,
        Guid apiTokenId, string reason, Guid operationId, CancellationToken cancellationToken, bool vncConsole = false);
    Task<TeamLabRemoteSessionModel> GetAsync(Guid sessionId, Guid actorId, bool administrator, CancellationToken cancellationToken);
    Task<TeamLabRemoteSessionModel> GetApiAsync(Guid sessionId, Guid actorUserId, Guid apiTokenId, CancellationToken cancellationToken);
    Task<TeamLabRemoteConnectModel> ConnectAsync(Guid sessionId, Guid actorId, bool administrator, CancellationToken cancellationToken);
    Task<TeamLabRemoteConnectModel> ConnectApiAsync(Guid sessionId, Guid actorId, Guid apiTokenId, CancellationToken cancellationToken);
    Task ProxyTerminalAsync(Guid sessionId, Guid actorId, bool administrator, WebSocket socket, CancellationToken cancellationToken);
    Task ProxyTerminalApiAsync(Guid sessionId, Guid actorId, Guid apiTokenId, WebSocket socket, CancellationToken cancellationToken);
    Task EndAsync(Guid sessionId, Guid actorId, bool administrator, string reason, CancellationToken cancellationToken);
    Task EndApiAsync(Guid sessionId, Guid actorId, Guid apiTokenId, string reason, CancellationToken cancellationToken);
    Task ExpireAsync(CancellationToken cancellationToken);
    Task EndRuntimeSessionsAsync(int runtimeId, int generation, string reason, CancellationToken cancellationToken);
    Task EndAssetSessionsAsync(int runtimeId, int assetId, int generation, string reason, CancellationToken cancellationToken);
    Task MarkInterruptedAsync(CancellationToken cancellationToken);
}
