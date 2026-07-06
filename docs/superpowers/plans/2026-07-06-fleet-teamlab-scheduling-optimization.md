# Fleet And TeamLab Scheduling Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a production-grade environment scheduling system for Docker, VM, and TeamLab runtimes that handles multi-node, multi-team, high-concurrency creation/destruction with correct queue visibility, capacity reservation, Redis/Nginx coordination, and test-driven verification.

**Architecture:** Keep the existing Fleet/Agent/TeamLab split, but make scheduling a first-class durable operation instead of an incidental side effect. Introduce explicit queue tickets tied to business resources, atomic capacity reservation for single-resource and batch TeamLab deployments, bounded per-node execution parallelism, and player/admin visible queue status. Preserve existing Docker/VM/TeamLab deployment behavior while replacing incomplete queue semantics with a real state machine.

**Tech Stack:** ASP.NET Core, EF Core/PostgreSQL, StackExchange.Redis, background hosted services, existing GZCTF Agent HTTP APIs, Mantine/React frontend, xUnit unit tests, integration-style service tests with EF test context.

---

## Current Audit Baseline

The current code already has useful building blocks:

- `src/GZCTF/Services/Fleet/FleetManager.cs` holds `fleet:scheduler` during node selection and increments `WorkerNode.CurrentContainers` / `CurrentVms`.
- `src/GZCTF/Services/Fleet/RedisDistributedLock.cs` uses Redis locks in `RunMode=Fleet` and fails hard if Redis is unreachable.
- `src/GZCTF/Services/Fleet/PortAllocationService.cs` allocates public TCP proxy ports atomically through Redis Lua.
- `src/GZCTF/Services/Fleet/NginxSyncService.cs` periodically syncs active container mappings and can refresh Redis port reservations when local Nginx sync is enabled.
- `src/GZCTF/Models/Data/WorkerNode.cs` has `MaxContainers`, `MaxVms`, heartbeat-backed load metrics, and an EF concurrency token.
- TeamLab has runtime state, events, UDP mapping facts, WireGuard peers, and WorkerNode tunnel health checks.

The verified gaps that this plan closes:

- `QueueManager.ProcessPendingAsync` only marks `DeploymentTarget` as `Assigned`; it does not create Docker/VM resources.
- `FleetContainerManager.CreateContainerAsync` cancels queued Docker targets immediately instead of returning a durable pending ticket.
- `FleetVmService.CreateVmAsync` disables queueing with `queueWhenNoNode: false`.
- TeamLab plans and deploys directly through Agent calls without durable scheduling, batch slot reservation, or queue visibility.
- Preferred-node Docker creation bypasses the global scheduler lock.
- Team/user container limits are checked without a per-owner lock and can be raced.
- External Nginx gateway mode can let Redis TCP port reservations expire while containers still run.

## Design Principles

- Durable queue entries must always be linked to the business resource they are intended to create. No orphan containers from payload-only replay.
- Scheduling and execution are separate states: `Pending`, `Assigned`, `Creating`, `Completed`, `Failed`, `Cancelled`.
- Capacity reservation must be atomic at the node level. For TeamLab it must reserve the whole environment asset set, not one abstract slot.
- Execution should be parallel across nodes and bounded per node. A busy node should not receive unlimited concurrent create calls.
- Player-facing APIs must return useful pending state instead of generic failure when work is queued.
- Redis/Nginx coordination must be driven by active runtime facts and must be safe under high concurrency.
- Tests come first for every behavior change. Each fix must have a failing test that proves the current gap.
- Do not log flags, submitted answers, registry credentials, WireGuard private keys/config text, container environment variables, or raw `DeploymentTarget.Payload`.

## Non-Negotiable Quality Gates

Every task in this plan must satisfy these gates before it can be marked complete:

- **TDD evidence:** Record the RED command and failure reason, then the GREEN command and pass result in the Progress Tracking section. A test that never failed does not count as coverage for a behavior change.
- **No code bloat:** Prefer one focused service or model over compatibility branches and duplicate paths. Delete or bypass obsolete behavior when it conflicts with the current architecture instead of preserving dead legacy semantics.
- **Concurrency correctness:** Any code that reads capacity and then mutates capacity must be under a Redis/local distributed lock or an EF concurrency-safe update loop. No optimistic "check then slow create" path may reserve capacity after an Agent call.
- **No serial bottleneck:** Queue execution may be ordered by fairness, but actual deployment must run in parallel across different nodes and bounded per node.
- **Accurate user feedback:** Capacity exhaustion must return a queue/rejection model with reason and queue position when applicable. Generic `common.error.encountered` is not acceptable for known capacity/scheduling failures.
- **Safe observability:** Admin logs and APIs may expose deployment ids, node ids, owner ids, status, slot counts, timestamps, and trimmed errors only. They must never expose raw payloads, flags, private keys, registry auth, container env vars, or full WireGuard configs.
- **Release exactly once:** Destroy, cancel, retry, and failed create paths must prove that Docker/VM slot counters and public port leases cannot leak or go negative.
- **Frontend consistency:** If UI is touched, use existing status typography and queue-state patterns; do not introduce new visual systems for this scheduling work.
- **Regression boundary:** Ordinary CTF Docker, training Docker, VM, and TeamLab flows must still build and pass targeted tests after each group of backend changes.

## 2026-07-06 Execution Revision: Best-Fit Complete Repair

The requested repair is not a minimal patch. The implementation must converge the scheduler into one coherent production path:

- Docker, VM, and TeamLab deployments must share the same durable queue semantics, capacity reservation rules, and safe status reporting.
- Any path that can consume node capacity must reserve capacity before slow create calls, release it exactly once on failure/cancel/destroy, and avoid direct check-then-create races.
- Any path that cannot run immediately due to capacity must return a queue state or explicit capacity rejection, not `common.error.encountered`.
- Existing obsolete behavior that conflicts with the current architecture should be removed rather than preserved as compatibility branches.
- Tests must prove behavior under resource exhaustion, concurrent requests, missing business objects, failed executor calls, and successful release-after-destroy.
- Sensitive data must remain internal: flags, registry auth, WireGuard private material, raw payloads, and container environment values must not appear in queue APIs, deployment target APIs, or system logs.
- TeamLab must be treated as a batch deployment: reserve all Docker/VM slots for the topology together or do not start it. Partial starts from partial capacity are not acceptable.
- The final verification must include targeted unit tests, backend build, static secret-output scan, and an executable server smoke-test checklist.

Current implementation checkpoint before continuing:

- Docker queue ticket model, queue service, and capacity reservation are in place.
- `QueueManager` can process Docker queue tickets through `DeploymentExecutionService`.
- VM queueing work is in progress; the next RED/GREEN cycle is `CreateVmAsync_QueuesWhenNoKvmCapacityExists`.
- TeamLab batch queueing/capacity, node execution gates, port lease refresh, and final cancellation semantics are still open.

## Progress Evidence Format

For each task, append a short entry under Progress Tracking using this format:

```text
- Task N: In progress / Completed / Blocked
  - RED: <command> -> <expected failure summary>
  - GREEN: <command> -> <pass summary>
  - Notes: <capacity/security/API/cleanup decisions>
```

## File Structure

### New Files

- `src/GZCTF/Models/Data/DeploymentQueueTicket.cs`  
  Durable queue ticket that links a deployment target to a business owner/resource: game instance, exercise instance, VM instance, TeamLab runtime, action, owner team/user, queue position metadata, and timestamps.

- `src/GZCTF/Services/Fleet/DeploymentQueueModels.cs`  
  Small records/enums for queue input/output: `DeploymentQueueKind`, `DeploymentQueueOwner`, `DeploymentQueueStatusModel`, `DeploymentQueueResult`.

- `src/GZCTF/Services/Fleet/DeploymentQueueService.cs`  
  Single service for creating tickets, computing queue positions, assigning tickets, cancelling tickets, and returning safe status models.

- `src/GZCTF/Services/Fleet/DeploymentExecutionService.cs`  
  Executes assigned queue tickets by calling the correct business service and updating ticket/target state. It must not create resources from raw payload alone.

- `src/GZCTF/Services/Fleet/NodeExecutionGate.cs`  
  Per-node bounded concurrency gate for Docker/VM/TeamLab operations. Uses Redis-backed lease counters in Fleet mode and local semaphores in standalone mode.

