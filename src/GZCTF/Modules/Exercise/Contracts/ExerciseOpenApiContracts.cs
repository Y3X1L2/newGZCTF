using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Text.Json.Serialization;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.AspNetCore.Mvc;

namespace GZCTF.Modules.Exercise.Contracts;

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
    public string? ContainerImage { get; set; }
    public int? MemoryLimit { get; set; }
    public int? StorageLimit { get; set; }
    public int? CPUCount { get; set; }
    public int? ExposePort { get; set; }
    public NetworkMode? NetworkMode { get; set; }
    public EnvironmentType Environment { get; set; }
    public int? ImageTemplateId { get; set; }
    public string? FlagTemplate { get; set; }
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