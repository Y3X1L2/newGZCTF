using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeOrchestrator(
    AppDbContext context,
    TeamLabRuntimePlanner planner,
    TeamLabRuntimeProjectionService projections,
    TeamLabRuntimeOverlayService overlays,
    TeamLabShardDeploymentService deployment,
    TeamLabTrafficApplicationService traffic,
    TeamLabRuntimeCleanupService cleanup,
    DeploymentQueueService queue,
    IPublicUdpGatewayProvider publicGateway,
    ILogger<TeamLabRuntimeOrchestrator> logger) : ITeamLabRuntimeApplicationService
{
    public async Task<TeamLabRuntimeCreateResult> PlanAndEnqueueAsync(
        CreateTeamLabRuntimeModel command,
        Guid actorUserId,
        Guid runtimeOwnerUserId,
        string requestHash,
        Guid? operationId,
        string? subjectDisplayName,
        CancellationToken cancellationToken)
    {
        var result = await planner.CreateAsync(command, runtimeOwnerUserId, requestHash, cancellationToken);
        if (result.Reused) return result;
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Include(item => item.Assets)
            .SingleAsync(item => item.Id == result.RuntimeId, cancellationToken);
        var dockerSlots = runtime.Assets.Count(item => item.Generation == runtime.Generation && item.Kind == TeamLabResourceKind.Docker);
        var vmSlots = runtime.Assets.Count(item => item.Generation == runtime.Generation && item.Kind == TeamLabResourceKind.Vm);
        var queued = await queue.EnqueueAsync(DeploymentQueueRequest.TeamLab(
            runtime.Id,
            dockerSlots,
            vmSlots,
            actorUserId,
            operationId,
            runtime.PublicId,
            subjectDisplayName ?? runtime.ExternalReference ?? runtime.PublicId.ToString("D"),
            $"{dockerSlots} Docker / {vmSlots} VM"), cancellationToken);
        await LinkOperationAsync(operationId, runtime, queued.TicketId, cancellationToken);
        return result;
    }

    public Task<TeamLabRuntimeProjectionModel> GetAsync(Guid runtimeId, CancellationToken cancellationToken) =>
        projections.GetAsync(runtimeId, cancellationToken);

    public async Task<TeamLabRuntimeCreateResult> ResetAndEnqueueAsync(
        Guid runtimeId,
        ResetTeamLabRuntimeModel command,
        Guid? operationId,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeByPublicIdAsync(runtimeId, cancellationToken);
        if (runtime.Status is TeamLabRuntimeStatus.Destroying or TeamLabRuntimeStatus.CleanupPending)
            throw new TeamLabApiContractException("runtime_cleanup_pending", "Runtime cleanup is already pending.", 409);
        await queue.CancelTeamLabRuntimeAsync(runtime.Id, "TeamLab runtime reset requested.", cancellationToken);
        var releaseActive = runtime.Status == TeamLabRuntimeStatus.Running;
        runtime.Status = TeamLabRuntimeStatus.Destroying;
        runtime.IsOpenToPlayers = false;
        runtime.Events.Add(Event(runtime, "reset", TeamLabEventLevel.Info, "Runtime generation reset started."));
        await context.SaveChangesAsync(cancellationToken);
        var cleaned = await cleanup.CleanupAsync(runtime, releaseActive, cancellationToken);
        if (!cleaned.Success)
            throw new TeamLabApiContractException("runtime_cleanup_pending", cleaned.Message, 409);
        runtime.Status = TeamLabRuntimeStatus.Destroyed;
        await context.SaveChangesAsync(cancellationToken);
        var result = await planner.ResetAsync(
            runtimeId,
            command.Overlays,
            command.ReleaseId,
            null,
            cancellationToken);
        context.ChangeTracker.Clear();
        var replanned = await context.TeamLabRuntimes.AsNoTracking().Include(item => item.Assets)
            .SingleAsync(item => item.Id == result.RuntimeId, cancellationToken);
        var dockerSlots = replanned.Assets.Count(item => item.Generation == replanned.Generation && item.Kind == TeamLabResourceKind.Docker);
        var vmSlots = replanned.Assets.Count(item => item.Generation == replanned.Generation && item.Kind == TeamLabResourceKind.Vm);
        var queued = await queue.EnqueueAsync(DeploymentQueueRequest.TeamLab(
            replanned.Id, dockerSlots, vmSlots, replanned.CreatedById, operationId, replanned.PublicId,
            replanned.ExternalReference ?? replanned.PublicId.ToString("D"),
            $"reset generation {replanned.Generation}"), cancellationToken);
        await LinkOperationAsync(operationId, replanned, queued.TicketId, cancellationToken);
        return result;
    }

    public async Task<TeamLabNodeResult> ExecuteQueuedAsync(int runtimeId, CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimeId, cancellationToken);
        if (runtime.Status == TeamLabRuntimeStatus.Running) return TeamLabNodeResult.Ok("Runtime is already running.");
        if (runtime.Status is not TeamLabRuntimeStatus.Scheduled and not TeamLabRuntimeStatus.Deploying)
            return TeamLabNodeResult.Failed($"Runtime cannot deploy from status {runtime.Status}.");
        var releaseId = runtime.TopologyReleaseId;
        if (releaseId == Guid.Empty)
            return TeamLabNodeResult.Failed("Runtime has no topology release.");
        var release = await context.TeamLabTopologyReleases.AsNoTracking().SingleAsync(item => item.Id == releaseId, cancellationToken);
        var definition = TeamLabReleaseCodec.Decode(release.CanonicalJson);
        var envelope = runtime.SecretEnvelopes.SingleOrDefault(item => item.Generation == runtime.Generation);
        IReadOnlyDictionary<string, TeamLabRuntimeOverlayModel> overlayValues;
        try
        {
            overlayValues = overlays.Unprotect(envelope);
        }
        catch (TeamLabApiContractException exception)
        {
            return await FailAsync(runtime, exception.Message, cancellationToken);
        }

        runtime.Status = TeamLabRuntimeStatus.Deploying;
        runtime.LastError = null;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        runtime.Events.Add(Event(runtime, "deploy", TeamLabEventLevel.Info, "Runtime deployment started."));
        foreach (var shard in runtime.Shards.Where(item => item.Generation == runtime.Generation))
            shard.Status = TeamLabRuntimeStatus.Deploying;
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            await deployment.DeployAsync(runtime, definition, overlayValues, cancellationToken);
            if (runtime.PublicUdpMapping is not null)
            {
                var gateway = await publicGateway.SyncMappingAsync(runtime.PublicUdpMapping, cancellationToken);
                if (!gateway.Success) throw new TeamLabRuntimeExecutionException(gateway.Message);
            }
            await traffic.StartCollectorsAsync(runtime, cancellationToken);
            TeamLabRuntimeOverlayService.Consume(envelope);
            runtime.Status = TeamLabRuntimeStatus.Running;
            runtime.IsOpenToPlayers = false;
            runtime.LastError = null;
            runtime.UpdatedAt = DateTimeOffset.UtcNow;
            foreach (var shard in runtime.Shards.Where(item => item.Generation == runtime.Generation))
            {
                shard.Status = TeamLabRuntimeStatus.Running;
                shard.LastError = null;
                shard.UpdatedAt = DateTimeOffset.UtcNow;
            }
            runtime.Events.Add(Event(runtime, "ready", TeamLabEventLevel.Success, "Runtime deployment completed."));
            await context.SaveChangesAsync(cancellationToken);
            return TeamLabNodeResult.Ok("Runtime deployment completed.");
        }
        catch (Exception exception) when (exception is TeamLabRuntimeExecutionException or AgentClientException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "TeamLab runtime {RuntimeId} deployment failed.", runtime.PublicId);
            var cleaned = await cleanup.CleanupAsync(runtime, releaseActiveCapacity: false, cancellationToken);
            return await FailAsync(runtime,
                cleaned.Success ? exception.Message : $"{exception.Message}; cleanup: {cleaned.Message}",
                cancellationToken,
                cleanupPending: !cleaned.Success);
        }
    }

    public async Task<TeamLabRuntimeProjectionModel> DestroyAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeByPublicIdAsync(runtimeId, cancellationToken);
        if (runtime.Status == TeamLabRuntimeStatus.Destroyed) return await projections.GetAsync(runtimeId, cancellationToken);
        await queue.CancelTeamLabRuntimeAsync(runtime.Id, "TeamLab runtime destroy requested.", cancellationToken);
        var releaseActive = runtime.Status == TeamLabRuntimeStatus.Running;
        runtime.Status = TeamLabRuntimeStatus.Destroying;
        runtime.IsOpenToPlayers = false;
        runtime.Events.Add(Event(runtime, "destroy", TeamLabEventLevel.Info, "Runtime destruction started."));
        await context.SaveChangesAsync(cancellationToken);
        var result = await cleanup.CleanupAsync(runtime, releaseActive, cancellationToken);
        if (result.Success)
        {
            runtime.Status = TeamLabRuntimeStatus.Destroyed;
            runtime.Events.Add(Event(runtime, "destroy", TeamLabEventLevel.Success, "Runtime destroyed."));
            await context.SaveChangesAsync(cancellationToken);
        }
        return await projections.GetAsync(runtimeId, cancellationToken);
    }

    private async Task<TeamLabNodeResult> FailAsync(
        TeamLabRuntime runtime,
        string message,
        CancellationToken cancellationToken,
        bool cleanupPending = false)
    {
        runtime.Status = cleanupPending ? TeamLabRuntimeStatus.CleanupPending : TeamLabRuntimeStatus.Failed;
        runtime.IsOpenToPlayers = false;
        runtime.LastError = Trim(message);
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        runtime.Events.Add(Event(runtime, "deploy", TeamLabEventLevel.Error, runtime.LastError));
        await context.SaveChangesAsync(cancellationToken);
        return TeamLabNodeResult.Failed(runtime.LastError);
    }

    private async Task<TeamLabRuntime> LoadRuntimeAsync(int runtimeId, CancellationToken cancellationToken) =>
        await RuntimeQuery().SingleOrDefaultAsync(item => item.Id == runtimeId, cancellationToken)
        ?? throw new TeamLabRuntimeExecutionException($"Runtime {runtimeId} was not found.");

    private async Task<TeamLabRuntime> LoadRuntimeByPublicIdAsync(Guid runtimeId, CancellationToken cancellationToken) =>
        await RuntimeQuery().SingleOrDefaultAsync(item => item.PublicId == runtimeId, cancellationToken)
        ?? throw new TeamLabApiContractException("runtime_not_found", "The TeamLab runtime was not found.", 404);

    private IQueryable<TeamLabRuntime> RuntimeQuery() => context.TeamLabRuntimes
        .Include(item => item.PublicUdpMapping)
        .Include(item => item.Shards).ThenInclude(item => item.Networks)
        .Include(item => item.Shards).ThenInclude(item => item.Assets)
        .Include(item => item.Networks).ThenInclude(item => item.NetworkLease)
        .Include(item => item.Assets)
        .Include(item => item.AccessGrants)
        .Include(item => item.VpnPeers)
        .Include(item => item.SecretEnvelopes)
        .Include(item => item.TrafficCaptureJobs)
        .Include(item => item.Events);

    private async Task LinkOperationAsync(
        Guid? operationId,
        TeamLabRuntime runtime,
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        if (operationId is not { } id) return;
        var operation = await context.ApiOperations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (operation is null) return;
        operation.DeploymentQueueTicketId = ticketId;
        operation.ResourceType = "teamlab-runtime";
        operation.ResourceId = runtime.PublicId.ToString("D");
        await context.SaveChangesAsync(cancellationToken);
    }

    private static TeamLabEvent Event(TeamLabRuntime runtime, string stage, TeamLabEventLevel level, string message) => new()
    {
        RuntimeId = runtime.Id,
        Generation = runtime.Generation,
        Stage = stage,
        Level = level,
        Message = Trim(message),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1024];
}
