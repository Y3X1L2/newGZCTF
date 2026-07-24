using System.Diagnostics;
using System.Diagnostics.Metrics;
using GZCTF.Extensions;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Infrastructure.Telemetry;

public static class PlatformTelemetry
{
    public const string RuntimeMeterName = "GZCTF.Runtime";
    public const string AgentClientMeterName = "GZCTF.AgentClient";
    public const string OperationsMeterName = "GZCTF.Operations";
    public const string TeamLabMeterName = "GZCTF.TeamLab";
    public const string RuntimeActivitySourceName = "GZCTF.Runtime";
    public const string AgentClientActivitySourceName = "GZCTF.AgentClient";
    public const string ImageActivitySourceName = "GZCTF.ImageDistribution";
    public const string TeamLabActivitySourceName = "GZCTF.TeamLab";
    public const string CacheActivitySourceName = "GZCTF.Cache";

    public static readonly ActivitySource RuntimeActivitySource = new(RuntimeActivitySourceName);
    public static readonly ActivitySource AgentClientActivitySource = new(AgentClientActivitySourceName);
    public static readonly ActivitySource ImageActivitySource = new(ImageActivitySourceName);
    public static readonly ActivitySource TeamLabActivitySource = new(TeamLabActivitySourceName);

    private static readonly Meter RuntimeMeter = new(RuntimeMeterName);
    private static readonly Meter AgentClientMeter = new(AgentClientMeterName);
    private static readonly Meter OperationsMeter = new(OperationsMeterName);
    private static readonly Meter TeamLabMeter = new(TeamLabMeterName);
    private static readonly Counter<long> AgentCalls =
        AgentClientMeter.CreateCounter<long>("gzctf_agent_calls_total");
    private static readonly Counter<long> AgentFailures =
        AgentClientMeter.CreateCounter<long>("gzctf_agent_call_failures_total");
    private static readonly Histogram<double> AgentDuration =
        AgentClientMeter.CreateHistogram<double>("gzctf_agent_call_duration_seconds", "s");
    private static readonly Counter<long> EventWrites =
        OperationsMeter.CreateCounter<long>("gzctf_operational_events_total");
    private static readonly Counter<long> RuntimeTransitions =
        RuntimeMeter.CreateCounter<long>("gzctf_runtime_transitions_total");
    private static readonly Counter<long> RecoveryDecisions =
        RuntimeMeter.CreateCounter<long>("gzctf_runtime_recovery_decisions_total");
    private static readonly Histogram<double> RuntimeDuration =
        RuntimeMeter.CreateHistogram<double>("gzctf_runtime_stage_duration_seconds", "s");
    private static readonly Counter<long> TeamLabLifecycle =
        TeamLabMeter.CreateCounter<long>("gzctf_teamlab_lifecycle_total");
    private static readonly Counter<long> TeamLabInfrastructure =
        TeamLabMeter.CreateCounter<long>("gzctf_teamlab_infrastructure_total");
    private static readonly Counter<long> TeamLabObservations =
        TeamLabMeter.CreateCounter<long>("gzctf_teamlab_observations_total");
    private static readonly Counter<long> TeamLabCaptures =
        TeamLabMeter.CreateCounter<long>("gzctf_teamlab_capture_actions_total");
    private static readonly Counter<long> TeamLabRecovery =
        TeamLabMeter.CreateCounter<long>("gzctf_teamlab_recovery_decisions_total");
    private static Measurement<long>[] _queueDepth = [];
    private static Measurement<long>[] _nodeSummary = [];

    static PlatformTelemetry()
    {
        RuntimeMeter.CreateObservableGauge("gzctf_runtime_queue_depth",
            () => Volatile.Read(ref _queueDepth));
        RuntimeMeter.CreateObservableGauge("gzctf_worker_nodes",
            () => Volatile.Read(ref _nodeSummary));
        OperationsMeter.CreateObservableGauge("gzctf_system_log_buffered",
            () => DatabaseLogSinkMetrics.Buffered);
        OperationsMeter.CreateObservableCounter("gzctf_system_log_dropped_total",
            () => DatabaseLogSinkMetrics.Dropped);
        OperationsMeter.CreateObservableCounter("gzctf_system_log_flush_failures_total",
            () => DatabaseLogSinkMetrics.FlushFailures);
    }

    public static void RecordAgentCall(
        string operation,
        bool success,
        TimeSpan duration,
        OperationalErrorCategory? errorCategory = null)
    {
        var result = success ? "success" : "failure";
        AgentCalls.Add(1, new TagList
        {
            { "operation", operation },
            { "result", result }
        });
        AgentDuration.Record(duration.TotalSeconds, new TagList
        {
            { "operation", operation },
            { "result", result }
        });
        if (!success)
            AgentFailures.Add(1, new TagList
            {
                { "operation", operation },
                { "error.category", errorCategory?.ToString() ?? "Unknown" }
            });
    }

    public static void RecordEvent(string eventCode, OperationalEventOutcome outcome) =>
        EventWrites.Add(1, new TagList
        {
            { "event.code", eventCode },
            { "outcome", outcome.ToString() }
        });

    public static void RecordRuntimeTransition(string workload, string stage, string outcome) =>
        RuntimeTransitions.Add(1, new TagList
        {
            { "workload", workload },
            { "stage", stage },
            { "outcome", outcome }
        });

    public static void RecordRuntimeDuration(string workload, string stage, TimeSpan duration) =>
        RuntimeDuration.Record(duration.TotalSeconds, new TagList
        {
            { "workload", workload },
            { "stage", stage }
        });

    public static void RecordRecoveryDecision(string decision, string workload) =>
        RecoveryDecisions.Add(1, new TagList
        {
            { "decision", decision },
            { "workload", workload }
        });

    public static void RecordTeamLabLifecycle(
        string stage,
        OperationalEventOutcome outcome,
        OperationalErrorCategory? errorCategory = null) =>
        TeamLabLifecycle.Add(1, new TagList
        {
            { "stage", stage },
            { "result", outcome.ToString() },
            { "error.category", errorCategory?.ToString() ?? "None" }
        });

    public static void RecordTeamLabInfrastructure(string result, string infrastructureKind) =>
        TeamLabInfrastructure.Add(1, new TagList
        {
            { "result", result },
            { "infrastructure.kind", infrastructureKind }
        });

    public static void RecordTeamLabObservation(string result, string evidenceKind, long count = 1) =>
        TeamLabObservations.Add(count, new TagList
        {
            { "result", result },
            { "evidence.kind", evidenceKind }
        });

    public static void RecordTeamLabCapture(string action, string scope, string result) =>
        TeamLabCaptures.Add(1, new TagList
        {
            { "action", action },
            { "capture.scope", scope },
            { "result", result }
        });

    public static void RecordTeamLabRecovery(string decision, string assetKind, string result) =>
        TeamLabRecovery.Add(1, new TagList
        {
            { "decision", decision },
            { "asset.kind", assetKind },
            { "result", result }
        });

    public static void UpdateQueueDepth(IEnumerable<Measurement<long>> measurements) =>
        Volatile.Write(ref _queueDepth, measurements.ToArray());

    public static void UpdateNodeSummary(IEnumerable<Measurement<long>> measurements) =>
        Volatile.Write(ref _nodeSummary, measurements.ToArray());
}
