# Tasks: CTF 场景化实战平台

**Input**: Design documents from `/specs/001-ctf-scenario-engine/`

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Per Constitution Principle II, Playwright E2E tests are MANDATORY for all new UI features. Each user story includes corresponding E2E test tasks.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

- **Backend**: `src/GZCTF/` (ASP.NET Core project)
- **Frontend**: `src/GZCTF/ClientApp/src/` (React + TypeScript)
- **E2E Tests**: `tests/e2e/` (Playwright)
- **GZCTF Repo**: https://github.com/yinyu-cybersecurity/GZCTF.git

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization, dependency installation, and basic environment configuration

- [x] T001 Clone GZCTF repository and create `001-ctf-scenario-engine` feature branch if not already done
- [x] T002 [P] Install KVM/QEMU + libvirt on dev server (`apt install qemu-kvm libvirt-daemon-system libvirt-clients virtinst bridge-utils`), verify with `kvm-ok`
- [x] T003 [P] Add Apache Guacamole (guacd + guacamole-client) services to `src/GZCTF/docker-compose.yml`, verify containers start successfully
- [x] T004 [P] Create storage directories for VM disk images (`/var/lib/gzctf/images/`) and set correct permissions for libvirt group
- [x] T005 Configure `src/GZCTF/appsettings.json` with new configuration sections: KvmSettings (image storage path, libvirt URI), GuacamoleSettings (guacd host, guacamole API URL), TimeSlotDefaults (max participants, slot duration)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T006 Extend GZCTF Challenge entity with `ChallengeType` discriminator column in `src/GZCTF/Models/Challenge.cs`, add enum `ChallengeType { Standard, Scenario, IRChallenge }`
- [x] T007 [P] Create ImageTemplate entity in `src/GZCTF/Models/ImageTemplate.cs` with fields: Id, Name, OSType, ImageType, RegistryUrl, RegistryAuth, LocalFilePath, FileSize, UploadedAt, Status
- [x] T008 [P] Create TimeSlot entity in `src/GZCTF/Models/TimeSlot.cs` with fields: Id, ScenarioId, StartTime, EndTime, MaxParticipants, CurrentParticipants
- [x] T009 [P] Create ScoringRule entity in `src/GZCTF/Models/ScoringRule.cs` with fields: Id, ChallengeId, SubmissionType, Weight, VerificationMode, MaxAttempts, ScoreDecay
- [x] T010 Implement VmManager service in `src/GZCTF/Services/VmManager.cs`: wrap virsh CLI commands for VM lifecycle (CreateFromTemplate, Start, Shutdown, Destroy, SnapshotRevert) with async Process API, timeout (120s), and error handling per Constitution Principle IV
- [x] T011 [P] Implement ImageStorage service in `src/GZCTF/Storage/ImageStorage.cs`: handle local disk image storage (save uploaded file, validate format .qcow2/.ova/.vmdk, validate size ≤ 50GB, generate metadata, register with libvirt storage pool)
- [x] T012 [P] Implement ContainerOrchestrator extension in `src/GZCTF/Services/ContainerOrchestrator.cs`: add methods to pull images from OCI Registry (public + private with auth), create/remove Docker networks for scenario isolation
- [x] T013 [P] Implement GuacamoleProxy service in `src/GZCTF/Services/GuacamoleProxy.cs`: wrap Guacamole REST API (create/delete connection, generate auth token) for Windows IR target access
- [x] T014 Create ImageTemplateController in `src/GZCTF/Controllers/ImageTemplateController.cs`: POST upload (multipart/form-data, format/size validation), GET list (search, filter by OSType), GET by id, DELETE (clean up local file + libvirt storage pool)
- [x] T015 [P] Set up ScenarioHub (SignalR) in `src/GZCTF/Hubs/ScenarioHub.cs`: define event contracts for StageUnlocked, TimeWarning, ScoreUpdated, EnvironmentReady, CheckpointCompleted, EnvironmentResetComplete
- [x] T016 Create EF Core database migration for new entities (ChallengeType discriminator, ImageTemplates, TimeSlots, ScoringRules) and apply via `dotnet ef migrations add AddScenarioAndIREntities` then `dotnet ef database update`
- [x] T017 [P] Install frontend dependencies for topology visualization: add `@xyflow/react` (React Flow) to `src/GZCTF/ClientApp/package.json` for network topology graph rendering

