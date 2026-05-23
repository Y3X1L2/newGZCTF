namespace GZCTF.Agent.Models;

public class AgentConfig
{
    public string ServerUrl { get; set; } = "http://localhost:8080";
    public Guid NodeId { get; set; }
    public string AuthToken { get; set; } = string.Empty;
    public int ListenPort { get; set; } = 5001;
    public int HeartbeatIntervalSeconds { get; set; } = 30;
}

public class DockerConfig
{
    public string Uri { get; set; } = "unix:///var/run/docker.sock";
    public string ChallengeNetwork { get; set; } = "gzctf";
    public string PublicEntry { get; set; } = "localhost";
}

public class KvmConfig
{
    public string LibvirtUri { get; set; } = "qemu:///system";
    public string ImageStoragePath { get; set; } = "/var/lib/gzctf/images";
    public int DefaultVmMemoryMb { get; set; } = 2048;
    public int DefaultVmCpu { get; set; } = 2;
}
