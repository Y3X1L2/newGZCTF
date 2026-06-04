using GZCTF.Models.Data;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class AwdRoundService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AwdRoundService> _logger;
    private readonly Dictionary<int, AwdGameState> _gameStates = new();

    public AwdRoundService(IServiceProvider serviceProvider, ILogger<AwdRoundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AWD Round Service started");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public void StartGame(Game game)
    {
        if (_gameStates.ContainsKey(game.Id)) return;

        _gameStates[game.Id] = new AwdGameState
        {
            GameId = game.Id,
            CurrentRound = 0,
            IsRunning = true
        };

        _ = Task.Run(async () => await RunGameLoop(game));
    }

    public void StopGame(int gameId)
    {
        if (_gameStates.TryGetValue(gameId, out var state))
        {
            state.IsRunning = false;
            _gameStates.Remove(gameId);
        }
    }

    public int? GetCurrentRound(int gameId)
    {
        return _gameStates.TryGetValue(gameId, out var state) ? state.CurrentRound : null;
    }

    private async Task RunGameLoop(Game game)
    {
        using var scope = _serviceProvider.CreateScope();
        var awdRepo = scope.ServiceProvider.GetRequiredService<IAwdRepository>();
        var instanceService = scope.ServiceProvider.GetRequiredService<AwdInstanceService>();
        var checkerService = scope.ServiceProvider.GetRequiredService<AwdCheckerService>();
        var scoreService = scope.ServiceProvider.GetRequiredService<AwdScoreService>();
        var eventRepo = scope.ServiceProvider.GetRequiredService<IGameEventRepository>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var services = await awdRepo.GetServicesByGame(game.Id);
        if (services.Length == 0) return;

        var service = services[0];
        var roundDuration = TimeSpan.FromMinutes(service.RoundDurationMinutes);
        var totalRounds = service.TotalRounds;

        // 预创建实例
        await instanceService.CreateInstancesForGame(game);

        for (int roundNum = 1; roundNum <= totalRounds; roundNum++)
        {
            if (!_gameStates.TryGetValue(game.Id, out var state) || !state.IsRunning)
                break;

            state.CurrentRound = roundNum;

            // 1. 创建轮次
            var round = new AwdRound
            {
                GameId = game.Id,
                RoundNumber = roundNum,
                StartTime = DateTimeOffset.UtcNow,
                Status = AwdRoundStatus.Running
            };
            await awdRepo.CreateRound(round);

            // 2. 生成 Flag
            var participations = game.Participations.Where(p => p.Status == ParticipationStatus.Accepted).ToList();
            var flags = new List<AwdFlag>();

            foreach (var svc in services)
            {
                foreach (var part in participations)
                {
                    var flagValue = FlagHelper.GenerateFlag(32);
                    flags.Add(new AwdFlag
                    {
                        RoundId = round.Id,
                        ServiceId = svc.Id,
                        TeamId = part.TeamId,
                        FlagValue = flagValue
                    });
                }
            }

            await awdRepo.CreateFlags(flags);

            // 3. 注入 Flag（重启容器）
            foreach (var svc in services)
            {
                foreach (var part in participations)
                {
                    var flag = flags.FirstOrDefault(f => f.ServiceId == svc.Id && f.TeamId == part.TeamId);
                    var instances = await context.AwdServiceInstances
                        .Where(i => i.ServiceId == svc.Id && i.TeamId == part.TeamId)
                        .ToListAsync();

                    foreach (var instance in instances)
                    {
                        await instanceService.ResetInstance(instance.Id, flag?.FlagValue);
                    }
                }
            }

            // 4. 广播轮次开始
            await eventRepo.AddEvent(new GameEvent
            {
                GameId = game.Id,
                Type = EventType.AwdRoundStart,
                Values = [$"Round {roundNum} started"]
            });

            // 5. 执行 Checker
            await checkerService.RunCheckerForRound(round, services, participations);

            // 6. 等待轮次结束
            await Task.Delay(roundDuration);

            // 7. 结算得分
            await scoreService.CalculateRoundScores(round, game);

            round.Status = AwdRoundStatus.Finished;
            round.EndTime = DateTimeOffset.UtcNow;
            context.AwdRounds.Update(round);
            await context.SaveChangesAsync();
        }

        _gameStates.Remove(game.Id);
        _logger.LogInformation("AWD game {GameId} finished", game.Id);
    }

    public void Dispose()
    {
    }
}

public class AwdGameState
{
    public int GameId { get; set; }
    public int CurrentRound { get; set; }
    public bool IsRunning { get; set; }
}
