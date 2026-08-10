# TeamLab External Control Plane Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Evolve TeamLab into a stable external control plane that supports reusable scenes, image preparation, service profiles, batch rollouts, runtime operations, observability, and lifecycle management without exposing internal platform entities or execution details.

**Architecture:** TeamLab owns the control-plane resources and uses a tenant-like control scope as the external authorization boundary. Operations remain the sole async command fact, `DeploymentQueueTicket` remains the sole execution queue fact, rollout remains the sole batch coordination fact, and runtime generation remains the sole execution-generation fact. The editor layout is a versioned presentation payload of a topology, while networking and scheduling continue to consume only its logical definition.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core/PostgreSQL, Redis distributed leases, React 19, TypeScript, Mantine, SWR, React Flow, NSwag/OpenAPI.

---

## Implementation log

- 2026-08-02: Added the initial control-scope entity, public topology editor round-trip, scope grant policy, and forward migration with deterministic historical backfill. Added recovery for the persisted runtime-without-ticket case so an operation repairs the missing queue ticket instead of creating a second runtime. Main project Release build passed after these changes. Rollout lease, lifecycle, permission projection, UI workbench, Penetration migration, OpenAPI refresh, and infrastructure acceptance remain pending.
- 2026-08-03: Reconciled this plan against the current TeamLab source and the multi-round product/architecture audit. The working tree contains an unverified first slice of external rollout support: scope authorization is wired into open topology/runtime/traffic entry points; external rollout resources, target snapshots, explicit target removal, target rebuild requests, per-rollout distributed leases, transaction-coupled runtime admission, Agent pause/resume endpoints, and forward migration `20260802161119_AddExternalTeamLabRolloutControlPlane` exist. This is not an acceptance result: their generation, concurrency, migration and infrastructure proofs remain outstanding; preparation/profile/observability contracts are incomplete; TeamLab still has Penetration reverse reads; and no migration or two-node acceptance has run. Leave every task unchecked until its stated tests and boundary proof pass.
- 2026-08-03: Performed the final pre-implementation boundary review. The plan now treats the current branch as an unaccepted partial implementation: runtime admission and Agent pause/resume code exist, but their transaction, generation, rollout, migration, API and infrastructure proofs remain outstanding. The implementation must follow the decisions below; it must not add a second queue, a second state store, an internal-address API, or compensating retry loops.
- 2026-08-05: Implemented rollout-level pause/resume on top of the external rollout slice. `TeamLabRollout.PauseRequested` is a coordination desire flag (no new rollout status value); `TeamLabRolloutTargetStatus.Paused=8` is a runtime-fact projection only. New operation kinds `RolloutPause=20` / `RolloutResume=21`, `POST /api/open/v1/teamlab/rollouts/{id}/pause|resume` (202 + Idempotency-Key), and forward migration `20260805050703_AddTeamLabRolloutPauseCoordination` (PauseRequested column + composite index `(TeamLabRuntimeId, Operation, CreatedAt)` for teardown-ticket lookup). Coordinator gates `(!PauseRequested || DrainRequested)`; paused rejections use `rollout_paused`; drain always clears pause. Three independent reviews drove fixes: (a) external-command kind range helper now excludes `RuntimePause/Resume` (they were being swallowed into `ExecuteExternalCommandAsync`, breaking both endpoints); (b) teardown progress is decided by the latest `DeploymentQueueTicket` of `Operation=Destroy` (Pending/Scheduling/Scheduled/Running/Succeeded keep `Draining`; Destroyed resets a rebuild target to `Pending` or a drain target to `Destroyed`; no ticket or Failed/Cancelled falls back to `Failed` so an explicit rebuild/drain retries) — this also closes the previously-unreachable `CleanupPending` stuck-in-`Draining` path; (c) coordinator writes are convergent (Ready/Blocked steady states perform zero writes, `Revision` stops growing) and `Draining` targets are no longer flipped by the normal fact-refresh branch. Fake `Guid.CreateVersion7()` operation IDs were removed from target teardown (ticket `ApiOperationId` stays null for coordinator-internal destroy). `TeamLabRolloutCountsModel` gained `paused`; the empty desired-target set now lands in `Blocked` with `rollout_no_desired_targets` guidance instead of a vacuous `Ready`. TeamLab module error messages, coordinator diagnostics, `LastError` text and Open v1 operation descriptions were localized to Chinese (error codes and status codes untouched; `generated/Api.ts` regeneration is still pending per Task 11). Accepted deviations recorded: `RolloutSetAccess` stays one kind for both open and close (routeKey distinguishes them); paused-time rejected intents (prepare/open-access) are not queued and must be re-sent after resume; drain keeps retrying teardown until every target is destroyed; runtime-level pause of rollout-managed runtimes remains rejected, so target `Paused` is a defensive projection only. Still pending: Task 4 concurrency proof, Task 5 failure projection, Task 6 image preparation/service profiles, frontend workbench, Penetration decoupling, OpenAPI regeneration and two-node acceptance.

## Implementation decisions frozen before coding

These decisions resolve the failure, concurrency, external-API and cleanup boundaries before implementation. A later change requires an explicit architecture decision and a forward-compatible contract change.