**Checkpoint**: Foundation ready — Challenge hierarchy extended, infrastructure services operational, image templates uploadable, SignalR hub prepared. User story implementation can now begin.

---

## Phase 3: User Story 1 - 多阶段真实场景挑战 (Priority: P1) 🎯 MVP

**Goal**: 管理员可创建多阶段攻击链场景，选手可逐步解锁并完成全部阶段。支持阶段间网络隔离、环境副本自动创建、阶段 Flag 验证解锁。

**Independent Test**: 管理员创建包含 3 个阶段的场景（外网入口→内网扫描→域控提权），选手从阶段 1 逐步解锁并完成全部阶段，系统记录每阶段的完成时间和操作路径。

### E2E Tests for User Story 1 (MANDATORY per Constitution Principle II)

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T018 [P] [US1] Playwright E2E test for scenario creation flow in `tests/e2e/scenario-create.spec.ts`: admin creates a 3-stage scenario with network rules and scoring config, verifies scenario appears in game challenge list
- [x] T019 [P] [US1] Playwright E2E test for scenario playthrough flow in `tests/e2e/scenario-play.spec.ts`: player joins scenario, submits flags for each stage, verifies stage unlock, completion summary, and timeline display

### Implementation for User Story 1

- [x] T020 [US1] Create Scenario entity in `src/GZCTF/Models/Scenario.cs` inheriting from Challenge with ChallengeType=Scenario, adding Stages navigation property, and state transition logic
- [x] T021 [P] [US1] Create Stage entity in `src/GZCTF/Models/Stage.cs` with fields: Id, ScenarioId, OrderIndex, Title, SkillDescription, PrerequisiteStages (many-to-many self-reference), NetworkRules (JSON), EnvironmentRefs (JSON), Flag (hashed)
- [x] T022 [P] [US1] Create ScenarioInstance entity in `src/GZCTF/Models/ScenarioInstance.cs` with fields: Id (guid), ScenarioId, UserId, CurrentStageId, StageStatuses (JSON), StageTimeline (JSON), EnvironmentCredentials (JSON), TimeSlotId, CreatedAt, Status
- [x] T023 [US1] Create EF Core migration for Scenario, Stage, ScenarioInstance tables then apply via `dotnet ef migrations add AddScenarioEntities` and `dotnet ef database update`
- [x] T024 [US1] Implement EnvironmentService in `src/GZCTF/Services/EnvironmentService.cs`: orchestrates VM/container creation per stage (calls VmManager for Windows, ContainerOrchestrator for Linux), creates Linux Bridge + iptables rules for network isolation per stage NetworkRules, generates per-player environment credentials, handles async creation with timeout/retry
- [x] T025 [US1] Implement ScenarioController in `src/GZCTF/Controllers/ScenarioController.cs`: POST create (admin), GET list (filter by gameId), GET by id, PUT update (Draft only), DELETE (Draft only), POST publish
- [x] T026 [US1] Implement scenario instance endpoints in ScenarioController: POST create instance (validate time slot availability, trigger async environment creation, return access credentials), GET instance status, POST submit stage flag (validate → unlock next stage → push StageUnlocked via SignalR)
- [x] T027 [US1] Implement TimeSlotController in `src/GZCTF/Controllers/TimeSlotController.cs`: GET available slots for scenario, POST reserve slot, GET my reservations
- [x] T028 [US1] Create scenario admin page in `src/GZCTF/ClientApp/src/pages/admin/ScenarioCreate.tsx`: multi-step form (basic info → stages configuration with environment selection → network rules → scoring → review), use Mantine UI components (Stepper, Select, MultiSelect, JsonInput)
- [x] T029 [P] [US1] Create scenario admin list/edit page in `src/GZCTF/ClientApp/src/pages/admin/ScenarioList.tsx`: table with search/filter by game, edit button for Draft scenarios, delete with confirmation modal
- [x] T030 [US1] Create scenario player view in `src/GZCTF/ClientApp/src/pages/game/ScenarioPlayer.tsx`: shows current stage info, stage timeline/progress, Flag submission form (with attempt counter), unlocked stages list, completion summary modal. Use Mantine Card/Badge/Progress/Timeline components
- [x] T031 [US1] Create scenario SignalR integration in `src/GZCTF/ClientApp/src/services/scenarioHub.ts`: connect to ScenarioHub, react to StageUnlocked (show notification, update UI), TimeWarning (show countdown alert), EnvironmentReady (enable access buttons)
- [x] T032 [US1] Create time slot reservation component in `src/GZCTF/ClientApp/src/components/scenario/TimeSlotPicker.tsx`: show available slots as time blocks, highlight current selection, confirm reservation with participant count display
- [x] T033 [US1] Add scenario entry points: integrate scenario challenge type into GZCTF existing Game detail page challenge list in `src/GZCTF/ClientApp/src/pages/game/GameDetail.tsx`, ensure Standard/Scenario/IRChallenge types are visually distinct

