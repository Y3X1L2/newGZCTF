using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GZCTF.Modules.Identity.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Identity.Infrastructure;

public static class ApiTokenDefaults
{
    public const string Scheme = "GzctfApiToken";
}

public sealed class ApiTokenSchemeOptions : AuthenticationSchemeOptions;

public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<ApiTokenSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenValidator validator)
    : AuthenticationHandler<ApiTokenSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authorization) ||
            !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
            return AuthenticateResult.NoResult();

        var result = await validator.ValidateAsync(authorization.Parameter, Context.RequestAborted);
        if (!result.Succeeded || result.Token is not { } token)
            return AuthenticateResult.Fail("Invalid API token.");

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, token.CreatorId.ToString()),
            new(ClaimTypes.Name, token.Name),
            new(ApiTokenClaimTypes.ActorType, "api_token"),
            new(ApiTokenClaimTypes.TokenId, token.Id.ToString()),
            new(ApiTokenClaimTypes.RequestsPerMinute, token.RequestsPerMinute.ToString())
        ];
        if (result.CreatorRole is { } creatorRole)
        {
            claims.Add(new Claim(ClaimTypes.Role, creatorRole.ToString()));
            // SuperAdmin must also satisfy `IsInRole(nameof(Role.Admin))` checks
            // on open v1 endpoints, mirroring the cookie-side role model.
            if (creatorRole >= Role.Admin)
                claims.Add(new Claim(ClaimTypes.Role, nameof(Role.Admin)));
        }
        if (token.LastUsedAt is { } lastUsedAt)
            claims.Add(new Claim(ApiTokenClaimTypes.LastUsedAt, lastUsedAt.ToUnixTimeSeconds().ToString()));
        claims.AddRange(token.Scopes.Select(scope => new Claim(ApiTokenClaimTypes.Scope, scope.Scope)));
        claims.AddRange(token.Resources.Select(resource => new Claim(
            ApiTokenClaimTypes.Resource,
            ApiTokenResourceClaim.Format(resource.ResourceType, resource.ResourceId))));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiTokenDefaults.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiTokenDefaults.Scheme));
    }
}
