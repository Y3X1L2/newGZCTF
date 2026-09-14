using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using GZCTF.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabServiceAccessTests
{
    [Fact]
    public async Task AgentRulesUseAccessIdentityAndReportRemovalFailure()
    {
        var runner = new RecordingRunner();
        var service = new GZCTF.Agent.Services.TeamLab.TeamLabServiceAccessService(runner);
        var request = new TeamLabServiceForwardRequest(
            Guid.NewGuid(), 7, 2, "tcp", 32012, "10.96.0.12", 8080);

        await service.ApplyAsync(request, default);

        Assert.Contains(runner.Commands, command => command.Contains($"gzctf-teamlab-service-{request.AccessId:N}"));
        Assert.Contains(runner.Commands, command => command.Contains("tcp dport 32012 dnat ip to 10.96.0.12:8080"));
        runner.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveAsync(request, default));
    }

    [Fact]
    public async Task CreateAndRemove_UsesExistingPortLeaseAndBothGateways()
    {
        await using var context = Context();
        var (runtime, asset, node) = await SeedAsync(context);
        var leaseId = Guid.NewGuid();
        var ports = new Mock<IPortAllocationService>(MockBehavior.Strict);
        ports.Setup(item => item.AllocatePortAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortLease(32010, leaseId, DateTimeOffset.UtcNow.AddMinutes(5)));
        ports.Setup(item => item.ReleasePortAsync(32010, leaseId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var worker = new Mock<ITeamLabServiceAccessGateway>(MockBehavior.Strict);
        worker.Setup(item => item.ApplyAsync(node.Id,
            It.Is<TeamLabServiceForwardRequest>(request => request.TargetAddress == asset.IpAddress &&
                request.TargetPort == 8080 && request.ListenPort == 32010 && request.Protocol == "tcp"),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        worker.Setup(item => item.RemoveAsync(node.Id, It.IsAny<TeamLabServiceForwardRequest>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var gateway = Gateway(success: true);
        var service = Service(context, ports.Object, gateway.Object, worker.Object);

        var created = await service.CreateAsync(runtime.PublicId, asset.Id,
            new CreateTeamLabServiceAccessModel("tcp", 8080, NetworkKey: "office"), default);

        Assert.Equal("active", created.Status);
        Assert.Equal("gateway.example:32010", created.Endpoint);
        var listed = await service.ListAsync(runtime.PublicId, default);
        Assert.Single(listed);
        var removed = await service.RemoveAsync(runtime.PublicId, created.Id, default);
        Assert.Equal("revoked", removed.Status);
        Assert.NotNull(removed.RevokedAt);
        worker.VerifyAll();
        ports.VerifyAll();
        gateway.VerifyAll();
    }

    [Fact]
    public async Task GatewayFailure_RemovesWorkerRuleAndReleasesPort()
    {
        await using var context = Context();
        var (runtime, asset, node) = await SeedAsync(context);
        var leaseId = Guid.NewGuid();
        var ports = new Mock<IPortAllocationService>();
        ports.Setup(item => item.AllocatePortAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PortLease(32011, leaseId, DateTimeOffset.UtcNow.AddMinutes(5)));
        ports.Setup(item => item.ReleasePortAsync(32011, leaseId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var worker = new Mock<ITeamLabServiceAccessGateway>();
        worker.Setup(item => item.ApplyAsync(node.Id, It.IsAny<TeamLabServiceForwardRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        worker.Setup(item => item.RemoveAsync(node.Id, It.IsAny<TeamLabServiceForwardRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = Service(context, ports.Object, Gateway(success: false).Object, worker.Object);

        var created = await service.CreateAsync(runtime.PublicId, asset.Id,
            new CreateTeamLabServiceAccessModel("udp", 502, NetworkKey: "office"), default);

        Assert.Equal("failed", created.Status);
        Assert.NotNull(created.RevokedAt);
        worker.Verify(item => item.RemoveAsync(node.Id, It.IsAny<TeamLabServiceForwardRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        ports.Verify(item => item.ReleasePortAsync(32011, leaseId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TeamLabServiceAccessService Service(AppDbContext context, IPortAllocationService ports,
        IPublicUdpGatewayProvider gateway, ITeamLabServiceAccessGateway worker) =>
        new(context, new TeamLabScopeAuthorizationService(context), ports, gateway, worker,
            Options.Create(new PublicUdpGatewayConfig { PublicEndpoint = "gateway.example" }));

    private static Mock<IPublicUdpGatewayProvider> Gateway(bool success)
    {
        var gateway = new Mock<IPublicUdpGatewayProvider>(MockBehavior.Strict);
        gateway.Setup(item => item.SyncServiceAsync(It.IsAny<PublicServiceGatewayMapping>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicUdpGatewaySyncResult(success, success ? "ok" : "gateway failed", []));
        if (success)
            gateway.Setup(item => item.RemoveServiceAsync(It.IsAny<PublicServiceGatewayMapping>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PublicUdpGatewaySyncResult(true, "removed", []));
        return gateway;
    }

    private static async Task<(TeamLabRuntime Runtime, TeamLabRuntimeAsset Asset, WorkerNode Node)> SeedAsync(AppDbContext context)
    {
        var node = new WorkerNode { Name = "worker-a", HostAddress = "10.0.0.10", TeamLabTunnelIp = "10.250.0.10" };
        var runtime = new TeamLabRuntime { Status = TeamLabRuntimeStatus.Running };
        var asset = new TeamLabRuntimeAsset
        {
            Runtime = runtime, Generation = runtime.Generation, Name = "Web", TopologyKey = "web",
            NetworkKey = "office", IpAddress = "10.96.0.10", WorkerNodeId = node.Id, WorkerNode = node,
            Status = TeamLabRuntimeStatus.Running
        };
        context.AddRange(node, runtime, asset);
        await context.SaveChangesAsync();
        return (runtime, asset, node);
    }

    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase($"service-access-{Guid.NewGuid():N}").Options);

    private sealed class RecordingRunner() : GZCTF.Agent.Services.TeamLabCommandRunner(
        NullLogger<GZCTF.Agent.Services.TeamLabCommandRunner>.Instance)
    {
        public List<string> Commands { get; } = [];
        public bool Fail { get; set; }

        public override Task<(bool Success, string Output)> RunAsync(string command, CancellationToken token)
        {
            Commands.Add(command);
            return Task.FromResult(Fail ? (false, "delete failed") : (true, string.Empty));
        }
    }
}
