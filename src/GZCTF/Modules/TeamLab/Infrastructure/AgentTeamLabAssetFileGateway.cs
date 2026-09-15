using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class AgentTeamLabAssetFileGateway(AgentClient agent) : ITeamLabAssetFileGateway
{
    public Task<TeamLabFileResult> ExecuteVmAsync(Guid nodeId, TeamLabVmFileRequest request, CancellationToken token) =>
        TranslateAsync(() => agent.ManageTeamLabVmFilesAsync(nodeId, request, token));
    public Task<TeamLabFileResult> ExecuteAsync(Guid nodeId, TeamLabContainerFileRequest request, CancellationToken token) =>
        TranslateAsync(() => agent.ManageTeamLabContainerFilesAsync(nodeId, request, token));
    public Task DownloadAsync(Guid nodeId, TeamLabContainerFileRequest request, Stream destination,
        long maxBytes, TimeSpan idleTimeout, CancellationToken token) =>
        TranslateAsync(() => agent.DownloadTeamLabContainerFileAsync(nodeId, request, destination,
            maxBytes, idleTimeout, token));
    public Task DownloadVmAsync(Guid nodeId, TeamLabVmFileRequest request, Stream destination,
        long maxBytes, TimeSpan idleTimeout, CancellationToken token) =>
        TranslateAsync(() => agent.DownloadTeamLabVmFileAsync(nodeId, request, destination,
            maxBytes, idleTimeout, token));
    public Task UploadAsync(Guid nodeId, TeamLabContainerFileRequest request, Stream source,
        long contentLength, CancellationToken token) =>
        TranslateAsync(() => agent.UploadTeamLabContainerFileAsync(nodeId, request, source, contentLength, token));
    public Task UploadVmAsync(Guid nodeId, TeamLabVmFileRequest request, Stream source,
        long contentLength, CancellationToken token) =>
        TranslateAsync(() => agent.UploadTeamLabVmFileAsync(nodeId, request, source, contentLength, token));

    private static async Task<TeamLabFileResult> TranslateAsync(Func<Task<TeamLabFileResult>> execute)
    {
        try { return await execute(); }
        catch (AgentClientException error)
        {
            if (!error.Error.Code.StartsWith("files.", StringComparison.Ordinal))
                throw new TeamLabApiContractException("files.node_unavailable", "节点文件服务不可用，请检查节点连接。", 503);
            var status = error.Error.Code switch
            {
                "files.not_found" => 404,
                "files.permission_denied" => 403,
                "files.too_large" => 413,
                "files.invalid_request" => 422,
                "files.storage_full" => 507,
                _ => 409
            };
            throw new TeamLabApiContractException(error.Error.Code, error.Error.Message, status);
        }
    }

    private static async Task TranslateAsync(Func<Task> execute)
    {
        try { await execute(); }
        catch (AgentClientException error)
        {
            if (!error.Error.Code.StartsWith("files.", StringComparison.Ordinal))
                throw new TeamLabApiContractException("files.node_unavailable", "节点文件服务不可用，请检查节点连接。", 503);
            var status = error.Error.Code switch
            {
                "files.not_found" => 404,
                "files.permission_denied" => 403,
                "files.too_large" => 413,
                "files.length_required" => 411,
                "files.invalid_request" => 422,
                "files.storage_full" => 507,
                _ => 409
            };
            throw new TeamLabApiContractException(error.Error.Code, error.Error.Message, status);
        }
    }
}
