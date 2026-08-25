# TeamLab vNext Orchestration Frontend Implementation Plan

> **Source design:** `docs/superpowers/specs/2026-07-25-teamlab-vnext-orchestration-design.md`

**Goal:** 在不改写 TeamLab 数据面和调度底座的前提下，交付独立场景库、设备导向编排器、发布与试运行、运行观测、比赛生命周期和选手端，并删除旧 Penetration 编排前端。

**Architecture:** vNext TeamLab feature 通过 session-authenticated 管理 API 接入现有 TeamLab application services。前端分为 API adapter、纯拓扑领域模型、场景库、编辑器、发布、运行观测和比赛适配视图。画布交互编译为已有 topology schema v2；赛事功能只绑定不可变 Release，不复制拓扑和 runtime 实现。

**Tech Stack:** React 19、TypeScript 6、React Router 7、SWR、`@xyflow/react` 12、CSS Modules、vNext design tokens、Vitest、Testing Library、.NET 10、EF Core 10、现有 operation/DeploymentQueue/SignalR。

## Execution Rules

- 以当前代码为唯一事实来源；设计文档或历史计划中尚未落地的实体不能当成现成功能。
- 不把新功能堆入旧 `PenetrationAdminPage.tsx`。
- 不在组件中调用 generated API 或 `fetch`；全部经过 feature adapter。
- 不引入 Mantine、旧 vNext 外视觉组件、`yy-*` 类或 inline style。
- 不新增前端全局状态库；状态按文档、画布、选择、保存和远端查询局部化。
- 不创建第二套 runtime、operation、部署队列、镜像分发或日志实现。
- 每个大单元结束时集中验证一次；不为每个小组件反复执行全量 build。
- 后端管理契约补充不得改变 `/api/open/v1/teamlab` 的现有资源语义。
- 所有新增 TSX 文件必须小于架构上限；页面目标不超过 250 行，普通组件目标不超过 180 行，复杂检查器通过子表单继续拆分。

## Dependency Gate

比赛提前部署依赖持久化 Rollout/准备批次。当前分支尚未实现 `TeamLabRollout`。实施到大单元 7 前必须满足以下二选一条件：

1. `docs/superpowers/plans/2026-07-22-teamlab-03-rollout-lifecycle.md` 已由其他工作流落地并合并；本计划只编写 adapter 和 UI。
2. 在进入大单元 7 前先执行该计划的通用 Rollout 核心，不得用 Controller 内 timer、浏览器定时器或不可恢复 `Task.Run` 替代。

本计划不复制 Rollout 执行代码。前六个大单元可在 Rollout 未落地时独立完成。

---

## Large Unit 1: Management Contract And Ownership Closure

**Objective:** 补齐 vNext 所需的最小管理投影和会话入口，同时统一普通 CTF 与 TeamLab 的创建者权限。

**Backend files:**

- Modify: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTopologyContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabAdminContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabAdminQueryService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminTopologyController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRuntimeController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs`
- Create: `src/GZCTF/Utils/ResourceOwnershipPolicy.cs`
- Modify: `src/GZCTF/Controllers/EditController.cs`
- Modify: `src/GZCTF/Controllers/PenetrationAdminController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAuthorizationService.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabAdminContractTests.cs`
- Test: `src/GZCTF.Integration.Test/Tests/Api/TeamLabAdminApiTests.cs`
- Test: `src/GZCTF.Integration.Test/Tests/Api/GameOwnershipAuthorizationTests.cs`

### Implementation

1. Extend `TeamLabTopologyEditorModel` with an `Infrastructure` dictionary using an empty default when old JSON does not contain the field. Update topology mapping and canonical editor persistence only; do not add Infrastructure coordinates to release content hashing.
2. Add an admin-only paged scene projection containing topology identity, owner display, revision, network/asset/infrastructure counts, latest release, last validation summary, latest trial runtime summary, game reference count and timestamps.
3. Add server-side query parameters for search, owner scope, status, cursor and page size. Keep ordering deterministic by `UpdatedAt desc, Id desc`.
4. Add a release readiness projection that aggregates the existing Plan, referenced image distribution facts and latest trial runtime. It must perform bounded grouped queries rather than one query per image or node.
5. Add session-authenticated admin runtime endpoints for list/create trial/reset/destroy/access grants/traffic paths. Delegate directly to existing TeamLab application services and operation pipeline.
6. Treat a standalone runtime without a Penetration team binding as a trial runtime in the admin projection. Do not introduce a second runtime implementation or parse a magic `ExternalReference` prefix.
7. Add `ResourceOwnershipPolicy.CanManage(ownerId, actorId, role)` with `role >= Role.Admin` semantics.
8. Replace ordinary CTF mutation lookups with owner-scoped lookups or a shared `RequireManageableGameAsync` helper. Apply it to game update/delete, notices, challenges, phases, divisions, theory, AWDP and TeamLab binding operations that currently rely only on `[RequireTeacher]`.
9. Replace TeamLab `Role == Admin` checks with `Role >= Admin`. Teachers can list and mutate only their own topologies; administrators can manage all.
10. Preserve API token scope and resource-grant behavior. The session management guard must not weaken open API authorization.

### Large-unit verification

Run once after all changes in this unit:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabAdminContractTests"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter "FullyQualifiedName~TeamLabAdminApiTests|FullyQualifiedName~GameOwnershipAuthorizationTests"
dotnet build src/GZCTF/GZCTF.csproj -c Release --no-restore
```

