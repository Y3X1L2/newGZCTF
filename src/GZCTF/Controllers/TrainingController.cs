using GZCTF.Middlewares;
using GZCTF.Models;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Request.Training;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Config;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[RequireStudent]
[ApiController]
[Route("api/training")]
public class TrainingController(
    AppDbContext context,
    UserManager<UserInfo> userManager,
    IExerciseInstanceRepository exerciseInstanceRepository,
    IContainerRepository containerRepository,
    IConfigService configService,
    ILogger<TrainingController> logger) : ControllerBase
{
    private async Task<UserInfo> CurrentUser() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Current user is missing.");

    private IQueryable<TrainingModule> VisibleModules(UserInfo user)
    {
        var groupIds = context.StudentGroupMembers
            .Where(m => m.StudentId == user.Id)
            .Select(m => m.GroupId);

        return context.TrainingModules
            .Include(m => m.EnvironmentTemplate)
            .Include(m => m.Visibilities)
            .ThenInclude(v => v.Group)
            .Include(m => m.Challenges)
            .ThenInclude(c => c.ExerciseChallenge)
            .Where(m => m.IsPublished && m.Direction.IsEnabled && m.Visibilities.Any(v =>
                v.VisibilityType == TrainingVisibilityType.AllStudents ||
                v.GroupId.HasValue && groupIds.Contains(v.GroupId.Value)));
    }

    private async Task<TrainingModule?> GetVisibleModule(UserInfo user, int moduleId, CancellationToken token) =>
        await VisibleModules(user)
            .SingleOrDefaultAsync(m => m.Id == moduleId, token);

    private async Task<TrainingModule?> GetVisibleModuleWithTheory(
        UserInfo user,
        int moduleId,
        CancellationToken token) =>
        await VisibleModules(user)
            .Include(m => m.TheoryPlan!)
            .ThenInclude(p => p.Questions)
            .ThenInclude(q => q.SourceQuestion)
            .SingleOrDefaultAsync(m => m.Id == moduleId, token);

    private async Task<ExerciseInstance?> GetOrCreateTrainingInstance(
        UserInfo user,
        TrainingModule module,
        int exerciseChallengeId,
        CancellationToken token)
    {
        if (module.Type != TrainingType.Ctf ||
            module.Challenges.All(c => c.ExerciseChallengeId != exerciseChallengeId))
            return null;

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

    private async Task<TrainingModuleProgress> EnsureModuleProgress(
        UserInfo user,
        TrainingModule module,
        CancellationToken token)
    {
        var progress = await context.TrainingModuleProgresses
            .SingleOrDefaultAsync(p => p.ModuleId == module.Id && p.UserId == user.Id, token);

        if (progress is not null)
            return progress;

        progress = new TrainingModuleProgress
        {
            ModuleId = module.Id,
            UserId = user.Id,
            Status = TrainingModuleProgressStatus.NotStarted,
            StartedAt = DateTimeOffset.UtcNow,
            ChallengeTotalCount = module.Challenges.Count
        };
        context.TrainingModuleProgresses.Add(progress);
        return progress;
    }

    private async Task<TrainingModuleProgress> RecalculateProgress(
        UserInfo user,
        TrainingModule module,
        CancellationToken token)
    {
        var progress = await EnsureModuleProgress(user, module, token);
        var challengeIds = module.Challenges.Select(c => c.ExerciseChallengeId).ToArray();
        var solvedIds = await context.TrainingCtfSubmissions
            .Where(s => s.UserId == user.Id &&
                        s.ModuleId == module.Id &&
                        s.Status == AnswerResult.Accepted &&
                        challengeIds.Contains(s.ExerciseChallengeId))
            .Select(s => s.ExerciseChallengeId)
            .Distinct()
            .ToArrayAsync(token);

        var articleRead = !module.CompletionRule.RequireArticleRead ||
                          await context.TrainingArticleProgresses.AnyAsync(p =>
                              p.UserId == user.Id && p.ModuleId == module.Id && p.CompletedAt != null, token);

        var requiredIds = module.Challenges
            .Where(c => c.IsRequired)
            .Select(c => c.ExerciseChallengeId)
            .ToHashSet();

        var challengeCompleted = module.Type switch
        {
            TrainingType.Ctf when module.CompletionRule.RequireAllRequiredChallenges =>
                requiredIds.Count == 0 || requiredIds.IsSubsetOf(solvedIds),
            TrainingType.Ctf when module.CompletionRule.RequiredChallengeCount > 0 =>
                solvedIds.Length >= module.CompletionRule.RequiredChallengeCount,
            TrainingType.Ctf => true,
            TrainingType.Theory => progress.TheoryBestPassRate >= module.CompletionRule.TheoryPassRate,
            _ => false
        };

        progress.ChallengeTotalCount = module.Challenges.Count;
        progress.ChallengeSolvedCount = solvedIds.Length;
        progress.Status = articleRead && challengeCompleted
            ? TrainingModuleProgressStatus.Completed
            : solvedIds.Length > 0 || progress.TheoryBestScore.HasValue
                ? TrainingModuleProgressStatus.Practicing
                : articleRead
                    ? TrainingModuleProgressStatus.Reading
                    : TrainingModuleProgressStatus.NotStarted;
        progress.StartedAt ??= DateTimeOffset.UtcNow;
        progress.CompletedAt = progress.Status == TrainingModuleProgressStatus.Completed
            ? progress.CompletedAt ?? DateTimeOffset.UtcNow
            : null;
        progress.UpdatedAt = DateTimeOffset.UtcNow;

        return progress;
    }

    [HttpGet("catalog")]
    [ProducesResponseType(typeof(TrainingDirectionModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog(CancellationToken token = default)
    {
        var user = await CurrentUser();
        var modules = await VisibleModules(user).ToArrayAsync(token);
        var moduleIds = modules.Select(m => m.Id).ToArray();
        var progresses = await context.TrainingModuleProgresses
            .Where(p => p.UserId == user.Id && moduleIds.Contains(p.ModuleId))
            .ToDictionaryAsync(p => p.ModuleId, token);

        var directions = await context.TrainingDirections
            .Where(d => d.IsEnabled && modules.Select(m => m.DirectionId).Contains(d.Id))
            .OrderBy(d => d.Type)
            .ThenBy(d => d.Order)
            .ToArrayAsync(token);

        var moduleModels = modules
            .OrderBy(m => m.DirectionId)
            .ThenBy(m => m.ParentId)
            .ThenBy(m => m.Order)
            .Select(m => TrainingModuleModel.FromModule(m, progresses.GetValueOrDefault(m.Id)))
            .GroupBy(m => m.DirectionId)
            .ToDictionary(g => g.Key, g => g.AsEnumerable());

        return Ok(directions.Select(d => TrainingDirectionModel.FromDirection(
            d, moduleModels.GetValueOrDefault(d.Id) ?? [])).ToArray());
    }

    [HttpGet("overview")]
    [ProducesResponseType(typeof(TrainingOverviewModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview(CancellationToken token = default)
    {
        var user = await CurrentUser();
        var modules = await VisibleModules(user).ToArrayAsync(token);
        var moduleIds = modules.Select(m => m.Id).ToArray();
        var progresses = await context.TrainingModuleProgresses
            .Include(p => p.Module)
            .Where(p => p.UserId == user.Id && moduleIds.Contains(p.ModuleId))
            .ToArrayAsync(token);

        return Ok(new TrainingOverviewModel
        {
            TotalModules = modules.Length,
            CompletedModules = progresses.Count(p => p.Status == TrainingModuleProgressStatus.Completed),
            CtfTotalChallenges = modules.Where(m => m.Type == TrainingType.Ctf).Sum(m => m.Challenges.Count),
            CtfSolvedChallenges = progresses.Sum(p => p.ChallengeSolvedCount),
            TheoryTotalModules = modules.Count(m => m.Type == TrainingType.Theory),
            TheoryCompletedModules = progresses.Count(p =>
                p.Module.Type == TrainingType.Theory && p.Status == TrainingModuleProgressStatus.Completed)
        });
    }

    [HttpGet("modules/{moduleId:int}")]
    [ProducesResponseType(typeof(TrainingModuleModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Module([FromRoute] int moduleId, CancellationToken token = default)
    {
        var user = await CurrentUser();
        var module = await VisibleModules(user).SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null)
            return NotFound();

        var progress = await context.TrainingModuleProgresses
            .SingleOrDefaultAsync(p => p.UserId == user.Id && p.ModuleId == moduleId, token);

        return Ok(TrainingModuleModel.FromModule(module, progress));
    }

    [HttpPost("modules/{moduleId:int}/read")]
    public async Task<IActionResult> MarkRead([FromRoute] int moduleId, CancellationToken token = default)
    {
        var user = await CurrentUser();
        var module = await VisibleModules(user).SingleOrDefaultAsync(m => m.Id == moduleId, token);
        if (module is null)
            return NotFound();

        var progress = await context.TrainingArticleProgresses
            .SingleOrDefaultAsync(p => p.ModuleId == moduleId && p.UserId == user.Id, token);
        if (progress is null)
        {
            progress = new TrainingArticleProgress { ModuleId = moduleId, UserId = user.Id };
            context.TrainingArticleProgresses.Add(progress);
        }

        progress.ReadPercent = 100;
        progress.CompletedAt ??= DateTimeOffset.UtcNow;
        progress.LastReadAt = DateTimeOffset.UtcNow;

        await RecalculateProgress(user, module, token);
        await context.SaveChangesAsync(token);

        return Ok();
    }

    [HttpGet("ctf/modules/{moduleId:int}/challenges")]
    [ProducesResponseType(typeof(TrainingModuleChallengeModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> CtfChallenges([FromRoute] int moduleId, CancellationToken token = default)
    {
        var user = await CurrentUser();
        var module = await GetVisibleModule(user, moduleId, token);
        if (module is null || module.Type != TrainingType.Ctf)
            return NotFound();

        await RecalculateProgress(user, module, token);
        await context.SaveChangesAsync(token);

        return Ok(module.Challenges.OrderBy(c => c.Order).Select(TrainingModuleChallengeModel.FromChallenge).ToArray());
    }

    [HttpGet("ctf/modules/{moduleId:int}/challenges/{challengeId:int}")]
    [ProducesResponseType(typeof(TrainingCtfChallengeDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CtfChallenge(
        [FromRoute] int moduleId,
        [FromRoute] int challengeId,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var module = await GetVisibleModule(user, moduleId, token);
        if (module is null)
            return NotFound();

        var instance = await GetOrCreateTrainingInstance(user, module, challengeId, token);
        if (instance is null)
            return NotFound();

        var attempts = await context.TrainingCtfSubmissions
            .CountAsync(s => s.UserId == user.Id && s.ModuleId == moduleId && s.ExerciseChallengeId == challengeId, token);

        var solved = await context.TrainingCtfSubmissions.AnyAsync(s =>
            s.UserId == user.Id &&
            s.ModuleId == moduleId &&
            s.ExerciseChallengeId == challengeId &&
            s.Status == AnswerResult.Accepted, token);

        return Ok(TrainingCtfChallengeDetailModel.FromInstance(moduleId, instance, attempts, solved));
    }

    [HttpPost("ctf/modules/{moduleId:int}/challenges/{challengeId:int}/container")]
    [ProducesResponseType(typeof(ContainerInfoModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCtfContainer(
        [FromRoute] int moduleId,
        [FromRoute] int challengeId,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var module = await GetVisibleModule(user, moduleId, token);
        if (module is null)
            return NotFound();

        var instance = await GetOrCreateTrainingInstance(user, module, challengeId, token);
        if (instance is null || !instance.Exercise.IsEnabled)
            return NotFound(new RequestResponse("培训题目不存在或未启用。", StatusCodes.Status404NotFound));

        if (!instance.Exercise.Type.IsContainer())
            return BadRequest(new RequestResponse("该培训题目不需要启动容器。"));

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
            return BadRequest(new RequestResponse("培训容器创建失败，请稍后重试。"));

        var progress = await EnsureModuleProgress(user, module, token);
        progress.Status = TrainingModuleProgressStatus.Practicing;
        progress.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);

        logger.Log("创建培训容器：用户 {UserName}，模块 {Module}，题目 {Challenge}",
            user, TaskStatus.Success);

        return Ok(ContainerInfoModel.FromContainer(result.Result));
    }

    [HttpDelete("ctf/modules/{moduleId:int}/challenges/{challengeId:int}/container")]
    public async Task<IActionResult> DestroyCtfContainer(
        [FromRoute] int moduleId,
        [FromRoute] int challengeId,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var module = await GetVisibleModule(user, moduleId, token);
        if (module is null)
            return NotFound();

        var instance = await GetOrCreateTrainingInstance(user, module, challengeId, token);
        if (instance is null)
            return NotFound();

        if (!instance.Exercise.Type.IsContainer())
            return BadRequest(new RequestResponse("该培训题目不需要启动容器。"));

        if (instance.Container is null)
            return BadRequest(new RequestResponse("培训容器尚未启动。"));

        if (instance.IsContainerOperationTooFrequent)
            return RequestResponse.Result("操作过于频繁，请稍后再试。", StatusCodes.Status429TooManyRequests);

        if (!await containerRepository.DestroyContainer(instance.Container, token))
            return BadRequest(new RequestResponse("培训容器销毁失败。"));

        instance.LastContainerOperation = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);

        return Ok();
    }

    [HttpPost("ctf/modules/{moduleId:int}/challenges/{challengeId:int}/submit")]
    [ProducesResponseType(typeof(TrainingSubmitResultModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitCtfFlag(
        [FromRoute] int moduleId,
        [FromRoute] int challengeId,
        [FromBody] FlagSubmitModel model,
        CancellationToken token = default)
    {
        var answer = configService.DecryptApiData(model.Flag)?.Trim() ?? model.Flag.Trim();
        if (string.IsNullOrWhiteSpace(answer))
            return BadRequest(new RequestResponse("Flag 不能为空。"));
        if (answer.Length > Limits.MaxFlagLength)
            return BadRequest(new RequestResponse("Flag 过长。"));

        var user = await CurrentUser();
        var module = await GetVisibleModule(user, moduleId, token);
        if (module is null)
            return NotFound();

        var instance = await GetOrCreateTrainingInstance(user, module, challengeId, token);
        if (instance is null)
            return NotFound(new RequestResponse("培训题目不存在。", StatusCodes.Status404NotFound));

        var attempts = await context.TrainingCtfSubmissions
            .CountAsync(s => s.UserId == user.Id && s.ModuleId == moduleId && s.ExerciseChallengeId == challengeId, token);
        if (instance.Exercise.SubmissionLimit > 0 && attempts >= instance.Exercise.SubmissionLimit)
            return BadRequest(new RequestResponse("该培训题目的提交次数已用完。"));

        var verify = await exerciseInstanceRepository.VerifyAnswer(user, instance, answer, model.FlagId, token);
        var submission = new TrainingCtfSubmission
        {
            ModuleId = moduleId,
            ExerciseChallengeId = challengeId,
            UserId = user.Id,
            Status = verify.Status,
            SubmittedAnswerHash = answer.ToSHA256String(),
            FlagId = verify.FlagId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty
        };
        context.TrainingCtfSubmissions.Add(submission);
        await context.SaveChangesAsync(token);

        var progress = await RecalculateProgress(user, module, token);
        await context.SaveChangesAsync(token);

        if (verify.Status == AnswerResult.Accepted)
            logger.Log($"培训题目解出：{module.Title} / {instance.Exercise.Title}", user, TaskStatus.Success);

        return Ok(new TrainingSubmitResultModel
        {
            SubmissionId = submission.Id,
            Status = verify.Status,
            ModuleCompleted = progress.Status == TrainingModuleProgressStatus.Completed
        });
    }

    [HttpGet("theory/modules/{moduleId:int}")]
    [ProducesResponseType(typeof(TrainingModuleModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> TheoryModule([FromRoute] int moduleId, CancellationToken token = default)
    {
        var user = await CurrentUser();
        var module = await GetVisibleModuleWithTheory(user, moduleId, token);
        if (module is null || module.Type != TrainingType.Theory)
            return NotFound();

        var progress = await context.TrainingModuleProgresses
            .SingleOrDefaultAsync(p => p.UserId == user.Id && p.ModuleId == moduleId, token);

        return Ok(TrainingModuleModel.FromModule(module, progress));
    }

    [HttpGet("theory/modules/{moduleId:int}/session")]
    [ProducesResponseType(typeof(TheoryTrainingSessionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTheorySession([FromRoute] int moduleId, CancellationToken token = default)
    {
        var user = await CurrentUser();
        var (module, plan, error) = await GetVisibleTheoryPlan(user, moduleId, token);
        if (error is not null)
            return error;

        var session = await context.TheoryTrainingSessions
            .Include(s => s.Questions)
            .Where(s => s.ModuleId == module!.Id && s.PlanId == plan!.Id && s.UserId == user.Id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(s => s.Status == TheoryTrainingSessionStatus.Draft, token)
            ?? await context.TheoryTrainingSessions
                .Include(s => s.Questions)
                .Where(s => s.ModuleId == module!.Id && s.PlanId == plan!.Id && s.UserId == user.Id)
                .OrderByDescending(s => s.Score)
                .ThenByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(token);

        if (session is null)
        {
            session = await CreateTheorySession(user, module!, plan!, token);
            if (session.Questions.Count == 0)
                return BadRequest(new RequestResponse("理论培训题目不足，请联系老师检查组卷配置。"));
            await context.SaveChangesAsync(token);
        }

        return Ok(TheoryTrainingSessionModel.FromSession(session, plan!));
    }

    [HttpPost("theory/modules/{moduleId:int}/session/regenerate")]
    [ProducesResponseType(typeof(TheoryTrainingSessionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> RegenerateTheorySession([FromRoute] int moduleId, CancellationToken token = default)
    {
        var user = await CurrentUser();
        var (module, plan, error) = await GetVisibleTheoryPlan(user, moduleId, token);
        if (error is not null)
            return error;

        if (!plan!.AllowRetake && await context.TheoryTrainingSessions.AnyAsync(s =>
                s.ModuleId == module!.Id && s.PlanId == plan.Id && s.UserId == user.Id &&
                s.Status == TheoryTrainingSessionStatus.Submitted, token))
            return BadRequest(new RequestResponse("该理论培训不允许重复测验。"));

        var drafts = await context.TheoryTrainingSessions
            .Where(s => s.ModuleId == module!.Id && s.PlanId == plan.Id && s.UserId == user.Id &&
                        s.Status == TheoryTrainingSessionStatus.Draft)
            .ToArrayAsync(token);
        context.TheoryTrainingSessions.RemoveRange(drafts);

        var session = await CreateTheorySession(user, module!, plan, token);
        if (session.Questions.Count == 0)
            return BadRequest(new RequestResponse("理论培训题目不足，请联系老师检查组卷配置。"));
        await context.SaveChangesAsync(token);

        return Ok(TheoryTrainingSessionModel.FromSession(session, plan));
    }

    [HttpPost("theory/sessions/{sessionId:int}/submit")]
    [ProducesResponseType(typeof(TheoryTrainingSessionModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitTheorySession(
        [FromRoute] int sessionId,
        [FromBody] TheoryTrainingSessionSubmitModel model,
        CancellationToken token = default)
    {
        var user = await CurrentUser();
        var session = await context.TheoryTrainingSessions
            .Include(s => s.Plan)
            .Include(s => s.Module)
            .ThenInclude(m => m.Challenges)
            .Include(s => s.Questions)
            .SingleOrDefaultAsync(s => s.Id == sessionId && s.UserId == user.Id, token);
        if (session is null)
            return NotFound();

        if (session.Status == TheoryTrainingSessionStatus.Submitted)
            return BadRequest(new RequestResponse("试卷已经提交。"));

        var visible = await GetVisibleModule(user, session.ModuleId, token);
        if (visible is null)
            return Forbid();

        var incoming = model.Answers
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.Last().SelectedIndexes.Distinct().OrderBy(i => i).ToList());

        foreach (var question in session.Questions)
        {
            var selected = incoming.GetValueOrDefault(question.Id, []);
            if (selected.Any(i => i < 0 || i >= question.Options.Count))
                return BadRequest(new RequestResponse($"题目 {question.Id} 的选项超出范围。"));
            if (question.Type is TheoryQuestionType.SingleChoice or TheoryQuestionType.TrueFalse && selected.Count > 1)
                return BadRequest(new RequestResponse($"题目 {question.Id} 只能选择一个答案。"));

            question.SelectedIndexes = selected;
            question.IsCorrect = selected.SequenceEqual(question.AnswerIndexes.OrderBy(i => i));
        }

        session.CorrectCount = session.Questions.Count(q => q.IsCorrect == true);
        session.TotalCount = session.Questions.Count;
        session.Score = session.Questions.Where(q => q.IsCorrect == true).Sum(q => q.Score);
        session.MaxScore = session.Questions.Sum(q => q.IsCorrect == true ? q.Score : q.Score);
        session.Status = TheoryTrainingSessionStatus.Submitted;
        session.SubmittedAt = DateTimeOffset.UtcNow;

        var progress = await EnsureModuleProgress(user, visible, token);
        var rate = session.MaxScore == 0 ? 0 : (int)Math.Round(session.Score * 100.0 / session.MaxScore);
        progress.TheoryBestScore = Math.Max(progress.TheoryBestScore ?? 0, session.Score);
        progress.TheoryBestPassRate = Math.Max(progress.TheoryBestPassRate ?? 0, rate);
        await RecalculateProgress(user, visible, token);
        await context.SaveChangesAsync(token);

        logger.Log($"理论培训提交：{session.Module.Title}，得分 {session.Score}/{session.MaxScore}", user, TaskStatus.Success);

        return Ok(TheoryTrainingSessionModel.FromSession(session, session.Plan));
    }

    private async Task<(TrainingModule? Module, TheoryTrainingPlan? Plan, IActionResult? Error)> GetVisibleTheoryPlan(
        UserInfo user,
        int moduleId,
        CancellationToken token)
    {
        var module = await GetVisibleModuleWithTheory(user, moduleId, token);
        if (module is null || module.Type != TrainingType.Theory)
            return (null, null, NotFound());
        if (module.TheoryPlan is null || !module.TheoryPlan.IsPublished)
            return (module, null, BadRequest(new RequestResponse("该理论培训尚未发布测验。")));

        return (module, module.TheoryPlan, null);
    }

    private async Task<TheoryTrainingSession> CreateTheorySession(
        UserInfo user,
        TrainingModule module,
        TheoryTrainingPlan plan,
        CancellationToken token)
    {
        var bankQuestions = await ResolveTheoryQuestions(plan, token);
        var session = new TheoryTrainingSession
        {
            PlanId = plan.Id,
            ModuleId = module.Id,
            UserId = user.Id,
            Status = TheoryTrainingSessionStatus.Draft,
            MaxScore = bankQuestions.Sum(q => q.Score),
            TotalCount = bankQuestions.Count,
            CreatedAt = DateTimeOffset.UtcNow
        };

        for (var i = 0; i < bankQuestions.Count; i++)
        {
            var source = bankQuestions[i];
            session.Questions.Add(new TheoryTrainingSessionQuestion
            {
                SourceQuestionId = source.SourceQuestionId,
                Type = source.Type,
                Title = source.Title,
                Content = source.Content,
                Options = source.Options,
                AnswerIndexes = source.AnswerIndexes,
                Score = source.Score,
                Order = i + 1
            });
        }

        context.TheoryTrainingSessions.Add(session);

        var progress = await EnsureModuleProgress(user, module, token);
        progress.Status = TrainingModuleProgressStatus.Practicing;
        progress.UpdatedAt = DateTimeOffset.UtcNow;

        return session;
    }

    private async Task<List<TheoryTrainingQuestionSnapshot>> ResolveTheoryQuestions(
        TheoryTrainingPlan plan,
        CancellationToken token)
    {
        if (plan.Mode == TheoryTrainingMode.Manual)
            return plan.Questions
                .OrderBy(q => q.Order)
                .Select(q => TheoryTrainingQuestionSnapshot.FromBankItem(q.SourceQuestion, q.Score))
                .ToList();

        var query = context.TheoryQuestionBankItems.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(plan.BankName))
            query = query.Where(q => q.BankName == plan.BankName);
        if (plan.QuestionTypes is { Count: > 0 })
            query = query.Where(q => plan.QuestionTypes.Contains(q.Type));

        var source = await query.ToArrayAsync(token);
        var selected = source
            .OrderBy(_ => Guid.NewGuid())
            .Take(Math.Min(plan.QuestionCount, source.Length))
            .Select(q => TheoryTrainingQuestionSnapshot.FromBankItem(q, 1))
            .ToList();

        return selected;
    }

    private sealed record TheoryTrainingQuestionSnapshot(
        int? SourceQuestionId,
        TheoryQuestionType Type,
        string Title,
        string Content,
        List<string> Options,
        List<int> AnswerIndexes,
        int Score)
    {
        internal static TheoryTrainingQuestionSnapshot FromBankItem(TheoryQuestionBankItem item, int score) =>
            new(item.Id, item.Type, item.Title, item.Content, item.Options, item.AnswerIndexes, score);
    }
}