- `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs`  
  Atomic single-resource and batch slot reservation/release service. Wraps scheduler lock, node selection, and `WorkerNode` counter persistence.

- `src/GZCTF/Services/Fleet/PortLeaseRefreshService.cs`  
  Hosted service that refreshes active Redis TCP proxy port leases from database facts even when `NginxSyncService.SyncLocalConfig=false`.

- `src/GZCTF/Services/TeamLab/TeamLabCapacityPlanner.cs`  
  Counts Docker/VM slots required by a published TeamLab topology and validates node capacity before planning/deployment.

- `src/GZCTF.Test/UnitTests/Fleet/DeploymentQueueServiceTests.cs`
- `src/GZCTF.Test/UnitTests/Fleet/FleetCapacityReservationServiceTests.cs`
- `src/GZCTF.Test/UnitTests/Fleet/NodeExecutionGateTests.cs`
- `src/GZCTF.Test/UnitTests/Fleet/PortLeaseRefreshServiceTests.cs`
- `src/GZCTF.Test/UnitTests/TeamLab/TeamLabSchedulingTests.cs`

### Modified Files

- `src/GZCTF/Models/AppDbContext.cs`  
  Register `DeploymentQueueTickets`, indexes, and relationships.

- `src/GZCTF/Models/Data/DeploymentTarget.cs`  
  Keep target as low-level deployment audit; add navigation to queue ticket if needed.

- `src/GZCTF/Services/Fleet/FleetManager.cs`  
  Delegate capacity reservation to `FleetCapacityReservationService`; keep thin compatibility methods where needed.

- `src/GZCTF/Services/Fleet/QueueManager.cs`  
  Replace assignment-only implementation with ticket selection and executor dispatch.

- `src/GZCTF/Services/Fleet/QueueProcessingService.cs`  
  Process multiple tickets per tick, grouped by node and bounded by execution gate.

- `src/GZCTF/Services/Fleet/FleetContainerManager.cs`  
  Preferred-node and automatic paths must use atomic reservation and return queue-aware results through repository/controller callers.

- `src/GZCTF/Services/Fleet/FleetVmService.cs`  
  Enable VM queueing and status reporting; release capacity only after actual failure/destroy.

- `src/GZCTF/Repositories/GameInstanceRepository.cs`  
  Add per-team lock around container limit checks and create/queue transition.

- `src/GZCTF/Repositories/ExerciseInstanceRepository.cs`  
  Add per-user lock around exercise container limit checks.

- `src/GZCTF/Controllers/GameController.cs`  
  Return `202 Accepted` queue status for Docker/VM creation when resource is queued; expose queue status for existing pending VM/container operations.

- `src/GZCTF/Controllers/TrainingCourseController.cs`  
  Return course container queue status instead of generic failure when queued.

- `src/GZCTF/Controllers/NodesController.cs`  
  Expose safe admin queue list and per-node active/queued slot usage without raw payload.

- `src/GZCTF/Controllers/TeamLabAdminController.cs`  
  Return TeamLab queue status and queue position for deploy requests.

- `src/GZCTF/Services/TeamLab/TeamLabPlanService.cs`  
  Plan with batch capacity awareness and UDP port locking.

- `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs`  
  Use batch capacity reservations, per-node execution gate, and parallel asset creation where safe.

- `src/GZCTF/ClientApp/src/Api.ts` and related API wrapper files  
  Add queue status response types.

- Player and admin pages that call container/VM/TeamLab deployment APIs  
  Display pending state and queue position without changing the existing visual language.

## Task 1: Durable Queue Ticket Model

**Files:**
- Create: `src/GZCTF/Models/Data/DeploymentQueueTicket.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/DeploymentQueueServiceTests.cs`

- [ ] **Step 1: Write failing tests for ticket uniqueness and safe ownership**

Add tests that create two tickets for the same business resource and assert the model contract only allows one active ticket.

```csharp
[Fact]
public void DeploymentQueueTicket_ActiveTicketIdentity_IsStableForGameContainer()
{
    var ticket = new DeploymentQueueTicket
    {
        Kind = DeploymentQueueKind.GameContainer,
        OwnerTeamId = 12,
        GameId = 5,
        ChallengeId = 9,
        Status = DeploymentQueueTicketStatus.Pending
    };

    Assert.Equal("game-container:5:12:9", ticket.ActiveIdentity);
}

[Fact]
public void DeploymentQueueTicket_DoesNotExposeRawPayload()
{
    var ticket = new DeploymentQueueTicket
    {
        Kind = DeploymentQueueKind.GameContainer,
        OwnerTeamId = 1,
        GameId = 2,
        ChallengeId = 3,
        Status = DeploymentQueueTicketStatus.Pending
    };

    var model = DeploymentQueueStatusModel.FromTicket(ticket, queuePosition: 4);

    Assert.Equal(4, model.QueuePosition);
    Assert.DoesNotContain("Payload", model.ToString(), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~DeploymentQueueServiceTests --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: fails because `DeploymentQueueTicket` and queue model types do not exist.

- [ ] **Step 3: Add the queue model**

Create `DeploymentQueueTicket` with explicit resource fields instead of replay-only payload:

```csharp
namespace GZCTF.Models.Data;

[Index(nameof(Status), nameof(CreatedAt))]
[Index(nameof(TargetNodeId), nameof(Status))]
[Index(nameof(ActiveIdentity), IsUnique = true)]
public class DeploymentQueueTicket
{
    [Key] public Guid Id { get; set; } = Guid.CreateVersion7();
    public DeploymentQueueKind Kind { get; set; }
    public DeploymentQueueTicketStatus Status { get; set; } = DeploymentQueueTicketStatus.Pending;
    public Guid? DeploymentTargetId { get; set; }
    public Guid? TargetNodeId { get; set; }
    public int? OwnerTeamId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public int? GameId { get; set; }
    public int? ChallengeId { get; set; }
    public Guid? VmInstanceId { get; set; }
    public int? TeamLabRuntimeId { get; set; }
    public int DockerSlots { get; set; }
    public int VmSlots { get; set; }
    [MaxLength(256)] public string ActiveIdentity { get; set; } = string.Empty;
    [MaxLength(1024)] public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public DeploymentTarget? DeploymentTarget { get; set; }
    public WorkerNode? TargetNode { get; set; }
}

public enum DeploymentQueueKind : byte
{
    GameContainer = 1,
    ExerciseContainer = 2,
    Vm = 3,
    TeamLabRuntime = 4
}

public enum DeploymentQueueTicketStatus : byte
{
    Pending = 0,
    Assigned = 1,
    Creating = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}
```

Add `DbSet<DeploymentQueueTicket>` and configure delete behavior to avoid deleting targets/resources unexpectedly.

- [ ] **Step 4: Add migration**

Run:

```powershell
dotnet ef migrations add AddDeploymentQueueTickets --project src/GZCTF/GZCTF.csproj
```

Expected: migration creates `DeploymentQueueTickets` table, indexes on status/node, and unique `ActiveIdentity`.

- [ ] **Step 5: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~DeploymentQueueServiceTests --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: tests pass.

## Task 2: Queue Service With Queue Position

**Files:**
- Create: `src/GZCTF/Services/Fleet/DeploymentQueueModels.cs`
- Create: `src/GZCTF/Services/Fleet/DeploymentQueueService.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/DeploymentQueueServiceTests.cs`

- [ ] **Step 1: Write failing tests for queue position and duplicate active tickets**

```csharp
[Fact]
public async Task EnqueueAsync_ReturnsExistingActiveTicketInsteadOfDuplicating()
{
    await using var context = TestDb.Create();
    var service = CreateQueueService(context);

    var request = DeploymentQueueRequest.GameContainer(gameId: 1, teamId: 2, challengeId: 3);
    var first = await service.EnqueueAsync(request, CancellationToken.None);
    var second = await service.EnqueueAsync(request, CancellationToken.None);

    Assert.Equal(first.TicketId, second.TicketId);
    Assert.Equal(1, await context.DeploymentQueueTickets.CountAsync());
}

