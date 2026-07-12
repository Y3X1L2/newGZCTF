namespace GZCTF.Infrastructure.Cache;

public enum CacheConsistencyMode
{
    TagInvalidation,
    ProjectionRevision
}

public sealed record CachePolicy(
    string Name,
    int SchemaVersion,
    TimeSpan LocalTtl,
    TimeSpan DistributedTtl,
    TimeSpan MaximumStale,
    long SizeLimit,
    CacheConsistencyMode ConsistencyMode)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidOperationException("Cache policy name is required.");
        if (SchemaVersion <= 0)
            throw new InvalidOperationException($"Cache policy '{Name}' must have a positive schema version.");
        if (LocalTtl <= TimeSpan.Zero || DistributedTtl < LocalTtl)
            throw new InvalidOperationException($"Cache policy '{Name}' has invalid expiration values.");
        if (MaximumStale < TimeSpan.Zero || SizeLimit <= 0)
            throw new InvalidOperationException($"Cache policy '{Name}' has invalid stale or size limits.");
    }
}
