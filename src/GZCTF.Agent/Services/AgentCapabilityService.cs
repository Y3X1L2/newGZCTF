using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public sealed class AgentCapabilityService(
    IOptions<AgentConfig> options,
    IOptions<KvmConfig> kvmOptions,
    DockerService docker)
{
    const int ManifestSchemaVersion = 1;
    readonly AgentConfig _config = options.Value;
    readonly KvmConfig _kvmConfig = kvmOptions.Value;
    readonly Lazy<Task<string?>> _binarySha256 = new(ComputeBinarySha256Async);

    public Task<string?> GetBinarySha256Async() => _binarySha256.Value;

    public async Task<AgentCapabilityManifest> GetManifestAsync(string? binarySha256, CancellationToken token)
    {
        var dockerAvailable = await docker.IsAvailableAsync(token);
        var kvmDevice = File.Exists("/dev/kvm");
        var cpuVirtualization = File.Exists("/proc/cpuinfo") &&
            System.Text.RegularExpressions.Regex.IsMatch(await File.ReadAllTextAsync("/proc/cpuinfo", token), @"\b(vmx|svm)\b");
        var features = new List<string>();
        if (dockerAvailable)
        {
            features.Add(AgentFeatureIds.Docker);
            features.Add(AgentFeatureIds.DockerPull);
        }
        var kvm = HasAnyCommand("virsh") && kvmDevice && cpuVirtualization;
        if (kvm)
        {
            features.Add(AgentFeatureIds.Kvm);
            features.Add(AgentFeatureIds.VmDownload);
            features.Add(AgentFeatureIds.VmQga);
            features.Add(AgentFeatureIds.VmWindowsBootstrap);
            if (HasAnyCommand("cloud-localds", "genisoimage", "xorriso"))
                features.Add(AgentFeatureIds.CloudInit);
        }
        features.Add(AgentFeatureIds.RuntimeInventory);
        features.Add(AgentFeatureIds.SelfUpdate);
        if (kvm && HasAnyCommand("nft") && _config.GuestManagement.Enabled)
            features.Add(AgentFeatureIds.VmGuestManagement);
        if (kvm && features.Contains(AgentFeatureIds.VmGuestManagement) &&
            HasAnyCommand("cloud-localds", "genisoimage", "xorriso"))
        {
            features.Add(AgentFeatureIds.VmConfigDriveV2);
            features.Add(AgentFeatureIds.VmPreparedImage);
        }
        if (features.Contains(AgentFeatureIds.VmPreparedImage))
            features.Add(AgentFeatureIds.VmPreparedImageUpload);
        features.Add(AgentFeatureIds.BootstrapArtifactPull);

        var logicalCpu = Math.Max(1, Environment.ProcessorCount);
        var limits = new AgentExecutionLimits(
            Resolve(_config.ExecutionLimits.DockerCreates, Math.Clamp(logicalCpu, 2, 8), dockerAvailable),
            Resolve(_config.ExecutionLimits.VmCreates, Math.Clamp((logicalCpu + 1) / 2, 1, 4), kvm),
            Resolve(_config.ExecutionLimits.DockerImageTransfers, 2, dockerAvailable),
            Resolve(_config.ExecutionLimits.VmImageTransfers, 1, kvm),
            0,
            Math.Max(1, _config.ExecutionLimits.ControlOperations ?? 2), 0, 0);
        var dockerRootDirectory = dockerAvailable ? await docker.GetDockerRootDirectoryAsync(token) : null;
        return new AgentCapabilityManifest(
            typeof(AgentCapabilityService).Assembly.GetName().Version?.ToString() ?? "unknown",
            binarySha256,
            ManifestSchemaVersion,
            features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            limits,
            new AgentHostFacts(logicalCpu, ReadTotalMemory(),
                kvm ? ReadAvailableStorage(_kvmConfig.ImageStoragePath) : 0,
                kvmDevice, cpuVirtualization,
                ReadAvailableStorage(dockerRootDirectory)),
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

    internal static long ReadAvailableStorage(string? path, Func<string, long>? availableFreeSpace = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
                !Directory.Exists(path)) return 0;
            return Math.Max(0, availableFreeSpace?.Invoke(path) ?? new DriveInfo(path).AvailableFreeSpace);
        }
        catch
        {
            return 0;
        }
    }

    static Task<string?> ComputeBinarySha256Async() =>
        AgentMaintenanceService.ComputeRunningBinarySha256Async(CancellationToken.None);
}
