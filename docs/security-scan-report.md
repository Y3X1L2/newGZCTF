# Security Scan Report - Role / Permission Focus

Date: 2026-07-02
Scope: `D:\newgz\newGZCTF-main`, focused on role and permission management, admin APIs, training/course APIs, game scoreboard/export, node/image/container proxy and upload surfaces.

## Executive Summary

This pass found two high-confidence exploitable authorization/privilege bugs:

- batch user import can reset higher-privilege account passwords
- platform WebSocket container proxy lacks object-level ownership checks

It also found two high-confidence team-membership integrity issues, and one important abandoned-surface issue: standalone IR/Scenario modules are still routable even though the product direction is to merge IR into normal CTF challenge types. The rest of the reviewed permission surfaces generally enforce the current role model, but there are several hardening/design risks around teacher-accessible infrastructure operations and VM archive extraction.

Because the Codex Security app workflow failed in this Windows environment while reading Git metadata and the configured deep-scan worker contract could not be satisfied, this report is a manual enhanced security scan. It should not be represented as a completed six-worker exhaustive Codex Security Deep Scan.

## Confirmed Finding

### SEC-001 - Teacher/Admin Batch Import Can Reset Higher-Privilege Account Passwords

Severity: High
Confidence: High
Affected files:
- `src/GZCTF/Controllers/AdminController.cs:252`
- `src/GZCTF/Models/Data/UserInfo.cs:108`
- `src/GZCTF/Utils/RolePolicy.cs:51`

Root cause:
- `AdminController.AddUsers` validates only the requested role in the import row:
  - `requestedRole = user.AssignedRole ?? Role.Student`
  - `RolePolicy.CanAssignRole(currentUser.Role, requestedRole)`
- If `CreateAsync` fails with `DuplicateEmail` or `DuplicateUserName`, the code loads the existing account by the duplicate identifier.
- It then calls `userInfo.UpdateUserInfo(user)` and `ResetPasswordAsync(userInfo, code, user.Password)` without checking whether the actor may manage the existing account's real role.
- `CanSyncStudentGroups` does not mitigate this path because non-student targets return `true`.

Attack path:
1. Attacker has a Teacher or Admin account with access to user batch import.
2. Attacker submits a row with `AssignedRole=Student`, but uses the known username or email of an Admin/SuperAdmin account.
3. Requested role validation passes because the row asks to create a Student.
4. Identity creation returns `DuplicateEmail` or `DuplicateUserName`.
5. Existing higher-privilege account is loaded and its password is reset to the attacker-provided password.
6. Attacker logs in as the higher-privilege account.

Impact:
- Teacher can likely take over Admin/SuperAdmin if the identifier is known.
- Admin can likely take over SuperAdmin.
- Also allows profile tampering for unmanageable existing accounts.

Minimal safe fix:
- In the duplicate branch, after resolving `userInfo`, require `RolePolicy.CanManageRole(currentUser.Role, userInfo.Role)` before any update or password reset.
- Return `Forbid()` or a conflict-style error for unmanageable duplicates.
- Keep the existing requested-role check; it is still needed for new-account creation.

Suggested patch shape:

```csharp
if (userInfo is null)
{
    await trans.RollbackAsync(token);
    return HandleIdentityError(result.Errors);
}

if (!RolePolicy.CanManageRole(currentUser!.Role, userInfo.Role))
{
    await trans.RollbackAsync(token);
    return Forbid();
}

userInfo.UpdateUserInfo(user);
var code = await userManager.GeneratePasswordResetTokenAsync(userInfo);
await userManager.ResetPasswordAsync(userInfo, code, user.Password);
```

Regression tests to add:
- Teacher importing existing Admin/SuperAdmin username/email must be forbidden and must not change password.
- Admin importing existing SuperAdmin username/email must be forbidden and must not change password.
- Teacher importing existing Student in one of their managed groups should continue to work if that is intended behavior.

### SEC-002 - Platform Container Proxy Does Not Enforce Container Ownership

