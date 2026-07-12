using GZCTF.Models.Data;

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

public sealed record CreateTeamLabCaptureModel(
    string Scope,
    string? NetworkKey,
    int MaxSeconds,
    long MaxBytes,
    int ExpiresInSeconds);

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
    string? Error);