| Boundary | Required decision | Reason and proof obligation |
| --- | --- | --- |
| Async command fact | `ApiOperation` is the only client-visible command fact. Every mutating browser, Penetration and Open API action returns the same `202 + operation URL` shape and is observed by polling/cursor queries. | A timeout or client disconnect can be recovered without guessing whether a command executed. Test duplicate browser and token submissions, changed-body conflict, and reconnect from a cursor. |
| Execution fact | `DeploymentQueueTicket` is the only durable execution intent for workload creation/destruction. It is inserted in the same EF transaction as runtime, generation, reservation and operation relation. | The ticket row is sufficient for the existing queue scanner to recover after a process crash. Post-commit notification only wakes the scanner; its loss cannot lose work. Test one committed runtime/generation/ticket tuple after an injected post-commit interruption. |
| Idempotency identity | Idempotency is scoped by `(controlScopeId, callerIdentity, routeKey, idempotencyKey, canonicalBodyHash)`. `callerIdentity` is API token ID for token calls and authenticated user ID for browser calls. | Equal keys from unrelated scopes or callers cannot collide; the same key with different content returns `409 idempotency_conflict`. No controller may create its own alternative deduplication rule. |
| Concurrent rollout coordination | One Redis distributed lease serializes one rollout coordinator pass. PostgreSQL unique constraints on rollout target, active runtime generation and ticket relation remain the final correctness guard. | Lease loss stops further admissions; already committed targets remain observable. A second process may not infer failure and recreate targets. Test two coordinators and database concurrency together. |
| Target state recovery | Only explicit operations move a target out of failure: rebuild target, remove target from desired set, or drain. `Prepare` persists/continues preparation only; it never silently resets failure. | Operators retain the failed evidence and can choose a safe recovery. No automatic retry or hidden replacement runtime. |
| Pause/resume | Pause retains the same generation, placement, address, overlay, network, access state and reservation. Resume only addresses the original Agent generation; stale identity or unavailable node returns `resume_blocked`. | Pause is not destruction and does not free capacity. Resume must never replan, redistribute an image or allocate another node. |
| Image claims | Release, rollout and runtime claims are separate references to the same distribution record. A claim is released only after the dependent runtime is terminally cleaned or the dependent rollout is drained/archived. | Shared images are not removed while another release, rollout or runtime still uses them. The registry main copy is never removed by runtime cleanup. |
| Observability | Runtime generation is the only evidence correlation key. Events, logs, flows, paths, captures, queue ticket and operation can all be filtered by it and paged by cursor. | A user can navigate from a failed asset/stage to the exact evidence without browser-side aggregation or raw Agent logs. |
| Authorization | Scope visibility, runtime state read, operational metadata read, remote-session operation and lifecycle management are independently evaluated. A permission grants only its named action. | Delegated operators can discover and inspect only authorized resources; remote access never implies reset, destroy, rollout access opening or secret visibility. |
| Public API evolution | `/api/open/v1` receives only additive endpoints/optional response fields. Public IDs, error codes, stages and cursor format are immutable within v1. Unknown documented optional input fields are ignored; invalid schema is `422`. | External clients can upgrade independently. Internal IDs, worker addresses, Agent routes, exception text and secrets never cross the boundary. |
| External notification | Cursor polling is the reliable recovery path. Webhooks are optional at-least-once notifications, signed, replayable and unable to issue business commands. | Missed, duplicated or reordered notifications cannot create duplicate workloads or decide a runtime state. |
| Archive and cleanup | Drain closes player and remote sessions, finalizes capture, destroys workloads, verifies terminal inventory, releases reservation, then releases claims. Archive is rejected while active resources exist. | Cleanup is ordered, idempotent and auditable. A timeout is not cleanup success and no destructive cascade hides a partial cleanup. |

## Dependency and acceptance order

1. Complete and prove atomic runtime admission plus scope-aware operation identity before adding any new rollout or browser command.
2. Complete rollout transition rules, lease/unique-index concurrency proof and explicit pause/resume before allowing batch lifecycle commands.
3. Complete release-scoped image preparation and service-profile catalog before making readiness or trial-run controls available to external callers.
4. Complete authorization, cursor evidence and remote-access projections before building product-facing screens that depend on them.
5. Complete the network workbench only against those stable contracts; presentation changes must not change execution digests.
6. Move Penetration onto TeamLab contracts and prove TeamLab has no Penetration persistence reads before declaring the base externally reusable.
7. Add webhooks only after polling/cursor recovery has passed. Run migration, OpenAPI, full local gate and two-node proof last as one release candidate.


## Non-negotiable rules

- Do not add a TeamLab-specific queue, log store, image-transfer worker, or browser-side scheduler.
- Do not expose WorkerNode addresses, Agent routes, credentials, WireGuard private keys, registry credentials, raw command output, or unredacted exception messages through the external API.
- Do not let TeamLab read `Penetration*` entities. Penetration must consume TeamLab contracts through an adapter/provider boundary.
- Do not introduce automatic retry as a replacement for a state transition. A failed runtime or rollout target only changes state through an explicit, auditable command.
- Do not create a second visual network model. Network regions are a persisted view projection of existing network keys.
- Keep `/api/open/v1` backward compatible: only optional fields and new endpoints. Any semantic break requires `/api/open/v2`.

## Delivery order

1. Establish scope, public contracts, atomic operations, and runtime state projections.
2. Make rollout a first-class external resource with safe concurrent coordination.
3. Close image preparation, service-profile, observability, permissions, and lifecycle contracts.
4. Build the topology workbench on top of the stable contracts.
5. Migrate Penetration onto the public TeamLab application surface and remove reverse dependencies.
6. Add optional webhook delivery only after cursor-based polling is complete and reliable.

