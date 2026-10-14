using System.Security.Claims;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Audit.Infrastructure;

/// <summary>Captures authenticated request identity and the trusted, forwarded connection address.</summary>
public sealed class RequestLogContextMiddleware(RequestDelegate next, ILogger<RequestLogContextMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var actorId = Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id : (Guid?)null;
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["ActorUserId"] = actorId,
            ["ActorKind"] = context.User.HasClaim(claim => claim.Type == ApiTokenClaimTypes.TokenId)
                ? "ApiToken" : context.User.Identity?.IsAuthenticated == true ? "User" : "Anonymous",
            ["UserName"] = context.User.Identity?.Name,
            ["IP"] = context.Connection.RemoteIpAddress
        });
        await next(context);
    }
}
