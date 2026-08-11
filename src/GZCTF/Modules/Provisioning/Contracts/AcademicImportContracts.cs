using System.ComponentModel.DataAnnotations;
using GZCTF.Models;
using GZCTF.Models.Request.Game;
using GZCTF.Modules.Exercise.Contracts;

namespace GZCTF.Modules.Provisioning.Contracts;

public interface IExternalImportItemModel
{
    string ExternalId { get; set; }
}

public abstract class ExternalImportItemModel : IExternalImportItemModel
{
    [Required, MinLength(1), MaxLength(128)]
    public string ExternalId { get; set; } = string.Empty;
}

public sealed class TrainingCourseImportBatchModel
{
    [Required, MinLength(1), MaxLength(50)]
    public List<TrainingCourseImportModel> Items { get; set; } = [];
}

public sealed class TrainingCourseImportModel : ExternalImportItemModel
{
    [Required, MinLength(1), MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Summary { get; set; } = string.Empty;

    [MaxLength(1_000_000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(16)]
    public List<string> Tags { get; set; } = [];

    public TrainingCourseEnrollmentPolicy EnrollmentPolicy { get; set; } =
        TrainingCourseEnrollmentPolicy.TeacherApproval;

    public bool Publish { get; set; }

    [MaxLength(500)]
    public List<TrainingChapterImportModel> Chapters { get; set; } = [];

    [MaxLength(500)]
    public List<TrainingExerciseImportModel> Exercises { get; set; } = [];

    [MaxLength(1000)]
    public List<TrainingTheoryQuestionImportModel> TheoryQuestions { get; set; } = [];

    [MaxLength(500)]
    public List<TrainingTheoryPaperImportModel> TheoryPapers { get; set; } = [];
}

public sealed class TrainingChapterImportModel : ExternalImportItemModel
{
    [MaxLength(128)]
    public string? ParentExternalId { get; set; }

    [Required, MinLength(1), MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Summary { get; set; } = string.Empty;

    [MaxLength(1_000_000)]
    public string Content { get; set; } = string.Empty;

    public TrainingArticleContentType ContentType { get; set; } = TrainingArticleContentType.Markdown;

    public TrainingChapterCompletionPolicy CompletionPolicy { get; set; } = new();

    public TrainingCourseVideoProvider VideoProvider { get; set; } = TrainingCourseVideoProvider.None;

    [MaxLength(1024)]
    public string? VideoUrl { get; set; }

    public int Order { get; set; }
    public bool IsPublished { get; set; } = true;
}

public sealed class TrainingExerciseImportModel : IExternalImportItemModel
{
    [Required, MinLength(1), MaxLength(128)]
    public string ExternalId { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(1_000_000)]
    public string Content { get; set; } = string.Empty;

    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;
    public bool Credit { get; set; }
    public bool IsEnabled { get; set; } = true;

    [MaxLength(100)]
    public List<string>? Tags { get; set; }

    [MaxLength(512)]
    public List<string>? Hints { get; set; }

    [MaxLength(512)]
    public string? ContainerImage { get; set; }

    public int? MemoryLimit { get; set; }
    public int? StorageLimit { get; set; }
    public int? CPUCount { get; set; }
    public int? ExposePort { get; set; }
    public NetworkMode? NetworkMode { get; set; }
    public EnvironmentType Environment { get; set; }
    public int? ImageTemplateId { get; set; }

    [MaxLength(Limits.MaxFlagTemplateLength)]
    public string? FlagTemplate { get; set; }

    [MaxLength(100)]
    public List<ExerciseOpenApiFlagModel>? Flags { get; set; }

    public ExerciseOpenApiAttachmentModel? Attachment { get; set; }

    [MaxLength(128)]
    public string? ChapterExternalId { get; set; }

    public int Order { get; set; }
    public bool IsRequired { get; set; } = true;

    [MaxLength(128)]
    public string? DisplayTitle { get; set; }
}

public sealed class TrainingTheoryQuestionImportModel : TheoryQuestionEditModel, IExternalImportItemModel
{
    [Required, MinLength(1), MaxLength(128)]
    public string ExternalId { get; set; } = string.Empty;
}

public sealed class TrainingTheoryPaperImportModel : ExternalImportItemModel
{
    [Required, MinLength(1), MaxLength(128)]
    public string ChapterExternalId { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1_000_000)]
    public string Description { get; set; } = string.Empty;

    [Range(1, 100)]
    public int PassRate { get; set; } = 60;

    public bool AllowRetake { get; set; } = true;
    public bool ShowCorrectAnswerAfterSubmit { get; set; } = true;
    public bool Publish { get; set; }

    [MaxLength(500)]
    public List<TrainingTheoryPaperQuestionImportModel> Questions { get; set; } = [];
}

public sealed class TrainingTheoryPaperQuestionImportModel : TheoryQuestionEditModel
{
    [MaxLength(128)]
    public string? SourceQuestionExternalId { get; set; }

    [Range(1, int.MaxValue)]
    public int Score { get; set; } = 1;

    public int Order { get; set; }
}

public sealed class TheoryQuestionImportBatchModel
{
    [Required, MinLength(1), MaxLength(1000)]
    public List<TheoryQuestionImportModel> Items { get; set; } = [];
}

public sealed class TheoryQuestionImportModel : TheoryQuestionEditModel, IExternalImportItemModel
{
    [Required, MinLength(1), MaxLength(128)]
    public string ExternalId { get; set; } = string.Empty;
}

public sealed class TheoryPaperImportModel
{
    [Required, MinLength(1), MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1_000_000)]
    public string Description { get; set; } = string.Empty;

    public bool Publish { get; set; }

    [Required, MinLength(1), MaxLength(1000)]
    public List<TheoryPaperQuestionImportModel> Questions { get; set; } = [];
}

public sealed class TheoryPaperQuestionImportModel : TheoryQuestionEditModel
{
    public int? SourceQuestionId { get; set; }

    [Range(1, int.MaxValue)]
    public int Score { get; set; } = 1;

    public int Order { get; set; }
}

public sealed class TeamImportBatchModel
{
    [Required, MinLength(1), MaxLength(200)]
    public List<TeamImportModel> Items { get; set; } = [];
}

public sealed class TeamImportModel : ExternalImportItemModel
{
    [Required, MinLength(1), MaxLength(Limits.MaxTeamNameLength)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(Limits.MaxTeamBioLength)]
    public string? Bio { get; set; }

    public bool Locked { get; set; }

    [Required]
    public ExternalUserReferenceModel Captain { get; set; } = new();

    [MaxLength(100)]
    public List<ExternalUserReferenceModel> Members { get; set; } = [];
}

public sealed class ExternalUserReferenceModel
{
    public Guid? UserId { get; set; }

    [MaxLength(64)]
    public string? UserName { get; set; }
}

public sealed record AcademicImportResultItem(
    string ExternalId,
    string ResourceType,
    string ResourceId,
    string Action);

public sealed record AcademicImportResult(IReadOnlyList<AcademicImportResultItem> Items);
