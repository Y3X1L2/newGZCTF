using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GZCTF.Models.Data;

[JsonConverter(typeof(JsonStringEnumConverter<TeamJoinRequestStatus>))]
public enum TeamJoinRequestStatus : byte
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}

public class TeamJoinRequest
{
    [Key]
    public int Id { get; set; }

    public int TeamId { get; set; }

    public Team Team { get; set; } = null!;

    public Guid UserId { get; set; }

    public UserInfo User { get; set; } = null!;

    [MaxLength(Limits.MaxUserDataLength)]
    public string? Message { get; set; }

    public TeamJoinRequestStatus Status { get; set; } = TeamJoinRequestStatus.Pending;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ReviewedAtUtc { get; set; }

    public Guid? ReviewedById { get; set; }

    public UserInfo? ReviewedBy { get; set; }
}
