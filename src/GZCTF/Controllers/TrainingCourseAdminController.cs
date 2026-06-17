using GZCTF.Middlewares;
using GZCTF.Models.Request.Training;
using GZCTF.Services;
using GZCTF.Services.Container.Manager;
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
    DockerImageRegistryService dockerRegistry,
    ContainerOrchestrator containerOrchestrator,
    ILogger<TrainingCourseAdminController> logger) : ControllerBase
{
    private async Task<UserInfo> CurrentUser() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Current user is missing.");

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
            .ThenInclude(ch => ch.ExerciseChallenge);

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

    private async Task QueueCourseDockerPull(ImageTemplate template, string registryUrl, string imageName,
        string? registryAuth)
    {
        try
        {
            await containerOrchestrator.PullImageFromRegistryAsync(registryUrl, imageName, registryAuth);
            template.Status = ImageStatus.Ready;
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            template.Status = ImageStatus.Error;
            await context.SaveChangesAsync();
            logger.LogWarning(ex, "Failed to pull course Docker image {Image}", template.RegistryUrl);
        }
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

    [HttpGet]
    [ProducesResponseType(typeof(TrainingCourseModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Courses(CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var query = CourseQuery();
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
        return Ok();
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

        context.TrainingCourseChapters.Remove(chapter);
        await context.SaveChangesAsync(token);
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

    [HttpGet("{courseId:int}/image-templates")]
    [ProducesResponseType(typeof(TrainingCourseImageTemplateModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImageTemplates([FromRoute] int courseId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        var templates = await context.ImageTemplates
            .Where(t => t.TrainingCourseId == courseId)
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

        return Ok(new
        {
            enabled = dockerRegistry.IsConfigured,
            address = dockerRegistry.RegistryAddress,
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

        var pullTarget = DockerImageReference.ResolvePullTarget(model.Name, model.RegistryUrl);
        var imageReference = pullTarget.FullImage;
        var name = model.Name.Trim();

        var existingTemplate = await context.ImageTemplates.FirstOrDefaultAsync(t =>
            t.ImageType == ImageType.Docker &&
            t.TrainingCourseId == courseId &&
            (t.Name == name || t.RegistryUrl == imageReference), token);
        if (existingTemplate is not null && existingTemplate.Status != ImageStatus.Error)
            return BadRequest(new RequestResponse("当前课程已存在同名或同 Registry URL 的 Docker 模板。"));

        var template = existingTemplate ?? new ImageTemplate { ImageType = ImageType.Docker };
        template.Name = name;
        template.OSType = model.OSType;
        template.RegistryUrl = imageReference;
        template.RegistryAuth = model.RegistryAuth;
        template.Status = ImageStatus.Importing;
        template.UploadedAt = DateTimeOffset.UtcNow;
        template.TrainingCourseId = courseId;

        if (existingTemplate is null)
            context.ImageTemplates.Add(template);
        await context.SaveChangesAsync(token);

        _ = QueueCourseDockerPull(template, pullTarget.RegistryUrl, pullTarget.ImageName, model.RegistryAuth);

        return Ok(TrainingCourseImageTemplateModel.FromTemplate(template));
    }

    [HttpPost("{courseId:int}/image-templates/upload-docker")]
    [RequestSizeLimit(60L * 1024 * 1024 * 1024)]
    [RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = 60L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> UploadDockerTemplate(
        [FromRoute] int courseId,
        [FromForm] IFormFile file,
        [FromForm] string name,
        [FromForm] string repository,
        [FromForm] string tag,
        [FromForm] string? sourceImage,
        [FromForm] OSType osType,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanEditCourse(actor, courseId, token))
            return NotFound();

        if (file is null || file.Length == 0)
            return BadRequest(new RequestResponse("未选择 Docker 镜像包。"));
        if (file.Length > dockerRegistry.MaxUploadSizeBytes)
            return BadRequest(new RequestResponse("Docker 镜像包超过服务器允许大小。"));
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new RequestResponse("模板显示名称不能为空。"));

        var fileName = file.FileName.ToLowerInvariant();
        var ext = Path.GetExtension(fileName);
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            ext = ".tar.gz";
        else if (fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            ext = ".tgz";
        if (ext is not ".tar" and not ".tar.gz" and not ".tgz")
            return BadRequest(new RequestResponse("仅支持 .tar、.tar.gz、.tgz 格式的 Docker 镜像包。"));

        var targetImage = dockerRegistry.BuildInternalImageReference(repository, tag);
        var displayName = name.Trim();
        var existingTemplate = await context.ImageTemplates.FirstOrDefaultAsync(t =>
            t.ImageType == ImageType.Docker &&
            t.TrainingCourseId == courseId &&
            (t.Name == displayName || t.RegistryUrl == targetImage), token);
        if (existingTemplate is not null && existingTemplate.Status != ImageStatus.Error)
            return BadRequest(new RequestResponse("当前课程已存在同名或同 Registry URL 的 Docker 模板。"));

        var tempDir = Path.Combine(Path.GetTempPath(), "gzctf_course_docker_uploads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var archivePath = Path.Combine(tempDir, $"image{ext}");

        try
        {
            await using (var stream = file.OpenReadStream())
            await using (var fs = System.IO.File.Create(archivePath))
                await stream.CopyToAsync(fs, token);

            var result = await dockerRegistry.ImportArchiveAsync(archivePath, repository, tag, sourceImage, token);
            var template = existingTemplate ?? new ImageTemplate { ImageType = ImageType.Docker };
            template.Name = displayName;
            template.OSType = osType;
            template.RegistryUrl = result.FullImage;
            template.RegistryAuth = null;
            template.Status = ImageStatus.Ready;
            template.UploadedAt = DateTimeOffset.UtcNow;
            template.FileSize = file.Length;
            template.ImageHash = result.ImageId?.Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase);
            template.OriginalArchiveName = file.FileName;
            template.Description = $"Course image loaded from {result.SourceImage}";
            template.TrainingCourseId = courseId;

            if (existingTemplate is null)
                context.ImageTemplates.Add(template);
            await context.SaveChangesAsync(token);

            return Ok(TrainingCourseImageTemplateModel.FromTemplate(template));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new RequestResponse(ex.Message));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best effort cleanup */ }
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
            template.TrainingCourseId = courseId;
            context.ImageTemplates.Add(template);
            await context.SaveChangesAsync(token);
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

            var result = await archiveExtractor.ExtractAndRegisterAsync(archivePath, file.FileName, token);
            if (!result.Success || result.Template is null)
                return BadRequest(new RequestResponse(result.Error ?? "VM 归档处理失败。"));

            result.Template.TrainingCourseId = courseId;
            await context.SaveChangesAsync(token);
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
            var template = await importer.ImportFromLocalPathAsync(model.LocalPath, model.DisplayName);
            template.TrainingCourseId = courseId;
            await context.SaveChangesAsync(token);
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
        if (!template.TrainingCourseId.HasValue && actor.Role < Role.Admin)
            return Forbid();
        if (template.TrainingCourseId.HasValue && template.TrainingCourseId.Value != courseId)
            return BadRequest(new RequestResponse("该环境模板已属于其他课程。"));

        template.TrainingCourseId = courseId;
        await context.SaveChangesAsync(token);
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

        var template = await context.ImageTemplates.SingleOrDefaultAsync(t => t.Id == templateId, token);
        if (template is null)
            return NotFound();
        if (template.TrainingCourseId != courseId)
            return BadRequest(new RequestResponse("该环境模板不属于当前课程。"));

        var inUse = await context.ExerciseChallenges.AnyAsync(c =>
            c.TrainingCourseId == courseId && c.ImageTemplateId == templateId, token);
        if (inUse)
            return BadRequest(new RequestResponse("该环境模板正在被课程题目使用，不能移除。"));

        context.ImageTemplates.Remove(template);
        await context.SaveChangesAsync(token);
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
        if (string.IsNullOrWhiteSpace(model.Title))
            return BadRequest(new RequestResponse("题目名称不能为空。"));
        if (model.Type.IsContainer() && string.IsNullOrWhiteSpace(model.ContainerImage))
            return BadRequest(new RequestResponse("容器题目需要配置 Docker 镜像。"));
        if (model.Environment == EnvironmentType.WindowsVM && !model.ImageTemplateId.HasValue)
            return BadRequest(new RequestResponse("Windows 靶机题目需要选择课程内 VM 模板。"));

        if (model.ImageTemplateId.HasValue)
        {
            var ownsTemplate = await context.ImageTemplates.AnyAsync(t =>
                t.Id == model.ImageTemplateId.Value && t.TrainingCourseId == courseId, token);
            if (!ownsTemplate)
                return BadRequest(new RequestResponse("只能使用当前课程的环境模板。"));
        }

        var order = model.Order > 0
            ? model.Order
            : await context.TrainingCourseChallenges
                .Where(c => c.CourseId == courseId)
                .MaxAsync(c => (int?)c.Order, token) + 1 ?? 1;

        await using var transaction = await context.Database.BeginTransactionAsync(token);

        var exerciseType = model.Environment == EnvironmentType.WindowsVM && model.ImageTemplateId.HasValue
            ? ChallengeType.StaticContainer
            : model.Type;

        var exercise = new ExerciseChallenge
        {
            Title = model.Title.Trim(),
            Content = model.Content,
            Category = model.Category,
            Type = exerciseType,
            Environment = model.Environment,
            ImageTemplateId = model.ImageTemplateId,
            ContainerImage = model.ContainerImage?.Trim(),
            MemoryLimit = model.MemoryLimit,
            CPUCount = model.CPUCount,
            StorageLimit = model.StorageLimit,
            ExposePort = model.ExposePort,
            NetworkMode = model.NetworkMode,
            FlagTemplate = model.FlagTemplate,
            SubmissionLimit = model.SubmissionLimit,
            IsEnabled = true,
            TrainingCourseId = courseId
        };

        if (!string.IsNullOrWhiteSpace(model.StaticFlag) && !exercise.Type.IsDynamic())
        {
            exercise.Flags.Add(new FlagContext
            {
                Flag = model.StaticFlag.Trim(),
                OrderIndex = 0,
                ScoreMode = FlagScoreMode.InheritDecay,
                AnswerType = AnswerType.Flag
            });
        }

        context.ExerciseChallenges.Add(exercise);
        await context.SaveChangesAsync(token);

        var link = new TrainingCourseChallenge
        {
            CourseId = courseId,
            ExerciseChallengeId = exercise.Id,
            ExerciseChallenge = exercise,
            Order = order,
            IsRequired = model.IsRequired,
            DisplayTitle = string.IsNullOrWhiteSpace(model.DisplayTitle) ? null : model.DisplayTitle.Trim(),
            CreatedById = actor.Id
        };
        context.TrainingCourseChallenges.Add(link);

        if (model.ChapterId.HasValue)
        {
            var chapterExists = await context.TrainingCourseChapters
                .AnyAsync(c => c.Id == model.ChapterId.Value && c.CourseId == courseId, token);
            if (!chapterExists)
                return BadRequest(new RequestResponse("课程章节不存在。"));

            context.TrainingCourseChapterChallenges.Add(new TrainingCourseChapterChallenge
            {
                CourseId = courseId,
                ChapterId = model.ChapterId.Value,
                ExerciseChallengeId = exercise.Id,
                Order = order
            });
        }

        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);

        return Ok(TrainingCourseChallengeModel.FromChallenge(link, model.ChapterId));
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

        if (model.ChapterId.HasValue)
        {
            var chapterExists = await context.TrainingCourseChapters
                .AnyAsync(c => c.Id == model.ChapterId.Value && c.CourseId == courseId, token);
            if (!chapterExists)
                return BadRequest(new RequestResponse("课程章节不存在。"));

            var chapterLink = await context.TrainingCourseChapterChallenges.SingleOrDefaultAsync(c =>
                c.ChapterId == model.ChapterId.Value &&
                c.CourseId == courseId &&
                c.ExerciseChallengeId == model.ExerciseChallengeId, token);
            if (chapterLink is null)
            {
                context.TrainingCourseChapterChallenges.Add(new TrainingCourseChapterChallenge
                {
                    ChapterId = model.ChapterId.Value,
                    CourseId = courseId,
                    ExerciseChallengeId = model.ExerciseChallengeId,
                    Order = model.Order
                });
            }
            else
            {
                chapterLink.Order = model.Order;
            }
        }

        await context.SaveChangesAsync(token);
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
            .SingleOrDefaultAsync(c => c.Id == exerciseChallengeId, token);
        var linkExists = await context.TrainingCourseChallenges
            .AnyAsync(c => c.CourseId == courseId && c.ExerciseChallengeId == exerciseChallengeId, token);
        if (!linkExists && challenge?.TrainingCourseId != courseId)
            return NotFound();

        if (challenge?.TrainingCourseId == courseId)
        {
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
        return Ok();
    }
}
