using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Infrastructure.Telemetry;

namespace GZCTF.Modules.Audit.Infrastructure;

public sealed class EfOperationalEventWriter(
    AppDbContext context,
    ILogger<EfOperationalEventWriter> logger) : IOperationalEventWriter
{
    private const int MaxMessageLength = 1024;
    private const int MaxDisplayNameLength = 256;
    private const int MaxDetailLength = 4096;
    private static readonly HashSet<string> AllowedDetailKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "attempt", "generation", "stage", "operation", "workload",
        "httpStatus", "durationMs", "queuePosition", "dockerSlots", "vmSlots",
        "cpuUnits", "memoryMiB", "storageMiB",
        "previousStatus", "currentStatus", "capability", "feature",
        "imageType", "digestPrefix", "sizeBytes", "routeCount", "assetCount",
        "shardCount", "decision", "reasonCode", "matchedCount", "missingCount",
        "conflictCount", "orphanCount", "deferredCount", "correctedCount", "replayedCount",
        "captureScope", "captureSegmentCount", "captureWorkerCount", "infrastructureCount",
        "leaseCount", "pathCount", "packetExactCount", "processCorrelatedCount", "temporalCount", "rejectedCount",
        "rebootCount", "assetKind", "assetKey", "infrastructureKind", "evidenceKind",
        "placementElapsedMs", "placementGroupCount", "placementEdgeCount", "placementImprovementPasses",
        "stateless", "result", "count", "reason", "errorCode", "remoteSessionId",
        "assetId", "protocol", "actorUserId",
        "protocolEventType", "protocolEventSource", "protocolEventOccurredAt",
        "protocolEventParameterCount", "protocolEventParameters"
    };
    private static readonly string[] SensitiveFragments =
    [
        "flag", "token", "authorization", "cookie", "password", "secret", "privatekey",
        "wireguardprivatekey", "userdata", "cloudinit", "registryauth", "command",
        "environment", "requestbody", "responsebody", "rdppassword", "sshprivatekey"
    ];
    private static readonly Regex SensitiveAssignmentPattern = new(
        @"\b(" + string.Join("|", SensitiveFragments.Select(Regex.Escape)) +
        @")\b\s*([:=])\s*(?:""[^""]*""|'[^']*'|[^\s,;}]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BearerPattern = new(
        @"\bBearer\s+[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public OperationalEvent Append(OperationalEventDraft draft)
    {
        Validate(draft);
        var correlationId = draft.CorrelationId ?? Guid.CreateVersion7();
        var entity = new OperationalEvent
        {
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId,
            TraceId = Activity.Current?.TraceId.ToString(),
            EventCode = draft.EventCode,
            Severity = draft.Severity,
            Outcome = draft.Outcome,
            ErrorCategory = draft.ErrorCategory,
            ErrorCode = Trim(draft.ErrorCode, 128),
            Retryable = draft.Retryable,
            Message = SanitizeText(draft.Message, MaxMessageLength),
            DetailJson = SerializeDetail(draft.Detail),
            ActorUserId = draft.ActorUserId,
            OwnerUserId = draft.OwnerUserId,
            OwnerTeamId = draft.OwnerTeamId,
            GameId = draft.GameId,
            CourseId = draft.CourseId,
            ChallengeId = draft.ChallengeId,
            ImageTemplateId = draft.ImageTemplateId,
            WorkerNodeId = draft.WorkerNodeId,
            DeploymentTicketId = draft.DeploymentTicketId,
            TeamLabRuntimeId = draft.TeamLabRuntimeId,
            VmInstanceId = draft.VmInstanceId,
            SubjectType = Trim(draft.SubjectType, 64),
            SubjectId = Trim(draft.SubjectId, 128),
            SubjectDisplayName = Trim(draft.SubjectDisplayName, MaxDisplayNameLength),
            ResourceType = Trim(draft.ResourceType, 64),
            ResourceId = Trim(draft.ResourceId, 128),
            ResourceDisplayName = Trim(draft.ResourceDisplayName, MaxDisplayNameLength)
        };

        context.Set<OperationalEvent>().Add(entity);
        PlatformTelemetry.RecordEvent(entity.EventCode, entity.Outcome);
        WriteStructuredLog(entity);
        return entity;
    }

    public async Task<OperationalEvent> AppendAndSaveAsync(
        OperationalEventDraft draft,
        CancellationToken token)
    {
        var entity = Append(draft);
        await context.SaveChangesAsync(token);
        return entity;
    }

    private static void Validate(OperationalEventDraft draft)
    {
        if (!OperationalEventCodes.IsDefined(draft.EventCode))
            throw new ArgumentException($"Unknown operational event code '{draft.EventCode}'.", nameof(draft));
        if (string.IsNullOrWhiteSpace(draft.Message))
            throw new ArgumentException("Operational event message is required.", nameof(draft));
        if (draft.Outcome == OperationalEventOutcome.Failed &&
            (draft.ErrorCategory is null || string.IsNullOrWhiteSpace(draft.ErrorCode)))
            throw new ArgumentException("Failed operational events require an error category and code.", nameof(draft));
        if (draft.ErrorCategory is not null && string.IsNullOrWhiteSpace(draft.ErrorCode))
            throw new ArgumentException("Operational error category requires an error code.", nameof(draft));
    }

    private static string? SerializeDetail(IReadOnlyDictionary<string, object?>? detail)
    {
        if (detail is null || detail.Count == 0)
            return null;

        foreach (var key in detail.Keys)
        {
            var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);
            if (!AllowedDetailKeys.Contains(key) ||
                SensitiveFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Operational event detail key '{key}' is not allowed.", nameof(detail));
        }

        var sanitized = detail.ToDictionary(
            item => item.Key,
            item => SanitizeDetailValue(item.Value),
            StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(sanitized);
        if (json.Length > MaxDetailLength)
            throw new ArgumentException($"Operational event detail exceeds {MaxDetailLength} characters.", nameof(detail));
        return json;
    }

    private static object? SanitizeDetailValue(object? value) => value switch
    {
        string text => SanitizeText(text, MaxDetailLength),
        IEnumerable<string> values => values.Select(item => SanitizeText(item, MaxDetailLength)).ToArray(),
        _ => value
    };

    private static string SanitizeText(string value, int maxLength)
    {
        var sanitized = BearerPattern.Replace(value, "Bearer [REDACTED]");
        sanitized = SensitiveAssignmentPattern.Replace(sanitized,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}[REDACTED]");
        return Trim(sanitized, maxLength) ?? string.Empty;
    }

    private void WriteStructuredLog(OperationalEvent entity)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = entity.CorrelationId,
            ["TraceId"] = entity.TraceId,
            ["EventCode"] = entity.EventCode,
            ["ErrorCategory"] = entity.ErrorCategory?.ToString(),
            ["ErrorCode"] = entity.ErrorCode,
            ["WorkerNodeId"] = entity.WorkerNodeId,
            ["DeploymentTicketId"] = entity.DeploymentTicketId,
            ["ResourceType"] = entity.ResourceType,
            ["ResourceId"] = entity.ResourceId
        });
        logger.Log(ToLogLevel(entity.Severity), "{OperationalEventMessage}", entity.Message);
    }

    private static LogLevel ToLogLevel(OperationalEventSeverity severity) => severity switch
    {
        OperationalEventSeverity.Debug => LogLevel.Debug,
        OperationalEventSeverity.Information => LogLevel.Information,
        OperationalEventSeverity.Warning => LogLevel.Warning,
        OperationalEventSeverity.Error => LogLevel.Error,
        OperationalEventSeverity.Critical => LogLevel.Critical,
        _ => LogLevel.Information
    };

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
