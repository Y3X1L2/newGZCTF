using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Contracts;
using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.Runtime.Domain;

public sealed class AgentRuntimeSignal
{
    public long Id { get; set; }
    public Guid OperationId { get; set; }
    public Guid WorkerNodeId { get; set; }
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public long Sequence { get; set; }
    public AgentRuntimeSignalStage Stage { get; set; }
    public AgentRuntimeSignalOutcome Outcome { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ResourceKind { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public bool Retryable { get; set; }
    public string FactsJson { get; set; } = "{}";
    public WorkerNode WorkerNode { get; set; } = null!;
    public TeamLabRuntime Runtime { get; set; } = null!;
}
