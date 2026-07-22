# TeamLab Unified Runtime Scheduling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove current TeamLab concurrency hazards and make ordinary and TeamLab workloads consume one authoritative node capacity ledger and one per-node dispatch budget.

**Architecture:** Runtime Scheduling Core owns resource arithmetic, queue fairness, reservations, and node dispatch limits. TeamLab placement keeps network-specific constraints but delegates capacity decisions to the shared core; all TeamLab Agent actions are limited by their real target node.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, Redis distributed leases, xUnit, Testcontainers.

---

## Task 1: Make Legacy Competition Submission DbContext-Safe

**Files:**
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs:83-95`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabCompetitionSubmissionTests.cs`

- [ ] **Step 1: Add the multi-team regression test**

```csharp
[Fact]
public async Task DeployGame_SubmitsEveryAcceptedTeamWithoutConcurrentContextUse()
{
    await using var fixture = await PenetrationTeamLabFixture.CreateAsync(teamCount: 3);
    var result = await fixture.Adapter.DeployGameAsync(
        fixture.GameId, fixture.ActorUserId, CancellationToken.None);

    Assert.Equal(3, result.Created + result.Reused);
    Assert.Equal(3, fixture.RuntimeService.Commands.Count);
}
```

- [ ] **Step 2: Run the regression test and verify the current implementation fails**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabCompetitionSubmissionTests
```

Expected: FAIL because the current adapter overlaps operations on one scoped context.

- [ ] **Step 3: Submit legacy targets deterministically**

Replace the shared-context `Task.WhenAll`. Node deployment remains asynchronous through the queue; only database planning and enqueue submission are serialized until Rollout replaces this entry point in Plan 03.

```csharp
var created = 0;
var reused = 0;
foreach (var teamId in teamIds.Order())
{
    var result = await DeployTeamAsync(gameId, teamId, actorUserId, cancellationToken);
    if (result.Reused) reused++;
    else created++;
}
return (created, reused);
```

- [ ] **Step 4: Run the focused test and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabCompetitionSubmissionTests
git add -- src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabCompetitionSubmissionTests.cs
git commit -m "fix: serialize legacy TeamLab target submission"
```

Expected: PASS and one focused commit.

## Task 2: Introduce The Shared Resource Vector

**Files:**
- Create: `src/GZCTF/Modules/Runtime/Domain/WorkloadResourceVector.cs`
- Modify: `src/GZCTF/Models/Data/FleetCapacityReservation.cs`
- Modify: `src/GZCTF/Models/Data/DeploymentQueueTicket.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/Persistence/WorkloadSchedulingEntityConfigurations.cs`
- Create: `src/GZCTF/Migrations/20260722090000_UnifyRuntimeResourceAccounting.cs`
- Create: `src/GZCTF.Test/UnitTests/Runtime/WorkloadResourceVectorTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/UnifiedRuntimeResourceMigrationTests.cs`

- [ ] **Step 1: Add immutable arithmetic tests**

```csharp
[Fact]
public void AvailableVector_RejectsARequestThatExceedsMemory()
{
    var total = new WorkloadResourceVector(16_000, 32_768, 500_000, 20, 4);
    var used = new WorkloadResourceVector(8_000, 24_576, 100_000, 8, 2);
    var request = new WorkloadResourceVector(2_000, 10_240, 50_000, 1, 0);

    Assert.False((total - used).CanFit(request));
}
```

- [ ] **Step 2: Implement the shared vector**

