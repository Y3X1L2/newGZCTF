# TeamLab completion progress

## Authoritative status - 2026-07-04

This section is the single source of truth after context compression. Read this first before doing any TeamLab work.

### Phase status

| Phase | Status | Exact meaning |
| --- | --- | --- |
| Phase 0 - baseline and compatibility boundary | Complete | Existing normal CTF Docker, VM/KVM, AWDP, and legacy penetration flows were kept as compatibility boundaries. Do not reopen this phase unless a regression is reported. |
| Phase 1 - control-plane model and node capability | Complete | TeamLab runtime/control-plane fields, node TeamLab capability state, UDP mapping model, and node enablement flow are implemented and covered by tests. |
| Phase 2 - infrastructure WireGuard tunnel and public UDP gateway | Complete for platform code; production gateway validation remains Phase 10 evidence | Code paths, models, gateway provider, cleanup, and failure handling are implemented and tested. Do not redo Phase 2 design work. Real target-network UDP validation is tracked under Phase 10, not as unfinished Phase 2 development. |
| Phase 3 - Linux bridge TeamLab data plane | Complete for platform code | Bridge/router namespace/WireGuard namespace command generation, runtime gating, probes, cleanup ownership, and player VPN export are implemented and tested. Do not describe this as "basically complete"; it is complete at code level. |
| Phase 4 - native Docker assets in TeamLab | Complete at platform-code level | Published topology assets are planned as native Docker specs, started with host `network none`, attached to LabNetwork bridges by veth, recorded as runtime facts, and included in cleanup. Real Linux Agent/Docker acceptance remains Phase 9/10 evidence. |
| Phase 5 - native VM assets in TeamLab | Complete at platform-code level | Agent KVM bridge NIC request generation, deployment-state-machine VM creation, runtime fact recording, cleanup planning, Ready/IP validation, and bridge-aware MAC/IP probing are implemented and tested. Real multi-OS Agent/KVM acceptance remains Phase 9/10 evidence. |
| Phase 6 - DHCP/DNS/AD orchestration | Complete at platform-code level | Agent dnsmasq DHCP/DNS provider, static leases/records, namespace DNS probes, cleanup ownership, deployment-state-machine gating, and AD role/start-priority metadata are implemented and tested. Real multi-OS DHCP lease and AD image-script acceptance remains Phase 9/10 evidence. |
| Phase 7 - admin orchestration UX | Complete at platform-code level | Admin orchestration surfaces are aligned with the TeamLab control-plane/runtime surfaces and no longer carry player-facing attack-graph/fog wording. Real operator acceptance remains Phase 10 evidence. |
| Phase 8 - player black-box VPN experience | Complete at platform-code level | Player workspace now exposes VPN config, challenge list/details, submit, reset, and progress only. Attack graph, fog state, topology unlock API/events, and engineering fields are removed from the player contract. Real VPN entry validation remains Phase 10 evidence. |
| Phase 9 - concurrency/capacity/fault injection | Not started | Needs explicit capacity and failure-injection acceptance. |
| Phase 10 - final regression and delivery evidence | Not complete | Includes real target public UDP gateway validation, full build/test suite, frontend checks, deployment docs, and regression evidence. |

### Current working phase

The current development phase is Phase 9/10. Phase 0-8 must not be reworked or re-audited as normal task flow unless a new regression points to a specific defect.

### Do not repeat

- Do not re-decide the architecture: main server is control plane only; WorkerNode is data plane; public server is a thin UDP gateway.
- Do not reintroduce attack graph, fog-of-war, topology-map player UI, or port-level ACL claims.
- Do not keep using "basically complete" for Phase 0-3. Use "complete at code level; production gateway validation remains Phase 10 evidence" when that distinction matters.
- Do not claim production acceptance for DHCP/DNS/AD until a real WorkerNode validates DHCP lease acquisition, DNS resolution, VM readiness, and AD image-script behavior. The platform-code path is implemented; target-network acceptance is still required.
- Do not preserve legacy fabric compatibility inside the new TeamLab runtime path. Legacy code may remain only where it is still required by old penetration games; new TeamLab deployment must be native and should delete obsolete compatibility branches once replacement behavior is proven by tests.
- Do not treat legacy penetration fabric compatibility as Commercial V1. Commercial V1 requires native Docker, native VM, DHCP/DNS, AD metadata, admin UX, and player UX closure.

