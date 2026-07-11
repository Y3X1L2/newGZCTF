using Microsoft.AspNetCore.Authorization;

namespace GZCTF.Modules.Identity.Application;

public sealed record ApiResourceRequirement(string ResourceType, string ResourceId) : IAuthorizationRequirement;

public static class ApiTokenResourceClaim
{
    private const char Separator = '\u001f';

    public static string Format(string resourceType, string resourceId) =>
        $"{resourceType}{Separator}{resourceId}";

    public static bool TryParse(string value, out string resourceType, out string resourceId)
    {
        var separatorIndex = value.IndexOf(Separator);
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            resourceType = string.Empty;
            resourceId = string.Empty;
            return false;
        }

        resourceType = value[..separatorIndex];
        resourceId = value[(separatorIndex + 1)..];
        return true;
    }
}

public sealed class ApiResourceAuthorizationHandler : AuthorizationHandler<ApiResourceRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiResourceRequirement requirement)
    {
        var grants = context.User.FindAll(ApiTokenClaimTypes.Resource).ToArray();
        if (grants.Length == 0)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var requiredType = requirement.ResourceType.Trim().ToLowerInvariant();
        var requiredId = requirement.ResourceId.Trim();
        if (grants.Any(claim =>
                ApiTokenResourceClaim.TryParse(claim.Value, out var type, out var id) &&
                (string.Equals(type, requiredType, StringComparison.Ordinal) || type == "*") &&
                (string.Equals(id, requiredId, StringComparison.Ordinal) || id == "*")))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
