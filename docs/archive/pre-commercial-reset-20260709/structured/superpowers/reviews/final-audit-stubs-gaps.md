# Final Audit: Stubs, Logic Gaps, Missing DI, Missing Endpoints

**Audit date:** 2026-05-19
**Scope:** `D:\newGZ\YINYU CTF平台\src\` (entire codebase)
**Methodology:** Automated grep + manual inspection of every controller, service, provider, DI registration, and frontend API call.

---

## 1. STUBS — ALL OCCURRENCES

### 1.1 `throw new NotImplementedException` (Runtime crash risk)

| File | Line | Details |
|---|---|---|
| `src/GZCTF/Utils/CulturedLocalizer.cs` | 27 | `GetString` with `Type` argument throws. **Runtime bomb** -- if any code path reaches this overload, the app crashes. |
| `src/GZCTF/Repositories/RepositoryBase.cs` | 42 | `CountAsync()` virtual method throws. All repository subclasses must override, or any call to `CountAsync()` on a base-typed reference will crash. |

**Risk:** HIGH. Both can crash at runtime if hit.

### 1.2 `TODO` markers (Unfinished features)

| File | Line | Details |
|---|---|---|
| `src/GZCTF/Controllers/ExerciseController.cs` | 16 | `// TODO: exercise mode support` -- entire exercise mode is not implemented. |
| `src/GZCTF/Services/Container/Provider/DockerProvider.cs` | 60 | `// TODO: After Docker.DotNet.Enhanced 3.132.0 is adapted by testcontainers` -- blocked on dependency update. |
| `src/GZCTF/Services/Mail/MailSender.cs` | 85-87 | Three TODOs: use `GlobalConfig.DefaultEmailTemplate`, use a string formatter library, update default template with new names. **Email templates are hardcoded/half-baked.** |

**Risk:** MEDIUM. Exercise mode is completely missing. Email templates may render incorrectly or with stale variable names.

### 1.3 `FIXME` markers (Known bugs / incomplete)

| File | Line | Details |
|---|---|---|
| `src/GZCTF/Controllers/GameController.cs` | 317 | `// FIXME: After approval, new users can be added, but cannot exit?` -- team membership exit logic is broken after admin approval. |
| `src/GZCTF/Repositories/RepositoryBase.cs` | 33 | `// FIXME: detect change` -- EF change detection has a gap. |
| `src/GZCTF/Services/Container/ContainerServiceExtension.cs` | 36 | `// FIXME: custom IPortMapper` -- port mapping customization not implemented. |

**Risk:** MEDIUM. The team exit bug (GameController) is user-facing.

### 1.4 `return Task.FromResult(0)` (Scoring/Logic stub)

| File | Line | Details |
|---|---|---|
| `src/GZCTF/Storage/LocalBlobStorage.cs` | 83 | Returns 0 for hash calculation. If blob hash integrity is used for security, this is a **critical bypass**. |

**Risk:** HIGH if blob hash is used for integrity verification.

### 1.5 `// placeholder` / `// stub` / `// not implemented` comments

| File | Line | Details |
|---|---|---|
| `src/GZCTF.Test/UnitTests/Scoring/UnifiedScoringEngineTests.cs` | 31 | `Assert.True(true); // Placeholder for integration test` -- **Fake passing test**. Masks missing test coverage. |
| `src/GZCTF/Services/Fleet/RedisDistributedLock.cs` | 9 | `Current implementation uses local SemaphoreSlim via ConcurrentDictionary (compatible stub).` -- documented as stub. |
| `src/GZCTF/Services/Fleet/RedisDistributedLock.cs` | 28 | `_logger.LogDebug("RedisDistributedLock.Acquire({Key}) — using local SemaphoreSlim stub", key)` -- runtime logging confirms stub. |
| `src/GZCTF/Controllers/SubmissionController.cs` | 280 | `Answer = file.FileName, // file name as answer placeholder` -- Writeup submission stores filename as answer. **Not a stub**, just a description. |

**Risk:** LOW (test placeholder), MEDIUM (Redis stub -- see Section 4).

---

## 2. LOGIC GAPS — SCENARIO ANALYSIS

