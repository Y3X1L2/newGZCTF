using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Info;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public class UserProfileTests(GZCTFApplicationFactory factory)
{
    private static readonly JsonSerializerOptions ApiJsonOptions = CreateApiJsonOptions();

    [Fact]
    public async Task PublicProfile_UsesPersonalFacts_AndRedactsSensitiveData()
    {
        var fixture = await CreateMixedProfileFixture();
        using var client = factory.CreateClient();

        var profileResponse = await client.GetAsync($"/api/users/{fixture.User.Id}");
        profileResponse.EnsureSuccessStatusCode();
        var rawProfile = await profileResponse.Content.ReadAsStringAsync();
        using var profileDocument = JsonDocument.Parse(rawProfile);
        var profileRoot = profileDocument.RootElement;

        Assert.Equal(fixture.User.Id, profileRoot.GetProperty("id").GetGuid());
        Assert.Equal(fixture.User.UserName, profileRoot.GetProperty("userName").GetString());
        Assert.Equal("Public profile bio", profileRoot.GetProperty("bio").GetString());
        Assert.Equal(JsonValueKind.Number, profileRoot.GetProperty("registeredAt").ValueKind);
        Assert.Equal(JsonValueKind.Object, profileRoot.GetProperty("publicTeam").ValueKind);
        Assert.DoesNotContain("email", rawProfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("realName", rawProfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stdNumber", rawProfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lastSignedIn", rawProfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"ip\"", rawProfile, StringComparison.OrdinalIgnoreCase);

        var overview = await client.GetFromJsonAsync<UserProfileOverviewModel>(
            $"/api/users/{fixture.User.Id}/overview?window=365d", ApiJsonOptions);
        Assert.NotNull(overview);
        Assert.Equal(3, overview.Metrics.Solved);
        Assert.Equal(4, overview.Metrics.Submissions);
        Assert.Equal(3, overview.Metrics.AcceptedSubmissions);
        Assert.Equal(75, overview.Metrics.SuccessRate);
        Assert.Equal(1, overview.Metrics.GameCount);
        Assert.Equal(1, overview.Dimensions.Single(item => item.Id == "web").Solved);
        Assert.Equal(0, overview.Dimensions.Single(item => item.Id == "pwn").Solved);
        Assert.Equal(1, overview.Dimensions.Single(item => item.Id == "reverse").Solved);
        Assert.Equal(1, overview.Dimensions.Single(item => item.Id == "crypto").Solved);

        var history = await client.GetFromJsonAsync<UserProfileHistoryPageModel>(
            $"/api/users/{fixture.User.Id}/history?type=challenges", ApiJsonOptions);
        Assert.Equal(2, history!.Items.Count);
        Assert.Contains(history.Items,
            item => int.Parse(item.Id.Split(':')[1]) == fixture.VisibleChallenge.Id);
        Assert.Contains(history.Items,
            item => int.Parse(item.Id.Split(':')[1]) == fixture.SecondVisibleChallenge.Id);
        Assert.DoesNotContain(fixture.HiddenChallenge.Title, history.Items.Select(item => item.Title));
        Assert.DoesNotContain(fixture.ActiveChallenge.Title, history.Items.Select(item => item.Title));

        var firstPage = await client.GetFromJsonAsync<UserProfileHistoryPageModel>(
            $"/api/users/{fixture.User.Id}/history?type=challenges&count=1", ApiJsonOptions);
        Assert.NotNull(firstPage?.NextCursor);
        var secondPage = await client.GetFromJsonAsync<UserProfileHistoryPageModel>(
            $"/api/users/{fixture.User.Id}/history?type=challenges&count=1&cursor={firstPage.NextCursor}",
            ApiJsonOptions);
        Assert.Single(secondPage!.Items);
        Assert.NotEqual(firstPage.Items[0].Id, secondPage.Items[0].Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var activity = await client.GetFromJsonAsync<List<UserActivityPointModel>>(
            $"/api/users/{fixture.User.Id}/activity?from={today.AddDays(-10):yyyy-MM-dd}&to={today:yyyy-MM-dd}",
            ApiJsonOptions);
        var activityPoints = Assert.IsType<List<UserActivityPointModel>>(activity);
        Assert.Equal(3, activityPoints.Sum(item => item.Ctf));
        Assert.Equal(1, activityPoints.Sum(item => item.Training));
    }

    [Fact]
    public async Task PublicProfile_SupportsConditionalRequests_AndValidatesRanges()
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), "Profile@Test123");
        using var client = factory.CreateClient();

        var first = await client.GetAsync($"/api/users/{user.Id}");
        first.EnsureSuccessStatusCode();
        Assert.True(first.Headers.TryGetValues("ETag", out var values));

        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/users/{user.Id}");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", values.Single());
        var second = await client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);

        var invalidWindow = await client.GetAsync($"/api/users/{user.Id}/overview?window=7d");
        Assert.Equal(HttpStatusCode.BadRequest, invalidWindow.StatusCode);
        var invalidRange = await client.GetAsync(
            $"/api/users/{user.Id}/activity?from=2024-01-01&to=2026-01-01");
        Assert.Equal(HttpStatusCode.BadRequest, invalidRange.StatusCode);
    }

    [Fact]
    public async Task PrivateOverview_RequiresMatchingAuthenticatedUser()
    {
        var password = "Profile@Test123";
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), password);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/users/me/private-overview")).StatusCode);

        using var authenticated = factory.CreateClient();
        (await authenticated.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = password })).EnsureSuccessStatusCode();
        (await authenticated.GetAsync("/api/users/me/private-overview")).EnsureSuccessStatusCode();
        var summary = await authenticated.GetFromJsonAsync<AccountSummaryModel>(
            "/api/Account/Summary", ApiJsonOptions);
        Assert.NotNull(summary);
        Assert.Equal(user.Id, summary.Id);
        Assert.Equal(user.UserName, summary.UserName);
    }

    private async Task<ProfileFixture> CreateMixedProfileFixture()
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), "Profile@Test123", $"profile-{Guid.NewGuid():N}@example.com");
        var teammate = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), "Profile@Test123");
        var team = await TestDataSeeder.CreateTeamAsync(factory.Services, user.Id, $"profile-{Guid.NewGuid():N}"[..20]);
        var now = DateTimeOffset.UtcNow;
        var visibleGame = await TestDataSeeder.CreateGameAsync(factory.Services, $"Visible {Guid.NewGuid():N}",
            now.AddDays(-4), now.AddDays(-2));
        var hiddenGame = await TestDataSeeder.CreateGameAsync(factory.Services, $"Hidden {Guid.NewGuid():N}",
            now.AddDays(-4), now.AddDays(-2));
        var activeGame = await TestDataSeeder.CreateGameAsync(factory.Services, $"Active {Guid.NewGuid():N}",
            now.AddHours(-1), now.AddHours(2));
        var visibleChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, visibleGame.Id,
            $"Visible Web {Guid.NewGuid():N}", "flag{visible}");
        var secondVisibleChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, visibleGame.Id,
            $"Visible Reverse {Guid.NewGuid():N}", "flag{visible-reverse}");
        var teammateChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, visibleGame.Id,
            $"Teammate Pwn {Guid.NewGuid():N}", "flag{teammate}");
        var hiddenChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, hiddenGame.Id,
            $"Hidden {Guid.NewGuid():N}", "flag{hidden}");
        var activeChallenge = await TestDataSeeder.CreateStaticChallengeAsync(factory.Services, activeGame.Id,
            $"Active {Guid.NewGuid():N}", "flag{active}");

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userEntity = await context.Users.SingleAsync(item => item.Id == user.Id);
        var teammateEntity = await context.Users.SingleAsync(item => item.Id == teammate.Id);
        userEntity.Bio = "Public profile bio";
        userEntity.RealName = "Private Real Name";
        userEntity.StdNumber = "PRIVATE-001";
        var teamEntity = await context.Teams.Include(item => item.Members).SingleAsync(item => item.Id == team.Id);
        teamEntity.Members.Add(teammateEntity);
        (await context.Games.SingleAsync(item => item.Id == hiddenGame.Id)).Hidden = true;
        (await context.GameChallenges.SingleAsync(item => item.Id == visibleChallenge.Id)).Category = ChallengeCategory.Web;
        (await context.GameChallenges.SingleAsync(item => item.Id == secondVisibleChallenge.Id)).Category =
            ChallengeCategory.Reverse;
        (await context.GameChallenges.SingleAsync(item => item.Id == teammateChallenge.Id)).Category = ChallengeCategory.Pwn;

        var course = new TrainingCourse
        {
            Title = $"Published profile course {Guid.NewGuid():N}",
            Slug = $"profile-{Guid.NewGuid():N}",
            Status = TrainingCourseStatus.Published,
            PublishedAt = now.AddDays(-6),
            CreatedById = user.Id,
            UpdatedById = user.Id
        };
        context.TrainingCourses.Add(course);
        await context.SaveChangesAsync();
        var trainingChallenge = new ExerciseChallenge
        {
            Title = $"Training Crypto {Guid.NewGuid():N}",
            Content = "Profile training fixture",
            Category = ChallengeCategory.Crypto,
            IsEnabled = true,
            TrainingCourseId = course.Id
        };
        context.ExerciseChallenges.Add(trainingChallenge);
        await context.SaveChangesAsync();

        var visibleParticipation = Participation(visibleGame.Id, team.Id, userEntity, teammateEntity);
        var hiddenParticipation = Participation(hiddenGame.Id, team.Id, userEntity);
        var activeParticipation = Participation(activeGame.Id, team.Id, userEntity);
        context.Participations.AddRange(visibleParticipation, hiddenParticipation, activeParticipation);
        await context.SaveChangesAsync();

        context.Submissions.AddRange(
            Submission(visibleGame.Id, visibleChallenge.Id, visibleParticipation.Id, team.Id, user.Id,
                AnswerResult.WrongAnswer, now.AddDays(-3).AddMinutes(-5)),
            Submission(visibleGame.Id, visibleChallenge.Id, visibleParticipation.Id, team.Id, user.Id,
                AnswerResult.Accepted, now.AddDays(-3)),
            Submission(visibleGame.Id, secondVisibleChallenge.Id, visibleParticipation.Id, team.Id, user.Id,
                AnswerResult.Accepted, now.AddDays(-3).AddMinutes(2)),
            Submission(visibleGame.Id, teammateChallenge.Id, visibleParticipation.Id, team.Id, teammate.Id,
                AnswerResult.Accepted, now.AddDays(-3)),
            Submission(hiddenGame.Id, hiddenChallenge.Id, hiddenParticipation.Id, team.Id, user.Id,
                AnswerResult.Accepted, now.AddDays(-3)),
            Submission(activeGame.Id, activeChallenge.Id, activeParticipation.Id, team.Id, user.Id,
                AnswerResult.Accepted, now.AddMinutes(-30)));
        context.TrainingCourseSubmissions.Add(new TrainingCourseSubmission
        {
            CourseId = course.Id,
            ExerciseChallengeId = trainingChallenge.Id,
            UserId = user.Id,
            Status = AnswerResult.Accepted,
            SubmittedAt = now.AddDays(-2),
            SubmittedAnswerHash = "profile-test-hash",
            IpAddress = "127.0.0.1"
        });
        await context.SaveChangesAsync();

        return new ProfileFixture(user, visibleChallenge, secondVisibleChallenge, hiddenChallenge, activeChallenge);
    }

    private static Participation Participation(int gameId, int teamId, params UserInfo[] users)
    {
        var participation = new Participation
        {
            GameId = gameId,
            TeamId = teamId,
            Status = ParticipationStatus.Accepted
        };
        foreach (var user in users)
        {
            participation.Members.Add(new UserParticipation
            {
                GameId = gameId,
                TeamId = teamId,
                UserId = user.Id,
                User = user
            });
        }
        return participation;
    }

    private static Submission Submission(int gameId, int challengeId, int participationId, int teamId,
        Guid userId, AnswerResult status, DateTimeOffset submittedAt) => new()
    {
        GameId = gameId,
        ChallengeId = challengeId,
        ParticipationId = participationId,
        TeamId = teamId,
        UserId = userId,
        Answer = "redacted-test-answer",
        Status = status,
        SubmitTimeUtc = submittedAt
    };

    private sealed record ProfileFixture(TestDataSeeder.SeededUser User,
        TestDataSeeder.SeededChallenge VisibleChallenge,
        TestDataSeeder.SeededChallenge SecondVisibleChallenge,
        TestDataSeeder.SeededChallenge HiddenChallenge,
        TestDataSeeder.SeededChallenge ActiveChallenge);

    private static JsonSerializerOptions CreateApiJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new DateTimeOffsetJsonConverter());
        return options;
    }
}
