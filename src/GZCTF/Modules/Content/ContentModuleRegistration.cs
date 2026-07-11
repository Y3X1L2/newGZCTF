using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Infrastructure;
using GZCTF.Modules.Ctf.Infrastructure;
using GZCTF.Modules.Exercise.Infrastructure;
using GZCTF.Modules.Penetration.Infrastructure;
using GZCTF.Modules.Training.Infrastructure;
using GZCTF.Modules.Training.Application;
using GZCTF.Modules.Audit.Application;

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
        services.AddScoped<IImageTemplateReferenceProvider, PenetrationImageTemplateReferenceProvider>();
        services.AddScoped<ITrainingCourseDeletionStore, EfTrainingCourseDeletionStore>();
        services.AddScoped<TrainingCourseDeletionService>();
        services.AddScoped<IImageImportSubmissionStore, EfImageImportSubmissionStore>();
        services.AddSingleton<IImageImportStagingStore, FileImageImportStagingStore>();
        services.AddScoped<IImageImportExecutor, DockerImageImportExecutor>();
        services.AddScoped<IImageImportTemplateStore, EfImageImportTemplateStore>();
        services.AddScoped<ImageImportApplicationService>();
        services.AddSingleton<DockerImageReferencePolicy>();
        services.AddScoped<ImageImportStagingReconciler>();
        services.AddScoped<IApiOperationHandler, ImageImportOperationHandler>();
        services.AddHostedService<ImageImportStagingReconcileService>();
        services.AddHostedService<ImageTemplateDeletionReconcileService>();
        return services;
    }
}
