using GZCTF.Models;
using GZCTF.Modules.Content.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Training.Infrastructure;

public sealed class TrainingImageTemplateReferenceProvider(AppDbContext context)
    : IImageTemplateReferenceProvider
{
    public string Module => "Training";

    public async Task<IReadOnlyList<ImageTemplateReference>> GetReferencesAsync(
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        var courses = await context.TrainingCourseImageTemplateBindings.AsNoTracking()
            .Where(binding => binding.ImageTemplateId == imageTemplateId)
            .Join(
                context.TrainingCourses.AsNoTracking(),
                binding => binding.CourseId,
                course => course.Id,
                (binding, course) => new { course.Id, course.Title })
            .ToArrayAsync(cancellationToken);
        var challenges = await context.ExerciseChallenges.AsNoTracking()
            .Where(challenge => challenge.ImageTemplateId == imageTemplateId &&
                                challenge.TrainingCourseId != null)
            .Select(challenge => new { challenge.Id, challenge.Title })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. courses.Select(item => new ImageTemplateReference(
                Module, "course", item.Id.ToString(), item.Title)),
            .. challenges.Select(item => new ImageTemplateReference(
                Module, "course-challenge", item.Id.ToString(), item.Title))
        ];
    }
}
