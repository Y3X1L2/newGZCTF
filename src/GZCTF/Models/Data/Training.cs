using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

public sealed class TrainingChapterCompletionPolicy
{
    public bool RequireContentRead { get; set; } = true;

    public bool RequireAllRequiredChallenges { get; set; } = true;

    [Range(0, int.MaxValue)]
    public int RequiredChallengeCount { get; set; }

    [Range(0, 100)]
    public int TheoryPassRate { get; set; } = 80;
}

[Index(nameof(Status), nameof(UpdatedAt))]
[Index(nameof(CreatedById))]
public class TrainingCourse
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Summary { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [MaxLength(Limits.FileHashLength)]
    public string? CoverFileHash { get; set; }

    public List<string> Tags { get; set; } = [];

    public TrainingCourseStatus Status { get; set; } = TrainingCourseStatus.Draft;

    public TrainingCourseEnrollmentPolicy EnrollmentPolicy { get; set; } =
        TrainingCourseEnrollmentPolicy.TeacherApproval;

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public Guid? UpdatedById { get; set; }

    [JsonIgnore]
    public UserInfo? UpdatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public List<TrainingCourseTeacher> Teachers { get; set; } = [];

    public List<TrainingCourseEnrollment> Enrollments { get; set; } = [];

    public List<TrainingCourseChapter> Chapters { get; set; } = [];

    public List<TrainingCourseResource> Resources { get; set; } = [];

    public List<TrainingCourseChallenge> Challenges { get; set; } = [];

    public List<TrainingCourseTheoryQuestion> TheoryQuestions { get; set; } = [];

    public List<TrainingCourseChapterTheoryPaper> TheoryPapers { get; set; } = [];
}

[PrimaryKey(nameof(CourseId), nameof(TeacherId))]
[Index(nameof(TeacherId))]
public class TrainingCourseTeacher
{
    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

    public Guid TeacherId { get; set; }

    public UserInfo Teacher { get; set; } = null!;

    public TrainingCourseTeacherRole Role { get; set; } = TrainingCourseTeacherRole.Teacher;

    public Guid? AssignedById { get; set; }

    [JsonIgnore]
    public UserInfo? AssignedBy { get; set; }

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;
}

[PrimaryKey(nameof(CourseId), nameof(UserId))]
[Index(nameof(UserId), nameof(Status))]
public class TrainingCourseEnrollment
{
    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

    public Guid UserId { get; set; }

    public UserInfo User { get; set; } = null!;

    public TrainingCourseEnrollmentStatus Status { get; set; } = TrainingCourseEnrollmentStatus.Pending;

    [MaxLength(512)]
    public string ApplyReason { get; set; } = string.Empty;

    [MaxLength(512)]
    public string ReviewComment { get; set; } = string.Empty;

    public Guid? ReviewedById { get; set; }

    [JsonIgnore]
    public UserInfo? ReviewedBy { get; set; }

    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ReviewedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Index(nameof(CourseId), nameof(ParentId), nameof(Order))]
public class TrainingCourseChapter
{
    [Key]
    public int Id { get; set; }

    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

    public int? ParentId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapter? Parent { get; set; }

    public List<TrainingCourseChapter> Children { get; set; } = [];

    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Summary { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public TrainingArticleContentType ContentType { get; set; } = TrainingArticleContentType.Markdown;

    public TrainingChapterCompletionPolicy CompletionPolicy { get; set; } = new();

    public TrainingCourseVideoProvider VideoProvider { get; set; } = TrainingCourseVideoProvider.None;

    [MaxLength(1024)]
    public string? VideoUrl { get; set; }

    public int? VideoFileId { get; set; }

    public LocalFile? VideoFile { get; set; }

    public int Order { get; set; }

    public bool IsPublished { get; set; } = true;

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public Guid? UpdatedById { get; set; }

    [JsonIgnore]
    public UserInfo? UpdatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<TrainingCourseChapterChallenge> Challenges { get; set; } = [];

    public TrainingCourseChapterTheoryPaper? TheoryPaper { get; set; }
}

[Index(nameof(CourseId), nameof(Type), nameof(BankName))]
public class TrainingCourseTheoryQuestion
{
    [Key]
    public int Id { get; set; }

    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

    public TheoryQuestionType Type { get; set; }

    [Required]
    [MaxLength(128)]
    public string BankName { get; set; } = "Default";

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public List<int> AnswerIndexes { get; set; } = [];

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public Guid? UpdatedById { get; set; }

    [JsonIgnore]
    public UserInfo? UpdatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[Index(nameof(CourseId))]
[Index(nameof(ChapterId), IsUnique = true)]
public class TrainingCourseChapterTheoryPaper
{
    [Key]
    public int Id { get; set; }

    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

    public int ChapterId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapter Chapter { get; set; } = null!;

    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int PassRate { get; set; } = 60;

    public bool AllowRetake { get; set; } = true;

    public bool ShowCorrectAnswerAfterSubmit { get; set; } = true;

    public bool IsPublished { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public Guid? UpdatedById { get; set; }

    [JsonIgnore]
    public UserInfo? UpdatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int TotalScore => ActiveQuestions.Sum(q => q.Score);

    public List<TrainingCourseChapterTheoryQuestion> Questions { get; set; } = [];

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public IEnumerable<TrainingCourseChapterTheoryQuestion> ActiveQuestions =>
        Questions.Where(question => !question.IsArchived);
}

[Index(nameof(PaperId))]
[Index(nameof(SourceQuestionId))]
public class TrainingCourseChapterTheoryQuestion
{
    [Key]
    public int Id { get; set; }

