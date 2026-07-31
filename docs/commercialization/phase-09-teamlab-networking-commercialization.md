# Phase 9 TeamLab Networking Commercialization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 TeamLab 完成一套可供平台内部和外部系统复用的商业级组网底座，支持 Docker、Linux VM、Windows VM、混合多节点分片、显式交换机/路由器、服务动态注入、全路径流量观测和可恢复生命周期。

**Architecture:** Phase 9 在 Phase 3 topology/release/runtime API、Phase 6 队列和容量调度、Phase 7 事件与事实恢复之上增加 topology schema v2。二层网段仍单节点归属，平台托管路由器通过多 shard L3 Fabric 实现跨节点连接；镜像化网络设备保留为可攻击资产。默认流量元数据覆盖所有观察点，按需 PCAP 分段写入 S3 兼容 BlobStorage，深度观测通过可选端点传感器补充进程关联。

**Tech Stack:** .NET 10、ASP.NET Core、EF Core 10、PostgreSQL 17、Redis Stream、GZCTF.Agent、libvirt/KVM、Docker、WireGuard、Linux bridge/netns、iptables/nftables、cloud-init、qemu guest agent、PowerShell、SharpPcap、PacketDotNet、OCI Registry、S3-compatible BlobStorage、OpenTelemetry、xUnit、Testcontainers。

---

## Implementation Progress

### 2026-07-21 Independent Review Remediation

- Closed all three production-blocking review findings. Agent cleanup no longer infers shared-resource ownership from a desired-state file; Managed capabilities require a current controlled probe; runtime cleanup expires Agent and object-storage capture artifacts before finalization.
- Closed the nine P2 findings across runtime create concurrency, reservation accounting, dnsmasq facts, Docker network readiness, VM multi-NIC routing, sensor/config-drive isolation, VM import staging, PCAP monitor deletion, and current-digest scenario certification.
- Release canonical JSON now freezes image digests for ordinary Docker and VM assets. Scenario content hashes are deterministic SHA-256 values with a consistent prefix. Admin publication is serialized per topology, and validate/publish use the same Bootstrap compatibility contract.
- The default Fabric link pool moved from RFC 3927/APIPA `169.254.0.0/16` to `100.64.0.0/16`, which remains separate from the isolated guest-management subnet `100.127.0.0/16`.
- Focused review regression passed `140/140`, and the complete unit suite passed `622/622`; Release builds for `GZCTF` and `GZCTF.Agent` passed. The immutable two-Worker Docker/Linux/Windows/traffic/reset/destroy acceptance completed on 2026-07-22, so the networking foundation is now `APPROVED`; the final evidence is recorded in section 15.

### 2026-07-20 VM Image Lifecycle Simplification

- The platform no longer builds Windows or Linux images from ISO/cloud-image sources. Packer, QEMU builder recipes, builder packages, builder jobs, Worker builder capability, and their Open API routes have been removed.
- Managed images are produced by an external CI/Image Factory and imported as immutable qcow2 files through `POST /api/open/v1/images/vm-qcow2`.
- Import performs streaming SHA-256 verification, publishes the qcow2 to the internal OCI Registry, records immutable artifact provenance, and creates an `Opaque` candidate. Import never grants `Managed` capabilities.
- `controlled-probe` may certify an immutable Opaque candidate. Only a successful platform-controlled boot, Guest Supervisor lifecycle, capability probe, clean shutdown, and evidence record promotes the template to `Managed`.
- `external-evidence` remains auditable supply-chain metadata and never promotes an Opaque template to Managed.
- Docker and VM node distribution remain asynchronous and capability-scoped. Runtime deployment uses the node-local verified cache and falls back to the same immutable Registry artifact when reconciliation detects a cache miss.
- Scenario baking remains part of TeamLab publication, but it requires already certified Managed templates. Scenario artifact upload reuses the generic Agent OCI uploader and is not an image-building API.
- Migration `SimplifyVmImageLifecycle` removes the unshipped builder tables and builder-only provenance columns while preserving existing prepared artifact rows and all historical migration files.

### 2026-07-17 Independent Review Closure

- Scenario VM distribution now resolves immutable OCI provenance from `TeamLabReleaseAssetArtifact`; it no longer incorrectly requires a `VmPreparedArtifact` row.
- VM image build sources are limited to their creator or an administrator, and output image names pass the existing image resource-grant policy.
- VM source upload now uses `Idempotency-Key`, a durable `ApiOperation`, a persisted upload job, and an operation result. Replays cannot create duplicate source records or OCI identities.
- Agent VM builds and Scenario commits persist `operationId + request hash + response` receipts before responding. Same-identity replay survives Agent restart; conflicting payloads return `409`.
- If the Agent crashes after OCI publication but before receipt persistence, replay now resolves the deterministic Registry target, verifies the operation annotations and layer blob, and reconstructs the response without rerunning Packer or re-sanitizing the Scenario VM. Scenario commit also persists a sanitation checkpoint before shutdown and resumes conversion only from that checkpoint.
- The factory cutover migration now aborts when legacy preparation rows exist instead of silently deleting them. Deployment must export or precisely remove failed legacy operations first.
- Production preflight on `10.0.7.118` found two legacy artifacts and eighteen legacy jobs. One successful artifact is template `71` (`Phase9 Ubuntu 24.04 QGA Cloud 20260715 [prepared-v1-e173f799]`); it has zero Game, course, TeamLab topology/runtime, and distribution references. The remaining artifact and seventeen jobs are failed Phase 9 preparation attempts. Deployment cleanup may remove only these verified unreferenced Phase 9 records and their operation-scoped files before migration.
- Focused regression coverage for Scenario distribution, source ownership, source-upload idempotency, Agent receipt replay, and OCI recovery passed. Final local gates passed: unit `608/608`, integration `235/235`, Release solution build with zero errors, no pending EF model changes, idempotent migration SQL generation, OpenAPI comparison, and `git diff --check`.

### 2026-07-17 Golden Image and Scenario Bake Cutover

- Replaced the unshipped QGA/WSMan image-preparation factory with versioned Packer/QEMU recipes and immutable build sources. The public contract is now `vm-image-recipes`, `vm-image-sources`, and `vm-image-builds`; `/images/{id}/preparations` is removed.
- Added Linux cloud-image and Windows Server 2019/2022/2025 recipe descriptors. Release packaging pins Packer 1.15.4, QEMU plugin 1.1.6, Cloudbase-init 1.1.8, VirtIO WHQL 1.9.58, and the release-specific Guest Supervisor binaries by SHA-256.
- Build packages are uploaded once to the internal OCI Registry. Workers download source and package blobs by immutable digest into an operation-scoped workspace; no VM image build contacts the public Internet.
- Added deterministic build identity, PostgreSQL advisory locking, one build per Worker, capacity-aware deferral, competition-first scheduling, `qemu-img check`, OCI publication, and post-build certification. Failed builds are terminal and are not automatically retried.
- Added `Managed`, `Opaque`, and `Scenario` runtime modes plus `Dhcp` and `Preconfigured` network modes. Opaque VMs use host-side reachability and traffic evidence without requiring Guest Supervisor.
- Added release-time scenario baking. A publish operation creates one internal no-player runtime, reuses normal topology compilation, sharding, dependency DAG, bootstrap, health, and observation paths, commits selected VM overlays as immutable Scenario artifacts, registers inherited certification evidence, and performs exact runtime cleanup before publication completes.
- Formal runtimes resolve `BakeAtPublish` assets to Scenario templates and do not rerun their heavy bootstrap. Scenario build runtimes never allocate a public UDP mapping and cannot receive player access grants.
- Added migration `20260717133513_FinalizePhaseNineVmBuildAndScenarioArtifacts`. Existing VM templates default to `Opaque` instead of reinterpreting legacy status bytes as Managed provenance; environments containing legacy preparation rows are explicitly blocked for operator-reviewed export or precise cleanup before cutover.
- Consolidated local gate completed on 2026-07-17: the Release solution build passed; unit tests passed `604/604`; PostgreSQL/Redis integration tests passed `234/234`; EF reports no pending model changes; the idempotent migration script was generated and inspected; the OpenAPI comparator self-tests and live contract comparison passed; `git diff --check` passed.
- The integration harness now caps Testcontainers concurrency at four and applies test-only scheduling thresholds through DI. Historical migration fixtures seed their actual historical column sets instead of using the latest EF model against an older schema.
- Live two-Worker acceptance remains pending. No completion claim is made until the immutable release is deployed and Docker, Managed Linux, Managed Windows/AD, mixed routing, traffic evidence, reset, destroy, and residue checks pass on `10.0.7.118/125`.

### 2026-07-16 Acceptance Stabilization Plan Freeze

- Live edit/deploy/retry work is paused. The remaining readiness, VM performance, Agent fleet update,
  deployment, and acceptance automation work is governed by
  `docs/commercialization/phase-09-runtime-readiness-and-acceptance-stabilization.md`.
- No further server deployment or runtime retry is permitted until that plan's Large Units 1-5 and
  consolidated local gate are complete. The next live action must be one immutable scripted release
  followed by one guarded Docker/Linux/Windows/AD acceptance run.
- A focused lifecycle review found six P1 blockers not covered by the passing 53-test focused gate:
  stale cleanup can delete current generationless network resources; stale destroy can complete with
  control/database residue; stale reset has no resumable checkpoint; reconciliation can race normal
  cleanup; default Docker Entrypoint/Cmd bypasses the network gate; and Linux VM certification does
  not consistently require the QGA contract used at runtime. All six are explicit large-unit and
  acceptance requirements in the stabilization plan.

### 2026-07-16 Consolidated Quality Gate and Acceptance Restart

- The live acceptance loop was stopped after repeated parameter and generation conflicts. The remaining work now follows one consolidated gate: five parallel static reviews, one integrated fix batch, one local Release/unit gate, one deployment, and one full live acceptance run. Runtime inputs must come from published topology/profile contracts and image entrypoints; guessed overlays are prohibited.
- Confirmed blockers under repair: partial-success deployment batches can leave unrecorded workloads; VM domain/sidecar generation facts are not fully cross-checked; stale overlay-only VMs cannot advance generation; dnsmasq background failure can be reported as success; desired-state replay can ignore data-plane drift; multi-network entry shards select the access network incorrectly; topology TCP/HTTP health intents are not executed; long native names can collide after truncation; return-path observation points are globally deduplicated; unresolved observations can be skipped permanently; observation spool deletion races in-flight writes; Windows routes are not persistent across the AD reboot boundary.
- Control-plane correction in progress: TeamLab reset/destroy tickets execute on the platform control plane and no longer require an arbitrary healthy WorkerNode merely to enter cleanup/replanning. Deployment failure, reset cleanup, and destroy consume the abandoned generation's protected runtime overlay.
- Native resource naming now uses a bounded readable prefix plus a stable hash when the Linux 15-character limit is exceeded. The same full identity produces the same name, while long shared prefixes do not collide.
- The final live gate explicitly includes Docker, Linux VM, Windows VM, a simple AD domain profile, mixed RFC1918 networks, two physical Workers, directional reachability, DNS/DHCP, WireGuard, ordered `A -> B -> C -> B -> A` evidence, runtime PCAP, reset, destroy, and residue checks. No Phase 9 completion claim is permitted before these checks and test-resource cleanup are recorded.
- Consolidated local gate completed after the repair batch: the full Release solution build passed with zero warnings and zero errors; the Runtime/TeamLab/VM/Registry/traffic unit selection passed `171/171`; Phase 9 PostgreSQL/traffic/capture/bootstrap integration passed `6/6`; the audit-detail and TeamLab foundation boundary follow-up passed `13/13`.
- The only integration defect found by the consolidated gate was a missing Phase 7 event-detail allowlist entry for `processCorrelatedCount`. The event taxonomy and writer contract now agree, and the complete selected integration gate passes.

### 2026-07-15 Live Two-Worker Acceptance Checkpoint

