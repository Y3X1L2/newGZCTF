using Microsoft.Extensions.Options;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed class DataRetentionPolicyCatalog
{
    private readonly IReadOnlyDictionary<string, DataSetRetentionPolicy> _policies;

    public DataRetentionPolicyCatalog(IOptions<DataRetentionOptions> options)
    {
        var value = options.Value;
        var policies = new[]
        {
            OwnerManaged("participation", "Ctf"),
            OwnerManaged("submission", "Ctf"),
            OwnerManaged("training-progress", "Training"),
            OwnerManaged("theory-answer", "Theory"),
            OwnerManaged("awdp-competition", "Awdp"),
            Policy("system-log", "Audit", DataLifecycleMode.PartitionedRaw,
                value.SystemLogDays, 180, PartitionGrain.Month, value.DeleteBatchSize,
                "aggregate-hourly-then-drop-partition"),
            Policy("operational-event", "Audit", DataLifecycleMode.TerminalHistory,
                value.OperationalEventDays, null, PartitionGrain.None, value.DeleteBatchSize,
                "delete-batched"),
            Policy("teamlab-flow", "TeamLab", DataLifecycleMode.PartitionedRaw,
                value.TeamLabFlowDays, value.TeamLabFlowAggregateDays, PartitionGrain.Day,
                value.DeleteBatchSize, "aggregate-five-minute-then-drop-partition"),
            Policy("teamlab-flow-aggregate", "TeamLab", DataLifecycleMode.Aggregate,
                value.TeamLabFlowAggregateDays, null, PartitionGrain.None, value.DeleteBatchSize,
                "delete-batched"),
            Policy("deployment-ticket", "Runtime", DataLifecycleMode.TerminalHistory,
                value.DeploymentTicketDays, 365, PartitionGrain.None, value.DeleteBatchSize,
                "delete-terminal-batched"),
            Policy("api-operation", "Audit", DataLifecycleMode.TerminalHistory,
                value.ApiOperationDays, null, PartitionGrain.None, value.DeleteBatchSize,
                "delete-terminal-batched"),
            Policy("teamlab-event", "TeamLab", DataLifecycleMode.TerminalHistory,
                value.TeamLabEventDays, null, PartitionGrain.None, value.DeleteBatchSize,
                "delete-terminal-runtime-batched"),
            Policy("governance-run", "Audit", DataLifecycleMode.TerminalHistory,
                value.GovernanceRunDays, null, PartitionGrain.None, value.DeleteBatchSize,
                "delete-terminal-batched"),
            Policy("worker-node-metric", "Runtime", DataLifecycleMode.TerminalHistory,
                value.WorkerNodeMetricDays, null, PartitionGrain.None, value.DeleteBatchSize,
                "delete-batched")
        };

        _policies = policies.ToDictionary(policy => policy.Name, StringComparer.Ordinal);
        if (_policies.Count != policies.Length)
            throw new InvalidOperationException("Data retention policy names must be unique.");
    }

    public IReadOnlyCollection<DataSetRetentionPolicy> Policies => _policies.Values.ToArray();

    public DataSetRetentionPolicy GetRequired(string name) =>
        _policies.TryGetValue(name, out var policy)
            ? policy
            : throw new KeyNotFoundException($"Data set '{name}' is not registered for automatic governance.");

    private static DataSetRetentionPolicy Policy(
        string name,
        string owner,
        DataLifecycleMode mode,
        int rawDays,
        int? aggregateDays,
        PartitionGrain grain,
        int batchSize,
        string action) =>
        new(name, owner, mode, TimeSpan.FromDays(rawDays),
            aggregateDays.HasValue ? TimeSpan.FromDays(aggregateDays.Value) : null,
            grain, batchSize, action, "fail-closed-and-retry");

    private static DataSetRetentionPolicy OwnerManaged(string name, string owner) =>
        new(name, owner, DataLifecycleMode.OwnerManaged, null, null, PartitionGrain.None, 0,
            "owner-explicit-delete-only", "never-delete-automatically");
}
