using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using GZCTF.Infrastructure.Concurrency;

namespace GZCTF.Modules.TeamLab.Application;

public interface ITeamLabAssetControlGateway
{
    Task<TeamLabAssetControlResult> ExecuteAsync(Guid nodeId, TeamLabAssetControlRequest request, CancellationToken token);
}

public sealed class TeamLabAssetControlService(AppDbContext context, TeamLabAuthorizationService authorization,
    TeamLabRuntimeLifecycleGuard lifecycle, ITeamLabRuntimeQueue queue, TeamLabRuntimeOperationPayloadProtector protector,
    ITeamLabAssetControlGateway gateway, ITeamLabRemoteAccessService sessions, TeamLabEventRecorder events, IDistributedLeaseProvider leases)
{
    public async Task<TeamLabAssetControlAvailability> AvailabilityAsync(Guid runtimeId, int assetId, Guid actorId, bool administrator, CancellationToken token)
    {
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, TeamLabRuntimePermission.StateRead, token);
        var permissions = await authorization.EvaluateAsync(runtimeId, actorId, administrator, token);
        if (!permissions.HasFlag(TeamLabRuntimePermission.LifecycleManage)) return new(false, "当前账号没有资产生命周期管理权限。");
        var asset = await LoadAsync(runtimeId, assetId, token);
        try { await RequireRuntimeAsync(asset, asset.Runtime.Generation, token); }
        catch (TeamLabApiContractException error) { return new(false, error.Message); }
        if (!await context.TeamLabExecutionPlanSnapshots.AnyAsync(item => item.RuntimeId == asset.RuntimeId && item.Generation == asset.Generation && item.ShardId == asset.ShardId, token))
            return new(false, "资产缺少原始执行计划，不能猜测重建或电源管理。");
        return new(true, null);
    }

    public async Task<TeamLabAssetControlTask> GetTaskAsync(Guid runtimeId, int assetId, Guid ticketId, Guid actorId, bool administrator, CancellationToken token)
    {
        var (ticket, _) = await RequireTicketAsync(runtimeId, assetId, ticketId, actorId, administrator, token);
        return new(ticket.Id, ticket.Status.ToString().ToLowerInvariant(), ticket.StageMessage,
            ticket.ErrorCode, ticket.Status == DeploymentQueueTicketStatus.Failed && ticket.Retryable);
    }

    public async Task<TeamLabQueueTicketResult> RetryAsync(Guid runtimeId, int assetId, Guid ticketId, Guid actorId, bool administrator, CancellationToken token)
    {
        var (ticket, payload) = await RequireTicketAsync(runtimeId, assetId, ticketId, actorId, administrator, token);
        if (ticket.Status != DeploymentQueueTicketStatus.Failed || !ticket.Retryable)
            throw new TeamLabApiContractException("asset_control.not_retryable", "只有失败的资产操作可以继续执行。", 409);
        var asset = await LoadAsync(runtimeId, assetId, token);
        await RequireRuntimeAsync(asset, ticket.Generation, token);
        return await QueueAsync(asset, actorId, payload, token);
    }

    async Task<(DeploymentQueueTicket Ticket, TeamLabRuntimeOperationPayload Payload)> RequireTicketAsync(
        Guid runtimeId, int assetId, Guid ticketId, Guid actorId, bool administrator, CancellationToken token)
    {
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, TeamLabRuntimePermission.LifecycleManage, token);
        var ticket = await context.DeploymentQueueTickets.AsNoTracking().SingleOrDefaultAsync(item => item.Id == ticketId && item.Operation == RuntimeOperationKind.AssetControl, token);
        var payload = ticket?.ProtectedPayload is { } value ? protector.Unprotect(value) : null;
        if (ticket is null || payload?.RuntimeId != runtimeId || payload.AssetControl?.AssetId != assetId)
            throw new TeamLabApiContractException("asset_control.task_not_found", "未找到该资产的操作任务。", 404);
        return (ticket, payload);
    }

    public async Task<TeamLabQueueTicketResult> EnqueueAsync(Guid runtimeId, int assetId, Guid actorId, bool administrator,
        TeamLabAssetControlCommand command, CancellationToken token)
    {
        Validate(command);
        await authorization.RequirePermissionAsync(runtimeId, actorId, administrator, TeamLabRuntimePermission.LifecycleManage, token);
        var asset = await LoadAsync(runtimeId, assetId, token);
        await RequireRuntimeAsync(asset, command.Generation, token);
        if (command.Action != "rebuild" && string.IsNullOrWhiteSpace(asset.RuntimeResourceId))
            throw new TeamLabApiContractException("asset_control.resource_missing", "资产缺少执行身份，请使用重建。", 409);
        var payload = new TeamLabRuntimeOperationPayload(null, runtimeId, null)
        {
            AssetControl = new(assetId, command with { Reason = command.Reason.Trim() }, asset.RuntimeResourceId,
                asset.Kind == TeamLabResourceKind.Vm ? asset.NativeIdentity : null, WorkerNodeId: asset.WorkerNodeId!.Value)
        };
        return await QueueAsync(asset, actorId, payload, token);
    }

    Task<TeamLabQueueTicketResult> QueueAsync(TeamLabRuntimeAsset asset, Guid actorId, TeamLabRuntimeOperationPayload payload, CancellationToken token)
    {
        var command = payload.AssetControl!.Command;
        var runtimeId = asset.Runtime.PublicId;
        return queue.EnqueueAsync(new(asset.RuntimeId, 0, 0, actorId, null, runtimeId,
            WorkloadSchedulingIdentity.ForRuntime(asset.RuntimeId, $"teamlab-runtime:{asset.RuntimeId}", asset.Runtime.CreatedById),
            asset.Runtime.ExternalReference ?? runtimeId.ToString("D"), $"{asset.Name}: {command.Action}", command.Generation,
            RuntimeOperationKind.AssetControl, TargetNodeId: asset.WorkerNodeId, ProtectedPayload: protector.Protect(payload),
            PayloadHash: Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)))), token);
    }

    public async Task<TeamLabNodeResult> ExecuteAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.Operation != RuntimeOperationKind.AssetControl || ticket.ProtectedPayload is null || ticket.OwnerUserId is null)
            return TeamLabNodeResult.Failed("asset_control.invalid_ticket");
        var payload = protector.Unprotect(ticket.ProtectedPayload);
        var control = payload.AssetControl ?? throw new TeamLabApiContractException("asset_control.invalid_ticket", "资产操作负载缺失。", 409);
        Validate(control.Command);
        await using var lease = await leases.AcquireAsync($"teamlab:asset-files:{payload.RuntimeId:D}:{control.AssetId}", TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(30), token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lease.LeaseLost);
        token = linked.Token;
        var asset = await LoadAsync(payload.RuntimeId!.Value, control.AssetId, token);
        if (ticket.TeamLabRuntimeId != asset.RuntimeId || ticket.Generation != control.Command.Generation || control.WorkerNodeId != asset.WorkerNodeId)
            return TeamLabNodeResult.Failed("asset_control.identity_conflict");
        var actor = await context.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == ticket.OwnerUserId, token);
        if (actor is null || actor.Role == Role.Banned) return TeamLabNodeResult.Failed("asset_control.actor_unavailable");
        await authorization.RequirePermissionAsync(asset.Runtime.PublicId, actor.Id, actor.Role >= Role.Admin,
            TeamLabRuntimePermission.LifecycleManage, token);
        await RequireRuntimeAsync(asset, ticket.Generation, token);
        var snapshot = await context.TeamLabExecutionPlanSnapshots.AsNoTracking().SingleOrDefaultAsync(item =>
            item.RuntimeId == asset.RuntimeId && item.Generation == asset.Generation && item.ShardId == asset.ShardId, token)
            ?? throw new TeamLabApiContractException("asset_control.plan_missing", "资产缺少原始执行计划，不能猜测重建。", 409);
        var plan = JsonSerializer.Deserialize<TeamLabExecutionPlanV2>(snapshot.PlanJson)
            ?? throw new TeamLabApiContractException("asset_control.plan_invalid", "原始执行计划不可读取。", 409);
        if (snapshot.WorkerNodeId != asset.WorkerNodeId || !plan.IsValid(out _) || plan.RuntimeId != asset.RuntimeId || plan.Generation != asset.Generation)
            return TeamLabNodeResult.Failed("asset_control.identity_conflict");
        var steps = control.Command.Action switch
        {
            "restart" => new[] { "stop", "start" }, "rebuild" => ["remove", "create"],
            var action => [action]
        };
        if (control.Phase < 0 || control.Phase > steps.Length) return TeamLabNodeResult.Failed("asset_control.invalid_checkpoint");
        if (control.Phase == steps.Length) return TeamLabNodeResult.Ok();
        if (control.Phase == 0 && (asset.RuntimeResourceId != control.ResourceId ||
            asset.Kind == TeamLabResourceKind.Vm && asset.NativeIdentity != control.NativeIdentity))
            return TeamLabNodeResult.Failed("asset_control.identity_conflict");
        asset.DesiredPowerState = control.Command.Action switch { "stop" => "stopped", "pause" => "paused", _ => "running" };
        await context.SaveChangesAsync(token);
        await sessions.EndAssetSessionsAsync(asset.RuntimeId, asset.Id, asset.Generation, "asset-lifecycle", token);
        if (await context.TeamLabRemoteSessions.AsNoTracking().AnyAsync(item => item.RuntimeAssetId == asset.Id && item.Generation == asset.Generation &&
                item.Status != TeamLabRemoteSessionStatus.Ended && item.Status != TeamLabRemoteSessionStatus.Failed, token))
            return TeamLabNodeResult.Failed("asset_control.session_cleanup_pending");
        for (var phase = control.Phase; phase < steps.Length; phase++)
        {
            var step = steps[phase];
            var stepLabel = step switch { "start" => "启动", "stop" => "停止", "pause" => "暂停", "resume" => "恢复", "remove" => "清理旧资源", "create" => "创建新资源", _ => step };
            ticket.StageMessage = $"{asset.Name}：{stepLabel}";
            await context.SaveChangesAsync(token);
            var result = await gateway.ExecuteAsync(asset.WorkerNodeId!.Value,
                new(plan, asset.TopologyKey, step, control.ResourceId, control.NativeIdentity), token);
            var observed = result.Asset?.State switch { "exited" or "shutoff" => "stopped", null => "missing", var state => state };
            var expected = step switch { "remove" => "missing", "stop" => "stopped", "pause" => "paused", _ => "running" };
            if (result.Success && observed != expected)
                result = result with { Success = false, ErrorCode = "asset_control.state_not_reached" };
            if (result.Asset is { } returned && (returned.AssetKey != asset.TopologyKey || returned.Generation != asset.Generation ||
                step != "create" && (returned.ResourceId != control.ResourceId ||
                    control.NativeIdentity is not null && returned.NativeIdentity != control.NativeIdentity)))
                result = new(false, "asset_control.identity_conflict", null);
            if (result.Asset is { } actual && result.ErrorCode != "asset_control.identity_conflict")
            {
                asset.RuntimeResourceId = actual.ResourceId;
                asset.NativeIdentity = asset.Kind == TeamLabResourceKind.Vm ? actual.NativeIdentity : actual.ResourceId;
                asset.Status = actual.State switch
                {
                    "running" => TeamLabRuntimeStatus.Running, "paused" => TeamLabRuntimeStatus.Paused,
                    "exited" or "shutoff" => TeamLabRuntimeStatus.Stopped, _ => TeamLabRuntimeStatus.Failed
                };
            }
            else if (result.Success && step == "remove")
            {
                asset.RuntimeResourceId = null;
                asset.NativeIdentity = null;
                asset.SftpHostKeySha256 = null;
                asset.Status = TeamLabRuntimeStatus.Deploying;
            }
            asset.ExecutionUpdatedAt = DateTimeOffset.UtcNow;
            asset.LastError = result.Success ? null : result.ErrorCode ?? "asset_control.failed";
            events.Record(asset.Runtime, "asset-control", result.Success ? TeamLabEventLevel.Success : TeamLabEventLevel.Error,
                OperationalEventCodes.TeamLab.AssetControlled, result.Success ? OperationalEventOutcome.Succeeded : OperationalEventOutcome.Failed,
                $"资产 {asset.Name}：{stepLabel}{(result.Success ? "完成" : "失败")}", error: result.Success ? null : DescribeFailure(asset.LastError!), workerNodeId: asset.WorkerNodeId,
                detail: new Dictionary<string, object?> { ["assetId"] = asset.Id, ["actorUserId"] = actor.Id,
                    ["operation"] = control.Command.Action, ["stage"] = step, ["reason"] = control.Command.Reason, ["ticketId"] = ticket.Id });
            if (result.Success)
            {
                control = control with { Phase = phase + 1 };
                payload = payload with { AssetControl = control };
                ticket.ProtectedPayload = protector.Protect(payload);
            }
            await context.SaveChangesAsync(token);
            if (!result.Success) return TeamLabNodeResult.Failed(asset.LastError ?? "asset_control.failed");
        }
        return TeamLabNodeResult.Ok();
    }

    public static OperationalError DescribeFailure(string code) => new(OperationalErrorCategory.Unknown,
        code, "单资产操作未完成，请查看资产状态和任务事件。",
        code is "asset_control.execution_failed" or "asset_control.state_not_reached" or
            "asset_control.attachment_cleanup_failed" or "asset_control.power_failed" or "asset_control.session_cleanup_pending");

    static void Validate(TeamLabAssetControlCommand command)
    {
        if (command.Generation <= 0 || command.Action is not ("start" or "stop" or "restart" or "rebuild" or "pause" or "resume") ||
            string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length is < 4 or > 500)
            throw new TeamLabApiContractException("asset_control.invalid_request", "请选择操作并填写 4-500 字的操作原因。", 422);
        if (command.Action is not ("start" or "resume") && !command.Confirmed)
            throw new TeamLabApiContractException("asset_control.confirmation_required", "此操作会中断连接或替换磁盘，需要确认。", 422);
    }

    async Task<TeamLabRuntimeAsset> LoadAsync(Guid runtimeId, int assetId, CancellationToken token) =>
        await context.TeamLabRuntimeAssets.Include(item => item.Runtime)
            .SingleOrDefaultAsync(item => item.Id == assetId && item.Runtime.PublicId == runtimeId, token)
        ?? throw new TeamLabApiContractException("runtime_asset_not_found", "未找到运行资产。", 404);

    async Task RequireRuntimeAsync(TeamLabRuntimeAsset asset, int generation, CancellationToken token)
    {
        if (asset.Generation != generation || asset.Runtime.Generation != generation || asset.Runtime.Status is not (TeamLabRuntimeStatus.Running or TeamLabRuntimeStatus.Failed) ||
            asset.Runtime.ExecutionModel != TeamLabExecutionModel.V2 || asset.WorkerNodeId is null)
            throw new TeamLabApiContractException("asset_control.unavailable", "仅可操作当前代运行环境内、已分配节点的 V2 资产。", 409);
        if (await lifecycle.IsRolloutManagedAsync(asset.Runtime.PublicId, token))
            throw new TeamLabApiContractException("runtime_managed_by_rollout", "此环境由批量部署单管理，不能绕过部署单控制资产。", 409);
    }
}
