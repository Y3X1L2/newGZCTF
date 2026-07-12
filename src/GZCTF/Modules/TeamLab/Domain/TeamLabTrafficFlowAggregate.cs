namespace GZCTF.Modules.TeamLab.Domain;

public sealed class TeamLabTrafficFlowAggregate
{
    public long Id { get; set; }
    public DateTimeOffset BucketStart { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public int ShardId { get; set; }
    public int NetworkId { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public string SourcePrefix { get; set; } = string.Empty;
    public string DestinationPrefix { get; set; } = string.Empty;
    public long FlowCount { get; set; }
    public long PacketCount { get; set; }
    public long Bytes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
