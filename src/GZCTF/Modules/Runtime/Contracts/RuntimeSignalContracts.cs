using GZCTF.GuestControl.Contracts;

namespace GZCTF.Modules.Runtime.Contracts;

public enum AgentRuntimeSignalStage : byte
{
    ResourceCreated = 0,
    NetworkReady = 1,
    DomainRunning = 2,
    GuestReady = 3,
    BootstrapRunning = 4,
    BootstrapCompleted = 5,
    Rebooting = 6,
    GuestReadyAfterReboot = 7,
    HealthReady = 8,
    ManagementLinkReady = GuestRuntimeSignalStageCodes.ManagementLinkReady,
    GuestEnrolled = GuestRuntimeSignalStageCodes.GuestEnrolled,
    NetworkApplied = GuestRuntimeSignalStageCodes.NetworkApplied,
    GuestReenrolledAfterBoot = GuestRuntimeSignalStageCodes.GuestReenrolledAfterBoot,
    ObservationReady = GuestRuntimeSignalStageCodes.ObservationReady,
    Failed = byte.MaxValue
}

public static class GuestRuntimeSignalMapper
{
    public static AgentRuntimeSignalStage ToRuntimeSignalStage(this GuestLifecycleStage stage) => stage switch
    {
        GuestLifecycleStage.ManagementLinkReady => AgentRuntimeSignalStage.ManagementLinkReady,
        GuestLifecycleStage.GuestEnrolled => AgentRuntimeSignalStage.GuestEnrolled,
        GuestLifecycleStage.NetworkApplied => AgentRuntimeSignalStage.NetworkApplied,
        GuestLifecycleStage.BootstrapRunning => AgentRuntimeSignalStage.BootstrapRunning,
        GuestLifecycleStage.RebootRequested => AgentRuntimeSignalStage.Rebooting,
        GuestLifecycleStage.GuestReenrolledAfterBoot => AgentRuntimeSignalStage.GuestReenrolledAfterBoot,
        GuestLifecycleStage.BootstrapCompleted => AgentRuntimeSignalStage.BootstrapCompleted,
        GuestLifecycleStage.ServiceHealthReady => AgentRuntimeSignalStage.HealthReady,
        GuestLifecycleStage.ObservationReady => AgentRuntimeSignalStage.ObservationReady,
        GuestLifecycleStage.Failed => AgentRuntimeSignalStage.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };
}

public enum AgentRuntimeSignalOutcome : byte
{
    Started = 0,
    Ready = 1,
    Failed = 2
}

public sealed record AgentRuntimeSignalModel(
    Guid OperationId,
    int RuntimeId,
    int Generation,
    string ResourceKind,
    string ResourceId,
    long Sequence,
    AgentRuntimeSignalStage Stage,
    AgentRuntimeSignalOutcome Outcome,
    DateTimeOffset ObservedAt,
    string? ErrorCode = null,
    bool Retryable = false,
    IReadOnlyDictionary<string, string>? Facts = null);

public sealed record AgentRuntimeSignalIngestResult(
    bool Accepted,
    bool Duplicate,
    bool Stale,
    long Sequence);
