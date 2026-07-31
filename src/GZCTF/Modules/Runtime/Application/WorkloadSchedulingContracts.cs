namespace GZCTF.Modules.Runtime.Application;

public sealed record WorkloadSchedulingIdentity
{
    public string TenantKey { get; }
    public string FairnessKey { get; }
    public string SubjectConcurrencyKey { get; }

    public WorkloadSchedulingIdentity(
        string tenantKey,
        string fairnessKey,
        string subjectConcurrencyKey)
    {
        TenantKey = Required(tenantKey, nameof(tenantKey));
        FairnessKey = Required(fairnessKey, nameof(fairnessKey));
        SubjectConcurrencyKey = Required(subjectConcurrencyKey, nameof(subjectConcurrencyKey));
    }

    public static WorkloadSchedulingIdentity ForCompetitionTeam(
        int gameId,
        int teamId,
        string subject) =>
        new($"competition:{gameId}", $"team:{teamId}", subject);

    public static WorkloadSchedulingIdentity ForUser(Guid userId, string subject, int? gameId = null) =>
        new(gameId is { } id ? $"competition:{id}" : $"user:{userId:D}", $"user:{userId:D}", subject);

    public static WorkloadSchedulingIdentity ForTeam(int teamId, string subject) =>
        new($"team:{teamId}", $"team:{teamId}", subject);

    public static WorkloadSchedulingIdentity ForRuntime(int runtimeId, string subject, Guid? ownerUserId = null) =>
        new($"teamlab-runtime:{runtimeId}",
            ownerUserId is { } userId ? $"user:{userId:D}" : $"teamlab-runtime:{runtimeId}",
            subject);

    public static WorkloadSchedulingIdentity ForSystem(string subject) =>
        new("system", "system", subject);

    private static string Required(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("Scheduling identity keys cannot be empty.", parameterName);
}
