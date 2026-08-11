using GZCTF.Modules.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Provisioning.Infrastructure;

public sealed class TrainingCourseApiTokenResourceGrantPolicy(AppDbContext context)
    : IApiTokenResourceGrantPolicy
{
    public string ResourceType => "training-course";

    public Task<bool> CanGrantAsync(
        ActorContext actor,
        string resourceId,
        CancellationToken cancellationToken)
    {
        if (actor.Role < Role.Teacher)
            return Task.FromResult(false);
        if (resourceId == "*")
            return Task.FromResult(true);
        if (!int.TryParse(resourceId, out var courseId) || courseId <= 0 || actor.UserId is not { } userId)
            return Task.FromResult(false);
        return actor.Role >= Role.Admin
            ? context.TrainingCourses.AsNoTracking().AnyAsync(course => course.Id == courseId, cancellationToken)
            : context.TrainingCourseTeachers.AsNoTracking().AnyAsync(
                teacher => teacher.CourseId == courseId && teacher.TeacherId == userId,
                cancellationToken);
    }
}

public sealed class TheoryBankApiTokenResourceGrantPolicy : IApiTokenResourceGrantPolicy
{
    public string ResourceType => "theory-bank";

    public Task<bool> CanGrantAsync(
        ActorContext actor,
        string resourceId,
        CancellationToken cancellationToken) =>
        Task.FromResult(actor.Role >= Role.Teacher && resourceId == "*");
}

public sealed class TeamApiTokenResourceGrantPolicy(AppDbContext context) : IApiTokenResourceGrantPolicy
{
    public string ResourceType => "team";

    public Task<bool> CanGrantAsync(
        ActorContext actor,
        string resourceId,
        CancellationToken cancellationToken)
    {
        if (actor.Role < Role.Admin)
            return Task.FromResult(false);
        if (resourceId == "*")
            return Task.FromResult(true);
        return int.TryParse(resourceId, out var teamId) && teamId > 0
            ? context.Teams.AsNoTracking().AnyAsync(team => team.Id == teamId, cancellationToken)
            : Task.FromResult(false);
    }
}