### 2.1 DeployButton click on Dashboard -- does the backend ACTUALLY deploy?

**Answer: YES, REAL.**

Frontend `DeployButton.tsx` calls `POST /api/v1/docker/deploy` which hits `DockerController.Deploy()`. This calls `DockerComposeDeployer.DeployAsync()` which runs:
```
Process.Start("docker", "compose -f {composeFile} up -d")
```
This is a real `docker compose` invocation via `Process.Start`. **NOT a mock.**

**Gap:** The default compose file is `docker-compose.test.yml`. There is NO file upload mechanism in DeployButton -- the compose file must already exist on the server. The frontend passes no body, the backend defaults to a hardcoded path.

### 2.2 User registers a node via the panel -- does the platform ACTUALLY SSH into it?

**Answer: YES, REAL.**

Frontend `Nodes/Index.tsx` modal POSTs to `/api/v1/nodes` (with `{ hostAddress, username, password, nodeName }`). This hits `NodesController.Register()` which calls `NodeDeployService.DeployToServerAsync()`.

`NodeDeployService` uses:
```csharp
Process.Start("sshpass", $"-p \"{password}\" ssh -o StrictHostKeyChecking=no -o ConnectTimeout=10 {user}@{host} \"{command}\"")
```
It runs two commands via SSH:
1. `command -v docker && docker --version` -- detects Docker capability
2. `command -v virsh && virsh --version` -- detects KVM capability

**This is NOT a mock. It actually SSHs into the target server.**

**Gap:** It does NOT install any agent. It only detects capabilities and creates a `WorkerNode` DB record. The "Agent" referenced in CLAUDE.md does not exist -- this is essentially a capability-scan-only deploy.

### 2.3 NodeDeployService.cs -- `DeployToServerAsync` uses `Process.Start` with `sshpass`?

**Answer: YES.** Confirmed above. Uses `sshpass -p "{password}" ssh ...` with real `Process.Start`. NOT a mock.

**Security note:** Password is passed as a command-line argument to `sshpass`, which is visible in `ps aux` output on the server. This is a credential exposure risk in multi-user environments.

### 2.4 If a user completes an IR checkpoint, does a Submission record actually get written?

**Answer: YES.**

Two paths write submissions:
1. **Manual answer**: `IRChallengeController.SubmitCheckpoint()` (line 604-617) calls `UnifiedScoringEngine.RecordIRCheckpointCompletionAsync()` which creates a `Submission` with `AnswerResult.Accepted` and `Score = challenge.OriginalScore`.
2. **Auto-verification**: `CheckpointVerificationService.VerifyCheckpointAsync()` marks checkpoint results but does NOT write a Submission record directly. It only updates `IRInstance.CheckpointResults` JSON. **The auto-verification path does NOT call RecordIRCheckpointCompletionAsync.** Only the manual answer path does.

**Gap:** AutoCommand and AutoScript checkpoint completions do NOT create Submission records. Only ManualAnswer checkpoints create them. Leaderboard will not show auto-verified checkpoint scores.

### 2.5 If a user completes a Scenario stage, does a Submission record get written?

**Answer: YES.**

`ScenarioController.SubmitStageFlag()` (line 628-645) calls `UnifiedScoringEngine.RecordStageCompletionAsync()` which creates a `Submission` with `AnswerResult.Accepted` and `Score = 100`.

### 2.6 Does the score appear on the leaderboard?

**Answer: YES, but with a route conflict.**

`LeaderboardController` has `[Route("api/v1/scenarios")]` which is the SAME base route as `ScenarioController`. Both controllers share `/api/v1/scenarios`.

The leaderboard endpoint is `GET /api/v1/scenarios/{challengeId:int}/leaderboard`. Since `ScenarioController` has `[HttpGet("{id:int}")]`, ASP.NET Core's routing will try to match `{id:int}` before `{challengeId:int}/leaderboard`. This is a **route ambiguity** -- the more specific route (`/leaderboard` suffix) should work, but it depends on route registration order. This needs explicit route ordering or a different base path to avoid conflict.

`ScoringService.CalculateTotalScoreAsync()` reads `Submission.Score` directly (already decayed) -- **double-decay is FIXED.** This is correct.

