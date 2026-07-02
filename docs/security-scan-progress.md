# Security Scan Progress

Date: 2026-07-02
Scope: whole repository, with focus on role/permission management, admin APIs, training, game access, scoreboard/export, node/image/container proxy surfaces.

## Runtime Notes

- Codex Security deep scan preflight was incomplete because the runtime could not confirm six usable worker slots.
- Standard repository scan preflight had only the same worker-slot warning; manual/terminal workflow continues.
- Codex Security workbench launch failed on Windows GBK decoding of Git commit metadata, so findings are recorded here and in final response.
- CodeGraph is healthy: 964 indexed files, 27019 nodes, 78817 edges.
- Repository worktree is dirty from previous development; this scan must not revert or edit existing business code.

## Threat Model Summary

- High-value assets: user roles, admin operations, challenge flags, training answers, private files/images, node tokens, registry credentials, container/vm runtime state, scoreboard/submission data.
- Main trust boundaries: anonymous vs authenticated users, student/teacher/admin/superadmin role levels, teacher-owned course/group boundaries, participant vs non-participant game access, public container proxy vs internal node APIs, uploaded archive/file boundaries.
- Highest-risk failure classes: authorization bypass, cross-role privilege escalation, cross-course/group data access, unauthenticated internal mapping exposure, unsafe local file/archive handling, server-side registry/image operations reachable by teachers.

## Confirmed / High-Confidence Candidates

### CAND-001: Teacher/admin batch user import can reset existing higher-privilege accounts

Status: high-confidence, likely reportable.

Evidence:
- `src/GZCTF/Controllers/AdminController.cs` `AddUsers` checks `RolePolicy.CanAssignRole(currentUser.Role, requestedRole)` before user creation.
- On `DuplicateEmail` / `DuplicateUserName`, it loads the existing account, then calls `userInfo.UpdateUserInfo(user)` and `ResetPasswordAsync(userInfo, code, user.Password)`.
- That duplicate branch does not check whether the existing account's real role is manageable by the actor before updating profile fields and resetting password.
- A teacher can send `AssignedRole=Student` with an admin/superadmin email or username; requested role passes, existing high-privilege account is selected by duplicate lookup, and password reset is executed.

Security impact under validation:
- Likely privilege escalation / account takeover of admin or superadmin from a teacher account, if duplicate email or username is known.
- Also likely denial/account tampering for any visible or non-visible existing account.

Needed validation:
- Identity `CreateAsync` duplicate error path is explicitly consumed by the code.
- `GeneratePasswordResetTokenAsync` + `ResetPasswordAsync` is intentionally the admin password reset mechanism elsewhere in `ResetPassword`.
- No rollback occurs on the successful duplicate branch; it proceeds to optional group sync, team handling, transaction commit.
- `CanSyncStudentGroups` returns true for non-student targets, so it does not mitigate high-role target takeover.

Proposed fix:
- In the duplicate branch, after resolving `userInfo`, require `RolePolicy.CanManageRole(currentUser.Role, userInfo.Role)`.
- Also require requested role assignment if the duplicate update ever changes role in future.
- Prefer returning conflict for unmanageable duplicate rather than resetting password.

## Candidate / Review Queue

- Training course admin teacher assignment: check whether course owner teacher can add `Admin`/`SuperAdmin` as course teacher because role check is `teacher.Role < Role.Teacher`, not assignability. Likely not privilege escalation by itself but may leak course contents to admins; probably acceptable or low severity depending product intent.
- Training module copy from formal game challenge: teacher can copy any `GameChallenge` by id via `/api/admin/training/modules/{moduleId}/challenges/from-game-challenge/{challengeId}`. Need determine whether teachers intentionally have full competition question-bank access.
- Course list/detail visibility: published courses appear in catalog before enrollment. Detail includes only `includeDetail=false` unless can learn/edit/admin; need inspect model serializer to ensure hidden chapters/resources/content really omitted.
- Image template anonymous download endpoint: `/api/v1/image-templates/download/{hash}` has `AllowAnonymous`; need validate hash entropy and whether hashes can leak from lower-privilege APIs.
- Internal mappings endpoint: `InternalController` uses `AllowAnonymous`; need verify token requirement is mandatory in deployed proxy mode and not fail-open when config missing.
- Scoreboard and sheet endpoints: check participant/monitor boundaries and whether non-participants can export or read restricted data.
- Node registration/deploy APIs: check anonymous endpoints and node token bootstrap flow.
- Archive extraction/import-local: check path containment, symlink/hardlink handling, command execution arguments, and teacher reachability.

