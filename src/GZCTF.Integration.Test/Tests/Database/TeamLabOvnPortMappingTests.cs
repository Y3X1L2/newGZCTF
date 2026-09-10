using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts.Execution;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class TeamLabOvnPortMappingTests
{
    [Fact]
    public async Task NativeOvnUsesSpecificProtocolPortsAndRemovesOnlySelectedMapping()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ovn");
        var version = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(directory, "Dockerfile"))))[..12];
        var image = new ImageFromDockerfileBuilder().WithName("gzctf-qa-ovn:" + version)
            .WithDockerfileDirectory(directory).WithImageBuildPolicy(PullPolicy.Missing).WithCleanUp(false).WithDeleteIfExists(false).Build();
        await image.CreateAsync(timeout.Token);
        await using var database = new ContainerBuilder(image)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("test", "-S", "/tmp/nb.sock")).Build();
        await database.StartAsync(timeout.Token);
        async Task<string> Run(string command)
        {
            var result = await database.ExecAsync(["sh", "-c", command], timeout.Token);
            Assert.True(result.ExitCode == 0, result.Stderr);
            return result.Stdout;
        }
        const string nb = "unix:/tmp/nb.sock";
        const string parameters = """{"mode":"dnat","externalPort":18080,"internalPort":80,"internalAddress":"10.80.0.2","protocol":"tcp"}""";
        var runtimeId = Guid.NewGuid();
        var digest = "sha256:" + new string('a', 64);
        await Run($"ovn-nbctl --db=unix:/tmp/nb.sock lr-add qa-router -- set Logical_Router qa-router external_ids:gzctf-runtime={runtimeId:D} external_ids:gzctf-generation=3 external_ids:gzctf-network-digest={digest} -- lr-add qa-peer-router -- lb-add qa-peer 10.80.0.1:19090 10.80.0.3:90 tcp -- lr-lb-add qa-peer-router qa-peer");
        var owner = new GZCTF.Agent.Models.TeamLabLinkPolicyApplyRequest(runtimeId, 3, "net", "web", "nat", parameters, NetworkDigest: digest);
        var apply = TeamLabLinkPolicyService.BuildNatCommands(nb, "qa-router", null, "10.80.0.0/24", "10.80.0.1", parameters, out var error, owner);
        Assert.Null(error);
        foreach (var command in apply) await Run(command);
        var state = await Run("ovn-nbctl --db=unix:/tmp/nb.sock lb-list");
        Assert.Contains("10.80.0.1:18080", state);
        Assert.Contains("10.80.0.2:80", state);
        Assert.Contains("10.80.0.1:19090", state);
        var nat = await Run("ovn-nbctl --db=unix:/tmp/nb.sock lr-nat-list qa-router");
        Assert.DoesNotContain("dnat_and_snat", nat);
        var remove = TeamLabLinkPolicyService.BuildNatRecoverCommands(nb, "qa-router", parameters, "10.80.0.1", out error);
        Assert.Null(error);
        foreach (var command in remove) await Run(command);
        state = await Run("ovn-nbctl --db=unix:/tmp/nb.sock lb-list");
        Assert.DoesNotContain("10.80.0.1:18080", state);
        Assert.Contains("10.80.0.1:19090", state);
        foreach (var command in apply) await Run(command);
        var plan = new TeamLabExecutionPlanV2(1, runtimeId, 3, "qa", digest, digest, true, [], [], []);
        var transaction = new JsonArray { "OVN_Northbound" };
        foreach (var operation in TeamLabOvnNetworkProvider.BuildRemoveOperations(plan)) transaction.Add(operation);
        var cleanup = await Run("ovsdb-client transact unix:/tmp/nb.sock " + TeamLabNetworkPrimitives.ShellQuote(transaction.ToJsonString()));
        Assert.DoesNotContain("\"error\"", cleanup);
        state = await Run("ovn-nbctl --db=unix:/tmp/nb.sock lb-list");
        Assert.DoesNotContain("10.80.0.1:18080", state);
        Assert.Contains("10.80.0.1:19090", state);
    }
}
