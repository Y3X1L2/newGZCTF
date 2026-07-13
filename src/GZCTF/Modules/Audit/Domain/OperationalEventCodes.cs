using System.Reflection;

namespace GZCTF.Modules.Audit.Domain;

public static class OperationalEventCodes
{
    public static class Runtime
    {
        public const string TicketEnqueued = "runtime.ticket.enqueued";
        public const string TicketDuplicate = "runtime.ticket.duplicate";
        public const string TicketCancelled = "runtime.ticket.cancelled";
        public const string AdmissionBlocked = "runtime.admission.blocked";
        public const string AdmissionAccepted = "runtime.admission.accepted";
        public const string SchedulingStarted = "runtime.scheduling.started";
        public const string SchedulingBlocked = "runtime.scheduling.blocked";
        public const string SchedulingAssigned = "runtime.scheduling.assigned";
        public const string ExecutionStarted = "runtime.execution.started";
        public const string ExecutionSucceeded = "runtime.execution.succeeded";
        public const string ExecutionFailed = "runtime.execution.failed";
        public const string ExecutionReplayQueued = "runtime.execution.replay_queued";
        public const string ExecutionClaimRecovered = "runtime.execution.claim_recovered";
        public const string ExecutionFailedClosed = "runtime.execution.failed_closed";
        public const string ControlExtendStarted = "runtime.control.extend.started";
        public const string ControlStopStarted = "runtime.control.stop.started";
        public const string ControlResetStarted = "runtime.control.reset.started";
        public const string ControlDestroyStarted = "runtime.control.destroy.started";
        public const string RollbackStarted = "runtime.rollback.started";
        public const string RollbackSucceeded = "runtime.rollback.succeeded";
        public const string RollbackFailed = "runtime.rollback.failed";
        public const string SnapshotImported = "runtime.snapshot.imported";
    }

    public static class Capacity
    {
        public const string Reserved = "runtime.capacity.reserved";
        public const string Blocked = "runtime.capacity.blocked";
        public const string Confirmed = "runtime.capacity.confirmed";
        public const string Released = "runtime.capacity.released";
        public const string Expired = "runtime.capacity.expired";
        public const string Reconciled = "runtime.capacity.reconciled";
        public const string Conflict = "runtime.capacity.conflict";
    }

    public static class Image
    {
        public const string DistributionQueued = "image.distribution.queued";
        public const string DistributionClaimed = "image.distribution.claimed";
        public const string TransferStarted = "image.transfer.started";
        public const string TransferSucceeded = "image.transfer.succeeded";
        public const string VerifyStarted = "image.verify.started";
        public const string VerifySucceeded = "image.verify.succeeded";
        public const string DistributionReady = "image.distribution.ready";
        public const string DistributionRetryQueued = "image.distribution.retry_queued";
        public const string DistributionFailed = "image.distribution.failed";
        public const string CleanupQueued = "image.cleanup.queued";
        public const string CleanupStarted = "image.cleanup.started";
        public const string CleanupSucceeded = "image.cleanup.succeeded";
        public const string CleanupFailed = "image.cleanup.failed";
        public const string ReferenceAttached = "image.reference.attached";
        public const string ReferenceReleased = "image.reference.released";
        public const string ReconcileCorrected = "image.reconcile.corrected";
        public const string SnapshotImported = "image.snapshot.imported";
    }

    public static class Node
    {
        public const string RegistrationStarted = "node.registration.started";
        public const string RegistrationSucceeded = "node.registration.succeeded";
        public const string RegistrationFailed = "node.registration.failed";
        public const string Deregistered = "node.deregistered";
        public const string Online = "node.online";
        public const string Offline = "node.offline";
        public const string CapabilityChanged = "node.capability.changed";
        public const string SchedulableEnabled = "node.schedulable.enabled";
        public const string SchedulableDisabled = "node.schedulable.disabled";
        public const string HealthDegraded = "node.health.degraded";
        public const string HealthRecovered = "node.health.recovered";
    }

