using GZCTF.Models;
using GZCTF.Models.Request.Exercise;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Exercise.Application;

internal static class ExerciseCatalogQuery
{
    public static async Task<ExerciseCatalogPageModel> PageAsync(AppDbContext context,
        IQueryable<ExerciseChallenge> visible, ExerciseFilter? filter, ExercisePagination pagination,
        Guid? userId, CancellationToken token)
    {
        var query = ApplyFilter(context, visible, filter, userId);
        var total = await query.CountAsync(token);
        var page = Math.Clamp(pagination.Page, 1, Math.Max(1, (int)Math.Ceiling((double)total / pagination.PageSize)));
        var items = await InfoModels(context, query.OrderBy(exercise => exercise.Id)
            .Skip((page - 1) * pagination.PageSize).Take(pagination.PageSize), userId).ToArrayAsync(token);
        return new ExerciseCatalogPageModel
        {
            Items = items, Total = total, Page = page, PageSize = pagination.PageSize,
            EnabledCount = await query.CountAsync(exercise => exercise.IsEnabled, token),
            ContainerCount = await query.CountAsync(exercise =>
                exercise.Type == ChallengeType.StaticContainer || exercise.Type == ChallengeType.DynamicContainer, token),
            // Filter choices span the visible catalog, not just the current page.
            Tags = await visible.Where(exercise => exercise.Tags != null).SelectMany(exercise => exercise.Tags!)
                .Distinct().OrderBy(tag => tag).ToArrayAsync(token)
        };
    }

    static IQueryable<ExerciseChallenge> ApplyFilter(AppDbContext context,
        IQueryable<ExerciseChallenge> query, ExerciseFilter? filter, Guid? userId)
    {
        if (filter is null) return query;
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            var pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            int.TryParse(search.TrimStart('#'), out var id);
            var hasCategory = Enum.TryParse<ChallengeCategory>(search, true, out var category);
            query = query.Where(exercise => exercise.Id == id || (hasCategory && exercise.Category == category) ||
                EF.Functions.ILike(exercise.Title, pattern, "\\") || EF.Functions.ILike(exercise.Content, pattern, "\\") ||
                (exercise.Tags != null && exercise.Tags.Any(tag => EF.Functions.ILike(tag, pattern, "\\"))));
        }
        if (filter.Categories is { Length: > 0 })
            query = query.Where(exercise => filter.Categories.Contains(exercise.Category));
        if (filter.Difficulties is { Length: > 0 })
            query = query.Where(exercise => filter.Difficulties.Contains(exercise.Difficulty));
        if (filter.Tags is { Length: > 0 })
            query = query.Where(exercise => exercise.Tags != null && exercise.Tags.Any(tag => filter.Tags.Contains(tag)));
        if (filter.Credit is { } credit)
            query = query.Where(exercise => exercise.Credit == credit);
        if (filter.Sources is { Length: > 0 })
            query = query.Where(exercise => filter.Sources.Contains(exercise.PoolSource));
        if (filter.Solved is { } solved && userId.HasValue)
            query = query.Where(exercise => context.ExerciseInstances.Any(instance =>
                instance.UserId == userId.Value && instance.ExerciseId == exercise.Id &&
                instance.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0)) == solved);
        return query;
    }

    public static IQueryable<ExerciseInfoModel> InfoModels(AppDbContext context,
        IQueryable<ExerciseChallenge> query, Guid? userId) => query.Select(exercise => new ExerciseInfoModel
        {
            Id = exercise.Id, Title = exercise.Title, Difficulty = exercise.Difficulty, Category = exercise.Category,
            Type = exercise.Type, IsEnabled = exercise.IsEnabled, Tags = exercise.Tags ?? new(),
            Credit = exercise.Credit, PoolSource = exercise.PoolSource,
            AcceptedCount = context.ExerciseInstances.Count(instance => instance.ExerciseId == exercise.Id &&
                instance.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0)),
            SubmissionCount = context.ExerciseSubmissions.Count(submission => submission.ExerciseChallengeId == exercise.Id),
            Solved = userId.HasValue && context.ExerciseInstances.Any(instance => instance.UserId == userId.Value &&
                instance.ExerciseId == exercise.Id && instance.SolveTimeUtc > DateTimeOffset.FromUnixTimeSeconds(0)),
            UserAcceptedCount = userId.HasValue ? context.ExerciseSubmissions.Count(submission =>
                submission.UserId == userId.Value && submission.ExerciseChallengeId == exercise.Id &&
                submission.Status == AnswerResult.Accepted) : 0,
            UserSubmissionCount = userId.HasValue ? context.ExerciseSubmissions.Count(submission =>
                submission.UserId == userId.Value && submission.ExerciseChallengeId == exercise.Id) : 0
        });
}
