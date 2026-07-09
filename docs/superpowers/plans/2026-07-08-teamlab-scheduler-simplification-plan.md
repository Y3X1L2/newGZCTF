# TeamLab Scheduler Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep TeamLab and normal Docker/VM scheduling safe under concurrency while removing magic protocol checks and avoiding an over-complex scheduler.

**Architecture:** Preserve the current queue plus capacity-reservation architecture. Improve only the parts that are necessary: centralized protocol requirements, explicit capability checks, current-plus-reserved capacity accounting, a small absolute-headroom tie-breaker, and node-level execution throttling for TeamLab asset creation. Do not introduce predictive scheduling, learning-based failure scoring, image locality, traffic-aware placement, or a full bin-packing solver in this round.

**Tech Stack:** ASP.NET Core, EF Core, Redis/local distributed lock, GZCTF Agent, xUnit, TypeScript admin UI.

---

## Current Assessment

The current scheduler is basically reasonable:

- Normal Docker/VM scheduling uses a queue, a global scheduler lock, and `Current + Reserved` capacity accounting.
- TeamLab shard placement uses network groups as the placement unit and keeps multi-interface/router assets with their connected networks.
- Docker-only TeamLab nodes and VM-capable TeamLab nodes are already intended to be separated by capability.

The current weaknesses are also clear:

- `TeamLabProtocolVersion < 3` is a magic-number gate. The value is correct for the current implementation, but the policy is not centralized.
- The scoring formula mostly uses load and percentage capacity. It can under-value a large node and a small node when both have the same utilization ratio.
- TeamLab runtime deployment bypasses the normal `NodeExecutionGate`, so same-priority assets inside one runtime can start too aggressively on the same node.
- The reservation model is necessary, but it needs tests and observability that prove stale reservations and queued capacity pressure are handled.

## Scope Decisions

### Necessary

- Centralize TeamLab protocol version requirements.
- Keep `Current + Reserved` capacity accounting.
- Keep queue-based pending deployment instead of direct failure when capacity is temporarily unavailable.
- Add a minimal absolute-headroom factor or tie-breaker to distinguish large and small nodes.
- Add/verify TeamLab Docker-only and VM-required capability tests.
- Put TeamLab asset creation under a node-level execution gate or equivalent per-node semaphore.

### Defer

- Image-locality scoring.
- Historical node failure-rate scoring.
- Traffic-aware scheduling.
- Predictive CPU/memory reservation.
- Cross-node single-network splitting.
- Dynamic autoscaling.

### Do Not Add

- Full bin-packing solver.
- Machine-learning or adaptive scheduler.
- Port-level ACL scheduling.
- Separate scheduler implementations for normal Docker/VM and TeamLab that duplicate the same capacity logic.

---

## File Structure

- Create: `src/GZCTF/Services/TeamLab/TeamLabProtocolRequirements.cs`
  - Owns protocol version constants and feature minimums.
- Modify: `src/GZCTF/Services/Fleet/WeightedScheduler.cs`
  - Replaces magic protocol version checks and exposes simple capability reasons.
- Modify: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
  - Reports the centralized current Agent TeamLab protocol version.
- Modify: `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs`
  - Adds minimal absolute-headroom scoring for normal Docker/VM reservations.
- Modify: `src/GZCTF/Services/TeamLab/TeamLabShardPlanner.cs`
  - Keeps current network-group placement, adds only simple headroom-aware ordering if needed.
- Modify: `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs`
  - Applies per-node execution throttling to TeamLab asset creation.
- Modify: `src/GZCTF.Test/UnitTests/Fleet/WeightedSchedulerTests.cs`
  - Covers protocol requirement and headroom scoring.
- Modify: `src/GZCTF.Test/UnitTests/Fleet/FleetCapacityReservationServiceTests.cs`
  - Covers reservation pressure and no overbooking.
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlanServiceTests.cs`
  - Covers Docker-only vs VM-required TeamLab capability behavior.
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabDeploymentServiceTests.cs`
  - Covers TeamLab per-node execution gate behavior if the file exists; otherwise create it.