**Checkpoint**: At this point, User Story 1 should be fully functional — admin creates scenarios, players can join, progress through stages, and receive real-time notifications.

---

## Phase 4: User Story 2 - 应急响应挑战模块 (Priority: P1)

**Goal**: 管理员创建 IR 挑战题目（含检查点配置），选手通过 SSH (Linux) 或 Web 桌面代理 (Windows) 访问环境，执行应急响应操作。系统自动检测检查点状态并评分。

**Independent Test**: 管理员创建 IR 题目（恢复被加密的数据库 + 找出攻击者 IP），选手登入环境执行操作，系统自动验证检查点完成并评分。

### E2E Tests for User Story 2 (MANDATORY per Constitution Principle II)

- [x] T034 [P] [US2] Playwright E2E test for IR challenge flow in `tests/e2e/ir-challenge.spec.ts`: admin creates IR challenge with checkpoints, player accesses environment, completes checkpoints (simulate or verify verification display), requests environment reset

### Implementation for User Story 2

- [x] T035 [US2] Create IRChallenge entity in `src/GZCTF/Models/IRChallenge.cs` inheriting from Challenge with ChallengeType=IRChallenge, adding OSType, AccessConfig (JSON), Checkpoints navigation property
- [x] T036 [P] [US2] Create IRCheckpoint entity in `src/GZCTF/Models/IRCheckpoint.cs` with fields: Id, ChallengeId, OrderIndex, Description, VerificationType (enum: AutoScript/AutoCommand/ManualAnswer/ManualReview), VerificationConfig (JSON), Score, IsRequired
- [x] T037 [P] [US2] Create IRInstance entity in `src/GZCTF/Models/IRInstance.cs` with fields: Id (guid), ChallengeId, UserId, EnvironmentStatus (enum: Creating/Ready/Error/Destroyed), CheckpointResults (JSON), ShellLog (JSON), ResetCount, AccessDetails (JSON), TimeSlotId, CreatedAt, EndedAt
- [x] T038 [US2] Create EF Core migration for IRChallenge, IRCheckpoint, IRInstance tables then apply via `dotnet ef migrations add AddIREntities` and `dotnet ef database update`
- [x] T039 [US2] Implement CheckpointVerificationService in `src/GZCTF/Services/CheckpointVerificationService.cs`: periodic background job (every 30s via IHostedService) that runs AutoScript/AutoCommand verifications on active IR instances, updates checkpoint status on success, pushes CheckpointCompleted via SignalR. Include timeout (30s per check) and error handling
- [x] T040 [US2] Implement IRChallengeController in `src/GZCTF/Controllers/IRChallengeController.cs`: POST create (admin, with checkpoints config), GET list, GET by id, PUT update, DELETE
- [x] T041 [US2] Implement IR instance endpoints in IRChallengeController: POST create instance (create environment via EnvironmentService, configure Guacamole connection for Windows or generate SSH creds for Linux, return access details), GET status (checkpoint progress, remaining time), POST submit checkpoint answer (ManualAnswer type), POST request reset (async, update ResetCount)
- [x] T042 [US2] Implement SSH credential management in `src/GZCTF/Services/SSHAccessService.cs`: generate temporary SSH credentials per IR instance, configure SSH access to Linux IR containers, rotate credentials on reset
- [x] T043 [US2] Create IR challenge admin page in `src/GZCTF/ClientApp/src/pages/admin/IRChallengeCreate.tsx`: form with basic info → OS type selection → checkpoints editor (add/remove/reorder, verification type selection with config fields per type) → scoring config. Use Mantine Stepper + dynamic form arrays
- [x] T044 [P] [US2] Create IR challenge admin list page in `src/GZCTF/ClientApp/src/pages/admin/IRChallengeList.tsx`: table with search/filter by game, OSType badge, edit/delete actions
- [x] T045 [US2] Create IR challenge player view in `src/GZCTF/ClientApp/src/pages/game/IRChallengePlayer.tsx`: environment status indicator, access instructions (SSH command + credential copy for Linux, embedded Guacamole iframe for Windows), checkpoint list with completion status, checkpoint answer submission form, reset request button with confirmation
- [x] T046 [US2] Integrate Guacamole JavaScript client: create `src/GZCTF/ClientApp/src/components/ir/GuacamoleDesktop.tsx` component wrapping guacamole-common-js for embedded Windows remote desktop in browser. Handle connection lifecycle (connect on mount, disconnect on unmount), display connection status, support clipboard sharing
- [x] T047 [US2] Create shell log viewer component in `src/GZCTF/ClientApp/src/components/ir/ShellLogViewer.tsx`: display IR environment command history log (read-only, scrollable), with syntax highlighting for bash/PowerShell commands
- [x] T048 [US2] Add IR challenge SignalR event handlers in `src/GZCTF/ClientApp/src/services/scenarioHub.ts`: handle CheckpointCompleted (update checkpoint UI), EnvironmentResetComplete (refresh access details), ShellLogUpdated

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently — admin can create both scenarios and IR challenges, players can participate in either type.

