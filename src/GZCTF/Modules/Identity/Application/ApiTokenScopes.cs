namespace GZCTF.Modules.Identity.Application;

public static class ApiTokenScopes
{
    public const string ImagesRead = "images:read";
    public const string ImagesWrite = "images:write";
    public const string ImagesDelete = "images:delete";
    public const string OperationsRead = "operations:read";
    public const string ChallengesRead = "challenges:read";
    public const string ChallengesWrite = "challenges:write";
    public const string ChallengesDelete = "challenges:delete";
    public const string ExercisesRead = "exercises:read";
    public const string ExercisesWrite = "exercises:write";
    public const string ExercisesDelete = "exercises:delete";
    public const string TeamLabTopologiesRead = "teamlab.topologies:read";
    public const string TeamLabTopologiesWrite = "teamlab.topologies:write";
    public const string TeamLabRuntimesRead = "teamlab.runtimes:read";
    public const string TeamLabRuntimesWrite = "teamlab.runtimes:write";
    public const string TeamLabTrafficRead = "teamlab.traffic:read";
    public const string TeamLabCaptureRead = "teamlab.capture:read";
    public const string TeamLabCaptureWrite = "teamlab.capture:write";

    public static readonly IReadOnlySet<string> TeacherScopes = new HashSet<string>(StringComparer.Ordinal)
    {
        ImagesRead,
        ImagesWrite,
        ImagesDelete,
        OperationsRead,
        ChallengesRead,
        ChallengesWrite,
        ChallengesDelete,
        ExercisesRead,
        ExercisesWrite,
        ExercisesDelete,
        TeamLabTopologiesRead,
        TeamLabTopologiesWrite,
        TeamLabRuntimesRead,
        TeamLabRuntimesWrite,
        TeamLabTrafficRead,
        TeamLabCaptureRead,
        TeamLabCaptureWrite
    };

    public static readonly IReadOnlySet<string> All = TeacherScopes;

    public static bool IsAllowed(Role role, string scope) =>
        role >= Role.Teacher && All.Contains(scope);
}
