using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public class HeartbeatWorker : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly AgentConfig _config;
    private readonly ILogger<HeartbeatWorker> _logger;

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
                var containers = await docker.GetContainerCountAsync(token);
                var vms = await kvm.GetVmCountAsync(token);

                var payload = new
                {
                    CpuLoad = cpuLoad,
                    MemoryLoad = memLoad,
                    CurrentContainers = containers,
                    CurrentVms = vms,
                    UsedPorts = 0
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
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds), token);
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