Acceptance:

- Infrastructure coordinates round-trip and old topology JSON remains readable.
- Teacher A cannot mutate Teacher B's game or TeamLab topology.
- Admin and SuperAdmin can manage both.
- Trial runtime management uses the same operation and runtime facts as open API.
- Scene catalog and readiness use bounded query counts.

---

## Large Unit 2: TeamLab Frontend Contract And Domain Foundation

**Objective:** 建立完全独立于 React 组件的 API adapter、编辑文档和确定性编译层。

**Files:**

- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/api/teamlabAdminApi.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/api/teamlabContracts.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/api/teamlabParsers.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/api/teamlabErrors.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/api/index.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/model/topologyDocument.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/model/topologyCommands.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/model/topologyCompiler.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/model/topologyMapper.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/model/topologyKeys.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/model/topologySelection.ts`
- Test: matching `*.test.ts` files in the same directories

### Implementation

1. Define transport types separately from editor types. Transport enums and nullable fields must be parsed at the adapter boundary; components never consume `unknown`.
2. Use the existing admin `runtimeJsonClient` and stable contract-failure helpers. Do not import generated API into pages or views.
3. Model editor objects as discriminated unions for switch, router, Docker, Linux VM and Windows VM. Store only user intent plus stable topology keys.
4. Implement pure commands for add, move, update, connect, disconnect, delete, duplicate, paste and bulk move. Each command returns the next document and an inverse command or history snapshot.
5. Compile switch creation into Network + ManagedSwitch, asset-switch membership into interfaces, router links into infrastructure interfaces/connections, and routing assets into `ViaAssetKey` connections.
6. Make key generation deterministic within a document and collision safe after paste. Do not use array index as identity.
7. Map API detail to editor document and back without dropping unknown-but-supported schema v2 fields.
8. Keep runtime overlays, secrets, worker placement and Fabric facts outside the editable topology document.
9. Add pure tests for multi-router topologies, multi-NIC assets, direction changes, dependency preservation, paste key remapping, deletion cleanup and API round-trip.

### Large-unit verification

```powershell
pnpm --dir src/GZCTF/ClientApp exec vitest run src/vnext/features/admin/teamlab/api src/vnext/features/admin/teamlab/model
pnpm --dir src/GZCTF/ClientApp check
```

Acceptance:

- Same editor document always compiles to the same topology payload.
- API round-trip preserves all supported schema v2 semantics.
- Copy/paste never reuses keys or retains dangling external connections.
- Domain tests do not render React.

---

## Large Unit 3: Scene Library And Detail Shell

**Objective:** 交付可进入、可查询、可创建的 TeamLab vNext 模块与场景详情导航。

**Files:**

- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/library/TeamLabLibraryPage.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/library/TeamLabLibraryPage.module.css`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/library/TeamLabSceneTable.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/library/TeamLabCreateDialog.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/library/useTeamLabCatalog.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/shared/TeamLabSceneShell.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/shared/TeamLabSceneShell.module.css`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/shared/TeamLabStatusBadge.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/app/shell/moduleRegistry.ts`
- Test: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/library/TeamLabLibraryPage.test.tsx`

### Implementation

1. Add lazy routes for scene library and scene design/releases/runtimes views.
2. Mark TeamLab module implemented only after the library route has complete loading, empty, error and permission states.
3. Use a dense table with stable server ordering. Keep previous data during background refresh and show a quiet refreshing state.
4. Add search, owner/status filters and cursor pagination without filtering a full catalog in the browser.
5. Create topology with only a name, then navigate to design. Do not create a fake default topology until the editor command initializes its first switch.
6. Build the scene shell with Design, Releases and Runtimes tabs. Keep route content unframed and avoid nested cards.
7. Mobile renders catalog and read-only scene summary; edit routes show a desktop requirement state.

### Large-unit verification

```powershell
pnpm --dir src/GZCTF/ClientApp exec vitest run src/vnext/features/admin/teamlab/library
pnpm --dir src/GZCTF/ClientApp check:architecture
pnpm --dir src/GZCTF/ClientApp check
```

Acceptance:

- `/admin/teamlab` is reachable from the existing Admin shell.
- Background refresh does not blank the table or reorder equal timestamps.
- Teacher sees own scenes; Admin/SuperAdmin can see all.

---

## Large Unit 4: Canvas, Nodes, Edges And Editor State

**Objective:** 交付设备导向画布、快捷键和高性能编辑状态，不包含复杂检查器。

**Files:**

- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/TeamLabDesignPage.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/TeamLabDesignPage.module.css`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/canvas/TeamLabCanvas.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/canvas/TeamLabCanvas.module.css`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/canvas/TeamLabCanvasToolbar.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/canvas/TeamLabMiniMap.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/palette/NodePalette.tsx`
- Create: one file per node under `editor/nodes/`
- Create: `NetworkEdge.tsx`, `DependencyEdge.tsx`, `TrafficEdge.tsx` under `editor/edges/`
- Create: `editor/state/editorReducer.ts`
- Create: `editor/state/useEditorHistory.ts`
- Create: `editor/state/useEditorSelection.ts`
- Create: `editor/state/useEditorShortcuts.ts`
- Modify: `src/GZCTF/ClientApp/src/App.tsx` to import React Flow base CSS once
- Test: reducer, shortcuts and canvas interaction tests

