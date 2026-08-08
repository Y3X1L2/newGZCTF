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
            network_id integer NULL,
            observation_point_id integer NOT NULL,
            observation_point_kind smallint NOT NULL,
            asset_id integer NULL,
            worker_node_id uuid NOT NULL,
            source_sequence bigint NOT NULL,
            source_ip varchar(64) NOT NULL,
            source_prefix varchar(64) NOT NULL,
            source_port integer NULL,
            destination_ip varchar(64) NOT NULL,
            destination_prefix varchar(64) NOT NULL,
            destination_port integer NULL,
            protocol varchar(16) NOT NULL,
            tcp_flags smallint NULL,
            packet_length integer NOT NULL,
            packet_fingerprint bytea NULL,
            flow_fingerprint bytea NOT NULL,
            process_identity_hash bytea NULL,
            evidence_kind smallint NOT NULL,
            direction varchar(16) NOT NULL,
            bytes bigint NOT NULL,
            packets bigint NOT NULL,
            first_seen_at timestamp with time zone NOT NULL,
            last_seen_at timestamp with time zone NOT NULL,
            captured_at timestamp with time zone NOT NULL,
            evidence_fingerprint bytea NOT NULL
        ) ON COMMIT DROP;
        """;

    private const string CopySql = """
        COPY teamlab_traffic_ingest_stage
            (runtime_id, generation, shard_id, network_id, observation_point_id,
             observation_point_kind, asset_id, worker_node_id, source_sequence,
             source_ip, source_prefix, source_port, destination_ip, destination_prefix,
             destination_port, protocol, tcp_flags, packet_length, packet_fingerprint,
             flow_fingerprint, process_identity_hash, evidence_kind, direction,
             bytes, packets, first_seen_at, last_seen_at, captured_at, evidence_fingerprint)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string InsertObservationSql = """
        INSERT INTO "TeamLabTrafficObservations"
            ("RuntimeId", "Generation", "ObservationPointId", "WorkerNodeId", "SourceSequence",
             "ObservedAt", "Direction", "SourceIp", "SourcePort", "DestinationIp",
             "DestinationPort", "Protocol", "TcpFlags", "PacketLength", "PacketFingerprint",
             "FlowFingerprint", "ProcessIdentityHash", "EvidenceKind")
        SELECT runtime_id, generation, observation_point_id, worker_node_id, source_sequence,
               captured_at, direction, source_ip, source_port, destination_ip,
               destination_port, protocol, tcp_flags, packet_length, packet_fingerprint,
               flow_fingerprint, process_identity_hash, evidence_kind
        FROM teamlab_traffic_ingest_stage
        ON CONFLICT ("RuntimeId", "Generation", "ObservationPointId", "SourceSequence") DO NOTHING;
        """;

    private const string InsertFlowSql = """
        INSERT INTO "TeamLabTrafficFlows"
            ("RuntimeId", "Generation", "SourceCursor", "ShardId", "NetworkId", "WorkerNodeId",
             "SourceIp", "SourcePrefix", "SourcePort", "DestinationIp", "DestinationPrefix",
             "DestinationPort", "Protocol", "Bytes", "Packets", "FirstSeenAt", "LastSeenAt",
             "CapturedAt", "Fingerprint")
        SELECT runtime_id, generation, source_sequence, shard_id, network_id, worker_node_id,
               source_ip, source_prefix, source_port, destination_ip, destination_prefix,
               destination_port, protocol, bytes, packets, first_seen_at, last_seen_at,
               captured_at, flow_fingerprint
        FROM teamlab_traffic_ingest_stage
        WHERE observation_point_kind = 0 AND evidence_kind = 0
          AND network_id IS NOT NULL
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
                await WriteNullableAsync(importer, envelope.NetworkId, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(envelope.ObservationPointId, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync((short)envelope.ObservationPointKind, NpgsqlDbType.Smallint, cancellationToken);
                await WriteNullableAsync(importer, envelope.AssetId, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(envelope.WorkerNodeId, NpgsqlDbType.Uuid, cancellationToken);
                await importer.WriteAsync(envelope.SourceSequence, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(envelope.SourceIp, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(ToPrivatePrefix(envelope.SourceIp), NpgsqlDbType.Varchar, cancellationToken);
                await WriteNullableAsync(importer, envelope.SourcePort, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(envelope.DestinationIp, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(ToPrivatePrefix(envelope.DestinationIp), NpgsqlDbType.Varchar, cancellationToken);
                await WriteNullableAsync(importer, envelope.DestinationPort, NpgsqlDbType.Integer, cancellationToken);
                await importer.WriteAsync(envelope.Protocol, NpgsqlDbType.Varchar, cancellationToken);
                await WriteNullableAsync(importer,
                    envelope.TcpFlags is { } flags ? (short?)flags : null,
                    NpgsqlDbType.Smallint, cancellationToken);
                await importer.WriteAsync(envelope.PacketLength, NpgsqlDbType.Integer, cancellationToken);
                await WriteDigestAsync(importer, envelope.PacketFingerprint, cancellationToken);
                await importer.WriteAsync(Convert.FromHexString(envelope.FlowFingerprint), NpgsqlDbType.Bytea,
                    cancellationToken);
                await WriteDigestAsync(importer, envelope.ProcessIdentityHash, cancellationToken);
                await importer.WriteAsync(
                    envelope.EvidenceKind.Equals("Packet", StringComparison.OrdinalIgnoreCase) ? (short)0 : (short)1,
                    NpgsqlDbType.Smallint, cancellationToken);
                await importer.WriteAsync(envelope.Direction, NpgsqlDbType.Varchar, cancellationToken);
                await importer.WriteAsync(envelope.Bytes, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(envelope.Packets, NpgsqlDbType.Bigint, cancellationToken);
                await importer.WriteAsync(envelope.FirstSeenAt ?? envelope.CapturedAt,
                    NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(envelope.LastSeenAt ?? envelope.CapturedAt,
                    NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(envelope.CapturedAt, NpgsqlDbType.TimestampTz, cancellationToken);
                await importer.WriteAsync(Convert.FromHexString(envelope.EvidenceFingerprint), NpgsqlDbType.Bytea,
                    cancellationToken);
            }

            await importer.CompleteAsync(cancellationToken);
        }

        int inserted;
        await using (var insert = new NpgsqlCommand(InsertObservationSql, connection, transaction))
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        await using (var insertFlows = new NpgsqlCommand(InsertFlowSql, connection, transaction))
            await insertFlows.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogDebug("已持久化 TeamLab 流量批次：received={Received}, inserted={Inserted}",
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

    private static async ValueTask WriteDigestAsync(
        NpgsqlBinaryImporter importer,
        string? digest,
        CancellationToken cancellationToken)
    {
        if (digest is null)
            await importer.WriteNullAsync(cancellationToken);
        else
            await importer.WriteAsync(Convert.FromHexString(digest), NpgsqlDbType.Bytea, cancellationToken);
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
