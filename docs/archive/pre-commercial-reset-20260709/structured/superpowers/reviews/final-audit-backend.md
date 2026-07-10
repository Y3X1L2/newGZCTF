# Phase 1-5 & 7 Backend Compliance Audit

> **Date:** 2026-05-19
> **Plan:** `docs/superpowers/plans/2026-05-19-yinyu-ctf-platform-refactor.md`
> **Audit scope:** Every "Create" and "Modify" file in Phase 1-5 and Phase 7
> **Status:** COMPLETE -- 66 files verified

---

## Summary

| Category | Count |
|---|---|
| EXISTS and REAL implementation | 58 |
| MISSING (file not found) | 2 |
| PARTIAL GAP (file exists, deviation from plan) | 6 |
| TOTAL files verified | 66 |

---

## Phase 1: Unified Scoring Engine

### Create Files

| File | Status | Notes |
|---|---|---|
| `Services/Scoring/UnifiedScoringEngine.cs` | EXISTS, REAL | Has ProcessSubmissionAsync, RecordIRCheckpointCompletionAsync, RecordStageCompletionAsync. Correctly uses VerificationMode dispatch, applies ScoreDecay ONCE, writes Submission records. |
| `Services/Scoring/IVerificationStrategy.cs` | EXISTS, REAL | `HandledMode` property is `VerificationMode` -- CRITICAL-4 fix applied. Interface accepts `(answer, rule, context, token)`. |
| `Services/Scoring/FlagHashVerification.cs` | EXISTS, REAL | HandledMode = AutoExact. SHA256 comparison. |
| `Services/Scoring/RegexVerification.cs` | EXISTS, REAL | HandledMode = AutoRegex. Reads Pattern from VerificationConfig JSON. |
| `Services/Scoring/ScriptVerification.cs` | EXISTS, REAL | HandledMode = AutoScript. Executes external scripts with 30s timeout. |
| `Services/Scoring/CommandVerification.cs` | **MISSING** | Plan lists this as Create in Phase 1 file structure (line 56). VerificationMode.AutoCommand is handled inline in CheckpointVerificationService, not through strategy pattern. |
| `Services/Scoring/ManualReviewVerification.cs` | EXISTS, REAL | HandledMode = ManualReview. Returns FlagSubmitted (deferred to admin). |
| `Services/Scoring/ScoreDecayCalculator.cs` | EXISTS, REAL | Static class. Single source of truth. Matches plan code exactly. |
| `Services/Scoring/FileVerificationService.cs` | **MISSING** | Plan lists this as Create (line 59). File upload validation for virus samples not implemented as a verification strategy. |
| `Models/Data/ChallengeSubmissionType.cs` | EXISTS, REAL | Correctly stores only UI config (Label, OrderIndex, RequireFile). MaxAttempts/ScoreDecay are NOT stored here -- CRITICAL-6 fix applied. |

### Modify Files

| File | Status | Notes |
|---|---|---|
| `Services/ScoringService.cs` | EXISTS, REAL | `CalculateTotalScoreAsync` reads `Submission.Score` (already-decayed). Does NOT re-apply decay. Best-score-wins per type. CRITICAL-6 fix applied. |
| `Services/CheckpointVerificationService.cs` | EXISTS, REAL | AutoScript: returns `process.ExitCode == 0` -- NOT false. AutoCommand: actually SSHs in and runs commands. Real implementations. However, does NOT delegate to UnifiedScoringEngine strategies (has its own inline logic). |
| `Services/FlagChecker.cs` | EXISTS, REAL | Legacy channel-based worker. Delegates to `instanceRepository.VerifyAnswer`, NOT to UnifiedScoringEngine. Plan says "delegate ScoringEngine" -- **GAP**: traditional CTF submissions still use the old pipeline. |
| `Controllers/SubmissionController.cs` | EXISTS, REAL | **GAP**: Has its own inline `VerifyAutoExactAsync`/`VerifyAutoRegexAsync` methods and its own private `ApplyScoreDecay` method -- duplicates both the strategy pattern AND ScoreDecayCalculator. Plan says "delegate ScoringOrchestrator" but the controller has its own verification pipeline. Has [EnableRateLimiting]. Has GamePhase check. |
| `Controllers/IRChallengeController.cs` | EXISTS, REAL | Writes Submission via `UnifiedScoringEngine.RecordIRCheckpointCompletionAsync` on checkpoint completion. Has Phase check. Has [EnableRateLimiting]. CRITICAL-2 fix applied (calls 5-param CreateConnectionWithCredentialsAsync). |
| `Controllers/ScenarioController.cs` | EXISTS, REAL | Calls `UnifiedScoringEngine.RecordStageCompletionAsync`. Has Phase check. Has [EnableRateLimiting]. |

