using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Controllers;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using RuntimeInventoryResource = GZCTF.Modules.Runtime.Contracts.AgentRuntimeInventoryResource;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class RuntimeInventoryAvailabilityTests
{
    [Fact]
    public async Task FailedComputeProviderIsUnavailableWhileOtherProviderRetainsFacts()
    {
        var unavailable = await RuntimeController.ReadComputeAsync(true,
            _ => throw new IOException("Daemon unavailable"), "Docker", NullLogger.Instance, default);
        Assert.False(unavailable.Available);
        var fact = new RuntimeInventoryResource("uuid", "vm-fixture", 3, "running");
        var available = await RuntimeController.ReadComputeAsync(true,
            _ => Task.FromResult<IReadOnlyList<RuntimeInventoryResource>>([fact]), "KVM", NullLogger.Instance, default);
        Assert.True(available.Available);
        Assert.Same(fact, Assert.Single(available.Resources));
    }

    [Fact]
    public async Task EmptySuccessfulInventoryIsDifferentFromUnavailableInventory()
    {
        var empty = await RuntimeController.ReadComputeAsync(true,
            _ => Task.FromResult<IReadOnlyList<RuntimeInventoryResource>>([]), "Docker", NullLogger.Instance, default);
        Assert.True(empty.Available);
        Assert.Empty(empty.Resources);
    }

    [Fact]
    public async Task CallerCancellationIsNeverConvertedIntoAnUnavailableProvider()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RuntimeController.ReadComputeAsync(true,
            _ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }, "Docker", NullLogger.Instance, cancellation.Token));
    }
}