- Acceptance hosts are `10.0.7.118` (platform plus WorkerNode) and `10.0.7.125` (WorkerNode). Both Agents report Docker, KVM, packet observation, and endpoint-sensor capabilities.
- A persistent infrastructure WireGuard link named `gzctf-fabric` was established with `10.250.0.1/24` on 118 and `10.250.0.2/24` on 125. Bidirectional ICMP succeeded and `wg show` reported a current handshake and transferred bytes.
- Release `019f65ca-0412-78da-bbed-20381f7af3ea` produced two shards, one cross-shard connection, and runtime CIDRs `10.62.0.0/24`, `172.23.0.0/24`, and `192.168.0.0/24`. After the infrastructure link was present, both Workers accepted the desired state and installed local `/30` uplinks plus remote routes through the real node Fabric addresses. The former `Nexthop has invalid gateway` failure did not recur.
- Live acceptance exposed and fixed a VM endpoint-sensor defect: VM registration did not create `/run/gzctf-sensor` before `virt-install` bound the virtio-serial Unix socket. The directory and stale socket are now prepared before both Docker and VM registration. A Linux VM subsequently reached `running`, created its sensor socket, and passed the QGA wait boundary.
- TeamLab `CpuUnits` now follows the platform resource convention of ten units per CPU when converted to VM vCPU count. The previous direct mapping could request 100 vCPUs for a 100-unit topology asset.
- Node Fabric health is no longer a manually asserted address. Agent status and heartbeat report the configured Fabric interface, actual IPv4 address, and readiness. Node enablement verifies that the requested tunnel IP is owned by the live Fabric interface; routing and scheduler eligibility require the reported `TeamLabFabricIp` instead of falling back to an unverified value.
- Runtime lifecycle submissions now serialize active operations per runtime with a PostgreSQL advisory transaction lock and reject overlapping deploy/reset/destroy mutations with `runtime_operation_in_progress`. This closes the acceptance-observed race where a destroy request removed bridges while a deployment retry was attaching containers.
- Concentrated local gate after these corrections: node management, runtime scheduling, VM network, and node model tests passed `54/54`; `GZCTF` and `GZCTF.Agent` Release builds completed with zero warnings and zero errors.
- Remaining live gate: create a fresh runtime without an overlapping destroy operation, verify Docker plus Linux VM service readiness, directional reachability, DNS, flow/path evidence, PCAP, reset, destroy, and residue cleanup. The workstation route to `10.0.7.125` became unavailable before this clean rerun could be observed, so no completion claim is recorded yet.

### 2026-07-14 Planning Baseline

- Branch: `codex/phase-09-teamlab-networking`.
- Code baseline: `58b3e7ecfe0081d97be4ad7761516f7cfbe96522`.
- Phase 8 is intentionally skipped. Phase 9 may implement the backend VM guest-control capabilities it directly requires, but it does not implement Phase 8 frontend access work.
- Current connected topology placement is incorrect for multi-node use because `TeamLabAssetPlanner.BuildGroups` unions every network attached to a routing asset.
- Current infrastructure is not first class: `TeamLabAssetKind` only supports Docker and VM, and switches are implicit bridges.
- Current Windows TeamLab VM path does not generate initialization data; the Agent does not create qemu guest agent or sensor channels.
- Current metadata collection creates one tcpdump text process per network; current PCAP is single-node and stored under `/run`.
- The approved design is recorded in `docs/superpowers/specs/2026-07-14-phase-09-teamlab-networking-commercialization-design.md`.
- Development uses large-unit gates. Unit tests run after a coherent subsystem is complete; the final branch receives one independent quality review and one consolidated verification pass.

### 2026-07-14 Large Unit 1: Topology v2 Foundation

- Added topology schema v2 contracts for managed switches, managed routers, directional connections, explicit dependencies, bootstrap references, stateless assets, endpoint observation, and runtime observation policy.
- Added one `TeamLabExecutionTopology` consumed by planning, logical runtime allocation, shard deployment, route application, orchestration, and image reference discovery. Schema branching is confined to `TeamLabReleaseCodec`, `TeamLabTopologyV1Normalizer`, and `TeamLabTopologyV2Compiler`.
- Preserved schema v1 canonical release shape and hash semantics. V1 releases normalize to implicit managed switches, bidirectional connections, and compatibility dependencies at the decode boundary; immutable release JSON is not rewritten.
- Corrected placement grouping semantics: managed routers are infrastructure and do not merge network placement groups; only image-backed multi-interface appliances pin their networks to one WorkerNode.
- Added structural and dependency-graph validators for managed infrastructure, cross-node key uniqueness, directional router references, interface ownership, bootstrap references, and dependency cycles.
- Moved TeamLab runtime, shard, network, asset, access, event, flow, and capture entities out of `Models/Data/TeamLabEntities.cs` into focused files under `Modules/TeamLab/Domain/Runtime` without changing table names or database keys.
- Added migration `ExpandPhaseNineTeamLabNetworking` for v2 draft metadata. Existing connection rows are backfilled as `Bidirectional`; JSON columns use valid PostgreSQL `jsonb` defaults.
- Verification evidence: `GZCTF` and `GZCTF.Test` Release builds pass with zero warnings; the concentrated TeamLab topology gate passes 12/12 tests; `dotnet ef migrations has-pending-model-changes` reports no pending changes.
- Environment limitation: migration integration tests could not start because local Docker could not pull `docker.io/testcontainers/ryuk:0.14.0`. Five attempted migration cases failed at the identical Testcontainers bootstrap step before application migration code ran. This gate remains scheduled for the consolidated container-enabled verification and `10.24.0.118` acceptance.

### 2026-07-14 Large Unit 2: Runtime Facts, Placement, and Fabric Leases

- Added persisted runtime facts for managed infrastructure, infrastructure fragments, dependency state, bootstrap execution, observation points, and per-shard Fabric link leases.
- Logical planning now records implicit/explicit switches, managed routers, directional connection summaries, and dependency state from the unified execution topology.
- Physical placement remains under the Phase 6 distributed scheduler lease and database transaction. It evaluates Docker and KVM requirements per placement group, reserves all selected nodes atomically, and keeps Docker-only shards independent from KVM capability.
- Placement ordering now includes directed cross-group connection cost before node reuse and Phase 6 node score. A deterministic improvement pass only moves groups when total cross-node edge cost is reduced without exceeding capacity or feature requirements.
- Managed switches create one fragment on their network shard. Managed routers create one fragment per participating shard without merging those networks into one placement group.
- Removed modulo-derived Fabric addresses from runtime routing. `TeamLabFabricLinkAllocator` now allocates persisted non-overlapping `/30` leases from `TeamLabNetworkConfig.FabricLinkPool`; route application consumes the stored hub/node addresses and cleanup releases the exact generation leases.
- Added database exclusion constraint `EX_TeamLabFabricLinkLeases_ActiveCidr` and a Phase 9 backfill migration. Active schema v1 runtimes receive equivalent implicit switch, fragment, Fabric lease, network observation, and Fabric observation facts without changing release ID, canonical JSON, or content hash.
- Plan projection now reports infrastructure keys per shard, managed infrastructure count, bootstrap artifact count, and observation-point estimate without exposing WorkerNode identity.
- Verification evidence: concentrated placement/route/Fabric gate passes 11/11 tests; the Phase 9 PostgreSQL migration/backfill integration test passes with Testcontainers Ryuk disabled; the existing TeamLab migration-to-latest test passes; EF reports no pending model changes; Release builds remain zero-warning.

### 2026-07-14 Large Unit 3: Idempotent Agent Infrastructure Contract

- Replaced the independent shard-network and route application calls with one `ApplyInfrastructureAsync` desired-state port. The application sends managed switches, router fragments, leased Fabric addresses, directional policies, observation points, stable native names, runtime generation, and route version in one request.
- Removed the Agent HTTP endpoints and main-service client methods for independent bridge, router, DHCP/DNS, DHCP probe, and Fabric mutation. `POST /api/teamlab/shards/apply` is now the only managed-infrastructure mutation endpoint; WireGuard access and asset attachment remain separate lifecycle operations.
- Split Agent execution into `TeamLabBridgeService`, `TeamLabRouterService`, `TeamLabFabricService`, `TeamLabFirewallService`, and a shared `TeamLabCommandExecutor`. Bridge reconciliation is non-destructive when the bridge already exists; router and Fabric execution remain owned by focused services.
- Added stable feature IDs `teamlab.infrastructure.v2` and `teamlab.fabric.leased-links.v1`. Agent manifests report them only when the implemented TeamLab network toolchain is available, and scheduling requires the exact feature set instead of a numeric protocol-version comparison or the legacy Fabric feature.
- Added runtime-generation firewall chains. Directional policies include an explicit `ESTABLISHED,RELATED` return rule and a deny-by-default terminal rule; nftables is preferred when available and iptables remains the fallback. Cleanup removes only the requested generation's policy chains.
- Added compact Agent desired-state persistence at `/run/gzctf-teamlab/runtime-{id}/generation-{generation}/state.json`. Repeated apply with the same normalized digest returns `AlreadyApplied=true` without executing commands. DHCP/DNS state and cleanup are scoped under the same generation directory.
- Added native resource facts to the apply response and persisted native identities onto runtime infrastructure fragments. Stable Linux resource naming is centralized in `TeamLabResourceNameFactory`; application services no longer invent Agent interface names independently.
- Separated WireGuard access revocation from shard cleanup so revoking a player grant cannot destroy runtime infrastructure. Full shard cleanup carries an explicit router namespace and generation.
- Verification evidence: the concentrated Agent network gate passes 42/42 tests, including stable digest, no-op reapply, generation cleanup, directional return traffic, route intent, and capability contract coverage. `GZCTF`, `GZCTF.Agent`, and `GZCTF.Test` Release builds complete with zero warnings.

### 2026-07-14 Large Unit 4: Bootstrap Profile Control Plane

- Added durable Bootstrap Profile lifecycle, immutable versions, OCI artifact storage, Phase 1-compliant asynchronous operations, cursor pagination, idempotency, scopes, audit integration, and capability-aware node pre-distribution.
- Added template capability certification facts bound to `ImageTemplateId + ImageHash + EvidenceDigest`; an image hash change invalidates older certification evidence for release compatibility checks.
- Added release-time compatibility validation for operating system, Docker/VM asset kind, certified template capabilities, parameter types, required values, and secret-overlay boundaries. Topology JSON cannot persist secret parameter values.
- Added official Linux service, Windows PowerShell service, endpoint sensor, and Windows AD manifests under `scenarios/bootstrap-profiles/`. The cross-platform execution identity is the platform abstraction `system`; Agent execution maps it to root or LocalSystem instead of exposing OS-specific identities in the content contract.
- Extracted `OciArtifactRegistryClient` and reused it for VM images and bootstrap artifacts. Existing-object checks verify layer digest and size, while Agent downloads verify digest, support resume, and use atomic replacement.
- Verification evidence: the concentrated content unit gate passes 11/11 tests; the PostgreSQL/OpenAPI integration gate passes 13/13 tests; `GZCTF`, `GZCTF.Agent`, and `GZCTF.Test` Release builds complete with zero warnings; EF reports no pending model changes.
- The certification source is explicit. `external-evidence` stores a caller-supplied SHA-256 evidence digest; `controlled-probe` selects an eligible KVM node, prepares the immutable image, starts a temporary domain, verifies requested capabilities through QGA, records generated evidence, and destroys the exact temporary generation. A caller cannot attach external evidence to a controlled probe.

### 2026-07-14 Large Unit 5: VM Guest Control and Cross-platform Injection

- Added `VmDomainBuilder` with deterministic runtime-generation UUIDs, stable MAC-preserving network arguments, the standard `org.qemu.guest_agent.0` channel, and the optional `org.gzctf.sensor.0` channel. `KvmService` now delegates domain construction instead of accumulating more command assembly logic.
- Added typed `VmGuestAgentService` operations for readiness, bounded file read/write, command execution, captured-output limits, reboot disconnect/reconnect detection, and generation-marker verification. QGA JSON is serialized and passed as a process argument to `virsh`; it is never assembled as shell JSON.
- Added `VmBootstrapService` for digest-verified and path-safe `tar.gz` extraction, bounded expansion, parameter/secret boundary validation, template rendering, Linux root and Windows LocalSystem execution, Windows static networking by MAC, reboot checkpoints, TCP/HTTP/entrypoint health checks, and protected runtime files.
- TeamLab VM creation now has explicit image-ready, domain-create, guest-control/bootstrap, IP-ready, and cleanup boundaries. Windows, profiled VMs, and endpoint-observed VMs require QGA; a bootstrap failure destroys the exact VM generation. Remote TeamLab VM requests no longer pass the main server's local template path to a WorkerNode.
- Linux profiles use NoCloud for deterministic initial hostname/networking and QGA for profile execution. Windows no longer emits a disabled cloud-init placeholder; it uses the certified QGA + PowerShell path and applies IP/DNS/routes by MAC after the initial dnsmasq reservation.
- Added controlled image certification for QGA, virtio-serial, Linux NoCloud marker, Windows PowerShell, e1000e/virtio boot, and firstboot write/execute behavior. Cloudbase-init certification fails closed until a real config-drive probe is implemented rather than accepting a guessed result.
- Added official runnable Linux HTTP systemd, Windows PowerShell HTTP, and Windows AD role/domain/health package sources. Endpoint sensor packaging remains linked to the compiled sensor delivered by Large Unit 7.
- Hardened failed-domain cleanup: partial overlays, seed media, generation files, bootstrap staging, libvirt definitions, and RDP proxy state are removed under the VM identity lock.
- Verification evidence: focused Bootstrap/Certification/TeamLab VM/QGA gate passes 28/28 tests; PostgreSQL migration, backfill, Bootstrap migration, and OpenAPI integration gate passes 14/14 tests; reconstructed backfill preserves the v1 canonical release and produces the expected `169.254.21.0/30` legacy-equivalent lease; EF reports no pending model changes under Release configuration; Release builds remain zero-warning.
- Remaining acceptance boundary: live Linux and Windows QGA/bootstrap/reboot behavior is scheduled for the consolidated deployment on `10.24.0.118`; the local gate verifies contracts, migrations, command construction, security boundaries, and orchestration wiring without pretending to be a guest OS runtime test.

