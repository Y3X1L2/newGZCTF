using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

public sealed class FleetCapacityReservation
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DeploymentQueueTicketId { get; set; }
    public Guid WorkerNodeId { get; set; }
    public int DockerSlots { get; set; }
    public int VmSlots { get; set; }
    public CapacityReservationStatus Status { get; set; } = CapacityReservationStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DeploymentQueueTicket DeploymentQueueTicket { get; set; } = null!;
    public WorkerNode WorkerNode { get; set; } = null!;
}

public enum CapacityReservationStatus : byte
{
    Active = 0,
    Confirmed = 1,
    Released = 2,
    Expired = 3
}