- Modify: `docs/teamlab-multinode-fabric-progress.md`
  - Records that these changes are future scheduler hardening, not current production behavior until implemented.

---

### Task 1: Centralize TeamLab Protocol Requirements

**Files:**
- Create: `src/GZCTF/Services/TeamLab/TeamLabProtocolRequirements.cs`
- Modify: `src/GZCTF/Services/Fleet/WeightedScheduler.cs`
- Modify: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/WeightedSchedulerTests.cs`

- [ ] **Step 1: Add failing tests for protocol policy**

Add tests that prove the scheduler no longer depends on an unexplained inline `3`.

```csharp
[Fact]
public void GetTeamLabFabricUnschedulableReason_RejectsNodeBelowMinimumFabricProtocol()
{
    var node = CreateHealthyTeamLabNode();
    node.TeamLabProtocolVersion = TeamLabProtocolRequirements.MinFabricProtocolVersion - 1;

    var reason = WeightedScheduler.GetTeamLabFabricUnschedulableReason(node);

    Assert.Contains($"protocol v{TeamLabProtocolRequirements.MinFabricProtocolVersion}", reason);
}

[Fact]
public void GetTeamLabFabricUnschedulableReason_AllowsNodeAtMinimumFabricProtocol()
{
    var node = CreateHealthyTeamLabNode();
    node.TeamLabProtocolVersion = TeamLabProtocolRequirements.MinFabricProtocolVersion;

    var reason = WeightedScheduler.GetTeamLabFabricUnschedulableReason(node);

    Assert.Null(reason);
}
```

- [ ] **Step 2: Run protocol tests and verify they fail**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~WeightedSchedulerTests" --no-restore
```

Expected: tests fail because `TeamLabProtocolRequirements` does not exist yet.

- [ ] **Step 3: Create protocol requirements class**

Create `src/GZCTF/Services/TeamLab/TeamLabProtocolRequirements.cs`:

```csharp
namespace GZCTF.Services.TeamLab;

public static class TeamLabProtocolRequirements
{
    public const int CurrentAgentProtocolVersion = 3;
    public const int MinFabricProtocolVersion = 3;
    public const int MinTrafficCaptureProtocolVersion = 3;
    public const int MinLinuxVmCloudInitProtocolVersion = 3;

    public static string FormatRequirement(string feature, int minimumVersion) =>
        $"{feature} requires TeamLab Agent protocol v{minimumVersion} or newer.";
}
```

- [ ] **Step 4: Replace magic protocol checks**

In `src/GZCTF/Services/Fleet/WeightedScheduler.cs`, import `GZCTF.Services.TeamLab` and replace:

```csharp
if (node.TeamLabProtocolVersion < 3)
    return "TeamLab Agent protocol is incompatible; TeamLab Fabric namespace uplink requires protocol v3";
```

with:

```csharp
if (node.TeamLabProtocolVersion < TeamLabProtocolRequirements.MinFabricProtocolVersion)
    return TeamLabProtocolRequirements.FormatRequirement(
        "TeamLab Fabric namespace uplink",
        TeamLabProtocolRequirements.MinFabricProtocolVersion);
```

- [ ] **Step 5: Use the centralized current Agent protocol**

In `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`, replace:

```csharp
ProtocolVersion: 3,
```

with:

```csharp
ProtocolVersion: TeamLabProtocolRequirements.CurrentAgentProtocolVersion,
```

Add the required namespace import if the Agent project can reference the shared service namespace. If it cannot, create a small Agent-local `AgentTeamLabProtocol` class with the same constant and add a test that both constants are intentionally equal by contract.

