using System;
using System.Collections.Generic;
using GZCTF.Agent.Services.TeamLab;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.TeamLab.Contracts.Execution;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabExecutionPlanV2Tests
{
    [Fact]
    public void Plan_RejectsAttachmentToPortOutsideItsNetwork()
    {
        var plan = Plan() with
        {
            Assets = [Asset("docker-1", "network-b", "port-a")]
        };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("invalid network", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsDuplicateAssetKeys()
    {
        var plan = Plan() with { Assets = [Asset("docker-1", "network-a", "port-a"), Asset("docker-1", "network-a", "port-a")] };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("unique", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsNonSha256PlanDigest()
    {
        var plan = Plan() with { PlanDigest = "sha256:not-a-digest" };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("plan digest", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsPortReferencingUnknownAsset()
    {
        var plan = Plan() with
        {
            Networks = [new TeamLabNetworkIntentV2(
                "network-a", "switch", "10.0.1.0/24", "10.0.1.1",
                [new TeamLabNetworkPortV2("port-a", "missing", "02:00:00:00:00:01", "10.0.1.10", true)], [], [])]
        };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("reference an asset", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsRouterAttachingTheSameNetworkTwice()
    {
        var plan = Plan() with
        {
            NetworkControl = new TeamLabNetworkControlIntentV2(
                "router-ns", 1,
                [new TeamLabRouterIntentV2("router-a", ["network-a", "network-a"])],
                null, [])
        };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("network", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsUnsupportedHealthCheck()
    {
        var plan = Plan() with
        {
            Assets = [Asset("docker-1", "network-a", "port-a") with
            {
                HealthChecks = [new TeamLabHealthCheckV2("icmp", "10.0.1.10", 80, null)]
            }]
        };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("invalid network", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Journal_ReturnsOnlyTheSameDigestAndClearsOnCleanup()
    {
        var plan = Plan();
        var journal = new TeamLabExecutionEventJournal();
        var response = new TeamLabExecutionPlanApplyResponse(true, false, plan.PlanDigest, [], []);

        journal.Save(plan, response);

        Assert.True(journal.TryGet(plan, out var repeated));
        Assert.True(repeated.AlreadyApplied);
        Assert.False(journal.TryGet(plan with { PlanDigest = "different" }, out _));
        journal.Remove(plan);
        Assert.False(journal.TryGet(plan, out _));
    }

    [Fact]
    public void Compiler_ProducesStableDigestAndCompleteNetworkControl()
    {
        var runtimePublicId = Guid.Parse("019fa217-fcee-73af-bb45-1bc400000001");
        var asset = new TeamLabNodeAssetCreateRequest(
            7, 11, runtimePublicId, 1, "docker-1", "Docker", TeamLabAssetKind.Docker,
            3, 1, 256, 256, null, true, new Dictionary<string, string>(),
            [new TeamLabNodeInterfaceIntent(
                "eth0", "network-a", "tl-network-a", "10.0.1.10", 24,
                "02:00:00:00:00:01", true, [], ["10.0.1.1"])],
            new TeamLabNodeHealthIntent(TeamLabHealthCheckKind.Tcp, 80),
            ImageReference: "registry.example/teamlab:latest");
        var infrastructure = new TeamLabNodeInfrastructureApplyRequest(
            7,
            1,
            1,
            "tlr-7-1",
            [new TeamLabNodeManagedSwitchIntent(
                new TeamLabNodeNetworkIntent("network-a", "Network A", "10.0.1.0/24", "10.0.1.1", "tl-network-a"),
                "dns-7-a",
                [new TeamLabNodeDnsRecord("Docker", "10.0.1.10", "02:00:00:00:00:01")])],
            [new TeamLabNodeManagedRouterFragmentIntent("router-a", ["network-a"])],
            new TeamLabNodeFabricIntent("100.64.0.2", "100.64.0.1/30", "100.64.0.2/30", "fabric-host", "fabric-ns", [], []),
            [new TeamLabNodeForwardPolicy("10.0.1.0/24", "10.0.2.0/24", false)],
            []);

        var first = TeamLabExecutionPlanCompiler.Compile(
            7, runtimePublicId, 1, "shard-a", infrastructure, [asset],
            new Dictionary<int, string> { [3] = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });
        var second = TeamLabExecutionPlanCompiler.Compile(
            7, runtimePublicId, 1, "shard-a", infrastructure, [asset],
            new Dictionary<int, string> { [3] = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });

        Assert.True(first.IsValid(out var error), error);
        Assert.Equal(first.PlanDigest, second.PlanDigest);
        Assert.Equal("router-a", first.NetworkControl!.Routers[0].Key);
        Assert.Equal("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", first.Assets[0].ImageDigest);
        Assert.Equal("registry.example/teamlab:latest", first.Assets[0].ImageReference);

        var changed = first with
        {
            Assets = [first.Assets[0] with { ImageReference = "registry.example/teamlab:changed" }]
        };
        Assert.False(changed.IsValid(out var changedError));
        Assert.Contains("digest", changedError, StringComparison.OrdinalIgnoreCase);
    }

    static TeamLabExecutionPlanV2 Plan() => new(
        7,
        Guid.Parse("019fa217-fcee-73af-bb45-1bc400000001"),
        1,
        "node-a",
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        [new TeamLabNetworkIntentV2(
            "network-a", "switch", "10.0.1.0/24", "10.0.1.1",
            [new TeamLabNetworkPortV2("port-a", "docker-1", "02:00:00:00:00:01", "10.0.1.10", true)], [], [])],
        [Asset("docker-1", "network-a", "port-a")],
        [], []);

    static TeamLabAssetExecutionSpecV2 Asset(string key, string network, string port) => new(
        key, "docker", key, "registry.example/teamlab@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null, 1, 256,
        [new TeamLabAssetNetworkAttachmentV2(network, port, "eth0", "10.0.1.10/24", true)], []);
}
