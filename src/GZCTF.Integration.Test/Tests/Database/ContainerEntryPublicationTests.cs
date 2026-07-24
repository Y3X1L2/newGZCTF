using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ContainerEntryPublicationTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task FailedPublication_PreservesPreviouslyReadyRoutes()
    {
        var readyAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var ready = NewContainer(ContainerEntryStatus.Ready, readyAt);
        var pending = NewContainer(ContainerEntryStatus.Pending, null);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Containers.AddRange(ready, pending);
        await context.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IContainerRepository>();

        var updated = await repository.SetEntryPublicationResultAsync(
            [ready.PublicPortLeaseId!.Value, pending.PublicPortLeaseId!.Value],
            ContainerEntryStatus.Error,
            "candidate failed",
            CancellationToken.None);

        Assert.Equal(1, updated);
        context.ChangeTracker.Clear();
        var persistedReady = await context.Containers.SingleAsync(item => item.Id == ready.Id);
        var persistedPending = await context.Containers.SingleAsync(item => item.Id == pending.Id);
        Assert.Equal(ContainerEntryStatus.Ready, persistedReady.EntryStatus);
        Assert.NotNull(persistedReady.EntryReadyAt);
        Assert.InRange(
            persistedReady.EntryReadyAt.Value,
            readyAt.AddMilliseconds(-1),
            readyAt.AddMilliseconds(1));
        Assert.Null(persistedReady.EntryError);
        Assert.Equal(ContainerEntryStatus.Error, persistedPending.EntryStatus);
        Assert.Null(persistedPending.EntryReadyAt);
        Assert.Equal("candidate failed", persistedPending.EntryError);
    }

    [Fact]
    public async Task SuccessfulPublication_MarksAllCurrentRoutesReady()
    {
        var first = NewContainer(ContainerEntryStatus.Pending, null);
        var second = NewContainer(ContainerEntryStatus.Error, null);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Containers.AddRange(first, second);
        await context.SaveChangesAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IContainerRepository>();

        var updated = await repository.SetEntryPublicationResultAsync(
            [first.PublicPortLeaseId!.Value, second.PublicPortLeaseId!.Value],
            ContainerEntryStatus.Ready,
            null,
            CancellationToken.None);

        Assert.Equal(2, updated);
        context.ChangeTracker.Clear();
        var persisted = await context.Containers
            .Where(item => item.Id == first.Id || item.Id == second.Id)
            .ToArrayAsync();
        Assert.All(persisted, item =>
        {
            Assert.Equal(ContainerEntryStatus.Ready, item.EntryStatus);
            Assert.NotNull(item.EntryReadyAt);
            Assert.Null(item.EntryError);
        });
    }

    private static Container NewContainer(ContainerEntryStatus status, DateTimeOffset? readyAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            Image = "phase-two-test",
            ContainerId = Guid.NewGuid().ToString("N"),
            Status = ContainerStatus.Running,
            IP = "10.24.0.30",
            Port = 32768,
            PublicIP = "203.0.113.10",
            PublicPort = 30000,
            PublicPortLeaseId = Guid.NewGuid(),
            EntryStatus = status,
            EntryReadyAt = readyAt
        };
}
