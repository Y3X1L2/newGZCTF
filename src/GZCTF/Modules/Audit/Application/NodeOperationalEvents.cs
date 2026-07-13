using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Audit.Application;

public static class NodeOperationalEvents
{
    public static OperationalEventDraft Create(
        WorkerNode node,
        string eventCode,
        OperationalEventOutcome outcome,
        string message,
        OperationalEventSeverity severity = OperationalEventSeverity.Information,
        OperationalError? error = null,
        Guid? correlationId = null,
        IReadOnlyDictionary<string, object?>? detail = null) =>
        new(
            eventCode,
            outcome,
            message,
            severity,
            correlationId,
            error?.Category,
            error?.Code,
            error?.Retryable ?? false,
            detail,
            WorkerNodeId: node.Id,
            SubjectType: "worker-node",
            SubjectId: node.Id.ToString(),
            SubjectDisplayName: node.Name,
            ResourceType: "worker-node",
            ResourceId: node.Id.ToString(),
            ResourceDisplayName: node.Name);
}
