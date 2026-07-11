using System.Net.Mime;
using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
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
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId))
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

        var operation = await operations.GetForTokenAsync(id, tokenId, cancellationToken);
        if (operation is null)
            throw new ApiOperationNotFoundException();

        var provider = (resultProviders ?? []).SingleOrDefault(item =>
            string.Equals(item.Kind, operation.Kind, StringComparison.Ordinal));
        var operationResult = provider is null
            ? null
            : await provider.GetResultAsync(operation.Id, cancellationToken);
        return Ok(ApiOperationModel.FromEntity(operation, operationResult));
    }
}
