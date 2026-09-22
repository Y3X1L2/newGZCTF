using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Domain;
using GZCTF.Modules.League.Infrastructure;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Application.Rollouts;
using GZCTF.Modules.TeamLab.Contracts;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GZCTF.Test.UnitTests.League;

public sealed class LeagueTeamLabAdapterTests
{
    [Fact]
    public async Task Prepare_ReusesOneRolloutWithTwoTeamTargets()
    {
        await using var db = CreateContext();
        var fixture = Seed(db);
        var adapter = Adapter(db);
        var cores = Cores(fixture.Match);

        var first = await adapter.PrepareAsync(fixture.Match, cores, default);
        var second = await adapter.PrepareAsync(fixture.Match, cores, default);

        Assert.Equal(first.OperationId, second.OperationId);
        Assert.Equal(2, first.Teams.Count);
        Assert.All(first.Teams, item => Assert.Equal(LeagueProgressState.Running, item.State));
        var rollout = Assert.Single(await db.TeamLabRollouts.Include(item => item.Targets).ToArrayAsync());
        Assert.Equal(LeagueTeamLabAdapter.Kind, rollout.AdapterKind);
        Assert.True(rollout.PreparationRequested);
        Assert.Equal(2, rollout.Targets.Count);
        Assert.Equal(2, rollout.Targets.Select(item => item.ExternalSubject).Distinct().Count());
    }

