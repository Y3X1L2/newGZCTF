using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public sealed class AgentCapabilityService(
    TeamLabNetworkService teamLab,
    IOptions<AgentConfig> options)
{
    const int ManifestSchemaVersion = 1;
    readonly AgentConfig _config = options.Value;
    readonly Lazy<Task<string?>> _binarySha256 = new(ComputeBinarySha256Async);

    public Task<string?> GetBinarySha256Async() => _binarySha256.Value;

    public async Task<AgentCapabilityManifest> GetManifestAsync(string? binarySha256, CancellationToken token)
    {
        var teamLabStatus = await teamLab.GetStatusAsync(token);
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
            if (HasAnyCommand("cloud-localds", "genisoimage", "xorriso"))
                features.Add(AgentFeatureIds.CloudInit);
        }
        if (teamLabStatus.Available)
            features.Add(AgentFeatureIds.TeamLabFabric);
        if (capabilities.WireGuard)
            features.Add(AgentFeatureIds.WireGuard);
        if (capabilities.Tcpdump || capabilities.Dumpcap)
        {
            features.Add(AgentFeatureIds.Flow);
            features.Add(AgentFeatureIds.Pcap);
        }
        features.Add(AgentFeatureIds.RuntimeInventory);
        features.Add(AgentFeatureIds.SelfUpdate);

        var logicalCpu = Math.Max(1, Environment.ProcessorCount);
        var limits = new AgentExecutionLimits(
            Resolve(_config.ExecutionLimits.DockerCreates, Math.Clamp(logicalCpu / 2, 2, 8), capabilities.Docker),
            Resolve(_config.ExecutionLimits.VmCreates, logicalCpu >= 16 ? 2 : 1, kvm),
            Resolve(_config.ExecutionLimits.DockerImageTransfers, 2, capabilities.Docker),
            Resolve(_config.ExecutionLimits.VmImageTransfers, 1, kvm),
            Resolve(_config.ExecutionLimits.TeamLabNetworkOperations, 4, teamLabStatus.Available),
            Math.Max(1, _config.ExecutionLimits.ControlOperations ?? 2));
        return new AgentCapabilityManifest(
            typeof(AgentCapabilityService).Assembly.GetName().Version?.ToString() ?? "unknown",
            binarySha256,
            ManifestSchemaVersion,
            features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            limits,
            new AgentHostFacts(logicalCpu, ReadTotalMemory(), capabilities.KvmDevice,
                capabilities.CpuVirtualization),
            DateTimeOffset.UtcNow);
    }

    static int Resolve(int? configured, int automatic, bool available) =>
        available ? Math.Max(1, configured ?? automatic) : 0;

    static bool HasAnyCommand(params string[] commands) => commands.Any(command =>
        new[] { "/sbin", "/usr/sbin", "/bin", "/usr/bin", "/usr/local/bin" }
            .Any(path => File.Exists(Path.Combine(path, command))));

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