### Next implementation queue

1. Phase 9: add capacity/failure-injection tests around node scheduling, UDP port exhaustion, DHCP/DNS failure, VM startup timeout, and cleanup pending states.
2. Phase 10: run real WorkerNode acceptance for Docker veth, KVM bridge NICs, dnsmasq DHCP/DNS, WireGuard UDP gateway, reset, destroy, and residual cleanup.
3. Update this document immediately after each completed implementation batch with the exact test command and result.

## Current objective

Complete the TeamLab / VPN / VM multi-segment lab module as a production-quality feature with real runtime gating, player VPN config export, and no frontend placeholder copy.

## Non-negotiable cleanup

- Remove frontend production-placeholder wording from node/player surfaces.
- Do not mark a TeamLab runtime as player-open or running unless real network, gateway, and peer state are available.
- Keep existing Docker TCP proxy, VM, AWDP, and normal CTF flows stable.
- Preserve unrelated NebulaMind scenario changes in the worktree.

## Work checkpoints

- [x] Inspect current TeamLab backend, Agent, and frontend entry points.
- [x] Add regression tests for placeholder peer/state behavior and player-facing WireGuard config export.
- [x] Replace placeholder player-open behavior with explicit network/gateway/peer success gating.
- [x] Expose operationally honest node/network checks in the admin UI.
- [x] Add player VPN config API and frontend copy/download panel.
- [x] Add EF migration for protected WireGuard peer secrets.
- [x] Return BadRequest for failed TeamLab plan/deploy/destroy operations instead of wrapping failures in HTTP 200.
- [x] Gate player VPN config export on the existing penetration team environment being Running.
- [x] Move gateway addressing out of Linux bridge and into the TeamLab router namespace interfaces.
- [x] Move the WorkerNode WireGuard interface into the TeamLab router namespace and add peer /32 route commands.
- [x] Record runtime networks and runtime assets after successful deployment, and mark assets destroyed on cleanup.
- [x] Resolve the running penetration environment entry fabric network and attach the TeamLab router to that existing bridge.
- [x] Keep TeamLab cleanup ownership scoped to TeamLab-owned resources; do not delete the external penetration fabric bridge.
- [x] Force TeamLab planning/deployment onto the WorkerNode that hosts the deployed penetration team environment.
- [x] Add VPN return routes on entry-fabric containers so challenge assets can reply to the WireGuard client /32.
- [x] Add TeamLab router namespace static routes for already-applied routes whose source is the entry fabric CIDR.
- [x] Separate player client AllowedIPs from the WorkerNode WireGuard peer allowed-ips; the server peer only binds the client tunnel /32.
- [x] Reject malformed WorkerNode TeamLab tunnel IPs before nodes can enter the TeamLab scheduling pool.
- [x] Add a node management enablement flow that separates network probing from explicit TeamLab scheduling enablement.
- [x] Run final backend, Agent, and frontend verification.
- [x] Correct the phase plan so Phase 0-3 MVP is not treated as Commercial V1 completion.
- [x] Add real TeamLab namespace connectivity probe before opening a runtime to players.
- [x] Replace placeholder public UDP mapping removal with executable iptables/nftables cleanup commands.
- [x] Keep failed TeamLab cleanup in `CleanupPending` instead of marking the runtime destroyed.
- [x] Require a deployable published penetration topology version before planning a TeamLab runtime.
- [x] Add Agent KVM network argument support for VM NICs attached to TeamLab Linux bridges while preserving the default libvirt network path for existing VM flows.
- [x] Add a tested TeamLab asset-spec layer for Docker/VM runtime interface facts, MAC generation, container veth attach requests, and VM bridge interface requests.
- [x] Add a tested published-topology asset planner that maps released TeamLab networks/nodes into stable LabNetwork bridges, Docker/VM asset kinds, IPs, MACs, and image/template sources.
- [x] Add a tested published snapshot parser for TeamLab so runtime planning can bind immutable released topology instead of current draft config or legacy runtime facts.
- [x] Extend Agent TeamLab container veth attach to configure static return routes and DNS in the container namespace, so native Docker assets can reply to VPN clients and resolve internal names without the legacy fabric path.
- [x] Remove the legacy penetration-fabric deployment branch from the new TeamLab runtime path; TeamLab now always deploys from the published topology snapshot.
- [x] Replace TeamLab Docker's misleading `UsePenetrationFabric=true` with explicit `UseHostNetworkNone=true`, so native TeamLab containers start with Docker `network none` before veth attachment.
- [x] Require native TeamLab VM Ready/IP validation after Agent VM creation before recording VM runtime facts.
- [x] Make Agent VM IP lookup for TeamLab bridge NICs use the planned bridge/MAC context instead of libvirt `default` / `virbr0` fallback.
- [x] Wire the TeamLab asset-spec layer into the deployment state machine so Docker assets are created/attached natively without relying on the legacy penetration fabric runtime.
- [x] Wire VM assets into TeamLab deployment with bridge NICs, runtime fact recording, failure cleanup, and Ready/IP detection.
- [x] Validate TeamLab planned cleanup names use the same DHCP/DNS service naming as runtime requests, so failed deploy and destroy cleanup can remove dnsmasq state deterministically.
- [x] Run Agent dnsmasq inside the TeamLab router namespace, matching the gateway-addressing design where gateway IPs live in the namespace instead of on the host bridge.
- [x] Add DNS/DHCP health probes through the TeamLab router namespace and gate deployment on those probes when records are present.
- [x] Add AD role metadata and startup priority to the published asset plan so DomainController/DNS assets start before DomainMember assets without hardcoding domain internals in the platform.
- [x] Remove player-facing attack graph/fog-of-war contract, route, SignalR event, service, and frontend listener from the TeamLab player path.
- [x] Keep player workspace as black-box VPN + challenge list/details + submit/reset only; prerequisites remain score-item based rather than topology-unlock based.
- [x] Align Phase 7/8 static wording with the design: no player-facing topology, security domain, internal asset list, attack graph, fog, or engineering route fields.
- [ ] Validate native Docker and VM TeamLab deployment against a real Linux Agent with Docker, KVM, bridge networking, WireGuard, dnsmasq, and KVM enabled.
- [ ] Validate real multi-OS DHCP lease acquisition and DNS resolution for Windows Server, Windows client, Ubuntu, and Kylin-style images.

