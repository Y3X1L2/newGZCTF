using System.Diagnostics;
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
                await client.PostAsJsonAsync(url, payload, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.HeartbeatIntervalSeconds), token);
        }
    }

    private static async Task<float> GetCpuLoadAsync()
    {
        try
        {
            var proc = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpu = proc.TotalProcessorTime;
            await Task.Delay(500);
            var endCpu = proc.TotalProcessorTime;
            var cpuUsedMs = (endCpu - startCpu).TotalMilliseconds;
            var totalMsPassed = 500.0;
            return (float)(cpuUsedMs / (Environment.ProcessorCount * totalMsPassed));
        }
        catch { return 0; }
    }

    private static float GetMemoryLoad()
    {
        try
        {
            var info = GC.GetGCMemoryInfo();
            return (float)((double)info.MemoryLoadBytes / info.TotalAvailableMemoryBytes);
        }
        catch { return 0; }
    }
}
