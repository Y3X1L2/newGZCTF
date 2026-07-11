using System.Security.Claims;
using GZCTF.Infrastructure.Api;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Identity.Infrastructure;

public sealed class ApiTokenRateLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IApiTokenRateLimitStore store,
        IApiTokenStore tokenStore)
    {
        if (!context.Request.Path.StartsWithSegments("/api/open/v1", StringComparison.OrdinalIgnoreCase) ||
            context.User.FindFirstValue(ApiTokenClaimTypes.ActorType) != "api_token")
        {
            await next(context);
            return;
        }

        if (!Guid.TryParse(context.User.FindFirstValue(ApiTokenClaimTypes.TokenId), out var tokenId) ||
            !int.TryParse(context.User.FindFirstValue(ApiTokenClaimTypes.RequestsPerMinute), out var limit))
        {
            await ExternalApiProblemDetails.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "authentication_required",
                "Authentication is required.");
            return;
        }

        var decision = await store.ConsumeAsync(tokenId, limit);
        if (!decision.Available)
        {
            await ExternalApiProblemDetails.WriteAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "quota_backend_unavailable",
                "API token quota service is unavailable.");
            return;
        }

        if (!decision.Allowed)
        {
            await ExternalApiProblemDetails.WriteAsync(
                context,
                StatusCodes.Status429TooManyRequests,
                "rate_limit_exceeded",
                "API token request quota exceeded.",
                configureHeaders: headers =>
                    headers.RetryAfter = decision.RetryAfterSeconds.ToString());
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var lastUsedClaim = context.User.FindFirstValue(ApiTokenClaimTypes.LastUsedAt);
        if (!long.TryParse(lastUsedClaim, out var lastUsedSeconds) ||
            DateTimeOffset.FromUnixTimeSeconds(lastUsedSeconds) < now.AddMinutes(-1))
            await tokenStore.RecordUsageAsync(tokenId, now, context.RequestAborted);

        await next(context);
    }
}
