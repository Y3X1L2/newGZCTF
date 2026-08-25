# Phase 9 Runtime Readiness and Acceptance Stabilization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans`. Work in the large units defined below. Do not deploy after individual edits. Update the progress ledger in this file after each large unit.

**Goal:** Replace the current timeout-driven TeamLab acceptance loop with durable readiness signals, a measured VM fast path, atomic fleet deployment, and one reproducible Docker/Linux/Windows/AD acceptance run.

**Architecture:** Keep the Phase 6 queue, Phase 7 audit/recovery model, Phase 9 topology compiler, shard scheduler, and dependency DAG as the control-plane foundation. Move readiness ownership to the component that can prove it: Agent owns host/network/libvirt/QGA facts; the main service persists monotonic Agent signals and advances the existing asset DAG only after a matching runtime/generation/resource signal. Short network mutations return a deterministic readiness receipt; long VM operations report durable asynchronous signals. Safety deadlines remain fail-closed circuit breakers and are never used as a substitute for readiness.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core/PostgreSQL, Redis, GZCTF.Agent, Docker, libvirt/KVM, qemu guest agent, dnsmasq, Linux network namespaces, qcow2 backing overlays, OCI image distribution, PowerShell, Python/Paramiko deployment transport, xUnit, Testcontainers.

---

## 1. Execution Rules

This plan supersedes the live edit/deploy/retry loop for the remaining Phase 9 acceptance work.

1. No additional deployment is allowed until Large Units 1-5 are complete locally.
2. Tests run at large-unit boundaries, not after each small edit.
3. Main service, Agent, endpoint sensor, scripts, API document, and migrations are built into one immutable release manifest.
4. The release is uploaded once to the main server. Worker Agents update from the main server over the internal network.
5. Acceptance creates TeamLab API resources only. It does not create games, courses, or unrelated competitions.
6. Every acceptance resource carries one run ID and is deleted in a `finally` cleanup path.
7. No script may delete a resource without matching the current acceptance run ID, runtime generation, and managed-resource labels.
8. A failed stage stops the run and preserves bounded evidence. It does not immediately modify code or retry with a larger timeout.
9. Windows and AD performance are judged from stage timestamps, not from a single overall HTTP timeout.
10. Phase 9 is not complete until Docker, Linux VM, Windows VM/AD, traffic evidence, reset, destroy, and residue checks all pass in one run.

## 2. Evidence-Based Current State

### 2.1 Confirmed working foundations

- Two physical Workers, `10.0.7.118` and `10.0.7.125`, report Docker, KVM, TeamLab Fabric, packet observation, endpoint sensor, VM QGA, and Windows bootstrap capabilities.
- A two-shard Docker runtime has already proved entry-to-core ICMP and HTTP across `10.96.0.0/28` and `172.29.0.0/28`.
- Docker runtime reset generation 3 completed its control-plane operation in approximately 3.3 seconds.
- VM creation already uses qcow2 backing overlays rather than copying the full template for every runtime.
- Linux template 34 and Windows template 69 remain managed templates and must not be deleted by acceptance cleanup.
- Image pre-distribution, topology releases, dependency DAG execution, QGA bootstrap, observer ingestion, path correlation, and multi-node PCAP contracts already exist and are retained.
- The currently deployed diagnostic build has main assembly SHA-256 `a0274d3b682952839d1556aeeab644a9846a13512874ab81d48c035c1d197a52` and Agent SHA-256 `e680e2fc4a8d675d1061cabf3218487adf5c1a3f2bfa205f6f902b816eb9d7a0` on both Workers. These are evidence anchors, not the final immutable release.

### 2.2 Confirmed defects and root causes

