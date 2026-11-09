using GZCTF.Modules.Runtime.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Runtime.Application;

/// <summary>Reads the minute samples already persisted independently of dashboard visits.</summary>
public sealed class NodeMetricHistoryQuery(AppDbContext context, TimeProvider? timeProvider = null)
{
    public async Task<NodeMetricHistory?> ReadAsync(string range, Guid? nodeId,
        CancellationToken cancellationToken)
    {
        var (intervalSeconds, pointCount) = range switch
        {
            "1h" => (60, 60),
            "24h" => (300, 288),
            "7d" => (1800, 336),
            _ => throw new ArgumentException("Unsupported metric history range.", nameof(range))
        };
        if (nodeId.HasValue && !await context.WorkerNodes.AsNoTracking()
                .AnyAsync(node => node.Id == nodeId.Value, cancellationToken))
            return null;

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        // Include the current partial bucket without shifting the previous buckets on each refresh.
        var to = DateTimeOffset.FromUnixTimeSeconds(
            (now.ToUnixTimeSeconds() / intervalSeconds + 1) * intervalSeconds);
        var from = to.AddSeconds(-(long)intervalSeconds * pointCount);
        var allNodes = !nodeId.HasValue;
        var selectedNode = nodeId ?? Guid.Empty;

        // Group in PostgreSQL: even a seven-day fleet request returns at most 336 rows.
        // Each stored node-minute contributes equally, regardless of heartbeat frequency.
        // Invalid historical metrics are excluded separately; missing measurements stay null.
        var rows = await context.Database.SqlQuery<MetricBucket>($"""
            SELECT to_timestamp(floor(extract(epoch FROM "WindowStart") / {intervalSeconds}) * {intervalSeconds}) AS "Timestamp",
                avg(CASE WHEN "AverageCpuLoad" BETWEEN 0 AND 1 THEN "AverageCpuLoad"::double precision END) AS "CpuLoad",
                avg(CASE WHEN "AverageMemoryLoad" BETWEEN 0 AND 1 THEN "AverageMemoryLoad"::double precision END) AS "MemoryLoad",
                count(DISTINCT "WorkerNodeId")::integer AS "NodeCount",
                sum("SampleCount")::bigint AS "SampleCount"
            FROM "WorkerNodeMetricSamples"
            WHERE "WindowStart" >= {from} AND "WindowStart" < {to}
                AND "WindowStart" <= {now} AND "SampleCount" > 0
                AND ({allNodes} OR "WorkerNodeId" = {selectedNode})
            GROUP BY 1
            ORDER BY 1
            """).ToArrayAsync(cancellationToken);
        var byTime = rows.ToDictionary(row => row.Timestamp);
        var points = Enumerable.Range(0, pointCount).Select(index =>
        {
            var timestamp = from.AddSeconds((long)index * intervalSeconds);
            return byTime.TryGetValue(timestamp, out var row)
                ? new NodeMetricHistoryPoint(timestamp, row.CpuLoad, row.MemoryLoad, row.NodeCount, row.SampleCount)
                : new NodeMetricHistoryPoint(timestamp, null, null, 0, 0);
        }).ToArray();
        return new NodeMetricHistory(from, to, intervalSeconds, points);
    }

    private sealed class MetricBucket
    {
        public DateTimeOffset Timestamp { get; set; }
        public double? CpuLoad { get; set; }
        public double? MemoryLoad { get; set; }
        public int NodeCount { get; set; }
        public long SampleCount { get; set; }
    }
}
