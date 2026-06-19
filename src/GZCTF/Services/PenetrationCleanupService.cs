namespace GZCTF.Services;

public class PenetrationCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<PenetrationCleanupService> logger) : BackgroundService
{
    static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var penetrationService = scope.ServiceProvider.GetRequiredService<PenetrationService>();
                var cleaned = await penetrationService.CleanupPendingEnvironments(stoppingToken);
                if (cleaned > 0)
                    logger.LogInformation("Cleaned {Count} pending penetration environments", cleaned);

                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Penetration cleanup service iteration failed");
            }
        }
    }
}
