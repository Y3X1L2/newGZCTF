using System.Net;

namespace GZCTF.Modules.TeamLab.Domain;

public sealed class TeamLabNetworkLease
{
    public long Id { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; } = 1;
    public Guid TopologyReleaseId { get; set; }
    public string NetworkKey { get; set; } = string.Empty;
    public IPNetwork AllocatedCidr { get; set; }
    public DateTimeOffset AllocatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReleasedAt { get; set; }
    public TeamLabTopologyRelease TopologyRelease { get; set; } = null!;
}
