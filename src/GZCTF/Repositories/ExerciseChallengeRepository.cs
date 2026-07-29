using System.Diagnostics.CodeAnalysis;
using GZCTF.Models.Request.Edit;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

[ExcludeFromCodeCoverage(Justification = "Exercise feature not yet implemented")]
public class ExerciseChallengeRepository(AppDbContext context, IBlobRepository blobRepository)
    : RepositoryBase(context),
        IExerciseChallengeRepository
{
    public async Task<ExerciseChallenge> CreateExercise(ExerciseChallenge exercise, CancellationToken token = default)
    {
        if (exercise.TrainingCourseId is not null)
            throw new InvalidOperationException("The public exercise repository cannot create course-owned exercises.");

        await Context.AddAsync(exercise, token);
        await SaveAsync(token);
        return exercise;
    }

    public Task<ExerciseChallenge[]> GetExercises(CancellationToken token = default) =>
        Context.ExerciseChallenges.OrderBy(e => e.Id).ToArrayAsync(token);

    public async Task RemoveExercise(ExerciseChallenge exercise, CancellationToken token = default)
    {
        if (exercise.TrainingCourseId is not null)
            throw new InvalidOperationException("The public exercise repository cannot remove course-owned exercises.");

        await blobRepository.DeleteAttachment(exercise.Attachment, token);

        await Context.Entry(exercise).Collection(item => item.Flags).LoadAsync(token);
        foreach (var flag in exercise.Flags)
            await blobRepository.DeleteAttachment(flag.Attachment, token);

        Context.RemoveRange(exercise.Flags);
        Context.Remove(exercise);
        await SaveAsync(token);
    }

    public async Task UpdateAttachment(ExerciseChallenge exercise, AttachmentCreateModel model,
        CancellationToken token = default)
    {
        var attachment = model.ToAttachment(await blobRepository.GetBlobByHash(model.FileHash, token));

        await blobRepository.DeleteAttachment(exercise.Attachment, token);

        if (attachment is not null)
            await Context.AddAsync(attachment, token);

        exercise.Attachment = attachment;

        await SaveAsync(token);
    }
}
