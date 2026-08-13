using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ExerciseWorkflowTests(GZCTFApplicationFactory factory)
{
    private const string Password = "Exercise@Test123";

    [Fact]
    public async Task Student_CanBrowseSolveAndQueueExerciseContainer()
    {
        var user = await TestDataSeeder.CreateUserAsync(
            factory.Services, TestDataSeeder.RandomName(), Password, role: Role.Student);
        var fixture = await CreateExercisesAsync();
        using var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = Password })).EnsureSuccessStatusCode();

        using var list = await client.GetAsync("/api/exercise");
        list.EnsureSuccessStatusCode();
        using var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Contains(listBody.RootElement.EnumerateArray(), item =>
            item.GetProperty("id").GetInt32() == fixture.StaticExerciseId);

        using var detail = await client.GetAsync($"/api/exercise/{fixture.StaticExerciseId}");
        detail.EnsureSuccessStatusCode();
        var detailJson = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(detailJson.GetProperty("solved").GetBoolean());
        Assert.Equal(fixture.StaticFlagId, detailJson.GetProperty("flags")[0].GetProperty("id").GetInt32());
        Assert.DoesNotContain(fixture.StaticFlag, await detail.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var submission = await client.PostAsJsonAsync($"/api/exercise/{fixture.StaticExerciseId}/flag",
            new { flag = fixture.StaticFlag, flagId = fixture.StaticFlagId });
        submission.EnsureSuccessStatusCode();
        var submissionJson = await submission.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Accepted", submissionJson.GetProperty("status").GetString());

        var solved = await client.GetFromJsonAsync<JsonElement>($"/api/exercise/{fixture.StaticExerciseId}");
        Assert.True(solved.GetProperty("solved").GetBoolean());

        using var create = await client.PostAsync($"/api/exercise/{fixture.ContainerExerciseId}/container", null);
        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        var createJson = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("queued", createJson.GetProperty("status").GetString());

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await context.DeploymentQueueTickets.AnyAsync(ticket =>
            ticket.Kind == DeploymentQueueKind.ExerciseContainer &&
            ticket.OwnerUserId == user.Id &&
            ticket.ChallengeId == fixture.ContainerExerciseId));
    }

    private async Task<ExerciseFixture> CreateExercisesAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staticExercise = new ExerciseChallenge
        {
            Title = $"Static exercise {Guid.NewGuid():N}",
            Content = "Submit the fixture flag.",
            Category = ChallengeCategory.Web,
            Difficulty = Difficulty.Easy,
            Type = ChallengeType.StaticAttachment,
            IsEnabled = true,
            MinimumVisibleRole = Role.Student
        };
        var staticFlag = new FlagContext
        {
            Exercise = staticExercise,
            Flag = $"flag{{exercise-{Guid.NewGuid():N}}}"
        };
        staticExercise.Flags.Add(staticFlag);

        var containerExercise = new ExerciseChallenge
        {
            Title = $"Container exercise {Guid.NewGuid():N}",
            Content = "Queue a disposable runtime.",
            Category = ChallengeCategory.Web,
            Difficulty = Difficulty.Easy,
            Type = ChallengeType.StaticContainer,
            IsEnabled = true,
            MinimumVisibleRole = Role.Student,
            ContainerImage = "example.invalid/gzctf/exercise-smoke:latest",
            ExposePort = 80
        };
        containerExercise.Flags.Add(new FlagContext
        {
            Exercise = containerExercise,
            Flag = "flag{container-smoke}"
        });

        context.ExerciseChallenges.AddRange(staticExercise, containerExercise);
        await context.SaveChangesAsync();
        return new ExerciseFixture(
            staticExercise.Id,
            staticFlag.Id,
            staticFlag.Flag,
            containerExercise.Id);
    }

    private sealed record ExerciseFixture(
        int StaticExerciseId,
        int StaticFlagId,
        string StaticFlag,
        int ContainerExerciseId);
}
