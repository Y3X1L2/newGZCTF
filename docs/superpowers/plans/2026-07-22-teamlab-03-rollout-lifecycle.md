# TeamLab Rollout And Runtime Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a generic rollout coordinator and complete runtime lifecycle so hundreds of scenario targets can be prepared, deployed, opened, suspended, resumed, drained, and destroyed with persistent progress.

**Architecture:** Rollout owns desired batch state and target aggregation; runtime remains the sole owner of network and workload facts. A coordinator leases rollout targets and calls public TeamLab application ports. Image preparation precedes target creation, reservations are wave-scoped, and all transitions are idempotent and recoverable.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, Redis wakeups and leases, existing Runtime Scheduling Core, Agent Docker/libvirt/network controls, OpenAPI, xUnit, Testcontainers.

---

## Task 1: Add Rollout And Target Aggregates

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Domain/Rollout/TeamLabRollout.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/Rollout/TeamLabRolloutTarget.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/Rollout/TeamLabRolloutPrimitives.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRolloutEntityConfigurations.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Migrations/20260722120000_AddTeamLabRollouts.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/TeamLabRolloutMigrationTests.cs`

- [ ] **Step 1: Add aggregate and uniqueness tests**

```csharp
[Fact]
public async Task RolloutTarget_IsUniqueByRolloutAndExternalSubject()
{
    await using var context = await fixture.CreateMigratedContextAsync();
    var rollout = RolloutFixture.Create();
    context.AddRange(rollout,
        Target(rollout, "team:17"),
        Target(rollout, "team:17"));

    await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
}
```

- [ ] **Step 2: Define rollout states**

```csharp
public enum TeamLabRolloutStatus : byte
{
    Draft = 0,
    CapacityChecking = 1,
    Blocked = 2,
    Distributing = 3,
    Verifying = 4,
    RollingOut = 5,
    Paused = 6,
    Ready = 7,
    Draining = 8,
    CleanupPending = 9,
    Completed = 10,
    Failed = 11
}

public enum TeamLabRolloutTargetStatus : byte
{
    Pending = 0,
    Preparing = 1,
    Queued = 2,
    Provisioning = 3,
    Ready = 4,
    AccessOpen = 5,
    Failed = 6,
    Draining = 7,
    Destroyed = 8,
    CleanupPending = 9
}
```

- [ ] **Step 3: Persist rollout policy and progress facts**

Store scenario version, tenant/external reference, canary size, wave size, failure threshold, desired access state, status, version concurrency token, operation IDs, timestamps, and aggregate counters. Targets store external subject, fairness key, runtime ID, wave, sanitized overlay hash, status, and last error code.

- [ ] **Step 4: Add indexes and migrate**

Use unique `(RolloutId, ExternalSubject)` and indexes on `(Status, UpdatedAt)`, `(RolloutId, Status, Wave)`, and `RuntimeId`. Do not cascade-delete runtimes or operation history.

- [ ] **Step 5: Run migration tests and commit**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabRolloutMigrationTests
git add -- src/GZCTF/Modules/TeamLab/Domain/Rollout src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRolloutEntityConfigurations.cs src/GZCTF/Models/AppDbContext.cs src/GZCTF/Migrations src/GZCTF.Integration.Test/Tests/Database/TeamLabRolloutMigrationTests.cs
git commit -m "feat: add TeamLab rollout aggregates"
```

Expected: PASS.