## Notes

- Current branch: `codex/teamlab-vpn-vm-phase-0-3`.
- CodeGraph status checked successfully on 2026-07-03; index contains 964 files.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab"` passed 28/28 after WireGuard key and config export fixes.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabAdminControllerTests|FullyQualifiedName~TeamLabWireGuardServiceTests"` passed 7/7 after API failure semantics and penetration-runtime gating fixes.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabCommandBuilderTests"` passed 7/7 after router namespace and WireGuard namespace command-chain fixes.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabDeploymentServiceTests"` passed 9/9 after runtime facts recording and cleanup-state tests.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabDeploymentServiceTests|FullyQualifiedName~TeamLabCommandBuilderTests"` passed 20/20 after fabric entry bridge, cleanup ownership, WorkerNode alignment, and WireGuard peer allowed-ips fixes.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabPlanServiceTests|FullyQualifiedName~TeamLabDeploymentServiceTests"` passed 19/19 after planning was constrained to the penetration environment WorkerNode.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet"` passed 95/95 after TeamLab/Fleet integration checks.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabDeploymentServiceTests|FullyQualifiedName~TeamLabCommandBuilderTests|FullyQualifiedName~TeamLabPlanServiceTests"` passed 28/28 after router namespace static-route command support.
- Current TeamLab opening is intentionally bridged to the existing penetration environment runtime: TeamLab VPN config is not exposed unless the existing challenge assets are already deployed and Running. The VPN router now attaches to the actual running penetration fabric entry bridge instead of an isolated TeamLab-only entry bridge.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabPlanServiceTests.SelectNode_RejectsMalformedTeamLabTunnelIp"` passed after adding strict IPv4 validation to TeamLab node scheduling and node health marking.
- Node management now supports the operational flow: probe TeamLab/VPN network first, then input the WorkerNode infrastructure tunnel IPv4 and explicitly enable TeamLab scheduling.
- Production frontend scan found no player/admin TeamLab placeholder wording such as `Phase 0-3`, `dry-run`, `入口目标`, `攻击图`, or `迷雾`; remaining `DryRun` matches backend/Agent control-plane fields and is not player-facing copy.
- Final verification on 2026-07-03:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet"` passed 101/101.
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore` passed with 0 warnings and 0 errors.
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore` passed with 0 warnings and 0 errors.
  - `pnpm --dir src/GZCTF/ClientApp check` passed.
  - `pnpm --dir src/GZCTF/ClientApp build` passed.
  - `git diff --check` passed; Git only reported line-ending normalization warnings for existing dirty files.
