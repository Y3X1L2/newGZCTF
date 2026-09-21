using GZCTF.Models;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.League;

public sealed class LeagueMigrationTests
{
    [Fact]
    public async Task ForwardUpgrade_PreservesExistingRows_AndPreUpgradeBackupRestores()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("league_upgrade").WithUsername("postgres").WithPassword("postgres").Build();
        await postgres.StartAsync();
        AppDbContext Context(string? database = null)
        {
            var connection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString());
            if (database is not null) connection.Database = database;
            return new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options)
                { SuppressProjectionRevisionBumps = true };
        }
        var user = new UserInfo { UserName = "league-sentinel" };
        await using (var before = Context())
        {
            var previous = before.Database.GetMigrations().Last(x => !x.Contains("League", StringComparison.Ordinal));
            await before.Database.GetService<IMigrator>().MigrateAsync(previous);
            before.Users.Add(user); await before.SaveChangesAsync();
        }
        var backup = await postgres.ExecAsync(["pg_dump", "-U", "postgres", "-d", "league_upgrade", "-Fc", "-f", "/tmp/league-before.dump"]);
        Assert.Equal(0, backup.ExitCode);
        await using (var upgraded = Context())
        {
            await upgraded.Database.MigrateAsync();
            Assert.Equal(user.UserName, (await upgraded.Users.SingleAsync(x => x.Id == user.Id)).UserName);
            Assert.False(upgraded.Database.HasPendingModelChanges());
            Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.Equal(2, await upgraded.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM information_schema.tables
                WHERE table_name IN ('LeagueMatches', 'LeagueRegistrations')
                """).SingleAsync());
        }
        Assert.Equal(0, (await postgres.ExecAsync(["createdb", "-U", "postgres", "league_restore"])).ExitCode);
        Assert.Equal(0, (await postgres.ExecAsync(["pg_restore", "-U", "postgres", "-d", "league_restore", "--exit-on-error", "/tmp/league-before.dump"])).ExitCode);
        await using var restored = Context("league_restore");
        Assert.Equal(user.UserName, (await restored.Users.SingleAsync(x => x.Id == user.Id)).UserName);
        Assert.Contains(await restored.Database.GetPendingMigrationsAsync(), x => x.EndsWith("AddLeaguePhaseOne", StringComparison.Ordinal));
        await restored.Database.MigrateAsync();
        Assert.False(restored.Database.HasPendingModelChanges());
    }
}
