using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Ctf.Infrastructure;
using GZCTF.Modules.Exercise.Infrastructure;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Modules.Training.Infrastructure;
using GZCTF.Modules.Training.Application;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Content;

public static class ContentModuleRegistration
{
    public static IServiceCollection AddContentModule(this IServiceCollection services)
    {
        services.AddScoped<IImageTemplateCatalog, EfImageTemplateCatalog>();
        services.AddScoped<IImageTemplateArtifactCleaner, ImageTemplateArtifactCleaner>();
        services.AddScoped<ImageTemplateReferenceService>();
        services.AddScoped<ImageTemplateDeletionService>();
        services.AddScoped<ImageTemplateDeletionReconciler>();
        services.AddScoped<IImageTemplateReferenceProvider, CtfImageTemplateReferenceProvider>();
        services.AddScoped<IImageTemplateReferenceProvider, ExerciseImageTemplateReferenceProvider>();
        services.AddScoped<IImageTemplateReferenceProvider, TrainingImageTemplateReferenceProvider>();
        services.AddScoped<IImageTemplateReferenceProvider, TeamLabImageTemplateReferenceProvider>();
        services.AddScoped<ITrainingCourseDeletionStore, EfTrainingCourseDeletionStore>();
        services.AddScoped<TrainingCourseDeletionService>();
        services.AddScoped<IImageImportSubmissionStore, EfImageImportSubmissionStore>();
        services.AddSingleton<IImageImportStagingStore, FileImageImportStagingStore>();
        services.AddScoped<IImageImportExecutor, DockerImageImportExecutor>();
        services.AddScoped<IVmQcow2ImageImportExecutor, VmQcow2ImageImportExecutor>();
        services.AddScoped<IImageImportTemplateStore, EfImageImportTemplateStore>();
        services.AddScoped<ImageImportApplicationService>();
        services.AddSingleton<DockerImageReferencePolicy>();
        services.AddScoped<ImageImportStagingReconciler>();
        services.AddKeyedScoped<IApiOperationHandler, ImageImportOperationHandler>(
            ImageImportApplicationService.OperationKind);
        services.AddScoped<OciArtifactRegistryClient>();
        services.AddScoped<BootstrapProfileArtifactService>();
        services.AddScoped<IBootstrapProfileArtifactStagingService>(serviceProvider =>
            serviceProvider.GetRequiredService<BootstrapProfileArtifactService>());
        services.AddScoped<BootstrapProfileDistributionService>();
        services.AddScoped<IBootstrapProfileDistributionService>(serviceProvider =>
            serviceProvider.GetRequiredService<BootstrapProfileDistributionService>());
        services.AddScoped<BootstrapProfileApplicationService>();
        services.AddScoped<ImageTemplateCertificationService>();
        services.AddScoped<ImageRemoteAccessService>();
        services.AddScoped<VmImageCertificationProbeService>();
        services.AddSingleton<PreparedImageConformancePackageFactory>();
        services.AddKeyedScoped<IApiOperationHandler, BootstrapProfileOperationHandler>(
            BootstrapProfileApplicationService.OperationKind);
        services.AddKeyedScoped<IApiOperationHandler, ImageTemplateCertificationOperationHandler>(
            ImageTemplateCertificationService.OperationKind);
        services.AddHostedService<ImageImportStagingReconcileService>();
        services.AddHostedService<ImageTemplateDeletionReconcileService>();
        services.AddScoped<IApiTokenResourceGrantPolicy, ImageApiTokenResourceGrantPolicy>();
        return services;
    }
}
