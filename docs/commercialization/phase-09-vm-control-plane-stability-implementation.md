# Phase 9 VM Control Plane Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan by large unit. Checkbox steps track durable progress. Do not deploy or run full acceptance between individual edits.

**Goal:** Replace QGA-dependent TeamLab VM provisioning with an isolated, authenticated Guest Supervisor control plane and an automatic platform-image preparation pipeline.

**Architecture:** Worker Agent owns a node-local VM management network, one-time guest enrollment, mTLS event ingestion, and config-drive delivery. Linux cloud-init and Windows Cloudbase-init start a preinstalled Guest Supervisor that applies signed bootstrap intent and emits durable lifecycle facts through the existing Phase 7/9 runtime-signal path. Raw images are converted by Image Factory into immutable platform-ready derivatives before TeamLab publication; QGA remains auxiliary only.

**Tech Stack:** .NET 10, ASP.NET Core/Kestrel, EF Core/PostgreSQL, Redis wake-up, libvirt/KVM, qcow2 overlays, cloud-init NoCloud, Cloudbase-init ConfigDrive v2, nftables bridge/inet policy, X.509 mTLS, OCI Registry v2, xUnit, Testcontainers, PowerShell, Python/Paramiko acceptance transport.

---

## 1. Execution Rules

1. Implement in five large units. Run focused tests only at each unit boundary.
2. Do not add a QGA timeout increase, cold-boot retry, automatic VM reboot, or automatic VM rebuild.
3. A transport reconnect may resume an already-persisted stage; it cannot rerun a stage or increment an attempt counter.
4. Existing Docker deployment, L3 Fabric, scheduler, traffic persistence, and Open API contracts remain operational throughout migration.
5. Templates 34 and 69 are immutable source artifacts and are never modified or deleted.
6. TeamLab publication switches to prepared-artifact certification only after Linux and Windows conformance paths both pass locally.
7. Live deployment occurs once after all local gates pass. `/opt/gzctf/publish/files` remains persistent and is not replaced.

## 2. File and Ownership Map

### Shared contracts and guest runtime

- Create `src/GZCTF.GuestControl.Contracts/GZCTF.GuestControl.Contracts.csproj`: protocol-only assembly with no platform dependency.
- Create `src/GZCTF.GuestControl.Contracts/GuestControlProtocol.cs`: version negotiation, identity, enrollment, intent, event, checkpoint, and health contracts.
- Create `src/GZCTF.GuestTelemetry/GZCTF.GuestTelemetry.csproj`: reusable Linux/Windows connection inventory and telemetry contracts extracted from EndpointSensor.
- Create `src/GZCTF.GuestSupervisor/GZCTF.GuestSupervisor.csproj`: self-contained Linux/Windows service.
- Create `src/GZCTF.GuestSupervisor/Enrollment/GuestEnrollmentClient.cs`: CSR generation, certificate persistence, and exact-identity enrollment.
- Create `src/GZCTF.GuestSupervisor/Lifecycle/GuestLifecycleEngine.cs`: monotonic checkpoint state machine.
- Create `src/GZCTF.GuestSupervisor/Bootstrap/GuestBootstrapExecutor.cs`: signed package verification and declared step execution.
- Create `src/GZCTF.GuestSupervisor/Telemetry/GuestTelemetryPublisher.cs`: process/connection enrichment over mTLS.
- Modify `src/GZCTF.EndpointSensor/*`: consume `GZCTF.GuestTelemetry` while retaining the existing Unix-channel compatibility executable.

### Worker Agent management plane

- Create `src/GZCTF.Agent/Services/GuestControl/GuestManagementNetworkService.cs`: `gzmgt0`, address leases, libvirt interface intents, and nft isolation.
- Create `src/GZCTF.Agent/Services/GuestControl/GuestEnrollmentStore.cs`: atomic generation-bound token/certificate/intent state.
- Create `src/GZCTF.Agent/Services/GuestControl/GuestCertificateAuthority.cs`: Worker-local CA and CSR signing.
- Create `src/GZCTF.Agent/Services/GuestControl/GuestEventIngestor.cs`: mTLS identity validation and runtime-signal projection.
- Create `src/GZCTF.Agent/Controllers/GuestControlController.cs`: main-platform authenticated prepare/revoke/status endpoints.
- Create `src/GZCTF.Agent/Controllers/GuestGatewayController.cs`: management-listener enrollment, intent, artifact, event, and secret endpoints.
- Create `src/GZCTF.Agent/Middlewares/AgentEndpointAuthenticationMiddleware.cs`: separate platform bearer and guest mTLS authentication paths.
- Modify `src/GZCTF.Agent/Program.cs`: dedicated HTTPS management listener and service registration.
- Modify `src/GZCTF.Agent/Models/AgentConfig.cs`: management listener, bridge, pool, CA, and state-root options.
- Modify `src/GZCTF.Agent/Models/VmModels.cs`: management NIC/config-drive intent and remove QGA from primary readiness.
- Modify `src/GZCTF.Agent/Services/Vm/VmDomainBuilder.cs`: deterministic management NIC attachment.
- Modify `src/GZCTF.Agent/Services/KvmService.cs`: config-drive lifecycle and exact-generation management cleanup.
- Modify `docs/node-deployment/setup-gzctf-worker-node.sh`: install management bridge/firewall prerequisites and create restricted state directories.

