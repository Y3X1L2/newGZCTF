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

export enum BootstrapProfileStatus {
  Active = 0,
  Deleting = 1,
  Deleted = 2,
}

export enum ApiOperationStatus {
  Pending = 0,
  Running = 1,
  Succeeded = 2,
  Failed = 3,
}

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

export enum OperationalErrorCategory {
  Authorization = 0,
  Validation = 1,
  Conflict = 2,
  Scheduling = 3,
  Capacity = 4,
  ImageRegistry = 5,
  ImageTransfer = 6,
  NodeUnavailable = 7,
  AgentProtocol = 8,
  AgentTransport = 9,
  Docker = 10,
  Kvm = 11,
  Network = 12,
  HealthCheck = 13,
  Storage = 14,
  Database = 15,
  Cache = 16,
  Unknown = 17,
}

export enum OperationalEventOutcome {
  Started = 0,
  Pending = 1,
  Blocked = 2,
  Succeeded = 3,
  Failed = 4,
  Cancelled = 5,
  Recovered = 6,
  Observed = 7,
}

export enum OperationalEventSeverity {
  Debug = 0,
  Information = 1,
  Warning = 2,
  Error = 3,
  Critical = 4,
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

export enum VmNetworkMode {
  Dhcp = 0,
  Preconfigured = 1,
}

export enum VmRuntimeMode {
  Managed = 0,
  Opaque = 1,
}

export enum VmArtifactStatus {
  None = 0,
  Building = 1,
  Ready = 2,
  Failed = 3,
  Invalidated = 4,
}

export enum ImageStatus {
  Ready = 0,
  Importing = 1,
  Error = 2,
  Deleting = 3,
}

export enum OSType {
  Linux = 0,
  Windows = 1,
}

export enum AgentUpdateState {
  Stable = 0,
  Cordoned = 1,
  Syncing = 2,
  AwaitingHeartbeat = 3,
  VerifyingFabric = 4,
  Failed = 5,
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

/**
 * Origin of a public exercise-pool entry. Source challenges are copied into the
 * pool so their original game/course lifecycle remains isolated.
 */
export enum ExercisePoolSource {
  Exercise = "Exercise",
  Game = "Game",
  Training = "Training",
}

export enum DeploymentStage {
  Queued = 0,
  AdmissionChecking = 1,
  CapacityWaiting = 2,
  ImagePreparing = 3,
  ImagePulling = 4,
  ImageVerifying = 5,
  NodeExecutionWaiting = 6,
  ContainerCreating = 7,
  VmCreating = 8,
  RuntimeNetworkApplying = 9,
  RuntimeAssetsCreating = 10,
  BootProbing = 11,
  AccessOpening = 12,
  Extending = 13,
  Stopping = 14,
  Destroying = 15,
  RollingBack = 16,
  Ready = 17,
  Failed = 18,
  Cancelled = 19,
  ArtifactsVerifying = 20,
  NetworkApplying = 21,
  RoutesApplying = 22,
  AssetBooting = 23,
  GuestWaiting = 24,
  BootstrapInjecting = 25,
  BootstrapRunning = 26,
  GuestRebooting = 27,
  HealthProbing = 28,
  ObservationStarting = 29,
}

export enum RuntimeOperationKind {
  Create = 1,
  Extend = 2,
  Stop = 3,
  Reset = 4,
  Destroy = 5,
  Pause = 6,
  Resume = 7,
  AssetControl = 8,
}

export enum DeploymentQueueKind {
  GameContainer = 1,
  ExerciseContainer = 2,
  TrainingContainer = 3,
  AwdpContainer = 4,
  ChallengeTestContainer = 5,
  VirtualMachine = 6,
  TeamLabRuntime = 7,
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

/** Player-facing container entry publication status. */
export enum ContainerEntryStatus {
  Pending = "Pending",
  Ready = "Ready",
  Error = "Error",
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

/** Login response status */
export enum RegisterStatus {
  LoggedIn = "LoggedIn",
  AdminConfirmationRequired = "AdminConfirmationRequired",
  EmailConfirmationRequired = "EmailConfirmationRequired",
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

export enum ImageType {
  Docker = 0,
  Qcow2 = 1,
  Ova = 2,
  Vmdk = 3,
}

export enum TeamLabDependencyCondition {
  NetworkReady = 0,
  GuestReady = 1,
  ServiceReady = 2,
  BootstrapCompleted = 3,
}

export enum TeamLabInfrastructureKind {
  ManagedSwitch = 0,
  ManagedRouter = 1,
}

export enum TeamLabConnectionDirection {
  FromTo = 0,
  Bidirectional = 1,
}

export enum TeamLabEndpointObservationMode {
  Disabled = 0,
  Optional = 1,
  Required = 2,
}

export enum TeamLabHealthCheckKind {
  Tcp = 0,
  Http = 1,
}

export enum TeamLabTrafficCaptureSegmentStatus {
  Pending = 0,
  Running = 1,
  Stopping = 2,
  Captured = 3,
  Uploading = 4,
  Uploaded = 5,
  Failed = 6,
  Expired = 7,
  CleanupPending = 8,
}

export enum TeamLabTrafficCaptureStatus {
  Pending = 0,
  Running = 1,
  Stopping = 2,
  Completed = 3,
  Failed = 4,
  Expired = 5,
  CleanupPending = 6,
}

export enum TeamLabObservationPointKind {
  NetworkBridge = 0,
  RouterFragment = 1,
  FabricUplink = 2,
  WorkloadEndpoint = 3,
}

export enum TeamLabTrafficEvidenceKind {
  Packet = 0,
  EndpointProcess = 1,
}

export enum TeamLabPathConfidence {
  PacketExact = 0,
  ProcessCorrelated = 1,
  TemporallyRelated = 2,
}

export enum TeamLabEventLevel {
  Info = 0,
  Success = 1,
  Warning = 2,
  Error = 3,
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

export enum DeploymentQueueTicketStatus {
  Pending = 0,
  Scheduling = 1,
  Scheduled = 2,
  Running = 3,
  Succeeded = 4,
  Failed = 5,
  Cancelled = 6,
}

export enum TeamLabAssetKind {
  Docker = 0,
  Vm = 1,
}

export enum TeamLabExecutionModel {
  V1 = 0,
  V2 = 1,
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
  Paused = 8,
  Destroying = 9,
  Destroyed = 10,
  Stopped = 11,
}

export enum TeamLabRemoteSessionStatus {
  Creating = 1,
  Ready = 2,
  Connected = 3,
  Ending = 4,
  Ended = 5,
  Failed = 6,
}

export enum TeamLabRemoteProtocol {
  ContainerTerminal = 1,
  Ssh = 2,
  Rdp = 3,
  Vnc = 4,
}

export interface TeamLabDevicePackagePageModel {
  items?: TeamLabDevicePackageModel[];
  next?: string | null;
}

export interface TeamLabDevicePackageModel {
  /** @format guid */
  id?: string;
  name?: string;
  displayName?: string;
  version?: string;
  artifactKind?: string;
  artifactReference?: string;
  digest?: string | null;
  description?: string | null;
  supportedAssetKinds?: string[];
  /** @format int32 */
  cpuMillis?: number;
  /** @format int32 */
  memoryMib?: number;
  /** @format int32 */
  storageGib?: number;
  ports?: TeamLabDevicePackagePortModel[];
  parameterSchema?: any;
  healthDeclaration?: any;
  protocolEventTypes?: string[];
  enabled?: boolean;
  archived?: boolean;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
  /** @format int32 */
  bindingId?: number;
}

export interface TeamLabDevicePackagePortModel {
  name?: string;
  /** @format int32 */
  port?: number;
  protocol?: string;
}

export interface TeamLabConnectorPageModel {
  items?: TeamLabConnectorModel[];
  next?: string | null;
}

export interface TeamLabConnectorModel {
  /** @format guid */
  id?: string;
  name?: string;
  displayName?: string;
  kind?: string;
  /** @format guid */
  controlScopeId?: string | null;
  supportsSharedUse?: boolean;
  /** @format int32 */
  capacity?: number;
  /** @format int32 */
  occupiedSlots?: number;
  activeLeases?: TeamLabConnectorLeaseModel[];
  health?: string;
  /** @format uint64 */
  healthObservedAt?: number | null;
  description?: string | null;
  archived?: boolean;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
}

export interface TeamLabConnectorLeaseModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  connectorId?: string;
  /** @format guid */
  runtimeId?: string;
  /** @format int32 */
  slot?: number;
  /** @format uint64 */
  acquiredAt?: number;
  /** @format uint64 */
  releasedAt?: number | null;
  releaseReason?: string;
}

export interface TeamLabResourcePoolSnapshotModel {
  computeNodes?: TeamLabComputeNodePoolModel[];
  templates?: TeamLabTemplatePoolModel[];
}

export interface TeamLabComputeNodePoolModel {
  /** @format guid */
  id?: string;
  name?: string;
  status?: string;
  schedulable?: boolean;
  dockerCapable?: boolean;
  kvmCapable?: boolean;
  teamLabNetworkEnabled?: boolean;
  fabricStatus?: string;
  /** @format int32 */
  currentContainers?: number;
  /** @format int32 */
  maxContainers?: number;
  /** @format int32 */
  currentVms?: number;
  /** @format int32 */
  maxVms?: number;
  /** @format double */
  cpuLoadPercent?: number;
  /** @format double */
  memoryLoadPercent?: number;
  agentVersion?: string | null;
  /** @format uint64 */
  lastHeartbeat?: number | null;
  /** @format uint64 */
  metricObservedAt?: number | null;
}

export interface TeamLabTemplatePoolModel {
  /** @format int32 */
  id?: number;
  name?: string;
  osType?: string;
  imageType?: string;
  status?: string;
  /** @format int64 */
  fileSizeBytes?: number;
  digest?: string | null;
  supportsInstanceCredentials?: boolean;
  /** @format uint64 */
  uploadedAt?: number;
}

export interface TeamLabNodeCachePageModel {
  items?: TeamLabNodeCachePoolModel[];
  next?: string | null;
}

export interface TeamLabNodeCachePoolModel {
  /** @format int32 */
  templateId?: number;
  /** @format guid */
  nodeId?: string;
  imageHash?: string | null;
  status?: string;
  operation?: string;
  stage?: string;
  /** @format int32 */
  attemptCount?: number;
  /** @format int32 */
  activeReferenceCount?: number;
  lastErrorCode?: string | null;
  /** @format uint64 */
  progressUpdatedAt?: number | null;
}

export interface RegisterTeamLabDevicePackageModel {
  name?: string;
  displayName?: string;
  version?: string;
  artifactKind?: string;
  artifactReference?: string;
  digest?: string | null;
  description?: string | null;
  supportedAssetKinds?: string[] | null;
  /** @format int32 */
  cpuMillis?: number;
  /** @format int32 */
  memoryMib?: number;
  /** @format int32 */
  storageGib?: number;
  ports?: TeamLabDevicePackagePortModel[] | null;
  parameterSchema?: any;
  healthDeclaration?: any;
  protocolEventTypes?: string[] | null;
}

export interface RegisterTeamLabConnectorModel {
  name?: string;
  displayName?: string;
  kind?: string;
  /** @format guid */
  controlScopeId?: string | null;
  supportsSharedUse?: boolean;
  /** @format int32 */
  capacity?: number;
  attachmentReference?: string | null;
  description?: string | null;
  managedNic?: TeamLabManagedNicModel | null;
}

export interface TeamLabManagedNicModel {
  /** @format guid */
  nodeId?: string;
  interfaceName?: string;
  macAddress?: string;
}

export interface SetTeamLabConnectorHealthModel {
  health?: string;
}

export interface ReleaseTeamLabConnectorLeaseModel {
  /** @format guid */
  runtimeId?: string;
}

export interface TeamLabRemoteSessionPage {
  items?: TeamLabRemoteSessionListItem[];
  /** @format int64 */
  nextCursor?: number | null;
}

export interface TeamLabRemoteSessionListItem {
  session?: TeamLabRemoteSessionModel;
  /** @format guid */
  workerNodeId?: string;
  /** @format guid */
  requestedByUserId?: string;
}

export interface TeamLabRemoteSessionModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  runtimeId?: string;
  /** @format int32 */
  assetId?: number;
  assetName?: string;
  protocol?: TeamLabRemoteProtocol;
  status?: TeamLabRemoteSessionStatus;
  reason?: string;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  expiresAt?: number;
  /** @format uint64 */
  connectedAt?: number | null;
  /** @format uint64 */
  endedAt?: number | null;
  endReason?: string | null;
}

export interface TeamLabRemoteAccessAvailabilityModel {
  /** @format int32 */
  assetId?: number;
  assetName?: string;
  protocol?: TeamLabRemoteProtocol | null;
  available?: boolean;
  unavailableReason?: string | null;
}

export interface CreateTeamLabRemoteSessionModel {
  reason?: string;
  vncConsole?: boolean;
}

export interface TeamLabRemoteConnectModel {
  url?: string;
  /** @format uint64 */
  expiresAt?: number;
}

export interface TeamLabRuntimeSearchPage {
  items?: TeamLabRuntimeSearchItem[];
  nextCursor?: string | null;
}

export interface TeamLabRuntimeSearchItem {
  /** @format guid */
  id?: string;
  /** @format guid */
  topologyId?: string | null;
  /** @format guid */
  releaseId?: string;
  reference?: string | null;
  /** @format int32 */
  generation?: number;
  status?: string;
  /** @format guid */
  createdById?: string | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format int32 */
  assetCount?: number;
  hasError?: boolean;
}

export interface TeamLabVmDiagnostics {
  state?: string;
  /** @format guid */
  nativeId?: string;
  /** @format uint64 */
  observedAt?: number;
}

export interface TeamLabRuntimeTaskPageModel {
  items?: TeamLabRuntimeTaskModel[];
  nextCursor?: string | null;
}

export interface TeamLabRuntimeTaskModel {
  /** @format guid */
  id?: string;
  /** @format int32 */
  generation?: number;
  operation?: string;
  status?: string;
  stage?: string;
  /** @format guid */
  operationId?: string | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  startedAt?: number | null;
  /** @format uint64 */
  completedAt?: number | null;
  errorCode?: string | null;
  blockedReasonCode?: string | null;
  retryable?: boolean;
}

export interface TeamLabContainerDiagnostics {
  state?: string;
  paused?: boolean;
  /** @format int64 */
  exitCode?: number;
  /** @format int64 */
  restartCount?: number;
  startedAt?: string;
  finishedAt?: string;
  logs?: string;
  truncated?: boolean;
  /** @format uint64 */
  observedAt?: number;
  logsError?: string | null;
}

export interface TeamLabAdminRuntimePageModel {
  items?: TeamLabAdminRuntimeSummaryModel[];
  nextCursor?: string | null;
}

export interface TeamLabAdminRuntimeSummaryModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  releaseId?: string;
  status?: TeamLabRuntimeStatus;
  stage?: string;
  openForAccess?: boolean;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number | null;
  error?: string | null;
}

export interface TeamLabRuntimeProjectionModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  releaseId?: string;
  /** @format int32 */
  generation?: number;
  executionModel?: TeamLabExecutionModel;
  status?: TeamLabRuntimeStatus;
  stage?: string;
  openForAccess?: boolean;
  shards?: TeamLabRuntimeShardProjectionModel[];
  networks?: TeamLabRuntimeNetworkProjectionModel[];
  assets?: TeamLabRuntimeAssetProjectionModel[];
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number | null;
  error?: string | null;
  /** @format guid */
  currentOperationId?: string | null;
  /** @format guid */
  deploymentQueueTicketId?: string | null;
  queueStatus?: DeploymentQueueTicketStatus | null;
  subStages?: TeamLabRuntimeSubStageProjectionModel[] | null;
  /** @format guid */
  controlScopeId?: string | null;
  /** @format int32 */
  releaseVersion?: number | null;
  recoveryActions?: string[] | null;
  failure?: TeamLabFailureProjectionModel | null;
  /** @format guid */
  managedRolloutId?: string | null;
}

export interface TeamLabRuntimeShardProjectionModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  workerNodeId?: string;
  workerNodeName?: string;
  status?: TeamLabRuntimeStatus;
  networkKeys?: string[];
  assetKeys?: string[];
  error?: string | null;
  failure?: TeamLabFailureProjectionModel | null;
}

export interface TeamLabFailureProjectionModel {
  code?: string;
  stage?: string;
  retryable?: boolean;
  actions?: string[];
  resourceType?: string | null;
  resourceId?: string | null;
  detail?: string | null;
}

export interface TeamLabRuntimeNetworkProjectionModel {
  key?: string;
  name?: string;
  cidr?: string;
  gatewayIp?: string;
}

export interface TeamLabRuntimeAssetProjectionModel {
  /** @format int32 */
  id?: number;
  key?: string;
  name?: string;
  kind?: TeamLabAssetKind;
  runtimeResourceId?: string | null;
  primaryIp?: string | null;
  status?: TeamLabRuntimeStatus;
  error?: string | null;
  failure?: TeamLabFailureProjectionModel | null;
}

export interface TeamLabRuntimeSubStageProjectionModel {
  id?: string;
  status?: string;
  message?: string | null;
}

export interface CreateTeamLabTrialRuntimeModel {
  /** @format guid */
  releaseId?: string;
  constraints?: TeamLabRuntimeConstraintsModel | null;
  overlays?: TeamLabRuntimeOverlayModel[] | null;
  externalReference?: string | null;
}

export interface TeamLabRuntimeConstraintsModel {
  preferredRegion?: string | null;
  requiredCapabilities?: string[];
}

export interface TeamLabRuntimeOverlayModel {
  assetKey?: string;
  secrets?: Record<string, string>;
}

export interface LogMessagePageModel {
  items?: LogMessageModel[];
  nextCursor?: string | null;
}

/** Log information (Admin) */
export interface LogMessageModel {
  /** @format int64 */
  id?: number;
  /** @format uint64 */
  time?: number;
  name?: string | null;
  level?: string | null;
  ip?: string | null;
  msg?: string | null;
  status?: TaskStatus | null;
  /** @format guid */
  correlationId?: string | null;
  traceId?: string | null;
  eventCode?: string | null;
  errorCategory?: string | null;
  errorCode?: string | null;
  /** @format guid */
  workerNodeId?: string | null;
  workerNodeName?: string | null;
  /** @format guid */
  deploymentTicketId?: string | null;
  resourceType?: string | null;
  resourceId?: string | null;
  resourceDisplayName?: string | null;
}

export interface ResetTeamLabRuntimeModel {
  overlays?: TeamLabRuntimeOverlayModel[] | null;
  /** @format guid */
  releaseId?: string | null;
}

export interface TeamLabRuntimeEventModel {
  /** @format int64 */
  cursor?: number;
  /** @format int32 */
  generation?: number;
  stage?: string;
  level?: TeamLabEventLevel;
  message?: string;
  objectType?: string | null;
  objectId?: string | null;
  /** @format uint64 */
  createdAt?: number;
}

export interface TeamLabLinkPolicyPageModel {
  items?: TeamLabLinkPolicyModel[];
  next?: string | null;
}

export interface TeamLabLinkPolicyModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  runtimeId?: string;
  networkKey?: string;
  assetKey?: string | null;
  kind?: string;
  parameters?: any;
  status?: string;
  /** @format uint64 */
  recoverAt?: number | null;
  /** @format uint64 */
  appliedAt?: number;
  /** @format uint64 */
  recoveredAt?: number | null;
  recoverOrigin?: string;
  lastError?: string | null;
}

export interface ApplyTeamLabLinkPolicyModel {
  /** @format guid */
  runtimeId?: string;
  networkKey?: string;
  assetKey?: string | null;
  kind?: string;
  parameters?: any;
  /** @format uint64 */
  recoverAt?: number | null;
}

export interface TeamLabTrafficFlowPageModel {
  items?: TeamLabTrafficFlowProjectionModel[];
  nextCursor?: string | null;
  completeness?: TeamLabTrafficCompletenessModel;
}

export interface TeamLabTrafficFlowProjectionModel {
  cursor?: string;
  /** @format guid */
  shardId?: string;
  networkKey?: string;
  sourceIp?: string;
  /** @format int32 */
  sourcePort?: number | null;
  destinationIp?: string;
  /** @format int32 */
  destinationPort?: number | null;
  protocol?: string;
  /** @format int64 */
  bytes?: number;
  /** @format int64 */
  packets?: number;
  /** @format uint64 */
  firstSeen?: number;
  /** @format uint64 */
  lastSeen?: number;
}

export interface TeamLabTrafficCompletenessModel {
  complete?: boolean;
  /** @format int64 */
  droppedRecords?: number;
}

export interface TeamLabTrafficPathPageModel {
  items?: TeamLabTrafficPathSummaryModel[];
  nextCursor?: string | null;
  completeness?: TeamLabTrafficCompletenessModel;
}

export interface TeamLabTrafficPathSummaryModel {
  cursor?: string;
  /** @format guid */
  id?: string;
  confidence?: TeamLabPathConfidence;
  sourceIp?: string;
  /** @format int32 */
  sourcePort?: number | null;
  destinationIp?: string;
  /** @format int32 */
  destinationPort?: number | null;
  protocol?: string;
  /** @format uint64 */
  startedAt?: number;
  /** @format uint64 */
  endedAt?: number;
  /** @format int32 */
  hopCount?: number;
}

export interface TeamLabTrafficPathModel {
  /** @format guid */
  id?: string;
  confidence?: TeamLabPathConfidence;
  sourceIp?: string;
  /** @format int32 */
  sourcePort?: number | null;
  destinationIp?: string;
  /** @format int32 */
  destinationPort?: number | null;
  protocol?: string;
  /** @format uint64 */
  startedAt?: number;
  /** @format uint64 */
  endedAt?: number;
  hops?: TeamLabTrafficPathHopModel[];
}

export interface TeamLabTrafficPathHopModel {
  /** @format int32 */
  ordinal?: number;
  /** @format uint64 */
  observedAt?: number;
  evidenceKind?: TeamLabTrafficEvidenceKind;
  observationPointKind?: TeamLabObservationPointKind;
  /** @format guid */
  shardId?: string | null;
  networkKey?: string | null;
  infrastructureKey?: string | null;
  assetKey?: string | null;
  direction?: string;
  sourceIp?: string;
  /** @format int32 */
  sourcePort?: number | null;
  destinationIp?: string;
  /** @format int32 */
  destinationPort?: number | null;
  protocol?: string;
}

export interface TeamLabAccessGrantModel {
  /** @format guid */
  id?: string;
  type?: string;
  clientAddress?: string;
  endpoint?: string;
  allowedIps?: string;
  dns?: string;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  expiresAt?: number | null;
  configurationDownloadUrl?: string | null;
}

export interface TeamLabAccessGrantCreateModel {
  type?: string;
}

export interface TeamLabCaptureModel {
  /** @format guid */
  id?: string;
  status?: TeamLabTrafficCaptureStatus;
  scope?: string;
  networkKey?: string | null;
  /** @format int64 */
  maxBytes?: number;
  /** @format int32 */
  maxSeconds?: number;
  /** @format int64 */
  capturedBytes?: number;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  startedAt?: number | null;
  /** @format uint64 */
  completedAt?: number | null;
  /** @format uint64 */
  expiresAt?: number | null;
  segments?: TeamLabCaptureSegmentModel[];
  error?: string | null;
}

export interface TeamLabCaptureSegmentModel {
  /** @format guid */
  id?: string;
  status?: TeamLabTrafficCaptureSegmentStatus;
  /** @format guid */
  observationPointId?: string;
  observationPointKind?: TeamLabObservationPointKind;
  networkKey?: string | null;
  infrastructureKey?: string | null;
  assetKey?: string | null;
  /** @format int64 */
  capturedBytes?: number;
  /** @format int64 */
  uploadedBytes?: number;
  sha256?: string | null;
  error?: string | null;
}

export interface CreateTeamLabCaptureModel {
  scope?: string;
  networkKey?: string | null;
  /** @format int32 */
  maxSeconds?: number;
  /** @format int64 */
  maxBytes?: number;
  /** @format int32 */
  expiresInSeconds?: number;
}

export interface TeamLabCapturePageModel {
  items?: TeamLabCaptureModel[];
  next?: string | null;
}

export interface TeamLabControlScopeModel {
  /** @format guid */
  id?: string;
  key?: string;
  displayName?: string;
  archived?: boolean;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
}

export interface TeamLabCapabilitiesModel {
  apiVersion?: string;
  topologySchemaVersions?: number[];
  assetKinds?: TeamLabAssetKind[];
  networkModel?: string;
  features?: TeamLabFeatureCapabilitiesModel;
  limits?: TeamLabContractLimitsModel;
}

export interface TeamLabFeatureCapabilitiesModel {
  multiNode?: boolean;
  linuxVm?: boolean;
  windowsVm?: boolean;
  trafficFlows?: boolean;
  onDemandPcap?: boolean;
  editorLayout?: boolean;
  /** @format int32 */
  editorLayoutVersion?: number;
  networkRegions?: boolean;
  rollouts?: boolean;
  pauseResume?: boolean;
}

export interface TeamLabContractLimitsModel {
  /** @format int32 */
  networksPerTopology?: number;
  /** @format int32 */
  assetsPerTopology?: number;
  /** @format int32 */
  interfacesPerAsset?: number;
}

export interface TeamLabAdminScenePageModel {
  items?: TeamLabAdminSceneSummaryModel[];
  nextCursor?: string | null;
}

export interface TeamLabAdminSceneSummaryModel {
  /** @format guid */
  id?: string;
  name?: string;
  /** @format guid */
  ownerId?: string | null;
  ownerDisplayName?: string;
  /** @format int32 */
  revision?: number;
  /** @format int32 */
  schemaVersion?: number;
  /** @format int32 */
  networkCount?: number;
  /** @format int32 */
  assetCount?: number;
  /** @format int32 */
  infrastructureCount?: number;
  latestRelease?: TeamLabAdminReleaseSummaryModel | null;
  validation?: TeamLabAdminValidationSummaryModel | null;
  latestTrialRuntime?: TeamLabAdminRuntimeSummaryModel | null;
  /** @format int32 */
  gameReferenceCount?: number;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
}

export interface TeamLabAdminReleaseSummaryModel {
  /** @format guid */
  id?: string;
  /** @format int32 */
  version?: number;
  /** @format int32 */
  sourceRevision?: number;
  contentHash?: string;
  /** @format uint64 */
  publishedAt?: number;
}

export interface TeamLabAdminValidationSummaryModel {
  /** @format int32 */
  revision?: number;
  valid?: boolean;
  /** @format int32 */
  issueCount?: number;
  /** @format uint64 */
  validatedAt?: number;
}

export interface TeamLabTopologyDetailModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  controlScopeId?: string | null;
  /** @format int32 */
  revision?: number;
  /** @format int32 */
  schemaVersion?: number;
  definition?: TeamLabTopologyDefinitionModel;
  editor?: TeamLabTopologyEditorModel;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
}

export interface TeamLabTopologyDefinitionModel {
  name?: string;
  networks?: TeamLabTopologyNetworkModel[];
  assets?: TeamLabTopologyAssetModel[];
  connections?: TeamLabTopologyConnectionModel[];
  infrastructure?: TeamLabTopologyInfrastructureModel[] | null;
  dependencies?: TeamLabTopologyDependencyModel[] | null;
  observation?: TeamLabObservationPolicyModel | null;
}

export interface TeamLabTopologyNetworkModel {
  key?: string;
  name?: string;
  addressPool?: TeamLabAddressPoolModel;
  isEntry?: boolean;
  /** @format int32 */
  orderIndex?: number;
}

export interface TeamLabAddressPoolModel {
  poolCidr?: string;
  /** @format int32 */
  runtimePrefixLength?: number;
}

export interface TeamLabTopologyAssetModel {
  key?: string;
  name?: string;
  kind?: TeamLabAssetKind;
  /** @format int32 */
  imageTemplateId?: number;
  resources?: TeamLabAssetResourceModel;
  interfaces?: TeamLabTopologyInterfaceModel[];
  /** @format int32 */
  exposePort?: number | null;
  healthCheck?: TeamLabHealthCheckModel | null;
  /** @format int32 */
  orderIndex?: number;
  endpointObservation?: TeamLabEndpointObservationMode;
  /** @format int32 */
  devicePackageId?: number | null;
  deviceParameters?: any;
  /** @format guid */
  connectorId?: string | null;
}

export interface TeamLabAssetResourceModel {
  /** @format int32 */
  cpuUnits?: number;
  /** @format int32 */
  memoryMiB?: number;
  /** @format int32 */
  storageMiB?: number;
}

export interface TeamLabTopologyInterfaceModel {
  key?: string;
  networkKey?: string;
  /** @format int32 */
  hostOffset?: number;
  primary?: boolean;
  /** @format int32 */
  orderIndex?: number;
}

export interface TeamLabHealthCheckModel {
  kind?: TeamLabHealthCheckKind;
  /** @format int32 */
  port?: number;
}

export interface TeamLabTopologyConnectionModel {
  key?: string;
  fromNetworkKey?: string;
  toNetworkKey?: string;
  viaAssetKey?: string | null;
  viaNodeKey?: string | null;
  direction?: TeamLabConnectionDirection | null;
}

export interface TeamLabTopologyInfrastructureModel {
  key?: string;
  name?: string;
  kind?: TeamLabInfrastructureKind;
  interfaces?: TeamLabTopologyInterfaceModel[];
  networkKey?: string | null;
}

export interface TeamLabTopologyDependencyModel {
  assetKey?: string;
  dependsOnKey?: string;
  condition?: TeamLabDependencyCondition;
}

export interface TeamLabObservationPolicyModel {
  flowMetadataEnabled?: boolean;
  onDemandPcapEnabled?: boolean;
  endpointObservation?: TeamLabEndpointObservationMode;
}

export interface TeamLabTopologyEditorModel {
  networks?: Record<string, TeamLabEditorItemModel>;
  assets?: Record<string, TeamLabEditorItemModel>;
  infrastructure?: Record<string, TeamLabEditorItemModel>;
}

export interface TeamLabEditorItemModel {
  /** @format double */
  x?: number;
  /** @format double */
  y?: number;
  /** @format double */
  width?: number | null;
  /** @format double */
  height?: number | null;
  collapsed?: boolean;
}

export interface CreateTeamLabTopologyModel {
  name?: string;
  networks?: TeamLabTopologyNetworkModel[];
  assets?: TeamLabTopologyAssetModel[];
  connections?: TeamLabTopologyConnectionModel[];
  editor?: TeamLabTopologyEditorModel | null;
  infrastructure?: TeamLabTopologyInfrastructureModel[] | null;
  dependencies?: TeamLabTopologyDependencyModel[] | null;
  observation?: TeamLabObservationPolicyModel | null;
  /** @format int32 */
  schemaVersion?: number;
  /** @format guid */
  controlScopeId?: string | null;
}

export interface UpdateTeamLabTopologyModel {
  /** @format int32 */
  revision?: number;
  name?: string;
  networks?: TeamLabTopologyNetworkModel[];
  assets?: TeamLabTopologyAssetModel[];
  connections?: TeamLabTopologyConnectionModel[];
  editor?: TeamLabTopologyEditorModel | null;
  infrastructure?: TeamLabTopologyInfrastructureModel[] | null;
  dependencies?: TeamLabTopologyDependencyModel[] | null;
  observation?: TeamLabObservationPolicyModel | null;
  /** @format int32 */
  schemaVersion?: number;
}

