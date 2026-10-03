using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    [InlineData(0)]
    [InlineData(2)]
    public void DhcpLinuxSeed_LeavesPerPortDnsGatewayAndClasslessRoutesToTheLease(int dnsCount)
    {
        var plan = Plan(TeamLabGuestNetworkMode.Dhcp);
        var dns = dnsCount == 0 ? Array.Empty<string>() : new[] { "192.168.20.53", "192.168.20.54" };
        var routes = new[] { new TeamLabGuestRouteV2("172.16.0.0/16", "192.168.20.1") };
        var first = plan.Assets[0].NetworkAttachments[0] with
            { UseDefaultGateway = false, GatewayIp = null, DnsServers = [] };
        var second = new TeamLabAssetNetworkAttachmentV2("core", "vm-core", "eth1", "192.168.20.20", "192.168.20.1", false,
            InterfaceKey: "core-nic", MacAddress: "02:42:29:19:d6:15", PrefixLength: 24,
            UseDefaultGateway: true, DnsServers: dns, StaticRoutes: routes);
        var firstNetwork = plan.Networks[0] with
            { DhcpLeases = [new(Mac, "10.96.1.20", "linux-vm", false, [], [])] };
        var secondNetwork = new TeamLabNetworkIntentV2("core", "192.168.20.0/24", "192.168.20.1",
            [new("vm-core", "linux-vm", "02:42:29:19:d6:15", "192.168.20.20")], [], [],
            DhcpLeases: [new("02:42:29:19:d6:15", "192.168.20.20", "linux-vm", true, dns, routes)]);
        var asset = plan.Assets[0] with { NetworkAttachments = [first, second] };
        plan = plan with { Networks = [firstNetwork, secondNetwork], Assets = [asset] };

        var config = LibvirtTeamLabProvider.BuildNoCloudNetworkConfig(plan, asset);

        Assert.NotNull(config);
        Assert.Contains("macaddress: \"" + Mac + "\"", config);
        Assert.Contains("macaddress: \"02:42:29:19:d6:15\"", config);
        Assert.Equal(2, config.Split("dhcp4: true", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("addresses:", config);
        Assert.DoesNotContain("nameservers:", config);
        Assert.DoesNotContain("routes:", config);
        Assert.DoesNotContain("gateway", config);
        Assert.DoesNotContain("192.168.20.", config);
        Assert.DoesNotContain("172.16.0.0", config);
        Assert.False(second.Primary);
        Assert.True(second.UseDefaultGateway);
        Assert.Equal(dnsCount, Assert.Single(secondNetwork.DhcpLeases!).DnsServers!.Count);
        Assert.Equal(routes, Assert.Single(secondNetwork.DhcpLeases!).StaticRoutes);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LinuxLiveReadback_NormalizesIpRouteHostDestinationsBeforeVerification(bool bareHostRoute)
    {
        var plan = Plan();
        var asset = plan.Assets[0] with { NetworkAttachments = [plan.Assets[0].NetworkAttachments[0] with
            { StaticRoutes = [new("172.16.100.8/32", "10.96.1.1", 25)] }] };
        plan = plan with { Assets = [asset] };
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, asset);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(TeamLabVmNetworkService.BuildLinuxReadScript(desired)));
        var fixture = $$"""
            import base64, json, subprocess
            from unittest import mock
            # Simulate the OS command output, then execute the actual Agent readback script.
            outputs = {
                ('ip','-j','-4','address','show'): json.dumps([{'ifname':'ens3','addr_info':[{'family':'inet','local':'10.96.1.20','prefixlen':24}]}]),
                ('ip','-j','-4','route','show','table','main'): json.dumps([
                    {'dst':'default','dev':'ens3','gateway':'10.96.1.1'},
                    {'dst':'{{(bareHostRoute ? "172.16.100.8" : "172.16.100.8/32")}}','dev':'ens3','gateway':'10.96.1.1','metric':25}]),
                ('ip','-j','link','show'): json.dumps([{'ifname':'ens3','address':'02:42:29:19:d6:14'}]),
                ('resolvectl','dns','ens3'): 'Link 3 (ens3): 10.96.1.53'
            }
            def output(args, **kwargs): return outputs[tuple(args)]
            with mock.patch('subprocess.check_output',side_effect=output), mock.patch('shutil.which',return_value='/mock/tool'):
                exec(base64.b64decode('{{payload}}').decode())
            """;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "python" : "python3",
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(fixture);
        process.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var read = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var xml = await read;
        Assert.True(process.ExitCode == 0, await error);
        Assert.Contains("destination=\"172.16.100.8/32\"", xml);
        var guest = new FakeGuest();
        guest.Reads.Enqueue(xml);
        var result = await Service(guest).ApplyAsync(plan, asset, true, CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Single(guest.Commands);
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
    [InlineData(5, 30L, true)]
    [InlineData(0, 25L, true)]
    [InlineData(null, 30L, false)]
    [InlineData(5, 4L, false)]
    [InlineData(5, -1L, false)]
    [InlineData(5, 4294967295L, false)]
    public async Task WindowsWmiRouteMetric_UsesRouteCostAndRejectsUnknownCosts(int? interfaceMetric, long totalMetric, bool valid)
    {
        // Windows PowerShell is a guest requirement, so this script execution gate runs on Windows hosts.
        if (!OperatingSystem.IsWindows()) return;
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var asset = plan.Assets[0] with { NetworkAttachments = [plan.Assets[0].NetworkAttachments[0] with
            { UseDefaultGateway = false, GatewayIp = null, DnsServers = [], StaticRoutes = [new("172.16.0.0/16", "10.96.1.1", 25)] }] };
        plan = plan with { Assets = [asset] };
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, asset);
        var apply = Convert.ToBase64String(Encoding.UTF8.GetBytes(TeamLabVmNetworkService.BuildWindowsApplyScript(desired)));
        var read = Convert.ToBase64String(Encoding.UTF8.GetBytes(TeamLabVmNetworkService.BuildWindowsReadScript(desired)));
        var fixture = $$"""
            $ErrorActionPreference='Stop'
            $stateDir=Join-Path $env:TEMP ('gzctf-route-metric-' + [Guid]::NewGuid().ToString('N'))
            $script:Cfg=New-Object PSObject -Property @{MACAddress='02:42:29:19:d6:14';Index=7;InterfaceIndex=19;IPConnectionMetric={{interfaceMetric?.ToString() ?? "$null"}};DHCPEnabled=$false;IPAddress=@('10.96.1.20');IPSubnet=@('255.255.255.0');DefaultIPGateway=@();DNSServerSearchOrder=@()}
            $script:Adapter=New-Object PSObject -Property @{Index=7;NetConnectionID='ens3'}
            $script:Route=New-Object PSObject -Property @{InterfaceIndex=19;Destination='172.16.0.0';Mask='255.255.0.0';NextHop='10.96.1.1';Metric1={{totalMetric}}}
            function Get-WmiObject {
              param($Class,$Filter)
              switch ($Class) {
                'Win32_NetworkAdapterConfiguration' { return $script:Cfg }
                'Win32_NetworkAdapter' { return $script:Adapter }
                'Win32_IP4RouteTable' { return $script:Route }
                default { throw 'Unexpected WMI class' }
              }
            }
            function Invoke-Netsh-Test { throw 'A correct existing route must not be deleted or added again' }
            $apply=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{apply}}'))
            $apply=$apply.Replace('$stateRoot = Join-Path $env:ProgramData ''GZCTF\TeamLab''', '$stateRoot = $stateDir')
            $apply=$apply.Replace('& "$env:SystemRoot\System32\netsh.exe"','Invoke-Netsh-Test')
            $read=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{read}}')).Replace('[Console]::Out.Write($doc.OuterXml)','Write-Output $doc.OuterXml')
            $read=$read.Replace('$stateRoot = Join-Path $env:ProgramData ''GZCTF\TeamLab''', '$stateRoot = $stateDir')
            try {
              & ([scriptblock]::Create($apply))
              & ([scriptblock]::Create($apply))
              & ([scriptblock]::Create($read))
            } finally {
              if (Test-Path -LiteralPath $stateDir) { Remove-Item -LiteralPath $stateDir -Recurse -Force }
            }
            """;
        var result = await RunPowerShellFixtureAsync(fixture);
        if (!valid)
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Route/interface metric", result.Error);
            return;
        }
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("metric=\"25\"", result.Output);
        Assert.True(TeamLabVmNetworkService.Matches(desired, TeamLabVmNetworkService.ParseSnapshot(result.Output), out var mismatch), mismatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowsFreshOrUnspecifiedMetric_DoesNotRequireUnrelatedOsRouteCosts(bool requestedRouteWithoutMetric)
    {
        if (!OperatingSystem.IsWindows()) return;
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var asset = plan.Assets[0] with { NetworkAttachments = [plan.Assets[0].NetworkAttachments[0] with
            { UseDefaultGateway = false, GatewayIp = null, DnsServers = [], StaticRoutes = requestedRouteWithoutMetric ? [new("172.16.0.0/16", "10.96.1.1")] : [] }] };
        plan = plan with { Assets = [asset] };
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, asset);
        var apply = Convert.ToBase64String(Encoding.UTF8.GetBytes(TeamLabVmNetworkService.BuildWindowsApplyScript(desired)));
        var read = Convert.ToBase64String(Encoding.UTF8.GetBytes(TeamLabVmNetworkService.BuildWindowsReadScript(desired)));
        var fixture = $$"""
            $ErrorActionPreference='Stop'
            $stateDir=Join-Path $env:TEMP ('gzctf-apipa-metric-' + [Guid]::NewGuid().ToString('N'))
            $script:Cfg=New-Object PSObject -Property @{MACAddress='02:42:29:19:d6:14';Index=7;InterfaceIndex=19;IPConnectionMetric=$null;DHCPEnabled={{(requestedRouteWithoutMetric ? "$false" : "$true")}};IPAddress=@('{{(requestedRouteWithoutMetric ? "10.96.1.20" : "169.254.10.20")}}');IPSubnet=@('{{(requestedRouteWithoutMetric ? "255.255.255.0" : "255.255.0.0")}}');DefaultIPGateway=@();DNSServerSearchOrder=@()}
            $script:Adapter=New-Object PSObject -Property @{Index=7;NetConnectionID='ens3'}
            $script:Routes=@(New-Object PSObject -Property @{InterfaceIndex=19;Destination='169.254.0.0';Mask='255.255.0.0';NextHop='0.0.0.0';Metric1=4294967295})
            if ({{(requestedRouteWithoutMetric ? "$true" : "$false")}}) { $script:Routes+=New-Object PSObject -Property @{InterfaceIndex=19;Destination='172.16.0.0';Mask='255.255.0.0';NextHop='10.96.1.1';Metric1=$null} }
            function Get-WmiObject {
              param($Class,$Filter)
              switch ($Class) {
                'Win32_NetworkAdapterConfiguration' { return $script:Cfg }
                'Win32_NetworkAdapter' { return $script:Adapter }
                'Win32_IP4RouteTable' { return $script:Routes }
                default { throw 'Unexpected WMI class' }
              }
            }
            function Invoke-Netsh-Test { throw 'Existing unspecified route cost must not cause a netsh change' }
            $apply=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{apply}}'))
            $apply=$apply.Replace('$stateRoot = Join-Path $env:ProgramData ''GZCTF\TeamLab''', '$stateRoot = $stateDir').Replace('& "$env:SystemRoot\System32\netsh.exe"','Invoke-Netsh-Test')
            $read=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{read}}'))
            $read=$read.Replace('$stateRoot = Join-Path $env:ProgramData ''GZCTF\TeamLab''', '$stateRoot = $stateDir').Replace('& "$env:SystemRoot\System32\netsh.exe"','Invoke-Netsh-Test').Replace('[Console]::Out.Write($doc.OuterXml)','Write-Output $doc.OuterXml')
            try {
              if ({{(requestedRouteWithoutMetric ? "$true" : "$false")}}) { & ([scriptblock]::Create($apply)); & ([scriptblock]::Create($apply)) }
              & ([scriptblock]::Create($read))
            } finally { if (Test-Path -LiteralPath $stateDir) { Remove-Item -LiteralPath $stateDir -Recurse -Force } }
            """;
        var result = await RunPowerShellFixtureAsync(fixture);
        Assert.True(result.ExitCode == 0, result.Error);
        var snapshot = TeamLabVmNetworkService.ParseSnapshot(result.Output);
        if (requestedRouteWithoutMetric)
        {
            Assert.Null(Assert.Single(Assert.Single(snapshot).Routes).Metric);
            Assert.True(TeamLabVmNetworkService.Matches(desired, snapshot, out var mismatch), mismatch);
        }
        else
        {
            Assert.True(Assert.Single(snapshot).Dhcp);
            Assert.Empty(Assert.Single(snapshot).Routes);
            var guest = new FakeGuest(); guest.Reads.Enqueue(result.Output);
            guest.Reads.Enqueue(Matching.Replace("<gateway ip='10.96.1.1'/>", "").Replace("<dns ip='10.96.1.53'/>", ""));
            var initialized = await Service(guest).ApplyAsync(plan, asset, false, CancellationToken.None);
            Assert.True(initialized.Success, initialized.Message);
            Assert.Equal(new[] { "teamlab-network-read", "teamlab-network-apply", "teamlab-network-read" }, guest.Commands.Select(command => command.StepId));
        }
    }

    [Theory]
    [InlineData("active")]
    [InlineData("persistent")]
    [InlineData("both")]
    [InlineData("already-absent")]
    public async Task WindowsOwnedRouteRemoval_VerifiesBothStoresAndRetainsOwnershipUntilRetry(string failedStore)
    {
        if (!OperatingSystem.IsWindows()) return;
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var asset = plan.Assets[0] with { NetworkAttachments = [plan.Assets[0].NetworkAttachments[0] with
            { UseDefaultGateway = false, GatewayIp = null, DnsServers = [], StaticRoutes = [] }] };
        plan = plan with { Assets = [asset] };
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, asset);
        var apply = Convert.ToBase64String(Encoding.UTF8.GetBytes(TeamLabVmNetworkService.BuildWindowsApplyScript(desired)));
        var read = Convert.ToBase64String(Encoding.UTF8.GetBytes(TeamLabVmNetworkService.BuildWindowsReadScript(desired)));
        var fixture = $$"""
            $ErrorActionPreference='Stop'
            $stateDir=Join-Path $env:TEMP ('gzctf-route-removal-' + [Guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($stateDir)
            $statePath=Join-Path $stateDir 'network.xml'
            $previous='<network><interface mac="02:42:29:19:d6:14"><route destination="172.16.0.0/16" nextHop="10.96.1.1" metric="25"/></interface></network>'
            [IO.File]::WriteAllText($statePath,$previous)
            $script:ActivePresent={{(failedStore == "already-absent" ? "$false" : "$true")}}
            $script:PersistentPresent=$script:ActivePresent
            $script:FailStore='{{failedStore}}'; $script:FailuresEnabled=$true; $script:DeleteCalls=0
            $script:Cfg=New-Object PSObject -Property @{MACAddress='02:42:29:19:d6:14';Index=7;InterfaceIndex=19;IPConnectionMetric=5;DHCPEnabled=$false;IPAddress=@('10.96.1.20');IPSubnet=@('255.255.255.0');DefaultIPGateway=@();DNSServerSearchOrder=@()}
            $script:Adapter=New-Object PSObject -Property @{Index=7;NetConnectionID='ens3'}
            function Get-WmiObject {
              param($Class,$Filter)
              switch ($Class) {
                'Win32_NetworkAdapterConfiguration' { return $script:Cfg }
                'Win32_NetworkAdapter' { return $script:Adapter }
                'Win32_IP4RouteTable' {
                  if ($script:ActivePresent) { New-Object PSObject -Property @{InterfaceIndex=19;Destination='172.16.0.0';Mask='255.255.0.0';NextHop='10.96.1.1';Metric1=30} }
                  # This user route on the declared NIC must be preserved and allowed.
                  New-Object PSObject -Property @{InterfaceIndex=19;Destination='192.0.2.0';Mask='255.255.255.0';NextHop='10.96.1.254';Metric1=30}
                }
                default { throw 'Unexpected WMI class' }
              }
            }
            function Invoke-Netsh-Test {
              $parts=@($args); $store='active'; if ($parts -contains 'store=persistent') { $store='persistent' }
              if ($parts[2] -eq 'delete') {
                if ($parts -notcontains 'prefix=172.16.0.0/16' -or $parts -notcontains 'interface=19' -or $parts -notcontains 'nexthop=10.96.1.1') { throw 'An undeclared/user route was changed' }
                $script:DeleteCalls++
                if ($script:FailuresEnabled -and ($script:FailStore -eq 'both' -or $script:FailStore -eq $store)) { $global:LASTEXITCODE=1; return }
                if ($store -eq 'active') { $wasPresent=$script:ActivePresent; $script:ActivePresent=$false }
                else { $wasPresent=$script:PersistentPresent; $script:PersistentPresent=$false }
                $global:LASTEXITCODE=1; if ($wasPresent) { $global:LASTEXITCODE=0 }; return
              }
              if ($parts[2] -ne 'show') { throw 'Unexpected netsh mutation' }
              $global:LASTEXITCODE=0
              if (($store -eq 'active' -and $script:ActivePresent) -or ($store -eq 'persistent' -and $script:PersistentPresent)) { 'No Manual 25 172.16.0.0/16 19 10.96.1.1' }
              # Same prefix/next-hop on an undeclared NIC does not prove target-NIC drift.
              'No Manual 25 172.16.0.0/16 20 10.96.1.1'
              'No Manual 25 192.0.2.0/24 19 10.96.1.254'
            }
            $apply=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{apply}}'))
            $apply=$apply.Replace('$stateRoot = Join-Path $env:ProgramData ''GZCTF\TeamLab''', '$stateRoot = $stateDir').Replace('& "$env:SystemRoot\System32\netsh.exe"','Invoke-Netsh-Test')
            $read=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{read}}'))
            $read=$read.Replace('$stateRoot = Join-Path $env:ProgramData ''GZCTF\TeamLab''', '$stateRoot = $stateDir').Replace('& "$env:SystemRoot\System32\netsh.exe"','Invoke-Netsh-Test').Replace('[Console]::Out.Write($doc.OuterXml)','Write-Output $doc.OuterXml')
            try {
              $before=& ([scriptblock]::Create($read)); Write-Output ('BEFORE:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($before)))
              if ($script:FailStore -ne 'already-absent') {
                $failed=$false
                try { & ([scriptblock]::Create($apply)) } catch {
                  if ($_.Exception.Message -notmatch 'Owned static route still exists') { throw }
                  $failed=$true
                }
                if (!$failed -or [IO.File]::ReadAllText($statePath) -ne $previous) { throw 'Delete failure lost route ownership or reported success' }
                Write-Output 'STATE-RETAINED'
              }
              $script:FailuresEnabled=$false
              & ([scriptblock]::Create($apply))
              if ($script:ActivePresent -or $script:PersistentPresent) { throw 'Old route survived retry' }
              $calls=$script:DeleteCalls; & ([scriptblock]::Create($apply))
              if ($calls -ne $script:DeleteCalls) { throw 'Already removed route was deleted again' }
              $after=& ([scriptblock]::Create($read)); Write-Output ('AFTER:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($after)))
            } finally { Remove-Item -LiteralPath $stateDir -Recurse -Force }
            """;
        var result = await RunPowerShellFixtureAsync(fixture);
        Assert.True(result.ExitCode == 0, result.Error);
        static string Snapshot(string output, string marker) => Encoding.UTF8.GetString(Convert.FromBase64String(
            output.Split('\n').Single(line => line.StartsWith(marker, StringComparison.Ordinal))[marker.Length..].Trim()));
        var before = Snapshot(result.Output, "BEFORE:");
        var after = Snapshot(result.Output, "AFTER:");
        Assert.True(TeamLabVmNetworkService.Matches(desired, TeamLabVmNetworkService.ParseSnapshot(after), out var mismatch), mismatch);
        if (failedStore == "already-absent")
            Assert.True(TeamLabVmNetworkService.Matches(desired, TeamLabVmNetworkService.ParseSnapshot(before), out _));
        else
        {
            Assert.Contains("STATE-RETAINED", result.Output);
            Assert.False(TeamLabVmNetworkService.Matches(desired, TeamLabVmNetworkService.ParseSnapshot(before), out _));
            var guest = new FakeGuest(); guest.Reads.Enqueue(before);
            var verifyOnly = await Service(guest).ApplyAsync(plan, asset, true, CancellationToken.None);
            Assert.False(verifyOnly.Success);
            Assert.Equal("guest_network_drift", verifyOnly.ErrorCode);
        }
    }

    static async Task<(int ExitCode, string Output, string Error)> RunPowerShellFixtureAsync(string fixture)
    {
        var path = Path.Combine(Path.GetTempPath(), "gzctf-wmi-fixture-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(path, fixture, Encoding.UTF8);
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe", UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true
            } };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path })
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            return (process.ExitCode, await output, await error);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task WindowsNetworkTransport_ExecutesEightNicsWithMaximumRoutesThroughBoundedStdin()
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var networks = Enumerable.Range(0, 8).Select(i => new TeamLabNetworkIntentV2("net" + i,
            $"10.96.{i}.0/24", $"10.96.{i}.1", [new("port" + i, "linux-vm", $"02:42:29:19:d6:{20 + i:x2}", $"10.96.{i}.20")], [], [])).ToArray();
        var attachments = Enumerable.Range(0, 8).Select(i => new TeamLabAssetNetworkAttachmentV2("net" + i,
            "port" + i, "eth" + i, $"10.96.{i}.20", null, false, InterfaceKey: "nic" + i,
            MacAddress: $"02:42:29:19:d6:{20 + i:x2}", PrefixLength: 24, UseDefaultGateway: false,
            DnsServers: ["192.0.2.53", "192.0.2.54", "192.0.2.55"],
            StaticRoutes: Enumerable.Range(0, 8).Select(r => new TeamLabGuestRouteV2($"172.16.{r}.0/24", $"10.96.{i}.1", 25)).ToArray())).ToArray();
        var asset = plan.Assets[0] with { NetworkAttachments = attachments };
        plan = plan with { Assets = [asset], Networks = networks };
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, asset);
        foreach (var command in new[] { TeamLabVmNetworkService.BuildApplyCommand(asset, desired), TeamLabVmNetworkService.BuildReadCommand(asset, desired) })
        {
            Assert.True(command.Arguments.Sum(value => value.Length) + command.Path.Length < 2000);
            Assert.True(Encoding.UTF8.GetByteCount(command.StandardInput!) < VmGuestAgentService.MaxStandardInputBytes);
            Assert.Contains("input-data", VmGuestAgentService.BuildGuestExecArguments(command));
        }
        if (!OperatingSystem.IsWindows()) return;
        var setup = """
            $script:Configs=@(); $script:Adapters=@(); $script:Routes=@()
            for ($i=0;$i -lt 8;$i++) {
              $script:Configs+=New-Object PSObject -Property @{MACAddress=('02:42:29:19:d6:' + (20+$i).ToString('x2'));Index=(7+$i);InterfaceIndex=(19+$i);IPConnectionMetric=5;DHCPEnabled=$false;IPAddress=@('10.96.'+$i+'.20');IPSubnet=@('255.255.255.0');DefaultIPGateway=@();DNSServerSearchOrder=@('192.0.2.53','192.0.2.54','192.0.2.55')}
              $script:Adapters+=New-Object PSObject -Property @{Index=(7+$i);NetConnectionID=('ens'+(3+$i))}
              for ($r=0;$r -lt 8;$r++) { $script:Routes+=New-Object PSObject -Property @{InterfaceIndex=(19+$i);Destination=('172.16.'+$r+'.0');Mask='255.255.255.0';NextHop=('10.96.'+$i+'.1');Metric1=30} }
            }
            function Get-WmiObject {
              param($Class,$Filter)
              switch($Class) {
                'Win32_NetworkAdapterConfiguration' { return $script:Configs }
                'Win32_NetworkAdapter' { if ($Filter) { return @($script:Adapters | Where-Object { ('Index='+$_.Index) -eq $Filter }) }; return $script:Adapters }
                'Win32_IP4RouteTable' { return $script:Routes }
                default { throw 'Unexpected WMI class' }
              }
            }
            function Invoke-Netsh-Test { throw 'Correct maximum-size topology should need no netsh changes' }
            """;
        foreach (var command in new[] { TeamLabVmNetworkService.BuildApplyCommand(asset, desired), TeamLabVmNetworkService.BuildReadCommand(asset, desired) })
        {
            var stateDir = Path.Combine(Path.GetTempPath(), "gzctf-stdin-max-" + Guid.NewGuid().ToString("N"));
            try
            {
                var source = command.StandardInput!.Replace("$stateRoot = Join-Path $env:ProgramData 'GZCTF\\TeamLab'",
                    "$stateRoot = '" + stateDir.Replace("'", "''", StringComparison.Ordinal) + "'")
                    .Replace("& \"$env:SystemRoot\\System32\\netsh.exe\"", "Invoke-Netsh-Test");
                Assert.DoesNotContain("netsh.exe", source);
                var result = await RunPowerShellStdinAsync(command, setup + "\n" + source);
                Assert.True(result.ExitCode == 0, result.Error);
                if (command.StepId == "teamlab-network-read")
                {
                    var actual = TeamLabVmNetworkService.ParseSnapshot(result.Output);
                    Assert.Equal(8, actual.Count);
                    Assert.True(TeamLabVmNetworkService.Matches(desired, actual, out var mismatch), mismatch);
                }
            }
            finally { if (Directory.Exists(stateDir)) Directory.Delete(stateDir, true); }
        }
    }

    [Fact]
    public async Task WindowsStdinUnavailable_FailsClearlyWithoutFallback()
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var guest = new FakeGuest { ReadFailure = new(false, false, 78, "stdin-unavailable", null, "GZCTF_GUEST_STDIN_UNAVAILABLE") };
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("guest_qga_stdin_unavailable", result.ErrorCode);
        Assert.Single(guest.Commands);
        if (!OperatingSystem.IsWindows()) return;
        var command = TeamLabVmNetworkService.BuildReadCommand(plan.Assets[0], TeamLabVmNetworkService.ResolveInterfaces(plan, plan.Assets[0]));
        var launched = await RunPowerShellStdinAsync(command, "");
        Assert.Equal(78, launched.ExitCode);
        Assert.Contains("GZCTF_GUEST_STDIN_UNAVAILABLE", launched.Error);
    }

    [Fact]
    public async Task WindowsGuestExecEnvironment_MustInheritProgramDataAndSystemRoot()
    {
        if (!OperatingSystem.IsWindows()) return;
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var command = TeamLabVmNetworkService.BuildReadCommand(plan.Assets[0], TeamLabVmNetworkService.ResolveInterfaces(plan, plan.Assets[0]));
        const string probe = """
            $ErrorActionPreference='Stop'
            # This is the first filesystem-path operation in actual Windows readback.
            $null=Join-Path $env:ProgramData 'GZCTF\TeamLab'
            if ([string]::IsNullOrEmpty($env:SystemRoot)) { throw 'SystemRoot is absent' }
            [Console]::Out.Write('INHERITED-SYSTEM-PATHS-OK')
            """;
        var inherited = await RunPowerShellStdinAsync(command, probe);
        Assert.True(inherited.ExitCode == 0, inherited.Error);
        Assert.Equal("INHERITED-SYSTEM-PATHS-OK", inherited.Output);
        var empty = await RunPowerShellStdinAsync(command, probe, clearEnvironment: true);
        Assert.NotEqual(0, empty.ExitCode);
        // With all Windows environment removed, PowerShell may fail even before script startup.
        Assert.NotEmpty(empty.Error);
        Assert.DoesNotContain("INHERITED-SYSTEM-PATHS-OK", empty.Output);
    }

    static async Task<(int ExitCode, string Output, string Error)> RunPowerShellStdinAsync(VmGuestCommandRequest command, string input, bool clearEnvironment = false)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo
        {
            FileName = VmBootstrapService.WindowsPowerShellPath, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false)
        } };
        if (clearEnvironment) process.StartInfo.Environment.Clear();
        foreach (var argument in command.Arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
        process.StandardInput.Close();
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, await output, await error);
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
        Assert.Equal(TeamLabVmNetworkService.WindowsStdinLauncher,
            Encoding.Unicode.GetString(Convert.FromBase64String(command.Arguments.Last())));
        var script = command.StandardInput!;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowsManagedNetwork_SelectsOneHostAndPreservesScriptInput(bool helperPresent)
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var guest = new FakeGuest { HelperPresent = helperPresent };
        guest.Reads.Enqueue(Drift);
        guest.Reads.Enqueue(Matching);
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal([(plan.Assets[0].ResourceId, TeamLabVmNetworkService.WindowsLegacyPowerShellHostPath)], guest.FileProbes);
        Assert.Equal(new[] { "teamlab-network-read", "teamlab-network-apply", "teamlab-network-read" }, guest.Commands.Select(item => item.StepId));
        var desired = TeamLabVmNetworkService.ResolveInterfaces(plan, plan.Assets[0]);
        foreach (var command in guest.Commands)
        {
            Assert.Equal(helperPresent ? TeamLabVmNetworkService.WindowsLegacyPowerShellHostPath : VmBootstrapService.WindowsPowerShellPath, command.Path);
            Assert.Equal(command.StepId == "teamlab-network-apply" ? 120 : 30, command.TimeoutSeconds);
            Assert.Equal(command.StepId == "teamlab-network-apply" ? TeamLabVmNetworkService.BuildWindowsApplyScript(desired) :
                TeamLabVmNetworkService.BuildWindowsReadScript(desired), command.StandardInput);
            if (helperPresent)
            {
                Assert.Empty(command.Arguments);
                using var payload = System.Text.Json.JsonDocument.Parse(VmGuestAgentService.BuildCommandPayload(
                    "guest-exec", VmGuestAgentService.BuildGuestExecArguments(command)));
                Assert.Equal(@"C:\Program Files\YINYU-GuestTools\LegacyPowerShellHost.exe",
                    payload.RootElement.GetProperty("arguments").GetProperty("path").GetString());
                Assert.Equal(0, payload.RootElement.GetProperty("arguments").GetProperty("arg").GetArrayLength());
            }
            Assert.True(VmGuestAgentService.BuildGuestExecArguments(command).ContainsKey("input-data"));
            Assert.DoesNotContain(command.StandardInput!, command.ToString());
        }
    }

    [Theory]
    [InlineData(TeamLabGuestOperatingSystem.Linux, TeamLabGuestNetworkMode.ManagedStatic)]
    [InlineData(TeamLabGuestOperatingSystem.Windows, TeamLabGuestNetworkMode.Dhcp)]
    [InlineData(TeamLabGuestOperatingSystem.Windows, TeamLabGuestNetworkMode.Preconfigured)]
    public async Task OptionalHostProbe_IsLimitedToManagedWindows(TeamLabGuestOperatingSystem os, TeamLabGuestNetworkMode mode)
    {
        var plan = Plan(mode, os);
        var guest = new FakeGuest { HelperPresent = true };
        guest.Reads.Enqueue(Matching);
        Assert.True((await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None)).Success);
        Assert.Empty(guest.FileProbes);
    }

    [Theory]
    [InlineData(false, null, "guest_network_apply_failed")]
    [InlineData(true, null, "guest_network_apply_timeout")]
    [InlineData(false, "GZCTF_GUEST_STDIN_UNAVAILABLE", "guest_qga_stdin_unavailable")]
    public async Task OptionalHostApplyFailure_PreservesTypedFailureAndNeverRetriesTheWrite(
        bool timedOut, string? marker, string expectedCode)
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var guest = new FakeGuest { HelperPresent = true,
            ApplyResult = new(false, timedOut, 1, "failed", "private payload", marker ?? "private diagnostics") };
        guest.Reads.Enqueue(Drift);
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Equal(2, guest.Commands.Count);
        Assert.All(guest.Commands, command => Assert.Equal(TeamLabVmNetworkService.WindowsLegacyPowerShellHostPath, command.Path));
        Assert.DoesNotContain("private", result.Message);
    }

    [Fact]
    public async Task OptionalHostReadFailure_CannotReportSuccessOrFallbackToAnotherHost()
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var guest = new FakeGuest { HelperPresent = true, ReadFailure = new(false, false, 1, "failed", "private data", "private error") };
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.Equal("guest_network_control_failed", result.ErrorCode);
        Assert.Equal(TeamLabVmNetworkService.WindowsLegacyPowerShellHostPath, Assert.Single(guest.Commands).Path);
        Assert.DoesNotContain("private", result.Message);
    }

    [Fact]
    public async Task OptionalHostProbe_UnknownFailureIsNotTreatedAsMissing()
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var guest = new FakeGuest { ProbeFailure = new InvalidOperationException("private QGA permission denied") };
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None);
        Assert.Equal("guest_network_control_failed", result.ErrorCode);
        Assert.DoesNotContain("private", result.Message);
        Assert.Empty(guest.Commands);
    }

    [Fact]
    public async Task OptionalHostProbe_IdentityChangePreventsGuestExecution()
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        var guest = new FakeGuest { HelperPresent = true };
        var checks = 0;
        var result = await Service(guest).ApplyAsync(plan, plan.Assets[0], false, CancellationToken.None,
            verifyIdentity: _ => Task.FromResult(++checks < 3));
        Assert.Equal("guest_identity_conflict", result.ErrorCode);
        Assert.Single(guest.FileProbes);
        Assert.Empty(guest.Commands);
    }

    [Fact]
    public async Task OptionalHostProbe_CallerCancellationPropagates()
    {
        var plan = Plan(os: TeamLabGuestOperatingSystem.Windows);
        using var cancellation = new CancellationTokenSource();
        var guest = new FakeGuest { OnProbe = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(guest).ApplyAsync(plan, plan.Assets[0], false, cancellation.Token));
        Assert.Empty(guest.Commands);
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
        public bool HelperPresent { get; init; }
        public Exception? ProbeFailure { get; init; }
        public Action? OnProbe { get; init; }
        public List<(string VmName, string Path)> FileProbes { get; } = [];
        public VmGuestCommandResponse? ReadFailure { get; init; }
        public VmGuestCommandResponse ApplyResult { get; init; } = new(true, false, 0, "succeeded", null, null);
        public Task<bool> TryFileExistsAsync(string vmName, string guestPath, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            FileProbes.Add((vmName, guestPath));
            OnProbe?.Invoke();
            token.ThrowIfCancellationRequested();
            if (ProbeFailure is { } error) throw error;
            return Task.FromResult(HelperPresent);
        }
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
