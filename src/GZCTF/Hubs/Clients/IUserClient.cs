using GZCTF.Models.Request.Game;

namespace GZCTF.Hubs.Clients;

public interface IUserClient
{
    /// <summary>
    /// 接收到比赛通知信息
    /// </summary>
    public Task ReceivedGameNotice(GameNotice notice);

    /// <summary>
    /// 接收到渗透攻击图安全摘要更新
    /// </summary>
    public Task ReceivedPenetrationAttackGraphUpdate(PenetrationAttackGraphUpdateModel update);
}
