namespace GZCTF.Modules.Runtime.Domain;

public sealed class WorkerNodeMetricSample
{
    public Guid WorkerNodeId { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public int SampleCount { get; set; }
    public float AverageCpuLoad { get; set; }
    public float MinimumCpuLoad { get; set; }
    public float MaximumCpuLoad { get; set; }
    public float AverageMemoryLoad { get; set; }
    public float MinimumMemoryLoad { get; set; }
    public float MaximumMemoryLoad { get; set; }
    public double AverageContainers { get; set; }
    public int MaximumContainers { get; set; }
    public double AverageVms { get; set; }
    public int MaximumVms { get; set; }
    public double AverageUsedPorts { get; set; }
    public int MaximumUsedPorts { get; set; }
    public long FirstSequence { get; set; }
    public long LastSequence { get; set; }
    public DateTimeOffset FirstReceivedAt { get; set; }
    public DateTimeOffset LastReceivedAt { get; set; }
}