    public int PaperId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapterTheoryPaper Paper { get; set; } = null!;

    public int? SourceQuestionId { get; set; }

    [JsonIgnore]
    public TrainingCourseTheoryQuestion? SourceQuestion { get; set; }

    public TheoryQuestionType Type { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public List<int> AnswerIndexes { get; set; } = [];

    public int Score { get; set; } = 1;

    public int Order { get; set; }

    public bool IsArchived { get; set; }
}

[Index(nameof(CourseId))]
[Index(nameof(ChapterId))]
[Index(nameof(UserId), nameof(ChapterId), nameof(AttemptNumber), IsUnique = true)]
public class TrainingCourseChapterTheorySheet
{
    [Key]
    public int Id { get; set; }

    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

    public int ChapterId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapter Chapter { get; set; } = null!;

    public int PaperId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapterTheoryPaper Paper { get; set; } = null!;

    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public int AttemptNumber { get; set; } = 1;

    public TheoryAnswerSheetStatus Status { get; set; } = TheoryAnswerSheetStatus.Draft;

    public int Score { get; set; }

    public int MaxScore { get; set; }

    public bool Passed { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SubmittedAt { get; set; }

    public List<TrainingCourseChapterTheoryAnswer> Answers { get; set; } = [];
}

[Index(nameof(SheetId))]
[Index(nameof(PaperQuestionId))]
public class TrainingCourseChapterTheoryAnswer
{
    [Key]
    public int Id { get; set; }

    public int SheetId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapterTheorySheet Sheet { get; set; } = null!;

    public int PaperQuestionId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapterTheoryQuestion PaperQuestion { get; set; } = null!;

    public List<int> SelectedIndexes { get; set; } = [];

    public bool? IsCorrect { get; set; }

    public int Score { get; set; }

    public TheoryQuestionType QuestionType { get; set; }

    [Required]
    public string QuestionTitle { get; set; } = string.Empty;

    public string QuestionContent { get; set; } = string.Empty;

    public List<string> QuestionOptions { get; set; } = [];

    public List<int> CorrectAnswerIndexes { get; set; } = [];

    public int MaxScore { get; set; }

    public int QuestionOrder { get; set; }

    public void CaptureQuestion(TrainingCourseChapterTheoryQuestion question)
    {
        QuestionType = question.Type;
        QuestionTitle = question.Title;
        QuestionContent = question.Content;
        QuestionOptions = [.. question.Options];
        CorrectAnswerIndexes = [.. question.AnswerIndexes];
        MaxScore = question.Score;
        QuestionOrder = question.Order;
    }
}

[Index(nameof(CourseId), nameof(Order))]
public class TrainingCourseResource
{
    [Key]
    public int Id { get; set; }

    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public TrainingCourseResourceType Type { get; set; } = TrainingCourseResourceType.File;

    [MaxLength(1024)]
    public string? ExternalUrl { get; set; }

    public int? LocalFileId { get; set; }

    public LocalFile? LocalFile { get; set; }

    public int Order { get; set; }

    public bool IsVisible { get; set; } = true;

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[PrimaryKey(nameof(CourseId), nameof(ExerciseChallengeId))]
[Index(nameof(ExerciseChallengeId))]
public class TrainingCourseChallenge
{
    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

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

[PrimaryKey(nameof(ChapterId), nameof(ExerciseChallengeId))]
public class TrainingCourseChapterChallenge
{
    public int ChapterId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapter Chapter { get; set; } = null!;

    public int CourseId { get; set; }

    public int ExerciseChallengeId { get; set; }

    [JsonIgnore]
    public TrainingCourseChallenge CourseChallenge { get; set; } = null!;

    public int Order { get; set; }
}

[Index(nameof(CourseId), nameof(ExerciseChallengeId), nameof(UserId))]
[Index(nameof(UserId), nameof(SubmittedAt))]
public class TrainingCourseSubmission
{
    [Key]
    public long Id { get; set; }

    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

    public int? ChapterId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapter? Chapter { get; set; }

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

public class TrainingCourseProgress
{
    public int CourseId { get; set; }

    [JsonIgnore]
    public TrainingCourse Course { get; set; } = null!;

    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public TrainingCourseProgressStatus Status { get; set; } = TrainingCourseProgressStatus.NotStarted;

    public int CompletedChapterCount { get; set; }

    public int TotalChapterCount { get; set; }

    public int ChallengeSolvedCount { get; set; }

    public int ChallengeTotalCount { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[PrimaryKey(nameof(UserId), nameof(CheckInDate))]
[Index(nameof(UserId), nameof(CheckedAt))]
public class TrainingCheckIn
{
    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public DateOnly CheckInDate { get; set; }

    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TrainingChapterProgress
{
    public int ChapterId { get; set; }

    [JsonIgnore]
    public TrainingCourseChapter Chapter { get; set; } = null!;

    public int CourseId { get; set; }

    public Guid UserId { get; set; }

    [JsonIgnore]
    public UserInfo User { get; set; } = null!;

    public TrainingCourseProgressStatus Status { get; set; } = TrainingCourseProgressStatus.NotStarted;

    public int ReadPercent { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