export interface TeamLabValidationResultModel {
  valid?: boolean;
  issues?: TeamLabValidationIssueModel[];
}

export interface TeamLabValidationIssueModel {
  code?: string;
  path?: string;
  message?: string;
}

export interface TeamLabReleaseModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  topologyId?: string;
  /** @format int32 */
  version?: number;
  /** @format int32 */
  sourceRevision?: number;
  /** @format int32 */
  schemaVersion?: number;
  contentHash?: string;
  /** @format guid */
  publishedBy?: string | null;
  publisherName?: string | null;
  /** @format uint64 */
  publishedAt?: number;
  editor?: TeamLabTopologyEditorModel | null;
  archived?: boolean;
}

export interface PublishTeamLabTopologyModel {
  /** @format int32 */
  revision?: number;
}

export interface TeamLabPlanModel {
  /** @format guid */
  topologyId?: string;
  /** @format guid */
  releaseId?: string;
  networks?: TeamLabPlanNetworkModel[];
  assets?: TeamLabPlanAssetModel[];
  shards?: TeamLabPlanShardModel[];
  /** @format int32 */
  crossShardConnections?: number;
  requiredCapabilities?: string[];
  warnings?: string[];
  planHash?: string;
  /** @format int32 */
  managedInfrastructureCount?: number;
  /** @format int32 */
  observationPointEstimate?: number;
}

export interface TeamLabPlanNetworkModel {
  key?: string;
  name?: string;
  candidateCidr?: string;
  isEntry?: boolean;
}

export interface TeamLabPlanAssetModel {
  key?: string;
  name?: string;
  kind?: TeamLabAssetKind;
  /** @format int32 */
  imageTemplateId?: number;
  resources?: TeamLabAssetResourceModel;
  interfaces?: TeamLabPlanInterfaceModel[];
}

export interface TeamLabPlanInterfaceModel {
  key?: string;
  networkKey?: string;
  /** @format int32 */
  hostOffset?: number;
  primary?: boolean;
}

export interface TeamLabPlanShardModel {
  key?: string;
  networkKeys?: string[];
  assetKeys?: string[];
  /** @format int32 */
  dockerSlots?: number;
  /** @format int32 */
  vmSlots?: number;
  infrastructureKeys?: string[] | null;
}

export interface TeamLabAdminReleaseReadinessModel {
  /** @format guid */
  topologyId?: string;
  /** @format guid */
  releaseId?: string;
  ready?: boolean;
  plan?: TeamLabPlanModel | null;
  images?: TeamLabAdminImageReadinessModel[];
  latestTrialRuntime?: TeamLabAdminRuntimeSummaryModel | null;
  blockingReasons?: string[];
}

export interface TeamLabAdminImageReadinessModel {
  /** @format int32 */
  imageTemplateId?: number;
  name?: string;
  imageType?: ImageType;
  /** @format int32 */
  eligibleNodeCount?: number;
  /** @format int32 */
  readyNodeCount?: number;
  /** @format int32 */
  pendingNodeCount?: number;
  /** @format int32 */
  failedNodeCount?: number;
}

export interface TeamLabAssetControlAvailability {
  allowed?: boolean;
  reason?: string | null;
}

export interface TeamLabQueueTicketResult {
  /** @format guid */
  ticketId?: string;
}

export interface TeamLabAssetControlCommand {
  /** @format int32 */
  generation?: number;
  action?: string;
  reason?: string;
  confirmed?: boolean;
}

export interface TeamLabAssetControlTask {
  /** @format guid */
  id?: string;
  status?: string;
  stage?: string | null;
  errorCode?: string | null;
  canRetry?: boolean;
}

export interface TeamLabFileResult {
  entries?: TeamLabFileEntry[] | null;
  /** @format byte */
  content?: Blob | null;
  hostKeySha256?: string | null;
}

export interface TeamLabFileEntry {
  name?: string;
  kind?: string;
  /** @format int64 */
  size?: number;
}

export interface TeamLabAssetFileCommand {
  /** @format int32 */
  generation?: number;
  operation?: string;
  path?: string;
  /** @format byte */
  content?: Blob | null;
  overwrite?: boolean;
  confirmed?: boolean;
}

export interface TeamLabDeviceHealthModel {
  /** @format int32 */
  assetId?: number;
  name?: string;
  /** @format int32 */
  generation?: number;
  observation?: TeamLabDeviceObservation | null;
  /** @format uint64 */
  nextProbeAt?: number | null;
}

export interface TeamLabDeviceObservation {
  status?: string;
  /** @format uint64 */
  observedAt?: number;
  errorCode?: string | null;
  bootId?: string | null;
  protocolCounters?: Record<string, number>;
}

export interface TeamLabRemoteAuditPage {
  state?: string;
  /** @format int32 */
  retentionDays?: number;
  items?: TeamLabRemoteAuditFileModel[];
}

export interface TeamLabRemoteAuditFileModel {
  /** @format int64 */
  id?: number;
  /** @format int64 */
  size?: number;
  sha256?: string;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  expiresAt?: number | null;
}

export interface RuntimeDifferencePreview {
  /** @format int32 */
  generation?: number;
  /** @format uint64 */
  observedAt?: number;
  operationInProgress?: boolean;
  items?: RuntimeResourceDifference[];
}

export interface RuntimeResourceDifference {
  /** @format int32 */
  assetId?: number | null;
  /** @format guid */
  workerNodeId?: string | null;
  resourceKind?: string;
  name?: string;
  expectedState?: string;
  actualState?: string | null;
  difference?: string;
  suggestedAction?: string | null;
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

/** Public account capabilities used to compose authentication pages. */
export interface AccountCapabilitiesModel {
  /** Whether local username and password login is available. */
  allowPasswordLogin?: boolean;
  /** Whether self-service account registration is available. */
  allowRegister?: boolean;
  /** Whether password recovery by email is available. */
  passwordRecoveryAvailable?: boolean;
  /** Whether new accounts require email confirmation. */
  emailConfirmationRequired?: boolean;
  /** Unified identity portal entry shown by the login page. */
  portalSso?: PortalSsoCapabilityModel;
}

export interface PortalSsoCapabilityModel {
  enabled?: boolean;
  entryUrl?: string | null;
}

export interface AccountSummaryModel {
  /** @format guid */
  id?: string;
  userName?: string;
  /** User role enumeration */
  role?: Role;
  bio?: string;
  avatar?: string | null;
  /** @format int32 */
  solved?: number;
  /** @format int32 */
  activeDays?: number;
  /** @format int32 */
  runningInstances?: number;
  /** @format int32 */
  pendingReviews?: number;
  continueItems?: AccountSummaryContinueItemModel[];
}

export interface AccountSummaryContinueItemModel {
  id?: string;
  kind?: string;
  title?: string;
  subtitle?: string;
  route?: string;
  /** @format uint64 */
  endsAt?: number | null;
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
  content?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  tags?: string[] | null;
  flagTemplate?: string;
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
  content?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  tags?: string[] | null;
  flagTemplate?: string;
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
  entry?: string | null;
  /** Publication status of the challenge entry point. */
  entryStatus?: ContainerEntryStatus;
  /**
   * Time when the challenge entry point became available.
   * @format uint64
   */
  entryReadyAt?: number | null;
  /** Player-safe route publication failure. */
  entryError?: string | null;
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

export interface DeploymentQueueStatusModel {
  /** @format guid */
  ticketId?: string;
  kind?: DeploymentQueueKind;
  status?: DeploymentQueueTicketStatus;
  operation?: RuntimeOperationKind;
  stage?: DeploymentStage;
  /** @format guid */
  targetNodeId?: string | null;
  targetNodeName?: string | null;
  /** @format int32 */
  queuePosition?: number;
  /** @format int32 */
  peopleAhead?: number;
  errorMessage?: string | null;
  blockedReasonCode?: string | null;
  stageMessage?: string | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  startedAt?: number | null;
  /** @format uint64 */
  completedAt?: number | null;
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

/** Basic exercise information */
export interface ExerciseInfoModel {
  /**
   * Exercise ID
   * @format int32
   */
  id?: number;
  /** Exercise title */
  title?: string;
  /** Difficulty of the exercise, used for tags, sorting, etc. */
  difficulty?: Difficulty;
  /** Exercise category */
  category?: ChallengeCategory;
  /** Exercise challenge type. */
  type?: ChallengeType;
  /** Whether the exercise is enabled. */
  isEnabled?: boolean;
  /** Additional tags for the exercise */
  tags?: string[] | null;
  /** Exercise points */
  credit?: boolean;
  /**
   * Origin of a public exercise-pool entry. Source challenges are copied into the
   * pool so their original game/course lifecycle remains isolated.
   */
  poolSource?: ExercisePoolSource;
  /**
   * Number of people who solved the exercise
   * @format int32
   */
  acceptedCount?: number;
  /**
   * Number of submissions
   * @format int32
   */
  submissionCount?: number;
  /** Whether the current user completed this exercise. */
  solved?: boolean;
  /**
   * Accepted submissions made by the current user.
   * @format int32
   */
  userAcceptedCount?: number;
  /**
   * Total submissions made by the current user.
   * @format int32
   */
  userSubmissionCount?: number;
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

export interface ExerciseImportFromGameModel {
  /** @format int32 */
  gameId?: number;
  challengeIds?: number[] | null;
}

export interface ExerciseImportFromTrainingModel {
  /** @format int32 */
  courseId?: number;
  challengeIds?: number[] | null;
}

export interface ExerciseCreateModel {
  title?: string;
  content?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  credit?: boolean;
  isEnabled?: boolean;
  tags?: string[] | null;
  hints?: string[] | null;
  containerImage?: string | null;
  /** @format int32 */
  memoryLimit?: number | null;
  /** @format int32 */
  storageLimit?: number | null;
  /** @format int32 */
  cpuCount?: number | null;
  /** @format int32 */
  exposePort?: number | null;
  networkMode?: NetworkMode | null;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  /** @format int32 */
  imageTemplateId?: number | null;
  flagTemplate?: string | null;
  /** @format int32 */
  submissionLimit?: number;
  flags?: ExerciseFlagCreateModel[] | null;
  attachment?: AttachmentCreateModel | null;
}

export type ExerciseFlagCreateModel = FlagCreateModel & {
  /** @format int32 */
  id?: number | null;
};

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

export interface SubmissionPageModel {
  items?: Submission[];
  nextCursor?: string | null;
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
  /**
   * Monotonic runtime generation used to reject stale create/control operations.
   * @format int32
   */
  runtimeGeneration?: number;
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
  /**
   * Stable owner identity used for compare-owner public port lease operations.
   * @format guid
   */
  publicPortLeaseId?: string | null;
  /**
   * Publication state of the player-facing entry.
   * Direct entries are ready immediately; gateway-backed entries require an acknowledgement.
   */
  entryStatus?: ContainerEntryStatus;
  /**
   * Time when the current player-facing route was confirmed by the gateway.
   * @format uint64
   */
  entryReadyAt?: number | null;
  /**
   * Sanitized route publication failure exposed to the player.
   * @maxLength 512
   */
  entryError?: string | null;
  /** Container instance access method */
  entry?: string;
  /** Player-facing entry. Pending and failed routes are intentionally withheld. */
  readyEntry?: string | null;
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
  maxContainers?: number;
  /** @format int32 */
  currentVms?: number;
  /** @format int32 */
  maxVms?: number;
  /** @format int32 */
  usedPorts?: number;
  /** @format int32 */
  totalPorts?: number;
  /** @format int64 */
  liveMetricSequence?: number;
  /** @format uint64 */
  liveMetricObservedAt?: number | null;
  /** @format uint64 */
  liveMetricReceivedAt?: number | null;
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
  teamLabFabricIp?: string | null;
  teamLabFabricStatus?: TeamLabFabricStatus;
  /** @maxLength 64 */
  agentVersion?: string | null;
  /** @maxLength 128 */
  agentBinarySha256?: string | null;
  /** @format int32 */
  capabilityManifestSchemaVersion?: number;
  /** @maxLength 8192 */
  capabilityManifestJson?: string;
  /** @maxLength 64 */
  capabilityHash?: string | null;
  /** @format uint64 */
  capabilityObservedAt?: number | null;
  agentUpdateState?: AgentUpdateState;
  agentUpdateWasSchedulable?: boolean;
  /** @maxLength 128 */
  agentUpdateExpectedSha256?: string | null;
  /** @maxLength 1024 */
  agentUpdateLastError?: string | null;
  /** @format uint64 */
  agentUpdateStartedAt?: number | null;
  /** @format uint64 */
  agentUpdateCompletedAt?: number | null;
  concurrencyToken?: number;
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
   * The image has been verified to consume instance-specific credentials through Cloudbase-Init.
   * Required for Windows player VM deployment.
   */
  supportsInstanceCredentials?: boolean;
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
  /** @format guid */
  createdById?: string | null;
  vmArtifactStatus?: VmArtifactStatus;
  vmRuntimeMode?: VmRuntimeMode;
  vmNetworkMode?: VmNetworkMode;
  /** @format int64 */
  preparedArtifactId?: number | null;
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
  /** Source classification shown in the public exercise pool. */
  poolSource?: ExercisePoolSource;
  /**
   * Original game ID when this entry was collected from a game.
   * @format int32
   */
  sourceGameId?: number | null;
  /**
   * Original course ID when this entry was collected from training.
   * @format int32
   */
  sourceTrainingCourseId?: number | null;
  /**
   * Original source challenge ID used for idempotent collection.
   * @format int32
   */
  sourceChallengeId?: number | null;
  /**
   * Original AWDP service ID when this entry was collected from an AWDP game.
   * @format int32
   */
  sourceAwdpServiceId?: number | null;
  /** Lowest role allowed to browse and run this pool entry. */
  minimumVisibleRole?: Role;
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
  /** Publication status of the challenge instance entry. */
  instanceEntryStatus?: ContainerEntryStatus | null;
  /**
   * Time when the current instance entry became available.
   * @format uint64
   */
  instanceEntryReadyAt?: number | null;
  /** Player-safe route publication failure. */
  instanceEntryError?: string | null;
  /** Attachment URL */
  url?: string | null;
  /**
   * Attachment file size
   * @format int64
   */
  fileSize?: number | null;
}

/** Multi-flag step metadata exposed to players without the answer value. */
export interface FlagStepInfo {
  /** @format int32 */
  id?: number;
  /** @format int32 */
  orderIndex?: number;
  description?: string | null;
}

/** Flag submission result */
export interface FlagSubmitResultModel {
  /**
   * Submission ID
   * @format int32
   */
  id: number;
  /** Answer verification result */
  status: AnswerResult;
  /** Blood rank awarded by this submission */
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
  /** Worker address for native RDP access (null until RDP is ready) */
  rdpHost?: string | null;
  /**
   * Worker proxy port for native RDP access (null until RDP is ready)
   * @format int32
   */
  rdpPort?: number | null;
  /** Fixed username configured on the image template */
  rdpUsername?: string | null;
  /** Fixed password configured on the image template */
  rdpPassword?: string | null;
  /** Guacamole RDP URL (null if not yet ready) */
  rdpUrl?: string | null;
  /**
   * When the VM was created
   * @format uint64
   */
  createdAt?: number;
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

export interface UpdateImageRemoteAccessModel {
  enabled?: boolean;
  protocol?: TeamLabRemoteProtocol;
  /** @format int32 */
  port?: number;
  username?: string | null;
  credential?: string | null;
  clearCredential?: boolean;
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

export interface PortMapAckRequest {
  revision?: string;
  succeeded?: boolean;
  leaseIds?: string[];
  error?: string | null;
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
  /** @format int64 */
  sequence?: number;
  /** @format uint64 */
  observedAt?: number | null;
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
  capabilityManifest?: AgentCapabilityManifest | null;
  teamLabFabricIp?: string | null;
  teamLabFabricStatus?: TeamLabFabricStatus | null;
}

export interface AgentCapabilityManifest {
  agentVersion?: string;
  binarySha256?: string | null;
  /** @format int32 */
  manifestSchemaVersion?: number;
  features?: string[];
  executionLimits?: AgentExecutionLimits;
  host?: AgentHostFacts;
  /** @format uint64 */
  observedAt?: number;
}

export interface AgentExecutionLimits {
  /** @format int32 */
  dockerCreates?: number;
  /** @format int32 */
  vmCreates?: number;
  /** @format int32 */
  dockerImageTransfers?: number;
  /** @format int32 */
  vmImageTransfers?: number;
  /** @format int32 */
  teamLabNetworkOperations?: number;
  /** @format int32 */
  controlOperations?: number;
  /** @format int32 */
  teamLabExecutionOperations?: number;
  /** @format int32 */
  artifactCleanupOperations?: number;
}

export interface AgentHostFacts {
  /** @format int32 */
  logicalCpu?: number;
  /** @format int64 */
  totalMemoryBytes?: number;
  /** @format int64 */
  availableVmImageStorageBytes?: number;
  kvmDevice?: boolean;
  cpuVirtualization?: boolean;
}

export interface OperationalEventViewPageModel {
  items?: OperationalEventViewModel[];
  nextCursor?: string | null;
}

export interface OperationalEventViewModel {
  event?: OperationalEventModel;
  domain?: string;
  labels?: OperationalEventLabels;
}

export interface OperationalEventModel {
  /** @format int64 */
  id?: number;
  /** @format uint64 */
  occurredAt?: number;
  /** @format guid */
  correlationId?: string;
  traceId?: string | null;
  eventCode?: string;
  severity?: OperationalEventSeverity;
  outcome?: OperationalEventOutcome;
  errorCategory?: OperationalErrorCategory | null;
  errorCode?: string | null;
  retryable?: boolean;
  message?: string;
  detail?: Record<string, any>;
  /** @format guid */
  actorUserId?: string | null;
  /** @format guid */
  ownerUserId?: string | null;
  /** @format int32 */
  ownerTeamId?: number | null;
  /** @format int32 */
  gameId?: number | null;
  /** @format int32 */
  courseId?: number | null;
  /** @format int32 */
  challengeId?: number | null;
  /** @format int32 */
  imageTemplateId?: number | null;
  /** @format guid */
  workerNodeId?: string | null;
  /** @format guid */
  deploymentTicketId?: string | null;
  /** @format int32 */
  teamLabRuntimeId?: number | null;
  /** @format guid */
  vmInstanceId?: string | null;
  subjectType?: string | null;
  subjectId?: string | null;
  subjectDisplayName?: string | null;
  resourceType?: string | null;
  resourceId?: string | null;
  resourceDisplayName?: string | null;
}

export interface OperationalEventLabels {
  actor?: string | null;
  owner?: string | null;
  team?: string | null;
  game?: string | null;
  course?: string | null;
  challenge?: string | null;
  imageTemplate?: string | null;
  workerNode?: string | null;
  deploymentTicket?: string | null;
  teamLabRuntime?: string | null;
  vmInstance?: string | null;
  subject?: string | null;
  resource?: string | null;
}

export interface OperationalCorrelationSummaryModel {
  /** @format guid */
  correlationId?: string;
  /** @format uint64 */
  startedAt?: number;
  /** @format uint64 */
  completedAt?: number;
  outcome?: OperationalEventOutcome;
  errorCategory?: OperationalErrorCategory | null;
  errorCode?: string | null;
  /** @format int32 */
  eventCount?: number;
  domains?: string[];
  workerNodes?: string[];
  subject?: string | null;
  resource?: string | null;
  timeline?: OperationalEventViewPageModel;
}

export interface BindPenetrationTopologyModel {
  /** @format guid */
  topologyId?: string;
}

export interface PenetrationGameLabBindingModel {
  /** @format int32 */
  gameId?: number;
  /** @format guid */
  topologyId?: string;
  /** @format guid */
  activeReleaseId?: string | null;
  /** @format int32 */
  maxResetCount?: number;
  /** @format int64 */
  objectiveRevision?: number;
  objectives?: PenetrationObjectiveModel[];
}

export interface PenetrationObjectiveModel {
  /** @format int32 */
  id?: number;
  key?: string;
  assetKey?: string;
  title?: string;
  description?: string | null;
  category?: string;
  /** @format int32 */
  score?: number;
  dynamic?: boolean;
  /** @format int32 */
  maxAttempts?: number;
  visible?: boolean;
  checkpoint?: boolean;
  prerequisiteKeys?: string[];
  /** @format int32 */
  orderIndex?: number;
}

export interface ReplacePenetrationObjectivesModel {
  /** @format int64 */
  revision?: number;
  /** @format int32 */
  maxResetCount?: number;
  objectives?: PenetrationObjectiveWriteModel[];
}

export interface PenetrationObjectiveWriteModel {
  key?: string;
  assetKey?: string;
  title?: string;
  description?: string | null;
  category?: string;
  /** @format int32 */
  score?: number;
  dynamic?: boolean;
  staticFlag?: string | null;
  flagTemplate?: string | null;
  /** @format int32 */
  maxAttempts?: number;
  visible?: boolean;
  checkpoint?: boolean;
  prerequisiteKeys?: string[] | null;
  /** @format int32 */
  orderIndex?: number;
  /** @format int32 */
  id?: number | null;
}

export interface TeamLabOperatorGrantWriteModel {
  viewAssets?: boolean;
  operateAssets?: boolean;
}

export interface PenetrationSubmitModel {
  /** @format int32 */
  objectiveId?: number;
  flag?: string;
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
  tags?: string[];
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
  supportsInstanceCredentials?: boolean;
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

export interface PublicUserProfileModel {
  /** @format guid */
  id?: string;
  userName?: string;
  /** User role enumeration */
  role?: Role;
  bio?: string;
  avatar?: string | null;
  /** @format uint64 */
  registeredAt?: number;
  publicTeam?: PublicUserTeamModel | null;
  taughtCourses?: PublicUserCourseModel[];
}

export interface PublicUserTeamModel {
  /** @format int32 */
  id?: number;
  name?: string;
  avatar?: string | null;
}

export interface PublicUserCourseModel {
  /** @format int32 */
  id?: number;
  title?: string;
}

export interface UserProfileOverviewModel {
  window?: string;
  /** @format uint64 */
  generatedAt?: number;
  metrics?: UserProfileMetricsModel;
  dimensions?: UserSkillDimensionModel[];
  trend?: UserProfileTrendPointModel[];
}

export interface UserProfileMetricsModel {
  /** @format int32 */
  solved?: number;
  /** @format int32 */
  submissions?: number;
  /** @format int32 */
  acceptedSubmissions?: number;
  /** @format double */
  successRate?: number;
  /** @format int32 */
  gameCount?: number;
  /** @format int32 */
  courseCount?: number;
  /** @format int32 */
  activeDays?: number;
}

export interface UserSkillDimensionModel {
  id?: string;
  label?: string;
  /** @format int32 */
  solved?: number;
  /** @format int32 */
  attempted?: number;
  /** @format int32 */
  submissions?: number;
  /** @format int32 */
  acceptedSubmissions?: number;
  /** @format double */
  successRate?: number;
  /** @format int32 */
  benchmarkP90?: number;
  /** @format double */
  radarValue?: number;
  sampleSufficient?: boolean;
}

export interface UserProfileTrendPointModel {
  /** @format date */
  date?: string;
  /** @format int32 */
  cumulativeSolved?: number;
  /** @format int32 */
  delta?: number;
}

export interface UserActivityPointModel {
  /** @format date */
  date?: string;
  /** @format int32 */
  ctf?: number;
  /** @format int32 */
  training?: number;
  /** @format int32 */
  theory?: number;
  /** @format int32 */
  awdp?: number;
  /** @format int32 */
  penetration?: number;
  /** @format int32 */
  total?: number;
}

export interface UserProfileHistoryPageModel {
  items?: UserProfileHistoryItemModel[];
  nextCursor?: string | null;
}

export interface UserProfileHistoryItemModel {
  id?: string;
  type?: string;
  /** @format uint64 */
  occurredAt?: number;
  title?: string;
  summary?: string;
  route?: string | null;
}

export interface UserPrivateOverviewModel {
  /** @format int32 */
  approvedCourses?: number;
  /** @format int32 */
  learningCourses?: number;
  /** @format int32 */
  completedCourses?: number;
  /** @format int32 */
  pendingEnrollments?: number;
  /** @format int32 */
  submittedTheoryAssignments?: number;
}

export type ExternalApiProblemDetailsModel = ProblemDetails & {
  code?: string;
  traceId?: string;
  [key: string]: any;
};

export interface AcquireTeamLabConnectorLeaseModel {
  /** @format guid */
  runtimeId?: string;
}

/** Release-level preparation state for external callers. */
export interface TeamLabReleasePreparationModel {
  /** @format guid */
  releaseId?: string;
  state?: string;
  planAvailable?: boolean;
  readyToStart?: boolean;
  blockers?: string[];
  images?: TeamLabReleaseImagePreparationModel[];
}

/** Per-template preparation projection. No worker address or Agent detail is exposed. */
export interface TeamLabReleaseImagePreparationModel {
  /** @format int32 */
  templateId?: number;
  templateName?: string;
  imageType?: string;
  /** @format int32 */
  eligibleNodeCount?: number;
  /** @format int32 */
  readyNodeCount?: number;
  /** @format int32 */
  preparingNodeCount?: number;
  /** @format int32 */
  failedNodeCount?: number;
  failure?: OpenTeamLabFailureModel | null;
}

export interface OpenTeamLabFailureModel {
  code?: string;
  stage?: string;
  retryable?: boolean;
  actions?: string[] | null;
  resourceType?: string | null;
  resourceId?: string | null;
  detail?: string | null;
}

export interface ApiOperationModel {
  /** @format guid */
  id?: string;
  kind?: string;
  status?: ApiOperationStatus;
  stage?: string;
  resourceType?: string | null;
  resourceId?: string | null;
  /** @format guid */
  deploymentQueueTicketId?: string | null;
  /** @format int64 */
  currentProgress?: number;
  /** @format int64 */
  totalProgress?: number;
  /** @format int32 */
  attemptCount?: number;
  errorCode?: string | null;
  errorDetail?: string | null;
  result?: any;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  startedAt?: number | null;
  /** @format uint64 */
  updatedAt?: number;
  /** @format uint64 */
  completedAt?: number | null;
}

export interface OpenTeamLabRemoteAvailabilityModel {
  /** @format int32 */
  assetId?: number;
  assetName?: string;
  protocol?: TeamLabRemoteProtocol | null;
  available?: boolean;
  unavailableReason?: string | null;
}

export interface OpenCreateTeamLabRemoteSessionModel {
  /**
   * @minLength 4
   * @maxLength 500
   */
  reason: string;
  vncConsole?: boolean;
}

export interface OpenTeamLabRemoteSessionModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  runtimeId?: string;
  /** @format int32 */
  assetId?: number;
  assetName?: string;
  protocol?: TeamLabRemoteProtocol;
  status?: TeamLabRemoteSessionStatus;
  reason?: string;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  expiresAt?: number;
  /** @format uint64 */
  connectedAt?: number | null;
  /** @format uint64 */
  endedAt?: number | null;
  endReason?: string | null;
}

export interface TeamLabRolloutPageModel {
  items?: TeamLabRolloutModel[];
  nextCursor?: string | null;
}

export interface TeamLabRolloutModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  releaseId?: string;
  status?: string;
  preparationRequested?: boolean;
  desiredAccessOpen?: boolean;
  drainRequested?: boolean;
  pauseRequested?: boolean;
  counts?: TeamLabRolloutCountsModel;
  /** @format uint64 */
  preparedAt?: number | null;
  /** @format uint64 */
  accessOpenedAt?: number | null;
  /** @format uint64 */
  drainingAt?: number | null;
  /** @format uint64 */
  completedAt?: number | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
  error?: string | null;
  /** @format guid */
  controlScopeId?: string | null;
  adapterKind?: string | null;
  externalReference?: string | null;
  /** @format int32 */
  revision?: number;
}

export interface TeamLabRolloutCountsModel {
  /** @format int32 */
  total?: number;
  /** @format int32 */
  pending?: number;
  /** @format int32 */
  provisioning?: number;
  /** @format int32 */
  ready?: number;
  /** @format int32 */
  accessOpen?: number;
  /** @format int32 */
  failed?: number;
  /** @format int32 */
  draining?: number;
  /** @format int32 */
  destroyed?: number;
  /** @format int32 */
  paused?: number;
}

export interface CreateTeamLabRolloutModel {
  /** @format guid */
  controlScopeId?: string;
  /** @format guid */
  releaseId?: string;
  externalReference?: string;
  targets?: TeamLabRolloutTargetInputModel[];
}

export interface TeamLabRolloutTargetInputModel {
  externalSubject?: string;
  displayName?: string;
}

export interface TeamLabRolloutTargetPageModel {
  items?: TeamLabRolloutTargetModel[];
  nextCursor?: string | null;
}

export interface TeamLabRolloutTargetModel {
  /** @format guid */
  id?: string;
  externalSubject?: string;
  displayName?: string;
  /** @format guid */
  runtimeId?: string | null;
  status?: string;
  /** @format guid */
  operationId?: string | null;
  runtimeStatus?: TeamLabRuntimeStatus | null;
  runtimeStage?: string | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
  error?: string | null;
}

export interface ReplaceTeamLabRolloutTargetsModel {
  targets?: TeamLabRolloutTargetInputModel[];
}

export interface CreateTeamLabRuntimeModel {
  /** @format guid */
  releaseId?: string;
  externalReference?: string | null;
  constraints?: TeamLabRuntimeConstraintsModel | null;
  overlays?: TeamLabRuntimeOverlayModel[] | null;
}

export interface OpenTeamLabRuntimeModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  releaseId?: string;
  /** @format int32 */
  generation?: number;
  executionModel?: TeamLabExecutionModel;
  status?: TeamLabRuntimeStatus;
  stage?: string;
  openForAccess?: boolean;
  shards?: OpenTeamLabRuntimeShardModel[];
  networks?: TeamLabRuntimeNetworkProjectionModel[];
  assets?: OpenTeamLabRuntimeAssetModel[];
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number | null;
  failure?: OpenTeamLabFailureModel | null;
  /** @format guid */
  currentOperationId?: string | null;
  /** @format guid */
  deploymentQueueTicketId?: string | null;
  queueStatus?: DeploymentQueueTicketStatus | null;
  subStages?: OpenTeamLabRuntimeSubStageModel[] | null;
  /** @format guid */
  controlScopeId?: string | null;
  /** @format int32 */
  releaseVersion?: number | null;
  recoveryActions?: string[] | null;
}

export interface OpenTeamLabRuntimeShardModel {
  /** @format guid */
  id?: string;
  status?: TeamLabRuntimeStatus;
  networkKeys?: string[];
  assetKeys?: string[];
  failure?: OpenTeamLabFailureModel | null;
}

export interface OpenTeamLabRuntimeAssetModel {
  key?: string;
  name?: string;
  kind?: TeamLabAssetKind;
  primaryIp?: string | null;
  status?: TeamLabRuntimeStatus;
  failure?: OpenTeamLabFailureModel | null;
}

export interface OpenTeamLabRuntimeSubStageModel {
  id?: string;
  status?: string;
  message?: string | null;
}

export interface TeamLabProtocolEventReportModel {
  type?: string;
  source?: string;
  /** @format uint64 */
  occurredAt?: number | null;
  parameters?: Record<string, string>;
}

export interface OpenTeamLabRuntimeEventPageModel {
  items?: TeamLabRuntimeEventModel[];
  nextCursor?: string | null;
}

export interface CreateTeamLabControlScopeModel {
  key?: string;
  displayName?: string;
}

export interface OpenCreateTeamLabTopologyModel {
  name?: string;
  networks?: TeamLabTopologyNetworkModel[];
  assets?: TeamLabTopologyAssetModel[];
  connections?: TeamLabTopologyConnectionModel[];
  editor?: TeamLabTopologyEditorModel | null;
  infrastructure?: TeamLabTopologyInfrastructureModel[] | null;
  dependencies?: TeamLabTopologyDependencyModel[] | null;
  observation?: TeamLabObservationPolicyModel | null;
  /** @format int32 */
  schemaVersion?: number;
  /** @format guid */
  controlScopeId?: string | null;
}

export interface OpenTeamLabTopologyPageModel {
  items?: TeamLabTopologySummaryModel[];
  nextCursor?: string | null;
}

export interface TeamLabTopologySummaryModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  controlScopeId?: string | null;
  name?: string;
  /** @format int32 */
  revision?: number;
  /** @format int32 */
  schemaVersion?: number;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
}

export interface OpenTeamLabTopologyDetailModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  controlScopeId?: string | null;
  /** @format int32 */
  revision?: number;
  /** @format int32 */
  schemaVersion?: number;
  definition?: TeamLabTopologyDefinitionModel;
  editor?: TeamLabTopologyEditorModel;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number;
}

export interface OpenUpdateTeamLabTopologyModel {
  /** @format int32 */
  revision?: number;
  name?: string;
  networks?: TeamLabTopologyNetworkModel[];
  assets?: TeamLabTopologyAssetModel[];
  connections?: TeamLabTopologyConnectionModel[];
  editor?: TeamLabTopologyEditorModel | null;
  infrastructure?: TeamLabTopologyInfrastructureModel[] | null;
  dependencies?: TeamLabTopologyDependencyModel[] | null;
  observation?: TeamLabObservationPolicyModel | null;
  /** @format int32 */
  schemaVersion?: number;
}

export interface OpenTeamLabReleasePageModel {
  items?: OpenTeamLabReleaseModel[];
  nextCursor?: string | null;
}

export interface OpenTeamLabReleaseModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  topologyId?: string;
  /** @format int32 */
  version?: number;
  /** @format int32 */
  sourceRevision?: number;
  /** @format int32 */
  schemaVersion?: number;
  contentHash?: string;
  /** @format uint64 */
  publishedAt?: number;
  editor?: TeamLabTopologyEditorModel | null;
  archived?: boolean;
  publisherName?: string | null;
}

export interface OpenTeamLabCapturePageModel {
  items?: OpenTeamLabCaptureModel[];
  next?: string | null;
}

export interface OpenTeamLabCaptureModel {
  /** @format guid */
  id?: string;
  status?: TeamLabTrafficCaptureStatus;
  scope?: string;
  networkKey?: string | null;
  /** @format int64 */
  maxBytes?: number;
  /** @format int32 */
  maxSeconds?: number;
  /** @format int64 */
  capturedBytes?: number;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  startedAt?: number | null;
  /** @format uint64 */
  completedAt?: number | null;
  /** @format uint64 */
  expiresAt?: number | null;
  segments?: TeamLabCaptureSegmentModel[];
  failure?: OpenTeamLabFailureModel | null;
}

export interface CreateTeamLabWebhookModel {
  /** @format guid */
  controlScopeId?: string;
  endpointUrl?: string;
  eventTypes?: string[];
  enabled?: boolean;
  /** @format int64 */
  fromEventId?: number | null;
}

export interface TeamLabWebhookPageModel {
  items?: TeamLabWebhookModel[];
  nextCursor?: string | null;
}

export interface TeamLabWebhookModel {
  /** @format guid */
  id?: string;
  /** @format guid */
  controlScopeId?: string;
  endpointUrl?: string;
  eventTypes?: string[];
  active?: boolean;
  /** @format int64 */
  deliveryCursor?: number;
  /** @format int32 */
  consecutiveFailures?: number;
  /** @format uint64 */
  nextDeliveryAt?: number | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  revokedAt?: number | null;
  recentFailures?: TeamLabWebhookFailureModel[];
}

export interface TeamLabWebhookFailureModel {
  /** @format int64 */
  id?: number;
  /** @format int64 */
  eventId?: number;
  eventStage?: string;
  error?: string;
  /** @format uint64 */
  occurredAt?: number;
}

export interface TeamImportBatchModel {
  /**
   * @maxItems 200
   * @minItems 1
   */
  items: TeamImportModel[];
}

export type TeamImportModel = ExternalImportItemModel & {
  /**
   * @minLength 1
   * @maxLength 20
   */
  name: string;
  /** @maxLength 72 */
  bio?: string | null;
  locked?: boolean;
  captain: ExternalUserReferenceModel;
  /** @maxItems 100 */
  members?: ExternalUserReferenceModel[];
};

export interface ExternalUserReferenceModel {
  /** @format guid */
  userId?: string | null;
  /** @maxLength 64 */
  userName?: string | null;
}

export interface ExternalImportItemModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  externalId: string;
}

