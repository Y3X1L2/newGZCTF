using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;

namespace GZCTF.Modules.TeamLab.Application;

public enum TeamLabDeploymentStage : byte
{
    ArtifactsVerifying = 0,
    NetworkApplying = 1,
    RoutesApplying = 2,
    AssetBooting = 3,
    HealthProbing = 4,
    ObservationStarting = 5
}

/// <summary>
/// Physical realization of a TeamLab link policy. The control plane persists
/// desired state and this port turns it into a real network effect on the
/// runtime's worker node (tc netem / ip-link). The port is deliberately node
/// and infrastructure agnostic so the Application layer stays behind the
/// execution boundary.
/// </summary>
public interface ITeamLabLinkPolicyDispatcher
{
    Task<TeamLabLinkPolicyDispatchResult> ApplyAsync(
        Domain.Runtime.TeamLabRuntime runtime,
        string networkKey,
        string assetKey,
        string kind,
        string parameters,
        CancellationToken cancellationToken);

    Task<TeamLabLinkPolicyDispatchResult> RecoverAsync(
        Domain.Runtime.TeamLabRuntime runtime,
        string networkKey,
        string assetKey,
        string kind,
        CancellationToken cancellationToken);
}

public sealed record TeamLabLinkPolicyDispatchResult(bool Success, string Message);

public interface ITeamLabDeploymentProgress
{
    Task SetAsync(TeamLabDeploymentStage stage, string message, CancellationToken cancellationToken);
}

public interface ITeamLabArtifactDistribution
{
    Task EnsureImageAsync(
        int runtimeId,
        Guid workerNodeId,
        ImageTemplate template,
        CancellationToken cancellationToken);

    Task ReleaseRuntimeAsync(int runtimeId, CancellationToken cancellationToken);

    /// <summary>
    /// Releases release-scoped image distribution references (the preparation
    /// claim that keeps a release's images warm). Implementations must be
    /// idempotent so the "last consumer destroyed" path can call it freely.
    /// </summary>
    Task ReleaseTeamLabReleaseReferencesAsync(Guid releaseId, CancellationToken token);
}

public interface ITeamLabCaptureCleanup
{
    Task<IReadOnlyList<string>> ExpireGenerationAsync(
        int runtimeId,
        int generation,
        CancellationToken cancellationToken);
}

public sealed record TeamLabQueueTicketResult(Guid TicketId);

public sealed record TeamLabQueueRequest(
    int RuntimeId,
    int DockerSlots,
    int VmSlots,
    Guid? OwnerUserId,
    Guid? OperationId,
    Guid RuntimePublicId,
    WorkloadSchedulingIdentity Identity,
    string SubjectDisplayName,
    string ResourceDisplayName,
    // Required, not defaulted: capacity accounting keys reservations by the ticket's generation, so
    // a stale default silently double-counts a runtime whose create request was turned into a reset.
    int Generation,
    RuntimeOperationKind Operation = RuntimeOperationKind.Create,
    Guid? TargetNodeId = null,
    string? ProtectedPayload = null,
    string? PayloadHash = null);

public interface ITeamLabRuntimeQueue
{
    Task<TeamLabQueueTicketResult> EnqueueAsync(
        TeamLabQueueRequest request,
        CancellationToken cancellationToken);

    Task<TeamLabQueueTicketResult> EnqueueInCurrentTransactionAsync(
        TeamLabQueueRequest request,
        CancellationToken cancellationToken);

    Task NotifyAsync(Guid ticketId, CancellationToken cancellationToken);
}
