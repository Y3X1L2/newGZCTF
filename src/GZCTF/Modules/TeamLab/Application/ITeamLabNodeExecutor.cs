using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.TeamLab.Contracts.Execution;

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

public sealed record TeamLabNodeDnsRecord(
    string Hostname,
    string IpAddress,
    string MacAddress,
    bool IsPrimary = true);

public sealed record TeamLabNodeRouteIntent(string TargetCidr, string GatewayIp, string SourceIp = "");

public sealed record TeamLabNodeForwardPolicy(
    string SourceCidr,
    string DestinationCidr,
    bool Allow);

public sealed record TeamLabNodeManagedSwitchIntent(
    TeamLabNodeNetworkIntent Network,
    string DhcpDnsServiceName,
    IReadOnlyList<TeamLabNodeDnsRecord> Records,
    IReadOnlyList<TeamLabNodeDnsRecord>? DnsRecords = null);

public sealed record TeamLabNodeManagedRouterFragmentIntent(
    string Key,
    IReadOnlyList<string> NetworkKeys);

public sealed record TeamLabNodeFabricIntent(
    string FabricIp,
    string HubAddressCidr,
    string NodeAddressCidr,
    string HostInterfaceName,
    string NamespaceInterfaceName,
    IReadOnlyList<TeamLabNodeRouteIntent> LocalRoutes,
    IReadOnlyList<TeamLabNodeRouteIntent> RemoteRoutes);

public sealed record TeamLabNodeObservationPointIntent(
    Guid PublicId,
    string TopologyKey,
    TeamLabObservationPointKind Kind,
    string InterfaceToken);

public sealed record TeamLabNodeInfrastructureApplyRequest(
    int RuntimeId,
    int Generation,
    int RouteVersion,
    string RouterNamespace,
    IReadOnlyList<TeamLabNodeManagedSwitchIntent> Switches,
    IReadOnlyList<TeamLabNodeManagedRouterFragmentIntent> Routers,
    TeamLabNodeFabricIntent Fabric,
    IReadOnlyList<TeamLabNodeForwardPolicy> ForwardPolicies,
    IReadOnlyList<TeamLabNodeObservationPointIntent> ObservationPoints);

public sealed record TeamLabNodeInfrastructureResourceFact(
    string Kind,
    string Key,
    string NativeIdentity,
    string Status);

public sealed record TeamLabNodeInfrastructureResult(
    bool Success,
    string Message,
    string? DesiredStateDigest,
    bool AlreadyApplied,
    IReadOnlyList<TeamLabNodeInfrastructureResourceFact> Resources)
{
    public static TeamLabNodeInfrastructureResult Applied(
        string digest,
        bool alreadyApplied = false,
        IReadOnlyList<TeamLabNodeInfrastructureResourceFact>? resources = null) =>
        new(true, alreadyApplied ? "Infrastructure already matches desired state." : "Infrastructure applied.",
            digest, alreadyApplied, resources ?? []);

    public static TeamLabNodeInfrastructureResult Failed(string message) => new(false, message, null, false, []);
}

public sealed record TeamLabNodeInventoryResource(
    string NativeId,
    string StableName,
    int Generation,
    string State,
    int? RuntimeId = null);

public sealed record TeamLabNodeRuntimeInventory(
    IReadOnlyList<TeamLabNodeInventoryResource> Containers,
    IReadOnlyList<TeamLabNodeInventoryResource> Vms,
    IReadOnlyList<TeamLabNodeInventoryResource> Infrastructure,
    DateTimeOffset ObservedAt);

public sealed record TeamLabNodeAssetCreateRequest(
    int RuntimeId,
    int RuntimeAssetId,
    Guid RuntimePublicId,
    int Generation,
    string AssetKey,
    string Name,
    TeamLabAssetKind Kind,
    int ImageTemplateId,
    int CpuUnits,
    int MemoryMiB,
    int StorageMiB,
    int? ExposePort,
    bool ImageReady,
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyList<TeamLabNodeInterfaceIntent> Interfaces,
    TeamLabNodeHealthIntent? Health = null,
    string? DependencyReadyToken = null,
    TeamLabEndpointObservationMode EndpointObservation = TeamLabEndpointObservationMode.Disabled,
    string RouterNamespace = "",
    Guid? OperationId = null,
    VmRuntimeMode? VmRuntimeMode = null,
    VmNetworkMode? VmNetworkMode = null,
    string? ImageReference = null);

