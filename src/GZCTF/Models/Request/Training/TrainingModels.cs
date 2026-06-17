using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Request.Shared;

namespace GZCTF.Models.Request.Training;

public class TrainingDirectionEditModel
{
    public TrainingType Type { get; set; }

    [Required]
    [MaxLength(64)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Icon { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Color { get; set; } = "#6beeb1";

    public int Order { get; set; }

    public bool IsEnabled { get; set; } = true;
}

public class TrainingModuleEditModel
{
    public int DirectionId { get; set; }

    public int? ParentId { get; set; }

    public TrainingType Type { get; set; }

    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Summary { get; set; } = string.Empty;

    public string ArticleContent { get; set; } = string.Empty;

    public TrainingArticleContentType ArticleContentType { get; set; } = TrainingArticleContentType.Markdown;

    public int? EnvironmentTemplateId { get; set; }

    public TrainingCompletionRule CompletionRule { get; set; } = new();

    public int Order { get; set; }
}

public class TrainingModuleVisibilityEditModel
{
    public TrainingVisibilityType VisibilityType { get; set; }

    public int? GroupId { get; set; }
}

public class TrainingModuleChallengeEditModel
{
    public int ExerciseChallengeId { get; set; }

    public int Order { get; set; }

    public bool IsRequired { get; set; } = true;

    [MaxLength(128)]
    public string? DisplayTitle { get; set; }
}

public class TrainingDirectionModel
{
    public int Id { get; set; }

    public TrainingType Type { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public int Order { get; set; }

    public bool IsEnabled { get; set; }

    public List<TrainingModuleModel> Modules { get; set; } = [];

    public static TrainingDirectionModel FromDirection(TrainingDirection direction, IEnumerable<TrainingModuleModel>? modules = null) =>
        new()
        {
            Id = direction.Id,
            Type = direction.Type,
            Key = direction.Key,
            Title = direction.Title,
            Description = direction.Description,
            Icon = direction.Icon,
            Color = direction.Color,
            Order = direction.Order,
            IsEnabled = direction.IsEnabled,
            Modules = modules?.ToList() ?? []
        };
}

public class TrainingModuleModel
{
    public int Id { get; set; }

    public int DirectionId { get; set; }

    public int? ParentId { get; set; }

    public TrainingType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string ArticleContent { get; set; } = string.Empty;

    public TrainingArticleContentType ArticleContentType { get; set; }

    public int? EnvironmentTemplateId { get; set; }

    public string? EnvironmentTemplateName { get; set; }

    public TrainingCompletionRule CompletionRule { get; set; } = new();

    public bool IsPublished { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int Order { get; set; }

    public List<TrainingModuleVisibilityModel> Visibilities { get; set; } = [];

    public List<TrainingModuleChallengeModel> Challenges { get; set; } = [];

    public TrainingModuleProgressStatus? ProgressStatus { get; set; }

    public int ChallengeSolvedCount { get; set; }

    public int ChallengeTotalCount { get; set; }

    public static TrainingModuleModel FromModule(TrainingModule module, TrainingModuleProgress? progress = null) =>
        new()
        {
            Id = module.Id,
            DirectionId = module.DirectionId,
            ParentId = module.ParentId,
            Type = module.Type,
            Title = module.Title,
            Slug = module.Slug,
            Summary = module.Summary,
            ArticleContent = module.ArticleContent,
            ArticleContentType = module.ArticleContentType,
            EnvironmentTemplateId = module.EnvironmentTemplateId,
            EnvironmentTemplateName = module.EnvironmentTemplate?.Name,
            CompletionRule = module.CompletionRule,
            IsPublished = module.IsPublished,
            PublishedAt = module.PublishedAt,
            Order = module.Order,
            Visibilities = module.Visibilities.Select(TrainingModuleVisibilityModel.FromVisibility).ToList(),
            Challenges = module.Challenges.OrderBy(c => c.Order).Select(TrainingModuleChallengeModel.FromChallenge).ToList(),
            ProgressStatus = progress?.Status,
            ChallengeSolvedCount = progress?.ChallengeSolvedCount ?? 0,
            ChallengeTotalCount = progress?.ChallengeTotalCount ?? module.Challenges.Count
        };
}

public class TrainingModuleVisibilityModel
{
    public int Id { get; set; }

    public TrainingVisibilityType VisibilityType { get; set; }

    public int? GroupId { get; set; }

    public string? GroupName { get; set; }

    public static TrainingModuleVisibilityModel FromVisibility(TrainingModuleVisibility visibility) =>
        new()
        {
            Id = visibility.Id,
            VisibilityType = visibility.VisibilityType,
            GroupId = visibility.GroupId,
            GroupName = visibility.Group?.Name
        };
}

public class TrainingModuleChallengeModel
{
    public int ExerciseChallengeId { get; set; }

    public string Title { get; set; } = string.Empty;

    public ChallengeCategory Category { get; set; }

    public ChallengeType Type { get; set; }

    public EnvironmentType Environment { get; set; }

    public int Order { get; set; }

    public bool IsRequired { get; set; }

    public string? DisplayTitle { get; set; }

    public static TrainingModuleChallengeModel FromChallenge(TrainingModuleChallenge challenge) =>
        new()
        {
            ExerciseChallengeId = challenge.ExerciseChallengeId,
            Title = challenge.ExerciseChallenge.Title,
            Category = challenge.ExerciseChallenge.Category,
            Type = challenge.ExerciseChallenge.Type,
            Environment = challenge.ExerciseChallenge.Environment,
            Order = challenge.Order,
            IsRequired = challenge.IsRequired,
            DisplayTitle = challenge.DisplayTitle
        };
}

public class TrainingOverviewModel
{
    public int TotalModules { get; set; }

    public int CompletedModules { get; set; }

    public int CtfSolvedChallenges { get; set; }

    public int CtfTotalChallenges { get; set; }

    public int TheoryCompletedModules { get; set; }

    public int TheoryTotalModules { get; set; }

    public double CompletionRate => TotalModules == 0 ? 0 : Math.Round(CompletedModules * 100.0 / TotalModules, 2);
}

public class TrainingCtfChallengeDetailModel
{
    public int Id { get; set; }

    public int ModuleId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public ChallengeCategory Category { get; set; }

    public ChallengeType Type { get; set; }

    public EnvironmentType Environment { get; set; }

    public List<string>? Hints { get; set; }

    public Difficulty Difficulty { get; set; }

    public List<string>? Tags { get; set; } = [];

    public bool Solved { get; set; }

    public int Attempts { get; set; }

    public int Limit { get; set; }

    public List<FlagStepInfo>? Flags { get; set; }

    public ClientFlagContext Context { get; set; } = new();

    internal static TrainingCtfChallengeDetailModel FromInstance(
        int moduleId,
        ExerciseInstance instance,
        int attempts,
        bool solved) =>
        new()
        {
            Id = instance.ExerciseId,
            ModuleId = moduleId,
            Title = instance.Exercise.Title,
            Content = instance.Exercise.Content,
            Category = instance.Exercise.Category,
            Type = instance.Exercise.Type,
            Environment = instance.Exercise.Environment,
            Hints = instance.Exercise.Hints,
            Difficulty = instance.Exercise.Difficulty,
            Tags = instance.Exercise.Tags,
            Solved = solved,
            Attempts = attempts,
            Limit = instance.Exercise.SubmissionLimit,
            Flags = instance.Exercise.Flags is { Count: > 1 }
                ? instance.Exercise.Flags
                    .OrderBy(f => f.OrderIndex)
                    .Select(f => new FlagStepInfo
                    {
                        Id = f.Id,
                        OrderIndex = f.OrderIndex,
                        Description = f.Description
                    })
                    .ToList()
                : null,
            Context = new ClientFlagContext
            {
                InstanceEntry = instance.Container?.Entry,
                CloseTime = instance.Container?.ExpectStopAt,
                Url = instance.AttachmentUrl,
                FileSize = instance.Attachment?.FileSize
            }
        };
}

public class TrainingSubmitResultModel
{
    public long SubmissionId { get; set; }

    public AnswerResult Status { get; set; }

    public bool ModuleCompleted { get; set; }
}

public class TheoryTrainingPlanEditModel
{
    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public TheoryTrainingMode Mode { get; set; } = TheoryTrainingMode.Random;

    [Range(1, 500)]
    public int QuestionCount { get; set; } = 30;

    [MaxLength(128)]
    public string? BankName { get; set; }

    public List<TheoryQuestionType>? QuestionTypes { get; set; } = [];

    [Range(0, 100)]
    public int PassRate { get; set; } = 80;

    public bool AllowRetake { get; set; } = true;

    public bool ShowCorrectAnswerAfterSubmit { get; set; } = true;

    public bool IsPublished { get; set; }

    public List<TheoryTrainingPlanQuestionEditModel> Questions { get; set; } = [];
}

public class TheoryTrainingPlanQuestionEditModel
{
    public int SourceQuestionId { get; set; }

    [Range(1, int.MaxValue)]
    public int Score { get; set; } = 1;

    public int Order { get; set; }
}

public class TheoryTrainingPlanModel : TheoryTrainingPlanEditModel
{
    public int Id { get; set; }

    public int ModuleId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    internal static TheoryTrainingPlanModel FromPlan(TheoryTrainingPlan plan) =>
        new()
        {
            Id = plan.Id,
            ModuleId = plan.ModuleId,
            Title = plan.Title,
            Description = plan.Description,
            Mode = plan.Mode,
            QuestionCount = plan.QuestionCount,
            BankName = plan.BankName,
            QuestionTypes = plan.QuestionTypes,
            PassRate = plan.PassRate,
            AllowRetake = plan.AllowRetake,
            ShowCorrectAnswerAfterSubmit = plan.ShowCorrectAnswerAfterSubmit,
            IsPublished = plan.IsPublished,
            UpdatedAt = plan.UpdatedAt,
            Questions = plan.Questions
                .OrderBy(q => q.Order)
                .Select(q => new TheoryTrainingPlanQuestionEditModel
                {
                    SourceQuestionId = q.SourceQuestionId,
                    Score = q.Score,
                    Order = q.Order
                })
                .ToList()
        };
}

public class TheoryTrainingAnswerModel
{
    public int QuestionId { get; set; }

    public List<int> SelectedIndexes { get; set; } = [];
}

public class TheoryTrainingSessionSubmitModel
{
    public List<TheoryTrainingAnswerModel> Answers { get; set; } = [];
}

public class TheoryTrainingQuestionModel
{
    public int Id { get; set; }

    public TheoryQuestionType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public int Score { get; set; }

    public int Order { get; set; }

    public bool? IsCorrect { get; set; }

    public List<int> SelectedIndexes { get; set; } = [];

    public List<int>? AnswerIndexes { get; set; }

    internal static TheoryTrainingQuestionModel FromQuestion(
        TheoryTrainingSessionQuestion question,
        bool revealAnswer) =>
        new()
        {
            Id = question.Id,
            Type = question.Type,
            Title = question.Title,
            Content = question.Content,
            Options = question.Options,
            Score = question.Score,
            Order = question.Order,
            IsCorrect = question.IsCorrect,
            SelectedIndexes = question.SelectedIndexes,
            AnswerIndexes = revealAnswer ? question.AnswerIndexes : null
        };
}

public class TheoryTrainingSessionModel
{
    public int Id { get; set; }

    public int ModuleId { get; set; }

    public TheoryTrainingSessionStatus Status { get; set; }

    public int Score { get; set; }

    public int MaxScore { get; set; }

    public int CorrectCount { get; set; }

    public int TotalCount { get; set; }

    public int PassRate { get; set; }

    public int CorrectRate { get; set; }

    public bool Passed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public List<TheoryTrainingQuestionModel> Questions { get; set; } = [];

    public static TheoryTrainingSessionModel FromSession(TheoryTrainingSession session, TheoryTrainingPlan plan)
    {
        var reveal = session.Status == TheoryTrainingSessionStatus.Submitted && plan.ShowCorrectAnswerAfterSubmit;
        var rate = session.MaxScore == 0 ? 0 : (int)Math.Round(session.Score * 100.0 / session.MaxScore);

        return new()
        {
            Id = session.Id,
            ModuleId = session.ModuleId,
            Status = session.Status,
            Score = session.Score,
            MaxScore = session.MaxScore,
            CorrectCount = session.CorrectCount,
            TotalCount = session.TotalCount,
            PassRate = plan.PassRate,
            CorrectRate = rate,
            Passed = session.Status == TheoryTrainingSessionStatus.Submitted && rate >= plan.PassRate,
            CreatedAt = session.CreatedAt,
            SubmittedAt = session.SubmittedAt,
            Questions = session.Questions
                .OrderBy(q => q.Order)
                .Select(q => TheoryTrainingQuestionModel.FromQuestion(q, reveal))
                .ToList()
        };
    }
}

public class TrainingStudentProgressModel
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string? RealName { get; set; }

    public int TotalModules { get; set; }

    public int CompletedModules { get; set; }

    public int CtfSolvedChallenges { get; set; }

    public int CtfTotalChallenges { get; set; }

    public int TheoryCompletedModules { get; set; }

    public int TheoryTotalModules { get; set; }

    public DateTimeOffset? LastActivity { get; set; }
}

public class TrainingGroupStatsModel
{
    public int GroupId { get; set; }

    public string GroupName { get; set; } = string.Empty;

    public int StudentCount { get; set; }

    public int TotalModules { get; set; }

    public double AverageCompletionRate { get; set; }

    public List<TrainingStudentProgressModel> Students { get; set; } = [];
}

public class TrainingCheckInModel
{
    public DateOnly Date { get; set; }

    public DateTimeOffset CheckedAt { get; set; }

    public bool IsToday { get; set; }
}

public class TrainingActivityPointModel
{
    public DateOnly Date { get; set; }

    public int StudyActions { get; set; }

    public int CompletedChapters { get; set; }

    public int AcceptedChallenges { get; set; }

    public bool CheckedIn { get; set; }
}

public class TrainingPersonalOverviewModel
{
    public int VisibleCourseCount { get; set; }

    public int JoinedCourseCount { get; set; }

    public int CompletedCourseCount { get; set; }

    public int AverageProgress { get; set; }

    public int CompletedChapterCount { get; set; }

    public int TotalChapterCount { get; set; }

    public int CtfSolvedChallenges { get; set; }

    public int CtfTotalChallenges { get; set; }

    public int TheoryCompletedModules { get; set; }

    public int TheoryTotalModules { get; set; }

    public int CheckInDays { get; set; }

    public int CurrentCheckInStreak { get; set; }

    public bool CheckedInToday { get; set; }

    public List<TrainingCheckInModel> CheckIns { get; set; } = [];

    public List<TrainingActivityPointModel> Activity { get; set; } = [];
}
