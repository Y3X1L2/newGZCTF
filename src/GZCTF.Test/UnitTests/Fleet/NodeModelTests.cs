using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TaskStatus = GZCTF.Utils.TaskStatus;

namespace GZCTF.Test.UnitTests.Fleet;

public class WorkerNodeTests
{
    [Fact]
    public void WorkerNode_DefaultsAreSetCorrectly()
    {
        var node = new WorkerNode();
        Assert.Equal(NodeCapability.Docker, node.Capabilities);
        Assert.Equal(NodeStatus.Unknown, node.Status);
        Assert.Equal(20, node.MaxContainers);
        Assert.Equal(5, node.MaxVms);
        Assert.Equal(28231, node.TotalPorts);
    }

    [Fact]
    public void NodeCapability_SupportsFlagCombination()
    {
        var combined = NodeCapability.Docker | NodeCapability.Kvm;
        Assert.True(combined.HasFlag(NodeCapability.Docker));
        Assert.True(combined.HasFlag(NodeCapability.Kvm));
    }

    [Fact]
    public void GetEffectiveStatus_RemoteOnlineNodeWithoutHeartbeat_IsOffline()
    {
        var node = new WorkerNode { Status = NodeStatus.Online, IsLocal = false, LastHeartbeat = null };

        var status = node.GetEffectiveStatus(DateTimeOffset.UtcNow);

        Assert.Equal(NodeStatus.Offline, status);
    }

    [Fact]
    public void GetEffectiveStatus_RemoteOnlineNodeWithStaleHeartbeat_IsOffline()
    {
        var node = new WorkerNode
        {
            Status = NodeStatus.Online,
            IsLocal = false,
            LastHeartbeat = DateTimeOffset.UtcNow - WorkerNode.DefaultHeartbeatTimeout - TimeSpan.FromSeconds(1)
        };

        var status = node.GetEffectiveStatus(DateTimeOffset.UtcNow);

        Assert.Equal(NodeStatus.Offline, status);
    }

    [Fact]
    public void GetEffectiveStatus_LocalOnlineNodeWithoutHeartbeat_StaysOnline()
    {
        var node = new WorkerNode { Status = NodeStatus.Online, IsLocal = true, LastHeartbeat = null };

        var status = node.GetEffectiveStatus(DateTimeOffset.UtcNow);

        Assert.Equal(NodeStatus.Online, status);
    }

    [Fact]
    public void ApplyLocalNodeRefresh_PreservesOperatorScheduling_WhenConfigIsUnset()
    {
        var node = new WorkerNode
        {
            IsLocal = true,
            IsSchedulable = true,
            HostAddress = "old-host",
            Capabilities = NodeCapability.Docker
        };

        LocalNodeRegistrar.ApplyLocalNodeRefresh(
            node,
            "10.24.0.27",
            NodeCapability.Docker | NodeCapability.Kvm,
            localSchedulableOverride: null,
            DateTimeOffset.Parse("2026-07-04T08:00:00Z"));

        Assert.True(node.IsSchedulable);
        Assert.Equal("10.24.0.27", node.HostAddress);
        Assert.Equal(NodeCapability.Docker | NodeCapability.Kvm, node.Capabilities);
        Assert.Equal(NodeStatus.Online, node.Status);
    }

    [Fact]
    public void ApplyLocalNodeRefresh_PreservesTeamLabTunnelState()
    {
        var handshake = DateTimeOffset.Parse("2026-07-04T07:55:00Z");
        var node = new WorkerNode
        {
            IsLocal = true,
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.24.0.27",
            TeamLabTunnelLastHandshake = handshake,
            TeamLabTunnelLastError = null,
            TeamLabTunnelConfigVersion = 3
        };

        LocalNodeRegistrar.ApplyLocalNodeRefresh(
            node,
            "10.24.0.27",
            NodeCapability.Docker,
            localSchedulableOverride: false,
            DateTimeOffset.Parse("2026-07-04T08:00:00Z"));

        Assert.True(node.TeamLabNetworkEnabled);
        Assert.Equal(TeamLabTunnelStatus.Healthy, node.TeamLabTunnelStatus);
        Assert.Equal("10.24.0.27", node.TeamLabTunnelIp);
        Assert.Equal(handshake, node.TeamLabTunnelLastHandshake);
        Assert.Null(node.TeamLabTunnelLastError);
        Assert.Equal(3, node.TeamLabTunnelConfigVersion);
        Assert.False(node.IsSchedulable);
    }

    [Fact]
    public void ApplyTeamLabDryRunProbe_PreservesAlreadyEnabledHealthyNode()
    {
        var node = new WorkerNode
        {
            TeamLabNetworkEnabled = true,
            TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
            TeamLabTunnelIp = "10.24.0.27",
            TeamLabTunnelLastError = null,
            TeamLabTunnelConfigVersion = 7
        };

        NodeTunnelService.ApplyDryRunProbeResult(node);

        Assert.True(node.TeamLabNetworkEnabled);
        Assert.Equal(TeamLabTunnelStatus.Healthy, node.TeamLabTunnelStatus);
        Assert.Equal("10.24.0.27", node.TeamLabTunnelIp);
        Assert.Null(node.TeamLabTunnelLastError);
        Assert.Equal(7, node.TeamLabTunnelConfigVersion);
    }

