using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Localization;

namespace GZCTF.Utils;

/// <summary>
/// User role enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Role>))]
public enum Role : byte
{
    /// <summary>
    /// Banned user role
    /// </summary>
    Banned = 0,

    /// <summary>
    /// Student role
    /// </summary>
    Student = 1,

    /// <summary>
    /// Teacher role
    /// </summary>
    Teacher = 2,

    /// <summary>
    /// Admin role, can view system logs
    /// </summary>
    Admin = 3,

    /// <summary>
    /// Super administrator role
    /// </summary>
    SuperAdmin = 4,

    /// <summary>
    /// Legacy regular user role
    /// </summary>
    User = Student,

    /// <summary>
    /// Legacy monitor role
    /// </summary>
    Monitor = Teacher
}

/// <summary>
/// Login response status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RegisterStatus>))]
public enum RegisterStatus : byte
{
    /// <summary>
    /// Registered successfully and logged in
    /// </summary>
    LoggedIn = 0,

    /// <summary>
    /// Waiting for admin confirmation
    /// </summary>
    AdminConfirmationRequired = 1,

    /// <summary>
    /// Waiting for email confirmation
    /// </summary>
    EmailConfirmationRequired = 2
}

/// <summary>
/// Task execution status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskStatus>))]
public enum TaskStatus : sbyte
{
    /// <summary>
    /// System is unhealthy
    /// </summary>
    Unhealthy = -3,

    /// <summary>
    /// System is in a degraded state
    /// </summary>
    Degraded = -2,

    /// <summary>
    /// Task is in progress
    /// </summary>
    Pending = -1,

    /// <summary>
    /// Task completed successfully
    /// </summary>
    Success = 0,

    /// <summary>
    /// Task execution failed
    /// </summary>
    Failed = 1,

    /// <summary>
    /// Task encountered a duplicate error
    /// </summary>
    Duplicate = 2,

    /// <summary>
    /// Task processing was denied
    /// </summary>
    Denied = 3,

    /// <summary>
    /// Task request not found
    /// </summary>
    NotFound = 4,

    /// <summary>
    /// Task thread is about to exit
    /// </summary>
    Exit = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<FileType>))]
public enum FileType : byte
{
    /// <summary>
    /// No attachment
    /// Normally, Attachment will not be this value, if there is no attachment, Attachment will be null
    /// </summary>
    None = 0,

    /// <summary>
    /// Local file
    /// </summary>
    Local = 1,

    /// <summary>
    /// Remote file
    /// </summary>
    Remote = 2
}

/// <summary>
/// Container status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContainerStatus>))]
public enum ContainerStatus : byte
{
    /// <summary>
    /// Starting
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Running
    /// </summary>
    Running = 1,

    /// <summary>
    /// Destroyed
    /// </summary>
    Destroyed = 2
}

/// <summary>
/// Game announcement type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NoticeType>))]
public enum NoticeType : byte
{
    /// <summary>
    /// Regular announcement
    /// </summary>
    Normal = 0,

    /// <summary>
    /// First blood announcement
    /// </summary>
    FirstBlood = 1,

    /// <summary>
    /// Second blood announcement
    /// </summary>
    SecondBlood = 2,

    /// <summary>
    /// Third blood announcement
    /// </summary>
    ThirdBlood = 3,

    /// <summary>
    /// New hint released
    /// </summary>
    NewHint = 4,

    /// <summary>
    /// New challenge released
    /// </summary>
    NewChallenge = 5
}

/// <summary>
/// Game type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GameType>))]
public enum GameType : byte
{
    /// <summary>
    /// Jeopardy mode
    /// </summary>
    [Description("Jeopardy")]
    Jeopardy = 0,

    /// <summary>
    /// AWDP mode (Attack with Defense Plus)
    /// </summary>
    [Description("AWDP")]
    AWDP = 1,

    /// <summary>
    /// Theory mode
    /// </summary>
    [Description("Theory")]
    Theory = 2,

    /// <summary>
    /// Mixed mode
    /// </summary>
    [Description("Mixed")]
    Mixed = 3,

    /// <summary>
    /// Penetration mode
    /// </summary>
    [Description("Penetration")]
    Penetration = 4
}

/// <summary>
/// Theory exam question type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TheoryQuestionType>))]
public enum TheoryQuestionType : byte
{
    /// <summary>
    /// Single choice question
    /// </summary>
    SingleChoice = 0,

    /// <summary>
    /// Multiple choice question
    /// </summary>
    MultipleChoice = 1,

    /// <summary>
    /// True or false question
    /// </summary>
    TrueFalse = 2
}

