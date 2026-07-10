using System;
using System.Collections.Generic;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Training;
using GZCTF.Services;
using GZCTF.Services.Training;
using GZCTF.Utils;
using Xunit;

namespace GZCTF.Test.UnitTests.Training;

public class TrainingCourseAccessPolicyTests
{
    [Fact]
    public void PlayerPaper_UsesStoredQuestionSnapshotForExistingSheet()
    {
        var paper = new TrainingCourseChapterTheoryPaper
        {
            Id = 10,
            ShowCorrectAnswerAfterSubmit = true,
            Questions =
            [
                new TrainingCourseChapterTheoryQuestion
                {
                    Id = 20,
                    Title = "Current paper question",
                    Score = 1
                },
                new TrainingCourseChapterTheoryQuestion
                {
                    Id = 21,
                    Title = "Archived paper question",
                    Score = 7,
                    IsArchived = true
                }
            ]
        };
        var sheet = new TrainingCourseChapterTheorySheet
        {
            Status = TheoryAnswerSheetStatus.Submitted,
            MaxScore = 7,
            Answers =
            [
                new TrainingCourseChapterTheoryAnswer
                {
                    Id = 30,
                    PaperQuestionId = 21,
                    QuestionType = TheoryQuestionType.SingleChoice,
                    QuestionTitle = "Historical snapshot",
                    QuestionContent = "Historical content",
                    QuestionOptions = ["zero", "one"],
                    CorrectAnswerIndexes = [1],
                    SelectedIndexes = [1],
                    MaxScore = 7,
                    QuestionOrder = 3
                }
            ]
        };

        var model = TrainingCourseChapterTheoryPlayerPaperModel.FromPaper(paper, sheet);

        var question = Assert.Single(model.Questions);
        Assert.Equal(21, question.Id);
        Assert.Equal("Historical snapshot", question.Title);
        Assert.Equal(["zero", "one"], question.Options);
        Assert.Equal([1], question.AnswerIndexes);
        Assert.Equal(7, model.TotalScore);
    }

    [Fact]
    public void PaperDetail_ExcludesArchivedHistoricalQuestions()
    {
        var paper = new TrainingCourseChapterTheoryPaper
        {
            Questions =
            [
                new TrainingCourseChapterTheoryQuestion { Id = 1, Title = "Active", Score = 4 },
                new TrainingCourseChapterTheoryQuestion
                    { Id = 2, Title = "Archived", Score = 9, IsArchived = true }
            ]
        };

        var model = TrainingCourseChapterTheoryPaperDetailModel.FromPaper(paper);

        Assert.Equal(4, model.TotalScore);
        Assert.Equal("Active", Assert.Single(model.Questions).Title);
    }

    [Theory]
    [InlineData(true, TheoryAnswerSheetStatus.Submitted, true)]
    [InlineData(false, TheoryAnswerSheetStatus.Submitted, false)]
    [InlineData(true, TheoryAnswerSheetStatus.Draft, false)]
    public void CanStartCourseTheoryRetake_RequiresEnabledSubmittedAttempt(
        bool allowRetake,
        TheoryAnswerSheetStatus status,
        bool expected)
    {
        var paper = new TrainingCourseChapterTheoryPaper { AllowRetake = allowRetake };
        var sheet = new TrainingCourseChapterTheorySheet { Status = status };

        Assert.Equal(expected, TheoryExamService.CanStartCourseTheoryRetake(paper, sheet));
    }

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