### 2026-07-14 Large Unit 6: Dependency DAG Execution and Guarded Recovery

- Replaced display-order workload execution with a compiled deployment DAG. Every asset now has explicit create, bootstrap, and health nodes; `GuestReady`, `BootstrapCompleted`, and `ServiceReady` dependencies unlock only the exact downstream create node they target, while independent ready nodes execute concurrently under a runtime-level concurrency bound.
- Persisted asset execution stages, immutable image/bootstrap digests, stateless intent, and execution timestamps. Recovery reconstructs the completed DAG node set only from durable stages plus native runtime identity, so a process restart resumes after the last proven boundary rather than recreating an already-running asset.
- Added stable queue stages for artifact verification, network/route application, asset boot, guest readiness, bootstrap injection/execution/reboot, health probing, and observation startup. Mixed ready batches retain concurrent execution and expose an aggregate stage message instead of serializing different node kinds solely for presentation.
- Added guest-local, digest-bound VM bootstrap step checkpoints. A checkpoint is accepted only when runtime, generation, asset, profile version, artifact digest, and step identity all match. Reboot-required steps checkpoint before and after reboot, so AD promotion and similar non-idempotent steps resume without blind re-execution after an Agent or platform interruption.
- Added guarded recovery policy. Automatic workload rebuild remains disabled by default; stateful assets are always denied. Stateless rebuild additionally requires an online missing-resource proof, immutable image/profile inputs, the current generation, elapsed recovery grace, and an active capacity reservation.
- Infrastructure replay requires current-generation route versions and desired-state digests, inventory-proven drift, and intact WireGuard/access identities. Reconciliation no longer marks a runtime Running merely because native containers or VMs exist; every asset must reach `ServiceReady` and infrastructure desired state must match.
- Initial deployment failure stops dependent scheduling and invokes generation-scoped cleanup through the runtime orchestrator. Failed cleanup remains durably owned as `CleanupPending` instead of releasing capacity as though cleanup had completed.
- Verification evidence: concentrated DAG/recovery/guest-control gate passes 9/9 tests; `GZCTF`, `GZCTF.Agent`, and `GZCTF.Test` Release compilation remains zero-warning; `dotnet ef migrations has-pending-model-changes` reports no pending model changes under Release configuration.

### 2026-07-14 Large Unit 7: Agent Packet Observer and Endpoint Sensor

- Replaced one-tcpdump-process-per-network metadata collection with one Agent in-process observer that opens only persisted managed interfaces. Infrastructure apply resolves network bridges, router-fragment host veths, Fabric uplinks, and enabled workload endpoint veths into a generation-scoped observation registry; Agent restart restores the registry from desired-state files.
- Added bounded IPv4 Ethernet/Linux-SLL parsing for ARP, TCP, UDP, and ICMP. Packet fingerprints include stable packet identity and payload evidence while excluding forwarding-mutated TTL and IP/TCP/UDP checksums; flow fingerprints remain directional. The observer uses a deterministic bounded flow accumulator and retries interfaces that do not exist until later asset attachment.
- Added a bounded memory plus disk observation spool under `/var/lib/gzctf/observations`. Writes are batched, files compact to the retained window, sequence cursors are monotonic per runtime generation, parser/drop/spool health is exposed, and cleanup tombstones prevent queued callbacks from recreating a destroyed generation's spool.
- Replaced Agent `flows/start`, `flows/stop`, and `flows/snapshot` with `observations/read` batch cursor semantics. Main-service compatibility flow aggregation now reads the network bridge observation point rather than starting or parsing an external tcpdump text process.
- Added the standalone `GZCTF.EndpointSensor` project. Linux maps `/proc/net/tcp|udp` socket inodes to bounded process snapshots; Windows uses native extended TCP/UDP owner-PID tables. Both emit observed/opened/closed facts without command lines, file content, payload, credentials, or keystrokes.
- Added HMAC-authenticated, runtime/generation/asset-bound newline-delimited sensor channels. VM channels use a deterministic `org.gzctf.sensor.0` virtio-serial Unix socket; Docker channels use an Agent-created Unix socket mount. Invalid identity, signature, timestamp, sequence replay, oversize payload, and unknown registration are rejected without logging raw events.
- Sensor credentials are generated by the platform inside the protected runtime overlay under a reserved key. Users cannot supply or override `GZCTF_SENSOR_*` values. Endpoint observation mode is persisted on the runtime asset, participates in placement capability checks, and is removed with generation cleanup.
- Added Linux and Windows self-contained sensor artifacts to platform publish output, node registration, and Agent self-sync. Agent capability manifests advertise packet observation only when libpcap is loadable and advertise endpoint sensor support only when both managed artifacts are present.
- Extended Agent runtime inventory with managed TeamLab infrastructure, observation-point, and sensor-channel facts. Optional endpoint observation may fail without blocking asset creation; required endpoint observation fails closed before workload creation.
- Verification evidence: concentrated observation/fingerprint/sensor gate passes 6/6 tests; `GZCTF`, `GZCTF.Agent`, `GZCTF.EndpointSensor`, and `GZCTF.Test` Release builds complete with zero warnings; source scan finds no old flow process endpoint, pid/log parser, or active `teamlab.flow.v1` code; EF reports no pending model changes.
- Unit boundary: this unit delivers the observer, sensor binary, secure channel, distribution, and runtime intent. Starting the managed sensor process inside Docker/Linux VM/Windows VM and persisting packet/process evidence into the Phase 8 traffic model are completed in Large Unit 8 so service injection and evidence persistence share one deployment contract.

### 2026-07-15 Large Unit 8: Traffic Persistence and Path Correlation

- Replaced per-network observation cursors with one persisted cursor per runtime generation and WorkerNode. Each Agent batch now carries all network bridge, router fragment, Fabric uplink, and endpoint evidence from that node, preventing one observation-point filter from advancing past records owned by another point.
- Added schema-v2 traffic envelopes and a bounded Redis-to-PostgreSQL pipeline. Binary COPY writes structured packet/process observations idempotently by runtime, generation, observation point, and source sequence; network-bridge packet evidence continues to feed the existing flow query contract without restoring the retired tcpdump text collector.
- Added persisted observation health. Node sequence, Agent drop count, local fallback drops, and the latest observer error update the node cursor and all enabled observation points in one database save after successful enqueue.
- Added idempotent path derivation with a persisted correlation cursor. Matching packet fingerprints across ordered observation points produce `PacketExact`; signed endpoint snapshots with the same process identity and different flows produce only `TemporallyRelated`. Current socket-table evidence never claims `ProcessCorrelated` causality.
- Added retention-safe path-hop snapshots. Raw packet/process observations expire after 7 days, while derived paths and compatibility flows default to 30 days; deleting raw evidence nulls the optional source observation link without deleting the bounded hop projection.
- Added public opaque-cursor path APIs under the existing `TeamLabTrafficRead` permission. Responses expose public shard/network/infrastructure/asset identity only and do not expose WorkerNode IDs, host interfaces, packet payload, process names, commands, files, or credentials.
- Completed endpoint sensor execution. Docker sensors run as Agent-managed host binaries inside the target container network namespace without modifying the image entrypoint. Linux VMs receive a QGA-injected systemd service; Windows VMs receive a QGA-injected protected startup task. Required mode rolls back asset creation on failure, while optional mode preserves the workload and records a bounded degradation reason.
- Added migration `20260714180834_AddTeamLabTrafficEvidencePersistence` for observation cursors, raw observations, correlation cursors, paths, and retention-safe hops.
- Verification evidence: Release builds for `GZCTF`, `GZCTF.Agent`, `GZCTF.EndpointSensor`, `GZCTF.Test`, and `GZCTF.Integration.Test` complete with zero warnings; concentrated unit gate passes 13/13; PostgreSQL/Redis integration gate passes 3/3; duplicate COPY ingestion and path replay are idempotent; raw observation deletion preserves path snapshots; EF reports no pending model changes.

### 2026-07-15 Large Unit 9: Multi-node PCAP and Object Storage

- Replaced the single-node capture job with a logical capture aggregate and one persisted segment per selected observation point. `runtime`, `network`, `path:{id}`, and `asset:{key}` scopes compile to deterministic point sets without exposing WorkerNode identity or host interfaces through public projections.
- Added `TeamLabPcapService` on the Agent. Segment state is stored under `/var/lib/gzctf/captures/runtime-{id}/generation-{generation}/capture-{id}/segment-{id}`, process ownership includes PID start ticks, Agent restart can restore state, generation cleanup terminates owned capture processes, and verified upload or explicit expiry removes local packet files.
- Segment startup, status, stop, upload, and delete now use generation/capture/segment identities. Different WorkerNodes execute in parallel while each node processes its own segment sequence, and metadata observation remains independent of on-demand PCAP.
- Added time-limited Data Protection upload grants bound to capture, segment, WorkerNode, expected size, maximum size, and SHA-256. The anonymous internal upload route accepts only the bound Bearer grant plus node/digest headers, streams through `IBlobStorage`, deletes digest/size mismatches, and treats a verified duplicate as idempotent success.
- Added `TeamLabCaptureCoordinatorWorker` for status polling, capture finalization, authenticated Agent upload, persisted-state confirmation, bounded retry, aggregate status, and expiry. Failed logical captures may still upload already captured segments while preserving the failed aggregate result.
- Runtime and administrator downloads now stream an uncompressed tar from BlobStorage with a camelCase `manifest.json` and every available verified segment. PCAP data is never assembled in main-service memory and Agent file-download endpoints were removed.
- Added capture retention to the Phase 4 governance catalog. Explicit capture expiry and governance reconciliation delete object-storage artifacts, mark segments/jobs expired, and preserve uploaded objects through runtime destroy until the configured capture expiry.
- Runtime cleanup now loads segment facts, removes Agent generation capture state, marks non-uploaded active segments failed, and leaves already uploaded audit evidence under retention control.
- Verification evidence: `GZCTF`, `GZCTF.Agent`, `GZCTF.Test`, and `GZCTF.Integration.Test` Release builds complete with zero warnings; concentrated capture unit gate passes 4/4; PostgreSQL/object-storage expiry integration passes 1/1. The final Phase 9 contract migration is intentionally completed in Large Unit 10 so legacy single-file capture rows are backfilled before old columns are removed.

## 0. Phase Boundary

### 0.1 Must Complete

- topology schema v2 with managed switches, managed routers, directional network connections, dependency DAG, bootstrap profile references, and observation policy;
- one internal topology execution model shared by v1 release normalization and v2 release decoding;
- true connected multi-node placement where managed-router connections do not collapse all networks into one shard;
- persisted non-overlapping Fabric link leases;
- Docker, Linux VM, Windows VM, and mixed shard deployment;
- qemu guest agent guest-control path and Windows PowerShell bootstrap;
- reusable, versioned, digest-pinned OCI bootstrap profiles with capability certification and node pre-distribution;
- parallel DAG execution, reboot checkpoints, health gates, and guarded recovery;
- explicit observation-point facts and Agent in-process metadata collector;
- packet hop reconstruction and optional endpoint process correlation;
- multi-node capture segments persisted to S3-compatible BlobStorage;
- Phase 3 public API, Phase 6 scheduling, Phase 7 audit/recovery, and Agent capability protocol compliance;
- concentrated code gates, one final review agent, and real deployment acceptance on `10.24.0.118`.

### 0.2 Explicitly Out of Scope

