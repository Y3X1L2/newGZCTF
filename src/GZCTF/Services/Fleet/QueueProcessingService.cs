using Microsoft.Extensions.Hosting;

namespace GZCTF.Services.Fleet;

public class QueueProcessingService : BackgroundService
{
    private readonly QueueManager _queueManager;
    private readonly ILogger<QueueProcessingService> _logger;
    private static readonly TimeSpan ProcessInterval = TimeSpan.FromSeconds(30);

    public QueueProcessingService(QueueManager queueManager, ILogger<QueueProcessingService> logger)
    {
        _queueManager = queueManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
}
