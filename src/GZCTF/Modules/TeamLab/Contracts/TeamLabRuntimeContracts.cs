using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabRuntimeConstraintsModel(
    string? PreferredRegion,
    IReadOnlyList<string> RequiredCapabilities);

public sealed record TeamLabRuntimeOverlayModel(
    string AssetKey,
    IReadOnlyDictionary<string, string>? Environment,
    IReadOnlyDictionary<string, string>? Secrets);

public sealed record CreateTeamLabRuntimeModel(
    Guid ReleaseId,
    string? ExternalReference,
    TeamLabRuntimeConstraintsModel? Constraints,
    IReadOnlyList<TeamLabRuntimeOverlayModel>? Overlays);

public sealed record ResetTeamLabRuntimeModel(
    IReadOnlyList<TeamLabRuntimeOverlayModel>? Overlays,
    Guid? ReleaseId = null);

public sealed record TeamLabRuntimeShardProjectionModel(
    Guid Id,
    Guid WorkerNodeId,
    string WorkerNodeName,
    TeamLabRuntimeStatus Status,
    IReadOnlyList<string> NetworkKeys,
    IReadOnlyList<string> AssetKeys,
    string? Error);

public sealed record TeamLabRuntimeNetworkProjectionModel(
    string Key,
    string Name,
    string Cidr,
    string GatewayIp);

public sealed record TeamLabRuntimeAssetProjectionModel(
    string Key,
    string Name,
    TeamLabAssetKind Kind,
    string? RuntimeResourceId,
    string? PrimaryIp,
    TeamLabRuntimeStatus Status,
    string? Error);

public sealed record TeamLabRuntimeProjectionModel(
    Guid Id,
    Guid ReleaseId,
    int Generation,
    TeamLabRuntimeStatus Status,
    string Stage,
    bool OpenForAccess,
    IReadOnlyList<TeamLabRuntimeShardProjectionModel> Shards,
    IReadOnlyList<TeamLabRuntimeNetworkProjectionModel> Networks,
    IReadOnlyList<TeamLabRuntimeAssetProjectionModel> Assets,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? Error);

public sealed record TeamLabRuntimeEventModel(
    long Cursor,
    int Generation,
    string Stage,
    TeamLabEventLevel Level,
    string Message,
    string? ObjectType,
    string? ObjectId,
    DateTimeOffset CreatedAt);

public sealed record TeamLabAccessGrantCreateModel(string Type = "WireGuard");

public sealed record TeamLabAccessGrantModel(
    Guid Id,
    string Type,
    string ClientAddress,
    string Endpoint,
    string AllowedIps,
    string Dns,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? ConfigurationDownloadUrl);

public sealed record TeamLabRuntimeCreateResult(int RuntimeId, Guid RuntimePublicId, bool Reused);
