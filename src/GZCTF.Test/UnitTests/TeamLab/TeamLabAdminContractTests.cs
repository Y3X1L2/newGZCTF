using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Modules.Runtime.Application;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Application.Rollouts;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public sealed class TeamLabAdminContractTests
{
    [Fact]
    public async Task AdminDraft_CanPersistIncompleteTopology_WhileStrictCreateRejectsIt()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new TeamLabTopologyApplicationService(
            context, new TeamLabTopologyValidator(), null!, new TeamLabControlScopeService(context),
            new NodeCapacitySnapshotService(context));
        var request = new CreateTeamLabTopologyModel(
            "Draft", [], [], [],
            new TeamLabTopologyEditorModel(
                new Dictionary<string, TeamLabEditorItemModel>(),
                new Dictionary<string, TeamLabEditorItemModel>()));
        var owner = Guid.CreateVersion7();

        var draft = await service.CreateDraftAsync(request, owner, CancellationToken.None);

        Assert.Equal("Draft", draft.Definition.Name);
        Assert.Empty(draft.Definition.Networks);
        Assert.Empty(draft.Definition.Assets);
        var error = await Assert.ThrowsAsync<TeamLabApiContractException>(
            () => service.CreateAsync(request, owner, CancellationToken.None));
        Assert.Equal("topology_invalid", error.Code);
    }

    [Fact]
    public async Task DraftWithoutEditor_GetsStableLayout_AndUpdateWithoutEditorKeepsIt()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new TeamLabTopologyApplicationService(
            context, new TeamLabTopologyValidator(), null!, new TeamLabControlScopeService(context),
            new NodeCapacitySnapshotService(context));
        var owner = Guid.CreateVersion7();
        var networks = new[]
        {
            new TeamLabTopologyNetworkModel(
                "entry", "入口网", new TeamLabAddressPoolModel("10.40.0.0/16", 24), true, 0),
            new TeamLabTopologyNetworkModel(
                "office", "办公网", new TeamLabAddressPoolModel("10.41.0.0/16", 24), false, 1)
        };
        var assets = new[]
        {
            Asset("web", "Web", "entry", 1, 10),
            Asset("api", "API", "entry", 2, 11),
            Asset("client", "Client", "office", 3, 12)
        };
        var create = new CreateTeamLabTopologyModel("Layout", networks, assets, []);

        var created = await service.CreateDraftAsync(create, owner, CancellationToken.None);

        Assert.Equal(2, created.Editor.Networks.Count);
        Assert.Equal(3, created.Editor.Assets.Count);
        Assert.Equal(2, created.Editor.Networks.Values.Select(item => (item.X, item.Y)).Distinct().Count());
        Assert.Equal(3, created.Editor.Assets.Values.Select(item => (item.X, item.Y)).Distinct().Count());
        var initialLayout = JsonSerializer.Serialize(created.Editor);
        context.ChangeTracker.Clear();

        var updated = await service.UpdateDraftAsync(
            created.Id,
            new UpdateTeamLabTopologyModel(created.Revision, "Layout", networks, assets, []),
            owner,
            false,
            CancellationToken.None);

        Assert.Equal(created.Revision, updated.Revision);
        Assert.Equal(initialLayout, JsonSerializer.Serialize(updated.Editor));
    }

    [Fact]
    public async Task EditorLayout_PreservesExplicitZeroCoordinates()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new TeamLabTopologyApplicationService(
            context, new TeamLabTopologyValidator(), null!, new TeamLabControlScopeService(context),
            new NodeCapacitySnapshotService(context));
        var owner = Guid.CreateVersion7();
        var network = new TeamLabTopologyNetworkModel(
            "entry", "入口网", new TeamLabAddressPoolModel("10.42.0.0/16", 24), true);
        var asset = Asset("web", "Web", "entry", 1, 10);
        var editor = new TeamLabTopologyEditorModel(
            new Dictionary<string, TeamLabEditorItemModel> { ["entry"] = new(0, 0, 560, 360) },
            new Dictionary<string, TeamLabEditorItemModel> { ["web"] = new(0, 0) });

        var created = await service.CreateDraftAsync(
            new CreateTeamLabTopologyModel("Zero", [network], [asset], [], editor),
            owner,
            CancellationToken.None);

        Assert.Equal(0, created.Editor.Networks["entry"].X);
        Assert.Equal(0, created.Editor.Networks["entry"].Y);
        Assert.Equal(0, created.Editor.Assets["web"].X);
        Assert.Equal(0, created.Editor.Assets["web"].Y);
    }

    private static TeamLabTopologyAssetModel Asset(
        string key,
        string name,
        string network,
        int imageTemplateId,
        int hostOffset) => new(
        key,
        name,
        TeamLabAssetKind.Docker,
        imageTemplateId,
        new TeamLabAssetResourceModel(10, 256, 512),
        [new TeamLabTopologyInterfaceModel("eth0", network, hostOffset, true)]);

    [Fact]
    public void EditorMetadata_RoundTripsInfrastructurePositions()
    {
        var model = new TeamLabTopologyEditorModel(
            new Dictionary<string, TeamLabEditorItemModel>(),
            new Dictionary<string, TeamLabEditorItemModel>(),
            new Dictionary<string, TeamLabEditorItemModel>
            {
                ["router-core"] = new(120, 240, 180, 96)
            });

        var roundTrip = JsonSerializer.Deserialize<TeamLabTopologyEditorModel>(
            JsonSerializer.Serialize(model));

        var position = Assert.Single(Assert.IsAssignableFrom<
            IReadOnlyDictionary<string, TeamLabEditorItemModel>>(roundTrip!.Infrastructure));
        Assert.Equal("router-core", position.Key);
        Assert.Equal(120, position.Value.X);
        Assert.Equal(240, position.Value.Y);
    }

    [Fact]
    public void EditorMetadata_OldJsonKeepsInfrastructureOptional()
    {
        const string json = """
                            {"Networks":{},"Assets":{}}
                            """;

        var model = JsonSerializer.Deserialize<TeamLabTopologyEditorModel>(json);

        Assert.NotNull(model);
        Assert.Null(model.Infrastructure);
    }

    [Theory]
    [InlineData(Role.Teacher, true, true)]
    [InlineData(Role.Teacher, false, false)]
    [InlineData(Role.Admin, false, true)]
    [InlineData(Role.SuperAdmin, false, true)]
    public void ResourceOwnership_UsesOwnerOrAdministratorPolicy(
        Role role,
        bool sameOwner,
        bool expected)
    {
        var actor = Guid.CreateVersion7();
        var owner = sameOwner ? actor : Guid.CreateVersion7();

        Assert.Equal(expected, ResourceOwnershipPolicy.CanManage(owner, actor, role));
    }

    [Fact]
    public void ResourceOwnership_UnownedResourcesRequireAdministrator()
    {
        var actor = Guid.CreateVersion7();

        Assert.False(ResourceOwnershipPolicy.CanManage(null, actor, Role.Teacher));
        Assert.True(ResourceOwnershipPolicy.CanManage(null, actor, Role.Admin));
    }

    [Fact]
    public void RuntimeCreationIdempotency_IsStoredSeparatelyFromBusinessReference()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var runtime = context.Model.FindEntityType(typeof(TeamLabRuntime));

        Assert.NotNull(runtime);
        Assert.Equal(128, runtime.FindProperty(nameof(TeamLabRuntime.CreationIdempotencyKey))?.GetMaxLength());
        var index = Assert.Single(runtime.GetIndexes(), item =>
            item.Properties.Select(property => property.Name).SequenceEqual([
                nameof(TeamLabRuntime.CreatedById),
                nameof(TeamLabRuntime.CreationIdempotencyKey)
            ]));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public async Task RolloutEnsure_IsIdempotent_AndPersistsDesiredPreparation()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var releaseId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var service = new TeamLabRolloutApplicationService(context);

        var first = await service.EnsureAsync(
            releaseId, ownerId, ownerId, "penetration", "game:17", CancellationToken.None);
        var second = await service.EnsureAsync(
            releaseId, ownerId, ownerId, "penetration", "game:17", CancellationToken.None);
        var prepared = await service.RequestPreparationAsync(first.Id, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.True(prepared.PreparationRequested);
        Assert.Equal("preparing", prepared.Status);
        Assert.Single(context.TeamLabRollouts);
    }

    [Fact]
    public async Task RolloutResume_RequeuesCoordinationAfterPause()
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var ownerId = Guid.CreateVersion7();
        var service = new TeamLabRolloutApplicationService(context);
        var rollout = await service.EnsureAsync(
            Guid.CreateVersion7(), ownerId, ownerId, "penetration", "game:18", CancellationToken.None);
        var entity = await context.TeamLabRollouts.SingleAsync(item => item.PublicId == rollout.Id);
        entity.Status = TeamLabRolloutStatus.Ready;
        entity.PauseRequested = true;
        entity.PreparationRequested = false;
        await context.SaveChangesAsync();

        var resumed = await service.RequestResumeAsync(rollout.Id, CancellationToken.None);

        Assert.False(resumed.PauseRequested);
        Assert.True(resumed.PreparationRequested);
    }

    [Fact]
    public void RolloutTarget_UsesStableUniqueSubjectAndCursorIndexes()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var target = context.Model.FindEntityType(typeof(TeamLabRolloutTarget));

        Assert.NotNull(target);
        Assert.Contains(target.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(TeamLabRolloutTarget.RolloutId),
                nameof(TeamLabRolloutTarget.ExternalSubject)
            ]));
        Assert.Contains(target.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(TeamLabRolloutTarget.RolloutId),
                nameof(TeamLabRolloutTarget.Status),
                nameof(TeamLabRolloutTarget.Id)
            ]));
    }
}
