using System.Net;
using GZCTF.Agent.Services;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/teamlab/vm-files")]
public sealed class TeamLabVmFilesController(KvmService kvm) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<TeamLabFileResult> Execute(TeamLabVmFileRequest request, CancellationToken token)
    {
        var guest = await kvm.ExecuteWithIdentityAsync(request.DomainName, request.Generation, request.NativeId.ToString("D"),
            ct => kvm.GetIpAddressWithDiagnosticAsync(request.DomainName, ct), token);
        if (!IPAddress.TryParse(guest.IpAddress, out var actual) || !IPAddress.TryParse(request.GuestAddress, out var expected) || !actual.Equals(expected))
            throw new AgentOperationException("Conflict", "files.identity_mismatch", "VM guest address does not match its bound identity.", false, 409);
        var management = await kvm.ExecuteWithIdentityAsync(request.DomainName, request.Generation, request.NativeId.ToString("D"),
            ct => kvm.GetManagementIpAddressWithDiagnosticAsync(request.DomainName, ct), token);
        if (!IPAddress.TryParse(management.IpAddress, out var address))
            throw new AgentOperationException("FileAccess", "files.management_unavailable", "VM management address is unavailable.", false, 409);
        Response.Headers.CacheControl = "no-store";
        return await SftpAssetFileStore.ExecuteAsync(address.ToString(), request, token);
    }
}