[Fact]
public async Task GetStatusAsync_ReturnsOneBasedQueuePositionWithinSameKind()
{
    await using var context = TestDb.Create();
    var service = CreateQueueService(context);

    var first = await service.EnqueueAsync(DeploymentQueueRequest.GameContainer(1, 1, 1), CancellationToken.None);
    var second = await service.EnqueueAsync(DeploymentQueueRequest.GameContainer(1, 2, 1), CancellationToken.None);

    var status = await service.GetStatusAsync(second.TicketId, CancellationToken.None);

    Assert.Equal(2, status!.QueuePosition);
    Assert.Equal(1, status.PeopleAhead);
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~EnqueueAsync_ReturnsExistingActiveTicketInsteadOfDuplicating|FullyQualifiedName~GetStatusAsync_ReturnsOneBasedQueuePositionWithinSameKind" --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: fails because queue service does not exist.

- [ ] **Step 3: Implement queue request/status models**

Use records with safe fields only:

```csharp
public sealed record DeploymentQueueRequest(
    DeploymentQueueKind Kind,
    int? OwnerTeamId,
    Guid? OwnerUserId,
    int? GameId,
    int? ChallengeId,
    Guid? VmInstanceId,
    int? TeamLabRuntimeId,
    int DockerSlots,
    int VmSlots)
{
    public static DeploymentQueueRequest GameContainer(int gameId, int teamId, int challengeId) =>
        new(DeploymentQueueKind.GameContainer, teamId, null, gameId, challengeId, null, null, 1, 0);

    public static DeploymentQueueRequest Vm(int gameId, Guid userId, int challengeId, Guid vmInstanceId) =>
        new(DeploymentQueueKind.Vm, null, userId, gameId, challengeId, vmInstanceId, null, 0, 1);

    public static DeploymentQueueRequest TeamLab(int gameId, int teamId, int runtimeId, int dockerSlots, int vmSlots) =>
        new(DeploymentQueueKind.TeamLabRuntime, teamId, null, gameId, null, null, runtimeId, dockerSlots, vmSlots);
}

public sealed record DeploymentQueueStatusModel(
    Guid TicketId,
    DeploymentQueueKind Kind,
    DeploymentQueueTicketStatus Status,
    Guid? TargetNodeId,
    string? TargetNodeName,
    int QueuePosition,
    int PeopleAhead,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public static DeploymentQueueStatusModel FromTicket(DeploymentQueueTicket ticket, int queuePosition) =>
        new(ticket.Id, ticket.Kind, ticket.Status, ticket.TargetNodeId, ticket.TargetNode?.Name,
            queuePosition, Math.Max(0, queuePosition - 1), ticket.ErrorMessage, ticket.CreatedAt,
            ticket.StartedAt, ticket.CompletedAt);
}
```

- [ ] **Step 4: Implement `DeploymentQueueService`**

Key behavior:

- Compute `ActiveIdentity` from kind and business IDs.
- Reuse existing tickets in `Pending`, `Assigned`, or `Creating`.
- Queue position is calculated among active pending tickets ordered by `CreatedAt`.
- `CancelAsync` marks ticket and target cancelled.
- No raw deployment payload is exposed.

- [ ] **Step 5: Register service**

Add:

```csharp
builder.Services.AddScoped<DeploymentQueueService>();
builder.Services.AddScoped<DeploymentExecutionService>();
builder.Services.AddSingleton<NodeExecutionGate>();
builder.Services.AddScoped<FleetCapacityReservationService>();
builder.Services.AddHostedService<PortLeaseRefreshService>();
```

- [ ] **Step 6: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~DeploymentQueueServiceTests --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: queue tests pass.

## Task 3: Atomic Capacity Reservation

**Files:**
- Create: `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs`
- Modify: `src/GZCTF/Services/Fleet/FleetManager.cs`
- Modify: `src/GZCTF/Services/Fleet/FleetContainerManager.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/FleetCapacityReservationServiceTests.cs`

- [ ] **Step 1: Write failing tests for concurrent batch reservations**

```csharp
[Fact]
public async Task ReserveBatchAsync_DoesNotOverbookNodeWhenRequestsArriveTogether()
{
    await using var context = TestDb.Create();
    var node = new WorkerNode
    {
        Id = Guid.NewGuid(),
        Name = "node-a",
        HostAddress = "10.24.0.30",
        Status = NodeStatus.Online,
        IsSchedulable = true,
        Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
        MaxContainers = 3,
        MaxVms = 1,
        TeamLabNetworkEnabled = true,
        TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy,
        TeamLabTunnelIp = "10.250.0.2",
        IsLocal = true
    };
    context.WorkerNodes.Add(node);
    await context.SaveChangesAsync();

    var service = CreateCapacityService(context);

    var first = await service.TryReserveAsync(new FleetCapacityRequest(NodeCapability.Docker, DockerSlots: 2, VmSlots: 0), CancellationToken.None);
    var second = await service.TryReserveAsync(new FleetCapacityRequest(NodeCapability.Docker, DockerSlots: 2, VmSlots: 0), CancellationToken.None);

    Assert.True(first.Success);
    Assert.False(second.Success);
    Assert.Contains("capacity", second.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task ReleaseAsync_RestoresReservedSlotsWithoutGoingNegative()
{
    await using var context = TestDb.Create();
    var node = SeedNode(context, currentContainers: 1, currentVms: 1);
    var service = CreateCapacityService(context);

    await service.ReleaseAsync(node.Id, dockerSlots: 2, vmSlots: 2, CancellationToken.None);

    var reloaded = await context.WorkerNodes.FindAsync(node.Id);
    Assert.Equal(0, reloaded!.CurrentContainers);
    Assert.Equal(0, reloaded.CurrentVms);
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~FleetCapacityReservationServiceTests --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: fails because the service does not exist.

- [ ] **Step 3: Implement reservation service**

Implement with `IDistributedLockService.AcquireAsync("fleet:scheduler")` around:

- fresh node load
- `WeightedScheduler.CanHost` / `CanHostTeamLab`
- batch slot check
- counter increment
- `SaveChangesAsync`

Use this request/result shape:

```csharp
public sealed record FleetCapacityRequest(NodeCapability Capability, int DockerSlots, int VmSlots, Guid? PreferredNodeId = null, bool RequireTeamLab = false);
public sealed record FleetCapacityReservation(bool Success, Guid? NodeId, WorkerNode? Node, int DockerSlots, int VmSlots, string Message);
```

Selection rule:

- Filter by capability and TeamLab readiness when requested.
- Reject nodes where `CurrentContainers + DockerSlots > MaxContainers` or `CurrentVms + VmSlots > MaxVms`.
- Score with existing weighted score plus a penalty for already-reserved slots.
- Persist the selected node counters before returning.

- [ ] **Step 4: Route preferred-node Docker through reservation service**

In `FleetContainerManager.CreateOnPreferredNodeAsync`, replace direct `WeightedScheduler.CanHost` and direct `FleetManager.ReserveCapacity` with `FleetCapacityReservationService.TryReserveAsync(... PreferredNodeId=nodeId ...)` unless `FleetCapacityReserved` is true.

- [ ] **Step 5: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~FleetCapacityReservationServiceTests|FullyQualifiedName~DeploymentTargetTests" --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 4: Real Queue Execution

**Files:**
- Create: `src/GZCTF/Services/Fleet/DeploymentExecutionService.cs`
- Modify: `src/GZCTF/Services/Fleet/QueueManager.cs`
- Modify: `src/GZCTF/Services/Fleet/QueueProcessingService.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/DeploymentQueueServiceTests.cs`

- [ ] **Step 1: Write failing test proving pending ticket is executed**

```csharp
[Fact]
public async Task ProcessPendingAsync_AssignsAndExecutesRunnableTicket()
{
    await using var context = TestDb.Create();
    var node = SeedDockerNode(context, maxContainers: 2);
    var ticket = SeedPendingGameContainerTicket(context, gameId: 1, teamId: 2, challengeId: 10);
    var executor = new FakeDeploymentExecutionService(success: true);
    var queue = CreateQueueManager(context, executor);

    var processed = await queue.ProcessPendingAsync(CancellationToken.None);

    Assert.Equal(1, processed);
    Assert.Equal(DeploymentQueueTicketStatus.Completed, ticket.Status);
    Assert.Equal(node.Id, ticket.TargetNodeId);
    Assert.Equal(1, executor.ExecutedTicketIds.Count);
}
```

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ProcessPendingAsync_AssignsAndExecutesRunnableTicket --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: fails because queue manager only changes target state and does not execute tickets.

- [ ] **Step 3: Implement execution service**

Execution service must load the linked business object and call the existing repository/service path:

- `GameContainer`: load `GameInstance` by `GameId + OwnerTeamId + ChallengeId` through `Participation`, `Team`, and `Game`, then call `GameInstanceRepository.CreateContainer`.
- `ExerciseContainer`: load `ExerciseInstance` by `OwnerUserId + ChallengeId`, then load `UserInfo` and call `ExerciseInstanceRepository.CreateContainer`.
- `Vm`: load `VmInstance`, challenge/template, then call `FleetVmService.CreateVmAsync` with a reserved node or queue bypass flag.
- `TeamLabRuntime`: call `TeamLabDeploymentService.DeployRuntimeFromTicketAsync`.

It must reject missing business objects as failed tickets with a clear message.

- [ ] **Step 4: Update `QueueManager.ProcessPendingAsync`**

Behavior:

- Select a bounded batch ordered by `CreatedAt`.
- Try atomic reservation for each ticket.
- Mark `Assigned`.
- Execute using `DeploymentExecutionService` through `NodeExecutionGate`.
- Leave ticket `Pending` if no node has capacity.
- Do not change raw `DeploymentTarget.Payload`.

- [ ] **Step 5: Update `QueueProcessingService` interval and batch strategy**

Use a shorter interval with jitter:

- initial delay: 2 seconds
- idle interval: 5 seconds
- busy interval: immediate next loop after a successful batch
- max tickets per batch: configurable, default 20

- [ ] **Step 6: Run queue tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeploymentQueueServiceTests|FullyQualifiedName~FleetCapacityReservationServiceTests" --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 5: Player-Facing Queue Responses

**Files:**
- Modify: `src/GZCTF/Controllers/GameController.cs`
- Modify: `src/GZCTF/Controllers/TrainingCourseController.cs`
- Modify: `src/GZCTF/ClientApp/src/Api.ts`
- Modify: relevant game/training container UI files
- Test: existing controller tests or new `src/GZCTF.Test/UnitTests/Fleet/DeploymentQueueControllerTests.cs`

- [ ] **Step 1: Write failing test for no-capacity Docker creation**

```csharp
[Fact]
public async Task CreateContainer_ReturnsAcceptedQueueStatusWhenNoNodeCapacity()
{
    var response = await Client.PostAsync("/api/game/1/container/10", null);
    var body = await response.Content.ReadFromJsonAsync<DeploymentQueueStatusModel>();

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    Assert.NotNull(body);
    Assert.True(body!.QueuePosition >= 1);
}
```

- [ ] **Step 2: Run test and verify it fails**

Expected: current behavior returns `400` with generic creation failed.

- [ ] **Step 3: Update API semantics**

Controllers should return:

- `200 OK` with container/VM info when already running or created immediately.
- `202 Accepted` with `DeploymentQueueStatusModel` when queued.
- `429 TooManyRequests` only for operation frequency limits.
- `400 BadRequest` only for invalid challenge config, not temporary capacity pressure.

- [ ] **Step 4: Update frontend**

Frontend behavior:

- Button enters disabled pending state after `202`.
- Display: `环境排队中，前方 {peopleAhead} 个任务`.
- Poll status endpoint every 3 seconds while pending/assigned/creating.
- Stop polling on completed/failed/cancelled.

- [ ] **Step 5: Run frontend type check/build**

Run:

```powershell
cd src/GZCTF/ClientApp
npm run build
```

Expected: build succeeds.

## Task 6: VM Queueing And Limits

**Files:**
- Modify: `src/GZCTF/Services/Fleet/FleetVmService.cs`
- Modify: `src/GZCTF/Controllers/GameController.cs`
- Modify: `src/GZCTF/Models/Internal/Configs.cs` if VM limit config is not already present
- Test: `src/GZCTF.Test/UnitTests/Fleet/FleetVmServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public async Task CreateVmAsync_QueuesWhenNoKvmCapacityExists()
{
    var result = await Service.CreateVmAsync(VmInstance, templateId: 1, templatePath: "/img.qcow2",
        memory: 2048, cpu: 2, flag: "flag{test}", CancellationToken.None);

    Assert.Null(result);
    Assert.Contains(Context.DeploymentQueueTickets, t => t.Kind == DeploymentQueueKind.Vm &&
        t.Status == DeploymentQueueTicketStatus.Pending);
}

[Fact]
public async Task CreateVmAsync_DoesNotExceedPerUserVmLimit()
{
    SeedRunningVmForUser(UserId);
    var response = await Controller.CreateContainer(GameId, WindowsChallengeId, CancellationToken.None);

    Assert.IsType<BadRequestObjectResult>(response);
}
```

- [ ] **Step 2: Run tests and verify failure**

Expected: VM service currently returns no queue ticket.

- [ ] **Step 3: Enable VM queueing**

Change VM creation path to create a queue ticket when no KVM node is available. Keep existing direct creation when capacity exists.

- [ ] **Step 4: Add VM owner limits**

Add configuration defaults:

- `MaxVmCountPerUser = 1`
- `MaxVmCountPerTeam = 0` meaning unlimited unless team mode needs enforcement

Apply per-user lock before creating/queueing VM.

- [ ] **Step 5: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~FleetVmService|FullyQualifiedName~CreateVmAsync" --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 7: Team/User Limit Race Protection

**Files:**
- Modify: `src/GZCTF/Repositories/GameInstanceRepository.cs`
- Modify: `src/GZCTF/Repositories/ExerciseInstanceRepository.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/OwnerLimitConcurrencyTests.cs`

- [ ] **Step 1: Write failing race test**

```csharp
[Fact]
public async Task GameContainerLimit_IsProtectedByTeamLock()
{
    var tasks = Enumerable.Range(0, 5)
        .Select(_ => Repository.CreateContainer(GameInstance, Team, User, GameWithLimitOne, CancellationToken.None));

    var results = await Task.WhenAll(tasks);

    Assert.Single(results.Where(r => r.Status == TaskStatus.Success));
    Assert.True(results.Count(r => r.Status == TaskStatus.Denied || r.Status == TaskStatus.Pending) >= 4);
}
```

- [ ] **Step 2: Run test and verify failure**

Expected: without a per-owner lock, multiple calls can pass the count check in parallel.

- [ ] **Step 3: Add owner locks**

Use `IDistributedLockService`:

- game containers: `container-limit:game:{game.Id}:team:{team.Id}`
- exercise containers: `container-limit:exercise:user:{user.Id}`
- VM containers: `vm-limit:game:{gameId}:user:{userId}`

Hold the lock only around count/check/create-or-queue transition, not around slow Agent creation.

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~OwnerLimitConcurrencyTests --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 8: TeamLab Batch Capacity Planning

**Files:**
- Create: `src/GZCTF/Services/TeamLab/TeamLabCapacityPlanner.cs`
- Modify: `src/GZCTF/Services/TeamLab/TeamLabPlanService.cs`
- Modify: `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabSchedulingTests.cs`

- [ ] **Step 1: Write failing tests for whole-environment capacity**

```csharp
[Fact]
public void CountRequiredSlots_CountsDockerAndVmAssets()
{
    var topology = PublishedTopologyFactory.Create(
        dockerAssets: 4,
        vmAssets: 2);

    var slots = TeamLabCapacityPlanner.CountRequiredSlots(topology);

    Assert.Equal(4, slots.DockerSlots);
    Assert.Equal(2, slots.VmSlots);
}

[Fact]
public async Task PlanRuntimeAsync_RejectsWhenOnlyPartialTeamLabCapacityExists()
{
    SeedTeamLabNode(maxContainers: 3, maxVms: 2);
    SeedPublishedTopology(dockerAssets: 4, vmAssets: 1);

    var result = await PlanService.PlanRuntimeAsync(gameId: 1, teamId: 2, CancellationToken.None);

    Assert.False(result.Success);
    Assert.Contains("capacity", result.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run tests and verify failure**

Expected: current TeamLab planning only checks one Docker and one KVM slot.

- [ ] **Step 3: Implement slot counting**

Count slots from published topology:

- Docker asset: `DockerSlots += 1`
- VM asset: `VmSlots += 1`
- Router/DHCP/DNS namespace helpers do not count as Docker slots unless implemented as actual containers.

- [ ] **Step 4: Use batch reservation during TeamLab plan/deploy**

Plan must call `FleetCapacityReservationService.TryReserveAsync` with:

```csharp
new FleetCapacityRequest(
    NodeCapability.Docker | NodeCapability.Kvm,
    DockerSlots: slots.DockerSlots,
    VmSlots: slots.VmSlots,
    PreferredNodeId: runtime.WorkerNodeId,
    RequireTeamLab: true)
```

If planning reserves capacity, deployment must reuse the same reservation without double incrementing. If deployment fails or is destroyed, release exactly the reserved slots.

- [ ] **Step 5: Run TeamLab tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabSchedulingTests|FullyQualifiedName~TeamLabDeploymentServiceTests|FullyQualifiedName~TeamLabPlanServiceTests" --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 9: TeamLab UDP Port Locking And Worker Port Uniqueness

**Files:**
- Modify: `src/GZCTF/Models/Data/TeamLabEntities.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Services/TeamLab/TeamLabPlanService.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabSchedulingTests.cs`

- [ ] **Step 1: Write failing concurrent port allocation test**

```csharp
[Fact]
public async Task PlanRuntimeAsync_AllocatesUniquePublicAndWorkerUdpPortsUnderConcurrency()
{
    SeedTeamLabNode(maxContainers: 20, maxVms: 10);
    SeedPublishedTopology(dockerAssets: 1, vmAssets: 0);

    var results = await Task.WhenAll(
        Enumerable.Range(1, 8).Select(teamId => PlanService.PlanRuntimeAsync(1, teamId, CancellationToken.None)));

    Assert.All(results, r => Assert.True(r.Success, r.Message));
    Assert.Equal(8, results.Select(r => r.Runtime!.PublicUdpMapping!.PublicUdpPort).Distinct().Count());
    Assert.Equal(8, results.Select(r => r.Runtime!.PublicUdpMapping!.WorkerWireGuardPort).Distinct().Count());
}
```

- [ ] **Step 2: Run test and verify failure**

Expected: current planner does not lock UDP scanning and does not enforce unique `WorkerWireGuardPort`.

- [ ] **Step 3: Add unique index**

Add unique index on `TeamLabPublicUdpMapping.WorkerWireGuardPort`.

- [ ] **Step 4: Lock planning critical section**

Wrap TeamLab planning node/port/capacity selection with:

```csharp
await lockService.AcquireAsync("teamlab:plan", TimeSpan.FromSeconds(30));
```

Inside the lock, re-read runtime, mappings, nodes, and capacity.

- [ ] **Step 5: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~PlanRuntimeAsync_AllocatesUniquePublicAndWorkerUdpPortsUnderConcurrency --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 10: Node Execution Gate And Parallel Deployment

**Files:**
- Create: `src/GZCTF/Services/Fleet/NodeExecutionGate.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentExecutionService.cs`
- Modify: `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/NodeExecutionGateTests.cs`

- [ ] **Step 1: Write failing tests for bounded concurrency**

```csharp
[Fact]
public async Task NodeExecutionGate_AllowsParallelismAcrossNodesButBoundsWithinNode()
{
    var gate = CreateGate(dockerLimitPerNode: 2, vmLimitPerNode: 1);
    var nodeA = Guid.NewGuid();
    var nodeB = Guid.NewGuid();
    var running = 0;
    var maxRunning = 0;

    async Task Work(Guid nodeId)
    {
        await using var lease = await gate.AcquireAsync(nodeId, NodeExecutionKind.Docker, CancellationToken.None);
        var now = Interlocked.Increment(ref running);
        maxRunning = Math.Max(maxRunning, now);
        await Task.Delay(50);
        Interlocked.Decrement(ref running);
    }

    await Task.WhenAll(Work(nodeA), Work(nodeA), Work(nodeA), Work(nodeB), Work(nodeB));

    Assert.True(maxRunning >= 3);
    Assert.True(maxRunning <= 4);
}
```

- [ ] **Step 2: Run test and verify failure**

Expected: gate does not exist.

- [ ] **Step 3: Implement gate**

Behavior:

- Docker default per-node concurrent create limit: 3.
- VM default per-node concurrent create limit: 1.
- TeamLab network setup default per-node concurrent deploy limit: 1.
- Fleet mode uses Redis counters with short TTL and token release.
- Standalone uses local semaphores.

- [ ] **Step 4: Use gate in execution**

Wrap slow Agent/local calls:

- Docker `CreateContainerAsync`
- VM `CreateVmAsync`
- TeamLab network setup and asset creation
- Destroy operations can use a separate higher limit, default 5, to drain quickly.

- [ ] **Step 5: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~NodeExecutionGateTests --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 11: TeamLab Parallel Asset Creation

**Files:**
- Modify: `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabDeploymentServiceTests.cs`

- [ ] **Step 1: Write failing dependency-order test**

```csharp
[Fact]
public async Task DeployNativeRuntimeAsync_CreatesIndependentAssetsInParallelAfterNetworkReady()
{
    var recorder = new DeploymentRecorder();
    SeedTopologyWithIndependentAssets(dockerAssets: 3, vmAssets: 0);

    await Service.DeployRuntimeAsync(gameId: 1, teamId: 2, CancellationToken.None);

    Assert.True(recorder.NetworkConfiguredBeforeAssets);
    Assert.True(recorder.MaxConcurrentAssetCreates > 1);
}
```

- [ ] **Step 2: Run test and verify failure**

Expected: current deployment creates assets sequentially.

- [ ] **Step 3: Refactor deployment stages**

Maintain this order:

1. Create bridges/router/WireGuard/DHCP/DNS.
2. Create Docker and VM assets in parallel with per-node gate.
3. Attach interfaces and run readiness checks per asset.
4. Record runtime assets.
5. Sync public UDP mapping.
6. Run final probes.
7. Mark running.

If any asset fails, cancel remaining asset tasks, cleanup created assets, mark `CleanupPending` or `Failed`.

- [ ] **Step 4: Run TeamLab deployment tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~TeamLabDeploymentServiceTests --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 12: TCP Proxy Port Lease Refresh

**Files:**
- Create: `src/GZCTF/Services/Fleet/PortLeaseRefreshService.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/PortLeaseRefreshServiceTests.cs`

- [ ] **Step 1: Write failing test for external gateway mode**

```csharp
[Fact]
public async Task RefreshActiveLeases_RefreshesRunningContainerProxyPorts()
{
    await using var context = TestDb.Create();
    SeedRunningProxyContainer(context, publicPort: 30042, publicEntry: "203.195.157.191");
    var allocator = new RecordingPortAllocator();
    var service = CreatePortLeaseRefreshService(context, allocator);

    await service.RefreshOnceAsync(CancellationToken.None);

    Assert.Contains(allocator.ReservedPorts, p => p.Port == 30042);
}
```

- [ ] **Step 2: Run test and verify failure**

Expected: no service refreshes Redis leases when Nginx local sync is disabled.

- [ ] **Step 3: Implement refresh service**

Behavior:

- Every 5 minutes, query `IContainerRepository.GetProxyPortMappingsAsync`.
- Call `IPortAllocationService.ReserveExistingPortAsync` for each active mapping.
- Log count and failures at debug/warning levels.
- Never allocate new ports; only refresh active facts.

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~PortLeaseRefreshServiceTests --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 13: Admin Queue And Node Visibility

**Files:**
- Modify: `src/GZCTF/Controllers/NodesController.cs`
- Modify: node management frontend files under `src/GZCTF/ClientApp/src/pages/admin`
- Test: `src/GZCTF.Test/UnitTests/Fleet/NodesControllerTests.cs`

- [ ] **Step 1: Write failing test for safe admin list**

```csharp
[Fact]
public async Task DeploymentTargetsList_DoesNotReturnRawPayload()
{
    var response = await AdminClient.GetFromJsonAsync<JsonElement>("/api/v1/deployment-targets");

    Assert.DoesNotContain("Payload", response.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("flag{", response.ToString(), StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run test and verify failure**

Expected: current `DeploymentTargetsController.List` returns `t.Payload`.

- [ ] **Step 3: Replace payload output with safe summary**

Admin list returns:

- target id/type/action/status/node/result/error
- linked ticket id/kind/owner/resource
- queue position
- slot count
- created/started/completed timestamps

It must not return `Payload`.

- [ ] **Step 4: Update frontend**

Node management should show:

- active Docker slots / max
- active VM slots / max
- queued Docker/VM/TeamLab tickets
- running TeamLab runtimes on node
- last failure message trimmed

- [ ] **Step 5: Run tests and build**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~NodesControllerTests --no-restore -p:UseSharedCompilation=false -m:1
cd src/GZCTF/ClientApp
npm run build
```

Expected: pass.

## Task 14: Cancellation And Destruction Semantics

**Files:**
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueService.cs`
- Modify: `src/GZCTF/Services/Fleet/FleetContainerManager.cs`
- Modify: `src/GZCTF/Services/Fleet/FleetVmService.cs`
- Modify: `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/DeploymentQueueServiceTests.cs`

- [ ] **Step 1: Write failing cancellation tests**

```csharp
[Fact]
public async Task CancelPendingTicket_DoesNotReleaseUnreservedCapacity()
{
    var ticket = SeedPendingTicket();
    await QueueService.CancelAsync(ticket.Id, "admin cancelled", CancellationToken.None);

    Assert.Equal(DeploymentQueueTicketStatus.Cancelled, ticket.Status);
    Assert.Equal(0, Node.CurrentContainers);
}

[Fact]
public async Task FailedCreatingTicket_ReleasesReservedCapacity()
{
    var ticket = SeedCreatingTicketWithReservedNode(dockerSlots: 1);
    await QueueService.FailAsync(ticket.Id, "agent failed", releaseCapacity: true, CancellationToken.None);

    Assert.Equal(0, Node.CurrentContainers);
    Assert.Equal(DeploymentQueueTicketStatus.Failed, ticket.Status);
}
```

- [ ] **Step 2: Run tests and verify failure**

Expected: cancellation/release is not centralized.

- [ ] **Step 3: Implement centralized finalization**

Rules:

- `Pending -> Cancelled`: no capacity release.
- `Assigned/Creating -> Failed/Cancelled`: release reserved slots once.
- `Completed -> Destroyed resource`: release at resource destroy path, not queue finalization.
- TeamLab destroy releases exactly topology slot counts.

- [ ] **Step 4: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeploymentQueueServiceTests|FullyQualifiedName~FleetVmService|FullyQualifiedName~TeamLabDeploymentServiceTests" --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: pass.

## Task 15: Integration And High-Concurrency Verification

**Files:**
- Modify: `docs/logging-coverage-progress.md` or create a new final verification doc
- Test: no new production files unless test fixtures need small helpers

- [ ] **Step 1: Run backend targeted tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Fleet|FullyQualifiedName~TeamLab|FullyQualifiedName~DeploymentQueue" --no-restore -p:UseSharedCompilation=false -m:1
```

Expected: all targeted tests pass.

- [ ] **Step 2: Run full backend build**

Run:

```powershell
dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false
```

Expected: build succeeds. Existing unrelated warnings can remain, but no new warnings from changed files.

- [ ] **Step 3: Run frontend build**

Run:

```powershell
cd src/GZCTF/ClientApp
npm run build
```

Expected: build succeeds.

- [ ] **Step 4: Run static hygiene checks**

Run:

```powershell
git diff --check
rg -n "DeploymentTarget.*Payload|Payload.*DeploymentTarget|flag\\{|PrivateKey|ProtectedClientPrivateKey|RegistryAuth|EnvironmentVariables" src/GZCTF/Controllers src/GZCTF/Services/Fleet src/GZCTF/Services/TeamLab
```

Expected:

- `git diff --check` has no output.
- Sensitive scan only reports legitimate model/business logic, not newly added log/API response output.

- [ ] **Step 5: Server-side smoke test plan**

On the deployed server:

1. Configure two schedulable Docker nodes with `MaxContainers=2`.
2. Create five teams.
3. Trigger five Docker challenge container creations nearly simultaneously.
4. Expected: four start immediately across both nodes, one returns queued status with `peopleAhead=0` or greater depending timing.
5. Destroy one running container.
6. Expected: queued ticket automatically starts within one queue cycle.
7. Repeat with one KVM node `MaxVms=1`.
8. Expected: second VM request queues instead of generic failure.
9. Deploy a TeamLab topology with 3 Docker and 1 VM asset on a node with exact capacity.
10. Expected: deployment succeeds and capacity counters reflect slots.
11. Attempt another TeamLab deployment on same node with no remaining capacity.
12. Expected: queued or rejected with explicit capacity message, not partial creation.

## Acceptance Criteria

- Multiple teams creating Docker containers simultaneously do not overbook a node.
- Multiple teams creating VMs simultaneously do not exceed KVM node limits.
- When capacity is available across multiple nodes, deployments are distributed and proceed concurrently.
- When capacity is exhausted, requests return queue status with queue position and clear text for the current user/team.
- Pending queue entries are automatically executed after capacity is released.
- TeamLab reserves capacity for the whole topology, not a single abstract slot.
- TeamLab UDP public ports and worker WireGuard ports are unique under concurrent planning.
- TeamLab asset creation is parallel where safe, with bounded per-node execution.
- Destroy/failure/cancel paths release capacity exactly once.
- Redis TCP proxy port leases are kept alive for active containers in external gateway mode.
- Admin APIs do not leak raw deployment payloads or secrets.
- Existing ordinary CTF, training container, VM, and TeamLab deployment flows still build and pass targeted tests.

## Progress Tracking

Update this section after each task:

## 2026-07-06 Continuing Execution Checklist

The remaining work must be treated as a complete scheduling repair, not a minimal bug fix. The next execution slice is:

- [ ] **A. Restore and lock the current green baseline**
  - Verify the TeamLab queue executor compile break is fixed.
  - Verify `Fleet|TeamLab|DeploymentQueue` tests pass after the VM payload contract test is aligned with the current payload shape.
  - Evidence target: `215/215` targeted tests passing.
- [ ] **B. Replace serial queue execution with bounded parallel execution**
  - Add `NodeExecutionGate` with per-node bounded concurrency and deterministic release on success/failure/cancellation.
  - Queue reservation may remain ordered/fair, but slow executor calls must run concurrently across nodes.
  - No queue item may execute without a prior capacity reservation.
- [ ] **C. Remove assignment-only legacy target queue semantics**
  - `QueueManager` must not mark old `DeploymentTarget` records as assigned without a business executor.
  - Durable `DeploymentQueueTicket` is the only creation queue path for Docker, VM, and TeamLab.
- [ ] **D. Centralize queue finalization**
  - `Pending -> Cancelled` must not release capacity.
  - `Creating -> Failed/Cancelled` must release reserved Docker/VM slots exactly once.
  - `Completed` capacity release remains owned by the actual destroy path.
- [ ] **E. Finish TeamLab queued execution**
  - Missing runtime and mismatched identity must fail safely without payload/secret leakage.
  - Existing queued runtime must execute through `DeployQueuedRuntimeAsync` using the already-reserved planned node.
  - TeamLab batch capacity must not be reserved twice.
- [ ] **F. Complete Redis/Nginx port lease refresh and admin visibility**
  - Active public proxy facts must keep Redis leases alive in external gateway mode.
  - Admin queue/target views must expose safe summaries only: no raw payload, flags, registry auth, WireGuard private key/config, or environment variables.
- [ ] **G. Final verification**
  - Run targeted queue/fleet/teamlab tests.
  - Run backend build.
  - Run static hygiene and secret-output scans.
  - Update this progress section with exact commands and results.

- Task 1: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~DeploymentQueueTicketTests --no-restore -p:UseSharedCompilation=false -m:1` -> failed because `DeploymentQueueTicket`, `DeploymentQueueRequest`, and `DeploymentQueueStatusModel` did not exist.
  - GREEN: same command -> passed 3 tests after adding queue ticket model, request/status models, and `AppDbContext.DeploymentQueueTickets`.
  - Notes: status model exposes only safe queue metadata and does not serialize raw `DeploymentTarget.Payload`.
- Task 2: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~DeploymentQueueServiceTests --no-restore -p:UseSharedCompilation=false -m:1` -> failed because `DeploymentQueueService` did not exist; test project also required restoring the newly added EF InMemory test provider.
  - GREEN: `dotnet restore src/GZCTF.Test/GZCTF.Test.csproj`, then same test command -> passed 2 service tests.
  - Notes: duplicate active tickets are reused by stable identity; queue position is one-based within same pending kind; no raw payload is exposed.
- Task 3: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~FleetCapacityReservationServiceTests --no-restore -p:UseSharedCompilation=false -m:1` -> failed because `FleetCapacityReservationService` did not exist.
  - GREEN: same command -> passed 2 capacity reservation tests; `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~FleetManager_TryScheduleWithTargetAsync_UsesAtomicCapacityReservation --no-restore -p:UseSharedCompilation=false -m:1` -> passed; `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~FleetManager_ReleasesReservedCapacity_WhenAssignedTargetPersistenceFails --no-restore -p:UseSharedCompilation=false -m:1` -> passed.
  - Notes: capacity reservation now happens under `fleet:scheduler`; batch slot requests cannot overbook node counters and releases are clamped at zero. `FleetManager.TryScheduleWithTargetAsync` now uses the capacity service before persisting an assigned deployment target and releases the reservation if assignment persistence fails.
- Task 3.1: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~FleetManager_QueuesDockerDeploymentWithDurableTicket_WhenCapacityIsExhausted --no-restore -p:UseSharedCompilation=false -m:1` -> failed because the queued ticket did not link back to `DeploymentTargetId`.
  - GREEN: same command -> passed after binding the active queue ticket to the target and keeping queue status free of flag/payload content. `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeploymentQueue|FullyQualifiedName~FleetCapacityReservation|FullyQualifiedName~NodesControllerTests" --no-restore -p:UseSharedCompilation=false -m:1` -> passed 35 tests.
  - Notes: corrected queue identity away from nonexistent `GameInstance.Id` / `ExerciseInstance.Id`. Game Docker queue identity is now `game-container:{gameId}:{teamId}:{challengeId}`; exercise Docker queue identity is `exercise-container:{userId}:{challengeId}`. Regenerated `AddDeploymentQueueTickets` migration so the queue table has no dead instance-id columns.
- Task 4: In progress
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ProcessPendingAsync_AssignsAndExecutesRunnableTicket --no-restore -p:UseSharedCompilation=false -m:1` -> failed because `QueueManager.ProcessPendingAsync` left `DeploymentQueueTicket` in `Pending`.
  - GREEN: same command -> passed after `QueueManager` started processing pending queue tickets: reserve capacity atomically, mark target/ticket creating, execute via `DeploymentExecutionService`, complete or fail, and release capacity on execution failure. `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeploymentQueue|FullyQualifiedName~FleetCapacityReservation|FullyQualifiedName~NodesControllerTests" --no-restore -p:UseSharedCompilation=false -m:1` -> passed 36 tests.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ExecuteAsync_FailsGameContainerTicket_WhenBusinessInstanceIsMissing --no-restore -p:UseSharedCompilation=false -m:1` -> failed because `DeploymentExecutionService` did not yet have a real constructor/business-key validation path.
  - GREEN: same command -> passed after `DeploymentExecutionService` started resolving Game/Exercise Docker tickets from business keys and failing safely when the business object is missing.
  - GREEN: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ProcessPendingAsync_ReleasesReservedCapacity_WhenExecutionFails --no-restore -p:UseSharedCompilation=false -m:1` -> passed, proving failed executor results release reserved node capacity and mark ticket/target failed.
  - GREEN: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeploymentQueue|FullyQualifiedName~FleetCapacityReservation|FullyQualifiedName~NodesControllerTests" --no-restore -p:UseSharedCompilation=false -m:1` -> passed 37 tests.
  - Notes: Docker Game/Exercise queue execution now reuses existing repository creation logic through `DeploymentExecutionContextAccessor`, injecting assigned node and `FleetCapacityReserved=true` instead of replaying raw payload or duplicating container creation code. Remaining Task 4/6/8 work: VM and TeamLab queue execution still need dedicated RED/GREEN coverage.
- Task 5: Completed
  - Notes: player-facing Docker/VM create paths now surface safe queue status instead of collapsing known capacity waits into generic failures. VM queued fallback is covered under Task 6.
- Task 6: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~CreateVmAsync_QueuesWhenNoKvmCapacityExists --no-restore -p:UseSharedCompilation=false -m:1` -> initially failed at test compile because the new VM queue test lacked `System.Net.Http` and `GZCTF.Utils` imports.
  - GREEN: same command -> passed 1 test after fixing test imports and confirming existing in-progress VM queue implementation creates a safe `DeploymentQueueKind.Vm` ticket, preserves `VmInstanceStatus.Creating`, and does not expose `flag{vm-secret}`.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ProcessPendingAsync_ExecutesVmTicket_WhenKvmCapacityBecomesAvailable --no-restore -p:UseSharedCompilation=false -m:1` -> first failed at compile because the VM execution constructor did not exist, then failed behaviorally with `DeploymentQueueTicketStatus.Failed` because queue execution re-entered scheduling and re-queued against already reserved capacity.
  - GREEN: same command -> passed 1 test after `DeploymentExecutionService` gained VM ticket execution through `FleetVmService`, and `FleetVmService` started honoring `DeploymentExecutionContextAccessor` assigned node/capacity reservation instead of double-scheduling.
  - GREEN: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~FleetVmServiceTests|FullyQualifiedName~DeploymentQueue|FullyQualifiedName~FleetCapacityReservation|FullyQualifiedName~NodesControllerTests" --no-restore -p:UseSharedCompilation=false -m:1` -> passed 39 tests.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~BuildVmCreateFallback_ReturnsAcceptedQueueStatus_WhenVmCreationWasQueued --no-restore -p:UseSharedCompilation=false -m:1` -> failed because `GameController.BuildVmCreateFallback` did not exist and the VM branch still returned generic KVM failure on queued creation.
  - GREEN: same command -> passed 1 test after VM create fallback started consuming scoped `DeploymentQueueStateAccessor` and returning `202 Accepted` with safe queue status when queued.
  - Notes: VM queue tickets now create real VM instances from business records (`VmInstance`, `GameChallenge`, `ImageTemplate`) and do not trust raw `DeploymentTarget.Payload`; player-facing VM creation now returns queued status rather than generic capacity failure.
- Task 7: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~ContainerOwnerLimitLockTests" --no-restore -p:UseSharedCompilation=false -m:1` -> failed 4 tests because `GameInstanceRepository` / `ExerciseInstanceRepository` did not acquire owner-level limit locks and did not count active queue tickets as container-limit usage.
  - GREEN: same command -> passed 4 tests after adding per-team/per-user distributed locks around container create/queue transitions and counting active `Pending` / `Assigned` / `Creating` Docker queue tickets against game-team and exercise-user limits.
  - Notes: lock granularity is owner-scoped (`container-limit:game:{gameId}:team:{teamId}` and `container-limit:exercise:user:{userId}`), so different teams/users still deploy concurrently. Active queue tickets for the same challenge are excluded to avoid duplicate ticket reuse blocking its own queued retry.
- Task 8: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~CountAssetSlots_CountsOnlyDockerAndVmRuntimeAssets --no-restore -p:UseSharedCompilation=false -m:1` -> failed because `TeamLabDeploymentService.CountAssetSlots` did not exist.
  - GREEN: same command -> passed after adding `TeamLabAssetSlotCount` and counting Docker/VM topology assets only.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TryReserveTeamLabCapacityAsync" --no-restore -p:UseSharedCompilation=false -m:1` -> failed because TeamLab deployment had no batch capacity reservation method and service constructor did not accept `FleetCapacityReservationService`.
  - GREEN: same command -> passed 2 tests after deployment started reserving the whole topology on the planned TeamLab node via `FleetCapacityReservationService` with `PreferredNodeId` and `RequireTeamLab`.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ReleaseTeamLabCapacityAsync_ReleasesTopologySlotsExactlyOnce --no-restore -p:UseSharedCompilation=false -m:1` -> failed because TeamLab had no centralized slot release helper.
  - GREEN: same command -> passed after adding `ReleaseTeamLabCapacityAsync` and `CountRuntimeAssetSlots`, and wiring release into deployment failure cleanup and successful destroy before runtime facts are marked destroyed.
  - GREEN: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabDeploymentServiceTests|FullyQualifiedName~TeamLabAssetPlanServiceTests|FullyQualifiedName~TeamLabAdminControllerTests" --no-restore -p:UseSharedCompilation=false -m:1` -> passed 59 tests.
  - Notes: TeamLab now treats topology deployment as an all-or-nothing capacity request for Docker/VM slots on the planned worker node. Capacity exhaustion creates a durable TeamLab queue ticket and queued execution uses `DeployQueuedRuntimeAsync` without reserving batch capacity twice.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ExecuteAsync_FailsTeamLabTicket_WhenRuntimeIsMissing --no-restore -p:UseSharedCompilation=false -m:1` -> failed at compile with `CS0103 capacityAlreadyReserved` because `DeployNativeRuntimeAsync` used the reservation flag without receiving it.
  - GREEN: same command -> passed 1 test after passing `capacityAlreadyReserved` from `DeployRuntimeAsync` into `DeployNativeRuntimeAsync`.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ReleaseTeamLabCapacityAsync_ReleasesPlannedSlotsWhenRuntimeAssetsAreNotRecordedYet --no-restore -p:UseSharedCompilation=false -m:1` -> failed at compile because `ReleaseTeamLabCapacityAsync` did not have a planned-slot overload.
  - GREEN: same command -> passed 1 test after adding planned-slot release and routing early post-reservation native deployment failures through that release path.
  - GREEN: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Fleet|FullyQualifiedName~TeamLab|FullyQualifiedName~DeploymentQueue" --no-restore -p:UseSharedCompilation=false -m:1` -> passed 215 tests after aligning `VmCreatePayload_IncludesFlagForRemoteScheduling` with the current VM queue payload shape while preserving the flag contract.
- Task 9: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~NodeExecutionGateTests --no-restore -p:UseSharedCompilation=false -m:1` -> failed at compile because `NodeExecutionGate` and `NodeExecutionGateOptions` did not exist.
  - GREEN: same command -> passed 2 tests after adding `NodeExecutionGate` with per-node semaphore limits and DI registration.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ProcessPendingAsync_ExecutesTicketsOnDifferentNodesConcurrently --no-restore -p:UseSharedCompilation=false -m:1` -> timed out because `QueueManager.ProcessPendingAsync` awaited each executor serially.
  - GREEN: same command -> passed 1 test after changing `QueueManager` to ordered reservation plus bounded parallel execution through `NodeExecutionGate`; `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeploymentQueue|FullyQualifiedName~FleetCapacityReservation|FullyQualifiedName~FleetVmServiceTests|FullyQualifiedName~GameControllerQueueResponseTests" --no-restore -p:UseSharedCompilation=false -m:1` -> passed 17 tests.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ProcessPendingAsync_ReleasesReservedCapacity_WhenTicketDisappearsBeforeExecution --no-restore -p:UseSharedCompilation=false -m:1` -> failed with node `CurrentContainers=1` because a ticket removed during executor failure skipped capacity release.
  - GREEN: same command -> passed after `QueueManager` stored reserved Docker/VM slots in `ReservedQueueTicket` and used that snapshot to release capacity when a ticket disappears before or during execution.
  - Notes: removed the old assignment-only `DeploymentTarget` fallback from `QueueManager`; durable `DeploymentQueueTicket` is now the only active creation queue execution path.
- Task 10: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~CancelAsync_DoesNotReleaseCapacityForPendingTicket|FullyQualifiedName~CancelAsync_ReleasesReservedCapacityForCreatingTicketExactlyOnce" --no-restore -p:UseSharedCompilation=false -m:1` -> failed at compile because `DeploymentQueueService` did not accept capacity reservation dependency.
  - GREEN: same command -> passed 2 tests after `DeploymentQueueService.CancelAsync` started releasing assigned/creating reserved capacity exactly once while leaving pending tickets untouched.
  - GREEN: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~DeploymentQueue --no-restore -p:UseSharedCompilation=false -m:1` -> passed 11 tests.
- Task 11: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeployQueuedRuntimeAsync_CreatesIndependentDockerAssetsConcurrently" --no-restore -p:UseSharedCompilation=false -m:1` -> failed because two same-priority Docker assets did not start concurrently; after fixing the test snapshot serialization to match platform Web JSON, the failure was confirmed as the target behavior gap.
  - GREEN: same command -> passed 1 test after TeamLab native asset creation was changed to run same-`StartPriority` assets concurrently while preserving ordered priority groups and keeping EF writes on the main deployment flow.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeployQueuedRuntimeAsync_CleansAlreadyCreatedParallelAssetsWhenOneAssetFails" --no-restore -p:UseSharedCompilation=false -m:1` -> failed because one failed parallel asset could hide another successfully-created container from cleanup.
  - GREEN: same command -> passed 1 test after created container/VM ids started being tracked immediately with a thread-safe cleanup list before attach/probe completion.
  - Notes: network/bridge/router/WireGuard/DHCP/DNS setup remains sequential; only slow independent Docker/VM asset create/attach/readiness work is parallelized. Parallel workers return asset records and created resource ids; `DbContext` mutation and `SaveChanges` stay serialized.
- Task 12: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~PortLeaseRefreshServiceTests --no-restore -p:UseSharedCompilation=false -m:1` -> failed because `PortLeaseRefreshService` did not exist.
  - GREEN: same command -> passed 1 test after adding `PortLeaseRefreshService` to refresh active public proxy port reservations from database mappings when Nginx uses an external gateway (`SyncLocalConfig=false`).
  - Notes: the service refreshes existing active mappings only; it does not allocate new ports or write Nginx config.
- Task 13: Partially completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~DeploymentTargetsController_List_DoesNotExposeRawPayloadOrSecrets --no-restore -p:UseSharedCompilation=false -m:1` -> failed because `DeploymentTargetsController.List` serialized raw `Payload` containing `flag{secret}`.
  - GREEN: same command -> passed after removing raw payload from `List` and `GetById` responses.
  - Notes: admin target list/detail now return operational metadata only; queue ticket summary fields still need to be added when Task 4/13 is completed.
- Task 14: Completed
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeploymentTargetsController_Cancel_CancelsLinkedQueueTicketAndReleasesReservedCapacity" --no-restore -p:UseSharedCompilation=false -m:1` -> failed first at compile because `DeploymentTargetsController` could not receive `DeploymentQueueService`; after wiring the dependency, failed in unit context because cancellation assumed `HttpContext` was always present.
  - GREEN: same command -> passed 1 test after DeploymentTarget admin cancellation was routed through the linked active `DeploymentQueueTicket` cancellation path and `HttpContext` cancellation token handling was made unit-test safe.
  - RED: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~NodesController_Deregister_CancelsActiveQueueTicketsForRemovedNode" --no-restore -p:UseSharedCompilation=false -m:1` -> failed because node deregistration had no active queue-ticket cleanup coverage and the previous bulk-update path was not testable with EF InMemory.
  - GREEN: same command -> passed 1 test after node deregistration started cancelling active tickets for the removed node, clearing node references, and using entity updates for this low-frequency consistency path.
  - Notes: cancellation release semantics are covered by Task 10 plus controller linkage above; Docker/VM/TeamLab destroy paths release capacity only after successful destroy and clamp counters at zero. Removed the old no-op `QueueManager.EnqueueAsync(DeploymentTarget)` compatibility path so durable queue tickets are the only active creation queue.
- Task 15: Completed
  - GREEN: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Fleet|FullyQualifiedName~TeamLab|FullyQualifiedName~DeploymentQueue" --no-restore -p:UseSharedCompilation=false -m:1` -> passed 232 tests.
  - GREEN: `dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false` -> build succeeded with 0 warnings and 0 errors.
  - GREEN: `git diff --check` -> exit 0; no whitespace errors. Git reported existing LF-to-CRLF working-copy warnings only.
  - REVIEW: `rg -n "EnqueueAsync\(DeploymentTarget|_queue\.EnqueueAsync\(|DeploymentTarget.*Payload|Payload.*DeploymentTarget|flag\\{|PrivateKey|ProtectedClientPrivateKey|RegistryAuth|EnvironmentVariables" src/GZCTF/Controllers src/GZCTF/Services/Fleet src/GZCTF/Services/TeamLab` -> old no-op DeploymentTarget queue hits are gone. Remaining hits are internal execution/configuration paths (Agent request construction, WireGuard key material generation, template registry auth storage, TeamLab env/flag injection), not newly exposed queue/admin API or log output.

## Self-Review

- Spec coverage: The plan covers Docker creation/destruction, VM creation/destruction, TeamLab creation/destruction, multi-node scheduling, multi-team high concurrency, queue position feedback, owner limits, Redis/Nginx proxy coordination, admin visibility, and TDD verification.
- Placeholder scan: This plan intentionally avoids undefined "TODO later" work. Each task has concrete files, expected tests, and verification commands.
- Risk control: The highest-risk work is split at service boundaries. The queue ticket model prevents creating resources from raw payload alone, and the capacity service centralizes node counter mutation.
