using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GZCTF.Models;

public class AppDbContext(DbContextOptions<AppDbContext> options) :
    IdentityDbContext<UserInfo, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        TypeInfoResolver = new AppJsonSerializerContext()
    };

    public DbSet<Post> Posts { get; set; } = null!;
    public DbSet<Game> Games { get; set; } = null!;
    public DbSet<Team> Teams { get; set; } = null!;
    public DbSet<TeamJoinRequest> TeamJoinRequests { get; set; } = null!;
    public DbSet<StudentGroup> StudentGroups { get; set; } = null!;
    public DbSet<StudentGroupMember> StudentGroupMembers { get; set; } = null!;
    public DbSet<StudentGroupManager> StudentGroupManagers { get; set; } = null!;
    public DbSet<Config> Configs { get; set; } = null!;
    public DbSet<LogModel> Logs { get; set; } = null!;
    public DbSet<Division> Divisions { get; set; } = null!;
    public DbSet<LocalFile> Files { get; set; } = null!;
    public DbSet<CheatInfo> CheatInfo { get; set; } = null!;
    public DbSet<Container> Containers { get; set; } = null!;
    public DbSet<GameEvent> GameEvents { get; set; } = null!;
    public DbSet<Submission> Submissions { get; set; } = null!;
    public DbSet<Attachment> Attachments { get; set; } = null!;
    public DbSet<GameNotice> GameNotices { get; set; } = null!;
    public DbSet<FlagContext> FlagContexts { get; set; } = null!;
    public DbSet<Participation> Participations { get; set; } = null!;
    public DbSet<GameInstance> GameInstances { get; set; } = null!;
    public DbSet<GameChallenge> GameChallenges { get; set; } = null!;
    public DbSet<FirstSolve> FirstSolves { get; set; } = null!;
    public DbSet<ExerciseInstance> ExerciseInstances { get; set; } = null!;
    public DbSet<ExerciseChallenge> ExerciseChallenges { get; set; } = null!;
    public DbSet<UserParticipation> UserParticipations { get; set; } = null!;
    public DbSet<ExerciseDependency> ExerciseDependencies { get; set; } = null!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<ApiToken> ApiTokens { get; set; } = null!;
    public DbSet<ImageTemplate> ImageTemplates { get; set; } = null!;
    public DbSet<VmInstance> VmInstances => Set<VmInstance>();
    public DbSet<WorkerNode> WorkerNodes { get; set; } = null!;
    public DbSet<DeploymentTarget> DeploymentTargets { get; set; } = null!;
    public DbSet<GamePhase> GamePhases => Set<GamePhase>();
    public DbSet<TimeSlot> TimeSlots { get; set; } = null!;
    public DbSet<ScoringRule> ScoringRules { get; set; } = null!;
    public DbSet<Stage> Stages { get; set; } = null!;
    public DbSet<ScenarioInstance> ScenarioInstances { get; set; } = null!;
    public DbSet<IRCheckpoint> IRCheckpoints { get; set; } = null!;
    public DbSet<IRInstance> IRInstances { get; set; } = null!;
    public DbSet<TheoryQuestionBankItem> TheoryQuestionBankItems { get; set; } = null!;
    public DbSet<TheoryPaper> TheoryPapers { get; set; } = null!;
    public DbSet<TheoryPaperQuestion> TheoryPaperQuestions { get; set; } = null!;
    public DbSet<TheoryAnswerSheet> TheoryAnswerSheets { get; set; } = null!;
    public DbSet<TheorySubmissionAnswer> TheorySubmissionAnswers { get; set; } = null!;
    public DbSet<AwdpService> AwdpServices { get; set; } = null!;
    public DbSet<AwdpServiceInstance> AwdpServiceInstances { get; set; } = null!;
    public DbSet<AwdpRound> AwdpRounds { get; set; } = null!;
    public DbSet<AwdpFlag> AwdpFlags { get; set; } = null!;
    public DbSet<AwdpCheckerTask> AwdpCheckerTasks { get; set; } = null!;
    public DbSet<AwdpPatchSubmission> AwdpPatchSubmissions { get; set; } = null!;
    public DbSet<AwdpResetRecord> AwdpResetRecords { get; set; } = null!;
    public DbSet<AwdpRecoveryRecord> AwdpRecoveryRecords { get; set; } = null!;
    public DbSet<PenetrationConfig> PenetrationConfigs { get; set; } = null!;
    public DbSet<PenetrationPublishedSnapshot> PenetrationPublishedSnapshots { get; set; } = null!;
    public DbSet<PenetrationNetwork> PenetrationNetworks { get; set; } = null!;
    public DbSet<PenetrationNode> PenetrationNodes { get; set; } = null!;
    public DbSet<PenetrationInterface> PenetrationInterfaces { get; set; } = null!;
    public DbSet<PenetrationEdge> PenetrationEdges { get; set; } = null!;
    public DbSet<PenetrationScoreItem> PenetrationScoreItems { get; set; } = null!;
    public DbSet<PenetrationTeamEnvironment> PenetrationTeamEnvironments { get; set; } = null!;
    public DbSet<PenetrationRuntimeNode> PenetrationRuntimeNodes { get; set; } = null!;
    public DbSet<PenetrationSubmission> PenetrationSubmissions { get; set; } = null!;
    public DbSet<PenetrationResetRecord> PenetrationResetRecords { get; set; } = null!;
    public DbSet<TrainingDirection> TrainingDirections { get; set; } = null!;
    public DbSet<TrainingModule> TrainingModules { get; set; } = null!;
    public DbSet<TrainingModuleVisibility> TrainingModuleVisibilities { get; set; } = null!;
    public DbSet<TrainingModuleChallenge> TrainingModuleChallenges { get; set; } = null!;
    public DbSet<TrainingCtfSubmission> TrainingCtfSubmissions { get; set; } = null!;
    public DbSet<TheoryTrainingPlan> TheoryTrainingPlans { get; set; } = null!;
    public DbSet<TheoryTrainingPlanQuestion> TheoryTrainingPlanQuestions { get; set; } = null!;
    public DbSet<TheoryTrainingSession> TheoryTrainingSessions { get; set; } = null!;
    public DbSet<TheoryTrainingSessionQuestion> TheoryTrainingSessionQuestions { get; set; } = null!;
    public DbSet<TrainingArticleProgress> TrainingArticleProgresses { get; set; } = null!;
    public DbSet<TrainingModuleProgress> TrainingModuleProgresses { get; set; } = null!;
    public DbSet<TrainingCourse> TrainingCourses { get; set; } = null!;
    public DbSet<TrainingCourseTeacher> TrainingCourseTeachers { get; set; } = null!;
    public DbSet<TrainingCourseEnrollment> TrainingCourseEnrollments { get; set; } = null!;
    public DbSet<TrainingCourseChapter> TrainingCourseChapters { get; set; } = null!;
    public DbSet<TrainingCourseResource> TrainingCourseResources { get; set; } = null!;
    public DbSet<TrainingCourseChallenge> TrainingCourseChallenges { get; set; } = null!;
    public DbSet<TrainingCourseChapterChallenge> TrainingCourseChapterChallenges { get; set; } = null!;
    public DbSet<TrainingCourseSubmission> TrainingCourseSubmissions { get; set; } = null!;
    public DbSet<TrainingCourseProgress> TrainingCourseProgresses { get; set; } = null!;
    public DbSet<TrainingCheckIn> TrainingCheckIns { get; set; } = null!;
    public DbSet<TrainingChapterProgress> TrainingChapterProgresses { get; set; } = null!;
    public DbSet<TrainingCourseTheoryQuestion> TrainingCourseTheoryQuestions { get; set; } = null!;
    public DbSet<TrainingCourseChapterTheoryPaper> TrainingCourseChapterTheoryPapers { get; set; } = null!;
    public DbSet<TrainingCourseChapterTheoryQuestion> TrainingCourseChapterTheoryQuestions { get; set; } = null!;
    public DbSet<TrainingCourseChapterTheorySheet> TrainingCourseChapterTheorySheets { get; set; } = null!;
    public DbSet<TrainingCourseChapterTheoryAnswer> TrainingCourseChapterTheoryAnswers { get; set; } = null!;

    private static ValueConverter<T?, string> GetJsonConverter<T>() where T : class, new() =>
        new(
            v => JsonSerializer.Serialize(v ?? new(), JsonOptions),
            v => JsonSerializer.Deserialize<T>(v, JsonOptions)
        );

    private static ValueComparer<TList> GetEnumerableComparer<TList, T>()
        where T : notnull
        where TList : IEnumerable<T>, new() =>
        new(
            (c1, c2) => (c1 == null && c2 == null) || (c2 != null && c1 != null && c1.SequenceEqual(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())));

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // var setConverter = GetJsonConverter<HashSet<string>>();
        // var setComparer = GetEnumerableComparer<HashSet<string>, string>();
        var listConverter = GetJsonConverter<List<string>>();
        var listComparer = GetEnumerableComparer<List<string>, string>();
        var intListConverter = GetJsonConverter<List<int>>();
        var intListComparer = GetEnumerableComparer<List<int>, int>();
        var trainingRuleConverter = GetJsonConverter<TrainingCompletionRule>();
        var theoryQuestionTypeListConverter = GetJsonConverter<List<TheoryQuestionType>>();
        var theoryQuestionTypeListComparer = GetEnumerableComparer<List<TheoryQuestionType>, TheoryQuestionType>();

        builder.Entity<UserInfo>(entity =>
        {
            entity.Property(e => e.Role)
                .HasConversion<int>();

            entity.Property(e => e.UserName)
                .HasMaxLength(16);

            entity.Property(e => e.ExerciseVisible)
                .HasDefaultValue(true);

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Game>(entity =>
        {
            entity.HasMany(e => e.GameEvents)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.GameNotices)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Challenges)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Divisions)
                .WithOne(e => e.Game)
                .HasForeignKey(e => e.GameId);

            entity.HasMany(e => e.Teams)
                .WithMany(e => e.Games)
                .UsingEntity<Participation>(
                    e => e.HasOne(p => p.Team)
                        .WithMany(t => t.Participations)
                        .HasForeignKey(p => p.TeamId),
                    e => e.HasOne(p => p.Game)
                        .WithMany(g => g.Participations)
                        .HasForeignKey(p => p.GameId)
                );
        });

        builder.Entity<Post>(entity =>
        {
            entity.HasOne(e => e.Author)
                .WithMany()
                .HasForeignKey(e => e.AuthorId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Tags)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Navigation(e => e.Author).AutoInclude();
        });

        builder.Entity<Team>(entity =>
        {
            entity.HasMany(e => e.Members)
                .WithMany(e => e.Teams);

            entity.HasOne(e => e.Captain)
                .WithMany()
                .HasForeignKey(e => e.CaptainId);

            entity.HasOne(e => e.Captain)
                .WithMany()
                .HasForeignKey(e => e.CaptainId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TeamJoinRequest>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ReviewedBy)
                .WithMany()
                .HasForeignKey(e => e.ReviewedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => new { e.TeamId, e.UserId, e.Status });
            entity.Navigation(e => e.User).AutoInclude();
            entity.Navigation(e => e.Team).AutoInclude();
        });

        builder.Entity<StudentGroup>(entity =>
        {
            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Members)
                .WithOne(e => e.Group)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Managers)
                .WithOne(e => e.Group)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<StudentGroupMember>(entity =>
        {
            entity.HasOne(e => e.Student)
                .WithMany()
                .HasForeignKey(e => e.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AddedBy)
                .WithMany()
                .HasForeignKey(e => e.AddedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Student).AutoInclude();
        });

        builder.Entity<StudentGroupManager>(entity =>
        {
            entity.Property(e => e.RoleInGroup)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Manager)
                .WithMany()
                .HasForeignKey(e => e.ManagerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AddedBy)
                .WithMany()
                .HasForeignKey(e => e.AddedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Manager).AutoInclude();
        });

        builder.Entity<Participation>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<int>();

            entity.HasMany(e => e.Instances).WithOne();

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.Participation)
                .HasForeignKey(e => e.ParticipationId);

            entity.HasMany(e => e.FirstSolves)
                .WithOne(e => e.Participation)
                .HasForeignKey(e => e.ParticipationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Members)
                .WithOne(e => e.Participation)
                .HasForeignKey(e => e.ParticipationId);

            entity.HasOne(e => e.Writeup)
                .WithMany();

            entity.HasOne(e => e.Division)
                .WithMany()
                .HasForeignKey(e => e.DivisionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Game).AutoInclude();
            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.Members).AutoInclude();
            entity.Navigation(e => e.Writeup).AutoInclude();

            entity.HasMany(e => e.Challenges)
                .WithMany(e => e.Teams)
                .UsingEntity<GameInstance>(
                    e => e.HasOne(i => i.Challenge)
                        .WithMany(c => c.Instances)
                        .HasForeignKey(i => i.ChallengeId),
                    e => e.HasOne(i => i.Participation)
                        .WithMany(p => p.Instances)
                        .HasForeignKey(i => i.ParticipationId)
                        .OnDelete(DeleteBehavior.Cascade),
                    e => e.HasKey(i => new { i.ChallengeId, i.ParticipationId })
                );
        });

        builder.Entity<GameInstance>(entity =>
        {
            entity.HasOne(e => e.FlagContext)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Challenge).AutoInclude();
        });

        builder.Entity<ExerciseInstance>(entity =>
        {
            entity.HasOne(e => e.FlagContext)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Exercise).AutoInclude();
        });

        builder.Entity<Container>(entity =>
        {
            entity.HasIndex(c => c.Status);
        });

        builder.Entity<GameChallenge>(entity =>
        {
            entity.Property(e => e.Hints)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.NetworkMode)
                .HasConversion<byte>()
                .HasDefaultValue(NetworkMode.Open);

            entity.HasMany(e => e.Flags)
                .WithOne(e => e.Challenge)
                .HasForeignKey(e => e.ChallengeId);

            entity.HasMany(e => e.Submissions)
                .WithOne(e => e.GameChallenge)
                .HasForeignKey(e => e.ChallengeId);

            entity.HasMany(e => e.FirstSolves)
                .WithOne(e => e.Challenge)
                .HasForeignKey(e => e.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Attachment)
                .WithMany()
                .HasForeignKey(e => e.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.TestContainer)
                .WithMany()
                .HasForeignKey(e => e.TestContainerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.Attachment).AutoInclude();
            entity.Navigation(e => e.TestContainer).AutoInclude();

            entity.HasIndex(e => e.GameId);

            entity.HasMany(e => e.DivisionConfigs)
                .WithOne(e => e.Challenge)
                .HasForeignKey(e => e.ChallengeId);
        });

        builder.Entity<Division>(entity =>
        {
            entity.HasMany(e => e.ChallengeConfigs)
                .WithOne(e => e.Division)
                .HasForeignKey(e => e.DivisionId);
        });

        builder.Entity<ExerciseChallenge>(entity =>
        {
            entity.Property(e => e.Hints)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.NetworkMode)
                .HasConversion<byte>()
                .HasDefaultValue(NetworkMode.Open);

            entity.HasOne(e => e.Attachment)
                .WithMany()
                .HasForeignKey(e => e.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(e => e.Tags)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.HasMany(e => e.Flags)
                .WithOne(e => e.Exercise)
                .HasForeignKey(e => e.ExerciseId);

            entity.HasOne(e => e.TestContainer)
                .WithMany()
                .HasForeignKey(e => e.TestContainerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.TrainingCourse)
                .WithMany()
                .HasForeignKey(e => e.TrainingCourseId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Dependencies)
                .WithMany()
                .UsingEntity<ExerciseDependency>(
                    l => l.HasOne(e => e.Target).WithMany().HasForeignKey(e => e.TargetId),
                    r => r.HasOne(e => e.Source).WithMany().HasForeignKey(e => e.SourceId)
                );

            entity.Navigation(e => e.Attachment).AutoInclude();
            entity.Navigation(e => e.TestContainer).AutoInclude();
        });

        builder.Entity<Submission>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>();
            entity.Property(e => e.SubmissionType)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.ReviewedBy)
                .WithMany()
                .HasForeignKey(e => e.ReviewedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.FlagContext)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.Status);

            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.User).AutoInclude();
            entity.Navigation(e => e.GameChallenge).AutoInclude();
        });

        builder.Entity<FlagContext>(entity =>
        {
            entity.HasOne(e => e.Attachment)
                .WithMany()
                .HasForeignKey(e => e.AttachmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(f => f.Challenge)
                .WithMany()
                .HasForeignKey(f => f.ChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Attachment).AutoInclude();
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.HasOne(e => e.LocalFile)
                .WithMany()
                .HasForeignKey(e => e.LocalFileId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Navigation(e => e.LocalFile).AutoInclude();
        });

        builder.Entity<GameNotice>(entity =>
        {
            entity.Property(e => e.Values)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);
        });

        builder.Entity<GameEvent>(entity =>
        {
            entity.Property(e => e.Values)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);

            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.User).AutoInclude();
        });

        builder.Entity<CheatInfo>(entity =>
        {
            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId);

            entity.HasOne(e => e.SourceTeam)
                .WithMany()
                .HasForeignKey(e => e.SourceTeamId);

            entity.HasOne(e => e.SubmitTeam)
                .WithMany()
                .HasForeignKey(e => e.SubmitTeamId);

            entity.HasOne(e => e.Submission)
                .WithMany()
                .HasForeignKey(e => e.SubmissionId);

            entity.HasKey(e => e.SubmissionId);
        });

        builder.Entity<FirstSolve>(entity =>
        {
            entity.HasKey(e => new { e.ParticipationId, e.ChallengeId, e.FlagId });

            entity.HasOne(e => e.Submission)
                .WithMany()
                .HasForeignKey(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FlagContext)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<UserParticipation>(entity =>
        {
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId);

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId);

            entity.HasKey(e => new { e.GameId, e.TeamId, e.UserId });
        });

        builder.Entity<TheoryQuestionBankItem>(entity =>
        {
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.BankName)
                .HasMaxLength(128);

            entity.Property(e => e.Options)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.AnswerIndexes)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);
        });

        builder.Entity<TheoryPaper>(entity =>
        {
            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Questions)
                .WithOne(e => e.Paper)
                .HasForeignKey(e => e.PaperId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TheoryPaperQuestion>(entity =>
        {
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.Options)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.AnswerIndexes)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);

            entity.HasOne(e => e.SourceQuestion)
                .WithMany()
                .HasForeignKey(e => e.SourceQuestionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TheoryAnswerSheet>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Paper)
                .WithMany()
                .HasForeignKey(e => e.PaperId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Participation)
                .WithMany()
                .HasForeignKey(e => e.ParticipationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Answers)
                .WithOne(e => e.AnswerSheet)
                .HasForeignKey(e => e.AnswerSheetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Participation).AutoInclude();
            entity.Navigation(e => e.User).AutoInclude();
        });

        builder.Entity<TheorySubmissionAnswer>(entity =>
        {
            entity.Property(e => e.SelectedIndexes)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);

            entity.HasOne(e => e.PaperQuestion)
                .WithMany()
                .HasForeignKey(e => e.PaperQuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ApiToken>(entity =>
        {
            entity.HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LogModel>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(Limits.MaxLogStatusLength);
        });

        builder.Entity<ImageTemplate>(entity =>
        {
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.TrainingCourseId);

            entity.HasOne(e => e.TrainingCourse)
                .WithMany()
                .HasForeignKey(e => e.TrainingCourseId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PenetrationConfig>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Game)
                .WithOne()
                .HasForeignKey<PenetrationConfig>(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PenetrationPublishedSnapshot>(entity =>
        {
            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PenetrationNetwork>(entity =>
        {
            entity.HasIndex(e => new { e.ConfigId, e.TopologyKey }).IsUnique();

            entity.Property(e => e.ZoneType)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.DefaultPolicy)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Config)
                .WithMany(e => e.Networks)
                .HasForeignKey(e => e.ConfigId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PenetrationNode>(entity =>
        {
            entity.HasIndex(e => new { e.ConfigId, e.TopologyKey }).IsUnique();

            entity.Property(e => e.NodeType)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Config)
                .WithMany(e => e.Nodes)
                .HasForeignKey(e => e.ConfigId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Network)
                .WithMany(e => e.Nodes)
                .HasForeignKey(e => e.NetworkId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ImageTemplate)
                .WithMany()
                .HasForeignKey(e => e.ImageTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PenetrationInterface>(entity =>
        {
            entity.HasIndex(e => new { e.NodeId, e.TopologyKey }).IsUnique();

            entity.HasOne(e => e.Node)
                .WithMany(e => e.Interfaces)
                .HasForeignKey(e => e.NodeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Network)
                .WithMany(e => e.Interfaces)
                .HasForeignKey(e => e.NetworkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PenetrationEdge>(entity =>
        {
            entity.HasIndex(e => new { e.ConfigId, e.TopologyKey }).IsUnique();

            entity.Property(e => e.SourceKind)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.TargetKind)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.Protocol)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.PolicyAction)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Config)
                .WithMany(e => e.Edges)
                .HasForeignKey(e => e.ConfigId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PenetrationScoreItem>(entity =>
        {
            entity.HasIndex(e => new { e.NodeId, e.TopologyKey }).IsUnique();

            entity.HasOne(e => e.Node)
                .WithMany(e => e.ScoreItems)
                .HasForeignKey(e => e.NodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PenetrationTeamEnvironment>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PenetrationRuntimeNode>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Environment)
                .WithMany(e => e.RuntimeNodes)
                .HasForeignKey(e => e.EnvironmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.TopologyNode)
                .WithMany()
                .HasForeignKey(e => e.TopologyNodeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Container)
                .WithMany()
                .HasForeignKey(e => e.ContainerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<PenetrationSubmission>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasIndex(e => new { e.GameId, e.TeamId, e.PublishedVersion, e.ScoreItemTopologyKey });

            entity.HasOne(e => e.Game)
                .WithMany()
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Participation)
                .WithMany()
                .HasForeignKey(e => e.ParticipationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ScoreItem)
                .WithMany()
                .HasForeignKey(e => e.ScoreItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.User).AutoInclude();
            entity.Navigation(e => e.ScoreItem).AutoInclude();
        });

        builder.Entity<PenetrationResetRecord>(entity =>
        {
            entity.HasOne(e => e.Environment)
                .WithMany()
                .HasForeignKey(e => e.EnvironmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<VmInstance>(entity =>
        {
            entity.HasOne(v => v.Challenge).WithMany().HasForeignKey(v => v.ChallengeId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingDirection>(entity =>
        {
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Modules)
                .WithOne(e => e.Direction)
                .HasForeignKey(e => e.DirectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingModule>(entity =>
        {
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.ArticleContentType)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.CompletionRule)
                .HasConversion(trainingRuleConverter);

            entity.HasOne(e => e.Parent)
                .WithMany(e => e.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.EnvironmentTemplate)
                .WithMany()
                .HasForeignKey(e => e.EnvironmentTemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingModuleVisibility>(entity =>
        {
            entity.Property(e => e.VisibilityType)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Module)
                .WithMany(e => e.Visibilities)
                .HasForeignKey(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Group)
                .WithMany()
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingModuleChallenge>(entity =>
        {
            entity.HasOne(e => e.Module)
                .WithMany(e => e.Challenges)
                .HasForeignKey(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ExerciseChallenge)
                .WithMany()
                .HasForeignKey(e => e.ExerciseChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingCtfSubmission>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Module)
                .WithMany()
                .HasForeignKey(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ExerciseChallenge)
                .WithMany()
                .HasForeignKey(e => e.ExerciseChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Flag)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TheoryTrainingPlan>(entity =>
        {
            entity.Property(e => e.Mode)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.QuestionTypes)
                .HasConversion(theoryQuestionTypeListConverter)
                .Metadata
                .SetValueComparer(theoryQuestionTypeListComparer);

            entity.HasOne(e => e.Module)
                .WithOne(e => e.TheoryPlan)
                .HasForeignKey<TheoryTrainingPlan>(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TheoryTrainingPlanQuestion>(entity =>
        {
            entity.HasOne(e => e.Plan)
                .WithMany(e => e.Questions)
                .HasForeignKey(e => e.PlanId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SourceQuestion)
                .WithMany()
                .HasForeignKey(e => e.SourceQuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TheoryTrainingSession>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Plan)
                .WithMany()
                .HasForeignKey(e => e.PlanId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Module)
                .WithMany()
                .HasForeignKey(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Questions)
                .WithOne(e => e.Session)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TheoryTrainingSessionQuestion>(entity =>
        {
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.Options)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.AnswerIndexes)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);

            entity.Property(e => e.SelectedIndexes)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);
        });

        builder.Entity<TrainingArticleProgress>(entity =>
        {
            entity.HasOne(e => e.Module)
                .WithMany()
                .HasForeignKey(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingModuleProgress>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Module)
                .WithMany()
                .HasForeignKey(e => e.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingCourse>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.EnrollmentPolicy)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.Tags)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Teachers)
                .WithOne(e => e.Course)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Enrollments)
                .WithOne(e => e.Course)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Chapters)
                .WithOne(e => e.Course)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Resources)
                .WithOne(e => e.Course)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Challenges)
                .WithOne(e => e.Course)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.TheoryQuestions)
                .WithOne(e => e.Course)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.TheoryPapers)
                .WithOne(e => e.Course)
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingCourseTeacher>(entity =>
        {
            entity.Property(e => e.Role)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Teacher)
                .WithMany()
                .HasForeignKey(e => e.TeacherId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssignedBy)
                .WithMany()
                .HasForeignKey(e => e.AssignedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingCourseEnrollment>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ReviewedBy)
                .WithMany()
                .HasForeignKey(e => e.ReviewedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingCourseChapter>(entity =>
        {
            entity.Property(e => e.ContentType)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.VideoProvider)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Parent)
                .WithMany(e => e.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.VideoFile)
                .WithMany()
                .HasForeignKey(e => e.VideoFileId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.TheoryPaper)
                .WithOne(e => e.Chapter)
                .HasForeignKey<TrainingCourseChapterTheoryPaper>(e => e.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingCourseResource>(entity =>
        {
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.LocalFile)
                .WithMany()
                .HasForeignKey(e => e.LocalFileId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingCourseChallenge>(entity =>
        {
            entity.HasOne(e => e.ExerciseChallenge)
                .WithMany()
                .HasForeignKey(e => e.ExerciseChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingCourseChapterChallenge>(entity =>
        {
            entity.HasOne(e => e.Chapter)
                .WithMany(e => e.Challenges)
                .HasForeignKey(e => e.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CourseChallenge)
                .WithMany()
                .HasForeignKey(e => new { e.CourseId, e.ExerciseChallengeId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingCourseSubmission>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Course)
                .WithMany()
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Chapter)
                .WithMany()
                .HasForeignKey(e => e.ChapterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ExerciseChallenge)
                .WithMany()
                .HasForeignKey(e => e.ExerciseChallengeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Flag)
                .WithMany()
                .HasForeignKey(e => e.FlagId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingCourseProgress>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Course)
                .WithMany()
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingCheckIn>(entity =>
        {
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingChapterProgress>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Chapter)
                .WithMany()
                .HasForeignKey(e => e.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingCourseTheoryQuestion>(entity =>
        {
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.Options)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.AnswerIndexes)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingCourseChapterTheoryPaper>(entity =>
        {
            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Questions)
                .WithOne(e => e.Paper)
                .HasForeignKey(e => e.PaperId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingCourseChapterTheoryQuestion>(entity =>
        {
            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.Options)
                .HasConversion(listConverter)
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.AnswerIndexes)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);

            entity.HasOne(e => e.SourceQuestion)
                .WithMany()
                .HasForeignKey(e => e.SourceQuestionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TrainingCourseChapterTheorySheet>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.Course)
                .WithMany()
                .HasForeignKey(e => e.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Chapter)
                .WithMany()
                .HasForeignKey(e => e.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Paper)
                .WithMany()
                .HasForeignKey(e => e.PaperId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Answers)
                .WithOne(e => e.Sheet)
                .HasForeignKey(e => e.SheetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrainingCourseChapterTheoryAnswer>(entity =>
        {
            entity.Property(e => e.SelectedIndexes)
                .HasConversion(intListConverter)
                .Metadata
                .SetValueComparer(intListComparer);

            entity.HasOne(e => e.PaperQuestion)
                .WithMany()
                .HasForeignKey(e => e.PaperQuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AwdpService>(entity =>
        {
            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => new { e.GameId, e.Name }).IsUnique();

            entity.HasOne(e => e.Game)
                .WithMany(e => e.AwdpServices)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AwdpServiceInstance>(entity =>
        {
            entity.Property(e => e.ContainerId)
                .HasColumnName("ContainerId1");

            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.TeamId);
            entity.HasIndex(e => new { e.ServiceId, e.TeamId }).IsUnique();

            entity.HasOne(e => e.Container)
                .WithMany()
                .HasForeignKey(e => e.ContainerId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Service)
                .WithMany(e => e.Instances)
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AwdpRound>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasIndex(e => e.GameId);
            entity.HasIndex(e => new { e.GameId, e.RoundNumber }).IsUnique();

            entity.HasOne(e => e.Game)
                .WithMany(e => e.AwdpRounds)
                .HasForeignKey(e => e.GameId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AwdpFlag>(entity =>
        {
            entity.HasIndex(e => e.RoundId);
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.TeamId);
            entity.HasIndex(e => e.SubmittedByTeamId);
            entity.HasIndex(e => e.FlagValue).IsUnique();
            entity.HasIndex(e => new { e.RoundId, e.ServiceId, e.TeamId }).IsUnique();

            entity.HasOne(e => e.Round)
                .WithMany(e => e.Flags)
                .HasForeignKey(e => e.RoundId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Service)
                .WithMany()
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SubmittedByTeam)
                .WithMany()
                .HasForeignKey(e => e.SubmittedByTeamId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SubmittedByUser)
                .WithMany()
                .HasForeignKey(e => e.SubmittedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<AwdpCheckerTask>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasIndex(e => e.RoundId);
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.TeamId);
            entity.HasIndex(e => new { e.RoundId, e.ServiceId, e.TeamId }).IsUnique();

            entity.HasOne(e => e.Round)
                .WithMany(e => e.CheckerTasks)
                .HasForeignKey(e => e.RoundId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Service)
                .WithMany()
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AwdpPatchSubmission>(entity =>
        {
            entity.Property(e => e.CheckerResult)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.ExpResult)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.FinalStatus)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasIndex(e => e.RoundId);
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.TeamId);
            entity.HasIndex(e => new { e.RoundId, e.ServiceId, e.TeamId, e.SubmittedAt });

            entity.HasOne(e => e.Round)
                .WithMany(e => e.PatchSubmissions)
                .HasForeignKey(e => e.RoundId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Service)
                .WithMany()
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AwdpResetRecord>(entity =>
        {
            entity.Property(e => e.ResetType)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.TeamId);
            entity.HasIndex(e => new { e.ServiceId, e.TeamId, e.ResetAt });

            entity.HasOne(e => e.Service)
                .WithMany()
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AwdpRecoveryRecord>(entity =>
        {
            entity.HasIndex(e => e.ServiceId);
            entity.HasIndex(e => e.TeamId);
            entity.HasIndex(e => new { e.ServiceId, e.TeamId, e.RecoveryAt });

            entity.HasOne(e => e.Service)
                .WithMany()
                .HasForeignKey(e => e.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
