using System.Collections.Concurrent;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabExecutionEventJournal
{
    readonly ConcurrentDictionary<(int RuntimeId, int Generation, string ShardKey), PlanState> plans = new();

    public bool TryGet(
        TeamLabExecutionPlanV2 plan,
        out TeamLabExecutionPlanApplyResponse response)
    {
        response = default!;
        return plans.TryGetValue(Key(plan), out var state) && state.TryCreateResponse(plan.PlanDigest, out response);
    }

    public void Save(TeamLabExecutionPlanV2 plan, TeamLabExecutionPlanApplyResponse response) =>
        plans[Key(plan)] = new PlanState(plan.PlanDigest, response);

    public void Remove(TeamLabExecutionPlanV2 plan) => plans.TryRemove(Key(plan), out _);

    static (int, int, string) Key(TeamLabExecutionPlanV2 plan) =>
        (plan.RuntimeId, plan.Generation, plan.ShardKey);

    sealed record PlanState(string Digest, TeamLabExecutionPlanApplyResponse Response)
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
