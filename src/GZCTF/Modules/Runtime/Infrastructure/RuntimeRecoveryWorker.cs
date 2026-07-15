using GZCTF.Infrastructure.Persistence.Governance;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Application;

namespace GZCTF.Modules.Runtime.Infrastructure;

public sealed class RuntimeRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RuntimeRecoveryWorker> logger) : BackgroundService
{
    private const long AdvisoryLockKey = 0x475A435446524543;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
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
                logger.LogError(exception, "Runtime recovery cycle failed and will be retried.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ExecuteCycleAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) != true)
            return;
        var leaseProvider = scope.ServiceProvider.GetRequiredService<PostgresGovernanceLease>();
        await using var lease = await leaseProvider.TryAcquireAsync(AdvisoryLockKey, token);
        if (lease is null)
            return;

        var runId = Guid.CreateVersion7();
        var correlation = scope.ServiceProvider.GetRequiredService<OperationalCorrelation>();
        using var correlationScope = correlation.Begin(runId);
        var events = scope.ServiceProvider.GetRequiredService<IOperationalEventWriter>();
        await events.AppendAndSaveAsync(new OperationalEventDraft(
            OperationalEventCodes.Recovery.RunStarted,
            OperationalEventOutcome.Started,
            "Runtime fact reconciliation started.",
            CorrelationId: runId,
            SubjectType: "runtime-recovery",
            SubjectId: runId.ToString(),
            ResourceType: "runtime-recovery-run",
            ResourceId: runId.ToString()), token);

        try
        {
            var service = scope.ServiceProvider.GetRequiredService<RuntimeFactReconciliationService>();
            var summary = await service.ReconcileAsync(runId, StaleAfter, token);
            await events.AppendAndSaveAsync(new OperationalEventDraft(
                OperationalEventCodes.Recovery.RunSucceeded,
                OperationalEventOutcome.Succeeded,
                "Runtime fact reconciliation completed.",
                CorrelationId: runId,
                Detail: new Dictionary<string, object?>
                {
                    ["matchedCount"] = summary.MatchedCount,
                    ["missingCount"] = summary.MissingCount,
                    ["conflictCount"] = summary.ConflictCount,
                    ["orphanCount"] = summary.OrphanCount,
                    ["deferredCount"] = summary.DeferredCount,
                    ["correctedCount"] = summary.CorrectedCount,
                    ["replayedCount"] = summary.ReplayedCount
                },
                SubjectType: "runtime-recovery",
                SubjectId: runId.ToString(),
                ResourceType: "runtime-recovery-run",
                ResourceId: runId.ToString()), token);
        }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            var error = OperationalErrorClassifier.FromException(exception, "runtime.recover");
            await events.AppendAndSaveAsync(new OperationalEventDraft(
                OperationalEventCodes.Recovery.RunFailed,
                OperationalEventOutcome.Failed,
                "Runtime fact reconciliation failed.",
                OperationalEventSeverity.Error,
                runId,
                error.Category,
                error.Code,
                error.Retryable,
                SubjectType: "runtime-recovery",
                SubjectId: runId.ToString(),
                ResourceType: "runtime-recovery-run",
                ResourceId: runId.ToString()), token);
            throw;
        }
    }
}
