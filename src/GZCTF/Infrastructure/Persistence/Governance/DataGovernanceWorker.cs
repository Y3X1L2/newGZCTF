using Microsoft.Extensions.Options;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed class DataGovernanceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DataRetentionOptions> options,
    ILogger<DataGovernanceWorker> logger) : BackgroundService
{
    private readonly string _leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.StartupDelaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.IntervalMinutes));
        do
        {
            try
            {
                await ExecuteCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Database governance cycle failed and will be retried.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ExecuteCycleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) != true)
            return;

        var leaseService = scope.ServiceProvider.GetRequiredService<PostgresGovernanceLease>();
        await using var lease = await leaseService.TryAcquireAsync(cancellationToken);
        if (lease is null)
            return;

        var executor = scope.ServiceProvider.GetRequiredService<DataRetentionExecutor>();
        await executor.ExecuteAsync(_leaseOwner, DateTimeOffset.UtcNow, cancellationToken);
    }
}
