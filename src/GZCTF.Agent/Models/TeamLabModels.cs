namespace GZCTF.Agent.Models;

public class AgentTeamLabConfig
{
    public bool Enable { get; set; } = true;
    public bool DryRun { get; set; }
    public string RuntimeStateRoot { get; set; } = "/var/lib/gzctf/teamlab";
    public string FabricInterfaceName { get; set; } = "gzctf-fabric";
    public int FabricMtu { get; set; } = 1420;
    public int ObservationSnapLength { get; set; } = 192;
    public int ObservationBatchSize { get; set; } = 500;
    public int ObservationMemoryRecordLimit { get; set; } = 20_000;
    public int ObservationMaxActiveFlows { get; set; } = 100_000;
    public long ObservationSpoolMaxBytes { get; set; } = 64L * 1024 * 1024;
    public int ObservationAggregationIntervalMilliseconds { get; set; } = 1_000;
    public bool ObservationPacketFingerprintEnabled { get; set; }
}

public record TeamLabToolCapabilityReport(
    bool Docker,
    bool Kvm,
    bool KvmDevice,
    bool CpuVirtualization,
    bool WireGuard,
    bool Iptables,
    bool Nftables,
    bool Tcpdump,
    bool Dumpcap,
    bool DnsProbe);

public record TeamLabStatusResponse(
    bool Available,
    bool Enable,
    bool DryRun,
    bool HasIpCommand,
    bool HasDockerCommand,
    bool HasKvmCommand,
    bool HasWireGuardCommand,
    bool HasIptablesCommand,
    bool HasNftCommand,
    bool HasTcpdumpCommand,
    bool HasDumpcapCommand,
    TeamLabToolCapabilityReport Capabilities,
    DateTimeOffset CheckedAt,
    string? Message = null,
    string? FabricInterfaceName = null,
    string? FabricIp = null,
    bool FabricReady = false);

public record TeamLabDryRunResponse(
    bool Success,
    bool DryRun,
    string Message,
    string[] Commands);

public record TeamLabManagedSwitchIntent(
    string Key,
    string Name,
    string Cidr,
    string GatewayIp,
    string BridgeName,
    string DhcpDnsServiceName,
    TeamLabDhcpLeaseRequest[] Records,
    TeamLabDnsRecordRequest[]? DnsRecords = null);

public record TeamLabManagedRouterFragmentIntent(
    string Key,
    string[] NetworkKeys);

public record TeamLabFabricUplinkIntent(
    string FabricIp,
    string HubAddressCidr,
    string NodeAddressCidr,
    string HostInterfaceName,
    string NamespaceInterfaceName,
    TeamLabStaticRouteRequest[] LocalRoutes,
    TeamLabStaticRouteRequest[] RemoteRoutes);

public record TeamLabObservationPointIntent(
    Guid PublicId,
    string TopologyKey,
    byte Kind,
    string InterfaceToken);

public record TeamLabInfrastructureApplyRequest(
    int RuntimeId,
    int Generation,
    int RouteVersion,
    string RouterNamespace,
    TeamLabManagedSwitchIntent[] Switches,
    TeamLabManagedRouterFragmentIntent[] Routers,
    TeamLabFabricUplinkIntent Fabric,
    TeamLabForwardPolicyRequest[] ForwardPolicies,
    TeamLabObservationPointIntent[] ObservationPoints,
    bool DryRun = true);

public record TeamLabInfrastructureResourceFact(
    string Kind,
    string Key,
    string NativeIdentity,
    string Status);

public record TeamLabInfrastructureApplyResponse(
    bool Success,
    bool DryRun,
    string Message,
    string? DesiredStateDigest,
    bool AlreadyApplied,
    TeamLabInfrastructureResourceFact[] Resources,
    string[] Commands);

