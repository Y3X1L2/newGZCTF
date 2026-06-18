using System.Net;
using System.Text.Json;
using GZCTF.Extensions;
using GZCTF.Models.Internal;
using GZCTF.Models.Request.Game;
using GZCTF.Repositories.Interface;
using GZCTF.Services.Cache;
using GZCTF.Services.Container.Manager;
using GZCTF.Services.Fleet;
using Microsoft.EntityFrameworkCore;
using DataContainer = GZCTF.Models.Data.Container;

namespace GZCTF.Services;

public class PenetrationService(
    AppDbContext context,
    IContainerManager containerManager,
    IServiceProvider serviceProvider,
    CacheHelper cacheHelper,
    ISubmissionRepository submissionRepository,
    IGameEventRepository gameEventRepository,
    ILogger<PenetrationService> logger)
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    public async Task<(bool Success, string Message)> DeployGame(int gameId, CancellationToken token = default)
    {
        var savedConfig = await LoadConfig(gameId, token);
        if (savedConfig is null || savedConfig.PublishedVersion <= 0)
            return (false, "请先发布渗透编排版本。");

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

        var capacity = await CheckFleetCapacity(config.Nodes.Count, participations.Length, token);
        if (!capacity.Success)
        {
            savedConfig.Status = PenetrationDeploymentStatus.Failed;
            savedConfig.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);
            return (false, capacity.Message);
        }

        var ok = 0;
        foreach (var (part, index) in participations.Select((p, i) => (p, i)))
        {
            var existing = await LoadTeamEnvironment(gameId, part.TeamId, token);
            if (existing is not null)
                await DestroyEnvironment(existing, token);

            var result = await DeployTeam(config, part.TeamId, index, existing is not null, existing, token);
            if (result.Success)
                ok++;
        }

        savedConfig.Status = ok == participations.Length
            ? PenetrationDeploymentStatus.Running
            : ok == 0 ? PenetrationDeploymentStatus.Failed : PenetrationDeploymentStatus.Partial;
        savedConfig.DeployedAt = DateTimeOffset.UtcNow;
        savedConfig.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        return (ok > 0 || participations.Length == 0, $"已部署 {ok}/{participations.Length} 支队伍环境。");
    }

    public async Task<(bool Success, string Message)> RebuildTeam(int gameId, int teamId, bool byAdmin, Guid? userId,
        CancellationToken token = default)
    {
        var savedConfig = await LoadConfig(gameId, token);
        if (savedConfig is null || savedConfig.PublishedVersion <= 0)
            return (false, "渗透编排尚未发布。");

        var teamIds = await context.Participations.AsNoTracking()
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .OrderBy(p => p.TeamId)
            .Select(p => p.TeamId)
            .ToArrayAsync(token);
        var index = Array.IndexOf(teamIds, teamId);
        if (index < 0)
            return (false, "队伍未通过比赛审核。");

        var environment = await LoadTeamEnvironment(gameId, teamId, token);
        var targetVersion = environment?.PublishedVersion > 0
            ? environment.PublishedVersion
            : savedConfig.PublishedVersion;
        var config = await LoadPublishedConfig(gameId, targetVersion, token);
        if (config is null)
            return (false, $"发布版本 v{targetVersion} 快照不存在，无法重建环境。");

        if (!byAdmin && environment is not null && environment.ResetCount >= config.MaxResetCount)
            return (false, "环境重置次数已用完。");

        if (environment is not null)
            await DestroyEnvironment(environment, token);

        if (environment is not null && !byAdmin)
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

        return await DeployTeam(config, teamId, index, true, environment, token);
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

    public async Task<(bool Success, string Message)> StopGame(int gameId, CancellationToken token = default)
    {
        var environments = await context.PenetrationTeamEnvironments
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.Container)
            .Where(e => e.GameId == gameId)
            .ToArrayAsync(token);

        foreach (var environment in environments)
            await DestroyEnvironment(environment, token);

        var config = await context.PenetrationConfigs.FirstOrDefaultAsync(c => c.GameId == gameId, token);
        if (config is not null)
        {
            config.Status = PenetrationDeploymentStatus.Stopped;
            config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(token);
        return (true, "渗透环境已停止。");
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
        var runtimeInterfaces = environment.RuntimeNodes
            .Where(r => !string.IsNullOrWhiteSpace(r.TopologyNodeKey))
            .ToDictionary(r => r.TopologyNodeKey, r => ReadRuntimeInterfaces(r), StringComparer.Ordinal);
        var teamIndex = await GetAcceptedTeamIndex(gameId, teamId, token);
        var networkNames = config.Networks.ToDictionary(n => n.Id, n => BuildRuntimeNetworkName(config, teamId, n));
        var networkCidrs = BuildNetworkSubnets(config, teamIndex, networkNames);

        return new PenetrationWorkspaceModel
        {
            GameId = gameId,
            TeamId = teamId,
            TeamName = environment.Team.Name,
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
                    NodeName = x.Node.Name,
                    Host = x.Runtime.Container?.PublicIP ?? x.Runtime.Container?.IP ?? x.Runtime.IpAddress,
                    Port = x.Runtime.Container?.PublicPort ?? 0,
                    ExposePort = x.Node.ExposePort
                }).ToList(),
            Networks = config.Networks.OrderBy(n => n.OrderIndex).Select(n => new PenetrationWorkspaceNetworkModel
            {
                Id = n.Id,
                Name = n.Name,
                Slug = n.Slug,
                ZoneType = n.ZoneType,
                TrustLevel = n.TrustLevel,
                OrderIndex = n.OrderIndex,
                IsEntry = n.IsEntry,
                Cidr = networkCidrs.GetValueOrDefault(networkNames[n.Id]) ?? environment.NetworkPrefix,
                PositionX = n.PositionX,
                PositionY = n.PositionY,
                Width = n.Width,
                Height = n.Height
            }).ToList(),
            Nodes = config.Nodes.OrderBy(n => n.OrderIndex).Select(n =>
            {
                runtimeByNode.TryGetValue(n.TopologyKey, out var runtime);
                runtimeInterfaces.TryGetValue(n.TopologyKey, out var runtimeInterfaceList);
                return new PenetrationWorkspaceNodeModel
                {
                    Id = n.Id,
                    NetworkId = n.NetworkId,
                    Name = n.Name,
                    Description = n.Description,
                    NodeType = n.NodeType,
                    IpAddress = runtime?.IpAddress,
                    IsEntry = n.IsEntry,
                    RuntimeStatus = runtime?.Status ?? PenetrationRuntimeStatus.Pending,
                    PositionX = n.PositionX,
                    PositionY = n.PositionY,
                    Interfaces = BuildWorkspaceInterfaces(n, runtimeInterfaceList ?? []),
                    ScoreItems = n.ScoreItems.Where(i => i.IsVisible).OrderBy(i => i.OrderIndex).Select(i =>
                        new PenetrationWorkspaceScoreItemModel
                        {
                            Id = i.Id,
                            Title = i.Title,
                            Description = i.Description,
                            Category = i.Category,
                            Score = i.Score,
                            Solved = solved.Contains(i.TopologyKey),
                            Attempts = attempts.GetValueOrDefault(i.TopologyKey),
                            MaxAttempts = i.MaxAttempts,
                            PrerequisiteItemIds = DeserializeIntList(i.PrerequisiteItemIds)
                        }).ToList()
                };
            }).ToList(),
            Policies = config.Edges.Where(e => e.SourceNodeId > 0 && e.TargetNodeId > 0)
                .OrderBy(e => e.Id)
                .Select(e => new PenetrationWorkspacePolicyModel
                {
                    Id = e.Id,
                    Label = string.IsNullOrWhiteSpace(e.Label) ? "访问路径" : e.Label,
                    SourceNodeId = e.SourceNodeId,
                    TargetNodeId = e.TargetNodeId,
                    Protocol = e.Protocol,
                    PortRange = e.PortRange
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

        var alreadySolved = await context.PenetrationSubmissions.AnyAsync(s =>
            s.GameId == gameId &&
            s.TeamId == teamId &&
            s.PublishedVersion == environment.PublishedVersion &&
            s.ScoreItemTopologyKey == item.TopologyKey &&
            s.Status == AnswerResult.Accepted,
            token);
        if (alreadySolved)
            return new PenetrationSubmitResultModel { Accepted = false, Message = "该得分项已完成。" };

        var prerequisites = DeserializeIntList(item.PrerequisiteItemIds);
        if (prerequisites.Count > 0)
        {
            var solvedPrerequisites = await context.PenetrationSubmissions.AsNoTracking()
                .Where(s => s.GameId == gameId &&
                            s.TeamId == teamId &&
                            s.PublishedVersion == environment.PublishedVersion &&
                            s.Status == AnswerResult.Accepted &&
                            prerequisites.Contains(s.ScoreItemId))
                .Select(s => s.ScoreItemId)
                .Distinct()
                .CountAsync(token);

            if (solvedPrerequisites < prerequisites.Count)
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

        var expected = BuildFlag(item, gameId, teamId, environment.PublishedVersion);
        var accepted = string.Equals(model.Flag.Trim(), expected, StringComparison.Ordinal);
        var submission = new PenetrationSubmission
        {
            GameId = gameId,
            TeamId = teamId,
            ParticipationId = participationId,
            UserId = userId,
            ScoreItemId = item.Id,
            PublishedVersion = environment.PublishedVersion,
            ScoreItemTopologyKey = item.TopologyKey,
            Answer = model.Flag.Trim(),
            Status = accepted ? AnswerResult.Accepted : AnswerResult.WrongAnswer,
            Score = accepted ? item.Score : 0,
            SubmittedAt = DateTimeOffset.UtcNow
        };

        await context.PenetrationSubmissions.AddAsync(submission, token);
        await context.SaveChangesAsync(token);

        if (accepted)
            await cacheHelper.FlushScoreboardCache(gameId, token);

        await PublishSubmissionSideEffects(submission, item, userId, token);

        return new PenetrationSubmitResultModel
        {
            Accepted = accepted,
            Score = submission.Score,
            Message = accepted ? "Flag 正确。" : "Flag 错误。"
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
        return await context.PenetrationTeamEnvironments.AsNoTracking()
            .Where(e => e.GameId == gameId)
            .Include(e => e.Team)
            .Include(e => e.Node)
            .Include(e => e.RuntimeNodes)
            .OrderBy(e => e.Team.Name)
            .Select(e => new PenetrationTeamEnvironmentModel
            {
                EnvironmentId = e.Id,
                TeamId = e.TeamId,
                TeamName = e.Team.Name,
                WorkerNodeId = e.NodeId,
                WorkerNodeName = e.Node == null ? null : e.Node.Name,
                NetworkPrefix = e.NetworkPrefix,
                PublishedVersion = e.PublishedVersion,
                Status = e.Status,
                ResetCount = e.ResetCount,
                RuntimeNodeCount = e.RuntimeNodes.Count,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
                LastError = e.LastError
            }).ToArrayAsync(token);
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

    async Task<(bool Success, string Message)> DeployTeam(PenetrationConfig config, int teamId, int teamIndex,
        bool rebuild, PenetrationTeamEnvironment? existing, CancellationToken token)
    {
        var worker = await SelectWorkerNode(config.Nodes.Count, token);
        if (worker is null)
            return (false, "没有足够容量的 Docker 节点。");

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

        environment.NodeId = worker.Id;
        environment.PublishedVersion = config.PublishedVersion;
        environment.NetworkPrefix = AllocateSubnet(config.BaseCidr, config.TeamSubnetPrefix, teamIndex);
        environment.Status = PenetrationRuntimeStatus.Pending;
        environment.UpdatedAt = DateTimeOffset.UtcNow;
        environment.LastError = null;
        await context.SaveChangesAsync(token);

        var plan = await BuildRuntimePlan(config, teamIndex, teamId, token);
        var success = true;
        var failureMessages = new List<string>();

        foreach (var nodePlan in plan.Nodes)
        {
            var containerConfig = BuildContainerConfig(nodePlan, teamId, worker.Id);
            var container = await containerManager.CreateContainerAsync(containerConfig, token);

            if (container is null)
            {
                success = false;
                failureMessages.Add($"节点“{nodePlan.Node.Name}”容器创建失败。");
                await context.PenetrationRuntimeNodes.AddAsync(new PenetrationRuntimeNode
                {
                    EnvironmentId = environment.Id,
                    TopologyNodeId = nodePlan.Node.Id,
                    TopologyNodeKey = nodePlan.Node.TopologyKey,
                    NetworkName = nodePlan.PrimaryInterface?.NetworkName ?? string.Empty,
                    IpAddress = nodePlan.PrimaryInterface?.IpAddress ?? string.Empty,
                    InterfaceSummary = JsonSerializer.Serialize(nodePlan.Interfaces, JsonOptions),
                    Status = PenetrationRuntimeStatus.Failed
                }, token);
                continue;
            }

            if (container.Id == Guid.Empty)
                container.Id = Guid.CreateVersion7();
            container.NodeId = worker.Id;
            await context.Containers.AddAsync(container, token);
            await context.SaveChangesAsync(token);

            var publicHost = container.PublicIP ?? container.IP;
            var adminUrl = nodePlan.Node.PublishPort || nodePlan.Node.IsEntry
                ? BuildAdminUrl(publicHost, container.PublicPort ?? 0, nodePlan.Node.ExposePort)
                : null;

            await context.PenetrationRuntimeNodes.AddAsync(new PenetrationRuntimeNode
            {
                EnvironmentId = environment.Id,
                TopologyNodeId = nodePlan.Node.Id,
                TopologyNodeKey = nodePlan.Node.TopologyKey,
                ContainerId = container.Id,
                NetworkName = nodePlan.PrimaryInterface?.NetworkName ?? string.Empty,
                IpAddress = nodePlan.PrimaryInterface?.IpAddress ?? container.IP,
                InterfaceSummary = JsonSerializer.Serialize(nodePlan.Interfaces, JsonOptions),
                PublicPort = container.PublicPort,
                AdminAccessUrl = adminUrl,
                Status = container.Status == ContainerStatus.Running
                    ? PenetrationRuntimeStatus.Running
                    : PenetrationRuntimeStatus.Failed
            }, token);

            if (container.Status != ContainerStatus.Running)
            {
                success = false;
                failureMessages.Add($"节点“{nodePlan.Node.Name}”未进入运行状态。");
            }
        }

        if (!success)
        {
            environment.LastError = failureMessages.Count == 0
                ? "部分节点部署失败。"
                : string.Join('\n', failureMessages);
            await context.SaveChangesAsync(token);
            await DestroyEnvironment(environment, token);
            environment.Status = PenetrationRuntimeStatus.Failed;
            environment.LastError = $"{environment.LastError}\n已清理残留资源。";
            environment.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(token);
            return (false, "队伍环境部署失败，已清理残留资源。");
        }

        environment.Status = PenetrationRuntimeStatus.Running;
        environment.LastError = null;
        environment.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        return (true, rebuild ? "渗透环境已重建。" : "渗透环境已部署。");
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
        config.BaseCidr = string.IsNullOrWhiteSpace(model.BaseCidr) ? "10.60.0.0/12" : model.BaseCidr.Trim();
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
                NodeType = nodeModel.NodeType,
                ImageTemplateId = nodeModel.ImageTemplateId,
                ImageName = CleanNullable(nodeModel.ImageName),
                CpuCount = Math.Clamp(nodeModel.CpuCount, 1, 128),
                MemoryLimit = Math.Clamp(nodeModel.MemoryLimit, 64, 262144),
                StorageLimit = Math.Clamp(nodeModel.StorageLimit, 64, 1048576),
                ExposePort = Math.Clamp(nodeModel.ExposePort, 1, 65535),
                IsEntry = nodeModel.IsEntry,
                PublishPort = nodeModel.PublishPort || nodeModel.IsEntry,
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

        config.BaseCidr = string.IsNullOrWhiteSpace(model.BaseCidr) ? "10.60.0.0/12" : model.BaseCidr.Trim();
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

        contextRemoveEdges(config);
        contextRemoveInterfaces(config);

        var removableNodes = config.Nodes.Where(n => !incomingNodeKeys.Contains(n.TopologyKey)).ToList();
        var blockedScore = removableNodes
            .SelectMany(n => n.ScoreItems)
            .FirstOrDefault(i => submittedScoreItemIds.Contains(i.Id));
        if (blockedScore is not null)
            throw new InvalidOperationException($"得分项“{blockedScore.Title}”已有提交记录，请先停止并归档环境后再删除。");

        foreach (var node in removableNodes)
            config.Nodes.Remove(node);

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

            node.Network = network;
            node.NetworkId = network.Id;
            node.Name = Clean(nodeModel.Name, "未命名节点");
            node.Description = CleanNullable(nodeModel.Description);
            node.NodeType = nodeModel.NodeType;
            node.ImageTemplateId = nodeModel.ImageTemplateId;
            node.ImageName = CleanNullable(nodeModel.ImageName);
            node.CpuCount = Math.Clamp(nodeModel.CpuCount, 1, 128);
            node.MemoryLimit = Math.Clamp(nodeModel.MemoryLimit, 64, 262144);
            node.StorageLimit = Math.Clamp(nodeModel.StorageLimit, 64, 1048576);
            node.ExposePort = Math.Clamp(nodeModel.ExposePort, 1, 65535);
            node.IsEntry = nodeModel.IsEntry;
            node.PublishPort = nodeModel.PublishPort || nodeModel.IsEntry;
            node.StaticIp = CleanNullable(nodeModel.StaticIp);
            node.EnvironmentVariables = JsonSerializer.Serialize(nodeModel.EnvironmentVariables ?? [], JsonOptions);
            node.StartCommand = CleanNullable(nodeModel.StartCommand);
            node.HealthCheck = CleanNullable(nodeModel.HealthCheck);
            node.ReservedAdRole = CleanNullable(nodeModel.ReservedAdRole);
            node.PositionX = nodeModel.PositionX;
            node.PositionY = nodeModel.PositionY;
            node.OrderIndex = nodeModel.OrderIndex;

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
            foreach (var interfaceModel in interfaces.OrderBy(i => i.OrderIndex))
            {
                var targetNetworkModelId = networkMap.ContainsKey(interfaceModel.NetworkId)
                    ? interfaceModel.NetworkId
                    : nodeModel.NetworkId;
                if (!networkMap.TryGetValue(targetNetworkModelId, out var network))
                    network = config.Networks.OrderBy(n => n.OrderIndex).First();

                node.Interfaces.Add(new PenetrationInterface
                {
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
                });
            }
        }

        return new TopologyModelMaps(networkMap, nodeMap, scoreKeyByModelId, false);

        static void contextRemoveEdges(PenetrationConfig config)
        {
            config.Edges.Clear();
        }

        static void contextRemoveInterfaces(PenetrationConfig config)
        {
            foreach (var node in config.Nodes)
                node.Interfaces.Clear();
        }
    }

    static void AddEdgesToConfig(PenetrationConfig config, PenetrationConfigModel model, TopologyModelMaps maps)
    {
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

            config.Edges.Add(new PenetrationEdge
            {
                Id = maps.PreserveModelIds ? edgeModel.Id : 0,
                ConfigId = config.Id,
                Config = config,
                TopologyKey = EnsureTopologyKey(edgeModel.TopologyKey, "edge", edgeModel.Id),
                SourceNodeId = sourceNode?.Id ?? 0,
                TargetNodeId = targetNode?.Id ?? 0,
                SourceKind = edgeModel.SourceKind,
                SourceId = sourceId,
                TargetKind = edgeModel.TargetKind,
                TargetId = targetId,
                Protocol = edgeModel.Protocol,
                PortRange = Clean(edgeModel.PortRange, "any"),
                PolicyAction = edgeModel.PolicyAction,
                IsRouteHint = edgeModel.IsRouteHint,
                Label = CleanNullable(edgeModel.Label),
                Description = CleanNullable(edgeModel.Description)
            });
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

    async Task DestroyEnvironment(PenetrationTeamEnvironment environment, CancellationToken token)
    {
        var networkNames = new HashSet<string>(StringComparer.Ordinal);
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
                    await containerManager.DestroyContainerAsync(runtime.Container, token);
                    context.Containers.Remove(runtime.Container);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to destroy penetration container {ContainerId}", runtime.ContainerId);
                }
            }

            runtime.ContainerId = null;
            runtime.Status = PenetrationRuntimeStatus.Stopped;
        }

        context.PenetrationRuntimeNodes.RemoveRange(environment.RuntimeNodes);
        environment.Status = PenetrationRuntimeStatus.Stopped;
        environment.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);

        foreach (var networkName in networkNames)
        {
            try
            {
                await RemoveRuntimeNetwork(environment.NodeId, networkName, token);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to remove penetration network {NetworkName}", networkName);
            }
        }
    }

    async Task RemoveRuntimeNetwork(Guid? nodeId, string networkName, CancellationToken token)
    {
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
            AddDuplicateKeyErrors(result, "安全域", config.Networks.Select(n => (n.Name, n.TopologyKey)));
            var maxSegments = 1 << Math.Max(0, config.NetworkSubnetPrefix - config.TeamSubnetPrefix);
            if (config.Networks.Count > maxSegments)
                result.Errors.Add($"当前队伍网段最多可切分 {maxSegments} 个安全域，请调整前缀或减少安全域数量。");
        }

        if (config.Nodes.Count == 0)
            result.Errors.Add("至少需要一个资产节点。");
        else
            AddDuplicateKeyErrors(result, "资产节点", config.Nodes.Select(n => (n.Name, n.TopologyKey)));

        if (config.Nodes.All(n => !n.IsEntry && !n.PublishPort))
            result.Errors.Add("至少需要一个入口节点或公开端口节点。");

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

            if (edge.PolicyAction == PenetrationPolicyAction.Allow && edge.IsRouteHint &&
                edge.SourceKind == PenetrationPolicyScope.Node && edge.TargetKind == PenetrationPolicyScope.Node)
            {
                var source = config.Nodes.FirstOrDefault(n => n.Id == edge.SourceId || n.Id == edge.SourceNodeId);
                var target = config.Nodes.FirstOrDefault(n => n.Id == edge.TargetId || n.Id == edge.TargetNodeId);
                if (source is null || target is null)
                    result.Errors.Add($"访问策略“{edge.Label ?? edge.Id.ToString()}”引用了不存在的节点。");
            }
        }

        if (config.Edges.Count > 0)
            result.Warnings.Add("访问策略会进入部署计划、选手拓扑和任务链；当前运行期隔离由安全域 Docker 网络与多网卡边界实现，尚未生成端口级 ACL。");

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
                IsInternal = IsInternalNetwork(n.Network)
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
                    IsInternal = IsInternalNetwork(i.Network)
                }).ToList()
            }).ToList(),
            Policies = config.Edges.OrderBy(e => e.Id).Select(e => new PenetrationPlanPolicyModel
            {
                PolicyId = e.Id,
                Label = string.IsNullOrWhiteSpace(e.Label) ? "访问路径" : e.Label,
                Source = ResolvePolicyName(config, e.SourceKind, e.SourceId, e.SourceNodeId),
                Target = ResolvePolicyName(config, e.TargetKind, e.TargetId, e.TargetNodeId),
                Protocol = e.Protocol,
                PortRange = e.PortRange,
                Action = e.PolicyAction,
                IsRouteHint = e.IsRouteHint
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
                "按安全域创建 Docker bridge 网络并写入 IPAM 子网，非入口安全域默认创建为内网隔离网络。",
                "按节点网卡配置创建容器主网卡和附加网卡。",
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
        var networks = config.Networks.OrderBy(n => n.OrderIndex).Select(n => new RuntimeNetworkPlan(
            n,
            networkNames[n.Id],
            networkSubnets.GetValueOrDefault(networkNames[n.Id]) ?? AllocateSubnet(config.BaseCidr, config.NetworkSubnetPrefix, n.OrderIndex)
        )).ToList();

        var runtimeInterfaces = BuildRuntimeInterfaces(config, teamIndex, networkNames, networkSubnets);
        var nodes = new List<RuntimeNodePlan>();
        foreach (var node in config.Nodes.OrderBy(n => n.OrderIndex))
        {
            var image = await ResolveImage(node, token) ?? string.Empty;
            var flagMap = BuildFlagMap(node, config.GameId, teamId, config.PublishedVersion);
            var interfaces = runtimeInterfaces.Where(i => i.Node.Id == node.Id)
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.OrderIndex)
                .ToList();
            nodes.Add(new RuntimeNodePlan(node, image, interfaces, flagMap));
        }

        return new RuntimePlan(networks, nodes);
    }

    ContainerConfig BuildContainerConfig(RuntimeNodePlan nodePlan, int teamId, Guid workerId)
    {
        var envVars = DeserializeDictionary(nodePlan.Node.EnvironmentVariables);
        InjectFlagEnvironmentVariables(envVars, nodePlan.Node.ScoreItems, nodePlan.FlagMap);
        var attachments = nodePlan.Interfaces.Select(i => new ContainerNetworkAttachment
        {
            NetworkName = i.NetworkName,
            SubnetCidr = i.Cidr,
            IPAddress = i.IpAddress,
            IsPrimary = i.IsPrimary,
            IsInternal = IsInternalNetwork(i.Network)
        }).ToList();
        var primary = nodePlan.PrimaryInterface;

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
            EnvironmentVariables = envVars,
            StartCommand = nodePlan.Node.StartCommand,
            HealthCheck = nodePlan.Node.HealthCheck,
            PreferredNodeId = workerId
        };
    }

    async Task<WorkerNode?> SelectWorkerNode(int requiredContainers, CancellationToken token)
    {
        var nodes = await context.WorkerNodes
            .Where(n => n.IsSchedulable)
            .ToArrayAsync(token);

        return nodes
            .Where(n => WeightedScheduler.CanHost(n, NodeCapability.Docker))
            .Where(n => n.MaxContainers - n.CurrentContainers >= requiredContainers)
            .OrderBy(n => n.CpuLoad)
            .ThenBy(n => n.MemoryLoad)
            .ThenBy(n => n.CurrentContainers)
            .FirstOrDefault();
    }

    async Task<(bool Success, string Message)> CheckFleetCapacity(int requiredContainersPerTeam, int teamCount,
        CancellationToken token)
    {
        if (teamCount == 0)
            return (true, "暂无参赛队伍。");

        var nodes = await context.WorkerNodes.AsNoTracking()
            .Where(n => n.IsSchedulable)
            .ToArrayAsync(token);

        var capacity = nodes
            .Where(n => WeightedScheduler.CanHost(n, NodeCapability.Docker))
            .Select(n => Math.Max(0, n.MaxContainers - n.CurrentContainers))
            .Sum(available => available / Math.Max(1, requiredContainersPerTeam));

        return capacity >= teamCount
            ? (true, $"容量检查通过，可部署 {capacity} 支队伍。")
            : (false, $"Docker 节点容量不足：当前最多可部署 {capacity} 支队伍，需要 {teamCount} 支队伍。");
    }

    async Task<string?> ResolveImage(PenetrationNode node, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(node.ImageName))
            return node.ImageName;

        if (node.ImageTemplateId is not { } templateId)
            return null;

        var template = await context.ImageTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == templateId, token);
        return template is { OSType: OSType.Linux, ImageType: ImageType.Docker, Status: ImageStatus.Ready }
            ? ResolveDockerImageReference(template.RegistryUrl, template.Name)
            : null;
    }

    static string ResolveDockerImageReference(string? registryUrl, string imageName)
    {
        var image = imageName.Trim();
        var registry = registryUrl?.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(registry))
            return image;

        var lastSlash = registry.LastIndexOf('/');
        var lastSegment = lastSlash >= 0 ? registry[(lastSlash + 1)..] : registry;
        var looksLikeFullImage = registry.Contains('@') || lastSegment.Contains(':');

        return looksLikeFullImage ? registry : $"{registry}/{image}";
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
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.Container)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.TopologyNode).ThenInclude(n => n.Interfaces).ThenInclude(i => i.Network)
            .Include(e => e.RuntimeNodes).ThenInclude(r => r.TopologyNode).ThenInclude(n => n.ScoreItems)
            .FirstOrDefaultAsync(e => e.GameId == gameId && e.TeamId == teamId, token);

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
                    NodeType = n.NodeType,
                    ImageTemplateId = n.ImageTemplateId,
                    ImageName = n.ImageName,
                    CpuCount = n.CpuCount,
                    MemoryLimit = n.MemoryLimit,
                    StorageLimit = n.StorageLimit,
                    ExposePort = n.ExposePort,
                    IsEntry = n.IsEntry,
                    PublishPort = n.PublishPort,
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

    sealed record RuntimePlan(List<RuntimeNetworkPlan> Networks, List<RuntimeNodePlan> Nodes);

    sealed record RuntimeNetworkPlan(PenetrationNetwork Network, string NetworkName, string Cidr);

    sealed record RuntimeNodePlan(
        PenetrationNode Node,
        string Image,
        List<RuntimeInterfacePlan> Interfaces,
        Dictionary<int, string> FlagMap)
    {
        public RuntimeInterfacePlan? PrimaryInterface => Interfaces.FirstOrDefault(i => i.IsPrimary) ?? Interfaces.FirstOrDefault();
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
        bool IsManagement);

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
