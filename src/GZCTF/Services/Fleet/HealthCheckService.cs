using GZCTF.Repositories.Interface;
using Microsoft.Extensions.Hosting;

namespace GZCTF.Services.Fleet;

public class FleetHealthCheckService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FleetHealthCheckService> _logger;
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public FleetHealthCheckService(IServiceScopeFactory scopeFactory, ILogger<FleetHealthCheckService> logger)
    { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<INodeRepository>();
                var count = await repo.MarkStaleNodesOfflineAsync(HeartbeatTimeout, stoppingToken);
                if (count > 0) _logger.LogWarning("Marked {Count} stale node(s) offline", count);
            }
            catch (Exception ex) { _logger.LogError(ex, "Health check error"); }
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
