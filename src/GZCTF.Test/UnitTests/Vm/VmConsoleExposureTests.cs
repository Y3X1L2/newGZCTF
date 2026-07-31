using System.Net;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Vm;
using Xunit;

namespace GZCTF.Test.UnitTests.Vm;

/// <summary>
/// The VM console reaches a tenant's desktop and pre-boot firmware with no authentication of its
/// own, so exposure is controlled entirely by where it listens and who may connect.
/// </summary>
public class VmConsoleExposureTests
{
    [Fact]
    public void VirtInstallArguments_KeepVncOffEveryNonLoopbackInterface()
    {
        var request = new CreateVmRequest
        {
            VmName = "tl42-dc01",
            Memory = 2048,
            Cpu = 2,
            Generation = 1
        };

        var arguments = VmDomainBuilder.BuildVirtInstallArguments(request, "/var/lib/gzctf/images/x.qcow2", string.Empty);

        Assert.Contains("--graphics vnc,listen=127.0.0.1", arguments);
        Assert.DoesNotContain("0.0.0.0", arguments);
    }

    [Fact]
    public void AccessPolicy_WithoutConfiguredSources_AcceptsLoopbackOnly()
    {
        // Fail closed: an unconfigured node refuses remote console rather than serving everyone.
        var policy = RdpProxyAccessPolicy.Create(null, null);

        Assert.True(policy.LoopbackOnly);
        Assert.True(policy.IsAllowed(IPAddress.Loopback));
        Assert.False(policy.IsAllowed(IPAddress.Parse("10.0.7.118")));
        Assert.False(policy.IsAllowed(IPAddress.Parse("192.168.1.5")));
    }

    [Fact]
    public void AccessPolicy_AllowsThePlatformAddressTheAgentReportsTo()
    {
        var policy = RdpProxyAccessPolicy.Create(null, "https://10.0.7.10:8080");

        Assert.True(policy.IsAllowed(IPAddress.Parse("10.0.7.10")));
        Assert.False(policy.IsAllowed(IPAddress.Parse("10.0.7.11")));
    }

    [Fact]
    public void AccessPolicy_HonoursConfiguredHostsAndRanges()
    {
        var policy = RdpProxyAccessPolicy.Create(["10.20.0.0/16", "172.31.4.9"], null);

        Assert.True(policy.IsAllowed(IPAddress.Parse("10.20.255.1")));
        Assert.True(policy.IsAllowed(IPAddress.Parse("172.31.4.9")));
        Assert.False(policy.IsAllowed(IPAddress.Parse("10.21.0.1")));
        Assert.False(policy.IsAllowed(IPAddress.Parse("172.31.4.10")));
    }

    [Fact]
    public void AccessPolicy_RejectsUnknownPeersAndReportsUnparsableSources()
    {
        var policy = RdpProxyAccessPolicy.Create(["not-a-cidr", "10.5.0.0/16"], null);

        Assert.Contains("not-a-cidr", policy.InvalidSources);
        Assert.True(policy.IsAllowed(IPAddress.Parse("10.5.1.1")));
        Assert.False(policy.IsAllowed(null));
    }

    [Fact]
    public void AccessPolicy_TreatsIpv4MappedPeersAsIpv4()
    {
        // A dual-stack accept surfaces IPv4 peers as ::ffff:a.b.c.d; matching must not depend on it.
        var policy = RdpProxyAccessPolicy.Create(["10.9.0.0/16"], null);

        Assert.True(policy.IsAllowed(IPAddress.Parse("10.9.0.4").MapToIPv6()));
        Assert.False(policy.IsAllowed(IPAddress.Parse("10.10.0.4").MapToIPv6()));
    }

    [Fact]
    public void AccessPolicy_IgnoresHostnamesSoDnsCannotGrantConsoleAccess()
    {
        var policy = RdpProxyAccessPolicy.Create(null, "https://platform.example.com:8080");

        Assert.True(policy.LoopbackOnly);
    }
}