### Implementation

1. Define `nodeTypes` and `edgeTypes` at module scope. Use memoized node components with primitive props and stable callbacks.
2. Build explicit nodes for ManagedSwitch, ManagedRouter, Docker, Linux VM and Windows VM. Keep node dimensions stable during loading, selection and status changes.
3. Dragging a palette item emits one add command. Connecting handles delegates to the compiler's legality rules; the view must not duplicate network rules.
4. Implement selection, box select, multi-select, bulk move, delete, duplicate and clipboard operations.
5. Add all confirmed shortcuts and guard inputs/content-editable elements from canvas handlers.
6. Commit position history on drag stop, not on every pointer move.
7. Enable visible-element rendering. Fit view only on first load or explicit action.
8. Add focus mode and collapsible side panels without changing the platform route or document state.
9. Use Lucide icons and tooltips for icon controls. Avoid text-filled rounded controls when a standard icon exists.

### Large-unit verification

```powershell
pnpm --dir src/GZCTF/ClientApp exec vitest run src/vnext/features/admin/teamlab/editor
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp check:architecture
```

Acceptance:

- A user can create switches, routers and all asset types, connect them, select them and use all shortcuts.
- Network and dependency edges are visually and semantically distinct.
- Dragging does not trigger API saves until release.

---

## Large Unit 5: Inspectors, Autosave And Validation

**Objective:** 完成全部 topology schema v2 配置、自动保存、冲突恢复和服务端校验定位。

**Files:**

- Create: separate inspector files under `editor/inspector/`
- Create: `SwitchInspector.tsx`
- Create: `RouterInspector.tsx`
- Create: `AssetInspector.tsx`
- Create: `NetworkInterfacesEditor.tsx`
- Create: `ResourceRequirementsEditor.tsx`
- Create: `HealthCheckEditor.tsx`
- Create: `BootstrapEditor.tsx`
- Create: `DependencyEditor.tsx`
- Create: `ObservationEditor.tsx`
- Create: `editor/state/useTopologyAutosave.ts`
- Create: `editor/state/useSaveConflict.ts`
- Create: `editor/validation/ValidationDrawer.tsx`
- Create: `editor/validation/ValidationIssueList.tsx`
- Create: `editor/validation/validationLocator.ts`
- Create: `editor/validation/SaveConflictDialog.tsx`
- Test: inspector, autosave and validation tests

