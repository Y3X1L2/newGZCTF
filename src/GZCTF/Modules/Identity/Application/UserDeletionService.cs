using System.Data;
using System.Data.Common;
using System.Net;
using GZCTF.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GZCTF.Modules.Identity.Application;

public enum UserDeletionResult { Deleted, NotFound, Forbidden, SelfDeletion, LastSuperAdmin, Conflict }

/// <summary>Retires login identity without cascading through business history.</summary>
public sealed class UserDeletionService(AppDbContext context, IMemoryCache cache,
    IEnumerable<IUserAccountRevocationParticipant> revocationParticipants, ILogger<UserDeletionService> logger)
{
    public async Task<UserDeletionResult> DeleteAsync(Guid actorId, Guid userId, CancellationToken token)
    {
        try { return await ExecuteAsync(actorId, userId, token); }
        catch (DbUpdateConcurrencyException) { return UserDeletionResult.Conflict; }
        catch (DbUpdateException exception) when (exception.InnerException is DbException { SqlState: "40001" or "40P01" })
        {
            return UserDeletionResult.Conflict;
        }
        catch (DbException exception) when (exception.SqlState is "40001" or "40P01")
        {
            return UserDeletionResult.Conflict;
        }
    }

    private async Task<UserDeletionResult> ExecuteAsync(Guid actorId, Guid userId, CancellationToken token)
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
        user.UserName = $"已删除-{user.Id.ToString("N")[^11..]}";
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
        foreach (var participant in revocationParticipants)
            await participant.PrepareAsync(userId, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        await transaction.DisposeAsync();
        cache.Remove($"public-user-profile:{userId:N}");
        foreach (var participant in revocationParticipants)
        {
            try { await participant.CompleteAsync(userId, token); }
            catch (Exception)
            {
                // The module owns durable cleanup/retry; an external outage cannot restore this account.
                logger.LogWarning("Account {UserId} retired; external access cleanup by {Participant} requires retry",
                    userId, participant.GetType().Name);
            }
        }
        return UserDeletionResult.Deleted;
    }
}
