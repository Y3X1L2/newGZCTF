using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
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
                "network-a", "10.0.1.0/24", "10.0.1.1",
                [new TeamLabNetworkPortV2("port-a", "missing", "02:00:00:00:00:01", "10.0.1.10")], [], [])]
        };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("invalid network", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsAttachmentToAnotherAssetsPort()
    {
        var plan = Plan() with
        {
            Networks = [new TeamLabNetworkIntentV2(
                "network-a", "10.0.1.0/24", "10.0.1.1",
                [
                    new TeamLabNetworkPortV2("port-a", "docker-1", "02:00:00:00:00:01", "10.0.1.10"),
                    new TeamLabNetworkPortV2("port-b", "docker-2", "02:00:00:00:00:02", "10.0.1.11")
                ], [], [])],
            Assets =
            [
                Asset("docker-1", "network-a", "port-b"),
                Asset("docker-2", "network-a", "port-b") with
                {
                    NetworkAttachments = [new TeamLabAssetNetworkAttachmentV2(
                        "network-a", "port-b", "eth0", "10.0.1.11/24")]
                }
            ]
        };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("invalid network", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsDuplicateStaticRoutes()
    {
        var route = new TeamLabNetworkRouteV2("10.0.2.0/24", "10.0.1.1");
        var plan = Plan() with
        {
            Networks = [new TeamLabNetworkIntentV2(
                "network-a", "10.0.1.0/24", "10.0.1.1",
                [new TeamLabNetworkPortV2("port-a", "docker-1", "02:00:00:00:00:01", "10.0.1.10")],
                [route, route], [])]
        };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("invalid network", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_AcceptsRouteWithEmptyNextHop()
    {
        var plan = WithDigests(Plan() with
        {
            Networks = [new TeamLabNetworkIntentV2(
                "network-a", "10.0.1.0/24", "10.0.1.1",
                [new TeamLabNetworkPortV2("port-a", "docker-1", "02:00:00:00:00:01", "10.0.1.10")],
                [new TeamLabNetworkRouteV2("10.0.2.0/24", null)], [])]
        });

        Assert.True(plan.IsValid(out var error), error);
    }

    [Fact]
    public void Plan_RejectsDuplicateDnsHostnameIgnoringCase()
    {
        var plan = Plan();
        plan = plan with
        {
            Networks = [plan.Networks[0] with
            {
                DnsRecords =
                [
                    new TeamLabDnsRecordV2("portal", "10.0.1.10"),
                    new TeamLabDnsRecordV2("PORTAL", "10.0.1.11")
                ]
            }]
        };

        Assert.False(plan.IsValid(out var error));
        Assert.Contains("DNS hostname", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_RejectsRouterAttachingTheSameNetworkTwice()
    {
        var plan = Plan() with
        {
            NetworkControl = new TeamLabNetworkControlIntentV2(
                [new TeamLabRouterIntentV2("router-a", ["network-a", "network-a"])],
                [])
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
            7, runtimePublicId, 1, "shard-a", true, infrastructure, [asset], [asset], [],
            new Dictionary<int, string> { [3] = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });
        var second = TeamLabExecutionPlanCompiler.Compile(
            7, runtimePublicId, 1, "shard-a", true, infrastructure, [asset], [asset], [],
            new Dictionary<int, string> { [3] = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });

        Assert.True(first.IsValid(out var error), error);
        Assert.Equal(first.PlanDigest, second.PlanDigest);
        Assert.Equal("router-a", first.NetworkControl!.Routers[0].Key);
        Assert.Equal("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", first.Assets[0].ImageDigest);
        Assert.Equal("registry.example/teamlab:latest", first.Assets[0].ImageReference);
        Assert.Equal(3, first.Assets[0].TemplateId);

        var changed = first with
        {
            Assets = [first.Assets[0] with { ImageReference = "registry.example/teamlab:changed" }]
        };
        Assert.False(changed.IsValid(out var changedError));
        Assert.Contains("digest", changedError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compiler_ProducesLegalDeterministicInterfaceNames()
    {
        var runtimePublicId = Guid.Parse("019fa217-fcee-73af-bb45-1bc400000002");
        var asset = new TeamLabNodeAssetCreateRequest(
            7, 11, runtimePublicId, 1, "docker-1", "Docker", TeamLabAssetKind.Docker,
            3, 1, 256, 256, null, true, new Dictionary<string, string>(),
            [
                new TeamLabNodeInterfaceIntent(
                    "docker-switch-nic", "network-a", "tl-network-a", "10.0.1.10", 24,
                    "02:00:00:00:00:01", true, [], ["10.0.1.1"]),
                new TeamLabNodeInterfaceIntent(
                    "uplink-extra", "network-a", "tl-network-a", "10.0.1.11", 24,
                    "02:00:00:00:00:02", false, [], ["10.0.1.1"])
            ],
            null,
            ImageReference: "registry.example/teamlab:latest");
        var infrastructure = new TeamLabNodeInfrastructureApplyRequest(
            7,
            1,
            1,
            "tlr-7-1",
            [new TeamLabNodeManagedSwitchIntent(
                new TeamLabNodeNetworkIntent("network-a", "Network A", "10.0.1.0/24", "10.0.1.1", "tl-network-a"),
                "dns-7-a",
                [
                    new TeamLabNodeDnsRecord("Docker", "10.0.1.10", "02:00:00:00:00:01"),
                    new TeamLabNodeDnsRecord("Docker2", "10.0.1.11", "02:00:00:00:00:02")
                ])],
            [new TeamLabNodeManagedRouterFragmentIntent("router-a", ["network-a"])],
            new TeamLabNodeFabricIntent("100.64.0.2", "100.64.0.1/30", "100.64.0.2/30", "fabric-host", "fabric-ns", [], []),
            [new TeamLabNodeForwardPolicy("10.0.1.0/24", "10.0.2.0/24", false)],
            []);

        var plan = TeamLabExecutionPlanCompiler.Compile(
            7, runtimePublicId, 1, "shard-a", true, infrastructure, [asset], [asset], [],
            new Dictionary<int, string> { [3] = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });

        var attachments = plan.Assets[0].NetworkAttachments;
        Assert.Equal(2, attachments.Count);
        Assert.Equal("docker-switch-nic", attachments[0].PortKey);
        Assert.Equal("eth0", attachments[0].InterfaceName);
        Assert.Equal("uplink-extra", attachments[1].PortKey);
        Assert.Equal("eth1", attachments[1].InterfaceName);
        Assert.All(attachments, attachment =>
        {
            Assert.True(attachment.InterfaceName.Length <= 15);
            Assert.Matches("^[a-zA-Z0-9_.-]+$", attachment.InterfaceName);
        });
    }
    [Fact]
    public void Compiler_AssignsPlayerGatewayToTheEntryNetworkLastHost()
    {
        var runtimePublicId = Guid.Parse("019fa217-fcee-73af-bb45-1bc400000003");
        var asset = new TeamLabNodeAssetCreateRequest(
            7, 11, runtimePublicId, 1, "docker-1", "Docker", TeamLabAssetKind.Docker,
            3, 1, 256, 256, null, true, new Dictionary<string, string>(),
            [new TeamLabNodeInterfaceIntent(
                "eth0", "network-a", "tl-network-a", "10.0.1.10", 24,
                "02:00:00:00:00:01", true, [], ["10.0.1.1"])],
            null,
            ImageReference: "registry.example/teamlab:latest");
        var infrastructure = new TeamLabNodeInfrastructureApplyRequest(
            7, 1, 1, "tlr-7-1",
            [new TeamLabNodeManagedSwitchIntent(
                new TeamLabNodeNetworkIntent(
                    "network-a", "Network A", "10.0.1.0/24", "10.0.1.1", "tl-network-a",
                    IsEntry: true),
                "dns-7-a",
                [new TeamLabNodeDnsRecord("Docker", "10.0.1.10", "02:00:00:00:00:01")])],
            [new TeamLabNodeManagedRouterFragmentIntent("router-a", ["network-a"])],
            new TeamLabNodeFabricIntent("100.64.0.2", "100.64.0.1/30", "100.64.0.2/30", "fabric-host", "fabric-ns", [], []),
            [new TeamLabNodeForwardPolicy("10.0.1.0/24", "10.0.2.0/24", false)],
            []);

        var plan = TeamLabExecutionPlanCompiler.Compile(
            7, runtimePublicId, 1, "shard-a", true, infrastructure, [asset], [asset], [],
            new Dictionary<int, string> { [3] = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });

        var gateway = Assert.Single(plan.Networks).PlayerGateway;
        Assert.NotNull(gateway);
        Assert.Equal("player-gateway", gateway.PortKey);
        Assert.Equal("10.0.1.254", gateway.IpAddress);
        Assert.Equal("tlwg7", gateway.InterfaceName);
    }

    static TeamLabExecutionPlanV2 WithDigests(TeamLabExecutionPlanV2 plan)
    {
        var networkDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { plan.Networks, plan.NetworkControl }))).ToLowerInvariant()}";
        plan = plan with { NetworkDigest = networkDigest };
        return plan with
        {
            PlanDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                plan with { PlanDigest = string.Empty }))).ToLowerInvariant()}"
        };
    }

    static TeamLabExecutionPlanV2 Plan()
    {
        var plan = new TeamLabExecutionPlanV2(
            7,
            Guid.Parse("019fa217-fcee-73af-bb45-1bc400000001"),
            1,
            "node-a",
            string.Empty,
            string.Empty,
            false,
            [new TeamLabNetworkIntentV2(
                "network-a", "10.0.1.0/24", "10.0.1.1",
                [new TeamLabNetworkPortV2("port-a", "docker-1", "02:00:00:00:00:01", "10.0.1.10")], [], [])],
            [Asset("docker-1", "network-a", "port-a")],
            []);
        var networkDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { plan.Networks, plan.NetworkControl }))).ToLowerInvariant()}";
        plan = plan with { NetworkDigest = networkDigest };
        return plan with
        {
            PlanDigest = $"sha256:{Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
                plan with { PlanDigest = string.Empty }))).ToLowerInvariant()}"
        };
    }

    static TeamLabAssetExecutionSpecV2 Asset(string key, string network, string port) => new(
        key, "docker", key, "registry.example/teamlab@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null, 3, 1, 256,
        [new TeamLabAssetNetworkAttachmentV2(network, port, "eth0", "10.0.1.10/24")], []);
}
