using System.Net;
using System.Net.Http.Json;
using GZCTF.Integration.Test.Base;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Exercise;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Api;

[Collection(nameof(IntegrationTestCollection))]
public sealed class ExerciseCatalogPaginationTests(GZCTFApplicationFactory factory)
{
    private const string Password = "Pagination@Test123";

    [Fact]
    public async Task Pages_AreBoundedOrderedFilteredAndKeepVisibilityAndWholeCatalogCounts()
    {
        var fixture = await SeedAsync();
        using var student = await LoginAsync(fixture.Student);
        using var admin = await LoginAsync(fixture.Admin);
        var search = Uri.EscapeDataString(fixture.Prefix);
        var first = (await student.GetFromJsonAsync<ExerciseCatalogPageModel>($"/api/exercise/page?Search={search}"))!;
        var second = (await student.GetFromJsonAsync<ExerciseCatalogPageModel>($"/api/exercise/page?Search={search}&Page=2"))!;
        Assert.Equal(25, first.Total);
        Assert.Equal(20, first.Items.Length);
        Assert.Equal(5, second.Items.Length);
        Assert.Equal(fixture.PublicIds.Take(20), first.Items.Select(item => item.Id));
        Assert.Equal(fixture.PublicIds.Skip(20), second.Items.Select(item => item.Id));
        Assert.All(first.Items.Concat(second.Items), item => Assert.True(item.IsEnabled));
        Assert.Contains(fixture.Prefix + "last-page-tag", first.Tags);
        Assert.DoesNotContain(fixture.Prefix + "course-secret-tag", first.Tags);
        Assert.DoesNotContain(fixture.Prefix + "teacher-only-tag", first.Tags);
        Assert.DoesNotContain(fixture.Prefix + "disabled-tag", first.Tags);

        var managed = (await admin.GetFromJsonAsync<ExerciseCatalogPageModel>(
            $"/api/exercise/manage/page?Search={search}&PageSize=20"))!;
        Assert.Equal(27, managed.Total);
        Assert.Equal(26, managed.EnabledCount);
        Assert.Equal(20, managed.Items.Length);
        Assert.Contains(fixture.Prefix + "disabled-tag", managed.Tags);
        Assert.DoesNotContain(fixture.Prefix + "course-secret-tag", managed.Tags);
        var tail = (await admin.GetFromJsonAsync<ExerciseCatalogPageModel>(
            $"/api/exercise/manage/page?Search={search}&Page=1000"))!;
        Assert.Equal(2, tail.Page);
        Assert.Equal(7, tail.Items.Length);
        Assert.Contains(tail.Items, item => !item.IsEnabled);
        using var forbidden = await student.GetAsync("/api/exercise/manage/page");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task StatusAndTags_AreAppliedBeforePagingAndUseCurrentUsersCompletion()
    {
        var fixture = await SeedAsync();
        using var student = await LoginAsync(fixture.Student);
        var search = Uri.EscapeDataString(fixture.Prefix);
        var solved = (await student.GetFromJsonAsync<ExerciseCatalogPageModel>(
            $"/api/exercise/page?Search={search}&Solved=true&PageSize=1"))!;
        Assert.Equal(1, solved.Total);
        Assert.Equal(fixture.PublicIds[24], Assert.Single(solved.Items).Id);
        Assert.True(solved.Items[0].Solved);
        var unsolved = (await student.GetFromJsonAsync<ExerciseCatalogPageModel>(
            $"/api/exercise/page?Search={search}&Solved=false&Tags={Uri.EscapeDataString(fixture.Prefix + "last-page-tag")}"))!;
        Assert.Equal(0, unsolved.Total);
        Assert.Empty(unsolved.Items);
        var byTag = (await student.GetFromJsonAsync<ExerciseCatalogPageModel>(
            $"/api/exercise/page?Search={search}&Tags={Uri.EscapeDataString(fixture.Prefix + "last-page-tag")}"))!;
        Assert.Equal(fixture.PublicIds[24], Assert.Single(byTag.Items).Id);
        var byId = (await student.GetFromJsonAsync<ExerciseCatalogPageModel>($"/api/exercise/page?Search=%23{fixture.PublicIds[23]}"))!;
        Assert.Equal(fixture.PublicIds[23], Assert.Single(byId.Items).Id);
    }

    [Theory]
    [InlineData("Page=0")]
    [InlineData("Page=-1")]
    [InlineData("Page=1000001")]
    [InlineData("PageSize=0")]
    [InlineData("PageSize=101")]
    [InlineData("PageSize=1.5")]
    public async Task InvalidPaging_IsRejectedForBothRoutes(string query)
    {
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), Password, role: Role.Admin);
        using var client = await LoginAsync(admin);
        foreach (var route in new[] { "/api/exercise/page", "/api/exercise/manage/page" })
        {
            using var response = await client.GetAsync(route + "?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    private async Task<HttpClient> LoginAsync(TestDataSeeder.SeededUser user)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/Account/LogIn",
            new LoginModel { UserName = user.UserName, Password = Password });
        response.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<Fixture> SeedAsync()
    {
        var student = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), Password, role: Role.Student);
        var admin = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), Password, role: Role.Admin);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prefix = "catalog-" + Guid.NewGuid().ToString("N") + "-";
        var course = new TrainingCourse { Title = prefix, CreatedById = admin.Id };
        context.Add(course);
        await context.SaveChangesAsync();
        var exercises = Enumerable.Range(0, 25).Select(index => new ExerciseChallenge
        {
            Title = prefix + index, Content = "pagination fixture", IsEnabled = true,
            MinimumVisibleRole = Role.Student, Tags = index == 24 ? [prefix + "last-page-tag"] : []
        }).ToArray();
        context.AddRange(exercises);
        context.AddRange(
            new ExerciseChallenge { Title = prefix + "disabled", Content = "", IsEnabled = false, Tags = [prefix + "disabled-tag"] },
            new ExerciseChallenge { Title = prefix + "teacher", Content = "", IsEnabled = true,
                MinimumVisibleRole = Role.Teacher, Tags = [prefix + "teacher-only-tag"] },
            new ExerciseChallenge { Title = prefix + "course", Content = "", IsEnabled = true,
                TrainingCourseId = course.Id, Tags = [prefix + "course-secret-tag"] });
        await context.SaveChangesAsync();
        context.AddRange(
            new ExerciseInstance { UserId = student.Id, ExerciseId = exercises[24].Id, SolveTimeUtc = DateTimeOffset.UtcNow },
            new ExerciseInstance { UserId = admin.Id, ExerciseId = exercises[23].Id, SolveTimeUtc = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        return new Fixture(student, admin, prefix, exercises.Select(item => item.Id).Order().ToArray());
    }

    private sealed record Fixture(TestDataSeeder.SeededUser Student, TestDataSeeder.SeededUser Admin,
        string Prefix, int[] PublicIds);
}
