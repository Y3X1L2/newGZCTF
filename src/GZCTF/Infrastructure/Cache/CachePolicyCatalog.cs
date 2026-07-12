namespace GZCTF.Infrastructure.Cache;

public static class CachePolicyNames
{
    public const string ClientConfig = "client-config";
    public const string Index = "index";
    public const string Favicon = "favicon";
    public const string CaptchaConfig = "captcha-config";
}

public static class RuntimeCacheKeys
{
    public const string CronJobLock = "cron-job-lock";
    public static string HashPow(string id) => $"hash-pow:{id.ToSHA256String()}";
    public static string ConnectionCount(Guid id) => $"connection-count:{id:N}";
}

public sealed class CachePolicyCatalog
{
    public static readonly CachePolicy Scoreboard = Revision("scoreboard", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), 8 * 1024 * 1024, schemaVersion: 2);
    public static readonly CachePolicy TrainingStatistics = Revision("training-statistics", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), 2 * 1024 * 1024);
    public static readonly CachePolicy TheoryStatistics = Revision("theory-statistics", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), 2 * 1024 * 1024);
    public static readonly CachePolicy ClientConfig = Tagged(CachePolicyNames.ClientConfig, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10), 256 * 1024);
    public static readonly CachePolicy Index = Tagged(CachePolicyNames.Index, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10), 512 * 1024);
    public static readonly CachePolicy Favicon = Tagged(CachePolicyNames.Favicon, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10), 1024);
    public static readonly CachePolicy CaptchaConfig = Tagged(CachePolicyNames.CaptchaConfig, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10), 64 * 1024);
    public static readonly CachePolicy GameList = Tagged("game-list", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), 2 * 1024 * 1024);
    public static readonly CachePolicy RecentGames = Tagged("recent-games", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), 2 * 1024 * 1024);
    public static readonly CachePolicy GameDetails = Tagged("game-details", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), 2 * 1024 * 1024);
    public static readonly CachePolicy Posts = Tagged("posts", TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(5), 4 * 1024 * 1024);
    public static readonly CachePolicy GameNotices = Tagged("game-notices", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1), 4 * 1024 * 1024);
    public static readonly CachePolicy ExerciseAvailability = Tagged("exercise-availability", TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), 1024);

    public IReadOnlyList<CachePolicy> All { get; } =
    [
        Scoreboard, TrainingStatistics, TheoryStatistics, ClientConfig, Index, Favicon, CaptchaConfig,
        GameList, RecentGames, GameDetails, Posts, GameNotices, ExerciseAvailability
    ];

    public CachePolicyCatalog()
    {
        foreach (var policy in All)
            policy.Validate();

        if (All.Select(policy => policy.Name).Distinct(StringComparer.Ordinal).Count() != All.Count)
            throw new InvalidOperationException("Cache policy names must be unique.");
    }

    public CachePolicy GetRequired(string name) =>
        All.SingleOrDefault(policy => string.Equals(policy.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Cache policy '{name}' is not registered.");

    private static CachePolicy Tagged(string name, TimeSpan local, TimeSpan distributed, long size) =>
        new(name, 1, local, distributed, distributed, size, CacheConsistencyMode.TagInvalidation);

    private static CachePolicy Revision(string name, TimeSpan local, TimeSpan distributed, long size,
        int schemaVersion = 1) =>
        new(name, schemaVersion, local, distributed, TimeSpan.Zero, size,
            CacheConsistencyMode.ProjectionRevision);
}
