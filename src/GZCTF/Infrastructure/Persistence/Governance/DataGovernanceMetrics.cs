using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed class DataGovernanceMetrics
{
    public const string MeterName = "GZCTF.DatabaseGovernance";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "gzctf_db_governance_duration_seconds", "s");
    private static readonly Counter<long> Rows = Meter.CreateCounter<long>(
        "gzctf_db_governance_rows_total", "rows");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>(
        "gzctf_db_governance_failures_total", "failures");
    private static readonly ConcurrentDictionary<string, int> HorizonDays = new(StringComparer.Ordinal);

    static DataGovernanceMetrics()
    {
        Meter.CreateObservableGauge("gzctf_db_partition_horizon_days",
            () => HorizonDays.Select(item => new Measurement<int>(item.Value,
                new KeyValuePair<string, object?>("data_set", item.Key))), "days");
    }

    public void RecordDuration(string dataSet, string operation, TimeSpan duration) =>
        Duration.Record(duration.TotalSeconds, Tags(dataSet, operation));

    public void RecordRows(string dataSet, string operation, long rows) =>
        Rows.Add(rows, Tags(dataSet, operation));

    public void RecordFailure(string dataSet, string operation) =>
        Failures.Add(1, Tags(dataSet, operation));

    public void SetPartitionHorizon(string dataSet, int days) => HorizonDays[dataSet] = days;

    private static TagList Tags(string dataSet, string operation) => new()
    {
        { "data_set", dataSet },
        { "operation", operation }
    };
}
