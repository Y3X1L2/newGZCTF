using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;

namespace GZCTF.Modules.TeamLab.Domain.Runtime;

public class TeamLabTrafficFlow
{
    [Key] public long Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public long SourceCursor { get; set; }
    public int? ShardId { get; set; }
    public int? NetworkId { get; set; }
    public Guid? WorkerNodeId { get; set; }
    [MaxLength(64)] public string SourceIp { get; set; } = string.Empty;
    [MaxLength(64)] public string SourcePrefix { get; set; } = string.Empty;
    public int? SourcePort { get; set; }
    [MaxLength(64)] public string DestinationIp { get; set; } = string.Empty;
    [MaxLength(64)] public string DestinationPrefix { get; set; } = string.Empty;
    public int? DestinationPort { get; set; }
    [MaxLength(16)] public string Protocol { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public long Packets { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public byte[] Fingerprint { get; set; } = [];
    public TeamLabRuntime Runtime { get; set; } = null!;
    public TeamLabRuntimeShard? Shard { get; set; }
    public TeamLabRuntimeNetwork? Network { get; set; }
    public WorkerNode? WorkerNode { get; set; }
}
