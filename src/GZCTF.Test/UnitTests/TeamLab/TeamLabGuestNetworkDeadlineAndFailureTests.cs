using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Services.Vm;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Modules.TeamLab.Infrastructure;
using GZCTF.Services.Fleet;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Moq;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabGuestNetworkDeadlineAndFailureTests
{
    [Theory]
    [InlineData(TeamLabGuestNetworkMode.Dhcp)]
    [InlineData(TeamLabGuestNetworkMode.Preconfigured)]
    public void LegacyMode_KeepsExistingDeadline(TeamLabGuestNetworkMode mode)
    {
        var plan = Plan(Asset("one", mode), Asset("two", mode), Asset("three", mode));
        Assert.Equal(TimeSpan.FromSeconds(390), AgentTeamLabNodeExecutor.ExecutionPlanDeadline(plan, null));
        Assert.Equal(TimeSpan.FromSeconds(270), AgentTeamLabNodeExecutor.ExecutionPlanDeadline(plan, Limits(2)));
        Assert.Equal(TimeSpan.FromSeconds(150), AgentTeamLabNodeExecutor.ExecutionPlanDeadline(plan, Limits(8)));
    }

    [Theory]
    [InlineData(TeamLabGuestOperatingSystem.Windows)]
    [InlineData(TeamLabGuestOperatingSystem.Linux)]
    public void ManagedDeadline_CoversActualAgentPhaseBoundsAndCompensation(TeamLabGuestOperatingSystem os)
    {
        var asset = Asset("managed", TeamLabGuestNetworkMode.ManagedStatic, os);
        var guest = new TeamLabVmNetworkService(null!);
        var required = TimeSpan.FromSeconds(120 + 30 + 120) + guest.ReadyTimeout + guest.VerifyTimeout +
            TimeSpan.FromSeconds(TeamLabVmNetworkService.BuildReadCommand(asset, []).TimeoutSeconds +
                                 TeamLabVmNetworkService.BuildApplyCommand(asset, []).TimeoutSeconds);
        var deadline = AgentTeamLabNodeExecutor.ExecutionPlanDeadline(Plan(asset), null);
        Assert.True(deadline >= required, $"HTTP deadline {deadline} is shorter than bounded Agent phases {required}.");
        Assert.Equal(TimeSpan.FromSeconds(150), AgentTeamLabNodeExecutor.ExecutionPlanDeadline(Plan(asset), null, includeGuestNetwork: false));
    }

    [Fact]
    public void MixedPlan_AccountsForOsAndConservativeAssetParallelism()
    {
        var linux = Asset("linux", TeamLabGuestNetworkMode.ManagedStatic, TeamLabGuestOperatingSystem.Linux);
        var windows = Asset("windows", TeamLabGuestNetworkMode.ManagedStatic, TeamLabGuestOperatingSystem.Windows);
        var dhcp = Asset("dhcp", TeamLabGuestNetworkMode.Dhcp);
        Assert.Equal(TimeSpan.FromSeconds(1390), AgentTeamLabNodeExecutor.ExecutionPlanDeadline(Plan(linux, windows, dhcp), Limits(1)));
        Assert.Equal(TimeSpan.FromSeconds(1270), AgentTeamLabNodeExecutor.ExecutionPlanDeadline(Plan(linux, windows, dhcp), Limits(2)));
        Assert.Equal(AgentTeamLabNodeExecutor.ExecutionPlanDeadline(Plan(linux, windows, dhcp), Limits(2)),
            AgentTeamLabNodeExecutor.ExecutionPlanDeadline(Plan(linux, windows, dhcp), Limits(32)));
    }

    [Fact]
    public async Task AgentGuestFailure_ReachesQueueProjectionWithoutRawOutput()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var node = new WorkerNode { Name = "worker", TeamLabFabricIp = "10.250.0.10" };
        node.CapabilityManifestJson = AgentCapabilityEvaluator.Normalize(new AgentCapabilityManifest("new", null, 1,
            [AgentFeatureIds.TeamLabExecutionPlan, AgentFeatureIds.TeamLabOvnOvs, AgentFeatureIds.TeamLabArtifactCache,
             AgentFeatureIds.TeamLabNativeLibvirt, AgentFeatureIds.TeamLabManagedGuestNetwork], Limits(1),
            new(4, 4L * 1024 * 1024 * 1024), DateTimeOffset.UtcNow)).Json;
        var template = new ImageTemplate { Id = 1, Name = "windows", ImageType = ImageType.Qcow2, OSType = OSType.Windows,
            VmNetworkMode = VmNetworkMode.ManagedStatic, ImageHash = "sha256:" + new string('a', 64) };
        var definition = TeamLabTopologyV2Compiler.Compile(new TeamLabTopologyDefinitionModel("guest negative",
            [new("entry", "Entry", new("10.48.0.0/16", 24), true)],
            [new("vm", "VM", TeamLabAssetKind.Vm, 1, new(2, 1024, 20480),
                [new("nic", "entry", 10, true, UseDefaultGateway: false)], VmNetworkMode: VmNetworkMode.ManagedStatic)], []));
        var runtime = new TeamLabRuntime { Id = 42, Generation = 1, Status = TeamLabRuntimeStatus.Deploying,
            Shards = [new() { Id = 7, Generation = 1, WorkerNodeId = node.Id }],
            Networks = [new() { Id = 9, Generation = 1, ShardId = 7, WorkerNodeId = node.Id, TopologyKey = "entry",
                Cidr = "10.48.0.0/24", GatewayIp = "10.48.0.1", BridgeName = "br-entry", IsEntry = true }],
            Assets = [new() { Id = 11, Generation = 1, ShardId = 7, WorkerNodeId = node.Id, TopologyKey = "vm", Name = "VM",
                Kind = TeamLabResourceKind.Vm, SourceTemplateId = 1, ImageDigest = template.ImageHash,
                ExecutionPlanJson = JsonSerializer.Serialize(definition.Assets[0]),
                InterfaceSummaryJson = JsonSerializer.Serialize(new[] {new {Key="nic",NetworkKey="entry",IpAddress="10.48.0.10",
                    PrefixLength=24,MacAddress="02:00:00:00:00:01",Primary=true}}) }] };
        context.ImageTemplates.Add(template); context.WorkerNodes.Add(node); context.TeamLabRuntimes.Add(runtime);
        context.TeamLabFabricLinkLeases.Add(new() { RuntimeId = 42, Generation = 1, ShardId = 7, WorkerNodeId = node.Id,
            AllocatedCidr = new IPNetwork(IPAddress.Parse("169.254.0.0"), 30), HubAddress = "169.254.0.1", NodeAddress = "169.254.0.2" });
        await context.SaveChangesAsync();
        const string unsafeOutput = "password=DO_NOT_EXPOSE full user-data=PRIVATE_PAYLOAD";
        var executor = new Mock<ITeamLabNodeExecutor>();
        executor.Setup(item => item.ApplyExecutionPlanAsync(node.Id, It.IsAny<TeamLabExecutionPlanV2>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, TeamLabExecutionPlanV2 plan, CancellationToken _) => new(false, false, plan.PlanDigest,
                [new(plan.RuntimeId, plan.RuntimePublicId, 1, plan.ShardKey, "vm", "guest-ready", "failed", "guest-ready",
                    "guest_qga_unavailable", DateTimeOffset.UtcNow, new Dictionary<string,string> { ["summary"] = unsafeOutput })], [],
                "guest-ready", "guest_qga_unavailable", unsafeOutput));
        executor.Setup(item => item.CleanupExecutionPlanAsync(node.Id, It.IsAny<TeamLabExecutionPlanV2>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, TeamLabExecutionPlanV2 plan, CancellationToken _) => new(true, plan.PlanDigest, [], []));
        var artifacts = new Mock<ITeamLabArtifactDistribution>();
        artifacts.Setup(item => item.EnsureImageAsync(42, node.Id, It.IsAny<ImageTemplate>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await using var provider = new ServiceCollection().AddSingleton(artifacts.Object).BuildServiceProvider();
        var service = new TeamLabShardDeploymentService(context, provider.GetRequiredService<IServiceScopeFactory>(), executor.Object, null!,
            new(context, executor.Object, null!), null!, Mock.Of<ITeamLabDeploymentProgress>(), NullLogger<TeamLabShardDeploymentService>.Instance);

        var exception = await Assert.ThrowsAsync<TeamLabGuestNetworkExecutionException>(() => service.DeployAsync(runtime, definition,
            new Dictionary<string,TeamLabRuntimeOverlayModel>(), default));
        executor.Verify(item => item.CleanupExecutionPlanAsync(node.Id, It.IsAny<TeamLabExecutionPlanV2>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("guest-ready", exception.Stage);
        Assert.DoesNotContain("DO_NOT_EXPOSE", exception.Message);
        Assert.DoesNotContain("PRIVATE_PAYLOAD", exception.Message);
        var ticket = new DeploymentQueueTicket { TargetNodeId = node.Id, Stage = DeploymentStage.Failed };
        var error = RuntimeOperationalEvents.Failure(ticket, "runtime.execute", exception);
        Assert.Equal("guest_qga_unavailable", error.Code);
        Assert.Equal(OperationalErrorCategory.AgentProtocol, error.Category);
        Assert.Equal("teamlab.guest-ready", error.Operation);
        ticket.ErrorCategory = error.Category; ticket.ErrorCode = error.Code; ticket.Retryable = error.Retryable;
        var projection = TeamLabFailurePresentation.ForRuntime(TeamLabRuntimeStatus.Failed, ticket, runtime.PublicId)!;
        Assert.Equal("guest_qga_unavailable", projection.Code);
        Assert.Equal("guest-ready", projection.Stage);
        Assert.Contains("QGA", projection.Detail);
        Assert.DoesNotContain("PRIVATE_PAYLOAD", projection.Detail);
    }

    [Fact]
    public void UnknownGuestCode_RemainsSafeAndKeepsReportedStage()
    {
        var error = TeamLabGuestNetworkExecutionException.FromAgent("guest-network-apply", "untrusted raw payload", Guid.NewGuid())!;
        Assert.Equal("guest-network-apply", error.Stage);
        Assert.Equal("guest_network_control_failed", error.Error.Code);
        Assert.DoesNotContain("untrusted", error.Message);
    }

    static AgentExecutionLimits Limits(int concurrency) => new(1, 1, 1, 1, TeamLabExecutionOperations: concurrency);
    static TeamLabAssetExecutionSpecV2 Asset(string key, TeamLabGuestNetworkMode mode,
        TeamLabGuestOperatingSystem os = TeamLabGuestOperatingSystem.Windows) =>
        new(key, "vm", key, "sha256:" + new string('a', 64), key, 1, 2, 1024, [], [], OperatingSystem: os, NetworkMode: mode);
    static TeamLabExecutionPlanV2 Plan(params TeamLabAssetExecutionSpecV2[] assets) =>
        new(42, Guid.NewGuid(), 1, "shard", "sha256:" + new string('a',64), "sha256:" + new string('b',64), true, [], assets, []);
}
