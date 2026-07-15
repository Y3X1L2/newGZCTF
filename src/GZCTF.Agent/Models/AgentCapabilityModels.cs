namespace GZCTF.Agent.Models;

public static class AgentFeatureIds
{
    public const string Docker = "runtime.docker.v1";
    public const string Kvm = "runtime.kvm.v1";
    public const string CloudInit = "runtime.vm.cloud-init.v1";
    public const string DockerPull = "image.docker.pull.v1";
    public const string VmDownload = "image.vm.download.v1";
    public const string TeamLabFabric = "teamlab.fabric.l3.v1";
    public const string WireGuard = "teamlab.wireguard.v1";
    public const string Flow = "teamlab.flow.v1";
    public const string Pcap = "teamlab.pcap.v1";
    public const string RuntimeInventory = "runtime.inventory.v1";
    public const string SelfUpdate = "maintenance.self-update.v1";
}

public sealed record AgentExecutionLimits(
    int DockerCreates,
    int VmCreates,
    int DockerImageTransfers,
    int VmImageTransfers,
    int TeamLabNetworkOperations,
    int ControlOperations);

public sealed record AgentHostFacts(
    int LogicalCpu,
    long TotalMemoryBytes,
    bool KvmDevice,
    bool CpuVirtualization);

public sealed record AgentCapabilityManifest(
    string AgentVersion,
    string? BinarySha256,
    int ManifestSchemaVersion,
    string[] Features,
    AgentExecutionLimits ExecutionLimits,
    AgentHostFacts Host,
    DateTimeOffset ObservedAt);
