using GZCTF.Modules.Exercise.Application;
using GZCTF.Modules.Exercise.Infrastructure;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Exercise;

public static class ExerciseModuleRegistration
{
    public static IServiceCollection AddExerciseModule(this IServiceCollection services)
    {
        services.AddScoped<IExerciseChallengeRepository, ExerciseChallengeRepository>();
        services.AddScoped<IExerciseInstanceRepository, ExerciseInstanceRepository>();
        services.AddScoped<IExerciseService, ExerciseService>();
        services.AddScoped<IExerciseManagementService, ExerciseManagementService>();
        services.AddScoped<IExerciseMutationSubmissionStore, EfExerciseMutationSubmissionStore>();
        services.AddScoped<ExerciseExternalApplicationService>();
        services.AddKeyedScoped<IApiOperationHandler, ExerciseMutationOperationHandler>(
            ExerciseExternalApplicationService.OperationKind);
        services.AddScoped<IApiOperationResultProvider, ExerciseMutationResultProvider>();
        services.AddScoped<IApiTokenResourceGrantPolicy, ExerciseApiTokenResourceGrantPolicy>();
        return services;
    }
}