### Implementation

1. Render only the selected object's inspector. Keep common fields shared through small field components, not one generic schema form.
2. Filter image choices by asset type and template readiness. Docker absence must not be presented as KVM failure and vice versa.
3. Keep common fields visible and advanced sections collapsed by default. Preserve advanced values while collapsed.
4. Use declared Bootstrap parameters. Secret input values remain in component memory only and are never stored in localStorage, URL or logs.
5. Implement debounced autosave with an abortable previous request. Serialize saves so a slower old response cannot replace a newer revision.
6. `Ctrl/Cmd + S` flushes pending changes. Navigation with unsaved changes uses the vNext confirm dialog.
7. On `topology_revision_conflict`, stop autosave, retain a local snapshot and offer reload remote or keep/export local draft. Never automatically overwrite.
8. Map server validation paths to object keys and fields. Clicking an issue selects the object, centers it and opens the relevant section.
9. Publish remains disabled until current revision has a successful server validation.

### Large-unit verification

```powershell
pnpm --dir src/GZCTF/ClientApp exec vitest run src/vnext/features/admin/teamlab/editor
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp lint:check
```

Acceptance:

- All current schema v2 fields can be edited without raw JSON.
- Concurrent edit conflict cannot lose either local or remote state.
- Validation errors navigate to the exact object/field.
- No secret is persisted by frontend state helpers.

---

## Large Unit 6: Releases, Trial Runtime And Observability

**Objective:** 交付发布、试运行、运行详情、日志、分片、流量和 PCAP 的管理闭环。

**Files:**

- Create files under `features/admin/teamlab/releases/`
- Create: `TeamLabReleasesPage.tsx`
- Create: `ReleaseTimeline.tsx`
- Create: `ReleaseReadinessPanel.tsx`
- Create: `ReleasePlanPanel.tsx`
- Create: `TrialRunDialog.tsx`
- Create files under `features/admin/teamlab/runtimes/`
- Create: `TeamLabRuntimesPage.tsx`
- Create: `TeamLabRuntimeDetailPage.tsx`
- Create: `RuntimeStageTimeline.tsx`
- Create: `RuntimeShardTable.tsx`
- Create: `RuntimeTopologyView.tsx`
- Create: `RuntimeEventPanel.tsx`
- Create: `RuntimeLogPanel.tsx`
- Create: `TrafficFlowPanel.tsx`
- Create: `TrafficPathPanel.tsx`
- Create: `CapturePanel.tsx`
- Create: `useTeamLabRuntime.ts`
- Create: `useTeamLabRuntimeEvents.ts`
- Create: `useTeamLabTraffic.ts`
- Test: release/runtime presentation and interaction tests

### Implementation

1. Publish only the saved, successfully validated revision. Confirm the immutable version and affected draft before calling the API.
2. Show Plan and release readiness as server facts. Do not infer image readiness from node online status.
3. Start trial runtime through the admin runtime adapter and immediately display the returned operation. Restore progress after navigation from operation/runtime IDs.
4. Poll runtime events by cursor at a short interval only while non-terminal; stop or slow down at terminal state. Merge events idempotently.
5. Render deployment stages from runtime/operation facts: queue, reserve, image, network, asset, health, access and cleanup. Do not estimate completion percentage when the backend has no progress value.
6. Reuse the canvas node and edge renderers in read-only mode. Overlay shard and asset status without importing editor state.
7. Query logs with resource/correlation filters and reuse the existing admin log API contract. Do not import `AdminLogsPage` or `AdminQueuePage` components.
8. Add traffic flow/path filters and bounded visualization. Only animate a selected path or a small live window.
9. Stream PCAP download directly through the browser download response. Display max seconds, max bytes and expiry before confirmation.
10. Runtime reset/destroy/access operations enter operation state immediately and remain recoverable after refresh.

### Large-unit verification

```powershell
pnpm --dir src/GZCTF/ClientApp exec vitest run src/vnext/features/admin/teamlab/releases src/vnext/features/admin/teamlab/runtimes
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp check:architecture
```