## Confirmed implementation baseline

The plan starts from a partially implemented branch, not a clean design-only baseline. The following facts determine the remaining order and must not be mistaken for completed features:

| Area | Current code fact | Required completion boundary |
| --- | --- | --- |
| External ownership | `TeamLabControlScope`, scope grants and `TeamLabScopeAuthorizationService` exist. | Every public resource and operation query must resolve through the same scope rule, including rollout and historical operation reads. |
| Rollout | External rollout API, snapshot targets, desired-target flags, rebuild requests and a rollout lease exist. | Database uniqueness, target transition rules, operation semantics, safe failure projection and concurrent PostgreSQL proof remain required. |
| Runtime creation | A recovery path can repair a persisted runtime that lacks a queue ticket; the current branch has an unaccepted transaction-coupled admission slice. | Runtime, reservation, generation, operation relation and durable ticket must commit atomically; recovery may repair only a missing relation. |
| Lifecycle | Existing create/reset/destroy paths are operation-backed; the current branch has an unaccepted Agent pause/resume slice. | Pause/resume must be a real Agent-backed lifecycle with original allocation semantics, not a renamed stopped state. |
| Image readiness | Existing distribution and release-preparation services are available. | External callers need release-scoped preparation claims, progress, safe errors and deterministic release on drain/archive. |
| Integration boundary | Penetration registers a rollout target provider, while TeamLab still reads Penetration persistence in query paths. | TeamLab must not reference Penetration entities; Penetration becomes an adapter over the TeamLab application contracts. |
| Browser product | The vNext editor and runtime panels exist. | They must consume the same stable contracts, present Chinese recovery guidance, paginate evidence and support large-region editing without browser-side scheduling. |

## Target file structure

```text
src/GZCTF/Modules/TeamLab/
  Contracts/
    TeamLabControlScopeContracts.cs
    TeamLabRolloutContracts.cs
    TeamLabServiceProfileContracts.cs
    TeamLabImagePreparationContracts.cs
    OpenTeamLabContracts.cs
  Domain/
    TeamLabControlScope.cs
    TeamLabRuntimeOperationJob.cs
    Rollout/TeamLabRollout.cs
  Application/
    TeamLabControlScopeService.cs
    TeamLabRuntimeOperationApplicationService.cs
    TeamLabRuntimeProjectionService.cs
    TeamLabReleaseImagePreparationService.cs
    TeamLabServiceProfileCatalogService.cs
    TeamLabFailurePresentation.cs
    Rollouts/TeamLabRolloutApplicationService.cs
    Rollouts/TeamLabRolloutCoordinator.cs
  Api/
    OpenTeamLabScopesController.cs
    OpenTeamLabRolloutsController.cs
    OpenTeamLabServiceProfilesController.cs
    OpenTeamLabImagePreparationsController.cs
    OpenTeamLabRuntimesController.cs
  Infrastructure/
    Persistence/TeamLabControlScopeEntityConfiguration.cs
    TeamLabWebhookDeliveryWorker.cs

src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/
  api/teamlabControlPlaneApi.ts
  editor/regions/NetworkRegionNode.tsx
  editor/regions/NetworkRegionNode.module.css
  editor/help/teamLabFieldHelp.ts
  editor/help/FieldHelpButton.tsx
  editor/layout/autoLayoutTopology.ts
  editor/canvas/TeamLabCanvas.tsx
  editor/inspector/ServiceProfilePicker.tsx
  editor/inspector/TeamLabInspector.tsx
  rollouts/TeamLabRolloutPage.tsx
  rollouts/TeamLabRolloutPage.module.css

docs/commercialization/
  teamlab-external-control-plane-contract.md
  open-api-v1-guide.md
  openapi/open-v1.json
  openapi/open-v1.zh-CN.html
```

## Task 1: Define externally owned control scopes and public contract ownership

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabControlScope.cs`
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabControlScopeContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabControlScopeService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabControlScopeEntityConfiguration.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopology.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopologyRelease.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeAggregate.cs`
- Modify: `src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs`
- Create: a new forward EF migration named `AddTeamLabControlScopes` under `src/GZCTF/Migrations/` (the EF-generated timestamp is intentionally assigned at generation time; do not edit an existing migration)
- Test: `src/GZCTF.Test/Modules/TeamLab/TeamLabControlScopeServiceTests.cs`

- [ ] Add a `TeamLabControlScope` aggregate with immutable public ID, unique normalized key, display name, archived flag, timestamps, and no dependency on `UserInfo`, `Game`, `Course`, or Penetration entities.
- [ ] Add nullable `ControlScopeId` foreign keys to topology, release, runtime, rollout, operation job, image-preparation claim, and webhook subscription records. Backfill existing TeamLab records to one built-in platform scope in the migration; keep existing owner checks for browser administration during the transition.
- [ ] Expose scope access through explicit API-token resource grants using `teamlab-scope:{scopePublicId}`. A request must resolve its scope before resolving a resource. For a resource outside the caller scope, return the same `404 resource_not_found` contract as a nonexistent resource.
- [ ] Add application methods with these signatures:

