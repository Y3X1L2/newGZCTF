using System.Diagnostics;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class LocalNodeMetricsService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalNodeMetricsService> _logger;

    public LocalNodeMetricsService(IServiceScopeFactory scopeFactory, ILogger<LocalNodeMetricsService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshLocalNodeMetricsAsync(_scopeFactory, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to refresh local node metrics");
            }

            await Task.Delay(RefreshInterval, stoppingToken);
        }
    }

    internal static async Task<bool> RefreshLocalNodeMetricsAsync(IServiceScopeFactory scopeFactory, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var capacity = scope.ServiceProvider.GetRequiredService<FleetCapacityReservationService>();
        var localNode = await context.WorkerNodes.FirstOrDefaultAsync(n => n.IsLocal, token);

        if (localNode is null)
            return false;

        var metrics = await SystemMetricsSampler.SampleAsync(token);

        localNode.CpuLoad = metrics.CpuLoad;
        localNode.MemoryLoad = metrics.MemoryLoad;
        var runningContainers = await context.Containers.CountAsync(
            c => c.Status == ContainerStatus.Running
                && (!c.NodeId.HasValue || c.NodeId == localNode.Id), token);
        var runningVms = await context.VmInstances.CountAsync(
            vm => vm.Status == VmInstanceStatus.Running
                && (!vm.NodeId.HasValue || vm.NodeId == localNode.Id), token);
        var runningTeamLabDockerAssets = await context.TeamLabRuntimeAssets.CountAsync(
            a => (a.WorkerNodeId == localNode.Id || a.Shard!.WorkerNodeId == localNode.Id)
                 && a.Runtime.Status == TeamLabRuntimeStatus.Running
                 && a.Kind == TeamLabResourceKind.Docker
                 && a.Status == TeamLabRuntimeStatus.Running, token);
        var runningTeamLabVmAssets = await context.TeamLabRuntimeAssets.CountAsync(
            a => (a.WorkerNodeId == localNode.Id || a.Shard!.WorkerNodeId == localNode.Id)
                 && a.Runtime.Status == TeamLabRuntimeStatus.Running
                 && a.Kind == TeamLabResourceKind.Vm
                 && a.Status == TeamLabRuntimeStatus.Running, token);

        localNode.CurrentContainers = runningContainers + runningTeamLabDockerAssets;
        localNode.CurrentVms = runningVms + runningTeamLabVmAssets;
        localNode.Status = NodeStatus.Online;
        localNode.LastHeartbeat = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(token);
        await capacity.ReconcileReservedAsync(localNode.Id, token);
        return true;
    }

    internal static class SystemMetricsSampler
    {
        internal static async Task<(float CpuLoad, float MemoryLoad)> SampleAsync(CancellationToken token)
        {
            var cpu = OperatingSystem.IsLinux()
                ? await ReadLinuxCpuLoadAsync(token)
                : await ReadWindowsCpuLoadAsync(token);
            var memory = OperatingSystem.IsLinux()
                ? await ReadLinuxMemoryLoadAsync(token)
                : await ReadWindowsMemoryLoadAsync(token);

            return (ClampRatio(cpu), ClampRatio(memory));
        }

        private static async Task<float> ReadLinuxCpuLoadAsync(CancellationToken token)
        {
            try
            {
                var first = await ReadLinuxCpuSnapshotAsync(token);
                await Task.Delay(500, token);
                var second = await ReadLinuxCpuSnapshotAsync(token);
                var totalDelta = second.Total - first.Total;
                var idleDelta = second.Idle - first.Idle;

                return totalDelta > 0
                    ? 1f - (float)idleDelta / totalDelta
                    : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static async Task<(ulong Idle, ulong Total)> ReadLinuxCpuSnapshotAsync(CancellationToken token)
        {
            var stat = await File.ReadAllTextAsync("/proc/stat", token);
            var cpuLine = stat.Split('\n').First(l => l.StartsWith("cpu ", StringComparison.Ordinal));
            var cols = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(ulong.Parse)
                .ToArray();
            var idle = cols[3] + (cols.Length > 4 ? cols[4] : 0);
            var total = cols.Aggregate(0UL, (sum, value) => sum + value);

            return (idle, total);
        }

        private static async Task<float> ReadLinuxMemoryLoadAsync(CancellationToken token)
        {
            try
            {
                var meminfo = await File.ReadAllTextAsync("/proc/meminfo", token);
                var lines = meminfo.Split('\n');
                var total = ParseMeminfoValue(lines.First(l => l.StartsWith("MemTotal:", StringComparison.Ordinal)));
                var available = ParseMeminfoValue(lines.First(l => l.StartsWith("MemAvailable:", StringComparison.Ordinal)));

                return total > 0
                    ? 1f - (float)available / total
                    : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static ulong ParseMeminfoValue(string line) =>
            ulong.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);

        private static async Task<float> ReadWindowsCpuLoadAsync(CancellationToken token)
        {
            var output = await RunPowerShellScalarAsync(
                "(Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average",
                token);

            return float.TryParse(output, out var value)
                ? value / 100f
                : 0f;
        }

        private static async Task<float> ReadWindowsMemoryLoadAsync(CancellationToken token)
        {
            var output = await RunPowerShellScalarAsync(
                "$os = Get-CimInstance Win32_OperatingSystem; 1 - ($os.FreePhysicalMemory / $os.TotalVisibleMemorySize)",
                token);

            return float.TryParse(output, out var value) ? value : 0f;
        }

        private static async Task<string> RunPowerShellScalarAsync(string command, CancellationToken token)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    ArgumentList =
                    {
                        "-NoProfile",
                        "-NonInteractive",
                        "-Command",
                        command
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (process is null)
                    return string.Empty;

                var output = await process.StandardOutput.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);

                return process.ExitCode == 0
                    ? output.Trim()
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static float ClampRatio(float value)
        {
            if (!float.IsFinite(value))
                return 0f;

            return Math.Clamp(value, 0f, 1f);
        }
    }
}
