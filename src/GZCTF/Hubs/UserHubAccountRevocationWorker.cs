using Microsoft.EntityFrameworkCore;

namespace GZCTF.Hubs;

/// <summary>Also closes connections held by another main process after account revocation.</summary>
public sealed class UserHubAccountRevocationWorker(UserHubAccountConnections connections,
    IServiceScopeFactory scopes, ILogger<UserHubAccountRevocationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var ids = connections.UserIds;
                if (ids.Length == 0) continue;
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var active = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) &&
                    u.DeletedAtUtc == null && u.Role != Role.Banned).Select(u => u.Id).ToArrayAsync(stoppingToken);
                foreach (var id in ids.Except(active)) connections.Abort(id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("UserHub account revocation check requires retry");
            }
        }
    }
}
