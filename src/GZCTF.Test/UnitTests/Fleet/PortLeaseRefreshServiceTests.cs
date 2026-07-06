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
        repository.Setup(r => r.GetProxyPortMappingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PortMappingEntry(30042, "10.24.0.30", 42762),
                new PortMappingEntry(29999, "10.24.0.31", 42763)
            ]);
        var allocator = new RecordingPortAllocator();
        var service = CreateService(repository.Object, allocator);

        await service.RefreshOnceAsync(CancellationToken.None);

        var reservation = Assert.Single(allocator.ReservedPorts);
        Assert.Equal(30042, reservation.Port);
        Assert.Contains("10.24.0.30", reservation.Owner, StringComparison.Ordinal);
        Assert.Contains("42762", reservation.Owner, StringComparison.Ordinal);
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
        public List<(int Port, string Owner)> ReservedPorts { get; } = [];
        public bool IsRedisBacked => true;
        public PortAllocationRange CurrentRange => new(30000, 30099, "nginx", RequiresRedis: true);

        public Task<int> AllocatePortAsync(Guid containerId, CancellationToken token = default) =>
            Task.FromResult(0);

        public Task ReleasePortAsync(int port, CancellationToken token = default) =>
            Task.CompletedTask;

        public Task ReserveExistingPortAsync(int port, string owner, CancellationToken token = default)
        {
            ReservedPorts.Add((port, owner));
            return Task.CompletedTask;
        }
    }

}
