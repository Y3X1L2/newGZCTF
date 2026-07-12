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

public sealed class ImageTemplateOwnershipMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260710140008_AddApiOperations";
    private const string OwnershipMigration = "20260710144140_DecoupleImageTemplateOwnership";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_phase_one_images")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_CreatesBindingsAndPreservesEveryTemplate()
    {
        var ownerId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        int courseId;
        int boundTemplateId;

        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            var owner = new UserInfo
            {
                Id = ownerId,
                UserName = "imageowner",
                NormalizedUserName = "IMAGEOWNER",
                Email = "image-owner@example.test",
                NormalizedEmail = "IMAGE-OWNER@EXAMPLE.TEST",
                EmailConfirmed = true,
                Role = Role.Teacher,
                RegisterTimeUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
            };
            var course = new TrainingCourse
            {
                Title = "Ownership Migration",
                Slug = "ownership-migration",
                CreatedById = ownerId,
                UpdatedById = ownerId
            };
            context.AddRange(owner, course);
            await context.SaveChangesAsync();
            courseId = course.Id;
            boundTemplateId = 900001;
            const int globalTemplateId = 900002;

            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "ImageTemplates"
                    ("Id", "Name", "OSType", "ImageType", "FileSize", "UploadedAt", "Status",
                     "ContainsMalware", "TrainingCourseId")
                VALUES
                    ({{boundTemplateId}}, 'bound', 0, 0, 0, now(), 0, false, {{courseId}}),
                    ({{globalTemplateId}}, 'global', 0, 0, 0, now(), 0, false, NULL);
                """);
            await migrator.MigrateAsync(OwnershipMigration);
        }

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT count(*) FROM "ImageTemplates"),
                (SELECT count(*) FROM "TrainingCourseImageTemplateBindings"),
                EXISTS (
                    SELECT 1 FROM "TrainingCourseImageTemplateBindings"
                    WHERE "CourseId" = @courseId
                      AND "ImageTemplateId" = @templateId
                      AND "AddedById" = @ownerId
                ),
                EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'ImageTemplates' AND column_name = 'TrainingCourseId'
                )
            """;
        command.Parameters.AddWithValue("courseId", courseId);
        command.Parameters.AddWithValue("templateId", boundTemplateId);
        command.Parameters.AddWithValue("ownerId", ownerId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
    }

    [Fact]
    public async Task DownMigration_RestoresOneCourseBindingPerTemplate()
    {
        const int firstCourseId = 910001;
        const int secondCourseId = 910002;
        const int templateId = 910003;

        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(OwnershipMigration);
            context.TrainingCourses.AddRange(
                new TrainingCourse { Id = firstCourseId, Title = "first", Slug = "first-down" },
                new TrainingCourse { Id = secondCourseId, Title = "second", Slug = "second-down" });
            context.ImageTemplates.Add(new ImageTemplate
            {
                Id = templateId,
                Name = "down",
                ImageType = ImageType.Docker,
                OSType = OSType.Linux,
                Status = ImageStatus.Ready
            });
            context.TrainingCourseImageTemplateBindings.AddRange(
                new()
                {
                    CourseId = secondCourseId,
                    ImageTemplateId = templateId
                },
                new()
                {
                    CourseId = firstCourseId,
                    ImageTemplateId = templateId
                });
            await context.SaveChangesAsync();

            await migrator.MigrateAsync(PreviousMigration);
        }

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "TrainingCourseId",
                   EXISTS (
                       SELECT 1 FROM information_schema.tables
                       WHERE table_name = 'TrainingCourseImageTemplateBindings'
                   )
            FROM "ImageTemplates"
            WHERE "Id" = @templateId
            """;
        command.Parameters.AddWithValue("templateId", templateId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(firstCourseId, reader.GetInt32(0));
        Assert.False(reader.GetBoolean(1));
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true")
            .Options;
        return new AppDbContext(options) { SuppressProjectionRevisionBumps = true };
    }
}
