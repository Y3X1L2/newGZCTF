using System.Diagnostics;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services;
using GZCTF.Services.Cache;
using GZCTF.Services.Config;
using GZCTF.Utils;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Repositories;

public class GameRepository(
    ILogger<GameRepository> logger,
    CacheHelper cacheHelper,
    IDivisionRepository divisionRepository,
    IGameChallengeRepository challengeRepository,
    IParticipationRepository participationRepository,
    IConfigService configService,
    AppDbContext context) : RepositoryBase(context), IGameRepository
{
    private readonly byte[] _xorKey = configService.GetXorKey();

    public override Task<int> CountAsync(CancellationToken token = default) => Context.Games.CountAsync(token);

    public Task<bool> HasGameAsync(int id, CancellationToken token = default)
        => Context.Games.AnyAsync(g => g.Id == id, token);

    public async Task<Game?> CreateGame(Game game, CancellationToken token = default)
    {
        game.GenerateKeyPair(_xorKey);

        if (_xorKey.Length == 0)
            logger.SystemLog(StaticLocalizer[nameof(Resources.Program.GameRepository_XorKeyNotConfigured)],
                TaskStatus.Pending,
                LogLevel.Warning);

        await Context.AddAsync(game, token);
        await SaveAsync(token);

        await cacheHelper.FlushGameListCache(token);
        await cacheHelper.FlushRecentGamesCache(token);

        return game;
    }

    public async Task UpdateGame(Game game, CancellationToken token = default)
    {
        await SaveAsync(token);

        await cacheHelper.RemoveAsync(CacheKey.GameCache(game.Id), token);
        await cacheHelper.FlushScoreboardCache(game.Id, token);
        await cacheHelper.FlushRecentGamesCache(token);
        await cacheHelper.FlushGameListCache(token);
    }

    public string GetToken(Game game, Team team) => $"{team.Id}:{game.Sign($"GZCTF_TEAM_{team.Id}", _xorKey)}";

    public Task<Game?> GetGameById(int id, CancellationToken token = default)
        => Context.Games.FirstOrDefaultAsync(x => x.Id == id, token);

    public async Task<GameJoinCheckInfoModel>
        GetCheckInfo(Game game, UserInfo user, CancellationToken token = default) =>
        new()
        {
            JoinedTeams = await participationRepository.GetJoinedTeams(game, user, token),
            JoinableDivisions = await divisionRepository.GetJoinableDivisionIds(game.Id, token),
        };

    public Task LoadDivisions(Game game, CancellationToken token = default)
        => Context.Entry(game).Collection(g => g.Divisions!).LoadAsync(token);

    public Task<int[]> GetUpcomingGames(CancellationToken token = default) =>
        Context.Games.Where(g => g.StartTimeUtc > DateTime.UtcNow
                                 && g.StartTimeUtc - DateTime.UtcNow < TimeSpan.FromMinutes(15))
            .OrderBy(g => g.StartTimeUtc).Select(g => g.Id).ToArrayAsync(token);

    public Task<BasicGameInfoModel[]> FetchGameList(int count, int skip, CancellationToken token) =>
        Context.Games.Where(g => !g.Hidden)
            .OrderByDescending(g => g.StartTimeUtc).Skip(skip).Take(count)
            .Select(game => new BasicGameInfoModel
            {
                Id = game.Id,
                Title = game.Title,
                Summary = game.Summary,
                PosterHash = game.PosterHash,
                StartTimeUtc = game.StartTimeUtc,
                EndTimeUtc = game.EndTimeUtc,
                TeamMemberCountLimit = game.TeamMemberCountLimit
            }).ToArrayAsync(token);

    public async Task<DetailedGameInfoModel?> GetDetailedGameInfo(int gameId, CancellationToken token = default)
    {
        var game = await cacheHelper.GetOrCreateAsync(logger, CacheKey.GameCache(gameId),
            entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromDays(2);
                return Context.Games.AsNoTracking()
                    .Include(g => g.Divisions)
                    .FirstOrDefaultAsync(x => x.Id == gameId, token);
            }, token: token);

        return game is null ? null : DetailedGameInfoModel.FromGame(game);
    }

    public async Task<ArrayResponse<BasicGameInfoModel>> GetGameInfo(int count = 20, int skip = 0,
        CancellationToken token = default)
    {
        var total = await Context.Games.CountAsync(game => !game.Hidden, token);
        if (skip >= total)
            return new([], total);

        if (skip + count > 100)
            return new(await FetchGameList(count, skip, token), total);

        var games = await cacheHelper.GetOrCreateAsync(logger, CacheKey.GameList,
            entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromDays(2);
                return FetchGameList(100, 0, token);
            }, token: token);

        return new(games.Skip(skip).Take(count).ToArray(), total);
    }

    public Task<DataWithModifiedTime<BasicGameInfoModel[]>> GetRecentGames(CancellationToken token = default)
        => cacheHelper.GetOrCreateAsync(logger, CacheKey.RecentGames,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                var games = await GenRecentGames(token);
                return new DataWithModifiedTime<BasicGameInfoModel[]>(games, DateTimeOffset.UtcNow);
            }, token: token);

    public Task<BasicGameInfoModel[]> GenRecentGames(CancellationToken token = default) =>
        // sort by following rules:
        // 1. ongoing games > upcoming games > ended games
        // 2. ongoing games: by end time, ascending
        // 3. upcoming games: by start time, ascending
        // 4. ended games: by end time, descending
        Context.Games
            .AsNoTracking()
            .Where(g => !g.Hidden)
            .OrderBy(g =>
                g.EndTimeUtc <= DateTimeOffset.UtcNow
                    ? DateTimeOffset.UtcNow - g.EndTimeUtc // ended games
                    : g.StartTimeUtc >= DateTimeOffset.UtcNow
                        ? g.StartTimeUtc - DateTimeOffset.UtcNow // upcoming games
                        : DateTimeOffset.UtcNow - g.StartTimeUtc < g.EndTimeUtc - DateTimeOffset.UtcNow
                            ? DateTimeOffset.UtcNow - g.StartTimeUtc
                            : g.EndTimeUtc - DateTimeOffset.UtcNow)
            .Take(50) // limit to 50 games
            .Select(game => new BasicGameInfoModel
            {
                Id = game.Id,
                Title = game.Title,
                Summary = game.Summary,
                PosterHash = game.PosterHash,
                StartTimeUtc = game.StartTimeUtc,
                EndTimeUtc = game.EndTimeUtc,
                TeamMemberCountLimit = game.TeamMemberCountLimit
            })
            .ToArrayAsync(token);

    public Task<ScoreboardModel> GetScoreboard(Game game, CancellationToken token = default)
        => cacheHelper.GetOrCreateAsync(logger, CacheKey.ScoreBoard(game.Id),
            entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromDays(7);
                return GenScoreboard(game, token);
            }, token: token);

    public Task<ScoreboardModel?> TryGetScoreboard(int gameId, CancellationToken token = default)
        => cacheHelper.GetAsync<ScoreboardModel>(CacheKey.ScoreBoard(gameId), token);

    public Task<bool> IsGameClosed(int gameId, CancellationToken token = default)
        => Context.Games.AnyAsync(game =>
            game.Id == gameId && game.EndTimeUtc < DateTimeOffset.UtcNow && !game.PracticeMode, token);

    public async Task<ScoreboardModel> GetScoreboardWithMembers(Game game, CancellationToken token = default)
    {
        // In most cases, we can get the scoreboard from the cache
        var scoreboard = await GetScoreboard(game, token);

        // load team info & participants
        var ids = scoreboard.Items.Values.Select(i => i.Id).ToArray();
        var teams = await Context.Teams
            .IgnoreAutoIncludes().Include(t => t.Captain)
            .Where(t => ids.Contains(t.Id))
            .Include(t => t.Members).ToHashSetAsync(token);

        // load participants with team id and game id, select all UserInfos
        var participants = await Context.UserParticipations
            .Where(p => ids.Contains(p.TeamId) && p.GameId == game.Id)
            .Include(p => p.User)
            .Select(p => new { p.TeamId, p.User })
            .GroupBy(p => p.TeamId)
            .ToDictionaryAsync(g => g.Key,
                g => g.Select(p => p.User).ToHashSet(), token);

        // update scoreboard items
        foreach (var item in scoreboard.Items.Values)
        {
            if (teams.FirstOrDefault(t => t.Id == item.Id) is { } team)
                item.TeamInfo = team;

            if (participants.TryGetValue(item.Id, out var users))
                item.Participants = users;
        }

        return scoreboard;
    }

    public async Task<TaskStatus> DeleteGame(Game game, CancellationToken token = default)
    {
        var trans = await BeginTransactionAsync(token);

        try
        {
            var count = await Context.GameChallenges.Where(i => i.Game == game).CountAsync(token);
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.GameRepository_GameDeletionChallenges), game.Title,
                    count], TaskStatus.Pending,
                LogLevel.Debug
            );

            foreach (var chal in await Context.GameChallenges.Where(c => c.Game == game)
                         .ToArrayAsync(token))
                await challengeRepository.RemoveChallenge(chal, false, token);

            count = await Context.Participations.Where(i => i.Game == game).CountAsync(token);

            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.GameRepository_GameDeletionTeams), game.Title, count],
                TaskStatus.Pending, LogLevel.Debug
            );

            foreach (var part in await Context.Participations.Where(p => p.Game == game).ToArrayAsync(token))
                await participationRepository.RemoveParticipation(part, false, token);

            Context.Remove(game);

            await SaveAsync(token);
            await trans.CommitAsync(token);

            await cacheHelper.FlushGameListCache(token);
            await cacheHelper.FlushRecentGamesCache(token);

            await cacheHelper.RemoveAsync(CacheKey.ScoreBoard(game.Id), token);

            return TaskStatus.Success;
        }
        catch (Exception e)
        {
            logger.SystemLog(StaticLocalizer[nameof(Resources.Program.Game_DeletionFailed)], TaskStatus.Pending,
                LogLevel.Debug);
            logger.SystemLog(e.Message, TaskStatus.Failed, LogLevel.Warning);
            await trans.RollbackAsync(token);

            return TaskStatus.Failed;
        }
    }

    public async Task DeleteAllWriteUps(Game game, CancellationToken token = default)
    {
        await Context.Entry(game).Collection(g => g.Participations).LoadAsync(token);

        logger.SystemLog(
            StaticLocalizer[nameof(Resources.Program.GameRepository_GameDeletionTeams), game.Title,
                game.Participations.Count],
            TaskStatus.Pending,
            LogLevel.Debug);

        foreach (var part in game.Participations)
            await participationRepository.DeleteParticipationWriteUp(part, token);
    }

    public Task<Game[]> GetGames(int count, int skip, CancellationToken token) =>
        Context.Games.OrderByDescending(g => g.Id).Skip(skip).Take(count).ToArrayAsync(token);

    // Generates a scoreboard snapshot inside a transaction so ranks and solve metadata stay consistent.
    public async Task<ScoreboardModel> GenScoreboard(Game game, CancellationToken token = default)
    {
        Dictionary<int, ScoreboardItem> items;
        Dictionary<int, ChallengeInfo> challenges;
        Dictionary<int, DivisionItem> divisions;
        Dictionary<int, ChallengeScoreMeta> challengeMetas;
        List<SolveSnapshot> solveSnapshots;
        List<FlagContext> allFlags;
        Dictionary<(int ParticipationId, int ChallengeId), int> dynamicInstanceFlagIds;

        // 0. Begin transaction
        await using (var trans = await Context.Database.BeginTransactionAsync(token))
        {
            // 1. Fetch all divisions for this game
            var divisionsQuery = await Context.Divisions.AsNoTracking().IgnoreAutoIncludes()
                .Where(d => d.GameId == game.Id)
                .Include(d => d.ChallengeConfigs)
                .Select(d => new
                {
                    d.Id,
                    d.Name,
                    d.DefaultPermissions,
                    ChallengeConfigs = d.ChallengeConfigs.Select(c => new DivisionChallengeItem
                    {
                        ChallengeId = c.ChallengeId,
                        Permissions = c.Permissions
                    }).ToList()
                })
                .ToListAsync(token);

            divisions = divisionsQuery.ToDictionary(
                d => d.Id,
                d => new DivisionItem
                {
                    Id = d.Id,
                    Name = d.Name,
                    DefaultPermissions = d.DefaultPermissions,
                    ChallengeConfigs = d.ChallengeConfigs.ToDictionary(c => c.ChallengeId)
                });

            // 2. Fetch all teams with their members from Participations, into ScoreboardItem
            items = await Context.Participations
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(p => p.GameId == game.Id && p.Status == ParticipationStatus.Accepted)
                .Include(p => p.Team)
                .Select(p => new ScoreboardItem
                {
                    Id = p.Team.Id,
                    Bio = p.Team.Bio,
                    Name = p.Team.Name,
                    Avatar = p.Team.AvatarUrl,
                    DivisionId = p.DivisionId,
                    ParticipantId = p.Id,
                    TeamInfo = p.Team,
                    // pending fields: SolvedChallenges
                    Rank = 0,
                    LastSubmissionTime = DateTimeOffset.MinValue
                    // update: only store accepted challenges
                }).ToDictionaryAsync(i => i.ParticipantId, token);

            // 3. Fetch all challenges from GameChallenges, capture scoring metadata
            var challengeRecords = await Context.GameChallenges
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(c => c.GameId == game.Id && c.IsEnabled)
                .OrderBy(c => c.Category)
                .ThenBy(c => c.Title)
                .Select(c => new ChallengeRecord
                (
                    c.Id,
                    new ChallengeScoreMeta(
                        c.Type,
                        c.OriginalScore,
                        c.MinScoreRate,
                        c.Difficulty),
                    new ChallengeInfo
                    {
                        Id = c.Id,
                        Title = c.Title,
                        Category = c.Category,
                        Score = c.OriginalScore,
                        SolvedCount = 0,
                        DeadlineUtc = c.DeadlineUtc,
                        DisableBloodBonus = c.DisableBloodBonus
                    }
                ))
                .ToDictionaryAsync(c => c.Id, c => c, token);

            challenges = challengeRecords.ToDictionary(c => c.Key, c => c.Value.Info);
            challengeMetas = challengeRecords.ToDictionary(c => c.Key, c => c.Value.Meta);

            var challengeIds = challengeRecords.Keys.ToArray();

            // 3.5. fetch all FlagContexts for challenges in this game
            allFlags = await Context.FlagContexts
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(f => f.ChallengeId != null && challengeIds.Contains(f.ChallengeId.Value))
                .OrderBy(f => f.OrderIndex)
                .ToListAsync(token);

            var dynamicChallengeIds = challengeRecords
                .Where(c => c.Value.Meta.Type.IsDynamic())
                .Select(c => c.Key)
                .ToArray();

            dynamicInstanceFlagIds = await Context.GameInstances
                .AsNoTracking()
                .IgnoreAutoIncludes()
                .Where(i => dynamicChallengeIds.Contains(i.ChallengeId) && i.FlagId != null)
                .Select(i => new { i.ParticipationId, i.ChallengeId, FlagId = i.FlagId!.Value })
                .ToDictionaryAsync(i => (i.ParticipationId, i.ChallengeId), i => i.FlagId, token);

            // 4. fetch all recorded first solves for this game
            solveSnapshots = await Context.FirstSolves
                .AsNoTracking()
                .Join(Context.Participations.AsNoTracking(),
                    fs => fs.ParticipationId,
                    participation => participation.Id,
                    (fs, participation) => new { fs, participation })
                .Where(x => x.participation.GameId == game.Id &&
                            x.participation.Status == ParticipationStatus.Accepted &&
                            challengeIds.Contains(x.fs.ChallengeId))
                .Join(Context.Submissions.AsNoTracking().IgnoreAutoIncludes(),
                    x => x.fs.SubmissionId,
                    submission => submission.Id,
                    (x, submission) => new SolveSnapshot(
                        x.fs.ChallengeId,
                        x.fs.FlagId!.Value,
                        x.fs.ParticipationId,
                        submission.SubmitTimeUtc,
                        submission.User == null ? string.Empty : submission.User.UserName ?? string.Empty))
                .ToListAsync(token);

            await trans.CommitAsync(token);
        }

        // Prepare solve metadata for scoring and statistics
        // Track accepted counts per-flag for dynamic decay
        Dictionary<(int ChallengeId, int FlagId), int> flagAcceptedCounts = [];
        // Track unique teams that solved each challenge (any flag)
        Dictionary<int, HashSet<int>> challengeSolverTeams = [];
        List<ScoreboardSolve> solves = [];

        foreach (var snapshot in solveSnapshots)
        {
            if (!challengeMetas.TryGetValue(snapshot.ChallengeId, out var challengeMeta))
                continue;

            if (challengeMeta.Type.IsDynamic() &&
                (!dynamicInstanceFlagIds.TryGetValue((snapshot.ParticipantId, snapshot.ChallengeId),
                     out var expectedFlagId) ||
                 expectedFlagId != snapshot.FlagId))
                continue;

            if (!items.TryGetValue(snapshot.ParticipantId, out var scoreboardItem))
                continue;

            var division = scoreboardItem.DivisionId is { } div ? divisions.GetValueOrDefault(div) : null;

            // Check if submission is within game time window
            var withinGameWindow = snapshot.SubmitTimeUtc >= game.StartTimeUtc &&
                                   snapshot.SubmitTimeUtc < game.EndTimeUtc;

            // Check if submission is within challenge deadline (if deadline is set)
            var challengeDeadline = challenges[snapshot.ChallengeId].DeadlineUtc;
            var withinDeadline = !challengeDeadline.HasValue ||
                                 snapshot.SubmitTimeUtc <= challengeDeadline.Value;

            // Submission is only eligible for scoring if within both game window and deadline
            var withinValidSubmissionWindow = withinGameWindow && withinDeadline;

            var scoreEligible = withinValidSubmissionWindow &&
                                CheckDivisionPermission(division, GamePermission.GetScore, snapshot.ChallengeId);

            var affectDynamicScore = withinValidSubmissionWindow &&
                                     CheckDivisionPermission(division, GamePermission.AffectDynamicScore,
                                         snapshot.ChallengeId);

            if (affectDynamicScore)
            {
                var flagKey = ScoreBucket(snapshot.ChallengeId, snapshot.FlagId, challengeMeta);
                flagAcceptedCounts[flagKey] =
                    flagAcceptedCounts.GetValueOrDefault(flagKey) + 1;
            }

            // Track challenge-level solver teams for SolvedCount
            if (affectDynamicScore)
            {
                if (!challengeSolverTeams.ContainsKey(snapshot.ChallengeId))
                    challengeSolverTeams[snapshot.ChallengeId] = [];
                challengeSolverTeams[snapshot.ChallengeId].Add(snapshot.ParticipantId);
            }

            var bloodEligible = withinValidSubmissionWindow &&
                                CheckDivisionPermission(division, GamePermission.GetBlood, snapshot.ChallengeId);

            solves.Add(new ScoreboardSolve(
                snapshot.ChallengeId,
                snapshot.FlagId,
                snapshot.ParticipantId,
                snapshot.SubmitTimeUtc,
                snapshot.UserName,
                scoreEligible,
                bloodEligible));
        }

        // Set challenge-level info: SolvedCount and TotalFlags
        foreach ((int challengeId, ChallengeInfo info) in challenges)
        {
            info.SolvedCount = challengeSolverTeams.GetValueOrDefault(challengeId)?.Count ?? 0;
            info.TotalFlags =
                challengeMetas.TryGetValue(challengeId, out var meta) && meta.Type.IsDynamic()
                    ? 1
                    : allFlags.Count(f => f.ChallengeId == challengeId);
        }

        // 5. Group solves by (ChallengeId, FlagId) and process per-flag
        var noBonus = game.BloodBonus.NoBonus;

        float[] bloodFactors =
        [
            game.BloodBonus.FirstBloodFactor,
            game.BloodBonus.SecondBloodFactor,
            game.BloodBonus.ThirdBloodFactor
        ];

        var flagGroups = solves
            .GroupBy(s => ScoreBucket(s.ChallengeId, s.FlagId, challengeMetas[s.ChallengeId]))
            .OrderBy(g => g.Min(s => s.SubmitTimeUtc))
            .ToList();

        foreach (var flagGroup in flagGroups)
        {
            var challengeId = flagGroup.Key.ChallengeId;
            if (!challengeMetas.TryGetValue(challengeId, out var challengeMeta))
                continue;
            if (!challenges.TryGetValue(challengeId, out var challengeInfo))
                continue;

            var flag = challengeMeta.Type.IsDynamic()
                ? null
                : allFlags.FirstOrDefault(f => f.Id == flagGroup.Key.FlagId);
            if (!challengeMeta.Type.IsDynamic() && flag is null)
                continue;

            // Determine this flag's base score
            var flagBaseScore = flag?.ScoreMode == FlagScoreMode.FixedScore
                ? flag.FixedScore
                : challengeMeta.OriginalScore;

            // Get accepted count for this flag (for dynamic decay formula)
            var acceptedForFlag =
                flagAcceptedCounts.GetValueOrDefault((challengeId, flagGroup.Key.FlagId));

            // Calculate current score for this flag using challenge-level decay formula
            var flagCurrentScore = GameChallenge.CalculateChallengeScore(
                flagBaseScore,
                challengeMeta.MinScoreRate,
                challengeMeta.Difficulty,
                acceptedForFlag);

            // Iterate solves in time order, assign bloods and scores per-flag
            var flagBloods = new List<(int ParticipantId, SubmissionType BloodType)>();

            foreach (var solve in flagGroup.OrderBy(s => s.SubmitTimeUtc))
            {
                if (!items.TryGetValue(solve.ParticipantId, out var scoreboardItem))
                    continue;

                var item = new ChallengeItem
                {
                    Id = solve.ChallengeId,
                    FlagId = solve.FlagId,
                    ParticipantId = solve.ParticipantId,
                    SubmitTimeUtc = solve.SubmitTimeUtc,
                    UserName = solve.UserName,
                    Type = SubmissionType.Normal,
                    Score = 0
                };

                // 5.1. generate bloods per-flag
                if (solve.BloodEligible && challengeInfo is { DisableBloodBonus: false } &&
                    flagBloods.Count < 3)
                {
                    item.Type = flagBloods.Count switch
                    {
                        0 => SubmissionType.FirstBlood,
                        1 => SubmissionType.SecondBlood,
                        2 => SubmissionType.ThirdBlood,
                        _ => throw new UnreachableException()
                    };

                    flagBloods.Add((solve.ParticipantId, item.Type));

                    challengeInfo.Bloods.Add(new Blood
                    {
                        Id = scoreboardItem.Id,
                        Avatar = scoreboardItem.Avatar,
                        Name = scoreboardItem.Name,
                        SubmitTimeUtc = item.SubmitTimeUtc
                    });
                }

                // 5.2. calculate score
                if (solve.ScoreEligible)
                {
                    item.Score = noBonus
                        ? flagCurrentScore
                        : item.Type switch
                        {
                            SubmissionType.Unaccepted => throw new UnreachableException(),
                            SubmissionType.FirstBlood =>
                                Convert.ToInt32(flagCurrentScore * bloodFactors[0]),
                            SubmissionType.SecondBlood =>
                                Convert.ToInt32(flagCurrentScore * bloodFactors[1]),
                            SubmissionType.ThirdBlood =>
                                Convert.ToInt32(flagCurrentScore * bloodFactors[2]),
                            SubmissionType.Normal => flagCurrentScore,
                            _ => throw new ArgumentException(nameof(item.Type))
                        };
                }
                else
                {
                    item.Score = 0;
                }

                // 5.3. update scoreboard item
                scoreboardItem.SolvedChallenges.Add(item);

                if (!solve.ScoreEligible)
                    continue;

                // only update last submission time for eligible solves,
                // to prevent incorrectly ranking teams with ineligible
                // late submissions above teams with eligible early submissions
                scoreboardItem.CtfScore += item.Score;
                scoreboardItem.LastSubmissionTime = item.SubmitTimeUtc;
            }
        }

        var awdpStates = game.GameType is GameType.AWDP or GameType.Mixed
            ? await GetAwdpScoreStates(game.Id, token)
            : new Dictionary<int, AwdpScoreState>();
        var penetrationStates = game.GameType is GameType.Penetration or GameType.Mixed
            ? await GetPenetrationScoreStates(game.Id, token)
            : new Dictionary<int, PenetrationScoreState>();

        if (awdpStates.Count > 0)
        {
            var itemsByTeamId = items.Values.ToDictionary(i => i.Id);
            foreach (var (teamId, state) in awdpStates)
            {
                if (!itemsByTeamId.TryGetValue(teamId, out var item))
                    continue;

                item.AwdScore = state.TotalScore;
                if (state.LastScoreTime > item.LastSubmissionTime)
                    item.LastSubmissionTime = state.LastScoreTime;
            }
        }

        if (penetrationStates.Count > 0)
        {
            var itemsByTeamId = items.Values.ToDictionary(i => i.Id);
            foreach (var (teamId, state) in penetrationStates)
            {
                if (!itemsByTeamId.TryGetValue(teamId, out var item))
                    continue;

                item.PentestScore = state.TotalScore;
                if (state.LastScoreTime > item.LastSubmissionTime)
                    item.LastSubmissionTime = state.LastScoreTime;
            }
        }

        // 6. sort scoreboard items by score and last submission time
        items = items.Values
            .OrderByDescending(i => i.Score)
            .ThenBy(i => i.LastSubmissionTime)
            .ToDictionary(i => i.Id); // team id -> scoreboard item

        // 7. update rank and organization rank
        var currentRank = 1;
        Dictionary<int, int> ranks = [];
        Dictionary<int, HashSet<int>> topTeams = new() { [0] = [] };

        foreach (var item in items.Values)
        {
            var division = item.DivisionId is { } div ? divisions.GetValueOrDefault(div) : null;

            if (CheckDivisionPermission(division, GamePermission.RankOverall))
            {
                item.Rank = currentRank++;

                if (item.Rank <= 10)
                    topTeams[0].Add(item.Id);
            }

            if (division is null)
                continue;

            if (ranks.TryGetValue(division.Id, out var rank))
            {
                item.DivisionRank = rank + 1;
                ranks[division.Id]++;
                if (item.DivisionRank <= 10)
                    topTeams[division.Id].Add(item.Id);
            }
            else
            {
                item.DivisionRank = 1;
                ranks[division.Id] = 1;
                topTeams[division.Id] = [item.Id];
            }
        }

        // 7. generate top timelines by solved challenges
        var timelines = topTeams.ToDictionary(
            i => i.Key,
            i => i.Value.Select(tid =>
                {
                    var item = items[tid];
                    return new TopTimeLine
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Items = item.SolvedChallenges
                            .Select(c => new ScoreTimelineEvent(c.SubmitTimeUtc, c.Score))
                            .Concat(awdpStates.GetValueOrDefault(item.Id)?.TimelineEvents ??
                                    Enumerable.Empty<ScoreTimelineEvent>())
                            .Concat(penetrationStates.GetValueOrDefault(item.Id)?.TimelineEvents ??
                                    Enumerable.Empty<ScoreTimelineEvent>())
                            .OrderBy(e => e.Time)
                            .Aggregate(new List<TimeLine>(), (acc, e) =>
                            {
                                var last = acc.LastOrDefault();
                                acc.Add(new TimeLine { Score = (last?.Score ?? 0) + e.Score, Time = e.Time });
                                return acc;
                            })
                    };
                }
            )
        );

        // 8. construct the final scoreboard model
        var challengesDict = challenges
            .Values
            .GroupBy(c => c.Category)
            .ToDictionary(c => c.Key, c => c.AsEnumerable());

        return new()
        {
            Challenges = challengesDict,
            Items = items,
            Divisions = divisions,
            TimeLines = timelines,
            BloodBonusValue = game.BloodBonus.Val
        };
    }

    private readonly record struct ChallengeRecord(
        int Id,
        ChallengeScoreMeta Meta,
        ChallengeInfo Info);

    private readonly record struct ChallengeScoreMeta(
        ChallengeType Type,
        int OriginalScore,
        double MinScoreRate,
        double Difficulty);

    private readonly record struct SolveSnapshot(
        int ChallengeId,
        int FlagId,
        int ParticipantId,
        DateTimeOffset SubmitTimeUtc,
        string? UserName);

    private readonly record struct ScoreboardSolve(
        int ChallengeId,
        int FlagId,
        int ParticipantId,
        DateTimeOffset SubmitTimeUtc,
        string? UserName,
        bool ScoreEligible,
        bool BloodEligible);

    private static (int ChallengeId, int FlagId) ScoreBucket(int challengeId, int flagId, ChallengeScoreMeta meta) =>
        (challengeId, meta.Type.IsDynamic() ? 0 : flagId);

    private readonly record struct ScoreTimelineEvent(DateTimeOffset Time, int Score);

    private sealed class AwdpScoreState
    {
        public int TotalScore { get; private set; }
        public DateTimeOffset LastScoreTime { get; private set; } = DateTimeOffset.MinValue;
        public List<ScoreTimelineEvent> TimelineEvents { get; } = [];

        public void Add(int score, DateTimeOffset? time)
        {
            if (score == 0)
                return;

            var eventTime = time ?? DateTimeOffset.MinValue;
            TotalScore += score;
            TimelineEvents.Add(new ScoreTimelineEvent(eventTime, score));

            if (eventTime > LastScoreTime)
                LastScoreTime = eventTime;
        }
    }

    private async Task<Dictionary<int, AwdpScoreState>> GetAwdpScoreStates(int gameId,
        CancellationToken token = default)
    {
        var services = await Context.AwdpServices.AsNoTracking()
            .Where(s => s.GameId == gameId)
            .Select(s => new
            {
                s.Id,
                s.AttackPoints,
                s.SlaPoints,
                s.PatchPoints,
                s.ServiceAbnormalPenalty
            })
            .ToDictionaryAsync(s => s.Id, token);

        Dictionary<int, AwdpScoreState> states = [];
        if (services.Count == 0)
            return states;

        AwdpScoreState GetState(int teamId)
        {
            if (!states.TryGetValue(teamId, out var state))
            {
                state = new AwdpScoreState();
                states[teamId] = state;
            }

            return state;
        }

        var flags = await Context.AwdpFlags.AsNoTracking()
            .Include(f => f.Round)
            .Where(f => f.Round.GameId == gameId && f.IsSubmitted && f.SubmittedByTeamId != null)
            .Select(f => new
            {
                f.ServiceId,
                f.SubmittedByTeamId,
                f.FirstSubmittedAt
            })
            .ToArrayAsync(token);

        foreach (var flag in flags)
        {
            if (flag.SubmittedByTeamId is not { } teamId ||
                !services.TryGetValue(flag.ServiceId, out var service))
                continue;

            GetState(teamId).Add(service.AttackPoints, flag.FirstSubmittedAt);
        }

        var checkerTasks = await Context.AwdpCheckerTasks.AsNoTracking()
            .Include(t => t.Round)
            .Where(t => t.Round.GameId == gameId && t.Status == CheckerStatus.OK)
            .Select(t => new
            {
                t.ServiceId,
                t.TeamId,
                t.ExecutedAt
            })
            .ToArrayAsync(token);

        foreach (var task in checkerTasks)
        {
            if (!services.TryGetValue(task.ServiceId, out var service))
                continue;

            GetState(task.TeamId).Add(service.SlaPoints, task.ExecutedAt);
        }

        var patches = await Context.AwdpPatchSubmissions.AsNoTracking()
            .Include(p => p.Round)
            .Where(p => p.Round.GameId == gameId)
            .ToArrayAsync(token);
        var resets = await Context.AwdpResetRecords.AsNoTracking()
            .Include(r => r.Service)
            .Where(r => r.Service.GameId == gameId)
            .ToArrayAsync(token);
        var recoveries = await Context.AwdpRecoveryRecords.AsNoTracking()
            .Include(r => r.Service)
            .Where(r => r.Service.GameId == gameId)
            .ToArrayAsync(token);

        foreach (var patch in patches
                     .GroupBy(p => new { p.RoundId, p.ServiceId, p.TeamId })
                     .Select(g =>
                     {
                         var first = g.First();
                         return AwdpPatchStateResolver.GetEffectivePatch(first.ServiceId, first.TeamId, g,
                             resets, recoveries, first.Round.StartTime, first.Round.EndTime);
                     })
                     .Where(p => p is not null)
                     .Cast<AwdpPatchSubmission>())
        {
            if (!services.TryGetValue(patch.ServiceId, out var service))
                continue;

            var delta = patch.FinalStatus switch
            {
                AwdpPatchStatus.ExpFailed => service.PatchPoints,
                AwdpPatchStatus.CheckerFailed => -service.ServiceAbnormalPenalty,
                _ => 0
            };

            GetState(patch.TeamId).Add(delta, patch.SubmittedAt);
        }

        return states;
    }

    private sealed class PenetrationScoreState
    {
        public int TotalScore { get; private set; }
        public DateTimeOffset LastScoreTime { get; private set; } = DateTimeOffset.MinValue;
        public List<ScoreTimelineEvent> TimelineEvents { get; } = [];

        public void Add(int score, DateTimeOffset time)
        {
            if (score == 0)
                return;

            TotalScore += score;
            TimelineEvents.Add(new ScoreTimelineEvent(time, score));
            if (time > LastScoreTime)
                LastScoreTime = time;
        }
    }

    private async Task<Dictionary<int, PenetrationScoreState>> GetPenetrationScoreStates(int gameId,
        CancellationToken token = default)
    {
        var acceptedSolves = await Context.PenetrationSubmissions.AsNoTracking()
            .Where(s => s.GameId == gameId && s.Status == AnswerResult.Accepted)
            .Select(s => new { s.TeamId, s.ScoreItemId, s.Score, s.SubmittedAt })
            .ToArrayAsync(token);

        var firstSolves = acceptedSolves
            .GroupBy(s => new { s.TeamId, s.ScoreItemId })
            .Select(g => g.OrderBy(s => s.SubmittedAt).First());

        Dictionary<int, PenetrationScoreState> states = [];
        foreach (var solve in firstSolves)
        {
            if (!states.TryGetValue(solve.TeamId, out var state))
            {
                state = new PenetrationScoreState();
                states[solve.TeamId] = state;
            }

            state.Add(solve.Score, solve.SubmittedAt);
        }

        return states;
    }

    private static bool CheckDivisionPermission(DivisionItem? division, GamePermission permission,
        int? challengeId = null)
    {
        if (division is null)
            return true;

        return challengeId is { } id && division.ChallengeConfigs.TryGetValue(id, out var config)
            ? config.Permissions.HasFlag(permission)
            : division.DefaultPermissions.HasFlag(permission);
    }
}