/// <summary>
/// Theory answer sheet status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TheoryAnswerSheetStatus>))]
public enum TheoryAnswerSheetStatus : byte
{
    /// <summary>
    /// Draft saved but not submitted
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Final answer sheet submitted
    /// </summary>
    Submitted = 1
}

/// <summary>
/// Training content family
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrainingType>))]
public enum TrainingType : byte
{
    Ctf = 0,
    Theory = 1
}

/// <summary>
/// Training article format
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrainingArticleContentType>))]
public enum TrainingArticleContentType : byte
{
    Markdown = 0,
    Html = 1
}

/// <summary>
/// Training visibility scope
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrainingVisibilityType>))]
public enum TrainingVisibilityType : byte
{
    GroupOnly = 0,
    AllStudents = 1
}

/// <summary>
/// Student training module progress status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrainingModuleProgressStatus>))]
public enum TrainingModuleProgressStatus : byte
{
    NotStarted = 0,
    Reading = 1,
    Practicing = 2,
    Completed = 3
}

/// <summary>
/// Theory training paper generation mode
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TheoryTrainingMode>))]
public enum TheoryTrainingMode : byte
{
    Random = 0,
    Manual = 1
}

/// <summary>
/// Training theory session status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TheoryTrainingSessionStatus>))]
public enum TheoryTrainingSessionStatus : byte
{
    Draft = 0,
    Submitted = 1
}

/// <summary>
/// Student group manager role
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StudentGroupManagerRole>))]
public enum StudentGroupManagerRole : byte
{
    Owner = 0,
    Assistant = 1
}

/// <summary>
/// Game event type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EventType>))]
public enum EventType : byte
{
    /// <summary>
    /// Regular information
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Container start information
    /// </summary>
    ContainerStart = 1,

    /// <summary>
    /// Container destroy information
    /// </summary>
    ContainerDestroy = 2,

    /// <summary>
    /// Flag submission information
    /// </summary>
    FlagSubmit = 3,

    /// <summary>
    /// Cheating information
    /// </summary>
    CheatDetected = 4,

    /// <summary>
    /// AWDP flag submission
    /// </summary>
    AwdpFlagSubmit = 20,

    /// <summary>
    /// AWDP service up (checker OK)
    /// </summary>
    AwdpServiceUp = 21,

    /// <summary>
    /// AWDP service down
    /// </summary>
    AwdpServiceDown = 22,

    /// <summary>
    /// AWDP service mumble
    /// </summary>
    AwdpServiceMumble = 23,

    /// <summary>
    /// AWDP round start
    /// </summary>
    AwdpRoundStart = 24,

    /// <summary>
    /// AWDP attack success
    /// </summary>
    AwdpAttackSuccess = 25,

    /// <summary>
    /// AWDP patch submission result
    /// </summary>
    AwdpPatchResult = 26
}

/// <summary>
/// Submission type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubmissionType>))]
public enum SubmissionType : byte
{
    /// <summary>
    /// Not solved
    /// </summary>
    Unaccepted = 0,

    /// <summary>
    /// First blood
    /// </summary>
    FirstBlood = 1,

    /// <summary>
    /// Second blood
    /// </summary>
    SecondBlood = 2,

    /// <summary>
    /// Third blood
    /// </summary>
    ThirdBlood = 3,

    /// <summary>
    /// Solved
    /// </summary>
    Normal = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<ParticipationStatus>))]
public enum ParticipationStatus : byte
{
    /// <summary>
    /// Registered
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Accepted
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// Rejected
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// Suspended
    /// </summary>
    Suspended = 3,

    /// <summary>
    /// Not submitted
    /// </summary>
    Unsubmitted = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<ChallengeType>))]
public enum ChallengeType : byte
{
    /// <summary>
    /// Static challenge
    /// All teams use the same attachment and flag
    /// </summary>
    StaticAttachment = 0b00,

    /// <summary>
    /// Static container challenge
    /// All teams use the same docker and flag
    /// </summary>
    StaticContainer = 0b01,

    /// <summary>
    /// Dynamic attachment challenge
    /// Randomly distribute attachments, implement flag specificity with attachments
    /// </summary>
    DynamicAttachment = 0b10,

    /// <summary>
    /// Dynamic container challenge
    /// Randomly distribute containers, dynamic flag passed in via environment variables
    /// </summary>
    DynamicContainer = 0b11,

    /// <summary>
    /// Multi-stage attack chain scenario
    /// A sequence of interconnected challenges forming a complete attack narrative
    /// </summary>
    Scenario = 0b100,

