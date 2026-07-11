using GZCTF.Models;
using GZCTF.Modules.Content.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Exercise.Infrastructure;

public sealed class ExerciseImageTemplateReferenceProvider(AppDbContext context)
    : IImageTemplateReferenceProvider
{
    public string Module => "Exercise";

    public async Task<IReadOnlyList<ImageTemplateReference>> GetReferencesAsync(
        int imageTemplateId,
        CancellationToken cancellationToken)
    {
        var items = await context.ExerciseChallenges.AsNoTracking()
            .Where(challenge => challenge.ImageTemplateId == imageTemplateId &&
                                challenge.TrainingCourseId == null)
            .Select(challenge => new { challenge.Id, challenge.Title })
            .ToArrayAsync(cancellationToken);
        return items.Select(item => new ImageTemplateReference(
            Module, "challenge", item.Id.ToString(), item.Title)).ToArray();
    }
}
