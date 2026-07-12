using GZCTF.Modules.Penetration.Application;

namespace GZCTF.Modules.Penetration;

public static class PenetrationModuleRegistration
{
    public static IServiceCollection AddPenetrationModule(this IServiceCollection services)
    {
        services.AddScoped<PenetrationObjectiveService>();
        services.AddScoped<PenetrationWorkspaceService>();
        services.AddScoped<PenetrationTeamLabAdapter>();
        return services;
    }
}
