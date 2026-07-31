namespace GZCTF.Agent.Models;

public sealed record CreateRemoteRelayRequest(
    Guid SessionId,
    int RuntimeId,
    int Generation,
    string VmName,
    string NativeId,
    string TargetAddress,
    int TargetPort,
    DateTimeOffset ExpiresAt);

public sealed record RemoteRelayResponse(Guid SessionId, int Port, DateTimeOffset ExpiresAt);

public sealed record TeamLabTerminalRequest(
    Guid SessionId,
    int RuntimeId,
    int Generation,
    string ContainerId,
    DateTimeOffset ExpiresAt);
