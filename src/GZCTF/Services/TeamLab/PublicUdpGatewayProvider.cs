using System.Diagnostics;
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

    public async Task<PublicUdpGatewaySyncResult> SyncMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token)
    {
        var validation = Validate(mapping);
        if (validation is not null)
            return new PublicUdpGatewaySyncResult(false, validation, []);

        var commands = BuildSyncCommands(mapping);
        if (!_config.Enable)
        {
            mapping.IsSynced = false;
            mapping.LastSyncError = "Public UDP gateway synchronization is not enabled.";
            return new PublicUdpGatewaySyncResult(false, mapping.LastSyncError, commands);
        }

        foreach (var command in commands)
        {
            var result = await RunCommandAsync(command, token);
            if (!result.Success && !IsBestEffortRemove(command))
            {
                LogCommandFailure(command, result.Output);
                mapping.IsSynced = false;
                mapping.LastSyncError = result.Output;
                return new PublicUdpGatewaySyncResult(false, result.Output, commands);
            }
        }

        mapping.IsSynced = true;
        mapping.LastSyncError = null;
        mapping.RuleVersion++;
        return new PublicUdpGatewaySyncResult(true, "Public UDP gateway mapping synchronized.", commands);
    }

    public async Task<PublicUdpGatewaySyncResult> RemoveMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token)
    {
        var validation = Validate(mapping);
        if (validation is not null)
            return new PublicUdpGatewaySyncResult(false, validation, []);

        var commands = BuildRemoveCommands(mapping);
        if (!_config.Enable)
        {
            mapping.IsSynced = false;
            return new PublicUdpGatewaySyncResult(true, "Public UDP gateway is disabled; mapping marked unsynced.", commands);
        }

        foreach (var command in commands.Where(command => !command.StartsWith('#')))
        {
            var result = await RunCommandAsync(command, token);
            if (!result.Success && ShouldWarnForCommandFailure(command, result.Output))
                LogCommandFailure(command, result.Output);
        }

        mapping.IsSynced = false;
        return new PublicUdpGatewaySyncResult(true, "Public UDP gateway mapping removal attempted.", commands);
    }

    private string[] BuildSyncCommands(TeamLabPublicUdpMapping mapping)
    {
        if (string.Equals(_config.Provider, "iptables", StringComparison.OrdinalIgnoreCase))
        {
            var prerouting =
                $"-t nat PREROUTING -p udp --dport {mapping.PublicUdpPort} -j DNAT --to-destination {mapping.WorkerTunnelIp}:{mapping.WorkerWireGuardPort}";
            var postrouting =
                $"-t nat POSTROUTING -p udp -d {mapping.WorkerTunnelIp} --dport {mapping.WorkerWireGuardPort} -j MASQUERADE";
            return
            [
                $"{_config.IptablesBinaryPath} {prerouting.Replace("-t nat PREROUTING", "-t nat -D PREROUTING")}",
                $"{_config.IptablesBinaryPath} {postrouting.Replace("-t nat POSTROUTING", "-t nat -D POSTROUTING")}",
                $"{_config.IptablesBinaryPath} {prerouting.Replace("-t nat PREROUTING", "-t nat -A PREROUTING")}",
                $"{_config.IptablesBinaryPath} {postrouting.Replace("-t nat POSTROUTING", "-t nat -A POSTROUTING")}"
            ];
        }

        var comment = BuildNftComment(mapping);
        return
        [
            BuildNftRemoveCommand(comment),
            $"{_config.NftBinaryPath} add rule {_config.NftTable} prerouting udp dport {mapping.PublicUdpPort} dnat ip to {mapping.WorkerTunnelIp}:{mapping.WorkerWireGuardPort} comment \"{comment}\"",
            $"{_config.NftBinaryPath} add rule {_config.NftTable} postrouting ip daddr {mapping.WorkerTunnelIp} udp dport {mapping.WorkerWireGuardPort} masquerade comment \"{comment}\""
        ];
    }

    private string[] BuildRemoveCommands(TeamLabPublicUdpMapping mapping)
    {
        if (string.Equals(_config.Provider, "iptables", StringComparison.OrdinalIgnoreCase))
        {
            var prerouting =
                $"-t nat PREROUTING -p udp --dport {mapping.PublicUdpPort} -j DNAT --to-destination {mapping.WorkerTunnelIp}:{mapping.WorkerWireGuardPort}";
            var postrouting =
                $"-t nat POSTROUTING -p udp -d {mapping.WorkerTunnelIp} --dport {mapping.WorkerWireGuardPort} -j MASQUERADE";
            return
            [
                $"{_config.IptablesBinaryPath} {prerouting.Replace("-t nat PREROUTING", "-t nat -D PREROUTING")}",
                $"{_config.IptablesBinaryPath} {postrouting.Replace("-t nat POSTROUTING", "-t nat -D POSTROUTING")}"
            ];
        }

        return [BuildNftRemoveCommand(BuildNftComment(mapping))];
    }

    private static bool IsBestEffortRemove(string command) =>
        command.Contains(" -D PREROUTING ", StringComparison.Ordinal) ||
        command.Contains(" -D POSTROUTING ", StringComparison.Ordinal) ||
        command.Contains(" delete rule ", StringComparison.Ordinal);

    internal static bool ShouldWarnForCommandFailure(string command, string output) =>
        !IsBestEffortRemove(command) ||
        !output.Contains("Bad rule", StringComparison.OrdinalIgnoreCase);

    private void LogCommandFailure(string command, string output) =>
        logger.LogWarning("Public UDP gateway command failed with output: {Command}\n{Output}", command, output);

    private string BuildNftRemoveCommand(string comment) =>
        $"{_config.NftBinaryPath} -a list chain {_config.NftTable} prerouting | awk '/comment \"{comment}\"/ {{print $NF}}' | xargs -r -I {{}} {_config.NftBinaryPath} delete rule {_config.NftTable} prerouting handle {{}}; " +
        $"{_config.NftBinaryPath} -a list chain {_config.NftTable} postrouting | awk '/comment \"{comment}\"/ {{print $NF}}' | xargs -r -I {{}} {_config.NftBinaryPath} delete rule {_config.NftTable} postrouting handle {{}}";

    private static string BuildNftComment(TeamLabPublicUdpMapping mapping) =>
        $"gzctf-teamlab-{mapping.Id}-{mapping.RuntimeId}-{mapping.PublicUdpPort}";

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

    private async Task<(bool Success, string Output)> RunCommandAsync(string command, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { "-c", command },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo);
        if (process is null)
            return (false, "Failed to start public UDP gateway command process.");

        var stdout = await process.StandardOutput.ReadToEndAsync(token);
        var stderr = await process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var output = string.Join('\n', new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (process.ExitCode == 0)
            return (true, output);

        return (false, string.IsNullOrWhiteSpace(output) ? $"Command failed with exit code {process.ExitCode}." : output);
    }
}
