using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed class PostgresGovernanceLease(AppDbContext context)
{
    private const long AdvisoryLockKey = 0x475A435446474F56;

    public async Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connectionString = context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString) ||
            context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) != true)
            return null;

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        var acquired = await command.ExecuteScalarAsync(cancellationToken) as bool? == true;
        if (!acquired)
        {
            await connection.DisposeAsync();
            return null;
        }

        return new Lease(connection);
    }

    private sealed class Lease(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", AdvisoryLockKey);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
