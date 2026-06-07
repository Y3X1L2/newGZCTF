using GZCTF.Models.Request.Game;

namespace GZCTF.Repositories.Interface;

public interface IAwdpRepository : IRepository
{
    Task<AwdpService?> GetService(int serviceId, CancellationToken token = default);

    Task<AwdpService?> GetServiceForUpdate(int serviceId, CancellationToken token = default);

    Task<AwdpService[]> GetServicesByGame(int gameId, CancellationToken token = default);

    Task<AwdpServiceViewModel[]> GetServiceViewsByGame(int gameId, CancellationToken token = default);

    Task<AwdpServiceInstance?> GetInstance(int instanceId, CancellationToken token = default);

    Task<AwdpServiceInstance?> GetInstanceForUpdate(int instanceId, CancellationToken token = default);

    Task<AwdpServiceInstance[]> GetInstancesByGame(int gameId, CancellationToken token = default);

    Task<AwdpServiceInstance[]> GetInstancesByService(int serviceId, CancellationToken token = default);

    Task<AwdpServiceInstance?> GetInstanceByTeamAndService(int teamId, int serviceId,
        CancellationToken token = default);

    Task<AwdpRound?> GetCurrentRound(int gameId, CancellationToken token = default);

    Task<AwdpRound?> GetCurrentRoundForUpdate(int gameId, CancellationToken token = default);

    Task<AwdpRound[]> GetRoundsByGame(int gameId, CancellationToken token = default);

    Task<AwdpFlag?> GetFlag(int roundId, int serviceId, int teamId, CancellationToken token = default);

    Task<AwdpFlag?> GetFlagForUpdate(int roundId, int serviceId, int teamId,
        CancellationToken token = default);

    Task<AwdpFlag?> GetFlagByValue(string flagValue, CancellationToken token = default);

    Task<AwdpFlag[]> GetFlagsByRound(int roundId, CancellationToken token = default);

    Task<AwdpCheckerTask[]> GetCheckerTasksByRound(int roundId, CancellationToken token = default);

    Task<AwdpCheckerTask?> GetCheckerTask(int roundId, int serviceId, int teamId,
        CancellationToken token = default);

    Task<AwdpPatchSubmission?> GetPatchSubmission(int roundId, int serviceId, int teamId,
        CancellationToken token = default);

    Task<AwdpPatchSubmission[]> GetPatchSubmissionsByRound(int roundId, CancellationToken token = default);

    Task<AwdpPatchSubmission[]> GetPatchSubmissionsByGame(int gameId, int count, int skip,
        CancellationToken token = default);

    Task<AwdpResetRecord[]> GetResetRecordsByGame(int gameId, CancellationToken token = default);

    Task<AwdpRecoveryRecord[]> GetRecoveryRecordsByGame(int gameId, CancellationToken token = default);

    Task<int> GetResetCount(int serviceId, int teamId, CancellationToken token = default);

    Task<int> GetRecoveryCount(int serviceId, int teamId, CancellationToken token = default);

    Task CreateService(AwdpService service, CancellationToken token = default);

    Task DeleteService(AwdpService service, CancellationToken token = default);

    Task CreateInstance(AwdpServiceInstance instance, CancellationToken token = default);

    Task CreateInstances(IEnumerable<AwdpServiceInstance> instances, CancellationToken token = default);

    Task CreateRound(AwdpRound round, CancellationToken token = default);

    Task CreateFlags(IEnumerable<AwdpFlag> flags, CancellationToken token = default);

    Task CreateCheckerTasks(IEnumerable<AwdpCheckerTask> tasks, CancellationToken token = default);

    Task CreatePatchSubmission(AwdpPatchSubmission submission, CancellationToken token = default);

    Task CreateResetRecord(AwdpResetRecord record, CancellationToken token = default);

    Task CreateRecoveryRecord(AwdpRecoveryRecord record, CancellationToken token = default);

    Task<bool> UpdateFlagSubmitted(int flagId, int submittedByTeamId, Guid submittedByUserId,
        CancellationToken token = default);
}
