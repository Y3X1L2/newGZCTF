using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.TeamLab.Contracts.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

        // Revision transactions must preserve metadata while refreshing both objects used by NAT lookup.
        using var rpc = new OvsdbJsonRpcClient();
        var provider = new TeamLabOvnNetworkProvider(rpc, Options.Create(new GZCTF.Agent.Models.AgentTeamLabConfig()),
            NullLogger<TeamLabOvnNetworkProvider>.Instance);
        var current = new TeamLabExecutionPlanV2(2, Guid.NewGuid(), 1, "revision", digest, digest, true,
            [new("revision-net", "10.81.0.0/24", "10.81.0.1", [], [], [])], [], [],
            new TeamLabNetworkControlIntentV2([new("revision-router", ["revision-net"])], []));
        async Task Transact(IReadOnlyList<JsonObject> operations)
        {
            var request = new JsonArray { "OVN_Northbound" };
            foreach (var operation in operations) request.Add(operation.DeepClone());
            var result = await Run("ovsdb-client transact unix:/tmp/nb.sock " + TeamLabNetworkPrimitives.ShellQuote(request.ToJsonString()));
            Assert.DoesNotContain("\"error\"", result);
        }
        await Transact(provider.BuildApplyOperations(current));
        var insertions = provider.BuildApplyOperations(current);
        var routerName = insertions.Single(operation => operation["table"]!.GetValue<string>() == "Logical_Router")["row"]!["name"]!.GetValue<string>();
        var portName = insertions.Single(operation => operation["table"]!.GetValue<string>() == "Logical_Router_Port")["row"]!["name"]!.GetValue<string>();
        await Run($"ovn-nbctl --db={nb} set Logical_Router {routerName} external_ids:qa-preserved=yes -- set Logical_Router_Port {portName} external_ids:qa-preserved=yes");
        var desired = current with { NetworkDigest = "sha256:" + new string('b', 64) };
        await Transact(provider.BuildRevisionOperations(current, desired, [new(), new(), new()]));
        foreach (var (table, name) in new[] { ("Logical_Router", routerName), ("Logical_Router_Port", portName) })
        {
            var match = await Run($"ovn-nbctl --db={nb} --data=bare --no-heading --columns=name find {table} external_ids:gzctf-runtime={current.RuntimePublicId:D} external_ids:gzctf-generation=1 {TeamLabLinkPolicyService.NetworkDigestCondition(desired.NetworkDigest)}");
            Assert.Equal(name, match.Trim());
            Assert.Contains("yes", await Run($"ovn-nbctl --db={nb} get {table} {name} external_ids:qa-preserved"));
        }
        var digestFilter = TeamLabLinkPolicyService.NetworkDigestCondition(desired.NetworkDigest);
        var portId = (await Run($"ovn-nbctl --db={nb} --data=bare --no-heading --columns=_uuid find Logical_Router_Port external_ids:gzctf-runtime={current.RuntimePublicId:D} external_ids:gzctf-generation=1 'networks{{>=}}10.81.0.1/24' {digestFilter}")).Trim();
        Assert.True(Guid.TryParse(portId, out _));
        var natRouter = await Run(TeamLabLinkPolicyService.BuildRouterLookupCommand(nb, current.RuntimePublicId, 1) +
            " " + TeamLabNetworkPrimitives.ShellQuote($"ports{{>=}}{portId}") + " " + digestFilter);
        Assert.Equal(routerName, natRouter.Trim());
        Assert.Contains(digest, await Run($"ovn-nbctl --db={nb} get Logical_Router qa-router external_ids:gzctf-network-digest"));
        await Transact(TeamLabOvnNetworkProvider.BuildRemoveOperations(current));
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
