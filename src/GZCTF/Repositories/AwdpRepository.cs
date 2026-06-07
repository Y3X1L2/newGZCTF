using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

public class AwdpRepository(AppDbContext context) : RepositoryBase(context), IAwdpRepository
{
    public override Task<int> CountAsync(CancellationToken token = default) =>
        Context.AwdpServices.CountAsync(token);

    public Task<AwdpService?> GetService(int serviceId, CancellationToken token = default) =>
        Context.AwdpServices.AsNoTracking()
            .Include(s => s.Game)
            .FirstOrDefaultAsync(s => s.Id == serviceId, token);

    public Task<AwdpService?> GetServiceForUpdate(int serviceId, CancellationToken token = default) =>
        Context.AwdpServices.FirstOrDefaultAsync(s => s.Id == serviceId, token);

    public Task<AwdpService[]> GetServicesByGame(int gameId, CancellationToken token = default) =>
        Context.AwdpServices.AsNoTracking()
            .Where(s => s.GameId == gameId)
            .OrderBy(s => s.Id)
            .ToArrayAsync(token);

    public Task<AwdpServiceViewModel[]> GetServiceViewsByGame(int gameId, CancellationToken token = default) =>
        Context.AwdpServices.AsNoTracking()
            .Where(s => s.GameId == gameId)
            .OrderBy(s => s.Id)
            .Select(s => new AwdpServiceViewModel
            {
                Id = s.Id,
                Name = s.Name,
                ImageName = s.ImageName,
                ExposePort = s.ExposePort,
                CheckerScript = s.CheckerScript,
                CheckerEntrypoint = s.CheckerEntrypoint,
                ExpScript = s.ExpScript,
                ExpEntrypoint = s.ExpEntrypoint,
                OriginalScore = s.OriginalScore,
                AttackPoints = s.AttackPoints,
                SlaPoints = s.SlaPoints,
                PatchPoints = s.PatchPoints,
                ServiceAbnormalPenalty = s.ServiceAbnormalPenalty,
                MaxAttackPerRound = s.MaxAttackPerRound,
                AttackPhaseMinutes = s.AttackPhaseMinutes,
                PatchPhaseMinutes = s.PatchPhaseMinutes,
                TotalRounds = s.TotalRounds,
                MaxResetCount = s.MaxResetCount,
                MaxRecoveryCount = s.MaxRecoveryCount
            })
            .ToArrayAsync(token);

