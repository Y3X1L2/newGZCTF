using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Infrastructure.Persistence.Queries;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Services.Fleet;

public sealed class DeploymentQueueListResult
{
    public List<DeploymentQueueItemModel> Items { get; set; } = [];
    public string? NextCursor { get; set; }
}

public sealed class DeploymentQueueItemModel
{
    public Guid Id { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? TargetId { get; set; }
    public DeploymentQueueKind? Kind { get; set; }
    public TargetType? Type { get; set; }
    public TargetAction Action { get; set; }
    public string ActionLabel { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;
    public string RequestLabel { get; set; } = string.Empty;
    public string? OwnerLabel { get; set; }
    public string? GameLabel { get; set; }
    public string? ChallengeLabel { get; set; }
    public string? Image { get; set; }
    public Guid? TargetNodeId { get; set; }
    public string? TargetNodeName { get; set; }
    public string? TargetNodeHost { get; set; }
    public string TargetNodeLabel { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string StatusKey { get; set; } = string.Empty;
    public int Status { get; set; }
    public int DockerSlots { get; set; }
    public int VmSlots { get; set; }
    public int QueuePosition { get; set; }
    public int PeopleAhead { get; set; }
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class DeploymentQueueViewService(AppDbContext context)
{
    static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<DeploymentQueueListResult> ListAsync(string? status, string? cursor, int pageSize,
        CancellationToken token)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedStatus = NormalizeStatusFilter(status);
        var decoded = string.IsNullOrWhiteSpace(cursor) ? (GuidTimeCursor?)null : GuidTimeCursor.Decode(cursor);

        var ticketQuery = context.DeploymentQueueTickets
            .AsNoTracking()
            .Include(t => t.TargetNode)
            .Include(t => t.DeploymentTarget).ThenInclude(t => t!.TargetNode)
            .AsQueryable();
        ticketQuery = ApplyTicketStatus(ticketQuery, normalizedStatus);
        if (decoded is { } ticketCursor)
            ticketQuery = ticketQuery.Where(item => item.CreatedAt < ticketCursor.Time ||
                item.CreatedAt == ticketCursor.Time && item.Id.CompareTo(ticketCursor.Id) < 0);
        var tickets = await ticketQuery
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(pageSize + 1)
            .ToListAsync(token);

        var targetQuery = context.DeploymentTargets
            .AsNoTracking()
            .Include(t => t.TargetNode)
            .Where(target => !context.DeploymentQueueTickets.Any(ticket =>
                ticket.DeploymentTargetId == target.Id))
            .AsQueryable();
        targetQuery = ApplyTargetStatus(targetQuery, normalizedStatus);
        if (decoded is { } targetCursor)
            targetQuery = targetQuery.Where(item => item.CreatedAt < targetCursor.Time ||
                item.CreatedAt == targetCursor.Time && item.Id.CompareTo(targetCursor.Id) < 0);
        var targets = await targetQuery
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(pageSize + 1)
            .ToListAsync(token);

        var pendingTickets = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(item => item.Status == DeploymentQueueTicketStatus.Pending)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Select(item => item.Id)
            .ToArrayAsync(token);
        var pendingPositions = pendingTickets
            .Select((id, index) => new { Id = id, Position = index + 1 })
            .ToDictionary(item => item.Id, item => item.Position);

        var rows = tickets.Select(t => DeploymentQueueSource.FromTicket(t,
                pendingPositions.GetValueOrDefault(t.Id)))
            .Concat(targets.Select(DeploymentQueueSource.FromTarget))
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Take(pageSize + 1)
            .ToArray();

        var pageRows = rows.Take(pageSize).ToArray();
        var lookup = await BuildLookupAsync(pageRows, token);
        var items = pageRows.Select(row => BuildItem(row, lookup)).ToList();
        var nextCursor = rows.Length > pageSize && pageRows.Length > 0
            ? new GuidTimeCursor(pageRows[^1].CreatedAt, pageRows[^1].Id).Encode()
            : null;

        return new DeploymentQueueListResult
        {
            Items = items,
            NextCursor = nextCursor
        };
    }

    static IQueryable<DeploymentQueueTicket> ApplyTicketStatus(
        IQueryable<DeploymentQueueTicket> query,
        string? status) => status switch
    {
        "pending" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Pending),
        "assigned" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Assigned),
        "running" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Creating),
        "completed" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Completed),
        "failed" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Failed),
        "cancelled" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Cancelled),
        _ => query
    };

    static IQueryable<DeploymentTarget> ApplyTargetStatus(
        IQueryable<DeploymentTarget> query,
        string? status) => status switch
    {
        "pending" => query.Where(item => item.Status == TargetStatus.Pending),
        "assigned" => query.Where(item => item.Status == TargetStatus.Assigned),
        "running" => query.Where(item => item.Status == TargetStatus.Creating || item.Status == TargetStatus.Running),
        "completed" => query.Where(item => item.Status == TargetStatus.Completed),
        "failed" => query.Where(item => item.Status == TargetStatus.Failed),
        "cancelled" => query.Where(item => item.Status == TargetStatus.Cancelled),
        _ => query
    };

    static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        var value = status.Trim().ToLowerInvariant();
        if (int.TryParse(value, out var numeric))
        {
            if (Enum.IsDefined(typeof(TargetStatus), numeric))
                return TargetStatusToKey((TargetStatus)numeric);
            if (Enum.IsDefined(typeof(DeploymentQueueTicketStatus), numeric))
                return TicketStatusToKey((DeploymentQueueTicketStatus)numeric);
        }

        return value switch
        {
            "pending" or "queued" => "pending",
            "assigned" => "assigned",
            "creating" or "running" => "running",
            "completed" or "success" => "completed",
            "failed" => "failed",
            "cancelled" or "canceled" => "cancelled",
            _ => null
        };
    }

    async Task<DeploymentQueueLookup> BuildLookupAsync(IReadOnlyCollection<DeploymentQueueSource> rows,
        CancellationToken token)
    {
        var gameIds = rows.Select(r => r.GameId).OfType<int>().Distinct().ToArray();
        var challengeIds = rows.Select(r => r.ChallengeId).OfType<int>().Distinct().ToArray();
        var teamIds = rows.Select(r => r.OwnerTeamId).OfType<int>().Distinct().ToArray();
        var userIds = rows.Select(r => r.OwnerUserId).OfType<Guid>().Distinct().ToArray();
        var templateIds = rows.Select(r => r.ImageTemplateId).OfType<int>().Distinct().ToArray();
        var runtimeIds = rows.Select(r => r.TeamLabRuntimeId).OfType<int>().Distinct().ToArray();

        var games = await context.Games.AsNoTracking()
            .Where(g => gameIds.Contains(g.Id))
            .Select(g => new { g.Id, g.Title })
            .ToDictionaryAsync(g => g.Id, g => g.Title, token);
        var challenges = await context.GameChallenges.AsNoTracking()
            .Where(c => challengeIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Title })
            .ToDictionaryAsync(c => c.Id, c => c.Title, token);
        var teams = await context.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, token);
        var users = await context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName, token);
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(t => templateIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.RegistryUrl, t.LocalFilePath })
            .ToDictionaryAsync(t => t.Id, t => ResolveTemplateLabel(t.Name, t.RegistryUrl, t.LocalFilePath), token);
        var runtimes = await (
                from binding in context.PenetrationTeamRuntimeBindings.AsNoTracking()
                join game in context.Games.AsNoTracking() on binding.GameId equals game.Id
                join team in context.Teams.AsNoTracking() on binding.TeamId equals team.Id
                where runtimeIds.Contains(binding.RuntimeId)
                select new { binding.RuntimeId, GameTitle = game.Title, TeamName = team.Name })
            .ToDictionaryAsync(r => r.RuntimeId, r => (r.GameTitle, r.TeamName), token);

        return new DeploymentQueueLookup(games, challenges, teams, users, templates, runtimes);
    }

    static DeploymentQueueItemModel BuildItem(DeploymentQueueSource row, DeploymentQueueLookup lookup)
    {
        var game = row.GameId is { } gameId && lookup.Games.TryGetValue(gameId, out var gameTitle)
            ? $"{gameTitle} #{gameId}"
            : row.GameId is null ? null : $"比赛 #{row.GameId}";
        var challenge = row.ChallengeId is { } challengeId &&
                        lookup.Challenges.TryGetValue(challengeId, out var challengeTitle)
            ? $"{challengeTitle} #{challengeId}"
            : row.ChallengeId is null ? null : $"题目 #{row.ChallengeId}";
        var owner = ResolveOwner(row, lookup);
        var image = row.Image;
        if (string.IsNullOrWhiteSpace(image) && row.ImageTemplateId is { } templateId &&
            lookup.Templates.TryGetValue(templateId, out var templateLabel))
            image = templateLabel;
        if (row.TeamLabRuntimeId is { } runtimeId && lookup.Runtimes.TryGetValue(runtimeId, out var runtime))
        {
            game ??= runtime.GameTitle;
            owner ??= runtime.TeamName;
        }

        var nodeLabel = ResolveNodeLabel(row.TargetNodeName, row.TargetNodeHost, row.TargetNodeId);
        var requestLabel = BuildRequestLabel(row, owner, game, challenge);

        return new DeploymentQueueItemModel
        {
            Id = row.Id,
            TicketId = row.TicketId,
            TargetId = row.TargetId,
            Kind = row.Kind,
            Type = row.Type,
            Action = row.Action,
            ActionLabel = ActionLabel(row.Action, row.Operation),
            TypeLabel = TypeLabel(row),
            RequestLabel = requestLabel,
            OwnerLabel = owner,
            GameLabel = game,
            ChallengeLabel = challenge,
            Image = image,
            TargetNodeId = row.TargetNodeId,
            TargetNodeName = row.TargetNodeName,
            TargetNodeHost = row.TargetNodeHost,
            TargetNodeLabel = nodeLabel,
            Status = row.StatusValue,
            StatusKey = row.StatusKey,
            StatusLabel = StatusLabel(row.StatusKey),
            DockerSlots = row.DockerSlots,
            VmSlots = row.VmSlots,
            QueuePosition = row.QueuePosition,
            PeopleAhead = Math.Max(0, row.QueuePosition - 1),
            Result = row.Result,
            ErrorMessage = row.ErrorMessage,
            CreatedAt = row.CreatedAt,
            StartedAt = row.StartedAt,
            CompletedAt = row.CompletedAt
        };
    }

    static string? ResolveOwner(DeploymentQueueSource row, DeploymentQueueLookup lookup)
    {
        if (row.OwnerTeamId is { } teamId)
            return lookup.Teams.TryGetValue(teamId, out var teamName) ? $"{teamName} #{teamId}" : $"队伍 #{teamId}";
        if (row.OwnerUserId is { } userId)
            return lookup.Users.TryGetValue(userId, out var userName) ? userName ?? userId.ToString("N")[..8] : userId.ToString("N")[..8];
        return null;
    }

    static string BuildRequestLabel(DeploymentQueueSource row, string? owner, string? game, string? challenge)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(owner)) parts.Add(owner);
        if (!string.IsNullOrWhiteSpace(game)) parts.Add(game);
        if (!string.IsNullOrWhiteSpace(challenge)) parts.Add(challenge);
        if (row.TeamLabRuntimeId is { } runtimeId) parts.Add($"TeamLab 环境 #{runtimeId}");
        if (row.VmInstanceId is { } vmId) parts.Add($"VM {vmId.ToString("N")[..8]}");
        return parts.Count > 0 ? string.Join(" / ", parts) : row.Id.ToString("N")[..8];
    }

    static string ResolveNodeLabel(string? name, string? host, Guid? id)
    {
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(host))
            return $"{name} ({host})";
        if (!string.IsNullOrWhiteSpace(name))
            return name;
        if (!string.IsNullOrWhiteSpace(host))
            return host;
        return id is null ? "未分配" : $"节点 {id.Value.ToString("N")[..8]}";
    }

    static string TypeLabel(DeploymentQueueSource row) =>
        row.Kind switch
        {
            DeploymentQueueKind.TeamLabRuntime => "TeamLab",
            DeploymentQueueKind.Vm => "VM",
            DeploymentQueueKind.GameContainer or DeploymentQueueKind.ExerciseContainer => "Docker",
            _ => row.Type switch
            {
                TargetType.Docker => "Docker",
                TargetType.Vm => "VM",
                _ => "任务"
            }
        };

    static string ActionLabel(TargetAction action, string? operation)
    {
        if (!string.IsNullOrWhiteSpace(operation))
        {
            if (operation.StartsWith("extend:", StringComparison.OrdinalIgnoreCase))
            {
                var value = operation["extend:".Length..];
                return string.IsNullOrWhiteSpace(value) ? "延期" : $"延期 {value}";
            }

            if (operation.Equals("destroy", StringComparison.OrdinalIgnoreCase))
                return "销毁";
        }

        return action switch
        {
            TargetAction.Create => "创建",
            TargetAction.Start => "启动",
            TargetAction.Destroy => "销毁",
            TargetAction.SnapshotRevert => "快照恢复",
            _ => action.ToString()
        };
    }

    static string StatusLabel(string key) => key switch
    {
        "pending" => "等待中",
        "assigned" => "已分配",
        "running" => "执行中",
        "completed" => "已完成",
        "failed" => "失败",
        "cancelled" => "已取消",
        _ => key
    };

    static string TargetStatusToKey(TargetStatus status) => status switch
    {
        TargetStatus.Pending => "pending",
        TargetStatus.Assigned => "assigned",
        TargetStatus.Creating or TargetStatus.Running => "running",
        TargetStatus.Completed => "completed",
        TargetStatus.Failed => "failed",
        TargetStatus.Cancelled => "cancelled",
        _ => "pending"
    };

    static string TicketStatusToKey(DeploymentQueueTicketStatus status) => status switch
    {
        DeploymentQueueTicketStatus.Pending => "pending",
        DeploymentQueueTicketStatus.Assigned => "assigned",
        DeploymentQueueTicketStatus.Creating => "running",
        DeploymentQueueTicketStatus.Completed => "completed",
        DeploymentQueueTicketStatus.Failed => "failed",
        DeploymentQueueTicketStatus.Cancelled => "cancelled",
        _ => "pending"
    };

    static int StatusCode(string key) => key switch
    {
        "pending" => 0,
        "assigned" => 1,
        "running" => 2,
        "completed" => 3,
        "failed" => 4,
        "cancelled" => 5,
        _ => 0
    };

    static string? ResolveTemplateLabel(string? name, string? registryUrl, string? localFilePath)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name;
        if (!string.IsNullOrWhiteSpace(registryUrl))
            return registryUrl;
        if (!string.IsNullOrWhiteSpace(localFilePath))
            return Path.GetFileName(localFilePath);
        return null;
    }

    static DeploymentPayloadInfo ParsePayload(DeploymentTarget target)
    {
        try
        {
            if (target.Type == TargetType.Docker)
            {
                var lifecycle = JsonSerializer.Deserialize<ContainerLifecyclePayload>(target.Payload, PayloadJsonOptions);
                if (lifecycle is not null && lifecycle.ContainerGuid != Guid.Empty)
                {
                    return new DeploymentPayloadInfo(
                        lifecycle.GameId,
                        lifecycle.TeamId,
                        lifecycle.UserId,
                        lifecycle.ChallengeId,
                        null,
                        null,
                        lifecycle.Image,
                        null,
                        lifecycle.Operation);
                }

                var config = JsonSerializer.Deserialize<ContainerConfig>(target.Payload, PayloadJsonOptions);
                if (config is null)
                    return DeploymentPayloadInfo.Empty;

                return new DeploymentPayloadInfo(
                    config.GameId,
                    int.TryParse(config.TeamId, out var teamId) ? teamId : null,
                    config.TeamId == "exercise" ? config.UserId : null,
                    config.ChallengeId,
                    null,
                    null,
                    config.Image,
                    null,
                    null);
            }

            var destroyPayload = JsonSerializer.Deserialize<VmDestroyPayload>(target.Payload, PayloadJsonOptions);
            if (destroyPayload is not null && destroyPayload.VmInstanceId != Guid.Empty)
                return new DeploymentPayloadInfo(destroyPayload.GameId, null, destroyPayload.UserId,
                    destroyPayload.ChallengeId, destroyPayload.VmInstanceId, null, destroyPayload.VmName, null, "destroy");

            var vm = JsonSerializer.Deserialize<VmPayload>(target.Payload, PayloadJsonOptions);
            return vm is null
                ? DeploymentPayloadInfo.Empty
                : new DeploymentPayloadInfo(vm.GameId, null, vm.UserId, vm.ChallengeId, vm.VmInstanceId, null,
                    vm.TemplatePath, vm.TemplateId, null);
        }
        catch (JsonException)
        {
            return DeploymentPayloadInfo.Empty;
        }
    }

    sealed record DeploymentQueueLookup(
        IReadOnlyDictionary<int, string> Games,
        IReadOnlyDictionary<int, string> Challenges,
        IReadOnlyDictionary<int, string> Teams,
        IReadOnlyDictionary<Guid, string?> Users,
        IReadOnlyDictionary<int, string?> Templates,
        IReadOnlyDictionary<int, (string GameTitle, string TeamName)> Runtimes);

    sealed record DeploymentPayloadInfo(
        int? GameId,
        int? OwnerTeamId,
        Guid? OwnerUserId,
        int? ChallengeId,
        Guid? VmInstanceId,
        int? TeamLabRuntimeId,
        string? Image,
        int? ImageTemplateId,
        string? Operation)
    {
        public static DeploymentPayloadInfo Empty { get; } = new(null, null, null, null, null, null, null, null, null);
    }

    sealed record DeploymentQueueSource(
        Guid Id,
        Guid? TicketId,
        Guid? TargetId,
        DeploymentQueueKind? Kind,
        TargetType? Type,
        TargetAction Action,
        Guid? TargetNodeId,
        string? TargetNodeName,
        string? TargetNodeHost,
        int? OwnerTeamId,
        Guid? OwnerUserId,
        int? GameId,
        int? ChallengeId,
        Guid? VmInstanceId,
        int? TeamLabRuntimeId,
        string? Image,
        int? ImageTemplateId,
        string? Operation,
        int DockerSlots,
        int VmSlots,
        int QueuePosition,
        string StatusKey,
        int StatusValue,
        string? Result,
        string? ErrorMessage,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt)
    {
        public static DeploymentQueueSource FromTicket(DeploymentQueueTicket ticket, int queuePosition)
        {
            var target = ticket.DeploymentTarget;
            var payload = target is null ? DeploymentPayloadInfo.Empty : ParsePayload(target);
            var node = ticket.TargetNode ?? target?.TargetNode;
            var statusKey = TicketStatusToKey(ticket.Status);

            return new DeploymentQueueSource(
                ticket.Id,
                ticket.Id,
                target?.Id,
                ticket.Kind,
                target?.Type,
                target?.Action ?? TargetAction.Create,
                ticket.TargetNodeId ?? target?.TargetNodeId,
                node?.Name,
                node?.HostAddress,
                ticket.OwnerTeamId ?? payload.OwnerTeamId,
                ticket.OwnerUserId ?? payload.OwnerUserId,
                ticket.GameId ?? payload.GameId,
                ticket.ChallengeId ?? payload.ChallengeId,
                ticket.VmInstanceId ?? payload.VmInstanceId,
                ticket.TeamLabRuntimeId ?? payload.TeamLabRuntimeId,
                payload.Image,
                payload.ImageTemplateId,
                payload.Operation,
                ticket.DockerSlots,
                ticket.VmSlots,
                ticket.Status == DeploymentQueueTicketStatus.Pending ? queuePosition : 0,
                statusKey,
                StatusCode(statusKey),
                BuildResult(target),
                ticket.ErrorMessage ?? target?.ErrorMessage,
                ticket.CreatedAt,
                ticket.StartedAt,
                ticket.CompletedAt);
        }

        public static DeploymentQueueSource FromTarget(DeploymentTarget target)
        {
            var payload = ParsePayload(target);
            var statusKey = TargetStatusToKey(target.Status);

            return new DeploymentQueueSource(
                target.Id,
                null,
                target.Id,
                null,
                target.Type,
                target.Action,
                target.TargetNodeId,
                target.TargetNode?.Name,
                target.TargetNode?.HostAddress,
                payload.OwnerTeamId,
                payload.OwnerUserId,
                payload.GameId,
                payload.ChallengeId,
                payload.VmInstanceId,
                payload.TeamLabRuntimeId,
                payload.Image,
                payload.ImageTemplateId,
                payload.Operation,
                target.Type == TargetType.Docker ? 1 : 0,
                target.Type == TargetType.Vm ? 1 : 0,
                0,
                statusKey,
                StatusCode(statusKey),
                BuildResult(target),
                target.ErrorMessage,
                target.CreatedAt,
                target.CreatedAt,
                target.CompletedAt);
        }

        static string? BuildResult(DeploymentTarget? target)
        {
            if (target is null)
                return null;
            if (!string.IsNullOrWhiteSpace(target.ResultHost) && target.ResultPort is > 0)
                return $"{target.ResultHost}:{target.ResultPort}";
            if (!string.IsNullOrWhiteSpace(target.ResultHost))
                return target.ResultHost;
            return target.ResultPort is > 0 ? target.ResultPort.Value.ToString() : null;
        }
    }

    sealed record VmPayload(
        int? TemplateId,
        string? TemplatePath,
        int? Memory,
        int? Cpu,
        string? VmName,
        string? Flag,
        Guid? VmInstanceId,
        int? GameId,
        Guid? UserId,
        int? ChallengeId);

    sealed record VmDestroyPayload(
        Guid VmInstanceId,
        string? VmName,
        int? GameId,
        Guid? UserId,
        int? ChallengeId);

    sealed record ContainerLifecyclePayload(
        Guid ContainerGuid,
        string? ContainerId,
        string? Image,
        int? ChallengeId,
        int? GameId,
        int? TeamId,
        Guid? UserId,
        string? Operation);
}
