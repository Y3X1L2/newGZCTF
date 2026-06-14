namespace GZCTF.Agent.Models;

public class CreateContainerRequest
{
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
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public string? StartCommand { get; set; }
    public string? HealthCheck { get; set; }
    public List<ContainerNetworkAttachment> NetworkAttachments { get; set; } = [];
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
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; }
    public int PublicPort { get; set; }
}
