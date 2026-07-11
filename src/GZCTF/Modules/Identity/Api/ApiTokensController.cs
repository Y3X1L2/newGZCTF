using System.Net.Mime;
using GZCTF.Middlewares;
using GZCTF.Models.Request.Admin;
using GZCTF.Modules.Identity.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Identity.Api;

[ApiController]
[RequireTeacher]
[Route("api/tokens")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class ApiTokensController(
    UserManager<UserInfo> userManager,
    ApiTokenIssuer issuer,
    IApiTokenStore store,
    ILogger<ApiTokensController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ApiTokenResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Issue(ApiTokenCreateModel model, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        try
        {
            var result = await issuer.IssueAsync(
                new ActorContext(user.Id, user.Role),
                new IssueApiTokenCommand(
                    model.Name,
                    model.Scopes,
                    model.Resources.Select(resource =>
                        new ApiTokenResourceGrantSpec(resource.ResourceType, resource.ResourceId)).ToArray(),
                    model.RequestsPerMinute,
                    model.ExpiresAt),
                cancellationToken);

            logger.Log(
                $"API token issued: token={result.Token.Id}, name={result.Token.Name}, scopes={string.Join(',', result.Token.Scopes.Select(scope => scope.Scope))}.",
                user,
                TaskStatus.Success,
                LogLevel.Information);

            return Ok(new ApiTokenResponse(result.PlainTextToken, ApiTokenModel.FromEntity(result.Token)));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new RequestResponse(exception.Message, StatusCodes.Status400BadRequest));
        }
        catch (ApiTokenScopeException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new RequestResponse(exception.Message, StatusCodes.Status403Forbidden));
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiTokenModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var tokens = await store.ListAsync(user.Role >= Role.Admin ? null : user.Id, cancellationToken);
        return Ok(tokens.Select(ApiTokenModel.FromEntity).ToArray());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var revoked = await store.RevokeAsync(id, user.Id, user.Role >= Role.Admin, cancellationToken);
        if (revoked)
            logger.Log($"API token revoked: token={id}.", user, TaskStatus.Success, LogLevel.Information);

        return revoked ? NoContent() : NotFound();
    }
}
