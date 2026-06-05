using GZCTF.Models.Request.Game;

namespace GZCTF.Hubs.Clients;

public interface IMonitorClient
{
    /// <summary>
    /// 接收到比赛事件信息
    /// </summary>
    public Task ReceivedGameEvent(GameEvent gameEvent);

    /// <summary>
    /// 接收到比赛提交信息
    /// </summary>
    public Task ReceivedSubmissions(Submission submission);

    /// <summary>
    /// 接收到AWD轮次变化
    /// </summary>
    public Task ReceivedAwdRoundChange(AwdGameStatusModel status);

    /// <summary>
    /// 接收到AWD服务状态变化
    /// </summary>
    public Task ReceivedAwdServiceStatusChange(AwdServiceStatusModel status);
}
