using GZCTF.Middlewares;
using GZCTF.Models.Request.Training;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Controllers;

[RequireTeacher]
[ApiController]
[Route("api/admin/student-groups")]
public class StudentGroupAdminController(
    AppDbContext context,
    UserManager<UserInfo> userManager,
    ILogger<StudentGroupAdminController> logger) : ControllerBase
{
    private async Task<UserInfo> CurrentUser() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Current user is missing.");

    private IQueryable<StudentGroup> VisibleGroups(UserInfo actor)
    {
        var query = context.StudentGroups
            .Include(g => g.Members)
            .ThenInclude(m => m.Student)
            .Include(g => g.Managers)
            .ThenInclude(m => m.Manager)
            .AsQueryable();

        if (actor.Role >= Role.Admin)
            return query;

        return query.Where(g => g.Managers.Any(m => m.ManagerId == actor.Id));
    }

    private async Task<bool> CanManageGroup(UserInfo actor, int groupId, CancellationToken token) =>
        actor.Role >= Role.Admin ||
        await context.StudentGroupManagers.AnyAsync(m => m.GroupId == groupId && m.ManagerId == actor.Id, token);

    [HttpGet]
    [ProducesResponseType(typeof(StudentGroupBriefModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroups([FromQuery] string? keyword = null,
        [FromQuery] bool includeArchived = false, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var query = VisibleGroups(actor);

        if (!includeArchived)
            query = query.Where(g => !g.IsArchived);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var lowered = keyword.Trim().ToLower();
            query = query.Where(g => g.Name.ToLower().Contains(lowered) || g.Description.ToLower().Contains(lowered));
        }

        var groups = await query.OrderBy(g => g.Name).ToArrayAsync(token);
        return Ok(groups.Select(StudentGroupBriefModel.FromGroup).ToArray());
    }

    [HttpGet("{groupId:int}")]
    [ProducesResponseType(typeof(StudentGroupDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGroup([FromRoute] int groupId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var group = await VisibleGroups(actor).SingleOrDefaultAsync(g => g.Id == groupId, token);

        return group is null ? NotFound() : Ok(StudentGroupDetailModel.FromDetail(group));
    }

    [HttpPost]
    [ProducesResponseType(typeof(StudentGroupDetailModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateGroup([FromBody] StudentGroupEditModel model, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var group = new StudentGroup
        {
            Name = model.Name.Trim(),
            Description = model.Description.Trim(),
            CreatedById = actor.Id,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        group.Managers.Add(new StudentGroupManager
        {
            Group = group,
            ManagerId = actor.Id,
            RoleInGroup = StudentGroupManagerRole.Owner,
            AddedById = actor.Id
        });

        context.StudentGroups.Add(group);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Created student group {group.Name}.", TaskStatus.Success, LogLevel.Information);

        return Ok(StudentGroupDetailModel.FromDetail(group));
    }

    [HttpPut("{groupId:int}")]
    public async Task<IActionResult> UpdateGroup([FromRoute] int groupId, [FromBody] StudentGroupEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var group = await context.StudentGroups.SingleOrDefaultAsync(g => g.Id == groupId, token);

        if (group is null)
            return NotFound();

        if (!await CanManageGroup(actor, groupId, token))
            return Forbid();

        group.Name = model.Name.Trim();
        group.Description = model.Description.Trim();
        group.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Updated student group {group.Name}.", TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    [HttpDelete("{groupId:int}")]
    public async Task<IActionResult> ArchiveGroup([FromRoute] int groupId, CancellationToken token = default)
    {
        var actor = await CurrentUser();
        var group = await context.StudentGroups.SingleOrDefaultAsync(g => g.Id == groupId, token);

        if (group is null)
            return NotFound();

        if (!await CanManageGroup(actor, groupId, token))
            return Forbid();

        group.IsArchived = true;
        group.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Archived student group {group.Name}.", TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    [HttpPost("{groupId:int}/members")]
    public async Task<IActionResult> AddMember([FromRoute] int groupId, [FromBody] StudentGroupMemberEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();

        if (!await CanManageGroup(actor, groupId, token))
            return Forbid();

        var student = await context.Users.SingleOrDefaultAsync(u => u.Id == model.StudentId, token);
        if (student is null || !RolePolicy.CanManageRole(actor.Role, student.Role) || student.Role != Role.Student)
            return BadRequest(new RequestResponse("只能把学生加入培训分组。"));

        var exists = await context.StudentGroupMembers.AnyAsync(m => m.GroupId == groupId && m.StudentId == model.StudentId, token);
        if (!exists)
        {
            context.StudentGroupMembers.Add(new StudentGroupMember
            {
                GroupId = groupId,
                StudentId = model.StudentId,
                AddedById = actor.Id,
                Note = model.Note.Trim()
            });
            await context.SaveChangesAsync(token);
            logger.SystemLog($"Added student {student.UserName} to student group {groupId}.",
                TaskStatus.Success, LogLevel.Information);
        }

        return Ok();
    }

    [HttpDelete("{groupId:int}/members/{studentId:guid}")]
    public async Task<IActionResult> RemoveMember([FromRoute] int groupId, [FromRoute] Guid studentId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();

        if (!await CanManageGroup(actor, groupId, token))
            return Forbid();

        var member = await context.StudentGroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.StudentId == studentId, token);
        if (member is null)
            return NotFound();

        context.StudentGroupMembers.Remove(member);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Removed student {studentId} from student group {groupId}.",
            TaskStatus.Success, LogLevel.Information);

        return Ok();
    }

    [HttpPost("{groupId:int}/managers")]
    public async Task<IActionResult> AddManager([FromRoute] int groupId, [FromBody] StudentGroupManagerEditModel model,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (actor.Role < Role.Admin)
            return Forbid();

        var teacher = await context.Users.SingleOrDefaultAsync(u => u.Id == model.TeacherId, token);
        if (teacher is null || teacher.Role != Role.Teacher)
            return BadRequest(new RequestResponse("只能把老师设置为分组管理人。"));

        var exists = await context.StudentGroupManagers.AnyAsync(m => m.GroupId == groupId && m.ManagerId == model.TeacherId, token);
        if (!exists)
        {
            context.StudentGroupManagers.Add(new StudentGroupManager
            {
                GroupId = groupId,
                ManagerId = model.TeacherId,
                RoleInGroup = model.RoleInGroup,
                AddedById = actor.Id
            });
            await context.SaveChangesAsync(token);
            logger.SystemLog($"Added teacher {teacher.UserName} as manager of student group {groupId}.",
                TaskStatus.Success, LogLevel.Information);
        }

        return Ok();
    }

    [HttpDelete("{groupId:int}/managers/{teacherId:guid}")]
    public async Task<IActionResult> RemoveManager([FromRoute] int groupId, [FromRoute] Guid teacherId,
        CancellationToken token = default)
    {
        var actor = await CurrentUser();
        if (actor.Role < Role.Admin)
            return Forbid();

        var manager = await context.StudentGroupManagers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.ManagerId == teacherId, token);
        if (manager is null)
            return NotFound();

        context.StudentGroupManagers.Remove(manager);
        await context.SaveChangesAsync(token);
        logger.SystemLog($"Removed teacher {teacherId} from student group {groupId} managers.",
            TaskStatus.Success, LogLevel.Information);

        return Ok();
    }
}
