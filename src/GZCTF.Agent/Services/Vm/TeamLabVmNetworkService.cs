using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GZCTF.Agent.Models;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.Vm;

/// <summary>Applies only the declared MAC interfaces, through the existing QGA channel.</summary>
public sealed partial class TeamLabVmNetworkService(IVmGuestAgentClient guest)
{
    internal TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromMinutes(3);
    internal TimeSpan VerifyTimeout { get; init; } = TimeSpan.FromSeconds(45);
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    public async Task<TeamLabVmNetworkResult> ApplyAsync(TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset, bool verifyOnly, CancellationToken token,
        Action<TeamLabVmNetworkProgress>? progress = null,
        Func<CancellationToken, Task<bool>>? verifyIdentity = null)
    {
        if (asset.NetworkMode != TeamLabGuestNetworkMode.ManagedStatic)
            return new(true, "guest-network-verify", null, "Guest network is not platform managed.");
        var stage = "guest-ready";
        try
        {
            var expectedName = TeamLabExecutionIdentityV2.VmDomainName(plan.RuntimePublicId, plan.Generation,
                plan.ShardKey, asset.AssetKey);
            if (asset.ResourceId != expectedName || asset.DomainIdentity is { Length: > 0 } identity && identity != expectedName)
                return new(false, stage, "guest_identity_conflict", "VM identity does not match its execution plan.");
            var desired = ResolveInterfaces(plan, asset);
            if (desired.Count == 0)
                return new(false, stage, "guest_network_empty", "Managed guest networking requires an interface.");
            using var readyDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            readyDeadline.CancelAfter(ReadyTimeout);
            await RequireIdentityAsync(verifyIdentity, token);
            var ready = await guest.WaitReadyAsync(expectedName, ReadyTimeout, readyDeadline.Token);
            if (!ready.Ready)
                return new(false, stage, "guest_qga_unavailable", "QEMU guest agent is unavailable; install QGA and its virtio-serial driver, then retry.");
            var useLegacyWindowsHost = asset.OperatingSystem == TeamLabGuestOperatingSystem.Windows &&
                await HasLegacyWindowsHostAsync(expectedName, verifyIdentity, readyDeadline.Token);
            progress?.Invoke(new(stage, "succeeded", "QEMU guest agent is ready on the VM control channel."));

            stage = "guest-network-verify";
            IReadOnlyList<GuestInterfaceSnapshot> before;
            try
            {
                // QGA can answer before Windows PnP/WMI exposes the new NICs.
                // Share the existing readiness budget; do not write until every MAC is unique.
                while (true)
                {
                    before = await ReadAsync(asset, desired, useLegacyWindowsHost, verifyIdentity, readyDeadline.Token);
                    if (desired.Any(item => before.Count(actual => actual.MacAddress == item.MacAddress) > 1))
                        return new(false, stage, "guest_network_interface_missing",
                            "A declared MAC interface is ambiguous in the guest; check the network driver, then retry.");
                    if (desired.All(item => before.Count(actual => actual.MacAddress == item.MacAddress) == 1))
                        break;
                    await Task.Delay(PollInterval, readyDeadline.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return new(false, stage, "guest_network_interface_missing",
                    "A declared MAC interface did not appear before the guest readiness deadline; check the network driver, then retry.");
            }
            if (Matches(desired, before, out _))
            {
                progress?.Invoke(new("guest-network-apply", "already_applied", "The declared MAC interfaces already match the plan."));
                progress?.Invoke(new(stage, "succeeded", "Live guest IPv4 addresses, DNS and routes match the plan."));
                return new(true, stage, null, "Guest network verified.");
            }
            if (verifyOnly)
                return new(false, stage, "guest_network_drift", "Live guest network differs from the managed execution plan; retry deployment to reconcile it.");

            stage = "guest-network-apply";
            await RequireIdentityAsync(verifyIdentity, token);
            var apply = await guest.ExecuteAsync(expectedName, BuildApplyCommand(asset, desired, useLegacyWindowsHost), token, verifyIdentity);
            if (!apply.Success)
            {
                if (apply.StandardError?.Contains("GZCTF_GUEST_STDIN_UNAVAILABLE", StringComparison.Ordinal) == true)
                    throw new GuestInputException();
                if (apply.StandardError?.Contains("GZCTF_NETWORK_ROLLBACK_FAILED", StringComparison.Ordinal) == true)
                    return new(false, stage, "guest_network_rollback_failed",
                        "Guest network configuration failed and rollback could not be confirmed; inspect the preserved /var/lib/gzctf/teamlab/network-backups files before retrying.");
                if (apply.StandardError?.Contains("GZCTF_NETWORK_TOOLS_MISSING", StringComparison.Ordinal) == true)
                    return new(false, stage, "guest_network_tools_unavailable",
                        "Managed Linux networking requires cloud-init, Netplan, Python/PyYAML, iproute2 and systemd-resolved; install the missing components, then retry.");
                return new(false, stage, apply.TimedOut ? "guest_network_apply_timeout" : "guest_network_apply_failed",
                    "Guest network configuration did not complete; check QGA, network drivers and the required guest tools, then retry.");
            }
            progress?.Invoke(new(stage, "succeeded", "Guest network configuration completed; live verification is pending."));

            stage = "guest-network-verify";
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(VerifyTimeout);
            var mismatch = "Live guest network has not converged.";
            try
            {
                while (true)
                {
                    var actual = await ReadAsync(asset, desired, useLegacyWindowsHost, verifyIdentity, deadline.Token);
                    if (Matches(desired, actual, out mismatch))
                    {
                        progress?.Invoke(new(stage, "succeeded", "Live guest IPv4 addresses, DNS and routes match the plan."));
                        return new(true, stage, null, "Guest network verified.");
                    }
                    await Task.Delay(PollInterval, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return new(false, stage, "guest_network_verify_timeout", mismatch + " Retry deployment after checking the guest network tools.");
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            if (stage == "guest-ready")
                return new(false, stage, "guest_qga_unavailable", "QEMU guest agent did not become ready before the deadline; check QGA and its virtio-serial driver, then retry.");
            return new(false, stage, "guest_network_timeout", "Guest network control deadline expired; retry deployment after checking QGA.");
        }
        catch (GuestNetworkToolsException)
        {
            return new(false, stage, "guest_network_tools_unavailable",
                "Managed Linux network readback requires Python, iproute2 JSON output and systemd-resolved/resolvectl; install the missing components, then retry.");
        }
        catch (GuestInputException)
        {
            return new(false, stage, "guest_qga_stdin_unavailable",
                "QEMU guest agent cannot deliver script input; upgrade QGA to a version supporting guest-exec input-data, then retry.");
        }
        catch (GuestIdentityException)
        {
            return new(false, stage, "guest_identity_conflict", "VM native identity changed during guest network control.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                            JsonException or System.Xml.XmlException or IOException or
                                            System.ComponentModel.Win32Exception)
        {
            // QGA errors can echo command arguments. Never include full scripts/configuration in events.
            return new(false, stage, "guest_network_control_failed", "Guest network control or live readback failed; check QGA, network drivers and guest network tools, then retry.");
        }
    }

    async Task<IReadOnlyList<GuestInterfaceSnapshot>> ReadAsync(TeamLabAssetExecutionSpecV2 asset,
        IReadOnlyList<GuestInterfaceRequirement> desired, bool useLegacyWindowsHost,
        Func<CancellationToken, Task<bool>>? verifyIdentity,
        CancellationToken token)
    {
        await RequireIdentityAsync(verifyIdentity, token);
        var result = await guest.ExecuteAsync(asset.ResourceId, BuildReadCommand(asset, desired, useLegacyWindowsHost), token, verifyIdentity);
        await RequireIdentityAsync(verifyIdentity, token);
        if (result.StandardError?.Contains("GZCTF_GUEST_STDIN_UNAVAILABLE", StringComparison.Ordinal) == true)
            throw new GuestInputException();
        if (result.StandardError?.Contains("GZCTF_NETWORK_TOOLS_MISSING", StringComparison.Ordinal) == true)
            throw new GuestNetworkToolsException();
        if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
            throw new InvalidOperationException("Guest network readback failed.");
        return ParseSnapshot(result.StandardOutput);
    }

    async Task<bool> HasLegacyWindowsHostAsync(string vmName, Func<CancellationToken, Task<bool>>? verifyIdentity,
        CancellationToken token)
    {
        await RequireIdentityAsync(verifyIdentity, token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var exists = false;
        try { exists = await guest.TryFileExistsAsync(vmName, WindowsLegacyPowerShellHostPath, deadline.Token); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested) { }
        await RequireIdentityAsync(verifyIdentity, token);
        return exists;
    }

    static async Task RequireIdentityAsync(Func<CancellationToken, Task<bool>>? verifyIdentity, CancellationToken token)
    {
        if (verifyIdentity is not null && !await verifyIdentity(token)) throw new GuestIdentityException();
    }

    internal static IReadOnlyList<GuestInterfaceRequirement> ResolveInterfaces(TeamLabExecutionPlanV2 plan,
        TeamLabAssetExecutionSpecV2 asset)
    {
        if (!TeamLabGuestNetworkValidation.IsValid(asset))
            throw new ArgumentException("Managed guest network requirements are invalid.");
        var requirements = new List<GuestInterfaceRequirement>();
        foreach (var attachment in asset.NetworkAttachments)
        {
            var network = plan.Networks.Single(item => item.Key == attachment.NetworkKey);
            var port = network.Ports.Single(item => item.Key == attachment.PortKey);
            var prefix = attachment.PrefixLength ?? int.Parse(network.Cidr.Split('/')[1]);
            var mac = attachment.MacAddress ?? port.MacAddress;
            if (!mac.Equals(port.MacAddress, StringComparison.OrdinalIgnoreCase) ||
                prefix != int.Parse(network.Cidr.Split('/')[1]) || attachment.IpAddress != port.IpAddress)
                throw new ArgumentException("Invalid managed guest interface identity.");
            var gateway = (attachment.UseDefaultGateway ?? attachment.Primary)
                ? attachment.GatewayIp ?? network.GatewayIp : null;
            var dns = attachment.DnsServers ?? ((attachment.DnsServerIp ?? network.DnsServerIp) is { Length: > 0 } server
                ? [server] : []);
            var routes = attachment.StaticRoutes ?? [];
            if (dns.Count > TeamLabGuestNetworkValidation.MaxDnsServers || dns.Any(value => !TeamLabGuestNetworkValidation.IsDnsServer(value)))
                throw new ArgumentException("Invalid guest DNS or route requirement.");
            requirements.Add(new(mac.ToLowerInvariant(), attachment.GuestInterfaceName, attachment.IpAddress!, prefix,
                string.IsNullOrWhiteSpace(gateway) ? null : gateway, dns, routes));
        }
        if (requirements.Select(item => item.MacAddress).Distinct(StringComparer.Ordinal).Count() != requirements.Count ||
            requirements.Count(item => item.Gateway is not null) > 1)
            throw new ArgumentException("Managed guest interface identities or default gateways are repeated.");
        return requirements;
    }

    internal static VmGuestCommandRequest BuildReadCommand(TeamLabAssetExecutionSpecV2 asset,
        IReadOnlyList<GuestInterfaceRequirement> desired, bool useLegacyWindowsHost = false) => asset.OperatingSystem == TeamLabGuestOperatingSystem.Windows
        ? WindowsCommand("teamlab-network-read", BuildWindowsReadScript(desired), 30, useLegacyWindowsHost)
        : new("teamlab-network-read", "/usr/bin/python3", ["-c", BuildLinuxReadScript(desired)], 30);

    internal static VmGuestCommandRequest BuildApplyCommand(TeamLabAssetExecutionSpecV2 asset,
        IReadOnlyList<GuestInterfaceRequirement> desired, bool useLegacyWindowsHost = false) => asset.OperatingSystem == TeamLabGuestOperatingSystem.Windows
        ? WindowsCommand("teamlab-network-apply", BuildWindowsApplyScript(desired), 120, useLegacyWindowsHost)
        : new("teamlab-network-apply", "/usr/bin/timeout", ["--signal=TERM", "240", "/usr/bin/python3", "-c", BuildLinuxApplyScript(desired)], 250);

    internal const string WindowsStdinLauncher = """
        $ErrorActionPreference='Stop'
        [Console]::InputEncoding=New-Object Text.UTF8Encoding
        $source=[Console]::In.ReadToEnd()
        if ([string]::IsNullOrEmpty($source)) { [Console]::Error.Write('GZCTF_GUEST_STDIN_UNAVAILABLE'); exit 78 }
        & ([scriptblock]::Create($source))
        """;

    internal const string WindowsLegacyPowerShellHostPath = @"C:\Program Files\YINYU-GuestTools\LegacyPowerShellHost.exe";

    static VmGuestCommandRequest WindowsCommand(string id, string script, int timeout, bool useLegacyWindowsHost) => new(id,
        useLegacyWindowsHost ? WindowsLegacyPowerShellHostPath : VmBootstrapService.WindowsPowerShellPath,
        useLegacyWindowsHost ? [] :
            ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(WindowsStdinLauncher))], timeout)
        { StandardInput = script };

    internal static IReadOnlyList<GuestInterfaceSnapshot> ParseSnapshot(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        if (root?.Name.LocalName != "network") throw new InvalidOperationException("Invalid guest network readback.");
        return root.Elements("interface").Select(item => new GuestInterfaceSnapshot(
            ((string?)item.Attribute("mac") ?? "").Replace('-', ':').ToLowerInvariant(),
            (string?)item.Attribute("name") ?? "", (string?)item.Attribute("dhcp") == "true",
            item.Elements("address").Select(address => new GuestAddress((string?)address.Attribute("ip") ?? "",
                int.TryParse((string?)address.Attribute("prefix"), out var prefix) ? prefix :
                    PrefixFromMask((string?)address.Attribute("mask") ?? ""))).Where(address => IsIpv4(address.Ip)).ToArray(),
            item.Elements("gateway").Select(address => (string?)address.Attribute("ip") ?? "").Where(IsIpv4).ToArray(),
            item.Elements("dns").Select(address => (string?)address.Attribute("ip") ?? "").Where(IsIpv4).ToArray(),
            item.Elements("route").Select(route => new TeamLabGuestRouteV2(
                (string?)route.Attribute("destination") ?? "",
                (string?)route.Attribute("nextHop") ?? "",
                int.TryParse((string?)route.Attribute("metric"), out var metric) ? metric : null)).ToArray(),
            (string?)item.Attribute("ownedRouteDrift") == "true")).ToArray();
    }

    internal static bool Matches(IReadOnlyList<GuestInterfaceRequirement> desired,
        IReadOnlyList<GuestInterfaceSnapshot> actual, out string error)
    {
        foreach (var expected in desired)
        {
            var matches = actual.Where(item => item.MacAddress == expected.MacAddress).ToArray();
            if (matches.Length != 1) { error = "A declared MAC interface is absent or ambiguous in the guest."; return false; }
            var item = matches[0];
            if (item.OwnedRouteDrift)
            { error = "A previously managed static route is still present after removal from the plan."; return false; }
            if (item.Dhcp || expected.Name is not null && item.Name != expected.Name || item.Addresses.Count != 1 ||
                item.Addresses[0] != new GuestAddress(expected.IpAddress, expected.PrefixLength))
            { error = "A declared MAC interface has an unexpected name, DHCP setting or IPv4 address/prefix."; return false; }
            if (!item.Gateways.Order().SequenceEqual(expected.Gateway is null ? [] : new[] { expected.Gateway }) ||
                !item.DnsServers.SequenceEqual(expected.DnsServers))
            { error = "A declared MAC interface has unexpected default gateways or DNS servers."; return false; }
            if (expected.Routes.Any(route => !item.Routes.Any(actualRoute => actualRoute.DestinationCidr == route.DestinationCidr &&
                    actualRoute.NextHop == route.NextHop && (route.Metric is null || actualRoute.Metric == route.Metric))))
            { error = "A declared static route was not found in the live guest route table."; return false; }
        }
        error = "";
        return true;
    }

    internal static int PrefixFromMask(string mask)
    {
        if (!IsIpv4(mask)) return -1;
        var bits = string.Concat(IPAddress.Parse(mask).GetAddressBytes().Select(value => Convert.ToString(value, 2).PadLeft(8, '0')));
        var prefix = bits.TakeWhile(value => value == '1').Count();
        return bits.Skip(prefix).All(value => value == '0') ? prefix : -1;
    }

    internal static string MaskFromPrefix(int prefix) => string.Join('.', Enumerable.Range(0, 4)
        .Select(index => (byte)(255 << Math.Clamp(8 - prefix + index * 8, 0, 8))));

    static bool IsIpv4(string? value) => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    sealed class GuestNetworkToolsException : Exception;
    sealed class GuestInputException : Exception;
    sealed class GuestIdentityException : Exception;
}

public sealed record TeamLabVmNetworkResult(bool Success, string Stage, string? ErrorCode, string Message);
public sealed record TeamLabVmNetworkProgress(string Stage, string Outcome, string Message);
internal sealed record GuestInterfaceRequirement(string MacAddress, string? Name, string IpAddress, int PrefixLength,
    string? Gateway, IReadOnlyList<string> DnsServers, IReadOnlyList<TeamLabGuestRouteV2> Routes);
internal sealed record GuestAddress(string Ip, int Prefix);
internal sealed record GuestInterfaceSnapshot(string MacAddress, string Name, bool Dhcp,
    IReadOnlyList<GuestAddress> Addresses, IReadOnlyList<string> Gateways, IReadOnlyList<string> DnsServers,
    IReadOnlyList<TeamLabGuestRouteV2> Routes, bool OwnedRouteDrift = false);
