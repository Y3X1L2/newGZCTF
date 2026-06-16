using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

[Index(nameof(Name))]
public class StudentGroup
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? CreatedById { get; set; }

    [JsonIgnore]
    public UserInfo? CreatedBy { get; set; }

    public List<StudentGroupMember> Members { get; set; } = [];

    public List<StudentGroupManager> Managers { get; set; } = [];
}

[PrimaryKey(nameof(GroupId), nameof(StudentId))]
[Index(nameof(StudentId))]
public class StudentGroupMember
{
    public int GroupId { get; set; }

    [JsonIgnore]
    public StudentGroup Group { get; set; } = null!;

    public Guid StudentId { get; set; }

    [JsonIgnore]
    public UserInfo Student { get; set; } = null!;

    public Guid? AddedById { get; set; }

    [JsonIgnore]
    public UserInfo? AddedBy { get; set; }

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    [MaxLength(256)]
    public string Note { get; set; } = string.Empty;
}

[PrimaryKey(nameof(GroupId), nameof(ManagerId))]
[Index(nameof(ManagerId))]
public class StudentGroupManager
{
    public int GroupId { get; set; }

    [JsonIgnore]
    public StudentGroup Group { get; set; } = null!;

    public Guid ManagerId { get; set; }

    [JsonIgnore]
    public UserInfo Manager { get; set; } = null!;

    public StudentGroupManagerRole RoleInGroup { get; set; } = StudentGroupManagerRole.Owner;

    public Guid? AddedById { get; set; }

    [JsonIgnore]
    public UserInfo? AddedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
