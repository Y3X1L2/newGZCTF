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
    int? NetworkId,
    int ObservationPointId,
    byte ObservationPointKind,
    int? AssetId,
    Guid WorkerNodeId,
    DateTimeOffset CapturedAt,
    long SourceSequence,
    string EvidenceFingerprint,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    byte? TcpFlags,
    int PacketLength,
    string? PacketFingerprint,
    string FlowFingerprint,
    string? ProcessIdentityHash,
    string EvidenceKind,
    string Direction,
    long Packets,
    long Bytes,
    DateTimeOffset? FirstSeenAt = null,
    DateTimeOffset? LastSeenAt = null)
{
    public const int CurrentSchemaVersion = 2;

    public static TeamLabTrafficEnvelope Create(
        int runtimeId,
        int generation,
        int? shardId,
        int? networkId,
        int observationPointId,
        TeamLabObservationPointKind observationPointKind,
        int? assetId,
        Guid workerNodeId,
        TeamLabNodeObservationRecord sample)
    {
        var sourceIp = NormalizeIp(sample.SourceIp);
        var destinationIp = NormalizeIp(sample.DestinationIp);
        var protocol = sample.Protocol.Trim().ToUpperInvariant();
        var capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(
            sample.CapturedAt.ToUniversalTime().ToUnixTimeMilliseconds());
        var packetFingerprint = NormalizeDigest(sample.PacketFingerprint);
        var flowFingerprint = NormalizeDigest(sample.FlowFingerprint)
                              ?? throw new ArgumentException("Observation flow fingerprint is missing.");
        var processIdentityHash = NormalizeDigest(sample.ProcessIdentityHash);
        var evidenceKind = sample.EvidenceKind.Trim();
        var firstSeenAt = sample.FirstSeenAt ?? capturedAt;
        var lastSeenAt = sample.LastSeenAt ?? capturedAt;
        var packets = Math.Max(1, sample.Packets);
        var bytes = Math.Max(0, sample.Bytes ?? sample.PacketLength);
        var evidenceInput = string.Join('|', runtimeId, generation, observationPointId, sample.Sequence,
            evidenceKind, packetFingerprint, flowFingerprint, processIdentityHash);

        return new TeamLabTrafficEnvelope(
            CurrentSchemaVersion,
            runtimeId,
            generation,
            shardId,
            networkId,
            observationPointId,
            (byte)observationPointKind,
            assetId,
            workerNodeId,
            capturedAt,
            sample.Sequence,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(evidenceInput))),
            sourceIp,
            sample.SourcePort,
            destinationIp,
            sample.DestinationPort,
            protocol,
            sample.TcpFlags,
            sample.PacketLength,
            packetFingerprint,
            flowFingerprint,
            processIdentityHash,
            evidenceKind,
            sample.Direction.Trim().ToLowerInvariant(),
            packets,
            bytes,
            firstSeenAt,
            lastSeenAt);
    }

    public int GetSerializedSize() => JsonSerializer.SerializeToUtf8Bytes(this).Length;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || RuntimeId <= 0 || Generation <= 0 ||
            ObservationPointId <= 0 || WorkerNodeId == Guid.Empty || CapturedAt == default || SourceSequence <= 0 ||
            EvidenceFingerprint.Length != 64 || !EvidenceFingerprint.All(Uri.IsHexDigit) ||
            SourceIp.Length is < 1 or > 64 || DestinationIp.Length is < 1 or > 64 ||
            Protocol.Length is < 1 or > 16 || EvidenceKind.Length is < 1 or > 32 ||
            Direction.Length is < 1 or > 16 || PacketLength < 0 || Packets < 1 || Bytes < 0 ||
            FirstSeenAt is { } firstSeenAt && LastSeenAt is { } lastSeenAt && firstSeenAt > lastSeenAt ||
            FlowFingerprint.Length != 64 || !FlowFingerprint.All(Uri.IsHexDigit) ||
            PacketFingerprint is not null &&
            (PacketFingerprint.Length != 64 || !PacketFingerprint.All(Uri.IsHexDigit)) ||
            ProcessIdentityHash is not null &&
            (ProcessIdentityHash.Length != 64 || !ProcessIdentityHash.All(Uri.IsHexDigit)) ||
            SourcePort is < 0 or > 65535 || DestinationPort is < 0 or > 65535)
            throw new ArgumentException("TeamLab traffic envelope is invalid.");
    }

    private static string NormalizeIp(string value) =>
        IPAddress.TryParse(value.Trim(), out var address) ? address.ToString() : value.Trim();

    private static string? NormalizeDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.StartsWith("sha256:", StringComparison.Ordinal)) normalized = normalized[7..];
        return normalized;
    }
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
    bool Deferred);

public sealed record TeamLabTrafficIngestMessage(
    string? StreamId,
    TeamLabTrafficEnvelope Envelope,
    long? LocalSequence = null);

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
        IReadOnlyCollection<long> localSequences,
        CancellationToken cancellationToken);
}
