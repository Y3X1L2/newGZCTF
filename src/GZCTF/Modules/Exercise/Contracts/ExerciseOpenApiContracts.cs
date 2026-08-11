using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json.Serialization;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Exercise.Contracts;

public sealed class ExerciseOpenApiFlagModel
{
    [Required, MaxLength(Limits.MaxFlagLength)]
    public string Flag { get; set; } = string.Empty;

    [Range(0, 10_000)]
    public int OrderIndex { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    public FlagScoreMode ScoreMode { get; set; } = FlagScoreMode.InheritDecay;

    [Range(0, 1_000_000)]
    public int FixedScore { get; set; }

    [Range(0, 100_000)]
    public int MaxAttempts { get; set; }

    [MaxLength(128)]
    public string? AttachmentHash { get; set; }

    public AnswerType AnswerType { get; set; } = AnswerType.Flag;

    [MaxLength(64)]
    public string? CustomName { get; set; }

    public ExerciseOpenApiAttachmentModel? Attachment { get; set; }
}

public sealed class ExerciseOpenApiFlagInfoModel
{
    public int Id { get; set; }
    public string Flag { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public string? Description { get; set; }
    public FlagScoreMode ScoreMode { get; set; }
    public int FixedScore { get; set; }
    public int MaxAttempts { get; set; }
    public string? AttachmentHash { get; set; }
    public AnswerType AnswerType { get; set; }
    public string? CustomName { get; set; }
    public ExerciseOpenApiAttachmentModel? Attachment { get; set; }
}

public sealed class ExerciseOpenApiAttachmentModel
{
    [Required, MaxLength(2048)]
    public string RemoteUrl { get; set; } = string.Empty;
}

public sealed class ExerciseExternalModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public ChallengeCategory Category { get; set; }
    public ChallengeType Type { get; set; }
    public Difficulty Difficulty { get; set; }
    public bool Credit { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public IReadOnlyList<string> Hints { get; set; } = [];
    public bool IsEnabled { get; set; }
    public ExercisePoolSource PoolSource { get; set; }
    public string? ContainerImage { get; set; }
    public int? MemoryLimit { get; set; }
    public int? StorageLimit { get; set; }
    public int? CPUCount { get; set; }
    public int? ExposePort { get; set; }
    public NetworkMode? NetworkMode { get; set; }
    public EnvironmentType Environment { get; set; }
    public int? ImageTemplateId { get; set; }
    public string? FlagTemplate { get; set; }
    public ExerciseOpenApiAttachmentModel? Attachment { get; set; }
    public IReadOnlyList<ExerciseOpenApiFlagInfoModel> Flags { get; set; } = [];
}

public sealed class ExerciseExternalSummaryModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public ChallengeCategory Category { get; set; }
    public ChallengeType Type { get; set; }
    public Difficulty Difficulty { get; set; }
    public bool Credit { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
    public bool IsEnabled { get; set; }
    public ExercisePoolSource PoolSource { get; set; }
}

public sealed class ExerciseExternalPageModel
{
    public IReadOnlyList<ExerciseExternalSummaryModel> Items { get; set; } = [];
    public string? NextCursor { get; set; }
}

public sealed class ExerciseCreateModel
{
    [Required, MinLength(1), MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [Required]
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

    public string? ContainerImage { get; set; }
    public int? MemoryLimit { get; set; }
    public int? StorageLimit { get; set; }
    public int? CPUCount { get; set; }
    public int? ExposePort { get; set; }
    public NetworkMode? NetworkMode { get; set; }
    public EnvironmentType Environment { get; set; }
    public int? ImageTemplateId { get; set; }
    public string? FlagTemplate { get; set; }

    [MaxLength(100)]
    public List<ExerciseOpenApiFlagModel>? Flags { get; set; }

    public ExerciseOpenApiAttachmentModel? Attachment { get; set; }
}

public sealed class ExerciseFlagSubmissionModel
{
    [Required, MinLength(1)]
    public string Flag { get; set; } = string.Empty;

    public int FlagId { get; set; }
}

public sealed record ExerciseFlagResult(bool Accepted, string Message);

public sealed class ExerciseImportFromExternalModel
{
    [Required, MinLength(1), MaxLength(100)]
    public List<ExerciseImportItemModel> Items { get; set; } = [];
}

public sealed class ExerciseImportItemModel
{
    [Required, MaxLength(128)]
    public string ExternalId { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(1_000_000)]
    public string Content { get; set; } = string.Empty;

    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;

    [MaxLength(100)]
    public List<string>? Tags { get; set; }

    [MaxLength(512)]
    public List<string>? Hints { get; set; }

    public bool IsEnabled { get; set; } = true;
    public bool Credit { get; set; }

    [MaxLength(100)]
    public List<ExerciseOpenApiFlagModel>? Flags { get; set; }

    public ExerciseOpenApiAttachmentModel? Attachment { get; set; }
}

public sealed class ExerciseImportResultItem
{
    public string ExternalId { get; set; } = string.Empty;
    public int ExerciseId { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed record ExerciseImportResult(
    IReadOnlyList<ExerciseImportResultItem> Imported,
    IReadOnlyList<string> Missing);

public sealed record ExerciseMutationResult(
    IReadOnlyList<ExerciseImportResultItem> Imported,
    IReadOnlyList<int> Updated,
    IReadOnlyList<int> Deleted);