### Main platform and image factory

- Create `src/GZCTF/Modules/Content/Domain/VmPreparedArtifact.cs`: source/prepared provenance and factory status.
- Create `src/GZCTF/Modules/Content/Domain/VmImagePreparationJob.cs`: durable asynchronous factory operation.
- Create `src/GZCTF/Modules/Content/Application/VmImagePreparationService.cs`: authorization, idempotency, and operation submission.
- Create `src/GZCTF/Modules/Content/Infrastructure/VmImagePreparationOperationHandler.cs`: isolated preparation workflow.
- Create `src/GZCTF/Modules/Content/Infrastructure/WindowsPreparationService.cs`: typed WinRM preparation contract through Worker Agent.
- Create `src/GZCTF/Modules/Content/Infrastructure/PreparedImageCertificationService.cs`: structural plus one fail-fast protocol conformance deployment.
- Create `src/GZCTF/Modules/Content/Api/OpenVmImagePreparationController.cs`: Open API submission/status model.
- Modify `src/GZCTF/Models/Data/ImageTemplate.cs`: source-template relation and platform-ready artifact identity.
- Modify `src/GZCTF/Models/AppDbContext.cs` and Content persistence configuration files.
- Add one EF migration for prepared artifacts, preparation jobs, protocol certification version, and provenance.
- Modify `src/GZCTF/Services/Fleet/AgentClient.cs`: typed management/enrollment/preparation calls.
- Modify `src/GZCTF/Services/Fleet/ImageDistributionService.cs`: distribute prepared digest, never raw source, to runtime nodes.
- Modify `src/GZCTF/Services/Fleet/VmImageRegistryService.cs`: register prepared OCI artifact and provenance.

### TeamLab runtime integration

- Modify `src/GZCTF/Modules/Runtime/Contracts/RuntimeSignalContracts.cs` and Agent counterpart: new control-plane stages.
- Modify `src/GZCTF/Modules/Runtime/Contracts/AgentCapabilityContracts.cs` and Agent counterpart: feature IDs and compatible protocol range.
- Modify `src/GZCTF/Modules/Content/Application/BootstrapProfileCompatibilityService.cs`: require Guest Supervisor prepared-artifact certification.
- Modify `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`: prepare enrollment, attach management NIC/config drive, and wait for Guest Supervisor facts.
- Modify `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`: consume monotonic control-plane stages without QGA bootstrap RPC.
- Modify `src/GZCTF/Modules/TeamLab/Application/TeamLabBootstrapOrchestrator.cs`: build signed guest intent rather than execute through QGA.
- Modify `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs`: revoke enrollment and clean management resources/certificates.
- Modify `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeBootstrap.cs`: remove retry semantics and persist boot epoch/checkpoint identity.

### Verification and operations

- Create `scripts/phase9/invoke-vm-control-plane-conformance.ps1`: local/API conformance runner.
- Extend `scripts/phase9/invoke-teamlab-acceptance.ps1`: Docker, Linux, Windows, AD, mixed traffic, failure, and cleanup evidence.
- Modify OpenAPI JSON/guide and Phase 9 progress documents only after implementation contracts are stable.

## 3. Large Unit 1: Versioned Protocol and Durable State

- [ ] Create the protocol assembly and define a strict compatibility contract.

```csharp
public static class GuestControlProtocol
{
    public const int SchemaVersion = 1;
    public const int MinimumCompatibleVersion = 1;
}

public sealed record GuestAssetIdentity(
    Guid OperationId,
    int RuntimeId,
    int Generation,
    string AssetKey,
    string VmName,
    Guid NativeVmId,
    long BootEpoch);

public enum GuestLifecycleStage : byte
{
    ManagementLinkReady = 1,
    GuestEnrolled = 2,
    NetworkApplied = 3,
    BootstrapRunning = 4,
    RebootRequested = 5,
    GuestReenrolledAfterBoot = 6,
    BootstrapCompleted = 7,
    ServiceHealthReady = 8,
    ObservationReady = 9,
    Failed = byte.MaxValue
}
```

