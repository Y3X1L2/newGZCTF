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
        node.AgentUpdateWasSchedulable = priorSchedulable;
        node.AgentUpdateExpectedSha256 = expectedSha;
        node.AgentUpdateStartedAt = DateTimeOffset.UtcNow;
        node.AgentUpdateCompletedAt = null;
        node.AgentUpdateLastError = null;
        node.AgentUpdateState = AgentUpdateState.Cordoned;
        node.IsSchedulable = false;
        await RecordStageAsync(node, correlationId, "cordoned",
            "Worker node was cordoned before Agent synchronization.", cancellationToken);

        try
        {
            node.AgentUpdateState = AgentUpdateState.Syncing;
            await RecordStageAsync(node, correlationId, "binary-transfer",
                "Agent and endpoint sensor artifacts are synchronizing from the main server.",
                cancellationToken);
            var result = await agent.SyncAgentAsync(node.Id,
                new AgentSyncRequest(
                    DownloadUrl: $"{serverUrl.TrimEnd('/')}/api/agent/download",
                    ExpectedSha256: expectedSha,
                    LinuxSensorDownloadUrl: $"{serverUrl.TrimEnd('/')}/api/agent/endpoint-sensor/linux-x64/download",
                    LinuxSensorSha256: NodeDeployService.ComputeBundledArtifactSha256(
                        "agent", "endpoint-sensor", "linux-x64", "gzctf-endpoint-sensor"),
                    WindowsSensorDownloadUrl: $"{serverUrl.TrimEnd('/')}/api/agent/endpoint-sensor/win-x64/download",
                    WindowsSensorSha256: NodeDeployService.ComputeBundledArtifactSha256(
                        "agent", "endpoint-sensor", "win-x64", "gzctf-endpoint-sensor.exe"),
                    VmControlPlane: new AgentVmControlPlaneSyncConfig(
                        node.TeamLabNetworkEnabled && priorCapabilities.HasFlag(NodeCapability.Kvm)),
                    TeamLabDataPlane: TeamLabDataPlaneSyncConfiguration.Create(node, controlPlaneNode)),
                cancellationToken);
            if (!result.Success)
                return await FailAsync(node, correlationId, result.Message, cancellationToken);

            node.AgentUpdateState = AgentUpdateState.AwaitingHeartbeat;
            await RecordStageAsync(node, correlationId, "awaiting-heartbeat",
                "Agent restart completed; waiting for the target capability manifest.", cancellationToken);
            var deadline = DateTimeOffset.UtcNow + HeartbeatDeadline;
            while (DateTimeOffset.UtcNow < deadline)
            {
                await context.Entry(node).ReloadAsync(cancellationToken);
                if (HasExpectedManifest(node, expectedSha, priorCapabilities))
                {
                    node.AgentUpdateState = AgentUpdateState.VerifyingFabric;
                    await context.SaveChangesAsync(cancellationToken);
                    if (FabricReady(node))
                    {
                        node.AgentUpdateState = AgentUpdateState.Stable;
                        node.IsSchedulable = priorSchedulable;
                        node.AgentUpdateCompletedAt = DateTimeOffset.UtcNow;
                        node.AgentUpdateLastError = null;
                        await RecordStageAsync(node, correlationId, "completed",
                            "Agent manifest and TeamLab Fabric health were confirmed.", cancellationToken,
                            OperationalEventCodes.Agent.SyncSucceeded,
                            OperationalEventOutcome.Succeeded);
                        return new AgentFleetUpdateResult(
                            true, "Agent synchronized and node scheduling state restored.",
                            node.AgentUpdateState, node.IsSchedulable);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            return await FailAsync(
                node,
                correlationId,
                "The target Agent manifest or TeamLab Fabric health was not observed before the deadline.",
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Agent fleet update failed on node {NodeId}", node.Id);
            return await FailAsync(node, correlationId, exception.Message, cancellationToken);
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
            if (!string.IsNullOrWhiteSpace(node.AgentUpdateExpectedSha256) &&
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

    private async Task<AgentFleetUpdateResult> FailAsync(
        WorkerNode node,
        Guid correlationId,
        string message,
        CancellationToken cancellationToken)
    {
        node.AgentUpdateState = AgentUpdateState.Failed;
        node.IsSchedulable = false;
        node.AgentUpdateCompletedAt = DateTimeOffset.UtcNow;
        node.AgentUpdateLastError = message.Length <= 1024 ? message : message[..1024];
        await RecordStageAsync(
            node,
            correlationId,
            "failed",
            "Agent synchronization failed and the node remains cordoned.",
            cancellationToken,
            OperationalEventCodes.Agent.SyncFailed,
            OperationalEventOutcome.Failed,
            OperationalEventSeverity.Error);
        return new AgentFleetUpdateResult(false, message, node.AgentUpdateState, node.IsSchedulable);
    }

    private async Task RecordStageAsync(
        WorkerNode node,
        Guid correlationId,
        string stage,
        string message,
        CancellationToken cancellationToken,
        string eventCode = OperationalEventCodes.Agent.SyncStarted,
        OperationalEventOutcome outcome = OperationalEventOutcome.Started,
        OperationalEventSeverity severity = OperationalEventSeverity.Information)
    {
        events.Append(NodeOperationalEvents.Create(
            node,
            eventCode,
            outcome,
            message,
            severity,
            correlationId: correlationId,
            detail: new Dictionary<string, object?>
            {
                ["operation"] = "agent.sync",
                ["stage"] = stage,
                ["expectedSha256"] = node.AgentUpdateExpectedSha256
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
