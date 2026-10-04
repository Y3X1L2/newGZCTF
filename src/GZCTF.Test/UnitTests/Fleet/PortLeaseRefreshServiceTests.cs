using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Models;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Fleet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.Fleet;

public class PortLeaseRefreshServiceTests
{
    [Fact]
    public async Task RefreshOnceAsync_RefreshesActiveProxyPortsWithinNginxRange()
    {
        var repository = new Mock<IContainerRepository>();
        var firstLease = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        repository.Setup(r => r.GetProxyPortMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PortMappingEntry(30042, "10.24.0.30", 42762, firstLease),
                new PortMappingEntry(29999, "10.24.0.31", 42763, Guid.NewGuid())
            ]);
        var allocator = new RecordingPortAllocator();
        await using var db = Context();
        var service = CreateService(repository.Object, allocator, db);

        await service.RefreshOnceAsync(CancellationToken.None);

        var reservation = Assert.Single(allocator.ReservedPorts);
        Assert.Equal(30042, reservation.Port);
        Assert.Equal(firstLease, reservation.LeaseId);
    }

    [Fact]
    public async Task RefreshOnceAsync_RefreshesOnlyActiveServiceMappings()
    {
        await using var db = Context();
        var leaseId = Guid.NewGuid();
        db.TeamLabServiceAccesses.AddRange(
            new TeamLabServiceAccess { PublicPort = 30050, PortLeaseId = leaseId, Status = "active" },
            new TeamLabServiceAccess { PublicPort = 30051, PortLeaseId = Guid.NewGuid(), Status = "revoked", RevokedAt = DateTimeOffset.UtcNow },
            new TeamLabServiceAccess { PublicPort = 30052, PortLeaseId = Guid.NewGuid(), Status = "failed" });
        await db.SaveChangesAsync();
        var repository = new Mock<IContainerRepository>();
        repository.Setup(item => item.GetProxyPortMappingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var allocator = new RecordingPortAllocator();

        await CreateService(repository.Object, allocator, db).RefreshOnceAsync(CancellationToken.None);

        Assert.Equal((30050, leaseId), Assert.Single(allocator.ReservedPorts));
    }

    static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    static PortLeaseRefreshService CreateService(IContainerRepository repository, IPortAllocationService allocator, AppDbContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(allocator);
        services.AddSingleton(context);
        var provider = services.BuildServiceProvider();

        return new PortLeaseRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PortLeaseRefreshService>.Instance);
    }

    sealed class RecordingPortAllocator : IPortAllocationService
    {
        public List<(int Port, Guid LeaseId)> ReservedPorts { get; } = [];
        public bool IsRedisBacked => true;
        public PortAllocationRange CurrentRange => new(30000, 30099, "nginx", RequiresRedis: true);

        public Task<PortLease?> AllocatePortAsync(Guid containerId, CancellationToken token = default) =>
            Task.FromResult<PortLease?>(null);

        public Task<bool> ReleasePortAsync(int port, Guid leaseId, CancellationToken token = default) =>
            Task.FromResult(true);

        public Task<bool> ReserveExistingPortAsync(int port, Guid leaseId, CancellationToken token = default)
        {
            ReservedPorts.Add((port, leaseId));
            return Task.FromResult(true);
        }
    }

}
