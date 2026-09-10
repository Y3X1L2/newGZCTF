using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public class HeartbeatWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly AgentConfig _config;
    private readonly ILogger<HeartbeatWorker> _logger;
    private long _sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public HeartbeatWorker(IServiceProvider sp, IOptions<AgentConfig> config, ILogger<HeartbeatWorker> logger)
    { _sp = sp; _config = config.Value; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var docker = scope.ServiceProvider.GetRequiredService<DockerService>();
                var kvm = scope.ServiceProvider.GetRequiredService<KvmService>();
                var clientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

                var client = clientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.AuthToken);

                var cpuLoad = await GetCpuLoadAsync();
                var memLoad = GetMemoryLoad();
                var teamLab = scope.ServiceProvider.GetRequiredService<TeamLabNetworkService>();
                var teamLabStatus = await teamLab.GetStatusAsync(token);
                var capabilityService = scope.ServiceProvider.GetRequiredService<AgentCapabilityService>();
                var manifest = await capabilityService.GetManifestAsync(
                    await capabilityService.GetBinarySha256Async(), token);
                var containers = await ReadCountAsync(manifest.Features.Contains(AgentFeatureIds.Docker, StringComparer.Ordinal),
                    docker.GetContainerCountAsync, "Docker", _logger, token);
                var vms = await ReadCountAsync(teamLabStatus.Capabilities.Kvm &&
                    teamLabStatus.Capabilities.KvmDevice && teamLabStatus.Capabilities.CpuVirtualization,
                    kvm.GetVmCountAsync, "KVM", _logger, token);

                var payload = new
                {
                    Sequence = Interlocked.Increment(ref _sequence),
                    ObservedAt = DateTimeOffset.UtcNow,
                    CpuLoad = cpuLoad,
                    MemoryLoad = memLoad,
                    CurrentContainers = containers,
                    CurrentVms = vms,
                    UsedPorts = 0,
                    CapabilityManifest = manifest,
                    TeamLabFabricIp = teamLabStatus.FabricIp,
                    TeamLabFabricStatus = teamLabStatus.Available && teamLabStatus.Enable && teamLabStatus.FabricReady ? 3 :
                        teamLabStatus.Available ? 1 : 4
                };

                var url = $"{_config.ServerUrl}/api/v1/nodes/{_config.NodeId}/heartbeat";
                using var response = await client.PostAsJsonAsync(url, payload, token);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(token);
                    _logger.LogWarning("Heartbeat failed with HTTP {StatusCode}: {Body}",
                        (int)response.StatusCode, body);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds), token);
        }
    }

    internal static async Task<int> ReadCountAsync(bool available,
        Func<CancellationToken, Task<int>> read, string capability, ILogger logger, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!available) return 0;
        try { return await read(token); }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            logger.LogWarning("{Capability} inventory collection failed ({ErrorType}); other heartbeat metrics remain available",
                capability, exception.GetType().Name);
            return 0;
        }
    }

    /// <summary>
    /// Measures SYSTEM-level CPU load via /proc/stat (not process-level).
    /// Samples CPU counters over 500ms to compute utilization percentage.
    /// </summary>
    private static async Task<float> GetCpuLoadAsync()
    {
        try
        {
            var stat1 = await File.ReadAllTextAsync("/proc/stat");
            var cpuLine1 = stat1.Split('\n').First(l => l.StartsWith("cpu "));
            var cols1 = cpuLine1.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(ulong.Parse).ToArray();
            var idle1 = cols1[3] + (cols1.Length > 4 ? cols1[4] : 0);
            var total1 = cols1.Aggregate(0UL, (a, b) => a + b);

            await Task.Delay(500);

            var stat2 = await File.ReadAllTextAsync("/proc/stat");
            var cpuLine2 = stat2.Split('\n').First(l => l.StartsWith("cpu "));
            var cols2 = cpuLine2.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(ulong.Parse).ToArray();
            var idle2 = cols2[3] + (cols2.Length > 4 ? cols2[4] : 0);
            var total2 = cols2.Aggregate(0UL, (a, b) => a + b);

            var idleDelta = idle2 - idle1;
            var totalDelta = total2 - total1;
            return totalDelta > 0 ? 1.0f - (float)idleDelta / totalDelta : 0f;
        }
        catch { return 0f; }
    }

    /// <summary>
    /// Measures SYSTEM-level memory load via /proc/meminfo (not GC heap).
    /// Computes used ratio as 1 - MemAvailable / MemTotal.
    /// </summary>
    private static float GetMemoryLoad()
    {
        try
        {
            var meminfo = File.ReadAllText("/proc/meminfo");
            var totalLine = meminfo.Split('\n').First(l => l.StartsWith("MemTotal:"));
            var availLine = meminfo.Split('\n').First(l => l.StartsWith("MemAvailable:"));
            var total = ulong.Parse(totalLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            var avail = ulong.Parse(availLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
            return total > 0 ? 1.0f - (float)avail / total : 0f;
        }
        catch { return 0f; }
    }
}
