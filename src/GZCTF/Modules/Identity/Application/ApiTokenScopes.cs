namespace GZCTF.Modules.Identity.Application;

public static class ApiTokenScopes
{
    public const string ImagesRead = "images:read";
    public const string ImagesWrite = "images:write";
    public const string ImagesDelete = "images:delete";
    public const string AssetsRead = "assets:read";
    public const string AssetsWrite = "assets:write";
    public const string AssetsDelete = "assets:delete";
    public const string OperationsRead = "operations:read";
    public const string ChallengesRead = "challenges:read";
    public const string ChallengesWrite = "challenges:write";
    public const string ChallengesDelete = "challenges:delete";
    public const string ExercisesRead = "exercises:read";
    public const string ExercisesWrite = "exercises:write";
    public const string ExercisesDelete = "exercises:delete";
    public const string TrainingWrite = "training:write";
    public const string TheoryWrite = "theory:write";
    public const string TeamsWrite = "teams:write";
    public const string TeamLabTopologiesRead = "teamlab.topologies:read";
    public const string TeamLabTopologiesWrite = "teamlab.topologies:write";
    public const string TeamLabRuntimesRead = "teamlab.runtimes:read";
    public const string TeamLabRuntimesWrite = "teamlab.runtimes:write";
    public const string TeamLabTrafficRead = "teamlab.traffic:read";
    public const string TeamLabCaptureRead = "teamlab.capture:read";
    public const string TeamLabCaptureWrite = "teamlab.capture:write";
    public const string TeamLabResourcePoolsRead = "teamlab.resource-pools:read";
    public const string TeamLabDevicePackagesRead = "teamlab.device-packages:read";
    public const string TeamLabConnectorsRead = "teamlab.connectors:read";
    public const string TeamLabConnectorsWrite = "teamlab.connectors:write";
    public const string TeamLabLinkPoliciesRead = "teamlab.link-policies:read";
    public const string TeamLabLinkPoliciesWrite = "teamlab.link-policies:write";
    public const string TeamLabRemoteSessionsRead = "teamlab.remote-sessions:read";
    public const string TeamLabRemoteSessionsWrite = "teamlab.remote-sessions:write";
    public const string BootstrapProfilesRead = "bootstrap-profiles:read";
    public const string BootstrapProfilesWrite = "bootstrap-profiles:write";

    public static readonly IReadOnlySet<string> TeacherScopes = new HashSet<string>(StringComparer.Ordinal)
    {
        ImagesRead,
        ImagesWrite,
        ImagesDelete,
        AssetsRead,
        AssetsWrite,
        AssetsDelete,
        OperationsRead,
        ChallengesRead,
        ChallengesWrite,
        ChallengesDelete,
        ExercisesRead,
        ExercisesWrite,
        ExercisesDelete,
        TrainingWrite,
        TheoryWrite,
        TeamLabTopologiesRead,
        TeamLabTopologiesWrite,
        TeamLabRuntimesRead,
        TeamLabRuntimesWrite,
        TeamLabTrafficRead,
        TeamLabCaptureRead,
        TeamLabCaptureWrite,
        TeamLabResourcePoolsRead,
        TeamLabDevicePackagesRead,
        TeamLabConnectorsRead,
        TeamLabConnectorsWrite,
        TeamLabLinkPoliciesRead,
        TeamLabLinkPoliciesWrite,
        TeamLabRemoteSessionsRead,
        TeamLabRemoteSessionsWrite,
        BootstrapProfilesRead,
        BootstrapProfilesWrite
    };

    public static readonly IReadOnlySet<string> AdminScopes = new HashSet<string>(StringComparer.Ordinal)
    {
        TeamsWrite
    };

    public static readonly IReadOnlySet<string> All = TeacherScopes
        .Concat(AdminScopes)
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsAllowed(Role role, string scope) =>
        role >= Role.Admin
            ? All.Contains(scope)
            : role >= Role.Teacher && TeacherScopes.Contains(scope);
}
