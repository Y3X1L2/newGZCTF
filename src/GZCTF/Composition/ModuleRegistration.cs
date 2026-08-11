using GZCTF.Modules.Audit;
using GZCTF.Modules.Content;
using GZCTF.Modules.Ctf;
using GZCTF.Modules.Exercise;
using GZCTF.Modules.Provisioning;
using GZCTF.Modules.Identity;
using GZCTF.Modules.TeamLab;
using GZCTF.Modules.Penetration;
using GZCTF.Modules.Theory;
using GZCTF.Infrastructure.Persistence.Governance;
using GZCTF.Modules.Theory.Application;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Infrastructure;
using GZCTF.Infrastructure.Cache;
using GZCTF.Services;
using Microsoft.Extensions.Options;

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
        services.AddExerciseModule();
        services.AddProvisioningModule();
        services.AddScoped<TheoryStatisticsProjectionService>();
        services.AddScoped<UserProfileQueryService>();
        services.AddSingleton<RedisDeploymentQueueWakeup>();
        services.AddSingleton<RedisRuntimeSignalWakeup>();
        services.AddSingleton<PollingDeploymentQueueWakeup>();
        services.AddSingleton<IDeploymentQueueWakeup>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<RedisRuntimeOptions>>().Value.Mode ==
            RedisRuntimeMode.Disabled
                ? serviceProvider.GetRequiredService<PollingDeploymentQueueWakeup>()
                : serviceProvider.GetRequiredService<RedisDeploymentQueueWakeup>());
        services.AddSingleton<IRuntimeSignalWakeup>(serviceProvider =>
            serviceProvider.GetRequiredService<RedisRuntimeSignalWakeup>());
        services.AddScoped<RuntimeSignalService>();
        services.AddSingleton<PostgresNodeLiveStateFallback>();
        services.AddSingleton<RedisNodeLiveStateStore>();
        services.AddSingleton<INodeLiveStateStore>(serviceProvider =>
            serviceProvider.GetRequiredService<RedisNodeLiveStateStore>());
        services.AddHostedService<NodeMetricPersistenceWorker>();
        return services;
    }
}
