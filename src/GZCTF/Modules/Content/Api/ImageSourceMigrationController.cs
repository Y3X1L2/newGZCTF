using System.ComponentModel.DataAnnotations;
using GZCTF.Middlewares;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Identity.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Content.Api;

[Authorize]
[RequireAdmin]
[ApiController]
[Route("api/v1/image-templates/{templateId:int}/source-migrations")]
public sealed class ImageSourceMigrationController(ImageSourceMigrationService migrations, UserManager<UserInfo> users)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Submit(int templateId,
        [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey, CancellationToken token)
    {
        var result = await migrations.SubmitAsync(templateId, await ActorAsync(), idempotencyKey, token);
        return AcceptedAtAction(nameof(Get), new { templateId, operationId = result.Operation.Id },
            ApiOperationModel.FromEntity(result.Operation));
    }

    [HttpGet("{operationId:guid}")]
    [ProducesResponseType(typeof(ApiOperationModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int templateId, Guid operationId, CancellationToken token)
    {
        var operation = await migrations.FindAsync(templateId, operationId, await ActorAsync(), token);
        return operation is null ? NotFound() : Ok(ApiOperationModel.FromEntity(operation));
    }

    async Task<ActorContext> ActorAsync()
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException("Current administrator is missing.");
        return new ActorContext(user.Id, user.Role);
    }
}
