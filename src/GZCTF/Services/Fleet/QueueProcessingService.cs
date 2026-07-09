using Microsoft.Extensions.Hosting;

namespace GZCTF.Services.Fleet;

public class QueueProcessingService : BackgroundService
{
    private readonly QueueManager _queueManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueueProcessingService> _logger;
    private static readonly TimeSpan ProcessInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CreatingRecoveryThreshold = TimeSpan.FromMinutes(10);

    public QueueProcessingService(QueueManager queueManager, IServiceScopeFactory scopeFactory,
        ILogger<QueueProcessingService> logger)
    {
        _queueManager = queueManager;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverStaleCreatingTicketsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await _queueManager.ProcessPendingAsync(stoppingToken);
                if (processed > 0)
                    _logger.LogInformation("Processed {Count} queued deployment(s)", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deployment queue");
            }

            await Task.Delay(ProcessInterval, stoppingToken);
        }
    }

    async Task RecoverStaleCreatingTicketsAsync(CancellationToken token)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<DeploymentQueueService>();
            var recovered = await queue.RecoverStaleCreatingTicketsAsync(CreatingRecoveryThreshold, token);
            if (recovered > 0)
                _logger.LogWarning("Recovered {Count} stale Creating deployment queue ticket(s)", recovered);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering stale deployment queue tickets");
        }
    }
}
