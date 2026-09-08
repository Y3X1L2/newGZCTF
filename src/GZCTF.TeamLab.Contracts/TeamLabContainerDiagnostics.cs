namespace GZCTF.TeamLab.Contracts;

public sealed record TeamLabContainerDiagnosticsRequest(int RuntimeId, int Generation, string ContainerId, int Tail);

public sealed record TeamLabContainerDiagnostics(
    string State, bool Paused, long ExitCode, long RestartCount,
    string StartedAt, string FinishedAt, string Logs, bool Truncated, DateTimeOffset ObservedAt,
    string? LogsError = null);