    [Fact]
    public async Task Provision_UsesResolvedMaterialAsRuntimeSecretOverlay()
    {
        await using var db = CreateContext();
        var fixture = Seed(db);
        var materialId = Guid.NewGuid();
        CreateTeamLabRuntimeModel? submitted = null;
        string? idempotencyKey = null;
        var runtime = new Mock<ITeamLabRuntimeApplicationService>();
        runtime.Setup(item => item.PlanAndEnqueueAsync(
                It.IsAny<CreateTeamLabRuntimeModel>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((CreateTeamLabRuntimeModel command, Guid _, Guid _, string _, string? key, Guid? _, string? _, CancellationToken _) =>
            {
                submitted = command;
                idempotencyKey = key;
            })
            .ReturnsAsync(new TeamLabRuntimeCreateResult(42, Guid.NewGuid(), false));
        var materials = MaterialPort(materialId, new(materialId, "core-api", "MATCH_FLAG", "flag-value"));
        var adapter = Adapter(db, runtime.Object, materials);
        var rollout = new TeamLabRollout
        {
            ReleaseId = fixture.Match.ReleaseId,
            OwnerUserId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            AdapterKind = LeagueTeamLabAdapter.Kind,
            ExternalReference = $"league:{fixture.Match.MatchId:N}:preparation:{fixture.Match.PreparationId:N}"
        };
        var target = new TeamLabRolloutTarget
        {
            ExternalSubject = $"team:{fixture.Match.Teams[0].TeamId}:material:{materialId:N}",
            DisplayName = fixture.Match.Teams[0].Name
        };

        await adapter.ProvisionAsync(rollout, target, default);

        Assert.NotNull(submitted);
        var overlay = Assert.Single(submitted!.Overlays!);
        Assert.Equal("core-api", overlay.AssetKey);
        Assert.Equal("flag-value", overlay.Secrets!["MATCH_FLAG"]);
        Assert.Equal($"league-{fixture.Match.PreparationId:N}-team-{fixture.Match.Teams[0].TeamId}", idempotencyKey);
    }

    [Fact]
    public async Task OpenAndCleanup_MutateOnlyTheBoundRollout()
    {
        await using var db = CreateContext();
        var fixture = Seed(db);
        var rollout = new TeamLabRollout
        {
            ReleaseId = fixture.Match.ReleaseId,
            OwnerUserId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            AdapterKind = LeagueTeamLabAdapter.Kind,
            ExternalReference = $"league:{fixture.Match.MatchId:N}:preparation:{fixture.Match.PreparationId:N}",
            Status = TeamLabRolloutStatus.Ready
        };
        var bindings = new List<LeagueRuntimeBinding>();
        foreach (var team in fixture.Match.Teams)
        {
            var runtime = new TeamLabRuntime
            {
                TopologyReleaseId = fixture.Match.ReleaseId,
                Status = TeamLabRuntimeStatus.Running,
                Generation = 1
            };
            rollout.Targets.Add(new TeamLabRolloutTarget
            {
                ExternalSubject = $"team:{team.TeamId}:material:{Guid.NewGuid():N}",
                DisplayName = team.Name,
                Runtime = runtime,
                Status = TeamLabRolloutTargetStatus.AccessOpen
            });
            bindings.Add(new(team.TeamId, runtime.PublicId, runtime.Generation));
        }
        db.Add(rollout);
        await db.SaveChangesAsync();
        var adapter = Adapter(db, materials: new UnavailableLeagueProviders());

        var opened = await adapter.OpenAccessAsync(fixture.Match, Guid.NewGuid(), bindings, default);
        var cleanup = await adapter.CleanupAsync(fixture.Match, Guid.NewGuid(), default);

        Assert.True(adapter.IsAvailable);
        Assert.True(opened.Completed);
        Assert.False(cleanup.Completed);
        await db.Entry(rollout).ReloadAsync();
        Assert.True(rollout.DesiredAccessOpen is false);
        Assert.True(rollout.DrainRequested);
        Assert.Equal(TeamLabRolloutStatus.Draining, rollout.Status);
    }

    [Fact]
    public async Task AttackAccess_RequiresRunningMatchAndParticipant()
    {
        await using var db = CreateContext();
        var fixture = Seed(db);
        var adapter = Adapter(db);
        var outsider = Guid.NewGuid();

        var closed = await Assert.ThrowsAsync<LeagueException>(() =>
            adapter.CreateAsync(fixture.Match.MatchId, outsider, default));
        Assert.Equal("league_access_closed", closed.Code);

        var match = await db.Set<LeagueMatch>().SingleAsync(item => item.Id == fixture.Match.MatchId);
        match.State = LeagueMatchState.Running;
        await db.SaveChangesAsync();

        var forbidden = await Assert.ThrowsAsync<LeagueException>(() =>
            adapter.CreateAsync(fixture.Match.MatchId, outsider, default));
        Assert.Equal("league_forbidden", forbidden.Code);
    }

    private static LeagueTeamLabAdapter Adapter(
        AppDbContext db,
        ITeamLabRuntimeApplicationService? runtimes = null,
        ILeagueCoreMaterialPort? materials = null)
    {
        var flags = new Mock<ILeagueFlagPort>();
        flags.SetupGet(item => item.IsAvailable).Returns(true);
        materials ??= MaterialPort(Guid.Empty, new(Guid.Empty, "core", "FLAG", "value"));
        return new(db, new TeamLabRolloutApplicationService(db), runtimes ?? Mock.Of<ITeamLabRuntimeApplicationService>(),
            null!, flags.Object, materials);
    }

    private static ILeagueCoreMaterialPort MaterialPort(Guid id, LeagueCoreMaterial material)
    {
        var result = new Mock<ILeagueCoreMaterialPort>();
        result.SetupGet(item => item.IsAvailable).Returns(true);
        result.Setup(item => item.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(material);
        return result.Object;
    }

    private static IReadOnlyList<LeagueCoreReference> Cores(LeagueFrozenMatch match) =>
        match.Teams.Select(team => new LeagueCoreReference(team.TeamId, "core", Guid.NewGuid())).ToArray();

    private static Fixture Seed(AppDbContext db)
    {
        var ownerId = Guid.NewGuid();
        var scope = new TeamLabControlScope { Key = Guid.NewGuid().ToString("N"), DisplayName = "League" };
        var topology = new TeamLabTopology { Name = "League", OwnerUserId = ownerId, ControlScope = scope };
        var release = new TeamLabTopologyRelease
        {
            Topology = topology,
            ControlScope = scope,
            Version = 1,
            CanonicalJson = "{}",
            ContentHash = "league"
        };
        var match = new LeagueMatch
        {
            CreatedById = ownerId,
            Name = "League",
            TopologyId = topology.PublicId,
            ReleaseId = release.Id,
            PreparationId = Guid.NewGuid(),
            ConfigurationVersion = 1,
            State = LeagueMatchState.Preparing,
            Registrations =
            [
                Registration(11, 1, Guid.NewGuid()),
                Registration(22, 2, Guid.NewGuid())
            ]
        };
        db.AddRange(scope, topology, release, match);
        db.SaveChanges();
        return new(LeagueMatchStore.Frozen(match));
    }

    private static LeagueRegistration Registration(int teamId, int seat, Guid memberId) => new()
    {
        TeamId = teamId,
        TeamName = $"Team {teamId}",
        RegisteredById = memberId,
        State = LeagueRegistrationState.Approved,
        Selected = true,
        Seat = seat,
        MemberIds = [memberId]
    };

    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options)
    {
        SuppressProjectionRevisionBumps = true
    };

    private sealed record Fixture(LeagueFrozenMatch Match);
}
