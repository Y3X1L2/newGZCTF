using System.ComponentModel.DataAnnotations;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Request.Shared;

namespace GZCTF.Models.Request.Training;

public class TrainingCheckInModel
{
    public DateOnly Date { get; set; }

    public DateTimeOffset CheckedAt { get; set; }

    public bool IsToday { get; set; }
}

public class TrainingActivityPointModel
{
    public DateOnly Date { get; set; }

    public int StudyActions { get; set; }

    public int CompletedChapters { get; set; }

    public int AcceptedChallenges { get; set; }

    public bool CheckedIn { get; set; }
}

public class TrainingPersonalOverviewModel
{
    public int VisibleCourseCount { get; set; }

    public int JoinedCourseCount { get; set; }

    public int CompletedCourseCount { get; set; }

    public int AverageProgress { get; set; }

    public int CompletedChapterCount { get; set; }

    public int TotalChapterCount { get; set; }

    public int CtfSolvedChallenges { get; set; }

    public int CtfTotalChallenges { get; set; }

    public int TheoryPassedAssessments { get; set; }

    public int TheoryTotalAssessments { get; set; }

    public int CheckInDays { get; set; }

    public int CurrentCheckInStreak { get; set; }

    public bool CheckedInToday { get; set; }

    public List<TrainingCheckInModel> CheckIns { get; set; } = [];

    public List<TrainingActivityPointModel> Activity { get; set; } = [];
}

public class TrainingCourseEditModel
{
    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Summary { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [MaxLength(Limits.FileHashLength)]
    public string? CoverFileHash { get; set; }

    public List<string> Tags { get; set; } = [];

    public TrainingCourseEnrollmentPolicy EnrollmentPolicy { get; set; } =
        TrainingCourseEnrollmentPolicy.TeacherApproval;
}

public class TrainingCourseEnrollmentApplyModel
{
    [MaxLength(512)]
    public string ApplyReason { get; set; } = string.Empty;
}

public class TrainingCourseEnrollmentReviewModel
{
    public TrainingCourseEnrollmentStatus Status { get; set; } = TrainingCourseEnrollmentStatus.Approved;

    [MaxLength(512)]
    public string ReviewComment { get; set; } = string.Empty;
}

public class TrainingCourseTeacherEditModel
{
    public Guid TeacherId { get; set; }

    public TrainingCourseTeacherRole Role { get; set; } = TrainingCourseTeacherRole.Teacher;
}

public class TrainingCourseStudentEnrollModel
{
    public Guid UserId { get; set; }
}

public class TrainingCourseChapterEditModel
{
    public int? ParentId { get; set; }

    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Summary { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public TrainingArticleContentType ContentType { get; set; } = TrainingArticleContentType.Markdown;

    public TrainingChapterCompletionPolicy CompletionPolicy { get; set; } = new();

    public TrainingCourseVideoProvider VideoProvider { get; set; } = TrainingCourseVideoProvider.None;

    [MaxLength(1024)]
    public string? VideoUrl { get; set; }

    [MaxLength(Limits.FileHashLength)]
    public string? VideoFileHash { get; set; }

    public int Order { get; set; }

    public bool IsPublished { get; set; } = true;
}

public class TrainingCourseResourceEditModel
{
    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public TrainingCourseResourceType Type { get; set; } = TrainingCourseResourceType.File;

    [MaxLength(1024)]
    public string? ExternalUrl { get; set; }

    [MaxLength(Limits.FileHashLength)]
    public string? LocalFileHash { get; set; }

    public int Order { get; set; }

    public bool IsVisible { get; set; } = true;
}

public class TrainingCourseChallengeEditModel
{
    public int ExerciseChallengeId { get; set; }

    public int? ChapterId { get; set; }

    public int Order { get; set; }

    public bool IsRequired { get; set; } = true;

    [MaxLength(128)]
    public string? DisplayTitle { get; set; }

    public FileType AttachmentType { get; set; } = FileType.None;

    [MaxLength(Limits.FileHashLength)]
    public string? AttachmentFileHash { get; set; }

    [MaxLength(1024)]
    public string? AttachmentRemoteUrl { get; set; }
}

public class TrainingCourseChallengeUpdateModel : TrainingCourseChallengeCreateModel;

public class TrainingCourseImageTemplateAttachModel
{
    public int TemplateId { get; set; }
}

public class TrainingCourseDockerRegisterModel
{
    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string RegistryUrl { get; set; } = string.Empty;