| Root cause | Runtime evidence | Required correction |
| --- | --- | --- |
| DNS ownership is incomplete | dnsmasq returned the A record but forwarded the AAAA query for the local runtime domain; clients waited or failed resolution | Mark the runtime domain authoritative with `--local`, validate the domain, and prove a DNS transaction before releasing a workload |
| Container startup ownership is split | main service creates a held container, Agent attaches veths, main service separately writes the gate marker after a fixed delay; nginx can start before DNS is usable | Agent finalizes all interfaces, routes, resolver facts, DNS response, and the gate marker in one generation-scoped operation |
| Reconciliation is unaware of active lifecycle mutation | fact reconciliation marked generation-1 assets missing and control resources orphaned while reset cleanup was still active | Exclude facts owned by an active create/reset/destroy ticket; reconcile them through ticket recovery until the operation reaches a terminal state |
| Generationless network names lack a fence | a delayed generation-1 cleanup can delete generation-2 bridge, router namespace, DNS, Fabric peer, and observation resources because their Linux names do not contain generation | Serialize apply/cleanup by runtime on Agent, persist active generation, and reject stale destructive cleanup against shared names |
| Stale destroy completion is workload-only | recovery can declare destroy complete after containers/VMs disappear even when DNS/router/Fabric, leases, mappings, grants, observations, or captures remain | Inspect Agent control resources and database cleanup side effects; converge through the one cleanup finalizer |
| Reset has no resumable phase contract | stale reset tickets fall into fail-closed because recovery handles create/stop/destroy only | Persist reset checkpoints for old-generation cleanup, new-generation planning, reservation, and deployment; resume from the checkpoint without incrementing generation twice |
| Agent update and scheduling overlap | a reset scheduled immediately after Agent restart saw no eligible TeamLab node before the new capability heartbeat arrived | Cordon node, sync Agent, verify expected SHA/feature manifest and Fabric health, then restore its prior schedulable state |
| Default Docker entrypoint bypasses the gate | only an explicit `StartCommand` receives the gate wrapper; a default image ENTRYPOINT/CMD can start and exit while still on `network=none` | Gate every TeamLab container and preserve the image's exact original Entrypoint/Cmd for release after network finalization |
| Linux VM certification disagrees with execution | Linux publication can omit QGA evidence, but the VM bootstrap path waits for QGA even without a profile | Require QGA certification for every TeamLab VM and publish `GuestReady` only after the matching QGA signal |
| VM readiness is treated as a long request | TeamLab waits up to 300/600 seconds in one Agent request; the number hides slow templates and blocks precise progress | Return VM domain creation immediately, track QGA/bootstrap/health as durable Agent operations, and advance the DAG from signals |
| Windows startup performance lacks a platform fast path | the controlled Windows image took about 125 seconds to reach QGA; earlier runtime requests hid the delay behind 600-second blocking calls | Record boot-stage evidence without rejecting the template, then optimize platform launch through pre-distribution, overlays, event-driven readiness, and a safe optional warm path |
| Deployment transport is repeated and fragile | a 52 MB Agent binary was manually uploaded to both Workers; VPN reset SSH near 9 MB repeatedly | Upload one resumable release bundle to the main server and use internal Agent self-sync for Workers |
| Acceptance is not reproducible | API calls, IDs, SSH diagnostics, traffic generation, and cleanup are manually reconstructed after each failure | Add one parameterized acceptance runner with fixtures, operation polling, evidence export, and guarded cleanup |

### 2.3 Temporary changes that are not accepted as final design

- The container-side `getent/nslookup` retry loop in `AgentTeamLabNodeExecutor` is a temporary diagnostic. Large Unit 1 removes it.
- Windows `600` seconds and Linux `300` seconds remain safety ceilings only until the signal contract replaces the blocking wait. They are not performance targets.
- Manual SFTP resume code used during diagnosis is not production deployment automation.
- The failed generation-3 Docker runtime and its remaining core container are acceptance-owned residue and must be destroyed by the guarded cleanup step before the next run.

## 3. Target Lifecycle

### 3.1 Durable control-plane sequence

```text
queue admission
  -> deterministic shard reservation
  -> image/profile ready
  -> infrastructure desired state applied
  -> asset native resource created
  -> network/guest readiness signal
  -> bootstrap signal
  -> service health signal
  -> observation ready
  -> runtime ready
```

Every signal is bound to:

```csharp
public sealed record AgentRuntimeSignalModel(
    Guid OperationId,
    Guid WorkerNodeId,
    int RuntimeId,
    int Generation,
    string ResourceKind,
    string ResourceId,
    long Sequence,
    AgentRuntimeSignalStage Stage,
    AgentRuntimeSignalOutcome Outcome,
    DateTimeOffset ObservedAt,
    string? ErrorCode,
    bool Retryable,
    IReadOnlyDictionary<string, string>? Facts);
```

The unique identity is `(WorkerNodeId, OperationId, Sequence)`. A signal for another runtime generation or native resource is rejected. PostgreSQL and the Agent journal are the facts; Redis is notification only. A lost Redis notification causes a persisted-signal read, not a lost transition. Agent journals are size/age bounded and are deleted only after the matching runtime generation is terminal and the main-service cursor is acknowledged. `Facts` is allow-listed and cannot carry userdata, flags, passwords, command output, or packet payload.

### 3.2 Container readiness transaction

1. Create the container with its normal process held by the existing startup gate.
2. Attach every declared interface without releasing the gate.
3. Call one Agent finalize operation containing all expected interfaces, routes, DNS servers, probe hostname, runtime ID, generation, and container identity.
4. Agent verifies interface names/MAC/IP, route table, DNS socket, and one A/NODATA DNS transaction from the container network namespace.
5. Agent writes `/tmp/.gzctf-teamlab-network-ready` only after all checks pass and emits `NetworkReady`.
6. A failed check destroys the exact container generation and returns a typed network error. No fixed sleep and no main-service retry loop remain.

The node bootstrap installs the DNS diagnostic binary once and advertises `teamlab.container-network-finalize.v1`. A node missing the feature cannot schedule TeamLab Docker assets.

### 3.3 VM readiness transaction

