using System.Text;
using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using GZCTF.GuestControl.Contracts;

namespace GZCTF.Agent.Services.GuestControl;

public static partial class GuestConfigDriveBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static GuestConfigDriveFiles Build(CreateVmRequest request, string rootPath)
    {
        var supervisor = request.GuestSupervisor
            ?? throw new InvalidOperationException("guest_supervisor_configuration_missing");
        var management = request.ManagementInterface
            ?? throw new InvalidOperationException("guest_management_interface_missing");
        if (supervisor.Identity.RuntimeId != request.RuntimeId ||
            supervisor.Identity.Generation != request.Generation ||
            !string.Equals(supervisor.Identity.VmName, request.VmName, StringComparison.Ordinal) ||
            management.Identity != supervisor.Identity ||
            !Uri.TryCreate(supervisor.EnrollmentEndpoint, UriKind.Absolute, out var enrollmentEndpoint) ||
            enrollmentEndpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("guest_supervisor_identity_invalid");

        Directory.CreateDirectory(rootPath);
        var configurationJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            identity = supervisor.Identity,
            enrollmentEndpoint,
            enrollmentToken = supervisor.EnrollmentToken,
            workerServerCertificateSha256 = supervisor.WorkerServerCertificateSha256,
            intentDigest = supervisor.IntentDigest,
            networkInterfaces = request.Interfaces
                .Where(item => !string.IsNullOrWhiteSpace(item.MacAddress) && !string.IsNullOrWhiteSpace(item.IpAddress) && item.PrefixLength.HasValue)
                .Select(item => new GuestNetworkExpectation(item.MacAddress!, item.IpAddress!, item.PrefixLength!.Value, false))
                .Append(new GuestNetworkExpectation(
                    management.MacAddress, management.IpAddress, management.PrefixLength, true))
                .ToArray(),
            stateRoot = request.GuestControl.OsType == VmInitOsType.Windows
                ? @"C:\ProgramData\GZCTF\GuestSupervisor"
                : "/var/lib/gzctf/guest-supervisor"
        }, JsonOptions);
        return request.GuestControl.OsType == VmInitOsType.Windows
            ? BuildWindows(request, rootPath, configurationJson)
            : BuildLinux(request, rootPath, configurationJson);
    }

    private static GuestConfigDriveFiles BuildWindows(
        CreateVmRequest request,
        string root,
        string configurationJson)
    {
        var openstack = Path.Combine(root, "openstack", "latest");
        Directory.CreateDirectory(openstack);
        var metadataPath = Path.Combine(openstack, "meta_data.json");
        var networkPath = Path.Combine(openstack, "network_data.json");
        var userDataPath = Path.Combine(openstack, "user_data");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(new
        {
            uuid = request.GuestSupervisor!.Identity.NativeVmId,
            hostname = request.CloudInit?.Hostname ?? request.VmName,
            name = request.VmName,
            launch_index = 0
        }, JsonOptions));
        File.WriteAllText(networkPath, BuildOpenStackNetworkData(request));
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(configurationJson));
        File.WriteAllText(userDataPath, $$"""
            #ps1_sysnative
            $ErrorActionPreference = 'Stop'
            $root = Join-Path $env:ProgramData 'GZCTF\GuestSupervisor'
            New-Item -ItemType Directory -Force -Path $root | Out-Null
            [IO.File]::WriteAllBytes((Join-Path $root 'config.json'), [Convert]::FromBase64String('{{encoded}}'))
            & icacls.exe (Join-Path $root 'config.json') /inheritance:r /grant:r '*S-1-5-18:F' '*S-1-5-32-544:F' | Out-Null
            Start-Service -Name 'GZCTFGuestSupervisor'
            """);
        return new GuestConfigDriveFiles(
            root,
            Path.Combine(root, "config-drive.iso"),
            "config-2",
            [
                ("openstack/latest/meta_data.json", metadataPath),
                ("openstack/latest/network_data.json", networkPath),
                ("openstack/latest/user_data", userDataPath)
            ]);
    }

    private static GuestConfigDriveFiles BuildLinux(
        CreateVmRequest request,
        string root,
        string configurationJson)
    {
        var metadataPath = Path.Combine(root, "meta-data");
        var networkPath = Path.Combine(root, "network-config");
        var userDataPath = Path.Combine(root, "user-data");
        File.WriteAllText(metadataPath, $$"""
            instance-id: {{request.GuestSupervisor!.Identity.NativeVmId:D}}
            local-hostname: {{request.CloudInit?.Hostname ?? request.VmName}}
            """);
        File.WriteAllText(networkPath, KvmService.BuildCloudInitNetworkConfig(request));
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(configurationJson));
        File.WriteAllText(userDataPath, $$"""
            #cloud-config
            write_files:
              - path: /etc/gzctf/guest-supervisor/config.json
                owner: root:root
                permissions: '0600'
                encoding: b64
                content: {{encoded}}
            runcmd:
              - [systemctl, enable, --now, gzctf-guest-supervisor.service]
            """);
        return new GuestConfigDriveFiles(
            root,
            Path.Combine(root, "config-drive.iso"),
            "CIDATA",
            [
                ("user-data", userDataPath),
                ("meta-data", metadataPath),
                ("network-config", networkPath)
            ]);
    }

    internal static string BuildOpenStackNetworkData(CreateVmRequest request)
    {
        var interfaces = request.Interfaces.Select(item => new OpenStackInterface(
                item.MacAddress, item.IpAddress, item.PrefixLength, item.Gateway,
                item.DnsServers, item.Routes, item.InterfaceName, item.IsPrimary, false))
            .Append(new OpenStackInterface(
                request.ManagementInterface!.MacAddress,
                request.ManagementInterface.IpAddress,
                request.ManagementInterface.PrefixLength,
                null, [], [], "gzmgmt0", false, true))
            .ToArray();
        ValidateInterfaces(interfaces);
        var links = interfaces.Select((item, index) => new
        {
            id = $"interface{index}",
            type = "phy",
            ethernet_mac_address = item.MacAddress,
            mtu = 1500
        });
        var networks = interfaces.SelectMany((item, index) =>
        {
            if (string.IsNullOrWhiteSpace(item.IpAddress) || item.PrefixLength is null) return [];
            var routes = new List<object>();
            if (item.IsPrimary && !string.IsNullOrWhiteSpace(item.Gateway))
                routes.Add(new { network = "0.0.0.0", netmask = "0.0.0.0", gateway = item.Gateway });
            routes.AddRange(item.Routes.Select(ParseRoute));
            return new object[]
            {
                new
                {
                    id = $"network{index}",
                    link = $"interface{index}",
                    type = "ipv4",
                    ip_address = item.IpAddress,
                    netmask = PrefixToNetmask(item.PrefixLength.Value),
                    routes
                }
            };
        });
        var dns = interfaces.SelectMany(item => item.DnsServers).Distinct(StringComparer.Ordinal)
            .Select(address => new { type = "dns", address });
        return JsonSerializer.Serialize(new { links, networks, services = dns }, JsonOptions);
    }

    private static string PrefixToNetmask(int prefix)
    {
        if (prefix is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(prefix));
        if (prefix == 0) return "0.0.0.0";
        var mask = uint.MaxValue << (32 - prefix);
        return $"{mask >> 24}.{mask >> 16 & 255}.{mask >> 8 & 255}.{mask & 255}";
    }

    private static object ParseRoute(string route)
    {
        var parts = route.Split(" via ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !TryParseCidr(parts[0], allowDefault: true, out var network, out var prefix) ||
            !IsIpv4(parts[1]))
            throw new InvalidOperationException("guest_network_route_invalid");
        return new { network, netmask = PrefixToNetmask(prefix), gateway = parts[1] };
    }

    private static void ValidateInterfaces(IReadOnlyList<OpenStackInterface> interfaces)
    {
        if (interfaces.Count is < 1 or > 17 ||
            interfaces.Select(item => item.MacAddress).Distinct(StringComparer.OrdinalIgnoreCase).Count() != interfaces.Count ||
            interfaces.Count(item => item.IsPrimary) > 1)
            throw new InvalidOperationException("guest_network_interface_identity_invalid");
        foreach (var item in interfaces)
        {
            if (item.MacAddress is null || !MacAddress().IsMatch(item.MacAddress) || !IsIpv4(item.IpAddress) ||
                item.PrefixLength is < 1 or > 32 ||
                !string.IsNullOrWhiteSpace(item.Gateway) && !IsIpv4(item.Gateway) ||
                !string.IsNullOrWhiteSpace(item.Gateway) && !item.IsPrimary ||
                item.DnsServers.Any(address => !IsIpv4(address)) ||
                item.IsManagement && (!string.IsNullOrWhiteSpace(item.Gateway) || item.Routes.Count > 0))
                throw new InvalidOperationException("guest_network_interface_invalid");
            foreach (var route in item.Routes) _ = ParseRoute(route);
        }
    }

    private static bool TryParseCidr(string value, bool allowDefault, out string network, out int prefix)
    {
        network = string.Empty;
        prefix = -1;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IsIpv4(parts[0]) || !int.TryParse(parts[1], out prefix) ||
            prefix is < 0 or > 32 || !allowDefault && prefix == 0)
            return false;
        network = parts[0];
        return true;
    }

    private static bool IsIpv4(string? value) =>
        IPAddress.TryParse(value, out var address) &&
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    private sealed record OpenStackInterface(
        string? MacAddress,
        string? IpAddress,
        int? PrefixLength,
        string? Gateway,
        IReadOnlyList<string> DnsServers,
        IReadOnlyList<string> Routes,
        string? Name,
        bool IsPrimary,
        bool IsManagement);

    [GeneratedRegex("^(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex MacAddress();
}
