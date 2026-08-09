using GZCTF.GuestControl.Contracts;

namespace GZCTF.Agent.Models;

public enum VmInitOsType
{
    Linux = 0,
    Windows = 1
}

public enum VmInitNetworkMode
{
    Dhcp = 0,
    Preconfigured = 1
}

public class CreateVmRequest
{
    public Guid? OperationId { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public int GuestReadyWarningAfterSeconds { get; set; } = 180;
    public int? TemplateId { get; set; }
    public string? TemplatePath { get; set; }
    public string VmName { get; set; } = string.Empty;
    public int Memory { get; set; } = 2048;
    public int Cpu { get; set; } = 2;
    public string DefaultNetworkModel { get; set; } = "e1000e";
    public string? Flag { get; set; }
    public List<VmNetworkInterfaceRequest> Interfaces { get; set; } = [];
    public VmInitConfig? CloudInit { get; set; }
    public VmGuestControlConfig GuestControl { get; set; } = new();
    public VmManagementInterfaceConfig? ManagementInterface { get; set; }
    public VmGuestSupervisorConfig? GuestSupervisor { get; set; }
}

public sealed class VmGuestSupervisorConfig
{
    public GuestAssetIdentity Identity { get; set; } = null!;
    public string EnrollmentToken { get; set; } = string.Empty;
    public string WorkerServerCertificateSha256 { get; set; } = string.Empty;
    public string EnrollmentEndpoint { get; set; } = string.Empty;
    public string IntentDigest { get; set; } = string.Empty;
}

public sealed record GuestConfigDriveFiles(
    string RootPath,
    string IsoPath,
    string VolumeLabel,
    IReadOnlyList<(string TargetName, string SourcePath)> Files);

public sealed class VmManagementInterfaceConfig
{
    public GuestAssetIdentity? Identity { get; set; }
    public string BridgeName { get; set; } = "gzmgt0";
    public string MacAddress { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int PrefixLength { get; set; } = 16;
    public string HostAddress { get; set; } = "100.127.0.1";
    public string Model { get; set; } = "e1000e";
}

public sealed class VmGuestControlConfig
{
    public bool Enabled { get; set; } = true;
    public bool Required { get; set; } = true;
    public bool EndpointSensorChannel { get; set; }
    public VmInitOsType? OsType { get; set; }
}

public class VmNetworkInterfaceRequest
{
    public string BridgeName { get; set; } = string.Empty;
    public string? HostInterfaceName { get; set; }
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
    public VmInitNetworkMode NetworkMode { get; set; } = VmInitNetworkMode.Dhcp;
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

public sealed record PublishVmImageRequest(
    int TemplateId,
    string Hash,
    long ExpectedSize,
    AgentOciRegistryTarget RegistryTarget);

public sealed record PublishVmImageResponse(
    bool Success,
    bool Verified,
    long Size,
    string Digest,
    string ManifestDigest);

public sealed record DownloadBootstrapArtifactRequest(
    Guid ProfileId,
    int Version,
    string RegistryAddress,
    string Repository,
    string Digest,
    long ExpectedSize);

public sealed record DownloadBootstrapArtifactResponse(
    bool Success,
    string Message,
    bool AlreadyExists,
    bool Verified,
    string? LocalPath,
    long Size,
    string Digest);

public sealed record VmGuestReadyRequest(int TimeoutSeconds = 180);

public sealed record VmGuestCommandRequest(
    string StepId,
    string Path,
    IReadOnlyList<string> Arguments,
    int TimeoutSeconds = 300,
    IReadOnlyDictionary<string, string>? Environment = null);

public sealed record VmGuestCommandResponse(
    bool Success,
    bool TimedOut,
    int? ExitCode,
    string Category,
    string? StandardOutput,
    string? StandardError);

public sealed record VmGuestStatusResponse(
    bool Ready,
    string Message,
    string? Version = null);

public sealed record VmBootstrapApplyRequest(
    Guid? OperationId,
    int RuntimeId,
    int Generation,
    string AssetKey,
    VmInitOsType OsType,
    Guid? ProfileId,
    int? ProfileVersion,
    string? ArtifactDigest,
    long? ArtifactSize,
    string? ManifestJson,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyDictionary<string, string> Secrets,
    IReadOnlyList<VmNetworkInterfaceRequest> Interfaces,
    bool RunHealthChecks = true);

public sealed record VmBootstrapApplyResponse(
    bool Success,
    string Stage,
    string Message,
    int RebootCount,
    IReadOnlyList<string> CompletedSteps,
    IReadOnlyList<string> PassedHealthChecks,
    string? ErrorCode = null,
    string? FailedStep = null,
    string? FailureCategory = null,
    int? ExitCode = null);

public sealed record VmCapabilityProbeRequest(
    VmInitOsType OsType,
    IReadOnlyList<string> Capabilities,
    string? ExpectedMarkerPath = null,
    string? ExpectedMarkerValue = null,
    int TimeoutSeconds = 180);

public sealed record VmCapabilityProbeResponse(
    bool Success,
    IReadOnlyList<string> VerifiedCapabilities,
    IReadOnlyDictionary<string, string> Evidence,
    string? ErrorCode,
    string? ErrorDetail);
