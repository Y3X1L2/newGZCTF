using GZCTF.Repositories.Interface;
using GZCTF.Infrastructure.Cache;
using GZCTF.Services.Fleet;

namespace GZCTF.Services.CronJob;

public static class RuntimeCronJobs
{
    [CronJob("*/3 * * * *")]
    public static async Task ContainerChecker(
        AsyncServiceScope scope,
        ILogger<CronJobService> logger,
        CancellationToken cancellationToken)
    {
        var containerRepo = scope.ServiceProvider.GetRequiredService<IContainerRepository>();
        var queue = scope.ServiceProvider.GetRequiredService<DeploymentQueueService>();

        foreach (var container in await containerRepo.GetDyingContainers(cancellationToken))
        {
            await queue.EnqueueAsync(DeploymentQueueRequest.MaintenanceContainer(
                container.Id, container.NodeId, container.Image), cancellationToken);
            logger.SystemLog(
                $"Expired container cleanup queued: container={container.Id}, node={container.NodeId}, image={container.Image}.",
                TaskStatus.Pending, LogLevel.Debug);
        }
    }

    [CronJob("0 * * * *")]
    public static async Task FlushRecentGames(
        AsyncServiceScope scope,
        ILogger<CronJobService> logger,
        CancellationToken cancellationToken)
    {
        var helper = scope.ServiceProvider.GetRequiredService<IPlatformCache>();

        await helper.InvalidateAsync(CachePolicyCatalog.RecentGames, "global", cancellationToken);
    }
}
