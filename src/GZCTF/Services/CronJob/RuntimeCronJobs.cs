using GZCTF.Repositories.Interface;
using GZCTF.Infrastructure.Cache;

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

        foreach (var container in await containerRepo.GetDyingContainers(cancellationToken))
        {
            await containerRepo.DestroyContainer(container, cancellationToken);
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.CronJob_RemoveExpiredContainer),
                    container.LogId],
                TaskStatus.Success, LogLevel.Debug);
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