```csharp
Task<TeamLabControlScopeModel> CreateAsync(CreateTeamLabControlScopeModel command, Guid actorId, bool administrator, CancellationToken cancellationToken);
Task<TeamLabControlScopeModel> GetAsync(Guid scopeId, TeamLabScopeActor actor, CancellationToken cancellationToken);
Task<TeamLabControlScopeModel> ArchiveAsync(Guid scopeId, TeamLabScopeActor actor, CancellationToken cancellationToken);
Task RequireResourceScopeAsync(Guid scopeId, TeamLabScopeActor actor, CancellationToken cancellationToken);
```

- [ ] Ensure archiving a scope blocks topology mutation, release publication, preparation, new rollout and new runtime commands, but allows read-only history, operation lookup, drain, and destruction.
- [ ] Write one focused test group for: isolated scope visibility, a token with an unrelated resource grant returning `404`, archived scope rejecting new commands with `scope_archived`, and the built-in scope retaining existing browser-owned topology access.
- [ ] Run: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~TeamLabControlScopeServiceTests`.

## Task 2: Make public topology contracts complete, versioned, and presentation-safe

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTopologyContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/ITeamLabTopologyApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabTopologyStructureValidator.cs`
- Test: `src/GZCTF.Test/Modules/TeamLab/OpenTopologyContractTests.cs`

- [ ] Add optional `Editor` to open topology create, update, and detail contracts. Preserve the current `TeamLabTopologyEditorModel` abstraction; do not expose React Flow node/edge objects.
- [ ] Extend editor layout with a network-region item `{ x, y, width, height, collapsed }` using the existing network key as dictionary key. Validation may reject malformed coordinates and unknown keys, but must never make deployment depend on editor data.
- [ ] Keep topology revision as the optimistic concurrency boundary for both logical definition and editor view. Publish snapshots both values, while the image/distribution digest is calculated only from the normalized logical execution definition.
- [ ] Return capabilities with explicit `editorLayoutVersion`, `networkRegions`, `serviceProfiles`, `rollouts`, `pauseResume`, and supported topology schema versions. A client with an unsupported schema receives `422 topology_schema_unsupported`; it must not silently lose fields.
- [ ] Test: an external client can round-trip a topology with regions; a layout-only update changes revision but produces the same execution digest; unknown network region keys fail validation; a runtime plan remains byte-for-byte identical after a layout-only update.
- [ ] Run targeted unit tests, then regenerate and inspect OpenAPI JSON before continuing.

## Task 3: Unify every long-running command behind Operation and atomic runtime admission

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOperationApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/EfTeamLabRuntimeOperationSubmissionStore.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRuntimeController.cs`
- Modify: `src/GZCTF/Controllers/PenetrationAdminController.cs`
- Modify: `src/GZCTF/Modules/Audit/Application/ApiOperationService.cs`
- Test: `src/GZCTF.Integration.Test/TeamLab/TeamLabOperationAdmissionTests.cs`

- [ ] Introduce operation kinds for `RuntimePause`, `RuntimeResume`, `RolloutPrepare`, `RolloutOpenAccess`, `RolloutCloseAccess`, `RolloutRebuildTarget`, `RolloutDrain`, `ScopeArchive`, and `ReleasePreparation`. Do not add direct Controller execution paths for these operations.
- [ ] Normalize idempotency identity as `(scopeId, apiTokenId or browserActorId, routeKey, idempotencyKey)` and persist the canonical request hash. Reusing the key with a changed payload returns `409 idempotency_conflict`.
- [ ] Refactor `PlanAndEnqueueAsync` so runtime, capacity reservation, deployment ticket, operation relation, and generation are committed in one EF transaction. The inserted `DeploymentQueueTicket` is the durable dispatch intent consumed by the existing queue scanner; after commit, send only a best-effort worker notification. Do not add an outbox table or a second dispatcher.
- [ ] Refactor browser admin actions and Penetration admin actions to submit the same TeamLab command/operation. Browser clients generate an idempotency key once per user action and observe the returned operation instead of assuming the immediate response is final.
- [ ] Implement recovery of a partially written pre-existing runtime by finding the operation/generation relation and creating only the missing ticket relation. Do not create a second runtime, reservation, generation or ticket.
- [ ] Integration tests: duplicate create request returns the original operation; mismatched retry returns conflict; simulated process interruption after transaction commit still produces exactly one ticket; reset/destroy/pause/resume never bypass operations; browser and API token paths produce equivalent operation facts.
- [ ] Run: `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~TeamLabOperationAdmissionTests`.

## Task 4: Implement a first-class, scope-safe rollout contract with deterministic coordination

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabRolloutContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/Rollout/TeamLabRollout.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRolloutEntityConfigurations.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/Rollouts/ITeamLabRolloutApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutCoordinator.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRolloutsController.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRolloutAuthorizationService.cs`
- Test: `src/GZCTF.Integration.Test/TeamLab/TeamLabRolloutConcurrencyTests.cs`

