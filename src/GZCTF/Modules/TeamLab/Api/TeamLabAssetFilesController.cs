using GZCTF.Middlewares;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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
        var result = await files.ExecuteAsync(runtimeId, assetId, actor.Id, actor.Role >= Role.Admin,
            new(generation, "download", path), token);
        return File(result.Content ?? throw new TeamLabApiContractException("files.content_unavailable", "节点未返回文件内容。", 502),
            "application/octet-stream", System.IO.Path.GetFileName(path));
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
