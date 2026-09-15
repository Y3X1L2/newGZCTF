using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace GZCTF.Modules.TeamLab.Api;

[RequireTeacher]
[ApiController]
[Route("api/admin/teamlab/runtimes/{runtimeId:guid}/assets/{assetId:int}/files")]
public sealed class TeamLabAssetFilesController(TeamLabAssetFileService files, UserManager<UserInfo> users) : ControllerBase
{
    [HttpGet("download")]
    public async Task<IActionResult> Download(Guid runtimeId, int assetId, [FromQuery] int generation, [FromQuery] string path, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User)
            ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        Response.Headers.CacheControl = "no-store";
        Response.ContentType = "application/octet-stream";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            { FileNameStar = System.IO.Path.GetFileName(path) }.ToString();
        await files.DownloadAsync(runtimeId, assetId, actor.Id, actor.Role >= Role.Admin,
            generation, path, Response.Body, token);
        return new EmptyResult();
    }

    [HttpPost("upload")]
    [Consumes("application/octet-stream")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(Guid runtimeId, int assetId,
        [FromQuery] OpenUploadTeamLabAssetFileModel model, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User)
            ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        var contentLength = Request.ContentLength
            ?? throw new TeamLabApiContractException("files.length_required", "上传文件必须提供 Content-Length。", 411);
        await files.UploadAsync(runtimeId, assetId, actor.Id, actor.Role >= Role.Admin,
            model.Generation, model.Path, Request.Body, contentLength, model.Overwrite, model.Confirmed, token);
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [HttpPost]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<TeamLabFileResult> Execute(Guid runtimeId, int assetId, TeamLabAssetFileCommand command, CancellationToken token)
    {
        var actor = await users.GetUserAsync(User)
            ?? throw new TeamLabApiContractException("authentication_required", "需要身份认证。", 401);
        Response.Headers.CacheControl = "no-store";
        return await files.ExecuteAsync(runtimeId, assetId, actor.Id, actor.Role >= Role.Admin, command, token);
    }
}
