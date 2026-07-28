using GZCTF.Repositories.Interface;
using GZCTF.Models.Request.Exercise;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Exercise.Application;

public sealed class ExerciseService(
    AppDbContext context,
    IExerciseInstanceRepository instanceRepository) : IExerciseService
{
    public async Task<ExerciseChallenge[]> GetExercisesAsync(CancellationToken token = default) =>
        await context.ExerciseChallenges
            .AsNoTracking()
            .Where(e => e.IsEnabled && e.TrainingCourseId == null)
            .OrderBy(e => e.Id)
            .ToArrayAsync(token);

    public async Task<ExerciseChallenge?> GetExerciseByIdAsync(int exerciseId, CancellationToken token = default) =>
        await context.ExerciseChallenges
            .AsNoTracking()
            .Include(e => e.Flags)
            .Include(e => e.Attachment)
            .FirstOrDefaultAsync(e => e.Id == exerciseId && e.IsEnabled && e.TrainingCourseId == null, token);

    public async Task<ExerciseInfoModel[]> GetExerciseListAsync(ExerciseFilter? filter, CancellationToken token = default)
    {
        var query = context.ExerciseChallenges
            .AsNoTracking()
            .Where(e => e.IsEnabled && e.TrainingCourseId == null);

        if (filter is null)
            return await BuildInfoModels(query).ToArrayAsync(token);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.ToLower();
            query = query.Where(e =>
                EF.Functions.ILike(e.Title, $"%{search}%") ||
                EF.Functions.ILike(e.Content, $"%{search}%"));
        }

        if (filter.Categories is { Length: > 0 })
            query = query.Where(e => filter.Categories.Contains(e.Category));

        if (filter.Difficulties is { Length: > 0 })
            query = query.Where(e => filter.Difficulties.Contains(e.Difficulty));

        if (filter.Tags is { Length: > 0 })
            query = query.Where(e => e.Tags != null && e.Tags.Any(t => filter.Tags.Contains(t)));

        if (filter.Credit.HasValue)
            query = query.Where(e => e.Credit == filter.Credit.Value);

        return await BuildInfoModels(query).ToArrayAsync(token);
    }

    public async Task<ExerciseDetailModel?> GetExerciseDetailAsync(UserInfo user, int exerciseId, CancellationToken token = default)
    {
        var instance = await instanceRepository.GetInstance(user, exerciseId, token);
        if (instance is null)
            return null;

        return ExerciseDetailModel.FromInstance(instance);
    }

    public async Task<(AnswerResult Status, int? FlagId)> SubmitFlagAsync(UserInfo user, int exerciseId, string answer,
        int? flagId = null, CancellationToken token = default)
    {
        var instance = await instanceRepository.GetInstance(user, exerciseId, token);
        if (instance is null)
            return (AnswerResult.NotFound, null);

        return await instanceRepository.VerifyAnswer(user, instance, answer, 0, flagId, token);
    }

    public async Task<TaskResult<Container>> CreateContainerAsync(UserInfo user, int exerciseId, CancellationToken token = default)
    {
        var instance = await instanceRepository.GetInstance(user, exerciseId, token);
        if (instance is null)
            return new TaskResult<Container>(TaskStatus.Failed);

        return await instanceRepository.CreateContainer(instance, user, token);
    }

    IQueryable<ExerciseInfoModel> BuildInfoModels(IQueryable<ExerciseChallenge> query) =>
        query.Select(e => new ExerciseInfoModel
        {
            Id = e.Id,
            Title = e.Title,
            Difficulty = e.Difficulty,
            Category = e.Category,
            Type = e.Type,
            IsEnabled = e.IsEnabled,
            Tags = e.Tags ?? new(),
            Credit = e.Credit,
            AcceptedCount = context.ExerciseInstances.Count(i =>
                i.ExerciseId == e.Id && i.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0)),
            SubmissionCount = context.ExerciseInstances.Count(i => i.ExerciseId == e.Id)
        });
}
