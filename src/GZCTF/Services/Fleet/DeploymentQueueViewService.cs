using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Models.Data;
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
    public DeploymentQueueKind Kind { get; set; }
    public RuntimeOperationKind Operation { get; set; }
    public DeploymentStage Stage { get; set; }
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
    public string? StageMessage { get; set; }
    public string? BlockedReasonCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class DeploymentQueueViewService(AppDbContext context)
{
    public async Task<DeploymentQueueListResult> ListAsync(string? status, string? cursor, int pageSize,
        CancellationToken token)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var decoded = string.IsNullOrWhiteSpace(cursor) ? (GuidTimeCursor?)null : GuidTimeCursor.Decode(cursor);
        var query = ApplyStatus(context.DeploymentQueueTickets.AsNoTracking().Include(item => item.TargetNode), status);
        if (decoded is { } value)
            query = query.Where(item => item.CreatedAt < value.Time ||
                                        item.CreatedAt == value.Time && item.Id.CompareTo(value.Id) < 0);

        var rows = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Take(pageSize + 1).ToListAsync(token);
        var page = rows.Take(pageSize).ToArray();
        var pendingPositions = await LoadPendingPositionsAsync(token);
        var lookup = await LoadLookupAsync(page, token);

        return new DeploymentQueueListResult
        {
            Items = page.Select(ticket => BuildItem(ticket, pendingPositions.GetValueOrDefault(ticket.Id), lookup))
                .ToList(),
            NextCursor = rows.Count > pageSize && page.Length > 0
                ? new GuidTimeCursor(page[^1].CreatedAt, page[^1].Id).Encode()
                : null
        };
    }

    static IQueryable<DeploymentQueueTicket> ApplyStatus(IQueryable<DeploymentQueueTicket> query, string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pending" or "queued" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Pending),
            "scheduling" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Scheduling),
            "assigned" or "scheduled" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Scheduled),
            "creating" or "running" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Running),
            "completed" or "success" or "succeeded" =>
                query.Where(item => item.Status == DeploymentQueueTicketStatus.Succeeded),
            "failed" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Failed),
            "cancelled" or "canceled" => query.Where(item => item.Status == DeploymentQueueTicketStatus.Cancelled),
            _ => query
        };
    }

    async Task<Dictionary<Guid, int>> LoadPendingPositionsAsync(CancellationToken token)
    {
        var ids = await context.DeploymentQueueTickets.AsNoTracking()
            .Where(item => item.Status == DeploymentQueueTicketStatus.Pending)
            .OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
            .Select(item => item.Id).ToArrayAsync(token);
        return ids.Select((id, index) => (id, position: index + 1)).ToDictionary(item => item.id,
            item => item.position);
    }

    async Task<QueueLookup> LoadLookupAsync(IReadOnlyCollection<DeploymentQueueTicket> rows,
        CancellationToken token)
    {
        var gameIds = rows.Select(item => item.GameId).OfType<int>().Distinct().ToArray();
        var challengeIds = rows.Select(item => item.ChallengeId).OfType<int>().Distinct().ToArray();
        var teamIds = rows.Select(item => item.OwnerTeamId).OfType<int>().Distinct().ToArray();
        var userIds = rows.Select(item => item.OwnerUserId).OfType<Guid>().Distinct().ToArray();

        var games = await context.Games.AsNoTracking().Where(item => gameIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Title, token);
        var gameChallenges = await context.GameChallenges.AsNoTracking()
            .Where(item => challengeIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Title, item.ContainerImage, item.ImageTemplateId })
            .ToDictionaryAsync(item => item.Id, token);
        var exercises = await context.ExerciseChallenges.AsNoTracking()
            .Where(item => challengeIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Title, item.ContainerImage })
            .ToDictionaryAsync(item => item.Id, token);
        var teams = await context.Teams.AsNoTracking().Where(item => teamIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var users = await context.Users.AsNoTracking().Where(item => userIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.UserName, token);

        return new QueueLookup(games, gameChallenges.ToDictionary(item => item.Key,
                item => new ChallengeLookup(item.Value.Title, item.Value.ContainerImage,
                    item.Value.ImageTemplateId)),
            exercises.ToDictionary(item => item.Key,
                item => new ChallengeLookup(item.Value.Title, item.Value.ContainerImage, null)), teams, users);
    }

    static DeploymentQueueItemModel BuildItem(DeploymentQueueTicket ticket, int queuePosition, QueueLookup lookup)
    {
        var gameLabel = ticket.GameId is { } gameId
            ? lookup.Games.TryGetValue(gameId, out var title) ? $"{title} #{gameId}" : $"比赛 #{gameId}"
            : null;
        var challenge = ticket.ChallengeId is { } challengeId
            ? (ticket.Kind is DeploymentQueueKind.ExerciseContainer or DeploymentQueueKind.TrainingContainer
                ? lookup.Exercises.GetValueOrDefault(challengeId)
                : lookup.GameChallenges.GetValueOrDefault(challengeId))
            : null;
        var challengeLabel = challenge is null || ticket.ChallengeId is not { } id
            ? null
            : $"{challenge.Title} #{id}";
        var ownerLabel = ticket.SubjectDisplayName ?? ResolveOwner(ticket, lookup);
        var statusKey = StatusKey(ticket.Status);
        var requestParts = new[] { ownerLabel, gameLabel, challengeLabel, ticket.ResourceDisplayName }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();

        return new DeploymentQueueItemModel
        {
            Id = ticket.Id,
            Kind = ticket.Kind,
            Operation = ticket.Operation,
            Stage = ticket.Stage,
            ActionLabel = OperationLabel(ticket.Operation),
            TypeLabel = WorkloadLabel(ticket.Kind),
            RequestLabel = requestParts.Length > 0 ? string.Join(" / ", requestParts) : ticket.Id.ToString("N")[..8],
            OwnerLabel = ownerLabel,
            GameLabel = gameLabel,
            ChallengeLabel = challengeLabel,
            Image = challenge?.Image,
            TargetNodeId = ticket.TargetNodeId,
            TargetNodeName = ticket.TargetNode?.Name,
            TargetNodeHost = ticket.TargetNode?.HostAddress,
            TargetNodeLabel = NodeLabel(ticket),
            Status = (int)ticket.Status,
            StatusKey = statusKey,
            StatusLabel = StatusLabel(statusKey),
            DockerSlots = ticket.DockerSlots,
            VmSlots = ticket.VmSlots,
            QueuePosition = ticket.Status == DeploymentQueueTicketStatus.Pending ? queuePosition : 0,
            PeopleAhead = ticket.Status == DeploymentQueueTicketStatus.Pending ? Math.Max(0, queuePosition - 1) : 0,
            StageMessage = ticket.StageMessage,
            BlockedReasonCode = ticket.BlockedReasonCode,
            ErrorMessage = ticket.ErrorMessage,
            CreatedAt = ticket.CreatedAt,
            StartedAt = ticket.StartedAt,
            CompletedAt = ticket.CompletedAt
        };
    }

    static string? ResolveOwner(DeploymentQueueTicket ticket, QueueLookup lookup)
    {
        if (ticket.OwnerTeamId is { } teamId)
            return lookup.Teams.TryGetValue(teamId, out var team) ? $"{team} #{teamId}" : $"队伍 #{teamId}";
        if (ticket.OwnerUserId is { } userId)
            return lookup.Users.TryGetValue(userId, out var user) ? user : userId.ToString("N")[..8];
        return null;
    }

    static string NodeLabel(DeploymentQueueTicket ticket)
    {
        if (!string.IsNullOrWhiteSpace(ticket.TargetNode?.Name) &&
            !string.IsNullOrWhiteSpace(ticket.TargetNode.HostAddress))
            return $"{ticket.TargetNode.Name} ({ticket.TargetNode.HostAddress})";
        if (!string.IsNullOrWhiteSpace(ticket.TargetNode?.Name))
            return ticket.TargetNode.Name;
        if (!string.IsNullOrWhiteSpace(ticket.TargetNode?.HostAddress))
            return ticket.TargetNode.HostAddress;
        return ticket.TargetNodeId is { } id ? $"节点 {id.ToString("N")[..8]}" : "未分配";
    }

    static string WorkloadLabel(DeploymentQueueKind kind) => kind switch
    {
        DeploymentQueueKind.VirtualMachine => "VM",
        DeploymentQueueKind.TeamLabRuntime => "TeamLab",
        _ => "Docker"
    };

    static string OperationLabel(RuntimeOperationKind operation) => operation switch
    {
        RuntimeOperationKind.Create => "创建",
        RuntimeOperationKind.Extend => "延期",
        RuntimeOperationKind.Stop => "停止",
        RuntimeOperationKind.Reset => "重置",
        RuntimeOperationKind.Destroy => "销毁",
        _ => operation.ToString()
    };

    static string StatusKey(DeploymentQueueTicketStatus status) => status switch
    {
        DeploymentQueueTicketStatus.Pending => "pending",
        DeploymentQueueTicketStatus.Scheduling => "scheduling",
        DeploymentQueueTicketStatus.Scheduled => "scheduled",
        DeploymentQueueTicketStatus.Running => "running",
        DeploymentQueueTicketStatus.Succeeded => "completed",
        DeploymentQueueTicketStatus.Failed => "failed",
        DeploymentQueueTicketStatus.Cancelled => "cancelled",
        _ => "pending"
    };

    static string StatusLabel(string status) => status switch
    {
        "pending" => "等待中",
        "scheduling" => "调度中",
        "scheduled" => "已分配",
        "running" => "执行中",
        "completed" => "已完成",
        "failed" => "失败",
        "cancelled" => "已取消",
        _ => status
    };

    sealed record ChallengeLookup(string Title, string? Image, int? ImageTemplateId);
    sealed record QueueLookup(
        IReadOnlyDictionary<int, string> Games,
        IReadOnlyDictionary<int, ChallengeLookup> GameChallenges,
        IReadOnlyDictionary<int, ChallengeLookup> Exercises,
        IReadOnlyDictionary<int, string> Teams,
        IReadOnlyDictionary<Guid, string?> Users);
}
