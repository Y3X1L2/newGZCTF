namespace GZCTF.Modules.Runtime.Contracts;

public sealed record RuntimeDifferencePreview(int Generation, DateTimeOffset ObservedAt,
    bool OperationInProgress, IReadOnlyList<RuntimeResourceDifference> Items);

public sealed record RuntimeResourceDifference(int? AssetId, Guid? WorkerNodeId, string ResourceKind,
    string Name, string ExpectedState, string? ActualState, string Difference, string? SuggestedAction);