- Follow-up hardening on 2026-07-03:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabCommandBuilderTests.ProbeAsync_DryRunBuildsNamespacePingProbe" -p:UseSharedCompilation=false -m:1` passed.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~PublicUdpGatewayProviderTests.RemoveMapping_BuildsExecutableIptablesDeleteCommands" -p:UseSharedCompilation=false -m:1` passed.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabDeploymentServiceTests.ResolveFabricProbeTarget_UsesRunningEntryRuntimeNodeIp" -p:UseSharedCompilation=false -m:1` passed.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet" -p:UseSharedCompilation=false -m:1` passed 104/104 after probe, cleanup, and UDP gateway hardening.
  - Parallel `dotnet test` on Windows can lock `GZCTF.dll` / `GZCTF.Agent.dll`; use `-p:UseSharedCompilation=false -m:1` for reliable local verification.
- Commercial V1 hardening on 2026-07-03:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabWireGuardServiceTests" -p:UseSharedCompilation=false -m:1` passed 6/6 after removing the legacy penetration-runtime argument from client config export tests.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabPlanServiceTests" -p:UseSharedCompilation=false -m:1` passed 12/12 after adding published-version gating.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabVmNetworkTests" -p:UseSharedCompilation=false -m:1` passed 3/3 after adding Agent KVM bridge NIC command generation.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabAssetPlanServiceTests" -p:UseSharedCompilation=false -m:1` passed 3/3 after adding TeamLab asset-spec helpers.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabAssetPlanServiceTests" -p:UseSharedCompilation=false -m:1` passed 5/5 after adding published-topology asset planning for Docker/VM specs.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabPublishedTopologyServiceTests" -p:UseSharedCompilation=false -m:1` passed 2/2 after adding published snapshot parsing.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabDeploymentServiceTests.RecordRuntimeAsset_TracksDockerSourceAndInterfaceFacts" -p:UseSharedCompilation=false -m:1` passed after adding full interface summary persistence for runtime assets.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabCommandBuilderTests.AttachContainerAsync_DryRunBuildsVethAttachmentWithoutFabricNaming" -p:UseSharedCompilation=false -m:1` passed after adding route/DNS commands to native container attach.
  - Current implementation is still not Commercial V1 complete: Docker/VM/DHCP/DNS/AD are not yet fully wired through `TeamLabDeploymentService`.
- 2026-07-04 TeamLab runtime path cleanup:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~BuildNativeDockerContainerConfig_UsesNoPublicPortAndFixedWorkerNode|FullyQualifiedName~ResolveDeploymentMode_AlwaysUsesNativePublishedTopologyForTeamLab" -p:UseSharedCompilation=false -m:1` passed 2/2 after replacing TeamLab's old `UsePenetrationFabric` container-start hint with explicit `UseHostNetworkNone`.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1` passed 78/78 after deleting the legacy fabric deployment branch from `TeamLabDeploymentService`.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~ValidateNativeVmReady_RequiresActualPrimaryIp" -p:UseSharedCompilation=false -m:1` passed 5/5 after adding VM Ready/IP validation.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1` passed 83/83 after wiring VM Ready/IP validation into the TeamLab deployment flow.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabVmNetworkTests" -p:UseSharedCompilation=false -m:1` passed 4/4 after adding bridge-aware TeamLab VM IP probe command coverage.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1` passed 84/84 after sending VM bridge/MAC context through the Agent VM IP lookup API.
- 2026-07-04 DHCP/DNS/AD closure:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~BuildNativeCleanupResourceNames_UsesPlannedNetworksBeforeRuntimeFactsExist|FullyQualifiedName~BuildNativeCleanupResourceNames_MatchesDhcpDnsRequestServiceNames|FullyQualifiedName~RecordNativeRuntimeFacts_TracksDhcpDnsServicesForDestroyCleanup" -p:UseSharedCompilation=false -m:1` passed 3/3 after replacing fragile DHCP/DNS cleanup-name derivation with a single service-name helper.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~ConfigureDhcpDnsAsync_DryRunBuildsDnsmasqStaticLeaseCommands|FullyQualifiedName~ProbeDhcpDnsAsync_DryRunBuildsNamespaceDnsProbe" -p:UseSharedCompilation=false -m:1` passed 2/2 after moving dnsmasq into the TeamLab router namespace and adding DNS/UDP 53 probes.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~BuildPublishedAssetPlan_MapsDockerAndVmAssetsToStableTeamLabInterfaces|FullyQualifiedName~BuildPublishedAssetPlan_OrdersDomainControllerBeforeDomainMembers" -p:UseSharedCompilation=false -m:1` passed 2/2 after adding AD role metadata and startup priority to asset planning.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1` passed 90/90 after DHCP/DNS namespace probing, cleanup-name hardening, and AD metadata changes.
- 2026-07-04 Phase 7/8 closure:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabPlayerWorkspaceContractTests" -p:UseSharedCompilation=false -m:1` failed red first because the player workspace still exposed `AttackGraph` / `FogState` and `PenetrationPlayerController` still exposed `/attack-graph`; after removing the legacy contract and route it passed 2/2.
  - Removed `PenetrationAttackGraphService`, its DI registration, player `/attack-graph` endpoint, `PenetrationAttackGraphModel`, `PenetrationFogState`, old attack-graph SignalR event, and frontend `getAttackGraph` / `ReceivedPenetrationAttackGraphUpdate` usage.
  - Player `GetWorkspace` now returns all visible score items in order and uses score-item prerequisites for locked state; submit no longer depends on topology/fog unlock and only publishes `ReceivedPenetrationWorkspaceUpdate` refresh events on successful submissions or environment lifecycle changes.
  - Static scan over player/admin TeamLab source found no player-facing `AttackGraph`, `attackGraph`, `fogState`, `PenetrationFogState`, `ReceivedPenetrationAttackGraphUpdate`, `入口目标`, `所属模块`, `攻击图`, `迷雾`, `Phase 0-3`, `dry-run`, or `生产启用` residuals in the checked source paths.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1` passed 92/92.
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false` passed with 0 warnings and 0 errors.
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore -p:UseSharedCompilation=false` passed with 0 warnings and 0 errors.
  - `pnpm --dir src/GZCTF/ClientApp check` passed.
  - `pnpm --dir src/GZCTF/ClientApp build` passed.
  - `git diff --check` passed; Git only reported line-ending normalization warnings for existing dirty files.
- 2026-07-04 Phase 7/8 product-semantics correction in progress:
  - User clarified that this round is not only legacy cleanup. The admin orchestration flow must be fully rewritten to the current TeamLab/VPN-first model.
  - Admin one-click preset is being changed from old external-entry/DMZ wording into a TeamLab internal lab generator. The default first network is now `service-lan` / `Service / 业务接入网段`; the old `DMZ / 初始业务区` and `dmz-service` defaults were removed from the active admin/backend default paths.
  - Admin production wording is being changed from `安全域` / `访问策略` to `内网网段` / `内网路由关系`, so operators configure TeamLab internal segments, asset NICs, and routed paths rather than an attack graph or public-entry scene.
  - One-click preset edges and newly drawn edges now default to `PenetrationEnforcementMode.Both`, so they are deployable TeamLab route relationships by default instead of hint-only task lines.
  - Backend validation/plan messages are being aligned with `内网网段` / `路由关系`; this is a visible API contract correction and still requires fresh tests before claiming completion.
  - Contract test `TeamLabPenetrationUxContractTests` was tightened to reject old admin wording (`DMZ / 初始业务区`, `dmz-service`, `安全域`, `访问策略`) and to require the admin one-click preset to use deployable runtime routes by default.
  - Verification still pending for this correction batch: targeted TeamLab UX contract tests, TeamLab tests, frontend `pnpm check`, static residual scans, and `git diff --check`.

## Remaining beyond this checkpoint

- Phase 9: capacity and failure-injection coverage for node count/resource exhaustion, UDP pool exhaustion, Docker/VM creation failure, dnsmasq failure, VM IP timeout, and cleanup pending states.
- Phase 10: real target-network acceptance on Linux WorkerNode with Docker, KVM, bridge networking, WireGuard, dnsmasq, and the public UDP gateway.
- Full regression before delivery: backend build, Agent build, TeamLab/Fleet tests, frontend `pnpm check`, frontend build, `git diff --check`, and deployment notes.

## 2026-07-04 Phase 7/8 route-semantics correction

- Root cause found: native TeamLab Docker attach previously installed static routes from each asset interface to every other LabNetwork. That made multi-segment labs effectively all-network reachable even when the admin topology only intended selected routed relationships.
- Fixed published topology parsing so released snapshots restore `PenetrationEdge` route relationships, not only networks/nodes/interfaces/score items.
- Added `TeamLabRuntimeRouteMatrix` and now deployment derives allowed cross-network CIDRs from published `RuntimeRoute/Both` relationships plus explicit multi-interface routing assets. Docker attach receives only VPN client return route and the allowed CIDRs for the asset's current interface network.
- Routing assets are now detected from multi-interface topology or Router/Firewall/Bastion infrastructure roles; native Docker configs for those assets get `NET_ADMIN` and IPv4 forwarding enabled. Normal assets stay non-forwarding.
- Admin UX default edge mode remains deployable `Both`; the selected-edge editor no longer falls back to `HintOnly`, and the runtime empty state explains that deployed RuntimeRoute/Both relationships produce records.
- Cleaned unused old public-admin URL helper and unused early two-network MVP helper methods to reduce stale external-entry interpretation.
- Fresh targeted verification passed: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabPublishedTopologyServiceTests.ParsePublishedSnapshot_BuildsTransientTopologyWithImages|FullyQualifiedName~TeamLabDeploymentServiceTests.BuildNativeContainerAttachRequests_UsesOnlyAllowedNetworkRoutes|FullyQualifiedName~TeamLabDeploymentServiceTests.BuildNativeContainerAttachRequests_DoesNotRouteToUnlinkedNetworks|FullyQualifiedName~TeamLabDeploymentServiceTests.BuildNativeDockerContainerConfig_EnablesForwardingOnlyForRoutingAssets|FullyQualifiedName~TeamLabDeploymentServiceTests.BuildRuntimeRouteMatrix_UsesPublishedRouteEdgesWithoutOpeningUnrelatedNetworks" -p:UseSharedCompilation=false -m:1` passed 5/5.
- Remaining verification for this batch: full TeamLab tests, frontend check, residual scans, and diff check.

