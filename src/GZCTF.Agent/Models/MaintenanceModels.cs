namespace GZCTF.Agent.Models;

public record AgentSyncRequest(
    string DownloadUrl,
    string? ExpectedSha256 = null,
    bool Restart = true);

public record AgentSyncResponse(
    bool Success,
    string Message,
    string? AgentVersion);
