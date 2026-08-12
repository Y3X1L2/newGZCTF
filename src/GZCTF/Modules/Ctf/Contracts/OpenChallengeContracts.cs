using System.ComponentModel.DataAnnotations;
using GZCTF.Models;

namespace GZCTF.Modules.Ctf.Contracts;

public sealed class OpenChallengeAttachmentModel
{
    [Required, MaxLength(2048)]
    public string RemoteUrl { get; set; } = string.Empty;
}

public sealed class OpenChallengeFlagModel
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

    public OpenChallengeAttachmentModel? Attachment { get; set; }
}

public sealed record OpenChallengeAttachmentInfoModel(FileType Type, string Url);

public sealed record OpenChallengeFlagInfoModel(
    int Id,
    string Flag,
    int OrderIndex,
    string? Description,
    FlagScoreMode ScoreMode,
    int FixedScore,
    int MaxAttempts,
    string? AttachmentHash,
    AnswerType AnswerType,
    string? CustomName,
    OpenChallengeAttachmentInfoModel? Attachment);

public sealed class OpenChallengeImportModel
{
    [Required, MaxLength(128)]
    public string ExternalId { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(1_000_000)]
    public string Content { get; set; } = string.Empty;

    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;

    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;

    [MaxLength(100)]
    public List<string>? Hints { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset? DeadlineUtc { get; set; }

    [Range(0, 10_000)]
    public int SubmissionLimit { get; set; }

    [Range(1, 1_000_000)]
    public int OriginalScore { get; set; } = 500;

    [Range(0, 1)]
    public double MinScoreRate { get; set; } = 0.25;

    [Range(0.01, 1_000_000)]
    public double Difficulty { get; set; } = 5;

    public bool DisableBloodBonus { get; set; }

    [MaxLength(Limits.MaxFlagTemplateLength)]
    public string? FlagTemplate { get; set; }

    public EnvironmentType? Environment { get; set; }

    [MaxLength(512)]
    public string? ContainerImage { get; set; }

    [Range(1, 65535)]
    public int? ExposePort { get; set; }

    public int? ImageTemplateId { get; set; }

    [Range(1, 1024)]
    public int CPUCount { get; set; } = 1;

    [Range(32, 1_048_576)]
    public int MemoryLimit { get; set; } = 64;

    [Range(0, 1_048_576)]
    public int StorageLimit { get; set; } = 256;

    public NetworkMode NetworkMode { get; set; } = NetworkMode.Open;

    public bool EnableTrafficCapture { get; set; }

    [MaxLength(256)]
    public string? FileName { get; set; }

    [Required, MaxLength(100)]
    public List<OpenChallengeFlagModel> Flags { get; set; } = [];

    public OpenChallengeAttachmentModel? Attachment { get; set; }
}

public sealed class OpenChallengeBatchImportModel
{
    [Required, MinLength(1), MaxLength(100)]
    public List<OpenChallengeImportModel> Items { get; set; } = [];
}

public sealed class OpenChallengeBatchDeleteModel
{
    [Required, MinLength(1), MaxLength(100)]
    public List<int> ChallengeIds { get; set; } = [];
}

public sealed record OpenChallengeImportResultItem(string ExternalId, int ChallengeId);

public sealed record OpenChallengeMutationResult(
    int GameId,
    IReadOnlyList<OpenChallengeImportResultItem> Imported,
    IReadOnlyList<int> Deleted,
    IReadOnlyList<int> Missing,
    IReadOnlyList<OpenAwdpServiceImportResultItem>? AwdpImported = null,
    IReadOnlyList<int>? AwdpDeleted = null);

public sealed record OpenChallengeModel(
    int Id,
    string Title,
    string Content,
    ChallengeCategory Category,
    ChallengeType Type,
    IReadOnlyList<string> Hints,
    bool IsEnabled,
    DateTimeOffset? DeadlineUtc,
    int SubmissionLimit,
    int OriginalScore,
    double MinScoreRate,
    double Difficulty,
    bool DisableBloodBonus,
    string? FlagTemplate,
    EnvironmentType Environment,
    string? ContainerImage,
    int? ExposePort,
    int? ImageTemplateId,
    int CPUCount,
    int MemoryLimit,
    int StorageLimit,
    NetworkMode NetworkMode,
    bool EnableTrafficCapture,
    string? FileName,
    IReadOnlyList<OpenChallengeFlagInfoModel> Flags,
    OpenChallengeAttachmentInfoModel? Attachment);

public sealed record OpenChallengeSummaryModel(
    int Id,
    string Title,
    ChallengeCategory Category,
    ChallengeType Type,
    bool IsEnabled,
    DateTimeOffset? DeadlineUtc,
    int OriginalScore,
    EnvironmentType Environment,
    int? ImageTemplateId);

public sealed record OpenChallengePageModel(
    IReadOnlyList<OpenChallengeSummaryModel> Items,
    string? NextCursor);

public sealed class OpenAwdpServiceImportModel
{
    [Required, MaxLength(128)] public string ExternalId { get; set; } = string.Empty;
    [Required, MaxLength(Limits.MaxServiceNameLength)] public string Name { get; set; } = string.Empty;
    [MaxLength(1_000_000)] public string Content { get; set; } = string.Empty;
    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;
    public Difficulty Difficulty { get; set; } = Difficulty.Normal;
    [MaxLength(100)] public List<string>? Tags { get; set; } = [];
    [Required, MaxLength(Limits.MaxFlagTemplateLength)] public string FlagTemplate { get; set; } = "flag{[GUID]}";
    [Required, MaxLength(Limits.MaxImageNameLength)] public string ImageName { get; set; } = string.Empty;
    [Range(1, 65535)] public int ExposePort { get; set; } = 80;
    [MaxLength(Limits.MaxScriptLength)] public string? CheckerScript { get; set; }
    [MaxLength(Limits.MaxEntrypointLength)] public string? CheckerEntrypoint { get; set; } = "python3 checker.py";
    [MaxLength(Limits.MaxScriptLength)] public string? ExpScript { get; set; }
    [MaxLength(Limits.MaxEntrypointLength)] public string? ExpEntrypoint { get; set; } = "python3 exp.py";
    [Range(0, 1_000_000)] public int OriginalScore { get; set; } = 1000;
    [Range(0, 1_000_000)] public int AttackPoints { get; set; } = 50;
    [Range(0, 1_000_000)] public int SlaPoints { get; set; } = 20;
    [Range(0, 1_000_000)] public int PatchPoints { get; set; } = 100;
    [Range(0, 1_000_000)] public int ServiceAbnormalPenalty { get; set; } = 200;
    [Range(1, 100_000)] public int MaxAttackPerRound { get; set; } = 3;
    [Range(1, 100_000)] public int AttackPhaseMinutes { get; set; } = 15;
    [Range(1, 100_000)] public int PatchPhaseMinutes { get; set; } = 10;
    [Range(1, 100_000)] public int TotalRounds { get; set; } = 20;
    [Range(0, 100_000)] public int MaxResetCount { get; set; } = 10;
    [Range(0, 100_000)] public int MaxRecoveryCount { get; set; } = 5;
}

public sealed class OpenAwdpServiceBatchImportModel
{
    [Required, MinLength(1), MaxLength(100)]
    public List<OpenAwdpServiceImportModel> Items { get; set; } = [];
}

public sealed record OpenAwdpServiceImportResultItem(string ExternalId, int ServiceId);

public sealed record OpenAwdpServiceModel(
    int Id,
    string Name,
    string Content,
    ChallengeCategory Category,
    Difficulty Difficulty,
    IReadOnlyList<string> Tags,
    string FlagTemplate,
    string ImageName,
    int ExposePort,
    string? CheckerScript,
    string? CheckerEntrypoint,
    string? ExpScript,
    string? ExpEntrypoint,
    int OriginalScore,
    int AttackPoints,
    int SlaPoints,
    int PatchPoints,
    int ServiceAbnormalPenalty,
    int MaxAttackPerRound,
    int AttackPhaseMinutes,
    int PatchPhaseMinutes,
    int TotalRounds,
    int MaxResetCount,
    int MaxRecoveryCount);

public sealed record OpenAwdpServicePageModel(
    IReadOnlyList<OpenAwdpServiceModel> Items,
    string? NextCursor);