    public OSType OSType { get; set; } = OSType.Linux;

    [MaxLength(512)]
    public string? RegistryAuth { get; set; }
}

public class TrainingCourseLocalImageImportModel
{
    [Required]
    [MaxLength(1024)]
    public string LocalPath { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? DisplayName { get; set; }
}

public class TrainingCourseChallengeCreateModel
{
    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;

    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;

    public EnvironmentType Environment { get; set; } = EnvironmentType.None;

    public int? ImageTemplateId { get; set; }

    [MaxLength(512)]
    public string? ContainerImage { get; set; }

    public int? MemoryLimit { get; set; } = 64;

    public int? CPUCount { get; set; } = 1;

    public int? StorageLimit { get; set; } = 256;

    public int? ExposePort { get; set; } = 80;

    public NetworkMode? NetworkMode { get; set; } = Utils.NetworkMode.Open;

    [MaxLength(Limits.MaxFlagTemplateLength)]
    public string? FlagTemplate { get; set; }

    [MaxLength(Limits.MaxFlagLength)]
    public string? StaticFlag { get; set; }

    public int SubmissionLimit { get; set; }

    public int? ChapterId { get; set; }

    public int Order { get; set; }

    public bool IsRequired { get; set; } = true;

    [MaxLength(128)]
    public string? DisplayTitle { get; set; }

    public FileType AttachmentType { get; set; } = FileType.None;

    [MaxLength(Limits.FileHashLength)]
    public string? AttachmentFileHash { get; set; }

    [MaxLength(1024)]
    public string? AttachmentRemoteUrl { get; set; }
}

public class TrainingCourseTeacherModel
{
    public Guid TeacherId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string RealName { get; set; } = string.Empty;

    public TrainingCourseTeacherRole Role { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public static TrainingCourseTeacherModel FromTeacher(TrainingCourseTeacher teacher) =>
        new()
        {
            TeacherId = teacher.TeacherId,
            UserName = teacher.Teacher.UserName ?? string.Empty,
            RealName = teacher.Teacher.RealName,
            Role = teacher.Role,
            AssignedAt = teacher.AssignedAt
        };
}

public class TrainingCourseEnrollmentModel
{
    public int CourseId { get; set; }

    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string RealName { get; set; } = string.Empty;

    public string StdNumber { get; set; } = string.Empty;

    public TrainingCourseEnrollmentStatus Status { get; set; }

    public string ApplyReason { get; set; } = string.Empty;

    public string ReviewComment { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public int CompletedChapterCount { get; set; }

    public int TotalChapterCount { get; set; }

    public TrainingCourseProgressStatus? ProgressStatus { get; set; }

    public DateTimeOffset? ProgressUpdatedAt { get; set; }

    public static TrainingCourseEnrollmentModel FromEnrollment(
        TrainingCourseEnrollment enrollment,
        TrainingCourseProgress? progress = null,
        int totalChapterCount = 0) =>
        new()
        {
            CourseId = enrollment.CourseId,
            UserId = enrollment.UserId,
            UserName = enrollment.User.UserName ?? string.Empty,
            RealName = enrollment.User.RealName,
            StdNumber = enrollment.User.StdNumber,
            Status = enrollment.Status,
            ApplyReason = enrollment.ApplyReason,
            ReviewComment = enrollment.ReviewComment,
            RequestedAt = enrollment.RequestedAt,
            ReviewedAt = enrollment.ReviewedAt,
            CompletedChapterCount = progress?.CompletedChapterCount ?? 0,
            TotalChapterCount = progress?.TotalChapterCount ?? totalChapterCount,
            ProgressStatus = progress?.Status,
            ProgressUpdatedAt = progress?.UpdatedAt
        };
}

public class TrainingCourseTeacherCandidateModel
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string RealName { get; set; } = string.Empty;

    public string StdNumber { get; set; } = string.Empty;

    public string? Email { get; set; }

    public Role Role { get; set; }

    public bool AlreadyTeacher { get; set; }

    public static TrainingCourseTeacherCandidateModel FromUser(UserInfo user, bool alreadyTeacher) =>
        new()
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            RealName = user.RealName,
            StdNumber = user.StdNumber,
            Email = user.Email,
            Role = user.Role,
            AlreadyTeacher = alreadyTeacher
        };
}

public class TrainingCourseStudentCandidateModel
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string RealName { get; set; } = string.Empty;

