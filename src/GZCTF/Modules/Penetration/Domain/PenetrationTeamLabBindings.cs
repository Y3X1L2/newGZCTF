namespace GZCTF.Modules.Penetration.Domain;

public sealed class PenetrationGameLabBinding
{
    public int GameId { get; set; }
    public int TopologyId { get; set; }
    public Guid? ActiveReleaseId { get; set; }
    public int MaxResetCount { get; set; } = 3;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PenetrationTeamRuntimeBinding
{
    public int GameId { get; set; }
    public int TeamId { get; set; }
    public int RuntimeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
