using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Utils;

public static class UserManagementGuard
{
    public static async Task<bool> CanManageUserRecordAsync(AppDbContext context, UserInfo actor, UserInfo target,
        CancellationToken token)
    {
        if (!RolePolicy.CanManageRole(actor.Role, target.Role))
            return false;

        if (actor.Role >= Role.Admin || target.Role != Role.Student)
            return true;

        return await context.StudentGroupMembers.AnyAsync(member =>
            member.StudentId == target.Id &&
            context.StudentGroupManagers.Any(manager =>
                manager.GroupId == member.GroupId && manager.ManagerId == actor.Id), token);
    }
}