## Task 2: Implement Capacity Preview And Image Preparation

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Application/Rollouts/ITeamLabRolloutApplicationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutApplicationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutCapacityService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutPreparationService.cs`
- Modify: `src/GZCTF/Services/Fleet/ImageDistributionCoordinator.cs`
- Modify: `src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRolloutPreparationTests.cs`

- [ ] **Step 1: Add preview and preparation tests**

Cover a Ready scenario, 500 target forecast, insufficient CPU/memory/disk, Docker-only and KVM node selection, digest reuse, per-node transfer limits, and failed distribution visibility.

```csharp
[Fact]
public async Task Prepare_DistributesEachDigestOncePerSelectedNode()
{
    var rollout = await fixture.CreateAsync(targets: 100, sharedScenario: true);
    await fixture.PrepareAsync(rollout.Id);

    Assert.All(fixture.SelectedNodes, node =>
        Assert.Single(fixture.DistributionsFor(node, fixture.WindowsDigest)));
}
```

- [ ] **Step 2: Define the rollout application port**

```csharp
public interface ITeamLabRolloutApplicationService
{
    Task<TeamLabRolloutModel> CreateAsync(
        CreateTeamLabRolloutModel command, ActorContext actor,
        string idempotencyKey, CancellationToken token);
    Task<ApiOperationModel> CheckCapacityAsync(
        Guid rolloutId, ActorContext actor, string idempotencyKey, CancellationToken token);
    Task<ApiOperationModel> PrepareAsync(
        Guid rolloutId, ActorContext actor, string idempotencyKey, CancellationToken token);
    Task<ApiOperationModel> StartAsync(
        Guid rolloutId, ActorContext actor, string idempotencyKey, CancellationToken token);
}
```

- [ ] **Step 3: Forecast without reserving all targets**

Compile one scenario resource vector and placement constraints, multiply only for capacity reporting, and simulate across current node snapshots. Persist required versus available totals and stable blocking reasons. Do not create `FleetCapacityReservation` rows during preview.

- [ ] **Step 4: Select candidate nodes and pre-distribute artifacts**

Preparation selects nodes sufficient for the configured canary and initial wave plus safety margin. Submit one distribution claim per `(digest, node)`. Different nodes run concurrently; same-node transfers obey shared transfer limits. Status remains Distributing until every required claim is Ready.

- [ ] **Step 5: Run preparation tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabRolloutPreparationTests
git add -- src/GZCTF/Modules/TeamLab/Application/Rollouts src/GZCTF/Services/Fleet/ImageDistributionCoordinator.cs src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabRolloutPreparationTests.cs
git commit -m "feat: prepare rollout capacity and artifacts"
```

Expected: PASS.

## Task 3: Add Persistent Canary And Wave Coordination

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Rollouts/TeamLabRolloutCoordinatorWorker.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutCoordinator.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutProjectionService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/ITeamLabRuntimeApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`
- Modify: `src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRolloutCoordinatorTests.cs`

- [ ] **Step 1: Add coordinator state-machine tests**

Cover canary success, canary failure, threshold pause, resume, idempotent restart, incremental targets, per-target independent scope, and no shared DbContext concurrency.

```csharp
[Fact]
public async Task Coordinator_PausesBeforeNextWaveWhenFailureThresholdIsReached()
{
    var rollout = await fixture.CreateAsync(canary: 2, wave: 20, failureThreshold: 0.10m);
    fixture.FailTargets(1);

    await fixture.TickAsync(rollout.Id);

    Assert.Equal(TeamLabRolloutStatus.Paused, fixture.Rollout.Status);
    Assert.Equal(2, fixture.SubmittedTargetCount);
}
```

- [ ] **Step 2: Pass generic scheduling identity into runtime creation**

Extend runtime planning with `WorkloadSchedulingIdentity`. Rollout target uses tenant key from rollout, fairness key from external subject, and subject key `teamlab-runtime:{runtimePublicId}`.

- [ ] **Step 3: Lease and submit targets in independent scopes**

The worker claims a bounded target page with `FOR UPDATE SKIP LOCKED` or equivalent EF transaction. Each target is processed in an independent DI scope. It never retains an EF entity across scopes.

- [ ] **Step 4: Enforce canary and wave rules**

Submit canary first. Start a new wave only when the prior wave has terminal Ready/Failed outcomes and the rollout is not Paused. Reserve only admitted targets. A threshold breach pauses future submission but does not destroy successful targets.

- [ ] **Step 5: Persist aggregate progress projections**

Update counters transactionally from target transitions: total, pending, preparing, provisioning, ready, failed, draining, destroyed, cleanup pending, active wave, and last event sequence.

- [ ] **Step 6: Run coordinator tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabRolloutCoordinatorTests
git add -- src/GZCTF/Modules/TeamLab/Infrastructure/Rollouts src/GZCTF/Modules/TeamLab/Application/Rollouts src/GZCTF/Modules/TeamLab/Application/ITeamLabRuntimeApplicationService.cs src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabRolloutCoordinatorTests.cs
git commit -m "feat: coordinate TeamLab canary and deployment waves"
```

Expected: PASS.

