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
    /// 接收到 AWDP 轮次状态变化
    /// </summary>
    public Task ReceivedAwdpRoundChange(AwdpGameStatusModel status);

    /// <summary>
    /// 接收到 AWDP 服务状态变化
    /// </summary>
    public Task ReceivedAwdpServiceStatusChange(AwdpServiceStatusModel status);

    /// <summary>
    /// 接收到 AWDP 修补结果
    /// </summary>
    public Task ReceivedAwdpPatchResult(AwdpPatchResultModel result);
}
