using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record TeamLabNodeResult(bool Success, string Message)
{
    public static TeamLabNodeResult Ok(string message = "OK") => new(true, message);
    public static TeamLabNodeResult Failed(string message) => new(false, message);
}

public sealed record TeamLabNodeNetworkIntent(
    string Key,
    string Name,
    string Cidr,
    string GatewayIp,
    string BridgeName);

public sealed record TeamLabNodeInterfaceIntent(
    string Key,
    string NetworkKey,
    string BridgeName,
    string IpAddress,
    int PrefixLength,
    string MacAddress,
    bool Primary,
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> DnsServers);

public sealed record TeamLabNodeDnsRecord(string Hostname, string IpAddress, string MacAddress);

public sealed record TeamLabNodeShardApplyRequest(
    int RuntimeId,
    int Generation,
    string RouterNamespace,
    IReadOnlyList<TeamLabNodeNetworkIntent> Networks,
    IReadOnlyDictionary<string, IReadOnlyList<TeamLabNodeDnsRecord>> RecordsByNetwork);

public sealed record TeamLabNodeRouteIntent(string TargetCidr, string GatewayIp, string SourceIp = "");

public sealed record TeamLabNodeForwardPolicy(
    string SourceCidr,
    string DestinationCidr,
    bool Allow);

public sealed record TeamLabNodeRouteApplyRequest(
    int RuntimeId,
    int Generation,
    int RouteVersion,
    string FabricIp,
    string RouterNamespace,
    string NamespaceHostAddressCidr,
    string NamespacePeerAddressCidr,
    IReadOnlyList<TeamLabNodeRouteIntent> LocalRoutes,
    IReadOnlyList<TeamLabNodeRouteIntent> RemoteRoutes,
    IReadOnlyList<TeamLabNodeForwardPolicy> ForwardPolicies);

public sealed record TeamLabNodeAssetCreateRequest(
    int RuntimeId,
    int Generation,
    string AssetKey,
    string Name,
    TeamLabAssetKind Kind,
    int ImageTemplateId,
    int CpuUnits,
    int MemoryMiB,
    int StorageMiB,
    int? ExposePort,
    bool RoutingEnabled,
    bool ImageReady,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyList<TeamLabNodeInterfaceIntent> Interfaces);

public sealed record TeamLabNodeAssetCreateResult(bool Success, string Message, string? RuntimeResourceId)
{
    public static TeamLabNodeAssetCreateResult Created(string resourceId) => new(true, "Created", resourceId);
    public static TeamLabNodeAssetCreateResult Failed(string message) => new(false, message, null);
}

public sealed record TeamLabNodeCleanupRequest(
    int RuntimeId,
    int Generation,
    IReadOnlyList<string> ResourceNames,
    IReadOnlyList<string> ContainerIds,
    IReadOnlyList<string> VmNames);

public sealed record TeamLabNodeProbeRequest(
    int RuntimeId,
    string RouterNamespace,
    string TargetIp);

public sealed record TeamLabNodeAccessApplyRequest(
    int RuntimeId,
    string RouterNamespace,
    string InterfaceName,
    int ListenPort,
    string ServerAddressCidr,
    string ServerPrivateKey,
    string ClientPublicKey,
    string ClientAddress,
    string ClientAllowedIps,
    IReadOnlyList<string> PlayerAllowedCidrs,
    IReadOnlyList<string> PlayerBlockedCidrs);

public sealed record TeamLabNodeFlowSample(
    long Cursor,
    DateTimeOffset CapturedAt,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    long Bytes);

public sealed record TeamLabNodeFlowResult(
    bool Success,
    string Message,
    long NextCursor,
    IReadOnlyList<TeamLabNodeFlowSample> Samples);

public sealed record TeamLabNodeCaptureStartRequest(
    int RuntimeId,
    int JobId,
    string Scope,
    string InterfaceName,
    int MaxSeconds,
    long MaxBytes);

public sealed record TeamLabNodeCaptureResult(
    bool Success,
    string Message,
    string? FilePath,
    long CapturedBytes,
    bool Running);

public sealed record TeamLabNodeCaptureDownload(
    bool Success,
    string Message,
    Stream? Stream,
    string FileName,
    string ContentType,
    long? Length,
    IDisposable? Owner);

public interface ITeamLabNodeExecutor
{
    Task<TeamLabNodeResult> ApplyShardAsync(Guid workerNodeId, TeamLabNodeShardApplyRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ApplyRoutesAsync(Guid workerNodeId, TeamLabNodeRouteApplyRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeAssetCreateResult> CreateAssetAsync(Guid workerNodeId, TeamLabNodeAssetCreateRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> DestroyAssetAsync(Guid workerNodeId, TeamLabAssetKind kind, string resourceId, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> CleanupShardAsync(Guid workerNodeId, TeamLabNodeCleanupRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ProbeAsync(Guid workerNodeId, TeamLabNodeProbeRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ConfigureAccessAsync(Guid workerNodeId, TeamLabNodeAccessApplyRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> StartFlowAsync(Guid workerNodeId, int runtimeId, int shardId, int networkId, string networkKey, string interfaceName, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> StopFlowAsync(Guid workerNodeId, int runtimeId, string networkKey, CancellationToken cancellationToken);
    Task<TeamLabNodeFlowResult> GetFlowSnapshotAsync(
        Guid workerNodeId,
        int runtimeId,
        string networkKey,
        long afterCursor,
        CancellationToken cancellationToken);
    Task<TeamLabNodeCaptureResult> StartCaptureAsync(Guid workerNodeId, TeamLabNodeCaptureStartRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeCaptureResult> StopCaptureAsync(Guid workerNodeId, int runtimeId, int jobId, CancellationToken cancellationToken);
    Task<TeamLabNodeCaptureResult> GetCaptureStatusAsync(Guid workerNodeId, int runtimeId, int jobId, CancellationToken cancellationToken);
    Task<TeamLabNodeCaptureDownload> DownloadCaptureAsync(Guid workerNodeId, int runtimeId, int jobId, CancellationToken cancellationToken);
}