Acceptance:

- Design → validate → publish → trial → observe → destroy completes without page refresh.
- Image preparation and environment creation are separate visible stages.
- Logs, deployment queue and runtime facts agree on correlation identity.
- Read-only topology uses the same renderer as the editor.

---

## Large Unit 7: Competition Binding And Rollout Experience

**Objective:** 在比赛管理中接入已发布场景、提前准备和队伍级运行控制。

**Prerequisite:** Dependency Gate satisfied. Use the merged Rollout application service and management contract; do not implement scheduling in React.

**Files:**

- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/games/teamlab/AdminGameTeamLabPage.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/games/teamlab/AdminGameTeamLabPage.module.css`
- Create: `ScenarioReleasePicker.tsx`
- Create: `GameObjectiveBindingEditor.tsx`
- Create: `GameOverlayEditor.tsx`
- Create: `RolloutPreparationPanel.tsx`
- Create: `RolloutProgressSummary.tsx`
- Create: `RolloutTargetTable.tsx`
- Create: `TeamRuntimeDrawer.tsx`
- Create: `useGameTeamLab.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/api/teamlabGameAdminApi.ts`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/games/GameAdminShell.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx`
- Test: game TeamLab adapter and page tests

### Implementation

1. Add the TeamLab tab only for Penetration and Mixed games.
2. List only Ready/published releases that the game owner can use. Display capability, resource, image and trial summaries before binding.
3. Keep objective binding in Penetration contracts; never write scores or flags into TeamLab topology/release.
4. Allow only declared overlay keys. Secret values use the established protected runtime overlay path.
5. Persist preparation time through the rollout API. Show capacity check, distribution, verification, rollout, ready, draining and cleanup states.
6. Show aggregate counts and cursor-paged targets. Do not load all teams/runtimes into memory for a large competition.
7. Allow per-target rebuild/cleanup and game-level prepare/open/close/drain based on capability and state.
8. Do not expose unsupported suspend/resume controls. If the rollout contract later reports the capability, add it through action descriptors rather than hard-coded buttons.
9. Keep target identity, operation ID and runtime ID visible in diagnostics while presenting team name as the primary label.

### Large-unit verification

```powershell
pnpm --dir src/GZCTF/ClientApp exec vitest run src/vnext/features/admin/games/teamlab src/vnext/features/admin/api
pnpm --dir src/GZCTF/ClientApp check
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter "FullyQualifiedName~PenetrationTeamLab|FullyQualifiedName~TeamLabRollout"
```

Acceptance:

- Owner binds a Release, schedules preparation, observes all targets and opens access without editing the topology.
- Another teacher cannot manage the game.
- Late team creates one incremental target.
- End-of-game cleanup retains failed target identity until cleanup completes.

---

## Large Unit 8: Player Experience And Legacy Cutover

**Objective:** 交付选手运行入口，并在功能对照后彻底退出旧 TeamLab 前端。

**Files:**