```csharp
public readonly record struct WorkloadResourceVector(
    long CpuUnits,
    long MemoryMiB,
    long StorageMiB,
    int DockerSlots,
    int VmSlots)
{
    public static WorkloadResourceVector Zero => new(0, 0, 0, 0, 0);

    public bool CanFit(WorkloadResourceVector required) =>
        CpuUnits >= required.CpuUnits &&
        MemoryMiB >= required.MemoryMiB &&
        StorageMiB >= required.StorageMiB &&
        DockerSlots >= required.DockerSlots &&
        VmSlots >= required.VmSlots;

    public static WorkloadResourceVector operator +(
        WorkloadResourceVector left, WorkloadResourceVector right) =>
        new(left.CpuUnits + right.CpuUnits,
            left.MemoryMiB + right.MemoryMiB,
            left.StorageMiB + right.StorageMiB,
            left.DockerSlots + right.DockerSlots,
            left.VmSlots + right.VmSlots);

    public static WorkloadResourceVector operator -(
        WorkloadResourceVector left, WorkloadResourceVector right) =>
        new(left.CpuUnits - right.CpuUnits,
            left.MemoryMiB - right.MemoryMiB,
            left.StorageMiB - right.StorageMiB,
            left.DockerSlots - right.DockerSlots,
            left.VmSlots - right.VmSlots);
}
```

- [ ] **Step 3: Persist full reservations and queue identities**

Add `CpuUnits`, `MemoryMiB`, and `StorageMiB` to reservations. Add non-null `TenantKey`, `FairnessKey`, and `SubjectConcurrencyKey` to queue tickets. Backfill existing tickets from team, user, runtime, and ticket identities.

```csharp
builder.HasIndex(item => new { item.Status, item.FairnessKey, item.CreatedAt });
builder.HasIndex(item => new { item.Status, item.SubjectConcurrencyKey });
builder.HasIndex(item => new { item.WorkerNodeId, item.Status, item.ExpiresAt });
```

- [ ] **Step 4: Verify migration and arithmetic, then commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~WorkloadResourceVectorTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~UnifiedRuntimeResourceMigrationTests
git add -- src/GZCTF/Modules/Runtime/Domain/WorkloadResourceVector.cs src/GZCTF/Models/Data/FleetCapacityReservation.cs src/GZCTF/Models/Data/DeploymentQueueTicket.cs src/GZCTF/Modules/Runtime/Infrastructure/Persistence/WorkloadSchedulingEntityConfigurations.cs src/GZCTF/Migrations src/GZCTF.Test/UnitTests/Runtime/WorkloadResourceVectorTests.cs src/GZCTF.Integration.Test/Tests/Database/UnifiedRuntimeResourceMigrationTests.cs
git commit -m "feat: unify runtime resource accounting"
```

Expected: both test slices PASS.

## Task 3: Normalize Queue Identity And SQL Admission

**Files:**
- Create: `src/GZCTF/Modules/Runtime/Application/WorkloadSchedulingContracts.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueModels.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueService.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/RuntimeAdmissionPolicy.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/RuntimeQueueSelector.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabInfrastructurePorts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabFleetAdapters.cs`
- Modify: `src/GZCTF.Test/UnitTests/Runtime/RuntimeControlPlaneTests.cs`

- [ ] **Step 1: Add fairness and subject serialization tests**

```csharp
[Fact]
public async Task Selector_RotatesFairnessKeysAndSerializesOneRuntimeSubject()
{
    await fixture.AddPendingAsync("competition:7", "team:1", "runtime:11");
    await fixture.AddPendingAsync("competition:7", "team:2", "runtime:12");
    await fixture.AddPendingAsync("competition:7", "team:1", "runtime:11");

    var selected = await fixture.SelectAsync();

    Assert.Equal(2, selected.Length);
    Assert.Equal(2, selected.Select(item => item.FairnessKey).Distinct().Count());
}
```

- [ ] **Step 2: Define the required scheduling identity**

```csharp
public sealed record WorkloadSchedulingIdentity(
    string TenantKey,
    string FairnessKey,
    string SubjectConcurrencyKey)
{
    public static WorkloadSchedulingIdentity ForTeam(
        int gameId, int teamId, string subject) =>
        new($"competition:{gameId}", $"team:{teamId}", subject);
}
```

Make `DeploymentQueueRequest` and `TeamLabQueueRequest` require this value. Existing direct Docker and VM callers use focused factory methods; empty keys are rejected at construction.

- [ ] **Step 3: Replace O(N) admission counting with indexed SQL**

```csharp
var pendingCount = await context.DeploymentQueueTickets.AsNoTracking()
    .CountAsync(ticket => ticket.Operation == RuntimeOperationKind.Create &&
                          ticket.Status == DeploymentQueueTicketStatus.Pending &&
                          ticket.FairnessKey == request.Identity.FairnessKey,
        token);
