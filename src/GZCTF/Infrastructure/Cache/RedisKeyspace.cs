using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace GZCTF.Infrastructure.Cache;

public enum RedisKeyPurpose
{
    Cache,
    Lock,
    Lease,
    Stream,
    Backplane,
    WakeUp
}

public sealed partial class RedisKeyspace
{
    public const int SchemaVersion = 1;

    private readonly string _prefix;

    public RedisKeyspace(IOptions<RedisRuntimeOptions> options) : this(options.Value.KeyPrefix)
    {
    }

    internal RedisKeyspace(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || !LiteralPattern().IsMatch(prefix))
            throw new ArgumentException("Redis key prefix is not canonical.", nameof(prefix));
        _prefix = prefix;
    }

    public RedisKey Create(RedisKeyPurpose purpose, params string[] resourceSegments) =>
        Build(purpose, (resourceSegments ?? throw new ArgumentNullException(nameof(resourceSegments)))
            .Select(ValidateLiteral));

    public string CreateFrameworkPrefix(RedisKeyPurpose purpose, params string[] resourceSegments) =>
        $"{Create(purpose, resourceSegments)}:";

    public RedisKey CreateTagged(RedisKeyPurpose purpose, string category, string hashTag,
        params string[] resourceSegments)
    {
        var segments = new List<string> { ValidateLiteral(category), $"{{{ValidateLiteral(hashTag)}}}" };
        segments.AddRange((resourceSegments ?? throw new ArgumentNullException(nameof(resourceSegments)))
            .Select(ValidateLiteral));
        return Build(purpose, segments);
    }

    public RedisKey CreateOpaque(RedisKeyPurpose purpose, string category, string sensitiveResource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensitiveResource);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sensitiveResource)));
        return Build(purpose, [ValidateLiteral(category), "sha256", digest]);
    }

    private RedisKey Build(RedisKeyPurpose purpose, IEnumerable<string> resourceSegments)
    {
        var segments = resourceSegments.ToArray();
        if (segments.Length == 0)
            throw new ArgumentException("At least one Redis resource segment is required.", nameof(resourceSegments));

        return (RedisKey)$"{_prefix}:v{SchemaVersion}:{PurposeName(purpose)}:{string.Join(':', segments)}";
    }

    private static string ValidateLiteral(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 64 || !LiteralPattern().IsMatch(value))
            throw new ArgumentException("Redis key segments must be canonical non-sensitive identifiers.", nameof(value));
        return value;
    }

    private static string PurposeName(RedisKeyPurpose purpose) => purpose switch
    {
        RedisKeyPurpose.Cache => "cache",
        RedisKeyPurpose.Lock => "lock",
        RedisKeyPurpose.Lease => "lease",
        RedisKeyPurpose.Stream => "stream",
        RedisKeyPurpose.Backplane => "backplane",
        RedisKeyPurpose.WakeUp => "wake-up",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
    };

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex LiteralPattern();
}