    public string StdNumber { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Avatar { get; set; }

    public bool AlreadyEnrolled { get; set; }

    public static TrainingCourseStudentCandidateModel FromUser(UserInfo user, bool alreadyEnrolled) =>
        new()
        {
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            RealName = user.RealName,
            StdNumber = user.StdNumber,
            Email = user.Email,
            Avatar = user.AvatarUrl,
            AlreadyEnrolled = alreadyEnrolled
        };
}

public class TrainingCourseStudentLearningSummaryModel
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string RealName { get; set; } = string.Empty;

    public string StdNumber { get; set; } = string.Empty;

    public TrainingCourseEnrollmentStatus EnrollmentStatus { get; set; }

    public int CompletedChapterCount { get; set; }

    public int TotalChapterCount { get; set; }

    public int ChallengeSolvedCount { get; set; }

    public int ChallengeTotalCount { get; set; }

    public int TheorySubmittedCount { get; set; }

    public int TheoryPassedCount { get; set; }

    public int TheoryTotalCount { get; set; }

    public int TheoryScore { get; set; }

    public int TheoryMaxScore { get; set; }

    public TrainingCourseProgressStatus? ProgressStatus { get; set; }

    public DateTimeOffset? LastActivityAt { get; set; }
}

public class TrainingCourseStudentLearningDetailModel : TrainingCourseStudentLearningSummaryModel
{
    public List<TrainingCourseStudentChapterLearningModel> Chapters { get; set; } = [];
}

public class TrainingCourseStudentChapterLearningModel
{
    public int ChapterId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public int Order { get; set; }

    public bool IsPublished { get; set; }

    public TrainingChapterCompletionPolicy CompletionPolicy { get; set; } = new();

    public TrainingCourseProgressStatus? ProgressStatus { get; set; }

    public int ReadPercent { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public TrainingCourseStudentTheoryLearningModel? Theory { get; set; }

    public List<TrainingCourseStudentChallengeLearningModel> Challenges { get; set; } = [];
}

public class TrainingCourseStudentChallengeLearningModel
{
    public int ExerciseChallengeId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? DisplayTitle { get; set; }

    public ChallengeCategory Category { get; set; }

    public ChallengeType Type { get; set; }

    public EnvironmentType Environment { get; set; }

    public bool IsRequired { get; set; }

    public bool Solved { get; set; }

    public int SubmissionCount { get; set; }

    public int AcceptedSubmissionCount { get; set; }

    public AnswerResult? LastStatus { get; set; }

    public DateTimeOffset? LastSubmittedAt { get; set; }

    public string? LastIpAddress { get; set; }

    public string? InstanceEntry { get; set; }

