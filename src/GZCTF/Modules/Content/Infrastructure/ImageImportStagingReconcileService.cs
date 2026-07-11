using GZCTF.Models;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class ImageImportStagingReconciler(
    AppDbContext context,
    IImageImportStagingStore staging)
{
    internal static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromHours(1);

    public async Task<int> ReconcileAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activePaths = await (
                from job in context.ImageImportJobs.AsNoTracking()
                join operation in context.ApiOperations.AsNoTracking()
                    on job.OperationId equals operation.Id
                where job.SourceKind == ImageImportSourceKind.DockerArchive &&
                      job.StagedPath != null &&
                      job.StagedPath != string.Empty &&
                      (operation.Status == ApiOperationStatus.Pending ||
                       operation.Status == ApiOperationStatus.Running)
                select job.StagedPath)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return await staging.DeleteUnreferencedAsync(
            activePaths!
                .ToHashSet(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal),
            now - OrphanGracePeriod,
            cancellationToken);
    }
}

public sealed class ImageImportStagingReconcileService(
    IServiceScopeFactory scopeFactory,
    ILogger<ImageImportStagingReconcileService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<ImageImportStagingReconciler>();
                var removed = await reconciler.ReconcileAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (removed > 0)
                    logger.LogInformation(
                        "Removed {Count} unreferenced image import staging files", removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to reconcile image import staging files");
            }
        } while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(
        PeriodicTimer timer,
        CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
