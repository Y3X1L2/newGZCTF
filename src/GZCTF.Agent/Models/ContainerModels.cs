namespace GZCTF.Agent.Models;

public class CreateContainerRequest
{
    public string? AssetKey { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public string Image { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public int ChallengeId { get; set; }
    public Guid UserId { get; set; }
    public int ExposedPort { get; set; }
    public string? Flag { get; set; }
    public bool EnableTrafficCapture { get; set; }
    public int MemoryLimit { get; set; } = 64;
    public int CPUCount { get; set; } = 1;
    public int StorageLimit { get; set; } = 256;
    public string NetworkMode { get; set; } = "Open";
    public string? NetworkName { get; set; }
    public string? IPAddress { get; set; }
    public List<string> AdditionalNetworkNames { get; set; } = [];
    public Dictionary<string, string> NetworkSubnets { get; set; } = [];
    public bool PublishPort { get; set; } = true;
    public int? PreferredHostPort { get; set; }
    public bool BypassPublicProxy { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public string? StartCommand { get; set; }
    public List<string> DnsServers { get; set; } = [];
    public string? HealthCheck { get; set; }
    public bool UsePenetrationFabric { get; set; }
    public bool UseHostNetworkNone { get; set; }
    public bool EnableTeamLabNetworkGate { get; set; } = true;
    public bool StartImmediately { get; set; } = true;
    public string? TeamLabPlanDigest { get; set; }
    public string? TeamLabShardKey { get; set; }
    public bool EnableNetworkAdmin { get; set; }
    public bool RemoveDefaultRoute { get; set; }
    public bool EnableIpForwarding { get; set; }
    public List<ContainerNetworkAttachment> NetworkAttachments { get; set; } = [];
    public List<ContainerBindMount> BindMounts { get; set; } = [];
}

public class ContainerBindMount
{
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public bool ReadOnly { get; set; } = true;
}

public class ContainerNetworkAttachment
{
    public string NetworkName { get; set; } = string.Empty;
    public string? SubnetCidr { get; set; }
    public string? IPAddress { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsInternal { get; set; }
}

public class AgentContainerResponse
{
    public string ContainerId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; }
    public int PublicPort { get; set; }
}

public class ExecuteContainerCommandRequest
{
    public List<string> Command { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 10;
}

public class AgentCommandResult
{
    public bool IsSupported { get; set; } = true;
    public bool Succeeded { get; set; }
    public bool TimedOut { get; set; }
    public long? ExitCode { get; set; }
    public string? Message { get; set; }

    public static AgentCommandResult Success(string? message = null) => new()
    {
        Succeeded = true,
        ExitCode = 0,
        Message = message
    };

    public static AgentCommandResult Failed(long? exitCode, string? message) => new()
    {
        Succeeded = false,
        ExitCode = exitCode,
        Message = message
    };

    public static AgentCommandResult Timeout(string? message = null) => new()
    {
        Succeeded = false,
        TimedOut = true,
        Message = message
    };
}

public class FabricNetworkRequest
{
    public string NetworkName { get; set; } = string.Empty;
    public string Cidr { get; set; } = string.Empty;
}

public class FabricAttachRequest
{
    public string NetworkName { get; set; } = string.Empty;
    public string NetworkCidr { get; set; } = string.Empty;
    public string HostInterfaceName { get; set; } = string.Empty;
    public string ContainerInterfaceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int PrefixLength { get; set; }
    public bool IsPrimary { get; set; }
    public bool RemoveDefaultRoute { get; set; }
}

public class FabricRouteRequest
{
    public string TargetCidr { get; set; } = string.Empty;
    public string GatewayIp { get; set; } = string.Empty;
}

public class FabricProbeRequest
{
    public string TargetIp { get; set; } = string.Empty;
}

public class AgentFabricResult
{
    public bool IsSupported { get; set; } = true;
    public bool Succeeded { get; set; }
    public bool TimedOut { get; set; }
    public long? ExitCode { get; set; }
    public string? Message { get; set; }

    public static AgentFabricResult Success(string? message = null) => new()
    {
        Succeeded = true,
        ExitCode = 0,
        Message = message
    };

    public static AgentFabricResult Failed(long? exitCode, string? message) => new()
    {
        Succeeded = false,
        ExitCode = exitCode,
        Message = message
    };

    public static AgentFabricResult Timeout(string? message = null) => new()
    {
        Succeeded = false,
        TimedOut = true,
        Message = message
    };

    public static AgentFabricResult Unsupported(string? message = null) => new()
    {
        IsSupported = false,
        Succeeded = false,
        Message = message
    };
}