public sealed record TeamLabNodeHealthIntent(
    TeamLabHealthCheckKind Kind,
    int? Port);

public sealed record TeamLabNodeAssetCreateResult(bool Success, string Message, string? RuntimeResourceId, string? NativeIdentity = null)
{
    public static TeamLabNodeAssetCreateResult Created(string resourceId, string? nativeIdentity = null) => new(true, "Created", resourceId, nativeIdentity);
    public static TeamLabNodeAssetCreateResult Failed(string message) => new(false, message, null);
}

public sealed record TeamLabScenarioArtifactCommitRequest(
    Guid OperationId,
    string VmName,
    OSType OsType,
    string BuildIdentity,
    string RegistryAddress,
    string RegistryRepository,
    string RegistryTag);

public sealed record TeamLabScenarioArtifactCommitResult(
    bool Success,
    string ArtifactDigest,
    long ArtifactSize,
    string EvidenceDigest,
    string RegistryAddress,
    string RegistryRepository,
    string RegistryTag,
    string? ErrorCode,
    string? ErrorDetail);

public sealed record TeamLabNodeHealthResult(
    bool Success,
    string Message,
    IReadOnlyList<string> PassedHealthChecks)
{
    public static TeamLabNodeHealthResult Completed(
        IReadOnlyList<string>? healthChecks = null) =>
        new(true, "Health check completed.", healthChecks ?? []);

    public static TeamLabNodeHealthResult Failed(string message) =>
        new(false, message, []);
}

public sealed record TeamLabNodeCleanupRequest(
    int RuntimeId,
    int Generation,
    string RouterNamespace,
    IReadOnlyList<string> ResourceNames,
    IReadOnlyList<string> ContainerIds,
    IReadOnlyList<string> VmNames,
    IReadOnlyList<string> SensorAssetKeys,
    IReadOnlyList<string> FabricRemoteCidrs);

public sealed record TeamLabNodeProbeRequest(
    int RuntimeId,
    string RouterNamespace,
    string TargetIp,
    TeamLabHealthCheckKind? Kind = null,
    int? Port = null);

public sealed record TeamLabNodeAccessApplyRequest(
    int RuntimeId,
    int Generation,
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

public sealed record TeamLabNodeAccessRemoveRequest(
    int RuntimeId,
    int Generation,
    string RouterNamespace,
    string InterfaceName);

public sealed record TeamLabNodeObservationRecord(
    long Sequence,
    Guid? ObservationPointId,
    string? AssetKey,
    DateTimeOffset CapturedAt,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    byte? TcpFlags,
    int PacketLength,
    string? PacketFingerprint,
    string FlowFingerprint,
    string EvidenceKind,
    string? ProcessIdentityHash,
    string Direction,
    DateTimeOffset? FirstSeenAt = null,
    DateTimeOffset? LastSeenAt = null,
    long Packets = 1,
    long? Bytes = null);

public sealed record TeamLabNodeObservationResult(
    bool Success,
    string Message,
    long NextSequence,
    long DroppedCount,
    IReadOnlyList<TeamLabNodeObservationRecord> Records,
    TeamLabNodeObservationHealth Health);

public sealed record TeamLabNodeObservationHealth(
    bool Running,
    int RegisteredPointCount,
    int ActiveInterfaceCount,
    int ActiveFlowCount,
    long DroppedCount,
    long ParserFailureCount,
    long SensorRejectedCount,
    long SpoolBytes,
    string? LastSensorErrorCode,
    string? LastError)
{
    public static readonly TeamLabNodeObservationHealth Unavailable =
        new(false, 0, 0, 0, 0, 0, 0, 0, null, "Observation health is unavailable.");
}

public sealed record TeamLabNodeCaptureStartRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    Guid ObservationPointId,
    string InterfaceToken,
    int MaxSeconds,
    long MaxBytes);

public sealed record TeamLabNodeCaptureUploadRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    string UploadPath,
    string UploadToken,
    long MaxBytes);

public sealed record TeamLabNodeCaptureResult(
    bool Success,
    string Message,
    Guid SegmentId,
    long CapturedBytes,
    bool Running,
    string? Sha256,
    bool Uploaded);

public interface ITeamLabNodeExecutor
{
    Task<TeamLabExecutionPlanApplyResponse> ApplyExecutionPlanAsync(
        Guid workerNodeId,
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken);
    Task<TeamLabExecutionPlanCleanupResponse> CleanupExecutionPlanAsync(
        Guid workerNodeId,
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken);

    Task<TeamLabNodeRuntimeInventory> GetRuntimeInventoryAsync(
        Guid workerNodeId,
        CancellationToken cancellationToken);

    Task<TeamLabNodeInfrastructureResult> ApplyInfrastructureAsync(
        Guid workerNodeId,
        TeamLabNodeInfrastructureApplyRequest request,
        CancellationToken cancellationToken);
    Task<TeamLabNodeAssetCreateResult> CreateAssetAsync(Guid workerNodeId, TeamLabNodeAssetCreateRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> WaitForAssetReadyAsync(
        Guid workerNodeId,
        string runtimeResourceId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken);
    Task<TeamLabNodeHealthResult> ProbeAssetHealthAsync(
        Guid workerNodeId,
        string runtimeResourceId,
        TeamLabNodeAssetCreateRequest request,
        CancellationToken cancellationToken);
    Task<TeamLabNodeResult> PauseAssetAsync(
        Guid workerNodeId,
        TeamLabAssetKind kind,
        string resourceId,
        int generation,
        CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ResumeAssetAsync(
        Guid workerNodeId,
        TeamLabAssetKind kind,
        string resourceId,
        int generation,
        CancellationToken cancellationToken);
    Task<TeamLabNodeResult> DestroyAssetAsync(Guid workerNodeId, TeamLabAssetKind kind, string resourceId, CancellationToken cancellationToken);
    Task<TeamLabScenarioArtifactCommitResult> CommitScenarioArtifactAsync(
        Guid workerNodeId,
        TeamLabScenarioArtifactCommitRequest request,
        CancellationToken cancellationToken);
    Task<TeamLabNodeResult> CleanupShardAsync(Guid workerNodeId, TeamLabNodeCleanupRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ProbeAsync(Guid workerNodeId, TeamLabNodeProbeRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> ConfigureAccessAsync(Guid workerNodeId, TeamLabNodeAccessApplyRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeResult> RemoveAccessAsync(Guid workerNodeId, TeamLabNodeAccessRemoveRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeObservationResult> ReadObservationsAsync(
        Guid workerNodeId,
        int runtimeId,
        int generation,
        long afterSequence,
        long acknowledgeThroughSequence,
        Guid? observationPointId,
        int limit,
        CancellationToken cancellationToken);
    Task<TeamLabNodeCaptureResult> StartCaptureAsync(Guid workerNodeId, TeamLabNodeCaptureStartRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeCaptureResult> StopCaptureAsync(Guid workerNodeId, int runtimeId, int generation, Guid captureId, Guid segmentId, CancellationToken cancellationToken);
    Task<TeamLabNodeCaptureResult> GetCaptureStatusAsync(Guid workerNodeId, int runtimeId, int generation, Guid captureId, Guid segmentId, CancellationToken cancellationToken);
    Task<TeamLabNodeCaptureResult> UploadCaptureAsync(Guid workerNodeId, TeamLabNodeCaptureUploadRequest request, CancellationToken cancellationToken);
    Task<TeamLabNodeCaptureResult> DeleteCaptureAsync(Guid workerNodeId, int runtimeId, int generation, Guid captureId, Guid segmentId, CancellationToken cancellationToken);
}
