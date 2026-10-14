using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Infrastructure.Telemetry;

namespace GZCTF.Modules.Audit.Infrastructure;

public sealed class EfOperationalEventWriter(
    AppDbContext context,
    ILogger<EfOperationalEventWriter> logger,
    IHttpContextAccessor? httpContextAccessor = null) : IOperationalEventWriter
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
        "leaseCount", "pathCount", "packetExactCount", "rejectedCount",
        "rebootCount", "assetKind", "assetKey", "infrastructureKind", "evidenceKind",
        "placementElapsedMs", "placementGroupCount", "placementEdgeCount", "placementImprovementPasses",
        "stateless", "result", "count", "reason", "errorCode", "remoteSessionId",
        "assetId", "protocol", "actorUserId", "auditFileId", "filePath", "destinationPath", "ticketId",
        "tail", "truncated", "logsAvailable",
        "protocolEventType", "protocolEventSource", "protocolEventOccurredAt",
        "protocolEventParameterCount", "protocolEventParameters",
        "actorKind", "actorDisplayName", "remoteIp", "workerNodeName"
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
        // Keep the caller's existing 4 KiB budget separate from bounded identity snapshots.
        _ = SerializeDetail(draft.Detail);
        var correlationId = draft.CorrelationId ?? Guid.CreateVersion7();
        var request = httpContextAccessor?.HttpContext;
        var requestActorId = Guid.TryParse(request?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var requestId) ? requestId : (Guid?)null;
        var actorId = draft.ActorUserId ?? (draft.ActorKind is "System" or "Agent" ? null : requestActorId);
        var requestMatchesActor = draft.ActorKind is not "System" and not "Agent" &&
            (draft.ActorUserId is null || draft.ActorUserId == requestActorId);
        var actorKind = draft.ActorKind ?? (actorId is not null ? "User" :
            request is null ? "System" : request.User.Identity?.IsAuthenticated == true ? "User" : "Anonymous");
        if (draft.ActorKind is null && requestMatchesActor && request?.User.HasClaim(claim =>
                claim.Type == GZCTF.Modules.Identity.Application.ApiTokenClaimTypes.TokenId) == true)
            actorKind = "ApiToken";
        var actorName = draft.ActorDisplayName ??
            (requestMatchesActor ? request?.User.Identity?.Name : null) ??
            (actorKind == "System" ? "System" : null);
        var ip = requestMatchesActor || draft.ActorKind == "Agent" ? request?.Connection.RemoteIpAddress : null;
        var nodeName = draft.WorkerNodeName ??
            (draft.ResourceType == "worker-node" ? draft.ResourceDisplayName : null);
        var detail = draft.Detail?.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in detail.Keys.Where(key => key.Equals("actorUserId", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("actorKind", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("actorDisplayName", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("remoteIp", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("workerNodeName", StringComparison.OrdinalIgnoreCase)).ToArray())
            detail.Remove(key);
        if (actorId is not null) detail["actorUserId"] = actorId;
        detail["actorKind"] = actorKind;
        if (actorName is not null) detail["actorDisplayName"] = SanitizeText(actorName, Limits.MaxUserNameLength);
        if (ip is not null) detail["remoteIp"] = ip.ToString();
        if (nodeName is not null) detail["workerNodeName"] = SanitizeText(nodeName, 128);
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
            DetailJson = SerializeDetail(detail, 8192),
            ActorUserId = actorId,
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
            SubjectDisplayName = draft.SubjectDisplayName is null ? null : SanitizeText(draft.SubjectDisplayName, MaxDisplayNameLength),
            ResourceType = Trim(draft.ResourceType, 64),
            ResourceId = Trim(draft.ResourceId, 128),
            ResourceDisplayName = draft.ResourceDisplayName is null ? null : SanitizeText(draft.ResourceDisplayName, MaxDisplayNameLength)
        };

        context.Set<OperationalEvent>().Add(entity);
        PlatformTelemetry.RecordEvent(entity.EventCode, entity.Outcome);
        WriteStructuredLog(entity, actorKind, actorName, ip, nodeName);
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

    private static string? SerializeDetail(IReadOnlyDictionary<string, object?>? detail,
        int maxLength = MaxDetailLength)
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
        if (json.Length > maxLength)
            throw new ArgumentException($"Operational event detail exceeds {maxLength} characters.", nameof(detail));
        return json;
    }

    private static object? SanitizeDetailValue(object? value) => value switch
    {
        string text => SanitizeText(text, MaxDetailLength),
        IEnumerable<string> values => values.Select(item => SanitizeText(item, MaxDetailLength)).ToArray(),
        _ => value
    };

    internal static string SanitizeText(string value, int maxLength)
    {
        var sanitized = BearerPattern.Replace(value, "Bearer [REDACTED]");
        sanitized = SensitiveAssignmentPattern.Replace(sanitized,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}[REDACTED]");
        return Trim(sanitized, maxLength) ?? string.Empty;
    }

    private void WriteStructuredLog(OperationalEvent entity, string actorKind, string? actorName,
        System.Net.IPAddress? ip, string? nodeName)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = entity.CorrelationId,
            ["TraceId"] = entity.TraceId,
            ["EventCode"] = entity.EventCode,
            ["ActorUserId"] = entity.ActorUserId,
            ["ActorKind"] = actorKind,
            ["UserName"] = actorName is null ? null : SanitizeText(actorName, Limits.MaxUserNameLength),
            ["IP"] = ip,
            ["Outcome"] = entity.Outcome.ToString(),
            ["Retryable"] = entity.ErrorCategory is null ? null : (bool?)entity.Retryable,
            ["DetailJson"] = entity.DetailJson,
            ["Status"] = entity.Outcome switch
            {
                OperationalEventOutcome.Started or OperationalEventOutcome.Pending => (TaskStatus?)TaskStatus.Pending,
                OperationalEventOutcome.Blocked => TaskStatus.Degraded,
                OperationalEventOutcome.Succeeded => TaskStatus.Success,
                OperationalEventOutcome.Failed => TaskStatus.Failed,
                _ => null
            },
            ["ErrorCategory"] = entity.ErrorCategory?.ToString(),
            ["ErrorCode"] = entity.ErrorCode,
            ["WorkerNodeId"] = entity.WorkerNodeId,
            ["WorkerNodeName"] = nodeName is null ? null : SanitizeText(nodeName, 128),
            ["DeploymentTicketId"] = entity.DeploymentTicketId,
            ["ResourceType"] = entity.ResourceType,
            ["ResourceId"] = entity.ResourceId,
            ["ResourceDisplayName"] = entity.ResourceDisplayName
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