- [ ] **Step 6: Run tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~WeightedSchedulerTests" --no-restore
```

Expected: PASS.

---

### Task 2: Keep Reservation, Strengthen Queue and Overbooking Guarantees

**Files:**
- Modify: `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs`
- Modify: `src/GZCTF/Services/Fleet/QueueManager.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/FleetCapacityReservationServiceTests.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/DeploymentQueueServiceTests.cs`

- [ ] **Step 1: Add tests proving reservation is required**

Add or strengthen tests with this behavior:

```csharp
[Fact]
public async Task TryReserveAsync_UsesCurrentPlusReservedCapacity()
{
    await using var context = CreateContext();
    var node = SeedNode(context, maxContainers: 2, currentContainers: 1, reservedContainers: 1);
    var service = CreateService(context);

    var result = await service.TryReserveAsync(
        new FleetCapacityRequest(NodeCapability.Docker, DockerSlots: 1, VmSlots: 0),
        CancellationToken.None);

    Assert.False(result.Success);
    Assert.Contains("capacity", result.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Add tests proving queued tickets stay pending when capacity is unavailable**

```csharp
[Fact]
public async Task ProcessPendingAsync_LeavesTicketPending_WhenCapacityIsTemporarilyUnavailable()
{
    await using var context = CreateContext();
    SeedDockerNode(context, maxContainers: 1, currentContainers: 1);
    var ticket = SeedDockerQueueTicket(context);
    var queue = CreateQueueManager(context, new RecordingDeploymentExecutionService());

    var processed = await queue.ProcessPendingAsync(CancellationToken.None);

    Assert.Equal(0, processed);
    Assert.Equal(DeploymentQueueTicketStatus.Pending, ticket.Status);
}
```

- [ ] **Step 3: Run reservation tests and verify current behavior**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~FleetCapacityReservationServiceTests|FullyQualifiedName~DeploymentQueueManagerTests" --no-restore
```

Expected: PASS after test helpers are aligned with existing fixtures. If a test fails because production code does not use `Reserved` in a path, fix that path instead of weakening the test.

- [ ] **Step 4: Keep current reservation model**

Do not remove `ReservedContainers` or `ReservedVms`. The target invariant is:

```csharp
node.AllocatedContainers + dockerSlots <= node.MaxContainers
node.AllocatedVms + vmSlots <= node.MaxVms
```

where `Allocated = Current + Reserved`.

---

### Task 3: Add Minimal Absolute-Headroom Scoring

**Files:**
- Modify: `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs`
- Modify: `src/GZCTF/Services/Fleet/WeightedScheduler.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/WeightedSchedulerTests.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/FleetCapacityReservationServiceTests.cs`

- [ ] **Step 1: Add failing test for large-node preference**

This test captures the only necessary scoring improvement: distinguish a large node from a small node when utilization ratio is similar.

```csharp
[Fact]
public void SelectOptimalNode_PrefersLargerAbsoluteDockerHeadroom_WhenLoadAndRatioAreSimilar()
{
    var small = new WorkerNode
    {
        Id = Guid.NewGuid(),
        Name = "small",
        Status = NodeStatus.Online,
        IsLocal = true,
        IsSchedulable = true,
        Capabilities = NodeCapability.Docker,
        CpuLoad = 0.2f,
        MemoryLoad = 0.2f,
        CurrentContainers = 5,
        MaxContainers = 10
    };
    var large = new WorkerNode
    {
        Id = Guid.NewGuid(),
        Name = "large",
        Status = NodeStatus.Online,
        IsLocal = true,
        IsSchedulable = true,
        Capabilities = NodeCapability.Docker,
        CpuLoad = 0.2f,
        MemoryLoad = 0.2f,
        CurrentContainers = 50,
        MaxContainers = 100
    };

    var selected = WeightedScheduler.SelectOptimalNode([small, large], NodeCapability.Docker);

    Assert.Equal(large.Id, selected?.Id);
}
```

- [ ] **Step 2: Implement a bounded headroom term**

Keep the existing score shape. Add only a small bounded term so headroom breaks ties without overpowering CPU/memory.

```csharp
private static float CalculateScore(WorkerNode n, NodeCapability required)
{
    var baseScore =
        1000f * (1 - Math.Clamp(n.CpuLoad, 0f, 1f)) +
        500f * (1 - Math.Clamp(n.MemoryLoad, 0f, 1f)) +
        200f * (1 - (float)n.AllocatedContainers / Math.Max(n.MaxContainers, 1)) +
        200f * (1 - (float)n.AllocatedVms / Math.Max(n.MaxVms, 1));

    var dockerHeadroom = Math.Max(0, n.MaxContainers - n.AllocatedContainers);
    var vmHeadroom = Math.Max(0, n.MaxVms - n.AllocatedVms);
    var headroomScore = required switch
    {
        NodeCapability.Docker => Math.Min(dockerHeadroom, 50) * 2f,
        NodeCapability.Kvm => Math.Min(vmHeadroom, 20) * 5f,
        NodeCapability.Docker | NodeCapability.Kvm =>
            Math.Min(dockerHeadroom, 50) * 1f + Math.Min(vmHeadroom, 20) * 3f,
        _ => 0f
    };

    return baseScore + headroomScore;
}
```

Use the same idea in `FleetCapacityReservationService.NodeScore`, passing the requested capability. Do not add image locality, failure history, or predictive CPU.

- [ ] **Step 3: Run scheduler tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~WeightedSchedulerTests|FullyQualifiedName~FleetCapacityReservationServiceTests" --no-restore
```

Expected: PASS.

---

### Task 4: Preserve Simple TeamLab Shard Placement

**Files:**
- Modify: `src/GZCTF/Services/TeamLab/TeamLabShardPlanner.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlanServiceTests.cs`

- [ ] **Step 1: Add tests for capability split**

Add tests that prove Docker-only TeamLab does not require KVM, but VM TeamLab does.

```csharp
[Fact]
public async Task PlanRuntimeAsync_AllowsDockerOnlyTopologyOnDockerFabricNodeWithoutKvm()
{
    await using var context = CreateContext();
    SeedDockerFabricNode(context, hasKvm: false, maxContainers: 5);
    SeedDockerOnlyTeamLabTopology(context);
    var service = CreateService(context);

    var result = await service.PlanRuntimeAsync(gameId: 1, teamId: 1, CancellationToken.None);

    Assert.True(result.Success, result.Message);
    Assert.All(result.Shards, shard => Assert.Equal(0, shard.VmSlots));
}

[Fact]
public async Task PlanRuntimeAsync_RejectsVmTopologyWhenNoKvmFabricNodeExists()
{
    await using var context = CreateContext();
    SeedDockerFabricNode(context, hasKvm: false, maxContainers: 5);
    SeedVmTeamLabTopology(context);
    var service = CreateService(context);

    var result = await service.PlanRuntimeAsync(gameId: 1, teamId: 1, CancellationToken.None);

    Assert.False(result.Success);
    Assert.Contains("Kvm", result.Message, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Keep the current placement model**

Do not replace `TeamLabShardPlanner` with a general solver. Keep these invariants:

```csharp
// One network group is the minimum placement unit.
// Multi-interface assets union their connected networks.
// A single oversized group fails with a clear message instead of being split.
```

- [ ] **Step 3: Add only deterministic ordering improvements**

If scoring changes are needed inside `TeamLabShardPlanner.ScoreNode`, mirror the bounded headroom idea from Task 3 and keep deterministic final ordering:

```csharp
.OrderByDescending(item => item.Existing is not null)
.ThenByDescending(item => item.Score)
.ThenBy(item => NodeSortKey(item.Node))
```

- [ ] **Step 4: Run TeamLab planning tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabPlanServiceTests" --no-restore
```

Expected: PASS.

---

### Task 5: Apply Node-Level Execution Throttling to TeamLab Asset Creation

**Files:**
- Modify: `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs`
- Modify: `src/GZCTF/Services/Fleet/NodeExecutionGate.cs` only if a small helper is needed
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabDeploymentServiceTests.cs`

- [ ] **Step 1: Add test for TeamLab same-node throttling**

Create a test with a fake asset creator that blocks until two operations attempt to enter the same node. The expected result is that only the configured limit enters concurrently.

```csharp
[Fact]
public async Task DeployRuntimeAsync_ThrottlesTeamLabAssetCreationPerWorkerNode()
{
    var gate = new NodeExecutionGate(
        new NodeExecutionGateOptions { MaxConcurrentOperationsPerNode = 1 },
        NullLogger<NodeExecutionGate>.Instance);
    var tracker = new ConcurrencyTracker();

    await RunTwoSamePriorityTeamLabAssetsOnSameNodeAsync(gate, tracker);

    Assert.Equal(1, tracker.MaxConcurrent);
}
```

- [ ] **Step 2: Wrap TeamLab asset creation with the same gate**

In the asset creation loop, preserve same-priority parallelism across different nodes while throttling per node:

```csharp
var assetResults = await Task.WhenAll(assetGroup
    .OrderBy(asset => asset.TopologyKey, StringComparer.Ordinal)
    .Select(asset =>
    {
        var shard = ResolveAssetShard(asset, shards);
        return _nodeExecutionGate.RunAsync(shard.WorkerNodeId,
            executionToken => CreateNativeAssetAsync(runtime, asset, shard, executionToken),
            token);
    }));
```

If `RunAsync` currently returns `Task` only, add a generic overload:

```csharp
public async Task<T> RunAsync<T>(Guid nodeId, Func<CancellationToken, Task<T>> operation, CancellationToken token)
{
    var gate = _gates.GetOrAdd(nodeId, _ => new SemaphoreSlim(_limit, _limit));
    await gate.WaitAsync(token);
    try
    {
        return await operation(token);
    }
    finally
    {
        gate.Release();
        _logger.LogDebug("Released node execution gate for node {NodeId}.", nodeId);
    }
}
```

- [ ] **Step 3: Run TeamLab deployment tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabDeploymentServiceTests|FullyQualifiedName~NodeExecutionGateTests" --no-restore
```

Expected: PASS.

---

### Task 6: Document and Expose Scheduling Reasons

**Files:**
- Modify: `docs/teamlab-multinode-fabric-progress.md`
- Modify: `src/GZCTF/Models/Request/Admin/NodeModels.cs`
- Modify: `src/GZCTF/Controllers/NodesController.cs`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/nodes/Index.tsx`

- [ ] **Step 1: Make node reasons explicit**

Ensure node management can distinguish:

```text
普通 Docker 可调度
普通 VM 可调度
TeamLab Docker 可调度
TeamLab VM 可调度
Fabric 健康
协议版本不兼容
依赖缺失
容量不足
调度开关关闭
```

- [ ] **Step 2: Keep UI simple**

Show reasons as compact status text or tooltip. Do not add a new complex scheduler dashboard in this round.

- [ ] **Step 3: Update progress documentation**

Append a section to `docs/teamlab-multinode-fabric-progress.md`:

```markdown
## Scheduler Hardening Plan

- Protocol checks are centralized by feature minimum version.
- Capacity reservation remains based on Current + Reserved.
- Scoring adds only bounded absolute headroom; no predictive scheduler is introduced.
- TeamLab asset creation uses the same per-node execution gate as normal queue tasks.
- Docker-only TeamLab and VM TeamLab capability checks remain separate.
```

- [ ] **Step 4: Run frontend typecheck**

Run:

```powershell
pnpm exec tsc -p tsconfig.app.json --noEmit
```

Expected: PASS.

---

## Verification Commands

Run these after implementation:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~WeightedSchedulerTests|FullyQualifiedName~FleetCapacityReservationServiceTests|FullyQualifiedName~DeploymentQueueManagerTests|FullyQualifiedName~TeamLabPlanServiceTests|FullyQualifiedName~TeamLabDeploymentServiceTests|FullyQualifiedName~NodeExecutionGateTests" --no-restore
dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore -p:UseSharedCompilation=false
pnpm exec tsc -p tsconfig.app.json --noEmit
```

Expected: all commands pass with zero new warnings.

## Final Acceptance Criteria

- No inline magic protocol version remains in scheduler or Agent TeamLab status reporting.
- Docker-only TeamLab nodes are not rejected for missing KVM.
- VM TeamLab still requires KVM.
- Concurrent reservations cannot overbook a node.
- Capacity shortage leaves deployable work queued or clearly reports resource insufficiency.
- Large nodes with materially more free slots are preferred when load and utilization ratio are otherwise similar.
- TeamLab asset creation no longer starts unlimited same-node operations in one runtime.
- Scheduler remains deterministic and understandable.
- No complex predictive or learning-based scheduling logic is introduced.

