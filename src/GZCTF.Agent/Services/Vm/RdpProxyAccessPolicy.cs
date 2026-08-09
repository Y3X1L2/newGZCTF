using System.Net;
using System.Net.Sockets;

namespace GZCTF.Agent.Services.Vm;

/// <summary>
/// Decides which peers may open a console proxy connection.
/// The proxy forwards straight into a tenant VM's RDP port with no protocol-level authentication,
/// so the source address is the only control available before bytes reach the guest. It fails
/// closed: when no source is configured or none parses, only loopback is accepted.
/// </summary>
public sealed class RdpProxyAccessPolicy
{
    private readonly (uint Network, uint Mask)[] _allowed;

    private RdpProxyAccessPolicy((uint, uint)[] allowed) => _allowed = allowed;

    /// <summary>Sources that could not be parsed, for the caller to surface to operators.</summary>
    public IReadOnlyList<string> InvalidSources { get; private init; } = [];

    /// <summary>True when nothing beyond loopback is permitted.</summary>
    public bool LoopbackOnly => _allowed.Length == 1;

    /// <summary>
    /// Builds a policy from operator-configured sources plus the platform address the agent reports
    /// to, which is where console requests legitimately originate. Loopback is always included so a
    /// misconfigured deployment degrades to local-only rather than wide open.
    /// </summary>
    public static RdpProxyAccessPolicy Create(IEnumerable<string>? allowedSources, string? serverUrl)
    {
        var ranges = new List<(uint, uint)> { (ToUInt32(IPAddress.Loopback) & 0xFF000000, 0xFF000000) };
        var invalid = new List<string>();

        foreach (var source in allowedSources ?? [])
        {
            if (string.IsNullOrWhiteSpace(source))
                continue;
            if (TryParseRange(source, out var range))
                ranges.Add(range);
            else
                invalid.Add(source);
        }

        if (TryExtractServerHost(serverUrl, out var serverAddress))
            ranges.Add((ToUInt32(serverAddress), uint.MaxValue));

        return new RdpProxyAccessPolicy(ranges.Distinct().ToArray()) { InvalidSources = invalid };
    }

    public bool IsAllowed(IPAddress? address)
    {
        if (address is null)
            return false;
        // A dual-stack accept surfaces IPv4 peers as ::ffff:a.b.c.d.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return IPAddress.IsLoopback(address);

        var value = ToUInt32(address);
        return _allowed.Any(range => (value & range.Mask) == range.Network);
    }

    /// <summary>
    /// Only literal addresses are honoured. A hostname would have to be resolved at connection
    /// time, which would let DNS decide who reaches a tenant console.
    /// </summary>
    static bool TryExtractServerHost(string? serverUrl, out IPAddress address)
    {
        address = IPAddress.None;
        return !string.IsNullOrWhiteSpace(serverUrl) &&
               Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) &&
               IPAddress.TryParse(uri.Host, out address!) &&
               address.AddressFamily == AddressFamily.InterNetwork;
    }

    static bool TryParseRange(string source, out (uint Network, uint Mask) range)
    {
        range = default;
        var parts = source.Split('/', StringSplitOptions.TrimEntries);
        if (!IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var prefix = 32;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out prefix) || prefix is < 0 or > 32))
            return false;
        if (parts.Length > 2)
            return false;

        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        range = (ToUInt32(address) & mask, mask);
        return true;
    }

    static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}
