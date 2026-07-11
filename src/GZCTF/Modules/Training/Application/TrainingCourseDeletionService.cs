using GZCTF.Modules.Identity.Application;

namespace GZCTF.Modules.Training.Application;

public sealed record TrainingCourseDeletionTarget(
    int Id,
    string Title,
    Guid? CreatedById,
    IReadOnlySet<Guid> OwnerIds);

public enum TrainingCourseDeletionStatus
{
    Deleted,
    NotFound,
    Forbidden
}

public sealed record TrainingCourseDeletionResult(TrainingCourseDeletionStatus Status, string? Title = null);

public interface ITrainingCourseDeletionStore
{
    Task<TrainingCourseDeletionTarget?> FindAsync(int courseId, CancellationToken cancellationToken);
    Task DeleteAsync(int courseId, CancellationToken cancellationToken);
}

public sealed class TrainingCourseDeletionService(ITrainingCourseDeletionStore store)
{
    public async Task<TrainingCourseDeletionResult> DeleteAsync(
        int courseId,
        ActorContext actor,
        CancellationToken cancellationToken)
    {
        var course = await store.FindAsync(courseId, cancellationToken);
        if (course is null)
            return new TrainingCourseDeletionResult(TrainingCourseDeletionStatus.NotFound);

        var authorized = actor.Role >= Role.Admin ||
                         actor.UserId == course.CreatedById ||
                         actor.UserId.HasValue && course.OwnerIds.Contains(actor.UserId.Value);
        if (!authorized)
            return new TrainingCourseDeletionResult(TrainingCourseDeletionStatus.Forbidden);

        await store.DeleteAsync(courseId, cancellationToken);
        return new TrainingCourseDeletionResult(TrainingCourseDeletionStatus.Deleted, course.Title);
    }
}
