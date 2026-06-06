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