Severity: High
Confidence: High
Affected files:
- `src/GZCTF/Controllers/ProxyController.cs:63`
- `src/GZCTF/Controllers/ProxyController.cs:126`
- `src/GZCTF/Controllers/ProxyController.cs:318`
- `src/GZCTF/Repositories/ContainerRepository.cs:67`
- `src/GZCTF/Models/Data/Container.cs:92`
- `src/GZCTF/Models/Request/Game/ContainerInfoModel.cs:25`
- `src/GZCTF/ClientApp/src/utils/Shared.tsx:583`

Root cause:
- `ProxyController` is class-gated by `[Authorize]`, but `ProxyForInstance` does not verify that the current user belongs to the `GameInstance.Participation` owning the container.
- `ValidateContainer(id)` only checks existence:
  - `Context.Containers.AnyAsync(c => c.Id == guid)`
- `GetContainerWithInstanceById` loads the owning participation/team, but the controller never uses it for authorization.
- `ProxyForNoInstance` is documented as an admin/test-container proxy, but it also has only the class-level `[Authorize]` and no `[RequireTeacher]` or `[RequireAdmin]`.
- For platform proxy mode, `Container.Entry` returns the container database GUID, `ContainerInfoModel.FromContainer` returns it to the legitimate client, and frontend `getProxyUrl` turns it into `ws(s)://host/api/proxy/{guid}` or `ws(s)://host/api/proxy/noinst/{guid}`.

Attack path:
1. Attacker has any authenticated account.
2. Attacker obtains another team's container GUID or a test-container GUID. This does not require guessing if the GUID appears in a screenshot, copied entry, browser history, admin/support log, or team chat.
3. Attacker opens `ws(s)://<platform>/api/proxy/<guid>`.
4. Server checks only that the container exists and is proxied, then connects the WebSocket to the target container IP/port.
5. Attacker can interact with another team's challenge service or a teacher/admin test container.

Impact:
- Cross-team challenge container access when platform proxy mode is enabled.
- Unauthorized access to teacher/admin preview containers via `/api/proxy/noinst/{guid}` if the GUID leaks.
- This can expose challenge services, flags, internal challenge state, or traffic-capture-relevant interactions depending on the container.

Counterevidence considered:
- GUID values are high entropy and not realistically brute-forceable. That reduces likelihood, but it does not replace server-side object authorization once a GUID is disclosed through normal product workflows.
- The controller requires login. The issue is not anonymous access; it is broken object-level authorization among authenticated users.

Minimal safe fix:
- In `ProxyForInstance`, load the container with `GameInstance.Participation.Members` and require:
  - current user is a member of the owning participation; or
  - current user has monitor/teacher/admin role.
- In `ProxyForNoInstance`, require `[RequireTeacher]` or `[RequireAdmin]`, and preferably verify the container is still attached as a `GameChallenge.TestContainer`.
- Avoid caching a positive `ValidateContainer` result before ownership is checked. If caching remains, key it by user/role/context or cache only existence, not authorization.

Regression tests to add:
- User A cannot connect to User/Team B's proxied container GUID.
- A monitor/teacher/admin can connect only when that is intended.
- Ordinary user cannot connect to `/api/proxy/noinst/{testContainerGuid}`.
- Existing owner can still connect to their own platform proxy container.

### SEC-003 - Invitation Code Join Bypasses Locked-Team Active-Game Freeze

Severity: Medium
Confidence: High
Affected files:
- `src/GZCTF/Controllers/TeamController.cs:526`
- `src/GZCTF/Controllers/TeamController.cs:479`
- `src/GZCTF/Controllers/TeamController.cs:611`
- `src/GZCTF/Controllers/TeamController.cs:713`

Root cause:
- `TeamController.Accept` validates the invite code and appends the current user to `team.Members`.
- It does not check `team.Locked && await teamRepository.AnyActiveGame(team, token)`.
- Other membership-changing routes such as kick, leave, and delete do check the locked active-game condition.

Attack path:
1. A team is locked because it is participating in an active game.
2. A user obtains the team's invite code.
3. The user calls `POST /api/.../Team/Accept`.
4. The user is added to the team even though other membership changes are frozen.

Impact:
- Violates competition membership-freeze rules.
- Can affect fairness, eligibility, writeups, team access, and game participation assumptions.

Minimal safe fix:
- Add the same locked active-game check used by other team mutation routes before `team.Members.Add(user!)`.
- Consider rotating invite tokens or invalidating invite joins when a team is locked for an active competition.