---

## Phase 5: User Story 3 - 综合提交与多维评分 (Priority: P2)

**Goal**: 选手可提交 Flag、解题报告、攻击者 IP 等多种类型答案。系统按管理员配置的权重自动计算综合得分，支持自动验证和人工评审混合模式。排行榜展示多维得分明细。

**Independent Test**: 管理员配置评分规则（Flag 40% + Writeup 30% + IP 30%），选手提交所有类型答案后，系统自动计算综合得分并更新排行榜。

### E2E Tests for User Story 3 (MANDATORY per Constitution Principle II)

- [x] T049 [P] [US3] Playwright E2E test for submission and scoring flow in `tests/e2e/submission-scoring.spec.ts`: admin configures multi-type scoring rules, player submits flag/writeup/IP, verifies auto-scoring and leaderboard update, admin performs manual review, verifies score recalculation

### Implementation for User Story 3

- [x] T050 [US3] Extend GZCTF Submission entity in `src/GZCTF/Models/Submission.cs`: add SubmissionType (enum: Flag/Writeup/IP/Credential/Custom), Content (JSON), ReviewedBy (FK nullable), ReviewComment, AttemptNumber fields
- [x] T051 [US3] Create EF Core migration for Submission extensions then apply via `dotnet ef migrations add ExtendSubmissions` and `dotnet ef database update`
- [x] T052 [US3] Implement ScoringService in `src/GZCTF/Services/ScoringService.cs`: load scoring rules for challenge, calculate total score = Σ(typeScore × weight/100), handle ScoreDecay strategies (None=full/zero, Half=half each retry, Linear=linear decrease), validate weight sum = 100%. Handle missing submission types (0 points for that type)
- [x] T053 [US3] Implement SubmissionController extensions in `src/GZCTF/Controllers/SubmissionController.cs`: POST submit (multi-type, auto-verify Flag/IP, queue Writeup for review), GET submissions (filter by challenge, user, type), POST upload file (multipart, 50MB max, PDF/Markdown only per spec)
- [x] T054 [US3] Implement admin review endpoints in `src/GZCTF/Controllers/SubmissionController.cs`: GET pending reviews (ManualReview type, filter by challenge), POST review submission (set score, comment), PUT re-review
- [x] T055 [US3] Implement LeaderboardService in `src/GZCTF/Services/LeaderboardService.cs`: calculate rankings per challenge (sort by totalScore DESC, then by lastSubmissionTime ASC), generate detail scores per submission type, cache via Redis
- [x] T056 [US3] Implement leaderboard API endpoint in `src/GZCTF/Controllers/SubmissionController.cs`: GET /leaderboard?challengeId=42 (return ranked entries with detail scores, paginated)
- [x] T057 [US3] Create scoring rule config component in `src/GZCTF/ClientApp/src/components/scenario/ScoringRuleEditor.tsx`: dynamic list of submission types with weight sliders (visual sum=100% validation), verification mode selector, attempt limit input, score decay selector. Reusable for both Scenario and IR challenge config pages.
- [x] T058 [US3] Create multi-type submission UI in `src/GZCTF/ClientApp/src/components/scenario/MultiTypeSubmission.tsx`: tabbed interface for different submission types (Flag text input, Writeup editor with Markdown preview + PDF upload, IP address input with validation, custom fields). Show per-type scores, attempt counts, and verification status.
- [x] T059 [US3] Create leaderboard component in `src/GZCTF/ClientApp/src/components/scenario/Leaderboard.tsx`: ranked table with rank column, team name, total score, detail score breakdown columns (one per submission type). Use Mantine Table with sorting. Auto-refresh via SignalR LeaderboardUpdated event.
- [x] T060 [US3] Create admin review page in `src/GZCTF/ClientApp/src/pages/admin/SubmissionReview.tsx`: table of pending ManualReview submissions, click to expand submission content (Writeup preview, file download), score input (1-10 slider), comment textarea, approve button. Filter by challenge and submission type.
- [x] T061 [US3] Wire scoring into scenario and IR challenge completion flows: call ScoringService.recalculate on each submission, push ScoreUpdated + LeaderboardUpdated via SignalR, update player score display in real-time