## Progress Log

- Completed initial CodeGraph overview of `RequirePrivilegeAttribute`, `RolePolicy`, `AdminController`, `StudentGroupAdminController`, `TrainingAdminController`, `TrainingCourseController`.
- Read `PrivilegeAuthentication.cs`, `RolePolicy.cs`, `Enums.cs`, `AdminController.cs`, `StudentGroupAdminController.cs`, `TrainingAdminController.cs`, and large portions of `TrainingCourseController.cs` / `TrainingCourseAdminController.cs`.
- Current active work: complete final report and preserve validated conclusions.

## Interim Review Notes - 2026-07-02

### Training/course authorization pass

- Public course catalog/detail: current code returns only summaries for users who cannot learn/edit; `TrainingCourseModel.FromCourse(... includeDetail=false)` emits empty Chapters/Resources/Challenges. No high-confidence content leak found in this pass.
- Training course enrollment review and teacher management are gated by course edit/manage-teacher checks. No confirmed cross-course enrollment management bypass found in this pass.
- `TrainingCourseAdminController.AddTeacher` allows adding any user with `Role >= Teacher` as course teacher. This is not currently promoted as a vulnerability because admins/superadmins already dominate course visibility; the risk is product/design noise rather than privilege escalation.
- `TrainingAdminController.AddChallengeFromGameChallenge` can copy formal `GameChallenge` data including flags into training exercises by challenge id. This remains a design-sensitive candidate: if teachers are intended to have full question-bank/competition management access, it is acceptable; if formal game challenge ownership is supposed to be isolated, it may leak flags. Needs product decision or ownership model evidence before reporting.

### Game/scoreboard pass

- Normal CTF `GameController.Scoreboard` requires login, game started, and participant acceptance for users below Teacher.
- `ScoreboardSheet` and `SubmissionSheet` are guarded by `RequireMonitor`, which maps to `Role.Teacher`, not ordinary users.
- Theory scoreboard has the same participant-or-teacher boundary.
- Penetration player scoreboard calls `GetContextInfo(... allowTeacherMonitor: true)` and still requires accepted participation for non-teachers. No high-confidence non-participant scoreboard leak found in this pass.

### File/archive and script execution pass

- AWDP checker/exp scripts are stored by teacher-facing AWDP admin APIs and executed on the server through `/bin/sh -c` or `cmd.exe /c` in `AwdpScriptRunner`. This is an intentional privileged competition-operation capability, not a player-triggered injection. It should be documented as requiring trusted teacher/admin operators; otherwise it is an architectural high-risk design boundary.
- AWDP patch uploads are better constrained: tar.gz validation rejects absolute paths, `..`, symlinks, hardlinks, device files and FIFO, requires `update.sh`, limits entry count and decompressed archive size before applying inside the target container.
- VM archive upload/import is weaker: `ArchiveExtractor` shells out to `unzip`/`tar` directly into an image storage subdirectory and later runs `tar -xf` on embedded OVA plus `qemu-img convert` on discovered VMDK/QCOW2. It does not perform per-member zip/tar containment checks, symlink/hardlink rejection, or decompressed-size limits before extraction. Reachability is Teacher via global image template upload and editable course teacher via course image upload. This is a security hardening candidate and may be reportable if teacher accounts are not fully trusted infrastructure operators.
- Local image import has an allowed-root check in both controller and importer; no immediate path traversal was found, though `Path.GetFullPath` without resolving symlinks means local filesystem trust still matters for server operators.

### API token / internal endpoint pass

- `/api/internal/port-map` is anonymous at routing level but fail-closed: it requires a valid API token, admin session, or configured Nginx sync bearer token using fixed-time comparison for the configured token.
- `/api/v1/image-templates/download/{hash}` is anonymous at routing level but requires either admin session or `nodeId` plus the matching node auth token. No direct unauthenticated image download path found in this pass.
- Node heartbeat requires the node bearer token. Node token equality uses plain string comparison; this is a low-risk hardening issue rather than a high-confidence exploitable finding in this context.

### Fleet/container proxy pass

