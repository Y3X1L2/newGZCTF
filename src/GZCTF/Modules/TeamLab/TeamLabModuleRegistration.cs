using GZCTF.Modules.TeamLab.Application;

namespace GZCTF.Modules.TeamLab;

public static class TeamLabModuleRegistration
{
    public static IServiceCollection AddTeamLabModule(this IServiceCollection services)
    {
        services.AddScoped<TeamLabTopologyValidator>();
        services.AddScoped<TeamLabReleaseService>();
        services.AddScoped<ITeamLabTopologyApplicationService, TeamLabTopologyApplicationService>();
        return services;
    }
}
