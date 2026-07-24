using System.Globalization;
using System.Text;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Models.Request.Info;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GZCTF.Services;

public sealed class UserProfileQueryService(AppDbContext context, IMemoryCache cache)
{
    private static readonly (string Id, string Label)[] SkillDimensions =
    [
        ("web", "Web"),
        ("pwn", "Pwn"),
        ("reverse", "Reverse"),
        ("crypto", "Crypto"),
        ("forensics-ir", "Forensics / IR"),
        ("pentest-osint", "Pentest / OSINT"),
        ("misc-ai-ppc", "Misc / AI / PPC"),
        ("other", "Other")
    ];

    private static readonly TimeSpan PublicCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BenchmarkCacheDuration = TimeSpan.FromHours(1);

    public static bool TryResolveWindow(string? value, out string normalized, out TimeSpan duration)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? "365d";
        duration = normalized switch
        {
            "90d" => TimeSpan.FromDays(90),
            "365d" => TimeSpan.FromDays(365),
            _ => TimeSpan.Zero
        };
        return duration > TimeSpan.Zero;
    }

    public static bool IsHistoryTypeSupported(string? value) =>
        NormalizeHistoryType(value) is "all" or "challenges" or "games" or "training";

    public async Task<PublicUserProfileModel?> GetProfileAsync(Guid userId, CancellationToken token)
    {
        var cacheKey = $"public-user-profile:{userId:N}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PublicCacheDuration;
            var user = await context.Users.AsNoTracking()
                .Where(item => item.Id == userId)
                .Select(item => new
                {
                    item.Id,
                    item.UserName,
                    item.Role,
                    item.Bio,
                    item.AvatarHash,
                    item.RegisterTimeUtc
                })
                .SingleOrDefaultAsync(token);

            if (user is null)
                return null;

            var team = await context.Teams.AsNoTracking()
                .Where(item => item.Members.Any(member => member.Id == userId))
                .OrderByDescending(item => item.CaptainId == userId)
                .ThenBy(item => item.Id)
                .Select(item => new PublicUserTeamModel
                {
                    Id = item.Id,
                    Name = item.Name,
                    Avatar = item.AvatarHash == null ? null : $"/assets/{item.AvatarHash}/avatar"
                })
                .FirstOrDefaultAsync(token);

            var taughtCourses = await context.TrainingCourseTeachers.AsNoTracking()
                .Where(item => item.TeacherId == userId && item.Course.Status == TrainingCourseStatus.Published)
                .OrderByDescending(item => item.Course.PublishedAt)
                .ThenBy(item => item.CourseId)
                .Select(item => new PublicUserCourseModel
                {
                    Id = item.CourseId,
                    Title = item.Course.Title
                })
                .Take(12)
                .ToListAsync(token);

            return new PublicUserProfileModel
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Role = user.Role,
                Bio = user.Bio,
                Avatar = user.AvatarHash is null ? null : $"/assets/{user.AvatarHash}/avatar",
                RegisteredAt = user.RegisterTimeUtc,
                PublicTeam = team,
                TaughtCourses = taughtCourses
            };
        });
    }

    public async Task<UserProfileOverviewModel?> GetOverviewAsync(Guid userId, string window,
        CancellationToken token)
    {
        if (!TryResolveWindow(window, out var normalized, out var duration))
            return null;

        if (!await context.Users.AsNoTracking().AnyAsync(item => item.Id == userId, token))
            return null;

        var cacheKey = $"public-user-overview:{userId:N}:{normalized}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PublicCacheDuration;
            var now = DateTimeOffset.UtcNow;
            var start = now - duration;
            var ctfFacts = await PublicCtfSubmissions(userId, start, now)
                .Select(item => new
                {
                    ChallengeKey = $"gameChallenge:{item.ChallengeId}",
                    Category = item.GameChallenge!.Category,
                    item.Status,
                    item.SubmitTimeUtc
                })
                .ToListAsync(token);
            var trainingFacts = await context.TrainingCourseSubmissions.AsNoTracking()
                .Where(item => item.UserId == userId && item.SubmittedAt >= start && item.SubmittedAt < now &&
                               item.Course.Status == TrainingCourseStatus.Published &&
                               (item.Status == AnswerResult.Accepted || item.Status == AnswerResult.WrongAnswer ||
                                item.Status == AnswerResult.CheatDetected))
                .Select(item => new
                {
                    ChallengeKey = $"exerciseChallenge:{item.ExerciseChallengeId}",
                    item.ExerciseChallenge.Category,
                    item.Status,
                    SubmitTimeUtc = item.SubmittedAt
                })
                .ToListAsync(token);
            var facts = ctfFacts.Concat(trainingFacts).ToList();

            var benchmarks = await GetBenchmarksAsync(start, now, token);
            var dimensions = SkillDimensions.Select(definition =>
            {
                var dimensionFacts = facts.Where(item => SkillDimensionId(item.Category) == definition.Id).ToList();
                var accepted = dimensionFacts.Count(item => item.Status == AnswerResult.Accepted);
                var solved = dimensionFacts.Where(item => item.Status == AnswerResult.Accepted)
                    .Select(item => item.ChallengeKey).Distinct().Count();
                var benchmark = benchmarks.GetValueOrDefault(definition.Id, 5);
                return new UserSkillDimensionModel
                {
                    Id = definition.Id,
                    Label = definition.Label,
                    Solved = solved,
                    Attempted = dimensionFacts.Select(item => item.ChallengeKey).Distinct().Count(),
                    Submissions = dimensionFacts.Count,
                    AcceptedSubmissions = accepted,
                    SuccessRate = Rate(accepted, dimensionFacts.Count),
                    BenchmarkP90 = benchmark,
                    RadarValue = Math.Round(Math.Clamp(100 * Math.Log(1 + solved) / Math.Log(1 + benchmark), 0, 100), 1)
                };
            }).ToList();

            var acceptedFacts = facts.Where(item => item.Status == AnswerResult.Accepted).ToList();
            var firstSolves = acceptedFacts.GroupBy(item => item.ChallengeKey)
                .Select(group => group.Min(item => item.SubmitTimeUtc))
                .GroupBy(item => DateOnly.FromDateTime(item.UtcDateTime))
                .ToDictionary(group => group.Key, group => group.Count());
            var trend = BuildTrend(DateOnly.FromDateTime(start.UtcDateTime), DateOnly.FromDateTime(now.UtcDateTime),
                firstSolves);
            var activity = await BuildActivityAsync(userId, start, now, token);
            var gameCount = await context.UserParticipations.AsNoTracking()
                .CountAsync(item => item.UserId == userId &&
                                    item.Participation.Status == ParticipationStatus.Accepted &&
                                    !item.Game.Hidden && !item.Game.IsTest && item.Game.EndTimeUtc <= now, token);
            var courseCount = await context.TrainingCourseEnrollments.AsNoTracking()
                .CountAsync(item => item.UserId == userId &&
                                    item.Status == TrainingCourseEnrollmentStatus.Approved &&
                                    item.Course.Status == TrainingCourseStatus.Published, token);
            if (courseCount == 0)
            {
                courseCount = await context.TrainingCourseTeachers.AsNoTracking()
                    .CountAsync(item => item.TeacherId == userId &&
                                        item.Course.Status == TrainingCourseStatus.Published, token);
            }

            return new UserProfileOverviewModel
            {
                Window = normalized,
                GeneratedAt = now,
                Metrics = new UserProfileMetricsModel
                {
                    Solved = acceptedFacts.Select(item => item.ChallengeKey).Distinct().Count(),
                    Submissions = facts.Count,
                    AcceptedSubmissions = acceptedFacts.Count,
                    SuccessRate = Rate(acceptedFacts.Count, facts.Count),
                    GameCount = gameCount,
                    CourseCount = courseCount,
                    ActiveDays = activity.Count
                },
                Dimensions = dimensions,
                Trend = trend
            };
        });
    }

    public async Task<List<UserActivityPointModel>?> GetActivityAsync(Guid userId, DateOnly from, DateOnly to,
        CancellationToken token)
    {
        if (!await context.Users.AsNoTracking().AnyAsync(item => item.Id == userId, token))
            return null;

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var endExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var cacheKey = $"public-user-activity:{userId:N}:{from:yyyyMMdd}:{to:yyyyMMdd}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PublicCacheDuration;
            return (await BuildActivityAsync(userId, start, endExclusive, token)).Values
                .OrderBy(item => item.Date)
                .ToList();
        });
    }

    public async Task<UserProfileHistoryPageModel?> GetHistoryAsync(Guid userId, string? type, string? cursor,
        int count, CancellationToken token)
    {
        if (!await context.Users.AsNoTracking().AnyAsync(item => item.Id == userId, token))
            return null;

        var normalizedType = NormalizeHistoryType(type);
        var items = new List<UserProfileHistoryItemModel>();
        var now = DateTimeOffset.UtcNow;

        if (normalizedType is "all" or "challenges")
        {
            var solves = await PublicCtfSubmissions(userId, DateTimeOffset.FromUnixTimeSeconds(0), now)
                .Where(item => item.Status == AnswerResult.Accepted)
                .Select(item => new
                {
                    item.ChallengeId,
                    ChallengeTitle = item.GameChallenge!.Title,
                    item.GameId,
                    GameTitle = item.Game!.Title,
                    Category = item.GameChallenge.Category,
                    item.SubmitTimeUtc
                })
                .ToListAsync(token);
            items.AddRange(solves.GroupBy(item => item.ChallengeId).Select(group =>
            {
                var solve = group.OrderBy(item => item.SubmitTimeUtc).First();
                return new UserProfileHistoryItemModel
                {
                    Id = $"challenge:{solve.ChallengeId}",
                    Type = "challenge",
                    OccurredAt = solve.SubmitTimeUtc,
                    Title = solve.ChallengeTitle,
                    Summary = $"{solve.Category} · {solve.GameTitle}",
                    Route = $"/games/{solve.GameId}/challenges"
                };
            }));
        }

        if (normalizedType is "all" or "games")
        {
            var games = await context.UserParticipations.AsNoTracking()
                .Where(item => item.UserId == userId &&
                               item.Participation.Status == ParticipationStatus.Accepted &&
                               !item.Game.Hidden && !item.Game.IsTest && item.Game.EndTimeUtc <= now)
                .OrderByDescending(item => item.Game.EndTimeUtc)
                .Take(200)
                .Select(item => new
                {
                    item.GameId,
                    item.Game.Title,
                    item.Game.GameType,
                    item.Game.EndTimeUtc,
                    TeamName = item.Team.Name
                })
                .ToListAsync(token);
            items.AddRange(games.Select(item => new UserProfileHistoryItemModel
            {
                Id = $"game:{item.GameId}",
                Type = "competition",
                OccurredAt = item.EndTimeUtc,
                Title = item.Title,
                Summary = $"{item.GameType} · {item.TeamName} · 团队参赛记录",
                Route = $"/games/{item.GameId}"
            }));

            var awdpRows = await context.AwdpFlags.AsNoTracking()
                .Where(item => item.SubmittedByUserId == userId && item.FirstSubmittedAt != null &&
                               !item.Service.Game.Hidden && !item.Service.Game.IsTest &&
                               item.Service.Game.EndTimeUtc <= now)
                .Select(item => new
                {
                    item.Service.GameId,
                    GameTitle = item.Service.Game.Title,
                    SubmittedAt = item.FirstSubmittedAt!.Value
                })
                .ToListAsync(token);
            items.AddRange(awdpRows.GroupBy(item => new { item.GameId, item.GameTitle }).Select(group =>
                new UserProfileHistoryItemModel
                {
                    Id = $"awdp:{group.Key.GameId}",
                    Type = "awdp",
                    OccurredAt = group.Max(item => item.SubmittedAt),
                    Title = group.Key.GameTitle,
                    Summary = $"AWDP 攻击命中 {group.Count()} 次",
                    Route = $"/games/{group.Key.GameId}/awdp"
                }));

            var penetrationRows = await context.PenetrationSubmissions.AsNoTracking()
                .Where(item => item.UserId == userId && item.Status == AnswerResult.Accepted &&
                               !item.Game.Hidden && !item.Game.IsTest && item.Game.EndTimeUtc <= now)
                .Select(item => new { item.GameId, GameTitle = item.Game.Title, item.SubmittedAt })
                .ToListAsync(token);
            items.AddRange(penetrationRows.GroupBy(item => new { item.GameId, item.GameTitle }).Select(group =>
                new UserProfileHistoryItemModel
                {
                    Id = $"penetration:{group.Key.GameId}",
                    Type = "penetration",
                    OccurredAt = group.Max(item => item.SubmittedAt),
                    Title = group.Key.GameTitle,
                    Summary = $"渗透目标完成 {group.Count()} 项",
                    Route = $"/games/{group.Key.GameId}"
                }));
        }

        if (normalizedType is "all" or "training")
        {
            var completedCourses = await context.TrainingCourseProgresses.AsNoTracking()
                .Where(item => item.UserId == userId && item.Status == TrainingCourseProgressStatus.Completed &&
                               item.CompletedAt != null && item.Course.Status == TrainingCourseStatus.Published)
                .OrderByDescending(item => item.CompletedAt)
                .Take(200)
                .Select(item => new { item.CourseId, item.Course.Title, CompletedAt = item.CompletedAt!.Value })
                .ToListAsync(token);
            items.AddRange(completedCourses.Select(item => new UserProfileHistoryItemModel
            {
                Id = $"course:{item.CourseId}",
                Type = "training",
                OccurredAt = item.CompletedAt,
                Title = item.Title,
                Summary = "已完成课程",
                Route = $"/training/courses/{item.CourseId}"
            }));

            var taughtCourses = await context.TrainingCourseTeachers.AsNoTracking()
                .Where(item => item.TeacherId == userId && item.Course.Status == TrainingCourseStatus.Published)
                .OrderByDescending(item => item.Course.PublishedAt)
                .Take(200)
                .Select(item => new
                {
                    item.CourseId,
                    item.Course.Title,
                    OccurredAt = item.Course.PublishedAt ?? item.AssignedAt
                })
                .ToListAsync(token);
            items.AddRange(taughtCourses.Select(item => new UserProfileHistoryItemModel
            {
                Id = $"teaching:{item.CourseId}",
                Type = "teaching",
                OccurredAt = item.OccurredAt,
                Title = item.Title,
                Summary = "授课课程",
                Route = $"/training/courses/{item.CourseId}"
            }));
        }

        var ordered = items.OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id, StringComparer.Ordinal);
        if (TryDecodeCursor(cursor, out var cursorTime, out var cursorId))
        {
            ordered = ordered.Where(item => item.OccurredAt.UtcDateTime.Ticks < cursorTime ||
                                            item.OccurredAt.UtcDateTime.Ticks == cursorTime &&
                                            string.CompareOrdinal(item.Id, cursorId) < 0)
                .OrderByDescending(item => item.OccurredAt)
                .ThenByDescending(item => item.Id, StringComparer.Ordinal);
        }

        var page = ordered.Take(count + 1).ToList();
        var hasMore = page.Count > count;
        if (hasMore)
            page.RemoveAt(page.Count - 1);

        return new UserProfileHistoryPageModel
        {
            Items = page,
            NextCursor = hasMore && page.Count > 0 ? EncodeCursor(page[^1]) : null
        };
    }

    public async Task<UserPrivateOverviewModel> GetPrivateOverviewAsync(Guid userId, CancellationToken token)
    {
        var enrollments = context.TrainingCourseEnrollments.AsNoTracking().Where(item => item.UserId == userId);
        return new UserPrivateOverviewModel
        {
            ApprovedCourses = await enrollments.CountAsync(
                item => item.Status == TrainingCourseEnrollmentStatus.Approved, token),
            PendingEnrollments = await enrollments.CountAsync(
                item => item.Status == TrainingCourseEnrollmentStatus.Pending, token),
            LearningCourses = await context.TrainingCourseProgresses.AsNoTracking().CountAsync(
                item => item.UserId == userId && item.Status == TrainingCourseProgressStatus.Learning, token),
            CompletedCourses = await context.TrainingCourseProgresses.AsNoTracking().CountAsync(
                item => item.UserId == userId && item.Status == TrainingCourseProgressStatus.Completed, token),
            SubmittedTheoryAssignments = await context.TrainingCourseChapterTheorySheets.AsNoTracking().CountAsync(
                item => item.UserId == userId && item.Status == TheoryAnswerSheetStatus.Submitted, token)
        };
    }

    public async Task<AccountSummaryModel?> GetAccountSummaryAsync(Guid userId, CancellationToken token)
    {
        var profile = await GetProfileAsync(userId, token);
        var overview = await GetOverviewAsync(userId, "365d", token);
        if (profile is null || overview is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var runningExerciseInstances = await context.ExerciseInstances.AsNoTracking().CountAsync(
            item => item.UserId == userId && item.Container != null &&
                    item.Container.Status == ContainerStatus.Running && item.Container.ExpectStopAt > now, token);
        var runningGameInstances = await context.GameInstances.AsNoTracking().CountAsync(
            item => item.Participation.Members.Any(member => member.UserId == userId) &&
                    item.Container != null && item.Container.Status == ContainerStatus.Running &&
                    item.Container.ExpectStopAt > now, token);
        var pendingReviews = profile.Role >= Role.Teacher
            ? await context.TrainingCourseEnrollments.AsNoTracking().CountAsync(
                item => item.Status == TrainingCourseEnrollmentStatus.Pending &&
                        item.Course.Teachers.Any(teacher => teacher.TeacherId == userId), token)
            : 0;

        var continueItems = new List<AccountSummaryContinueItemModel>();
        var activeGame = await context.UserParticipations.AsNoTracking()
            .Where(item => item.UserId == userId && item.Participation.Status == ParticipationStatus.Accepted &&
                           !item.Game.Hidden && !item.Game.IsTest && item.Game.EndTimeUtc > now)
            .OrderBy(item => item.Game.EndTimeUtc)
            .Select(item => new { item.GameId, item.Game.Title, item.Game.EndTimeUtc })
            .FirstOrDefaultAsync(token);
        if (activeGame is not null)
        {
            continueItems.Add(new AccountSummaryContinueItemModel
            {
                Id = $"game:{activeGame.GameId}",
                Kind = "game",
                Title = activeGame.Title,
                Subtitle = "继续赛事",
                Route = $"/games/{activeGame.GameId}",
                EndsAt = activeGame.EndTimeUtc
            });
        }

        var runningGame = await context.GameInstances.AsNoTracking()
            .Where(item => item.Participation.Members.Any(member => member.UserId == userId) &&
                           item.Container != null && item.Container.Status == ContainerStatus.Running &&
                           item.Container.ExpectStopAt > now)
            .OrderBy(item => item.Container!.ExpectStopAt)
            .Select(item => new
            {
                item.Participation.GameId,
                item.Challenge.Title,
                item.Container!.ExpectStopAt
            })
            .FirstOrDefaultAsync(token);
        if (runningGame is not null)
        {
            continueItems.Add(new AccountSummaryContinueItemModel
            {
                Id = $"instance:{runningGame.GameId}",
                Kind = "instance",
                Title = runningGame.Title,
                Subtitle = "运行中的比赛实例",
                Route = $"/games/{runningGame.GameId}/challenges",
                EndsAt = runningGame.ExpectStopAt
            });
        }

        var recentCourse = await context.TrainingCourseProgresses.AsNoTracking()
            .Where(item => item.UserId == userId && item.Status != TrainingCourseProgressStatus.Completed &&
                           item.Course.Status == TrainingCourseStatus.Published)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new { item.CourseId, item.Course.Title })
            .FirstOrDefaultAsync(token);
        if (recentCourse is not null)
        {
            continueItems.Add(new AccountSummaryContinueItemModel
            {
                Id = $"course:{recentCourse.CourseId}",
                Kind = "training",
                Title = recentCourse.Title,
                Subtitle = "继续学习",
                Route = $"/training/courses/{recentCourse.CourseId}"
            });
        }

        return new AccountSummaryModel
        {
            Id = profile.Id,
            UserName = profile.UserName,
            Role = profile.Role,
            Bio = profile.Bio,
            Avatar = profile.Avatar,
            Solved = overview.Metrics.Solved,
            ActiveDays = overview.Metrics.ActiveDays,
            RunningInstances = runningExerciseInstances + runningGameInstances,
            PendingReviews = pendingReviews,
            ContinueItems = continueItems.DistinctBy(item => item.Id).Take(3).ToList()
        };
    }

    private IQueryable<Submission> PublicCtfSubmissions(Guid userId, DateTimeOffset start, DateTimeOffset end)
    {
        var now = DateTimeOffset.UtcNow;
        return context.Submissions.AsNoTracking().Where(item =>
            item.UserId == userId && item.SubmitTimeUtc >= start && item.SubmitTimeUtc < end &&
            item.Game != null && !item.Game.Hidden && !item.Game.IsTest && item.Game.EndTimeUtc <= now &&
            item.GameChallenge != null &&
            (item.Status == AnswerResult.Accepted || item.Status == AnswerResult.WrongAnswer ||
             item.Status == AnswerResult.CheatDetected));
    }

    private async Task<Dictionary<string, int>> GetBenchmarksAsync(DateTimeOffset start, DateTimeOffset end,
        CancellationToken token)
    {
        var key = $"public-user-benchmarks:{start:yyyyMMdd}:{end:yyyyMMdd}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = BenchmarkCacheDuration;
            var ctfFacts = await context.Submissions.AsNoTracking()
                .Where(item => item.UserId != null && item.Status == AnswerResult.Accepted &&
                               item.SubmitTimeUtc >= start && item.SubmitTimeUtc < end &&
                               item.Game != null && !item.Game.Hidden && !item.Game.IsTest &&
                               item.Game.EndTimeUtc <= end && item.GameChallenge != null)
                .Select(item => new
                {
                    UserId = item.UserId!.Value,
                    ChallengeKey = $"gameChallenge:{item.ChallengeId}",
                    Category = item.GameChallenge!.Category
                })
                .Distinct()
                .ToListAsync(token);
            var trainingFacts = await context.TrainingCourseSubmissions.AsNoTracking()
                .Where(item => item.Status == AnswerResult.Accepted && item.SubmittedAt >= start &&
                               item.SubmittedAt < end && item.Course.Status == TrainingCourseStatus.Published)
                .Select(item => new
                {
                    item.UserId,
                    ChallengeKey = $"exerciseChallenge:{item.ExerciseChallengeId}",
                    item.ExerciseChallenge.Category
                })
                .Distinct()
                .ToListAsync(token);
            var facts = ctfFacts.Concat(trainingFacts).ToList();

            return SkillDimensions.ToDictionary(definition => definition.Id, definition =>
            {
                var counts = facts.Where(item => SkillDimensionId(item.Category) == definition.Id)
                    .GroupBy(item => item.UserId)
                    .Select(group => group.Count())
                    .OrderBy(value => value)
                    .ToArray();
                if (counts.Length == 0)
                    return 5;
                var index = Math.Clamp((int)Math.Ceiling(counts.Length * 0.9) - 1, 0, counts.Length - 1);
                return Math.Max(5, counts[index]);
            });
        }) ?? SkillDimensions.ToDictionary(item => item.Id, _ => 5);
    }

    private async Task<Dictionary<DateOnly, UserActivityPointModel>> BuildActivityAsync(Guid userId,
        DateTimeOffset start, DateTimeOffset end, CancellationToken token)
    {
        var points = new Dictionary<DateOnly, UserActivityPointModel>();
        void Add(IEnumerable<DateTimeOffset> dates, Action<UserActivityPointModel> increment)
        {
            foreach (var date in dates)
            {
                var key = DateOnly.FromDateTime(date.UtcDateTime);
                if (!points.TryGetValue(key, out var point))
                    points[key] = point = new UserActivityPointModel { Date = key };
                increment(point);
            }
        }

        var ctf = await PublicCtfSubmissions(userId, start, end)
            .Select(item => item.SubmitTimeUtc).ToListAsync(token);
        Add(ctf, point => point.Ctf++);

        var training = await context.TrainingCourseSubmissions.AsNoTracking()
            .Where(item => item.UserId == userId && item.SubmittedAt >= start && item.SubmittedAt < end &&
                           item.Course.Status == TrainingCourseStatus.Published)
            .Select(item => item.SubmittedAt).ToListAsync(token);
        var chapterUpdates = await context.TrainingChapterProgresses.AsNoTracking()
            .Where(item => item.UserId == userId && item.UpdatedAt >= start && item.UpdatedAt < end &&
                           item.Status != TrainingCourseProgressStatus.NotStarted &&
                           item.Chapter.Course.Status == TrainingCourseStatus.Published)
            .Select(item => item.UpdatedAt).ToListAsync(token);
        var checkIns = await context.TrainingCheckIns.AsNoTracking()
            .Where(item => item.UserId == userId && item.CheckedAt >= start && item.CheckedAt < end)
            .Select(item => item.CheckedAt).ToListAsync(token);
        Add(training.Concat(chapterUpdates).Concat(checkIns), point => point.Training++);

        var theory = await context.TheoryAnswerSheets.AsNoTracking()
            .Where(item => item.UserId == userId && item.SubmittedAt != null && item.SubmittedAt >= start &&
                           item.SubmittedAt < end && !item.Game.Hidden && !item.Game.IsTest &&
                           item.Game.EndTimeUtc <= end)
            .Select(item => item.SubmittedAt!.Value).ToListAsync(token);
        var trainingTheory = await context.TrainingCourseChapterTheorySheets.AsNoTracking()
            .Where(item => item.UserId == userId && item.SubmittedAt != null && item.SubmittedAt >= start &&
                           item.SubmittedAt < end && item.Course.Status == TrainingCourseStatus.Published)
            .Select(item => item.SubmittedAt!.Value).ToListAsync(token);
        Add(theory.Concat(trainingTheory), point => point.Theory++);

        var awdp = await context.AwdpFlags.AsNoTracking()
            .Where(item => item.SubmittedByUserId == userId && item.FirstSubmittedAt != null &&
                           item.FirstSubmittedAt >= start && item.FirstSubmittedAt < end &&
                           !item.Service.Game.Hidden && !item.Service.Game.IsTest && item.Service.Game.EndTimeUtc <= end)
            .Select(item => item.FirstSubmittedAt!.Value).ToListAsync(token);
        Add(awdp, point => point.Awdp++);

        var penetration = await context.PenetrationSubmissions.AsNoTracking()
            .Where(item => item.UserId == userId && item.SubmittedAt >= start && item.SubmittedAt < end &&
                           item.Status == AnswerResult.Accepted && !item.Game.Hidden && !item.Game.IsTest &&
                           item.Game.EndTimeUtc <= end)
            .Select(item => item.SubmittedAt).ToListAsync(token);
        Add(penetration, point => point.Penetration++);

        return points;
    }

    private static List<UserProfileTrendPointModel> BuildTrend(DateOnly start, DateOnly end,
        IReadOnlyDictionary<DateOnly, int> dailySolves)
    {
        var result = new List<UserProfileTrendPointModel>();
        var cumulative = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var delta = dailySolves.GetValueOrDefault(date);
            cumulative += delta;
            result.Add(new UserProfileTrendPointModel
            {
                Date = date,
                Delta = delta,
                CumulativeSolved = cumulative
            });
        }
        return result;
    }

    private static double Rate(int accepted, int total) =>
        total == 0 ? 0 : Math.Round((double)accepted / total * 100, 1);

    private static string SkillDimensionId(ChallengeCategory category) => category switch
    {
        ChallengeCategory.Web => "web",
        ChallengeCategory.Pwn => "pwn",
        ChallengeCategory.Reverse => "reverse",
        ChallengeCategory.Crypto => "crypto",
        ChallengeCategory.Forensics or ChallengeCategory.IR => "forensics-ir",
        ChallengeCategory.Pentest or ChallengeCategory.OSINT => "pentest-osint",
        ChallengeCategory.Misc or ChallengeCategory.AI or ChallengeCategory.PPC => "misc-ai-ppc",
        _ => "other"
    };

    private static string NormalizeHistoryType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "challenge" or "challenges" => "challenges",
        "game" or "competition" or "games" => "games",
        "course" or "teaching" or "training" => "training",
        null or "" or "all" => "all",
        var other => other
    };

    private static string EncodeCursor(UserProfileHistoryItemModel item)
    {
        var value = $"{item.OccurredAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture)}|{item.Id}";
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(value));
    }

    private static bool TryDecodeCursor(string? cursor, out long ticks, out string id)
    {
        ticks = 0;
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(cursor))
            return false;
        try
        {
            var value = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            var separator = value.IndexOf('|');
            return separator > 0 && long.TryParse(value[..separator], CultureInfo.InvariantCulture, out ticks) &&
                   !string.IsNullOrWhiteSpace(id = value[(separator + 1)..]);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
