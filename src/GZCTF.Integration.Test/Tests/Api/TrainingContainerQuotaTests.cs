using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Admin;
using GZCTF.Models.Request.Account;
using GZCTF.Services.Fleet;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class TrainingContainerQuotaTests(GZCTFApplicationFactory factory)
{
    private const string Password = "Training@Test123";

    [Fact]
    public async Task AdminQuotaSave_AppliesToNextCreateWithoutRestartAndPreservesOtherPoliciesAndTickets()
    {
        var fixture = await CreateFixtureAsync();
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), Password, role: Role.Admin);
        using var adminClient = await LoginAsync(admin);
        using var learnerClient = await LoginAsync(fixture.User);
        var before = (await adminClient.GetFromJsonAsync<ConfigEditModel>("/api/Admin/Config"))!;
        try
        {
            await SaveLimit(1);
            // No intervening GET/reload: PUT alone must change the next request's options snapshot.
            using var first = await learnerClient.PostAsync(Route(fixture, fixture.TrainingIds[0]), null);
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            using var denied = await learnerClient.PostAsync(Route(fixture, fixture.TrainingIds[1]), null);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);

            await SaveLimit(2);
            using var second = await learnerClient.PostAsync(Route(fixture, fixture.TrainingIds[1]), null);
            Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
            await SaveLimit(1);
            using var thirdDenied = await learnerClient.PostAsync(Route(fixture, fixture.TrainingIds[2]), null);
            Assert.Equal(HttpStatusCode.BadRequest, thirdDenied.StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tickets = await context.DeploymentQueueTickets.Where(ticket =>
                    ticket.OwnerUserId == fixture.User.Id).ToListAsync();
                Assert.Equal(2, tickets.Count);
                Assert.All(tickets, ticket => Assert.NotEqual(DeploymentQueueTicketStatus.Cancelled, ticket.Status));
                Assert.Equal("1", (await context.Configs.SingleAsync(config =>
                    config.ConfigKey == "TrainingContainerPolicy:MaxContainerCountPerUser")).Value);
            }

            await SaveLimit(0);
            foreach (var id in fixture.TrainingIds.Skip(2))
            {
                using var response = await learnerClient.PostAsync(Route(fixture, id), null);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }
            var after = (await adminClient.GetFromJsonAsync<ConfigEditModel>("/api/Admin/Config"))!;
            Assert.Equal(0, after.TrainingContainerPolicy!.MaxContainerCountPerUser);
            Assert.Equal(JsonSerializer.Serialize(before.ContainerPolicy), JsonSerializer.Serialize(after.ContainerPolicy));
            Assert.Equal(JsonSerializer.Serialize(before.AccountPolicy), JsonSerializer.Serialize(after.AccountPolicy));
            Assert.Equal(JsonSerializer.Serialize(before.GlobalConfig), JsonSerializer.Serialize(after.GlobalConfig));
            using var publicFirst = await learnerClient.PostAsync($"/api/exercise/{fixture.PublicIds[0]}/container", null);
            Assert.Equal(HttpStatusCode.Accepted, publicFirst.StatusCode);
            using var publicSecond = await learnerClient.PostAsync($"/api/exercise/{fixture.PublicIds[1]}/container", null);
            Assert.Equal(HttpStatusCode.BadRequest, publicSecond.StatusCode);
        }
        finally
        {
            await SaveLimit(before.TrainingContainerPolicy!.MaxContainerCountPerUser);
        }

        async Task SaveLimit(int limit)
        {
            using var response = await adminClient.PutAsJsonAsync("/api/Admin/Config", new
                { trainingContainerPolicy = new { maxContainerCountPerUser = limit } });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task AdminQuotaSave_InvalidNumberIsRejectedWithoutChangingAnySection(string value)
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), Password, role: Role.Admin);
        using var client = await LoginAsync(admin);
        var before = await client.GetStringAsync("/api/Admin/Config");
        using var body = new StringContent("{\"globalConfig\":{\"title\":\"must not persist\"}," +
            "\"trainingContainerPolicy\":{\"maxContainerCountPerUser\":" + value + "}}",
            System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PutAsync("/api/Admin/Config", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await client.GetStringAsync("/api/Admin/Config"));
    }

    [Theory]
    [InlineData(Role.Student)]
    [InlineData(Role.Teacher)]
    public async Task TrainingQuotaConfig_RequiresAdminForReadAndWrite(Role role)
    {
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), Password, role: role);
        using var client = await LoginAsync(user);
        using var read = await client.GetAsync("/api/Admin/Config");
        using var write = await client.PutAsJsonAsync("/api/Admin/Config", new
            { trainingContainerPolicy = new { maxContainerCountPerUser = 0 } });
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task StudentWithoutCourseEnrollment_CannotCreateCourseRuntime()
    {
        var fixture = await CreateFixtureAsync();
        var student = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), Password, role: Role.Student);
        using var client = await LoginAsync(student);
        using var response = await client.PostAsync(Route(fixture, fixture.TrainingIds[0]), null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await context.DeploymentQueueTickets.AnyAsync(ticket => ticket.OwnerUserId == student.Id));
        Assert.False(await context.ExerciseInstances.AnyAsync(instance => instance.UserId == student.Id));
    }

    [Fact]
    public async Task ConcurrentChapterCreates_AdmitThreeAndRejectFourthWithoutReplacingTickets()
    {
        var fixture = await CreateFixtureAsync();
        using var client = await LoginAsync(fixture.User);
        var responses = await Task.WhenAll(fixture.TrainingIds.Select(id =>
            client.PostAsync(Route(fixture, id), null)));
        try
        {
            Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
            var acceptedIndex = Array.FindIndex(responses, response => response.StatusCode == HttpStatusCode.Accepted);
            var admitted = await responses[acceptedIndex].Content.ReadFromJsonAsync<JsonElement>();
            using var repeat = await client.PostAsync(Route(fixture, fixture.TrainingIds[acceptedIndex]), null);
            Assert.Equal(HttpStatusCode.Accepted, repeat.StatusCode);
            var repeated = await repeat.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(admitted.GetProperty("queue").GetProperty("ticketId").GetGuid(),
                repeated.GetProperty("queue").GetProperty("ticketId").GetGuid());

            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tickets = await context.DeploymentQueueTickets.Where(ticket =>
                ticket.Kind == DeploymentQueueKind.TrainingContainer && ticket.OwnerUserId == fixture.User.Id)
                .ToListAsync();
            Assert.Equal(3, tickets.Count);
            Assert.Equal(3, tickets.Select(ticket => ticket.ChallengeId).Distinct().Count());
            Assert.All(tickets, ticket => Assert.Contains(ticket.Status,
                new[] { DeploymentQueueTicketStatus.Pending, DeploymentQueueTicketStatus.Scheduling,
                    DeploymentQueueTicketStatus.Scheduled, DeploymentQueueTicketStatus.Running }));
            Assert.False(await context.ExerciseInstances.AnyAsync(instance =>
                instance.UserId == fixture.User.Id && instance.ContainerId != null));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task PublicAndTrainingQueueAdmission_KeepIndependentLimits()
    {
        var fixture = await CreateFixtureAsync();
        using var client = await LoginAsync(fixture.User);
        using var firstPublic = await client.PostAsync($"/api/exercise/{fixture.PublicIds[0]}/container", null);
        Assert.Equal(HttpStatusCode.Accepted, firstPublic.StatusCode);
        foreach (var id in fixture.TrainingIds.Take(3))
        {
            using var response = await client.PostAsync(Route(fixture, id), null);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        using var secondPublic = await client.PostAsync($"/api/exercise/{fixture.PublicIds[1]}/container", null);
        Assert.Equal(HttpStatusCode.BadRequest, secondPublic.StatusCode);
        using var fourthTraining = await client.PostAsync(Route(fixture, fixture.TrainingIds[3]), null);
        Assert.Equal(HttpStatusCode.BadRequest, fourthTraining.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await context.DeploymentQueueTickets.CountAsync(ticket =>
            ticket.OwnerUserId == fixture.User.Id && ticket.Kind == DeploymentQueueKind.ExerciseContainer));
        Assert.Equal(3, await context.DeploymentQueueTickets.CountAsync(ticket =>
            ticket.OwnerUserId == fixture.User.Id && ticket.Kind == DeploymentQueueKind.TrainingContainer));
    }

    private async Task<HttpClient> LoginAsync(TestDataSeeder.SeededUser user)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = Password });
        response.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<Fixture> CreateFixtureAsync()
    {
        await ContainerHelper.SetLocalNodeSchedulingAsync(factory.Services, false);
        var user = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), Password, role: Role.Teacher);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new TrainingCourse { Title = "training quota regression", CreatedById = user.Id };
        var chapter = new TrainingCourseChapter { Title = "four concurrent labs", Course = course };
        context.Add(chapter);
        await context.SaveChangesAsync();
        context.Add(new TrainingCourseTeacher
            { CourseId = course.Id, TeacherId = user.Id, Role = TrainingCourseTeacherRole.Owner });
        ExerciseChallenge MakeExercise(int? courseId) => new()
        {
            Title = "quota fixture " + Guid.NewGuid().ToString("N"), Content = "platform lifecycle regression",
            TrainingCourseId = courseId, IsEnabled = true, MinimumVisibleRole = Role.Student,
            Type = ChallengeType.DynamicContainer, ContainerImage = "example.test/quota:latest", ExposePort = 80
        };
        var training = Enumerable.Range(0, 4).Select(_ => MakeExercise(course.Id)).ToArray();
        var publicExercises = Enumerable.Range(0, 2).Select(_ => MakeExercise(null)).ToArray();
        context.AddRange(training.Concat(publicExercises));
        await context.SaveChangesAsync();
        foreach (var exercise in training)
        {
            context.Add(new TrainingCourseChallenge { CourseId = course.Id, ExerciseChallengeId = exercise.Id });
            context.Add(new TrainingCourseChapterChallenge
                { CourseId = course.Id, ChapterId = chapter.Id, ExerciseChallengeId = exercise.Id });
        }
        await context.SaveChangesAsync();
        return new Fixture(user, course.Id, chapter.Id, training.Select(item => item.Id).ToArray(),
            publicExercises.Select(item => item.Id).ToArray());
    }

    private static string Route(Fixture fixture, int id) =>
        $"/api/training/courses/{fixture.CourseId}/challenges/{id}/container?chapterId={fixture.ChapterId}";

    private sealed record Fixture(TestDataSeeder.SeededUser User, int CourseId, int ChapterId, int[] TrainingIds, int[] PublicIds);
}
