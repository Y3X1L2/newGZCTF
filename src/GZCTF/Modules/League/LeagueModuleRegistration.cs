using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Infrastructure;
using GZCTF.Modules.TeamLab.Application.Rollouts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GZCTF.Modules.League;

public static class LeagueModuleRegistration
{
    public static IServiceCollection AddLeagueModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LeagueOptions>(configuration.GetSection("League"));
        services.AddScoped<LeagueMatchStore>();
        services.AddScoped<LeagueMatchService>();
        services.AddScoped<ILeagueMatchQuery>(p => p.GetRequiredService<LeagueMatchService>());
        services.AddScoped<ILeagueFinalizationService, LeagueFinalizationService>();
        services.AddScoped<LeagueLifecycleService>();
        services.AddScoped<LeagueTeamLabAdapter>();
        services.AddScoped<ILeagueRuntimePort>(provider => provider.GetRequiredService<LeagueTeamLabAdapter>());
        services.AddScoped<ILeagueAttackAccessPort>(provider => provider.GetRequiredService<LeagueTeamLabAdapter>());
        services.AddScoped<ITeamLabRolloutTargetProvider>(provider => provider.GetRequiredService<LeagueTeamLabAdapter>());
        services.TryAddScoped<ILeagueFlagPort, UnavailableLeagueProviders>();
        services.TryAddScoped<ILeagueCoinPort, UnavailableLeagueProviders>();
        services.TryAddScoped<ILeagueCoreMaterialPort, UnavailableLeagueProviders>();
        services.AddHostedService<LeagueLifecycleWorker>();
        return services;
    }
}
