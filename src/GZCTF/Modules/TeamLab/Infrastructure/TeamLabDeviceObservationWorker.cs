using GZCTF.Infrastructure.Persistence.Governance;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class AgentTeamLabDeviceObserver(AgentClient agent) : ITeamLabDeviceObserver
{
    public Task<TeamLabDeviceObservation?> ProbeAsync(Guid nodeId, TeamLabDeviceProbeRequest request, CancellationToken token) =>
        agent.ProbeTeamLabDeviceAsync(nodeId, request, token);
}

public sealed class TeamLabDeviceObservationWorker(IServiceScopeFactory scopes, ILogger<TeamLabDeviceObservationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(token))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var leases = scope.ServiceProvider.GetRequiredService<PostgresGovernanceLease>();
                await using var lease = await leases.TryAcquireAsync(0x544C444556494345, token);
                if (lease is null) continue;
                await scope.ServiceProvider.GetRequiredService<TeamLabDeviceObservationService>().ScanAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "TeamLab device observation cycle failed."); }
        }
    }
}