```

Group queue selection by persisted `FairnessKey` and block active `SubjectConcurrencyKey` values.

- [ ] **Step 4: Run the queue slice and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~RuntimeControlPlaneTests
git add -- src/GZCTF/Modules/Runtime/Application/WorkloadSchedulingContracts.cs src/GZCTF/Services/Fleet/DeploymentQueueModels.cs src/GZCTF/Services/Fleet/DeploymentQueueService.cs src/GZCTF/Modules/Runtime/Application/RuntimeAdmissionPolicy.cs src/GZCTF/Modules/Runtime/Application/RuntimeQueueSelector.cs src/GZCTF/Modules/TeamLab/Application/TeamLabInfrastructurePorts.cs src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabFleetAdapters.cs src/GZCTF.Test/UnitTests/Runtime/RuntimeControlPlaneTests.cs
git commit -m "feat: add tenant-aware runtime queue identity"
```

Expected: PASS.

## Task 4: Use One Capacity Ledger For Ordinary And TeamLab Workloads

**Files:**
- Modify: `src/GZCTF/Modules/Runtime/Application/NodeCapacitySnapshotService.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/NodeEligibilityEvaluator.cs`
- Modify: `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs`
- Modify: `src/GZCTF/Services/Fleet/TeamLabCapacityFacts.cs`
- Create: `src/GZCTF.Test/UnitTests/Runtime/UnifiedCapacityAccountingTests.cs`

- [ ] **Step 1: Add mixed-workload accounting tests**

```csharp
[Fact]
public async Task Snapshot_SubtractsOrdinaryAndTeamLabReservationsTogether()
{
    var snapshot = await fixture.LoadAsync(
        ordinary: Resources(cpu: 2_000, memory: 2_048, docker: 1),
        teamLab: Resources(cpu: 4_000, memory: 8_192, vm: 1));

    Assert.Equal(
        fixture.Total - Resources(cpu: 6_000, memory: 10_240, docker: 1, vm: 1),
        snapshot.Available);
}
```

Also assert a Docker-only node remains eligible for Docker when KVM is absent.

- [ ] **Step 2: Expose total, actual, reserved, safety, and available vectors**

```csharp
public WorkloadResourceVector Available =>
    Total - Actual - Reserved - SafetyMargin;
```

Reservations include every active `FleetCapacityReservation`, regardless of workload kind.

- [ ] **Step 3: Compare capabilities and vectors separately**

```csharp
public string? GetReason(
    NodeCapacitySnapshot snapshot,
    NodeCapability requiredCapability,
    WorkloadResourceVector required,
    IReadOnlyCollection<string>? requiredFeatures = null)
```

Return stable reason codes for capability, CPU, memory, storage, Docker slots, and VM slots.

- [ ] **Step 4: Translate ordinary Docker and VM requests into the same vector**

Use their declared specifications and existing defaults. Do not introduce TeamLab defaults into common services.

