using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(Type), nameof(Order))]
public class TrainingDirection
{
    [Key]
    public int Id { get; set; }

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

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<TrainingModule> Modules { get; set; } = [];
}

[Index(nameof(DirectionId), nameof(ParentId), nameof(Order))]
[Index(nameof(Type), nameof(IsPublished))]
public class TrainingModule
{
    [Key]
    public int Id { get; set; }

    public int DirectionId { get; set; }

    [JsonIgnore]
    public TrainingDirection Direction { get; set; } = null!;

    public int? ParentId { get; set; }

    [JsonIgnore]
    public TrainingModule? Parent { get; set; }

    public List<TrainingModule> Children { get; set; } = [];

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

    [MaxLength(Limits.FileHashLength)]
    public string? CoverFileHash { get; set; }

    public int? EnvironmentTemplateId { get; set; }

    public ImageTemplate? EnvironmentTemplate { get; set; }

    public TrainingCompletionRule CompletionRule { get; set; } = new();

    public bool IsPublished { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int Order { get; set; }

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public Guid? UpdatedById { get; set; }

    [JsonIgnore]
    public UserInfo? UpdatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<TrainingModuleVisibility> Visibilities { get; set; } = [];

    public List<TrainingModuleChallenge> Challenges { get; set; } = [];

    public TheoryTrainingPlan? TheoryPlan { get; set; }
}

public class TrainingCompletionRule
{
    public bool RequireArticleRead { get; set; } = true;

    public bool RequireAllRequiredChallenges { get; set; } = true;

    public int RequiredChallengeCount { get; set; }

    public int TheoryPassRate { get; set; } = 80;
}

[Index(nameof(ModuleId), nameof(VisibilityType), nameof(GroupId), IsUnique = true)]
[Index(nameof(GroupId))]
public class TrainingModuleVisibility
{
    [Key]
    public int Id { get; set; }

    public int ModuleId { get; set; }

    [JsonIgnore]
    public TrainingModule Module { get; set; } = null!;

    public int? GroupId { get; set; }

    [JsonIgnore]
    public StudentGroup? Group { get; set; }

    public TrainingVisibilityType VisibilityType { get; set; } = TrainingVisibilityType.GroupOnly;

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[PrimaryKey(nameof(ModuleId), nameof(ExerciseChallengeId))]
public class TrainingModuleChallenge
{
    public int ModuleId { get; set; }

    [JsonIgnore]
    public TrainingModule Module { get; set; } = null!;

    public int ExerciseChallengeId { get; set; }

    public ExerciseChallenge ExerciseChallenge { get; set; } = null!;

    public int Order { get; set; }

    public bool IsRequired { get; set; } = true;

    [MaxLength(128)]
    public string? DisplayTitle { get; set; }

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Index(nameof(ModuleId), nameof(ExerciseChallengeId), nameof(UserId))]
[Index(nameof(UserId), nameof(SubmittedAt))]
public class TrainingCtfSubmission
{
    [Key]
    public long Id { get; set; }

    public int ModuleId { get; set; }

    [JsonIgnore]
    public TrainingModule Module { get; set; } = null!;

    public int ExerciseChallengeId { get; set; }

    public ExerciseChallenge ExerciseChallenge { get; set; } = null!;

    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public AnswerResult Status { get; set; }

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(128)]
    public string SubmittedAnswerHash { get; set; } = string.Empty;

    public int? FlagId { get; set; }

    public FlagContext? Flag { get; set; }

    [MaxLength(64)]
    public string IpAddress { get; set; } = string.Empty;
}

[Index(nameof(ModuleId), IsUnique = true)]
public class TheoryTrainingPlan
{
    [Key]
    public int Id { get; set; }

    public int ModuleId { get; set; }

    [JsonIgnore]
    public TrainingModule Module { get; set; } = null!;

    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public TheoryTrainingMode Mode { get; set; } = TheoryTrainingMode.Random;

    public int QuestionCount { get; set; } = 30;

    [MaxLength(128)]
    public string? BankName { get; set; }

    public List<TheoryQuestionType>? QuestionTypes { get; set; } = [];

    public int PassRate { get; set; } = 80;

    public bool AllowRetake { get; set; } = true;

    public bool ShowCorrectAnswerAfterSubmit { get; set; } = true;

    public bool IsPublished { get; set; }

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public Guid? UpdatedById { get; set; }

    [JsonIgnore]
    public UserInfo? UpdatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<TheoryTrainingPlanQuestion> Questions { get; set; } = [];
}

[PrimaryKey(nameof(PlanId), nameof(SourceQuestionId))]
public class TheoryTrainingPlanQuestion
{
    public int PlanId { get; set; }

    [JsonIgnore]
    public TheoryTrainingPlan Plan { get; set; } = null!;

    public int SourceQuestionId { get; set; }

    public TheoryQuestionBankItem SourceQuestion { get; set; } = null!;

    public int Score { get; set; } = 1;

    public int Order { get; set; }
}

[Index(nameof(UserId), nameof(ModuleId))]
[Index(nameof(UserId), nameof(Status))]
public class TheoryTrainingSession
{
    [Key]
    public int Id { get; set; }

    public int PlanId { get; set; }

    [JsonIgnore]
    public TheoryTrainingPlan Plan { get; set; } = null!;

    public int ModuleId { get; set; }

    [JsonIgnore]
    public TrainingModule Module { get; set; } = null!;

    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public TheoryTrainingSessionStatus Status { get; set; } = TheoryTrainingSessionStatus.Draft;

    public int Score { get; set; }

    public int MaxScore { get; set; }

    public int CorrectCount { get; set; }

    public int TotalCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SubmittedAt { get; set; }

    public List<TheoryTrainingSessionQuestion> Questions { get; set; } = [];
}

[Index(nameof(SessionId))]
public class TheoryTrainingSessionQuestion
{
    [Key]
    public int Id { get; set; }

    public int SessionId { get; set; }

    [JsonIgnore]
    public TheoryTrainingSession Session { get; set; } = null!;

    public int? SourceQuestionId { get; set; }

    public TheoryQuestionType Type { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public List<int> AnswerIndexes { get; set; } = [];

    public List<int> SelectedIndexes { get; set; } = [];

    public bool? IsCorrect { get; set; }

    public int Score { get; set; } = 1;

    public int Order { get; set; }
}

[PrimaryKey(nameof(ModuleId), nameof(UserId))]
[Index(nameof(UserId), nameof(CompletedAt))]
public class TrainingArticleProgress
{
    public int ModuleId { get; set; }

    [JsonIgnore]
    public TrainingModule Module { get; set; } = null!;

    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public int ReadPercent { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset LastReadAt { get; set; } = DateTimeOffset.UtcNow;
}

[PrimaryKey(nameof(ModuleId), nameof(UserId))]
[Index(nameof(UserId), nameof(Status))]
[Index(nameof(UpdatedAt))]
public class TrainingModuleProgress
{
    public int ModuleId { get; set; }

    [JsonIgnore]
    public TrainingModule Module { get; set; } = null!;

    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public TrainingModuleProgressStatus Status { get; set; } = TrainingModuleProgressStatus.NotStarted;

    public int ChallengeSolvedCount { get; set; }

    public int ChallengeTotalCount { get; set; }

    public int? TheoryBestScore { get; set; }

    public int? TheoryBestPassRate { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
