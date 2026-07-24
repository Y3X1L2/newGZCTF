namespace GZCTF.Modules.Runtime.Application;

public sealed class RuntimeSchedulingOptions
{
    public int MaxConcurrentCreatesPerTeam { get; set; } = 4;
    public int MaxConcurrentCreatesPerUser { get; set; } = 2;
    public int MaxQueuedCreatesPerOwner { get; set; } = 32;
    public int EligibleWindowSize { get; set; } = 512;
    public int SchedulingBatchSize { get; set; } = 64;
    public int PlacementImprovementPasses { get; set; } = 4;
    public int PlacementComputationBudgetMs { get; set; } = 250;
    public float CpuRejectThreshold { get; set; } = 0.95f;
    public float MemoryRejectThreshold { get; set; } = 0.92f;
}
