using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GZCTF.Agent.Services.TeamLab;

internal static partial class TeamLabNetworkPrimitives
{
    internal static string? ValidateLinuxName(string value, string field) =>
        string.IsNullOrWhiteSpace(value) || value.Length > 15 || !LinuxNameRegex().IsMatch(value)
            ? $"Invalid {field}."
            : null;

    internal static string? ValidateCidr(string value, string field)
    {
        var parts = value.Split('/');
        return parts.Length != 2 || !IsIpv4(parts[0]) ||
               !int.TryParse(parts[1], out var prefix) || prefix is < 1 or > 32
            ? $"Invalid {field}."
            : null;
    }

    internal static string? ValidateIp(string value, string field) =>
        IsIpv4(value) ? null : $"Invalid {field}.";

    internal static string? ValidateHostname(string value, string field) =>
        string.IsNullOrWhiteSpace(value) || !HostnameRegex().IsMatch(value)
            ? $"Invalid {field}."
            : null;

    internal static string ShellQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    /// <summary>
    /// Fits an interface name into the kernel's 15-character limit without losing what makes it
    /// unique. Plain truncation collapses distinct names — a 14-character namespace turns both
    /// "&lt;ns&gt;h0" and "&lt;ns&gt;h1" into "&lt;ns&gt;h", so building the second veth pair deletes the first, and a
    /// 15-character namespace makes a pair's two ends share one name. Mirrors the control plane's
    /// TeamLabResourceNameFactory.LinuxName so both sides derive the same name for the same input.
    /// </summary>
    internal static string TrimInterfaceName(string value)
    {
        if (value.Length <= 15) return value;
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..6];
        return $"{value[..8]}-{digest}";
    }

    internal static string BuildEnsureVethPairCommand(
        string namespaceName,
        string hostName,
        string peerName) =>
        $"if ip link show dev {hostName} >/dev/null 2>&1 && ip netns exec {namespaceName} ip link show dev {peerName} >/dev/null 2>&1; " +
        $"then host_index=$(cat /sys/class/net/{hostName}/ifindex) && host_link=$(cat /sys/class/net/{hostName}/iflink) && " +
        $"peer_index=$(ip netns exec {namespaceName} cat /sys/class/net/{peerName}/ifindex) && " +
        $"peer_link=$(ip netns exec {namespaceName} cat /sys/class/net/{peerName}/iflink) && " +
        "test \"$host_link\" = \"$peer_index\" && test \"$peer_link\" = \"$host_index\"; else false; fi || { " +
        $"ip link delete {hostName} 2>/dev/null || true; " +
        $"ip netns exec {namespaceName} ip link delete {peerName} 2>/dev/null || true; " +
        $"ip link add {hostName} type veth peer name {peerName}; " +
        $"ip link set {peerName} netns {namespaceName}; }}";

    internal static string BuildHostIpv4AddressConvergenceCommand(string interfaceName, string addressCidr) =>
        BuildIpv4AddressConvergenceCommand("ip", interfaceName, addressCidr);

    internal static string BuildNamespaceIpv4AddressConvergenceCommand(
        string namespaceName,
        string interfaceName,
        string addressCidr) =>
        BuildIpv4AddressConvergenceCommand($"ip netns exec {namespaceName} ip", interfaceName, addressCidr);

    private static string BuildIpv4AddressConvergenceCommand(
        string ipCommand,
        string interfaceName,
        string addressCidr) =>
        $"current=$({ipCommand} -o -4 addr show dev {interfaceName} scope global | awk '{{print $4}}' | sort | tr '\\n' ' '); " +
        $"test \"$current\" = {ShellQuote($"{addressCidr} ")} || {{ " +
        $"{ipCommand} addr flush dev {interfaceName} scope global; " +
        $"{ipCommand} addr add {addressCidr} dev {interfaceName}; }}";

    internal static string AddressFromCidr(string cidr)
    {
        var index = cidr.IndexOf('/');
        return index > 0 ? cidr[..index] : cidr;
    }

    internal static string? NetmaskFromCidr(string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix) || prefix is < 1 or > 32)
            return null;
        var mask = uint.MaxValue << (32 - prefix);
        return new IPAddress([
            (byte)(mask >> 24),
            (byte)(mask >> 16),
            (byte)(mask >> 8),
            (byte)mask
        ]).ToString();
    }

    internal static bool HasCommand(string command) =>
        new[] { "/sbin", "/usr/sbin", "/bin", "/usr/bin", "/usr/local/bin" }
            .Any(path => File.Exists(Path.Combine(path, command)));

    private static bool IsIpv4(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 4 && parts.All(part => part.Length > 0 &&
            int.TryParse(part, out var octet) && octet is >= 0 and <= 255);
    }

    [GeneratedRegex("^[a-zA-Z0-9_.-]+$")]
    private static partial Regex LinuxNameRegex();

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9.-]{0,62}$")]
    private static partial Regex HostnameRegex();
}
