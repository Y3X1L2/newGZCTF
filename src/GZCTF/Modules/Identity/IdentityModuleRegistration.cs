using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace GZCTF.Modules.Identity;

public static class IdentityModuleRegistration
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        _ = configuration;
        services.AddScoped<IApiTokenStore, EfApiTokenStore>();
        services.AddSingleton<ApiTokenSecretHasher>();
        services.AddSingleton<IApiTokenSecretHasher>(provider =>
            provider.GetRequiredService<ApiTokenSecretHasher>());
        services.AddScoped<ApiTokenIssuer>();
        services.AddScoped<ApiTokenValidator>();
        services.AddSingleton<ApiTokenRateLimitStore>();
        services.AddSingleton<IApiTokenRateLimitStore>(provider =>
            provider.GetRequiredService<ApiTokenRateLimitStore>());
        services.AddSingleton<IAuthorizationHandler, ApiScopeAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, ApiResourceAuthorizationHandler>();
        services.AddAuthentication()
            .AddScheme<ApiTokenSchemeOptions, ApiTokenAuthenticationHandler>(ApiTokenDefaults.Scheme, _ => { });
        services.AddAuthorization(options =>
        {
            AddScopePolicy(options, ApiTokenScopes.ImagesRead);
            AddScopePolicy(options, ApiTokenScopes.ImagesWrite);
            AddScopePolicy(options, ApiTokenScopes.ImagesDelete);
            AddScopePolicy(options, ApiTokenScopes.OperationsRead);
            AddScopePolicy(options, ApiTokenScopes.ChallengesRead);
            AddScopePolicy(options, ApiTokenScopes.ChallengesWrite);
            AddScopePolicy(options, ApiTokenScopes.ChallengesDelete);
            AddScopePolicy(options, ApiTokenScopes.ExercisesRead);
            AddScopePolicy(options, ApiTokenScopes.ExercisesWrite);
            AddScopePolicy(options, ApiTokenScopes.ExercisesDelete);
        });
        return services;
    }

    private static void AddScopePolicy(AuthorizationOptions options, string scope)
    {
        options.AddPolicy($"scope:{scope}", policy =>
        {
            policy.AddAuthenticationSchemes(ApiTokenDefaults.Scheme);
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new ApiScopeRequirement(scope));
        });
    }
}
