using GZCTF.Models.Internal;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Application.Validation;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Application.Rollouts;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.TeamLab;

public static class TeamLabModuleRegistration
{
    public static IServiceCollection AddTeamLabModule(this IServiceCollection services)
    {
        // Built from configuration so tenant addressing stays inside the range the platform owns and
        // clear of the networks its nodes already route; either overlap would replace host routes on
        // the WorkerNode and break unrelated games running there.
        services.AddScoped<TeamLabTopologyValidator>(provider =>
        {
            var network = provider.GetRequiredService<IOptions<TeamLabNetworkConfig>>().Value;
            return new TeamLabTopologyValidator(TeamLabAddressPolicy.ForPlatform(
                network.ReservedCidrs, network.FabricLinkPool, network.RuntimeNetworkBaseCidr));
        });
        services.AddScoped<TeamLabReleaseService>();
        services.AddScoped<TeamLabReleaseImagePreparationService>();
        services.AddScoped<TeamLabControlScopeService>();
        services.AddScoped<TeamLabScopeAuthorizationService>();
        services.AddScoped<IApiTokenResourceGrantPolicy, TeamLabScopeApiTokenResourceGrantPolicy>();
        services.AddScoped<ITeamLabTopologyApplicationService, TeamLabTopologyApplicationService>();
        services.AddScoped<TeamLabRuntimeOverlayService>();
        services.AddScoped<TeamLabRuntimePlanner>();
        services.AddScoped<TeamLabFabricLinkAllocator>();
        services.AddScoped<TeamLabEventRecorder>();
        services.AddScoped<TeamLabRuntimeProjectionService>();
        services.AddScoped<TeamLabAdminQueryService>();
        services.AddScoped<ITeamLabUsageProjectionProvider, TeamLabEmptyUsageProjectionProvider>();
        services.AddScoped<TeamLabAuthorizationService>();
        services.AddScoped<TeamLabRemoteAccessAuthorizationService>();
        services.AddScoped<ITeamLabRemoteRelayGateway, AgentTeamLabRemoteRelayGateway>();
        services.AddScoped<ITeamLabRemoteAccessService, TeamLabRemoteAccessService>();
        services.AddHostedService<TeamLabRemoteSessionWorker>();
        services.AddScoped<TeamLabRuntimeLifecycleGuard>();
        services.AddScoped<TeamLabTrafficApplicationService>();
        services.AddSingleton<RedisTeamLabTrafficIngestor>();
        services.AddSingleton<ITeamLabTrafficIngestor>(serviceProvider =>
            serviceProvider.GetRequiredService<RedisTeamLabTrafficIngestor>());
        services.AddScoped<PostgresTeamLabTrafficBatchWriter>();
        services.AddHostedService<TeamLabTrafficPersistenceWorker>();
        services.AddScoped<TeamLabTrafficPathCorrelator>();
        services.AddHostedService<TeamLabTrafficPathWorker>();
        services.AddSingleton<TeamLabCaptureUploadTokenService>();
        services.AddSingleton<TeamLabCaptureArtifactStore>();
        services.AddScoped<TeamLabCaptureUploadService>();
        services.AddScoped<TeamLabCaptureCoordinator>();
        services.AddScoped<ITeamLabCaptureCleanup>(provider =>
            provider.GetRequiredService<TeamLabCaptureCoordinator>());
        services.AddHostedService<TeamLabCaptureCoordinatorWorker>();
        services.AddScoped<ITeamLabNodeExecutor, AgentTeamLabNodeExecutor>();
        services.AddScoped<TeamLabRouteApplicationService>();
        services.AddScoped<TeamLabDeploymentStageMachine>();
        services.AddScoped<ITeamLabDeploymentProgress>(serviceProvider =>
            serviceProvider.GetRequiredService<TeamLabDeploymentStageMachine>());
        services.AddScoped<ITeamLabArtifactDistribution, TeamLabArtifactDistribution>();
        services.AddScoped<ITeamLabRuntimeQueue, TeamLabRuntimeQueue>();
        services.AddScoped<TeamLabRuntimeRecoveryPolicy>();
        services.AddScoped<TeamLabShardDeploymentService>();
        services.AddScoped<TeamLabRuntimeCleanupService>();
        services.AddScoped<ITeamLabRuntimeApplicationService, TeamLabRuntimeOrchestrator>();
        services.AddScoped<TeamLabAccessGrantService>();
        services.AddScoped<ITeamLabRolloutApplicationService, TeamLabRolloutApplicationService>();
        services.AddScoped<ITeamLabRolloutTargetProvider, TeamLabExternalRolloutProvider>();
        services.AddScoped<TeamLabRolloutCoordinator>();
        services.AddHostedService<TeamLabRolloutCoordinatorWorker>();
        services.AddScoped<TeamLabRuntimeOperationPayloadProtector>();
        services.AddScoped<ITeamLabRuntimeOperationSubmissionStore, EfTeamLabRuntimeOperationSubmissionStore>();
        services.AddScoped<TeamLabRuntimeOperationApplicationService>();
        services.AddScoped<ITeamLabControlPlaneOperationService>(provider =>
            provider.GetRequiredService<TeamLabRuntimeOperationApplicationService>());
        services.AddKeyedScoped<IApiOperationHandler, TeamLabRuntimeOperationHandler>(
            TeamLabRuntimeOperationApplicationService.OperationKind);
        services.AddScoped<IApiOperationResultProvider, TeamLabRuntimeOperationResultProvider>();
        services.AddScoped<TeamLabWebhookService>();
        services.AddScoped<ITeamLabWebhookDeliverer, HttpTeamLabWebhookDeliverer>();
        services.AddHostedService<TeamLabWebhookDeliveryWorker>();
        return services;
    }
}
