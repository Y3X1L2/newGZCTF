using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class PreparedArtifactMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260716150550_AddAgentFleetUpdateState";
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_prepared_artifacts")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task PreparedArtifactMigration_BackfillsRawWithoutFabricatingProvenance()
    {
        await using (var context = CreateContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO "ImageTemplates"
                    ("Name", "OSType", "ImageType", "FileSize", "UploadedAt", "Status",
                     "ContainsMalware", "ImageHash")
                VALUES
                    ('raw-windows', 1, 1, 4096, CURRENT_TIMESTAMP, 0, false,
                     'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
                """);
            await migrator.MigrateAsync();
        }

        await using var migrated = CreateContext();
        var template = await migrated.ImageTemplates.AsNoTracking().SingleAsync(item => item.Name == "raw-windows");

        Assert.Equal(VmRuntimeMode.Opaque, template.VmRuntimeMode);
        Assert.Equal(VmArtifactStatus.None, template.VmArtifactStatus);
        Assert.Null(template.PreparedArtifactId);
        Assert.Empty(await migrated.VmPreparedArtifacts.AsNoTracking().ToArrayAsync());
        Assert.Empty(await migrated.Database.GetPendingMigrationsAsync());

        var bootstrapExecutionsRemoved = await migrated.Database.SqlQueryRaw<bool>("""
            SELECT to_regclass('public."TeamLabBootstrapExecutions"') IS NULL AS "Value"
            """).SingleAsync();
        Assert.True(bootstrapExecutionsRemoved);
    }

    [Fact]
    public async Task PreparedArtifactMigration_BlocksWhenLegacyFactoryDataExists()
    {
        await using var context = CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260717033200_AddVmPreparedArtifactControlPlaneAndFactoryCutover");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "ImageTemplates"
                ("Name", "OSType", "ImageType", "FileSize", "UploadedAt", "Status",
                 "ContainsMalware", "ImageHash", "VmPreparationStatus")
            VALUES
                ('legacy-prepared-source', 0, 1, 4096, CURRENT_TIMESTAMP, 0, false,
                 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 0);

            INSERT INTO "VmPreparedArtifacts"
                ("PublicId", "SourceImageTemplateId", "SourceImageHash", "FactoryVersion",
                 "PreparationContractVersion", "GuestProtocolVersion", "OSType", "Status",
                 "ArtifactDigest", "ArtifactSize", "RegistryAddress", "RegistryRepository",
                 "RegistryTag", "CreatedAt")
            SELECT
                gen_random_uuid(), "Id", "ImageHash", 1, 1, 1, 0, 1,
                'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
                4096, '10.24.0.28:5000', 'gzctf/legacy/prepared', 'legacy', CURRENT_TIMESTAMP
            FROM "ImageTemplates" WHERE "Name" = 'legacy-prepared-source';
            """);

        var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());

        Assert.Equal("P0001", error.SqlState);
        Assert.Equal("phase9_vm_factory_data_requires_explicit_cleanup", error.MessageText);
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM \"VmPreparedArtifacts\"").SingleAsync());
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql($"{_postgres.GetConnectionString()};Include Error Detail=true").Options)
    {
        SuppressProjectionRevisionBumps = true
    };
}
