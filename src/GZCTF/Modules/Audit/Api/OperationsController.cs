using System.Net.Mime;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Identity.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Audit.Api;

[ApiController]
[ApiExplorerSettings(GroupName = "open-v1")]
[Route("api/open/v1/operations")]
[Authorize(Policy = "scope:" + ApiTokenScopes.OperationsRead)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class OperationsController(
    ApiOperationService operations,
    IEnumerable<IApiOperationResultProvider>? resultProviders = null) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiOperationPageModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] ApiOperationStatus? status = null,
        [FromQuery] string? kind = null,
        [FromQuery, Range(1, 100)] int limit = 50,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId))
            return AuthenticationRequired();

        var page = await operations.ListForTokenAsync(
            tokenId, status, kind, after, limit, cancellationToken);
        return Ok(new ApiOperationPageModel(
            page.Items.Select(operation => ApiOperationModel.FromEntity(operation)).ToArray(),
            page.NextCursor));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) ||
            !Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return AuthenticationRequired();

        var hasExplicitGrant = User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
            ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var resourceId) &&
            (string.Equals(type, "operation", StringComparison.Ordinal) || type == "*") &&
            (string.Equals(resourceId, id.ToString("D"), StringComparison.OrdinalIgnoreCase) || resourceId == "*"));
        var isAdministrator = User.FindAll(ApiTokenClaimTypes.Resource).Any(claim =>
            ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var resourceId) &&
            (string.Equals(type, "operation", StringComparison.Ordinal) || type == "*") &&
            resourceId == "*");
        var operation = await operations.GetAccessibleAsync(
            id,
            tokenId,
            actorUserId,
            isAdministrator,
            hasExplicitGrant,
            cancellationToken);
        if (operation is null)
            throw new ApiOperationNotFoundException();

        var provider = (resultProviders ?? []).SingleOrDefault(item =>
            string.Equals(item.Kind, operation.Kind, StringComparison.Ordinal));
        var operationResult = provider is null
            ? null
            : await provider.GetResultAsync(operation.Id, cancellationToken);
        return Ok(ApiOperationModel.FromEntity(operation, operationResult));
    }

    private IActionResult AuthenticationRequired()
    {
        var result = new ObjectResult(ExternalApiProblemDetails.Create(
            HttpContext,
            StatusCodes.Status401Unauthorized,
            "authentication_required",
            "Authentication is required."))
        {
            StatusCode = StatusCodes.Status401Unauthorized
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