- [ ] **Step 5: Run the capacity slice and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~UnifiedCapacityAccountingTests
git add -- src/GZCTF/Modules/Runtime/Application/NodeCapacitySnapshotService.cs src/GZCTF/Modules/Runtime/Application/NodeEligibilityEvaluator.cs src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs src/GZCTF/Services/Fleet/TeamLabCapacityFacts.cs src/GZCTF.Test/UnitTests/Runtime/UnifiedCapacityAccountingTests.cs
git commit -m "feat: account for mixed workload capacity"
```

Expected: PASS.

## Task 5: Upgrade TeamLab Placement And Atomic Reservation

**Files:**
- Modify: `src/GZCTF/Modules/Runtime/Application/TeamLabPhysicalPlacementService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAssetPlanner.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/RuntimeSchedulingOptions.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlacementCapacityTests.cs`

- [ ] **Step 1: Add heterogeneous placement tests**

```csharp
[Fact]
public async Task Placement_UsesDeclaredResourcesWithoutSplittingANetwork()
{
    var result = await fixture.PlaceAsync(
        Network("entry", Docker(cpu: 1_000, memory: 512)),
        Network("domain", Vm(cpu: 8_000, memory: 16_384, storage: 80_000)));

    Assert.Equal(fixture.LargeKvmNodeId, result.NodeFor("domain"));
    Assert.Single(result.NodesForNetwork("domain"));
}
```

Add deterministic output and all-or-nothing reservation cases.

- [ ] **Step 2: Sum a resource vector per placement group**

Reject a single group that fits no node with `single_network_capacity_exceeded`. Preserve capability filters and minimize cross-node edges after feasibility.

- [ ] **Step 3: Bound local placement improvement**

Add `PlacementImprovementPasses` and `PlacementComputationBudgetMs`. Stop on no improvement or elapsed budget. Emit elapsed time, group count, edge count, and pass count.

- [ ] **Step 4: Revalidate and reserve atomically**

Compute from a versioned snapshot outside the transaction. Under `fleet:scheduler`, reload capacity, revalidate all shard vectors, and insert all reservations in one transaction. On mismatch insert none and return `capacity_changed`.

- [ ] **Step 5: Run placement tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabPlacementCapacityTests
git add -- src/GZCTF/Modules/Runtime/Application/TeamLabPhysicalPlacementService.cs src/GZCTF/Modules/TeamLab/Application/TeamLabAssetPlanner.cs src/GZCTF/Modules/Runtime/Application/RuntimeSchedulingOptions.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlacementCapacityTests.cs
git commit -m "feat: reserve TeamLab declared resources"
```

Expected: PASS.

## Task 6: Enforce Real Node Dispatch Budgets

**Files:**
- Modify: `src/GZCTF/Modules/Runtime/Application/NodeDispatchLimiter.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/RuntimeExecutionService.cs:137-177`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs:127-156`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs`
- Create: `src/GZCTF.Test/UnitTests/Runtime/NodeDispatchBudgetTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabDeploymentOrchestrationTests.cs`

- [ ] **Step 1: Add cross-runtime node budget tests**

```csharp
Assert.True(fixture.MaxObserved(nodeA, NodeDispatchCategory.VmCreate) <= 1);
Assert.True(fixture.ObservedOverlap(nodeA, nodeB));
```

Run two TeamLab runtimes against one node and against separate nodes. Cover Docker create, VM create, image transfer, network apply, probe, control, and cleanup.

- [ ] **Step 2: Add explicit probe, workload-control, and cleanup categories**

Keep existing transfer, Docker, VM, and TeamLab network categories. Limits come from capability manifest with platform safety caps.

- [ ] **Step 3: Wrap each Agent action by its real node**

```csharp
await limiter.RunAsync(
    workerNodeId,
    NodeDispatchCategory.VmCreate,
    limits.VmCreates,
    operationToken => client.CreateVmAsync(request, operationToken),
    cancellationToken);
```

Apply the same form to network, probe, control, and cleanup methods.

- [ ] **Step 4: Remove local concurrency 16 and entry-node whole-runtime gating**

Delete the shard-local semaphore. Dependency-ready tasks may use `Task.WhenAll` because every node action is globally limited. Direct Docker/VM tickets remain node-gated; TeamLab tickets act as coordinators and rely on subject serialization plus per-action node gates.

