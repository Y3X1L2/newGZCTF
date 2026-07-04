using System;
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
}
