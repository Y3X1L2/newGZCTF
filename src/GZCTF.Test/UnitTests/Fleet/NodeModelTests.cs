using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models.Data;
using GZCTF.Services.Fleet;
using GZCTF.Services.TeamLab;
using Microsoft.Extensions.Logging;
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
}

public class DeploymentTargetTests
{
    [Fact]
    public void DeploymentTarget_Defaults()
    {
        var target = new DeploymentTarget();
        Assert.Equal(TargetStatus.Pending, target.Status);
        Assert.Equal(TargetType.Docker, target.Type);
    }

    [Fact]
    public void VmCreatePayload_IncludesFlagForRemoteScheduling()
    {
        var payloadType = typeof(FleetVmService).GetNestedType("VmCreatePayload", BindingFlags.NonPublic);
        Assert.NotNull(payloadType);

        var payload = Activator.CreateInstance(payloadType,
            42,
            "/images/windows.qcow2",
            4096,
            2,
            "team-1-windows",
            "flag{vm_contract}",
            Guid.NewGuid(),
            1,
            Guid.NewGuid(),
            2);

        var json = JsonSerializer.Serialize(payload);

        Assert.Contains("\"Flag\":\"flag{vm_contract}\"", json);
    }

    [Fact]
    public void DeploymentTargetLogHelper_FormatsCompletedTargetWithoutPayload()
    {
        var target = new DeploymentTarget
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            TargetNodeId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Payload = "{\"Flag\":\"flag{secret}\",\"RegistryAuth\":\"secret-token\"}",
            Status = TargetStatus.Completed,
            ResultHost = "203.195.157.191",
            ResultPort = 30001
        };
        var node = new WorkerNode
        {
            Id = target.TargetNodeId.Value,
            Name = "node-1",
            HostAddress = "10.24.0.30"
        };

        var (message, status, level) = DeploymentTargetLogHelper.Build("completed", target, node);

        Assert.Equal(TaskStatus.Success, status);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("Deployment target 11111111-1111-1111-1111-111111111111 completed", message);
        Assert.Contains("Docker/Create", message);
        Assert.Contains("node-1", message);
        Assert.Contains("203.195.157.191:30001", message);
        Assert.DoesNotContain("flag{secret}", message);
        Assert.DoesNotContain("secret-token", message);
    }

    [Fact]
    public void DeploymentTargetLogHelper_MapsFailedAndCancelledStatuses()
    {
        var failed = new DeploymentTarget
        {
            Status = TargetStatus.Failed,
            Type = TargetType.Vm,
            Action = TargetAction.Create,
            ErrorMessage = "Agent VM creation failed"
        };
        var cancelled = new DeploymentTarget
        {
            Status = TargetStatus.Cancelled,
            Type = TargetType.Docker,
            Action = TargetAction.Create,
            ErrorMessage = "Target node was deregistered."
        };

        var (_, failedStatus, failedLevel) = DeploymentTargetLogHelper.Build("failed", failed);
        var (cancelledMessage, cancelledStatus, cancelledLevel) = DeploymentTargetLogHelper.Build("cancelled", cancelled);

        Assert.Equal(TaskStatus.Failed, failedStatus);
        Assert.Equal(LogLevel.Warning, failedLevel);
        Assert.Equal(TaskStatus.Exit, cancelledStatus);
        Assert.Equal(LogLevel.Information, cancelledLevel);
        Assert.Contains("Target node was deregistered.", cancelledMessage);
    }
}
