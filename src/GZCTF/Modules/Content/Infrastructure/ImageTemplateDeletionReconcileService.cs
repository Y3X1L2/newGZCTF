using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class ImageTemplateDeletionReconciler(
    AppDbContext context,
    IImageTemplateCatalog catalog,
    ILogger<ImageTemplateDeletionReconciler> logger)
{
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        var templateIds = await context.ImageTemplates.AsNoTracking()
            .Where(template => template.Status == ImageStatus.Deleting)
            .OrderBy(template => template.Id)
            .Select(template => template.Id)
            .ToArrayAsync(cancellationToken);

        var completed = 0;
        foreach (var templateId in templateIds)
        {
            try
            {
                await catalog.CompleteDeletionAsync(templateId, cancellationToken);
                completed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Image template deletion reconciliation failed for template {TemplateId}",
                    templateId);
            }
        }

        return completed;
    }
}

public sealed class ImageTemplateDeletionReconcileService(
    IServiceScopeFactory scopeFactory,
    ILogger<ImageTemplateDeletionReconcileService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<ImageTemplateDeletionReconciler>();
                var completed = await reconciler.ReconcileAsync(stoppingToken);
                if (completed > 0)
                    logger.LogInformation(
                        "Completed {Count} pending image template deletions", completed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to reconcile pending image template deletions");
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
