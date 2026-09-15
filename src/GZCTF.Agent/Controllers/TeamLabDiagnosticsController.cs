using GZCTF.Agent.Services;
using GZCTF.Agent.Models;
using GZCTF.TeamLab.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/teamlab/diagnostics")]
public sealed class TeamLabDiagnosticsController(DockerService docker, KvmService kvm,
    IOptions<AgentTeamLabConfig> options) : ControllerBase
{
    readonly AgentTeamLabConfig teamLab = options.Value;

    [HttpPost("container/files")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public Task<TeamLabFileResult> Files(TeamLabContainerFileRequest request, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return docker.ManageTeamLabFilesAsync(request, token);
    }

    [HttpPost("container/files/download")]
    public async Task DownloadFile(TeamLabContainerFileRequest request, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentType = "application/octet-stream";
        await docker.DownloadTeamLabFileAsync(request with { Operation = "download", Content = null },
            Response.Body, teamLab.MaxFileTransferBytes,
            TimeSpan.FromSeconds(teamLab.FileTransferIdleTimeoutSeconds), token);
    }

    [HttpPost("container/files/upload")]
    [DisableRequestSizeLimit]
    public async Task UploadFile(
        [FromQuery] int runtimeId,
        [FromQuery] int generation,
        [FromQuery] string containerId,
        [FromQuery] string path,
        [FromQuery] bool overwrite,
        CancellationToken token)
    {
        var contentLength = Request.ContentLength
            ?? throw new AgentOperationException("Validation", "files.length_required", "File length is required.", false, 411);
        await docker.UploadTeamLabFileAsync(
            new(runtimeId, generation, containerId, "upload", path, Overwrite: overwrite),
            Request.Body, contentLength, teamLab.MaxFileTransferBytes,
            TimeSpan.FromSeconds(teamLab.FileTransferIdleTimeoutSeconds), token);
        Response.Headers.CacheControl = "no-store";
    }

    [HttpPost("vm")]
    public Task<TeamLabVmDiagnostics> Vm(TeamLabVmDiagnosticsRequest request, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return kvm.GetTeamLabDiagnosticsAsync(request, token);
    }

    [HttpPost("container")]
    public Task<TeamLabContainerDiagnostics> Container(TeamLabContainerDiagnosticsRequest request, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return docker.GetTeamLabDiagnosticsAsync(request, token);
    }
}