    public DateTimeOffset? InstanceStopAt { get; set; }
}

public class TrainingCourseStudentTheoryLearningModel
{
    public int PaperId { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public int QuestionCount { get; set; }

    public int TotalScore { get; set; }

    public int PassRate { get; set; }

    public TheoryAnswerSheetStatus? Status { get; set; }

    public int? Score { get; set; }

    public bool? Passed { get; set; }

    public int CorrectCount { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public List<TrainingCourseStudentTheoryAnswerDetailModel> Answers { get; set; } = [];
}

public class TrainingCourseStudentTheoryAnswerDetailModel
{
    public int QuestionId { get; set; }

    public TheoryQuestionType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public List<int> AnswerIndexes { get; set; } = [];

    public List<int> SelectedIndexes { get; set; } = [];

    public bool? IsCorrect { get; set; }

    public int Score { get; set; }

    public int MaxScore { get; set; }

    public int Order { get; set; }
}

public class TrainingCourseResourceModel
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public TrainingCourseResourceType Type { get; set; }

    public string? ExternalUrl { get; set; }

    public string? FileName { get; set; }

    public long? FileSize { get; set; }

    public string? DownloadUrl { get; set; }

    public int Order { get; set; }

    public bool IsVisible { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static TrainingCourseResourceModel FromResource(TrainingCourseResource resource, bool revealDownload) =>
        new()
        {
            Id = resource.Id,
            CourseId = resource.CourseId,
            Title = resource.Title,
            Description = resource.Description,
            Type = resource.Type,
            ExternalUrl = revealDownload ? resource.ExternalUrl : null,
            FileName = resource.LocalFile?.Name,
            FileSize = resource.LocalFile?.FileSize,
            DownloadUrl = revealDownload
                ? resource.Type == TrainingCourseResourceType.File
                    ? resource.LocalFile?.Url()
                    : resource.ExternalUrl
                : null,
            Order = resource.Order,
            IsVisible = resource.IsVisible,
            CreatedAt = resource.CreatedAt
        };
}

public class TrainingCourseChallengeModel
{
    public int ExerciseChallengeId { get; set; }

    public int? ChapterId { get; set; }

    public string Title { get; set; } = string.Empty;

    public ChallengeCategory Category { get; set; }

    public ChallengeType Type { get; set; }

    public EnvironmentType Environment { get; set; }

    public int Order { get; set; }

    public bool IsRequired { get; set; }

    public bool Solved { get; set; }

    public string? DisplayTitle { get; set; }

    public bool HasAttachment { get; set; }

    public string? AttachmentFileName { get; set; }

    public static TrainingCourseChallengeModel FromChallenge(
        TrainingCourseChallenge challenge,
        int? chapterId = null,
        bool solved = false) =>
        new()
        {
            ExerciseChallengeId = challenge.ExerciseChallengeId,
            ChapterId = chapterId,
            Title = challenge.ExerciseChallenge.Title,
            Category = challenge.ExerciseChallenge.Category,
            Type = challenge.ExerciseChallenge.Type,
            Environment = challenge.ExerciseChallenge.Environment,
            Order = challenge.Order,
            IsRequired = challenge.IsRequired,
            Solved = solved,
            DisplayTitle = challenge.DisplayTitle,
            HasAttachment = challenge.ExerciseChallenge.Attachment is not null,
            AttachmentFileName = challenge.ExerciseChallenge.Attachment?.LocalFile?.Name
        };
}

public class TrainingCourseChallengeEditDetailModel : TrainingCourseChallengeCreateModel
{
    public int ExerciseChallengeId { get; set; }

    public string? AttachmentUrl { get; set; }

    public string? AttachmentFileName { get; set; }

    public long? AttachmentFileSize { get; set; }

    public int SubmissionCount { get; set; }

    public bool HasSubmittedAnswers => SubmissionCount > 0;

    public static TrainingCourseChallengeEditDetailModel FromChallenge(
        TrainingCourseChallenge link,
        int? chapterId,
        int submissionCount)
    {
        var challenge = link.ExerciseChallenge;
        var staticFlag = challenge.Type.IsDynamic()
            ? null
            : challenge.Flags.OrderBy(f => f.OrderIndex).FirstOrDefault()?.Flag;

        return new TrainingCourseChallengeEditDetailModel
        {
            ExerciseChallengeId = link.ExerciseChallengeId,
            Title = challenge.Title,
            Content = challenge.Content,
            Category = challenge.Category,
            Type = challenge.Type,
            Environment = challenge.Environment,
            ImageTemplateId = challenge.ImageTemplateId,
            ContainerImage = challenge.ContainerImage,
            MemoryLimit = challenge.MemoryLimit,
            CPUCount = challenge.CPUCount,
            StorageLimit = challenge.StorageLimit,
            ExposePort = challenge.ExposePort,
            NetworkMode = challenge.NetworkMode,
            FlagTemplate = challenge.FlagTemplate,
            StaticFlag = staticFlag,
            SubmissionLimit = challenge.SubmissionLimit,
            ChapterId = chapterId,
            Order = link.Order,
            IsRequired = link.IsRequired,
            DisplayTitle = link.DisplayTitle,
            AttachmentType = challenge.Attachment?.Type ?? FileType.None,
            AttachmentFileHash = challenge.Attachment?.LocalFile?.Hash,
            AttachmentRemoteUrl = challenge.Attachment?.RemoteUrl,
            AttachmentUrl = challenge.Attachment?.Url,
            AttachmentFileName = challenge.Attachment?.LocalFile?.Name,
            AttachmentFileSize = challenge.Attachment?.FileSize,
            SubmissionCount = submissionCount
        };
    }
}

public class TrainingCourseTheoryQuestionModel : TheoryQuestionEditModel
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public static TrainingCourseTheoryQuestionModel FromQuestion(TrainingCourseTheoryQuestion question) =>
        new()
        {
            Id = question.Id,
            CourseId = question.CourseId,
            Type = question.Type,
            BankName = question.BankName,
            Title = question.Title,
            Content = question.Content,
            Options = question.Options,
            AnswerIndexes = question.AnswerIndexes,
            CreatedAt = question.CreatedAt,
            UpdatedAt = question.UpdatedAt
        };
}

public class TrainingCourseTheoryPaperQuestionEditModel : TheoryQuestionEditModel
{
    public int Id { get; set; }