- [ ] Define enrollment requests around guest-generated ECDSA P-256 CSRs. The Worker never generates or returns a guest private key.
- [ ] Extend runtime signals with control-plane stages while preserving numeric values for existing stages. Add explicit mapping rather than renumbering historical values.
- [ ] Add prepared-artifact and preparation-job entities. A prepared artifact is uniquely keyed by `(sourceImageHash, factoryVersion, guestProtocolVersion, osType)`.
- [ ] Add `PreparationContractVersion` and `GuestProtocolVersion` to capability certification. Legacy certifications remain readable but cannot satisfy TeamLab VM publication.
- [ ] Replace `TeamLabBootstrapExecution.Attempt` behavior with immutable execution identity plus boot epoch. Keep the column only for migration backfill, set it to one, and prohibit increments in application code.
- [ ] Add migration and model snapshot updates. Backfill existing VM images as `Raw`; do not create fake prepared artifacts.
- [ ] Add contract tests for incompatible versions, stale generation, native UUID mismatch, duplicate sequence conflict, and prohibited attempt increments.

**Large-unit gate:**

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build --filter "FullyQualifiedName~GuestControlContract|FullyQualifiedName~RuntimeSignal|FullyQualifiedName~BootstrapProfileCompatibility"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-build --filter "FullyQualifiedName~PreparedArtifactMigration|FullyQualifiedName~RuntimeSignal"
```

Expected: protocol versions negotiate deterministically; stale/conflicting events are rejected; no legacy certification can publish a TeamLab VM; migration has no pending model changes.

## 4. Large Unit 2: Worker Management Network and mTLS Gateway

- [ ] Add bootstrap configuration for `gzmgt0` with Worker-local `100.127.0.1/16`. The subnet is not announced through Fabric and has no host forwarding.
- [ ] Implement deterministic lease allocation from `(runtimeId, generation, assetKey)` with collision detection under `AgentResourceLock`. Persist leases under the Agent TeamLab state root using atomic replace.
- [ ] Apply nftables rules that allow guest-to-Worker TCP 5443, deny guest-to-guest forwarding, deny management-to-Fabric/player forwarding, and deny access to Agent port 5001.
- [ ] Add a dedicated Kestrel HTTPS listener on `100.127.0.1:5443`. Keep platform bearer endpoints on port 5001. Middleware rejects a route arriving on the wrong listener before controller dispatch.
- [ ] Implement Worker-local CA creation with private-key file mode `0600`, CA rotation metadata, and certificate subjects containing the exact asset identity digest.
- [ ] Implement one-time enrollment:
  - main platform prepares token, identity, intent digest, and expiry;
  - guest submits token plus CSR;
  - Agent atomically consumes token and signs a short-lived certificate;
  - a consumed, expired, wrong-UUID, or wrong-generation token is terminally rejected.
- [ ] Store bootstrap intent encrypted with a Worker-local data-protection key. Config drive contains only the one-time token, CA pin, endpoint, and intent digest.
- [ ] Validate guest mTLS certificate, body identity, monotonic sequence, boot epoch, and payload digest before projecting an event into `AgentRuntimeSignalPublisher`.
- [ ] Attach a deterministic management NIC to every TeamLab VM and include it in exact-generation inventory and cleanup. Topology NIC configuration remains MAC-based, so management NIC ordering cannot alter challenge addresses.
- [ ] Add Agent capability features:

```csharp
public const string VmGuestManagement = "runtime.vm.guest-management.v1";
public const string VmConfigDriveV2 = "runtime.vm.config-drive-v2.v1";
public const string VmPreparedImage = "image.vm.prepared.v1";
public const string VmPreparedImageUpload = "image.vm.prepared-upload.v1";
```

- [ ] Add dry-run/network tests that inspect commands and nft rules, plus integration tests using a generated client certificate against the management listener.

**Large-unit gate:**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~GuestManagementNetwork|FullyQualifiedName~GuestEnrollment|FullyQualifiedName~VmDomainBuilder|FullyQualifiedName~AgentCapabilityContract"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~GuestGateway|FullyQualifiedName~RuntimeSignal"
```

Expected: management traffic cannot reach player/Fabric paths; one token enrolls one VM; certificate/identity mismatches fail closed; duplicate valid events remain idempotent; cleanup removes all exact-generation management facts.

## 5. Large Unit 3: Guest Supervisor and Config Drives

