# CLAUDE.md

This file is the handoff manual for agents working on this repository. Read it before making changes. It records the current architecture, active work, deployment and test flows, coding standards, and module-specific constraints that are easy to lose during context compaction.

## 1. Project Identity

- Project name in product copy: **YINYU CTF Platform**.
- Repository root on this machine: `D:\newgz\newGZCTF-main`.
- Current active development branch at the time this file was recreated: `codex/main-node-pentest-merge`.
- Main product: a GZCTF-derived CTF/training platform with Jeopardy, Theory, AWDP, Mixed, VM/Docker/Fleet node deployment, training/course modules, team management, scoreboard display, and an in-progress commercial-grade Penetration orchestration mode.
- The user expects commercial-grade implementation quality: no fake buttons, no half-wired frontends, no UI claims that backend/network behavior does not enforce.

## 2. Non-Negotiable Working Rules

1. Do not revert user or collaborator changes unless explicitly asked.
2. The worktree is often dirty. Treat existing modifications as intentional and work with them.
3. Do not push, deploy, force reset, or force merge unless the user explicitly asks.
4. Use `apply_patch` for manual file edits.
5. Prefer `rg` / `rg --files` for literal search.
6. Use CodeGraph for structural questions when the `codegraph_*` tools are available. This repo has `.codegraph/` and the project instructions say CodeGraph is authoritative for symbol-level exploration.
7. If CodeGraph tools are not available, fall back to targeted `rg` and file reads.
8. Never store plaintext passwords, API keys, tokens, SSH private keys, or production secrets in committed files. This document may record server addresses, usernames, paths, service names, and safe operational steps, but passwords must be obtained through an operator or secure local secret store.
9. When reading or writing Chinese docs on Windows, use UTF-8 explicitly where possible. Some existing docs render as mojibake if read with the wrong console encoding.
10. Paths containing route segments like `[id]` should be accessed with PowerShell `-LiteralPath` when needed.

## 3. Repository Layout

Important top-level directories:

- `src/GZCTF`: main ASP.NET Core web application and React frontend.
- `src/GZCTF/ClientApp`: Vite/React frontend.
- `src/GZCTF.Agent`: Linux/Fleet agent service used for remote Docker/container operations.
- `src/GZCTF.AppHost`: app host project.
- `src/GZCTF.Test`: unit tests.
- `src/GZCTF.Integration.Test`: integration tests.
- `docs`: design docs, audit reports, deployment docs, execution plans.
- `scripts`: helper scripts.
- `tests`: external test assets.
- `artifacts/vendor/react-bits`: vendored ReactBits reference/components used by previous visual work.
- `docker-compose.yml`: production-like local compose stack.
- `docker-compose.dev.yml`: local dependency stack for PostgreSQL/Redis/guacd.

Important docs:

- `docs/pentest-commercialization-execution-plan.md`: primary commercial Penetration execution plan.
- `docs/pentest-commercialization-execution-plan-review.md`: review notes for the Penetration plan.
- `docs/pentest-comprehensive-audit.md`: broad Penetration audit.
- `docs/pentest-phase1-review.md`: Phase 1 review.
- `docs/pentest-phase2-review.md`: Phase 2 review.
- `docs/Phase 3，4 深度审查报告.md`: Phase 3/4 deep review.
- `docs/role-permission-exercise-execution-plan.md`: role/permission/training design plan.
- `docs/training-platform-frontend-redesign-plan-v2.md`: training frontend redesign plan.
- `docs/training-course-development-progress.md`: training progress notes.
- `docs/deploy/production.md` and `docs/deploy/agent-node.md`: deployment notes, but some older Chinese text may display incorrectly unless read as UTF-8.

## 4. Technology Stack

Backend:

- .NET `net10.0`.
- ASP.NET Core web app.
- Entity Framework Core with PostgreSQL.
- Redis for cache/distributed coordination/SignalR scale-out.
- SignalR for realtime notifications.
- Serilog and OpenTelemetry packages are present.
- Docker.DotNet enhanced packages for Docker control.
- Kubernetes client is present.
- Guacamole/guacd integration for remote VM access.
- SSH.NET, SharpPcap, PacketDotNet, KVM/libvirt related settings exist.
- MemoryPack and source-generated JSON serializer context are used in some paths.

Frontend:

- React `19`.
- TypeScript `6`.
- Vite `8`.
- React Router `7`, with file-based page organization under `src/GZCTF/ClientApp/src/pages`.
- Mantine `9`.
- SWR, Axios.
- SignalR client.
- ECharts/Recharts for charting.
- `@xyflow/react` for the Penetration low-code canvas.
- Three / React Three Fiber / postprocessing for some visual effects.
- MDI icons are widely used through `@mdi/react` and `@mdi/js`; lucide-react is also installed, but follow existing local patterns unless there is a reason to switch.
- ReactBits-derived visual components/effects have been integrated and modified in prior work.

Frontend scripts from `src/GZCTF/ClientApp/package.json`:

```powershell
pnpm --dir src/GZCTF/ClientApp dev
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp build
pnpm --dir src/GZCTF/ClientApp genapi
```

The `build` script runs `check` and then `vite build`.

## 5. Build And Test Commands

Run from repository root unless noted.

Frontend typecheck:

```powershell
pnpm --dir src/GZCTF/ClientApp check
```

Frontend production build:

```powershell
pnpm --dir src/GZCTF/ClientApp build
```

Backend main build:

```powershell
dotnet build src/GZCTF/GZCTF.csproj --no-restore
```

Agent build:

```powershell
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore
```

Whitespace check:

```powershell
git diff --check
```

On this machine, the bundled Codex runtime may be needed for Node/pnpm:

```powershell
$env:PATH="C:\Users\87701\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin;C:\Users\87701\.cache\codex-runtimes\codex-primary-runtime\dependencies\bin;$env:PATH"
```

Known environment blocker:

- `dotnet build ... --no-restore` may fail with `NU1301` / NuGet SSL credential errors when the local environment cannot access `https://api.nuget.org/v3/index.json`. If this happens, report it as an environment issue, not as a code failure, unless the error points at source compilation after restore succeeds.

## 6. Local Development Dependencies

`docker-compose.dev.yml` starts local infrastructure only:

- PostgreSQL on `5432`
- Redis on `6379`
- guacd on `4822`

Command:

```powershell
docker compose -f docker-compose.dev.yml up -d
```

`docker-compose.yml` can build/run the full API stack on `8080` with PostgreSQL, Redis, guacd, and API. It uses `DB_PASSWORD` if provided, otherwise a default password from the compose file.

## 7. Deployment Handoff

Known deployment target from prior work:

- Host: `10.0.7.118`
- HTTP port: `8080`
- Linux service: `gzctf.service`
- Publish directory: `/opt/gzctf/publish`
- Typical app URL: `http://10.0.7.118:8080/`
- SSH user previously used: `whoami`
- Password: do not commit in this file. Obtain from the operator or secure local secret store.

Safe deployment flow used in this project:

1. Build a full local publish package.
2. Upload the complete publish package to a temporary directory on the server.
3. Stop the service:

```bash
sudo systemctl stop gzctf.service
```

4. Back up the existing server publish directory:

```bash
sudo cp -a /opt/gzctf/publish /opt/gzctf/publish.backup.$(date +%Y%m%d%H%M%S)
```

5. Preserve server-local persistent/config files before replacing contents:

- `/opt/gzctf/publish/appsettings.json`
- `/opt/gzctf/publish/files`
- `/opt/gzctf/publish/keys`
- any other operator-managed upload/storage directory present on the server.

6. Replace backend and frontend publish contents.
7. Restore preserved config/data directories.
8. Fix ownership and execution permissions as required by the service user.
9. Start service:

```bash
sudo systemctl start gzctf.service
```

10. Health checks:

```bash
sudo systemctl status gzctf.service --no-pager
ss -lntp | grep 8080
curl -I http://127.0.0.1:8080/
sudo journalctl -u gzctf.service -n 120 --no-pager
```

Do not delete production `files`, `keys`, database volumes, upload directories, or custom server config during deployment.

## 8. Git And Branch State Notes

At the time this file was recreated, `git status --short --branch` showed the branch:

```text
codex/main-node-pentest-merge...origin/codex/main-node-pentest-merge
```

The worktree had many uncommitted Penetration-related changes and `CLAUDE.md` was deleted. This file recreates it.

Known untracked or changed Penetration migration/service files at that point included:

- `src/GZCTF/Migrations/20260619060822_AddPenetrationRuntimeRoutes.*`
- `src/GZCTF/Migrations/20260619093000_AddPenetrationDeploymentCleanupState.cs`
- `src/GZCTF/Migrations/20260619113000_AddPenetrationDeploymentEvents.cs`
- `src/GZCTF/Migrations/20260619162000_AddPenetrationAttackGraphFields.cs`
- `src/GZCTF/Migrations/20260619184500_AddPenetrationEnvironmentTeamIndex.cs`
- `src/GZCTF/Services/PenetrationAttackGraphService.cs`
- `src/GZCTF/Services/PenetrationCleanupService.cs`

Never assume these are committed. Check status before continuing:

```powershell
git status --short --branch
```

## 9. Backend Architecture Notes

Core backend areas:

- Controllers live under `src/GZCTF/Controllers`.
- EF entities live under `src/GZCTF/Models/Data`.
- Request/response DTOs live under `src/GZCTF/Models/Request`.
- EF context is `src/GZCTF/Models/AppDbContext.cs`.
- Services live under `src/GZCTF/Services`.
- Container abstractions live under `src/GZCTF/Services/Container`.
- Fleet/agent abstractions live under `src/GZCTF/Services/Fleet`.
- JSON source generation lives in `src/GZCTF/Utils/JsonSerializerContext.cs`.
- Authorization/roles utilities live under `src/GZCTF/Utils` and middleware/extensions.

When adding a backend feature, check all of these:

1. EF entity/model.
2. Migration.
3. DTO/request/response model.
4. Controller route and authorization.
5. Service implementation and transaction boundaries.
6. Serializer context if the project uses source-generated JSON for that model.
7. Frontend API wrapper/type.
8. Frontend page and loading/error/empty states.
9. System logs/audit trail where the feature changes runtime state.
10. Tests or static verification.

## 10. Frontend Architecture Notes

Key frontend paths:

- `src/GZCTF/ClientApp/src/pages`: routed pages.
- `src/GZCTF/ClientApp/src/components`: shared components.
- `src/GZCTF/ClientApp/src/Api.ts`: generated/general API entry.
- `src/GZCTF/ClientApp/src/Api/*.ts`: custom API wrappers, including Penetration and Training.
- `src/GZCTF/ClientApp/src/styles/YinyuRefinement.css`: broad visual refinement styles used by the YINYU frontend.

Frontend quality expectations:

- Use existing Mantine/component patterns.
- Keep operational pages dense but not crowded.
- Preserve consistent glass-card styles and gradients where already established.
- Do not add visible instructional text unless the user explicitly requested a help/instruction UI.
- All visible buttons must map to real behavior or a clear disabled state with a reason.
- Avoid layout shifts in cards, status labels, toolbars, boards, and grids.
- Prevent overlapping text and inconsistent wrapping. Long titles should clamp, fit, or measure down rather than breaking the layout.
- For route files with `[id]` in the path, use `-LiteralPath` in PowerShell reads/patch planning if needed.

## 11. Visual System Handoff

The project went through several YINYU visual redesign rounds. The current user preference is not the older "crystal theme" toggle. Important decisions:

- The bottom-left theme toggle should not exist.
- The stray bottom-left avatar/login button should not exist unless the current branch intentionally reintroduced it.
- Current default visual direction is the YINYU green / dark cyber style, with lavender/purple accents allowed where already used.
- Avoid old noisy/honeycomb backgrounds on cards where the user asked for clean glass cards.
- Admin pages and training pages should use the management-style background rather than stacking a new background over the old honeycomb background.
- Status/category labels should usually use gradient text or semantically distinct colors, not identical capsules for all states.
- Progress bars that became noisy should be normalized to the AWDP-style progress bar treatment.

ReactBits effects that were previously discussed/integrated:

- `ColorBends`
- `DotField`
- `GridScan`
- `DarkVeil`
- `GradientText`
- `TextType`
- `ScrambledText`
- `MagicBento`

Do not blindly copy original visual code over current work. Inspect the current component/style integration first.

## 12. Game Modes And Data Expectations

Main game modes in current product context:

- Jeopardy/CTF
- Theory
- AWDP
- Mixed
- Penetration

Scoreboards and display-screen data must align with the actual per-mode scoreboard APIs. A prior issue was that a CTF display screen showed empty/wrong totals while the game scoreboard had correct teams, scores, and challenge counts. When fixing display screens, compare with the mode's real scoreboard page/API rather than inventing a separate aggregation.

For CTF container-running state:

- The "running" visual border for a CTF challenge should mean from container creation request until container destroy.
- It should not apply to ordinary open challenges or VM targets unless specifically requested.

Blood medals:

- First/second/third blood icons were changed several times for performance and style.
- If replacing them, use simple performant SVGs/components.
- Notifications, challenge cards, realtime logs, and display screens should use the same component.

## 13. Penetration Module Handoff

This is the most sensitive active module. The user explicitly asked to fully complete it, not leave a demo.

### 13.1 Important Files

Backend:

- `src/GZCTF/Services/PenetrationService.cs`
- `src/GZCTF/Services/PenetrationAttackGraphService.cs`
- `src/GZCTF/Services/PenetrationCleanupService.cs`
- `src/GZCTF/Controllers/PenetrationAdminController.cs`
- `src/GZCTF/Controllers/PenetrationPlayerController.cs`
- `src/GZCTF/Models/Data/PenetrationEntities.cs`
- `src/GZCTF/Models/Request/Game/PenetrationModels.cs`
- `src/GZCTF/Models/AppDbContext.cs`
- `src/GZCTF/Utils/JsonSerializerContext.cs`

Agent/Fleet/container:

- `src/GZCTF.Agent/Controllers/ContainerController.cs`
- `src/GZCTF.Agent/Models/ContainerModels.cs`
- `src/GZCTF.Agent/Services/DockerService.cs`
- `src/GZCTF/Services/Container/Manager/IContainerManager.cs`
- `src/GZCTF/Services/Container/Manager/DockerManager.cs`
- `src/GZCTF/Services/Container/Manager/KubernetesManager.cs`
- `src/GZCTF/Services/Fleet/AgentClient.cs`
- `src/GZCTF/Services/Fleet/FleetContainerManager.cs`
- `src/GZCTF/Services/Fleet/FleetManager.cs`
- `src/GZCTF/Services/Fleet/QueueManager.cs`
- `src/GZCTF/Services/Fleet/RedisDistributedLock.cs`

Frontend:

- `src/GZCTF/ClientApp/src/Api/PenetrationApi.ts`
- `src/GZCTF/ClientApp/src/pages/admin/games/[id]/Penetration.tsx`
- `src/GZCTF/ClientApp/src/pages/games/[id]/Penetration.tsx`
- `src/GZCTF/ClientApp/src/styles/YinyuRefinement.css`

Docs:

- `docs/pentest-commercialization-execution-plan.md`
- `docs/pentest-phase1-review.md`
- `docs/pentest-phase2-review.md`
- `docs/Phase 3，4 深度审查报告.md`

### 13.2 Implemented Concepts To Preserve

- Stable `TopologyKey` on topology objects:
  - networks
  - nodes
  - interfaces
  - edges
  - score items
- Draft topology is separate from published snapshots and runtime environments.
- Team environment binds to `PublishedVersion`; runtime behavior must use the deployed published version, not the current draft.
- Runtime nodes store `TopologyNodeKey`.
- Runtime routes are represented by `PenetrationRuntimeRoute`.
- Save/upsert behavior should preserve topology keys rather than clear/rebuild everything.
- Validate/plan can accept transient models and should not persist drafts.
- Player attack graph/fog should hide unknown nodes and must not leak hidden node real IDs, names, IPs, topology keys, or network details.
- Deployment failure cleanup should not be cancelled just because the deployment request was cancelled.
- Plan/runtime route DTOs include `IsExecutable`.
- Admin UI should distinguish executable network routes from hint/audit-only paths.

### 13.3 Network/Policy Boundary

This is critical:

- Current approved direction is **network-level isolation/reachability**, not port-level firewall/ACL enforcement.
- The intended implementation direction is platform-managed Linux bridge/veth fabric with Docker network `Internal=true` and explicit routing.
- Protocol/port fields in policies are currently hints, path summaries, future expansion fields, and player/admin explanations unless a later implementation truly enforces them.
- Do not claim "firewall denies TCP/80" or similar unless packet/port enforcement exists.
- `Deny` should mean no reachable network-level route is generated in the current phase, not that an iptables/nftables port rule exists.
- Duplicate same-network-pair runtime routes should execute only the highest-priority route; later same-pair paths should become hint/audit unless a safe multipath design is implemented.

### 13.4 Penetration User Experience Direction

Admin builder:

- Low-code canvas for security domains, asset nodes, interfaces, edges/policies, scoring items, templates, and deployment planning.
- The admin needs clear guidance:
  - start by placing security domains
  - add nodes/templates
  - attach interfaces
  - draw access/policy paths
  - add score items and prerequisites
  - validate
  - preview plan
  - publish
  - deploy
  - observe/cleanup
- Save, validate, plan, publish, deploy must have different semantics and clear UI states.

Player workspace:

- Should not expose raw enterprise network implementation details.
- Should present an attack graph/topology with fog-of-war.
- Initial/entry targets are visible.
- Completing/unlocking score items reveals connected next modules/areas.
- The user discussed a WarCraft-style fog direction: center/entry area visible, surrounding areas black fog, linked modules reveal and fog between linked areas clears after prerequisites are solved.

### 13.5 Known Penetration Review Findings

From review docs and prior work, watch for these issues:

- Redis deployment lock TTL must be long enough or renewed; a fixed 45-second lock is unsafe for large deployments.
- Deployment must have a second-line status guard against stale/expired locks.
- `ManualCleanupRequired` must not be overwritten back to `CleanupPending`.
- Remote Fleet container destroy failures must not mark containers `Destroyed` if the agent call failed.
- `BuildRuntimePlan` errors must transition environment state safely and not leave `CreatingNetworks` forever.
- VM capacity release was previously suspected; verify against current code before changing. The user reported VM release may be a false positive based on testing.
- `QueueManager` must not mark queued targets `Running` without actually creating resources, unless its contract has been redesigned and documented.
- Deployment parallelism and team/node grouping need care; do not introduce race conditions.
- Running observation tables should expose enough trace data: container ID, network name, IP, public address/port, status, node, health, event timeline.
- Cleanup retry count should not double-increment on nested failures.
- Node deregistration should protect active resources; forced cleanup must be explicit and auditable if implemented.

## 14. Node Management Handoff

The node management page was requested to show, after selecting a node, a bottom detail/list panel containing current and historical containers/VMs on that node:

- active resources first
- historical records after
- sorted by time
- paginated
- grouped by container and VM
- traceable fields:
  - opener/user/team
  - start time
  - duration
  - exposed address
  - resource name
  - status
  - associated game/challenge
  - node
  - container/VM ID when available
- management actions:
  - destroy container/VM
  - duration management if supported
  - view trace/history
- status display should use the existing gradient text semantics.

If merging branches, preserve latest training work from main and merge only node-management and multi-segment Penetration changes as requested by the user.

## 15. Training Module Handoff

The training module evolved from "online practice" to a broader training platform.

User-approved direction:

- There are student groups managed by teachers or higher roles.
- Training module has two major tracks:
  - Theory training
  - CTF training
- CTF training is organized by directions/categories such as Web, Misc, Crypto, Reverse, Pwn, etc.
- Directions should be customizable by teacher/admin.
- Within a direction, teachers/admins can edit outline modules/chapters.
- Chapter pages should contain article/Markdown content, iframe video embedding, and embedded resource/container cards.
- The older "练练手" jump-button concept should be removed in favor of embedding launchable challenge/container cards at the end of a chapter.
- Teachers/admins should edit training content in a Feishu-like efficient editor page/panel, not cramped modal-only flows.
- Students should see completion state directly in the training module, including completed chapters/modules.
- Teachers should query detailed student training progress for their groups.
- Admins can query all students.
- Training pages should use the management-style background, not honeycomb stacked underneath.

Important training files seen in prior work:

- `src/GZCTF/ClientApp/src/Api/TrainingApi.ts`
- `src/GZCTF/ClientApp/src/pages/admin/training.tsx`
- `src/GZCTF/ClientApp/src/pages/training.tsx`
- `src/GZCTF/ClientApp/src/pages/training/ctf/modules/[moduleId]/challenges.tsx`
- `src/GZCTF/Controllers/TrainingAdminController.cs`
- `src/GZCTF/Controllers/TrainingController.cs`
- `src/GZCTF/Controllers/StudentGroupAdminController.cs`
- `src/GZCTF/Models/Data/Training.cs`
- `src/GZCTF/Models/Data/StudentGroup.cs`
- `src/GZCTF/Models/Request/Training/TrainingModels.cs`
- `src/GZCTF/Models/Request/Training/StudentGroupModels.cs`

Known UI quality expectations:

- No left sidebar overlap.
- Training background should replace old page background, not cover it with another layer.
- Course cards must align and have consistent dimensions.
- Course posters should not be squeezed into tiny homepage cards; show them on the course detail/introduction page.
- Learning overview should be graphic and comprehensive:
  - total/completed courses or chapters
  - progress by type
  - accuracy where applicable
  - cumulative check-in days
  - continuous check-in days
  - calendar/date heatmap based on real date/month data
- Avoid low-contrast gray text on dark backgrounds.
- Lists showing `3/3` must render all items or paginate/scroll clearly.
- Do not rely on dummy/empty backend data for visible training functions.

## 16. Role And Permission Handoff

Role groups requested by the user:

1. Student
2. Teacher
3. Admin
4. Super Admin

Permission direction:

- Super Admin is built-in highest permission.
- Super Admin can CRUD all other role groups.
- Admin can CRUD teacher and student role groups, but not admins/super admins.
- Teacher can CRUD/manage student groups/accounts within scope.
- Management/admin interface is visible only to Teacher and above.
- Teacher route access:
  - game management
  - challenge library/question bank
  - environment templates
  - user management limited to student accounts/groups
  - training management for their scope
- Admin has broad access except CRUD of peer/higher admin roles.
- Students should not see the admin button. They should see training/online learning entry instead.
- User management UI should show role labels with distinct gradient/color styles.
- Users with insufficient scope should not see higher/equal roles in management lists.
- Backend must enforce all frontend role visibility rules.

Always verify actual current role enums and policies before modifying. Likely files:

- `src/GZCTF/Utils/Enums.cs`
- `src/GZCTF/Utils/RolePolicy.cs`
- `src/GZCTF/Middlewares/PrivilegeAuthentication.cs`
- `src/GZCTF/Controllers/AdminController.cs`
- `src/GZCTF/Controllers/AccountController.cs`
- `src/GZCTF/ClientApp/src/components/WithRole.tsx`
- `src/GZCTF/ClientApp/src/components/admin/WithAdminTab.tsx`
- `src/GZCTF/ClientApp/src/pages/admin/Users.tsx`

## 17. Team Management Handoff

The team page has been redesigned multiple times. Current desired direction from the user:

- Use the same card style as homepage challenge cards, not old card styles.
- Full-width page usage similar to homepage; avoid large empty right area.
- Left panel:
  - functional area for creating/joining teams
  - joined team list below
- Right panel:
  - selected team detail directly editable/viewable if leader/admin
  - no separate "team detail" button
  - team avatar large on left
  - team name above avatar
  - member list to the right, scrollable and not overly wide
  - team signature/invite code/details below
  - join request approval area
  - leader/admin can approve/reject join requests and remove members in member list
- The page should fit without unnecessary vertical scrolling when possible.

Known prior runtime bug:

- Team management once threw `TypeError: Cannot read properties of undefined (reading 'default')`, likely from an asset/icon import or lazy route import. Verify current code if this reappears.

## 18. Display Screen / Big Screen Handoff

The display-screen module had data alignment issues.

Requirements:

- All game-mode display screens must align with the real scoreboard APIs/pages:
  - CTF/Jeopardy
  - AWDP
  - Theory
  - Mixed where applicable
  - Penetration where applicable
- Do not calculate team scores differently from the scoreboard.
- Use `ScoreboardItem.score` or the mode's authoritative scoreboard DTO where applicable.
- Challenge count, solve count, team count, score curves, realtime logs, and submission/solve events must be checked against real mode data.
- Entry page should contain formal production copy, not design notes like "visual baseline" or "data source".
- Big screen should support view drag/pan and zoom if the current branch includes that feature.
- Ranking image/display area may need a visible drag line/handle for horizontal movement.

## 19. Node/Fleet/Container Concepts

Core ideas:

- Main service can deploy locally through Docker manager or remotely through Fleet Agent.
- Fleet Agent runs on worker nodes and exposes container operations to the main service.
- Guacamole/guacd is used for remote VM-style access paths.
- Docker challenge public port pool support was added in prior main commits:
  - configured `PublicPortStart/PublicPortEnd`
  - main service and Agent support it
  - if exhausted, fall back to Docker random port

When changing container lifecycle:

- Do not clear container IDs before a verified destroy.
- Agent failures must preserve traceability.
- Capacity release must be audited for Docker and VM paths.
- Runtime resource state should not say `Running` until the actual resource exists and is reachable enough for that state.

## 20. Common Bug Patterns In This Repo

