using GZCTF.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Integration.Test.Base;

public sealed class IsolatedPostgresDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly string _databaseName;

    private IsolatedPostgresDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<IsolatedPostgresDatabase> CreateAsync(
        string baseConnectionString,
        CancellationToken cancellationToken = default)
    {
        var databaseName = $"isolated_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var target = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName
        };
        var database = new IsolatedPostgresDatabase(
            admin.ConnectionString,
            databaseName,
            target.ConnectionString);
        try
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync(cancellationToken);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var target = new NpgsqlConnection(ConnectionString))
            NpgsqlConnection.ClearPool(target);
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
