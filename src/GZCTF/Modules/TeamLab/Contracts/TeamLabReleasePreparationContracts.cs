namespace GZCTF.Modules.TeamLab.Contracts;

/// <summary>Per-template preparation projection. No worker address or Agent detail is exposed.</summary>
public sealed record TeamLabReleaseImagePreparationModel(
    int TemplateId,
    string TemplateName,
    string ImageType,
    int EligibleNodeCount,
    int ReadyNodeCount,
    int PreparingNodeCount,
    int FailedNodeCount,
    OpenTeamLabFailureModel? Failure);

/// <summary>
/// Release admission and cache state. ReadyToStart allows missing cache to download
/// after node selection; onDemand distinguishes this from existing cached copies.
/// PlanAvailable describes eligible capabilities, not reserved runtime resources.
/// </summary>
public sealed record TeamLabReleasePreparationModel(
    Guid ReleaseId,
    string State,
    bool PlanAvailable,
    bool ReadyToStart,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<TeamLabReleaseImagePreparationModel> Images);