1. Main service allocates an operation ID before VM creation.
2. Agent creates the qcow2 overlay and libvirt domain, persists the native UUID/generation/operation journal, and returns immediately after `domain.running`.
3. Agent background coordination performs QGA readiness probes locally. QGA has no native guest-ready event, so this adapter may probe at 500 ms; only state transitions leave the Agent.
4. Agent emits `GuestReady`, `BootstrapRunning`, `Rebooting`, `GuestReadyAfterReboot`, and `HealthReady` signals.
5. Main service awaits Redis notification backed by the persisted signal row. On restart it reloads the current asset stage and replays the Agent journal cursor; it does not restart a completed bootstrap step.
6. A bounded safety deadline terminates a stalled operation, but measured template duration is not a scheduling eligibility condition. Duration evidence drives platform optimization and capacity planning only.

### 3.4 Reset, destroy, and reconciliation

- An active TeamLab queue ticket is the lifecycle owner for its runtime generation.
- Fact reconciliation must not correct owned transient facts while create/reset/destroy is active.
- Agent keeps one active-generation fence per runtime under `/var/lib/gzctf/teamlab/runtime-{id}/active-generation.json` and serializes apply/cleanup with the same runtime lock. A stale cleanup may remove only files and rules proven exclusive to its old generation. Agent restart cannot forget the fence.
- Reset persists `CleaningPreviousGeneration`, `PlanningNextGeneration`, `ReservingNextGeneration`, and `DeployingNextGeneration` checkpoints. Recovery resumes the persisted phase and never performs a second generation increment.
- Stale-ticket recovery first inspects Agent operation journals and current inventory, then chooses complete, resume, cleanup, or fail-closed.
- Destroy reaches terminal state only through the shared cleanup finalizer after both Agent control inventory and database side effects are empty.
- Reset keeps runtime public identity, WireGuard identity, topology semantics, and stable IP allocation where the released address is still available.
- Destroy stops signal trackers, sensors, captures, VMs/containers, infrastructure, and leases before capacity release.

## 4. Performance Contract

The acceptance runner records p50/p95 when repeated samples are available and enforces the following per-runtime ceilings in the two-Worker acceptance environment:

| Stage | Cold-path ceiling | Fast-path ceiling |
| --- | ---: | ---: |
| Two-shard Docker runtime ready | 20 s | 10 s |
| Linux VM domain running | 15 s | 10 s |
| Linux VM QGA + injected HTTP service ready | 90 s | 60 s |
| Windows VM QGA ready from certified image | 90 s | 45 s |
| Single-DC simple AD DNS/authentication ready | 150 s | 90 s |
| Mixed Docker + Linux + Windows/AD runtime ready | 240 s | 180 s |
| Reset with already-distributed images | no slower than initial runtime and no image transfer | same |

The fast path is not implemented by skipping checks. It uses:

- immutable templates already present on the target node;
- qcow2 backing overlays;
- QGA, virtio serial, storage, and network drivers baked into the certified image;
- Windows Update and firstboot installers completed before certification;
- AD DS binaries present in the image; the profile performs configuration/promotion only;
- an optional per-node managed-save warm pool for certified Windows templates.

Warm-pool entries are individually specialized clean VMs, not memory-cloned domain controllers. They have no competition network, flags, domain membership, or runtime secrets. A slot is claimed only after scheduler capacity reservation, is bound to one runtime generation, and is replenished asynchronously after claim. Stateful promoted DCs are never returned to the pool.

## 5. File and Ownership Map

### 5.1 Agent readiness and signal ownership

- Modify: `src/GZCTF.Agent/Models/TeamLabModels.cs`
- Modify: `src/GZCTF.Agent/Models/VmModels.cs`
- Modify: `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- Modify: `src/GZCTF.Agent/Controllers/VmController.cs`
- Modify: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
- Modify: `src/GZCTF.Agent/Services/DockerService.cs`
- Modify: `src/GZCTF.Agent/Services/TeamLab/TeamLabBridgeService.cs`
- Create: `src/GZCTF.Agent/Services/TeamLab/TeamLabContainerNetworkFinalizer.cs`
- Create: `src/GZCTF.Agent/Services/Vm/VmRuntimeOperationCoordinator.cs`
- Create: `src/GZCTF.Agent/Services/RuntimeSignals/AgentRuntimeSignalJournal.cs`
- Create: `src/GZCTF.Agent/Services/RuntimeSignals/AgentRuntimeSignalPublisher.cs`
- Modify: `src/GZCTF.Agent/Services/AgentCapabilityService.cs`
- Modify: `src/GZCTF.Agent/Program.cs`

### 5.2 Main-service orchestration and recovery

- Modify: `src/GZCTF/Services/Fleet/AgentClient.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabDependencyGraph.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/RuntimeFactReconciliationService.cs`
- Create: `src/GZCTF/Modules/Runtime/Domain/AgentRuntimeSignal.cs`
- Create: `src/GZCTF/Modules/Runtime/Application/RuntimeSignalService.cs`
- Create: `src/GZCTF/Modules/Runtime/Api/InternalRuntimeSignalsController.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeSignalAwaiter.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/Persistence/AgentRuntimeSignalEntityConfiguration.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Add one expand/backfill/contract migration set for signal persistence and asset operation identity.

