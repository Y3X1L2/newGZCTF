using GZCTF.Agent.Services;
using GZCTF.TeamLab.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/teamlab/diagnostics")]
public sealed class TeamLabDiagnosticsController(DockerService docker, KvmService kvm) : ControllerBase
{
    [HttpPost("container/files")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public Task<TeamLabFileResult> Files(TeamLabContainerFileRequest request, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        return docker.ManageTeamLabFilesAsync(request, token);
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
