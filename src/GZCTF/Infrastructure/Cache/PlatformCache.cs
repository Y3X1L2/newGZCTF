using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Caching.Hybrid;

namespace GZCTF.Infrastructure.Cache;

public interface IPlatformCache
{
    ValueTask<T> GetOrCreateAsync<T>(CachePolicy policy, string resourceKey,
        Func<CancellationToken, ValueTask<T>> factory, CancellationToken cancellationToken = default);
    ValueTask InvalidateAsync(CachePolicy policy, string resourceKey,
        CancellationToken cancellationToken = default);
    ValueTask InvalidateAllAsync(CachePolicy policy, CancellationToken cancellationToken = default);
}

public sealed class PlatformCache(
    HybridCache cache,
    IProjectionRevisionStore revisions,
    RedisRuntimeState runtimeState,
    ILogger<PlatformCache> logger) : IPlatformCache
{
    private static readonly ActivitySource ActivitySource = new("GZCTF.Cache");

    public async ValueTask<T> GetOrCreateAsync<T>(CachePolicy policy, string resourceKey,
        Func<CancellationToken, ValueTask<T>> factory, CancellationToken cancellationToken = default)
    {
        policy.Validate();
        var revision = policy.ConsistencyMode == CacheConsistencyMode.ProjectionRevision
            ? await revisions.GetAsync(policy.Name, resourceKey, cancellationToken)
            : 0;
        var globalRevision = policy.ConsistencyMode == CacheConsistencyMode.ProjectionRevision
            ? await revisions.GetAsync(policy.Name, GlobalRevisionKey, cancellationToken)
            : 0;
        var key = BuildKey(policy, resourceKey, globalRevision, revision);
        using var activity = ActivitySource.StartActivity("cache.get_or_create");
        activity?.SetTag("cache.policy", policy.Name);

        if (runtimeState.ShouldBypassCache)
            return await factory(cancellationToken);

        try
        {
            var options = new HybridCacheEntryOptions
            {
                LocalCacheExpiration = policy.LocalTtl,
                Expiration = policy.DistributedTtl
            };
            async ValueTask<T> InvokeFactory(CancellationToken token)
            {
                try
                {
                    return await factory(token);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw new PlatformCacheFactoryException(exception);
                }
            }

            var value = await cache.GetOrCreateAsync(key, InvokeFactory, options,
                [PolicyTag(policy), Tag(policy, resourceKey)], cancellationToken);
            runtimeState.RecordSuccess("cache");
            return value;
        }
        catch (PlatformCacheFactoryException exception)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException!).Throw();
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            runtimeState.RecordFailure("cache");
            logger.LogWarning(exception, "Cache policy {Policy} bypassed after a cache failure", policy.Name);
            return await factory(cancellationToken);
        }
    }

    public async ValueTask InvalidateAsync(CachePolicy policy, string resourceKey,
        CancellationToken cancellationToken = default)
    {
        if (policy.ConsistencyMode == CacheConsistencyMode.ProjectionRevision)
        {
            await revisions.BumpAsync(policy.Name, resourceKey, cancellationToken);
            return;
        }

        try
        {
            await cache.RemoveByTagAsync(Tag(policy, resourceKey), cancellationToken);
            runtimeState.RecordSuccess("cache-invalidation");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            runtimeState.RecordFailure("cache-invalidation");
            logger.LogWarning(exception, "Cache invalidation failed for policy {Policy}", policy.Name);
        }
    }

    public async ValueTask InvalidateAllAsync(CachePolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (policy.ConsistencyMode == CacheConsistencyMode.ProjectionRevision)
        {
            await revisions.BumpAsync(policy.Name, GlobalRevisionKey, cancellationToken);
            return;
        }

        try
        {
            await cache.RemoveByTagAsync(PolicyTag(policy), cancellationToken);
            runtimeState.RecordSuccess("cache-invalidation");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            runtimeState.RecordFailure("cache-invalidation");
            logger.LogWarning(exception, "Global cache invalidation failed for policy {Policy}", policy.Name);
        }
    }

    private const string GlobalRevisionKey = "__global__";

    private static string BuildKey(CachePolicy policy, string resourceKey, long globalRevision, long revision) =>
        $"{policy.Name}:s{policy.SchemaVersion}:g{globalRevision}:r{revision}:{resourceKey.ToSHA256String()}";

    private static string Tag(CachePolicy policy, string resourceKey) =>
        $"{policy.Name}:s{policy.SchemaVersion}:{resourceKey.ToSHA256String()}";

    private static string PolicyTag(CachePolicy policy) => $"{policy.Name}:s{policy.SchemaVersion}";

    private sealed class PlatformCacheFactoryException(Exception innerException)
        : Exception("The cache value factory failed.", innerException);
}