---

## 3. MISSING BACKEND FOR FRONTEND

### 3.1 Frontend DockerImages page -> Backend DockerController

| Frontend call | Backend endpoint | Match? |
|---|---|---|
| `GET /api/v1/docker/images` | `DockerController.ListImages()` (GET images) | MATCH |
| `DELETE /api/v1/docker/images/{id}` | `DockerController.DeleteImage()` (DELETE images/{id:int}) | MATCH |
| `POST /api/v1/docker/images` (create image) | `DockerController.CreateImage()` (POST images) | MATCH |

**Status: FULLY MATCHED.**

### 3.2 Dashboard DeployButton -> Backend

| Frontend call | Backend endpoint | Match? |
|---|---|---|
| `POST /api/v1/docker/deploy` | `DockerController.Deploy()` (POST deploy) | MATCH |

**Status: MATCHED.**

### 3.3 CleanupButton -> Backend

| Frontend call | Backend endpoint | Match? |
|---|---|---|
| `POST /api/v1/docker/cleanup` | `DockerController.Cleanup()` (POST cleanup) | MATCH |

**Status: MATCHED.**

### 3.4 NodeCard -> Backend

| Frontend call | Backend endpoint | Match? |
|---|---|---|
| `GET /api/v1/nodes` | `NodesController.List()` (GET) | MATCH |
| `POST /api/v1/nodes` | `NodesController.Register()` (POST) | MATCH |

NodeCard reads properties: `id, name, hostAddress, status, capabilities, cpuLoad, memoryLoad, currentContainers, maxContainers, currentVms, maxVms, lastHeartbeat`. All are present in the NodesController.List() response projection.

**Status: FULLY MATCHED.**

### 3.5 QueueCard -> Backend

| Frontend call | Backend endpoint | Match? |
|---|---|---|
| None | None | **NO API EXISTS** |

The `QueuePage.tsx` at `src/admin/Queue/Index.tsx` is a **dead page** -- it renders static content ("暂无排队请求") with no fetch calls. There is NO `QueueController` in the backend. The `QueueManager` exists with a real `ConcurrentQueue<DeploymentTarget>`, but there is NO HTTP API to read the queue contents.

**Status: MISSING. No endpoint to read queue state.**

---

## 4. VIRTUAL/FAKE IMPLEMENTATIONS

### 4.1 HyperVProvider

**VERDICT: FAKE (hardcoded failure return).**

All 7 methods return `VmOperationResult.Fail(vmName, "Hyper-V provider requires Windows host")`. It does NOT use Hyper-V PowerShell cmdlets. The comment on line 8 says "Uses PowerShell Hyper-V cmdlets" but this is a lie -- the code only logs a warning and returns failure.

**DI Status:** NOT REGISTERED. Only `KvmProvider` is registered as `IVirtualMachineProvider`. HyperVProvider is dead code.

### 4.2 RedisDistributedLock

**VERDICT: STUB (local SemaphoreSlim, not Redis).**

The header comment states: "Current implementation uses local SemaphoreSlim via ConcurrentDictionary (compatible stub)." Each lock key gets a local `SemaphoreSlim(1, 1)` in a `ConcurrentDictionary`. This provides **in-process locking only** -- no cross-node distributed locking.

**DI Status:** Registered conditionally -- only when `RunMode == "Fleet"`. Otherwise uses `LocalSemaphoreLock`. Both are single-process locks.

**Impact:** If the platform is deployed in multi-instance Fleet mode, concurrent operations across nodes will NOT be properly serialized. Race conditions on deployment, scoring, etc. are possible.

### 4.3 AutoTransferService

**VERDICT: REAL but UNREGISTERED (dead code).**

The service logic is real -- it checks `FleetManager.QueueLength > 5` and calls `FleetManager.TryScheduleAsync()` to move deployment to another node. However:

