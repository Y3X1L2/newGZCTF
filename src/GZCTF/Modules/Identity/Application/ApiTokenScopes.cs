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

    public static readonly IReadOnlySet<string> TeacherScopes = new HashSet<string>(StringComparer.Ordinal)
    {
        ImagesRead,
        ImagesWrite,
        ImagesDelete,
        OperationsRead,
        ChallengesRead,
        ChallengesWrite,
        ChallengesDelete
    };

    public static readonly IReadOnlySet<string> All = TeacherScopes;

    public static bool IsAllowed(Role role, string scope) =>
        role >= Role.Teacher && All.Contains(scope);
}
