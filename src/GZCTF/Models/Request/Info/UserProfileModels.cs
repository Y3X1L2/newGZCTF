namespace GZCTF.Models.Request.Info;

public sealed class PublicUserTeamModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Avatar { get; set; }
}

public sealed class PublicUserCourseModel
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
}

public sealed class PublicUserProfileModel
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public Role Role { get; set; }

    public string Bio { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public DateTimeOffset RegisteredAt { get; set; }

    public PublicUserTeamModel? PublicTeam { get; set; }

    public List<PublicUserCourseModel> TaughtCourses { get; set; } = [];
}

public sealed class UserProfileMetricsModel
{
    public int Solved { get; set; }

    public int Submissions { get; set; }

    public int AcceptedSubmissions { get; set; }

    public double SuccessRate { get; set; }

    public int GameCount { get; set; }

    public int CourseCount { get; set; }

    public int ActiveDays { get; set; }
}

public sealed class UserSkillDimensionModel
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public int Solved { get; set; }

    public int Attempted { get; set; }

    public int Submissions { get; set; }

    public int AcceptedSubmissions { get; set; }

    public double SuccessRate { get; set; }

    public int BenchmarkP90 { get; set; }

    public double RadarValue { get; set; }

    public bool SampleSufficient => Attempted >= 3;
}

public sealed class UserProfileTrendPointModel
{
    public DateOnly Date { get; set; }

    public int CumulativeSolved { get; set; }

    public int Delta { get; set; }
}

public sealed class UserProfileOverviewModel
{
    public string Window { get; set; } = "365d";

    public DateTimeOffset GeneratedAt { get; set; }

    public UserProfileMetricsModel Metrics { get; set; } = new();

    public List<UserSkillDimensionModel> Dimensions { get; set; } = [];

    public List<UserProfileTrendPointModel> Trend { get; set; } = [];
}

public sealed class UserActivityPointModel
{
    public DateOnly Date { get; set; }

    public int Ctf { get; set; }

    public int Training { get; set; }

    public int Theory { get; set; }

    public int Awdp { get; set; }

    public int Penetration { get; set; }

    public int Total => Ctf + Training + Theory + Awdp + Penetration;
}

public sealed class UserProfileHistoryItemModel
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? Route { get; set; }
}

public sealed class UserProfileHistoryPageModel
{
    public List<UserProfileHistoryItemModel> Items { get; set; } = [];

    public string? NextCursor { get; set; }
}

public sealed class UserPrivateOverviewModel
{
    public int ApprovedCourses { get; set; }

    public int LearningCourses { get; set; }

    public int CompletedCourses { get; set; }

    public int PendingEnrollments { get; set; }

    public int SubmittedTheoryAssignments { get; set; }
}

public sealed class AccountSummaryContinueItemModel
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;

    public DateTimeOffset? EndsAt { get; set; }
}

public sealed class AccountSummaryModel
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public Role Role { get; set; }

    public string Bio { get; set; } = string.Empty;

    public string? Avatar { get; set; }

    public int Solved { get; set; }

    public int ActiveDays { get; set; }

    public int RunningInstances { get; set; }

    public int PendingReviews { get; set; }

    public List<AccountSummaryContinueItemModel> ContinueItems { get; set; } = [];
}
