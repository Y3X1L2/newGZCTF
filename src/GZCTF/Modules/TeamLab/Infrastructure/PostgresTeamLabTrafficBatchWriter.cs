using System.Net;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace GZCTF.Modules.TeamLab.Infrastructure;

public sealed class PostgresTeamLabTrafficBatchWriter(
    AppDbContext context,
    ILogger<PostgresTeamLabTrafficBatchWriter> logger)
{
    private const string CreateStagingSql = """
        CREATE TEMP TABLE teamlab_traffic_ingest_stage
        (
            runtime_id integer NOT NULL,
            generation integer NOT NULL,
            shard_id integer NULL,
            network_id integer NOT NULL,
            worker_node_id uuid NULL,
            source_cursor bigint NOT NULL,
            source_ip varchar(64) NOT NULL,
            source_prefix varchar(64) NOT NULL,
            source_port integer NULL,
            destination_ip varchar(64) NOT NULL,
            destination_prefix varchar(64) NOT NULL,
            destination_port integer NULL,
            protocol varchar(16) NOT NULL,
            bytes bigint NOT NULL,
            packets bigint NOT NULL,
            captured_at timestamp with time zone NOT NULL,
            fingerprint bytea NOT NULL
        ) ON COMMIT DROP;
        """;

    private const string CopySql = """
        COPY teamlab_traffic_ingest_stage
            (runtime_id, generation, shard_id, network_id, worker_node_id, source_cursor,
             source_ip, source_prefix, source_port, destination_ip, destination_prefix,
             destination_port, protocol, bytes, packets, captured_at, fingerprint)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string InsertSql = """
        INSERT INTO "TeamLabTrafficFlows"
            ("RuntimeId", "Generation", "SourceCursor", "ShardId", "NetworkId", "WorkerNodeId",
             "SourceIp", "SourcePrefix", "SourcePort", "DestinationIp", "DestinationPrefix",
             "DestinationPort", "Protocol", "Bytes", "Packets", "FirstSeenAt", "LastSeenAt",
             "CapturedAt", "Fingerprint")
        SELECT runtime_id, generation, source_cursor, shard_id, network_id, worker_node_id,
               source_ip, source_prefix, source_port, destination_ip, destination_prefix,
               destination_port, protocol, bytes, packets, captured_at, captured_at,
               captured_at, fingerprint
        FROM teamlab_traffic_ingest_stage
        ON CONFLICT ("CapturedAt", "RuntimeId", "Generation", "Fingerprint") DO NOTHING;
        """;

    public async Task<int> WriteAsync(
        IReadOnlyList<TeamLabTrafficEnvelope> envelopes,
        CancellationToken cancellationToken)
    {
        if (envelopes.Count == 0)
            return 0;
        if (envelopes.Count > TeamLabTrafficIngestionLimits.MaxBatchSamples ||
            envelopes.Sum(item => item.GetSerializedSize()) > TeamLabTrafficIngestionLimits.MaxBatchBytes)
            throw new ArgumentException("TeamLab traffic persistence batch exceeds the ingest limits.");

        foreach (var envelope in envelopes)
            envelope.Validate();

        var connectionString = context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString) ||
            context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("TeamLab traffic batch persistence requires PostgreSQL.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var create = new NpgsqlCommand(CreateStagingSql, connection, transaction))
            await create.ExecuteNonQueryAsync(cancellationToken);

        await using (var importer = await connection.BeginBinaryImportAsync(CopySql, cancellationToken))
        {
            foreach (var envelope in envelopes)
            {
                await importer.StartRowAsync(cancellationToken);
                await importer.WriteAsync(envelope.RuntimeId, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(envelope.Generation, NpgsqlDbType.Integer, cancellationToken);
                await WriteNullableAsync(importer, envelope.ShardId, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(envelope.NetworkId, NpgsqlDbType.Integer, cancellationToken);
                await WriteNullableAsync(importer, envelope.WorkerNodeId, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(envelope.SourceCursor, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(envelope.SourceIp, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(ToPrivatePrefix(envelope.SourceIp), NpgsqlDbType.Varchar, cancellationToken);
                await WriteNullableAsync(importer, envelope.SourcePort, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(envelope.DestinationIp, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(ToPrivatePrefix(envelope.DestinationIp), NpgsqlDbType.Varchar, cancellationToken);
                await WriteNullableAsync(importer, envelope.DestinationPort, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(envelope.Protocol, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(envelope.Bytes, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(envelope.Packets, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(envelope.CapturedAt, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(Convert.FromHexString(envelope.Fingerprint), NpgsqlDbType.Bytea,
                    cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        int inserted;
        await using (var insert = new NpgsqlCommand(InsertSql, connection, transaction))
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogDebug("Persisted TeamLab traffic batch: received={Received}, inserted={Inserted}",
            envelopes.Count, inserted);
        return inserted;
    }

    private static async ValueTask WriteNullableAsync<T>(
        NpgsqlBinaryImporter importer,
        T? value,
        NpgsqlDbType type,
        CancellationToken cancellationToken)
        where T : struct
    {
        if (value.HasValue)
            await importer.WriteAsync(value.Value, type, cancellationToken);
        else
            await importer.WriteNullAsync(cancellationToken);
    }

    private static string ToPrivatePrefix(string value)
    {
        if (!IPAddress.TryParse(value, out var address))
            return "unknown";

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";

        Array.Clear(bytes, 8, bytes.Length - 8);
        return $"{new IPAddress(bytes)}/64";
    }
}
