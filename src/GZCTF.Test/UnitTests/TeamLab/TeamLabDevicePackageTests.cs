using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.EntityFrameworkCore;
using GZCTF.Modules.TeamLab.Contracts;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabDevicePackageTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"device-packages-{Guid.NewGuid():N}")
            .Options);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static readonly string DefaultDigest = "sha256:" + new string('a', 64);

    private static RegisterTeamLabDevicePackageModel Command(string version = "1.0.0", string? digest = null) => new(
        "plc-simulator",
        "PLC 模拟器",
        version,
        "oci-image",
        "registry.example.com/yinyu/plc-simulator:1.0.0",
        digest ?? DefaultDigest,
        null,
        ["docker"],
        500, 256, 4,
        [new TeamLabDevicePackagePortModel("modbus", 502, "tcp")],
        Json("""{"type":"object","properties":{"slaveId":{"type":"integer"}}}"""),
        Json("""{"kind":"tcp","port":502,"intervalSeconds":30}"""),
        ["modbus-read", "modbus-write"]);

    [Fact]
    public async Task Register_PersistsValidPackageWithCanonicalMetadata()
    {
        using var context = CreateContext();
        var service = new TeamLabDevicePackageService(context);

        var model = await service.RegisterAsync(Command(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, model.Id);
        Assert.Equal("plc-simulator", model.Name);
        Assert.Equal("1.0.0", model.Version);
        Assert.Equal("oci-image", model.ArtifactKind);
        Assert.Equal(DefaultDigest, model.Digest);
        Assert.Equal(["modbus-read", "modbus-write"], model.ProtocolEventTypes);
        Assert.Equal("tcp", model.Ports[0].Protocol);
        var stored = await service.GetAsync(model.Id, CancellationToken.None);
        Assert.Equal(model.Id, stored.Id);
    }

    [Fact]
    public async Task Register_SameVersionConflicts_DifferentVersionSucceeds()
    {
        using var context = CreateContext();
        var service = new TeamLabDevicePackageService(context);
        await service.RegisterAsync(Command(), CancellationToken.None);

        var conflict = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.RegisterAsync(Command(), CancellationToken.None));
        Assert.Equal("device_package_version_conflict", conflict.Code);
        Assert.Equal(409, conflict.StatusCode);

        var next = await service.RegisterAsync(Command(version: "1.1.0"), CancellationToken.None);
        Assert.Equal("1.1.0", next.Version);
    }

    [Theory]
    [InlineData("sha256:short")]
    [InlineData("md5:0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Register_RejectsInvalidDigest(string digest)
    {
        using var context = CreateContext();
        var service = new TeamLabDevicePackageService(context);
        var exception = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.RegisterAsync(Command(digest: digest), CancellationToken.None));
        Assert.Equal("artifact_digest_invalid", exception.Code);
    }

    [Fact]
    public async Task Register_RejectsTcpHealthWithoutPort()
    {
        using var context = CreateContext();
        var service = new TeamLabDevicePackageService(context);
        var command = Command() with { HealthDeclaration = Json("""{"kind":"tcp"}""") };
        var exception = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.RegisterAsync(command, CancellationToken.None));
        Assert.Equal("device_package_health_invalid", exception.Code);
    }

    [Fact]
    public async Task List_FiltersByName_AndPagesWithCursor()
    {
        using var context = CreateContext();
        var service = new TeamLabDevicePackageService(context);
        await service.RegisterAsync(Command(version: "1.0.0"), CancellationToken.None);
        await service.RegisterAsync(Command(version: "2.0.0"), CancellationToken.None);

        var page = await service.ListAsync("plc-simulator", null, 1, CancellationToken.None);
        Assert.Single(page.Items);
        Assert.NotNull(page.Next);

        var next = await service.ListAsync("plc-simulator", page.Next, 50, CancellationToken.None);
        Assert.Single(next.Items);
        Assert.Null(next.Next);
        Assert.NotEqual(page.Items[0].Version, next.Items[0].Version);

        var missing = await service.ListAsync("unknown", null, 50, CancellationToken.None);
        Assert.Empty(missing.Items);
    }

    [Fact]
    public async Task DisableAndArchive_HidePackageFromExternalSurface()
    {
        using var context = CreateContext();
        var service = new TeamLabDevicePackageService(context);
        var model = await service.RegisterAsync(Command(), CancellationToken.None);

        var disabled = await service.SetEnabledAsync(model.Id, false, CancellationToken.None);
        Assert.False(disabled.Enabled);

        await service.ArchiveAsync(model.Id, CancellationToken.None);
        var archived = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.GetAsync(model.Id, CancellationToken.None));
        Assert.Equal("device_package_not_found", archived.Code);
    }
}
