using GZCTF.Models.Request.Training;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Training.Application;

/// <summary>Enriches an already-authorized course model with the caller's progress and visible chapter bindings.</summary>
public sealed class TrainingCourseDetailProjectionService(AppDbContext context)
{
    public async Task PopulateAsync(TrainingCourseModel model, Guid userId, CancellationToken token)
    {
        if (model.Chapters.Count == 0)
            return;

        var chapterIds = model.Chapters.Select(chapter => chapter.Id).ToArray();
        var progresses = await context.TrainingChapterProgresses.AsNoTracking()
            .Where(item => item.CourseId == model.Id && item.UserId == userId && chapterIds.Contains(item.ChapterId))
            .ToDictionaryAsync(item => item.ChapterId, token);
        var challengeIds = model.Challenges.Select(challenge => challenge.ExerciseChallengeId).ToArray();
        var links = await context.TrainingCourseChapterChallenges.AsNoTracking()
            .Include(link => link.CourseChallenge).ThenInclude(link => link.ExerciseChallenge)
            .ThenInclude(challenge => challenge.Attachment).ThenInclude(attachment => attachment!.LocalFile)
            .Where(link => link.CourseId == model.Id && chapterIds.Contains(link.ChapterId) &&
                           challengeIds.Contains(link.ExerciseChallengeId))
            .OrderBy(link => link.Order).ThenBy(link => link.ExerciseChallengeId)
            .ToArrayAsync(token);
        var solvedIds = (await context.TrainingCourseSubmissions.AsNoTracking()
            .Where(item => item.CourseId == model.Id && item.UserId == userId &&
                           item.Status == AnswerResult.Accepted && challengeIds.Contains(item.ExerciseChallengeId))
            .Select(item => item.ExerciseChallengeId).Distinct().ToArrayAsync(token)).ToHashSet();
        var linksByChapter = links.ToLookup(link => link.ChapterId);
        var linksByChallenge = links.ToLookup(link => link.ExerciseChallengeId);

        foreach (var chapter in model.Chapters)
        {
            var progress = progresses.GetValueOrDefault(chapter.Id);
            chapter.ProgressStatus = progress?.Status;
            chapter.ReadPercent = progress?.ReadPercent ?? 0;
            chapter.CompletedAt = progress?.CompletedAt;
            chapter.Challenges = linksByChapter[chapter.Id]
                .Select(link => TrainingCourseChallengeModel.FromChallenge(
                    link.CourseChallenge, chapter.Id, solvedIds.Contains(link.ExerciseChallengeId)))
                .ToList();
        }

        foreach (var challenge in model.Challenges)
        {
            var chapters = linksByChallenge[challenge.ExerciseChallengeId].Select(link => link.ChapterId).ToArray();
            // The legacy scalar can represent one binding; the chapter collection preserves all bindings.
            challenge.ChapterId = chapters.Length == 1 ? chapters[0] : null;
            challenge.Solved = solvedIds.Contains(challenge.ExerciseChallengeId);
        }

        // Historic course aggregates may lag the chapter rows by one completion.
        var published = model.Chapters.Where(chapter => chapter.IsPublished).ToArray();
        model.TotalChapterCount = published.Length;
        model.CompletedChapterCount = published.Count(chapter => chapter.ProgressStatus == TrainingCourseProgressStatus.Completed);
        model.ProgressStatus = published.Length > 0 && model.CompletedChapterCount == published.Length
            ? TrainingCourseProgressStatus.Completed
            : model.CompletedChapterCount > 0 || solvedIds.Count > 0 ||
              published.Any(chapter => chapter.ProgressStatus == TrainingCourseProgressStatus.Learning)
                ? TrainingCourseProgressStatus.Learning
                : TrainingCourseProgressStatus.NotStarted;
    }
}
