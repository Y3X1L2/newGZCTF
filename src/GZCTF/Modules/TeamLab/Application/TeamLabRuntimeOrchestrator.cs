using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using GZCTF.Modules.Runtime.Application;
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
    ImageDistributionService imageDistribution,
    TeamLabPhysicalPlacementService placement,
    TeamLabRuntimeOperationPayloadProtector operationPayloads,
    DeploymentQueueService queue,
    IPublicUdpGatewayProvider publicGateway,
    TeamLabEventRecorder eventRecorder,
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
        var entryNodeId = runtime.Shards
            .Where(shard => shard.Generation == runtime.Generation && shard.Id == runtime.EntryShardId)
            .Select(shard => (Guid?)shard.WorkerNodeId)
            .FirstOrDefault();
        var dockerSlots = runtime.Assets.Count(item => item.Generation == runtime.Generation && item.Kind == TeamLabResourceKind.Docker);
        var vmSlots = runtime.Assets.Count(item => item.Generation == runtime.Generation && item.Kind == TeamLabResourceKind.Vm);
        var payload = new TeamLabRuntimeOperationPayload(null, runtime.PublicId, command);
        var protectedPayload = operationPayloads.Protect(payload);
        var payloadHash = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)))}";
        var queued = await queue.EnqueueAsync(DeploymentQueueRequest.TeamLab(
            runtime.Id, dockerSlots, vmSlots, runtime.CreatedById, operationId, runtime.PublicId,
            runtime.ExternalReference ?? runtime.PublicId.ToString("D"),
            $"reset generation {runtime.Generation + 1}") with
        {
            Operation = RuntimeOperationKind.Reset,
            Generation = runtime.Generation + 1,
            TargetNodeId = entryNodeId,
            ProtectedPayload = protectedPayload,
            PayloadHash = payloadHash
        }, cancellationToken);
        eventRecorder.Record(
            runtime,
            "reset",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.ResetQueued,
            OperationalEventOutcome.Pending,
            "Runtime reset queued.");
        await context.SaveChangesAsync(cancellationToken);
        await LinkOperationAsync(operationId, runtime, queued.TicketId, cancellationToken);
        return new TeamLabRuntimeCreateResult(runtime.Id, runtime.PublicId, false);
    }

    public async Task<TeamLabNodeResult> ExecuteQueuedResetAsync(
        int runtimeId,
        Guid ticketId,
        string? protectedPayload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(protectedPayload))
            return TeamLabNodeResult.Failed("TeamLab reset payload is unavailable.");
        TeamLabRuntimeOperationPayload payload;
        try
        {
            payload = operationPayloads.Unprotect(protectedPayload);
        }
        catch (ApiOperationTerminalException exception)
        {
            return TeamLabNodeResult.Failed(exception.Message);
        }

        var command = payload.Reset ?? new ResetTeamLabRuntimeModel(null);
        var runtime = await LoadRuntimeAsync(runtimeId, cancellationToken);
        runtime.Status = TeamLabRuntimeStatus.Destroying;
        runtime.IsOpenToPlayers = false;
        using var activity = PlatformTelemetry.TeamLabActivitySource.StartActivity(
            "teamlab.reset", ActivityKind.Internal);
        activity?.SetTag("gzctf.teamlab_runtime_id", runtime.Id);
        activity?.SetTag("teamlab.generation", runtime.Generation);
        eventRecorder.Record(
            runtime,
            "reset",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.ResetStarted,
            OperationalEventOutcome.Started,
            "Runtime generation reset started.");
        await context.SaveChangesAsync(cancellationToken);
        var cleaned = await cleanup.CleanupAsync(runtime, cancellationToken);
        if (!cleaned.Success)
            return await FailAsync(runtime, cleaned.Message, cancellationToken, cleanupPending: true,
                OperationalEventCodes.TeamLab.ResetFailed, "reset");
        runtime.Status = TeamLabRuntimeStatus.Destroyed;
        await context.SaveChangesAsync(cancellationToken);

        await planner.ResetAsync(runtime.PublicId, command.Overlays, command.ReleaseId, null, cancellationToken);
        var reserved = await placement.BindAndReserveAsync(ticketId, runtime.Id, cancellationToken);
        if (!reserved.Success || reserved.NodeId is not { } entryNodeId)
            return await FailAsync(runtime, reserved.Message, cancellationToken, false,
                OperationalEventCodes.TeamLab.ResetFailed, "reset");

        var ticket = await context.DeploymentQueueTickets.SingleAsync(item => item.Id == ticketId, cancellationToken);
        var replanned = await context.TeamLabRuntimes.Include(item => item.Assets)
            .SingleAsync(item => item.Id == runtime.Id, cancellationToken);
        ticket.TargetNodeId = entryNodeId;
        ticket.DockerSlots = replanned.Assets.Count(item =>
            item.Generation == replanned.Generation && item.Kind == TeamLabResourceKind.Docker);
        ticket.VmSlots = replanned.Assets.Count(item =>
            item.Generation == replanned.Generation && item.Kind == TeamLabResourceKind.Vm);
        await context.SaveChangesAsync(cancellationToken);
        var resetResult = await ExecuteQueuedAsync(runtime.Id, cancellationToken);
        if (resetResult.Success)
        {
            eventRecorder.Record(
                runtime,
                "reset",
                TeamLabEventLevel.Success,
                OperationalEventCodes.TeamLab.ResetSucceeded,
                OperationalEventOutcome.Succeeded,
                "Runtime reset completed successfully.");
            await context.SaveChangesAsync(cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, "reset_failed");
        }
        return resetResult;
    }

    public async Task<TeamLabNodeResult> ExecuteQueuedAsync(int runtimeId, CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimeId, cancellationToken);
        using var activity = PlatformTelemetry.TeamLabActivitySource.StartActivity(
            "teamlab.deploy", ActivityKind.Internal);
        activity?.SetTag("gzctf.teamlab_runtime_id", runtime.Id);
        activity?.SetTag("teamlab.generation", runtime.Generation);
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
        eventRecorder.Record(
            runtime,
            "deploy",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.DeployStarted,
            OperationalEventOutcome.Started,
            "Runtime deployment started.");
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
            eventRecorder.Record(
                runtime,
                "ready",
                TeamLabEventLevel.Success,
                OperationalEventCodes.TeamLab.Ready,
                OperationalEventOutcome.Succeeded,
                "Runtime deployment completed.");
            await context.SaveChangesAsync(cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return TeamLabNodeResult.Ok("Runtime deployment completed.");
        }
        catch (Exception exception) when (exception is TeamLabRuntimeExecutionException or AgentClientException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "TeamLab runtime {RuntimeId} deployment failed.", runtime.PublicId);
            var cleaned = await cleanup.CleanupAsync(runtime, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            var error = exception is AgentClientException agentException
                ? agentException.Error
                : TeamLabFailure(runtime, "teamlab.deploy");
            return await FailAsync(runtime,
                cleaned.Success ? exception.Message : $"{exception.Message}; cleanup: {cleaned.Message}",
                cancellationToken,
                cleanupPending: !cleaned.Success,
                error: error);
        }
    }

    public async Task<TeamLabRuntimeProjectionModel> DestroyAsync(Guid runtimeId, CancellationToken cancellationToken)
    {
        var queued = await DestroyAndEnqueueAsync(runtimeId, null, null, cancellationToken);
        _ = queued;
        return await projections.GetAsync(runtimeId, cancellationToken);
    }

    public async Task<DeploymentQueueResult> DestroyAndEnqueueAsync(
        Guid runtimeId,
        Guid? operationId,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeByPublicIdAsync(runtimeId, cancellationToken);
        var entryNodeId = runtime.Shards
            .Where(shard => shard.Generation == runtime.Generation && shard.Id == runtime.EntryShardId)
            .Select(shard => (Guid?)shard.WorkerNodeId)
            .FirstOrDefault();
        return await queue.EnqueueAsync(DeploymentQueueRequest.TeamLab(
            runtime.Id, 0, 0, actorUserId ?? runtime.CreatedById, operationId, runtime.PublicId,
            runtime.ExternalReference ?? runtime.PublicId.ToString("D"), "destroy runtime") with
        {
            Operation = RuntimeOperationKind.Destroy,
            Generation = runtime.Generation,
            TargetNodeId = entryNodeId
        }, cancellationToken);
    }

    public async Task<TeamLabNodeResult> ExecuteQueuedDestroyAsync(int runtimeId,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadRuntimeAsync(runtimeId, cancellationToken);
        if (runtime.Status == TeamLabRuntimeStatus.Destroyed)
            return TeamLabNodeResult.Ok("Runtime is already destroyed.");
        runtime.Status = TeamLabRuntimeStatus.Destroying;
        runtime.IsOpenToPlayers = false;
        using var activity = PlatformTelemetry.TeamLabActivitySource.StartActivity(
            "teamlab.destroy", ActivityKind.Internal);
        activity?.SetTag("gzctf.teamlab_runtime_id", runtime.Id);
        eventRecorder.Record(
            runtime,
            "destroy",
            TeamLabEventLevel.Info,
            OperationalEventCodes.TeamLab.DestroyStarted,
            OperationalEventOutcome.Started,
            "Runtime destruction started.");
        await context.SaveChangesAsync(cancellationToken);
        var result = await cleanup.CleanupAsync(runtime, cancellationToken);
        if (result.Success)
        {
            await imageDistribution.ReleaseTeamLabRuntimeReferencesAsync(runtime.Id, cancellationToken);
            runtime.Status = TeamLabRuntimeStatus.Destroyed;
            eventRecorder.Record(
                runtime,
                "destroy",
                TeamLabEventLevel.Success,
                OperationalEventCodes.TeamLab.DestroySucceeded,
                OperationalEventOutcome.Succeeded,
                "Runtime destroyed.");
            await context.SaveChangesAsync(cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            var error = TeamLabFailure(runtime, "teamlab.destroy");
            eventRecorder.Record(
                runtime,
                "destroy",
                TeamLabEventLevel.Error,
                OperationalEventCodes.TeamLab.DestroyFailed,
                OperationalEventOutcome.Failed,
                "Runtime destruction failed.",
                error);
            await context.SaveChangesAsync(cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Error, error.Code);
        }
        return result;
    }

    private async Task<TeamLabNodeResult> FailAsync(
        TeamLabRuntime runtime,
        string message,
        CancellationToken cancellationToken,
        bool cleanupPending = false,
        string eventCode = OperationalEventCodes.TeamLab.DeployFailed,
        string stage = "deploy",
        OperationalError? error = null)
    {
        runtime.Status = cleanupPending ? TeamLabRuntimeStatus.CleanupPending : TeamLabRuntimeStatus.Failed;
        runtime.IsOpenToPlayers = false;
        runtime.LastError = Trim(message);
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        error ??= TeamLabFailure(runtime, $"teamlab.{stage}");
        eventRecorder.Record(
            runtime,
            stage,
            TeamLabEventLevel.Error,
            eventCode,
            OperationalEventOutcome.Failed,
            stage == "reset" ? "Runtime reset failed." : "Runtime deployment failed.",
            error);
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

    private static OperationalError TeamLabFailure(TeamLabRuntime runtime, string operation) =>
        new(
            OperationalErrorCategory.Network,
            OperationalErrorCodes.NetworkOperationFailed,
            "TeamLab runtime operation failed.",
            true,
            Operation: operation);

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1024];
}
