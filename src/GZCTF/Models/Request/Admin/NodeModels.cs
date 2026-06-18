using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Data;

namespace GZCTF.Models.Request.Admin;

public class NodeListResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string HostAddress { get; set; } = string.Empty;
    public NodeStatus Status { get; set; }
    public float CpuLoad { get; set; }
    public float MemoryLoad { get; set; }
    public int CurrentContainers { get; set; }
    public int MaxContainers { get; set; }
    public int CurrentVms { get; set; }
    public int MaxVms { get; set; }
    public DateTimeOffset? LastHeartbeat { get; set; }

    public static NodeListResponse FromNode(WorkerNode n) => new()
    {
        Id = n.Id, Name = n.Name, HostAddress = n.HostAddress,
        Status = n.Status, CpuLoad = n.CpuLoad, MemoryLoad = n.MemoryLoad,
        CurrentContainers = n.CurrentContainers, MaxContainers = n.MaxContainers,
        CurrentVms = n.CurrentVms, MaxVms = n.MaxVms, LastHeartbeat = n.LastHeartbeat
    };
}

public class NodeResourceListResponse
{
    public Guid NodeId { get; set; }
    public string NodeName { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int RunningCount { get; set; }
    public int ContainerCount { get; set; }
    public int VmCount { get; set; }
    public List<NodeResourceItemModel> Items { get; set; } = [];
}

public class NodeResourceItemModel
{
    public string Kind { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ExpectedStopAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public string Duration { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string? RuntimeId { get; set; }
    public string? Entry { get; set; }
    public string? Ip { get; set; }
    public int? Port { get; set; }
    public int? GameId { get; set; }
    public string? GameTitle { get; set; }
    public int? ChallengeId { get; set; }
    public string? ChallengeTitle { get; set; }
    public string? ChallengeCategory { get; set; }
    public int? TeamId { get; set; }
    public string? TeamName { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string? ProviderName { get; set; }
    public string? OsType { get; set; }
}
