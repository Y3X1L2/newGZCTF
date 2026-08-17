using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Text;
using GZCTF.Agent.Services;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

public sealed class LinuxNetworkAttachmentService(ILogger<LinuxNetworkAttachmentService> logger)
{
    static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    public async Task<TeamLabAttachmentResult> AttachContainerAsync(
        TeamLabExecutionPlanV2 plan,
        long pid,
        TeamLabAssetExecutionSpecV2 asset,
        TeamLabAssetNetworkAttachmentV2 attachment,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            return TeamLabAttachmentResult.Failed("network", "Linux container attachments are only available on Linux Agents.");
        if (pid <= 0 || string.IsNullOrWhiteSpace(attachment.InterfaceName))
            return TeamLabAttachmentResult.Failed("validation", "Container network attachment identity is invalid.");
        var network = plan.Networks.FirstOrDefault(item => item.Key == attachment.NetworkKey);
        var port = network?.Ports.FirstOrDefault(item => item.Key == attachment.PortKey);
        if (network is null || port is null || string.IsNullOrWhiteSpace(port.MacAddress))
            return TeamLabAttachmentResult.Failed("validation", "Container network attachment is missing its declared port identity.");

        var hostInterface = HostInterfaceName(plan, asset.AssetKey, attachment.NetworkKey);
        var peerInterface = PeerInterfaceName(plan, asset.AssetKey, attachment.NetworkKey);
        try
        {
            if (await IsAttachmentConvergedAsync(pid, hostInterface, attachment.InterfaceName, cancellationToken))
            {
                await ApplyAttachmentStateAsync(pid, attachment, port.MacAddress, TeamLabNetworkService.PrefixLength(network.Cidr), cancellationToken);
                return new TeamLabAttachmentResult(true, hostInterface);
            }

            if (await SucceedsAsync("ip", ["link", "show", hostInterface], cancellationToken))
                await RunAsync("ip", ["link", "delete", hostInterface], cancellationToken, allowFailure: true);
            await RunAsync("ip", ["link", "add", hostInterface, "type", "veth", "peer", "name", peerInterface], cancellationToken);
            await RunAsync("ip", ["link", "set", peerInterface, "address", port.MacAddress], cancellationToken);
            await RunAsync("nsenter", ["-t", pid.ToString(), "-n", "ip", "link", "del", peerInterface], cancellationToken, allowFailure: true);
            await RunAsync("ip", ["link", "set", peerInterface, "netns", pid.ToString()], cancellationToken);
            await RunAsync("ip", ["link", "set", hostInterface, "up"], cancellationToken);
            await RunAsync("nsenter", ["-t", pid.ToString(), "-n", "ip", "link", "del", attachment.InterfaceName], cancellationToken, allowFailure: true);
            await RunAsync("nsenter", ["-t", pid.ToString(), "-n", "ip", "link", "set", peerInterface, "name", attachment.InterfaceName], cancellationToken);
            await RunAsync("nsenter", ["-t", pid.ToString(), "-n", "ip", "link", "set", attachment.InterfaceName, "up"], cancellationToken);
            await ApplyAttachmentStateAsync(pid, attachment, port.MacAddress, TeamLabNetworkService.PrefixLength(network.Cidr), cancellationToken);
            return new TeamLabAttachmentResult(true, hostInterface);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            logger.LogWarning(exception, "Failed to attach container interface {Interface} for asset {AssetKey}",
                attachment.InterfaceName, asset.AssetKey);
            return TeamLabAttachmentResult.Failed("network", exception.Message);
        }
    }

    public async Task<TeamLabAttachmentResult> RemoveContainerAttachmentAsync(
        TeamLabExecutionPlanV2 plan,
        string assetKey,
        string networkKey,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) return new TeamLabAttachmentResult(true, "Linux attachment is not present on this Agent.");
        var hostInterface = HostInterfaceName(plan, assetKey, networkKey);
        try
        {
            await RunAsync("ip", ["link", "delete", hostInterface], cancellationToken, allowFailure: true);
            return new TeamLabAttachmentResult(true, "Attachment removed.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
        {
            return TeamLabAttachmentResult.Failed("cleanup", exception.Message);
        }
    }

    async Task ApplyAttachmentStateAsync(
        long pid,
        TeamLabAssetNetworkAttachmentV2 attachment,
        string macAddress,
        int prefixLength,
        CancellationToken token)
    {
        await RunAsync("nsenter", ["-t", pid.ToString(), "-n", "ip", "link", "set", attachment.InterfaceName, "address", macAddress], token);
        if (!string.IsNullOrWhiteSpace(attachment.IpAddress))
            await RunAsync("nsenter", ["-t", pid.ToString(), "-n", "ip", "address", "replace", $"{attachment.IpAddress}/{prefixLength}", "dev", attachment.InterfaceName], token);
        if (attachment.Primary && !string.IsNullOrWhiteSpace(attachment.GatewayIp))
            await RunAsync("nsenter", ["-t", pid.ToString(), "-n", "ip", "route", "replace", "default", "via", attachment.GatewayIp, "dev", attachment.InterfaceName], token);
    }

    async Task<bool> IsAttachmentConvergedAsync(
        long pid,
        string hostInterface,
        string containerInterface,
        CancellationToken token)
    {
        var hostIndex = await ReadInterfaceNumberAsync($"/sys/class/net/{hostInterface}/ifindex", token);
        var hostLink = await ReadInterfaceNumberAsync($"/sys/class/net/{hostInterface}/iflink", token);
        var peerIndex = await ReadInterfaceNumberAsync($"/proc/{pid}/root/sys/class/net/{containerInterface}/ifindex", token);
        var peerLink = await ReadInterfaceNumberAsync($"/proc/{pid}/root/sys/class/net/{containerInterface}/iflink", token);
        return hostIndex > 0 && hostLink > 0 && peerIndex > 0 && peerLink > 0 &&
               hostLink == peerIndex && peerLink == hostIndex;
    }

    static async Task<long> ReadInterfaceNumberAsync(string path, CancellationToken token)
    {
        try
        {
            var value = await File.ReadAllTextAsync(path, token);
            return long.TryParse(value.Trim(), out var number) ? number : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static string HostInterfaceName(TeamLabExecutionPlanV2 plan, string assetKey, string networkKey) =>
        TeamLabExecutionIdentityV2.WorkloadHostInterface(plan.RuntimePublicId, plan.Generation, assetKey, networkKey);

    public static string PeerInterfaceName(TeamLabExecutionPlanV2 plan, string assetKey, string networkKey) =>
        StableName("tlp", plan, assetKey, networkKey);

    static string StableName(string prefix, TeamLabExecutionPlanV2 plan, string assetKey, string networkKey)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"{plan.RuntimePublicId:D}:{plan.Generation}:{assetKey}:{networkKey}"))).ToLowerInvariant();
        return $"{prefix}{hash[..Math.Min(12, hash.Length)]}"[..15];
    }

    static async Task RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken token, bool allowFailure = false)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(CommandTimeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException($"{fileName} failed: {await process.StandardError.ReadToEndAsync(token)}");
    }

    static async Task<bool> SucceedsAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(CommandTimeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }
}
