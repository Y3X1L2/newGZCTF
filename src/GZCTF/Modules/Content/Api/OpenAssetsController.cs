using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using System.Text.Json.Serialization;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Identity.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Content.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/assets")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class OpenAssetsController(
    AssetApplicationService assets,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "scope:" + ApiTokenScopes.AssetsWrite)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(AssetApplicationService.MaxUploadSize + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = AssetApplicationService.MaxUploadSize)]
    [ProducesResponseType(typeof(AssetDescriptor), StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(
        [FromForm] AssetUploadModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        [FromHeader(Name = "Content-Digest"), Required] string contentDigest,
        CancellationToken cancellationToken)
    {
        var (tokenId, actorId) = GetActor();
        var hash = Convert.ToHexStringLower(AssetApplicationService.ParseContentDigest(contentDigest));
        var grant = await authorization.AuthorizeAsync(User, null, new ApiResourceRequirement("asset", hash));
        if (!grant.Succeeded)
            throw new AssetApiContractException("insufficient_permission", "The token does not grant this asset.", 403);
        var result = await assets.UploadAsync(model.File, model.Filename, tokenId, actorId,
            idempotencyKey, contentDigest, cancellationToken);
        Response.Headers["Operation-Location"] = $"/api/open/v1/operations/{result.OperationId}";
        return Created($"/api/open/v1/assets/{result.Asset.Hash}", result.Asset);
    }

    [HttpGet("{hash:length(64)}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.AssetsRead)]
    [ProducesResponseType(typeof(AssetDescriptor), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get([RegularExpression("^[0-9a-f]{64}$")] string hash,
        CancellationToken cancellationToken)
    {
        var (_, actorId) = GetActor();
        var restriction = await authorization.AuthorizeAsync(User, null, new ApiResourceRequirement("asset", hash));
        if (!restriction.Succeeded)
            return NotFound();
        var grant = await authorization.AuthorizeAsync(User, null, new ApiResourceRequirement("asset", hash, true));
        var asset = await assets.FindAccessibleAsync(hash, actorId, grant.Succeeded, cancellationToken);
        return asset is null ? NotFound() : Ok(asset);
    }

    (Guid TokenId, Guid ActorId) GetActor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
            return (tokenId, actorId);
        throw new AssetApiContractException("authentication_required", "Authentication is required.", 401);
    }
}

public sealed class AssetUploadModel
{
    [Required]
    [FromForm(Name = "file")]
    [JsonPropertyName("file")]
    public IFormFile File { get; set; } = null!;

    [FromForm(Name = "filename")]
    [JsonPropertyName("filename")]
    public string? Filename { get; set; }
}
