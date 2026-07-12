using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Modules.Audit.Application;

namespace GZCTF.Modules.TeamLab;

public static class TeamLabModuleRegistration
{
    public static IServiceCollection AddTeamLabModule(this IServiceCollection services)
    {
        services.AddScoped<TeamLabTopologyValidator>();
        services.AddScoped<TeamLabReleaseService>();
        services.AddScoped<ITeamLabTopologyApplicationService, TeamLabTopologyApplicationService>();
        services.AddScoped<TeamLabRuntimeOverlayService>();
        services.AddScoped<TeamLabRuntimePlanner>();
        services.AddScoped<TeamLabRuntimeProjectionService>();
        services.AddScoped<TeamLabAuthorizationService>();
        services.AddScoped<TeamLabTrafficApplicationService>();
        services.AddScoped<ITeamLabNodeExecutor, AgentTeamLabNodeExecutor>();
        services.AddScoped<TeamLabRouteApplicationService>();
        services.AddScoped<TeamLabShardDeploymentService>();
        services.AddScoped<TeamLabRuntimeCleanupService>();
        services.AddScoped<ITeamLabRuntimeApplicationService, TeamLabRuntimeOrchestrator>();
        services.AddScoped<TeamLabAccessGrantService>();
        services.AddScoped<TeamLabRuntimeOperationPayloadProtector>();
        services.AddScoped<ITeamLabRuntimeOperationSubmissionStore, EfTeamLabRuntimeOperationSubmissionStore>();
        services.AddScoped<TeamLabRuntimeOperationApplicationService>();
        services.AddScoped<IApiOperationHandler, TeamLabRuntimeOperationHandler>();
        services.AddScoped<IApiOperationResultProvider, TeamLabRuntimeOperationResultProvider>();
        return services;
    }
}