**Checkpoint**: At this point, User Stories 1, 2, AND 3 should all work — submissions flow seamlessly, scores aggregate correctly, leaderboard reflects real-time standings.

---

## Phase 6: User Story 4 - 场景拓扑可视化与管理 (Priority: P3)

**Goal**: 管理员通过可视化编辑器设计场景网络拓扑（节点+连线+隔离规则）。选手在挑战中查看当前已解锁部分的拓扑视图，帮助理解攻击路径。

**Independent Test**: 管理员创建包含 4 个节点的拓扑（外网区→DMZ区→内网区→核心区），设置节点连接和隔离规则，选手视角仅展示已解锁阶段对应的拓扑。

### E2E Tests for User Story 4 (MANDATORY per Constitution Principle II)

- [x] T062 [P] [US4] Playwright E2E test for topology editor flow in `tests/e2e/topology-editor.spec.ts`: admin creates topology with 4 nodes and connections between them, sets isolation rules, saves; verifies admin view shows complete topology, player view filters by unlocked stage

### Implementation for User Story 4

- [x] T063 [US4] Create TopologyNode component in `src/GZCTF/ClientApp/src/components/topology/TopologyNode.tsx`: custom React Flow node rendering with node type icon (entry point, internal host, domain controller, etc.), status badge (locked/unlocked/completed), hover tooltip with skill description
- [x] T064 [US4] Create TopologyEditor component in `src/GZCTF/ClientApp/src/components/topology/TopologyEditor.tsx`: React Flow canvas with drag-and-drop node creation (node type palette), edge drawing between nodes, network isolation rule editor per edge (allow/deny, protocol, port range), auto-layout button, zoom/pan controls. Nodes are mapped to stages.
- [x] T065 [US4] Create TopologyViewer component in `src/GZCTF/ClientApp/src/components/topology/TopologyViewer.tsx`: read-only React Flow view showing topology filtered by player's current stage (locked nodes greyed out, unlocked nodes visible, completed nodes highlighted). Different from editor — no drag/drop, just view + hover info.
- [ ] T066 [US4] Add topology data to Stage entity: extend NetworkRules JSON to include topology node positions (x, y coordinates), node type, and edge routing data to persist the visual layout
- [ ] T067 [US4] Create topology API endpoints in ScenarioController: GET /scenarios/{id}/topology (returns full topology for admin), GET /scenarios/instances/{instanceId}/topology (returns stage-filtered topology for player, only unlocked stages visible), PUT /scenarios/{id}/topology (save layout positions)
- [ ] T068 [US4] Integrate TopologyEditor into scenario create/edit page (`src/GZCTF/ClientApp/src/pages/admin/ScenarioCreate.tsx`): add topology editing step in the scenario creation wizard after stage configuration
- [ ] T069 [US4] Integrate TopologyViewer into scenario player view (`src/GZCTF/ClientApp/src/pages/game/ScenarioPlayer.tsx`): show topology panel above or alongside the stage info, update visibility on StageUnlocked events

