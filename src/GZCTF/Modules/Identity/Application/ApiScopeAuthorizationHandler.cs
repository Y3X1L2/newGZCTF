using Microsoft.AspNetCore.Authorization;

namespace GZCTF.Modules.Identity.Application;

public sealed class ApiScopeAuthorizationHandler : AuthorizationHandler<ApiScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiScopeRequirement requirement)
    {
        if (context.User.HasClaim(ApiTokenClaimTypes.Scope, requirement.Scope))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

public static class ApiTokenClaimTypes
{
    public const string ActorType = "gzctf:actor_type";
    public const string TokenId = "gzctf:api_token_id";
    public const string Scope = "gzctf:scope";
    public const string Resource = "gzctf:resource";
    public const string RequestsPerMinute = "gzctf:requests_per_minute";
    public const string LastUsedAt = "gzctf:last_used_at";
}
