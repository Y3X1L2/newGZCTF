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

        return new LogModel
        {
            TimeUtc = logEvent.Timestamp.ToUniversalTime(),
            Level = logEvent.Level.ToString(),
            Message = logEvent.RenderMessageWithExceptions(),
            UserName = LogHelper.GetLogPropertyValue(userName, "Anonymous"),
            Logger = LogHelper.GetLogPropertyValue<string>(sourceContext, "Unknown") ?? string.Empty,
            RemoteIP = LogHelper.GetLogPropertyValue<IPAddress>(ip, null),
            Status = logEvent.Exception is null
                ? LogHelper.GetLogPropertyValue(status, TaskStatus.Success)
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
            ResourceDisplayName = LogHelper.GetLogPropertyValue<string>(resourceDisplayName, null)
        };
    }
}
