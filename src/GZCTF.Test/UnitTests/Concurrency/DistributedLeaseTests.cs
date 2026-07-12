using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Infrastructure.Concurrency;
using Xunit;

namespace GZCTF.Test.UnitTests.Concurrency;

public sealed class DistributedLeaseTests
{
    [Fact]
    public async Task LocalLease_SerializesSameResourceAndReleasesOnAsyncDispose()
    {
        var provider = new LocalDevelopmentLeaseProvider();
        await using var first = await provider.AcquireAsync("scheduler", TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await provider.AcquireAsync("scheduler", TimeSpan.FromMilliseconds(20)));

        await first.DisposeAsync();
        await using var second = await provider.AcquireAsync("scheduler", TimeSpan.FromSeconds(1));

        Assert.NotEqual(first.OwnerToken, second.OwnerToken);
        Assert.False(second.LeaseLost.IsCancellationRequested);
        Assert.True(await second.RenewAsync());
    }

    [Fact]
    public async Task LocalLease_NeverAllowsConcurrentOwnersDuringRapidReuse()
    {
        var provider = new LocalDevelopmentLeaseProvider();
        var currentOwners = 0;
        var maximumOwners = 0;
        var counterGate = new object();

        async Task ExerciseLeaseAsync()
        {
            for (var iteration = 0; iteration < 250; iteration++)
            {
                await using var lease = await provider.AcquireAsync("rapid-reuse", TimeSpan.FromSeconds(2));
                var owners = Interlocked.Increment(ref currentOwners);
                lock (counterGate)
                    maximumOwners = Math.Max(maximumOwners, owners);
                await Task.Yield();
                Interlocked.Decrement(ref currentOwners);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ExerciseLeaseAsync()));

        Assert.Equal(1, maximumOwners);
    }
}
