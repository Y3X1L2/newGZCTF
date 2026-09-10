namespace GZCTF.TeamLab.Contracts;

public sealed record TeamLabVmDiagnosticsRequest(string DomainName, int Generation, Guid NativeId);
public sealed record TeamLabVmDiagnostics(string State, Guid NativeId, DateTimeOffset ObservedAt);
