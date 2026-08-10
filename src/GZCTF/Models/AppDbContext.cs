using System.Text.Json;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using GZCTF.Modules;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Domain;
using ApiOperationEntity = GZCTF.Modules.Audit.Domain.ApiOperation;
using ApiTokenEntity = GZCTF.Modules.Identity.Domain.ApiToken;
using ApiTokenResourceGrantEntity = GZCTF.Modules.Identity.Domain.ApiTokenResourceGrant;
using ApiTokenScopeGrantEntity = GZCTF.Modules.Identity.Domain.ApiTokenScopeGrant;
using TrainingCourseImageTemplateBindingEntity = GZCTF.Modules.Training.Domain.TrainingCourseImageTemplateBinding;
using ImageImportJobEntity = GZCTF.Modules.Content.Domain.ImageImportJob;
using ChallengeMutationJobEntity = GZCTF.Modules.Ctf.Domain.ChallengeMutationJob;
using ExternalApiRequestAuditEntity = GZCTF.Modules.Audit.Domain.ExternalApiRequestAudit;
using OperationalEventEntity = GZCTF.Modules.Audit.Domain.OperationalEvent;
using TeamLabTopologyEntity = GZCTF.Modules.TeamLab.Domain.TeamLabTopology;
using TeamLabTopologyNetworkEntity = GZCTF.Modules.TeamLab.Domain.TeamLabTopologyNetwork;
using TeamLabTopologyAssetEntity = GZCTF.Modules.TeamLab.Domain.TeamLabTopologyAsset;
using TeamLabTopologyInterfaceEntity = GZCTF.Modules.TeamLab.Domain.TeamLabTopologyInterface;
using TeamLabTopologyConnectionEntity = GZCTF.Modules.TeamLab.Domain.TeamLabTopologyConnection;
using TeamLabTopologyReleaseEntity = GZCTF.Modules.TeamLab.Domain.TeamLabTopologyRelease;
using TeamLabNetworkLeaseEntity = GZCTF.Modules.TeamLab.Domain.TeamLabNetworkLease;
using PenetrationObjectiveEntity = GZCTF.Modules.Penetration.Domain.PenetrationObjective;
using PenetrationGameLabBindingEntity = GZCTF.Modules.Penetration.Domain.PenetrationGameLabBinding;
using PenetrationTeamRuntimeBindingEntity = GZCTF.Modules.Penetration.Domain.PenetrationTeamRuntimeBinding;
using TeamLabRuntimeOperationJobEntity = GZCTF.Modules.TeamLab.Domain.TeamLabRuntimeOperationJob;
using TheoryQuestionTagEntity = GZCTF.Modules.Theory.Domain.TheoryQuestionTag;
using TheoryQuestionTagBindingEntity = GZCTF.Modules.Theory.Domain.TheoryQuestionTagBinding;
using ImageDistributionReferenceEntity = GZCTF.Modules.Runtime.Domain.ImageDistributionReference;
using DataGovernanceRunEntity = GZCTF.Modules.Audit.Domain.DataGovernanceRun;
using OperationalLogAggregateEntity = GZCTF.Modules.Audit.Domain.OperationalLogAggregate;
using DeploymentLifecycleAggregateEntity = GZCTF.Modules.Audit.Domain.DeploymentLifecycleAggregate;
using TeamLabTrafficFlowAggregateEntity = GZCTF.Modules.TeamLab.Domain.TeamLabTrafficFlowAggregate;
using ProjectionRevisionEntity = GZCTF.Infrastructure.Cache.ProjectionRevision;
using WorkerNodeMetricSampleEntity = GZCTF.Modules.Runtime.Domain.WorkerNodeMetricSample;
using AgentRuntimeSignal = GZCTF.Modules.Runtime.Domain.AgentRuntimeSignal;
using TeamLabRemoteSessionEntity = GZCTF.Modules.TeamLab.Domain.Runtime.TeamLabRemoteSession;
using TeamLabRemoteAuditFileEntity = GZCTF.Modules.TeamLab.Domain.Runtime.TeamLabRemoteAuditFile;
using ImageTemplateRemoteAccessEntity = GZCTF.Modules.Content.Domain.ImageTemplateRemoteAccess;
using PenetrationTeamLabOperatorGrantEntity = GZCTF.Modules.Penetration.Domain.PenetrationTeamLabOperatorGrant;

