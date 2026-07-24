using System.Net.Http.Headers;
using GZCTF.Modules.TeamLab.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[AllowAnonymous]
[Route("api/internal/teamlab/captures")]
public sealed class InternalTeamLabCaptureUploadController(
    TeamLabCaptureUploadService uploads) : ControllerBase
{
    public const string WorkerNodeHeader = "X-GZCTF-Worker-Node";
    public const string Sha256Header = "X-Content-SHA256";

    [HttpPut("{captureId:guid}/segments/{segmentId:guid}")]
    [DisableRequestSizeLimit]
    [Consumes("application/vnd.tcpdump.pcap", "application/octet-stream")]
    public async Task<IActionResult> Upload(
        Guid captureId,
        Guid segmentId,
        CancellationToken cancellationToken)
    {
        if (!TryBearer(Request, out var bearer) ||
            !Guid.TryParse(Request.Headers[WorkerNodeHeader], out var workerNodeId))
            return Unauthorized();

        var result = await uploads.UploadAsync(
            new TeamLabCaptureSegmentUploadCommand(
                captureId,
                segmentId,
                workerNodeId,
                bearer,
                Request.Headers[Sha256Header].ToString(),
                Request.ContentLength,
                Request.Body),
            cancellationToken);

        if (result.StatusCode == StatusCodes.Status200OK)
            return Ok(new { segmentId, uploaded = true, alreadyExists = result.AlreadyExists });
        if (result.StatusCode == StatusCodes.Status401Unauthorized)
            return Unauthorized();

        var problem = new { code = result.Code };
        return result.StatusCode switch
        {
            StatusCodes.Status400BadRequest => BadRequest(problem),
            StatusCodes.Status404NotFound => NotFound(problem),
            StatusCodes.Status409Conflict => Conflict(problem),
            _ => StatusCode(result.StatusCode, problem)
        };
    }

    private static bool TryBearer(HttpRequest request, out string token)
    {
        token = string.Empty;
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
            return false;
        token = header.Parameter;
        return true;
    }
}
