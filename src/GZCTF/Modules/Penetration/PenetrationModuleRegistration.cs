using GZCTF.Modules.Penetration.Application;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Application.Rollouts;

namespace GZCTF.Modules.Penetration;

public static class PenetrationModuleRegistration
{
    public static IServiceCollection AddPenetrationModule(this IServiceCollection services)
    {
        services.AddScoped<PenetrationObjectiveService>();
        services.AddScoped<PenetrationWorkspaceService>();
        services.AddScoped<PenetrationTeamLabAdapter>();
        services.AddScoped<ITeamLabRolloutTargetProvider>(provider =>
            provider.GetRequiredService<PenetrationTeamLabAdapter>());
        services.AddScoped<IRuntimeTicketLifecycleObserver, PenetrationTeamLabLifecycleObserver>();
        return services;
    }
}
