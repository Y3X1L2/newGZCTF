# Phase 9 TeamLab Networking Commercialization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 TeamLab 完成一套可供平台内部和外部系统复用的商业级组网底座，支持 Docker、Linux VM、Windows VM、混合多节点分片、显式交换机/路由器、服务动态注入、全路径流量观测和可恢复生命周期。

**Architecture:** Phase 9 在 Phase 3 topology/release/runtime API、Phase 6 队列和容量调度、Phase 7 事件与事实恢复之上增加 topology schema v2。二层网段仍单节点归属，平台托管路由器通过多 shard L3 Fabric 实现跨节点连接；镜像化网络设备保留为可攻击资产。默认流量元数据覆盖所有观察点，按需 PCAP 分段写入 S3 兼容 BlobStorage，深度观测通过可选端点传感器补充进程关联。

**Tech Stack:** .NET 10、ASP.NET Core、EF Core 10、PostgreSQL 17、Redis Stream、GZCTF.Agent、libvirt/KVM、Docker、WireGuard、Linux bridge/netns、iptables/nftables、cloud-init、qemu guest agent、PowerShell、SharpPcap、PacketDotNet、OCI Registry、S3-compatible BlobStorage、OpenTelemetry、xUnit、Testcontainers。

---

## Implementation Progress

### 2026-07-14 Planning Baseline

- Branch: `codex/phase-09-teamlab-networking`.
- Code baseline: `f3a0da4e0fc6125e14287d743af49d0238f9e988`.
- Phase 8 is intentionally skipped. Phase 9 may implement the backend VM guest-control capabilities it directly requires, but it does not implement Phase 8 frontend access work.
- Current connected topology placement is incorrect for multi-node use because `TeamLabAssetPlanner.BuildGroups` unions every network attached to a routing asset.
- Current infrastructure is not first class: `TeamLabAssetKind` only supports Docker and VM, and switches are implicit bridges.
- Current Windows TeamLab VM path does not generate initialization data; the Agent does not create qemu guest agent or sensor channels.
- Current metadata collection creates one tcpdump text process per network; current PCAP is single-node and stored under `/run`.
- The approved design is recorded in `docs/superpowers/specs/2026-07-14-phase-09-teamlab-networking-commercialization-design.md`.
- Development uses large-unit gates. Unit tests run after a coherent subsystem is complete; the final branch receives one independent quality review and one consolidated verification pass.

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

- [ ] Implement schema v2 contracts, canonical codec, v1 normalizer, execution model, validators, and release capability advertisement.
- [ ] Generate an expand migration for topology v2 draft/release metadata without rewriting immutable v1 release JSON or hashes.
- [ ] Run the concentrated topology gate:

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

`TeamLabFabricLinkAllocator` allocates /30 links from configured `TeamLab:FabricLinkPool`, default `169.254.0.0/16`, inside the runtime planning transaction. PostgreSQL `cidr` plus an active GiST exclusion constraint prevents overlap. Reset creates new-generation leases only after old generation cleanup; destroy releases leases after Agent facts confirm cleanup.

### 2.4 Atomic reservation and rollback

`TeamLabPhysicalPlacementService` must reserve all selected nodes in one scheduler lease and database transaction. If any node becomes ineligible before commit, no shard assignment or reservation is committed. Recovered existing assignments are revalidated per shard feature requirements, so a Docker-only shard does not require KVM.

### 2.5 Large-unit gate

- [ ] Implement runtime infrastructure/dependency/observation facts, corrected connected placement, and persisted Fabric link leases.
- [ ] Add expand/backfill tests proving active v1 runtimes receive equivalent implicit switch/router facts without changing release identity.
- [ ] Run the concentrated placement gate:

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

- [ ] Implement desired-state managed switches, distributed router fragments, leased Fabric uplinks, directional policies, inventory, and precise cleanup.
- [ ] Remove the old independent create-bridge/create-router/fabric orchestration path after all callers use the desired-state port.
- [ ] Run the concentrated Agent network gate:

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

- [ ] Implement profile lifecycle, OCI artifact storage, template certification, release compatibility validation, and capability-aware pre-distribution.
- [ ] Add an official minimal Linux service profile, Windows PowerShell service profile, endpoint sensor profile, and AD profile manifest under `scenarios/bootstrap-profiles/`.
- [ ] Run the concentrated content gate:

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

- [ ] Implement QGA channels, typed guest operations, Linux/Windows injection, reboot checkpoints, stable VM identity, and precise cleanup.
- [ ] Remove Windows `CloudInit.Enabled = false` as the only behavior; Windows now selects an explicitly certified guest-init path.
- [ ] Run the concentrated VM gate:

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

- [ ] Implement dependency execution, stable stages, bootstrap resume, initial cleanup semantics, and guarded recovery.
- [ ] Delete the `OrderIndex` group loop from `TeamLabShardDeploymentService` after DAG execution is active.
- [ ] Run the concentrated orchestration gate:

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

- [ ] Implement observation registry, in-process packet metadata, bounded spool, cross-platform sensor, secure channel, and Agent inventory facts.
- [ ] Delete the old per-network tcpdump text collector and file parser after the new observer is active.
- [ ] Run the concentrated observer gate:

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

- [ ] Implement structured observation ingestion, retention, aggregate compatibility, path correlation, and public path query.
- [ ] Run the concentrated persistence gate:

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

- [ ] Implement capture scopes, multi-node segments, authenticated streaming upload, S3-compatible persistence, manifest download, retry, and expiry.
- [ ] Remove single WorkerNode/FilePath assumptions from capture application and projection code.
- [ ] Run the concentrated capture gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabCapture|FullyQualifiedName~BlobStorage"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~TeamLabCapture|FullyQualifiedName~S3"
```

Expected: runtime scope creates segments on every required node; duplicate upload is idempotent; digest mismatch fails; expiry deletes objects; download manifest lists all segment identities.

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

- [ ] Complete public contracts, scopes, events, metrics, inventory, recovery, and expand/backfill/contract migrations.
- [ ] Run the concentrated contract gate:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~OpenTeamLab|FullyQualifiedName~ExternalApi|FullyQualifiedName~OperationalEvent|FullyQualifiedName~RuntimeFact"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-restore --filter "FullyQualifiedName~OpenApi|FullyQualifiedName~Migration"
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj
```

Expected: no pending model changes; v1 API identity remains stable; all new writes use operation/idempotency; recovery can classify every new Agent fact.

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

- [ ] Dispatch one quality-review agent after the complete implementation diff exists.
- [ ] Scope the review to Phase 9 correctness, module boundaries, security, concurrency, recovery, API compliance, traffic evidence integrity, sensitive-data handling, and cleanup.
- [ ] Verify every finding against current code and runtime intent.
- [ ] Fix confirmed issues in one batch, then rerun only affected gates followed by the final full gate once.
- [ ] Record the review result and exact verification evidence in this document.

## 12. Deployment and Real Acceptance on 10.24.0.118

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
- AD is demonstrated as a profile/DAG scenario rather than core hard-coding;
- default metadata covers every managed observation point without one tcpdump process per network;
- packet hops and process correlation expose explicit evidence confidence;
- runtime-wide PCAP persists as verified multi-node segments in S3-compatible BlobStorage;
- failure handling does not rebuild stateful assets or rotate player WireGuard identity;
- reset/destroy leave no runtime resources or capture artifacts outside retention policy;
- Phase 3 API, Phase 6 scheduling, Phase 7 audit/recovery, migration, build, unit, integration, sensitive-data, and quality-review gates pass;
- real acceptance evidence from `10.24.0.118` is recorded.
