using GZCTF.Repositories.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public class PortLeaseRefreshService : BackgroundService
{
    readonly IServiceScopeFactory _scopeFactory;
    readonly ILogger<PortLeaseRefreshService> _logger;

    public PortLeaseRefreshService(IServiceScopeFactory scopeFactory, ILogger<PortLeaseRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh public port leases.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    public async Task RefreshOnceAsync(CancellationToken token)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IContainerRepository>();
        var allocator = scope.ServiceProvider.GetRequiredService<IPortAllocationService>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mappings = await repository.GetProxyPortMappingsAsync(token);
        var servicePorts = await context.TeamLabServiceAccesses.AsNoTracking()
            .Where(item => item.RevokedAt == null && item.Status == "active")
            .Select(item => new { item.PublicPort, LeaseId = item.PortLeaseId })
            .ToArrayAsync(token);
        var range = allocator.CurrentRange;
        var refreshed = 0;

        foreach (var mapping in mappings.Select(item => new { item.PublicPort, item.LeaseId })
                     .Concat(servicePorts).Where(item => item.PublicPort >= range.Start && item.PublicPort <= range.End))
        {
            if (await allocator.ReserveExistingPortAsync(mapping.PublicPort, mapping.LeaseId, token))
                refreshed++;
        }

        _logger.LogDebug("Refreshed {Count} public port lease(s).", refreshed);
    }
}
