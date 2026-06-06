using System.Diagnostics;
using System.Text.Json;

namespace GZCTF.Services;

/// <summary>
/// Structured audit logging service with trace ID for full-chain tracing.
/// Logs all scenario and IR challenge operations per Constitution Principle V.
/// </summary>
public class AuditLogService
{
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ILogger<AuditLogService> logger)
    {
        _logger = logger;
    }

    public void LogOperation(string operation, string entityType, int entityId, string userId, object? details = null)
    {
        var traceId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        var logEntry = new
        {
            TraceId = traceId,
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            EntityType = entityType,
            EntityId = entityId,
            UserId = userId,
            Details = details
        };

        _logger.LogInformation("AUDIT {Operation} {EntityType}#{EntityId} by {UserId} | Trace: {TraceId} | {Details}",
            operation, entityType, entityId, userId, traceId,
            details is not null ? JsonSerializer.Serialize(details) : "-");
    }
}