- [ ] Define public rollout input as `scopeId`, `releaseId`, a stable external reference, and target snapshots `{ externalSubject, displayName }`. The external reference is unique only within `(scope, adapterKind, release)`, never used for authorization.
- [ ] Make `ITeamLabRolloutTargetProvider` a provider of target synchronization only. It must not expose Penetration entities to TeamLab and must not be required for externally supplied snapshot targets.
- [ ] Add commands/endpoints for create/get/list, replace target snapshot, prepare, open access, close access, pause, resume, rebuild one failed target, drain, and archive. Every mutation returns `202` with an operation URL and requires an idempotency key.
- [ ] Acquire one distributed lease named `teamlab:rollout:{rolloutPublicId}` for each coordinator pass. If acquisition fails, skip this pass; if the lease is lost, stop before submitting the next target command. Retain the existing bounded batch size for different targets.
- [ ] Enforce target state transitions in one domain method: `Pending -> Provisioning -> Ready -> AccessOpen -> Paused -> Draining -> Destroyed`, with `Failed` reachable from provisioning/resume and only `RebuildTarget` allowed to leave `Failed`.
- [ ] Default opening policy is all-ready. A blocked rollout keeps successful targets isolated and access closed. Its only recovery actions are rebuild failed target, remove target from desired set, or drain.
- [ ] Tests: two coordinator instances only provision one runtime per target; repeated target sync is idempotent; a failed target does not reopen itself on prepare; access cannot open until every desired target is ready; target removal does not destroy a runtime until drain; drain is reentrant after partial cleanup.
- [ ] Run rollout tests plus a PostgreSQL integration test to prove the unique target/runtime indexes hold under concurrent requests.

## Task 5: Close lifecycle transitions, reservations, failure contracts, and runtime projection

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeAggregate.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/ITeamLabRuntimeApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabFailurePresentation.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- Test: `src/GZCTF.Test/Modules/TeamLab/TeamLabRuntimeLifecycleTests.cs`

- [ ] Add explicit `Paused` semantics: stop workload processes, preserve overlays/network/addresses/generation/access state, and retain a resumable capacity reservation. Do not represent pause by a generic `Stopped` status without command history.
- [ ] Resume uses the original shard/node plan and original generation. If inventory no longer satisfies the reservation, return `resume_blocked` with an explicit action (`wait_for_node`, `rebuild_runtime`, or `drain_runtime`); never silently reschedule.
- [ ] Extend runtime projection with current operation ID, ticket ID, queue status, structured sub-stage array, generation, control scope, release version, safe recovery actions, and per-asset/per-shard failure descriptors.
- [ ] Define one `TeamLabFailureDescriptor` mapper that accepts a stable code, stage, retryable value, action IDs, resource identity, and redacted detail. Replace fixed `teamlab_runtime_failed` mappings in Open contracts.
- [ ] Keep `LastError` only as protected diagnostic context. It must not become the public machine contract or be parsed by clients.
- [ ] Tests: pause preserves resource identities; resume does not call image distribution or planner; stale generation event cannot change a newer runtime; public failure projection includes code/action but excludes raw node and command details; cleanup is idempotent after a partial Agent success.
- [ ] Run lifecycle unit tests and the TeamLab runtime integration suite.

## Task 6: Expose image preparation and service-profile catalog as external control-plane resources

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabImagePreparationContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabServiceProfileContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabServiceProfileCatalogService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseImagePreparationService.cs`
- Modify: `src/GZCTF/Services/Fleet/ImageDistributionService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabImagePreparationsController.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabServiceProfilesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAdminQueryService.cs`
- Test: `src/GZCTF.Integration.Test/TeamLab/TeamLabPreparationAndProfileTests.cs`

- [ ] Define a catalog DTO containing profile ID, version, Chinese/English display name, purpose, supported asset kinds, parameter schema, defaults, sample values, execution phase, published/retired state, and documentation URL. It never exposes profile secrets or script bodies.
- [ ] Replace free-text Profile ID entry at the API boundary with a validated profile reference. Existing persisted references remain readable; migration validates them during the next publish and reports `service_profile_not_found` rather than deleting data.
- [ ] Represent release preparation as a scope/release claim with a bounded retention policy. On runtime or rollout creation, transfer ownership to runtime/rollout claims; on drain/destroy/archive, release only claims that no active runtime/rollout owns.
- [ ] Publish preparation projection by template and eligible node counts: required, ready, preparing, failed, and failure descriptor. Do not reveal individual worker network addresses to external callers.
- [ ] Make readiness distinguish `planAvailable`, `preparing`, `readyToStart`, and `blocked`; `readyToStart` is false while a required image is pending or failed.
- [ ] Tests: a shared image is distributed once but referenced by two rollouts; releasing one rollout retains cache; release archive drops only its preparation claim; unknown/retired service profile blocks publication with a stable code; an external caller can wait for preparation without querying node inventory.

## Task 7: Make observability, remote operations, and permission projections coherent

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAuthorizationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRemoteAccessAuthorizationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRuntimeController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRemoteAccessController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTrafficController.cs`
- Modify: `src/GZCTF/Repositories/LogRepository.cs`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/useRuntimeLogs.ts`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/RuntimeLogPanel.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/RuntimeEventPanel.tsx`
- Test: `src/GZCTF.Integration.Test/TeamLab/TeamLabOperationalVisibilityTests.cs`

