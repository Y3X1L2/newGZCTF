using GZCTF.Modules.Runtime.Application;

namespace GZCTF.Modules.Runtime.Infrastructure;

public sealed class RuntimeExecutionWorker(
    RuntimeExecutionService execution,
    IDeploymentQueueWakeup wakeup,
    ILogger<RuntimeExecutionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromMilliseconds(250);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var count = await execution.ExecuteScheduledAsync(stoppingToken);
                    poll = count > 0 ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(1);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Runtime execution iteration failed.");
                    poll = TimeSpan.FromSeconds(1);
                }

                await wakeup.WaitAsync(poll, stoppingToken);
            }
        }
        finally
        {
            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await execution.WaitForIdleAsync(drain.Token);
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Runtime execution tasks did not stop within the shutdown grace period.");
            }
        }
    }
}
