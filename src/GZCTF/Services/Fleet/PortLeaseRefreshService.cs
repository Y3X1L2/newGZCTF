using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.Fleet;

public class PortLeaseRefreshService : BackgroundService
{
    readonly IServiceScopeFactory _scopeFactory;
    readonly NginxProxyConfig _config;
    readonly ILogger<PortLeaseRefreshService> _logger;

    public PortLeaseRefreshService(IServiceScopeFactory scopeFactory, IOptions<ContainerProvider> containerProvider,
        ILogger<PortLeaseRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = containerProvider.Value.NginxProxyConfig ?? new NginxProxyConfig();
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enable || _config.SyncLocalConfig)
            return;

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
                _logger.LogWarning(ex, "Failed to refresh external Nginx public port leases.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    public async Task RefreshOnceAsync(CancellationToken token)
    {
        if (!_config.Enable)
            return;

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IContainerRepository>();
        var allocator = scope.ServiceProvider.GetRequiredService<IPortAllocationService>();
        var mappings = await repository.GetProxyPortMappingsAsync(token);
        var refreshed = 0;

        foreach (var mapping in mappings
                     .Where(m => m.PublicPort >= _config.ListenPortStart && m.PublicPort <= _config.ListenPortEnd))
        {
            if (await allocator.ReserveExistingPortAsync(mapping.PublicPort, mapping.LeaseId, token))
                refreshed++;
        }

        _logger.LogDebug("Refreshed {Count} external Nginx public port lease(s).", refreshed);
    }
}
