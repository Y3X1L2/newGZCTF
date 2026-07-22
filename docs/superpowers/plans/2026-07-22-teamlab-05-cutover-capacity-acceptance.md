# TeamLab Cutover, Capacity, And Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove legacy control paths, prove capacity and recovery behavior, publish complete external documentation, and deliver one repeatable full-chain acceptance workflow with guaranteed cleanup.

**Architecture:** Cut adapters and APIs over to scenario/rollout/runtime contracts in one release. Keep only read-response compatibility, never duplicate writes. Use synthetic control-plane load for 500 targets and hardware-sized two-worker acceptance for Docker, Linux VM, Windows VM, VPN, traffic, reset, and destroy.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, Redis, OpenTelemetry, OpenAPI/Swagger UI, React/TypeScript, PowerShell/Python, Docker, libvirt/KVM, WireGuard, PCAP.

---

## Task 1: Remove Legacy Control Paths

**Files:**
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs`
- Modify: `src/GZCTF/Modules/Penetration/Domain/PenetrationTeamLabBindings.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabScenarioBakeService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAccessGrantService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeAccess.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimePrimitives.cs`
- Modify: `src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs`
- Create: `src/GZCTF/Migrations/20260722180000_FinalizeTeamLabCommercialControlPlane.cs`
- Create: `src/GZCTF.Test/UnitTests/Architecture/BoundaryScanner.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommercialCutoverTests.cs`

- [ ] **Step 1: Add architectural cutover tests**

```csharp
[Fact]
public void TeamLabFoundation_HasNoPenetrationDependencyOrLegacyBatchPath()
{
    var violations = BoundaryScanner.FindReferences(
        typeof(TeamLabModuleRegistration).Assembly,
        forbiddenNamespaces: ["GZCTF.Modules.Penetration"]);

    Assert.Empty(violations);
    Assert.Null(typeof(PenetrationTeamLabAdapter).GetMethod("DeployLegacyTeamsAsync"));
}
```

Also assert no replace-all access type, no release-publish bake call, and no write path driven by the deprecated single runtime status.

- [ ] **Step 2: Remove migrated implementations**

Delete the direct game team loop, old publish-time bake entry, replace-all grant logic, and deprecated lifecycle writes. Preserve historical database facts only for audit. No runtime branch may select old versus new behavior.

- [ ] **Step 3: Finalize migration constraints**

Backfill active game bindings to scenario/rollout IDs, revoke unidentified legacy grants, preserve tombstones, and apply non-null/foreign-key constraints after conversion. Abort migration with a specific precondition error if an active binding cannot be mapped.

- [ ] **Step 4: Keep read compatibility only**

Derive deprecated `status` and active-release response fields from new projections for one documented API window. Reject legacy writes or route them into the new application contract; never call old services.

- [ ] **Step 5: Test and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabCommercialCutoverTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~FinalizeTeamLabCommercialControlPlane
git add -- src/GZCTF/Modules/Penetration src/GZCTF/Modules/TeamLab src/GZCTF/Migrations src/GZCTF.Test/UnitTests/Architecture/BoundaryScanner.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommercialCutoverTests.cs
git commit -m "refactor: finalize TeamLab control plane cutover"
```

Expected: PASS and no dual write path.

## Task 2: Complete Operational Events, Metrics, And Projections

**Files:**
- Modify: `src/GZCTF/Modules/Audit/Domain/OperationalEventCodes.cs`
- Modify: `src/GZCTF/Infrastructure/Telemetry/PlatformTelemetry.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabEventRecorder.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutProjectionService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs`
- Modify: `docs/commercialization/event-taxonomy.md`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommercialObservabilityTests.cs`

- [ ] **Step 1: Add event coverage tests**

Assert every scenario, rollout, lifecycle, access, distribution, reset, and cleanup transition has a registered event code and sanitized detail. Failures include stable code, operation, target, stage, and worker when known.

- [ ] **Step 2: Add commercial event codes and bounded metrics**

Cover validation/approval/retirement, capacity/preparation/canary/wave/pause/drain, access open/close/session replacement, compute suspend/resume, asset restart/rebuild, cleanup residual, and tombstone. Metrics record counts and latency by workload kind, stage, result, and capability; IDs are forbidden metric labels.

- [ ] **Step 3: Serve lists from checkpointed projections**

Rollout and runtime list endpoints query indexed projection rows. Projection workers persist a sequence and recover after restart. Event detail remains cursor-paginated.

- [ ] **Step 4: Test and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabCommercialObservabilityTests
git add -- src/GZCTF/Modules/Audit/Domain/OperationalEventCodes.cs src/GZCTF/Infrastructure/Telemetry/PlatformTelemetry.cs src/GZCTF/Modules/TeamLab/Application/TeamLabEventRecorder.cs src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutProjectionService.cs src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs docs/commercialization/event-taxonomy.md src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommercialObservabilityTests.cs
git commit -m "feat: complete TeamLab commercial observability"
```

Expected: PASS.