public record TeamLabInfrastructureStateResponse(
    bool Exists,
    int RuntimeId,
    int Generation,
    int RouteVersion,
    string? DesiredStateDigest,
    TeamLabInfrastructureResourceFact[] Resources,
    DateTimeOffset? AppliedAt);

public record TeamLabBridgeRequest(
    int RuntimeId,
    string BridgeName,
    string Cidr,
    bool DryRun = true);

public record TeamLabRouterInterfaceRequest(
    string BridgeName,
    string GatewayAddressCidr);

public record TeamLabStaticRouteRequest(
    string TargetCidr,
    string GatewayIp,
    string SourceIp = "");

public record TeamLabRouterRequest(
    int RuntimeId,
    string NamespaceName,
    TeamLabRouterInterfaceRequest[] Interfaces,
    TeamLabStaticRouteRequest[] Routes,
    bool DryRun = true);

public record TeamLabWireGuardRequest(
    int RuntimeId,
    int Generation,
    string NamespaceName,
    string InterfaceName,
    int ListenPort,
    string AddressCidr,
    string InterfacePrivateKey,
    string PeerPublicKey,
    string PeerClientAddress,
    string PeerAllowedIps,
    string[] PlayerAllowedCidrs,
    string[] PlayerBlockedCidrs,
    bool DryRun = true);

public record TeamLabWireGuardCleanupRequest(
    int RuntimeId,
    int Generation,
    string NamespaceName,
    string InterfaceName,
    bool DryRun = true);

public record TeamLabCleanupRequest(
    int RuntimeId,
    int Generation,
    string RouterNamespace,
    string[] ResourceNames,
    string[] SensorAssetKeys,
    string[] FabricRemoteCidrs,
    bool DryRun = true);

public record TeamLabAssetLifecycleRequest(
    string Kind,
    string ResourceId,
    int Generation,
    bool DryRun = false);

public record TeamLabAssetLifecycleResponse(
    bool Success,
    bool DryRun,
    string State,
    string Message);

public record TeamLabProbeRequest(
    int RuntimeId,
    string NamespaceName,
    string TargetIp,
    string? Kind = null,
    int? Port = null,
    bool DryRun = true);

public record TeamLabContainerAttachRequest(
    int RuntimeId,
    string ContainerId,
    string BridgeName,
    string HostInterfaceName,
    string ContainerInterfaceName,
    string AddressCidr,
    string? MacAddress,
    bool RemoveDefaultRoute,
    string? GatewayIp,
    string[] StaticRoutes,
    string[] DnsServers,
    bool DryRun = true);

public record TeamLabContainerInterfaceExpectation(
    string Name,
    string AddressCidr,
    string MacAddress);

public record TeamLabContainerRouteExpectation(
    string TargetCidr,
    string? GatewayIp,
    string InterfaceName);

public record TeamLabContainerDnsProbeExpectation(
    string Server,
    string QueryName,
    string ExpectedAddress);

public record TeamLabContainerNetworkFinalizeRequest(
    Guid OperationId,
    int RuntimeId,
    int Generation,
    string ContainerId,
    string ContainerName,
    TeamLabContainerInterfaceExpectation[] Interfaces,
    TeamLabContainerRouteExpectation[] Routes,
    string[] DnsServers,
    TeamLabContainerDnsProbeExpectation[] DnsProbes,
    bool RequireNoDefaultRoute,
    bool DryRun = false);

public record TeamLabContainerNetworkFinalizeResponse(
    bool Success,
    bool DryRun,
    string Message,
    bool AlreadyFinalized,
    string[] Commands);

public record TeamLabDhcpLeaseRequest(
    string MacAddress,
    string IpAddress,
    string Hostname,
    bool IsPrimary = true);

public record TeamLabDnsRecordRequest(
    string Hostname,
    string IpAddress);

public record TeamLabDhcpDnsRequest(
    int RuntimeId,
    string ServiceName,
    string NamespaceName,
    string BridgeName,
    string InterfaceName,
    string GatewayIp,
    string Cidr,
    string Domain,
    TeamLabDhcpLeaseRequest[] Leases,
    TeamLabDnsRecordRequest[] DnsRecords,
    bool DryRun = true,
    int Generation = 0);

