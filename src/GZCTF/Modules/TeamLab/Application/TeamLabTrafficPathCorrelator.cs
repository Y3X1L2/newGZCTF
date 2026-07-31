using System.Security.Cryptography;
using System.Text;
using GZCTF.Infrastructure.Concurrency;
using GZCTF.Models;
using GZCTF.Modules.TeamLab.Domain;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabTrafficPathCorrelator(
    AppDbContext context,
    IDistributedLeaseProvider locks,
    TeamLabEventRecorder eventRecorder,
    ILogger<TeamLabTrafficPathCorrelator> logger)
{
    private const int BatchSize = 500;
    private static readonly TimeSpan PacketWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProcessWindow = TimeSpan.FromSeconds(10);

    internal async Task<int> CorrelatePendingAsync(CancellationToken cancellationToken)
    {
        var sourceRows = await context.TeamLabTrafficObservations.AsNoTracking()
            .Where(observation => !context.TeamLabTrafficCorrelationCursors.Any(cursor =>
                cursor.RuntimeId == observation.RuntimeId &&
                cursor.Generation == observation.Generation &&
                cursor.LastObservationId >= observation.Id))
            .Select(observation => new
            {
                observation.RuntimeId,
                observation.Generation,
                LastScannedAt = context.TeamLabTrafficCorrelationCursors
                    .Where(cursor => cursor.RuntimeId == observation.RuntimeId &&
                                     cursor.Generation == observation.Generation)
                    .Select(cursor => (DateTimeOffset?)cursor.UpdatedAt)
                    .FirstOrDefault()
            })
            .Distinct()
            .OrderBy(source => source.LastScannedAt ?? DateTimeOffset.MinValue)
            .ThenBy(source => source.RuntimeId)
            .ThenBy(source => source.Generation)
            .Take(100)
            .ToArrayAsync(cancellationToken);
        var sources = sourceRows
            .Select(source => new CorrelationSource(source.RuntimeId, source.Generation))
            .ToArray();

        var created = 0;
        foreach (var source in sources)
        {
            try
            {
                created += await CorrelateSourceAsync(source, cancellationToken);
            }
            catch (TimeoutException)
            {
                // Another application instance owns this correlation lease.
            }
        }
        return created;
    }

    private async Task<int> CorrelateSourceAsync(
        CorrelationSource source,
        CancellationToken cancellationToken)
    {
        await using var correlationLock = await locks.AcquireAsync(
            $"teamlab:traffic:path:{source.RuntimeId}:{source.Generation}",
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(15),
            cancellationToken);
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, correlationLock.LeaseLost);
        cancellationToken = leaseCancellation.Token;

        var cursor = await context.TeamLabTrafficCorrelationCursors.SingleOrDefaultAsync(
            item => item.RuntimeId == source.RuntimeId && item.Generation == source.Generation,
            cancellationToken);
        if (cursor is null)
        {
            cursor = new TeamLabTrafficCorrelationCursor
            {
                RuntimeId = source.RuntimeId,
                Generation = source.Generation
            };
            context.TeamLabTrafficCorrelationCursors.Add(cursor);
        }

        var pending = await context.TeamLabTrafficObservations.AsNoTracking()
            .Where(item => item.RuntimeId == source.RuntimeId && item.Generation == source.Generation &&
                           item.Id > cursor.LastObservationId)
            .OrderBy(item => item.Id)
            .Take(BatchSize)
            .ToArrayAsync(cancellationToken);
        if (pending.Length == 0)
            return 0;

        var packetFingerprints = pending
            .Where(item => item.PacketFingerprint is { Length: > 0 })
            .Select(item => item.PacketFingerprint!)
            .Distinct(ByteArrayComparer.Instance)
            .ToArray();
        var processHashes = pending
            .Where(item => item.ProcessIdentityHash is { Length: > 0 })
            .Select(item => item.ProcessIdentityHash!)
            .Distinct(ByteArrayComparer.Instance)
            .ToArray();
        var earliest = pending.Min(item => item.ObservedAt);
        var latest = pending.Max(item => item.ObservedAt);

        var packetCandidates = packetFingerprints.Length == 0
            ? []
            : await context.TeamLabTrafficObservations.AsNoTracking()
                .Where(item => item.RuntimeId == source.RuntimeId && item.Generation == source.Generation &&
                               item.PacketFingerprint != null &&
                               packetFingerprints.Contains(item.PacketFingerprint) &&
                               item.ObservedAt >= earliest - PacketWindow &&
                               item.ObservedAt <= latest + PacketWindow)
                .OrderBy(item => item.ObservedAt)
                .ThenBy(item => item.Id)
                .ToArrayAsync(cancellationToken);
        var processCandidates = processHashes.Length == 0
            ? []
            : await context.TeamLabTrafficObservations.AsNoTracking()
                .Where(item => item.RuntimeId == source.RuntimeId && item.Generation == source.Generation &&
                               item.ProcessIdentityHash != null &&
                               processHashes.Contains(item.ProcessIdentityHash) &&
                               item.ObservedAt >= earliest - ProcessWindow &&
                               item.ObservedAt <= latest + ProcessWindow)
                .OrderBy(item => item.ObservedAt)
                .ThenBy(item => item.Id)
                .ToArrayAsync(cancellationToken);

        var paths = BuildPacketPaths(source, packetCandidates)
            .Concat(BuildTemporalProcessPaths(source, processCandidates))
            .ToArray();
        if (paths.Length > 0)
        {
            var fingerprints = paths.Select(item => item.EvidenceFingerprint).ToArray();
            var existing = await context.TeamLabTrafficPaths.AsNoTracking()
                .Where(item => item.RuntimeId == source.RuntimeId && item.Generation == source.Generation &&
                               fingerprints.Contains(item.EvidenceFingerprint))
                .Select(item => item.EvidenceFingerprint)
                .ToArrayAsync(cancellationToken);
            var existingSet = existing.ToHashSet(ByteArrayComparer.Instance);
            context.TeamLabTrafficPaths.AddRange(paths.Where(item => !existingSet.Contains(item.EvidenceFingerprint)));
        }

        cursor.LastObservationId = NextScanCursor(pending, cursor.LastObservationId);
        cursor.UpdatedAt = DateTimeOffset.UtcNow;
        var created = context.ChangeTracker.Entries<TeamLabTrafficPath>()
            .Count(entry => entry.State == EntityState.Added);
        if (created > 0)
        {
            var runtime = await context.TeamLabRuntimes.SingleOrDefaultAsync(
                item => item.Id == source.RuntimeId,
                cancellationToken);
            var pathEntries = context.ChangeTracker.Entries<TeamLabTrafficPath>()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity)
                .ToArray();
            if (runtime is not null)
                eventRecorder.Record(
                    runtime,
                    "traffic-path",
                    TeamLabEventLevel.Info,
                    OperationalEventCodes.TeamLab.TrafficPathDerived,
                    OperationalEventOutcome.Observed,
                    "Traffic paths were derived from observation evidence.",
                    detail: new Dictionary<string, object?>
                    {
                        ["generation"] = source.Generation,
                        ["stage"] = "traffic-path",
                        ["pathCount"] = created,
                        ["packetExactCount"] = pathEntries.Count(item => item.Confidence == TeamLabPathConfidence.PacketExact),
                        ["processCorrelatedCount"] = pathEntries.Count(item => item.Confidence == TeamLabPathConfidence.ProcessCorrelated),
                        ["temporalCount"] = pathEntries.Count(item => item.Confidence == TeamLabPathConfidence.TemporallyRelated)
                    });
            foreach (var group in pathEntries.GroupBy(item => item.Confidence))
                PlatformTelemetry.RecordTeamLabObservation(
                    "path-derived", group.Key.ToString(), group.LongCount());
        }
        await context.SaveChangesAsync(cancellationToken);
        if (created > 0)
            logger.LogDebug(
                "Derived {Count} TeamLab traffic path(s) for runtime {RuntimeId} generation {Generation}",
                created, source.RuntimeId, source.Generation);
        return created;
    }

    internal static IReadOnlyList<TeamLabTrafficPath> BuildPacketPaths(
        int runtimeId,
        int generation,
        IReadOnlyCollection<TeamLabTrafficObservation> observations) =>
        BuildPacketPaths(new CorrelationSource(runtimeId, generation), observations).ToArray();

    internal static IReadOnlyList<TeamLabTrafficPath> BuildTemporalProcessPaths(
        int runtimeId,
        int generation,
        IReadOnlyCollection<TeamLabTrafficObservation> observations) =>
        BuildTemporalProcessPaths(new CorrelationSource(runtimeId, generation), observations).ToArray();

    internal static long NextScanCursor(
        IReadOnlyCollection<TeamLabTrafficObservation> observations,
        long current = 0) =>
        observations.Count == 0 ? current : Math.Max(current, observations.Max(item => item.Id));

    private static IEnumerable<TeamLabTrafficPath> BuildPacketPaths(
        CorrelationSource source,
        IReadOnlyCollection<TeamLabTrafficObservation> observations)
    {
        foreach (var group in observations
                     .Where(item => item.PacketFingerprint is { Length: > 0 })
                     .GroupBy(item => item.PacketFingerprint!, ByteArrayComparer.Instance))
        {
            var ordered = group.OrderBy(item => item.ObservedAt).ThenBy(item => item.Id).ToArray();
            if (ordered.Length < 2 || ordered[^1].ObservedAt - ordered[0].ObservedAt > PacketWindow)
                continue;
            yield return CreatePath(source, TeamLabPathConfidence.PacketExact, ordered);
        }
    }

    private static IEnumerable<TeamLabTrafficPath> BuildTemporalProcessPaths(
        CorrelationSource source,
        IReadOnlyCollection<TeamLabTrafficObservation> observations)
    {
        foreach (var group in observations
                     .Where(item => item.ProcessIdentityHash is { Length: > 0 })
                     .GroupBy(item => item.ProcessIdentityHash!, ByteArrayComparer.Instance))
        {
            var ordered = group.OrderBy(item => item.ObservedAt).ThenBy(item => item.Id)
                .GroupBy(item => item.FlowFingerprint, ByteArrayComparer.Instance)
                .Select(item => item.First())
                .OrderBy(item => item.ObservedAt)
                .ThenBy(item => item.Id)
                .ToArray();
            if (ordered.Length < 2 || ordered[^1].ObservedAt - ordered[0].ObservedAt > ProcessWindow)
                continue;
            var confidence = HasDirectedProcessTransition(ordered)
                ? TeamLabPathConfidence.ProcessCorrelated
                : TeamLabPathConfidence.TemporallyRelated;
            yield return CreatePath(source, confidence, ordered);
        }
    }

    private static bool HasDirectedProcessTransition(IReadOnlyList<TeamLabTrafficObservation> observations)
    {
        var accepted = false;
        foreach (var observation in observations)
        {
            if (IsInboundProcessEvent(observation.Direction))
            {
                accepted = true;
                continue;
            }
            if (accepted && IsOutboundProcessEvent(observation.Direction))
                return true;
        }
        return false;
    }

    private static bool IsInboundProcessEvent(string value) =>
        value.Equals("accept", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("accepted", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("inbound", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("received", StringComparison.OrdinalIgnoreCase);

    private static bool IsOutboundProcessEvent(string value) =>
        value.Equals("connect", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("connected", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("outbound", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("opened", StringComparison.OrdinalIgnoreCase);

    private static TeamLabTrafficPath CreatePath(
        CorrelationSource source,
        TeamLabPathConfidence confidence,
        IReadOnlyList<TeamLabTrafficObservation> observations)
    {
        var evidence = string.Join('|', observations.Select(item => item.Id).Prepend((long)(byte)confidence));
        var first = observations[0];
        return new TeamLabTrafficPath
        {
            RuntimeId = source.RuntimeId,
            Generation = source.Generation,
            Confidence = confidence,
            EvidenceFingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(evidence)),
            SourceIp = first.SourceIp,
            SourcePort = first.SourcePort,
            DestinationIp = first.DestinationIp,
            DestinationPort = first.DestinationPort,
            Protocol = first.Protocol,
            StartedAt = observations[0].ObservedAt,
            EndedAt = observations[^1].ObservedAt,
            Hops = observations.Select((item, ordinal) => new TeamLabTrafficPathHop
            {
                Ordinal = ordinal,
                ObservationId = item.Id,
                ObservationPointId = item.ObservationPointId,
                ObservedAt = item.ObservedAt,
                EvidenceKind = item.EvidenceKind,
                Direction = item.Direction,
                SourceIp = item.SourceIp,
                SourcePort = item.SourcePort,
                DestinationIp = item.DestinationIp,
                DestinationPort = item.DestinationPort,
                Protocol = item.Protocol
            }).ToList()
        };
    }

    private sealed record CorrelationSource(int RuntimeId, int Generation);

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();
        public bool Equals(byte[]? left, byte[]? right) =>
            ReferenceEquals(left, right) || left is not null && right is not null && left.AsSpan().SequenceEqual(right);
        public int GetHashCode(byte[] value)
        {
            var hash = new HashCode();
            hash.AddBytes(value);
            return hash.ToHashCode();
        }
    }
}
