using System.Data;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GZCTF.Modules.Identity.Application;

public enum UserDeletionResult { Deleted, NotFound, Forbidden, SelfDeletion, LastSuperAdmin }

/// <summary>Retires login identity without cascading through business history.</summary>
public sealed class UserDeletionService(AppDbContext context, IMemoryCache cache)
{
    public async Task<UserDeletionResult> DeleteAsync(Guid actorId, Guid userId, CancellationToken token)
    {
        // A concurrent captain transfer/member change must not leave an orphaned captain.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var actor = await context.Users.SingleOrDefaultAsync(u => u.Id == actorId && u.DeletedAtUtc == null, token);
        var user = await context.Users.SingleOrDefaultAsync(u => u.Id == userId && u.DeletedAtUtc == null, token);
        if (actor is null || actor.Role < Role.Admin)
            return UserDeletionResult.Forbidden;
        if (actorId == userId)
            return UserDeletionResult.SelfDeletion;
        if (user is null)
            return UserDeletionResult.NotFound;
        if (!await UserManagementGuard.CanManageUserRecordAsync(context, actor, user, token))
            return UserDeletionResult.Forbidden;
        if (user.Role == Role.SuperAdmin &&
            await context.Users.CountAsync(u => u.Role == Role.SuperAdmin && u.DeletedAtUtc == null, token) <= 1)
            return UserDeletionResult.LastSuperAdmin;

        var now = DateTimeOffset.UtcNow;
        var teams = await context.Teams.Include(t => t.Members)
            .Where(t => t.CaptainId == userId || t.Members.Any(m => m.Id == userId)).ToListAsync(token);
        foreach (var team in teams)
        {
            team.Members.Remove(user);
            if (team.CaptainId == userId)
            {
                var successor = team.Members.Where(m => m.DeletedAtUtc == null)
                    .OrderBy(m => m.Role == Role.Banned).ThenBy(m => m.RegisterTimeUtc).ThenBy(m => m.Id)
                    .FirstOrDefault();
                if (successor is not null)
                    team.Captain = successor;
                else
                    team.Locked = true; // Preserve empty team's participation and score history.
            }
            team.UpdateInviteToken();
        }

        var requests = await context.TeamJoinRequests.Where(r =>
            r.Status == TeamJoinRequestStatus.Pending &&
            (r.UserId == userId || (r.Team.CaptainId == userId && !r.Team.Members.Any(m => m.Id != userId))))
            .ToListAsync(token);
        foreach (var request in requests)
        {
            request.Status = TeamJoinRequestStatus.Rejected;
            request.ReviewedAtUtc = now;
            request.ReviewedById = actorId;
        }
        context.StudentGroupMembers.RemoveRange(await context.StudentGroupMembers.Where(m => m.StudentId == userId).ToListAsync(token));
        context.StudentGroupManagers.RemoveRange(await context.StudentGroupManagers.Where(m => m.ManagerId == userId).ToListAsync(token));
        context.UserLogins.RemoveRange(await context.UserLogins.Where(l => l.UserId == userId).ToListAsync(token));
        context.UserTokens.RemoveRange(await context.UserTokens.Where(t => t.UserId == userId).ToListAsync(token));
        context.UserClaims.RemoveRange(await context.UserClaims.Where(c => c.UserId == userId).ToListAsync(token));
        context.UserRoles.RemoveRange(await context.UserRoles.Where(r => r.UserId == userId).ToListAsync(token));
        foreach (var apiToken in await context.ApiTokens.Where(t => t.CreatorId == userId && t.RevokedAt == null).ToListAsync(token))
            apiToken.RevokedAt = now;

        user.DeletedAtUtc = now;
        user.Role = Role.Banned;
        user.UserName = $"deleted-{user.Id.ToString("N")[^8..]}";
        // Null normalized keys release the previous username/email without creating a new login identity.
        user.NormalizedUserName = null;
        user.Email = user.NormalizedEmail = user.PhoneNumber = user.PasswordHash = user.AvatarHash = null;
        user.RealName = user.StdNumber = user.Bio = string.Empty;
        user.IP = IPAddress.Any;
        user.EmailConfirmed = user.PhoneNumberConfirmed = user.TwoFactorEnabled = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        cache.Remove($"public-user-profile:{userId:N}");
        return UserDeletionResult.Deleted;
    }
}
