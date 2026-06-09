namespace GZCTF.Services;

public static class AwdpPatchStateResolver
{
    public static AwdpPatchSubmission? GetEffectivePatch(int serviceId, int teamId,
        IEnumerable<AwdpPatchSubmission> patchSubmissions, IEnumerable<AwdpResetRecord> resetRecords,
        IEnumerable<AwdpRecoveryRecord> recoveryRecords, DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null)
    {
        var boundary = GetLatestResetOrRecoveryTime(serviceId, teamId, resetRecords, recoveryRecords, windowStart,
            windowEnd);

        return patchSubmissions
            .Where(p => p.ServiceId == serviceId && p.TeamId == teamId &&
                        IsInWindow(p.SubmittedAt, windowStart, windowEnd) &&
                        (!boundary.HasValue || p.SubmittedAt > boundary.Value))
            .OrderByDescending(p => p.SubmittedAt)
            .FirstOrDefault();
    }

    public static CheckerStatus? ResolveLatestCheckerStatus(int serviceId, int teamId,
        IEnumerable<AwdpCheckerTask> checkerTasks, IEnumerable<AwdpPatchSubmission> patchSubmissions,
        IEnumerable<AwdpResetRecord> resetRecords, IEnumerable<AwdpRecoveryRecord> recoveryRecords,
        DateTimeOffset? windowStart = null, DateTimeOffset? windowEnd = null)
    {
        var checker = checkerTasks
            .Where(t => t.ServiceId == serviceId && t.TeamId == teamId)
            .OrderByDescending(t => t.ExecutedAt)
            .FirstOrDefault();
        var patch = GetEffectivePatch(serviceId, teamId, patchSubmissions, resetRecords, recoveryRecords,
            windowStart, windowEnd);

        if (patch is not null && (checker is null || patch.SubmittedAt >= checker.ExecutedAt))
            return patch.CheckerResult;

        return checker?.Status;
    }

    static DateTimeOffset? GetLatestResetOrRecoveryTime(int serviceId, int teamId,
        IEnumerable<AwdpResetRecord> resetRecords, IEnumerable<AwdpRecoveryRecord> recoveryRecords,
        DateTimeOffset? windowStart, DateTimeOffset? windowEnd)
    {
        var latestReset = resetRecords
            .Where(r => r.ServiceId == serviceId && r.TeamId == teamId &&
                        IsInWindow(r.ResetAt, windowStart, windowEnd))
            .Select(r => (DateTimeOffset?)r.ResetAt)
            .Max();
        var latestRecovery = recoveryRecords
            .Where(r => r.ServiceId == serviceId && r.TeamId == teamId &&
                        IsInWindow(r.RecoveryAt, windowStart, windowEnd))
            .Select(r => (DateTimeOffset?)r.RecoveryAt)
            .Max();

        if (latestReset is null)
            return latestRecovery;

        if (latestRecovery is null)
            return latestReset;

        return latestReset > latestRecovery ? latestReset : latestRecovery;
    }

    static bool IsInWindow(DateTimeOffset value, DateTimeOffset? windowStart, DateTimeOffset? windowEnd) =>
        (!windowStart.HasValue || value >= windowStart.Value) &&
        (!windowEnd.HasValue || value <= windowEnd.Value);
}
