using System.Linq;
using GZCTF.Modules.TeamLab.Application.Validation;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

/// <summary>
/// Runtime CIDRs derived from a topology address pool are installed in the WorkerNode host routing
/// table, so a pool outside the platform's range — or overlapping something the node already routes
/// — would shadow the node's own routes and break unrelated games and plain Docker/VM instances.
/// </summary>
public class TeamLabAddressPolicyTests
{
    static TeamLabAddressPolicy Configured() =>
        TeamLabAddressPolicy.ForPlatform(["192.168.1.0/24"], "100.64.0.0/16", "10.180.0.0/16");

    static (uint Start, uint End) Range(string cidr)
    {
        Assert.True(TeamLabAddressPolicy.TryParseCidr(cidr, out var start, out var end));
        return (start, end);
    }

    [Theory]
    [InlineData("10.180.0.0/16")]  // exactly the pool
    [InlineData("10.180.4.0/24")]  // a slice of it
    [InlineData("10.180.255.0/29")]
    public void PoolsInsideTheRuntimeRangeAreAccepted(string cidr)
    {
        var (start, end) = Range(cidr);

        Assert.True(Configured().IsWithinAllowedPools(start, end));
    }

    [Theory]
    [InlineData("10.181.0.0/24")] // adjacent but outside
    [InlineData("192.168.50.0/24")]
    [InlineData("10.0.0.0/8")]    // strictly contains the pool rather than sitting inside it
    [InlineData("10.180.0.0/15")] // straddles the pool boundary
    public void PoolsOutsideTheRuntimeRangeAreRejected(string cidr)
    {
        var (start, end) = Range(cidr);

        Assert.False(Configured().IsWithinAllowedPools(start, end));
    }

    [Fact]
    public void ContainmentIsSkippedWhenNoRuntimeRangeIsConfigured()
    {
        // Containment is defence in depth; an unset range must not make every topology unusable.
        // The reserved-range exclusion below still applies.
        var policy = TeamLabAddressPolicy.ForPlatform(null, null, null);
        var (start, end) = Range("192.168.77.0/24");

        Assert.True(policy.AllowedPoolsUnconfigured);
        Assert.True(policy.IsWithinAllowedPools(start, end));
    }

    [Fact]
    public void UnparsableRuntimeRangeDegradesToNoContainmentAndIsReported()
    {
        var policy = TeamLabAddressPolicy.ForPlatform(null, null, "not-a-cidr");

        Assert.Contains("not-a-cidr", policy.InvalidSources);
        Assert.True(policy.AllowedPoolsUnconfigured);
    }

    [Theory]
    [InlineData("172.17.0.0/16")] // docker0 exactly
    [InlineData("172.17.5.0/24")] // a slice of it
    [InlineData("172.16.0.0/12")] // merely straddling still shadows it
    public void ReservedRangesAreRejectedOnAnyOverlap(string cidr)
    {
        var (start, end) = Range(cidr);

        Assert.True(TeamLabAddressPolicy.PlatformDefaults.TryFindReservedConflict(start, end, out _));
    }

    [Theory]
    [InlineData("172.18.0.0/16")] // adjacent, must not trip the inclusive overlap check
    [InlineData("172.16.0.0/16")]
    [InlineData("10.180.4.0/24")]
    public void UnrelatedRangesAreNotReserved(string cidr)
    {
        var (start, end) = Range(cidr);

        Assert.False(TeamLabAddressPolicy.PlatformDefaults.TryFindReservedConflict(start, end, out _));
    }

    [Fact]
    public void OperatorSuppliedAndFabricRangesAreReserved()
    {
        var policy = Configured();

        Assert.True(policy.TryFindReservedConflict(Range("192.168.1.128/25").Start,
            Range("192.168.1.128/25").End, out var management));
        Assert.Contains("ReservedCidrs", management!.Reason);

        Assert.True(policy.TryFindReservedConflict(Range("100.64.7.0/24").Start,
            Range("100.64.7.0/24").End, out var fabric));
        Assert.Contains("Fabric", fabric!.Reason);
    }

    [Fact]
    public void MalformedReservedEntriesAreReportedWithoutBlockingTheRest()
    {
        var policy = TeamLabAddressPolicy.ForPlatform(["not-a-cidr", "10.90.0.0/16"], null, null);

        Assert.Contains("not-a-cidr", policy.InvalidSources);
        Assert.True(policy.TryFindReservedConflict(Range("10.90.1.0/24").Start,
            Range("10.90.1.0/24").End, out _));
        Assert.DoesNotContain(policy.ReservedRanges, range => range.Cidr == "not-a-cidr");
    }

    [Fact]
    public void NonePolicyRestrictsNothing()
    {
        var (start, end) = Range("172.17.0.0/16");

        Assert.False(TeamLabAddressPolicy.None.TryFindReservedConflict(start, end, out _));
        Assert.True(TeamLabAddressPolicy.None.IsWithinAllowedPools(start, end));
    }
}