- Weighted scheduler respects node online status, `IsSchedulable`, capability and capacity. It returns no node when all schedulable Docker nodes are disabled/offline/exhausted.
- `FleetContainerManager.CreateContainerAsync` returns null when there are no online nodes. If a remote node was scheduled but agent creation fails, it calls `TryCreateLocalFallback`, which can create on a local node if that local node is online, schedulable, and has Docker capacity. This may violate deployment policy when operators expect the main server not to host workloads, but it still checks `IsSchedulable`; classify as policy/availability risk rather than a direct auth bypass.
- Nginx proxy port allocation uses Redis when Nginx proxy mode is enabled and refuses local fallback if Redis is unavailable. This avoids duplicate public ports in distributed mode.
- Nginx stream config writes only mappings within the configured listen range and tests config before reload. It uses process argument lists for `nginx -t` / reload. No command injection found there.
- Penetration fabric commands shell out on Linux hosts, but generated parameters are shell-quoted and upstream config validation checks CIDR format, subnet containment, overlap, and static-IP membership. No direct command injection found in this pass. Residual risk: strict IP/CIDR validation should remain mandatory before any deploy path; direct DB tampering would bypass it.

### Teacher permission surface pass

- `EditController` is class-gated by `RequireTeacher`; teachers can create/edit/export games, access game hash salt, read challenge edit details, and load flags for non-dynamic-container challenges. This matches the current broad teacher permission model described by the product direction, but there is no per-game ownership boundary. If future policy expects teachers to manage only their own games/question banks, this controller needs an ownership layer.
- Global `ImageTemplateController` is also teacher-gated for VM image upload/import, Docker image registry registration and Docker archive upload. This gives teachers infrastructure-affecting capabilities. Treat this as trusted-operator design unless teacher accounts are intended to be low-trust course authors.
- Course-scoped image template upload/import exists for editable course teachers as well; it can still invoke VM archive extraction and Docker registry import under the server account.

## Final Scan Closure - 2026-07-02

Final report written:
- `docs/security-scan-report.md`

Disposition summary:
- `SEC-001` remains confirmed/high-confidence: batch user import duplicate branch can reset passwords of higher-privilege existing accounts because it validates only requested role, not target account role.
- `ArchiveExtractor` is recorded as high-priority hardening / conditional security issue, not unconditional confirmed exploit, because exploitability depends on archive tool behavior and Teacher trust level.
- Teacher-level broad competition/image/training authority is recorded as a product boundary decision, not a vulnerability under the current stated role model.
- Internal port-map, image download, scoreboard/export, training catalog/detail, student groups, and penetration player scoreboard were reviewed and not promoted.

Important limitation:
- This is not a completed six-worker Codex Security Deep Scan. Codex Security app setup failed on Windows Git metadata decoding and the strict deep-scan worker contract was not satisfied. The report is a manual enhanced security scan using CodeGraph and direct source review.

## Continuation Pass - 2026-07-02

User clarification:
- The standalone IR module should already be removed as a separate product module and folded into normal CTF challenge types. Audit must explicitly check for abandoned IR/Scenario surfaces.

### Legacy IR / Scenario module audit

Finding status: high-confidence abandoned active surface, not classified as a direct auth bypass by itself.

Evidence:
- Backend MVC still maps all controllers through `app.MapControllers()` in `src/GZCTF/Extensions/Startup/AppExtensions.cs:109`.
- Standalone old controllers still exist with concrete routes:
  - `src/GZCTF/Controllers/IRChallengeController.cs:21` route `api/v1/ir-challenges`
  - `src/GZCTF/Controllers/ScenarioController.cs:26` route `api/v1/scenarios`
  - `src/GZCTF/Controllers/TimeSlotController.cs:17` route `api/v1/scenarios`
  - `src/GZCTF/Controllers/LeaderboardController.cs:16` route `api/v1/scenarios`
- Player-reachable legacy endpoints remain:
  - IR instance create/status/submit/reset: `IRChallengeController.cs:326`, `508`, `538`, `675`
  - Scenario instance create/status/submit: `ScenarioController.cs:325`, `466`, `500`
  - Scenario time slots: `TimeSlotController.cs:47`, `98`
  - Legacy scenario/IR leaderboard: `LeaderboardController.cs:45`
- Admin CRUD for legacy IR/Scenario remains reachable to Admin:
  - `IRChallengeController.cs:77`, `137`, `180`, `207`, `277`
  - `ScenarioController.cs:61`, `143`, `181`, `213`, `256`, `296`