- [ ] Consolidate four permissions: runtime state read, operational metadata read, remote session operate, lifecycle manage. Evaluate them through one authorization service for browser, Penetration provider, and API tokens.
- [ ] Permit a delegated operator to list only authorized runtimes and read their deployment state, authorized assets, safe failure summary, logs/events/traffic/captures. Do not grant lifecycle control merely because remote-session permission exists.
- [ ] Add cursor pagination and server-side filters to runtime events and logs. Every log/event/flow/path/capture query accepts generation and optional shard/asset/stage filters; response rows carry generation and safe resource display information.
- [ ] Project remote access availability in the runtime response or load it through a bounded batched endpoint. Remove unbounded `Promise.all` fan-out from `RuntimeRemoteAccessPanel`; preserve per-asset checking/error state.
- [ ] Correlate events, logs, flows, paths, captures, operation, and ticket through runtime public ID plus generation. UI links from failed asset/stage to pre-filtered evidence views.
- [ ] Tests: delegated viewer cannot reset/destroy/open player access but can inspect allowed asset diagnostics; denied scope has no runtime enumeration; log cursor reaches records beyond 100; traffic from an old generation is absent from the new generation; remote availability batch does not fail all assets when one asset is unavailable.

## Task 8: Build the network-region workbench and product-level guidance

**Files:**
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/regions/NetworkRegionNode.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/regions/NetworkRegionNode.module.css`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/help/teamLabFieldHelp.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/help/FieldHelpButton.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/inspector/ServiceProfilePicker.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/canvas/TeamLabCanvas.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/canvas/TeamLabCanvas.module.css`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/layout/autoLayoutTopology.ts`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/TeamLabDesignPage.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/TeamLabDesignPage.module.css`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/inspector/TeamLabInspector.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/inspector/AssetInspector.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/inspector/NetworkInterfacesEditor.tsx`
- Test: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/canvas/TeamLabCanvas.test.tsx`
- Test: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/editor/layout/autoLayoutTopology.test.ts`

- [ ] Render each network key as a region based on `editor.networks`. Place switch and member assets inside their network region; render routers and cross-network edges at region boundaries. Regions are visual only and cannot change connectivity by moving a node.
- [ ] Enable drag selection on empty canvas, retain direct node dragging, and reserve Space/middle-button for pan. Add region click selection, double-click region focus, all-topology fit, and current-region fit. Multi-selection summary shows network membership, cross-network connection count, and requested resources only.
- [ ] Update auto layout in deterministic passes: place network regions, place switches near region center, place member assets around switches, place routers between connected regions, route cross-region edges. Preserve manual region size when it can contain children; expand only when necessary.
- [ ] Give palette and inspector independent vertical scrolling. Keep canvas pan/zoom isolated; do not make the document body the scroll container. Validate at 390, 1366, 1920, and 2560 widths.
- [ ] Use service profile catalog data in a search/select control. Display purpose, supported assets, public parameters, defaults, sample values, execution phase, and documentation. Do not expose a raw Profile ID input in ordinary mode.
- [ ] Add shared Chinese help metadata for host offset, interface order, publish-time baking, endpoint observation, service injection, health checks, and network regions. Show help only where substantive explanation exists; retain industry acronyms only with Chinese purpose text.
- [ ] Frontend tests: empty-canvas drag produces a multi-node selection; Space drag pans rather than selects; region focus calls fitView for region children; automatic layout is stable for identical input; service picker writes only profile reference and public parameters; a long inspector scrolls without shifting the canvas.
- [ ] Run the complete front-end gate once after this unit: `pnpm validate:locales`, `pnpm lint:check`, `pnpm check`, `pnpm check:architecture`, `pnpm test`, `pnpm build`.

## Task 9: Migrate Penetration to the TeamLab control-plane boundary and remove reverse dependencies

**Files:**
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs`
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabRemoteAccessAuthorizationProvider.cs`
- Modify: `src/GZCTF/Controllers/PenetrationAdminController.cs`
- Modify: `src/GZCTF/Controllers/PenetrationPlayerController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAdminQueryService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs`
- Test: `src/GZCTF.Integration.Test/Penetration/PenetrationTeamLabBoundaryTests.cs`

- [ ] Make Penetration create or resolve its own TeamLab control scope, submit target snapshots through the rollout application contract, and retain only its game-to-scope/release/rollout binding.
- [ ] Replace all direct `TeamLabAdminQueryService` reads of `PenetrationGameLabBindings` and `PenetrationTeamRuntimeBindings` with generic TeamLab usage/scope projections. TeamLab must not use Penetration DbSets after this task.
- [ ] Preserve player behavior through a dedicated, read-only Penetration projection: not open, preparing, ready to connect, temporarily unavailable, ended. It must not reveal worker/node diagnostics.
- [ ] Map Penetration owner and delegated operator grants into the four TeamLab permission levels via providers, without copying TeamLab authorization logic into Penetration.
- [ ] Tests: TeamLab project compiles without a reference to Penetration namespaces/entities; a Penetration rollout can prepare/open/drain through TeamLab operations; player output is safe and state-specific; deleting a game drains its rollout before removing the binding.

## Task 10: Add optional webhook delivery after polling parity is proven

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabWebhookContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabWebhookSubscription.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabWebhookService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabWebhookDeliveryWorker.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabWebhooksController.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabWebhookEntityConfigurations.cs`
- Create: a new forward EF migration named `AddTeamLabWebhookSubscriptions` under `src/GZCTF/Migrations/` (the EF-generated timestamp is intentionally assigned at generation time; do not edit an existing migration)
- Test: `src/GZCTF.Integration.Test/TeamLab/TeamLabWebhookDeliveryTests.cs`

