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
        var resume = CanResumeExistingGeneration(runtime, ticket, now);
        if (!resume.Allowed) return resume;
        if (!_config.EnableStatelessAutoRecovery)
            return TeamLabRecoveryDecision.Deny("Automatic workload rebuild is disabled by default.");
        if (!inventoryProvesMissing)
            return TeamLabRecoveryDecision.Deny("Worker inventory did not prove the workload is absent.");
        if (!asset.Stateless)
            return TeamLabRecoveryDecision.Deny("Stateful TeamLab assets are never rebuilt automatically.");
        if (string.IsNullOrWhiteSpace(asset.ImageDigest))
            return TeamLabRecoveryDecision.Deny("Immutable image digest is missing.");
        if (asset.BootstrapDigest is not null && string.IsNullOrWhiteSpace(asset.BootstrapDigest))
            return TeamLabRecoveryDecision.Deny("Immutable bootstrap digest is missing.");
        if (!hasActiveReservation)
            return TeamLabRecoveryDecision.Deny("No active replacement capacity reservation exists.");
        return TeamLabRecoveryDecision.Allow("Stateless asset can be rebuilt from immutable inputs.");
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
