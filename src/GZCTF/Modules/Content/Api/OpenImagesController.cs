using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using System.Text.Json.Serialization;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Identity.Application;
using GZCTF.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Content.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/images")]
[Produces(MediaTypeNames.Application.Json, "application/problem+json")]
public sealed class OpenImagesController(
    ImageImportApplicationService imports,
    IImageTemplateCatalog catalog,
    ImageTemplateDeletionService deletion,
    IAuthorizationService authorization) : ControllerBase
{
    [HttpPost("docker-references")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesWrite)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RegisterDockerReference(
        DockerImageReferenceImportModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (tokenId, actorUserId) = GetActor();
        await AuthorizeImageAsync(model.Name);

        var result = await imports.SubmitDockerReferenceAsync(
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            new DockerImageReferenceImportCommand(
                model.Name,
                model.RegistryUrl,
                model.OSType,
                model.ExpectedDigest),
            cancellationToken);

        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpPost("docker-archives")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesWrite)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)]
    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = 60L * 1024 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RegisterDockerArchive(
        [FromForm] DockerImageArchiveUploadModel model,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (tokenId, actorUserId) = GetActor();
        await AuthorizeImageAsync(model.Name);
        if (model.File.Length <= 0)
            throw new ImageImportContractException(
                "image_archive_size_invalid", "Docker archive is empty.", 400);

        await using var stream = model.File.OpenReadStream();
        var result = await imports.SubmitDockerArchiveAsync(
            tokenId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            idempotencyKey,
            stream,
            model.File.FileName,
            model.File.Length,
            new DockerImageArchiveImportCommand(
                model.Name,
                model.SourceImage,
                model.OSType,
                model.ExpectedDigest),
            cancellationToken);

        var operation = ApiOperationModel.FromEntity(result.Operation);
        return Accepted($"/api/open/v1/operations/{operation.Id}", operation);
    }

    [HttpGet("{imageTemplateId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesRead)]
    [ProducesResponseType(typeof(OpenImageTemplateModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        var (_, actorUserId) = GetActor();
        var template = await catalog.FindDetailsAsync(imageTemplateId, cancellationToken);
        if (template is null || !await CanAccessTemplateAsync(template, actorUserId))
            return await NotFoundProblemAsync();

        return Ok(OpenImageTemplateModel.FromDetails(template));
    }

    [HttpDelete("{imageTemplateId:int}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesDelete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        var (tokenId, actorUserId) = GetActor();
        var result = await deletion.DeleteAsync(
            imageTemplateId,
            new ActorContext(actorUserId, Role.Teacher, tokenId),
            cancellationToken);
        if (result.Status is ImageTemplateDeleteStatus.NotFound or ImageTemplateDeleteStatus.Forbidden)
            return await NotFoundProblemAsync();
        if (result.Status == ImageTemplateDeleteStatus.InUse)
        {
            await ExternalApiProblemDetails.WriteAsync(
                HttpContext,
                StatusCodes.Status409Conflict,
                "asset_in_use",
                "Image template is in use.",
                "The image template is still referenced by platform resources.",
                configureProblem: problem =>
                    problem.Extensions["errors"] = new Dictionary<string, string[]>
                    {
                        ["imageTemplateId"] = result.References.Select(reference =>
                            $"{reference.Module}/{reference.ResourceType}/{reference.ResourceId}: {reference.DisplayName}")
                            .ToArray()
                    });
            return new EmptyResult();
        }

        return NoContent();
    }

    private (Guid TokenId, Guid ActorUserId) GetActor()
    {
        if (Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return (tokenId, actorUserId);

        throw new ImageImportContractException(
            "authentication_required", "Authentication is required.", 401);
    }

    private async Task AuthorizeImageAsync(string name)
    {
        var resource = await authorization.AuthorizeAsync(
            User,
            null,
            new ApiResourceRequirement("image", name.Trim()));
        if (!resource.Succeeded)
            throw new ImageImportContractException(
                "insufficient_permission",
                "The token does not grant access to this image resource.",
                403);
    }

    private async Task<bool> CanAccessTemplateAsync(ImageTemplateDetails template, Guid actorUserId)
    {
        if (template.CreatedById == actorUserId)
            return true;
        if (!User.HasClaim(claim => claim.Type == ApiTokenClaimTypes.Resource))
            return false;

        var result = await authorization.AuthorizeAsync(
            User,
            null,
            new ApiResourceRequirement("image", template.Name));
        return result.Succeeded;
    }

    private async Task<IActionResult> NotFoundProblemAsync()
    {
        await ExternalApiProblemDetails.WriteAsync(
            HttpContext,
            StatusCodes.Status404NotFound,
            "image_not_found",
            "The image template was not found.");
        return new EmptyResult();
    }
}

public sealed class DockerImageArchiveUploadModel
{
    [Required]
    [FromForm(Name = "file")]
    [JsonPropertyName("file")]
    public IFormFile File { get; set; } = null!;

    [Required, MaxLength(256)]
    [FromForm(Name = "name")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    [FromForm(Name = "sourceImage")]
    [JsonPropertyName("sourceImage")]
    public string? SourceImage { get; set; }

    [FromForm(Name = "osType")]
    [JsonPropertyName("osType")]
    public OSType OSType { get; set; }

    [MaxLength(128)]
    [FromForm(Name = "expectedDigest")]
    [JsonPropertyName("expectedDigest")]
    public string? ExpectedDigest { get; set; }
}
