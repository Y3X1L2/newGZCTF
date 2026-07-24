using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Domain;

namespace GZCTF.Modules.TeamLab.Contracts;

public sealed record TeamLabTrafficFlowProjectionModel(
    string Cursor,
    Guid ShardId,
    string NetworkKey,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    long Bytes,
    long Packets,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);

public sealed record TeamLabTrafficFlowPageModel(
    IReadOnlyList<TeamLabTrafficFlowProjectionModel> Items,
    string? NextCursor);

public sealed record TeamLabTrafficPathSummaryModel(
    string Cursor,
    Guid Id,
    TeamLabPathConfidence Confidence,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int HopCount);

public sealed record TeamLabTrafficPathPageModel(
    IReadOnlyList<TeamLabTrafficPathSummaryModel> Items,
    string? NextCursor);

public sealed record TeamLabTrafficPathHopModel(
    int Ordinal,
    DateTimeOffset ObservedAt,
    TeamLabTrafficEvidenceKind EvidenceKind,
    TeamLabObservationPointKind ObservationPointKind,
    Guid? ShardId,
    string? NetworkKey,
    string? InfrastructureKey,
    string? AssetKey,
    string Direction,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol);

public sealed record TeamLabTrafficPathModel(
    Guid Id,
    TeamLabPathConfidence Confidence,
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    IReadOnlyList<TeamLabTrafficPathHopModel> Hops);

public sealed record CreateTeamLabCaptureModel(
    string Scope,
    string? NetworkKey,
    int MaxSeconds,
    long MaxBytes,
    int ExpiresInSeconds);

public sealed record TeamLabCaptureSegmentModel(
    Guid Id,
    TeamLabTrafficCaptureSegmentStatus Status,
    Guid ObservationPointId,
    TeamLabObservationPointKind ObservationPointKind,
    string? NetworkKey,
    string? InfrastructureKey,
    string? AssetKey,
    long CapturedBytes,
    long UploadedBytes,
    string? Sha256,
    string? Error);

public sealed record TeamLabCaptureModel(
    Guid Id,
    TeamLabTrafficCaptureStatus Status,
    string Scope,
    string? NetworkKey,
    long MaxBytes,
    int MaxSeconds,
    long CapturedBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<TeamLabCaptureSegmentModel> Segments,
    string? Error);
