using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public sealed record AgentFleetUpdateResult(
    bool Success,
    string Message,
    AgentUpdateState State,
    bool Schedulable);

public sealed class AgentFleetUpdateCoordinator(
    AppDbContext context,
    AgentClient agent,
    IOperationalEventWriter events,
    ILogger<AgentFleetUpdateCoordinator> logger)
{
    private static readonly TimeSpan HeartbeatDeadline = TimeSpan.FromSeconds(90);

    public async Task<AgentFleetUpdateResult> SyncAsync(
        Guid nodeId,
        string serverUrl,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var node = await context.WorkerNodes.SingleOrDefaultAsync(item => item.Id == nodeId, cancellationToken)
                   ?? throw new InvalidOperationException($"Worker node {nodeId} was not found.");
        if (node.AgentUpdateState is AgentUpdateState.Cordoned or AgentUpdateState.Syncing or
            AgentUpdateState.AwaitingHeartbeat or AgentUpdateState.VerifyingFabric)
            return new AgentFleetUpdateResult(
                false, "An Agent update is already active for this node.", node.AgentUpdateState,
                node.IsSchedulable);

        var expectedSha = NodeDeployService.ComputeAgentBinarySha256();
        if (string.IsNullOrWhiteSpace(expectedSha))
            throw new InvalidOperationException("The bundled Agent binary is unavailable.");
        var priorCapabilities = node.Capabilities;
        var controlPlaneNode = await context.WorkerNodes.AsNoTracking()
            .SingleOrDefaultAsync(item => item.IsLocal && item.TeamLabNetworkEnabled, cancellationToken);
        var priorSchedulable = node.AgentUpdateState == AgentUpdateState.Failed
            ? node.AgentUpdateWasSchedulable
            : node.IsSchedulable;
        await BeginUpdateAsync(node, priorSchedulable, expectedSha, cancellationToken);
        await RecordStageAsync(node, correlationId, "cordoned",
            "Worker node was cordoned before Agent synchronization.", cancellationToken);

        try
        {
            await TransitionAsync(node, AgentUpdateState.Syncing, cancellationToken);
            await RecordStageAsync(node, correlationId, "binary-transfer",
                "Agent binary is synchronizing from the main server.",
                cancellationToken);
            var result = await agent.SyncAgentAsync(node.Id,
            CreateSyncRequest(serverUrl, expectedSha, node, controlPlaneNode,
                    includeManagedArtifacts: false, includeNodeConfiguration: false), cancellationToken);
            if (!result.Success)
                return await FailAsync(node, correlationId, result.Message, cancellationToken,
                    CreateSyncFailure(result.Message, node));

            // A database SHA is a cached report, not proof of the process currently serving
            // the Agent API. Always observe the expected heartbeat before sending host config.
            await TransitionAsync(node, AgentUpdateState.AwaitingHeartbeat, cancellationToken);
            await RecordStageAsync(node, correlationId, "awaiting-heartbeat",
                "Agent restart completed; waiting for the target capability manifest.", cancellationToken);
            var deadline = DateTimeOffset.UtcNow + HeartbeatDeadline;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await context.Entry(node).ReloadAsync(cancellationToken);
                if (HasExpectedManifest(node, expectedSha, priorCapabilities)) break;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            if (!HasExpectedManifest(node, expectedSha, priorCapabilities))
                return await FailAsync(node, correlationId,
                    "The target Agent manifest was not observed before the update deadline.", cancellationToken,
                    CreateSyncFailure(
                        "The target Agent manifest was not observed before the update deadline.", node));

            await TransitionAsync(node, AgentUpdateState.Syncing, cancellationToken);
            await RecordStageAsync(node, correlationId, "node-configuration",
                "The updated Agent is applying VM control-plane and TeamLab data-plane configuration.",
                cancellationToken);
            result = await agent.SyncAgentAsync(node.Id,
            CreateSyncRequest(serverUrl, expectedSha, node, controlPlaneNode,
                    includeManagedArtifacts: true, includeNodeConfiguration: true), cancellationToken);
            if (!result.Success)
                return await FailAsync(node, correlationId, result.Message, cancellationToken,
                    CreateSyncFailure(result.Message, node));

            await context.Entry(node).ReloadAsync(cancellationToken);
            await TransitionAsync(node, AgentUpdateState.VerifyingFabric, cancellationToken);
            if (!FabricReady(node))
                return await FailAsync(node, correlationId,
                    "TeamLab Fabric health was not confirmed after Agent synchronization.", cancellationToken,
                    CreateSyncFailure(
                        "TeamLab Fabric health was not confirmed after Agent synchronization.", node));

            await CompleteAsync(node, priorSchedulable, cancellationToken);
            await RecordStageAsync(node, correlationId, "completed",
                "Agent binary, configuration and TeamLab Fabric health were confirmed.", cancellationToken,
                OperationalEventCodes.Agent.SyncSucceeded,
                OperationalEventOutcome.Succeeded);
            return new AgentFleetUpdateResult(
                true, "Agent synchronized and node scheduling state restored.",
                node.AgentUpdateState, node.IsSchedulable);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Agent fleet update failed on node {NodeId}", node.Id);
            var error = exception is IOperationalFailureException failure
                ? failure.Error
                : OperationalErrorClassifier.FromException(exception, "maintenance.sync", node.Id);
            return await FailAsync(node, correlationId, error.Message, cancellationToken, error);
        }
    }

    public async Task RecoverPendingAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = await context.WorkerNodes.Where(item =>
                item.AgentUpdateState == AgentUpdateState.Cordoned ||
                item.AgentUpdateState == AgentUpdateState.Syncing ||
                item.AgentUpdateState == AgentUpdateState.AwaitingHeartbeat ||
                item.AgentUpdateState == AgentUpdateState.VerifyingFabric)
            .ToArrayAsync(cancellationToken);
        foreach (var node in pending)
        {
            // Only this state is entered after the node configuration request has succeeded.
            // Earlier states may have transferred the binary but must never be scheduled before
            // the configuration phase has run.
            if (node.AgentUpdateState == AgentUpdateState.VerifyingFabric &&
                !string.IsNullOrWhiteSpace(node.AgentUpdateExpectedSha256) &&
                HasExpectedManifest(node, node.AgentUpdateExpectedSha256, node.Capabilities) &&
                FabricReady(node))
            {
                node.AgentUpdateState = AgentUpdateState.Stable;
                node.IsSchedulable = node.AgentUpdateWasSchedulable;
                node.AgentUpdateCompletedAt = now;
                node.AgentUpdateLastError = null;
                continue;
            }
            if (node.AgentUpdateStartedAt < now - TimeSpan.FromMinutes(10))
            {
                node.AgentUpdateState = AgentUpdateState.Failed;
                node.IsSchedulable = false;
                node.AgentUpdateLastError = "Agent update recovery timed out before manifest and Fabric convergence.";
                node.AgentUpdateCompletedAt = now;
            }
        }
        if (context.ChangeTracker.HasChanges()) await context.SaveChangesAsync(cancellationToken);
    }

    internal static bool HasExpectedManifest(
        WorkerNode node,
        string expectedSha,
        NodeCapability requiredCapabilities)
    {
        if (!string.Equals(
                NormalizeSha(node.AgentBinarySha256),
                NormalizeSha(expectedSha),
                StringComparison.OrdinalIgnoreCase))
            return false;
        var required = new List<string>
        {
            AgentFeatureIds.SelfUpdate,
            AgentFeatureIds.RuntimeInventory,
            AgentFeatureIds.RuntimeSignals
        };
        if ((requiredCapabilities & NodeCapability.Docker) != 0)
            required.Add(AgentFeatureIds.Docker);
        if ((requiredCapabilities & NodeCapability.Kvm) != 0)
            required.AddRange([
                AgentFeatureIds.Kvm,
                AgentFeatureIds.VmDownload,
                AgentFeatureIds.VmReadinessSignals
            ]);
        if (node.TeamLabNetworkEnabled)
        {
            required.AddRange([
                AgentFeatureIds.TeamLabInfrastructure,
                AgentFeatureIds.TeamLabFabricLeasedLinks,
                AgentFeatureIds.TeamLabObservation
            ]);
            if ((requiredCapabilities & NodeCapability.Kvm) != 0)
                required.AddRange([
                    AgentFeatureIds.VmGuestManagement,
                    AgentFeatureIds.VmConfigDriveV2,
                    AgentFeatureIds.VmPreparedImage
                ]);
            if ((requiredCapabilities & NodeCapability.Docker) != 0)
                required.Add(AgentFeatureIds.TeamLabContainerNetworkFinalize);
        }
        return AgentCapabilityEvaluator.Supports(node, required.Distinct(StringComparer.Ordinal).ToArray());
    }

    internal static bool FabricReady(WorkerNode node) =>
        !node.TeamLabNetworkEnabled ||
        node.TeamLabTunnelStatus == TeamLabTunnelStatus.Healthy &&
        node.TeamLabFabricStatus == TeamLabFabricStatus.Healthy;

    private async Task BeginUpdateAsync(WorkerNode node, bool priorSchedulable, string expectedSha,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        if (context.Database.IsRelational())
        {
            await context.WorkerNodes.Where(item => item.Id == node.Id)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(item => item.AgentUpdateWasSchedulable, priorSchedulable)
                    .SetProperty(item => item.AgentUpdateExpectedSha256, expectedSha)
                    .SetProperty(item => item.AgentUpdateStartedAt, startedAt)
                    .SetProperty(item => item.AgentUpdateCompletedAt, (DateTimeOffset?)null)
                    .SetProperty(item => item.AgentUpdateLastError, (string?)null)
                    .SetProperty(item => item.AgentUpdateState, AgentUpdateState.Cordoned)
                    .SetProperty(item => item.IsSchedulable, false), cancellationToken);
        }
        else
        {
            node.AgentUpdateWasSchedulable = priorSchedulable;
            node.AgentUpdateExpectedSha256 = expectedSha;
            node.AgentUpdateStartedAt = startedAt;
            node.AgentUpdateCompletedAt = null;
            node.AgentUpdateLastError = null;
            node.AgentUpdateState = AgentUpdateState.Cordoned;
            node.IsSchedulable = false;
            await context.SaveChangesAsync(cancellationToken);
        }
        await context.Entry(node).ReloadAsync(cancellationToken);
    }

    private async Task TransitionAsync(WorkerNode node, AgentUpdateState state, CancellationToken cancellationToken)
    {
        if (context.Database.IsRelational())
        {
            await context.WorkerNodes.Where(item => item.Id == node.Id)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(item => item.AgentUpdateState, state), cancellationToken);
        }
        else
        {
            node.AgentUpdateState = state;
            await context.SaveChangesAsync(cancellationToken);
        }
        await context.Entry(node).ReloadAsync(cancellationToken);
    }

    private async Task CompleteAsync(WorkerNode node, bool schedulable, CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        if (context.Database.IsRelational())
        {
            await context.WorkerNodes.Where(item => item.Id == node.Id)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(item => item.AgentUpdateState, AgentUpdateState.Stable)
                    .SetProperty(item => item.IsSchedulable, schedulable)
                    .SetProperty(item => item.AgentUpdateCompletedAt, completedAt)
                    .SetProperty(item => item.AgentUpdateLastError, (string?)null), cancellationToken);
        }
        else
        {
            node.AgentUpdateState = AgentUpdateState.Stable;
            node.IsSchedulable = schedulable;
            node.AgentUpdateCompletedAt = completedAt;
            node.AgentUpdateLastError = null;
            await context.SaveChangesAsync(cancellationToken);
        }
        await context.Entry(node).ReloadAsync(cancellationToken);
    }

    private static AgentSyncRequest CreateSyncRequest(string serverUrl, string expectedSha, WorkerNode node,
        WorkerNode? controlPlaneNode, bool includeManagedArtifacts, bool includeNodeConfiguration) =>
        new(
            DownloadUrl: $"{serverUrl.TrimEnd('/')}/api/agent/download",
            ExpectedSha256: expectedSha,
            LinuxSensorDownloadUrl: includeManagedArtifacts
                ? $"{serverUrl.TrimEnd('/')}/api/agent/endpoint-sensor/linux-x64/download"
                : null,
            LinuxSensorSha256: includeManagedArtifacts
                ? NodeDeployService.ComputeBundledArtifactSha256(
                    "agent", "endpoint-sensor", "linux-x64", "gzctf-endpoint-sensor")
                : null,
            WindowsSensorDownloadUrl: includeManagedArtifacts
                ? $"{serverUrl.TrimEnd('/')}/api/agent/endpoint-sensor/win-x64/download"
                : null,
            WindowsSensorSha256: includeManagedArtifacts
                ? NodeDeployService.ComputeBundledArtifactSha256(
                    "agent", "endpoint-sensor", "win-x64", "gzctf-endpoint-sensor.exe")
                : null,
            VmControlPlane: includeNodeConfiguration
                ? new AgentVmControlPlaneSyncConfig(
                    node.TeamLabNetworkEnabled && node.Capabilities.HasFlag(NodeCapability.Kvm))
                : null,
            TeamLabDataPlane: includeNodeConfiguration
                ? TeamLabDataPlaneSyncConfiguration.Create(node, controlPlaneNode)
                : null);

    private async Task<AgentFleetUpdateResult> FailAsync(
        WorkerNode node,
        Guid correlationId,
        string message,
        CancellationToken cancellationToken,
        OperationalError error)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var boundedMessage = message.Length <= 1024 ? message : message[..1024];
        // Heartbeat writes WorkerNode concurrently. Persist the terminal sync state with a
        // targeted update so a stale tracked concurrency token cannot turn a known failure
        // into HTTP 500. The audit event is saved separately below.
        context.Entry(node).State = EntityState.Unchanged;
        if (context.Database.IsRelational())
        {
            await context.WorkerNodes
                .Where(item => item.Id == node.Id)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(item => item.AgentUpdateState, AgentUpdateState.Failed)
                    .SetProperty(item => item.IsSchedulable, false)
                    .SetProperty(item => item.AgentUpdateCompletedAt, completedAt)
                    .SetProperty(item => item.AgentUpdateLastError, boundedMessage), cancellationToken);
        }
        else
        {
            node.AgentUpdateState = AgentUpdateState.Failed;
            node.IsSchedulable = false;
            node.AgentUpdateCompletedAt = completedAt;
            node.AgentUpdateLastError = boundedMessage;
            await context.SaveChangesAsync(cancellationToken);
            context.Entry(node).State = EntityState.Unchanged;
        }
        node.AgentUpdateState = AgentUpdateState.Failed;
        node.IsSchedulable = false;
        node.AgentUpdateCompletedAt = completedAt;
        node.AgentUpdateLastError = boundedMessage;
        await RecordStageAsync(
            node,
            correlationId,
            "failed",
            "Agent synchronization failed and the node remains cordoned.",
            cancellationToken,
            OperationalEventCodes.Agent.SyncFailed,
            OperationalEventOutcome.Failed,
            OperationalEventSeverity.Error,
            error);
        return new AgentFleetUpdateResult(false, message, node.AgentUpdateState, node.IsSchedulable);
    }

    private static OperationalError CreateSyncFailure(string message, WorkerNode node) =>
        new(
            OperationalErrorCategory.AgentProtocol,
            OperationalErrorCodes.AgentSyncFailed,
            message,
            Retryable: true,
            WorkerNodeId: node.Id,
            Operation: "maintenance.sync");

    private async Task RecordStageAsync(
        WorkerNode node,
        Guid correlationId,
        string stage,
        string message,
        CancellationToken cancellationToken,
        string eventCode = OperationalEventCodes.Agent.SyncStarted,
        OperationalEventOutcome outcome = OperationalEventOutcome.Started,
        OperationalEventSeverity severity = OperationalEventSeverity.Information,
        OperationalError? error = null)
    {
        events.Append(NodeOperationalEvents.Create(
            node,
            eventCode,
            outcome,
            message,
            severity,
            error,
            correlationId: correlationId,
            detail: new Dictionary<string, object?>
            {
                ["operation"] = "agent.sync",
                ["stage"] = stage
            }));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeSha(string? value) =>
        (value ?? string.Empty).Trim().Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase);
}

public sealed class AgentFleetUpdateRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AgentFleetUpdateRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentFleetUpdateCoordinator>()
                    .RecoverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Agent fleet update recovery scan failed");
            }
        }
    }
}
