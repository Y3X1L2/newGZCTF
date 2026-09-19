using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabRuntimeUpdateService(
    AppDbContext context,
    TeamLabRuntimeOperationPayloadProtector payloads,
    TeamLabTopologyValidator topologyValidator,
    ITeamLabRuntimeQueue queue,
    FleetCapacityReservationService capacity,
    TeamLabRuntimeLifecycleGuard lifecycleGuard,
    TeamLabShardDeploymentService deployment,
    ITeamLabNodeExecutor nodes,
    ITeamLabAssetControlGateway assetControl,
    ITeamLabArtifactDistribution artifacts,
    ITeamLabRemoteAccessService remoteAccess,
    TeamLabServiceAccessService serviceAccess,
    TeamLabTrafficApplicationService traffic,
    TeamLabEventRecorder events,
    ILogger<TeamLabRuntimeUpdateService> logger)
{
    public async Task<TeamLabRuntimeUpdatePreviewModel> PreviewAsync(
        Guid runtimeId,
        Guid releaseId,
        CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (await lifecycleGuard.IsRolloutManagedAsync(runtimeId, token))
            throw new TeamLabApiContractException(
                "runtime_managed_by_rollout",
                "此运行时由比赛 rollout 管理，请使用比赛生命周期 API。",
                409);
        return (await BuildReleaseUpdateAsync(runtime, releaseId, token)).Preview;
    }

    public async Task<TeamLabQueueTicketResult> EnqueueAsync(
        Guid runtimeId,
        UpdateTeamLabRuntimeModel command,
        Guid actorUserId,
        Guid? operationId,
        CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (await lifecycleGuard.IsRolloutManagedAsync(runtimeId, token))
            throw new TeamLabApiContractException(
                "runtime_managed_by_rollout",
                "此运行时由比赛 rollout 管理，请使用比赛生命周期 API。",
                409);
        var plan = await BuildReleaseUpdateAsync(runtime, command.ReleaseId, token);
        var preview = plan.Preview;
        if (!preview.CanApply)
            throw new TeamLabApiContractException(
                "runtime_update_requires_reset",
                preview.ResetRequiredReason ?? "本次修改需要完整重置运行环境。",
                409);
        BuildOverlayValues(command.Overlays, preview.Changes);

        var payload = new TeamLabRuntimeOperationPayload(null, runtimeId, null)
        {
            ControlScopeId = runtime.ControlScopeId,
            Update = command
        };
        var protectedPayload = payloads.Protect(payload);
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(protectedPayload)));
        return await queue.EnqueueAsync(new TeamLabQueueRequest(
            runtime.Id,
            preview.Changes.Count(item => item.Kind == TeamLabAssetKind.Docker && item.Action is "add" or "replace"),
            preview.Changes.Count(item => item.Kind == TeamLabAssetKind.Vm && item.Action is "add" or "replace"),
            actorUserId,
            operationId,
            runtime.PublicId,
            WorkloadSchedulingIdentity.ForRuntime(runtime.Id, $"teamlab-runtime:{runtime.Id}", runtime.CreatedById),
            runtime.ExternalReference ?? runtime.PublicId.ToString("D"),
            $"运行修订 {runtime.PlanRevision + 1}",
            runtime.Generation,
            RuntimeOperationKind.Update,
            ProtectedPayload: protectedPayload,
            PayloadHash: payloadHash), token);
    }

    public async Task<TeamLabQueueTicketResult> EnqueueChangesAsync(
        Guid runtimeId,
        ChangeTeamLabRuntimeAssetsModel command,
        Guid actorUserId,
        Guid? operationId,
        CancellationToken token)
    {
        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .SingleOrDefaultAsync(item => item.PublicId == runtimeId, token)
            ?? throw new TeamLabApiContractException("runtime_not_found", "未找到 TeamLab 运行时", 404);
        if (await lifecycleGuard.IsRolloutManagedAsync(runtimeId, token))
            throw new TeamLabApiContractException(
                "runtime_managed_by_rollout",
                "此运行时由 rollout 管理，请通过 rollout 更新资产。",
                409);
        var plan = await BuildAssetChangesAsync(runtime, command, token);
        if (!plan.Preview.CanApply)
            throw new TeamLabApiContractException(
                "runtime_update_not_applicable",
                plan.Preview.ResetRequiredReason ?? "本次资产变更不能应用。",
                409);

        var payload = new TeamLabRuntimeOperationPayload(null, runtimeId, null)
        {
            ControlScopeId = runtime.ControlScopeId,
            AssetChanges = command
        };
        var protectedPayload = payloads.Protect(payload);
        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(protectedPayload)));
        return await queue.EnqueueAsync(new TeamLabQueueRequest(
            runtime.Id,
            plan.Preview.Changes.Count(item => item.Kind == TeamLabAssetKind.Docker && item.Action is "add" or "replace"),
            plan.Preview.Changes.Count(item => item.Kind == TeamLabAssetKind.Vm && item.Action is "add" or "replace"),
            actorUserId,
            operationId,
            runtime.PublicId,
            WorkloadSchedulingIdentity.ForRuntime(runtime.Id, $"teamlab-runtime:{runtime.Id}", runtime.CreatedById),
            runtime.ExternalReference ?? runtime.PublicId.ToString("D"),
            $"运行修订 {runtime.PlanRevision + 1}",
            runtime.Generation,
            RuntimeOperationKind.Update,
            ProtectedPayload: protectedPayload,
            PayloadHash: payloadHash), token);
    }

    public async Task<TeamLabNodeResult> ExecuteAsync(
        int runtimeId,
        Guid ticketId,
        string? protectedPayload,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(protectedPayload))
            return TeamLabNodeResult.Failed("runtime_update.payload_missing");
        var payload = payloads.Unprotect(protectedPayload);
        if ((payload.Update is null) == (payload.AssetChanges is null))
            return TeamLabNodeResult.Failed("runtime_update.payload_invalid");

        var ticket = await context.DeploymentQueueTickets.SingleOrDefaultAsync(
            item => item.Id == ticketId && item.TeamLabRuntimeId == runtimeId &&
                    item.Operation == RuntimeOperationKind.Update,
            token);
        if (ticket is null)
            return TeamLabNodeResult.Failed("runtime_update.ticket_invalid");

        var runtime = await LoadRuntimeAsync(runtimeId, token);
        if (runtime.Status != TeamLabRuntimeStatus.Running)
            return TeamLabNodeResult.Failed("runtime_update.runtime_not_running");

        var updatePlan = payload.Update is { } update
            ? await BuildReleaseUpdateAsync(runtime, update.ReleaseId, token)
            : await BuildAssetChangesAsync(runtime, payload.AssetChanges!, token);
        var preview = updatePlan.Preview;
        if (!preview.CanApply)
            return TeamLabNodeResult.Failed(preview.ResetRequiredReason ?? "runtime_update.requires_reset");

        var currentDefinition = updatePlan.Current;
        var targetDefinition = updatePlan.Target;
        var currentAssets = currentDefinition.Assets.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var targetAssets = targetDefinition.Assets.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var changes = preview.Changes;
        var overlayValues = BuildOverlayValues(payload.Update?.Overlays ?? payload.AssetChanges?.Overlays, changes);
        if (changes.Count == 0)
        {
            if (updatePlan.TargetRelease is null)
                return TeamLabNodeResult.Failed("runtime_update.no_changes");
            runtime.TopologyReleaseId = updatePlan.TargetRelease.Id;
            runtime.ControlScopeId = updatePlan.TargetRelease.ControlScopeId;
            runtime.UpdatedAt = DateTimeOffset.UtcNow;
            events.Record(runtime, "update", TeamLabEventLevel.Success,
                GZCTF.Modules.Audit.Domain.OperationalEventCodes.TeamLab.RuntimeUpdateSucceeded,
                GZCTF.Modules.Audit.Domain.OperationalEventOutcome.Succeeded,
                "运行环境已切换到内容相同的新发布版本。");
            await context.SaveChangesAsync(token);
            return TeamLabNodeResult.Ok("Runtime release updated without execution changes.");
        }

        var affectedKeys = changes.Select(item => item.AssetKey).ToHashSet(StringComparer.Ordinal);
        var trackedAssets = runtime.Assets
            .Where(item => item.Generation == runtime.Generation && affectedKeys.Contains(item.TopologyKey))
            .ToArray();
        var propertySnapshots = trackedAssets.ToDictionary(
            item => item.Id,
            item => context.Entry(item).CurrentValues.Clone());
        var oldRuntimeValues = context.Entry(runtime).CurrentValues.Clone();
        var currentPlans = runtime.ExecutionPlanSnapshots
            .Where(item => item.Generation == runtime.Generation)
            .ToDictionary(item => item.ShardId, item => ReadPlan(item.CurrentPlanJson ?? item.PlanJson));
        var oldPlans = trackedAssets
            .Where(item => item.Status != TeamLabRuntimeStatus.Destroyed)
            .ToDictionary(
                item => item.TopologyKey,
                item => currentPlans[item.ShardId ?? throw new TeamLabApiContractException(
                    "runtime_update_plan_missing", $"资产 {item.Name} 缺少执行分片。", 409)],
                StringComparer.Ordinal);

        var newAssets = new List<TeamLabRuntimeAsset>();
        var replacementAssets = new Dictionary<int, TeamLabRuntimeAsset>();
        var replacedAddresses = new HashSet<int>();
        var created = new List<(TeamLabRuntimeAsset Asset, TeamLabExecutionPlanV2 Plan)>();
        var removed = new List<(TeamLabRuntimeAsset Asset, TeamLabExecutionPlanV2 Plan, string? ResourceId, string? NativeIdentity)>();
        TeamLabExecutionPlanV2? currentNetworkPlan = null;
        TeamLabExecutionPlanV2? desiredNetworkPlan = null;
        Guid networkOwnerNodeId = Guid.Empty;
        var networkUpdated = false;
        try
        {
            var templates = await LoadTemplatesAsync(targetDefinition, token);
            var networks = runtime.Networks
                .Where(item => item.Generation == runtime.Generation)
                .ToDictionary(item => item.TopologyKey, StringComparer.Ordinal);
            var groups = TeamLabAssetPlanner.BuildGroups(targetDefinition);
            var groupByAsset = groups.SelectMany(group => group.AssetKeys.Select(key => (key, group.Key)))
                .ToDictionary(item => item.key, item => item.Key, StringComparer.Ordinal);
            var shardByNetwork = networks.ToDictionary(
                item => item.Key,
                item => item.Value.ShardId ?? throw new TeamLabApiContractException(
                    "runtime_update_placement_missing", "运行网段缺少节点分片。", 409),
                StringComparer.Ordinal);
            var capacityItems = BuildCapacityDelta(
                changes,
                currentAssets,
                targetAssets,
                runtime,
                shardByNetwork);
            if (capacityItems.Count > 0)
            {
                var reservation = await capacity.TryReserveBatchAsync(
                    ticketId, capacityItems, requireTeamLab: true, token);
                if (!reservation.Success)
                    throw new TeamLabRuntimeExecutionException(reservation.Message);
            }

            foreach (var change in changes)
            {
                var existing = runtime.Assets
                    .Where(item => item.Generation == runtime.Generation && item.TopologyKey == change.AssetKey)
                    .OrderByDescending(item => item.Status != TeamLabRuntimeStatus.Destroyed)
                    .ThenByDescending(item => item.Id)
                    .FirstOrDefault();
                if (change.Action == "remove")
                {
                    if (existing is null || !oldPlans.TryGetValue(change.AssetKey, out var oldPlan))
                        throw new TeamLabApiContractException("runtime_update_asset_missing", $"运行资产 {change.AssetKey} 不存在。", 409);
                    removed.Add((existing, oldPlan, existing.RuntimeResourceId, existing.NativeIdentity));
                    existing.Status = TeamLabRuntimeStatus.Destroying;
                    continue;
                }

                var target = targetAssets[change.AssetKey];
                var shardIds = target.Interfaces.Select(item => shardByNetwork[item.NetworkKey]).Distinct().ToArray();
                if (shardIds.Length != 1)
                    throw new TeamLabApiContractException(
                        "runtime_update_requires_reset",
                        $"资产 {target.Name} 会改变现有跨节点网络归属，需要完整重置。",
                        409);
                var shard = runtime.Shards.Single(item => item.Id == shardIds[0] && item.Generation == runtime.Generation);
                var desired = TeamLabRuntimePlanner.CreateRuntimeAsset(
                    runtime, target, groupByAsset[target.Key], networks, templates[target.ImageTemplateId]);
                desired.ShardId = shard.Id;
                desired.WorkerNodeId = shard.WorkerNodeId;
                desired.AgentOperationId = Guid.CreateVersion7();
                if (change.Action == "replace")
                {
                    if (existing is null || !oldPlans.TryGetValue(change.AssetKey, out var oldPlan))
                        throw new TeamLabApiContractException("runtime_update_asset_missing", $"运行资产 {change.AssetKey} 不存在。", 409);
                    removed.Add((existing, oldPlan, existing.RuntimeResourceId, existing.NativeIdentity));
                    existing.Status = TeamLabRuntimeStatus.Destroying;
                    replacementAssets[existing.Id] = desired;
                    if (!string.Equals(existing.IpAddress, desired.IpAddress, StringComparison.Ordinal))
                        replacedAddresses.Add(existing.Id);
                }
                else if (existing is { Status: TeamLabRuntimeStatus.Destroyed })
                {
                    CopyDesiredAsset(desired, existing);
                }
                else
                {
                    runtime.Assets.Add(desired);
                    newAssets.Add(desired);
                }
            }

            runtime.Status = TeamLabRuntimeStatus.Deploying;
            runtime.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);

            foreach (var replacement in replacementAssets)
                CopyDesiredAsset(replacement.Value, runtime.Assets.Single(item => item.Id == replacement.Key));

            var activeAssets = runtime.Assets
                .Where(item => item.Generation == runtime.Generation &&
                               targetAssets.ContainsKey(item.TopologyKey) &&
                               item.Status != TeamLabRuntimeStatus.Destroyed)
                .ToArray();
            var targetPlans = await deployment.CompileExecutionPlansAsync(
                runtime, targetDefinition, activeAssets, templates, overlayValues, token);
            var currentSnapshot = runtime.ExecutionPlanSnapshots.Single(item =>
                item.Generation == runtime.Generation && currentPlans[item.ShardId].NetworkOwner);
            currentNetworkPlan = currentPlans[currentSnapshot.ShardId];
            desiredNetworkPlan = targetPlans.Values.Single(item => item.NetworkOwner);
            networkOwnerNodeId = currentSnapshot.WorkerNodeId;

            await StopAffectedCapturesAsync(runtime, trackedAssets.Select(item => item.Id).ToArray(), token);
            foreach (var item in removed)
            {
                await remoteAccess.EndAssetSessionsAsync(
                    runtime.Id, item.Asset.Id, runtime.Generation, "runtime-update", token);
                var result = await assetControl.ExecuteAsync(
                    item.Asset.WorkerNodeId!.Value,
                    new(item.Plan, item.Asset.TopologyKey, "remove", item.ResourceId, item.NativeIdentity),
                    token);
                if (!result.Success)
                    throw new TeamLabRuntimeExecutionException(
                        $"资产 {item.Asset.Name} 移除失败：{result.ErrorCode ?? "asset_remove_failed"}");
                item.Asset.RuntimeResourceId = null;
                item.Asset.NativeIdentity = null;
                item.Asset.SftpHostKeySha256 = null;
                item.Asset.ExecutionUpdatedAt = DateTimeOffset.UtcNow;
            }

            var networkResult = await nodes.UpdateExecutionNetworkAsync(
                networkOwnerNodeId, currentNetworkPlan, desiredNetworkPlan, token);
            if (!networkResult.Success)
                throw new TeamLabRuntimeExecutionException(
                    networkResult.Message ?? networkResult.ErrorCode ?? "Network update failed.");
            networkUpdated = true;

            foreach (var change in changes.Where(item => item.Action is "add" or "replace"))
            {
                var asset = runtime.Assets.Single(item => item.Generation == runtime.Generation &&
                    item.Status != TeamLabRuntimeStatus.Destroyed && item.TopologyKey == change.AssetKey);
                var plan = targetPlans[asset.ShardId!.Value];
                var template = templates[asset.SourceTemplateId!.Value];
                await artifacts.EnsureImageAsync(runtime.Id, asset.WorkerNodeId!.Value, template, token);
                var result = await assetControl.ExecuteAsync(
                    asset.WorkerNodeId.Value,
                    new(plan, asset.TopologyKey, "create", null, null),
                    token);
                if (!result.Success || result.Asset is null)
                    throw new TeamLabRuntimeExecutionException(
                        $"资产 {asset.Name} 创建失败：{result.ErrorCode ?? "asset_create_failed"}");
                asset.RuntimeResourceId = result.Asset.ResourceId;
                asset.NativeIdentity = result.Asset.NativeIdentity ?? result.Asset.ResourceId;
                asset.Status = TeamLabRuntimeStatus.Running;
                asset.ExecutionStage = TeamLabAssetExecutionStage.ServiceReady;
                asset.LastError = null;
                asset.ExecutionUpdatedAt = DateTimeOffset.UtcNow;
                created.Add((asset, plan));
            }

            foreach (var assetId in replacedAddresses)
            {
                var accessIds = await context.TeamLabServiceAccesses.AsNoTracking()
                    .Where(item => item.RuntimeAssetId == assetId && item.RevokedAt == null)
                    .Select(item => item.PublicId)
                    .ToArrayAsync(token);
                foreach (var accessId in accessIds)
                    await serviceAccess.RemoveAsync(runtime.PublicId, accessId, token);
            }

            foreach (var change in changes.Where(item => item.Action == "remove"))
            {
                var asset = removed.Single(item => item.Asset.TopologyKey == change.AssetKey).Asset;
                var accessIds = await context.TeamLabServiceAccesses.AsNoTracking()
                    .Where(item => item.RuntimeAssetId == asset.Id && item.RevokedAt == null)
                    .Select(item => item.PublicId)
                    .ToArrayAsync(token);
                foreach (var accessId in accessIds)
                    await serviceAccess.RemoveAsync(runtime.PublicId, accessId, token);
                asset.Status = TeamLabRuntimeStatus.Destroyed;
                asset.LastError = null;
            }

            SyncWorkloadObservationPoints(runtime, changes);

            foreach (var snapshot in runtime.ExecutionPlanSnapshots.Where(item =>
                         item.Generation == runtime.Generation && targetPlans.ContainsKey(item.ShardId)))
                snapshot.CurrentPlanJson = JsonSerializer.Serialize(targetPlans[snapshot.ShardId]);
            if (updatePlan.TargetRelease is not null)
            {
                runtime.TopologyReleaseId = updatePlan.TargetRelease.Id;
                runtime.ControlScopeId = updatePlan.TargetRelease.ControlScopeId;
            }
            runtime.PlanRevision++;
            runtime.Status = TeamLabRuntimeStatus.Running;
            runtime.LastError = null;
            runtime.UpdatedAt = DateTimeOffset.UtcNow;
            foreach (var shard in runtime.Shards.Where(item => item.Generation == runtime.Generation))
            {
                shard.Status = TeamLabRuntimeStatus.Running;
                shard.LastError = null;
                shard.UpdatedAt = runtime.UpdatedAt;
            }
            events.Record(runtime, "update", TeamLabEventLevel.Success,
                GZCTF.Modules.Audit.Domain.OperationalEventCodes.TeamLab.RuntimeUpdateSucceeded,
                GZCTF.Modules.Audit.Domain.OperationalEventOutcome.Succeeded,
                $"运行环境修订 {runtime.PlanRevision} 已完成，共处理 {changes.Count} 个资产。");
            await context.SaveChangesAsync(token);
            return TeamLabNodeResult.Ok("Runtime assets updated.");
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            logger.LogWarning(exception, "TeamLab 运行环境 {RuntimeId} 热更新失败", runtime.PublicId);
            var rollbackErrors = new List<string>();
            foreach (var item in created.AsEnumerable().Reverse())
                try
                {
                    await assetControl.ExecuteAsync(item.Asset.WorkerNodeId!.Value,
                        new(item.Plan, item.Asset.TopologyKey, "remove", item.Asset.RuntimeResourceId, item.Asset.NativeIdentity), token);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException.Message);
                }
            if (networkUpdated && currentNetworkPlan is not null && desiredNetworkPlan is not null)
                try
                {
                    var result = await nodes.UpdateExecutionNetworkAsync(
                        networkOwnerNodeId, desiredNetworkPlan, currentNetworkPlan, token);
                    if (!result.Success) rollbackErrors.Add(result.Message ?? "Network rollback failed.");
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException.Message);
                }
            var restoredIdentities = new Dictionary<int, (string? ResourceId, string? NativeIdentity)>();
            foreach (var item in removed)
                try
                {
                    var result = await assetControl.ExecuteAsync(item.Asset.WorkerNodeId!.Value,
                        new(item.Plan, item.Asset.TopologyKey, "create", null, null), token);
                    if (!result.Success || result.Asset is null)
                        rollbackErrors.Add(result.ErrorCode ?? $"{item.Asset.TopologyKey} rollback failed");
                    else
                        restoredIdentities[item.Asset.Id] =
                            (result.Asset.ResourceId, result.Asset.NativeIdentity ?? result.Asset.ResourceId);
                }
                catch (Exception rollbackException)
                {
                    rollbackErrors.Add(rollbackException.Message);
                }

            foreach (var snapshot in propertySnapshots)
                context.Entry(trackedAssets.Single(item => item.Id == snapshot.Key)).CurrentValues.SetValues(snapshot.Value);
            foreach (var restored in restoredIdentities)
            {
                var asset = trackedAssets.Single(item => item.Id == restored.Key);
                asset.RuntimeResourceId = restored.Value.ResourceId;
                asset.NativeIdentity = restored.Value.NativeIdentity;
            }
            foreach (var asset in newAssets)
                context.TeamLabRuntimeAssets.Remove(asset);
            context.Entry(runtime).CurrentValues.SetValues(oldRuntimeValues);
            runtime.Status = rollbackErrors.Count == 0 ? TeamLabRuntimeStatus.Running : TeamLabRuntimeStatus.Failed;
            runtime.LastError = rollbackErrors.Count == 0
                ? null
                : string.Join("; ", rollbackErrors).Truncate(1024);
            runtime.UpdatedAt = DateTimeOffset.UtcNow;
            events.Record(runtime, "update", TeamLabEventLevel.Error,
                GZCTF.Modules.Audit.Domain.OperationalEventCodes.TeamLab.RuntimeUpdateFailed,
                GZCTF.Modules.Audit.Domain.OperationalEventOutcome.Failed,
                rollbackErrors.Count == 0
                    ? "运行环境更新失败，本次变更已撤销。"
                    : "运行环境更新失败，部分撤销操作未完成。",
                OperationalErrorClassifier.FromException(exception, "teamlab.runtime.update"));
            await context.SaveChangesAsync(CancellationToken.None);
            return TeamLabNodeResult.Failed(exception.Message);
        }
    }

    async Task<RuntimeUpdatePlan> BuildReleaseUpdateAsync(
        TeamLabRuntime runtime,
        Guid releaseId,
        CancellationToken token)
    {
        var currentRelease = await LoadReleaseAsync(runtime.TopologyReleaseId, token);
        var targetRelease = await LoadReleaseAsync(releaseId, token);
        var source = TeamLabReleaseCodec.DecodeExecution(currentRelease.SchemaVersion, currentRelease.CanonicalJson);
        var current = CurrentDefinition(runtime, source);
        var target = TeamLabReleaseCodec.DecodeExecution(targetRelease.SchemaVersion, targetRelease.CanonicalJson);
        var reason = runtime.Status != TeamLabRuntimeStatus.Running
            ? "只有运行中的环境可以更新资产。"
            : currentRelease.TopologyId != targetRelease.TopologyId
                ? "目标版本不属于当前场景，需要完整重置。"
                : targetRelease.IsArchived
                    ? "目标发布版本已归档。"
                    : !SameTopologyStructure(current, target)
                        ? "网段、路由或基础设施发生变化，需要完整重置。"
                        : !SameConnectorBindings(current, target)
                            ? "现场连接器发生变化，需要完整重置。"
                            : PlacementChangeReason(runtime, target);
        var preview = new TeamLabRuntimeUpdatePreviewModel(
            runtime.PublicId,
            currentRelease.Id,
            targetRelease.Id,
            runtime.PlanRevision,
            reason is null,
            reason,
            BuildChanges(current, target));
        return new(current, target, targetRelease, preview);
    }

    async Task<RuntimeUpdatePlan> BuildAssetChangesAsync(
        TeamLabRuntime runtime,
        ChangeTeamLabRuntimeAssetsModel command,
        CancellationToken token)
    {
        if (command.ExpectedPlanRevision != runtime.PlanRevision)
            throw new TeamLabApiContractException(
                "runtime_plan_revision_conflict",
                $"运行计划已经更新，当前修订为 {runtime.PlanRevision}。",
                409);
        var release = await LoadReleaseAsync(runtime.TopologyReleaseId, token);
        var source = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        var current = CurrentDefinition(runtime, source);
        var target = ApplyAssetChanges(current, command);
        var validation = topologyValidator.Validate(ToDefinition(target), target.SchemaVersion);
        if (!validation.Valid)
            throw TeamLabTopologyApplicationService.InvalidTopology(validation);
        await TeamLabTopologyApplicationService.ValidateImageTemplatesAsync(context, target, token);
        await TeamLabTopologyApplicationService.ValidateCapabilityResourcesAsync(context, ToDefinition(target), token);
        var changes = BuildChanges(current, target);
        if (changes.Count == 0)
            throw new TeamLabApiContractException("runtime_update_no_changes", "本次没有资产变更。", 422);
        BuildOverlayValues(command.Overlays, changes);
        var reason = runtime.Status != TeamLabRuntimeStatus.Running
            ? "只有运行中的环境可以更新资产。"
            : PlacementChangeReason(runtime, target);
        var preview = new TeamLabRuntimeUpdatePreviewModel(
            runtime.PublicId,
            release.Id,
            release.Id,
            runtime.PlanRevision,
            reason is null,
            reason,
            changes);
        return new(current, target, null, preview);
    }

    static TeamLabExecutionTopology CurrentDefinition(
        TeamLabRuntime runtime,
        TeamLabExecutionTopology source)
    {
        var assets = runtime.Assets
            .Where(item => item.Generation == runtime.Generation && item.Status != TeamLabRuntimeStatus.Destroyed)
            .OrderBy(item => item.Id)
            .Select(item => string.IsNullOrWhiteSpace(item.ExecutionPlanJson)
                ? throw new TeamLabApiContractException(
                    "runtime_asset_plan_missing",
                    $"运行资产 {item.Name} 缺少执行定义。",
                    409)
                : JsonSerializer.Deserialize<TeamLabExecutionAsset>(item.ExecutionPlanJson)
                  ?? throw new TeamLabApiContractException(
                      "runtime_asset_plan_invalid",
                      $"运行资产 {item.Name} 的执行定义无效。",
                      409))
            .ToArray();
        return source with { Assets = assets };
    }

    static TeamLabExecutionTopology ApplyAssetChanges(
        TeamLabExecutionTopology current,
        ChangeTeamLabRuntimeAssetsModel command)
    {
        var assets = current.Assets.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in command.Add ?? [])
        {
            RequireUniqueChange(requested, asset.Key);
            if (assets.ContainsKey(asset.Key))
                throw new TeamLabApiContractException("runtime_asset_exists", $"资产 {asset.Key} 已存在。", 409);
            assets.Add(asset.Key, TeamLabTopologyV1Normalizer.ToExecution(asset));
        }
        foreach (var asset in command.Replace ?? [])
        {
            RequireUniqueChange(requested, asset.Key);
            if (!assets.ContainsKey(asset.Key))
                throw new TeamLabApiContractException("runtime_asset_not_found", $"未找到资产 {asset.Key}。", 404);
            assets[asset.Key] = TeamLabTopologyV1Normalizer.ToExecution(asset);
        }
        foreach (var key in command.Remove ?? [])
        {
            RequireUniqueChange(requested, key);
            if (!assets.Remove(key))
                throw new TeamLabApiContractException("runtime_asset_not_found", $"未找到资产 {key}。", 404);
        }
        if (requested.Count == 0)
            throw new TeamLabApiContractException("runtime_update_no_changes", "请至少新增、替换或移除一个资产。", 422);
        return current with
        {
            Assets = assets.Values
                .OrderBy(item => item.DisplayOrder)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .ToArray()
        };
    }

    static void RequireUniqueChange(ISet<string> requested, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !requested.Add(key))
            throw new TeamLabApiContractException(
                "runtime_asset_change_duplicated",
                $"资产 {key} 在同一次变更中重复出现。",
                422);
    }

    static TeamLabTopologyDefinitionModel ToDefinition(TeamLabExecutionTopology topology) => new(
        topology.Name,
        topology.Networks.Select(item => new TeamLabTopologyNetworkModel(
            item.Key, item.Name, new TeamLabAddressPoolModel(item.AddressPoolCidr, item.RuntimePrefixLength),
            item.IsEntry, item.DisplayOrder)).ToArray(),
        topology.Assets.Select(item => new TeamLabTopologyAssetModel(
            item.Key, item.Name, item.Kind, item.ImageTemplateId,
            new TeamLabAssetResourceModel(item.CpuUnits, item.MemoryMiB, item.StorageMiB),
            item.Interfaces.Select(iface => new TeamLabTopologyInterfaceModel(
                iface.Key, iface.NetworkKey, iface.HostOffset, iface.Primary, iface.DisplayOrder)).ToArray(),
            item.ExposePort,
            item.HealthCheckKind is { } kind && item.HealthCheckPort is { } port
                ? new TeamLabHealthCheckModel(kind, port)
                : null,
            item.DisplayOrder,
            item.DevicePackageId,
            string.IsNullOrWhiteSpace(item.DeviceParametersJson)
                ? null
                : JsonDocument.Parse(item.DeviceParametersJson).RootElement.Clone(),
            item.ConnectorId)).ToArray(),
        topology.Connections.Select(item => new TeamLabTopologyConnectionModel(
            item.Key, item.FromNetworkKey, item.ToNetworkKey, item.ViaAssetKey,
            item.ViaNodeKey, item.Direction)).ToArray(),
        topology.Infrastructure.Select(item => new TeamLabTopologyInfrastructureModel(
            item.Key, item.Name, item.Kind,
            item.Interfaces.Select(iface => new TeamLabTopologyInterfaceModel(
                iface.Key, iface.NetworkKey, iface.HostOffset, iface.Primary, iface.DisplayOrder)).ToArray(),
            item.NetworkKey)).ToArray(),
        new TeamLabObservationPolicyModel(
            topology.Observation.FlowMetadataEnabled,
            topology.Observation.OnDemandPcapEnabled));

    internal static IReadOnlyList<TeamLabRuntimeUpdateChangeModel> BuildChanges(
        TeamLabExecutionTopology current,
        TeamLabExecutionTopology target)
    {
        var currentByKey = current.Assets.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var targetByKey = target.Assets.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var changes = new List<TeamLabRuntimeUpdateChangeModel>();
        foreach (var asset in target.Assets.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!currentByKey.TryGetValue(asset.Key, out var existing))
                changes.Add(new(asset.Key, asset.Name, asset.Kind, "add"));
            else if (!AssetEquals(existing, asset))
                changes.Add(new(asset.Key, asset.Name, asset.Kind, "replace"));
        }
        foreach (var asset in current.Assets.Where(item => !targetByKey.ContainsKey(item.Key))
                     .OrderBy(item => item.DisplayOrder).ThenBy(item => item.Key, StringComparer.Ordinal))
            changes.Add(new(asset.Key, asset.Name, asset.Kind, "remove"));
        return changes;
    }

    static bool AssetEquals(TeamLabExecutionAsset left, TeamLabExecutionAsset right) =>
        string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal);

    internal static bool SameTopologyStructure(TeamLabExecutionTopology current, TeamLabExecutionTopology target)
    {
        static string Shape(TeamLabExecutionTopology topology) => JsonSerializer.Serialize(new
        {
            topology.SchemaVersion,
            topology.Networks,
            topology.Infrastructure,
            topology.Connections,
            topology.Observation
        });
        return string.Equals(Shape(current), Shape(target), StringComparison.Ordinal);
    }

    internal static bool SameConnectorBindings(
        TeamLabExecutionTopology current,
        TeamLabExecutionTopology target)
    {
        static string Shape(TeamLabExecutionTopology topology) => JsonSerializer.Serialize(
            topology.Assets
                .Where(item => item.ConnectorId is not null)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new { item.Key, item.ConnectorId }));
        return string.Equals(Shape(current), Shape(target), StringComparison.Ordinal);
    }

    internal static IReadOnlyDictionary<string, TeamLabRuntimeOverlayModel> BuildOverlayValues(
        IReadOnlyList<TeamLabRuntimeOverlayModel>? overlays,
        IReadOnlyList<TeamLabRuntimeUpdateChangeModel> changes)
    {
        var eligibleKeys = changes
            .Where(item => item.Action is "add" or "replace")
            .Select(item => item.AssetKey)
            .ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, TeamLabRuntimeOverlayModel>(StringComparer.Ordinal);
        foreach (var overlay in overlays ?? [])
        {
            if (!eligibleKeys.Contains(overlay.AssetKey))
                throw new TeamLabApiContractException(
                    "runtime_update_overlay_not_applicable",
                    $"资产 {overlay.AssetKey} 未新增或替换，不能在本次更新中修改运行参数。",
                    400);
            if (!values.TryAdd(overlay.AssetKey, overlay))
                throw new TeamLabApiContractException(
                    "runtime_update_overlay_duplicated",
                    $"资产 {overlay.AssetKey} 的运行参数重复。",
                    400);
        }
        return values;
    }

    static string? PlacementChangeReason(TeamLabRuntime runtime, TeamLabExecutionTopology target)
    {
        var shardByNetwork = runtime.Networks
            .Where(item => item.Generation == runtime.Generation)
            .ToDictionary(item => item.TopologyKey, item => item.ShardId, StringComparer.Ordinal);
        var workerByShard = runtime.Shards
            .Where(item => item.Generation == runtime.Generation)
            .ToDictionary(item => item.Id, item => item.WorkerNodeId);
        var activeByKey = runtime.Assets
            .Where(item => item.Generation == runtime.Generation && item.Status != TeamLabRuntimeStatus.Destroyed)
            .ToDictionary(item => item.TopologyKey, StringComparer.Ordinal);
        foreach (var asset in target.Assets)
        {
            var shards = asset.Interfaces.Select(item => shardByNetwork.GetValueOrDefault(item.NetworkKey)).Distinct().ToArray();
            if (shards.Any(item => item is null) || shards.Length != 1)
                return $"资产 {asset.Name} 会改变现有跨节点网络归属，需要完整重置。";
            if (activeByKey.TryGetValue(asset.Key, out var current) &&
                current.WorkerNodeId != workerByShard[shards[0]!.Value])
                return $"资产 {asset.Name} 会迁移到其他节点，需要完整重置。";
        }
        return null;
    }

    static IReadOnlyList<FleetCapacityBatchItem> BuildCapacityDelta(
        IReadOnlyList<TeamLabRuntimeUpdateChangeModel> changes,
        IReadOnlyDictionary<string, TeamLabExecutionAsset> currentDefinitions,
        IReadOnlyDictionary<string, TeamLabExecutionAsset> targetDefinitions,
        TeamLabRuntime runtime,
        IReadOnlyDictionary<string, int> shardByNetwork)
    {
        var currentAssets = runtime.Assets
            .Where(item => item.Generation == runtime.Generation && item.Status != TeamLabRuntimeStatus.Destroyed)
            .ToDictionary(item => item.TopologyKey, StringComparer.Ordinal);
        var workerByShard = runtime.Shards
            .Where(item => item.Generation == runtime.Generation)
            .ToDictionary(item => item.Id, item => item.WorkerNodeId);
        var deltas = new Dictionary<Guid, WorkloadResourceVector>();
        foreach (var change in changes)
        {
            if (change.Action is "remove" or "replace")
            {
                var current = currentAssets[change.AssetKey];
                Add(current.WorkerNodeId!.Value, Negate(Resource(currentDefinitions[change.AssetKey])));
            }
            if (change.Action is "add" or "replace")
            {
                var target = targetDefinitions[change.AssetKey];
                var shardId = target.Interfaces.Select(item => shardByNetwork[item.NetworkKey]).Distinct().Single();
                Add(workerByShard[shardId], Resource(target));
            }
        }

        return deltas
            .Select(item => new FleetCapacityBatchItem(item.Key, Positive(item.Value)))
            .Where(item => item.Resources != WorkloadResourceVector.Zero)
            .ToArray();

        void Add(Guid nodeId, WorkloadResourceVector value) =>
            deltas[nodeId] = deltas.GetValueOrDefault(nodeId) + value;
    }

    static WorkloadResourceVector Resource(TeamLabExecutionAsset asset) => new(
        asset.CpuUnits,
        asset.MemoryMiB,
        asset.StorageMiB,
        asset.Kind == TeamLabAssetKind.Docker ? 1 : 0,
        asset.Kind == TeamLabAssetKind.Vm ? 1 : 0);

    static WorkloadResourceVector Positive(WorkloadResourceVector value) => new(
        Math.Max(0, value.CpuUnits),
        Math.Max(0, value.MemoryMiB),
        Math.Max(0, value.StorageMiB),
        Math.Max(0, value.DockerSlots),
        Math.Max(0, value.VmSlots));

    static WorkloadResourceVector Negate(WorkloadResourceVector value) => new(
        -value.CpuUnits,
        -value.MemoryMiB,
        -value.StorageMiB,
        -value.DockerSlots,
        -value.VmSlots);

    async Task<TeamLabRuntime> LoadRuntimeAsync(int runtimeId, CancellationToken token) =>
        await context.TeamLabRuntimes
            .Include(item => item.Shards)
            .Include(item => item.Networks)
            .Include(item => item.Assets)
            .Include(item => item.Infrastructure).ThenInclude(item => item.Fragments)
            .Include(item => item.ObservationPoints)
            .Include(item => item.FabricLinkLeases)
            .Include(item => item.ExecutionPlanSnapshots)
            .Include(item => item.Events)
            .SingleAsync(item => item.Id == runtimeId, token);

    async Task<TeamLabTopologyRelease> LoadReleaseAsync(Guid releaseId, CancellationToken token) =>
        await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == releaseId, token)
        ?? throw new TeamLabApiContractException("release_not_found", "未找到拓扑版本", 404);

    async Task<Dictionary<int, ImageTemplate>> LoadTemplatesAsync(
        TeamLabExecutionTopology definition,
        CancellationToken token)
    {
        await TeamLabTopologyApplicationService.ValidateImageTemplatesAsync(context, definition, token);
        var ids = definition.Assets.Select(item => item.ImageTemplateId).Distinct().ToArray();
        return await context.ImageTemplates.AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, token);
    }

    static TeamLabExecutionPlanV2 ReadPlan(string? json)
    {
        var plan = string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<TeamLabExecutionPlanV2>(json);
        if (plan is null || !plan.IsValid(out _))
            throw new TeamLabApiContractException("runtime_update_plan_invalid", "运行执行快照不可读取。", 409);
        return plan;
    }

    static void CopyDesiredAsset(TeamLabRuntimeAsset source, TeamLabRuntimeAsset target)
    {
        target.PlacementGroupKey = source.PlacementGroupKey;
        target.Kind = source.Kind;
        target.Name = source.Name;
        target.SourceTemplateId = source.SourceTemplateId;
        target.Image = source.Image;
        target.NetworkKey = source.NetworkKey;
        target.IpAddress = source.IpAddress;
        target.MacAddress = source.MacAddress;
        target.InterfaceSummaryJson = source.InterfaceSummaryJson;
        target.ImageDigest = source.ImageDigest;
        target.DevicePackageId = source.DevicePackageId;
        target.DevicePackageParametersJson = source.DevicePackageParametersJson;
        target.ConnectorId = source.ConnectorId;
        target.ExecutionPlanJson = source.ExecutionPlanJson;
        target.ShardId = source.ShardId;
        target.WorkerNodeId = source.WorkerNodeId;
        target.AgentOperationId = source.AgentOperationId;
        target.RuntimeResourceId = null;
        target.NativeIdentity = null;
        target.SftpHostKeySha256 = null;
        target.Status = TeamLabRuntimeStatus.Pending;
        target.ExecutionStage = TeamLabAssetExecutionStage.Pending;
        target.DesiredPowerState = "running";
        target.LastError = null;
    }

    internal static void SyncWorkloadObservationPoints(
        TeamLabRuntime runtime,
        IReadOnlyCollection<TeamLabRuntimeUpdateChangeModel> changes)
    {
        var changedKeys = changes.Select(item => item.AssetKey).ToHashSet(StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        foreach (var point in runtime.ObservationPoints.Where(item =>
                     item.Generation == runtime.Generation &&
                     item.Kind == TeamLabObservationPointKind.WorkloadEndpoint &&
                     changedKeys.Contains(item.TopologyKey)))
        {
            point.Enabled = false;
            point.UpdatedAt = now;
        }

        var networks = runtime.Networks
            .Where(item => item.Generation == runtime.Generation)
            .ToDictionary(item => item.TopologyKey, StringComparer.Ordinal);
        foreach (var asset in runtime.Assets.Where(item =>
                     item.Generation == runtime.Generation &&
                     item.Status == TeamLabRuntimeStatus.Running &&
                     changedKeys.Contains(item.TopologyKey)))
        {
            var interfaces = JsonSerializer.Deserialize<RuntimeInterfaceIntent[]>(asset.InterfaceSummaryJson) ?? [];
            foreach (var iface in interfaces)
            {
                var interfaceToken = asset.Kind == TeamLabResourceKind.Vm
                    ? TeamLabExecutionIdentityV2.VmTapName(
                        runtime.PublicId, runtime.Generation, asset.TopologyKey, iface.NetworkKey)
                    : TeamLabExecutionIdentityV2.WorkloadHostInterface(
                        runtime.PublicId, runtime.Generation, asset.TopologyKey, iface.NetworkKey);
                var point = runtime.ObservationPoints.FirstOrDefault(item =>
                    item.Generation == runtime.Generation &&
                    item.Kind == TeamLabObservationPointKind.WorkloadEndpoint &&
                    item.AssetId == asset.Id &&
                    item.InterfaceToken == interfaceToken);
                if (point is null)
                {
                    runtime.ObservationPoints.Add(new TeamLabObservationPoint
                    {
                        RuntimeId = runtime.Id,
                        Generation = runtime.Generation,
                        WorkerNodeId = asset.WorkerNodeId!.Value,
                        ShardId = asset.ShardId,
                        NetworkId = networks[iface.NetworkKey].Id,
                        AssetId = asset.Id,
                        Kind = TeamLabObservationPointKind.WorkloadEndpoint,
                        TopologyKey = asset.TopologyKey,
                        InterfaceToken = interfaceToken
                    });
                }
                else
                {
                    point.Enabled = true;
                    point.WorkerNodeId = asset.WorkerNodeId!.Value;
                    point.ShardId = asset.ShardId;
                    point.NetworkId = networks[iface.NetworkKey].Id;
                    point.UpdatedAt = now;
                }
            }
        }
    }

    async Task StopAffectedCapturesAsync(
        TeamLabRuntime runtime,
        IReadOnlyCollection<int> assetIds,
        CancellationToken token)
    {
        if (assetIds.Count == 0) return;
        var ids = await context.TeamLabTrafficCaptureJobs.AsNoTracking()
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation &&
                           item.Segments.Any(segment => segment.ObservationPoint.AssetId != null &&
                               assetIds.Contains(segment.ObservationPoint.AssetId.Value)) &&
                           item.Status != TeamLabTrafficCaptureStatus.Completed &&
                           item.Status != TeamLabTrafficCaptureStatus.Failed &&
                           item.Status != TeamLabTrafficCaptureStatus.Expired)
            .Select(item => item.PublicId)
            .ToArrayAsync(token);
        foreach (var id in ids)
            await traffic.StopCaptureAsync(runtime.PublicId, id, token);
    }
}

file static class TeamLabRuntimeUpdateStringExtensions
{
    public static string Truncate(this string value, int length) =>
        value.Length <= length ? value : value[..length];
}

file sealed record RuntimeInterfaceIntent(string NetworkKey);

internal sealed record RuntimeUpdatePlan(
    TeamLabExecutionTopology Current,
    TeamLabExecutionTopology Target,
    TeamLabTopologyRelease? TargetRelease,
    TeamLabRuntimeUpdatePreviewModel Preview);
