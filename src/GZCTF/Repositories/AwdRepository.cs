using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

public class AwdRepository(AppDbContext context) : RepositoryBase(context), IAwdRepository
{
    public Task<AwdService?> GetService(int serviceId, CancellationToken token = default)
        => Context.AwdServices.FirstOrDefaultAsync(s => s.Id == serviceId, token);

    public Task<AwdService[]> GetServicesByGame(int gameId, CancellationToken token = default)
        => Context.AwdServices.Where(s => s.GameId == gameId).ToArrayAsync(token);

    public Task<AwdServiceInstance?> GetInstance(int instanceId, CancellationToken token = default)
        => Context.AwdServiceInstances
            .Include(i => i.Container)
            .Include(i => i.Team)
            .FirstOrDefaultAsync(i => i.Id == instanceId, token);

    public Task<AwdServiceInstance[]> GetInstancesByGame(int gameId, CancellationToken token = default)
        => Context.AwdServiceInstances
            .Include(i => i.Service)
            .Include(i => i.Container)
            .Include(i => i.Team)
            .Where(i => i.Service.GameId == gameId)
            .ToArrayAsync(token);

    public Task<AwdRound?> GetCurrentRound(int gameId, CancellationToken token = default)
        => Context.AwdRounds
            .Where(r => r.GameId == gameId && r.Status == AwdRoundStatus.Running)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(token);

    public Task<AwdRound[]> GetRoundsByGame(int gameId, CancellationToken token = default)
        => Context.AwdRounds.Where(r => r.GameId == gameId).OrderBy(r => r.RoundNumber).ToArrayAsync(token);

    public Task<AwdFlag?> GetFlag(int roundId, int serviceId, int teamId, CancellationToken token = default)
        => Context.AwdFlags.FirstOrDefaultAsync(
            f => f.RoundId == roundId && f.ServiceId == serviceId && f.TeamId == teamId, token);

    public Task<AwdFlag?> GetFlagByValue(string flagValue, CancellationToken token = default)
        => Context.AwdFlags
            .Include(f => f.Round)
            .Include(f => f.Service)
            .Include(f => f.Team)
            .FirstOrDefaultAsync(f => f.FlagValue == flagValue, token);

    public Task<AwdCheckerTask[]> GetCheckerTasksByRound(int roundId, CancellationToken token = default)
        => Context.AwdCheckerTasks
            .Where(t => t.RoundId == roundId)
            .ToArrayAsync(token);

    public async Task UpdateFlagSubmitted(int flagId, CancellationToken token = default)
    {
        var flag = await Context.AwdFlags.FindAsync([flagId], token);
        if (flag is not null)
        {
            flag.IsSubmitted = true;
            flag.FirstSubmittedAt = DateTimeOffset.UtcNow;
            await SaveAsync(token);
        }
    }

    public async Task CreateRound(AwdRound round, CancellationToken token = default)
    {
        await Context.AwdRounds.AddAsync(round, token);
        await SaveAsync(token);
    }

    public async Task CreateFlags(IEnumerable<AwdFlag> flags, CancellationToken token = default)
    {
        await Context.AwdFlags.AddRangeAsync(flags, token);
        await SaveAsync(token);
    }

    public async Task CreateCheckerTasks(IEnumerable<AwdCheckerTask> tasks, CancellationToken token = default)
    {
        await Context.AwdCheckerTasks.AddRangeAsync(tasks, token);
        await SaveAsync(token);
    }
}
