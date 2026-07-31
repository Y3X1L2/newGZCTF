using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace GZCTF.Modules.TeamLab.Application.Validation;

/// <summary>An address range tenants must not claim, together with why it is off limits.</summary>
public sealed record TeamLabReservedRange(string Cidr, string Reason);

/// <summary>
/// Decides whether a topology may claim an address pool. Runtime CIDRs derived from a pool are
/// pushed to WorkerNodes and installed in the host root namespace, so a pool overlapping
/// infrastructure would replace the node's own routes and break unrelated games and plain
/// Docker/VM instances on that node.
///
/// Two rules apply: the pool must sit inside the platform's runtime address range, and it must not
/// intersect a reserved range.
/// </summary>
public sealed class TeamLabAddressPolicy
{
    /// <summary>
    /// Ranges the platform always reserves. Site-specific ranges — node management LANs, extra
    /// Docker address pools, storage or database networks — belong in
    /// <c>TeamLabNetwork.ReservedCidrs</c> because they cannot be known here.
    /// </summary>
    private static readonly TeamLabReservedRange[] BuiltIn =
    [
        new("172.17.0.0/16", "Docker 默认网桥 docker0 网段")
    ];

    private readonly (uint Start, uint End, string Cidr, string Reason)[] _reserved;
    private readonly (uint Start, uint End, string Cidr)[] _allowedPools;

    private TeamLabAddressPolicy(
        (uint, uint, string, string)[] reserved,
        (uint, uint, string)[] allowedPools)
    {
        _reserved = reserved;
        _allowedPools = allowedPools;
    }

    /// <summary>Sources that could not be parsed, for the caller to surface to operators.</summary>
    public IReadOnlyList<string> InvalidSources { get; private init; } = [];

    /// <summary>True when no runtime pool is configured, so containment cannot be enforced.</summary>
    public bool AllowedPoolsUnconfigured => _allowedPools.Length == 0;

    /// <summary>Human-readable list of permitted ranges, for validation messages.</summary>
    public string AllowedPoolDescription => string.Join("、", _allowedPools.Select(item => item.Cidr));

    public IReadOnlyList<TeamLabReservedRange> ReservedRanges =>
        _reserved.Select(item => new TeamLabReservedRange(item.Cidr, item.Reason)).ToArray();

    /// <summary>Policy with no restrictions, for callers validating structure without platform context.</summary>
    public static TeamLabAddressPolicy None { get; } = new([], []);

    /// <summary>Built-in reservations only, used when a caller supplies no configuration.</summary>
    public static TeamLabAddressPolicy PlatformDefaults { get; } = ForPlatform(null, null, null);

    /// <summary>
    /// Builds the policy from configuration. <paramref name="runtimeNetworkBaseCidr" /> is the range
    /// the platform allocates runtime networks from; pools must sit inside it. The Fabric link pool
    /// is reserved because it is admin-configurable and could be moved into RFC1918, where a tenant
    /// pool could otherwise collide with it.
    /// </summary>
    public static TeamLabAddressPolicy ForPlatform(
        IEnumerable<string>? reservedCidrs,
        string? fabricLinkPool,
        string? runtimeNetworkBaseCidr)
    {
        var ranges = new List<TeamLabReservedRange>(BuiltIn);
        if (!string.IsNullOrWhiteSpace(fabricLinkPool))
            ranges.Add(new TeamLabReservedRange(fabricLinkPool, "TeamLab Fabric 链路地址池"));
        foreach (var cidr in reservedCidrs ?? [])
        {
            if (!string.IsNullOrWhiteSpace(cidr))
                ranges.Add(new TeamLabReservedRange(cidr, "平台保留网段（TeamLabNetwork:ReservedCidrs）"));
        }

        var reserved = new List<(uint, uint, string, string)>();
        var invalid = new List<string>();
        foreach (var range in ranges)
        {
            if (TryParseCidr(range.Cidr, out var start, out var end))
                reserved.Add((start, end, range.Cidr, range.Reason));
            else
                invalid.Add(range.Cidr);
        }

        var allowed = new List<(uint, uint, string)>();
        if (!string.IsNullOrWhiteSpace(runtimeNetworkBaseCidr))
        {
            if (TryParseCidr(runtimeNetworkBaseCidr, out var start, out var end))
                allowed.Add((start, end, runtimeNetworkBaseCidr));
            else
                invalid.Add(runtimeNetworkBaseCidr);
        }

        return new TeamLabAddressPolicy(reserved.Distinct().ToArray(), allowed.Distinct().ToArray())
        {
            InvalidSources = invalid
        };
    }

    /// <summary>
    /// Reports the first reserved range the inclusive span touches. Any overlap counts, not just
    /// containment: a pool that merely straddles a reserved range still produces host routes that
    /// shadow it.
    /// </summary>
    public bool TryFindReservedConflict(uint start, uint end, [NotNullWhen(true)] out TeamLabReservedRange? conflict)
    {
        foreach (var range in _reserved)
        {
            if (start <= range.End && range.Start <= end)
            {
                conflict = new TeamLabReservedRange(range.Cidr, range.Reason);
                return true;
            }
        }

        conflict = null;
        return false;
    }

    /// <summary>
    /// Whether the span sits entirely inside a permitted runtime pool. Returns true when no pool is
    /// configured: containment is defence in depth, and an unset or unparsable range must not make
    /// every topology unusable — the reserved-range exclusion still applies in that case.
    /// </summary>
    public bool IsWithinAllowedPools(uint start, uint end) =>
        _allowedPools.Length == 0 || _allowedPools.Any(pool => pool.Start <= start && end <= pool.End);

    internal static bool TryParseCidr(string cidr, out uint start, out uint end)
    {
        start = 0;
        end = 0;
        var parts = (cidr ?? string.Empty).Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32)
            return false;
        var bytes = address.GetAddressBytes();
        var raw = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        start = raw & mask;
        end = start | ~mask;
        return true;
    }
}