export interface TheoryQuestionImportBatchModel {
  /**
   * @maxItems 1000
   * @minItems 1
   */
  items: TheoryQuestionImportModel[];
}

export type TheoryQuestionImportModel = TheoryQuestionEditModel & {
  /**
   * @minLength 1
   * @maxLength 128
   */
  externalId: string;
};

export interface TheoryPaperImportModel {
  /**
   * @minLength 1
   * @maxLength 256
   */
  title: string;
  /** @maxLength 1000000 */
  description?: string;
  publish?: boolean;
  /**
   * @maxItems 1000
   * @minItems 1
   */
  questions: TheoryPaperQuestionImportModel[];
}

export type TheoryPaperQuestionImportModel = TheoryQuestionEditModel & {
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

export interface TrainingCourseImportBatchModel {
  /**
   * @maxItems 50
   * @minItems 1
   */
  items: TrainingCourseImportModel[];
}

export type TrainingCourseImportModel = ExternalImportItemModel & {
  /**
   * @minLength 1
   * @maxLength 128
   */
  title: string;
  /** @maxLength 128 */
  slug?: string;
  /** @maxLength 512 */
  summary?: string;
  /** @maxLength 1000000 */
  description?: string;
  /** @maxItems 16 */
  tags?: string[];
  /** Training course enrollment policy */
  enrollmentPolicy?: TrainingCourseEnrollmentPolicy;
  publish?: boolean;
  /** @maxItems 500 */
  chapters?: TrainingChapterImportModel[];
  /** @maxItems 500 */
  exercises?: TrainingExerciseImportModel[];
  /** @maxItems 1000 */
  theoryQuestions?: TrainingTheoryQuestionImportModel[];
  /** @maxItems 500 */
  theoryPapers?: TrainingTheoryPaperImportModel[];
};

export type TrainingChapterImportModel = ExternalImportItemModel & {
  /** @maxLength 128 */
  parentExternalId?: string | null;
  /**
   * @minLength 1
   * @maxLength 128
   */
  title: string;
  /** @maxLength 512 */
  summary?: string;
  /** @maxLength 1000000 */
  content?: string;
  /** Training article format */
  contentType?: TrainingArticleContentType;
  completionPolicy?: TrainingChapterCompletionPolicy;
  /** Training course video provider */
  videoProvider?: TrainingCourseVideoProvider;
  /** @maxLength 1024 */
  videoUrl?: string | null;
  /** @format int32 */
  order?: number;
  isPublished?: boolean;
};

export interface TrainingExerciseImportModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  externalId: string;
  /**
   * @minLength 1
   * @maxLength 256
   */
  title: string;
  /**
   * @minLength 1
   * @maxLength 1000000
   */
  content: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  credit?: boolean;
  isEnabled?: boolean;
  /** @maxItems 100 */
  tags?: string[] | null;
  /** @maxItems 512 */
  hints?: string[] | null;
  /** @maxLength 512 */
  containerImage?: string | null;
  /** @format int32 */
  memoryLimit?: number | null;
  /** @format int32 */
  storageLimit?: number | null;
  /** @format int32 */
  cpuCount?: number | null;
  /** @format int32 */
  exposePort?: number | null;
  networkMode?: NetworkMode | null;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  /** @format int32 */
  imageTemplateId?: number | null;
  /** @maxLength 120 */
  flagTemplate?: string | null;
  /** @maxItems 100 */
  flags?: ExerciseOpenApiFlagModel[] | null;
  attachment?: ExerciseOpenApiAttachmentModel | null;
  /** @maxLength 128 */
  chapterExternalId?: string | null;
  /** @format int32 */
  order?: number;
  isRequired?: boolean;
  /** @maxLength 128 */
  displayTitle?: string | null;
}

export interface ExerciseOpenApiFlagModel {
  /**
   * @format int32
   * @min 1
   * @max 2147483647
   */
  id?: number | null;
  /**
   * @minLength 1
   * @maxLength 127
   */
  flag: string;
  /**
   * @format int32
   * @min 0
   * @max 10000
   */
  orderIndex?: number;
  /** @maxLength 512 */
  description?: string | null;
  /** Flag score mode */
  scoreMode?: FlagScoreMode;
  /**
   * @format int32
   * @min 0
   * @max 1000000
   */
  fixedScore?: number;
  /**
   * @format int32
   * @min 0
   * @max 100000
   */
  maxAttempts?: number;
  /** @maxLength 128 */
  attachmentHash?: string | null;
  /** Answer type for challenge submission */
  answerType?: AnswerType;
  /** @maxLength 64 */
  customName?: string | null;
  attachment?: ExerciseOpenApiAttachmentModel | null;
}

export interface ExerciseOpenApiAttachmentModel {
  /** @maxLength 2048 */
  remoteUrl?: string;
  /** @maxLength 64 */
  fileHash?: string | null;
}

export type TrainingTheoryQuestionImportModel = TheoryQuestionEditModel & {
  /**
   * @minLength 1
   * @maxLength 128
   */
  externalId: string;
};

export type TrainingTheoryPaperImportModel = ExternalImportItemModel & {
  /**
   * @minLength 1
   * @maxLength 128
   */
  chapterExternalId: string;
  /**
   * @minLength 1
   * @maxLength 128
   */
  title: string;
  /** @maxLength 1000000 */
  description?: string;
  /**
   * @format int32
   * @min 1
   * @max 100
   */
  passRate?: number;
  allowRetake?: boolean;
  showCorrectAnswerAfterSubmit?: boolean;
  publish?: boolean;
  /** @maxItems 500 */
  questions?: TrainingTheoryPaperQuestionImportModel[];
};

export type TrainingTheoryPaperQuestionImportModel = TheoryQuestionEditModel & {
  /** @maxLength 128 */
  sourceQuestionExternalId?: string | null;
  /**
   * @format int32
   * @min 1
   * @max 2147483647
   */
  score?: number;
  /** @format int32 */
  order?: number;
};

export interface ExerciseExternalPageModel {
  items?: ExerciseExternalSummaryModel[];
  nextCursor?: string | null;
}

export interface ExerciseExternalSummaryModel {
  /** @format int32 */
  id?: number;
  title?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  credit?: boolean;
  tags?: string[];
  isEnabled?: boolean;
  /**
   * Origin of a public exercise-pool entry. Source challenges are copied into the
   * pool so their original game/course lifecycle remains isolated.
   */
  poolSource?: ExercisePoolSource;
}

export interface ExerciseExternalModel {
  /** @format int32 */
  id?: number;
  title?: string;
  content?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  credit?: boolean;
  tags?: string[];
  hints?: string[];
  isEnabled?: boolean;
  /**
   * Origin of a public exercise-pool entry. Source challenges are copied into the
   * pool so their original game/course lifecycle remains isolated.
   */
  poolSource?: ExercisePoolSource;
  containerImage?: string | null;
  /** @format int32 */
  memoryLimit?: number | null;
  /** @format int32 */
  storageLimit?: number | null;
  /** @format int32 */
  cpuCount?: number | null;
  /** @format int32 */
  exposePort?: number | null;
  networkMode?: NetworkMode | null;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  /** @format int32 */
  imageTemplateId?: number | null;
  flagTemplate?: string | null;
  attachment?: ExerciseOpenApiAttachmentModel | null;
  flags?: ExerciseOpenApiFlagInfoModel[];
}

export interface ExerciseOpenApiFlagInfoModel {
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
  attachmentHash?: string | null;
  /** Answer type for challenge submission */
  answerType?: AnswerType;
  customName?: string | null;
  attachment?: ExerciseOpenApiAttachmentModel | null;
}

export interface ExerciseImportFromExternalModel {
  /**
   * @maxItems 100
   * @minItems 1
   */
  items: ExerciseImportItemModel[];
}

export interface ExerciseImportItemModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  externalId: string;
  /**
   * @minLength 1
   * @maxLength 256
   */
  title: string;
  /**
   * @minLength 1
   * @maxLength 1000000
   */
  content: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  /** @maxItems 100 */
  tags?: string[] | null;
  /** @maxItems 512 */
  hints?: string[] | null;
  isEnabled?: boolean;
  credit?: boolean;
  containerImage?: string | null;
  /** @format int32 */
  memoryLimit?: number | null;
  /** @format int32 */
  storageLimit?: number | null;
  /** @format int32 */
  cpuCount?: number | null;
  /** @format int32 */
  exposePort?: number | null;
  networkMode?: NetworkMode | null;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  /** @format int32 */
  imageTemplateId?: number | null;
  flagTemplate?: string | null;
  /** @maxItems 100 */
  flags?: ExerciseOpenApiFlagModel[] | null;
  attachment?: ExerciseOpenApiAttachmentModel | null;
}

export interface ExerciseCreateModel2 {
  /**
   * @minLength 1
   * @maxLength 256
   */
  title: string;
  /** @minLength 1 */
  content: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  credit?: boolean;
  isEnabled?: boolean;
  /** @maxItems 100 */
  tags?: string[] | null;
  /** @maxItems 512 */
  hints?: string[] | null;
  containerImage?: string | null;
  /** @format int32 */
  memoryLimit?: number | null;
  /** @format int32 */
  storageLimit?: number | null;
  /** @format int32 */
  cpuCount?: number | null;
  /** @format int32 */
  exposePort?: number | null;
  networkMode?: NetworkMode | null;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  /** @format int32 */
  imageTemplateId?: number | null;
  flagTemplate?: string | null;
  /** @maxItems 100 */
  flags?: ExerciseOpenApiFlagModel[] | null;
  attachment?: ExerciseOpenApiAttachmentModel | null;
}

export interface OpenAwdpServiceImportModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  externalId: string;
  /**
   * @minLength 1
   * @maxLength 128
   */
  name: string;
  /** @maxLength 1000000 */
  content?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  /** Challenge difficulty */
  difficulty?: Difficulty;
  /** @maxItems 100 */
  tags?: string[] | null;
  /**
   * @minLength 1
   * @maxLength 120
   */
  flagTemplate: string;
  /**
   * @minLength 1
   * @maxLength 256
   */
  imageName: string;
  /**
   * @format int32
   * @min 1
   * @max 65535
   */
  exposePort?: number;
  /** @maxLength 65536 */
  checkerScript?: string | null;
  /** @maxLength 256 */
  checkerEntrypoint?: string | null;
  /** @maxLength 65536 */
  expScript?: string | null;
  /** @maxLength 256 */
  expEntrypoint?: string | null;
  /**
   * @format int32
   * @min 0
   * @max 1000000
   */
  originalScore?: number;
  /**
   * @format int32
   * @min 0
   * @max 1000000
   */
  attackPoints?: number;
  /**
   * @format int32
   * @min 0
   * @max 1000000
   */
  slaPoints?: number;
  /**
   * @format int32
   * @min 0
   * @max 1000000
   */
  patchPoints?: number;
  /**
   * @format int32
   * @min 0
   * @max 1000000
   */
  serviceAbnormalPenalty?: number;
  /**
   * @format int32
   * @min 1
   * @max 100000
   */
  maxAttackPerRound?: number;
  /**
   * @format int32
   * @min 1
   * @max 100000
   */
  attackPhaseMinutes?: number;
  /**
   * @format int32
   * @min 1
   * @max 100000
   */
  patchPhaseMinutes?: number;
  /**
   * @format int32
   * @min 1
   * @max 100000
   */
  totalRounds?: number;
  /**
   * @format int32
   * @min 0
   * @max 100000
   */
  maxResetCount?: number;
  /**
   * @format int32
   * @min 0
   * @max 100000
   */
  maxRecoveryCount?: number;
}

export interface OpenAwdpServiceBatchImportModel {
  /**
   * @maxItems 100
   * @minItems 1
   */
  items: OpenAwdpServiceImportModel[];
}

export interface OpenChallengePageModel {
  items?: OpenChallengeSummaryModel[];
  nextCursor?: string | null;
}

export interface OpenChallengeSummaryModel {
  /** @format int32 */
  id?: number;
  title?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  isEnabled?: boolean;
  /** @format uint64 */
  deadlineUtc?: number | null;
  /** @format int32 */
  originalScore?: number;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  /** @format int32 */
  imageTemplateId?: number | null;
}

export interface OpenChallengeModel {
  /** @format int32 */
  id?: number;
  title?: string;
  content?: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  hints?: string[];
  isEnabled?: boolean;
  /** @format uint64 */
  deadlineUtc?: number | null;
  /** @format int32 */
  submissionLimit?: number;
  /** @format int32 */
  originalScore?: number;
  /** @format double */
  minScoreRate?: number;
  /** @format double */
  difficulty?: number;
  disableBloodBonus?: boolean;
  flagTemplate?: string | null;
  /** Environment type for challenge deployment */
  environment?: EnvironmentType;
  containerImage?: string | null;
  /** @format int32 */
  exposePort?: number | null;
  /** @format int32 */
  imageTemplateId?: number | null;
  /** @format int32 */
  cpuCount?: number;
  /** @format int32 */
  memoryLimit?: number;
  /** @format int32 */
  storageLimit?: number;
  /** Container network mode */
  networkMode?: NetworkMode;
  enableTrafficCapture?: boolean;
  fileName?: string | null;
  flags?: OpenChallengeFlagInfoModel[];
  attachment?: OpenChallengeAttachmentInfoModel | null;
}

export interface OpenChallengeFlagInfoModel {
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
  attachmentHash?: string | null;
  /** Answer type for challenge submission */
  answerType?: AnswerType;
  customName?: string | null;
  attachment?: OpenChallengeAttachmentInfoModel | null;
}

export interface OpenChallengeAttachmentInfoModel {
  type?: FileType;
  url?: string;
}

export interface OpenChallengeImportModel {
  /**
   * @minLength 1
   * @maxLength 128
   */
  externalId: string;
  /**
   * @minLength 1
   * @maxLength 256
   */
  title: string;
  /**
   * @minLength 1
   * @maxLength 1000000
   */
  content: string;
  /** Challenge category */
  category?: ChallengeCategory;
  type?: ChallengeType;
  /** @maxItems 100 */
  hints?: string[] | null;
  isEnabled?: boolean;
  /** @format uint64 */
  deadlineUtc?: number | null;
  /**
   * @format int32
   * @min 0
   * @max 10000
   */
  submissionLimit?: number;
  /**
   * @format int32
   * @min 1
   * @max 1000000
   */
  originalScore?: number;
  /**
   * @format double
   * @min 0
   * @max 1
   */
  minScoreRate?: number;
  /**
   * @format double
   * @min 0.01
   * @max 1000000
   */
  difficulty?: number;
  disableBloodBonus?: boolean;
  /** @maxLength 120 */
  flagTemplate?: string | null;
  environment?: EnvironmentType | null;
  /** @maxLength 512 */
  containerImage?: string | null;
  /**
   * @format int32
   * @min 1
   * @max 65535
   */
  exposePort?: number | null;
  /** @format int32 */
  imageTemplateId?: number | null;
  /**
   * @format int32
   * @min 1
   * @max 1024
   */
  cpuCount?: number;
  /**
   * @format int32
   * @min 32
   * @max 1048576
   */
  memoryLimit?: number;
  /**
   * @format int32
   * @min 0
   * @max 1048576
   */
  storageLimit?: number;
  /** Container network mode */
  networkMode?: NetworkMode;
  enableTrafficCapture?: boolean;
  /** @maxLength 256 */
  fileName?: string | null;
  /** @maxItems 100 */
  flags: OpenChallengeFlagModel[];
  attachment?: OpenChallengeAttachmentModel | null;
}

export interface OpenChallengeFlagModel {
  /**
   * @minLength 1
   * @maxLength 127
   */
  flag: string;
  /**
   * @format int32
   * @min 0
   * @max 10000
   */
  orderIndex?: number;
  /** @maxLength 512 */
  description?: string | null;
  /** Flag score mode */
  scoreMode?: FlagScoreMode;
  /**
   * @format int32
   * @min 0
   * @max 1000000
   */
  fixedScore?: number;
  /**
   * @format int32
   * @min 0
   * @max 100000
   */
  maxAttempts?: number;
  /** @maxLength 128 */
  attachmentHash?: string | null;
  /** Answer type for challenge submission */
  answerType?: AnswerType;
  /** @maxLength 64 */
  customName?: string | null;
  attachment?: OpenChallengeAttachmentModel | null;
}

export interface OpenChallengeAttachmentModel {
  /**
   * @minLength 1
   * @maxLength 2048
   */
  remoteUrl: string;
}

export interface OpenChallengeBatchImportModel {
  /**
   * @maxItems 100
   * @minItems 1
   */
  items: OpenChallengeImportModel[];
}

export interface OpenChallengeBatchDeleteModel {
  /**
   * @maxItems 100
   * @minItems 1
   */
  challengeIds: number[];
}

export interface AssetDescriptor {
  hash?: string;
  name?: string;
  /** @format int64 */
  size?: number;
  remoteUrl?: string;
}

export interface BootstrapProfileCreateModel {
  name?: string;
  description?: string | null;
}

export interface BootstrapProfileCursorPage {
  items?: BootstrapProfileModel[];
  nextCursor?: string | null;
}

export interface BootstrapProfileModel {
  /** @format guid */
  id?: string;
  name?: string;
  description?: string | null;
  status?: BootstrapProfileStatus;
  /** @format int32 */
  latestVersion?: number | null;
  /** @format uint64 */
  createdAt?: number;
  /** @format uint64 */
  updatedAt?: number | null;
}

export interface DockerImageReferenceImportModel {
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
  /** @maxLength 128 */
  expectedDigest?: string | null;
}

export interface OpenImageTemplateModel {
  /** @format int32 */
  id?: number;
  name?: string;
  osType?: OSType;
  imageType?: ImageType;
  status?: ImageStatus;
  registryUrl?: string | null;
  /** @format int64 */
  fileSize?: number;
  description?: string | null;
  errorMessage?: string | null;
  imageHash?: string | null;
  vmArtifactStatus?: VmArtifactStatus;
  vmRuntimeMode?: VmRuntimeMode;
  vmNetworkMode?: VmNetworkMode;
  /** @format uint64 */
  uploadedAt?: number;
}

