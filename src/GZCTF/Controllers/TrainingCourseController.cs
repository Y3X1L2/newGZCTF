using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Request.Training;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Config;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Controllers;

[RequireStudent]
[ApiController]
[Route("api/training/courses")]
public class TrainingCourseController(
    AppDbContext context,
    UserManager<UserInfo> userManager,
    IExerciseInstanceRepository exerciseInstanceRepository,
    IContainerRepository containerRepository,
    IConfigService configService,
    IOptionsSnapshot<ContainerPolicy> containerPolicy,
    ILogger<TrainingCourseController> logger) : ControllerBase
{
    private async Task<UserInfo> CurrentUser() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Current user is missing.");

    private static DateOnly Today() =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).DateTime);

    private IQueryable<TrainingCourse> CourseQuery() =>
        context.TrainingCourses
            .Include(c => c.Teachers)
            .ThenInclude(t => t.Teacher)
            .Include(c => c.Enrollments)
            .ThenInclude(e => e.User)
            .Include(c => c.Chapters)
            .ThenInclude(ch => ch.VideoFile)
            .Include(c => c.Resources)
            .ThenInclude(r => r.LocalFile)
            .Include(c => c.Challenges)
            .ThenInclude(ch => ch.ExerciseChallenge)
            .ThenInclude(ch => ch.Attachment)
            .ThenInclude(a => a!.LocalFile);

    private async Task<bool> CanEditCourse(UserInfo actor, TrainingCourse course, CancellationToken token) =>
        actor.Role >= Role.Admin ||
        await context.TrainingCourseTeachers.AnyAsync(t => t.CourseId == course.Id && t.TeacherId == actor.Id, token);

    private async Task<bool> CanManageTeachers(UserInfo actor, TrainingCourse course, CancellationToken token) =>
        actor.Role >= Role.Admin ||
        course.CreatedById == actor.Id ||
        await context.TrainingCourseTeachers.AnyAsync(t =>
            t.CourseId == course.Id &&
            t.TeacherId == actor.Id &&
            t.Role == TrainingCourseTeacherRole.Owner, token);

    private async Task<bool> CanLearnCourse(UserInfo actor, TrainingCourse course, CancellationToken token)
    {
        if (actor.Role >= Role.Admin || await CanEditCourse(actor, course, token))
            return true;

        return course.Status == TrainingCourseStatus.Published &&
               await context.TrainingCourseEnrollments.AnyAsync(e =>
                   e.CourseId == course.Id &&
                   e.UserId == actor.Id &&
                   e.Status == TrainingCourseEnrollmentStatus.Approved, token);
    }

    private IQueryable<TrainingCourse> VisibleCourseQuery(UserInfo user) =>
        CourseQuery()
            .Where(c => c.Status == TrainingCourseStatus.Published ||
                        user.Role >= Role.Admin ||
                        c.Teachers.Any(t => t.TeacherId == user.Id));

    private async Task<TrainingPersonalOverviewModel> BuildOverview(UserInfo user, CancellationToken token)
    {
        var courses = await VisibleCourseQuery(user).ToArrayAsync(token);
        var groupIds = context.StudentGroupMembers
            .Where(m => m.StudentId == user.Id)
            .Select(m => m.GroupId);
        var editableCourseIds = await context.TrainingCourseTeachers
            .Where(t => t.TeacherId == user.Id)
            .Select(t => t.CourseId)
            .ToArrayAsync(token);
        var learnableCourseIds = courses
            .Where(c => user.Role >= Role.Admin ||
                        editableCourseIds.Contains(c.Id) ||
                        c.Enrollments.Any(e => e.UserId == user.Id && e.Status == TrainingCourseEnrollmentStatus.Approved))
            .Select(c => c.Id)
            .ToArray();

        var progresses = await context.TrainingCourseProgresses
            .Where(p => p.UserId == user.Id && learnableCourseIds.Contains(p.CourseId))
            .ToArrayAsync(token);
        var chapterProgresses = await context.TrainingChapterProgresses
            .Where(p => p.UserId == user.Id && learnableCourseIds.Contains(p.CourseId))
            .ToArrayAsync(token);
        var submissions = await context.TrainingCourseSubmissions
            .Where(s => s.UserId == user.Id && learnableCourseIds.Contains(s.CourseId))
            .ToArrayAsync(token);
        var moduleProgresses = await context.TrainingModuleProgresses
            .Include(p => p.Module)
            .Where(p => p.UserId == user.Id)
            .ToArrayAsync(token);
        var visibleTheoryTotal = await context.TrainingModules.CountAsync(m =>
            m.IsPublished &&
            m.Type == TrainingType.Theory &&
            m.Direction.IsEnabled &&
            (user.Role >= Role.Teacher ||
             m.Visibilities.Any(v =>
                 v.VisibilityType == TrainingVisibilityType.AllStudents ||
                 v.GroupId.HasValue && groupIds.Contains(v.GroupId.Value))), token);
        var since = Today().AddDays(-41);
        var checkIns = await context.TrainingCheckIns
            .Where(c => c.UserId == user.Id && c.CheckInDate >= since)
            .OrderBy(c => c.CheckInDate)
            .ToArrayAsync(token);

        var totalChapters = courses
            .Where(c => learnableCourseIds.Contains(c.Id))
            .SelectMany(c => c.Chapters)
            .Count(c => c.IsPublished);
        var completedChapters = chapterProgresses
            .Count(p => p.Status == TrainingCourseProgressStatus.Completed);
        var ctfTotal = courses
            .Where(c => learnableCourseIds.Contains(c.Id))
            .Sum(c => c.Challenges.Count);
        var ctfSolved = submissions
            .Where(s => s.Status == AnswerResult.Accepted)
            .Select(s => new { s.CourseId, s.ExerciseChallengeId })
            .Distinct()
            .Count();

        var checkedDates = checkIns.Select(c => c.CheckInDate).ToHashSet();
        var today = Today();
        var streak = 0;
        for (var cursor = today; checkedDates.Contains(cursor); cursor = cursor.AddDays(-1))
            streak++;

        var activity = Enumerable.Range(0, 42)
            .Select(offset =>
            {
                var date = since.AddDays(offset);
                var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8))
                    .UtcDateTime;
                var dayEnd = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8))
                    .UtcDateTime;
                return new TrainingActivityPointModel
                {
                    Date = date,
                    CheckedIn = checkedDates.Contains(date),
                    CompletedChapters = chapterProgresses.Count(p =>
                        p.CompletedAt is not null &&
                        p.CompletedAt.Value.UtcDateTime >= dayStart &&
                        p.CompletedAt.Value.UtcDateTime < dayEnd),
                    AcceptedChallenges = submissions.Count(s =>
                        s.Status == AnswerResult.Accepted &&
                        s.SubmittedAt.UtcDateTime >= dayStart &&
                        s.SubmittedAt.UtcDateTime < dayEnd),
                    StudyActions = chapterProgresses.Count(p =>
                        p.UpdatedAt.UtcDateTime >= dayStart &&
                        p.UpdatedAt.UtcDateTime < dayEnd) +
                        submissions.Count(s =>
                            s.SubmittedAt.UtcDateTime >= dayStart &&
                            s.SubmittedAt.UtcDateTime < dayEnd)
                };
            })
            .ToList();

        return new TrainingPersonalOverviewModel
        {
            VisibleCourseCount = courses.Length,
            JoinedCourseCount = learnableCourseIds.Length,
            CompletedCourseCount = progresses.Count(p => p.Status == TrainingCourseProgressStatus.Completed),
            AverageProgress = totalChapters == 0 ? 0 : (int)Math.Round(completedChapters * 100.0 / totalChapters),
            CompletedChapterCount = completedChapters,
            TotalChapterCount = totalChapters,
            CtfSolvedChallenges = ctfSolved,
            CtfTotalChallenges = ctfTotal,
            TheoryCompletedModules = moduleProgresses.Count(p =>
                p.Module.Type == TrainingType.Theory && p.Status == TrainingModuleProgressStatus.Completed),
            TheoryTotalModules = visibleTheoryTotal,
            CheckInDays = await context.TrainingCheckIns.CountAsync(c => c.UserId == user.Id, token),
            CurrentCheckInStreak = streak,
            CheckedInToday = checkedDates.Contains(today),
            CheckIns = checkIns
                .Select(c => new TrainingCheckInModel
                {
                    Date = c.CheckInDate,
                    CheckedAt = c.CheckedAt,
                    IsToday = c.CheckInDate == today
                })
                .ToList(),
            Activity = activity
        };
    }

    private async Task<TrainingCourseProgress> EnsureCourseProgress(
        UserInfo user,
        TrainingCourse course,
        CancellationToken token)
    {
        var progress = await context.TrainingCourseProgresses
            .SingleOrDefaultAsync(p => p.CourseId == course.Id && p.UserId == user.Id, token);

        if (progress is not null)
            return progress;

        progress = new TrainingCourseProgress
        {
            CourseId = course.Id,
            UserId = user.Id,
            StartedAt = DateTimeOffset.UtcNow
        };
        context.TrainingCourseProgresses.Add(progress);
        return progress;
    }

    private async Task<TrainingCourseProgress> RecalculateProgress(
        UserInfo user,
        TrainingCourse course,
        CancellationToken token)
    {
        var progress = await EnsureCourseProgress(user, course, token);
        var publishedChapterIds = await context.TrainingCourseChapters
            .Where(c => c.CourseId == course.Id && c.IsPublished)
            .Select(c => c.Id)
            .ToArrayAsync(token);

        var completedChapterIds = await context.TrainingChapterProgresses
            .Where(p => p.UserId == user.Id &&
                        p.CourseId == course.Id &&
                        publishedChapterIds.Contains(p.ChapterId) &&
                        p.Status == TrainingCourseProgressStatus.Completed)
            .Select(p => p.ChapterId)
            .ToArrayAsync(token);
        var theoryRequiredChapterIds = await context.TrainingCourseChapterTheoryPapers
            .Where(p => p.CourseId == course.Id && p.IsPublished && publishedChapterIds.Contains(p.ChapterId))
            .Select(p => p.ChapterId)
            .ToArrayAsync(token);
        var theoryPassedChapterIds = await context.TrainingCourseChapterTheorySheets
            .Where(s => s.UserId == user.Id &&
                        s.CourseId == course.Id &&
                        s.Status == TheoryAnswerSheetStatus.Submitted &&
                        s.Passed &&
                        publishedChapterIds.Contains(s.ChapterId))
            .Select(s => s.ChapterId)
            .ToArrayAsync(token);
        var theoryRequiredSet = theoryRequiredChapterIds.ToHashSet();
        var theoryPassedSet = theoryPassedChapterIds.ToHashSet();
        var completedChapters = completedChapterIds.Count(id =>
            !theoryRequiredSet.Contains(id) || theoryPassedSet.Contains(id));

        var challengeIds = await context.TrainingCourseChallenges
            .Where(c => c.CourseId == course.Id)
            .Select(c => c.ExerciseChallengeId)
            .ToArrayAsync(token);

        var solvedCount = await context.TrainingCourseSubmissions
            .Where(s => s.UserId == user.Id &&
                        s.CourseId == course.Id &&
                        s.Status == AnswerResult.Accepted &&
                        challengeIds.Contains(s.ExerciseChallengeId))
            .Select(s => s.ExerciseChallengeId)
            .Distinct()
            .CountAsync(token);

        progress.TotalChapterCount = publishedChapterIds.Length;
        progress.CompletedChapterCount = completedChapters;
        progress.ChallengeTotalCount = challengeIds.Length;
        progress.ChallengeSolvedCount = solvedCount;
        progress.Status = publishedChapterIds.Length > 0 && completedChapters >= publishedChapterIds.Length
            ? TrainingCourseProgressStatus.Completed
            : completedChapters > 0 || solvedCount > 0
                ? TrainingCourseProgressStatus.Learning
                : TrainingCourseProgressStatus.NotStarted;
        progress.StartedAt ??= DateTimeOffset.UtcNow;
        progress.CompletedAt = progress.Status == TrainingCourseProgressStatus.Completed
            ? progress.CompletedAt ?? DateTimeOffset.UtcNow
            : null;
        progress.UpdatedAt = DateTimeOffset.UtcNow;

        return progress;
    }

    private async Task MarkChapterCompletedIfReady(
        UserInfo user,
        int courseId,
        int chapterId,
        CancellationToken token)
    {
        var requiredChallengeIds = await context.TrainingCourseChapterChallenges
            .Where(c => c.ChapterId == chapterId)
            .Join(context.TrainingCourseChallenges.Where(c => c.CourseId == courseId && c.IsRequired),
                c => new { c.CourseId, c.ExerciseChallengeId },
                c => new { c.CourseId, c.ExerciseChallengeId },
                (chapterChallenge, _) => chapterChallenge.ExerciseChallengeId)
            .Distinct()
            .ToArrayAsync(token);

        if (requiredChallengeIds.Length > 0)
        {
            var solvedIds = await context.TrainingCourseSubmissions
                .Where(s => s.CourseId == courseId &&
                            s.UserId == user.Id &&
                            s.Status == AnswerResult.Accepted &&
                            requiredChallengeIds.Contains(s.ExerciseChallengeId))
                .Select(s => s.ExerciseChallengeId)
                .Distinct()
                .ToArrayAsync(token);

            if (requiredChallengeIds.Except(solvedIds).Any())
                return;
        }

        var theoryPaper = await context.TrainingCourseChapterTheoryPapers
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.CourseId == courseId && p.ChapterId == chapterId && p.IsPublished, token);
        if (theoryPaper is not null)
        {
            var passed = await context.TrainingCourseChapterTheorySheets.AnyAsync(s =>
                s.CourseId == courseId &&
                s.ChapterId == chapterId &&
                s.UserId == user.Id &&
                s.Status == TheoryAnswerSheetStatus.Submitted &&
                s.Passed, token);

            if (!passed)
                return;
        }

        var progress = await context.TrainingChapterProgresses
            .SingleOrDefaultAsync(p => p.ChapterId == chapterId && p.UserId == user.Id, token);
        if (progress is null)
        {
            progress = new TrainingChapterProgress
            {
                ChapterId = chapterId,
                CourseId = courseId,
                UserId = user.Id,
                StartedAt = DateTimeOffset.UtcNow
            };
            context.TrainingChapterProgresses.Add(progress);
        }

        progress.Status = TrainingCourseProgressStatus.Completed;
        progress.CompletedAt ??= DateTimeOffset.UtcNow;
        progress.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<ExerciseInstance?> GetOrCreateCourseInstance(
        UserInfo user,
        TrainingCourse course,
        int exerciseChallengeId,
        int? chapterId,
        CancellationToken token)
    {
        var linked = await context.TrainingCourseChallenges.AnyAsync(c =>
            c.CourseId == course.Id && c.ExerciseChallengeId == exerciseChallengeId, token);
        if (!linked)
            return null;

        if (chapterId.HasValue)
        {
            var inChapter = await context.TrainingCourseChapterChallenges.AnyAsync(c =>
                c.CourseId == course.Id &&
                c.ChapterId == chapterId.Value &&
                c.ExerciseChallengeId == exerciseChallengeId, token);
            if (!inChapter)
                return null;
        }

        var instance = await context.ExerciseInstances
            .Include(i => i.FlagContext)
            .Include(i => i.Container)
            .SingleOrDefaultAsync(i => i.UserId == user.Id && i.ExerciseId == exerciseChallengeId, token);

        if (instance is null)
        {
            instance = new ExerciseInstance
            {
                UserId = user.Id,
                ExerciseId = exerciseChallengeId,
                IsLoaded = false
            };
            context.ExerciseInstances.Add(instance);
            await context.SaveChangesAsync(token);
        }

        return await exerciseInstanceRepository.GetInstance(user, exerciseChallengeId, token);
    }

    private async Task<TrainingCourseChapterTheorySheet> GetOrCreateTheorySheet(
        UserInfo user,
        TrainingCourseChapterTheoryPaper paper,
        CancellationToken token)
    {
        var sheet = await context.TrainingCourseChapterTheorySheets
            .Include(s => s.Answers)
            .SingleOrDefaultAsync(s => s.ChapterId == paper.ChapterId && s.UserId == user.Id, token);

        if (sheet is not null)
            return sheet;

        sheet = new TrainingCourseChapterTheorySheet
        {
            CourseId = paper.CourseId,
            ChapterId = paper.ChapterId,
            PaperId = paper.Id,
            UserId = user.Id,
            MaxScore = paper.Questions.Sum(q => q.Score)
        };
        context.TrainingCourseChapterTheorySheets.Add(sheet);
        await context.SaveChangesAsync(token);
        return sheet;
    }

    private static string? ApplyTheoryAnswers(
        TrainingCourseChapterTheorySheet sheet,
        TrainingCourseChapterTheoryPaper paper,
        List<TheoryAnswerModel> answers)
    {
        var questions = paper.Questions.ToDictionary(q => q.Id);
        var incoming = answers
            .GroupBy(a => a.PaperQuestionId)
            .ToDictionary(g => g.Key, g => TheoryExamService.NormalizeIndexes(g.Last().SelectedIndexes));

        foreach (var (questionId, selected) in incoming)
        {
            if (!questions.TryGetValue(questionId, out var question))
                return $"Question {questionId} does not belong to this chapter paper.";

            if (selected.Any(i => i < 0 || i >= question.Options.Count))
                return $"Answer index is out of range for question {questionId}.";

            if (question.Type is TheoryQuestionType.SingleChoice or TheoryQuestionType.TrueFalse && selected.Count > 1)
                return $"Question {questionId} accepts only one answer.";
        }

        sheet.Answers.RemoveAll(a => !incoming.ContainsKey(a.PaperQuestionId));

        foreach (var question in questions.Values)
        {
            var selected = incoming.GetValueOrDefault(question.Id, []);
            var answer = sheet.Answers.FirstOrDefault(a => a.PaperQuestionId == question.Id);
            if (answer is null)
            {
                answer = new TrainingCourseChapterTheoryAnswer { PaperQuestionId = question.Id };
                sheet.Answers.Add(answer);
            }

            answer.SelectedIndexes = selected;
            answer.IsCorrect = null;
            answer.Score = 0;
        }

        sheet.PaperId = paper.Id;
        sheet.MaxScore = paper.Questions.Sum(q => q.Score);
        sheet.UpdatedAt = DateTimeOffset.UtcNow;
        return null;
    }

    private static void GradeTheorySheet(
        TrainingCourseChapterTheorySheet sheet,
        TrainingCourseChapterTheoryPaper paper)
    {
        var answers = sheet.Answers.ToDictionary(a => a.PaperQuestionId);
        var score = 0;

        foreach (var question in paper.Questions)
        {
            if (!answers.TryGetValue(question.Id, out var answer))
                continue;

            var selected = TheoryExamService.NormalizeIndexes(answer.SelectedIndexes);
            var correct = selected.SequenceEqual(TheoryExamService.NormalizeIndexes(question.AnswerIndexes));
            answer.SelectedIndexes = selected;
            answer.IsCorrect = correct;
            answer.Score = correct ? question.Score : 0;
            score += answer.Score;
        }

        sheet.Score = score;
        sheet.MaxScore = paper.Questions.Sum(q => q.Score);
        sheet.Status = TheoryAnswerSheetStatus.Submitted;
        sheet.SubmittedAt = DateTimeOffset.UtcNow;
        sheet.UpdatedAt = sheet.SubmittedAt.Value;
        sheet.Passed = sheet.MaxScore == 0 || (int)Math.Round(sheet.Score * 100.0 / sheet.MaxScore) >= paper.PassRate;
    }

    [HttpGet]
    [ProducesResponseType(typeof(TrainingCourseModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Courses(CancellationToken token = default)
    {
        var user = await CurrentUser();
        var query = VisibleCourseQuery(user);

        var courses = await query.OrderByDescending(c => c.UpdatedAt).ToArrayAsync(token);
        var courseIds = courses.Select(c => c.Id).ToArray();
        var progresses = await context.TrainingCourseProgresses
            .Where(p => p.UserId == user.Id && courseIds.Contains(p.CourseId))
            .ToDictionaryAsync(p => p.CourseId, token);

        var models = new List<TrainingCourseModel>();
        foreach (var course in courses)
        {
            var enrollment = course.Enrollments.SingleOrDefault(e => e.UserId == user.Id);
            var canEdit = await CanEditCourse(user, course, token);
            var canManageTeachers = await CanManageTeachers(user, course, token);
            var canLearn = await CanLearnCourse(user, course, token);
            models.Add(TrainingCourseModel.FromCourse(
                course,
                enrollment,
                progresses.GetValueOrDefault(course.Id),
                canLearn,
                canEdit,
                canManageTeachers,
                canEdit,
                false));
        }

        return Ok(models.ToArray());
    }

    [HttpGet("overview")]
    [ProducesResponseType(typeof(TrainingPersonalOverviewModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview(CancellationToken token = default)
    {
        var user = await CurrentUser();
        return Ok(await BuildOverview(user, token));
    }

    [HttpPost("check-in")]
    [ProducesResponseType(typeof(TrainingPersonalOverviewModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckIn(CancellationToken token = default)
    {
        var user = await CurrentUser();
        var today = Today();
        var checkIn = await context.TrainingCheckIns
            .SingleOrDefaultAsync(c => c.UserId == user.Id && c.CheckInDate == today, token);
        if (checkIn is null)
        {
            context.TrainingCheckIns.Add(new TrainingCheckIn
            {
                UserId = user.Id,
                CheckInDate = today,
                CheckedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync(token);
        }

        return Ok(await BuildOverview(user, token));
    }

    [HttpGet("{courseId:int}")]
    [ProducesResponseType(typeof(TrainingCourseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Course([FromRoute] int courseId, CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();

        var canEdit = await CanEditCourse(user, course, token);
        if (course.Status != TrainingCourseStatus.Published && !canEdit && user.Role < Role.Admin)
            return NotFound();

        var enrollment = course.Enrollments.SingleOrDefault(e => e.UserId == user.Id);
        var canManageTeachers = await CanManageTeachers(user, course, token);
        var canLearn = await CanLearnCourse(user, course, token);
        var progress = await context.TrainingCourseProgresses
            .SingleOrDefaultAsync(p => p.UserId == user.Id && p.CourseId == courseId, token);

        var model = TrainingCourseModel.FromCourse(
            course,
            enrollment,
            progress,
            canLearn,
            canEdit,
            canManageTeachers,
            canEdit || user.Role >= Role.Admin,
            true);

        if (model.Chapters.Count > 0)
        {
            var chapterIds = model.Chapters.Select(c => c.Id).ToArray();
            var papers = await context.TrainingCourseChapterTheoryPapers
                .AsNoTracking()
                .Include(p => p.Questions)
                .Where(p => p.CourseId == courseId && chapterIds.Contains(p.ChapterId))
                .ToArrayAsync(token);
            var paperIds = papers.Select(p => p.Id).ToArray();
            var sheets = paperIds.Length == 0
                ? Array.Empty<TrainingCourseChapterTheorySheet>()
                : await context.TrainingCourseChapterTheorySheets
                    .AsNoTracking()
                    .Where(s => s.UserId == user.Id && paperIds.Contains(s.PaperId))
                    .ToArrayAsync(token);
            var sheetsByPaperId = sheets.ToDictionary(s => s.PaperId);
            var papersByChapterId = papers.ToDictionary(
                p => p.ChapterId,
                p => TrainingCourseChapterTheorySummaryModel.FromPaper(
                    p,
                    sheetsByPaperId.GetValueOrDefault(p.Id)));

            foreach (var chapter in model.Chapters)
            {
                chapter.TheoryPaper = papersByChapterId.GetValueOrDefault(chapter.Id);
            }
        }

        return Ok(model);
    }

    [HttpPost("{courseId:int}/enroll")]
    [ProducesResponseType(typeof(TrainingCourseEnrollmentModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Enroll(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseEnrollmentApplyModel model,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await context.TrainingCourses.SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null || course.Status != TrainingCourseStatus.Published)
            return NotFound();

        var status = course.EnrollmentPolicy == TrainingCourseEnrollmentPolicy.AutoApprove
            ? TrainingCourseEnrollmentStatus.Approved
            : TrainingCourseEnrollmentStatus.Pending;

        var enrollment = await context.TrainingCourseEnrollments
            .Include(e => e.User)
            .SingleOrDefaultAsync(e => e.CourseId == courseId && e.UserId == user.Id, token);

        if (enrollment is null)
        {
            enrollment = new TrainingCourseEnrollment
            {
                CourseId = courseId,
                UserId = user.Id,
                User = user,
                RequestedAt = DateTimeOffset.UtcNow
            };
            context.TrainingCourseEnrollments.Add(enrollment);
        }

        enrollment.Status = status;
        enrollment.ApplyReason = model.ApplyReason.Trim();
        enrollment.ReviewComment = string.Empty;
        enrollment.ReviewedAt = status == TrainingCourseEnrollmentStatus.Approved ? DateTimeOffset.UtcNow : null;
        enrollment.ReviewedById = status == TrainingCourseEnrollmentStatus.Approved ? user.Id : null;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(token);
        return Ok(TrainingCourseEnrollmentModel.FromEnrollment(enrollment));
    }

    [HttpDelete("{courseId:int}/enroll")]
    public async Task<IActionResult> CancelEnroll([FromRoute] int courseId, CancellationToken token = default)
    {
        var user = await CurrentUser();
        var enrollment = await context.TrainingCourseEnrollments
            .SingleOrDefaultAsync(e => e.CourseId == courseId && e.UserId == user.Id, token);
        if (enrollment is null)
            return NotFound();

        enrollment.Status = TrainingCourseEnrollmentStatus.Cancelled;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        return Ok();
    }

    [HttpGet("{courseId:int}/chapters/{chapterId:int}")]
    [ProducesResponseType(typeof(TrainingCourseChapterModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Chapter(
        [FromRoute] int courseId,
        [FromRoute] int chapterId,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var chapter = await context.TrainingCourseChapters
            .Include(c => c.VideoFile)
            .Include(c => c.Challenges)
            .SingleOrDefaultAsync(c => c.Id == chapterId && c.CourseId == courseId, token);
        if (chapter is null || (!chapter.IsPublished && !await CanEditCourse(user, course, token)))
            return NotFound();

        var progress = await context.TrainingChapterProgresses
            .SingleOrDefaultAsync(p => p.ChapterId == chapterId && p.UserId == user.Id, token);
        var solvedIds = await context.TrainingCourseSubmissions
            .Where(s => s.CourseId == courseId && s.UserId == user.Id && s.Status == AnswerResult.Accepted)
            .Select(s => s.ExerciseChallengeId)
            .Distinct()
            .ToArrayAsync(token);
        var challengeIds = chapter.Challenges.Select(c => c.ExerciseChallengeId).ToArray();
        var courseChallenges = await context.TrainingCourseChallenges
            .Include(c => c.ExerciseChallenge)
            .Where(c => c.CourseId == courseId && challengeIds.Contains(c.ExerciseChallengeId))
            .OrderBy(c => c.Order)
            .ToArrayAsync(token);
        var challengeModels = courseChallenges
            .Select(c => TrainingCourseChallengeModel.FromChallenge(c, chapterId, solvedIds.Contains(c.ExerciseChallengeId)))
            .ToArray();
        var canEditCourse = await CanEditCourse(user, course, token);
        var theoryPaper = await context.TrainingCourseChapterTheoryPapers
            .Include(p => p.Questions)
            .SingleOrDefaultAsync(p =>
                p.CourseId == courseId &&
                p.ChapterId == chapterId &&
                (p.IsPublished || canEditCourse), token);
        var theorySheet = theoryPaper is null
            ? null
            : await context.TrainingCourseChapterTheorySheets
                .SingleOrDefaultAsync(s => s.ChapterId == chapterId && s.UserId == user.Id, token);
        var theorySummary = theoryPaper is null
            ? null
            : TrainingCourseChapterTheorySummaryModel.FromPaper(theoryPaper, theorySheet);

        return Ok(TrainingCourseChapterModel.FromChapter(chapter, progress, challengeModels, theoryPaper: theorySummary));
    }

    [HttpPost("{courseId:int}/chapters/{chapterId:int}/complete")]
    public async Task<IActionResult> CompleteChapter(
        [FromRoute] int courseId,
        [FromRoute] int chapterId,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var chapter = await context.TrainingCourseChapters
            .SingleOrDefaultAsync(c => c.Id == chapterId && c.CourseId == courseId && c.IsPublished, token);
        if (chapter is null)
            return NotFound();

        await MarkChapterCompletedIfReady(user, courseId, chapterId, token);
        await RecalculateProgress(user, course, token);
        await context.SaveChangesAsync(token);

        return Ok();
    }

    [HttpGet("{courseId:int}/resources/{resourceId:int}/download")]
    public async Task<IActionResult> DownloadResource(
        [FromRoute] int courseId,
        [FromRoute] int resourceId,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var resource = await context.TrainingCourseResources
            .Include(r => r.LocalFile)
            .SingleOrDefaultAsync(r => r.Id == resourceId && r.CourseId == courseId && r.IsVisible, token);
        if (resource is null)
            return NotFound();

        var url = resource.Type == TrainingCourseResourceType.File
            ? resource.LocalFile?.Url()
            : resource.ExternalUrl;
        if (string.IsNullOrWhiteSpace(url))
            return NotFound();

        return Redirect(url);
    }

    [HttpGet("{courseId:int}/chapters/{chapterId:int}/theory")]
    [ProducesResponseType(typeof(TrainingCourseChapterTheoryPlayerPaperModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChapterTheory(
        [FromRoute] int courseId,
        [FromRoute] int chapterId,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var paper = await context.TrainingCourseChapterTheoryPapers
            .Include(p => p.Questions)
            .SingleOrDefaultAsync(p => p.CourseId == courseId && p.ChapterId == chapterId && p.IsPublished, token);
        if (paper is null)
            return NotFound(new RequestResponse("课后测试尚未发布。", StatusCodes.Status404NotFound));

        var sheet = await GetOrCreateTheorySheet(user, paper, token);
        return Ok(TrainingCourseChapterTheoryPlayerPaperModel.FromPaper(paper, sheet));
    }

    [HttpPut("{courseId:int}/chapters/{chapterId:int}/theory/draft")]
    [ProducesResponseType(typeof(TrainingCourseChapterTheoryPlayerPaperModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveChapterTheoryDraft(
        [FromRoute] int courseId,
        [FromRoute] int chapterId,
        [FromBody] TheoryAnswerSheetEditModel model,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var paper = await context.TrainingCourseChapterTheoryPapers
            .Include(p => p.Questions)
            .SingleOrDefaultAsync(p => p.CourseId == courseId && p.ChapterId == chapterId && p.IsPublished, token);
        if (paper is null)
            return NotFound(new RequestResponse("课后测试尚未发布。", StatusCodes.Status404NotFound));

        var sheet = await GetOrCreateTheorySheet(user, paper, token);
        if (sheet.Status == TheoryAnswerSheetStatus.Submitted)
            return BadRequest(new RequestResponse("课后测试已提交，不能继续修改。"));
        if (ApplyTheoryAnswers(sheet, paper, model.Answers) is { } error)
            return BadRequest(new RequestResponse(error));

        await context.SaveChangesAsync(token);
        return Ok(TrainingCourseChapterTheoryPlayerPaperModel.FromPaper(paper, sheet));
    }

    [HttpPost("{courseId:int}/chapters/{chapterId:int}/theory/submit")]
    [ProducesResponseType(typeof(TrainingCourseChapterTheoryPlayerPaperModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitChapterTheory(
        [FromRoute] int courseId,
        [FromRoute] int chapterId,
        [FromBody] TheoryAnswerSheetEditModel model,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var paper = await context.TrainingCourseChapterTheoryPapers
            .Include(p => p.Questions)
            .SingleOrDefaultAsync(p => p.CourseId == courseId && p.ChapterId == chapterId && p.IsPublished, token);
        if (paper is null)
            return NotFound(new RequestResponse("课后测试尚未发布。", StatusCodes.Status404NotFound));

        var sheet = await GetOrCreateTheorySheet(user, paper, token);
        if (sheet.Status == TheoryAnswerSheetStatus.Submitted)
            return BadRequest(new RequestResponse("课后测试已提交，不能重复提交。"));
        if (ApplyTheoryAnswers(sheet, paper, model.Answers) is { } error)
            return BadRequest(new RequestResponse(error));

        GradeTheorySheet(sheet, paper);
        await context.SaveChangesAsync(token);

        await MarkChapterCompletedIfReady(user, courseId, chapterId, token);
        await RecalculateProgress(user, course, token);
        await context.SaveChangesAsync(token);

        return Ok(TrainingCourseChapterTheoryPlayerPaperModel.FromPaper(paper, sheet));
    }

    [HttpGet("{courseId:int}/challenges/{challengeId:int}")]
    [ProducesResponseType(typeof(TrainingCourseChallengeDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Challenge(
        [FromRoute] int courseId,
        [FromRoute] int challengeId,
        [FromQuery] int? chapterId = null,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var instance = await GetOrCreateCourseInstance(user, course, challengeId, chapterId, token);
        if (instance is null)
            return NotFound();

        var attempts = await context.TrainingCourseSubmissions
            .CountAsync(s => s.UserId == user.Id && s.CourseId == courseId && s.ExerciseChallengeId == challengeId, token);
        var solved = await context.TrainingCourseSubmissions.AnyAsync(s =>
            s.UserId == user.Id &&
            s.CourseId == courseId &&
            s.ExerciseChallengeId == challengeId &&
            s.Status == AnswerResult.Accepted, token);

        return Ok(TrainingCourseChallengeDetailModel.FromInstance(courseId, chapterId, instance, attempts, solved));
    }

    [HttpPost("{courseId:int}/challenges/{challengeId:int}/container")]
    [ProducesResponseType(typeof(ContainerInfoModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateContainer(
        [FromRoute] int courseId,
        [FromRoute] int challengeId,
        [FromQuery] int? chapterId = null,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var instance = await GetOrCreateCourseInstance(user, course, challengeId, chapterId, token);
        if (instance is null || !instance.Exercise.IsEnabled)
            return NotFound(new RequestResponse("课程题目不存在或未启用。", StatusCodes.Status404NotFound));
        if (!instance.Exercise.Type.IsContainer())
            return BadRequest(new RequestResponse("该课程题目不需要启动容器。"));
        if (instance.IsContainerOperationTooFrequent)
            return RequestResponse.Result("操作过于频繁，请稍后再试。", StatusCodes.Status429TooManyRequests);

        if (instance.Container is not null)
        {
            if (instance.Container.Status == ContainerStatus.Running)
                return Ok(ContainerInfoModel.FromContainer(instance.Container));

            await containerRepository.DestroyContainer(instance.Container, token);
        }

        var result = await exerciseInstanceRepository.CreateContainer(instance, user, token);
        if (result.Status != TaskStatus.Success || result.Result is null)
            return BadRequest(new RequestResponse("课程容器创建失败，请稍后重试。"));

        await RecalculateProgress(user, course, token);
        await context.SaveChangesAsync(token);

        logger.Log($"创建课程容器：{course.Title} / {instance.Exercise.Title}", user, TaskStatus.Success);
        return Ok(ContainerInfoModel.FromContainer(result.Result));
    }

    [HttpPost("{courseId:int}/challenges/{challengeId:int}/container/extend")]
    [ProducesResponseType(typeof(ContainerInfoModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExtendContainer(
        [FromRoute] int courseId,
        [FromRoute] int challengeId,
        [FromQuery] int? chapterId = null,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var instance = await GetOrCreateCourseInstance(user, course, challengeId, chapterId, token);
        if (instance is null || !instance.Exercise.IsEnabled)
            return NotFound(new RequestResponse("课程题目不存在或未启用。", StatusCodes.Status404NotFound));
        if (!instance.Exercise.Type.IsContainer())
            return BadRequest(new RequestResponse("该课程题目不需要启动容器。"));
        if (instance.Container is null)
            return BadRequest(new RequestResponse("课程容器尚未启动。"));
        if (instance.Container.ExpectStopAt - DateTimeOffset.UtcNow >
            TimeSpan.FromMinutes(containerPolicy.Value.RenewalWindow))
            return BadRequest(new RequestResponse("当前还未进入实例续期窗口。"));

        await containerRepository.ExtendLifetime(instance.Container,
            TimeSpan.FromMinutes(containerPolicy.Value.ExtensionDuration), token);

        return Ok(ContainerInfoModel.FromContainer(instance.Container));
    }

    [HttpDelete("{courseId:int}/challenges/{challengeId:int}/container")]
    public async Task<IActionResult> DestroyContainer(
        [FromRoute] int courseId,
        [FromRoute] int challengeId,
        [FromQuery] int? chapterId = null,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var instance = await GetOrCreateCourseInstance(user, course, challengeId, chapterId, token);
        if (instance is null)
            return NotFound();
        if (!instance.Exercise.Type.IsContainer())
            return BadRequest(new RequestResponse("该课程题目不需要启动容器。"));
        if (instance.Container is null)
            return BadRequest(new RequestResponse("课程容器尚未启动。"));
        if (instance.IsContainerOperationTooFrequent)
            return RequestResponse.Result("操作过于频繁，请稍后再试。", StatusCodes.Status429TooManyRequests);

        if (!await containerRepository.DestroyContainer(instance.Container, token))
            return BadRequest(new RequestResponse("课程容器销毁失败。"));

        instance.LastContainerOperation = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        return Ok();
    }

    [HttpPost("{courseId:int}/challenges/{challengeId:int}/submit")]
    [ProducesResponseType(typeof(TrainingCourseSubmitResultModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitFlag(
        [FromRoute] int courseId,
        [FromRoute] int challengeId,
        [FromQuery] int? chapterId,
        [FromBody] FlagSubmitModel model,
        CancellationToken token = default)
    {
        var answer = configService.DecryptApiData(model.Flag)?.Trim() ?? model.Flag.Trim();
        if (string.IsNullOrWhiteSpace(answer))
            return BadRequest(new RequestResponse("Flag 不能为空。"));
        if (answer.Length > Limits.MaxFlagLength)
            return BadRequest(new RequestResponse("Flag 过长。"));

        var user = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanLearnCourse(user, course, token))
            return Forbid();

        var instance = await GetOrCreateCourseInstance(user, course, challengeId, chapterId, token);
        if (instance is null)
            return NotFound(new RequestResponse("课程题目不存在。", StatusCodes.Status404NotFound));

        var attempts = await context.TrainingCourseSubmissions
            .CountAsync(s => s.UserId == user.Id && s.CourseId == courseId && s.ExerciseChallengeId == challengeId, token);
        if (instance.Exercise.SubmissionLimit > 0 && attempts >= instance.Exercise.SubmissionLimit)
            return BadRequest(new RequestResponse("该课程题目的提交次数已用完。"));

        var verify = await exerciseInstanceRepository.VerifyAnswer(user, instance, answer, model.FlagId, token);
        var submission = new TrainingCourseSubmission
        {
            CourseId = courseId,
            ChapterId = chapterId,
            ExerciseChallengeId = challengeId,
            UserId = user.Id,
            Status = verify.Status,
            SubmittedAnswerHash = answer.ToSHA256String(),
            FlagId = verify.FlagId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty
        };
        context.TrainingCourseSubmissions.Add(submission);
        await context.SaveChangesAsync(token);

        if (verify.Status == AnswerResult.Accepted && chapterId.HasValue)
            await MarkChapterCompletedIfReady(user, courseId, chapterId.Value, token);

        var courseProgress = await RecalculateProgress(user, course, token);
        await context.SaveChangesAsync(token);

        var chapterCompleted = chapterId.HasValue && await context.TrainingChapterProgresses.AnyAsync(p =>
            p.ChapterId == chapterId.Value &&
            p.UserId == user.Id &&
            p.Status == TrainingCourseProgressStatus.Completed, token);

        if (verify.Status == AnswerResult.Accepted)
            logger.Log($"课程题目解出：{course.Title} / {instance.Exercise.Title}", user, TaskStatus.Success);

        return Ok(new TrainingCourseSubmitResultModel
        {
            SubmissionId = submission.Id,
            Status = verify.Status,
            ChapterCompleted = chapterCompleted,
            CourseCompleted = courseProgress.Status == TrainingCourseProgressStatus.Completed
        });
    }
}
