using GZCTF.Agent.Models;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabHostGatewayProvider(
    TeamLabCommandRunner runner,
    IOptions<AgentTeamLabConfig> options)
{
    readonly AgentTeamLabConfig config = options.Value;

    public async Task<TeamLabAttachmentResult> ApplyAsync(
        TeamLabExecutionPlanV2 plan,
        TeamLabNetworkIntentV2 network,
        CancellationToken token)
    {
        var gateway = network.HostGateway!;
        var prefix = network.Cidr[(network.Cidr.LastIndexOf('/') + 1)..];
        var logicalPort = TeamLabOvnNaming.LogicalPortId(plan, network.Key, gateway.PortKey);
        var commands = new[]
        {
            $"ovs-vsctl --may-exist add-port {config.OvsIntegrationBridgeName} {gateway.InterfaceName} -- " +
            $"set Interface {gateway.InterfaceName} type=internal " +
            $"mac='\"{gateway.MacAddress}\"' " +
            $"external_ids:iface-id={logicalPort} external_ids:gzctf-runtime={plan.RuntimePublicId:D} " +
            $"external_ids:gzctf-generation={plan.Generation} external_ids:gzctf-network-key={network.Key}",
            $"ip link set dev {gateway.InterfaceName} address {gateway.MacAddress}",
            $"ip addr replace {gateway.IpAddress}/{prefix} dev {gateway.InterfaceName}",
            $"ip link set dev {gateway.InterfaceName} up"
        };
        foreach (var command in commands)
        {
            var result = await runner.RunAsync(command, token);
            if (!result.Success)
                return TeamLabAttachmentResult.Failed("network", result.Output);
        }
        return new TeamLabAttachmentResult(true, "Host gateway attached.");
    }

    public async Task<TeamLabAttachmentResult> ProbeAsync(
        TeamLabExecutionPlanV2 plan,
        TeamLabNetworkIntentV2 network,
        CancellationToken token)
    {
        var gateway = network.HostGateway!;
        var prefix = network.Cidr[(network.Cidr.LastIndexOf('/') + 1)..];
        var logicalPort = TeamLabOvnNaming.LogicalPortId(plan, network.Key, gateway.PortKey);
        var command = $"test \"$(ovs-vsctl --data=bare --no-heading get Interface {gateway.InterfaceName} external_ids:iface-id)\" = {logicalPort} && " +
                      $"test \"$(cat /sys/class/net/{gateway.InterfaceName}/address)\" = {gateway.MacAddress.ToLowerInvariant()} && " +
                      $"ip -o -4 addr show dev {gateway.InterfaceName} | grep -F ' {gateway.IpAddress}/{prefix} ' >/dev/null";
        var result = await runner.RunAsync(command, token);
        return result.Success
            ? new TeamLabAttachmentResult(true, "Host gateway is present.")
            : TeamLabAttachmentResult.Failed("network", "Host gateway is missing from the datapath.");
    }

    public async Task<TeamLabAttachmentResult> RemoveAsync(
        TeamLabNetworkIntentV2 network,
        CancellationToken token)
    {
        var result = await runner.RunAsync(
            $"ovs-vsctl --if-exists del-port {config.OvsIntegrationBridgeName} {network.HostGateway!.InterfaceName}", token);
        return result.Success
            ? new TeamLabAttachmentResult(true, "Host gateway removed.")
            : TeamLabAttachmentResult.Failed("cleanup", result.Output);
    }
}
