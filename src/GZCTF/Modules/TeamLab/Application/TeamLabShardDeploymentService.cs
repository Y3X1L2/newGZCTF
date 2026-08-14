using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services;
using GZCTF.Modules.Runtime.Domain;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabShardDeploymentService(
    AppDbContext context,
    IServiceScopeFactory scopeFactory,
    ITeamLabNodeExecutor executor,
    DockerImageRegistryService dockerRegistry,
    TeamLabRouteApplicationService routes,
    TeamLabEventRecorder eventRecorder,
    ITeamLabDeploymentProgress stageMachine,
    ILogger<TeamLabShardDeploymentService> logger)
{

    public async Task DeployAsync(
        TeamLabRuntime runtime,
        TeamLabExecutionTopology definition,
        IReadOnlyDictionary<string, TeamLabRuntimeOverlayModel> overlays,
        CancellationToken cancellationToken)
    {
        var currentShards = runtime.Shards.Where(item => item.Generation == runtime.Generation).ToArray();
        var runtimeAssets = runtime.Assets
            .Where(item => item.Generation == runtime.Generation)
            .OrderBy(item => item.TopologyKey, StringComparer.Ordinal)
            .ToArray();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(item => runtimeAssets.Select(asset => asset.SourceTemplateId).Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        IReadOnlyDictionary<(int? ShardId, int TemplateId), Task>? imagePreparation = null;
        var shardNodes = runtime.Shards.ToDictionary(item => item.Id, item => item.WorkerNodeId);
        IReadOnlyDictionary<(int? ShardId, int TemplateId), Task> StartImagePreparation()
        {
            if (imagePreparation is not null) return imagePreparation;
            imagePreparation = runtimeAssets
                .Where(asset => asset.SourceTemplateId.HasValue)
                .Select(asset => (asset.ShardId, TemplateId: asset.SourceTemplateId!.Value))
                .Distinct()
                .ToDictionary(
                    item => item,
                    item => PrepareImageAsync(
                        runtime.Id,
                        shardNodes[item.ShardId!.Value],
                        templates[item.TemplateId], cancellationToken));
            return imagePreparation;
        }
        await stageMachine.SetAsync(
            TeamLabDeploymentStage.ArtifactsVerifying,
            "Verifying runtime images and bootstrap artifacts on assigned nodes.",
            cancellationToken);
        foreach (var asset in runtimeAssets.Where(item => item.AgentOperationId is null))
            asset.AgentOperationId = Guid.CreateVersion7();
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            switch (runtime.ExecutionModel)
            {
                case TeamLabExecutionModel.V2:
                    await stageMachine.SetAsync(
                        TeamLabDeploymentStage.NetworkApplying,
                        "Applying the versioned TeamLab execution plan.",
                        cancellationToken);
                    await ApplyExecutionPlansAsync(runtime, definition, runtimeAssets, templates,
                        overlays, StartImagePreparation, cancellationToken);
                    await context.SaveChangesAsync(cancellationToken);
                    break;
                case TeamLabExecutionModel.V1:
                    imagePreparation = StartImagePreparation();
                    await stageMachine.SetAsync(
                        TeamLabDeploymentStage.NetworkApplying,
                        "Applying managed TeamLab network desired state.",
                        cancellationToken);
                    await routes.ApplyAsync(runtime, definition, cancellationToken);
                    break;
                default:
                    throw new TeamLabRuntimeExecutionException(
                        $"不支持的 TeamLab 执行模型 {runtime.ExecutionModel}。");
            }
        }
        catch
        {
            if (imagePreparation is not null)
                await ObserveImagePreparationAsync(imagePreparation.Values);
            throw;
        }
        await stageMachine.SetAsync(
            TeamLabDeploymentStage.RoutesApplying,
            "Managed routes and directional policies are ready.",
            cancellationToken);
        eventRecorder.Record(
            runtime,
            "network",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.NetworkApplied,
            OperationalEventOutcome.Succeeded,
            "Runtime shard networks were applied.",
            detail: new Dictionary<string, object?>
            {
                ["generation"] = runtime.Generation,
                ["stage"] = "network",
                ["executionModel"] = runtime.ExecutionModel.ToString(),
                ["shardCount"] = currentShards.Length,
                ["assetCount"] = runtimeAssets.Length
            });
        await context.SaveChangesAsync(cancellationToken);

        eventRecorder.Record(
            runtime,
            "route",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.RouteApplied,
            OperationalEventOutcome.Succeeded,
            "Runtime routes were applied.",
            detail: new Dictionary<string, object?>
            {
                ["generation"] = runtime.Generation,
                ["stage"] = "route",
                ["executionModel"] = runtime.ExecutionModel.ToString(),
                ["routeCount"] = definition.Connections.Count,
                ["shardCount"] = currentShards.Length
            });
        await context.SaveChangesAsync(cancellationToken);
        MarkDependenciesSatisfied(runtime, null, TeamLabDependencyCondition.NetworkReady);
        await context.SaveChangesAsync(cancellationToken);

        if (runtime.ExecutionModel == TeamLabExecutionModel.V2)
        {
            await CompleteV2ReadinessAsync(runtime, runtimeAssets, cancellationToken);
            return;
        }

        var topologyAssets = definition.Assets.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var legacyPreparedImages = imagePreparation ?? StartImagePreparation();
        var allowedRoutes = BuildAllowedRoutes(runtime, definition);
        var builtRequests = await Task.WhenAll(runtimeAssets.Select(item => BuildAssetRequest(
            runtime,
            item,
            topologyAssets[item.TopologyKey],
            templates[item.SourceTemplateId!.Value],
            overlays.GetValueOrDefault(item.TopologyKey),
            allowedRoutes,
            imageReady: true,
            cancellationToken)));
        var work = runtimeAssets.Zip(builtRequests)
            .ToDictionary(pair => pair.First.TopologyKey,
                pair => new AssetWork(pair.First, pair.Second),
                StringComparer.Ordinal);
        var graph = TeamLabDependencyGraph.Compile(definition);
        var completed = TeamLabDependencyGraph.RestoreCompletedNodes(runtimeAssets);
        var scheduled = new HashSet<string>(StringComparer.Ordinal);
        while (completed.Count < graph.Count)
        {
            if (!graph.TryTakeReadyBatch(completed, scheduled, out var batch))
                throw new TeamLabRuntimeExecutionException(
                    $"TeamLab dependency execution stalled: {string.Join("; ", graph.DescribeBlocked(completed, scheduled))}");
            foreach (var node in batch) scheduled.Add(node.Key);
            await SetBatchStageAsync(batch, cancellationToken);
            var tasks = batch.Select(async node =>
            {
                var item = work[node.AssetKey];
                var request = item.Request with
                {
                    DependencyReadyToken = BuildDependencyReadyToken(runtime, node.AssetKey)
                };
                return await ExecuteNodeAsync(
                    runtime,
                    node,
                    item.Asset,
                    request,
                    legacyPreparedImages,
                    cancellationToken);
            }).ToArray();
            var results = await Task.WhenAll(tasks);
            var orderedResults = results.OrderBy(item => item.Node.Key, StringComparer.Ordinal).ToArray();
            foreach (var result in orderedResults)
            {
                if (result.Success)
                {
                    ApplyNodeSuccess(runtime, result);
                    completed.Add(result.Node.Key);
                    scheduled.Remove(result.Node.Key);
                    RecordAssetEvent(runtime, result, success: true);
                    continue;
                }

                result.Asset.ExecutionStage = TeamLabAssetExecutionStage.Failed;
                result.Asset.Status = TeamLabRuntimeStatus.Failed;
                result.Asset.LastError = Trim(result.Message);
                result.Asset.ExecutionUpdatedAt = DateTimeOffset.UtcNow;
                MarkDependenciesFailed(runtime, result.Asset.TopologyKey, result.Message);
                RecordAssetEvent(runtime, result, success: false);
            }
            await context.SaveChangesAsync(cancellationToken);
            var failures = orderedResults.Where(item => !item.Success).ToArray();
            if (failures.Length > 0)
                throw new TeamLabRuntimeExecutionException(string.Join("; ", failures.Select(item =>
                    $"{item.Node.Key}: {item.Message}")));
        }

        await VerifyRuntimeInventoryAsync(runtimeAssets, cancellationToken);

        runtime.Status = TeamLabRuntimeStatus.Probing;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        eventRecorder.Record(
            runtime,
            "probe",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.ProbeSucceeded,
            OperationalEventOutcome.Succeeded,
            "Runtime asset probes completed successfully.");
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteV2ReadinessAsync(
        TeamLabRuntime runtime,
        IReadOnlyCollection<TeamLabRuntimeAsset> runtimeAssets,
        CancellationToken cancellationToken)
    {
        await VerifyRuntimeInventoryAsync(runtimeAssets, cancellationToken);
        foreach (var asset in runtimeAssets)
        {
            asset.ExecutionStage = TeamLabAssetExecutionStage.ServiceReady;
            asset.Status = TeamLabRuntimeStatus.Running;
            asset.LastError = null;
            asset.ExecutionUpdatedAt = DateTimeOffset.UtcNow;
            MarkDependenciesSatisfied(runtime, asset.TopologyKey, TeamLabDependencyCondition.GuestReady);
            MarkDependenciesSatisfied(runtime, asset.TopologyKey, TeamLabDependencyCondition.ServiceReady);
        }
        runtime.Status = TeamLabRuntimeStatus.Probing;
        runtime.UpdatedAt = DateTimeOffset.UtcNow;
        eventRecorder.Record(
            runtime,
            "probe",
            TeamLabEventLevel.Success,
            OperationalEventCodes.TeamLab.ProbeSucceeded,
            OperationalEventOutcome.Succeeded,
            "Versioned execution-plan asset readiness checks completed successfully.");
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyExecutionPlansAsync(
        TeamLabRuntime runtime,
        TeamLabExecutionTopology definition,
        IReadOnlyCollection<TeamLabRuntimeAsset> runtimeAssets,
        IReadOnlyDictionary<int, ImageTemplate> templates,
        IReadOnlyDictionary<string, TeamLabRuntimeOverlayModel> overlays,
        Func<IReadOnlyDictionary<(int? ShardId, int TemplateId), Task>> startImagePreparation,
        CancellationToken cancellationToken)
    {
        var shards = runtime.Shards.Where(item => item.Generation == runtime.Generation).ToArray();
        var nodeIds = shards.Select(item => item.WorkerNodeId).ToArray();
        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(item => nodeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var hasVm = runtimeAssets.Any(item => item.Kind == TeamLabResourceKind.Vm);
        var required = new List<string>
        {
            AgentFeatureIds.TeamLabExecutionPlan,
            AgentFeatureIds.TeamLabOvnOvs,
            AgentFeatureIds.TeamLabArtifactCache
        };
        if (hasVm) required.Add(AgentFeatureIds.TeamLabNativeLibvirt);
        if (runtimeAssets.Any(item => item.Kind == TeamLabResourceKind.Docker))
            required.Add(AgentFeatureIds.Docker);
        var requiredFeatures = required.Distinct().ToArray();
        foreach (var shard in shards)
        {
            if (!nodes.TryGetValue(shard.WorkerNodeId, out var node))
                throw new TeamLabRuntimeExecutionException(
                    $"执行计划节点 {shard.WorkerNodeId} 不存在，无法部署 V2 执行模型。");
            var missing = AgentCapabilityEvaluator.MissingFeatures(node, requiredFeatures);
            if (missing.Length > 0)
            {
                logger.LogWarning(
                    "TeamLab V2 deployment rejected on node {NodeId} ({NodeName}): missing {Features}",
                    node.Id, node.Name, missing);
                throw new TeamLabRuntimeExecutionException(
                    $"节点 {node.Name} 缺少 V2 执行能力：{string.Join("、", missing)}");
            }
        }

        // Runtime overlays currently travel on the legacy injection path. V2 plans carry immutable
        // execution facts only, so a user secret can never be delivered by the V2 Agent. Fail
        // loudly instead of silently falling back to V1.
        var unsupportedSecret = TeamLabExecutionModelPolicy.FindUnsupportedSecretKey(overlays.Values);
        if (unsupportedSecret is not null)
            throw new TeamLabRuntimeExecutionException(
                $"V2 执行模型不支持运行时密钥覆盖，资产密钥 '{unsupportedSecret}' 无法投递。");

        foreach (var asset in runtimeAssets)
            if (asset.SourceTemplateId is not { } templateId ||
                !templates.TryGetValue(templateId, out var template) ||
                string.IsNullOrWhiteSpace(template.ImageHash))
                throw new TeamLabRuntimeExecutionException(
                    $"Immutable image digest is missing for asset '{asset.TopologyKey}'.");

        var plans = await CompileExecutionPlansAsync(
            runtime, definition, runtimeAssets, templates, overlays, cancellationToken);

        await Task.WhenAll(startImagePreparation().Values);

        // Persist the execution-plan identity before contacting any Agent. A process interruption
        // after an Agent has accepted the plan must still select the V2 cleanup path.
        var now = DateTimeOffset.UtcNow;
        var existingSnapshots = await context.TeamLabExecutionPlanSnapshots
            .Where(item => item.RuntimeId == runtime.Id && item.Generation == runtime.Generation)
            .ToDictionaryAsync(item => item.ShardId, cancellationToken);
        foreach (var shard in shards)
        {
            var plan = plans[shard.Id];
            var planJson = JsonSerializer.Serialize(plan);
            if (existingSnapshots.TryGetValue(shard.Id, out var snapshot))
            {
                if (!string.Equals(snapshot.PlanDigest, plan.PlanDigest, StringComparison.Ordinal))
                    throw new TeamLabRuntimeIdentityConflictException(
                        $"Execution-plan identity conflict for shard {shard.Id}. " +
                        "The accepted generation is immutable; reset the runtime to create a new generation.");
            }
            else
            {
                context.TeamLabExecutionPlanSnapshots.Add(new TeamLabExecutionPlanSnapshot
                {
                    RuntimeId = runtime.Id,
                    Generation = runtime.Generation,
                    ShardId = shard.Id,
                    WorkerNodeId = shard.WorkerNodeId,
                    PlanDigest = plan.PlanDigest,
                    PlanJson = planJson
                });
            }
            foreach (var fragment in runtime.Infrastructure
                         .Where(item => item.Generation == runtime.Generation)
                         .SelectMany(item => item.Fragments)
                         .Where(item => item.ShardId == shard.Id))
            {
                fragment.Status = TeamLabRuntimeStatus.Deploying;
                fragment.NativeResourceId = $"execution-plan-v2/{shard.Id}";
                fragment.DesiredStateDigest = plan.PlanDigest;
                fragment.LastError = null;
                fragment.UpdatedAt = now;
            }
        }
        await context.SaveChangesAsync(cancellationToken);

        var results = await Task.WhenAll(shards.Select(shard => ApplyExecutionPlanAsync(
            shard.WorkerNodeId, plans[shard.Id], cancellationToken)));
        var failed = results.Where(item => !item.Success).ToArray();
        if (failed.Length > 0)
        {
            var identityConflict = failed.FirstOrDefault(item =>
                string.Equals(item.ErrorCategory, "validation", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ErrorCode, "identity_conflict", StringComparison.OrdinalIgnoreCase));
            if (identityConflict is not null)
                throw new TeamLabRuntimeIdentityConflictException(
                    "Execution-plan identity conflict. Reset the runtime to create a new generation.");
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var compensationError = await CompensateExecutionPlansAsync(
                results, cleanupTimeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            var failure = string.Join("; ", failed.Select(item =>
                $"node {item.WorkerNodeId}: {item.Message}"));
            throw new TeamLabRuntimeExecutionException(
                compensationError is null
                    ? $"Execution plan apply failed: {failure}"
                    : $"Execution plan apply failed: {failure}; compensation failed: {compensationError}");
        }

        var inventoryFailures = results.SelectMany(result => result.Plan.Assets
                .Where(asset => !result.Response!.Inventory.Any(item =>
                    string.Equals(item.AssetKey, asset.AssetKey, StringComparison.Ordinal)))
                .Select(asset => $"node {result.WorkerNodeId} is missing asset '{asset.AssetKey}' in inventory"))
            .ToArray();
        if (inventoryFailures.Length > 0)
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var compensationError = await CompensateExecutionPlansAsync(results, cleanupTimeout.Token);
            cancellationToken.ThrowIfCancellationRequested();
            var failure = string.Join("; ", inventoryFailures);
            throw new TeamLabRuntimeExecutionException(
                compensationError is null
                    ? $"Execution plan inventory verification failed: {failure}"
                    : $"Execution plan inventory verification failed: {failure}; compensation failed: {compensationError}");
        }

        foreach (var result in results)
        {
            var resultShardIds = shards.Where(shard => shard.WorkerNodeId == result.WorkerNodeId)
                .Select(shard => shard.Id)
                .ToHashSet();
            foreach (var asset in runtimeAssets.Where(item =>
                         item.ShardId is { } shardId && resultShardIds.Contains(shardId)))
            {
                var actual = result.Response!.Inventory.First(item =>
                    string.Equals(item.AssetKey, asset.TopologyKey, StringComparison.Ordinal));
                asset.RuntimeResourceId = actual.ResourceId;
                asset.NativeIdentity = actual.ResourceId;
                asset.ExecutionStage = asset.Kind == TeamLabResourceKind.Vm
                    ? TeamLabAssetExecutionStage.Pending
                    : TeamLabAssetExecutionStage.GuestReady;
                asset.Status = asset.Kind == TeamLabResourceKind.Vm
                    ? TeamLabRuntimeStatus.Deploying
                    : TeamLabRuntimeStatus.Probing;
                asset.LastError = null;
                asset.ExecutionUpdatedAt = DateTimeOffset.UtcNow;
            }
            foreach (var infrastructureItem in runtime.Infrastructure.Where(item =>
                         item.Generation == runtime.Generation &&
                         item.Fragments.Any(fragment => resultShardIds.Contains(fragment.ShardId))))
            foreach (var fragment in infrastructureItem.Fragments.Where(fragment =>
                         resultShardIds.Contains(fragment.ShardId)))
            {
                fragment.Status = TeamLabRuntimeStatus.Running;
                fragment.NativeResourceId = $"execution-plan-v2/{fragment.ShardId}";
                fragment.DesiredStateDigest = result.Plan.PlanDigest;
                fragment.LastError = null;
                fragment.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }
    private async Task<ExecutionPlanApplyResult> ApplyExecutionPlanAsync(
        Guid workerNodeId,
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await executor.ApplyExecutionPlanAsync(workerNodeId, plan, cancellationToken);
            if (response.Success)
                return new ExecutionPlanApplyResult(workerNodeId, plan, response, null);
            var message = FailureDetail(response.Events) ?? response.Message ?? "Agent rejected the execution plan.";
            logger.LogWarning("TeamLab execution plan apply failed for node {WorkerNodeId}, runtime {RuntimeId}: {Message}",
                workerNodeId, plan.RuntimeId, message);
            return new ExecutionPlanApplyResult(workerNodeId, plan, null, message,
                response.ErrorCategory, response.ErrorCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ExecutionPlanApplyResult(workerNodeId, plan, null, "Execution plan apply was cancelled.");
        }
        catch (Exception exception)
        {
            return new ExecutionPlanApplyResult(workerNodeId, plan, null, exception.Message);
        }
    }

    private async Task<string?> CompensateExecutionPlansAsync(
        IEnumerable<ExecutionPlanApplyResult> applied,
        CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(applied.Select(async item =>
        {
            try
            {
                var cleanup = await executor.CleanupExecutionPlanAsync(
                    item.WorkerNodeId, item.Plan, cancellationToken);
                var failure = FailureDetail(cleanup.Events) ?? cleanup.Message;
                return cleanup.Success ? null : $"node {item.WorkerNodeId}: {failure}";
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return $"node {item.WorkerNodeId}: {exception.Message}";
            }
            catch (OperationCanceledException)
            {
                return $"node {item.WorkerNodeId}: execution-plan compensation timed out";
            }
        }));
        var failures = results.Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
        return failures.Length == 0 ? null : string.Join("; ", failures!);
    }

    static string? FailureDetail(IReadOnlyList<TeamLabExecutionEventV2> events) =>
        events.Where(item => item.Outcome == "failed")
            .Select(item => item.Detail is { } detail &&
                            detail.TryGetValue("summary", out var summary) ? summary : null)
            .FirstOrDefault(summary => !string.IsNullOrWhiteSpace(summary));

    private sealed record ExecutionPlanApplyResult(
        Guid WorkerNodeId,
        TeamLabExecutionPlanV2 Plan,
        TeamLabExecutionPlanApplyResponse? Response,
        string? Message,
        string? ErrorCategory = null,
        string? ErrorCode = null)
    {
        public bool Success => Response is not null;
    }

    public async Task<IReadOnlyDictionary<int, TeamLabExecutionPlanV2>> CompileExecutionPlansAsync(
        TeamLabRuntime runtime,
        TeamLabExecutionTopology definition,
        CancellationToken cancellationToken)
    {
        var runtimeAssets = runtime.Assets
            .Where(item => item.Generation == runtime.Generation)
            .OrderBy(item => item.TopologyKey, StringComparer.Ordinal)
            .ToArray();
        var templateIds = runtimeAssets
            .Where(item => item.SourceTemplateId.HasValue)
            .Select(item => item.SourceTemplateId!.Value)
            .Distinct()
            .ToArray();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(item => templateIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (templates.Count != templateIds.Length)
            throw new TeamLabRuntimeExecutionException("One or more execution-plan image templates are missing.");

        return await CompileExecutionPlansAsync(
            runtime, definition, runtimeAssets, templates,
            new Dictionary<string, TeamLabRuntimeOverlayModel>(StringComparer.Ordinal),
            cancellationToken);
    }

    private async Task<IReadOnlyDictionary<int, TeamLabExecutionPlanV2>> CompileExecutionPlansAsync(
        TeamLabRuntime runtime,
        TeamLabExecutionTopology definition,
        IReadOnlyCollection<TeamLabRuntimeAsset> runtimeAssets,
        IReadOnlyDictionary<int, ImageTemplate> templates,
        IReadOnlyDictionary<string, TeamLabRuntimeOverlayModel> overlays,
        CancellationToken cancellationToken)
    {
        var infrastructure = await routes.BuildInfrastructureRequestsAsync(
            runtime, definition, cancellationToken);
        var allowedRoutes = BuildAllowedRoutes(runtime, definition);
        var digests = runtimeAssets
            .Where(item => item.SourceTemplateId.HasValue)
            .GroupBy(item => item.SourceTemplateId!.Value)
            .ToDictionary(group => group.Key, group =>
            {
                var values = group.Select(item => NormalizeImageDigest(item.ImageDigest ?? string.Empty))
                    .Distinct(StringComparer.Ordinal).ToArray();
                if (values.Length != 1)
                    throw new TeamLabRuntimeExecutionException(
                        $"Runtime has conflicting frozen image digests for template {group.Key}.");
                return values[0];
            });
        var plans = new Dictionary<int, TeamLabExecutionPlanV2>();
        foreach (var shard in runtime.Shards.Where(item => item.Generation == runtime.Generation))
        {
            var shardAssets = await Task.WhenAll(runtimeAssets.Where(item => item.ShardId == shard.Id)
                .OrderBy(item => item.TopologyKey, StringComparer.Ordinal)
                .Select(asset => BuildAssetRequest(
                    runtime,
                    asset,
                    definition.Assets.Single(item => item.Key == asset.TopologyKey),
                    templates[asset.SourceTemplateId!.Value],
                    overlays.GetValueOrDefault(asset.TopologyKey),
                    allowedRoutes,
                    imageReady: true,
                    cancellationToken)));
            plans.Add(shard.Id, TeamLabExecutionPlanCompiler.Compile(
                runtime.Id,
                runtime.PublicId,
                runtime.Generation,
                shard.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                infrastructure[shard.Id],
                shardAssets,
                digests));
        }
        return plans;
    }

    private static string NormalizeImageDigest(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return trimmed.ToLowerInvariant();
        if (trimmed.Length == 64 && trimmed.All(Uri.IsHexDigit))
            return $"sha256:{trimmed.ToLowerInvariant()}";
        throw new TeamLabRuntimeExecutionException("Image template digest is not a SHA-256 digest.");
    }

    private async Task VerifyRuntimeInventoryAsync(
        IReadOnlyCollection<TeamLabRuntimeAsset> assets,
        CancellationToken cancellationToken)
    {
        var nodeAssets = assets
            .Where(item => item.WorkerNodeId.HasValue && !string.IsNullOrWhiteSpace(item.RuntimeResourceId))
            .GroupBy(item => item.WorkerNodeId!.Value)
            .ToArray();
        var snapshots = await Task.WhenAll(nodeAssets.Select(async group =>
            (Assets: group.ToArray(), Inventory: await executor.GetRuntimeInventoryAsync(group.Key, cancellationToken))));
        var failures = new List<string>();

        foreach (var snapshot in snapshots)
        foreach (var asset in snapshot.Assets)
        {
            var actual = asset.Kind == TeamLabResourceKind.Docker
                ? snapshot.Inventory.Containers.FirstOrDefault(item =>
                    item.NativeId.Equals(asset.RuntimeResourceId, StringComparison.Ordinal))
                : snapshot.Inventory.Vms.FirstOrDefault(item =>
                    item.StableName.Equals(asset.RuntimeResourceId, StringComparison.Ordinal));
            if (actual is null)
            {
                failures.Add($"{asset.TopologyKey}: runtime resource is missing from node inventory");
                continue;
            }
            if (actual.Generation != asset.Generation)
            {
                failures.Add(
                    $"{asset.TopologyKey}: generation {actual.Generation} does not match {asset.Generation}");
                continue;
            }
            if (!actual.State.Equals("running", StringComparison.OrdinalIgnoreCase))
                failures.Add($"{asset.TopologyKey}: runtime resource state is {actual.State}");
        }

        if (failures.Count > 0)
            throw new TeamLabRuntimeExecutionException(
                $"Runtime inventory validation failed: {string.Join("; ", failures)}");
    }

    async Task PrepareImageAsync(
        int runtimeId,
        Guid workerNodeId,
        ImageTemplate template,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var scopedArtifacts = scope.ServiceProvider.GetRequiredService<ITeamLabArtifactDistribution>();
        await scopedArtifacts.EnsureImageAsync(runtimeId, workerNodeId, template, cancellationToken);
    }

    private async Task<TeamLabNodeAssetCreateRequest> BuildAssetRequest(
        TeamLabRuntime runtime,
        TeamLabRuntimeAsset asset,
        TeamLabExecutionAsset topologyAsset,
        ImageTemplate template,
        TeamLabRuntimeOverlayModel? overlay,
        IReadOnlyDictionary<string, IReadOnlyList<string>> allowedRoutes,
        bool imageReady,
        CancellationToken cancellationToken)
    {
        var shard = runtime.Shards.Single(item => item.Id == asset.ShardId);
        var parsedInterfaces = ParseInterfaces(asset).ToArray();
        var primaryInterface = parsedInterfaces.SingleOrDefault(iface => iface.Primary) ?? parsedInterfaces.FirstOrDefault();
        var interfaces = parsedInterfaces.Select(iface =>
        {
            var network = runtime.Networks.Single(item => item.Generation == runtime.Generation && item.TopologyKey == iface.NetworkKey);
            IReadOnlyList<string> dnsServers = iface.Key == primaryInterface?.Key
                ? [network.GatewayIp]
                : [];
            return new TeamLabNodeInterfaceIntent(
                iface.Key, iface.NetworkKey, network.BridgeName, iface.IpAddress, iface.PrefixLength,
                iface.MacAddress, iface.Primary,
                allowedRoutes.GetValueOrDefault(iface.NetworkKey) ?? [],
                dnsServers);
        }).ToArray();
        var secrets = overlay?.Secrets ?? new Dictionary<string, string>();
        var imageReference = topologyAsset.Kind == TeamLabAssetKind.Docker
            ? await dockerRegistry.ResolveImageReferenceAsync(
                asset.Image ?? DockerImageReference.ResolvePullTarget(template.Name, template.RegistryUrl).FullImage,
                cancellationToken)
            : null;
        return new TeamLabNodeAssetCreateRequest(
                runtime.Id, asset.Id, runtime.PublicId, runtime.Generation, asset.TopologyKey, asset.Name, topologyAsset.Kind,
                asset.SourceTemplateId ?? topologyAsset.ImageTemplateId, topologyAsset.CpuUnits, topologyAsset.MemoryMiB,
                topologyAsset.StorageMiB, topologyAsset.ExposePort, imageReady, secrets, interfaces,
                topologyAsset.HealthCheckKind is { } healthKind
                    ? new TeamLabNodeHealthIntent(healthKind, topologyAsset.HealthCheckPort)
                    : null,
                null,
                topologyAsset.EndpointObservation,
                TeamLabResourceNameFactory.RouterNamespace(runtime.Id, shard.Id),
                asset.AgentOperationId,
                topologyAsset.Kind == TeamLabAssetKind.Vm ? template.VmRuntimeMode : null,
                topologyAsset.Kind == TeamLabAssetKind.Vm ? template.VmNetworkMode : null,
                topologyAsset.Kind == TeamLabAssetKind.Docker
                    ? imageReference
                    : null);
    }

    private async Task<NodeExecution> ExecuteNodeAsync(
        TeamLabRuntime runtime,
        TeamLabDeploymentNode node,
        TeamLabRuntimeAsset asset,
        TeamLabNodeAssetCreateRequest request,
        IReadOnlyDictionary<(int? ShardId, int TemplateId), Task> imagePreparation,
        CancellationToken cancellationToken)
    {
        var shard = runtime.Shards.Single(item => item.Id == asset.ShardId);
        try
        {
            switch (node.Kind)
            {
                case TeamLabDeploymentNodeKind.Create:
                    if (asset.SourceTemplateId is { } templateId)
                        await imagePreparation[(asset.ShardId, templateId)];
                    var created = await executor.CreateAssetAsync(
                        shard.WorkerNodeId, request, cancellationToken);
                    return new NodeExecution(
                        node, asset, request, created.Success, created.Message,
                        created.RuntimeResourceId, created.NativeIdentity);
                case TeamLabDeploymentNodeKind.GuestReady:
                    if (string.IsNullOrWhiteSpace(asset.RuntimeResourceId))
                        return NodeExecution.Failed(node, asset, request,
                            "Runtime asset identity is missing before guest readiness.");
                    var ready = await executor.WaitForAssetReadyAsync(
                        shard.WorkerNodeId, asset.RuntimeResourceId, request, cancellationToken);
                    return new NodeExecution(
                        node, asset, request, ready.Success, ready.Message,
                        asset.RuntimeResourceId, asset.NativeIdentity);
                case TeamLabDeploymentNodeKind.Health:
                    if (string.IsNullOrWhiteSpace(asset.RuntimeResourceId))
                        return NodeExecution.Failed(node, asset, request,
                            "Runtime asset identity is missing before health probing.");
                    var health = await executor.ProbeAssetHealthAsync(
                        shard.WorkerNodeId, asset.RuntimeResourceId, request, cancellationToken);
                    return new NodeExecution(
                        node, asset, request, health.Success, health.Message,
                        asset.RuntimeResourceId, asset.NativeIdentity);
                default:
                    throw new ArgumentOutOfRangeException(nameof(node.Kind));
            }
        }
        catch (Exception exception) when (
            exception is IOperationalFailureException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return NodeExecution.Failed(node, asset, request, exception.Message);
        }
    }

    private void ApplyNodeSuccess(TeamLabRuntime runtime, NodeExecution result)
    {
        result.Asset.LastError = null;
        result.Asset.ExecutionUpdatedAt = DateTimeOffset.UtcNow;
        switch (result.Node.Kind)
        {
            case TeamLabDeploymentNodeKind.Create:
                result.Asset.RuntimeResourceId = result.RuntimeResourceId;
                result.Asset.NativeIdentity = result.NativeIdentity;
                if (result.Asset.Kind == TeamLabResourceKind.Vm)
                {
                    result.Asset.ExecutionStage = TeamLabAssetExecutionStage.Pending;
                    result.Asset.Status = TeamLabRuntimeStatus.Deploying;
                }
                else
                {
                    result.Asset.ExecutionStage = TeamLabAssetExecutionStage.GuestReady;
                    result.Asset.Status = TeamLabRuntimeStatus.Probing;
                    MarkDependenciesSatisfied(
                        runtime, result.Asset.TopologyKey, TeamLabDependencyCondition.GuestReady);
                }
                break;
            case TeamLabDeploymentNodeKind.GuestReady:
                result.Asset.ExecutionStage = TeamLabAssetExecutionStage.GuestReady;
                result.Asset.Status = TeamLabRuntimeStatus.Probing;
                MarkDependenciesSatisfied(
                    runtime, result.Asset.TopologyKey, TeamLabDependencyCondition.GuestReady);
                break;
            case TeamLabDeploymentNodeKind.Health:
                result.Asset.ExecutionStage = TeamLabAssetExecutionStage.ServiceReady;
                result.Asset.Status = TeamLabRuntimeStatus.Running;
                MarkDependenciesSatisfied(
                    runtime, result.Asset.TopologyKey, TeamLabDependencyCondition.ServiceReady);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result.Node.Kind));
        }
    }

    private async Task SetBatchStageAsync(
        IReadOnlyList<TeamLabDeploymentNode> batch,
        CancellationToken cancellationToken)
    {
        var kinds = batch.Select(item => item.Kind).Distinct().Order().ToArray();
        var kind = kinds[0];
        var (stage, message) = kind switch
        {
            TeamLabDeploymentNodeKind.Create =>
                (TeamLabDeploymentStage.AssetBooting, "Creating independent runtime assets in parallel."),
            TeamLabDeploymentNodeKind.GuestReady =>
                (TeamLabDeploymentStage.AssetBooting, "Waiting for VM guest readiness signals."),
            TeamLabDeploymentNodeKind.Health =>
                (TeamLabDeploymentStage.HealthProbing, "Running service and network health probes."),
            _ => throw new ArgumentOutOfRangeException()
        };
        if (kinds.Length > 1)
            message = $"Executing {batch.Count} ready DAG nodes across {string.Join(", ", kinds.Select(item => item.ToString().ToLowerInvariant()))} stages.";
        await stageMachine.SetAsync(stage, message, cancellationToken);
    }

    private static void MarkDependenciesSatisfied(
        TeamLabRuntime runtime,
        string? dependsOnKey,
        TeamLabDependencyCondition condition)
    {
        foreach (var state in runtime.DependencyStates.Where(item =>
                     item.Generation == runtime.Generation && item.Condition == condition &&
                     (condition == TeamLabDependencyCondition.NetworkReady || item.DependsOnKey == dependsOnKey)))
        {
            state.Status = TeamLabDependencyStateStatus.Satisfied;
            state.SatisfiedAt = DateTimeOffset.UtcNow;
            state.LastError = null;
        }
    }

    private static void MarkDependenciesFailed(
        TeamLabRuntime runtime,
        string dependsOnKey,
        string message)
    {
        foreach (var state in runtime.DependencyStates.Where(item =>
                     item.Generation == runtime.Generation && item.DependsOnKey == dependsOnKey &&
                     item.Status == TeamLabDependencyStateStatus.Pending))
        {
            state.Status = TeamLabDependencyStateStatus.Failed;
            state.LastError = Trim(message);
        }
    }

    private static string? BuildDependencyReadyToken(TeamLabRuntime runtime, string assetKey)
    {
        var facts = runtime.DependencyStates.Where(item =>
                item.Generation == runtime.Generation && item.AssetKey == assetKey &&
                item.Status == TeamLabDependencyStateStatus.Satisfied)
            .OrderBy(item => item.DependsOnKey, StringComparer.Ordinal)
            .ThenBy(item => item.Condition)
            .Select(item => new { item.DependsOnKey, item.Condition, item.SatisfiedAt })
            .ToArray();
        return facts.Length == 0
            ? null
            : $"sha256:{Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(facts)))}";
    }

    private void RecordAssetEvent(TeamLabRuntime runtime, NodeExecution result, bool success)
    {
        var shard = runtime.Shards.Single(item => item.Id == result.Asset.ShardId);
        var (successCode, failureCode, category, errorCode) = result.Node.Kind switch
        {
            TeamLabDeploymentNodeKind.Create => (
                OperationalEventCodes.TeamLab.AssetCreated,
                OperationalEventCodes.TeamLab.AssetCreateFailed,
                result.Asset.Kind == TeamLabResourceKind.Vm
                    ? OperationalErrorCategory.Kvm
                    : OperationalErrorCategory.Docker,
                result.Asset.Kind == TeamLabResourceKind.Vm
                    ? OperationalErrorCodes.KvmOperationFailed
                    : OperationalErrorCodes.DockerOperationFailed),
            TeamLabDeploymentNodeKind.GuestReady => (
                OperationalEventCodes.TeamLab.GuestReady,
                OperationalEventCodes.TeamLab.GuestReadinessFailed,
                OperationalErrorCategory.Kvm,
                OperationalErrorCodes.KvmOperationFailed),
            TeamLabDeploymentNodeKind.Health => (
                OperationalEventCodes.TeamLab.HealthSucceeded,
                OperationalEventCodes.TeamLab.HealthFailed,
                OperationalErrorCategory.HealthCheck,
                OperationalErrorCodes.HealthProbeTimeout),
            _ => throw new ArgumentOutOfRangeException()
        };
        eventRecorder.Record(
            runtime,
            result.Node.Kind.ToString().ToLowerInvariant(),
            success ? TeamLabEventLevel.Success : TeamLabEventLevel.Error,
            success ? successCode : failureCode,
            success ? OperationalEventOutcome.Succeeded : OperationalEventOutcome.Failed,
            success
                ? $"Runtime asset stage {result.Node.Kind} completed."
                : $"Runtime asset stage {result.Node.Kind} failed.",
            success
                ? null
                : new OperationalError(
                    category,
                    errorCode,
                    "TeamLab asset stage failed.",
                    true,
                    WorkerNodeId: shard.WorkerNodeId,
                    Operation: $"teamlab.asset.{result.Node.Kind.ToString().ToLowerInvariant()}"),
            shard.WorkerNodeId,
            new Dictionary<string, object?>
            {
                ["generation"] = runtime.Generation,
                ["stage"] = result.Node.Kind.ToString(),
                ["assetKey"] = result.Asset.TopologyKey,
                ["imageType"] = result.Asset.Kind.ToString()
            });
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildAllowedRoutes(
        TeamLabRuntime runtime,
        TeamLabExecutionTopology definition)
    {
        var networkByKey = runtime.Networks.Where(item => item.Generation == runtime.Generation)
            .ToDictionary(item => item.TopologyKey, StringComparer.Ordinal);
        var targets = networkByKey.Keys.ToDictionary(key => key, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var connection in definition.Connections)
        {
            if (!networkByKey.ContainsKey(connection.FromNetworkKey) || !networkByKey.ContainsKey(connection.ToNetworkKey)) continue;
            targets[connection.FromNetworkKey].Add(networkByKey[connection.ToNetworkKey].Cidr);
            targets[connection.ToNetworkKey].Add(networkByKey[connection.FromNetworkKey].Cidr);
        }
        return targets.ToDictionary(
            item => item.Key,
            item => (IReadOnlyList<string>)item.Value.Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    }

    private static RuntimeInterfaceIntent[] ParseInterfaces(TeamLabRuntimeAsset asset) =>
        JsonSerializer.Deserialize<RuntimeInterfaceIntent[]>(asset.InterfaceSummaryJson) ?? [];

    private static async Task ObserveImagePreparationAsync(IEnumerable<Task> tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Infrastructure failure remains the primary deployment failure.
        }
    }

    private static string Trim(string value) => value.Length <= 1024 ? value : value[..1024];

    private sealed record AssetWork(
        TeamLabRuntimeAsset Asset,
        TeamLabNodeAssetCreateRequest Request);

    private sealed record NodeExecution(
        TeamLabDeploymentNode Node,
        TeamLabRuntimeAsset Asset,
        TeamLabNodeAssetCreateRequest Request,
        bool Success,
        string Message,
        string? RuntimeResourceId,
        string? NativeIdentity)
    {
        public static NodeExecution Failed(
            TeamLabDeploymentNode node,
            TeamLabRuntimeAsset asset,
            TeamLabNodeAssetCreateRequest request,
            string message) => new(
            node, asset, request, false, message, asset.RuntimeResourceId, asset.NativeIdentity);
    }
    private sealed record RuntimeInterfaceIntent(
        string Key,
        string NetworkKey,
        string IpAddress,
        int PrefixLength,
        string MacAddress,
        bool Primary);
}