- Frontend uses `vite-plugin-pages` with `Pages({ dirs: [{ dir: './src/pages', baseRoute: '', filePattern: '**/*.tsx' }] })` in `src/GZCTF/ClientApp/vite.config.mts:72`.
  Therefore files under `src/GZCTF/ClientApp/src/pages/admin/ir-challenges/*` and `src/GZCTF/ClientApp/src/pages/admin/scenarios/*` become routable pages even though the current admin tab no longer lists them.
- Legacy player pages remain:
  - `src/GZCTF/ClientApp/src/pages/game/IRChallengePlayer.tsx`
  - `src/GZCTF/ClientApp/src/pages/game/ScenarioPlayer.tsx`
- Legacy services/models remain registered or active:
  - `EnvironmentService`, `LeaderboardService`, `CheckpointVerificationService` registrations in `src/GZCTF/Extensions/Startup/ServicesExtension.cs:129-134`
  - `ScenarioHub` class exists, while startup currently maps only `/hub/user`, `/hub/monitor`, `/hub/admin`; legacy frontend uses `/hubs/scenario`, making this path at least partially broken/stale.
  - `ChallengeType.Scenario` and `ChallengeType.IRChallenge` remain in `src/GZCTF/Utils/Enums.cs:585` and `591`.

Assessment:
- This is not "deleted". It is an active abandoned module surface.
- Current permission checks on old routes are not obviously unauthenticated bypasses: admin CRUD is admin-gated, player routes require accepted participation for non-teachers.
- The security/product risk is the stale second implementation of instance lifecycle, scoring, time slots, environment creation, SignalR, and submission handling that no longer matches the current "IR as normal CTF type" direction.
- Recommended fix direction if product confirms deprecation: remove frontend pages/components/services and either delete controllers/services/models in a migration-aware way, or hard-disable old routes with explicit `410 Gone`/feature flag. Do not silently leave file-route pages.

### Team membership audit

Candidate `TEAM-001`: invitation-code join bypasses locked-team active-game freeze.
- Evidence: `TeamController.Accept` validates invitation token then adds `team.Members.Add(user!)`; it does not check `team.Locked && await teamRepository.AnyActiveGame(team, token)`.
- Negative control: `KickUser`, `Leave`, and `DeleteTeam` perform the locked active-game check.
- Impact: a locked team in an active game can still accept new members through leaked/shared invitation code, violating membership freeze/fairness.
- Confidence: high.

Candidate `TEAM-002`: kicking a member removes current captain participation rows instead of the kicked member.
- Evidence: `TeamController.KickUser` resolves `kickUser`, removes it from `team.Members`, then calls `participationRepository.RemoveUserParticipations(user, team, token)` where `user` is the current captain, not `kickUser`.
- Sink: `ParticipationRepository.RemoveUserParticipations(UserInfo user, Team team)` deletes rows matching that user and team.
- Impact: incorrect participation cleanup; may leave kicked user's participation rows and remove captain rows.
- Confidence: high for bug, security impact depends on downstream participation checks.

### Transfer import audit update

- `PosterHash` and local `AttachmentSection.Hash` have `[RegularExpression(@"^[a-fA-F0-9]{64}$")]`.
- `TransferValidator.ValidateRecursive` validates nested collection/object properties, so manifest references to local files are not currently promoted as a path traversal in `ImportFileAsync`.
- `ZipFile.ExtractToDirectoryAsync` still extracts before manifest validation and before file-count/size limits. Keep this as archive resource-control hardening rather than confirmed arbitrary file write without runtime proof.

### Submission / legacy scoring audit update

- Normal CTF flag submission uses `GameController.Submit`.
- Legacy `SubmissionController` uses `ScoringRule`, `ScenarioHub`, and scenario stage fallback and is only referenced by legacy scenario/IR frontend components and admin review pages.
- It enforces accepted participation through `ValidateSubmissionContextAsync`, but it is another stale scoring/submission path that should be removed or disabled together with old IR/Scenario if those modules are deprecated.

### Node / proxy audit update

- `NodesController` management routes are admin-gated.
- Heartbeat and image download node-token checks use ordinary string comparison rather than fixed-time comparison; classify as low-risk hardening, not practical standalone exploit.
- `ProxyController` is `[Authorize]` and validates container existence, but `ValidateContainer` only checks container existence through repository.

