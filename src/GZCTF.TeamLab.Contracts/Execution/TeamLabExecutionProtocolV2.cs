namespace GZCTF.TeamLab.Contracts.Execution;

public static class TeamLabExecutionProtocolV2
{
    static readonly HashSet<string> Stages =
    [
        "validation", "capacity", "artifact", "network", "compute", "guest", "service", "observation", "cleanup"
    ];

    static readonly HashSet<string> Outcomes = ["succeeded", "failed", "already_applied"];

    public static bool IsStage(string value) => Stages.Contains(value);

    public static bool IsOutcome(string value) => Outcomes.Contains(value);
}
