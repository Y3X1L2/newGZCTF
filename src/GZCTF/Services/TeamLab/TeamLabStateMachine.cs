using GZCTF.Models.Data;

namespace GZCTF.Services.TeamLab;

public static class TeamLabStateMachine
{
    private static readonly HashSet<(TeamLabRuntimeStatus From, TeamLabRuntimeStatus To)> Allowed =
    [
        (TeamLabRuntimeStatus.Pending, TeamLabRuntimeStatus.Planning),
        (TeamLabRuntimeStatus.Stopped, TeamLabRuntimeStatus.Planning),
        (TeamLabRuntimeStatus.Failed, TeamLabRuntimeStatus.Planning),
        (TeamLabRuntimeStatus.Destroyed, TeamLabRuntimeStatus.Planning),
        (TeamLabRuntimeStatus.Planning, TeamLabRuntimeStatus.Scheduled),
        (TeamLabRuntimeStatus.Planning, TeamLabRuntimeStatus.Failed),
        (TeamLabRuntimeStatus.Scheduled, TeamLabRuntimeStatus.Planning),
        (TeamLabRuntimeStatus.Scheduled, TeamLabRuntimeStatus.Deploying),
        (TeamLabRuntimeStatus.Scheduled, TeamLabRuntimeStatus.Failed),
        (TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.Probing),
        (TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.Failed),
        (TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.CleanupPending),
        (TeamLabRuntimeStatus.Probing, TeamLabRuntimeStatus.Running),
        (TeamLabRuntimeStatus.Probing, TeamLabRuntimeStatus.Failed),
        (TeamLabRuntimeStatus.Probing, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.Scheduled, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.Running, TeamLabRuntimeStatus.Stopped),
        (TeamLabRuntimeStatus.Running, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.Stopped, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.Failed, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.CleanupPending, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.Destroying, TeamLabRuntimeStatus.Destroyed),
        (TeamLabRuntimeStatus.Destroying, TeamLabRuntimeStatus.CleanupPending)
    ];

    public static bool CanTransition(TeamLabRuntimeStatus from, TeamLabRuntimeStatus to) =>
        from == to || Allowed.Contains((from, to));
}
