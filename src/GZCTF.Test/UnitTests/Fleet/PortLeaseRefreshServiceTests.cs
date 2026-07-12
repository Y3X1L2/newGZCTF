using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Fleet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var service = CreateService(repository.Object, allocator);

        await service.RefreshOnceAsync(CancellationToken.None);

        var reservation = Assert.Single(allocator.ReservedPorts);
        Assert.Equal(30042, reservation.Port);
        Assert.Equal(firstLease, reservation.LeaseId);
    }

    static PortLeaseRefreshService CreateService(IContainerRepository repository, IPortAllocationService allocator)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(allocator);
        var provider = services.BuildServiceProvider();

        return new PortLeaseRefreshService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ContainerProvider
            {
                NginxProxyConfig = new NginxProxyConfig
                {
                    Enable = true,
                    SyncLocalConfig = false,
                    ListenPortStart = 30000,
                    ListenPortEnd = 30099
                }
            }),
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