- [ ] Extract connection inventory providers from EndpointSensor into `GZCTF.GuestTelemetry`. Keep output and HMAC behavior byte-compatible for the Unix-channel executable.
- [ ] Build Guest Supervisor as a Windows service and Linux systemd service. It reads platform configuration from fixed OS-specific paths and never accepts inbound network connections.
- [ ] Implement first enrollment, certificate persistence with OS ACLs, mTLS event delivery, boot epoch persistence, and server CA pinning.
- [ ] Implement lifecycle compare-and-set. A stage executes only when the local checkpoint's expected predecessor and platform intent digest match.
- [ ] Port bootstrap package execution from `VmBootstrapService` into Guest Supervisor:
  - verify manifest schema, artifact digest, and platform signature;
  - enforce declared OS, architecture, files, commands, services, ports, and reboot count;
  - persist step result before emitting the next event;
  - never repeat a failed or completed step automatically.
- [ ] Implement secret retrieval only after enrollment. Store secrets with restrictive ACLs, redact them from output, and delete generation secrets during explicit cleanup.
- [ ] Implement reboot semantics: persist `RebootRequested`, emit it, invoke the OS reboot locally, increment boot epoch on next service start, then emit `GuestReenrolledAfterBoot` without rerunning prior steps.
- [ ] Implement Linux NoCloud output and Windows ConfigDrive v2 output. Network data matches topology NICs by MAC and includes the isolated management NIC without a default route.
- [ ] Publish self-contained `linux-x64` and `win-x64` artifacts. Add deterministic package hashes to the release manifest.
- [ ] Run Guest Supervisor unit tests with an in-memory mTLS gateway and filesystem checkpoint sandbox. Do not use sleeps; drive state changes with explicit test events.

**Large-unit gate:**

```powershell
dotnet build src/GZCTF.GuestSupervisor/GZCTF.GuestSupervisor.csproj -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build --filter "FullyQualifiedName~GuestSupervisor|FullyQualifiedName~BootstrapProfile|FullyQualifiedName~EndpointSensor"
```

Expected: Linux and Windows service hosts produce identical protocol facts; reboot resumes the next checkpoint; failed steps do not rerun; secrets never enter logs; existing endpoint telemetry remains compatible.

## 6. Large Unit 4: Image Factory and TeamLab Cutover

- [ ] Add Open API submission for Windows/Linux image preparation with idempotency and one-time onboarding secret protection. Never return the supplied credential in operation payloads or logs.
- [ ] Implement isolated preparation VM placement only on KVM nodes supporting management v1, prepared-image v1, image upload, and the requested OS preparation method.
- [ ] Support two preparation inputs:
  - platform-ready source with compatible Guest Supervisor/Cloudbase contract;
  - assisted Windows source with valid WinRM credential on the isolated e1000e preparation network.
- [ ] Reject unsupported raw images before creating a derived template. Do not use offline registry mutation, password reset, credential guessing, or an automatic reboot as fallback.
- [ ] In assisted Windows preparation, install fixed-digest Cloudbase-init, Guest Supervisor, drivers, and optional QGA packages from the Worker-local endpoint; run Sysprep/generalize and require a clean domain shutdown event.
- [ ] Flatten the prepared overlay to a new qcow2, verify it, push it directly from Worker to the internal OCI registry, and return digest/size/provenance to the main platform.
- [ ] Create a new `ImageTemplate` derivative linked to source template 69 or 34. Do not mutate source hash, status, file path, or certification.
- [ ] Perform one fail-fast conformance deployment with QGA disabled. Require GuestEnrolled, NetworkApplied, no-op package completion, controlled reboot/resume, health, observation, and clean shutdown in one operation; any failed stage rejects the derivative.
- [ ] Update TeamLab publication compatibility to require the current prepared-artifact and Guest Supervisor protocol certification. QGA capabilities remain optional metadata.
- [ ] Change TeamLab VM creation to prepare enrollment and intent before domain start, then advance the existing dependency DAG from Guest Supervisor signals.
- [ ] Remove QGA RPCs from TeamLab bootstrap/health and VM endpoint-sensor critical paths. Retain administrative QGA APIs for auxiliary diagnostics.
- [ ] Ensure image distribution resolves runtime VM references to the prepared derivative digest and never prepares an image during deployment.