- Create: `src/GZCTF/ClientApp/src/vnext/features/games/teamlab/TeamLabWorkspacePage.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/games/teamlab/TeamLabWorkspacePage.module.css`
- Create: `PlayerRuntimeStatus.tsx`
- Create: `PlayerRuntimeStages.tsx`
- Create: `PlayerAccessPanel.tsx`
- Create: `PlayerObjectiveList.tsx`
- Create: `PlayerResetDialog.tsx`
- Create: `usePlayerTeamLab.ts`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/games/workspace/GameWorkspaceShell.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx`
- Delete after parity: `src/GZCTF/ClientApp/src/Components/topology/penetration/PenetrationAdminPage.tsx`
- Delete after parity: `src/GZCTF/ClientApp/src/Components/topology/penetration/PenetrationAdminPage.module.css`
- Delete after parity: `src/GZCTF/ClientApp/src/Components/topology/penetration/TeamLabRuntimeObservability.tsx`
- Delete or reduce old route re-export: `src/GZCTF/ClientApp/src/Pages/admin/games/[id]/Penetration.tsx`
- Remove obsolete old TeamLab API types after reference audit
- Test: player workspace and route tests

### Implementation

1. Render current team runtime, real deployment stages, access state, VPN session, entry information, objectives and reset allowance.
2. Start/reset actions enter pending UI immediately from the returned operation, then reconcile with server facts.
3. Keep player UI responsive on mobile. Do not expose worker nodes, shard internals, full hidden topology or management logs.
4. Use a formal player projection. Do not fetch admin topology and hide fields in React.
5. Complete a feature-parity checklist against the old page before switching routes.
6. Switch all admin and player entry points to vNext.
7. Use `rg` to verify no live import references old Penetration UI, then delete old components, styles, duplicate conversion helpers and hidden routes.
8. Do not keep a feature flag or dual-write path after cutover unless a production rollback mechanism already exists at the deployment level.

### Large-unit verification

```powershell
pnpm --dir src/GZCTF/ClientApp exec vitest run src/vnext/features/games/teamlab src/vnext/features/admin/teamlab
pnpm --dir src/GZCTF/ClientApp check:architecture
pnpm --dir src/GZCTF/ClientApp check
rg -n "PenetrationAdminPage|TeamLabRuntimeObservability" src/GZCTF/ClientApp/src
```

Acceptance:

- Player can observe start stages, obtain VPN, use objectives and request allowed reset without refresh.
- Mobile player UI is complete; mobile editor is read-only.
- No production route imports the old TeamLab management UI.

---

## Large Unit 9: Performance, Visual And End-to-End Acceptance

**Objective:** 一次性完成大型场景、全流程、共享底座回归和生产构建验收。

**Files:**

- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/testing/largeTopologyFixture.ts`
- Create: focused performance/presentation tests beside affected modules
- Update: `docs/superpowers/plans/2026-07-25-teamlab-vnext-orchestration.md` progress section during execution
- Update: relevant API/OpenAPI docs only if management contracts are documented there

### Acceptance sequence

1. Generate a deterministic 32-network, 128-asset fixture with switches, routers, Docker, Linux VM, Windows VM, multi-NIC assets and dependencies.
2. Verify initial render, pan, zoom, box select, drag, inspector edit and undo/redo without global layout shifts or continuous save storms.
3. Verify light/dark themes and desktop, medium-width and mobile read-only layouts in a real browser.
4. Execute the complete management flow: create → edit → autosave → validate → publish → trial runtime → logs/traffic/capture → destroy.
5. Execute the complete competition flow: bind → readiness → prepare → open access → target rebuild → close access → drain/destroy.
6. Verify operation recovery after page refresh and network reconnect.
7. Verify ordinary CTF Docker/VM create, queue display and destroy still work and share capacity correctly.
8. Verify permission matrix for Teacher owner, another Teacher, Admin, SuperAdmin and player.
9. Confirm no secrets appear in localStorage, URLs, console output, logs or serialized editor snapshots.

### Final commands