## Task 4: Split Runtime Deployment, Access, And Compute States

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimePrimitives.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeAggregate.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabRuntimeContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs`
- Create: `src/GZCTF/Migrations/20260722130000_SplitTeamLabRuntimeLifecycle.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRuntimeLifecycleStateTests.cs`

- [ ] **Step 1: Add legal-transition tests**

```csharp
[Theory]
[InlineData(TeamLabAccessState.Closed, TeamLabAccessState.Opening, true)]
[InlineData(TeamLabAccessState.Open, TeamLabAccessState.Opening, false)]
public void AccessState_OnlyAllowsDeclaredTransitions(
    TeamLabAccessState from, TeamLabAccessState to, bool allowed)
{
    Assert.Equal(allowed, TeamLabRuntimeTransitions.CanTransition(from, to));
}
```

Add equivalent deployment and compute transitions plus impossible cross-state combinations.

- [ ] **Step 2: Define orthogonal states**

```csharp
public enum TeamLabDeploymentState : byte
{
    Queued, Distributing, Provisioning, Verifying, Ready, Failed, Destroying, Destroyed
}
public enum TeamLabAccessState : byte { Closed, Opening, Open, Closing }
public enum TeamLabComputeState : byte { Running, Suspending, Suspended, Resuming }
```

Backfill current Running to Ready/Closed/Running unless `IsOpenToPlayers` is true. Backfill Destroying/Destroyed and failed states explicitly.

- [ ] **Step 3: Update projection contracts without leaking old ambiguity**

Return deployment, access, and compute states separately. Keep old `status/stage` fields during one documented API deprecation window, derived from new states and never used for writes.

- [ ] **Step 4: Run state and migration tests, then commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabRuntimeLifecycleStateTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~SplitTeamLabRuntimeLifecycle
git add -- src/GZCTF/Modules/TeamLab/Domain/Runtime src/GZCTF/Modules/TeamLab/Contracts/TeamLabRuntimeContracts.cs src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs src/GZCTF/Migrations src/GZCTF.Test/UnitTests/TeamLab/TeamLabRuntimeLifecycleStateTests.cs
git commit -m "feat: split TeamLab runtime lifecycle states"
```

Expected: PASS.

## Task 5: Implement Close/Open Access And Suspend/Resume Workloads

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeOperationJob.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/ITeamLabRuntimeApplicationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Runtimes/TeamLabRuntimeLifecycleService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/ITeamLabNodeExecutor.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- Modify: `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- Create: `src/GZCTF.Agent/Services/TeamLab/TeamLabWorkloadControlService.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRuntimeLifecycleOperationTests.cs`

- [ ] **Step 1: Add operation tests**

Cover close access while compute remains Running, suspend with access already closed, resume, repeated idempotency keys, partial node failure, service restart, and generation mismatch rejection.

- [ ] **Step 2: Add operation kinds and application commands**

Add `AccessOpen`, `AccessClose`, `WorkloadsSuspend`, and `WorkloadsResume`. Each command carries runtime, expected generation, operation ID, actor, and idempotency key.

- [ ] **Step 3: Implement access close/open**

Close revokes or disables all active peers and sets Access Closed only after Agent acknowledgement. Open reapplies currently authorized sessions; it does not create a new session. Access operations do not stop containers or VMs.

- [ ] **Step 4: Implement workload suspend/resume by asset kind**

Docker uses pause/unpause. KVM uses managed libvirt suspend/resume and verifies domain state. Execute shards in parallel through node WorkloadControl budgets. Persist successful asset receipts before reporting a partial failure.

- [ ] **Step 5: Recover from partial execution**

Reconciliation compares desired compute state with Agent inventory and submits only missing actions. It never rolls back successful nodes merely because another node failed.

- [ ] **Step 6: Implement single-asset restart and rebuild**

Restart calls the native Docker/libvirt restart operation and verifies readiness. Rebuild first computes and returns dependency impact, requires explicit confirmation, deletes only the selected generation-owned asset, recreates it from the scenario digest, reapplies its network interfaces and overlays, and runs readiness. A single-asset success does not mark a failed runtime globally Ready.

- [ ] **Step 7: Run lifecycle operation tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabRuntimeLifecycleOperationTests
git add -- src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeOperationJob.cs src/GZCTF/Modules/TeamLab/Application/ITeamLabRuntimeApplicationService.cs src/GZCTF/Modules/TeamLab/Application/Runtimes src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs src/GZCTF/Modules/TeamLab/Application/ITeamLabNodeExecutor.cs src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs src/GZCTF.Agent/Controllers/TeamLabController.cs src/GZCTF.Agent/Services/TeamLab/TeamLabWorkloadControlService.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabRuntimeLifecycleOperationTests.cs
git commit -m "feat: control TeamLab access and compute independently"
```

