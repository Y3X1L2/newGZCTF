using System.Net;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using GZCTF.Extensions;
using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Concurrency;
using GZCTF.Services.TeamLab;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace GZCTF.Services;

public class PenetrationService(
    AppDbContext context,
    IServiceProvider serviceProvider,
    CacheHelper cacheHelper,
    ISubmissionRepository submissionRepository,
    IGameEventRepository gameEventRepository,
    IHubContext<UserHub, IUserClient> userHub,
    IDistributedLockService lockService,
    TeamLabDeploymentService teamLabDeploymentService,
    ILogger<PenetrationService> logger)
{
    sealed class DeploymentActorScope(Guid? previous) : IDisposable
    {
        public void Dispose() => DeploymentActor.Value = previous;
    }

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    static readonly TimeSpan DeployLockTimeout = TimeSpan.FromSeconds(2);
    static readonly ConcurrentDictionary<int, CancellationTokenSource> DeploymentCancellations = new();
    static readonly TimeSpan[] CleanupBackoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)
    ];
    static readonly TimeSpan SubmitRateWindow = TimeSpan.FromSeconds(60);
    static readonly AsyncLocal<Guid?> DeploymentActor = new();
    const int SubmitRateWindowLimit = 5;

    public async Task<PenetrationConfigModel> GetOrCreateConfig(int gameId, CancellationToken token = default)
    {
        var config = await LoadConfig(gameId, token);
        if (config is not null)
            return ToModel(config);

        var game = await context.Games.FindAsync([gameId], token)
                   ?? throw new InvalidOperationException("Game not found.");

        config = new PenetrationConfig { GameId = game.Id, Game = game };
        config.Networks.Add(new PenetrationNetwork
        {
            TopologyKey = EnsureTopologyKey(null, "network", -1),
            Name = "业务接入网段",
            Slug = "service-lan",
            ZoneType = PenetrationZoneType.Dmz,
            TrustLevel = 30,
            Description = "选手连接队伍 VPN 后可在该内网网段中发现业务资产。",
            IsEntry = false,
            OrderIndex = 0,
            PositionX = 80,
            PositionY = 80,
            Width = 560,
            Height = 390
        });

        context.PenetrationConfigs.Add(config);
        await context.SaveChangesAsync(token);
        return ToModel(config);
    }

    public async Task<PenetrationConfigModel> SaveConfig(int gameId, PenetrationConfigModel model,
        CancellationToken token = default)
    {
        var incomingValidation = await ValidateModel(gameId, model, token);
        if (incomingValidation.Errors.Count > 0)
            throw new InvalidOperationException(string.Join('\n', incomingValidation.Errors));

        await using var transaction = await context.Database.BeginTransactionAsync(token);

        var game = await context.Games.FindAsync([gameId], token)
                   ?? throw new InvalidOperationException("Game not found.");
        var config = await LoadConfig(gameId, token);
        if (config is null)
        {
            config = new PenetrationConfig { GameId = gameId, Game = game };
            context.PenetrationConfigs.Add(config);
            await context.SaveChangesAsync(token);
        }

        var referencedNodeKeys = await context.PenetrationRuntimeNodes.AsNoTracking()
            .Where(r => r.Environment.GameId == gameId && r.TopologyNodeKey != string.Empty)
            .Select(r => r.TopologyNodeKey)
            .ToHashSetAsync(token);
        var submittedScoreItemIds = await context.PenetrationSubmissions.AsNoTracking()
            .Where(s => s.GameId == gameId)
            .Select(s => s.ScoreItemId)
            .ToHashSetAsync(token);

        var maps = ApplyModelToTrackedConfig(config, model, referencedNodeKeys, submittedScoreItemIds);
        await context.SaveChangesAsync(token);
        RemapPrerequisites(config, maps.ScoreKeyByModelId);
        AddEdgesToConfig(config, model, maps);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return ToModel((await LoadConfig(gameId, token))!);
    }

    public async Task<PenetrationValidationModel> Validate(int gameId, CancellationToken token = default)
    {
        var config = await LoadConfig(gameId, token);
        return config is null
            ? new PenetrationValidationModel { Valid = false, Errors = ["渗透编排配置不存在。"] }
            : await ValidateConfig(config, token);
    }

    public async Task<PenetrationValidationModel> ValidateModel(int gameId, PenetrationConfigModel model,
        CancellationToken token = default)
    {
        var config = await BuildTransientConfig(gameId, model, token);
        return await ValidateConfig(config, token);
    }

    public async Task<PenetrationPlanModel> GetPlan(int gameId, CancellationToken token = default)
    {
        var config = await LoadConfig(gameId, token);
        if (config is null)
            return new PenetrationPlanModel
            {
                GameId = gameId,
                Validation = new PenetrationValidationModel { Valid = false, Errors = ["渗透编排配置不存在。"] }
            };

        var teamCount = await context.Participations.AsNoTracking()
            .CountAsync(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted, token);
        return await BuildPlan(config, teamIndex: 0, teamId: 0, teamCount, token);
    }

    public async Task<PenetrationPlanModel> GetPlan(int gameId, PenetrationConfigModel model,
        CancellationToken token = default)
    {
        var config = await BuildTransientConfig(gameId, model, token);
        var teamCount = await context.Participations.AsNoTracking()
            .CountAsync(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted, token);
        return await BuildPlan(config, teamIndex: 0, teamId: 0, teamCount, token);
    }

    public async Task<PenetrationConfigModel> Publish(int gameId, CancellationToken token = default)
    {
        var validation = await Validate(gameId, token);
        if (!validation.Valid)
            throw new InvalidOperationException(string.Join('\n', validation.Errors));

        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var config = (await LoadConfig(gameId, token))!;
        var publishedVersion = config.PublishedVersion + 1;
        config.PublishedVersion = publishedVersion;
        config.Status = PenetrationDeploymentStatus.Published;
        config.PublishedAt = DateTimeOffset.UtcNow;
        config.UpdatedAt = DateTimeOffset.UtcNow;
        var snapshot = CreatePublishedSnapshot(config, publishedVersion);
        await context.PenetrationPublishedSnapshots.AddAsync(snapshot, token);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return ToModel(config);
    }

    public async Task<(bool Success, string Message)> DeployGame(int gameId, bool forceRebuild = false,
        CancellationToken token = default, Guid? userId = null)
    {
        using var actorScope = WithDeploymentActor(userId);
        try
        {
            using var deployLock = await lockService.AcquireAsync(BuildDeployLockKey(gameId), DeployLockTimeout);
            using var deploymentCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (!DeploymentCancellations.TryAdd(gameId, deploymentCts))
                return (false, "该比赛已有渗透部署任务正在执行，请稍后再试。");

            try
            {
                return await DeployGameCore(gameId, forceRebuild, deploymentCts.Token);
            }
            finally
            {
                DeploymentCancellations.TryRemove(gameId, out _);
            }
        }
        catch (TimeoutException)
        {
            return (false, "该比赛已有渗透部署、停止或重建任务正在执行，请稍后再试。");
        }
    }

    public Task<(bool Success, string Message)> CancelDeployment(int gameId, CancellationToken token = default)
    {
        if (DeploymentCancellations.TryGetValue(gameId, out var cts))
        {
            cts.Cancel();
            return Task.FromResult((true, "已请求取消当前渗透部署任务；进行中的队伍会进入清理链路，未开始队伍不会部署。"));
        }

        return Task.FromResult((false, "当前没有正在执行的渗透部署任务。"));
    }

    async Task<(bool Success, string Message)> DeployGameCore(int gameId, bool forceRebuild, CancellationToken token)
    {
        var savedConfig = await LoadConfig(gameId, token);
        if (savedConfig is null || savedConfig.PublishedVersion <= 0)
            return (false, "请先发布渗透编排版本。");

        if (savedConfig.Status == PenetrationDeploymentStatus.Deploying)
            return (false, "该比赛已有渗透部署任务正在执行，请稍后再试。");

        var config = await LoadPublishedConfig(gameId, savedConfig.PublishedVersion, token);
        if (config is null)
            return (false, $"发布版本 v{savedConfig.PublishedVersion} 快照不存在，请重新发布后再部署。");

        var validation = await ValidateConfig(config, token);
        if (!validation.Valid)
            return (false, string.Join('\n', validation.Errors));

        savedConfig.Status = PenetrationDeploymentStatus.Deploying;
        savedConfig.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);

        var participations = await context.Participations.AsNoTracking()
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .OrderBy(p => p.TeamId)
            .ToArrayAsync(token);

        var deploymentTargets = new List<(Participation Participation, int TeamIndex, PenetrationTeamEnvironment? Existing)>();
        var skipped = 0;
        foreach (var (part, index) in participations.Select((p, i) => (p, i)))
        {
            var existing = await LoadTeamEnvironment(gameId, part.TeamId, token);
            if (!forceRebuild && existing is not null && IsEnvironmentRunningVersion(existing, config))
            {
                skipped++;
                continue;
            }

            deploymentTargets.Add((part, index, existing));
        }

        var ok = 0;
        var failed = 0;
        var cancelled = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = token,
            MaxDegreeOfParallelism = ResolveDeploymentParallelism()
        };

        try
        {
            await Parallel.ForEachAsync(deploymentTargets, parallelOptions, async (target, itemToken) =>
            {
                var result = await DeployTeamInIsolatedScope(
                    gameId,
                    savedConfig.PublishedVersion,
                    target.Participation.TeamId,
                    forceRebuild || target.Existing is not null,
                    itemToken);

                if (result.Cancelled)
                    Interlocked.Increment(ref cancelled);
                else if (result.Success)
                    Interlocked.Increment(ref ok);
                else
                    Interlocked.Increment(ref failed);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            cancelled += deploymentTargets.Count - ok - failed - cancelled;
        }

        var running = ok + skipped;
        savedConfig.Status = token.IsCancellationRequested
            ? running > 0 ? PenetrationDeploymentStatus.Partial : PenetrationDeploymentStatus.Failed
            : running == participations.Length
            ? PenetrationDeploymentStatus.Running
            : running == 0 ? PenetrationDeploymentStatus.Failed : PenetrationDeploymentStatus.Partial;
        savedConfig.DeployedAt = DateTimeOffset.UtcNow;
        savedConfig.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(CancellationToken.None);
        var message = token.IsCancellationRequested
            ? $"部署已取消：已运行 {running}/{participations.Length} 支队伍，新部署 {ok} 支，跳过 {skipped} 支，失败 {failed} 支，取消 {cancelled} 支。"
            : $"已运行 {running}/{participations.Length} 支队伍环境，新部署 {ok} 支，跳过 {skipped} 支，失败 {failed} 支。";
        return (running > 0 || participations.Length == 0, message);
    }

    async Task<TeamDeploymentResult> DeployTeamInIsolatedScope(int gameId, int publishedVersion, int teamId,
        bool rebuild, CancellationToken token)
    {
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var scopedService = scope.ServiceProvider.GetRequiredService<PenetrationService>();
            return await scopedService.DeployTeamByPublishedVersion(gameId, publishedVersion, teamId, rebuild, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await CleanupCancelledDeploymentScope(gameId, teamId, publishedVersion);
            return TeamDeploymentResult.CancelledResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Penetration deployment failed for game {GameId}, team {TeamId}. Continuing with other teams.",
                gameId, teamId);
            return TeamDeploymentResult.Failed(ex.Message);
        }
    }

    async Task CleanupCancelledDeploymentScope(int gameId, int teamId, int publishedVersion)
    {
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var deployment = scope.ServiceProvider.GetRequiredService<TeamLabDeploymentService>();
            var cleanup = await deployment.DestroyRuntimeAsync(gameId, teamId, CancellationToken.None);
            if (!cleanup.Success && cleanup.Runtime is not null)
                logger.LogWarning(
                    "Cancelled TeamLab deployment cleanup left residual resources for game {GameId}, team {TeamId}: {Message}",
                    gameId, teamId, cleanup.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to run compensation cleanup for cancelled TeamLab deployment, game {GameId}, team {TeamId}.",
                gameId, teamId);
        }
    }

    async Task<TeamDeploymentResult> DeployTeamByPublishedVersion(int gameId, int publishedVersion, int teamId,
        bool rebuild, CancellationToken token)
    {
        var config = await LoadPublishedConfig(gameId, publishedVersion, token);
        if (config is null)
            return TeamDeploymentResult.Failed($"发布版本 v{publishedVersion} 快照不存在，无法部署队伍环境。");

        try
        {
            var runtime = await context.TeamLabRuntimes.AsNoTracking()
                .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);

            if (!rebuild &&
                runtime is
                {
                    PublishedVersion: var version,
                    Status: TeamLabRuntimeStatus.Running,
                    IsOpenToPlayers: true
                } &&
                version == publishedVersion)
                return TeamDeploymentResult.SuccessResult;

            if (runtime is not null)
            {
                using var destroyCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var destroyed = await teamLabDeploymentService.DestroyRuntimeAsync(gameId, teamId, destroyCts.Token);
                if (!destroyed.Success)
                    return TeamDeploymentResult.Failed(destroyed.Message);
            }

            token.ThrowIfCancellationRequested();
            var deploy = await teamLabDeploymentService.DeployRuntimeAsync(gameId, teamId, token);
            if (deploy.Success)
                await PublishWorkspaceRefresh(gameId, teamId, publishedVersion, token);
            return deploy.Success ? TeamDeploymentResult.SuccessResult : TeamDeploymentResult.Failed(deploy.Message);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await CleanupCancelledDeploymentScope(gameId, teamId, publishedVersion);
            return TeamDeploymentResult.CancelledResult;
        }
    }

    public async Task<(bool Success, string Message)> RebuildTeam(int gameId, int teamId, bool byAdmin, Guid? userId,
        CancellationToken token = default)
    {
        using var actorScope = WithDeploymentActor(userId);
        try
        {
            using var deployLock = await lockService.AcquireAsync(BuildDeployLockKey(gameId), DeployLockTimeout);
            return await RebuildTeamCore(gameId, teamId, byAdmin, userId, token);
        }
        catch (TimeoutException)
        {
            return (false, "该比赛已有渗透部署、停止或重建任务正在执行，请稍后再试。");
        }
    }

    async Task<(bool Success, string Message)> RebuildTeamCore(int gameId, int teamId, bool byAdmin, Guid? userId,
        CancellationToken token)
    {
        var savedConfig = await LoadConfig(gameId, token);
        if (savedConfig is null || savedConfig.PublishedVersion <= 0)
            return (false, "渗透编排尚未发布。");

        var teamIds = await context.Participations.AsNoTracking()
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .OrderBy(p => p.TeamId)
            .Select(p => p.TeamId)
            .ToArrayAsync(token);
        var acceptedIndex = Array.IndexOf(teamIds, teamId);
        if (acceptedIndex < 0)
            return (false, "队伍未通过比赛审核。");

        var environment = await LoadTeamEnvironment(gameId, teamId, token);
        var index = environment is not null && (environment.TeamIndex > 0 || !string.IsNullOrWhiteSpace(environment.NetworkPrefix))
            ? environment.TeamIndex
            : acceptedIndex;
        var targetVersion = environment?.PublishedVersion > 0
            ? environment.PublishedVersion
            : savedConfig.PublishedVersion;
        var config = await LoadPublishedConfig(gameId, targetVersion, token);
        if (config is null)
            return (false, $"发布版本 v{targetVersion} 快照不存在，无法重建环境。");

        if (!byAdmin && environment is not null && environment.ResetCount >= config.MaxResetCount)
            return (false, "环境重置次数已用完。");

        var runtime = await context.TeamLabRuntimes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);
        if (runtime is not null)
        {
            var destroyed = await teamLabDeploymentService.DestroyRuntimeAsync(gameId, teamId, token);
            if (!destroyed.Success)
                return (false, $"旧环境清理失败，已进入待清理状态：{destroyed.Message}");
        }

        var deploy = await teamLabDeploymentService.DeployRuntimeAsync(gameId, teamId, token);
        var syncedEnvironment = await LoadTeamEnvironment(gameId, teamId, token);
        if (deploy.Success && syncedEnvironment is not null && !byAdmin)
        {
            syncedEnvironment.ResetCount = environment?.ResetCount + 1 ?? 1;
            await context.PenetrationResetRecords.AddAsync(new PenetrationResetRecord
            {
                EnvironmentId = syncedEnvironment.Id,
                UserId = userId,
                ByAdmin = false
            }, token);
            await context.SaveChangesAsync(token);
        }

        if (deploy.Success)
            await PublishWorkspaceRefresh(gameId, teamId, targetVersion, token);

        return (deploy.Success, deploy.Success ? "渗透环境已重建。" : deploy.Message);
    }

    public async Task<(bool Success, string Message)> RestartRuntimeNode(int runtimeNodeId, CancellationToken token = default)
    {
        var runtime = await context.PenetrationRuntimeNodes
            .Include(r => r.Environment)
            .FirstOrDefaultAsync(r => r.Id == runtimeNodeId, token);
        if (runtime is null)
            return (false, "运行节点不存在。");

        return await RebuildTeam(runtime.Environment.GameId, runtime.Environment.TeamId, true, null, token);
    }

    public Task<(bool Success, string Message)> RebuildTeamByRuntimeNode(int runtimeNodeId,
        CancellationToken token = default) => RestartRuntimeNode(runtimeNodeId, token);

    public async Task<(bool Success, string Message)> CleanupTeamEnvironment(int gameId, int teamId,
        CancellationToken token = default, Guid? userId = null)
    {
        using var actorScope = WithDeploymentActor(userId);
        try
        {
            using var deployLock = await lockService.AcquireAsync(BuildDeployLockKey(gameId), DeployLockTimeout);
            var runtime = await context.TeamLabRuntimes.AsNoTracking()
                .FirstOrDefaultAsync(r => r.GameId == gameId && r.TeamId == teamId, token);
            if (runtime is null or { Status: TeamLabRuntimeStatus.Destroyed })
                return (true, "该队伍没有渗透环境需要清理。");

            var result = await teamLabDeploymentService.DestroyRuntimeAsync(gameId, teamId, token);
            return result.Success
                ? (true, "队伍渗透环境残留资源已清理。")
                : (false, $"残留资源仍未清理完成：{result.Message}");
        }
        catch (TimeoutException)
        {
            return (false, "该比赛已有渗透部署、停止或重建任务正在执行，请稍后再试。");
        }
    }

    public async Task<(bool Success, string Message)> StopGame(int gameId, CancellationToken token = default,
        Guid? userId = null)
    {
        using var actorScope = WithDeploymentActor(userId);
        try
        {
            using var deployLock = await lockService.AcquireAsync(BuildDeployLockKey(gameId), DeployLockTimeout);
            return await StopGameCore(gameId, token);
        }
        catch (TimeoutException)
        {
            return (false, "该比赛已有渗透部署、停止或重建任务正在执行，请稍后再试。");
        }
    }

    async Task<(bool Success, string Message)> StopGameCore(int gameId, CancellationToken token)
    {
        var runtimes = await context.TeamLabRuntimes.AsNoTracking()
            .Where(r => r.GameId == gameId && r.Status != TeamLabRuntimeStatus.Destroyed)
            .Select(r => r.TeamId)
            .ToArrayAsync(token);

        var failed = 0;
        foreach (var teamId in runtimes)
        {
            var result = await teamLabDeploymentService.DestroyRuntimeAsync(gameId, teamId, token);
            if (!result.Success)
                failed++;
        }

        var config = await context.PenetrationConfigs.FirstOrDefaultAsync(c => c.GameId == gameId, token);
        if (config is not null)
        {
            config.Status = failed == 0 ? PenetrationDeploymentStatus.Stopped : PenetrationDeploymentStatus.Partial;
            config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(token);
        return (failed == 0,
            failed == 0 ? "渗透环境已停止。" : $"部分渗透环境清理失败，{failed} 支队伍已进入待清理状态。");
    }

    public async Task<int> CleanupPendingEnvironments(CancellationToken token = default)
    {
        var runtimes = await context.TeamLabRuntimes.AsNoTracking()
            .Where(r => r.Status == TeamLabRuntimeStatus.CleanupPending)
            .OrderBy(e => e.UpdatedAt)
            .Take(20)
            .Select(r => new { r.GameId, r.TeamId })
            .ToArrayAsync(token);

        var cleaned = 0;
        foreach (var runtime in runtimes)
        {
            try
            {
                using var deployLock = await lockService.AcquireAsync(BuildDeployLockKey(runtime.GameId),
                    TimeSpan.FromSeconds(1));
                var result = await teamLabDeploymentService.DestroyRuntimeAsync(runtime.GameId, runtime.TeamId, token);
                if (result.Success)
                    cleaned++;
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex,
                    "Concurrent TeamLab cleanup update for game {GameId}, team {TeamId}.",
                    runtime.GameId, runtime.TeamId);
                context.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to run TeamLab cleanup for game {GameId}, team {TeamId}.",
                    runtime.GameId, runtime.TeamId);
                context.ChangeTracker.Clear();
            }
        }

        return cleaned;
    }

    public async Task<PenetrationWorkspaceModel?> GetWorkspace(int gameId, int teamId, CancellationToken token = default)
    {
        var environment = await LoadTeamEnvironment(gameId, teamId, token);
        if (environment is null)
            return null;

        var config = await LoadPublishedConfig(gameId, environment.PublishedVersion, token);
        if (config is null)
            return null;

        var solved = await context.PenetrationSubmissions.AsNoTracking()
            .Where(s => s.GameId == gameId &&
                        s.TeamId == teamId &&
                        s.PublishedVersion == environment.PublishedVersion &&
                        s.Status == AnswerResult.Accepted)
            .Select(s => s.ScoreItemTopologyKey)
            .ToHashSetAsync(token);

        var attempts = await context.PenetrationSubmissions.AsNoTracking()
            .Where(s => s.GameId == gameId &&
                        s.TeamId == teamId &&
                        s.PublishedVersion == environment.PublishedVersion)
            .GroupBy(s => s.ScoreItemTopologyKey)
            .Select(g => new { ScoreItemTopologyKey = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ScoreItemTopologyKey, g => g.Count, token);

        var runtimeByNode = environment.RuntimeNodes
            .Where(r => !string.IsNullOrWhiteSpace(r.TopologyNodeKey))
            .ToDictionary(r => r.TopologyNodeKey, StringComparer.Ordinal);
        return new PenetrationWorkspaceModel
        {
            GameId = gameId,
            TeamId = teamId,
            TeamName = environment.Team.Name,
            Status = environment.Status,
            ResetCount = environment.ResetCount,
            MaxResetCount = config.MaxResetCount,
            Nodes = config.Nodes
                .OrderBy(n => n.OrderIndex)
                .Select(n =>
            {
                runtimeByNode.TryGetValue(n.TopologyKey, out var runtime);
                return new PenetrationWorkspaceNodeModel
                {
                    Id = n.Id,
                    NetworkId = n.NetworkId,
                    TopologyKey = n.TopologyKey,
                    Name = string.IsNullOrWhiteSpace(n.PlayerAlias) ? n.Name : n.PlayerAlias!,
                    Description = string.IsNullOrWhiteSpace(n.PlayerDescription) ? n.Description : n.PlayerDescription,
                    NodeType = NormalizeTeamLabNodeType(n.NodeType),
                    RuntimeStatus = runtime?.Status ?? PenetrationRuntimeStatus.Pending,
                    ScoreItems = n.ScoreItems.Where(i => i.IsVisible).OrderBy(i => i.OrderIndex).Select(i =>
                        new PenetrationWorkspaceScoreItemModel
                        {
                            Id = i.Id,
                            TopologyKey = i.TopologyKey,
                            Title = i.Title,
                            Description = i.Description,
                            Category = i.Category,
                            Score = i.Score,
                            Solved = solved.Contains(i.TopologyKey),
                            Attempts = attempts.GetValueOrDefault(i.TopologyKey),
                            MaxAttempts = i.MaxAttempts,
                            IsCheckpoint = i.IsCheckpoint,
                            PrerequisiteItemIds = DeserializeIntList(i.PrerequisiteItemIds),
                            PrerequisiteItemKeys = ResolvePrerequisiteKeys(config, i)
                        }).ToList()
                };
            }).ToList()
        };
    }

    public async Task<PenetrationSubmitResultModel> Submit(int gameId, int teamId, int participationId, Guid userId,
        PenetrationSubmitModel model, CancellationToken token = default)
    {
        var environment = await LoadTeamEnvironment(gameId, teamId, token);
        if (environment is null || environment.Status != PenetrationRuntimeStatus.Running)
            return new PenetrationSubmitResultModel { Accepted = false, Message = "本队渗透环境尚未运行。" };

        var config = await LoadPublishedConfig(gameId, environment.PublishedVersion, token);
        if (config is null)
            return new PenetrationSubmitResultModel { Accepted = false, Message = "当前运行版本快照不存在，请联系管理员重建环境。" };

        var item = config.Nodes.SelectMany(n => n.ScoreItems)
            .FirstOrDefault(i => i.Id == model.ScoreItemId);
        if (item is null)
            return new PenetrationSubmitResultModel { Accepted = false, Message = "得分项不存在。" };

        if (!item.IsVisible)
            return new PenetrationSubmitResultModel { Accepted = false, Message = "该得分项当前不可提交。" };


        PenetrationSubmission submission;
        bool accepted;
        using (await lockService.AcquireAsync(BuildSubmitLockKey(gameId, teamId, environment.PublishedVersion,
                   item.TopologyKey), TimeSpan.FromSeconds(5)))
        {
            var alreadySolved = await context.PenetrationSubmissions.AnyAsync(s =>
                s.GameId == gameId &&
                s.TeamId == teamId &&
                s.PublishedVersion == environment.PublishedVersion &&
                s.ScoreItemTopologyKey == item.TopologyKey &&
                s.Status == AnswerResult.Accepted,
                token);
            if (alreadySolved)
                return new PenetrationSubmitResultModel { Accepted = false, Message = "该得分项已完成。" };

            var prerequisiteKeys = ResolvePrerequisiteKeys(config, item);
            if (prerequisiteKeys.Count > 0)
            {
                var solvedPrerequisites = await context.PenetrationSubmissions.AsNoTracking()
                    .Where(s => s.GameId == gameId &&
                                s.TeamId == teamId &&
                                s.PublishedVersion == environment.PublishedVersion &&
                                s.Status == AnswerResult.Accepted &&
                                prerequisiteKeys.Contains(s.ScoreItemTopologyKey))
                    .Select(s => s.ScoreItemTopologyKey)
                    .Distinct()
                    .CountAsync(token);

                if (solvedPrerequisites < prerequisiteKeys.Count)
                    return new PenetrationSubmitResultModel { Accepted = false, Message = "请先完成前置得分项。" };
            }

            var attemptCount = await context.PenetrationSubmissions.CountAsync(s =>
                s.GameId == gameId &&
                s.TeamId == teamId &&
                s.PublishedVersion == environment.PublishedVersion &&
                s.ScoreItemTopologyKey == item.TopologyKey,
                token);
            if (item.MaxAttempts > 0 && attemptCount >= item.MaxAttempts)
                return new PenetrationSubmitResultModel { Accepted = false, Message = "提交次数已达到上限。" };

            var recentWindowStart = DateTimeOffset.UtcNow - SubmitRateWindow;
            var recentAttempts = await context.PenetrationSubmissions.CountAsync(s =>
                s.GameId == gameId &&
                s.TeamId == teamId &&
                s.PublishedVersion == environment.PublishedVersion &&
                s.ScoreItemTopologyKey == item.TopologyKey &&
                s.SubmittedAt >= recentWindowStart,
                token);
            if (recentAttempts >= SubmitRateWindowLimit)
                return new PenetrationSubmitResultModel { Accepted = false, Message = "提交过于频繁，请稍后再试。" };

            var expected = BuildFlag(item, gameId, teamId, environment.PublishedVersion);
            accepted = string.Equals(model.Flag.Trim(), expected, StringComparison.Ordinal);
            var currentScoreItemId = await context.PenetrationScoreItems.AsNoTracking()
                .Where(i => i.Node.Config.GameId == gameId && i.TopologyKey == item.TopologyKey)
                .Select(i => (int?)i.Id)
                .FirstOrDefaultAsync(token);
            submission = new PenetrationSubmission
            {
                GameId = gameId,
                TeamId = teamId,
                ParticipationId = participationId,
                UserId = userId,
                ScoreItemId = currentScoreItemId ?? item.Id,
                PublishedVersion = environment.PublishedVersion,
                ScoreItemTopologyKey = item.TopologyKey,
                Answer = model.Flag.Trim(),
                Status = accepted ? AnswerResult.Accepted : AnswerResult.WrongAnswer,
                Score = accepted ? item.Score : 0,
                SubmittedAt = DateTimeOffset.UtcNow
            };

            await context.PenetrationSubmissions.AddAsync(submission, token);
            await context.SaveChangesAsync(token);
        }

        if (accepted)
            await cacheHelper.FlushScoreboardCache(gameId, token);

        await PublishSubmissionSideEffects(submission, item, userId, token);

        if (accepted)
            await PublishWorkspaceRefresh(gameId, teamId, environment.PublishedVersion, token);


        return new PenetrationSubmitResultModel
        {
            Accepted = accepted,
            Score = submission.Score,
            Message = accepted ? "Flag 正确。" : "Flag 错误。",
        };
    }

    public async Task<PenetrationScoreboardItemModel[]> GetScoreboard(int gameId, CancellationToken token = default)
    {
        var teams = await context.Participations.AsNoTracking()
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .Include(p => p.Team)
            .Select(p => new { p.TeamId, TeamName = p.Team.Name })
            .ToArrayAsync(token);

        var scores = await GetScoreStates(gameId, token);
        var rows = teams.Select(t =>
        {
            var score = scores.GetValueOrDefault(t.TeamId);
            return new PenetrationScoreboardItemModel
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName,
                Score = score?.TotalScore ?? 0,
                SolvedCount = score?.SolvedCount ?? 0,
                LastSubmissionTime = score?.LastScoreTime ?? DateTimeOffset.MinValue
            };
        }).OrderByDescending(i => i.Score)
            .ThenBy(i => i.LastSubmissionTime == DateTimeOffset.MinValue ? DateTimeOffset.MaxValue : i.LastSubmissionTime)
            .ThenBy(i => i.TeamName)
            .ToArray();

        for (var i = 0; i < rows.Length; i++)
            rows[i].Rank = i + 1;

        return rows;
    }

    public async Task<PenetrationTeamEnvironmentModel[]> GetTeamEnvironments(int gameId,
        CancellationToken token = default)
    {
        var rows = await context.PenetrationTeamEnvironments.AsNoTracking()
            .Where(e => e.GameId == gameId)
            .Include(e => e.Team)
            .Include(e => e.Node)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.TopologyNode)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.Container)
            .Include(e => e.RuntimeRoutes)
            .Include(e => e.DeploymentEvents)
            .OrderBy(e => e.Team.Name)
            .ToArrayAsync(token);

        return rows.Select(e => new PenetrationTeamEnvironmentModel
        {
            EnvironmentId = e.Id,
            TeamId = e.TeamId,
            TeamName = e.Team.Name,
            WorkerNodeId = e.NodeId,
            WorkerNodeName = e.Node == null ? null : e.Node.Name,
            NetworkPrefix = e.NetworkPrefix,
            TeamIndex = e.TeamIndex,
            PublishedVersion = e.PublishedVersion,
            Status = e.Status,
            ResetCount = e.ResetCount,
            RuntimeNodeCount = e.RuntimeNodes.Count,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            LastError = e.LastError,
            CleanupRetryCount = e.CleanupRetryCount,
            NextCleanupAt = e.NextCleanupAt,
            LastCleanupAttemptAt = e.LastCleanupAttemptAt,
            RuntimeNodes = e.RuntimeNodes
                .OrderBy(r => r.TopologyNode.OrderIndex)
                .Select(r => new PenetrationRuntimeNodeModel
                {
                    RuntimeNodeId = r.Id,
                    TopologyNodeId = r.TopologyNodeId,
                    TopologyNodeKey = r.TopologyNodeKey,
                    NodeName = r.TopologyNode.Name,
                    NetworkName = r.NetworkName,
                    IpAddress = r.IpAddress,
                    AdminAccessUrl = null,
                    PublicPort = null,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt,
                    ContainerGuid = r.ContainerId,
                    ContainerId = r.Container == null ? null : r.Container.ContainerId,
                    ContainerStatus = r.Container?.Status,
                    Image = r.Container?.Image,
                    PublicHost = null,
                    InterfaceSummary = r.InterfaceSummary
                }).ToList(),
            RuntimeRoutes = e.RuntimeRoutes
                .OrderBy(r => r.Id)
                .Select(r => new PenetrationRuntimeRouteModel
                {
                    Id = r.Id,
                    EdgeTopologyKey = r.EdgeTopologyKey,
                    Label = r.Label,
                    EnforcementMode = r.EnforcementMode,
                    Status = r.Status,
                    RouteNodeKey = r.RouteNodeKey,
                    RouteNodeName = r.RouteNodeName,
                    SourceNetworkName = r.SourceNetworkName,
                    TargetNetworkName = r.TargetNetworkName,
                    SourceCidr = r.SourceCidr,
                    TargetCidr = r.TargetCidr,
                    GatewayIp = r.GatewayIp,
                    CommandSummary = r.CommandSummary,
                    Message = r.Message,
                    IsExecutable = r.Status is PenetrationRouteStatus.RoutePlanned or PenetrationRouteStatus.RouteApplied,
                    CreatedAt = r.CreatedAt,
                    AppliedAt = r.AppliedAt
                }).ToList(),
            Events = e.DeploymentEvents
                .OrderByDescending(ev => ev.CreatedAt)
                .Take(20)
                .Select(ev => new PenetrationDeploymentEventModel
                {
                    Id = ev.Id,
                    EnvironmentId = e.Id,
                    TeamId = e.TeamId,
                    TeamName = e.Team.Name,
                    Stage = ev.Stage,
                    Level = ev.Level,
                    Message = ev.Message,
                    NodeName = ev.NodeName,
                    Detail = ev.Detail,
                    UserId = ev.UserId,
                    CreatedAt = ev.CreatedAt
                }).ToList()
        }).ToArray();
    }

    public async Task<ArrayResponse<PenetrationDeploymentEventModel>> GetDeploymentEvents(int gameId,
        int count = 50, int skip = 0, int? environmentId = null, CancellationToken token = default)
    {
        count = count <= 0 ? 50 : Math.Min(count, 200);
        skip = Math.Max(0, skip);

        var query = context.PenetrationDeploymentEvents.AsNoTracking()
            .Where(ev => ev.Environment.GameId == gameId);

        if (environmentId is > 0)
            query = query.Where(ev => ev.EnvironmentId == environmentId);

        var total = await query.CountAsync(token);
        var events = await query
            .OrderByDescending(ev => ev.CreatedAt)
            .ThenByDescending(ev => ev.Id)
            .Skip(skip)
            .Take(count)
            .Select(ev => new PenetrationDeploymentEventModel
            {
                Id = ev.Id,
                EnvironmentId = ev.EnvironmentId,
                TeamId = ev.Environment.TeamId,
                TeamName = ev.Environment.Team.Name,
                Stage = ev.Stage,
                Level = ev.Level,
                Message = ev.Message,
                NodeName = ev.NodeName,
                Detail = ev.Detail,
                UserId = ev.UserId,
                CreatedAt = ev.CreatedAt
            })
            .ToArrayAsync(token);

        return events.ToResponse(total);
    }

    public async Task<PenetrationAdminAccessModel[]> GetAdminAccess(int gameId, int teamId,
        CancellationToken token = default)
    {
        var query = context.PenetrationRuntimeNodes.AsNoTracking()
            .Where(r => r.Environment.GameId == gameId)
            .Include(r => r.Environment).ThenInclude(e => e.Team)
            .Include(r => r.Environment).ThenInclude(e => e.Node)
            .Include(r => r.TopologyNode)
            .Include(r => r.Container)
            .AsQueryable();

        if (teamId > 0)
            query = query.Where(r => r.Environment.TeamId == teamId);

        var rows = await query.OrderBy(r => r.Environment.Team.Name)
            .ThenBy(r => r.TopologyNode.OrderIndex)
            .ToArrayAsync(token);
        var snapshotByVersion = new Dictionary<int, PenetrationConfig>();

        var result = new List<PenetrationAdminAccessModel>(rows.Length);
        foreach (var r in rows)
        {
            if (!snapshotByVersion.TryGetValue(r.Environment.PublishedVersion, out var snapshot))
            {
                snapshot = await LoadPublishedConfig(gameId, r.Environment.PublishedVersion, token)
                           ?? new PenetrationConfig();
                snapshotByVersion[r.Environment.PublishedVersion] = snapshot;
            }

            var node = snapshot.Nodes.FirstOrDefault(n => n.TopologyKey == r.TopologyNodeKey);
            var host = r.Container?.IP;

            result.Add(new PenetrationAdminAccessModel
            {
                RuntimeNodeId = r.Id,
                TeamId = r.Environment.TeamId,
                TeamName = r.Environment.Team.Name,
                NodeId = node?.Id ?? r.TopologyNodeId,
                NodeName = node?.Name ?? r.TopologyNode.Name,
                Status = r.Status,
                WorkerNodeName = r.Environment.Node?.Name ?? "本地节点",
                ContainerId = r.Container?.ContainerId ?? string.Empty,
                InternalIp = r.IpAddress,
                InterfaceSummary = r.InterfaceSummary,
                Host = host,
                PublicPort = null,
                Url = null,
                ExposePort = node?.ExposePort ?? r.TopologyNode.ExposePort
            });
        }

        return result.ToArray();
    }

    public async Task<Dictionary<int, PenetrationScoreState>> GetScoreStates(int gameId,
        CancellationToken token = default)
    {
        var accepted = await context.PenetrationSubmissions.AsNoTracking()
            .Where(s => s.GameId == gameId && s.Status == AnswerResult.Accepted)
            .Select(s => new
            {
                s.TeamId,
                s.PublishedVersion,
                s.ScoreItemTopologyKey,
                s.ScoreItemId,
                s.Score,
                s.SubmittedAt
            })
            .ToArrayAsync(token);

        var solves = accepted
            .GroupBy(s => new
            {
                s.TeamId,
                ScoreIdentity = s.PublishedVersion > 0 && !string.IsNullOrWhiteSpace(s.ScoreItemTopologyKey)
                    ? $"{s.PublishedVersion}:{s.ScoreItemTopologyKey}"
                    : $"legacy:{s.ScoreItemId}"
            })
            .Select(g => g.OrderBy(s => s.SubmittedAt).First())
            .ToArray();

        Dictionary<int, PenetrationScoreState> states = [];
        foreach (var solve in solves)
        {
            if (!states.TryGetValue(solve.TeamId, out var state))
            {
                state = new PenetrationScoreState(solve.TeamId);
                states[solve.TeamId] = state;
            }

            state.Add(solve.Score, solve.SubmittedAt);
        }

        return states;
    }

    public async Task<ArrayResponse<PenetrationSubmissionLogModel>> GetSubmissionLogs(int gameId, int count, int skip,
        CancellationToken token = default)
    {
        var query = context.PenetrationSubmissions.AsNoTracking()
            .Where(s => s.GameId == gameId);
        var total = await query.CountAsync(token);
        var rows = await query
            .OrderByDescending(s => s.SubmittedAt)
            .Skip(Math.Max(0, skip))
            .Take(count <= 0 ? 50 : Math.Min(count, 100))
            .Select(s => new
            {
                s.Id,
                s.SubmittedAt,
                s.TeamId,
                TeamName = s.Team.Name,
                UserName = s.User.UserName ?? string.Empty,
                CurrentNodeName = s.ScoreItem.Node.Name,
                CurrentItemTitle = s.ScoreItem.Title,
                CurrentCategory = s.ScoreItem.Category,
                s.PublishedVersion,
                s.ScoreItemTopologyKey,
                s.Score,
                s.Status
            }).ToArrayAsync(token);

        var snapshotByVersion = new Dictionary<int, PenetrationConfig>();
        var items = new List<PenetrationSubmissionLogModel>(rows.Length);
        foreach (var s in rows)
        {
            PenetrationScoreItem? item = null;
            if (s.PublishedVersion > 0 && !string.IsNullOrWhiteSpace(s.ScoreItemTopologyKey))
            {
                if (!snapshotByVersion.TryGetValue(s.PublishedVersion, out var snapshot))
                {
                    snapshot = await LoadPublishedConfig(gameId, s.PublishedVersion, token)
                               ?? new PenetrationConfig();
                    snapshotByVersion[s.PublishedVersion] = snapshot;
                }

                item = snapshot.Nodes.SelectMany(n => n.ScoreItems)
                    .FirstOrDefault(i => i.TopologyKey == s.ScoreItemTopologyKey);
            }

            items.Add(new PenetrationSubmissionLogModel
            {
                Id = s.Id,
                Time = s.SubmittedAt,
                TeamId = s.TeamId,
                TeamName = s.TeamName,
                UserName = s.UserName,
                NodeName = item?.Node.Name ?? s.CurrentNodeName,
                ItemTitle = item?.Title ?? s.CurrentItemTitle,
                Category = item?.Category ?? s.CurrentCategory,
                Score = s.Score,
                Status = s.Status
            });
        }

        return items.ToArray().ToResponse(total);
    }

    async Task PublishSubmissionSideEffects(PenetrationSubmission submission, PenetrationScoreItem item, Guid userId,
        CancellationToken token)
    {
        var loadedSubmission = await context.PenetrationSubmissions.AsNoTracking()
            .Where(s => s.Id == submission.Id)
            .Select(s => new
            {
                s.Answer,
                s.Status,
                s.SubmittedAt,
                s.Score,
                s.GameId,
                Game = s.Game,
                s.TeamId,
                Team = s.Team,
                s.ParticipationId,
                Participation = s.Participation,
                s.UserId,
                User = s.User
            })
            .FirstOrDefaultAsync(token);

        if (loadedSubmission is null)
            return;

        var displayName = $"[渗透] {item.Node.Name} / {item.Title}";
        var compatibleSubmission = new Submission
        {
            Answer = loadedSubmission.Answer,
            Status = loadedSubmission.Status,
            SubmitTimeUtc = loadedSubmission.SubmittedAt,
            SubmissionType = ScoringSubmissionType.Flag,
            Content = JsonSerializer.Serialize(new
            {
                mode = "Penetration",
                nodeId = item.NodeId,
                nodeTopologyKey = item.Node.TopologyKey,
                nodeName = item.Node.Name,
                scoreItemId = item.Id,
                scoreItemTopologyKey = item.TopologyKey,
                itemTitle = item.Title,
                publishedVersion = submission.PublishedVersion,
                item.Category
            }, JsonOptions),
            AttemptNumber = 1,
            Score = loadedSubmission.Score,
            UserId = loadedSubmission.UserId,
            User = loadedSubmission.User,
            TeamId = loadedSubmission.TeamId,
            Team = loadedSubmission.Team,
            ParticipationId = loadedSubmission.ParticipationId,
            Participation = loadedSubmission.Participation,
            GameId = loadedSubmission.GameId,
            Game = loadedSubmission.Game,
            ChallengeId = 0,
            DisplayChallengeName = displayName
        };

        try
        {
            await gameEventRepository.AddEvent(new GameEvent
            {
                TeamId = submission.TeamId,
                UserId = userId,
                GameId = submission.GameId,
                Type = EventType.FlagSubmit,
                Values =
                [
                    submission.Status.ToString(),
                    submission.Answer,
                    displayName,
                    item.Id.ToString()
                ]
            }, token);

            await submissionRepository.SendSubmission(compatibleSubmission);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish penetration submission side effects.");
        }
    }

    async Task<HashSet<string>> GetSolvedScoreItemKeys(int gameId, int teamId, int publishedVersion,
        CancellationToken token) =>
        await context.PenetrationSubmissions.AsNoTracking()
            .Where(s => s.GameId == gameId &&
                        s.TeamId == teamId &&
                        s.PublishedVersion == publishedVersion &&
                        s.Status == AnswerResult.Accepted)
            .Select(s => s.ScoreItemTopologyKey)
            .ToHashSetAsync(token);

    static List<string> ResolvePrerequisiteKeys(PenetrationConfig config, PenetrationScoreItem item)
    {
        var prerequisites = DeserializeIntList(item.PrerequisiteItemIds);
        if (prerequisites.Count == 0)
            return [];

        var keyById = config.Nodes
            .SelectMany(n => n.ScoreItems)
            .Where(i => i.Id > 0 && !string.IsNullOrWhiteSpace(i.TopologyKey))
            .GroupBy(i => i.Id)
            .ToDictionary(g => g.Key, g => g.First().TopologyKey);

        return prerequisites
            .Select(id => keyById.GetValueOrDefault(id))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToList()!;
    }

    async Task PublishWorkspaceRefresh(int gameId, int teamId, int publishedVersion, CancellationToken token)
    {
        try
        {
            await userHub.Clients.Group(UserHub.PenetrationTeamGroupName(gameId, teamId))
                .ReceivedPenetrationWorkspaceUpdate(new PenetrationWorkspaceUpdateModel
                {
                    GameId = gameId,
                    TeamId = teamId,
                    PublishedVersion = publishedVersion,
                    Time = DateTimeOffset.UtcNow
                });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to push penetration workspace refresh for game {GameId}, team {TeamId}.",
                gameId, teamId);
        }
    }

    async Task<PenetrationConfig> BuildTransientConfig(int gameId, PenetrationConfigModel model,
        CancellationToken token)
    {
        var game = await context.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, token)
                   ?? throw new InvalidOperationException("Game not found.");
        var saved = await LoadConfig(gameId, token);
        var config = new PenetrationConfig
        {
            Id = saved?.Id ?? -gameId,
            GameId = gameId,
            Game = game,
            PublishedVersion = model.PublishedVersion > 0 ? model.PublishedVersion : saved?.PublishedVersion ?? 0,
            Status = saved?.Status ?? PenetrationDeploymentStatus.Draft,
            PublishedAt = saved?.PublishedAt,
            DeployedAt = saved?.DeployedAt
        };
        ApplyModelToConfig(config, model, preserveModelIds: true, includeEdges: true);
        return config;
    }

    static TopologyModelMaps ApplyModelToConfig(PenetrationConfig config, PenetrationConfigModel model,
        bool preserveModelIds, bool includeEdges)
    {
        config.BaseCidr = string.IsNullOrWhiteSpace(model.BaseCidr) ? "10.60.0.0/16" : model.BaseCidr.Trim();
        config.TeamSubnetPrefix = model.TeamSubnetPrefix is >= 16 and <= 28 ? model.TeamSubnetPrefix : 24;
        config.NetworkSubnetPrefix = model.NetworkSubnetPrefix is >= 24 and <= 30 ? model.NetworkSubnetPrefix : 28;
        config.MaxResetCount = Math.Clamp(model.MaxResetCount, 0, 100);
        config.Status = config.PublishedVersion > 0 ? PenetrationDeploymentStatus.Published : PenetrationDeploymentStatus.Draft;
        config.UpdatedAt = DateTimeOffset.UtcNow;

        config.Edges.Clear();
        foreach (var node in config.Nodes)
        {
            node.Interfaces.Clear();
            node.ScoreItems.Clear();
        }
        config.Nodes.Clear();
        config.Networks.Clear();

        if (model.Networks.Count == 0)
            model.Networks.Add(DefaultNetworkModel(-1, 0));

        var networkMap = new Dictionary<int, PenetrationNetwork>();
        var networkKeyMap = new Dictionary<string, PenetrationNetwork>(StringComparer.Ordinal);
        foreach (var networkModel in model.Networks.OrderBy(n => n.OrderIndex))
        {
            var network = new PenetrationNetwork
            {
                Id = preserveModelIds ? networkModel.Id : 0,
                ConfigId = config.Id,
                Config = config,
                TopologyKey = EnsureTopologyKey(networkModel.TopologyKey, "network", networkModel.Id),
                Name = Clean(networkModel.Name, "未命名网段"),
                Slug = Slugify(string.IsNullOrWhiteSpace(networkModel.Slug) ? networkModel.Name : networkModel.Slug),
                Cidr = CleanNullable(networkModel.Cidr),
                ZoneType = NormalizeTeamLabZoneType(networkModel.ZoneType),
                TrustLevel = Math.Clamp(networkModel.TrustLevel, 0, 100),
                Description = CleanNullable(networkModel.Description),
                DefaultPolicy = networkModel.DefaultPolicy,
                IsEntry = false,
                OrderIndex = networkModel.OrderIndex,
                PositionX = networkModel.PositionX,
                PositionY = networkModel.PositionY,
                Width = Math.Clamp(networkModel.Width <= 0 ? 560 : networkModel.Width, 360, 1800),
                Height = Math.Clamp(networkModel.Height <= 0 ? 390 : networkModel.Height, 260, 1200),
                Collapsed = networkModel.Collapsed
            };
            config.Networks.Add(network);
            networkMap[networkModel.Id] = network;
            networkKeyMap[network.TopologyKey] = network;
        }

        var modelInterfaces = model.Interfaces.Count > 0
            ? model.Interfaces
            : model.Nodes.SelectMany(n => n.Interfaces).ToList();
        var nodeMap = new Dictionary<int, PenetrationNode>();
        var nodeKeyMap = new Dictionary<string, PenetrationNode>(StringComparer.Ordinal);
        var scoreKeyByModelId = new Dictionary<int, string>();

        foreach (var nodeModel in model.Nodes.OrderBy(n => n.OrderIndex))
        {
            var primaryNetworkId = ResolvePrimaryNetworkId(nodeModel, modelInterfaces, networkMap);
            var network = networkMap.GetValueOrDefault(primaryNetworkId) ?? config.Networks.OrderBy(n => n.OrderIndex).First();

            var node = new PenetrationNode
            {
                Id = preserveModelIds ? nodeModel.Id : 0,
                ConfigId = config.Id,
                Config = config,
                NetworkId = network.Id,
                Network = network,
                TopologyKey = EnsureTopologyKey(nodeModel.TopologyKey, "node", nodeModel.Id),
                Name = Clean(nodeModel.Name, "未命名节点"),
                Description = CleanNullable(nodeModel.Description),
                PlayerAlias = CleanNullable(nodeModel.PlayerAlias),
                PlayerDescription = CleanNullable(nodeModel.PlayerDescription),
                NodeType = NormalizeTeamLabNodeType(nodeModel.NodeType),
                ImageTemplateId = nodeModel.ImageTemplateId,
                ImageName = CleanNullable(nodeModel.ImageName),
                CpuCount = Math.Clamp(nodeModel.CpuCount, 1, 128),
                MemoryLimit = Math.Clamp(nodeModel.MemoryLimit, 64, 262144),
                StorageLimit = Math.Clamp(nodeModel.StorageLimit, 64, 1048576),
                ExposePort = Math.Clamp(nodeModel.ExposePort, 1, 65535),
                IsEntry = false,
                PublishPort = false,
                AllowRouting = nodeModel.AllowRouting,
                StaticIp = CleanNullable(nodeModel.StaticIp),
                EnvironmentVariables = JsonSerializer.Serialize(nodeModel.EnvironmentVariables ?? [], JsonOptions),
                StartCommand = CleanNullable(nodeModel.StartCommand),
                HealthCheck = CleanNullable(nodeModel.HealthCheck),
                ReservedAdRole = CleanNullable(nodeModel.ReservedAdRole),
                PositionX = nodeModel.PositionX,
                PositionY = nodeModel.PositionY,
                OrderIndex = nodeModel.OrderIndex
            };

            foreach (var itemModel in nodeModel.ScoreItems.OrderBy(i => i.OrderIndex))
            {
                var scoreItem = new PenetrationScoreItem
                {
                    Id = preserveModelIds ? itemModel.Id : 0,
                    Node = node,
                    NodeId = node.Id,
                    TopologyKey = EnsureTopologyKey(itemModel.TopologyKey, "score", itemModel.Id),
                    Title = Clean(itemModel.Title, "未命名得分项"),
                    Description = CleanNullable(itemModel.Description),
                    Category = Clean(itemModel.Category, "General"),
                    Score = Math.Max(0, itemModel.Score),
                    IsDynamic = itemModel.IsDynamic,
                    StaticFlag = CleanNullable(itemModel.StaticFlag),
                    FlagTemplate = CleanNullable(itemModel.FlagTemplate),
                    MaxAttempts = Math.Clamp(itemModel.MaxAttempts, 0, 1000),
                    IsVisible = itemModel.IsVisible,
                    IsCheckpoint = itemModel.IsCheckpoint,
                    PrerequisiteItemIds = JsonSerializer.Serialize(itemModel.PrerequisiteItemIds ?? [], JsonOptions),
                    OrderIndex = itemModel.OrderIndex
                };
                node.ScoreItems.Add(scoreItem);
                scoreKeyByModelId[itemModel.Id] = scoreItem.TopologyKey;
            }

            config.Nodes.Add(node);
            network.Nodes.Add(node);
            nodeMap[nodeModel.Id] = node;
            nodeKeyMap[node.TopologyKey] = node;
        }

        foreach (var nodeModel in model.Nodes.OrderBy(n => n.OrderIndex))
        {
            if (!nodeMap.TryGetValue(nodeModel.Id, out var node))
                continue;

            var interfaces = BuildModelInterfaces(nodeModel, modelInterfaces, networkMap.Keys.ToHashSet());
            foreach (var interfaceModel in interfaces.OrderBy(i => i.OrderIndex))
            {
                var targetNetworkModelId = networkMap.ContainsKey(interfaceModel.NetworkId)
                    ? interfaceModel.NetworkId
                    : nodeModel.NetworkId;
                if (!networkMap.TryGetValue(targetNetworkModelId, out var network))
                    network = config.Networks.OrderBy(n => n.OrderIndex).First();

                var iface = new PenetrationInterface
                {
                    Id = preserveModelIds ? interfaceModel.Id : 0,
                    Node = node,
                    NodeId = node.Id,
                    Network = network,
                    NetworkId = network.Id,
                    TopologyKey = EnsureTopologyKey(interfaceModel.TopologyKey, "interface", interfaceModel.Id),
                    Name = Clean(interfaceModel.Name, $"eth{interfaceModel.OrderIndex}"),
                    StaticIp = CleanNullable(interfaceModel.StaticIp),
                    IsPrimary = interfaceModel.IsPrimary,
                    IsManagement = interfaceModel.IsManagement,
                    OrderIndex = interfaceModel.OrderIndex
                };
                node.Interfaces.Add(iface);
                network.Interfaces.Add(iface);
            }
        }

        var maps = new TopologyModelMaps(networkMap, nodeMap, scoreKeyByModelId, preserveModelIds);
        if (includeEdges)
        {
            RemapPrerequisites(config, scoreKeyByModelId);
            AddEdgesToConfig(config, model, maps);
        }

        return maps;
    }

    static TopologyModelMaps ApplyModelToTrackedConfig(PenetrationConfig config, PenetrationConfigModel model,
        HashSet<string> referencedNodeKeys, HashSet<int> submittedScoreItemIds)
    {
        if (model.Networks.Count == 0)
            model.Networks.Add(DefaultNetworkModel(-1, 0));

        config.BaseCidr = string.IsNullOrWhiteSpace(model.BaseCidr) ? "10.60.0.0/16" : model.BaseCidr.Trim();
        config.TeamSubnetPrefix = model.TeamSubnetPrefix is >= 16 and <= 28 ? model.TeamSubnetPrefix : 24;
        config.NetworkSubnetPrefix = model.NetworkSubnetPrefix is >= 24 and <= 30 ? model.NetworkSubnetPrefix : 28;
        config.MaxResetCount = Math.Clamp(model.MaxResetCount, 0, 100);
        config.Status = config.PublishedVersion > 0 ? PenetrationDeploymentStatus.Published : PenetrationDeploymentStatus.Draft;
        config.UpdatedAt = DateTimeOffset.UtcNow;

        var incomingNetworkKeys = model.Networks
            .Select(n => EnsureTopologyKey(n.TopologyKey, "network", n.Id))
            .ToHashSet(StringComparer.Ordinal);
        var incomingNodeKeys = model.Nodes
            .Select(n => EnsureTopologyKey(n.TopologyKey, "node", n.Id))
            .ToHashSet(StringComparer.Ordinal);

        var blockedDeletedNode = config.Nodes.FirstOrDefault(n =>
            referencedNodeKeys.Contains(n.TopologyKey) && !incomingNodeKeys.Contains(n.TopologyKey));
        if (blockedDeletedNode is not null)
            throw new InvalidOperationException($"节点“{blockedDeletedNode.Name}”已被运行环境引用，请先停止并清理环境后再删除。");

        var blockedDeletedNetwork = config.Networks.FirstOrDefault(n =>
            n.Nodes.Any(node => referencedNodeKeys.Contains(node.TopologyKey)) && !incomingNetworkKeys.Contains(n.TopologyKey));
        if (blockedDeletedNetwork is not null)
            throw new InvalidOperationException($"内网网段“{blockedDeletedNetwork.Name}”包含运行中的资产，请先停止并清理环境后再删除。");

        var existingNetworks = config.Networks.ToDictionary(n => n.TopologyKey, StringComparer.Ordinal);
        var existingNodes = config.Nodes.ToDictionary(n => n.TopologyKey, StringComparer.Ordinal);

        var removableNodes = config.Nodes.Where(n => !incomingNodeKeys.Contains(n.TopologyKey)).ToList();
        var blockedScore = removableNodes
            .SelectMany(n => n.ScoreItems)
            .FirstOrDefault(i => submittedScoreItemIds.Contains(i.Id));
        if (blockedScore is not null)
            throw new InvalidOperationException($"得分项“{blockedScore.Title}”已有提交记录，请先停止并归档环境后再删除。");

        foreach (var node in removableNodes)
        {
            node.Network.Nodes.Remove(node);
            config.Nodes.Remove(node);
        }

        foreach (var network in config.Networks.Where(n => !incomingNetworkKeys.Contains(n.TopologyKey)).ToList())
            config.Networks.Remove(network);

        var networkMap = new Dictionary<int, PenetrationNetwork>();
        foreach (var networkModel in model.Networks.OrderBy(n => n.OrderIndex))
        {
            var key = EnsureTopologyKey(networkModel.TopologyKey, "network", networkModel.Id);
            if (!existingNetworks.TryGetValue(key, out var network))
            {
                network = new PenetrationNetwork { Config = config, ConfigId = config.Id, TopologyKey = key };
                config.Networks.Add(network);
            }

            network.Name = Clean(networkModel.Name, "未命名网段");
            network.Slug = Slugify(string.IsNullOrWhiteSpace(networkModel.Slug) ? networkModel.Name : networkModel.Slug);
            network.Cidr = CleanNullable(networkModel.Cidr);
            network.ZoneType = NormalizeTeamLabZoneType(networkModel.ZoneType);
            network.TrustLevel = Math.Clamp(networkModel.TrustLevel, 0, 100);
            network.Description = CleanNullable(networkModel.Description);
            network.DefaultPolicy = networkModel.DefaultPolicy;
            network.IsEntry = false;
            network.OrderIndex = networkModel.OrderIndex;
            network.PositionX = networkModel.PositionX;
            network.PositionY = networkModel.PositionY;
            network.Width = Math.Clamp(networkModel.Width <= 0 ? 560 : networkModel.Width, 360, 1800);
            network.Height = Math.Clamp(networkModel.Height <= 0 ? 390 : networkModel.Height, 260, 1200);
            network.Collapsed = networkModel.Collapsed;
            networkMap[networkModel.Id] = network;
        }

        var modelInterfaces = model.Interfaces.Count > 0
            ? model.Interfaces
            : model.Nodes.SelectMany(n => n.Interfaces).ToList();
        var nodeMap = new Dictionary<int, PenetrationNode>();
        var scoreKeyByModelId = new Dictionary<int, string>();

        foreach (var nodeModel in model.Nodes.OrderBy(n => n.OrderIndex))
        {
            var key = EnsureTopologyKey(nodeModel.TopologyKey, "node", nodeModel.Id);
            var primaryNetworkId = ResolvePrimaryNetworkId(nodeModel, modelInterfaces, networkMap);
            var network = networkMap.GetValueOrDefault(primaryNetworkId) ?? config.Networks.OrderBy(n => n.OrderIndex).First();

            if (!existingNodes.TryGetValue(key, out var node))
            {
                node = new PenetrationNode { Config = config, ConfigId = config.Id, TopologyKey = key };
                config.Nodes.Add(node);
            }

            if (node.Network is not null && node.Network.Id != network.Id)
                node.Network.Nodes.Remove(node);

            node.Network = network;
            node.NetworkId = network.Id;
            node.Name = Clean(nodeModel.Name, "未命名节点");
            node.Description = CleanNullable(nodeModel.Description);
            node.PlayerAlias = CleanNullable(nodeModel.PlayerAlias);
            node.PlayerDescription = CleanNullable(nodeModel.PlayerDescription);
            node.NodeType = NormalizeTeamLabNodeType(nodeModel.NodeType);
            node.ImageTemplateId = nodeModel.ImageTemplateId;
            node.ImageName = CleanNullable(nodeModel.ImageName);
            node.CpuCount = Math.Clamp(nodeModel.CpuCount, 1, 128);
            node.MemoryLimit = Math.Clamp(nodeModel.MemoryLimit, 64, 262144);
            node.StorageLimit = Math.Clamp(nodeModel.StorageLimit, 64, 1048576);
            node.ExposePort = Math.Clamp(nodeModel.ExposePort, 1, 65535);
            node.IsEntry = false;
            node.PublishPort = false;
            node.AllowRouting = nodeModel.AllowRouting;
            node.StaticIp = CleanNullable(nodeModel.StaticIp);
            node.EnvironmentVariables = JsonSerializer.Serialize(nodeModel.EnvironmentVariables ?? [], JsonOptions);
            node.StartCommand = CleanNullable(nodeModel.StartCommand);
            node.HealthCheck = CleanNullable(nodeModel.HealthCheck);
            node.ReservedAdRole = CleanNullable(nodeModel.ReservedAdRole);
            node.PositionX = nodeModel.PositionX;
            node.PositionY = nodeModel.PositionY;
            node.OrderIndex = nodeModel.OrderIndex;

            if (!network.Nodes.Contains(node))
                network.Nodes.Add(node);

            var existingScores = node.ScoreItems.ToDictionary(i => i.TopologyKey, StringComparer.Ordinal);
            var incomingScoreKeys = nodeModel.ScoreItems
                .Select(i => EnsureTopologyKey(i.TopologyKey, "score", i.Id))
                .ToHashSet(StringComparer.Ordinal);
            var removedRuntimeScore = node.ScoreItems.FirstOrDefault(i =>
                referencedNodeKeys.Contains(node.TopologyKey) && !incomingScoreKeys.Contains(i.TopologyKey));
            if (removedRuntimeScore is not null)
                throw new InvalidOperationException($"得分项“{removedRuntimeScore.Title}”属于已部署节点，请先停止并清理环境后再删除。");

            var removedSubmittedScore = node.ScoreItems.FirstOrDefault(i =>
                submittedScoreItemIds.Contains(i.Id) && !incomingScoreKeys.Contains(i.TopologyKey));
            if (removedSubmittedScore is not null)
                throw new InvalidOperationException($"得分项“{removedSubmittedScore.Title}”已有提交记录，请先归档环境后再删除。");

            foreach (var score in node.ScoreItems.Where(i => !incomingScoreKeys.Contains(i.TopologyKey)).ToList())
                node.ScoreItems.Remove(score);

            foreach (var itemModel in nodeModel.ScoreItems.OrderBy(i => i.OrderIndex))
            {
                var scoreKey = EnsureTopologyKey(itemModel.TopologyKey, "score", itemModel.Id);
                if (!existingScores.TryGetValue(scoreKey, out var scoreItem))
                {
                    scoreItem = new PenetrationScoreItem { Node = node, NodeId = node.Id, TopologyKey = scoreKey };
                    node.ScoreItems.Add(scoreItem);
                }

                scoreItem.Title = Clean(itemModel.Title, "未命名得分项");
                scoreItem.Description = CleanNullable(itemModel.Description);
                scoreItem.Category = Clean(itemModel.Category, "General");
                scoreItem.Score = Math.Max(0, itemModel.Score);
                scoreItem.IsDynamic = itemModel.IsDynamic;
                scoreItem.StaticFlag = CleanNullable(itemModel.StaticFlag);
                scoreItem.FlagTemplate = CleanNullable(itemModel.FlagTemplate);
                scoreItem.MaxAttempts = Math.Clamp(itemModel.MaxAttempts, 0, 1000);
                scoreItem.IsVisible = itemModel.IsVisible;
                scoreItem.IsCheckpoint = itemModel.IsCheckpoint;
                scoreItem.PrerequisiteItemIds = JsonSerializer.Serialize(itemModel.PrerequisiteItemIds ?? [], JsonOptions);
                scoreItem.OrderIndex = itemModel.OrderIndex;
                scoreKeyByModelId[itemModel.Id] = scoreItem.TopologyKey;
            }

            nodeMap[nodeModel.Id] = node;
        }

        foreach (var nodeModel in model.Nodes.OrderBy(n => n.OrderIndex))
        {
            if (!nodeMap.TryGetValue(nodeModel.Id, out var node))
                continue;

            var interfaces = BuildModelInterfaces(nodeModel, modelInterfaces, networkMap.Keys.ToHashSet());
            var incomingInterfaceKeys = interfaces
                .Select(i => EnsureTopologyKey(i.TopologyKey, "interface", i.Id))
                .ToHashSet(StringComparer.Ordinal);
            var existingInterfaces = node.Interfaces.ToDictionary(i => i.TopologyKey, StringComparer.Ordinal);

            foreach (var iface in node.Interfaces.Where(i => !incomingInterfaceKeys.Contains(i.TopologyKey)).ToList())
            {
                iface.Network?.Interfaces.Remove(iface);
                node.Interfaces.Remove(iface);
            }

            foreach (var interfaceModel in interfaces.OrderBy(i => i.OrderIndex))
            {
                var targetNetworkModelId = networkMap.ContainsKey(interfaceModel.NetworkId)
                    ? interfaceModel.NetworkId
                    : nodeModel.NetworkId;
                if (!networkMap.TryGetValue(targetNetworkModelId, out var network))
                    network = config.Networks.OrderBy(n => n.OrderIndex).First();

                var interfaceKey = EnsureTopologyKey(interfaceModel.TopologyKey, "interface", interfaceModel.Id);
                if (!existingInterfaces.TryGetValue(interfaceKey, out var iface))
                {
                    iface = new PenetrationInterface
                    {
                        Node = node,
                        NodeId = node.Id,
                        TopologyKey = interfaceKey
                    };
                    node.Interfaces.Add(iface);
                }

                if (iface.Network is not null && iface.Network.Id != network.Id)
                    iface.Network?.Interfaces.Remove(iface);

                iface.Node = node;
                iface.NodeId = node.Id;
                iface.Network = network;
                iface.NetworkId = network.Id;
                iface.Name = Clean(interfaceModel.Name, $"eth{interfaceModel.OrderIndex}");
                iface.StaticIp = CleanNullable(interfaceModel.StaticIp);
                iface.IsPrimary = interfaceModel.IsPrimary;
                iface.IsManagement = interfaceModel.IsManagement;
                iface.OrderIndex = interfaceModel.OrderIndex;

                if (!network.Interfaces.Contains(iface))
                    network.Interfaces.Add(iface);
            }
        }

        return new TopologyModelMaps(networkMap, nodeMap, scoreKeyByModelId, false);
    }

    static void AddEdgesToConfig(PenetrationConfig config, PenetrationConfigModel model, TopologyModelMaps maps)
    {
        var incomingEdgeKeys = model.Edges
            .Select(e => EnsureTopologyKey(e.TopologyKey, "edge", e.Id))
            .ToHashSet(StringComparer.Ordinal);
        var existingEdges = config.Edges.ToDictionary(e => e.TopologyKey, StringComparer.Ordinal);

        foreach (var edge in config.Edges.Where(e => !incomingEdgeKeys.Contains(e.TopologyKey)).ToList())
            config.Edges.Remove(edge);

        foreach (var edgeModel in model.Edges)
        {
            var sourceScopeModelId = edgeModel.SourceId > 0 ? edgeModel.SourceId : edgeModel.SourceNodeId;
            var targetScopeModelId = edgeModel.TargetId > 0 ? edgeModel.TargetId : edgeModel.TargetNodeId;
            var sourceNode = edgeModel.SourceNodeId > 0
                ? maps.NodeMap.GetValueOrDefault(edgeModel.SourceNodeId)
                : edgeModel.SourceKind == PenetrationPolicyScope.Node
                    ? maps.NodeMap.GetValueOrDefault(sourceScopeModelId)
                    : null;
            var targetNode = edgeModel.TargetNodeId > 0
                ? maps.NodeMap.GetValueOrDefault(edgeModel.TargetNodeId)
                : edgeModel.TargetKind == PenetrationPolicyScope.Node
                    ? maps.NodeMap.GetValueOrDefault(targetScopeModelId)
                    : null;

            var sourceId = edgeModel.SourceKind == PenetrationPolicyScope.Network
                ? maps.NetworkMap.GetValueOrDefault(sourceScopeModelId)?.Id ?? 0
                : maps.NodeMap.GetValueOrDefault(sourceScopeModelId)?.Id ?? sourceNode?.Id ?? 0;
            var targetId = edgeModel.TargetKind == PenetrationPolicyScope.Network
                ? maps.NetworkMap.GetValueOrDefault(targetScopeModelId)?.Id ?? 0
                : maps.NodeMap.GetValueOrDefault(targetScopeModelId)?.Id ?? targetNode?.Id ?? 0;

            if (sourceId == targetId)
                continue;

            if (!maps.PreserveModelIds && (sourceId <= 0 || targetId <= 0))
                continue;

            var edgeKey = EnsureTopologyKey(edgeModel.TopologyKey, "edge", edgeModel.Id);
            if (!existingEdges.TryGetValue(edgeKey, out var edge))
            {
                edge = new PenetrationEdge
                {
                    ConfigId = config.Id,
                    Config = config,
                    TopologyKey = edgeKey
                };

                if (maps.PreserveModelIds)
                    edge.Id = edgeModel.Id;

                config.Edges.Add(edge);
            }

            edge.ConfigId = config.Id;
            edge.Config = config;
            edge.SourceNodeId = sourceNode?.Id ?? 0;
            edge.TargetNodeId = targetNode?.Id ?? 0;
            edge.SourceKind = edgeModel.SourceKind;
            edge.SourceId = sourceId;
            edge.TargetKind = edgeModel.TargetKind;
            edge.TargetId = targetId;
            edge.Protocol = edgeModel.Protocol;
            edge.PortRange = Clean(edgeModel.PortRange, "any");
            edge.PolicyAction = PenetrationPolicyAction.Allow;
            edge.IsRouteHint = true;
            edge.EnforcementMode = edgeModel.EnforcementMode == PenetrationEnforcementMode.RuntimeRoute
                ? PenetrationEnforcementMode.RuntimeRoute
                : PenetrationEnforcementMode.Both;
            edge.Priority = Math.Clamp(edgeModel.Priority, 0, 10000);
            edge.Label = CleanNullable(edgeModel.Label);
            edge.Description = CleanNullable(edgeModel.Description);
        }
    }

    static void RemapPrerequisites(PenetrationConfig config, Dictionary<int, string> scoreKeyByModelId)
    {
        var savedIdByKey = config.Nodes
            .SelectMany(n => n.ScoreItems)
            .Where(i => i.Id > 0)
            .ToDictionary(i => i.TopologyKey, i => i.Id, StringComparer.Ordinal);

        foreach (var item in config.Nodes.SelectMany(n => n.ScoreItems))
        {
            var prerequisites = DeserializeIntList(item.PrerequisiteItemIds);
            if (prerequisites.Count == 0)
                continue;

            var remapped = prerequisites
                .Select(id => scoreKeyByModelId.GetValueOrDefault(id))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => savedIdByKey.GetValueOrDefault(key!))
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            item.PrerequisiteItemIds = JsonSerializer.Serialize(remapped, JsonOptions);
        }
    }

    static string EnsureTopologyKey(string? key, string prefix, int legacyId)
    {
        var normalized = CleanNullable(key);
        if (!string.IsNullOrWhiteSpace(normalized))
            return normalized.Length <= 64 ? normalized : normalized[..64];

        return legacyId > 0
            ? $"legacy-{prefix}-{legacyId}"
            : $"{prefix}-{Guid.NewGuid():N}";
    }

    static PenetrationPublishedSnapshot CreatePublishedSnapshot(PenetrationConfig config, int publishedVersion)
    {
        var snapshotModel = ToModel(config);
        snapshotModel.PublishedVersion = publishedVersion;
        snapshotModel.Status = PenetrationDeploymentStatus.Published;
        var json = JsonSerializer.Serialize(snapshotModel, JsonOptions);
        return new PenetrationPublishedSnapshot
        {
            GameId = config.GameId,
            PublishedVersion = publishedVersion,
            SnapshotJson = json,
            SnapshotHash = json.ToSHA256String(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    async Task<PenetrationConfig?> LoadPublishedConfig(int gameId, int publishedVersion, CancellationToken token)
    {
        if (publishedVersion <= 0)
            return null;

        var snapshot = await context.PenetrationPublishedSnapshots.AsNoTracking()
            .Where(s => s.GameId == gameId && s.PublishedVersion == publishedVersion)
            .Select(s => s.SnapshotJson)
            .FirstOrDefaultAsync(token);
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            logger.LogError(
                "Penetration published snapshot missing for game {GameId} version {PublishedVersion}.",
                gameId, publishedVersion);
            return null;
        }

        PenetrationConfigModel? model;
        try
        {
            model = JsonSerializer.Deserialize<PenetrationConfigModel>(snapshot, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "Failed to deserialize penetration published snapshot for game {GameId} version {PublishedVersion}.",
                gameId, publishedVersion);
            return null;
        }

        if (model is null)
            return null;

        model.GameId = gameId;
        model.PublishedVersion = publishedVersion;
        model.Status = PenetrationDeploymentStatus.Published;
        return await BuildTransientConfig(gameId, model, token);
    }

    async Task<int> GetAcceptedTeamIndex(int gameId, int teamId, CancellationToken token)
    {
        var teamIds = await context.Participations.AsNoTracking()
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .OrderBy(p => p.TeamId)
            .Select(p => p.TeamId)
            .ToArrayAsync(token);
        var index = Array.IndexOf(teamIds, teamId);
        return Math.Max(0, index);
    }

    static bool IsEnvironmentRunningVersion(PenetrationTeamEnvironment environment, PenetrationConfig config)
    {
        if (environment.Status != PenetrationRuntimeStatus.Running ||
            environment.PublishedVersion != config.PublishedVersion ||
            environment.RuntimeNodes.Count != config.Nodes.Count)
            return false;

        var runningNodeKeys = environment.RuntimeNodes
            .Where(r => r.Status == PenetrationRuntimeStatus.Running && !string.IsNullOrWhiteSpace(r.TopologyNodeKey))
            .Select(r => r.TopologyNodeKey)
            .ToHashSet(StringComparer.Ordinal);

        return config.Nodes.All(n => runningNodeKeys.Contains(n.TopologyKey));
    }

    static string BuildDeployLockKey(int gameId) => $"pentest:deploy:{gameId}";

    static string BuildSubmitLockKey(int gameId, int teamId, int publishedVersion, string scoreItemTopologyKey) =>
        $"pentest:submit:{gameId}:{teamId}:{publishedVersion}:{scoreItemTopologyKey}";

    static void MarkEnvironmentCleanupPending(PenetrationTeamEnvironment environment, List<string> errors)
    {
        environment.CleanupRetryCount++;
        environment.LastError = string.Join('\n', errors.Take(8));
        environment.UpdatedAt = DateTimeOffset.UtcNow;
        environment.LastCleanupAttemptAt = DateTimeOffset.UtcNow;
        environment.NextCleanupAt = environment.CleanupRetryCount >= CleanupBackoff.Length
            ? null
            : DateTimeOffset.UtcNow + CleanupBackoff[environment.CleanupRetryCount - 1];
        environment.Status = environment.CleanupRetryCount >= CleanupBackoff.Length
            ? PenetrationRuntimeStatus.ManualCleanupRequired
            : PenetrationRuntimeStatus.CleanupPending;
    }

    static void AddDeploymentEvent(PenetrationTeamEnvironment environment, string stage,
        PenetrationDeploymentEventLevel level, string message, string? nodeName = null, string? detail = null)
    {
        environment.DeploymentEvents.Add(new PenetrationDeploymentEvent
        {
            Environment = environment,
            Stage = Truncate(stage, 64),
            Level = level,
            Message = Truncate(message, 256),
            NodeName = TruncateNullable(nodeName, 128),
            Detail = TruncateNullable(detail, 1024),
            UserId = DeploymentActor.Value,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    static IDisposable WithDeploymentActor(Guid? userId)
    {
        var previous = DeploymentActor.Value;
        if (userId.HasValue)
            DeploymentActor.Value = userId;
        return new DeploymentActorScope(previous);
    }

    static string ShortContainerId(string? containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId))
            return "unknown";

        return containerId.Length <= 12 ? containerId : containerId[..12];
    }

    async Task<PenetrationValidationModel> ValidateConfig(PenetrationConfig config, CancellationToken token)
    {
        var result = new PenetrationValidationModel { Valid = true };

        if (!TryParseCidr(config.BaseCidr, out _, out var basePrefix))
            result.Errors.Add("基础 CIDR 格式不正确。");

        if (config.TeamSubnetPrefix <= basePrefix)
            result.Errors.Add("队伍网段前缀必须大于基础 CIDR 前缀。");

        if (config.NetworkSubnetPrefix < config.TeamSubnetPrefix)
            result.Errors.Add("内网子网前缀不能小于队伍网段前缀。");

        if (config.Networks.Count == 0)
            result.Errors.Add("至少需要一个内网网段。");
        else
        {
            if (config.Networks.Any(n => n.IsEntry))
                result.Errors.Add("当前 TeamLab 版本统一通过队伍 WireGuard VPN 进入环境，不再支持网段直连标记。");

            AddDuplicateKeyErrors(result, "内网网段", config.Networks.Select(n => (n.Name, n.TopologyKey)));
            var maxSegments = 1 << Math.Max(0, config.NetworkSubnetPrefix - config.TeamSubnetPrefix);
            if (config.Networks.Count > maxSegments)
                result.Errors.Add($"当前队伍网段最多可切分 {maxSegments} 个内网网段，请调整前缀或减少网段数量。");
            foreach (var network in config.Networks)
            {
                if (network.OrderIndex < 0)
                    result.Errors.Add($"内网网段“{network.Name}”的排序序号不能小于 0。");
                else if (network.OrderIndex >= maxSegments && string.IsNullOrWhiteSpace(network.Cidr))
                    result.Errors.Add($"内网网段“{network.Name}”的排序序号 {network.OrderIndex} 超出队伍网段可切分范围（0-{maxSegments - 1}），会导致 CIDR 越界。");
            }
        }

        if (config.Nodes.Count == 0)
            result.Errors.Add("至少需要一个资产节点。");
        else
        {
            AddDuplicateKeyErrors(result, "资产节点", config.Nodes.Select(n => (n.Name, n.TopologyKey)));
            AddDuplicateKeyErrors(result, "得分项", config.Nodes.SelectMany(n =>
                n.ScoreItems.Select(i => ($"{n.Name}/{i.Title}", i.TopologyKey))));
            AddDuplicateKeyErrors(result, "网卡", config.Nodes.SelectMany(n =>
                n.Interfaces.Select(i => ($"{n.Name}/{i.Name}", i.TopologyKey))));
        }

        AddDuplicateKeyErrors(result, "路由关系", config.Edges.Select(e => (e.Label ?? $"关系 {e.Id}", e.TopologyKey)));

        var templateIds = config.Nodes.Where(n => n.ImageTemplateId.HasValue).Select(n => n.ImageTemplateId!.Value)
            .Distinct().ToArray();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, token);

        foreach (var network in config.Networks)
        {
            if (!string.IsNullOrWhiteSpace(network.Cidr) && !TryParseCidr(network.Cidr, out _, out _))
                result.Errors.Add($"内网网段“{network.Name}”的自定义 CIDR 格式不正确。");
        }

        var interfaces = GetEffectiveInterfaces(config);
        var sampleNetworkNames = BuildNetworkPreviewKeys(config);
        var sampleSubnetsByName = BuildNetworkSubnets(config, 0, sampleNetworkNames);
        var sampleSubnetByNetworkId = config.Networks.ToDictionary(
            n => n.Id,
            n => sampleSubnetsByName.GetValueOrDefault(sampleNetworkNames[n.Id]) ?? string.Empty);

        if (TryParseCidr(AllocateSubnet(config.BaseCidr, config.TeamSubnetPrefix, 0), out var sampleTeamNetwork,
                out var sampleTeamPrefix))
        {
            var parsedNetworks = new List<(PenetrationNetwork Network, uint Address, int Prefix)>();

            foreach (var network in config.Networks)
            {
                var subnet = sampleSubnetByNetworkId.GetValueOrDefault(network.Id);
                if (string.IsNullOrWhiteSpace(subnet) || !TryParseCidr(subnet, out var networkAddress, out var networkPrefix))
                    continue;

                parsedNetworks.Add((network, networkAddress, networkPrefix));

                if (!ContainsCidr(sampleTeamNetwork, sampleTeamPrefix, networkAddress, networkPrefix))
                    result.Errors.Add($"内网网段“{network.Name}”的 CIDR 必须位于样例队伍网段内。");

                var interfaceCount = interfaces.Count(i => i.Network.Id == network.Id);
                var capacity = UsableDockerHostCapacity(networkPrefix);
                if ((uint)interfaceCount > capacity)
                    result.Errors.Add($"内网网段“{network.Name}”可用资产 IP 不足：当前需要 {interfaceCount} 个，CIDR {subnet} 约可用 {capacity} 个。");
            }

            for (var i = 0; i < parsedNetworks.Count; i++)
            {
                for (var j = i + 1; j < parsedNetworks.Count; j++)
                {
                    var left = parsedNetworks[i];
                    var right = parsedNetworks[j];
                    if (CidrRangesOverlap(left.Address, left.Prefix, right.Address, right.Prefix))
                        result.Errors.Add($"内网网段“{left.Network.Name}”和“{right.Network.Name}”的 CIDR 存在重叠。");
                }
            }
        }

        var staticIpByNetwork = new Dictionary<int, HashSet<string>>();

        foreach (var node in config.Nodes)
        {
            AddDuplicateKeyErrors(result, $"节点“{node.Name}”的得分项",
                node.ScoreItems.Select(i => (i.Title, i.TopologyKey)));
            AddDuplicateKeyErrors(result, $"节点“{node.Name}”的网卡",
                node.Interfaces.Select(i => (i.Name, i.TopologyKey)));

            if (node.NodeType == PenetrationNodeType.DomainControllerReserved)
                result.Warnings.Add($"节点“{node.Name}”是 AD 预留节点，本版不会自动编排 Windows 域控。");

            if (node.IsEntry || node.PublishPort)
                result.Errors.Add($"节点“{node.Name}”不能启用直连发布；选手必须通过队伍 VPN 内网访问资产。");

            if (!interfaces.Any(i => i.Node.Id == node.Id))
                result.Errors.Add($"节点“{node.Name}”至少需要一个网卡。");

            if (node.ImageTemplateId is { } templateId)
            {
                if (!templates.TryGetValue(templateId, out var template))
                    result.Errors.Add($"节点“{node.Name}”引用的环境模板不存在。");
                else if (template is not { Status: ImageStatus.Ready })
                    result.Errors.Add($"节点“{node.Name}”必须引用已就绪的环境模板。");
            }
            else if (string.IsNullOrWhiteSpace(node.ImageName))
            {
                result.Errors.Add($"节点“{node.Name}”需要选择环境模板或填写 Docker 镜像。");
            }

            foreach (var item in node.ScoreItems)
            {
                if (!item.IsDynamic && string.IsNullOrWhiteSpace(item.StaticFlag))
                    result.Errors.Add($"得分项“{item.Title}”为静态 Flag 时必须填写 Flag。");
            }
        }

        foreach (var item in interfaces)
        {
            if (!string.IsNullOrWhiteSpace(item.StaticIp))
            {
                if (!IPAddress.TryParse(item.StaticIp, out var staticIp))
                {
                    result.Errors.Add($"节点“{item.Node.Name}”网卡“{item.Name}”的固定 IP 格式不正确。");
                    continue;
                }

                if (sampleSubnetByNetworkId.TryGetValue(item.Network.Id, out var sampleSubnet) &&
                    TryParseCidr(sampleSubnet, out var sampleNetwork, out var samplePrefix) &&
                    !ContainsAddress(sampleNetwork, samplePrefix, staticIp))
                    result.Errors.Add($"节点“{item.Node.Name}”网卡“{item.Name}”的固定 IP 必须位于内网网段“{item.Network.Name}”样例 CIDR 内。");

                if (!staticIpByNetwork.TryGetValue(item.Network.Id, out var usedIps))
                {
                    usedIps = new HashSet<string>(StringComparer.Ordinal);
                    staticIpByNetwork[item.Network.Id] = usedIps;
                }

                if (!usedIps.Add(staticIp.ToString()))
                    result.Errors.Add($"内网网段“{item.Network.Name}”中存在重复固定 IP：{staticIp}。");
            }
        }

        foreach (var edge in config.Edges)
        {
            var sourceExists = edge.SourceKind == PenetrationPolicyScope.Network
                ? config.Networks.Any(n => n.Id == edge.SourceId)
                : config.Nodes.Any(n => n.Id == edge.SourceId || n.Id == edge.SourceNodeId);
            var targetExists = edge.TargetKind == PenetrationPolicyScope.Network
                ? config.Networks.Any(n => n.Id == edge.TargetId)
                : config.Nodes.Any(n => n.Id == edge.TargetId || n.Id == edge.TargetNodeId);

            if (!sourceExists || !targetExists)
                result.Errors.Add($"路由关系“{edge.Label ?? edge.Id.ToString()}”引用了不存在的源或目标。");

            if (edge.PolicyAction == PenetrationPolicyAction.Deny)
                result.Errors.Add($"路由关系“{edge.Label ?? edge.Id.ToString()}”不能使用拒绝动作；当前 TeamLab 版本只支持通过连线表达网段路径和题目线索。");

            if (edge.PolicyAction == PenetrationPolicyAction.Allow && edge.IsRouteHint &&
                edge.SourceKind == PenetrationPolicyScope.Node && edge.TargetKind == PenetrationPolicyScope.Node)
            {
                var source = config.Nodes.FirstOrDefault(n => n.Id == edge.SourceId || n.Id == edge.SourceNodeId);
                var target = config.Nodes.FirstOrDefault(n => n.Id == edge.TargetId || n.Id == edge.TargetNodeId);
                if (source is null || target is null)
                    result.Errors.Add($"路由关系“{edge.Label ?? edge.Id.ToString()}”引用了不存在的节点。");
            }
        }

        // Validation uses team index 0 as a deterministic IPAM sample. Runtime deployment
        // recomputes the same shape per team, so warnings here are topology-level signals.
        var runtimeInterfaces = BuildRuntimeInterfaces(config, 0, sampleNetworkNames, sampleSubnetsByName);
        var runtimeRoutes = CompileRuntimeRoutes(config, runtimeInterfaces);
        foreach (var unsupported in runtimeRoutes.Where(r =>
                     RequiresRuntimeRoute(r.Edge) && r.Edge.PolicyAction == PenetrationPolicyAction.Allow &&
                     r.Status == PenetrationRouteStatus.Unsupported))
            result.Errors.Add($"路由关系“{unsupported.Label}”无法执行为网络级路由：{unsupported.Message}");

        foreach (var duplicate in runtimeRoutes
                     .Where(r => r.Status == PenetrationRouteStatus.HintOnly &&
                                 r.Message.Contains("同一网段路径已由", StringComparison.Ordinal))
                     .Take(6))
            result.Warnings.Add(
                $"路由关系“{duplicate.Label}”覆盖了已有运行期网段路径：{duplicate.Message}");

        if (config.Edges.Count > 0)
            result.Warnings.Add("路由关系会进入部署计划和任务链；RuntimeRoute/Both 表达网段级连通路径。协议/端口字段只作为出题备注，不作为防火墙规则。");

        result.Valid = result.Errors.Count == 0;
        return result;
    }

    static void AddDuplicateKeyErrors(PenetrationValidationModel result, string scope,
        IEnumerable<(string Name, string TopologyKey)> items)
    {
        foreach (var group in items
                     .Where(i => !string.IsNullOrWhiteSpace(i.TopologyKey))
                     .GroupBy(i => i.TopologyKey, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            var names = string.Join("、", group.Select(i => i.Name).Take(4));
            result.Errors.Add($"{scope}存在重复拓扑标识“{group.Key}”：{names}。");
        }
    }

    async Task<PenetrationPlanModel> BuildPlan(PenetrationConfig config, int teamIndex, int teamId, int teamCount,
        CancellationToken token)
    {
        var validation = await ValidateConfig(config, token);
        var runtime = await BuildRuntimePlan(config, teamIndex, teamId, token);
        return new PenetrationPlanModel
        {
            GameId = config.GameId,
            TeamCount = teamCount,
            SampleTeamPrefix = AllocateSubnet(config.BaseCidr, config.TeamSubnetPrefix, teamIndex),
            Validation = validation,
            Networks = runtime.Networks.Select(n => new PenetrationPlanNetworkModel
            {
                NetworkId = n.Network.Id,
                NetworkName = n.NetworkName,
                Slug = n.Network.Slug,
                ZoneType = n.Network.ZoneType,
                Cidr = n.Cidr,
                DefaultPolicy = n.Network.DefaultPolicy,
                IsInternal = n.IsInternal
            }).ToList(),
            Nodes = runtime.Nodes.Select(n => new PenetrationPlanNodeModel
            {
                NodeId = n.Node.Id,
                NodeName = n.Node.Name,
                NodeType = n.Node.NodeType,
                Image = n.Image,
                PublishPort = false,
                ExposePort = n.Node.ExposePort,
                AdminAccessHint = "TeamLab 内网资产，可通过运行观测查看网卡事实和资源状态",
                Interfaces = n.Interfaces.Select(i => new PenetrationPlanInterfaceModel
                {
                    InterfaceId = i.InterfaceId,
                    Name = i.InterfaceName,
                    NetworkId = i.Network.Id,
                    NetworkName = i.NetworkName,
                    NetworkSlug = i.Network.Slug,
                    Cidr = i.Cidr,
                    IpAddress = i.IpAddress,
                    IsPrimary = i.IsPrimary,
                    IsManagement = i.IsManagement,
                    IsInternal = runtime.Networks.FirstOrDefault(n => n.Network.Id == i.Network.Id)?.IsInternal ?? IsInternalNetwork(i.Network)
                }).ToList()
            }).ToList(),
            Policies = runtime.Routes.OrderBy(r => r.Edge.Priority).ThenBy(r => r.Edge.Id).Select(r => new PenetrationPlanPolicyModel
            {
                PolicyId = r.Edge.Id,
                Label = r.Label,
                Source = ResolvePolicyName(config, r.Edge.SourceKind, r.Edge.SourceId, r.Edge.SourceNodeId),
                Target = ResolvePolicyName(config, r.Edge.TargetKind, r.Edge.TargetId, r.Edge.TargetNodeId),
                Protocol = r.Edge.Protocol,
                PortRange = r.Edge.PortRange,
                Action = r.Edge.PolicyAction,
                IsRouteHint = r.Edge.IsRouteHint,
                EnforcementMode = r.Edge.EnforcementMode,
                RouteStatus = r.Status,
                RuntimeSummary = r.Status == PenetrationRouteStatus.RoutePlanned
                    ? "将部署网络级显式路由"
                    : r.Status == PenetrationRouteStatus.HintOnly
                        ? "仅作为题目提示/内网路径"
                        : "首版无法执行为网络级路由",
                RouteNodeName = r.RouteNode?.Name,
                SourceNetworkName = r.SourceInterface?.Network.Name,
                TargetNetworkName = r.TargetInterface?.Network.Name,
                GatewayIp = r.SourceRouteInterface?.IpAddress,
                CompileMessage = r.Message,
                IsExecutable = r.IsExecutable
            }).ToList(),
            Flags = config.Nodes.SelectMany(n => n.ScoreItems.Select(i => new PenetrationPlanFlagModel
            {
                ScoreItemId = i.Id,
                NodeId = n.Id,
                NodeName = n.Name,
                Title = i.Title,
                Category = i.Category,
                Score = i.Score,
                IsDynamic = i.IsDynamic,
                Preview = i.IsDynamic ? BuildFlag(i, config.GameId, teamId, config.PublishedVersion) : "静态 Flag"
            })).ToList(),
            DeploymentSteps =
            [
                "为每支队伍分配独立队伍网段。",
                "按内网网段创建平台管理的 Linux bridge/veth 或 VM bridge 网络；所有资产只接入队伍 VPN 内网。",
                "按资产网卡把 Docker veth 或 VM bridge 网卡接入对应内网网段，并配置固定 IP/CIDR。",
                "对 RuntimeRoute/Both 路由关系编译并应用网络级显式路由；协议和端口字段只作为出题备注，不做运行期阻断。",
                "注入动态 Flag、环境变量和资源限制。",
                "生成队伍 VPN、运行节点事实、网卡摘要和提交日志。"
            ]
        };
    }

    async Task<RuntimePlan> BuildRuntimePlan(PenetrationConfig config, int teamIndex, int teamId,
        CancellationToken token)
    {
        var networkNames = config.Networks.ToDictionary(n => n.Id, n => BuildRuntimeNetworkName(config, teamId, n));
        var networkSubnets = BuildNetworkSubnets(config, teamIndex, networkNames);
        var runtimeInterfaces = BuildRuntimeInterfaces(config, teamIndex, networkNames, networkSubnets);
        var routePlans = CompileRuntimeRoutes(config, runtimeInterfaces);
        var networks = config.Networks.OrderBy(n => n.OrderIndex).Select(n => new RuntimeNetworkPlan(
            n,
            networkNames[n.Id],
            networkSubnets.GetValueOrDefault(networkNames[n.Id]) ?? AllocateSubnet(config.BaseCidr, config.NetworkSubnetPrefix, n.OrderIndex),
            IsInternalNetwork(n)
        )).ToList();
        var routeNodeKeys = routePlans
            .Where(r => r.Status == PenetrationRouteStatus.RoutePlanned && r.RouteNode is not null)
            .Select(r => r.RouteNode!.TopologyKey)
            .ToHashSet(StringComparer.Ordinal);
        var routedEndpointKeys = routePlans
            .Where(r => r.Status == PenetrationRouteStatus.RoutePlanned)
            .SelectMany(r => r.EndpointNodeKeys)
            .ToHashSet(StringComparer.Ordinal);
        var nodes = new List<RuntimeNodePlan>();
        foreach (var node in config.Nodes.OrderBy(n => n.OrderIndex))
        {
            var image = ResolveImageDisplayName(node);
            var flagMap = BuildFlagMap(node, config.GameId, teamId, config.PublishedVersion);
            var interfaces = runtimeInterfaces.Where(i => i.Node.Id == node.Id)
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.OrderIndex)
                .ToList();
            nodes.Add(new RuntimeNodePlan(
                node,
                image,
                interfaces,
                flagMap,
                networks.ToDictionary(n => n.Network.Id, n => n.IsInternal),
                routeNodeKeys.Contains(node.TopologyKey),
                routeNodeKeys.Contains(node.TopologyKey) || routedEndpointKeys.Contains(node.TopologyKey)));
        }

        return new RuntimePlan(networks, nodes, routePlans);
    }

    static string ResolveImageDisplayName(PenetrationNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.ImageName))
            return node.ImageName;

        return node.ImageTemplateId is { } templateId
            ? $"ImageTemplate:{templateId}"
            : string.Empty;
    }

    List<RuntimeRoutePlan> CompileRuntimeRoutes(PenetrationConfig config, IReadOnlyList<RuntimeInterfacePlan> interfaces)
    {
        var plans = new List<RuntimeRoutePlan>();
        var interfacesByNode = interfaces.GroupBy(i => i.Node.Id).ToDictionary(g => g.Key, g => g.ToList());
        var interfacesByNetwork = interfaces.GroupBy(i => i.Network.Id).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var edge in config.Edges.OrderBy(e => e.Priority).ThenBy(e => e.Id))
        {
            var label = string.IsNullOrWhiteSpace(edge.Label) ? "内网路由关系" : edge.Label;
            if (!RequiresRuntimeRoute(edge))
            {
                plans.Add(RuntimeRoutePlan.Hint(edge, label, "该策略仅用于题目路径提示，不改变运行期网络可达性。"));
                continue;
            }

            if (edge.PolicyAction != PenetrationPolicyAction.Allow)
            {
                plans.Add(RuntimeRoutePlan.Unsupported(edge, label,
                    "当前 TeamLab 版本不支持拒绝动作；请删除该策略或改为路径提示。"));
                continue;
            }

            var sourceNetworks = ResolvePolicyNetworks(config, interfacesByNode, edge.SourceKind, edge.SourceId, edge.SourceNodeId);
            var targetNetworks = ResolvePolicyNetworks(config, interfacesByNode, edge.TargetKind, edge.TargetId, edge.TargetNodeId);
            if (sourceNetworks.Count == 0 || targetNetworks.Count == 0)
            {
                plans.Add(RuntimeRoutePlan.Unsupported(edge, label, "源或目标没有可解析的内网网段网卡。"));
                continue;
            }

            var distinctPairs = sourceNetworks
                .SelectMany(source => targetNetworks.Select(target => (Source: source, Target: target)))
                .Where(pair => pair.Source.Network.Id != pair.Target.Network.Id)
                .DistinctBy(pair => $"{pair.Source.Network.Id}:{pair.Target.Network.Id}")
                .ToList();

            if (distinctPairs.Count == 0)
            {
                plans.Add(RuntimeRoutePlan.Hint(edge, label, "源和目标位于同一内网网段，二层网络内天然可达，无需生成显式路由。"));
                continue;
            }

            var edgePlannedPairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in distinctPairs)
            {
                var pairKey = BuildRuntimeRoutePairKey(pair.Source.Network.Id, pair.Target.Network.Id);
                if (!edgePlannedPairs.Add(pairKey))
                    continue;

                var existingExecutable = plans
                    .Where(p => p.IsExecutable)
                    .FirstOrDefault(p => p.PairKey == pairKey);
                if (existingExecutable is not null)
                {
                    plans.Add(RuntimeRoutePlan.Hint(edge, label,
                        $"同一网段路径已由更高优先级关系“{existingExecutable.Label}”生成网络级路由，本关系仅保留为题目提示和审计记录。"));
                    continue;
                }

                var routeNode = FindRouteNode(config, interfacesByNode, pair.Source.Network.Id, pair.Target.Network.Id);
                if (routeNode is null)
                {
                    plans.Add(RuntimeRoutePlan.Unsupported(edge, label,
                        $"无法连接“{pair.Source.Network.Name}”到“{pair.Target.Network.Name}”：缺少同时连接两个网段且允许路由的跳板/防火墙节点。"));
                    continue;
                }

                if (!interfacesByNode.TryGetValue(routeNode.Id, out var routeInterfaces))
                {
                    plans.Add(RuntimeRoutePlan.Unsupported(edge, label, $"路由节点“{routeNode.Name}”没有运行期网卡。"));
                    continue;
                }

                var sourceRouteInterface = SelectRouteInterface(routeInterfaces, pair.Source.Network.Id);
                var targetRouteInterface = SelectRouteInterface(routeInterfaces, pair.Target.Network.Id);
                var sourceEndpointNodes = ResolveEndpointNodes(edge.SourceKind, edge.SourceId, edge.SourceNodeId,
                    interfacesByNetwork.GetValueOrDefault(pair.Source.Network.Id) ?? [], routeNode.Id);
                var targetEndpointNodes = ResolveEndpointNodes(edge.TargetKind, edge.TargetId, edge.TargetNodeId,
                    interfacesByNetwork.GetValueOrDefault(pair.Target.Network.Id) ?? [], routeNode.Id);

                if (sourceEndpointNodes.Count == 0 || targetEndpointNodes.Count == 0)
                {
                    plans.Add(RuntimeRoutePlan.Unsupported(edge, label,
                        $"无法连接“{pair.Source.Network.Name}”到“{pair.Target.Network.Name}”：源或目标网段缺少除路由节点以外的可探测端点。"));
                    continue;
                }

                var commandSummary =
                    $"源端点: ip route replace {targetRouteInterface.Cidr} via {sourceRouteInterface.IpAddress}; 路由节点: sysctl net.ipv4.ip_forward=1; 目标端点: ip route replace {sourceRouteInterface.Cidr} via {targetRouteInterface.IpAddress}";
                plans.Add(new RuntimeRoutePlan(
                    edge,
                    label,
                    PenetrationRouteStatus.RoutePlanned,
                    routeNode,
                    pair.Source,
                    pair.Target,
                    sourceRouteInterface,
                    targetRouteInterface,
                    sourceEndpointNodes,
                    targetEndpointNodes,
                    commandSummary,
                    $"将通过“{routeNode.Name}”启用“{pair.Source.Network.Name}”与“{pair.Target.Network.Name}”的网络级可达性；首版会写入反向路由保证回包，协议/端口仅作为说明，不做运行期阻断。"));
            }
        }

        return plans;
    }

    static bool RequiresRuntimeRoute(PenetrationEdge edge) =>
        edge.EnforcementMode is PenetrationEnforcementMode.RuntimeRoute or PenetrationEnforcementMode.Both;

    static string BuildRuntimeRoutePairKey(int sourceNetworkId, int targetNetworkId) =>
        sourceNetworkId <= targetNetworkId
            ? $"{sourceNetworkId}:{targetNetworkId}"
            : $"{targetNetworkId}:{sourceNetworkId}";

    static List<RuntimeInterfacePlan> ResolvePolicyNetworks(PenetrationConfig config,
        Dictionary<int, List<RuntimeInterfacePlan>> interfacesByNode, PenetrationPolicyScope kind, int id,
        int fallbackNodeId)
    {
        if (kind == PenetrationPolicyScope.Network)
            return config.Nodes
                .Where(n => n.NetworkId == id)
                .SelectMany(n => interfacesByNode.GetValueOrDefault(n.Id) ?? [])
                .Where(i => i.Network.Id == id)
                .DistinctBy(i => i.Network.Id)
                .ToList();

        var nodeId = id > 0 ? id : fallbackNodeId;
        return interfacesByNode.GetValueOrDefault(nodeId)?.DistinctBy(i => i.Network.Id).ToList() ?? [];
    }

    static PenetrationNode? FindRouteNode(PenetrationConfig config,
        Dictionary<int, List<RuntimeInterfacePlan>> interfacesByNode, int sourceNetworkId, int targetNetworkId)
    {
        return config.Nodes
            .Where(n => IsRouteCapableNode(n))
            .Select(n => new
            {
                Node = n,
                Interfaces = interfacesByNode.GetValueOrDefault(n.Id) ?? []
            })
            .Where(x => x.Interfaces.Any(i => i.Network.Id == sourceNetworkId) &&
                        x.Interfaces.Any(i => i.Network.Id == targetNetworkId))
            .OrderByDescending(x => x.Node.AllowRouting)
            .ThenBy(x => x.Node.OrderIndex)
            .Select(x => x.Node)
            .FirstOrDefault();
    }

    static RuntimeInterfacePlan SelectRouteInterface(IEnumerable<RuntimeInterfacePlan> interfaces, int networkId) =>
        interfaces
            .Where(i => i.Network.Id == networkId)
            .OrderByDescending(i => i.IsPrimary)
            .ThenByDescending(i => i.IsManagement)
            .ThenBy(i => i.OrderIndex)
            .First();

    static bool IsRouteCapableNode(PenetrationNode node) =>
        node.AllowRouting || node.NodeType is PenetrationNodeType.JumpHost or PenetrationNodeType.Bastion or PenetrationNodeType.FirewallRouter;

    static List<RuntimeInterfacePlan> ResolveEndpointNodes(PenetrationPolicyScope kind, int id, int fallbackNodeId,
        IReadOnlyList<RuntimeInterfacePlan> networkInterfaces, int routeNodeId)
    {
        if (kind == PenetrationPolicyScope.Node)
        {
            var nodeId = id > 0 ? id : fallbackNodeId;
            return networkInterfaces.Where(i => i.Node.Id == nodeId)
                .DistinctBy(i => i.Node.Id)
                .ToList();
        }

        return networkInterfaces.Where(i => i.Node.Id != routeNodeId)
            .DistinctBy(i => i.Node.Id)
            .ToList();
    }

    int ResolveDeploymentParallelism()
    {
        var config = serviceProvider.GetService<IConfiguration>();
        return Math.Clamp(config?.GetValue("Penetration:DeploymentParallelism", 2) ?? 2, 1, 4);
    }

    async Task<PenetrationConfig?> LoadConfig(int gameId, CancellationToken token = default) =>
        await context.PenetrationConfigs
            .Include(c => c.Networks).ThenInclude(n => n.Interfaces)
            .Include(c => c.Edges)
            .Include(c => c.Nodes).ThenInclude(n => n.Interfaces).ThenInclude(i => i.Network)
            .Include(c => c.Nodes).ThenInclude(n => n.ScoreItems)
            .Include(c => c.Nodes).ThenInclude(n => n.ImageTemplate)
            .Include(c => c.Nodes).ThenInclude(n => n.Network)
            .FirstOrDefaultAsync(c => c.GameId == gameId, token);

    async Task<PenetrationTeamEnvironment?> LoadTeamEnvironment(int gameId, int teamId, CancellationToken token) =>
        await context.PenetrationTeamEnvironments
            .Include(e => e.Team)
            .Include(e => e.Node)
            .Include(e => e.DeploymentEvents)
            .Include(e => e.RuntimeRoutes)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.Container)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.TopologyNode).ThenInclude(n => n.Interfaces).ThenInclude(i => i.Network)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.TopologyNode).ThenInclude(n => n.ScoreItems)
            .FirstOrDefaultAsync(e => e.GameId == gameId && e.TeamId == teamId, token);

    async Task<PenetrationTeamEnvironment?> LoadTeamEnvironmentById(int environmentId, CancellationToken token) =>
        await context.PenetrationTeamEnvironments
            .Include(e => e.Team)
            .Include(e => e.Node)
            .Include(e => e.DeploymentEvents)
            .Include(e => e.RuntimeRoutes)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.Container)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.TopologyNode).ThenInclude(n => n.Interfaces).ThenInclude(i => i.Network)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.TopologyNode).ThenInclude(n => n.ScoreItems)
            .FirstOrDefaultAsync(e => e.Id == environmentId, token);

    static PenetrationConfigModel ToModel(PenetrationConfig config)
    {
        var networkNames = BuildNetworkPreviewKeys(config);
        var networkPreview = BuildNetworkSubnets(config, 0, networkNames)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var effectiveInterfaces = GetEffectiveInterfaces(config).ToList();
        var previewByInterface = BuildPreviewInterfaceIps(config, effectiveInterfaces);

        return new PenetrationConfigModel
        {
            GameId = config.GameId,
            BaseCidr = config.BaseCidr,
            TeamSubnetPrefix = config.TeamSubnetPrefix,
            NetworkSubnetPrefix = config.NetworkSubnetPrefix,
            MaxResetCount = config.MaxResetCount,
            PublishedVersion = config.PublishedVersion,
            Status = config.Status,
            Networks = config.Networks.OrderBy(n => n.OrderIndex).Select((n, index) => new PenetrationNetworkModel
            {
                Id = n.Id,
                TopologyKey = n.TopologyKey,
                Name = n.Name,
                Slug = n.Slug,
                Cidr = n.Cidr,
                ZoneType = NormalizeTeamLabZoneType(n.ZoneType),
                TrustLevel = n.TrustLevel,
                Description = n.Description,
                DefaultPolicy = n.DefaultPolicy,
                OrderIndex = n.OrderIndex,
                IsEntry = false,
                PositionX = n.PositionX == 0 && n.PositionY == 0 ? 80 + index * 620 : n.PositionX,
                PositionY = n.PositionX == 0 && n.PositionY == 0 ? 80 + (index % 2) * 40 : n.PositionY,
                Width = n.Width <= 0 ? 560 : n.Width,
                Height = n.Height <= 0 ? 390 : n.Height,
                Collapsed = n.Collapsed,
                PreviewCidr = networkPreview.GetValueOrDefault(networkNames[n.Id]) ?? n.Cidr
            }).ToList(),
            Nodes = config.Nodes.OrderBy(n => n.OrderIndex).Select(n =>
            {
                var nodeInterfaces = effectiveInterfaces.Where(i => i.Node.Id == n.Id)
                    .OrderBy(i => i.OrderIndex)
                    .Select(i => ToInterfaceModel(i, previewByInterface.GetValueOrDefault(i.Key)))
                    .ToList();
                var primary = nodeInterfaces.FirstOrDefault(i => i.IsPrimary) ?? nodeInterfaces.FirstOrDefault();
                return new PenetrationNodeModel
                {
                    Id = n.Id,
                    TopologyKey = n.TopologyKey,
                    NetworkId = primary?.NetworkId ?? n.NetworkId,
                    Name = n.Name,
                    Description = n.Description,
                    PlayerAlias = n.PlayerAlias,
                    PlayerDescription = n.PlayerDescription,
                    NodeType = NormalizeTeamLabNodeType(n.NodeType),
                    ImageTemplateId = n.ImageTemplateId,
                    ImageName = n.ImageName,
                    CpuCount = n.CpuCount,
                    MemoryLimit = n.MemoryLimit,
                    StorageLimit = n.StorageLimit,
                    ExposePort = n.ExposePort,
                    IsEntry = false,
                    PublishPort = false,
                    AllowRouting = n.AllowRouting,
                    StaticIp = primary?.StaticIp ?? n.StaticIp,
                    EnvironmentVariables = DeserializeDictionary(n.EnvironmentVariables),
                    StartCommand = n.StartCommand,
                    HealthCheck = n.HealthCheck,
                    ReservedAdRole = n.ReservedAdRole,
                    PositionX = n.PositionX,
                    PositionY = n.PositionY,
                    OrderIndex = n.OrderIndex,
                    PreviewIp = primary?.PreviewIp,
                    Interfaces = nodeInterfaces,
                    ScoreItems = n.ScoreItems.OrderBy(i => i.OrderIndex).Select(i => new PenetrationScoreItemModel
                    {
                        Id = i.Id,
                        TopologyKey = i.TopologyKey,
                        Title = i.Title,
                        Description = i.Description,
                        Category = i.Category,
                        Score = i.Score,
                        IsDynamic = i.IsDynamic,
                        StaticFlag = i.StaticFlag,
                        FlagTemplate = i.FlagTemplate,
                        MaxAttempts = i.MaxAttempts,
                        IsVisible = i.IsVisible,
                        IsCheckpoint = i.IsCheckpoint,
                        PrerequisiteItemIds = DeserializeIntList(i.PrerequisiteItemIds),
                        OrderIndex = i.OrderIndex
                    }).ToList()
                };
            }).ToList(),
            Interfaces = effectiveInterfaces.OrderBy(i => i.Node.OrderIndex).ThenBy(i => i.OrderIndex)
                .Select(i => ToInterfaceModel(i, previewByInterface.GetValueOrDefault(i.Key)))
                .ToList(),
            Edges = config.Edges.Select(e => new PenetrationEdgeModel
            {
                Id = e.Id,
                TopologyKey = e.TopologyKey,
                SourceNodeId = e.SourceNodeId,
                TargetNodeId = e.TargetNodeId,
                SourceKind = e.SourceKind,
                SourceId = e.SourceId,
                TargetKind = e.TargetKind,
                TargetId = e.TargetId,
                Protocol = e.Protocol,
                PortRange = e.PortRange,
                PolicyAction = e.PolicyAction,
                IsRouteHint = e.IsRouteHint,
                EnforcementMode = e.EnforcementMode,
                Priority = e.Priority,
                Label = e.Label,
                Description = e.Description
            }).ToList()
        };
    }

    static PenetrationInterfaceModel ToInterfaceModel(EffectiveInterface item, string? previewIp) => new()
    {
        Id = item.InterfaceId,
        TopologyKey = item.TopologyKey,
        NodeId = item.Node.Id,
        NetworkId = item.Network.Id,
        Name = item.Name,
        StaticIp = item.StaticIp,
        PreviewIp = previewIp,
        IsPrimary = item.IsPrimary,
        IsManagement = item.IsManagement,
        OrderIndex = item.OrderIndex
    };

    static Dictionary<string, string> BuildPreviewInterfaceIps(PenetrationConfig config,
        IReadOnlyCollection<EffectiveInterface> interfaces)
    {
        var networkNames = BuildNetworkPreviewKeys(config);
        var subnets = BuildNetworkSubnets(config, 0, networkNames);
        return AllocateInterfaceIps(config, interfaces, 0, networkNames, subnets)
            .ToDictionary(kv => kv.Key.Key, kv => kv.Value);
    }

    List<RuntimeInterfacePlan> BuildRuntimeInterfaces(PenetrationConfig config, int teamIndex,
        Dictionary<int, string> networkNames, Dictionary<string, string> networkSubnets)
    {
        var effectiveInterfaces = GetEffectiveInterfaces(config).ToList();
        var ipMap = AllocateInterfaceIps(config, effectiveInterfaces, teamIndex, networkNames, networkSubnets);
        return effectiveInterfaces.Select(item =>
        {
            var networkName = networkNames[item.Network.Id];
            return new RuntimeInterfacePlan(
                item.InterfaceId,
                item.Node,
                item.Network,
                item.Name,
                networkName,
                networkSubnets.GetValueOrDefault(networkName) ?? string.Empty,
                ipMap[item],
                item.IsPrimary,
                item.IsManagement,
                item.OrderIndex
            );
        }).ToList();
    }

    static IReadOnlyList<EffectiveInterface> GetEffectiveInterfaces(PenetrationConfig config)
    {
        var items = new List<EffectiveInterface>();
        foreach (var node in config.Nodes.OrderBy(n => n.OrderIndex))
        {
            var nodeInterfaces = node.Interfaces.Count > 0
                ? node.Interfaces.OrderBy(i => i.OrderIndex).ToList()
                : [];

            if (nodeInterfaces.Count == 0)
            {
                var network = node.Network ?? config.Networks.First(n => n.Id == node.NetworkId);
                items.Add(new EffectiveInterface(
                    -(node.Id * 1000 + 1),
                    $"{node.TopologyKey}:eth0",
                    node,
                    network,
                    "eth0",
                    node.StaticIp,
                    true,
                    false,
                    0));
                continue;
            }

            if (nodeInterfaces.All(i => !i.IsPrimary))
                nodeInterfaces[0].IsPrimary = true;

            items.AddRange(nodeInterfaces.Select(i => new EffectiveInterface(
                i.Id,
                i.TopologyKey,
                node,
                i.Network,
                i.Name,
                i.StaticIp,
                i.IsPrimary,
                i.IsManagement,
                i.OrderIndex)));
        }

        return items;
    }

    static Dictionary<EffectiveInterface, string> AllocateInterfaceIps(PenetrationConfig config,
        IReadOnlyCollection<EffectiveInterface> interfaces, int teamIndex, Dictionary<int, string> networkNames,
        Dictionary<string, string> networkSubnets)
    {
        var result = new Dictionary<EffectiveInterface, string>();
        var counters = interfaces.GroupBy(i => i.Network.Id).ToDictionary(g => g.Key, _ => 2u);
        var usedByNetwork = interfaces.GroupBy(i => i.Network.Id)
            .ToDictionary(g => g.Key, _ => new HashSet<string>(StringComparer.Ordinal));
        var ordered = interfaces.OrderBy(i => i.Network.OrderIndex).ThenBy(i => i.Node.OrderIndex).ThenBy(i => i.OrderIndex)
            .ToArray();

        foreach (var item in ordered)
        {
            if (!string.IsNullOrWhiteSpace(item.StaticIp) && IPAddress.TryParse(item.StaticIp, out var staticIp))
            {
                var shifted = ShiftStaticIp(config, item.Network.Id, staticIp, teamIndex, networkNames);
                result[item] = shifted;
                usedByNetwork[item.Network.Id].Add(shifted);
            }
        }

        foreach (var item in ordered)
        {
            if (result.ContainsKey(item))
                continue;

            var networkName = networkNames[item.Network.Id];
            var subnet = networkSubnets[networkName];
            TryParseCidr(subnet, out var network, out var prefix);
            var (_, end) = CidrRange(network, prefix);
            var offset = counters[item.Network.Id]++;
            var candidate = network + offset;

            while ((ulong)candidate < end && usedByNetwork[item.Network.Id].Contains(FromUInt(candidate).ToString()))
            {
                offset = counters[item.Network.Id]++;
                candidate = network + offset;
            }

            var ip = FromUInt(candidate).ToString();
            result[item] = ip;
            usedByNetwork[item.Network.Id].Add(ip);
        }

        return result;
    }

    static string ShiftStaticIp(PenetrationConfig config, int networkId, IPAddress staticIp, int teamIndex,
        Dictionary<int, string> networkNames)
    {
        var sampleSubnets = BuildNetworkSubnets(config, 0, networkNames);
        var teamSubnets = BuildNetworkSubnets(config, teamIndex, networkNames);
        var networkName = networkNames[networkId];
        if (!TryParseCidr(sampleSubnets[networkName], out var sampleNetwork, out _) ||
            !TryParseCidr(teamSubnets[networkName], out var teamNetwork, out _))
            return staticIp.ToString();

        var ipValue = ToUInt(staticIp);
        var offset = ipValue >= sampleNetwork ? ipValue - sampleNetwork : 0;
        return FromUInt(teamNetwork + offset).ToString();
    }

    static List<PenetrationInterfaceModel> BuildModelInterfaces(PenetrationNodeModel node,
        IReadOnlyCollection<PenetrationInterfaceModel> allInterfaces, HashSet<int> networkIds)
    {
        var interfaces = allInterfaces.Where(i => i.NodeId == node.Id).ToList();
        if (interfaces.Count == 0)
            interfaces = node.Interfaces.ToList();

        if (interfaces.Count == 0)
        {
            interfaces.Add(new PenetrationInterfaceModel
            {
                Id = -1,
                NodeId = node.Id,
                NetworkId = node.NetworkId,
                Name = "eth0",
                StaticIp = node.StaticIp,
                IsPrimary = true,
                IsManagement = false,
                OrderIndex = 0
            });
        }

        var normalized = interfaces
            .Where(i => networkIds.Contains(i.NetworkId))
            .OrderBy(i => i.OrderIndex)
            .Select((i, index) => new PenetrationInterfaceModel
            {
                Id = i.Id,
                NodeId = node.Id,
                NetworkId = i.NetworkId,
                Name = string.IsNullOrWhiteSpace(i.Name) ? $"eth{index}" : i.Name,
                StaticIp = i.StaticIp,
                IsPrimary = i.IsPrimary,
                IsManagement = i.IsManagement,
                OrderIndex = index
            }).ToList();

        if (normalized.Count == 0)
            normalized.Add(new PenetrationInterfaceModel
            {
                NodeId = node.Id,
                NetworkId = node.NetworkId,
                Name = "eth0",
                StaticIp = node.StaticIp,
                IsPrimary = true,
                IsManagement = false
            });

        if (normalized.All(i => !i.IsPrimary))
            normalized[0].IsPrimary = true;

        return normalized;
    }

    static int ResolvePrimaryNetworkId(PenetrationNodeModel node,
        IReadOnlyCollection<PenetrationInterfaceModel> interfaces, Dictionary<int, PenetrationNetwork> networkMap)
    {
        var primary = interfaces.FirstOrDefault(i => i.NodeId == node.Id && i.IsPrimary)
                      ?? node.Interfaces.FirstOrDefault(i => i.IsPrimary)
                      ?? interfaces.FirstOrDefault(i => i.NodeId == node.Id)
                      ?? node.Interfaces.FirstOrDefault();
        return primary is not null && networkMap.ContainsKey(primary.NetworkId) ? primary.NetworkId : node.NetworkId;
    }

    static List<PenetrationInterfaceModel> BuildWorkspaceInterfaces(PenetrationNode node,
        IReadOnlyCollection<RuntimeInterfaceInfo> runtimeInterfaces)
    {
        if (runtimeInterfaces.Count > 0)
            return runtimeInterfaces.Select((item, index) => new PenetrationInterfaceModel
            {
                Id = item.InterfaceId,
                NodeId = node.Id,
                NetworkId = item.NetworkId,
                Name = item.InterfaceName,
                PreviewIp = item.IpAddress,
                IsPrimary = item.IsPrimary,
                IsManagement = item.IsManagement,
                OrderIndex = index
            }).ToList();

        return GetEffectiveInterfaces(new PenetrationConfig
        {
            Nodes = [node],
            Networks = node.Interfaces.Select(i => i.Network).DistinctBy(n => n.Id).ToList()
        }).Select(i => new PenetrationInterfaceModel
        {
            Id = i.InterfaceId,
            NodeId = node.Id,
            NetworkId = i.Network.Id,
            Name = i.Name,
            StaticIp = i.StaticIp,
            IsPrimary = i.IsPrimary,
            IsManagement = i.IsManagement,
            OrderIndex = i.OrderIndex
        }).ToList();
    }

    static RuntimeInterfaceInfo[] ReadRuntimeInterfaces(PenetrationRuntimeNode runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.InterfaceSummary))
            return [];

        try
        {
            return JsonSerializer.Deserialize<RuntimeInterfaceInfo[]>(runtime.InterfaceSummary, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    static string SerializeRuntimeInterfaces(IEnumerable<RuntimeInterfaceInfo> interfaces) =>
        JsonSerializer.Serialize(interfaces.Select(i => new RuntimeInterfaceInfo(
            i.InterfaceId,
            i.NodeId,
            i.NetworkId,
            i.InterfaceName,
            i.NetworkName,
            i.NetworkSlug,
            i.Cidr,
            i.IpAddress,
            i.IsPrimary,
            i.IsManagement)
        {
            FabricHostInterfaceName = i.FabricHostInterfaceName,
            FabricContainerInterfaceName = i.FabricContainerInterfaceName
        }).ToArray(), JsonOptions);

    static Dictionary<string, string> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    static List<int> DeserializeIntList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    static string BuildFlag(PenetrationScoreItem item, int gameId, int teamId, int version)
    {
        if (!item.IsDynamic)
            return item.StaticFlag ?? string.Empty;

        var nodeKey = string.IsNullOrWhiteSpace(item.Node?.TopologyKey) ? item.NodeId.ToString() : item.Node.TopologyKey;
        var scoreKey = string.IsNullOrWhiteSpace(item.TopologyKey) ? item.Id.ToString() : item.TopologyKey;
        var token = $"{gameId}:{teamId}:{nodeKey}:{scoreKey}:{version}".ToSHA256String()[..16];
        var template = string.IsNullOrWhiteSpace(item.FlagTemplate) ? "flag{[TEAM_HASH]}" : item.FlagTemplate;
        return template.Replace("[TEAM_HASH]", token, StringComparison.OrdinalIgnoreCase)
            .Replace("[TOKEN]", token, StringComparison.OrdinalIgnoreCase);
    }

    static Dictionary<int, string> BuildFlagMap(PenetrationNode node, int gameId, int teamId, int version)
    {
        return node.ScoreItems
            .Where(i => i.IsDynamic || !string.IsNullOrWhiteSpace(i.StaticFlag))
            .OrderBy(i => i.OrderIndex)
            .ToDictionary(i => i.Id, i => BuildFlag(i, gameId, teamId, version));
    }

    static void InjectFlagEnvironmentVariables(Dictionary<string, string> envVars,
        IEnumerable<PenetrationScoreItem> scoreItems, Dictionary<int, string> flagMap)
    {
        if (flagMap.Count == 0)
            return;

        envVars["GZCTF_FLAG"] = flagMap.Values.First();

        var items = scoreItems.ToDictionary(i => i.Id);
        foreach (var (itemId, flag) in flagMap)
        {
            envVars[$"GZCTF_FLAG_{itemId}"] = flag;
            if (items.TryGetValue(itemId, out var item))
            {
                var key = ToEnvKey(item.Title);
                if (!string.IsNullOrWhiteSpace(key))
                    envVars[$"GZCTF_FLAG_{key}"] = flag;
            }
        }
    }

    static void ResolveRuntimeEnvironmentPlaceholders(Dictionary<string, string> envVars, RuntimePlan plan)
    {
        if (envVars.Count == 0)
            return;

        var runtimeByKey = plan.Nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Node.TopologyKey))
            .ToDictionary(n => n.Node.TopologyKey, StringComparer.Ordinal);

        foreach (var key in envVars.Keys.ToArray())
            envVars[key] = ReplaceRuntimeEnvironmentPlaceholders(envVars[key], runtimeByKey);
    }

    static string ReplaceRuntimeEnvironmentPlaceholders(string value,
        IReadOnlyDictionary<string, RuntimeNodePlan> runtimeByKey)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            (!value.Contains("{{asset:", StringComparison.Ordinal) &&
             !value.Contains("{{node:", StringComparison.Ordinal)))
            return value;

        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var (start, tokenLength) = FindNextAssetPlaceholder(value, index);
            if (start < 0)
            {
                builder.Append(value, index, value.Length - index);
                break;
            }

            builder.Append(value, index, start - index);
            var end = value.IndexOf("}}", start + tokenLength, StringComparison.Ordinal);
            if (end < 0)
            {
                builder.Append(value, start, value.Length - start);
                break;
            }

            var expression = value[(start + tokenLength)..end];
            builder.Append(ResolveNodePlaceholder(expression, runtimeByKey) ?? value[start..(end + 2)]);
            index = end + 2;
        }

        return builder.ToString();
    }

    static (int Start, int TokenLength) FindNextAssetPlaceholder(string value, int index)
    {
        var assetStart = value.IndexOf("{{asset:", index, StringComparison.Ordinal);
        var legacyNodeStart = value.IndexOf("{{node:", index, StringComparison.Ordinal);
        if (assetStart < 0)
            return legacyNodeStart < 0 ? (-1, 0) : (legacyNodeStart, "{{node:".Length);
        if (legacyNodeStart < 0 || assetStart < legacyNodeStart)
            return (assetStart, "{{asset:".Length);
        return (legacyNodeStart, "{{node:".Length);
    }

    static string? ResolveNodePlaceholder(string expression,
        IReadOnlyDictionary<string, RuntimeNodePlan> runtimeByKey)
    {
        var parts = expression.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;

        var nodeKey = parts[0];
        var mode = parts[1].ToLowerInvariant();
        if (!runtimeByKey.TryGetValue(nodeKey, out var nodePlan))
            return null;

        var primary = nodePlan.PrimaryInterface;
        if (primary is null || string.IsNullOrWhiteSpace(primary.IpAddress))
            return null;

        return mode switch
        {
            "host" or "ip" => primary.IpAddress,
            "url" => BuildInternalNodeUrl(primary.IpAddress,
                parts.Length >= 3 && int.TryParse(parts[2], out var explicitPort)
                    ? explicitPort
                    : nodePlan.Node.ExposePort),
            "port" => nodePlan.Node.ExposePort.ToString(),
            _ => null
        };
    }

    static string BuildInternalNodeUrl(string host, int port)
    {
        var scheme = port == 443 ? "https" : "http";
        return $"{scheme}://{host}:{port}";
    }

    static bool TryParseCidr(string cidr, out uint network, out int prefix)
    {
        network = 0;
        prefix = 0;
        var parts = cidr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || !int.TryParse(parts[1], out prefix))
            return false;
        if (prefix is < 0 or > 32)
            return false;
        network = ToUInt(address);
        return true;
    }

    static (ulong Start, ulong End) CidrRange(uint network, int prefix)
    {
        var size = prefix >= 32 ? 1UL : 1UL << (32 - prefix);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var start = (ulong)(network & mask);
        return (start, start + size - 1);
    }

    static bool ContainsCidr(uint outerNetwork, int outerPrefix, uint innerNetwork, int innerPrefix)
    {
        var outer = CidrRange(outerNetwork, outerPrefix);
        var inner = CidrRange(innerNetwork, innerPrefix);
        return inner.Start >= outer.Start && inner.End <= outer.End;
    }

    static bool ContainsAddress(uint network, int prefix, IPAddress address)
    {
        var range = CidrRange(network, prefix);
        var value = (ulong)ToUInt(address);
        return value >= range.Start && value <= range.End;
    }

    static bool CidrRangesOverlap(uint leftNetwork, int leftPrefix, uint rightNetwork, int rightPrefix)
    {
        var left = CidrRange(leftNetwork, leftPrefix);
        var right = CidrRange(rightNetwork, rightPrefix);
        return left.Start <= right.End && right.Start <= left.End;
    }

    static uint UsableDockerHostCapacity(int prefix)
    {
        if (prefix >= 31)
            return 0;

        var size = 1UL << (32 - prefix);
        return size <= 3 ? 0 : (uint)(size - 3);
    }

    static string AllocateSubnet(string baseCidr, int prefix, int index)
    {
        if (!TryParseCidr(baseCidr, out var network, out var basePrefix))
            return $"10.60.{index}.0/{prefix}";
        var subnetSize = 1u << (32 - prefix);
        var offset = subnetSize * (uint)Math.Max(0, index);
        var mask = basePrefix == 0 ? 0u : uint.MaxValue << (32 - basePrefix);
        return $"{FromUInt((network & mask) + offset)}/{prefix}";
    }

    static Dictionary<string, string> BuildNetworkSubnets(PenetrationConfig config, int teamIndex,
        Dictionary<int, string> networkNames)
    {
        Dictionary<string, string> subnets = [];
        var teamSubnet = AllocateSubnet(config.BaseCidr, config.TeamSubnetPrefix, teamIndex);
        TryParseCidr(teamSubnet, out var teamNetwork, out _);
        var sampleTeamSubnet = AllocateSubnet(config.BaseCidr, config.TeamSubnetPrefix, 0);
        TryParseCidr(sampleTeamSubnet, out var sampleTeamNetwork, out _);
        var subnetSize = 1u << (32 - config.NetworkSubnetPrefix);

        foreach (var network in config.Networks.OrderBy(n => n.OrderIndex))
        {
            if (!networkNames.TryGetValue(network.Id, out var networkName))
                continue;

            if (!string.IsNullOrWhiteSpace(network.Cidr) &&
                TryParseCidr(network.Cidr, out var customNetwork, out var customPrefix))
            {
                var offset = customNetwork >= sampleTeamNetwork ? customNetwork - sampleTeamNetwork : 0;
                subnets[networkName] = $"{FromUInt(teamNetwork + offset)}/{customPrefix}";
            }
            else
            {
                subnets[networkName] =
                    $"{FromUInt(teamNetwork + subnetSize * (uint)Math.Max(0, network.OrderIndex))}/{config.NetworkSubnetPrefix}";
            }
        }

        return subnets;
    }

    static uint ToUInt(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return 0;
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    static IPAddress FromUInt(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    static string Slugify(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"net-{Guid.NewGuid():N}"[..12] : slug[..Math.Min(slug.Length, 48)];
    }

    static string ToEnvKey(string value)
    {
        var chars = value.Trim().ToUpperInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return new string(chars).Trim('_');
    }

    static string Clean(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    static string? CleanNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static PenetrationZoneType NormalizeTeamLabZoneType(PenetrationZoneType value) =>
        value == PenetrationZoneType.Public ? PenetrationZoneType.Dmz : value;

    static PenetrationNodeType NormalizeTeamLabNodeType(PenetrationNodeType value) =>
        value == PenetrationNodeType.Entry ? PenetrationNodeType.Web : value;

    static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maxLength);

    static PenetrationNetworkModel DefaultNetworkModel(int id, int orderIndex) => new()
    {
        Id = id,
        TopologyKey = EnsureTopologyKey(null, "network", id),
        Name = "业务接入网段",
        Slug = "service-lan",
        ZoneType = PenetrationZoneType.Dmz,
        TrustLevel = 30,
        IsEntry = false,
        OrderIndex = orderIndex,
        PositionX = 80,
        PositionY = 80,
        Width = 560,
        Height = 390
    };

    static string ResolvePolicyName(PenetrationConfig config, PenetrationPolicyScope kind, int id, int fallbackNodeId)
    {
        if (kind == PenetrationPolicyScope.Network)
            return config.Networks.FirstOrDefault(n => n.Id == id)?.Name ?? $"内网网段 {id}";

        var nodeId = id > 0 ? id : fallbackNodeId;
        return config.Nodes.FirstOrDefault(n => n.Id == nodeId)?.Name ?? $"节点 {nodeId}";
    }

    static bool IsInternalNetwork(PenetrationNetwork network) => true;

    static Dictionary<int, string> BuildNetworkPreviewKeys(PenetrationConfig config) =>
        config.Networks.ToDictionary(n => n.Id, n => $"preview-{n.Id}");

    static string BuildRuntimeNetworkName(PenetrationConfig config, int teamId, PenetrationNetwork network) =>
        $"pentest-g{config.GameId}-t{teamId}-n{network.Id}-{network.Slug}-{config.PublishedVersion}";

    sealed record TopologyModelMaps(
        Dictionary<int, PenetrationNetwork> NetworkMap,
        Dictionary<int, PenetrationNode> NodeMap,
        Dictionary<int, string> ScoreKeyByModelId,
        bool PreserveModelIds);

    sealed record RuntimePlan(
        List<RuntimeNetworkPlan> Networks,
        List<RuntimeNodePlan> Nodes,
        List<RuntimeRoutePlan> Routes);

    sealed record RuntimeNetworkPlan(PenetrationNetwork Network, string NetworkName, string Cidr, bool IsInternal);

    sealed record RuntimeNodePlan(
        PenetrationNode Node,
        string Image,
        List<RuntimeInterfacePlan> Interfaces,
        Dictionary<int, string> FlagMap,
        Dictionary<int, bool> RuntimeNetworks,
        bool IsRouteNode,
        bool RequiresNetworkAdmin)
    {
        public RuntimeInterfacePlan? PrimaryInterface => Interfaces.FirstOrDefault(i => i.IsPrimary) ?? Interfaces.FirstOrDefault();
    }

    sealed record RuntimeRoutePlan(
        PenetrationEdge Edge,
        string Label,
        PenetrationRouteStatus Status,
        PenetrationNode? RouteNode,
        RuntimeInterfacePlan? SourceInterface,
        RuntimeInterfacePlan? TargetInterface,
        RuntimeInterfacePlan? SourceRouteInterface,
        RuntimeInterfacePlan? TargetRouteInterface,
        List<RuntimeInterfacePlan> SourceEndpointInterfaces,
        List<RuntimeInterfacePlan> TargetEndpointInterfaces,
        string CommandSummary,
        string Message)
    {
        public IEnumerable<string> EndpointNodeKeys =>
            SourceEndpointInterfaces.Concat(TargetEndpointInterfaces)
                .Select(i => i.Node.TopologyKey)
                .Where(key => !string.IsNullOrWhiteSpace(key));

        public bool IsExecutable =>
            Status == PenetrationRouteStatus.RoutePlanned &&
            RouteNode is not null &&
            SourceInterface is not null &&
            TargetInterface is not null &&
            SourceRouteInterface is not null &&
            TargetRouteInterface is not null;

        public string? PairKey =>
            SourceInterface is null || TargetInterface is null
                ? null
                : BuildRuntimeRoutePairKey(SourceInterface.Network.Id, TargetInterface.Network.Id);

        public static RuntimeRoutePlan Hint(PenetrationEdge edge, string label, string message) =>
            new(edge, label, PenetrationRouteStatus.HintOnly, null, null, null, null, null, [], [], string.Empty, message);

        public static RuntimeRoutePlan Unsupported(PenetrationEdge edge, string label, string message) =>
            new(edge, label, PenetrationRouteStatus.Unsupported, null, null, null, null, null, [], [], string.Empty, message);
    }

    public record RuntimeInterfaceInfo(
        int InterfaceId,
        int NodeId,
        int NetworkId,
        string InterfaceName,
        string NetworkName,
        string NetworkSlug,
        string Cidr,
        string IpAddress,
        bool IsPrimary,
        bool IsManagement)
    {
        public string? FabricHostInterfaceName { get; set; }
        public string? FabricContainerInterfaceName { get; set; }
    }

    sealed record RuntimeInterfacePlan(
        int InterfaceId,
        PenetrationNode Node,
        PenetrationNetwork Network,
        string InterfaceName,
        string NetworkName,
        string Cidr,
        string IpAddress,
        bool IsPrimary,
        bool IsManagement,
        int OrderIndex)
        : RuntimeInterfaceInfo(InterfaceId, Node.Id, Network.Id, InterfaceName, NetworkName, Network.Slug, Cidr,
            IpAddress, IsPrimary, IsManagement);

    sealed record EffectiveInterface(
        int InterfaceId,
        string TopologyKey,
        PenetrationNode Node,
        PenetrationNetwork Network,
        string Name,
        string? StaticIp,
        bool IsPrimary,
        bool IsManagement,
        int OrderIndex)
    {
        public string Key => $"{Node.Id}:{Network.Id}:{Name}:{OrderIndex}";
    }

    sealed record TeamDeploymentResult(bool Success, bool Cancelled, string Message)
    {
        public static readonly TeamDeploymentResult SuccessResult = new(true, false, "部署成功。");
        public static readonly TeamDeploymentResult CancelledResult = new(false, true, "部署已取消。");

        public static TeamDeploymentResult Failed(string message) => new(false, false, message);
    }
}

public sealed class PenetrationScoreState(int teamId)
{
    public int TeamId { get; } = teamId;
    public int TotalScore { get; private set; }
    public int SolvedCount { get; private set; }
    public DateTimeOffset LastScoreTime { get; private set; } = DateTimeOffset.MinValue;
    public List<PenetrationScoreTimelineEvent> TimelineEvents { get; } = [];

    public void Add(int score, DateTimeOffset time)
    {
        TotalScore += score;
        SolvedCount++;
        LastScoreTime = time > LastScoreTime ? time : LastScoreTime;
        TimelineEvents.Add(new PenetrationScoreTimelineEvent(time, score));
    }
}

public sealed record PenetrationScoreTimelineEvent(DateTimeOffset Time, int Score);
