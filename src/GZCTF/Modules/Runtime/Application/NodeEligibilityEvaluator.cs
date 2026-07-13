using System.Globalization;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Services.Fleet;
using Microsoft.Extensions.Options;

namespace GZCTF.Modules.Runtime.Application;

public sealed class NodeEligibilityEvaluator(IOptions<RuntimeSchedulingOptions> options)
{
    readonly RuntimeSchedulingOptions _options = options.Value;

    public string? GetReason(NodeCapacitySnapshot snapshot, NodeCapability required, int dockerSlots,
        int vmSlots, bool requireTeamLab, IReadOnlyCollection<string>? requiredFeatures = null)
    {
        var node = snapshot.Node;
        if (node.GetEffectiveStatus(DateTimeOffset.UtcNow) != NodeStatus.Online)
            return "node_offline";
        if (!node.IsSchedulable)
            return "node_scheduling_disabled";
        if ((node.Capabilities & required) != required)
            return "node_capability_unavailable";
        if (!float.IsFinite(node.CpuLoad) || !float.IsFinite(node.MemoryLoad) ||
            node.CpuLoad < 0 || node.CpuLoad > 1 || node.MemoryLoad < 0 || node.MemoryLoad > 1 ||
            node.MaxContainers < 0 || node.MaxVms < 0)
            return "node_metrics_invalid";
        if (node.CpuLoad >= _options.CpuRejectThreshold)
            return "node_cpu_overloaded";
        if (node.MemoryLoad >= _options.MemoryRejectThreshold)
            return "node_memory_overloaded";
        if (requireTeamLab)
        {
            if (!AgentCapabilityEvaluator.Supports(node, AgentFeatureIds.TeamLabFabric))
                return "teamlab_fabric_capability_unavailable";
            if (!node.TeamLabNetworkEnabled)
                return "teamlab_network_disabled";
            if (node.TeamLabTunnelStatus != TeamLabTunnelStatus.Healthy ||
                node.TeamLabFabricStatus != TeamLabFabricStatus.Healthy)
                return "teamlab_fabric_unhealthy";
            if (!IsValidIpv4Address(node.TeamLabTunnelIp))
                return "teamlab_tunnel_ip_invalid";
        }
        if (requiredFeatures is { Count: > 0 } &&
            !AgentCapabilityEvaluator.Supports(node, requiredFeatures.ToArray()))
            return "agent_feature_unavailable";
        if (dockerSlots > snapshot.AvailableDocker || vmSlots > snapshot.AvailableVm)
            return "node_capacity_exhausted";
        return null;
    }

    public double Score(NodeCapacitySnapshot snapshot, int dockerSlots, int vmSlots)
    {
        var node = snapshot.Node;
        var dockerAfter = snapshot.AllocatedDocker + dockerSlots;
        var vmAfter = snapshot.AllocatedVm + vmSlots;
        var dockerUtilization = node.MaxContainers == 0 ? (dockerAfter == 0 ? 0 : 1) :
            (double)dockerAfter / node.MaxContainers;
        var vmUtilization = node.MaxVms == 0 ? (vmAfter == 0 ? 0 : 1) : (double)vmAfter / node.MaxVms;
        var absoluteHeadroom = Math.Min(32, Math.Max(0, node.MaxContainers - dockerAfter)) +
                               Math.Min(8, Math.Max(0, node.MaxVms - vmAfter)) * 4;
        return 1000 * (1 - Math.Clamp(node.CpuLoad, 0, 1)) +
               500 * (1 - Math.Clamp(node.MemoryLoad, 0, 1)) +
               200 * (1 - Math.Clamp(dockerUtilization, 0, 1)) +
               200 * (1 - Math.Clamp(vmUtilization, 0, 1)) +
               absoluteHeadroom;
    }

    static bool IsValidIpv4Address(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split('.');
        return parts.Length == 4 && parts.All(part => part.Length > 0 &&
            int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet) &&
            octet is >= 0 and <= 255);
    }
}
