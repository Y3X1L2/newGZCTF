# YINYU CTF Platform Full Review Agent Brief

> Audience: another agent or review team with little prior project context.  
> Goal: run a rigorous, modular, evidence-driven review of the current platform implementation and produce a verified defect/architecture report.  
> Scope: static review first, targeted runtime verification where needed. Do not blindly trust old docs or previous agent summaries; use current code and runtime behavior as the source of truth.

## 1. Review Objective

The platform has accumulated many large changes: role groups, training, AWDP, Docker/VM fleet scheduling, Nginx/Redis public proxy, TeamLab multi-segment VPN lab, node management, logging, and legacy IR/Scenario cleanup. The review must answer three questions:

1. Does each module implement the intended product behavior correctly and completely?
2. Is each module properly integrated into platform-level infrastructure: authorization, scheduling, queueing, logs, metrics, node/agent communication, cache, frontend routes, and deployment lifecycle?
3. Is the codebase still maintainable: clear responsibilities, no obsolete dead paths, no duplicate architecture, no sensitive data leakage, no "UI-only" features without backend support?

The output must be a professional review report, not a vague issue list. Every finding must include evidence, impact, affected code scope, reproduction or static proof, and a minimal safe repair direction.

## 2. Current Architecture Snapshot

Read these files first to establish the real code map:

- `src/GZCTF/Program.cs`
- `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- `src/GZCTF/Models/AppDbContext.cs`
- `src/GZCTF/Middlewares/PrivilegeAuthentication.cs`
- `src/GZCTF/Utils/RolePolicy.cs`
- `src/GZCTF/ClientApp/src/Api.ts`
- `src/GZCTF.Agent/Program.cs`

Important backend areas:

- Controllers: `src/GZCTF/Controllers`
- Services: `src/GZCTF/Services`
- Agent: `src/GZCTF.Agent`
- EF models: `src/GZCTF/Models/Data`, `src/GZCTF/Models/Request`
- Repositories: `src/GZCTF/Repositories`
- Tests: `src/GZCTF.Test/UnitTests`

Important frontend areas:

- Admin pages: `src/GZCTF/ClientApp/src/pages/admin`
- Player game pages: `src/GZCTF/ClientApp/src/pages/games/[id]`
- Training pages: `src/GZCTF/ClientApp/src/pages/training`
- Shared components: `src/GZCTF/ClientApp/src/components`
- Generated API client: `src/GZCTF/ClientApp/src/Api.ts`
- Custom API wrappers: `src/GZCTF/ClientApp/src/utils`, `src/GZCTF/ClientApp/src/Api`

Relevant design/progress docs, for context only:

- `docs/pentest-vpn-vm-main-architecture.md`
- `docs/pentest-vpn-vm-phase-plan.md`
- `docs/superpowers/plans/2026-07-06-fleet-teamlab-scheduling-optimization.md`
- `docs/logging-coverage-progress.md`
- `docs/teamlab-full-chain-test-progress.md`
- `docs/nebulamind-handoff.md`
- `docs/nginx-redis-port-proxy-usage.md`
- `docs/nginx-redis-merged-deployment.md`

Some older Chinese documents may render incorrectly in PowerShell due to console encoding. Use UTF-8 reads and do not treat mojibake as source content corruption without checking the actual file bytes.

## 3. Mandatory Review Process

The main reviewing agent must not personally skim everything and report directly. Use a multi-agent review process:

1. Dispatch one subagent per review part listed in Section 7.
2. Each subagent must independently inspect code and tests for its part, not only summarize docs.
3. Each subagent must mark every candidate issue as one of:
   - Confirmed
   - Likely but needs runtime verification
   - Not reproduced / insufficient evidence
   - False positive
4. The main agent then dispatches cross-check subagents:
   - Cross-check A: verify all High/Critical findings from backend and security angles.
   - Cross-check B: verify frontend/API integration findings against actual routes and API calls.
   - Cross-check C: verify queue/log/scheduling findings against lifecycle state transitions.
5. A finding may enter the final report only after main-agent validation plus at least one independent cross-check, unless it is a low-risk code-quality observation clearly supported by static evidence.
6. Final report must distinguish:
   - Defects confirmed by code and/or runtime evidence.
   - Architectural risks with strong evidence but no immediate reproduction.
   - Open questions requiring product decision or real environment access.

Do not inflate severity. Do not hide uncertainty. Do not report a vulnerability unless it is plausibly exploitable or creates a real data/control-plane breach.

## 4. Evidence Standard

Every final finding must include:

- Title.
- Severity: Critical / High / Medium / Low / Info.
- Category: correctness, security, architecture, reliability, performance, UX-blocking, observability, maintainability.
- Affected module.
- Exact file paths and relevant symbols/routes.
- Evidence:
  - Static proof: code path, state transition, missing branch, missing authorization, missing log, wrong query, unsafe concurrency, stale API contract.
  - Runtime proof if used: request, response, log line, database state, network state, screenshot, or command output summary.
- Impact:
  - What user/admin/player sees.
  - What data or resource can be corrupted, leaked, stuck, or misrepresented.
  - Whether it affects single-team, multi-team, multi-node, or high-concurrency cases.
- Repair direction:
  - Minimal safe fix.
  - Tests needed.
  - Any migration/ops implications.

Reject these weak findings:

- "May be wrong" without a concrete path.
- "Could be vulnerable" without attacker capability and impact.
- UI style opinions unless they block use or contradict explicit product requirements.
- Findings based only on historical docs when current code has changed.

## 5. Global Review Rules

- Use CodeGraph for structural lookup before grep. Use grep for literal strings only.
- Prefer current source and tests over old docs.
- Verify frontend and backend together. A button is not a feature unless the backend route, permission, model, and state transition exist.
- Check both success and failure paths. Many platform defects appear only on failed deploy, destroy, retry, cancelled queue ticket, stale node, or permission denial.
- Check logging for lifecycle completeness and sensitive data exclusion.
- Check consistency across Docker, VM, TeamLab, training, ordinary CTF, and AWDP. Similar operations should not have unrelated behavior.
- Watch for legacy residue: independent Scenario/IR modules are deprecated; IR should be treated as a normal CTF category/branch where applicable.
- Do not recommend broad rewrites unless a module has duplicate conflicting architectures. Prefer focused repairs with clear ownership boundaries.

## 6. Severity Rubric

Critical:

- Unauthorized privilege escalation, cross-team data/control access, flag/private key/credential leakage, destructive operation across teams/nodes, or TeamLab isolation bypass.
- Resource lifecycle bug that can break all deployment, exhaust nodes permanently, or corrupt scoring.

High:

- Major business flow blocked: deploy/start/stop/reset/submit cannot complete in valid conditions.
- Multi-node scheduler overbooks or ignores capacity under concurrent requests.
- Logs omit or misrepresent critical deployment/security/audit events.
- Player can access internal networks or resources beyond intended TeamLab entry segment.

Medium:

- Important feature incomplete but with workaround.
- Error handling hides actionable cause as generic `common.error.encountered`.
- Admin page state is stale or requires full refresh for normal operation.
- Queue status, node status, or port usage is materially misleading.

Low:

- Non-blocking UI mismatch, confusing labels, minor missing filters, incomplete low-frequency logs.

Info:

- Code style, cleanup, docs mismatch, test gaps without direct user impact.

## 7. Modular Review Assignments

### Part A: Authorization, Roles, and Admin Access Control

Primary code:

- `src/GZCTF/Utils/RolePolicy.cs`
- `src/GZCTF/Middlewares/PrivilegeAuthentication.cs`
- `src/GZCTF/Controllers/AdminController.cs`
- `src/GZCTF/Controllers/StudentGroupAdminController.cs`
- `src/GZCTF/Controllers/TrainingAdminController.cs`
- `src/GZCTF/Controllers/TrainingCourseAdminController.cs`
- `src/GZCTF/ClientApp/src/components/WithRole.tsx`
- `src/GZCTF/ClientApp/src/components/admin/WithAdminTab.tsx`
- `src/GZCTF/ClientApp/src/pages/admin/Users.tsx`
- `src/GZCTF/ClientApp/src/components/admin/UserEditModal.tsx`

Review goals:

- Confirm role hierarchy: Student < Teacher < Admin < SuperAdmin.
- Confirm teacher/admin/superadmin visibility and mutability rules match backend and frontend.
- Confirm no admin-only route is only frontend-hidden but backend-open.
- Confirm teacher can only see/manage allowed student groups and student users.
- Confirm user management filters do not leak peer/higher roles.
- Confirm forced navigation for insufficient role is clean and consistent.
- Explain "IP" in user management: `UserInfo.IP` is the recent remote IP updated by `UserInfo.UpdateUserInfo(HttpContext)`. Review whether label, visibility, and privacy are appropriate.

Known suspicion points:

- Frontend role maps include legacy `Role.User` and `Role.Monitor`; verify generated API enum and backend enum compatibility.
- `AdminController.Users`, `SearchUsers`, `UserInfo`, user update/delete/reset endpoints must all apply equivalent role filtering.
- Admin tab visibility and backend `[RequireAdmin]` may not align with teacher-level access if teachers are expected to access some admin pages.

Required output:

- Matrix of each role vs each admin tab and backend route.
- List of any route whose backend permission is broader/narrower than UI.
- Findings for unclear IP field and recommended wording/placement.

### Part B: Deployment Queue, Fleet Scheduling, Capacity, and Concurrency

Primary code:

- `src/GZCTF/Models/Data/DeploymentQueueTicket.cs`
- `src/GZCTF/Services/Fleet/DeploymentQueueService.cs`
- `src/GZCTF/Services/Fleet/DeploymentExecutionService.cs`
- `src/GZCTF/Services/Fleet/QueueManager.cs`
- `src/GZCTF/Services/Fleet/QueueProcessingService.cs`
- `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs`
- `src/GZCTF/Services/Fleet/NodeExecutionGate.cs`
- `src/GZCTF/Services/Fleet/FleetManager.cs`
- `src/GZCTF/Services/Fleet/FleetContainerManager.cs`
- `src/GZCTF/Services/Fleet/FleetVmService.cs`
- `src/GZCTF/Services/Fleet/WeightedScheduler.cs`
- `src/GZCTF/Controllers/NodesController.cs`
- `src/GZCTF/ClientApp/src/pages/admin/queue/Index.tsx`

Review goals:

- Confirm Docker, VM, TeamLab all use durable queue semantics when capacity is unavailable.
- Confirm capacity is reserved before slow create and released exactly once on failure, cancel, destroy, or disappeared ticket.
- Confirm high concurrency cannot assign many simultaneous requests to a seemingly idle node and overbook it.
- Confirm queue processing is parallel across nodes and bounded per node.
- Confirm per-team/per-user deployment limits count active queued tickets.
- Confirm queue position shown to user/admin is meaningful and stable.
- Confirm no raw payload, flags, registry auth, WireGuard private keys, or container environment variables are exposed in queue APIs/logs.

Boundary cases to test/reason:

- Team count greater than schedulable node count.
- Node count zero / all nodes unschedulable / only non-KVM nodes for VM request.
- Node goes offline after reservation but before agent create.
- Agent create succeeds but proxy mapping fails.
- Destroy succeeds but capacity release hits EF concurrency conflict.
- Cancel while pending vs cancel while creating.
- Queue ticket duplicated by rapid double-click.
- Process restart with pending/creating tickets.
- Redis unavailable in Fleet mode.

Required output:

- State-machine diagram or table for `DeploymentQueueTicket` and `DeploymentTarget`.
- Findings where state can get stuck or counters can go wrong.
- Test gap list for concurrency and failure injection.

### Part C: System Logs, Deployment Logs, and Audit Observability

Primary code:

- `src/GZCTF/Extensions/DatabaseSinkExtension.cs`
- `src/GZCTF/Extensions/SignalRSinkExtension.cs`
- `src/GZCTF/Utils/LogHelper.cs`
- `src/GZCTF/Services/Fleet/DeploymentTargetLogHelper.cs`
- `src/GZCTF/Services/AuditLogService.cs`
- `src/GZCTF/ClientApp/src/pages/admin/Logs.tsx`
- All lifecycle controllers/services for Docker, VM, TeamLab, training, images, node registration, queue, AWDP.

Review goals:

- Build a lifecycle event coverage table:
  - request accepted
  - validation failed
  - queued
  - assigned
  - creating
  - success
  - failed
  - cancelled
  - destroy requested
  - destroy success/failure
  - cleanup pending
  - retry
- Confirm each critical event reaches admin-visible system logs where product requires it, not only regular service logs.
- Confirm TeamLab runtime events and global system logs are bridged enough for administrators.
- Confirm deployment queue and node events are visible without leaking sensitive payload.
- Confirm logs have useful actor, node, resource id, team/game, status, and sanitized error.
- Confirm Admin Logs page is usable: live updates, pagination, level filter, IP/user meaning, no noisy console logs.

Known suspicion points:

- User complaint: "部署队列和系统日志接入存在严重问题，非常多事件都没有被记录."
- Current code has both `SystemLog` and plain `LogInformation`; determine which events are actually visible in admin log UI.
- `AuditLogService` still says Scenario/IR in comments; determine whether it is dead code or misleading residue.

Required output:

- Event coverage matrix by module.
- List of missing admin-visible events.
- Sensitive-data scan result.
- Recommendation for a unified audit/event helper if duplication is causing gaps.

### Part D: Node Management, Agent Integration, Nginx/Redis Proxy, and Port Pool

Primary code:

- `src/GZCTF/Controllers/NodesController.cs`
- `src/GZCTF/Services/Fleet/NodeDeployService.cs`
- `src/GZCTF/Services/Fleet/AgentClient.cs`
- `src/GZCTF/Services/Fleet/NginxSyncService.cs`
- `src/GZCTF/Services/Fleet/PortAllocationService.cs`
- `src/GZCTF/Services/Fleet/PortLeaseRefreshService.cs`
- `src/GZCTF/Services/Fleet/HealthCheckService.cs`
- `src/GZCTF/Services/Fleet/LocalNodeRegistrar.cs`
- `src/GZCTF.Agent/Controllers/*`
- `src/GZCTF.Agent/Services/*`
- `src/GZCTF/ClientApp/src/pages/admin/nodes/Index.tsx`
- `src/GZCTF/ClientApp/src/pages/admin/nodes/[id]/Detail.tsx`

Review goals:

- Confirm node registration, heartbeat, capability detection, schedule enable/disable, capacity limits, Docker/KVM/TeamLab capability are coherent.
- Confirm node page does not refresh unnecessarily or lose expanded/selected-node state.
- Confirm node resource list correctly separates running vs history, Docker vs VM, and paginates/sorts correctly.
- Confirm port pool display reflects actual public proxy allocation facts, not hardcoded or stale configuration.
- Confirm Docker public proxy uses Nginx/Redis path, not deprecated FRP path.
- Confirm Windows VM forwarding path is not accidentally changed by Docker proxy fixes.
- Confirm storage registry and image distribution assumptions are current.

Boundary cases:

- Public gateway configured but Redis unavailable.
- Active mapping exists but Nginx sync fails.
- Port lease expires while container still runs.
- Node deregistration with active queued tickets and running resources.
- Node capability changed after registration.
- Node has Docker but no KVM; TeamLab requiring VM should not schedule there.

Required output:

- Data-flow diagram for Docker public proxy and VM access.
- UI state defects with exact component cause if found.
- Port pool truth-source analysis.

### Part E: TeamLab Multi-Segment VPN Lab

Primary code:

- `src/GZCTF/Controllers/TeamLabAdminController.cs`
- `src/GZCTF/Controllers/PenetrationAdminController.cs`
- `src/GZCTF/Controllers/PenetrationPlayerController.cs`
- `src/GZCTF/Services/TeamLab/*`
- `src/GZCTF/Services/PenetrationService.cs`
- `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
- `src/GZCTF.Agent/Models/TeamLabModels.cs`
- `src/GZCTF/Models/Data/TeamLabEntities.cs`
- `src/GZCTF/Models/Data/PenetrationEntities.cs`
- `src/GZCTF/ClientApp/src/pages/admin/games/[id]/Penetration.tsx`
- `src/GZCTF/ClientApp/src/pages/games/[id]/Penetration.tsx`

Review goals:

- Confirm design is now WireGuard entry into team internal network, not public TCP entry per service.
- Confirm player VPN `AllowedIPs` only exposes intended entry segment and does not directly route all internal segments unless product explicitly requires it.
- Confirm router namespace and forwarding rules enforce intended segment reachability.
- Confirm Docker assets are attached to LabNetwork correctly with stable IP/MAC and no accidental host reachability.
- Confirm VM assets, if supported, attach to LabNetwork bridge instead of default NAT for TeamLab mode.
- Confirm TeamLab deploy/reset/destroy are atomic enough: partial failure must not open environment as running.
- Confirm generated player UI hides engineering-only fields: security domain, topology, route, "entry target", module ids, raw internal mapping.
- Confirm admin UI exposes enough plan/validation/deploy events for debugging.
- Confirm old penetration/fabric/attack-graph/fog/topology remnants do not mislead current product.

Boundary cases:

- TeamLab node without TeamLabNetwork enabled.
- Worker tunnel unhealthy.
- Public UDP port unavailable.
- Multiple teams deployed on same WorkerNode.
- Destroy leaves bridge, namespace, veth, wg peer, route, iptables/nft rules.
- Player imports stale VPN config after peer reset.
- Runtime is reset while submissions exist; score and progress must remain stable.

Required output:

- Current implemented topology model summary.
- Gap list against `docs/pentest-vpn-vm-main-architecture.md` and current product decisions.
- Isolation proof checklist and any missing tests.

### Part F: Docker and VM Environment Templates / Runtime Lifecycle

Primary code:

- `src/GZCTF/Controllers/ImageTemplateController.cs`
- `src/GZCTF/Controllers/GameController.cs`
- `src/GZCTF/Repositories/GameInstanceRepository.cs`
- `src/GZCTF/Repositories/ExerciseInstanceRepository.cs`
- `src/GZCTF/Services/Container/*`
- `src/GZCTF/Services/Fleet/FleetContainerManager.cs`
- `src/GZCTF/Services/Fleet/FleetVmService.cs`
- `src/GZCTF/Services/Fleet/VmReadyService.cs`
- `src/GZCTF/Services/Vm/*`
- `src/GZCTF.Agent/Services/DockerService.cs`
- `src/GZCTF.Agent/Services/KvmService.cs`
- `src/GZCTF/ClientApp/src/pages/admin/images/Index.tsx`
- Challenge edit pages under `src/GZCTF/ClientApp/src/pages/admin/games/[id]/challenges`

Review goals:

- Confirm challenge creation enforces valid image binding where required.
- Confirm Docker and Windows/Linux VM template selectors are mutually correct: selecting Windows/VM type should not force Docker image selection.
- Confirm image upload/import/delete/distribution state is visible and logged.
- Confirm VM access URLs include correct token/team context where required.
- Confirm create/destroy/restart lifecycle updates DB state, node capacity, public proxy, and UI button state.
- Confirm failure messages identify missing image/template/capability instead of generic error.

Boundary cases:

- Image template deleted while challenge references it.
- Docker image exists on registry but not on worker.
- VM template path missing on worker.
- VM started but IP never appears.
- Destroy called twice.
- Start button double-click.

Required output:

- Template type vs UI fields matrix.
- Lifecycle state audit for Docker and VM.
- Findings around stale/invalid challenges.

### Part G: Ordinary CTF, Teams, Submissions, Scoreboard, and Screens

Primary code:

- `src/GZCTF/Controllers/GameController.cs`
- `src/GZCTF/Controllers/TeamController.cs`
- `src/GZCTF/Controllers/SubmissionController.cs`
- `src/GZCTF/Repositories/GameRepository.cs`
- `src/GZCTF/Repositories/TeamRepository.cs`
- `src/GZCTF/Repositories/SubmissionRepository.cs`
- `src/GZCTF/Services/ScoringService.cs`
- `src/GZCTF/Services/LeaderboardService.cs`
- `src/GZCTF/ClientApp/src/pages/Teams.tsx`
- `src/GZCTF/ClientApp/src/pages/games/[id]/*`
- `src/GZCTF/ClientApp/src/pages/admin/games/[id]/Screen/*`

Review goals:

- Confirm team rename does not lose score/participation/submission state.
- Confirm invitation copy works reliably and UI state is centered/consistent for captain/member.
- Confirm team join request, approval, kick, transfer captain, invite token, and game participation flows are logged where important.
- Confirm scoreboard/data screen counts submissions exactly once and does not duplicate logs.
- Confirm scoreboard/export permissions match game visibility and participation rules.
- Confirm stale cached scoreboard is invalidated on score-affecting changes.
- Confirm player challenge UI only shows intended fields and valid environment entry info.

Boundary cases:

- Team name changed during active game.
- User belongs to multiple teams.
- Team captain leaves or is kicked.
- Pending join request duplicate.
- Submission after game end or before start.
- Accepted duplicate flag by same team.
- Scoreboard access by non-participant.

Required output:

- Team identity vs score identity analysis.
- Submission-to-scoreboard event chain.
- List of cache invalidation points.

### Part H: Training Platform

Primary code:

- `src/GZCTF/Controllers/TrainingCourseController.cs`
- `src/GZCTF/Controllers/TrainingCourseAdminController.cs`
- `src/GZCTF/Controllers/TrainingAdminController.cs`
- `src/GZCTF/Controllers/StudentGroupAdminController.cs`
- `src/GZCTF/Models/Data/Training.cs`
- `src/GZCTF/Models/Data/StudentGroup.cs`
- `src/GZCTF/Models/Request/Training/*`
- `src/GZCTF/ClientApp/src/pages/training/*`
- `src/GZCTF/ClientApp/src/pages/admin/training.tsx`
- `src/GZCTF/ClientApp/src/components/training/*`
- `src/GZCTF/ClientApp/src/utils/TrainingApi.ts`

Review goals:

- Confirm student group visibility: admin sees all, teacher sees own groups, student sees own learning.
- Confirm course enrollment approval flow is enforced by backend and reflected in UI.
- Confirm draft/publish/archive lifecycle works and archived courses disappear or show according to spec.
- Confirm course progress, chapter completion, CTF challenge submission, theory paper submission, wrong-answer review, standard answer, and explanation are complete.
- Confirm check-in calendar uses real dates/months, timezone is consistent, and streak/cumulative count are correct.
- Confirm training Docker containers join scheduling/queue/logging paths where applicable.
- Confirm frontend has no non-formal explanatory copy that should not appear in product UI.

Known suspicion points:

- Prior reports mentioned: after completing theory test, only total score was shown; missing wrong list, personal wrong answers, correct answers, and explanations.
- Passing-line input may auto-fill 60 when empty, blocking deletion.
- Training management entry was previously removed/reintroduced several times; confirm final expected route visibility.

Required output:

- Training permission matrix.
- Course/chapter/theory/CTF lifecycle diagram.
- Check-in data correctness audit.

### Part I: AWDP and Data Visualization / Large Screen

Primary code:

- `src/GZCTF/Controllers/AwdpAdminController.cs`
- `src/GZCTF/Controllers/AwdpPlayerController.cs`
- `src/GZCTF/Services/Awdp*`
- `src/GZCTF/ClientApp/src/pages/admin/games/[id]/AwdServices.tsx`
- `src/GZCTF/ClientApp/src/pages/games/[id]/Awd.tsx`
- `src/GZCTF/ClientApp/src/pages/admin/games/[id]/Screen/*`
- `src/GZCTF/ClientApp/src/components/screen/*`
- `src/GZCTF/ClientApp/src/utils/screenDemoData.ts`

Review goals:

- Confirm AWDP scoring, attack/defense/patch status, checker loop, service instance lifecycle, and reset/recovery are coherent.
- Confirm 3D attack visualization is only used for AWDP attack/defense mode if present; ordinary modes should not show irrelevant attack-situation effects.
- Confirm large screen data is real unless explicitly in demo mode.
- Confirm demo data cannot leak into production display accidentally.
- Confirm performance: avoid excessive re-rendering, unbounded animations, and layout jank.

Required output:

- AWDP event-to-score matrix.
- Large-screen data source map.
- Performance risks and easy wins.

### Part J: Legacy IR/Scenario Residue and Dead Code

Primary code:

- `src/GZCTF/Controllers/IRChallengeController.cs`
- `src/GZCTF/Controllers/ScenarioController.cs`
- `src/GZCTF/Controllers/LeaderboardController.cs`
- `src/GZCTF/Controllers/TimeSlotController.cs`
- `src/GZCTF/Services/EnvironmentService.cs`
- `src/GZCTF/Services/CheckpointVerificationService.cs`
- `src/GZCTF/Services/AuditLogService.cs`
- `src/GZCTF/ClientApp/src/pages/admin/ir-challenges/*`
- `src/GZCTF/ClientApp/src/pages/admin/scenarios/*`
- `src/GZCTF/ClientApp/src/pages/game/IRChallengePlayer.tsx`
- `src/GZCTF/ClientApp/src/pages/game/ScenarioPlayer.tsx`
- `src/GZCTF/Utils/RolePolicy.cs`

Review goals:

- Confirm independent IR/Scenario modules are truly disabled or removed according to product decision.
- Confirm IR as a normal CTF branch/category still works where intended.
- Confirm legacy routes do not expose dead admin UI or old APIs.
- Confirm obsolete services are not still registered and running if no longer needed.
- Confirm comments, docs, API paths, frontend exports, and static build artifacts do not mislead future developers.

Required output:

- List of live legacy endpoints and whether they are intentionally blocked by `LegacyFeatureGone`.
- List of registered services that may be dead residue.
- Cleanup recommendation with migration risk.

### Part K: Frontend Architecture, UX Blocking Issues, and API Contract Consistency

Primary code:

- `src/GZCTF/ClientApp/src/pages`
- `src/GZCTF/ClientApp/src/components`
- `src/GZCTF/ClientApp/src/styles`
- `src/GZCTF/ClientApp/src/Api.ts`
- `src/GZCTF/ClientApp/src/Api/*`
- `src/GZCTF/ClientApp/src/utils/*`

Review goals:

- Confirm frontend routes match backend endpoints and generated API client.
- Confirm no important feature is implemented as UI-only placeholder.
- Confirm major pages preserve state and do not trigger unnecessary full refresh.
- Confirm common status/progress/badge styling is centralized enough to avoid repeated regressions.
- Confirm invalid permission access redirects cleanly instead of rough 404s where product expects home redirect.
- Confirm API error handling surfaces actionable backend messages.
- Confirm scroll/overflow behavior does not block function on training, team, node, and wrong-answer pages.

Do not prioritize pure beautification unless it blocks use or contradicts required platform style.

Required output:

- Route/API mismatch list.
- UI state-refresh defects.
- Shared component duplication or style debt that causes recurring bugs.

### Part L: Security and Sensitive Data

Primary code:

- All controllers with `[RequireUser]`, `[RequireMonitor]`, `[RequireAdmin]`, `[Authorize]`, or no attribute.
- `src/GZCTF/Services/Fleet/*`
- `src/GZCTF/Services/TeamLab/*`
- `src/GZCTF/Services/Vm/*`
- `src/GZCTF/Services/DockerImageRegistryService.cs`
- `src/GZCTF/Services/Container/*`
- `src/GZCTF/Extensions/DatabaseSinkExtension.cs`
- `src/GZCTF/Models/Request/*`

Review goals:

- Confirm no route exposes flags, answers, registry credentials, WireGuard private keys, VM tokens, container env, raw deployment payloads, or internal sync tokens.
- Confirm public/internal sync endpoints are authenticated and scope-limited.
- Confirm file upload/import/archive extraction paths defend against path traversal, oversize, unsupported type, and unsafe command invocation.
- Confirm shell command construction in KVM/Docker/registry paths is properly escaped or argument-array based.
- Confirm user/team/game scoping prevents cross-team access.
- Confirm TeamLab isolation is not bypassable by VPN route, host bridge, or public proxy.

Required output:

- Validated security findings only.
- False-positive notes for scary-looking but non-exploitable paths.
- Secret-output scan summary.

## 8. Cross-Module Integration Checks

Each subagent must also answer these integration questions for its module:

1. Does it participate in deployment queue if it creates Docker/VM/TeamLab resources?
2. Does it update node capacity and release it exactly once?
3. Does it write admin-visible logs for important lifecycle events?
4. Does it avoid leaking sensitive data in logs/API/UI?
5. Does it enforce backend authorization independent of frontend hiding?
6. Does it surface actionable errors instead of generic `common.error.encountered`?
7. Does it clean up resources idempotently?
8. Does it have tests covering success, failure, permission denial, and concurrency where relevant?
9. Does frontend display state come from backend state rather than local assumptions?
10. Does it conflict with current product decisions, especially TeamLab VPN design and removal of old attack graph/fog/topology concepts?

## 9. Suggested Static Commands

Run these as needed. Summarize outputs; do not paste huge logs.

```powershell
git status --short
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Fleet|FullyQualifiedName~TeamLab|FullyQualifiedName~DeploymentQueue" --no-restore -p:UseSharedCompilation=false -m:1
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Training|FullyQualifiedName~StudentGroup|FullyQualifiedName~Role" --no-restore -p:UseSharedCompilation=false -m:1
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Awdp|FullyQualifiedName~Game|FullyQualifiedName~Team" --no-restore -p:UseSharedCompilation=false -m:1
dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore -p:UseSharedCompilation=false
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp build
git diff --check
```

Literal searches useful for coverage:

```powershell
rg -n "SystemLog\\(|LogInformation\\(|LogWarning\\(|LogError\\(" src/GZCTF/Controllers src/GZCTF/Services src/GZCTF/Repositories
rg -n "RequireAdmin|RequireUser|RequireMonitor|Authorize|LegacyFeatureGone" src/GZCTF/Controllers
rg -n "flag\\{|PrivateKey|RegistryAuth|EnvironmentVariables|Payload|ProtectedClientPrivateKey" src/GZCTF/Controllers src/GZCTF/Services src/GZCTF/Models
rg -n "scenario|IRChallenge|attack graph|fog|topology|frp" src/GZCTF src/GZCTF/ClientApp/src docs
```

## 10. Suggested Runtime Verification Targets

Only run runtime tests when static evidence is insufficient or a finding depends on actual behavior.

Minimum smoke checks:

- Login as admin, teacher, student.
- Open admin users, nodes, queue, logs, images, training, games.
- Create/destroy one ordinary Docker challenge instance.
- Create/destroy one VM instance if KVM node is available.
- Deploy one TeamLab Docker-only multi-segment environment.
- Download/import player WireGuard config and verify:
  - entry segment reachable;
  - non-entry segment not directly reachable unless routing is unlocked by scenario design;
  - destroy removes access.
- Trigger a known deployment failure and confirm:
  - user sees actionable error/queue state;
  - admin logs show stage and root cause;
  - node capacity is not leaked.
- Submit flag and confirm scoreboard/screen event count increments exactly once.
- Complete training theory test and confirm wrong-answer review data is visible if configured.

## 11. Final Report Format

The final review report must be written to a new file under `docs/`, for example:

`docs/platform-full-review-report-YYYYMMDD.md`

Use this structure:

```markdown
# Platform Full Review Report

## Executive Summary
- Overall risk level:
- Modules reviewed:
- Confirmed Critical/High:
- Main architectural concern:
- Recommended repair order:

## Review Method
- CodeGraph/context sources:
- Subagents dispatched:
- Cross-checks performed:
- Runtime tests performed:
- Limitations:

## Confirmed Findings
### F-001: Title
- Severity:
- Category:
- Module:
- Evidence:
- Impact:
- Repair direction:
- Tests to add:

## Likely Issues Requiring Runtime Verification

## False Positives / Not Reproduced

## Module Coverage Matrix
| Module | Function Correctness | Platform Integration | Logs | Auth | Tests | Status |

## Deployment Queue and Log Coverage Matrix

## Architecture Debt and Cleanup Recommendations

## Suggested Repair Roadmap
```

## 12. Immediate High-Value Review Focus

Start with these because user-visible defects already point there:

1. Deployment queue and system log coverage: verify every resource lifecycle writes admin-visible, sanitized events.
2. Node management UX/state: identify why page refreshes or loses state; verify port/capacity display truth.
3. User management IP: clarify meaning, privacy, label, and whether it is useful in that page.
4. TeamLab multi-segment correctness: verify VPN route scope, router ACL, Docker/VM LabNetwork attachment, reset/destroy cleanup.
5. Permission consistency: teacher/admin/superadmin backend routes vs frontend tabs.
6. Legacy IR/Scenario cleanup: determine live residue and whether it should be fully removed or blocked.
7. Training completion/review: ensure theory wrong-answer and check-in data are real and visible.
8. Ordinary CTF/team/scoreboard: ensure score identity survives team rename and screen logs are not duplicated.

## 13. Quality Bar for Reviewers

The review is not complete until:

- Every module in Section 7 has a subagent result.
- All High/Critical candidates have cross-check evidence.
- Every known user direction in Section 12 is explicitly addressed.
- All findings include exact file paths and concrete behavior.
- The report distinguishes confirmed defects from uncertain risks.
- The report includes repair order, not just raw findings.
- The main agent has read and reconciled all subagent outputs, then independently sanity-checked the final conclusions.

