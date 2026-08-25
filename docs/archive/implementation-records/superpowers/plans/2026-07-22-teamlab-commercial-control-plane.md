# TeamLab Commercial Control Plane Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the proven single-runtime TeamLab networking foundation into a reusable commercial scenario, rollout, lifecycle, access, and high-concurrency control plane without creating a second scheduler or coupling TeamLab to competition entities.

**Architecture:** Keep the existing TeamLab data plane and introduce three one-way boundaries: TeamLab Foundation owns scenarios and runtimes, TeamLab Orchestration owns rollout coordination, and Penetration Adapter maps games and teams to the public application contract. All workloads share Runtime Scheduling Core capacity accounting and node dispatch limits.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core/PostgreSQL, Redis, React/TypeScript, SWR, Docker, libvirt/KVM, WireGuard, OCI Registry, xUnit, Testcontainers.

---

## Source Design

Implementation must remain aligned with:

- `docs/superpowers/specs/2026-07-22-teamlab-commercial-control-plane-design.md`
- `docs/commercialization/external-api-standard.md`
- `docs/commercialization/teamlab-api-foundation-contract.md`
- `docs/commercialization/agent-capability-protocol.md`
- `docs/commercialization/event-taxonomy.md`

## Plan Suite And Order

Execute these plans in order. A later plan may start only after the previous plan's module acceptance gate passes.

1. `docs/superpowers/plans/2026-07-22-teamlab-01-unified-runtime-scheduling.md`
2. `docs/superpowers/plans/2026-07-22-teamlab-02-scenario-library.md`
3. `docs/superpowers/plans/2026-07-22-teamlab-03-rollout-lifecycle.md`
4. `docs/superpowers/plans/2026-07-22-teamlab-04-permissions-access-experience.md`
5. `docs/superpowers/plans/2026-07-22-teamlab-05-cutover-capacity-acceptance.md`

## Cross-Phase File Boundaries

### Runtime Scheduling Core

- `src/GZCTF/Modules/Runtime/Domain/WorkloadResourceVector.cs`: common resource vector and arithmetic.
- `src/GZCTF/Modules/Runtime/Application/WorkloadSchedulingContracts.cs`: workload identity, fairness, capacity, and dispatch contracts.
- `src/GZCTF/Modules/Runtime/Application/NodeCapacitySnapshotService.cs`: actual plus reserved node capacity.
- `src/GZCTF/Modules/Runtime/Application/NodeEligibilityEvaluator.cs`: capability and resource eligibility only.
- `src/GZCTF/Modules/Runtime/Application/NodeDispatchLimiter.cs`: process-wide per-node/per-category gates.
- `src/GZCTF/Modules/Runtime/Application/RuntimeQueueSelector.cs`: fairness and subject serialization.
- `src/GZCTF/Modules/Runtime/Application/TeamLabPhysicalPlacementService.cs`: TeamLab-specific placement constraints, not generic capacity accounting.

### TeamLab Foundation

- `src/GZCTF/Modules/TeamLab/Domain/Scenario/`: scenario identity, immutable versions, manifests, validation runs.
- `src/GZCTF/Modules/TeamLab/Domain/Runtime/`: runtime, asset, shard, lifecycle, access session.
- `src/GZCTF/Modules/TeamLab/Application/Scenarios/`: scenario application ports and validation orchestration.
- `src/GZCTF/Modules/TeamLab/Application/Runtimes/`: lifecycle application ports.
- Existing topology, network, capture, bootstrap, and shard services remain focused executors.

### TeamLab Orchestration

- `src/GZCTF/Modules/TeamLab/Domain/Rollout/`: rollout and target aggregates.
- `src/GZCTF/Modules/TeamLab/Application/Rollouts/`: capacity preview, preparation, wave submission, drain, projections.
- `src/GZCTF/Modules/TeamLab/Infrastructure/Rollouts/`: workers, persistence configurations, and recovery.

### Identity And Adapters

- `src/GZCTF/Modules/Identity/Domain/Access/`: generic access groups and resource role bindings.
- `src/GZCTF/Modules/Identity/Application/ResourceAuthorization/`: resource authorization port and implementation.
- `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs`: game-to-rollout mapping only.
- TeamLab Foundation and Orchestration must not import Penetration namespace types.

### API And Frontend

- `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabScenariosController.cs`
- `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRolloutsController.cs`
- `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- `src/GZCTF/ClientApp/src/pages/admin/games/[id]/teamlab/`: admin rollout views.
- `src/GZCTF/ClientApp/src/pages/games/[id]/Penetration.tsx`: player workspace composition only; new views live in focused components.

## Non-Negotiable Constraints

- Do not add a TeamLab-only capacity ledger, dispatch limiter, or deployment queue.
- Do not retain direct game-wide `Task.WhenAll` deployment after rollout cutover.
- Do not use fixed sleeps, blind retries, or larger timeouts as lifecycle correctness.
- Do not delete game/team/runtime bindings before factual cleanup succeeds.
- Do not make TeamLab application services query game, participation, or training tables.
- Do not expose WorkerNode credentials, private addresses, protected payloads, or WireGuard private keys in projections.
- Do not use runtime snapshots as trusted reusable scenarios without validation and immutable manifest creation.
- Do not reserve the full forecasted capacity of 500 targets. Reserve only admitted waves with expiring leases.
- Do not run full solution tests after every small edit. Run focused tests inside a task and one module acceptance suite at each plan boundary.

## Commit Discipline

Each numbered task in the child plans produces one focused commit. Never stage unrelated Phase 9 work. Use explicit file lists with `git add -- <paths>` and do not amend commits.

Every named EF migration is generated from `src/GZCTF` with `dotnet ef migrations add <exact-name>`. The task must include the named migration `.cs`, its `.Designer.cs`, and the modified `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`; hand-written schema changes without a matching model snapshot are not accepted.

## Global Completion Gate

The plan suite is complete only when all of the following are true:

- A scenario can be validated, approved, stored as an immutable version, and pre-distributed.
- A generic external caller can create and observe a rollout without referencing competition entities.
- A penetration game uses that same rollout contract for at least 500 target records.
- Ordinary containers, ordinary VMs, training workloads, and TeamLab share capacity and dispatch budgets.
- Access close/open and workload suspend/resume are distinct operations.
- Each player has at most one active VPN device and team members do not evict each other.
- Reset accounting is atomic and infrastructure failures do not consume quota.
- Game end closes access first, drains and destroys by node budget, then keeps auditable tombstones.
- OpenAPI JSON, Chinese Swagger HTML, generated TypeScript clients, runbooks, and benchmarks match runtime behavior.
- Mixed-load, restart-recovery, node-loss, mass-close, and mass-destroy acceptance passes without leaked resources.

## Design Coverage Map

| Design requirement | Implemented by |
|---|---|
| Shared capacity and ordinary workload compatibility | Plan 01 Tasks 2-6 |
| Scenario validation, approval, immutable storage, and distribution | Plan 02 Tasks 1-7 |
| Canary, waves, progress, pause, drain, and game adapter | Plan 03 Tasks 1-3 and 7-9 |
| Access close/open, compute suspend/resume, reset, asset control, destroy | Plan 03 Tasks 4-6 |
| Permission groups and external resource authorization | Plan 04 Tasks 1-3 and 6 |
| Per-user one-device VPN and player connection health | Plan 04 Tasks 4-5 and 8 |
| Administrator and player projections and UI | Plan 04 Tasks 7-9 |
| Legacy removal, 500-target load, restart recovery, docs, and two-worker acceptance | Plan 05 Tasks 1-6 |
