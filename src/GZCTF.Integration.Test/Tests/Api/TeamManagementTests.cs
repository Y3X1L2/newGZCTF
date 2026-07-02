using System.Net;
using System.Net.Http.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Info;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

/// <summary>
/// Integration tests for team management operations
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public class TeamManagementTests(GZCTFApplicationFactory factory)
{
    /// <summary>
    /// Test team creation and retrieval
    /// </summary>
    [Fact]
    public async Task Team_Creation_And_Retrieval_ShouldWork()
    {
        var password = "Team@Create123";
        var userName = TestDataSeeder.RandomName();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            userName, password);

        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password });
        loginResponse.EnsureSuccessStatusCode();

        // Test 1: Create a new team via API
        var createResponse = await client.PostAsJsonAsync("/api/Team",
            new TeamUpdateModel { Name = "Test Team Alpha", Bio = "This is a test team for integration testing" });
        createResponse.EnsureSuccessStatusCode();
        var createdTeam = await createResponse.Content.ReadFromJsonAsync<TeamInfoModel>();
        Assert.NotNull(createdTeam);
        Assert.Equal("Test Team Alpha", createdTeam.Name);
        Assert.Equal("This is a test team for integration testing", createdTeam.Bio);

        // Test 2: Retrieve the created team by ID
        var getResponse = await client.GetAsync($"/api/Team/{createdTeam.Id}");
        getResponse.EnsureSuccessStatusCode();
        var retrievedTeam = await getResponse.Content.ReadFromJsonAsync<TeamInfoModel>();
        Assert.NotNull(retrievedTeam);
        Assert.Equal(createdTeam.Id, retrievedTeam.Id);
        Assert.Equal(createdTeam.Name, retrievedTeam.Name);

        // Test 3: Get user's teams
        var teamsResponse = await client.GetAsync("/api/Team");
        teamsResponse.EnsureSuccessStatusCode();
        var teams = await teamsResponse.Content.ReadFromJsonAsync<TeamInfoModel[]>();
        Assert.NotNull(teams);
        Assert.Contains(teams, t => t.Id == createdTeam.Id);
    }

    /// <summary>
    /// Test team update functionality
    /// </summary>
    [Fact]
    public async Task Team_Update_ShouldModifyTeamInfo()
    {
        var password = "Team@Update123";
        var userName = TestDataSeeder.RandomName();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            userName, password);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, user.Id, "Original Team Name");

        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password });

        // Test 1: Update team information
        var updateResponse = await client.PutAsJsonAsync($"/api/Team/{team.Id}",
            new TeamUpdateModel { Name = "Updated Team Name", Bio = "Updated bio information" });
        updateResponse.EnsureSuccessStatusCode();
        var updatedTeam = await updateResponse.Content.ReadFromJsonAsync<TeamInfoModel>();
        Assert.NotNull(updatedTeam);
        Assert.Equal("Updated Team Name", updatedTeam.Name);
        Assert.Equal("Updated bio information", updatedTeam.Bio);

        // Test 2: Verify the update persisted
        var getResponse = await client.GetAsync($"/api/Team/{team.Id}");
        getResponse.EnsureSuccessStatusCode();
        var retrievedTeam = await getResponse.Content.ReadFromJsonAsync<TeamInfoModel>();
        Assert.NotNull(retrievedTeam);
        Assert.Equal("Updated Team Name", retrievedTeam.Name);
        Assert.Equal("Updated bio information", retrievedTeam.Bio);
    }

    /// <summary>
    /// Test team member limit enforcement
    /// </summary>
    [Fact]
    public async Task Team_Creation_ShouldEnforceLimit()
    {
        var password = "Team@Limit123";
        var userName = TestDataSeeder.RandomName();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            userName, password);

        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password });

        // Create teams up to the limit (MaxTeamsAllowed = 3 per TeamController)
        var team1Response = await client.PostAsJsonAsync("/api/Team", new TeamUpdateModel { Name = "Team One" });
        team1Response.EnsureSuccessStatusCode();

        var team2Response = await client.PostAsJsonAsync("/api/Team", new TeamUpdateModel { Name = "Team Two" });
        team2Response.EnsureSuccessStatusCode();

        var team3Response = await client.PostAsJsonAsync("/api/Team", new TeamUpdateModel { Name = "Team Three" });
        team3Response.EnsureSuccessStatusCode();

        // Test: Attempt to create a 4th team should fail
        var team4Response = await client.PostAsJsonAsync("/api/Team", new TeamUpdateModel { Name = "Team Four" });
        Assert.Equal(HttpStatusCode.BadRequest, team4Response.StatusCode);
    }

    /// <summary>
    /// Test that unauthenticated users cannot access team endpoints
    /// </summary>
    [Fact]
    public async Task Team_Endpoints_ShouldRequireAuthentication()
    {
        using var client = factory.CreateClient();

        // Test 1: GET /api/Team should return 401
        var getTeamsResponse = await client.GetAsync("/api/Team");
        Assert.Equal(HttpStatusCode.Unauthorized, getTeamsResponse.StatusCode);

        // Test 2: POST /api/Team should return 401
        var createResponse =
            await client.PostAsJsonAsync("/api/Team", new TeamUpdateModel { Name = "Unauthorized Team" });
        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);

        // Test 3: PUT /api/Team/{id} should return 401
        var updateResponse = await client.PutAsJsonAsync("/api/Team/1", new TeamUpdateModel { Name = "Updated Name" });
        Assert.Equal(HttpStatusCode.Unauthorized, updateResponse.StatusCode);
    }

    /// <summary>
    /// Test team information validation
    /// </summary>
    [Fact]
    public async Task Team_Creation_ShouldValidateInput()
    {
        var password = "Team@Validate123";
        var userName = TestDataSeeder.RandomName();
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            userName, password);

        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password });

        // Test 1: Empty team name should be rejected
        var emptyNameResponse = await client.PostAsJsonAsync("/api/Team", new TeamUpdateModel { Name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyNameResponse.StatusCode);

        // Test 2: Null team name should be rejected
        var nullNameResponse = await client.PostAsJsonAsync("/api/Team", new TeamUpdateModel { Name = null! });
        Assert.Equal(HttpStatusCode.BadRequest, nullNameResponse.StatusCode);
    }

    [Fact]
    public async Task Team_AcceptInvite_ShouldRejectLockedTeamInActiveGame()
    {
        var captainPassword = "Team@InviteCaptain123";
        var memberPassword = "Team@InviteMember123";
        var captain = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), captainPassword);
        var member = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), memberPassword);
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, captain.Id, "Locked Invite Team");
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, "Locked Invite Game");
        await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, captain.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var teamEntity = await context.Teams.FirstAsync(t => t.Id == team.Id);
            teamEntity.Locked = true;
            await context.SaveChangesAsync();
        }

        using var captainClient = factory.CreateClient();
        var captainLogin = await captainClient.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = captain.UserName, Password = captainPassword });
        captainLogin.EnsureSuccessStatusCode();
        var inviteResponse = await captainClient.GetAsync($"/api/Team/{team.Id}/Invite");
        inviteResponse.EnsureSuccessStatusCode();
        var inviteCode = await inviteResponse.Content.ReadFromJsonAsync<string>();

        using var memberClient = factory.CreateClient();
        var memberLogin = await memberClient.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = member.UserName, Password = memberPassword });
        memberLogin.EnsureSuccessStatusCode();

        var acceptResponse = await memberClient.PostAsJsonAsync("/api/Team/Accept", inviteCode);
        Assert.Equal(HttpStatusCode.BadRequest, acceptResponse.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var joined = await verifyContext.Teams
            .Where(t => t.Id == team.Id)
            .SelectMany(t => t.Members)
            .AnyAsync(u => u.Id == member.Id);
        Assert.False(joined);
    }

    [Fact]
    public async Task Team_KickUser_ShouldRemoveKickedUserParticipationOnly()
    {
        var captainPassword = "Team@KickCaptain123";
        var captain = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), captainPassword);
        var member = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), "Team@KickMember123");
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, captain.Id, "Kick Participation Team");
        var game = await TestDataSeeder.CreateGameAsync(factory.Services, "Kick Participation Game");

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var teamEntity = await context.Teams.Include(t => t.Members).FirstAsync(t => t.Id == team.Id);
            var memberEntity = await context.Users.FirstAsync(u => u.Id == member.Id);
            teamEntity.Members.Add(memberEntity);
            await context.SaveChangesAsync();
        }

        await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, captain.Id);
        await TestDataSeeder.JoinGameAsync(factory.Services, game.Id, team.Id, member.Id);

        using var captainClient = factory.CreateClient();
        var captainLogin = await captainClient.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = captain.UserName, Password = captainPassword });
        captainLogin.EnsureSuccessStatusCode();

        var kickResponse = await captainClient.PostAsync($"/api/Team/{team.Id}/Kick/{member.Id}", null);
        kickResponse.EnsureSuccessStatusCode();

        using var verifyScope = factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participations = await verifyContext.UserParticipations
            .Where(p => p.GameId == game.Id && p.TeamId == team.Id)
            .Select(p => p.UserId)
            .ToArrayAsync();

        Assert.Contains(captain.Id, participations);
        Assert.DoesNotContain(member.Id, participations);
    }
}
