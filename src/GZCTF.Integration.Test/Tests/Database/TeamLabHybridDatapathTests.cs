using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using GZCTF.Agent.Services;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

// Provider-level traffic acceptance. External endpoint is simulated; this is not a main-site lifecycle sign-off.
public sealed class TeamLabHybridDatapathTests
{
    [Fact]
    public async Task RealOvnTrafficIsolationFailureRecoveryAndExactCleanup()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = timeout.Token;
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Hybrid");
        var inputs = string.Join("", Directory.GetFiles(directory).Order().Select(File.ReadAllText));
        var version = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(inputs)))[..12];
        var image = new ImageFromDockerfileBuilder().WithName("gzctf-qa-hybrid:" + version)
            .WithDockerfileDirectory(directory).WithImageBuildPolicy(PullPolicy.Missing)
            .WithCleanUp(false).WithDeleteIfExists(false).Build();
        await image.CreateAsync(token);
        await using var host = new ContainerBuilder(image).WithPrivileged(true)
            .WithPortBinding(6640, true).WithPortBinding(6641, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("test", "-S", "/var/run/ovn/ovnnb_db.sock"))
            .Build();
        await host.StartAsync(token);
        async Task<string> Run(string command)
        {
            var result = await host.ExecAsync(["sh", "-ec", command], token);
            Assert.True(result.ExitCode == 0, command + "\n" + result.Stderr + result.Stdout);
            return result.Stdout;
        }
        async Task Eventually(string command)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var result = await host.ExecAsync(["sh", "-ec", command], token);
                if (result.ExitCode == 0) return;
                await Task.Delay(1000, token);
            }
            await Run("(" + command + ") || { tail -30 /tmp/vm.log 2>/dev/null; ovs-vsctl show; exit 1; }");
        }
        using var rpc = new OvsdbJsonRpcClient();
        var options = Options.Create(new AgentTeamLabConfig
        {
            OvnNorthboundEndpoint = $"tcp:{host.Hostname}:{host.GetMappedPublicPort(6641)}",
            OvsLocalEndpoint = $"tcp:{host.Hostname}:{host.GetMappedPublicPort(6640)}",
            OvsIntegrationBridgeName = "br-int"
        });
        var ovn = new TeamLabOvnNetworkProvider(rpc, options, NullLogger<TeamLabOvnNetworkProvider>.Instance);
        var ovs = new TeamLabOvsAttachmentProvider(rpc, options);
        var managedNics = new TeamLabManagedNicProvider(new FixtureCommands(async (command, ct) =>
        {
            var result = await host.ExecAsync(["sh", "-ec", command], ct);
            return (result.ExitCode == 0, result.Stdout + result.Stderr);
        }), ovs);
        var plan = Plan();
        var connector = plan.Networks[0].Connectors!.Single();
        var applied = await ovn.ApplyAsync(plan, token);
        Assert.True(applied.Success, applied.Message);
        foreach (var name in new[] { "client", "external", "isolated" })
        {
            var suffix = name == "client" ? 2 : name == "external" ? 4 : 5;
            await Run($"ip netns add {name}; ip link add qa-{name} type veth peer name peer; ip link set peer netns {name}; ip link set qa-{name} up; ip -n {name} link set lo up; ip -n {name} link set peer address 02:00:00:83:00:0{suffix}; ip -n {name} link set peer up; ip -n {name} addr add 10.83.0.{suffix}/24 dev peer");
            // Userspace OVS receives veth packets without the kernel datapath's checksum completion.
            await Run($"ip netns exec {name} ethtool -K peer tx off; ethtool -K qa-{name} tx off");
            if (name == "external") await Run($"ip link set qa-external address {connector.MacAddress}");
            var result = name == "external"
                ? await managedNics.AttachAsync(plan, "mixed", connector, token)
                : await ovs.AttachAsync(plan, "qa-" + name, name == "isolated" ? "isolated" : "mixed", name, token);
            Assert.True(result.Success, result.Message);
        }
        await Run("mkdir -p /tmp/external; echo hybrid-external > /tmp/external/index.html; ip netns exec external busybox httpd -p 8080 -h /tmp/external; sh /opt/hybrid/prepare-guest.sh; ip tuntap add qa-vm mode tap; ip link set qa-vm up; qemu-system-x86_64 -m 256 -accel tcg -kernel /boot/vmlinuz-* -initrd /tmp/guest.cpio.gz -append 'console=ttyS0 rdinit=/init panic=-1' -netdev tap,id=n,ifname=qa-vm,script=no,downscript=no -device e1000,netdev=n,mac=02:00:00:83:00:03 -display none -serial file:/tmp/vm.log -pidfile /tmp/vm.pid -daemonize");
        var vmAttach = await ovs.AttachAsync(plan, "qa-vm", "mixed", "vm", token);
        Assert.True(vmAttach.Success, vmAttach.Message);
        await Eventually("ip netns exec client curl -fsS --max-time 2 http://10.83.0.3:8080 | grep -q hybrid-vm");
        await Run("ip netns exec client timeout 5 bash -c " + TeamLabNetworkPrimitives.ShellQuote(
            TeamLabExecutionPlanExecutor.BuildHealthProbeScript(new("http", "10.83.0.3", 8080, "/"))));
        var missingHealth = await host.ExecAsync(["ip", "netns", "exec", "client", "timeout", "5", "bash", "-c",
            TeamLabExecutionPlanExecutor.BuildHealthProbeScript(new("http", "10.83.0.3", 8080, "/missing"))], token);
        Assert.NotEqual(0, missingHealth.ExitCode);
        await Eventually("ip netns exec client curl -fsS --max-time 2 http://10.83.0.4:8080 | grep -q hybrid-external");
        await Run("! ip netns exec isolated ping -c 2 -W 1 10.83.0.3; ! ip netns exec client ping -c 2 -W 1 10.83.0.5");
        await Run("ip netns exec external timeout 10 tcpdump -U -ni peer -c 2 -w /tmp/mixed.pcap tcp port 8080 >/tmp/capture.log 2>&1 &");
        await Eventually("ip netns exec client curl -fsS --max-time 2 http://10.83.0.4:8080 >/dev/null; test -s /tmp/mixed.pcap");
        await Eventually("tcpdump -nr /tmp/mixed.pcap 2>/dev/null | grep -q '10.83.0.2'");
        Assert.True((await ovn.ProbeAsync(plan, token)).Success);
        await Eventually("test $(ovs-vsctl get Interface qa-client ofport) -gt 0");
        Assert.True((await ovs.ProbeAsync(plan, "qa-client", "mixed", "client", token)).Success);
        await Run("ovs-vsctl del-port br-int qa-external");
        Assert.False((await managedNics.ProbeAsync(plan, "mixed", connector, token)).Success);
        await Run("! ip netns exec client curl -fsS --max-time 2 http://10.83.0.4:8080");
        Assert.True((await managedNics.AttachAsync(plan, "mixed", connector, token)).Success);
        await Eventually("ip netns exec client curl -fsS --max-time 2 http://10.83.0.4:8080 | grep -q hybrid-external");
        await Run("kill -TERM $(cat /tmp/controller.pid)");
        await Eventually("test ! -e /tmp/controller.pid");
        await Run("ovn-controller --pidfile=/tmp/controller.pid --detach");
        await Eventually("ip netns exec client curl -fsS --max-time 2 http://10.83.0.3:8080 | grep -q hybrid-vm");
        // Independent runtime must survive deletion of the tested runtime.
        var peer = Plan() with { RuntimeId = 84, RuntimePublicId = Guid.NewGuid() };
        peer = Sign(peer);
        Assert.True((await ovn.ApplyAsync(peer, token)).Success);
        Assert.True((await managedNics.ProbeAsync(plan, "mixed", connector, token)).Success);
        Assert.False((await managedNics.AttachAsync(peer, "mixed", connector, token)).Success);
        Assert.True((await managedNics.DetachAsync(plan, "mixed", connector, token)).Success);
        await Run("ip link show qa-external");
        foreach (var name in new[] { "client", "isolated", "vm" })
            Assert.True((await ovs.RemoveAsync(plan, "qa-" + name, name == "isolated" ? "isolated" : "mixed", token)).Success);
        Assert.True((await ovn.RemoveAsync(plan, token)).Success);
        Assert.False((await ovn.ProbeAsync(plan, token)).Success);
        Assert.True((await ovn.ProbeAsync(peer, token)).Success);
        Assert.True((await ovn.RemoveAsync(peer, token)).Success);
        Assert.DoesNotContain("qa-client", await Run("ovs-vsctl list-ports br-int"));
        Assert.DoesNotContain("gzctf-runtime", await Run("ovn-nbctl list Logical_Switch"));
    }

    static TeamLabExecutionPlanV2 Plan() => Sign(new(83, Guid.NewGuid(), 1, "hybrid",
        "", "sha256:" + new string('a', 64), true,
        [new("mixed", "10.83.0.0/24", "10.83.0.1",
            [new("client", "client", "02:00:00:83:00:02", "10.83.0.2"),
             new("vm", "vm", "02:00:00:83:00:03", "10.83.0.3")], [], [],
             Connectors: [new(Guid.Parse("01900000-0000-7000-8000-000000000083"), Guid.Parse("01900000-0000-7000-8000-000000000084"), "qa-external", "02:00:00:83:00:44")]),
         new("isolated", "10.83.0.0/24", "10.83.0.1",
            [new("isolated", "isolated", "02:00:00:83:00:05", "10.83.0.5")], [], [])], [], []));

    static TeamLabExecutionPlanV2 Sign(TeamLabExecutionPlanV2 plan) => plan with
    {
        PlanDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(plan with { PlanDigest = "" })))
    };

    sealed class FixtureCommands(Func<string, CancellationToken, Task<(bool Success, string Output)>> run)
        : TeamLabCommandRunner(NullLogger<TeamLabCommandRunner>.Instance)
    {
        public override Task<(bool Success, string Output)> RunAsync(string command, CancellationToken token) => run(command, token);
    }
}
