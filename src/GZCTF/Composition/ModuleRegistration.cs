using GZCTF.Modules.Audit;
using GZCTF.Modules.Content;
using GZCTF.Modules.Ctf;
using GZCTF.Modules.Identity;

namespace GZCTF.Composition;

public static class ModuleRegistration
{
    public static IServiceCollection AddPlatformModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityModule(configuration);
        services.AddAuditModule();
        services.AddContentModule();
        services.AddCtfModule();
        return services;
    }
}
