using System.Collections.Concurrent;
using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class AwdpRoundService(
    IServiceScopeFactory scopeFactory,
    ILogger<AwdpRoundService> logger) : IHostedService, IDisposable
{
    readonly ConcurrentDictionary<int, CancellationTokenSource> _gameLoops = new();
    readonly CancellationTokenSource _shutdown = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeGameIds = await context.AwdpRounds.AsNoTracking()
            .Where(r => r.Status != AwdpRoundStatus.Finished)
            .Select(r => r.GameId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        foreach (var gameId in activeGameIds)
            StartLoop(gameId);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown.Cancel();
        foreach (var loop in _gameLoops.Values)
            loop.Cancel();
        return Task.CompletedTask;
    }

    public async Task<(bool Success, string Message)> StartGame(Game game, CancellationToken token = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAwdpRepository>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (game.GameType is not GameType.AWDP and not GameType.Mixed)
            return (false, "该比赛不是 AWDP 或混合模式");

        var services = await repository.GetServicesByGame(game.Id, token);
        if (services.Length == 0)
            return (false, "请先配置至少一个 AWDP 服务");

        if (!TryGetSchedule(services, out _, out _, out _, out var error))
            return (false, error);

        var participantCount = await context.Participations.AsNoTracking()
            .CountAsync(p => p.GameId == game.Id && p.Status == ParticipationStatus.Accepted, token);
        if (participantCount == 0)
            return (false, "没有已通过审核的参赛队伍");

        var started = StartLoop(game.Id);
        return started ? (true, "AWDP 比赛已启动") : (false, "AWDP 比赛已经在运行");
    }

    public async Task<(bool WasRunning, string Message)> StopGame(int gameId, bool cleanupInstances = true,
        CancellationToken token = default)
    {
        var wasRunning = _gameLoops.TryRemove(gameId, out var cts);
        if (cts is not null)
        {
            cts.Cancel();
            cts.Dispose();
        }

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IAwdpRepository>();
        var cacheHelper = scope.ServiceProvider.GetRequiredService<CacheHelper>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<MonitorHub, IMonitorClient>>();
        var services = await repository.GetServicesByGame(gameId, token);

        var round = await repository.GetCurrentRoundForUpdate(gameId, token);
        if (round is not null)
        {
            round.Status = AwdpRoundStatus.Finished;
            round.EndTime = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);

            await hub.Clients.Group($"Game_{gameId}")
                .ReceivedAwdpRoundChange(ToStatusModel(gameId, round, services));
        }

        if (cleanupInstances)
        {
            var instanceService = scope.ServiceProvider.GetRequiredService<AwdpInstanceService>();
            await instanceService.DestroyInstancesForGame(gameId, token);
        }

        await cacheHelper.FlushScoreboardCache(gameId, token);

        return wasRunning
            ? (true, "AWDP 比赛已停止，实例资源已清理")
            : (false, "AWDP 比赛未在运行，已完成状态与资源清理");
    }

    public async Task<AwdpGameStatusModel> GetStatus(int gameId, CancellationToken token = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAwdpRepository>();
        var services = await repository.GetServicesByGame(gameId, token);
        var round = await repository.GetCurrentRound(gameId, token);

        if (round is null)
            return new AwdpGameStatusModel
            {
                GameId = gameId,
                CurrentRound = 0,
                RoundStartTime = DateTimeOffset.UtcNow,
                AttackPhaseMinutes = services.FirstOrDefault()?.AttackPhaseMinutes ?? 0,
                PatchPhaseMinutes = services.FirstOrDefault()?.PatchPhaseMinutes ?? 0,
                Status = AwdpRoundStatus.Finished
            };

        return ToStatusModel(gameId, round, services);
    }

    bool StartLoop(int gameId)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        if (!_gameLoops.TryAdd(gameId, cts))
        {
            cts.Dispose();
            return false;
        }

        _ = Task.Run(() => RunGameLoop(gameId, cts.Token), CancellationToken.None);
        return true;
    }

    async Task RunGameLoop(int gameId, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var shouldContinue = await RunNextRoundStep(gameId, token);
                if (!shouldContinue)
                    break;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.LogInformation("AWDP game loop cancelled for game {GameId}", gameId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AWDP game loop crashed for game {GameId}", gameId);
        }
        finally
        {
            if (_gameLoops.TryRemove(gameId, out var cts))
                cts.Dispose();
        }
    }

    async Task<bool> RunNextRoundStep(int gameId, CancellationToken token)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IAwdpRepository>();
        var instanceService = scope.ServiceProvider.GetRequiredService<AwdpInstanceService>();
        var checkerService = scope.ServiceProvider.GetRequiredService<AwdpCheckerService>();
        var cacheHelper = scope.ServiceProvider.GetRequiredService<CacheHelper>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<MonitorHub, IMonitorClient>>();

        var game = await context.Games.FirstOrDefaultAsync(g => g.Id == gameId, token);
        if (game is null)
            return false;

        var services = await repository.GetServicesByGame(gameId, token);
        if (services.Length == 0)
        {
            logger.LogWarning("AWDP game {GameId} stopped: {Reason}", gameId, "no services");
            return false;
        }

        if (!TryGetSchedule(services, out var attackMinutes, out var patchMinutes,
                out var totalRounds, out var scheduleError))
        {
            logger.LogWarning("AWDP game {GameId} stopped: {Reason}", gameId, scheduleError);
            return false;
        }

        var participations = await context.Participations
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .Include(p => p.Team)
            .OrderBy(p => p.TeamId)
            .ToArrayAsync(token);

        if (participations.Length == 0)
            return false;

        await instanceService.CreateInstancesForGame(game, token);

        var round = await context.AwdpRounds
            .Where(r => r.GameId == gameId && r.Status != AwdpRoundStatus.Finished)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(token);

        if (round is null)
        {
            var nextRoundNumber = await context.AwdpRounds
                .Where(r => r.GameId == gameId)
                .Select(r => (int?)r.RoundNumber)
                .MaxAsync(token) ?? 0;

            if (nextRoundNumber >= totalRounds)
                return false;

            round = new AwdpRound
            {
                GameId = gameId,
                RoundNumber = nextRoundNumber + 1,
                StartTime = DateTimeOffset.UtcNow,
                AttackPhaseStart = DateTimeOffset.UtcNow,
                Status = AwdpRoundStatus.AttackPhase
            };
            await context.AwdpRounds.AddAsync(round, token);
            await context.SaveChangesAsync(token);

            await GenerateFlagsAndInject(round, services, participations, instanceService, repository, token);
        }

        if (round.Status == AwdpRoundStatus.AttackPhase)
        {
            await hub.Clients.Group($"Game_{gameId}")
                .ReceivedAwdpRoundChange(ToStatusModel(gameId, round, services));

            var checkerTasks = await checkerService.RunCheckerForRound(round, services, participations, token);
            await cacheHelper.FlushScoreboardCache(gameId, token);
            await BroadcastServiceStatus(gameId, services, checkerTasks, repository, hub, token);

            await DelayUntil(round.AttackPhaseStart.AddMinutes(attackMinutes), token);

            round.Status = AwdpRoundStatus.PatchPhase;
            round.PatchPhaseStart = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);

            await hub.Clients.Group($"Game_{gameId}")
                .ReceivedAwdpRoundChange(ToStatusModel(gameId, round, services));
        }

        if (round.Status == AwdpRoundStatus.PatchPhase)
        {
            var patchStart = round.PatchPhaseStart ?? DateTimeOffset.UtcNow;
            await DelayUntil(patchStart.AddMinutes(patchMinutes), token);

            round.Status = AwdpRoundStatus.Finished;
            round.EndTime = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);

            await cacheHelper.FlushScoreboardCache(gameId, token);
            await hub.Clients.Group($"Game_{gameId}")
                .ReceivedAwdpRoundChange(ToStatusModel(gameId, round, services));

            if (round.RoundNumber >= totalRounds)
                await instanceService.DestroyInstancesForGame(gameId, token);
        }

        return round.RoundNumber < totalRounds;
    }

    static async Task GenerateFlagsAndInject(AwdpRound round, AwdpService[] services,
        Participation[] participations, AwdpInstanceService instanceService, IAwdpRepository repository,
        CancellationToken token)
    {
        List<AwdpFlag> flags = [];

        foreach (var service in services)
        foreach (var part in participations)
            flags.Add(new AwdpFlag
            {
                RoundId = round.Id,
                ServiceId = service.Id,
                TeamId = part.TeamId,
                FlagValue = FlagHelper.GenerateFlag(32)
            });

        await repository.CreateFlags(flags, token);

        foreach (var flag in flags)
        {
            var instance = await repository.GetInstanceByTeamAndService(flag.TeamId, flag.ServiceId, token);
            if (instance is not null)
                await instanceService.ResetInstanceForRound(instance.Id, flag.FlagValue, token);
        }
    }

    static async Task BroadcastServiceStatus(int gameId, AwdpService[] services, AwdpCheckerTask[] checkerTasks,
        IAwdpRepository repository, IHubContext<MonitorHub, IMonitorClient> hub, CancellationToken token)
    {
        var instances = await repository.GetInstancesByGame(gameId, token);
        var resets = await repository.GetResetRecordsByGame(gameId, token);
        var recoveries = await repository.GetRecoveryRecordsByGame(gameId, token);

        foreach (var service in services)
        {
            var model = new AwdpServiceStatusModel
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                TeamStatuses = instances.Where(i => i.ServiceId == service.Id)
                    .Select(i => new AwdpTeamServiceStatus
                    {
                        InstanceId = i.Id,
                        ServiceId = service.Id,
                        ServiceName = service.Name,
                        TeamId = i.TeamId,
                        TeamName = i.Team.Name,
                        IpAddress = i.Container?.PublicIP ?? i.Container?.IP,
                        Port = i.Container?.PublicPort ?? i.Container?.Port,
                        LastCheckerStatus = checkerTasks
                            .FirstOrDefault(t => t.ServiceId == service.Id && t.TeamId == i.TeamId)?.Status,
                        IsRunning = i.IsRunning && i.Container?.Status == ContainerStatus.Running,
                        RemainingResetCount = Math.Max(0,
                            service.MaxResetCount - resets.Count(r =>
                                r.ServiceId == service.Id && r.TeamId == i.TeamId &&
                                r.ResetType == AwdpResetType.Player)),
                        RemainingRecoveryCount = Math.Max(0,
                            service.MaxRecoveryCount -
                            recoveries.Count(r => r.ServiceId == service.Id && r.TeamId == i.TeamId))
                    }).ToList()
            };

            await hub.Clients.Group($"Game_{gameId}").ReceivedAwdpServiceStatusChange(model);
        }
    }

    static async Task DelayUntil(DateTimeOffset until, CancellationToken token)
    {
        var delay = until - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, token);
    }

    static AwdpGameStatusModel ToStatusModel(int gameId, AwdpRound round, AwdpService[] services) =>
        new()
        {
            GameId = gameId,
            CurrentRound = round.RoundNumber,
            RoundStartTime = round.StartTime,
            AttackPhaseMinutes = services.FirstOrDefault()?.AttackPhaseMinutes ?? 0,
            PatchPhaseMinutes = services.FirstOrDefault()?.PatchPhaseMinutes ?? 0,
            Status = round.Status
        };

    static bool TryGetSchedule(AwdpService[] services, out int attackMinutes, out int patchMinutes,
        out int totalRounds, out string error)
    {
        attackMinutes = services[0].AttackPhaseMinutes;
        patchMinutes = services[0].PatchPhaseMinutes;
        totalRounds = services[0].TotalRounds;

        if (attackMinutes <= 0 || patchMinutes <= 0 || totalRounds <= 0)
        {
            error = "AWDP 轮次配置必须大于 0";
            return false;
        }

        var expectedAttackMinutes = attackMinutes;
        var expectedPatchMinutes = patchMinutes;
        var expectedTotalRounds = totalRounds;

        if (services.Any(s => s.AttackPhaseMinutes != expectedAttackMinutes ||
                              s.PatchPhaseMinutes != expectedPatchMinutes ||
                              s.TotalRounds != expectedTotalRounds))
        {
            error = "同一比赛内所有 AWDP 服务必须使用相同的轮次配置";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public void Dispose()
    {
        _shutdown.Dispose();
        foreach (var loop in _gameLoops.Values)
            loop.Dispose();
    }
}
