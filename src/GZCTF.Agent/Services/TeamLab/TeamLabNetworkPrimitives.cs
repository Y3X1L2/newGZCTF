using System.Net;
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

    internal static string TrimInterfaceName(string value) => value.Length <= 15 ? value : value[..15];

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
