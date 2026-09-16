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
public sealed class TrainingCourseReadRegressionTests(GZCTFApplicationFactory factory)
{
    [Fact]
    public async Task CourseDetail_ReturnsCallerProgressAndAllVisibleBindings()
    {
        var learner = await CreateUserAsync(Role.Student);
        var other = await CreateUserAsync(Role.Student);
        var admin = await CreateUserAsync(Role.Admin);
        var outsider = await CreateUserAsync(Role.Student);
        var course = new TrainingCourse { Title = "Training projection regression", Status = TrainingCourseStatus.Published };
        var first = new TrainingCourseChapter { Title = "Completed chapter", Course = course, Order = 1 };
        var second = new TrainingCourseChapter { Title = "Learning chapter", Course = course, Order = 2 };
        var draft = new TrainingCourseChapter { Title = "Draft chapter", Course = course, IsPublished = false };
        var shared = new ExerciseChallenge { Title = "Shared lab" };
        var hidden = new ExerciseChallenge { Title = "Draft lab" };
        var unbound = new ExerciseChallenge { Title = "Unbound lab" };

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AddRange(first, second, draft, shared, hidden, unbound);
            await db.SaveChangesAsync();
            db.TrainingCourseEnrollments.AddRange(
                new TrainingCourseEnrollment { CourseId = course.Id, UserId = learner.Id, Status = TrainingCourseEnrollmentStatus.Approved },
                new TrainingCourseEnrollment { CourseId = course.Id, UserId = other.Id, Status = TrainingCourseEnrollmentStatus.Approved });
            db.TrainingCourseChallenges.AddRange(new[] { shared, hidden, unbound }.Select(challenge =>
                new TrainingCourseChallenge { CourseId = course.Id, ExerciseChallengeId = challenge.Id }));
            await db.SaveChangesAsync();
            db.TrainingCourseChapterChallenges.AddRange(
                new TrainingCourseChapterChallenge { CourseId = course.Id, ChapterId = first.Id, ExerciseChallengeId = shared.Id },
                new TrainingCourseChapterChallenge { CourseId = course.Id, ChapterId = second.Id, ExerciseChallengeId = shared.Id },
                new TrainingCourseChapterChallenge { CourseId = course.Id, ChapterId = draft.Id, ExerciseChallengeId = hidden.Id });
            db.TrainingChapterProgresses.AddRange(
                new TrainingChapterProgress { CourseId = course.Id, ChapterId = first.Id, UserId = learner.Id,
                    Status = TrainingCourseProgressStatus.Completed, ReadPercent = 100, CompletedAt = DateTimeOffset.UtcNow },
                new TrainingChapterProgress { CourseId = course.Id, ChapterId = second.Id, UserId = learner.Id,
                    Status = TrainingCourseProgressStatus.Learning, ReadPercent = 60 },
                new TrainingChapterProgress { CourseId = course.Id, ChapterId = second.Id, UserId = other.Id,
                    Status = TrainingCourseProgressStatus.Completed, ReadPercent = 100, CompletedAt = DateTimeOffset.UtcNow });
            // Deliberately stale course aggregate: reads must reflect the real chapter facts.
            db.TrainingCourseProgresses.Add(new TrainingCourseProgress { CourseId = course.Id, UserId = learner.Id });
            db.TrainingCourseSubmissions.AddRange(
                new TrainingCourseSubmission { CourseId = course.Id, ExerciseChallengeId = shared.Id,
                    UserId = learner.Id, Status = AnswerResult.Accepted },
                new TrainingCourseSubmission { CourseId = course.Id, ExerciseChallengeId = unbound.Id,
                    UserId = other.Id, Status = AnswerResult.Accepted });
            await db.SaveChangesAsync();
        }

