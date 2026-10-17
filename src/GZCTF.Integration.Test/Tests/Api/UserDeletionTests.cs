using System.Net;
using System.Net.Http.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Utils;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SeededUser = GZCTF.Integration.Test.Base.TestDataSeeder.SeededUser;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class UserDeletionTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task DeleteCaptain_RetiresIdentityTransfersTeamsAndPreservesBusinessReferences()
    {
        var admin = await CreateUser(Role.SuperAdmin);
        var victim = await CreateUser(Role.Teacher);
        var member = await CreateUser(Role.Student);
        var shared = await TestDataSeeder.CreateTeamAsync(factory.Services, victim.Id, TestDataSeeder.RandomName());
        var solo = await TestDataSeeder.CreateTeamAsync(factory.Services, victim.Id, TestDataSeeder.RandomName());
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, TestDataSeeder.RandomName());
        var challenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, game.Id,
            TestDataSeeder.RandomName(), "test-only-flag");
        string oldInvite;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var team = await db.Teams.Include(t => t.Members).SingleAsync(t => t.Id == shared.Id);
            team.Members.Add(await db.Users.SingleAsync(u => u.Id == member.Id));
            team.Locked = true; // Administrator deletion also handles active/locked teams.
            var soloTeam = await db.Teams.SingleAsync(t => t.Id == solo.Id);
            oldInvite = soloTeam.InviteCode;
            var user = await db.Users.SingleAsync(u => u.Id == victim.Id);
            user.RealName = "Private Name";
            user.StdNumber = "private-id";
            user.PhoneNumber = "123456789";
            var participation = new Participation { TeamId = solo.Id, GameId = game.Id, Status = ParticipationStatus.Accepted };
            participation.Members.Add(new UserParticipation { UserId = victim.Id, TeamId = solo.Id, GameId = game.Id, Participation = participation });
            db.Participations.Add(participation);
            db.Submissions.Add(new Submission
            {
                UserId = victim.Id, TeamId = solo.Id, GameId = game.Id, ChallengeId = challenge.Id,
                Participation = participation, Status = AnswerResult.Accepted, Score = 321
            });
            var group = new StudentGroup { Name = TestDataSeeder.RandomName(), CreatedById = victim.Id };
            group.Members.Add(new StudentGroupMember { StudentId = victim.Id });
            group.Managers.Add(new StudentGroupManager { ManagerId = victim.Id });
            db.StudentGroups.Add(group);
            db.ApiTokens.Add(new ApiToken { Name = "retained audit token", CreatorId = victim.Id });
            db.UserLogins.Add(new IdentityUserLogin<Guid> { UserId = victim.Id, LoginProvider = "Portal", ProviderKey = Guid.NewGuid().ToString() });
            db.UserTokens.Add(new IdentityUserToken<Guid> { UserId = victim.Id, LoginProvider = "test", Name = "refresh", Value = "test-only" });
            db.TeamJoinRequests.Add(new TeamJoinRequest { TeamId = shared.Id, UserId = victim.Id });
            await db.SaveChangesAsync();
        }
        using var oldSession = await Login(victim);
        Assert.Equal(HttpStatusCode.OK, (await oldSession.GetAsync("/api/Account/Profile")).StatusCode);
        using var adminClient = await Login(admin);
        (await adminClient.DeleteAsync($"/api/Admin/Users/{victim.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/Account/Profile")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.GetAsync($"/api/Admin/Users/{victim.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.GetAsync($"/api/users/{victim.Id}")).StatusCode);
        Assert.DoesNotContain(victim.Id.ToString(), await (await adminClient.GetAsync($"/api/Admin/Users?keyword={victim.Id}")).Content.ReadAsStringAsync());
        using var memberClient = await Login(member);
        Assert.Equal(HttpStatusCode.BadRequest, (await memberClient.PostAsJsonAsync("/api/Team/Accept", oldInvite)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await memberClient.PostAsJsonAsync($"/api/Team/{solo.Id}/Requests", new { Message = "join" })).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == victim.Id);
            Assert.NotNull(user.DeletedAtUtc);
            Assert.Equal(Role.Banned, user.Role);
            Assert.Null(user.Email);
            Assert.Null(user.NormalizedUserName);
            Assert.Null(user.PasswordHash);
            Assert.Empty(user.RealName);
            Assert.Empty(user.StdNumber);
            Assert.Null(user.PhoneNumber);
            Assert.Empty(await db.UserLogins.Where(l => l.UserId == victim.Id).ToListAsync());
            Assert.Empty(await db.UserTokens.Where(t => t.UserId == victim.Id).ToListAsync());
            Assert.NotNull((await db.ApiTokens.SingleAsync(t => t.CreatorId == victim.Id)).RevokedAt);
            Assert.False(await db.StudentGroupMembers.AnyAsync(m => m.StudentId == victim.Id));
            Assert.False(await db.StudentGroupManagers.AnyAsync(m => m.ManagerId == victim.Id));
            Assert.False(await db.Teams.AnyAsync(t => t.Members.Any(m => m.Id == victim.Id)));
            Assert.Equal(member.Id, (await db.Teams.SingleAsync(t => t.Id == shared.Id)).CaptainId);
            Assert.True((await db.Teams.SingleAsync(t => t.Id == solo.Id)).Locked);
            Assert.True(await db.UserParticipations.AnyAsync(p => p.UserId == victim.Id && p.TeamId == solo.Id));
            Assert.True(await db.Participations.AnyAsync(p => p.TeamId == solo.Id && p.Status == ParticipationStatus.Accepted));
            Assert.Equal(321, (await db.Submissions.SingleAsync(s => s.UserId == victim.Id)).Score);
            Assert.Equal(TeamJoinRequestStatus.Rejected, (await db.TeamJoinRequests.SingleAsync(r => r.UserId == victim.Id)).Status);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PutAsJsonAsync($"/api/Admin/Users/{victim.Id}", new { Role = Role.Student })).StatusCode);

        // Login keys can be reused, but cannot restore access to the former user's history.
        var replacement = await TestDataSeeder.CreateUserAsync(factory.Services, victim.UserName, victim.Password, victim.Email);
        Assert.NotEqual(victim.Id, replacement.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.DeleteAsync($"/api/Admin/Users/{victim.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(Role.Student)]
    [InlineData(Role.Teacher)]
    [InlineData(Role.Admin)]
    public async Task DeleteUser_EnforcesRoleHierarchy(Role role)
    {
        var actor = await CreateUser(role);
        var target = await CreateUser(role == Role.Admin ? Role.SuperAdmin : Role.Student);
        using var client = await Login(actor);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/Admin/Users/{target.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteUser_RejectsSelfAndAnonymous()
    {
        var actor = await CreateUser(Role.SuperAdmin);
        using var client = await Login(actor);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/api/Admin/Users/{actor.Id}")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/Admin/Users/{actor.Id}")).StatusCode);
    }

    private Task<SeededUser> CreateUser(Role role) => TestDataSeeder.CreateUserAsync(factory.Services,
        TestDataSeeder.RandomName(), "Delete@Test123", role: role);

    private async Task<HttpClient> Login(SeededUser user)
    {
        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = user.Password })).EnsureSuccessStatusCode();
        return client;
    }
}