    /// <summary>
    /// Incident response challenge
    /// Time-boxed incident response exercise with forensic analysis and remediation tasks
    /// </summary>
    IRChallenge = 0b1000,

}

public static class ChallengeTypeExtensions
{
    extension(ChallengeType type)
    {
        /// <summary>
        /// Is it a static challenge
        /// </summary>
        public bool IsStatic() => ((byte)type & 0b10) == 0;

        /// <summary>
        /// Is it a dynamic challenge
        /// </summary>
        public bool IsDynamic() => ((byte)type & 0b10) != 0;

        /// <summary>
        /// Is it an attachment challenge
        /// </summary>
        public bool IsAttachment() => ((byte)type & 0b01) == 0;

        /// <summary>
        /// Is it a container challenge
        /// </summary>
        public bool IsContainer() => ((byte)type & 0b01) != 0;

        /// <summary>
        /// Is it a multi-stage attack chain scenario
        /// </summary>
        public bool IsScenario() => ((byte)type & 0b100) != 0;

        /// <summary>
        /// Is it an incident response challenge
        /// </summary>
        public bool IsIRChallenge() => ((byte)type & 0b1000) != 0;
    }
}

/// <summary>
/// Environment type for challenge deployment
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EnvironmentType>))]
public enum EnvironmentType : byte
{
    /// <summary>
    /// No environment required
    /// </summary>
    None = 0,

    /// <summary>
    /// Docker container environment
    /// </summary>
    Docker = 1,

    /// <summary>
    /// Windows virtual machine environment
    /// </summary>
    WindowsVM = 2,
}

/// <summary>
/// Verification type for IR challenge checkpoints
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VerificationType>))]
public enum VerificationType : byte
{
    /// <summary>
    /// Auto-verify by running a script
    /// </summary>
    AutoScript = 0,

    /// <summary>
    /// Auto-verify by executing a command and checking output
    /// </summary>
    AutoCommand = 1,

    /// <summary>
    /// Verify by comparing player-submitted answer
    /// </summary>
    ManualAnswer = 2,

    /// <summary>
    /// Require manual admin review
    /// </summary>
    ManualReview = 3
}

/// <summary>
/// Environment status for IR challenge instances
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EnvironmentStatus>))]
public enum EnvironmentStatus : byte
{
    /// <summary>
    /// Environment is being created
    /// </summary>
    Creating = 0,

    /// <summary>
    /// Environment is ready for use
    /// </summary>
    Ready = 1,

    /// <summary>
    /// Environment creation failed
    /// </summary>
    Error = 2,

    /// <summary>
    /// Environment has been destroyed
    /// </summary>
    Destroyed = 3
}

/// <summary>
/// Flag score mode
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FlagScoreMode>))]
public enum FlagScoreMode : byte
{
    /// <summary>
    /// Inherit game scoring formula with decay
    /// </summary>
    InheritDecay = 0,

    /// <summary>
    /// Fixed score regardless of solve count
    /// </summary>
    FixedScore = 1,
}

/// <summary>
/// Answer type for challenge submission
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnswerType>))]
public enum AnswerType : byte
{
    /// <summary>
    /// Standard flag submission
    /// </summary>
    Flag = 0,

    /// <summary>
    /// File upload submission
    /// </summary>
    File = 1,

    /// <summary>
    /// Custom verification logic
    /// </summary>
    Custom = 2,
}

/// <summary>
/// Challenge category
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChallengeCategory>))]
public enum ChallengeCategory : byte
{
    Misc = 0,
    Crypto = 1,
    Pwn = 2,
    Web = 3,
    Reverse = 4,
    Blockchain = 5,
    Forensics = 6,
    Hardware = 7,
    Mobile = 8,
    PPC = 9,

    // ReSharper disable once InconsistentNaming
    AI = 10,
    Pentest = 11,

    // ReSharper disable once InconsistentNaming
    OSINT = 12,

    /// <summary>
    /// Multi-stage attack chain scenario
    /// </summary>
    Scenario = 13,

    /// <summary>
    /// Incident response challenge
    /// </summary>
    // ReSharper disable once InconsistentNaming
    IR = 14
}

/// <summary>
/// Challenge difficulty
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<Difficulty>))]
public enum Difficulty : byte
{
    Baby = 0,
    Trivial = 1,
    Easy = 2,
    Normal = 3,
    Medium = 4,
    Hard = 5,
    Expert = 6,
    Insane = 7
}

/// <summary>
/// Container network mode
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NetworkMode>))]
public enum NetworkMode : byte
{
    /// <summary>
    /// Open network
    /// </summary>
    /// <remarks>
    /// Allows the container to access external networks, including the internet.
    /// Suitable for challenges that require external connectivity.
    /// </remarks>
    Open = 0,

