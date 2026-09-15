using System;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
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
    public async Task StreamingTransferSupportsFilesAboveLegacyJsonLimit()
    {
        await using var db = Context();
        var asset = await Seed(db);
        var payload = new byte[TeamLabFileLimits.MaxBytes + 1024];
        RandomNumberGenerator.Fill(payload);
        var gateway = new Mock<ITeamLabAssetFileGateway>(MockBehavior.Strict);
        gateway.Setup(item => item.UploadAsync(asset.WorkerNodeId!.Value,
                It.Is<TeamLabContainerFileRequest>(request => request.Operation == "upload" && request.Path == "/large.bin"),
                It.IsAny<Stream>(), payload.LongLength, It.IsAny<CancellationToken>()))
            .Returns<Guid, TeamLabContainerFileRequest, Stream, long, CancellationToken>(
                async (_, _, source, _, token) =>
                {
                    using var copy = new MemoryStream();
                    await source.CopyToAsync(copy, token);
                    Assert.Equal(payload, copy.ToArray());
                });
        gateway.Setup(item => item.DownloadAsync(asset.WorkerNodeId!.Value,
                It.Is<TeamLabContainerFileRequest>(request => request.Operation == "download" && request.Path == "/large.bin"),
                It.IsAny<Stream>(), TeamLabFileLimits.DefaultMaxTransferBytes, It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, TeamLabContainerFileRequest, Stream, long, TimeSpan, CancellationToken>(
                async (_, _, destination, _, _, token) => await destination.WriteAsync(payload, token));
        var service = Service(db, gateway.Object);
        await using (var source = new MemoryStream(payload, writable: false))
            await service.UploadAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value,
                false, 3, "/large.bin", source, payload.LongLength, false, false, default);
        await using var destination = new MemoryStream();
        await service.DownloadAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value,
            false, 3, "/large.bin", destination, default);
        Assert.Equal(payload, destination.ToArray());
        gateway.VerifyAll();
    }

    [Theory]
    [InlineData("mkdir", "/data/archive", null, false, false)]
    [InlineData("move", "/data/a.txt", "/data/archive/a.txt", false, false)]
    [InlineData("delete", "/data/archive", null, true, true)]
    public async Task DirectoryOperationsReachAgentWithStructuredFields(
        string operation, string path, string? destinationPath, bool recursive, bool confirmed)
    {
        await using var db = Context();
        var asset = await Seed(db);
        var gateway = new Mock<ITeamLabAssetFileGateway>(MockBehavior.Strict);
        gateway.Setup(item => item.ExecuteAsync(asset.WorkerNodeId!.Value,
            It.Is<TeamLabContainerFileRequest>(request => request.Operation == operation && request.Path == path &&
                request.DestinationPath == destinationPath && request.Recursive == recursive),
            It.IsAny<CancellationToken>())).ReturnsAsync(new TeamLabFileResult());

        await Service(db, gateway.Object).ExecuteAsync(asset.Runtime.PublicId, asset.Id,
            asset.Runtime.CreatedById!.Value, false,
            new TeamLabAssetFileCommand(3, operation, path, Confirmed: confirmed,
                DestinationPath: destinationPath, Recursive: recursive), default);

        gateway.VerifyAll();
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
        new(db, new TeamLabAuthorizationService(db, [], []), new TeamLabScopeAuthorizationService(db), gateway,
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
