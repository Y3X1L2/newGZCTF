using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class ImageDistributionReconcileService(
    IServiceScopeFactory scopeFactory,
    ILogger<ImageDistributionReconcileService> logger) : BackgroundService
{
    static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await ReconcileOnceAsync(stoppingToken);

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    async Task ReconcileOnceAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var distribution = scope.ServiceProvider.GetRequiredService<ImageDistributionService>();
        var now = DateTimeOffset.UtcNow;

        var endedGameIds = await context.Games.AsNoTracking()
            .Where(g => g.EndTimeUtc < now)
            .Select(g => g.Id)
            .ToArrayAsync(token);

        foreach (var gameId in endedGameIds)
        {
            try
            {
                await distribution.ReleaseGameReferencesAsync(gameId, token);
            }
            catch (Exception ex) when (ex is InvalidOperationException or AgentClientException or HttpRequestException)
            {
                logger.LogWarning(ex,
                    "Failed to release image distribution references for ended game {GameId}.",
                    gameId);
            }
        }

        try
        {
            await distribution.CleanupUnreferencedAsync(token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AgentClientException or HttpRequestException)
        {
            logger.LogWarning(ex, "Failed to cleanup unreferenced image distribution records.");
        }
    }
}
