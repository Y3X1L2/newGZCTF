namespace GZCTF.Modules.Runtime.Contracts;

public static class AgentFeatureIds
{
    public const string Docker = "runtime.docker.v1";
    public const string Kvm = "runtime.kvm.v1";
    public const string CloudInit = "runtime.vm.cloud-init.v1";
    public const string DockerPull = "image.docker.pull.v1";
    public const string VmDownload = "image.vm.download.v1";
    public const string TeamLabInfrastructure = "teamlab.infrastructure.v2";
    public const string TeamLabFabricLeasedLinks = "teamlab.fabric.leased-links.v1";
    public const string TeamLabContainerNetworkFinalize = "teamlab.container-network-finalize.v1";
    public const string WireGuard = "teamlab.wireguard.v1";
    public const string Pcap = "teamlab.pcap.v1";
    public const string VmQga = "runtime.vm.qga.v1";
    public const string VmWindowsBootstrap = "runtime.vm.windows-bootstrap.v1";
    public const string TeamLabObservation = "teamlab.observation.v2";
    public const string TeamLabEndpointSensor = "teamlab.endpoint-sensor.v2";
    public const string TeamLabPcapObjectStorage = "teamlab.pcap-object-storage.v1";
    public const string BootstrapArtifactPull = "bootstrap.artifact.pull.v1";
    public const string RuntimeInventory = "runtime.inventory.v1";
    public const string SelfUpdate = "maintenance.self-update.v1";
    public const string RuntimeSignals = "runtime.signals.v1";
    public const string VmReadinessSignals = "runtime.vm.readiness-signals.v1";
    public const string VmGuestManagement = "runtime.vm.guest-management.v1";
    public const string VmConfigDriveV2 = "runtime.vm.config-drive-v2.v1";
    public const string VmPreparedImage = "image.vm.prepared.v1";
    public const string VmPreparedImageUpload = "image.vm.prepared-upload.v1";
}

public sealed record AgentExecutionLimits(
    int DockerCreates,
    int VmCreates,
    int DockerImageTransfers,
    int VmImageTransfers,
    int TeamLabNetworkOperations = 0,
    int ControlOperations = 0);

public sealed record AgentHostFacts(
    int LogicalCpu,
    long TotalMemoryBytes,
    long AvailableVmImageStorageBytes = 0,
    bool KvmDevice = false,
    bool CpuVirtualization = false);

public sealed record AgentCapabilityManifest(
    string AgentVersion,
    string? BinarySha256,
    int ManifestSchemaVersion,
    string[] Features,
    AgentExecutionLimits ExecutionLimits,
    AgentHostFacts Host,
    DateTimeOffset ObservedAt);
