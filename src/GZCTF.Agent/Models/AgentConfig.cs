namespace GZCTF.Agent.Models;

public class AgentConfig
{
    public string ServerUrl { get; set; } = "http://localhost:8080";
    public Guid NodeId { get; set; }
    public string AuthToken { get; set; } = string.Empty;
    public int ListenPort { get; set; } = 5001;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public string OperationStateRoot { get; set; } = "/var/lib/gzctf/agent";
    public GuestManagementConfig GuestManagement { get; set; } = new();
    public AgentExecutionLimitOverrides ExecutionLimits { get; set; } = new();
}

public sealed class GuestManagementConfig
{
    public bool Enabled { get; set; }
    public string BridgeName { get; set; } = "gzmgt0";
    public string HostAddress { get; set; } = "100.127.0.1";
    public int PrefixLength { get; set; } = 16;
    public int ListenPort { get; set; } = 5443;
    public string StateRoot { get; set; } = "/var/lib/gzctf/teamlab/guest-control";
    public int EnrollmentTtlMinutes { get; set; } = 15;
    public int ClientCertificateLifetimeMinutes { get; set; } = 120;
}

public sealed class AgentExecutionLimitOverrides
{
    public int? DockerCreates { get; set; }
    public int? VmCreates { get; set; }
    public int? DockerImageTransfers { get; set; }
    public int? VmImageTransfers { get; set; }
    public int? TeamLabNetworkOperations { get; set; }
    public int? ControlOperations { get; set; }
}

public class DockerConfig
{
    public string Uri { get; set; } = "unix:///var/run/docker.sock";
    public string ChallengeNetwork { get; set; } = "gzctf";
    public string PublicEntry { get; set; } = "localhost";
    public int? PublicPortStart { get; set; }
    public int? PublicPortEnd { get; set; }
}

public class KvmConfig
{
    public string LibvirtUri { get; set; } = "qemu:///system";
    public string ImageStoragePath { get; set; } = "/var/lib/gzctf/images";
    public int DefaultVmMemoryMb { get; set; } = 2048;
    public int DefaultVmCpu { get; set; } = 2;

    /// <summary>
    /// Addresses or CIDRs allowed to open a VM console proxy connection. The proxy forwards into a
    /// tenant VM's RDP port without protocol-level authentication, so this is the control that keeps
    /// it reachable only by the platform. Loopback is always allowed; when nothing else resolves,
    /// the proxy accepts loopback only rather than everyone.
    /// </summary>
    public string[] RdpProxyAllowedSources { get; set; } = [];
}