    [Fact]
    public async Task LocalNodeMetricsSampler_ReturnsNormalizedRatios()
    {
        var (cpuLoad, memoryLoad) = await LocalNodeMetricsService.SystemMetricsSampler.SampleAsync(CancellationToken.None);

        Assert.InRange(cpuLoad, 0f, 1f);
        Assert.InRange(memoryLoad, 0f, 1f);
    }

    [Fact]
    public async Task LocalNodeMetricsRefresh_CountsRunningTeamLabAssetsAsActiveCapacity()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddLogging();
        services.AddSingleton<IDistributedLeaseProvider>(
            _ => new LocalDevelopmentLeaseProvider());
        var liveStateStore = new RecordingNodeLiveStateStore();
        services.AddSingleton<INodeLiveStateStore>(liveStateStore);
        services.AddScoped<FleetCapacityReservationService>();
        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var nodeId = Guid.NewGuid();
            var game = new Game
            {
                Id = 7,
                Title = "teamlab",
                StartTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                EndTimeUtc = DateTimeOffset.UtcNow.AddHours(1)
            };
            var team = new Team { Id = 11, Name = "team-a" };

            context.WorkerNodes.Add(new WorkerNode
            {
                Id = nodeId,
                Name = "local",
                HostAddress = "10.24.0.27",
                Status = NodeStatus.Online,
                IsLocal = true,
                Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
                CurrentContainers = 0,
                CurrentVms = 0
            });
            context.Games.Add(game);
            context.Teams.Add(team);
            context.Containers.Add(new Container
            {
                Id = Guid.NewGuid(),
                Image = "alpine:latest",
                ContainerId = "normal-running",
                Status = ContainerStatus.Running,
                NodeId = nodeId,
                IP = "127.0.0.1",
                Port = 80
            });
            context.TeamLabRuntimes.AddRange(
                new TeamLabRuntime
                {
                    Id = 21,
                    Status = TeamLabRuntimeStatus.Running,
                    Assets =
                    [
                        new TeamLabRuntimeAsset
                        {
                            Name = "docker-running",
                            Kind = TeamLabResourceKind.Docker,
                            WorkerNodeId = nodeId,
                            Status = TeamLabRuntimeStatus.Running
                        },
                        new TeamLabRuntimeAsset
                        {
                            Name = "vm-running",
                            Kind = TeamLabResourceKind.Vm,
                            WorkerNodeId = nodeId,
                            Status = TeamLabRuntimeStatus.Running
                        },
                        new TeamLabRuntimeAsset
                        {
                            Name = "docker-destroyed",
                            Kind = TeamLabResourceKind.Docker,
                            WorkerNodeId = nodeId,
                            Status = TeamLabRuntimeStatus.Destroyed
                        }
                    ]
                },
                new TeamLabRuntime
                {
                    Id = 22,
                    Status = TeamLabRuntimeStatus.Destroyed,
                    Assets =
                    [
                        new TeamLabRuntimeAsset
                        {
                            Name = "docker-runtime-destroyed",
                            Kind = TeamLabResourceKind.Docker,
                            WorkerNodeId = nodeId,
                            Status = TeamLabRuntimeStatus.Running
                        }
                    ]
                });
            await context.SaveChangesAsync();
        }

        Assert.True(await LocalNodeMetricsService.RefreshLocalNodeMetricsAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            CancellationToken.None));

        Assert.NotNull(liveStateStore.State);
        Assert.Equal(2, liveStateStore.State.CurrentContainers);
        Assert.Equal(1, liveStateStore.State.CurrentVms);
    }

    private sealed class RecordingNodeLiveStateStore : INodeLiveStateStore
    {
        public NodeLiveState? State { get; private set; }
        public TimeSpan FreshnessTtl => TimeSpan.FromSeconds(120);

        public ValueTask<NodeLiveStateWriteResult> WriteAsync(NodeLiveState state,
            CancellationToken cancellationToken = default)
        {
            State = state;
            return ValueTask.FromResult(NodeLiveStateWriteResult.Stored);
        }

        public ValueTask<NodeLiveState?> GetAsync(Guid workerNodeId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(State);

        public ValueTask<IReadOnlyDictionary<Guid, NodeLiveState>> GetManyAsync(
            IReadOnlyCollection<Guid> workerNodeIds,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<Guid, NodeLiveState>>(
                State is null
                    ? new Dictionary<Guid, NodeLiveState>()
                    : new Dictionary<Guid, NodeLiveState> { [State.WorkerNodeId] = State });
    }
}