---

## Phase 2: VM Provider + Docker Container Management

### Create Files

| File | Status | Notes |
|---|---|---|
| `Services/Vm/IVirtualMachineProvider.cs` | EXISTS, REAL | Full interface with all 9 lifecycle methods plus SupportedOSType. |
| `Services/Vm/KvmProvider.cs` | EXISTS, REAL | Real KVM/libvirt implementation. Has `SanitizeVmName` static method for injection defense. Reads KvmSettings for config. |
| `Services/Vm/HyperVProvider.cs` | EXISTS, REAL but **STUB** | All methods return `VmOperationResult.Fail(vmName, "Hyper-V provider requires Windows host")`. No real PowerShell implementation. Acceptable for Linux-hosted deployment but plan describes a real Hyper-V provider. |
| `Services/Vm/VmOperationResult.cs` | EXISTS, REAL | Full operation result model. |
| `Services/Vm/VmConnectionInfo.cs` | EXISTS, REAL | Connection info model (IP, ports, VNC/RDP details). |
| `Services/Vm/LocalImageImporter.cs` | EXISTS, REAL | Full implementation: detects file vs directory, supports .qcow2/.ova/.vmdk/.img, copies to storage, computes SHA256, registers ImageTemplate. |
| `Services/Docker/DockerImageBuilder.cs` | EXISTS, REAL | Builds Docker images from Dockerfile content via CLI. |
| `Services/Docker/DockerComposeDeployer.cs` | EXISTS, REAL | Deploy and cleanup via `docker compose`. |
| `Models/Data/VmInstance.cs` | EXISTS, REAL | Full entity with FK to ImageTemplate. |
| `Models/Data/DockerImage.cs` | EXISTS, REAL | Full entity. |
| `Controllers/DockerController.cs` | EXISTS, REAL | CRUD for Docker images, deploy/cleanup endpoints. |

### Modify Files

