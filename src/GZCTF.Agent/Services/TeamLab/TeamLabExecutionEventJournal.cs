using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabExecutionEventJournal
{
    static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    const int MaximumEntries = 4096;
    readonly object sync = new();
    readonly Dictionary<(int RuntimeId, int Generation, string ShardKey), PlanState> plans = [];

    public bool TryGet(
        TeamLabExecutionPlanV2 plan,
        out TeamLabExecutionPlanApplyResponse response)
    {
        lock (sync)
        {
            Prune();
            response = default!;
            return plans.TryGetValue(Key(plan), out var state) &&
                   state.TryCreateResponse(plan.PlanDigest, out response);
        }
    }

    public bool TryGetIdentity(TeamLabExecutionPlanV2 plan, out string digest)
    {
        lock (sync)
        {
            Prune();
            if (plans.TryGetValue(Key(plan), out var state))
            {
                digest = state.Digest;
                return true;
            }
            digest = string.Empty;
            return false;
        }
    }

    public bool ContainsIdentity(TeamLabExecutionPlanV2 plan)
    {
        lock (sync)
        {
            Prune();
            return plans.ContainsKey(Key(plan));
        }
    }

    public void Save(TeamLabExecutionPlanV2 plan, TeamLabExecutionPlanApplyResponse response)
    {
        lock (sync)
        {
            Prune();
            if (plans.Count >= MaximumEntries && !plans.ContainsKey(Key(plan)))
                plans.Remove(plans.MinBy(item => item.Value.UpdatedAt).Key);
            plans[Key(plan)] = new PlanState(plan.PlanDigest, response, DateTimeOffset.UtcNow);
        }
    }

    public void Remove(TeamLabExecutionPlanV2 plan)
    {
        lock (sync)
            plans.Remove(Key(plan));
    }

    static (int, int, string) Key(TeamLabExecutionPlanV2 plan) =>
        (plan.RuntimeId, plan.Generation, plan.ShardKey);

    void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow - Retention;
        var expired = plans
            .Where(item => item.Value.UpdatedAt < cutoff)
            .Select(item => item.Key)
            .ToArray();
        foreach (var key in expired)
            plans.Remove(key);
    }

    sealed record PlanState(string Digest, TeamLabExecutionPlanApplyResponse Response, DateTimeOffset UpdatedAt)
    {
        public bool TryCreateResponse(string requestedDigest, out TeamLabExecutionPlanApplyResponse response)
        {
            if (!string.Equals(requestedDigest, Digest, StringComparison.Ordinal))
            {
                response = default!;
                return false;
            }

            response = Response with { AlreadyApplied = true };
            return true;
        }
    }
}
