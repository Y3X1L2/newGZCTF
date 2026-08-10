using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Application.Rollouts;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class TeamLabRuntimeOperationHandler(
    AppDbContext context,
    ITeamLabRuntimeApplicationService runtimes,
    ITeamLabTopologyApplicationService topologies,
    TeamLabScopeAuthorizationService scopeAuthorization,
    TeamLabRuntimeLifecycleGuard lifecycleGuard,
    TeamLabAccessGrantService access,
    TeamLabTrafficApplicationService traffic,
    TeamLabRuntimeOperationPayloadProtector protector,
    ApiOperationService operations,
    ITeamLabRolloutApplicationService rolloutService,
    TeamLabReleaseImagePreparationService preparation,
    TeamLabWebhookService webhooks) : IApiOperationHandler
{
    public string Kind => TeamLabRuntimeOperationApplicationService.OperationKind;

    public async Task ExecuteAsync(Guid operationId, string leaseOwner, CancellationToken cancellationToken)
    {
        var job = await context.TeamLabRuntimeOperationJobs.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken)
            ?? throw new ApiOperationTerminalException("teamlab_job_not_found", "未找到 TeamLab 操作任务。");
        if (job.ResultJson is not null) return;
        var operation = await context.ApiOperations.AsNoTracking().SingleAsync(item => item.Id == operationId, cancellationToken);

        if (job.Kind is TeamLabRuntimeOperationKind.Create or TeamLabRuntimeOperationKind.Reset or
            TeamLabRuntimeOperationKind.Destroy or TeamLabRuntimeOperationKind.RuntimePause or
            TeamLabRuntimeOperationKind.RuntimeResume)
        {
            var payload = ReadPayload(job);
            var scopeId = payload.ControlScopeId
                ?? throw new ApiOperationTerminalException("teamlab_scope_missing", "TeamLab 控制范围缺失。");
            var administrator = await IsAdministratorAsync(operation.ActorUserId, cancellationToken);
            var resolvedScope = job.Kind switch
            {
                TeamLabRuntimeOperationKind.Create when payload.Create is not null =>
                    await scopeAuthorization.RequireReleaseScopeAsync(
                        payload.Create.ReleaseId, operation.ApiTokenId, administrator, true, cancellationToken),
                TeamLabRuntimeOperationKind.Reset or TeamLabRuntimeOperationKind.Destroy or
                    TeamLabRuntimeOperationKind.RuntimePause or TeamLabRuntimeOperationKind.RuntimeResume when payload.RuntimeId is { } runtimeId =>
                    payload.RolloutId is not null
                        ? scopeId
                        : await scopeAuthorization.RequireRuntimeScopeAsync(
                            runtimeId, operation.ApiTokenId, administrator,
                            job.Kind != TeamLabRuntimeOperationKind.Destroy, cancellationToken),
                _ => throw new ApiOperationTerminalException("teamlab_payload_invalid", "TeamLab 运行时操作负载无效。")
            };
            if (resolvedScope != scopeId)
                throw new ApiOperationTerminalException("teamlab_scope_mismatch", "TeamLab 资源超出请求的控制范围。");
        }

        if (job.Kind is TeamLabRuntimeOperationKind.Reset or TeamLabRuntimeOperationKind.Destroy or
            TeamLabRuntimeOperationKind.RuntimePause or TeamLabRuntimeOperationKind.RuntimeResume)
        {
            var payload = ReadPayload(job);
            if (payload.RolloutId is null)
            {
                var lifecycleRuntimeId = job.RuntimePublicId;
                if (!lifecycleRuntimeId.HasValue)
                    lifecycleRuntimeId = payload.RuntimeId;
                if (!lifecycleRuntimeId.HasValue)
                    throw new ApiOperationTerminalException(
                        "teamlab_payload_invalid",
                        "运行时生命周期操作 ID 缺失。");
                if (await lifecycleGuard.IsRolloutManagedAsync(lifecycleRuntimeId.Value, cancellationToken))
                    throw new ApiOperationTerminalException(
                        "runtime_managed_by_rollout",
                        "此运行时由比赛 rollout 管理，请使用比赛生命周期 API。");
            }
        }

        if (IsExternalCommand(job.Kind))
        {
            await ExecuteExternalCommandAsync(job, operation, leaseOwner, cancellationToken);
            return;
        }

        if (job.Kind == TeamLabRuntimeOperationKind.Destroy)
        {
            var payload = ReadPayload(job);
            var runtimeId = payload.RuntimeId ?? job.RuntimePublicId
                ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "销毁运行时 ID 缺失。");
            await operations.UpdateProgressAsync(operationId, leaseOwner, "runtime-destroying", 0, 1,
                "teamlab-runtime", runtimeId.ToString("D"), null, cancellationToken);
            var queued = await runtimes.DestroyAndEnqueueAsync(
                runtimeId, operationId, operation.ActorUserId, cancellationToken);
            job.RuntimeId = await context.TeamLabRuntimes.AsNoTracking()
                .Where(runtime => runtime.PublicId == runtimeId)
                .Select(runtime => (int?)runtime.Id)
                .SingleAsync(cancellationToken);
            job.RuntimePublicId = runtimeId;
            job.ProtectedPayload = null;
            await context.SaveChangesAsync(cancellationToken);
            await WaitForTicketAsync(job, queued.TicketId, operationId, leaseOwner, cancellationToken);
            return;
        }

        if (job.Kind is TeamLabRuntimeOperationKind.RuntimePause or TeamLabRuntimeOperationKind.RuntimeResume)
        {
            var payload = ReadPayload(job);
            var runtimeId = payload.RuntimeId ?? job.RuntimePublicId
                ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "运行时生命周期操作 ID 缺失。");
            await operations.UpdateProgressAsync(operationId, leaseOwner,
                job.Kind == TeamLabRuntimeOperationKind.RuntimePause ? "runtime-pausing" : "runtime-resuming",
                0, 1, "teamlab-runtime", runtimeId.ToString("D"), null, cancellationToken);
            TeamLabRuntimeProjectionModel projection;
            if (payload.RolloutId is { } rolloutPublicId)
            {
                var rolloutId = await context.TeamLabRollouts.AsNoTracking()
                    .Where(rollout => rollout.PublicId == rolloutPublicId)
                    .Select(rollout => (int?)rollout.Id)
                    .SingleAsync(cancellationToken)
                    ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "rollout 不存在。");
                projection = job.Kind == TeamLabRuntimeOperationKind.RuntimePause
                    ? await runtimes.PauseRolloutTargetAsync(runtimeId, rolloutId, cancellationToken)
                    : await runtimes.ResumeRolloutTargetAsync(runtimeId, rolloutId, cancellationToken);
            }
            else
            {
                projection = job.Kind == TeamLabRuntimeOperationKind.RuntimePause
                    ? await runtimes.PauseAsync(runtimeId, cancellationToken)
                    : await runtimes.ResumeAsync(runtimeId, cancellationToken);
            }
            job.RuntimePublicId = runtimeId;
            job.RuntimeId = await context.TeamLabRuntimes.AsNoTracking()
                .Where(runtime => runtime.PublicId == runtimeId)
                .Select(runtime => (int?)runtime.Id)
                .SingleAsync(cancellationToken);
            await operations.UpdateProgressAsync(operationId, leaseOwner,
                job.Kind == TeamLabRuntimeOperationKind.RuntimePause ? "runtime-paused" : "runtime-resumed",
                1, 1, "teamlab-runtime", runtimeId.ToString("D"), null, cancellationToken);
            await CompleteJobAsync(job, projection.ToOpen(), cancellationToken);
            return;
        }

        if (job.RuntimeId is null)
        {
            var linkedRuntimeId = await context.DeploymentQueueTickets.AsNoTracking()
                .Where(ticket => ticket.ApiOperationId == operationId && ticket.TeamLabRuntimeId != null)
                .Select(ticket => ticket.TeamLabRuntimeId)
                .FirstOrDefaultAsync(cancellationToken);
            if (linkedRuntimeId is not null)
            {
                job.RuntimeId = linkedRuntimeId;
                job.RuntimePublicId = await context.TeamLabRuntimes.AsNoTracking()
                    .Where(item => item.Id == linkedRuntimeId)
                    .Select(item => (Guid?)item.PublicId)
                    .SingleAsync(cancellationToken);
            }
            else
            {
                var payload = ReadPayload(job);
                TeamLabRuntimeCreateResult result;
                if (job.Kind == TeamLabRuntimeOperationKind.Create)
                {
                    var create = payload.Create ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "创建负载缺失。");
                    var runtimeOwner = await context.TeamLabTopologyReleases.AsNoTracking()
                        .Where(item => item.Id == create.ReleaseId)
                        .Select(item => item.Topology.OwnerUserId)
                        .SingleOrDefaultAsync(cancellationToken)
                        ?? operation.ActorUserId
                        ?? throw new ApiOperationTerminalException("authentication_required", "操作执行者缺失。");
                    result = await runtimes.PlanAndEnqueueAsync(
                        create,
                        operation.ActorUserId ?? throw new ApiOperationTerminalException("authentication_required", "操作执行者缺失。"),
                        runtimeOwner,
                        operation.RequestHash,
                        null,
                        operationId,
                        payload.Create?.ExternalReference,
                        cancellationToken);
                }
                else if (job.Kind == TeamLabRuntimeOperationKind.Reset)
                {
                    var payloadForReset = ReadPayload(job);
                    if (payloadForReset.RolloutId is { } rolloutPublicId)
                    {
                        var rolloutId = await context.TeamLabRollouts.AsNoTracking()
                            .Where(rollout => rollout.PublicId == rolloutPublicId)
                            .Select(rollout => (int?)rollout.Id)
                            .SingleAsync(cancellationToken)
                            ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "rollout 不存在。");
                        var runtimeId = payloadForReset.RuntimeId
                            ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "重置运行时 ID 缺失。");
                        await operations.UpdateProgressAsync(operationId, leaseOwner, "runtime-resetting", 0, 1,
                            "teamlab-runtime", runtimeId.ToString("D"), null, cancellationToken);
                        result = new TeamLabRuntimeCreateResult(
                            await context.TeamLabRuntimes.AsNoTracking()
                                .Where(runtime => runtime.PublicId == runtimeId)
                                .Select(runtime => runtime.Id)
                                .SingleAsync(cancellationToken),
                            runtimeId,
                            false);
                        job.RuntimeId = result.RuntimeId;
                        job.RuntimePublicId = runtimeId;
                        job.ProtectedPayload = null;
                        await context.SaveChangesAsync(cancellationToken);
                        var projection = await runtimes.ResetRolloutTargetAsync(
                            runtimeId, rolloutId, operationId, cancellationToken);
                        await operations.UpdateProgressAsync(operationId, leaseOwner, "runtime-reset", 1, 1,
                            "teamlab-runtime", runtimeId.ToString("D"), null, cancellationToken);
                        await CompleteJobAsync(job, projection.ToOpen(), cancellationToken);
                        return;
                    }
                    result = await runtimes.ResetAndEnqueueAsync(
                        payload.RuntimeId ?? throw new ApiOperationTerminalException("teamlab_payload_invalid", "重置运行时 ID 缺失。"),
                        payload.Reset ?? new ResetTeamLabRuntimeModel(null),
                        operationId,
                        cancellationToken);
                }
                else
                {
                    throw new ApiOperationTerminalException("teamlab_operation_invalid", "TeamLab 操作类型无效。");
                }
                job.RuntimeId = result.RuntimeId;
                job.RuntimePublicId = result.RuntimePublicId;
            }
            job.ProtectedPayload = null;
            await context.SaveChangesAsync(cancellationToken);
        }

        var ticket = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(item => item.TeamLabRuntimeId == job.RuntimeId && item.ApiOperationId == operationId)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (ticket is null && job.Kind == TeamLabRuntimeOperationKind.Create && job.RuntimePublicId is { } runtimePublicId)
        {
            var queued = await runtimes.EnqueuePlannedRuntimeAsync(
                runtimePublicId,
                operation.ActorUserId ?? throw new ApiOperationTerminalException("authentication_required", "操作执行者缺失。"),
                operationId,
                null,
                cancellationToken);
            ticket = await context.DeploymentQueueTickets.AsNoTracking()
                .SingleAsync(item => item.Id == queued.TicketId, cancellationToken);
        }
        if (ticket is null)
            throw new ApiOperationTerminalException("teamlab_ticket_missing", "TeamLab 操作没有对应的部署队列 ticket。");
        await WaitForTicketAsync(job, ticket.Id, operationId, leaseOwner, cancellationToken);
    }

    private async Task ExecuteExternalCommandAsync(
        TeamLabRuntimeOperationJob job,
        ApiOperation operation,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var actorUserId = operation.ActorUserId
            ?? throw new ApiOperationTerminalException("authentication_required", "操作执行者缺失。");
        var isAdministrator = await context.Users.AsNoTracking()
            .Where(item => item.Id == actorUserId)
            .Select(item => item.Role >= Role.Admin)
            .SingleOrDefaultAsync(cancellationToken);
        var payload = ReadPayload(job);

        if (IsExternalCommand(job.Kind))
        {
            var scopeId = payload.ControlScopeId ?? payload.CreateTopology?.ControlScopeId ??
                          payload.CreateRollout?.ControlScopeId;
            if (scopeId is not { } resolvedScope)
                throw new ApiOperationTerminalException("teamlab_scope_missing", "TeamLab 控制范围缺失。");
            await scopeAuthorization.RequireWritableAsync(
                resolvedScope, operation.ApiTokenId, isAdministrator, cancellationToken);
        }

        switch (job.Kind)
        {
            case TeamLabRuntimeOperationKind.TopologyCreate:
            {
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "topology-creating", 0, 1,
                    "teamlab-topology", null, null, cancellationToken);
                var result = await topologies.CreateForOperationAsync(
                    payload.CreateTopology ?? throw MissingPayload("拓扑创建"),
                    actorUserId, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "topology-created", 1, 1,
                    "teamlab-topology", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.TopologyUpdate:
            {
                var topologyId = payload.TopologyId ?? throw MissingPayload("拓扑更新 ID");
                var result = await topologies.UpdateForOperationAsync(
                    topologyId,
                    payload.UpdateTopology ?? throw MissingPayload("拓扑更新"),
                    actorUserId, true, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "topology-updated", 1, 1,
                    "teamlab-topology", topologyId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.TopologyDelete:
            {
                var topologyId = payload.TopologyId ?? throw MissingPayload("拓扑删除 ID");
                await topologies.DeleteForOperationAsync(
                    topologyId, actorUserId, true, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "topology-deleted", 1, 1,
                    "teamlab-topology", topologyId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, new { topologyId, deleted = true }, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.TopologyPublish:
            {
                var topologyId = payload.TopologyId ?? throw MissingPayload("拓扑发布 ID");
                var result = await topologies.PublishForOperationAsync(
                    topologyId,
                    payload.PublishTopology?.Revision ?? throw MissingPayload("拓扑发布修订"),
                    actorUserId,
                    true,
                    operation.Id,
                    cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "release-published", 1, 1,
                    "teamlab-release", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.AccessGrantCreate:
            {
                var runtimeId = payload.RuntimeId ?? throw MissingPayload("运行时 ID");
                if (!string.Equals(payload.CreateAccessGrant?.Type, "WireGuard", StringComparison.OrdinalIgnoreCase))
                    throw new ApiOperationTerminalException(
                        "topology_invalid", "仅支持 WireGuard 访问授权。");
                await RequireRuntimeScopeAsync(runtimeId, payload, operation, isAdministrator, true, cancellationToken);
                var result = await access.CreateForOperationAsync(runtimeId, operation.Id, cancellationToken);
                job.RuntimePublicId = runtimeId;
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "access-grant-created", 1, 1,
                    "teamlab-access-grant", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, new { grantId = result.Id }, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.AccessGrantRevoke:
            {
                var runtimeId = payload.RuntimeId ?? throw MissingPayload("运行时 ID");
                var grantId = payload.AccessGrantId ?? throw MissingPayload("访问授权 ID");
                await RequireRuntimeScopeAsync(runtimeId, payload, operation, isAdministrator, true, cancellationToken);
                await access.RevokeAsync(runtimeId, grantId, cancellationToken);
                job.RuntimePublicId = runtimeId;
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "access-grant-revoked", 1, 1,
                    "teamlab-access-grant", grantId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, new { grantId, revoked = true }, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.CaptureStart:
            {
                var runtimeId = payload.RuntimeId ?? throw MissingPayload("运行时 ID");
                await RequireRuntimeScopeAsync(runtimeId, payload, operation, isAdministrator, true, cancellationToken);
                var result = await traffic.StartCaptureForOperationAsync(
                    runtimeId,
                    payload.CreateCapture ?? throw MissingPayload("抓包请求"),
                    operation.Id,
                    cancellationToken);
                job.RuntimePublicId = runtimeId;
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "capture-started", 1, 1,
                    "teamlab-capture", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.CaptureStop:
            {
                var runtimeId = payload.RuntimeId ?? throw MissingPayload("运行时 ID");
                var captureId = payload.CaptureId ?? throw MissingPayload("抓包 ID");
                await RequireRuntimeScopeAsync(runtimeId, payload, operation, isAdministrator, true, cancellationToken);
                var result = await traffic.StopCaptureAsync(runtimeId, captureId, cancellationToken);
                job.RuntimePublicId = runtimeId;
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "capture-stopped", 1, 1,
                    "teamlab-capture", captureId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result.ToOpen(), cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.RolloutCreate:
            {
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-creating", 0, 1,
                    "teamlab-rollout", null, null, cancellationToken);
                var result = await rolloutService.CreateExternalAsync(
                    payload.CreateRollout ?? throw MissingPayload("rollout 创建"),
                    actorUserId,
                    operation.Id,
                    cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-created", 1, 1,
                    "teamlab-rollout", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.RolloutReplaceTargets:
            {
                var rolloutId = payload.RolloutId ?? throw MissingPayload("rollout ID");
                var result = await rolloutService.ReplaceTargetsAsync(
                    rolloutId,
                    payload.ReplaceRolloutTargets ?? throw MissingPayload("rollout targets"),
                    actorUserId,
                    operation.Id,
                    cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-targets-replaced", 1, 1,
                    "teamlab-rollout", rolloutId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.RolloutPrepare:
            {
                var rolloutId = payload.RolloutId ?? throw MissingPayload("rollout ID");
                var result = await rolloutService.RequestPreparationForOperationAsync(
                    rolloutId, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-preparing", 1, 1,
                    "teamlab-rollout", rolloutId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.RolloutSetAccess:
            {
                var rolloutId = payload.RolloutId ?? throw MissingPayload("rollout ID");
                var result = await rolloutService.SetAccessForOperationAsync(
                    rolloutId, payload.RolloutAccessOpen ?? throw MissingPayload("rollout access state"),
                    operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-access-updated", 1, 1,
                    "teamlab-rollout", rolloutId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.RolloutDrain:
            {
                var rolloutId = payload.RolloutId ?? throw MissingPayload("rollout ID");
                var result = await rolloutService.RequestDrainForOperationAsync(
                    rolloutId, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-draining", 1, 1,
                    "teamlab-rollout", rolloutId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.RolloutRebuildTarget:
            {
                var rolloutId = payload.RolloutId ?? throw MissingPayload("rollout ID");
                var result = await rolloutService.RequestRebuildAsync(
                    rolloutId,
                    payload.RolloutTargetId ?? throw MissingPayload("rollout target ID"),
                    actorUserId,
                    operation.Id,
                    cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-target-rebuild-requested", 1, 1,
                    "teamlab-rollout", rolloutId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.RolloutArchive:
            {
                var rolloutId = payload.RolloutId ?? throw MissingPayload("rollout ID");
                var result = await rolloutService.ArchiveAsync(
                    rolloutId, actorUserId, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-archived", 1, 1,
                    "teamlab-rollout", rolloutId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.RolloutPause:
            {
                var rolloutId = payload.RolloutId ?? throw MissingPayload("rollout ID");
                var result = await rolloutService.RequestPauseForOperationAsync(
                    rolloutId, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-paused", 1, 1,
                    "teamlab-rollout", rolloutId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.RolloutResume:
            {
                var rolloutId = payload.RolloutId ?? throw MissingPayload("rollout ID");
                var result = await rolloutService.RequestResumeForOperationAsync(
                    rolloutId, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "rollout-resumed", 1, 1,
                    "teamlab-rollout", rolloutId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.ReleasePreparation:
            {
                var releaseId = payload.ReleaseId ?? throw MissingPayload("发布版本 ID");
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "release-preparing", 0, 1,
                    "teamlab-release", releaseId.ToString("D"), null, cancellationToken);
                await preparation.QueueAsync(releaseId, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "release-prepared", 1, 1,
                    "teamlab-release", releaseId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, new { releaseId, queued = true }, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.WebhookCreate:
            {
                var create = payload.CreateWebhook ?? throw MissingPayload("webhook 创建请求");
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "webhook-creating", 0, 1,
                    "teamlab-webhook", null, null, cancellationToken);
                var result = await webhooks.CreateForOperationAsync(
                    create, actorUserId, operation.Id, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "webhook-created", 1, 1,
                    "teamlab-webhook", result.Id.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.WebhookRevoke:
            {
                var webhookId = payload.WebhookId ?? throw MissingPayload("webhook ID");
                await webhooks.RevokeAsync(webhookId, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "webhook-revoked", 1, 1,
                    "teamlab-webhook", webhookId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, new { webhookId, revoked = true }, cancellationToken);
                return;
            }
            case TeamLabRuntimeOperationKind.WebhookReplay:
            {
                var webhookId = payload.WebhookId ?? throw MissingPayload("webhook ID");
                var result = await webhooks.ReplayAsync(webhookId, payload.ReplayFromEventId, cancellationToken);
                await operations.UpdateProgressAsync(operation.Id, leaseOwner, "webhook-replayed", 1, 1,
                    "teamlab-webhook", webhookId.ToString("D"), null, cancellationToken);
                await CompleteJobAsync(job, result, cancellationToken);
                return;
            }
            default:
                throw new ApiOperationTerminalException(
                    "teamlab_operation_invalid", "TeamLab 操作类型无效。");
        }
    }

    private static ApiOperationTerminalException MissingPayload(string field) =>
        new("teamlab_payload_invalid", $"TeamLab 操作缺少 {field}。");

    private static bool IsExternalCommand(TeamLabRuntimeOperationKind kind) =>
        kind is >= TeamLabRuntimeOperationKind.TopologyCreate and <= TeamLabRuntimeOperationKind.RolloutArchive or
            TeamLabRuntimeOperationKind.RolloutPause or TeamLabRuntimeOperationKind.RolloutResume or
            TeamLabRuntimeOperationKind.ReleasePreparation or
            TeamLabRuntimeOperationKind.WebhookCreate or
            TeamLabRuntimeOperationKind.WebhookRevoke or
            TeamLabRuntimeOperationKind.WebhookReplay;

    private Task<bool> IsAdministratorAsync(Guid? actorUserId, CancellationToken cancellationToken) =>
        actorUserId is not { } userId
            ? Task.FromResult(false)
            : context.Users.AsNoTracking().Where(item => item.Id == userId)
                .Select(item => item.Role >= Role.Admin).SingleOrDefaultAsync(cancellationToken);

    private async Task RequireRuntimeScopeAsync(
        Guid runtimeId,
        TeamLabRuntimeOperationPayload payload,
        ApiOperation operation,
        bool administrator,
        bool writable,
        CancellationToken cancellationToken)
    {
        var expectedScope = payload.ControlScopeId
            ?? throw new ApiOperationTerminalException("teamlab_scope_missing", "TeamLab 控制范围缺失。");
        var actualScope = await scopeAuthorization.RequireRuntimeScopeAsync(
            runtimeId, operation.ApiTokenId, administrator, writable, cancellationToken);
        if (actualScope != expectedScope)
            throw new ApiOperationTerminalException("teamlab_scope_mismatch", "TeamLab 运行时超出请求的控制范围。");
    }


    public async Task OnTerminalFailureAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var job = await context.TeamLabRuntimeOperationJobs.SingleOrDefaultAsync(item => item.OperationId == operationId, cancellationToken);
        if (job is null) return;
        job.ProtectedPayload = null;
        if (job.Kind == TeamLabRuntimeOperationKind.AccessGrantCreate)
        {
            var grant = await context.TeamLabAccessGrants.SingleOrDefaultAsync(
                item => item.ApiOperationId == operationId, cancellationToken);
            if (grant is not null)
            {
                grant.ProtectedDownloadToken = null;
                grant.Revoked = true;
                grant.RevokedAt ??= DateTimeOffset.UtcNow;
            }
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task WaitForTicketAsync(
        TeamLabRuntimeOperationJob job,
        Guid ticketId,
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            context.ChangeTracker.Clear();
            var ticket = await context.DeploymentQueueTickets.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == ticketId, cancellationToken)
                ?? throw new InvalidOperationException("The TeamLab deployment queue ticket was deleted.");
            var (stage, progress) = ticket.Status switch
            {
                DeploymentQueueTicketStatus.Pending => ("runtime-queued", 1L),
                DeploymentQueueTicketStatus.Scheduling or DeploymentQueueTicketStatus.Scheduled =>
                    ("runtime-assigned", 2L),
                DeploymentQueueTicketStatus.Running => ("runtime-deploying", 3L),
                DeploymentQueueTicketStatus.Succeeded => ("runtime-ready", 4L),
                DeploymentQueueTicketStatus.Failed => ("runtime-failed", 4L),
                DeploymentQueueTicketStatus.Cancelled => ("runtime-cancelled", 4L),
                _ => ("runtime-queued", 1L)
            };
            await operations.UpdateProgressAsync(operationId, leaseOwner, stage, progress, 4,
                "teamlab-runtime", job.RuntimePublicId?.ToString("D"), ticket.Id, cancellationToken);
            if (ticket.Status == DeploymentQueueTicketStatus.Succeeded)
            {
                var projection = (await runtimes.GetAsync(job.RuntimePublicId!.Value, cancellationToken)).ToOpen();
                var trackedJob = await context.TeamLabRuntimeOperationJobs.SingleAsync(item => item.OperationId == operationId, cancellationToken);
                await CompleteJobAsync(trackedJob, projection, cancellationToken);
                return;
            }
            if (ticket.Status is DeploymentQueueTicketStatus.Failed or DeploymentQueueTicketStatus.Cancelled)
                throw new ApiOperationTerminalException(
                    ticket.Status == DeploymentQueueTicketStatus.Cancelled ? "operation_cancelled" : "operation_failed",
                    ticket.Status == DeploymentQueueTicketStatus.Cancelled
                        ? "TeamLab 部署已取消。"
                        : "TeamLab 部署失败，请使用 operation ID 查看管理员诊断。");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private TeamLabRuntimeOperationPayload ReadPayload(TeamLabRuntimeOperationJob job)
    {
        if (string.IsNullOrWhiteSpace(job.ProtectedPayload))
            throw new ApiOperationTerminalException("teamlab_payload_missing", "TeamLab 操作负载不可用。");
        return protector.Unprotect(job.ProtectedPayload);
    }

    private async Task CompleteJobAsync<T>(TeamLabRuntimeOperationJob job, T result, CancellationToken cancellationToken)
    {
        job.ResultJson = JsonSerializer.Serialize(result);
        job.ProtectedPayload = null;
        job.CompletedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
