using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Runtime.Application;

public sealed record RuntimeReconciliationSummary(
    int MatchedCount,
    int MissingCount,
    int ConflictCount,
    int OrphanCount,
    int DeferredCount,
    int CorrectedCount,
    int ReplayedCount,
    int RecoveredTicketCount);

public sealed class RuntimeFactReconciliationService(
    AppDbContext context,
    AgentClient agentClient,
    FleetCapacityReservationService capacity,
    TeamLabRuntimeRecoveryPolicy teamLabRecovery,
    IDeploymentQueueWakeup wakeup,
    IOperationalEventWriter events,
    ILogger<RuntimeFactReconciliationService> logger)
{
    private static readonly TeamLabRuntimeStatus[] ActiveTeamLabRuntimeStatuses =
    [
        TeamLabRuntimeStatus.Probing,
        TeamLabRuntimeStatus.Running
    ];

    private static readonly TeamLabRuntimeStatus[] ExpectedTeamLabWorkloadStatuses =
    [
        TeamLabRuntimeStatus.Deploying,
        TeamLabRuntimeStatus.Probing,
        TeamLabRuntimeStatus.Running
    ];

    public async Task<RuntimeReconciliationSummary> ReconcileAsync(
        Guid runId,
        TimeSpan staleAfter,
        CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        await capacity.RenewActiveTicketReservationsAsync(token);

        var nodes = await context.WorkerNodes.AsNoTracking().ToArrayAsync(token);
        foreach (var nodeId in nodes.Select(node => node.Id))
            await capacity.ReconcileReservedAsync(nodeId, token);

        var activeTeamLabOwners = await LoadActiveTeamLabLifecycleOwnersAsync(token);
        var expected = await LoadExpectedFactsAsync(activeTeamLabOwners, token);
        var expectedTeamLabControl = await LoadExpectedTeamLabControlFactsAsync(activeTeamLabOwners, token);
        // Staleness means "nobody is executing this any more", not "this is taking a while".
        // The executor renews its claim while it works, and legitimate deployments routinely run
        // longer than the elapsed-time threshold — a Windows guest with a reboot-bearing bootstrap
        // profile easily does. Reclaiming those restarts the deployment from scratch, doubling
        // startup time, and rewrites the ExecutionStage of assets that are still in flight.
        var staleTickets = await context.DeploymentQueueTickets
            .Where(ticket => ticket.Status == DeploymentQueueTicketStatus.Running)
            .Where(ticket => ticket.ClaimOwner == null || ticket.ClaimExpiresAt == null ||
                             ticket.ClaimExpiresAt <= now)
            .Where(ticket => (ticket.StartedAt ?? ticket.AssignedAt ?? ticket.CreatedAt) < now - staleAfter)
            .OrderBy(ticket => ticket.CreatedAt)
            .ToArrayAsync(token);
        var inventories = await LoadInventoriesAsync(nodes, now, token);

        await RecordInventoryAvailabilityAsync(
            runId,
            expected.Select(item => item.WorkerNodeId)
                .Concat(expectedTeamLabControl.Select(item => item.WorkerNodeId))
                .Concat(staleTickets.Where(item => item.TargetNodeId != null)
                    .Select(item => item.TargetNodeId!.Value))
                .Distinct()
                .ToArray(),
            inventories,
            now,
            token);

        var matched = 0;
        var missing = 0;
        var conflicts = 0;
        var corrected = 0;
        var deferred = 0;
        foreach (var fact in expected)
        {
            if (!inventories.TryGetValue(fact.WorkerNodeId, out var inventory) ||
                inventory.Availability != InventoryAvailability.Available ||
                !inventory.Supports(fact.Kind))
            {
                deferred++;
                continue;
            }

            var actual = inventory.Find(fact);
            if (actual is null)
            {
                var conflicting = inventory.FindIdentityConflict(fact);
                if (conflicting is not null)
                {
                    conflicts++;
                    if (CorrectExpectedFact(fact, "runtime_identity_conflict"))
                    {
                        corrected++;
                        AppendFactCorrection(runId, fact, OperationalEventCodes.Recovery.IdentityConflict,
                            "Managed runtime resource native identity conflicts with the database fact.", conflicting,
                            "runtime_identity_conflict");
                    }
                    continue;
                }

                missing++;
                if (CorrectExpectedFact(fact, "resource_missing"))
                {
                    corrected++;
                    AppendFactCorrection(runId, fact, OperationalEventCodes.Recovery.ResourceMissing,
                        "Managed runtime resource is missing from the assigned node.", null, "resource_missing");
                }
                continue;
            }

            if (fact.Generation is { } expectedGeneration && actual.Generation != expectedGeneration)
            {
                conflicts++;
                if (CorrectExpectedFact(fact, "runtime_identity_conflict"))
                {
                    corrected++;
                    AppendFactCorrection(runId, fact, OperationalEventCodes.Recovery.IdentityConflict,
                        "Managed runtime resource generation does not match the database fact.", actual,
                        "runtime_identity_conflict");
                }
                continue;
            }

            fact.BackfillNativeIdentity(actual);

            if (!IsActive(fact.Kind, actual.State))
            {
                missing++;
                if (CorrectExpectedFact(fact, "resource_not_running"))
                {
                    corrected++;
                    AppendFactCorrection(runId, fact, OperationalEventCodes.Recovery.ResourceMissing,
                        "Managed runtime resource exists but is not running.", actual, "resource_not_running");
                }
                continue;
            }

            matched++;
        }

        var control = await ReconcileTeamLabControlFactsAsync(
            runId, expectedTeamLabControl, activeTeamLabOwners, inventories, now, token);
        matched += control.Matched;
        missing += control.Missing;
        conflicts += control.Conflicts;
        deferred += control.Deferred;

        var orphanCount = await ObserveOrphansAsync(
            runId, expected, activeTeamLabOwners, inventories, token) + control.Orphans;
        var replayIds = new List<Guid>();
        var recoveredTickets = 0;
        var replayedTickets = 0;
        foreach (var ticket in staleTickets)
        {
            var inspection = await InspectTicketAsync(ticket, inventories, token);
            ticket.ClaimOwner = null;
            ticket.ClaimExpiresAt = null;
            switch (inspection.Decision)
            {
                case TicketRecoveryDecision.Completed:
                    CompleteTicket(ticket, inspection);
                    await ConfirmTicketCapacityAsync(ticket, token);
                    recoveredTickets++;
                    matched++;
                    break;
                case TicketRecoveryDecision.SafeReplay:
                    ReplayTicket(ticket, inspection);
                    replayIds.Add(ticket.Id);
                    replayedTickets++;
                    break;
                case TicketRecoveryDecision.Deferred:
                    await DeferTicketAsync(ticket, inspection, runId, now, token);
                    deferred++;
                    break;
                default:
                    FailTicket(ticket, inspection);
                    await ReleaseTicketCapacityAsync(ticket, token);
                    conflicts++;
                    break;
            }
        }

        await context.SaveChangesAsync(token);
        foreach (var ticketId in replayIds)
            await wakeup.NotifyAsync(ticketId, token);

        return new RuntimeReconciliationSummary(
            matched,
            missing,
            conflicts,
            orphanCount,
            deferred,
            corrected,
            replayedTickets,
            recoveredTickets);
    }

    private async Task<int[]> LoadActiveTeamLabLifecycleOwnersAsync(CancellationToken token) =>
        await context.DeploymentQueueTickets.AsNoTracking()
            .Where(item => item.Kind == DeploymentQueueKind.TeamLabRuntime &&
                           item.TeamLabRuntimeId != null &&
                           (item.Operation == RuntimeOperationKind.Create ||
                            item.Operation == RuntimeOperationKind.Reset ||
                            item.Operation == RuntimeOperationKind.Destroy) &&
                           (item.Status == DeploymentQueueTicketStatus.Pending ||
                            item.Status == DeploymentQueueTicketStatus.Scheduling ||
                            item.Status == DeploymentQueueTicketStatus.Scheduled ||
                            item.Status == DeploymentQueueTicketStatus.Running))
            .Select(item => item.TeamLabRuntimeId!.Value)
            .Distinct()
            .ToArrayAsync(token);

    private async Task<ExpectedRuntimeFact[]> LoadExpectedFactsAsync(
        IReadOnlyCollection<int> activeTeamLabOwners,
        CancellationToken token)
    {
        var containers = await context.Containers
            .Where(item => item.NodeId != null && item.Status != ContainerStatus.Destroyed &&
                           item.ContainerId != string.Empty)
            .ToArrayAsync(token);
        var vms = await context.VmInstances
            .Where(item => item.NodeId != null &&
                           (item.Status == VmInstanceStatus.Creating || item.Status == VmInstanceStatus.Running) &&
                           item.VmName != string.Empty)
            .ToArrayAsync(token);
        var teamLabAssets = await context.TeamLabRuntimeAssets
            .Include(item => item.Runtime)
            .Include(item => item.Shard)
            .Where(item => item.WorkerNodeId != null && item.RuntimeResourceId != null &&
                           item.Generation == item.Runtime.Generation &&
                           !activeTeamLabOwners.Contains(item.RuntimeId) &&
                           (item.Kind == TeamLabResourceKind.Docker || item.Kind == TeamLabResourceKind.Vm) &&
                           ExpectedTeamLabWorkloadStatuses.Contains(item.Status) &&
                           ActiveTeamLabRuntimeStatuses.Contains(item.Runtime.Status))
            .ToArrayAsync(token);

        return containers.Select(ExpectedRuntimeFact.FromContainer)
            .Concat(vms.Select(ExpectedRuntimeFact.FromVm))
            .Concat(teamLabAssets.Select(ExpectedRuntimeFact.FromTeamLabAsset))
            .ToArray();
    }

    private async Task<ExpectedTeamLabControlFact[]> LoadExpectedTeamLabControlFactsAsync(
        IReadOnlyCollection<int> activeTeamLabOwners,
        CancellationToken token)
    {
        var networks = await context.TeamLabRuntimeNetworks.AsNoTracking()
            .Include(item => item.Runtime)
            .Where(item => item.WorkerNodeId != null && item.Generation == item.Runtime.Generation &&
                           !activeTeamLabOwners.Contains(item.RuntimeId) &&
                           ActiveTeamLabRuntimeStatuses.Contains(item.Runtime.Status))
            .ToArrayAsync(token);
        var fragments = await context.TeamLabRuntimeInfrastructureFragments.AsNoTracking()
            .Include(item => item.Infrastructure)
            .ThenInclude(item => item.Runtime)
            .Where(item => item.Infrastructure.Generation == item.Infrastructure.Runtime.Generation &&
                           !activeTeamLabOwners.Contains(item.Infrastructure.RuntimeId) &&
                           item.Infrastructure.Kind == TeamLabInfrastructureKind.ManagedRouter &&
                           ActiveTeamLabRuntimeStatuses.Contains(item.Infrastructure.Runtime.Status))
            .ToArrayAsync(token);
        var fabric = await context.TeamLabFabricLinkLeases.AsNoTracking()
            .Include(item => item.Runtime)
            .Where(item => item.ReleasedAt == null && item.Generation == item.Runtime.Generation &&
                           !activeTeamLabOwners.Contains(item.RuntimeId) &&
                           ActiveTeamLabRuntimeStatuses.Contains(item.Runtime.Status))
            .ToArrayAsync(token);
        var observations = await context.TeamLabObservationPoints.AsNoTracking()
            .Include(item => item.Runtime)
            .Where(item => item.Enabled && item.Generation == item.Runtime.Generation &&
                           !activeTeamLabOwners.Contains(item.RuntimeId) &&
                           ActiveTeamLabRuntimeStatuses.Contains(item.Runtime.Status))
            .ToArrayAsync(token);
        var assets = await context.TeamLabRuntimeAssets.AsNoTracking()
            .Include(item => item.Runtime)
            .Where(item => item.WorkerNodeId != null && item.RuntimeResourceId != null &&
                           item.Generation == item.Runtime.Generation &&
                           !activeTeamLabOwners.Contains(item.RuntimeId) &&
                           ActiveTeamLabRuntimeStatuses.Contains(item.Runtime.Status))
            .ToArrayAsync(token);
        var captures = await context.TeamLabTrafficCaptureSegments.AsNoTracking()
            .Include(item => item.CaptureJob)
            .ThenInclude(item => item.Runtime)
            .Where(item => item.CaptureJob.Generation == item.CaptureJob.Runtime.Generation &&
                           !activeTeamLabOwners.Contains(item.CaptureJob.RuntimeId) &&
                           ActiveTeamLabRuntimeStatuses.Contains(item.CaptureJob.Runtime.Status) &&
                           item.Status != TeamLabTrafficCaptureSegmentStatus.Pending &&
                           item.Status != TeamLabTrafficCaptureSegmentStatus.Failed &&
                           item.Status != TeamLabTrafficCaptureSegmentStatus.Expired)
            .ToArrayAsync(token);

        return networks.SelectMany(item => new[]
            {
                new ExpectedTeamLabControlFact(
                    item.WorkerNodeId!.Value, item.RuntimeId, item.Generation, "managed-switch",
                    item.TopologyKey, item.BridgeName, null),
                new ExpectedTeamLabControlFact(
                    item.WorkerNodeId.Value, item.RuntimeId, item.Generation, "dhcp-dns",
                    item.TopologyKey, TeamLabResourceNameFactory.DhcpDnsService(item.RuntimeId, item.TopologyKey), null)
            })
            .Concat(fragments.Select(item => new ExpectedTeamLabControlFact(
                item.WorkerNodeId,
                item.Infrastructure.RuntimeId,
                item.Infrastructure.Generation,
                "managed-router-fragment",
                item.Infrastructure.TopologyKey,
                item.NativeResourceId ?? TeamLabResourceNameFactory.RouterNamespace(
                    item.Infrastructure.RuntimeId, item.ShardId),
                item.DesiredStateDigest)))
            .Concat(fabric.Select(item => new ExpectedTeamLabControlFact(
                item.WorkerNodeId,
                item.RuntimeId,
                item.Generation,
                "fabric-uplink",
                "fabric",
                TeamLabResourceNameFactory.FabricHostInterface(item.RuntimeId),
                null)))
            .Concat(observations.Select(item => new ExpectedTeamLabControlFact(
                item.WorkerNodeId,
                item.RuntimeId,
                item.Generation,
                "observation-point",
                item.PublicId.ToString("D"),
                item.InterfaceToken,
                item.DesiredStateDigest)))
            .Concat(assets.Where(item => item.EndpointObservation != TeamLabEndpointObservationMode.Disabled)
                .Select(item => new ExpectedTeamLabControlFact(
                    item.WorkerNodeId!.Value,
                    item.RuntimeId,
                    item.Generation,
                    "endpoint-sensor",
                    item.TopologyKey,
                    item.RuntimeResourceId,
                    null)))
            .Concat(captures.Select(item => new ExpectedTeamLabControlFact(
                item.WorkerNodeId,
                item.CaptureJob.RuntimeId,
                item.CaptureJob.Generation,
                "pcap-segment",
                item.PublicId.ToString("D"),
                null,
                item.Sha256)))
            .OrderBy(item => item.WorkerNodeId)
            .ThenBy(item => item.RuntimeId)
            .ThenBy(item => item.ResourceKind, StringComparer.Ordinal)
            .ThenBy(item => item.StableName, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<TeamLabControlReconciliation> ReconcileTeamLabControlFactsAsync(
        Guid runId,
        IReadOnlyCollection<ExpectedTeamLabControlFact> expected,
        IReadOnlyCollection<int> activeTeamLabOwners,
        IReadOnlyDictionary<Guid, NodeInventory> inventories,
        DateTimeOffset now,
        CancellationToken token)
    {
        var matched = 0;
        var missing = 0;
        var conflicts = 0;
        var deferred = 0;
        var recentCutoff = now.AddMinutes(-15);
        var recent = (await context.OperationalEvents.AsNoTracking()
                .Where(item => item.OccurredAt >= recentCutoff && item.WorkerNodeId != null &&
                               (item.EventCode == OperationalEventCodes.Recovery.ResourceMissing ||
                                item.EventCode == OperationalEventCodes.Recovery.IdentityConflict ||
                                item.EventCode == OperationalEventCodes.Recovery.OrphanObserved))
                .Select(item => new { item.EventCode, item.WorkerNodeId, item.ResourceType, item.ResourceId })
                .ToArrayAsync(token))
            .Select(item => $"{item.EventCode}|{item.WorkerNodeId:D}|{item.ResourceType}|{item.ResourceId}")
            .ToHashSet(StringComparer.Ordinal);
        var expectedKeys = expected.Select(item => item.IdentityKey).ToHashSet(StringComparer.Ordinal);

        foreach (var fact in expected)
        {
            if (!inventories.TryGetValue(fact.WorkerNodeId, out var inventory) ||
                inventory.Availability != InventoryAvailability.Available)
            {
                deferred++;
                continue;
            }
            var actual = inventory.TeamLabResources.FirstOrDefault(item =>
                item.RuntimeId == fact.RuntimeId && item.Generation == fact.Generation &&
                item.ResourceKind.Equals(fact.ResourceKind, StringComparison.Ordinal) &&
                item.StableName.Equals(fact.StableName, StringComparison.Ordinal));
            if (actual is null)
            {
                missing++;
                AppendControlFactEvent(
                    recent, runId, fact, OperationalEventCodes.Recovery.ResourceMissing,
                    OperationalEventOutcome.Blocked, "Managed TeamLab control resource is missing.");
                continue;
            }
            var identityConflict = !string.IsNullOrWhiteSpace(fact.NativeId) &&
                                   !actual.NativeId.Equals(fact.NativeId, StringComparison.Ordinal);
            var digestConflict = !string.IsNullOrWhiteSpace(fact.DesiredStateDigest) &&
                                 !string.IsNullOrWhiteSpace(actual.DesiredStateDigest) &&
                                 !NormalizeDigest(actual.DesiredStateDigest)
                                     .Equals(NormalizeDigest(fact.DesiredStateDigest), StringComparison.Ordinal);
            if (identityConflict || digestConflict || actual.State.Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                conflicts++;
                AppendControlFactEvent(
                    recent, runId, fact, OperationalEventCodes.Recovery.IdentityConflict,
                    OperationalEventOutcome.Blocked,
                    "Managed TeamLab control resource conflicts with the desired runtime fact.");
                continue;
            }
            matched++;
        }

        var orphanCandidates = inventories.Values
            .Where(item => item.Availability == InventoryAvailability.Available)
            .SelectMany(item => item.TeamLabResources.Select(resource => (item.WorkerNodeId, Resource: resource)))
            .Where(item => item.Resource.RuntimeId.HasValue &&
                           !activeTeamLabOwners.Contains(item.Resource.RuntimeId.Value) &&
                           !expectedKeys.Contains(ExpectedTeamLabControlFact.IdentityKeyFor(
                               item.WorkerNodeId, item.Resource)))
            .ToArray();
        var orphans = 0;
        foreach (var candidate in orphanCandidates)
        {
            var resourceType = $"teamlab-{candidate.Resource.ResourceKind}";
            var eventKey = $"{OperationalEventCodes.Recovery.OrphanObserved}|{candidate.WorkerNodeId:D}|" +
                           $"{resourceType}|{candidate.Resource.StableName}";
            if (!recent.Add(eventKey)) continue;
            events.Append(new OperationalEventDraft(
                OperationalEventCodes.Recovery.OrphanObserved,
                OperationalEventOutcome.Observed,
                "Agent reported a TeamLab control resource with no current desired-state owner.",
                OperationalEventSeverity.Warning,
                runId,
                Detail: new Dictionary<string, object?>
                {
                    ["generation"] = candidate.Resource.Generation,
                    ["currentStatus"] = candidate.Resource.State,
                    ["reasonCode"] = "teamlab_control_orphan"
                },
                WorkerNodeId: candidate.WorkerNodeId,
                TeamLabRuntimeId: candidate.Resource.RuntimeId,
                SubjectType: "teamlab-runtime",
                SubjectId: candidate.Resource.RuntimeId?.ToString(),
                ResourceType: resourceType,
                ResourceId: candidate.Resource.StableName));
            orphans++;
        }
        return new TeamLabControlReconciliation(matched, missing, conflicts, orphans, deferred);
    }

    private void AppendControlFactEvent(
        ISet<string> recent,
        Guid runId,
        ExpectedTeamLabControlFact fact,
        string eventCode,
        OperationalEventOutcome outcome,
        string message)
    {
        var resourceType = $"teamlab-{fact.ResourceKind}";
        var eventKey = $"{eventCode}|{fact.WorkerNodeId:D}|{resourceType}|{fact.StableName}";
        if (!recent.Add(eventKey)) return;
        events.Append(new OperationalEventDraft(
            eventCode,
            outcome,
            message,
            OperationalEventSeverity.Warning,
            runId,
            Detail: new Dictionary<string, object?>
            {
                ["generation"] = fact.Generation,
                ["reasonCode"] = eventCode == OperationalEventCodes.Recovery.ResourceMissing
                    ? "teamlab_control_missing"
                    : "teamlab_control_conflict"
            },
            WorkerNodeId: fact.WorkerNodeId,
            TeamLabRuntimeId: fact.RuntimeId,
            SubjectType: "teamlab-runtime",
            SubjectId: fact.RuntimeId.ToString(),
            ResourceType: resourceType,
            ResourceId: fact.StableName));
    }

    private async Task<Dictionary<Guid, NodeInventory>> LoadInventoriesAsync(
        IReadOnlyCollection<WorkerNode> nodes,
        DateTimeOffset now,
        CancellationToken token)
    {
        using var limiter = new SemaphoreSlim(8, 8);
        var tasks = nodes.Select(async node =>
        {
            if (node.IsLocal &&
                !AgentCapabilityEvaluator.Supports(node, AgentFeatureIds.RuntimeInventory))
                return new NodeInventory(
                    node.Id,
                    InventoryAvailability.Local,
                    node.Capabilities.HasFlag(NodeCapability.Docker),
                    node.Capabilities.HasFlag(NodeCapability.Kvm),
                    [],
                    [],
                    [],
                    null);
            if (node.GetEffectiveStatus(now) != NodeStatus.Online)
                return new NodeInventory(node.Id, InventoryAvailability.Offline, false, false, [], [], [],
                    "Node is offline.");
            if (!AgentCapabilityEvaluator.Supports(node, AgentFeatureIds.RuntimeInventory))
                return new NodeInventory(node.Id, InventoryAvailability.Unsupported, false, false, [], [], [],
                    "Agent does not advertise runtime inventory support.");

            await limiter.WaitAsync(token);
            try
            {
                var response = await agentClient.GetRuntimeInventoryAsync(node.Id, token);
                return new NodeInventory(
                    node.Id,
                    InventoryAvailability.Available,
                    response.DockerSupported,
                    response.KvmSupported,
                    response.Containers,
                    response.Vms,
                    response.TeamLabResources ?? [],
                    null);
            }
            catch (AgentClientException exception) when (
                exception.Error.Code == OperationalErrorCodes.AgentFeatureMissing)
            {
                return new NodeInventory(node.Id, InventoryAvailability.Unsupported, false, false, [], [], [],
                    exception.Error.Message);
            }
            catch (Exception exception) when (exception is AgentClientException or HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(exception, "Runtime inventory is unavailable on node {NodeId}.", node.Id);
                return new NodeInventory(node.Id, InventoryAvailability.Unavailable, false, false, [], [], [],
                    exception.Message);
            }
            finally
            {
                limiter.Release();
            }
        });

        return (await Task.WhenAll(tasks)).ToDictionary(item => item.WorkerNodeId);
    }

    private async Task RecordInventoryAvailabilityAsync(
        Guid runId,
        IReadOnlyCollection<Guid> relevantNodeIds,
        IReadOnlyDictionary<Guid, NodeInventory> inventories,
        DateTimeOffset now,
        CancellationToken token)
    {
        if (relevantNodeIds.Count == 0)
            return;
        var cutoff = now.AddMinutes(-15);
        var recent = await context.OperationalEvents.AsNoTracking()
            .Where(item => item.WorkerNodeId != null && relevantNodeIds.Contains(item.WorkerNodeId.Value) &&
                           item.OccurredAt >= cutoff &&
                           (item.EventCode == OperationalEventCodes.Recovery.NodeUnavailable ||
                            item.EventCode == OperationalEventCodes.Recovery.InventoryUnsupported ||
                            item.EventCode == OperationalEventCodes.Agent.InventoryUnavailable))
            .Select(item => new { NodeId = item.WorkerNodeId!.Value, item.EventCode })
            .ToArrayAsync(token);
        var existing = recent.Select(item => (item.NodeId, item.EventCode)).ToHashSet();

        foreach (var nodeId in relevantNodeIds)
        {
            if (!inventories.TryGetValue(nodeId, out var inventory) ||
                inventory.Availability is InventoryAvailability.Available or InventoryAvailability.Local)
                continue;
            var code = inventory.Availability switch
            {
                InventoryAvailability.Unsupported => OperationalEventCodes.Recovery.InventoryUnsupported,
                InventoryAvailability.Offline => OperationalEventCodes.Recovery.NodeUnavailable,
                _ => OperationalEventCodes.Agent.InventoryUnavailable
            };
            if (!existing.Add((nodeId, code)))
                continue;
            events.Append(new OperationalEventDraft(
                code,
                OperationalEventOutcome.Blocked,
                inventory.Error ?? "Runtime inventory is unavailable.",
                OperationalEventSeverity.Warning,
                runId,
                inventory.Availability == InventoryAvailability.Unsupported
                    ? OperationalErrorCategory.AgentProtocol
                    : OperationalErrorCategory.NodeUnavailable,
                inventory.Availability == InventoryAvailability.Unsupported
                    ? OperationalErrorCodes.AgentFeatureMissing
                    : OperationalErrorCodes.NodeOffline,
                true,
                Detail: new Dictionary<string, object?>
                {
                    ["reasonCode"] = inventory.Availability.ToString()
                },
                WorkerNodeId: nodeId,
                SubjectType: "worker-node",
                SubjectId: nodeId.ToString(),
                ResourceType: "runtime-inventory",
                ResourceId: nodeId.ToString()));
        }
    }

    private async Task<int> ObserveOrphansAsync(
        Guid runId,
        IReadOnlyCollection<ExpectedRuntimeFact> expected,
        IReadOnlyCollection<int> activeTeamLabOwners,
        IReadOnlyDictionary<Guid, NodeInventory> inventories,
        CancellationToken token)
    {
        var expectedKeys = expected.Select(item => item.IdentityKey).ToHashSet(StringComparer.Ordinal);
        var candidates = inventories.Values
            .Where(item => item.Availability == InventoryAvailability.Available)
            .SelectMany(item => item.AllResources())
            .Where(item => !expectedKeys.Contains(item.IdentityKey) &&
                           (!item.Resource.RuntimeId.HasValue ||
                            !activeTeamLabOwners.Contains(item.Resource.RuntimeId.Value)))
            .ToArray();
        if (candidates.Length == 0)
            return 0;

        var nodeIds = candidates.Select(item => item.WorkerNodeId).Distinct().ToArray();
        var resourceIds = candidates.Select(item => item.EventResourceId).Distinct(StringComparer.Ordinal).ToArray();
        var existing = (await context.OperationalEvents.AsNoTracking()
                .Where(item => item.EventCode == OperationalEventCodes.Recovery.OrphanObserved &&
                               item.WorkerNodeId != null && nodeIds.Contains(item.WorkerNodeId.Value) &&
                               item.ResourceId != null && resourceIds.Contains(item.ResourceId))
                .Select(item => new { NodeId = item.WorkerNodeId!.Value, item.ResourceType, item.ResourceId })
                .ToArrayAsync(token))
            .Select(item => $"{item.NodeId:D}|{item.ResourceType}|{item.ResourceId}")
            .ToHashSet(StringComparer.Ordinal);

        var observed = 0;
        foreach (var candidate in candidates)
        {
            var key = $"{candidate.WorkerNodeId:D}|{candidate.EventResourceType}|{candidate.EventResourceId}";
            if (!existing.Add(key))
                continue;
            events.Append(new OperationalEventDraft(
                OperationalEventCodes.Recovery.OrphanObserved,
                OperationalEventOutcome.Observed,
                "Agent reported a managed runtime resource with no active database owner.",
                OperationalEventSeverity.Warning,
                runId,
                Detail: new Dictionary<string, object?>
                {
                    ["generation"] = candidate.Resource.Generation,
                    ["currentStatus"] = candidate.Resource.State,
                    ["reasonCode"] = "orphan_resource"
                },
                WorkerNodeId: candidate.WorkerNodeId,
                SubjectType: "worker-node",
                SubjectId: candidate.WorkerNodeId.ToString(),
                ResourceType: candidate.EventResourceType,
                ResourceId: candidate.EventResourceId,
                ResourceDisplayName: candidate.Resource.StableName));
            observed++;
        }
        return observed;
    }

    private bool CorrectExpectedFact(ExpectedRuntimeFact fact, string reasonCode)
    {
        switch (fact.Container)
        {
            case { Status: not ContainerStatus.Destroyed } container:
                container.Status = ContainerStatus.Destroyed;
                return true;
        }
        switch (fact.Vm)
        {
            case { Status: not VmInstanceStatus.Error and not VmInstanceStatus.Destroyed } vm:
                vm.Status = VmInstanceStatus.Error;
                return true;
        }
        if (fact.TeamLabAsset is not { } asset || asset.Status == TeamLabRuntimeStatus.Failed)
            return false;

        asset.Status = TeamLabRuntimeStatus.Failed;
        asset.LastError = reasonCode;
        if (asset.Shard is not null)
        {
            asset.Shard.Status = TeamLabRuntimeStatus.Failed;
            asset.Shard.LastError = reasonCode;
        }
        asset.Runtime.Status = TeamLabRuntimeStatus.Failed;
        asset.Runtime.IsOpenToPlayers = false;
        asset.Runtime.LastError = reasonCode;
        asset.Runtime.UpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }

    private void AppendFactCorrection(
        Guid runId,
        ExpectedRuntimeFact fact,
        string findingCode,
        string message,
        AgentRuntimeInventoryResource? actual,
        string reasonCode)
    {
        var conflict = findingCode == OperationalEventCodes.Recovery.IdentityConflict;
        var missing = findingCode == OperationalEventCodes.Recovery.ResourceMissing;
        events.Append(new OperationalEventDraft(
            findingCode,
            conflict || missing ? OperationalEventOutcome.Failed : OperationalEventOutcome.Observed,
            message,
            OperationalEventSeverity.Warning,
            runId,
            conflict || missing ? OperationalErrorCategory.Conflict : null,
            conflict
                ? OperationalErrorCodes.RuntimeIdentityConflict
                : missing
                    ? OperationalErrorCodes.RuntimeResourceMissing
                    : null,
            false,
            new Dictionary<string, object?>
            {
                ["generation"] = actual?.Generation ?? fact.Generation,
                ["currentStatus"] = actual?.State ?? "missing",
                ["reasonCode"] = reasonCode
            },
            WorkerNodeId: fact.WorkerNodeId,
            TeamLabRuntimeId: fact.TeamLabAsset?.RuntimeId,
            VmInstanceId: fact.Vm?.Id,
            SubjectType: fact.SubjectType,
            SubjectId: fact.SubjectId,
            SubjectDisplayName: fact.DisplayName,
            ResourceType: fact.EventResourceType,
            ResourceId: fact.EventResourceId,
            ResourceDisplayName: fact.DisplayName));
        events.Append(new OperationalEventDraft(
            OperationalEventCodes.Recovery.StateCorrected,
            OperationalEventOutcome.Recovered,
            "Database runtime state was corrected from Agent inventory.",
            OperationalEventSeverity.Warning,
            runId,
            Detail: new Dictionary<string, object?>
            {
                ["previousStatus"] = fact.PreviousStatus,
                ["currentStatus"] = fact.CorrectedStatus,
                ["reasonCode"] = reasonCode
            },
            WorkerNodeId: fact.WorkerNodeId,
            TeamLabRuntimeId: fact.TeamLabAsset?.RuntimeId,
            VmInstanceId: fact.Vm?.Id,
            SubjectType: fact.SubjectType,
            SubjectId: fact.SubjectId,
            SubjectDisplayName: fact.DisplayName,
            ResourceType: fact.EventResourceType,
            ResourceId: fact.EventResourceId,
            ResourceDisplayName: fact.DisplayName));
    }

    private async Task<TicketInspection> InspectTicketAsync(
        DeploymentQueueTicket ticket,
        IReadOnlyDictionary<Guid, NodeInventory> inventories,
        CancellationToken token)
    {
        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime &&
            ticket.Operation == RuntimeOperationKind.Reset)
            return await InspectTeamLabResetTicketAsync(ticket, token);

        if (ticket.Operation != RuntimeOperationKind.Create)
            return ticket.Operation is RuntimeOperationKind.Stop or RuntimeOperationKind.Destroy
                ? ticket.Kind == DeploymentQueueKind.TeamLabRuntime
                    ? await InspectTeamLabControlTicketAsync(ticket, inventories, token)
                    : await InspectControlTicketAsync(ticket, inventories, token)
                : TicketInspection.FailClosed("Operation cannot be proven safe to replay.",
                    OperationalErrorCodes.RuntimeIdentityConflict);

        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime)
            return await InspectTeamLabTicketAsync(ticket, inventories, token);
        if (ticket.TargetNodeId is not { } nodeId)
            return TicketInspection.FailClosed("Deployment ticket has no target node.",
                OperationalErrorCodes.NodeNotFound);
        if (!inventories.TryGetValue(nodeId, out var inventory))
            return TicketInspection.Deferred("Assigned node inventory is unavailable.",
                OperationalErrorCodes.NodeOffline);

        var resource = await ResolveTicketResourceAsync(ticket, token);
        if (inventory.Availability is not (InventoryAvailability.Available or InventoryAvailability.Local))
            return TicketInspection.Deferred(inventory.Error ?? "Assigned node inventory is unavailable.",
                inventory.Availability == InventoryAvailability.Unsupported
                    ? OperationalErrorCodes.AgentFeatureMissing
                    : OperationalErrorCodes.NodeOffline);
        var requiredKind = ResourceKind(ticket);
        if (!inventory.Supports(requiredKind))
            return TicketInspection.Deferred("Assigned node cannot report the required runtime kind.",
                OperationalErrorCodes.AgentFeatureMissing);
        if (inventory.Availability == InventoryAvailability.Local)
            return resource?.IsDatabaseRunning == true
                ? TicketInspection.Completed("Local database runtime fact confirms completion.", resource)
                : TicketInspection.SafeReplay("Local create operation is safe to replay by stable identity.");
        if (resource is null)
            return TicketInspection.SafeReplay("No persisted runtime resource exists; create is safe to replay.");
        if (resource.WorkerNodeId is { } resourceNode && resourceNode != nodeId)
            return TicketInspection.FailClosed("Persisted runtime resource belongs to a different node.",
                OperationalErrorCodes.RuntimeIdentityConflict);
        if (resource.Generation != ticket.Generation)
            return TicketInspection.FailClosed("Persisted runtime generation does not match the deployment ticket.",
                OperationalErrorCodes.RuntimeIdentityConflict, resource);

        var actual = inventory.Find(resource);
        if (actual is null)
        {
            if (inventory.FindIdentityConflict(resource) is not null)
                return TicketInspection.FailClosed("Runtime native identity does not match the deployment ticket.",
                    OperationalErrorCodes.RuntimeIdentityConflict, resource);
            return TicketInspection.SafeReplay("Runtime resource is absent; create is safe to replay by stable identity.",
                resource);
        }
        if (actual.Generation != ticket.Generation)
            return TicketInspection.FailClosed("Runtime generation does not match the deployment ticket.",
                OperationalErrorCodes.RuntimeIdentityConflict, resource);
        resource.BackfillNativeIdentity(actual);
        return IsActive(resource.Kind, actual.State)
            ? TicketInspection.Completed("Agent inventory confirms the runtime resource.", resource)
            : TicketInspection.SafeReplay("Runtime resource is not running; create is safe to replay.", resource);
    }

    private async Task<TicketInspection> InspectTeamLabResetTicketAsync(
        DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.TeamLabRuntimeId is not { } runtimeId)
            return TicketInspection.FailClosed(
                "TeamLab reset ticket has no runtime identity.",
                OperationalErrorCodes.RuntimeIdentityConflict);
        var runtime = await context.TeamLabRuntimes
            .Include(item => item.Events)
            .SingleOrDefaultAsync(item => item.Id == runtimeId, token);
        if (runtime is null)
            return TicketInspection.FailClosed(
                "TeamLab runtime no longer exists.",
                OperationalErrorCodes.RuntimeIdentityConflict);

        var checkpoint = TeamLabResetCheckpointFacts.Get(runtime, ticket.Id);
        if (runtime.Generation == ticket.Generation && runtime.Status == TeamLabRuntimeStatus.Running)
            return TicketInspection.Completed(
                "Runtime facts confirm the reset target generation is running.");
        if (runtime.Generation < ticket.Generation - 1 || runtime.Generation > ticket.Generation)
            return TicketInspection.FailClosed(
                "TeamLab reset checkpoint conflicts with the persisted runtime generation.",
                OperationalErrorCodes.RuntimeIdentityConflict);

        return checkpoint switch
        {
            TeamLabResetCheckpoint.CleaningPreviousGeneration
                when runtime.Generation == ticket.Generation - 1 =>
                TicketInspection.SafeReplay(
                    "Reset cleanup checkpoint can resume without advancing generation."),
            TeamLabResetCheckpoint.PlanningNextGeneration
                when runtime.Generation is var generation &&
                     (generation == ticket.Generation - 1 || generation == ticket.Generation) =>
                TicketInspection.SafeReplay(
                    "Reset planning checkpoint can resume from persisted generation facts."),
            TeamLabResetCheckpoint.ReservingNextGeneration or
                TeamLabResetCheckpoint.DeployingNextGeneration
                when runtime.Generation == ticket.Generation =>
                TicketInspection.SafeReplay(
                    "Reset reservation or deployment checkpoint can resume idempotently."),
            null when runtime.Generation == ticket.Generation - 1 =>
                TicketInspection.SafeReplay(
                    "Reset has not persisted its first checkpoint and can start idempotently."),
            _ => TicketInspection.FailClosed(
                "TeamLab reset checkpoint cannot be reconciled with runtime state.",
                OperationalErrorCodes.RuntimeIdentityConflict)
        };
    }

    private async Task<TicketInspection> InspectControlTicketAsync(
        DeploymentQueueTicket ticket,
        IReadOnlyDictionary<Guid, NodeInventory> inventories,
        CancellationToken token)
    {
        if (ticket.TargetNodeId is not { } nodeId)
            return TicketInspection.FailClosed("Control ticket has no target node.", OperationalErrorCodes.NodeNotFound);
        if (!inventories.TryGetValue(nodeId, out var inventory) ||
            inventory.Availability is InventoryAvailability.Offline or InventoryAvailability.Unavailable or InventoryAvailability.Unsupported)
            return TicketInspection.Deferred(inventory?.Error ?? "Assigned node inventory is unavailable.",
                inventory?.Availability == InventoryAvailability.Unsupported
                    ? OperationalErrorCodes.AgentFeatureMissing
                    : OperationalErrorCodes.NodeOffline);
        var requiredKind = ResourceKind(ticket);
        if (!inventory.Supports(requiredKind))
            return TicketInspection.Deferred("Assigned node cannot report the required runtime kind.",
                OperationalErrorCodes.AgentFeatureMissing);

        var resource = await ResolveTicketResourceAsync(ticket, token);
        if (resource is null)
            return TicketInspection.Completed("Runtime resource no longer exists; control operation is complete.");
        if (resource.WorkerNodeId is { } resourceNode && resourceNode != nodeId)
            return TicketInspection.FailClosed("Persisted runtime resource belongs to a different node.",
                OperationalErrorCodes.RuntimeIdentityConflict);
        if (resource.Generation != ticket.Generation)
            return TicketInspection.FailClosed("Control ticket generation does not match the persisted runtime.",
                OperationalErrorCodes.RuntimeIdentityConflict);
        if (inventory.Availability == InventoryAvailability.Local)
            return resource.IsDatabaseDestroyed
                ? TicketInspection.Completed("Local database runtime fact confirms the control operation.", resource)
                : TicketInspection.SafeReplay("Local control operation is safe to replay.", resource);

        var actual = inventory.Find(resource);
        if (actual is null)
        {
            if (inventory.FindIdentityConflict(resource) is not null)
                return TicketInspection.FailClosed("Control target native identity has changed.",
                    OperationalErrorCodes.RuntimeIdentityConflict);
            resource.MarkDestroyed();
            return TicketInspection.Completed("Agent inventory confirms the runtime resource is absent.", resource);
        }
        if (actual.Generation != ticket.Generation)
            return TicketInspection.FailClosed("Control target generation has changed.",
                OperationalErrorCodes.RuntimeIdentityConflict);

        resource.BackfillNativeIdentity(actual);
        resource.MarkRunning(nodeId);
        return TicketInspection.SafeReplay("Agent inventory confirms the exact control target; replay is safe.", resource);
    }

    private async Task<TicketInspection> InspectTeamLabTicketAsync(
        DeploymentQueueTicket ticket,
        IReadOnlyDictionary<Guid, NodeInventory> inventories,
        CancellationToken token)
    {
        if (ticket.TeamLabRuntimeId is not { } runtimeId)
            return TicketInspection.FailClosed("TeamLab ticket has no runtime identity.",
                OperationalErrorCodes.RuntimeIdentityConflict);
        var runtime = await context.TeamLabRuntimes
            .Include(item => item.Assets)
            .Include(item => item.Shards)
            .Include(item => item.Infrastructure).ThenInclude(item => item.Fragments)
            .Include(item => item.AccessGrants)
            .Include(item => item.PublicUdpMapping)
            .SingleOrDefaultAsync(item => item.Id == runtimeId, token);
        if (runtime is null)
            return TicketInspection.FailClosed("TeamLab runtime no longer exists.",
                OperationalErrorCodes.RuntimeIdentityConflict);
        var assets = runtime.Assets.Where(item => item.Generation == runtime.Generation &&
                                                  (item.Kind == TeamLabResourceKind.Docker ||
                                                   item.Kind == TeamLabResourceKind.Vm)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var activeReservationNodes = await context.FleetCapacityReservations.AsNoTracking()
            .Where(item => item.DeploymentQueueTicketId == ticket.Id &&
                           item.Status == CapacityReservationStatus.Active && item.ExpiresAt > now)
            .Select(item => item.WorkerNodeId)
            .ToHashSetAsync(token);
        if (assets.Length == 0)
            return runtime.Status == TeamLabRuntimeStatus.Running
                ? TicketInspection.FailClosed("Running TeamLab runtime has no runtime assets.",
                    OperationalErrorCodes.RuntimeIdentityConflict)
                : TicketInspection.SafeReplay("TeamLab runtime has no created assets and can be replayed.");

        foreach (var asset in assets)
        {
            if (asset.WorkerNodeId is not { } nodeId || string.IsNullOrWhiteSpace(asset.RuntimeResourceId))
            {
                if (runtime.Status == TeamLabRuntimeStatus.Running)
                    return TicketInspection.FailClosed("Running TeamLab runtime has incomplete asset identity.",
                        OperationalErrorCodes.RuntimeIdentityConflict);
                var resume = teamLabRecovery.CanResumeExistingGeneration(runtime, ticket, now);
                return resume.Allowed && asset.ExecutionStage == TeamLabAssetExecutionStage.Pending
                    ? TicketInspection.SafeReplay("TeamLab asset was not created; persisted DAG execution can resume.")
                    : TicketInspection.Deferred(resume.Reason, OperationalErrorCodes.RecoveryDeferred);
            }
            if (!inventories.TryGetValue(nodeId, out var inventory) ||
                inventory.Availability is InventoryAvailability.Offline or InventoryAvailability.Unavailable or InventoryAvailability.Unsupported)
                return TicketInspection.Deferred(inventory?.Error ?? "TeamLab shard inventory is unavailable.",
                    inventory?.Availability == InventoryAvailability.Unsupported
                        ? OperationalErrorCodes.AgentFeatureMissing
                        : OperationalErrorCodes.NodeOffline);
            var fact = ExpectedRuntimeFact.FromTeamLabAsset(asset);
            if (!inventory.Supports(fact.Kind))
                return TicketInspection.Deferred("TeamLab shard cannot report the required runtime kind.",
                    OperationalErrorCodes.AgentFeatureMissing);
            if (inventory.Availability == InventoryAvailability.Local)
                continue;
            var actual = inventory.Find(fact);
            if (actual is null || !IsActive(fact.Kind, actual.State))
            {
                var rebuild = teamLabRecovery.CanRebuildMissingAsset(
                    runtime, ticket, asset, inventoryProvesMissing: true,
                    activeReservationNodes.Contains(nodeId), now);
                if (!rebuild.Allowed)
                {
                    events.Append(RuntimeOperationalEvents.Ticket(
                        ticket,
                        OperationalEventCodes.TeamLab.GuardedRebuildBlocked,
                        OperationalEventOutcome.Blocked,
                        "Automatic TeamLab workload rebuild was blocked by recovery policy.",
                        OperationalEventSeverity.Warning,
                        new OperationalError(
                            OperationalErrorCategory.Conflict,
                            OperationalErrorCodes.RuntimeIdentityConflict,
                            "Guarded workload rebuild was denied.",
                            false,
                            WorkerNodeId: nodeId,
                            Operation: "teamlab.recovery.rebuild"),
                        nodeId,
                        TeamLabRecoveryDetail(runtime, asset, "blocked", rebuild.Reason)));
                    PlatformTelemetry.RecordTeamLabRecovery(
                        "guarded-rebuild", asset.Kind.ToString(), "blocked");
                    return TicketInspection.FailClosed(
                        $"TeamLab asset is absent and cannot be rebuilt automatically: {rebuild.Reason}",
                        OperationalErrorCodes.RuntimeIdentityConflict);
                }
                events.Append(RuntimeOperationalEvents.Ticket(
                    ticket,
                    OperationalEventCodes.TeamLab.GuardedRebuildAllowed,
                    OperationalEventOutcome.Recovered,
                    "Automatic rebuild was allowed for a stateless TeamLab workload.",
                    workerNodeId: nodeId,
                    detail: TeamLabRecoveryDetail(runtime, asset, "allowed", rebuild.Reason)));
                PlatformTelemetry.RecordTeamLabRecovery(
                    "guarded-rebuild", asset.Kind.ToString(), "allowed");
                asset.RuntimeResourceId = null;
                asset.ExecutionStage = TeamLabAssetExecutionStage.Pending;
                asset.Status = TeamLabRuntimeStatus.Deploying;
                asset.LastError = null;
                return TicketInspection.SafeReplay(rebuild.Reason);
            }
            if (actual.Generation != runtime.Generation)
                return TicketInspection.FailClosed("TeamLab asset generation conflicts with the runtime generation.",
                    OperationalErrorCodes.RuntimeIdentityConflict);
            if (asset.ExecutionStage == TeamLabAssetExecutionStage.Pending)
            {
                asset.ExecutionStage = TeamLabAssetExecutionStage.GuestReady;
                asset.Status = TeamLabRuntimeStatus.Probing;
                asset.ExecutionUpdatedAt = now;
            }
        }

        var infrastructureDrift = false;
        var routeFactsComplete = true;
        foreach (var shard in runtime.Shards.Where(item => item.Generation == runtime.Generation))
        {
            var expectedDigests = runtime.Infrastructure
                .SelectMany(item => item.Fragments)
                .Where(item => item.ShardId == shard.Id)
                .Select(item => item.DesiredStateDigest)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            routeFactsComplete &= shard.RouteVersion > 0 && expectedDigests.Length == 1;
            TeamLabInfrastructureStateResponse state;
            try
            {
                state = await agentClient.GetTeamLabInfrastructureStateAsync(
                    shard.WorkerNodeId, runtime.Id, runtime.Generation, token);
            }
            catch (Exception exception) when (
                exception is AgentClientException or HttpRequestException or TaskCanceledException)
            {
                return TicketInspection.Deferred(
                    $"TeamLab infrastructure inventory is unavailable: {exception.Message}",
                    OperationalErrorCodes.NodeOffline);
            }
            if (!state.Exists || state.RouteVersion != shard.RouteVersion || expectedDigests.Length != 1 ||
                !string.Equals(state.DesiredStateDigest, expectedDigests[0], StringComparison.Ordinal))
                infrastructureDrift = true;
        }
        if (infrastructureDrift)
        {
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.TeamLab.InfrastructureDriftDetected,
                OperationalEventOutcome.Observed,
                "TeamLab infrastructure desired-state drift was detected.",
                OperationalEventSeverity.Warning,
                new OperationalError(
                    OperationalErrorCategory.Network,
                    OperationalErrorCodes.NetworkOperationFailed,
                    "Infrastructure desired state differs from WorkerNode facts.",
                    true,
                    Operation: "teamlab.infrastructure.reconcile"),
                detail: new Dictionary<string, object?>
                {
                    ["generation"] = runtime.Generation,
                    ["stage"] = "infrastructure-recovery",
                    ["decision"] = "drift-detected"
                }));
            PlatformTelemetry.RecordTeamLabInfrastructure("drift", "mixed");
            var activeGrants = runtime.AccessGrants.Where(item =>
                item.Generation == runtime.Generation && !item.Revoked).ToArray();
            var accessFactsIntact = activeGrants.All(item =>
                                        item.PublicId != Guid.Empty &&
                                        !string.IsNullOrWhiteSpace(item.ClientAddress) &&
                                        !string.IsNullOrWhiteSpace(item.PublicKey) &&
                                        !string.IsNullOrWhiteSpace(item.ServerPublicKey) &&
                                        !string.IsNullOrWhiteSpace(item.ProtectedServerPrivateKey)) &&
                                    (runtime.PublicUdpMapping is null ||
                                     runtime.PublicUdpMapping.Generation == runtime.Generation &&
                                     runtime.PublicUdpMapping.PublicUdpPort > 0 &&
                                     runtime.PublicUdpMapping.WorkerWireGuardPort > 0);
            var replay = teamLabRecovery.CanReplayInfrastructure(
                runtime, ticket, inventoryProvesDrift: true, routeFactsComplete, accessFactsIntact, now);
            if (!replay.Allowed)
            {
                events.Append(RuntimeOperationalEvents.Ticket(
                    ticket,
                    OperationalEventCodes.TeamLab.GuardedRebuildBlocked,
                    OperationalEventOutcome.Blocked,
                    "Infrastructure replay was blocked by recovery policy.",
                    OperationalEventSeverity.Warning,
                    detail: new Dictionary<string, object?>
                    {
                        ["generation"] = runtime.Generation,
                        ["stage"] = "infrastructure-recovery",
                        ["decision"] = "blocked",
                        ["reason"] = replay.Reason
                    }));
                PlatformTelemetry.RecordTeamLabRecovery("infrastructure-replay", "infrastructure", "blocked");
                return TicketInspection.FailClosed(
                    $"TeamLab infrastructure drift cannot be replayed: {replay.Reason}",
                    OperationalErrorCodes.RuntimeIdentityConflict);
            }
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.TeamLab.InfrastructureReplayQueued,
                OperationalEventOutcome.Recovered,
                "Infrastructure replay was queued with stable WireGuard identity.",
                detail: new Dictionary<string, object?>
                {
                    ["generation"] = runtime.Generation,
                    ["stage"] = "infrastructure-recovery",
                    ["decision"] = "replay",
                    ["reason"] = replay.Reason
                }));
            PlatformTelemetry.RecordTeamLabRecovery("infrastructure-replay", "infrastructure", "allowed");
            runtime.Status = TeamLabRuntimeStatus.Deploying;
            foreach (var shard in runtime.Shards.Where(item => item.Generation == runtime.Generation))
                shard.Status = TeamLabRuntimeStatus.Deploying;
            return TicketInspection.SafeReplay(replay.Reason);
        }

        if (assets.Any(item => item.ExecutionStage != TeamLabAssetExecutionStage.ServiceReady))
        {
            var resume = teamLabRecovery.CanResumeExistingGeneration(runtime, ticket, now);
            return resume.Allowed
                ? TicketInspection.SafeReplay("Agent inventory confirms native assets; persisted DAG stages can resume.")
                : TicketInspection.Deferred(resume.Reason, OperationalErrorCodes.RecoveryDeferred);
        }

        runtime.Status = TeamLabRuntimeStatus.Running;
        foreach (var shard in runtime.Shards.Where(item => item.Generation == runtime.Generation))
            shard.Status = TeamLabRuntimeStatus.Running;
        foreach (var asset in assets)
            asset.Status = TeamLabRuntimeStatus.Running;
        return TicketInspection.Completed("Agent inventory confirms every TeamLab runtime asset.");
    }

    private static IReadOnlyDictionary<string, object?> TeamLabRecoveryDetail(
        TeamLabRuntime runtime,
        TeamLabRuntimeAsset asset,
        string decision,
        string reason) => new Dictionary<string, object?>
    {
        ["generation"] = runtime.Generation,
        ["stage"] = "workload-recovery",
        ["decision"] = decision,
        ["assetKind"] = asset.Kind.ToString(),
        ["reason"] = reason
    };

    private async Task<TicketInspection> InspectTeamLabControlTicketAsync(
        DeploymentQueueTicket ticket,
        IReadOnlyDictionary<Guid, NodeInventory> inventories,
        CancellationToken token)
    {
        if (ticket.TeamLabRuntimeId is not { } runtimeId)
            return TicketInspection.FailClosed("TeamLab control ticket has no runtime identity.",
                OperationalErrorCodes.RuntimeIdentityConflict);
        var runtime = await context.TeamLabRuntimes
            .Include(item => item.Assets)
            .Include(item => item.Shards)
            .Include(item => item.Infrastructure).ThenInclude(item => item.Fragments)
            .Include(item => item.ObservationPoints)
            .Include(item => item.AccessGrants)
            .Include(item => item.VpnPeers)
            .Include(item => item.SecretEnvelopes)
            .Include(item => item.PublicUdpMapping)
            .Include(item => item.TrafficCaptureJobs).ThenInclude(item => item.Segments)
            .SingleOrDefaultAsync(item => item.Id == runtimeId, token);
        if (runtime is null)
            return TicketInspection.Completed("TeamLab runtime no longer exists; control operation is complete.");
        if (runtime.Generation != ticket.Generation)
            return TicketInspection.FailClosed("TeamLab control ticket generation is stale.",
                OperationalErrorCodes.RuntimeIdentityConflict);

        var generation = runtime.Generation;
        var assets = runtime.Assets.Where(item => item.Generation == generation &&
                                                  (item.Kind == TeamLabResourceKind.Docker ||
                                                   item.Kind == TeamLabResourceKind.Vm)).ToArray();
        var hasPhysicalResource = false;
        var shardNodeIds = runtime.Shards
            .Where(item => item.Generation == generation)
            .Select(item => item.WorkerNodeId)
            .Concat(assets.Where(item => item.WorkerNodeId.HasValue)
                .Select(item => item.WorkerNodeId!.Value))
            .Distinct()
            .ToArray();
        foreach (var nodeId in shardNodeIds)
        {
            if (!inventories.TryGetValue(nodeId, out var inventory) ||
                inventory.Availability is InventoryAvailability.Offline or InventoryAvailability.Unavailable or
                    InventoryAvailability.Unsupported)
                return TicketInspection.Deferred(inventory?.Error ?? "TeamLab shard inventory is unavailable.",
                    inventory?.Availability == InventoryAvailability.Unsupported
                        ? OperationalErrorCodes.AgentFeatureMissing
                        : OperationalErrorCodes.NodeOffline);
            if (inventory.Availability == InventoryAvailability.Local)
            {
                hasPhysicalResource |= assets.Any(item => item.WorkerNodeId == nodeId &&
                    item.Status != TeamLabRuntimeStatus.Destroyed);
                continue;
            }

            hasPhysicalResource |= inventory.Containers.Any(item =>
                                       item.RuntimeId == runtime.Id && item.Generation == generation) ||
                                   inventory.Vms.Any(item =>
                                       item.RuntimeId == runtime.Id && item.Generation == generation) ||
                                   inventory.TeamLabResources.Any(item =>
                                       item.RuntimeId == runtime.Id && item.Generation == generation);
            foreach (var asset in assets.Where(item => item.WorkerNodeId == nodeId &&
                                                        !string.IsNullOrWhiteSpace(item.RuntimeResourceId)))
            {
                var fact = ExpectedRuntimeFact.FromTeamLabAsset(asset);
                if (!inventory.Supports(fact.Kind))
                    return TicketInspection.Deferred(
                        "TeamLab shard cannot report the required runtime kind.",
                        OperationalErrorCodes.AgentFeatureMissing);
                var actual = inventory.Find(fact);
                if (actual is not null)
                {
                    if (actual.Generation != generation)
                        return TicketInspection.FailClosed(
                            "TeamLab control target generation has changed.",
                            OperationalErrorCodes.RuntimeIdentityConflict);
                    hasPhysicalResource = true;
                    continue;
                }
                if (inventory.FindIdentityConflict(fact) is not null)
                    return TicketInspection.FailClosed("TeamLab control target native identity has changed.",
                        OperationalErrorCodes.RuntimeIdentityConflict);
            }
        }

        if (hasPhysicalResource)
            return TicketInspection.SafeReplay(
                "Agent inventory confirms workload or control resources remain; cleanup replay is safe.");

        if (await TeamLabRuntimeCleanupService.HasPendingSideEffectsAsync(
                context, runtime, generation, token))
            return TicketInspection.SafeReplay(
                "Database cleanup side effects remain; cleanup replay is safe.");

        await TeamLabRuntimeCleanupService.FinalizeGenerationAsync(
            context, runtime, generation, markRuntimeDestroyed: true, token);
        return TicketInspection.Completed(
            "Agent and database facts confirm TeamLab cleanup is complete.");
    }

    private async Task<TicketResource?> ResolveTicketResourceAsync(
        DeploymentQueueTicket ticket,
        CancellationToken token)
    {
        if (ticket.Kind == DeploymentQueueKind.ChallengeTestContainer &&
            ticket.SubjectType == "runtime-container" &&
            Guid.TryParse(ticket.SubjectPublicId, out var containerId))
        {
            var maintained = await context.Containers.SingleOrDefaultAsync(item => item.Id == containerId, token);
            return maintained is null ? null : TicketResource.FromContainer(maintained);
        }

        GZCTF.Models.Data.Container? container = ticket.Kind switch
        {
            DeploymentQueueKind.GameContainer when ticket.GameId is { } gameId &&
                                                       ticket.OwnerTeamId is { } teamId &&
                                                       ticket.ChallengeId is { } challengeId =>
                await context.GameInstances
                    .Where(item => item.Participation.GameId == gameId &&
                                   item.Participation.TeamId == teamId && item.ChallengeId == challengeId)
                    .Select(item => item.Container)
                    .SingleOrDefaultAsync(token),
            DeploymentQueueKind.ExerciseContainer or DeploymentQueueKind.TrainingContainer
                when ticket.OwnerUserId is { } userId && ticket.ChallengeId is { } exerciseId =>
                await context.ExerciseInstances
                    .Where(item => item.UserId == userId && item.ExerciseId == exerciseId)
                    .Select(item => item.Container)
                    .SingleOrDefaultAsync(token),
            DeploymentQueueKind.AwdpContainer when ticket.AwdpServiceInstanceId is { } instanceId =>
                await context.AwdpServiceInstances
                    .Where(item => item.Id == instanceId)
                    .Select(item => item.Container)
                    .SingleOrDefaultAsync(token),
            DeploymentQueueKind.ChallengeTestContainer when ticket.SubjectType == "challenge-test-container" &&
                                                             ticket.GameId is { } testGameId &&
                                                             ticket.ChallengeId is { } testChallengeId =>
                await context.GameChallenges
                    .Where(item => item.GameId == testGameId && item.Id == testChallengeId)
                    .Select(item => item.TestContainer)
                    .SingleOrDefaultAsync(token),
            _ => null
        };
        if (container is not null)
            return TicketResource.FromContainer(container);

        if (ticket.Kind == DeploymentQueueKind.VirtualMachine && ticket.VmInstanceId is { } vmId)
        {
            var vm = await context.VmInstances.SingleOrDefaultAsync(item => item.Id == vmId, token);
            if (vm is not null)
                return TicketResource.FromVm(vm);
        }
        return null;
    }

    private void CompleteTicket(DeploymentQueueTicket ticket, TicketInspection inspection)
    {
        if (ticket.Operation == RuntimeOperationKind.Create)
            inspection.Resource?.MarkRunning(ticket.TargetNodeId);
        else if (ticket.Operation is RuntimeOperationKind.Stop or RuntimeOperationKind.Destroy)
            inspection.Resource?.MarkDestroyed();
        ticket.Status = DeploymentQueueTicketStatus.Succeeded;
        ticket.Stage = DeploymentStage.Ready;
        ticket.StageMessage = inspection.Message;
        ticket.ErrorMessage = null;
        ticket.ErrorCategory = null;
        ticket.ErrorCode = null;
        ticket.Retryable = false;
        ticket.CompletedAt = DateTimeOffset.UtcNow;
        ticket.ProtectedPayload = null;
        events.Append(RuntimeOperationalEvents.Ticket(
            ticket,
            OperationalEventCodes.Recovery.FactConfirmed,
            OperationalEventOutcome.Recovered,
            inspection.Message,
            detail: RecoveryDetail(ticket, "completed", inspection.ErrorCode)));
        PlatformTelemetry.RecordRecoveryDecision("completed", ticket.Kind.ToString());
    }

    private void ReplayTicket(DeploymentQueueTicket ticket, TicketInspection inspection)
    {
        ticket.Status = DeploymentQueueTicketStatus.Scheduled;
        ticket.Stage = DeploymentStage.NodeExecutionWaiting;
        ticket.StageMessage = inspection.Message;
        ticket.ErrorMessage = null;
        ticket.ErrorCategory = null;
        ticket.ErrorCode = null;
        ticket.Retryable = false;
        ticket.StartedAt = null;
        ticket.CompletedAt = null;
        ticket.AttemptCount++;
        events.Append(RuntimeOperationalEvents.Ticket(
            ticket,
            OperationalEventCodes.Recovery.TicketReplayed,
            OperationalEventOutcome.Recovered,
            inspection.Message,
            OperationalEventSeverity.Warning,
            detail: RecoveryDetail(ticket, "safe_replay", inspection.ErrorCode)));
        PlatformTelemetry.RecordRecoveryDecision("safe_replay", ticket.Kind.ToString());
    }

    /// <summary>
    /// How long a ticket may stay deferred waiting for an external condition (node back online,
    /// agent upgraded) before the condition is treated as permanent. Long enough to survive a node
    /// reboot, short enough that a retired node cannot strand a runtime indefinitely.
    /// </summary>
    private static readonly TimeSpan MaxDeferWindow = TimeSpan.FromHours(2);

    private async Task DeferTicketAsync(
        DeploymentQueueTicket ticket,
        TicketInspection inspection,
        Guid runId,
        DateTimeOffset now,
        CancellationToken token)
    {
        ticket.StageMessage = inspection.Message;
        ticket.ErrorMessage = inspection.Message;
        ticket.ErrorCategory = inspection.ErrorCode == OperationalErrorCodes.AgentFeatureMissing
            ? OperationalErrorCategory.AgentProtocol
            : OperationalErrorCategory.NodeUnavailable;
        ticket.ErrorCode = inspection.ErrorCode;
        ticket.Retryable = true;

        // A deferred ticket used to keep Status = Running, which holds the per-runtime concurrency
        // slot; every later destroy or reset for that runtime was then blocked from scheduling
        // forever, so its resources could never be reclaimed. Return it to the queue with a backoff
        // instead: the retry is preserved and the slot is freed. Beyond the window the condition is
        // treated as permanent rather than waited on indefinitely.
        var waitingSince = ticket.StartedAt ?? ticket.AssignedAt ?? ticket.CreatedAt;
        if (now - waitingSince > MaxDeferWindow)
        {
            FailTicket(ticket, inspection);
            await ReleaseTicketCapacityAsync(ticket, token);
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                OperationalEventCodes.Recovery.TicketDeferralExpired,
                OperationalEventOutcome.Failed,
                $"Ticket stayed deferred for over {MaxDeferWindow.TotalHours:0.#}h: {inspection.Message}",
                OperationalEventSeverity.Error,
                detail: RecoveryDetail(ticket, "defer_window_exceeded", inspection.ErrorCode)) with
            {
                CorrelationId = runId
            });
            PlatformTelemetry.RecordRecoveryDecision("defer_window_exceeded", ticket.Kind.ToString());
            return;
        }

        ticket.Status = DeploymentQueueTicketStatus.Pending;
        ticket.ClaimOwner = null;
        ticket.ClaimExpiresAt = null;
        ticket.AttemptCount++;
        ticket.NotBeforeAt = now.AddSeconds(Math.Min(300, 15 << Math.Min(5, ticket.AttemptCount)));

        var code = inspection.ErrorCode == OperationalErrorCodes.AgentFeatureMissing
            ? OperationalEventCodes.Recovery.InventoryUnsupported
            : OperationalEventCodes.Recovery.NodeUnavailable;
        var recentlyRecorded = await context.OperationalEvents.AsNoTracking().AnyAsync(item =>
            item.DeploymentTicketId == ticket.Id && item.EventCode == code &&
            item.OccurredAt >= now.AddMinutes(-15), token);
        if (!recentlyRecorded)
            events.Append(RuntimeOperationalEvents.Ticket(
                ticket,
                code,
                OperationalEventOutcome.Blocked,
                inspection.Message,
                OperationalEventSeverity.Warning,
                new OperationalError(
                    ticket.ErrorCategory.Value,
                    inspection.ErrorCode ?? OperationalErrorCodes.NodeOffline,
                    inspection.Message,
                    true,
                    WorkerNodeId: ticket.TargetNodeId,
                    Operation: "runtime.recover"),
                detail: RecoveryDetail(ticket, "deferred", inspection.ErrorCode)) with
            {
                CorrelationId = runId
            });
        PlatformTelemetry.RecordRecoveryDecision("deferred", ticket.Kind.ToString());
    }

    private void FailTicket(DeploymentQueueTicket ticket, TicketInspection inspection)
    {
        inspection.Resource?.MarkFailed();
        ticket.Status = DeploymentQueueTicketStatus.Failed;
        ticket.Stage = DeploymentStage.Failed;
        ticket.ErrorMessage = inspection.Message;
        ticket.StageMessage = inspection.Message;
        ticket.CompletedAt = DateTimeOffset.UtcNow;
        ticket.ProtectedPayload = null;
        ticket.ErrorCategory = OperationalErrorCategory.Conflict;
        ticket.ErrorCode = inspection.ErrorCode ?? OperationalErrorCodes.RuntimeIdentityConflict;
        ticket.Retryable = false;
        events.Append(RuntimeOperationalEvents.Ticket(
            ticket,
            ticket.ErrorCode == OperationalErrorCodes.RuntimeIdentityConflict
                ? OperationalEventCodes.Recovery.IdentityConflict
                : OperationalEventCodes.Runtime.ExecutionFailedClosed,
            OperationalEventOutcome.Failed,
            inspection.Message,
            OperationalEventSeverity.Warning,
            new OperationalError(
                ticket.ErrorCategory.Value,
                ticket.ErrorCode,
                inspection.Message,
                false,
                WorkerNodeId: ticket.TargetNodeId,
                Operation: "runtime.recover"),
            detail: RecoveryDetail(ticket, "failed_closed", ticket.ErrorCode)));
        PlatformTelemetry.RecordRecoveryDecision("failed_closed", ticket.Kind.ToString());
    }

    private async Task ConfirmTicketCapacityAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.TeamLabRuntimeId is { } runtimeId)
        {
            foreach (var slot in await TeamLabCapacityFacts.LoadAsync(context, runtimeId, token))
                await capacity.ConfirmAsync(ticket.Id, slot.WorkerNodeId, token);
            return;
        }
        if (ticket.TargetNodeId is { } nodeId)
            await capacity.ConfirmAsync(ticket.Id, nodeId, token);
    }

    private async Task ReleaseTicketCapacityAsync(DeploymentQueueTicket ticket, CancellationToken token)
    {
        if (ticket.Kind == DeploymentQueueKind.TeamLabRuntime && ticket.TeamLabRuntimeId is { } runtimeId)
        {
            foreach (var slot in await TeamLabCapacityFacts.LoadAsync(context, runtimeId, token))
                await capacity.ReleaseAsync(ticket.Id, slot.WorkerNodeId, token);
            return;
        }
        if (ticket.TargetNodeId is { } nodeId)
            await capacity.ReleaseAsync(ticket.Id, nodeId, token);
    }

    private static IReadOnlyDictionary<string, object?> RecoveryDetail(
        DeploymentQueueTicket ticket,
        string decision,
        string? reasonCode) => new Dictionary<string, object?>
    {
        ["workload"] = ticket.Kind.ToString(),
        ["operation"] = ticket.Operation.ToString(),
        ["stage"] = ticket.Stage.ToString(),
        ["attempt"] = ticket.AttemptCount,
        ["generation"] = ticket.Generation,
        ["decision"] = decision,
        ["reasonCode"] = reasonCode ?? decision
    };

    private static bool IsActive(RuntimeFactKind kind, string state) => kind switch
    {
        RuntimeFactKind.Docker => state.Equals("running", StringComparison.OrdinalIgnoreCase),
        RuntimeFactKind.Vm => state.Equals("running", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static RuntimeFactKind ResourceKind(DeploymentQueueTicket ticket) =>
        ticket.Kind == DeploymentQueueKind.VirtualMachine ? RuntimeFactKind.Vm : RuntimeFactKind.Docker;

    private enum RuntimeFactKind : byte { Docker, Vm }
    private enum InventoryAvailability : byte { Available, Local, Offline, Unsupported, Unavailable }
    private enum TicketRecoveryDecision : byte { Completed, SafeReplay, Deferred, FailClosed }

    private sealed record NodeInventory(
        Guid WorkerNodeId,
        InventoryAvailability Availability,
        bool DockerSupported,
        bool KvmSupported,
        IReadOnlyList<AgentRuntimeInventoryResource> Containers,
        IReadOnlyList<AgentRuntimeInventoryResource> Vms,
        IReadOnlyList<AgentRuntimeInventoryResource> TeamLabResources,
        string? Error)
    {
        public bool Supports(RuntimeFactKind kind) => kind switch
        {
            RuntimeFactKind.Docker => DockerSupported,
            RuntimeFactKind.Vm => KvmSupported,
            _ => false
        };

        public AgentRuntimeInventoryResource? Find(ExpectedRuntimeFact fact) => fact.Kind switch
        {
            RuntimeFactKind.Docker => Containers.FirstOrDefault(item =>
                item.NativeId.Equals(fact.NativeId, StringComparison.Ordinal)),
            RuntimeFactKind.Vm when !string.IsNullOrWhiteSpace(fact.NativeId) => Vms.FirstOrDefault(item =>
                item.NativeId.Equals(fact.NativeId, StringComparison.OrdinalIgnoreCase)),
            RuntimeFactKind.Vm => Vms.FirstOrDefault(item =>
                item.StableName.Equals(fact.StableName, StringComparison.Ordinal)),
            _ => null
        };

        public AgentRuntimeInventoryResource? FindIdentityConflict(ExpectedRuntimeFact fact) =>
            fact.Kind == RuntimeFactKind.Vm && !string.IsNullOrWhiteSpace(fact.NativeId)
                ? Vms.FirstOrDefault(item => item.StableName.Equals(fact.StableName, StringComparison.Ordinal) &&
                                             !item.NativeId.Equals(fact.NativeId, StringComparison.OrdinalIgnoreCase))
                : null;

        public AgentRuntimeInventoryResource? Find(TicketResource fact) => fact.Kind switch
        {
            RuntimeFactKind.Docker => Containers.FirstOrDefault(item =>
                item.NativeId.Equals(fact.NativeId, StringComparison.Ordinal)),
            RuntimeFactKind.Vm when !string.IsNullOrWhiteSpace(fact.NativeId) => Vms.FirstOrDefault(item =>
                item.NativeId.Equals(fact.NativeId, StringComparison.OrdinalIgnoreCase)),
            RuntimeFactKind.Vm => Vms.FirstOrDefault(item =>
                item.StableName.Equals(fact.StableName, StringComparison.Ordinal)),
            _ => null
        };

        public AgentRuntimeInventoryResource? FindIdentityConflict(TicketResource fact) =>
            fact.Kind == RuntimeFactKind.Vm && !string.IsNullOrWhiteSpace(fact.NativeId)
                ? Vms.FirstOrDefault(item => item.StableName.Equals(fact.StableName, StringComparison.Ordinal) &&
                                             !item.NativeId.Equals(fact.NativeId, StringComparison.OrdinalIgnoreCase))
                : null;

        public IEnumerable<ActualRuntimeFact> AllResources()
        {
            foreach (var item in Containers)
                yield return new ActualRuntimeFact(WorkerNodeId, RuntimeFactKind.Docker, item);
            foreach (var item in Vms)
                yield return new ActualRuntimeFact(WorkerNodeId, RuntimeFactKind.Vm, item);
        }
    }

    private sealed record ExpectedTeamLabControlFact(
        Guid WorkerNodeId,
        int RuntimeId,
        int Generation,
        string ResourceKind,
        string StableName,
        string? NativeId,
        string? DesiredStateDigest)
    {
        public string IdentityKey =>
            $"{WorkerNodeId:D}|{RuntimeId}|{Generation}|{ResourceKind}|{StableName}";

        public static string IdentityKeyFor(Guid workerNodeId, AgentRuntimeInventoryResource resource) =>
            $"{workerNodeId:D}|{resource.RuntimeId}|{resource.Generation}|" +
            $"{resource.ResourceKind}|{resource.StableName}";
    }

    private sealed record TeamLabControlReconciliation(
        int Matched,
        int Missing,
        int Conflicts,
        int Orphans,
        int Deferred);

    private static string NormalizeDigest(string value) =>
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? value[7..].ToLowerInvariant()
            : value.ToLowerInvariant();

    private sealed record ActualRuntimeFact(
        Guid WorkerNodeId,
        RuntimeFactKind Kind,
        AgentRuntimeInventoryResource Resource)
    {
        public string IdentityKey => Kind == RuntimeFactKind.Docker
            ? $"{WorkerNodeId:D}|docker|{Resource.NativeId}|g{Resource.Generation}"
            : $"{WorkerNodeId:D}|vm|{Resource.StableName}|g{Resource.Generation}";
        public string EventResourceType => Kind == RuntimeFactKind.Docker ? "container" : "vm";
        public string EventResourceId =>
            $"{(Kind == RuntimeFactKind.Docker ? Resource.NativeId : Resource.StableName)}@g{Resource.Generation}";
    }

    private sealed record ExpectedRuntimeFact(
        Guid WorkerNodeId,
        RuntimeFactKind Kind,
        string NativeId,
        string StableName,
        int? Generation,
        string SubjectType,
        string SubjectId,
        string? DisplayName,
        string PreviousStatus,
        string CorrectedStatus,
        GZCTF.Models.Data.Container? Container,
        VmInstance? Vm,
        TeamLabRuntimeAsset? TeamLabAsset)
    {
        public string IdentityKey => Kind == RuntimeFactKind.Docker
            ? $"{WorkerNodeId:D}|docker|{NativeId}|g{Generation}"
            : $"{WorkerNodeId:D}|vm|{StableName}|g{Generation}";
        public string EventResourceType => Kind == RuntimeFactKind.Docker ? "container" : "vm";
        public string EventResourceId => Kind == RuntimeFactKind.Docker ? NativeId : StableName;

        public static ExpectedRuntimeFact FromContainer(GZCTF.Models.Data.Container item) => new(
            item.NodeId!.Value,
            RuntimeFactKind.Docker,
            item.ContainerId,
            string.Empty,
            item.RuntimeGeneration,
            "container",
            item.Id.ToString(),
            item.Image,
            item.Status.ToString(),
            ContainerStatus.Destroyed.ToString(),
            item,
            null,
            null);

        public static ExpectedRuntimeFact FromVm(VmInstance item) => new(
            item.NodeId!.Value,
            RuntimeFactKind.Vm,
            item.RuntimeNativeId ?? string.Empty,
            item.VmName,
            item.RuntimeGeneration,
            "vm-instance",
            item.Id.ToString(),
            item.VmName,
            item.Status.ToString(),
            VmInstanceStatus.Error.ToString(),
            null,
            item,
            null);

        public static ExpectedRuntimeFact FromTeamLabAsset(TeamLabRuntimeAsset item) => new(
            item.WorkerNodeId!.Value,
            item.Kind == TeamLabResourceKind.Docker ? RuntimeFactKind.Docker : RuntimeFactKind.Vm,
            item.Kind == TeamLabResourceKind.Docker ? item.RuntimeResourceId! : string.Empty,
            item.Kind == TeamLabResourceKind.Vm ? item.RuntimeResourceId! : string.Empty,
            item.Generation,
            "teamlab-asset",
            item.Id.ToString(),
            item.Name,
            item.Status.ToString(),
            TeamLabRuntimeStatus.Failed.ToString(),
            null,
            null,
            item);

        public void BackfillNativeIdentity(AgentRuntimeInventoryResource actual)
        {
            if (Vm is not null && string.IsNullOrWhiteSpace(Vm.RuntimeNativeId))
                Vm.RuntimeNativeId = actual.NativeId;
        }
    }

    private sealed record TicketResource(
        RuntimeFactKind Kind,
        string NativeId,
        string StableName,
        int Generation,
        Guid? WorkerNodeId,
        bool IsDatabaseRunning,
        bool IsDatabaseDestroyed,
        GZCTF.Models.Data.Container? Container,
        VmInstance? Vm)
    {
        public static TicketResource FromContainer(GZCTF.Models.Data.Container item) => new(
            RuntimeFactKind.Docker,
            item.ContainerId,
            string.Empty,
            item.RuntimeGeneration,
            item.NodeId,
            item.Status == ContainerStatus.Running,
            item.Status == ContainerStatus.Destroyed,
            item,
            null);

        public static TicketResource FromVm(VmInstance item) => new(
            RuntimeFactKind.Vm,
            item.RuntimeNativeId ?? string.Empty,
            item.VmName,
            item.RuntimeGeneration,
            item.NodeId,
            item.Status == VmInstanceStatus.Running,
            item.Status == VmInstanceStatus.Destroyed,
            null,
            item);

        public void BackfillNativeIdentity(AgentRuntimeInventoryResource actual)
        {
            if (Vm is not null && string.IsNullOrWhiteSpace(Vm.RuntimeNativeId))
                Vm.RuntimeNativeId = actual.NativeId;
        }

        public void MarkRunning(Guid? nodeId)
        {
            if (Container is not null)
            {
                Container.Status = ContainerStatus.Running;
                Container.NodeId ??= nodeId;
            }
            if (Vm is not null)
            {
                Vm.Status = VmInstanceStatus.Running;
                Vm.NodeId ??= nodeId;
            }
        }

        public void MarkFailed()
        {
            if (Container is not null)
                Container.Status = ContainerStatus.Destroyed;
            if (Vm is not null && Vm.Status != VmInstanceStatus.Destroyed)
                Vm.Status = VmInstanceStatus.Error;
        }

        public void MarkDestroyed()
        {
            if (Container is not null)
                Container.Status = ContainerStatus.Destroyed;
            if (Vm is not null)
            {
                Vm.Status = VmInstanceStatus.Destroyed;
                Vm.DestroyedAt ??= DateTimeOffset.UtcNow;
            }
        }
    }

    private sealed record TicketInspection(
        TicketRecoveryDecision Decision,
        string Message,
        string? ErrorCode = null,
        TicketResource? Resource = null)
    {
        public static TicketInspection Completed(string message, TicketResource? resource = null) =>
            new(TicketRecoveryDecision.Completed, message, Resource: resource);
        public static TicketInspection SafeReplay(string message, TicketResource? resource = null) =>
            new(TicketRecoveryDecision.SafeReplay, message, Resource: resource);
        public static TicketInspection Deferred(string message, string errorCode) =>
            new(TicketRecoveryDecision.Deferred, message, errorCode);
        public static TicketInspection FailClosed(string message, string errorCode,
            TicketResource? resource = null) =>
            new(TicketRecoveryDecision.FailClosed, message, errorCode, resource);
    }
}
