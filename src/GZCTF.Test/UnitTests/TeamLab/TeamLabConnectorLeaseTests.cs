using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Moq;
using GZCTF.TeamLab.Contracts;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabConnectorLeaseTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"connectors-{Guid.NewGuid():N}")
            .Options);

    private static RegisterTeamLabConnectorModel Command(
        string name = "field-vlan-1",
        bool shared = false,
        int capacity = 1,
        Guid? scopeId = null) => new(
        name, name, "vlan", scopeId, shared, capacity, "ops-managed:enp3s0.120", null);

    private static async Task<TeamLabRuntime> AddRuntimeAsync(AppDbContext context, TeamLabRuntimeStatus status = TeamLabRuntimeStatus.Running)
    {
        var runtime = new TeamLabRuntime { Status = status };
        context.TeamLabRuntimes.Add(runtime);
        await context.SaveChangesAsync();
        return runtime;
    }

    [Fact]
    public async Task Acquire_IsExclusiveAcrossRuntimes_AndIdempotentPerRuntime()
    {
        using var context = CreateContext();
        var service = new TeamLabConnectorService(context, Mock.Of<ITeamLabConnectorNodeGateway>());
        var connector = await service.RegisterAsync(Command(), CancellationToken.None);
        var first = await AddRuntimeAsync(context);
        var second = await AddRuntimeAsync(context);

        var lease = await service.AcquireAsync(connector.Id, first.PublicId, null, CancellationToken.None);
        Assert.Equal(1, lease.Slot);
        Assert.Null(lease.ReleasedAt);

        var again = await service.AcquireAsync(connector.Id, first.PublicId, null, CancellationToken.None);
        Assert.Equal(lease.Id, again.Id);

        var occupied = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.AcquireAsync(connector.Id, second.PublicId, null, CancellationToken.None));
        Assert.Equal("connector_occupied", occupied.Code);
        Assert.Equal(409, occupied.StatusCode);
    }

    [Fact]
    public async Task Release_AllowsTheNextRuntimeToAcquire()
    {
        using var context = CreateContext();
        var service = new TeamLabConnectorService(context, Mock.Of<ITeamLabConnectorNodeGateway>());
        var connector = await service.RegisterAsync(Command(), CancellationToken.None);
        var first = await AddRuntimeAsync(context);
        var second = await AddRuntimeAsync(context);
        await service.AcquireAsync(connector.Id, first.PublicId, null, CancellationToken.None);

        var released = await service.ReleaseAsync(
            connector.Id, first.PublicId, TeamLabConnectorLeaseReleaseReason.ManualRelease, CancellationToken.None);
        Assert.NotNull(released.ReleasedAt);
        Assert.Equal("manual-release", released.ReleaseReason);

        var repeat = await service.ReleaseAsync(
            connector.Id, first.PublicId, TeamLabConnectorLeaseReleaseReason.ManualRelease, CancellationToken.None);
        Assert.Equal(released.Id, repeat.Id);

        var next = await service.AcquireAsync(connector.Id, second.PublicId, null, CancellationToken.None);
        Assert.Equal(1, next.Slot);
    }

    [Fact]
    public async Task SharedConnector_RespectsDeclaredCapacity()
    {
        using var context = CreateContext();
        var service = new TeamLabConnectorService(context, Mock.Of<ITeamLabConnectorNodeGateway>());
        var connector = await service.RegisterAsync(Command(name: "shared-gateway", shared: true, capacity: 2), CancellationToken.None);
        var first = await AddRuntimeAsync(context);
        var second = await AddRuntimeAsync(context);
        var third = await AddRuntimeAsync(context);

        await service.AcquireAsync(connector.Id, first.PublicId, null, CancellationToken.None);
        var secondLease = await service.AcquireAsync(connector.Id, second.PublicId, null, CancellationToken.None);
        Assert.Equal(2, secondLease.Slot);

        var exhausted = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.AcquireAsync(connector.Id, third.PublicId, null, CancellationToken.None));
        Assert.Equal("connector_occupied", exhausted.Code);
    }

    [Fact]
    public async Task Acquire_RejectsTerminatedRuntime_AndDoesNotUseLegacyManualHealth()
    {
        using var context = CreateContext();
        var service = new TeamLabConnectorService(context, Mock.Of<ITeamLabConnectorNodeGateway>());
        var connector = await service.RegisterAsync(Command(), CancellationToken.None);
        var runtime = await AddRuntimeAsync(context, TeamLabRuntimeStatus.Destroyed);

        var terminated = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.AcquireAsync(connector.Id, runtime.PublicId, null, CancellationToken.None));
        Assert.Equal("runtime_not_active", terminated.Code);

        var healthy = await service.SetHealthAsync(
            connector.Id, TeamLabConnectorHealth.Unreachable, CancellationToken.None);
        Assert.Equal("unreachable", healthy.Health);
        var available = await AddRuntimeAsync(context);
        var acquired = await service.AcquireAsync(connector.Id, available.PublicId, null, CancellationToken.None);
        Assert.Equal(available.PublicId, acquired.RuntimeId);
    }

    [Fact]
    public async Task ScopeBoundConnector_IsHiddenFromOtherScopes()
    {
        using var context = CreateContext();
        var scope = new TeamLabControlScope { Key = "tenant-a", DisplayName = "Tenant A" };
        context.TeamLabControlScopes.Add(scope);
        await context.SaveChangesAsync();
        var service = new TeamLabConnectorService(context, Mock.Of<ITeamLabConnectorNodeGateway>());
        var connector = await service.RegisterAsync(Command(name: "scoped", scopeId: scope.Id), CancellationToken.None);

        var visible = await service.ListAsync(scope.Id, null, 50, CancellationToken.None);
        Assert.Contains(visible.Items, item => item.Id == connector.Id);

        var other = await service.ListAsync(Guid.NewGuid(), null, 50, CancellationToken.None);
        Assert.DoesNotContain(other.Items, item => item.Id == connector.Id);

        var hidden = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.GetAsync(connector.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("connector_not_found", hidden.Code);
    }

    [Fact]
    public async Task Archive_BlocksWhileLeased_AndExposesOccupancy()
    {
        using var context = CreateContext();
        var service = new TeamLabConnectorService(context, Mock.Of<ITeamLabConnectorNodeGateway>());
        var connector = await service.RegisterAsync(Command(), CancellationToken.None);
        var runtime = await AddRuntimeAsync(context);
        await service.AcquireAsync(connector.Id, runtime.PublicId, null, CancellationToken.None);

        var leased = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.ArchiveAsync(connector.Id, CancellationToken.None));
        Assert.Equal("connector_leased", leased.Code);

        var model = await service.GetAsync(connector.Id, null, CancellationToken.None);
        Assert.Equal(1, model.OccupiedSlots);
        Assert.Equal(runtime.PublicId, Assert.Single(model.ActiveLeases).RuntimeId);
    }

    [Fact]
    public async Task List_ReadsManagedInterfaceStateFromItsNode()
    {
        using var context = CreateContext();
        var node = new WorkerNode { Name = "field-worker", HostAddress = "10.0.0.10" };
        context.WorkerNodes.Add(node);
        await context.SaveChangesAsync();
        var gateway = new Mock<ITeamLabConnectorNodeGateway>();
        gateway.Setup(item => item.GetInterfacesAsync(node.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TeamLabHostInterface("enp2s0", "02:00:00:00:00:01", true, ["10.10.0.2"])]);
        var service = new TeamLabConnectorService(context, gateway.Object);
        await service.RegisterAsync(new RegisterTeamLabConnectorModel(
            "field-nic", "现场网卡", "managed-nic", null, false, 1, null, null,
            new TeamLabManagedNicModel(node.Id, "enp2s0", "02:00:00:00:00:01")), CancellationToken.None);

        var connector = Assert.Single((await service.ListAsync(null, null, 50, CancellationToken.None)).Items);

        Assert.Equal("healthy", connector.Health);
        Assert.NotNull(connector.HealthObservedAt);
        gateway.Verify(item => item.GetInterfacesAsync(node.Id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
