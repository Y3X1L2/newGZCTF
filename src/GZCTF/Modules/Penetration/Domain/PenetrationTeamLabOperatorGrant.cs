using GZCTF.Modules.TeamLab.Domain.Runtime;

namespace GZCTF.Modules.Penetration.Domain;

public sealed class PenetrationTeamLabOperatorGrant
{
    public long Id { get; set; }
    public int GameId { get; set; }
    public Guid UserId { get; set; }
    public TeamLabOperatorPermission Permissions { get; set; }
    public Guid GrantedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Game Game { get; set; } = null!;
    public UserInfo User { get; set; } = null!;
    public UserInfo GrantedBy { get; set; } = null!;
}