### SEC-004 - Kicking a Member Removes the Captain's Participation Rows Instead of the Kicked Member's

Severity: Medium-Low security, Medium integrity
Confidence: High
Affected files:
- `src/GZCTF/Controllers/TeamController.cs:462`
- `src/GZCTF/Controllers/TeamController.cs:487`
- `src/GZCTF/Repositories/ParticipationRepository.cs:157`

Root cause:
- `KickUser` resolves `kickUser`, removes that user from `team.Members`, but then calls:
  - `participationRepository.RemoveUserParticipations(user, team, token)`
- `user` is the current captain, not the kicked member.
- The repository method deletes rows by the supplied user and team.

Impact:
- The kicked user's participation rows may remain.
- The captain's participation rows may be removed incorrectly.
- Downstream behavior can include incorrect game/team access, scoreboard membership, or participation state corruption depending on how stale rows are consumed.

Minimal safe fix:
- Change the cleanup call to `RemoveUserParticipations(kickUser, team, token)`.
- Add a regression test verifying the kicked member loses participation rows and the captain remains intact.

## High-Priority Hardening / Conditional Security Issues

### HARD-000 - Standalone IR/Scenario Modules Remain Routable After Product Direction Changed

Severity: High-priority cleanup / abandoned attack surface
Confidence: High
Affected files:
- `src/GZCTF/Controllers/IRChallengeController.cs:21`
- `src/GZCTF/Controllers/ScenarioController.cs:26`
- `src/GZCTF/Controllers/TimeSlotController.cs:17`
- `src/GZCTF/Controllers/LeaderboardController.cs:16`
- `src/GZCTF/ClientApp/vite.config.mts:72`
- `src/GZCTF/ClientApp/src/pages/admin/ir-challenges/*`
- `src/GZCTF/ClientApp/src/pages/admin/scenarios/*`
- `src/GZCTF/ClientApp/src/pages/game/IRChallengePlayer.tsx`
- `src/GZCTF/ClientApp/src/pages/game/ScenarioPlayer.tsx`
- `src/GZCTF/Extensions/Startup/ServicesExtension.cs:129`
- `src/GZCTF/Extensions/Startup/ServicesExtension.cs:131`
- `src/GZCTF/Extensions/Startup/ServicesExtension.cs:134`
- `src/GZCTF/Utils/Enums.cs:585`
- `src/GZCTF/Utils/Enums.cs:591`

Evidence:
- Backend MVC maps controllers globally, so old controllers remain active routes.
- Old player endpoints remain:
  - IR instance create/status/submit/reset
  - Scenario instance create/status/submit
  - Scenario time slots
  - Legacy scenario/IR leaderboard
- Vite uses `vite-plugin-pages`; therefore files under `src/pages/admin/ir-challenges` and `src/pages/admin/scenarios` are direct client routes even if hidden from the current admin navigation.
- Legacy player pages and scenario components still call `/api/v1/ir-challenges`, `/api/v1/scenarios`, `/api/v1/submissions`, and `/hubs/scenario`.
- `ScenarioHub` exists, but startup currently maps `/hub/user`, `/hub/monitor`, and `/hub/admin`, not `/hubs/scenario`; this suggests the old flow is stale or partially broken.

Assessment:
- This is not a direct unauthenticated bypass in the reviewed code: old admin CRUD is admin-gated and old player routes check accepted participation for ordinary users.
- It is still a real abandoned attack surface and product consistency risk because it preserves a second implementation of environment lifecycle, scoring, time slots, submissions, and real-time updates after IR was supposed to be part of normal CTF challenge types.

Recommended fix:
- If standalone IR/Scenario is deprecated, remove the frontend route files/components/services and hard-disable or delete the backend controllers/services.
- If data migration risk prevents immediate deletion, return explicit `410 Gone` from old routes behind a feature flag defaulting to disabled.
- Remove `ChallengeType.IRChallenge` and `ChallengeType.Scenario` only after a migration plan for existing rows is defined.
- Do not leave hidden file-routes as "dead" code; with `vite-plugin-pages`, the file path itself is an entry point.