## Task 3: Add Deterministic Capacity And Recovery Harnesses

**Files:**
- Create: `src/GZCTF.LoadTests/GZCTF.LoadTests.csproj`
- Create: `src/GZCTF.LoadTests/TeamLab/RolloutControlPlaneLoad.cs`
- Create: `src/GZCTF.LoadTests/TeamLab/MixedWorkloadSchedulingLoad.cs`
- Create: `src/GZCTF.LoadTests/TeamLab/LargeTopologyPlacementLoad.cs`
- Modify: `src/GZCTF.slnx`
- Create: `src/GZCTF.Integration.Test/Tests/Runtime/TeamLabRolloutRecoveryTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Runtime/TeamLabMassCleanupTests.cs`
- Create: `docs/commercialization/benchmarks/teamlab-commercial-capacity-baseline.md`

- [ ] **Step 1: Implement the 500-target rollout load**

```csharp
var result = await TeamLabLoadScenario.RunAsync(new(
    TargetCount: 500,
    CanarySize: 5,
    WaveSize: 50,
    CoordinatorWorkers: 8,
    AgentLatency: TimeSpan.FromMilliseconds(25)));

result.AssertNoSharedDbContextFailures();
result.AssertNoDuplicateTargetSubmission();
result.AssertNoUnboundedQueryGrowth();
```

Exercise preparation, canary, waves, projection reads, mass access close, and drain. Record query count, throughput, p50/p95/p99 latency, and allocations.

- [ ] **Step 2: Implement mixed scheduling and large placement loads**

Submit 200 ordinary container, ordinary VM, training, TeamLab Docker, and TeamLab VM requests. Assert no capacity overcommit, no class starvation, and node category limits. Run a 32-network/128-asset topology repeatedly and assert one placement hash within the configured computation budget.

- [ ] **Step 3: Implement restart and mass-cleanup integration tests**

Restart during distribution, canary, provisioning, access open, suspend, and destroy. Test one offline node during 500-session close/destroy: healthy nodes clean, offline targets retain bindings in CleanupPending, and later inventory reconciliation completes them. State transitions must not depend on fixed sleeps.

- [ ] **Step 4: Run and record baselines**

```powershell
dotnet run --project src/GZCTF.LoadTests/GZCTF.LoadTests.csproj -c Release -- --scenario teamlab-commercial-baseline --output artifacts/teamlab-load.json
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabRolloutRecoveryTests|FullyQualifiedName~TeamLabMassCleanupTests"
```

Acceptance: no duplicate runtime/reservation, no overcommit, all workload classes progress, projection GET p95 below 500 ms in the baseline environment, deterministic bounded placement, and restart recovery without duplicate effects.

- [ ] **Step 5: Commit**

```powershell
git add -- src/GZCTF.LoadTests src/GZCTF.slnx src/GZCTF.Integration.Test/Tests/Runtime/TeamLabRolloutRecoveryTests.cs src/GZCTF.Integration.Test/Tests/Runtime/TeamLabMassCleanupTests.cs docs/commercialization/benchmarks/teamlab-commercial-capacity-baseline.md
git commit -m "test: add TeamLab commercial capacity harness"
```

## Task 4: Publish Canonical OpenAPI And Chinese Swagger HTML

**Files:**
- Modify: `docs/commercialization/openapi/open-v1.json`
- Modify: `docs/commercialization/open-api-v1-guide.md`
- Modify: `docs/commercialization/teamlab-api-foundation-contract.md`
- Create: `docs/commercialization/openapi/index.html`
- Modify: `src/GZCTF.Integration.Test/Tests/Api/OpenApiDocumentationTests.cs`
- Modify: `src/GZCTF/ClientApp/src/generated/Api.ts`

- [ ] **Step 1: Add documentation completeness assertions**

Assert every approved scenario, rollout, runtime lifecycle, access-session, permission group, event, traffic, and operation route exists. Every write documents idempotency, `202`, operation polling, required resource action, stable errors, and examples.

- [ ] **Step 2: Regenerate one canonical OpenAPI document**

Generate JSON from the running application. Verify unique operation IDs and protected-field exclusion. Do not manually maintain another endpoint schema.

- [ ] **Step 3: Update Chinese Swagger UI and guide**

Serve the same JSON from Swagger UI. Add Chinese authentication, resource authorization, idempotency, async polling, error taxonomy, and scenario-to-rollout examples. The HTML must not embed a divergent schema copy.

