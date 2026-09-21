using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.League.Application;

public sealed class LeagueLifecycleService(AppDbContext db, LeagueMatchStore store, ILeagueRuntimePort runtime,
    ILeagueFlagPort flags, ILeagueCoinPort coins)
{
    public async Task ProcessAsync(Guid matchId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var m = await store.LockAsync(matchId, ct);
        if (!NeedsWork(m)) return;
        try
        {
            var frozen = LeagueMatchStore.Frozen(m);
            if (m.State == LeagueMatchState.Ended)
            {
                m.CleanupAttemptCount++;
                if (!runtime.IsAvailable) ApplyCleanup(m, new(false, LeagueFailure.DependencyUnavailable));
                else ApplyCleanup(m, await runtime.CleanupAsync(frozen, m.CleanupOperationId!.Value, ct));
            }
            else
            {
                m.AttemptCount++;
                if (!runtime.IsAvailable || !flags.IsAvailable || !coins.IsAvailable)
                    Fail(m, LeagueFailure.DependencyUnavailable, true);
                else if (m.State == LeagueMatchState.Preparing) await PrepareAsync(m, frozen, ct);
                else
                {
                    var bindings = Bindings(m);
                    if (!await flags.ConfirmBindingsAsync(frozen, bindings, ct)) Fail(m, LeagueFailure.FlagBindingFailed, true);
                    else
                    {
                        var result = await runtime.OpenAccessAsync(frozen, m.StartOperationId!.Value, bindings, ct);
                        if (result.Failure != LeagueFailure.None) Fail(m, result.Failure, result.Retryable);
                        else if (result.Completed)
                        {
                            m.State = LeagueMatchState.Running;
                            m.StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                            m.OperationState = LeagueProgressState.Ready; m.Revision++;
                            foreach (var team in m.Registrations.Where(x => x.Selected)) team.AccessClosed = false;
                        }
                        else m.OperationState = LeagueProgressState.Running;
                    }
                }
            }
            m.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(5);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Roll back provider-local writes too. Never persist/log provider exception messages (may contain secrets).
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            await using var failureTx = await db.Database.BeginTransactionAsync(ct);
            m = await store.LockAsync(matchId, ct);
            if (NeedsWork(m))
            {
                if (m.State == LeagueMatchState.Ended) { m.CleanupAttemptCount++; ApplyCleanup(m, new(false, LeagueFailure.ProviderUnavailable)); }
                else { m.AttemptCount++; Fail(m, LeagueFailure.ProviderUnavailable, true); }
                await db.SaveChangesAsync(ct);
            }
            await failureTx.CommitAsync(ct);
        }
    }

    public static bool NeedsWork(LeagueMatch m) =>
        (m.State is LeagueMatchState.Preparing or LeagueMatchState.Starting &&
         m.OperationState is LeagueProgressState.Pending or LeagueProgressState.Running) ||
        (m.State == LeagueMatchState.Ended && m.CleanupOperationId.HasValue &&
         m.CleanupState is LeagueProgressState.Pending or LeagueProgressState.Running);

    private async Task PrepareAsync(LeagueMatch m, LeagueFrozenMatch frozen, CancellationToken ct)
    {
        await coins.InitializeAsync(frozen, ct);
        var cores = await flags.PrepareAsync(frozen, ct);
        if (cores.Count != 2 || cores.Any(x => x.MaterialId == Guid.Empty || x.CoreKey != "core") ||
            !cores.Select(x => x.TeamId).Order().SequenceEqual(frozen.Teams.Select(x => x.TeamId).Order()) ||
            cores.Select(x => x.MaterialId).Distinct().Count() != 2)
        { Fail(m, LeagueFailure.InvalidProviderResponse, false); return; }
        var result = await runtime.PrepareAsync(frozen, cores, ct);
        if (result.OperationId == Guid.Empty || (m.ProviderOperationId.HasValue && m.ProviderOperationId != result.OperationId) ||
            result.Teams.Count != 2 || !result.Teams.Select(x => x.TeamId).Order().SequenceEqual(frozen.Teams.Select(x => x.TeamId).Order()) ||
            result.Teams.Any(x => !Enum.IsDefined(x.State) || !Enum.IsDefined(x.Failure) ||
                (x.Binding is { } b && (b.TeamId != x.TeamId || b.RuntimeId == Guid.Empty || b.Generation < 1))) ||
            result.Teams.Where(x => x.Binding != null).Select(x => x.Binding!.RuntimeId).Distinct().Count() != result.Teams.Count(x => x.Binding != null))
        { Fail(m, LeagueFailure.InvalidProviderResponse, false); return; }
        m.ProviderOperationId = result.OperationId;
        foreach (var progress in result.Teams)
        {
            var r = m.Registrations.Single(x => x.TeamId == progress.TeamId);
            // A retry may finish an existing runtime, but must not silently replace a bound generation.
            if (r.RuntimeId.HasValue && (r.RuntimeId != progress.Binding?.RuntimeId || r.Generation != progress.Binding?.Generation))
            { Fail(m, LeagueFailure.InvalidProviderResponse, false); return; }
            r.PreparationState = progress.State; r.RuntimeId = progress.Binding?.RuntimeId; r.Generation = progress.Binding?.Generation;
            r.EnvironmentReady = progress.EnvironmentReady; r.FlagInjected = progress.FlagInjected;
            r.EntryPrepared = progress.EntryPrepared; r.AccessClosed = progress.AccessClosed;
            r.Failure = progress.Failure; r.Retryable = progress.Retryable;
        }
        var failures = result.Teams.Where(x => x.State == LeagueProgressState.Failed || x.Failure != LeagueFailure.None).ToArray();
        if (failures.Length > 0) { Fail(m, LeagueFailure.EnvironmentFailed, failures.All(x => x.Retryable)); return; }
        if (result.Teams.Any(x => x.State != LeagueProgressState.Ready || x.Binding == null || !x.EnvironmentReady || !x.FlagInjected || !x.EntryPrepared || !x.AccessClosed))
        { m.OperationState = LeagueProgressState.Running; return; }
        if (!await flags.ConfirmBindingsAsync(frozen, Bindings(m), ct))
        { Fail(m, LeagueFailure.FlagBindingFailed, true); return; }
        m.State = LeagueMatchState.Ready; m.OperationState = LeagueProgressState.Ready;
        m.Failure = LeagueFailure.None; m.Revision++;
    }

    private static LeagueRuntimeBinding[] Bindings(LeagueMatch m) => m.Registrations.Where(x => x.Selected)
        .Select(x => new LeagueRuntimeBinding(x.TeamId, x.RuntimeId!.Value, x.Generation!.Value)).ToArray();
    private static void Fail(LeagueMatch m, LeagueFailure failure, bool retryable)
    { m.OperationState = LeagueProgressState.Failed; m.Failure = Enum.IsDefined(failure) ? failure : LeagueFailure.InvalidProviderResponse; m.Retryable = retryable; }
    private static void ApplyCleanup(LeagueMatch m, LeagueEffectResult result)
    {
        m.CleanupState = result.Failure != LeagueFailure.None ? LeagueProgressState.Failed : result.Completed ? LeagueProgressState.Ready : LeagueProgressState.Running;
        m.CleanupFailure = Enum.IsDefined(result.Failure) ? result.Failure : LeagueFailure.InvalidProviderResponse;
        m.CleanupRetryable = result.Retryable;
        if (m.CleanupState == LeagueProgressState.Ready)
            foreach (var team in m.Registrations.Where(x => x.Selected)) team.AccessClosed = true;
    }
}
