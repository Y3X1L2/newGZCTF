using System.Net;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.GuestControl;

public sealed class GuestManagementNetworkService(
    IOptions<AgentConfig> options,
    TeamLabCommandExecutor executor,
    TeamLabCommandRunner runner,
    AgentResourceLock resourceLock)
{
    private readonly GuestManagementConfig _config = options.Value.GuestManagement;

    public async Task<TeamLabDryRunResponse> ApplyAsync(bool dryRun, CancellationToken cancellationToken)
    {
        Validate();
        await using var networkLock = await resourceLock.AcquireAsync("guest-control:network", cancellationToken);
        var bridge = TeamLabNetworkPrimitives.ShellQuote(_config.BridgeName);
        var cidr = TeamLabNetworkPrimitives.ShellQuote($"{_config.HostAddress}/{_config.PrefixLength}");
        var commands = new[]
        {
            $"ip link show {bridge} >/dev/null 2>&1 || ip link add {bridge} type bridge",
            $"ip address replace {cidr} dev {bridge}",
            $"ip link set {bridge} up",
            BuildNftCommand()
        };
        return await executor.ExecuteAsync(commands, dryRun, cancellationToken);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled) return false;
        Validate();
        var bridge = TeamLabNetworkPrimitives.ShellQuote(_config.BridgeName);
        var (bridgeReady, _) = await runner.RunAsync(
            $"ip -4 address show dev {bridge} | grep -F -- {TeamLabNetworkPrimitives.ShellQuote($"{_config.HostAddress}/{_config.PrefixLength}")}",
            cancellationToken);
        var (firewallReady, _) = await runner.RunAsync(
            "nft list table inet gzctf_guest_mgmt >/dev/null 2>&1", cancellationToken);
        return bridgeReady && firewallReady;
    }

    internal string[] BuildPlan()
    {
        Validate();
        var bridge = TeamLabNetworkPrimitives.ShellQuote(_config.BridgeName);
        var cidr = TeamLabNetworkPrimitives.ShellQuote($"{_config.HostAddress}/{_config.PrefixLength}");
        return
        [
            $"ip link show {bridge} >/dev/null 2>&1 || ip link add {bridge} type bridge",
            $"ip address replace {cidr} dev {bridge}",
            $"ip link set {bridge} up",
            BuildNftCommand()
        ];
    }

    private string BuildNftCommand()
    {
        var bridge = _config.BridgeName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $$"""
            nft -f - <<'GZCTF_NFT'
            destroy table inet gzctf_guest_mgmt
            table inet gzctf_guest_mgmt {
              chain input {
                type filter hook input priority -10; policy accept;
                iifname "{{bridge}}" ct state established,related accept
                iifname "{{bridge}}" udp dport 67 accept
                iifname "{{bridge}}" tcp dport {{_config.ListenPort}} accept
                iifname "{{bridge}}" drop
              }
              chain forward {
                type filter hook forward priority -10; policy accept;
                iifname "{{bridge}}" drop
                oifname "{{bridge}}" drop
              }
            }
            GZCTF_NFT
            """;
    }

    private void Validate()
    {
        if (TeamLabNetworkPrimitives.ValidateLinuxName(_config.BridgeName, nameof(_config.BridgeName)) is not null ||
            !IPAddress.TryParse(_config.HostAddress, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            _config.PrefixLength != 16 || _config.ListenPort is < 1 or > 65535)
            throw new InvalidOperationException("guest_management_configuration_invalid");
    }
}
