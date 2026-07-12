using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GZCTF.Modules.TeamLab.Application;

public sealed record TeamLabTrafficEnvelope(
    int SchemaVersion,
    int RuntimeId,
    int Generation,
    int? ShardId,
    int NetworkId,
    Guid? WorkerNodeId,
    DateTimeOffset CapturedAt,
    long SourceCursor,
    string Fingerprint,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    long Packets,
    long Bytes)
{
    public const int CurrentSchemaVersion = 1;

    public static TeamLabTrafficEnvelope Create(
        int runtimeId,
        int generation,
        int? shardId,
        int networkId,
        Guid? workerNodeId,
        TeamLabNodeFlowSample sample)
    {
        var sourceIp = NormalizeIp(sample.SourceIp);
        var destinationIp = NormalizeIp(sample.DestinationIp);
        var protocol = sample.Protocol.Trim().ToUpperInvariant();
        var capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(
            sample.CapturedAt.ToUniversalTime().ToUnixTimeMilliseconds());
        var fingerprintInput = string.Join('|', runtimeId, generation, networkId,
            sourceIp, sample.SourcePort, destinationIp, sample.DestinationPort, protocol,
            capturedAt.UtcTicks, sample.Cursor, sample.Bytes, 1);

        return new TeamLabTrafficEnvelope(
            CurrentSchemaVersion,
            runtimeId,
            generation,
            shardId,
            networkId,
            workerNodeId,
            capturedAt,
            sample.Cursor,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))),
            sourceIp,
            sample.SourcePort,
            destinationIp,
            sample.DestinationPort,
            protocol,
            1,
            sample.Bytes);
    }

    public int GetSerializedSize() => JsonSerializer.SerializeToUtf8Bytes(this).Length;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || RuntimeId <= 0 || Generation <= 0 || NetworkId <= 0 ||
            CapturedAt == default || SourceCursor <= 0 || Fingerprint.Length != 64 || !Fingerprint.All(Uri.IsHexDigit) ||
            SourceIp.Length is < 1 or > 64 || DestinationIp.Length is < 1 or > 64 ||
            Protocol.Length is < 1 or > 16 || Packets < 1 || Bytes < 0 ||
            SourcePort is < 0 or > 65535 || DestinationPort is < 0 or > 65535)
            throw new ArgumentException("TeamLab traffic envelope is invalid.");
    }

    private static string NormalizeIp(string value) =>
        IPAddress.TryParse(value.Trim(), out var address) ? address.ToString() : value.Trim();
}

public static class TeamLabTrafficIngestionLimits
{
    public const int MaxBatchSamples = 1000;
    public const int MaxBatchBytes = 1024 * 1024;
}

public sealed record TeamLabTrafficEnqueueResult(
    int AcceptedCount,
    int BatchCount,
    int DroppedCount,
    bool UsedLocalBuffer);

public sealed record TeamLabTrafficIngestMessage(
    string? StreamId,
    TeamLabTrafficEnvelope Envelope);

public sealed record TeamLabTrafficReadBatch(
    IReadOnlyList<TeamLabTrafficIngestMessage> Messages)
{
    public static readonly TeamLabTrafficReadBatch Empty = new([]);
}

public interface ITeamLabTrafficIngestor
{
    ValueTask<TeamLabTrafficEnqueueResult> EnqueueAsync(
        IReadOnlyCollection<TeamLabTrafficEnvelope> envelopes,
        CancellationToken cancellationToken);

    ValueTask<TeamLabTrafficReadBatch> ReadAsync(
        string consumerName,
        int maxCount,
        TimeSpan reclaimIdle,
        CancellationToken cancellationToken);

    ValueTask AcknowledgeAsync(
        IReadOnlyCollection<string> streamIds,
        CancellationToken cancellationToken);
}
