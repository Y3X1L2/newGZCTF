using System.Diagnostics;
using GZCTF.Infrastructure.Telemetry;
using GZCTF.Models.Data;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Services.Fleet;

namespace GZCTF.Modules.Runtime.Application;

public static class RuntimeOperationalEvents
{
    public static OperationalEventDraft Ticket(
        DeploymentQueueTicket ticket,
        string eventCode,
        OperationalEventOutcome outcome,
        string message,
        OperationalEventSeverity severity = OperationalEventSeverity.Information,
        OperationalError? error = null,
        Guid? workerNodeId = null,
        IReadOnlyDictionary<string, object?>? detail = null) =>
        new(
            eventCode,
            outcome,
            message,
            severity,
            ticket.Id,
            error?.Category,
            error?.Code,
            error?.Retryable ?? false,
            detail ?? BaseDetail(ticket),
            OwnerUserId: ticket.OwnerUserId,
            OwnerTeamId: ticket.OwnerTeamId,
            GameId: ticket.GameId,
            ChallengeId: ticket.ChallengeId,
            WorkerNodeId: workerNodeId ?? ticket.TargetNodeId,
            DeploymentTicketId: ticket.Id,
            TeamLabRuntimeId: ticket.TeamLabRuntimeId,
            VmInstanceId: ticket.VmInstanceId,
            SubjectType: ticket.SubjectType ?? ticket.Kind.ToString(),
            SubjectId: ticket.SubjectPublicId ?? ticket.Id.ToString(),
            SubjectDisplayName: ticket.SubjectDisplayName,
            ResourceType: "deployment-ticket",
            ResourceId: ticket.Id.ToString(),
            ResourceDisplayName: ticket.ResourceDisplayName);

    public static OperationalError Failure(
        DeploymentQueueTicket ticket,
        string operation,
        Exception? exception = null,
        string? code = null,
        OperationalErrorCategory category = OperationalErrorCategory.Unknown,
        bool retryable = false)
    {
        if (exception is AgentClientException agent)
            return agent.Error with { Operation = operation, WorkerNodeId = ticket.TargetNodeId };
        if (exception is not null)
            return OperationalErrorClassifier.FromException(exception, operation, ticket.TargetNodeId);
        return new OperationalError(
            category,
            code ?? OperationalErrorCodes.UnclassifiedFailure,
            "Runtime operation failed.",
            retryable,
            WorkerNodeId: ticket.TargetNodeId,
            Operation: operation);
    }

    public static Activity? StartActivity(
        DeploymentQueueTicket ticket,
        string name,
        ActivityKind kind = ActivityKind.Consumer)
    {
        Activity? activity;
        if (ActivityContext.TryParse(ticket.TraceParent, ticket.TraceState, out var parent))
            activity = PlatformTelemetry.RuntimeActivitySource.StartActivity(name, kind, parent);
        else
            activity = PlatformTelemetry.RuntimeActivitySource.StartActivity(name, kind);
        activity?.SetTag("runtime.workload", ticket.Kind.ToString());
        activity?.SetTag("runtime.operation", ticket.Operation.ToString());
        activity?.SetTag("runtime.stage", ticket.Stage.ToString());
        activity?.SetTag("gzctf.deployment_ticket_id", ticket.Id.ToString());
        activity?.SetTag("gzctf.worker_node_id", ticket.TargetNodeId?.ToString());
        return activity;
    }

    public static IReadOnlyDictionary<string, object?> BaseDetail(DeploymentQueueTicket ticket) =>
        new Dictionary<string, object?>
        {
            ["workload"] = ticket.Kind.ToString(),
            ["operation"] = ticket.Operation.ToString(),
            ["stage"] = ticket.Stage.ToString(),
            ["attempt"] = ticket.AttemptCount,
            ["dockerSlots"] = ticket.DockerSlots,
            ["vmSlots"] = ticket.VmSlots
        };

    public static string ControlStartedCode(RuntimeOperationKind operation) => operation switch
    {
        RuntimeOperationKind.Extend => OperationalEventCodes.Runtime.ControlExtendStarted,
        RuntimeOperationKind.Stop => OperationalEventCodes.Runtime.ControlStopStarted,
        RuntimeOperationKind.Reset => OperationalEventCodes.Runtime.ControlResetStarted,
        RuntimeOperationKind.Destroy => OperationalEventCodes.Runtime.ControlDestroyStarted,
        _ => OperationalEventCodes.Runtime.ExecutionStarted
    };

    public static string? ResourceStartedCode(DeploymentQueueTicket ticket) =>
        ticket.Operation == RuntimeOperationKind.Create
            ? ticket.Kind == DeploymentQueueKind.VirtualMachine
                ? OperationalEventCodes.Vm.CreateStarted
                : ticket.Kind == DeploymentQueueKind.TeamLabRuntime
                    ? null
                    : OperationalEventCodes.Container.CreateStarted
            : null;

    public static string? ResourceSucceededCode(DeploymentQueueTicket ticket) => ticket.Kind switch
    {
        DeploymentQueueKind.VirtualMachine => ticket.Operation switch
        {
            RuntimeOperationKind.Create => OperationalEventCodes.Vm.CreateSucceeded,
            RuntimeOperationKind.Stop => OperationalEventCodes.Vm.StopSucceeded,
            RuntimeOperationKind.Destroy => OperationalEventCodes.Vm.DestroySucceeded,
            _ => null
        },
        DeploymentQueueKind.TeamLabRuntime => null,
        _ => ticket.Operation switch
        {
            RuntimeOperationKind.Create => OperationalEventCodes.Container.CreateSucceeded,
            RuntimeOperationKind.Stop => OperationalEventCodes.Container.StopSucceeded,
            RuntimeOperationKind.Destroy => OperationalEventCodes.Container.DestroySucceeded,
            _ => null
        }
    };

    public static string? ResourceFailedCode(DeploymentQueueTicket ticket) => ticket.Kind switch
    {
        DeploymentQueueKind.VirtualMachine => ticket.Operation == RuntimeOperationKind.Create
            ? OperationalEventCodes.Vm.CreateFailed
            : ticket.Operation == RuntimeOperationKind.Destroy
                ? OperationalEventCodes.Vm.DestroyFailed
                : null,
        DeploymentQueueKind.TeamLabRuntime => null,
        _ => ticket.Operation == RuntimeOperationKind.Create
            ? OperationalEventCodes.Container.CreateFailed
            : ticket.Operation == RuntimeOperationKind.Destroy
                ? OperationalEventCodes.Container.DestroyFailed
                : null
    };
}