**Checkpoint**: All four user stories now complete — topology provides visual context for both admin design and player navigation.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories and ensure production readiness per Constitution Principle I

- [x] T070 [P] Add RBAC permission checks: ensure Scenario/IRChallenge controller actions validate admin/author/player roles per Constitution Principle V in `src/GZCTF/Controllers/ScenarioController.cs` and `src/GZCTF/Controllers/IRChallengeController.cs`
- [x] T071 [P] Add structured audit logging to all scenario/IR operations (create, update, delete, instance start/end, submission review) in `src/GZCTF/Services/AuditLogService.cs` with Trace ID for full-chain tracing
- [x] T072 [P] Add loading, empty, and error states to all new frontend components per Constitution Principle I: ScenarioPlayer, IRChallengePlayer, Leaderboard, TopologyViewer, SubmissionReview
- [x] T073 Add global error boundary for scenario/IR pages using React Error Boundary in `src/GZCTF/ClientApp/src/components/scenario/ScenarioErrorBoundary.tsx`
- [ ] T074 [P] Run `specs/001-ctf-scenario-engine/quickstart.md` validation: execute all setup steps on a clean dev server, verify all verification steps pass
- [ ] T075 [P] Performance optimization: add database query indexes per data-model.md indexing strategy, verify topology rendering < 2s (SC-007), verify environment reset < 60s (SC-008)
- [x] T076 Code cleanup and consistency: ensure all new C# code follows GZCTF existing patterns (Controllers/Services/Repositories), all new React components use Mantine UI + Tailwind CSS consistently
- [ ] T077 Run full Playwright E2E test suite (`tests/e2e/`) and verify all tests pass; fix any regression issues with existing GZCTF functionality

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational (Phase 2) completion
- **User Story 2 (Phase 4)**: Depends on Foundational (Phase 2) completion — Independent of US1
- **User Story 3 (Phase 5)**: Depends on Foundational (Phase 2) completion — Builds on US1+US2 submission interfaces
- **User Story 4 (Phase 6)**: Depends on Foundational (Phase 2) completion — Extends US1 scenario model
- **Polish (Phase 7)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) — No dependencies on other stories
- **User Story 2 (P1)**: Can start after Foundational (Phase 2) — Independent of US1 (parallel)
- **User Story 3 (P2)**: Can start after Foundational (Phase 2) — Submission UI integrates with US1+US2 frontend, but scoring engine is independent. Can be developed in parallel with US1+US2 if team capacity allows, with integration after both are done.
- **User Story 4 (P3)**: Can start after US1 models are defined — Extends US1's Stage and Scenario entities

