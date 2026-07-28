using GZCTF.Modules.Exercise.Application;
using GZCTF.Modules.Exercise.Infrastructure;
using GZCTF.Repositories;
using GZCTF.Repositories.Interface;

namespace GZCTF.Modules.Exercise;

public static class ExerciseModuleRegistration
{
    public static IServiceCollection AddExerciseModule(this IServiceCollection services)
    {
        services.AddScoped<IExerciseChallengeRepository, ExerciseChallengeRepository>();
        services.AddScoped<IExerciseInstanceRepository, ExerciseInstanceRepository>();
        services.AddScoped<IExerciseService, ExerciseService>();
        services.AddScoped<IExerciseManagementService, ExerciseManagementService>();
        return services;
    }
}
