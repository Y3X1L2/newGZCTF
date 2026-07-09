using System;
using System.Collections.Generic;
using GZCTF.Models.Data;
using GZCTF.Services.Training;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Training;

public class TrainingCourseAccessPolicyTests
{
    [Fact]
    public void IsVisibleInList_ExcludesArchivedCourses()
    {
        var course = new TrainingCourse { Status = TrainingCourseStatus.Archived };

        Assert.False(TrainingCourseAccessPolicy.IsVisibleInList(course));
    }

    [Fact]
    public void CanDelete_AllowsAdminCreatorAndOwnerTeacherOnly()
    {
        var creatorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var ownerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var teacherId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var studentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var course = new TrainingCourse
        {
            CreatedById = creatorId,
            Teachers =
            [
                new TrainingCourseTeacher { TeacherId = ownerId, Role = TrainingCourseTeacherRole.Owner },
                new TrainingCourseTeacher { TeacherId = teacherId, Role = TrainingCourseTeacherRole.Teacher }
            ]
        };

        Assert.True(TrainingCourseAccessPolicy.CanDelete(new UserInfo { Id = Guid.NewGuid(), Role = Role.Admin }, course));
        Assert.True(TrainingCourseAccessPolicy.CanDelete(new UserInfo { Id = creatorId, Role = Role.Teacher }, course));
        Assert.True(TrainingCourseAccessPolicy.CanDelete(new UserInfo { Id = ownerId, Role = Role.Teacher }, course));
        Assert.False(TrainingCourseAccessPolicy.CanDelete(new UserInfo { Id = teacherId, Role = Role.Teacher }, course));
        Assert.False(TrainingCourseAccessPolicy.CanDelete(new UserInfo { Id = studentId, Role = Role.User }, course));
    }
}
