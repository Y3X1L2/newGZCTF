using System;
using System.Text;
using GZCTF.Models;
using GZCTF.Modules.Penetration.Application;
using GZCTF.Modules.Penetration.Domain;
using GZCTF.Services.Config;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class PenetrationObjectiveSecurityTests
{
    [Fact]
    public void DynamicFlag_IsKeyedAndDomainSeparated()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var firstConfig = Config("0123456789abcdef-first-key");
        var secondConfig = Config("0123456789abcdef-second-key");
        var objective = new PenetrationObjective
        {
            Key = "initial-access",
            TopologyAssetKey = "web",
            IsDynamic = true
        };
        var first = Service(context, firstConfig).BuildFlag(objective, 8, 12, 3);
        var same = Service(context, firstConfig).BuildFlag(objective, 8, 12, 3);
        var second = Service(context, secondConfig).BuildFlag(objective, 8, 12, 3);

        Assert.Equal(first, same);
        Assert.NotEqual(first, second);
        Assert.StartsWith("flag{", first);
        Assert.DoesNotContain(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes("8:12:web:initial-access:3"))).ToLowerInvariant()[..16], first);
    }

    [Fact]
    public void DynamicFlag_RejectsMissingServerKey()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var objective = new PenetrationObjective
        {
            Key = "initial-access",
            TopologyAssetKey = "web",
            IsDynamic = true
        };

        Assert.Throws<InvalidOperationException>(() =>
            Service(context, Config(string.Empty)).BuildFlag(objective, 8, 12, 3));
    }

    private static IConfigService Config(string key)
    {
        var config = new Mock<IConfigService>();
        config.Setup(item => item.GetXorKey()).Returns(Encoding.UTF8.GetBytes(key));
        return config.Object;
    }

    private static PenetrationObjectiveService Service(AppDbContext context, IConfigService config) =>
        new(context, null!, null!, null!, null!, null!, config);
}