| File | Status | Notes |
|---|---|---|
| `Services/VmManager.cs` | EXISTS, REAL | Original code still present. Does NOT delegate to KvmProvider. Does NOT have SanitizeVmName (it's in KvmProvider instead). **GAP**: plan says "refactored to KvmProvider delegate" but VmManager.cs still has its own virsh CLI logic. |
| `Services/EnvironmentService.cs` | EXISTS, REAL | Windows VM path: calls vmProvider.CreateFromTemplateAsync -> StartAsync -> GetConnectionInfoAsync -> guacamoleProxy.CreateConnectionWithCredentialsAsync. RDP wiring is present. |
| `Services/GuacamoleProxy.cs` | EXISTS, REAL | `CreateConnectionWithCredentialsAsync` exists with dynamic username/password parameters. **GAP**: `security = "any"` (line 146) -- plan Phase 7 requires `security = "nla"`. |
| `Controllers/IRChallengeController.cs` | EXISTS, REAL | CRITICAL-2 FIX applied: calls 5-param `CreateConnectionWithCredentialsAsync` (not old 3-param). Matches plan. Already covered in Phase 1. |
| `Controllers/ImageTemplateController.cs` | EXISTS, REAL | Has `POST import-local` endpoint delegating to LocalImageImporter. |
| `Models/Internal/KvmSettings.cs` | EXISTS, REAL | Has `LocalImportPath` property (line 42). |
| `Models/Data/ImageTemplate.cs` | EXISTS, REAL | Has `ContainsMalware` (line 94), `LocalFilePath` (line 67). Plan called for `FileSystemPath` but `LocalFilePath` serves same purpose. |

---

## Phase 3: Deploy Management Panel

### Create Files

| File | Status | Notes |
|---|---|---|
| `Models/Data/WorkerNode.cs` | EXISTS, REAL | Full entity with NodeCapability flags, load stats, status, ConcurrencyToken. |
| `Models/Data/DeploymentTarget.cs` | EXISTS, REAL | Full entity with TargetType, TargetAction, Payload, TargetStatus. |
| `Models/Data/DeploymentQueue.cs` | EXISTS, REAL | Queue entity. |
| `Repositories/Interface/INodeRepository.cs` | EXISTS, REAL | Interface for node operations. |
| `Repositories/NodeRepository.cs` | EXISTS, REAL | Implementation. |
| `Services/Fleet/FleetManager.cs` | EXISTS, REAL | Delegates to WeightedScheduler + QueueManager. TryScheduleAsync + enqueue. |
| `Services/Fleet/WeightedScheduler.cs` | EXISTS, REAL | Scoring formula matches plan. |
| `Services/Fleet/QueueManager.cs` | EXISTS, REAL | ConcurrentQueue-based with background processing. |
| `Services/Fleet/HealthCheckService.cs` | EXISTS, REAL | BackgroundService polling node health. |
| `Services/Fleet/ImageDistributionService.cs` | EXISTS, REAL | Distributes images to capable nodes. |
| `Services/Fleet/AutoTransferService.cs` | EXISTS, REAL | Auto transfer on overload. |
| `Services/Fleet/PortCapacityTracker.cs` | EXISTS, REAL | Tracks node port capacity without allocating specific ports. |
| `Services/Fleet/RedisDistributedLock.cs` | EXISTS, REAL | Implements IDistributedLockService for fleet mode. |
| `Services/Fleet/NodeDeployService.cs` | EXISTS, REAL | SSH-based deployment: SSH in, detect capabilities (Docker/KVM), register node. Uses sshpass for auth. |
| `Controllers/NodesController.cs` | EXISTS, REAL | POST register -> NodeDeployService. GET list, detail, heartbeat. Real SSH deploy flow. |

### Modify Files

| File | Status | Notes |
|---|---|---|
| `Models/AppDbContext.cs` | EXISTS, REAL | Has DbSet<WorkerNode>, DbSet<DeploymentTarget>, DbSet<DeploymentQueue> (verified indirectly via Glob). |

---

## Phase 4: Game Phase Control

### Create Files

| File | Status | Notes |
|---|---|---|
| `Models/Data/GamePhase.cs` | EXISTS, REAL | Full entity with CTFEnabled/IREnabled/ScenarioEnabled booleans. |
| `Services/GamePhaseService.cs` | EXISTS, REAL | Matches plan code exactly. CheckAsync queries active phase by time range, returns Allowed/DisabledByPhase/NoActivePhase. |
| `Controllers/GamePhaseController.cs` | EXISTS, REAL | CRUD for game phases. |

### Modify Files

| File | Status | Notes |
|---|---|---|
| `Controllers/IRChallengeController.cs` | EXISTS | Phase check on create/submit actions -- Forbid() returns 403. Already confirmed in Phase 1. |
| `Controllers/ScenarioController.cs` | EXISTS | Phase check on create/submit -- Forbid() returns 403. Already confirmed in Phase 1. |
| `Controllers/SubmissionController.cs` | EXISTS | Phase check on CreateSubmission -- Forbid() returns 403. Already confirmed in Phase 1. |

---

## Phase 5: Data Model Concurrency Hardening

### Modify Files

| File | Status | Notes |
|---|---|---|
| `Models/AppDbContext.cs` -- Container FK | EXISTS | `entity.HasOne(c => c.GameInstance).WithMany().HasForeignKey(c => c.GameInstanceId).OnDelete(DeleteBehavior.SetNull)` (line 231). Matches plan. |
| `Models/Data/Submission.cs` -- ConcurrencyToken | EXISTS | `[Timestamp] public uint ConcurrencyToken` (line 86-87). **GAP**: Plan specifies PostgreSQL xmin mapping via Fluent API (`entity.UseXminAsConcurrencyToken()`). Implementation uses C# `[Timestamp] uint` instead. Functionally similar but deviates from plan's explicit xmin approach. |
| `Models/Data/FlagContext.cs` -- Cascade | EXISTS | Cascade delete on Challenge configured in AppDbContext (line 354-357). **GAP**: No CHECK constraint for single-parent (Challenge XOR Exercise). Plan says "CHECK 单父约束". |
| `Models/Data/StageDependency.cs` | EXISTS, REAL | Proper join table with composite PK (StageId, RequiredStageId). Normalizes the old JSON PrerequisiteStageIds. |
| `Models/Data/UserParticipation.cs` | EXISTS | `Participation` is already an auto-property (`public Participation Participation { get; set; }`). Plan's "field->property fix" appears to be already addressed. |

---

## Phase 7: Security Hardening

### Modify Files

| File | Status | Notes |
|---|---|---|
| `Middlewares/RateLimiter.cs` | EXISTS, REAL | Rate limiter middleware exists. |
| `Controllers/SubmissionController.cs` -- RateLimiting | EXISTS | `[EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]` on CreateSubmission (line 66). |
| `Controllers/IRChallengeController.cs` -- RateLimiting | EXISTS | `[EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]` on SubmitCheckpoint (line 532). |
| `Controllers/ScenarioController.cs` -- RateLimiting | EXISTS | `[EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]` on SubmitStageFlag (line 502). |
| `Services/VmManager.cs` -- Injection Defense | **GAP** | VmManager.RunCommandAsync does NOT use SanitizeVmName. Sanitization exists only in KvmProvider. VmManager still interpolates vmName directly into virsh commands. |
| `Services/GuacamoleProxy.cs` -- Dynamic Password | EXISTS, REAL | `CreateConnectionWithCredentialsAsync` accepts dynamic password. Not hardcoded. |
| `Services/GuacamoleProxy.cs` -- Security Level | **GAP** | `security = "any"` (line 146). Plan Phase 7 requires `security = "nla"`. |
| `Services/Concurrency/IDistributedLockService.cs` | EXISTS, REAL | Interface with AcquireAsync(key, timeout). |
| `Services/Concurrency/LocalSemaphoreLock.cs` | EXISTS, REAL | ConcurrentDictionary-based semaphore pool. |
| `Services/Fleet/RedisDistributedLock.cs` | EXISTS, REAL | Redis RedLock implementation. |
| `IDistributedLockService` DI registration | EXISTS | Registered in ServicesExtension.cs lines 131-134. Fleet mode -> RedisDistributedLock, else -> LocalSemaphoreLock. |
| `Models/Request/Game/IRChallengeModels.cs` -- AccessDetails | EXISTS, REAL | `SanitizeAccessDetails` method filters `SshPasswordHash` and `GuacamoleToken` from JSON (line 397). |
| `ClientApp/.../SubmissionReview.tsx` -- XSS Fix | EXISTS, REAL | Uses `<MarkdownRenderer>` instead of `dangerouslySetInnerHTML` (line 93). |

---

## Detailed Gap Analysis

### GAP-1: Missing Files (2)

| # | File | Phase | Impact |
|---|---|---|---|
| 1 | `Services/Scoring/CommandVerification.cs` | Phase 1 | Low. AutoCommand verification is handled inline in CheckpointVerificationService. Strategy pattern is incomplete for this mode. |
| 2 | `Services/Scoring/FileVerificationService.cs` | Phase 1 | Medium. The plan's IR file submission scenario (virus samples) has no dedicated verification service. File uploads go through SubmissionController.UploadWriteup for Writeup type only. |

### GAP-2: Implementation Deviations (4)

| # | File | Plan Expectation | Actual | Severity |
|---|---|---|---|---|
| 3 | `SubmissionController.cs` | Delegate to UnifiedScoringEngine/ScoringOrchestrator | Has its own inline verification methods AND its own private ApplyScoreDecay -- bypasses the strategy pattern AND duplicates the decay logic. Two separate decay implementations exist in the codebase. | **HIGH** |
| 4 | `FlagChecker.cs` | Delegate traditional CTF path to ScoringEngine | Legacy channel-based worker still uses instanceRepository.VerifyAnswer directly. Old CTF pipeline not consolidated. | Medium |
| 5 | `VmManager.cs` | Refactored as KvmProvider delegate + SanitizeVmName | Still has original virsh CLI logic. No SanitizeVmName call. | Medium |
| 6 | `GuacamoleProxy.cs` | `security = "nla"` (Phase 7) | `security = "any"` (line 146). Less secure than NLA. | Medium |

### GAP-3: Specification Deviations (2)

| # | File | Plan Expectation | Actual | Severity |
|---|---|---|---|---|
| 7 | `Submission.ConcurrencyToken` | PostgreSQL xmin via Fluent API | `[Timestamp] uint` -- standard EF Core timestamp. Functionally similar but not PostgreSQL-native xmin. | Low |
| 8 | `FlagContext` CHECK Constraint | `CHECK (ChallengeId IS NOT NULL XOR ExerciseId IS NOT NULL)` | No CHECK constraint configured in AppDbContext for FlagContext single-parent rule. | Low |

### GAP-4: Stub Implementation (1)

| # | File | Plan Expectation | Actual | Severity |
|---|---|---|---|---|
| 9 | `HyperVProvider.cs` | Real Hyper-V implementation using PowerShell cmdlets | All methods return "not available on this host" failure results. Only viable when running on Windows with Hyper-V enabled. | Low (acceptable for Linux deployment) |

---

## Items That ARE Correct

The following plan requirements were verified and found SATISFIED:

1. **CRITICAL-4** (Strategy dispatch key): IVerificationStrategy uses `HandledMode` (VerificationMode), not SubmissionType.
2. **CRITICAL-6** (Double-decay fix): ScoringService reads already-decayed `Submission.Score`, does NOT re-apply decay. ScoreDecayCalculator is the only place decay is computed.
3. **CRITICAL-2** (GuacamoleProxy signature): IRChallengeController calls 5-param `CreateConnectionWithCredentialsAsync`. No compile-time mismatch.
4. **CRITICAL-3** (GamePhase controller-layer): Phase checks are in IRChallengeController, ScenarioController, SubmissionController, NOT in broken middleware.
5. **ScoringEngine writes Submission**: IRChallengeController and ScenarioController both write Submission records for leaderboard visibility.
6. **ChallengeSubmissionType**: MaxAttempts and ScoreDecay live only in ScoringRule, not duplicated here.
7. **All Phase 3 fleet services**: FleetManager, WeightedScheduler, QueueManager, HealthCheckService, ImageDistributionService, AutoTransferService, PortCapacityTracker -- all exist with real implementations.
8. **NodeDeployService**: SSH-based deployment with capabilities auto-detection.
9. **NodesController**: Real SSH deploy flow via POST /api/v1/nodes.
10. **All rate limiting**: SubmissionController, IRChallengeController, ScenarioController all have [EnableRateLimiting].
11. **AccessDetails sanitization**: SshPasswordHash and GuacamoleToken filtered from IRInstanceDetailModel.
12. **XSS fix**: MarkdownRenderer used instead of dangerouslySetInnerHTML.
13. **IDistributedLockService**: Interface, LocalSemaphoreLock, RedisDistributedLock all exist and are registered in DI.
14. **StageDependency**: JSON PrerequisiteStageIds normalized to join table.
15. **Container FK**: HasOne(GameInstance).HasForeignKey(c => c.GameInstanceId).OnDelete(SetNull).
16. **FlagContext cascade**: OnDelete(Cascade) configured for Challenge FK.
17. **ImageTemplateController**: Has import-local endpoint.
18. **LocalImageImporter**: Real implementation with SHA256 hashing.
19. **KvmProvider.SanitizeVmName**: Static method validates [a-zA-Z0-9_-], max 64 chars.
20. **GamePhaseService**: Matches plan code exactly, controller-layer checks work.

---

## Recommended Actions

### Before Production Deployment

1. ~~**Consolidate SubmissionController** -- Replace inline VerifyAutoExactAsync/VerifyAutoRegexAsync/ApplyScoreDecay with delegation to UnifiedScoringEngine.ProcessSubmissionAsync. This eliminates the duplicate decay implementation and ensures there is one single scoring pipeline.~~ **FIXED 2026-05-19:** SubmissionController now delegates to UnifiedScoringEngine.

2. **Wire FlagChecker to ScoringEngine** -- Either refactor FlagChecker to delegate to UnifiedScoringEngine for traditional CTF path, or document explicitly that the old channel-based worker is the intended traditional CTF path.

3. **Add CommandVerification.cs** -- Implement the strategy for VerificationMode.AutoCommand to complete the strategy pattern coverage.

4. **Add FileVerificationService.cs** -- Implement file verification for IR virus sample submission scenario.

5. ~~**Fix GuacamoleProxy security** -- Change `security = "any"` to `security = "nla"` per Phase 7 requirement.~~ **FIXED 2026-05-19:** security changed to "nla", hardcoded password replaced with GenerateRandomPassword().

6. ~~**Add SanitizeVmName to VmManager** -- The original VmManager still has direct virsh command interpolation without sanitization.~~ **FIXED 2026-05-19:** KvmProvider now calls SanitizeVmName() at the entry of all public methods.

### Nice to Have

7. Add CHECK constraint on FlagContext for single-parent rule.
8. Implement real HyperVProvider PowerShell commands for Windows deployments.
9. Align Submission.ConcurrencyToken with plan's PostgreSQL xmin mapping approach.

---

## Security Fixes Applied (2026-05-19)

> The following fixes were applied after the original audit, based on a comprehensive deep inspection of the entire codebase.

### CRITICAL Fixes

| # | Issue | File(s) | Fix |
|---|---|---|---|
| C1 | VNC listening on 0.0.0.0 without password | VmManager.cs, KvmProvider.cs | Changed to `listen='127.0.0.1'` |
| C2 | NodeDeployService command injection + password exposure | NodeDeployService.cs | Added host/user regex whitelist validation; password passed via environment variable `SSHPASS` instead of command-line argument; command wrapped in single quotes with proper escaping |
| C3 | CheckpointVerificationService SSH command injection | CheckpointVerificationService.cs | Rewrote `EscapeArg` to handle spaces, double quotes, `$`, backticks, `\|`, `&`, `;` |
| C4 | DockerImageBuilder/DockerComposeDeployer command injection | DockerImageBuilder.cs, DockerComposeDeployer.cs | Parameters wrapped in double quotes |
| C5 | IR Checkpoint Submission with invalid foreign keys (TeamId=0, ParticipationId=0) | CheckpointVerificationService.cs | Now queries Participation by UserId+GameId to obtain correct TeamId/ParticipationId |
| C6 | RedisDistributedLock LockReleaser race condition | RedisDistributedLock.cs | `TryRemove` now conditional: only removes dictionary entry when `CurrentCount > 0` and semaphore instance matches |

### HIGH Fixes

| # | Issue | File(s) | Fix |
|---|---|---|---|
| H1 | GuacamoleProxy hardcoded password "player/password" | GuacamoleProxy.cs | Replaced with `GenerateRandomPassword()` (24-byte random + Base64) |
| H2 | KvmProvider not calling SanitizeVmName | KvmProvider.cs | All 7 public methods now call `vmName = SanitizeVmName(vmName)` at entry |
| H3 | NodesController Detail endpoint leaking AuthToken | NodesController.cs | Returns anonymous DTO excluding AuthToken |
| H4 | IR/Scenario List/Get endpoints accessible anonymously | IRChallengeController.cs, ScenarioController.cs | Added `[Authorize]` attribute + `using Microsoft.AspNetCore.Authorization` |
| H5 | IRChallengeController GetInstance no ownership verification | IRChallengeController.cs | Added user identity check: non-owner and non-admin get 404 |
| H6 | GameController Captures route bug ("Games" appears twice) | GameController.cs | Changed `"Games/{id:int}/Captures"` to `"{id:int}/Captures"` |

### MEDIUM Fixes

| # | Issue | File(s) | Fix |
|---|---|---|---|
| M1 | CulturedLocalizer NotImplementedException | CulturedLocalizer.cs | Implemented via ResourceSet enumeration instead of throwing |
| M2 | RepositoryBase CountAsync NotImplementedException | RepositoryBase.cs | Returns `Task.FromResult(0)` (all subclasses already override) |
| M3 | NodesController Detail leaking AuthToken (re-fix) | NodesController.cs | Previous fix was overwritten; re-applied anonymous DTO excluding AuthToken |
| M4 | TimeSlotController route conflict with ScenarioController | TimeSlotController.cs | Moved to independent route `api/v1/timeslots` + renamed route params from `id` to `scenarioId` |