- [ ] **Step 4: Verify and commit**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~OpenApiDocumentationTests
pnpm --dir src/GZCTF/ClientApp check
git add -- docs/commercialization/openapi/open-v1.json docs/commercialization/open-api-v1-guide.md docs/commercialization/teamlab-api-foundation-contract.md docs/commercialization/openapi/index.html src/GZCTF.Integration.Test/Tests/Api/OpenApiDocumentationTests.cs src/GZCTF/ClientApp/src/generated/Api.ts
git commit -m "docs: publish TeamLab commercial API reference"
```

Expected: PASS.

## Task 5: Create One Repeatable Full-Chain Runner

**Files:**
- Create: `scripts/deployment/teamlab-commercial-acceptance.ps1`
- Create: `scripts/deployment/teamlab-commercial-acceptance.json.example`
- Create: `scripts/deployment/lib/TeamLabAcceptance.psm1`
- Modify: `scripts/deployment/phase9_teamlab_acceptance.py`
- Create: `docs/commercialization/runbooks/teamlab-commercial-full-chain-acceptance.md`
- Create: `src/GZCTF.Test/UnitTests/Deployment/TeamLabAcceptanceRunnerTests.cs`

- [ ] **Step 1: Define validated, secret-free configuration**

Configuration contains platform URL, token environment-variable name, two WorkerNode IDs, scenario source IDs, test user IDs, output directory, and cleanup policy. Credentials and private keys never enter config or evidence.

- [ ] **Step 2: Implement idempotent prerequisites and setup**

Verify platform/Agent protocol, Docker/KVM/WireGuard/traffic capabilities, Registry reachability, disk headroom, Ready scenario, artifact distribution, and access prerequisites before creating a rollout.

- [ ] **Step 3: Execute all functional checks**

Use Docker entry, managed Linux VM, Windows VM, mixed RFC1918 networks, and at least two physical shards. Verify pre-distribution, runtime readiness, two user sessions, VPN/DNS/entry service, routing, isolation, access close/open, compute suspend/resume, reset without download, traffic path, and PCAP.

- [ ] **Step 4: Guarantee cleanup and evidence**

Use `finally` to close access, stop capture, drain rollout, wait for factual destroy, and query both Agent inventories. Delete only operation-owned test resources. Emit JSON containing digests, IDs, placements, timings, reservations, VPN health, probes, traffic, PCAP digest, cleanup facts, and redacted final result.

- [ ] **Step 5: Validate locally and commit**

```powershell
pwsh scripts/deployment/teamlab-commercial-acceptance.ps1 -Config scripts/deployment/teamlab-commercial-acceptance.json.example -ValidateOnly
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabAcceptanceRunnerTests
git add -- scripts/deployment/teamlab-commercial-acceptance.ps1 scripts/deployment/teamlab-commercial-acceptance.json.example scripts/deployment/lib/TeamLabAcceptance.psm1 scripts/deployment/phase9_teamlab_acceptance.py docs/commercialization/runbooks/teamlab-commercial-full-chain-acceptance.md src/GZCTF.Test/UnitTests/Deployment/TeamLabAcceptanceRunnerTests.cs
git commit -m "test: automate TeamLab full-chain acceptance"
```

Expected: validation and fake execution PASS.

## Task 6: Final Local And Two-Worker Acceptance Gates

**Files:**
- Modify: `docs/commercialization/phase-09-teamlab-networking-commercialization.md`
- Modify: `docs/platform-commercialization-audit-progress.md`
- Update after real run: `docs/commercialization/benchmarks/teamlab-commercial-capacity-baseline.md`
- Generated evidence: `artifacts/teamlab-acceptance/<operation-id>/`

- [ ] **Step 1: Run one final local backend pass**

```powershell
dotnet test src/GZCTF.slnx -c Release --no-restore
```

- [ ] **Step 2: Run frontend, API, load, and recovery passes**

```powershell
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp test --run
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter FullyQualifiedName~OpenApi
dotnet run --project src/GZCTF.LoadTests/GZCTF.LoadTests.csproj -c Release -- --scenario teamlab-commercial-baseline --output artifacts/teamlab-load-final.json
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabRolloutRecovery|FullyQualifiedName~TeamLabMassCleanup"
```

- [ ] **Step 3: Run build, diff, and boundary checks**

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
git diff --check
```

Verify no TeamLab-to-Penetration dependency and no legacy dual write path.

- [ ] **Step 4: Deploy one standard release package to two workers**

Preserve platform files and volumes. Deploy matching Agent binaries and protocol versions. Do not hot-swap individual DLLs.

- [ ] **Step 5: Run real full-chain acceptance**

```powershell
$env:GZCTF_ACCEPTANCE_TOKEN='<provided-at-execution-time>'
pwsh scripts/deployment/teamlab-commercial-acceptance.ps1 -Config artifacts/teamlab-acceptance-config.json
```

Expected: exit 0 and machine-readable evidence.

- [ ] **Step 6: Independently verify cleanup**

Read tombstones and both Agent inventories. Confirm no acceptance-owned container, domain, overlay, ISO, namespace, veth, route, firewall, capture, lease, reservation, or distribution claim remains. Scenario Registry artifacts remain referenced and reusable.

- [ ] **Step 7: Record completion evidence and commit**

```powershell
git add -- docs/commercialization/phase-09-teamlab-networking-commercialization.md docs/platform-commercialization-audit-progress.md docs/commercialization/benchmarks/teamlab-commercial-capacity-baseline.md
git commit -m "docs: close TeamLab commercial control plane delivery"
```

The implementation is complete only after local gates and the real two-worker gate pass.