### 5.3 VM performance and fleet update

- Modify: `src/GZCTF.Agent/Services/KvmService.cs`
- Modify: `src/GZCTF.Agent/Services/Vm/VmGuestAgentService.cs`
- Create: `src/GZCTF.Agent/Services/Vm/VmWarmPoolService.cs`
- Modify: `src/GZCTF/Modules/Content/Application/ImageTemplateCertificationService.cs`
- Modify: `src/GZCTF/Modules/Content/Application/BootstrapProfileCompatibilityService.cs`
- Modify: `src/GZCTF/Modules/Content/Infrastructure/VmImageCertificationProbeService.cs`
- Create: `src/GZCTF/Services/Fleet/AgentFleetUpdateCoordinator.cs`
- Modify: `src/GZCTF/Controllers/NodesController.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/NodeEligibilityEvaluator.cs`
- Modify: `src/GZCTF/Services/Fleet/NodeDeployService.cs`
- Modify: `docs/node-deployment/setup-gzctf-worker-node.sh`

### 5.4 Deployment and acceptance automation

- Create: `scripts/deployment/build-gzctf-release.ps1`
- Create: `scripts/deployment/deploy-gzctf-release.py`
- Create: `scripts/deployment/activate-gzctf-release.sh`
- Create: `scripts/phase9/invoke-teamlab-acceptance.ps1`
- Create: `scripts/phase9/Phase9.Acceptance.psm1`
- Create: `scripts/phase9/fixtures/docker-multishard.json`
- Create: `scripts/phase9/fixtures/linux-vm-mixed.json`
- Create: `scripts/phase9/fixtures/windows-ad-mixed.json`
- Create: `scripts/phase9/fixtures/traffic-path.json`
- Create: `scripts/tests/test_resumable_deploy.py`
- Modify: `.gitignore` only if a more specific evidence path is required; existing `artifacts/` exclusion is preferred.
- Retire after replacement: `scripts/deploy-server.py` because it is project-mismatched, deletes the remote root, rebuilds on the server, and cannot resume transport.

## 6. Large Unit 1: Readiness Signals and Container Network Finalization

- [ ] Add Agent runtime signal contracts, local append-only journal, monotonic sequence, authenticated callback, and replay-by-cursor endpoint.
- [ ] Persist signals idempotently in PostgreSQL and wake waiters through Redis after the database commit.
- [ ] Split the dependency graph into native-resource-created, network/guest-ready, bootstrap, and health nodes. Persist `AgentOperationId` and current signal sequence on the runtime asset.
- [ ] Add `TeamLabContainerNetworkFinalizeRequest` with runtime, generation, container native identity, expected interfaces, DNS servers, and probe name.
- [ ] Make Agent finalization verify all network invariants and release the startup gate in the same command ownership boundary.
- [ ] Gate every TeamLab Docker container. Preserve and replay the image's original Entrypoint/Cmd when no explicit start command is supplied; never start a default entrypoint on `network=none`.
- [ ] Add dnsmasq authoritative local-domain behavior and domain validation.
- [ ] Remove the fixed `sleep 0.2`, the main-service gate-marker exec, and the temporary container DNS retry loop.
- [ ] Make create/reset/destroy reconciliation aware of active queue-ticket ownership and suppress transient orphan correction until terminal or stale recovery.
- [ ] Add the Agent runtime generation fence and runtime-scoped apply/cleanup lock. A cleanup older than the active generation cannot mutate generationless Linux resources.
- [ ] Replace workload-only stale-destroy completion with full control-resource and database-side-effect verification followed by the shared cleanup finalizer.
- [ ] Persist reset checkpoints and add reset-specific stale-ticket inspection/replay without repeating generation increment or completed cleanup.

