using System.Net;
using GZCTF.Agent.Services;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text.Json;

namespace GZCTF.Agent.Controllers;

[ApiController]
[Route("api/teamlab/vm-files")]
public sealed class TeamLabVmFilesController(KvmService kvm, IOptions<AgentTeamLabConfig> options) : ControllerBase
{
    readonly AgentTeamLabConfig teamLab = options.Value;

    [HttpPost]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<TeamLabFileResult> Execute(TeamLabVmFileRequest request, CancellationToken token)
    {
        var address = await ResolveManagementAddressAsync(request, token);
        Response.Headers.CacheControl = "no-store";
        return await SftpAssetFileStore.ExecuteAsync(address.ToString(), request, token);
    }

    [HttpPost("download")]
    public async Task Download(TeamLabVmFileRequest request, CancellationToken token)
    {
        var address = await ResolveManagementAddressAsync(request, token);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentType = "application/octet-stream";
        await SftpAssetFileStore.DownloadToAsync(address.ToString(), request with { Operation = "download", Content = null },
            Response.Body, teamLab.MaxFileTransferBytes,
            TimeSpan.FromSeconds(teamLab.FileTransferIdleTimeoutSeconds), token);
    }

    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    public async Task Upload(CancellationToken token)
    {
        var mediaType = MediaTypeHeaderValue.Parse(Request.ContentType);
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary))
            throw new AgentOperationException("Validation", "files.invalid_request", "Multipart boundary is required.", false, 422);
        var reader = new MultipartReader(boundary, Request.Body);
        var metadata = await reader.ReadNextSectionAsync(token)
            ?? throw new AgentOperationException("Validation", "files.invalid_request", "File metadata is required.", false, 422);
        var request = await JsonSerializer.DeserializeAsync<TeamLabVmFileRequest>(metadata.Body,
            cancellationToken: token)
            ?? throw new AgentOperationException("Validation", "files.invalid_request", "File metadata is invalid.", false, 422);
        var file = await reader.ReadNextSectionAsync(token)
            ?? throw new AgentOperationException("Validation", "files.invalid_request", "File content is required.", false, 422);
        if (file.Headers is null || !file.Headers.TryGetValue(HeaderNames.ContentLength, out var lengths) ||
            !long.TryParse(lengths.ToString(), out var contentLength))
            throw new AgentOperationException("Validation", "files.length_required", "File length is required.", false, 411);
        var address = await ResolveManagementAddressAsync(request, token);
        await SftpAssetFileStore.UploadFromAsync(address.ToString(), request with { Operation = "upload", Content = null },
            file.Body, contentLength, teamLab.MaxFileTransferBytes,
            TimeSpan.FromSeconds(teamLab.FileTransferIdleTimeoutSeconds), token);
        Response.Headers.CacheControl = "no-store";
    }

    async Task<IPAddress> ResolveManagementAddressAsync(TeamLabVmFileRequest request, CancellationToken token)
    {
        var guest = await kvm.ExecuteWithIdentityAsync(request.DomainName, request.Generation, request.NativeId.ToString("D"),
            ct => kvm.GetIpAddressWithDiagnosticAsync(request.DomainName, ct), token);
        if (!IPAddress.TryParse(guest.IpAddress, out var actual) || !IPAddress.TryParse(request.GuestAddress, out var expected) || !actual.Equals(expected))
            throw new AgentOperationException("Conflict", "files.identity_mismatch", "VM guest address does not match its bound identity.", false, 409);
        var management = await kvm.ExecuteWithIdentityAsync(request.DomainName, request.Generation, request.NativeId.ToString("D"),
            ct => kvm.GetManagementIpAddressWithDiagnosticAsync(request.DomainName, ct), token);
        return IPAddress.TryParse(management.IpAddress, out var address)
            ? address
            : throw new AgentOperationException("FileAccess", "files.management_unavailable", "VM management address is unavailable.", false, 409);
    }
}