public record TeamLabFabricApplyRequest(
    int RuntimeId,
    int Generation,
    int RouteVersion,
    string FabricIp,
    string? NamespaceName = null,
    string NamespaceHostAddressCidr = "",
    string NamespacePeerAddressCidr = "",
    string HostInterfaceName = "",
    string NamespaceInterfaceName = "",
    TeamLabStaticRouteRequest[]? LocalRoutes = null,
    TeamLabStaticRouteRequest[]? Routes = null,
    TeamLabForwardPolicyRequest[]? ForwardPolicies = null,
    bool DryRun = true);

public record TeamLabForwardPolicyRequest(
    string SourceCidr,
    string DestinationCidr,
    bool Allow);

public record TeamLabCaptureStartRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    Guid ObservationPointId,
    string InterfaceToken,
    int MaxSeconds,
    long MaxBytes,
    bool DryRun = true);

public record TeamLabCaptureStopRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    bool DryRun = true);

public record TeamLabCaptureStatusRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    bool DryRun = true);

public record TeamLabCaptureDeleteRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    bool DryRun = true);

public record TeamLabCaptureUploadRequest(
    int RuntimeId,
    int Generation,
    Guid CaptureId,
    Guid SegmentId,
    string UploadPath,
    string UploadToken,
    long MaxBytes,
    bool DryRun = true);

public record TeamLabCaptureResponse(
    bool Success,
    bool DryRun,
    string Message,
    Guid SegmentId,
    string? FilePath,
    long CapturedBytes,
    bool Running,
    string? Sha256,
    bool Uploaded,
    string[] Commands);

public enum TeamLabObservationEvidenceKind : byte
{
    Packet = 0,
    EndpointProcess = 1
}

public record TeamLabObservationBatchRequest(
    int RuntimeId,
    int Generation,
    long AfterSequence = 0,
    Guid? ObservationPointId = null,
    int Limit = 500);

public record TeamLabObservationRecord(
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
    TeamLabObservationEvidenceKind EvidenceKind,
    string? ProcessIdentityHash = null,
    string Direction = "observed",
    DateTimeOffset? FirstSeenAt = null,
    DateTimeOffset? LastSeenAt = null,
    long Packets = 1,
    long? Bytes = null);

public record TeamLabObservationHealth(
    bool Running,
    int RegisteredPointCount,
    int ActiveInterfaceCount,
    int ActiveFlowCount,
    long DroppedCount,
    long ParserFailureCount,
    long SensorRejectedCount,
    long SpoolBytes,
    string? LastSensorErrorCode,
    string? LastError);

public record TeamLabObservationBatchResponse(
    bool Success,
    string Message,
    long NextSequence,
    long DroppedCount,
    TeamLabObservationRecord[] Records,
    TeamLabObservationHealth Health);

public enum TeamLabEndpointSensorChannelMode : byte
{
    Vm = 0,
    Docker = 1
}

public record TeamLabEndpointSensorRegistrationRequest(
    int RuntimeId,
    string RuntimePublicId,
    int Generation,
    string AssetKey,
    string RuntimeResourceId,
    int SensorVersion,
    string HmacKeyBase64,
    TeamLabEndpointSensorChannelMode Mode);

public record TeamLabEndpointSensorRemoveRequest(
    int RuntimeId,
    int Generation,
    string AssetKey);

public record TeamLabEndpointSensorStartRequest(
    int RuntimeId,
    int Generation,
    string AssetKey,
    string RuntimeResourceId,
    TeamLabEndpointSensorChannelMode Mode,
    VmInitOsType? OsType = null);

public record TeamLabEndpointSensorResponse(
    bool Success,
    string Message,
    string? ChannelEndpoint = null);