    /// <summary>
    /// Isolated network
    /// </summary>
    /// <remarks>
    /// Restricts the container from accessing any external networks.
    /// The container can only communicate within container internal network.
    /// </remarks>
    Isolated = 32,

    /// <summary>
    /// Custom network
    /// </summary>
    /// <remarks>
    /// <para>
    ///  For kubernetes provider<br/>
    ///  this mode will add a label to the pod: <c>gzctf.gzti.me/NetworkMode: custom</c>
    /// </para>
    /// <para>
    ///  For Docker provider<br/>
    ///  this mode will create the container in a user-defined docker network specified in the config.
    /// </para>
    /// </remarks>
    Custom = 255,
}

/// <summary>
/// Game participant permission
/// </summary>
[Flags]
[JsonConverter(typeof(JsonNumberEnumConverter<GamePermission>))]
public enum GamePermission
{
    /// <summary>
    /// Join the game
    /// </summary>
    /// <remarks>
    /// Division-level permission only. Controls whether users can join the game through this division.
    /// Without this permission, the division cannot accept new participants.
    /// </remarks>
    JoinGame = 1 << 0,

    /// <summary>
    /// Can be ranked on the overall scoreboard
    /// </summary>
    /// <remarks>
    /// Division-level permission only. Determines if teams in this division appear on the overall scoreboard rankings.
    /// Teams without this permission will only have division-specific rankings.
    /// Use case: Unofficial participants or guest teams that should not compete for overall prizes.
    /// </remarks>
    RankOverall = 1 << 1,

    /// <summary>
    /// Require review before acceptance
    /// </summary>
    /// <remarks>
    /// Division-level permission only. When enabled, participations require manual admin approval (Pending status).
    /// When disabled, participations are automatically accepted without review (Accepted status).
    /// Overrides game-level AcceptWithoutReview setting for this specific division.
    /// Use case: Require review for external registrations while auto-accepting internal teams.
    /// Note: Permission name is positive (require review) so that All permission defaults to the secure behavior.
    /// </remarks>
    RequireReview = 1 << 2,

    /// <summary>
    /// Can view challenge
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Controls access to challenge details, descriptions, and attachments.
    /// Without this permission, challenges will be hidden from the team.
    /// </remarks>
    ViewChallenge = 1 << 8,

    /// <summary>
    /// Can submit flags
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Allows teams to submit flag answers for challenges.
    /// Can be combined with ViewChallenge but without GetScore for practice/demo scenarios.
    /// </remarks>
    SubmitFlags = 1 << 9,

    /// <summary>
    /// Can be awarded points
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Determines if successful flag submissions award points to the team.
    /// Teams without this permission can submit flags but won't receive any score.
    /// Use case: Observer teams or late joiners who participate without competing.
    /// </remarks>
    GetScore = 1 << 10,

    /// <summary>
    /// Can earn blood bonuses
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Allows teams to receive first/second/third blood bonus points.
    /// Requires GetScore permission to be effective. Blood bonuses are typically 30%, 20%, and 10% extra points.
    /// Use case: Restrict blood bonuses to specific divisions while allowing others to score normally.
    /// </remarks>
    GetBlood = 1 << 11,

    /// <summary>
    /// Affects dynamic scoring calculation
    /// </summary>
    /// <remarks>
    /// Challenge-specific permission. Controls whether this team's submissions count toward challenge accept count,
    /// which influences dynamic score calculation for all teams.
    /// Independent of GetScore - teams can score without affecting others' challenge values.
    /// Use case: Allow external participants to score without affecting internal competition's dynamic scoring.
    /// </remarks>
    AffectDynamicScore = 1 << 12,

    /// <summary>
    /// All permissions, including future permissions
    /// </summary>
    /// <remarks>
    /// Special value representing all current and future permissions.
    /// Use int.MaxValue to ensure compatibility with newly added permission flags.
    /// </remarks>
    All = int.MaxValue
}

/// <summary>
/// Judgement result
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnswerResult>))]
public enum AnswerResult : sbyte
{
    /// <summary>
    /// Flag submitted successfully
    /// </summary>
    FlagSubmitted = 0,

    /// <summary>
    /// Answer is correct
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// Answer is wrong
    /// </summary>
    WrongAnswer = 2,

    /// <summary>
    /// Cheating detected
    /// </summary>
    CheatDetected = 3,

    /// <summary>
    /// Submitted challenge instance not found
    /// </summary>
    NotFound = -1
}

public static class AnswerResultExtensions
{
    extension(AnswerResult result)
    {
        public string ToShortString(IStringLocalizer<Program> localizer) =>
            result switch
            {
                AnswerResult.FlagSubmitted => localizer[nameof(Resources.Program.Submission_FlagSubmitted)],
                AnswerResult.Accepted => localizer[nameof(Resources.Program.Submission_Accepted)],
                AnswerResult.WrongAnswer => localizer[nameof(Resources.Program.Submission_WrongAnswer)],
                AnswerResult.CheatDetected => localizer[nameof(Resources.Program.Submission_CheatDetected)],
                AnswerResult.NotFound => localizer[nameof(Resources.Program.Submission_UnknownInstance)],
                _ => "??"
            };
    }
}

/// <summary>
/// System error codes, starting from 10000
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// Game not started
    /// </summary>
    public const int GameNotStarted = 10001;

    /// <summary>
    /// Game ended
    /// </summary>
    public const int GameEnded = 10002;
}

/// <summary>
/// Checker execution status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CheckerStatus>))]
public enum CheckerStatus : byte
{
    /// <summary>
    /// Service is functioning normally
    /// </summary>
    [Description("OK")]
    OK = 0,

    /// <summary>
    /// Service is degraded but partially functional
    /// </summary>
    [Description("Mumble")]
    Mumble = 1,

    /// <summary>
    /// Service is completely down
    /// </summary>
    [Description("Down")]
    Down = 2,

    /// <summary>
    /// Service data is corrupted
    /// </summary>
    [Description("Corrupt")]
    Corrupt = 3,

    /// <summary>
    /// Checker execution was skipped
    /// </summary>
    [Description("Skipped")]
    Skipped = 4
}

/// <summary>
/// AWDP round phase status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AwdpRoundStatus>))]
public enum AwdpRoundStatus : byte
{
    /// <summary>
    /// Attack phase: players can submit flags
    /// </summary>
    [Description("AttackPhase")]
    AttackPhase = 0,

    /// <summary>
    /// Patch phase: players can upload patches
    /// </summary>
    [Description("PatchPhase")]
    PatchPhase = 1,

    /// <summary>
    /// Round finished, scores calculated
    /// </summary>
    [Description("Finished")]
    Finished = 2
}

/// <summary>
/// AWDP patch verification result
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AwdpPatchStatus>))]
public enum AwdpPatchStatus : byte
{
    /// <summary>
    /// Patch submitted, awaiting verification
    /// </summary>
    [Description("Pending")]
    Pending = 0,

    /// <summary>
    /// Checker failed after patch (service abnormal)
    /// </summary>
    [Description("CheckerFailed")]
    CheckerFailed = 1,

    /// <summary>
    /// Exp succeeded after patch (vulnerability not fixed)
    /// </summary>
    [Description("ExpSucceeded")]
    ExpSucceeded = 2,

    /// <summary>
    /// Exp failed after patch (vulnerability fixed, patch success)
    /// </summary>
    [Description("ExpFailed")]
    ExpFailed = 3,

    /// <summary>
    /// Verification timed out
    /// </summary>
    [Description("Timeout")]
    Timeout = 4,

    /// <summary>
    /// Patch application is not supported by the current container backend
    /// </summary>
    [Description("Unsupported")]
    Unsupported = 5
}

/// <summary>
/// AWDP challenge status from player perspective
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AwdpChallengeStatus>))]
public enum AwdpChallengeStatus : byte
{
    /// <summary>
    /// Flag not yet submitted for this challenge
    /// </summary>
    [Description("Unattacked")]
    Unattacked = 0,

    /// <summary>
    /// Flag successfully submitted
    /// </summary>
    [Description("Attacked")]
    Attacked = 1,

    /// <summary>
    /// No patch submitted yet
    /// </summary>
    [Description("Undefended")]
    Undefended = 2,

    /// <summary>
    /// Patch verified successfully (checker OK + exp failed)
    /// </summary>
    [Description("Defended")]
    Defended = 3,

    /// <summary>
    /// Patch caused service abnormal (checker failed)
    /// </summary>
    [Description("DefenseAbnormal")]
    DefenseAbnormal = 4,

    /// <summary>
    /// Patch did not fix vulnerability (exp succeeded)
    /// </summary>
    [Description("DefenseFailed")]
    DefenseFailed = 5
}

/// <summary>
/// AWDP reset type
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AwdpResetType>))]
public enum AwdpResetType : byte
{
    /// <summary>
    /// Player self-service reset
    /// </summary>
    [Description("Player")]
    Player = 0,

    /// <summary>
    /// Admin manual reset
    /// </summary>
    [Description("Admin")]
    Admin = 1
}
