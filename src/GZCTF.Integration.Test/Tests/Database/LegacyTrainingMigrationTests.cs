using System.Data;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class LegacyTrainingMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260709060159_AddImageDistributionRecords";
    private const string PhaseZeroMigration = "20260710100000_RemoveLegacyIrScenarioTraining";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_phase_zero")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ContractMigration_PreservesLegacyTrainingFactsAndDropsLegacyTables()
    {
        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await SeedLegacyTrainingTreeAsync(context);
            await migrator.MigrateAsync(PhaseZeroMigration);
        }

        await using var migrated = CreateContext();
        var course = await migrated.TrainingCourses
            .Include(item => item.Enrollments)
            .Include(item => item.Chapters)
                .ThenInclude(chapter => chapter.Challenges)
            .Include(item => item.Challenges)
            .SingleAsync();

        Assert.Equal("legacy-root", course.Slug);
        Assert.Equal(TrainingCourseStatus.Published, course.Status);
        Assert.Equal(TrainingCourseEnrollmentPolicy.TeacherApproval, course.EnrollmentPolicy);
        Assert.Contains("direction:blue-team", course.Tags);
        Assert.Contains("training-type:Ctf", course.Tags);
        Assert.Equal(2, course.Chapters.Count);

        var rootChapter = course.Chapters.Single(chapter => chapter.Title == "Root");
        var childChapter = course.Chapters.Single(chapter => chapter.Title == "Child");
        Assert.Equal(rootChapter.Id, childChapter.ParentId);
        Assert.False(childChapter.CompletionPolicy.RequireContentRead);
        Assert.True(childChapter.CompletionPolicy.RequireAllRequiredChallenges);
        Assert.Single(course.Enrollments, enrollment =>
            enrollment.Status == TrainingCourseEnrollmentStatus.Approved && enrollment.UserId == SeedUserId);
        Assert.Single(course.Challenges);
        Assert.Single(childChapter.Challenges);

        var submission = await migrated.TrainingCourseSubmissions.SingleAsync();
        Assert.Equal(course.Id, submission.CourseId);
        Assert.Equal(childChapter.Id, submission.ChapterId);
        Assert.Equal(AnswerResult.Accepted, submission.Status);

        var paper = await migrated.TrainingCourseChapterTheoryPapers.SingleAsync();
        Assert.Equal(childChapter.Id, paper.ChapterId);
        Assert.True(paper.AllowRetake);
        Assert.False(paper.ShowCorrectAnswerAfterSubmit);

        var sheets = await migrated.TrainingCourseChapterTheorySheets
            .Include(sheet => sheet.Answers)
            .OrderBy(sheet => sheet.AttemptNumber)
            .ToArrayAsync();
        Assert.Equal([1, 2], sheets.Select(sheet => sheet.AttemptNumber));
        Assert.All(sheets, sheet => Assert.Single(sheet.Answers));
        Assert.Equal([0, 10], sheets.Select(sheet => sheet.Score));

        var chapterProgress = await migrated.TrainingChapterProgresses
            .SingleAsync(progress => progress.UserId == SeedUserId && progress.ChapterId == childChapter.Id);
        Assert.Equal(37, chapterProgress.ReadPercent);
        Assert.Equal(TrainingCourseProgressStatus.Learning, chapterProgress.Status);

        foreach (var table in RemovedTables)
            Assert.False(await TableExistsAsync(migrated, table), $"Legacy table {table} still exists.");
    }

    private static readonly Guid SeedUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly string[] RemovedTables =
    [
        "IRCheckpoints",
        "IRInstances",
        "Stages",
        "ScenarioInstances",
        "ScoringRules",
        "TimeSlots",
        "TrainingDirections",
        "TrainingModules",
        "TrainingModuleVisibilities",
        "TrainingModuleChallenges",
        "TrainingCtfSubmissions",
        "TheoryTrainingPlans",
        "TheoryTrainingPlanQuestions",
        "TheoryTrainingSessions",
        "TheoryTrainingSessionQuestions",
        "TrainingArticleProgresses",
        "TrainingModuleProgresses"
    ];

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task SeedLegacyTrainingTreeAsync(AppDbContext context)
    {
        var user = new UserInfo
        {
            Id = SeedUserId,
            UserName = "phase0student",
            NormalizedUserName = "PHASE0STUDENT",
            Email = "phase-zero@example.test",
            NormalizedEmail = "PHASE-ZERO@EXAMPLE.TEST",
            EmailConfirmed = true,
            Role = Role.Student,
            RegisterTimeUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        };
        var group = new StudentGroup
        {
            Name = "Legacy learners",
            Members =
            [
                new StudentGroupMember
                {
                    Student = user,
                    JoinedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z")
                }
            ]
        };
        var challenge = new ExerciseChallenge
        {
            Title = "Legacy challenge",
            Content = "Migrated practical exercise",
            Category = ChallengeCategory.Web,
            Type = ChallengeType.StaticAttachment,
            IsEnabled = true,
            Credit = true
        };
        var bankQuestion = new TheoryQuestionBankItem
        {
            Type = TheoryQuestionType.SingleChoice,
            BankName = "Legacy",
            Title = "Legacy question",
            Content = "Choose zero",
            Options = ["zero", "one"],
            AnswerIndexes = [0]
        };

        context.StudentGroups.Add(group);
        context.ExerciseChallenges.Add(challenge);
        context.TheoryQuestionBankItems.Add(bankQuestion);
        await context.SaveChangesAsync();

        var createdAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z");
        await context.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO "TrainingDirections"
                ("Id", "Type", "Key", "Title", "Description", "Icon", "Color", "Order", "IsEnabled", "CreatedById", "CreatedAt", "UpdatedAt")
            VALUES
                (1001, 'Ctf', 'blue-team', 'Blue Team', 'Legacy direction', 'shield', '#00aa77', 0, TRUE, {{SeedUserId}}, {{createdAt}}, {{createdAt}});

            INSERT INTO "TrainingModules"
                ("Id", "DirectionId", "ParentId", "Type", "Title", "Slug", "Summary", "ArticleContent", "ArticleContentType", "CoverFileHash", "EnvironmentTemplateId", "CompletionRule", "IsPublished", "PublishedAt", "Order", "CreatedById", "UpdatedById", "CreatedAt", "UpdatedAt")
            VALUES
                (1101, 1001, NULL, 'Ctf', 'Root', 'legacy-root', 'Root summary', 'Root content', 'Markdown', NULL, NULL,
                 '{"RequireArticleRead":true,"RequireAllRequiredChallenges":true,"RequiredChallengeCount":0,"TheoryPassRate":80}',
                 TRUE, {{createdAt}}, 0, {{SeedUserId}}, {{SeedUserId}}, {{createdAt}}, {{createdAt}}),
                (1102, 1001, 1101, 'Theory', 'Child', 'legacy-child', 'Child summary', 'Child content', 'Markdown', NULL, NULL,
                 '{"RequireArticleRead":false,"RequireAllRequiredChallenges":true,"RequiredChallengeCount":0,"TheoryPassRate":70}',
                 TRUE, {{createdAt}}, 1, {{SeedUserId}}, {{SeedUserId}}, {{createdAt.AddMinutes(1)}}, {{createdAt.AddMinutes(1)}});

            INSERT INTO "TrainingModuleVisibilities"
                ("Id", "ModuleId", "GroupId", "VisibilityType", "CreatedById", "CreatedAt")
            VALUES
                (1201, 1101, {{group.Id}}, 'GroupOnly', {{SeedUserId}}, {{createdAt}}),
                (1202, 1102, {{group.Id}}, 'GroupOnly', {{SeedUserId}}, {{createdAt}});

            INSERT INTO "TrainingModuleChallenges"
                ("ModuleId", "ExerciseChallengeId", "Order", "IsRequired", "DisplayTitle", "CreatedById", "CreatedAt")
            VALUES
                (1102, {{challenge.Id}}, 0, TRUE, 'Migrated challenge', {{SeedUserId}}, {{createdAt}});

            INSERT INTO "TrainingCtfSubmissions"
                ("Id", "ModuleId", "ExerciseChallengeId", "UserId", "Status", "SubmittedAt", "SubmittedAnswerHash", "FlagId", "IpAddress")
            VALUES
                (1301, 1102, {{challenge.Id}}, {{SeedUserId}}, 'Accepted', {{createdAt.AddHours(1)}}, 'answer-hash', NULL, '10.0.0.2');

            INSERT INTO "TheoryTrainingPlans"
                ("Id", "ModuleId", "Title", "Description", "Mode", "QuestionCount", "BankName", "QuestionTypes", "PassRate", "AllowRetake", "ShowCorrectAnswerAfterSubmit", "IsPublished", "CreatedById", "UpdatedById", "CreatedAt", "UpdatedAt")
            VALUES
                (1401, 1102, 'Legacy paper', 'Legacy paper description', 'Manual', 1, 'Legacy', '["SingleChoice"]', 60, TRUE, FALSE, TRUE, {{SeedUserId}}, {{SeedUserId}}, {{createdAt}}, {{createdAt}});

            INSERT INTO "TheoryTrainingPlanQuestions" ("PlanId", "SourceQuestionId", "Score", "Order")
            VALUES (1401, {{bankQuestion.Id}}, 10, 0);

            INSERT INTO "TheoryTrainingSessions"
                ("Id", "PlanId", "ModuleId", "UserId", "Status", "Score", "MaxScore", "CorrectCount", "TotalCount", "CreatedAt", "SubmittedAt")
            VALUES
                (1501, 1401, 1102, {{SeedUserId}}, 'Submitted', 0, 10, 0, 1, {{createdAt.AddHours(2)}}, {{createdAt.AddHours(2).AddMinutes(5)}}),
                (1502, 1401, 1102, {{SeedUserId}}, 'Submitted', 10, 10, 1, 1, {{createdAt.AddHours(3)}}, {{createdAt.AddHours(3).AddMinutes(5)}});

            INSERT INTO "TheoryTrainingSessionQuestions"
                ("Id", "SessionId", "SourceQuestionId", "Type", "Title", "Content", "Options", "AnswerIndexes", "SelectedIndexes", "IsCorrect", "Score", "Order")
            VALUES
                (1601, 1501, {{bankQuestion.Id}}, 'SingleChoice', 'Legacy question', 'Choose zero', '["zero","one"]', '[0]', '[]', FALSE, 10, 0),
                (1602, 1502, {{bankQuestion.Id}}, 'SingleChoice', 'Legacy question', 'Choose zero', '["zero","one"]', '[0]', '[0]', TRUE, 10, 0);

            INSERT INTO "TrainingArticleProgresses"
                ("ModuleId", "UserId", "ReadPercent", "CompletedAt", "LastReadAt")
            VALUES
                (1102, {{SeedUserId}}, 37, NULL, {{createdAt.AddHours(4)}});

            INSERT INTO "TrainingModuleProgresses"
                ("ModuleId", "UserId", "Status", "ChallengeSolvedCount", "ChallengeTotalCount", "TheoryBestScore", "TheoryBestPassRate", "StartedAt", "CompletedAt", "UpdatedAt")
            VALUES
                (1102, {{SeedUserId}}, 'Reading', 1, 1, 10, 100, {{createdAt.AddHours(1)}}, NULL, {{createdAt.AddHours(4)}});
            """);
    }

    private static async Task<bool> TableExistsAsync(AppDbContext context, string tableName)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@qualified_name) IS NOT NULL";
        command.Parameters.AddWithValue("qualified_name", $"public.\"{tableName}\"");
        return (bool)(await command.ExecuteScalarAsync())!;
    }
}
