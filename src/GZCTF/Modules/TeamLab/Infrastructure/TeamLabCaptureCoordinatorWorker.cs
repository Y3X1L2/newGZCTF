using GZCTF.Modules.TeamLab.Application;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabCaptureCoordinatorWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TeamLabCaptureCoordinatorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TeamLabCaptureCoordinator>()
                    .ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "TeamLab 抓包协调周期执行失败");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
