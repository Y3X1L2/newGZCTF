using GZCTF.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Exercise.Infrastructure;

public sealed class ExerciseApiTokenResourceGrantPolicy(AppDbContext context) : IApiTokenResourceGrantPolicy
{
    public string ResourceType => "exercise";

    public Task<bool> CanGrantAsync(
        ActorContext actor,
        string resourceId,
        CancellationToken cancellationToken)
    {
        if (actor.Role < Role.Teacher)
            return Task.FromResult(false);
        if (resourceId == "*")
            return Task.FromResult(true);
        if (!int.TryParse(resourceId, out var exerciseId) || exerciseId <= 0)
            return Task.FromResult(false);
        return context.ExerciseChallenges.AsNoTracking().AnyAsync(
            exercise => exercise.Id == exerciseId && exercise.TrainingCourseId == null,
            cancellationToken);
    }
}
