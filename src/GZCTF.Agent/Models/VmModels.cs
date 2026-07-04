namespace GZCTF.Agent.Models;

public class CreateVmRequest
{
    public int? TemplateId { get; set; }
    public string VmName { get; set; } = string.Empty;
    public int Memory { get; set; } = 2048;
    public int Cpu { get; set; } = 2;
    public string? Flag { get; set; }
    public List<VmNetworkInterfaceRequest> Interfaces { get; set; } = [];
}

public class VmNetworkInterfaceRequest
{
    public string BridgeName { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public string Model { get; set; } = "e1000e";
}

public class CreateVmResponse
{
    public string VmName { get; set; } = string.Empty;
    public string Status { get; set; } = "Running";
    public string? VncAddress { get; set; }
    public List<VmNetworkInterfaceRequest> Interfaces { get; set; } = [];
}

public class VmIpResponse
{
    public string VmName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public int? RdpPort { get; set; }
    public string Status { get; set; } = "Pending";
}

public class VmIpQueryRequest
{
    public List<VmNetworkInterfaceRequest> Interfaces { get; set; } = [];
}

public class PullDockerImageRequest
{
    public string Image { get; set; } = string.Empty;
    public string? RegistryAuth { get; set; }
}

public class EnsureDockerRegistryRequest
{
    public int Port { get; set; } = 5000;
}

public class ConfigureDockerRegistryRequest
{
    public string Registry { get; set; } = string.Empty;
    public string[] Registries { get; set; } = [];
}

public class DownloadVmImageRequest
{
    public int? TemplateId { get; set; }
    public string Hash { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
}