    public static class Agent
    {
        public const string SyncStarted = "agent.sync.started";
        public const string SyncSucceeded = "agent.sync.succeeded";
        public const string SyncFailed = "agent.sync.failed";
        public const string CallFailed = "agent.call.failed";
        public const string InventoryUnavailable = "agent.inventory.unavailable";
    }

    public static class Container
    {
        public const string CreateStarted = "container.create.started";
        public const string CreateSucceeded = "container.create.succeeded";
        public const string CreateFailed = "container.create.failed";
        public const string StopSucceeded = "container.stop.succeeded";
        public const string DestroySucceeded = "container.destroy.succeeded";
        public const string DestroyFailed = "container.destroy.failed";
    }

    public static class Vm
    {
        public const string CreateStarted = "vm.create.started";
        public const string CreateSucceeded = "vm.create.succeeded";
        public const string CreateFailed = "vm.create.failed";
        public const string BootProbeStarted = "vm.boot.probe_started";
        public const string BootReady = "vm.boot.ready";
        public const string BootFailed = "vm.boot.failed";
        public const string StopSucceeded = "vm.stop.succeeded";
        public const string DestroySucceeded = "vm.destroy.succeeded";
        public const string DestroyFailed = "vm.destroy.failed";
        public const string AccessOpened = "vm.access.opened";
        public const string AccessFailed = "vm.access.failed";
    }

    public static class TeamLab
    {
        public const string PlanStarted = "teamlab.plan.started";
        public const string PlanSucceeded = "teamlab.plan.succeeded";
        public const string PlacementSucceeded = "teamlab.placement.succeeded";
        public const string DeployStarted = "teamlab.deploy.started";
        public const string NetworkApplied = "teamlab.network.applied";
        public const string AssetCreated = "teamlab.asset.created";
        public const string RouteApplied = "teamlab.route.applied";
        public const string ProbeSucceeded = "teamlab.probe.succeeded";
        public const string Ready = "teamlab.ready";
        public const string DeployFailed = "teamlab.deploy.failed";
        public const string ResetQueued = "teamlab.reset.queued";
        public const string ResetStarted = "teamlab.reset.started";
        public const string ResetSucceeded = "teamlab.reset.succeeded";
        public const string DestroyStarted = "teamlab.destroy.started";
        public const string DestroySucceeded = "teamlab.destroy.succeeded";
        public const string DestroyFailed = "teamlab.destroy.failed";
        public const string CaptureStarted = "teamlab.capture.started";
        public const string CaptureStopped = "teamlab.capture.stopped";
        public const string CaptureFailed = "teamlab.capture.failed";
        public const string SnapshotImported = "teamlab.snapshot.imported";
    }

    public static class Recovery
    {
        public const string RunStarted = "recovery.run.started";
        public const string RunSucceeded = "recovery.run.succeeded";
        public const string RunFailed = "recovery.run.failed";
        public const string FactConfirmed = "recovery.fact.confirmed";
        public const string ResourceMissing = "recovery.resource.missing";
        public const string IdentityConflict = "recovery.identity.conflict";
        public const string TicketReplayed = "recovery.ticket.replayed";
        public const string StateCorrected = "recovery.state.corrected";
        public const string NodeUnavailable = "recovery.node.unavailable";
        public const string InventoryUnsupported = "recovery.inventory.unsupported";
        public const string OrphanObserved = "recovery.orphan.observed";
    }

    public static class Audit
    {
        public const string AdminMutationSucceeded = "audit.admin.mutation.succeeded";
        public const string AdminMutationFailed = "audit.admin.mutation.failed";
        public const string ExternalRequest = "audit.external.request";
        public const string SensitiveDownload = "audit.sensitive.download";
        public const string VmAccessOpened = "audit.access.vm_opened";
        public const string PcapDownloaded = "audit.access.pcap_downloaded";
    }

    private static readonly HashSet<string> KnownCodes = typeof(OperationalEventCodes)
        .GetNestedTypes(BindingFlags.Public)
        .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsDefined(string eventCode) => KnownCodes.Contains(eventCode);

    public static IReadOnlySet<string> All => KnownCodes;
}
