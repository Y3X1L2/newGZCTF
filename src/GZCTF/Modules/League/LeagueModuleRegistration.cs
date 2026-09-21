using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Infrastructure;
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
        services.TryAddScoped<ILeagueRuntimePort, UnavailableLeagueProviders>();
        services.TryAddScoped<ILeagueFlagPort, UnavailableLeagueProviders>();
        services.TryAddScoped<ILeagueCoinPort, UnavailableLeagueProviders>();
        services.AddHostedService<LeagueLifecycleWorker>();
        return services;
    }
}