    public Task<AwdpServiceInstance?> GetInstance(int instanceId, CancellationToken token = default) =>
        InstanceQuery().AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, token);

    public Task<AwdpServiceInstance?> GetInstanceForUpdate(int instanceId, CancellationToken token = default) =>
        Context.AwdpServiceInstances
            .Include(i => i.Service)
            .Include(i => i.Team)
            .Include(i => i.Container)
            .FirstOrDefaultAsync(i => i.Id == instanceId, token);

    public Task<AwdpServiceInstance[]> GetInstancesByGame(int gameId, CancellationToken token = default) =>
        InstanceQuery().AsNoTracking()
            .Where(i => i.Service.GameId == gameId)
            .OrderBy(i => i.ServiceId)
            .ThenBy(i => i.TeamId)
            .ToArrayAsync(token);

    public Task<AwdpServiceInstance[]> GetInstancesByService(int serviceId, CancellationToken token = default) =>
        InstanceQuery().AsNoTracking()
            .Where(i => i.ServiceId == serviceId)
            .OrderBy(i => i.TeamId)
            .ToArrayAsync(token);

    public Task<AwdpServiceInstance?> GetInstanceByTeamAndService(int teamId, int serviceId,
        CancellationToken token = default) =>
        InstanceQuery().AsNoTracking()
            .FirstOrDefaultAsync(i => i.TeamId == teamId && i.ServiceId == serviceId, token);

    public Task<AwdpRound?> GetCurrentRound(int gameId, CancellationToken token = default) =>
        RoundQuery().AsNoTracking()
            .Where(r => r.GameId == gameId && r.Status != AwdpRoundStatus.Finished)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(token);

    public Task<AwdpRound?> GetCurrentRoundForUpdate(int gameId, CancellationToken token = default) =>
        Context.AwdpRounds
            .Where(r => r.GameId == gameId && r.Status != AwdpRoundStatus.Finished)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(token);

    public Task<AwdpRound[]> GetRoundsByGame(int gameId, CancellationToken token = default) =>
        RoundQuery().AsNoTracking()
            .Where(r => r.GameId == gameId)
            .OrderBy(r => r.RoundNumber)
            .ToArrayAsync(token);

    public Task<AwdpFlag?> GetFlag(int roundId, int serviceId, int teamId, CancellationToken token = default) =>
        FlagQuery().AsNoTracking()
            .FirstOrDefaultAsync(f => f.RoundId == roundId && f.ServiceId == serviceId && f.TeamId == teamId,
                token);

    public Task<AwdpFlag?> GetFlagForUpdate(int roundId, int serviceId, int teamId,
        CancellationToken token = default) =>
        Context.AwdpFlags
            .FirstOrDefaultAsync(f => f.RoundId == roundId && f.ServiceId == serviceId && f.TeamId == teamId,
                token);

    public Task<AwdpFlag?> GetFlagByValue(string flagValue, CancellationToken token = default) =>
        FlagQuery().AsNoTracking()
            .FirstOrDefaultAsync(f => f.FlagValue == flagValue, token);

    public Task<AwdpFlag[]> GetFlagsByRound(int roundId, CancellationToken token = default) =>
        FlagQuery().AsNoTracking()
            .Where(f => f.RoundId == roundId)
            .OrderBy(f => f.ServiceId)
            .ThenBy(f => f.TeamId)
            .ToArrayAsync(token);

    public Task<AwdpCheckerTask[]> GetCheckerTasksByRound(int roundId, CancellationToken token = default) =>
        Context.AwdpCheckerTasks.AsNoTracking()
            .Include(t => t.Service)
            .Include(t => t.Team)
            .Where(t => t.RoundId == roundId)
            .OrderBy(t => t.ServiceId)
            .ThenBy(t => t.TeamId)
            .ToArrayAsync(token);

    public Task<AwdpCheckerTask?> GetCheckerTask(int roundId, int serviceId, int teamId,
        CancellationToken token = default) =>
        Context.AwdpCheckerTasks.AsNoTracking()
            .Include(t => t.Service)
            .Include(t => t.Team)
            .FirstOrDefaultAsync(t => t.RoundId == roundId && t.ServiceId == serviceId && t.TeamId == teamId,
                token);

    public Task<AwdpPatchSubmission?> GetPatchSubmission(int roundId, int serviceId, int teamId,
        CancellationToken token = default) =>
        PatchQuery().AsNoTracking()
            .Where(p => p.RoundId == roundId && p.ServiceId == serviceId && p.TeamId == teamId)
            .OrderByDescending(p => p.SubmittedAt)
            .FirstOrDefaultAsync(token);

    public Task<AwdpPatchSubmission[]> GetPatchSubmissionsByRound(int roundId, CancellationToken token = default) =>
        PatchQuery().AsNoTracking()
            .Where(p => p.RoundId == roundId)
            .OrderByDescending(p => p.SubmittedAt)
            .ToArrayAsync(token);

    public Task<AwdpPatchSubmission[]> GetPatchSubmissionsByGame(int gameId, int count, int skip,
        CancellationToken token = default) =>
        PatchQuery().AsNoTracking()
            .Where(p => p.Service.GameId == gameId)
            .OrderByDescending(p => p.SubmittedAt)
            .Skip(Math.Max(skip, 0))
            .Take(count <= 0 ? 100 : count)
            .ToArrayAsync(token);

    public Task<AwdpResetRecord[]> GetResetRecordsByGame(int gameId, CancellationToken token = default) =>
        Context.AwdpResetRecords.AsNoTracking()
            .Include(r => r.Service)
            .Include(r => r.Team)
            .Where(r => r.Service.GameId == gameId)
            .OrderByDescending(r => r.ResetAt)
            .ToArrayAsync(token);

    public Task<AwdpRecoveryRecord[]> GetRecoveryRecordsByGame(int gameId, CancellationToken token = default) =>
        Context.AwdpRecoveryRecords.AsNoTracking()
            .Include(r => r.Service)
            .Include(r => r.Team)
            .Where(r => r.Service.GameId == gameId)
            .OrderByDescending(r => r.RecoveryAt)
            .ToArrayAsync(token);

    public Task<int> GetResetCount(int serviceId, int teamId, CancellationToken token = default) =>
        Context.AwdpResetRecords.AsNoTracking()
            .CountAsync(r => r.ServiceId == serviceId && r.TeamId == teamId && r.ResetType == AwdpResetType.Player,
                token);

    public Task<int> GetRecoveryCount(int serviceId, int teamId, CancellationToken token = default) =>
        Context.AwdpRecoveryRecords.AsNoTracking()
            .CountAsync(r => r.ServiceId == serviceId && r.TeamId == teamId, token);

    public async Task CreateService(AwdpService service, CancellationToken token = default)
    {
        await Context.AwdpServices.AddAsync(service, token);
        await SaveAsync(token);
    }

    public async Task DeleteService(AwdpService service, CancellationToken token = default)
    {
        Context.AwdpServices.Remove(service);
        await SaveAsync(token);
    }

    public async Task CreateInstance(AwdpServiceInstance instance, CancellationToken token = default)
    {
        await Context.AwdpServiceInstances.AddAsync(instance, token);
        await SaveAsync(token);
    }

    public async Task CreateInstances(IEnumerable<AwdpServiceInstance> instances, CancellationToken token = default)
    {
        await Context.AwdpServiceInstances.AddRangeAsync(instances, token);
        await SaveAsync(token);
    }

    public async Task CreateRound(AwdpRound round, CancellationToken token = default)
    {
        await Context.AwdpRounds.AddAsync(round, token);
        await SaveAsync(token);
    }

    public async Task CreateFlags(IEnumerable<AwdpFlag> flags, CancellationToken token = default)
    {
        await Context.AwdpFlags.AddRangeAsync(flags, token);
        await SaveAsync(token);
    }

    public async Task CreateCheckerTasks(IEnumerable<AwdpCheckerTask> tasks, CancellationToken token = default)
    {
        await Context.AwdpCheckerTasks.AddRangeAsync(tasks, token);
        await SaveAsync(token);
    }

    public async Task CreatePatchSubmission(AwdpPatchSubmission submission, CancellationToken token = default)
    {
        await Context.AwdpPatchSubmissions.AddAsync(submission, token);
        await SaveAsync(token);
    }

    public async Task CreateResetRecord(AwdpResetRecord record, CancellationToken token = default)
    {
        await Context.AwdpResetRecords.AddAsync(record, token);
        await SaveAsync(token);
    }

    public async Task CreateRecoveryRecord(AwdpRecoveryRecord record, CancellationToken token = default)
    {
        await Context.AwdpRecoveryRecords.AddAsync(record, token);
        await SaveAsync(token);
    }

    public async Task<bool> UpdateFlagSubmitted(int flagId, int submittedByTeamId, Guid submittedByUserId,
        CancellationToken token = default)
    {
        var flag = await Context.AwdpFlags.FirstOrDefaultAsync(f => f.Id == flagId, token);
        if (flag is null || flag.IsSubmitted)
            return false;

        flag.IsSubmitted = true;
        flag.FirstSubmittedAt = DateTimeOffset.UtcNow;
        flag.SubmittedByTeamId = submittedByTeamId;
        flag.SubmittedByUserId = submittedByUserId;
        await SaveAsync(token);
        return true;
    }

    private IQueryable<AwdpServiceInstance> InstanceQuery() =>
        Context.AwdpServiceInstances
            .Include(i => i.Service)
            .Include(i => i.Team)
            .Include(i => i.Container);

    private IQueryable<AwdpRound> RoundQuery() =>
        Context.AwdpRounds
            .Include(r => r.Game);

    private IQueryable<AwdpFlag> FlagQuery() =>
        Context.AwdpFlags
            .Include(f => f.Round)
            .Include(f => f.Service)
            .Include(f => f.Team)
            .Include(f => f.SubmittedByTeam)
            .Include(f => f.SubmittedByUser);

    private IQueryable<AwdpPatchSubmission> PatchQuery() =>
        Context.AwdpPatchSubmissions
            .Include(p => p.Round)
            .Include(p => p.Service)
            .Include(p => p.Team);
}