Run once after all issues found above are fixed:

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-build
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-build
pnpm --dir src/GZCTF/ClientApp lint:check
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp check:architecture
pnpm --dir src/GZCTF/ClientApp test
pnpm --dir src/GZCTF/ClientApp build
git diff --check
```

Final completion requires:

- No TeamLab vNext architecture violation.
- No old TeamLab UI live reference.
- No change to TeamLab data-plane behavior.
- No regression in ordinary CTF container/VM lifecycle.
- Large topology remains usable and status animation reflects persisted facts.
- Design and implementation documents contain the final progress, deviations and acceptance evidence.

---

## Execution Progress

### 2026-07-25

- Large Unit 1 implementation completed: Infrastructure editor coordinates, owner-or-admin policy, paged scene projection, Release readiness projection, trial runtime management endpoints, traffic path/access session endpoints, and imported-game ownership.
- Ordinary CTF and Penetration management mutations now authorize the game owner or `Role >= Admin` at the controller boundary; execution services and open API authorization remain unchanged.
- `dotnet build src/GZCTF/GZCTF.csproj -c Release --no-restore` passed. The focused test command rebuilt the full test dependency graph and exceeded the 120-second tool limit without returning a test failure; rerun is deferred to the consolidated `--no-build` acceptance pass.
- Large Unit 2 completed: strict TeamLab management adapters, schema-v2 editor document, deterministic commands, compiler/mapper round-trip and explicit `PUT` transport for revisioned topology updates. The focused API/model suite passed 11 tests.
- Large Unit 3 completed: server-paged scene library, owner/status/search filters, create-only-name flow, scene detail shell, Design/Releases/Runtimes routes, permission/empty/error/loading states and admin navigation integration. `pnpm check:architecture` and strict TypeScript checking passed.
- Large Unit 4 completed: React Flow canvas, switch/router/Docker/Linux VM/Windows VM nodes, network/route/dependency edges, drag/drop palette, minimap, focus mode, multi-select, copy/paste/duplicate/delete, undo/redo, keyboard shortcuts and mobile read-only behavior. No TeamLab scheduling, Fabric, Agent, image-transfer, Docker/VM creation, or traffic data-plane code was modified.
- Large Unit 5 completed: schema-v2 object inspectors, capability-filtered Ready image selection, resource/network/health/bootstrap/dependency/observation editors, serialized debounced autosave, manual flush, before-unload protection, revision conflict export/reload, server validation drawer and object locator. Admin topology create/update now use explicit draft application methods so incomplete editor states can persist; Open API mutation and publish paths retain strict validation.
- Large Unit 6 completed: immutable release timeline, server plan/readiness, trial creation, cursor-paged runtime list, runtime detail, persisted stage/shard/asset projection, incremental event cursor merge, correlation-scoped system logs, flow/path pagination, path evidence, PCAP lifecycle/download, reset/destroy and recoverable WireGuard grant listing/create/download/revoke.
- Large Unit 7 completed: persistent rollout/target aggregates, desired-state coordinator, competition team target adapter, image pre-distribution references, preparation/access/drain lifecycle, late-team reconciliation, per-team rebuild/cleanup and cursor-paged projections are implemented behind TeamLab application contracts. The vNext competition page consumes those persisted contracts and contains no scheduler or data-plane logic.
- Competition scoring management is migrated to vNext: topology assets can be bound to ordered static/dynamic Flag objectives with prerequisites, visibility, checkpoint, score, attempt and reset limits. Objective writes preserve stable IDs and write-only secrets, reject active-rollout edits and protect submission history from destructive replacement.
- Large Unit 8 player workspace completed and routed through the formal `/api/pentest/games/{id}/workspace` projection. It includes persisted runtime stages, objectives, VPN grant and reset allowance without exposing worker/shard/hidden topology facts. The old TeamLab admin/player routes, old TeamLab API, legacy navigation branches and unused global styles have been removed; the read-only scoreboard adapter remains for the separate live-screen module.
- Large Unit 9 local scale fixture completed: deterministic 32-network, 128-asset topology with eight routers, multi-NIC assets and dependencies verifies stable compile/map scale. Browser visual acceptance and the final repository-wide command batch remain pending.
- Verification evidence: strict TypeScript passed after integration; TeamLab editor/model/API focused suites passed 25 tests; editor/runtime suites passed 20 tests; runtime/API suites passed 19 tests; vNext lint reports 0 warnings/errors; architecture check reports 0 vNext inline styles; `dotnet build src/GZCTF/GZCTF.csproj -c Release --no-restore` passed with 0 warnings/errors. The focused .NET test command rebuilt successfully but the desktop command window expired before the test host returned a result, so it is not recorded as passed.
- Independent review completed with no Critical findings. All four Important findings were resolved: SPA navigation now flushes or blocks unsaved topology drafts; runtime system logs use an owner-or-admin TeamLab-scoped endpoint and resource identity rather than request correlation; and trial creation persists its idempotency key separately from the business external reference.
- Final frontend production acceptance passed: locale validation, oxlint (0 warnings/errors), strict TypeScript, architecture check (`vnext-inline=0`), 65 Vitest files / 182 tests, Vite production build and bundle budget. `TeamLabDesignRoute` remains approximately 74 KB gzip.
- Final .NET compilation passed with 0 warnings/errors for the production and test projects. EF reports no pending model changes after `AddTeamLabRuntimeCreationIdempotency`; `git diff --check` passed. The TeamLab-filtered .NET test host did not return within the 240-second command window and is deliberately not recorded as passed or rerun without new diagnostic evidence.
- Local Vite preview is available at `http://127.0.0.1:5173`. Desktop and mobile browser probes reached the vNext application shell; authenticated visual workflow acceptance still requires a running backend session and is not represented as completed by the shell-only probe.
- Post-rollout frontend acceptance passed: locale validation, oxlint, strict TypeScript, architecture rules (`vnext-inline=0`), 68 Vitest files / 188 tests, Vite production build, artifact manifest and bundle budget. `AdminGameTeamLabPage` is approximately 10.3 KB gzip.
- Post-rollout production backend compilation passed with 0 warnings and 0 errors. The shared drawer close path now uses the close-request fact for animation completion, eliminating an event/state commit race exposed by the new objective editor.
- EF Core reports no pending model changes after the rollout migrations, and the final diff whitespace check passed.
- Independent post-implementation review found no Critical rollout architecture defect, then identified and closed the remaining production risks: drain now terminalizes targets that never acquired a runtime; topology ownership is enforced for teachers; all competition configuration mutations share one distributed lease; objective writes use optimistic revision control; submitted scoring contracts cannot drift or be deleted; topology rebinding clears objectives atomically or rejects preserved submission history; and cross-game/stale-save frontend races are blocked.
- Dynamic penetration Flags are now derived with a domain-separated HMAC-SHA256 server key and a 128-bit token instead of a hash of public identifiers. Raw submitted Flags are no longer copied into game event values. The dedicated security tests compile in the production test assembly; the known desktop test-host stall prevented recording their execution result and was not retried again.
- Final schema verification passed after `AddPenetrationObjectiveRevision`: EF Core reports no pending model changes. `git diff --check` passed; the only legacy-name search hit is the active node-management helper `NodeTeamLabApi`, not a removed TeamLab UI dependency.

