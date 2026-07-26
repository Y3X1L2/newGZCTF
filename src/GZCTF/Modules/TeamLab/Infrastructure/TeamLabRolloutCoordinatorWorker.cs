using GZCTF.Modules.TeamLab.Application.Rollouts;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabRolloutCoordinatorWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TeamLabRolloutCoordinatorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<TeamLabRolloutCoordinator>();
                await coordinator.ProcessBatchAsync(8, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "TeamLab rollout coordinator tick failed.");
            }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
