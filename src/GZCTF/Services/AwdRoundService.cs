using System.Collections.Concurrent;
using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Utils;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services;

public class AwdRoundService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AwdRoundService> _logger;
    private readonly ConcurrentDictionary<int, AwdGameState> _gameStates = new();

    public AwdRoundService(IServiceProvider serviceProvider, ILogger<AwdRoundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AWD Round Service starting, recovering active games...");

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var activeRounds = await context.AwdRounds
            .Where(r => r.Status == AwdRoundStatus.Running)
            .GroupBy(r => r.GameId)
            .ToListAsync(cancellationToken);

        foreach (var group in activeRounds)
        {
            var gameId = group.Key;
            var maxRound = group.Max(r => r.RoundNumber);
            _logger.LogInformation("Recovering AWD game {GameId} at round {Round}", gameId, maxRound);

            var game = await context.Games.FindAsync([gameId], cancellationToken);
            if (game is not null)
            {
                var state = new AwdGameState
                {
                    GameId = gameId,
                    CurrentRound = maxRound,
                    IsRunning = true
                };
                _gameStates[gameId] = state;
                _ = Task.Run(async () => await RunGameLoop(game, maxRound + 1));
            }
        }

        _logger.LogInformation("AWD Round Service started");
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

        _ = Task.Run(async () =>
        {
            try
            {
                await RunGameLoop(game);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AWD game loop crashed for game {GameId}", game.Id);
                _gameStates.TryRemove(game.Id, out _);
            }
        });
    }

    public void StopGame(int gameId)
    {
        if (_gameStates.TryRemove(gameId, out var state))
        {
            state.IsRunning = false;
        }
    }

    public int? GetCurrentRound(int gameId)
    {
        return _gameStates.TryGetValue(gameId, out var state) ? state.CurrentRound : null;
    }

    private async Task RunGameLoop(Game game, int startRound = 1)
    {
        using var scope = _serviceProvider.CreateScope();
        var awdRepo = scope.ServiceProvider.GetRequiredService<IAwdRepository>();
        var instanceService = scope.ServiceProvider.GetRequiredService<AwdInstanceService>();
        var checkerService = scope.ServiceProvider.GetRequiredService<AwdCheckerService>();
        var scoreService = scope.ServiceProvider.GetRequiredService<AwdScoreService>();
        var eventRepo = scope.ServiceProvider.GetRequiredService<IGameEventRepository>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<MonitorHub, IMonitorClient>>();

        var services = await awdRepo.GetServicesByGame(game.Id);
        if (services.Length == 0) return;

        // Validate all services share the same round configuration
        var roundDurationMinutes = services[0].RoundDurationMinutes;
        var totalRounds = services[0].TotalRounds;
        if (services.Any(s => s.RoundDurationMinutes != roundDurationMinutes || s.TotalRounds != totalRounds))
        {
            _logger.LogError("AWD game {GameId} has services with inconsistent round configuration", game.Id);
            return;
        }

        var roundDuration = TimeSpan.FromMinutes(roundDurationMinutes);

        // Pre-create instances only on fresh start
        if (startRound == 1)
        {
            await instanceService.CreateInstancesForGame(game);
        }

        for (int roundNum = startRound; roundNum <= totalRounds; roundNum++)
        {
            if (!_gameStates.TryGetValue(game.Id, out var state) || !state.IsRunning)
                break;

            state.CurrentRound = roundNum;

            // 1. Create round
            var round = new AwdRound
            {
                GameId = game.Id,
                RoundNumber = roundNum,
                StartTime = DateTimeOffset.UtcNow,
                Status = AwdRoundStatus.Running
            };
            await awdRepo.CreateRound(round);

            // 2. Generate flags
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

            // 3. Inject flags (reset containers)
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

            // 4. Broadcast round start via SignalR
            await eventRepo.AddEvent(new GameEvent
            {
                GameId = game.Id,
                Type = EventType.AwdRoundStart,
                Values = [$"Round {roundNum} started"]
            });

            await hubContext.Clients.All.ReceivedAwdRoundChange(new AwdGameStatusModel
            {
                GameId = game.Id,
                CurrentRound = roundNum,
                RoundStartTime = round.StartTime,
                RoundDurationMinutes = roundDurationMinutes,
                Status = AwdRoundStatus.Running
            });

            _logger.LogInformation("AWD game {GameId} round {Round} started", game.Id, roundNum);

            // 5. Execute checkers
            await checkerService.RunCheckerForRound(round, services, participations);

            // 6. Wait for round end
            await Task.Delay(roundDuration);

            // 7. Calculate scores
            await scoreService.CalculateRoundScores(round, game);

            round.Status = AwdRoundStatus.Finished;
            round.EndTime = DateTimeOffset.UtcNow;
            context.AwdRounds.Update(round);
            await context.SaveChangesAsync();

            _logger.LogInformation("AWD game {GameId} round {Round} finished", game.Id, roundNum);
        }

        _gameStates.TryRemove(game.Id, out _);
        _logger.LogInformation("AWD game {GameId} all rounds completed", game.Id);
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