    public int? SourceQuestionId { get; set; }

    [Range(1, int.MaxValue)]
    public int Score { get; set; } = 1;

    public int Order { get; set; }
}

public class TrainingCourseChapterTheoryPaperEditModel
{
    [Required]
    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Range(1, 100)]
    public int PassRate { get; set; } = 60;

    public bool AllowRetake { get; set; } = true;

    public bool ShowCorrectAnswerAfterSubmit { get; set; } = true;

    public bool IsPublished { get; set; }

    public List<TrainingCourseTheoryPaperQuestionEditModel> Questions { get; set; } = [];
}

public class TrainingCourseChapterTheorySummaryModel
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    public int ChapterId { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsPublished { get; set; }

    public int QuestionCount { get; set; }

    public int TotalScore { get; set; }

    public int PassRate { get; set; }

    public bool AllowRetake { get; set; }

    public bool ShowCorrectAnswerAfterSubmit { get; set; }

    public int? AttemptNumber { get; set; }

    public TheoryAnswerSheetStatus? Status { get; set; }

    public int? Score { get; set; }

    public bool? Passed { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public static TrainingCourseChapterTheorySummaryModel FromPaper(
        TrainingCourseChapterTheoryPaper paper,
        TrainingCourseChapterTheorySheet? sheet = null) =>
        new()
        {
            Id = paper.Id,
            CourseId = paper.CourseId,
            ChapterId = paper.ChapterId,
            Title = paper.Title,
            IsPublished = paper.IsPublished,
            QuestionCount = paper.ActiveQuestions.Count(),
            TotalScore = paper.ActiveQuestions.Sum(q => q.Score),
            PassRate = paper.PassRate,
            AllowRetake = paper.AllowRetake,
            ShowCorrectAnswerAfterSubmit = paper.ShowCorrectAnswerAfterSubmit,
            AttemptNumber = sheet?.AttemptNumber,
            Status = sheet?.Status,
            Score = sheet?.Status == TheoryAnswerSheetStatus.Submitted ? sheet.Score : null,
            Passed = sheet?.Status == TheoryAnswerSheetStatus.Submitted ? sheet.Passed : null,
            SubmittedAt = sheet?.SubmittedAt
        };
}

public class TrainingCourseChapterTheoryPaperDetailModel : TrainingCourseChapterTheoryPaperEditModel
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    public int ChapterId { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int TotalScore { get; set; }

    public static TrainingCourseChapterTheoryPaperDetailModel Empty(int courseId, TrainingCourseChapter chapter) =>
        new()
        {
            CourseId = courseId,
            ChapterId = chapter.Id,
            Title = $"{chapter.Title} 课后测试",
            Description = string.Empty,
            PassRate = 60,
            AllowRetake = true,
            ShowCorrectAnswerAfterSubmit = true,
            IsPublished = false,
            Questions = [],
            TotalScore = 0
        };

    public static TrainingCourseChapterTheoryPaperDetailModel FromPaper(TrainingCourseChapterTheoryPaper paper) =>
        new()
        {
            Id = paper.Id,
            CourseId = paper.CourseId,
            ChapterId = paper.ChapterId,
            Title = paper.Title,
            Description = paper.Description,
            PassRate = paper.PassRate,
            AllowRetake = paper.AllowRetake,
            ShowCorrectAnswerAfterSubmit = paper.ShowCorrectAnswerAfterSubmit,
            IsPublished = paper.IsPublished,
            PublishedAt = paper.PublishedAt,
            UpdatedAt = paper.UpdatedAt,
            TotalScore = paper.ActiveQuestions.Sum(q => q.Score),
            Questions = paper.ActiveQuestions
                .OrderBy(q => q.Order)
                .Select(q => new TrainingCourseTheoryPaperQuestionEditModel
                {
                    Id = q.Id,
                    SourceQuestionId = q.SourceQuestionId,
                    Type = q.Type,
                    BankName = string.Empty,
                    Title = q.Title,
                    Content = q.Content,
                    Options = q.Options,
                    AnswerIndexes = q.AnswerIndexes,
                    Score = q.Score,
                    Order = q.Order
                })
                .ToList()
        };
}

public class TrainingCourseChapterTheoryPlayerQuestionModel
{
    public int Id { get; set; }

    public TheoryQuestionType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public List<string> Options { get; set; } = [];

    public int Score { get; set; }

    public int Order { get; set; }

    public List<int>? AnswerIndexes { get; set; }

    public static TrainingCourseChapterTheoryPlayerQuestionModel FromQuestion(
        TrainingCourseChapterTheoryQuestion question,
        bool revealAnswer) =>
        new()
        {
            Id = question.Id,
            Type = question.Type,
            Title = question.Title,
            Content = question.Content,
            Options = question.Options,
            Score = question.Score,
            Order = question.Order,
            AnswerIndexes = revealAnswer ? question.AnswerIndexes : null
        };

    public static TrainingCourseChapterTheoryPlayerQuestionModel FromAnswer(
        TrainingCourseChapterTheoryAnswer answer,
        bool revealAnswer) =>
        new()
        {
            Id = answer.PaperQuestionId,
            Type = answer.QuestionType,
            Title = answer.QuestionTitle,
            Content = answer.QuestionContent,
            Options = answer.QuestionOptions,
            Score = answer.MaxScore,
            Order = answer.QuestionOrder,
            AnswerIndexes = revealAnswer ? answer.CorrectAnswerIndexes : null
        };
}

public class TrainingCourseChapterTheoryPlayerPaperModel
{
    public int PaperId { get; set; }

    public int CourseId { get; set; }

    public int ChapterId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int TotalScore { get; set; }

    public int PassRate { get; set; }

    public bool AllowRetake { get; set; }

    public bool ShowCorrectAnswerAfterSubmit { get; set; }

    public int? AttemptNumber { get; set; }

    public TheoryAnswerSheetStatus? Status { get; set; }

    public int? Score { get; set; }

    public bool? Passed { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<TrainingCourseChapterTheoryPlayerQuestionModel> Questions { get; set; } = [];

    public List<TheoryAnswerModel> Answers { get; set; } = [];

    public static TrainingCourseChapterTheoryPlayerPaperModel FromPaper(
        TrainingCourseChapterTheoryPaper paper,
        TrainingCourseChapterTheorySheet? sheet)
    {
        var revealAnswer = sheet?.Status == TheoryAnswerSheetStatus.Submitted &&
                           paper.ShowCorrectAnswerAfterSubmit;
        return new TrainingCourseChapterTheoryPlayerPaperModel
        {
            PaperId = paper.Id,
            CourseId = paper.CourseId,
            ChapterId = paper.ChapterId,
            Title = paper.Title,
            Description = paper.Description,
            TotalScore = sheet?.Answers.Count > 0
                ? sheet.MaxScore
                : paper.ActiveQuestions.Sum(q => q.Score),
            PassRate = paper.PassRate,
            AllowRetake = paper.AllowRetake,
            ShowCorrectAnswerAfterSubmit = paper.ShowCorrectAnswerAfterSubmit,
            AttemptNumber = sheet?.AttemptNumber,
            Status = sheet?.Status,
            Score = sheet?.Status == TheoryAnswerSheetStatus.Submitted ? sheet.Score : null,
            Passed = sheet?.Status == TheoryAnswerSheetStatus.Submitted ? sheet.Passed : null,
            SubmittedAt = sheet?.SubmittedAt,
            UpdatedAt = sheet?.UpdatedAt,
            Questions = sheet?.Answers.Count > 0
                ? sheet.Answers
                    .OrderBy(answer => answer.QuestionOrder)
                    .ThenBy(answer => answer.Id)
                    .Select(answer => TrainingCourseChapterTheoryPlayerQuestionModel.FromAnswer(answer, revealAnswer))
                    .ToList()
                : paper.ActiveQuestions
                    .OrderBy(question => question.Order)
                    .ThenBy(question => question.Id)
                    .Select(question =>
                        TrainingCourseChapterTheoryPlayerQuestionModel.FromQuestion(question, revealAnswer))
                    .ToList(),
            Answers = sheet?.Answers
                .Select(a => new TheoryAnswerModel
                {
                    PaperQuestionId = a.PaperQuestionId,
                    SelectedIndexes = a.SelectedIndexes
                })
                .ToList() ?? []
        };
    }
}

public class TrainingCourseImageTemplateModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public OSType OSType { get; set; }

    public ImageType ImageType { get; set; }

    public ImageStatus Status { get; set; }

    public long FileSize { get; set; }

    public string? Description { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ImageHash { get; set; }

    public string? RegistryUrl { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public static TrainingCourseImageTemplateModel FromTemplate(ImageTemplate template) =>
        new()
        {
            Id = template.Id,
            Name = template.Name,
            OSType = template.OSType,
            ImageType = template.ImageType,
            Status = template.Status,
            FileSize = template.FileSize,
            Description = template.Description,
            ErrorMessage = template.ErrorMessage,
            ImageHash = template.ImageHash,
            RegistryUrl = template.RegistryUrl,
            UploadedAt = template.UploadedAt
        };
}

public class TrainingCourseChapterModel
{
    public int Id { get; set; }

    public int CourseId { get; set; }

    public int? ParentId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public TrainingArticleContentType ContentType { get; set; }

    public TrainingCourseVideoProvider VideoProvider { get; set; }

    public string? VideoUrl { get; set; }

    public string? VideoFileUrl { get; set; }

    public int Order { get; set; }

    public bool IsPublished { get; set; }

    public TrainingChapterCompletionPolicy CompletionPolicy { get; set; } = new();

    public TrainingCourseProgressStatus? ProgressStatus { get; set; }

    public int ReadPercent { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public List<TrainingCourseChallengeModel> Challenges { get; set; } = [];

    public TrainingCourseChapterTheorySummaryModel? TheoryPaper { get; set; }

    public static TrainingCourseChapterModel FromChapter(
        TrainingCourseChapter chapter,
        TrainingChapterProgress? progress = null,
        IEnumerable<TrainingCourseChallengeModel>? challenges = null,
        bool revealContent = true,
        TrainingCourseChapterTheorySummaryModel? theoryPaper = null) =>
        new()
        {
            Id = chapter.Id,
            CourseId = chapter.CourseId,
            ParentId = chapter.ParentId,
            Title = chapter.Title,
            Summary = chapter.Summary,
            Content = revealContent ? chapter.Content : string.Empty,
            ContentType = chapter.ContentType,
            VideoProvider = chapter.VideoProvider,
            VideoUrl = revealContent ? chapter.VideoUrl : null,
            VideoFileUrl = revealContent ? chapter.VideoFile?.Url() : null,
            Order = chapter.Order,
            IsPublished = chapter.IsPublished,
            CompletionPolicy = chapter.CompletionPolicy,
            ProgressStatus = progress?.Status,
            ReadPercent = progress?.ReadPercent ?? 0,
            CompletedAt = progress?.CompletedAt,
            Challenges = challenges?.ToList() ?? [],
            TheoryPaper = theoryPaper
        };
}

public class TrainingCourseModel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? CoverFileHash { get; set; }

    public string? CoverUrl => string.IsNullOrWhiteSpace(CoverFileHash) ? null : $"/assets/{CoverFileHash}/cover";

    public List<string> Tags { get; set; } = [];

    public TrainingCourseStatus Status { get; set; }

    public TrainingCourseEnrollmentPolicy EnrollmentPolicy { get; set; }

    public TrainingCourseEnrollmentStatus? EnrollmentStatus { get; set; }

    public bool CanLearn { get; set; }

    public bool CanEdit { get; set; }

    public bool CanManageTeachers { get; set; }

    public bool CanManageEnrollments { get; set; }

    public bool CanDelete { get; set; }

    public int ChapterCount { get; set; }

    public int ResourceCount { get; set; }

    public int EnrollmentCount { get; set; }

    public int CompletedChapterCount { get; set; }

    public int TotalChapterCount { get; set; }

    public TrainingCourseProgressStatus? ProgressStatus { get; set; }

    public DateTimeOffset? LastStudiedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<TrainingCourseTeacherModel> Teachers { get; set; } = [];

    public List<TrainingCourseChapterModel> Chapters { get; set; } = [];

    public List<TrainingCourseResourceModel> Resources { get; set; } = [];

    public List<TrainingCourseChallengeModel> Challenges { get; set; } = [];

    public static TrainingCourseModel FromCourse(
        TrainingCourse course,
        TrainingCourseEnrollment? enrollment = null,
        TrainingCourseProgress? progress = null,
        bool canLearn = false,
        bool canEdit = false,
        bool canManageTeachers = false,
        bool canManageEnrollments = false,
        bool canDelete = false,
        bool includeDetail = false) =>
        new()
        {
            Id = course.Id,
            Title = course.Title,
            Slug = course.Slug,
            Summary = course.Summary,
            Description = includeDetail || canLearn || canEdit ? course.Description : course.Summary,
            CoverFileHash = course.CoverFileHash,
            Tags = course.Tags,
            Status = course.Status,
            EnrollmentPolicy = course.EnrollmentPolicy,
            EnrollmentStatus = enrollment?.Status,
            CanLearn = canLearn,
            CanEdit = canEdit,
            CanManageTeachers = canManageTeachers,
            CanManageEnrollments = canManageEnrollments,
            CanDelete = canDelete,
            ChapterCount = course.Chapters.Count(c => c.IsPublished || canEdit),
            ResourceCount = course.Resources.Count(r => r.IsVisible || canEdit),
            EnrollmentCount = course.Enrollments.Count(e => e.Status == TrainingCourseEnrollmentStatus.Approved),
            CompletedChapterCount = progress?.CompletedChapterCount ?? 0,
            TotalChapterCount = progress?.TotalChapterCount ?? course.Chapters.Count(c => c.IsPublished),
            ProgressStatus = progress?.Status,
            LastStudiedAt = progress?.UpdatedAt,
            CreatedAt = course.CreatedAt,
            UpdatedAt = course.UpdatedAt,
            Teachers = course.Teachers
                .OrderBy(t => t.Role)
                .ThenBy(t => t.Teacher.UserName)
                .Select(TrainingCourseTeacherModel.FromTeacher)
                .ToList(),
            Chapters = includeDetail
                ? course.Chapters
                    .Where(c => c.IsPublished || canEdit)
                    .OrderBy(c => c.Order)
                    .ThenBy(c => c.Id)
                    .Select(c => TrainingCourseChapterModel.FromChapter(c, revealContent: canLearn || canEdit))
                    .ToList()
                : [],
            Resources = includeDetail
                ? course.Resources
                    .Where(r => r.IsVisible || canEdit)
                    .OrderBy(r => r.Order)
                    .ThenBy(r => r.Id)
                    .Select(r => TrainingCourseResourceModel.FromResource(r, canLearn || canEdit))
                    .ToList()
                : [],
            Challenges = includeDetail
                ? course.Challenges
                    .OrderBy(c => c.Order)
                    .ThenBy(c => c.ExerciseChallengeId)
                    .Select(c => TrainingCourseChallengeModel.FromChallenge(c))
                    .ToList()
                : []
        };
}

public class TrainingCourseSubmitResultModel
{
    public long SubmissionId { get; set; }

    public AnswerResult Status { get; set; }

    public bool ChapterCompleted { get; set; }

    public bool CourseCompleted { get; set; }
}

public class TrainingCourseChallengeDetailModel
{
    public int CourseId { get; set; }

    public int? ChapterId { get; set; }

    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public ChallengeCategory Category { get; set; }

    public ChallengeType Type { get; set; }

    public EnvironmentType Environment { get; set; }

    public List<string>? Hints { get; set; }

    public Difficulty Difficulty { get; set; }

    public List<string>? Tags { get; set; } = [];

    public bool Solved { get; set; }

    public int Attempts { get; set; }

    public int Limit { get; set; }

    public List<FlagStepInfo>? Flags { get; set; }

    public ClientFlagContext Context { get; set; } = new();

    public static TrainingCourseChallengeDetailModel FromInstance(
        int courseId,
        int? chapterId,
        ExerciseInstance instance,
        int attempts,
        bool solved)
    {
        return new TrainingCourseChallengeDetailModel
        {
            CourseId = courseId,
            ChapterId = chapterId,
            Id = instance.ExerciseId,
            Title = instance.Exercise.Title,
            Content = instance.Exercise.Content,
            Category = instance.Exercise.Category,
            Type = instance.Exercise.Type,
            Environment = instance.Exercise.Environment,
            Hints = instance.Exercise.Hints,
            Difficulty = instance.Exercise.Difficulty,
            Tags = instance.Exercise.Tags,
            Solved = solved,
            Attempts = attempts,
            Limit = instance.Exercise.SubmissionLimit,
            Flags = instance.Exercise.Flags is { Count: > 1 }
                ? instance.Exercise.Flags
                    .OrderBy(flag => flag.OrderIndex)
                    .Select(flag => new FlagStepInfo
                    {
                        Id = flag.Id,
                        OrderIndex = flag.OrderIndex,
                        Description = flag.Description
                    })
                    .ToList()
                : null,
            Context = new ClientFlagContext
            {
                InstanceEntry = instance.Container?.Entry,
                CloseTime = instance.Container?.ExpectStopAt,
                Url = instance.AttachmentUrl,
                FileSize = instance.Attachment?.FileSize
            }
        };
    }
}
