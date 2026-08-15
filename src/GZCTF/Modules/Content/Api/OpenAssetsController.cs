using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Identity.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Content.Api;

/// <summary>Attachment asset APIs authenticated with personal access tokens.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/assets")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class OpenAssetsController(AssetApplicationService assets) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.AssetsWrite)]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = long.MaxValue)]
    [ProducesResponseType(typeof(AssetDescriptor), StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(
        [FromForm, Required] IFormFile file,
        [FromForm] string? filename,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "asset_empty",
                detail: "Attachment file must not be empty.");

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var creatorId))
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "asset_actor_invalid");

        var asset = await assets.UploadAsync(file, filename, creatorId, cancellationToken);
        return Created($"/api/open/v1/assets/{asset.Hash}", asset);
    }

    [HttpGet("{hash:length(64)}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.AssetsRead)]
    [ProducesResponseType(typeof(AssetDescriptor), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([RegularExpression("[0-9a-f]{64}")] string hash,
        CancellationToken cancellationToken)
    {
        var asset = await assets.FindAsync(hash, cancellationToken);
        return asset is null ? NotFound() : Ok(asset);
    }

    [HttpDelete("{hash:length(64)}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.AssetsDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete([RegularExpression("[0-9a-f]{64}")] string hash,
        CancellationToken cancellationToken)
    {
        var result = await assets.DeleteAsync(hash, cancellationToken);
        return result switch
        {
            AssetDeleteStatus.Success => NoContent(),
            AssetDeleteStatus.NotFound => NotFound(),
            AssetDeleteStatus.InUse => Problem(statusCode: StatusCodes.Status409Conflict, title: "asset_in_use",
                detail: "The attachment is still referenced by a platform resource."),
            _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: "asset_delete_failed")
        };
    }
}
