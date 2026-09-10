using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class TeamLabManagedNicProvider(TeamLabCommandRunner runner, TeamLabOvsAttachmentProvider ovs)
{
    public async Task<TeamLabAttachmentResult> AttachAsync(TeamLabExecutionPlanV2 plan, string networkKey,
        TeamLabConnectorAttachmentV2 connector, CancellationToken token)
    {
        if (!connector.IsValid()) return new(false, "Invalid managed NIC identity.");
        var existing = await ovs.ProbeAsync(plan, connector.InterfaceName, networkKey, connector.PortKey, token);
        var (ready, _) = await runner.RunAsync(PrepareCommand(connector, existing.Success), token);
        if (!ready) return new(false, "Dedicated NIC must match its registered MAC, be up, and have no host addresses or bridge master.");
        return await ovs.AttachAsync(plan, connector.InterfaceName, networkKey, connector.PortKey, token);
    }

    public async Task<TeamLabAttachmentResult> ProbeAsync(TeamLabExecutionPlanV2 plan, string networkKey,
        TeamLabConnectorAttachmentV2 connector, CancellationToken token)
    {
        if (!connector.IsValid()) return new(false, "Invalid managed NIC identity.");
        var (ready, _) = await runner.RunAsync(PrepareCommand(connector, attached: true), token);
        return ready ? await ovs.ProbeAsync(plan, connector.InterfaceName, networkKey, connector.PortKey, token)
            : new(false, "Dedicated NIC identity or link is unavailable.");
    }

    public Task<TeamLabAttachmentResult> DetachAsync(TeamLabExecutionPlanV2 plan, string networkKey,
        TeamLabConnectorAttachmentV2 connector, CancellationToken token) =>
        ovs.RemoveAsync(plan, connector.InterfaceName, networkKey, token);

    internal static string PrepareCommand(TeamLabConnectorAttachmentV2 connector, bool attached = false)
    {
        if (!connector.IsValid()) throw new ArgumentException("Invalid managed NIC identity.");
        var name = TeamLabNetworkPrimitives.ShellQuote(connector.InterfaceName);
        var path = "/sys/class/net/" + connector.InterfaceName;
        return $"test \"$(cat {path}/address)\" = {TeamLabNetworkPrimitives.ShellQuote(connector.MacAddress.ToLowerInvariant())} && " +
            $"test \"$(cat {path}/operstate)\" = up && " + (attached ? "" : $"test ! -e {path}/master && ") +
            $"test -z \"$(ip -o addr show dev {name} scope global)\" && " +
            $"test -z \"$(ip route show default dev {name})\" && test -z \"$(ip -6 route show default dev {name})\"";
    }
}
