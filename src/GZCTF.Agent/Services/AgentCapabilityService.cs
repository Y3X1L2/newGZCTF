using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace GZCTF.Agent.Services;

public sealed class AgentCapabilityService(
    TeamLabNetworkService teamLab,
    TeamLabDataPlanePreparationService dataPlane,
    IOptions<AgentConfig> options,
    IOptions<AgentTeamLabConfig> teamLabOptions)
{
    const int ManifestSchemaVersion = 1;
    readonly AgentConfig _config = options.Value;
    readonly AgentTeamLabConfig _teamLabConfig = teamLabOptions.Value;
    readonly Lazy<Task<string?>> _binarySha256 = new(ComputeBinarySha256Async);

    public Task<string?> GetBinarySha256Async() => _binarySha256.Value;

    public async Task<AgentCapabilityManifest> GetManifestAsync(string? binarySha256, CancellationToken token)
    {
        var teamLabStatus = await teamLab.GetStatusAsync(token);
        var dataPlaneReadiness = await dataPlane.GetReadinessAsync(_teamLabConfig, token);
        var capabilities = teamLabStatus.Capabilities;
        var features = new List<string>();
        if (capabilities.Docker)
        {
            features.Add(AgentFeatureIds.Docker);
            features.Add(AgentFeatureIds.DockerPull);
        }
        var kvm = capabilities.Kvm && capabilities.KvmDevice && capabilities.CpuVirtualization;
        if (kvm)
        {
            features.Add(AgentFeatureIds.Kvm);
            features.Add(AgentFeatureIds.VmDownload);
            features.Add(AgentFeatureIds.VmQga);
            features.Add(AgentFeatureIds.VmWindowsBootstrap);
            if (HasAnyCommand("cloud-localds", "genisoimage", "xorriso"))
                features.Add(AgentFeatureIds.CloudInit);
        }
        if (teamLabStatus.Available)
        {
            features.Add(AgentFeatureIds.TeamLabInfrastructure);
            features.Add(AgentFeatureIds.TeamLabFabricLeasedLinks);
            if (capabilities.Docker && capabilities.DnsProbe)
                features.Add(AgentFeatureIds.TeamLabContainerNetworkFinalize);
            if (HasEndpointSensorArtifacts())
                features.Add(AgentFeatureIds.TeamLabEndpointSensor);
            if (HasLibPcap())
                features.Add(AgentFeatureIds.TeamLabObservation);
            if (HasNativeLibvirt())
                features.Add(AgentFeatureIds.TeamLabNativeLibvirt);
            if (dataPlaneReadiness.Ready &&
                capabilities.OvsVsctl && capabilities.OvsdbClient && capabilities.OvnController &&
                capabilities.OvnNorthboundClient && capabilities.OvnSouthboundClient)
                features.Add(AgentFeatureIds.TeamLabOvnOvs);
            if (dataPlaneReadiness.Ready &&
                capabilities.OvsVsctl && capabilities.OvsdbClient && capabilities.OvnController &&
                capabilities.OvnNorthboundClient && capabilities.OvnSouthboundClient &&
                (capabilities.Docker || kvm))
                features.Add(AgentFeatureIds.TeamLabExecutionPlan);
            if (HasArtifactCacheRoot())
                features.Add(AgentFeatureIds.TeamLabArtifactCache);
        }
        if (capabilities.WireGuard)
            features.Add(AgentFeatureIds.WireGuard);
        if (capabilities.Tcpdump || capabilities.Dumpcap)
            features.Add(AgentFeatureIds.Pcap);
        features.Add(AgentFeatureIds.RuntimeInventory);
        features.Add(AgentFeatureIds.SelfUpdate);
        features.Add(AgentFeatureIds.RuntimeSignals);
        if (kvm) features.Add(AgentFeatureIds.VmReadinessSignals);
        if (kvm && capabilities.Nftables && _config.GuestManagement.Enabled)
            features.Add(AgentFeatureIds.VmGuestManagement);
        if (kvm && features.Contains(AgentFeatureIds.VmGuestManagement) &&
            HasAnyCommand("cloud-localds", "genisoimage", "xorriso"))
        {
            features.Add(AgentFeatureIds.VmConfigDriveV2);
            features.Add(AgentFeatureIds.VmPreparedImage);
        }
        if (features.Contains(AgentFeatureIds.VmPreparedImage))
            features.Add(AgentFeatureIds.VmPreparedImageUpload);
        if (capabilities.Docker || kvm)
            features.Add(AgentFeatureIds.RemoteAccessRelay);
        features.Add(AgentFeatureIds.BootstrapArtifactPull);

        var logicalCpu = Math.Max(1, Environment.ProcessorCount);
        var limits = new AgentExecutionLimits(
            Resolve(_config.ExecutionLimits.DockerCreates, Math.Clamp(logicalCpu / 2, 2, 8), capabilities.Docker),
            Resolve(_config.ExecutionLimits.VmCreates, logicalCpu >= 16 ? 2 : 1, kvm),
            Resolve(_config.ExecutionLimits.DockerImageTransfers, 2, capabilities.Docker),
            Resolve(_config.ExecutionLimits.VmImageTransfers, 1, kvm),
            Resolve(_config.ExecutionLimits.TeamLabNetworkOperations, 4, teamLabStatus.Available),
            Math.Max(1, _config.ExecutionLimits.ControlOperations ?? 2),
            Resolve(_config.ExecutionLimits.TeamLabExecutionOperations, 1,
                features.Contains(AgentFeatureIds.TeamLabExecutionPlan)),
            Resolve(_config.ExecutionLimits.ArtifactCleanupOperations, 1,
                features.Contains(AgentFeatureIds.TeamLabArtifactCache)));
        return new AgentCapabilityManifest(
            typeof(AgentCapabilityService).Assembly.GetName().Version?.ToString() ?? "unknown",
            binarySha256,
            ManifestSchemaVersion,
            features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            limits,
            new AgentHostFacts(logicalCpu, ReadTotalMemory(), ReadAvailableVmImageStorage(), capabilities.KvmDevice,
                capabilities.CpuVirtualization),
            DateTimeOffset.UtcNow);
    }

    static int Resolve(int? configured, int automatic, bool available) =>
        available ? Math.Max(1, configured ?? automatic) : 0;

    static bool HasAnyCommand(params string[] commands) => commands.Any(command =>
        new[] { "/sbin", "/usr/sbin", "/bin", "/usr/bin", "/usr/local/bin" }
            .Any(path => File.Exists(Path.Combine(path, command))));

    static bool HasLibPcap()
    {
        foreach (var name in new[] { "libpcap.so.1", "libpcap.so.0.8", "libpcap.so", "wpcap.dll" })
        {
            if (!NativeLibrary.TryLoad(name, out var handle)) continue;
            NativeLibrary.Free(handle);
            return true;
        }
        return false;
    }

    static bool HasNativeLibvirt() =>
        OperatingSystem.IsLinux() &&
        (NativeLibrary.TryLoad("libvirt.so.0", out var handle) ||
         NativeLibrary.TryLoad("libvirt.so", out handle)) &&
        Release(handle);

    static bool Release(nint handle)
    {
        if (handle != 0) NativeLibrary.Free(handle);
        return handle != 0;
    }

    static bool HasArtifactCacheRoot() =>
        Directory.Exists("/var/lib/gzctf/images") || Directory.Exists("/var/lib/gzctf/teamlab");

    static bool HasEndpointSensorArtifacts() =>
        File.Exists("/opt/gzctf/endpoint-sensor/linux-x64/gzctf-endpoint-sensor") &&
        File.Exists("/opt/gzctf/endpoint-sensor/win-x64/gzctf-endpoint-sensor.exe");

    static long ReadTotalMemory()
    {
        try
        {
            var line = File.ReadLines("/proc/meminfo").First(item => item.StartsWith("MemTotal:"));
            return long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) * 1024;
        }
        catch
        {
            return 0;
        }
    }

    static long ReadAvailableVmImageStorage()
    {
        try
        {
            const string imagePath = "/var/lib/gzctf/images";
            var root = Path.GetPathRoot(Path.GetFullPath(imagePath));
            return string.IsNullOrWhiteSpace(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }

    static async Task<string?> ComputeBinarySha256Async()
    {
        var path = File.Exists("/usr/local/bin/gzctf-agent")
            ? "/usr/local/bin/gzctf-agent"
            : Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? await AgentMaintenanceService.ComputeFileSha256Async(path, CancellationToken.None)
            : null;
    }
}
