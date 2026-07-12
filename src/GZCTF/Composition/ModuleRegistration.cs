using GZCTF.Modules.Audit;
using GZCTF.Modules.Content;
using GZCTF.Modules.Ctf;
using GZCTF.Modules.Identity;
using GZCTF.Modules.TeamLab;
using GZCTF.Modules.Penetration;
using GZCTF.Modules.Theory;
using GZCTF.Infrastructure.Persistence.Governance;

namespace GZCTF.Composition;

public static class ModuleRegistration
{
    public static IServiceCollection AddPlatformModules(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DataRetentionOptions>()
            .Bind(configuration.GetSection(DataRetentionOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.TeamLabFlowAggregateDays > options.TeamLabFlowDays,
                "TeamLab aggregate retention must exceed raw flow retention.")
            .ValidateOnStart();
        services.AddSingleton<DataRetentionPolicyCatalog>();
        services.AddSingleton<DataGovernanceMetrics>();
        services.AddScoped<PostgresGovernanceLease>();
        services.AddScoped<PostgresPartitionManager>();
        services.AddScoped<OperationalAggregationService>();
        services.AddScoped<TerminalHistoryCleaner>();
        services.AddScoped<DataRetentionExecutor>();
        services.AddHostedService<DataGovernanceWorker>();
        services.AddIdentityModule(configuration);
        services.AddAuditModule();
        services.AddContentModule();
        services.AddCtfModule();
        services.AddTeamLabModule();
        services.AddPenetrationModule();
        services.AddTheoryModule();
        return services;
    }
}
