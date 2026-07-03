using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using Microsoft.Extensions.Options;

namespace GZCTF.Services.TeamLab;

public interface IPublicUdpGatewayProvider
{
    Task<PublicUdpGatewaySyncResult> SyncMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token);
    Task<PublicUdpGatewaySyncResult> RemoveMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token);
}

public sealed record PublicUdpGatewaySyncResult(bool Success, string Message, string[] Commands);

public class PublicUdpGatewayProvider(
    IOptions<PublicUdpGatewayConfig> options,
    ILogger<PublicUdpGatewayProvider> logger) : IPublicUdpGatewayProvider
{
    private readonly PublicUdpGatewayConfig _config = options.Value;

    public Task<PublicUdpGatewaySyncResult> SyncMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token)
    {
        var validation = Validate(mapping);
        if (validation is not null)
            return Task.FromResult(new PublicUdpGatewaySyncResult(false, validation, []));

        var commands = BuildSyncCommands(mapping);
        if (!_config.Enable)
        {
            mapping.IsSynced = false;
            return Task.FromResult(new PublicUdpGatewaySyncResult(true, "Dry-run public UDP gateway mapping generated.", commands));
        }

        logger.LogWarning("PublicUdpGatewayConfig.Enable=true but Phase 0-3 provider is command-plan only. Provider={Provider}",
            _config.Provider);
        mapping.IsSynced = false;
        mapping.LastSyncError = "Public UDP gateway execution is not enabled in Phase 0-3.";
        return Task.FromResult(new PublicUdpGatewaySyncResult(false, mapping.LastSyncError, commands));
    }

    public Task<PublicUdpGatewaySyncResult> RemoveMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token)
    {
        var validation = Validate(mapping);
        if (validation is not null)
            return Task.FromResult(new PublicUdpGatewaySyncResult(false, validation, []));

        var commands = BuildRemoveCommands(mapping);
        mapping.IsSynced = false;
        return Task.FromResult(new PublicUdpGatewaySyncResult(true, "Dry-run public UDP gateway removal generated.", commands));
    }

    private string[] BuildSyncCommands(TeamLabPublicUdpMapping mapping) =>
        string.Equals(_config.Provider, "iptables", StringComparison.OrdinalIgnoreCase)
            ?
            [
                $"{_config.IptablesBinaryPath} -t nat -A PREROUTING -p udp --dport {mapping.PublicUdpPort} -j DNAT --to-destination {mapping.WorkerTunnelIp}:{mapping.WorkerWireGuardPort}",
                $"{_config.IptablesBinaryPath} -t nat -A POSTROUTING -p udp -d {mapping.WorkerTunnelIp} --dport {mapping.WorkerWireGuardPort} -j MASQUERADE"
            ]
            :
            [
                $"{_config.NftBinaryPath} add rule {_config.NftTable} prerouting udp dport {mapping.PublicUdpPort} dnat to {mapping.WorkerTunnelIp}:{mapping.WorkerWireGuardPort}",
                $"{_config.NftBinaryPath} add rule {_config.NftTable} postrouting ip daddr {mapping.WorkerTunnelIp} udp dport {mapping.WorkerWireGuardPort} masquerade"
            ];

    private string[] BuildRemoveCommands(TeamLabPublicUdpMapping mapping) =>
    [
        $"# remove public UDP mapping {mapping.PublicUdpPort} -> {mapping.WorkerTunnelIp}:{mapping.WorkerWireGuardPort}"
    ];

    private static string? Validate(TeamLabPublicUdpMapping mapping)
    {
        if (mapping.PublicUdpPort is <= 0 or > ushort.MaxValue)
            return "Invalid public UDP port.";

        if (mapping.WorkerWireGuardPort is <= 0 or > ushort.MaxValue)
            return "Invalid Worker WireGuard port.";

        if (string.IsNullOrWhiteSpace(mapping.WorkerTunnelIp))
            return "Worker tunnel IP is required.";

        return null;
    }
}