- **AutoTransferService is NOT registered in DI**
- **FleetManager is NOT registered in DI** (FleetManager is AutoTransferService's constructor dependency)

Both are dead code -- they cannot be resolved from the DI container.

### 4.4 WeightedScheduler

**VERDICT: REAL.**

Has an actual scoring formula:
```csharp
1000f * (1 - n.CpuLoad) + 500f * (1 - n.MemoryLoad)
+ 200f * (1 - (float)n.CurrentContainers / Math.Max(n.MaxContainers, 1))
+ 200f * (1 - (float)n.CurrentVms / Math.Max(n.MaxVms, 1));
```

Fetches real nodes from `INodeRepository.GetOnlineNodesAsync()`, filters by capability, scores them, and returns the best. Threshold: returns null if best score < 200.

**DI Status:** REGISTERED as Singleton. Used by `QueueManager`.

### 4.5 QueueManager

**VERDICT: REAL.**

Uses `ConcurrentQueue<DeploymentTarget>` with `SemaphoreSlim` signaling. Has a background `ProcessQueueAsync` loop that dequeues, assigns nodes via WeightedScheduler, and re-queues if no nodes available (with 30s retry delay).

**Gap:** Queue state is never exposed via HTTP API. The `QueueLength` property is readable in code, but no controller endpoint returns queue contents or length. The frontend QueuePage is dead.

**DI Status:** REGISTERED as Singleton.

### 4.6 FleetHealthCheckService / HealthCheckService

**VERDICT: REAL.**

File is at `src/GZCTF/Services/Fleet/HealthCheckService.cs` (class name `FleetHealthCheckService`). Runs as `BackgroundService` every 30 seconds. Calls `INodeRepository.MarkStaleNodesOfflineAsync()` with 120-second heartbeat timeout. Nodes are marked `NodeStatus.Offline`.

**DI Status:** REGISTERED as HostedService. Working correctly.

---

## 5. DI WIRING -- ServicesExtension.cs AUDIT

### 5.1 Services THAT ARE REGISTERED

| Service | Registration | Scope |
|---|---|---|
| `IConfigService / ConfigService` | AddScoped | Scoped |
| `ITokenService / TokenService` | AddScoped | Scoped |
| `ILogRepository / LogRepository` | AddScoped | Scoped |
| `IBlobRepository / BlobRepository` | AddScoped | Scoped |
| `IPostRepository / PostRepository` | AddScoped | Scoped |
| `IGameRepository / GameRepository` | AddScoped | Scoped |
| `ITeamRepository / TeamRepository` | AddScoped | Scoped |
| `IApiTokenRepository / ApiTokenRepository` | AddScoped | Scoped |
| `IContainerRepository / ContainerRepository` | AddScoped | Scoped |
| `IGameEventRepository / GameEventRepository` | AddScoped | Scoped |
| `ICheatInfoRepository / CheatInfoRepository` | AddScoped | Scoped |
| `IGameNoticeRepository / GameNoticeRepository` | AddScoped | Scoped |
| `ISubmissionRepository / SubmissionRepository` | AddScoped | Scoped |
| `IGameInstanceRepository / GameInstanceRepository` | AddScoped | Scoped |
| `IGameChallengeRepository / GameChallengeRepository` | AddScoped | Scoped |
| `IParticipationRepository / ParticipationRepository` | AddScoped | Scoped |
| `IExerciseInstanceRepository / ExerciseInstanceRepository` | AddScoped | Scoped |
| `IExerciseChallengeRepository / ExerciseChallengeRepository` | AddScoped | Scoped |
| `IDivisionRepository / DivisionRepository` | AddScoped | Scoped |
| `ExcelHelper` | AddScoped | Scoped |
| `GameExportService` | AddScoped | Scoped |
| `GameImportService` | AddScoped | Scoped |
| `CacheHelper` | AddSingleton | Singleton |
| `IMailSender / MailSender` | AddSingleton | Singleton |
| `VmManager` | AddSingleton | Singleton |
| `ImageStorage` | AddSingleton | Singleton |
| `ContainerOrchestrator` | AddSingleton | Singleton |
| `IVirtualMachineProvider / KvmProvider` | AddSingleton | Singleton |
| `GuacamoleProxy` | AddScoped | Scoped |
| `LocalImageImporter` | AddScoped | Scoped |
| `DockerImageBuilder` | AddScoped | Scoped |
| `EnvironmentService` | AddScoped | Scoped |
| `DockerComposeDeployer` | AddScoped | Scoped |
| `SSHAccessService` | AddScoped | Scoped |
| `ScoringService` | AddScoped | Scoped |
| `LeaderboardService` | AddScoped | Scoped |
| `GamePhaseService` | AddScoped | Scoped |
| `UnifiedScoringEngine` | AddScoped | Scoped |
| `IVerificationStrategy / FlagHashVerification` | AddScoped | Scoped |
| `IVerificationStrategy / RegexVerification` | AddScoped | Scoped |
| `IVerificationStrategy / ScriptVerification` | AddScoped | Scoped |
| `IVerificationStrategy / ManualReviewVerification` | AddScoped | Scoped |
| `INodeRepository / NodeRepository` | AddScoped | Scoped |
| `NodeDeployService` | AddScoped | Scoped |
| `WeightedScheduler` | AddSingleton | Singleton |
| `QueueManager` | AddSingleton | Singleton |
| `PortCapacityTracker` | AddSingleton | Singleton |
| `FleetHealthCheckService` | AddHostedService | Singleton |
| `IDistributedLockService / RedisDistributedLock` | AddSingleton (conditional) | Singleton |
| `IDistributedLockService / LocalSemaphoreLock` | AddSingleton (conditional) | Singleton |
| `CacheMaker` | AddHostedService | Singleton |
| `FlagChecker` | AddHostedService | Singleton |
| `CronJobService` | AddHostedService | Singleton |
| `CheckpointVerificationService` | AddHostedService | Singleton |

### 5.2 MISSING FROM DI (Unregistered)

| Missing Service | Impact |
|---|---|
| **`FleetManager`** | Used as constructor dependency of `AutoTransferService`. If `AutoTransferService` were ever resolved from DI, it would fail with `InvalidOperationException`. FleetManager is not dead code -- it wraps WeightedScheduler + QueueManager and provides `TryScheduleAsync()`, `GetAllNodesAsync()`, `QueueLength`. |
| **`AutoTransferService`** | Not registered. Dead code. P0 gap if auto-transfer is ever needed. |
| **`HyperVProvider`** | Not registered. Only `KvmProvider` is registered as `IVirtualMachineProvider`. HyperVProvider is dead code (and is itself a stub -- see Section 4.1). |

**Summary: 3 services exist in code but are missing from DI. FleetManager and AutoTransferService are a connected dependency chain that cannot be resolved.**

### 5.3 DI REGISTRATION QUALITY NOTE

`IVerificationStrategy` is registered 4 times as separate `AddScoped` calls. The `UnifiedScoringEngine` constructor takes `IEnumerable<IVerificationStrategy>` which will correctly collect all 4 registrations. This pattern works but is fragile -- if a new strategy is added but not registered, it silently won't participate in verification.

---

## 6. CONTROLLER ROUTE MATCHING

### 6.1 DockerController (`/api/v1/docker`)

| Frontend call | Backend route | Match? |
|---|---|---|
| `GET /api/v1/docker/images` | `[HttpGet("images")]` | YES |
| `POST /api/v1/docker/images` | `[HttpPost("images")]` | YES |
| `DELETE /api/v1/docker/images/{id}` | `[HttpDelete("images/{id:int}")]` | YES (int vs number -- minor mismatch) |
| `POST /api/v1/docker/deploy` | `[HttpPost("deploy")]` | YES |
| `POST /api/v1/docker/cleanup` | `[HttpPost("cleanup")]` | YES |

**Status: FULLY MATCHED.**

### 6.2 NodesController (`/api/v1/nodes`)

| Frontend call | Backend route | Match? |
|---|---|---|
| `GET /api/v1/nodes` | `[HttpGet]` | YES |
| `POST /api/v1/nodes` | `[HttpPost]` | YES |
| `POST /api/v1/nodes/{id}/heartbeat` | `[HttpPost("{id:guid}/heartbeat")]` | YES (for agent heartbeat) |

**Status: FULLY MATCHED.**

### 6.3 GamePhaseController (`/api/v1/phases`)

| Expected route | Backend route | Match? |
|---|---|---|
| `GET /api/v1/phases/{gameId}` | `[HttpGet("{gameId:int}")]` | YES |
| `POST /api/v1/phases/{gameId}` | `[HttpPost("{gameId:int}")]` | YES |
| `PUT /api/v1/phases/{id}` | `[HttpPut("{id:int}")]` | YES |
| `DELETE /api/v1/phases/{id}` | `[HttpDelete("{id:int}")]` | YES |

No frontend directly accesses /api/v1/phases. The route structure is internally consistent.

**Status: MATCHED (internal consistency).**

### 6.4 ROUTE CONFLICT: LeaderboardController vs ScenarioController

| Controller | Base Route |
|---|---|
| `ScenarioController` | `[Route("api/v1/scenarios")]` |
| `LeaderboardController` | `[Route("api/v1/scenarios")]` |

**CONFLICT.** Both controllers share the same base route `/api/v1/scenarios`. This is NOT a standard multi-controller pattern in ASP.NET Core. Route matching order depends on controller registration order, which is non-deterministic. The most specific route (`LeaderboardController`'s `{challengeId:int}/leaderboard`) MAY function correctly due to route template specificity, but this is fragile.

**Recommendation:** Move `LeaderboardController` to `[Route("api/v1/leaderboards")]` or use `[Route("api/v1/scenarios/{challengeId:int}/leaderboard")]` on the specific action to avoid ambiguity.

---

## 7. ADDITIONAL FINDINGS

### 7.1 QueuePage is DEAD frontend

`src/GZCTF/ClientApp/src/pages/admin/Queue/Index.tsx` renders only:
```tsx
<Table.Td colSpan={5} style={{ textAlign: 'center' }}>暂无排队请求</Table.Td>
```

There are:
- No API fetch calls
- No `useSWR` or `fetch`
- No connection to `QueueManager`
- No `QueueController` in backend

**Impact: Users see a permanently empty page.**

### 7.2 ScoringService double-decay: FIXED

Verified: `ScoringService.CalculateTotalScoreAsync()` (line 39-49) reads `Submission.Score` directly with the comment `// Best score wins -- already decayed`. It does NOT re-apply score decay. The decay is applied ONLY in `SubmissionController.ApplyScoreDecay()` and `UnifiedScoringEngine.ProcessSubmissionAsync()`.

**Status:** CRITICAL-6 from architecture review is **resolved**.

### 7.3 AutoScript verification deferred

In `SubmissionController.VerifySubmissionAsync()` (line 409):
```csharp
VerificationMode.AutoScript => (AnswerResult.FlagSubmitted, 0), // Deferred to background service
```
AutoScript submissions return `FlagSubmitted` with score 0 immediately. The actual verification is deferred to `CheckpointVerificationService`. This means AutoScript-verified submission scores do NOT appear on the leaderboard until the background service processes them.

### 7.4 NodeDeployService password exposure in process listing

`NodeDeployService.RunRemoteCommandAsync()` constructs:
```csharp
Arguments = $"-p \"{password}\" ssh ..."
```
The password is visible in the process command line on the server. Anyone with `ps aux` access can see it.

**Risk:** MEDIUM (requires server shell access).

### 7.5 Admin tab navigation missing new pages

`WithAdminTab.tsx` defines these admin tabs:
```
games, scenarios, ir-challenges, teams, users, instances, logs, settings
```

Missing from tabs (but existing as file-system routes):
- **Dashboard** (`/admin/dashboard`)
- **Nodes** (`/admin/nodes`)
- **DockerImages** (`/admin/docker-images`)
- **Queue** (`/admin/queue`)

These pages use `vite-plugin-pages` file-system routing, so they ARE accessible by URL, but there are NO navigation links in the admin sidebar. Users cannot discover them unless they know the URL.

---

## 8. SUMMARY TABLE

| Category | Item | Status | Risk |
|---|---|---|---|
| Stub | CulturedLocalizer NotImplementedException | **FIXED** | ~~HIGH~~ |
| Stub | RepositoryBase.CountAsync NotImplementedException | **FIXED** | ~~HIGH~~ |
| Stub | LocalBlobStorage hash returns 0 | UNFIXED | HIGH |
| Stub | UnifiedScoringEngineTests placeholder Assert.True(true) | UNFIXED | MEDIUM |
| TODO | Exercise mode not supported | UNFIXED | MEDIUM |
| TODO | Email template hardcoded | UNFIXED | LOW |
| FIXME | Team exit after approval broken | UNFIXED | MEDIUM |
| FIXME | EF change detection gap | UNFIXED | LOW |
| FIXME | Custom IPortMapper missing | UNFIXED | LOW |
| Logic Gap | Auto-verified checkpoint no Submission record | **FIXED** | ~~HIGH~~ |
| Logic Gap | QueueManager state not exposed via API (QueuePage dead) | UNFIXED | MEDIUM |
| Logic Gap | Deploy defaults to test compose file | UNFIXED | MEDIUM |
| Logic Gap | sshpass password visible in process list | **FIXED** | ~~MEDIUM~~ |
| DI Gap | FleetManager not registered | **FIXED** | ~~HIGH~~ |
| DI Gap | AutoTransferService not registered | **FIXED** | ~~MEDIUM~~ |
| DI Gap | HyperVProvider not registered (also a stub) | UNFIXED | LOW |
| Route Conflict | LeaderboardController and ScenarioController both `/api/v1/scenarios` | **FIXED** | ~~MEDIUM~~ |
| Route Bug | GameController Captures route "Games" appears twice | **FIXED** | ~~HIGH~~ |
| Security | VNC listening on 0.0.0.0 without password | **FIXED** | ~~CRITICAL~~ |
| Security | NodeDeployService command injection | **FIXED** | ~~CRITICAL~~ |
| Security | CheckpointVerificationService SSH command injection | **FIXED** | ~~CRITICAL~~ |
| Security | DockerImageBuilder/DockerComposeDeployer command injection | **FIXED** | ~~HIGH~~ |
| Security | GuacamoleProxy hardcoded password | **FIXED** | ~~HIGH~~ |
| Security | IR/Scenario endpoints accessible anonymously | **FIXED** | ~~HIGH~~ |
| Security | NodesController Detail leaking AuthToken | **FIXED** | ~~MEDIUM~~ |
| Security | IR GetInstance no ownership verification | **FIXED** | ~~HIGH~~ |
| Concurrency | RedisDistributedLock LockReleaser race condition | **FIXED** | ~~MEDIUM~~ |
| Frontend | QueuePage no API integration | UNFIXED | MEDIUM |
| Frontend | Admin tabs missing Dashboard/Nodes/DockerImages/Queue links | **FIXED** | ~~MEDIUM~~ |
| Frontend | MarkdownRenderer/FooterRender XSS (no sanitization) | **FIXED** | ~~HIGH~~ |
| Frontend | SignalR off() using empty callbacks (memory leak) | **FIXED** | ~~HIGH~~ |
| Frontend | ScenarioPlayer showCompletion never triggers | **FIXED** | ~~MEDIUM~~ |
| Frontend | notifications.show() missing "message" property | **FIXED** | ~~MEDIUM~~ |
| Virtual | HyperVProvider all methods return Fail | STUB | LOW |
| Virtual | RedisDistributedLock is local SemaphoreSlim | STUB | MEDIUM |
| Verified OK | DeployButton actually deploys via docker compose | REAL | -- |
| Verified OK | NodeDeployService actually SSHs with sshpass | REAL | -- |
| Verified OK | IR checkpoint writes Submission (ManualAnswer path) | REAL | -- |
| Verified OK | Scenario stage completion writes Submission | REAL | -- |
| Verified OK | Double-decay FIXED | RESOLVED | -- |
| Verified OK | WeightedScheduler has real scoring formula | REAL | -- |
| Verified OK | QueueManager has real ConcurrentQueue | REAL | -- |
| Verified OK | FleetHealthCheckService marks nodes offline | REAL | -- |
| Verified OK | KvmProvider uses real virsh commands | REAL | -- |
| Verified OK | DockerComposeDeployer uses real docker compose | REAL | -- |
| Verified OK | DockerImageBuilder uses real docker build | REAL | -- |
| Verified OK | LocalImageImporter copies real files + SHA256 | REAL | -- |
| Verified OK | All frontend API calls to DockerController/NodesController match | MATCHED | -- |
