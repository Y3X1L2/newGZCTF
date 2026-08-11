using System.Diagnostics;
using System.ComponentModel;
using System.Net;
using System.Text.Json.Nodes;
using GZCTF.Agent.Models;

namespace GZCTF.Agent.Services.TeamLab;

public sealed record TeamLabDataPlaneReadiness(
    bool Ready,
    bool LocalOvsReady,
    bool OvnServiceReady,
    bool NorthboundReachable,
    string Code);

public sealed class TeamLabDataPlanePreparationService(
    OvsdbJsonRpcClient ovsdb,
    ILogger<TeamLabDataPlanePreparationService> logger)
{
    private static readonly TimeSpan ProbeCacheDuration = TimeSpan.FromSeconds(20);
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private TeamLabDataPlaneReadiness? _cachedReadiness;
    private DateTimeOffset _cachedAt;

    public async Task<TeamLabDataPlaneReadiness> ApplyAsync(TeamLabDataPlaneSyncConfig desired,
        CancellationToken cancellationToken)
    {
        if (!desired.Enabled)
            return new(false, false, false, false, "disabled");
        Validate(desired);
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("OVS/OVN data plane preparation requires Linux.");

        await EnsurePackagesAsync(desired.ControlPlane, cancellationToken);
        await EnsureServiceAsync(["openvswitch-switch", "openvswitch"], cancellationToken);
        await RunRequiredAsync("ovs-vsctl", ["--may-exist", "add-br", desired.IntegrationBridgeName], cancellationToken);

        if (desired.ControlPlane)
        {
            await EnsureServiceAsync(["ovn-central"], cancellationToken);
            await RunRequiredAsync("ovn-nbctl", ["set-connection", desired.NorthboundListenEndpoint!], cancellationToken);
            await RunRequiredAsync("ovn-sbctl", ["set-connection", desired.SouthboundListenEndpoint!], cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(desired.SouthboundEndpoint))
        {
            await EnsureServiceAsync(["ovn-controller"], cancellationToken);
            await RunRequiredAsync("ovs-vsctl",
                [
                    "set", "Open_vSwitch", ".",
                    $"external-ids:ovn-remote={desired.SouthboundEndpoint}",
                    "external-ids:ovn-encap-type=geneve",
                    $"external-ids:ovn-encap-ip={desired.ChassisEncapIp}"
                ], cancellationToken);
            await RunRequiredAsync("systemctl", ["restart", "ovn-controller"], cancellationToken);
        }

        _cachedReadiness = null;
        return await GetReadinessAsync(desired, cancellationToken);
    }

    public async Task<TeamLabDataPlaneReadiness> GetReadinessAsync(AgentTeamLabConfig config,
        CancellationToken cancellationToken)
    {
        var desired = new TeamLabDataPlaneSyncConfig(
            config.Enable,
            false,
            config.OvnNorthboundEndpoint,
            config.OvnSouthboundEndpoint,
            null,
            null,
            null,
            config.OvsIntegrationBridgeName);
        return await GetReadinessAsync(desired, cancellationToken);
    }

    public async Task<TeamLabDataPlaneReadiness> GetReadinessAsync(TeamLabDataPlaneSyncConfig desired,
        CancellationToken cancellationToken)
    {
        if (!desired.Enabled || !OperatingSystem.IsLinux())
            return new(false, false, false, false, desired.Enabled ? "unsupported_os" : "disabled");
        if (_cachedReadiness is { } cached && DateTimeOffset.UtcNow - _cachedAt < ProbeCacheDuration)
            return cached;

        await _probeGate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedReadiness is { } current && DateTimeOffset.UtcNow - _cachedAt < ProbeCacheDuration)
                return current;

            var localOvs = await SucceedsAsync("ovs-vsctl", ["br-exists", desired.IntegrationBridgeName], cancellationToken);
            var service = desired.ControlPlane
                ? await SucceedsAsync("systemctl", ["is-active", "--quiet", "ovn-central"], cancellationToken)
                : await SucceedsAsync("systemctl", ["is-active", "--quiet", "ovn-controller"], cancellationToken);
            var northbound = false;
            if (localOvs && service && IsOvsdbEndpoint(desired.NorthboundEndpoint))
            {
                try
                {
                    await ovsdb.SelectAsync(desired.NorthboundEndpoint!, "OVN_Northbound", "NB_Global",
                        new JsonArray(), cancellationToken);
                    northbound = true;
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Net.Sockets.SocketException)
                {
                    logger.LogDebug(exception, "OVN Northbound probe is not ready.");
                }
            }

            var code = !localOvs ? "ovs_bridge_unavailable"
                : !service ? "ovn_service_unavailable"
                : !northbound ? "ovn_northbound_unavailable"
                : "ready";
            return _cachedReadiness = new TeamLabDataPlaneReadiness(
                localOvs && service && northbound, localOvs, service, northbound, code);
        }
        finally
        {
            _cachedAt = DateTimeOffset.UtcNow;
            _probeGate.Release();
        }
    }

    private static void Validate(TeamLabDataPlaneSyncConfig desired)
    {
        if (!IsLinuxName(desired.IntegrationBridgeName))
            throw new InvalidOperationException("OVS integration bridge name is invalid.");
        if (!string.IsNullOrWhiteSpace(desired.NorthboundEndpoint) && !IsOvsdbEndpoint(desired.NorthboundEndpoint))
            throw new InvalidOperationException("OVN Northbound endpoint is invalid.");
        if (!string.IsNullOrWhiteSpace(desired.SouthboundEndpoint) && !IsOvsdbEndpoint(desired.SouthboundEndpoint))
            throw new InvalidOperationException("OVN Southbound endpoint is invalid.");
        if (!string.IsNullOrWhiteSpace(desired.ChassisEncapIp) && !IPAddress.TryParse(desired.ChassisEncapIp, out _))
            throw new InvalidOperationException("OVN chassis encapsulation address is invalid.");
        if (!string.IsNullOrWhiteSpace(desired.SouthboundEndpoint) &&
            !IPAddress.TryParse(desired.ChassisEncapIp, out _))
            throw new InvalidOperationException("OVN chassis encapsulation address is required.");
        if (desired.ControlPlane &&
            (!IsOvsdbEndpoint(desired.NorthboundEndpoint) || !IsOvsdbEndpoint(desired.SouthboundEndpoint) ||
             !IsPassiveTcpEndpoint(desired.NorthboundListenEndpoint) ||
             !IsPassiveTcpEndpoint(desired.SouthboundListenEndpoint)))
        {
            throw new InvalidOperationException("OVN control plane endpoints are incomplete.");
        }
    }

    private async Task EnsurePackagesAsync(bool controlPlane, CancellationToken cancellationToken)
    {
        if ((File.Exists("/usr/bin/ovs-vsctl") || File.Exists("/usr/local/bin/ovs-vsctl")) &&
            (File.Exists("/usr/sbin/ovn-controller") || File.Exists("/usr/bin/ovn-controller")) &&
            (!controlPlane ||
             (File.Exists("/usr/bin/ovn-nbctl") || File.Exists("/usr/sbin/ovn-nbctl"))))
            return;

        var packageManager = DetectPackageManager();
        var packages = packageManager switch
        {
            "apt-get" => controlPlane
                ? new[] { "openvswitch-switch", "ovn-host", "ovn-central" }
                : new[] { "openvswitch-switch", "ovn-host" },
            "dnf" or "yum" => controlPlane
                ? new[] { "openvswitch", "ovn-host", "ovn-central" }
                : new[] { "openvswitch", "ovn-host" },
            "zypper" => controlPlane
                ? new[] { "openvswitch", "ovn", "ovn-central" }
                : new[] { "openvswitch", "ovn" },
            "pacman" => controlPlane
                ? new[] { "openvswitch", "ovn" }
                : new[] { "openvswitch", "ovn" },
            _ => throw new InvalidOperationException("Unsupported Linux package manager for OVS/OVN preparation.")
        };
        var arguments = packageManager switch
        {
            "apt-get" => new List<string> { "install", "-y", "--no-install-recommends" },
            "dnf" or "yum" => new List<string> { "install", "-y" },
            "zypper" => new List<string> { "--non-interactive", "install", "-y" },
            "pacman" => new List<string> { "-Sy", "--noconfirm", "--needed" },
            _ => throw new InvalidOperationException("Unsupported Linux package manager for OVS/OVN preparation.")
        };
        arguments.AddRange(packages);
        if (packageManager == "apt-get")
            await RunRequiredAsync(packageManager, ["update", "-y"], cancellationToken);
        await RunRequiredAsync(packageManager, arguments, cancellationToken);
    }

    private static string? DetectPackageManager() => new[] { "apt-get", "dnf", "yum", "zypper", "pacman" }
        .FirstOrDefault(command => File.Exists(Path.Combine("/usr/bin", command)) ||
                                   File.Exists(Path.Combine("/bin", command)));

    private async Task EnsureServiceAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        foreach (var name in names)
        {
            if (await SucceedsAsync("systemctl", ["enable", "--now", name], cancellationToken))
                return;
        }
        throw new InvalidOperationException($"Unable to start required service '{string.Join("' or '", names)}'.");
    }

    private static bool IsLinuxName(string value) => value.Length is > 0 and <= 15 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsOvsdbEndpoint(string? value) =>
        value is not null && (value.StartsWith("unix:/", StringComparison.Ordinal) ||
                              value.StartsWith("tcp:", StringComparison.Ordinal));

    private static bool IsPassiveTcpEndpoint(string? value) =>
        value is not null && value.StartsWith("ptcp:", StringComparison.Ordinal);

    private async Task RunRequiredAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(fileName, arguments, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{fileName} failed with exit code {result.ExitCode}.");
    }

    private async Task<bool> SucceedsAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        (await RunAsync(fileName, arguments, cancellationToken)).ExitCode == 0;

    private static async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException($"Unable to start {fileName}.");
            await process.WaitForExitAsync(timeout.Token);
            return new CommandResult(process.ExitCode);
        }
        catch (Win32Exception)
        {
            // Capability probes treat a missing optional host tool as not ready.
            return new CommandResult(-1);
        }
    }

    private sealed record CommandResult(int ExitCode);
}