Candidate `SEC-002`: platform WebSocket container proxy lacks object-level ownership authorization.
- Evidence:
  - `ProxyController.ProxyForInstance` requires only `[Authorize]`, then calls `ValidateContainer(id)` and `GetContainerWithInstanceById(id)`.
  - `ProxyController.ProxyForNoInstance` also only has class-level `[Authorize]`; despite the comment "for admins", it lacks `[RequireTeacher]`/`[RequireAdmin]`.
  - `ContainerRepository.ValidateContainer` is `Context.Containers.AnyAsync(c => c.Id == guid)`.
  - `GetContainerWithInstanceById` loads `GameInstance.Participation.Team` but the controller never checks whether the current user is in that participation/team.
  - `Container.Entry` returns the raw container GUID when `IsProxy` is true, and `ContainerInfoModel.FromContainer` returns that entry to the legitimate player.
  - Frontend `getProxyUrl` turns that GUID into `ws(s)://host/api/proxy/{guid}`.
  - Test containers are created by `EditController.CreateTestContainer`, returned as `ContainerInfoModel`, and consumed through `/api/proxy/noinst/{guid}`.
- Impact:
  - Any authenticated user who obtains another team's proxy container GUID can connect to that team's challenge container over the platform proxy.
  - Any authenticated user who obtains a test-container GUID can connect to an admin/teacher preview container.
  - GUID guessing is not realistic, but GUID disclosure through screenshots, logs, admin instance pages, browser history, support messages, or team sharing is plausible enough that server-side ownership enforcement is required.
- Recommended fix:
  - In `ProxyForInstance`, after loading the container with instance/participation members, require the current user to be a member of that participation, or require Teacher/Admin monitor role.
  - Tighten `ProxyForNoInstance` with `[RequireTeacher]` or `[RequireAdmin]` and ideally verify the test container is still attached to an editable challenge.

## Remediation Pass - 2026-07-02

User clarification:
- IR is a normal CTF direction with the same product status as Web/Misc, not a standalone module.
- Do not address VM archive hardening or broad Teacher permission redesign in this pass.
- Fix high-confidence issues and low-risk hardening items that can be repaired safely.

Changes applied:
- Fixed `AdminController.AddUsers` duplicate-user branch so an actor must be able to manage the existing target account role before profile update or password reset.
- Fixed `TeamController.KickUser` to remove the kicked user's participation records, not the captain's.
- Added locked-active-game guard to invitation-code join in `TeamController.Accept`.
- Fixed team score-curve lookup in the team page to prefer `team.id` before falling back to team name, preventing team rename from hiding historical score data.
- Tightened platform proxy authorization:
  - normal container proxy now requires same participation membership or Teacher+ role after loading the container instance;
  - no-instance/test proxy now requires Teacher+ and verifies the container is still attached as a challenge test container.
- Removed Docker remote-failure local fallback in `FleetContainerManager`; a remote agent failure now fails the deployment target instead of silently creating on the local node.
- Replaced ordinary node-token equality checks with fixed-time comparison in `NodesController.Heartbeat` and `ImageTemplateController.DownloadByHash`.
- Disabled old standalone IR/Scenario APIs with a controller-level `410 Gone` filter while preserving model/history compatibility.
- Replaced old IR/Scenario admin and player file-routes with redirects and removed now-unreferenced old frontend scenario components/service.

Validation:
- `dotnet build src/GZCTF/GZCTF.csproj --no-restore` passed with 0 warnings and 0 errors.
- `pnpm --dir src/GZCTF/ClientApp check` passed.
- `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter "FullyQualifiedName~TeamManagementTests" --no-restore` compiled but could not run because local Docker/Testcontainers could not connect to `npipe://./pipe/docker_engine`.
- Static residual checks found no remaining `TryCreateLocalFallback`, no remote-to-local fallback message, no `authToken != node.AuthToken`, no `RemoveUserParticipations(user, team)` in `TeamController`, and no frontend source fetches to old `/api/v1/ir-challenges` or `/api/v1/scenarios` routes.

Remaining intentional non-goals:
- VM archive extraction hardening was explicitly deferred.
- Teacher global infrastructure/question-bank authority was explicitly left as current product policy.
- Legacy backend Scenario/IR data types and controllers remain compiled for migration/history compatibility, but old routes are short-circuited with `410 Gone`.
