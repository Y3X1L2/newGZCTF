using GZCTF.Modules.TeamLab.Application;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabTrafficPathWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TeamLabTrafficPathWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TeamLabTrafficPathCorrelator>()
                    .CorrelatePendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "TeamLab traffic path correlation cycle failed");
            }
        }
    }
}
