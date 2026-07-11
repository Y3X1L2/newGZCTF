using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabPlanNetworkModel(
    string Key,
    string Name,
    string CandidateCidr,
    bool IsEntry);

public sealed record TeamLabPlanInterfaceModel(
    string Key,
    string NetworkKey,
    int HostOffset,
    bool Primary);

public sealed record TeamLabPlanAssetModel(
    string Key,
    string Name,
    TeamLabAssetKind Kind,
    int ImageTemplateId,
    TeamLabAssetResourceModel Resources,
    IReadOnlyList<TeamLabPlanInterfaceModel> Interfaces,
    bool RoutingEnabled);

public sealed record TeamLabPlanShardModel(
    string Key,
    IReadOnlyList<string> NetworkKeys,
    IReadOnlyList<string> AssetKeys,
    int DockerSlots,
    int VmSlots);

public sealed record TeamLabPlanModel(
    Guid TopologyId,
    Guid ReleaseId,
    IReadOnlyList<TeamLabPlanNetworkModel> Networks,
    IReadOnlyList<TeamLabPlanAssetModel> Assets,
    IReadOnlyList<TeamLabPlanShardModel> Shards,
    int CrossShardConnections,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> Warnings,
    string PlanHash);
