using GZCTF.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Identity.Infrastructure;

public sealed class EfTeamMembershipQuery(AppDbContext db) : ITeamMembershipQuery
{
    public async Task<TeamMembershipSnapshot?> GetAsync(int teamId, CancellationToken cancellationToken)
    {
        var team = await db.Teams.AsNoTracking().Include(x => x.Members).SingleOrDefaultAsync(x => x.Id == teamId, cancellationToken);
        return team is null ? null : new(team.Id, team.Name, team.CaptainId, team.Locked,
            team.Members.Select(x => x.Id).Append(team.CaptainId).Distinct().Order().ToArray());
    }

    public async Task<IReadOnlyList<int>> GetUserTeamsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Teams.AsNoTracking().Where(x => x.CaptainId == userId || x.Members.Any(m => m.Id == userId))
            .Select(x => x.Id).ToArrayAsync(cancellationToken);
}
