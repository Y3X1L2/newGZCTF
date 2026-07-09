using GZCTF.Models.Data;

namespace GZCTF.Services.Training;

public static class TrainingCourseAccessPolicy
{
    public static bool IsVisibleInList(TrainingCourse course) =>
        course.Status != TrainingCourseStatus.Archived;

    public static bool CanDelete(UserInfo actor, TrainingCourse course) =>
        actor.Role >= Role.Admin ||
        course.CreatedById == actor.Id ||
        course.Teachers.Any(t => t.TeacherId == actor.Id && t.Role == TrainingCourseTeacherRole.Owner);
}
