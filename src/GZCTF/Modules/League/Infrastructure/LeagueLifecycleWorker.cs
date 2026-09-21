using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.League.Infrastructure;

public sealed class LeagueLifecycleWorker(IServiceScopeFactory scopes, IOptions<LeagueOptions> options,
    ILogger<LeagueLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                var ids = await db.Set<LeagueMatch>().AsNoTracking().Where(x => x.NextAttemptAt <= now &&
                    (((x.State == LeagueMatchState.Preparing || x.State == LeagueMatchState.Starting) &&
                      (x.OperationState == LeagueProgressState.Pending || x.OperationState == LeagueProgressState.Running)) ||
                     (x.State == LeagueMatchState.Ended && x.CleanupOperationId != null &&
                      (x.CleanupState == LeagueProgressState.Pending || x.CleanupState == LeagueProgressState.Running))))
                    .OrderBy(x => x.NextAttemptAt).Take(32).Select(x => x.Id).ToArrayAsync(stoppingToken);
                foreach (var id in ids)
                {
                    await using var itemScope = scopes.CreateAsyncScope();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    try { await itemScope.ServiceProvider.GetRequiredService<LeagueLifecycleService>().ProcessAsync(id, timeout.Token); }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning("League operation timed out for match {MatchId}; saved intent will be retried", id); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("League recovery scan failed; it will retry on the next tick"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
