using System.ComponentModel.DataAnnotations;

namespace GZCTF.Models.Request.Training;

public class StudentGroupEditModel
{
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;
}

public class StudentGroupMemberEditModel
{
    public Guid StudentId { get; set; }

    [MaxLength(256)]
    public string Note { get; set; } = string.Empty;
}

public class StudentGroupManagerEditModel
{
    public Guid TeacherId { get; set; }

    public StudentGroupManagerRole RoleInGroup { get; set; } = StudentGroupManagerRole.Assistant;
}

public class StudentGroupBriefModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsArchived { get; set; }

    public int MemberCount { get; set; }

    public int ManagerCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public static StudentGroupBriefModel FromGroup(StudentGroup group) =>
        new()
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            IsArchived = group.IsArchived,
            MemberCount = group.Members.Count,
            ManagerCount = group.Managers.Count,
            UpdatedAt = group.UpdatedAt
        };
}

public class StudentGroupMemberModel
{
    public Guid StudentId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string RealName { get; set; } = string.Empty;

    public string StdNumber { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public string Note { get; set; } = string.Empty;

    public DateTimeOffset JoinedAt { get; set; }

    public static StudentGroupMemberModel FromMember(StudentGroupMember member) =>
        new()
        {
            StudentId = member.StudentId,
            UserName = member.Student.UserName ?? string.Empty,
            RealName = member.Student.RealName,
            StdNumber = member.Student.StdNumber,
            Avatar = member.Student.AvatarUrl,
            Note = member.Note,
            JoinedAt = member.JoinedAt
        };
}

public class StudentGroupManagerModel
{
    public Guid TeacherId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string RealName { get; set; } = string.Empty;

    public StudentGroupManagerRole RoleInGroup { get; set; }

    public static StudentGroupManagerModel FromManager(StudentGroupManager manager) =>
        new()
        {
            TeacherId = manager.ManagerId,
            UserName = manager.Manager.UserName ?? string.Empty,
            RealName = manager.Manager.RealName,
            RoleInGroup = manager.RoleInGroup
        };
}

public class StudentGroupDetailModel : StudentGroupBriefModel
{
    public List<StudentGroupMemberModel> Members { get; set; } = [];

    public List<StudentGroupManagerModel> Managers { get; set; } = [];

    public static StudentGroupDetailModel FromDetail(StudentGroup group) =>
        new()
        {
            Id = group.Id,
            Name = group.Name,
            Description = group.Description,
            IsArchived = group.IsArchived,
            MemberCount = group.Members.Count,
            ManagerCount = group.Managers.Count,
            UpdatedAt = group.UpdatedAt,
            Members = group.Members.Select(StudentGroupMemberModel.FromMember).ToList(),
            Managers = group.Managers.Select(StudentGroupManagerModel.FromManager).ToList()
        };
}