Expected: PASS.

## Task 6: Implement Drain, Factual Destroy, And Tombstones

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeRecoveryPolicy.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/RuntimeFactReconciliationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeTombstone.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRuntimeTombstoneEntityConfiguration.cs`
- Create: `src/GZCTF/Migrations/20260722140000_AddTeamLabRuntimeTombstones.cs`
- Modify: `src/GZCTF.Test/UnitTests/Runtime/RuntimeFactReconciliationTests.cs`

- [ ] **Step 1: Add phased cleanup tests**

Assert access closes first, captures stop, workloads delete, routes/networks clean, leases and reservations release, image references release, facts verify, and only then Destroyed/tombstone is written.

- [ ] **Step 2: Persist a cleanup checkpoint per phase**

Use explicit phases and resource identities. Re-entry resumes at the first incomplete phase. Cleanup only names resources from runtime facts; no fuzzy prefix sweep is allowed.

- [ ] **Step 3: Create the minimal tombstone**

Store runtime public ID, scenario version, external reference, tenant, final generation, create/ready/destroy times, terminal result, rollout target, and audit correlation. Do not store protected overlays or private keys.

- [ ] **Step 4: Verify node facts before completion**

Require Agent inventory to report no owned container, domain, overlay, namespace, veth, route, firewall, capture, or lease. Missing/offline nodes keep CleanupPending and preserve bindings.

- [ ] **Step 5: Run reconciliation tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~RuntimeFactReconciliationTests
git add -- src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeRecoveryPolicy.cs src/GZCTF/Modules/Runtime/Application/RuntimeFactReconciliationService.cs src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeTombstone.cs src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRuntimeTombstoneEntityConfiguration.cs src/GZCTF/Migrations src/GZCTF.Test/UnitTests/Runtime/RuntimeFactReconciliationTests.cs
git commit -m "feat: make TeamLab destroy fact-driven and auditable"
```

Expected: PASS.

## Task 7: Cut Penetration Deployment Over To Generic Rollout

**Files:**
- Modify: `src/GZCTF/Modules/Penetration/Domain/PenetrationTeamLabBindings.cs`
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs`
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationWorkspaceService.cs`
- Modify: `src/GZCTF/Modules/Penetration/Infrastructure/Persistence/PenetrationEntityConfigurations.cs`
- Create: `src/GZCTF/Migrations/20260722150000_BindPenetrationGamesToTeamLabRollouts.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/PenetrationRolloutAdapterTests.cs`

- [ ] **Step 1: Add adapter boundary tests**

Assert the Penetration namespace maps games/teams to generic rollout commands, while TeamLab assemblies contain no Penetration type reference. Verify accepted teams become targets with stable external subject and fairness keys.

- [ ] **Step 2: Bind games to scenario version and rollout**

Replace active release deployment ownership with `ScenarioVersionId` and current `RolloutId`. Preserve legacy release ID only for migration audit, not runtime creation.

- [ ] **Step 3: Replace direct game-wide deployment and destroy loops**

`DeployGameAsync` creates or starts one rollout. Team addition creates an incremental target. Stop closes rollout access and starts drain. Remove the temporary serialized team loop from Plan 01 after no callers remain.

- [ ] **Step 4: Map reset and workspace through rollout target**

Resolve the target and runtime through rollout application ports. Keep competition reset quota in Penetration; invoke generic runtime reset with a stable operation ID.

- [ ] **Step 5: Run adapter tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~PenetrationRolloutAdapterTests
git add -- src/GZCTF/Modules/Penetration src/GZCTF/Migrations src/GZCTF.Test/UnitTests/TeamLab/PenetrationRolloutAdapterTests.cs
git commit -m "feat: deploy penetration games through TeamLab rollouts"
```

Expected: PASS and no direct game-wide runtime loop remains.

## Task 8: Publish Rollout And Lifecycle Open API

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRolloutsController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs`
- Modify: `src/GZCTF/Modules/Identity/Application/ApiTokenScopes.cs`
- Modify: `docs/commercialization/openapi/open-v1.json`
- Create: `src/GZCTF.Integration.Test/Tests/Api/OpenTeamLabRolloutApiTests.cs`
- Modify: `src/GZCTF.Integration.Test/Tests/Api/OpenApiDocumentationTests.cs`

