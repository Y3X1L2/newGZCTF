namespace GZCTF.Agent.Models;

public class AgentTeamLabConfig
{
    public bool Enable { get; set; } = true;
    public bool DryRun { get; set; }
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
    bool Dumpcap);

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
    string? Message = null);

public record TeamLabDryRunResponse(
    bool Success,
    bool DryRun,
    string Message,
    string[] Commands);

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

public record TeamLabCleanupRequest(
    int RuntimeId,
    string[] ResourceNames,
    bool DryRun = true);

public record TeamLabProbeRequest(
    int RuntimeId,
    string NamespaceName,
    string TargetIp,
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

public record TeamLabDhcpLeaseRequest(
    string MacAddress,
    string IpAddress,
    string Hostname);

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
    bool DryRun = true);

public record TeamLabDhcpDnsProbeRequest(
    int RuntimeId,
    string NamespaceName,
    string GatewayIp,
    string Hostname,
    bool DryRun = true);

public record TeamLabFabricApplyRequest(
    int RuntimeId,
    int RouteVersion,
    string FabricIp,
    string? NamespaceName = null,
    string NamespaceHostAddressCidr = "",
    string NamespacePeerAddressCidr = "",
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
    int JobId,
    string Scope,
    string InterfaceName,
    int MaxSeconds,
    long MaxBytes,
    bool DryRun = true);

public record TeamLabCaptureStopRequest(
    int RuntimeId,
    int JobId,
    bool DryRun = true);

public record TeamLabCaptureStatusRequest(
    int RuntimeId,
    int JobId,
    bool DryRun = true);

public record TeamLabCaptureResponse(
    bool Success,
    bool DryRun,
    string Message,
    string? FilePath,
    long CapturedBytes,
    bool Running,
    string[] Commands);

public record TeamLabFlowStartRequest(
    int RuntimeId,
    int? ShardId,
    int? NetworkId,
    string NetworkKey,
    string InterfaceName,
    bool DryRun = true);

public record TeamLabFlowStopRequest(
    int RuntimeId,
    string NetworkKey,
    bool DryRun = true);

public record TeamLabFlowSnapshotRequest(
    int RuntimeId,
    string NetworkKey,
    long AfterCursor = 0,
    bool DryRun = true);

public record TeamLabFlowSample(
    long Cursor,
    DateTimeOffset CapturedAt,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    long Bytes);

public record TeamLabFlowResponse(
    bool Success,
    bool DryRun,
    string Message,
    long NextCursor,
    TeamLabFlowSample[] Samples,
    string[] Commands);