### HARD-001 - Teacher-Reachable VM Archive Extraction Lacks Per-Entry Containment and Resource Controls

Severity: Medium by default, High if Teacher/course-owner accounts are not trusted infrastructure operators
Confidence: Medium-High
Affected files:
- `src/GZCTF/Services/Vm/ArchiveExtractor.cs:34`
- `src/GZCTF/Controllers/ImageTemplateController.cs:372`
- `src/GZCTF/Controllers/TrainingCourseAdminController.cs:1258`

Evidence:
- Teacher-facing global VM archive upload calls `ArchiveExtractor.ExtractAndRegisterAsync`.
- Editable training course teachers can also upload VM archives for a course.
- `ArchiveExtractor` shells out to `unzip` and `tar` directly into a storage subdirectory.
- It does not inspect archive entries before extraction for `..`, absolute paths, symlinks, hardlinks, device files, excessive file count, or decompressed size.
- It later extracts embedded OVA with `tar -xf` and invokes `qemu-img convert` on discovered disk files.

Why this matters:
- The exact exploitability depends on platform `tar`/`unzip` behavior and storage permissions, so this pass does not claim confirmed arbitrary host write.
- However, the upload path crosses from a lower admin role (Teacher/course teacher) into host filesystem and image tooling. That is a strong enough boundary to require deterministic archive validation.

Recommended fix:
- Replace direct extraction with managed archive enumeration where possible.
- Before writing any member, canonicalize destination path and enforce it stays under `extractDir`.
- Reject symlinks, hardlinks, absolute paths, `..`, device files and FIFO entries.
- Enforce max member count, max decompressed bytes, and max nested extraction size for OVA.
- Use `ProcessStartInfo.ArgumentList` for external tools instead of string arguments.
- Clean up `extractDir` on failure, not just temp upload dir.

### HARD-002 - Teacher Role Has Broad Infrastructure and Competition Authoring Powers

Severity: Design risk
Confidence: High
Affected areas:
- `src/GZCTF/Controllers/EditController.cs`
- `src/GZCTF/Controllers/ImageTemplateController.cs`
- `src/GZCTF/Controllers/TrainingAdminController.cs`
- `src/GZCTF/Controllers/TrainingCourseAdminController.cs`

Evidence:
- `EditController` is class-gated by `RequireTeacher`; teachers can create/edit games and read challenge edit details.
- Global image template upload/import/register endpoints are `RequireTeacher`.
- `TrainingAdminController.AddChallengeFromGameChallenge` can copy formal game challenge data and flags into training exercises by challenge id.

Assessment:
- This appears aligned with the current product direction that teachers can manage competitions, question banks, templates and training.
- It is not a vulnerability if Teacher accounts are treated as trusted competition operators.
- If the intended model becomes "teachers can only manage owned courses/games/questions", an ownership boundary must be added across these controllers.

### HARD-003 - Remote Docker Failure Can Fall Back to Local Node if Local Node Is Schedulable

Severity: Policy / isolation risk
Confidence: High
Affected file:
- `src/GZCTF/Services/Fleet/FleetContainerManager.cs:428`

Evidence:
- `TryCreateLocalFallback` selects an online local node with `IsSchedulable` and Docker capacity after remote creation failure.
- This does respect node schedulability, so it is not a scheduling-auth bypass.
- It can still surprise operators who believe the main server will never host workloads.

Recommended fix:
- Make local fallback explicitly configurable, default off for distributed production deployments.
- Log a high-signal event when fallback occurs.

### HARD-004 - Node Bearer Token Comparison Uses Plain String Equality in One Download Path

Severity: Low
Confidence: Medium
Affected file:
- `src/GZCTF/Controllers/ImageTemplateController.cs:463`

Evidence:
- Anonymous image download path requires node bearer token when not admin.
- It compares `authToken != node.AuthToken`.
- Other internal sync token comparison already uses fixed-time comparison.

Recommended fix:
- Normalize this to fixed-time comparison for consistency. This is not currently assessed as a practical exploit by itself.

## Reviewed But Not Promoted

### Internal Nginx Port Map

Files:
- `src/GZCTF/Controllers/InternalController.cs:38`
- `src/GZCTF/Utils/ContextHelper.cs:71`

