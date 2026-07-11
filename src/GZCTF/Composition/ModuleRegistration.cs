using GZCTF.Modules.Audit;
using GZCTF.Modules.Content;
using GZCTF.Modules.Ctf;
using GZCTF.Modules.Identity;
using GZCTF.Modules.TeamLab;

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
        services.AddTeamLabModule();
        return services;
    }
}
