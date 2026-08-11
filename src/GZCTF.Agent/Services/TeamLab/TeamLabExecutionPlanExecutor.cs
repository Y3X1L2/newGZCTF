using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Observation;
using GZCTF.Agent.Services.Vm;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabExecutionPlanExecutor(
    TeamLabOvnNetworkProvider ovn,
    TeamLabOvsAttachmentProvider ovs,
    LinuxNetworkAttachmentService linuxNetwork,
    DockerService docker,
    LibvirtTeamLabProvider libvirt,
    ObservationPointRegistry observations,
    TeamLabExecutionEventJournal journal,
    IHttpClientFactory httpClients,
    IOptions<AgentConfig> agentOptions,
    ILogger<TeamLabExecutionPlanExecutor> logger)
{
    static readonly TimeSpan HealthProbeTimeout = TimeSpan.FromSeconds(10);
    readonly AgentConfig agent = agentOptions.Value;
    readonly KeyedSemaphoreRegistry<(int RuntimeId, int Generation, string ShardKey)> executionLocks = new();

    public async Task<TeamLabExecutionPlanApplyResponse> ApplyAsync(
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        if (!plan.IsValid(out var validationError))
            return Failure(plan, "validation", validationError!);
        using var executionLock = await executionLocks.AcquireAsync(
            (plan.RuntimeId, plan.Generation, plan.ShardKey), cancellationToken);
        if (journal.TryGetIdentity(plan, out var existingDigest) &&
            !string.Equals(existingDigest, plan.PlanDigest, StringComparison.OrdinalIgnoreCase))
            return Failure(plan, "validation", "A different execution plan is already active for this runtime generation and shard.");
        if (journal.TryGet(plan, out var existing))
        {
            var inventory = await ReadInventoryAsync(plan, cancellationToken);
            if (plan.Assets.All(asset => inventory.Any(item =>
                    item.AssetKey == asset.AssetKey &&
                    item.Generation == plan.Generation &&
                    item.State.Equals("running", StringComparison.OrdinalIgnoreCase))))
                return existing with { AlreadyApplied = true, Inventory = inventory };
            journal.Remove(plan);
        }
        return await ApplyCoreAsync(plan, cancellationToken);
    }

    async Task<TeamLabExecutionPlanApplyResponse> ApplyCoreAsync(
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        var network = await ovn.ApplyAsync(plan, cancellationToken);
        if (!network.Success)
        {
            logger.LogWarning("TeamLab network apply failed for runtime {RuntimeId}, generation {Generation}: {Message}",
                plan.RuntimeId, plan.Generation, network.Message);
            return Failure(plan, network.Stage, network.Message);
        }
        try
        {
            await observations.ApplyExecutionPlanAsync(plan, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception,
                "TeamLab observation registration failed for runtime {RuntimeId}, generation {Generation}",
                plan.RuntimeId, plan.Generation);
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await CleanupCoreAsync(plan, cleanupTimeout.Token);
            return Failure(plan, "observation", exception.Message);
        }

        var events = new ConcurrentQueue<TeamLabExecutionEventV2>();
        events.Enqueue(Event(plan, null, "network", "succeeded", null, network.Message));
        var limit = Math.Max(1, agent.ExecutionLimits.TeamLabExecutionOperations ?? 1);
        try
        {
            await Parallel.ForEachAsync(plan.Assets,
                new ParallelOptions { MaxDegreeOfParallelism = limit, CancellationToken = cancellationToken },
                async (asset, token) =>
                {
                    try
                    {
                        if (string.Equals(asset.Kind, "docker", StringComparison.OrdinalIgnoreCase))
                            await ApplyDockerAsync(plan, asset, events, token);
                        else if (string.Equals(asset.Kind, "vm", StringComparison.OrdinalIgnoreCase))
                            await ApplyVmAsync(plan, asset, events, token);
                        else
                            events.Enqueue(Event(plan, asset.AssetKey, "validation", "failed", "asset_kind_unsupported", asset.Kind));
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        logger.LogWarning(exception,
                            "TeamLab execution asset failed for runtime {RuntimeId}, generation {Generation}, asset {AssetKey}",
                            plan.RuntimeId, plan.Generation, asset.AssetKey);
                        events.Enqueue(Event(plan, asset.AssetKey, "compute", "failed", "asset_execution_failed", exception.Message));
                    }
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await CleanupCoreAsync(plan, cleanupTimeout.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception,
                    "TeamLab execution cancellation cleanup failed for runtime {RuntimeId}, generation {Generation}",
                    plan.RuntimeId, plan.Generation);
            }
            throw;
        }

        IReadOnlyList<TeamLabExecutionInventoryFactV2> inventory;
        try
        {
            inventory = await ReadInventoryAsync(plan, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "TeamLab execution inventory read failed after apply for runtime {RuntimeId}, generation {Generation}",
                plan.RuntimeId, plan.Generation);
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var compensation = await CleanupCoreAsync(plan, cleanupTimeout.Token);
            events.Enqueue(Event(plan, null, "compute", "failed", "inventory_read_failed",
                "Agent inventory could not be read after execution-plan apply."));
            foreach (var cleanupEvent in compensation.Events)
                events.Enqueue(cleanupEvent);
            return new TeamLabExecutionPlanApplyResponse(
                false, false, plan.PlanDigest, events.ToArray(), compensation.Inventory,
                "compute", "inventory_read_failed", "Execution plan inventory verification failed.");
        }
        foreach (var asset in plan.Assets)
        {
            var actual = inventory.FirstOrDefault(item => item.AssetKey == asset.AssetKey);
            if (actual is null)
                events.Enqueue(Event(plan, asset.AssetKey, "compute", "failed", "inventory_missing",
                    "The requested asset is not present in the Agent inventory."));
            else if (!actual.State.Equals("running", StringComparison.OrdinalIgnoreCase))
                events.Enqueue(Event(plan, asset.AssetKey, "compute", "failed", "resource_not_running",
                    "The requested asset is present but is not running."));
        }
        var eventArray = events.ToArray();
        var success = eventArray.All(item => item.Outcome is "succeeded" or "already_applied");
        if (!success)
        {
            try
            {
                // The caller may cancel after the Agent has created host resources.
                // Cleanup is a separate bounded operation and must not inherit that
                // cancellation, otherwise an interrupted apply leaks the shard.
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var compensation = await CleanupCoreAsync(plan, cleanupTimeout.Token);
                foreach (var cleanupEvent in compensation.Events)
                    events.Enqueue(cleanupEvent);
                inventory = compensation.Inventory;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception,
                    "TeamLab execution compensation failed for runtime {RuntimeId}, generation {Generation}",
                    plan.RuntimeId, plan.Generation);
                events.Enqueue(Event(plan, null, "cleanup", "failed", "compensation_failed", "Execution plan compensation failed."));
            }
            eventArray = events.ToArray();
        }
        var response = new TeamLabExecutionPlanApplyResponse(
            success,
            false,
            plan.PlanDigest,
            eventArray,
            inventory,
            success ? null : "compute",
            success ? null : "asset_execution_failed",
            success ? "Execution plan applied." : "One or more assets failed to start.");
        if (success) journal.Save(plan, response);
        else journal.Remove(plan);
        return response;
    }

    public async Task<TeamLabExecutionPlanCleanupResponse> CleanupAsync(
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        if (!plan.IsValid(out var validationError))
            return CleanupFailure(plan, "validation", validationError!);

        using var executionLock = await executionLocks.AcquireAsync(
            (plan.RuntimeId, plan.Generation, plan.ShardKey), cancellationToken);
        journal.Remove(plan);
        return await CleanupCoreAsync(plan, cancellationToken);
    }

    async Task<TeamLabExecutionPlanCleanupResponse> CleanupCoreAsync(
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        var events = new ConcurrentQueue<TeamLabExecutionEventV2>();
        var beforeCleanup = await ReadInventoryAsync(plan, cancellationToken);
        await Parallel.ForEachAsync(plan.Assets,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, agent.ExecutionLimits.TeamLabExecutionOperations ?? 1),
                CancellationToken = cancellationToken
            },
            async (asset, token) => await CleanupAssetAsync(plan, asset, beforeCleanup, events, token));

        foreach (var asset in plan.Assets.Where(asset =>
                     asset.Kind.Equals("docker", StringComparison.OrdinalIgnoreCase)))
            foreach (var attachment in asset.NetworkAttachments)
            {
                var local = await linuxNetwork.RemoveContainerAttachmentAsync(
                    plan, asset.AssetKey, attachment.NetworkKey, cancellationToken);
                if (!local.Success)
                    events.Enqueue(Event(plan, asset.AssetKey, "cleanup", "failed", "veth_cleanup_failed", local.Message));
                var ovsResult = await ovs.RemoveAsync(plan, LinuxNetworkAttachmentService.HostInterfaceName(plan, asset.AssetKey, attachment.NetworkKey),
                    attachment.NetworkKey, cancellationToken);
                if (!ovsResult.Success)
                    events.Enqueue(Event(plan, asset.AssetKey, "cleanup", "failed", "ovs_cleanup_failed", ovsResult.Message));
            }

        var network = await ovn.RemoveAsync(plan, cancellationToken);
        events.Enqueue(Event(plan, null, "cleanup", network.Success ? "succeeded" : "failed",
            network.Success ? null : "network_cleanup_failed", network.Message));
        try
        {
            await observations.RemoveAsync(plan.RuntimeId, plan.Generation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            events.Enqueue(Event(plan, null, "cleanup", "failed", "observation_cleanup_failed", exception.Message));
        }
        var inventory = await ReadInventoryAsync(plan, cancellationToken);
        foreach (var resource in inventory)
            events.Enqueue(Event(plan, resource.AssetKey, "cleanup", "failed", "resource_remains",
                $"Resource remains in state {resource.State}."));
        var eventArray = events.ToArray();
        var success = inventory.Count == 0 && eventArray.All(item => item.Outcome is "succeeded" or "already_applied");
        return new TeamLabExecutionPlanCleanupResponse(
            success, plan.PlanDigest, eventArray, inventory,
            success ? null : "cleanup", success ? null : "resource_cleanup_failed",
            success ? "Execution plan cleaned." : "One or more execution resources remain.");
    }

    async Task CleanupAssetAsync(
        TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset,
        IReadOnlyList<TeamLabExecutionInventoryFactV2> beforeCleanup,
        ConcurrentQueue<TeamLabExecutionEventV2> events,
        CancellationToken token)
    {
        try
        {
            if (asset.Kind.Equals("docker", StringComparison.OrdinalIgnoreCase))
            {
                var item = beforeCleanup.FirstOrDefault(item => item.AssetKey == asset.AssetKey);
                if (item is not null)
                    await docker.DestroyContainerAsync(item.ResourceId, token, plan.Generation, plan.PlanDigest);
            }
            else if (asset.Kind.Equals("vm", StringComparison.OrdinalIgnoreCase))
            {
                var result = await libvirt.DestroyAsync(plan, asset, token);
                if (!result.Success)
                {
                    events.Enqueue(Event(plan, asset.AssetKey, "cleanup", "failed", "vm_cleanup_failed", result.State));
                    return;
                }
            }
            else
            {
                events.Enqueue(Event(plan, asset.AssetKey, "cleanup", "failed", "asset_kind_unsupported", asset.Kind));
                return;
            }
            events.Enqueue(Event(plan, asset.AssetKey, "cleanup", "succeeded", null, "Resource cleaned."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception,
                "TeamLab cleanup asset failed for runtime {RuntimeId}, generation {Generation}, asset {AssetKey}",
                plan.RuntimeId, plan.Generation, asset.AssetKey);
            events.Enqueue(Event(plan, asset.AssetKey, "cleanup", "failed", "resource_cleanup_failed", exception.Message));
        }
    }

    async Task<IReadOnlyList<TeamLabExecutionInventoryFactV2>> ReadInventoryAsync(
        TeamLabExecutionPlanV2 plan,
        CancellationToken cancellationToken)
    {
        var inventory = (await docker.GetManagedRuntimeInventoryAsync(cancellationToken))
            .Where(item => item.RuntimeId == plan.RuntimeId && item.Generation == plan.Generation &&
                           string.Equals(item.ShardKey, plan.ShardKey, StringComparison.Ordinal))
            .Select(item => new TeamLabExecutionInventoryFactV2(
                "docker", item.AssetKey ?? string.Empty, item.NativeId, item.State, item.Generation))
            .Where(item => !string.IsNullOrWhiteSpace(item.AssetKey))
            .ToList();
        if (plan.Assets.Any(item => item.Kind.Equals("vm", StringComparison.OrdinalIgnoreCase)))
            inventory.AddRange(libvirt.GetInventory(plan));
        return inventory;
    }

    async Task ApplyDockerAsync(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset,
        ConcurrentQueue<TeamLabExecutionEventV2> events, CancellationToken token)
    {
        var request = new CreateContainerRequest
        {
            RuntimeId = plan.RuntimeId,
            Generation = plan.Generation,
            Image = ImmutableDockerReference(asset),
            TeamId = plan.RuntimePublicId.ToString("N"),
            ChallengeId = StableChallengeId(asset.AssetKey),
            AssetKey = asset.AssetKey,
            UserId = Guid.Empty,
            ExposedPort = asset.HealthChecks.FirstOrDefault()?.Port ?? 0,
            PublishPort = false,
            UseHostNetworkNone = true,
            NetworkMode = "None",
            EnableTeamLabNetworkGate = false,
            StartImmediately = true,
            MemoryLimit = Math.Max(64, asset.MemoryMiB),
            CPUCount = Math.Max(1, asset.Cpu),
            TeamLabPlanDigest = plan.PlanDigest,
            TeamLabShardKey = plan.ShardKey
        };
        AgentContainerResponse? container = null;
        var attached = new List<TeamLabAssetNetworkAttachmentV2>();
        var completed = false;
        try
        {
            container = await docker.CreateContainerAsync(request, token);
            if (container is null)
            {
                events.Enqueue(Event(plan, asset.AssetKey, "compute", "failed", "docker_create_failed", "Docker container creation failed."));
                return;
            }
            foreach (var attachment in asset.NetworkAttachments)
            {
                var pid = await docker.GetContainerPidAsync(container.ContainerId, token);
                var local = await linuxNetwork.AttachContainerAsync(plan, pid, asset, attachment, token);
                if (!local.Success)
                {
                    events.Enqueue(Event(plan, asset.AssetKey, "network", "failed", "container_attach_failed", local.Message));
                    return;
                }
                attached.Add(attachment);
                var ovsResult = await ovs.AttachAsync(plan, local.Message, attachment.NetworkKey, attachment.PortKey, token);
                if (!ovsResult.Success)
                {
                    events.Enqueue(Event(plan, asset.AssetKey, "network", "failed", "ovs_attach_failed", ovsResult.Message));
                    return;
                }
            }
            if (!await RunHealthChecksAsync(plan, asset, events, token))
                return;
            events.Enqueue(Event(plan, asset.AssetKey, "compute", "succeeded", null, container.ContainerId));
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                if (container is not null)
                    await docker.DestroyContainerAsync(container.ContainerId, CancellationToken.None, plan.Generation,
                        plan.PlanDigest);
                foreach (var attachment in attached)
                {
                    await linuxNetwork.RemoveContainerAttachmentAsync(plan, asset.AssetKey, attachment.NetworkKey, CancellationToken.None);
                    await ovs.RemoveAsync(plan, LinuxNetworkAttachmentService.HostInterfaceName(plan, asset.AssetKey, attachment.NetworkKey),
                        attachment.NetworkKey, CancellationToken.None);
                }
            }
        }
    }

    async Task ApplyVmAsync(TeamLabExecutionPlanV2 plan, TeamLabAssetExecutionSpecV2 asset,
        ConcurrentQueue<TeamLabExecutionEventV2> events, CancellationToken token)
    {
        var result = await libvirt.EnsureRunningAsync(plan, asset, token);
        if (!result.Success)
        {
            events.Enqueue(Event(plan, asset.AssetKey, "compute", "failed", "vm_start_failed", result.State));
            return;
        }
        if (!await RunHealthChecksAsync(plan, asset, events, token))
            return;
        events.Enqueue(Event(plan, asset.AssetKey, "compute", "succeeded", null, result.ResourceId));
    }

    async Task<bool> RunHealthChecksAsync(
        TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset,
        ConcurrentQueue<TeamLabExecutionEventV2> events,
        CancellationToken cancellationToken)
    {
        foreach (var check in asset.HealthChecks)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(HealthProbeTimeout);
            try
            {
                if (check.Port is < 1 or > 65535 || !IPAddress.TryParse(check.Host, out _))
                    throw new InvalidOperationException("Health check target is invalid.");

                if (check.Protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase))
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(check.Host, check.Port, deadline.Token);
                }
                else if (check.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase))
                {
                    var address = new UriBuilder(Uri.UriSchemeHttp, check.Host, check.Port,
                        string.IsNullOrWhiteSpace(check.Path) ? "/" : check.Path).Uri;
                    using var response = await httpClients.CreateClient().GetAsync(address, deadline.Token);
                    if ((int)response.StatusCode is < 200 or >= 400)
                        throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported health check protocol '{check.Protocol}'.");
                }
                events.Enqueue(Event(plan, asset.AssetKey, "service", "succeeded", null,
                    $"{check.Protocol.ToUpperInvariant()} health check passed."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is SocketException or HttpRequestException or
                                                InvalidOperationException or OperationCanceledException)
            {
                events.Enqueue(Event(plan, asset.AssetKey, "service", "failed", "health_check_failed",
                    $"{check.Protocol.ToUpperInvariant()} health check failed."));
                return false;
            }
        }
        return true;
    }

    static TeamLabExecutionEventV2 Event(TeamLabExecutionPlanV2 plan, string? assetKey, string stage,
        string outcome, string? errorCode, string detail)
    {
        if (!TeamLabExecutionProtocolV2.IsStage(stage) || !TeamLabExecutionProtocolV2.IsOutcome(outcome))
            throw new InvalidOperationException("TeamLab execution emitted an unsupported event protocol value.");
        return new TeamLabExecutionEventV2(
            plan.RuntimeId, plan.RuntimePublicId, plan.Generation, plan.ShardKey, assetKey, stage, outcome,
            outcome == "succeeded" ? null : stage, errorCode, DateTimeOffset.UtcNow,
            new Dictionary<string, string> { ["summary"] = detail });
    }

    static TeamLabExecutionPlanApplyResponse Failure(TeamLabExecutionPlanV2 plan, string stage, string message) =>
        new(false, false, plan.PlanDigest, [Event(plan, null, stage, "failed", $"{stage}_failed", message)], [], stage,
            $"{stage}_failed", message);

    static TeamLabExecutionPlanCleanupResponse CleanupFailure(
        TeamLabExecutionPlanV2 plan, string stage, string message) =>
        new(false, plan.PlanDigest,
            [Event(plan, null, stage, "failed", $"{stage}_failed", message)], [], stage,
            $"{stage}_failed", message);

    static int StableChallengeId(string key) =>
        BitConverter.ToInt32(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)), 0) & int.MaxValue;

    static string ImmutableDockerReference(TeamLabAssetExecutionSpecV2 asset)
    {
        if (string.IsNullOrWhiteSpace(asset.ImageReference)) return asset.ImageDigest;
        var reference = asset.ImageReference.Trim();
        var digestMarker = reference.IndexOf("@sha256:", StringComparison.OrdinalIgnoreCase);
        if (digestMarker >= 0)
            return reference[..digestMarker] + "@" + asset.ImageDigest;

        var lastSlash = reference.LastIndexOf('/');
        var lastColon = reference.LastIndexOf(':');
        var repository = lastColon > lastSlash ? reference[..lastColon] : reference;
        return repository + "@" + asset.ImageDigest;
    }
}
