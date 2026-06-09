using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class AwdpCheckerService(
    AppDbContext context,
    IAwdpRepository awdpRepository,
    AwdpScriptRunner scriptRunner,
    ILogger<AwdpCheckerService> logger)
{
    const int MaxDownRetries = 2;
    static readonly TimeSpan DownRetryDelay = TimeSpan.FromSeconds(2);

    public async Task<AwdpCheckerTask[]> RunCheckerForRound(AwdpRound round, AwdpService[] services,
        Participation[] participations, CancellationToken token = default)
    {
        var instances = await awdpRepository.GetInstancesByGame(round.GameId, token);
        var existing = await context.AwdpCheckerTasks
            .Where(t => t.RoundId == round.Id)
            .ToDictionaryAsync(t => new CheckerTaskKey(t.ServiceId, t.TeamId), token);

        foreach (var service in services)
        {
            foreach (var part in participations)
            {
                var key = new CheckerTaskKey(service.Id, part.TeamId);
                var instance = instances.FirstOrDefault(i => i.ServiceId == service.Id && i.TeamId == part.TeamId);
                var flag = await awdpRepository.GetFlag(round.Id, service.Id, part.TeamId, token);

                var result = instance?.Container is null
                    ? (CheckerStatus.Down, "服务实例不存在或容器未运行")
                    : await RunCheckerWithWarmup(service, instance, flag?.FlagValue ?? string.Empty, token);

                if (existing.TryGetValue(key, out var task))
                {
                    task.Status = result.Item1;
                    task.Message = result.Item2;
                    task.ExecutedAt = DateTimeOffset.UtcNow;
                    continue;
                }

                await context.AwdpCheckerTasks.AddAsync(new AwdpCheckerTask
                {
                    RoundId = round.Id,
                    ServiceId = service.Id,
                    TeamId = part.TeamId,
                    Status = result.Item1,
                    Message = result.Item2,
                    ExecutedAt = DateTimeOffset.UtcNow
                }, token);
            }
        }

        await context.SaveChangesAsync(token);
        logger.LogInformation("AWDP checker finished for game {GameId}, round {RoundNumber}", round.GameId,
            round.RoundNumber);

        return await awdpRepository.GetCheckerTasksByRound(round.Id, token);
    }

    async Task<(CheckerStatus Status, string? Message)> RunCheckerWithWarmup(AwdpService service,
        AwdpServiceInstance instance, string flag, CancellationToken token)
    {
        var result = await scriptRunner.RunChecker(service, instance, flag, token);

        for (var retry = 0; result.Status == CheckerStatus.Down && retry < MaxDownRetries; retry++)
        {
            await Task.Delay(DownRetryDelay, token);
            result = await scriptRunner.RunChecker(service, instance, flag, token);
        }

        return result;
    }

    readonly record struct CheckerTaskKey(int ServiceId, int TeamId);
}
