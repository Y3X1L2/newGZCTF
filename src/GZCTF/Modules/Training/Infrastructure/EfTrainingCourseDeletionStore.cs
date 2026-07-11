using GZCTF.Models;
using GZCTF.Modules.Training.Application;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Training.Infrastructure;

public sealed class EfTrainingCourseDeletionStore(
    AppDbContext context,
    IBlobRepository blobRepository,
    ImageDistributionService imageDistribution) : ITrainingCourseDeletionStore
{
    public async Task<TrainingCourseDeletionTarget?> FindAsync(
        int courseId,
        CancellationToken cancellationToken)
    {
        var course = await context.TrainingCourses.AsNoTracking()
            .Where(item => item.Id == courseId)
            .Select(item => new
            {
                item.Id,
                item.Title,
                item.CreatedById,
                Owners = item.Teachers
                    .Where(teacher => teacher.Role == TrainingCourseTeacherRole.Owner)
                    .Select(teacher => teacher.TeacherId)
                    .ToArray()
            })
            .SingleOrDefaultAsync(cancellationToken);
        return course is null
            ? null
            : new TrainingCourseDeletionTarget(
                course.Id, course.Title, course.CreatedById, course.Owners.ToHashSet());
    }

    public async Task DeleteAsync(int courseId, CancellationToken cancellationToken)
    {
        var course = await context.TrainingCourses.SingleOrDefaultAsync(
            item => item.Id == courseId, cancellationToken);
        if (course is null)
            return;

        var challenges = await context.ExerciseChallenges
            .Include(challenge => challenge.Attachment)
            .ThenInclude(attachment => attachment!.LocalFile)
            .Where(challenge => challenge.TrainingCourseId == courseId)
            .ToArrayAsync(cancellationToken);
        var challengeIds = challenges.Select(challenge => challenge.Id).ToArray();
        if (challengeIds.Length > 0)
        {
            await context.TrainingCourseSubmissions
                .Where(submission => submission.CourseId == courseId &&
                                     challengeIds.Contains(submission.ExerciseChallengeId))
                .ExecuteDeleteAsync(cancellationToken);
            await context.TrainingCourseChapterChallenges
                .Where(item => item.CourseId == courseId &&
                               challengeIds.Contains(item.ExerciseChallengeId))
                .ExecuteDeleteAsync(cancellationToken);
            await context.ExerciseInstances
                .Where(instance => challengeIds.Contains(instance.ExerciseId))
                .ExecuteDeleteAsync(cancellationToken);
            await context.FlagContexts
                .Where(flag => flag.ExerciseId.HasValue && challengeIds.Contains(flag.ExerciseId.Value))
                .ExecuteDeleteAsync(cancellationToken);
            await context.TrainingCourseChallenges
                .Where(item => item.CourseId == courseId &&
                               challengeIds.Contains(item.ExerciseChallengeId))
                .ExecuteDeleteAsync(cancellationToken);

            foreach (var challenge in challenges)
            {
                await blobRepository.DeleteAttachment(challenge.Attachment, cancellationToken);
                context.ExerciseChallenges.Remove(challenge);
            }
        }

        await context.TrainingCourseChapters
            .Where(chapter => chapter.CourseId == courseId && chapter.ParentId != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(chapter => chapter.ParentId, (int?)null),
                cancellationToken);
        context.TrainingCourses.Remove(course);
        await context.SaveChangesAsync(cancellationToken);
        await imageDistribution.ReleaseTrainingCourseReferencesAsync(courseId, cancellationToken);
    }
}