export interface ImageTemplateCertificationRequest {
  capabilities?: string[];
  evidenceDigest?: string | null;
  probeKind?: string;
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
 * YINYU CTF Platform internal API document
 */
export class Api<
  SecurityDataType extends unknown,
> extends HttpClient<SecurityDataType> {
  internalTeamLabCaptureUpload = {
    /**
     * No description
     *
     * @tags InternalTeamLabCaptureUpload
     * @name InternalTeamLabCaptureUploadUpload
     * @request PUT:/api/internal/teamlab/captures/{captureId}/segments/{segmentId}
     */
    internalTeamLabCaptureUploadUpload: (
      captureId: string,
      segmentId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/internal/teamlab/captures/${captureId}/segments/${segmentId}`,
        method: "PUT",
        ...params,
      }),
  };
  teamLabAdminCapabilityResources = {
    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesArchiveConnector
     * @request POST:/api/admin/teamlab/connectors/{connectorId}/archive
     */
    teamLabAdminCapabilityResourcesArchiveConnector: (
      connectorId: string,
      params: RequestParams = {},
    ) =>
      this.request<void, any>({
        path: `/api/admin/teamlab/connectors/${connectorId}/archive`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesArchiveDevicePackage
     * @request POST:/api/admin/teamlab/device-packages/{packageId}/archive
     */
    teamLabAdminCapabilityResourcesArchiveDevicePackage: (
      packageId: string,
      params: RequestParams = {},
    ) =>
      this.request<void, any>({
        path: `/api/admin/teamlab/device-packages/${packageId}/archive`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesDisableDevicePackage
     * @request POST:/api/admin/teamlab/device-packages/{packageId}/disable
     */
    teamLabAdminCapabilityResourcesDisableDevicePackage: (
      packageId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabDevicePackageModel, any>({
        path: `/api/admin/teamlab/device-packages/${packageId}/disable`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesEnableDevicePackage
     * @request POST:/api/admin/teamlab/device-packages/{packageId}/enable
     */
    teamLabAdminCapabilityResourcesEnableDevicePackage: (
      packageId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabDevicePackageModel, any>({
        path: `/api/admin/teamlab/device-packages/${packageId}/enable`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesGetDevicePackage
     * @request GET:/api/admin/teamlab/device-packages/{packageId}
     */
    teamLabAdminCapabilityResourcesGetDevicePackage: (
      packageId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabDevicePackageModel, any>({
        path: `/api/admin/teamlab/device-packages/${packageId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesGetDevicePackage
     * @request GET:/api/admin/teamlab/device-packages/{packageId}
     */
    useTeamLabAdminCapabilityResourcesGetDevicePackage: (
      packageId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabDevicePackageModel, any>(
        doFetch ? `/api/admin/teamlab/device-packages/${packageId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesGetDevicePackage
     * @request GET:/api/admin/teamlab/device-packages/{packageId}
     */
    mutateTeamLabAdminCapabilityResourcesGetDevicePackage: (
      packageId: string,
      data?: TeamLabDevicePackageModel | Promise<TeamLabDevicePackageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabDevicePackageModel>(
        `/api/admin/teamlab/device-packages/${packageId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesListConnectors
     * @request GET:/api/admin/teamlab/connectors
     */
    teamLabAdminCapabilityResourcesListConnectors: (
      query?: {
        /** @format guid */
        scopeId?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabConnectorPageModel, any>({
        path: `/api/admin/teamlab/connectors`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesListConnectors
     * @request GET:/api/admin/teamlab/connectors
     */
    useTeamLabAdminCapabilityResourcesListConnectors: (
      query?: {
        /** @format guid */
        scopeId?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabConnectorPageModel, any>(
        doFetch ? [`/api/admin/teamlab/connectors`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesListConnectors
     * @request GET:/api/admin/teamlab/connectors
     */
    mutateTeamLabAdminCapabilityResourcesListConnectors: (
      query?: {
        /** @format guid */
        scopeId?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: TeamLabConnectorPageModel | Promise<TeamLabConnectorPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabConnectorPageModel>(
        [`/api/admin/teamlab/connectors`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesListDevicePackages
     * @request GET:/api/admin/teamlab/device-packages
     */
    teamLabAdminCapabilityResourcesListDevicePackages: (
      query?: {
        name?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabDevicePackagePageModel, any>({
        path: `/api/admin/teamlab/device-packages`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesListDevicePackages
     * @request GET:/api/admin/teamlab/device-packages
     */
    useTeamLabAdminCapabilityResourcesListDevicePackages: (
      query?: {
        name?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabDevicePackagePageModel, any>(
        doFetch ? [`/api/admin/teamlab/device-packages`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesListDevicePackages
     * @request GET:/api/admin/teamlab/device-packages
     */
    mutateTeamLabAdminCapabilityResourcesListDevicePackages: (
      query?: {
        name?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?:
        | TeamLabDevicePackagePageModel
        | Promise<TeamLabDevicePackagePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabDevicePackagePageModel>(
        [`/api/admin/teamlab/device-packages`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesListNodeCache
     * @request GET:/api/admin/teamlab/resource-pools/node-cache
     */
    teamLabAdminCapabilityResourcesListNodeCache: (
      query?: {
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabNodeCachePageModel, any>({
        path: `/api/admin/teamlab/resource-pools/node-cache`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesListNodeCache
     * @request GET:/api/admin/teamlab/resource-pools/node-cache
     */
    useTeamLabAdminCapabilityResourcesListNodeCache: (
      query?: {
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabNodeCachePageModel, any>(
        doFetch
          ? [`/api/admin/teamlab/resource-pools/node-cache`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesListNodeCache
     * @request GET:/api/admin/teamlab/resource-pools/node-cache
     */
    mutateTeamLabAdminCapabilityResourcesListNodeCache: (
      query?: {
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: TeamLabNodeCachePageModel | Promise<TeamLabNodeCachePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabNodeCachePageModel>(
        [`/api/admin/teamlab/resource-pools/node-cache`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesRegisterConnector
     * @request POST:/api/admin/teamlab/connectors
     */
    teamLabAdminCapabilityResourcesRegisterConnector: (
      data: RegisterTeamLabConnectorModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabConnectorModel, any>({
        path: `/api/admin/teamlab/connectors`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesRegisterDevicePackage
     * @request POST:/api/admin/teamlab/device-packages
     */
    teamLabAdminCapabilityResourcesRegisterDevicePackage: (
      data: RegisterTeamLabDevicePackageModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabDevicePackageModel, any>({
        path: `/api/admin/teamlab/device-packages`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesResourcePoolSnapshot
     * @request GET:/api/admin/teamlab/resource-pools
     */
    teamLabAdminCapabilityResourcesResourcePoolSnapshot: (
      params: RequestParams = {},
    ) =>
      this.request<TeamLabResourcePoolSnapshotModel, any>({
        path: `/api/admin/teamlab/resource-pools`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesResourcePoolSnapshot
     * @request GET:/api/admin/teamlab/resource-pools
     */
    useTeamLabAdminCapabilityResourcesResourcePoolSnapshot: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabResourcePoolSnapshotModel, any>(
        doFetch ? `/api/admin/teamlab/resource-pools` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesResourcePoolSnapshot
     * @request GET:/api/admin/teamlab/resource-pools
     */
    mutateTeamLabAdminCapabilityResourcesResourcePoolSnapshot: (
      data?:
        | TeamLabResourcePoolSnapshotModel
        | Promise<TeamLabResourcePoolSnapshotModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabResourcePoolSnapshotModel>(
        `/api/admin/teamlab/resource-pools`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesRevokeConnectorLease
     * @request POST:/api/admin/teamlab/connectors/{connectorId}/leases/revoke
     */
    teamLabAdminCapabilityResourcesRevokeConnectorLease: (
      connectorId: string,
      data: ReleaseTeamLabConnectorLeaseModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabConnectorLeaseModel, any>({
        path: `/api/admin/teamlab/connectors/${connectorId}/leases/revoke`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminCapabilityResources
     * @name TeamLabAdminCapabilityResourcesSetConnectorHealth
     * @request POST:/api/admin/teamlab/connectors/{connectorId}/health
     */
    teamLabAdminCapabilityResourcesSetConnectorHealth: (
      connectorId: string,
      data: SetTeamLabConnectorHealthModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabConnectorModel, any>({
        path: `/api/admin/teamlab/connectors/${connectorId}/health`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  teamLabAdminRemoteAccess = {
    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessConnect
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/connect
     */
    teamLabAdminRemoteAccessConnect: (
      sessionId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRemoteConnectModel, any>({
        path: `/api/admin/teamlab/remote-sessions/${sessionId}/connect`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessConnect
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/connect
     */
    useTeamLabAdminRemoteAccessConnect: (
      sessionId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRemoteConnectModel, any>(
        doFetch
          ? `/api/admin/teamlab/remote-sessions/${sessionId}/connect`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessConnect
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/connect
     */
    mutateTeamLabAdminRemoteAccessConnect: (
      sessionId: string,
      data?: TeamLabRemoteConnectModel | Promise<TeamLabRemoteConnectModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRemoteConnectModel>(
        `/api/admin/teamlab/remote-sessions/${sessionId}/connect`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessCreate
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-sessions
     */
    teamLabAdminRemoteAccessCreate: (
      runtimeId: string,
      assetId: number,
      data: CreateTeamLabRemoteSessionModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRemoteSessionModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/remote-sessions`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessEnd
     * @request DELETE:/api/admin/teamlab/remote-sessions/{sessionId}
     */
    teamLabAdminRemoteAccessEnd: (
      sessionId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/remote-sessions/${sessionId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessGet
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}
     */
    teamLabAdminRemoteAccessGet: (
      sessionId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRemoteSessionModel, any>({
        path: `/api/admin/teamlab/remote-sessions/${sessionId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessGet
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}
     */
    useTeamLabAdminRemoteAccessGet: (
      sessionId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRemoteSessionModel, any>(
        doFetch ? `/api/admin/teamlab/remote-sessions/${sessionId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessGet
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}
     */
    mutateTeamLabAdminRemoteAccessGet: (
      sessionId: string,
      data?: TeamLabRemoteSessionModel | Promise<TeamLabRemoteSessionModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRemoteSessionModel>(
        `/api/admin/teamlab/remote-sessions/${sessionId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessGetAvailability
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-access
     */
    teamLabAdminRemoteAccessGetAvailability: (
      runtimeId: string,
      assetId: number,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRemoteAccessAvailabilityModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/remote-access`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessGetAvailability
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-access
     */
    useTeamLabAdminRemoteAccessGetAvailability: (
      runtimeId: string,
      assetId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRemoteAccessAvailabilityModel, any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/remote-access`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessGetAvailability
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-access
     */
    mutateTeamLabAdminRemoteAccessGetAvailability: (
      runtimeId: string,
      assetId: number,
      data?:
        | TeamLabRemoteAccessAvailabilityModel
        | Promise<TeamLabRemoteAccessAvailabilityModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRemoteAccessAvailabilityModel>(
        `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/remote-access`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessGetAvailabilityBatch
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/remote-access
     */
    teamLabAdminRemoteAccessGetAvailabilityBatch: (
      runtimeId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRemoteAccessAvailabilityModel[], any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/remote-access`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessGetAvailabilityBatch
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/remote-access
     */
    useTeamLabAdminRemoteAccessGetAvailabilityBatch: (
      runtimeId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRemoteAccessAvailabilityModel[], any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/remote-access`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessGetAvailabilityBatch
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/remote-access
     */
    mutateTeamLabAdminRemoteAccessGetAvailabilityBatch: (
      runtimeId: string,
      data?:
        | TeamLabRemoteAccessAvailabilityModel[]
        | Promise<TeamLabRemoteAccessAvailabilityModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRemoteAccessAvailabilityModel[]>(
        `/api/admin/teamlab/runtimes/${runtimeId}/remote-access`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessList
     * @request GET:/api/admin/teamlab/remote-sessions
     */
    teamLabAdminRemoteAccessList: (
      query?: {
        /** @format guid */
        runtimeId?: string | null;
        /** @format guid */
        workerNodeId?: string | null;
        /** @format guid */
        requestedByUserId?: string | null;
        status?: TeamLabRemoteSessionStatus | null;
        /** @format int64 */
        after?: number | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRemoteSessionPage, any>({
        path: `/api/admin/teamlab/remote-sessions`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessList
     * @request GET:/api/admin/teamlab/remote-sessions
     */
    useTeamLabAdminRemoteAccessList: (
      query?: {
        /** @format guid */
        runtimeId?: string | null;
        /** @format guid */
        workerNodeId?: string | null;
        /** @format guid */
        requestedByUserId?: string | null;
        status?: TeamLabRemoteSessionStatus | null;
        /** @format int64 */
        after?: number | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRemoteSessionPage, any>(
        doFetch ? [`/api/admin/teamlab/remote-sessions`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessList
     * @request GET:/api/admin/teamlab/remote-sessions
     */
    mutateTeamLabAdminRemoteAccessList: (
      query?: {
        /** @format guid */
        runtimeId?: string | null;
        /** @format guid */
        workerNodeId?: string | null;
        /** @format guid */
        requestedByUserId?: string | null;
        status?: TeamLabRemoteSessionStatus | null;
        /** @format int64 */
        after?: number | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
      },
      data?: TeamLabRemoteSessionPage | Promise<TeamLabRemoteSessionPage>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRemoteSessionPage>(
        [`/api/admin/teamlab/remote-sessions`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRemoteAccess
     * @name TeamLabAdminRemoteAccessTerminal
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/terminal
     */
    teamLabAdminRemoteAccessTerminal: (
      sessionId: string,
      params: RequestParams = {},
    ) =>
      this.request<void, any>({
        path: `/api/admin/teamlab/remote-sessions/${sessionId}/terminal`,
        method: "GET",
        ...params,
      }),
  };
  teamLabAdminRuntime = {
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeApplyLinkPolicy
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/link-policies
     */
    teamLabAdminRuntimeApplyLinkPolicy: (
      runtimeId: string,
      data: ApplyTeamLabLinkPolicyModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/link-policies`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeCreateAccessGrant
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/access-grants
     */
    teamLabAdminRuntimeCreateAccessGrant: (
      runtimeId: string,
      data: TeamLabAccessGrantCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabAccessGrantModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/access-grants`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeCreateTrial
     * @request POST:/api/admin/teamlab/runtimes/trials
     */
    teamLabAdminRuntimeCreateTrial: (
      data: CreateTeamLabTrialRuntimeModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRuntimeProjectionModel, any>({
        path: `/api/admin/teamlab/runtimes/trials`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDestroy
     * @request DELETE:/api/admin/teamlab/runtimes/{runtimeId}
     */
    teamLabAdminRuntimeDestroy: (
      runtimeId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRuntimeProjectionModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDiagnostics
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/diagnostics
     */
    teamLabAdminRuntimeDiagnostics: (
      runtimeId: string,
      assetId: number,
      query?: {
        /**
         * @format int32
         * @default 200
         */
        tail?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabContainerDiagnostics, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/diagnostics`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDiagnostics
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/diagnostics
     */
    useTeamLabAdminRuntimeDiagnostics: (
      runtimeId: string,
      assetId: number,
      query?: {
        /**
         * @format int32
         * @default 200
         */
        tail?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabContainerDiagnostics, any>(
        doFetch
          ? [
              `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/diagnostics`,
              query,
            ]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDiagnostics
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/diagnostics
     */
    mutateTeamLabAdminRuntimeDiagnostics: (
      runtimeId: string,
      assetId: number,
      query?: {
        /**
         * @format int32
         * @default 200
         */
        tail?: number;
      },
      data?: TeamLabContainerDiagnostics | Promise<TeamLabContainerDiagnostics>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabContainerDiagnostics>(
        [
          `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/diagnostics`,
          query,
        ],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDownloadAccessGrant
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/access-grants/{grantId}/download
     */
    teamLabAdminRuntimeDownloadAccessGrant: (
      runtimeId: string,
      grantId: string,
      query?: {
        token?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/access-grants/${grantId}/download`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDownloadAccessGrant
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/access-grants/{grantId}/download
     */
    useTeamLabAdminRuntimeDownloadAccessGrant: (
      runtimeId: string,
      grantId: string,
      query?: {
        token?: string;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? [
              `/api/admin/teamlab/runtimes/${runtimeId}/access-grants/${grantId}/download`,
              query,
            ]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDownloadAccessGrant
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/access-grants/{grantId}/download
     */
    mutateTeamLabAdminRuntimeDownloadAccessGrant: (
      runtimeId: string,
      grantId: string,
      query?: {
        token?: string;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        [
          `/api/admin/teamlab/runtimes/${runtimeId}/access-grants/${grantId}/download`,
          query,
        ],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDownloadCapture
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}/download
     */
    teamLabAdminRuntimeDownloadCapture: (
      runtimeId: string,
      captureId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}/download`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDownloadCapture
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}/download
     */
    useTeamLabAdminRuntimeDownloadCapture: (
      runtimeId: string,
      captureId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}/download`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeDownloadCapture
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}/download
     */
    mutateTeamLabAdminRuntimeDownloadCapture: (
      runtimeId: string,
      captureId: string,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}/download`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeEvents
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/events
     */
    teamLabAdminRuntimeEvents: (
      runtimeId: string,
      query?: {
        /**
         * @format int64
         * @default 0
         */
        after?: number;
        /**
         * @format int32
         * @default 100
         */
        limit?: number;
        /** @format int32 */
        generation?: number | null;
        stage?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRuntimeEventModel[], any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/events`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeEvents
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/events
     */
    useTeamLabAdminRuntimeEvents: (
      runtimeId: string,
      query?: {
        /**
         * @format int64
         * @default 0
         */
        after?: number;
        /**
         * @format int32
         * @default 100
         */
        limit?: number;
        /** @format int32 */
        generation?: number | null;
        stage?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRuntimeEventModel[], any>(
        doFetch
          ? [`/api/admin/teamlab/runtimes/${runtimeId}/events`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeEvents
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/events
     */
    mutateTeamLabAdminRuntimeEvents: (
      runtimeId: string,
      query?: {
        /**
         * @format int64
         * @default 0
         */
        after?: number;
        /**
         * @format int32
         * @default 100
         */
        limit?: number;
        /** @format int32 */
        generation?: number | null;
        stage?: string | null;
      },
      data?: TeamLabRuntimeEventModel[] | Promise<TeamLabRuntimeEventModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRuntimeEventModel[]>(
        [`/api/admin/teamlab/runtimes/${runtimeId}/events`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeFlows
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/traffic/flows
     */
    teamLabAdminRuntimeFlows: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @default 100
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        networkKey?: string | null;
        /** @format int32 */
        port?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabTrafficFlowPageModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/traffic/flows`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeFlows
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/traffic/flows
     */
    useTeamLabAdminRuntimeFlows: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @default 100
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        networkKey?: string | null;
        /** @format int32 */
        port?: number | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabTrafficFlowPageModel, any>(
        doFetch
          ? [`/api/admin/teamlab/runtimes/${runtimeId}/traffic/flows`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeFlows
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/traffic/flows
     */
    mutateTeamLabAdminRuntimeFlows: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @default 100
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        networkKey?: string | null;
        /** @format int32 */
        port?: number | null;
      },
      data?: TeamLabTrafficFlowPageModel | Promise<TeamLabTrafficFlowPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabTrafficFlowPageModel>(
        [`/api/admin/teamlab/runtimes/${runtimeId}/traffic/flows`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeGet
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}
     */
    teamLabAdminRuntimeGet: (runtimeId: string, params: RequestParams = {}) =>
      this.request<TeamLabRuntimeProjectionModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeGet
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}
     */
    useTeamLabAdminRuntimeGet: (
      runtimeId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRuntimeProjectionModel, any>(
        doFetch ? `/api/admin/teamlab/runtimes/${runtimeId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeGet
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}
     */
    mutateTeamLabAdminRuntimeGet: (
      runtimeId: string,
      data?:
        | TeamLabRuntimeProjectionModel
        | Promise<TeamLabRuntimeProjectionModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRuntimeProjectionModel>(
        `/api/admin/teamlab/runtimes/${runtimeId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeGetCapture
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}
     */
    teamLabAdminRuntimeGetCapture: (
      runtimeId: string,
      captureId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabCaptureModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeGetCapture
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}
     */
    useTeamLabAdminRuntimeGetCapture: (
      runtimeId: string,
      captureId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabCaptureModel, any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeGetCapture
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}
     */
    mutateTeamLabAdminRuntimeGetCapture: (
      runtimeId: string,
      captureId: string,
      data?: TeamLabCaptureModel | Promise<TeamLabCaptureModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabCaptureModel>(
        `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeLinkPolicies
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/link-policies
     */
    teamLabAdminRuntimeLinkPolicies: (
      runtimeId: string,
      query?: {
        status?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabLinkPolicyPageModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/link-policies`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeLinkPolicies
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/link-policies
     */
    useTeamLabAdminRuntimeLinkPolicies: (
      runtimeId: string,
      query?: {
        status?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabLinkPolicyPageModel, any>(
        doFetch
          ? [`/api/admin/teamlab/runtimes/${runtimeId}/link-policies`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeLinkPolicies
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/link-policies
     */
    mutateTeamLabAdminRuntimeLinkPolicies: (
      runtimeId: string,
      query?: {
        status?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: TeamLabLinkPolicyPageModel | Promise<TeamLabLinkPolicyPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabLinkPolicyPageModel>(
        [`/api/admin/teamlab/runtimes/${runtimeId}/link-policies`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeList
     * @request GET:/api/admin/teamlab/runtimes
     */
    teamLabAdminRuntimeList: (
      query?: {
        /** @format guid */
        topologyId?: string | null;
        after?: string | null;
        /**
         * @format int32
         * @default 30
         */
        limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabAdminRuntimePageModel, any>({
        path: `/api/admin/teamlab/runtimes`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeList
     * @request GET:/api/admin/teamlab/runtimes
     */
    useTeamLabAdminRuntimeList: (
      query?: {
        /** @format guid */
        topologyId?: string | null;
        after?: string | null;
        /**
         * @format int32
         * @default 30
         */
        limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabAdminRuntimePageModel, any>(
        doFetch ? [`/api/admin/teamlab/runtimes`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeList
     * @request GET:/api/admin/teamlab/runtimes
     */
    mutateTeamLabAdminRuntimeList: (
      query?: {
        /** @format guid */
        topologyId?: string | null;
        after?: string | null;
        /**
         * @format int32
         * @default 30
         */
        limit?: number;
      },
      data?:
        | TeamLabAdminRuntimePageModel
        | Promise<TeamLabAdminRuntimePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabAdminRuntimePageModel>(
        [`/api/admin/teamlab/runtimes`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeListAccessGrants
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/access-grants
     */
    teamLabAdminRuntimeListAccessGrants: (
      runtimeId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabAccessGrantModel[], any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/access-grants`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeListAccessGrants
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/access-grants
     */
    useTeamLabAdminRuntimeListAccessGrants: (
      runtimeId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabAccessGrantModel[], any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/access-grants`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeListAccessGrants
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/access-grants
     */
    mutateTeamLabAdminRuntimeListAccessGrants: (
      runtimeId: string,
      data?: TeamLabAccessGrantModel[] | Promise<TeamLabAccessGrantModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabAccessGrantModel[]>(
        `/api/admin/teamlab/runtimes/${runtimeId}/access-grants`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeListCaptures
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/captures
     */
    teamLabAdminRuntimeListCaptures: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabCapturePageModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/captures`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeListCaptures
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/captures
     */
    useTeamLabAdminRuntimeListCaptures: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabCapturePageModel, any>(
        doFetch
          ? [`/api/admin/teamlab/runtimes/${runtimeId}/captures`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeListCaptures
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/captures
     */
    mutateTeamLabAdminRuntimeListCaptures: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
      },
      data?: TeamLabCapturePageModel | Promise<TeamLabCapturePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabCapturePageModel>(
        [`/api/admin/teamlab/runtimes/${runtimeId}/captures`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeLogs
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/logs
     */
    teamLabAdminRuntimeLogs: (
      runtimeId: string,
      query?: {
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        Level?: string | null;
        /** @format guid */
        CorrelationId?: string | null;
        Logger?: string | null;
        EventCode?: string | null;
        Keyword?: string | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<LogMessagePageModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/logs`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeLogs
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/logs
     */
    useTeamLabAdminRuntimeLogs: (
      runtimeId: string,
      query?: {
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        Level?: string | null;
        /** @format guid */
        CorrelationId?: string | null;
        Logger?: string | null;
        EventCode?: string | null;
        Keyword?: string | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<LogMessagePageModel, any>(
        doFetch
          ? [`/api/admin/teamlab/runtimes/${runtimeId}/logs`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeLogs
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/logs
     */
    mutateTeamLabAdminRuntimeLogs: (
      runtimeId: string,
      query?: {
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        Level?: string | null;
        /** @format guid */
        CorrelationId?: string | null;
        Logger?: string | null;
        EventCode?: string | null;
        Keyword?: string | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      data?: LogMessagePageModel | Promise<LogMessagePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<LogMessagePageModel>(
        [`/api/admin/teamlab/runtimes/${runtimeId}/logs`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimePath
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}
     */
    teamLabAdminRuntimePath: (
      runtimeId: string,
      pathId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabTrafficPathModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/traffic/paths/${pathId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimePath
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}
     */
    useTeamLabAdminRuntimePath: (
      runtimeId: string,
      pathId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabTrafficPathModel, any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/traffic/paths/${pathId}`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimePath
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}
     */
    mutateTeamLabAdminRuntimePath: (
      runtimeId: string,
      pathId: string,
      data?: TeamLabTrafficPathModel | Promise<TeamLabTrafficPathModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabTrafficPathModel>(
        `/api/admin/teamlab/runtimes/${runtimeId}/traffic/paths/${pathId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimePaths
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/traffic/paths
     */
    teamLabAdminRuntimePaths: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @default 100
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        confidence?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabTrafficPathPageModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/traffic/paths`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimePaths
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/traffic/paths
     */
    useTeamLabAdminRuntimePaths: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @default 100
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        confidence?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabTrafficPathPageModel, any>(
        doFetch
          ? [`/api/admin/teamlab/runtimes/${runtimeId}/traffic/paths`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimePaths
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/traffic/paths
     */
    mutateTeamLabAdminRuntimePaths: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @default 100
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        confidence?: string | null;
      },
      data?: TeamLabTrafficPathPageModel | Promise<TeamLabTrafficPathPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabTrafficPathPageModel>(
        [`/api/admin/teamlab/runtimes/${runtimeId}/traffic/paths`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimePause
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/pause
     */
    teamLabAdminRuntimePause: (runtimeId: string, params: RequestParams = {}) =>
      this.request<TeamLabRuntimeProjectionModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/pause`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeRecoverLinkPolicy
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/link-policies/{policyId}/recover
     */
    teamLabAdminRuntimeRecoverLinkPolicy: (
      runtimeId: string,
      policyId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabLinkPolicyModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/link-policies/${policyId}/recover`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeReset
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/reset
     */
    teamLabAdminRuntimeReset: (
      runtimeId: string,
      data: ResetTeamLabRuntimeModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRuntimeProjectionModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/reset`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeResume
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/resume
     */
    teamLabAdminRuntimeResume: (
      runtimeId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRuntimeProjectionModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/resume`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeRevokeAccessGrant
     * @request DELETE:/api/admin/teamlab/runtimes/{runtimeId}/access-grants/{grantId}
     */
    teamLabAdminRuntimeRevokeAccessGrant: (
      runtimeId: string,
      grantId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/access-grants/${grantId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeSearch
     * @request GET:/api/admin/teamlab/runtimes/search
     */
    teamLabAdminRuntimeSearch: (
      query?: {
        Search?: string | null;
        Status?: TeamLabRuntimeStatus | null;
        Node?: string | null;
        /** @format int32 */
        Generation?: number | null;
        /** @format guid */
        ReleaseId?: string | null;
        /** @format guid */
        CreatedById?: string | null;
        ErrorsOnly?: boolean;
        After?: string | null;
        /** @format int32 */
        Limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRuntimeSearchPage, any>({
        path: `/api/admin/teamlab/runtimes/search`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeSearch
     * @request GET:/api/admin/teamlab/runtimes/search
     */
    useTeamLabAdminRuntimeSearch: (
      query?: {
        Search?: string | null;
        Status?: TeamLabRuntimeStatus | null;
        Node?: string | null;
        /** @format int32 */
        Generation?: number | null;
        /** @format guid */
        ReleaseId?: string | null;
        /** @format guid */
        CreatedById?: string | null;
        ErrorsOnly?: boolean;
        After?: string | null;
        /** @format int32 */
        Limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRuntimeSearchPage, any>(
        doFetch ? [`/api/admin/teamlab/runtimes/search`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeSearch
     * @request GET:/api/admin/teamlab/runtimes/search
     */
    mutateTeamLabAdminRuntimeSearch: (
      query?: {
        Search?: string | null;
        Status?: TeamLabRuntimeStatus | null;
        Node?: string | null;
        /** @format int32 */
        Generation?: number | null;
        /** @format guid */
        ReleaseId?: string | null;
        /** @format guid */
        CreatedById?: string | null;
        ErrorsOnly?: boolean;
        After?: string | null;
        /** @format int32 */
        Limit?: number;
      },
      data?: TeamLabRuntimeSearchPage | Promise<TeamLabRuntimeSearchPage>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRuntimeSearchPage>(
        [`/api/admin/teamlab/runtimes/search`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeStartCapture
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/captures
     */
    teamLabAdminRuntimeStartCapture: (
      runtimeId: string,
      data: CreateTeamLabCaptureModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabCaptureModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/captures`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeStopCapture
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/captures/{captureId}/stop
     */
    teamLabAdminRuntimeStopCapture: (
      runtimeId: string,
      captureId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabCaptureModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/captures/${captureId}/stop`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeTasks
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/tasks
     */
    teamLabAdminRuntimeTasks: (
      runtimeId: string,
      query?: {
        /** @format int32 */
        generation?: number | null;
        after?: string | null;
        /**
         * @format int32
         * @default 20
         */
        limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRuntimeTaskPageModel, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/tasks`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeTasks
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/tasks
     */
    useTeamLabAdminRuntimeTasks: (
      runtimeId: string,
      query?: {
        /** @format int32 */
        generation?: number | null;
        after?: string | null;
        /**
         * @format int32
         * @default 20
         */
        limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRuntimeTaskPageModel, any>(
        doFetch
          ? [`/api/admin/teamlab/runtimes/${runtimeId}/tasks`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeTasks
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/tasks
     */
    mutateTeamLabAdminRuntimeTasks: (
      runtimeId: string,
      query?: {
        /** @format int32 */
        generation?: number | null;
        after?: string | null;
        /**
         * @format int32
         * @default 20
         */
        limit?: number;
      },
      data?: TeamLabRuntimeTaskPageModel | Promise<TeamLabRuntimeTaskPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRuntimeTaskPageModel>(
        [`/api/admin/teamlab/runtimes/${runtimeId}/tasks`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeVmDiagnostics
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/vm-diagnostics
     */
    teamLabAdminRuntimeVmDiagnostics: (
      runtimeId: string,
      assetId: number,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabVmDiagnostics, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/vm-diagnostics`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeVmDiagnostics
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/vm-diagnostics
     */
    useTeamLabAdminRuntimeVmDiagnostics: (
      runtimeId: string,
      assetId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabVmDiagnostics, any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/vm-diagnostics`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminRuntime
     * @name TeamLabAdminRuntimeVmDiagnostics
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/vm-diagnostics
     */
    mutateTeamLabAdminRuntimeVmDiagnostics: (
      runtimeId: string,
      assetId: number,
      data?: TeamLabVmDiagnostics | Promise<TeamLabVmDiagnostics>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabVmDiagnostics>(
        `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/vm-diagnostics`,
        data,
        options,
      ),
  };
  teamLabAdminScopes = {
    /**
     * No description
     *
     * @tags TeamLabAdminScopes
     * @name TeamLabAdminScopesArchive
     * @request POST:/api/admin/teamlab/scopes/{scopeId}/archive
     */
    teamLabAdminScopesArchive: (scopeId: string, params: RequestParams = {}) =>
      this.request<void, any>({
        path: `/api/admin/teamlab/scopes/${scopeId}/archive`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminScopes
     * @name TeamLabAdminScopesList
     * @request GET:/api/admin/teamlab/scopes
     */
    teamLabAdminScopesList: (params: RequestParams = {}) =>
      this.request<TeamLabControlScopeModel[], any>({
        path: `/api/admin/teamlab/scopes`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminScopes
     * @name TeamLabAdminScopesList
     * @request GET:/api/admin/teamlab/scopes
     */
    useTeamLabAdminScopesList: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabControlScopeModel[], any>(
        doFetch ? `/api/admin/teamlab/scopes` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminScopes
     * @name TeamLabAdminScopesList
     * @request GET:/api/admin/teamlab/scopes
     */
    mutateTeamLabAdminScopesList: (
      data?: TeamLabControlScopeModel[] | Promise<TeamLabControlScopeModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabControlScopeModel[]>(
        `/api/admin/teamlab/scopes`,
        data,
        options,
      ),
  };
  teamLabAdminTopology = {
    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyCapabilities
     * @request GET:/api/admin/teamlab/capabilities
     */
    teamLabAdminTopologyCapabilities: (params: RequestParams = {}) =>
      this.request<TeamLabCapabilitiesModel, any>({
        path: `/api/admin/teamlab/capabilities`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyCapabilities
     * @request GET:/api/admin/teamlab/capabilities
     */
    useTeamLabAdminTopologyCapabilities: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabCapabilitiesModel, any>(
        doFetch ? `/api/admin/teamlab/capabilities` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyCapabilities
     * @request GET:/api/admin/teamlab/capabilities
     */
    mutateTeamLabAdminTopologyCapabilities: (
      data?: TeamLabCapabilitiesModel | Promise<TeamLabCapabilitiesModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabCapabilitiesModel>(
        `/api/admin/teamlab/capabilities`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyCreate
     * @request POST:/api/admin/teamlab/topologies
     */
    teamLabAdminTopologyCreate: (
      data: CreateTeamLabTopologyModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabTopologyDetailModel, any>({
        path: `/api/admin/teamlab/topologies`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyDelete
     * @request DELETE:/api/admin/teamlab/topologies/{topologyId}
     */
    teamLabAdminTopologyDelete: (
      topologyId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/topologies/${topologyId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyGet
     * @request GET:/api/admin/teamlab/topologies/{topologyId}
     */
    teamLabAdminTopologyGet: (topologyId: string, params: RequestParams = {}) =>
      this.request<TeamLabTopologyDetailModel, any>({
        path: `/api/admin/teamlab/topologies/${topologyId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyGet
     * @request GET:/api/admin/teamlab/topologies/{topologyId}
     */
    useTeamLabAdminTopologyGet: (
      topologyId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabTopologyDetailModel, any>(
        doFetch ? `/api/admin/teamlab/topologies/${topologyId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyGet
     * @request GET:/api/admin/teamlab/topologies/{topologyId}
     */
    mutateTeamLabAdminTopologyGet: (
      topologyId: string,
      data?: TeamLabTopologyDetailModel | Promise<TeamLabTopologyDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabTopologyDetailModel>(
        `/api/admin/teamlab/topologies/${topologyId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyList
     * @request GET:/api/admin/teamlab/topologies
     */
    teamLabAdminTopologyList: (
      query?: {
        search?: string | null;
        owner?: string | null;
        /** @format guid */
        ownerId?: string | null;
        status?: string | null;
        after?: string | null;
        /**
         * @format int32
         * @default 30
         */
        limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabAdminScenePageModel, any>({
        path: `/api/admin/teamlab/topologies`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyList
     * @request GET:/api/admin/teamlab/topologies
     */
    useTeamLabAdminTopologyList: (
      query?: {
        search?: string | null;
        owner?: string | null;
        /** @format guid */
        ownerId?: string | null;
        status?: string | null;
        after?: string | null;
        /**
         * @format int32
         * @default 30
         */
        limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabAdminScenePageModel, any>(
        doFetch ? [`/api/admin/teamlab/topologies`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyList
     * @request GET:/api/admin/teamlab/topologies
     */
    mutateTeamLabAdminTopologyList: (
      query?: {
        search?: string | null;
        owner?: string | null;
        /** @format guid */
        ownerId?: string | null;
        status?: string | null;
        after?: string | null;
        /**
         * @format int32
         * @default 30
         */
        limit?: number;
      },
      data?: TeamLabAdminScenePageModel | Promise<TeamLabAdminScenePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabAdminScenePageModel>(
        [`/api/admin/teamlab/topologies`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyPlan
     * @request POST:/api/admin/teamlab/topologies/{topologyId}/releases/{releaseId}/plan
     */
    teamLabAdminTopologyPlan: (
      topologyId: string,
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabPlanModel, any>({
        path: `/api/admin/teamlab/topologies/${topologyId}/releases/${releaseId}/plan`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyPrepareImages
     * @request POST:/api/admin/teamlab/topologies/{topologyId}/releases/{releaseId}/images/prepare
     */
    teamLabAdminTopologyPrepareImages: (
      topologyId: string,
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabAdminReleaseReadinessModel, any>({
        path: `/api/admin/teamlab/topologies/${topologyId}/releases/${releaseId}/images/prepare`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyPublish
     * @request POST:/api/admin/teamlab/topologies/{topologyId}/releases
     */
    teamLabAdminTopologyPublish: (
      topologyId: string,
      data: PublishTeamLabTopologyModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabReleaseModel, any>({
        path: `/api/admin/teamlab/topologies/${topologyId}/releases`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyReadiness
     * @request GET:/api/admin/teamlab/topologies/{topologyId}/releases/{releaseId}/readiness
     */
    teamLabAdminTopologyReadiness: (
      topologyId: string,
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabAdminReleaseReadinessModel, any>({
        path: `/api/admin/teamlab/topologies/${topologyId}/releases/${releaseId}/readiness`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyReadiness
     * @request GET:/api/admin/teamlab/topologies/{topologyId}/releases/{releaseId}/readiness
     */
    useTeamLabAdminTopologyReadiness: (
      topologyId: string,
      releaseId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabAdminReleaseReadinessModel, any>(
        doFetch
          ? `/api/admin/teamlab/topologies/${topologyId}/releases/${releaseId}/readiness`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyReadiness
     * @request GET:/api/admin/teamlab/topologies/{topologyId}/releases/{releaseId}/readiness
     */
    mutateTeamLabAdminTopologyReadiness: (
      topologyId: string,
      releaseId: string,
      data?:
        | TeamLabAdminReleaseReadinessModel
        | Promise<TeamLabAdminReleaseReadinessModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabAdminReleaseReadinessModel>(
        `/api/admin/teamlab/topologies/${topologyId}/releases/${releaseId}/readiness`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyReleases
     * @request GET:/api/admin/teamlab/topologies/{topologyId}/releases
     */
    teamLabAdminTopologyReleases: (
      topologyId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabReleaseModel[], any>({
        path: `/api/admin/teamlab/topologies/${topologyId}/releases`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyReleases
     * @request GET:/api/admin/teamlab/topologies/{topologyId}/releases
     */
    useTeamLabAdminTopologyReleases: (
      topologyId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabReleaseModel[], any>(
        doFetch ? `/api/admin/teamlab/topologies/${topologyId}/releases` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyReleases
     * @request GET:/api/admin/teamlab/topologies/{topologyId}/releases
     */
    mutateTeamLabAdminTopologyReleases: (
      topologyId: string,
      data?: TeamLabReleaseModel[] | Promise<TeamLabReleaseModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabReleaseModel[]>(
        `/api/admin/teamlab/topologies/${topologyId}/releases`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyUpdate
     * @request PUT:/api/admin/teamlab/topologies/{topologyId}
     */
    teamLabAdminTopologyUpdate: (
      topologyId: string,
      data: UpdateTeamLabTopologyModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabTopologyDetailModel, any>({
        path: `/api/admin/teamlab/topologies/${topologyId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAdminTopology
     * @name TeamLabAdminTopologyValidate
     * @request POST:/api/admin/teamlab/topologies/{topologyId}/validate
     */
    teamLabAdminTopologyValidate: (
      topologyId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabValidationResultModel, any>({
        path: `/api/admin/teamlab/topologies/${topologyId}/validate`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
  teamLabAssetControl = {
    /**
     * No description
     *
     * @tags TeamLabAssetControl
     * @name TeamLabAssetControlAvailability
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
     */
    teamLabAssetControlAvailability: (
      runtimeId: string,
      assetId: number,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabAssetControlAvailability, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/control`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAssetControl
     * @name TeamLabAssetControlAvailability
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
     */
    useTeamLabAssetControlAvailability: (
      runtimeId: string,
      assetId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabAssetControlAvailability, any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/control`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAssetControl
     * @name TeamLabAssetControlAvailability
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
     */
    mutateTeamLabAssetControlAvailability: (
      runtimeId: string,
      assetId: number,
      data?:
        | TeamLabAssetControlAvailability
        | Promise<TeamLabAssetControlAvailability>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabAssetControlAvailability>(
        `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/control`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAssetControl
     * @name TeamLabAssetControlControl
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control
     */
    teamLabAssetControlControl: (
      runtimeId: string,
      assetId: number,
      data: TeamLabAssetControlCommand,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabQueueTicketResult, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/control`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabAssetControl
     * @name TeamLabAssetControlGet
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control/{ticketId}
     */
    teamLabAssetControlGet: (
      runtimeId: string,
      assetId: number,
      ticketId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabAssetControlTask, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/control/${ticketId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAssetControl
     * @name TeamLabAssetControlGet
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control/{ticketId}
     */
    useTeamLabAssetControlGet: (
      runtimeId: string,
      assetId: number,
      ticketId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabAssetControlTask, any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/control/${ticketId}`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAssetControl
     * @name TeamLabAssetControlGet
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control/{ticketId}
     */
    mutateTeamLabAssetControlGet: (
      runtimeId: string,
      assetId: number,
      ticketId: string,
      data?: TeamLabAssetControlTask | Promise<TeamLabAssetControlTask>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabAssetControlTask>(
        `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/control/${ticketId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAssetControl
     * @name TeamLabAssetControlRetry
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/control/{ticketId}/retry
     */
    teamLabAssetControlRetry: (
      runtimeId: string,
      assetId: number,
      ticketId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabQueueTicketResult, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/control/${ticketId}/retry`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
  teamLabAssetFiles = {
    /**
     * No description
     *
     * @tags TeamLabAssetFiles
     * @name TeamLabAssetFilesDownload
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/files/download
     */
    teamLabAssetFilesDownload: (
      runtimeId: string,
      assetId: number,
      query?: {
        /** @format int32 */
        generation?: number;
        path?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/files/download`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabAssetFiles
     * @name TeamLabAssetFilesDownload
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/files/download
     */
    useTeamLabAssetFilesDownload: (
      runtimeId: string,
      assetId: number,
      query?: {
        /** @format int32 */
        generation?: number;
        path?: string;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? [
              `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/files/download`,
              query,
            ]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAssetFiles
     * @name TeamLabAssetFilesDownload
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/files/download
     */
    mutateTeamLabAssetFilesDownload: (
      runtimeId: string,
      assetId: number,
      query?: {
        /** @format int32 */
        generation?: number;
        path?: string;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        [
          `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/files/download`,
          query,
        ],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabAssetFiles
     * @name TeamLabAssetFilesExecute
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/files
     */
    teamLabAssetFilesExecute: (
      runtimeId: string,
      assetId: number,
      data: TeamLabAssetFileCommand,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabFileResult, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/assets/${assetId}/files`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  teamLabDeviceHealth = {
    /**
     * No description
     *
     * @tags TeamLabDeviceHealth
     * @name TeamLabDeviceHealthRead
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/device-health
     */
    teamLabDeviceHealthRead: (runtimeId: string, params: RequestParams = {}) =>
      this.request<TeamLabDeviceHealthModel[], any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/device-health`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabDeviceHealth
     * @name TeamLabDeviceHealthRead
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/device-health
     */
    useTeamLabDeviceHealthRead: (
      runtimeId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabDeviceHealthModel[], any>(
        doFetch
          ? `/api/admin/teamlab/runtimes/${runtimeId}/device-health`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabDeviceHealth
     * @name TeamLabDeviceHealthRead
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/device-health
     */
    mutateTeamLabDeviceHealthRead: (
      runtimeId: string,
      data?: TeamLabDeviceHealthModel[] | Promise<TeamLabDeviceHealthModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabDeviceHealthModel[]>(
        `/api/admin/teamlab/runtimes/${runtimeId}/device-health`,
        data,
        options,
      ),
  };
  teamLabRemoteAudit = {
    /**
     * No description
     *
     * @tags TeamLabRemoteAudit
     * @name TeamLabRemoteAuditDownload
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/audit-files/{fileId}/download
     */
    teamLabRemoteAuditDownload: (
      sessionId: string,
      fileId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/teamlab/remote-sessions/${sessionId}/audit-files/${fileId}/download`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabRemoteAudit
     * @name TeamLabRemoteAuditDownload
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/audit-files/{fileId}/download
     */
    useTeamLabRemoteAuditDownload: (
      sessionId: string,
      fileId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/admin/teamlab/remote-sessions/${sessionId}/audit-files/${fileId}/download`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabRemoteAudit
     * @name TeamLabRemoteAuditDownload
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/audit-files/{fileId}/download
     */
    mutateTeamLabRemoteAuditDownload: (
      sessionId: string,
      fileId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/admin/teamlab/remote-sessions/${sessionId}/audit-files/${fileId}/download`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabRemoteAudit
     * @name TeamLabRemoteAuditGenerate
     * @request POST:/api/admin/teamlab/remote-sessions/{sessionId}/audit-files
     */
    teamLabRemoteAuditGenerate: (
      sessionId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRemoteAuditPage, any>({
        path: `/api/admin/teamlab/remote-sessions/${sessionId}/audit-files`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TeamLabRemoteAudit
     * @name TeamLabRemoteAuditList
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/audit-files
     */
    teamLabRemoteAuditList: (sessionId: string, params: RequestParams = {}) =>
      this.request<TeamLabRemoteAuditPage, any>({
        path: `/api/admin/teamlab/remote-sessions/${sessionId}/audit-files`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabRemoteAudit
     * @name TeamLabRemoteAuditList
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/audit-files
     */
    useTeamLabRemoteAuditList: (
      sessionId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRemoteAuditPage, any>(
        doFetch
          ? `/api/admin/teamlab/remote-sessions/${sessionId}/audit-files`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabRemoteAudit
     * @name TeamLabRemoteAuditList
     * @request GET:/api/admin/teamlab/remote-sessions/{sessionId}/audit-files
     */
    mutateTeamLabRemoteAuditList: (
      sessionId: string,
      data?: TeamLabRemoteAuditPage | Promise<TeamLabRemoteAuditPage>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRemoteAuditPage>(
        `/api/admin/teamlab/remote-sessions/${sessionId}/audit-files`,
        data,
        options,
      ),
  };
  teamLabRuntimeDifferences = {
    /**
     * No description
     *
     * @tags TeamLabRuntimeDifferences
     * @name TeamLabRuntimeDifferencesPreview
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/differences
     */
    teamLabRuntimeDifferencesPreview: (
      runtimeId: string,
      params: RequestParams = {},
    ) =>
      this.request<RuntimeDifferencePreview, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/differences`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags TeamLabRuntimeDifferences
     * @name TeamLabRuntimeDifferencesPreview
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/differences
     */
    useTeamLabRuntimeDifferencesPreview: (
      runtimeId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<RuntimeDifferencePreview, any>(
        doFetch ? `/api/admin/teamlab/runtimes/${runtimeId}/differences` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabRuntimeDifferences
     * @name TeamLabRuntimeDifferencesPreview
     * @request GET:/api/admin/teamlab/runtimes/{runtimeId}/differences
     */
    mutateTeamLabRuntimeDifferencesPreview: (
      runtimeId: string,
      data?: RuntimeDifferencePreview | Promise<RuntimeDifferencePreview>,
      options?: MutatorOptions,
    ) =>
      mutate<RuntimeDifferencePreview>(
        `/api/admin/teamlab/runtimes/${runtimeId}/differences`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags TeamLabRuntimeDifferences
     * @name TeamLabRuntimeDifferencesRepair
     * @request POST:/api/admin/teamlab/runtimes/{runtimeId}/differences/assets/{assetId}/repair
     */
    teamLabRuntimeDifferencesRepair: (
      runtimeId: string,
      assetId: number,
      data: TeamLabAssetControlCommand,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabQueueTicketResult, any>({
        path: `/api/admin/teamlab/runtimes/${runtimeId}/differences/assets/${assetId}/repair`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
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
     * No description
     *
     * @tags Account
     * @name AccountCapabilities
     * @summary Get public account capabilities used by authentication pages.
     * @request GET:/api/account/capabilities
     */
    accountCapabilities: (params: RequestParams = {}) =>
      this.request<AccountCapabilitiesModel, any>({
        path: `/api/account/capabilities`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Account
     * @name AccountCapabilities
     * @summary Get public account capabilities used by authentication pages.
     * @request GET:/api/account/capabilities
     */
    useAccountCapabilities: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AccountCapabilitiesModel, any>(
        doFetch ? `/api/account/capabilities` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Account
     * @name AccountCapabilities
     * @summary Get public account capabilities used by authentication pages.
     * @request GET:/api/account/capabilities
     */
    mutateAccountCapabilities: (
      data?: AccountCapabilitiesModel | Promise<AccountCapabilitiesModel>,
      options?: MutatorOptions,
    ) =>
      mutate<AccountCapabilitiesModel>(
        `/api/account/capabilities`,
        data,
        options,
      ),

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
     * No description
     *
     * @tags Account
     * @name AccountSummary
     * @summary Get the lightweight identity and activity summary used by the account drawer.
     * @request GET:/api/account/summary
     */
    accountSummary: (params: RequestParams = {}) =>
      this.request<AccountSummaryModel, RequestResponse>({
        path: `/api/account/summary`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Account
     * @name AccountSummary
     * @summary Get the lightweight identity and activity summary used by the account drawer.
     * @request GET:/api/account/summary
     */
    useAccountSummary: (options?: SWRConfiguration, doFetch: boolean = true) =>
      useSWR<AccountSummaryModel, RequestResponse>(
        doFetch ? `/api/account/summary` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Account
     * @name AccountSummary
     * @summary Get the lightweight identity and activity summary used by the account drawer.
     * @request GET:/api/account/summary
     */
    mutateAccountSummary: (
      data?: AccountSummaryModel | Promise<AccountSummaryModel>,
      options?: MutatorOptions,
    ) => mutate<AccountSummaryModel>(`/api/account/summary`, data, options),

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
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        Level?: string | null;
        /** @format guid */
        CorrelationId?: string | null;
        Logger?: string | null;
        EventCode?: string | null;
        Keyword?: string | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<LogMessagePageModel, RequestResponse>({
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
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        Level?: string | null;
        /** @format guid */
        CorrelationId?: string | null;
        Logger?: string | null;
        EventCode?: string | null;
        Keyword?: string | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<LogMessagePageModel, RequestResponse>(
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
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        Level?: string | null;
        /** @format guid */
        CorrelationId?: string | null;
        Logger?: string | null;
        EventCode?: string | null;
        Keyword?: string | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      data?: LogMessagePageModel | Promise<LogMessagePageModel>,
      options?: MutatorOptions,
    ) => mutate<LogMessagePageModel>([`/api/admin/logs`, query], data, options),

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
  deploymentQueue = {
    /**
     * No description
     *
     * @tags DeploymentQueue
     * @name DeploymentQueueCancel
     * @request DELETE:/api/v1/deployment-queue/{id}
     */
    deploymentQueueCancel: (id: string, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/deployment-queue/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags DeploymentQueue
     * @name DeploymentQueueGetById
     * @request GET:/api/v1/deployment-queue/{id}
     */
    deploymentQueueGetById: (id: string, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/deployment-queue/${id}`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags DeploymentQueue
     * @name DeploymentQueueGetById
     * @request GET:/api/v1/deployment-queue/{id}
     */
    useDeploymentQueueGetById: (
      id: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/v1/deployment-queue/${id}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags DeploymentQueue
     * @name DeploymentQueueGetById
     * @request GET:/api/v1/deployment-queue/{id}
     */
    mutateDeploymentQueueGetById: (
      id: string,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/v1/deployment-queue/${id}`, data, options),

    /**
     * No description
     *
     * @tags DeploymentQueue
     * @name DeploymentQueueList
     * @request GET:/api/v1/deployment-queue
     */
    deploymentQueueList: (
      query?: {
        status?: string | null;
        cursor?: string | null;
        /**
         * @format int32
         * @default 20
         */
        pageSize?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/deployment-queue`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags DeploymentQueue
     * @name DeploymentQueueList
     * @request GET:/api/v1/deployment-queue
     */
    useDeploymentQueueList: (
      query?: {
        status?: string | null;
        cursor?: string | null;
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
        doFetch ? [`/api/v1/deployment-queue`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags DeploymentQueue
     * @name DeploymentQueueList
     * @request GET:/api/v1/deployment-queue
     */
    mutateDeploymentQueueList: (
      query?: {
        status?: string | null;
        cursor?: string | null;
        /**
         * @format int32
         * @default 20
         */
        pageSize?: number;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>([`/api/v1/deployment-queue`, query], data, options),
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
      this.request<DeploymentQueueStatusModel, RequestResponse>({
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
  exercise = {
    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseBackfillPool
     * @request POST:/api/exercise/pool/backfill
     */
    exerciseBackfillPool: (params: RequestParams = {}) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/pool/backfill`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseCreateContainer
     * @request POST:/api/exercise/{id}/container
     */
    exerciseCreateContainer: (id: number, params: RequestParams = {}) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/${id}/container`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseCreateExercise
     * @request POST:/api/exercise
     */
    exerciseCreateExercise: (
      data: ExerciseCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseDeleteExercise
     * @request DELETE:/api/exercise/{id}
     */
    exerciseDeleteExercise: (id: number, params: RequestParams = {}) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/${id}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseDestroyContainer
     * @request DELETE:/api/exercise/{id}/container
     */
    exerciseDestroyContainer: (id: number, params: RequestParams = {}) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/${id}/container`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseExtendContainer
     * @request POST:/api/exercise/{id}/container/extend
     */
    exerciseExtendContainer: (id: number, params: RequestParams = {}) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/${id}/container/extend`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExercise
     * @request GET:/api/exercise/{id}
     */
    exerciseGetExercise: (id: number, params: RequestParams = {}) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/${id}`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExercise
     * @request GET:/api/exercise/{id}
     */
    useExerciseGetExercise: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<any, RequestResponse>(
        doFetch ? `/api/exercise/${id}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExercise
     * @request GET:/api/exercise/{id}
     */
    mutateExerciseGetExercise: (
      id: number,
      data?: any | Promise<any>,
      options?: MutatorOptions,
    ) => mutate<any>(`/api/exercise/${id}`, data, options),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExerciseForManagement
     * @request GET:/api/exercise/{id}/manage
     */
    exerciseGetExerciseForManagement: (
      id: number,
      params: RequestParams = {},
    ) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/${id}/manage`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExerciseForManagement
     * @request GET:/api/exercise/{id}/manage
     */
    useExerciseGetExerciseForManagement: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<any, RequestResponse>(
        doFetch ? `/api/exercise/${id}/manage` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExerciseForManagement
     * @request GET:/api/exercise/{id}/manage
     */
    mutateExerciseGetExerciseForManagement: (
      id: number,
      data?: any | Promise<any>,
      options?: MutatorOptions,
    ) => mutate<any>(`/api/exercise/${id}/manage`, data, options),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExercises
     * @request GET:/api/exercise
     */
    exerciseGetExercises: (
      query?: {
        Search?: string | null;
        Categories?: ChallengeCategory[] | null;
        Difficulties?: Difficulty[] | null;
        Tags?: string[] | null;
        Credit?: boolean | null;
        Sources?: ExercisePoolSource[] | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExercises
     * @request GET:/api/exercise
     */
    useExerciseGetExercises: (
      query?: {
        Search?: string | null;
        Categories?: ChallengeCategory[] | null;
        Difficulties?: Difficulty[] | null;
        Tags?: string[] | null;
        Credit?: boolean | null;
        Sources?: ExercisePoolSource[] | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<any, RequestResponse>(
        doFetch ? [`/api/exercise`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExercises
     * @request GET:/api/exercise
     */
    mutateExerciseGetExercises: (
      query?: {
        Search?: string | null;
        Categories?: ChallengeCategory[] | null;
        Difficulties?: Difficulty[] | null;
        Tags?: string[] | null;
        Credit?: boolean | null;
        Sources?: ExercisePoolSource[] | null;
      },
      data?: any | Promise<any>,
      options?: MutatorOptions,
    ) => mutate<any>([`/api/exercise`, query], data, options),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExercisesForManagement
     * @request GET:/api/exercise/manage
     */
    exerciseGetExercisesForManagement: (params: RequestParams = {}) =>
      this.request<ExerciseInfoModel[], RequestResponse>({
        path: `/api/exercise/manage`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExercisesForManagement
     * @request GET:/api/exercise/manage
     */
    useExerciseGetExercisesForManagement: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ExerciseInfoModel[], RequestResponse>(
        doFetch ? `/api/exercise/manage` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseGetExercisesForManagement
     * @request GET:/api/exercise/manage
     */
    mutateExerciseGetExercisesForManagement: (
      data?: ExerciseInfoModel[] | Promise<ExerciseInfoModel[]>,
      options?: MutatorOptions,
    ) => mutate<ExerciseInfoModel[]>(`/api/exercise/manage`, data, options),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseImportFromGame
     * @request POST:/api/exercise/import
     */
    exerciseImportFromGame: (
      data: ExerciseImportFromGameModel,
      params: RequestParams = {},
    ) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/import`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseImportFromTraining
     * @request POST:/api/exercise/import/training
     */
    exerciseImportFromTraining: (
      data: ExerciseImportFromTrainingModel,
      params: RequestParams = {},
    ) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/import/training`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseSubmitFlag
     * @request POST:/api/exercise/{id}/flag
     */
    exerciseSubmitFlag: (
      id: number,
      data: FlagSubmitModel,
      params: RequestParams = {},
    ) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/${id}/flag`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags Exercise
     * @name ExerciseUpdateExercise
     * @request PUT:/api/exercise/{id}
     */
    exerciseUpdateExercise: (
      id: number,
      data: ExerciseCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<any, RequestResponse>({
        path: `/api/exercise/${id}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
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
         * @min 1
         * @max 100
         * @default 100
         */
        count?: number;
        cursor?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<SubmissionPageModel, RequestResponse>({
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
         * @min 1
         * @max 100
         * @default 100
         */
        count?: number;
        cursor?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<SubmissionPageModel, RequestResponse>(
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
         * @min 1
         * @max 100
         * @default 100
         */
        count?: number;
        cursor?: string | null;
      },
      data?: SubmissionPageModel | Promise<SubmissionPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<SubmissionPageModel>(
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
     * @name ImageTemplateGetRemoteAccess
     * @request GET:/api/v1/image-templates/{id}/remote-access
     */
    imageTemplateGetRemoteAccess: (id: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/${id}/remote-access`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateGetRemoteAccess
     * @request GET:/api/v1/image-templates/{id}/remote-access
     */
    useImageTemplateGetRemoteAccess: (
      id: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/v1/image-templates/${id}/remote-access` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags ImageTemplate
     * @name ImageTemplateGetRemoteAccess
     * @request GET:/api/v1/image-templates/{id}/remote-access
     */
    mutateImageTemplateGetRemoteAccess: (
      id: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/v1/image-templates/${id}/remote-access`,
        data,
        options,
      ),

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
     * @name ImageTemplateUpdateRemoteAccess
     * @request PATCH:/api/v1/image-templates/{id}/remote-access
     */
    imageTemplateUpdateRemoteAccess: (
      id: number,
      data: UpdateImageRemoteAccessModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/v1/image-templates/${id}/remote-access`,
        method: "PATCH",
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
     * @name InternalAcknowledgePortMap
     * @summary Acknowledge that the public gateway applied the current TCP port map.
     * @request POST:/api/internal/port-map/ack
     */
    internalAcknowledgePortMap: (
      data: PortMapAckRequest,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/internal/port-map/ack`,
        method: "POST",
        body: data,
        type: ContentType.Json,
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
     * @name NodesDownloadLinuxEndpointSensor
     * @request GET:/api/agent/endpoint-sensor/linux-x64/download
     */
    nodesDownloadLinuxEndpointSensor: (params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/agent/endpoint-sensor/linux-x64/download`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadLinuxEndpointSensor
     * @request GET:/api/agent/endpoint-sensor/linux-x64/download
     */
    useNodesDownloadLinuxEndpointSensor: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/agent/endpoint-sensor/linux-x64/download` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadLinuxEndpointSensor
     * @request GET:/api/agent/endpoint-sensor/linux-x64/download
     */
    mutateNodesDownloadLinuxEndpointSensor: (
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/agent/endpoint-sensor/linux-x64/download`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadLinuxGuestSupervisor
     * @request GET:/api/agent/guest-supervisor/linux-x64/download
     */
    nodesDownloadLinuxGuestSupervisor: (params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/agent/guest-supervisor/linux-x64/download`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadLinuxGuestSupervisor
     * @request GET:/api/agent/guest-supervisor/linux-x64/download
     */
    useNodesDownloadLinuxGuestSupervisor: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/agent/guest-supervisor/linux-x64/download` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadLinuxGuestSupervisor
     * @request GET:/api/agent/guest-supervisor/linux-x64/download
     */
    mutateNodesDownloadLinuxGuestSupervisor: (
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/agent/guest-supervisor/linux-x64/download`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadWindowsEndpointSensor
     * @request GET:/api/agent/endpoint-sensor/win-x64/download
     */
    nodesDownloadWindowsEndpointSensor: (params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/agent/endpoint-sensor/win-x64/download`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadWindowsEndpointSensor
     * @request GET:/api/agent/endpoint-sensor/win-x64/download
     */
    useNodesDownloadWindowsEndpointSensor: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/agent/endpoint-sensor/win-x64/download` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadWindowsEndpointSensor
     * @request GET:/api/agent/endpoint-sensor/win-x64/download
     */
    mutateNodesDownloadWindowsEndpointSensor: (
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/agent/endpoint-sensor/win-x64/download`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadWindowsGuestSupervisor
     * @request GET:/api/agent/guest-supervisor/win-x64/download
     */
    nodesDownloadWindowsGuestSupervisor: (params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/agent/guest-supervisor/win-x64/download`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadWindowsGuestSupervisor
     * @request GET:/api/agent/guest-supervisor/win-x64/download
     */
    useNodesDownloadWindowsGuestSupervisor: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/agent/guest-supervisor/win-x64/download` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Nodes
     * @name NodesDownloadWindowsGuestSupervisor
     * @request GET:/api/agent/guest-supervisor/win-x64/download
     */
    mutateNodesDownloadWindowsGuestSupervisor: (
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/agent/guest-supervisor/win-x64/download`,
        data,
        options,
      ),

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
  operations = {
    /**
     * No description
     *
     * @tags Operations
     * @name OperationsCorrelation
     * @request GET:/api/admin/operations/correlations/{correlationId}
     */
    operationsCorrelation: (
      correlationId: string,
      params: RequestParams = {},
    ) =>
      this.request<OperationalCorrelationSummaryModel, ProblemDetails>({
        path: `/api/admin/operations/correlations/${correlationId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Operations
     * @name OperationsCorrelation
     * @request GET:/api/admin/operations/correlations/{correlationId}
     */
    useOperationsCorrelation: (
      correlationId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OperationalCorrelationSummaryModel, ProblemDetails>(
        doFetch ? `/api/admin/operations/correlations/${correlationId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Operations
     * @name OperationsCorrelation
     * @request GET:/api/admin/operations/correlations/{correlationId}
     */
    mutateOperationsCorrelation: (
      correlationId: string,
      data?:
        | OperationalCorrelationSummaryModel
        | Promise<OperationalCorrelationSummaryModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OperationalCorrelationSummaryModel>(
        `/api/admin/operations/correlations/${correlationId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Operations
     * @name OperationsEvents
     * @request GET:/api/admin/operations/events
     */
    operationsEvents: (
      query?: {
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        /** @format guid */
        CorrelationId?: string | null;
        /** @format uint64 */
        From?: number | null;
        /** @format uint64 */
        To?: number | null;
        Domain?: string | null;
        EventCode?: string | null;
        Outcome?: OperationalEventOutcome | null;
        ErrorCategory?: OperationalErrorCategory | null;
        /** @format guid */
        ActorUserId?: string | null;
        /** @format guid */
        OwnerUserId?: string | null;
        /** @format int32 */
        OwnerTeamId?: number | null;
        /** @format int32 */
        GameId?: number | null;
        /** @format int32 */
        CourseId?: number | null;
        /** @format int32 */
        ChallengeId?: number | null;
        /** @format int32 */
        ImageTemplateId?: number | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        /** @format int32 */
        TeamLabRuntimeId?: number | null;
        /** @format guid */
        VmInstanceId?: string | null;
        SubjectType?: string | null;
        SubjectId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<OperationalEventViewPageModel, any>({
        path: `/api/admin/operations/events`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Operations
     * @name OperationsEvents
     * @request GET:/api/admin/operations/events
     */
    useOperationsEvents: (
      query?: {
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        /** @format guid */
        CorrelationId?: string | null;
        /** @format uint64 */
        From?: number | null;
        /** @format uint64 */
        To?: number | null;
        Domain?: string | null;
        EventCode?: string | null;
        Outcome?: OperationalEventOutcome | null;
        ErrorCategory?: OperationalErrorCategory | null;
        /** @format guid */
        ActorUserId?: string | null;
        /** @format guid */
        OwnerUserId?: string | null;
        /** @format int32 */
        OwnerTeamId?: number | null;
        /** @format int32 */
        GameId?: number | null;
        /** @format int32 */
        CourseId?: number | null;
        /** @format int32 */
        ChallengeId?: number | null;
        /** @format int32 */
        ImageTemplateId?: number | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        /** @format int32 */
        TeamLabRuntimeId?: number | null;
        /** @format guid */
        VmInstanceId?: string | null;
        SubjectType?: string | null;
        SubjectId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OperationalEventViewPageModel, any>(
        doFetch ? [`/api/admin/operations/events`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Operations
     * @name OperationsEvents
     * @request GET:/api/admin/operations/events
     */
    mutateOperationsEvents: (
      query?: {
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        /** @format guid */
        CorrelationId?: string | null;
        /** @format uint64 */
        From?: number | null;
        /** @format uint64 */
        To?: number | null;
        Domain?: string | null;
        EventCode?: string | null;
        Outcome?: OperationalEventOutcome | null;
        ErrorCategory?: OperationalErrorCategory | null;
        /** @format guid */
        ActorUserId?: string | null;
        /** @format guid */
        OwnerUserId?: string | null;
        /** @format int32 */
        OwnerTeamId?: number | null;
        /** @format int32 */
        GameId?: number | null;
        /** @format int32 */
        CourseId?: number | null;
        /** @format int32 */
        ChallengeId?: number | null;
        /** @format int32 */
        ImageTemplateId?: number | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        /** @format int32 */
        TeamLabRuntimeId?: number | null;
        /** @format guid */
        VmInstanceId?: string | null;
        SubjectType?: string | null;
        SubjectId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      data?:
        | OperationalEventViewPageModel
        | Promise<OperationalEventViewPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OperationalEventViewPageModel>(
        [`/api/admin/operations/events`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Operations
     * @name OperationsGet
     * @request GET:/api/open/v1/operations/{id}
     */
    operationsGet: (id: string, params: RequestParams = {}) =>
      this.request<ApiOperationModel, ProblemDetails>({
        path: `/api/open/v1/operations/${id}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Operations
     * @name OperationsGet
     * @request GET:/api/open/v1/operations/{id}
     */
    useOperationsGet: (
      id: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ApiOperationModel, ProblemDetails>(
        doFetch ? `/api/open/v1/operations/${id}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Operations
     * @name OperationsGet
     * @request GET:/api/open/v1/operations/{id}
     */
    mutateOperationsGet: (
      id: string,
      data?: ApiOperationModel | Promise<ApiOperationModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ApiOperationModel>(`/api/open/v1/operations/${id}`, data, options),

    /**
     * No description
     *
     * @tags Operations
     * @name OperationsRecovery
     * @request GET:/api/admin/operations/recovery
     */
    operationsRecovery: (
      query?: {
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        /** @format guid */
        CorrelationId?: string | null;
        /** @format uint64 */
        From?: number | null;
        /** @format uint64 */
        To?: number | null;
        Domain?: string | null;
        EventCode?: string | null;
        Outcome?: OperationalEventOutcome | null;
        ErrorCategory?: OperationalErrorCategory | null;
        /** @format guid */
        ActorUserId?: string | null;
        /** @format guid */
        OwnerUserId?: string | null;
        /** @format int32 */
        OwnerTeamId?: number | null;
        /** @format int32 */
        GameId?: number | null;
        /** @format int32 */
        CourseId?: number | null;
        /** @format int32 */
        ChallengeId?: number | null;
        /** @format int32 */
        ImageTemplateId?: number | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        /** @format int32 */
        TeamLabRuntimeId?: number | null;
        /** @format guid */
        VmInstanceId?: string | null;
        SubjectType?: string | null;
        SubjectId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<OperationalEventViewPageModel, any>({
        path: `/api/admin/operations/recovery`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Operations
     * @name OperationsRecovery
     * @request GET:/api/admin/operations/recovery
     */
    useOperationsRecovery: (
      query?: {
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        /** @format guid */
        CorrelationId?: string | null;
        /** @format uint64 */
        From?: number | null;
        /** @format uint64 */
        To?: number | null;
        Domain?: string | null;
        EventCode?: string | null;
        Outcome?: OperationalEventOutcome | null;
        ErrorCategory?: OperationalErrorCategory | null;
        /** @format guid */
        ActorUserId?: string | null;
        /** @format guid */
        OwnerUserId?: string | null;
        /** @format int32 */
        OwnerTeamId?: number | null;
        /** @format int32 */
        GameId?: number | null;
        /** @format int32 */
        CourseId?: number | null;
        /** @format int32 */
        ChallengeId?: number | null;
        /** @format int32 */
        ImageTemplateId?: number | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        /** @format int32 */
        TeamLabRuntimeId?: number | null;
        /** @format guid */
        VmInstanceId?: string | null;
        SubjectType?: string | null;
        SubjectId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OperationalEventViewPageModel, any>(
        doFetch ? [`/api/admin/operations/recovery`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Operations
     * @name OperationsRecovery
     * @request GET:/api/admin/operations/recovery
     */
    mutateOperationsRecovery: (
      query?: {
        Cursor?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 200
         */
        Count?: number;
        /** @format guid */
        CorrelationId?: string | null;
        /** @format uint64 */
        From?: number | null;
        /** @format uint64 */
        To?: number | null;
        Domain?: string | null;
        EventCode?: string | null;
        Outcome?: OperationalEventOutcome | null;
        ErrorCategory?: OperationalErrorCategory | null;
        /** @format guid */
        ActorUserId?: string | null;
        /** @format guid */
        OwnerUserId?: string | null;
        /** @format int32 */
        OwnerTeamId?: number | null;
        /** @format int32 */
        GameId?: number | null;
        /** @format int32 */
        CourseId?: number | null;
        /** @format int32 */
        ChallengeId?: number | null;
        /** @format int32 */
        ImageTemplateId?: number | null;
        /** @format guid */
        WorkerNodeId?: string | null;
        /** @format guid */
        DeploymentTicketId?: string | null;
        /** @format int32 */
        TeamLabRuntimeId?: number | null;
        /** @format guid */
        VmInstanceId?: string | null;
        SubjectType?: string | null;
        SubjectId?: string | null;
        ResourceType?: string | null;
        ResourceId?: string | null;
      },
      data?:
        | OperationalEventViewPageModel
        | Promise<OperationalEventViewPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OperationalEventViewPageModel>(
        [`/api/admin/operations/recovery`, query],
        data,
        options,
      ),
  };
  penetrationAdmin = {
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminActivateRelease
     * @request POST:/api/admin/pentest/games/{gameId}/releases/{releaseId}/activate
     */
    penetrationAdminActivateRelease: (
      gameId: number,
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/releases/${releaseId}/activate`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminBind
     * @request PUT:/api/admin/pentest/games/{gameId}/binding
     */
    penetrationAdminBind: (
      gameId: number,
      data: BindPenetrationTopologyModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/binding`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
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
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teams/${teamId}/cleanup`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminCloseTeamLabAccess
     * @request POST:/api/admin/pentest/games/{gameId}/teamlab/access/close
     */
    penetrationAdminCloseTeamLabAccess: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/access/close`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminDeleteTeamLabOperator
     * @request DELETE:/api/admin/pentest/games/{gameId}/teamlab/operators/{userId}
     */
    penetrationAdminDeleteTeamLabOperator: (
      gameId: number,
      userId: string,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/operators/${userId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminDeploy
     * @request POST:/api/admin/pentest/games/{gameId}/deploy
     */
    penetrationAdminDeploy: (gameId: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/deploy`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminDrainTeamLab
     * @request POST:/api/admin/pentest/games/{gameId}/teamlab/drain
     */
    penetrationAdminDrainTeamLab: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/drain`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetBinding
     * @request GET:/api/admin/pentest/games/{gameId}/binding
     */
    penetrationAdminGetBinding: (gameId: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/binding`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetBinding
     * @request GET:/api/admin/pentest/games/{gameId}/binding
     */
    usePenetrationAdminGetBinding: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/admin/pentest/games/${gameId}/binding` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetBinding
     * @request GET:/api/admin/pentest/games/{gameId}/binding
     */
    mutatePenetrationAdminGetBinding: (
      gameId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(`/api/admin/pentest/games/${gameId}/binding`, data, options),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetRuntimes
     * @request GET:/api/admin/pentest/games/{gameId}/runtimes
     */
    penetrationAdminGetRuntimes: (gameId: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/runtimes`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetRuntimes
     * @request GET:/api/admin/pentest/games/{gameId}/runtimes
     */
    usePenetrationAdminGetRuntimes: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/admin/pentest/games/${gameId}/runtimes` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetRuntimes
     * @request GET:/api/admin/pentest/games/{gameId}/runtimes
     */
    mutatePenetrationAdminGetRuntimes: (
      gameId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/admin/pentest/games/${gameId}/runtimes`,
        data,
        options,
      ),

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
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/scoreboard`,
        method: "GET",
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
      useSWR<Blob, any>(
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
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
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
         * @min 1
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
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/submissions`,
        method: "GET",
        query: query,
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
         * @min 1
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
      useSWR<Blob, any>(
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
         * @min 1
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
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        [`/api/admin/pentest/games/${gameId}/submissions`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetTeamLab
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab
     */
    penetrationAdminGetTeamLab: (gameId: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetTeamLab
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab
     */
    usePenetrationAdminGetTeamLab: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/admin/pentest/games/${gameId}/teamlab` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminGetTeamLab
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab
     */
    mutatePenetrationAdminGetTeamLab: (
      gameId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(`/api/admin/pentest/games/${gameId}/teamlab`, data, options),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminListTeamLabOperators
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab/operators
     */
    penetrationAdminListTeamLabOperators: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/operators`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminListTeamLabOperators
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab/operators
     */
    usePenetrationAdminListTeamLabOperators: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/admin/pentest/games/${gameId}/teamlab/operators` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminListTeamLabOperators
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab/operators
     */
    mutatePenetrationAdminListTeamLabOperators: (
      gameId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/admin/pentest/games/${gameId}/teamlab/operators`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminListTeamLabReleases
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab/releases
     */
    penetrationAdminListTeamLabReleases: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/releases`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminListTeamLabReleases
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab/releases
     */
    usePenetrationAdminListTeamLabReleases: (
      gameId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/admin/pentest/games/${gameId}/teamlab/releases` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminListTeamLabReleases
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab/releases
     */
    mutatePenetrationAdminListTeamLabReleases: (
      gameId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/admin/pentest/games/${gameId}/teamlab/releases`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminListTeamLabTargets
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab/targets
     */
    penetrationAdminListTeamLabTargets: (
      gameId: number,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 30
         */
        limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/targets`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminListTeamLabTargets
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab/targets
     */
    usePenetrationAdminListTeamLabTargets: (
      gameId: number,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 30
         */
        limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? [`/api/admin/pentest/games/${gameId}/teamlab/targets`, query]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminListTeamLabTargets
     * @request GET:/api/admin/pentest/games/{gameId}/teamlab/targets
     */
    mutatePenetrationAdminListTeamLabTargets: (
      gameId: number,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 30
         */
        limit?: number;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        [`/api/admin/pentest/games/${gameId}/teamlab/targets`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminOpenTeamLabAccess
     * @request POST:/api/admin/pentest/games/{gameId}/teamlab/access/open
     */
    penetrationAdminOpenTeamLabAccess: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/access/open`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminPauseTeamLab
     * @request POST:/api/admin/pentest/games/{gameId}/teamlab/pause
     */
    penetrationAdminPauseTeamLab: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/pause`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminPrepareTeamLab
     * @request POST:/api/admin/pentest/games/{gameId}/teamlab/prepare
     */
    penetrationAdminPrepareTeamLab: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/prepare`,
        method: "POST",
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
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teams/${teamId}/rebuild`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminReplaceObjectives
     * @request PUT:/api/admin/pentest/games/{gameId}/objectives
     */
    penetrationAdminReplaceObjectives: (
      gameId: number,
      data: ReplacePenetrationObjectivesModel,
      params: RequestParams = {},
    ) =>
      this.request<PenetrationGameLabBindingModel, RequestResponse>({
        path: `/api/admin/pentest/games/${gameId}/objectives`,
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
     * @name PenetrationAdminResumeTeamLab
     * @request POST:/api/admin/pentest/games/{gameId}/teamlab/resume
     */
    penetrationAdminResumeTeamLab: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/resume`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationAdmin
     * @name PenetrationAdminSetTeamLabOperator
     * @request PUT:/api/admin/pentest/games/{gameId}/teamlab/operators/{userId}
     */
    penetrationAdminSetTeamLabOperator: (
      gameId: number,
      userId: string,
      data: TeamLabOperatorGrantWriteModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/teamlab/operators/${userId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
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
      this.request<Blob, any>({
        path: `/api/admin/pentest/games/${gameId}/stop`,
        method: "POST",
        ...params,
      }),
  };
  penetrationPlayer = {
    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerCreateAccessGrant
     * @request POST:/api/pentest/games/{gameId}/access-grants
     */
    penetrationPlayerCreateAccessGrant: (
      gameId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/pentest/games/${gameId}/access-grants`,
        method: "POST",
        ...params,
      }),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerDownloadAccessGrant
     * @request GET:/api/pentest/games/{gameId}/access-grants/{grantId}/download
     */
    penetrationPlayerDownloadAccessGrant: (
      gameId: number,
      grantId: string,
      query?: {
        token?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/pentest/games/${gameId}/access-grants/${grantId}/download`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerDownloadAccessGrant
     * @request GET:/api/pentest/games/{gameId}/access-grants/{grantId}/download
     */
    usePenetrationPlayerDownloadAccessGrant: (
      gameId: number,
      grantId: string,
      query?: {
        token?: string;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? [
              `/api/pentest/games/${gameId}/access-grants/${grantId}/download`,
              query,
            ]
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerDownloadAccessGrant
     * @request GET:/api/pentest/games/{gameId}/access-grants/{grantId}/download
     */
    mutatePenetrationPlayerDownloadAccessGrant: (
      gameId: number,
      grantId: string,
      query?: {
        token?: string;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        [
          `/api/pentest/games/${gameId}/access-grants/${grantId}/download`,
          query,
        ],
        data,
        options,
      ),

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
      this.request<Blob, any>({
        path: `/api/pentest/games/${gameId}/scoreboard`,
        method: "GET",
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
      useSWR<Blob, any>(
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
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/pentest/games/${gameId}/scoreboard`, data, options),

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
      this.request<Blob, any>({
        path: `/api/pentest/games/${gameId}/workspace`,
        method: "GET",
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
      useSWR<Blob, any>(
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
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) => mutate<Blob>(`/api/pentest/games/${gameId}/workspace`, data, options),

    /**
     * No description
     *
     * @tags PenetrationPlayer
     * @name PenetrationPlayerReset
     * @request POST:/api/pentest/games/{gameId}/reset
     */
    penetrationPlayerReset: (gameId: number, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/pentest/games/${gameId}/reset`,
        method: "POST",
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
      this.request<Blob, any>({
        path: `/api/pentest/games/${gameId}/submit`,
        method: "POST",
        body: data,
        type: ContentType.Json,
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
        tag?: string[] | null;
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
        tag?: string[] | null;
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
        tag?: string[] | null;
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
  users = {
    /**
     * No description
     *
     * @tags Users
     * @name UsersActivity
     * @request GET:/api/users/{userId}/activity
     */
    usersActivity: (
      userId: string,
      query?: {
        /** @format date */
        from?: string;
        /** @format date */
        to?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<UserActivityPointModel[], RequestResponse>({
        path: `/api/users/${userId}/activity`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Users
     * @name UsersActivity
     * @request GET:/api/users/{userId}/activity
     */
    useUsersActivity: (
      userId: string,
      query?: {
        /** @format date */
        from?: string;
        /** @format date */
        to?: string;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<UserActivityPointModel[], RequestResponse>(
        doFetch ? [`/api/users/${userId}/activity`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Users
     * @name UsersActivity
     * @request GET:/api/users/{userId}/activity
     */
    mutateUsersActivity: (
      userId: string,
      query?: {
        /** @format date */
        from?: string;
        /** @format date */
        to?: string;
      },
      data?: UserActivityPointModel[] | Promise<UserActivityPointModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<UserActivityPointModel[]>(
        [`/api/users/${userId}/activity`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Users
     * @name UsersHistory
     * @request GET:/api/users/{userId}/history
     */
    usersHistory: (
      userId: string,
      query?: {
        /** @default "all" */
        type?: string | null;
        cursor?: string | null;
        /**
         * @format int32
         * @default 20
         */
        count?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<UserProfileHistoryPageModel, RequestResponse>({
        path: `/api/users/${userId}/history`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Users
     * @name UsersHistory
     * @request GET:/api/users/{userId}/history
     */
    useUsersHistory: (
      userId: string,
      query?: {
        /** @default "all" */
        type?: string | null;
        cursor?: string | null;
        /**
         * @format int32
         * @default 20
         */
        count?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<UserProfileHistoryPageModel, RequestResponse>(
        doFetch ? [`/api/users/${userId}/history`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Users
     * @name UsersHistory
     * @request GET:/api/users/{userId}/history
     */
    mutateUsersHistory: (
      userId: string,
      query?: {
        /** @default "all" */
        type?: string | null;
        cursor?: string | null;
        /**
         * @format int32
         * @default 20
         */
        count?: number;
      },
      data?: UserProfileHistoryPageModel | Promise<UserProfileHistoryPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<UserProfileHistoryPageModel>(
        [`/api/users/${userId}/history`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Users
     * @name UsersOverview
     * @request GET:/api/users/{userId}/overview
     */
    usersOverview: (
      userId: string,
      query?: {
        /** @default "365d" */
        window?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<UserProfileOverviewModel, void | RequestResponse>({
        path: `/api/users/${userId}/overview`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Users
     * @name UsersOverview
     * @request GET:/api/users/{userId}/overview
     */
    useUsersOverview: (
      userId: string,
      query?: {
        /** @default "365d" */
        window?: string;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<UserProfileOverviewModel, void | RequestResponse>(
        doFetch ? [`/api/users/${userId}/overview`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Users
     * @name UsersOverview
     * @request GET:/api/users/{userId}/overview
     */
    mutateUsersOverview: (
      userId: string,
      query?: {
        /** @default "365d" */
        window?: string;
      },
      data?: UserProfileOverviewModel | Promise<UserProfileOverviewModel>,
      options?: MutatorOptions,
    ) =>
      mutate<UserProfileOverviewModel>(
        [`/api/users/${userId}/overview`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Users
     * @name UsersPrivateOverview
     * @request GET:/api/users/me/private-overview
     */
    usersPrivateOverview: (params: RequestParams = {}) =>
      this.request<UserPrivateOverviewModel, RequestResponse>({
        path: `/api/users/me/private-overview`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Users
     * @name UsersPrivateOverview
     * @request GET:/api/users/me/private-overview
     */
    useUsersPrivateOverview: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<UserPrivateOverviewModel, RequestResponse>(
        doFetch ? `/api/users/me/private-overview` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Users
     * @name UsersPrivateOverview
     * @request GET:/api/users/me/private-overview
     */
    mutateUsersPrivateOverview: (
      data?: UserPrivateOverviewModel | Promise<UserPrivateOverviewModel>,
      options?: MutatorOptions,
    ) =>
      mutate<UserPrivateOverviewModel>(
        `/api/users/me/private-overview`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags Users
     * @name UsersProfile
     * @request GET:/api/users/{userId}
     */
    usersProfile: (userId: string, params: RequestParams = {}) =>
      this.request<PublicUserProfileModel, void | RequestResponse>({
        path: `/api/users/${userId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags Users
     * @name UsersProfile
     * @request GET:/api/users/{userId}
     */
    useUsersProfile: (
      userId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<PublicUserProfileModel, void | RequestResponse>(
        doFetch ? `/api/users/${userId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags Users
     * @name UsersProfile
     * @request GET:/api/users/{userId}
     */
    mutateUsersProfile: (
      userId: string,
      data?: PublicUserProfileModel | Promise<PublicUserProfileModel>,
      options?: MutatorOptions,
    ) => mutate<PublicUserProfileModel>(`/api/users/${userId}`, data, options),
  };
  teamLabConnectors = {
    /**
     * @description 为运行时申请连接器租约；独占连接器同一时间只属于一个运行时，重复申请幂等返回
     *
     * @tags TeamLab - Connectors
     * @name OpenTeamLabConnectorsAcquire
     * @summary 占用现场连接器
     * @request POST:/api/open/v1/teamlab/connectors/{connectorId}/leases
     */
    openTeamLabConnectorsAcquire: (
      connectorId: string,
      data: AcquireTeamLabConnectorLeaseModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabConnectorLeaseModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/connectors/${connectorId}/leases`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 返回类型、授权范围、容量、健康与当前占用
     *
     * @tags TeamLab - Connectors
     * @name OpenTeamLabConnectorsGet
     * @summary 获取现场连接器
     * @request GET:/api/open/v1/teamlab/connectors/{connectorId}
     */
    openTeamLabConnectorsGet: (
      connectorId: string,
      query?: {
        /** @format guid */
        scopeId?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabConnectorModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/connectors/${connectorId}`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 返回类型、授权范围、容量、健康与当前占用
     *
     * @tags TeamLab - Connectors
     * @name OpenTeamLabConnectorsGet
     * @summary 获取现场连接器
     * @request GET:/api/open/v1/teamlab/connectors/{connectorId}
     */
    useOpenTeamLabConnectorsGet: (
      connectorId: string,
      query?: {
        /** @format guid */
        scopeId?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabConnectorModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? [`/api/open/v1/teamlab/connectors/${connectorId}`, query]
          : null,
        options,
      ),

    /**
     * @description 返回类型、授权范围、容量、健康与当前占用
     *
     * @tags TeamLab - Connectors
     * @name OpenTeamLabConnectorsGet
     * @summary 获取现场连接器
     * @request GET:/api/open/v1/teamlab/connectors/{connectorId}
     */
    mutateOpenTeamLabConnectorsGet: (
      connectorId: string,
      query?: {
        /** @format guid */
        scopeId?: string | null;
      },
      data?: TeamLabConnectorModel | Promise<TeamLabConnectorModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabConnectorModel>(
        [`/api/open/v1/teamlab/connectors/${connectorId}`, query],
        data,
        options,
      ),

    /**
     * @description 列出平台级与已授权 control scope 的连接器及占用状态，不暴露接入地址
     *
     * @tags TeamLab - Connectors
     * @name OpenTeamLabConnectorsList
     * @summary 列出现场连接器
     * @request GET:/api/open/v1/teamlab/connectors
     */
    openTeamLabConnectorsList: (
      query?: {
        /** @format guid */
        scopeId?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabConnectorPageModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/connectors`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 列出平台级与已授权 control scope 的连接器及占用状态，不暴露接入地址
     *
     * @tags TeamLab - Connectors
     * @name OpenTeamLabConnectorsList
     * @summary 列出现场连接器
     * @request GET:/api/open/v1/teamlab/connectors
     */
    useOpenTeamLabConnectorsList: (
      query?: {
        /** @format guid */
        scopeId?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabConnectorPageModel, ExternalApiProblemDetailsModel>(
        doFetch ? [`/api/open/v1/teamlab/connectors`, query] : null,
        options,
      ),

    /**
     * @description 列出平台级与已授权 control scope 的连接器及占用状态，不暴露接入地址
     *
     * @tags TeamLab - Connectors
     * @name OpenTeamLabConnectorsList
     * @summary 列出现场连接器
     * @request GET:/api/open/v1/teamlab/connectors
     */
    mutateOpenTeamLabConnectorsList: (
      query?: {
        /** @format guid */
        scopeId?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: TeamLabConnectorPageModel | Promise<TeamLabConnectorPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabConnectorPageModel>(
        [`/api/open/v1/teamlab/connectors`, query],
        data,
        options,
      ),

    /**
     * @description 释放该运行时的活动租约；重复释放幂等返回
     *
     * @tags TeamLab - Connectors
     * @name OpenTeamLabConnectorsRelease
     * @summary 释放现场连接器
     * @request POST:/api/open/v1/teamlab/connectors/{connectorId}/leases/release
     */
    openTeamLabConnectorsRelease: (
      connectorId: string,
      data: ReleaseTeamLabConnectorLeaseModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabConnectorLeaseModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/connectors/${connectorId}/leases/release`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  teamLabDevicePackages = {
    /**
     * @description 返回版本、制品引用、资源需求、参数 schema 与能力声明
     *
     * @tags TeamLab - Device packages
     * @name OpenTeamLabDevicePackagesGet
     * @summary 获取设备包版本
     * @request GET:/api/open/v1/teamlab/device-packages/{packageId}
     */
    openTeamLabDevicePackagesGet: (
      packageId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabDevicePackageModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/device-packages/${packageId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回版本、制品引用、资源需求、参数 schema 与能力声明
     *
     * @tags TeamLab - Device packages
     * @name OpenTeamLabDevicePackagesGet
     * @summary 获取设备包版本
     * @request GET:/api/open/v1/teamlab/device-packages/{packageId}
     */
    useOpenTeamLabDevicePackagesGet: (
      packageId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabDevicePackageModel, ExternalApiProblemDetailsModel>(
        doFetch ? `/api/open/v1/teamlab/device-packages/${packageId}` : null,
        options,
      ),

    /**
     * @description 返回版本、制品引用、资源需求、参数 schema 与能力声明
     *
     * @tags TeamLab - Device packages
     * @name OpenTeamLabDevicePackagesGet
     * @summary 获取设备包版本
     * @request GET:/api/open/v1/teamlab/device-packages/{packageId}
     */
    mutateOpenTeamLabDevicePackagesGet: (
      packageId: string,
      data?: TeamLabDevicePackageModel | Promise<TeamLabDevicePackageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabDevicePackageModel>(
        `/api/open/v1/teamlab/device-packages/${packageId}`,
        data,
        options,
      ),

    /**
     * @description 按名称过滤返回不可变设备包版本，使用稳定 cursor 分页
     *
     * @tags TeamLab - Device packages
     * @name OpenTeamLabDevicePackagesList
     * @summary 列出设备包
     * @request GET:/api/open/v1/teamlab/device-packages
     */
    openTeamLabDevicePackagesList: (
      query?: {
        name?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        TeamLabDevicePackagePageModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/device-packages`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 按名称过滤返回不可变设备包版本，使用稳定 cursor 分页
     *
     * @tags TeamLab - Device packages
     * @name OpenTeamLabDevicePackagesList
     * @summary 列出设备包
     * @request GET:/api/open/v1/teamlab/device-packages
     */
    useOpenTeamLabDevicePackagesList: (
      query?: {
        name?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabDevicePackagePageModel, ExternalApiProblemDetailsModel>(
        doFetch ? [`/api/open/v1/teamlab/device-packages`, query] : null,
        options,
      ),

    /**
     * @description 按名称过滤返回不可变设备包版本，使用稳定 cursor 分页
     *
     * @tags TeamLab - Device packages
     * @name OpenTeamLabDevicePackagesList
     * @summary 列出设备包
     * @request GET:/api/open/v1/teamlab/device-packages
     */
    mutateOpenTeamLabDevicePackagesList: (
      query?: {
        name?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?:
        | TeamLabDevicePackagePageModel
        | Promise<TeamLabDevicePackagePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabDevicePackagePageModel>(
        [`/api/open/v1/teamlab/device-packages`, query],
        data,
        options,
      ),
  };
  teamLabImagePreparation = {
    /**
     * @description 返回发布版本的就绪投影：planAvailable/preparing/readyToStart/blocked 与按模板统计的节点就绪计数。
     *
     * @tags TeamLab - Image Preparation
     * @name OpenTeamLabImagePreparationsGet
     * @summary 获取镜像准备状态
     * @request GET:/api/open/v1/teamlab/preparations/releases/{releaseId}
     */
    openTeamLabImagePreparationsGet: (
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<
        TeamLabReleasePreparationModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/preparations/releases/${releaseId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回发布版本的就绪投影：planAvailable/preparing/readyToStart/blocked 与按模板统计的节点就绪计数。
     *
     * @tags TeamLab - Image Preparation
     * @name OpenTeamLabImagePreparationsGet
     * @summary 获取镜像准备状态
     * @request GET:/api/open/v1/teamlab/preparations/releases/{releaseId}
     */
    useOpenTeamLabImagePreparationsGet: (
      releaseId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabReleasePreparationModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? `/api/open/v1/teamlab/preparations/releases/${releaseId}`
          : null,
        options,
      ),

    /**
     * @description 返回发布版本的就绪投影：planAvailable/preparing/readyToStart/blocked 与按模板统计的节点就绪计数。
     *
     * @tags TeamLab - Image Preparation
     * @name OpenTeamLabImagePreparationsGet
     * @summary 获取镜像准备状态
     * @request GET:/api/open/v1/teamlab/preparations/releases/{releaseId}
     */
    mutateOpenTeamLabImagePreparationsGet: (
      releaseId: string,
      data?:
        | TeamLabReleasePreparationModel
        | Promise<TeamLabReleasePreparationModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabReleasePreparationModel>(
        `/api/open/v1/teamlab/preparations/releases/${releaseId}`,
        data,
        options,
      ),

    /**
     * @description 幂等提交发布版本的镜像预分发，适用于发布后或失败后的显式重试。
     *
     * @tags TeamLab - Image Preparation
     * @name OpenTeamLabImagePreparationsQueue
     * @summary 提交镜像准备
     * @request POST:/api/open/v1/teamlab/preparations/releases/{releaseId}
     */
    openTeamLabImagePreparationsQueue: (
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/preparations/releases/${releaseId}`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 幂等释放发布版本的镜像预分发引用，停止继续保留准备状态。
     *
     * @tags TeamLab - Image Preparation
     * @name OpenTeamLabImagePreparationsRelease
     * @summary 释放镜像准备引用
     * @request DELETE:/api/open/v1/teamlab/preparations/releases/{releaseId}
     */
    openTeamLabImagePreparationsRelease: (
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/preparations/releases/${releaseId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  teamLabLinkPolicies = {
    /**
     * @description 在运行时网段/链路上声明式应用损伤或访问策略；同参数重复应用幂等，不同参数需先恢复
     *
     * @tags TeamLab - Link policies
     * @name OpenTeamLabLinkPoliciesApply
     * @summary 应用链路策略
     * @request POST:/api/open/v1/teamlab/link-policies
     */
    openTeamLabLinkPoliciesApply: (
      data: ApplyTeamLabLinkPolicyModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabLinkPolicyModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/link-policies`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 默认返回未恢复的策略，可按 active/recovered/failed 过滤
     *
     * @tags TeamLab - Link policies
     * @name OpenTeamLabLinkPoliciesList
     * @summary 列出运行时链路策略
     * @request GET:/api/open/v1/teamlab/link-policies
     */
    openTeamLabLinkPoliciesList: (
      query?: {
        /** @format guid */
        runtimeId?: string;
        status?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabLinkPolicyPageModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/link-policies`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 默认返回未恢复的策略，可按 active/recovered/failed 过滤
     *
     * @tags TeamLab - Link policies
     * @name OpenTeamLabLinkPoliciesList
     * @summary 列出运行时链路策略
     * @request GET:/api/open/v1/teamlab/link-policies
     */
    useOpenTeamLabLinkPoliciesList: (
      query?: {
        /** @format guid */
        runtimeId?: string;
        status?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabLinkPolicyPageModel, ExternalApiProblemDetailsModel>(
        doFetch ? [`/api/open/v1/teamlab/link-policies`, query] : null,
        options,
      ),

    /**
     * @description 默认返回未恢复的策略，可按 active/recovered/failed 过滤
     *
     * @tags TeamLab - Link policies
     * @name OpenTeamLabLinkPoliciesList
     * @summary 列出运行时链路策略
     * @request GET:/api/open/v1/teamlab/link-policies
     */
    mutateOpenTeamLabLinkPoliciesList: (
      query?: {
        /** @format guid */
        runtimeId?: string;
        status?: string | null;
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: TeamLabLinkPolicyPageModel | Promise<TeamLabLinkPolicyPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabLinkPolicyPageModel>(
        [`/api/open/v1/teamlab/link-policies`, query],
        data,
        options,
      ),

    /**
     * @description 手工恢复一条活动或失败的链路策略；已恢复的策略幂等返回
     *
     * @tags TeamLab - Link policies
     * @name OpenTeamLabLinkPoliciesRecover
     * @summary 恢复链路策略
     * @request POST:/api/open/v1/teamlab/link-policies/{policyId}/recover
     */
    openTeamLabLinkPoliciesRecover: (
      policyId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabLinkPolicyModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/link-policies/${policyId}/recover`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
  teamLabRemoteSessions = {
    /**
     * @description 返回运行时全部资产的可用协议与不可用原因。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsAvailability
     * @summary 查询远程访问可用性
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/remote-access
     */
    openTeamLabRemoteSessionsAvailability: (
      runtimeId: string,
      params: RequestParams = {},
    ) =>
      this.request<
        OpenTeamLabRemoteAvailabilityModel[],
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/remote-access`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回运行时全部资产的可用协议与不可用原因。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsAvailability
     * @summary 查询远程访问可用性
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/remote-access
     */
    useOpenTeamLabRemoteSessionsAvailability: (
      runtimeId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<
        OpenTeamLabRemoteAvailabilityModel[],
        ExternalApiProblemDetailsModel
      >(
        doFetch
          ? `/api/open/v1/teamlab/runtimes/${runtimeId}/remote-access`
          : null,
        options,
      ),

    /**
     * @description 返回运行时全部资产的可用协议与不可用原因。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsAvailability
     * @summary 查询远程访问可用性
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/remote-access
     */
    mutateOpenTeamLabRemoteSessionsAvailability: (
      runtimeId: string,
      data?:
        | OpenTeamLabRemoteAvailabilityModel[]
        | Promise<OpenTeamLabRemoteAvailabilityModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabRemoteAvailabilityModel[]>(
        `/api/open/v1/teamlab/runtimes/${runtimeId}/remote-access`,
        data,
        options,
      ),

    /**
     * @description 消费 VM 会话连接入口；入口只返回一次，过期需重新创建会话。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsConnect
     * @summary 获取一次性远程连接
     * @request POST:/api/open/v1/teamlab/remote-sessions/{sessionId}/connect
     */
    openTeamLabRemoteSessionsConnect: (
      sessionId: string,
      params: RequestParams = {},
    ) =>
      this.request<any, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/remote-sessions/${sessionId}/connect`,
        method: "POST",
        ...params,
      }),

    /**
     * @description 为单个资产创建限时会话；VM 使用 connect，容器使用携带 Bearer 身份的 terminal WebSocket。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsCreate
     * @summary 创建远程会话
     * @request POST:/api/open/v1/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-sessions
     */
    openTeamLabRemoteSessionsCreate: (
      runtimeId: string,
      assetId: number,
      data: OpenCreateTeamLabRemoteSessionModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/assets/${assetId}/remote-sessions`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 主动结束会话并回收转发通道；重复关闭幂等。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsEnd
     * @summary 关闭远程会话
     * @request DELETE:/api/open/v1/teamlab/remote-sessions/{sessionId}
     */
    openTeamLabRemoteSessionsEnd: (
      sessionId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/remote-sessions/${sessionId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description 返回会话状态、协议、访问原因与时间线。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsGet
     * @summary 查询远程会话
     * @request GET:/api/open/v1/teamlab/remote-sessions/{sessionId}
     */
    openTeamLabRemoteSessionsGet: (
      sessionId: string,
      params: RequestParams = {},
    ) =>
      this.request<
        OpenTeamLabRemoteSessionModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/remote-sessions/${sessionId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回会话状态、协议、访问原因与时间线。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsGet
     * @summary 查询远程会话
     * @request GET:/api/open/v1/teamlab/remote-sessions/{sessionId}
     */
    useOpenTeamLabRemoteSessionsGet: (
      sessionId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenTeamLabRemoteSessionModel, ExternalApiProblemDetailsModel>(
        doFetch ? `/api/open/v1/teamlab/remote-sessions/${sessionId}` : null,
        options,
      ),

    /**
     * @description 返回会话状态、协议、访问原因与时间线。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsGet
     * @summary 查询远程会话
     * @request GET:/api/open/v1/teamlab/remote-sessions/{sessionId}
     */
    mutateOpenTeamLabRemoteSessionsGet: (
      sessionId: string,
      data?:
        | OpenTeamLabRemoteSessionModel
        | Promise<OpenTeamLabRemoteSessionModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabRemoteSessionModel>(
        `/api/open/v1/teamlab/remote-sessions/${sessionId}`,
        data,
        options,
      ),

    /**
     * @description WebSocket：二进制消息为 PTY 字节；文本 JSON 支持 resize、input、signal。Bearer token 必须在 Authorization 请求头中。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsTerminal
     * @summary 连接容器终端
     * @request GET:/api/open/v1/teamlab/remote-sessions/{sessionId}/terminal
     */
    openTeamLabRemoteSessionsTerminal: (
      sessionId: string,
      params: RequestParams = {},
    ) =>
      this.request<any, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/remote-sessions/${sessionId}/terminal`,
        method: "GET",
        ...params,
      }),
    /**
     * @description WebSocket：二进制消息为 PTY 字节；文本 JSON 支持 resize、input、signal。Bearer token 必须在 Authorization 请求头中。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsTerminal
     * @summary 连接容器终端
     * @request GET:/api/open/v1/teamlab/remote-sessions/{sessionId}/terminal
     */
    useOpenTeamLabRemoteSessionsTerminal: (
      sessionId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<any, ExternalApiProblemDetailsModel>(
        doFetch
          ? `/api/open/v1/teamlab/remote-sessions/${sessionId}/terminal`
          : null,
        options,
      ),

    /**
     * @description WebSocket：二进制消息为 PTY 字节；文本 JSON 支持 resize、input、signal。Bearer token 必须在 Authorization 请求头中。
     *
     * @tags TeamLab - Remote sessions
     * @name OpenTeamLabRemoteSessionsTerminal
     * @summary 连接容器终端
     * @request GET:/api/open/v1/teamlab/remote-sessions/{sessionId}/terminal
     */
    mutateOpenTeamLabRemoteSessionsTerminal: (
      sessionId: string,
      data?: any | Promise<any>,
      options?: MutatorOptions,
    ) =>
      mutate<any>(
        `/api/open/v1/teamlab/remote-sessions/${sessionId}/terminal`,
        data,
        options,
      ),
  };
  teamLabResourcePools = {
    /**
     * @description 按节点与模板返回分发状态、阶段与活动用途引用计数
     *
     * @tags TeamLab - Resource pools
     * @name OpenTeamLabResourcePoolsNodeCache
     * @summary 列出节点制品缓存
     * @request GET:/api/open/v1/teamlab/resource-pools/node-cache
     */
    openTeamLabResourcePoolsNodeCache: (
      query?: {
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabNodeCachePageModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/resource-pools/node-cache`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 按节点与模板返回分发状态、阶段与活动用途引用计数
     *
     * @tags TeamLab - Resource pools
     * @name OpenTeamLabResourcePoolsNodeCache
     * @summary 列出节点制品缓存
     * @request GET:/api/open/v1/teamlab/resource-pools/node-cache
     */
    useOpenTeamLabResourcePoolsNodeCache: (
      query?: {
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabNodeCachePageModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? [`/api/open/v1/teamlab/resource-pools/node-cache`, query]
          : null,
        options,
      ),

    /**
     * @description 按节点与模板返回分发状态、阶段与活动用途引用计数
     *
     * @tags TeamLab - Resource pools
     * @name OpenTeamLabResourcePoolsNodeCache
     * @summary 列出节点制品缓存
     * @request GET:/api/open/v1/teamlab/resource-pools/node-cache
     */
    mutateOpenTeamLabResourcePoolsNodeCache: (
      query?: {
        /**
         * @format int32
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: TeamLabNodeCachePageModel | Promise<TeamLabNodeCachePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabNodeCachePageModel>(
        [`/api/open/v1/teamlab/resource-pools/node-cache`, query],
        data,
        options,
      ),

    /**
     * @description 返回计算节点与模板的只读容量/状态投影，不暴露执行面地址
     *
     * @tags TeamLab - Resource pools
     * @name OpenTeamLabResourcePoolsSnapshot
     * @summary 获取资源池快照
     * @request GET:/api/open/v1/teamlab/resource-pools
     */
    openTeamLabResourcePoolsSnapshot: (params: RequestParams = {}) =>
      this.request<
        TeamLabResourcePoolSnapshotModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/resource-pools`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回计算节点与模板的只读容量/状态投影，不暴露执行面地址
     *
     * @tags TeamLab - Resource pools
     * @name OpenTeamLabResourcePoolsSnapshot
     * @summary 获取资源池快照
     * @request GET:/api/open/v1/teamlab/resource-pools
     */
    useOpenTeamLabResourcePoolsSnapshot: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabResourcePoolSnapshotModel, ExternalApiProblemDetailsModel>(
        doFetch ? `/api/open/v1/teamlab/resource-pools` : null,
        options,
      ),

    /**
     * @description 返回计算节点与模板的只读容量/状态投影，不暴露执行面地址
     *
     * @tags TeamLab - Resource pools
     * @name OpenTeamLabResourcePoolsSnapshot
     * @summary 获取资源池快照
     * @request GET:/api/open/v1/teamlab/resource-pools
     */
    mutateOpenTeamLabResourcePoolsSnapshot: (
      data?:
        | TeamLabResourcePoolSnapshotModel
        | Promise<TeamLabResourcePoolSnapshotModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabResourcePoolSnapshotModel>(
        `/api/open/v1/teamlab/resource-pools`,
        data,
        options,
      ),
  };
  teamLabRollouts = {
    /**
     * @description 归档已完全清理的 rollout 并保留只读历史
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsArchive
     * @summary 归档 TeamLab rollout
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/archive
     */
    openTeamLabRolloutsArchive: (
      rolloutId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/archive`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 关闭玩家访问而不销毁 rollout
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsCloseAccess
     * @summary 关闭 rollout 访问
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/close-access
     */
    openTeamLabRolloutsCloseAccess: (
      rolloutId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/close-access`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 基于不可变 release 与 target 快照创建外部 rollout
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsCreate
     * @summary 创建 TeamLab rollout
     * @request POST:/api/open/v1/teamlab/rollouts
     */
    openTeamLabRolloutsCreate: (
      data: CreateTeamLabRolloutModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 关闭访问并按有界批次销毁所有 target runtimes
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsDrain
     * @summary 清理 TeamLab rollout
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/drain
     */
    openTeamLabRolloutsDrain: (rolloutId: string, params: RequestParams = {}) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/drain`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 返回 rollout 状态、target 数量与生命周期时间戳
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsGet
     * @summary 获取 TeamLab rollout
     * @request GET:/api/open/v1/teamlab/rollouts/{rolloutId}
     */
    openTeamLabRolloutsGet: (rolloutId: string, params: RequestParams = {}) =>
      this.request<TeamLabRolloutModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回 rollout 状态、target 数量与生命周期时间戳
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsGet
     * @summary 获取 TeamLab rollout
     * @request GET:/api/open/v1/teamlab/rollouts/{rolloutId}
     */
    useOpenTeamLabRolloutsGet: (
      rolloutId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRolloutModel, ExternalApiProblemDetailsModel>(
        doFetch ? `/api/open/v1/teamlab/rollouts/${rolloutId}` : null,
        options,
      ),

    /**
     * @description 返回 rollout 状态、target 数量与生命周期时间戳
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsGet
     * @summary 获取 TeamLab rollout
     * @request GET:/api/open/v1/teamlab/rollouts/{rolloutId}
     */
    mutateOpenTeamLabRolloutsGet: (
      rolloutId: string,
      data?: TeamLabRolloutModel | Promise<TeamLabRolloutModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRolloutModel>(
        `/api/open/v1/teamlab/rollouts/${rolloutId}`,
        data,
        options,
      ),

    /**
     * @description 列出单个已授权 control scope 内的外部 rollouts
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsList
     * @summary 列出 TeamLab rollouts
     * @request GET:/api/open/v1/teamlab/rollouts
     */
    openTeamLabRolloutsList: (
      query?: {
        /** @format guid */
        scopeId?: string;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabRolloutPageModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 列出单个已授权 control scope 内的外部 rollouts
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsList
     * @summary 列出 TeamLab rollouts
     * @request GET:/api/open/v1/teamlab/rollouts
     */
    useOpenTeamLabRolloutsList: (
      query?: {
        /** @format guid */
        scopeId?: string;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRolloutPageModel, ExternalApiProblemDetailsModel>(
        doFetch ? [`/api/open/v1/teamlab/rollouts`, query] : null,
        options,
      ),

    /**
     * @description 列出单个已授权 control scope 内的外部 rollouts
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsList
     * @summary 列出 TeamLab rollouts
     * @request GET:/api/open/v1/teamlab/rollouts
     */
    mutateOpenTeamLabRolloutsList: (
      query?: {
        /** @format guid */
        scopeId?: string;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: TeamLabRolloutPageModel | Promise<TeamLabRolloutPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRolloutPageModel>(
        [`/api/open/v1/teamlab/rollouts`, query],
        data,
        options,
      ),

    /**
     * @description 仅在所有期望 targets 就绪后开放玩家访问
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsOpenAccess
     * @summary 打开 rollout 访问
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/open-access
     */
    openTeamLabRolloutsOpenAccess: (
      rolloutId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/open-access`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 暂停 rollout 协调，已提交的目标与运行时保持不变
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsPause
     * @summary 暂停 TeamLab rollout
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/pause
     */
    openTeamLabRolloutsPause: (rolloutId: string, params: RequestParams = {}) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/pause`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 在原节点上挂起该 target 的运行时，保留运行时身份、网络与容量预留
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsPauseTarget
     * @summary 暂停单个 rollout target
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/targets/{targetId}/pause
     */
    openTeamLabRolloutsPauseTarget: (
      rolloutId: string,
      targetId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/targets/${targetId}/pause`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 启动 image 准备与 target 供给协调
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsPrepare
     * @summary 准备 TeamLab rollout
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/prepare
     */
    openTeamLabRolloutsPrepare: (
      rolloutId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/prepare`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 请求显式重建单个失败的 target
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsRebuild
     * @summary 重建失败的 rollout target
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/targets/{targetId}/rebuild
     */
    openTeamLabRolloutsRebuild: (
      rolloutId: string,
      targetId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/targets/${targetId}/rebuild`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 替换期望的 target 快照；被移除的 targets 在显式清理前保持不变
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsReplaceTargets
     * @summary 替换 rollout targets
     * @request PUT:/api/open/v1/teamlab/rollouts/{rolloutId}/targets
     */
    openTeamLabRolloutsReplaceTargets: (
      rolloutId: string,
      data: ReplaceTeamLabRolloutTargetsModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/targets`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 按原发布版本受控清理并重新部署该 target 的运行时
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsRestartTarget
     * @summary 重启单个 rollout target
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/targets/{targetId}/restart
     */
    openTeamLabRolloutsRestartTarget: (
      rolloutId: string,
      targetId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/targets/${targetId}/restart`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 从暂停状态恢复 rollout 协调
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsResume
     * @summary 恢复 TeamLab rollout
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/resume
     */
    openTeamLabRolloutsResume: (
      rolloutId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/resume`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 仅在原始分配节点上恢复该 target 的运行时
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsResumeTarget
     * @summary 恢复单个 rollout target
     * @request POST:/api/open/v1/teamlab/rollouts/{rolloutId}/targets/{targetId}/resume
     */
    openTeamLabRolloutsResumeTarget: (
      rolloutId: string,
      targetId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/targets/${targetId}/resume`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 使用稳定 cursor 返回 target 状态
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsTargets
     * @summary 列出 rollout targets
     * @request GET:/api/open/v1/teamlab/rollouts/{rolloutId}/targets
     */
    openTeamLabRolloutsTargets: (
      rolloutId: string,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        TeamLabRolloutTargetPageModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/rollouts/${rolloutId}/targets`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 使用稳定 cursor 返回 target 状态
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsTargets
     * @summary 列出 rollout targets
     * @request GET:/api/open/v1/teamlab/rollouts/{rolloutId}/targets
     */
    useOpenTeamLabRolloutsTargets: (
      rolloutId: string,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabRolloutTargetPageModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? [`/api/open/v1/teamlab/rollouts/${rolloutId}/targets`, query]
          : null,
        options,
      ),

    /**
     * @description 使用稳定 cursor 返回 target 状态
     *
     * @tags TeamLab - Rollouts
     * @name OpenTeamLabRolloutsTargets
     * @summary 列出 rollout targets
     * @request GET:/api/open/v1/teamlab/rollouts/{rolloutId}/targets
     */
    mutateOpenTeamLabRolloutsTargets: (
      rolloutId: string,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?:
        | TeamLabRolloutTargetPageModel
        | Promise<TeamLabRolloutTargetPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabRolloutTargetPageModel>(
        [`/api/open/v1/teamlab/rollouts/${rolloutId}/targets`, query],
        data,
        options,
      ),
  };
  teamLabRuntimes = {
    /**
     * @description 为单个队伍或自动化属主提交已发布拓扑版本的部署任务。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesCreate
     * @summary 创建运行时
     * @request POST:/api/open/v1/teamlab/runtimes
     */
    openTeamLabRuntimesCreate: (
      data: CreateTeamLabRuntimeModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 提交创建短时效、单次下载的玩家访问配置。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesCreateAccessGrant
     * @summary 创建 WireGuard 访问授权
     * @request POST:/api/open/v1/teamlab/runtimes/{runtimeId}/access-grants
     */
    openTeamLabRuntimesCreateAccessGrant: (
      runtimeId: string,
      data: TeamLabAccessGrantCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/access-grants`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 提交清理运行时的所有分片、资产、路由、抓包与访问授权。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesDestroy
     * @summary 销毁运行时
     * @request DELETE:/api/open/v1/teamlab/runtimes/{runtimeId}
     */
    openTeamLabRuntimesDestroy: (
      runtimeId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description 消耗一次性下载 token 并返回 WireGuard 配置文件。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesDownloadAccessConfiguration
     * @summary 下载访问配置
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/access-grants/{grantId}/download
     */
    openTeamLabRuntimesDownloadAccessConfiguration: (
      runtimeId: string,
      grantId: string,
      query?: {
        token?: string;
      },
      params: RequestParams = {},
    ) =>
      this.request<void, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/access-grants/${grantId}/download`,
        method: "GET",
        query: query,
        ...params,
      }),

    /**
     * @description 返回 cursor 分页的生命周期与部署事件，用于排障与审计。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesEvents
     * @summary 列出运行时事件
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/events
     */
    openTeamLabRuntimesEvents: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        /** @format int32 */
        generation?: number | null;
        stage?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        OpenTeamLabRuntimeEventPageModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/events`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 返回 cursor 分页的生命周期与部署事件，用于排障与审计。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesEvents
     * @summary 列出运行时事件
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/events
     */
    useOpenTeamLabRuntimesEvents: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        /** @format int32 */
        generation?: number | null;
        stage?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenTeamLabRuntimeEventPageModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? [`/api/open/v1/teamlab/runtimes/${runtimeId}/events`, query]
          : null,
        options,
      ),

    /**
     * @description 返回 cursor 分页的生命周期与部署事件，用于排障与审计。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesEvents
     * @summary 列出运行时事件
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/events
     */
    mutateOpenTeamLabRuntimesEvents: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        /** @format int32 */
        generation?: number | null;
        stage?: string | null;
      },
      data?:
        | OpenTeamLabRuntimeEventPageModel
        | Promise<OpenTeamLabRuntimeEventPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabRuntimeEventPageModel>(
        [`/api/open/v1/teamlab/runtimes/${runtimeId}/events`, query],
        data,
        options,
      ),

    /**
     * @description 返回聚合的运行时、分片、网络与资产状态。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesGet
     * @summary 获取运行时
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}
     */
    openTeamLabRuntimesGet: (runtimeId: string, params: RequestParams = {}) =>
      this.request<OpenTeamLabRuntimeModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回聚合的运行时、分片、网络与资产状态。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesGet
     * @summary 获取运行时
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}
     */
    useOpenTeamLabRuntimesGet: (
      runtimeId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenTeamLabRuntimeModel, ExternalApiProblemDetailsModel>(
        doFetch ? `/api/open/v1/teamlab/runtimes/${runtimeId}` : null,
        options,
      ),

    /**
     * @description 返回聚合的运行时、分片、网络与资产状态。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesGet
     * @summary 获取运行时
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}
     */
    mutateOpenTeamLabRuntimesGet: (
      runtimeId: string,
      data?: OpenTeamLabRuntimeModel | Promise<OpenTeamLabRuntimeModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabRuntimeModel>(
        `/api/open/v1/teamlab/runtimes/${runtimeId}`,
        data,
        options,
      ),

    /**
     * @description 在原节点上挂起工作负载，同时保留运行时身份、网络、磁盘、地址、访问状态与容量预留。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesPause
     * @summary 暂停运行时
     * @request POST:/api/open/v1/teamlab/runtimes/{runtimeId}/pause
     */
    openTeamLabRuntimesPause: (runtimeId: string, params: RequestParams = {}) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/pause`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 设备/传感器把去敏的协议事件（如点位读写、握手、告警）写入运行时事件流，可用 events?stage=protocol 查询。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesReportProtocolEvent
     * @summary 上报协议事件
     * @request POST:/api/open/v1/teamlab/runtimes/{runtimeId}/protocol-events
     */
    openTeamLabRuntimesReportProtocolEvent: (
      runtimeId: string,
      data: TeamLabProtocolEventReportModel,
      params: RequestParams = {},
    ) =>
      this.request<void, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/protocol-events`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * @description 按运行时的发布版本与可选覆盖配置提交受控清理并重新部署。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesReset
     * @summary 重置运行时
     * @request POST:/api/open/v1/teamlab/runtimes/{runtimeId}/reset
     */
    openTeamLabRuntimesReset: (
      runtimeId: string,
      data: ResetTeamLabRuntimeModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/reset`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 仅在原始分配节点上恢复，不会重新调度或下载镜像；原节点不可用时返回 resume_blocked。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesResume
     * @summary 恢复运行时
     * @request POST:/api/open/v1/teamlab/runtimes/{runtimeId}/resume
     */
    openTeamLabRuntimesResume: (
      runtimeId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/resume`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 提交撤销并清理现有的运行时访问授权。
     *
     * @tags TeamLab - Runtimes
     * @name OpenTeamLabRuntimesRevokeAccessGrant
     * @summary 撤销访问授权
     * @request DELETE:/api/open/v1/teamlab/runtimes/{runtimeId}/access-grants/{grantId}
     */
    openTeamLabRuntimesRevokeAccessGrant: (
      runtimeId: string,
      grantId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/access-grants/${grantId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  teamLabControlScopes = {
    /**
     * @description Archived scopes stay readable and drainable but accept no new writes. Idempotent; only administrator-created tokens may archive.
     *
     * @tags TeamLab - Control scopes
     * @name OpenTeamLabScopesArchive
     * @summary Archive a TeamLab control scope
     * @request POST:/api/open/v1/teamlab/scopes/{scopeId}/archive
     */
    openTeamLabScopesArchive: (scopeId: string, params: RequestParams = {}) =>
      this.request<void, any>({
        path: `/api/open/v1/teamlab/scopes/${scopeId}/archive`,
        method: "POST",
        ...params,
      }),

    /**
     * @description Creates an external resource boundary. Only API tokens created by administrators may create scopes.
     *
     * @tags TeamLab - Control scopes
     * @name OpenTeamLabScopesCreate
     * @summary Create a TeamLab control scope
     * @request POST:/api/open/v1/teamlab/scopes
     */
    openTeamLabScopesCreate: (
      data: CreateTeamLabControlScopeModel,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabControlScopeModel, any>({
        path: `/api/open/v1/teamlab/scopes`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description Lists only scopes granted to the current token, unless the token carries a wildcard teamlab-scope grant.
     *
     * @tags TeamLab - Control scopes
     * @name OpenTeamLabScopesList
     * @summary List TeamLab control scopes
     * @request GET:/api/open/v1/teamlab/scopes
     */
    openTeamLabScopesList: (params: RequestParams = {}) =>
      this.request<TeamLabControlScopeModel[], any>({
        path: `/api/open/v1/teamlab/scopes`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description Lists only scopes granted to the current token, unless the token carries a wildcard teamlab-scope grant.
     *
     * @tags TeamLab - Control scopes
     * @name OpenTeamLabScopesList
     * @summary List TeamLab control scopes
     * @request GET:/api/open/v1/teamlab/scopes
     */
    useOpenTeamLabScopesList: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabControlScopeModel[], any>(
        doFetch ? `/api/open/v1/teamlab/scopes` : null,
        options,
      ),

    /**
     * @description Lists only scopes granted to the current token, unless the token carries a wildcard teamlab-scope grant.
     *
     * @tags TeamLab - Control scopes
     * @name OpenTeamLabScopesList
     * @summary List TeamLab control scopes
     * @request GET:/api/open/v1/teamlab/scopes
     */
    mutateOpenTeamLabScopesList: (
      data?: TeamLabControlScopeModel[] | Promise<TeamLabControlScopeModel[]>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabControlScopeModel[]>(
        `/api/open/v1/teamlab/scopes`,
        data,
        options,
      ),
  };
  teamLabTopologies = {
    /**
     * @description 归档后版本保持可读、既有运行时继续运行，但不能再创建新运行时；重复归档幂等。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesArchiveRelease
     * @summary 归档拓扑版本
     * @request POST:/api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}/archive
     */
    openTeamLabTopologiesArchiveRelease: (
      topologyId: string,
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<void, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/topologies/${topologyId}/releases/${releaseId}/archive`,
        method: "POST",
        ...params,
      }),

    /**
     * @description 返回本平台版本支持的拓扑 schema 与功能能力。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesCapabilities
     * @summary 获取 TeamLab 能力
     * @request GET:/api/open/v1/teamlab/capabilities
     */
    openTeamLabTopologiesCapabilities: (params: RequestParams = {}) =>
      this.request<TeamLabCapabilitiesModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/capabilities`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回本平台版本支持的拓扑 schema 与功能能力。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesCapabilities
     * @summary 获取 TeamLab 能力
     * @request GET:/api/open/v1/teamlab/capabilities
     */
    useOpenTeamLabTopologiesCapabilities: (
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabCapabilitiesModel, ExternalApiProblemDetailsModel>(
        doFetch ? `/api/open/v1/teamlab/capabilities` : null,
        options,
      ),

    /**
     * @description 返回本平台版本支持的拓扑 schema 与功能能力。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesCapabilities
     * @summary 获取 TeamLab 能力
     * @request GET:/api/open/v1/teamlab/capabilities
     */
    mutateOpenTeamLabTopologiesCapabilities: (
      data?: TeamLabCapabilitiesModel | Promise<TeamLabCapabilitiesModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabCapabilitiesModel>(
        `/api/open/v1/teamlab/capabilities`,
        data,
        options,
      ),

    /**
     * @description 把已有拓扑复制为调用者名下的新草稿，并重新校验镜像、设备包与连接器引用。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesClone
     * @summary 克隆拓扑
     * @request POST:/api/open/v1/teamlab/topologies/{topologyId}/clone
     */
    openTeamLabTopologiesClone: (
      topologyId: string,
      params: RequestParams = {},
    ) =>
      this.request<
        OpenTeamLabTopologyDetailModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/topologies/${topologyId}/clone`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 提交创建可复用的 TeamLab 拓扑草稿。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesCreate
     * @summary 创建拓扑
     * @request POST:/api/open/v1/teamlab/topologies
     */
    openTeamLabTopologiesCreate: (
      data: OpenCreateTeamLabTopologyModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/topologies`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 提交删除不再使用的拓扑。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesDelete
     * @summary 删除拓扑
     * @request DELETE:/api/open/v1/teamlab/topologies/{topologyId}
     */
    openTeamLabTopologiesDelete: (
      topologyId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/topologies/${topologyId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * @description 返回完整的可编辑拓扑定义。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesGet
     * @summary 获取拓扑
     * @request GET:/api/open/v1/teamlab/topologies/{topologyId}
     */
    openTeamLabTopologiesGet: (
      topologyId: string,
      params: RequestParams = {},
    ) =>
      this.request<
        OpenTeamLabTopologyDetailModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/topologies/${topologyId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回完整的可编辑拓扑定义。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesGet
     * @summary 获取拓扑
     * @request GET:/api/open/v1/teamlab/topologies/{topologyId}
     */
    useOpenTeamLabTopologiesGet: (
      topologyId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenTeamLabTopologyDetailModel, ExternalApiProblemDetailsModel>(
        doFetch ? `/api/open/v1/teamlab/topologies/${topologyId}` : null,
        options,
      ),

    /**
     * @description 返回完整的可编辑拓扑定义。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesGet
     * @summary 获取拓扑
     * @request GET:/api/open/v1/teamlab/topologies/{topologyId}
     */
    mutateOpenTeamLabTopologiesGet: (
      topologyId: string,
      data?:
        | OpenTeamLabTopologyDetailModel
        | Promise<OpenTeamLabTopologyDetailModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabTopologyDetailModel>(
        `/api/open/v1/teamlab/topologies/${topologyId}`,
        data,
        options,
      ),

    /**
     * @description 返回一个不可变拓扑版本及其编译后的定义。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesGetRelease
     * @summary 获取拓扑版本
     * @request GET:/api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}
     */
    openTeamLabTopologiesGetRelease: (
      topologyId: string,
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<OpenTeamLabReleaseModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/topologies/${topologyId}/releases/${releaseId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回一个不可变拓扑版本及其编译后的定义。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesGetRelease
     * @summary 获取拓扑版本
     * @request GET:/api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}
     */
    useOpenTeamLabTopologiesGetRelease: (
      topologyId: string,
      releaseId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenTeamLabReleaseModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? `/api/open/v1/teamlab/topologies/${topologyId}/releases/${releaseId}`
          : null,
        options,
      ),

    /**
     * @description 返回一个不可变拓扑版本及其编译后的定义。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesGetRelease
     * @summary 获取拓扑版本
     * @request GET:/api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}
     */
    mutateOpenTeamLabTopologiesGetRelease: (
      topologyId: string,
      releaseId: string,
      data?: OpenTeamLabReleaseModel | Promise<OpenTeamLabReleaseModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabReleaseModel>(
        `/api/open/v1/teamlab/topologies/${topologyId}/releases/${releaseId}`,
        data,
        options,
      ),

    /**
     * @description 返回当前 API token 属主可见的 cursor 分页拓扑列表。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesList
     * @summary 列出拓扑
     * @request GET:/api/open/v1/teamlab/topologies
     */
    openTeamLabTopologiesList: (
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<
        OpenTeamLabTopologyPageModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/topologies`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 返回当前 API token 属主可见的 cursor 分页拓扑列表。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesList
     * @summary 列出拓扑
     * @request GET:/api/open/v1/teamlab/topologies
     */
    useOpenTeamLabTopologiesList: (
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenTeamLabTopologyPageModel, ExternalApiProblemDetailsModel>(
        doFetch ? [`/api/open/v1/teamlab/topologies`, query] : null,
        options,
      ),

    /**
     * @description 返回当前 API token 属主可见的 cursor 分页拓扑列表。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesList
     * @summary 列出拓扑
     * @request GET:/api/open/v1/teamlab/topologies
     */
    mutateOpenTeamLabTopologiesList: (
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?:
        | OpenTeamLabTopologyPageModel
        | Promise<OpenTeamLabTopologyPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabTopologyPageModel>(
        [`/api/open/v1/teamlab/topologies`, query],
        data,
        options,
      ),

    /**
     * @description 使用 cursor 分页返回拓扑的不可变版本列表。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesListReleases
     * @summary 列出拓扑版本
     * @request GET:/api/open/v1/teamlab/topologies/{topologyId}/releases
     */
    openTeamLabTopologiesListReleases: (
      topologyId: string,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<OpenTeamLabReleasePageModel, ExternalApiProblemDetailsModel>(
        {
          path: `/api/open/v1/teamlab/topologies/${topologyId}/releases`,
          method: "GET",
          query: query,
          format: "json",
          ...params,
        },
      ),
    /**
     * @description 使用 cursor 分页返回拓扑的不可变版本列表。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesListReleases
     * @summary 列出拓扑版本
     * @request GET:/api/open/v1/teamlab/topologies/{topologyId}/releases
     */
    useOpenTeamLabTopologiesListReleases: (
      topologyId: string,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenTeamLabReleasePageModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? [`/api/open/v1/teamlab/topologies/${topologyId}/releases`, query]
          : null,
        options,
      ),

    /**
     * @description 使用 cursor 分页返回拓扑的不可变版本列表。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesListReleases
     * @summary 列出拓扑版本
     * @request GET:/api/open/v1/teamlab/topologies/{topologyId}/releases
     */
    mutateOpenTeamLabTopologiesListReleases: (
      topologyId: string,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: OpenTeamLabReleasePageModel | Promise<OpenTeamLabReleasePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabReleasePageModel>(
        [`/api/open/v1/teamlab/topologies/${topologyId}/releases`, query],
        data,
        options,
      ),

    /**
     * @description 在不创建运行时资源的情况下为版本构建部署计划。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesPlan
     * @summary 规划运行时部署
     * @request POST:/api/open/v1/teamlab/topologies/{topologyId}/releases/{releaseId}/plan
     */
    openTeamLabTopologiesPlan: (
      topologyId: string,
      releaseId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabPlanModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/topologies/${topologyId}/releases/${releaseId}/plan`,
        method: "POST",
        format: "json",
        ...params,
      }),

    /**
     * @description 校验并提交创建用于运行时部署的不可变拓扑版本。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesPublish
     * @summary 发布拓扑版本
     * @request POST:/api/open/v1/teamlab/topologies/{topologyId}/releases
     */
    openTeamLabTopologiesPublish: (
      topologyId: string,
      data: PublishTeamLabTopologyModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/topologies/${topologyId}/releases`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 提交替换可编辑的拓扑定义。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesUpdate
     * @summary 更新拓扑
     * @request PUT:/api/open/v1/teamlab/topologies/{topologyId}
     */
    openTeamLabTopologiesUpdate: (
      topologyId: string,
      data: OpenUpdateTeamLabTopologyModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/topologies/${topologyId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 在不发布的情况下校验拓扑结构、寻址、连通性、资产与部署约束。
     *
     * @tags TeamLab - Topologies
     * @name OpenTeamLabTopologiesValidate
     * @summary 校验拓扑
     * @request POST:/api/open/v1/teamlab/topologies/{topologyId}/validate
     */
    openTeamLabTopologiesValidate: (
      topologyId: string,
      params: RequestParams = {},
    ) =>
      this.request<
        TeamLabValidationResultModel,
        ExternalApiProblemDetailsModel
      >({
        path: `/api/open/v1/teamlab/topologies/${topologyId}/validate`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
  teamLabTrafficAndCaptures = {
    /**
     * @description 流式返回已完成的运行时抓包归档文件。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficDownloadCapture
     * @summary 下载抓包文件
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}/download
     */
    openTeamLabTrafficDownloadCapture: (
      runtimeId: string,
      captureId: string,
      params: RequestParams = {},
    ) =>
      this.request<void, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/captures/${captureId}/download`,
        method: "GET",
        ...params,
      }),

    /**
     * @description 返回抓包范围、限额、进度、产物状态与保留元数据。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetCapture
     * @summary 获取抓包状态
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}
     */
    openTeamLabTrafficGetCapture: (
      runtimeId: string,
      captureId: string,
      params: RequestParams = {},
    ) =>
      this.request<OpenTeamLabCaptureModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/captures/${captureId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回抓包范围、限额、进度、产物状态与保留元数据。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetCapture
     * @summary 获取抓包状态
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}
     */
    useOpenTeamLabTrafficGetCapture: (
      runtimeId: string,
      captureId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenTeamLabCaptureModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? `/api/open/v1/teamlab/runtimes/${runtimeId}/captures/${captureId}`
          : null,
        options,
      ),

    /**
     * @description 返回抓包范围、限额、进度、产物状态与保留元数据。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetCapture
     * @summary 获取抓包状态
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}
     */
    mutateOpenTeamLabTrafficGetCapture: (
      runtimeId: string,
      captureId: string,
      data?: OpenTeamLabCaptureModel | Promise<OpenTeamLabCaptureModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabCaptureModel>(
        `/api/open/v1/teamlab/runtimes/${runtimeId}/captures/${captureId}`,
        data,
        options,
      ),

    /**
     * @description 返回由 TeamLab 数据平面采集的 cursor 分页、运行时范围的流量元数据。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetFlows
     * @summary 列出流量记录
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/traffic/flows
     */
    openTeamLabTrafficGetFlows: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        networkKey?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 65535
         */
        port?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabTrafficFlowPageModel, ExternalApiProblemDetailsModel>(
        {
          path: `/api/open/v1/teamlab/runtimes/${runtimeId}/traffic/flows`,
          method: "GET",
          query: query,
          format: "json",
          ...params,
        },
      ),
    /**
     * @description 返回由 TeamLab 数据平面采集的 cursor 分页、运行时范围的流量元数据。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetFlows
     * @summary 列出流量记录
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/traffic/flows
     */
    useOpenTeamLabTrafficGetFlows: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        networkKey?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 65535
         */
        port?: number | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabTrafficFlowPageModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? [`/api/open/v1/teamlab/runtimes/${runtimeId}/traffic/flows`, query]
          : null,
        options,
      ),

    /**
     * @description 返回由 TeamLab 数据平面采集的 cursor 分页、运行时范围的流量元数据。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetFlows
     * @summary 列出流量记录
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/traffic/flows
     */
    mutateOpenTeamLabTrafficGetFlows: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        networkKey?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 65535
         */
        port?: number | null;
      },
      data?: TeamLabTrafficFlowPageModel | Promise<TeamLabTrafficFlowPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabTrafficFlowPageModel>(
        [`/api/open/v1/teamlab/runtimes/${runtimeId}/traffic/flows`, query],
        data,
        options,
      ),

    /**
     * @description 返回一条关联流量路径的有序跳点与证据。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetPath
     * @summary 获取流量路径
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}
     */
    openTeamLabTrafficGetPath: (
      runtimeId: string,
      pathId: string,
      params: RequestParams = {},
    ) =>
      this.request<TeamLabTrafficPathModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/traffic/paths/${pathId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回一条关联流量路径的有序跳点与证据。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetPath
     * @summary 获取流量路径
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}
     */
    useOpenTeamLabTrafficGetPath: (
      runtimeId: string,
      pathId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabTrafficPathModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? `/api/open/v1/teamlab/runtimes/${runtimeId}/traffic/paths/${pathId}`
          : null,
        options,
      ),

    /**
     * @description 返回一条关联流量路径的有序跳点与证据。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetPath
     * @summary 获取流量路径
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}
     */
    mutateOpenTeamLabTrafficGetPath: (
      runtimeId: string,
      pathId: string,
      data?: TeamLabTrafficPathModel | Promise<TeamLabTrafficPathModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabTrafficPathModel>(
        `/api/open/v1/teamlab/runtimes/${runtimeId}/traffic/paths/${pathId}`,
        data,
        options,
      ),

    /**
     * @description 返回跨参与资产与网段的端到端流量路径关联。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetPaths
     * @summary 列出关联流量路径
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths
     */
    openTeamLabTrafficGetPaths: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        confidence?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabTrafficPathPageModel, ExternalApiProblemDetailsModel>(
        {
          path: `/api/open/v1/teamlab/runtimes/${runtimeId}/traffic/paths`,
          method: "GET",
          query: query,
          format: "json",
          ...params,
        },
      ),
    /**
     * @description 返回跨参与资产与网段的端到端流量路径关联。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetPaths
     * @summary 列出关联流量路径
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths
     */
    useOpenTeamLabTrafficGetPaths: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        confidence?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabTrafficPathPageModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? [`/api/open/v1/teamlab/runtimes/${runtimeId}/traffic/paths`, query]
          : null,
        options,
      ),

    /**
     * @description 返回跨参与资产与网段的端到端流量路径关联。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficGetPaths
     * @summary 列出关联流量路径
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths
     */
    mutateOpenTeamLabTrafficGetPaths: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        query?: string | null;
        protocol?: string | null;
        confidence?: string | null;
      },
      data?: TeamLabTrafficPathPageModel | Promise<TeamLabTrafficPathPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabTrafficPathPageModel>(
        [`/api/open/v1/teamlab/runtimes/${runtimeId}/traffic/paths`, query],
        data,
        options,
      ),

    /**
     * @description 按创建时间倒序返回该运行时的抓包任务，使用稳定 cursor 分页。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficListCaptures
     * @summary 列出抓包任务
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/captures
     */
    openTeamLabTrafficListCaptures: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<OpenTeamLabCapturePageModel, ExternalApiProblemDetailsModel>(
        {
          path: `/api/open/v1/teamlab/runtimes/${runtimeId}/captures`,
          method: "GET",
          query: query,
          format: "json",
          ...params,
        },
      ),
    /**
     * @description 按创建时间倒序返回该运行时的抓包任务，使用稳定 cursor 分页。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficListCaptures
     * @summary 列出抓包任务
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/captures
     */
    useOpenTeamLabTrafficListCaptures: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenTeamLabCapturePageModel, ExternalApiProblemDetailsModel>(
        doFetch
          ? [`/api/open/v1/teamlab/runtimes/${runtimeId}/captures`, query]
          : null,
        options,
      ),

    /**
     * @description 按创建时间倒序返回该运行时的抓包任务，使用稳定 cursor 分页。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficListCaptures
     * @summary 列出抓包任务
     * @request GET:/api/open/v1/teamlab/runtimes/{runtimeId}/captures
     */
    mutateOpenTeamLabTrafficListCaptures: (
      runtimeId: string,
      query?: {
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
      },
      data?: OpenTeamLabCapturePageModel | Promise<OpenTeamLabCapturePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenTeamLabCapturePageModel>(
        [`/api/open/v1/teamlab/runtimes/${runtimeId}/captures`, query],
        data,
        options,
      ),

    /**
     * @description 为选定的运行时分片或网段提交有上限的抓包任务。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficStartCapture
     * @summary 开始抓包
     * @request POST:/api/open/v1/teamlab/runtimes/{runtimeId}/captures
     */
    openTeamLabTrafficStartCapture: (
      runtimeId: string,
      data: CreateTeamLabCaptureModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/captures`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 提交提前停止并归档正在运行的抓包任务。
     *
     * @tags TeamLab - Traffic and Captures
     * @name OpenTeamLabTrafficStopCapture
     * @summary 停止抓包
     * @request POST:/api/open/v1/teamlab/runtimes/{runtimeId}/captures/{captureId}/stop
     */
    openTeamLabTrafficStopCapture: (
      runtimeId: string,
      captureId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/runtimes/${runtimeId}/captures/${captureId}/stop`,
        method: "POST",
        format: "json",
        ...params,
      }),
  };
  teamLabWebhooks = {
    /**
     * @description 在指定控制范围内创建 https 端点的事件通知订阅；端点必须可公网解析。
     *
     * @tags TeamLab - Webhooks
     * @name OpenTeamLabWebhooksCreate
     * @summary 创建 webhook 订阅
     * @request POST:/api/open/v1/teamlab/webhooks
     */
    openTeamLabWebhooksCreate: (
      data: CreateTeamLabWebhookModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/webhooks`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * @description 返回订阅详情与最近投递失败记录；签名密钥永不返回。
     *
     * @tags TeamLab - Webhooks
     * @name OpenTeamLabWebhooksGet
     * @summary 获取 webhook 订阅
     * @request GET:/api/open/v1/teamlab/webhooks/{webhookId}
     */
    openTeamLabWebhooksGet: (webhookId: string, params: RequestParams = {}) =>
      this.request<TeamLabWebhookModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/webhooks/${webhookId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * @description 返回订阅详情与最近投递失败记录；签名密钥永不返回。
     *
     * @tags TeamLab - Webhooks
     * @name OpenTeamLabWebhooksGet
     * @summary 获取 webhook 订阅
     * @request GET:/api/open/v1/teamlab/webhooks/{webhookId}
     */
    useOpenTeamLabWebhooksGet: (
      webhookId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabWebhookModel, ExternalApiProblemDetailsModel>(
        doFetch ? `/api/open/v1/teamlab/webhooks/${webhookId}` : null,
        options,
      ),

    /**
     * @description 返回订阅详情与最近投递失败记录；签名密钥永不返回。
     *
     * @tags TeamLab - Webhooks
     * @name OpenTeamLabWebhooksGet
     * @summary 获取 webhook 订阅
     * @request GET:/api/open/v1/teamlab/webhooks/{webhookId}
     */
    mutateOpenTeamLabWebhooksGet: (
      webhookId: string,
      data?: TeamLabWebhookModel | Promise<TeamLabWebhookModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabWebhookModel>(
        `/api/open/v1/teamlab/webhooks/${webhookId}`,
        data,
        options,
      ),

    /**
     * @description 按控制范围返回 cursor 分页的订阅列表。
     *
     * @tags TeamLab - Webhooks
     * @name OpenTeamLabWebhooksList
     * @summary 列出 webhook 订阅
     * @request GET:/api/open/v1/teamlab/webhooks
     */
    openTeamLabWebhooksList: (
      query?: {
        /** @format guid */
        scopeId?: string;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<TeamLabWebhookPageModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/webhooks`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * @description 按控制范围返回 cursor 分页的订阅列表。
     *
     * @tags TeamLab - Webhooks
     * @name OpenTeamLabWebhooksList
     * @summary 列出 webhook 订阅
     * @request GET:/api/open/v1/teamlab/webhooks
     */
    useOpenTeamLabWebhooksList: (
      query?: {
        /** @format guid */
        scopeId?: string;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<TeamLabWebhookPageModel, ExternalApiProblemDetailsModel>(
        doFetch ? [`/api/open/v1/teamlab/webhooks`, query] : null,
        options,
      ),

    /**
     * @description 按控制范围返回 cursor 分页的订阅列表。
     *
     * @tags TeamLab - Webhooks
     * @name OpenTeamLabWebhooksList
     * @summary 列出 webhook 订阅
     * @request GET:/api/open/v1/teamlab/webhooks
     */
    mutateOpenTeamLabWebhooksList: (
      query?: {
        /** @format guid */
        scopeId?: string;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: TeamLabWebhookPageModel | Promise<TeamLabWebhookPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<TeamLabWebhookPageModel>(
        [`/api/open/v1/teamlab/webhooks`, query],
        data,
        options,
      ),

    /**
     * @description 从指定事件 ID 重新投递不可变信封；不推进投递游标，也不会创建新的运行时操作。
     *
     * @tags TeamLab - Webhooks
     * @name OpenTeamLabWebhooksReplay
     * @summary 重放 webhook 事件
     * @request POST:/api/open/v1/teamlab/webhooks/{webhookId}/replay
     */
    openTeamLabWebhooksReplay: (
      webhookId: string,
      query?: {
        /**
         * @format int64
         * @min 1
         * @max 9223372036854780000
         */
        fromEventId?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/webhooks/${webhookId}/replay`,
        method: "POST",
        query: query,
        format: "json",
        ...params,
      }),

    /**
     * @description 停止后续投递；已排队投递不会回滚。
     *
     * @tags TeamLab - Webhooks
     * @name OpenTeamLabWebhooksRevoke
     * @summary 撤销 webhook 订阅
     * @request DELETE:/api/open/v1/teamlab/webhooks/{webhookId}
     */
    openTeamLabWebhooksRevoke: (
      webhookId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ExternalApiProblemDetailsModel>({
        path: `/api/open/v1/teamlab/webhooks/${webhookId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),
  };
  teamsOpenApi = {
    /**
     * No description
     *
     * @tags TeamsOpenApi
     * @name TeamsOpenApiImport
     * @request POST:/api/open/v1/teams/import
     */
    teamsOpenApiImport: (
      data: TeamImportBatchModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/teams/import`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  theoryOpenApi = {
    /**
     * No description
     *
     * @tags TheoryOpenApi
     * @name TheoryOpenApiImportPaper
     * @request PUT:/api/open/v1/theory/games/{gameId}/paper
     */
    theoryOpenApiImportPaper: (
      gameId: number,
      data: TheoryPaperImportModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/theory/games/${gameId}/paper`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags TheoryOpenApi
     * @name TheoryOpenApiImportQuestions
     * @request POST:/api/open/v1/theory/questions/import
     */
    theoryOpenApiImportQuestions: (
      data: TheoryQuestionImportBatchModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/theory/questions/import`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  trainingOpenApi = {
    /**
     * No description
     *
     * @tags TrainingOpenApi
     * @name TrainingOpenApiImport
     * @request POST:/api/open/v1/training/courses/import
     */
    trainingOpenApiImport: (
      data: TrainingCourseImportBatchModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/training/courses/import`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  exerciseOpenApi = {
    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiCreate
     * @request POST:/api/open/v1/exercises
     */
    exerciseOpenApiCreate: (
      data: ExerciseCreateModel2,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ProblemDetails>({
        path: `/api/open/v1/exercises`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiDelete
     * @request DELETE:/api/open/v1/exercises/{exerciseId}
     */
    exerciseOpenApiDelete: (exerciseId: number, params: RequestParams = {}) =>
      this.request<void, ProblemDetails>({
        path: `/api/open/v1/exercises/${exerciseId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiGet
     * @request GET:/api/open/v1/exercises/{exerciseId}
     */
    exerciseOpenApiGet: (exerciseId: number, params: RequestParams = {}) =>
      this.request<ExerciseExternalModel, ProblemDetails>({
        path: `/api/open/v1/exercises/${exerciseId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiGet
     * @request GET:/api/open/v1/exercises/{exerciseId}
     */
    useExerciseOpenApiGet: (
      exerciseId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ExerciseExternalModel, ProblemDetails>(
        doFetch ? `/api/open/v1/exercises/${exerciseId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiGet
     * @request GET:/api/open/v1/exercises/{exerciseId}
     */
    mutateExerciseOpenApiGet: (
      exerciseId: number,
      data?: ExerciseExternalModel | Promise<ExerciseExternalModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ExerciseExternalModel>(
        `/api/open/v1/exercises/${exerciseId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiImport
     * @request POST:/api/open/v1/exercises/import
     */
    exerciseOpenApiImport: (
      data: ExerciseImportFromExternalModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/exercises/import`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiList
     * @request GET:/api/open/v1/exercises
     */
    exerciseOpenApiList: (
      query?: {
        search?: string | null;
        category?: string | null;
        difficulty?: string | null;
        tags?: string | null;
        source?: string | null;
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
      },
      params: RequestParams = {},
    ) =>
      this.request<ExerciseExternalPageModel, any>({
        path: `/api/open/v1/exercises`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiList
     * @request GET:/api/open/v1/exercises
     */
    useExerciseOpenApiList: (
      query?: {
        search?: string | null;
        category?: string | null;
        difficulty?: string | null;
        tags?: string | null;
        source?: string | null;
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<ExerciseExternalPageModel, any>(
        doFetch ? [`/api/open/v1/exercises`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiList
     * @request GET:/api/open/v1/exercises
     */
    mutateExerciseOpenApiList: (
      query?: {
        search?: string | null;
        category?: string | null;
        difficulty?: string | null;
        tags?: string | null;
        source?: string | null;
        after?: string | null;
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
      },
      data?: ExerciseExternalPageModel | Promise<ExerciseExternalPageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<ExerciseExternalPageModel>(
        [`/api/open/v1/exercises`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags ExerciseOpenApi
     * @name ExerciseOpenApiUpdate
     * @request PUT:/api/open/v1/exercises/{exerciseId}
     */
    exerciseOpenApiUpdate: (
      exerciseId: number,
      data: ExerciseCreateModel2,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, ProblemDetails>({
        path: `/api/open/v1/exercises/${exerciseId}`,
        method: "PUT",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),
  };
  openAwdpServices = {
    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesDelete
     * @request DELETE:/api/open/v1/games/{gameId}/awdp-services/{serviceId}
     */
    openAwdpServicesDelete: (
      gameId: number,
      serviceId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/open/v1/games/${gameId}/awdp-services/${serviceId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesDeleteBatch
     * @request POST:/api/open/v1/games/{gameId}/awdp-services/batch-delete
     */
    openAwdpServicesDeleteBatch: (
      gameId: number,
      data: number[],
      query?: {
        routeKey?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/open/v1/games/${gameId}/awdp-services/batch-delete`,
        method: "POST",
        query: query,
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesGet
     * @request GET:/api/open/v1/games/{gameId}/awdp-services/{serviceId}
     */
    openAwdpServicesGet: (
      gameId: number,
      serviceId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/open/v1/games/${gameId}/awdp-services/${serviceId}`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesGet
     * @request GET:/api/open/v1/games/{gameId}/awdp-services/{serviceId}
     */
    useOpenAwdpServicesGet: (
      gameId: number,
      serviceId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/open/v1/games/${gameId}/awdp-services/${serviceId}`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesGet
     * @request GET:/api/open/v1/games/{gameId}/awdp-services/{serviceId}
     */
    mutateOpenAwdpServicesGet: (
      gameId: number,
      serviceId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/open/v1/games/${gameId}/awdp-services/${serviceId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesImportBatch
     * @request POST:/api/open/v1/games/{gameId}/awdp-services/batch
     */
    openAwdpServicesImportBatch: (
      gameId: number,
      data: OpenAwdpServiceBatchImportModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/open/v1/games/${gameId}/awdp-services/batch`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesImportOne
     * @request POST:/api/open/v1/games/{gameId}/awdp-services
     */
    openAwdpServicesImportOne: (
      gameId: number,
      data: OpenAwdpServiceImportModel,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/open/v1/games/${gameId}/awdp-services`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesList
     * @request GET:/api/open/v1/games/{gameId}/awdp-services
     */
    openAwdpServicesList: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        /** @format int32 */
        after?: number | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/open/v1/games/${gameId}/awdp-services`,
        method: "GET",
        query: query,
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesList
     * @request GET:/api/open/v1/games/{gameId}/awdp-services
     */
    useOpenAwdpServicesList: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        /** @format int32 */
        after?: number | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? [`/api/open/v1/games/${gameId}/awdp-services`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenAwdpServices
     * @name OpenAwdpServicesList
     * @request GET:/api/open/v1/games/{gameId}/awdp-services
     */
    mutateOpenAwdpServicesList: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        /** @format int32 */
        after?: number | null;
      },
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        [`/api/open/v1/games/${gameId}/awdp-services`, query],
        data,
        options,
      ),
  };
  openChallenges = {
    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesDelete
     * @request DELETE:/api/open/v1/games/{gameId}/challenges/{challengeId}
     */
    openChallengesDelete: (
      gameId: number,
      challengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/games/${gameId}/challenges/${challengeId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesDeleteBatch
     * @request POST:/api/open/v1/games/{gameId}/challenges/batch-delete
     */
    openChallengesDeleteBatch: (
      gameId: number,
      data: OpenChallengeBatchDeleteModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/games/${gameId}/challenges/batch-delete`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesGet
     * @request GET:/api/open/v1/games/{gameId}/challenges/{challengeId}
     */
    openChallengesGet: (
      gameId: number,
      challengeId: number,
      params: RequestParams = {},
    ) =>
      this.request<OpenChallengeModel, ProblemDetails>({
        path: `/api/open/v1/games/${gameId}/challenges/${challengeId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesGet
     * @request GET:/api/open/v1/games/{gameId}/challenges/{challengeId}
     */
    useOpenChallengesGet: (
      gameId: number,
      challengeId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenChallengeModel, ProblemDetails>(
        doFetch
          ? `/api/open/v1/games/${gameId}/challenges/${challengeId}`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesGet
     * @request GET:/api/open/v1/games/{gameId}/challenges/{challengeId}
     */
    mutateOpenChallengesGet: (
      gameId: number,
      challengeId: number,
      data?: OpenChallengeModel | Promise<OpenChallengeModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenChallengeModel>(
        `/api/open/v1/games/${gameId}/challenges/${challengeId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesImportBatch
     * @request POST:/api/open/v1/games/{gameId}/challenges/batch
     */
    openChallengesImportBatch: (
      gameId: number,
      data: OpenChallengeBatchImportModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/games/${gameId}/challenges/batch`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesImportOne
     * @request POST:/api/open/v1/games/{gameId}/challenges
     */
    openChallengesImportOne: (
      gameId: number,
      data: OpenChallengeImportModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/games/${gameId}/challenges`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesList
     * @request GET:/api/open/v1/games/{gameId}/challenges
     */
    openChallengesList: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<OpenChallengePageModel, any>({
        path: `/api/open/v1/games/${gameId}/challenges`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesList
     * @request GET:/api/open/v1/games/{gameId}/challenges
     */
    useOpenChallengesList: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenChallengePageModel, any>(
        doFetch ? [`/api/open/v1/games/${gameId}/challenges`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenChallenges
     * @name OpenChallengesList
     * @request GET:/api/open/v1/games/{gameId}/challenges
     */
    mutateOpenChallengesList: (
      gameId: number,
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: OpenChallengePageModel | Promise<OpenChallengePageModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenChallengePageModel>(
        [`/api/open/v1/games/${gameId}/challenges`, query],
        data,
        options,
      ),
  };
  openAssets = {
    /**
     * No description
     *
     * @tags OpenAssets
     * @name OpenAssetsGet
     * @request GET:/api/open/v1/assets/{hash}
     */
    openAssetsGet: (hash: string, params: RequestParams = {}) =>
      this.request<AssetDescriptor, ProblemDetails>({
        path: `/api/open/v1/assets/${hash}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenAssets
     * @name OpenAssetsGet
     * @request GET:/api/open/v1/assets/{hash}
     */
    useOpenAssetsGet: (
      hash: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<AssetDescriptor, ProblemDetails>(
        doFetch ? `/api/open/v1/assets/${hash}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenAssets
     * @name OpenAssetsGet
     * @request GET:/api/open/v1/assets/{hash}
     */
    mutateOpenAssetsGet: (
      hash: string,
      data?: AssetDescriptor | Promise<AssetDescriptor>,
      options?: MutatorOptions,
    ) => mutate<AssetDescriptor>(`/api/open/v1/assets/${hash}`, data, options),

    /**
     * No description
     *
     * @tags OpenAssets
     * @name OpenAssetsUpload
     * @request POST:/api/open/v1/assets
     */
    openAssetsUpload: (
      data: {
        /** @format binary */
        file?: File | null;
        filename?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<AssetDescriptor, any>({
        path: `/api/open/v1/assets`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),
  };
  openBootstrapProfiles = {
    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesCreate
     * @request POST:/api/open/v1/bootstrap-profiles
     */
    openBootstrapProfilesCreate: (
      data: BootstrapProfileCreateModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/bootstrap-profiles`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesDelete
     * @request DELETE:/api/open/v1/bootstrap-profiles/{profileId}
     */
    openBootstrapProfilesDelete: (
      profileId: string,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/bootstrap-profiles/${profileId}`,
        method: "DELETE",
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesGet
     * @request GET:/api/open/v1/bootstrap-profiles/{profileId}
     */
    openBootstrapProfilesGet: (profileId: string, params: RequestParams = {}) =>
      this.request<Blob, any>({
        path: `/api/open/v1/bootstrap-profiles/${profileId}`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesGet
     * @request GET:/api/open/v1/bootstrap-profiles/{profileId}
     */
    useOpenBootstrapProfilesGet: (
      profileId: string,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch ? `/api/open/v1/bootstrap-profiles/${profileId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesGet
     * @request GET:/api/open/v1/bootstrap-profiles/{profileId}
     */
    mutateOpenBootstrapProfilesGet: (
      profileId: string,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/open/v1/bootstrap-profiles/${profileId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesGetVersion
     * @request GET:/api/open/v1/bootstrap-profiles/{profileId}/versions/{version}
     */
    openBootstrapProfilesGetVersion: (
      profileId: string,
      version: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/open/v1/bootstrap-profiles/${profileId}/versions/${version}`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesGetVersion
     * @request GET:/api/open/v1/bootstrap-profiles/{profileId}/versions/{version}
     */
    useOpenBootstrapProfilesGetVersion: (
      profileId: string,
      version: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/open/v1/bootstrap-profiles/${profileId}/versions/${version}`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesGetVersion
     * @request GET:/api/open/v1/bootstrap-profiles/{profileId}/versions/{version}
     */
    mutateOpenBootstrapProfilesGetVersion: (
      profileId: string,
      version: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/open/v1/bootstrap-profiles/${profileId}/versions/${version}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesList
     * @request GET:/api/open/v1/bootstrap-profiles
     */
    openBootstrapProfilesList: (
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<BootstrapProfileCursorPage, any>({
        path: `/api/open/v1/bootstrap-profiles`,
        method: "GET",
        query: query,
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesList
     * @request GET:/api/open/v1/bootstrap-profiles
     */
    useOpenBootstrapProfilesList: (
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<BootstrapProfileCursorPage, any>(
        doFetch ? [`/api/open/v1/bootstrap-profiles`, query] : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesList
     * @request GET:/api/open/v1/bootstrap-profiles
     */
    mutateOpenBootstrapProfilesList: (
      query?: {
        /**
         * @format int32
         * @min 1
         * @max 100
         * @default 50
         */
        limit?: number;
        after?: string | null;
      },
      data?: BootstrapProfileCursorPage | Promise<BootstrapProfileCursorPage>,
      options?: MutatorOptions,
    ) =>
      mutate<BootstrapProfileCursorPage>(
        [`/api/open/v1/bootstrap-profiles`, query],
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenBootstrapProfiles
     * @name OpenBootstrapProfilesPublishVersion
     * @request POST:/api/open/v1/bootstrap-profiles/{profileId}/versions
     */
    openBootstrapProfilesPublishVersion: (
      profileId: string,
      data: {
        /** @format binary */
        artifact?: File | null;
        manifest?: string | null;
        /** @format int32 */
        version?: number | null;
        expectedDigest?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/bootstrap-profiles/${profileId}/versions`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),
  };
  openImages = {
    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesCertifications
     * @request GET:/api/open/v1/images/{imageTemplateId}/certifications
     */
    openImagesCertifications: (
      imageTemplateId: number,
      params: RequestParams = {},
    ) =>
      this.request<Blob, any>({
        path: `/api/open/v1/images/${imageTemplateId}/certifications`,
        method: "GET",
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesCertifications
     * @request GET:/api/open/v1/images/{imageTemplateId}/certifications
     */
    useOpenImagesCertifications: (
      imageTemplateId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<Blob, any>(
        doFetch
          ? `/api/open/v1/images/${imageTemplateId}/certifications`
          : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesCertifications
     * @request GET:/api/open/v1/images/{imageTemplateId}/certifications
     */
    mutateOpenImagesCertifications: (
      imageTemplateId: number,
      data?: Blob | Promise<Blob>,
      options?: MutatorOptions,
    ) =>
      mutate<Blob>(
        `/api/open/v1/images/${imageTemplateId}/certifications`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesCertify
     * @request POST:/api/open/v1/images/{imageTemplateId}/certifications
     */
    openImagesCertify: (
      imageTemplateId: number,
      data: ImageTemplateCertificationRequest,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/images/${imageTemplateId}/certifications`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesDelete
     * @request DELETE:/api/open/v1/images/{imageTemplateId}
     */
    openImagesDelete: (imageTemplateId: number, params: RequestParams = {}) =>
      this.request<void, ProblemDetails>({
        path: `/api/open/v1/images/${imageTemplateId}`,
        method: "DELETE",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesGet
     * @request GET:/api/open/v1/images/{imageTemplateId}
     */
    openImagesGet: (imageTemplateId: number, params: RequestParams = {}) =>
      this.request<OpenImageTemplateModel, ProblemDetails>({
        path: `/api/open/v1/images/${imageTemplateId}`,
        method: "GET",
        format: "json",
        ...params,
      }),
    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesGet
     * @request GET:/api/open/v1/images/{imageTemplateId}
     */
    useOpenImagesGet: (
      imageTemplateId: number,
      options?: SWRConfiguration,
      doFetch: boolean = true,
    ) =>
      useSWR<OpenImageTemplateModel, ProblemDetails>(
        doFetch ? `/api/open/v1/images/${imageTemplateId}` : null,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesGet
     * @request GET:/api/open/v1/images/{imageTemplateId}
     */
    mutateOpenImagesGet: (
      imageTemplateId: number,
      data?: OpenImageTemplateModel | Promise<OpenImageTemplateModel>,
      options?: MutatorOptions,
    ) =>
      mutate<OpenImageTemplateModel>(
        `/api/open/v1/images/${imageTemplateId}`,
        data,
        options,
      ),

    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesRegisterDockerArchive
     * @request POST:/api/open/v1/images/docker-archives
     */
    openImagesRegisterDockerArchive: (
      data: {
        /** @format binary */
        file?: File | null;
        name?: string | null;
        sourceImage?: string | null;
        osType?: OSType;
        expectedDigest?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/images/docker-archives`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesRegisterDockerReference
     * @request POST:/api/open/v1/images/docker-references
     */
    openImagesRegisterDockerReference: (
      data: DockerImageReferenceImportModel,
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/images/docker-references`,
        method: "POST",
        body: data,
        type: ContentType.Json,
        format: "json",
        ...params,
      }),

    /**
     * No description
     *
     * @tags OpenImages
     * @name OpenImagesRegisterVmQcow2
     * @request POST:/api/open/v1/images/vm-qcow2
     */
    openImagesRegisterVmQcow2: (
      data: {
        /** @format binary */
        file?: File | null;
        name?: string | null;
        osType?: OSType;
        networkMode?: VmNetworkMode;
        expectedDigest?: string | null;
      },
      params: RequestParams = {},
    ) =>
      this.request<ApiOperationModel, any>({
        path: `/api/open/v1/images/vm-qcow2`,
        method: "POST",
        body: data,
        type: ContentType.FormData,
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
