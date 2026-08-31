using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record TeamLabRecoveryDecision(bool Allowed, string Reason)
{
    public static TeamLabRecoveryDecision Allow(string reason) => new(true, reason);
    public static TeamLabRecoveryDecision Deny(string reason) => new(false, reason);
}

public sealed class TeamLabRuntimeRecoveryPolicy(IOptions<TeamLabNetworkConfig> options)
{
    private readonly TeamLabNetworkConfig _config = options.Value;

    public TeamLabRecoveryDecision CanResumeExistingGeneration(
        TeamLabRuntime runtime,
        DeploymentQueueTicket ticket,
        DateTimeOffset now)
    {
        if (ticket.Generation != runtime.Generation)
            return TeamLabRecoveryDecision.Deny("Recovery ticket generation is stale.");
        if (runtime.Status is TeamLabRuntimeStatus.Destroying or TeamLabRuntimeStatus.Destroyed or
            TeamLabRuntimeStatus.CleanupPending)
            return TeamLabRecoveryDecision.Deny("Runtime lifecycle does not allow deployment replay.");
        if (ticket.StartedAt is { } startedAt &&
            startedAt > now - TimeSpan.FromSeconds(Math.Max(5, _config.RecoveryGraceSeconds)))
            return TeamLabRecoveryDecision.Deny("Recovery grace period has not elapsed.");
        return TeamLabRecoveryDecision.Allow("Persisted generation and native identities can resume.");
    }

    public TeamLabRecoveryDecision CanRebuildMissingAsset(
        TeamLabRuntime runtime,
        DeploymentQueueTicket ticket,
        TeamLabRuntimeAsset asset,
        bool inventoryProvesMissing,
        bool hasActiveReservation,
        DateTimeOffset now)
    {
        return TeamLabRecoveryDecision.Deny(
            "缺失资产必须由管理员显式重建，平台不会自动替换运行中的场景资产。");
    }

    public TeamLabRecoveryDecision CanReplayInfrastructure(
        TeamLabRuntime runtime,
        DeploymentQueueTicket ticket,
        bool inventoryProvesDrift,
        bool routeFactsComplete,
        bool accessFactsIntact,
        DateTimeOffset now)
    {
        var resume = CanResumeExistingGeneration(runtime, ticket, now);
        if (!resume.Allowed) return resume;
        if (!inventoryProvesDrift)
            return TeamLabRecoveryDecision.Deny("Worker inventory did not prove infrastructure drift.");
        if (!routeFactsComplete)
            return TeamLabRecoveryDecision.Deny("Route version or desired-state digest is missing.");
        if (!accessFactsIntact)
            return TeamLabRecoveryDecision.Deny("WireGuard access identity facts are incomplete.");
        return TeamLabRecoveryDecision.Allow(
            "Infrastructure desired state can be replayed without changing player WireGuard identity.");
    }
}
