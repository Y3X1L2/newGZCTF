using System.Text.Json;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace GZCTF.Integration.Test.Tests.Database;

public sealed class DatabaseStorageReportTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("gzctf_storage_report_test")
        .WithCleanUp(true)
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Report_IsReadOnly_AndCountsPartitionStorageOnceWithoutReadingPayloads()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Database", "storage-health-report.sql"));
        using (var empty = await ReadReportAsync(connection, sql))
            Assert.Equal(0, empty.RootElement.GetProperty("logical_tables").GetArrayLength());

        await using (var seed = new NpgsqlCommand("""
            CREATE SCHEMA report_fixture;
            CREATE TABLE report_fixture."Events" (day integer, payload text) PARTITION BY RANGE (day);
            CREATE TABLE report_fixture."Events_p1" PARTITION OF report_fixture."Events" FOR VALUES FROM (0) TO (10);
            CREATE TABLE report_fixture."Events_p2" PARTITION OF report_fixture."Events" FOR VALUES FROM (10) TO (20);
            CREATE TABLE report_fixture."Events_default" PARTITION OF report_fixture."Events" DEFAULT;
            CREATE INDEX ON report_fixture."Events" (day);
            INSERT INTO report_fixture."Events" VALUES
                (1, 'report-must-not-read-business-payload'), (11, 'second'), (21, 'default');
            CREATE TABLE public."Events" (id integer PRIMARY KEY);
            INSERT INTO public."Events" VALUES (1);
            """, connection))
            await seed.ExecuteNonQueryAsync();

        using var report = await ReadReportAsync(connection, sql);
        Assert.Equal("on", report.RootElement.GetProperty("transaction_read_only").GetString());
        Assert.DoesNotContain("report-must-not-read-business-payload", report.RootElement.GetRawText());
        var tables = report.RootElement.GetProperty("logical_tables").EnumerateArray().ToArray();
        Assert.Equal(2, tables.Length);
        var partitioned = Assert.Single(tables, item => item.GetProperty("schema_name").GetString() == "report_fixture");
        Assert.Equal("Events", partitioned.GetProperty("table_name").GetString());
        Assert.Equal(3, partitioned.GetProperty("physical_relations").GetInt32());
        Assert.Single(tables, item => item.GetProperty("schema_name").GetString() == "public");
        Assert.Equal(3, report.RootElement.GetProperty("partitions").GetArrayLength());
        await using var expectedSize = new NpgsqlCommand("""
            SELECT sum(pg_total_relation_size(inhrelid))::bigint
            FROM pg_inherits WHERE inhparent = 'report_fixture."Events"'::regclass
            """, connection);
        Assert.Equal((long)(await expectedSize.ExecuteScalarAsync())!,
            partitioned.GetProperty("total_bytes").GetInt64());
        await using var rows = new NpgsqlCommand("SELECT count(*) FROM report_fixture.\"Events\"", connection);
        Assert.Equal(3L, (long)(await rows.ExecuteScalarAsync())!);
    }

    private static async Task<JsonDocument> ReadReportAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
    }
}