**Large-unit test gate:**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabCommandBuilderTests|FullyQualifiedName~TeamLabDeploymentOrchestrationTests|FullyQualifiedName~RuntimeFactReconciliationTests|FullyQualifiedName~TeamLabFoundationTopologyTests"
```

Expected: network finalization cannot release the gate before DNS evidence; default and explicit image commands are both gated; duplicate/out-of-generation signals are rejected; delayed old-generation cleanup cannot remove current resources; active reset facts are not marked missing; stale reset/destroy operations converge without residue.

## 7. Large Unit 2: VM Signal Path and Fast Launch

- [ ] Change VM create to return after overlay/domain identity is durable; do not hold the create HTTP request until QGA is ready.
- [ ] Persist and resume Agent VM operation journals across Agent restart.
- [ ] Emit QGA, bootstrap, reboot, and health transitions; make bootstrap checkpoints consume the exact operation/generation signal.
- [ ] Require `GuestQga` certification for Linux and Windows TeamLab templates, including VMs without bootstrap profiles or endpoint sensors. Do not publish `GuestReady` before QGA evidence.
- [ ] Record certification stage durations as operational evidence; never use duration alone to reject scheduling.
- [ ] Fail TeamLab publication only for missing QGA/driver capability evidence, not for a slow boot measurement.
- [ ] Measure template 69 against the platform startup target and optimize the platform path until the end-to-end budget is met.
- [ ] Benchmark the event-driven overlay path first. Add a bounded managed-save warm pool only if the platform path still misses the three-minute mixed-runtime target; never reject a template because of its measured duration.
- [ ] If the measured gate requires a warm path, key slots by immutable template digest and VM shape, include claims in normal capacity reservation, and prohibit reuse after runtime secrets, competition networking, domain membership, or bootstrap state exist.
- [ ] Keep Linux on the direct overlay path unless measured evidence shows a warm pool is needed.

**Large-unit test gate:**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~VmGuestControlTests|FullyQualifiedName~TeamLabVmNetworkTests|FullyQualifiedName~TeamLabDeploymentOrchestrationTests|FullyQualifiedName~ImageTemplateCertification"
```

Expected: create returns at domain-running; every TeamLab VM requires certified QGA; signals advance the exact generation; restart replays without rerunning completed bootstrap; warm slots cannot leak identity, secrets, networking, or capacity.

## 8. Large Unit 3: Agent Fleet Update and Scheduling Isolation

- [ ] Add an update coordinator that compares each heartbeat manifest to the bundled desired Agent SHA and required feature set.
- [ ] Cordon the node before update while preserving its prior schedulable setting.
- [ ] Update from the main server internal URL, verify binary/sensor hashes, restart Agent, wait for a matching capability manifest and healthy Fabric, then uncordon.
- [ ] Never make Docker eligibility depend on KVM capability. VM eligibility continues to require KVM/QGA features.
- [ ] Prevent runtime placement onto a node whose update state is `cordoned`, `syncing`, `awaiting-heartbeat`, or `failed`.
- [ ] Emit one correlated audit sequence for start, binary transfer, restart, manifest confirmation, Fabric confirmation, and completion/failure.

**Large-unit test gate:**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~NodesControllerTests|FullyQualifiedName~AgentCapabilityContractTests|FullyQualifiedName~RuntimeControlPlaneTests|FullyQualifiedName~NodeEligibility"
```

Expected: deployment cannot race scheduling against an Agent restart; Docker-only nodes remain valid Docker targets; a failed sync remains cordoned with a precise error.

## 9. Large Unit 4: Immutable, Resumable Deployment

`build-gzctf-release.ps1` performs one restore/build/publish pass and writes `release-manifest.json` containing Git commit, migration, every artifact path, size, and SHA-256. It packages the main publish output, Linux Agent, Linux/Windows endpoint sensors, scripts, and OpenAPI assets once.

`deploy-gzctf-release.py`:

1. reads host/user/password or key from environment; no credential is committed;
2. uploads chunks to `/opt/gzctf/staging/<release-id>.partial` and resumes from the verified remote offset after reconnect;
3. verifies total size and SHA-256 before rename;
4. invokes `activate-gzctf-release.sh` once;
5. streams only stage transitions and the final bounded diagnostic tail;
6. exits non-zero on transfer, activation, migration, health, or Agent convergence failure.

`activate-gzctf-release.sh`:

1. extracts to `/opt/gzctf/releases/<release-id>`;
2. keeps persistent files under `/opt/gzctf/shared/files` and links each immutable release to that directory;
3. validates ownership, configuration, binary hashes, and migration compatibility;
4. atomically switches `/opt/gzctf/current`;
5. restarts only `gzctf.service`;
6. waits for local health;
7. installs and verifies the main host's local Agent from the same release bundle while that node is cordoned;
8. lets the fleet coordinator converge remote Agents from the internal server URL;
9. rolls back the symlink and service if main health fails;
10. retains the current and one previous release, never ad hoc timestamped copies of `files`.

**Large-unit script gate:**

```powershell
python -m unittest scripts.tests.test_resumable_deploy
pwsh -File scripts/deployment/build-gzctf-release.ps1 -Configuration Release -VerifyOnly
python scripts/deployment/deploy-gzctf-release.py --plan-only
```

Expected: interrupted upload resumes without duplicate bytes; persistent `files` is never copied over or removed; plan-only output contains no credential; activation rollback is deterministic.

## 10. Large Unit 5: One Full Acceptance Runner

The runner accepts:

```powershell
pwsh -File scripts/phase9/invoke-teamlab-acceptance.ps1 `
  -BaseUrl http://10.0.7.118:8080 `
  -ApiToken $env:GZCTF_ACCEPTANCE_TOKEN `
  -WorkerHosts 10.0.7.118,10.0.7.125 `
  -LinuxTemplateId 34 `
  -WindowsTemplateId 69 `
  -OutputDirectory artifacts/phase9-acceptance
```

The script performs exactly one ordered run:

1. **Preflight:** API document, migration, registry, Redis, object storage, Worker manifests, Fabric, template certification, image/profile distribution, capacity, and clock skew.
2. **Guarded cleanup:** destroy only unfinished resources carrying the previous Phase 9 acceptance external-reference prefix. Verify deletion before continuing.
3. **Docker:** deploy two physical shards and two mixed RFC1918 networks; verify DNS short/FQDN response, both HTTP directions, isolation, flow metadata, PCAP, reset generation increment, and no image pull during reset.
4. **Linux VM mixed:** deploy Docker dependency plus Linux VM across two networks; verify domain-running signal, QGA, NoCloud static IP/DNS/routes, injected HTTP service, cross-shard traffic, reset, and cleanup.
5. **Windows/AD mixed:** deploy a certified DC and dependent member/service asset; verify QGA, AD profile, reboot transition, DNS, domain authentication, traffic/process evidence, and the performance contract.
6. **Traffic path:** generate A -> B, B -> C, C -> B, B -> A; verify four directional flows, ordered observation hops, endpoint evidence confidence, and multi-node PCAP manifest.
7. **Recovery:** restart one Agent only through the scripted coordinator, verify journal replay and no duplicate bootstrap; delay one old-generation cleanup and prove it cannot delete current-generation networking; interrupt reset at every persisted checkpoint; interrupt destroy after workload deletion and prove control resources still force replay. Do not restart the main server or host.
8. **Destroy:** destroy all runtimes and assert no acceptance-owned containers, domains, overlays, seed/config ISOs, namespaces, veths, routes, firewall rules, sensor sockets, active capture files, leases, reservations, or unreferenced distribution claims remain.
9. **Evidence:** write `summary.json`, `operations.jsonl`, `stage-durations.json`, `placements.json`, `traffic.json`, `cleanup.json`, and a concise Markdown report.

The script uses unique idempotency keys derived from the run ID and operation name. A repeated invocation with the same run ID reads the existing operation/resource identity instead of creating duplicates. The `finally` block attempts destroy and records cleanup failure without hiding the original error.

**Large-unit script gate:** run `-PlanOnly`, fixture schema validation, and mocked operation/evidence tests locally. Do not contact `10.0.7.118` in this gate.

## 11. Large Unit 6: Consolidated Verification and Single Deployment

Run once after Large Units 1-5:

```powershell
dotnet restore src/GZCTF.slnx
dotnet build src/GZCTF.slnx -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-build
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj --configuration Release
pwsh -File scripts/verify-openapi-contract.ps1
git diff --check
```

Required result: zero build warnings/errors, all unit/integration tests pass, no pending model changes, OpenAPI remains backward compatible, and no whitespace errors.

Then perform exactly one scripted deployment and one live acceptance run. Do not hot-patch a DLL or Agent binary during acceptance. If the run fails, retain evidence, clean acceptance-owned resources, update this plan with the failed invariant, return to local development, and repeat the consolidated gate only after the integrated correction is complete.

## 12. Exit Criteria

- [ ] No fixed sleep is accepted as proof of container startup, VM readiness, bootstrap completion, or runtime state transition; bounded internal probes may only publish observed state changes.
- [ ] Safety deadlines are documented circuit breakers derived from certification evidence.
- [ ] Agent signals are durable, idempotent, generation-bound, replayable, and auditable.
- [ ] Reconciliation does not fight an active lifecycle owner.
- [ ] Agent update cannot race workload scheduling.
- [ ] A release uploads once and Workers update over the internal network.
- [ ] Persistent `files` cannot be lost during release activation.
- [ ] Docker, Linux VM, Windows/AD, mixed traffic, reset, recovery, destroy, and cleanup pass in one run.
- [ ] Docker, Linux, Windows, and AD stage durations meet the contract or Phase 9 remains incomplete with measured evidence.
- [ ] The Phase 9 main plan, operations runbook, benchmark, OpenAPI reference, and project progress document contain the final evidence IDs and release hashes.

## 13. Progress Ledger

### 2026-07-16 Plan Freeze

- [x] Stopped the edit/deploy/retry acceptance loop.
- [x] Recorded the confirmed DNS, startup ownership, reconciliation, Agent update, VM performance, deployment, and acceptance automation root causes.
- [x] Defined event-driven readiness, performance budgets, immutable deployment, one-run acceptance, and guarded cleanup.
- [x] Integrated the focused lifecycle review: six P1 findings covering generation fencing, stale destroy, stale reset, cleanup reconciliation, default Docker entrypoints, and Linux QGA contracts. The review's 53 focused tests passed but did not cover these failure windows, so each is now an explicit test requirement.
- [x] User design/plan approval.
- [x] Large Unit 1.
- [x] Large Unit 2.
- [ ] Large Unit 3.
- [ ] Large Unit 4.
- [ ] Large Unit 5.
- [ ] Consolidated local gate.
- [ ] Single deployment.
- [ ] Full live acceptance and final evidence.

### 2026-07-16 Large Unit 1 Working Notes

- User approved the stabilization plan; no further live deployment or runtime retry has occurred.
- Added the main-service runtime signal foundation: node-authenticated internal ingest, PostgreSQL idempotency and payload-conflict detection, generation/node/asset operation ownership checks, persisted asset cursor, Redis wake-up, and PostgreSQL fallback reads.
- Added the Agent signal foundation: bounded append-only journal under the configured TeamLab state root, monotonic sequence, ACK cursor, startup replay, authenticated callback, and retry worker.
- Container network finalization/generation fencing and main-service reset/destroy recovery are being implemented in parallel. No large-unit compile or test gate has run yet.

### 2026-07-16 Large Unit 1 Complete

- Agent container lifecycle now has one generation-scoped finalization owner. Every TeamLab Docker image command is held, all interfaces/routes/resolver facts and real DNS answers are verified, and the gate is released only by the Agent finalizer.
- Removed the main-service container DNS retry loop and separate gate-marker exec. Main service advances the asset DAG only after the matching `NetworkReady` signal is committed to PostgreSQL; Redis is wake-up only and the database remains the replay source.
- Added the bounded Agent append-only signal journal, authenticated callback, monotonic cursor, PostgreSQL idempotency, payload-conflict detection, generation/node/operation ownership validation, and asset signal cursor.
- Added an Agent runtime generation fence and shared runtime lock. Delayed old-generation cleanup preserves generationless bridge/router/DNS/Fabric resources owned by the active generation.
- Added persisted reset checkpoints and stale reset replay. Stale destroy now checks workload inventory, TeamLab control-resource inventory, captures, grants, mappings, leases, distribution references, and cleanup state before terminal completion.
- Fact reconciliation excludes resources owned by active TeamLab lifecycle tickets, preventing normal create/reset/destroy mutations from being misclassified as drift.
- Added migration `20260716143433_AddRuntimeReadinessAndVmEvidence` for Agent operations, signal cursors, durable signals, and non-blocking VM performance evidence.
- Release build succeeded with zero warnings and errors. The Large Unit 1 concentrated gate passed `78/78` tests covering signal journal replay, command/finalizer invariants, generation fencing, deployment/cleanup orchestration, stale reset/destroy recovery, reconciliation ownership, and foundation topology behavior.

### 2026-07-16 Large Unit 2 Complete

- TeamLab VM create now returns after the overlay and libvirt domain identity are durable. It no longer holds an Agent HTTP request in `/guest/wait` for 300/600 seconds.
- Agent persists `DomainRunning` before returning, then performs the QGA adapter locally and publishes `GuestReady`. The same journal resumes unfinished QGA work after Agent restart.
- VM bootstrap now emits `BootstrapRunning`, `Rebooting`, `GuestReadyAfterReboot`, `BootstrapCompleted`, and `HealthReady` against the same operation/generation/native identity. Existing guest-side step checkpoints remain the idempotent bootstrap replay boundary.
- Linux and Windows TeamLab VM publication both require certified QGA plus their OS/network capability contracts. Certification records domain-create, QGA-ready, and full-probe durations, but duration is explicitly not a scheduling eligibility rule.
- Signal delivery now drains operations in parallel batches with a bounded degree of 16; the prior one-operation-per-two-second behavior was removed. Main-service signal reads use independent short-lived EF scopes so parallel assets cannot share a non-thread-safe `DbContext`.
- KVM launch keeps qcow2 backing overlays and now uses host CPU passthrough plus a host entropy source. The VM-create permit is released at domain-running, so local QGA waits do not serialize additional VM domains.
- Managed-save warm launch remains conditional on the live benchmark. It will not be implemented unless the safe event-driven overlay path misses the three-minute mixed-runtime target, because mutable memory-state cloning would otherwise add identity and AD correctness risk without evidence of need.
- The concentrated Large Unit 2 gate passed `51/51` tests covering VM identity/domain arguments, cloud-init multi-NIC networking, bootstrap checkpoints, certification compatibility, durable VM signal tracking/replay cleanup, and deployment orchestration.

### 2026-07-17 Live Acceptance Stabilization

- Split VM execution into durable `Create -> GuestReady -> Bootstrap -> Health` DAG nodes. VM create persists the native domain identity immediately; recovery cannot infer `GuestReady` from domain existence.
- Agent QGA readiness is event-driven and persistent. Linux generation 7 produced `DomainRunning -> GuestReady` in about 74 seconds and Windows generation 7 in about 392 seconds; slow readiness records evidence and does not fail or reject scheduling.
- Fixed Docker network finalization to inject only the primary interface network's local DNS gateway. Requiring a single-interface container to reach every runtime subnet DNS server violated the topology isolation contract and exceeded normal resolver limits.
- Removed automatic Docker daemon trust reconciliation from main-service startup and local-node registration. Registry trust changes are now an explicit node registration or registry migration responsibility, so a main-service restart cannot restart PostgreSQL, Redis, Registry, Guacamole, and workload containers.
- A main-service restart interrupted reset generation 8 after `DeployingNextGeneration`. Reconciliation correctly selected safe replay, but the execution preflight rejected the replay because it accepted only `ticketGeneration == runtimeGeneration + 1`. Reset execution now accepts both the pre-plan state (`ticketGeneration == runtimeGeneration + 1`) and the persisted target-generation state (`ticketGeneration == runtimeGeneration`); the reset orchestrator remains the checkpoint and ticket-ownership authority.
- The reset generation contract regression test passed `1/1`; the main service Release build completed with zero warnings and errors. The corrected DLL SHA-256 deployed to `10.0.7.118` is `4ee003c13c773aeee633a3eda48832ee526dca2acef9663a0dde2a57ddd39ace`; persistent `files` was not replaced.
- Generation 9 live reset operation `019f6bcd-e64e-7cda-95fa-99f8d82a3e71` advanced through cleanup, planning, atomic two-node placement, Fabric, infrastructure, route application, and four durable asset identities without another stale-generation failure.
- The current acceptance topology is not a valid startup benchmark: it requests `cpuUnits=100` (10 vCPU) for Linux and `cpuUnits=200` (20 vCPU) for Windows. On worker `10.0.7.125`, the Windows domain consumed more than one full host CPU while another VM was active and host load reached `9.35`; even `virsh domstate` exceeded 12 seconds during that contention. Publish a resource-corrected release before measuring the three-minute target.
- Live acceptance remains open. Do not mark Phase 9 complete until a resource-corrected release passes Docker, Linux VM, Windows/AD, traffic path, isolation, reset, destroy, and residue checks.

### 2026-07-17 Windows Prepared Image Factory Evidence

- Database and runtime evidence confirmed that the legacy challenge credential belongs to VMs backed by template `1`; template `69` has no recoverable onboarding credential. Password guessing, offline NTFS/SAM mutation, and password-reset fallbacks were removed from the supported path.
- Template `69` successfully reached QGA `110.0.2` with `guest-exec` enabled when the factory domain explicitly attached `org.qemu.guest_agent.0`. Assisted Windows preparation now uses QGA SYSTEM execution when no one-time credential is supplied.
- Operation `019f6f26-30e2-7dc9-aac0-6debc28dafd0` proved QGA execution and failed only because the source image could not load the PowerShell `Storage` module for `Get-Volume`. Factory scripts now locate the `GZFACTORY` volume through `System.IO.DriveInfo`.
- Operation `019f6f4d-5f2c-7021-b4d6-c37994b3ff78` proved the .NET volume path and failed because `manifest.json` was not visible at the Windows ISO view.
- Operation `019f6f60-ca69-7329-b556-6491ce71a1ef` proved recursive lookup still could not see the package files. The exact root cause is ISO generation without Joliet: Rock Ridge preserved long names for Linux inspection while Windows saw truncated ISO9660 names. Factory package ISO generation now uses both Joliet and Rock Ridge (`-J -R`).
- The current prepared-image attempt is `019f6f70-949f-73b8-a9e9-b182c20d6f1e`. Both Workers run Agent SHA-256 `98ca2fd38c3b062082fc9fc2e5aa8880e738900e67fb5607425c861e9eb87524`.
- The production API reference is available at `/api-docs`, loads only `/openapi/open-v1.json`, and includes Chinese authentication, idempotency, module, and TeamLab navigation guidance.

### 2026-07-20 VM Lifecycle Simplification and Storage Proof

- The online Worker Image Factory/Packer branch is removed. VM images now enter through one immutable contract: external qcow2 import, streaming digest verification, OCI storage, Opaque registration, controlled certification, capability-filtered distribution, and runtime use.
- Fixed the live import blocker by making the shared staging store accept an explicit `DockerArchive` or `VmQcow2` contract. Docker remains limited to `.tar`, `.tar.gz`, and `.tgz`; VM import accepts `.qcow2` and returns VM-specific format/size errors.
- Focused staging regression gate passed `4/4`.
- Deployed immutable release `phase9-qcow2-staging-20260720`, archive SHA-256 `a4fe8af2d0e9f3d7f3dbd015818a0eca54dffbc2ee412ab35464a058278327b3`.
- Operation `019f8052-b7a5-73f4-bf8c-baf1969b8979` imported and distributed template `113` to both `10.0.7.118` and `10.0.7.125`. The OCI and both local cache digests matched `1b7af784841125887e688254229e282829e17245d0e47fe7280da9b68a30d9e1`.
- Deletion returned `204` and removed the template, prepared artifact, distribution facts, both node caches, and OCI manifest. No smoke-test residue remains.
- Full mixed-runtime acceptance remains open; this storage proof does not claim Docker/Linux/Windows/AD networking completion.