- [ ] **Step 5: Run dispatch and orchestration tests, then commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~NodeDispatchBudgetTests|FullyQualifiedName~TeamLabDeploymentOrchestrationTests"
git add -- src/GZCTF/Modules/Runtime/Application/NodeDispatchLimiter.cs src/GZCTF/Modules/Runtime/Application/RuntimeExecutionService.cs src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs src/GZCTF.Test/UnitTests/Runtime/NodeDispatchBudgetTests.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabDeploymentOrchestrationTests.cs
git commit -m "fix: enforce node budgets across TeamLab shards"
```

Expected: PASS and measured concurrency never exceeds node budgets.

## Task 7: Make Reset And Destroy Ownership Durable

**Files:**
- Modify: `src/GZCTF/Modules/Penetration/Domain/PenetrationTeamLabBindings.cs`
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs:139-190`
- Modify: `src/GZCTF/Modules/Penetration/Infrastructure/Persistence/PenetrationEntityConfigurations.cs`
- Create: `src/GZCTF/Migrations/20260722100000_HardenPenetrationTeamLabLifecycle.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/PenetrationTeamLabLifecycleTests.cs`

- [ ] **Step 1: Add lifecycle regression tests**

Assert concurrent reset requests cannot exceed quota, infrastructure failure releases quota, and destroy retains the binding until factual cleanup succeeds.

- [ ] **Step 2: Make reset quota reservation atomic**

Add `OperationId`, `TargetGeneration`, `Status`, and `FailureClass` to reset records. Add a unique active-intent index.

```csharp
builder.HasIndex(item => new { item.RuntimeId, item.TargetGeneration })
    .IsUnique()
    .HasFilter("\"Status\" IN (0, 1)");
```

Only succeeded and scenario-caused failures consume quota. Infrastructure failures release it.

- [ ] **Step 3: Preserve bindings through cleanup**

Add `DestroyOperationId`, `DestroyedAt`, and binding status. Queueing destroy marks `Destroying`; the completion projection marks `Destroyed` only after runtime cleanup and factual verification.

- [ ] **Step 4: Run lifecycle and migration tests, then commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~PenetrationTeamLabLifecycleTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~HardenPenetrationTeamLabLifecycle
git add -- src/GZCTF/Modules/Penetration/Domain/PenetrationTeamLabBindings.cs src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs src/GZCTF/Modules/Penetration/Infrastructure/Persistence/PenetrationEntityConfigurations.cs src/GZCTF/Migrations src/GZCTF.Test/UnitTests/TeamLab/PenetrationTeamLabLifecycleTests.cs
git commit -m "fix: preserve TeamLab lifecycle ownership"
```

Expected: PASS.

## Task 8: Unified Scheduling Acceptance Gate

**Files:**
- Create: `docs/commercialization/benchmarks/teamlab-unified-scheduling-baseline.md`
- Modify: `docs/commercialization/phase-06-runtime-scheduling-concurrency.md`

- [ ] **Step 1: Run the Runtime and TeamLab unit slice once**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~Runtime|FullyQualifiedName~TeamLab"
```

- [ ] **Step 2: Run migration and Open API integration slices once**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter "FullyQualifiedName~Database|FullyQualifiedName~OpenApi"
```

- [ ] **Step 3: Run one Release build and diff check**

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
git diff --check
```

- [ ] **Step 4: Record and commit the module baseline**

Record mixed ordinary/TeamLab accounting, observed per-node concurrency, 32-network/128-asset placement time, deterministic placement hash, and reservation rollback.

```powershell
git add -- docs/commercialization/benchmarks/teamlab-unified-scheduling-baseline.md docs/commercialization/phase-06-runtime-scheduling-concurrency.md
git commit -m "docs: record unified runtime scheduling baseline"
```

Expected: all commands PASS before Plan 02 starts.
