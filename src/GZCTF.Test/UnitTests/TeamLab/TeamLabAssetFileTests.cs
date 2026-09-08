using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using GZCTF.Modules.Audit.Application;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabAssetFileTests
{
    [Theory]
    [InlineData("/tmp/file.txt", true)]
    [InlineData("/", true)]
    [InlineData("/tmp/../etc/passwd", false)]
    [InlineData("/tmp/./file", false)]
    [InlineData("relative", false)]
    [InlineData("/tmp/a\0b", false)]
    [InlineData("/tmp/a\\b", false)]
    public void PathsAreExplicitAndNormalized(string path, bool valid) => Assert.Equal(valid, TeamLabFileLimits.IsValidPath(path));

    [Theory]
    [InlineData("delete", false)]
    [InlineData("upload", true)]
    [InlineData("reset-ssh-identity", false)]
    public async Task DestructiveOperationsRequireConfirmation(string operation, bool overwrite)
    {
        await using var db = Context();
        var gateway = new Mock<ITeamLabAssetFileGateway>(MockBehavior.Strict);
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => Service(db, gateway.Object).ExecuteAsync(
            Guid.NewGuid(), 1, Guid.NewGuid(), true, new(3, operation, "/file", [], overwrite), default));
        Assert.Equal("files.confirmation_required", error.Code);
        gateway.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(TeamLabRuntimeStatus.Running)]
    [InlineData(TeamLabRuntimeStatus.Failed)]
    public async Task FilesUseCurrentAssetIdentityAndAuditOnlyMetadata(TeamLabRuntimeStatus status)
    {
        await using var db = Context();
        var asset = await Seed(db);
        asset.Runtime.Status = status;
        await db.SaveChangesAsync();
        var gateway = new Mock<ITeamLabAssetFileGateway>(MockBehavior.Strict);
        gateway.Setup(item => item.ExecuteAsync(asset.WorkerNodeId!.Value,
            It.Is<TeamLabContainerFileRequest>(request => request.RuntimeId == asset.RuntimeId && request.Generation == 3 && request.ContainerId == "fixture-container"),
            It.IsAny<CancellationToken>())).ReturnsAsync(new TeamLabFileResult(Content: [1, 2, 3]));
        var response = await Service(db, gateway.Object).ExecuteAsync(asset.Runtime.PublicId, asset.Id,
            asset.Runtime.CreatedById!.Value, false, new(3, "download", "/sensitive-name"), default);
        Assert.Equal(new byte[] { 1, 2, 3 }, response.Content);
        Assert.Single(await db.TeamLabEvents.ToArrayAsync());
        gateway.VerifyAll();
    }

    [Fact]
    public async Task StaleGenerationAndUnrelatedUserCannotContactAgent()
    {
        await using var db = Context();
        var asset = await Seed(db);
        var gateway = new Mock<ITeamLabAssetFileGateway>(MockBehavior.Strict);
        var service = Service(db, gateway.Object);
        var stale = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.ExecuteAsync(asset.Runtime.PublicId,
            asset.Id, asset.Runtime.CreatedById!.Value, false, new(2, "list", "/"), default));
        Assert.Equal("files.unavailable", stale.Code);
        var denied = await Assert.ThrowsAsync<TeamLabApiContractException>(() => service.ExecuteAsync(asset.Runtime.PublicId,
            asset.Id, Guid.NewGuid(), false, new(3, "list", "/"), default));
        Assert.Equal(403, denied.StatusCode);
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VmIdentityIsPinnedBeforeFileAccessAndCanBeExplicitlyRenewed()
    {
        await using var db = Context();
        var asset = await Seed(db);
        asset.Kind = TeamLabResourceKind.Vm;
        asset.NativeIdentity = Guid.NewGuid().ToString();
        asset.IpAddress = "10.80.0.10";
        asset.SourceTemplateId = 42;
        var protection = new EphemeralDataProtectionProvider();
        var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        db.ImageTemplateRemoteAccesses.Add(new ImageTemplateRemoteAccess
        {
            ImageTemplateId = 42, Enabled = true, Protocol = TeamLabRemoteProtocol.Ssh, Port = 22, Username = "qa",
            ProtectedSecret = protection.CreateProtector("GZCTF.TeamLab.ImageRemoteAccess.v1").Protect(credential)
        });
        await db.SaveChangesAsync();
        var firstKey = "SHA256:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=');
        var gateway = new Mock<ITeamLabAssetFileGateway>(MockBehavior.Strict);
        gateway.Setup(item => item.ExecuteVmAsync(asset.WorkerNodeId!.Value,
            It.Is<TeamLabVmFileRequest>(request => request.Operation == "probe" && request.HostKeySha256 == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabFileResult(HostKeySha256: firstKey));
        gateway.Setup(item => item.ExecuteVmAsync(asset.WorkerNodeId!.Value,
            It.Is<TeamLabVmFileRequest>(request => request.Operation == "list" && request.HostKeySha256 == firstKey), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabFileResult(Entries: [], HostKeySha256: firstKey));
        var service = Service(db, gateway.Object, protection);
        await service.ExecuteAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value, false, new(3, "list", "/"), default);
        Assert.Equal(firstKey, asset.SftpHostKeySha256);
        var replacementKey = "SHA256:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=');
        gateway.Setup(item => item.ExecuteVmAsync(asset.WorkerNodeId!.Value,
            It.Is<TeamLabVmFileRequest>(request => request.Operation == "probe" && request.HostKeySha256 == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamLabFileResult(HostKeySha256: replacementKey));
        await service.ExecuteAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value, false,
            new(3, "reset-ssh-identity", "/", Confirmed: true), default);
        Assert.Equal(replacementKey, asset.SftpHostKeySha256);
        gateway.Verify(item => item.ExecuteVmAsync(It.IsAny<Guid>(), It.Is<TeamLabVmFileRequest>(request => request.Operation == "list"), It.IsAny<CancellationToken>()), Times.Once);
    }

    static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    static TeamLabAssetFileService Service(AppDbContext db, ITeamLabAssetFileGateway gateway, IDataProtectionProvider? protection = null) =>
        new(db, new TeamLabAuthorizationService(db, [], []), gateway,
            new TeamLabEventRecorder(db, new EfOperationalEventWriter(db, NullLogger<EfOperationalEventWriter>.Instance), new OperationalCorrelation()),
            new ImageRemoteAccessService(db, protection ?? new EphemeralDataProtectionProvider()), new LocalDevelopmentLeaseProvider(),
            new TeamLabRuntimeOperationPayloadProtector(new EphemeralDataProtectionProvider()));
    static async Task<TeamLabRuntimeAsset> Seed(AppDbContext db)
    {
        var asset = new TeamLabRuntimeAsset { Generation = 3, Kind = TeamLabResourceKind.Docker,
            RuntimeResourceId = "fixture-container", WorkerNodeId = Guid.NewGuid(),
            Runtime = new TeamLabRuntime { Generation = 3, Status = TeamLabRuntimeStatus.Running, CreatedById = Guid.NewGuid() } };
        db.TeamLabRuntimeAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }
}
