using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services;
using GZCTF.Agent.Services.Vm;
using GZCTF.TeamLab.Contracts.Execution;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabManagedVmNetworkTests
{
    const string Mac = "02:42:29:19:d6:14";
    const string Matching = "<network><interface mac='02:42:29:19:d6:14' name='ens3' dhcp='false'><address ip='10.96.1.20' prefix='24'/><gateway ip='10.96.1.1'/><dns ip='10.96.1.53'/></interface></network>";
    const string Drift = "<network><interface mac='02:42:29:19:d6:14' name='ens3' dhcp='true'><address ip='10.96.1.99' prefix='24'/></interface></network>";

    [Theory]
    [InlineData(TeamLabGuestNetworkMode.Dhcp)]
    [InlineData(TeamLabGuestNetworkMode.Preconfigured)]
    public async Task UnmanagedModes_DoNotInvokeGuestControl(TeamLabGuestNetworkMode mode)
    {
        var plan = Plan(mode);
        var guest = new FakeGuest();
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(0, guest.ReadyCalls);
        Assert.Empty(guest.Commands);
    }

    [Fact]
    public async Task MatchingGuest_DoesNotReapplyOrRebuildInterfaces()
    {
        var plan = Plan();
        var guest = new FakeGuest();
        guest.Reads.Enqueue(Matching);
        var progress = new List<TeamLabVmNetworkProgress>();
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None, progress.Add);
        Assert.True(result.Success);
        Assert.Single(guest.Commands);
        Assert.Contains(progress, item => item.Stage == "guest-network-apply" && item.Outcome == "already_applied");
        Assert.Contains(progress, item => item.Stage == "guest-network-verify" && item.Outcome == "succeeded");
    }

    [Fact]
    public async Task Drift_IsAppliedThenVerifiedFromASeparateLiveRead()
    {
        var plan = Plan();
        var guest = new FakeGuest();
        guest.Reads.Enqueue(Drift);
        guest.Reads.Enqueue(Matching);
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(new[] { "teamlab-network-read", "teamlab-network-apply", "teamlab-network-read" }, guest.Commands.Select(item => item.StepId));
    }

    [Fact]
    public async Task AlreadyAppliedVerification_ReportsDriftWithoutChangingGuest()
    {
        var plan = Plan();
        var guest = new FakeGuest();
        guest.Reads.Enqueue(Drift);
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], true, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("guest_network_drift", result.ErrorCode);
        Assert.Single(guest.Commands);
    }

    [Fact]
    public async Task MissingQga_CannotReportNetworkReady()
    {
        var plan = Plan();
        var guest = new FakeGuest { Ready = false };
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("guest-ready", result.Stage);
        Assert.Equal("guest_qga_unavailable", result.ErrorCode);
        Assert.Empty(guest.Commands);
    }

    [Fact]
    public async Task MissingDriverOrMac_FailsBeforeAnyConfiguration()
    {
        var plan = Plan();
        var guest = new FakeGuest();
        guest.Reads.Enqueue("<network/>");
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("guest_network_interface_missing", result.ErrorCode);
        Assert.Single(guest.Commands);
    }

    [Fact]
    public async Task SuccessfulApply_WithoutLiveConvergenceTimesOut()
    {
        var plan = Plan();
        var guest = new FakeGuest();
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("guest-network-verify", result.Stage);
        Assert.Equal("guest_network_verify_timeout", result.ErrorCode);
        Assert.Contains(guest.Commands, item => item.StepId == "teamlab-network-apply");
    }

    [Fact]
    public async Task ApplyTimeout_ReturnsRetryableDiagnosisWithoutRawCommandOutput()
    {
        var plan = Plan();
        var guest = new FakeGuest { ApplyResult = new(false, true, null, "timeout", "sensitive user data", "private credentials") };
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("guest_network_apply_timeout", result.ErrorCode);
        Assert.DoesNotContain("sensitive", result.Message);
        Assert.DoesNotContain("credentials", result.Message);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAndDoesNotReturnSuccess()
    {
        var plan = Plan();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(new FakeGuest()).ApplyAsync(plan, plan.Assets[0], false, source.Token));
    }

    [Fact]
    public async Task ForeignVmIdentity_IsRejectedBeforeQga()
    {
        var plan = Plan();
        var guest = new FakeGuest();
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0] with { ResourceId = "foreign-vm" }, false, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("guest_identity_conflict", result.ErrorCode);
        Assert.Equal(0, guest.ReadyCalls);
    }

    [Fact]
    public async Task RecreatedGeneration_AppliesAndReadsOnlyItsNewDomainIdentity()
    {
        var plan = Plan();
        var domain = TeamLabExecutionIdentityV2.VmDomainName(plan.RuntimePublicId, 2, plan.ShardKey, "linux-vm");
        var asset = plan.Assets[0] with { ResourceId = domain, DomainIdentity = domain };
        plan = plan with { Generation = 2, Assets = [asset] };
        var guest = new FakeGuest();
        guest.Reads.Enqueue(Drift);
        guest.Reads.Enqueue(Matching);
        var result = await Service(guest).ApplyAsync(plan, asset, false, CancellationToken.None);
        Assert.True(result.Success);
        Assert.NotEmpty(guest.VmNames);
        Assert.All(guest.VmNames, name => Assert.Equal(domain, name));
    }

    [Fact]
    public async Task MissingGuestTools_IsExplicitAndDoesNotEchoGuestOutput()
    {
        var plan = Plan();
        var guest = new FakeGuest { ReadFailure = new(false, false, 78, "non-zero-exit", "private data",
            "GZCTF_NETWORK_TOOLS_MISSING additional private data") };
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.Equal("guest_network_tools_unavailable", result.ErrorCode);
        Assert.DoesNotContain("private", result.Message);
        Assert.Single(guest.Commands);
    }

    [Fact]
    public async Task NativeIdentityChangeWhileWaitingForQga_BlocksAllGuestCommands()
    {
        var plan = Plan();
        var guest = new FakeGuest();
        var checks = 0;
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None,
            verifyIdentity: _ => Task.FromResult(++checks == 1));
        Assert.False(result.Success);
        Assert.Equal("guest_identity_conflict", result.ErrorCode);
        Assert.Equal(1, guest.ReadyCalls);
        Assert.Empty(guest.Commands);
    }

    [Fact]
    public async Task NativeIdentityChangeDuringReadback_CannotReportVerified()
    {
        var plan = Plan();
        var guest = new FakeGuest();
        guest.Reads.Enqueue(Matching);
        var checks = 0;
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], true, CancellationToken.None,
            verifyIdentity: _ => Task.FromResult(++checks < 3));
        Assert.False(result.Success);
        Assert.Equal("guest_identity_conflict", result.ErrorCode);
        Assert.Single(guest.Commands);
    }

    [Fact]
    public void Requirements_ResolveEachMacAndExplicitEmptyDnsAndNoDefaultGateway()
    {
        var plan = Plan();
        var first = plan.Assets[0].NetworkAttachments[0];
        var second = new TeamLabAssetNetworkAttachmentV2("core", "vm-core", "eth1", "192.168.20.20", null, false,
            InterfaceKey: "core-nic", MacAddress: "02:42:29:19:d6:15", PrefixLength: 24,
            GuestInterfaceName: "core0", UseDefaultGateway: false, DnsServers: [],
            StaticRoutes: [new("172.16.0.0/16", "192.168.20.1", 25)]);
        var network = new TeamLabNetworkIntentV2("core", "192.168.20.0/24", "192.168.20.1",
            [new("vm-core", "linux-vm", "02:42:29:19:d6:15", "192.168.20.20")], [], [], DnsServerIp: "192.168.20.53");
        plan = plan with { Networks = [plan.Networks[0], network] };
        var asset = plan.Assets[0] with { NetworkAttachments = [first, second] };
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, asset);
        Assert.Equal("10.96.1.53", Assert.Single(desired[0].DnsServers));
        Assert.Equal(Mac, desired[0].MacAddress);
        Assert.Null(desired[0].Name);
        Assert.Empty(desired[1].DnsServers);
        Assert.Null(desired[1].Gateway);
        Assert.Equal("core0", desired[1].Name);
        Assert.Equal(25, Assert.Single(desired[1].Routes).Metric);
    }

    [Theory]
    [InlineData("eth0;shutdown")]
    [InlineData("eth0\nfoo")]
    [InlineData("'$(evil)'")]
    [InlineData("this-interface-name-is-too-long")]
    public void UnsafeGuestNames_AreRejectedBeforeGeneratingCommands(string name)
    {
        var plan = Plan();
        var asset = plan.Assets[0] with { NetworkAttachments = [plan.Assets[0].NetworkAttachments[0] with { GuestInterfaceName = name }] };
        Assert.Throws<ArgumentException>(() => TeamLabVmNetworkService.ResolveInterfaces(plan, asset));
    }

    [Fact]
    public void WrongCurrentMac_CannotSelectAnotherGuestDevice()
    {
        var plan = Plan();
        var asset = plan.Assets[0] with { NetworkAttachments = [plan.Assets[0].NetworkAttachments[0] with { MacAddress = "02:42:29:19:d6:15" }] };
        Assert.Throws<ArgumentException>(() => TeamLabVmNetworkService.ResolveInterfaces(plan, asset));
    }

    [Fact]
    public void ManagedLinuxSeed_UsesMacWithoutDhcpLeaseAndRespectsNoGatewayOrDns()
    {
        var plan = Plan();
        var asset = plan.Assets[0] with { NetworkAttachments = [plan.Assets[0].NetworkAttachments[0] with
            { GuestInterfaceName = "field0", UseDefaultGateway = false, GatewayIp = null, DnsServers = [], StaticRoutes = [new("172.16.0.0/16", "10.96.1.1", 30)] }] };
        var config = LibvirtTeamLabProvider.BuildNoCloudNetworkConfig(plan, asset);
        Assert.NotNull(config);
        Assert.Contains("macaddress: \"" + Mac + "\"", config);
        Assert.Contains("set-name: \"field0\"", config);
        Assert.Contains("dhcp4: false", config);
        Assert.Contains("addresses: [10.96.1.20/24]", config);
        Assert.Contains("addresses: []", config);
        Assert.Contains("metric: 30", config);
        Assert.DoesNotContain("to: default", config);
        Assert.DoesNotContain("set-name: \"eth0\"", config);
        Assert.Empty(plan.Networks[0].DhcpLeases ?? []);
    }

    [Theory]
    [InlineData(TeamLabGuestOperatingSystem.Linux, TeamLabGuestNetworkMode.Preconfigured)]
    [InlineData(TeamLabGuestOperatingSystem.Windows, TeamLabGuestNetworkMode.ManagedStatic)]
    public void PreconfiguredAndWindows_DoNotReceiveNoCloudSeed(TeamLabGuestOperatingSystem os, TeamLabGuestNetworkMode mode)
    {
        var plan = Plan(mode, os);
        Assert.Null(LibvirtTeamLabProvider.BuildNoCloudNetworkConfig(plan, plan.Assets[0]));
    }

    [Fact]
    public void Readback_RequiresExactAddressPrefixDnsOrderAndDefaultRoute()
    {
        var plan = Plan();
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, plan.Assets[0]);
        foreach (var xml in new[] {
                     Matching.Replace("prefix='24'", "prefix='25'"),
                     Matching.Replace("10.96.1.53", "10.96.1.54"),
                     Matching.Replace("<gateway ip='10.96.1.1'/>", ""),
                     Matching.Replace("dhcp='false'", "dhcp='true'") })
            Assert.False(TeamLabVmNetworkService.Matches(desired, TeamLabVmNetworkService.ParseSnapshot(xml), out _));
        Assert.True(TeamLabVmNetworkService.Matches(desired, TeamLabVmNetworkService.ParseSnapshot(Matching), out _));
    }

    [Fact]
    public void Readback_StaticRouteIsBoundToTheTargetMacAndMetric()
    {
        var plan = Plan();
        var asset = plan.Assets[0] with { NetworkAttachments = [plan.Assets[0].NetworkAttachments[0] with
            { StaticRoutes = [new("172.16.0.0/16", "10.96.1.1", 25)] }] };
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, asset);
        var right = Matching.Replace("</interface>", "<route destination='172.16.0.0/16' nextHop='10.96.1.1' metric='25'/></interface>");
        Assert.True(TeamLabVmNetworkService.Matches(desired, TeamLabVmNetworkService.ParseSnapshot(right), out _));
        Assert.False(TeamLabVmNetworkService.Matches(desired, TeamLabVmNetworkService.ParseSnapshot(right.Replace("metric='25'", "metric='26'")), out _));
        Assert.False(TeamLabVmNetworkService.Matches(desired, TeamLabVmNetworkService.ParseSnapshot(Matching), out _));
    }

    [Fact]
    public void WindowsReadback_ParsesWmiMasksWithoutAssumingAdapterOrder()
    {
        var snapshot = TeamLabVmNetworkService.ParseSnapshot(Matching.Replace("prefix='24'", "mask='255.255.255.0'"));
        Assert.Equal(new GuestAddress("10.96.1.20", 24), Assert.Single(Assert.Single(snapshot).Addresses));
        Assert.Equal(-1, TeamLabVmNetworkService.PrefixFromMask("255.0.255.0"));
        Assert.Equal("255.255.255.0", TeamLabVmNetworkService.MaskFromPrefix(24));
        Assert.Equal("128.0.0.0", TeamLabVmNetworkService.MaskFromPrefix(1));
        Assert.Equal("255.255.255.255", TeamLabVmNetworkService.MaskFromPrefix(32));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowsReadback_IgnoresIpv6DnsForEmptyOrNonemptyIpv4Requirements(bool ipv4Dns)
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var asset = plan.Assets[0] with { NetworkAttachments = [plan.Assets[0].NetworkAttachments[0] with
            { DnsServers = ipv4Dns ? ["10.96.1.53"] : [] }] };
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, asset);
        var xml = Matching.Replace("<dns ip='10.96.1.53'/>",
            "<dns ip='fec0:0:0:ffff::1'/><dns ip='2001:db8::53'/>" + (ipv4Dns ? "<dns ip='10.96.1.53'/>" : ""));
        var actual = TeamLabVmNetworkService.ParseSnapshot(xml);
        Assert.Equal(ipv4Dns ? 1 : 0, Assert.Single(actual).DnsServers.Count);
        Assert.True(TeamLabVmNetworkService.Matches(desired, actual, out _));
    }

    [Fact]
    public void WindowsCommands_UseOnePs2CompatibleWmiAndNetshImplementation()
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, plan.Assets[0]);
        var command = TeamLabVmNetworkService.BuildApplyCommand(plan.Assets[0], desired);
        Assert.Equal(VmBootstrapService.WindowsPowerShellPath, command.Path);
        Assert.Contains("-EncodedCommand", command.Arguments);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(command.Arguments.Last()));
        Assert.Contains("Get-WmiObject", script);
        Assert.Contains("netsh.exe", script);
        Assert.Contains("gateway = 'none'", script);
        Assert.Contains("SetDNSServerSearchOrder", script);
        Assert.Contains("InterfaceIndex", script);
        Assert.DoesNotContain("ConvertFrom-Json", script);
        Assert.DoesNotContain("Get-NetAdapter", script);
        Assert.DoesNotContain("NetTCPIP", script);
        Assert.DoesNotContain("Remove-NetIPAddress", script);
        Assert.DoesNotContain("Invoke-Expression", script);
    }

    [Theory]
    [InlineData("guest-ready")]
    [InlineData("guest-network-apply")]
    [InlineData("guest-network-verify")]
    public void Protocol_AllowsRealGuestNetworkStages(string stage) => Assert.True(TeamLabExecutionProtocolV2.IsStage(stage));

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void ManagedNetworkCapability_RequiresExecutionNativeLibvirtAndQgaTransport(
        bool execution, bool libvirt, bool virsh, bool kvm, bool expected)
    {
        var features = new List<string>();
        if (kvm) features.Add(AgentFeatureIds.Kvm);
        if (execution) features.Add(AgentFeatureIds.TeamLabExecutionPlan);
        if (libvirt) features.Add(AgentFeatureIds.TeamLabNativeLibvirt);
        Assert.Equal(expected, AgentCapabilityService.SupportsManagedGuestNetwork(features, virsh));
    }

    static TeamLabVmNetworkService Service(FakeGuest guest) => new(guest)
    {
        VerifyTimeout = TimeSpan.FromMilliseconds(20), PollInterval = TimeSpan.FromMilliseconds(2)
    };

    static TeamLabExecutionPlanV2 Plan(TeamLabGuestNetworkMode mode = TeamLabGuestNetworkMode.ManagedStatic,
        TeamLabGuestOperatingSystem os = TeamLabGuestOperatingSystem.Linux)
    {
        var runtime = Guid.Parse("019fa217-fcee-73af-bb45-1bc400000001");
        var domain = TeamLabExecutionIdentityV2.VmDomainName(runtime, 1, "shard", "linux-vm");
        var network = new TeamLabNetworkIntentV2("field", "10.96.1.0/24", "10.96.1.1",
            [new("vm-port", "linux-vm", Mac, "10.96.1.20")], [], [], DnsServerIp: "10.96.1.53");
        var asset = new TeamLabAssetExecutionSpecV2("linux-vm", "vm", domain, "sha256:" + new string('a', 64), domain,
            79, 2, 2048, [new("field", "vm-port", "eth0", "10.96.1.20", "10.96.1.1", true,
                InterfaceKey: "field-nic", MacAddress: Mac, PrefixLength: 24)], [], OperatingSystem: os, NetworkMode: mode);
        return new(1, runtime, 1, "shard", "sha256:" + new string('b', 64), "sha256:" + new string('c', 64),
            false, [network], [asset], []);
    }

    sealed class FakeGuest : IVmGuestAgentClient
    {
        public bool Ready { get; init; } = true;
        public int ReadyCalls { get; private set; }
        public Queue<string> Reads { get; } = new();
        public List<VmGuestCommandRequest> Commands { get; } = [];
        public List<string> VmNames { get; } = [];
        public VmGuestCommandResponse? ReadFailure { get; init; }
        public VmGuestCommandResponse ApplyResult { get; init; } = new(true, false, 0, "succeeded", null, null);
        public Task<VmGuestStatusResponse> WaitReadyAsync(string vmName, TimeSpan timeout, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ReadyCalls++;
            VmNames.Add(vmName);
            return Task.FromResult(new VmGuestStatusResponse(Ready, "ready"));
        }
        public Task<VmGuestCommandResponse> ExecuteAsync(string vmName, VmGuestCommandRequest command, CancellationToken token,
            Func<CancellationToken, Task<bool>>? verifyIdentity = null)
        {
            token.ThrowIfCancellationRequested();
            Commands.Add(command);
            VmNames.Add(vmName);
            return Task.FromResult(command.StepId == "teamlab-network-apply" ? ApplyResult :
                ReadFailure ?? new(true, false, 0, "succeeded", Reads.Count > 0 ? Reads.Dequeue() : Drift, null));
        }
    }
}
