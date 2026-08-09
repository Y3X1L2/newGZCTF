using GZCTF.Middlewares;
using GZCTF.Modules.Training.Domain;
using GZCTF.Modules.Training.Application;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Infrastructure.Cache;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Request.Training;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Fleet;
using GZCTF.Services.Training;
using GZCTF.Services.Vm;
using GZCTF.Storage;
using GZCTF.Utils;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[RequireTeacher]
[ApiController]
[Route("api/admin/training/courses")]
public class TrainingCourseAdminController(
    AppDbContext context,
    UserManager<UserInfo> userManager,
    ImageStorage imageStorage,
    IArchiveExtractor archiveExtractor,
    IBlobRepository blobRepository,
    DockerImageRegistryService dockerRegistry,
    TheoryExamService theoryService,
    TrainingCourseDeletionService courseDeletion,
    ImageDistributionService imageDistribution,
    ImageImportApplicationService imageImports,
    IProjectionRevisionStore projectionRevisions,
    ILogger<TrainingCourseAdminController> logger) : ControllerBase
{
    private async Task<UserInfo> CurrentUser() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Current user is missing.");

    private IQueryable<ImageTemplate> CourseTemplates(int courseId) =>
        context.TrainingCourseImageTemplateBindings
            .Where(binding => binding.CourseId == courseId)
            .Join(
                context.ImageTemplates,
                binding => binding.ImageTemplateId,
                template => template.Id,
                (_, template) => template);

    private Task<bool> CourseHasTemplateAsync(int courseId, int templateId, CancellationToken token) =>
        context.TrainingCourseImageTemplateBindings.AnyAsync(
            binding => binding.CourseId == courseId && binding.ImageTemplateId == templateId,
            token);

    private async Task BindTemplateAsync(
        int courseId,
        int templateId,
        Guid actorId,
        CancellationToken token)
    {
        if (await CourseHasTemplateAsync(courseId, templateId, token))
            return;

        context.TrainingCourseImageTemplateBindings.Add(new TrainingCourseImageTemplateBinding
        {
            CourseId = courseId,
            ImageTemplateId = templateId,
            AddedById = actorId
        });
        await context.SaveChangesAsync(token);
    }

    async Task DistributeCourseImageAsync(
        int courseId,
        int? templateId,
        string reason,
        CancellationToken token)
    {
        if (!templateId.HasValue)
            return;

        try
        {
            var template = await context.ImageTemplates.SingleAsync(
                item => item.Id == templateId.Value,
                token);
            await imageDistribution.DistributeToCapableNodesAsync(
                template,
                token,
                ImageDistributionReferenceKey.TrainingCourse(courseId));
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException or AgentClientException)
        {
            logger.LogWarning(ex,
                "Failed to distribute training image for course {CourseId}, template {TemplateId} after {Reason}.",
                courseId, templateId.Value, reason);
            throw;
        }
    }

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

    private async Task<bool> CanEditCourse(UserInfo actor, int courseId, CancellationToken token) =>
        actor.Role >= Role.Admin ||
        await context.TrainingCourseTeachers.AnyAsync(t => t.CourseId == courseId && t.TeacherId == actor.Id, token);

    private async Task<bool> CanManageTeachers(UserInfo actor, TrainingCourse course, CancellationToken token) =>
        actor.Role >= Role.Admin ||
        course.CreatedById == actor.Id ||
        await context.TrainingCourseTeachers.AnyAsync(t =>
            t.CourseId == course.Id &&
            t.TeacherId == actor.Id &&
            t.Role == TrainingCourseTeacherRole.Owner, token);

    private static bool CanDeleteCourse(UserInfo actor, TrainingCourse course) =>
        TrainingCourseAccessPolicy.CanDelete(actor, course);

    private async Task<TrainingCourse?> EditableCourse(UserInfo actor, int courseId, CancellationToken token)
    {
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return null;

        return await CanEditCourse(actor, courseId, token) ? course : null;
    }

    private async Task<LocalFile?> ResolveFile(string? hash, CancellationToken token) =>
        string.IsNullOrWhiteSpace(hash)
            ? null
            : await context.Files.SingleOrDefaultAsync(f => f.Hash == hash.Trim(), token);

    private async Task<Attachment?> ResolveAttachment(
        FileType attachmentType,
        string? fileHash,
        string? remoteUrl,
        CancellationToken token)
    {
        return attachmentType switch
        {
            FileType.None => null,
            FileType.Local => await ResolveFile(fileHash, token) is { } file
                ? new Attachment { Type = FileType.Local, LocalFileId = file.Id, LocalFile = file }
                : throw new InvalidOperationException("附件文件不存在。"),
            FileType.Remote => !string.IsNullOrWhiteSpace(remoteUrl)
                ? new Attachment { Type = FileType.Remote, RemoteUrl = remoteUrl.Trim() }
                : throw new InvalidOperationException("外链附件需要填写 URL。"),
            _ => throw new InvalidOperationException("不支持的附件类型。")
        };
    }

    private async Task<IActionResult?> ValidateCourseChallengeModel(
        int courseId,
        TrainingCourseChallengeCreateModel model,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            return BadRequest(new RequestResponse("题目名称不能为空。"));
        if (model.Type == ChallengeType.DynamicAttachment)
            return BadRequest(new RequestResponse("课程题目暂不支持动态附件，请使用静态附件或容器题目。"));
        if (model.Type.IsContainer() && model.Environment == EnvironmentType.Docker &&
            string.IsNullOrWhiteSpace(model.ContainerImage))
            return BadRequest(new RequestResponse("Docker 容器题目需要配置镜像。"));
        if (model.Environment == EnvironmentType.WindowsVM && !model.ImageTemplateId.HasValue)
            return BadRequest(new RequestResponse("Windows 靶机题目需要选择课程内 VM 模板。"));
        if (model.AttachmentType == FileType.Local && string.IsNullOrWhiteSpace(model.AttachmentFileHash))
            return BadRequest(new RequestResponse("本地附件需要先上传文件。"));
        if (model.AttachmentType == FileType.Remote && string.IsNullOrWhiteSpace(model.AttachmentRemoteUrl))
            return BadRequest(new RequestResponse("外链附件需要填写 URL。"));

        if (!string.IsNullOrWhiteSpace(model.AttachmentFileHash) &&
            await ResolveFile(model.AttachmentFileHash, token) is null)
            return BadRequest(new RequestResponse("附件文件不存在。"));

        if (model.ImageTemplateId.HasValue)
        {
            var ownsTemplate = await CourseHasTemplateAsync(
                courseId, model.ImageTemplateId.Value, token);
            if (!ownsTemplate)
                return BadRequest(new RequestResponse("只能使用当前课程的环境模板。"));
        }

        if (model.ChapterId.HasValue)
        {
            var chapterExists = await context.TrainingCourseChapters
                .AnyAsync(c => c.Id == model.ChapterId.Value && c.CourseId == courseId, token);
            if (!chapterExists)
                return BadRequest(new RequestResponse("课程章节不存在。"));
        }

        return null;
    }

    private async Task ApplyCourseChallengeModel(
        int courseId,
        TrainingCourseChallenge link,
        ExerciseChallenge exercise,
        TrainingCourseChallengeCreateModel model,
        CancellationToken token)
    {
        var exerciseType = model.Environment == EnvironmentType.WindowsVM && model.ImageTemplateId.HasValue
            ? ChallengeType.StaticContainer
            : model.Type;

        exercise.Title = model.Title.Trim();
        exercise.Content = model.Content;
        exercise.Category = model.Category;
        exercise.Type = exerciseType;
        exercise.Environment = model.Environment;
        exercise.ImageTemplateId = model.ImageTemplateId;
        exercise.ContainerImage = model.ContainerImage?.Trim();
        exercise.MemoryLimit = model.MemoryLimit;
        exercise.CPUCount = model.CPUCount;
        exercise.StorageLimit = model.StorageLimit;
        exercise.ExposePort = model.ExposePort;
        exercise.NetworkMode = model.NetworkMode;
        exercise.FlagTemplate = model.FlagTemplate;
        exercise.SubmissionLimit = model.SubmissionLimit;
        exercise.IsEnabled = true;
        exercise.TrainingCourseId = courseId;

        link.Order = model.Order;
        link.IsRequired = model.IsRequired;
        link.DisplayTitle = string.IsNullOrWhiteSpace(model.DisplayTitle) ? null : model.DisplayTitle.Trim();

        await ReplaceCourseChallengeAttachment(exercise, model, token);
        SyncCourseChallengeStaticFlag(exercise, model);
    }

    private async Task ReplaceCourseChallengeAttachment(
        ExerciseChallenge exercise,
        TrainingCourseChallengeCreateModel model,
        CancellationToken token)
    {
        var attachment = await ResolveAttachment(
            model.AttachmentType,
            model.AttachmentFileHash,
            model.AttachmentRemoteUrl,
            token);

        if (IsSameAttachment(exercise.Attachment, attachment))
            return;

        await blobRepository.DeleteAttachment(exercise.Attachment, token);
        exercise.Attachment = attachment;
    }

    private static bool IsSameAttachment(Attachment? current, Attachment? next) =>
        (current, next) switch
        {
            (null, null) => true,
            ({ Type: FileType.None }, null) => true,
            (null, { Type: FileType.None }) => true,
            ({ Type: FileType.Local } left, { Type: FileType.Local } right) =>
                left.LocalFileId.HasValue && left.LocalFileId == right.LocalFileId,
            ({ Type: FileType.Remote } left, { Type: FileType.Remote } right) =>
                string.Equals(left.RemoteUrl?.Trim(), right.RemoteUrl?.Trim(), StringComparison.Ordinal),
            _ => false
        };

    private static void SyncCourseChallengeStaticFlag(ExerciseChallenge exercise, TrainingCourseChallengeCreateModel model)
    {
        if (exercise.Type.IsDynamic())
        {
            exercise.Flags.Clear();
            return;
        }

        var flag = model.StaticFlag?.Trim();
        exercise.Flags.Clear();

        if (string.IsNullOrWhiteSpace(flag))
            return;

        exercise.Flags.Add(new FlagContext
        {
            Flag = flag,
            OrderIndex = 0,
            ScoreMode = FlagScoreMode.InheritDecay,
            AnswerType = AnswerType.Flag
        });
    }

    private async Task SetCourseChallengeChapterLink(
        int courseId,
        int exerciseChallengeId,
        int? chapterId,
        int order,
        CancellationToken token)
    {
        await context.TrainingCourseChapterChallenges
            .Where(c => c.CourseId == courseId && c.ExerciseChallengeId == exerciseChallengeId)
            .ExecuteDeleteAsync(token);

        if (!chapterId.HasValue)
            return;

        context.TrainingCourseChapterChallenges.Add(new TrainingCourseChapterChallenge
        {
            CourseId = courseId,
            ChapterId = chapterId.Value,
            ExerciseChallengeId = exerciseChallengeId,
            Order = order
        });
    }

    private static void FillCourse(TrainingCourse course, TrainingCourseEditModel model, UserInfo actor)
    {
        course.Title = model.Title.Trim();
        course.Slug = string.IsNullOrWhiteSpace(model.Slug) ? model.Title.Trim() : model.Slug.Trim();
        course.Summary = model.Summary.Trim();
        course.Description = model.Description;
        course.CoverFileHash = string.IsNullOrWhiteSpace(model.CoverFileHash) ? null : model.CoverFileHash.Trim();
        course.Tags = model.Tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToList();
        course.EnrollmentPolicy = model.EnrollmentPolicy;
        course.UpdatedById = actor.Id;
        course.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<List<TrainingCourseStudentLearningSummaryModel>> BuildLearningSummaries(
        int courseId,
        Guid[]? userFilter,
        CancellationToken token)
    {
        var enrollmentsQuery = context.TrainingCourseEnrollments
            .AsNoTracking()
            .Include(e => e.User)
            .Where(e => e.CourseId == courseId);

        if (userFilter is { Length: > 0 })
            enrollmentsQuery = enrollmentsQuery.Where(e => userFilter.Contains(e.UserId));

        var enrollments = await enrollmentsQuery
            .OrderBy(e => e.Status)
            .ThenBy(e => e.User.RealName)
            .ThenBy(e => e.User.UserName)
            .ToArrayAsync(token);
        var userIds = enrollments.Select(e => e.UserId).ToArray();
        if (userIds.Length == 0)
            return [];

        var totalChapterCount = await context.TrainingCourseChapters
            .CountAsync(c => c.CourseId == courseId && c.IsPublished, token);
        var challengeIds = await context.TrainingCourseChallenges
            .Where(c => c.CourseId == courseId)
            .Select(c => c.ExerciseChallengeId)
            .ToArrayAsync(token);
        var challengeTotal = challengeIds.Length;
        var paperIds = await context.TrainingCourseChapterTheoryPapers
            .Where(p => p.CourseId == courseId && p.IsPublished)
            .Select(p => p.Id)
            .ToArrayAsync(token);
        var theoryTotal = paperIds.Length;

        var progresses = await context.TrainingCourseProgresses
            .AsNoTracking()
            .Where(p => p.CourseId == courseId && userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, token);
        var submissions = await context.TrainingCourseSubmissions
            .AsNoTracking()
            .Where(s => s.CourseId == courseId && userIds.Contains(s.UserId))
            .ToArrayAsync(token);
        var sheets = await context.TrainingCourseChapterTheorySheets
            .AsNoTracking()
            .Where(s => s.CourseId == courseId && userIds.Contains(s.UserId) && paperIds.Contains(s.PaperId))
            .ToArrayAsync(token);

        var submissionsByUser = submissions
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var sheetsByUser = sheets
            .GroupBy(s => s.UserId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(sheet => sheet.PaperId)
                    .Select(attempts => attempts
                        .OrderByDescending(sheet => sheet.AttemptNumber)
                        .ThenByDescending(sheet => sheet.Id)
                        .First())
                    .ToArray());

        return enrollments.Select(enrollment =>
        {
            var progress = progresses.GetValueOrDefault(enrollment.UserId);
            var userSubmissions = submissionsByUser.GetValueOrDefault(enrollment.UserId) ?? [];
            var userSheets = sheetsByUser.GetValueOrDefault(enrollment.UserId) ?? [];
            var submittedSheets = userSheets.Where(s => s.Status == TheoryAnswerSheetStatus.Submitted).ToArray();
            var solvedCount = userSubmissions
                .Where(s => s.Status == AnswerResult.Accepted)
                .Select(s => s.ExerciseChallengeId)
                .Distinct()
                .Count();

            return new TrainingCourseStudentLearningSummaryModel
            {
                UserId = enrollment.UserId,
                UserName = enrollment.User.UserName ?? string.Empty,
                RealName = enrollment.User.RealName,
                StdNumber = enrollment.User.StdNumber,
                EnrollmentStatus = enrollment.Status,
                CompletedChapterCount = progress?.CompletedChapterCount ?? 0,
                TotalChapterCount = progress?.TotalChapterCount ?? totalChapterCount,
                ChallengeSolvedCount = solvedCount,
                ChallengeTotalCount = progress?.ChallengeTotalCount ?? challengeTotal,
                TheorySubmittedCount = submittedSheets.Length,
                TheoryPassedCount = submittedSheets.Count(s => s.Passed),
                TheoryTotalCount = theoryTotal,
                TheoryScore = submittedSheets.Sum(s => s.Score),
                TheoryMaxScore = submittedSheets.Sum(s => s.MaxScore),
                ProgressStatus = progress?.Status,
                LastActivityAt = MaxDate(
                    progress?.UpdatedAt,
                    userSubmissions.Select(s => (DateTimeOffset?)s.SubmittedAt).DefaultIfEmpty().Max(),
                    userSheets.Select(s => (DateTimeOffset?)s.UpdatedAt).DefaultIfEmpty().Max())
            };
        }).ToList();
    }

    private static DateTimeOffset? MaxDate(params DateTimeOffset?[] values)
    {
        var max = values.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty().Max();
        return max == default ? null : max;
    }

    [HttpGet]
    [ProducesResponseType(typeof(TrainingCourseModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Courses(CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var query = CourseQuery()
            .Where(c => c.Status != TrainingCourseStatus.Archived);
        if (actor.Role < Role.Admin)
            query = query.Where(c => c.Teachers.Any(t => t.TeacherId == actor.Id));

        var courses = await query.OrderByDescending(c => c.UpdatedAt).ToArrayAsync(token);
        var models = courses.Select(course => TrainingCourseModel.FromCourse(
            course,
            canLearn: true,
            canEdit: true,
            canManageTeachers: actor.Role >= Role.Admin ||
                               course.CreatedById == actor.Id ||
                               course.Teachers.Any(t => t.TeacherId == actor.Id && t.Role == TrainingCourseTeacherRole.Owner),
            canManageEnrollments: true,
            canDelete: CanDeleteCourse(actor, course),
            includeDetail: false)).ToArray();

        return Ok(models);
    }

    [HttpGet("{courseId:int}")]
    [ProducesResponseType(typeof(TrainingCourseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Course([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var course = await EditableCourse(actor, courseId, token);
        if (course is null)
            return NotFound();

        return Ok(TrainingCourseModel.FromCourse(
            course,
            canLearn: true,
            canEdit: true,
            canManageTeachers: await CanManageTeachers(actor, course, token),
            canManageEnrollments: true,
            canDelete: CanDeleteCourse(actor, course),
            includeDetail: true));
    }

    [HttpPost]
    [ProducesResponseType(typeof(TrainingCourseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCourse([FromBody] TrainingCourseEditModel model, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (string.IsNullOrWhiteSpace(model.Title))
            return BadRequest(new RequestResponse("课程名称不能为空。"));

        var course = new TrainingCourse
        {
            CreatedById = actor.Id,
            UpdatedById = actor.Id,
            Status = TrainingCourseStatus.Draft
        };
        FillCourse(course, model, actor);
        course.Teachers.Add(new TrainingCourseTeacher
        {
            TeacherId = actor.Id,
            Teacher = actor,
            Role = TrainingCourseTeacherRole.Owner,
            AssignedById = actor.Id
        });

        context.TrainingCourses.Add(course);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Created training course {course.Title}.", TaskStatus.Success, LogLevel.Information);

        course = await CourseQuery().SingleAsync(c => c.Id == course.Id, token);
        return Ok(TrainingCourseModel.FromCourse(
            course,
            canLearn: true,
            canEdit: true,
            canManageTeachers: true,
            canManageEnrollments: true,
            canDelete: true,
            includeDetail: true));
    }

    [HttpPut("{courseId:int}")]
    public async Task<IActionResult> UpdateCourse(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var course = await EditableCourse(actor, courseId, token);
        if (course is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(model.Title))
            return BadRequest(new RequestResponse("课程名称不能为空。"));

        FillCourse(course, model, actor);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Updated training course {course.Title}.", TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpPost("{courseId:int}/publish")]
    public async Task<IActionResult> Publish([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var course = await EditableCourse(actor, courseId, token);
        if (course is null)
            return NotFound();
        if (course.Chapters.Count == 0)
            return BadRequest(new RequestResponse("发布课程前至少需要添加一个章节。"));

        course.Status = TrainingCourseStatus.Published;
        course.PublishedAt ??= DateTimeOffset.UtcNow;
        course.ArchivedAt = null;
        course.UpdatedById = actor.Id;
        course.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Published training course {course.Title}.", TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpPost("{courseId:int}/archive")]
    public async Task<IActionResult> Archive([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var course = await EditableCourse(actor, courseId, token);
        if (course is null)
            return NotFound();

        course.Status = TrainingCourseStatus.Archived;
        course.ArchivedAt = DateTimeOffset.UtcNow;
        course.UpdatedById = actor.Id;
        course.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Archived training course {course.Title}.", TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpPost("{courseId:int}/draft")]
    public async Task<IActionResult> MoveToDraft([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var course = await EditableCourse(actor, courseId, token);
        if (course is null)
            return NotFound();

        course.Status = TrainingCourseStatus.Draft;
        course.ArchivedAt = null;
        course.UpdatedById = actor.Id;
        course.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Moved training course {course.Title} to draft.", TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpDelete("{courseId:int}")]
    public async Task<IActionResult> DeleteCourse([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var result = await courseDeletion.DeleteAsync(
            courseId, new ActorContext(actor.Id, actor.Role), token);
        if (result.Status == TrainingCourseDeletionStatus.NotFound)
            return NotFound();
        if (result.Status == TrainingCourseDeletionStatus.Forbidden)
            return Forbid();

        logger.SystemLog($"Deleted training course {result.Title}.", TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpGet("{courseId:int}/enrollments")]
    [ProducesResponseType(typeof(TrainingCourseEnrollmentModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Enrollments([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var enrollments = await context.TrainingCourseEnrollments
            .Include(e => e.User)
            .Where(e => e.CourseId == courseId)
            .OrderBy(e => e.Status)
            .ThenByDescending(e => e.RequestedAt)
            .ToArrayAsync(token);

        var totalChapterCount = await context.TrainingCourseChapters
            .CountAsync(c => c.CourseId == courseId && c.IsPublished, token);
        var progresses = await context.TrainingCourseProgresses
            .Where(p => p.CourseId == courseId)
            .ToDictionaryAsync(p => p.UserId, token);

        return Ok(enrollments
            .Select(e => TrainingCourseEnrollmentModel.FromEnrollment(
                e,
                progresses.GetValueOrDefault(e.UserId),
                totalChapterCount))
            .ToArray());
    }

    [HttpGet("{courseId:int}/learning-summaries")]
    [ProducesResponseType(typeof(TrainingCourseStudentLearningSummaryModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> LearningSummaries([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        return Ok(await BuildLearningSummaries(courseId, null, token));
    }

    [HttpGet("{courseId:int}/students/{userId:guid}/learning")]
    [ProducesResponseType(typeof(TrainingCourseStudentLearningDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> StudentLearningDetail(
        [FromRoute] int courseId,
        [FromRoute] Guid userId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var summary = (await BuildLearningSummaries(courseId, [userId], token)).SingleOrDefault();
        if (summary is null)
            return NotFound();

        var chapters = await context.TrainingCourseChapters
            .AsNoTracking()
            .Where(c => c.CourseId == courseId && c.IsPublished)
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Id)
            .ToArrayAsync(token);
        var chapterIds = chapters.Select(c => c.Id).ToArray();

        var chapterProgresses = await context.TrainingChapterProgresses
            .AsNoTracking()
            .Where(p => p.CourseId == courseId && p.UserId == userId && chapterIds.Contains(p.ChapterId))
            .ToDictionaryAsync(p => p.ChapterId, token);
        var chapterChallengeLinks = await context.TrainingCourseChapterChallenges
            .AsNoTracking()
            .Include(c => c.CourseChallenge)
            .ThenInclude(c => c.ExerciseChallenge)
            .Where(c => c.CourseId == courseId && chapterIds.Contains(c.ChapterId))
            .OrderBy(c => c.Order)
            .ToArrayAsync(token);
        var challengeIds = chapterChallengeLinks.Select(c => c.ExerciseChallengeId).Distinct().ToArray();
        var submissions = await context.TrainingCourseSubmissions
            .AsNoTracking()
            .Where(s => s.CourseId == courseId && s.UserId == userId && challengeIds.Contains(s.ExerciseChallengeId))
            .OrderByDescending(s => s.SubmittedAt)
            .ToArrayAsync(token);
        var submissionsByChallenge = submissions
            .GroupBy(s => s.ExerciseChallengeId)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var instances = await context.ExerciseInstances
            .AsNoTracking()
            .Include(i => i.Container)
            .Where(i => i.UserId == userId && challengeIds.Contains(i.ExerciseId))
            .ToDictionaryAsync(i => i.ExerciseId, token);

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
                .Include(s => s.Answers)
                .Where(s => s.CourseId == courseId && s.UserId == userId && paperIds.Contains(s.PaperId))
                .ToArrayAsync(token);
        var papersByChapter = papers.ToDictionary(p => p.ChapterId);
        var sheetsByPaper = sheets
            .GroupBy(sheet => sheet.PaperId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(sheet => sheet.AttemptNumber).ThenByDescending(sheet => sheet.Id).First());
        var challengeLinksByChapter = chapterChallengeLinks
            .GroupBy(c => c.ChapterId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var detail = new TrainingCourseStudentLearningDetailModel
        {
            UserId = summary.UserId,
            UserName = summary.UserName,
            RealName = summary.RealName,
            StdNumber = summary.StdNumber,
            EnrollmentStatus = summary.EnrollmentStatus,
            CompletedChapterCount = summary.CompletedChapterCount,
            TotalChapterCount = summary.TotalChapterCount,
            ChallengeSolvedCount = summary.ChallengeSolvedCount,
            ChallengeTotalCount = summary.ChallengeTotalCount,
            TheorySubmittedCount = summary.TheorySubmittedCount,
            TheoryPassedCount = summary.TheoryPassedCount,
            TheoryTotalCount = summary.TheoryTotalCount,
            TheoryScore = summary.TheoryScore,
            TheoryMaxScore = summary.TheoryMaxScore,
            ProgressStatus = summary.ProgressStatus,
            LastActivityAt = summary.LastActivityAt
        };

        detail.Chapters = chapters.Select(chapter =>
        {
            var progress = chapterProgresses.GetValueOrDefault(chapter.Id);
            var theory = BuildStudentTheoryModel(
                papersByChapter.GetValueOrDefault(chapter.Id),
                sheetsByPaper);
            var chapterChallenges = challengeLinksByChapter.GetValueOrDefault(chapter.Id) ??
                                    Array.Empty<TrainingCourseChapterChallenge>();

            return new TrainingCourseStudentChapterLearningModel
            {
                ChapterId = chapter.Id,
                Title = chapter.Title,
                Summary = chapter.Summary,
                Order = chapter.Order,
                IsPublished = chapter.IsPublished,
                CompletionPolicy = chapter.CompletionPolicy,
                ProgressStatus = progress?.Status,
                ReadPercent = progress?.ReadPercent ?? 0,
                CompletedAt = progress?.CompletedAt,
                Theory = theory,
                Challenges = chapterChallenges.Select(link =>
                    BuildStudentChallengeModel(
                        link,
                        submissionsByChallenge.GetValueOrDefault(link.ExerciseChallengeId) ??
                        Array.Empty<TrainingCourseSubmission>(),
                        instances.GetValueOrDefault(link.ExerciseChallengeId))).ToList()
            };
        }).ToList();

        return Ok(detail);
    }

    private static TrainingCourseStudentTheoryLearningModel? BuildStudentTheoryModel(
        TrainingCourseChapterTheoryPaper? paper,
        Dictionary<int, TrainingCourseChapterTheorySheet> sheetsByPaper)
    {
        if (paper is null)
            return null;

        var sheet = sheetsByPaper.GetValueOrDefault(paper.Id);
        var submitted = sheet?.Status == TheoryAnswerSheetStatus.Submitted;

        return new TrainingCourseStudentTheoryLearningModel
        {
            PaperId = paper.Id,
            Title = paper.Title,
            IsPublished = paper.IsPublished,
            QuestionCount = paper.ActiveQuestions.Count(),
            TotalScore = paper.ActiveQuestions.Sum(question => question.Score),
            PassRate = paper.PassRate,
            Status = sheet?.Status,
            Score = submitted ? sheet?.Score : null,
            Passed = submitted ? sheet?.Passed : null,
            CorrectCount = submitted ? sheet!.Answers.Count(a => a.IsCorrect == true) : 0,
            SubmittedAt = sheet?.SubmittedAt,
            Answers = submitted
                ? sheet!.Answers
                    .OrderBy(answer => answer.QuestionOrder)
                    .ThenBy(answer => answer.Id)
                    .Select(answer => new TrainingCourseStudentTheoryAnswerDetailModel
                    {
                        QuestionId = answer.PaperQuestionId,
                        Type = answer.QuestionType,
                        Title = answer.QuestionTitle,
                        Content = answer.QuestionContent,
                        Options = answer.QuestionOptions,
                        AnswerIndexes = answer.CorrectAnswerIndexes,
                        SelectedIndexes = answer.SelectedIndexes,
                        IsCorrect = answer.IsCorrect,
                        Score = answer.Score,
                        MaxScore = answer.MaxScore,
                        Order = answer.QuestionOrder
                    }).ToList()
                : []
        };
    }

    private static TrainingCourseStudentChallengeLearningModel BuildStudentChallengeModel(
        TrainingCourseChapterChallenge link,
        TrainingCourseSubmission[] submissions,
        ExerciseInstance? instance)
    {
        var last = submissions.OrderByDescending(s => s.SubmittedAt).FirstOrDefault();
        var acceptedCount = submissions.Count(s => s.Status == AnswerResult.Accepted);
        var challenge = link.CourseChallenge.ExerciseChallenge;
        var activeContainer = instance?.Container is { Status: ContainerStatus.Running } container &&
                              container.IsActiveAt(DateTimeOffset.UtcNow)
            ? container
            : null;

        return new TrainingCourseStudentChallengeLearningModel
        {
            ExerciseChallengeId = link.ExerciseChallengeId,
            Title = challenge.Title,
            DisplayTitle = link.CourseChallenge.DisplayTitle,
            Category = challenge.Category,
            Type = challenge.Type,
            Environment = challenge.Environment,
            IsRequired = link.CourseChallenge.IsRequired,
            Solved = acceptedCount > 0,
            SubmissionCount = submissions.Length,
            AcceptedSubmissionCount = acceptedCount,
            LastStatus = last?.Status,
            LastSubmittedAt = last?.SubmittedAt,
            LastIpAddress = last?.IpAddress,
            InstanceEntry = activeContainer?.ReadyEntry,
            InstanceStopAt = activeContainer?.ExpectStopAt
        };
    }

    [HttpPut("{courseId:int}/enrollments/{userId:guid}")]
    public async Task<IActionResult> ReviewEnrollment(
        [FromRoute] int courseId,
        [FromRoute] Guid userId,
        [FromBody] TrainingCourseEnrollmentReviewModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        if (model.Status is not TrainingCourseEnrollmentStatus.Approved and not TrainingCourseEnrollmentStatus.Rejected)
            return BadRequest(new RequestResponse("报名审核只能通过或拒绝。"));

        var enrollment = await context.TrainingCourseEnrollments
            .SingleOrDefaultAsync(e => e.CourseId == courseId && e.UserId == userId, token);
        if (enrollment is null)
            return NotFound();

        enrollment.Status = model.Status;
        enrollment.ReviewComment = model.ReviewComment.Trim();
        enrollment.ReviewedById = actor.Id;
        enrollment.ReviewedAt = DateTimeOffset.UtcNow;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Reviewed training course enrollment: course={courseId}, user={userId}, status={model.Status}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpGet("{courseId:int}/student-candidates")]
    [ProducesResponseType(typeof(TrainingCourseStudentCandidateModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> StudentCandidates(
        [FromRoute] int courseId,
        [FromQuery] string? keyword = null,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var enrolledIds = await context.TrainingCourseEnrollments
            .AsNoTracking()
            .Where(e => e.CourseId == courseId)
            .Select(e => e.UserId)
            .ToArrayAsync(token);
        var enrolledSet = enrolledIds.ToHashSet();

        var query = context.Users.AsNoTracking()
            .Where(u => u.Role == Role.Student);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim().ToLower();
            query = query.Where(u =>
                u.UserName!.ToLower().Contains(key) ||
                u.RealName.ToLower().Contains(key) ||
                u.StdNumber.ToLower().Contains(key) ||
                u.Email!.ToLower().Contains(key) ||
                u.Id.ToString().ToLower().Contains(key));
        }

        var users = await query
            .OrderBy(u => enrolledIds.Contains(u.Id))
            .ThenBy(u => u.RealName)
            .ThenBy(u => u.UserName)
            .Take(30)
            .ToArrayAsync(token);

        return Ok(users.Select(u => TrainingCourseStudentCandidateModel.FromUser(u, enrolledSet.Contains(u.Id))).ToArray());
    }

    [HttpPost("{courseId:int}/enrollments")]
    [ProducesResponseType(typeof(TrainingCourseEnrollmentModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddEnrollment(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseStudentEnrollModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == model.UserId, token);
        if (user is null || user.Role != Role.Student)
            return BadRequest(new RequestResponse("只能添加学员账号。"));

        var enrollment = await context.TrainingCourseEnrollments
            .Include(e => e.User)
            .SingleOrDefaultAsync(e => e.CourseId == courseId && e.UserId == model.UserId, token);
        if (enrollment is null)
        {
            enrollment = new TrainingCourseEnrollment
            {
                CourseId = courseId,
                UserId = model.UserId,
                User = user,
                Status = TrainingCourseEnrollmentStatus.Approved,
                ReviewComment = "Teacher added",
                ReviewedById = actor.Id,
                ReviewedAt = DateTimeOffset.UtcNow,
                RequestedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            context.TrainingCourseEnrollments.Add(enrollment);
        }
        else
        {
            enrollment.User = user;
            enrollment.Status = TrainingCourseEnrollmentStatus.Approved;
            enrollment.ReviewComment = string.Empty;
            enrollment.ReviewedById = actor.Id;
            enrollment.ReviewedAt = DateTimeOffset.UtcNow;
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(token);
        var totalChapterCount = await context.TrainingCourseChapters
            .CountAsync(c => c.CourseId == courseId && c.IsPublished, token);
        var progress = await context.TrainingCourseProgresses
            .SingleOrDefaultAsync(p => p.CourseId == courseId && p.UserId == model.UserId, token);
        logger.SystemLog($"Added training course enrollment: course={courseId}, user={user.UserName}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok(TrainingCourseEnrollmentModel.FromEnrollment(enrollment, progress, totalChapterCount));
    }

    [HttpGet("{courseId:int}/teacher-candidates")]
    [ProducesResponseType(typeof(TrainingCourseTeacherCandidateModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> TeacherCandidates(
        [FromRoute] int courseId,
        [FromQuery] string? keyword = null,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanManageTeachers(actor, course, token))
            return Forbid();

        var teacherIds = course.Teachers.Select(t => t.TeacherId).ToHashSet();
        var query = context.Users.AsNoTracking().Where(u => u.Role >= Role.Teacher);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim().ToLower();
            query = query.Where(u =>
                u.UserName!.ToLower().Contains(key) ||
                u.RealName.ToLower().Contains(key) ||
                u.StdNumber.ToLower().Contains(key) ||
                u.Email!.ToLower().Contains(key) ||
                u.Id.ToString().ToLower().Contains(key));
        }

        var users = await query
            .OrderBy(u => u.Role)
            .ThenBy(u => u.RealName)
            .ThenBy(u => u.UserName)
            .Take(30)
            .ToArrayAsync(token);

        return Ok(users.Select(u => TrainingCourseTeacherCandidateModel.FromUser(u, teacherIds.Contains(u.Id))).ToArray());
    }

    [HttpPost("{courseId:int}/teachers")]
    public async Task<IActionResult> AddTeacher(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseTeacherEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanManageTeachers(actor, course, token))
            return Forbid();

        var teacher = await context.Users.SingleOrDefaultAsync(u => u.Id == model.TeacherId, token);
        if (teacher is null || teacher.Role < Role.Teacher)
            return BadRequest(new RequestResponse("只能添加老师及以上权限用户为授课老师。"));

        var link = await context.TrainingCourseTeachers
            .SingleOrDefaultAsync(t => t.CourseId == courseId && t.TeacherId == model.TeacherId, token);
        if (link is null)
        {
            link = new TrainingCourseTeacher
            {
                CourseId = courseId,
                TeacherId = model.TeacherId,
                Role = model.Role,
                AssignedById = actor.Id
            };
            context.TrainingCourseTeachers.Add(link);
        }
        else
        {
            link.Role = model.Role;
        }

        await context.SaveChangesAsync(token);
        logger.SystemLog($"Updated training course teacher: course={courseId}, teacher={teacher.UserName}, role={model.Role}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpDelete("{courseId:int}/teachers/{teacherId:guid}")]
    public async Task<IActionResult> RemoveTeacher(
        [FromRoute] int courseId,
        [FromRoute] Guid teacherId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var course = await CourseQuery().SingleOrDefaultAsync(c => c.Id == courseId, token);
        if (course is null)
            return NotFound();
        if (!await CanManageTeachers(actor, course, token))
            return Forbid();

        var link = await context.TrainingCourseTeachers
            .SingleOrDefaultAsync(t => t.CourseId == courseId && t.TeacherId == teacherId, token);
        if (link is null)
            return NotFound();
        if (link.Role == TrainingCourseTeacherRole.Owner &&
            await context.TrainingCourseTeachers.CountAsync(t =>
                t.CourseId == courseId && t.Role == TrainingCourseTeacherRole.Owner, token) <= 1)
            return BadRequest(new RequestResponse("课程至少需要保留一名负责人。"));

        context.TrainingCourseTeachers.Remove(link);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Removed training course teacher: course={courseId}, teacher={teacherId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpPost("{courseId:int}/chapters")]
    [ProducesResponseType(typeof(TrainingCourseChapterModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateChapter(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseChapterEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var file = await ResolveFile(model.VideoFileHash, token);
        if (!string.IsNullOrWhiteSpace(model.VideoFileHash) && file is null)
            return BadRequest(new RequestResponse("视频文件不存在。"));
        if (model.ParentId.HasValue &&
            !await context.TrainingCourseChapters.AnyAsync(c => c.Id == model.ParentId.Value && c.CourseId == courseId, token))
            return BadRequest(new RequestResponse("父级章节不存在。"));

        var chapter = new TrainingCourseChapter
        {
            CourseId = courseId,
            CreatedById = actor.Id,
            UpdatedById = actor.Id
        };
        FillChapter(chapter, model, file, actor);
        context.TrainingCourseChapters.Add(chapter);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Created training course chapter {chapter.Title}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok(TrainingCourseChapterModel.FromChapter(chapter));
    }

    [HttpPut("{courseId:int}/chapters/{chapterId:int}")]
    public async Task<IActionResult> UpdateChapter(
        [FromRoute] int courseId,
        [FromRoute] int chapterId,
        [FromBody] TrainingCourseChapterEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var chapter = await context.TrainingCourseChapters
            .SingleOrDefaultAsync(c => c.Id == chapterId && c.CourseId == courseId, token);
        if (chapter is null)
            return NotFound();
        if (model.ParentId == chapterId)
            return BadRequest(new RequestResponse("父级章节不能选择自身。"));
        if (model.ParentId.HasValue &&
            !await context.TrainingCourseChapters.AnyAsync(c => c.Id == model.ParentId.Value && c.CourseId == courseId, token))
            return BadRequest(new RequestResponse("父级章节不存在。"));

        var file = await ResolveFile(model.VideoFileHash, token);
        if (!string.IsNullOrWhiteSpace(model.VideoFileHash) && file is null)
            return BadRequest(new RequestResponse("视频文件不存在。"));

        FillChapter(chapter, model, file, actor);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Updated training course chapter {chapter.Title}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpDelete("{courseId:int}/chapters/{chapterId:int}")]
    public async Task<IActionResult> DeleteChapter(
        [FromRoute] int courseId,
        [FromRoute] int chapterId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var chapter = await context.TrainingCourseChapters
            .SingleOrDefaultAsync(c => c.Id == chapterId && c.CourseId == courseId, token);
        if (chapter is null)
            return NotFound();

        await using var transaction = await context.Database.BeginTransactionAsync(token);

        await context.TrainingCourseChapters
            .Where(c => c.CourseId == courseId && c.ParentId == chapterId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.ParentId, (int?)null), token);

        var paperIds = await context.TrainingCourseChapterTheoryPapers
            .Where(p => p.CourseId == courseId && p.ChapterId == chapterId)
            .Select(p => p.Id)
            .ToArrayAsync(token);

        if (paperIds.Length > 0)
        {
            var sheetIds = await context.TrainingCourseChapterTheorySheets
                .Where(s => s.CourseId == courseId && s.ChapterId == chapterId)
                .Select(s => s.Id)
                .ToArrayAsync(token);

            if (sheetIds.Length > 0)
                await context.TrainingCourseChapterTheoryAnswers
                    .Where(a => sheetIds.Contains(a.SheetId))
                    .ExecuteDeleteAsync(token);

            await context.TrainingCourseChapterTheorySheets
                .Where(s => s.CourseId == courseId && s.ChapterId == chapterId)
                .ExecuteDeleteAsync(token);

            await context.TrainingCourseChapterTheoryQuestions
                .Where(q => paperIds.Contains(q.PaperId))
                .ExecuteDeleteAsync(token);

            await context.TrainingCourseChapterTheoryPapers
                .Where(p => paperIds.Contains(p.Id))
                .ExecuteDeleteAsync(token);
        }

        await context.TrainingCourseSubmissions
            .Where(s => s.CourseId == courseId && s.ChapterId == chapterId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.ChapterId, (int?)null), token);

        await context.TrainingChapterProgresses
            .Where(p => p.CourseId == courseId && p.ChapterId == chapterId)
            .ExecuteDeleteAsync(token);

        await context.TrainingCourseChapterChallenges
            .Where(c => c.CourseId == courseId && c.ChapterId == chapterId)
            .ExecuteDeleteAsync(token);

        context.TrainingCourseChapters.Remove(chapter);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.SystemLog($"Deleted training course chapter {chapter.Title}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    private static void FillChapter(
        TrainingCourseChapter chapter,
        TrainingCourseChapterEditModel model,
        LocalFile? videoFile,
        UserInfo actor)
    {
        chapter.ParentId = model.ParentId;
        chapter.Title = model.Title.Trim();
        chapter.Summary = model.Summary.Trim();
        chapter.Content = model.Content;
        chapter.ContentType = model.ContentType;
        chapter.CompletionPolicy = model.CompletionPolicy;
        chapter.VideoProvider = model.VideoProvider;
        chapter.VideoUrl = string.IsNullOrWhiteSpace(model.VideoUrl) ? null : model.VideoUrl.Trim();
        chapter.VideoFileId = videoFile?.Id;
        chapter.VideoFile = videoFile;
        chapter.Order = model.Order;
        chapter.IsPublished = model.IsPublished;
        chapter.UpdatedById = actor.Id;
        chapter.UpdatedAt = DateTimeOffset.UtcNow;
    }

    [HttpPost("{courseId:int}/resources")]
    [ProducesResponseType(typeof(TrainingCourseResourceModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateResource(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseResourceEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var file = await ResolveFile(model.LocalFileHash, token);
        if (model.Type == TrainingCourseResourceType.File && file is null)
            return BadRequest(new RequestResponse("本地资源文件不存在。"));
        if (model.Type != TrainingCourseResourceType.File && string.IsNullOrWhiteSpace(model.ExternalUrl))
            return BadRequest(new RequestResponse("外链资源需要填写 URL。"));

        var resource = new TrainingCourseResource
        {
            CourseId = courseId,
            CreatedById = actor.Id
        };
        FillResource(resource, model, file);
        context.TrainingCourseResources.Add(resource);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Created training course resource {resource.Title}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);

        return Ok(TrainingCourseResourceModel.FromResource(resource, true));
    }

    [HttpPut("{courseId:int}/resources/{resourceId:int}")]
    public async Task<IActionResult> UpdateResource(
        [FromRoute] int courseId,
        [FromRoute] int resourceId,
        [FromBody] TrainingCourseResourceEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var resource = await context.TrainingCourseResources
            .SingleOrDefaultAsync(r => r.Id == resourceId && r.CourseId == courseId, token);
        if (resource is null)
            return NotFound();

        var file = await ResolveFile(model.LocalFileHash, token);
        if (model.Type == TrainingCourseResourceType.File && file is null)
            return BadRequest(new RequestResponse("本地资源文件不存在。"));
        if (model.Type != TrainingCourseResourceType.File && string.IsNullOrWhiteSpace(model.ExternalUrl))
            return BadRequest(new RequestResponse("外链资源需要填写 URL。"));

        FillResource(resource, model, file);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Updated training course resource {resource.Title}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpDelete("{courseId:int}/resources/{resourceId:int}")]
    public async Task<IActionResult> DeleteResource(
        [FromRoute] int courseId,
        [FromRoute] int resourceId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var resource = await context.TrainingCourseResources
            .SingleOrDefaultAsync(r => r.Id == resourceId && r.CourseId == courseId, token);
        if (resource is null)
            return NotFound();

        context.TrainingCourseResources.Remove(resource);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Deleted training course resource {resource.Title}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    private static void FillResource(
        TrainingCourseResource resource,
        TrainingCourseResourceEditModel model,
        LocalFile? file)
    {
        resource.Title = model.Title.Trim();
        resource.Description = model.Description.Trim();
        resource.Type = model.Type;
        resource.ExternalUrl = model.Type == TrainingCourseResourceType.File ? null : model.ExternalUrl?.Trim();
        resource.LocalFileId = model.Type == TrainingCourseResourceType.File ? file?.Id : null;
        resource.LocalFile = model.Type == TrainingCourseResourceType.File ? file : null;
        resource.Order = model.Order;
        resource.IsVisible = model.IsVisible;
    }

    [HttpGet("{courseId:int}/theory-questions")]
    [ProducesResponseType(typeof(TrainingCourseTheoryQuestionModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> TheoryQuestions(
        [FromRoute] int courseId,
        [FromQuery] string? keyword = null,
        [FromQuery] TheoryQuestionType? type = null,
        [FromQuery] string? bankName = null,
        [FromQuery] int count = 1000,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var query = context.TrainingCourseTheoryQuestions.AsNoTracking().Where(q => q.CourseId == courseId);
        if (type.HasValue)
            query = query.Where(q => q.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(bankName))
            query = query.Where(q => q.BankName == bankName.Trim());
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var key = keyword.Trim();
            query = query.Where(q => q.Title.Contains(key) || q.Content.Contains(key) || q.BankName.Contains(key));
        }

        var questions = await query
            .OrderBy(q => q.Type)
            .ThenBy(q => q.BankName)
            .ThenByDescending(q => q.UpdatedAt)
            .Take(Math.Clamp(count, 1, 5000))
            .ToArrayAsync(token);

        return Ok(questions.Select(TrainingCourseTheoryQuestionModel.FromQuestion).ToArray());
    }

    [HttpPost("{courseId:int}/theory-questions")]
    [ProducesResponseType(typeof(TrainingCourseTheoryQuestionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTheoryQuestion(
        [FromRoute] int courseId,
        [FromBody] TheoryQuestionEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();
        if (theoryService.NormalizeAndValidate(model) is { } error)
            return BadRequest(new RequestResponse(error));

        var question = new TrainingCourseTheoryQuestion
        {
            CourseId = courseId,
            Type = model.Type,
            BankName = model.BankName,
            Title = model.Title,
            Content = model.Content,
            Options = model.Options,
            AnswerIndexes = model.AnswerIndexes,
            CreatedById = actor.Id,
            UpdatedById = actor.Id
        };

        context.TrainingCourseTheoryQuestions.Add(question);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Created training course theory question {question.Id}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok(TrainingCourseTheoryQuestionModel.FromQuestion(question));
    }

    [HttpPut("{courseId:int}/theory-questions/{questionId:int}")]
    [ProducesResponseType(typeof(TrainingCourseTheoryQuestionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTheoryQuestion(
        [FromRoute] int courseId,
        [FromRoute] int questionId,
        [FromBody] TheoryQuestionEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();
        if (theoryService.NormalizeAndValidate(model) is { } error)
            return BadRequest(new RequestResponse(error));

        var question = await context.TrainingCourseTheoryQuestions
            .SingleOrDefaultAsync(q => q.Id == questionId && q.CourseId == courseId, token);
        if (question is null)
            return NotFound();

        question.Type = model.Type;
        question.BankName = model.BankName;
        question.Title = model.Title;
        question.Content = model.Content;
        question.Options = model.Options;
        question.AnswerIndexes = model.AnswerIndexes;
        question.UpdatedById = actor.Id;
        question.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(token);
        logger.SystemLog($"Updated training course theory question {question.Id}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok(TrainingCourseTheoryQuestionModel.FromQuestion(question));
    }

    [HttpDelete("{courseId:int}/theory-questions/{questionId:int}")]
    public async Task<IActionResult> DeleteTheoryQuestion(
        [FromRoute] int courseId,
        [FromRoute] int questionId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var inUse = await context.TrainingCourseChapterTheoryQuestions
            .AnyAsync(q => q.SourceQuestionId == questionId, token);
        if (inUse)
            return BadRequest(new RequestResponse("该题已被章节测试引用，不能直接删除。"));

        var question = await context.TrainingCourseTheoryQuestions
            .SingleOrDefaultAsync(q => q.Id == questionId && q.CourseId == courseId, token);
        if (question is null)
            return NotFound();

        context.TrainingCourseTheoryQuestions.Remove(question);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Deleted training course theory question {questionId}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpGet("{courseId:int}/theory-papers")]
    [ProducesResponseType(typeof(TrainingCourseChapterTheorySummaryModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChapterTheoryPapers([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var papers = await context.TrainingCourseChapterTheoryPapers
            .Include(p => p.Questions)
            .Where(p => p.CourseId == courseId)
            .OrderBy(p => p.Chapter.Order)
            .ThenBy(p => p.ChapterId)
            .ToArrayAsync(token);

        return Ok(papers.Select(p => TrainingCourseChapterTheorySummaryModel.FromPaper(p)).ToArray());
    }

    [HttpGet("{courseId:int}/chapters/{chapterId:int}/theory-paper")]
    [ProducesResponseType(typeof(TrainingCourseChapterTheoryPaperDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChapterTheoryPaper(
        [FromRoute] int courseId,
        [FromRoute] int chapterId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var chapter = await context.TrainingCourseChapters
            .SingleOrDefaultAsync(c => c.Id == chapterId && c.CourseId == courseId, token);
        if (chapter is null)
            return NotFound();

        var paper = await context.TrainingCourseChapterTheoryPapers
            .Include(p => p.Questions)
            .SingleOrDefaultAsync(p => p.CourseId == courseId && p.ChapterId == chapterId, token);

        return Ok(paper is null
            ? TrainingCourseChapterTheoryPaperDetailModel.Empty(courseId, chapter)
            : TrainingCourseChapterTheoryPaperDetailModel.FromPaper(paper));
    }

    [HttpPut("{courseId:int}/chapters/{chapterId:int}/theory-paper")]
    [ProducesResponseType(typeof(TrainingCourseChapterTheoryPaperDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveChapterTheoryPaper(
        [FromRoute] int courseId,
        [FromRoute] int chapterId,
        [FromBody] TrainingCourseChapterTheoryPaperEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();
        if (string.IsNullOrWhiteSpace(model.Title))
            return BadRequest(new RequestResponse("测试标题不能为空。"));
        if (model.IsPublished && model.Questions.Count == 0)
            return BadRequest(new RequestResponse("发布课后测试前至少需要添加一道题。"));

        var chapter = await context.TrainingCourseChapters
            .SingleOrDefaultAsync(c => c.Id == chapterId && c.CourseId == courseId, token);
        if (chapter is null)
            return NotFound();

        var sourceIds = model.Questions
            .Where(q => q.SourceQuestionId.HasValue)
            .Select(q => q.SourceQuestionId!.Value)
            .Distinct()
            .ToArray();
        if (sourceIds.Length > 0)
        {
            var existingIds = await context.TrainingCourseTheoryQuestions
                .Where(q => q.CourseId == courseId && sourceIds.Contains(q.Id))
                .Select(q => q.Id)
                .ToArrayAsync(token);
            if (existingIds.Length != sourceIds.Length)
                return BadRequest(new RequestResponse("测试题目只能引用当前课程题库。"));
        }

        foreach (var question in model.Questions)
        {
            if (theoryService.NormalizeAndValidate(question, question.Score) is { } error)
                return BadRequest(new RequestResponse(error));
        }

        var paper = await context.TrainingCourseChapterTheoryPapers
            .Include(p => p.Questions)
            .SingleOrDefaultAsync(p => p.CourseId == courseId && p.ChapterId == chapterId, token);

        paper ??= new TrainingCourseChapterTheoryPaper
        {
            CourseId = courseId,
            ChapterId = chapterId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        paper.Title = model.Title.Trim();
        paper.Description = model.Description.Trim();
        paper.PassRate = Math.Clamp(model.PassRate, 1, 100);
        paper.AllowRetake = model.AllowRetake;
        paper.ShowCorrectAnswerAfterSubmit = model.ShowCorrectAnswerAfterSubmit;
        paper.IsPublished = model.IsPublished;
        paper.PublishedAt = model.IsPublished ? paper.PublishedAt ?? DateTimeOffset.UtcNow : null;
        paper.UpdatedById = actor.Id;
        paper.UpdatedAt = DateTimeOffset.UtcNow;

        var activeQuestions = paper.ActiveQuestions.ToArray();
        var activeQuestionIds = activeQuestions.Select(question => question.Id).Where(id => id > 0).ToArray();
        var referencedQuestionIds = activeQuestionIds.Length == 0
            ? []
            : await context.TrainingCourseChapterTheoryAnswers
                .Where(answer => activeQuestionIds.Contains(answer.PaperQuestionId))
                .Select(answer => answer.PaperQuestionId)
                .Distinct()
                .ToArrayAsync(token);
        var referencedQuestionIdSet = referencedQuestionIds.ToHashSet();

        foreach (var question in activeQuestions.Where(question => referencedQuestionIdSet.Contains(question.Id)))
            question.IsArchived = true;

        context.TrainingCourseChapterTheoryQuestions.RemoveRange(
            activeQuestions.Where(question => !referencedQuestionIdSet.Contains(question.Id)));
        paper.Questions = paper.Questions.Where(question => question.IsArchived).ToList();
        paper.Questions.AddRange(model.Questions
            .OrderBy(q => q.Order > 0 ? q.Order : int.MaxValue)
            .Select((q, index) => new TrainingCourseChapterTheoryQuestion
            {
                SourceQuestionId = q.SourceQuestionId,
                Type = q.Type,
                Title = q.Title.Trim(),
                Content = q.Content.Trim(),
                Options = q.Options,
                AnswerIndexes = TheoryExamService.NormalizeIndexes(q.AnswerIndexes),
                Score = q.Score,
                Order = q.Order > 0 ? q.Order : index + 1,
                IsArchived = false
            })
            .ToList());

        if (paper.Id == 0)
            context.TrainingCourseChapterTheoryPapers.Add(paper);

        await context.SaveChangesAsync(token);
        logger.SystemLog($"Saved training course chapter theory paper {paper.Title}: course={courseId}, chapter={chapterId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok(TrainingCourseChapterTheoryPaperDetailModel.FromPaper(paper));
    }

    [HttpGet("{courseId:int}/image-templates")]
    [ProducesResponseType(typeof(TrainingCourseImageTemplateModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImageTemplates([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var templates = await CourseTemplates(courseId)
            .OrderByDescending(t => t.UploadedAt)
            .ToArrayAsync(token);

        return Ok(templates.Select(TrainingCourseImageTemplateModel.FromTemplate).ToArray());
    }

    [HttpGet("{courseId:int}/image-templates/docker-registry")]
    public async Task<IActionResult> DockerRegistry([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var endpoint = await dockerRegistry.GetActiveEndpointAsync(token);
        return Ok(new
        {
            enabled = endpoint is not null,
            address = endpoint?.Address ?? string.Empty,
            @namespace = dockerRegistry.RegistryNamespace,
            maxUploadSizeGb = dockerRegistry.MaxUploadSizeGb
        });
    }

    [HttpPost("{courseId:int}/image-templates/register-docker")]
    public async Task<IActionResult> RegisterDockerTemplate(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseDockerRegisterModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        if (!string.IsNullOrWhiteSpace(model.RegistryAuth))
            return BadRequest(new RequestResponse("持久化镜像导入不接受明文 Registry 凭据。"));

        try
        {
            var imported = await imageImports.ImportDockerReferenceNowAsync(
                new ActorContext(actor.Id, actor.Role),
                new DockerImageReferenceImportCommand(
                    model.Name,
                    model.RegistryUrl,
                    model.OSType,
                    null),
                token);
            var template = await context.ImageTemplates.SingleAsync(
                item => item.Id == imported.Id, token);
            await BindTemplateAsync(courseId, template.Id, actor.Id, token);
            await DistributeCourseImageAsync(
                courseId, template.Id, "training Docker reference import", token);
            logger.SystemLog($"Imported training course Docker template {template.Name}: course={courseId}, template={template.Id}.",
                TaskStatus.Success, LogLevel.Information);
            return Ok(TrainingCourseImageTemplateModel.FromTemplate(template));
        }
        catch (ApiContractException ex)
        {
            return StatusCode(ex.StatusCode, new RequestResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new RequestResponse(ex.Message));
        }
    }

    [HttpPost("{courseId:int}/image-templates/upload-docker")]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)]
    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = 60L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadDockerTemplate(
        [FromRoute] int courseId,
        [FromForm] IFormFile file,
        [FromForm] string name,
        [FromForm] string? sourceImage,
        [FromForm] OSType osType,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        if (file is null || file.Length == 0)
            return BadRequest(new RequestResponse("未选择 Docker 镜像包。"));

        try
        {
            await using var stream = file.OpenReadStream();
            var imported = await imageImports.ImportDockerArchiveNowAsync(
                new ActorContext(actor.Id, actor.Role),
                stream,
                file.FileName,
                file.Length,
                new DockerImageArchiveImportCommand(
                    name,
                    sourceImage,
                    osType,
                    null),
                token);
            var template = await context.ImageTemplates.SingleAsync(
                item => item.Id == imported.Id, token);
            await BindTemplateAsync(courseId, template.Id, actor.Id, token);
            await DistributeCourseImageAsync(
                courseId, template.Id, "training Docker archive import", token);
            logger.SystemLog($"Uploaded training course Docker template {template.Name}: course={courseId}, template={template.Id}.",
                TaskStatus.Success, LogLevel.Information);

            return Ok(TrainingCourseImageTemplateModel.FromTemplate(template));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new RequestResponse(ex.Message));
        }
        catch (ApiContractException ex)
        {
            return StatusCode(ex.StatusCode, new RequestResponse(ex.Message));
        }
    }

    [HttpPost("{courseId:int}/image-templates/upload-vm")]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadVmTemplate(
        [FromRoute] int courseId,
        IFormFile file,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();
        if (file is null || file.Length == 0)
            return BadRequest(new RequestResponse("未选择 VM 镜像文件。"));

        try
        {
            var template = await imageStorage.SaveImageAsync(file);
            template.CreatedById = actor.Id;
            context.ImageTemplates.Add(template);
            await context.SaveChangesAsync(token);
            await BindTemplateAsync(courseId, template.Id, actor.Id, token);
            logger.SystemLog($"Uploaded training course VM template {template.Name}: course={courseId}, template={template.Id}.",
                TaskStatus.Success, LogLevel.Information);
            return Ok(TrainingCourseImageTemplateModel.FromTemplate(template));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new RequestResponse(ex.Message));
        }
    }

    [HttpPost("{courseId:int}/image-templates/upload-vm-archive")]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadVmArchiveTemplate(
        [FromRoute] int courseId,
        IFormFile file,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();
        if (file is null || file.Length == 0)
            return BadRequest(new RequestResponse("未选择 VM 归档文件。"));

        var fileName = file.FileName.ToLowerInvariant();
        var ext = Path.GetExtension(fileName);
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            ext = ".tar.gz";
        else if (fileName.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase))
            ext = ".tar.xz";
        if (ext is not ".zip" and not ".tar.gz" and not ".tgz" and not ".tar.xz" and not ".txz")
            return BadRequest(new RequestResponse("仅支持 .zip、.tar.gz、.tgz、.tar.xz、.txz 格式的 VM 归档。"));

        var tempDir = Path.Combine(Path.GetTempPath(), "gzctf_course_vm_uploads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var archivePath = Path.Combine(tempDir, $"archive{ext}");

        try
        {
            await using (var stream = file.OpenReadStream())
            await using (var fs = System.IO.File.Create(archivePath))
                await stream.CopyToAsync(fs, token);

            var result = await archiveExtractor.ExtractAndRegisterAsync(
                archivePath, file.FileName, actor.Id, token);
            if (!result.Success || result.Template is null)
                return BadRequest(new RequestResponse(result.Error ?? "VM 归档处理失败。"));

            await BindTemplateAsync(courseId, result.Template.Id, actor.Id, token);
            logger.SystemLog($"Uploaded training course VM archive template {result.Template.Name}: course={courseId}, template={result.Template.Id}.",
                TaskStatus.Success, LogLevel.Information);
            return Ok(TrainingCourseImageTemplateModel.FromTemplate(result.Template));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort cleanup */ }
        }
    }

    [HttpPost("{courseId:int}/image-templates/import-local")]
    public async Task<IActionResult> ImportLocalTemplate(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseLocalImageImportModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();
        if (string.IsNullOrWhiteSpace(model.LocalPath))
            return BadRequest(new RequestResponse("服务器本地路径不能为空。"));

        var fullPath = Path.GetFullPath(model.LocalPath);
        var allowedRoots = new[]
        {
            Path.GetFullPath("./images"),
            Path.GetFullPath("/var/lib/gzctf/images"),
            Path.GetFullPath("/var/lib/libvirt/images"),
        };
        if (!allowedRoots.Any(r => fullPath.StartsWith(r + Path.DirectorySeparatorChar) || fullPath == r))
            return BadRequest(new RequestResponse("路径不在允许的镜像目录内。"));

        try
        {
            var importer = HttpContext.RequestServices.GetRequiredService<LocalImageImporter>();
            var template = await importer.ImportFromLocalPathAsync(
                model.LocalPath, model.DisplayName, actor.Id, token);
            await BindTemplateAsync(courseId, template.Id, actor.Id, token);
            logger.SystemLog($"Imported training course local image template {template.Name}: course={courseId}, template={template.Id}.",
                TaskStatus.Success, LogLevel.Information);
            return Ok(TrainingCourseImageTemplateModel.FromTemplate(template));
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new RequestResponse(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new RequestResponse(ex.Message));
        }
    }

    [HttpPost("{courseId:int}/image-templates")]
    public async Task<IActionResult> AttachImageTemplate(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseImageTemplateAttachModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var template = await context.ImageTemplates.SingleOrDefaultAsync(t => t.Id == model.TemplateId, token);
        if (template is null)
            return BadRequest(new RequestResponse("环境模板不存在。"));
        if (template.Status != ImageStatus.Ready)
            return BadRequest(new RequestResponse("环境模板尚未就绪。"));
        if (actor.Role < Role.Admin && template.CreatedById != actor.Id)
            return Forbid();

        await BindTemplateAsync(courseId, template.Id, actor.Id, token);
        logger.SystemLog($"Attached training course image template {template.Id}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        await DistributeCourseImageAsync(courseId, template.Id, "training template attach", token);
        return Ok();
    }

    [HttpDelete("{courseId:int}/image-templates/{templateId:int}")]
    public async Task<IActionResult> DetachImageTemplate(
        [FromRoute] int courseId,
        [FromRoute] int templateId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var binding = await context.TrainingCourseImageTemplateBindings.SingleOrDefaultAsync(item =>
            item.CourseId == courseId && item.ImageTemplateId == templateId, token);
        if (binding is null)
            return NotFound();

        var inUse = await context.ExerciseChallenges.AnyAsync(c =>
            c.TrainingCourseId == courseId && c.ImageTemplateId == templateId, token);
        if (inUse)
            return BadRequest(new RequestResponse("该环境模板正在被课程题目使用，不能移除。"));

        context.TrainingCourseImageTemplateBindings.Remove(binding);
        await context.SaveChangesAsync(token);
        await imageDistribution.ReleaseTrainingCourseTemplateReferenceAsync(courseId, templateId, token);
        logger.SystemLog($"Detached training course image template {templateId}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpPost("{courseId:int}/challenges/create")]
    [ProducesResponseType(typeof(TrainingCourseChallengeModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCourseChallenge(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseChallengeCreateModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();
        if (await ValidateCourseChallengeModel(courseId, model, token) is { } validation)
            return validation;

        var order = model.Order > 0
            ? model.Order
            : await context.TrainingCourseChallenges
                .Where(c => c.CourseId == courseId)
                .MaxAsync(c => (int?)c.Order, token) + 1 ?? 1;
        model.Order = order;

        await using var transaction = await context.Database.BeginTransactionAsync(token);

        var exercise = new ExerciseChallenge();
        var link = new TrainingCourseChallenge
        {
            CourseId = courseId,
            ExerciseChallenge = exercise,
            CreatedById = actor.Id
        };

        await ApplyCourseChallengeModel(courseId, link, exercise, model, token);
        context.ExerciseChallenges.Add(exercise);
        context.TrainingCourseChallenges.Add(link);
        await context.SaveChangesAsync(token);

        await SetCourseChallengeChapterLink(courseId, exercise.Id, model.ChapterId, order, token);

        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.SystemLog($"Created training course challenge {exercise.Title}: course={courseId}, challenge={exercise.Id}.",
            TaskStatus.Success, LogLevel.Information);
        await DistributeCourseImageAsync(
            courseId, exercise.ImageTemplateId, "training challenge create", token);

        return Ok(TrainingCourseChallengeModel.FromChallenge(link, model.ChapterId));
    }

    [HttpGet("{courseId:int}/challenges/{exerciseChallengeId:int}/edit")]
    [ProducesResponseType(typeof(TrainingCourseChallengeEditDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CourseChallengeEditDetail(
        [FromRoute] int courseId,
        [FromRoute] int exerciseChallengeId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var link = await context.TrainingCourseChallenges
            .Include(c => c.ExerciseChallenge)
            .ThenInclude(c => c.Flags)
            .Include(c => c.ExerciseChallenge)
            .ThenInclude(c => c.Attachment)
            .ThenInclude(a => a!.LocalFile)
            .SingleOrDefaultAsync(c => c.CourseId == courseId && c.ExerciseChallengeId == exerciseChallengeId, token);
        if (link is null || link.ExerciseChallenge.TrainingCourseId != courseId)
            return NotFound();

        var chapterId = await context.TrainingCourseChapterChallenges
            .Where(c => c.CourseId == courseId && c.ExerciseChallengeId == exerciseChallengeId)
            .OrderBy(c => c.Order)
            .Select(c => (int?)c.ChapterId)
            .FirstOrDefaultAsync(token);
        var submissionCount = await context.TrainingCourseSubmissions.CountAsync(s =>
            s.CourseId == courseId && s.ExerciseChallengeId == exerciseChallengeId, token);

        return Ok(TrainingCourseChallengeEditDetailModel.FromChallenge(link, chapterId, submissionCount));
    }

    [HttpPut("{courseId:int}/challenges/{exerciseChallengeId:int}")]
    [ProducesResponseType(typeof(TrainingCourseChallengeEditDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCourseChallenge(
        [FromRoute] int courseId,
        [FromRoute] int exerciseChallengeId,
        [FromBody] TrainingCourseChallengeUpdateModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();
        if (await ValidateCourseChallengeModel(courseId, model, token) is { } validation)
            return validation;

        var link = await context.TrainingCourseChallenges
            .Include(c => c.ExerciseChallenge)
            .ThenInclude(c => c.Flags)
            .Include(c => c.ExerciseChallenge)
            .ThenInclude(c => c.Attachment)
            .ThenInclude(a => a!.LocalFile)
            .SingleOrDefaultAsync(c => c.CourseId == courseId && c.ExerciseChallengeId == exerciseChallengeId, token);
        if (link is null || link.ExerciseChallenge.TrainingCourseId != courseId)
            return NotFound();

        model.Order = model.Order > 0 ? model.Order : link.Order;
        await using var transaction = await context.Database.BeginTransactionAsync(token);

        await ApplyCourseChallengeModel(courseId, link, link.ExerciseChallenge, model, token);
        await context.SaveChangesAsync(token);
        await SetCourseChallengeChapterLink(courseId, exerciseChallengeId, model.ChapterId, model.Order, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        logger.SystemLog($"Updated training course challenge {link.ExerciseChallenge.Title}: course={courseId}, challenge={exerciseChallengeId}.",
            TaskStatus.Success, LogLevel.Information);
        await DistributeCourseImageAsync(
            courseId, link.ExerciseChallenge.ImageTemplateId, "training challenge update", token);

        return await CourseChallengeEditDetail(courseId, exerciseChallengeId, token);
    }

    [HttpPost("{courseId:int}/challenges")]
    public async Task<IActionResult> AddChallenge(
        [FromRoute] int courseId,
        [FromBody] TrainingCourseChallengeEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var challenge = await context.ExerciseChallenges
            .SingleOrDefaultAsync(c => c.Id == model.ExerciseChallengeId, token);
        if (challenge is null)
            return BadRequest(new RequestResponse("练习题目不存在。"));
        if (!challenge.TrainingCourseId.HasValue && actor.Role < Role.Admin)
            return Forbid();
        if (challenge.TrainingCourseId.HasValue && challenge.TrainingCourseId.Value != courseId)
            return BadRequest(new RequestResponse("该练习题属于其他课程，不能跨课程复用。"));

        var link = await context.TrainingCourseChallenges
            .SingleOrDefaultAsync(c => c.CourseId == courseId && c.ExerciseChallengeId == model.ExerciseChallengeId, token);
        if (link is null)
        {
            link = new TrainingCourseChallenge
            {
                CourseId = courseId,
                ExerciseChallengeId = model.ExerciseChallengeId,
                CreatedById = actor.Id
            };
            context.TrainingCourseChallenges.Add(link);
        }

        link.Order = model.Order;
        link.IsRequired = model.IsRequired;
        link.DisplayTitle = model.DisplayTitle;

        if (model.ChapterId.HasValue &&
            !await context.TrainingCourseChapters.AnyAsync(c => c.Id == model.ChapterId.Value && c.CourseId == courseId, token))
            return BadRequest(new RequestResponse("课程章节不存在。"));

        await SetCourseChallengeChapterLink(courseId, model.ExerciseChallengeId, model.ChapterId, model.Order, token);

        await context.SaveChangesAsync(token);
        logger.SystemLog($"Attached training course challenge {model.ExerciseChallengeId}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        await DistributeCourseImageAsync(
            courseId, challenge.ImageTemplateId, "training challenge attach", token);
        return Ok();
    }

    [HttpDelete("{courseId:int}/challenges/{exerciseChallengeId:int}")]
    public async Task<IActionResult> RemoveChallenge(
        [FromRoute] int courseId,
        [FromRoute] int exerciseChallengeId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var challenge = await context.ExerciseChallenges
            .Include(c => c.Attachment)
            .ThenInclude(a => a!.LocalFile)
            .SingleOrDefaultAsync(c => c.Id == exerciseChallengeId, token);
        var linkExists = await context.TrainingCourseChallenges
            .AnyAsync(c => c.CourseId == courseId && c.ExerciseChallengeId == exerciseChallengeId, token);
        if (!linkExists && challenge?.TrainingCourseId != courseId)
            return NotFound();

        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (challenge?.TrainingCourseId == courseId)
        {
            await blobRepository.DeleteAttachment(challenge.Attachment, token);
            await context.TrainingCourseSubmissions
                .Where(s => s.CourseId == courseId && s.ExerciseChallengeId == exerciseChallengeId)
                .ExecuteDeleteAsync(token);
            await context.TrainingCourseChapterChallenges
                .Where(c => c.CourseId == courseId && c.ExerciseChallengeId == exerciseChallengeId)
                .ExecuteDeleteAsync(token);
            await context.ExerciseInstances
                .Where(i => i.ExerciseId == exerciseChallengeId)
                .ExecuteDeleteAsync(token);
            await context.FlagContexts
                .Where(f => f.ExerciseId == exerciseChallengeId)
                .ExecuteDeleteAsync(token);
            await context.TrainingCourseChallenges
                .Where(c => c.CourseId == courseId && c.ExerciseChallengeId == exerciseChallengeId)
                .ExecuteDeleteAsync(token);

            context.ExerciseChallenges.Remove(challenge);
        }
        else
        {
            await context.TrainingCourseChallenges
                .Where(c => c.CourseId == courseId && c.ExerciseChallengeId == exerciseChallengeId)
                .ExecuteDeleteAsync(token);
        }

        await context.SaveChangesAsync(token);
        await projectionRevisions.BumpAsync(
            CachePolicyCatalog.TrainingStatistics.Name, "__global__", token);
        await transaction.CommitAsync(token);
        logger.SystemLog($"Removed training course challenge {exerciseChallengeId}: course={courseId}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }
}
