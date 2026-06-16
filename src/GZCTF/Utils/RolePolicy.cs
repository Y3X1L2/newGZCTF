namespace GZCTF.Utils;

public enum AdminTab
{
    Games,
    TheoryBank,
    Images,
    Training,
    Users,
    Teams,
    Instances,
    Nodes,
    Queue,
    Logs,
    Settings,
    Scenarios,
    IRChallenges,
    Submissions
}

public static class RolePolicy
{
    public static bool IsStudent(Role role) => role == Role.Student;

    public static bool IsTeacherOrAbove(Role role) => role >= Role.Teacher;

    public static bool IsAdminOrAbove(Role role) => role >= Role.Admin;

    public static bool IsSuperAdmin(Role role) => role >= Role.SuperAdmin;

    public static bool CanAccessAdmin(Role actor) => IsTeacherOrAbove(actor);

    public static bool CanAccessAdminTab(Role actor, AdminTab tab) =>
        actor switch
        {
            >= Role.SuperAdmin => true,
            >= Role.Admin => true,
            >= Role.Teacher => tab is AdminTab.Games or AdminTab.TheoryBank or AdminTab.Images or AdminTab.Training or AdminTab.Users,
            _ => false
        };

    public static bool CanViewRole(Role actor, Role target) =>
        actor switch
        {
            >= Role.SuperAdmin => true,
            >= Role.Admin => target is Role.Student or Role.Teacher,
            >= Role.Teacher => target is Role.Student,
            _ => false
        };

    public static bool CanManageRole(Role actor, Role target) =>
        actor switch
        {
            >= Role.SuperAdmin => true,
            >= Role.Admin => target is Role.Student or Role.Teacher,
            >= Role.Teacher => target is Role.Student,
            _ => false
        };

    public static bool CanAssignRole(Role actor, Role target) => CanManageRole(actor, target);

    public static Role[] ViewableRoles(Role actor) =>
        actor switch
        {
            >= Role.SuperAdmin => [Role.Banned, Role.Student, Role.Teacher, Role.Admin, Role.SuperAdmin],
            >= Role.Admin => [Role.Banned, Role.Student, Role.Teacher],
            >= Role.Teacher => [Role.Student],
            _ => []
        };

    public static Role[] AssignableRoles(Role actor) =>
        actor switch
        {
            >= Role.SuperAdmin => [Role.Banned, Role.Student, Role.Teacher, Role.Admin, Role.SuperAdmin],
            >= Role.Admin => [Role.Banned, Role.Student, Role.Teacher],
            >= Role.Teacher => [Role.Student],
            _ => []
        };
}