- [ ] Persist subscriptions by scope with endpoint, event-type filter, encrypted signing secret, creation/revocation timestamps, delivery cursor, and bounded failed-delivery records. Never store plaintext secret after response creation.
- [ ] Validate subscription endpoints at creation: HTTPS only; resolve and reject loopback, RFC1918, link-local, multicast, and platform-host addresses; revalidate DNS before delivery.
- [ ] Emit immutable event envelopes from the same commit that changes operation/runtime/rollout state. Include event ID, type, occurredAt, scope ID, resource type/ID, resource version, operation ID, and safe resource URL.
- [ ] Deliver at least once with `X-TeamLab-Event-Id`, timestamp, and HMAC signature; accept 2xx only; use bounded exponential retry and a replay endpoint. Delivery failure never changes runtime or rollout state.
- [ ] Tests: duplicate delivery has same event ID; a reordered delivery is detectable by resource version; private endpoint is rejected; a failed webhook does not block deployment; replay does not create a second runtime operation.

## Task 11: Documentation, OpenAPI, migration safety, and end-to-end acceptance

**Files:**
- Create: `docs/commercialization/teamlab-external-control-plane-contract.md`
- Modify: `docs/commercialization/open-api-v1-guide.md`
- Modify: `docs/commercialization/openapi/open-v1.json`
- Modify: `docs/commercialization/openapi/open-v1.zh-CN.html`
- Modify: `docs/development/current-state.md`
- Modify: `docs/commercialization/reviews/teamlab-full-flow-audit-20260802.md`

- [ ] Document all external TeamLab resources, required scopes, resource-grant rules, idempotency behavior, cursor rules, stable errors, recovery actions, quota behavior, webhook semantics, and complete examples for topology with editor regions, service profile selection, image preparation, rollout, runtime lifecycle, and observability queries.
- [ ] Generate OpenAPI from Controllers and compare it to the committed JSON. Verify every externally exposed endpoint has an operation ID, authorization scope, problem-details failures, pagination description where applicable, and no internal DTO or secret-bearing field.
- [ ] Render and visually inspect the Chinese HTML reference. It must explain concepts in Chinese, reserve English for protocol/acronym identifiers, and separate administrator browser operations from external API calls.
- [ ] Validate every migration against a PostgreSQL snapshot containing existing topologies, releases, trial runtimes, Penetration bindings, image distribution records, and remote sessions. Verify forward migration, rollback procedure, scope backfill, and no accidental delete.
- [ ] Run one consolidated validation only after all implementation tasks: `git diff --check`; full backend build/unit/integration tests; full front-end validation/build; OpenAPI compatibility check; and a real two-node acceptance that creates an external-scope topology, pre-distributes Docker and VM images, deploys a multi-region rollout, opens access, performs remote operations/traffic capture, pauses/resumes, rebuilds one failed target, drains, archives, and verifies claim/resource cleanup.
- [ ] Record actual release SHA, migration IDs, environment, acceptance evidence, remaining known limitations, and rollback release in `current-state.md`. Do not record credentials, private keys, tokens, or user-data.

## Plan self-review

- Coverage: every P1/P2 issue in `teamlab-full-flow-audit-20260802.md` maps to Tasks 1-11; no task relies on a new queue, duplicate log store, or Penetration data access from TeamLab.
- Dependency order: scope/contract and operation atomicity precede rollout, lifecycle, UI, and Penetration migration; webhooks follow polling parity.
- Failure closure: commands have idempotency, workers have rollout leases, runtimes have generations, and lifecycle operations have a single explicit recovery path.
- Compatibility: Open v1 only gains optional fields/endpoints; browser administration is migrated onto the same command surface rather than removed abruptly.

## Boundary and failure review gates

These gates are implementation acceptance criteria, not additional subsystems.

| Boundary or failure | Required behavior | Rejected shortcut |
| --- | --- | --- |
| Scope ownership | A scope is only a stable resource namespace plus an API-token grant target. It has no billing, user directory, quota engine, or parallel organization model. Existing browser ownership remains the platform adapter during migration. | Creating a generic tenant subsystem or using `externalReference` as authorization. |
| Browser and token identity | The operation store records a normalized actor identity and optional scope beside its existing token/user facts. Idempotency lookup includes that identity, route, key, and canonical body hash. Browser operations use a real server-issued actor identity, never a synthetic shared token. | Reusing the current token-only unique key for all browser actions, or treating an absent token as one common caller. |
| Publish versus editor save | A topology revision protects both execution definition and view layout. Publishing snapshots one revision. Layout-only changes never change the release execution digest, preparation claims, placements, or active runtimes. | Inferring a deployment change from editor coordinates, or discarding a concurrent editor layout silently. |
| Create interruption | Runtime, generation, reservation, ticket relation, operation job, and durable dispatch intent commit together. A recovered operation repairs only a missing durable relation after checking the unique generation/runtime constraints. | A best-effort `Task.Run`, a second ticket, or a second runtime after a timeout. |
| Concurrent rollout workers | A per-rollout lease serializes target admission; database uniqueness is the final guard. Lease loss stops new submissions and leaves already committed targets observable. | Assuming one web process, or resubmitting every target when a worker restarts. |
| Partial rollout failure | Ready targets stay isolated. `Blocked` exposes only three explicit recovery commands: rebuild one target, remove it from desired targets, or drain. Each recovery command has a new operation and preserves prior evidence. | Hidden retry during prepare, automatic access opening, or deleting good targets to mask a partial failure. |
| Pause and resume | Pause retains resource identities and reservation accounting. Resume is allowed only on the original allocation and generation; otherwise it reaches `resume_blocked` with a named next action. | Replanning silently, re-pulling images, or reporting pause as a generic stopped runtime. |
| Events and webhooks | Cursor resource/event queries are the recovery mechanism. Webhook delivery is a lossy notification optimization, independently retryable and unable to mutate runtime state. | Letting a missed webhook decide deployment completion or running business commands from webhook retries. |
| External API stability | All externally visible IDs are immutable public IDs; public responses expose stable status, stage, version, cursor and safe failure data. Unknown fields are ignored only where documented; schema incompatibility returns a stable 422 code. | Returning internal database IDs, Agent details, exception text, or using a changed enum/field meaning under `/api/open/v1`. |
| Cleanup and archival | Drain closes player/remote access, ends observability, deletes workload resources, then releases reservations and image claims in idempotent stages. Archive is allowed only once active resources are drained or an explicit archival policy records why they remain. | Releasing image claims before runtime teardown, destructive cascade deletion, or treating an operation timeout as cleanup success. |