**Large-unit gate:**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~VmImagePreparation|FullyQualifiedName~PreparedImageCertification|FullyQualifiedName~TeamLabDeploymentOrchestration|FullyQualifiedName~ImageDistribution"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~ImagePreparation|FullyQualifiedName~TeamLab"
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj --configuration Release
```

Expected: raw templates cannot enter TeamLab scheduling; derived artifacts retain provenance; Windows conformance succeeds with QGA disabled; runtime bootstrap uses only management-plane events; preparation never occurs on the runtime path.

## 7. Large Unit 5: Acceptance Automation, Documentation, and Cleanup

- [ ] Add a preflight that verifies management listener, CA, protocol range, prepared artifact, config-drive support, image distribution, Fabric, Redis, and runtime capacity before creating a runtime.
- [ ] Add Linux conformance: management enrollment, topology static IP/DNS/routes, signed HTTP service package, process telemetry, explicit reset, and cleanup.
- [ ] Add Windows conformance with QGA disabled: Cloudbase-init, enrollment, static network, service package, reboot/resume, health, and cleanup.
- [ ] Add simple AD conformance: prepared AD DS binaries, forest/domain configuration, DNS, authentication, reboot checkpoint, and dependent asset connectivity.
- [ ] Add mixed two-Worker topology with Docker, Linux VM, Windows/AD, mixed RFC1918 networks, explicit router edges, and unauthorized direct-access denial.
- [ ] Generate A -> B, B -> C, C -> B, and B -> A traffic. Verify host-side flow/PCAP evidence independently, then verify Guest Supervisor process enrichment.
- [ ] Add negative cases that must fail once without retry: wrong enrollment token, stale generation event, invalid package signature, failed bootstrap command, management isolation violation, and Agent restart during an already-completed stage.
- [ ] In `finally`, destroy only resources matching the acceptance run ID and verify no domains, overlays, config drives, management leases, certificates, bridges, nft rules, routes, sensors, captures, reservations, or secret files remain.
- [ ] Update OpenAPI HTML/JSON, API guide, operations runbook, Phase 9 main plan, stabilization plan, and progress ledger with final protocol and evidence IDs.

**Consolidated local gate:**

```powershell
dotnet restore src/GZCTF.slnx
dotnet build src/GZCTF.slnx -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-build
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj --configuration Release
pwsh -File scripts/verify-openapi-contract.ps1
pwsh -File scripts/phase9/invoke-vm-control-plane-conformance.ps1 -PlanOnly
pwsh -File scripts/phase9/invoke-teamlab-acceptance.ps1 -PlanOnly
git diff --check
```

Expected: zero build errors/warnings, all tests pass, no pending model changes, OpenAPI remains valid, scripts produce a deterministic plan without contacting a server, and no whitespace errors exist.

## 8. Live Acceptance Boundary

After the consolidated local gate:

1. Build one immutable release containing main service, Agent, Guest Supervisor for both OS families, EndpointSensor compatibility binaries, migrations, scripts, and OpenAPI assets.
2. Deploy once to `10.0.7.118`; preserve persistent files and update Workers through internal Agent synchronization.
3. Prepare derivative templates from source 34 and 69. Source templates remain untouched.
4. Run exactly one full acceptance operation on the two-Worker environment.
5. Preserve evidence on failure, perform guarded cleanup, return to local correction, and do not patch the live release in place.

## 9. Completion Criteria

- [ ] TeamLab Linux, Windows, and AD provisioning remains correct with QGA disabled.
- [ ] No correctness path contains an enlarged timeout, repeated cold boot, automatic reboot, automatic rebuild, or bootstrap-stage retry.
- [ ] Management traffic is isolated from player, challenge, and Fabric networks.
- [ ] Every lifecycle transition is authenticated, persisted, generation-bound, replayable, and auditable.
- [ ] Raw images cannot be scheduled; prepared artifacts have immutable provenance and current protocol certification.
- [ ] Full host-side traffic evidence does not depend on Guest Supervisor availability.
- [ ] Docker, Linux, Windows/AD, mixed routing, isolation, reset, destroy, and residue checks pass in one acceptance run.

## 10. Progress Ledger

### 2026-07-17 Design and Planning

- [x] Captured live QGA failure evidence from template 69: valid libvirt channel, installed VirtioSerial driver and ports, QEMU-GA SCM timeout, channel disconnected.
- [x] Rejected timeout growth, repeated cold boots, and QGA-only provisioning as production corrections.
- [x] Approved isolated management plane, Cloudbase-init, Guest Supervisor, and Image Factory architecture.
- [x] Wrote and committed `phase-09-vm-control-plane-stability-design.md` as commit `c48ca06`.
- [x] Wrote this implementation plan.
- [ ] Large Unit 1 complete.
- [ ] Large Unit 2 complete.
- [ ] Large Unit 3 complete.
- [ ] Large Unit 4 complete.
- [ ] Large Unit 5 complete.
- [ ] Consolidated local gate complete.
- [ ] Live acceptance complete.
