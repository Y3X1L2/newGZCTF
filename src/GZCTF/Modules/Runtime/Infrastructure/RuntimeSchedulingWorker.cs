using GZCTF.Modules.Runtime.Application;
using GZCTF.Services.Fleet;

namespace GZCTF.Modules.Runtime.Infrastructure;

public sealed class RuntimeSchedulingWorker(
    IServiceScopeFactory scopeFactory,
    IDeploymentQueueWakeup wakeup,
    ILogger<RuntimeSchedulingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<RuntimeSchedulingService>();
                var count = await service.SchedulePendingAsync(stoppingToken);
                poll = count > 0 ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(2);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Runtime scheduling iteration failed.");
                poll = TimeSpan.FromSeconds(2);
            }

            await wakeup.WaitAsync(poll, stoppingToken);
        }
    }
}
