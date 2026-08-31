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

/// <summary>Release-level preparation state for external callers.</summary>
public sealed record TeamLabReleasePreparationModel(
    Guid ReleaseId,
    string State,
    bool PlanAvailable,
    bool ReadyToStart,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<TeamLabReleaseImagePreparationModel> Images);
