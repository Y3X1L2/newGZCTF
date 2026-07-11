/* eslint-disable */
/* tslint:disable */
// @ts-nocheck
/*
 * ---------------------------------------------------------------
 * ## THIS FILE WAS GENERATED VIA SWAGGER-TYPESCRIPT-API        ##
 * ##                                                           ##
 * ## AUTHOR: acacode                                           ##
 * ## SOURCE: https://github.com/acacode/swagger-typescript-api ##
 * ---------------------------------------------------------------
 */

/** Training course resource type */
export enum TrainingCourseResourceType {
  File = "File",
  Link = "Link",
  Video = "Video",
}

/** Training course video provider */
export enum TrainingCourseVideoProvider {
  None = "None",
  LocalFile = "LocalFile",
  ExternalUrl = "ExternalUrl",
}

/** Training article format */
export enum TrainingArticleContentType {
  Markdown = "Markdown",
  Html = "Html",
}

/** Training course teacher role */
export enum TrainingCourseTeacherRole {
  Owner = "Owner",
  Teacher = "Teacher",
}

/** Training course learning progress status */
export enum TrainingCourseProgressStatus {
  NotStarted = "NotStarted",
  Learning = "Learning",
  Completed = "Completed",
}

/** Training course enrollment status */
export enum TrainingCourseEnrollmentStatus {
  Pending = "Pending",
  Approved = "Approved",
  Rejected = "Rejected",
  Cancelled = "Cancelled",
}

/** Training course enrollment policy */
export enum TrainingCourseEnrollmentPolicy {
  TeacherApproval = "TeacherApproval",
  AutoApprove = "AutoApprove",
}

/** Training course lifecycle status */
export enum TrainingCourseStatus {
  Draft = "Draft",
  Published = "Published",
  Archived = "Archived",
}

/** Theory answer sheet status */
export enum TheoryAnswerSheetStatus {
  Draft = "Draft",
  Submitted = "Submitted",
}

/** Theory exam question type */
export enum TheoryQuestionType {
  SingleChoice = "SingleChoice",
  MultipleChoice = "MultipleChoice",
  TrueFalse = "TrueFalse",
}

export enum TeamJoinRequestStatus {
  Pending = "Pending",
  Accepted = "Accepted",
  Rejected = "Rejected",
}

/** Student group manager role */
export enum StudentGroupManagerRole {
  Owner = "Owner",
  Assistant = "Assistant",
}

export enum TeamLabTrafficCaptureStatus {
  Pending = 0,
  Running = 1,
  Stopping = 2,
  Completed = 3,
  Failed = 4,
  Expired = 5,
}

export enum TeamLabResourceKind {
  Docker = 0,
  Vm = 1,
  RouterNamespace = 2,
  DhcpDnsService = 3,
  WireGuard = 4,
  PublicUdpMapping = 5,
}

export enum TeamLabRuntimeStatus {
  Pending = 0,
  Planning = 1,
  Scheduled = 2,
  Deploying = 3,
  Probing = 4,
  Running = 5,
  Failed = 6,
  CleanupPending = 7,
  Stopped = 8,
  Destroying = 9,
  Destroyed = 10,
}

export enum PenetrationDeploymentEventLevel {
  Info = "Info",
  Success = "Success",
  Warning = "Warning",
  Error = "Error",
}

export enum PenetrationRuntimeStatus {
  Pending = "Pending",
  Running = "Running",
  Stopped = "Stopped",
  Failed = "Failed",
  CreatingNetworks = "CreatingNetworks",
  CreatingContainers = "CreatingContainers",
  CleanupPending = "CleanupPending",
  Orphaned = "Orphaned",
  ManualCleanupRequired = "ManualCleanupRequired",
}

export enum PenetrationRouteStatus {
  HintOnly = "HintOnly",
  RoutePlanned = "RoutePlanned",
  RouteApplied = "RouteApplied",
  RouteFailed = "RouteFailed",
  Unsupported = "Unsupported",
}

export enum PenetrationEnforcementMode {
  HintOnly = "HintOnly",
  RuntimeRoute = "RuntimeRoute",
  Both = "Both",
}

export enum PenetrationPolicyAction {
  Allow = "Allow",
  Deny = "Deny",
}

export enum PenetrationProtocol {
  Tcp = "Tcp",
  Udp = "Udp",
  Icmp = "Icmp",
  Any = "Any",
}

export enum PenetrationPolicyScope {
  Node = "Node",
  Network = "Network",
}

export enum PenetrationNodeType {
  Entry = "Entry",
  Web = "Web",
  Database = "Database",
  JumpHost = "JumpHost",
  Internal = "Internal",
  DomainControllerReserved = "DomainControllerReserved",
  Custom = "Custom",
  Bastion = "Bastion",
  FirewallRouter = "FirewallRouter",
  Service = "Service",
}

export enum PenetrationDefaultPolicy {
  DenyAll = "DenyAll",
  AllowInternal = "AllowInternal",
}

export enum PenetrationZoneType {
  Public = "Public",
  Dmz = "Dmz",
  Business = "Business",
  Data = "Data",
  Operations = "Operations",
  Management = "Management",
  Custom = "Custom",
}

export enum PenetrationDeploymentStatus {
  Draft = "Draft",
  Published = "Published",
  Deploying = "Deploying",
  Running = "Running",
  Partial = "Partial",
  Stopped = "Stopped",
  Failed = "Failed",
}

export enum CaptchaProvider {
  None = "None",
  HashPow = "HashPow",
  CloudflareTurnstile = "CloudflareTurnstile",
}

export enum ContainerPortMappingType {
  Default = "Default",
  PlatformProxy = "PlatformProxy",
}

export enum DeploymentQueueTicketStatus {
  Pending = 0,
  Assigned = 1,
  Creating = 2,
  Completed = 3,
  Failed = 4,
  Cancelled = 5,
}

export enum DeploymentQueueKind {
  GameContainer = 1,
  ExerciseContainer = 2,
  Vm = 3,
  TeamLabRuntime = 4,
}

/** Challenge difficulty */
export enum Difficulty {
  Baby = "Baby",
  Trivial = "Trivial",
  Easy = "Easy",
  Normal = "Normal",
  Medium = "Medium",
  Hard = "Hard",
  Expert = "Expert",
  Insane = "Insane",
}

export enum ImageStatus {
  Ready = 0,
  Importing = 1,
  Error = 2,
}

export enum ImageType {
  Docker = 0,
  Qcow2 = 1,
  Ova = 2,
  Vmdk = 3,
}

export enum OSType {
  Linux = 0,
  Windows = 1,
}

export enum TeamLabFabricStatus {
  Unknown = 0,
  Disabled = 1,
  Probing = 2,
  Healthy = 3,
  Error = 4,
}

export enum TeamLabTunnelStatus {
  Unknown = 0,
  Disabled = 1,
  Probing = 2,
  Healthy = 3,
  Error = 4,
}

export enum NodeStatus {
  Unknown = 0,
  Online = 1,
  Offline = 2,
  Busy = 3,
  Error = 4,
}

export enum NodeCapability {
  None = 0,
  Docker = 1,
  Kvm = 2,
}

export enum ScoringSubmissionType {
  Flag = 0,
  Writeup = 1,
  IP = 2,
  Credential = 3,
  Custom = 4,
}

/** Judgement result */
export enum AnswerResult {
  FlagSubmitted = "FlagSubmitted",
  Accepted = "Accepted",
  WrongAnswer = "WrongAnswer",
  CheatDetected = "CheatDetected",
  NotFound = "NotFound",
}

/** Game event type */
export enum EventType {
  Normal = "Normal",
  ContainerStart = "ContainerStart",
  ContainerDestroy = "ContainerDestroy",
  FlagSubmit = "FlagSubmit",
  CheatDetected = "CheatDetected",
  AwdpFlagSubmit = "AwdpFlagSubmit",
  AwdpServiceUp = "AwdpServiceUp",
  AwdpServiceDown = "AwdpServiceDown",
  AwdpServiceMumble = "AwdpServiceMumble",
  AwdpRoundStart = "AwdpRoundStart",
  AwdpAttackSuccess = "AwdpAttackSuccess",
  AwdpPatchResult = "AwdpPatchResult",
}

/** Submission type */
export enum SubmissionType {
  Unaccepted = "Unaccepted",
  FirstBlood = "FirstBlood",
  SecondBlood = "SecondBlood",
  ThirdBlood = "ThirdBlood",
  Normal = "Normal",
}

/** Environment type for challenge deployment */
export enum EnvironmentType {
  None = "None",
  Docker = "Docker",
  WindowsVM = "WindowsVM",
}

/** Container network mode */
export enum NetworkMode {
  Open = "Open",
  Isolated = "Isolated",
  Custom = "Custom",
}

/** Answer type for challenge submission */
export enum AnswerType {
  Flag = "Flag",
  File = "File",
  Custom = "Custom",
}

/** Flag score mode */
export enum FlagScoreMode {
  InheritDecay = "InheritDecay",
  FixedScore = "FixedScore",
}

/** Container status */
export enum ContainerStatus {
  Pending = "Pending",
  Running = "Running",
  Destroyed = "Destroyed",
}

export enum FileType {
  None = "None",
  Local = "Local",
  Remote = "Remote",
}

export enum ChallengeType {
  StaticAttachment = "StaticAttachment",
  StaticContainer = "StaticContainer",
  DynamicAttachment = "DynamicAttachment",
  DynamicContainer = "DynamicContainer",
}

/** Game participant permission */
export enum GamePermission {
  JoinGame = 1,
  RankOverall = 2,
  RequireReview = 4,
  ViewChallenge = 256,
  SubmitFlags = 512,
  GetScore = 1024,
  GetBlood = 2048,
  AffectDynamicScore = 4096,
  All = 2147483647,
}

/** Game announcement type */
export enum NoticeType {
  Normal = "Normal",
  FirstBlood = "FirstBlood",
  SecondBlood = "SecondBlood",
  ThirdBlood = "ThirdBlood",
  NewHint = "NewHint",
  NewChallenge = "NewChallenge",
}

/** Game type */
export enum GameType {
  /** Jeopardy */
  Jeopardy = "Jeopardy",
  /** AWDP */
  AWDP = "AWDP",
  /** Theory */
  Theory = "Theory",
  /** Mixed */
  Mixed = "Mixed",
  /** Penetration */
  Penetration = "Penetration",
}

/** AWDP challenge status from player perspective */
export enum AwdpChallengeStatus {
  /** Unattacked */
  Unattacked = "Unattacked",
  /** Attacked */
  Attacked = "Attacked",
  /** Undefended */
  Undefended = "Undefended",
  /** Defended */
  Defended = "Defended",
  /** DefenseAbnormal */
  DefenseAbnormal = "DefenseAbnormal",
  /** DefenseFailed */
  DefenseFailed = "DefenseFailed",
}

/** AWDP patch verification result */
export enum AwdpPatchStatus {
  /** Pending */
  Pending = "Pending",
  /** CheckerFailed */
  CheckerFailed = "CheckerFailed",
  /** ExpSucceeded */
  ExpSucceeded = "ExpSucceeded",
  /** ExpFailed */
  ExpFailed = "ExpFailed",
  /** Timeout */
  Timeout = "Timeout",
  /** Unsupported */
  Unsupported = "Unsupported",
}

/** Checker execution status */
export enum CheckerStatus {
  /** OK */
  OK = "OK",
  /** Mumble */
  Mumble = "Mumble",
  /** Down */
  Down = "Down",
  /** Corrupt */
  Corrupt = "Corrupt",
  /** Skipped */
  Skipped = "Skipped",
}

/** AWDP round phase status */
export enum AwdpRoundStatus {
  /** AttackPhase */
  AttackPhase = "AttackPhase",
  /** PatchPhase */
  PatchPhase = "PatchPhase",
  /** Finished */
  Finished = "Finished",
}

/** Challenge category */
export enum ChallengeCategory {
  Misc = "Misc",
  Crypto = "Crypto",
  Pwn = "Pwn",
  Web = "Web",
  Reverse = "Reverse",
  Blockchain = "Blockchain",
  Forensics = "Forensics",
  Hardware = "Hardware",
  Mobile = "Mobile",
  PPC = "PPC",
  AI = "AI",
  Pentest = "Pentest",
  OSINT = "OSINT",
  IR = "IR",
}

export enum ParticipationStatus {
  Pending = "Pending",
  Accepted = "Accepted",
  Rejected = "Rejected",
  Suspended = "Suspended",
  Unsubmitted = "Unsubmitted",
}

/** Task execution status */
export enum TaskStatus {
  Success = "Success",
  Failed = "Failed",
  Duplicate = "Duplicate",
  Denied = "Denied",
  NotFound = "NotFound",
  Exit = "Exit",
  Unhealthy = "Unhealthy",
  Degraded = "Degraded",
  Pending = "Pending",
}

/** User role enumeration */
export enum Role {
  Banned = "Banned",
  Student = "Student",
  User = "Student",
  Teacher = "Teacher",
  Monitor = "Teacher",
  Admin = "Admin",
  SuperAdmin = "SuperAdmin",
}

/** Login response status */
export enum RegisterStatus {
  LoggedIn = "LoggedIn",
  AdminConfirmationRequired = "AdminConfirmationRequired",
  EmailConfirmationRequired = "EmailConfirmationRequired",
}

export interface ApiTokenResponse {
  plainTextToken?: string;
  info?: ApiTokenModel;
}

export interface ApiTokenModel {
  /** @format guid */
  id?: string;
  name?: string;
  /** @format guid */
  creatorId?: string;
  scopes?: string[];
  resources?: ApiTokenResourceGrantModel[];
  /** @format int32 */
  requestsPerMinute?: number;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  expiresAt?: number | null;
  /** @format uint64 */
  lastUsedAt?: number | null;
  /** @format uint64 */
  revokedAt?: number | null;
}

export interface ApiTokenResourceGrantModel {
  resourceType?: string;
  resourceId?: string;
}

/** API token creation model. */
export interface ApiTokenCreateModel {
  /**
   * The user-friendly name for the token to identify its purpose.
   * @minLength 1
   * @maxLength 128
   */
  name: string;
  /** @minItems 1 */
  scopes?: string[];
  resources?: ApiTokenResourceGrantModel[];
  /**
   * @format int32
   * @min 1
   * @max 10000
   */
  requestsPerMinute?: number;
  /** @format uint64 */
  expiresAt?: number | null;
}

export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  /** @format int32 */
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  [key: string]: any;
}

/** Request response */
export interface RequestResponse {
  /** Response message */
  title?: string;
  /**
   * Status code
   * @format int32
   */
  status?: number;
}

/** Request response */
export interface RequestResponseOfRegisterStatus {
  /** Response message */
  title?: string;
  /** Data */
  data?: RegisterStatus;
  /**
   * Status code
   * @format int32
   */
  status?: number;
}

/** Account registration */
export type RegisterModel = ModelWithCaptcha & {
  /**
   * Username
   * @minLength 3
   * @maxLength 15
   */
  userName: string;
  /**
   * Password
   * @minLength 1
   */
  password: string;
  /**
   * Email
   * @format email
   * @minLength 1
   */
  email: string;
};

export interface ModelWithCaptcha {
  /** Captcha Challenge */
  challenge?: string | null;
}

/** Account recovery */
export type RecoveryModel = ModelWithCaptcha & {
  /**
   * User email
   * @format email
   * @minLength 1
   */
  email: string;
};

/** Account password reset */
export interface PasswordResetModel {
  /**
   * Password
   * @minLength 1
   */
  password: string;
  /**
   * Email
   * @minLength 1
   */
  email: string;
  /**
   * Base64 formatted token received via email
   * @minLength 1
   */
  rToken: string;
}

/** Account verification */
export interface AccountVerifyModel {
  /**
   * Base64 formatted token received via email
   * @minLength 1
   */
  token: string;
  /**
   * Base64 formatted user email
   * @minLength 1
   */
  email: string;
}

/** Login */
export type LoginModel = ModelWithCaptcha & {
  /**
   * Username or email
   * @minLength 1
   */
  userName: string;
  /**
   * Password
   * @minLength 1
   */
  password: string;
};

/** Basic account information update */
export interface ProfileUpdateModel {
  /**
   * Username
   * @minLength 3
   * @maxLength 15
   */
  userName?: string | null;
  /**
   * Description
   * @maxLength 128
   */
  bio?: string | null;
  /** Phone number */
  phone?: string | null;
  /**
   * Real name
   * @maxLength 128
   */
  realName?: string | null;
  /**
   * Student ID
   * @maxLength 64
   */
  stdNumber?: string | null;
}

/** Password change */
export interface PasswordChangeModel {
  /**
   * Old password
   * @minLength 6
   */
  old: string;
  /**
   * New password
   * @minLength 6
   */
  new: string;
}

/** Request response */
export interface RequestResponseOfBoolean {
  /** Response message */
  title?: string;
  /** Data */
  data?: boolean;
  /**
   * Status code
   * @format int32
   */
  status?: number;
}

/** Email change */
export interface MailChangeModel {
  /**
   * New email
   * @format email
   * @minLength 1
   */
  newMail: string;
}

/** Basic account information */
export interface ProfileUserInfoModel {
  /**
   * User ID
   * @format guid
   */
  userId?: string;
  /** User role */
  role?: Role;
  /** Username */
  userName?: string | null;
  /** Email */
  email?: string | null;
  /** Bio */
  bio?: string | null;
  /** Phone number */
  phone?: string | null;
  /** Real name */
  realName?: string | null;
  /** Student ID */
  stdNumber?: string | null;
  /** Avatar URL */
  avatar?: string | null;
}

/** Global configuration update */
export interface ConfigEditModel {
  /** User policy */
  accountPolicy?: AccountPolicy | null;
  /** Global configuration */
  globalConfig?: GlobalConfig | null;
  /** Game policy */
  containerPolicy?: ContainerPolicy | null;
}

/** Account policy */
export interface AccountPolicy {
  /** Allow user registration */
  allowRegister?: boolean;
  /** Activate account upon registration */
  activeOnRegister?: boolean;
  /** Use captcha verification */
  useCaptcha?: boolean;
  /** Email confirmation required for registration, email change, and password recovery */
  emailConfirmationRequired?: boolean;
  /** Email domain list, separated by commas */
  emailDomainList?: string;
}

/** Global settings */
export interface GlobalConfig {
  /** Platform prefix name */
  title?: string;
  /** Platform slogan */
  slogan?: string;
  /** Site description information */
  description?: string | null;
  /** Footer information */
  footerInfo?: string | null;
  /** Custom theme color */
  customTheme?: string | null;
  /** Use asymmetric encryption for API requests */
  apiEncryption?: boolean;
  /** Platform logo hash */
  logoHash?: string | null;
  /** Platform favicon hash */
  faviconHash?: string | null;
}

/** Container policy */
export interface ContainerPolicy {
  /** Automatically destroy the oldest container when the limit is reached */
  autoDestroyOnLimitReached?: boolean;
  /**
   * User container limit, used to limit the number of exercise containers
   * @format int32
   */
  maxExerciseContainerCountPerUser?: number;
  /**
   * Default container lifetime in minutes
   * @format int32
   * @min 1
   * @max 7200
   */
  defaultLifetime?: number;
  /**
   * Extension duration for each renewal in minutes
   * @format int32
   * @min 1
   * @max 7200
   */
  extensionDuration?: number;
  /**
   * Renewal window before container stops in minutes
   * @format int32
   * @min 1
   * @max 360
   */
  renewalWindow?: number;
}

