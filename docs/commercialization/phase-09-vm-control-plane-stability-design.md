# Phase 9 VM Control Plane Stability Design

## 1. Objective

TeamLab VM deployment must be deterministic for Linux, Windows, AD, and mixed Docker/VM topologies. Runtime correctness must not depend on increasing timeouts, repeating cold boots, retrying failed bootstrap steps, or waiting for QEMU Guest Agent (QGA) to recover by chance.

The platform will move VM provisioning and readiness to an isolated guest management control plane. QGA remains an optional hypervisor integration for shutdown, snapshots, and diagnostics. It is not a prerequisite for network configuration, service injection, endpoint observation, AD orchestration, or runtime readiness.

## 2. Non-Negotiable Invariants

1. A runtime stage advances only after a persisted, authenticated fact proves completion.
2. No fixed delay, enlarged timeout, repeated cold boot, or orchestration-level retry may be used as proof of readiness.
3. A failed bootstrap step is not rerun automatically. Recovery resumes only from a persisted idempotent checkpoint whose previous result is known.
4. A VM is never rebuilt or rebooted automatically to make an unexplained failure disappear.
5. Player, challenge, Fabric, and guest management traffic use separate routing and firewall domains.
6. Raw uploaded Windows images do not participate directly in runtime scheduling. Only immutable platform-prepared artifacts can be published for TeamLab use.
7. Template certification validates the artifact and control protocol contract. It does not certify reliability by counting successful retries or repeated boots.
8. PostgreSQL and the Agent journal are lifecycle facts. Redis is notification only.
9. Every operation is bound to `runtimeId`, `generation`, `assetKey`, `operationId`, and native VM identity. Stale facts cannot advance a newer generation.
10. A safety deadline may terminate and clean up a stalled operation, but it is never treated as the mechanism that makes the operation correct.

## 3. Rejected Approaches

### 3.1 QGA-only provisioning

Rejected because Windows QGA depends on virtio-serial driver and service ordering inside the guest. The live template 69 evidence showed a valid libvirt channel, installed VirtioSerial driver, and enumerated ports while the QEMU-GA service still failed to connect to the Service Control Manager. This dependency is not a suitable single control plane for commercial runtime orchestration.

### 3.2 Larger QGA wait budgets or more probes

Rejected because waiting cannot repair a missing, occupied, or failed guest channel. Probe loops may observe a state transition, but they cannot create correctness. A circuit breaker remains for cleanup only.

### 3.3 Multiple certification boots as reliability proof

Rejected because two or more successful boots only reduce the probability of observing a nondeterministic defect. Certification must verify a deterministic prepared-image contract and one complete control-plane transaction without retrying the transaction.

### 3.4 Warm pools as the primary correction

Rejected as the first solution because a warm pool hides image initialization defects and adds identity, capacity, lifecycle, and AD state risks. A warm pool may be considered later only as a measured performance optimization for already-correct immutable artifacts.

## 4. Target Architecture

### 4.1 Control plane roles

- **Main platform** owns topology compilation, placement, operation state, signed bootstrap intent, audit, and aggregated readiness.
- **Worker Agent** owns libvirt domains, local management networking, config-drive attachment, enrollment, guest event ingestion, host packet capture, and exact-generation cleanup.
- **Guest Supervisor** owns in-guest network application, service-package execution, reboot checkpoints, health evidence, and endpoint telemetry.
- **Image Factory** converts raw VM images into immutable platform-ready artifacts before publication.
- **QGA** is an optional auxiliary adapter. Its failure is reported independently and does not block the primary Guest Supervisor control channel.

### 4.2 Isolated VM management network

Each Worker creates a node-local management network that is never advertised through TeamLab Fabric and is never routed to player or challenge networks.

- Every VM receives one dedicated management NIC in addition to topology NICs.
- Windows uses an emulated `e1000e` management NIC to avoid a first-boot virtio dependency.
- Linux may use `virtio`, but the protocol does not depend on the model.
- Management addresses come from a Worker-local allocation pool and have no default route into TeamLab networks.
- nftables permits only VM-to-Worker enrollment, artifact retrieval, time, and telemetry endpoints required by the control protocol.
- VM-to-VM management traffic, forwarding to host networks, and access from player/Fabric interfaces are denied.
- Management addresses are control-plane facts and are never shown as challenge topology addresses.

The management network exists to carry control traffic, not challenge traffic. Its lifecycle is owned by the runtime generation fence and its resources are included in residue checks.

### 4.3 Guest enrollment and identity

The config drive contains a one-time enrollment token, Worker CA pin, runtime identity, desired artifact digest, and a signed bootstrap manifest. It does not contain reusable platform credentials.

1. Guest Supervisor connects outbound to the Worker management endpoint.
2. The Worker verifies the one-time token against the exact VM native identity and runtime generation.
3. Successful enrollment issues a short-lived mTLS client certificate bound to the asset.
4. The token is consumed atomically and cannot enroll another VM.
5. All subsequent events are signed by the mTLS identity and carry a monotonic guest sequence.
6. Worker Agent journals events before acknowledging them and forwards them to the main platform through the existing durable signal path.

Certificate rotation and reconnection maintain protocol liveness. They do not rerun lifecycle stages or bootstrap actions.

### 4.4 Config-drive provisioning

Linux retains cloud-init NoCloud. Windows implements Cloudbase-init with an OpenStack-compatible ConfigDrive v2 artifact.

The platform injects only runtime intent:

- hostname and stable instance identity;
- management and topology NIC identity by MAC address;
- topology IP, prefix, DNS, gateway, and routes;
- Guest Supervisor enrollment configuration;
- signed bootstrap profile reference and immutable digest;
- secret references only; actual runtime secrets are released over mTLS after successful enrollment.

Large binaries and scenario logic are not embedded in userdata. Guest Supervisor retrieves signed service packages from the Worker-local artifact endpoint after enrollment.

### 4.5 Image Factory

Image Factory is part of template onboarding, not runtime deployment.

For Windows, the preparation pipeline runs in an isolated, non-schedulable network and produces a new artifact digest:

1. Validate qcow2 integrity, OS family, architecture, free space, and supported Windows version.
2. Establish preparation control through one of two explicit contracts:
   - a platform-ready source already containing a compatible Cloudbase-init and Guest Supervisor bootstrap contract; or
   - an assisted source with a one-time administrator credential supplied as an onboarding secret, used only through WinRM on the isolated preparation network and erased after preparation.
3. Reject a raw image that has neither a compatible preparation contract nor a valid one-time control credential. The factory never attempts credential guessing or an undocumented offline registry mutation.
4. Install and configure Cloudbase-init, Guest Supervisor, supported network/storage drivers, and optional QGA.
5. Install only generic platform prerequisites. Scenario services and secrets are not baked into the image.
6. Configure services for deterministic boot ordering and disable first-boot package installation and update races.
7. Run Sysprep/generalize with the required device persistence policy and shut down cleanly.
8. Capture a new immutable qcow2 artifact, calculate its digest, upload it to the internal OCI registry, and record provenance from the raw source digest.
9. Run one fail-fast conformance deployment. It must complete enrollment, network application, a signed no-op bootstrap profile, health reporting, controlled reboot/resume, and clean shutdown without rerunning a failed stage.
10. Publish only the derived artifact if all structural and live protocol checks pass.

Linux images follow the same provenance and conformance model using cloud-init and the same Guest Supervisor protocol.

Templates 34 and 69 remain unchanged source artifacts. Platform-ready derivatives receive new identities and digests.

### 4.6 Runtime state machine

The VM execution state machine is:

```text
ImageReady
  -> DomainRunning
  -> ManagementLinkReady
  -> GuestEnrolled
  -> NetworkApplied
  -> BootstrapRunning
  -> RebootRequested (optional)
  -> GuestReenrolledAfterBoot (optional)
  -> BootstrapCompleted
  -> ServiceHealthReady
  -> ObservationReady
  -> AssetReady
```

Rules:

- State transitions are monotonic and compare-and-set against the expected previous state.
- Duplicate events with the same sequence and payload are idempotent.
- Duplicate sequence numbers with different payloads are integrity failures.
- Events from another generation, asset, VM UUID, or certificate are rejected.
- Bootstrap steps write guest and platform checkpoints before the next step begins.
- A reboot is initiated by the Guest Supervisor after persisting `RebootRequested`; resumption requires the same operation identity and the next boot epoch.
- No failed stage is automatically repeated. An administrator or explicit reset operation creates a new controlled lifecycle decision.

### 4.7 Traffic observation

Network evidence remains independent from the in-guest control channel.

- Worker capture points on VM tap interfaces, TeamLab bridges, router namespaces, and Fabric ingress/egress record all participating network segments.
- The path correlator reconstructs A -> B, B -> C, C -> B, and B -> A from host-observed flow facts across shards.
- Guest Supervisor contributes process, service, user, and command correlation metadata over the management channel.
- Loss of guest enrichment lowers evidence confidence but does not remove host-captured network evidence.
- PCAP collection remains bounded by size, duration, scope, retention, and runtime generation.

The previous VM endpoint-sensor virtio channel becomes an optional compatibility transport and is not required for complete network capture.

## 5. Service Package Contract

Service packages are immutable OCI artifacts with:

- manifest schema version;
- supported OS and architecture;
- required Guest Supervisor protocol range;
- declared files, commands, services, ports, health checks, and reboot checkpoints;
- parameter and secret schema;
- artifact digest and platform signature;
- rollback/cleanup instructions for resources created by the package.

Guest Supervisor verifies the digest and signature before extraction. It executes only declared entrypoints under bounded resource and time policies. Package output is redacted before audit ingestion. A package cannot modify management trust, enroll another identity, change topology routes outside its declared intent, or access another asset's secrets.

AD profiles use the same contract. AD DS binaries are prepared in the platform artifact; runtime work is limited to forest/domain configuration, DNS policy, account fixtures, controlled reboot, and health evidence.

## 6. Failure Handling

- Missing management link, failed enrollment, invalid signature, stale identity, network mismatch, bootstrap failure, and health failure produce distinct terminal error codes.
- Worker Agent records the last proven stage and exact failure evidence before reporting failure.
- Main platform marks the asset and runtime failed without rebuilding or rebooting it automatically.
- Cleanup is exact-generation and can be invoked independently after evidence retention.
- Explicit reset creates a new generation and never reuses secrets, certificates, management leases, or incomplete guest checkpoints.
- Safety deadlines are per-stage containment limits derived from the platform contract. Reaching one reports `stage_stalled`; it does not initiate another attempt.

## 7. Security Boundaries

- Management CA keys remain on Workers or the platform PKI service and are never placed in images.
- Enrollment tokens are one-time, short-lived, asset-bound, and encrypted inside the config-drive artifact at rest.
- Guest events and artifact requests require mTLS after enrollment.
- Management endpoints are unavailable from player WireGuard, TeamLab Fabric, challenge bridges, and public interfaces.
- Guest Supervisor runs with the minimum OS privileges required by each declared package step and records privilege transitions.
- Runtime secrets are released only after successful enrollment and never written to operational logs or traffic metadata.

## 8. Migration Strategy

1. Introduce management-network and Guest Supervisor contracts without changing existing Docker behavior.
2. Implement Linux Guest Supervisor over the existing cloud-init path and prove the state machine.
3. Implement Windows Image Factory, Cloudbase-init ConfigDrive, and Windows Guest Supervisor.
4. Switch TeamLab VM readiness from QGA to Guest Supervisor events.
5. Keep QGA telemetry as an independent optional capability during migration.
6. Remove VM bootstrap and endpoint observation dependencies on QGA after both OS paths pass conformance.
7. Invalidate legacy VM certifications for TeamLab publication unless a platform-ready derived artifact and current control-protocol certification exist.

There is no compatibility fallback that silently returns to QGA-only bootstrap. Unsupported legacy images remain visible but cannot be published for TeamLab runtime use.

## 9. Acceptance Criteria

### 9.1 Structural certification

- Artifact provenance links source digest, prepared digest, factory version, protocol version, and preparation evidence.
- Windows artifact contains configured Cloudbase-init and Guest Supervisor services, with no pending driver installation or update reboot.
- Service packages and config-drive manifests validate against versioned schemas and signatures.
- No runtime path installs drivers, package managers, Cloudbase-init, or Guest Supervisor.

### 9.2 Runtime correctness

- Docker, Linux VM, Windows VM, Windows AD, and mixed environments advance solely from persisted facts.
- Windows provisioning succeeds with QGA disabled to prove the primary path is independent.
- Agent or main-service restart replays persisted facts without repeating bootstrap stages.
- A stale event, duplicated conflicting event, expired enrollment token, or wrong VM UUID is rejected.
- No automatic VM rebuild or stage retry occurs after a terminal error.

### 9.3 Network and observation

- Management addresses are unreachable from player and challenge networks.
- Management traffic does not appear as a challenge topology edge.
- Authorized topology edges work and unauthorized direct access fails across shards.
- Host capture records all four A -> B -> C -> B -> A directions even if guest enrichment is disabled.
- Guest enrichment correctly associates process/service facts when enabled.

### 9.4 Performance and cleanup

- Runtime uses pre-distributed immutable images and qcow2 overlays; no image preparation occurs during deployment.
- Linux service, Windows service, and simple AD profiles meet the Phase 9 stage budgets without skipping health checks.
- Destroy removes domains, overlays, config drives, management leases, certificates, bridges, routes, capture jobs, and runtime secrets for the exact generation.
- Templates and registry source artifacts are not removed by runtime cleanup.

## 10. Implementation Boundary

This design changes the VM control plane and image-onboarding pipeline. It does not redesign the TeamLab topology model, L3 Fabric, Docker finalization path, scheduler, traffic storage, or frontend editor. Those systems consume the new VM lifecycle facts through existing module contracts.
