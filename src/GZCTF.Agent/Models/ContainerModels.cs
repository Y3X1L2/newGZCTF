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
}

public class AgentContainerResponse
{
    public string ContainerId { get; set; } = string.Empty;
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; }
    public int PublicPort { get; set; }
}
