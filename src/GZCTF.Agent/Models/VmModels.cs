namespace GZCTF.Agent.Models;

public enum VmInitOsType
{
    Linux = 0,
    Windows = 1
}

public class CreateVmRequest
{
    public int Generation { get; set; } = 1;
    public int? TemplateId { get; set; }
    public string? TemplatePath { get; set; }
    public string VmName { get; set; } = string.Empty;
    public int Memory { get; set; } = 2048;
    public int Cpu { get; set; } = 2;
    public string? Flag { get; set; }
    public List<VmNetworkInterfaceRequest> Interfaces { get; set; } = [];
    public VmInitConfig? CloudInit { get; set; }
}

public class VmNetworkInterfaceRequest
{
    public string BridgeName { get; set; } = string.Empty;
    public string? MacAddress { get; set; }
    public string Model { get; set; } = "e1000e";
    public string? InterfaceName { get; set; }
    public string? IpAddress { get; set; }
    public int? PrefixLength { get; set; }
    public string? Gateway { get; set; }
    public List<string> DnsServers { get; set; } = [];
    public List<string> Routes { get; set; } = [];
    public bool IsPrimary { get; set; }
}

public class VmInitConfig
{
    public bool Enabled { get; set; }
    public VmInitOsType OsType { get; set; } = VmInitOsType.Linux;
    public string Hostname { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string UserData { get; set; } = string.Empty;
    public string MetaData { get; set; } = string.Empty;
    public string NetworkConfig { get; set; } = string.Empty;
    public List<string> SensitiveKeys { get; set; } = [];
}

public sealed record CloudInitSeedFiles(
    string UserDataPath,
    string MetaDataPath,
    string NetworkConfigPath,
    string IsoPath);

public class CreateVmResponse
{
    public string VmName { get; set; } = string.Empty;
    public string NativeId { get; set; } = string.Empty;
    public int Generation { get; set; } = 1;
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
    public string? Diagnostic { get; set; }
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
    public long? ExpectedSize { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string? RegistryAddress { get; set; }
    public string? Repository { get; set; }
    public string? Tag { get; set; }
    public string? Digest { get; set; }
}

public record DownloadVmImageResponse(
    bool Success,
    string Message,
    bool AlreadyExists,
    bool Verified,
    long? Size,
    string? Digest);
