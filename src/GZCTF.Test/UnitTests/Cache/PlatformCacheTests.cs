using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Cache;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.Cache;

public sealed class PlatformCacheTests
{
    [Fact]
    public async Task ConcurrentFactoryFailure_IsExecutedOnceAndPropagatedToAllCallers()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddMemoryCache();
        serviceCollection.AddHybridCache();
        using var services = serviceCollection.BuildServiceProvider();
        using var telemetry = new RedisTelemetry();
        var runtimeState = new RedisRuntimeState(Options.Create(new RedisRuntimeOptions
        {
            Mode = RedisRuntimeMode.Disabled
        }), telemetry);
        var cache = new PlatformCache(
            services.GetRequiredService<HybridCache>(),
            new ZeroRevisionStore(),
            runtimeState,
            NullLogger<PlatformCache>.Instance);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;

        async ValueTask<int> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref factoryCalls);
            factoryStarted.TrySetResult();
            await releaseFactory.Task.WaitAsync(cancellationToken);
            throw new TestFactoryException();
        }

        var callers = Enumerable.Range(0, 16)
            .Select(async _ => await cache.GetOrCreateAsync(
                CachePolicyCatalog.GameList, "shared", Factory, CancellationToken.None))
            .ToArray();
        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        releaseFactory.SetResult();

        foreach (var caller in callers)
            await Assert.ThrowsAsync<TestFactoryException>(async () => await caller);
        Assert.Equal(1, Volatile.Read(ref factoryCalls));
    }

    private sealed class ZeroRevisionStore : IProjectionRevisionStore
    {
        public ValueTask<long> GetAsync(string projection, string resourceKey,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(0L);

        public ValueTask BumpAsync(string projection, string resourceKey,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class TestFactoryException : Exception;
}