- frontend pages, topology editor UX, and frontend design-language changes;
- IPv6;
- VXLAN, EVPN, stretched L2, or cross-node broadcast domains;
- port-level or protocol-level ACL;
- transparent proxying or forcing player traffic through the public/main server;
- automatic rebuild of stateful assets;
- hard-coded AD logic inside TeamLab core;
- permanent full-PCAP capture for every runtime;
- changes to Phase 3 public runtime identity, Phase 1 operation identity, or Phase 6 queue identity.

## 1. Frozen Public and Internal Contracts

### 1.1 Topology v2 contracts

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTopologyV2Contracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabInfrastructurePrimitives.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTopologyContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopology.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopologyRelease.cs`
- Split and delete after reference migration: `src/GZCTF/Models/Data/TeamLabEntities.cs`

Before adding new runtime facts, move the existing TeamLab runtime, shard, network, asset, access-grant, event, flow, and capture entities into focused files under `src/GZCTF/Modules/TeamLab/Domain/Runtime/`. Keep table names and keys unchanged, update `AppDbContext` and all callers in one commit, then delete `Models/Data/TeamLabEntities.cs`. No `GZCTF.Models.Data.TeamLab*` type remains after this unit.

The v2 definition uses separate infrastructure and workload collections:

```csharp
public enum TeamLabInfrastructureKind : byte
{
    ManagedSwitch = 0,
    ManagedRouter = 1
}

public enum TeamLabConnectionDirection : byte
{
    FromTo = 0,
    Bidirectional = 1
}

public enum TeamLabDependencyCondition : byte
{
    NetworkReady = 0,
    GuestReady = 1,
    ServiceReady = 2,
    BootstrapCompleted = 3
}

public sealed record TeamLabTopologyInfrastructureModel(
    string Key,
    string Name,
    TeamLabInfrastructureKind Kind,
    IReadOnlyList<TeamLabTopologyInterfaceModel> Interfaces,
    string? NetworkKey);

public sealed record TeamLabTopologyConnectionV2Model(
    string Key,
    string FromNetworkKey,
    string ToNetworkKey,
    string ViaNodeKey,
    TeamLabConnectionDirection Direction);

public sealed record TeamLabTopologyDependencyModel(
    string AssetKey,
    string DependsOnKey,
    TeamLabDependencyCondition Condition);

public sealed record TeamLabBootstrapReferenceModel(
    Guid ProfileId,
    int Version,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record TeamLabTopologyDefinitionV2Model(
    string Name,
    IReadOnlyList<TeamLabTopologyNetworkModel> Networks,
    IReadOnlyList<TeamLabTopologyInfrastructureModel> Infrastructure,
    IReadOnlyList<TeamLabTopologyAssetV2Model> Assets,
    IReadOnlyList<TeamLabTopologyConnectionV2Model> Connections,
    IReadOnlyList<TeamLabTopologyDependencyModel> Dependencies,
    TeamLabObservationPolicyModel Observation);
```

`TeamLabTopologyAssetV2Model` retains Docker/VM image reference and resources, and adds:

```csharp
bool Stateless;
TeamLabBootstrapReferenceModel? Bootstrap;
TeamLabEndpointObservationMode EndpointObservation;
```

`OrderIndex` remains display metadata only and is excluded from runtime dependency semantics.

### 1.2 Unified internal plan

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyExecutionModel.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyV1Normalizer.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyV2Compiler.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseCodec.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs`

Both schema versions produce this internal shape:

```csharp
public sealed record TeamLabExecutionTopology(
    int SchemaVersion,
    IReadOnlyList<TeamLabExecutionNetwork> Networks,
    IReadOnlyList<TeamLabExecutionInfrastructure> Infrastructure,
    IReadOnlyList<TeamLabExecutionAsset> Assets,
    IReadOnlyList<TeamLabExecutionConnection> Connections,
    IReadOnlyList<TeamLabExecutionDependency> Dependencies,
    TeamLabExecutionObservationPolicy Observation);
```

Only codec/normalizer code can branch on topology schema version. Planner, scheduler, deployment, recovery, and traffic code consume `TeamLabExecutionTopology` and contain no v1/v2 branches.

### 1.3 Validation rules

**Files:**

- Split: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyValidator.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabTopologyStructureValidator.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabDependencyGraphValidator.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabBootstrapCompatibilityValidator.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabReachabilityCompiler.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabTopologyV2Tests.cs`

Validation must reject:

- missing or duplicate node keys;
- managed switches that do not own exactly one network;
- managed routers with fewer than two unique network interfaces;
- connections whose router/appliance does not attach to both networks;
- custom multi-interface appliances whose placement group exceeds every eligible node;
- dependency cycles or dependencies on unknown keys;
- bootstrap parameters outside the profile schema;
- template/profile capability mismatch;
- endpoint telemetry marked required when the template lacks the sensor channel capability;
- address-pool overlap and reserved host offsets;
- topology counts above 32 networks, 128 workload assets, or 8 interfaces per asset.

### 1.4 Large-unit gate

- [x] Implement schema v2 contracts, canonical codec, v1 normalizer, execution model, validators, and release capability advertisement.
- [x] Generate an expand migration for topology v2 draft/release metadata without rewriting immutable v1 release JSON or hashes.
- [x] Run the concentrated topology gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabTopology|FullyQualifiedName~TeamLabFoundation"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~Migration"
```

Expected: v1 canonical tests remain stable; v2 validation/codec/migration tests pass; no execution service branches on schema version.

## 2. Runtime Facts, Placement, and Fabric Leases

### 2.1 Persistence model

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeInfrastructure.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeBootstrap.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeObservation.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabInfrastructureEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabBootstrapEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabObservationEntityConfigurations.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRuntimeEntityConfigurations.cs`

Add current-generation entities:

```csharp
TeamLabRuntimeInfrastructure
TeamLabRuntimeInfrastructureFragment
TeamLabFabricLinkLease
TeamLabRuntimeDependencyState
TeamLabBootstrapExecution
TeamLabObservationPoint
```

Required uniqueness:

```text
RuntimeInfrastructure: RuntimeId + Generation + TopologyKey
InfrastructureFragment: InfrastructureId + ShardId
FabricLinkLease: active AllocatedCidr exclusion constraint
DependencyState: RuntimeId + Generation + AssetKey + DependsOnKey + Condition
BootstrapExecution: RuntimeId + Generation + AssetId + ProfileVersionId + StepKey + Attempt
ObservationPoint: RuntimeId + Generation + WorkerNodeId + InterfaceToken + Kind
```

### 2.2 Correct placement grouping

**Files:**

- Replace internals: `src/GZCTF/Modules/TeamLab/Application/TeamLabAssetPlanner.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimePlanner.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/TeamLabPhysicalPlacementService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabPlanContracts.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabConnectedShardPlacementTests.cs`
- Test: `src/GZCTF.Test/UnitTests/Runtime/TeamLabPhysicalPlacementTests.cs`

The grouping algorithm is fixed as:

```csharp
var groups = networks.ToDisjointSets();
foreach (var appliance in assets.Where(asset => asset.IsImageBacked && asset.Interfaces.Count > 1))
    groups.Union(appliance.Interfaces.Select(item => item.NetworkKey));
// Managed routers never union their attached network groups.
```

Placement score order is deterministic:

1. required Docker/KVM/Fabric features and capacity;
2. single-node plan when it fits;
3. minimum cross-node directed connections;
4. reuse a node already selected for another group when capacity remains;
5. Phase 6 node score;
6. node name and UUID tie-breakers.

The plan response includes managed infrastructure count, cross-shard route count, required feature set, bootstrap artifact count, and observation-point estimate without exposing WorkerNode identity.

### 2.3 Persisted Fabric link allocation

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabFabricLinkAllocator.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRouteApplicationService.cs`
- Delete method: `TeamLabRouteApplicationService.FabricAddress`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabFabricLinkAllocatorTests.cs`
- Integration test: `src/GZCTF.Integration.Test/TeamLabFabricLeaseTests.cs`

`TeamLabFabricLinkAllocator` allocates /30 links from configured `TeamLab:FabricLinkPool`, default `100.64.0.0/16`, inside the runtime planning transaction. This avoids RFC 3927 link-local/APIPA semantics and does not overlap the isolated guest-management subnet `100.127.0.0/16`. PostgreSQL `cidr` plus an active GiST exclusion constraint prevents overlap. Reset creates new-generation leases only after old generation cleanup; destroy releases leases after Agent facts confirm cleanup.

### 2.4 Atomic reservation and rollback

`TeamLabPhysicalPlacementService` must reserve all selected nodes in one scheduler lease and database transaction. If any node becomes ineligible before commit, no shard assignment or reservation is committed. Recovered existing assignments are revalidated per shard feature requirements, so a Docker-only shard does not require KVM.

### 2.5 Large-unit gate

- [x] Implement runtime infrastructure/dependency/observation facts, corrected connected placement, and persisted Fabric link leases.
- [x] Add expand/backfill tests proving active v1 runtimes receive equivalent implicit switch/router facts without changing release identity.
- [x] Run the concentrated placement gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabConnectedShardPlacement|FullyQualifiedName~TeamLabPhysicalPlacement|FullyQualifiedName~TeamLabFabricLink"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabFabricLease"
```

Expected: a connected three-network topology splits across eligible Docker and KVM nodes; managed routers do not pin networks; custom multi-NIC appliances do; link leases never overlap.

## 3. Managed Infrastructure Agent Contract

### 3.1 Agent feature revisions

**Files:**

- Modify: `src/GZCTF.Agent/Services/AgentCapabilityService.cs`
- Modify: `src/GZCTF/Modules/Runtime/Contracts/AgentCapabilityContracts.cs`
- Modify: `docs/commercialization/agent-capability-protocol.md`
- Test: `src/GZCTF.Test/UnitTests/Fleet/AgentCapabilityProtocolTests.cs`

Add stable features:

```text
teamlab.infrastructure.v2
teamlab.fabric.leased-links.v1
runtime.vm.qga.v1
runtime.vm.windows-bootstrap.v1
teamlab.observation.v2
teamlab.endpoint-sensor.v1
teamlab.pcap-object-storage.v1
```

Each workload declares an exact feature subset. No `protocolVersion >= N` or global `TeamLabAvailable` branch is allowed.

### 3.2 Split network execution responsibilities

**Files:**

- Refactor: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
- Create: `src/GZCTF.Agent/Services/TeamLab/TeamLabBridgeService.cs`
- Create: `src/GZCTF.Agent/Services/TeamLab/TeamLabRouterService.cs`
- Create: `src/GZCTF.Agent/Services/TeamLab/TeamLabFabricService.cs`
- Create: `src/GZCTF.Agent/Services/TeamLab/TeamLabFirewallService.cs`
- Keep shared runner: `src/GZCTF.Agent/Services/TeamLabCommandRunner.cs`
- Modify: `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- Modify: `src/GZCTF.Agent/Models/TeamLabModels.cs`

The Agent receives a desired-state request rather than individual shell-oriented bridge/router calls:

```csharp
public sealed record TeamLabShardDesiredStateRequest(
    int RuntimeId,
    int Generation,
    int RouteVersion,
    IReadOnlyList<TeamLabManagedSwitchIntent> Switches,
    IReadOnlyList<TeamLabManagedRouterFragmentIntent> Routers,
    TeamLabFabricUplinkIntent Fabric,
    IReadOnlyList<TeamLabObservationPointIntent> ObservationPoints);
```

Apply is idempotent by `RuntimeId + Generation + RouteVersion`. The Agent writes a compact desired-state digest under `/run/gzctf-teamlab/runtime-{id}/generation-{generation}/state.json`; inventory returns the digest and native resource identities.

### 3.3 Firewall backend

`TeamLabFirewallService` chooses nftables when the Agent capability probe confirms required operations, otherwise uses iptables. It exposes only:

```csharp
ApplyRuntimePoliciesAsync(runtimeId, generation, policies, token)
RemoveRuntimePoliciesAsync(runtimeId, generation, token)
```

Policies are network-CIDR directional rules plus established/related return traffic. Command builders validate every name, CIDR, and address before shell execution. Managed infrastructure does not accept port or protocol fields.

### 3.4 Main application port

**Files:**

- Modify: `src/GZCTF/Modules/TeamLab/Application/ITeamLabNodeExecutor.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRouteApplicationService.cs`

Replace shell-shaped `ApplyShardAsync` and `ApplyRoutesAsync` with one desired-state method while keeping asset create/destroy separate:

```csharp
Task<TeamLabNodeInfrastructureResult> ApplyInfrastructureAsync(
    Guid workerNodeId,
    TeamLabNodeInfrastructureApplyRequest request,
    CancellationToken cancellationToken);
```

Application code cannot reference Agent request types, Linux interface names outside the stable resource-name factory, or firewall command syntax.

### 3.5 Large-unit gate

- [x] Implement desired-state managed switches, distributed router fragments, leased Fabric uplinks, directional policies, inventory, and precise cleanup.
- [x] Remove the old independent create-bridge/create-router/fabric orchestration path after all callers use the desired-state port.
- [x] Run the concentrated Agent network gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabCommandBuilder|FullyQualifiedName~TeamLabRouteIsolation|FullyQualifiedName~AgentCapability"
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj -c Release --no-restore
```

Expected: repeated apply produces identical desired-state facts; directional policy allows return traffic but blocks reverse initiation; cleanup removes only the exact runtime generation.

## 4. Bootstrap Profiles and Template Certification

### 4.1 Content-domain profile model

**Files:**

- Create: `src/GZCTF/Modules/Content/Domain/BootstrapProfile.cs`
- Create: `src/GZCTF/Modules/Content/Contracts/BootstrapProfileContracts.cs`
- Create: `src/GZCTF/Modules/Content/Application/BootstrapProfileApplicationService.cs`
- Create: `src/GZCTF/Modules/Content/Infrastructure/BootstrapProfileArtifactService.cs`
- Create: `src/GZCTF/Modules/Content/Infrastructure/BootstrapProfileDistributionService.cs`
- Create: `src/GZCTF/Modules/Content/Infrastructure/Persistence/BootstrapProfileEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/Content/Api/OpenBootstrapProfilesController.cs`
- Modify: `src/GZCTF/Modules/Content/ContentModuleRegistration.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`

Profile manifests use a bounded declarative contract:

```csharp
public sealed record BootstrapProfileManifest(
    int SchemaVersion,
    IReadOnlySet<OSType> OperatingSystems,
    IReadOnlySet<TeamLabAssetKind> AssetKinds,
    IReadOnlySet<string> RequiredTemplateCapabilities,
    IReadOnlyList<BootstrapParameterDefinition> Parameters,
    IReadOnlyList<BootstrapFileDefinition> Files,
    IReadOnlyList<BootstrapStepDefinition> Steps,
    IReadOnlyList<BootstrapHealthCheckDefinition> HealthChecks,
    int MaxReboots);
```

Commands are contained in the artifact, not arbitrary runtime request text. Parameter values are rendered into controlled files or environment entries. Secret parameters are accepted only from the encrypted runtime overlay.

### 4.2 OCI artifact storage

**Files:**

- Create: `src/GZCTF/Modules/Content/Infrastructure/OciArtifactRegistryClient.cs`
- Refactor: `src/GZCTF/Services/Fleet/VmImageRegistryService.cs`

Extract generic OCI blob/manifest push, resolve, download, and digest verification from `VmImageRegistryService`. VM images and bootstrap profiles use the same client but retain separate domain services and repository naming. The bootstrap repository is deterministic:

```text
10.24.0.28:5000/gzctf/bootstrap-profile/{profilePublicId}:{version}
```

### 4.3 Profile API

External endpoints follow Phase 1:

```text
POST   /api/open/v1/bootstrap-profiles
GET    /api/open/v1/bootstrap-profiles
GET    /api/open/v1/bootstrap-profiles/{profileId}
POST   /api/open/v1/bootstrap-profiles/{profileId}/versions
GET    /api/open/v1/bootstrap-profiles/{profileId}/versions/{version}
DELETE /api/open/v1/bootstrap-profiles/{profileId}
```

Artifact import and delete require `Idempotency-Key`, return `202 Accepted`, and reuse `ApiOperation`. Add scopes `bootstrap-profiles:read` and `bootstrap-profiles:write`. Cursor pagination and ProblemDetails follow `external-api-standard.md`.

### 4.4 Template capability certification

**Files:**

- Create: `src/GZCTF/Modules/Content/Domain/ImageTemplateCapabilityCertification.cs`
- Create: `src/GZCTF/Modules/Content/Application/ImageTemplateCertificationService.cs`
- Create: `src/GZCTF/Modules/Content/Infrastructure/ImageTemplateCertificationOperationHandler.cs`
- Modify: `src/GZCTF/Models/Data/ImageTemplate.cs`
- Modify: `src/GZCTF/Controllers/ImageTemplateController.cs`
- Modify: `src/GZCTF/Modules/Content/Api/OpenImagesController.cs`

Certification is bound to `ImageTemplateId + ImageHash`. Capabilities include:

```text
linux.cloud-init.nocloud.v1
guest.qga.v1
guest.virtio-serial.v1
windows.powershell.v1
windows.cloudbase-init.v1
network.virtio.v1
network.e1000e.v1
bootstrap.firstboot.v1
```

Certification may use a controlled temporary VM on a KVM node. It records probe step, evidence digest, node, time, and typed failure, then destroys the temporary domain and overlays. A changed image digest invalidates the result.

### 4.5 Pre-distribution

Publishing a release creates bootstrap-profile distribution jobs for nodes eligible to host referenced assets. Different nodes transfer in parallel; each node respects its existing image-transfer limit. The identity is `profileVersionId + artifactDigest + workerNodeId`, so repeated releases do not download the same artifact again.

### 4.6 Large-unit gate

- [x] Implement profile lifecycle, OCI artifact storage, template certification, release compatibility validation, and capability-aware pre-distribution.
- [x] Add an official minimal Linux service profile, Windows PowerShell service profile, endpoint sensor profile, and AD profile manifest under `scenarios/bootstrap-profiles/`.
- [x] Run the concentrated content gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~BootstrapProfile|FullyQualifiedName~ImageTemplateCertification|FullyQualifiedName~ImageDistribution"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~BootstrapProfile|FullyQualifiedName~OpenApi"
```

Expected: duplicate artifact/version operations are idempotent; digest mismatch fails closed; unsupported template/profile combinations cannot publish.

## 5. VM Guest Control and Cross-platform Injection

### 5.1 Libvirt domain contract

**Files:**

- Modify: `src/GZCTF.Agent/Models/VmModels.cs`
- Refactor: `src/GZCTF.Agent/Services/KvmService.cs`
- Create: `src/GZCTF.Agent/Services/Vm/VmDomainBuilder.cs`
- Create: `src/GZCTF.Agent/Services/Vm/VmGuestAgentService.cs`
- Create: `src/GZCTF.Agent/Services/Vm/VmBootstrapService.cs`
- Create: `src/GZCTF.Agent/Services/Vm/VmSensorChannelService.cs`
- Modify: `src/GZCTF.Agent/Controllers/VmController.cs`
- Test: `src/GZCTF.Test/UnitTests/Vm/VmGuestControlTests.cs`

`VmDomainBuilder` generates libvirt XML or virt-install arguments with:

```text
stable domain generation metadata
qemu guest agent virtio channel: org.qemu.guest_agent.0
optional endpoint sensor channel: org.gzctf.sensor.0
stable MAC-addressed NICs
cloud-init/config-drive media
qcow2 overlay backed by the verified template
```

QGA operations are typed and bounded:

```csharp
Task<VmGuestAgentStatus> WaitReadyAsync(string vmName, TimeSpan timeout, CancellationToken token);
Task WriteFileAsync(string vmName, string guestPath, Stream content, CancellationToken token);
Task<VmGuestCommandResult> ExecuteAsync(string vmName, VmGuestCommand command, CancellationToken token);
Task RebootAndWaitAsync(string vmName, TimeSpan timeout, CancellationToken token);
```

All `virsh qemu-agent-command` payloads use JSON serialization, never shell-built JSON. Command/result logs contain step IDs and exit categories, not command body, secrets, or stdout containing secret values.

### 5.2 Linux path

- NoCloud writes hostname, MAC-matched static IP, DNS, routes, runtime metadata, and bootstrap launcher.
- QGA confirms guest readiness and handles post-firstboot files, commands, reboot, and health facts.
- If a topology requires a profile or sensor, missing cloud-init/QGA certification fails release validation.

### 5.3 Windows path

- Initial network uses dnsmasq MAC reservation so Windows can boot before guest bootstrap.
- QGA writes a protected runtime JSON file and bootstrap artifact to `C:\ProgramData\GZCTF\Runtime`.
- PowerShell executes with `-NoProfile -NonInteractive -ExecutionPolicy Bypass -File` against a digest-verified local script.
- Static IP/DNS/routes, where requested by the profile, are applied by interface MAC rather than display name.
- Reboot completion requires QGA disconnect followed by a new ready session and matching runtime-generation marker.
- Cloudbase-init configuration drive is used only for templates certified for it; no runtime fallback guessing is performed.

### 5.4 Agent request separation

Extend `TeamLabNodeAssetCreateRequest` with profile reference, dependency-ready token, health contract, and endpoint observation mode. Do not put raw script content into this request.

The main application records separate stages for VM image ready, domain create, guest ready, profile inject, profile execute, reboot, and health. Image transfer and domain creation remain distinct operations and events.

### 5.5 Large-unit gate

- [x] Implement QGA channels, typed guest operations, Linux/Windows injection, reboot checkpoints, stable VM identity, and precise cleanup.
- [x] Remove Windows `CloudInit.Enabled = false` as the only behavior; Windows now selects an explicitly certified guest-init path.
- [x] Run the concentrated VM gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabVm|FullyQualifiedName~VmGuestControl|FullyQualifiedName~KvmProvider"
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj -c Release --no-restore
```

Expected: Linux NoCloud stays deterministic; Windows QGA bootstrap is typed and resumable; reboot preserves generation and interface identity; destroy removes overlay, seed/config media, channels, and bootstrap staging.

## 6. Dependency DAG Execution and Guarded Recovery

### 6.1 DAG compiler and execution state

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabDependencyGraph.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabDeploymentStageMachine.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabBootstrapOrchestrator.cs`
- Refactor: `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs`

Execution uses ready batches:

```csharp
while (graph.TryTakeReadyBatch(facts, out var batch))
{
    var results = await Parallel.ForEachAsync(
        batch,
        new ParallelOptions { MaxDegreeOfParallelism = runtimeLimit },
        ExecuteNodeAsync);
    PersistResultsAndUnlockDependents(results);
}
```

The Agent still applies node-local Docker/VM/network semaphores. The main runtime limit prevents one large topology from monopolizing all dispatch capacity.

### 6.2 Stable stages

Add stable deployment stages to queue and operation projections:

```text
artifacts-verifying
network-applying
routes-applying
asset-booting
guest-waiting
bootstrap-injecting
bootstrap-running
guest-rebooting
health-probing
observation-starting
```

Stage names are API values. Display strings remain client-localized in a future frontend phase.

### 6.3 Initial failure semantics

Any required DAG node failure:

1. marks the exact dependency/bootstrap fact failed;
2. stops scheduling new dependent nodes;
3. closes or avoids opening access;
4. runs generation-scoped cleanup in parallel across shards;
5. records cleanup-pending facts when a Worker is unavailable;
6. releases capacity only after terminal cleanup or durable pending cleanup ownership.

### 6.4 Recovery guards

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeRecoveryPolicy.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/RuntimeFactReconciliationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAccessGrantService.cs`

Automatic infrastructure replay requires:

```text
grace period elapsed
single recovery lease held
runtime still current generation
node inventory proves missing/drifted infrastructure
route version and desired-state digest known
WireGuard grant/server key/public UDP mapping facts intact
no native identity conflict
```

Automatic workload rebuild additionally requires `Stateless = true`, immutable image/profile digests, online Agent missing-resource proof, and an unused replacement capacity reservation. Default is no rebuild.

Entry-shard replacement changes only Worker tunnel target and internal Worker WireGuard port. Player endpoint, client address, grant public ID, client key, server public key, and public UDP port remain unchanged.

### 6.5 Large-unit gate

- [x] Implement dependency execution, stable stages, bootstrap resume, initial cleanup semantics, and guarded recovery.
- [x] Delete the `OrderIndex` group loop from `TeamLabShardDeploymentService` after DAG execution is active.
- [x] Run the concentrated orchestration gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabDeployment|FullyQualifiedName~TeamLabDependency|FullyQualifiedName~RuntimeRecovery|FullyQualifiedName~TeamLabAccessGrant"
```

Expected: independent assets run in parallel; dependencies wait for exact conditions; cycles fail at publish; infrastructure replay preserves WireGuard config; stateful assets never rebuild automatically.

## 7. Agent Packet Observer and Endpoint Sensor

### 7.1 Agent in-process observer

**Files:**

- Modify: `src/Directory.Packages.props` only if package version adjustment is required
- Modify: `src/GZCTF.Agent/GZCTF.Agent.csproj` to reference existing `SharpPcap` and `PacketDotNet`
- Create: `src/GZCTF.Agent/Services/Observation/TeamLabPacketObserver.cs`
- Create: `src/GZCTF.Agent/Services/Observation/ObservationPointRegistry.cs`
- Create: `src/GZCTF.Agent/Services/Observation/PacketFingerprint.cs`
- Create: `src/GZCTF.Agent/Services/Observation/FlowAccumulator.cs`
- Create: `src/GZCTF.Agent/Services/Observation/ObservationBatchSpool.cs`
- Modify: `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- Modify: `src/GZCTF.Agent/Models/TeamLabModels.cs`
- Remove flow-process methods from: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`

The observer:

- opens only registered managed interfaces;
- uses snap length 192 bytes by default;
- applies `ip or arp` capture filtering and emits IPv4 flow/path facts in Phase 9;
- parses Ethernet/SLL, IPv4, TCP, UDP, and ICMP;
- excludes TTL and checksums from packet fingerprint;
- tracks capture drops, parser failures, active keys, flush duration, and spool size with bounded metric labels;
- caps active flow keys and evicts least-recently-seen entries deterministically;
- writes a bounded local spool when the main service is unavailable.

Replace the Agent flow API with batch cursor semantics:

```csharp
Task<TeamLabObservationBatchResponse> ReadObservationBatchAsync(
    TeamLabObservationBatchRequest request,
    CancellationToken token);
```

The request includes runtime/generation and `afterSequence`; the response includes records, next sequence, dropped count, and observer health.

### 7.2 Endpoint sensor project

**Files:**

- Create: `src/GZCTF.EndpointSensor/GZCTF.EndpointSensor.csproj`
- Create: `src/GZCTF.EndpointSensor/Program.cs`
- Create: `src/GZCTF.EndpointSensor/Contracts/SensorEvent.cs`
- Create: `src/GZCTF.EndpointSensor/Platform/LinuxConnectionProvider.cs`
- Create: `src/GZCTF.EndpointSensor/Platform/WindowsConnectionProvider.cs`
- Create: `src/GZCTF.EndpointSensor/Transport/SensorChannelWriter.cs`
- Create: `src/GZCTF.EndpointSensor/Security/SensorEventSigner.cs`
- Modify: `src/GZCTF.slnx`
- Modify: `src/GZCTF/GZCTF.csproj` publish targets to build `linux-x64` and `win-x64` sensor artifacts

The sensor emits bounded newline-delimited JSON:

```csharp
public sealed record SensorEvent(
    int SchemaVersion,
    string RuntimePublicId,
    int Generation,
    string AssetKey,
    long Sequence,
    DateTimeOffset ObservedAt,
    SensorEventKind Kind,
    SensorProcessIdentity Process,
    SensorEndpoint Local,
    SensorEndpoint Remote,
    string Signature);
```

Linux maps socket-table snapshots to process identities through `/proc` with bounded scans and an incremental inode cache. Windows uses native extended TCP/UDP tables and stable process start time. Providers emit observed/open/closed lifecycle facts supported by their evidence source; they do not fabricate an exact accept/connect event that the source cannot prove. The sensor does not capture command-line secrets, file contents, payloads, credentials, or keystrokes.

### 7.3 Secure transport

**Files:**

- Create: `src/GZCTF.Agent/Services/Observation/EndpointSensorChannelService.cs`
- Create: `src/GZCTF.Agent/Services/Observation/EndpointSensorAuthenticator.cs`

- Docker receives a runtime-scoped Unix socket mount.
- VM receives `org.gzctf.sensor.0` virtio-serial channel backed by an Agent-owned Unix socket.
- Credential is HMAC material encrypted in the runtime overlay and bound to runtime, generation, asset, and sensor version.
- Agent rejects invalid signature, replayed sequence, oversized event, wrong generation, or unknown asset without writing raw payload to logs.

### 7.4 Large-unit gate

- [x] Implement observation registry, in-process packet metadata, bounded spool, cross-platform sensor, secure channel, and Agent inventory facts.
- [x] Delete the old per-network tcpdump text collector and file parser after the new observer is active.
- [x] Run the concentrated observer gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabObservation|FullyQualifiedName~PacketFingerprint|FullyQualifiedName~EndpointSensor"
dotnet build src/GZCTF.EndpointSensor/GZCTF.EndpointSensor.csproj -c Release --no-restore
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj -c Release --no-restore
```

Expected: same forwarded packet has a stable fingerprint across TTL/checksum changes; memory and spool remain bounded; invalid sensor events are rejected; disabled optional sensor does not block networking.

## 8. Traffic Persistence and Path Correlation

### 8.1 Observation and path facts

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTrafficObservation.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTrafficPath.cs`
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTrafficPathContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTrafficContracts.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabTrafficObservationEntityConfiguration.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabTrafficPathEntityConfiguration.cs`

`TeamLabTrafficObservation` stores bounded evidence:

```text
RuntimeId, Generation, ObservationPointId, WorkerNodeId
SourceSequence, ObservedAt, Direction
Source/Destination IP and port, Protocol, TCP flags
PacketLength, PacketFingerprint, FlowFingerprint
ProcessIdentityHash nullable, EvidenceKind
```

Raw packet payload is not stored.

### 8.2 Redis and PostgreSQL pipeline

**Files:**

- Modify: `src/GZCTF/Modules/TeamLab/Application/ITeamLabTrafficIngestor.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/RedisTeamLabTrafficIngestor.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/PostgresTeamLabTrafficBatchWriter.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabTrafficPersistenceWorker.cs`
- Modify: `src/GZCTF/Infrastructure/Persistence/Governance/DataRetentionPolicyCatalog.cs`

Extend the existing stream schema rather than create a second queue. Batch limits remain bounded by count and serialized bytes. PostgreSQL uses binary COPY into a staging table and `ON CONFLICT DO NOTHING` on runtime/generation/observation-point/source-sequence identity.

Retention defaults:

- packet observations: 7 days;
- aggregated flows: 30 days;
- derived paths: 30 days;
- endpoint process facts: 7 days;
- configurable extension through Phase 4 retention policy.

### 8.3 Path correlator

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficPathCorrelator.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabTrafficPathWorker.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs`

Correlation confidence is explicit:

```csharp
public enum TeamLabPathConfidence : byte
{
    PacketExact = 0,
    ProcessCorrelated = 1,
    TemporallyRelated = 2
}
```

- `PacketExact` requires matching packet fingerprint across ordered observation points.
- `ProcessCorrelated` requires signed endpoint facts matching one process instance between receive and connect events.
- `TemporallyRelated` is a bounded heuristic and is never described as causal.

Path derivation is idempotent by evidence fingerprint and time window. Re-running the worker cannot duplicate paths.

### 8.4 Public query

Add:

```text
GET /api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths?after={cursor}&limit=100
GET /api/open/v1/teamlab/runtimes/{runtimeId}/traffic/paths/{pathId}
```

Queries require existing runtime traffic permission, use opaque cursor pagination, expose public shard/network/infrastructure/asset keys, and never expose WorkerNode identity or host interface names.

### 8.5 Large-unit gate

- [x] Implement structured observation ingestion, retention, aggregate compatibility, path correlation, and public path query.
- [x] Run the concentrated persistence gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabTraffic|FullyQualifiedName~RedisTeamLab|FullyQualifiedName~Retention"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabTraffic|FullyQualifiedName~Redis"
```

Expected: Redis outage uses bounded fallback; replay is idempotent; path confidence is correct; existing flow cursor API remains compatible.

## 9. Multi-node PCAP and Object Storage

### 9.1 Capture model

**Files:**

- Modify: `src/GZCTF/Models/Data/TeamLabEntities.cs` only to remove the old capture definition after moving it
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTrafficCapture.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabCaptureEntityConfigurations.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`

`TeamLabTrafficCaptureJob` becomes an aggregate with child segments:

```csharp
public sealed class TeamLabTrafficCaptureSegment
{
    public Guid PublicId { get; set; }
    public int CaptureJobId { get; set; }
    public Guid WorkerNodeId { get; set; }
    public int ObservationPointId { get; set; }
    public TeamLabTrafficCaptureStatus Status { get; set; }
    public string? ObjectPath { get; set; }
    public string? Sha256 { get; set; }
    public long CapturedBytes { get; set; }
    public long UploadedBytes { get; set; }
}
```

Capture scope compilation resolves a deterministic observation-point set for network, path, asset neighborhood, or runtime.

### 9.2 Agent segment capture

**Files:**

- Create: `src/GZCTF.Agent/Services/Observation/TeamLabPcapService.cs`
- Create: `src/GZCTF.Agent/Services/Observation/PcapSegmentUploader.cs`
- Modify: `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- Remove capture methods from: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`

On-demand capture may use dumpcap or tcpdump. Each segment has an exact observation interface, size/time limits, PID/native process identity, capture digest, and resumable upload state. Metadata collector remains independent and continues running.

### 9.3 BlobStorage upload and download

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabCaptureArtifactStore.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/InternalTeamLabCaptureUploadController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTrafficController.cs`
- Modify: `src/GZCTF/Storage/Interface/IBlobStorage.cs` only if a required streaming primitive is missing

Agent upload uses a short-lived single-segment token bound to capture ID, segment ID, WorkerNode, expected maximum size, and expiry. The controller streams directly to `IBlobStorage.WriteAsync`, calculates SHA-256 while reading, and atomically marks the segment uploaded only after size/digest validation.

Object path:

```text
teamlab/captures/{runtimePublicId}/{generation}/{capturePublicId}/{segmentPublicId}.pcapng
```

Download streams a tar archive with `manifest.json` and every available segment through existing storage streaming helpers. It does not load full capture files into main-server memory.

### 9.4 Retention and cleanup

- capture expiry enters the Phase 4 retention catalog;
- expired objects are deleted from BlobStorage and the segment status becomes Expired;
- Agent local files remain until verified upload, then are deleted;
- interrupted upload can resume/retry by segment identity without starting a second capture;
- download is recorded as a sensitive-access operational event.

### 9.5 Large-unit gate

- [x] Implement capture scopes, multi-node segments, authenticated streaming upload, S3-compatible persistence, manifest download, retry, and expiry.
- [x] Remove single WorkerNode/FilePath assumptions from capture application and projection code.
- [x] Run the concentrated capture gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabCapture|FullyQualifiedName~BlobStorage"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabCapture|FullyQualifiedName~S3"
```

Expected: runtime scope creates segments on every required node; duplicate upload is idempotent; digest mismatch fails; expiry deletes objects; download manifest lists all segment identities.

Local evidence on 2026-07-15: capture unit gate passed `5/5`; PostgreSQL/Object Storage capture persistence gate passed `1/1`. Runtime capture creates one segment per required observation point, upload authorization is bound to capture/segment/node/size/digest, duplicate upload is idempotent, and expiry first enters `CleanupPending`. A dedicated coordinator test proves that the terminal state changes to `Expired` only after both the Agent segment and BlobStorage object are deleted.

## 10. API, Audit, Recovery Inventory, and Contract Migration

### 10.1 Public API compliance

**Files:**

- Modify: `src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTrafficController.cs`
- Modify: `src/GZCTF/Modules/Identity/Application/ApiTokenScopes.cs`
- Modify: `docs/commercialization/teamlab-api-foundation-contract.md`
- Modify: `docs/commercialization/open-api-v1-guide.md`
- Regenerate: `docs/commercialization/openapi/open-v1.json`

Every new public write endpoint:

- requires a valid scoped API token and object authorization;
- validates `Idempotency-Key` through `ExternalIdempotencyKey`;
- creates/reuses `ApiOperation` with `(tokenId, routeKey, key)` identity;
- returns `202 Accepted` for async work;
- uses `application/problem+json` with stable `code` and `traceId`;
- records external API audit without request secrets or artifact content.

### 10.2 Events and telemetry

**Files:**

- Modify: `src/GZCTF/Modules/Audit/Domain/OperationalEventCodes.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabEventRecorder.cs`
- Modify: `src/GZCTF/Infrastructure/Telemetry/PlatformTelemetry.cs`
- Modify: `docs/commercialization/event-taxonomy.md`

Add lifecycle coverage for infrastructure apply/drift/replay, Fabric lease, profile distribution, template certification, bootstrap step/reboot/health, observation point, observer drop/backpressure, sensor authentication, path derivation, capture segment upload/download/expiry, and guarded rebuild decision.

Metrics use bounded labels only: stage, result, asset kind, infrastructure kind, evidence type, capture scope, error category. Runtime, team, node, asset, and capture IDs stay in spans/events, not metric labels.

### 10.3 Agent inventory and recovery

**Files:**

- Modify: `src/GZCTF.Agent/Models/RuntimeInventoryModels.cs`
- Modify: `src/GZCTF.Agent/Controllers/RuntimeController.cs`
- Modify: `src/GZCTF/Services/Fleet/AgentClient.cs`
- Modify: `src/GZCTF/Modules/Runtime/Application/RuntimeFactReconciliationService.cs`

Inventory adds managed switch, router fragment, Fabric uplink, observation point, sensor channel, bootstrap execution, and PCAP segment resource kinds. Each fact includes runtime, generation, stable resource key, native identity, desired-state digest, and status. It excludes commands, environment, secrets, packet data, guest files, and output text.

### 10.4 Migrations

Generate three migrations:

```text
ExpandPhaseNineTeamLabNetworking
BackfillPhaseNineTeamLabNetworking
ContractPhaseNineTeamLabNetworking
```

- Expand adds v2 and runtime facts without requiring them for existing rows.
- Backfill creates implicit managed-switch/infrastructure facts for active v1 runtimes and imports current capture jobs as one segment when the file fact is still valid.
- Contract verifies every active runtime has consistent current-generation shard/network/asset/infrastructure facts before removing old modulo-link, per-network flow-process, and single-file capture fields.

Historical migrations remain untouched. Runtime code does not retain fallback reads after Contract.

### 10.5 Large-unit gate

- [x] Complete public contracts, scopes, events, metrics, inventory, recovery, and expand/backfill/contract migrations.
- [x] Run the concentrated contract gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~OpenTeamLab|FullyQualifiedName~ExternalApi|FullyQualifiedName~OperationalEvent|FullyQualifiedName~RuntimeFact"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~OpenApi|FullyQualifiedName~Migration"
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj
```

Expected: no pending model changes; v1 API identity remains stable; all new writes use operation/idempotency; recovery can classify every new Agent fact.

Local evidence on 2026-07-15:

- Release solution build passed with zero warnings and zero errors.
- TeamLab/Open API/operational event/runtime fact unit gate passed `38/38`.
- OpenAPI/migration/capture/traffic PostgreSQL integration gate passed `30/30` with Testcontainers Ryuk disabled per repository convention.
- `dotnet ef migrations has-pending-model-changes --configuration Release --no-build` reported `No changes have been made to the model since the last migration.`
- OpenAPI snapshot was regenerated from the real `open-v1` document host and now includes topology v2 traffic path and multi-segment capture contracts.
- Migration flow is explicit: Expand creates the dual-schema capture segment structure, Backfill maps legacy file facts and network keys, Contract validates active runtime facts before deleting the old single-node capture columns.

## 11. Consolidated Verification and Quality Review

### 11.1 Source boundary checks

```powershell
rg -n "protocolVersion\s*[><=]|TeamLabProtocolVersion|FabricAddress\(|StartFlowMetadataAsync|ResolveFlowLogPath|OrderIndex.*Deploy|new TeamLabEvent" src/GZCTF src/GZCTF.Agent
rg -n "GZCTF\.Models\.Data\.TeamLab|GZCTF\.Services\.Fleet" src/GZCTF/Modules/TeamLab/Contracts src/GZCTF/Modules/TeamLab/Application
rg -n "Flag|token|password|privateKey|user-data|userdata|Authorization" src/GZCTF/Modules/TeamLab src/GZCTF.Agent/Services/Observation
```

Expected:

- no active global protocol threshold;
- no modulo Fabric address generator;
- no old tcpdump text flow collector;
- no deployment ordering by `OrderIndex`;
- TeamLab events only through `TeamLabEventRecorder`;
- TeamLab contracts/application do not expose legacy model namespaces or Fleet implementation types;
- sensitive strings appear only in validation/redaction/test contexts, never log payload construction.

### 11.2 Full code gate

Run once after all large units are complete:

```powershell
dotnet restore src/GZCTF.slnx
dotnet build src/GZCTF.slnx -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build
$env:TESTCONTAINERS_RYUK_DISABLED='true'
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-build
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj
git diff --check
```

Expected: zero build warnings/errors, all unit/integration tests pass, EF reports no pending model change, and whitespace check passes.

### 11.3 Independent review

- [x] Dispatch one quality-review agent after the complete implementation diff exists.
- [x] Scope the review to Phase 9 correctness, module boundaries, security, concurrency, recovery, API compliance, traffic evidence integrity, sensitive-data handling, and cleanup.
- [x] Verify every finding against current code and runtime intent.
- [x] Fix confirmed issues in one batch, then rerun only affected gates followed by the final full gate once.
- [x] Record the review result and exact verification evidence in this document.

Independent review result on 2026-07-15:

- No critical finding remained. Nine important findings and one minor finding were confirmed and closed.
- Bootstrap Profile publication and background execution now both enforce owner/admin authorization. Docker profiles are rejected at publication until a production Docker executor exists, instead of failing after deployment starts.
- Capture expiry now uses `CleanupPending`; governance cannot bypass Agent cleanup, retry preserves recoverable evidence, and `Expired` requires successful Agent plus object-storage deletion.
- Fabric backfill allocates globally unique sequential `/30` leases. Contract migration validates asset, infrastructure, fragment, lease, and legacy capture completeness before destructive schema removal.
- Observation spooling uses activation epochs, serialized-byte limits, and streaming restore. Redis traffic ingestion uses a cross-replica capacity lease, exact trimming, and a hard backpressure limit that does not delete pending entries.
- The source boundary review found no active numeric protocol threshold, modulo Fabric allocator, legacy text flow collector, deployment ordering by display index, TeamLab application dependency on Fleet implementation types, or sensitive-value log payload.

Final local gate on 2026-07-15:

- `dotnet restore src/GZCTF.slnx`: all projects current.
- Release solution build: `0` warnings, `0` errors.
- Unit tests: `513/513` passed.
- PostgreSQL/Redis integration tests: `232/232` passed.
- EF migration model check: no pending model changes.
- Runtime-generated OpenAPI snapshot matches the committed document and the compatibility comparator reports backward compatible.
- `git diff --check` passes.
- Server deployment and live Linux/Windows/Docker multi-node acceptance were not executed in this round by user instruction.

## 12. Deployment and Real Acceptance on 10.0.7.118

Live acceptance started on 2026-07-15 against the two-Worker environment at `10.0.7.118` and
`10.0.7.125`. This section is an in-progress evidence ledger; only explicitly recorded checks are
claimed as complete.

### 12.0 Current acceptance ledger

- The customer-facing Open API reference is deployed at `/api-docs/`. The production host serves
  only `open-v1`; the live document contains 34 external routes, Bearer authentication, and three
  customer-readable TeamLab tags.
- Current deployed diagnostic main service assembly SHA-256 is
  `a0274d3b682952839d1556aeeab644a9846a13512874ab81d48c035c1d197a52`.
  Current diagnostic Agent binary SHA-256 on both Workers is
  `e680e2fc4a8d675d1061cabf3218487adf5c1a3f2bfa205f6f902b816eb9d7a0`.
  These builds are not the final immutable acceptance release.
- The stale mixed runtime `019f69e6-8ad8-77e9-804f-5f0473645d65` was destroyed through the public
  operation API. Both shards and all four assets reached `Destroyed`; the cleanup released Fabric
  link leases and image references.
- Docker topology `019f6a18-ba2d-7b47-9354-7d5d4045827c`, release
  `019f6a18-c091-7595-bfb5-cc17059874c7`, produced two physical shards with one cross-shard link.
  Runtime `019f6a1e-171d-73c5-97d1-224933bde87e` placed `entry-edge` on `10.0.7.118` and
  `core-portal` on `10.0.7.125`, and reached the initial Ready state in about 4.8 seconds.
- The first Docker run exposed four concrete defects before connectivity could be accepted: topology
  `startCommand` is persisted and decoded but omitted from the node create request; the local
  Worker is excluded from Agent inventory reconciliation even when it advertises
  `runtime.inventory.v1`; empty command stderr produces an empty deployment failure; and the public
  TeamLab capability response hard-codes `windowsVm=false`. The fixes were deployed together;
  concentrated deployment, command-builder, and fact-reconciliation tests passed `63/63`, and the
  live capability response now reports `windowsVm=true`.
- Fixed acceptance topology `019f6a3a-8ec0-7f86-b048-9951ba7ce0ce` produces two Docker shards and
  one cross-shard managed-router connection. Updating this topology after a destroyed runtime
  exposed a PostgreSQL lifecycle defect: draft updates deleted all network rows while released
  `TeamLabNetworkLease` rows retained restrictive foreign keys. Network rows are now reconciled in
  place by stable topology key; compatible updates preserve lease identities, and incompatible
  removal of a leased network returns `409 topology_network_in_use`. The same live PUT advanced the
  draft from revision 1 to revision 2 without deleting lease history.
- Release `019f6a4e-25ea-7c8d-b192-37bb953cee0c` preserves verified image start commands and required
  runtime environment. A controlled `--network none` image probe proved the portal process remains
  running and the edge process remains running with exact Docker command
  `["sh","-c","nginx -g \"daemon off;\""]`. The next live deployment passed image resolution,
  parallel container creation, and TeamLab veth attachment on both Workers. It then exposed an
  immediate single-shot Docker health probe; the main service now polls for up to 30 seconds and
  returns immediately on readiness. Final Docker reachability/HTTP re-run and all VM acceptance are
  pending because the client-to-lab network simultaneously lost both SSH `22/tcp` and platform
  `8080/tcp` reachability on `10.0.7.118`.
- Linux template `34` passed controlled certification for `bootstrap.firstboot.v1`, `guest.qga.v1`,
  `linux.cloud-init.nocloud.v1`, and `network.virtio.v1`; evidence digest
  `09a60af1c08bdd2a638a37ac3171795cc115821e4a935d46fcfcbc864908c652`.
- Windows template `35` proved that KVM and Windows boot are functional, but certification is not
  accepted: the injected full virtio guest-tools installer did not return from firstboot, so the
  QGA service and remaining firstboot scripts never ran. The replacement path uses the official
  QEMU Guest Agent MSI with unattended `msiexec` and auditable exit logging.
- Replacement Windows template `68` was imported through the normal `import-local` API with image
  hash `53fdf0bba10aaff6479932e195550ef75a8a983c4f3d86d44b12800a974fa939`. Its controlled probe
  reached the MSI-triggered reboot, but the legacy base image spent more than the 600-second probe
  budget shutting down Windows Update and rebooting. The template is therefore not accepted as a
  production asset. A one-time golden-image warm-up is in progress so QGA installation is sealed
  into the template instead of repeating for every team deployment.
- The replacement Windows golden-image candidate completed a single firstboot pass with the QEMU
  Guest Agent MSI and the Windows Server 2022 `vioserial` driver installed through
  `C:\Windows\Sysnative\pnputil.exe`. The libvirt QGA channel answered `guest-ping` after 25
  five-second probes (about 125 seconds). The independent qcow2 is being finalized before standard
  import and controlled certification.
- Bootstrap profiles were created and version 2 published successfully through the public API:
  Linux service `019f658f-a136-75ae-89cf-908053684744`, Windows PowerShell service
  `019f658f-a280-75b6-92cf-c9ff76e1656a`, and Windows AD Domain
  `019f658f-a2e6-7516-a02a-8a1a6c21c38e`. Publication exposed and fixed an OCI protocol defect:
  registry blob transfer bodies now use `application/octet-stream`, while artifact media types
  remain in manifest descriptors.
- Both Workers now publish `teamlab.observation.v2` on Ubuntu's `libpcap.so.0.8`, and both pass the
  separate TeamLab Docker and VM eligibility checks after their TeamLab tunnel state was enabled.
- Image certification now holds an `ImageCertification` distribution reference, concurrent VM OCI
  uploads use process-level single-flight, and stale same-host distribution claims are recovered at
  worker startup.
- Repeated historical publish/source/hotpatch backups on `10.0.7.118` were removed after preserving
  the active publish directory, the Phase 9 pre-deploy baseline, current source/deploy cache, and
  database SQL backups. Root filesystem usage dropped from `77%` to `69%`; VM and Docker caches were
  not pruned.
- Multi-node player entry and central policy enforcement passed on runtime
  `019f79c8-ac0e-7025-8099-09eb1127a4e0`: Docker, Managed Linux, and Opaque Windows assets reached
  Ready across two Workers; Windows TCP/3389 health passed; WireGuard entry and unauthorized-network
  isolation passed. The runtime was destroyed after evidence collection.
- Deterministic VM observation interfaces now use libvirt `target.dev` independently from the guest
  interface name. Runtime PCAP `019f79f7-aa85-7b34-86f7-1f317c9fc34a` captured and uploaded all ten
  bridge, router, Fabric, Docker, and Linux VM TAP segments. The downloaded archive is
  `artifacts/phase9-linux-final-reset2-20260719-019f79f7-aa85-7b34-86f7-1f317c9fc34a.tar`, 37,376
  bytes, SHA-256 `1a64af8a859ce5a5ecdb7f4e11cf40758d5455e58b6779b12d9ec8f0d848c2fd`.
- Reset capacity reconciliation is fact-driven. Cleanup records each destroyed asset's
  `ExecutionUpdatedAt`; placement discounts an old heartbeat count only when the heartbeat predates
  the completed cleanup of the immediately preceding generation. Database live facts and active
  reservations remain charged, so the correction does not permit capacity oversubscription.
  Concentrated VM-network, command-builder, and reset-placement tests passed `63/63`.
- Final Linux reset acceptance passed on runtime `019f79f6-24e7-7889-a847-9f4edabd4d3c`, release
  `019f79f6-1ce8-7387-9bc3-dc2fdd660fb5`. Generation 1 and generation 2 each reached Ready with two
  physical shards, Docker assets at `10.62.0.10` and `172.23.0.10`, and Managed Linux VM at
  `192.168.0.20`. WireGuard returned `3/3` packets before and after reset while direct player access
  to `172.23.0.10` remained blocked. Flow evidence covered all three RFC1918 networks and path
  correlation produced a two-hop path.
- Final destroy completed for generation 2. Post-destroy inspection on `10.0.7.118` and
  `10.0.7.125` found no TeamLab namespace, link, container, libvirt domain, or capture process. The
  protected non-TeamLab user VM on `10.0.7.125` was not modified. Complete evidence is recorded in
  `artifacts/phase9-linux-final-reset2-20260719.json`.
- The deployed main assembly SHA-256 is
  `ff6f6b8fa19c2b3811c20ca6b0e5fe91f5f0d8c6bc8399d8c4e0aa7da6da3006`; both deployed Agent
  binaries remain `11fd82ea6ee0da24fb771ac574a516ebfb3c72ca25c237500da9836fdff3466b`.
- Full published Windows AD scenario baking, domain membership verification, and a final mixed
  Windows reset remain pending. Existing evidence already covers Windows VM boot/health, mixed
  Docker/Linux/Windows sharding, Linux reset, multi-node PCAP, WireGuard isolation, and residue-free
  destroy; these are not restated as completion of the remaining AD acceptance.

### 12.1 Environment preparation

- Deploy main service and current Agent without interrupting unrelated platforms.
- Verify PostgreSQL, Redis, S3-compatible BlobStorage, internal OCI Registry, WireGuard, libvirt/KVM, Docker, SharpPcap/libpcap, dumpcap/tcpdump, qemu guest agent tooling, cloud-init ISO tooling, and firewall backend.
- Register enough WorkerNodes to prove physical sharding. If only one Worker exists, create isolated test Worker VMs on `10.24.0.118` rather than claiming multi-node acceptance from logical shards on one Agent.
- Import or create certified Linux and Windows templates through the normal ImageTemplate flow.
- Publish bootstrap profiles through the new profile API and verify OCI digest plus node distribution facts.

### 12.2 Acceptance topology A: dozens-node Docker industrial-style environment

Build at least:

- 8 managed switches/networks across mixed `10/8`, `172.16/12`, and `192.168/16` pools;
- 3 managed routers with directional connections;
- 24 or more low-resource Docker assets;
- HTTP, DNS, TCP service, simulated industrial protocol endpoints, and a jump chain;
- at least two physical WorkerNodes/shards.

Verify placement, capacity reservation, parallel startup, directed reachability, WireGuard entry, DNS, reset, destroy, flow metadata, hop path, runtime PCAP, and cleanup.

### 12.3 Acceptance topology B: Linux VM

Use at least two networks, one managed router, one Linux VM with two NICs or two Linux VMs, and one Docker dependency. Verify image pre-distribution, cloud-init, QGA, static addressing, DNS, routes, bootstrap profile, service health, sensor channel, reboot, traffic path, reset, and cleanup.

### 12.4 Acceptance topology C: Windows VM and AD

Use a certified Windows domain-controller template and member template:

1. deploy domain controller;
2. inject forest/domain/DNS profile;
3. reboot and pass AD/DNS health;
4. deploy member after dependency satisfaction;
5. join domain and reboot;
6. verify domain authentication or an equivalent automated membership check;
7. capture Windows network/process observations;
8. destroy and verify no VM/bootstrap/channel/capture residue.

### 12.5 Acceptance topology D: mixed full path

Place A, B, and C so the traffic crosses at least two networks and, where possible, two WorkerNodes. Generate:

```text
A -> B request
B -> C request
C -> B response
B -> A response
```

Verify:

- all four flow segments exist;
- packet-identical traffic has ordered switch/router/Fabric observation hops;
- B endpoint facts relate receive and outbound connect to the same process where the sensor supports it;
- confidence is `ProcessCorrelated` only with signed endpoint evidence;
- runtime PCAP manifest contains every participating Worker/observation segment;
- no direct unauthorized reverse initiation is possible.

### 12.6 Failure and cleanup acceptance

- Restart Agent during metadata collection and verify observer inventory/recovery without duplicate facts.
- Interrupt one capture upload and verify idempotent resume.
- Temporarily make one Worker unavailable and verify `Degraded`, grace period, no premature asset rebuild, and stable WireGuard player config.
- Drift a managed route and verify infrastructure replay restores the same desired-state digest.
- Mark one disposable Docker asset stateless, remove it through Agent facts, and verify guarded rebuild only after every precondition passes.
- Destroy all test runtimes and delete only Phase 9-created test games/topologies/profiles/templates.
- Confirm no containers, domains, qcow2 overlays, seed/config ISO, bridges, namespaces, routes, firewall rules, WireGuard interfaces, sensor sockets, bootstrap staging, local PCAP, or unreferenced object-storage artifacts remain.

### 12.7 Final evidence

Record in this document:

- deployed Git commit and Agent binary SHA-256;
- migration version;
- Worker capability manifests;
- topology/release/runtime public IDs;
- shard placement summary;
- startup-stage durations;
- flow/path/capture evidence IDs;
- failure/recovery correlation IDs;
- cleanup inventory results;
- unresolved environment limitations, if any, without claiming unexecuted acceptance.

### 12.8 Operational documentation

**Files:**

- Create: `docs/commercialization/runbooks/teamlab-networking-operations.md`
- Create: `docs/commercialization/benchmarks/phase-09-teamlab-networking-baseline.md`
- Modify: `docs/platform-commercialization-audit-progress.md`
- Modify: `docs/platform-commercialization-master-plan.md`

The runbook must cover topology/profile publication, node capability diagnosis, Fabric route inspection, VM guest-control diagnosis, observer health, capture storage, guarded rebuild, reset/destroy residue checks, rollback, and correlation-based incident handling. The benchmark records real startup, placement, observation drop, persistence lag, PCAP upload, recovery, and cleanup evidence from `10.24.0.118`.

## 13. Exit Criteria

Phase 9 is complete only when:

- a connected managed-router topology truly spans multiple WorkerNodes;
- Docker, Linux VM, Windows VM, and mixed environments deploy through one runtime path;
- topology schema v2 is public, documented, idempotent, and compatible with immutable v1 releases through one decode normalizer;
- bootstrap profiles are digest-pinned, certified, pre-distributed, resumable, and audited;
- AD and other service roles remain image or signed Bootstrap Profile scenario content rather than TeamLab core hard-coding; domain promotion is not a networking-foundation gate;
- default metadata covers every managed observation point without one tcpdump process per network;
- packet hops and process correlation expose explicit evidence confidence;
- runtime-wide PCAP persists as verified multi-node segments in S3-compatible BlobStorage;
- failure handling does not rebuild stateful assets or rotate player WireGuard identity;
- reset/destroy leave no runtime resources or capture artifacts outside retention policy;
- Phase 3 API, Phase 6 scheduling, Phase 7 audit/recovery, migration, build, unit, integration, sensitive-data, and quality-review gates pass;
- real acceptance evidence from `10.0.7.118` is recorded.

## 14. 2026-07-20 Current Acceptance State

- VM storage and distribution now use the simplified immutable path: external qcow2, platform digest verification, OCI Registry, Opaque registration, controlled certification, and KVM-node distribution. Platform-side ISO/Packer building is no longer part of TeamLab.
- Immutable release `phase9-qcow2-staging-20260720` is active on `10.0.7.118`; archive SHA-256 is `a4fe8af2d0e9f3d7f3dbd015818a0eca54dffbc2ee412ab35464a058278327b3`.
- Real operation `019f8052-b7a5-73f4-bf8c-baf1969b8979` proved qcow2 import, immutable artifact registration, parallel distribution to Workers `10.0.7.118` and `10.0.7.125`, byte-identical local caches, and complete API-driven deletion with no Registry, database, or node-cache residue.
- The proof template remained `Opaque`; no legacy or synthetic image was falsely promoted to `Managed`.
- This checkpoint was superseded by the final 2026-07-22 acceptance in section 15.

## 15. 2026-07-22 Final Two-Worker Acceptance

### 15.1 Production conclusion

- Networking-foundation status: `APPROVED`.
- The accepted immutable release is `phase9-reset-placement-final3-20260722`; archive SHA-256 is `7c7485eef1e9328ee21013b63c582499d07f14504817ffdb1fb49d56b96966e5`.
- The standard release archive was atomically activated on `10.0.7.118`; no DLL hotpatch or persistent `files` replacement was used.
- The independent review's three P1 and nine P2 findings are closed. The final live run additionally verified the PCAP deletion race fix and exact-placement reset behavior.

### 15.2 Accepted topology and placement

- Runtime `019f899b-5629-7cd2-ac41-f4e2d5b00020`, release `019f899b-4d74-7150-84f1-3dd2eb327528`, contained Entry Docker, Core Docker, Managed Linux VM, and Opaque Windows VM assets.
- Four mixed RFC1918 networks were used: `10.92.0.0/24`, `172.28.0.0/24`, `192.168.64.0/24`, and `10.93.0.0/24`.
- Two physical shards reached `Running`. Generation 1 and generation 2 used the same placement: `entry/data` on `10.0.7.118`, `core/ad` on `10.0.7.125`.
- Reset reused placement only after exact topology/cardinality matching and fresh node capability, protocol, Fabric, and slot checks. It ignored only stale dynamic CPU/memory load for replacement of the generation that had just been released; it did not bypass capability or capacity-slot admission.

### 15.3 Network, observation, lifecycle, and cleanup evidence

- Player WireGuard reached the entry asset at `10.92.0.10`; direct player access to all three internal networks was denied.
- Traffic ingestion persisted 100 flow records and produced one correlated path. Runtime capture uploaded and digest-verified all `11/11` bridge, router, Fabric, Docker, and VM segments.
- Reset completed exactly from generation 1 to generation 2. The old WireGuard grant was rejected and the newly issued grant reached the replacement generation.
- Destroy completed in approximately `10.6s`. Inspections on both Workers found no acceptance-runtime containers, libvirt domains, namespaces, links, processes, or files.
- Initial create took `406.2s` and reset took `425.4s`; capture took `12.4s`. The create/reset critical path was the selected Opaque Windows template's TCP/3389 readiness, not image transfer, Fabric convergence, or fixed waiting in the networking control plane.

### 15.4 Evidence artifacts and service-content boundary

- Machine-readable evidence: `artifacts/phase9-review-mixed-20260722-final3.json`, SHA-256 `8DF373DB94B3D379B2EAB36CA46F84A9C4E8065FCAF9B86CD2A2B6C9897FB47B`.
- PCAP archive: `artifacts/phase9-review-mixed-20260722-final3-019f89a1-94ab-7d6c-a0de-c3f70d6bfa05.tar`, SHA-256 `1758D413C1D37710B57C4AEBA4223FF95D976C41ABF7A786056A1ED00169C149`.
- TeamLab owns placement, network attachment, routing, DNS/DHCP, WireGuard/Fabric, lifecycle, health contracts, and traffic evidence. AD DS promotion, domain membership, industrial services, and application installation are supplied by immutable images or signed Bootstrap Profiles. Those services use the same network substrate but are not implemented or hard-coded by it.