- [ ] **Step 1: Add route contract tests**

Cover rollout create/get, capacity check, prepare, start, pause, resume, access open/close, suspend/resume workloads, delete/drain, target listing, runtime asset restart/rebuild, operation polling, idempotency, authorization, and state conflict errors.

- [ ] **Step 2: Implement the approved routes**

Implement these rollout routes:

- `GET/POST /api/open/v1/teamlab/rollouts`
- `GET /api/open/v1/teamlab/rollouts/{id}`
- `POST /api/open/v1/teamlab/rollouts/{id}/capacity-check`
- `POST /api/open/v1/teamlab/rollouts/{id}/prepare`
- `POST /api/open/v1/teamlab/rollouts/{id}/start`
- `POST /api/open/v1/teamlab/rollouts/{id}/pause`
- `POST /api/open/v1/teamlab/rollouts/{id}/resume`
- `POST /api/open/v1/teamlab/rollouts/{id}/open-access`
- `POST /api/open/v1/teamlab/rollouts/{id}/close-access`
- `POST /api/open/v1/teamlab/rollouts/{id}/suspend`
- `POST /api/open/v1/teamlab/rollouts/{id}/resume-workloads`
- `DELETE /api/open/v1/teamlab/rollouts/{id}`
- `GET /api/open/v1/teamlab/rollouts/{id}/targets`

Implement these runtime routes in addition to the existing GET/reset/destroy contract:

- `POST /api/open/v1/teamlab/runtimes/{id}/open-access`
- `POST /api/open/v1/teamlab/runtimes/{id}/close-access`
- `POST /api/open/v1/teamlab/runtimes/{id}/suspend`
- `POST /api/open/v1/teamlab/runtimes/{id}/resume`
- `POST /api/open/v1/teamlab/runtimes/{id}/assets/{assetKey}/restart`
- `POST /api/open/v1/teamlab/runtimes/{id}/assets/{assetKey}/rebuild`

Every write returns `202 Accepted` with an operation. GET endpoints read projections only. Bulk commands record one parent operation and child target operations; they do not wait for all targets in the HTTP request.

- [ ] **Step 3: Add cursor-based rollout and runtime event queries**

Return stable event code, outcome, time, target, node, operation, and sanitized detail. Use cursor plus limit; do not return unbounded histories.

- [ ] **Step 4: Regenerate OpenAPI and run contract tests**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter "FullyQualifiedName~OpenTeamLabRolloutApiTests|FullyQualifiedName~OpenApiDocumentationTests"
```

- [ ] **Step 5: Commit**

```powershell
git add -- src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRolloutsController.cs src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs src/GZCTF/Modules/Identity/Application/ApiTokenScopes.cs docs/commercialization/openapi/open-v1.json src/GZCTF.Integration.Test/Tests/Api/OpenTeamLabRolloutApiTests.cs src/GZCTF.Integration.Test/Tests/Api/OpenApiDocumentationTests.cs
git commit -m "feat: expose TeamLab rollout lifecycle API"
```

Expected: PASS.

## Task 9: Rollout And Lifecycle Acceptance Gate

**Files:**
- Create: `docs/commercialization/runbooks/teamlab-rollout-operations.md`
- Create: `docs/commercialization/benchmarks/teamlab-rollout-baseline.md`
- Modify: `docs/commercialization/phase-09-teamlab-networking-commercialization.md`

- [ ] **Step 1: Run TeamLab rollout, runtime, and recovery test slices once**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabRollout|FullyQualifiedName~TeamLabRuntime|FullyQualifiedName~RuntimeFactReconciliation|FullyQualifiedName~PenetrationRollout"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabRollout|FullyQualifiedName~OpenApi"
```

- [ ] **Step 2: Run one Release build and diff check**

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
git diff --check
```

- [ ] **Step 3: Record module evidence**

Record artifact preparation, canary, two waves, threshold pause/resume, access close/open, workload suspend/resume, main-service restart recovery, batch drain, cleanup pending, final fact verification, and tombstone.

- [ ] **Step 4: Commit documentation**

```powershell
git add -- docs/commercialization/runbooks/teamlab-rollout-operations.md docs/commercialization/benchmarks/teamlab-rollout-baseline.md docs/commercialization/phase-09-teamlab-networking-commercialization.md
git commit -m "docs: add TeamLab rollout operations runbook"
```

Expected: all commands PASS before Plan 04 starts.