### Within Each User Story

- E2E tests MUST be written and FAIL before implementation (Constitution Principle II)
- Models before services
- Services before controllers/endpoints
- Backend before frontend integration
- Core implementation before SignalR integration
- Story complete before moving to next priority (sequential) or stories can proceed in parallel (if team capacity allows)

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel (T002, T003, T004)
- All Foundational tasks marked [P] can run in parallel within Phase 2 (T007, T008, T009, T011, T012, T013, T015, T017)
- US1 and US2 can be developed in parallel after Foundational phase (different models, controllers, pages)
- All E2E test tasks within a story marked [P] can run in parallel (T018 | T019, T034, T049, T062)
- Models within a story marked [P] can run in parallel (T021 | T022, T036 | T037)
- Frontend components within a story marked [P] can run in parallel (T029 | T028, T044 | T043)
- All Polish tasks marked [P] can run in parallel (T070, T071, T072, T074, T075)

---

## Parallel Example: User Story 1

```bash
# Launch all E2E tests for User Story 1 together:
Task: "Playwright E2E test for scenario creation flow in tests/e2e/scenario-create.spec.ts"
Task: "Playwright E2E test for scenario playthrough flow in tests/e2e/scenario-play.spec.ts"

# Launch all models for User Story 1 together:
Task: "Create Stage entity in src/GZCTF/Models/Stage.cs"
Task: "Create ScenarioInstance entity in src/GZCTF/Models/ScenarioInstance.cs"

# Launch frontend components in parallel:
Task: "Create scenario admin list/edit page in src/GZCTF/ClientApp/src/pages/admin/ScenarioList.tsx"
Task: "Create IR challenge admin list page in src/GZCTF/ClientApp/src/pages/admin/IRChallengeList.tsx"
```

## Parallel Example: User Story 1 + 2 Concurrent Development

```bash
# After Foundational phase completes, US1 and US2 can run side by side:
# Developer A: US1 tasks (T020-T033)
# Developer B: US2 tasks (T035-T048)

# Both stories use shared infrastructure (VmManager, GuacamoleProxy, EnvironmentService)
# but operate on independent models, controllers, and frontend pages
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T005)
2. Complete Phase 2: Foundational (T006-T017) — CRITICAL
3. Complete Phase 3: User Story 1 (T018-T033)
4. **STOP and VALIDATE**: Test User Story 1 independently per Independent Test criteria
5. Deploy/demo — Scenario creation + playthrough is functional

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP! 多阶段场景可用)
3. Add User Story 2 → Test independently → Deploy/Demo (IR 挑战可用)
4. Add User Story 3 → Test independently → Deploy/Demo (综合评分可用)
5. Add User Story 4 → Test independently → Deploy/Demo (拓扑可视化可用)
6. Polish → Production ready
7. Each story adds value without breaking previous stories

### Recommended Delivery Order

Due to the project's complexity and the fact that US1 and US2 share no code dependencies (only shared infrastructure from Phase 2), they can technically proceed in parallel. However, for risk reduction, the recommended approach is:

1. **Phase 1-2**: Setup + Foundational (sequential, 1 developer)
2. **Phase 3**: User Story 1 (MVP) — validate core scenario engine works end-to-end
3. **Phase 4**: User Story 2 — add IR capabilities on proven foundation
4. **Phase 5**: User Story 3 — enhance with multi-type scoring
5. **Phase 6**: User Story 4 — add topology visualization
6. **Phase 7**: Polish — production hardening

This sequential approach minimizes integration risk and allows learning from each phase before starting the next.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- E2E tests are MANDATORY per Constitution Principle II — write and verify they FAIL before implementation
- Commit after each task or logical group per Constitution Principle VI (原子化提交)
- Stop at any checkpoint to validate story independently
- All new backend code under `src/GZCTF/`, all new frontend code under `src/GZCTF/ClientApp/src/`
- All E2E tests under `tests/e2e/` at repository root
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence
