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

- [x] Create the protocol assembly and define a strict compatibility contract.

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

- [x] Define enrollment requests around guest-generated ECDSA P-256 CSRs. The Worker never generates or returns a guest private key.
- [x] Extend runtime signals with control-plane stages while preserving numeric values for existing stages. Add explicit mapping rather than renumbering historical values.
- [x] Add prepared-artifact and preparation-job entities. A prepared artifact is uniquely keyed by `(sourceImageHash, factoryVersion, guestProtocolVersion, osType)`.
- [x] Add `PreparationContractVersion` and `GuestProtocolVersion` to capability certification. Legacy certifications remain readable and fail the current-prepared compatibility predicate; publication enforcement remains the deliberate Large Unit 4 cutover.
- [x] Replace `TeamLabBootstrapExecution.Attempt` behavior with immutable execution identity plus boot epoch. Keep the column only for migration backfill, set it to one, and prohibit increments in application code.
- [x] Add migration and model snapshot updates. Backfill existing VM images as `Raw`; do not create fake prepared artifacts.
- [x] Add contract tests for incompatible versions, stale generation, native UUID mismatch, duplicate sequence conflict, and prohibited attempt increments.

**Large-unit gate:**

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build --filter "FullyQualifiedName~GuestControlContract|FullyQualifiedName~RuntimeSignal|FullyQualifiedName~BootstrapProfileCompatibility"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-build --filter "FullyQualifiedName~PreparedArtifactMigration|FullyQualifiedName~RuntimeSignal"
```

Expected: protocol versions negotiate deterministically; stale/conflicting events are rejected; no legacy certification can publish a TeamLab VM; migration has no pending model changes.

## 4. Large Unit 2: Worker Management Network and mTLS Gateway

- [x] Add bootstrap configuration for `gzmgt0` with Worker-local `100.127.0.1/16`. The subnet is not announced through Fabric and has no host forwarding.
- [x] Implement deterministic lease allocation from `(runtimeId, generation, assetKey)` with collision detection under `AgentResourceLock`. Persist leases under the Agent TeamLab state root using atomic replace.
- [x] Apply nftables rules that allow guest-to-Worker TCP 5443, deny management-to-Fabric/player forwarding, and deny access to Agent port 5001. VM management taps additionally use Linux bridge port isolation to block same-bridge guest-to-guest L2 traffic.
- [x] Add a dedicated Kestrel HTTPS listener on `100.127.0.1:5443`. Keep platform bearer endpoints on port 5001. Middleware rejects a route arriving on the wrong listener before controller dispatch.
- [x] Implement Worker-local CA creation with private-key file mode `0600`, CA rotation metadata, and certificate subjects containing the exact asset identity digest.
- [x] Implement one-time enrollment:
  - main platform prepares token, identity, intent digest, and expiry;
  - guest submits token plus CSR;
  - Agent atomically consumes token and signs a short-lived certificate;
  - a consumed, expired, wrong-UUID, or wrong-generation token is terminally rejected.
- [x] Store bootstrap intent and the recoverable pending token encrypted with a Worker-local AES-GCM key and exact-identity associated data. Config-drive materialization remains in Large Unit 3.
- [x] Validate guest mTLS certificate, body identity, monotonic sequence, boot epoch, and payload digest before projecting an event into `AgentRuntimeSignalPublisher`.
- [x] Add deterministic management-NIC domain/config contracts, bridge-port isolation, and exact-generation enrollment cleanup. TeamLab runtime activation remains the deliberate Large Unit 4 cutover after Guest Supervisor is available.
- [x] Add Agent capability features:

```csharp
public const string VmGuestManagement = "runtime.vm.guest-management.v1";
public const string VmConfigDriveV2 = "runtime.vm.config-drive-v2.v1";
public const string VmPreparedImage = "image.vm.prepared.v1";
public const string VmPreparedImageUpload = "image.vm.prepared-upload.v1";
```

- [x] Add dry-run/network tests that inspect commands and nft rules, plus integration tests using a generated client certificate against the management listener.

**Large-unit gate:**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~GuestManagementNetwork|FullyQualifiedName~GuestEnrollment|FullyQualifiedName~VmDomainBuilder|FullyQualifiedName~AgentCapabilityContract"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~GuestGateway|FullyQualifiedName~RuntimeSignal"
```

Expected: management traffic cannot reach player/Fabric paths; one token enrolls one VM; certificate/identity mismatches fail closed; duplicate valid events remain idempotent; cleanup removes all exact-generation management facts.

## 5. Large Unit 3: Guest Supervisor and Config Drives

- [x] Extract connection inventory providers from EndpointSensor into `GZCTF.GuestTelemetry`. Keep output and HMAC behavior byte-compatible for the Unix-channel executable.
- [x] Build Guest Supervisor as a Windows service and Linux systemd service. It reads platform configuration from fixed OS-specific paths and never accepts inbound network connections.
- [x] Implement first enrollment, certificate persistence with OS ACLs, mTLS event delivery, boot epoch persistence, and server CA pinning.
- [x] Implement lifecycle compare-and-set. A stage executes only when the local checkpoint's expected predecessor and platform intent digest match.
- [x] Port bootstrap package execution from `VmBootstrapService` into Guest Supervisor:
  - verify manifest schema, artifact digest, and platform signature;
  - enforce declared OS, architecture, files, commands, services, ports, and reboot count;
  - persist step result before emitting the next event;
  - never repeat a failed or completed step automatically.
- [x] Implement secret retrieval only after enrollment. Store secrets with restrictive ACLs, redact them from output, and delete generation secrets during explicit cleanup.
- [x] Implement reboot semantics: persist `RebootRequested`, emit it, invoke the OS reboot locally, increment boot epoch on next service start, then emit `GuestReenrolledAfterBoot` without rerunning prior steps.
- [x] Implement Linux NoCloud output and Windows ConfigDrive v2 output. Network data matches topology NICs by MAC and includes the isolated management NIC without a default route.
- [x] Publish self-contained `linux-x64` and `win-x64` artifacts. Add deterministic package hashes to the release manifest.
- [x] Run Guest Supervisor unit tests with an in-memory gateway and filesystem checkpoint sandbox. Tests drive persisted state and boot epochs directly without timing sleeps.

**Large-unit gate:**

```powershell
dotnet build src/GZCTF.GuestSupervisor/GZCTF.GuestSupervisor.csproj -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build --filter "FullyQualifiedName~GuestSupervisor|FullyQualifiedName~BootstrapProfile|FullyQualifiedName~EndpointSensor"
```

Expected: Linux and Windows service hosts produce identical protocol facts; reboot resumes the next checkpoint; failed steps do not rerun; secrets never enter logs; existing endpoint telemetry remains compatible.

## 6. Large Unit 4: Image Factory and TeamLab Cutover

- [x] Add Open API submission for Windows/Linux image preparation with idempotency and one-time onboarding secret protection. Never return the supplied credential in operation payloads or logs.
- [x] Implement isolated preparation VM placement only on KVM nodes supporting management v1, prepared-image v1, image upload, and the requested OS preparation method.
- [x] Support two preparation inputs:
  - platform-ready source with compatible Guest Supervisor/Cloudbase contract;
  - assisted Windows source with valid WinRM credential on the isolated e1000e preparation network.
- [x] Reject unsupported raw images before creating a derived template. Do not use offline registry mutation, password reset, credential guessing, or an automatic reboot as fallback.
- [x] In assisted Windows preparation, install fixed-digest Cloudbase-init, Guest Supervisor, drivers, and optional QGA packages from the Worker-local endpoint; run Sysprep/generalize and require a clean domain shutdown event.
- [x] Flatten the prepared overlay to a new qcow2, verify it, push it directly from Worker to the internal OCI registry, and return digest/size/provenance to the main platform.
- [x] Create a new `ImageTemplate` derivative linked to source template 69 or 34. Do not mutate source hash, status, file path, or certification.
- [x] Perform one fail-fast conformance deployment with QGA disabled. Require GuestEnrolled, NetworkApplied, no-op package completion, controlled reboot/resume, health, observation, and clean shutdown in one operation; any failed stage rejects the derivative.
- [x] Update TeamLab publication compatibility to require the current prepared-artifact and Guest Supervisor protocol certification. QGA capabilities remain optional metadata.
- [x] Change TeamLab VM creation to prepare enrollment and intent before domain start, then advance the existing dependency DAG from Guest Supervisor signals.
- [x] Remove QGA RPCs from TeamLab bootstrap/health and VM endpoint-sensor critical paths. Retain administrative QGA APIs for auxiliary diagnostics.
- [x] Ensure image distribution resolves runtime VM references to the prepared derivative digest and never prepares an image during deployment.

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
- [x] Large Unit 1 complete.
- [x] Large Unit 2 complete.
- [x] Large Unit 3 complete.
- [x] Large Unit 4 complete: preparation submission, Worker Image Factory, immutable derivative publication, QGA-disabled conformance, TeamLab Guest Supervisor cutover, and online Worker artifact synchronization are closed locally.
- [ ] Large Unit 5 complete.
- [ ] Consolidated local gate complete.
- [ ] Live acceptance complete.

### 2026-07-17 Large Unit 1 Evidence

- [x] Added dependency-free `GZCTF.GuestControl.Contracts` shared by the main platform and Worker Agent.
- [x] Added strict protocol negotiation, CSR-only enrollment contracts, exact asset/generation/native-UUID/boot-epoch identity fences, and monotonic event conflict semantics.
- [x] Added one-way source-to-prepared provenance, durable image preparation jobs, nullable certification protocol versions, and raw-template defaults.
- [x] Added combined migration `20260717033200_AddVmPreparedArtifactControlPlaneAndFactoryCutover`; repeated historical attempts fail migration explicitly instead of being silently merged, and normal history receives unique execution identities.
- [x] `dotnet build src/GZCTF.slnx -c Release --no-restore`: 0 warnings, 0 errors.
- [x] Focused unit gate: 12 passed, 0 failed.
- [x] PostgreSQL prepared-artifact migration/runtime-signal integration gate completed successfully.
- [x] `dotnet ef migrations has-pending-model-changes`: no model changes pending.

### 2026-07-17 Large Unit 2 Evidence

- [x] Added `gzmgt0` bootstrap, `100.127.0.1/16` deterministic leases, nft host isolation, and Linux bridge port isolation for VM management taps.
- [x] Added separate platform bearer and guest mTLS listener authentication; wrong-listener routes fail before controller dispatch.
- [x] Added RSA-3072 Worker CA metadata, RSA service certificate for cross-provider TLS compatibility, and ECDSA P-256 guest CSR enforcement with identity-derived certificate subjects.
- [x] Added encrypted one-time enrollment state, consumed-token rejection, exact generation/native UUID/boot epoch validation, and journal-before-ack event projection.
- [x] Full solution build: 0 warnings, 0 errors.
- [x] Focused management-network/enrollment/domain/capability gate completed successfully.
- [x] Real Kestrel HTTPS integration gate completed enrollment without a client certificate, then accepted a lifecycle event only with the issued mTLS certificate.

### 2026-07-17 Large Unit 3 Complete

- [x] Physically extracted connection telemetry contracts and Linux/Windows providers into `GZCTF.GuestTelemetry`; EndpointSensor now consumes the shared library and retains its legacy transport/signing adapter.
- [x] Added the cross-platform `GZCTF.GuestSupervisor` service host, fixed configuration locations, P-256 guest key generation, CA pinning, client certificate persistence, and OS-specific restrictive file ACL handling.
- [x] Added boot identity/epoch persistence and a compare-and-set lifecycle engine with durable pending emission, so a process restart resends an already-persisted event but never reruns an acknowledged stage.
- [x] Added exact-byte ECDSA manifest verification, SHA-256/size checked artifact download, bounded traversal-safe tar extraction, declared file/step/health execution, and durable `Running/Completed/RebootPending/Failed` step facts. Interrupted or failed steps are terminal and are never rerun automatically.
- [x] Added encrypted Worker-side generation secret state and mTLS-only secret retrieval bound to certificate, runtime, generation, native VM UUID, boot epoch, and declared opaque references. Guest materialization uses restrictive OS ACLs and no secret value enters config-drive or operational facts.
- [x] Added persisted reboot checkpoints and strict `RebootRequested -> boot epoch + 1 -> GuestReenrolledAfterBoot` validation. A same-boot restart cannot satisfy or repeat a rebooting step.
- [x] Added topology and management NIC expectations to config-drive, guest-side MAC/IP/prefix verification, structured OpenStack routes, and an isolated management NIC without a default route.
- [x] Added Windows service/systemd hosting plus deterministic self-contained `linux-x64` and `win-x64` release manifests. Verified both manifest hashes against their generated binaries.
- [x] Fixed the Worker HTTPS certificate contract for Schannel: RSA server certificates now include key encipherment and Windows server/client private keys use persistent OS key containers. Real Kestrel anonymous enrollment followed by mTLS event ingestion passes.
- [x] Focused Large Unit 3 gate passes: 44 unit tests and the real Kestrel guest gateway integration test pass. Existing solution dependency-advisory warnings remain tracked outside this unit; no compiler warning or error was introduced by Guest Supervisor code.

### 2026-07-17 Large Unit 4 Complete

- [x] Added idempotent Open API VM preparation, encrypted assisted-Windows onboarding credentials, deterministic capability-based Worker placement, and immutable source-to-derived provenance.
- [x] Added Agent Image Factory preparation with isolated WinRM, fixed-digest package validation, Sysprep/clean shutdown, qcow2 flatten/check, and direct OCI publication.
- [x] Cut TeamLab runtime and controlled certification over to the management NIC, ConfigDrive v2, mTLS Guest Supervisor lifecycle signals, static topology IP probing, and QGA-disabled domain creation.
- [x] Runtime publication and scheduling reject raw, stale, uncertified, or protocol-incompatible VM templates; prepared image distribution resolves the immutable OCI artifact directly.
- [x] Worker online synchronization now transfers Guest Supervisor with its SHA-256 and refreshes an existing Image Factory package manifest; KVM convergence checks require the new management/config-drive/prepared-image contract and no longer require QGA.
- [x] Corrected manifest signing to the fixed-length P-256 IEEE P1363 format consumed by Guest Supervisor; platform signing, persisted signature, and guest verification now share one contract.
- [x] Focused unit gate passes 37/37 and focused PostgreSQL/TeamLab integration gate passes 8/8 before the final API submission test addition.
- [x] Full solution build completes with 0 errors; existing repository NuGet vulnerability advisories remain visible and unchanged by this unit.
- [x] EF reports no pending model changes, and TeamLab/Content critical paths contain no `WaitVmGuestAsync`, `ApplyVmBootstrapAsync`, `CheckVmBootstrapHealthAsync`, `GetVmIpAsync`, or `VmQga` dependency.

