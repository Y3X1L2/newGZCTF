using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services;
using GZCTF.Modules.Runtime.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabShardDeploymentService(
    AppDbContext context,
    IServiceScopeFactory scopeFactory,
    ITeamLabNodeExecutor executor,
    TeamLabRouteApplicationService routes,
    TeamLabEventRecorder eventRecorder,
    ITeamLabDeploymentProgress stageMachine,
    TeamLabBootstrapOrchestrator bootstrapOrchestrator)
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
        var imagePreparation = runtimeAssets
            .Where(asset => asset.SourceTemplateId.HasValue)
            .Select(asset => (asset.ShardId, TemplateId: asset.SourceTemplateId!.Value))
            .Distinct()
            .ToDictionary(
                item => item,
                item => PrepareImageAsync(
                    runtime.Id,
                    runtime.Shards.Single(shard => shard.Id == item.ShardId).WorkerNodeId,
                    templates[item.TemplateId], cancellationToken));
        await stageMachine.SetAsync(
            TeamLabDeploymentStage.ArtifactsVerifying,
            "Verifying runtime images and bootstrap artifacts on assigned nodes.",
            cancellationToken);
        try
        {
            await stageMachine.SetAsync(
                TeamLabDeploymentStage.NetworkApplying,
                "Applying managed TeamLab network desired state.",
                cancellationToken);
            await routes.ApplyAsync(runtime, definition, cancellationToken);
        }
        catch
        {
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
                ["routeCount"] = definition.Connections.Count,
                ["shardCount"] = currentShards.Length
            });
        await context.SaveChangesAsync(cancellationToken);
        MarkDependenciesSatisfied(runtime, null, TeamLabDependencyCondition.NetworkReady);
        await context.SaveChangesAsync(cancellationToken);

        foreach (var asset in runtimeAssets.Where(item => item.AgentOperationId is null))
            asset.AgentOperationId = Guid.CreateVersion7();
        await context.SaveChangesAsync(cancellationToken);

        var topologyAssets = definition.Assets.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var allowedRoutes = BuildAllowedRoutes(runtime, definition);
        var work = runtimeAssets.ToDictionary(
            item => item.TopologyKey,
            item => new AssetWork(
                item,
                BuildAssetRequest(
                    runtime,
                    item,
                    topologyAssets[item.TopologyKey],
                    templates[item.SourceTemplateId!.Value],
                    overlays.GetValueOrDefault(item.TopologyKey),
                    allowedRoutes,
                    imageReady: true)),
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
                    imagePreparation,
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
                if (result.Node.Kind == TeamLabDeploymentNodeKind.Bootstrap)
                    bootstrapOrchestrator.RecordFailure(runtime, result.Asset, result.Request, result.Message);
                RecordAssetEvent(runtime, result, success: false);
            }
            await context.SaveChangesAsync(cancellationToken);
            var failures = orderedResults.Where(item => !item.Success).ToArray();
            if (failures.Length > 0)
                throw new TeamLabRuntimeExecutionException(string.Join("; ", failures.Select(item =>
                    $"{item.Node.Key}: {item.Message}")));
        }

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

    private static TeamLabNodeAssetCreateRequest BuildAssetRequest(
        TeamLabRuntime runtime,
        TeamLabRuntimeAsset asset,
        TeamLabExecutionAsset topologyAsset,
        ImageTemplate template,
        TeamLabRuntimeOverlayModel? overlay,
        IReadOnlyDictionary<string, IReadOnlyList<string>> allowedRoutes,
        bool imageReady)
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
        var environment = topologyAsset.Environment
            .Concat(overlay?.Environment ?? new Dictionary<string, string>())
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.Ordinal);
        var secrets = overlay?.Secrets ?? new Dictionary<string, string>();
        var scenarioArtifact = topologyAsset.BakeAtPublish && !runtime.IsScenarioBuild;
        return new TeamLabNodeAssetCreateRequest(
                runtime.Id, runtime.PublicId, runtime.Generation, asset.TopologyKey, asset.Name, topologyAsset.Kind,
                asset.SourceTemplateId ?? topologyAsset.ImageTemplateId, topologyAsset.CpuUnits, topologyAsset.MemoryMiB,
                topologyAsset.StorageMiB, topologyAsset.ExposePort, topologyAsset.RoutingEnabled, imageReady,
                environment, secrets, interfaces,
                topologyAsset.Bootstrap is null || scenarioArtifact
                    ? null
                    : new TeamLabNodeBootstrapIntent(
                        topologyAsset.Bootstrap.ProfileId,
                        topologyAsset.Bootstrap.Version,
                        topologyAsset.Bootstrap.Parameters),
                topologyAsset.HealthCheckKind is { } healthKind
                    ? new TeamLabNodeHealthIntent(healthKind, topologyAsset.HealthCheckPort)
                    : null,
                null,
                topologyAsset.EndpointObservation,
                TeamLabResourceNameFactory.RouterNamespace(runtime.Id, shard.Id),
                topologyAsset.StartCommand,
                asset.AgentOperationId,
                topologyAsset.Kind == TeamLabAssetKind.Vm ? template.VmRuntimeMode : null,
                topologyAsset.Kind == TeamLabAssetKind.Vm ? template.VmNetworkMode : null);
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
                        created.RuntimeResourceId, TeamLabNodeBootstrapResult.Completed());
                case TeamLabDeploymentNodeKind.GuestReady:
                    if (string.IsNullOrWhiteSpace(asset.RuntimeResourceId))
                        return NodeExecution.Failed(node, asset, request,
                            "Runtime asset identity is missing before guest readiness.");
                    var ready = await executor.WaitForAssetReadyAsync(
                        shard.WorkerNodeId, asset.RuntimeResourceId, request, cancellationToken);
                    return new NodeExecution(
                        node, asset, request, ready.Success, ready.Message,
                        asset.RuntimeResourceId, TeamLabNodeBootstrapResult.Completed());
                case TeamLabDeploymentNodeKind.Bootstrap:
                    if (string.IsNullOrWhiteSpace(asset.RuntimeResourceId))
                        return NodeExecution.Failed(node, asset, request,
                            "Runtime asset identity is missing before bootstrap.");
                    var bootstrap = await executor.ApplyBootstrapAsync(
                        shard.WorkerNodeId, asset.RuntimeResourceId, request, cancellationToken);
                    return new NodeExecution(
                        node, asset, request, bootstrap.Success, bootstrap.Message,
                        asset.RuntimeResourceId, bootstrap);
                case TeamLabDeploymentNodeKind.Health:
                    if (string.IsNullOrWhiteSpace(asset.RuntimeResourceId))
                        return NodeExecution.Failed(node, asset, request,
                            "Runtime asset identity is missing before health probing.");
                    var health = await executor.ProbeAssetHealthAsync(
                        shard.WorkerNodeId, asset.RuntimeResourceId, request, cancellationToken);
                    return new NodeExecution(
                        node, asset, request, health.Success, health.Message,
                        asset.RuntimeResourceId, health);
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
            case TeamLabDeploymentNodeKind.Bootstrap:
                result.Asset.ExecutionStage = TeamLabAssetExecutionStage.BootstrapCompleted;
                result.Asset.Status = TeamLabRuntimeStatus.Deploying;
                bootstrapOrchestrator.RecordSuccess(
                    runtime, result.Asset, result.Request, result.BootstrapResult);
                MarkDependenciesSatisfied(
                    runtime, result.Asset.TopologyKey, TeamLabDependencyCondition.BootstrapCompleted);
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
            TeamLabDeploymentNodeKind.Bootstrap =>
                (TeamLabDeploymentStage.BootstrapInjecting, "Injecting and executing bootstrap profiles."),
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
            TeamLabDeploymentNodeKind.Bootstrap => (
                OperationalEventCodes.TeamLab.BootstrapSucceeded,
                OperationalEventCodes.TeamLab.BootstrapFailed,
                OperationalErrorCategory.AgentProtocol,
                OperationalErrorCodes.BootstrapOperationFailed),
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
        if (success && result.Node.Kind == TeamLabDeploymentNodeKind.Bootstrap &&
            result.BootstrapResult.RebootCount > 0)
            eventRecorder.Record(
                runtime,
                "bootstrap",
                TeamLabEventLevel.Info,
                OperationalEventCodes.TeamLab.BootstrapRebooted,
                OperationalEventOutcome.Observed,
                "Bootstrap completed required guest reboot cycles.",
                workerNodeId: shard.WorkerNodeId,
                detail: new Dictionary<string, object?>
                {
                    ["generation"] = runtime.Generation,
                    ["stage"] = "bootstrap",
                    ["assetKind"] = result.Asset.Kind.ToString(),
                    ["rebootCount"] = result.BootstrapResult.RebootCount
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
        TeamLabNodeBootstrapResult BootstrapResult)
    {
        public static NodeExecution Failed(
            TeamLabDeploymentNode node,
            TeamLabRuntimeAsset asset,
            TeamLabNodeAssetCreateRequest request,
            string message) => new(
            node, asset, request, false, message, asset.RuntimeResourceId,
            TeamLabNodeBootstrapResult.Failed(message));
    }
    private sealed record RuntimeInterfaceIntent(
        string Key,
        string NetworkKey,
        string IpAddress,
        int PrefixLength,
        string MacAddress,
        bool Primary);
}
