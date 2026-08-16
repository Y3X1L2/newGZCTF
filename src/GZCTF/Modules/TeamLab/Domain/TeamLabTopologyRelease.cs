namespace GZCTF.Modules.TeamLab.Domain;

public sealed class TeamLabTopologyRelease
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public int TopologyId { get; set; }
    public Guid? ControlScopeId { get; set; }
    public int Version { get; set; }
    public int SourceRevision { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string CanonicalJson { get; set; } = string.Empty;
    public string EditorMetadataJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public Guid? PublishedById { get; set; }
    public Guid? ApiOperationId { get; set; }
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Archived releases stay readable and drainable but no longer accept new runtimes.</summary>
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public TeamLabTopology Topology { get; set; } = null!;
    public TeamLabControlScope? ControlScope { get; set; }
}
