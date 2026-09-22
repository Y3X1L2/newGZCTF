using GZCTF.Modules.League.Application;
using GZCTF.Modules.League.Contracts;
using GZCTF.Modules.League.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GZCTF.Integration.Test.Tests.League;

public sealed class LeagueLifecycleTests(LeagueDatabase database) : IClassFixture<LeagueDatabase>
{
    private async Task<LeagueTestEnvironment> EnvironmentAsync()
    { var env = new LeagueTestEnvironment(database); await env.SeedAsync(); return env; }

    [Fact]
    public async Task DraftRoster_ReflectsCurrentMembers_ThenFreezesMembershipAndVisibility()
    {
        var env = await EnvironmentAsync();
        var newcomer = new GZCTF.Models.Data.UserInfo { UserName = "roster-" + Guid.NewGuid().ToString("N")[..8] };
        var newcomerActor = new GZCTF.Modules.Identity.Application.ActorContext(newcomer.Id, newcomer.Role);
        async Task ChangeMembersAsync(Guid remove, Guid add)
        {
            await using var db = database.CreateContext();
            var team = await db.Teams.Include(x => x.Members).SingleAsync(x => x.Id == env.FirstTeamId);
            team.Members.RemoveWhere(x => x.Id == remove);
            team.Members.Add(await db.Users.SingleAsync(x => x.Id == add));
            await db.SaveChangesAsync();
        }
        async Task AssertRosterAsync(GZCTF.Modules.Identity.Application.ActorContext actor, params Guid[] expected)
        {
            var detail = await env.DetailAsync(actor);
            Assert.Equal(expected.Order(), detail.Registrations.Single(x => x.TeamId == env.FirstTeamId).MemberIds.Order());
        }

        await using (var db = database.CreateContext())
        { db.Users.Add(newcomer); await db.SaveChangesAsync(); }
        await ChangeMembersAsync(Guid.Empty, env.Outsider.UserId!.Value);
        await env.ConfigureAsync();
        foreach (var actor in new[] { env.Admin, env.First, env.Outsider })
            await AssertRosterAsync(actor, env.First.UserId!.Value, env.Outsider.UserId.Value);
        Assert.Null((await env.DetailAsync()).ConfigurationVersion);
        await AssertRosterAsync(env.Second);

        await ChangeMembersAsync(env.Outsider.UserId.Value, newcomer.Id);
        foreach (var actor in new[] { env.Admin, env.First, newcomerActor })
            await AssertRosterAsync(actor, env.First.UserId!.Value, newcomer.Id);
        await AssertRosterAsync(env.Outsider);
        await using (var db = database.CreateContext())
        {
            Assert.All(await db.Set<LeagueRegistration>().Where(x => x.MatchId == env.MatchId).ToArrayAsync(),
                registration => Assert.Empty(registration.MemberIds));
            Assert.Null(await env.Service(db).GetParticipantTeamAsync(env.MatchId, newcomer.Id, default));
        }

        var prepared = await env.PrepareAsync();
        Assert.NotNull(prepared.ConfigurationVersion);
        await ChangeMembersAsync(newcomer.Id, env.Outsider.UserId.Value);
        foreach (var actor in new[] { env.Admin, env.First, newcomerActor })
            await AssertRosterAsync(actor, env.First.UserId!.Value, newcomer.Id);
        await AssertRosterAsync(env.Outsider);
        await AssertRosterAsync(env.Second);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(env.FirstTeamId, await env.Service(db).GetParticipantTeamAsync(env.MatchId, newcomer.Id, default));
            Assert.Null(await env.Service(db).GetParticipantTeamAsync(env.MatchId, env.Outsider.UserId.Value, default));
        }
    }

    [Fact]
    public async Task Registration_Review_Selection_Freeze_PersistAndEnforcePermissions()
    {
        var env = await EnvironmentAsync();
        await using var db = database.CreateContext(); var service = env.Service(db);
        Assert.Equal(403, (await Assert.ThrowsAsync<LeagueException>(() => service.RegisterAsync(env.MatchId, env.FirstTeamId, env.Outsider, default))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<LeagueException>(() => service.ReviewAsync(env.MatchId, env.FirstTeamId, LeagueRegistrationState.Approved, env.First, default))).StatusCode);
        await env.ConfigureAsync();
        var detail = await service.RegisterAsync(env.MatchId, env.FirstTeamId, env.First, default);
        Assert.Equal(2, (await env.DetailAsync()).Registrations.Count);
        Assert.Equal(detail.Match.Revision, (await env.DetailAsync()).Match.Revision);
        Assert.Equal("league_revision_conflict", (await Assert.ThrowsAsync<LeagueException>(() => service.PrepareAsync(env.MatchId, 0, env.Admin, default))).Code);
        var prepared = await env.PrepareAsync();
        Assert.Equal(LeagueMatchState.Preparing, prepared.Match.State);
        var duplicate = await service.PrepareAsync(env.MatchId, 0, env.Admin, default);
        Assert.Equal(prepared.Operation!.Id, duplicate.Operation!.Id);
        Assert.Equal("league_configuration_frozen", (await Assert.ThrowsAsync<LeagueException>(() => service.ReviewAsync(env.MatchId, env.FirstTeamId, LeagueRegistrationState.Rejected, env.Admin, default))).Code);
        var team = await db.Teams.Include(x => x.Members).SingleAsync(x => x.Id == env.FirstTeamId);
        team.Members.Clear(); team.CaptainId = env.Outsider.UserId!.Value; await db.SaveChangesAsync();
        Assert.Equal(env.FirstTeamId, await service.GetParticipantTeamAsync(env.MatchId, env.First.UserId!.Value, default));
        Assert.Null(await service.GetParticipantTeamAsync(env.MatchId, env.Outsider.UserId.Value, default));
        var player = await env.DetailAsync(env.First);
        Assert.NotEmpty(player.Registrations.Single(x => x.TeamId == env.FirstTeamId).MemberIds);
        Assert.Empty(player.Registrations.Single(x => x.TeamId == env.SecondTeamId).MemberIds);
    }

    [Fact]
    public async Task MissingProviders_AndDisabledFeature_DoNotCreatePreparationIntent()
    {
        var env = await EnvironmentAsync(); await env.ConfigureAsync(); env.Providers.IsAvailable = false;
        var error = await Assert.ThrowsAsync<LeagueException>(env.PrepareAsync);
        Assert.Equal(503, error.StatusCode); Assert.Null((await env.DetailAsync()).Operation);
        await using var db = database.CreateContext();
        Assert.Equal("league_disabled", (await Assert.ThrowsAsync<LeagueException>(() => env.Service(db, false).GetAsync(env.MatchId, env.Admin, default))).Code);
    }

    [Theory]
    [InlineData(LeagueProgressState.Pending, LeagueProgressState.Running)]
    [InlineData(LeagueProgressState.Failed, LeagueProgressState.Failed)]
    [InlineData(LeagueProgressState.Ready, LeagueProgressState.Ready)]
    public async Task SharedPreparationScenarios_ReportRealProgressAndResumeOriginalOperation(LeagueProgressState teamState, LeagueProgressState operationState)
    {
        var env = await EnvironmentAsync(); await env.ConfigureAsync(); var original = await env.PrepareAsync();
        env.Providers.PreparationState = teamState; await env.ProcessAsync();
        var detail = await env.DetailAsync(); Assert.Equal(operationState, detail.Operation!.State);
        Assert.Equal(original.Operation!.Id, detail.Operation.Id);
        if (teamState != LeagueProgressState.Ready)
        {
            Assert.Equal(LeagueMatchState.Preparing, detail.Match.State);
            await using var db = database.CreateContext(); var service = env.Service(db);
            Assert.Equal("league_not_ready", (await Assert.ThrowsAsync<LeagueException>(() => service.StartAsync(env.MatchId, env.Admin, default))).Code);
            if (teamState == LeagueProgressState.Failed) await service.RetryAsync(env.MatchId, false, env.Admin, default);
            env.Providers.PreparationState = LeagueProgressState.Ready; await env.ProcessAsync();
        }
        Assert.Equal(LeagueMatchState.Ready, (await env.DetailAsync()).Match.State);
        Assert.Single(env.Providers.Initializations);
        Assert.All(env.Providers.PreparationCalls, id => Assert.Equal(original.Operation.Id, id));
    }

    [Fact]
    public async Task Readiness_RequiresClosedAccessAndFlagBinding()
    {
        var env = await EnvironmentAsync(); await env.ConfigureAsync(); await env.PrepareAsync();
        env.Providers.AccessClosed = false; await env.ProcessAsync();
        Assert.Equal(LeagueMatchState.Preparing, (await env.DetailAsync()).Match.State);
        env.Providers.AccessClosed = true; env.Providers.BindingConfirmed = false; await env.ProcessAsync();
        Assert.Equal(LeagueFailure.FlagBindingFailed, (await env.DetailAsync()).Operation!.Failure);
    }

    [Fact]
    public async Task StartPartialFailure_RetriesSameId_AndRecordsOneStartTime()
    {
        var env = await EnvironmentAsync(); await env.ConfigureAsync(); await env.PrepareAsync(); await env.ProcessAsync();
        await using var db = database.CreateContext(); var service = env.Service(db);
        var start = await service.StartAsync(env.MatchId, env.Admin, default);
        env.Providers.OpenResult = new(false, LeagueFailure.AccessFailed); await env.ProcessAsync();
        var failed = await env.DetailAsync(); Assert.Equal(LeagueMatchState.Starting, failed.Match.State); Assert.Null(failed.Match.StartedAt);
        await service.RetryAsync(env.MatchId, false, env.Admin, default);
        env.Providers.OpenResult = new(true); await env.ProcessAsync();
        var running = await service.StartAsync(env.MatchId, env.Admin, default);
        Assert.Equal(LeagueMatchState.Running, running.Match.State); Assert.NotNull(running.Match.StartedAt);
        Assert.Equal(start.Operation!.Id, running.Operation!.Id);
        Assert.Equal(running.Match.StartedAt, (await service.StartAsync(env.MatchId, env.Admin, default)).Match.StartedAt);
        Assert.All(env.Providers.StartCalls, id => Assert.Equal(start.Operation.Id, id));
    }

    [Fact]
    public async Task TerminalWrite_RequiresCallerTransaction_AndRollsBackWithSubmission()
    {
        var env = await EnvironmentAsync(); await env.RunAsync(); var command = await env.KnockoutAsync();
        await using (var db = database.CreateContext())
        {
            var finalizer = new LeagueFinalizationService(db, new(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => finalizer.ConfirmKnockoutAsync(command, default));
            await using var tx = await db.Database.BeginTransactionAsync();
            // T5 substitute: a separate database row written in the same local transaction.
            await db.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE league_submission_probe (id uuid PRIMARY KEY)");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO league_submission_probe VALUES ({command.SubmissionId})");
            Assert.True((await finalizer.ConfirmKnockoutAsync(command, default)).Applied);
            await tx.RollbackAsync();
        }
        var after = await env.DetailAsync(); Assert.Equal(LeagueMatchState.Running, after.Match.State);
        Assert.Null(after.Match.Result); Assert.Null(after.Cleanup);
        await using (var db = database.CreateContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            Assert.True((await new LeagueFinalizationService(db, new(db)).ConfirmKnockoutAsync(command, default)).Applied);
            await tx.CommitAsync();
        }
        Assert.Equal(command.SubmissionId, (await env.DetailAsync()).Match.Result!.SubmissionId);
    }

    [Fact]
    public async Task ConcurrentKnockoutsAndAbort_HaveExactlyOneImmutableResultAndCleanupIntent()
    {
        var env = await EnvironmentAsync(); await env.RunAsync();
        var a = await env.KnockoutAsync(); var b = await env.KnockoutAsync(false);
        async Task<LeagueFinalizationResult> Finish(LeagueKnockoutCommand command)
        {
            await using var db = database.CreateContext(); await using var tx = await db.Database.BeginTransactionAsync();
            var result = await new LeagueFinalizationService(db, new(db)).ConfirmKnockoutAsync(command, default);
            await tx.CommitAsync(); return result;
        }
        var results = await Task.WhenAll(Finish(a), Finish(b));
        Assert.Single(results, x => x.Applied); Assert.Equal(results[0].Result, results[1].Result);
        var original = await env.DetailAsync();
        await using var abortDb = database.CreateContext();
        var aborted = await env.Service(abortDb).AbortAsync(env.MatchId, "测试中止", env.Admin, default);
        Assert.Equal(original.Match.Result, aborted.Match.Result); Assert.Equal(original.Cleanup!.Id, aborted.Cleanup!.Id);
        env.Providers.CleanupResult = new(false, LeagueFailure.CleanupFailed); await env.ProcessAsync();
        Assert.Equal(LeagueProgressState.Failed, (await env.DetailAsync()).Cleanup!.State);
        await env.Service(abortDb).RetryAsync(env.MatchId, true, env.Admin, default);
        env.Providers.CleanupResult = new(true); await env.ProcessAsync();
        var clean = await env.DetailAsync(); Assert.Equal(LeagueProgressState.Ready, clean.Cleanup!.State);
        Assert.Equal(original.Match.Result, clean.Match.Result);
        Assert.All(env.Providers.CleanupCalls, id => Assert.Equal(original.Cleanup.Id, id));
    }

    [Fact]
    public async Task AbortPartialPreparation_CleansWithoutRuntimeResponse_AndNeverRestarts()
    {
        var env = await EnvironmentAsync(); await env.ConfigureAsync(); await env.PrepareAsync();
        env.Providers.Throw = true; await env.ProcessAsync();
        Assert.Equal(LeagueFailure.ProviderUnavailable, (await env.DetailAsync()).Operation!.Failure);
        await using var db = database.CreateContext();
        var aborted = await env.Service(db).AbortAsync(env.MatchId, "准备失败，重新安排", env.Admin, default);
        Assert.Null(aborted.Match.Result!.WinnerTeamId); Assert.Equal(LeagueEndReason.Aborted, aborted.Match.Result.Reason);
        Assert.Equal("准备失败，重新安排", aborted.Match.Result.AbortReason);
        await env.ProcessAsync(); Assert.Equal(LeagueProgressState.Ready, (await env.DetailAsync()).Cleanup!.State);
        Assert.Single(env.Providers.PreparationCalls); Assert.Single(env.Providers.CleanupCalls);
        await Assert.ThrowsAsync<LeagueException>(() => env.Service(db).StartAsync(env.MatchId, env.Admin, default));
    }

    [Fact]
    public async Task Knockout_RejectsOutsiderOwnTargetAndOldGeneration()
    {
        var env = await EnvironmentAsync(); await env.RunAsync(); var command = await env.KnockoutAsync();
        async Task Reject(LeagueKnockoutCommand invalid, string code)
        {
            await using var db = database.CreateContext(); await using var tx = await db.Database.BeginTransactionAsync();
            Assert.Equal(code, (await Assert.ThrowsAsync<LeagueException>(() => new LeagueFinalizationService(db, new(db)).ConfirmKnockoutAsync(invalid, default))).Code);
        }
        await Reject(command with { UserId = env.Outsider.UserId!.Value }, "league_not_participant");
        await Reject(command with { Target = command.Target with { TeamId = env.FirstTeamId } }, "league_binding_mismatch");
        await Reject(command with { Target = command.Target with { Generation = 2 } }, "league_binding_mismatch");
        await Reject(command with { PreparationId = Guid.NewGuid() }, "league_binding_mismatch");
        Assert.Null((await env.DetailAsync()).Match.Result);
    }

    [Fact]
    public async Task Seats_CanSwapInDraft_AndFreezeInTheChosenOrder()
    {
        var env = await EnvironmentAsync(); await env.ConfigureAsync();
        await using var db = database.CreateContext(); var service = env.Service(db);
        var detail = await env.DetailAsync();
        await service.SelectTeamsAsync(env.MatchId, new(detail.Match.Revision, env.SecondTeamId, env.FirstTeamId), env.Admin, default);
        await env.PrepareAsync();
        var frozen = await service.GetFrozenAsync(env.MatchId, default);
        Assert.Equal(env.SecondTeamId, frozen.Teams[0].TeamId); Assert.Equal(1, frozen.Teams[0].Seat);
        Assert.Equal(env.FirstTeamId, frozen.Teams[1].TeamId); Assert.Equal(2, frozen.Teams[1].Seat);
        Assert.Equal("league_abort_reason_required", (await Assert.ThrowsAsync<LeagueException>(() => service.AbortAsync(env.MatchId, " ", env.Admin, default))).Code);
    }

    [Fact]
    public async Task Configuration_RejectsMissingReleaseNegativeCoinsAndOverlappingRosters()
    {
        var env = await EnvironmentAsync(); await env.ConfigureAsync();
        await using var db = database.CreateContext(); var service = env.Service(db);
        Assert.Equal("league_release_unavailable", (await Assert.ThrowsAsync<LeagueException>(() =>
            service.CreateAsync(new("invalid", Guid.NewGuid(), Guid.NewGuid(), 0), env.Admin, default))).Code);
        Assert.Equal("league_invalid_configuration", (await Assert.ThrowsAsync<LeagueException>(() =>
            service.CreateAsync(new("invalid", null, null, -1), env.Admin, default))).Code);
        var second = await db.Teams.Include(x => x.Members).SingleAsync(x => x.Id == env.SecondTeamId);
        second.Members.Add(await db.Users.SingleAsync(x => x.Id == env.First.UserId)); await db.SaveChangesAsync();
        Assert.Equal("league_roster_overlap", (await Assert.ThrowsAsync<LeagueException>(env.PrepareAsync)).Code);
        Assert.Null((await env.DetailAsync()).Operation);
    }

    [Fact]
    public async Task NonRetryableFailure_RequiresAbortInsteadOfRepeatingSideEffects()
    {
        var env = await EnvironmentAsync(); await env.ConfigureAsync(); await env.PrepareAsync();
        env.Providers.PreparationState = LeagueProgressState.Failed; env.Providers.Retryable = false;
        await env.ProcessAsync(); await env.ProcessAsync();
        Assert.Single(env.Providers.PreparationCalls);
        await using var db = database.CreateContext();
        Assert.Equal("league_not_retryable", (await Assert.ThrowsAsync<LeagueException>(() =>
            env.Service(db).RetryAsync(env.MatchId, false, env.Admin, default))).Code);
        Assert.DoesNotContain("retry", (await env.DetailAsync()).AllowedActions);
    }

    [Fact]
    public async Task ConcurrentAbortAndKnockout_UseTheSameTerminalConstraint()
    {
        var env = await EnvironmentAsync(); await env.RunAsync(); var command = await env.KnockoutAsync();
        async Task<LeagueResultModel> Knockout()
        {
            await using var db = database.CreateContext(); await using var tx = await db.Database.BeginTransactionAsync();
            var result = await new LeagueFinalizationService(db, new(db)).ConfirmKnockoutAsync(command, default);
            await tx.CommitAsync(); return result.Result;
        }
        async Task<LeagueResultModel> Abort()
        {
            await using var db = database.CreateContext();
            return (await env.Service(db).AbortAsync(env.MatchId, "管理员中止", env.Admin, default)).Match.Result!;
        }
        var results = await Task.WhenAll(Knockout(), Abort());
        Assert.Equal(results[0], results[1]);
        Assert.NotNull((await env.DetailAsync()).Cleanup);
    }
}