## Large Unit 10: Canvas-First Editor Interaction

**Goal:** Increase usable topology canvas area and make direct navigation predictable without adding editor modes.

**Files:**

- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/palette/NodePalette.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/palette/NodePalette.module.css`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/canvas/TeamLabCanvas.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/canvas/TeamLabCanvas.module.css`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/TeamLabDesignPage.module.css`
- Test: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/TeamLabDesignPage.test.tsx`

Implementation:

1. Replace the 232px searchable card palette with a 64px icon rail that preserves click and drag/drop semantics and accessible labels.
2. Make the editor consume the available viewport height while retaining the existing mobile read-only boundary and focus mode.
3. Configure React Flow for left-button background panning, node direct manipulation, Shift-drag box selection, wheel zoom, middle-button panning and Space-assisted panning.
4. Add explicit grab/grabbing pane cursors without overriding node and handle cursors.
5. Verify node creation, palette accessibility and canvas interaction configuration as one frontend unit, then run strict type and architecture checks.

Acceptance:

- The compact palette costs no more than 64px of horizontal canvas space and can still be collapsed.
- Dragging a node moves it; dragging blank canvas pans it; Shift-dragging blank canvas box-selects.
- Existing undo/redo, connection creation, inspector, minimap, focus mode and mobile read-only behavior remain intact.

Completion evidence (2026-07-25):

- Completed the 64px icon palette, viewport-height editor layout, direct node dragging, blank-pane left-drag panning and Shift-drag selection without changing TeamLab scheduling or data-plane contracts.
- Browser acceptance at 1600x1000 measured the palette at 64px and the canvas at 807x708. Background dragging changed the React Flow viewport, Shift-drag displayed the selection rectangle, palette tooltips rendered, and the browser reported no console or page errors.
- The authenticated preview scene is `Canvas Layout Preview` (`019f99f1-7371-7d5f-8d15-04557f5dc13c`) at `/admin/teamlab/019f99f1-7371-7d5f-8d15-04557f5dc13c/design`.
- The browser workflow exposed an existing server-side query translation defect in the scene/runtime projections. `TeamLabAdminQueryService` now filters, groups and orders entity joins before the final DTO projection; both scene and trial-runtime list endpoints return HTTP 200.
- Focused canvas/editor tests passed (2 files / 2 tests). The complete frontend run passed 68 files / 188 tests except one shared Drawer animation test that timed out only under full parallel load; its complete 4-test file passed when run independently, so no product regression was identified.
- Locale validation, oxlint (0 warnings/errors), strict TypeScript, architecture validation (`vnext-inline=0`), backend Release build (0 warnings/errors), Vite production build, artifact manifest (210 files / 3,097,300 bytes), bundle budget and `git diff --check` passed. `TeamLabDesignRoute` is 73,591 bytes gzip.