/** List response */
export interface ArrayResponseOfUserInfoModel {
  /** Data */
  data: UserInfoModel[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

/** User information (Admin) */
export interface UserInfoModel {
  /**
   * User ID
   * @format guid
   */
  id?: string | null;
  /** Username */
  userName?: string | null;
  /** Real name */
  realName?: string | null;
  /** Student number */
  stdNumber?: string | null;
  /** Contact phone number */
  phone?: string | null;
  /** Bio */
  bio?: string | null;
  /**
   * Registration time
   * @format uint64
   */
  registerTimeUtc?: number;
  /**
   * Last visit time
   * @format uint64
   */
  lastVisitedUtc?: number;
  /** Last visit IP */
  ip?: string;
  /** Email */
  email?: string | null;
  /** Avatar URL */
  avatar?: string | null;
  /** User role */
  role?: Role | null;
  /** Is email confirmed (can log in) */
  emailConfirmed?: boolean | null;
  /** Training student groups */
  studentGroups?: UserStudentGroupModel[];
}

export interface UserStudentGroupModel {
  /** @format int32 */
  id?: number;
  name?: string;
}

/** Batch user creation (Admin) */
export interface UserCreateModel {
  /**
   * Username
   * @minLength 3
   * @maxLength 15
   */
  userName: string;
  /**
   * Password
   * @minLength 1
   */
  password: string;
  /**
   * Email
   * @format email
   * @minLength 1
   */
  email: string;
  /**
   * Real name
   * @maxLength 128
   */
  realName?: string | null;
  /**
   * Student number
   * @maxLength 64
   */
  stdNumber?: string | null;
  /** Contact phone number */
  phone?: string | null;
  /**
   * Team the user joins
   * @maxLength 20
   */
  teamName?: string | null;
  /** Role assigned to the user */
  assignedRole?: Role | null;
  /** Student groups to join after creation */
  studentGroupIds?: number[] | null;
}

/** List response */
export interface ArrayResponseOfTeamInfoModel {
  /** Data */
  data: TeamInfoModel[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

/** Team information */
export interface TeamInfoModel {
  /**
   * Team ID
   * @format int32
   */
  id?: number;
  /** Team name */
  name?: string | null;
  /** Team bio */
  bio?: string | null;
  /** Avatar URL */
  avatar?: string | null;
  /** Is locked */
  locked?: boolean;
  /** Team members */
  members?: TeamUserInfoModel[] | null;
}

/** Team member information */
export interface TeamUserInfoModel {
  /**
   * User ID
   * @format guid
   */
  id?: string | null;
  /** Username */
  userName?: string | null;
  /** Bio */
  bio?: string | null;
  /** Avatar URL */
  avatar?: string | null;
  /** Is Captain */
  captain?: boolean;
}

/** Team information modification (Admin) */
export interface AdminTeamModel {
  /**
   * Team name
   * @maxLength 20
   */
  name?: string | null;
  /**
   * Team bio
   * @maxLength 72
   */
  bio?: string | null;
  /** Is locked */
  locked?: boolean | null;
}

/** User information modification (Admin) */
export interface AdminUserInfoModel {
  /**
   * Username
   * @minLength 3
   * @maxLength 15
   */
  userName?: string | null;
  /**
   * Email
   * @format email
   */
  email?: string | null;
  /**
   * Signature
   * @maxLength 128
   */
  bio?: string | null;
  /** Phone number */
  phone?: string | null;
  /**
   * Real name
   * @maxLength 128
   */
  realName?: string | null;
  /**
   * Student number
   * @maxLength 64
   */
  stdNumber?: string | null;
  /** Is email confirmed (can log in) */
  emailConfirmed?: boolean | null;
  /** User role */
  role?: Role | null;
  /** Student groups to sync */
  studentGroupIds?: number[] | null;
}

/** Log information (Admin) */
export interface LogMessageModel {
  /**
   * Log time
   * @format uint64
   */
  time?: number;
  /** Username */
  name?: string | null;
  level?: string | null;
  /** IP address */
  ip?: string | null;
  /** Log message */
  msg?: string | null;
  /** Task status */
  status?: TaskStatus | null;
}

/** Modify the participation information */
export interface ParticipationEditModel {
  /** Participation Status */
  status?: ParticipationStatus | null;
  /**
   * The division of the participated team
   * @format int32
   */
  divisionId?: number | null;
}

/** Game writeup information */
export interface WriteupInfoModel {
  /** Division ID to Division Name mapping */
  divisions?: Record<string, string>;
  /** Writeups list */
  writeups?: WriteupInfo[];
}

export interface WriteupInfo {
  /**
   * Participation ID
   * @format int32
   */
  id?: number;
  /** Team information */
  team?: TeamInfoModel;
  /** File URL */
  url?: string;
  /**
   * File upload time
   * @format uint64
   */
  uploadTimeUtc?: number;
  /**
   * The division the team belongs to
   * @format int32
   */
  divisionId?: number | null;
}

/** List response */
export interface ArrayResponseOfContainerInstanceModel {
  /** Data */
  data: ContainerInstanceModel[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

/** Container instance information (Admin) */
export interface ContainerInstanceModel {
  /** Team */
  team?: TeamModel | null;
  /** Challenge */
  challenge?: ChallengeModel | null;
  /** Container image */
  image?: string;
  /**
   * Container database ID
   * @format guid
   */
  containerGuid?: string;
  /** Container ID */
  containerId?: string;
  /**
   * Container creation time
   * @format uint64
   */
  startedAt?: number;
  /**
   * Expected container stop time
   * @format uint64
   */
  expectStopAt?: number;
  /** Access IP */
  ip?: string;
  /**
   * Access port
   * @format int32
   */
  port?: number;
}

/** Team information */
export interface TeamModel {
  /**
   * Team ID
   * @format int32
   */
  id?: number;
  /** Team name */
  name?: string;
  /** Team avatar */
  avatar?: string | null;
}

/** Challenge information */
export interface ChallengeModel {
  /**
   * Challenge ID
   * @format int32
   */
  id?: number;
  /** Challenge title */
  title?: string;
  /** Challenge category */
  category?: ChallengeCategory;
}

/** List response */
export interface ArrayResponseOfLocalFile {
  /** Data */
  data: LocalFile[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

export interface LocalFile {
  /**
   * File hash
   * @maxLength 64
   */
  hash?: string;
  /**
   * File name
   * @minLength 1
   */
  name: string;
}

/** AWDP 服务视图模型 */
export interface AwdpServiceViewModel {
  /** @format int32 */
  id?: number;
  name?: string;
  imageName?: string;
  /** @format int32 */
  exposePort?: number;
  checkerScript?: string | null;
  checkerEntrypoint?: string | null;
  expScript?: string | null;
  expEntrypoint?: string | null;
  /** @format int32 */
  originalScore?: number;
  /** @format int32 */
  attackPoints?: number;
  /** @format int32 */
  slaPoints?: number;
  /** @format int32 */
  patchPoints?: number;
  /** @format int32 */
  serviceAbnormalPenalty?: number;
  /** @format int32 */
  maxAttackPerRound?: number;
  /** @format int32 */
  attackPhaseMinutes?: number;
  /** @format int32 */
  patchPhaseMinutes?: number;
  /** @format int32 */
  totalRounds?: number;
  /** @format int32 */
  maxResetCount?: number;
  /** @format int32 */
  maxRecoveryCount?: number;
}

/** AWDP 排行榜条目 */
export interface AwdpScoreboardItem {
  /** @format int32 */
  rank?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  /** @format int32 */
  ctfScore?: number;
  /** @format int32 */
  awdpScore?: number;
  /** @format int32 */
  totalScore?: number;
  /** @format int32 */
  attackScore?: number;
  /** @format int32 */
  slaScore?: number;
  /** @format int32 */
  patchScore?: number;
  /** @format int32 */
  penaltyScore?: number;
}

/** AWDP 服务创建模型 */
export interface AwdpServiceCreateModel {
  /**
   * 服务名称
   * @minLength 1
   */
  name: string;
  /**
   * 容器镜像名
   * @minLength 1
   */
  imageName: string;
  /**
   * 暴露端口
   * @format int32
   */
  exposePort?: number;
  /** Checker 脚本内容 */
  checkerScript?: string | null;
  /** Checker 入口命令 */
  checkerEntrypoint?: string | null;
  /** Exp 脚本内容 */
  expScript?: string | null;
  /** Exp 入口命令 */
  expEntrypoint?: string | null;
  /**
   * 原始分数
   * @format int32
   */
  originalScore?: number;
  /**
   * 攻击得分
   * @format int32
   */
  attackPoints?: number;
  /**
   * SLA 得分
   * @format int32
   */
  slaPoints?: number;
  /**
   * 修补成功得分
   * @format int32
   */
  patchPoints?: number;
  /**
   * 服务异常扣分
   * @format int32
   */
  serviceAbnormalPenalty?: number;
  /**
   * 每轮最大攻击次数
   * @format int32
   */
  maxAttackPerRound?: number;
  /**
   * 攻击阶段时长 (分钟)
   * @format int32
   */
  attackPhaseMinutes?: number;
  /**
   * 修补阶段时长 (分钟)
   * @format int32
   */
  patchPhaseMinutes?: number;
  /**
   * 总轮数
   * @format int32
   */
  totalRounds?: number;
  /**
   * 最大重置次数
   * @format int32
   */
  maxResetCount?: number;
  /**
   * 最大一键恢复次数
   * @format int32
   */
  maxRecoveryCount?: number;
}

/** AWDP 服务更新模型 */
export type AwdpServiceUpdateModel = AwdpServiceCreateModel & object;

/** AWDP 比赛状态模型 */
export interface AwdpGameStatusModel {
  /** @format int32 */
  gameId?: number;
  /** @format int32 */
  currentRound?: number;
  /** @format uint64 */
  roundStartTime?: number;
  /** @format int32 */
  attackPhaseMinutes?: number;
  /** @format int32 */
  patchPhaseMinutes?: number;
  /** AWDP round phase status */
  status?: AwdpRoundStatus;
}

/** AWDP 服务状态模型 (SignalR 推送用) */
export interface AwdpServiceStatusModel {
  /** @format int32 */
  serviceId?: number;
  serviceName?: string;
  teamStatuses?: AwdpTeamServiceStatus[];
}

/** AWDP 队伍服务状态 */
export interface AwdpTeamServiceStatus {
  /** @format int32 */
  instanceId?: number;
  /** @format int32 */
  serviceId?: number;
  serviceName?: string;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  ipAddress?: string | null;
  /** @format int32 */
  port?: number | null;
  lastCheckerStatus?: CheckerStatus | null;
  isRunning?: boolean;
  /** @format int32 */
  remainingResetCount?: number;
  /** @format int32 */
  remainingRecoveryCount?: number;
  canManage?: boolean;
}

/** AWDP 容器操作结果 */
export interface AwdpInstanceActionModel {
  /** @format int32 */
  instanceId?: number;
  success?: boolean;
  message?: string;
}

/** List response */
export interface ArrayResponseOfAwdpPatchSubmissionViewModel {
  /** Data */
  data: AwdpPatchSubmissionViewModel[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

/** AWDP 修补包提交视图 */
export interface AwdpPatchSubmissionViewModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  roundId?: number;
  /** @format int32 */
  roundNumber?: number;
  /** @format int32 */
  serviceId?: number;
  serviceName?: string;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  patchFileHash?: string;
  /** @format uint64 */
  submittedAt?: number;
  /** Checker execution status */
  checkerResult?: CheckerStatus;
  /** AWDP patch verification result */
  expResult?: AwdpPatchStatus;
  /** AWDP patch verification result */
  finalStatus?: AwdpPatchStatus;
  message?: string | null;
}

/** List response */
export interface ArrayResponseOfAwdpAttackLogItem {
  /** Data */
  data: AwdpAttackLogItem[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

/** AWDP 攻击日志条目 */
export interface AwdpAttackLogItem {
  /** @format uint64 */
  time?: number;
  attackerTeam?: string;
  victimTeam?: string;
  serviceName?: string;
  /** @format int32 */
  points?: number;
}

/** AWDP Flag submission result model */
export interface AwdpSubmitResultModel {
  accepted?: boolean;
  /** @format int32 */
  points?: number;
  /** @format int32 */
  roundNumber?: number;
  /** @format int32 */
  serviceId?: number;
  serviceName?: string;
  message?: string;
}

/** AWDP Flag 提交模型 */
export interface AwdpSubmitModel {
  /**
   * Flag 值
   * @minLength 1
   */
  flag: string;
}

/** AWDP 修补包状态条目 */
export interface AwdpPatchStatusItem {
  /** @format int32 */
  serviceId?: number;
  serviceName?: string;
  /** AWDP challenge status from player perspective */
  attackStatus?: AwdpChallengeStatus;
  /** AWDP challenge status from player perspective */
  defenseStatus?: AwdpChallengeStatus;
  lastPatchResult?: AwdpPatchStatus | null;
  /** @format uint64 */
  lastPatchTime?: number | null;
  message?: string | null;
}

/** Post item (Edit) */
export interface PostEditModel {
  /**
   * Post title
   * @maxLength 50
   */
  title?: string | null;
  /** Post summary */
  summary?: string | null;
  /** Post content */
  content?: string | null;
  /** Post tags */
  tags?: string[] | null;
  /** Is pinned */
  isPinned?: boolean | null;
}

/** Post details */
export interface PostDetailModel {
  /**
   * Post ID
   * @minLength 1
   */
  id: string;
  /**
   * Post title
   * @minLength 1
   */
  title: string;
  /**
   * Post summary
   * @minLength 1
   */
  summary: string;
  /**
   * Post content
   * @minLength 1
   */
  content: string;
  /** Is pinned */
  isPinned: boolean;
  /** Post tags */
  tags?: string[] | null;
  /** Author avatar */
  authorAvatar?: string | null;
  /** Author name */
  authorName?: string | null;
  /**
   * Publish time
   * @format uint64
   * @minLength 1
   */
  time: number;
}

/** Game information (Edit) */
export interface GameInfoModel {
  /**
   * Game ID
   * @format int32
   */
  id?: number;
  /**
   * Game title
   * @minLength 1
   */
  title: string;
  /** Is hidden */
  hidden?: boolean;
  /** Game summary */
  summary?: string;
  /** Game detailed description */
  content?: string;
  /** Accept teams without review */
  acceptWithoutReview?: boolean;
  /** Is writeup required */
  writeupRequired?: boolean;
  /**
   * Game invitation code
   * @maxLength 32
   */
  inviteCode?: string | null;
  /**
   * Team member count limit, 0 means no limit
   * @format int32
   */
  teamMemberCountLimit?: number;
  /**
   * Container count limit per team
   * @format int32
   */
  containerCountLimit?: number;
  /** Game poster URL */
  poster?: string | null;
  /** Game public key */
  publicKey?: string;
  /** Is the game in practice mode (accessible even after the game ends) */
  practiceMode?: boolean;
  /** Is the game for internal demo/testing usage */
  isTest?: boolean;
  /**
   * Start time
   * @format uint64
   * @minLength 1
   */
  start: number;
  /**
   * End time
   * @format uint64
   * @minLength 1
   */
  end: number;
  /**
   * Writeup submission deadline
   * @format uint64
   */
  writeupDeadline?: number;
  /** Writeup additional notes */
  writeupNote?: string;
  /**
   * Blood bonus points
   * @format int64
   */
  bloodBonus?: number;
  /** Game type (Jeopardy, AWD, Theory, Mixed) */
  gameType?: GameType;
}

/** List response */
export interface ArrayResponseOfGameInfoModel {
  /** Data */
  data: GameInfoModel[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

/**
 * Game notice, which will be sent to the client.
 * Information includes first, second, and third blood notifications, hint release notifications, challenging opening notifications, etc.
 */
export type GameNotice = FormattableDataOfNoticeType & {
  /** @format int32 */
  id: number;
  /**
   * Publish time
   * @format uint64
   * @minLength 1
   */
  time: number;
};

/** Formattable data */
export interface FormattableDataOfNoticeType {
  /** Data type */
  type: NoticeType;
  /** List of formatted values */
  values: string[];
}

/** Game notice (Edit) */
export interface GameNoticeModel {
  /**
   * Notice content
   * @minLength 1
   */
  content: string;
}

export interface Division {
  /** @format int32 */
  id: number;
  /**
   * The name of the division.
   * @minLength 1
   * @maxLength 31
   */
  name: string;
  /**
   * Invitation code for joining the division.
   * @maxLength 32
   */
  inviteCode?: string | null;
  /** Permissions associated with the division. */
  defaultPermissions?: GamePermission;
  /** Challenge configs for this division. */
  challengeConfigs?: DivisionChallengeConfig[];
}

export interface DivisionChallengeConfig {
  /** @format int32 */
  challengeId: number;
  /** Challenge Specific Permissions */
  permissions?: GamePermission;
}

export interface DivisionCreateModel {
  /**
   * The name of the division.
   * @minLength 1
   * @maxLength 31
   */
  name: string;
  /**
   * Invitation code for joining the division.
   * @maxLength 32
   */
  inviteCode?: string | null;
  /** Permissions associated with the division. */
  defaultPermissions?: GamePermission | null;
  /** Challenge configs for this division. */
  challengeConfigs?: DivisionChallengeConfigModel[] | null;
}

export interface DivisionChallengeConfigModel {
  /**
   * Challenge ID
   * @format int32
   */
  challengeId: number;
  /** Challenge Specific Permissions */
  permissions?: GamePermission;
}

export interface DivisionEditModel {
  /**
   * The name of the division.
   * @maxLength 31
   */
  name?: string | null;
  /**
   * Invitation code for joining the division.
   * @maxLength 32
   */
  inviteCode?: string | null;
  /** Permissions associated with the division. */
  defaultPermissions?: GamePermission | null;
  /** Challenge configs for this division. */
  challengeConfigs?: DivisionChallengeConfigModel[] | null;
}

/** Challenge detailed information (Edit) */
export interface ChallengeEditDetailModel {
  /**
   * Challenge ID
   * @format int32
   */
  id?: number;
  /**
   * Challenge title
   * @minLength 1
   */
  title: string;
  /** Challenge content */
  content?: string;
  /** Challenge category */
  category: ChallengeCategory;
  /** Challenge type */
  type: ChallengeType;
  /** Challenge hints */
  hints?: string[];
  /**
   * Flag template, used to generate Flag based on Token and challenge, game information
   * @maxLength 120
   */
  flagTemplate?: string | null;
  /** Is the challenge enabled */
  isEnabled: boolean;
  /**
   * Number of people who passed
   * @format int32
   */
  acceptedCount: number;
  /** Unified file name (only for dynamic attachments) */
  fileName?: string | null;
  /** Challenge attachment (dynamic attachments are stored in FlagInfoModel) */
  attachment?: Attachment | null;
  /** Test container */
  testContainer?: ContainerInfoModel | null;
  /** Challenge Flag information */
  flags: FlagInfoModel[];
  /**
   * Image name and tag
   * @minLength 1
   */
  containerImage: string;
  /**
   * Memory limit (MB)
   * @format int32
   */
  memoryLimit?: number | null;
  /**
   * CPU limit (0.1 CPUs)
   * @format int32
   */
  cpuCount?: number | null;
  /**
   * Storage limit (MB)
   * @format int32
   */
  storageLimit?: number | null;
  /**
   * Container exposed port
   * @format int32
   */
  exposePort?: number | null;
  /** Container network mode */
  networkMode?: NetworkMode | null;
  /** Whether to record traffic */
  enableTrafficCapture?: boolean | null;
  /** Whether to disable blood bonus */
  disableBloodBonus?: boolean | null;
  /**
   * The deadline of the challenge, null means no deadline
   * @format uint64
   */
  deadlineUtc?: number | null;
  /**
   * Maximum number of submissions allowed per team (0 = no limit)
   * @format int32
   */
  submissionLimit: number;
  /** Deployment environment type */
  environment?: EnvironmentType;
  /**
   * Image template ID for VM deployment
   * @format int32
   */
  imageTemplateId?: number | null;
  /**
   * Initial score
   * @format int32
   */
  originalScore: number;
  /**
   * Minimum score rate
   * @format double
   * @min 0
   * @max 1
   */
  minScoreRate: number;
  /**
   * Difficulty coefficient
   * @format double
   */
  difficulty: number;
}

export interface Attachment {
  /** @format int32 */
  id: number;
  /** Attachment type */
  type: FileType;
  /** Default file URL */
  url?: string | null;
  /**
   * Get attachment size
   * @format int64
   */
  fileSize?: number | null;
}

export interface ContainerInfoModel {
  /** Container status */
  status?: ContainerStatus;
  /**
   * Container creation time
   * @format uint64
   */
  startedAt?: number;
  /**
   * Expected container stop time
   * @format uint64
   */
  expectStopAt?: number;
  /** Challenge entry point */
  entry?: string;
}

export interface FlagInfoModel {
  /** @format int32 */
  id?: number;
  flag?: string;
  /** @format int32 */
  orderIndex?: number;
  description?: string | null;
  /** Flag score mode */
  scoreMode?: FlagScoreMode;
  /** @format int32 */
  fixedScore?: number;
  /** @format int32 */
  maxAttempts?: number;
  /** Answer type for challenge submission */
  answerType?: AnswerType;
  customName?: string | null;
  attachmentHash?: string | null;
  attachment?: Attachment | null;
}

/** Basic challenge information (Edit) */
export interface ChallengeInfoModel {
  /**
   * Challenge ID
   * @format int32
   */
  id?: number;
  /**
   * Challenge title
   * @minLength 1
   */
  title: string;
  /** Challenge category */
  category?: ChallengeCategory;
  /** Challenge type */
  type?: ChallengeType;
  /** Container image name and tag. Required when creating a container challenge. */
  containerImage?: string | null;
  /**
   * Container exposed port. Required when creating a container challenge.
   * @format int32
   * @min 1
   * @max 65535
   */
  exposePort?: number | null;
  /** Deployment environment type. */
  environment?: EnvironmentType | null;
  /**
   * Image template ID for VM deployment.
   * @format int32
   */
  imageTemplateId?: number | null;
  /** Is the challenge enabled */
  isEnabled?: boolean;
  /**
   * Challenge score
   * @format int32
   */
  score?: number;
  /**
   * Minimum score
   * @format int32
   */
  minScore?: number;
  /**
   * Original score
   * @format int32
   */
  originalScore?: number;
  /**
   * The deadline of the challenge, null means no deadline
   * @format uint64
   */
  deadlineUtc?: number | null;
}

/** Challenge update information (Edit) */
export interface ChallengeUpdateModel {
  /**
   * Challenge title
   * @minLength 1
   */
  title?: string | null;
  /** Challenge content */
  content?: string | null;
  /**
   * Flag template, used to generate Flag based on Token and challenge/game information
   * @maxLength 120
   */
  flagTemplate?: string | null;
  /** Challenge category */
  category?: ChallengeCategory | null;
  /** Challenge hints */
  hints?: string[] | null;
  /** Is the challenge enabled */
  isEnabled?: boolean | null;
  /** Unified file name */
  fileName?: string | null;
  /**
   * The deadline of the challenge, null means no deadline
   * @format uint64
   */
  deadlineUtc?: number | null;
  /**
   * Maximum number of flag submissions allowed per team for this challenge (0 = no limit)
   * @format int32
   * @min 0
   * @max 10000
   */
  submissionLimit?: number | null;
  /** Container image name and tag */
  containerImage?: string | null;
  /**
   * Memory limit (MB)
   * @format int32
   * @min 32
   * @max 1048576
   */
  memoryLimit?: number | null;
  /**
   * CPU limit (0.1 CPUs)
   * @format int32
   * @min 1
   * @max 1024
   */
  cpuCount?: number | null;
  /**
   * Storage limit (MB)
   * @format int32
   * @min 0
   * @max 1048576
   */
  storageLimit?: number | null;
  /**
   * Container exposed port
   * @format int32
   * @min 1
   * @max 65535
   */
  exposePort?: number | null;
  /** Container network mode */
  networkMode?: NetworkMode | null;
  /** Is traffic capture enabled (disabled by default) */
  enableTrafficCapture?: boolean | null;
  /** Is blood bonus disabled (enable by default) */
  disableBloodBonus?: boolean | null;
  /**
   * Initial score
   * @format int32
   */
  originalScore?: number | null;
  /**
   * Minimum score rate
   * @format double
   * @min 0
   * @max 1
   */
  minScoreRate?: number | null;
  /**
   * Difficulty coefficient
   * @format double
   */
  difficulty?: number | null;
  /** Deployment environment type */
  environment?: EnvironmentType | null;
  /**
   * Image template ID for VM/container deployment
   * @format int32
   */
  imageTemplateId?: number | null;
}

/** New attachment information (Edit) */
export interface AttachmentCreateModel {
  /** Attachment type */
  attachmentType?: FileType;
  /** File hash (local file) */
  fileHash?: string | null;
  /** File URL (remote file) */
  remoteUrl?: string | null;
}

/** New Flag information (Edit) */
export interface FlagCreateModel {
  /**
   * Flag text
   * @minLength 1
   * @maxLength 127
   */
  flag: string;
  /**
   * Display order index for this flag in the challenge
   * @format int32
   */
  orderIndex?: number;
  /**
   * Description of this flag/checkpoint
   * @maxLength 512
   */
  description?: string | null;
  /** Score mode for this flag */
  scoreMode?: FlagScoreMode;
  /**
   * Fixed score value (used when ScoreMode is Fixed)
   * @format int32
   */
  fixedScore?: number;
  /**
   * Maximum number of submission attempts for this flag
   * @format int32
   */
  maxAttempts?: number;
  /**
   * SHA256 hash of the attachment file
   * @maxLength 128
   */
  attachmentHash?: string | null;
  /** Type of answer expected */
  answerType?: AnswerType;
  /**
   * Custom display name for this flag
   * @maxLength 64
   */
  customName?: string | null;
  /** Attachment type */
  attachmentType?: FileType;
  /** File hash (local file) */
  fileHash?: string | null;
  /** File URL (remote file) */
  remoteUrl?: string | null;
}

/** Basic game information, excluding detailed description and current team registration status */
export interface BasicGameInfoModel {
  /** @format int32 */
  id: number;
  /** Game title */
  title?: string;
  /** Game summary */
  summary?: string;
  /** Poster image URL */
  poster?: string | null;
  /**
   * Team member limit
   * @format int32
   */
  limit?: number;
  /**
   * Start time
   * @format uint64
   * @minLength 1
   */
  start: number;
  /**
   * End time
   * @format uint64
   * @minLength 1
   */
  end: number;
}

/** List response */
export interface ArrayResponseOfBasicGameInfoModel {
  /** Data */
  data: BasicGameInfoModel[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

/** Detailed game information, including detailed introduction and current team registration status */
export interface DetailedGameInfoModel {
  /** @format int32 */
  id?: number;
  /** Game title */
  title?: string;
  /** Game description */
  summary?: string;
  /** Detailed introduction of the game */
  content?: string;
  /** Whether the game is hidden */
  hidden?: boolean;
  /** List of participation divisions */
  divisions?: DivisionInfo[] | null;
  /** Whether an invitation code is required */
  inviteCodeRequired?: boolean;
  /** Whether writeup submission is required */
  writeupRequired?: boolean;
  /** Game poster URL */
  poster?: string | null;
  /**
   * Team member count limit
   * @format int32
   */
  limit?: number;
  /**
   * Number of teams registered for participation
   * @format int32
   */
  teamCount?: number;
  /**
   * Current registered division
   * @format int32
   */
  division?: number | null;
  /** Team name for participation */
  teamName?: string | null;
  /** Whether the game is in practice mode (can still be accessed after the game ends) */
  practiceMode?: boolean;
  /** Team participation status */
  status?: ParticipationStatus;
  /**
   * Start time
   * @format uint64
   */
  start?: number;
  /**
   * End time
   * @format uint64
   */
  end?: number;
  /** Game type (Jeopardy, AWD, Theory, Mixed) */
  gameType?: GameType;
}

export interface DivisionInfo {
  /**
   * Division ID
   * @format int32
   */
  id?: number;
  /** Division name */
  name?: string;
  /** Is the division invite code required */
  inviteCodeRequired?: boolean;
}

export interface GameJoinCheckInfoModel {
  /** The teams that the current user has joined and participated in the game */
  joinedTeams?: JoinedTeam[];
  /** IDs of divisions that can be joined */
  joinableDivisions?: number[];
}

export interface JoinedTeam {
  /**
   * Team ID
   * @format int32
   */
  id: number;
  /**
   * The division ID the team has joined
   * @format int32
   */
  division: number;
}

export interface GameJoinModel {
  /**
   * Team ID for participation
   * @format int32
   */
  teamId: number;
  /**
   * Division for participation
   * @format int32
   */
  divisionId?: number | null;
  /** Invitation code for participation */
  inviteCode?: string | null;
}

/** Scoreboard */
export interface ScoreboardModel {
  /**
   * Update time
   * @format uint64
   * @minLength 1
   */
  updateTimeUtc: number;
  /**
   * Blood bonus coefficient
   * @format int64
   */
  bloodBonus: number;
  /** List of top ten timelines */
  timelines: TimeLineItem[];
  /** List of team information */
  items: ScoreboardItem[];
  /** List of division information */
  divisions: DivisionItem[];
  /** Challenge information */
  challenges: Record<string, ChallengeInfo[]>;
  /**
   * Number of challenges
   * @format int32
   */
  challengeCount: number;
}

export interface TimeLineItem {
  /** @format int32 */
  divisionId?: number;
  teams?: TopTimeLine[];
}

export interface TopTimeLine {
  /**
   * Team ID
   * @format int32
   */
  id: number;
  /**
   * Team name
   * @minLength 1
   */
  name: string;
  /** Timeline */
  items: TimeLine[];
}

export interface TimeLine {
  /**
   * Time
   * @format uint64
   * @minLength 1
   */
  time: number;
  /**
   * Score
   * @format int32
   */
  score: number;
}

export interface ScoreboardItem {
  /**
   * Team ID
   * @format int32
   */
  id: number;
  /**
   * Team name
   * @minLength 1
   */
  name: string;
  /** Team Bio */
  bio?: string | null;
  /**
   * Division of participation
   * @format int32
   */
  divisionId?: number | null;
  /** Team avatar */
  avatar?: string | null;
  /**
   * CTF Score
   * @format int32
   */
  ctfScore: number;
  /**
   * AWDP Score
   * @format int32
   */
  awdScore: number;
  /**
   * Penetration Score
   * @format int32
   */
  pentestScore: number;
  /**
   * Total Score
   * @format int32
   */
  score: number;
  /**
   * Rank
   * @format int32
   */
  rank: number;
  /**
   * Division rank
   * @format int32
   */
  divisionRank?: number | null;
  /**
   * Last submission time
   * @format uint64
   * @minLength 1
   */
  lastSubmissionTime: number;
  /** List of solved challenges */
  solvedChallenges: ChallengeItem[];
  /**
   * Number of solved challenges
   * @format int32
   */
  solvedCount: number;
}

export interface ChallengeItem {
  /**
   * Challenge ID
   * @format int32
   */
  id: number;
  /**
   * Flag ID
   * @format int32
   */
  flagId: number;
  /**
   * Challenge score
   * @format int32
   */
  score: number;
  /** Submission type (unsolved, first blood, second blood, third blood, or others) */
  type: SubmissionType;
  /** Username of the solver */
  userName?: string | null;
  /**
   * Submission time for the challenge, used to calculate the timeline
   * @format uint64
   * @minLength 1
   */
  time: number;
}

export interface DivisionItem {
  /**
   * Division ID
   * @format int32
   */
  id: number;
  /**
   * The name of the division.
   * @minLength 1
   */
  name: string;
  /** Permissions associated with the division. */
  defaultPermissions: GamePermission;
  /** Challenge configs for this division. */
  challengeConfigs: Record<string, DivisionChallengeItem>;
}

export interface DivisionChallengeItem {
  /**
   * Challenge ID
   * @format int32
   */
  challengeId: number;
  /** Permissions for a specific challenge. */
  permissions: GamePermission;
}

export interface ChallengeInfo {
  /**
   * Challenge ID
   * @format int32
   */
  id: number;
  /**
   * Challenge title
   * @minLength 1
   */
  title: string;
  /** Challenge category */
  category: ChallengeCategory;
  /**
   * Challenge score
   * @format int32
   */
  score: number;
  /**
   * Number of teams that solved the challenge
   * @format int32
   */
  solved: number;
  /**
   * Total number of flags in this challenge
   * @format int32
   */
  totalFlags: number;
  /**
   * The deadline of the challenge, null means no deadline
   * @format uint64
   */
  deadline?: number | null;
  /** Bloods for the challenge */
  bloods: Blood[];
  /** Whether to disable blood bonus */
  disableBloodBonus: boolean;
}

export interface Blood {
  /**
   * Team ID
   * @format int32
   */
  id: number;
  /**
   * Team name
   * @minLength 1
   */
  name: string;
  /** Team avatar */
  avatar?: string | null;
  /**
   * Time when the blood was obtained
   * @format uint64
   */
  submitTimeUtc?: number | null;
}

/**
 * Game event, recorded but not sent to the client.
 * Information includes flag submission, container start/stop, cheating, and score changes.
 */
export type GameEvent = FormattableDataOfEventType & {
  /**
   * Publish time
   * @format uint64
   * @minLength 1
   */
  time: number;
  /** Related username */
  user?: string;
  /** Related team name */
  team?: string;
};

/** Formattable data */
export interface FormattableDataOfEventType {
  /** Data type */
  type: EventType;
  /** List of formatted values */
  values: string[];
}

export interface Submission {
  /**
   * Submitted answer string
   * @maxLength 127
   */
  answer?: string;
  /** Status of the submitted answer */
  status?: AnswerResult;
  /**
   * Time the answer was submitted
   * @format uint64
   */
  time?: number;
  /** User who submitted */
  user?: string;
  /** Team that submitted */
  team?: string;
  /** Challenge that was submitted */
  challenge?: string;
  /** Type of submission (Flag, Writeup, IP, Credential, Custom) */
  submissionType?: ScoringSubmissionType;
  /**
   * JSON content of the submission (used for Writeup, IP, Credential, Custom types)
   * @maxLength 4096
   */
  content?: string | null;
  /**
   * Reviewer feedback/comment
   * @maxLength 1024
   */
  reviewComment?: string | null;
  /**
   * Attempt number for this submission type (starts at 1)
   * @format int32
   */
  attemptNumber?: number;
  /**
   * Score awarded for this submission (set by scoring engine or manual review)
   * @format int32
   */
  score?: number;
  /** Concurrency token */
  concurrencyToken?: number;
  /**
   * Flag ID (nullable for backward compatibility)
   * @format int32
   */
  flagId?: number | null;
  /** Flag context */
  flagContext?: FlagContext | null;
}

export interface FlagContext {
  /** @format int32 */
  id?: number;
  /**
   * Flag content
   * @minLength 1
   * @maxLength 127
   */
  flag: string;
  /** Whether it is occupied */
  isOccupied?: boolean;
  /**
   * Order index for multi-flag challenges
   * @format int32
   */
  orderIndex?: number;
  /**
   * Description of this flag/answer
   * @maxLength 512
   */
  description?: string | null;
  /** Score mode for this flag */
  scoreMode?: FlagScoreMode;
  /**
   * Fixed score value (used when ScoreMode is Fixed)
   * @format int32
   */
  fixedScore?: number;
  /**
   * Maximum submission attempts for this flag
   * @format int32
   */
  maxAttempts?: number;
  /**
   * SHA256 hash of the attachment file
   * @maxLength 128
   */
  attachmentHash?: string | null;
  /** Type of answer expected for this flag */
  answerType?: AnswerType;
  /**
   * Custom display name for this flag
   * @maxLength 64
   */
  customName?: string | null;
  /**
   * Attachment ID
   * @format int32
   */
  attachmentId?: number | null;
  /** Attachment */
  attachment?: Attachment | null;
  /**
   * Challenge ID
   * @format int32
   */
  challengeId?: number | null;
  /** Challenge */
  challenge?: GameChallenge | null;
  /**
   * Exercise ID
   * @format int32
   */
  exerciseId?: number | null;
  /** Exercise */
  exercise?: ExerciseChallenge | null;
}

export type GameChallenge = Challenge & {
  /** Whether to record traffic */
  enableTrafficCapture?: boolean;
  /** Whether to disable blood bonus */
  disableBloodBonus?: boolean;
  /**
   * Initial score
   * @format int32
   */
  originalScore: number;
  /**
   * Minimum score rate
   * @format double
   * @min 0
   * @max 1
   */
  minScoreRate: number;
  /**
   * Difficulty coefficient
   * @format double
   */
  difficulty: number;
  /**
   * Current score of the challenge
   * @format int32
   */
  currentScore?: number;
  /** Submissions */
  submissions?: Submission[];
  /** Challenge instances */
  instances?: GameInstance[];
  /** Teams that activated the challenge */
  teams?: Participation[];
  /** Configurations for divisions */
  divisionConfigs?: DivisionChallengeConfig[];
  /** First solves recorded for this challenge. */
  firstSolves?: FirstSolve[] | null;
  /**
   * Game ID
   * @format int32
   */
  gameId?: number;
  /** Game object */
  game?: Game;
};

export type GameInstance = Instance & {
  /** Get instance attachment */
  attachment?: Attachment | null;
  /** Get instance attachment URL */
  attachmentUrl?: string | null;
  /** @format int32 */
  challengeId: number;
  /** Challenge object */
  challenge?: GameChallenge;
  /** @format int32 */
  participationId: number;
  /** Participation team object */
  participation?: Participation;
};

/** Participation information */
export interface Participation {
  /** Participation status */
  status: ParticipationStatus;
  /**
   * Team token
   * @minLength 1
   */
  token: string;
  /** Team writeup */
  writeup?: LocalFile | null;
  /** Members participating in the team */
  members?: UserParticipation[];
  /** Challenges activated by the team */
  challenges?: GameChallenge[];
  /** Game instances */
  instances?: GameInstance[];
  /** Submissions */
  submissions?: Submission[];
  /** First solves recorded for this participation. */
  firstSolves?: FirstSolve[];
  /**
   * Game ID
   * @format int32
   */
  gameId: number;
  /** Game */
  game?: Game;
  /**
   * Team ID
   * @format int32
   */
  teamId: number;
  /** Team */
  team?: Team;
  /**
   * Division ID
   * @format int32
   */
  divisionId?: number | null;
  /** Division this participation belongs to */
  division?: Division | null;
}

export interface UserParticipation {
  /** Participation object */
  participation?: Participation;
  /**
   * User ID
   * @format guid
   * @minLength 1
   */
  userId: string;
  /** User */
  user?: UserInfo;
  /**
   * Team ID
   * @format int32
   */
  teamId: number;
  /** Team */
  team?: Team;
  /**
   * Game ID
   * @format int32
   */
  gameId: number;
  /** Game */
  game?: Game;
  /**
   * Participation ID
   * @format int32
   */
  participationId: number;
}

export type UserInfo = IdentityUserOfGuid & {
  /**
   * Override Guid to use Ulid
   * @format guid
   */
  id?: string;
  /** User role */
  role?: Role;
  /** User's recent IP address */
  ip?: string;
  /**
   * User's last sign-in time
   * @format uint64
   */
  lastSignedInUtc?: number;
  /**
   * User's last visit time
   * @format uint64
   */
  lastVisitedUtc?: number;
  /**
   * User registration time
   * @format uint64
   */
  registerTimeUtc?: number;
  /**
   * User bio
   * @maxLength 128
   */
  bio?: string;
  /**
   * Real name
   * @maxLength 128
   */
  realName?: string;
  /**
   * Student ID
   * @maxLength 64
   */
  stdNumber?: string;
  /** Hide in exercise scoreboard */
  exerciseVisible?: boolean;
  avatarUrl?: string | null;
  /**
   * Avatar hash
   * @maxLength 64
   */
  avatarHash?: string | null;
  /** Personal submission records */
  submissions?: Submission[];
  /** Participated teams */
  teams?: Team[];
};

export interface Team {
  /** @format int32 */
  id?: number;
  /**
   * Team name
   * @minLength 1
   * @maxLength 20
   */
  name: string;
  /**
   * Team bio
   * @maxLength 72
   */
  bio?: string | null;
  /**
   * Avatar hash
   * @maxLength 64
   */
  avatarHash?: string | null;
  /** Is the team locked */
  locked?: boolean;
  /**
   * Invite token
   * @maxLength 32
   */
  inviteToken?: string;
  /** Invitation code */
  inviteCode?: string;
  avatarUrl?: string | null;
  /**
   * Captain user ID
   * @format guid
   */
  captainId?: string;
  /** Captain */
  captain?: UserInfo | null;
  /** Participation objects */
  participations?: Participation[];
  /** Games */
  games?: Game[] | null;
  /** Members */
  members?: UserInfo[];
}

export interface Game {
  /** @format int32 */
  id: number;
  /**
   * Game title
   * @minLength 1
   */
  title: string;
  /**
   * Token signature public key
   * @minLength 1
   * @maxLength 63
   */
  publicKey: string;
  /**
   * Token signature private key
   * @minLength 1
   * @maxLength 63
   */
  privateKey: string;
  /** Whether to hide */
  hidden: boolean;
  /** Whether the game is in practice mode (most operations can still be performed after the game ends) */
  practiceMode?: boolean;
  /** Whether the game is for internal demo/testing usage */
  isTest?: boolean;
  /**
   * Poster hash
   * @maxLength 64
   */
  posterHash?: string | null;
  /** Game description */
  summary?: string;
  /** Detailed introduction of the game */
  content?: string;
  /** Game type (Jeopardy, AWD, Theory, Mixed) */
  gameType?: GameType;
  /** Teams can join without review */
  acceptWithoutReview?: boolean;
  /** Whether writeup is required */
  writeupRequired?: boolean;
  /**
   * Game invitation code
   * @maxLength 32
   */
  inviteCode?: string | null;
  /**
   * Limit on the number of team members, 0 means no limit
   * @format int32
   */
  teamMemberCountLimit?: number;
  /**
   * Limit on the number of containers a team can have simultaneously
   * @format int32
   */
  containerCountLimit?: number;
  /**
   * Start time
   * @format uint64
   * @minLength 1
   */
  start: number;
  /**
   * End time
   * @format uint64
   * @minLength 1
   */
  end: number;
  /**
   * Writeup submission deadline
   * @format uint64
   * @minLength 1
   */
  writeupDeadline: number;
  /**
   * Additional notes for writeup
   * @minLength 1
   */
  writeupNote: string;
  /** Blood bonus */
  bloodBonus: BloodBonus;
  /** Poster URL */
  posterUrl?: string | null;
  /** Team hash salt */
  teamHashSalt?: string;
  /** List of divisions for the game */
  divisions?: Division[] | null;
}

/** Blood bonus */
export interface BloodBonus {
  /** @format int64 */
  val?: number;
  /** @format int64 */
  firstBlood?: number;
  /** @format float */
  firstBloodFactor?: number;
  /** @format int64 */
  secondBlood?: number;
  /** @format float */
  secondBloodFactor?: number;
  /** @format int64 */
  thirdBlood?: number;
  /** @format float */
  thirdBloodFactor?: number;
  noBonus?: boolean;
}

export interface IdentityUserOfGuid {
  /** @format guid */
  id?: string;
  userName?: string | null;
  normalizedUserName?: string | null;
  email?: string | null;
  normalizedEmail?: string | null;
  emailConfirmed?: boolean;
  passwordHash?: string | null;
  securityStamp?: string | null;
  concurrencyStamp?: string | null;
  phoneNumber?: string | null;
  phoneNumberConfirmed?: boolean;
  twoFactorEnabled?: boolean;
  /** @format uint64 */
  lockoutEnd?: number | null;
  lockoutEnabled?: boolean;
  /** @format int32 */
  accessFailedCount?: number;
}

/**
 * Represents the first successful solve of a challenge for a specific participation.
 * This table acts as the immutable fact source for scoreboard and scoring related logic.
 */
export interface FirstSolve {
  /**
   * Participation ID.
   * @format int32
   */
  participationId: number;
  /**
   * Challenge ID.
   * @format int32
   */
  challengeId: number;
  /**
   * Submission ID that produced this solve.
   * @format int32
   */
  submissionId: number;
  /**
   * Flag ID.
   * @format int32
   */
  flagId?: number | null;
  /** Flag context. */
  flagContext?: FlagContext | null;
  /** Participation information */
  participation?: Participation;
  challenge?: GameChallenge;
  submission?: Submission;
}

export interface Instance {
  /** Whether the challenge is loaded */
  isLoaded?: boolean;
  /**
   * Last container operation time to ensure operations are not too frequent
   * @format uint64
   */
  lastContainerOperation?: number;
  isContainerOperationTooFrequent?: boolean;
  /** @format int32 */
  flagId?: number | null;
  /** Flag context object */
  flagContext?: FlagContext | null;
  /** @format guid */
  containerId?: string | null;
  /** Container object */
  container?: Container | null;
}

export interface Container {
  /**
   * Container GUID
   * @format guid
   */
  id?: string;
  /**
   * The Image used to create the container
   * @minLength 1
   */
  image: string;
  /**
   * Container ID
   * @minLength 1
   */
  containerId: string;
  /** Container status */
  status: ContainerStatus;
  /**
   * Container creation time
   * @format uint64
   * @minLength 1
   */
  startedAt: number;
  /**
   * Expected container stop time
   * @format uint64
   * @minLength 1
   */
  expectStopAt: number;
  /** Whether the container has a reverse proxy */
  isProxy: boolean;
  /**
   * Local IP
   * @minLength 1
   */
  ip: string;
  /**
   * Local port
   * @format int32
   */
  port: number;
  /** Public IP */
  publicIP?: string | null;
  /**
   * Public port
   * @format int32
   */
  publicPort?: number | null;
  /** Container instance access method */
  entry?: string;
  /** Whether traffic capture is enabled */
  enableTrafficCapture?: boolean;
  /** Shortened container GUID for logging purposes */
  shortId?: string;
  /** The container ID for logging purposes */
  logId?: string;
  /** @format guid */
  nodeId?: string | null;
  node?: WorkerNode | null;
  /** Game challenge instance object */
  gameInstance?: GameInstance | null;
  /**
   * Game challenge instance object ID
   * @format int32
   */
  gameInstanceId?: number | null;
}

export interface WorkerNode {
  /** @format guid */
  id?: string;
  /**
   * @minLength 1
   * @maxLength 128
   */
  name: string;
  /**
   * @minLength 1
   * @maxLength 256
   */
  hostAddress: string;
  /**
   * @minLength 1
   * @maxLength 128
   */
  authToken: string;
  capabilities?: NodeCapability;
  status?: NodeStatus;
  /** @format float */
  cpuLoad?: number;
  /** @format float */
  memoryLoad?: number;
  /** @format int32 */
  currentContainers?: number;
  /** @format int32 */
  reservedContainers?: number;
  /** @format int32 */
  maxContainers?: number;
  /** @format int32 */
  currentVms?: number;
  /** @format int32 */
  reservedVms?: number;
  /** @format int32 */
  maxVms?: number;
  /** @format int32 */
  usedPorts?: number;
  /** @format int32 */
  totalPorts?: number;
  /** @format uint64 */
  registeredAt?: number;
  /** @format uint64 */
  lastHeartbeat?: number | null;
  /** @maxLength 512 */
  labels?: string | null;
  isSchedulable?: boolean;
  isLocal?: boolean;
  isStorageNode?: boolean;
  /** @format int32 */
  agentPort?: number;
  /** @format int32 */
  registryPort?: number;
  teamLabNetworkEnabled?: boolean;
  teamLabTunnelStatus?: TeamLabTunnelStatus;
  /** @maxLength 64 */
  teamLabTunnelIp?: string | null;
  /** @format uint64 */
  teamLabTunnelLastHandshake?: number | null;
  /** @maxLength 1024 */
  teamLabTunnelLastError?: string | null;
  /** @format int32 */
  teamLabTunnelConfigVersion?: number;
  /** @maxLength 64 */
  teamLabAgentVersion?: string | null;
  /** @format int32 */
  teamLabProtocolVersion?: number;
  /** @maxLength 64 */
  teamLabFabricIp?: string | null;
  teamLabFabricStatus?: TeamLabFabricStatus;
  /** @maxLength 4096 */
  teamLabCapabilitiesJson?: string;
  concurrencyToken?: number;
  /** @format int32 */
  allocatedContainers?: number;
  /** @format int32 */
  allocatedVms?: number;
}

export interface Challenge {
  /** @format int32 */
  id: number;
  /**
   * Challenge title
   * @minLength 1
   */
  title: string;
  /**
   * Challenge content
   * @minLength 1
   */
  content: string;
  /** Challenge category */
  category: ChallengeCategory;
  /** Challenge type, cannot be changed after creation */
  type: ChallengeType;
  /** Challenge hints */
  hints?: string[] | null;
  /** Whether the challenge is enabled */
  isEnabled?: boolean;
  /**
   * The deadline of the challenge, null means no deadline
   * @format uint64
   */
  deadlineUtc?: number | null;
  /**
   * Maximum number of submissions allowed per team (0 = no limit)
   * @format int32
   */
  submissionLimit: number;
  /** Image name and tag */
  containerImage?: string | null;
  /**
   * Memory limit (MB)
   * @format int32
   */
  memoryLimit?: number | null;
  /**
   * Storage limit (MB)
   * @format int32
   */
  storageLimit?: number | null;
  /**
   * CPU limit (0.1 CPUs)
   * @format int32
   */
  cpuCount?: number | null;
  /**
   * Container exposed port
   * @format int32
   */
  exposePort?: number | null;
  /** Container network mode */
  networkMode?: NetworkMode | null;
  /** Download file name, used only for dynamic attachment unified file name */
  fileName?: string | null;
  /** OS type hint for the target environment (Windows or Linux) */
  osType?: string | null;
  /**
   * Flag template, used to generate flags based on token and challenge, game information
   * @maxLength 120
   */
  flagTemplate?: string | null;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  /** @format int32 */
  imageTemplateId?: number | null;
  imageTemplate?: ImageTemplate | null;
  /**
   * Challenge attachment ID
   * @format int32
   */
  attachmentId?: number | null;
  /** Challenge attachment (dynamic attachments are stored in FlagContext) */
  attachment?: Attachment | null;
  /**
   * Test container ID
   * @format guid
   */
  testContainerId?: string | null;
  /** Test container */
  testContainer?: Container | null;
  /** List of flags for the challenge */
  flags?: FlagContext[];
}

export interface ImageTemplate {
  /** @format int32 */
  id: number;
  /**
   * Image template name
   * @minLength 1
   * @maxLength 256
   */
  name: string;
  /** Operating system type */
  osType: OSType;
  /** Image format type */
  imageType: ImageType;
  /**
   * Registry URL for pulling the image
   * @maxLength 512
   */
  registryUrl?: string | null;
  /**
   * Registry authentication token or credentials
   * @maxLength 512
   */
  registryAuth?: string | null;
  /**
   * Local file path for imported images
   * @maxLength 512
   */
  localFilePath?: string | null;
  /**
   * File size in bytes
   * @format int64
   */
  fileSize?: number;
  /**
   * Upload timestamp
   * @format uint64
   */
  uploadedAt?: number;
  /** Current status of the image */
  status: ImageStatus;
  /**
   * Optional description of the image template
   * @maxLength 1024
   */
  description?: string | null;
  /**
   * Last import, pull, or distribution error for operator diagnosis
   * @maxLength 1024
   */
  errorMessage?: string | null;
  /** Whether this image is classified as containing known malware */
  containsMalware?: boolean;
  /**
   * SHA256 hash of the image file
   * @maxLength 64
   */
  imageHash?: string | null;
  /**
   * Original archive file name from upload
   * @maxLength 256
   */
  originalArchiveName?: string | null;
  /**
   * Owning training course. Null means global template.
   * @format int32
   */
  trainingCourseId?: number | null;
}

export type ExerciseChallenge = Challenge & {
  /** Credits for the exercise challenge */
  credit?: boolean;
  /** Difficulty of the exercise challenge, used for tags, sorting, etc. */
  difficulty?: Difficulty;
  /** Additional tags for the exercise challenge */
  tags?: string[] | null;
  /**
   * Owning training course. Null means global exercise challenge.
   * @format int32
   */
  trainingCourseId?: number | null;
  /** Dependent exercise challenges */
  dependencies?: ExerciseChallenge[];
};

/** Cheat behavior information */
export interface CheatInfoModel {
  /** Team owning the flag */
  ownedTeam?: ParticipationModel;
  /** Team submitting the flag */
  submitTeam?: ParticipationModel;
  /** Submission corresponding to this cheating behavior */
  submission?: Submission;
}

/** Team participation information */
export interface ParticipationModel {
  /**
   * Participation ID
   * @format int32
   */
  id?: number;
  /** Team information */
  team?: TeamModel;
  /** Team participation status */
  status?: ParticipationStatus;
  /** Team division */
  division?: string | null;
  /**
   * Team division ID
   * @format int32
   */
  divisionId?: number | null;
}

export interface ChallengeTrafficModel {
  /**
   * Challenge ID
   * @format int32
   */
  id?: number;
  /**
   * Challenge title
   * @minLength 1
   */
  title: string;
  /** Challenge category */
  category?: ChallengeCategory;
  /** Challenge type */
  type?: ChallengeType;
  /** Is the challenge enabled */
  isEnabled?: boolean;
  /**
   * Number of team traffic captured by the challenge
   * @format int32
   */
  count?: number;
}

/** Team traffic information */
export interface TeamTrafficModel {
  /**
   * Participation ID
   * @format int32
   */
  id?: number;
  /**
   * Team Id
   * @format int32
   */
  teamId?: number;
  /** Team name */
  name?: string | null;
  /** Division of participation */
  division?: string | null;
  /** Avatar URL */
  avatar?: string | null;
  /**
   * Number of traffic captured by the challenge
   * @format int32
   */
  count?: number;
}

/** File record */
export interface FileRecord {
  /** File name */
  fileName?: string;
  /**
   * File size
   * @format int64
   */
  size?: number;
  /**
   * File modification date
   * @format uint64
   */
  updateTime?: number;
}

export interface GameDetailModel {
  /** Challenge information */
  challenges?: Record<string, ChallengeInfo[]>;
  /**
   * Number of challenges
   * @format int32
   */
  challengeCount?: number;
  /** Scoreboard information */
  rank?: ScoreboardItem | null;
  /**
   * Team token
   * @minLength 1
   */
  teamToken: string;
  /** Whether writeup submission is required */
  writeupRequired: boolean;
  /**
   * Writeup submission deadline
   * @format uint64
   * @minLength 1
   */
  writeupDeadline: number;
}

/** Participation for review (Admin) */
export interface ParticipationInfoModel {
  /**
   * Participation ID
   * @format int32
   */
  id: number;
  /** Participating team */
  team: TeamWithDetailedUserInfo;
  /** Registered members */
  registeredMembers: string[];
  /**
   * Division of the game
   * @format int32
   */
  divisionId?: number | null;
  /** Participation status */
  status: ParticipationStatus;
}

/** Detailed team information for review (Admin) */
export interface TeamWithDetailedUserInfo {
  /**
   * Team ID
   * @format int32
   */
  id?: number;
  /** Is locked */
  locked?: boolean;
  /**
   * Captain ID
   * @format guid
   */
  captainId?: string;
  /** Team name */
  name?: string | null;
  /** Team bio */
  bio?: string | null;
  /** Avatar URL */
  avatar?: string | null;
  /** Team members */
  members?: ProfileUserInfoModel[];
}

export interface ChallengeDetailModel {
  /** @format int32 */
  id?: number;
  title?: string;
  content?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  hints?: string[] | null;
  /** @format int32 */
  score?: number;
  type?: ChallengeType;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  context?: ClientFlagContext;
  /** @format int32 */
  limit?: number;
  /** @format int32 */
  attempts?: number;
  /** @format uint64 */
  deadline?: number | null;
  flags?: FlagStepInfo[] | null;
}

export interface ClientFlagContext {
  /**
   * Close time of the challenge instance
   * @format uint64
   */
  closeTime?: number | null;
  /** Connection method of the challenge instance */
  instanceEntry?: string | null;
  /** Attachment URL */
  url?: string | null;
  /**
   * Attachment file size
   * @format int64
   */
  fileSize?: number | null;
}

/**
 * Multi-flag step metadata — exposed to players for guided solving.
 * Does NOT contain the actual flag values.
 */
export interface FlagStepInfo {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  orderIndex?: number;
  description?: string | null;
}

/** Flag submission */
export interface FlagSubmitModel {
  /**
   * Flag content
   * @minLength 1
   */
  flag: string;
  /**
   * Specific Flag ID being submitted against (multi-flag challenges)
   * @format int32
   */
  flagId?: number | null;
}

/** Flag submission result */
export interface FlagSubmitResultModel {
  /** @format int32 */
  id: number;
  status: AnswerResult;
  bloodType: SubmissionType;
}

/** Game writeup submission information */
export interface BasicWriteupInfoModel {
  /** Whether it has been submitted */
  submitted?: boolean;
  /** File name */
  name?: string;
  /**
   * File size
   * @format int64
   */
  fileSize?: number;
  /** Writeup additional notes */
  note?: string;
}

/** Response model for VM instance status queries. */
export interface VmStatusResponse {
  /**
   * VM instance ID
   * @format guid
   */
  vmInstanceId?: string;
  /** Current status: Creating, Running, Stopped, Destroyed, Error */
  status?: string;
  /** Current deployment stage: image-pending, image-pulling, vm-creating, vm-booting, ready, error */
  stage?: string | null;
  /** Human-readable deployment stage label */
  stageMessage?: string | null;
  /** Deployment queue status when the VM is waiting or being created */
  queue?: DeploymentQueueStatusModel | null;
  /** VM IP address (null if not yet assigned) */
  ipAddress?: string | null;
  /** Guacamole RDP URL (null if not yet ready) */
  rdpUrl?: string | null;
  /**
   * When the VM was created
   * @format uint64
   */
  createdAt?: number;
}

export interface DeploymentQueueStatusModel {
  /** @format guid */
  ticketId?: string;
  kind?: DeploymentQueueKind;
  status?: DeploymentQueueTicketStatus;
  /** @format guid */
  targetNodeId?: string | null;
  targetNodeName?: string | null;
  /** @format int32 */
  queuePosition?: number;
  /** @format int32 */
  peopleAhead?: number;
  errorMessage?: string | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  startedAt?: number | null;
  /** @format uint64 */
  completedAt?: number | null;
}

export interface GamePhase {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  gameId?: number;
  /**
   * @minLength 1
   * @maxLength 256
   */
  name: string;
  /** @format uint64 */
  startTime?: number;
  /** @format uint64 */
  endTime?: number;
  ctfEnabled?: boolean;
  /** @maxLength 2048 */
  securityPolicy?: string | null;
  game?: Game | null;
}

export interface LocalImportRequest {
  /** @minLength 1 */
  localPath: string;
  displayName?: string | null;
}

export interface DockerRegisterRequest {
  /**
   * @minLength 1
   * @maxLength 256
   */
  name: string;
  /**
   * @minLength 1
   * @maxLength 512
   */
  registryUrl: string;
  osType?: OSType;
  /** @maxLength 512 */
  registryAuth?: string | null;
}

/** Post information */
export interface PostInfoModel {
  /**
   * Post ID
   * @minLength 1
   */
  id: string;
  /**
   * Post title
   * @minLength 1
   */
  title: string;
  /**
   * Post summary
   * @minLength 1
   */
  summary: string;
  /** Is pinned */
  isPinned: boolean;
  /** Post tags */
  tags?: string[] | null;
  /** Author avatar */
  authorAvatar?: string | null;
  /** Author name */
  authorName?: string | null;
  /**
   * Update time
   * @format uint64
   * @minLength 1
   */
  time: number;
}

/** Client configuration */
export interface ClientConfig {
  /** Platform prefix name */
  title?: string;
  /** Platform slogan */
  slogan?: string;
  /** Site description information */
  description?: string | null;
  /** Footer information */
  footerInfo?: string | null;
  /** Custom theme color */
  customTheme?: string | null;
  /** The public key used for API requests */
  apiPublicKey?: string | null;
  /** Platform logo URL */
  logoUrl?: string | null;
  /** Container port mapping type */
  portMapping?: ContainerPortMappingType;
  /**
   * Default container lifetime in minutes
   * @format int32
   */
  defaultLifetime?: number;
  /**
   * Extension duration for each renewal in minutes
   * @format int32
   */
  extensionDuration?: number;
  /**
   * Renewal window before container stops in minutes
   * @format int32
   */
  renewalWindow?: number;
}

/** Client CAPTCHA information */
export interface ClientCaptchaInfoModel {
  /** Captcha Provider Type */
  type?: CaptchaProvider;
  /** Site Key */
  siteKey?: string;
}

/** Hash Pow verification */
export interface HashPowChallenge {
  /** Challenge ID */
  id?: string;
  /** Verification challenge */
  challenge?: string;
  /**
   * Difficulty coefficient
   * @format int32
   */
  difficulty?: number;
}

export interface NodeDeployRequest {
  /** @minLength 1 */
  hostAddress: string;
  /** @minLength 1 */
  username: string;
  /** @minLength 1 */
  password: string;
  nodeName?: string | null;
}

export interface UpdateNodeRequest {
  isSchedulable?: boolean | null;
  /** @format int32 */
  maxContainers?: number | null;
  /** @format int32 */
  maxVms?: number | null;
  isStorageNode?: boolean | null;
  /** @format int32 */
  registryPort?: number | null;
}

export interface EnableTeamLabNetworkRequest {
  dryRun?: boolean;
  tunnelIp?: string | null;
}

export interface HeartbeatRequest {
  /** @format float */
  cpuLoad?: number;
  /** @format float */
  memoryLoad?: number;
  /** @format int32 */
  currentContainers?: number;
  /** @format int32 */
  currentVms?: number;
  /** @format int32 */
  usedPorts?: number;
  agentVersion?: string | null;
  /** @format int32 */
  teamLabProtocolVersion?: number | null;
  teamLabFabricIp?: string | null;
  teamLabFabricStatus?: TeamLabFabricStatus | null;
  teamLabCapabilities?: TeamLabNodeCapabilityReport | null;
}

export interface TeamLabNodeCapabilityReport {
  docker?: boolean;
  kvm?: boolean;
  kvmDevice?: boolean;
  cpuVirtualization?: boolean;
  wireGuard?: boolean;
  iptables?: boolean;
  nftables?: boolean;
  tcpdump?: boolean;
  dumpcap?: boolean;
}

export interface PenetrationConfigModel {
  /** @format int32 */
  gameId?: number;
  baseCidr?: string;
  /** @format int32 */
  teamSubnetPrefix?: number;
  /** @format int32 */
  networkSubnetPrefix?: number;
  /** @format int32 */
  maxResetCount?: number;
  /** @format int32 */
  publishedVersion?: number;
  status?: PenetrationDeploymentStatus;
  networks?: PenetrationNetworkModel[];
  nodes?: PenetrationNodeModel[];
  interfaces?: PenetrationInterfaceModel[];
  edges?: PenetrationEdgeModel[];
}

export interface PenetrationNetworkModel {
  /** @format int32 */
  id?: number;
  topologyKey?: string;
  /** @minLength 1 */
  name: string;
  slug?: string;
  cidr?: string | null;
  zoneType?: PenetrationZoneType;
  /** @format int32 */
  trustLevel?: number;
  description?: string | null;
  defaultPolicy?: PenetrationDefaultPolicy;
  /** @format int32 */
  orderIndex?: number;
  isEntry?: boolean;
  /** @format double */
  positionX?: number;
  /** @format double */
  positionY?: number;
  /** @format double */
  width?: number;
  /** @format double */
  height?: number;
  collapsed?: boolean;
  previewCidr?: string | null;
}

export interface PenetrationNodeModel {
  /** @format int32 */
  id?: number;
  topologyKey?: string;
  /** @format int32 */
  networkId?: number;
  /** @minLength 1 */
  name: string;
  description?: string | null;
  playerAlias?: string | null;
  playerDescription?: string | null;
  nodeType?: PenetrationNodeType;
  /** @format int32 */
  imageTemplateId?: number | null;
  imageName?: string | null;
  /** @format int32 */
  cpuCount?: number;
  /** @format int32 */
  memoryLimit?: number;
  /** @format int32 */
  storageLimit?: number;
  /** @format int32 */
  exposePort?: number;
  isEntry?: boolean;
  publishPort?: boolean;
  allowRouting?: boolean;
  staticIp?: string | null;
  environmentVariables?: Record<string, string>;
  startCommand?: string | null;
  healthCheck?: string | null;
  reservedAdRole?: string | null;
  /** @format double */
  positionX?: number;
  /** @format double */
  positionY?: number;
  /** @format int32 */
  orderIndex?: number;
  previewIp?: string | null;
  interfaces?: PenetrationInterfaceModel[];
  scoreItems?: PenetrationScoreItemModel[];
}

export interface PenetrationInterfaceModel {
  /** @format int32 */
  id?: number;
  topologyKey?: string;
  /** @format int32 */
  nodeId?: number;
  /** @format int32 */
  networkId?: number;
  name?: string;
  staticIp?: string | null;
  previewIp?: string | null;
  isPrimary?: boolean;
  isManagement?: boolean;
  /** @format int32 */
  orderIndex?: number;
}

export interface PenetrationScoreItemModel {
  /** @format int32 */
  id?: number;
  topologyKey?: string;
  /** @minLength 1 */
  title: string;
  description?: string | null;
  category?: string;
  /** @format int32 */
  score?: number;
  isDynamic?: boolean;
  staticFlag?: string | null;
  flagTemplate?: string | null;
  /** @format int32 */
  maxAttempts?: number;
  isVisible?: boolean;
  isCheckpoint?: boolean;
  prerequisiteItemIds?: number[];
  /** @format int32 */
  orderIndex?: number;
}

export interface PenetrationEdgeModel {
  /** @format int32 */
  id?: number;
  topologyKey?: string;
  /** @format int32 */
  sourceNodeId?: number;
  /** @format int32 */
  targetNodeId?: number;
  sourceKind?: PenetrationPolicyScope;
  /** @format int32 */
  sourceId?: number;
  targetKind?: PenetrationPolicyScope;
  /** @format int32 */
  targetId?: number;
  protocol?: PenetrationProtocol;
  portRange?: string;
  policyAction?: PenetrationPolicyAction;
  isRouteHint?: boolean;
  enforcementMode?: PenetrationEnforcementMode;
  /** @format int32 */
  priority?: number;
  label?: string | null;
  description?: string | null;
}

export interface PenetrationValidationModel {
  valid?: boolean;
  errors?: string[];
  warnings?: string[];
}

export interface PenetrationPlanModel {
  /** @format int32 */
  gameId?: number;
  /** @format int32 */
  teamCount?: number;
  sampleTeamPrefix?: string;
  validation?: PenetrationValidationModel;
  networks?: PenetrationPlanNetworkModel[];
  nodes?: PenetrationPlanNodeModel[];
  policies?: PenetrationPlanPolicyModel[];
  flags?: PenetrationPlanFlagModel[];
  deploymentSteps?: string[];
}

export interface PenetrationPlanNetworkModel {
  /** @format int32 */
  networkId?: number;
  networkName?: string;
  slug?: string;
  zoneType?: PenetrationZoneType;
  cidr?: string;
  defaultPolicy?: PenetrationDefaultPolicy;
  isInternal?: boolean;
}

export interface PenetrationPlanNodeModel {
  /** @format int32 */
  nodeId?: number;
  nodeName?: string;
  nodeType?: PenetrationNodeType;
  image?: string;
  publishPort?: boolean;
  /** @format int32 */
  exposePort?: number;
  interfaces?: PenetrationPlanInterfaceModel[];
  adminAccessHint?: string | null;
}

export interface PenetrationPlanInterfaceModel {
  /** @format int32 */
  interfaceId?: number;
  name?: string;
  /** @format int32 */
  networkId?: number;
  networkName?: string;
  networkSlug?: string;
  cidr?: string;
  ipAddress?: string;
  isPrimary?: boolean;
  isManagement?: boolean;
  isInternal?: boolean;
}

export interface PenetrationPlanPolicyModel {
  /** @format int32 */
  policyId?: number;
  label?: string;
  source?: string;
  target?: string;
  protocol?: PenetrationProtocol;
  portRange?: string;
  action?: PenetrationPolicyAction;
  isRouteHint?: boolean;
  enforcementMode?: PenetrationEnforcementMode;
  routeStatus?: PenetrationRouteStatus;
  runtimeSummary?: string;
  routeNodeName?: string | null;
  sourceNetworkName?: string | null;
  targetNetworkName?: string | null;
  gatewayIp?: string | null;
  compileMessage?: string | null;
  isExecutable?: boolean;
}

export interface PenetrationPlanFlagModel {
  /** @format int32 */
  scoreItemId?: number;
  /** @format int32 */
  nodeId?: number;
  nodeName?: string;
  title?: string;
  category?: string;
  /** @format int32 */
  score?: number;
  isDynamic?: boolean;
  preview?: string;
}

export interface PenetrationAdminAccessModel {
  /** @format int32 */
  runtimeNodeId?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  /** @format int32 */
  nodeId?: number;
  nodeName?: string;
  status?: PenetrationRuntimeStatus;
  workerNodeName?: string;
  containerId?: string;
  internalIp?: string;
  interfaceSummary?: string;
  host?: string | null;
  /** @format int32 */
  publicPort?: number | null;
  url?: string | null;
  /** @format int32 */
  exposePort?: number;
}

export interface PenetrationScoreboardItemModel {
  /** @format int32 */
  rank?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  /** @format int32 */
  score?: number;
  /** @format int32 */
  solvedCount?: number;
  /** @format uint64 */
  lastSubmissionTime?: number;
}

export interface PenetrationTeamEnvironmentModel {
  /** @format int32 */
  environmentId?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  /** @format guid */
  workerNodeId?: string | null;
  workerNodeName?: string | null;
  networkPrefix?: string;
  /** @format int32 */
  teamIndex?: number;
  /** @format int32 */
  publishedVersion?: number;
  status?: PenetrationRuntimeStatus;
  /** @format int32 */
  resetCount?: number;
  /** @format int32 */
  runtimeNodeCount?: number;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number | null;
  lastError?: string | null;
  /** @format int32 */
  cleanupRetryCount?: number;
  /** @format uint64 */
  nextCleanupAt?: number | null;
  /** @format uint64 */
  lastCleanupAttemptAt?: number | null;
  events?: PenetrationDeploymentEventModel[];
  runtimeNodes?: PenetrationRuntimeNodeModel[];
  runtimeRoutes?: PenetrationRuntimeRouteModel[];
  teamLabShards?: TeamLabRuntimeShardSummaryModel[];
  teamLabNetworks?: TeamLabRuntimeNetworkSummaryModel[];
  teamLabAssets?: TeamLabRuntimeAssetSummaryModel[];
  teamLabCaptureJobs?: TeamLabTrafficCaptureJobSummaryModel[];
  teamLabTrafficFlows?: TeamLabTrafficFlowSummaryModel[];
}

export interface PenetrationDeploymentEventModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  environmentId?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  stage?: string;
  level?: PenetrationDeploymentEventLevel;
  message?: string;
  nodeName?: string | null;
  detail?: string | null;
  /** @format guid */
  userId?: string | null;
  /** @format uint64 */
  createdAt?: number;
}

export interface PenetrationRuntimeNodeModel {
  /** @format int32 */
  runtimeNodeId?: number;
  /** @format int32 */
  topologyNodeId?: number;
  topologyNodeKey?: string;
  nodeName?: string;
  networkName?: string;
  ipAddress?: string;
  adminAccessUrl?: string | null;
  /** @format int32 */
  publicPort?: number | null;
  status?: PenetrationRuntimeStatus;
  /** @format uint64 */
  createdAt?: number;
  /** @format guid */
  containerGuid?: string | null;
  containerId?: string | null;
  containerStatus?: ContainerStatus | null;
  image?: string | null;
  publicHost?: string | null;
  interfaceSummary?: string;
}

export interface PenetrationRuntimeRouteModel {
  /** @format int32 */
  id?: number;
  edgeTopologyKey?: string;
  label?: string;
  enforcementMode?: PenetrationEnforcementMode;
  status?: PenetrationRouteStatus;
  routeNodeKey?: string | null;
  routeNodeName?: string | null;
  sourceNetworkName?: string | null;
  targetNetworkName?: string | null;
  sourceCidr?: string | null;
  targetCidr?: string | null;
  gatewayIp?: string | null;
  commandSummary?: string | null;
  message?: string | null;
  isExecutable?: boolean;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  appliedAt?: number | null;
}

export interface TeamLabRuntimeShardSummaryModel {
  /** @format int32 */
  id?: number;
  /** @format guid */
  workerNodeId?: string;
  workerNodeName?: string;
  status?: TeamLabRuntimeStatus;
  /** @format int32 */
  routeVersion?: number;
  networkKeys?: string[];
  assetKeys?: string[];
  lastError?: string | null;
}

export interface TeamLabRuntimeNetworkSummaryModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  shardId?: number | null;
  /** @format guid */
  workerNodeId?: string | null;
  workerNodeName?: string;
  topologyKey?: string;
  name?: string;
  cidr?: string;
  gatewayIp?: string;
  bridgeName?: string;
}

export interface TeamLabRuntimeAssetSummaryModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  shardId?: number | null;
  /** @format guid */
  workerNodeId?: string | null;
  workerNodeName?: string;
  kind?: TeamLabResourceKind;
  topologyKey?: string;
  name?: string;
  runtimeResourceId?: string | null;
  /** @format int32 */
  sourceTemplateId?: number | null;
  image?: string | null;
  networkKey?: string | null;
  ipAddress?: string | null;
  macAddress?: string | null;
  interfaceSummaryJson?: string;
  status?: TeamLabRuntimeStatus;
  lastError?: string | null;
}

export interface TeamLabTrafficCaptureJobSummaryModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  runtimeId?: number;
  /** @format int32 */
  shardId?: number | null;
  /** @format int32 */
  networkId?: number | null;
  /** @format guid */
  workerNodeId?: string | null;
  workerNodeName?: string;
  status?: TeamLabTrafficCaptureStatus;
  scope?: string;
  filePath?: string | null;
  /** @format int64 */
  maxBytes?: number;
  /** @format int32 */
  maxSeconds?: number;
  /** @format int64 */
  capturedBytes?: number;
  lastError?: string | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  startedAt?: number | null;
  /** @format uint64 */
  completedAt?: number | null;
  /** @format uint64 */
  expiresAt?: number | null;
}

export interface TeamLabTrafficFlowSummaryModel {
  /** @format int32 */
  shardId?: number | null;
  /** @format int32 */
  networkId?: number | null;
  /** @format guid */
  workerNodeId?: string | null;
  workerNodeName?: string;
  networkName?: string;
  sourceIp?: string;
  /** @format int32 */
  sourcePort?: number | null;
  destinationIp?: string;
  /** @format int32 */
  destinationPort?: number | null;
  protocol?: string;
  /** @format int64 */
  bytes?: number;
  /** @format uint64 */
  capturedAt?: number;
}

/** List response */
export interface ArrayResponseOfPenetrationDeploymentEventModel {
  /** Data */
  data: PenetrationDeploymentEventModel[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

/** List response */
export interface ArrayResponseOfPenetrationSubmissionLogModel {
  /** Data */
  data: PenetrationSubmissionLogModel[];
  /**
   * Data length
   * @format int32
   */
  length: number;
  /**
   * Total length
   * @format int32
   */
  total?: number;
}

export interface PenetrationSubmissionLogModel {
  /** @format int32 */
  id?: number;
  /** @format uint64 */
  time?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  userName?: string;
  nodeName?: string;
  itemTitle?: string;
  category?: string;
  /** @format int32 */
  score?: number;
  /** Judgement result */
  status?: AnswerResult;
}

export interface PenetrationWorkspaceModel {
  /** @format int32 */
  gameId?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  status?: PenetrationRuntimeStatus;
  /** @format int32 */
  resetCount?: number;
  /** @format int32 */
  maxResetCount?: number;
  nodes?: PenetrationWorkspaceNodeModel[];
}

export interface PenetrationWorkspaceNodeModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  networkId?: number;
  topologyKey?: string;
  name?: string;
  description?: string | null;
  nodeType?: PenetrationNodeType;
  runtimeStatus?: PenetrationRuntimeStatus;
  scoreItems?: PenetrationWorkspaceScoreItemModel[];
}

export interface PenetrationWorkspaceScoreItemModel {
  /** @format int32 */
  id?: number;
  topologyKey?: string;
  title?: string;
  description?: string | null;
  category?: string;
  /** @format int32 */
  score?: number;
  solved?: boolean;
  /** @format int32 */
  attempts?: number;
  /** @format int32 */
  maxAttempts?: number;
  isCheckpoint?: boolean;
  prerequisiteItemIds?: number[];
  prerequisiteItemKeys?: string[];
}

export interface TeamLabClientConfigModel {
  /** @format int32 */
  gameId?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  endpoint?: string;
  clientAddress?: string;
  allowedIPs?: string;
  dns?: string;
  /** @format int32 */
  configVersion?: number;
  fileName?: string;
  configText?: string;
}

export interface PenetrationSubmitResultModel {
  accepted?: boolean;
  /** @format int32 */
  score?: number;
  message?: string;
}

export interface PenetrationSubmitModel {
  /** @format int32 */
  scoreItemId?: number;
  /** @minLength 1 */
  flag: string;
}

export interface StudentGroupBriefModel {
  /** @format int32 */
  id?: number;
  name?: string;
  description?: string;
  isArchived?: boolean;
  /** @format int32 */
  memberCount?: number;
  /** @format int32 */
  managerCount?: number;
  /** @format uint64 */
  updatedAt?: number;
}

export type StudentGroupDetailModel = StudentGroupBriefModel & {
  members?: StudentGroupMemberModel[];
  managers?: StudentGroupManagerModel[];
};

export interface StudentGroupMemberModel {
  /** @format guid */
  studentId?: string;
  userName?: string;
  realName?: string;
  stdNumber?: string;
  avatar?: string | null;
  note?: string;
  /** @format uint64 */
  joinedAt?: number;
}

export interface StudentGroupManagerModel {
  /** @format guid */
  teacherId?: string;
  userName?: string;
  realName?: string;
  /** Student group manager role */
  roleInGroup?: StudentGroupManagerRole;
}

export interface StudentGroupEditModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  name: string;
  /** @maxLength 512 */
  description?: string;
}

export interface StudentGroupMemberEditModel {
  /** @format guid */
  studentId?: string;
  /** @maxLength 256 */
  note?: string;
}

export interface StudentGroupManagerEditModel {
  /** @format guid */
  teacherId?: string;
  /** Student group manager role */
  roleInGroup?: StudentGroupManagerRole;
}

/** Team information update */
export interface TeamUpdateModel {
  /**
   * Team name
   * @maxLength 20
   */
  name?: string | null;
  /**
   * Team bio
   * @maxLength 72
   */
  bio?: string | null;
}

export interface TeamJoinRequestModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string | null;
  /** Team member information */
  user?: TeamUserInfoModel;
  message?: string | null;
  status?: TeamJoinRequestStatus;
  /** @format uint64 */
  createdAtUtc?: number;
  /** @format uint64 */
  reviewedAtUtc?: number | null;
}

export interface TeamJoinRequestCreateModel {
  /** @maxLength 128 */
  message?: string | null;
}

export interface TeamJoinRequestReviewModel {
  accepted?: boolean;
}

export interface TeamTransferModel {
  /**
   * New captain ID
   * @format guid
   * @minLength 1
   */
  newCaptainId: string;
}

/** Signature verification */
export interface SignatureVerifyModel {
  /**
   * Team token
   * @minLength 1
   */
  teamToken: string;
  /**
   * Game public key, Base64 encoded
   * @minLength 1
   */
  publicKey: string;
}

export interface TeamLabCaptureStartModel {
  networkTopologyKey?: string | null;
  /** @format int32 */
  shardId?: number | null;
  /** @format int32 */
  maxSeconds?: number;
  /** @format int64 */
  maxBytes?: number;
  /** @format int32 */
  retentionSeconds?: number;
}

export type TheoryQuestionBankItemModel = TheoryQuestionEditModel & {
  /** @format int32 */
  id?: number;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
};

export interface TheoryQuestionEditModel {
  /** Theory exam question type */
  type: TheoryQuestionType;
  bankName?: string;
  /** @minLength 1 */
  title: string;
  content?: string;
  options?: string[];
  answerIndexes?: number[];
}

export type TheoryPaperDetailModel = TheoryPaperEditModel & {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  gameId?: number;
  isPublished?: boolean;
  /** @format uint64 */
  publishedAt?: number | null;
  /** @format uint64 */
  updatedAt?: number;
  /** @format int32 */
  totalScore?: number;
};

export interface TheoryPaperEditModel {
  /** @minLength 1 */
  title: string;
  description?: string;
  questions?: TheoryPaperQuestionEditModel[];
}

export type TheoryPaperQuestionEditModel = TheoryQuestionEditModel & {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  sourceQuestionId?: number | null;
  /**
   * @format int32
   * @min 1
   * @max 2147483647
   */
  score?: number;
  /** @format int32 */
  order?: number;
};

export interface TheoryResultsModel {
  submissions?: TheoryAnswerSheetSummaryModel[];
  scoreboard?: TheoryScoreboardItemModel[];
}

export interface TheoryAnswerSheetSummaryModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  participationId?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  /** @format guid */
  userId?: string;
  userName?: string;
  /** Theory answer sheet status */
  status?: TheoryAnswerSheetStatus;
  /** @format int32 */
  score?: number;
  /** @format int32 */
  maxScore?: number;
  /** @format uint64 */
  updatedAt?: number;
  /** @format uint64 */
  submittedAt?: number | null;
}

export interface TheoryScoreboardItemModel {
  /** @format int32 */
  rank?: number;
  /** @format int32 */
  teamId?: number;
  teamName?: string;
  /** @format int32 */
  divisionId?: number | null;
  /** @format int32 */
  score?: number;
  /** @format int32 */
  maxScore?: number;
  userName?: string | null;
  /** @format uint64 */
  submittedAt?: number | null;
}

export interface TheoryPlayerPaperModel {
  /** @format int32 */
  paperId?: number;
  /** @format int32 */
  gameId?: number;
  title?: string;
  description?: string;
  /** @format int32 */
  totalScore?: number;
  status?: TheoryAnswerSheetStatus | null;
  /** @format int32 */
  score?: number | null;
  /** @format uint64 */
  submittedAt?: number | null;
  /** @format uint64 */
  updatedAt?: number | null;
  questions?: TheoryPlayerQuestionModel[];
  answers?: TheoryAnswerModel[];
}

export interface TheoryPlayerQuestionModel {
  /** @format int32 */
  id?: number;
  /** Theory exam question type */
  type?: TheoryQuestionType;
  title?: string;
  content?: string;
  options?: string[];
  /** @format int32 */
  score?: number;
  /** @format int32 */
  order?: number;
}

export interface TheoryAnswerModel {
  /** @format int32 */
  paperQuestionId?: number;
  selectedIndexes?: number[];
}

export interface TheoryAnswerSheetEditModel {
  answers?: TheoryAnswerModel[];
}

export interface TrainingCourseModel {
  /** @format int32 */
  id?: number;
  title?: string;
  slug?: string;
  summary?: string;
  description?: string;
  coverFileHash?: string | null;
  coverUrl?: string | null;
  tags?: string[];
  /** Training course lifecycle status */
  status?: TrainingCourseStatus;
  /** Training course enrollment policy */
  enrollmentPolicy?: TrainingCourseEnrollmentPolicy;
  enrollmentStatus?: TrainingCourseEnrollmentStatus | null;
  canLearn?: boolean;
  canEdit?: boolean;
  canManageTeachers?: boolean;
  canManageEnrollments?: boolean;
  canDelete?: boolean;
  /** @format int32 */
  chapterCount?: number;
  /** @format int32 */
  resourceCount?: number;
  /** @format int32 */
  enrollmentCount?: number;
  /** @format int32 */
  completedChapterCount?: number;
  /** @format int32 */
  totalChapterCount?: number;
  progressStatus?: TrainingCourseProgressStatus | null;
  /** @format uint64 */
  lastStudiedAt?: number | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
  teachers?: TrainingCourseTeacherModel[];
  chapters?: TrainingCourseChapterModel[];
  resources?: TrainingCourseResourceModel[];
  challenges?: TrainingCourseChallengeModel[];
}

export interface TrainingCourseTeacherModel {
  /** @format guid */
  teacherId?: string;
  userName?: string;
  realName?: string;
  /** Training course teacher role */
  role?: TrainingCourseTeacherRole;
  /** @format uint64 */
  assignedAt?: number;
}

export interface TrainingCourseChapterModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  courseId?: number;
  /** @format int32 */
  parentId?: number | null;
  title?: string;
  summary?: string;
  content?: string;
  /** Training article format */
  contentType?: TrainingArticleContentType;
  /** Training course video provider */
  videoProvider?: TrainingCourseVideoProvider;
  videoUrl?: string | null;
  videoFileUrl?: string | null;
  /** @format int32 */
  order?: number;
  isPublished?: boolean;
  completionPolicy?: TrainingChapterCompletionPolicy;
  progressStatus?: TrainingCourseProgressStatus | null;
  /** @format int32 */
  readPercent?: number;
  /** @format uint64 */
  completedAt?: number | null;
  challenges?: TrainingCourseChallengeModel[];
  theoryPaper?: TrainingCourseChapterTheorySummaryModel | null;
}

export interface TrainingChapterCompletionPolicy {
  requireContentRead?: boolean;
  requireAllRequiredChallenges?: boolean;
  /**
   * @format int32
   * @min 0
   * @max 2147483647
   */
  requiredChallengeCount?: number;
  /**
   * @format int32
   * @min 0
   * @max 100
   */
  theoryPassRate?: number;
}

export interface TrainingCourseChallengeModel {
  /** @format int32 */
  exerciseChallengeId?: number;
  /** @format int32 */
  chapterId?: number | null;
  title?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  /** @format int32 */
  order?: number;
  isRequired?: boolean;
  solved?: boolean;
  displayTitle?: string | null;
  hasAttachment?: boolean;
  attachmentFileName?: string | null;
}

export interface TrainingCourseChapterTheorySummaryModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  courseId?: number;
  /** @format int32 */
  chapterId?: number;
  title?: string;
  isPublished?: boolean;
  /** @format int32 */
  questionCount?: number;
  /** @format int32 */
  totalScore?: number;
  /** @format int32 */
  passRate?: number;
  allowRetake?: boolean;
  showCorrectAnswerAfterSubmit?: boolean;
  /** @format int32 */
  attemptNumber?: number | null;
  status?: TheoryAnswerSheetStatus | null;
  /** @format int32 */
  score?: number | null;
  passed?: boolean | null;
  /** @format uint64 */
  submittedAt?: number | null;
}

export interface TrainingCourseResourceModel {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  courseId?: number;
  title?: string;
  description?: string;
  /** Training course resource type */
  type?: TrainingCourseResourceType;
  externalUrl?: string | null;
  fileName?: string | null;
  /** @format int64 */
  fileSize?: number | null;
  downloadUrl?: string | null;
  /** @format int32 */
  order?: number;
  isVisible?: boolean;
  /** @format uint64 */
  createdAt?: number;
}

export interface TrainingCourseEditModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  title: string;
  /** @maxLength 128 */
  slug?: string;
  /** @maxLength 512 */
  summary?: string;
  description?: string;
  /** @maxLength 64 */
  coverFileHash?: string | null;
  tags?: string[];
  /** Training course enrollment policy */
  enrollmentPolicy?: TrainingCourseEnrollmentPolicy;
}

export interface TrainingCourseEnrollmentModel {
  /** @format int32 */
  courseId?: number;
  /** @format guid */
  userId?: string;
  userName?: string;
  realName?: string;
  stdNumber?: string;
  /** Training course enrollment status */
  status?: TrainingCourseEnrollmentStatus;
  applyReason?: string;
  reviewComment?: string;
  /** @format uint64 */
  requestedAt?: number;
  /** @format uint64 */
  reviewedAt?: number | null;
  /** @format int32 */
  completedChapterCount?: number;
  /** @format int32 */
  totalChapterCount?: number;
  progressStatus?: TrainingCourseProgressStatus | null;
  /** @format uint64 */
  progressUpdatedAt?: number | null;
}

export interface TrainingCourseStudentLearningSummaryModel {
  /** @format guid */
  userId?: string;
  userName?: string;
  realName?: string;
  stdNumber?: string;
  /** Training course enrollment status */
  enrollmentStatus?: TrainingCourseEnrollmentStatus;
  /** @format int32 */
  completedChapterCount?: number;
  /** @format int32 */
  totalChapterCount?: number;
  /** @format int32 */
  challengeSolvedCount?: number;
  /** @format int32 */
  challengeTotalCount?: number;
  /** @format int32 */
  theorySubmittedCount?: number;
  /** @format int32 */
  theoryPassedCount?: number;
  /** @format int32 */
  theoryTotalCount?: number;
  /** @format int32 */
  theoryScore?: number;
  /** @format int32 */
  theoryMaxScore?: number;
  progressStatus?: TrainingCourseProgressStatus | null;
  /** @format uint64 */
  lastActivityAt?: number | null;
}

export type TrainingCourseStudentLearningDetailModel =
  TrainingCourseStudentLearningSummaryModel & {
    chapters?: TrainingCourseStudentChapterLearningModel[];
  };

export interface TrainingCourseStudentChapterLearningModel {
  /** @format int32 */
  chapterId?: number;
  title?: string;
  summary?: string;
  /** @format int32 */
  order?: number;
  isPublished?: boolean;
  completionPolicy?: TrainingChapterCompletionPolicy;
  progressStatus?: TrainingCourseProgressStatus | null;
  /** @format int32 */
  readPercent?: number;
  /** @format uint64 */
  completedAt?: number | null;
  theory?: TrainingCourseStudentTheoryLearningModel | null;
  challenges?: TrainingCourseStudentChallengeLearningModel[];
}

export interface TrainingCourseStudentTheoryLearningModel {
  /** @format int32 */
  paperId?: number;
  title?: string;
  isPublished?: boolean;
  /** @format int32 */
  questionCount?: number;
  /** @format int32 */
  totalScore?: number;
  /** @format int32 */
  passRate?: number;
  status?: TheoryAnswerSheetStatus | null;
  /** @format int32 */
  score?: number | null;
  passed?: boolean | null;
  /** @format int32 */
  correctCount?: number;
  /** @format uint64 */
  submittedAt?: number | null;
  answers?: TrainingCourseStudentTheoryAnswerDetailModel[];
}

export interface TrainingCourseStudentTheoryAnswerDetailModel {
  /** @format int32 */
  questionId?: number;
  /** Theory exam question type */
  type?: TheoryQuestionType;
  title?: string;
  content?: string;
  options?: string[];
  answerIndexes?: number[];
  selectedIndexes?: number[];
  isCorrect?: boolean | null;
  /** @format int32 */
  score?: number;
  /** @format int32 */
  maxScore?: number;
  /** @format int32 */
  order?: number;
}

export interface TrainingCourseStudentChallengeLearningModel {
  /** @format int32 */
  exerciseChallengeId?: number;
  title?: string;
  displayTitle?: string | null;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  isRequired?: boolean;
  solved?: boolean;
  /** @format int32 */
  submissionCount?: number;
  /** @format int32 */
  acceptedSubmissionCount?: number;
  lastStatus?: AnswerResult | null;
  /** @format uint64 */
  lastSubmittedAt?: number | null;
  lastIpAddress?: string | null;
  instanceEntry?: string | null;
  /** @format uint64 */
  instanceStopAt?: number | null;
}

export interface TrainingCourseEnrollmentReviewModel {
  /** Training course enrollment status */
  status?: TrainingCourseEnrollmentStatus;
  /** @maxLength 512 */
  reviewComment?: string;
}

export interface TrainingCourseStudentCandidateModel {
  /** @format guid */
  userId?: string;
  userName?: string;
  realName?: string;
  stdNumber?: string;
  email?: string | null;
  avatar?: string | null;
  alreadyEnrolled?: boolean;
}

export interface TrainingCourseStudentEnrollModel {
  /** @format guid */
  userId?: string;
}

export interface TrainingCourseTeacherCandidateModel {
  /** @format guid */
  userId?: string;
  userName?: string;
  realName?: string;
  stdNumber?: string;
  email?: string | null;
  /** User role enumeration */
  role?: Role;
  alreadyTeacher?: boolean;
}

export interface TrainingCourseTeacherEditModel {
  /** @format guid */
  teacherId?: string;
  /** Training course teacher role */
  role?: TrainingCourseTeacherRole;
}

export interface TrainingCourseChapterEditModel {
  /** @format int32 */
  parentId?: number | null;
  /**
   * @minLength 1
   * @maxLength 128
   */
  title: string;
  /** @maxLength 512 */
  summary?: string;
  content?: string;
  /** Training article format */
  contentType?: TrainingArticleContentType;
  completionPolicy?: TrainingChapterCompletionPolicy;
  /** Training course video provider */
  videoProvider?: TrainingCourseVideoProvider;
  /** @maxLength 1024 */
  videoUrl?: string | null;
  /** @maxLength 64 */
  videoFileHash?: string | null;
  /** @format int32 */
  order?: number;
  isPublished?: boolean;
}

export interface TrainingCourseResourceEditModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  title: string;
  /** @maxLength 512 */
  description?: string;
  /** Training course resource type */
  type?: TrainingCourseResourceType;
  /** @maxLength 1024 */
  externalUrl?: string | null;
  /** @maxLength 64 */
  localFileHash?: string | null;
  /** @format int32 */
  order?: number;
  isVisible?: boolean;
}

export type TrainingCourseTheoryQuestionModel = TheoryQuestionEditModel & {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  courseId?: number;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
};

export type TrainingCourseChapterTheoryPaperDetailModel =
  TrainingCourseChapterTheoryPaperEditModel & {
    /** @format int32 */
    id?: number;
    /** @format int32 */
    courseId?: number;
    /** @format int32 */
    chapterId?: number;
    /** @format uint64 */
    publishedAt?: number | null;
    /** @format uint64 */
    updatedAt?: number;
    /** @format int32 */
    totalScore?: number;
  };

export interface TrainingCourseChapterTheoryPaperEditModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  title: string;
  description?: string;
  /**
   * @format int32
   * @min 1
   * @max 100
   */
  passRate?: number;
  allowRetake?: boolean;
  showCorrectAnswerAfterSubmit?: boolean;
  isPublished?: boolean;
  questions?: TrainingCourseTheoryPaperQuestionEditModel[];
}

export type TrainingCourseTheoryPaperQuestionEditModel =
  TheoryQuestionEditModel & {
    /** @format int32 */
    id?: number;
    /** @format int32 */
    sourceQuestionId?: number | null;
    /**
     * @format int32
     * @min 1
     * @max 2147483647
     */
    score?: number;
    /** @format int32 */
    order?: number;
  };

export interface TrainingCourseImageTemplateModel {
  /** @format int32 */
  id?: number;
  name?: string;
  osType?: OSType;
  imageType?: ImageType;
  status?: ImageStatus;
  /** @format int64 */
  fileSize?: number;
  description?: string | null;
  errorMessage?: string | null;
  imageHash?: string | null;
  registryUrl?: string | null;
  /** @format uint64 */
  uploadedAt?: number;
  /** @format int32 */
  trainingCourseId?: number | null;
}

export interface TrainingCourseDockerRegisterModel {
  /**
   * @minLength 1
   * @maxLength 256
   */
  name: string;
  /**
   * @minLength 1
   * @maxLength 512
   */
  registryUrl: string;
  osType?: OSType;
  /** @maxLength 512 */
  registryAuth?: string | null;
}

export interface TrainingCourseLocalImageImportModel {
  /**
   * @minLength 1
   * @maxLength 1024
   */
  localPath: string;
  /** @maxLength 256 */
  displayName?: string | null;
}

export interface TrainingCourseImageTemplateAttachModel {
  /** @format int32 */
  templateId?: number;
}

export interface TrainingCourseChallengeCreateModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  title: string;
  content?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  /** @format int32 */
  imageTemplateId?: number | null;
  /** @maxLength 512 */
  containerImage?: string | null;
  /** @format int32 */
  memoryLimit?: number | null;
  /** @format int32 */
  cpuCount?: number | null;
  /** @format int32 */
  storageLimit?: number | null;
  /** @format int32 */
  exposePort?: number | null;
  networkMode?: NetworkMode | null;
  /** @maxLength 120 */
  flagTemplate?: string | null;
  /** @maxLength 127 */
  staticFlag?: string | null;
  /** @format int32 */
  submissionLimit?: number;
  /** @format int32 */
  chapterId?: number | null;
  /** @format int32 */
  order?: number;
  isRequired?: boolean;
  /** @maxLength 128 */
  displayTitle?: string | null;
  attachmentType?: FileType;
  /** @maxLength 64 */
  attachmentFileHash?: string | null;
  /** @maxLength 1024 */
  attachmentRemoteUrl?: string | null;
}

export type TrainingCourseChallengeEditDetailModel =
  TrainingCourseChallengeCreateModel & {
    /** @format int32 */
    exerciseChallengeId?: number;
    attachmentUrl?: string | null;
    attachmentFileName?: string | null;
    /** @format int64 */
    attachmentFileSize?: number | null;
    /** @format int32 */
    submissionCount?: number;
    hasSubmittedAnswers?: boolean;
  };

export type TrainingCourseChallengeUpdateModel =
  TrainingCourseChallengeCreateModel & object;

export interface TrainingCourseChallengeEditModel {
  /** @format int32 */
  exerciseChallengeId?: number;
  /** @format int32 */
  chapterId?: number | null;
  /** @format int32 */
  order?: number;
  isRequired?: boolean;
  /** @maxLength 128 */
  displayTitle?: string | null;
  attachmentType?: FileType;
  /** @maxLength 64 */
  attachmentFileHash?: string | null;
  /** @maxLength 1024 */
  attachmentRemoteUrl?: string | null;
}

export interface TrainingPersonalOverviewModel {
  /** @format int32 */
  visibleCourseCount?: number;
  /** @format int32 */
  joinedCourseCount?: number;
  /** @format int32 */
  completedCourseCount?: number;
  /** @format int32 */
  averageProgress?: number;
  /** @format int32 */
  completedChapterCount?: number;
  /** @format int32 */
  totalChapterCount?: number;
  /** @format int32 */
  ctfSolvedChallenges?: number;
  /** @format int32 */
  ctfTotalChallenges?: number;
  /** @format int32 */
  theoryPassedAssessments?: number;
  /** @format int32 */
  theoryTotalAssessments?: number;
  /** @format int32 */
  checkInDays?: number;
  /** @format int32 */
  currentCheckInStreak?: number;
  checkedInToday?: boolean;
  checkIns?: TrainingCheckInModel[];
  activity?: TrainingActivityPointModel[];
}

export interface TrainingCheckInModel {
  /** @format date */
  date?: string;
  /** @format uint64 */
  checkedAt?: number;
  isToday?: boolean;
}

export interface TrainingActivityPointModel {
  /** @format date */
  date?: string;
  /** @format int32 */
  studyActions?: number;
  /** @format int32 */
  completedChapters?: number;
  /** @format int32 */
  acceptedChallenges?: number;
  checkedIn?: boolean;
}

export interface TrainingCourseEnrollmentApplyModel {
  /** @maxLength 512 */
  applyReason?: string;
}

export interface TrainingCourseChapterTheoryPlayerPaperModel {
  /** @format int32 */
  paperId?: number;
  /** @format int32 */
  courseId?: number;
  /** @format int32 */
  chapterId?: number;
  title?: string;
  description?: string;
  /** @format int32 */
  totalScore?: number;
  /** @format int32 */
  passRate?: number;
  allowRetake?: boolean;
  showCorrectAnswerAfterSubmit?: boolean;
  /** @format int32 */
  attemptNumber?: number | null;
  status?: TheoryAnswerSheetStatus | null;
  /** @format int32 */
  score?: number | null;
  passed?: boolean | null;
  /** @format uint64 */
  submittedAt?: number | null;
  /** @format uint64 */
  updatedAt?: number | null;
  questions?: TrainingCourseChapterTheoryPlayerQuestionModel[];
  answers?: TheoryAnswerModel[];
}

export interface TrainingCourseChapterTheoryPlayerQuestionModel {
  /** @format int32 */
  id?: number;
  /** Theory exam question type */
  type?: TheoryQuestionType;
  title?: string;
  content?: string;
  options?: string[];
  /** @format int32 */
  score?: number;
  /** @format int32 */
  order?: number;
  answerIndexes?: number[] | null;
}

export interface TrainingCourseChallengeDetailModel {
  /** @format int32 */
  courseId?: number;
  /** @format int32 */
  chapterId?: number | null;
  /** @format int32 */
  id?: number;
  title?: string;
  content?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  hints?: string[] | null;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  tags?: string[] | null;
  solved?: boolean;
  /** @format int32 */
  attempts?: number;
  /** @format int32 */
  limit?: number;
  flags?: FlagStepInfo[] | null;
  context?: ClientFlagContext;
}

export interface TrainingCourseSubmitResultModel {
  /** @format int64 */
  submissionId?: number;
  /** Judgement result */
  status?: AnswerResult;
  chapterCompleted?: boolean;
  courseCompleted?: boolean;
}

import { apiLanguage } from "@Utils/I18n";
import type {
  AxiosInstance,
  AxiosRequestConfig,
  AxiosResponse,
  HeadersDefaults,
  ResponseType,
} from "axios";
import axios from "axios";

export type QueryParamsType = Record<string | number, any>;

export interface FullRequestParams
  extends Omit<AxiosRequestConfig, "data" | "params" | "url" | "responseType"> {
  /** set parameter to `true` for call `securityWorker` for this request */
  secure?: boolean;
  /** request path */
  path: string;
  /** content type of request body */
  type?: ContentType;
  /** query params */
  query?: QueryParamsType;
  /** format of response (i.e. response.json() -> format: "json") */
  format?: ResponseType;
  /** request body */
  body?: unknown;
}

export type RequestParams = Omit<
  FullRequestParams,
  "body" | "method" | "query" | "path"
>;

export interface ApiConfig<SecurityDataType = unknown>
  extends Omit<AxiosRequestConfig, "data" | "cancelToken"> {
  securityWorker?: (
    securityData: SecurityDataType | null,
  ) => Promise<AxiosRequestConfig | void> | AxiosRequestConfig | void;
  secure?: boolean;
  format?: ResponseType;
}

export enum ContentType {
  Json = "application/json",
  FormData = "multipart/form-data",
  UrlEncoded = "application/x-www-form-urlencoded",
  Text = "text/plain",
}

export class HttpClient<SecurityDataType = unknown> {
  public instance: AxiosInstance;
  private securityData: SecurityDataType | null = null;
  private securityWorker?: ApiConfig<SecurityDataType>["securityWorker"];
  private secure?: boolean;
  private format?: ResponseType;

  constructor({
    securityWorker,
    secure,
    format,
    ...axiosConfig
  }: ApiConfig<SecurityDataType> = {}) {
    this.instance = axios.create({
      ...axiosConfig,
      baseURL: axiosConfig.baseURL || "",
    });
    this.secure = secure;
    this.format = format;
    this.securityWorker = securityWorker;
  }

  public setSecurityData = (data: SecurityDataType | null) => {
    this.securityData = data;
  };

  protected mergeRequestParams(
    params1: AxiosRequestConfig,
    params2?: AxiosRequestConfig,
  ): AxiosRequestConfig {
    const method = params1.method || (params2 && params2.method);

    return {
      ...this.instance.defaults,
      ...params1,
      ...params2,
      headers: {
        ...(method &&
          this.instance.defaults.headers[
            method.toLowerCase() as keyof HeadersDefaults
          ]),
        ...params1.headers,
        ...(params2 && params2.headers),
      },
    };
  }

  protected stringifyFormItem(formItem: unknown) {
    if (typeof formItem === "object" && formItem !== null) {
      return JSON.stringify(formItem);
    } else {
      return `${formItem}`;
    }
  }

  protected createFormData(input: Record<string, unknown>): FormData {
    return Object.keys(input || {}).reduce((formData, key) => {
      const property = input[key];
      const propertyContent: any[] =
        property instanceof Array ? property : [property];

      for (const formItem of propertyContent) {
        const isFileType = formItem instanceof Blob || formItem instanceof File;
        formData.append(
          key,
          isFileType ? formItem : this.stringifyFormItem(formItem),
        );
      }

      return formData;
    }, new FormData());
  }

  public request = async <T = any, _E = any>({
    secure,
    path,
    type,
    query,
    format,
    body,
    ...params
  }: FullRequestParams): Promise<AxiosResponse<T>> => {
    const secureParams =
      ((typeof secure === "boolean" ? secure : this.secure) &&
        this.securityWorker &&
        (await this.securityWorker(this.securityData))) ||
      {};
    const requestParams = this.mergeRequestParams(params, secureParams);
    const responseFormat = format || this.format || undefined;

    if (
      type === ContentType.FormData &&
      body &&
      body !== null &&
      typeof body === "object"
    ) {
      body = this.createFormData(body as Record<string, unknown>);
    }

    if (
      type === ContentType.Text &&
      body &&
      body !== null &&
      typeof body !== "string"
    ) {
      body = JSON.stringify(body);
    }

    return this.instance.request({
      ...requestParams,
      headers: {
        ...requestParams.headers,
        ...(type && type !== ContentType.FormData
          ? { "Content-Type": type }
          : {}),
        "Accept-Language": apiLanguage,
      },
      params: query,
      responseType: responseFormat,
      data: body,
      url: path,
    });
  };
}

import useSWR, { MutatorOptions, SWRConfiguration, mutate } from "swr";

/**
 * @title YINYU CTF Platform API
 * @version v1
 *
 * YINYU CTF Platform API Document
 */
export class Api<
  SecurityDataType extends unknown,
> extends HttpClient<SecurityDataType> {
  apiTokens = {
    /**
     * No description
     *
     * @tags ApiTokens
     * @name ApiTokensIssue
     * @request POST:/api/tokens
     */
    apiTokensIssue: (data: ApiTokenCreateModel, params: RequestParams = {}) =>
      this.request<ApiTokenResponse, any>({
        path: `/api/tokens`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ApiTokens
     * @name ApiTokensList
     * @request GET:/api/tokens
     */
    apiTokensList: (params: RequestParams = {}) =>
      this.request<ApiTokenModel[], any>({
        path: `/api/tokens`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags ApiTokens
     * @name ApiTokensList
     * @request GET:/api/tokens
     */
    useApiTokensList: (options?: SWRConfiguration, doFetch: boolean = true) =>
      useSWR<ApiTokenModel[], any>(doFetch ? `/api/tokens` : null, options),

    /**
     * No description
     *
     * @tags ApiTokens
     * @name ApiTokensList
     * @request GET:/api/tokens
     */
    mutateApiTokensList: (
      data?: ApiTokenModel[] | Promise<ApiTokenModel[]>,
      options?: MutatorOptions,
    ) => mutate<ApiTokenModel[]>(`/api/tokens`, data, options),

    /**
     * No description
     *
     * @tags ApiTokens
     * @name ApiTokensRevoke
     * @request DELETE:/api/tokens/{id}
     */
    apiTokensRevoke: (id: string, params: RequestParams = {}) =>
      this.request<void, ProblemDetails>({
        path: `/api/tokens/${id}`,
        method: "DELETE",
        ...params,
      }),
  };
  account = {
    /**
     * @description Use this API to update user's avatar. User permissions required.
     *
     * @tags Account
     * @name AccountAvatar
     * @summary Update user avatar
     * @request PUT:/api/account/avatar
     */
    accountAvatar: (
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<string, RequestResponse>({
        path: `/api/account/avatar`,
        method: "PUT",
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),

    /**
     * @description Use this API to change user's email. User permissions required. Email URL: /confirm
     *
     * @tags Account
     * @name AccountChangeEmail
     * @summary User email change
     * @request PUT:/api/account/changeemail
     */
    accountChangeEmail: (data: MailChangeModel, params: RequestParams = {}) =>
      this.request<RequestResponseOfBoolean, RequestResponse>({
        path: `/api/account/changeemail`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Use this API to change user's password. User permissions required.
     *
     * @tags Account
     * @name AccountChangePassword
     * @summary User password change
     * @request PUT:/api/account/changepassword
     */
    accountChangePassword: (
      data: PasswordChangeModel,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/account/changepassword`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to log in to the account.
     *
     * @tags Account
     * @name AccountLogIn
     * @summary User login
     * @request POST:/api/account/login
     */
    accountLogIn: (data: LoginModel, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/account/login`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to log out of the account. User permissions required.
     *
     * @tags Account
     * @name AccountLogOut
     * @summary User logout
     * @request POST:/api/account/logout
     */
    accountLogOut: (params: RequestParams = {}) =>
      this.request<void, any>({
        path: `/api/account/logout`,
        method: "POST",
        ...params,
      }),

    /**
     * @description Use this API to confirm email change. Email verification code required. User permissions required.
     *
     * @tags Account
     * @name AccountMailChangeConfirm
     * @summary User email change confirmation
     * @request POST:/api/account/mailchangeconfirm
     */
    accountMailChangeConfirm: (
      data: AccountVerifyModel,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/account/mailchangeconfirm`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to reset the password. Email verification code is required.
     *
     * @tags Account
     * @name AccountPasswordReset
     * @summary User password reset
     * @request POST:/api/account/passwordreset
     */
    accountPasswordReset: (
      data: PasswordResetModel,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/account/passwordreset`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Account
     * @name AccountPortalSso
     * @summary Login through the unified portal IAM service.
     * @request GET:/api/account/portal-sso
     */
    accountPortalSso: (
      query?: {
        /** Token passed by the portal dashboard. */
        portal_token?: string | null;
        /**
         * Local URL to redirect to after login.
         * @default "/"
         */
        returnUrl?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<any, void | RequestResponse>({
        path: `/api/account/portal-sso`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags Account
     * @name AccountPortalSso
     * @summary Login through the unified portal IAM service.
     * @request GET:/api/account/portal-sso
     */
    useAccountPortalSso: (
      query?: {
        /** Token passed by the portal dashboard. */
        portal_token?: string | null;
        /**
         * Local URL to redirect to after login.
         * @default "/"
         */
        returnUrl?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<any, void | RequestResponse>(
        doFetch ? [`/api/account/portal-sso`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Account
     * @name AccountPortalSso
     * @summary Login through the unified portal IAM service.
     * @request GET:/api/account/portal-sso
     */
    mutateAccountPortalSso: (
      query?: {
        /** Token passed by the portal dashboard. */
        portal_token?: string | null;
        /**
         * Local URL to redirect to after login.
         * @default "/"
         */
        returnUrl?: string | null;
      },
      data?: any | Promise<any>,
      options?: MutatorOptions,
    ) => mutate<any>([`/api/account/portal-sso`, query], data, options),

    /**
     * @description Use this API to get user information. User permissions required.
     *
     * @tags Account
     * @name AccountProfile
     * @summary Get user information
     * @request GET:/api/account/profile
     */
    accountProfile: (params: RequestParams = {}) =>
      this.request<ProfileUserInfoModel, RequestResponse>({
        path: `/api/account/profile`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Use this API to get user information. User permissions required.
     *
     * @tags Account
     * @name AccountProfile
     * @summary Get user information
     * @request GET:/api/account/profile
     */
    useAccountProfile: (options?: SWRConfiguration, doFetch: boolean = true) =>
      useSWR<ProfileUserInfoModel, RequestResponse>(
        doFetch ? `/api/account/profile` : null,
        options,
      ),

    /**
     * @description Use this API to get user information. User permissions required.
     *
     * @tags Account
     * @name AccountProfile
     * @summary Get user information
     * @request GET:/api/account/profile
     */
    mutateAccountProfile: (
      data?: ProfileUserInfoModel | Promise<ProfileUserInfoModel>,
      options?: MutatorOptions,
    ) => mutate<ProfileUserInfoModel>(`/api/account/profile`, data, options),

    /**
     * @description Use this API to request password recovery. Sends an email to the user. Email URL: /reset
     *
     * @tags Account
     * @name AccountRecovery
     * @summary User password recovery request
     * @request POST:/api/account/recovery
     */
    accountRecovery: (data: RecoveryModel, params: RequestParams = {}) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/account/recovery`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Use this API to register a new user. In development environment, no verification. Email URL: /verify
     *
     * @tags Account
     * @name AccountRegister
     * @summary User registration
     * @request POST:/api/account/register
     */
    accountRegister: (data: RegisterModel, params: RequestParams = {}) =>
      this.request<RequestResponseOfRegisterStatus, RequestResponse>({
        path: `/api/account/register`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Use this API to update username and description. User permissions required.
     *
     * @tags Account
     * @name AccountUpdate
     * @summary User data update
     * @request PUT:/api/account/update
     */
    accountUpdate: (data: ProfileUpdateModel, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/account/update`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to confirm email using the verification code.
     *
     * @tags Account
     * @name AccountVerify
     * @summary User email confirmation
     * @request POST:/api/account/verify
     */
    accountVerify: (data: AccountVerifyModel, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/account/verify`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),
  };
  admin = {
    /**
     * @description Use this API to add users in batch, requires Admin permission
     *
     * @tags Admin
     * @name AdminAddUsers
     * @summary Add users in batch
     * @request POST:/api/admin/users
     */
    adminAddUsers: (data: UserCreateModel[], params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/users`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to delete team, requires Admin permission
     *
     * @tags Admin
     * @name AdminDeleteTeam
     * @summary Delete team
     * @request DELETE:/api/admin/teams/{id}
     */
    adminDeleteTeam: (id: number, params: RequestParams = {}) =>
      this.request<string, RequestResponse>({
        path: `/api/admin/teams/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description Use this API to delete user, requires Admin permission
     *
     * @tags Admin
     * @name AdminDeleteUser
     * @summary Delete user
     * @request DELETE:/api/admin/users/{userid}
     */
    adminDeleteUser: (userid: string, params: RequestParams = {}) =>
      this.request<string, RequestResponse>({
        path: `/api/admin/users/${userid}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description Use this API to forcibly delete container instance, requires Admin permission
     *
     * @tags Admin
     * @name AdminDestroyInstance
     * @summary Delete container instance
     * @request DELETE:/api/admin/instances/{id}
     */
    adminDestroyInstance: (id: string, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/instances/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Use this API to download all Writeups, requires Admin permission
     *
     * @tags Admin
     * @name AdminDownloadAllWriteups
     * @summary Download all Writeups
     * @request GET:/api/admin/writeups/{id}/all
     */
    adminDownloadAllWriteups: (id: number, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/writeups/${id}/all`,
        method: "GET",
        ...params,
      }),

    /**
     * @description Use this API to get all files, requires Admin permission
     *
     * @tags Admin
     * @name AdminFiles
     * @summary Get all files
     * @request GET:/api/admin/files
     */
    adminFiles: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 500
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<ArrayResponseOfLocalFile, RequestResponse>({
        path: `/api/admin/files`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Use this API to get all files, requires Admin permission
     *
     * @tags Admin
     * @name AdminFiles
     * @summary Get all files
     * @request GET:/api/admin/files
     */
    useAdminFiles: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 500
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfLocalFile, RequestResponse>(
        doFetch ? [`/api/admin/files`, query] : null,
        options,
      ),

    /**
     * @description Use this API to get all files, requires Admin permission
     *
     * @tags Admin
     * @name AdminFiles
     * @summary Get all files
     * @request GET:/api/admin/files
     */
    mutateAdminFiles: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 500
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?: ArrayResponseOfLocalFile | Promise<ArrayResponseOfLocalFile>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfLocalFile>(
        [`/api/admin/files`, query],
        data,
        options,
      ),

    /**
     * @description Use this API to get global settings, requires Admin permission
     *
     * @tags Admin
     * @name AdminGetConfigs
     * @summary Get configuration
     * @request GET:/api/admin/config
     */
    adminGetConfigs: (params: RequestParams = {}) =>
      this.request<ConfigEditModel, RequestResponse>({
        path: `/api/admin/config`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Use this API to get global settings, requires Admin permission
     *
     * @tags Admin
     * @name AdminGetConfigs
     * @summary Get configuration
     * @request GET:/api/admin/config
     */
    useAdminGetConfigs: (options?: SWRConfiguration, doFetch: boolean = true) =>
      useSWR<ConfigEditModel, RequestResponse>(
        doFetch ? `/api/admin/config` : null,
        options,
      ),

    /**
     * @description Use this API to get global settings, requires Admin permission
     *
     * @tags Admin
     * @name AdminGetConfigs
     * @summary Get configuration
     * @request GET:/api/admin/config
     */
    mutateAdminGetConfigs: (
      data?: ConfigEditModel | Promise<ConfigEditModel>,
      options?: MutatorOptions,
    ) => mutate<ConfigEditModel>(`/api/admin/config`, data, options),

    /**
     * @description Use this API to get all container instances, requires Admin permission
     *
     * @tags Admin
     * @name AdminInstances
     * @summary Get all container instances
     * @request GET:/api/admin/instances
     */
    adminInstances: (params: RequestParams = {}) =>
      this.request<ArrayResponseOfContainerInstanceModel, RequestResponse>({
        path: `/api/admin/instances`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Use this API to get all container instances, requires Admin permission
     *
     * @tags Admin
     * @name AdminInstances
     * @summary Get all container instances
     * @request GET:/api/admin/instances
     */
    useAdminInstances: (options?: SWRConfiguration, doFetch: boolean = true) =>
      useSWR<ArrayResponseOfContainerInstanceModel, RequestResponse>(
        doFetch ? `/api/admin/instances` : null,
        options,
      ),

    /**
     * @description Use this API to get all container instances, requires Admin permission
     *
     * @tags Admin
     * @name AdminInstances
     * @summary Get all container instances
     * @request GET:/api/admin/instances
     */
    mutateAdminInstances: (
      data?:
        | ArrayResponseOfContainerInstanceModel
        | Promise<ArrayResponseOfContainerInstanceModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfContainerInstanceModel>(
        `/api/admin/instances`,
        data,
        options,
      ),

    /**
     * @description Use this API to get all logs, requires Admin permission
     *
     * @tags Admin
     * @name AdminLogs
     * @summary Get all logs
     * @request GET:/api/admin/logs
     */
    adminLogs: (
      query?: {
        /** @default "All" */
        level?: string | null;
        /**
         * @format int32
         * @min 0
         * @max 1000
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<LogMessageModel[], RequestResponse>({
        path: `/api/admin/logs`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Use this API to get all logs, requires Admin permission
     *
     * @tags Admin
     * @name AdminLogs
     * @summary Get all logs
     * @request GET:/api/admin/logs
     */
    useAdminLogs: (
      query?: {
        /** @default "All" */
        level?: string | null;
        /**
         * @format int32
         * @min 0
         * @max 1000
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<LogMessageModel[], RequestResponse>(
        doFetch ? [`/api/admin/logs`, query] : null,
        options,
      ),

    /**
     * @description Use this API to get all logs, requires Admin permission
     *
     * @tags Admin
     * @name AdminLogs
     * @summary Get all logs
     * @request GET:/api/admin/logs
     */
    mutateAdminLogs: (
      query?: {
        /** @default "All" */
        level?: string | null;
        /**
         * @format int32
         * @min 0
         * @max 1000
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?: LogMessageModel[] | Promise<LogMessageModel[]>,
      options?: MutatorOptions,
    ) => mutate<LogMessageModel[]>([`/api/admin/logs`, query], data, options),

    /**
     * @description Use this API to update team participation status, review application, requires Admin permission
     *
     * @tags Admin
     * @name AdminParticipation
     * @summary Update participation status
     * @request PUT:/api/admin/participation/{id}
     */
    adminParticipation: (
      id: number,
      data: ParticipationEditModel,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/participation/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to reset the platform Logo, requires Admin permission
     *
     * @tags Admin
     * @name AdminResetLogo
     * @summary Reset platform Logo
     * @request DELETE:/api/admin/config/logo
     */
    adminResetLogo: (params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/config/logo`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Use this API to reset user password, requires Admin permission
     *
     * @tags Admin
     * @name AdminResetPassword
     * @summary Reset user password
     * @request DELETE:/api/admin/users/{userid}/password
     */
    adminResetPassword: (userid: string, params: RequestParams = {}) =>
      this.request<string, RequestResponse>({
        path: `/api/admin/users/${userid}/password`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description Use this API to search teams, requires Admin permission
     *
     * @tags Admin
     * @name AdminSearchTeams
     * @summary Search teams
     * @request POST:/api/admin/teams/search
     */
    adminSearchTeams: (
      query?: {
        hint?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<ArrayResponseOfTeamInfoModel, RequestResponse>({
        path: `/api/admin/teams/search`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * @description Use this API to search users, requires Admin permission
     *
     * @tags Admin
     * @name AdminSearchUsers
     * @summary Search users
     * @request POST:/api/admin/users/search
     */
    adminSearchUsers: (
      query?: {
        hint?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<ArrayResponseOfUserInfoModel, RequestResponse>({
        path: `/api/admin/users/search`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * @description Use this API to get all teams, requires Admin permission
     *
     * @tags Admin
     * @name AdminTeams
     * @summary Get all team information
     * @request GET:/api/admin/teams
     */
    adminTeams: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 500
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<ArrayResponseOfTeamInfoModel, RequestResponse>({
        path: `/api/admin/teams`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Use this API to get all teams, requires Admin permission
     *
     * @tags Admin
     * @name AdminTeams
     * @summary Get all team information
     * @request GET:/api/admin/teams
     */
    useAdminTeams: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 500
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfTeamInfoModel, RequestResponse>(
        doFetch ? [`/api/admin/teams`, query] : null,
        options,
      ),

    /**
     * @description Use this API to get all teams, requires Admin permission
     *
     * @tags Admin
     * @name AdminTeams
     * @summary Get all team information
     * @request GET:/api/admin/teams
     */
    mutateAdminTeams: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 500
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?:
        | ArrayResponseOfTeamInfoModel
        | Promise<ArrayResponseOfTeamInfoModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfTeamInfoModel>(
        [`/api/admin/teams`, query],
        data,
        options,
      ),

    /**
     * @description Use this API to change global settings, requires Admin permission
     *
     * @tags Admin
     * @name AdminUpdateConfigs
     * @summary Change configuration
     * @request PUT:/api/admin/config
     */
    adminUpdateConfigs: (data: ConfigEditModel, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/config`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to change the platform Logo, requires Admin permission
     *
     * @tags Admin
     * @name AdminUpdateLogo
     * @summary Change platform Logo
     * @request POST:/api/admin/config/logo
     */
    adminUpdateLogo: (
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/config/logo`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        ...params,
      }),

    /**
     * @description Use this API to modify team information, requires Admin permission
     *
     * @tags Admin
     * @name AdminUpdateTeam
     * @summary Modify team information
     * @request PUT:/api/admin/teams/{id}
     */
    adminUpdateTeam: (
      id: number,
      data: AdminTeamModel,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/teams/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to modify user information, requires Admin permission
     *
     * @tags Admin
     * @name AdminUpdateUserInfo
     * @summary Modify user information
     * @request PUT:/api/admin/users/{userid}
     */
    adminUpdateUserInfo: (
      userid: string,
      data: AdminUserInfoModel,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/users/${userid}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to get user information, requires Admin permission
     *
     * @tags Admin
     * @name AdminUserInfo
     * @summary Get user information
     * @request GET:/api/admin/users/{userid}
     */
    adminUserInfo: (userid: string, params: RequestParams = {}) =>
      this.request<ProfileUserInfoModel, RequestResponse>({
        path: `/api/admin/users/${userid}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Use this API to get user information, requires Admin permission
     *
     * @tags Admin
     * @name AdminUserInfo
     * @summary Get user information
     * @request GET:/api/admin/users/{userid}
     */
    useAdminUserInfo: (
      userid: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ProfileUserInfoModel, RequestResponse>(
        doFetch ? `/api/admin/users/${userid}` : null,
        options,
      ),

    /**
     * @description Use this API to get user information, requires Admin permission
     *
     * @tags Admin
     * @name AdminUserInfo
     * @summary Get user information
     * @request GET:/api/admin/users/{userid}
     */
    mutateAdminUserInfo: (
      userid: string,
      data?: ProfileUserInfoModel | Promise<ProfileUserInfoModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ProfileUserInfoModel>(`/api/admin/users/${userid}`, data, options),

    /**
     * @description Use this API to get all users, requires Admin permission
     *
     * @tags Admin
     * @name AdminUsers
     * @summary Get all users
     * @request GET:/api/admin/users
     */
    adminUsers: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 500
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
        role?: Role | null;
        /** @format int32 */
        groupId?: number | null;
        keyword?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<ArrayResponseOfUserInfoModel, RequestResponse>({
        path: `/api/admin/users`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Use this API to get all users, requires Admin permission
     *
     * @tags Admin
     * @name AdminUsers
     * @summary Get all users
     * @request GET:/api/admin/users
     */
    useAdminUsers: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 500
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
        role?: Role | null;
        /** @format int32 */
        groupId?: number | null;
        keyword?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfUserInfoModel, RequestResponse>(
        doFetch ? [`/api/admin/users`, query] : null,
        options,
      ),

    /**
     * @description Use this API to get all users, requires Admin permission
     *
     * @tags Admin
     * @name AdminUsers
     * @summary Get all users
     * @request GET:/api/admin/users
     */
    mutateAdminUsers: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 500
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
        role?: Role | null;
        /** @format int32 */
        groupId?: number | null;
        keyword?: string | null;
      },
      data?:
        | ArrayResponseOfUserInfoModel
        | Promise<ArrayResponseOfUserInfoModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfUserInfoModel>(
        [`/api/admin/users`, query],
        data,
        options,
      ),

    /**
     * @description Use this API to get Writeup basic information, requires Admin permission
     *
     * @tags Admin
     * @name AdminWriteups
     * @summary Get all Writeup basic information
     * @request GET:/api/admin/writeups/{id}
     */
    adminWriteups: (id: number, params: RequestParams = {}) =>
      this.request<WriteupInfoModel, RequestResponse>({
        path: `/api/admin/writeups/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Use this API to get Writeup basic information, requires Admin permission
     *
     * @tags Admin
     * @name AdminWriteups
     * @summary Get all Writeup basic information
     * @request GET:/api/admin/writeups/{id}
     */
    useAdminWriteups: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<WriteupInfoModel, RequestResponse>(
        doFetch ? `/api/admin/writeups/${id}` : null,
        options,
      ),

    /**
     * @description Use this API to get Writeup basic information, requires Admin permission
     *
     * @tags Admin
     * @name AdminWriteups
     * @summary Get all Writeup basic information
     * @request GET:/api/admin/writeups/{id}
     */
    mutateAdminWriteups: (
      id: number,
      data?: WriteupInfoModel | Promise<WriteupInfoModel>,
      options?: MutatorOptions,
    ) => mutate<WriteupInfoModel>(`/api/admin/writeups/${id}`, data, options),
  };
  assets = {
    /**
     * @description Delete a file by hash
     *
     * @tags Assets
     * @name AssetsDelete
     * @summary File deletion interface
     * @request DELETE:/api/assets/{hash}
     */
    assetsDelete: (hash: string, params: RequestParams = {}) =>
      this.request<void, RequestResponse | ProblemDetails>({
        path: `/api/assets/${hash}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Retrieve a file by hash, filename is not matched
     *
     * @tags Assets
     * @name AssetsGetFile
     * @summary File retrieval interface
     * @request GET:/assets/{hash}/{filename}
     */
    assetsGetFile: (
      hash: string,
      filename: string,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/assets/${hash}/${filename}`,
        method: "GET",
        ...params,
      }),

    /**
     * @description Upload one or more files
     *
     * @tags Assets
     * @name AssetsUpload
     * @summary File upload interface
     * @request POST:/api/assets
     */
    assetsUpload: (
      data: {
        files?: File[] | null;
      },
      query?: {
        /** Unified filename */
        filename?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<LocalFile[], RequestResponse>({
        path: `/api/assets`,
        method: "POST",
        query: query,
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),
  };
  awdpAdmin = {
    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminCreateService
     * @request POST:/api/admin/awdp/games/{gameId}/services
     */
    awdpAdminCreateService: (
      gameId: number,
      data: AwdpServiceCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<AwdpServiceViewModel, RequestResponse>({
        path: `/api/admin/awdp/games/${gameId}/services`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminDeleteService
     * @request DELETE:/api/admin/awdp/services/{serviceId}
     */
    awdpAdminDeleteService: (serviceId: number, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/awdp/services/${serviceId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetAttackLogs
     * @request GET:/api/admin/awdp/games/{gameId}/attacklogs
     */
    awdpAdminGetAttackLogs: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<ArrayResponseOfAwdpAttackLogItem, RequestResponse>({
        path: `/api/admin/awdp/games/${gameId}/attacklogs`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetAttackLogs
     * @request GET:/api/admin/awdp/games/{gameId}/attacklogs
     */
    useAwdpAdminGetAttackLogs: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfAwdpAttackLogItem, RequestResponse>(
        doFetch ? [`/api/admin/awdp/games/${gameId}/attacklogs`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetAttackLogs
     * @request GET:/api/admin/awdp/games/{gameId}/attacklogs
     */
    mutateAwdpAdminGetAttackLogs: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?:
        | ArrayResponseOfAwdpAttackLogItem
        | Promise<ArrayResponseOfAwdpAttackLogItem>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfAwdpAttackLogItem>(
        [`/api/admin/awdp/games/${gameId}/attacklogs`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetInstances
     * @request GET:/api/admin/awdp/games/{gameId}/instances
     */
    awdpAdminGetInstances: (gameId: number, params: RequestParams = {}) =>
      this.request<AwdpServiceStatusModel[], RequestResponse>({
        path: `/api/admin/awdp/games/${gameId}/instances`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetInstances
     * @request GET:/api/admin/awdp/games/{gameId}/instances
     */
    useAwdpAdminGetInstances: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AwdpServiceStatusModel[], RequestResponse>(
        doFetch ? `/api/admin/awdp/games/${gameId}/instances` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetInstances
     * @request GET:/api/admin/awdp/games/{gameId}/instances
     */
    mutateAwdpAdminGetInstances: (
      gameId: number,
      data?: AwdpServiceStatusModel[] | Promise<AwdpServiceStatusModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<AwdpServiceStatusModel[]>(
        `/api/admin/awdp/games/${gameId}/instances`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetPatches
     * @request GET:/api/admin/awdp/games/{gameId}/patches
     */
    awdpAdminGetPatches: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        ArrayResponseOfAwdpPatchSubmissionViewModel,
        RequestResponse
      >({
        path: `/api/admin/awdp/games/${gameId}/patches`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetPatches
     * @request GET:/api/admin/awdp/games/{gameId}/patches
     */
    useAwdpAdminGetPatches: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfAwdpPatchSubmissionViewModel, RequestResponse>(
        doFetch ? [`/api/admin/awdp/games/${gameId}/patches`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetPatches
     * @request GET:/api/admin/awdp/games/{gameId}/patches
     */
    mutateAwdpAdminGetPatches: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?:
        | ArrayResponseOfAwdpPatchSubmissionViewModel
        | Promise<ArrayResponseOfAwdpPatchSubmissionViewModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfAwdpPatchSubmissionViewModel>(
        [`/api/admin/awdp/games/${gameId}/patches`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetScoreboard
     * @request GET:/api/admin/awdp/games/{gameId}/scoreboard
     */
    awdpAdminGetScoreboard: (gameId: number, params: RequestParams = {}) =>
      this.request<AwdpScoreboardItem[], RequestResponse>({
        path: `/api/admin/awdp/games/${gameId}/scoreboard`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetScoreboard
     * @request GET:/api/admin/awdp/games/{gameId}/scoreboard
     */
    useAwdpAdminGetScoreboard: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AwdpScoreboardItem[], RequestResponse>(
        doFetch ? `/api/admin/awdp/games/${gameId}/scoreboard` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetScoreboard
     * @request GET:/api/admin/awdp/games/{gameId}/scoreboard
     */
    mutateAwdpAdminGetScoreboard: (
      gameId: number,
      data?: AwdpScoreboardItem[] | Promise<AwdpScoreboardItem[]>,
      options?: MutatorOptions,
    ) =>
      mutate<AwdpScoreboardItem[]>(
        `/api/admin/awdp/games/${gameId}/scoreboard`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetServices
     * @request GET:/api/admin/awdp/games/{gameId}/services
     */
    awdpAdminGetServices: (gameId: number, params: RequestParams = {}) =>
      this.request<AwdpServiceViewModel[], RequestResponse>({
        path: `/api/admin/awdp/games/${gameId}/services`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetServices
     * @request GET:/api/admin/awdp/games/{gameId}/services
     */
    useAwdpAdminGetServices: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AwdpServiceViewModel[], RequestResponse>(
        doFetch ? `/api/admin/awdp/games/${gameId}/services` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetServices
     * @request GET:/api/admin/awdp/games/{gameId}/services
     */
    mutateAwdpAdminGetServices: (
      gameId: number,
      data?: AwdpServiceViewModel[] | Promise<AwdpServiceViewModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<AwdpServiceViewModel[]>(
        `/api/admin/awdp/games/${gameId}/services`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetStatus
     * @request GET:/api/admin/awdp/games/{gameId}/status
     */
    awdpAdminGetStatus: (gameId: number, params: RequestParams = {}) =>
      this.request<AwdpGameStatusModel, RequestResponse>({
        path: `/api/admin/awdp/games/${gameId}/status`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetStatus
     * @request GET:/api/admin/awdp/games/{gameId}/status
     */
    useAwdpAdminGetStatus: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AwdpGameStatusModel, RequestResponse>(
        doFetch ? `/api/admin/awdp/games/${gameId}/status` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminGetStatus
     * @request GET:/api/admin/awdp/games/{gameId}/status
     */
    mutateAwdpAdminGetStatus: (
      gameId: number,
      data?: AwdpGameStatusModel | Promise<AwdpGameStatusModel>,
      options?: MutatorOptions,
    ) =>
      mutate<AwdpGameStatusModel>(
        `/api/admin/awdp/games/${gameId}/status`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminRecoverInstance
     * @request POST:/api/admin/awdp/instances/{instanceId}/recover
     */
    awdpAdminRecoverInstance: (
      instanceId: number,
      params: RequestParams = {},
    ) =>
      this.request<AwdpInstanceActionModel, RequestResponse>({
        path: `/api/admin/awdp/instances/${instanceId}/recover`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminResetInstance
     * @request POST:/api/admin/awdp/instances/{instanceId}/reset
     */
    awdpAdminResetInstance: (instanceId: number, params: RequestParams = {}) =>
      this.request<AwdpInstanceActionModel, RequestResponse>({
        path: `/api/admin/awdp/instances/${instanceId}/reset`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminStartGame
     * @request POST:/api/admin/awdp/games/{gameId}/start
     */
    awdpAdminStartGame: (gameId: number, params: RequestParams = {}) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/admin/awdp/games/${gameId}/start`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminStopGame
     * @request POST:/api/admin/awdp/games/{gameId}/stop
     */
    awdpAdminStopGame: (gameId: number, params: RequestParams = {}) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/admin/awdp/games/${gameId}/stop`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AwdpAdmin
     * @name AwdpAdminUpdateService
     * @request PUT:/api/admin/awdp/services/{serviceId}
     */
    awdpAdminUpdateService: (
      serviceId: number,
      data: AwdpServiceUpdateModel,
      params: RequestParams = {},
    ) =>
      this.request<AwdpServiceViewModel, RequestResponse>({
        path: `/api/admin/awdp/services/${serviceId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  awdpPlayer = {
    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetAttackLogs
     * @request GET:/api/awdp/games/{gameId}/attacklogs
     */
    awdpPlayerGetAttackLogs: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<ArrayResponseOfAwdpAttackLogItem, RequestResponse>({
        path: `/api/awdp/games/${gameId}/attacklogs`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetAttackLogs
     * @request GET:/api/awdp/games/{gameId}/attacklogs
     */
    useAwdpPlayerGetAttackLogs: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfAwdpAttackLogItem, RequestResponse>(
        doFetch ? [`/api/awdp/games/${gameId}/attacklogs`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetAttackLogs
     * @request GET:/api/awdp/games/{gameId}/attacklogs
     */
    mutateAwdpPlayerGetAttackLogs: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?:
        | ArrayResponseOfAwdpAttackLogItem
        | Promise<ArrayResponseOfAwdpAttackLogItem>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfAwdpAttackLogItem>(
        [`/api/awdp/games/${gameId}/attacklogs`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetInstances
     * @request GET:/api/awdp/games/{gameId}/instances
     */
    awdpPlayerGetInstances: (gameId: number, params: RequestParams = {}) =>
      this.request<AwdpTeamServiceStatus[], RequestResponse>({
        path: `/api/awdp/games/${gameId}/instances`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetInstances
     * @request GET:/api/awdp/games/{gameId}/instances
     */
    useAwdpPlayerGetInstances: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AwdpTeamServiceStatus[], RequestResponse>(
        doFetch ? `/api/awdp/games/${gameId}/instances` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetInstances
     * @request GET:/api/awdp/games/{gameId}/instances
     */
    mutateAwdpPlayerGetInstances: (
      gameId: number,
      data?: AwdpTeamServiceStatus[] | Promise<AwdpTeamServiceStatus[]>,
      options?: MutatorOptions,
    ) =>
      mutate<AwdpTeamServiceStatus[]>(
        `/api/awdp/games/${gameId}/instances`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetPatchStatus
     * @request GET:/api/awdp/games/{gameId}/patchstatus
     */
    awdpPlayerGetPatchStatus: (gameId: number, params: RequestParams = {}) =>
      this.request<AwdpPatchStatusItem[], RequestResponse>({
        path: `/api/awdp/games/${gameId}/patchstatus`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetPatchStatus
     * @request GET:/api/awdp/games/{gameId}/patchstatus
     */
    useAwdpPlayerGetPatchStatus: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AwdpPatchStatusItem[], RequestResponse>(
        doFetch ? `/api/awdp/games/${gameId}/patchstatus` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetPatchStatus
     * @request GET:/api/awdp/games/{gameId}/patchstatus
     */
    mutateAwdpPlayerGetPatchStatus: (
      gameId: number,
      data?: AwdpPatchStatusItem[] | Promise<AwdpPatchStatusItem[]>,
      options?: MutatorOptions,
    ) =>
      mutate<AwdpPatchStatusItem[]>(
        `/api/awdp/games/${gameId}/patchstatus`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetScoreboard
     * @request GET:/api/awdp/games/{gameId}/scoreboard
     */
    awdpPlayerGetScoreboard: (gameId: number, params: RequestParams = {}) =>
      this.request<AwdpScoreboardItem[], RequestResponse>({
        path: `/api/awdp/games/${gameId}/scoreboard`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetScoreboard
     * @request GET:/api/awdp/games/{gameId}/scoreboard
     */
    useAwdpPlayerGetScoreboard: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AwdpScoreboardItem[], RequestResponse>(
        doFetch ? `/api/awdp/games/${gameId}/scoreboard` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetScoreboard
     * @request GET:/api/awdp/games/{gameId}/scoreboard
     */
    mutateAwdpPlayerGetScoreboard: (
      gameId: number,
      data?: AwdpScoreboardItem[] | Promise<AwdpScoreboardItem[]>,
      options?: MutatorOptions,
    ) =>
      mutate<AwdpScoreboardItem[]>(
        `/api/awdp/games/${gameId}/scoreboard`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetStatus
     * @request GET:/api/awdp/games/{gameId}/status
     */
    awdpPlayerGetStatus: (gameId: number, params: RequestParams = {}) =>
      this.request<AwdpGameStatusModel, RequestResponse>({
        path: `/api/awdp/games/${gameId}/status`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetStatus
     * @request GET:/api/awdp/games/{gameId}/status
     */
    useAwdpPlayerGetStatus: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AwdpGameStatusModel, RequestResponse>(
        doFetch ? `/api/awdp/games/${gameId}/status` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerGetStatus
     * @request GET:/api/awdp/games/{gameId}/status
     */
    mutateAwdpPlayerGetStatus: (
      gameId: number,
      data?: AwdpGameStatusModel | Promise<AwdpGameStatusModel>,
      options?: MutatorOptions,
    ) =>
      mutate<AwdpGameStatusModel>(
        `/api/awdp/games/${gameId}/status`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerRecoverInstance
     * @request POST:/api/awdp/instances/{instanceId}/recover
     */
    awdpPlayerRecoverInstance: (
      instanceId: number,
      params: RequestParams = {},
    ) =>
      this.request<AwdpInstanceActionModel, RequestResponse>({
        path: `/api/awdp/instances/${instanceId}/recover`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerResetInstance
     * @request POST:/api/awdp/instances/{instanceId}/reset
     */
    awdpPlayerResetInstance: (instanceId: number, params: RequestParams = {}) =>
      this.request<AwdpInstanceActionModel, RequestResponse>({
        path: `/api/awdp/instances/${instanceId}/reset`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerSubmitFlag
     * @request POST:/api/awdp/games/{gameId}/flags
     */
    awdpPlayerSubmitFlag: (
      gameId: number,
      data: AwdpSubmitModel,
      params: RequestParams = {},
    ) =>
      this.request<AwdpSubmitResultModel, RequestResponse>({
        path: `/api/awdp/games/${gameId}/flags`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags AwdpPlayer
     * @name AwdpPlayerSubmitPatch
     * @request POST:/api/awdp/games/{gameId}/patches
     */
    awdpPlayerSubmitPatch: (
      gameId: number,
      data: {
        /** @format int32 */
        ServiceId?: number;
        /** @format binary */
        File?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<AwdpPatchSubmissionViewModel, RequestResponse>({
        path: `/api/awdp/games/${gameId}/patches`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),
  };
  edit = {
    /**
     * @description Adding a game challenge flag requires administrator privileges
     *
     * @tags Edit
     * @name EditAddFlags
     * @summary Add Game Challenge Flag
     * @request POST:/api/edit/games/{id}/challenges/{cId}/flags
     */
    editAddFlags: (
      id: number,
      cId: number,
      data: FlagCreateModel[],
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/edit/games/${id}/challenges/${cId}/flags`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Adding a game requires administrator privileges
     *
     * @tags Edit
     * @name EditAddGame
     * @summary Add Game
     * @request POST:/api/edit/games
     */
    editAddGame: (data: GameInfoModel, params: RequestParams = {}) =>
      this.request<GameInfoModel, RequestResponse>({
        path: `/api/edit/games`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Adding a game challenge requires administrator privileges
     *
     * @tags Edit
     * @name EditAddGameChallenge
     * @summary Add Game Challenge
     * @request POST:/api/edit/games/{id}/challenges
     */
    editAddGameChallenge: (
      id: number,
      data: ChallengeInfoModel,
      params: RequestParams = {},
    ) =>
      this.request<ChallengeEditDetailModel, RequestResponse>({
        path: `/api/edit/games/${id}/challenges`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Adding a game notice requires administrator privileges
     *
     * @tags Edit
     * @name EditAddGameNotice
     * @summary Add Game Notice
     * @request POST:/api/edit/games/{id}/notices
     */
    editAddGameNotice: (
      id: number,
      data: GameNoticeModel,
      params: RequestParams = {},
    ) =>
      this.request<GameNotice, RequestResponse>({
        path: `/api/edit/games/${id}/notices`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Adding a post requires administrator privileges
     *
     * @tags Edit
     * @name EditAddPost
     * @summary Add Post
     * @request POST:/api/edit/posts
     */
    editAddPost: (data: PostEditModel, params: RequestParams = {}) =>
      this.request<string, RequestResponse>({
        path: `/api/edit/posts`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Add a new division for a game; requires administrator privileges
     *
     * @tags Edit
     * @name EditCreateDivision
     * @summary Create Division
     * @request POST:/api/edit/games/{id}/divisions
     */
    editCreateDivision: (
      id: number,
      data: DivisionCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<Division, RequestResponse>({
        path: `/api/edit/games/${id}/divisions`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Testing a game challenge container requires administrator privileges
     *
     * @tags Edit
     * @name EditCreateTestContainer
     * @summary Test Game Challenge Container
     * @request POST:/api/edit/games/{id}/challenges/{cId}/container
     */
    editCreateTestContainer: (
      id: number,
      cId: number,
      params: RequestParams = {},
    ) =>
      this.request<ContainerInfoModel, RequestResponse>({
        path: `/api/edit/games/${id}/challenges/${cId}/container`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description Delete a division for a game; requires administrator privileges
     *
     * @tags Edit
     * @name EditDeleteDivision
     * @summary Delete Division
     * @request DELETE:/api/edit/games/{id}/divisions/{divisionId}
     */
    editDeleteDivision: (
      id: number,
      divisionId: number,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/edit/games/${id}/divisions/${divisionId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Deleting a game requires administrator privileges
     *
     * @tags Edit
     * @name EditDeleteGame
     * @summary Delete Game
     * @request DELETE:/api/edit/games/{id}
     */
    editDeleteGame: (id: number, params: RequestParams = {}) =>
      this.request<GameInfoModel, RequestResponse>({
        path: `/api/edit/games/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description Deleting a game notice requires administrator privileges
     *
     * @tags Edit
     * @name EditDeleteGameNotice
     * @summary Delete Game Notice
     * @request DELETE:/api/edit/games/{id}/notices/{noticeId}
     */
    editDeleteGameNotice: (
      id: number,
      noticeId: number,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/edit/games/${id}/notices/${noticeId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Deleting all WriteUps for a game requires administrator privileges
     *
     * @tags Edit
     * @name EditDeleteGameWriteUps
     * @summary Delete All WriteUps
     * @request DELETE:/api/edit/games/{id}/writeups
     */
    editDeleteGameWriteUps: (id: number, params: RequestParams = {}) =>
      this.request<GameInfoModel, RequestResponse>({
        path: `/api/edit/games/${id}/writeups`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description Deleting a post requires administrator privileges
     *
     * @tags Edit
     * @name EditDeletePost
     * @summary Delete Post
     * @request DELETE:/api/edit/posts/{id}
     */
    editDeletePost: (id: string, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/edit/posts/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Destroying a test game challenge container requires administrator privileges
     *
     * @tags Edit
     * @name EditDestroyTestContainer
     * @summary Destroy Test Game Challenge Container
     * @request DELETE:/api/edit/games/{id}/challenges/{cId}/container
     */
    editDestroyTestContainer: (
      id: number,
      cId: number,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/edit/games/${id}/challenges/${cId}/container`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Export game with all challenges, divisions, and attachments as a ZIP file; requires Admin permission
     *
     * @tags Edit
     * @name EditExportGame
     * @summary Export game package
     * @request POST:/api/edit/games/{id}/export
     */
    editExportGame: (id: number, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/edit/games/${id}/export`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Edit
     * @name EditFlushScoreboardCache
     * @summary Flush Scoreboard Cache
     * @request POST:/api/edit/games/{id}/scoreboard/flush
     */
    editFlushScoreboardCache: (id: number, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/edit/games/${id}/scoreboard/flush`,
        method: "POST",
        ...params,
      }),

    /**
     * @description Retrieve all divisions for a game; requires administrator privileges
     *
     * @tags Edit
     * @name EditGetDivisions
     * @summary Get Divisions
     * @request GET:/api/edit/games/{id}/divisions
     */
    editGetDivisions: (id: number, params: RequestParams = {}) =>
      this.request<Division[], RequestResponse>({
        path: `/api/edit/games/${id}/divisions`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieve all divisions for a game; requires administrator privileges
     *
     * @tags Edit
     * @name EditGetDivisions
     * @summary Get Divisions
     * @request GET:/api/edit/games/{id}/divisions
     */
    useEditGetDivisions: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Division[], RequestResponse>(
        doFetch ? `/api/edit/games/${id}/divisions` : null,
        options,
      ),

    /**
     * @description Retrieve all divisions for a game; requires administrator privileges
     *
     * @tags Edit
     * @name EditGetDivisions
     * @summary Get Divisions
     * @request GET:/api/edit/games/{id}/divisions
     */
    mutateEditGetDivisions: (
      id: number,
      data?: Division[] | Promise<Division[]>,
      options?: MutatorOptions,
    ) => mutate<Division[]>(`/api/edit/games/${id}/divisions`, data, options),

    /**
     * @description Retrieving a game requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGame
     * @summary Get Game
     * @request GET:/api/edit/games/{id}
     */
    editGetGame: (id: number, params: RequestParams = {}) =>
      this.request<GameInfoModel, RequestResponse>({
        path: `/api/edit/games/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieving a game requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGame
     * @summary Get Game
     * @request GET:/api/edit/games/{id}
     */
    useEditGetGame: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<GameInfoModel, RequestResponse>(
        doFetch ? `/api/edit/games/${id}` : null,
        options,
      ),

    /**
     * @description Retrieving a game requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGame
     * @summary Get Game
     * @request GET:/api/edit/games/{id}
     */
    mutateEditGetGame: (
      id: number,
      data?: GameInfoModel | Promise<GameInfoModel>,
      options?: MutatorOptions,
    ) => mutate<GameInfoModel>(`/api/edit/games/${id}`, data, options),

    /**
     * @description Retrieving a game challenge requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGameChallenge
     * @summary Get Game Challenge
     * @request GET:/api/edit/games/{id}/challenges/{cId}
     */
    editGetGameChallenge: (
      id: number,
      cId: number,
      params: RequestParams = {},
    ) =>
      this.request<ChallengeEditDetailModel, RequestResponse>({
        path: `/api/edit/games/${id}/challenges/${cId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieving a game challenge requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGameChallenge
     * @summary Get Game Challenge
     * @request GET:/api/edit/games/{id}/challenges/{cId}
     */
    useEditGetGameChallenge: (
      id: number,
      cId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ChallengeEditDetailModel, RequestResponse>(
        doFetch ? `/api/edit/games/${id}/challenges/${cId}` : null,
        options,
      ),

    /**
     * @description Retrieving a game challenge requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGameChallenge
     * @summary Get Game Challenge
     * @request GET:/api/edit/games/{id}/challenges/{cId}
     */
    mutateEditGetGameChallenge: (
      id: number,
      cId: number,
      data?: ChallengeEditDetailModel | Promise<ChallengeEditDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ChallengeEditDetailModel>(
        `/api/edit/games/${id}/challenges/${cId}`,
        data,
        options,
      ),

    /**
     * @description Retrieving all game challenges requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGameChallenges
     * @summary Get All Game Challenges
     * @request GET:/api/edit/games/{id}/challenges
     */
    editGetGameChallenges: (id: number, params: RequestParams = {}) =>
      this.request<ChallengeInfoModel[], RequestResponse>({
        path: `/api/edit/games/${id}/challenges`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieving all game challenges requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGameChallenges
     * @summary Get All Game Challenges
     * @request GET:/api/edit/games/{id}/challenges
     */
    useEditGetGameChallenges: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ChallengeInfoModel[], RequestResponse>(
        doFetch ? `/api/edit/games/${id}/challenges` : null,
        options,
      ),

    /**
     * @description Retrieving all game challenges requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGameChallenges
     * @summary Get All Game Challenges
     * @request GET:/api/edit/games/{id}/challenges
     */
    mutateEditGetGameChallenges: (
      id: number,
      data?: ChallengeInfoModel[] | Promise<ChallengeInfoModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<ChallengeInfoModel[]>(
        `/api/edit/games/${id}/challenges`,
        data,
        options,
      ),

    /**
     * @description Retrieving game notices requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGameNotices
     * @summary Get Game Notices
     * @request GET:/api/edit/games/{id}/notices
     */
    editGetGameNotices: (id: number, params: RequestParams = {}) =>
      this.request<GameNotice[], RequestResponse>({
        path: `/api/edit/games/${id}/notices`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieving game notices requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGameNotices
     * @summary Get Game Notices
     * @request GET:/api/edit/games/{id}/notices
     */
    useEditGetGameNotices: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<GameNotice[], RequestResponse>(
        doFetch ? `/api/edit/games/${id}/notices` : null,
        options,
      ),

    /**
     * @description Retrieving game notices requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGameNotices
     * @summary Get Game Notices
     * @request GET:/api/edit/games/{id}/notices
     */
    mutateEditGetGameNotices: (
      id: number,
      data?: GameNotice[] | Promise<GameNotice[]>,
      options?: MutatorOptions,
    ) => mutate<GameNotice[]>(`/api/edit/games/${id}/notices`, data, options),

    /**
     * @description Retrieving the game list requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGames
     * @summary Get Game List
     * @request GET:/api/edit/games
     */
    editGetGames: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         */
        count?: number;
        /** @format int32 */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<ArrayResponseOfGameInfoModel, RequestResponse>({
        path: `/api/edit/games`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieving the game list requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGames
     * @summary Get Game List
     * @request GET:/api/edit/games
     */
    useEditGetGames: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         */
        count?: number;
        /** @format int32 */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfGameInfoModel, RequestResponse>(
        doFetch ? [`/api/edit/games`, query] : null,
        options,
      ),

    /**
     * @description Retrieving the game list requires administrator privileges
     *
     * @tags Edit
     * @name EditGetGames
     * @summary Get Game List
     * @request GET:/api/edit/games
     */
    mutateEditGetGames: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         */
        count?: number;
        /** @format int32 */
        skip?: number;
      },
      data?:
        | ArrayResponseOfGameInfoModel
        | Promise<ArrayResponseOfGameInfoModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfGameInfoModel>(
        [`/api/edit/games`, query],
        data,
        options,
      ),

    /**
     * @description Import game from a ZIP package; requires Admin permission
     *
     * @tags Edit
     * @name EditImportGame
     * @summary Import game package
     * @request POST:/api/edit/games/import
     */
    editImportGame: (
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<number, RequestResponse>({
        path: `/api/edit/games/import`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),

    /**
     * @description Deleting a game challenge flag requires administrator privileges
     *
     * @tags Edit
     * @name EditRemoveFlag
     * @summary Delete Game Challenge Flag
     * @request DELETE:/api/edit/games/{id}/challenges/{cId}/flags/{fId}
     */
    editRemoveFlag: (
      id: number,
      cId: number,
      fId: number,
      params: RequestParams = {},
    ) =>
      this.request<TaskStatus, RequestResponse>({
        path: `/api/edit/games/${id}/challenges/${cId}/flags/${fId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description Deleting a game challenge requires administrator privileges
     *
     * @tags Edit
     * @name EditRemoveGameChallenge
     * @summary Delete Game Challenge
     * @request DELETE:/api/edit/games/{id}/challenges/{cId}
     */
    editRemoveGameChallenge: (
      id: number,
      cId: number,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/edit/games/${id}/challenges/${cId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Updating a game challenge attachment requires administrator privileges; only for non-dynamic attachment challenges
     *
     * @tags Edit
     * @name EditUpdateAttachment
     * @summary Update Game Challenge Attachment
     * @request POST:/api/edit/games/{id}/challenges/{cId}/attachment
     */
    editUpdateAttachment: (
      id: number,
      cId: number,
      data: AttachmentCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<number, RequestResponse>({
        path: `/api/edit/games/${id}/challenges/${cId}/attachment`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Update a division for a game; requires administrator privileges
     *
     * @tags Edit
     * @name EditUpdateDivision
     * @summary Update Division
     * @request PUT:/api/edit/games/{id}/divisions/{divisionId}
     */
    editUpdateDivision: (
      id: number,
      divisionId: number,
      data: DivisionEditModel,
      params: RequestParams = {},
    ) =>
      this.request<Division, RequestResponse>({
        path: `/api/edit/games/${id}/divisions/${divisionId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Updating a game challenge flag requires administrator privileges
     *
     * @tags Edit
     * @name EditUpdateFlag
     * @summary Update Game Challenge Flag
     * @request PUT:/api/edit/games/{id}/challenges/{cId}/flags/{fId}
     */
    editUpdateFlag: (
      id: number,
      cId: number,
      fId: number,
      data: FlagCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/edit/games/${id}/challenges/${cId}/flags/${fId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Updating a game requires administrator privileges
     *
     * @tags Edit
     * @name EditUpdateGame
     * @summary Update Game
     * @request PUT:/api/edit/games/{id}
     */
    editUpdateGame: (
      id: number,
      data: GameInfoModel,
      params: RequestParams = {},
    ) =>
      this.request<GameInfoModel, RequestResponse>({
        path: `/api/edit/games/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Updating a game challenge, requires administrator privileges. Flags are not affected; use Flag-related APIs to modify
     *
     * @tags Edit
     * @name EditUpdateGameChallenge
     * @summary Update Game Challenge Information
     * @request PUT:/api/edit/games/{id}/challenges/{cId}
     */
    editUpdateGameChallenge: (
      id: number,
      cId: number,
      data: ChallengeUpdateModel,
      params: RequestParams = {},
    ) =>
      this.request<ChallengeEditDetailModel, RequestResponse>({
        path: `/api/edit/games/${id}/challenges/${cId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Updating a game notice requires administrator privileges
     *
     * @tags Edit
     * @name EditUpdateGameNotice
     * @summary Update Game Notice
     * @request PUT:/api/edit/games/{id}/notices/{noticeId}
     */
    editUpdateGameNotice: (
      id: number,
      noticeId: number,
      data: GameNoticeModel,
      params: RequestParams = {},
    ) =>
      this.request<GameNotice, RequestResponse>({
        path: `/api/edit/games/${id}/notices/${noticeId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Use this endpoint to update the game poster; administrator privileges required
     *
     * @tags Edit
     * @name EditUpdateGamePoster
     * @summary Update Game Poster
     * @request PUT:/api/edit/games/{id}/poster
     */
    editUpdateGamePoster: (
      id: number,
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<string, RequestResponse>({
        path: `/api/edit/games/${id}/poster`,
        method: "PUT",
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),

    /**
     * @description Updating a post requires administrator privileges
     *
     * @tags Edit
     * @name EditUpdatePost
     * @summary Update Post
     * @request PUT:/api/edit/posts/{id}
     */
    editUpdatePost: (
      id: string,
      data: PostEditModel,
      params: RequestParams = {},
    ) =>
      this.request<PostDetailModel, RequestResponse>({
        path: `/api/edit/posts/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  game = {
    /**
     * @description Retrieves all challenges of the game; requires User permission and active team participation
     *
     * @tags Game
     * @name GameChallengesWithTeamInfo
     * @summary Get team details in a game
     * @request GET:/api/game/{id}/details
     */
    gameChallengesWithTeamInfo: (id: number, params: RequestParams = {}) =>
      this.request<GameDetailModel, RequestResponse>({
        path: `/api/game/${id}/details`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves all challenges of the game; requires User permission and active team participation
     *
     * @tags Game
     * @name GameChallengesWithTeamInfo
     * @summary Get team details in a game
     * @request GET:/api/game/{id}/details
     */
    useGameChallengesWithTeamInfo: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<GameDetailModel, RequestResponse>(
        doFetch ? `/api/game/${id}/details` : null,
        options,
      ),

    /**
     * @description Retrieves all challenges of the game; requires User permission and active team participation
     *
     * @tags Game
     * @name GameChallengesWithTeamInfo
     * @summary Get team details in a game
     * @request GET:/api/game/{id}/details
     */
    mutateGameChallengesWithTeamInfo: (
      id: number,
      data?: GameDetailModel | Promise<GameDetailModel>,
      options?: MutatorOptions,
    ) => mutate<GameDetailModel>(`/api/game/${id}/details`, data, options),

    /**
     * @description Retrieves game cheat data; requires Monitor permission
     *
     * @tags Game
     * @name GameCheatInfo
     * @summary Get game cheat information
     * @request GET:/api/game/{id}/cheatinfo
     */
    gameCheatInfo: (id: number, params: RequestParams = {}) =>
      this.request<CheatInfoModel[], RequestResponse>({
        path: `/api/game/${id}/cheatinfo`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves game cheat data; requires Monitor permission
     *
     * @tags Game
     * @name GameCheatInfo
     * @summary Get game cheat information
     * @request GET:/api/game/{id}/cheatinfo
     */
    useGameCheatInfo: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<CheatInfoModel[], RequestResponse>(
        doFetch ? `/api/game/${id}/cheatinfo` : null,
        options,
      ),

    /**
     * @description Retrieves game cheat data; requires Monitor permission
     *
     * @tags Game
     * @name GameCheatInfo
     * @summary Get game cheat information
     * @request GET:/api/game/{id}/cheatinfo
     */
    mutateGameCheatInfo: (
      id: number,
      data?: CheatInfoModel[] | Promise<CheatInfoModel[]>,
      options?: MutatorOptions,
    ) => mutate<CheatInfoModel[]>(`/api/game/${id}/cheatinfo`, data, options),

    /**
     * @description Creates a container; requires User permission
     *
     * @tags Game
     * @name GameCreateContainer
     * @summary Creates a container
     * @request POST:/api/game/{id}/container/{challengeId}
     */
    gameCreateContainer: (
      id: number,
      challengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<ContainerInfoModel, RequestResponse>({
        path: `/api/game/${id}/container/${challengeId}`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description Deletes a team's traffic packet files for a challenge; requires Monitor permission
     *
     * @tags Game
     * @name GameDeleteAllTeamTraffic
     * @summary Deletes all traffic files
     * @request DELETE:/api/game/captures/{challengeId}/{partId}/all
     */
    gameDeleteAllTeamTraffic: (
      challengeId: number,
      partId: number,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/game/captures/${challengeId}/${partId}/all`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Deletes a container; requires User permission
     *
     * @tags Game
     * @name GameDeleteContainer
     * @summary Deletes a container
     * @request DELETE:/api/game/{id}/container/{challengeId}
     */
    gameDeleteContainer: (
      id: number,
      challengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/game/${id}/container/${challengeId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Deletes a traffic packet file; requires Monitor permission
     *
     * @tags Game
     * @name GameDeleteTeamTraffic
     * @summary Deletes a traffic file
     * @request DELETE:/api/game/captures/{challengeId}/{partId}/{filename}
     */
    gameDeleteTeamTraffic: (
      challengeId: number,
      partId: number,
      filename: string,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/game/captures/${challengeId}/${partId}/${filename}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Destroys a Windows VM instance and cleans up the Guacamole RDP connection.
     *
     * @tags Game
     * @name GameDestroyVm
     * @summary Destroy a VM instance
     * @request DELETE:/api/game/{id}/vm/{challengeId}
     */
    gameDestroyVm: (
      id: number,
      challengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/game/${id}/vm/${challengeId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Retrieves game event data; requires Monitor permission
     *
     * @tags Game
     * @name GameEvents
     * @summary Get game events
     * @request GET:/api/game/{id}/events
     */
    gameEvents: (
      id: number,
      query?: {
        /**
         * Hide container events
         * @default false
         */
        hideContainer?: boolean;
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<GameEvent[], RequestResponse>({
        path: `/api/game/${id}/events`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves game event data; requires Monitor permission
     *
     * @tags Game
     * @name GameEvents
     * @summary Get game events
     * @request GET:/api/game/{id}/events
     */
    useGameEvents: (
      id: number,
      query?: {
        /**
         * Hide container events
         * @default false
         */
        hideContainer?: boolean;
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<GameEvent[], RequestResponse>(
        doFetch ? [`/api/game/${id}/events`, query] : null,
        options,
      ),

    /**
     * @description Retrieves game event data; requires Monitor permission
     *
     * @tags Game
     * @name GameEvents
     * @summary Get game events
     * @request GET:/api/game/{id}/events
     */
    mutateGameEvents: (
      id: number,
      query?: {
        /**
         * Hide container events
         * @default false
         */
        hideContainer?: boolean;
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?: GameEvent[] | Promise<GameEvent[]>,
      options?: MutatorOptions,
    ) => mutate<GameEvent[]>([`/api/game/${id}/events`, query], data, options),

    /**
     * @description Extends container lifetime; requires User permission and can only be extended two hours within ten minutes before expiration
     *
     * @tags Game
     * @name GameExtendContainerLifetime
     * @summary Extends container lifetime
     * @request POST:/api/game/{id}/container/{challengeId}/extend
     */
    gameExtendContainerLifetime: (
      id: number,
      challengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<ContainerInfoModel, RequestResponse>({
        path: `/api/game/${id}/container/${challengeId}/extend`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description Retrieves detailed information about the game
     *
     * @tags Game
     * @name GameGame
     * @summary Get detailed game information
     * @request GET:/api/game/{id}
     */
    gameGame: (id: number, params: RequestParams = {}) =>
      this.request<DetailedGameInfoModel, RequestResponse>({
        path: `/api/game/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves detailed information about the game
     *
     * @tags Game
     * @name GameGame
     * @summary Get detailed game information
     * @request GET:/api/game/{id}
     */
    useGameGame: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<DetailedGameInfoModel, RequestResponse>(
        doFetch ? `/api/game/${id}` : null,
        options,
      ),

    /**
     * @description Retrieves detailed information about the game
     *
     * @tags Game
     * @name GameGame
     * @summary Get detailed game information
     * @request GET:/api/game/{id}
     */
    mutateGameGame: (
      id: number,
      data?: DetailedGameInfoModel | Promise<DetailedGameInfoModel>,
      options?: MutatorOptions,
    ) => mutate<DetailedGameInfoModel>(`/api/game/${id}`, data, options),

    /**
     * @description Retrieves game information in specified range
     *
     * @tags Game
     * @name GameGames
     * @summary Get games
     * @request GET:/api/game
     */
    gameGames: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 50
         * @default 10
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<ArrayResponseOfBasicGameInfoModel, RequestResponse>({
        path: `/api/game`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves game information in specified range
     *
     * @tags Game
     * @name GameGames
     * @summary Get games
     * @request GET:/api/game
     */
    useGameGames: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 50
         * @default 10
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfBasicGameInfoModel, RequestResponse>(
        doFetch ? [`/api/game`, query] : null,
        options,
      ),

    /**
     * @description Retrieves game information in specified range
     *
     * @tags Game
     * @name GameGames
     * @summary Get games
     * @request GET:/api/game
     */
    mutateGameGames: (
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 50
         * @default 10
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?:
        | ArrayResponseOfBasicGameInfoModel
        | Promise<ArrayResponseOfBasicGameInfoModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfBasicGameInfoModel>(
        [`/api/game`, query],
        data,
        options,
      ),

    /**
     * @description Downloads all traffic packet files for a team and challenge; requires Monitor permission
     *
     * @tags Game
     * @name GameGetAllTeamTraffic
     * @summary Download all traffic files
     * @request GET:/api/game/captures/{challengeId}/{partId}/all
     */
    gameGetAllTeamTraffic: (
      challengeId: number,
      partId: number,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/game/captures/${challengeId}/${partId}/all`,
        method: "GET",
        ...params,
      }),

    /**
     * @description Retrieves challenge information; requires User permission and active team participation
     *
     * @tags Game
     * @name GameGetChallenge
     * @summary Get challenge information
     * @request GET:/api/game/{id}/challenges/{challengeId}
     */
    gameGetChallenge: (
      id: number,
      challengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<ChallengeDetailModel, RequestResponse>({
        path: `/api/game/${id}/challenges/${challengeId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves challenge information; requires User permission and active team participation
     *
     * @tags Game
     * @name GameGetChallenge
     * @summary Get challenge information
     * @request GET:/api/game/{id}/challenges/{challengeId}
     */
    useGameGetChallenge: (
      id: number,
      challengeId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ChallengeDetailModel, RequestResponse>(
        doFetch ? `/api/game/${id}/challenges/${challengeId}` : null,
        options,
      ),

    /**
     * @description Retrieves challenge information; requires User permission and active team participation
     *
     * @tags Game
     * @name GameGetChallenge
     * @summary Get challenge information
     * @request GET:/api/game/{id}/challenges/{challengeId}
     */
    mutateGameGetChallenge: (
      id: number,
      challengeId: number,
      data?: ChallengeDetailModel | Promise<ChallengeDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ChallengeDetailModel>(
        `/api/game/${id}/challenges/${challengeId}`,
        data,
        options,
      ),

    /**
     * @description Retrieves challenges with traffic capturing enabled; requires Monitor permission
     *
     * @tags Game
     * @name GameGetChallengesWithTrafficCapturing
     * @summary Get challenges with traffic capturing enabled
     * @request GET:/api/game/games/{id}/captures
     */
    gameGetChallengesWithTrafficCapturing: (
      id: number,
      params: RequestParams = {},
    ) =>
      this.request<ChallengeTrafficModel[], RequestResponse>({
        path: `/api/game/games/${id}/captures`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves challenges with traffic capturing enabled; requires Monitor permission
     *
     * @tags Game
     * @name GameGetChallengesWithTrafficCapturing
     * @summary Get challenges with traffic capturing enabled
     * @request GET:/api/game/games/{id}/captures
     */
    useGameGetChallengesWithTrafficCapturing: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ChallengeTrafficModel[], RequestResponse>(
        doFetch ? `/api/game/games/${id}/captures` : null,
        options,
      ),

    /**
     * @description Retrieves challenges with traffic capturing enabled; requires Monitor permission
     *
     * @tags Game
     * @name GameGetChallengesWithTrafficCapturing
     * @summary Get challenges with traffic capturing enabled
     * @request GET:/api/game/games/{id}/captures
     */
    mutateGameGetChallengesWithTrafficCapturing: (
      id: number,
      data?: ChallengeTrafficModel[] | Promise<ChallengeTrafficModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<ChallengeTrafficModel[]>(
        `/api/game/games/${id}/captures`,
        data,
        options,
      ),

    /**
     * @description Retrieves the list of captured teams for a game challenge; requires Monitor permission
     *
     * @tags Game
     * @name GameGetChallengeTraffic
     * @summary Get team captures in a challenge
     * @request GET:/api/game/captures/{challengeId}
     */
    gameGetChallengeTraffic: (
      challengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<TeamTrafficModel[], RequestResponse>({
        path: `/api/game/captures/${challengeId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves the list of captured teams for a game challenge; requires Monitor permission
     *
     * @tags Game
     * @name GameGetChallengeTraffic
     * @summary Get team captures in a challenge
     * @request GET:/api/game/captures/{challengeId}
     */
    useGameGetChallengeTraffic: (
      challengeId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamTrafficModel[], RequestResponse>(
        doFetch ? `/api/game/captures/${challengeId}` : null,
        options,
      ),

    /**
     * @description Retrieves the list of captured teams for a game challenge; requires Monitor permission
     *
     * @tags Game
     * @name GameGetChallengeTraffic
     * @summary Get team captures in a challenge
     * @request GET:/api/game/captures/{challengeId}
     */
    mutateGameGetChallengeTraffic: (
      challengeId: number,
      data?: TeamTrafficModel[] | Promise<TeamTrafficModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamTrafficModel[]>(
        `/api/game/captures/${challengeId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Game
     * @name GameGetGameJoinCheckInfo
     * @summary Get check info for joining a game
     * @request GET:/api/game/{id}/check
     */
    gameGetGameJoinCheckInfo: (id: number, params: RequestParams = {}) =>
      this.request<GameJoinCheckInfoModel, RequestResponse>({
        path: `/api/game/${id}/check`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Game
     * @name GameGetGameJoinCheckInfo
     * @summary Get check info for joining a game
     * @request GET:/api/game/{id}/check
     */
    useGameGetGameJoinCheckInfo: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<GameJoinCheckInfoModel, RequestResponse>(
        doFetch ? `/api/game/${id}/check` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Game
     * @name GameGetGameJoinCheckInfo
     * @summary Get check info for joining a game
     * @request GET:/api/game/{id}/check
     */
    mutateGameGetGameJoinCheckInfo: (
      id: number,
      data?: GameJoinCheckInfoModel | Promise<GameJoinCheckInfoModel>,
      options?: MutatorOptions,
    ) => mutate<GameJoinCheckInfoModel>(`/api/game/${id}/check`, data, options),

    /**
     * @description Retrieves a traffic packet file; requires Monitor permission
     *
     * @tags Game
     * @name GameGetTeamTraffic
     * @summary Get a traffic file
     * @request GET:/api/game/captures/{challengeId}/{partId}/{filename}
     */
    gameGetTeamTraffic: (
      challengeId: number,
      partId: number,
      filename: string,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/game/captures/${challengeId}/${partId}/${filename}`,
        method: "GET",
        ...params,
      }),

    /**
     * @description Retrieves traffic packet files for a team and challenge; requires Monitor permission
     *
     * @tags Game
     * @name GameGetTeamTrafficAll
     * @summary Get traffic files
     * @request GET:/api/game/captures/{challengeId}/{partId}
     */
    gameGetTeamTrafficAll: (
      challengeId: number,
      partId: number,
      params: RequestParams = {},
    ) =>
      this.request<FileRecord[], RequestResponse>({
        path: `/api/game/captures/${challengeId}/${partId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves traffic packet files for a team and challenge; requires Monitor permission
     *
     * @tags Game
     * @name GameGetTeamTrafficAll
     * @summary Get traffic files
     * @request GET:/api/game/captures/{challengeId}/{partId}
     */
    useGameGetTeamTrafficAll: (
      challengeId: number,
      partId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<FileRecord[], RequestResponse>(
        doFetch ? `/api/game/captures/${challengeId}/${partId}` : null,
        options,
      ),

    /**
     * @description Retrieves traffic packet files for a team and challenge; requires Monitor permission
     *
     * @tags Game
     * @name GameGetTeamTrafficAll
     * @summary Get traffic files
     * @request GET:/api/game/captures/{challengeId}/{partId}
     */
    mutateGameGetTeamTrafficAll: (
      challengeId: number,
      partId: number,
      data?: FileRecord[] | Promise<FileRecord[]>,
      options?: MutatorOptions,
    ) =>
      mutate<FileRecord[]>(
        `/api/game/captures/${challengeId}/${partId}`,
        data,
        options,
      ),

    /**
     * @description Returns the current status of a Windows VM instance including RDP connection URL when ready.
     *
     * @tags Game
     * @name GameGetVmStatus
     * @summary Get VM instance status and RDP access URL
     * @request GET:/api/game/{id}/vm/{challengeId}
     */
    gameGetVmStatus: (
      id: number,
      challengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<VmStatusResponse, RequestResponse>({
        path: `/api/game/${id}/vm/${challengeId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Returns the current status of a Windows VM instance including RDP connection URL when ready.
     *
     * @tags Game
     * @name GameGetVmStatus
     * @summary Get VM instance status and RDP access URL
     * @request GET:/api/game/{id}/vm/{challengeId}
     */
    useGameGetVmStatus: (
      id: number,
      challengeId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<VmStatusResponse, RequestResponse>(
        doFetch ? `/api/game/${id}/vm/${challengeId}` : null,
        options,
      ),

    /**
     * @description Returns the current status of a Windows VM instance including RDP connection URL when ready.
     *
     * @tags Game
     * @name GameGetVmStatus
     * @summary Get VM instance status and RDP access URL
     * @request GET:/api/game/{id}/vm/{challengeId}
     */
    mutateGameGetVmStatus: (
      id: number,
      challengeId: number,
      data?: VmStatusResponse | Promise<VmStatusResponse>,
      options?: MutatorOptions,
    ) =>
      mutate<VmStatusResponse>(
        `/api/game/${id}/vm/${challengeId}`,
        data,
        options,
      ),

    /**
     * @description Retrieves post-game writeup submission information; requires User permission
     *
     * @tags Game
     * @name GameGetWriteup
     * @summary Get writeup information
     * @request GET:/api/game/{id}/writeup
     */
    gameGetWriteup: (id: number, params: RequestParams = {}) =>
      this.request<BasicWriteupInfoModel, RequestResponse>({
        path: `/api/game/${id}/writeup`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves post-game writeup submission information; requires User permission
     *
     * @tags Game
     * @name GameGetWriteup
     * @summary Get writeup information
     * @request GET:/api/game/{id}/writeup
     */
    useGameGetWriteup: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<BasicWriteupInfoModel, RequestResponse>(
        doFetch ? `/api/game/${id}/writeup` : null,
        options,
      ),

    /**
     * @description Retrieves post-game writeup submission information; requires User permission
     *
     * @tags Game
     * @name GameGetWriteup
     * @summary Get writeup information
     * @request GET:/api/game/{id}/writeup
     */
    mutateGameGetWriteup: (
      id: number,
      data?: BasicWriteupInfoModel | Promise<BasicWriteupInfoModel>,
      options?: MutatorOptions,
    ) =>
      mutate<BasicWriteupInfoModel>(`/api/game/${id}/writeup`, data, options),

    /**
     * @description Join a game; requires User permission
     *
     * @tags Game
     * @name GameJoinGame
     * @summary Join a game
     * @request POST:/api/game/{id}
     */
    gameJoinGame: (
      id: number,
      data: GameJoinModel,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/game/${id}`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Leave a game; requires User permission
     *
     * @tags Game
     * @name GameLeaveGame
     * @summary Leave a game
     * @request DELETE:/api/game/{id}
     */
    gameLeaveGame: (id: number, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/game/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * @description Retrieves game notice data
     *
     * @tags Game
     * @name GameNotices
     * @summary Get game notices
     * @request GET:/api/game/{id}/notices
     */
    gameNotices: (
      id: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @min 0
         * @max 300
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<GameNotice[], RequestResponse>({
        path: `/api/game/${id}/notices`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves game notice data
     *
     * @tags Game
     * @name GameNotices
     * @summary Get game notices
     * @request GET:/api/game/{id}/notices
     */
    useGameNotices: (
      id: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @min 0
         * @max 300
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<GameNotice[], RequestResponse>(
        doFetch ? [`/api/game/${id}/notices`, query] : null,
        options,
      ),

    /**
     * @description Retrieves game notice data
     *
     * @tags Game
     * @name GameNotices
     * @summary Get game notices
     * @request GET:/api/game/{id}/notices
     */
    mutateGameNotices: (
      id: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @min 0
         * @max 300
         * @default 0
         */
        skip?: number;
      },
      data?: GameNotice[] | Promise<GameNotice[]>,
      options?: MutatorOptions,
    ) =>
      mutate<GameNotice[]>([`/api/game/${id}/notices`, query], data, options),

    /**
     * @description Retrieves all participation information of the game; requires Admin permission
     *
     * @tags Game
     * @name GameParticipations
     * @summary Get all game participations
     * @request GET:/api/game/{id}/participations
     */
    gameParticipations: (id: number, params: RequestParams = {}) =>
      this.request<ParticipationInfoModel[], RequestResponse>({
        path: `/api/game/${id}/participations`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves all participation information of the game; requires Admin permission
     *
     * @tags Game
     * @name GameParticipations
     * @summary Get all game participations
     * @request GET:/api/game/{id}/participations
     */
    useGameParticipations: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ParticipationInfoModel[], RequestResponse>(
        doFetch ? `/api/game/${id}/participations` : null,
        options,
      ),

    /**
     * @description Retrieves all participation information of the game; requires Admin permission
     *
     * @tags Game
     * @name GameParticipations
     * @summary Get all game participations
     * @request GET:/api/game/{id}/participations
     */
    mutateGameParticipations: (
      id: number,
      data?: ParticipationInfoModel[] | Promise<ParticipationInfoModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<ParticipationInfoModel[]>(
        `/api/game/${id}/participations`,
        data,
        options,
      ),

    /**
     * @description Retrieves recent game in three weeks
     *
     * @tags Game
     * @name GameRecentGames
     * @summary Get the recent games
     * @request GET:/api/game/recent
     */
    gameRecentGames: (
      query?: {
        /**
         * Limit of the number of games
         * @format int32
         * @min 0
         * @max 50
         */
        limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<BasicGameInfoModel[], RequestResponse>({
        path: `/api/game/recent`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves recent game in three weeks
     *
     * @tags Game
     * @name GameRecentGames
     * @summary Get the recent games
     * @request GET:/api/game/recent
     */
    useGameRecentGames: (
      query?: {
        /**
         * Limit of the number of games
         * @format int32
         * @min 0
         * @max 50
         */
        limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<BasicGameInfoModel[], RequestResponse>(
        doFetch ? [`/api/game/recent`, query] : null,
        options,
      ),

    /**
     * @description Retrieves recent game in three weeks
     *
     * @tags Game
     * @name GameRecentGames
     * @summary Get the recent games
     * @request GET:/api/game/recent
     */
    mutateGameRecentGames: (
      query?: {
        /**
         * Limit of the number of games
         * @format int32
         * @min 0
         * @max 50
         */
        limit?: number;
      },
      data?: BasicGameInfoModel[] | Promise<BasicGameInfoModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<BasicGameInfoModel[]>([`/api/game/recent`, query], data, options),

    /**
     * @description Retrieves the scoreboard data
     *
     * @tags Game
     * @name GameScoreboard
     * @summary Get the scoreboard
     * @request GET:/api/game/{id}/scoreboard
     */
    gameScoreboard: (id: number, params: RequestParams = {}) =>
      this.request<ScoreboardModel, RequestResponse>({
        path: `/api/game/${id}/scoreboard`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves the scoreboard data
     *
     * @tags Game
     * @name GameScoreboard
     * @summary Get the scoreboard
     * @request GET:/api/game/{id}/scoreboard
     */
    useGameScoreboard: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ScoreboardModel, RequestResponse>(
        doFetch ? `/api/game/${id}/scoreboard` : null,
        options,
      ),

    /**
     * @description Retrieves the scoreboard data
     *
     * @tags Game
     * @name GameScoreboard
     * @summary Get the scoreboard
     * @request GET:/api/game/{id}/scoreboard
     */
    mutateGameScoreboard: (
      id: number,
      data?: ScoreboardModel | Promise<ScoreboardModel>,
      options?: MutatorOptions,
    ) => mutate<ScoreboardModel>(`/api/game/${id}/scoreboard`, data, options),

    /**
     * @description Downloads the game scoreboard; requires Monitor permission
     *
     * @tags Game
     * @name GameScoreboardSheet
     * @summary Downloads the scoreboard
     * @request GET:/api/game/{id}/scoreboardsheet
     */
    gameScoreboardSheet: (id: number, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/game/${id}/scoreboardsheet`,
        method: "GET",
        ...params,
      }),

    /**
     * @description Queries flag status; requires User permission
     *
     * @tags Game
     * @name GameStatus
     * @summary Queries flag status
     * @request GET:/api/game/{id}/challenges/{challengeId}/status/{submitId}
     */
    gameStatus: (
      id: number,
      challengeId: number,
      submitId: number,
      params: RequestParams = {},
    ) =>
      this.request<AnswerResult, RequestResponse>({
        path: `/api/game/${id}/challenges/${challengeId}/status/${submitId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Queries flag status; requires User permission
     *
     * @tags Game
     * @name GameStatus
     * @summary Queries flag status
     * @request GET:/api/game/{id}/challenges/{challengeId}/status/{submitId}
     */
    useGameStatus: (
      id: number,
      challengeId: number,
      submitId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AnswerResult, RequestResponse>(
        doFetch
          ? `/api/game/${id}/challenges/${challengeId}/status/${submitId}`
          : null,
        options,
      ),

    /**
     * @description Queries flag status; requires User permission
     *
     * @tags Game
     * @name GameStatus
     * @summary Queries flag status
     * @request GET:/api/game/{id}/challenges/{challengeId}/status/{submitId}
     */
    mutateGameStatus: (
      id: number,
      challengeId: number,
      submitId: number,
      data?: AnswerResult | Promise<AnswerResult>,
      options?: MutatorOptions,
    ) =>
      mutate<AnswerResult>(
        `/api/game/${id}/challenges/${challengeId}/status/${submitId}`,
        data,
        options,
      ),

    /**
     * @description Retrieves game submission data; requires Monitor permission
     *
     * @tags Game
     * @name GameSubmissions
     * @summary Get game submissions
     * @request GET:/api/game/{id}/submissions
     */
    gameSubmissions: (
      id: number,
      query?: {
        /** Submission type */
        type?: AnswerResult | null;
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<Submission[], RequestResponse>({
        path: `/api/game/${id}/submissions`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Retrieves game submission data; requires Monitor permission
     *
     * @tags Game
     * @name GameSubmissions
     * @summary Get game submissions
     * @request GET:/api/game/{id}/submissions
     */
    useGameSubmissions: (
      id: number,
      query?: {
        /** Submission type */
        type?: AnswerResult | null;
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Submission[], RequestResponse>(
        doFetch ? [`/api/game/${id}/submissions`, query] : null,
        options,
      ),

    /**
     * @description Retrieves game submission data; requires Monitor permission
     *
     * @tags Game
     * @name GameSubmissions
     * @summary Get game submissions
     * @request GET:/api/game/{id}/submissions
     */
    mutateGameSubmissions: (
      id: number,
      query?: {
        /** Submission type */
        type?: AnswerResult | null;
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 100
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?: Submission[] | Promise<Submission[]>,
      options?: MutatorOptions,
    ) =>
      mutate<Submission[]>(
        [`/api/game/${id}/submissions`, query],
        data,
        options,
      ),

    /**
     * @description Downloads all submissions of the game; requires Monitor permission
     *
     * @tags Game
     * @name GameSubmissionSheet
     * @summary Downloads all submissions
     * @request GET:/api/game/{id}/submissionsheet
     */
    gameSubmissionSheet: (id: number, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/game/${id}/submissionsheet`,
        method: "GET",
        ...params,
      }),

    /**
     * @description Submits a flag; requires User permission and active team participation
     *
     * @tags Game
     * @name GameSubmit
     * @summary Submits a flag
     * @request POST:/api/game/{id}/challenges/{challengeId}
     */
    gameSubmit: (
      id: number,
      challengeId: number,
      data: FlagSubmitModel,
      params: RequestParams = {},
    ) =>
      this.request<FlagSubmitResultModel, RequestResponse>({
        path: `/api/game/${id}/challenges/${challengeId}`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Submits a post-game writeup; requires User permission
     *
     * @tags Game
     * @name GameSubmitWriteup
     * @summary Submits a writeup
     * @request POST:/api/game/{id}/writeup
     */
    gameSubmitWriteup: (
      id: number,
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/game/${id}/writeup`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        ...params,
      }),
  };
  gamePhase = {
    /**
     * No description
     *
     * @tags GamePhase
     * @name GamePhaseCreate
     * @request POST:/api/v1/phases/{gameId}
     */
    gamePhaseCreate: (
      gameId: number,
      data: GamePhase,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/phases/${gameId}`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags GamePhase
     * @name GamePhaseDelete
     * @request DELETE:/api/v1/phases/{id}
     */
    gamePhaseDelete: (id: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/phases/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags GamePhase
     * @name GamePhaseList
     * @request GET:/api/v1/phases/{gameId}
     */
    gamePhaseList: (gameId: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/phases/${gameId}`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags GamePhase
     * @name GamePhaseList
     * @request GET:/api/v1/phases/{gameId}
     */
    useGamePhaseList: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(doFetch ? `/api/v1/phases/${gameId}` : null, options),

    /**
     * No description
     *
     * @tags GamePhase
     * @name GamePhaseList
     * @request GET:/api/v1/phases/{gameId}
     */
    mutateGamePhaseList: (
      gameId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/v1/phases/${gameId}`, data, options),

    /**
     * No description
     *
     * @tags GamePhase
     * @name GamePhaseUpdate
     * @request PUT:/api/v1/phases/{id}
     */
    gamePhaseUpdate: (
      id: number,
      data: GamePhase,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/phases/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),
  };
  imageTemplate = {
    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateDelete
     * @summary Delete an image template and its stored file.
     * @request DELETE:/api/v1/image-templates/{id}
     */
    imageTemplateDelete: (id: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateDownloadByHash
     * @request GET:/api/v1/image-templates/download/{hash}
     */
    imageTemplateDownloadByHash: (
      hash: string,
      query?: {
        /** @format guid */
        nodeId?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/download/${hash}`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateDownloadByHash
     * @request GET:/api/v1/image-templates/download/{hash}
     */
    useImageTemplateDownloadByHash: (
      hash: string,
      query?: {
        /** @format guid */
        nodeId?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? [`/api/v1/image-templates/download/${hash}`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateDownloadByHash
     * @request GET:/api/v1/image-templates/download/{hash}
     */
    mutateImageTemplateDownloadByHash: (
      hash: string,
      query?: {
        /** @format guid */
        nodeId?: string | null;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        [`/api/v1/image-templates/download/${hash}`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateGetById
     * @summary Get a specific image template by ID.
     * @request GET:/api/v1/image-templates/{id}
     */
    imageTemplateGetById: (id: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/${id}`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateGetById
     * @summary Get a specific image template by ID.
     * @request GET:/api/v1/image-templates/{id}
     */
    useImageTemplateGetById: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/v1/image-templates/${id}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateGetById
     * @summary Get a specific image template by ID.
     * @request GET:/api/v1/image-templates/{id}
     */
    mutateImageTemplateGetById: (
      id: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/v1/image-templates/${id}`, data, options),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateGetDockerRegistrySettings
     * @request GET:/api/v1/image-templates/docker-registry
     */
    imageTemplateGetDockerRegistrySettings: (params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/docker-registry`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateGetDockerRegistrySettings
     * @request GET:/api/v1/image-templates/docker-registry
     */
    useImageTemplateGetDockerRegistrySettings: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/v1/image-templates/docker-registry` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateGetDockerRegistrySettings
     * @request GET:/api/v1/image-templates/docker-registry
     */
    mutateImageTemplateGetDockerRegistrySettings: (
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/v1/image-templates/docker-registry`, data, options),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateImportFromLocal
     * @summary Import VM image from local filesystem path.
     * @request POST:/api/v1/image-templates/import-local
     */
    imageTemplateImportFromLocal: (
      data: LocalImportRequest,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/import-local`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateList
     * @summary List all image templates with optional filtering.
     * @request GET:/api/v1/image-templates
     */
    imageTemplateList: (
      query?: {
        osType?: OSType | null;
        imageType?: ImageType | null;
        search?: string | null;
        /**
         * @format int32
         * @default 1
         */
        page?: number;
        /**
         * @format int32
         * @default 20
         */
        pageSize?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateList
     * @summary List all image templates with optional filtering.
     * @request GET:/api/v1/image-templates
     */
    useImageTemplateList: (
      query?: {
        osType?: OSType | null;
        imageType?: ImageType | null;
        search?: string | null;
        /**
         * @format int32
         * @default 1
         */
        page?: number;
        /**
         * @format int32
         * @default 20
         */
        pageSize?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? [`/api/v1/image-templates`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateList
     * @summary List all image templates with optional filtering.
     * @request GET:/api/v1/image-templates
     */
    mutateImageTemplateList: (
      query?: {
        osType?: OSType | null;
        imageType?: ImageType | null;
        search?: string | null;
        /**
         * @format int32
         * @default 1
         */
        page?: number;
        /**
         * @format int32
         * @default 20
         */
        pageSize?: number;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>([`/api/v1/image-templates`, query], data, options),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateRegisterDocker
     * @summary Register a Docker image template from a registry URL.
     * @request POST:/api/v1/image-templates/register-docker
     */
    imageTemplateRegisterDocker: (
      data: DockerRegisterRequest,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/register-docker`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateUpload
     * @summary Upload a VM disk image file.
     * @request POST:/api/v1/image-templates
     */
    imageTemplateUpload: (
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        ...params,
      }),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateUploadArchive
     * @summary Upload a VM image archive file (.zip, .tar.gz, .tar.xz).
     * @request POST:/api/v1/image-templates/upload
     */
    imageTemplateUploadArchive: (
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/upload`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        ...params,
      }),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateUploadDockerArchive
     * @summary Upload a docker save archive and push it to the configured internal registry.
     * @request POST:/api/v1/image-templates/upload-docker
     */
    imageTemplateUploadDockerArchive: (
      data: {
        ContentType?: string | null;
        ContentDisposition?: string | null;
        Headers?: any[] | null;
        /** @format int64 */
        Length?: number;
        Name?: string | null;
        FileName?: string | null;
        name?: string | null;
        repository?: string | null;
        tag?: string | null;
        sourceImage?: string | null;
        osType?: OSType;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/upload-docker`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        ...params,
      }),
  };
  info = {
    /**
     * @description Get Captcha configuration
     *
     * @tags Info
     * @name InfoGetClientCaptchaInfo
     * @summary Get Captcha configuration
     * @request GET:/api/captcha
     */
    infoGetClientCaptchaInfo: (params: RequestParams = {}) =>
      this.request<ClientCaptchaInfoModel, any>({
        path: `/api/captcha`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Get Captcha configuration
     *
     * @tags Info
     * @name InfoGetClientCaptchaInfo
     * @summary Get Captcha configuration
     * @request GET:/api/captcha
     */
    useInfoGetClientCaptchaInfo: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ClientCaptchaInfoModel, any>(
        doFetch ? `/api/captcha` : null,
        options,
      ),

    /**
     * @description Get Captcha configuration
     *
     * @tags Info
     * @name InfoGetClientCaptchaInfo
     * @summary Get Captcha configuration
     * @request GET:/api/captcha
     */
    mutateInfoGetClientCaptchaInfo: (
      data?: ClientCaptchaInfoModel | Promise<ClientCaptchaInfoModel>,
      options?: MutatorOptions,
    ) => mutate<ClientCaptchaInfoModel>(`/api/captcha`, data, options),

    /**
     * @description Get client configuration
     *
     * @tags Info
     * @name InfoGetClientConfig
     * @summary Get client configuration
     * @request GET:/api/config
     */
    infoGetClientConfig: (params: RequestParams = {}) =>
      this.request<ClientConfig, any>({
        path: `/api/config`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Get client configuration
     *
     * @tags Info
     * @name InfoGetClientConfig
     * @summary Get client configuration
     * @request GET:/api/config
     */
    useInfoGetClientConfig: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) => useSWR<ClientConfig, any>(doFetch ? `/api/config` : null, options),

    /**
     * @description Get client configuration
     *
     * @tags Info
     * @name InfoGetClientConfig
     * @summary Get client configuration
     * @request GET:/api/config
     */
    mutateInfoGetClientConfig: (
      data?: ClientConfig | Promise<ClientConfig>,
      options?: MutatorOptions,
    ) => mutate<ClientConfig>(`/api/config`, data, options),

    /**
     * @description Get the latest posts
     *
     * @tags Info
     * @name InfoGetLatestPosts
     * @summary Get the latest posts
     * @request GET:/api/posts/latest
     */
    infoGetLatestPosts: (params: RequestParams = {}) =>
      this.request<PostInfoModel[], any>({
        path: `/api/posts/latest`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Get the latest posts
     *
     * @tags Info
     * @name InfoGetLatestPosts
     * @summary Get the latest posts
     * @request GET:/api/posts/latest
     */
    useInfoGetLatestPosts: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PostInfoModel[], any>(
        doFetch ? `/api/posts/latest` : null,
        options,
      ),

    /**
     * @description Get the latest posts
     *
     * @tags Info
     * @name InfoGetLatestPosts
     * @summary Get the latest posts
     * @request GET:/api/posts/latest
     */
    mutateInfoGetLatestPosts: (
      data?: PostInfoModel[] | Promise<PostInfoModel[]>,
      options?: MutatorOptions,
    ) => mutate<PostInfoModel[]>(`/api/posts/latest`, data, options),

    /**
     * @description Get post details
     *
     * @tags Info
     * @name InfoGetPost
     * @summary Get post details
     * @request GET:/api/posts/{id}
     */
    infoGetPost: (id: string, params: RequestParams = {}) =>
      this.request<PostDetailModel, RequestResponse>({
        path: `/api/posts/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Get post details
     *
     * @tags Info
     * @name InfoGetPost
     * @summary Get post details
     * @request GET:/api/posts/{id}
     */
    useInfoGetPost: (
      id: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PostDetailModel, RequestResponse>(
        doFetch ? `/api/posts/${id}` : null,
        options,
      ),

    /**
     * @description Get post details
     *
     * @tags Info
     * @name InfoGetPost
     * @summary Get post details
     * @request GET:/api/posts/{id}
     */
    mutateInfoGetPost: (
      id: string,
      data?: PostDetailModel | Promise<PostDetailModel>,
      options?: MutatorOptions,
    ) => mutate<PostDetailModel>(`/api/posts/${id}`, data, options),

    /**
     * @description Get all posts
     *
     * @tags Info
     * @name InfoGetPosts
     * @summary Get all posts
     * @request GET:/api/posts
     */
    infoGetPosts: (params: RequestParams = {}) =>
      this.request<PostInfoModel[], any>({
        path: `/api/posts`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Get all posts
     *
     * @tags Info
     * @name InfoGetPosts
     * @summary Get all posts
     * @request GET:/api/posts
     */
    useInfoGetPosts: (options?: SWRConfiguration, doFetch: boolean = true) =>
      useSWR<PostInfoModel[], any>(doFetch ? `/api/posts` : null, options),

    /**
     * @description Get all posts
     *
     * @tags Info
     * @name InfoGetPosts
     * @summary Get all posts
     * @request GET:/api/posts
     */
    mutateInfoGetPosts: (
      data?: PostInfoModel[] | Promise<PostInfoModel[]>,
      options?: MutatorOptions,
    ) => mutate<PostInfoModel[]>(`/api/posts`, data, options),

    /**
     * @description Create Pow Captcha, valid for 5 minutes
     *
     * @tags Info
     * @name InfoPowChallenge
     * @summary Create Pow Captcha
     * @request GET:/api/captcha/powchallenge
     */
    infoPowChallenge: (params: RequestParams = {}) =>
      this.request<HashPowChallenge, RequestResponse>({
        path: `/api/captcha/powchallenge`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Create Pow Captcha, valid for 5 minutes
     *
     * @tags Info
     * @name InfoPowChallenge
     * @summary Create Pow Captcha
     * @request GET:/api/captcha/powchallenge
     */
    useInfoPowChallenge: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<HashPowChallenge, RequestResponse>(
        doFetch ? `/api/captcha/powchallenge` : null,
        options,
      ),

    /**
     * @description Create Pow Captcha, valid for 5 minutes
     *
     * @tags Info
     * @name InfoPowChallenge
     * @summary Create Pow Captcha
     * @request GET:/api/captcha/powchallenge
     */
    mutateInfoPowChallenge: (
      data?: HashPowChallenge | Promise<HashPowChallenge>,
      options?: MutatorOptions,
    ) => mutate<HashPowChallenge>(`/api/captcha/powchallenge`, data, options),
  };
  internal = {
    /**
     * No description
     *
     * @tags Internal
     * @name InternalGetPortMap
     * @summary 获取所有活跃容器的端口映射（用于 Nginx stream 配置同步）
     * @request GET:/api/internal/port-map
     */
    internalGetPortMap: (params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/internal/port-map`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Internal
     * @name InternalGetPortMap
     * @summary 获取所有活跃容器的端口映射（用于 Nginx stream 配置同步）
     * @request GET:/api/internal/port-map
     */
    useInternalGetPortMap: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) => useSWR<Blob, any>(doFetch ? `/api/internal/port-map` : null, options),

    /**
     * No description
     *
     * @tags Internal
     * @name InternalGetPortMap
     * @summary 获取所有活跃容器的端口映射（用于 Nginx stream 配置同步）
     * @request GET:/api/internal/port-map
     */
    mutateInternalGetPortMap: (
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/internal/port-map`, data, options),

    /**
     * No description
     *
     * @tags Internal
     * @name InternalGetTeamLabUdpMap
     * @summary Get active TeamLab WireGuard UDP mappings for a public UDP gateway.
     * @request GET:/api/internal/teamlab-udp-map
     */
    internalGetTeamLabUdpMap: (params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/internal/teamlab-udp-map`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Internal
     * @name InternalGetTeamLabUdpMap
     * @summary Get active TeamLab WireGuard UDP mappings for a public UDP gateway.
     * @request GET:/api/internal/teamlab-udp-map
     */
    useInternalGetTeamLabUdpMap: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/internal/teamlab-udp-map` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Internal
     * @name InternalGetTeamLabUdpMap
     * @summary Get active TeamLab WireGuard UDP mappings for a public UDP gateway.
     * @request GET:/api/internal/teamlab-udp-map
     */
    mutateInternalGetTeamLabUdpMap: (
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/internal/teamlab-udp-map`, data, options),
  };
  nodes = {
    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDeregister
     * @request DELETE:/api/v1/nodes/{id}
     */
    nodesDeregister: (
      id: string,
      query?: {
        /** @default false */
        force?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes/${id}`,
        method: "DELETE",
        query: query,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDestroyVm
     * @request DELETE:/api/v1/nodes/vms/{instanceId}
     */
    nodesDestroyVm: (instanceId: string, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes/vms/${instanceId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDestroyVmAsAdmin
     * @request DELETE:/api/v1/nodes/vms/{instanceId}/admin
     */
    nodesDestroyVmAsAdmin: (instanceId: string, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes/vms/${instanceId}/admin`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDetail
     * @request GET:/api/v1/nodes/{id}
     */
    nodesDetail: (id: string, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes/${id}`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDetail
     * @request GET:/api/v1/nodes/{id}
     */
    useNodesDetail: (
      id: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) => useSWR<Blob, any>(doFetch ? `/api/v1/nodes/${id}` : null, options),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDetail
     * @request GET:/api/v1/nodes/{id}
     */
    mutateNodesDetail: (
      id: string,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/v1/nodes/${id}`, data, options),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadAgent
     * @request GET:/api/agent/download
     */
    nodesDownloadAgent: (params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/agent/download`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadAgent
     * @request GET:/api/agent/download
     */
    useNodesDownloadAgent: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) => useSWR<Blob, any>(doFetch ? `/api/agent/download` : null, options),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadAgent
     * @request GET:/api/agent/download
     */
    mutateNodesDownloadAgent: (
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/agent/download`, data, options),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesEnableTeamLabNetwork
     * @request POST:/api/v1/nodes/{id}/teamlab/enable
     */
    nodesEnableTeamLabNetwork: (
      id: string,
      data: EnableTeamLabNetworkRequest,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes/${id}/teamlab/enable`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesHeartbeat
     * @request POST:/api/v1/nodes/{id}/heartbeat
     */
    nodesHeartbeat: (
      id: string,
      data: HeartbeatRequest,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes/${id}/heartbeat`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesList
     * @request GET:/api/v1/nodes
     */
    nodesList: (params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Nodes
     * @name NodesList
     * @request GET:/api/v1/nodes
     */
    useNodesList: (options?: SWRConfiguration, doFetch: boolean = true) =>
      useSWR<Blob, any>(doFetch ? `/api/v1/nodes` : null, options),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesList
     * @request GET:/api/v1/nodes
     */
    mutateNodesList: (data?: Blob | Promise<Blob>, options?: MutatorOptions) =>
      mutate<Blob>(`/api/v1/nodes`, data, options),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesRegister
     * @request POST:/api/v1/nodes
     */
    nodesRegister: (data: NodeDeployRequest, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesResources
     * @request GET:/api/v1/nodes/{id}/resources
     */
    nodesResources: (
      id: string,
      query?: {
        /** @default "all" */
        type?: string;
        /** @default "all" */
        status?: string;
        /**
         * @format int32
         * @default 1
         */
        page?: number;
        /**
         * @format int32
         * @default 12
         */
        pageSize?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes/${id}/resources`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags Nodes
     * @name NodesResources
     * @request GET:/api/v1/nodes/{id}/resources
     */
    useNodesResources: (
      id: string,
      query?: {
        /** @default "all" */
        type?: string;
        /** @default "all" */
        status?: string;
        /**
         * @format int32
         * @default 1
         */
        page?: number;
        /**
         * @format int32
         * @default 12
         */
        pageSize?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? [`/api/v1/nodes/${id}/resources`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesResources
     * @request GET:/api/v1/nodes/{id}/resources
     */
    mutateNodesResources: (
      id: string,
      query?: {
        /** @default "all" */
        type?: string;
        /** @default "all" */
        status?: string;
        /**
         * @format int32
         * @default 1
         */
        page?: number;
        /**
         * @format int32
         * @default 12
         */
        pageSize?: number;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>([`/api/v1/nodes/${id}/resources`, query], data, options),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesSyncAgent
     * @request POST:/api/v1/nodes/{id}/sync-agent
     */
    nodesSyncAgent: (id: string, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes/${id}/sync-agent`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesUpdateNode
     * @request PATCH:/api/v1/nodes/{id}
     */
    nodesUpdateNode: (
      id: string,
      data: UpdateNodeRequest,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/nodes/${id}`,
        method: "PATCH",
        body: data,
        type: ContentType.Json,
        ...params,
      }),
  };
  deploymentTargets = {
    /**
     * No description
     *
     * @tags DeploymentTargets
     * @name DeploymentTargetsCancel
     * @request DELETE:/api/v1/deployment-targets/{id}
     */
    deploymentTargetsCancel: (id: string, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/deployment-targets/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DeploymentTargets
     * @name DeploymentTargetsGetById
     * @request GET:/api/v1/deployment-targets/{id}
     */
    deploymentTargetsGetById: (id: string, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/deployment-targets/${id}`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags DeploymentTargets
     * @name DeploymentTargetsGetById
     * @request GET:/api/v1/deployment-targets/{id}
     */
    useDeploymentTargetsGetById: (
      id: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/v1/deployment-targets/${id}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags DeploymentTargets
     * @name DeploymentTargetsGetById
     * @request GET:/api/v1/deployment-targets/{id}
     */
    mutateDeploymentTargetsGetById: (
      id: string,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/v1/deployment-targets/${id}`, data, options),

    /**
     * No description
     *
     * @tags DeploymentTargets
     * @name DeploymentTargetsList
     * @request GET:/api/v1/deployment-targets
     */
    deploymentTargetsList: (
      query?: {
        status?: string | null;
        /**
         * @format int32
         * @default 1
         */
        page?: number;
        /**
         * @format int32
         * @default 20
         */
        pageSize?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/deployment-targets`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags DeploymentTargets
     * @name DeploymentTargetsList
     * @request GET:/api/v1/deployment-targets
     */
    useDeploymentTargetsList: (
      query?: {
        status?: string | null;
        /**
         * @format int32
         * @default 1
         */
        page?: number;
        /**
         * @format int32
         * @default 20
         */
        pageSize?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? [`/api/v1/deployment-targets`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags DeploymentTargets
     * @name DeploymentTargetsList
     * @request GET:/api/v1/deployment-targets
     */
    mutateDeploymentTargetsList: (
      query?: {
        status?: string | null;
        /**
         * @format int32
         * @default 1
         */
        page?: number;
        /**
         * @format int32
         * @default 20
         */
        pageSize?: number;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>([`/api/v1/deployment-targets`, query], data, options),
  };
  penetrationAdmin = {
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminCancelDeploy
     * @request POST:/api/admin/pentest/games/{gameId}/deploy/cancel
     */
    penetrationAdminCancelDeploy: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/deploy/cancel`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminCleanupTeam
     * @request POST:/api/admin/pentest/games/{gameId}/teams/{teamId}/cleanup
     */
    penetrationAdminCleanupTeam: (
      gameId: number,
      teamId: number,
      params: RequestParams = {},
    ) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/teams/${teamId}/cleanup`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminDeploy
     * @request POST:/api/admin/pentest/games/{gameId}/deploy
     */
    penetrationAdminDeploy: (
      gameId: number,
      query?: {
        /** @default false */
        force?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/deploy`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetAccess
     * @request GET:/api/admin/pentest/games/{gameId}/access
     */
    penetrationAdminGetAccess: (gameId: number, params: RequestParams = {}) =>
      this.request<PenetrationAdminAccessModel[], RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/access`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetAccess
     * @request GET:/api/admin/pentest/games/{gameId}/access
     */
    usePenetrationAdminGetAccess: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PenetrationAdminAccessModel[], RequestResponse>(
        doFetch ? `/api/admin/pentest/games/${gameId}/access` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetAccess
     * @request GET:/api/admin/pentest/games/{gameId}/access
     */
    mutatePenetrationAdminGetAccess: (
      gameId: number,
      data?:
        | PenetrationAdminAccessModel[]
        | Promise<PenetrationAdminAccessModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<PenetrationAdminAccessModel[]>(
        `/api/admin/pentest/games/${gameId}/access`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetConfig
     * @request GET:/api/admin/pentest/games/{gameId}
     */
    penetrationAdminGetConfig: (gameId: number, params: RequestParams = {}) =>
      this.request<PenetrationConfigModel, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetConfig
     * @request GET:/api/admin/pentest/games/{gameId}
     */
    usePenetrationAdminGetConfig: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PenetrationConfigModel, RequestResponse>(
        doFetch ? `/api/admin/pentest/games/${gameId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetConfig
     * @request GET:/api/admin/pentest/games/{gameId}
     */
    mutatePenetrationAdminGetConfig: (
      gameId: number,
      data?: PenetrationConfigModel | Promise<PenetrationConfigModel>,
      options?: MutatorOptions,
    ) =>
      mutate<PenetrationConfigModel>(
        `/api/admin/pentest/games/${gameId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetDeploymentEvents
     * @request GET:/api/admin/pentest/games/{gameId}/deployment-events
     */
    penetrationAdminGetDeploymentEvents: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 200
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
        /** @format int32 */
        environmentId?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        ArrayResponseOfPenetrationDeploymentEventModel,
        RequestResponse
      >({
        path: `/api/admin/pentest/games/${gameId}/deployment-events`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetDeploymentEvents
     * @request GET:/api/admin/pentest/games/{gameId}/deployment-events
     */
    usePenetrationAdminGetDeploymentEvents: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 200
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
        /** @format int32 */
        environmentId?: number | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfPenetrationDeploymentEventModel, RequestResponse>(
        doFetch
          ? [`/api/admin/pentest/games/${gameId}/deployment-events`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetDeploymentEvents
     * @request GET:/api/admin/pentest/games/{gameId}/deployment-events
     */
    mutatePenetrationAdminGetDeploymentEvents: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 200
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
        /** @format int32 */
        environmentId?: number | null;
      },
      data?:
        | ArrayResponseOfPenetrationDeploymentEventModel
        | Promise<ArrayResponseOfPenetrationDeploymentEventModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfPenetrationDeploymentEventModel>(
        [`/api/admin/pentest/games/${gameId}/deployment-events`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetPlan
     * @request POST:/api/admin/pentest/games/{gameId}/plan
     */
    penetrationAdminGetPlan: (
      gameId: number,
      data: PenetrationConfigModel,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationPlanModel, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/plan`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetScoreboard
     * @request GET:/api/admin/pentest/games/{gameId}/scoreboard
     */
    penetrationAdminGetScoreboard: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationScoreboardItemModel[], RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/scoreboard`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetScoreboard
     * @request GET:/api/admin/pentest/games/{gameId}/scoreboard
     */
    usePenetrationAdminGetScoreboard: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PenetrationScoreboardItemModel[], RequestResponse>(
        doFetch ? `/api/admin/pentest/games/${gameId}/scoreboard` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetScoreboard
     * @request GET:/api/admin/pentest/games/{gameId}/scoreboard
     */
    mutatePenetrationAdminGetScoreboard: (
      gameId: number,
      data?:
        | PenetrationScoreboardItemModel[]
        | Promise<PenetrationScoreboardItemModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<PenetrationScoreboardItemModel[]>(
        `/api/admin/pentest/games/${gameId}/scoreboard`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetSubmissions
     * @request GET:/api/admin/pentest/games/{gameId}/submissions
     */
    penetrationAdminGetSubmissions: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        ArrayResponseOfPenetrationSubmissionLogModel,
        RequestResponse
      >({
        path: `/api/admin/pentest/games/${gameId}/submissions`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetSubmissions
     * @request GET:/api/admin/pentest/games/{gameId}/submissions
     */
    usePenetrationAdminGetSubmissions: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ArrayResponseOfPenetrationSubmissionLogModel, RequestResponse>(
        doFetch
          ? [`/api/admin/pentest/games/${gameId}/submissions`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetSubmissions
     * @request GET:/api/admin/pentest/games/{gameId}/submissions
     */
    mutatePenetrationAdminGetSubmissions: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 0
         * @max 100
         * @default 50
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?:
        | ArrayResponseOfPenetrationSubmissionLogModel
        | Promise<ArrayResponseOfPenetrationSubmissionLogModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ArrayResponseOfPenetrationSubmissionLogModel>(
        [`/api/admin/pentest/games/${gameId}/submissions`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetTeamAccess
     * @request GET:/api/admin/pentest/games/{gameId}/teams/{teamId}/access
     */
    penetrationAdminGetTeamAccess: (
      gameId: number,
      teamId: number,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationAdminAccessModel[], RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/teams/${teamId}/access`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetTeamAccess
     * @request GET:/api/admin/pentest/games/{gameId}/teams/{teamId}/access
     */
    usePenetrationAdminGetTeamAccess: (
      gameId: number,
      teamId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PenetrationAdminAccessModel[], RequestResponse>(
        doFetch
          ? `/api/admin/pentest/games/${gameId}/teams/${teamId}/access`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetTeamAccess
     * @request GET:/api/admin/pentest/games/{gameId}/teams/{teamId}/access
     */
    mutatePenetrationAdminGetTeamAccess: (
      gameId: number,
      teamId: number,
      data?:
        | PenetrationAdminAccessModel[]
        | Promise<PenetrationAdminAccessModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<PenetrationAdminAccessModel[]>(
        `/api/admin/pentest/games/${gameId}/teams/${teamId}/access`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetTeamEnvironments
     * @request GET:/api/admin/pentest/games/{gameId}/environments
     */
    penetrationAdminGetTeamEnvironments: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationTeamEnvironmentModel[], RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/environments`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetTeamEnvironments
     * @request GET:/api/admin/pentest/games/{gameId}/environments
     */
    usePenetrationAdminGetTeamEnvironments: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PenetrationTeamEnvironmentModel[], RequestResponse>(
        doFetch ? `/api/admin/pentest/games/${gameId}/environments` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetTeamEnvironments
     * @request GET:/api/admin/pentest/games/{gameId}/environments
     */
    mutatePenetrationAdminGetTeamEnvironments: (
      gameId: number,
      data?:
        | PenetrationTeamEnvironmentModel[]
        | Promise<PenetrationTeamEnvironmentModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<PenetrationTeamEnvironmentModel[]>(
        `/api/admin/pentest/games/${gameId}/environments`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminPublish
     * @request POST:/api/admin/pentest/games/{gameId}/publish
     */
    penetrationAdminPublish: (gameId: number, params: RequestParams = {}) =>
      this.request<PenetrationConfigModel, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/publish`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminRebuildTeam
     * @request POST:/api/admin/pentest/games/{gameId}/teams/{teamId}/rebuild
     */
    penetrationAdminRebuildTeam: (
      gameId: number,
      teamId: number,
      params: RequestParams = {},
    ) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/teams/${teamId}/rebuild`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminRebuildTeamByRuntimeNode
     * @request POST:/api/admin/pentest/runtime-nodes/{runtimeNodeId}/rebuild-team
     */
    penetrationAdminRebuildTeamByRuntimeNode: (
      runtimeNodeId: number,
      params: RequestParams = {},
    ) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/admin/pentest/runtime-nodes/${runtimeNodeId}/rebuild-team`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminRestartRuntimeNode
     * @request POST:/api/admin/pentest/runtime-nodes/{runtimeNodeId}/restart
     */
    penetrationAdminRestartRuntimeNode: (
      runtimeNodeId: number,
      params: RequestParams = {},
    ) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/admin/pentest/runtime-nodes/${runtimeNodeId}/restart`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminSaveConfig
     * @request PUT:/api/admin/pentest/games/{gameId}
     */
    penetrationAdminSaveConfig: (
      gameId: number,
      data: PenetrationConfigModel,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationConfigModel, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminStop
     * @request POST:/api/admin/pentest/games/{gameId}/stop
     */
    penetrationAdminStop: (gameId: number, params: RequestParams = {}) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/stop`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminValidate
     * @request POST:/api/admin/pentest/games/{gameId}/validate
     */
    penetrationAdminValidate: (
      gameId: number,
      data: PenetrationConfigModel,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationValidationModel, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/validate`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  penetrationPlayer = {
    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerGetScoreboard
     * @request GET:/api/pentest/games/{gameId}/scoreboard
     */
    penetrationPlayerGetScoreboard: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationScoreboardItemModel[], RequestResponse>({
        path: `/api/pentest/games/${gameId}/scoreboard`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerGetScoreboard
     * @request GET:/api/pentest/games/{gameId}/scoreboard
     */
    usePenetrationPlayerGetScoreboard: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PenetrationScoreboardItemModel[], RequestResponse>(
        doFetch ? `/api/pentest/games/${gameId}/scoreboard` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerGetScoreboard
     * @request GET:/api/pentest/games/{gameId}/scoreboard
     */
    mutatePenetrationPlayerGetScoreboard: (
      gameId: number,
      data?:
        | PenetrationScoreboardItemModel[]
        | Promise<PenetrationScoreboardItemModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<PenetrationScoreboardItemModel[]>(
        `/api/pentest/games/${gameId}/scoreboard`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerGetTeamLabVpnConfig
     * @request GET:/api/pentest/games/{gameId}/teamlab/vpn-config
     */
    penetrationPlayerGetTeamLabVpnConfig: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabClientConfigModel, RequestResponse>({
        path: `/api/pentest/games/${gameId}/teamlab/vpn-config`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerGetTeamLabVpnConfig
     * @request GET:/api/pentest/games/{gameId}/teamlab/vpn-config
     */
    usePenetrationPlayerGetTeamLabVpnConfig: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabClientConfigModel, RequestResponse>(
        doFetch ? `/api/pentest/games/${gameId}/teamlab/vpn-config` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerGetTeamLabVpnConfig
     * @request GET:/api/pentest/games/{gameId}/teamlab/vpn-config
     */
    mutatePenetrationPlayerGetTeamLabVpnConfig: (
      gameId: number,
      data?: TeamLabClientConfigModel | Promise<TeamLabClientConfigModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabClientConfigModel>(
        `/api/pentest/games/${gameId}/teamlab/vpn-config`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerGetWorkspace
     * @request GET:/api/pentest/games/{gameId}/workspace
     */
    penetrationPlayerGetWorkspace: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationWorkspaceModel, RequestResponse>({
        path: `/api/pentest/games/${gameId}/workspace`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerGetWorkspace
     * @request GET:/api/pentest/games/{gameId}/workspace
     */
    usePenetrationPlayerGetWorkspace: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PenetrationWorkspaceModel, RequestResponse>(
        doFetch ? `/api/pentest/games/${gameId}/workspace` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerGetWorkspace
     * @request GET:/api/pentest/games/{gameId}/workspace
     */
    mutatePenetrationPlayerGetWorkspace: (
      gameId: number,
      data?: PenetrationWorkspaceModel | Promise<PenetrationWorkspaceModel>,
      options?: MutatorOptions,
    ) =>
      mutate<PenetrationWorkspaceModel>(
        `/api/pentest/games/${gameId}/workspace`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerReset
     * @request POST:/api/pentest/games/{gameId}/reset
     */
    penetrationPlayerReset: (gameId: number, params: RequestParams = {}) =>
      this.request<RequestResponse, RequestResponse>({
        path: `/api/pentest/games/${gameId}/reset`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerSubmit
     * @request POST:/api/pentest/games/{gameId}/submit
     */
    penetrationPlayerSubmit: (
      gameId: number,
      data: PenetrationSubmitModel,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationSubmitResultModel, RequestResponse>({
        path: `/api/pentest/games/${gameId}/submit`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  proxy = {
    /**
     * No description
     *
     * @tags Proxy
     * @name ProxyProxyForInstance
     * @summary Proxy TCP over websocket
     * @request GET:/api/proxy/{id}
     */
    proxyProxyForInstance: (id: string, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/proxy/${id}`,
        method: "GET",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Proxy
     * @name ProxyProxyForNoInstance
     * @summary Proxy TCP over websocket for admins
     * @request GET:/api/proxy/noinst/{id}
     */
    proxyProxyForNoInstance: (id: string, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/proxy/noinst/${id}`,
        method: "GET",
        ...params,
      }),
  };
  studentGroupAdmin = {
    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminAddManager
     * @request POST:/api/admin/student-groups/{groupId}/managers
     */
    studentGroupAdminAddManager: (
      groupId: number,
      data: StudentGroupManagerEditModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/student-groups/${groupId}/managers`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminAddMember
     * @request POST:/api/admin/student-groups/{groupId}/members
     */
    studentGroupAdminAddMember: (
      groupId: number,
      data: StudentGroupMemberEditModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/student-groups/${groupId}/members`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminArchiveGroup
     * @request DELETE:/api/admin/student-groups/{groupId}
     */
    studentGroupAdminArchiveGroup: (
      groupId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/student-groups/${groupId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminCreateGroup
     * @request POST:/api/admin/student-groups
     */
    studentGroupAdminCreateGroup: (
      data: StudentGroupEditModel,
      params: RequestParams = {},
    ) =>
      this.request<StudentGroupDetailModel, any>({
        path: `/api/admin/student-groups`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminGetGroup
     * @request GET:/api/admin/student-groups/{groupId}
     */
    studentGroupAdminGetGroup: (groupId: number, params: RequestParams = {}) =>
      this.request<StudentGroupDetailModel, any>({
        path: `/api/admin/student-groups/${groupId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminGetGroup
     * @request GET:/api/admin/student-groups/{groupId}
     */
    useStudentGroupAdminGetGroup: (
      groupId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<StudentGroupDetailModel, any>(
        doFetch ? `/api/admin/student-groups/${groupId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminGetGroup
     * @request GET:/api/admin/student-groups/{groupId}
     */
    mutateStudentGroupAdminGetGroup: (
      groupId: number,
      data?: StudentGroupDetailModel | Promise<StudentGroupDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<StudentGroupDetailModel>(
        `/api/admin/student-groups/${groupId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminGetGroups
     * @request GET:/api/admin/student-groups
     */
    studentGroupAdminGetGroups: (
      query?: {
        keyword?: string | null;
        /** @default false */
        includeArchived?: boolean;
      },
      params: RequestParams = {},
    ) =>
      this.request<StudentGroupBriefModel[], any>({
        path: `/api/admin/student-groups`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminGetGroups
     * @request GET:/api/admin/student-groups
     */
    useStudentGroupAdminGetGroups: (
      query?: {
        keyword?: string | null;
        /** @default false */
        includeArchived?: boolean;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<StudentGroupBriefModel[], any>(
        doFetch ? [`/api/admin/student-groups`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminGetGroups
     * @request GET:/api/admin/student-groups
     */
    mutateStudentGroupAdminGetGroups: (
      query?: {
        keyword?: string | null;
        /** @default false */
        includeArchived?: boolean;
      },
      data?: StudentGroupBriefModel[] | Promise<StudentGroupBriefModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<StudentGroupBriefModel[]>(
        [`/api/admin/student-groups`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminRemoveManager
     * @request DELETE:/api/admin/student-groups/{groupId}/managers/{teacherId}
     */
    studentGroupAdminRemoveManager: (
      groupId: number,
      teacherId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/student-groups/${groupId}/managers/${teacherId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminRemoveMember
     * @request DELETE:/api/admin/student-groups/{groupId}/members/{studentId}
     */
    studentGroupAdminRemoveMember: (
      groupId: number,
      studentId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/student-groups/${groupId}/members/${studentId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags StudentGroupAdmin
     * @name StudentGroupAdminUpdateGroup
     * @request PUT:/api/admin/student-groups/{groupId}
     */
    studentGroupAdminUpdateGroup: (
      groupId: number,
      data: StudentGroupEditModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/student-groups/${groupId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),
  };
  team = {
    /**
     * @description Interface to accept invitation, requires User permission and not being in team
     *
     * @tags Team
     * @name TeamAccept
     * @summary Accept invitation
     * @request POST:/api/team/accept
     */
    teamAccept: (data: string, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/team/accept`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description Use this API to update team avatar, requires User permission and team membership
     *
     * @tags Team
     * @name TeamAvatar
     * @summary Update team avatar
     * @request PUT:/api/team/{id}/avatar
     */
    teamAvatar: (
      id: number,
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<string, RequestResponse>({
        path: `/api/team/${id}/avatar`,
        method: "PUT",
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),

    /**
     * @description Users can request to join a team. The team captain reviews the request.
     *
     * @tags Team
     * @name TeamCreateJoinRequest
     * @summary Create a team join request
     * @request POST:/api/team/{id}/requests
     */
    teamCreateJoinRequest: (
      id: number,
      data: TeamJoinRequestCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamJoinRequestModel, RequestResponse>({
        path: `/api/team/${id}/requests`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description User API for creating teams, each user can only create one team
     *
     * @tags Team
     * @name TeamCreateTeam
     * @summary Create team
     * @request POST:/api/team
     */
    teamCreateTeam: (data: TeamUpdateModel, params: RequestParams = {}) =>
      this.request<TeamInfoModel, RequestResponse>({
        path: `/api/team`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description User API for deleting team, requires User permission and team captain status
     *
     * @tags Team
     * @name TeamDeleteTeam
     * @summary Delete team
     * @request DELETE:/api/team/{id}
     */
    teamDeleteTeam: (id: number, params: RequestParams = {}) =>
      this.request<TeamInfoModel, RequestResponse>({
        path: `/api/team/${id}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description Get basic information of a team by ID
     *
     * @tags Team
     * @name TeamGetBasicInfo
     * @summary Get team information
     * @request GET:/api/team/{id}
     */
    teamGetBasicInfo: (id: number, params: RequestParams = {}) =>
      this.request<TeamInfoModel, RequestResponse>({
        path: `/api/team/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Get basic information of a team by ID
     *
     * @tags Team
     * @name TeamGetBasicInfo
     * @summary Get team information
     * @request GET:/api/team/{id}
     */
    useTeamGetBasicInfo: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamInfoModel, RequestResponse>(
        doFetch ? `/api/team/${id}` : null,
        options,
      ),

    /**
     * @description Get basic information of a team by ID
     *
     * @tags Team
     * @name TeamGetBasicInfo
     * @summary Get team information
     * @request GET:/api/team/{id}
     */
    mutateTeamGetBasicInfo: (
      id: number,
      data?: TeamInfoModel | Promise<TeamInfoModel>,
      options?: MutatorOptions,
    ) => mutate<TeamInfoModel>(`/api/team/${id}`, data, options),

    /**
     * @description Team captain can view pending join requests.
     *
     * @tags Team
     * @name TeamGetJoinRequests
     * @summary Get pending join requests
     * @request GET:/api/team/{id}/requests
     */
    teamGetJoinRequests: (id: number, params: RequestParams = {}) =>
      this.request<TeamJoinRequestModel[], RequestResponse>({
        path: `/api/team/${id}/requests`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Team captain can view pending join requests.
     *
     * @tags Team
     * @name TeamGetJoinRequests
     * @summary Get pending join requests
     * @request GET:/api/team/{id}/requests
     */
    useTeamGetJoinRequests: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamJoinRequestModel[], RequestResponse>(
        doFetch ? `/api/team/${id}/requests` : null,
        options,
      ),

    /**
     * @description Team captain can view pending join requests.
     *
     * @tags Team
     * @name TeamGetJoinRequests
     * @summary Get pending join requests
     * @request GET:/api/team/{id}/requests
     */
    mutateTeamGetJoinRequests: (
      id: number,
      data?: TeamJoinRequestModel[] | Promise<TeamJoinRequestModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamJoinRequestModel[]>(`/api/team/${id}/requests`, data, options),

    /**
     * @description Get basic information of a team based on user
     *
     * @tags Team
     * @name TeamGetTeamsInfo
     * @summary Get current team information
     * @request GET:/api/team
     */
    teamGetTeamsInfo: (params: RequestParams = {}) =>
      this.request<TeamInfoModel[], RequestResponse>({
        path: `/api/team`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Get basic information of a team based on user
     *
     * @tags Team
     * @name TeamGetTeamsInfo
     * @summary Get current team information
     * @request GET:/api/team
     */
    useTeamGetTeamsInfo: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamInfoModel[], RequestResponse>(
        doFetch ? `/api/team` : null,
        options,
      ),

    /**
     * @description Get basic information of a team based on user
     *
     * @tags Team
     * @name TeamGetTeamsInfo
     * @summary Get current team information
     * @request GET:/api/team
     */
    mutateTeamGetTeamsInfo: (
      data?: TeamInfoModel[] | Promise<TeamInfoModel[]>,
      options?: MutatorOptions,
    ) => mutate<TeamInfoModel[]>(`/api/team`, data, options),

    /**
     * @description Get team invitation information, must be team creator
     *
     * @tags Team
     * @name TeamInviteCode
     * @summary Get invitation information
     * @request GET:/api/team/{id}/invite
     */
    teamInviteCode: (id: number, params: RequestParams = {}) =>
      this.request<string, RequestResponse>({
        path: `/api/team/${id}/invite`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Get team invitation information, must be team creator
     *
     * @tags Team
     * @name TeamInviteCode
     * @summary Get invitation information
     * @request GET:/api/team/{id}/invite
     */
    useTeamInviteCode: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<string, RequestResponse>(
        doFetch ? `/api/team/${id}/invite` : null,
        options,
      ),

    /**
     * @description Get team invitation information, must be team creator
     *
     * @tags Team
     * @name TeamInviteCode
     * @summary Get invitation information
     * @request GET:/api/team/{id}/invite
     */
    mutateTeamInviteCode: (
      id: number,
      data?: string | Promise<string>,
      options?: MutatorOptions,
    ) => mutate<string>(`/api/team/${id}/invite`, data, options),

    /**
     * @description User kick API, kick user with corresponding ID, requires team creator permission
     *
     * @tags Team
     * @name TeamKickUser
     * @summary Kick user
     * @request POST:/api/team/{id}/kick/{userId}
     */
    teamKickUser: (id: number, userId: string, params: RequestParams = {}) =>
      this.request<TeamInfoModel, RequestResponse>({
        path: `/api/team/${id}/kick/${userId}`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description Interface to leave team, requires User permission and being in team
     *
     * @tags Team
     * @name TeamLeave
     * @summary Leave team
     * @request POST:/api/team/{id}/leave
     */
    teamLeave: (id: number, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/team/${id}/leave`,
        method: "POST",
        ...params,
      }),

    /**
     * @description Team captain can accept or reject a pending join request.
     *
     * @tags Team
     * @name TeamReviewJoinRequest
     * @summary Review a join request
     * @request POST:/api/team/{id}/requests/{requestId}
     */
    teamReviewJoinRequest: (
      id: number,
      requestId: number,
      data: TeamJoinRequestReviewModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamInfoModel, RequestResponse>({
        path: `/api/team/${id}/requests/${requestId}`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Search visible teams by team name or ID, requires User permission
     *
     * @tags Team
     * @name TeamSearch
     * @summary Search teams for join request
     * @request GET:/api/team/search
     */
    teamSearch: (
      query?: {
        /** Team name or ID */
        hint?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamInfoModel[], any>({
        path: `/api/team/search`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description Search visible teams by team name or ID, requires User permission
     *
     * @tags Team
     * @name TeamSearch
     * @summary Search teams for join request
     * @request GET:/api/team/search
     */
    useTeamSearch: (
      query?: {
        /** Team name or ID */
        hint?: string;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamInfoModel[], any>(
        doFetch ? [`/api/team/search`, query] : null,
        options,
      ),

    /**
     * @description Search visible teams by team name or ID, requires User permission
     *
     * @tags Team
     * @name TeamSearch
     * @summary Search teams for join request
     * @request GET:/api/team/search
     */
    mutateTeamSearch: (
      query?: {
        /** Team name or ID */
        hint?: string;
      },
      data?: TeamInfoModel[] | Promise<TeamInfoModel[]>,
      options?: MutatorOptions,
    ) => mutate<TeamInfoModel[]>([`/api/team/search`, query], data, options),

    /**
     * @description Team ownership transfer API, must be team creator
     *
     * @tags Team
     * @name TeamTransfer
     * @summary Transfer team ownership
     * @request PUT:/api/team/{id}/transfer
     */
    teamTransfer: (
      id: number,
      data: TeamTransferModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamInfoModel, RequestResponse>({
        path: `/api/team/${id}/transfer`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Interface to update invitation token, must be team creator
     *
     * @tags Team
     * @name TeamUpdateInviteToken
     * @summary Update invitation token
     * @request PUT:/api/team/{id}/invite
     */
    teamUpdateInviteToken: (id: number, params: RequestParams = {}) =>
      this.request<string, RequestResponse>({
        path: `/api/team/${id}/invite`,
        method: "PUT",
        format: "json",
        ...params,
      }),

    /**
     * @description Team information update API, must be team creator
     *
     * @tags Team
     * @name TeamUpdateTeam
     * @summary Update team information
     * @request PUT:/api/team/{id}
     */
    teamUpdateTeam: (
      id: number,
      data: TeamUpdateModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamInfoModel, RequestResponse>({
        path: `/api/team/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Perform signature verification
     *
     * @tags Team
     * @name TeamVerifySignature
     * @summary Verify signature
     * @request POST:/api/team/verify
     */
    teamVerifySignature: (
      data: SignatureVerifyModel,
      params: RequestParams = {},
    ) =>
      this.request<void, RequestResponse>({
        path: `/api/team/verify`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),
  };
  teamLabAdmin = {
    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminCaptures
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/captures
     */
    teamLabAdminCaptures: (
      gameId: number,
      teamId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/captures`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminCaptures
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/captures
     */
    useTeamLabAdminCaptures: (
      gameId: number,
      teamId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/admin/teamlab/games/${gameId}/teams/${teamId}/captures`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminCaptures
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/captures
     */
    mutateTeamLabAdminCaptures: (
      gameId: number,
      teamId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/admin/teamlab/games/${gameId}/teams/${teamId}/captures`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminDeploy
     * @request POST:/api/admin/teamlab/games/{gameId}/teams/{teamId}/deploy
     */
    teamLabAdminDeploy: (
      gameId: number,
      teamId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/deploy`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminDestroy
     * @request POST:/api/admin/teamlab/games/{gameId}/teams/{teamId}/destroy
     */
    teamLabAdminDestroy: (
      gameId: number,
      teamId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/destroy`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminDownloadCapture
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/captures/{jobId}/download
     */
    teamLabAdminDownloadCapture: (
      gameId: number,
      teamId: number,
      jobId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/captures/${jobId}/download`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminDownloadCapture
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/captures/{jobId}/download
     */
    useTeamLabAdminDownloadCapture: (
      gameId: number,
      teamId: number,
      jobId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/admin/teamlab/games/${gameId}/teams/${teamId}/captures/${jobId}/download`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminDownloadCapture
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/captures/{jobId}/download
     */
    mutateTeamLabAdminDownloadCapture: (
      gameId: number,
      teamId: number,
      jobId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/admin/teamlab/games/${gameId}/teams/${teamId}/captures/${jobId}/download`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminEvents
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/events
     */
    teamLabAdminEvents: (
      gameId: number,
      teamId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/events`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminEvents
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/events
     */
    useTeamLabAdminEvents: (
      gameId: number,
      teamId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/admin/teamlab/games/${gameId}/teams/${teamId}/events`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminEvents
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/events
     */
    mutateTeamLabAdminEvents: (
      gameId: number,
      teamId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/admin/teamlab/games/${gameId}/teams/${teamId}/events`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminFlows
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/flows
     */
    teamLabAdminFlows: (
      gameId: number,
      teamId: number,
      query?: {
        /** @format int32 */
        count?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/flows`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminFlows
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/flows
     */
    useTeamLabAdminFlows: (
      gameId: number,
      teamId: number,
      query?: {
        /** @format int32 */
        count?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? [`/api/admin/teamlab/games/${gameId}/teams/${teamId}/flows`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminFlows
     * @request GET:/api/admin/teamlab/games/{gameId}/teams/{teamId}/flows
     */
    mutateTeamLabAdminFlows: (
      gameId: number,
      teamId: number,
      query?: {
        /** @format int32 */
        count?: number;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        [`/api/admin/teamlab/games/${gameId}/teams/${teamId}/flows`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminPlan
     * @request POST:/api/admin/teamlab/games/{gameId}/teams/{teamId}/plan
     */
    teamLabAdminPlan: (
      gameId: number,
      teamId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/plan`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminRefreshCaptureStatus
     * @request POST:/api/admin/teamlab/games/{gameId}/teams/{teamId}/captures/{jobId}/status
     */
    teamLabAdminRefreshCaptureStatus: (
      gameId: number,
      teamId: number,
      jobId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/captures/${jobId}/status`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminRefreshFlows
     * @request POST:/api/admin/teamlab/games/{gameId}/teams/{teamId}/flows/refresh
     */
    teamLabAdminRefreshFlows: (
      gameId: number,
      teamId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/flows/refresh`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminStartCapture
     * @request POST:/api/admin/teamlab/games/{gameId}/teams/{teamId}/captures/start
     */
    teamLabAdminStartCapture: (
      gameId: number,
      teamId: number,
      data: TeamLabCaptureStartModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/captures/start`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdmin
     * @name TeamLabAdminStopCapture
     * @request POST:/api/admin/teamlab/games/{gameId}/teams/{teamId}/captures/{jobId}/stop
     */
    teamLabAdminStopCapture: (
      gameId: number,
      teamId: number,
      jobId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/games/${gameId}/teams/${teamId}/captures/${jobId}/stop`,
        method: "POST",
        ...params,
      }),
  };
  theoryAdmin = {
    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminCreateQuestion
     * @request POST:/api/admin/theory/questions
     */
    theoryAdminCreateQuestion: (
      data: TheoryQuestionEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TheoryQuestionBankItemModel, RequestResponse>({
        path: `/api/admin/theory/questions`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminDeleteQuestion
     * @request DELETE:/api/admin/theory/questions/{id}
     */
    theoryAdminDeleteQuestion: (id: number, params: RequestParams = {}) =>
      this.request<void, RequestResponse>({
        path: `/api/admin/theory/questions/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminGetPaper
     * @request GET:/api/admin/theory/games/{gameId}/paper
     */
    theoryAdminGetPaper: (gameId: number, params: RequestParams = {}) =>
      this.request<TheoryPaperDetailModel, RequestResponse>({
        path: `/api/admin/theory/games/${gameId}/paper`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminGetPaper
     * @request GET:/api/admin/theory/games/{gameId}/paper
     */
    useTheoryAdminGetPaper: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TheoryPaperDetailModel, RequestResponse>(
        doFetch ? `/api/admin/theory/games/${gameId}/paper` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminGetPaper
     * @request GET:/api/admin/theory/games/{gameId}/paper
     */
    mutateTheoryAdminGetPaper: (
      gameId: number,
      data?: TheoryPaperDetailModel | Promise<TheoryPaperDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TheoryPaperDetailModel>(
        `/api/admin/theory/games/${gameId}/paper`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminGetQuestions
     * @request GET:/api/admin/theory/questions
     */
    theoryAdminGetQuestions: (
      query?: {
        keyword?: string | null;
        /**
         * @format int32
         * @min 0
         * @max 5000
         * @default 1000
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<TheoryQuestionBankItemModel[], RequestResponse>({
        path: `/api/admin/theory/questions`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminGetQuestions
     * @request GET:/api/admin/theory/questions
     */
    useTheoryAdminGetQuestions: (
      query?: {
        keyword?: string | null;
        /**
         * @format int32
         * @min 0
         * @max 5000
         * @default 1000
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TheoryQuestionBankItemModel[], RequestResponse>(
        doFetch ? [`/api/admin/theory/questions`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminGetQuestions
     * @request GET:/api/admin/theory/questions
     */
    mutateTheoryAdminGetQuestions: (
      query?: {
        keyword?: string | null;
        /**
         * @format int32
         * @min 0
         * @max 5000
         * @default 1000
         */
        count?: number;
        /**
         * @format int32
         * @default 0
         */
        skip?: number;
      },
      data?:
        | TheoryQuestionBankItemModel[]
        | Promise<TheoryQuestionBankItemModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TheoryQuestionBankItemModel[]>(
        [`/api/admin/theory/questions`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminGetResults
     * @request GET:/api/admin/theory/games/{gameId}/results
     */
    theoryAdminGetResults: (gameId: number, params: RequestParams = {}) =>
      this.request<TheoryResultsModel, RequestResponse>({
        path: `/api/admin/theory/games/${gameId}/results`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminGetResults
     * @request GET:/api/admin/theory/games/{gameId}/results
     */
    useTheoryAdminGetResults: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TheoryResultsModel, RequestResponse>(
        doFetch ? `/api/admin/theory/games/${gameId}/results` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminGetResults
     * @request GET:/api/admin/theory/games/{gameId}/results
     */
    mutateTheoryAdminGetResults: (
      gameId: number,
      data?: TheoryResultsModel | Promise<TheoryResultsModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TheoryResultsModel>(
        `/api/admin/theory/games/${gameId}/results`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminPublishPaper
     * @request POST:/api/admin/theory/games/{gameId}/paper/publish
     */
    theoryAdminPublishPaper: (gameId: number, params: RequestParams = {}) =>
      this.request<TheoryPaperDetailModel, RequestResponse>({
        path: `/api/admin/theory/games/${gameId}/paper/publish`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminRecalculateResults
     * @request POST:/api/admin/theory/games/{gameId}/results/recalculate
     */
    theoryAdminRecalculateResults: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<TheoryResultsModel, RequestResponse>({
        path: `/api/admin/theory/games/${gameId}/results/recalculate`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminSavePaper
     * @request PUT:/api/admin/theory/games/{gameId}/paper
     */
    theoryAdminSavePaper: (
      gameId: number,
      data: TheoryPaperEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TheoryPaperDetailModel, RequestResponse>({
        path: `/api/admin/theory/games/${gameId}/paper`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TheoryAdmin
     * @name TheoryAdminUpdateQuestion
     * @request PUT:/api/admin/theory/questions/{id}
     */
    theoryAdminUpdateQuestion: (
      id: number,
      data: TheoryQuestionEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TheoryQuestionBankItemModel, RequestResponse>({
        path: `/api/admin/theory/questions/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  theoryPlayer = {
    /**
     * No description
     *
     * @tags TheoryPlayer
     * @name TheoryPlayerGetPaper
     * @request GET:/api/theory/games/{gameId}/paper
     */
    theoryPlayerGetPaper: (gameId: number, params: RequestParams = {}) =>
      this.request<TheoryPlayerPaperModel, RequestResponse>({
        path: `/api/theory/games/${gameId}/paper`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TheoryPlayer
     * @name TheoryPlayerGetPaper
     * @request GET:/api/theory/games/{gameId}/paper
     */
    useTheoryPlayerGetPaper: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TheoryPlayerPaperModel, RequestResponse>(
        doFetch ? `/api/theory/games/${gameId}/paper` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryPlayer
     * @name TheoryPlayerGetPaper
     * @request GET:/api/theory/games/{gameId}/paper
     */
    mutateTheoryPlayerGetPaper: (
      gameId: number,
      data?: TheoryPlayerPaperModel | Promise<TheoryPlayerPaperModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TheoryPlayerPaperModel>(
        `/api/theory/games/${gameId}/paper`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryPlayer
     * @name TheoryPlayerSaveDraft
     * @request PUT:/api/theory/games/{gameId}/draft
     */
    theoryPlayerSaveDraft: (
      gameId: number,
      data: TheoryAnswerSheetEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TheoryPlayerPaperModel, RequestResponse>({
        path: `/api/theory/games/${gameId}/draft`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TheoryPlayer
     * @name TheoryPlayerScoreboard
     * @request GET:/api/theory/games/{gameId}/scoreboard
     */
    theoryPlayerScoreboard: (gameId: number, params: RequestParams = {}) =>
      this.request<TheoryScoreboardItemModel[], RequestResponse>({
        path: `/api/theory/games/${gameId}/scoreboard`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TheoryPlayer
     * @name TheoryPlayerScoreboard
     * @request GET:/api/theory/games/{gameId}/scoreboard
     */
    useTheoryPlayerScoreboard: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TheoryScoreboardItemModel[], RequestResponse>(
        doFetch ? `/api/theory/games/${gameId}/scoreboard` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryPlayer
     * @name TheoryPlayerScoreboard
     * @request GET:/api/theory/games/{gameId}/scoreboard
     */
    mutateTheoryPlayerScoreboard: (
      gameId: number,
      data?: TheoryScoreboardItemModel[] | Promise<TheoryScoreboardItemModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TheoryScoreboardItemModel[]>(
        `/api/theory/games/${gameId}/scoreboard`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TheoryPlayer
     * @name TheoryPlayerSubmit
     * @request POST:/api/theory/games/{gameId}/submit
     */
    theoryPlayerSubmit: (
      gameId: number,
      data: TheoryAnswerSheetEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TheoryPlayerPaperModel, RequestResponse>({
        path: `/api/theory/games/${gameId}/submit`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  trainingCourseAdmin = {
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminAddChallenge
     * @request POST:/api/admin/training/courses/{courseId}/challenges
     */
    trainingCourseAdminAddChallenge: (
      courseId: number,
      data: TrainingCourseChallengeEditModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/challenges`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminAddEnrollment
     * @request POST:/api/admin/training/courses/{courseId}/enrollments
     */
    trainingCourseAdminAddEnrollment: (
      courseId: number,
      data: TrainingCourseStudentEnrollModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseEnrollmentModel, any>({
        path: `/api/admin/training/courses/${courseId}/enrollments`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminAddTeacher
     * @request POST:/api/admin/training/courses/{courseId}/teachers
     */
    trainingCourseAdminAddTeacher: (
      courseId: number,
      data: TrainingCourseTeacherEditModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/teachers`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminArchive
     * @request POST:/api/admin/training/courses/{courseId}/archive
     */
    trainingCourseAdminArchive: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/archive`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminAttachImageTemplate
     * @request POST:/api/admin/training/courses/{courseId}/image-templates
     */
    trainingCourseAdminAttachImageTemplate: (
      courseId: number,
      data: TrainingCourseImageTemplateAttachModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/image-templates`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminChapterTheoryPaper
     * @request GET:/api/admin/training/courses/{courseId}/chapters/{chapterId}/theory-paper
     */
    trainingCourseAdminChapterTheoryPaper: (
      courseId: number,
      chapterId: number,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChapterTheoryPaperDetailModel, any>({
        path: `/api/admin/training/courses/${courseId}/chapters/${chapterId}/theory-paper`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminChapterTheoryPaper
     * @request GET:/api/admin/training/courses/{courseId}/chapters/{chapterId}/theory-paper
     */
    useTrainingCourseAdminChapterTheoryPaper: (
      courseId: number,
      chapterId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseChapterTheoryPaperDetailModel, any>(
        doFetch
          ? `/api/admin/training/courses/${courseId}/chapters/${chapterId}/theory-paper`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminChapterTheoryPaper
     * @request GET:/api/admin/training/courses/{courseId}/chapters/{chapterId}/theory-paper
     */
    mutateTrainingCourseAdminChapterTheoryPaper: (
      courseId: number,
      chapterId: number,
      data?:
        | TrainingCourseChapterTheoryPaperDetailModel
        | Promise<TrainingCourseChapterTheoryPaperDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseChapterTheoryPaperDetailModel>(
        `/api/admin/training/courses/${courseId}/chapters/${chapterId}/theory-paper`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminChapterTheoryPapers
     * @request GET:/api/admin/training/courses/{courseId}/theory-papers
     */
    trainingCourseAdminChapterTheoryPapers: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChapterTheorySummaryModel[], any>({
        path: `/api/admin/training/courses/${courseId}/theory-papers`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminChapterTheoryPapers
     * @request GET:/api/admin/training/courses/{courseId}/theory-papers
     */
    useTrainingCourseAdminChapterTheoryPapers: (
      courseId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseChapterTheorySummaryModel[], any>(
        doFetch
          ? `/api/admin/training/courses/${courseId}/theory-papers`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminChapterTheoryPapers
     * @request GET:/api/admin/training/courses/{courseId}/theory-papers
     */
    mutateTrainingCourseAdminChapterTheoryPapers: (
      courseId: number,
      data?:
        | TrainingCourseChapterTheorySummaryModel[]
        | Promise<TrainingCourseChapterTheorySummaryModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseChapterTheorySummaryModel[]>(
        `/api/admin/training/courses/${courseId}/theory-papers`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCourse
     * @request GET:/api/admin/training/courses/{courseId}
     */
    trainingCourseAdminCourse: (courseId: number, params: RequestParams = {}) =>
      this.request<TrainingCourseModel, any>({
        path: `/api/admin/training/courses/${courseId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCourse
     * @request GET:/api/admin/training/courses/{courseId}
     */
    useTrainingCourseAdminCourse: (
      courseId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseModel, any>(
        doFetch ? `/api/admin/training/courses/${courseId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCourse
     * @request GET:/api/admin/training/courses/{courseId}
     */
    mutateTrainingCourseAdminCourse: (
      courseId: number,
      data?: TrainingCourseModel | Promise<TrainingCourseModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseModel>(
        `/api/admin/training/courses/${courseId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCourseChallengeEditDetail
     * @request GET:/api/admin/training/courses/{courseId}/challenges/{exerciseChallengeId}/edit
     */
    trainingCourseAdminCourseChallengeEditDetail: (
      courseId: number,
      exerciseChallengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChallengeEditDetailModel, any>({
        path: `/api/admin/training/courses/${courseId}/challenges/${exerciseChallengeId}/edit`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCourseChallengeEditDetail
     * @request GET:/api/admin/training/courses/{courseId}/challenges/{exerciseChallengeId}/edit
     */
    useTrainingCourseAdminCourseChallengeEditDetail: (
      courseId: number,
      exerciseChallengeId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseChallengeEditDetailModel, any>(
        doFetch
          ? `/api/admin/training/courses/${courseId}/challenges/${exerciseChallengeId}/edit`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCourseChallengeEditDetail
     * @request GET:/api/admin/training/courses/{courseId}/challenges/{exerciseChallengeId}/edit
     */
    mutateTrainingCourseAdminCourseChallengeEditDetail: (
      courseId: number,
      exerciseChallengeId: number,
      data?:
        | TrainingCourseChallengeEditDetailModel
        | Promise<TrainingCourseChallengeEditDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseChallengeEditDetailModel>(
        `/api/admin/training/courses/${courseId}/challenges/${exerciseChallengeId}/edit`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCourses
     * @request GET:/api/admin/training/courses
     */
    trainingCourseAdminCourses: (params: RequestParams = {}) =>
      this.request<TrainingCourseModel[], any>({
        path: `/api/admin/training/courses`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCourses
     * @request GET:/api/admin/training/courses
     */
    useTrainingCourseAdminCourses: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseModel[], any>(
        doFetch ? `/api/admin/training/courses` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCourses
     * @request GET:/api/admin/training/courses
     */
    mutateTrainingCourseAdminCourses: (
      data?: TrainingCourseModel[] | Promise<TrainingCourseModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseModel[]>(
        `/api/admin/training/courses`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCreateChapter
     * @request POST:/api/admin/training/courses/{courseId}/chapters
     */
    trainingCourseAdminCreateChapter: (
      courseId: number,
      data: TrainingCourseChapterEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChapterModel, any>({
        path: `/api/admin/training/courses/${courseId}/chapters`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCreateCourse
     * @request POST:/api/admin/training/courses
     */
    trainingCourseAdminCreateCourse: (
      data: TrainingCourseEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseModel, any>({
        path: `/api/admin/training/courses`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCreateCourseChallenge
     * @request POST:/api/admin/training/courses/{courseId}/challenges/create
     */
    trainingCourseAdminCreateCourseChallenge: (
      courseId: number,
      data: TrainingCourseChallengeCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChallengeModel, any>({
        path: `/api/admin/training/courses/${courseId}/challenges/create`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCreateResource
     * @request POST:/api/admin/training/courses/{courseId}/resources
     */
    trainingCourseAdminCreateResource: (
      courseId: number,
      data: TrainingCourseResourceEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseResourceModel, any>({
        path: `/api/admin/training/courses/${courseId}/resources`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminCreateTheoryQuestion
     * @request POST:/api/admin/training/courses/{courseId}/theory-questions
     */
    trainingCourseAdminCreateTheoryQuestion: (
      courseId: number,
      data: TheoryQuestionEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseTheoryQuestionModel, any>({
        path: `/api/admin/training/courses/${courseId}/theory-questions`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminDeleteChapter
     * @request DELETE:/api/admin/training/courses/{courseId}/chapters/{chapterId}
     */
    trainingCourseAdminDeleteChapter: (
      courseId: number,
      chapterId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/chapters/${chapterId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminDeleteCourse
     * @request DELETE:/api/admin/training/courses/{courseId}
     */
    trainingCourseAdminDeleteCourse: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminDeleteResource
     * @request DELETE:/api/admin/training/courses/{courseId}/resources/{resourceId}
     */
    trainingCourseAdminDeleteResource: (
      courseId: number,
      resourceId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/resources/${resourceId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminDeleteTheoryQuestion
     * @request DELETE:/api/admin/training/courses/{courseId}/theory-questions/{questionId}
     */
    trainingCourseAdminDeleteTheoryQuestion: (
      courseId: number,
      questionId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/theory-questions/${questionId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminDetachImageTemplate
     * @request DELETE:/api/admin/training/courses/{courseId}/image-templates/{templateId}
     */
    trainingCourseAdminDetachImageTemplate: (
      courseId: number,
      templateId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/image-templates/${templateId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminDockerRegistry
     * @request GET:/api/admin/training/courses/{courseId}/image-templates/docker-registry
     */
    trainingCourseAdminDockerRegistry: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/image-templates/docker-registry`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminDockerRegistry
     * @request GET:/api/admin/training/courses/{courseId}/image-templates/docker-registry
     */
    useTrainingCourseAdminDockerRegistry: (
      courseId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/admin/training/courses/${courseId}/image-templates/docker-registry`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminDockerRegistry
     * @request GET:/api/admin/training/courses/{courseId}/image-templates/docker-registry
     */
    mutateTrainingCourseAdminDockerRegistry: (
      courseId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/admin/training/courses/${courseId}/image-templates/docker-registry`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminEnrollments
     * @request GET:/api/admin/training/courses/{courseId}/enrollments
     */
    trainingCourseAdminEnrollments: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseEnrollmentModel[], any>({
        path: `/api/admin/training/courses/${courseId}/enrollments`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminEnrollments
     * @request GET:/api/admin/training/courses/{courseId}/enrollments
     */
    useTrainingCourseAdminEnrollments: (
      courseId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseEnrollmentModel[], any>(
        doFetch ? `/api/admin/training/courses/${courseId}/enrollments` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminEnrollments
     * @request GET:/api/admin/training/courses/{courseId}/enrollments
     */
    mutateTrainingCourseAdminEnrollments: (
      courseId: number,
      data?:
        | TrainingCourseEnrollmentModel[]
        | Promise<TrainingCourseEnrollmentModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseEnrollmentModel[]>(
        `/api/admin/training/courses/${courseId}/enrollments`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminImageTemplates
     * @request GET:/api/admin/training/courses/{courseId}/image-templates
     */
    trainingCourseAdminImageTemplates: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseImageTemplateModel[], any>({
        path: `/api/admin/training/courses/${courseId}/image-templates`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminImageTemplates
     * @request GET:/api/admin/training/courses/{courseId}/image-templates
     */
    useTrainingCourseAdminImageTemplates: (
      courseId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseImageTemplateModel[], any>(
        doFetch
          ? `/api/admin/training/courses/${courseId}/image-templates`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminImageTemplates
     * @request GET:/api/admin/training/courses/{courseId}/image-templates
     */
    mutateTrainingCourseAdminImageTemplates: (
      courseId: number,
      data?:
        | TrainingCourseImageTemplateModel[]
        | Promise<TrainingCourseImageTemplateModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseImageTemplateModel[]>(
        `/api/admin/training/courses/${courseId}/image-templates`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminImportLocalTemplate
     * @request POST:/api/admin/training/courses/{courseId}/image-templates/import-local
     */
    trainingCourseAdminImportLocalTemplate: (
      courseId: number,
      data: TrainingCourseLocalImageImportModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/image-templates/import-local`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminLearningSummaries
     * @request GET:/api/admin/training/courses/{courseId}/learning-summaries
     */
    trainingCourseAdminLearningSummaries: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseStudentLearningSummaryModel[], any>({
        path: `/api/admin/training/courses/${courseId}/learning-summaries`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminLearningSummaries
     * @request GET:/api/admin/training/courses/{courseId}/learning-summaries
     */
    useTrainingCourseAdminLearningSummaries: (
      courseId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseStudentLearningSummaryModel[], any>(
        doFetch
          ? `/api/admin/training/courses/${courseId}/learning-summaries`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminLearningSummaries
     * @request GET:/api/admin/training/courses/{courseId}/learning-summaries
     */
    mutateTrainingCourseAdminLearningSummaries: (
      courseId: number,
      data?:
        | TrainingCourseStudentLearningSummaryModel[]
        | Promise<TrainingCourseStudentLearningSummaryModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseStudentLearningSummaryModel[]>(
        `/api/admin/training/courses/${courseId}/learning-summaries`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminMoveToDraft
     * @request POST:/api/admin/training/courses/{courseId}/draft
     */
    trainingCourseAdminMoveToDraft: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/draft`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminPublish
     * @request POST:/api/admin/training/courses/{courseId}/publish
     */
    trainingCourseAdminPublish: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/publish`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminRegisterDockerTemplate
     * @request POST:/api/admin/training/courses/{courseId}/image-templates/register-docker
     */
    trainingCourseAdminRegisterDockerTemplate: (
      courseId: number,
      data: TrainingCourseDockerRegisterModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/image-templates/register-docker`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminRemoveChallenge
     * @request DELETE:/api/admin/training/courses/{courseId}/challenges/{exerciseChallengeId}
     */
    trainingCourseAdminRemoveChallenge: (
      courseId: number,
      exerciseChallengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/challenges/${exerciseChallengeId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminRemoveTeacher
     * @request DELETE:/api/admin/training/courses/{courseId}/teachers/{teacherId}
     */
    trainingCourseAdminRemoveTeacher: (
      courseId: number,
      teacherId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/teachers/${teacherId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminReviewEnrollment
     * @request PUT:/api/admin/training/courses/{courseId}/enrollments/{userId}
     */
    trainingCourseAdminReviewEnrollment: (
      courseId: number,
      userId: string,
      data: TrainingCourseEnrollmentReviewModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/enrollments/${userId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminSaveChapterTheoryPaper
     * @request PUT:/api/admin/training/courses/{courseId}/chapters/{chapterId}/theory-paper
     */
    trainingCourseAdminSaveChapterTheoryPaper: (
      courseId: number,
      chapterId: number,
      data: TrainingCourseChapterTheoryPaperEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChapterTheoryPaperDetailModel, any>({
        path: `/api/admin/training/courses/${courseId}/chapters/${chapterId}/theory-paper`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminStudentCandidates
     * @request GET:/api/admin/training/courses/{courseId}/student-candidates
     */
    trainingCourseAdminStudentCandidates: (
      courseId: number,
      query?: {
        keyword?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseStudentCandidateModel[], any>({
        path: `/api/admin/training/courses/${courseId}/student-candidates`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminStudentCandidates
     * @request GET:/api/admin/training/courses/{courseId}/student-candidates
     */
    useTrainingCourseAdminStudentCandidates: (
      courseId: number,
      query?: {
        keyword?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseStudentCandidateModel[], any>(
        doFetch
          ? [
              `/api/admin/training/courses/${courseId}/student-candidates`,
              query,
            ]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminStudentCandidates
     * @request GET:/api/admin/training/courses/{courseId}/student-candidates
     */
    mutateTrainingCourseAdminStudentCandidates: (
      courseId: number,
      query?: {
        keyword?: string | null;
      },
      data?:
        | TrainingCourseStudentCandidateModel[]
        | Promise<TrainingCourseStudentCandidateModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseStudentCandidateModel[]>(
        [`/api/admin/training/courses/${courseId}/student-candidates`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminStudentLearningDetail
     * @request GET:/api/admin/training/courses/{courseId}/students/{userId}/learning
     */
    trainingCourseAdminStudentLearningDetail: (
      courseId: number,
      userId: string,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseStudentLearningDetailModel, any>({
        path: `/api/admin/training/courses/${courseId}/students/${userId}/learning`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminStudentLearningDetail
     * @request GET:/api/admin/training/courses/{courseId}/students/{userId}/learning
     */
    useTrainingCourseAdminStudentLearningDetail: (
      courseId: number,
      userId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseStudentLearningDetailModel, any>(
        doFetch
          ? `/api/admin/training/courses/${courseId}/students/${userId}/learning`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminStudentLearningDetail
     * @request GET:/api/admin/training/courses/{courseId}/students/{userId}/learning
     */
    mutateTrainingCourseAdminStudentLearningDetail: (
      courseId: number,
      userId: string,
      data?:
        | TrainingCourseStudentLearningDetailModel
        | Promise<TrainingCourseStudentLearningDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseStudentLearningDetailModel>(
        `/api/admin/training/courses/${courseId}/students/${userId}/learning`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminTeacherCandidates
     * @request GET:/api/admin/training/courses/{courseId}/teacher-candidates
     */
    trainingCourseAdminTeacherCandidates: (
      courseId: number,
      query?: {
        keyword?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseTeacherCandidateModel[], any>({
        path: `/api/admin/training/courses/${courseId}/teacher-candidates`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminTeacherCandidates
     * @request GET:/api/admin/training/courses/{courseId}/teacher-candidates
     */
    useTrainingCourseAdminTeacherCandidates: (
      courseId: number,
      query?: {
        keyword?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseTeacherCandidateModel[], any>(
        doFetch
          ? [
              `/api/admin/training/courses/${courseId}/teacher-candidates`,
              query,
            ]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminTeacherCandidates
     * @request GET:/api/admin/training/courses/{courseId}/teacher-candidates
     */
    mutateTrainingCourseAdminTeacherCandidates: (
      courseId: number,
      query?: {
        keyword?: string | null;
      },
      data?:
        | TrainingCourseTeacherCandidateModel[]
        | Promise<TrainingCourseTeacherCandidateModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseTeacherCandidateModel[]>(
        [`/api/admin/training/courses/${courseId}/teacher-candidates`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminTheoryQuestions
     * @request GET:/api/admin/training/courses/{courseId}/theory-questions
     */
    trainingCourseAdminTheoryQuestions: (
      courseId: number,
      query?: {
        keyword?: string | null;
        type?: TheoryQuestionType | null;
        bankName?: string | null;
        /**
         * @format int32
         * @default 1000
         */
        count?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseTheoryQuestionModel[], any>({
        path: `/api/admin/training/courses/${courseId}/theory-questions`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminTheoryQuestions
     * @request GET:/api/admin/training/courses/{courseId}/theory-questions
     */
    useTrainingCourseAdminTheoryQuestions: (
      courseId: number,
      query?: {
        keyword?: string | null;
        type?: TheoryQuestionType | null;
        bankName?: string | null;
        /**
         * @format int32
         * @default 1000
         */
        count?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseTheoryQuestionModel[], any>(
        doFetch
          ? [`/api/admin/training/courses/${courseId}/theory-questions`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminTheoryQuestions
     * @request GET:/api/admin/training/courses/{courseId}/theory-questions
     */
    mutateTrainingCourseAdminTheoryQuestions: (
      courseId: number,
      query?: {
        keyword?: string | null;
        type?: TheoryQuestionType | null;
        bankName?: string | null;
        /**
         * @format int32
         * @default 1000
         */
        count?: number;
      },
      data?:
        | TrainingCourseTheoryQuestionModel[]
        | Promise<TrainingCourseTheoryQuestionModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseTheoryQuestionModel[]>(
        [`/api/admin/training/courses/${courseId}/theory-questions`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminUpdateChapter
     * @request PUT:/api/admin/training/courses/{courseId}/chapters/{chapterId}
     */
    trainingCourseAdminUpdateChapter: (
      courseId: number,
      chapterId: number,
      data: TrainingCourseChapterEditModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/chapters/${chapterId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminUpdateCourse
     * @request PUT:/api/admin/training/courses/{courseId}
     */
    trainingCourseAdminUpdateCourse: (
      courseId: number,
      data: TrainingCourseEditModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminUpdateCourseChallenge
     * @request PUT:/api/admin/training/courses/{courseId}/challenges/{exerciseChallengeId}
     */
    trainingCourseAdminUpdateCourseChallenge: (
      courseId: number,
      exerciseChallengeId: number,
      data: TrainingCourseChallengeUpdateModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChallengeEditDetailModel, any>({
        path: `/api/admin/training/courses/${courseId}/challenges/${exerciseChallengeId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminUpdateResource
     * @request PUT:/api/admin/training/courses/{courseId}/resources/{resourceId}
     */
    trainingCourseAdminUpdateResource: (
      courseId: number,
      resourceId: number,
      data: TrainingCourseResourceEditModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/resources/${resourceId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminUpdateTheoryQuestion
     * @request PUT:/api/admin/training/courses/{courseId}/theory-questions/{questionId}
     */
    trainingCourseAdminUpdateTheoryQuestion: (
      courseId: number,
      questionId: number,
      data: TheoryQuestionEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseTheoryQuestionModel, any>({
        path: `/api/admin/training/courses/${courseId}/theory-questions/${questionId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminUploadDockerTemplate
     * @request POST:/api/admin/training/courses/{courseId}/image-templates/upload-docker
     */
    trainingCourseAdminUploadDockerTemplate: (
      courseId: number,
      data: {
        ContentType?: string | null;
        ContentDisposition?: string | null;
        Headers?: any[] | null;
        /** @format int64 */
        Length?: number;
        Name?: string | null;
        FileName?: string | null;
        name?: string | null;
        repository?: string | null;
        tag?: string | null;
        sourceImage?: string | null;
        osType?: OSType;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/image-templates/upload-docker`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminUploadVmArchiveTemplate
     * @request POST:/api/admin/training/courses/{courseId}/image-templates/upload-vm-archive
     */
    trainingCourseAdminUploadVmArchiveTemplate: (
      courseId: number,
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/image-templates/upload-vm-archive`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourseAdmin
     * @name TrainingCourseAdminUploadVmTemplate
     * @request POST:/api/admin/training/courses/{courseId}/image-templates/upload-vm
     */
    trainingCourseAdminUploadVmTemplate: (
      courseId: number,
      data: {
        /** @format binary */
        file?: File | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/training/courses/${courseId}/image-templates/upload-vm`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        ...params,
      }),
  };
  trainingCourse = {
    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCancelEnroll
     * @request DELETE:/api/training/courses/{courseId}/enroll
     */
    trainingCourseCancelEnroll: (
      courseId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/training/courses/${courseId}/enroll`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseChallenge
     * @request GET:/api/training/courses/{courseId}/challenges/{challengeId}
     */
    trainingCourseChallenge: (
      courseId: number,
      challengeId: number,
      query?: {
        /** @format int32 */
        chapterId?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChallengeDetailModel, any>({
        path: `/api/training/courses/${courseId}/challenges/${challengeId}`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseChallenge
     * @request GET:/api/training/courses/{courseId}/challenges/{challengeId}
     */
    useTrainingCourseChallenge: (
      courseId: number,
      challengeId: number,
      query?: {
        /** @format int32 */
        chapterId?: number | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseChallengeDetailModel, any>(
        doFetch
          ? [
              `/api/training/courses/${courseId}/challenges/${challengeId}`,
              query,
            ]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseChallenge
     * @request GET:/api/training/courses/{courseId}/challenges/{challengeId}
     */
    mutateTrainingCourseChallenge: (
      courseId: number,
      challengeId: number,
      query?: {
        /** @format int32 */
        chapterId?: number | null;
      },
      data?:
        | TrainingCourseChallengeDetailModel
        | Promise<TrainingCourseChallengeDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseChallengeDetailModel>(
        [`/api/training/courses/${courseId}/challenges/${challengeId}`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseChapter
     * @request GET:/api/training/courses/{courseId}/chapters/{chapterId}
     */
    trainingCourseChapter: (
      courseId: number,
      chapterId: number,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChapterModel, any>({
        path: `/api/training/courses/${courseId}/chapters/${chapterId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseChapter
     * @request GET:/api/training/courses/{courseId}/chapters/{chapterId}
     */
    useTrainingCourseChapter: (
      courseId: number,
      chapterId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseChapterModel, any>(
        doFetch
          ? `/api/training/courses/${courseId}/chapters/${chapterId}`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseChapter
     * @request GET:/api/training/courses/{courseId}/chapters/{chapterId}
     */
    mutateTrainingCourseChapter: (
      courseId: number,
      chapterId: number,
      data?: TrainingCourseChapterModel | Promise<TrainingCourseChapterModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseChapterModel>(
        `/api/training/courses/${courseId}/chapters/${chapterId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseChapterTheory
     * @request GET:/api/training/courses/{courseId}/chapters/{chapterId}/theory
     */
    trainingCourseChapterTheory: (
      courseId: number,
      chapterId: number,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChapterTheoryPlayerPaperModel, any>({
        path: `/api/training/courses/${courseId}/chapters/${chapterId}/theory`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseChapterTheory
     * @request GET:/api/training/courses/{courseId}/chapters/{chapterId}/theory
     */
    useTrainingCourseChapterTheory: (
      courseId: number,
      chapterId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseChapterTheoryPlayerPaperModel, any>(
        doFetch
          ? `/api/training/courses/${courseId}/chapters/${chapterId}/theory`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseChapterTheory
     * @request GET:/api/training/courses/{courseId}/chapters/{chapterId}/theory
     */
    mutateTrainingCourseChapterTheory: (
      courseId: number,
      chapterId: number,
      data?:
        | TrainingCourseChapterTheoryPlayerPaperModel
        | Promise<TrainingCourseChapterTheoryPlayerPaperModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseChapterTheoryPlayerPaperModel>(
        `/api/training/courses/${courseId}/chapters/${chapterId}/theory`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCheckIn
     * @request POST:/api/training/courses/check-in
     */
    trainingCourseCheckIn: (params: RequestParams = {}) =>
      this.request<TrainingPersonalOverviewModel, any>({
        path: `/api/training/courses/check-in`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCompleteChapter
     * @request POST:/api/training/courses/{courseId}/chapters/{chapterId}/complete
     */
    trainingCourseCompleteChapter: (
      courseId: number,
      chapterId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/training/courses/${courseId}/chapters/${chapterId}/complete`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCourse
     * @request GET:/api/training/courses/{courseId}
     */
    trainingCourseCourse: (courseId: number, params: RequestParams = {}) =>
      this.request<TrainingCourseModel, any>({
        path: `/api/training/courses/${courseId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCourse
     * @request GET:/api/training/courses/{courseId}
     */
    useTrainingCourseCourse: (
      courseId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseModel, any>(
        doFetch ? `/api/training/courses/${courseId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCourse
     * @request GET:/api/training/courses/{courseId}
     */
    mutateTrainingCourseCourse: (
      courseId: number,
      data?: TrainingCourseModel | Promise<TrainingCourseModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingCourseModel>(
        `/api/training/courses/${courseId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCourses
     * @request GET:/api/training/courses
     */
    trainingCourseCourses: (params: RequestParams = {}) =>
      this.request<TrainingCourseModel[], any>({
        path: `/api/training/courses`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCourses
     * @request GET:/api/training/courses
     */
    useTrainingCourseCourses: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingCourseModel[], any>(
        doFetch ? `/api/training/courses` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCourses
     * @request GET:/api/training/courses
     */
    mutateTrainingCourseCourses: (
      data?: TrainingCourseModel[] | Promise<TrainingCourseModel[]>,
      options?: MutatorOptions,
    ) => mutate<TrainingCourseModel[]>(`/api/training/courses`, data, options),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseCreateContainer
     * @request POST:/api/training/courses/{courseId}/challenges/{challengeId}/container
     */
    trainingCourseCreateContainer: (
      courseId: number,
      challengeId: number,
      query?: {
        /** @format int32 */
        chapterId?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<ContainerInfoModel, any>({
        path: `/api/training/courses/${courseId}/challenges/${challengeId}/container`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseDestroyContainer
     * @request DELETE:/api/training/courses/{courseId}/challenges/{challengeId}/container
     */
    trainingCourseDestroyContainer: (
      courseId: number,
      challengeId: number,
      query?: {
        /** @format int32 */
        chapterId?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/training/courses/${courseId}/challenges/${challengeId}/container`,
        method: "DELETE",
        query: query,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseDownloadResource
     * @request GET:/api/training/courses/{courseId}/resources/{resourceId}/download
     */
    trainingCourseDownloadResource: (
      courseId: number,
      resourceId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/training/courses/${courseId}/resources/${resourceId}/download`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseDownloadResource
     * @request GET:/api/training/courses/{courseId}/resources/{resourceId}/download
     */
    useTrainingCourseDownloadResource: (
      courseId: number,
      resourceId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/training/courses/${courseId}/resources/${resourceId}/download`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseDownloadResource
     * @request GET:/api/training/courses/{courseId}/resources/{resourceId}/download
     */
    mutateTrainingCourseDownloadResource: (
      courseId: number,
      resourceId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/training/courses/${courseId}/resources/${resourceId}/download`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseEnroll
     * @request POST:/api/training/courses/{courseId}/enroll
     */
    trainingCourseEnroll: (
      courseId: number,
      data: TrainingCourseEnrollmentApplyModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseEnrollmentModel, any>({
        path: `/api/training/courses/${courseId}/enroll`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseExtendContainer
     * @request POST:/api/training/courses/{courseId}/challenges/{challengeId}/container/extend
     */
    trainingCourseExtendContainer: (
      courseId: number,
      challengeId: number,
      query?: {
        /** @format int32 */
        chapterId?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<ContainerInfoModel, any>({
        path: `/api/training/courses/${courseId}/challenges/${challengeId}/container/extend`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseOverview
     * @request GET:/api/training/courses/overview
     */
    trainingCourseOverview: (params: RequestParams = {}) =>
      this.request<TrainingPersonalOverviewModel, any>({
        path: `/api/training/courses/overview`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseOverview
     * @request GET:/api/training/courses/overview
     */
    useTrainingCourseOverview: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TrainingPersonalOverviewModel, any>(
        doFetch ? `/api/training/courses/overview` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseOverview
     * @request GET:/api/training/courses/overview
     */
    mutateTrainingCourseOverview: (
      data?:
        | TrainingPersonalOverviewModel
        | Promise<TrainingPersonalOverviewModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TrainingPersonalOverviewModel>(
        `/api/training/courses/overview`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseRetryChapterTheory
     * @request POST:/api/training/courses/{courseId}/chapters/{chapterId}/theory/retry
     */
    trainingCourseRetryChapterTheory: (
      courseId: number,
      chapterId: number,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChapterTheoryPlayerPaperModel, any>({
        path: `/api/training/courses/${courseId}/chapters/${chapterId}/theory/retry`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseSaveChapterTheoryDraft
     * @request PUT:/api/training/courses/{courseId}/chapters/{chapterId}/theory/draft
     */
    trainingCourseSaveChapterTheoryDraft: (
      courseId: number,
      chapterId: number,
      data: TheoryAnswerSheetEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChapterTheoryPlayerPaperModel, any>({
        path: `/api/training/courses/${courseId}/chapters/${chapterId}/theory/draft`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseSubmitChapterTheory
     * @request POST:/api/training/courses/{courseId}/chapters/{chapterId}/theory/submit
     */
    trainingCourseSubmitChapterTheory: (
      courseId: number,
      chapterId: number,
      data: TheoryAnswerSheetEditModel,
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseChapterTheoryPlayerPaperModel, any>({
        path: `/api/training/courses/${courseId}/chapters/${chapterId}/theory/submit`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TrainingCourse
     * @name TrainingCourseSubmitFlag
     * @request POST:/api/training/courses/{courseId}/challenges/{challengeId}/submit
     */
    trainingCourseSubmitFlag: (
      courseId: number,
      challengeId: number,
      data: FlagSubmitModel,
      query?: {
        /** @format int32 */
        chapterId?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TrainingCourseSubmitResultModel, any>({
        path: `/api/training/courses/${courseId}/challenges/${challengeId}/submit`,
        method: "POST",
        query: query,
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
}

const api = new Api();
export default api;

export const fetcher = async (
  args: string | [string, Record<string, unknown>],
) => {
  if (typeof args === "string") {
    const response = await api.request({ path: args });
    return response.data;
  } else {
    const [path, query] = args;
    const response = await api.request({ path, query });
    return response.data;
  }
};