Assessment:
- Route is `[AllowAnonymous]`, but it fail-closes through valid API token, admin session, or configured Nginx sync bearer token.
- Missing configured sync token returns false, not open.
- Not promoted.

### Image Template Download Endpoint

File:
- `src/GZCTF/Controllers/ImageTemplateController.cs:446`

Assessment:
- Route is anonymous, but non-admin callers must provide `nodeId` and the matching node bearer token.
- No direct unauthenticated image download found in this pass.
- Token comparison hardening is tracked separately as HARD-004.

### Normal CTF Scoreboard and Export

File:
- `src/GZCTF/Controllers/GameController.cs:346`
- `src/GZCTF/Controllers/GameController.cs:905`
- `src/GZCTF/Controllers/GameController.cs:953`

Assessment:
- Scoreboard requires authenticated user and accepted participation for users below Teacher.
- `ScoreboardSheet` and `SubmissionSheet` require `RequireMonitor`, which maps to Teacher or above.
- No ordinary-user scoreboard export bypass found in this pass.

### Training Course Catalog and Detail Visibility

Files:
- `src/GZCTF/Controllers/TrainingCourseController.cs:497`
- `src/GZCTF/Controllers/TrainingCourseController.cs:561`
- `src/GZCTF/Models/Request/Training/CourseModels.cs:858`

Assessment:
- Course list uses visible-course query and returns summaries without detail.
- Detail only includes chapters/resources/challenges when user can learn, edit, or is admin.
- Chapter, resource, challenge, container, and submit endpoints check `CanLearnCourse`.
- No unauthenticated or unenrolled content leak found in this pass.

### Student Group Management

File:
- `src/GZCTF/Controllers/StudentGroupAdminController.cs`

Assessment:
- Teacher sees groups they manage; Admin sees all.
- Adding/removing group members checks group manage permission and student role.
- Only Admin or above can add/remove group managers.
- No confirmed cross-group teacher bypass found in this pass.

### Penetration Player Scoreboard

Files:
- `src/GZCTF/Controllers/PenetrationPlayerController.cs`
- `src/GZCTF/Services/PenetrationService.cs`

Assessment:
- Previous concern about non-participant visibility was reviewed against the service access pattern.
- Non-teacher player paths require accepted participation through context resolution.
- Not promoted in this pass.

### Nginx/Redis Stream Mapping vs Platform WebSocket Proxy

Files:
- `src/GZCTF/Services/Fleet/NginxSyncService.cs`
- `src/GZCTF/Services/Fleet/PortAllocationService.cs`
- `src/GZCTF/Controllers/ProxyController.cs`

Assessment:
- Nginx/Redis stream mapping was reviewed as a distributed port-allocation and sync surface; no direct unauthenticated mapping-read or shell-injection issue was promoted.
- The platform WebSocket proxy is a different path. Its object-level authorization flaw is promoted as `SEC-002`.

## Scan Limitations

- The Codex Security app/workbench path failed in this Windows environment while decoding Git metadata under the system code page.
- The strict Deep Security Scan workflow requires a six-worker discovery loop; that contract was not completed here.
- This report is based on targeted static analysis with CodeGraph plus direct source review.
- No live HTTP exploit reproduction was performed for `SEC-001` through `SEC-004`; the code paths are sufficiently direct to classify as high-confidence static validation, but targeted regression/integration tests should be added during fixes.

## Recommended Immediate Order

1. Fix `SEC-001` first. It is a small controller-level patch with account-takeover impact.
2. Fix `SEC-002` next. Add object-level authorization to `/api/proxy/{guid}` and role-gate `/api/proxy/noinst/{guid}`.
3. Fix `SEC-003` and `SEC-004` together in `TeamController`; both are small, localized team-membership corrections.
4. Disable or delete the standalone IR/Scenario module routes and file-route pages if the product direction is confirmed.
5. Harden `ArchiveExtractor` before relying on Teacher/course-owner VM archive uploads in production.
6. Decide whether Teacher is a trusted infrastructure operator. If not, add ownership and capability boundaries to game/question/image/training admin surfaces.
7. Consider disabling local Docker fallback in distributed deployments unless explicitly requested.
