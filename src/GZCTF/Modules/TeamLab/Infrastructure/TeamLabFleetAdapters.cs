using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabDeploymentStageMachine(
    AppDbContext context,
    DeploymentExecutionContextAccessor executionContext) : ITeamLabDeploymentProgress
{
    public async Task SetAsync(
        TeamLabDeploymentStage stage,
        string message,
        CancellationToken cancellationToken)
    {
        if (executionContext.Current?.TicketId is not { } ticketId) return;
        var ticket = await context.DeploymentQueueTickets.SingleOrDefaultAsync(
            item => item.Id == ticketId, cancellationToken);
        if (ticket is null) return;
        ticket.Stage = Map(stage);
        ticket.StageMessage = message.Length <= 512 ? message : message[..512];
        await context.SaveChangesAsync(cancellationToken);
    }

    private static DeploymentStage Map(TeamLabDeploymentStage stage) => stage switch
    {
        TeamLabDeploymentStage.ArtifactsVerifying => DeploymentStage.ArtifactsVerifying,
        TeamLabDeploymentStage.NetworkApplying => DeploymentStage.NetworkApplying,
        TeamLabDeploymentStage.RoutesApplying => DeploymentStage.RoutesApplying,
        TeamLabDeploymentStage.AssetBooting => DeploymentStage.AssetBooting,
        TeamLabDeploymentStage.HealthProbing => DeploymentStage.HealthProbing,
        TeamLabDeploymentStage.ObservationStarting => DeploymentStage.ObservationStarting,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}

public sealed class TeamLabArtifactDistribution(ImageDistributionService distribution)
    : ITeamLabArtifactDistribution
{
    public async Task EnsureImageAsync(
        int runtimeId,
        Guid workerNodeId,
        ImageTemplate template,
        CancellationToken cancellationToken)
    {
        var reference = ImageDistributionReferenceKey.TeamLabRuntime(runtimeId);
        if (template.ImageType == ImageType.Docker)
        {
            var image = DockerImageReference.ResolvePullTarget(template.Name, template.RegistryUrl).FullImage;
            await distribution.EnsureDockerImageOnNodeAsync(
                image, workerNodeId, reference, cancellationToken);
            return;
        }

        var result = await distribution.EnsureVmTemplateOnNodeAsync(
            template.Id, workerNodeId, reference, cancellationToken);
        if (!result.Success)
            throw new TeamLabRuntimeExecutionException(result.Message);
    }

    public Task ReleaseTeamLabReleaseReferencesAsync(Guid releaseId, CancellationToken token) =>
        distribution.ReleaseTeamLabReleaseReferencesAsync(releaseId, token);

    public Task ReleaseRuntimeAsync(int runtimeId, CancellationToken cancellationToken) =>
        distribution.ReleaseTeamLabRuntimeReferencesAsync(runtimeId, cancellationToken);
}

public sealed class TeamLabRuntimeQueue(DeploymentQueueService queue) : ITeamLabRuntimeQueue
{
    public async Task<TeamLabQueueTicketResult> EnqueueAsync(
        TeamLabQueueRequest request,
        CancellationToken cancellationToken)
    {
        var queued = await queue.EnqueueAsync(ToDeploymentRequest(request), cancellationToken);
        return new TeamLabQueueTicketResult(queued.TicketId);
    }

    public async Task<TeamLabQueueTicketResult> EnqueueInCurrentTransactionAsync(
        TeamLabQueueRequest request,
        CancellationToken cancellationToken)
    {
        var queued = await queue.EnqueueInCurrentTransactionAsync(ToDeploymentRequest(request), cancellationToken);
        return new TeamLabQueueTicketResult(queued.TicketId);
    }

    public Task NotifyAsync(Guid ticketId, CancellationToken cancellationToken) =>
        queue.NotifyAsync(ticketId, cancellationToken);

    private static DeploymentQueueRequest ToDeploymentRequest(TeamLabQueueRequest request) =>
        DeploymentQueueRequest.TeamLab(
            request.RuntimeId,
            request.DockerSlots,
            request.VmSlots,
            request.OwnerUserId,
            request.OperationId,
            request.RuntimePublicId,
            request.SubjectDisplayName,
            request.ResourceDisplayName) with
        {
            Identity = request.Identity,
            Operation = request.Operation,
            Generation = request.Generation,
            TargetNodeId = request.TargetNodeId,
            ProtectedPayload = request.ProtectedPayload,
            PayloadHash = request.PayloadHash
        };
}
