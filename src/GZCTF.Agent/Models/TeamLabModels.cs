namespace GZCTF.Agent.Models;

public class AgentTeamLabConfig
{
    public bool Enable { get; set; }
    public bool DryRun { get; set; } = true;
}

public record TeamLabStatusResponse(
    bool Available,
    bool Enable,
    bool DryRun,
    bool HasIpCommand,
    bool HasWireGuardCommand,
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
    string GatewayIp,
    bool DryRun = true);

public record TeamLabRouterRequest(
    int RuntimeId,
    string NamespaceName,
    string[] BridgeNames,
    bool DryRun = true);

public record TeamLabWireGuardRequest(
    int RuntimeId,
    string InterfaceName,
    int ListenPort,
    string AddressCidr,
    string PeerPublicKey,
    string PeerAllowedIps,
    bool DryRun = true);

public record TeamLabCleanupRequest(
    int RuntimeId,
    string[] ResourceNames,
    bool DryRun = true);
