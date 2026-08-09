using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;

namespace GZCTF.Modules.TeamLab.Application;

public enum TeamLabDeploymentStage : byte
{
    ArtifactsVerifying = 0,
    NetworkApplying = 1,
    RoutesApplying = 2,
    AssetBooting = 3,
    BootstrapInjecting = 4,
    HealthProbing = 5,
    ObservationStarting = 6
}

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
}
