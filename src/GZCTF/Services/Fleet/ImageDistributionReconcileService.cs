using Microsoft.EntityFrameworkCore;
using GZCTF.Modules.Runtime.Domain;

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
            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Image distribution reconciliation failed; the next scheduled pass will retry.");
            }

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

        var endedGameIds = await context.ImageDistributionReferences.AsNoTracking()
            .Where(reference => reference.Kind == ImageDistributionReferenceKind.Game)
            .Join(context.Games.AsNoTracking().Where(game => game.EndTimeUtc < now),
                reference => reference.ResourceId,
                game => game.Id,
                (_, game) => game.Id)
            .Distinct()
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
