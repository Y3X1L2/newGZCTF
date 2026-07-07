using GZCTF.Models.Data;

namespace GZCTF.Utils;

public static class TeamPolicy
{
    public static bool CanCaptainLeave(Team team, Guid userId) => team.CaptainId != userId;

    public static bool CanKickMember(Team team, Guid userId) => team.CaptainId != userId;

    public static bool CanTransferTo(Team team, UserInfo newCaptain) =>
        team.Members.Any(member => member.Id == newCaptain.Id);
}