namespace GZCTF.Models;

public partial class AppDbContext(DbContextOptions<AppDbContext> options) :
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
    public DbSet<ApiTokenEntity> ApiTokens { get; set; } = null!;
    public DbSet<ApiTokenScopeGrantEntity> ApiTokenScopeGrants { get; set; } = null!;
    public DbSet<ApiTokenResourceGrantEntity> ApiTokenResourceGrants { get; set; } = null!;
    public DbSet<ApiOperationEntity> ApiOperations { get; set; } = null!;
    public DbSet<ExternalApiRequestAuditEntity> ExternalApiRequestAudits { get; set; } = null!;
    public DbSet<OperationalEventEntity> OperationalEvents => Set<OperationalEventEntity>();
    public DbSet<TrainingCourseImageTemplateBindingEntity> TrainingCourseImageTemplateBindings { get; set; } = null!;
    public DbSet<ImageImportJobEntity> ImageImportJobs { get; set; } = null!;
    public DbSet<ChallengeMutationJobEntity> ChallengeMutationJobs { get; set; } = null!;
    public DbSet<ImageTemplate> ImageTemplates { get; set; } = null!;
    public DbSet<ImageTemplateRemoteAccessEntity> ImageTemplateRemoteAccesses => Set<ImageTemplateRemoteAccessEntity>();
    public DbSet<BootstrapProfile> BootstrapProfiles => Set<BootstrapProfile>();
    public DbSet<BootstrapProfileVersion> BootstrapProfileVersions => Set<BootstrapProfileVersion>();
    public DbSet<BootstrapProfileOperationJob> BootstrapProfileOperationJobs => Set<BootstrapProfileOperationJob>();
    public DbSet<BootstrapProfileDistribution> BootstrapProfileDistributions => Set<BootstrapProfileDistribution>();
    public DbSet<ImageTemplateCapabilityCertification> ImageTemplateCapabilityCertifications => Set<ImageTemplateCapabilityCertification>();
    public DbSet<ImageTemplateCertificationJob> ImageTemplateCertificationJobs => Set<ImageTemplateCertificationJob>();
    public DbSet<VmPreparedArtifact> VmPreparedArtifacts => Set<VmPreparedArtifact>();
    public DbSet<ImageDistributionRecord> ImageDistributionRecords { get; set; } = null!;
    public DbSet<ImageDistributionReferenceEntity> ImageDistributionReferences => Set<ImageDistributionReferenceEntity>();
    public DbSet<DockerRegistryMigrationTask> DockerRegistryMigrationTasks { get; set; } = null!;
    public DbSet<DockerRegistryMigrationItem> DockerRegistryMigrationItems { get; set; } = null!;
    public DbSet<VmInstance> VmInstances => Set<VmInstance>();
    public DbSet<WorkerNode> WorkerNodes { get; set; } = null!;
    public DbSet<DeploymentQueueTicket> DeploymentQueueTickets { get; set; } = null!;
    public DbSet<FleetCapacityReservation> FleetCapacityReservations => Set<FleetCapacityReservation>();
    public DbSet<GamePhase> GamePhases => Set<GamePhase>();
    public DbSet<TheoryQuestionBankItem> TheoryQuestionBankItems { get; set; } = null!;
    public DbSet<TheoryQuestionTagEntity> TheoryQuestionTags => Set<TheoryQuestionTagEntity>();
    public DbSet<TheoryQuestionTagBindingEntity> TheoryQuestionTagBindings => Set<TheoryQuestionTagBindingEntity>();
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
    public DbSet<PenetrationSubmission> PenetrationSubmissions { get; set; } = null!;
    public DbSet<PenetrationResetRecord> PenetrationResetRecords { get; set; } = null!;
    public DbSet<PenetrationObjectiveEntity> PenetrationObjectives => Set<PenetrationObjectiveEntity>();
    public DbSet<PenetrationGameLabBindingEntity> PenetrationGameLabBindings => Set<PenetrationGameLabBindingEntity>();
    public DbSet<PenetrationTeamRuntimeBindingEntity> PenetrationTeamRuntimeBindings => Set<PenetrationTeamRuntimeBindingEntity>();
    public DbSet<PenetrationTeamLabOperatorGrantEntity> PenetrationTeamLabOperatorGrants => Set<PenetrationTeamLabOperatorGrantEntity>();
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
    public DbSet<TeamLabRuntime> TeamLabRuntimes => Set<TeamLabRuntime>();
    public DbSet<TeamLabRuntimeShard> TeamLabRuntimeShards => Set<TeamLabRuntimeShard>();
    public DbSet<TeamLabRuntimeNetwork> TeamLabRuntimeNetworks => Set<TeamLabRuntimeNetwork>();
    public DbSet<TeamLabRuntimeAsset> TeamLabRuntimeAssets => Set<TeamLabRuntimeAsset>();
    public DbSet<TeamLabVpnPeerRuntime> TeamLabVpnPeerRuntimes => Set<TeamLabVpnPeerRuntime>();
    public DbSet<TeamLabPublicUdpMapping> TeamLabPublicUdpMappings => Set<TeamLabPublicUdpMapping>();
    public DbSet<TeamLabEvent> TeamLabEvents => Set<TeamLabEvent>();
    public DbSet<TeamLabTrafficFlow> TeamLabTrafficFlows => Set<TeamLabTrafficFlow>();
    public DbSet<TeamLabTrafficCaptureJob> TeamLabTrafficCaptureJobs => Set<TeamLabTrafficCaptureJob>();
    public DbSet<TeamLabTrafficCaptureSegment> TeamLabTrafficCaptureSegments => Set<TeamLabTrafficCaptureSegment>();
    public DbSet<TeamLabAccessGrant> TeamLabAccessGrants => Set<TeamLabAccessGrant>();
    public DbSet<TeamLabRollout> TeamLabRollouts => Set<TeamLabRollout>();
    public DbSet<TeamLabRolloutTarget> TeamLabRolloutTargets => Set<TeamLabRolloutTarget>();
    public DbSet<TeamLabRuntimeSecretEnvelope> TeamLabRuntimeSecretEnvelopes => Set<TeamLabRuntimeSecretEnvelope>();
    public DbSet<TeamLabControlScope> TeamLabControlScopes => Set<TeamLabControlScope>();
    public DbSet<TeamLabTopologyEntity> TeamLabTopologies => Set<TeamLabTopologyEntity>();
    public DbSet<TeamLabTopologyNetworkEntity> TeamLabTopologyNetworks => Set<TeamLabTopologyNetworkEntity>();
    public DbSet<TeamLabTopologyAssetEntity> TeamLabTopologyAssets => Set<TeamLabTopologyAssetEntity>();
    public DbSet<TeamLabTopologyInterfaceEntity> TeamLabTopologyInterfaces => Set<TeamLabTopologyInterfaceEntity>();
    public DbSet<TeamLabTopologyConnectionEntity> TeamLabTopologyConnections => Set<TeamLabTopologyConnectionEntity>();
    public DbSet<TeamLabTopologyReleaseEntity> TeamLabTopologyReleases => Set<TeamLabTopologyReleaseEntity>();
    public DbSet<TeamLabNetworkLeaseEntity> TeamLabNetworkLeases => Set<TeamLabNetworkLeaseEntity>();
    public DbSet<TeamLabRuntimeInfrastructure> TeamLabRuntimeInfrastructures => Set<TeamLabRuntimeInfrastructure>();
    public DbSet<TeamLabRuntimeInfrastructureFragment> TeamLabRuntimeInfrastructureFragments => Set<TeamLabRuntimeInfrastructureFragment>();
    public DbSet<TeamLabFabricLinkLease> TeamLabFabricLinkLeases => Set<TeamLabFabricLinkLease>();
    public DbSet<TeamLabRuntimeDependencyState> TeamLabRuntimeDependencyStates => Set<TeamLabRuntimeDependencyState>();
    public DbSet<TeamLabObservationPoint> TeamLabObservationPoints => Set<TeamLabObservationPoint>();
    public DbSet<TeamLabObservationCursor> TeamLabObservationCursors => Set<TeamLabObservationCursor>();
    public DbSet<TeamLabTrafficObservation> TeamLabTrafficObservations => Set<TeamLabTrafficObservation>();
    public DbSet<TeamLabTrafficPath> TeamLabTrafficPaths => Set<TeamLabTrafficPath>();
    public DbSet<TeamLabTrafficPathHop> TeamLabTrafficPathHops => Set<TeamLabTrafficPathHop>();
    public DbSet<TeamLabTrafficCorrelationCursor> TeamLabTrafficCorrelationCursors => Set<TeamLabTrafficCorrelationCursor>();
    public DbSet<TeamLabRuntimeOperationJobEntity> TeamLabRuntimeOperationJobs => Set<TeamLabRuntimeOperationJobEntity>();
    public DbSet<TeamLabRemoteSessionEntity> TeamLabRemoteSessions => Set<TeamLabRemoteSessionEntity>();
    public DbSet<TeamLabRemoteAuditFileEntity> TeamLabRemoteAuditFiles => Set<TeamLabRemoteAuditFileEntity>();
    public DbSet<TeamLabWebhookSubscription> TeamLabWebhookSubscriptions => Set<TeamLabWebhookSubscription>();
    public DbSet<TeamLabWebhookDeliveryFailure> TeamLabWebhookDeliveryFailures => Set<TeamLabWebhookDeliveryFailure>();
    public DbSet<DataGovernanceRunEntity> DataGovernanceRuns => Set<DataGovernanceRunEntity>();
    public DbSet<OperationalLogAggregateEntity> OperationalLogAggregates => Set<OperationalLogAggregateEntity>();
    public DbSet<DeploymentLifecycleAggregateEntity> DeploymentLifecycleAggregates => Set<DeploymentLifecycleAggregateEntity>();
    public DbSet<TeamLabTrafficFlowAggregateEntity> TeamLabTrafficFlowAggregates => Set<TeamLabTrafficFlowAggregateEntity>();
    public DbSet<ProjectionRevisionEntity> ProjectionRevisions => Set<ProjectionRevisionEntity>();
    public DbSet<WorkerNodeMetricSampleEntity> WorkerNodeMetricSamples => Set<WorkerNodeMetricSampleEntity>();
    public DbSet<AgentRuntimeSignal> AgentRuntimeSignals => Set<AgentRuntimeSignal>();

    private static ValueConverter<T?, string> GetJsonConverter<T>() where T : class, new() =>
        new(
            v => JsonSerializer.Serialize(v ?? new(), JsonOptions),
            v => JsonSerializer.Deserialize<T>(v, JsonOptions)
        );

    private static ValueConverter<T, string> GetRequiredJsonConverter<T>() where T : class, new() =>
        new(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v => JsonSerializer.Deserialize<T>(v, JsonOptions) ?? new()
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
        builder.HasPostgresExtension("pg_trgm");
        builder.ApplyConfigurationsFromAssembly(typeof(ModuleAssemblyMarker).Assembly,
            type => type.Namespace?.StartsWith("GZCTF.Modules", StringComparison.Ordinal) == true);
        builder.ApplyConfiguration(new Infrastructure.Persistence.ProjectionRevisionEntityConfiguration());

        // var setConverter = GetJsonConverter<HashSet<string>>();
        // var setComparer = GetEnumerableComparer<HashSet<string>, string>();
        var listConverter = GetJsonConverter<List<string>>();
        var requiredListConverter = GetRequiredJsonConverter<List<string>>();
        var listComparer = GetEnumerableComparer<List<string>, string>();
        var intListConverter = GetRequiredJsonConverter<List<int>>();
        var intListComparer = GetEnumerableComparer<List<int>, int>();
        var trainingChapterCompletionPolicyConverter = GetRequiredJsonConverter<TrainingChapterCompletionPolicy>();
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
                .OnDelete(DeleteBehavior.Cascade);
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

        builder.Entity<DockerRegistryMigrationTask>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasMany(e => e.Items)
                .WithOne(e => e.Task)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DockerRegistryMigrationItem>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasOne(e => e.ImageTemplate)
                .WithMany()
                .HasForeignKey(e => e.ImageTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PenetrationSubmission>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.HasIndex(e => new { e.GameId, e.TeamId, e.ObjectiveId });

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

            entity.HasOne(e => e.Objective)
                .WithMany()
                .HasForeignKey(e => e.ObjectiveId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Navigation(e => e.Team).AutoInclude();
            entity.Navigation(e => e.User).AutoInclude();
            entity.Navigation(e => e.Objective).AutoInclude();
        });

        builder.Entity<PenetrationResetRecord>(entity =>
        {
            entity.HasOne(e => e.Runtime)
                .WithMany()
                .HasForeignKey(e => e.RuntimeId)
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

        builder.Entity<TrainingCourse>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.EnrollmentPolicy)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.Tags)
                .HasConversion(requiredListConverter)
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

            entity.Property(e => e.CompletionPolicy)
                .HasConversion(trainingChapterCompletionPolicyConverter);

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

        builder.Entity<TrainingCheckIn>(entity =>
        {
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
                .HasConversion(requiredListConverter)
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
            entity.HasQueryFilter(question => !question.IsArchived);

            entity.Property(e => e.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.Options)
                .HasConversion(requiredListConverter)
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

            entity.Property(e => e.QuestionType)
                .HasConversion<string>()
                .HasMaxLength(32);

            entity.Property(e => e.QuestionOptions)
                .HasConversion(GetRequiredJsonConverter<List<string>>())
                .Metadata
                .SetValueComparer(listComparer);

            entity.Property(e => e.CorrectAnswerIndexes)
                .HasConversion(GetRequiredJsonConverter<List<int>>())
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

        builder.Entity<TeamLabRuntime>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<byte>();

        });

        builder.Entity<TeamLabRuntimeShard>(entity =>
        {
            entity.Property(e => e.Status)
                .HasConversion<byte>();

            entity.HasIndex(e => new { e.RuntimeId, e.Generation, e.WorkerNodeId })
                .IsUnique();

            entity.HasOne(e => e.Runtime)
                .WithMany(e => e.Shards)
                .HasForeignKey(e => e.RuntimeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.WorkerNode)
                .WithMany()
                .HasForeignKey(e => e.WorkerNodeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TeamLabRuntimeNetwork>(entity =>
        {
            entity.HasIndex(e => new { e.RuntimeId, e.Generation, e.TopologyKey })
                .IsUnique();

            entity.HasOne(e => e.Runtime)
                .WithMany(e => e.Networks)
                .HasForeignKey(e => e.RuntimeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Shard)
                .WithMany(e => e.Networks)
                .HasForeignKey(e => e.ShardId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.WorkerNode)
                .WithMany()
                .HasForeignKey(e => e.WorkerNodeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TeamLabRuntimeAsset>(entity =>
        {
            entity.Property(e => e.Kind)
                .HasConversion<byte>();

            entity.Property(e => e.Status)
                .HasConversion<byte>();

            entity.Property(e => e.ExecutionStage)
                .HasConversion<byte>();

            entity.Property(e => e.EndpointObservation)
                .HasConversion<byte>();

            entity.HasIndex(e => new { e.RuntimeId, e.Generation, e.Kind, e.TopologyKey });
            entity.HasIndex(e => e.AgentOperationId)
                .IsUnique()
                .HasFilter("\"AgentOperationId\" IS NOT NULL");

            entity.HasOne(e => e.Runtime)
                .WithMany(e => e.Assets)
                .HasForeignKey(e => e.RuntimeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Shard)
                .WithMany(e => e.Assets)
                .HasForeignKey(e => e.ShardId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.WorkerNode)
                .WithMany()
                .HasForeignKey(e => e.WorkerNodeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TeamLabVpnPeerRuntime>(entity =>
        {
            entity.HasIndex(e => new { e.RuntimeId, e.Revoked });

            entity.HasOne(e => e.Runtime)
                .WithMany(e => e.VpnPeers)
                .HasForeignKey(e => e.RuntimeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TeamLabPublicUdpMapping>(entity =>
        {
            entity.HasIndex(e => e.RuntimeId).IsUnique();
            entity.HasIndex(e => e.PublicUdpPort).IsUnique();

            entity.HasOne(e => e.Runtime)
                .WithOne(e => e.PublicUdpMapping)
                .HasForeignKey<TeamLabPublicUdpMapping>(e => e.RuntimeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TeamLabEvent>(entity =>
        {
            entity.Property(e => e.Level)
                .HasConversion<byte>();

            entity.HasIndex(e => new { e.RuntimeId, e.CreatedAt });

            entity.HasOne(e => e.Runtime)
                .WithMany(e => e.Events)
                .HasForeignKey(e => e.RuntimeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    }
}