## 2026-07-04 Phase 7/8 admin route-mode closure

- Root cause found: the admin editor still exposed the old `HintOnly` execution mode and route-hint checkbox, while request/data models still defaulted `PenetrationEdge.EnforcementMode` to `HintOnly`. That allowed newly edited TeamLab routes to become non-deployable hint lines again.
- Added contract coverage to reject `HintOnly` from the admin editor and require request/data edge defaults to use deployable TeamLab routes.
- Updated the admin edge editor to expose only deployable route modes (`Both` and `RuntimeRoute`) and removed the old "sync as challenge path hint" toggle from the TeamLab editing path.
- Updated request/data defaults to `PenetrationEnforcementMode.Both`.
- Updated `PenetrationService.AddEdgesToConfig` to normalize saved TeamLab route relationships to `Allow`, `IsRouteHint=true`, and either `RuntimeRoute` or `Both`; incoming `HintOnly` / `Deny` values from stale drafts or manual API calls no longer persist into the active TeamLab topology.
- Fresh red/green verification: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabPenetrationUxContractTests" -p:UseSharedCompilation=false -m:1` first failed 2/12 on the new contract assertions, then passed 12/12 after the fix.
- Full verification for this batch:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1` passed 118/118.
  - `pnpm --dir src/GZCTF/ClientApp check` passed.
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false` passed with 0 warnings and 0 errors.
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore -p:UseSharedCompilation=false` passed with 0 warnings and 0 errors.
  - Static residual scan over admin/player TeamLab sources found no active `安全域`, `访问策略`, `攻击图`, `迷雾`, `公网入口`, `发布宿主端口`, `Public / Edge`, `edge-gateway`, `dmz-service`, `DMZ / 初始`, `VPN 初始网段`, `{{node:nm-node`, `仅题目提示`, `题目路径线索`, or `提示/审计` matches. The only `外网` match is the harmless substring inside `额外网卡`.
  - `git diff --check` passed; output contains only existing CRLF normalization warnings and no whitespace errors.
