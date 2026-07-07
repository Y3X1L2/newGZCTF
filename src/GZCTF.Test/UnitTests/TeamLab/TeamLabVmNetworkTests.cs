using System;
using System.Collections.Generic;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabVmNetworkTests
{
    [Fact]
    public void BuildVirtInstallNetworkArguments_DefaultsToExistingLibvirtNetwork()
    {
        var args = KvmService.BuildVirtInstallNetworkArguments(new CreateVmRequest());

        Assert.Equal("--network network=default,model=e1000e", args);
    }

    [Fact]
    public void BuildVirtInstallNetworkArguments_UsesTeamLabBridgeAndStableMac()
    {
        var args = KvmService.BuildVirtInstallNetworkArguments(new CreateVmRequest
        {
            Interfaces =
            [
                new VmNetworkInterfaceRequest
                {
                    BridgeName = "tl123-lab",
                    MacAddress = "02:42:ac:10:00:02",
                    Model = "virtio"
                }
            ]
        });

        Assert.Equal("--network bridge=tl123-lab,model=virtio,mac=02:42:ac:10:00:02", args);
    }

    [Fact]
    public void BuildVirtInstallNetworkArguments_RejectsUnsafeBridgeNames()
    {
        Assert.Throws<ArgumentException>(() => KvmService.BuildVirtInstallNetworkArguments(new CreateVmRequest
        {
            Interfaces =
            [
                new VmNetworkInterfaceRequest
                {
                    BridgeName = "tl123;rm",
                    MacAddress = "02:42:ac:10:00:02"
                }
            ]
        }));
    }

    [Fact]
    public void BuildTeamLabVmIpProbeCommands_UseInterfaceBridgesInsteadOfDefaultNetwork()
    {
        var commands = KvmService.BuildTeamLabVmIpProbeCommands(new CreateVmRequest
        {
            VmName = "tl12-win-ad",
            Interfaces =
            [
                new VmNetworkInterfaceRequest
                {
                    BridgeName = "tl12-data",
                    MacAddress = "02:42:ac:10:00:02",
                    Model = "virtio"
                },
                new VmNetworkInterfaceRequest
                {
                    BridgeName = "tl12-ops",
                    MacAddress = "02:42:ac:10:00:03",
                    Model = "virtio"
                }
            ]
        });

        Assert.Contains("ip neigh show dev tl12-data 2>/dev/null", commands);
        Assert.Contains("ip neigh show dev tl12-ops 2>/dev/null", commands);
        Assert.DoesNotContain(commands, command => command.Contains("virsh net-dhcp-leases default"));
        Assert.DoesNotContain(commands, command => command.Contains("virbr0"));
    }

    [Fact]
    public void BuildCloudInitNetworkConfig_UsesMacMatchedStaticAddresses()
    {
        var config = KvmService.BuildCloudInitNetworkConfig(new CreateVmRequest
        {
            Interfaces =
            [
                new VmNetworkInterfaceRequest
                {
                    BridgeName = "tl12-entry",
                    InterfaceName = "eth0",
                    MacAddress = "02:42:ac:10:00:04",
                    IpAddress = "10.90.0.20",
                    PrefixLength = 28,
                    Gateway = "10.90.0.17",
                    DnsServers = ["10.90.0.17"],
                    Routes = ["10.90.0.32/28 via 10.90.0.17"],
                    IsPrimary = true
                }
            ]
        });

        Assert.Contains("version: 2", config);
        Assert.Contains("ethernets:", config);
        Assert.Contains("macaddress: \"02:42:ac:10:00:04\"", config);
        Assert.Contains("set-name: eth0", config);
        Assert.Contains("dhcp4: false", config);
        Assert.Contains("addresses: [10.90.0.20/28]", config);
        Assert.Contains("gateway4: 10.90.0.17", config);
        Assert.Contains("addresses: [10.90.0.17]", config);
        Assert.Contains("to: 10.90.0.32/28", config);
        Assert.Contains("via: 10.90.0.17", config);
    }

    [Theory]
    [InlineData("999.1.1.1", 24, "10.90.0.1", null)]
    [InlineData("10.90.0.20", 0, "10.90.0.1", null)]
    [InlineData("10.90.0.20", 33, "10.90.0.1", null)]
    [InlineData("10.90.0.20", 24, "not-an-ip", null)]
    [InlineData("10.90.0.20", 24, "10.90.0.1", "bad-route")]
    public void BuildCloudInitNetworkConfig_RejectsInvalidStaticNetworkValues(
        string ipAddress, int prefixLength, string gateway, string? route)
    {
        var routes = route is null ? [] : new List<string> { route };
        var request = new CreateVmRequest
        {
            Interfaces =
            [
                new VmNetworkInterfaceRequest
                {
                    BridgeName = "tl12-entry",
                    InterfaceName = "eth0",
                    MacAddress = "02:42:ac:10:00:04",
                    IpAddress = ipAddress,
                    PrefixLength = prefixLength,
                    Gateway = gateway,
                    DnsServers = [gateway],
                    Routes = routes,
                    IsPrimary = true
                }
            ]
        };

        Assert.Throws<ArgumentException>(() => KvmService.BuildCloudInitNetworkConfig(request));
    }

    [Fact]
    public void BuildVirtInstallCloudInitArguments_UsesDirectCloudInitWhenRequested()
    {
        var files = new CloudInitSeedFiles(
            "/var/lib/gzctf/images/seed/vm1/user-data",
            "/var/lib/gzctf/images/seed/vm1/meta-data",
            "/var/lib/gzctf/images/seed/vm1/network-config",
            "/var/lib/gzctf/images/seed/vm1/seed.iso");

        var args = KvmService.BuildVirtInstallCloudInitArguments(files, useDirectCloudInit: true);

        Assert.Contains("--cloud-init", args);
        Assert.Contains("user-data='/var/lib/gzctf/images/seed/vm1/user-data'", args);
        Assert.Contains("meta-data='/var/lib/gzctf/images/seed/vm1/meta-data'", args);
        Assert.Contains("network-config='/var/lib/gzctf/images/seed/vm1/network-config'", args);
        Assert.DoesNotContain("seed.iso,device=cdrom", args);
    }

    [Fact]
    public void BuildVirtInstallCloudInitArguments_FallsBackToCdromSeedIso()
    {
        var files = new CloudInitSeedFiles(
            "/var/lib/gzctf/images/seed/vm1/user-data",
            "/var/lib/gzctf/images/seed/vm1/meta-data",
            "/var/lib/gzctf/images/seed/vm1/network-config",
            "/var/lib/gzctf/images/seed/vm1/seed.iso");

        var args = KvmService.BuildVirtInstallCloudInitArguments(files, useDirectCloudInit: false);

        Assert.Equal("--disk path='/var/lib/gzctf/images/seed/vm1/seed.iso',device=cdrom", args);
    }
}
