namespace GZCTF.Infrastructure.Cache;

public sealed class ProjectionRevision
{
    public string Projection { get; set; } = string.Empty;
    public string ResourceKey { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
