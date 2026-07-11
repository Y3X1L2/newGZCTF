using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace GZCTF.Infrastructure.Api;

public sealed class ExternalApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!context.Request.Path.StartsWithSegments("/api/open/v1", StringComparison.OrdinalIgnoreCase) ||
            authorizeResult.Succeeded)
        {
            await _fallback.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (authorizeResult.Challenged)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await ExternalApiProblemDetails.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "authentication_required",
                "Authentication is required.");
            return;
        }

        await ExternalApiProblemDetails.WriteAsync(
            context,
            StatusCodes.Status403Forbidden,
            "insufficient_permission",
            "The token does not grant access to this resource.");
    }
}
