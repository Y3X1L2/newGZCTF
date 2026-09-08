using System;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Audit.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabAssetDiagnosticsTests
{
    [Fact]
    public async Task VmRead_UsesBoundUuidAndRecordsActualState()
    {
        await using var db = Context();
        var asset = await Seed(db);
        asset.Kind = TeamLabResourceKind.Vm;
        var nativeId = Guid.NewGuid();
        asset.NativeIdentity = nativeId.ToString();
        await db.SaveChangesAsync();
        var gateway = new Mock<ITeamLabAssetDiagnosticsGateway>(MockBehavior.Strict);
        gateway.Setup(item => item.ReadVmAsync(asset.WorkerNodeId!.Value,
            It.Is<TeamLabVmDiagnosticsRequest>(request => request.NativeId == nativeId && request.Generation == 3 && request.DomainName == asset.RuntimeResourceId),
            It.IsAny<CancellationToken>())).ReturnsAsync(new TeamLabVmDiagnostics("paused", nativeId, DateTimeOffset.UtcNow));
        var result = await Service(db, gateway.Object).ReadVmAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value, false, default);
        Assert.Equal("paused", result.State);
        Assert.Single(await db.TeamLabEvents.ToArrayAsync());
        gateway.VerifyAll();
    }

    [Fact]
    public async Task VmRead_RejectsMissingIdentityWithoutCallingAgent()
    {
        await using var db = Context();
        var asset = await Seed(db);
        asset.Kind = TeamLabResourceKind.Vm;
        await db.SaveChangesAsync();
        var gateway = new Mock<ITeamLabAssetDiagnosticsGateway>(MockBehavior.Strict);
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => Service(db, gateway.Object)
            .ReadVmAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value, false, default));
        Assert.Equal("diagnostics.unavailable", error.Code);
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VmRead_RejectsUuidChangedDuringSampling()
    {
        await using var db = Context();
        var asset = await Seed(db);
        asset.Kind = TeamLabResourceKind.Vm;
        var nativeId = Guid.NewGuid();
        asset.NativeIdentity = nativeId.ToString();
        await db.SaveChangesAsync();
        var gateway = new Mock<ITeamLabAssetDiagnosticsGateway>();
        gateway.Setup(item => item.ReadVmAsync(It.IsAny<Guid>(), It.IsAny<TeamLabVmDiagnosticsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { asset.NativeIdentity = Guid.NewGuid().ToString(); await db.SaveChangesAsync();
                return new TeamLabVmDiagnostics("running", nativeId, DateTimeOffset.UtcNow); });
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => Service(db, gateway.Object)
            .ReadVmAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value, false, default));
        Assert.Equal("diagnostics.stale_generation", error.Code);
        Assert.Empty(await db.TeamLabEvents.ToArrayAsync());
    }

    [Fact]
    public async Task Read_UsesBoundNodeAndGeneration()
    {
        await using var db = Context();
        var asset = await Seed(db);
        var gateway = new Mock<ITeamLabAssetDiagnosticsGateway>();
        gateway.Setup(item => item.ReadAsync(asset.WorkerNodeId!.Value,
            It.Is<TeamLabContainerDiagnosticsRequest>(r => r.ContainerId == "fixture-container" &&
                r.Generation == 3 && r.RuntimeId == asset.RuntimeId && r.Tail == 200), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result());
        var result = await Service(db, gateway.Object).ReadAsync(asset.Runtime.PublicId, asset.Id,
            asset.Runtime.CreatedById!.Value, false, 200, default);
        Assert.Equal("running", result.State);
        gateway.VerifyAll();
    }

    [Fact]
    public async Task Read_DeniesUnrelatedUserBeforeContactingAgent()
    {
        await using var db = Context();
        var asset = await Seed(db);
        var gateway = new Mock<ITeamLabAssetDiagnosticsGateway>(MockBehavior.Strict);
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => Service(db, gateway.Object)
            .ReadAsync(asset.Runtime.PublicId, asset.Id, Guid.NewGuid(), false, 200, default));
        Assert.Equal(403, error.StatusCode);
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Read_RejectsResultWhenAssetWasRebuiltDuringCollection()
    {
        await using var db = Context();
        var asset = await Seed(db);
        var gateway = new Mock<ITeamLabAssetDiagnosticsGateway>();
        gateway.Setup(item => item.ReadAsync(It.IsAny<Guid>(), It.IsAny<TeamLabContainerDiagnosticsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { asset.Runtime.Generation++; await db.SaveChangesAsync(); return Result(); });
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => Service(db, gateway.Object)
            .ReadAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value, false, 200, default));
        Assert.Equal("diagnostics.stale_generation", error.Code);
    }

    private static TeamLabContainerDiagnostics Result() => new("running", false, 0, 0, "", "", "fixture output", false, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Read_RejectsResultWhenBoundNodeChanges()
    {
        await using var db = Context();
        var asset = await Seed(db);
        var gateway = new Mock<ITeamLabAssetDiagnosticsGateway>();
        gateway.Setup(item => item.ReadAsync(It.IsAny<Guid>(), It.IsAny<TeamLabContainerDiagnosticsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { asset.WorkerNodeId = Guid.NewGuid(); await db.SaveChangesAsync(); return Result(); });
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => Service(db, gateway.Object)
            .ReadAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value, false, 200, default));
        Assert.Equal("diagnostics.stale_generation", error.Code);
        Assert.Empty(await db.TeamLabEvents.ToArrayAsync());
    }

    [Fact]
    public async Task Read_RejectsResultWhenPermissionWasRevokedDuringCollection()
    {
        await using var db = Context();
        var asset = await Seed(db);
        var actor = asset.Runtime.CreatedById!.Value;
        var gateway = new Mock<ITeamLabAssetDiagnosticsGateway>();
        gateway.Setup(item => item.ReadAsync(It.IsAny<Guid>(), It.IsAny<TeamLabContainerDiagnosticsRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { asset.Runtime.CreatedById = Guid.NewGuid(); await db.SaveChangesAsync(); return Result(); });
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(() => Service(db, gateway.Object)
            .ReadAsync(asset.Runtime.PublicId, asset.Id, actor, false, 200, default));
        Assert.Equal(403, error.StatusCode);
        Assert.Empty(await db.TeamLabEvents.ToArrayAsync());
    }

    [Fact]
    public async Task Read_RecordsMetadataWithoutCopyingContainerOutput()
    {
        await using var db = Context();
        var asset = await Seed(db);
        var gateway = new Mock<ITeamLabAssetDiagnosticsGateway>();
        gateway.Setup(item => item.ReadAsync(It.IsAny<Guid>(), It.IsAny<TeamLabContainerDiagnosticsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result() with { LogsError = "diagnostics.logs_unavailable" });
        OperationalEventDraft? recorded = null;
        var writer = new Mock<IOperationalEventWriter>();
        writer.Setup(item => item.Append(It.IsAny<OperationalEventDraft>()))
            .Callback<OperationalEventDraft>(draft => recorded = draft);
        var service = new TeamLabAssetDiagnosticsService(db, new TeamLabAuthorizationService(db, [], []), gateway.Object,
            new TeamLabEventRecorder(db, writer.Object, new OperationalCorrelation()));
        await service.ReadAsync(asset.Runtime.PublicId, asset.Id, asset.Runtime.CreatedById!.Value, false, 200, default);
        Assert.NotNull(recorded);
        Assert.Equal(OperationalEventCodes.TeamLab.AssetDiagnosticsRead, recorded.EventCode);
        Assert.Equal(asset.WorkerNodeId, recorded.WorkerNodeId);
        Assert.Equal(asset.Runtime.CreatedById, recorded.Detail!["actorUserId"]);
        Assert.Equal(false, recorded.Detail["logsAvailable"]);
        Assert.Equal(6, recorded.Detail.Count);
        Assert.DoesNotContain("fixture output", System.Text.Json.JsonSerializer.Serialize(recorded));
        Assert.Single(await db.TeamLabEvents.ToArrayAsync());
    }
    private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static TeamLabAssetDiagnosticsService Service(AppDbContext db, ITeamLabAssetDiagnosticsGateway gateway) =>
        new(db, new TeamLabAuthorizationService(db, [], []), gateway,
            new TeamLabEventRecorder(db, new EfOperationalEventWriter(db, NullLogger<EfOperationalEventWriter>.Instance), new OperationalCorrelation()));
    private static async Task<TeamLabRuntimeAsset> Seed(AppDbContext db)
    {
        var asset = new TeamLabRuntimeAsset { Name = "fixture", Kind = TeamLabResourceKind.Docker,
            WorkerNodeId = Guid.NewGuid(), RuntimeResourceId = "fixture-container",
            Runtime = new TeamLabRuntime { CreatedById = Guid.NewGuid(), Generation = 3 } };
        db.TeamLabRuntimeAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }
}
