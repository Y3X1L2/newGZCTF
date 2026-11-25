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
public sealed class TrainingChapterCompletionTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task FirstReadingConfirmation_PersistsChapterAndCourseCompletion()
    {
        const string password = "Training@Test123";
        var learner = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), password, role: Role.Student);
        await using var seedScope = factory.Services.CreateAsyncScope();
        var seed = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new TrainingCourse
        {
            Title = "Reading confirmation regression",
            CreatedById = learner.Id,
            Status = TrainingCourseStatus.Published
        };
        var chapter = new TrainingCourseChapter { Title = "Reading chapter", Course = course };
        seed.Add(chapter);
        seed.Add(new TrainingCourseEnrollment
        {
            Course = course,
            UserId = learner.Id,
            Status = TrainingCourseEnrollmentStatus.Approved
        });
        await seed.SaveChangesAsync();

        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/account/login",
            new LoginModel { UserName = learner.UserName, Password = password });
        login.EnsureSuccessStatusCode();
        var coursePath = $"/api/training/courses/{course.Id}";
        var chapterPath = $"{coursePath}/chapters/{chapter.Id}";
        using var before = await client.GetFromJsonAsync<JsonDocument>(chapterPath);
        Assert.Equal(0, before!.RootElement.GetProperty("readPercent").GetInt32());

        using var response = await client.PostAsync($"{chapterPath}/complete", null);
        response.EnsureSuccessStatusCode();
        using var updated = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(100, updated!.RootElement.GetProperty("readPercent").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, updated.RootElement.GetProperty("completedAt").ValueKind);

        using var summary = await client.GetFromJsonAsync<JsonDocument>(coursePath);
        Assert.Equal(1, summary!.RootElement.GetProperty("completedChapterCount").GetInt32());

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var progress = await verify.TrainingChapterProgresses.SingleAsync(p =>
            p.ChapterId == chapter.Id && p.UserId == learner.Id);
        Assert.Equal(TrainingCourseProgressStatus.Completed, progress.Status);
        var courseProgress = await verify.TrainingCourseProgresses.SingleAsync(p =>
            p.CourseId == course.Id && p.UserId == learner.Id);
        Assert.Equal(TrainingCourseProgressStatus.Completed, courseProgress.Status);
        Assert.Equal(1, courseProgress.CompletedChapterCount);
    }

    [Fact]
    public async Task CourseDetail_UsesChapterProgressAndSavedChallengeBindings()
    {
        const string password = "Training@Test123";
        var learner = await TestDataSeeder.CreateUserAsync(factory.Services,
            TestDataSeeder.RandomName(), password, role: Role.Student);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = new TrainingCourse { Title = "Course detail regression", Status = TrainingCourseStatus.Published };
        var completed = new TrainingCourseChapter { Course = course, Title = "Completed chapter" };
        var remaining = new TrainingCourseChapter { Course = course, Title = "Remaining chapter" };
        var bound = new ExerciseChallenge { TrainingCourse = course, Title = "Bound challenge" };
        var unbound = new ExerciseChallenge { TrainingCourse = course, Title = "Unbound challenge" };
        db.AddRange(completed, remaining, bound, unbound);
        await db.SaveChangesAsync();

        db.AddRange(
            new TrainingCourseEnrollment
                { CourseId = course.Id, UserId = learner.Id, Status = TrainingCourseEnrollmentStatus.Approved },
            new TrainingCourseProgress { CourseId = course.Id, UserId = learner.Id, TotalChapterCount = 9 },
            new TrainingChapterProgress
            {
                CourseId = course.Id, ChapterId = completed.Id, UserId = learner.Id,
                Status = TrainingCourseProgressStatus.Completed, ReadPercent = 100, CompletedAt = DateTimeOffset.UtcNow
            },
            new TrainingCourseChallenge { CourseId = course.Id, ExerciseChallengeId = bound.Id },
            new TrainingCourseChallenge { CourseId = course.Id, ExerciseChallengeId = unbound.Id },
            new TrainingCourseChapterChallenge
                { CourseId = course.Id, ChapterId = completed.Id, ExerciseChallengeId = bound.Id });
        await db.SaveChangesAsync();

        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/account/login",
            new LoginModel { UserName = learner.UserName, Password = password });
        login.EnsureSuccessStatusCode();

        using var result = await client.GetFromJsonAsync<JsonDocument>($"/api/training/courses/{course.Id}");
        var detail = result!.RootElement;
        Assert.Equal(1, detail.GetProperty("completedChapterCount").GetInt32());
        Assert.Equal(2, detail.GetProperty("totalChapterCount").GetInt32());
        Assert.Equal("Learning", detail.GetProperty("progressStatus").GetString());
        var chapter = detail.GetProperty("chapters").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == completed.Id);
        Assert.Equal("Completed", chapter.GetProperty("progressStatus").GetString());
        Assert.Equal(100, chapter.GetProperty("readPercent").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, chapter.GetProperty("completedAt").ValueKind);
        Assert.Equal(bound.Id, chapter.GetProperty("challenges")[0].GetProperty("exerciseChallengeId").GetInt32());
        var challenges = detail.GetProperty("challenges").EnumerateArray()
            .ToDictionary(item => item.GetProperty("exerciseChallengeId").GetInt32());
        Assert.Equal(completed.Id, challenges[bound.Id].GetProperty("chapterId").GetInt32());
        Assert.True(!challenges[unbound.Id].TryGetProperty("chapterId", out var unboundChapter) ||
            unboundChapter.ValueKind == JsonValueKind.Null);
    }
}
