using System.Threading.Channels;
using GZCTF.Models;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Audit.Infrastructure;

public sealed class ExternalApiAuditWriter(
    IServiceScopeFactory scopeFactory,
    ILogger<ExternalApiAuditWriter> logger) : BackgroundService
{
    private const int BatchSize = 256;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(20);
    private readonly Channel<ExternalApiRequestAudit> _queue = Channel.CreateBounded<ExternalApiRequestAudit>(
        new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(ExternalApiRequestAudit audit) => _queue.Writer.WriteAsync(audit);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ExternalApiRequestAudit>(BatchSize);
        while (await _queue.Reader.WaitToReadAsync())
        {
            batch.Clear();
            using var flush = new CancellationTokenSource(FlushInterval);
            while (batch.Count < BatchSize)
            {
                while (batch.Count < BatchSize && _queue.Reader.TryRead(out var audit))
                    batch.Add(audit);
                if (batch.Count >= BatchSize || flush.IsCancellationRequested)
                    break;
                try
                {
                    if (!await _queue.Reader.WaitToReadAsync(flush.Token))
                        break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            while (!await PersistAsync(batch))
                await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private async Task<bool> PersistAsync(IReadOnlyCollection<ExternalApiRequestAudit> batch)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.ExternalApiRequestAudits.AddRange(batch);
            await database.SaveChangesAsync();
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to persist a batch of {Count} external API request audits", batch.Count);
            return false;
        }
    }
}
