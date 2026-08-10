using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Contracts;

/// <summary>Catalog parameter schema entry. Secret parameters never carry a default or sample value.</summary>
public sealed record TeamLabServiceProfileParameterModel(
    string Key,
    string Type,
    bool Required,
    bool Secret,
    string? DefaultValue,
    string? SampleValue = null);

/// <summary>Deterministic summary of what the profile will execute at deploy time.</summary>
public sealed record TeamLabServiceProfileExecutionModel(
    int Steps,
    int HealthChecks,
    int MaxReboots,
    string Phase);

public sealed record TeamLabServiceProfileSummaryModel(
    Guid Id,
    int Version,
    IReadOnlyList<int> AvailableVersions,
    string Name,
    string? Description,
    IReadOnlyList<TeamLabAssetKind> AssetKinds,
    DateTimeOffset UpdatedAt,
    string? DisplayNameZh = null,
    string? DisplayNameEn = null,
    string? Purpose = null,
    string Status = "published");

public sealed record TeamLabServiceProfileDetailModel(
    Guid Id,
    int Version,
    IReadOnlyList<int> AvailableVersions,
    string Name,
    string? Description,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<TeamLabAssetKind> AssetKinds,
    IReadOnlyList<TeamLabServiceProfileParameterModel> Parameters,
    TeamLabServiceProfileExecutionModel Execution,
    string Status,
    string? DocumentationUrl,
    string SecretSupply,
    DateTimeOffset PublishedAt,
    string? DisplayNameZh = null,
    string? DisplayNameEn = null,
    string? Purpose = null,
    DateTimeOffset? RetiredAt = null);

public sealed record TeamLabServiceProfilePageModel(
    IReadOnlyList<TeamLabServiceProfileSummaryModel> Items,
    string? NextCursor);

/// <summary>Per-template preparation projection. No worker address or Agent detail is exposed.</summary>
public sealed record TeamLabReleaseImagePreparationModel(
    int TemplateId,
    string TemplateName,
    string ImageType,
    string Digest,
    int EligibleNodeCount,
    int ReadyNodeCount,
    int PreparingNodeCount,
    int FailedNodeCount,
    OpenTeamLabFailureModel? Failure);

/// <summary>Release-level preparation state for external callers.</summary>
public sealed record TeamLabReleasePreparationModel(
    Guid ReleaseId,
    string State,
    bool PlanAvailable,
    bool ReadyToStart,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<TeamLabReleaseImagePreparationModel> Images);
