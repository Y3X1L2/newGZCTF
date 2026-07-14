namespace GZCTF.Modules.TeamLab.Domain;

public sealed class TeamLabTopologyRelease
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public int TopologyId { get; set; }
    public int Version { get; set; }
    public int SourceRevision { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string CanonicalJson { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public Guid? PublishedById { get; set; }
    public Guid? ApiOperationId { get; set; }
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabTopology Topology { get; set; } = null!;
}
