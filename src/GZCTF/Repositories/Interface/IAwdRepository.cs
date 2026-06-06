namespace GZCTF.Repositories.Interface;

public interface IAwdRepository : IRepository
{
    Task<AwdService?> GetService(int serviceId, CancellationToken token = default);
    Task<AwdService[]> GetServicesByGame(int gameId, CancellationToken token = default);
    Task<AwdServiceInstance?> GetInstance(int instanceId, CancellationToken token = default);
    Task<AwdServiceInstance[]> GetInstancesByGame(int gameId, CancellationToken token = default);
    Task<AwdRound?> GetCurrentRound(int gameId, CancellationToken token = default);
    Task<AwdRound[]> GetRoundsByGame(int gameId, CancellationToken token = default);
    Task<AwdFlag?> GetFlag(int roundId, int serviceId, int teamId, CancellationToken token = default);
    Task<AwdFlag?> GetFlagByValue(string flagValue, CancellationToken token = default);
    Task<AwdCheckerTask[]> GetCheckerTasksByRound(int roundId, CancellationToken token = default);
    Task UpdateFlagSubmitted(int flagId, CancellationToken token = default);
    Task CreateRound(AwdRound round, CancellationToken token = default);
    Task CreateFlags(IEnumerable<AwdFlag> flags, CancellationToken token = default);
    Task CreateCheckerTasks(IEnumerable<AwdCheckerTask> tasks, CancellationToken token = default);
}
