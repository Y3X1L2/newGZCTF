using GZCTF.Models;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

/// <summary>
/// Closes capability-resource lifecycles that no longer have an owner:
/// connector leases and link policies of destroyed runtimes, and link
/// policies whose scheduled recovery time has passed. Bounded batches keep
/// the pass cheap and every step is idempotent.
/// </summary>
public sealed class TeamLabCapabilityResourceWorker(
    IServiceScopeFactory scopes,
    ILogger<TeamLabCapabilityResourceWorker> logger) : BackgroundService
{
    private const int BatchLimit = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var connectors = scope.ServiceProvider.GetRequiredService<TeamLabConnectorService>();
                var runtimeIds = await context.TeamLabConnectorLeases.AsNoTracking()
                    .Where(lease => lease.ReleasedAt == null && context.TeamLabRuntimes.Any(runtime =>
                        runtime.Id == lease.RuntimeId && runtime.Status == TeamLabRuntimeStatus.Destroyed))
                    .OrderBy(lease => lease.Id)
                    .Take(BatchLimit)
                    .Select(lease => lease.RuntimeId)
                    .Distinct()
                    .ToArrayAsync(stoppingToken);
                foreach (var runtimeId in runtimeIds)
                    await connectors.ReleaseRuntimeLeasesAsync(
                        runtimeId, TeamLabConnectorLeaseReleaseReason.RuntimeDestroyed, stoppingToken);
                var policies = scope.ServiceProvider.GetRequiredService<TeamLabLinkPolicyService>();
                await policies.RecoverDueAsync(BatchLimit, stoppingToken);
                await policies.CloseDestroyedRuntimePoliciesAsync(BatchLimit, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "TeamLab 能力资源生命周期收敛失败"); }
        }
    }
}
