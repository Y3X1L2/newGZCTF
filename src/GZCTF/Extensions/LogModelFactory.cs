using System.Net;
using Serilog.Events;

namespace GZCTF.Extensions;

internal static class LogModelFactory
{
    public static LogModel FromLogEvent(LogEvent logEvent)
    {
        logEvent.Properties.TryGetValue("UserName", out var userName);
        logEvent.Properties.TryGetValue("SourceContext", out var sourceContext);
        logEvent.Properties.TryGetValue("IP", out var ip);
        logEvent.Properties.TryGetValue("Status", out var status);
        logEvent.Properties.TryGetValue("CorrelationId", out var correlationId);
        logEvent.Properties.TryGetValue("TraceId", out var traceId);
        logEvent.Properties.TryGetValue("EventCode", out var eventCode);
        logEvent.Properties.TryGetValue("ErrorCategory", out var errorCategory);
        logEvent.Properties.TryGetValue("ErrorCode", out var errorCode);
        logEvent.Properties.TryGetValue("WorkerNodeId", out var workerNodeId);
        logEvent.Properties.TryGetValue("WorkerNodeName", out var workerNodeName);
        logEvent.Properties.TryGetValue("DeploymentTicketId", out var deploymentTicketId);
        logEvent.Properties.TryGetValue("ResourceType", out var resourceType);
        logEvent.Properties.TryGetValue("ResourceId", out var resourceId);
        logEvent.Properties.TryGetValue("ResourceDisplayName", out var resourceDisplayName);
        logEvent.Properties.TryGetValue("ActorUserId", out var actorUserId);
        logEvent.Properties.TryGetValue("ActorKind", out var actorKind);
        logEvent.Properties.TryGetValue("Outcome", out var outcome);
        logEvent.Properties.TryGetValue("Retryable", out var retryable);
        logEvent.Properties.TryGetValue("DetailJson", out var detailJson);

        return new LogModel
        {
            TimeUtc = logEvent.Timestamp.ToUniversalTime(),
            Level = logEvent.Level.ToString(),
            Message = logEvent.RenderMessageWithExceptions(),
            UserName = LogHelper.GetLogPropertyValue<string>(userName, null),
            Logger = LogHelper.GetLogPropertyValue<string>(sourceContext, "Unknown") ?? string.Empty,
            RemoteIP = LogHelper.GetLogPropertyValue<IPAddress>(ip, null),
            Status = logEvent.Exception is null
                ? LogHelper.GetLogPropertyValue<TaskStatus?>(status, null)
                : TaskStatus.Failed,
            Exception = logEvent.Exception?.ToString(),
            CorrelationId = LogHelper.GetLogPropertyValue<Guid?>(correlationId, null),
            TraceId = LogHelper.GetLogPropertyValue<string>(traceId, null),
            EventCode = LogHelper.GetLogPropertyValue<string>(eventCode, null),
            ErrorCategory = LogHelper.GetLogPropertyValue<string>(errorCategory, null),
            ErrorCode = LogHelper.GetLogPropertyValue<string>(errorCode, null),
            WorkerNodeId = LogHelper.GetLogPropertyValue<Guid?>(workerNodeId, null),
            WorkerNodeName = LogHelper.GetLogPropertyValue<string>(workerNodeName, null),
            DeploymentTicketId = LogHelper.GetLogPropertyValue<Guid?>(deploymentTicketId, null),
            ResourceType = LogHelper.GetLogPropertyValue<string>(resourceType, null),
            ResourceId = LogHelper.GetLogPropertyValue<string>(resourceId, null),
            ResourceDisplayName = LogHelper.GetLogPropertyValue<string>(resourceDisplayName, null),
            ActorUserId = LogHelper.GetLogPropertyValue<Guid?>(actorUserId, null),
            ActorKind = LogHelper.GetLogPropertyValue<string>(actorKind, null),
            Outcome = LogHelper.GetLogPropertyValue<string>(outcome, null),
            Retryable = LogHelper.GetLogPropertyValue<bool?>(retryable, null),
            DetailJson = LogHelper.GetLogPropertyValue<string>(detailJson, null)
        };
    }
}
