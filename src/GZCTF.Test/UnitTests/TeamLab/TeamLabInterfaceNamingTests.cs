using System;
using System.Linq;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.TeamLab.Contracts.Execution;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

/// <summary>
/// Interface names must stay unique after being fitted into the kernel's 15-character limit.
/// Two interfaces sharing a name makes "ip link delete" for a later one tear down an earlier veth
/// pair, silently wiring a segment to the wrong bridge.
/// </summary>
public class TeamLabInterfaceNamingTests
{
    [Theory]
    [InlineData(14)] // "<ns>h0" and "<ns>h1" both truncate to "<ns>h"
    [InlineData(15)] // "<ns>h0" and "<ns>n0" both truncate to "<ns>"
    public void DerivedRouterInterfaces_StayUniqueForLongNamespaces(int namespaceLength)
    {
        var ns = new string('t', namespaceLength);

        var derived = Enumerable.Range(0, 4)
            .SelectMany(index => new[]
            {
                TeamLabNetworkPrimitives.TrimInterfaceName($"{ns}h{index}"),
                TeamLabNetworkPrimitives.TrimInterfaceName($"{ns}n{index}")
            })
            .ToArray();

        Assert.Equal(derived.Length, derived.Distinct(StringComparer.Ordinal).Count());
        Assert.All(derived, name => Assert.True(name.Length <= 15, $"'{name}' exceeds 15 characters"));
    }

    [Fact]
    public void ShortNamesArePreservedVerbatim()
    {
        Assert.Equal("tlr12-3h0", TeamLabNetworkPrimitives.TrimInterfaceName("tlr12-3h0"));
    }

    [Fact]
    public void NamingIsDeterministic()
    {
        var first = TeamLabNetworkPrimitives.TrimInterfaceName("tlr123456789-42n7");
        var second = TeamLabNetworkPrimitives.TrimInterfaceName("tlr123456789-42n7");

        Assert.Equal(first, second);
    }

    [Fact]
    public void AgentAndControlPlaneDeriveTheSameName()
    {
        // Both sides name the same interfaces; diverging algorithms would make the agent configure
        // one device while the control plane records another.
        const string value = "tlr987654321-123n0";

        Assert.Equal(
            TeamLabResourceNameFactory.LinuxName(value),
            TeamLabNetworkPrimitives.TrimInterfaceName(value));
    }

    [Fact]
    public void WorkloadGuestInterfaces_AreIndependentFromTopologyKeys()
    {
        var names = Enumerable.Range(0, TeamLabTopologyValidator.MaxInterfacesPerAsset)
            .Select(TeamLabResourceNameFactory.WorkloadGuestInterface)
            .ToArray();

        Assert.Equal("eth0", names[0]);
        Assert.Equal("eth7", names[^1]);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.True(name.Length <= 15));
    }

    [Fact]
    public void ContainerAttachmentCleanupUsesTheSameHostInterfaceNameAsApply()
    {
        var plan = new TeamLabExecutionPlanV2(
            7, Guid.Parse("019fa217-fcee-73af-bb45-1bc400000001"), 2, "node-a",
            "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            string.Empty, false, [], [], []);

        var first = LinuxNetworkAttachmentService.HostInterfaceName(plan, "web", "network-a");
        var second = LinuxNetworkAttachmentService.HostInterfaceName(plan, "web", "network-a");

        Assert.Equal(first, second);
        Assert.StartsWith("tlh", first, StringComparison.Ordinal);
        Assert.True(first.Length <= 15);
    }
}
