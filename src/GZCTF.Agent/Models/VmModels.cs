namespace GZCTF.Agent.Models;

public class CreateVmRequest
{
    public int? TemplateId { get; set; }
    public string VmName { get; set; } = string.Empty;
    public int Memory { get; set; } = 2048;
    public int Cpu { get; set; } = 2;
    public string? Flag { get; set; }
}

public class CreateVmResponse
{
    public string VmName { get; set; } = string.Empty;
    public string Status { get; set; } = "Running";
    public string? VncAddress { get; set; }
}

public class PullDockerImageRequest
{
    public string Image { get; set; } = string.Empty;
    public string? RegistryAuth { get; set; }
}

public class DownloadVmImageRequest
{
    public string Hash { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
}