### External-platform completion proof

Before the work is called complete, one API-token-only client, with no browser session and no direct database/Agent access, must complete this sequence inside one scope: create/update a topology with layout, validate and publish it, observe image preparation, create and manage a rollout, inspect a target failure and recover it with an explicit command, query scoped logs/traffic/capture evidence, pause/resume, drain, archive, and recover its full progress from cursor queries after a simulated client disconnect. This proof is separate from, and does not weaken, the existing browser and Penetration acceptance paths.

## Implementation log

### 2026-08-08

- Confirmed the production `ApiOperationWorker` consumes TeamLab operations after keyed handler registration and scoped-handler lifetime fixes; a submitted topology operation reached a terminal business validation result instead of remaining pending.
- Added administrator-only `teamlab-scope:*` grant validation and release editor snapshot persistence. The execution digest remains based only on canonical execution JSON.
- Fixed release editor snapshot deserialization across existing camelCase and PascalCase database values.
- Extended the runtime projection with current operation/ticket correlation, queue state, structured current stage, control scope, release version, recovery actions, and safe structured failure descriptors. Raw `LastError` remains outside the open failure contract.
- Replaced topology-scoped image-preparation references with release-GUID references. Release preparation now has a renewable 24-hour retention window; scope archival releases only release-preparation claims while rollout/runtime claims remain independent.
- Focused Task 1/2/5 verification: 23 tests passed, 0 failed. Full gates and production deployment remain pending until Tasks 6-11 are closed.
- Closed the Task 3 production blocker: API operation idempotency now uses `(scope, token, route, key)` for tokens and `(scope, browser actor, route, key)` for browser commands, with filtered PostgreSQL unique indexes. Rollout target lifecycle commands preserve the caller key; coordinator-generated keys include target and rollout revision so later pause/resume cycles cannot reuse an old operation.
- PostgreSQL operation/worker verification passed `10/10`; TeamLab unit verification passed `233/233`. The production release migration is `20260808085200_NormalizeApiOperationActorIdempotency`.
- Deployed `controlplane-20260808-0922` to `10.0.7.118` through an independent release directory, migration bundle and atomic symlink switch. API-token-only duplicate/conflict/client-poll/revoke proof passed and no pending TeamLab operation jobs remained.
- Browser screenshots at 1366 and 1920 proved the library and design page render without horizontal overflow. The same run proved Task 8 is still blocked by a `401` service-profile request from the Cookie-authenticated workbench; browser topology mutations also still bypass Operation.
- Independent review found unresolved Task 9/10 blockers: Penetration command and deletion lifecycle are not fully routed through TeamLab contracts, and webhook secret delivery, DNS rebinding resistance, transaction-scoped locking, bounded failure handling and cursor semantics are incomplete. These are recorded as blockers, not accepted deviations.
- Follow-up production acceptance deployed `controlplane-20260808-1835` to `10.0.7.118` with backup `/opt/gzctf-vnext/backups/20260808T183700Z/gzctf.dump` and atomic symlink switching. The migration head remained `20260808085200_NormalizeApiOperationActorIdempotency`; the site, Agent and two-node inventory were healthy.
- Re-ran token-only control-plane acceptance against the deployed release. An administrator wildcard grant created a scope and the browser adapter archived it; a scoped token enumerated only its grant. Five concurrent identical webhook submissions converged to one `202` operation, a changed body with the same idempotency key returned `409`, and create/revoke both reached `Succeeded` through `ApiOperationWorker`. All acceptance tokens and webhook subscriptions were revoked; database checks found zero active acceptance tokens, zero active test webhooks, and zero pending/running API operations or TeamLab operation jobs.
- Re-ran browser inspection on the deployed design page at 1366 and 1920 widths. No horizontal overflow, overlap, post-login API `401/404`, or relevant console error occurred. The Cookie-authenticated service-profile catalog now uses the management endpoint and returned `200`.
- This proves the server-side scope, idempotency, Worker, webhook command and management catalog paths. It does not replace Task 11's required full token-only two-node topology/rollout/lifecycle/observability proof, nor does it add operation/rollout terminal webhook parity; both remain explicit follow-up acceptance/design work.
