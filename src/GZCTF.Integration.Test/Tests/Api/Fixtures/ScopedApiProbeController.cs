using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Audit.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace GZCTF.Integration.Test.Tests.Api.Fixtures;

[ApiController]
[Route("test/scopes")]
public sealed class ScopedApiProbeController : ControllerBase
{
    [HttpGet("images-read")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesRead)]
    public IActionResult Read() => Ok();

    [HttpPost("images-write")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesWrite)]
    public IActionResult Write() => Ok();

    [HttpGet("/api/open/v1/test/rate-limit")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesRead)]
    public IActionResult RateLimit() => Ok();

    [HttpPost("/api/open/v1/test/images-write")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesWrite)]
    public IActionResult ExternalWrite() => Ok();

    [HttpPost("/api/open/v1/test/model-validation")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesRead)]
    public IActionResult ValidateModel(RequiredProbeModel model) => Ok(model.Value);

    [HttpGet("/test/resources/{resourceId}")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesRead)]
    public async Task<IActionResult> Resource(
        string resourceId,
        [FromServices] IAuthorizationService authorization)
    {
        var result = await authorization.AuthorizeAsync(
            User, null, new ApiResourceRequirement("image", resourceId));
        return result.Succeeded ? Ok() : StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpGet("/api/open/v1/test/problems/conflict")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesRead)]
    public IActionResult ConflictProblem() => throw new IdempotencyConflictException();

    [HttpGet("/api/open/v1/test/problems/unknown")]
    [Authorize(Policy = "scope:" + ApiTokenScopes.ImagesRead)]
    public IActionResult Unknown() => throw new InvalidOperationException("phase-one-sensitive-detail");
}

public sealed class RequiredProbeModel
{
    [Required]
    public string Value { get; set; } = string.Empty;
}
