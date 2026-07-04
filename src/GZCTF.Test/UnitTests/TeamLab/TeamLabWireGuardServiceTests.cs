using System;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.TeamLab;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabWireGuardServiceTests
{
    [Fact]
    public void GenerateKeyPair_ReturnsWireGuardBase64Keys()
    {
        var pair = TeamLabWireGuardService.GenerateKeyPair();

        Assert.Equal(32, Convert.FromBase64String(pair.PrivateKey).Length);
        Assert.Equal(32, Convert.FromBase64String(pair.PublicKey).Length);
        Assert.NotEqual(pair.PrivateKey, pair.PublicKey);
    }

    [Fact]
    public void BuildClientConfig_ContainsOnlyPlayerFacingVpnFields()
    {
        var config = TeamLabWireGuardService.BuildClientConfig(
            "client-private",
            "server-public",
            "10.180.1.2/32",
            "203.0.113.10:32001",
            "10.180.1.0/24,10.180.2.0/24",
            "10.180.1.1");

        Assert.Contains("[Interface]", config);
        Assert.Contains("PrivateKey = client-private", config);
        Assert.Contains("Address = 10.180.1.2/32", config);
        Assert.Contains("[Peer]", config);
        Assert.Contains("PublicKey = server-public", config);
        Assert.Contains("Endpoint = 203.0.113.10:32001", config);
        Assert.DoesNotContain("Phase", config);
        Assert.DoesNotContain("dry-run", config, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildClientConfig_OmitsDnsUntilRealDnsIsAvailable()
    {
        var config = TeamLabWireGuardService.BuildClientConfig(
            "client-private",
            "server-public",
            "10.180.1.2/32",
            "203.0.113.10:32001",
            "10.180.1.0/24",
            "");

        Assert.Contains("[Interface]", config);
        Assert.DoesNotContain("DNS =", config);
        Assert.Contains("AllowedIPs = 10.180.1.0/24", config);
    }

    [Fact]
    public void BuildClientConfigModel_ReturnsNullUntilRuntimeIsRunningAndOpen()
    {
        var service = CreateService();
        var runtime = CreateRuntime(TeamLabRuntimeStatus.Probing, isOpen: false);

        service.EnsurePeer(runtime, "10.180.0.2/32", "10.180.0.0/24", "10.180.0.1");

        Assert.Null(service.BuildClientConfigModel(runtime));
    }

    [Fact]
    public void BuildClientConfigModel_DoesNotRequireLegacyPenetrationRuntime()
    {
        var service = CreateService();
        var runtime = CreateRuntime(TeamLabRuntimeStatus.Running, isOpen: true);

        service.EnsurePeer(runtime, "10.180.0.2/32", "10.180.0.0/24", "10.180.0.1");

        var model = service.BuildClientConfigModel(runtime);

        Assert.NotNull(model);
        Assert.Equal("10.180.0.0/24", model.AllowedIPs);
    }

    [Fact]
    public void BuildClientConfigModel_ExportsPlayerFacingConfigForRunningRuntime()
    {
        var service = CreateService();
        var runtime = CreateRuntime(TeamLabRuntimeStatus.Running, isOpen: true);
        var material = service.EnsurePeer(runtime, "10.180.0.2/32", "10.180.0.0/24,10.181.0.0/24", "10.180.0.1");

        var model = service.BuildClientConfigModel(runtime);

        Assert.NotNull(model);
        Assert.Equal(10, model.GameId);
        Assert.Equal(20, model.TeamId);
        Assert.Equal("Blue", model.TeamName);
        Assert.Equal("203.0.113.10:32001", model.Endpoint);
        Assert.Equal("10.180.0.2/32", model.ClientAddress);
        Assert.Equal("10.180.0.0/24,10.181.0.0/24", model.AllowedIPs);
        Assert.Equal("10.180.0.1", model.Dns);
        Assert.Equal(material.Peer.ConfigVersion, model.ConfigVersion);
        Assert.Contains("PrivateKey = ", model.ConfigText);
        Assert.Contains("Endpoint = 203.0.113.10:32001", model.ConfigText);
        Assert.DoesNotContain("Phase", model.ConfigText);
        Assert.DoesNotContain("dry-run", model.ConfigText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvePublicEndpoint_FallsBackToContainerPublicEntry()
    {
        var endpoint = TeamLabWireGuardService.ResolvePublicEndpoint(
            new PublicUdpGatewayConfig { PublicEndpoint = "" },
            new ContainerProvider { PublicEntry = "203.195.157.191" });

        Assert.Equal("203.195.157.191", endpoint);
    }

    private static TeamLabWireGuardService CreateService() => new(
        new EphemeralDataProtectionProvider(),
        Options.Create(new PublicUdpGatewayConfig { PublicEndpoint = "203.0.113.10" }),
        Options.Create(new ContainerProvider { PublicEntry = "203.195.157.191" }));

    private static TeamLabRuntime CreateRuntime(TeamLabRuntimeStatus status, bool isOpen) => new()
    {
        Id = 3,
        GameId = 10,
        TeamId = 20,
        Team = new Team { Id = 20, Name = "Blue" },
        Status = status,
        IsOpenToPlayers = isOpen,
        PublicUdpMapping = new TeamLabPublicUdpMapping
        {
            PublicUdpPort = 32001,
            WorkerWireGuardPort = 42001,
            WorkerTunnelIp = "10.250.0.10"
        }
    };
}
