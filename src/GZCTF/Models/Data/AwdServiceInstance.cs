using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Data;

public class AwdServiceInstance
{
    [Key]
    public int Id { get; set; }

    public int ServiceId { get; set; }
    public AwdService Service { get; set; } = null!;

    public int TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public int? ContainerId { get; set; }
    public Container? Container { get; set; }

    public string NetworkName { get; set; } = string.Empty;
    public bool IsRunning { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
