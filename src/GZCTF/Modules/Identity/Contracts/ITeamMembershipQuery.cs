namespace GZCTF.Modules.Identity.Contracts;

public sealed record TeamMembershipSnapshot(int TeamId, string Name, Guid CaptainId, bool Locked, IReadOnlyList<Guid> MemberIds);
public interface ITeamMembershipQuery
{
    Task<TeamMembershipSnapshot?> GetAsync(int teamId, CancellationToken cancellationToken);
    Task<IReadOnlyList<int>> GetUserTeamsAsync(Guid userId, CancellationToken cancellationToken);
}
