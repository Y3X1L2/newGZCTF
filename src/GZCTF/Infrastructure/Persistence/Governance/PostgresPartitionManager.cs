using System.Globalization;
using System.Text.RegularExpressions;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GZCTF.Infrastructure.Persistence.Governance;

public sealed partial class PostgresPartitionManager(
    AppDbContext context,
    DataRetentionPolicyCatalog catalog,
    DataGovernanceMetrics metrics)
{
    private const long AdvisoryLockKey = 0x475A435446504152;

    private static readonly IReadOnlyDictionary<string, PartitionDefinition> Definitions =
        new Dictionary<string, PartitionDefinition>(StringComparer.Ordinal)
        {
            ["system-log"] = new("Logs", "Logs_p", PartitionGrain.Month),
            ["teamlab-flow"] = new("TeamLabTrafficFlows", "TeamLabTrafficFlows_p", PartitionGrain.Day)
        };

    public async Task EnsureFuturePartitionsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsurePostgres();
        foreach (var (dataSet, definition) in Definitions)
        {
            _ = catalog.GetRequired(dataSet);
            var current = Floor(now, definition.Grain);
            for (var offset = 0; offset <= 2; offset++)
            {
                var start = Add(current, definition.Grain, offset);
                await CreatePartitionAsync(definition, start, Add(start, definition.Grain, 1),
                    cancellationToken);
            }

            var horizon = Add(current, definition.Grain, 3) - now;
            metrics.SetPartitionHorizon(dataSet, Math.Max(0, (int)Math.Floor(horizon.TotalDays)));
        }
    }

    public async Task<IReadOnlyList<PartitionWindow>> GetExpiredPartitionsAsync(
        string dataSet,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        EnsurePostgres();
        var definition = GetDefinition(dataSet);
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        var partitions = new List<PartitionWindow>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT child.relname
                FROM pg_inherits
                JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
                JOIN pg_class child ON pg_inherits.inhrelid = child.oid
                WHERE parent.relname = @parent
                ORDER BY child.relname
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "parent";
            parameter.Value = definition.ParentTable;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                if (TryGetBounds(definition, name, out var lower, out var upper) && upper <= cutoff)
                    partitions.Add(new PartitionWindow(name, lower, upper));
            }
        }

        return partitions;
    }

    public async Task<PartitionDropSummary> DropExpiredPartitionsAsync(
        string dataSet,
        DateTimeOffset cutoff,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        EnsurePostgres();
        var definition = GetDefinition(dataSet);
        var partitions = await GetExpiredPartitionsAsync(dataSet, cutoff, cancellationToken);

        var dropped = 0;
        long rowsDeleted = 0;
        foreach (var partition in partitions)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0})", [AdvisoryLockKey], cancellationToken);
            await ExecuteDdlAsync($"LOCK TABLE \"{partition.Name}\" IN ACCESS EXCLUSIVE MODE", cancellationToken);

            var sourceRows = await CountPartitionRowsAsync(partition.Name, cancellationToken);
            var aggregationProven = await context.DataGovernanceRuns.AsNoTracking().AnyAsync(run =>
                run.DataSet == dataSet && run.Operation == "aggregate-partition" &&
                run.PartitionName == partition.Name && run.Status == DataGovernanceRunStatus.Completed &&
                run.Cutoff == partition.Upper && run.RowsRead == sourceRows,
                cancellationToken);
            if (!aggregationProven ||
                dataSet == "teamlab-flow" && await HasOpenRuntimeWindowAsync(partition, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            await ExecuteDdlAsync($"DROP TABLE IF EXISTS \"{partition.Name}\"", cancellationToken);
            context.DataGovernanceRuns.Add(new DataGovernanceRun
            {
                DataSet = dataSet,
                Operation = "drop-partition",
                Status = DataGovernanceRunStatus.Completed,
                LeaseOwner = leaseOwner,
                Cutoff = partition.Upper,
                RowsRead = sourceRows,
                RowsDeleted = sourceRows,
                PartitionName = partition.Name,
                CompletedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            dropped++;
            rowsDeleted += sourceRows;
        }

        return new PartitionDropSummary(dropped, rowsDeleted);
    }

    private async Task<long> CountPartitionRowsAsync(string partition, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM \"{partition}\"";
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task<bool> HasOpenRuntimeWindowAsync(
        PartitionWindow partition,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT EXISTS (
                SELECT 1
                FROM \"{partition.Name}\" flow
                JOIN \"TeamLabRuntimes\" runtime ON runtime.\"Id\" = flow.\"RuntimeId\"
                WHERE runtime.\"Status\" NOT IN (6, 8, 10)
            )
            """;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task CreatePartitionAsync(
        PartitionDefinition definition,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var name = PartitionName(definition, start);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})", [AdvisoryLockKey], cancellationToken);
        await ExecuteDdlAsync($$"""
            CREATE TABLE IF NOT EXISTS "{{name}}"
            PARTITION OF "{{definition.ParentTable}}"
            FOR VALUES FROM ('{{start:O}}') TO ('{{end:O}}')
            """, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string PartitionName(PartitionDefinition definition, DateTimeOffset start)
    {
        var suffix = definition.Grain == PartitionGrain.Month
            ? start.ToString("yyyyMM", CultureInfo.InvariantCulture)
            : start.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var name = $"{definition.Prefix}{suffix}";
        if (!IdentifierRegex().IsMatch(name))
            throw new InvalidOperationException("Generated partition identifier is invalid.");
        return name;
    }

    private static bool TryGetBounds(
        PartitionDefinition definition,
        string partition,
        out DateTimeOffset lower,
        out DateTimeOffset upper)
    {
        lower = default;
        upper = default;
        if (!partition.StartsWith(definition.Prefix, StringComparison.Ordinal))
            return false;
        var suffix = partition[definition.Prefix.Length..];
        var format = definition.Grain == PartitionGrain.Month ? "yyyyMM" : "yyyyMMdd";
        if (!DateTimeOffset.TryParseExact(suffix, format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out lower))
            return false;
        lower = new DateTimeOffset(lower.Year, lower.Month, lower.Day, 0, 0, 0, TimeSpan.Zero);
        upper = Add(lower, definition.Grain, 1);
        return true;
    }

    private static DateTimeOffset Floor(DateTimeOffset value, PartitionGrain grain)
    {
        var utc = value.ToUniversalTime();
        return grain == PartitionGrain.Month
            ? new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero)
            : new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset Add(DateTimeOffset value, PartitionGrain grain, int amount) =>
        grain == PartitionGrain.Month ? value.AddMonths(amount) : value.AddDays(amount);

    private static PartitionDefinition GetDefinition(string dataSet) =>
        Definitions.TryGetValue(dataSet, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Data set '{dataSet}' is not partition-managed.");

    private async Task ExecuteDdlAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsurePostgres()
    {
        if (context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Partition governance requires PostgreSQL.");
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    private sealed record PartitionDefinition(string ParentTable, string Prefix, PartitionGrain Grain);
}

public sealed record PartitionWindow(string Name, DateTimeOffset Lower, DateTimeOffset Upper);

public sealed record PartitionDropSummary(int PartitionCount, long RowsDeleted);