1. Encoding-state text appears until refresh.
   - Check for literal escaped strings, mojibake, incorrect i18n loading, and loading components rendering bad fallback text.

2. Loading animation residue on admin route changes.
   - Check shared wrappers such as `WithNavbar`, `WithAdminTab`, route loading components, and CSS pseudo-elements.

3. Background replacement accidentally stacks over old honeycomb.
   - The fix should remove/disable old background at the page root, not add another fixed layer above it.

4. `The model field is required`.
   - Usually a JSON body/model binding mismatch. Check request body shape, `Content-Type`, DTO required fields, route/controller parameter attributes, and frontend normalize function.

5. Typewriter text stacks after changing questions.
   - Reset key/state on question ID change.

6. `Cannot read properties of null (reading 'addEventListener')`.
   - Check effects that assume DOM refs exist. Guard refs and clean up listeners.

7. `Cannot read properties of undefined (reading 'default')`.
   - Check dynamic import/default export, SVG imports, route lazy imports, or bundler asset handling.

8. Frontend succeeds visually but backend is empty.
   - The user strongly dislikes empty frontends. Verify backend API and persisted data for every visible function.

## 21. Coding Standards

Backend:

- Keep service boundaries clear; do not put every new behavior into one giant service.
- Use transactions for database-only atomic operations.
- Do not hold database transactions open around long external Docker/Agent calls.
- External resource workflows need persisted state machines and compensation.
- Return specific errors with object/field context.
- Add migrations for model changes.
- Update `JsonSerializerContext` when source generation requires it.
- Use cancellation tokens, but do not let request cancellation abort necessary cleanup after partial deployment.
- Add system logs/audit events for destructive operations and deployment lifecycle events.

Frontend:

- Use typed API wrappers.
- Loading, empty, error, success, and disabled states are all required.
- Use stable dimensions for cards/toolbars/status labels to avoid jitter.
- Avoid nested cards unless the design already requires it.
- Keep card style consistent with current YINYU visual system.
- Prefer existing shared components over one-off copies.
- Long text must wrap/clamp/fit gracefully.
- Do not show admin-only actions to unauthorized users, and do not rely on frontend-only checks for security.

Data/API:

- Backend is the source of truth for permissions, score, solved state, training progress, and runtime state.
- Frontend may optimistically update only when the backend contract supports rollback/error states.
- When adding an endpoint, align the controller, DTO, API wrapper, page, permissions, logs, and tests.

## 22. Quality Gate Checklist Before Final Response

At minimum, run or explain why you could not run:

```powershell
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp build
dotnet build src/GZCTF/GZCTF.csproj --no-restore
git diff --check
```

For Agent/Fleet changes, also run or explain:

```powershell
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore
```

For migrations/model changes:

- Verify migration files are present.
- Verify `AppDbContextModelSnapshot.cs` is consistent.
- Verify existing data upgrade path.

For frontend visual/layout changes:

- Check desktop and mobile-ish widths where possible.
- Check no old background remains underneath.
- Check no text overlap, abnormal wrapping, or scroll gaps.
- Check route changes do not leave loading residue.

For deployment:

- Confirm service active.
- Confirm `8080` listens.
- Curl local root.
- Check recent logs.
- Report any logs/errors clearly.

## 23. What Future Agents Should Do First

1. Run:

```powershell
git status --short --branch
```

2. Read the user message carefully and identify whether they are asking for:
   - code changes
   - plan/documentation
   - review/audit
   - deployment
   - push/branch work

3. If working on Penetration, read:

```powershell
Get-Content -Path docs\pentest-commercialization-execution-plan.md -Encoding UTF8
Get-Content -Path docs\pentest-phase1-review.md -Encoding UTF8
Get-Content -Path docs\pentest-phase2-review.md -Encoding UTF8
Get-Content -LiteralPath "docs\Phase 3，4 深度审查报告.md" -Encoding UTF8
```

4. If working on training, read:

```powershell
Get-Content -Path docs\training-platform-frontend-redesign-plan-v2.md -Encoding UTF8
Get-Content -Path docs\training-course-development-progress.md -Encoding UTF8
Get-Content -Path docs\role-permission-exercise-execution-plan.md -Encoding UTF8
```

5. Use CodeGraph for structure if available; otherwise use `rg` and targeted file reads.

6. Before finalizing, summarize exactly what changed, what was verified, and what remains blocked.