        using var client = await LoginAsync(learner);
        var model = await client.GetFromJsonAsync<JsonElement>($"/api/training/courses/{course.Id}");
        var chapters = model.GetProperty("chapters").EnumerateArray().ToArray();
        Assert.Equal(2, chapters.Length);
        Assert.Equal("Completed", chapters.Single(chapter => chapter.GetProperty("id").GetInt32() == first.Id).GetProperty("progressStatus").GetString());
        Assert.Equal("Learning", chapters.Single(chapter => chapter.GetProperty("id").GetInt32() == second.Id).GetProperty("progressStatus").GetString());
        Assert.Equal(60, chapters.Single(chapter => chapter.GetProperty("id").GetInt32() == second.Id).GetProperty("readPercent").GetInt32());
        Assert.Equal(1, model.GetProperty("completedChapterCount").GetInt32());
        Assert.Equal(2, model.GetProperty("totalChapterCount").GetInt32());
        Assert.Equal("Learning", model.GetProperty("progressStatus").GetString());
        Assert.True(model.GetProperty("challenges").EnumerateArray()
            .Single(challenge => challenge.GetProperty("exerciseChallengeId").GetInt32() == shared.Id).GetProperty("solved").GetBoolean());
        Assert.False(model.GetProperty("challenges").EnumerateArray()
            .Single(challenge => challenge.GetProperty("exerciseChallengeId").GetInt32() == unbound.Id).GetProperty("solved").GetBoolean());
        Assert.All(chapters, chapter => Assert.Equal(shared.Id,
            Assert.Single(chapter.GetProperty("challenges").EnumerateArray()).GetProperty("exerciseChallengeId").GetInt32()));
        Assert.All(model.GetProperty("challenges").EnumerateArray(), challenge =>
            Assert.False(challenge.TryGetProperty("chapterId", out var id) && id.ValueKind != JsonValueKind.Null));

        using var otherClient = await LoginAsync(other);
        var otherModel = await otherClient.GetFromJsonAsync<JsonElement>($"/api/training/courses/{course.Id}");
        Assert.False(otherModel.GetProperty("chapters")[0].TryGetProperty("progressStatus", out var status) && status.ValueKind != JsonValueKind.Null);
        Assert.Equal("Completed", otherModel.GetProperty("chapters")[1].GetProperty("progressStatus").GetString());

        using var adminClient = await LoginAsync(admin);
        var adminModel = await adminClient.GetFromJsonAsync<JsonElement>($"/api/admin/training/courses/{course.Id}");
        Assert.Equal(3, adminModel.GetProperty("chapters").GetArrayLength());
        Assert.Equal(draft.Id, adminModel.GetProperty("challenges").EnumerateArray()
            .Single(challenge => challenge.GetProperty("exerciseChallengeId").GetInt32() == hidden.Id).GetProperty("chapterId").GetInt32());

        using var outsiderClient = await LoginAsync(outsider);
        var outsiderModel = await outsiderClient.GetFromJsonAsync<JsonElement>($"/api/training/courses/{course.Id}");
        Assert.Equal(0, outsiderModel.GetProperty("chapters").GetArrayLength());
        Assert.Equal(0, outsiderModel.GetProperty("challenges").GetArrayLength());
    }

    [Fact]
    public async Task CompletingFirstChapter_PersistsCourseCompletionInTheSameRequest()
    {
        var learner = await CreateUserAsync(Role.Student);
        var course = new TrainingCourse { Title = "First completion regression", Status = TrainingCourseStatus.Published };
        var chapter = new TrainingCourseChapter { Title = "Read and complete", Course = course };
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(chapter);
            await db.SaveChangesAsync();
            db.TrainingCourseEnrollments.Add(new TrainingCourseEnrollment
                { CourseId = course.Id, UserId = learner.Id, Status = TrainingCourseEnrollmentStatus.Approved });
            await db.SaveChangesAsync();
        }

        using var client = await LoginAsync(learner);
        // No preceding chapter GET and no progress row: both helpers must use the same tracked row.
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var response = await client.PostAsync($"/api/training/courses/{course.Id}/chapters/{chapter.Id}/complete", null);
            response.EnsureSuccessStatusCode();
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var progress = await db.TrainingCourseProgresses.SingleAsync(item => item.CourseId == course.Id && item.UserId == learner.Id);
            Assert.Equal(TrainingCourseProgressStatus.Completed, progress.Status);
            Assert.Equal(1, progress.CompletedChapterCount);
            Assert.Equal(1, progress.TotalChapterCount);
            Assert.Equal(1, await db.TrainingChapterProgresses.CountAsync(item => item.ChapterId == chapter.Id && item.UserId == learner.Id));
        }
    }

    private Task<TestDataSeeder.SeededUser> CreateUserAsync(Role role) =>
        TestDataSeeder.CreateUserAsync(factory.Services, TestDataSeeder.RandomName(), $"Aa1!{Guid.NewGuid():N}", role: role);

    private async Task<HttpClient> LoginAsync(TestDataSeeder.SeededUser user)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/Account/LogIn", new LoginModel { UserName = user.UserName, Password = user.Password });
        response.EnsureSuccessStatusCode();
        return client;
    }
}