### 2026-07-17 Golden Image Architecture Correction

- [x] Removed the unshipped `VmImagePreparationApplicationService`, assisted-Windows credential path, Worker VM factory controller, WinRM transport, package retry branches, and old public preparation route.
- [x] Added explicit `VmImageBuildSource`, `VmImageBuildJob`, versioned recipe catalog, Packer/QEMU Worker capability, immutable OCI build output, and independent post-build certification.
- [x] Added release-bundled, SHA-256-pinned builder dependencies and an internal OCI package distribution path; build Workers no longer download dependencies from public endpoints.
- [x] Added `Managed`, `Opaque`, and `Scenario` template modes, DHCP/preconfigured networking, host-side Opaque health probing, and conditional guest-control requirements.
- [x] Added release-time `BakeAtPublish` orchestration with an internal no-player runtime, scenario overlay flatten/check/upload, release-to-asset artifact mapping, repeated-publish reuse, and exact cleanup.
- [x] Added a clean-cut migration that drops old factory state rather than preserving an invalid dual-track compatibility layer.
- [x] Replaced preparation tests and OpenAPI documentation with source/recipe/build contracts.
- [ ] Run the consolidated local gate, independent quality review, immutable release build, and full Docker/Linux/Windows/AD two-Worker acceptance.

### 2026-07-20 Simplified Image Lifecycle Cutover

The platform-side ISO/Packer builder described above is superseded and removed. The supported production path is now:

```text
external image pipeline -> qcow2 import -> SHA-256 verification -> immutable OCI artifact
-> Opaque template -> controlled certification -> Managed template -> node distribution -> runtime
```

- [x] Removed platform build sources, recipes, Packer jobs, Worker builder endpoints, builder capability advertisement, and build-capacity reservation.
- [x] Added `POST /api/open/v1/images/vm-qcow2` with streaming SHA-256 verification, immutable OCI publication, durable operation state, and automatic KVM-node distribution.
- [x] Imported qcow2 templates are always `Opaque`; only a successful platform-controlled probe can promote the exact digest to `Managed`. External evidence never promotes runtime mode.
- [x] Scenario baking requires an already certified `Managed` template. Legacy Windows templates are not implicitly certified.
- [x] Migration `20260720153831_SimplifyVmImageLifecycle` removes the unshipped builder schema and retains only the runtime network-mode contract.
- [x] The Chinese Open API guide and runtime OpenAPI document expose the qcow2 import route and no longer expose builder routes.
- [x] Release solution build completed with zero errors; unit gate passed `614/614`, OpenAPI runtime documentation and comparator gates passed, EF reported no pending model changes, and `git diff --check` passed. The full local Testcontainers suite remains unavailable because Docker Desktop is not running on the development host.

#### 2026-07-20 Live Import and Distribution Evidence

- Immutable release: `phase9-qcow2-staging-20260720`.
- Release archive SHA-256: `a4fe8af2d0e9f3d7f3dbd015818a0eca54dffbc2ee412ab35464a058278327b3`.
- Import operation: `019f8052-b7a5-73f4-bf8c-baf1969b8979`; terminal stage `completed`, progress `3/3`, one attempt.
- Imported test template: `113`; runtime mode `Opaque`, network mode `Dhcp`, prepared artifact `48` in `Ready` state.
- Source/artifact SHA-256: `1b7af784841125887e688254229e282829e17245d0e47fe7280da9b68a30d9e1`.
- OCI manifest digest: `sha256:bfa09673143ff81fd52215d57782b1641f7f48303e58035837fdac345b5d5904`.
- Workers `10.0.7.118` and `10.0.7.125` both reached distribution status `Ready`; each local file was `196616` bytes and matched the source SHA-256 exactly.
- Deleting template `113` returned HTTP `204`. The template, prepared artifact, distribution rows, both node caches, and OCI manifest were all absent afterward; all three database residue counts were zero.
- The protected non-TeamLab VM on `10.0.7.125` was not modified.

The remaining live gate is controlled certification of a genuinely Managed-capable external qcow2, followed by one mixed Docker/Linux/Windows/AD two-Worker runtime, reset, destroy, traffic evidence, and residue inspection.
