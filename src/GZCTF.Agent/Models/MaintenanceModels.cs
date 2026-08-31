using GZCTF.TeamLab.Contracts;

namespace GZCTF.Agent.Models;

public record AgentSyncRequest(
    string DownloadUrl,
    string? ExpectedSha256 = null,
    string? LinuxSensorDownloadUrl = null,
    string? LinuxSensorSha256 = null,
    string? WindowsSensorDownloadUrl = null,
    string? WindowsSensorSha256 = null,
    AgentVmControlPlaneSyncConfig? VmControlPlane = null,
    TeamLabDataPlaneSyncConfig? TeamLabDataPlane = null,
    bool Restart = true);

public sealed record AgentVmControlPlaneSyncConfig(
    bool Enabled,
    string BridgeName = "gzmgt0",
    string HostAddress = "100.127.0.1",
    int PrefixLength = 16,
    int ListenPort = 5443,
    string GuestStateRoot = "/var/lib/gzctf/teamlab/guest-control");

public sealed record TeamLabDataPlaneSyncConfig(
    bool Enabled,
    TeamLabExecutionModel ExecutionModel,
    bool ControlPlane,
    string? NorthboundEndpoint,
    string? SouthboundEndpoint,
    string? NorthboundListenEndpoint,
    string? SouthboundListenEndpoint,
    string? ChassisEncapIp,
    string IntegrationBridgeName = "br-int",
    int ManagedDhcpLeaseSeconds = 3600);

public record AgentSyncResponse(
    bool Success,
    string Message,
    string? AgentVersion);
