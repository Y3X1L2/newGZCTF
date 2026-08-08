using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using GZCTF.Modules.Identity.Infrastructure;

namespace GZCTF.Extensions.Startup;

internal static class IdentityExtension
{
    private const string RequestScheme = "GzctfRequest";
    private const string DataProtectionApplicationName = "GZCTF";

    extension(WebApplicationBuilder builder)
    {
        public void ConfigureIdentity()
        {
            builder.Services.AddDataProtection()
                .SetApplicationName(DataProtectionApplicationName)
                .PersistKeysToDbContext<AppDbContext>();

            builder.Services.AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = RequestScheme;
                    o.DefaultChallengeScheme = RequestScheme;
                    o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
                })
                .AddPolicyScheme(RequestScheme, null, options =>
                {
                    options.ForwardDefaultSelector = context =>
                        context.Request.Path.StartsWithSegments(
                            "/api/open/v1", StringComparison.OrdinalIgnoreCase)
                            ? ApiTokenDefaults.Scheme
                            : IdentityConstants.ApplicationScheme;
                })
                .AddIdentityCookies(options =>
                {
                    options.ApplicationCookie?.Configure(auth =>
                    {
                        auth.Cookie.Name = "GZCTF_Token";
                        auth.SlidingExpiration = true;
                        auth.ExpireTimeSpan = TimeSpan.FromDays(7);
                        auth.Events.OnValidatePrincipal =
                            SecurityStampValidator.ValidatePrincipalAsync;
                    });
                });

            builder.Services.AddIdentityCore<UserInfo>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                    options.Password.RequireNonAlphanumeric = false;
                    options.SignIn.RequireConfirmedEmail = true;

                    // Allow all characters in username
                    options.User.AllowedUserNameCharacters = string.Empty;
                })
                .AddSignInManager<SignInManager<UserInfo>>()
                .AddUserManager<UserManager<UserInfo>>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddErrorDescriber<TranslatedIdentityErrorDescriber>()
                .AddDefaultTokenProviders();

            builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
                o.TokenLifespan = TimeSpan.FromHours(3)
            );
            builder.Services.Configure<SecurityStampValidatorOptions>(o =>
                o.ValidationInterval = TimeSpan.Zero);
        }
    }
}
