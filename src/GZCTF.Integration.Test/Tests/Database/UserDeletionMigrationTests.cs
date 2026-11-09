using GZCTF.Migrations;
using GZCTF.Models;
using GZCTF.Utils;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class UserDeletionMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("user_deletion").WithUsername("postgres").WithPassword("postgres").Build();
    public Task InitializeAsync() => postgres.StartAsync();
    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task AdditiveUpgradeAndBackupRestore_PreserveExistingIdentityAndHistory()
    {
        await using var db = Context(postgres.GetConnectionString());
        await db.Database.EnsureCreatedAsync();
        var user = new UserInfo { UserName = "migration-user", Role = Role.Student };
        var team = new Team { Name = "history-team", Captain = user, Members = [user] };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        var migration = new AddUserDeletionTombstone();
        await Apply(db, migration.DownOperations);
        Assert.Equal(0, (await postgres.ExecAsync(["pg_dump", "-U", "postgres", "-Fc", "-f", "/tmp/before.dump", "user_deletion"])).ExitCode);
        await Apply(db, migration.UpOperations);
        db.ChangeTracker.Clear();
        Assert.Null((await db.Users.SingleAsync()).DeletedAtUtc);
        Assert.Equal(user.Id, (await db.Teams.SingleAsync()).CaptainId);
        Assert.Equal(0, (await postgres.ExecAsync(["createdb", "-U", "postgres", "user_restored"])).ExitCode);
        Assert.Equal(0, (await postgres.ExecAsync(["pg_restore", "-U", "postgres", "-d", "user_restored", "/tmp/before.dump"])).ExitCode);
        var restoredConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = "user_restored" };
        await using var restored = Context(restoredConnection.ConnectionString);
        await Apply(restored, migration.UpOperations);
        Assert.Null((await restored.Users.SingleAsync()).DeletedAtUtc);
        Assert.Equal(user.Id, (await restored.Teams.Include(t => t.Members).SingleAsync()).Members.Single().Id);
        Assert.False(restored.Database.HasPendingModelChanges());
    }

    private static AppDbContext Context(string connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
    private static async Task Apply(AppDbContext db, IReadOnlyList<MigrationOperation> operations)
    {
        foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
    }
}
