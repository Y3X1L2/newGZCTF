using GZCTF.Middlewares;
using GZCTF.Models.Request.Training;
using GZCTF.Repositories.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[RequireTeacher]
[ApiController]
[Route("api/admin/training")]
public class TrainingAdminController(
    AppDbContext context,
    UserManager<UserInfo> userManager,
    IBlobRepository blobRepository,
    ILogger<TrainingAdminController> logger) : ControllerBase
{
    private async Task<UserInfo> CurrentUser() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Current user is missing.");

    private async Task<bool> CanUseGroup(UserInfo actor, int groupId, CancellationToken token) =>
        actor.Role >= Role.Admin ||
        await context.StudentGroupManagers.AnyAsync(m => m.GroupId == groupId && m.ManagerId == actor.Id, token);

    private async Task<bool> CanEditModule(UserInfo actor, TrainingModule module, CancellationToken token)
    {
        if (actor.Role >= Role.Admin || module.CreatedById == actor.Id)
            return true;

        var groupIds = module.Visibilities.Where(v => v.GroupId.HasValue).Select(v => v.GroupId!.Value);
        return await context.StudentGroupManagers.AnyAsync(m => groupIds.Contains(m.GroupId) && m.ManagerId == actor.Id, token);
    }

    private IQueryable<TrainingModule> ModuleQuery() =>
        context.TrainingModules
            .Include(m => m.EnvironmentTemplate)
            .Include(m => m.Visibilities)
            .ThenInclude(v => v.Group)
            .Include(m => m.Challenges)
            .ThenInclude(c => c.ExerciseChallenge)
            .Include(m => m.TheoryPlan);

    private async Task<RequestResponse?> ValidateModuleEdit(
        UserInfo actor,
        TrainingModuleEditModel model,
        int? moduleId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            return new RequestResponse("培训模块标题不能为空。");

        var direction = await context.TrainingDirections.SingleOrDefaultAsync(d => d.Id == model.DirectionId, token);
        if (direction is null)
            return new RequestResponse("培训方向不存在。");

        if (direction.Type != model.Type)
            return new RequestResponse("培训模块类型必须与所属方向一致。");

        if (moduleId.HasValue && model.ParentId == moduleId.Value)
            return new RequestResponse("父级模块不能选择自身。");

        if (model.ParentId.HasValue)
        {
            var parent = await context.TrainingModules
                .AsNoTracking()
                .SingleOrDefaultAsync(m => m.Id == model.ParentId.Value, token);
            if (parent is null)
                return new RequestResponse("父级培训模块不存在。");
            if (parent.DirectionId != model.DirectionId || parent.Type != model.Type)
                return new RequestResponse("父级模块必须属于同一培训方向。");
            var parentForPermission = await ModuleQuery().SingleAsync(m => m.Id == model.ParentId.Value, token);
            if (!await CanEditModule(actor, parentForPermission, token))
                return new RequestResponse("不能把模块挂到无权管理的父级大纲下。");

            var visited = new HashSet<int>();
            var current = parent.ParentId;
            while (current.HasValue)
            {
                if (!visited.Add(current.Value))
                    return new RequestResponse("培训模块层级存在循环引用。");
                if (current == moduleId)
                    return new RequestResponse("父级模块不能选择当前模块的子模块。");

                current = await context.TrainingModules
                    .AsNoTracking()
                    .Where(m => m.Id == current.Value)
                    .Select(m => m.ParentId)
                    .SingleOrDefaultAsync(token);
            }
        }

        if (model.EnvironmentTemplateId.HasValue)
        {
            var template = await context.ImageTemplates
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == model.EnvironmentTemplateId.Value, token);
            if (template is null)
                return new RequestResponse("环境模板不存在。");
            if (template.ImageType != ImageType.Docker || template.Status != ImageStatus.Ready)
                return new RequestResponse("培训模块只能选择已就绪的 Docker 环境模板。");
        }

        return null;
    }

    [HttpGet("directions")]
    [ProducesResponseType(typeof(TrainingDirectionModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDirections([FromQuery] TrainingType? type = null, CancellationToken token = default)
    {
        var query = context.TrainingDirections.AsQueryable();
        if (type.HasValue)
            query = query.Where(d => d.Type == type.Value);

        var directions = await query.OrderBy(d => d.Type).ThenBy(d => d.Order).ToArrayAsync(token);
        return Ok(directions.Select(d => TrainingDirectionModel.FromDirection(d)).ToArray());
    }

    [HttpPost("directions")]
    [ProducesResponseType(typeof(TrainingDirectionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateDirection([FromBody] TrainingDirectionEditModel model, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var direction = new TrainingDirection
        {
            Type = model.Type,
            Key = model.Key.Trim(),
            Title = model.Title.Trim(),
            Description = model.Description.Trim(),
            Icon = model.Icon.Trim(),
            Color = model.Color.Trim(),
            Order = model.Order,
            IsEnabled = model.IsEnabled,
            CreatedById = actor.Id
        };

        context.TrainingDirections.Add(direction);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Created training direction {direction.Title}.", TaskStatus.Success, LogLevel.Information);

        return Ok(TrainingDirectionModel.FromDirection(direction));
    }

    [HttpPut("directions/{directionId:int}")]
    public async Task<IActionResult> UpdateDirection([FromRoute] int directionId, [FromBody] TrainingDirectionEditModel model,
        CancellationToken token = default)
    {
        var direction = await context.TrainingDirections.SingleOrDefaultAsync(d => d.Id == directionId, token);
        if (direction is null)
            return NotFound();

        direction.Type = model.Type;
        direction.Key = model.Key.Trim();
        direction.Title = model.Title.Trim();
        direction.Description = model.Description.Trim();
        direction.Icon = model.Icon.Trim();
        direction.Color = model.Color.Trim();
        direction.Order = model.Order;
        direction.IsEnabled = model.IsEnabled;
        direction.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Updated training direction {direction.Title}.", TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    [HttpGet("modules")]
    [ProducesResponseType(typeof(TrainingModuleModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModules([FromQuery] TrainingType? type = null, [FromQuery] int? directionId = null,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var query = ModuleQuery();

        if (type.HasValue)
            query = query.Where(m => m.Type == type.Value);
        if (directionId.HasValue)
            query = query.Where(m => m.DirectionId == directionId.Value);
        if (actor.Role < Role.Admin)
            query = query.Where(m => m.CreatedById == actor.Id || m.Visibilities.Any(v =>
                v.GroupId.HasValue && v.Group!.Managers.Any(gm => gm.ManagerId == actor.Id)));

        var modules = await query.OrderBy(m => m.DirectionId).ThenBy(m => m.Order).ToArrayAsync(token);
        return Ok(modules.Select(m => TrainingModuleModel.FromModule(m)).ToArray());
    }

    [HttpGet("modules/{moduleId:int}")]
    [ProducesResponseType(typeof(TrainingModuleModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModule([FromRoute] int moduleId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);

        if (module is null)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();

        return Ok(TrainingModuleModel.FromModule(module));
    }

    [HttpPost("modules")]
    [ProducesResponseType(typeof(TrainingModuleModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateModule([FromBody] TrainingModuleEditModel model, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (await ValidateModuleEdit(actor, model, null, token) is { } validationError)
            return BadRequest(validationError);

        var module = new TrainingModule
        {
            DirectionId = model.DirectionId,
            ParentId = model.ParentId,
            Type = model.Type,
            Title = model.Title.Trim(),
            Slug = string.IsNullOrWhiteSpace(model.Slug) ? model.Title.Trim() : model.Slug.Trim(),
            Summary = model.Summary.Trim(),
            ArticleContent = model.ArticleContent,
            ArticleContentType = model.ArticleContentType,
            EnvironmentTemplateId = model.EnvironmentTemplateId,
            CompletionRule = model.CompletionRule,
            Order = model.Order,
            CreatedById = actor.Id,
            UpdatedById = actor.Id
        };

        context.TrainingModules.Add(module);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Created training module {module.Title}.", TaskStatus.Success, LogLevel.Information);

        module = await ModuleQuery().SingleAsync(m => m.Id == module.Id, token);
        return Ok(TrainingModuleModel.FromModule(module));
    }

    [HttpPut("modules/{moduleId:int}")]
    public async Task<IActionResult> UpdateModule([FromRoute] int moduleId, [FromBody] TrainingModuleEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();
        if (await ValidateModuleEdit(actor, model, moduleId, token) is { } validationError)
            return BadRequest(validationError);

        module.DirectionId = model.DirectionId;
        module.ParentId = model.ParentId;
        module.Type = model.Type;
        module.Title = model.Title.Trim();
        module.Slug = string.IsNullOrWhiteSpace(model.Slug) ? model.Title.Trim() : model.Slug.Trim();
        module.Summary = model.Summary.Trim();
        module.ArticleContent = model.ArticleContent;
        module.ArticleContentType = model.ArticleContentType;
        module.EnvironmentTemplateId = model.EnvironmentTemplateId;
        module.CompletionRule = model.CompletionRule;
        module.Order = model.Order;
        module.UpdatedById = actor.Id;
        module.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Updated training module {module.Title}.", TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    [HttpPut("modules/{moduleId:int}/visibility")]
    public async Task<IActionResult> SetModuleVisibility([FromRoute] int moduleId,
        [FromBody] TrainingModuleVisibilityEditModel[] model, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();

        foreach (var item in model)
        {
            if (item.VisibilityType == TrainingVisibilityType.AllStudents && actor.Role < Role.Admin)
                return Forbid();

            if (item.GroupId.HasValue && !await CanUseGroup(actor, item.GroupId.Value, token))
                return Forbid();

            if (item.VisibilityType == TrainingVisibilityType.GroupOnly && !item.GroupId.HasValue)
                return BadRequest(new RequestResponse("按分组发布时必须选择学生分组。"));
        }

        var deduped = model
            .GroupBy(item => item.VisibilityType == TrainingVisibilityType.AllStudents
                ? $"{item.VisibilityType}"
                : $"{item.VisibilityType}:{item.GroupId}")
            .Select(group => group.First())
            .ToArray();

        context.TrainingModuleVisibilities.RemoveRange(module.Visibilities);
        foreach (var item in deduped)
        {
            context.TrainingModuleVisibilities.Add(new TrainingModuleVisibility
            {
                ModuleId = moduleId,
                VisibilityType = item.VisibilityType,
                GroupId = item.GroupId,
                CreatedById = actor.Id
            });
        }

        await context.SaveChangesAsync(token);
        logger.SystemLog($"Updated training module visibility for {module.Title}.",
            TaskStatus.Success, LogLevel.Information);
        return Ok();
    }

    [HttpPost("modules/{moduleId:int}/publish")]
    public async Task<IActionResult> PublishModule([FromRoute] int moduleId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();
        if (module.Visibilities.Count == 0)
            return BadRequest(new RequestResponse("发布培训模块前需要先配置可见分组。"));
        if (module.Type == TrainingType.Ctf && module.Challenges.Count == 0)
            return BadRequest(new RequestResponse("发布 CTF 培训模块前需要至少配置一道练手题。"));
        if (module.Type == TrainingType.Theory && (module.TheoryPlan is null || !module.TheoryPlan.IsPublished))
            return BadRequest(new RequestResponse("发布理论培训模块前需要先保存并发布测验计划。"));

        module.IsPublished = true;
        module.PublishedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Published training module {module.Title}.", TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    [HttpPost("modules/{moduleId:int}/unpublish")]
    public async Task<IActionResult> UnpublishModule([FromRoute] int moduleId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();

        module.IsPublished = false;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Unpublished training module {module.Title}.", TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    [HttpPost("modules/{moduleId:int}/challenges")]
    public async Task<IActionResult> AddModuleChallenge([FromRoute] int moduleId,
        [FromBody] TrainingModuleChallengeEditModel model, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();

        var challenge = await context.ExerciseChallenges.SingleOrDefaultAsync(c => c.Id == model.ExerciseChallengeId, token);
        if (challenge is null)
            return BadRequest(new RequestResponse("培训题目不存在。"));
        if (challenge.TrainingCourseId.HasValue)
            return BadRequest(new RequestResponse("课程专属题目不能绑定到旧培训模块。"));

        var exists = await context.TrainingModuleChallenges.AnyAsync(c =>
            c.ModuleId == moduleId && c.ExerciseChallengeId == model.ExerciseChallengeId, token);
        if (!exists)
        {
            context.TrainingModuleChallenges.Add(new TrainingModuleChallenge
            {
                ModuleId = moduleId,
                ExerciseChallengeId = model.ExerciseChallengeId,
                Order = model.Order,
                IsRequired = model.IsRequired,
                DisplayTitle = model.DisplayTitle,
                CreatedById = actor.Id
            });
            await context.SaveChangesAsync(token);
            logger.SystemLog($"Added training challenge {challenge.Title} to module {module.Title}.",
                TaskStatus.Success, LogLevel.Information);
        }

        return Ok();
    }

    [HttpPost("modules/{moduleId:int}/challenges/from-game-challenge/{challengeId:int}")]
    [ProducesResponseType(typeof(TrainingModuleChallengeModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddChallengeFromGameChallenge(
        [FromRoute] int moduleId,
        [FromRoute] int challengeId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null || module.Type != TrainingType.Ctf)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();

        var source = await context.GameChallenges
            .Include(c => c.Flags)
            .ThenInclude(f => f.Attachment)
            .ThenInclude(a => a!.LocalFile)
            .Include(c => c.Attachment)
            .ThenInclude(a => a!.LocalFile)
            .SingleOrDefaultAsync(c => c.Id == challengeId, token);
        if (source is null)
            return NotFound(new RequestResponse("正式比赛题目不存在。", StatusCodes.Status404NotFound));

        var exercise = new ExerciseChallenge
        {
            Title = source.Title,
            Content = source.Content,
            Category = source.Category,
            Type = source.Type,
            Hints = source.Hints,
            IsEnabled = true,
            DeadlineUtc = null,
            SubmissionLimit = source.SubmissionLimit,
            ContainerImage = source.ContainerImage,
            MemoryLimit = source.MemoryLimit,
            StorageLimit = source.StorageLimit,
            CPUCount = source.CPUCount,
            ExposePort = source.ExposePort,
            NetworkMode = source.NetworkMode,
            FileName = source.FileName,
            FlagTemplate = source.FlagTemplate,
            Environment = source.Environment,
            ImageTemplateId = source.ImageTemplateId,
            Credit = true,
            Difficulty = Difficulty.Normal,
            Tags = [source.Category.ToString()]
        };

        if (source.Attachment is not null)
            exercise.Attachment = await CopyAttachment(source.Attachment, token);

        foreach (var flag in source.Flags.OrderBy(f => f.OrderIndex))
        {
            exercise.Flags.Add(new FlagContext
            {
                Flag = flag.Flag,
                OrderIndex = flag.OrderIndex,
                Description = flag.Description,
                ScoreMode = flag.ScoreMode,
                FixedScore = flag.FixedScore,
                MaxAttempts = flag.MaxAttempts,
                AttachmentHash = flag.AttachmentHash,
                AnswerType = flag.AnswerType,
                CustomName = flag.CustomName,
                Attachment = flag.Attachment is null ? null : await CopyAttachment(flag.Attachment, token)
            });
        }

        context.ExerciseChallenges.Add(exercise);
        await context.SaveChangesAsync(token);

        var link = new TrainingModuleChallenge
        {
            ModuleId = moduleId,
            ExerciseChallengeId = exercise.Id,
            Order = module.Challenges.Count + 1,
            IsRequired = true,
            CreatedById = actor.Id
        };
        context.TrainingModuleChallenges.Add(link);
        await context.SaveChangesAsync(token);

        link.ExerciseChallenge = exercise;
        logger.SystemLog($"Copied game challenge {source.Title} to training module {module.Title}.",
            TaskStatus.Success, LogLevel.Information);

        return Ok(TrainingModuleChallengeModel.FromChallenge(link));
    }

    private async Task<Attachment> CopyAttachment(Attachment source, CancellationToken token)
    {
        var attachment = new Attachment
        {
            Type = source.Type,
            RemoteUrl = source.RemoteUrl
        };

        if (source.Type == FileType.Local && source.LocalFile is not null)
        {
            await blobRepository.IncrementBlobReference(source.LocalFile.Hash, token);
            attachment.LocalFileId = source.LocalFileId;
            attachment.LocalFile = source.LocalFile;
        }

        return attachment;
    }

    [HttpDelete("modules/{moduleId:int}/challenges/{exerciseChallengeId:int}")]
    public async Task<IActionResult> RemoveModuleChallenge([FromRoute] int moduleId, [FromRoute] int exerciseChallengeId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();

        var link = await context.TrainingModuleChallenges.SingleOrDefaultAsync(c =>
            c.ModuleId == moduleId && c.ExerciseChallengeId == exerciseChallengeId, token);
        if (link is null)
            return NotFound();

        context.TrainingModuleChallenges.Remove(link);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Removed training challenge {exerciseChallengeId} from module {module.Title}.",
            TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    [HttpGet("modules/{moduleId:int}/theory-plan")]
    [ProducesResponseType(typeof(TheoryTrainingPlanModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTheoryPlan([FromRoute] int moduleId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null || module.Type != TrainingType.Theory)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();

        var plan = await context.TheoryTrainingPlans
            .Include(p => p.Questions)
            .ThenInclude(q => q.SourceQuestion)
            .SingleOrDefaultAsync(p => p.ModuleId == moduleId, token);

        return plan is null
            ? Ok(new TheoryTrainingPlanModel
            {
                ModuleId = moduleId,
                Title = module.Title,
                Description = module.Summary,
                QuestionCount = 30,
                PassRate = module.CompletionRule.TheoryPassRate,
                AllowRetake = true,
                ShowCorrectAnswerAfterSubmit = true,
                IsPublished = false
            })
            : Ok(TheoryTrainingPlanModel.FromPlan(plan));
    }

    [HttpPut("modules/{moduleId:int}/theory-plan")]
    [ProducesResponseType(typeof(TheoryTrainingPlanModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveTheoryPlan(
        [FromRoute] int moduleId,
        [FromBody] TheoryTrainingPlanEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var module = await ModuleQuery().SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null || module.Type != TrainingType.Theory)
            return NotFound();
        if (!await CanEditModule(actor, module, token))
            return Forbid();

        if (string.IsNullOrWhiteSpace(model.Title))
            return BadRequest(new RequestResponse("理论培训计划标题不能为空。"));

        var sourceIds = model.Questions.Select(q => q.SourceQuestionId).Distinct().ToArray();
        var sourceQuestions = sourceIds.Length == 0
            ? []
            : await context.TheoryQuestionBankItems.Where(q => sourceIds.Contains(q.Id)).ToArrayAsync(token);
        if (sourceIds.Except(sourceQuestions.Select(q => q.Id)).Any())
            return BadRequest(new RequestResponse("部分理论题目不存在。"));

        if (model.Mode == TheoryTrainingMode.Manual && model.Questions.Count == 0)
            return BadRequest(new RequestResponse("手动组卷至少需要选择一道题。"));

        var plan = await context.TheoryTrainingPlans
            .Include(p => p.Questions)
            .SingleOrDefaultAsync(p => p.ModuleId == moduleId, token);

        plan ??= new TheoryTrainingPlan
        {
            ModuleId = moduleId,
            CreatedById = actor.Id
        };

        plan.Title = model.Title.Trim();
        plan.Description = model.Description.Trim();
        plan.Mode = model.Mode;
        plan.QuestionCount = Math.Max(1, model.QuestionCount);
        plan.BankName = string.IsNullOrWhiteSpace(model.BankName) ? null : model.BankName.Trim();
        plan.QuestionTypes = model.QuestionTypes ?? [];
        plan.PassRate = Math.Clamp(model.PassRate, 0, 100);
        plan.AllowRetake = model.AllowRetake;
        plan.ShowCorrectAnswerAfterSubmit = model.ShowCorrectAnswerAfterSubmit;
        plan.IsPublished = model.IsPublished;
        plan.UpdatedById = actor.Id;
        plan.UpdatedAt = DateTimeOffset.UtcNow;

        context.TheoryTrainingPlanQuestions.RemoveRange(plan.Questions);
        plan.Questions.Clear();
        foreach (var (item, index) in model.Questions.Select((q, i) => (q, i)))
        {
            plan.Questions.Add(new TheoryTrainingPlanQuestion
            {
                SourceQuestionId = item.SourceQuestionId,
                Score = Math.Max(1, item.Score),
                Order = item.Order > 0 ? item.Order : index + 1
            });
        }

        if (plan.Id == 0)
            context.TheoryTrainingPlans.Add(plan);

        module.CompletionRule.TheoryPassRate = plan.PassRate;
        module.UpdatedById = actor.Id;
        module.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(token);

        plan = await context.TheoryTrainingPlans
            .Include(p => p.Questions)
            .ThenInclude(q => q.SourceQuestion)
            .SingleAsync(p => p.ModuleId == moduleId, token);

        logger.SystemLog($"Saved theory training plan {plan.Title}.", TaskStatus.Success, LogLevel.Information);
        return Ok(TheoryTrainingPlanModel.FromPlan(plan));
    }

    [HttpGet("stats/groups/{groupId:int}")]
    [ProducesResponseType(typeof(TrainingGroupStatsModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GroupStats([FromRoute] int groupId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (!await CanUseGroup(actor, groupId, token))
            return Forbid();

        var group = await context.StudentGroups
            .Include(g => g.Members)
            .ThenInclude(m => m.Student)
            .SingleOrDefaultAsync(g => g.Id == groupId, token);
        if (group is null)
            return NotFound();

        var modules = await ModuleQuery()
            .Where(m => m.IsPublished && m.Visibilities.Any(v =>
                v.VisibilityType == TrainingVisibilityType.AllStudents ||
                v.GroupId == groupId))
            .ToArrayAsync(token);
        var moduleIds = modules.Select(m => m.Id).ToArray();
        var studentIds = group.Members.Select(m => m.StudentId).ToArray();
        var progresses = await context.TrainingModuleProgresses
            .Where(p => studentIds.Contains(p.UserId) && moduleIds.Contains(p.ModuleId))
            .ToArrayAsync(token);

        var moduleById = modules.ToDictionary(m => m.Id);
        var students = group.Members
            .OrderBy(m => m.Student.UserName)
            .Select(member =>
            {
                var owned = progresses.Where(p => p.UserId == member.StudentId).ToArray();
                return new TrainingStudentProgressModel
                {
                    UserId = member.StudentId,
                    UserName = member.Student.UserName ?? string.Empty,
                    RealName = member.Student.RealName,
                    TotalModules = modules.Length,
                    CompletedModules = owned.Count(p => p.Status == TrainingModuleProgressStatus.Completed),
                    CtfTotalChallenges = modules.Where(m => m.Type == TrainingType.Ctf).Sum(m => m.Challenges.Count),
                    CtfSolvedChallenges = owned.Sum(p => p.ChallengeSolvedCount),
                    TheoryTotalModules = modules.Count(m => m.Type == TrainingType.Theory),
                    TheoryCompletedModules = owned.Count(p =>
                        moduleById.TryGetValue(p.ModuleId, out var module) &&
                        module.Type == TrainingType.Theory &&
                        p.Status == TrainingModuleProgressStatus.Completed),
                    LastActivity = owned.Length == 0 ? null : owned.Max(p => p.UpdatedAt)
                };
            })
            .ToList();

        var avg = students.Count == 0 || modules.Length == 0
            ? 0
            : Math.Round(students.Average(s => s.CompletedModules * 100.0 / modules.Length), 2);

        return Ok(new TrainingGroupStatsModel
        {
            GroupId = group.Id,
            GroupName = group.Name,
            StudentCount = group.Members.Count,
            TotalModules = modules.Length,
            AverageCompletionRate = avg,
            Students = students
        });
    }

    [HttpGet("stats/overview")]
    [ProducesResponseType(typeof(TrainingGroupStatsModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> StatsOverview(CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var groups = await context.StudentGroups
            .Where(g => !g.IsArchived)
            .Where(g => actor.Role >= Role.Admin || g.Managers.Any(m => m.ManagerId == actor.Id))
            .OrderBy(g => g.Name)
            .Select(g => new TrainingGroupStatsModel
            {
                GroupId = g.Id,
                GroupName = g.Name,
                StudentCount = g.Members.Count,
                TotalModules = context.TrainingModules.Count(m => m.IsPublished && m.Visibilities.Any(v =>
                    v.VisibilityType == TrainingVisibilityType.AllStudents || v.GroupId == g.Id))
            })
            .ToArrayAsync(token);

        return Ok(groups);
    }
}
