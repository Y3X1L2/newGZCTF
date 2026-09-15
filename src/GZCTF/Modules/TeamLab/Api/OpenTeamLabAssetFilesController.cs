using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;
using System.Net.Http.Headers;

namespace GZCTF.Modules.TeamLab.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[OpenApiTags("TeamLab - Asset files")]
[Route("api/open/v1/teamlab/runtimes/{runtimeId:guid}/assets/{assetId:int}/files")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType(typeof(ExternalApiProblemDetailsModel), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public sealed class OpenTeamLabAssetFilesController(TeamLabAssetFileService files) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsRead)]
    [OpenApiOperation("列出资产文件", "列出授权运行时当前代资产中指定目录的直接子项。")]
    [ProducesResponseType(typeof(OpenTeamLabAssetFileListModel), StatusCodes.Status200OK)]
    public async Task<OpenTeamLabAssetFileListModel> List(
        Guid runtimeId,
        int assetId,
        [FromQuery, Range(1, int.MaxValue)] int generation,
        [FromQuery, Required, StringLength(1024, MinimumLength = 1)] string path,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        Response.Headers.CacheControl = "no-store";
        return (await files.ExecuteApiAsync(
            runtimeId,
            assetId,
            actor.TokenId,
            actor.UserId,
            new TeamLabAssetFileCommand(generation, "list", path),
            cancellationToken)).ToOpenFileList();
    }

    [HttpGet("download")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsRead)]
    [OpenApiOperation("下载资产文件", "以二进制流下载授权运行时当前代资产中的单个文件。")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, MediaTypeNames.Application.Octet)]
    public async Task<IActionResult> Download(
        Guid runtimeId,
        int assetId,
        [FromQuery, Range(1, int.MaxValue)] int generation,
        [FromQuery, Required, StringLength(1024, MinimumLength = 1)] string path,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.ContentType = MediaTypeNames.Application.Octet;
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            { FileNameStar = System.IO.Path.GetFileName(path) }.ToString();
        await files.DownloadApiAsync(runtimeId, assetId, actor.TokenId, actor.UserId,
            generation, path, Response.Body, cancellationToken);
        return new EmptyResult();
    }

    [HttpPost("upload")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsWrite)]
    [Consumes(MediaTypeNames.Application.Octet)]
    [DisableRequestSizeLimit]
    [OpenApiOperation("上传资产文件", "以 multipart 二进制流上传单个文件；覆盖已有文件时必须显式确认。")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Upload(
        Guid runtimeId,
        int assetId,
        [FromQuery] OpenUploadTeamLabAssetFileModel model,
        CancellationToken cancellationToken)
    {
        var contentLength = Request.ContentLength
            ?? throw new TeamLabApiContractException("files.length_required", "上传文件必须提供 Content-Length。", 411);
        var actor = Actor();
        await files.UploadApiAsync(runtimeId, assetId, actor.TokenId, actor.UserId,
            model.Generation, model.Path, Request.Body, contentLength, model.Overwrite, model.Confirmed,
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [HttpPost("directories")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsWrite)]
    [OpenApiOperation("创建资产目录", "在授权运行时当前代资产中创建目录。")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CreateDirectory(
        Guid runtimeId,
        int assetId,
        OpenCreateTeamLabAssetDirectoryModel model,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await files.ExecuteApiAsync(
            runtimeId,
            assetId,
            actor.TokenId,
            actor.UserId,
            new TeamLabAssetFileCommand(model.Generation, "mkdir", model.Path),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [HttpPost("move")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsWrite)]
    [OpenApiOperation("移动资产文件", "在同一授权资产内移动或重命名文件；覆盖目标时必须显式确认。")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Move(
        Guid runtimeId,
        int assetId,
        OpenMoveTeamLabAssetFileModel model,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await files.ExecuteApiAsync(
            runtimeId,
            assetId,
            actor.TokenId,
            actor.UserId,
            new TeamLabAssetFileCommand(
                model.Generation,
                "move",
                model.SourcePath,
                Overwrite: model.Overwrite,
                Confirmed: model.Confirmed,
                DestinationPath: model.DestinationPath),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    [HttpDelete]
    [Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabRemoteSessionsWrite)]
    [OpenApiOperation("删除资产文件", "删除授权运行时当前代资产中的文件或目录，必须显式确认。")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(
        Guid runtimeId,
        int assetId,
        [FromQuery, Range(1, int.MaxValue)] int generation,
        [FromQuery, Required, StringLength(1024, MinimumLength = 1)] string path,
        [FromQuery] bool recursive,
        [FromQuery] bool confirmed,
        CancellationToken cancellationToken)
    {
        var actor = Actor();
        await files.ExecuteApiAsync(
            runtimeId,
            assetId,
            actor.TokenId,
            actor.UserId,
            new TeamLabAssetFileCommand(
                generation,
                "delete",
                path,
                Confirmed: confirmed,
                Recursive: recursive),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }

    private (Guid TokenId, Guid UserId) Actor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return (tokenId, userId);
        throw new TeamLabApiContractException("authentication_required", "需要身份验证。", 401);
    }
}
