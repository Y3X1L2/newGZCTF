using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Extensions;
using GZCTF.Hubs;
using GZCTF.Hubs.Clients;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Concurrency;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using DataContainer = GZCTF.Models.Data.Container;

namespace GZCTF.Services;

public class PenetrationService(
    AppDbContext context,
    IContainerManager containerManager,
    IPenetrationFabricManager penetrationFabricManager,
    IServiceProvider serviceProvider,
    PenetrationAttackGraphService penetrationAttackGraphService,
    CacheHelper cacheHelper,
    ISubmissionRepository submissionRepository,
    IGameEventRepository gameEventRepository,
    IHubContext<UserHub, IUserClient> userHub,
    IDistributedLockService lockService,
    DockerImageRegistryService dockerRegistry,
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
            Name = "公网入口区",
            Slug = "public",
            ZoneType = PenetrationZoneType.Public,
            TrustLevel = 10,
            Description = "队伍首先接触的外部入口安全域。",
            IsEntry = true,
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

        var capacity = await CheckFleetCapacity(config.Nodes.Count, deploymentTargets.Select(t => t.Participation.TeamId).ToArray(),
            gameId, token);
        if (!capacity.Success)
        {
            savedConfig.Status = PenetrationDeploymentStatus.Failed;
            savedConfig.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);
            return (false, capacity.Message);
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
                    target.TeamIndex,
                    target.Existing is not null,
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
        int teamIndex, bool rebuild, CancellationToken token)
    {
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var scopedService = scope.ServiceProvider.GetRequiredService<PenetrationService>();
            return await scopedService.DeployTeamByPublishedVersion(gameId, publishedVersion, teamId, teamIndex,
                rebuild, token);
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
            var scopedService = scope.ServiceProvider.GetRequiredService<PenetrationService>();
            await scopedService.CleanupCancelledDeployment(gameId, teamId, publishedVersion);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to run compensation cleanup for cancelled penetration deployment, game {GameId}, team {TeamId}.",
                gameId, teamId);
        }
    }

    async Task CleanupCancelledDeployment(int gameId, int teamId, int publishedVersion)
    {
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var environment = await LoadTeamEnvironment(gameId, teamId, cleanupCts.Token);
        if (environment is null || environment.PublishedVersion != publishedVersion)
            return;

        if (environment.Status is PenetrationRuntimeStatus.Running or PenetrationRuntimeStatus.Stopped)
            return;

        environment.LastError = "部署任务已取消，系统正在清理该队伍的半创建资源。";
        AddDeploymentEvent(environment, "cancel", PenetrationDeploymentEventLevel.Warning,
            "部署任务已取消，开始补偿清理已创建的容器和网络。");
        await context.SaveChangesAsync(cleanupCts.Token);

        var cleanup = await DestroyEnvironment(environment, cleanupCts.Token);
        if (!cleanup.Success)
            logger.LogWarning(
                "Cancelled penetration deployment cleanup left residual resources for game {GameId}, team {TeamId}: {Message}",
                gameId, teamId, cleanup.Message);
    }

    async Task<TeamDeploymentResult> DeployTeamByPublishedVersion(int gameId, int publishedVersion, int teamId,
        int teamIndex, bool rebuild, CancellationToken token)
    {
        var config = await LoadPublishedConfig(gameId, publishedVersion, token);
        if (config is null)
            return TeamDeploymentResult.Failed($"发布版本 v{publishedVersion} 快照不存在，无法部署队伍环境。");

        try
        {
            var existing = await LoadTeamEnvironment(gameId, teamId, token);
            if (existing is not null)
            {
                using var destroyCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var destroyed = await DestroyEnvironment(existing, destroyCts.Token);
                if (!destroyed.Success)
                    return TeamDeploymentResult.Failed(destroyed.Message);
            }

            token.ThrowIfCancellationRequested();
            var deploy = await DeployTeam(config, teamId, teamIndex, rebuild, existing, token);
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

        if (environment is not null)
        {
            var destroyed = await DestroyEnvironment(environment, token);
            if (!destroyed.Success)
                return (false, $"旧环境清理失败，已进入待清理状态：{destroyed.Message}");
        }

        var deploy = await DeployTeam(config, teamId, index, true, environment, token);
        if (deploy.Success && environment is not null && !byAdmin)
        {
            environment.ResetCount++;
            await context.PenetrationResetRecords.AddAsync(new PenetrationResetRecord
            {
                EnvironmentId = environment.Id,
                UserId = userId,
                ByAdmin = false
            }, token);
            await context.SaveChangesAsync(token);
        }

        return deploy;
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
            var environment = await LoadTeamEnvironment(gameId, teamId, token);
            if (environment is null)
                return (true, "该队伍没有渗透环境需要清理。");

            var result = await DestroyEnvironment(environment, token);
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
        var environments = await context.PenetrationTeamEnvironments
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.Container)
            .Where(e => e.GameId == gameId)
            .ToArrayAsync(token);

        var failed = 0;
        foreach (var environment in environments)
        {
            var result = await DestroyEnvironment(environment, token);
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
        var now = DateTimeOffset.UtcNow;
        var environments = await context.PenetrationTeamEnvironments
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.Container)
            .Include(e => e.DeploymentEvents)
            .Where(e =>
                (e.Status == PenetrationRuntimeStatus.CleanupPending ||
                 e.Status == PenetrationRuntimeStatus.Orphaned) &&
                (e.NextCleanupAt == null || e.NextCleanupAt <= now))
            .OrderBy(e => e.UpdatedAt)
            .Take(20)
            .ToArrayAsync(token);

        var cleaned = 0;
        foreach (var environment in environments)
        {
            var environmentId = environment.Id;
            var gameId = environment.GameId;
            var retryCountBeforeAttempt = environment.CleanupRetryCount;
            try
            {
                using var deployLock = await lockService.AcquireAsync(BuildDeployLockKey(gameId),
                    TimeSpan.FromSeconds(1));
                context.ChangeTracker.Clear();

                var currentEnvironment = await LoadTeamEnvironmentById(environmentId, token);
                if (currentEnvironment is null ||
                    currentEnvironment.Status is not (PenetrationRuntimeStatus.CleanupPending or
                        PenetrationRuntimeStatus.Orphaned) ||
                    (currentEnvironment.NextCleanupAt is { } nextCleanupAt && nextCleanupAt > DateTimeOffset.UtcNow))
                    continue;

                retryCountBeforeAttempt = currentEnvironment.CleanupRetryCount;
                var result = await DestroyEnvironment(currentEnvironment, token);
                if (result.Success)
                    cleaned++;
            }
            catch (TimeoutException)
            {
                continue;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex, "Concurrent penetration cleanup update for environment {EnvironmentId}",
                    environmentId);
                context.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to run penetration cleanup for environment {EnvironmentId}",
                    environmentId);
                context.ChangeTracker.Clear();
                var currentEnvironment = await LoadTeamEnvironmentById(environmentId, token);
                if (currentEnvironment is not null &&
                    currentEnvironment.Status is PenetrationRuntimeStatus.CleanupPending or
                        PenetrationRuntimeStatus.Orphaned &&
                    currentEnvironment.CleanupRetryCount == retryCountBeforeAttempt)
                    MarkEnvironmentCleanupPending(currentEnvironment, [$"补偿清理任务失败：{ex.Message}"]);
                await context.SaveChangesAsync(token);
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

        var attackGraph = penetrationAttackGraphService.GetOrBuild(config, environment, solved);
        var attackNodeByKey = attackGraph.Nodes
            .Where(n => n.Status != PenetrationFogState.Hidden)
            .ToDictionary(n => n.TopologyKey, StringComparer.Ordinal);
        var accessibleNodeKeys = attackGraph.Nodes
            .Where(n => n.Status is PenetrationFogState.Accessible or PenetrationFogState.Completed)
            .Select(n => n.TopologyKey)
            .ToHashSet(StringComparer.Ordinal);
        var runtimeByNode = environment.RuntimeNodes
            .Where(r => !string.IsNullOrWhiteSpace(r.TopologyNodeKey))
            .ToDictionary(r => r.TopologyNodeKey, StringComparer.Ordinal);
        var runtimeInterfaces = environment.RuntimeNodes
            .Where(r => !string.IsNullOrWhiteSpace(r.TopologyNodeKey))
            .ToDictionary(r => r.TopologyNodeKey, r => ReadRuntimeInterfaces(r), StringComparer.Ordinal);
        return new PenetrationWorkspaceModel
        {
            GameId = gameId,
            TeamId = teamId,
            TeamName = environment.Team.Name,
            TargetHost = environment.Node?.HostAddress ?? string.Empty,
            Status = environment.Status,
            ResetCount = environment.ResetCount,
            MaxResetCount = config.MaxResetCount,
            EntryPoints = environment.RuntimeNodes
                .Select(r => new
                {
                    Runtime = r,
                    Node = config.Nodes.FirstOrDefault(n => n.TopologyKey == r.TopologyNodeKey)
                })
                .Where(x => x.Node is not null && (x.Node.IsEntry || x.Node.PublishPort))
                .Where(x => x.Runtime.Container?.PublicPort is > 0)
                .Select(x => new PenetrationEntryPointModel
                {
                    NodeId = x.Node!.Id,
                    NodeName = string.IsNullOrWhiteSpace(x.Node.PlayerAlias) ? x.Node.Name : x.Node.PlayerAlias!,
                    Host = x.Runtime.Container?.PublicIP ?? x.Runtime.Container?.IP ?? x.Runtime.IpAddress,
                    Port = x.Runtime.Container?.PublicPort ?? 0,
                    ExposePort = x.Node.ExposePort
                }).ToList(),
            Networks = config.Networks
                .Where(n => n.IsEntry ||
                            config.Nodes.Any(node =>
                                node.NetworkId == n.Id && attackNodeByKey.ContainsKey(node.TopologyKey)))
                .OrderBy(n => n.OrderIndex)
                .Select(n => new PenetrationWorkspaceNetworkModel
                {
                    Id = n.Id,
                    Name = n.Name,
                    Slug = n.Slug,
                    ZoneType = n.ZoneType,
                    TrustLevel = n.TrustLevel,
                    OrderIndex = n.OrderIndex,
                    IsEntry = n.IsEntry,
                    Cidr = n.IsEntry ? environment.NetworkPrefix : null,
                    PositionX = n.PositionX,
                    PositionY = n.PositionY,
                    Width = n.Width,
                    Height = n.Height
                }).ToList(),
            Nodes = config.Nodes
                .Where(n => attackNodeByKey.ContainsKey(n.TopologyKey))
                .OrderBy(n => n.OrderIndex)
                .Select(n =>
            {
                var graphNode = attackNodeByKey[n.TopologyKey];
                var canOperate = accessibleNodeKeys.Contains(n.TopologyKey);
                runtimeByNode.TryGetValue(n.TopologyKey, out var runtime);
                runtimeInterfaces.TryGetValue(n.TopologyKey, out var runtimeInterfaceList);
                return new PenetrationWorkspaceNodeModel
                {
                    Id = n.Id,
                    NetworkId = n.NetworkId,
                    TopologyKey = n.TopologyKey,
                    Name = graphNode.DisplayName,
                    Description = graphNode.Description,
                    NodeType = n.NodeType,
                    IpAddress = n.IsEntry ? runtime?.IpAddress : null,
                    IsEntry = n.IsEntry,
                    FogState = graphNode.Status,
                    RuntimeStatus = runtime?.Status ?? PenetrationRuntimeStatus.Pending,
                    PositionX = n.PositionX,
                    PositionY = n.PositionY,
                    Interfaces = n.IsEntry ? BuildWorkspaceInterfaces(n, runtimeInterfaceList ?? []) : [],
                    ScoreItems = canOperate
                        ? n.ScoreItems.Where(i => i.IsVisible).OrderBy(i => i.OrderIndex).Select(i =>
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
                        : []
                };
            }).ToList(),
            Policies = config.Edges.Where(e => e.SourceNodeId > 0 && e.TargetNodeId > 0)
                .Where(e => e.PolicyAction == PenetrationPolicyAction.Allow && e.IsRouteHint)
                .Where(e =>
                {
                    var source = config.Nodes.FirstOrDefault(n => n.Id == e.SourceNodeId);
                    var target = config.Nodes.FirstOrDefault(n => n.Id == e.TargetNodeId);
                    return source is not null &&
                           target is not null &&
                           attackNodeByKey.ContainsKey(source.TopologyKey) &&
                           attackNodeByKey.ContainsKey(target.TopologyKey);
                })
                .OrderBy(e => e.Id)
                .Select(e => new PenetrationWorkspacePolicyModel
                {
                    Id = e.Id,
                    Label = string.IsNullOrWhiteSpace(e.Label) ? "访问路径" : e.Label,
                    SourceNodeId = e.SourceNodeId,
                    TargetNodeId = e.TargetNodeId,
                    Protocol = e.Protocol,
                    PortRange = e.PortRange
                }).ToList(),
            AttackGraph = attackGraph
        };
    }

    public async Task<PenetrationAttackGraphModel?> GetAttackGraph(int gameId, int teamId,
        CancellationToken token = default)
    {
        var environment = await LoadTeamEnvironment(gameId, teamId, token);
        if (environment is null)
            return null;

        var config = await LoadPublishedConfig(gameId, environment.PublishedVersion, token);
        if (config is null)
            return null;

        var solved = await GetSolvedScoreItemKeys(gameId, teamId, environment.PublishedVersion, token);
        return penetrationAttackGraphService.GetOrBuild(config, environment, solved);
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

        var solvedScoreItemKeys = await GetSolvedScoreItemKeys(gameId, teamId, environment.PublishedVersion, token);

        var attackGraphBefore = penetrationAttackGraphService.Build(config, environment, solvedScoreItemKeys);
        var node = config.Nodes.FirstOrDefault(n => n.ScoreItems.Any(i => i.TopologyKey == item.TopologyKey));
        if (node is null || !IsAttackNodeOperable(attackGraphBefore, node.TopologyKey))
            return new PenetrationSubmitResultModel { Accepted = false, Message = "该任务尚未解锁，请先完成前置攻击路径。" };

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

        var attackGraphChanged = false;
        var unlockedNodeCount = 0;
        if (accepted)
        {
            var nextSolvedScoreItemKeys = solvedScoreItemKeys.ToHashSet(StringComparer.Ordinal);
            nextSolvedScoreItemKeys.Add(item.TopologyKey);
            var attackGraphAfter = penetrationAttackGraphService.Build(config, environment, nextSolvedScoreItemKeys);
            var visibleBefore = attackGraphBefore.Nodes
                .Where(n => n.Status != PenetrationFogState.Hidden)
                .Select(n => n.TopologyKey)
                .ToHashSet(StringComparer.Ordinal);
            unlockedNodeCount = attackGraphAfter.Nodes.Count(n =>
                n.Status != PenetrationFogState.Hidden && !visibleBefore.Contains(n.TopologyKey));
            attackGraphChanged = HasAttackGraphSummaryChanged(attackGraphBefore, attackGraphAfter);

            await PublishAttackGraphUpdate(gameId, teamId, environment.PublishedVersion, attackGraphAfter,
                unlockedNodeCount, attackGraphChanged, token);
        }

        return new PenetrationSubmitResultModel
        {
            Accepted = accepted,
            Score = submission.Score,
            Message = accepted ? "Flag 正确。" : "Flag 错误。",
            AttackGraphChanged = attackGraphChanged,
            UnlockedNodeCount = unlockedNodeCount
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
                    AdminAccessUrl = r.AdminAccessUrl,
                    PublicPort = r.PublicPort,
                    Status = r.Status,
                    CreatedAt = r.CreatedAt,
                    ContainerGuid = r.ContainerId,
                    ContainerId = r.Container == null ? null : r.Container.ContainerId,
                    ContainerStatus = r.Container?.Status,
                    Image = r.Container?.Image,
                    PublicHost = r.Container?.PublicIP,
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
            var host = r.Container?.PublicIP ?? r.Container?.IP;
            var url = r.AdminAccessUrl;
            if (string.IsNullOrWhiteSpace(url) && r.PublicPort is > 0 && !string.IsNullOrWhiteSpace(host))
                url = $"http://{host}:{r.PublicPort}";

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
                PublicPort = r.PublicPort,
                Url = url,
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

    static bool IsAttackNodeOperable(PenetrationAttackGraphModel graph, string topologyKey) =>
        graph.Nodes.Any(n => n.TopologyKey == topologyKey &&
                             n.Status is PenetrationFogState.Accessible or PenetrationFogState.Completed);

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

    static bool HasAttackGraphSummaryChanged(PenetrationAttackGraphModel before, PenetrationAttackGraphModel after) =>
        before.VisibleNodeCount != after.VisibleNodeCount ||
        before.CompletedNodeCount != after.CompletedNodeCount ||
        before.SolvedScoreItemCount != after.SolvedScoreItemCount ||
        before.Edges.Count != after.Edges.Count ||
        before.Nodes.Any(beforeNode =>
            after.Nodes.FirstOrDefault(afterNode => afterNode.TopologyKey == beforeNode.TopologyKey) is { } afterNode &&
            (beforeNode.Status != afterNode.Status ||
             beforeNode.ScoreSummary.Solved != afterNode.ScoreSummary.Solved ||
             beforeNode.ScoreSummary.CheckpointSolved != afterNode.ScoreSummary.CheckpointSolved));

    async Task PublishAttackGraphUpdate(int gameId, int teamId, int publishedVersion,
        PenetrationAttackGraphModel attackGraph, int unlockedNodeCount, bool graphChanged, CancellationToken token)
    {
        try
        {
            await userHub.Clients.Group(UserHub.PenetrationTeamGroupName(gameId, teamId))
                .ReceivedPenetrationAttackGraphUpdate(new PenetrationAttackGraphUpdateModel
                {
                    GameId = gameId,
                    TeamId = teamId,
                    PublishedVersion = publishedVersion,
                    Accepted = true,
                    GraphChanged = graphChanged,
                    CompletedNodeCount = attackGraph.CompletedNodeCount,
                    VisibleNodeCount = attackGraph.VisibleNodeCount,
                    UnlockedNodeCount = unlockedNodeCount,
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
                "Failed to push penetration attack graph update for game {GameId}, team {TeamId}.",
                gameId, teamId);
        }
    }

    async Task PublishWorkspaceRefresh(int gameId, int teamId, int publishedVersion, CancellationToken token)
    {
        try
        {
            await userHub.Clients.Group(UserHub.PenetrationTeamGroupName(gameId, teamId))
                .ReceivedPenetrationAttackGraphUpdate(new PenetrationAttackGraphUpdateModel
                {
                    GameId = gameId,
                    TeamId = teamId,
                    PublishedVersion = publishedVersion,
                    Accepted = false,
                    GraphChanged = true,
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

    async Task<(bool Success, string Message)> DeployTeam(PenetrationConfig config, int teamId, int teamIndex,
        bool rebuild, PenetrationTeamEnvironment? existing, CancellationToken token)
    {
        var allocation = await AllocateTeamWorkerNode(config, teamId, teamIndex, existing, token);
        if (!allocation.Success)
            return (false, allocation.Message);

        var environment = allocation.Environment!;
        var worker = allocation.Worker!;
        var reservedSlotsNotBackedByContainer = config.Nodes.Count;
        var pendingContainers = new List<DataContainer>();
        try
        {
            RuntimePlan plan;
            try
            {
                plan = await BuildRuntimePlan(config, teamIndex, teamId, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Failed to build penetration runtime plan for game {GameId}, team {TeamId}.",
                    config.GameId, teamId);
                await ReleaseReservedDockerCapacity(worker, reservedSlotsNotBackedByContainer, token);
                reservedSlotsNotBackedByContainer = 0;
                environment.Status = PenetrationRuntimeStatus.Failed;
                environment.LastError = $"部署计划生成失败：{ex.Message}";
                environment.UpdatedAt = DateTimeOffset.UtcNow;
                AddDeploymentEvent(environment, "plan", PenetrationDeploymentEventLevel.Error,
                    $"部署计划生成失败：{ex.Message}");
                await SavePenetrationStateAsync("mark penetration plan generation failure", token);
                return (false, "部署计划生成失败，请检查镜像模板、网段和节点配置。");
            }

            foreach (var network in plan.Networks)
                AddDeploymentEvent(environment, "network", PenetrationDeploymentEventLevel.Success,
                    $"网络 {network.NetworkName} 计划就绪：{network.Cidr}{(network.IsInternal ? "，fabric 二层隔离" : IsInternalNetwork(network.Network) ? "，将由 RuntimeRoute 显式路由控制可达性" : "，入口可达")}。");

            foreach (var route in plan.Routes)
            {
                var level = route.Status == PenetrationRouteStatus.RoutePlanned
                    ? PenetrationDeploymentEventLevel.Info
                    : route.Status == PenetrationRouteStatus.HintOnly
                        ? PenetrationDeploymentEventLevel.Info
                        : PenetrationDeploymentEventLevel.Warning;
                AddDeploymentEvent(environment, "route-plan", level,
                    $"{route.Label}：{route.Message}", route.RouteNode?.Name,
                    route.CommandSummary);
            }

            var success = true;
            var failureMessages = new List<string>();
            environment.Status = PenetrationRuntimeStatus.CreatingContainers;
            environment.UpdatedAt = DateTimeOffset.UtcNow;
            AddDeploymentEvent(environment, "container", PenetrationDeploymentEventLevel.Info,
                $"开始创建 {plan.Nodes.Count} 个资产容器。");
            await SavePenetrationStateAsync("record penetration container creation start", token);

            try
            {
                foreach (var nodePlan in plan.Nodes)
                {
                    token.ThrowIfCancellationRequested();
                    AddDeploymentEvent(environment, "container", PenetrationDeploymentEventLevel.Info,
                        $"开始创建资产“{nodePlan.Node.Name}”。", nodePlan.Node.Name);
                    var containerConfig = BuildContainerConfig(nodePlan, plan, teamId, worker.Id);
                    containerConfig.FleetCapacityReserved = true;
                    var container = await containerManager.CreateContainerAsync(containerConfig, CancellationToken.None);

                    if (container is null)
                    {
                        await ReleaseReservedDockerCapacity(worker, 1, CancellationToken.None);
                        reservedSlotsNotBackedByContainer--;
                        success = false;
                        failureMessages.Add($"节点“{nodePlan.Node.Name}”容器创建失败。");
                        AddDeploymentEvent(environment, "container", PenetrationDeploymentEventLevel.Error,
                            "容器创建失败，请检查镜像是否可被目标节点拉取、节点容量、网络和 Agent 状态。", nodePlan.Node.Name);
                        await context.PenetrationRuntimeNodes.AddAsync(new PenetrationRuntimeNode
                        {
                            EnvironmentId = environment.Id,
                            TopologyNodeId = nodePlan.Node.Id,
                            TopologyNodeKey = nodePlan.Node.TopologyKey,
                            NetworkName = nodePlan.PrimaryInterface?.NetworkName ?? string.Empty,
                            IpAddress = nodePlan.PrimaryInterface?.IpAddress ?? string.Empty,
                            InterfaceSummary = SerializeRuntimeInterfaces(nodePlan.Interfaces),
                            Status = PenetrationRuntimeStatus.Failed
                        }, CancellationToken.None);
                        await SavePenetrationStateAsync("record penetration container creation failure",
                            CancellationToken.None);
                        continue;
                    }

                    reservedSlotsNotBackedByContainer--;
                    if (container.Id == Guid.Empty)
                        container.Id = Guid.CreateVersion7();
                    container.NodeId = worker.Id;
                    pendingContainers.Add(container);
                    await context.Containers.AddAsync(container, CancellationToken.None);
                    await SavePenetrationStateAsync("record created penetration container",
                        CancellationToken.None);

                    var publicHost = container.PublicIP ?? container.IP;
                    var adminUrl = nodePlan.Node.PublishPort || nodePlan.Node.IsEntry
                        ? BuildAdminUrl(publicHost, container.PublicPort ?? 0, nodePlan.Node.ExposePort)
                        : null;

                    var fabric = await AttachRuntimeFabricInterfaces(environment, nodePlan, container, token);
                    if (!fabric.Success)
                    {
                        success = false;
                        failureMessages.Add($"节点“{nodePlan.Node.Name}”fabric 网卡配置失败：{fabric.Message}");
                        AddDeploymentEvent(environment, "fabric", PenetrationDeploymentEventLevel.Error,
                            fabric.Message, nodePlan.Node.Name);
                    }

                    var runtimeStatus = container.Status == ContainerStatus.Running && fabric.Success
                        ? PenetrationRuntimeStatus.Running
                        : PenetrationRuntimeStatus.Failed;
                    await context.PenetrationRuntimeNodes.AddAsync(new PenetrationRuntimeNode
                    {
                        EnvironmentId = environment.Id,
                        TopologyNodeId = nodePlan.Node.Id,
                        TopologyNodeKey = nodePlan.Node.TopologyKey,
                        ContainerId = container.Id,
                        NetworkName = nodePlan.PrimaryInterface?.NetworkName ?? string.Empty,
                        IpAddress = nodePlan.PrimaryInterface?.IpAddress ?? container.IP,
                        InterfaceSummary = SerializeRuntimeInterfaces(nodePlan.Interfaces),
                        PublicPort = container.PublicPort,
                        AdminAccessUrl = adminUrl,
                        Status = runtimeStatus
                    }, CancellationToken.None);
                    await SavePenetrationStateAsync("record penetration runtime node",
                        CancellationToken.None);
                    pendingContainers.Remove(container);

                    if (container.Status != ContainerStatus.Running)
                    {
                        success = false;
                        failureMessages.Add($"节点“{nodePlan.Node.Name}”未进入运行状态。");
                        AddDeploymentEvent(environment, "health", PenetrationDeploymentEventLevel.Error,
                            $"容器状态为 {container.Status}，未通过基础运行状态检查。", nodePlan.Node.Name);
                    }
                    else
                    {
                        var health = await ProbeRuntimeNode(nodePlan, container, token);
                        if (!health.Success)
                        {
                            success = false;
                            failureMessages.Add($"节点“{nodePlan.Node.Name}”健康检查失败：{health.Message}");
                            AddDeploymentEvent(environment, "health", PenetrationDeploymentEventLevel.Error,
                                health.Message, nodePlan.Node.Name);
                        }
                        else
                        {
                            AddDeploymentEvent(environment, "health", PenetrationDeploymentEventLevel.Success,
                                health.Message, nodePlan.Node.Name);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Unexpected penetration container creation failure for game {GameId}, team {TeamId}.",
                    config.GameId, teamId);
                if (reservedSlotsNotBackedByContainer > 0)
                {
                    await ReleaseReservedDockerCapacity(worker, reservedSlotsNotBackedByContainer,
                        CancellationToken.None);
                    reservedSlotsNotBackedByContainer = 0;
                }

                success = false;
                failureMessages.Add($"容器创建阶段异常：{ex.Message}");
                AddDeploymentEvent(environment, "container", PenetrationDeploymentEventLevel.Error,
                    $"容器创建阶段异常：{ex.Message}");
                await CleanupPendingContainers(environment, pendingContainers, CancellationToken.None);
            }

            if (success)
            {
                var routeResult = await ApplyRuntimeRoutes(environment, plan, token);
                if (!routeResult.Success)
                {
                    success = false;
                    failureMessages.Add(routeResult.Message);
                }
            }

            if (!success)
            {
                environment.LastError = failureMessages.Count == 0
                    ? "部分节点部署失败。"
                    : string.Join('\n', failureMessages);
                await SavePenetrationStateAsync("record failed penetration deployment before cleanup", token);
                var cleanup = await DestroyEnvironment(environment, CancellationToken.None);
                if (cleanup.Success)
                    environment.Status = PenetrationRuntimeStatus.Failed;
                environment.LastError = cleanup.Success
                    ? $"{environment.LastError}\n已清理残留资源。"
                    : $"{environment.LastError}\n清理残留资源失败：{cleanup.Message}";
                if (!cleanup.Success && environment.Status is PenetrationRuntimeStatus.Stopped or PenetrationRuntimeStatus.Running)
                    environment.Status = PenetrationRuntimeStatus.CleanupPending;
                environment.UpdatedAt = DateTimeOffset.UtcNow;
                AddDeploymentEvent(environment, "cleanup", cleanup.Success
                        ? PenetrationDeploymentEventLevel.Warning
                        : PenetrationDeploymentEventLevel.Error,
                    cleanup.Success ? "部署失败后的残留资源已清理。" : $"部署失败且残留资源清理未完成：{cleanup.Message}");
                await SavePenetrationStateAsync("record failed penetration deployment cleanup result", token);
                return (false, cleanup.Success
                    ? "队伍环境部署失败，已清理残留资源。"
                    : "队伍环境部署失败，残留资源已进入待清理状态。");
            }

            environment.Status = PenetrationRuntimeStatus.Running;
            environment.LastError = null;
            environment.UpdatedAt = DateTimeOffset.UtcNow;
            AddDeploymentEvent(environment, "complete", PenetrationDeploymentEventLevel.Success,
                $"队伍环境部署完成，发布版本 v{config.PublishedVersion} 已运行。");
            await SavePenetrationStateAsync("complete penetration deployment", token);
            await PublishWorkspaceRefresh(config.GameId, teamId, config.PublishedVersion, token);
            return (true, rebuild ? "渗透环境已重建。" : "渗透环境已部署。");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (reservedSlotsNotBackedByContainer > 0)
            {
                await ReleaseReservedDockerCapacity(worker, reservedSlotsNotBackedByContainer,
                    CancellationToken.None);
            }

            throw;
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
                ZoneType = networkModel.ZoneType,
                TrustLevel = Math.Clamp(networkModel.TrustLevel, 0, 100),
                Description = CleanNullable(networkModel.Description),
                DefaultPolicy = networkModel.DefaultPolicy,
                IsEntry = networkModel.IsEntry,
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
                NodeType = nodeModel.NodeType,
                ImageTemplateId = nodeModel.ImageTemplateId,
                ImageName = CleanNullable(nodeModel.ImageName),
                CpuCount = Math.Clamp(nodeModel.CpuCount, 1, 128),
                MemoryLimit = Math.Clamp(nodeModel.MemoryLimit, 64, 262144),
                StorageLimit = Math.Clamp(nodeModel.StorageLimit, 64, 1048576),
                ExposePort = Math.Clamp(nodeModel.ExposePort, 1, 65535),
                IsEntry = nodeModel.IsEntry,
                PublishPort = nodeModel.PublishPort || nodeModel.IsEntry,
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
            throw new InvalidOperationException($"安全域“{blockedDeletedNetwork.Name}”包含运行中的节点，请先停止并清理环境后再删除。");

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
            network.ZoneType = networkModel.ZoneType;
            network.TrustLevel = Math.Clamp(networkModel.TrustLevel, 0, 100);
            network.Description = CleanNullable(networkModel.Description);
            network.DefaultPolicy = networkModel.DefaultPolicy;
            network.IsEntry = networkModel.IsEntry;
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
            node.NodeType = nodeModel.NodeType;
            node.ImageTemplateId = nodeModel.ImageTemplateId;
            node.ImageName = CleanNullable(nodeModel.ImageName);
            node.CpuCount = Math.Clamp(nodeModel.CpuCount, 1, 128);
            node.MemoryLimit = Math.Clamp(nodeModel.MemoryLimit, 64, 262144);
            node.StorageLimit = Math.Clamp(nodeModel.StorageLimit, 64, 1048576);
            node.ExposePort = Math.Clamp(nodeModel.ExposePort, 1, 65535);
            node.IsEntry = nodeModel.IsEntry;
            node.PublishPort = nodeModel.PublishPort || nodeModel.IsEntry;
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
            var sourceModelId = edgeModel.SourceId > 0 ? edgeModel.SourceId : edgeModel.SourceNodeId;
            var targetModelId = edgeModel.TargetId > 0 ? edgeModel.TargetId : edgeModel.TargetNodeId;
            var sourceNode = edgeModel.SourceKind == PenetrationPolicyScope.Node
                ? maps.NodeMap.GetValueOrDefault(sourceModelId)
                : null;
            var targetNode = edgeModel.TargetKind == PenetrationPolicyScope.Node
                ? maps.NodeMap.GetValueOrDefault(targetModelId)
                : null;

            var sourceId = edgeModel.SourceKind == PenetrationPolicyScope.Network
                ? maps.NetworkMap.GetValueOrDefault(sourceModelId)?.Id ?? 0
                : sourceNode?.Id ?? 0;
            var targetId = edgeModel.TargetKind == PenetrationPolicyScope.Network
                ? maps.NetworkMap.GetValueOrDefault(targetModelId)?.Id ?? 0
                : targetNode?.Id ?? 0;

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
            edge.PolicyAction = edgeModel.PolicyAction;
            edge.IsRouteHint = edgeModel.IsRouteHint;
            edge.EnforcementMode = edgeModel.EnforcementMode;
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

    async Task<(bool Success, string Message)> DestroyEnvironment(PenetrationTeamEnvironment environment,
        CancellationToken token)
    {
        var networkNames = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        environment.Status = PenetrationRuntimeStatus.CleanupPending;
        environment.UpdatedAt = DateTimeOffset.UtcNow;
        environment.LastCleanupAttemptAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);

        foreach (var runtime in environment.RuntimeNodes)
        {
            if (!string.IsNullOrWhiteSpace(runtime.NetworkName))
                networkNames.Add(runtime.NetworkName);

            foreach (var item in ReadRuntimeInterfaces(runtime))
                if (!string.IsNullOrWhiteSpace(item.NetworkName))
                    networkNames.Add(item.NetworkName);

            if (runtime.Container is not null)
            {
                try
                {
                    AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Info,
                        $"开始销毁容器 {ShortContainerId(runtime.Container.ContainerId)}。", runtime.TopologyNode?.Name);
                    await containerManager.DestroyContainerAsync(runtime.Container, token);
                    if (runtime.Container.Status != ContainerStatus.Destroyed)
                    {
                        runtime.Status = PenetrationRuntimeStatus.Orphaned;
                        errors.Add($"容器 {runtime.Container.ContainerId} 销毁后状态仍为 {runtime.Container.Status}。");
                        AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Error,
                            $"容器销毁未确认，当前状态：{runtime.Container.Status}。", runtime.TopologyNode?.Name,
                            runtime.Container.ContainerId);
                    }
                    else
                    {
                        context.Containers.Remove(runtime.Container);
                        runtime.ContainerId = null;
                        runtime.Status = PenetrationRuntimeStatus.Stopped;
                        AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Success,
                            "容器已销毁。", runtime.TopologyNode?.Name);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to destroy penetration container {ContainerId}", runtime.ContainerId);
                    runtime.Status = PenetrationRuntimeStatus.Orphaned;
                    errors.Add($"容器 {runtime.Container.ContainerId} 销毁失败：{ex.Message}");
                    AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Error,
                        $"容器销毁失败：{ex.Message}", runtime.TopologyNode?.Name,
                        runtime.Container.ContainerId);
                }
            }
            else
            {
                runtime.ContainerId = null;
                runtime.Status = PenetrationRuntimeStatus.Stopped;
            }
        }

        foreach (var networkName in networkNames)
        {
            try
            {
                AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Info,
                    $"开始清理网络 {networkName}。");
                await RemoveRuntimeNetwork(environment.NodeId, networkName, token);
                AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Success,
                    $"网络 {networkName} 已清理。");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to remove penetration network {NetworkName}", networkName);
                errors.Add($"网络 {networkName} 清理失败：{ex.Message}");
                AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Error,
                    $"网络 {networkName} 清理失败：{ex.Message}", detail: networkName);
            }
        }

        if (errors.Count > 0)
        {
            MarkEnvironmentCleanupPending(environment, errors);
            await context.SaveChangesAsync(token);
            return (false, string.Join('\n', errors));
        }

        context.PenetrationRuntimeRoutes.RemoveRange(environment.RuntimeRoutes);
        context.PenetrationRuntimeNodes.RemoveRange(environment.RuntimeNodes);
        environment.Status = PenetrationRuntimeStatus.Stopped;
        environment.UpdatedAt = DateTimeOffset.UtcNow;
        environment.LastError = null;
        environment.CleanupRetryCount = 0;
        environment.NextCleanupAt = null;
        AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Success, "环境资源已全部清理。");
        await context.SaveChangesAsync(token);
        await PublishWorkspaceRefresh(environment.GameId, environment.TeamId, environment.PublishedVersion, token);
        return (true, "环境资源已清理。");
    }

    async Task CleanupPendingContainers(PenetrationTeamEnvironment environment, List<DataContainer> containers,
        CancellationToken token)
    {
        foreach (var container in containers.ToArray())
        {
            try
            {
                AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Warning,
                    $"开始清理未完成登记的容器 {ShortContainerId(container.ContainerId)}。", detail: container.ContainerId);
                await containerManager.DestroyContainerAsync(container, token);
                if (container.Status == ContainerStatus.Destroyed)
                {
                    context.Containers.Remove(container);
                    containers.Remove(container);
                    AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Success,
                        "未完成登记的容器已清理。", detail: container.ContainerId);
                }
            }
            catch (Exception cleanupEx)
            {
                logger.LogWarning(cleanupEx,
                    "Failed to cleanup pending penetration container {ContainerId} for environment {EnvironmentId}.",
                    container.ContainerId, environment.Id);
                AddDeploymentEvent(environment, "cleanup", PenetrationDeploymentEventLevel.Error,
                    $"未完成登记的容器清理失败：{cleanupEx.Message}", detail: container.ContainerId);
            }
        }

        await context.SaveChangesAsync(token);
    }

    async Task RemoveRuntimeNetwork(Guid? nodeId, string networkName, CancellationToken token)
    {
        var fabric = await penetrationFabricManager.RemoveNetworkAsync(networkName, token);
        if (fabric is { IsSupported: true, Succeeded: false })
            throw new InvalidOperationException(NormalizeFabricError(fabric.Message,
                $"fabric 网络 {networkName} 清理失败。"));
        if (fabric.IsSupported)
            return;

        if (nodeId is { } workerId)
        {
            var worker = await context.WorkerNodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == workerId, token);
            if (worker is { IsLocal: false })
            {
                var agentClient = serviceProvider.GetService<AgentClient>();
                if (agentClient is not null)
                {
                    await agentClient.RemoveNetworkAsync(workerId, networkName, token);
                    return;
                }
            }
        }

        var orchestrator = serviceProvider.GetService<ContainerOrchestrator>();
        if (orchestrator is not null)
            await orchestrator.RemoveNetwork(networkName);
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
            result.Errors.Add("安全域网段前缀不能小于队伍网段前缀。");

        if (config.Networks.Count == 0)
            result.Errors.Add("至少需要一个安全域。");
        else
        {
            if (config.Networks.Count(n => n.IsEntry) > 1)
                result.Errors.Add("只能设置一个入口安全域。");

            AddDuplicateKeyErrors(result, "安全域", config.Networks.Select(n => (n.Name, n.TopologyKey)));
            var maxSegments = 1 << Math.Max(0, config.NetworkSubnetPrefix - config.TeamSubnetPrefix);
            if (config.Networks.Count > maxSegments)
                result.Errors.Add($"当前队伍网段最多可切分 {maxSegments} 个安全域，请调整前缀或减少安全域数量。");
            foreach (var network in config.Networks)
            {
                if (network.OrderIndex < 0)
                    result.Errors.Add($"安全域“{network.Name}”的排序序号不能小于 0。");
                else if (network.OrderIndex >= maxSegments && string.IsNullOrWhiteSpace(network.Cidr))
                    result.Errors.Add($"安全域“{network.Name}”的排序序号 {network.OrderIndex} 超出队伍网段可切分范围（0-{maxSegments - 1}），会导致 CIDR 越界。");
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

        if (config.Nodes.All(n => !n.IsEntry && !n.PublishPort))
            result.Errors.Add("至少需要一个入口节点或公开端口节点。");

        foreach (var portGroup in config.Nodes
                     .Where(n => n.IsEntry || n.PublishPort)
                     .GroupBy(n => n.ExposePort)
                     .Where(g => g.Count() > 1))
        {
            var names = string.Join("、", portGroup.Select(n => n.Name).Take(4));
            result.Errors.Add(
                $"渗透赛入口/发布节点会直接绑定队伍节点的服务端口，端口 {portGroup.Key} 被多个节点重复使用：{names}。请为同一编排内的公开服务配置不同端口。");
        }

        AddDuplicateKeyErrors(result, "访问策略", config.Edges.Select(e => (e.Label ?? $"策略 {e.Id}", e.TopologyKey)));

        var templateIds = config.Nodes.Where(n => n.ImageTemplateId.HasValue).Select(n => n.ImageTemplateId!.Value)
            .Distinct().ToArray();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, token);

        foreach (var network in config.Networks)
        {
            if (!string.IsNullOrWhiteSpace(network.Cidr) && !TryParseCidr(network.Cidr, out _, out _))
                result.Errors.Add($"安全域“{network.Name}”的自定义 CIDR 格式不正确。");
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
                    result.Errors.Add($"安全域“{network.Name}”的 CIDR 必须位于样例队伍网段内。");

                var interfaceCount = interfaces.Count(i => i.Network.Id == network.Id);
                var capacity = UsableDockerHostCapacity(networkPrefix);
                if ((uint)interfaceCount > capacity)
                    result.Errors.Add($"安全域“{network.Name}”可用容器 IP 不足：当前需要 {interfaceCount} 个，CIDR {subnet} 约可用 {capacity} 个。");
            }

            for (var i = 0; i < parsedNetworks.Count; i++)
            {
                for (var j = i + 1; j < parsedNetworks.Count; j++)
                {
                    var left = parsedNetworks[i];
                    var right = parsedNetworks[j];
                    if (CidrRangesOverlap(left.Address, left.Prefix, right.Address, right.Prefix))
                        result.Errors.Add($"安全域“{left.Network.Name}”和“{right.Network.Name}”的 CIDR 存在重叠。");
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

            if (!interfaces.Any(i => i.Node.Id == node.Id))
                result.Errors.Add($"节点“{node.Name}”至少需要一个网卡。");

            if (node.IsEntry)
            {
                var primaryNetwork = config.Networks.FirstOrDefault(n => n.Id == node.NetworkId);
                if (primaryNetwork is not { IsEntry: true })
                    result.Errors.Add($"入口节点“{node.Name}”必须位于入口安全域。");
            }

            if (node.ImageTemplateId is { } templateId)
            {
                if (!templates.TryGetValue(templateId, out var template))
                    result.Errors.Add($"节点“{node.Name}”引用的环境模板不存在。");
                else if (template is not { OSType: OSType.Linux, ImageType: ImageType.Docker, Status: ImageStatus.Ready })
                    result.Errors.Add($"节点“{node.Name}”必须引用已就绪的 Linux Docker 环境模板。");
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
                    result.Errors.Add($"节点“{item.Node.Name}”网卡“{item.Name}”的固定 IP 必须位于安全域“{item.Network.Name}”样例 CIDR 内。");

                if (!staticIpByNetwork.TryGetValue(item.Network.Id, out var usedIps))
                {
                    usedIps = new HashSet<string>(StringComparer.Ordinal);
                    staticIpByNetwork[item.Network.Id] = usedIps;
                }

                if (!usedIps.Add(staticIp.ToString()))
                    result.Errors.Add($"安全域“{item.Network.Name}”中存在重复固定 IP：{staticIp}。");
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
                result.Errors.Add($"访问策略“{edge.Label ?? edge.Id.ToString()}”引用了不存在的源或目标。");

            if (RequiresRuntimeRoute(edge) && edge.PolicyAction == PenetrationPolicyAction.Deny)
                result.Warnings.Add($"访问策略“{edge.Label ?? edge.Id.ToString()}”为 Deny：首版不会生成可达路由，也不会执行端口级阻断。");

            if (edge.PolicyAction == PenetrationPolicyAction.Allow && edge.IsRouteHint &&
                edge.SourceKind == PenetrationPolicyScope.Node && edge.TargetKind == PenetrationPolicyScope.Node)
            {
                var source = config.Nodes.FirstOrDefault(n => n.Id == edge.SourceId || n.Id == edge.SourceNodeId);
                var target = config.Nodes.FirstOrDefault(n => n.Id == edge.TargetId || n.Id == edge.TargetNodeId);
                if (source is null || target is null)
                    result.Errors.Add($"访问策略“{edge.Label ?? edge.Id.ToString()}”引用了不存在的节点。");
            }
        }

        // Validation uses team index 0 as a deterministic IPAM sample. Runtime deployment
        // recomputes the same shape per team, so warnings here are topology-level signals.
        var runtimeInterfaces = BuildRuntimeInterfaces(config, 0, sampleNetworkNames, sampleSubnetsByName);
        var runtimeRoutes = CompileRuntimeRoutes(config, runtimeInterfaces);
        foreach (var unsupported in runtimeRoutes.Where(r =>
                     RequiresRuntimeRoute(r.Edge) && r.Edge.PolicyAction == PenetrationPolicyAction.Allow &&
                     r.Status == PenetrationRouteStatus.Unsupported))
            result.Errors.Add($"访问策略“{unsupported.Label}”无法执行为网络级路由：{unsupported.Message}");

        foreach (var duplicate in runtimeRoutes
                     .Where(r => r.Status == PenetrationRouteStatus.HintOnly &&
                                 r.Message.Contains("同一安全域路径已由", StringComparison.Ordinal))
                     .Take(6))
            result.Warnings.Add(
                $"访问策略“{duplicate.Label}”覆盖了已有运行期安全域路径：{duplicate.Message}");

        if (config.Edges.Count > 0)
            result.Warnings.Add("访问策略会进入部署计划、选手拓扑和任务链；RuntimeRoute/Both 会生成网络级显式路由。首版为保证回包和探测会写入反向路由，呈现网段级连通，不做单向 ACL、协议/端口级防火墙。");

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
                PublishPort = n.Node.PublishPort || n.Node.IsEntry,
                ExposePort = n.Node.ExposePort,
                AdminAccessHint = n.Node.PublishPort || n.Node.IsEntry ? "部署后生成公开管理入口" : "内部节点，可通过后台查看容器和网卡信息",
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
                        ? "仅作为题目提示/拓扑路径"
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
                "按安全域创建平台管理的 Linux bridge/veth fabric；普通内网节点使用 Docker network none，入口/发布节点保留管理网。",
                "按节点网卡把 veth 接入容器网络命名空间并配置固定 IP/CIDR。",
                "对 RuntimeRoute/Both 策略编译并应用网络级显式路由；首版为保证回包会写入反向路由，协议和端口字段只作为路径摘要，不做运行期阻断。",
                "注入动态 Flag、环境变量和资源限制。",
                "生成入口端口、后台管理入口、运行节点和提交日志。"
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
        var routedNetworkIds = routePlans
            .Where(r => r.Status == PenetrationRouteStatus.RoutePlanned)
            .SelectMany(r => new[] { r.SourceInterface?.Network.Id, r.TargetInterface?.Network.Id })
            .OfType<int>()
            .ToHashSet();
        var networks = config.Networks.OrderBy(n => n.OrderIndex).Select(n => new RuntimeNetworkPlan(
            n,
            networkNames[n.Id],
            networkSubnets.GetValueOrDefault(networkNames[n.Id]) ?? AllocateSubnet(config.BaseCidr, config.NetworkSubnetPrefix, n.OrderIndex),
            IsInternalNetwork(n) && !routedNetworkIds.Contains(n.Id)
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
            var image = await ResolveImage(node, token) ?? string.Empty;
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

    ContainerConfig BuildContainerConfig(RuntimeNodePlan nodePlan, RuntimePlan plan, int teamId, Guid workerId)
    {
        var envVars = DeserializeDictionary(nodePlan.Node.EnvironmentVariables);
        InjectFlagEnvironmentVariables(envVars, nodePlan.Node.ScoreItems, nodePlan.FlagMap);
        ResolveRuntimeEnvironmentPlaceholders(envVars, plan);
        var attachments = nodePlan.Interfaces.Select(i => new ContainerNetworkAttachment
        {
            NetworkName = i.NetworkName,
            SubnetCidr = i.Cidr,
            IPAddress = i.IpAddress,
            IsPrimary = i.IsPrimary,
            IsInternal = nodePlan.RuntimeNetworks.GetValueOrDefault(i.Network.Id, IsInternalNetwork(i.Network))
        }).ToList();
        var primary = nodePlan.PrimaryInterface;

        var removeDefaultRoute = ShouldRemoveDefaultRoute(nodePlan);
        return new ContainerConfig
        {
            Image = nodePlan.Image,
            TeamId = teamId.ToString(),
            ChallengeId = nodePlan.Node.Id,
            UserId = Guid.Empty,
            ExposedPort = nodePlan.Node.ExposePort,
            Flag = nodePlan.FlagMap.Values.FirstOrDefault(),
            CPUCount = nodePlan.Node.CpuCount,
            MemoryLimit = nodePlan.Node.MemoryLimit,
            StorageLimit = nodePlan.Node.StorageLimit,
            NetworkMode = NetworkMode.Custom,
            NetworkName = primary?.NetworkName,
            IPAddress = primary?.IpAddress,
            AdditionalNetworkNames = attachments.Where(a => !a.IsPrimary).Select(a => a.NetworkName).ToList(),
            NetworkSubnets = attachments.ToDictionary(a => a.NetworkName, a => a.SubnetCidr ?? string.Empty),
            NetworkAttachments = attachments,
            PublishPort = nodePlan.Node.PublishPort || nodePlan.Node.IsEntry,
            PreferredHostPort = null,
            BypassPublicProxy = nodePlan.Node.PublishPort || nodePlan.Node.IsEntry,
            EnvironmentVariables = envVars,
            StartCommand = nodePlan.Node.StartCommand,
            HealthCheck = nodePlan.Node.HealthCheck,
            UsePenetrationFabric = true,
            EnableNetworkAdmin = false,
            RemoveDefaultRoute = removeDefaultRoute,
            EnableIpForwarding = false,
            PreferredNodeId = workerId
        };
    }

    static bool ShouldRemoveDefaultRoute(RuntimeNodePlan nodePlan) =>
        !nodePlan.Node.PublishPort &&
        !nodePlan.Node.IsEntry &&
        nodePlan.Interfaces.Any(i =>
            IsInternalNetwork(i.Network) &&
            !nodePlan.RuntimeNetworks.GetValueOrDefault(i.Network.Id, IsInternalNetwork(i.Network)));

    List<RuntimeRoutePlan> CompileRuntimeRoutes(PenetrationConfig config, IReadOnlyList<RuntimeInterfacePlan> interfaces)
    {
        var plans = new List<RuntimeRoutePlan>();
        var interfacesByNode = interfaces.GroupBy(i => i.Node.Id).ToDictionary(g => g.Key, g => g.ToList());
        var interfacesByNetwork = interfaces.GroupBy(i => i.Network.Id).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var edge in config.Edges.OrderBy(e => e.Priority).ThenBy(e => e.Id))
        {
            var label = string.IsNullOrWhiteSpace(edge.Label) ? "访问路径" : edge.Label;
            if (!RequiresRuntimeRoute(edge))
            {
                plans.Add(RuntimeRoutePlan.Hint(edge, label, "该策略仅用于题目拓扑、提示和迷雾解锁，不改变运行期网络可达性。"));
                continue;
            }

            if (edge.PolicyAction != PenetrationPolicyAction.Allow)
            {
                plans.Add(RuntimeRoutePlan.Hint(edge, label,
                    "Deny 在首版表示不生成可达路由；平台不承诺端口级或包级阻断。"));
                continue;
            }

            var sourceNetworks = ResolvePolicyNetworks(config, interfacesByNode, edge.SourceKind, edge.SourceId, edge.SourceNodeId);
            var targetNetworks = ResolvePolicyNetworks(config, interfacesByNode, edge.TargetKind, edge.TargetId, edge.TargetNodeId);
            if (sourceNetworks.Count == 0 || targetNetworks.Count == 0)
            {
                plans.Add(RuntimeRoutePlan.Unsupported(edge, label, "源或目标没有可解析的安全域网卡。"));
                continue;
            }

            var distinctPairs = sourceNetworks
                .SelectMany(source => targetNetworks.Select(target => (Source: source, Target: target)))
                .Where(pair => pair.Source.Network.Id != pair.Target.Network.Id)
                .DistinctBy(pair => $"{pair.Source.Network.Id}:{pair.Target.Network.Id}")
                .ToList();

            if (distinctPairs.Count == 0)
            {
                plans.Add(RuntimeRoutePlan.Hint(edge, label, "源和目标位于同一安全域，Docker 网络内天然可达，无需生成显式路由。"));
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
                        $"同一安全域路径已由更高优先级策略“{existingExecutable.Label}”生成网络级路由，本策略仅保留为题目提示和审计记录。"));
                    continue;
                }

                var routeNode = FindRouteNode(config, interfacesByNode, pair.Source.Network.Id, pair.Target.Network.Id);
                if (routeNode is null)
                {
                    plans.Add(RuntimeRoutePlan.Unsupported(edge, label,
                        $"无法连接“{pair.Source.Network.Name}”到“{pair.Target.Network.Name}”：缺少同时连接两个安全域且允许路由的跳板/防火墙节点。"));
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
                        $"无法连接“{pair.Source.Network.Name}”到“{pair.Target.Network.Name}”：源或目标安全域缺少除路由节点以外的可探测端点。"));
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

    async Task<(bool Success, string Message)> ApplyRuntimeRoutes(PenetrationTeamEnvironment environment,
        RuntimePlan plan, CancellationToken token)
    {
        var runtimeByKey = environment.RuntimeNodes
            .Where(r => !string.IsNullOrWhiteSpace(r.TopologyNodeKey))
            .ToDictionary(r => r.TopologyNodeKey, StringComparer.Ordinal);
        var errors = new List<string>();
        var nextRuntimeRoutes = new List<PenetrationRuntimeRoute>(plan.Routes.Count);
        var appliedRoutes = new List<RuntimeRoutePlan>();

        foreach (var route in plan.Routes)
        {
            var runtimeRoute = new PenetrationRuntimeRoute
            {
                Environment = environment,
                EdgeTopologyKey = route.Edge.TopologyKey,
                Label = Truncate(route.Label, 128),
                EnforcementMode = route.Edge.EnforcementMode,
                Status = route.Status,
                RouteNodeKey = route.RouteNode?.TopologyKey,
                RouteNodeName = TruncateNullable(route.RouteNode?.Name, 128),
                SourceNetworkName = TruncateNullable(route.SourceInterface?.NetworkName, 128),
                TargetNetworkName = TruncateNullable(route.TargetInterface?.NetworkName, 128),
                SourceCidr = TruncateNullable(route.SourceInterface?.Cidr, 64),
                TargetCidr = TruncateNullable(route.TargetInterface?.Cidr, 64),
                GatewayIp = TruncateNullable(route.SourceRouteInterface?.IpAddress, 64),
                CommandSummary = TruncateNullable(route.CommandSummary, 1024),
                Message = TruncateNullable(route.Message, 1024),
                CreatedAt = DateTimeOffset.UtcNow
            };
            nextRuntimeRoutes.Add(runtimeRoute);

            if (route.Status != PenetrationRouteStatus.RoutePlanned)
                continue;

            try
            {
                await ApplyRoutePlan(environment, route, runtimeByKey, token);
                appliedRoutes.Add(route);
                runtimeRoute.Status = PenetrationRouteStatus.RouteApplied;
                runtimeRoute.AppliedAt = DateTimeOffset.UtcNow;
                runtimeRoute.Message = "网络级显式路由已应用；协议/端口字段仅作为路径说明。";
                AddDeploymentEvent(environment, "route-apply", PenetrationDeploymentEventLevel.Success,
                    $"访问策略“{route.Label}”的网络级路由已应用。", route.RouteNode?.Name,
                    route.CommandSummary);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                runtimeRoute.Status = PenetrationRouteStatus.RouteFailed;
                var message = NormalizeFabricError(ex.Message, "运行期网络级路由应用失败。");
                runtimeRoute.Message = Truncate(message, 1024);
                errors.Add($"访问策略“{route.Label}”路由应用失败：{message}");
                AddDeploymentEvent(environment, "route-apply", PenetrationDeploymentEventLevel.Error,
                    $"访问策略“{route.Label}”路由应用失败：{message}", route.RouteNode?.Name,
                    route.CommandSummary);
            }
        }

        if (errors.Count > 0 && appliedRoutes.Count > 0)
        {
            var summary = string.Join("；", appliedRoutes.Select(r => r.Label).Distinct().Take(6));
            AddDeploymentEvent(environment, "route-apply", PenetrationDeploymentEventLevel.Warning,
                $"部分网络级路由已经写入，但后续策略失败，环境将进入清理链路。已写入：{summary}。");
        }

        context.PenetrationRuntimeRoutes.RemoveRange(environment.RuntimeRoutes);
        environment.RuntimeRoutes.Clear();
        foreach (var runtimeRoute in nextRuntimeRoutes)
            environment.RuntimeRoutes.Add(runtimeRoute);

        await context.SaveChangesAsync(token);
        return errors.Count == 0
            ? (true, "运行期路由已应用。")
            : (false, string.Join('\n', errors));
    }

    async Task ApplyRoutePlan(PenetrationTeamEnvironment environment, RuntimeRoutePlan route,
        IReadOnlyDictionary<string, PenetrationRuntimeNode> runtimeByKey, CancellationToken token)
    {
        if (route is not
            {
                RouteNode: not null,
                SourceRouteInterface: not null,
                TargetRouteInterface: not null,
                SourceInterface: not null,
                TargetInterface: not null
            })
            throw new InvalidOperationException("路由计划缺少必要的网卡或路由节点。");

        var routeRuntime = ResolveRuntimeContainer(runtimeByKey, route.RouteNode.TopologyKey, route.RouteNode.Name);
        var forwarding = await penetrationFabricManager.EnableForwardingAsync(routeRuntime.Container!, token);
        if (!forwarding.IsSupported || !forwarding.Succeeded)
            throw new InvalidOperationException(NormalizeFabricError(forwarding.Message,
                $"路由节点“{route.RouteNode.Name}”无法开启 IPv4 转发。"));
        AddDeploymentEvent(environment, "fabric-route", PenetrationDeploymentEventLevel.Info,
            $"路由节点“{route.RouteNode.Name}”已开启 IPv4 转发。", route.RouteNode.Name, forwarding.Message);

        foreach (var endpoint in route.SourceEndpointInterfaces)
        {
            if (endpoint.Node.Id == route.RouteNode.Id)
                continue;

            await ApplyFabricRoute(environment, runtimeByKey, endpoint.Node.TopologyKey, endpoint.Node.Name,
                route.TargetRouteInterface.Cidr, route.SourceRouteInterface.IpAddress, token);
        }

        foreach (var endpoint in route.TargetEndpointInterfaces)
        {
            if (endpoint.Node.Id == route.RouteNode.Id)
                continue;

            // Phase 3 executes network-level reachability, not one-way ACLs. We add the
            // reverse route so return traffic and route probes work; Deny remains
            // non-executable in this version and never enters this code path.
            await ApplyFabricRoute(environment, runtimeByKey, endpoint.Node.TopologyKey, endpoint.Node.Name,
                route.SourceRouteInterface.Cidr, route.TargetRouteInterface.IpAddress, token);
        }

        await ProbeRouteReachability(environment, route, runtimeByKey, token);
    }

    async Task<(bool Success, string Message)> AttachRuntimeFabricInterfaces(PenetrationTeamEnvironment environment,
        RuntimeNodePlan nodePlan, DataContainer container, CancellationToken token)
    {
        if (!penetrationFabricManager.IsSupported)
            return (false, "当前容器后端不支持渗透 fabric 网络，无法部署 RuntimeRoute 级多网段拓扑。");

        foreach (var iface in nodePlan.Interfaces.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.OrderIndex))
        {
            if (!TryParseCidr(iface.Cidr, out _, out var prefix))
                return (false, $"网卡“{iface.InterfaceName}”所在安全域 CIDR 无效：{iface.Cidr}。");

            var hostIf = BuildFabricHostInterfaceName(environment.Id, nodePlan.Node.Id, iface.InterfaceId);
            var containerIf = BuildFabricContainerInterfaceName(iface);
            var spec = new PenetrationFabricInterfaceSpec(
                iface.NetworkName,
                iface.Cidr,
                hostIf,
                containerIf,
                iface.IpAddress,
                prefix,
                iface.IsPrimary,
                ShouldRemoveDefaultRoute(nodePlan) && iface.IsPrimary);

            var result = await penetrationFabricManager.AttachInterfaceAsync(container, spec, token);
            if (!result.IsSupported || !result.Succeeded)
                return (false, NormalizeFabricError(result.Message,
                    $"网卡“{iface.InterfaceName}”接入安全域“{iface.Network.Name}”失败。"));

            iface.FabricHostInterfaceName = hostIf;
            iface.FabricContainerInterfaceName = containerIf;
            AddDeploymentEvent(environment, "fabric", PenetrationDeploymentEventLevel.Success,
                $"网卡“{containerIf}”已接入 fabric 安全域“{iface.Network.Name}”。",
                nodePlan.Node.Name, result.Message);
        }

        return (true, "fabric 网卡已配置。");
    }

    async Task ApplyFabricRoute(PenetrationTeamEnvironment environment,
        IReadOnlyDictionary<string, PenetrationRuntimeNode> runtimeByKey, string nodeKey, string nodeName,
        string targetCidr, string gatewayIp, CancellationToken token)
    {
        var runtime = ResolveRuntimeContainer(runtimeByKey, nodeKey, nodeName);
        var result = await penetrationFabricManager.ApplyRouteAsync(runtime.Container!, targetCidr, gatewayIp, token);
        if (!result.IsSupported || !result.Succeeded)
            throw new InvalidOperationException(NormalizeFabricError(result.Message,
                $"节点“{nodeName}”写入到 {targetCidr} 的显式路由失败。"));

        AddDeploymentEvent(environment, "fabric-route", PenetrationDeploymentEventLevel.Info,
            $"节点“{nodeName}”已写入到 {targetCidr} 的显式路由。", nodeName, result.Message);
    }

    static PenetrationRuntimeNode ResolveRuntimeContainer(
        IReadOnlyDictionary<string, PenetrationRuntimeNode> runtimeByKey, string nodeKey, string nodeName)
    {
        if (!runtimeByKey.TryGetValue(nodeKey, out var runtime) || runtime.Container is null)
            throw new InvalidOperationException($"节点“{nodeName}”没有可执行 fabric 操作的运行容器。");

        return runtime;
    }

    async Task ProbeRouteReachability(PenetrationTeamEnvironment environment, RuntimeRoutePlan route,
        IReadOnlyDictionary<string, PenetrationRuntimeNode> runtimeByKey, CancellationToken token)
    {
        if (route.SourceEndpointInterfaces.Count == 0 || route.TargetEndpointInterfaces.Count == 0)
            throw new InvalidOperationException("显式路由缺少可探测的源端点或目标端点，无法确认网络级可达性。");

        var targetProbe = route.TargetEndpointInterfaces[0];
        foreach (var source in route.SourceEndpointInterfaces)
            await ExecuteRouteProbe(environment, runtimeByKey, source.Node.TopologyKey, source.Node.Name,
                targetProbe.IpAddress, $"{source.Node.Name} -> {targetProbe.Node.Name}", token);

        var sourceProbe = route.SourceEndpointInterfaces[0];
        foreach (var target in route.TargetEndpointInterfaces)
            await ExecuteRouteProbe(environment, runtimeByKey, target.Node.TopologyKey, target.Node.Name,
                sourceProbe.IpAddress, $"{target.Node.Name} -> {sourceProbe.Node.Name}", token);

        AddDeploymentEvent(environment, "route-probe", PenetrationDeploymentEventLevel.Success,
            $"访问策略“{route.Label}”的端到端网络级连通探测通过。", route.RouteNode?.Name,
            $"{route.SourceInterface?.NetworkName} <-> {route.TargetInterface?.NetworkName}");
    }

    async Task ExecuteRouteProbe(PenetrationTeamEnvironment environment,
        IReadOnlyDictionary<string, PenetrationRuntimeNode> runtimeByKey, string nodeKey, string nodeName,
        string targetIp, string label, CancellationToken token)
    {
        if (!runtimeByKey.TryGetValue(nodeKey, out var runtime) || runtime.Container is null)
            throw new InvalidOperationException($"节点“{nodeName}”没有可执行路由探测的运行容器。");

        var result = await penetrationFabricManager.ProbeAsync(runtime.Container, targetIp, token);
        if (!result.IsSupported)
            throw new InvalidOperationException($"当前容器后端不支持 fabric 路由探测：{result.Message}");

        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"路由探测“{label}”失败：{(result.TimedOut ? "执行超时" : NormalizeFabricError(result.Message, $"退出码 {result.ExitCode}"))}");
    }

    static string BuildFabricHostInterfaceName(int environmentId, int nodeId, int interfaceId) =>
        BuildFabricName("yyp", $"{environmentId}:{nodeId}:{interfaceId}");

    static string BuildFabricContainerInterfaceName(RuntimeInterfacePlan iface)
    {
        return BuildFabricName("yyc", $"{iface.Node.TopologyKey}:{iface.InterfaceId}");
    }

    static string BuildFabricName(string prefix, string seed, int maxLength = 15)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        var name = $"{prefix}{hash}";
        return name[..Math.Min(name.Length, maxLength)];
    }

    static string ShellQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    static string NormalizeFabricError(string? message, string fallback)
    {
        if (string.IsNullOrWhiteSpace(message))
            return fallback;

        var trimmed = message.Trim();
        if (trimmed.Contains("missing host ip command", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("ip: not found", StringComparison.OrdinalIgnoreCase))
            return "节点宿主缺少 iproute2/ip 命令，无法配置渗透 fabric 网络。请在 Docker/Agent 节点安装 iproute2。";

        if (trimmed.Contains("missing nsenter command", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("nsenter: not found", StringComparison.OrdinalIgnoreCase))
            return "节点宿主缺少 nsenter，无法进入容器网络命名空间配置渗透 fabric。请安装 util-linux/nsenter。";

        if (trimmed.Contains("missing host ping command", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("ping: not found", StringComparison.OrdinalIgnoreCase))
            return "节点宿主缺少 ping 命令，无法执行端到端探测。请在 Docker/Agent 节点安装 iputils-ping，或暂时将该策略改为 HintOnly。";

        if (trimmed.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
            return "节点缺少 CAP_NET_ADMIN/root 网络管理权限，无法配置渗透 fabric。请使用具备网络命名空间管理权限的 Fleet Agent。";

        if (trimmed.Contains("100% packet loss", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Destination Host Unreachable", StringComparison.OrdinalIgnoreCase))
            return "fabric 路由已配置但端到端探测不通，请检查拓扑、路由节点转发、网卡 IP 和策略方向。";

        return trimmed;
    }

    async Task<(bool Success, string Message)> ProbeRuntimeNode(RuntimeNodePlan nodePlan, DataContainer container,
        CancellationToken token)
    {
        var publicPort = container.PublicPort ?? 0;
        var publicHost = container.PublicIP ?? container.IP;
        var shouldProbePublicPort = (nodePlan.Node.PublishPort || nodePlan.Node.IsEntry)
            && publicPort > 0
            && !string.IsNullOrWhiteSpace(publicHost);

        var healthCheckNote = string.IsNullOrWhiteSpace(nodePlan.Node.HealthCheck)
            ? string.Empty
            : "；已配置命令健康探针，当前平台版本仅执行端口/运行状态探针。";

        if (!shouldProbePublicPort)
            return (true, $"容器已运行，内网地址 {container.IP}:{container.Port}{healthCheckNote}。");

        var probe = await ProbeTcp(publicHost!, publicPort, token);
        return probe.Success
            ? (true, $"容器已运行，公开入口 {publicHost}:{publicPort} 已通过 TCP 探测{healthCheckNote}。")
            : (false, $"公开入口 {publicHost}:{publicPort} TCP 探测失败：{probe.Message}{healthCheckNote}。");
    }

    static async Task<(bool Success, string Message)> ProbeTcp(string host, int port, CancellationToken token)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            return (true, "ok");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return (false, "连接超时");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    int ResolveDeploymentParallelism()
    {
        var config = serviceProvider.GetService<IConfiguration>();
        return Math.Clamp(config?.GetValue("Penetration:DeploymentParallelism", 2) ?? 2, 1, 4);
    }

    async Task<(bool Success, string Message, PenetrationTeamEnvironment? Environment, WorkerNode? Worker)>
        AllocateTeamWorkerNode(PenetrationConfig config, int teamId, int teamIndex,
            PenetrationTeamEnvironment? existing, CancellationToken token)
    {
        var requiredContainers = config.Nodes.Count;
        if (requiredContainers <= 0)
            return (false, "渗透编排至少需要一个资产节点。", null, null);

        using var scheduleLock = await lockService.AcquireAsync("fleet:scheduler", TimeSpan.FromSeconds(10));
        var environment = existing ?? await LoadTeamEnvironment(config.GameId, teamId, token);
        if (environment is null)
        {
            environment = new PenetrationTeamEnvironment
            {
                GameId = config.GameId,
                TeamId = teamId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.PenetrationTeamEnvironments.Add(environment);
        }

        var nodes = await context.WorkerNodes
            .Where(n => n.IsSchedulable && !n.IsLocal)
            .ToArrayAsync(token);
        var occupiedNodeIds = await GetOccupiedPenetrationNodeIds([teamId], config.GameId, token);
        var preferredNodeId = environment.NodeId;

        var worker = nodes
            .Where(n => WeightedScheduler.CanHost(n, NodeCapability.Docker))
            .Where(n => !occupiedNodeIds.Contains(n.Id) || n.Id == preferredNodeId)
            .Where(n => n.MaxContainers - n.CurrentContainers >= requiredContainers)
            .OrderByDescending(n => n.Id == preferredNodeId)
            .ThenBy(n => n.CpuLoad)
            .ThenBy(n => n.MemoryLoad)
            .ThenBy(n => n.CurrentContainers)
            .FirstOrDefault();

        if (worker is null)
            return (false, "没有可用的渗透参赛节点。请在节点管理中注册远端节点，并确保该节点在线、可调度且 Docker 容量足够。", null, null);

        for (var i = 0; i < requiredContainers; i++)
            FleetManager.ReserveCapacity(worker, NodeCapability.Docker);

        environment.NodeId = worker.Id;
        environment.TeamIndex = teamIndex;
        environment.PublishedVersion = config.PublishedVersion;
        environment.NetworkPrefix = AllocateSubnet(config.BaseCidr, config.TeamSubnetPrefix, teamIndex);
        environment.Status = PenetrationRuntimeStatus.CreatingNetworks;
        environment.UpdatedAt = DateTimeOffset.UtcNow;
        environment.LastError = null;
        environment.CleanupRetryCount = 0;
        environment.NextCleanupAt = null;
        environment.LastCleanupAttemptAt = null;
        AddDeploymentEvent(environment, "allocate", PenetrationDeploymentEventLevel.Info,
            $"已分配队伍网段 {environment.NetworkPrefix}，目标节点：{worker.Name}（{worker.HostAddress}）。");
        await context.SaveChangesAsync(token);
        return (true, "ok", environment, worker);
    }

    async Task SavePenetrationStateAsync(string operation, CancellationToken token)
    {
        for (var retry = 0; retry < 3; retry++)
        {
            try
            {
                await context.SaveChangesAsync(token);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(e => e.Entity is WorkerNode))
            {
                logger.LogWarning(ex,
                    "Worker node state changed while trying to {Operation}; saving penetration state without stale node counters.",
                    operation);

                foreach (var entry in ex.Entries)
                {
                    if (entry.Entity is not WorkerNode)
                        throw;

                    entry.State = EntityState.Detached;
                }
            }
        }

        await context.SaveChangesAsync(token);
    }

    async Task ReleaseReservedDockerCapacity(WorkerNode worker, int count, CancellationToken token)
    {
        if (count <= 0)
            return;

        using var releaseLock = await lockService.AcquireAsync("fleet:scheduler", TimeSpan.FromSeconds(10));
        await using var scope = serviceProvider.CreateAsyncScope();
        var releaseContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var currentWorker = await releaseContext.WorkerNodes
            .FirstOrDefaultAsync(n => n.Id == worker.Id, token);

        if (currentWorker is null)
        {
            logger.LogWarning(
                "Failed to release {Count} reserved Docker capacity slots for deleted worker node {NodeId}.",
                count, worker.Id);
            context.Entry(worker).State = EntityState.Detached;
            return;
        }

        var before = currentWorker.CurrentContainers;
        currentWorker.CurrentContainers = Math.Max(0, currentWorker.CurrentContainers - count);
        await releaseContext.SaveChangesAsync(token);

        logger.LogDebug(
            "Released {Count} reserved Docker capacity slots for worker node {NodeId}: {Before} -> {After}.",
            count, worker.Id, before, currentWorker.CurrentContainers);
        context.Entry(worker).State = EntityState.Detached;
    }

    async Task<(bool Success, string Message)> CheckFleetCapacity(int requiredContainersPerTeam,
        IReadOnlyCollection<int> targetTeamIds, int? gameId, CancellationToken token)
    {
        var teamCount = targetTeamIds.Count;
        if (teamCount == 0)
            return (true, "暂无参赛队伍。");

        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(n => n.IsSchedulable && !n.IsLocal)
            .ToArrayAsync(token);
        var occupiedNodeIds = await GetOccupiedPenetrationNodeIds(targetTeamIds, gameId, token);

        var capacity = nodes
            .Where(n => WeightedScheduler.CanHost(n, NodeCapability.Docker))
            .Where(n => !occupiedNodeIds.Contains(n.Id))
            .Count(n => n.MaxContainers - n.CurrentContainers >= requiredContainersPerTeam);

        return capacity >= teamCount
            ? (true, $"容量检查通过，可部署 {capacity} 支队伍。")
            : (false, $"渗透参赛节点容量不足：当前最多可部署 {capacity} 支队伍，需要 {teamCount} 支队伍。请注册足够的远端节点；平台本地节点不会作为渗透靶标节点参与调度。");
    }

    async Task<HashSet<Guid>> GetOccupiedPenetrationNodeIds(IReadOnlyCollection<int> excludedTeamIds, int? gameId,
        CancellationToken token)
    {
        var query = context.PenetrationTeamEnvironments.AsNoTracking()
            .Where(e => e.NodeId != null &&
                        e.Status != PenetrationRuntimeStatus.Stopped &&
                        e.Status != PenetrationRuntimeStatus.Failed);

        if (gameId.HasValue && excludedTeamIds.Count > 0)
            query = query.Where(e => e.GameId != gameId.Value || !excludedTeamIds.Contains(e.TeamId));

        return await query
            .Select(e => e.NodeId!.Value)
            .ToHashSetAsync(token);
    }

    async Task<string?> ResolveImage(PenetrationNode node, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(node.ImageName))
            return await dockerRegistry.ResolveImageReferenceAsync(node.ImageName, token);

        if (node.ImageTemplateId is not { } templateId)
            return null;

        var template = await context.ImageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, token);
        return template is { OSType: OSType.Linux, ImageType: ImageType.Docker, Status: ImageStatus.Ready }
            ? await dockerRegistry.ResolveImageTemplateReferenceAsync(template.Name, template.RegistryUrl, token)
            : null;
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
                ZoneType = n.ZoneType,
                TrustLevel = n.TrustLevel,
                Description = n.Description,
                DefaultPolicy = n.DefaultPolicy,
                OrderIndex = n.OrderIndex,
                IsEntry = n.IsEntry,
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
                    NodeType = n.NodeType,
                    ImageTemplateId = n.ImageTemplateId,
                    ImageName = n.ImageName,
                    CpuCount = n.CpuCount,
                    MemoryLimit = n.MemoryLimit,
                    StorageLimit = n.StorageLimit,
                    ExposePort = n.ExposePort,
                    IsEntry = n.IsEntry,
                    PublishPort = n.PublishPort,
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
                    node.IsEntry,
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
                IsManagement = node.IsEntry,
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
                IsManagement = node.IsEntry
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
        const string startToken = "{{node:";
        if (string.IsNullOrWhiteSpace(value) || !value.Contains(startToken, StringComparison.Ordinal))
            return value;

        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var start = value.IndexOf(startToken, index, StringComparison.Ordinal);
            if (start < 0)
            {
                builder.Append(value, index, value.Length - index);
                break;
            }

            builder.Append(value, index, start - index);
            var end = value.IndexOf("}}", start + startToken.Length, StringComparison.Ordinal);
            if (end < 0)
            {
                builder.Append(value, start, value.Length - start);
                break;
            }

            var expression = value[(start + startToken.Length)..end];
            builder.Append(ResolveNodePlaceholder(expression, runtimeByKey) ?? value[start..(end + 2)]);
            index = end + 2;
        }

        return builder.ToString();
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
        Name = "公网入口区",
        Slug = "public",
        ZoneType = PenetrationZoneType.Public,
        TrustLevel = 10,
        IsEntry = true,
        OrderIndex = orderIndex,
        PositionX = 80,
        PositionY = 80,
        Width = 560,
        Height = 390
    };

    static string ResolvePolicyName(PenetrationConfig config, PenetrationPolicyScope kind, int id, int fallbackNodeId)
    {
        if (kind == PenetrationPolicyScope.Network)
            return config.Networks.FirstOrDefault(n => n.Id == id)?.Name ?? $"安全域 {id}";

        var nodeId = id > 0 ? id : fallbackNodeId;
        return config.Nodes.FirstOrDefault(n => n.Id == nodeId)?.Name ?? $"节点 {nodeId}";
    }

    static string? BuildAdminUrl(string? host, int publicPort, int exposePort)
    {
        if (string.IsNullOrWhiteSpace(host) || publicPort <= 0)
            return null;

        var scheme = exposePort == 443 ? "https" : "http";
        return $"{scheme}://{host}:{publicPort}";
    }

    static bool IsInternalNetwork(PenetrationNetwork network) =>
        network.ZoneType != PenetrationZoneType.Public && !network.IsEntry;

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
