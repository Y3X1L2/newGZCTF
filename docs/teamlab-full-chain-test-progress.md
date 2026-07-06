# TeamLab full-chain test progress

## Scope

Target server: `10.24.0.27`

Objective: verify the currently deployed TeamLab / VPN / VM multi-segment module end to end, collect every error with trigger path and raw evidence, fix newly found base-function defects first, then create and validate a Docker-only TeamLab test range through the normal platform template/orchestration flow.

## 2026-07-05 full-function acceptance target

This round is a real server acceptance pass, not a code-level claim. The target is a Docker-only full TeamLab range on `10.24.0.27`; Windows and real multi-OS VM acceptance are explicitly outside this round because the user requested excluding Windows and current KVM node readiness is not the focus.

### Must pass in this round

| Area | Acceptance requirement | Evidence to collect |
| --- | --- | --- |
| Platform health | `gzctf` and `gzctf-agent` active, `/` and `/api/status` HTTP 200 | systemctl / curl output summary |
| Node readiness | At least one schedulable TeamLab node with `TeamLabNetworkEnabled=true`, healthy tunnel status, valid tunnel IP | node API / database state |
| Normal platform flow | Admin creates game, student creates team and joins, admin saves / validates / plans / publishes topology using normal API paths | API status and IDs |
| Multi-segment IPAM | At least three internal networks are assigned non-overlapping runtime CIDRs inside one team prefix | DB runtime networks and router namespace addresses |
| Docker native attach | Multiple Docker assets start with TeamLab bridge/veth attachment, stable runtime IP/MAC facts, no public TCP entry dependency | DB runtime assets, Docker inspect / namespace evidence |
| Routed reachability | A dual-interface router asset or runtime route enables only intended cross-segment reachability | route matrix, namespace route table, positive and negative probes |
| DNS/DHCP | Per-segment dnsmasq starts in TeamLab router namespace; DNS records resolve expected asset names/aliases | dnsmasq process, DNS probe result |
| WireGuard/VPN export | Player VPN config returns public endpoint, client /32, DNS, and AllowedIPs covering all intended networks only | player API response |
| Player black-box UX contract | Player workspace shows challenge list/details/submit/reset and does not expose topology, entry target, internal asset list, attack graph, or fog fields | workspace JSON field inspection |
| Flag lifecycle | Wrong flag rejected; all expected correct flags accepted and score reflected | submit API responses |
| Reset | Reset keeps player progress/VPN semantics and recreates runtime resources cleanly | reset API, runtime status, post-reset reachability |
| Destroy/cleanup | Destroy removes TeamLab namespaces, bridges, wg links, NAT rules, dnsmasq processes, and runtime containers | host residual scan and DB status |
| Internal UDP map | `/api/internal/teamlab-udp-map` rejects unauthenticated access and returns active mappings only while runtime is open | HTTP 401 and authenticated response |

### Out of scope for this round, but still required before final commercial acceptance

- Real external public UDP gateway handshake through `203.195.157.191`.
- Real VM acceptance through KVM bridge NICs, including Windows, Ubuntu, and Kylin-like images.
- AD image-script acceptance. The platform orchestration metadata can be checked statically, but domain content remains image-owned.
- High-concurrency capacity testing such as 10/50/100 teams and UDP port exhaustion.

### Current blocker carried into this round

- A fresh three-segment acceptance deployment reached native Docker attach, then failed with `sh: 1: cannot create /etc/resolv.conf: Permission denied`.
- Root cause: `TeamLabNetworkService.AttachContainerAsync` treats DNS injection as an attach-time shell mutation and writes container `/etc/resolv.conf` through `docker exec`. Some real Docker images expose Docker-managed or otherwise non-writable resolver files, so deployment can fail after the network fabric is otherwise valid.
- Required fix before full acceptance can proceed: DNS servers must be passed during container creation through Docker `HostConfig.DNS` on both local and Agent creation paths. Attach commands should only attach veth, set MAC/IP/routes, and show routes.

## Test layers

| Layer | Status | Notes |
| --- | --- | --- |
| Local build and automated tests | Passed | TeamLab/Fleet tests passed 140/140. GZCTF build, Agent build, frontend check/build, and diff check passed. |
| Server runtime health | In progress | Services and Agent API healthy. TeamLab scheduling not enabled yet because node tunnel IP is not configured. |
| Rendered frontend smoke | Pending | Load server UI, collect console errors, verify primary pages do not crash. |
| Docker-only TeamLab range | Pending | Create/publish/deploy using normal platform flow, no Windows VM in this round. |
| Network isolation and lifecycle | Pending | Validate bridge/router/veth/WireGuard-facing reachability, cross-team isolation, reset/destroy cleanup. |

## Findings

### F-000 - Test account username exceeded platform validation limit

- Status: Closed / test data corrected.
- Layer: Test setup.
- Trigger: attempted to register `codex-e2e-admin-20260704150008` and `codex-e2e-student-20260704150008`.
- Raw response: HTTP 400 `{"title":"Username is too long","status":400}`.
- Root cause: test-generated usernames were longer than the platform username limit.
- Fix: use short `e2ea*` / `e2es*` usernames for subsequent setup.

### F-001 - TeamLab scheduling is not enabled on current server nodes

- Status: Investigating / likely environment precondition.
- Layer: Server runtime health.
- Evidence: database `WorkerNodes` on `10.24.0.27` shows `TeamLabNetworkEnabled=false` for both `Local Server` and `worker-10.24.0.30`.
- Raw message: `Network components are detected. Configure a tunnel IP before enabling TeamLab scheduling.`
- Impact: Docker-only TeamLab range deployment cannot start through TeamLab scheduling until a node is explicitly enabled with a valid TeamLab tunnel IP or the runtime path supports local single-node testing without infrastructure tunnel.
- Next action: inspect TeamLab node enablement flow and decide the correct test setup for main-server-only validation.

## Command log

- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet" -p:UseSharedCompilation=false -m:1` -> passed 140/140.
- `dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false` -> passed, 0 warnings, 0 errors.
- `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore -p:UseSharedCompilation=false` -> passed, 0 warnings, 0 errors.
- `pnpm --dir src/GZCTF/ClientApp check` -> passed.
- `pnpm --dir src/GZCTF/ClientApp build` -> passed.
- `git diff --check` -> passed; only Git line-ending normalization warnings were printed.
- Server health check on `10.24.0.27`: `gzctf=active`, `gzctf-agent=active`, `/` HTTP 200, `/api/status` HTTP 200, `/api/teamlab/status` HTTP 200.


## Latest server baseline - 2026-07-04 15:12 CST

- `gzctf.service`: active.
- `gzctf-agent.service`: active.
- `GET http://127.0.0.1:8080/`: HTTP 200.
- Direct unauthenticated Agent checks now return 401 / `Invalid auth token`; this is expected for the deployed authenticated Agent API and is not treated as a health failure.
- `ContainerProvider.PublicEntry`: `203.195.157.191`.
- `NginxProxyConfig`: enabled, listen range `30000-30059`.
- `DockerRegistrySettings.Address`: `10.24.0.28:5000`.
- Local node `10.24.0.27`: schedulable, `TeamLabNetworkEnabled=true`, `TeamLabTunnelIp=10.24.0.27`, tunnel status `3`.
- Worker node `10.24.0.30`: schedulable but `TeamLabNetworkEnabled=false`; excluded from this round because the user requested main-server-only functional testing first.
- Ready Docker template candidates confirmed: `ImageTemplates.Status=0` means `ImageStatus.Ready`; selected template for first minimal test: ID 114 `pwn1` unless runtime image pull fails.

## Executed API setup - 2026-07-04 15:14 CST

- Admin login `e2ea150043` -> HTTP 200; profile role `SuperAdmin`.
- Student login `e2es150043` -> HTTP 200; profile role `Student`.
- Student created team `e2e-team-150043` -> HTTP 200, team ID `29`.
- Admin created Penetration game `e2e TeamLab Docker Only 150043` through `POST /api/edit/Games` -> HTTP 200, game ID `28`.
- Student joined game through `POST /api/game/28` with team `29` -> HTTP 200.
- Admin `GET /api/game/28/Participations` -> HTTP 200, participation `50`, status `Accepted`.


## Minimal Penetration topology setup - 2026-07-04 15:18 CST

- `GET /api/admin/pentest/games/28` -> HTTP 200; default config contained one network and no nodes, so validation failed with expected errors: no asset node and no entry/public node.
- Saved minimal Docker-only topology through `PUT /api/admin/pentest/games/28`:
  - one network `E2E Entry Network`, slug `e2e-entry`, auto CIDR preview `10.48.0.0/28`.
  - one entry Docker node `e2e-entry-web`, image template ID `114`, exposed service port `80`.
  - one primary interface `eth0`, auto preview IP `10.48.0.2`.
  - one static score item `E2E Static Flag`, flag `flag{codex_teamlab_e2e_static}`.
- `POST /api/admin/pentest/games/28/validate` -> HTTP 200, `valid=true`.
- `POST /api/admin/pentest/games/28/plan` -> HTTP 200, node image resolved to `10.24.0.28:5000/ctf/pwn/21:latest`.
- `POST /api/admin/pentest/games/28/publish` -> HTTP 200, `publishedVersion=1`, `status=Published`.
- Observation: server normalized entry node `publishPort` to `true`; keep this under watch during VPN-first design validation.


### F-002 - TeamLab plan returns HTTP 500 after creating Scheduled runtime

- Status: Open / root cause identified.
- Layer: TeamLab admin API / serialization / deployment state machine.
- Trigger: `POST /api/admin/teamlab/games/28/teams/29/plan` after publishing minimal topology.
- Raw response: HTTP 500. Body starts with `{"success":true,"message":"TeamLab runtime planned.","runtime":...}` and then repeatedly serializes `runtime.publicUdpMapping.runtime.publicUdpMapping...` before the generic `{"title":"Internal server error","status":500}` tail.
- Database side effect: runtime ID `1` was created with status `Scheduled`; event ID `1` says `TeamLab runtime planned on node Local Server with UDP 32000.`
- Secondary trigger: `POST /api/admin/teamlab/games/28/teams/29/deploy` immediately after plan.
- Raw secondary response: HTTP 500. Body starts with `{"success":false,"message":"Cannot plan TeamLab runtime from status Scheduled.","runtime":...}` and has the same recursive serialization shape.
- Root cause hypothesis: TeamLab API result DTO exposes EF entity `TeamLabRuntime`; JSON serialization walks bidirectional navigation (`Runtime.PublicUdpMapping.Runtime...`). Deploy path also calls plan logic that refuses an already Scheduled runtime instead of continuing deployment from the planned runtime.
- Required fix: return a cycle-free DTO or omit runtime entity from API response; make deploy idempotently reuse Scheduled/Ready planned runtime instead of failing.


### F-002 verification - 2026-07-04 15:30 CST

- Fix deployed to `10.24.0.27` from `artifacts/publish-1024-teamlab-e2e-fix`.
- `GET /` after restart -> HTTP 200.
- `POST /api/admin/teamlab/games/28/teams/29/plan` -> HTTP 200.
- Response now contains a bounded `runtime` summary with `publicUdpMapping`, no EF navigation recursion.
- Re-plan of status `Scheduled` returns `TeamLab runtime is already planned.` and does not fail.
- Status: Closed.

### F-003 - TeamLab deploy fails because Public UDP gateway endpoint is not configured

- Status: Open / investigating.
- Layer: TeamLab deployment / public UDP gateway configuration.
- Trigger: `POST /api/admin/teamlab/games/28/teams/29/deploy` after F-002 fix.
- Raw response: HTTP 400 `{"success":false,"message":"Public UDP gateway endpoint is not configured.", ... "status":6, "lastError":"Public UDP gateway endpoint is not configured."}`.
- Events: deploy started, then error event with the same message.
- Impact: runtime reaches `Failed`; no player VPN exposure can be completed.
- Next action: inspect `PublicUdpGatewayProvider` and deployed config defaults to decide whether the missing endpoint should be derived from existing `ContainerProvider.PublicEntry` / Nginx public endpoint or requires explicit `PublicUdpGatewayConfig`.

### F-003 verification - 2026-07-04 15:30 CST

- Status: Closed.
- Fix: `TeamLabWireGuardService` now falls back to `ContainerProvider.PublicEntry` when `PublicUdpGatewayConfig.PublicEndpoint` is not configured.
- Verification: TeamLab/Fleet tests passed after the fix; server `/` health check returned HTTP 200 after deployment.

### F-004 - Healthy local TeamLab node loses schedulable state after restart or dry-run probe

- Status: Closed locally / pending server deployment verification.
- Layer: Fleet local node registration and TeamLab node enablement probe.
- Trigger 1: service restart refreshes the existing local `WorkerNode`.
- Trigger 2: node management calls `POST /api/v1/nodes/{id}/teamlab/enable` with `dryRun=true` on an already healthy node.
- Raw server evidence before fix:
  - `TeamLabNetworkEnabled = f`
  - `TeamLabTunnelIp = 10.24.0.27`
  - `TeamLabTunnelStatus = 2`
  - `TeamLabTunnelLastError = Network components are detected. Configure a tunnel IP before enabling TeamLab scheduling.`
  - deploy response: `No schedulable TeamLabNetwork WorkerNode is healthy.`
- Root cause:
  - `LocalNodeRegistrar` used `GetValue("Agent:LocalNodeSchedulable", false)`, so an unset config key was treated as an explicit operator decision to disable scheduling during every local-node refresh.
  - `NodeTunnelService.EnableDryRunAsync` always set `TeamLabNetworkEnabled=false` and `TeamLabTunnelStatus=Probing`, so a harmless "check TeamLab network" action degraded an already enabled healthy node.
- Fix:
  - `LocalNodeRegistrar.ApplyLocalNodeRefresh` preserves the existing scheduling flag unless `Agent:LocalNodeSchedulable` is explicitly configured.
  - `NodeTunnelService.ApplyDryRunProbeResult` preserves already enabled healthy tunnel state; dry-run now only places not-yet-enabled nodes into the "needs tunnel IP" state.
- Local verification:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~ApplyLocalNodeRefresh|FullyQualifiedName~ApplyTeamLabDryRunProbe" -p:UseSharedCompilation=false -m:1` -> passed 3/3.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet" -p:UseSharedCompilation=false -m:1` -> passed 146/146.
- Next action:
  - Publish and deploy to `10.24.0.27`.
  - Re-enable the local node with tunnel IP `10.24.0.27` if the deployed database is currently degraded.
  - Re-run `plan` / `deploy` for game `28`, team `29`, then continue Docker-only TeamLab lifecycle and network-isolation checks.

### F-004 server verification - 2026-07-04 15:53 CST

- Status: Closed.
- Deployed package: `artifacts\publish-1024-teamlab-e2e-fix3`.
- Server health after deployment: `gzctf.service=active`, `GET /` -> HTTP 200.
- Existing database state was degraded by the old dry-run behavior, so local node was explicitly re-enabled through:
  - `POST /api/v1/nodes/02ec0080-77ef-4030-b075-4bce445ea2f3/teamlab/enable`
  - body: `{"dryRun":false,"tunnelIp":"10.24.0.27"}`
- Node API after re-enable:
  - `TeamLabNetworkEnabled=true`
  - `TeamLabTunnelStatus=3`
  - `TeamLabTunnelIp=10.24.0.27`
  - `CanHostTeamLab=true`
- `POST /api/admin/teamlab/games/28/teams/29/plan` -> HTTP 200, `TeamLab runtime planned.`

### F-005 - Deploy stops because TeamLab command response is still dry-run

- Status: Closed locally / pending server deployment verification.
- Layer: TeamLab deployment / Agent command execution.
- Trigger: `POST /api/admin/teamlab/games/28/teams/29/deploy` after F-004 was fixed and node became schedulable.
- Raw response: HTTP 400 `{"success":false,"message":"TeamLab command plan returned without execution.", ... "status":6, "lastError":"TeamLab command plan returned without execution."}`
- Event evidence: deploy event `Starting native TeamLab deployment from published topology.`, followed by error `TeamLab command plan returned without execution.`
- Impact: native bridge/router/WireGuard/DHCP/DNS creation does not start; Docker-only TeamLab range cannot be built.
- Root cause:
  - The WorkerNode Agent was correctly configured with `TeamLab__Enable=true` and `TeamLab__DryRun=false`.
  - The platform server had no `TeamLabNetwork` config block, so `TeamLabNetworkConfig.DryRun` fell back to its code default `true`.
  - `TeamLabDeploymentService` passes `_config.DryRun` into every bridge/router/WireGuard/DHCP/DNS/container-attach/probe request, so real deploy always sent `dryRun=true` to the Agent.
- Fix:
  - `TeamLabNetworkConfig.DryRun` now defaults to `false`. The safety boundary remains on the Agent: a WorkerNode still executes OS network mutation only when `TeamLab__Enable=true` and `TeamLab__DryRun=false`.
- Local verification:
  - Added `GlobalConfigTests.TeamLabNetworkConfig_DefaultsToRealDeploymentRequests`.
  - Red test first failed with `Expected: False / Actual: True`.
  - After fix: `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabNetworkConfig_DefaultsToRealDeploymentRequests|FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet" -p:UseSharedCompilation=false -m:1` -> passed 147/147.
- Next action:
  - Publish and deploy the fix to `10.24.0.27`.
  - Retry `POST /api/admin/teamlab/games/28/teams/29/deploy`.

### F-005 server verification - 2026-07-04 16:08 CST

- Status: Closed.
- Deployed package: `artifacts\publish-1024-teamlab-e2e-fix4`.
- Server health after deployment: `gzctf.service=active`, `GET /` -> HTTP 200.
- `POST /api/admin/teamlab/games/28/teams/29/deploy` no longer returns `TeamLab command plan returned without execution`; the Agent now executes real commands.

### F-006 - Agent real command execution fails with missing cwd / key-file permission error

- Status: Fixed locally / pending server deployment verification.
- Layer: WorkerNode Agent TeamLab command runner / Linux command execution.
- Trigger: `POST /api/admin/teamlab/games/28/teams/29/deploy` after F-005 fix.
- Raw response: HTTP 400 `{"success":false,"message":"sh: 0: getcwd() failed: No such file or directory\nfopen: Permission denied\n", ... "status":6}`
- Event evidence: deploy event `Starting native TeamLab deployment from published topology.`, followed by the same shell error.
- Impact: native network creation starts but fails before runtime reaches container creation and connectivity probe.
- Server evidence:
  - `gzctf-agent.service` PID `581599` was still running from `/opt/gzctf/publish (deleted)` and `/opt/gzctf/publish/agent/gzctf-agent (deleted)` after the publish directory was replaced.
  - Agent logs show bridge and router creation succeeded; failure occurred at `ip netns exec tlr1 wg set tlwg1 private-key /run/gzctf-teamlab/tlwg1.key ...`.
  - `cd /` removed the `getcwd()` warning, proving the deleted working directory was one cause.
  - Inside `tlr1`, root could `cat /run/gzctf-teamlab/tlwg1.key`, but `wg set ... private-key <file>` failed with `openat(..., O_RDONLY) = -1 EACCES`.
  - `wg genkey | ip netns exec tlr1 wg set tlwg1 private-key /dev/stdin` succeeded, proving stdin is the reliable key delivery path for this WorkerNode.
- Root cause:
  - Deployment replaced `/opt/gzctf/publish` without restarting `gzctf-agent.service`, leaving the Agent process with a deleted cwd/exe.
  - `wg set private-key <file>` is not reliable in this node's `ip netns exec` execution context even when the file is readable by root.
- Fix:
  - `TeamLabCommandRunner` now sets `WorkingDirectory=/` for all shell commands.
  - `ConfigureWireGuardAsync` now streams the private key to `wg set ... private-key /dev/stdin` through process stdin instead of writing and reopening a key file.
- Local verification:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabCommandBuilderTests" -p:UseSharedCompilation=false -m:1` -> passed 13/13.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet" -p:UseSharedCompilation=false -m:1` -> passed 148/148.
- Next action:
  - Publish and deploy to `10.24.0.27`.
  - Restart both `gzctf.service` and `gzctf-agent.service`.
  - Re-run `POST /api/admin/teamlab/games/28/teams/29/deploy`.

### F-006 server verification - 2026-07-04 16:28 CST

- Status: Closed.
- Deployed package: `artifacts\publish-1024-teamlab-e2e-fix5`.
- Server health after deployment:
  - `gzctf.service=active`
  - `gzctf-agent.service=active`
  - `GET /` -> HTTP 200
  - `GET /api/status` -> HTTP 200
  - Agent cwd/exe are `/opt/gzctf/publish` and `/opt/gzctf/publish/agent/gzctf-agent`, no longer `(deleted)`.
- Verification:
  - Retried `POST /api/admin/teamlab/games/28/teams/29/deploy`.
  - The previous `getcwd() failed` and `fopen: Permission denied` WireGuard key error did not recur.

### F-007 - Scheduled TeamLab runtime cannot be destroyed after a failed deploy is re-planned

- Status: Fixed locally / pending server deployment verification.
- Layer: TeamLab lifecycle state machine / cleanup.
- Trigger:
  - Old failed deploy left native Linux resources on the WorkerNode: `tlr1` namespace and `tl1-network-7e9` bridge.
  - `POST /api/admin/teamlab/games/28/teams/29/plan` moved the runtime from `Failed` back to `Scheduled`.
  - `POST /api/admin/teamlab/games/28/teams/29/destroy` returned HTTP 400 `Cannot destroy TeamLab runtime from status Scheduled.`
  - Retrying deploy then failed with HTTP 400 `RTNETLINK answers: File exists`.
- Root cause:
  - The state machine allowed `Failed -> Destroying`, but not `Scheduled -> Destroying`. Re-planning a failed runtime could therefore strand residual resources behind a non-destroyable `Scheduled` runtime.
- Fix:
  - Allow `Scheduled -> Destroying` and `Stopped -> Destroying` transitions.
- Local verification:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabStateMachineTests|FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet" -p:UseSharedCompilation=false -m:1` -> passed 150/150.
- Next action:
  - Publish and deploy the state-machine fix.
  - Use platform `destroy` to clean stale native resources, then re-run plan/deploy.

### F-007 server verification - 2026-07-04 16:36 CST

- Status: Closed.
- Deployed package: `artifacts\publish-1024-teamlab-e2e-fix6`.
- Server health after deployment:
  - `gzctf.service=active`
  - `gzctf-agent.service=active`
  - `GET /` -> HTTP 200
  - Agent cwd/exe are valid under `/opt/gzctf/publish`.
- Verification:
  - `POST /api/admin/teamlab/games/28/teams/29/destroy` now returns HTTP 200 from `Scheduled`.

### F-008 - Destroy can report success while early-created native resources remain untracked

- Status: Fixed locally / pending server deployment verification.
- Layer: TeamLab destroy cleanup / early deployment failure.
- Trigger:
  - After F-007, platform `destroy` returned HTTP 200.
  - Re-plan and deploy still failed with HTTP 400 `RTNETLINK answers: File exists`.
  - Server inspection showed old `tlr1` namespace and `tl1-network-7e9` bridge still existed.
- Root cause:
  - The first failure occurred before `RecordNativeRuntimeFacts`, so `runtime.Networks` and `runtime.Assets` did not contain the bridge/router/DHCP/WireGuard names.
  - Destroy cleanup used only tracked runtime facts, so early-created bridge/router resources could be missed.
- Fix:
  - `DestroyRuntimeAsync` now merges tracked resource names with names reconstructed from the published topology and runtime id before calling Agent cleanup.
- Local verification:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet" -p:UseSharedCompilation=false -m:1` -> passed 150/150.
- Next action:
  - Publish and deploy the cleanup fallback.
  - Re-run destroy, verify no `tlr1`/`tl1-*` resources remain, then re-run deploy.

### F-008 deployment checkpoint - 2026-07-04 16:48 CST

- Deployed package: `artifacts\publish-1024-teamlab-e2e-fix7`.
- Upload target: `/tmp/publish-1024-teamlab-e2e-fix7.tar.gz`, SHA256 `4010c30b7bb6f6d349d8cbd463bf19679ca03d5ee2dbf64859875f52d991cf71`.
- Deployment preserved existing `appsettings.json`, `files`, and `keys`.
- `gzctf.service` and `gzctf-agent.service` were both restarted after replacing `/opt/gzctf/publish`.
- Health after startup:
  - `gzctf.service=active`
  - `gzctf-agent.service=active`
  - `GET /` -> HTTP 200
  - `GET /api/status` -> HTTP 200
  - `gzctf-agent` process is running from `/opt/gzctf/publish/agent/gzctf-agent`.
- Note: the first scripted health check used only a 3-second wait and hit `root_http=000`; a follow-up check after startup completed returned HTTP 200. This is startup timing, not a service failure.

### F-008 server verification - 2026-07-04 16:50 CST

- Status: Closed.
- Before cleanup:
  - `ip netns list`: no `tlr1` namespace remained.
  - `ip link show type bridge`: stale bridge `tl1-network-7e9` still existed.
- Action:
  - `POST /api/admin/teamlab/games/28/teams/29/destroy` -> HTTP 200.
  - Response message: `TeamLab runtime destroyed.`
- After cleanup:
  - `ip netns list`: no TeamLab namespace.
  - `ip link show`: no `tl1-*`, `tlr*`, `tlwg*`, or `teamlab` links.
  - `ip link show type bridge`: no TeamLab bridge.
- Agent evidence:
  - `POST /api/teamlab/cleanup` reached the Agent.
  - Agent log: `Executed 16 TeamLab network commands.`
- Conclusion: the reconstructed-name cleanup path works for early-created resources that were not persisted in runtime facts.

### F-009 - DHCP/DNS service bound dnsmasq to host bridge instead of router namespace interface

- Status: Fixed locally and deployed / pending deploy retry verification.
- Layer: TeamLab Agent DHCP/DNS configuration.
- Trigger: after F-008 cleanup, `POST /api/admin/teamlab/games/28/teams/29/deploy`.
- Raw API response: HTTP 400 with empty `message` and empty `lastError`.
- Runtime event: deploy error event with empty message.
- Agent evidence:
  - bridge, router namespace, WireGuard, and DHCP/DNS configure requests all reached the Agent.
  - `dnsmasq` printed `unknown interface tl1-network-7e9`.
  - Probe failed at `ip netns exec tlr1 nc -uz -w 2 10.180.0.1 53`.
- Root cause:
  - `dnsmasq` runs inside the router namespace, but the request passed the host bridge name (`tl1-network-7e9`) as `--interface`.
  - The router namespace only has the namespace-side veth (`tlr1n0` for the first network), so dnsmasq could not bind.
- Fix:
  - `TeamLabDhcpDnsRequest` now carries both host bridge name and router namespace interface name.
  - `TeamLabDeploymentService.BuildDhcpDnsRequests` generates the router interface name using the same `{routerNamespace}n{index}` rule as Agent router creation.
  - `TeamLabNetworkService.ConfigureDhcpDnsAsync` binds dnsmasq with `--interface={InterfaceName}`.
- Verification:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabCommandBuilderTests|FullyQualifiedName~TeamLabDeploymentServiceTests" -p:UseSharedCompilation=false -m:1` -> passed 39/39.
  - `dotnet publish src/GZCTF/GZCTF.csproj -c Release -r linux-x64 --self-contained false -p:UseAppHost=true -o artifacts\publish-1024-teamlab-e2e-fix8` -> passed.
  - Deployed `artifacts\publish-1024-teamlab-e2e-fix8` to `10.24.0.27`, restarted `gzctf.service` and `gzctf-agent.service`.
  - Health after deployment: `GET /` -> HTTP 200, `GET /api/status` -> HTTP 200.

### F-010 - Native Docker asset used registry prefix as the whole image name

- Status: Fixed locally / pending server deployment verification.
- Layer: TeamLab asset planning / native Docker deployment.
- Trigger: E2E deploy with image template ID `3` where `Name='busybox:latest'` and `RegistryUrl='docker.io/library'`.
- Raw Agent evidence: container creation tried image `docker.io/library`, then Docker attempted to pull from Docker Hub registry root and timed out.
- Root cause: `TeamLabAssetPlanService.ResolveTemplateImage` returned `ImageTemplate.RegistryUrl` directly for Docker templates. Some existing templates store Docker image data in the legacy two-field form: registry prefix in `RegistryUrl`, image name/tag in `Name`.
- Fix: TeamLab asset planning now uses `DockerImageReference.ResolvePullTarget(template.Name, template.RegistryUrl).FullImage`, matching the platform's existing image-template resolution rules.
- Regression:
  - `BuildPublishedAssetPlan_CombinesDockerRegistryPrefixWithTemplateName` first failed with actual `docker.io/library`; after fix it passes and resolves `docker.io/library/busybox:latest`.

### F-011 - Long deployment errors could overflow TeamLab event columns and turn business failure into HTTP 500

- Status: Fixed locally / pending server deployment verification.
- Layer: TeamLab deployment failure persistence.
- Trigger: Docker API error body longer than `TeamLabEvents.Message` max length.
- Raw evidence from previous deploy: `Npgsql.PostgresException: 22001: value too long for type character varying(256)` in `TeamLabDeploymentService.FailAsync`.
- Root cause: deploy failure messages were written directly to `TeamLabRuntime.LastError` and `TeamLabEvent.Message` without matching database column limits.
- Fix: deployment failure errors now normalize `LastError` to 1024 chars and split event text into bounded `Message` (256 chars) plus `Detail` (1024 chars).
- Regression:
  - `BuildRuntimeEvent_SplitsLongMessageIntoBoundedMessageAndDetail` passes.
  - `NormalizeRuntimeError_ClampsLongDatabaseErrorFields` passes.

### F-012 - TeamLab native Docker path did not resolve `gzctf-internal://` image references

- Status: Fixed locally / pending server deployment verification.
- Layer: TeamLab native Docker deployment / image template registry integration.
- Trigger: using existing ready environment templates such as ID `114` whose `RegistryUrl` is `gzctf-internal://ctf/pwn/21:latest`.
- Root cause: ordinary CTF/AWDP container paths call `DockerImageRegistryService.ResolveImageReferenceAsync` before sending image names to the Agent, but TeamLab native deployment sent the planned image directly. Docker cannot pull the `gzctf-internal://` pseudo scheme.
- Fix: TeamLab Docker container config now rewrites internal image references through the configured registry address before calling the Agent. This uses the platform's existing Docker registry settings and does not alter public Docker image names.
- Regression:
  - `BuildResolvedNativeDockerContainerConfig_RewritesInternalRegistryReferenceForAgent` passes and resolves `gzctf-internal://ctf/pwn/21:latest` to `10.24.0.28:5000/ctf/pwn/21:latest` under the server's registry config shape.

### F-013 - TeamLab deploy is blocked by missing public UDP gateway synchronization

- Status: Investigating / server E2E blocked before native asset creation.
- Layer: TeamLab public WireGuard entry / PublicUdpGatewayProvider.
- Trigger: `POST /api/admin/teamlab/games/31/teams/32/deploy` on `10.24.0.27` after publishing a minimal Docker-only topology with image template `114`.
- Raw response: HTTP 400 `Public UDP gateway synchronization is not enabled.`
- Runtime evidence:
  - Plan succeeded with runtime `3`, public UDP `32002`, worker WireGuard port `42002`, worker tunnel IP `10.24.0.27`.
  - Topology plan resolved image template `114` to `10.24.0.28:5000/ctf/pwn/21:latest`, so this is not the earlier image-resolution failure.
  - No TeamLab namespace/link residuals were present before this run.
- Server config evidence:
  - `/opt/gzctf/publish/appsettings.json` contains `ContainerProvider.PublicEntry=203.195.157.191`.
  - It contains no `PublicUdpGatewayConfig` block, so `PublicUdpGatewayConfig.Enable=false` by default.
  - `gzctf.service` and `gzctf-agent.service` run as root on `10.24.0.27`; local iptables/nftables rule mutation is possible on the main server.
- Architecture assessment:
  - The design requires a public UDP gateway fact/sync path. A TeamLab runtime must not be marked Running/open to players until the WireGuard entry is genuinely synchronized.
  - Current code only has a local shell-based `PublicUdpGatewayProvider`; it does not yet expose a `/api/internal/teamlab-udp-map` style fact source for the external public gateway to pull, unlike the existing Docker TCP `/api/internal/port-map` flow.
  - For this round's main-server-only functional test, a local iptables provider can unblock native Docker/bridge/router/WireGuard validation on `10.24.0.27`. This is a test-environment precondition, not proof that the external public gateway is wired.
- Next action:
  - Enable `PublicUdpGatewayConfig` on `10.24.0.27` in local `iptables` mode for the single-node E2E run.
  - Re-run deploy and inspect the next real failure, if any.
  - Add the external-public-gateway TeamLab UDP mapping fact source before claiming production gateway closure.

### F-013 follow-up and E2E lifecycle closure - 2026-07-04 22:59 CST

- Status: Platform fact-source gap fixed locally; single-node deployed runtime lifecycle verified.
- Added internal read-only endpoint `GET /api/internal/teamlab-udp-map`.
  - It reuses the same internal sync authorization contract as `/api/internal/port-map`: valid API token, admin session, or `ContainerProvider:NginxProxyConfig:SyncToken`.
  - It returns currently player-open TeamLab WireGuard UDP mappings with `publicUdpPort`, `workerTunnelIp`, `workerWireGuardPort`, `runtimeId`, `gameId`, `teamId`, `workerNodeId`, `ruleVersion`, `isSynced`, and `lastSyncError`.
  - It does not replace the existing TCP Docker `/api/internal/port-map`; TCP Nginx/Redis and TeamLab UDP WireGuard remain separate channels.
- Regression:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabInternalControllerTests|FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1` -> passed 124/124.
- Reset-after-deploy cleanup closure on `10.24.0.27`:
  - `POST /api/admin/teamlab/games/32/teams/33/destroy` -> HTTP 200, `TeamLab runtime destroyed.`
  - DB runtime: ID `4`, status `10` (`Destroyed`), `IsOpenToPlayers=false`, `LastError=''`.
  - DB assets: Docker, DHCP/DNS, router namespace, WireGuard, and public UDP mapping asset rows all status `10` (`Destroyed`).
  - DB public UDP mapping: `PublicUdpPort=32003`, `WorkerWireGuardPort=42003`, `IsSynced=false`, `RuleVersion=3`.
  - Host residual scan as unprivileged user showed no `tlr4`, `tl4`, or `tlwg4` netns/link matches; final sudo residual scan will run again after the next deployment.
- Production gateway note:
  - The new endpoint provides the public UDP gateway fact source.
  - Full production closure still requires the external public server gateway process to pull that endpoint, apply UDP rules, and pass a real WireGuard handshake through the public endpoint. The current 10.24.0.27 single-node test uses local `iptables` mode to validate data-plane/lifecycle behavior.

## Deployment and E2E verification - 2026-07-04 23:18 CST

- Deployed package: `artifacts\publish-1024-teamlab-e2e-final-20260704-231404.tar.gz`.
- SHA256: `528D0EF825F485688CCE1DFF44071D1A81889980B6C41BEDDF6901DB0E8DBBBA`.
- Deployment target: `10.24.0.27`.
- Post-deploy health:
  - `gzctf.service=active`.
  - `gzctf-agent.service=active`.
  - `GET /` -> HTTP 200.
  - `GET /api/status` -> HTTP 200.
  - Initial Agent heartbeat connection-refused logs occurred during the startup window only; later service health is normal.
- Local verification before deploy:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet" -p:UseSharedCompilation=false -m:1` -> passed 173/173.
  - `pnpm --dir src/GZCTF/ClientApp check` -> passed.
  - `dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false` -> passed, 0 warnings, 0 errors.
  - `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore -p:UseSharedCompilation=false` -> passed, 0 warnings, 0 errors.
  - `git diff --check` -> passed; only CRLF normalization warnings.
- Fresh E2E run on `10.24.0.27`:
  - Admin: `e2ea231631`.
  - Student: `e2es231631`.
  - Game ID: `33`.
  - Team ID: `34`.
  - Runtime ID: `5`.
  - Topology save / validate / plan / publish succeeded through normal platform APIs.
  - TeamLab plan allocated `networkPrefix=10.180.4.0/24`, public UDP `32004`, Worker WireGuard port `42004`, Worker tunnel IP `10.24.0.27`.
  - TeamLab deploy succeeded: runtime status `5` (`Running`), `IsOpenToPlayers=true`, public UDP mapping `IsSynced=true`, `RuleVersion=1`.
  - Player workspace returned only black-box challenge fields: one challenge node, one score item, no topology/attack-graph/fog/entry-target fields.
  - Player VPN config returned endpoint `203.195.157.191:32004`, client address `10.180.4.2/32`, allowed IPs `10.180.4.0/28`.
  - Wrong flag returned `accepted=false`.
  - Correct flag returned `accepted=true`, score `100`.
- Runtime network verification:
  - `/api/internal/teamlab-udp-map` without auth returned HTTP 401.
  - Same endpoint with admin session returned the active runtime mapping for `32004 -> 10.24.0.27:42004`.
  - `ip netns list` contained `tlr5`.
  - Router namespace had `tlr5n0=10.180.4.1/28` and `tlwg5=10.180.4.254/32`.
  - `ip netns exec tlr5 ping -c 2 10.180.4.3` had 0% packet loss.
  - `ip netns exec tlr5 nc -uz -w 2 10.180.4.1 53` succeeded.
  - iptables NAT had `32004 -> 10.24.0.27:42004` DNAT and matching MASQUERADE.
  - Docker asset container `215247fe2688...` was running with runtime IP `10.180.4.3`.
- Reset and destroy verification:
  - Player reset returned HTTP 200 and workspace showed `status=Running`, `resetCount=1`.
  - Router namespace could still ping `10.180.4.3` after reset.
  - Admin destroy returned HTTP 200, runtime status `10` (`Destroyed`), `IsOpenToPlayers=false`, mapping `IsSynced=false`, `RuleVersion=2`.
  - `/api/internal/teamlab-udp-map` returned `[]` after destroy.
  - Exact residual scan after destroy showed no `tlr5`/`tl5`/`tlwg5` netns/link matches, no `32004`/`42004` NAT rules, and no `dnsmasq` process for `tlr5`/`tl5`/`tldns5`.

## Final deployment refresh and smoke E2E - 2026-07-04 23:34 CST

- Deployed package: `artifacts\publish-1024-teamlab-e2e-final2-20260704-232309.tar.gz`.
- SHA256: `0F70A10D015B5FA3BC5083DB356A5E5002F0FBFE103AD14D11F629BD37F5EB1C`.
- Deployment target: `10.24.0.27`.
- Purpose:
  - Sync the `PublicUdpGatewayProvider` idempotent cleanup logging fix to the server.
  - Preserve existing server `appsettings.json`, `files`, and `keys`.
  - Keep the previous full E2E package behavior unchanged.
- Post-deploy health:
  - `gzctf.service=active`.
  - `gzctf-agent.service=active`.
  - Port `8080` is listening.
  - `GET /` -> HTTP 200.
  - `GET /api/status` -> HTTP 200.
  - `GET /api/internal/teamlab-udp-map` without auth -> HTTP 401.
  - Since the second package service start, `Public UDP gateway command failed` / `iptables: Bad rule` warning count is `0`.
- Fresh smoke E2E after the second package:
  - Admin: `e2ea233107`.
  - Student: `e2es233107`.
  - Game ID: `34`.
  - Team ID: `35`.
  - Runtime ID: `6`.
  - Topology save / validate / plan / publish succeeded through platform APIs.
  - TeamLab plan allocated `networkPrefix=10.180.5.0/24`, public UDP `32005`, Worker WireGuard port `42005`, Worker tunnel IP `10.24.0.27`.
  - TeamLab deploy succeeded: runtime status `5` (`Running`), `IsOpenToPlayers=true`, public UDP mapping `IsSynced=true`, `RuleVersion=1`.
  - Player workspace returned the black-box challenge list/detail fields only.
  - Player VPN config returned endpoint `203.195.157.191:32005`, client address `10.180.5.2/32`, allowed IPs `10.180.5.0/28`.
  - Wrong flag returned `accepted=false`.
  - Correct flag returned `accepted=true`, score `100`.
- Reset and destroy after smoke E2E:
  - Player reset returned HTTP 200 and workspace showed `status=Running`, `resetCount=1`.
  - Admin destroy returned HTTP 200.
  - DB runtime after destroy: status `10` (`Destroyed`), `IsOpenToPlayers=false`, empty `LastError`.
  - DB runtime assets after destroy all have status `10`.
  - DB public UDP mapping after destroy: `PublicUdpPort=32005`, `WorkerWireGuardPort=42005`, `IsSynced=false`, empty `LastSyncError`.
  - `/api/internal/teamlab-udp-map` returned `[]` after destroy.
  - Root-level residual scan after destroy found no `tlr6`/`tl6`/`tlwg6` netns or links, no `32005`/`42005` NAT rules, no `dnsmasq` process for runtime `6`, and no Docker container residual for `140823e0e381...`.
- Remaining production caveat:
  - This verifies the platform and single-node local iptables data-plane path on `10.24.0.27`.
  - Full public gateway closure still requires the external public UDP gateway process to pull `/api/internal/teamlab-udp-map`, apply the public UDP rules, and verify a real WireGuard handshake through `203.195.157.191`.

## Multi-segment runtime smoke - 2026-07-04 23:55 CST

- Purpose: answer whether TeamLab multi-segment runtime is actually usable, not only single-segment smoke.
- Test topology:
  - Game ID: `36`.
  - Team ID: `37`.
  - Runtime ID: `7`.
  - Two internal networks:
    - `net-edge` -> runtime CIDR `10.180.6.0/28`, gateway `10.180.6.1`, bridge `tl7-net-edge`.
    - `net-data` -> runtime CIDR `10.180.6.16/28`, gateway `10.180.6.17`, bridge `tl7-net-data`.
  - Three Docker assets:
    - `node-edge` on `net-edge`, runtime IP `10.180.6.3`.
    - `node-data` on `net-data`, runtime IP `10.180.6.19`.
    - `node-router` with two interfaces, `10.180.6.4` on `net-edge` and `10.180.6.20` on `net-data`, `reservedAdRole=Router`.
  - One RuntimeRoute policy from `net-edge` to `net-data`.
- Important validation behavior:
  - A direct RuntimeRoute between two single-interface ordinary assets was rejected by validation because no node simultaneously connected both networks. This matches the product rule: cross-segment routing needs a router/firewall/bastion-style asset.
  - After adding the dual-interface router asset, validate / plan / publish succeeded.
- Runtime evidence:
  - TeamLab deploy succeeded: runtime status `5` (`Running`), `IsOpenToPlayers=true`, UDP mapping `32006 -> 10.24.0.27:42006`, `IsSynced=true`.
  - Player workspace returned both challenge nodes and the router node.
  - Player VPN config returned endpoint `203.195.157.191:32006`.
  - Player VPN `AllowedIPs` correctly covered both segments: `10.180.6.0/28,10.180.6.16/28`.
  - Both score items accepted their static flags:
    - `Edge Web Flag` -> accepted, score `100`.
    - `Data API Flag` -> accepted, score `150`.
  - Host network facts while running:
    - `tlr7` router namespace existed.
    - `tl7-net-edge` and `tl7-net-data` Linux bridges existed.
    - Router namespace had `tlr7n0=10.180.6.1/28`, `tlr7n1=10.180.6.17/28`, `tlwg7=10.180.6.254/32`.
    - Router namespace route table included both connected CIDRs and the VPN client route.
    - Two dnsmasq processes were bound to `tlr7n0` and `tlr7n1` with per-segment DNS records.
    - NAT contained the public UDP DNAT/MASQUERADE rules for `32006` and `42006`.
- Cleanup:
  - Admin destroy returned HTTP 200.
  - DB runtime after destroy: status `10` (`Destroyed`), `IsOpenToPlayers=false`.
  - Runtime assets after destroy all had status `10`.
  - UDP mapping after destroy: `IsSynced=false`, empty `LastSyncError`.
  - `/api/internal/teamlab-udp-map` returned `[]` after destroy.
  - Root-level residual scan found no `tlr7` / `tl7-*` / `tlwg7` netns or links, no `32006` / `42006` NAT rules, no runtime dnsmasq processes, and no runtime Docker container residuals.
- Observation:
  - TeamLab Native deployment uses `TeamLabRuntimeRouteMatrix` to configure container static routes and VPN allowed IPs. In this smoke, `PenetrationRuntimeRoutes` management rows were not populated for the TeamLab runtime. This did not block data-plane deployment or player completion, but if the admin UI is expected to show persisted runtime route rows for TeamLab Native deployments, that is a separate management-plane follow-up.

## Full Linux/Docker acceptance pass - 2026-07-05 01:12 CST

### Objective

Run one fresh, normal-platform-flow TeamLab environment on `10.24.0.27` and verify the current commercial V1 data-plane requirements that do not depend on Windows/KVM:

- create a new Penetration/TeamLab game through platform APIs;
- create a student team and join the game through the normal participant flow;
- publish a three-segment topology;
- deploy Docker assets natively into TeamLab LabNetworks;
- verify runtime IPAM, bridge/router namespace, DNS, VPN config export, public UDP map fact source, player black-box workspace, flag lifecycle, reset, and destroy cleanup.

### Explicitly out of scope in this pass

- Windows/KVM VM boot and VM bridge NIC acceptance;
- multi-OS DHCP behavior;
- AD domain image initialization;
- real public WireGuard handshake from an external client through `203.195.157.191`;
- 10/50/100-team concurrency and failure-injection capacity tests.

### Acceptance topology

- `BaseCidr`: `10.190.0.0/16`
- `TeamSubnetPrefix`: `24`
- `NetworkSubnetPrefix`: `28`
- Networks:
  - `net-entry`: Entry Service LAN / DMZ
  - `net-core`: Core Business LAN / Business
  - `net-data`: Data Model LAN / Data
- Docker assets:
  - `asset-edge`: one NIC in `net-entry`, one visible score item;
  - `asset-core`: one NIC in `net-core`, one visible score item;
  - `asset-data`: one NIC in `net-data`, one visible score item;
  - `asset-router`: three NICs across all three networks, `reservedAdRole=Router`, `allowRouting=true`.
- Route policies:
  - `net-entry -> net-core`, `RuntimeRoute`;
  - `net-core -> net-data`, `RuntimeRoute`.

### Required evidence

- platform and agent services active; `/` and `/api/status` return HTTP 200;
- at least one schedulable TeamLab node is enabled and healthy;
- validate / plan / publish / TeamLab plan / deploy all return success;
- runtime has three non-overlapping `/28` networks inside one team `/24`;
- router namespace has three gateway interfaces plus WireGuard interface;
- dnsmasq is running per segment and Docker container `HostConfig.DNS` contains the segment gateway DNS;
- player VPN config endpoint uses the public UDP endpoint and `AllowedIPs` covers all three TeamLab CIDRs;
- player workspace exposes challenge nodes and score items, but does not expose topology, entry targets, attack graph, or fog fields;
- wrong flag rejected, all three expected flags accepted, scoreboard reflects the total score;
- reset keeps the runtime usable and preserves black-box workspace semantics;
- destroy clears DB open state, UDP mapping, router namespace, bridges, WireGuard links, NAT rules, dnsmasq processes, and runtime Docker containers.

### Current status

- Completed on the deployed `10.24.0.27` build after fixing a real Docker veth collision defect.

### Result

- Result: `PASS`.
- Report artifact: `artifacts/teamlab-fullaccept-report-0705015300.json`.
- Verification time: `2026-07-05 01:53 CST`.
- Server: `10.24.0.27`.
- Game ID: `42`.
- Team ID: `43`.
- Runtime ID: `13`.
- ImageTemplate used by all four Docker test assets: `114`.

### Defect Found And Fixed During This Pass

- Symptom: strict host reachability detected that `asset-edge` was marked `Running` in DB but had no `eth0` inside the container namespace.
- Root cause: TeamLab Docker host-side veth names were generated from `runtimeId + networkKey + interfaceName` and then truncated to 15 characters. Multiple assets on the same network with `eth0` collided, so attaching a later asset deleted the earlier asset veth.
- Fix: `TeamLabAssetPlanService.BuildHostInterfaceName` now derives the host veth name from `runtimeId + nodeKey + networkKey + interfaceName` using a short stable hash, keeping Linux interface names within 15 characters while making same-network `eth0` assets unique.
- Regression test added: same runtime/network/interface but different node keys produce different valid host interface names.
- Unit verification: `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --filter FullyQualifiedName~TeamLabAssetPlanServiceTests --no-restore` passed `11/11`.

### Runtime Evidence

- Platform baseline:
  - `gzctf.service=active`.
  - `gzctf-agent.service=active`.
  - `http://127.0.0.1:8080/` and platform status endpoint returned HTTP 200 during the acceptance run.
  - anonymous `/api/internal/teamlab-udp-map` returned HTTP 401.
- Published topology:
  - `BaseCidr=10.190.0.0/16`.
  - `TeamSubnetPrefix=24`.
  - `NetworkSubnetPrefix=28`.
  - validate / plan / publish / TeamLab plan / deploy all succeeded.
- Allocated runtime network:
  - Team prefix: `10.180.12.0/24`.
  - `net-entry`: `10.180.12.0/28`, gateway `10.180.12.1`, bridge `tl13-net-entry`.
  - `net-core`: `10.180.12.16/28`, gateway `10.180.12.17`, bridge `tl13-net-core`.
  - `net-data`: `10.180.12.32/28`, gateway `10.180.12.33`, bridge `tl13-net-data`.
- Public UDP / WireGuard facts:
  - Active deployment mapping: public UDP `32012` -> worker tunnel IP `10.24.0.27`, worker WireGuard port `42012`.
  - Player VPN endpoint: `203.195.157.191:32012`.
  - Player VPN client address: `10.180.12.2/32`.
  - Player VPN AllowedIPs: `10.180.12.0/28,10.180.12.16/28,10.180.12.32/28`.
- Host network facts while running:
  - router namespace existed as `tlr13`.
  - router namespace had all three gateway interfaces plus TeamLab WireGuard interface.
  - three dnsmasq processes were running, one per segment.
  - all four Docker assets were reachable from the router namespace.
  - all four Docker assets had DNS injected as `10.180.12.1`, `10.180.12.17`, `10.180.12.33`.
- Player API facts:
  - workspace returned `4` nodes and `3` visible score items.
  - workspace did not expose top-level topology, networks, interfaces, entry target, attack graph, or fog fields.
  - wrong flag was rejected.
  - all three expected flags were accepted: scores `100`, `150`, `200`.
  - scoreboard reflected team score `450` and solved count `3`.
- Reset:
  - player reset returned successfully.
  - runtime remained `Running` / open.
  - all assets remained reachable after reset.
- Destroy:
  - admin destroy returned successfully.
  - runtime status became `Destroyed`, `IsOpenToPlayers=false`.
  - public UDP mapping was unsynced and removed from the active internal map.
  - no `tlr13`, `tl13-*`, `tlwg13`, `32012`, `42012`, dnsmasq process, or runtime Docker container residual remained.

### Still Not Covered By This Pass

- Real public WireGuard handshake from an external client through `203.195.157.191`.
- Windows/KVM VM bridge NIC boot and lifecycle.
- Multi-OS DHCP behavior across Windows, Ubuntu, and domestic Linux images.
- AD domain initialization behavior inside images.
- Multi-node TeamLab scheduling after `10.24.0.30` gets KVM/network prerequisites fully ready.
- 10/50/100 team concurrency, UDP port exhaustion, node failure, public gateway sync failure, and cleanup failure injection.
- Regression of ordinary CTF Docker TCP proxy, AWDP, existing VM/Guacamole access, and Redis/Nginx TCP proxy paths.

## External public WireGuard acceptance - 2026-07-05 02:10 CST

### Objective

Close the previous gap: verify a real external WireGuard client reaches the TeamLab runtime through the public endpoint `203.195.157.191`, not only through local host or platform-side checks.

### Prepared runtime

- Server: `10.24.0.27`.
- Public gateway: `203.195.157.191`.
- Game ID: `43`.
- Team ID: `44`.
- Runtime ID: `14`.
- Report artifact: `artifacts/teamlab-fullaccept-report-0705020653.json`.
- Local client config: `artifacts/teamlab-external-client-0705020653.conf`.
- Public WireGuard endpoint: `203.195.157.191:32013`.
- Worker WireGuard endpoint: `10.24.0.27:42013`.
- Client address: `10.180.13.2/32`.
- AllowedIPs: `10.180.13.0/28,10.180.13.16/28,10.180.13.32/28`.
- Test assets:
  - entry gateway `10.180.13.3`;
  - core service `10.180.13.19`;
  - data service `10.180.13.35`.

### Evidence already collected

- Platform services on `10.24.0.27`: `gzctf.service=active`, `gzctf-agent.service=active`.
- TeamLab runtime `14` is Running and open to players.
- Platform internal map returns active mapping `32013 -> 10.24.0.27:42013`.
- `10.24.0.27` has local TeamLab NAT rules for `32013/42013` and `tlr14` contains `tlwg14` listening on UDP `42013`.
- Router namespace `tlr14` can reach all four Docker assets.
- Player workspace exposes only black-box challenge fields.
- Wrong flag was rejected; all three expected flags were accepted; scoreboard reflected `450`.
- Public server `203.195.157.191` has infra WireGuard `wg-gzctf` active with route to `10.24.0.27`.
- Public server initially had no TeamLab UDP NAT rule. Root cause: only the existing TCP/Nginx port-map sync service was present; TeamLab UDP `/api/internal/teamlab-udp-map` was not being applied on the public gateway.
- Added and enabled an independent public UDP sync service on `203.195.157.191`:
  - `/usr/local/sbin/gzctf-sync-teamlab-udp`;
  - `/usr/local/sbin/gzctf-sync-teamlab-udp-loop`;
  - `gzctf-teamlab-udp-gateway-sync.service`.
- The public server now has managed NAT rules:
  - `PREROUTING udp dport 32013 -> 10.24.0.27:42013`;
  - `POSTROUTING -d 10.24.0.27 udp dport 42013 MASQUERADE`.

### Current blocker

- Closed. The local Windows workstation could not be controlled by the Codex process because creating a tunnel service required administrator permission, but the user enabled the WireGuard tunnel manually.
- A secondary product defect was identified before the user-side import succeeded: Windows WireGuard derives the tunnel name from the `.conf` file name and rejects overly long names. The temporary file `teamlab-external-client-0705020653.conf` failed with "invalid tunnel name"; the short copy `tl14.conf` imported successfully.

### External client verification result

- Result: `PASS`.
- External client path: real Windows WireGuard client -> `203.195.157.191:32013` -> public UDP gateway NAT -> infra WireGuard `wg-gzctf` -> `10.24.0.27:42013` -> runtime namespace `tlr14/tlwg14`.
- Client ping results:
  - `10.180.13.1`: 2/2 received, 0% loss, avg 72 ms;
  - `10.180.13.3`: 2/2 received, 0% loss, avg 73 ms;
  - `10.180.13.19`: 2/2 received, 0% loss, avg 72 ms;
  - `10.180.13.35`: 2/2 received, 0% loss, avg 71 ms.
- Worker evidence:
  - `ip netns exec tlr14 wg show tlwg14` showed peer endpoint `10.250.0.1:64817`;
  - `latest handshake: 1 minute, 53 seconds ago`;
  - transfer counters: `596 B received, 380 B sent`;
  - router namespace still reached all three asset IPs.
- Public gateway evidence:
  - managed DNAT rule existed while runtime was open: `udp dpt:32013 -> 10.24.0.27:42013`;
  - managed MASQUERADE rule existed while runtime was open for `10.24.0.27:42013`;
  - infra WireGuard `wg-gzctf` was active and had a current handshake with the `10.24.0.27` peer.

### Cleanup verification

- `POST /api/admin/teamlab/games/43/teams/44/destroy` returned HTTP 200.
- Runtime `14` became `Destroyed`, `IsOpenToPlayers=false`.
- `/api/internal/teamlab-udp-map` returned `[]`.
- Worker residual scan found no `tlr14`, `tl14-*`, `tlwg14`, `32013`, `42013`, or runtime dnsmasq residuals.
- Public gateway residual scan found no managed `gzctf-teamlab-public-udp` / `32013` / `42013` NAT rules after destroy.
- `gzctf-teamlab-udp-gateway-sync.service` remained `active`.

### Product fix queued locally

- Backend `TeamLabClientConfigModel` now includes `FileName`.
- `TeamLabWireGuardService.BuildClientConfigFileName(gameId, teamId)` returns a short ASCII filename: `tl-{gameId}-{teamId}.conf`.
- Frontend VPN download uses `vpnConfig.fileName` and falls back to `tl-{gameId}-{teamId}.conf`.
- Unit regression: maximum int IDs still produce a tunnel name <= 32 characters.
- Verification:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabWireGuardServiceTests" --no-restore -p:UseSharedCompilation=false -m:1` passed 8/8.
  - `pnpm --dir src/GZCTF/ClientApp check` passed.

## Linux VM multi-network acceptance - 2026-07-05 16:10 CST

### Objective

Verify TeamLab can deploy Linux virtual machines, not Docker containers, through the standard platform image-template flow and run them across multiple TeamLab networks on `10.24.0.27`.

### Standard image-template flow

- Template display name: `TeamLab Linux VM Acceptance CirrOS 0.6.3`.
- Template ID: `116`.
- Source image on server: `/var/lib/gzctf/images/teamlab-linux-vm-accept-cirros-0.6.3.img`.
- The template was created/reused through `/api/v1/image-templates/import-local`; no direct database insertion was used.
- The template is visible in the platform environment-template/image-template list.

### Fixes made in this pass

- VM creation now passes the imported template `LocalFilePath` through the main service to the agent.
- The agent now validates that VM template paths stay under the configured image storage directory and no longer silently falls back to an empty disk.
- VM IP readiness now reports diagnostics from `domifaddr`, bridge neighbor state, and TeamLab DHCP leases.
- TeamLab dnsmasq is launched with `--user=root --group=root` so it can read static lease files under `/run/gzctf-teamlab`.
- Native TeamLab runtime opening is now gated by connectivity to every planned asset interface IP from the router namespace, not by DHCP lease/IP discovery alone. This closes the race where a VM obtained a lease but was not yet ping-reachable.

### Deployment evidence

- Published package: `artifacts/publish-1024-linux-vm-connectivity-20260705160511.tar.gz`.
- SHA256: `9E999D6E10393B26E14FED492C63148F1248F7ACF2A43CE85B8101CE99CFB31B`.
- Deployed to: `10.24.0.27`.
- Server backup created at deploy time: `/opt/gzctf/publish.backup-linux-vm-connectivity-20260705080642`.
- Health check after deployment:
  - `gzctf.service=active`.
  - `gzctf-agent.service=active`.
  - `http://127.0.0.1:8080/` returned `200`.
  - `http://127.0.0.1:8080/api/status` returned `200`.
  - `0.0.0.0:5001` and `*:8080` were listening.

### Acceptance result

- Result: `PASS`.
- Report artifact: `artifacts/teamlab-linux-vm-accept-report-0705160716.json`.
- Test game ID: `51`.
- Test team ID: `52`.
- Runtime ID: `22`.
- Cleanup before test deleted only our previous test game: `TeamLab Linux VM Acceptance 0705155352`.
- Runtime networks:
  - `net-entry`: `10.180.21.0/28`, gateway `10.180.21.1`, bridge `tl22-net-entry`.
  - `net-core`: `10.180.21.16/28`, gateway `10.180.21.17`, bridge `tl22-net-core`.
  - `net-data`: `10.180.21.32/28`, gateway `10.180.21.33`, bridge `tl22-net-data`.
- Linux VM assets:
  - `vm-entry`: `tl22-vm-entry`, `10.180.21.3`, running.
  - `vm-router`: `tl22-vm-router`, `10.180.21.4`, running.
  - `vm-core`: `tl22-vm-core`, `10.180.21.19`, running.
  - `vm-data`: `tl22-vm-data`, `10.180.21.35`, running.
- Router namespace verification:
  - `tlr22` could ping all four VM primary IPs.
  - `virsh list --all` contained all four `tl22-vm-*` domains while running.
  - Three TeamLab dnsmasq services were running, one per segment.
- Player-facing verification:
  - Student workspace exposed four score items.
  - VPN config file name was short and Windows-compatible: `tl-51-52.conf`.
- Destroy verification:
  - `POST /api/admin/teamlab/games/51/teams/52/destroy` succeeded.
  - Runtime became `Destroyed`, `IsOpenToPlayers=false`.
  - No `tlr22`, `tl22-*`, `tlwg22`, or `tl22-vm-*` residual remained after destroy.
- Post-test data cleanup:
  - `DELETE /api/Edit/Games/51` returned HTTP 200.
  - Remaining TeamLab test games matching `e2e TeamLab%`, `TeamLab Full Acceptance%`, or `TeamLab Linux VM Acceptance%`: `[]`.
  - Standard Linux VM template `116` was intentionally preserved for environment-template visibility and future reuse.

### Verification commands

- Unit tests:
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" --no-restore -p:UseSharedCompilation=false -m:1`
  - Result: `133/133` passed.
- End-to-end Linux VM acceptance:
  - `python artifacts\teamlab_linux_vm_accept_runner.py`
  - Result: `PASS`.

### Remaining gaps

- Windows VM TeamLab deployment is still out of scope for this pass.
- Multi-node VM scheduling remains blocked by the separate KVM readiness issue on `10.24.0.30`.
- External public WireGuard handshake was previously verified for Docker TeamLab; it was not repeated for this Linux VM-only pass.

## Windows VM multi-network acceptance - 2026-07-05 20:44 CST

### Objective

Continue TeamLab Windows VM acceptance through the standard platform image-template flow, using the existing Windows Server 2022 template instead of Docker assets.

### Current evidence

- Template reused through the platform image-template list:
  - ID: `117`.
  - Name: `TeamLab Windows VM Acceptance Windows Server 2022`.
  - Type: VM / Windows.
  - Local path: `/var/lib/gzctf/images/win2022-base_aaef7335.qcow2`.
- First Windows acceptance report:
  - Artifact: `artifacts/teamlab-windows-vm-accept-report-0705203832.json`.
  - Game ID: `52`.
  - Team ID: `53`.
  - Runtime ID: `23`.
  - Deploy failed before any VM was created; `virsh list --all` was empty.

### Defect found

- Status: fixed locally, pending server deployment and acceptance rerun.
- Layer: TeamLab Agent DNS/DHCP readiness probe.
- Symptom: `POST /api/admin/teamlab/games/52/teams/53/deploy` returned HTTP 400 with `TeamLab runtime operation failed.`
- Raw log evidence:
  - `TeamLab command failed with exit code 1: ip netns exec tlr23 nc -uz -w 2 10.180.22.1 53`
- Root cause:
  - `TeamLabNetworkService.ProbeDhcpDnsAsync` treated UDP `nc -uz` as a hard readiness condition. UDP port probing is not reliable for dnsmasq readiness and can return non-zero even when the following DNS resolution path would become ready shortly.
- Fix:
  - `ProbeDhcpDnsAsync` no longer emits `nc -uz`.
  - It now emits one bounded retry command that attempts `nslookup <hostname> <gateway>` up to 10 times with 1 second between attempts, then prints the final `nslookup` output if readiness still fails.
- Regression verification:
  - Red test first failed because the old command list still contained `nc -uz`.
  - After the fix, `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" --no-restore -p:UseSharedCompilation=false -m:1` passed `133/133`.

### Next action

- Publish and deploy the fixed build to `10.24.0.27`.
- Re-run `python artifacts\teamlab_windows_vm_accept_runner.py`.
- If deployment reaches VM boot and then fails, inspect `virsh`, DHCP leases, bridge neighbor state, and TeamLab Agent logs before changing readiness timeouts.

### Server verification after DNS probe fix

- Deployed package: `artifacts/publish-1024-windows-vm-dnsprobe-20260705204522.tar.gz`.
- Server health after deployment:
  - `gzctf.service=active`.
  - `gzctf-agent.service=active`.
  - `http://127.0.0.1:8080/` returned HTTP 200.
- Re-run report: `artifacts/teamlab-windows-vm-accept-report-0705204724.json`.
- Result: still failed, but the failure moved past DHCP/DNS service probing into VM readiness.
- New raw error:
  - `VM Core Windows is not ready. Current status: Pending. domifaddr-agent=<empty> | domifaddr=... | neigh:tl24-net-core:02:42:74:22:0f:b9=<empty> | No matching TeamLab lease.`
- Interpretation:
  - The DNS probe fix is validated by phase movement: deployment ran for about two minutes and created a VM before failing.
  - The next blocker is Windows VM network readiness, not router/DHCP service startup.

### Defect found: Windows TeamLab VM uses virtio NIC model unconditionally

- Status: fixed locally, pending server deployment and acceptance rerun.
- Layer: TeamLab asset plan -> Agent VM creation.
- Root cause:
  - `TeamLabAssetPlanService.ToVmInterfaceRequest` hard-coded every TeamLab VM NIC to `virtio`.
  - The Windows Server 2022 acceptance template did not obtain DHCP on that NIC model, producing empty `domifaddr`, empty bridge neighbor state, and empty dnsmasq leases until the readiness window expired.
- Fix:
  - `TeamLabAssetSpec` now carries `OSType` from the selected `ImageTemplate`.
  - `BuildNativeVmRequest` passes `spec.OSType` into VM interface conversion.
  - Linux TeamLab VMs keep `virtio`; Windows TeamLab VMs use `e1000e`.
- Regression verification:
  - Red test first showed the Windows VM request still produced `virtio`.
  - After the fix:
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~BuildNativeVmRequest" --no-restore -p:UseSharedCompilation=false -m:1` passed `2/2`.
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" --no-restore -p:UseSharedCompilation=false -m:1` passed `134/134`.

### Windows VM readiness root cause - 2026-07-05 21:31 CST

- Status: fixed locally, pending server deployment and full Windows VM acceptance rerun.
- Latest failed acceptance artifact:
  - `artifacts/teamlab-windows-vm-accept-report-0705210431.json`.
  - Game ID: `54`, team ID: `55`, runtime ID: `25`.
  - Failure: `VM Core Windows is not ready. Current status: Pending... No matching TeamLab lease.`
- Direct retained-VM diagnostics:
  - Diagnostic script: `artifacts/teamlab_windows_vm_direct_diagnostic.py`.
  - Report without DHCP service: `artifacts/teamlab-windows-vm-direct-diagnostic-0705211415.json`.
    - The Agent-created VM domain XML used `<model type='e1000e'/>`, so the platform request path and libvirt conversion were correct.
    - The VM stayed running and attached to the requested bridge, but there was no lease source in that synthetic setup.
  - Report with a minimal dnsmasq lease source: `artifacts/teamlab-windows-vm-direct-diagnostic-0705212125.json`.
    - The same Windows image obtained `10.250.0.3` on `e1000e`.
    - `dnsmasq` first observed `DHCPDISCOVER` at `13:24:17`, about 171 seconds after dnsmasq startup at `13:21:26`.
    - The VM became ready on probe 17 with 10-second diagnostic polling.
- Root cause:
  - The Windows Server 2022 template is valid and can DHCP on `e1000e`, but its cold boot reaches network initialization after roughly 170 seconds.
  - The TeamLab production readiness loop allowed only `24 * 5s`, about 120 seconds, so Windows VM deployment was cleaned up before the guest could request DHCP.
- Fix:
  - Added `TeamLabDeploymentService.ResolveNativeVmReadyProbeAttempts(OSType)`.
  - Linux/default VMs keep the existing 24 attempts.
  - Windows VMs use 72 attempts at the existing 5-second interval, giving a bounded 6-minute readiness window.
  - The debug log now reports the OS-specific maximum attempt count.
- Regression verification:
  - Red test first failed because `ResolveNativeVmReadyProbeAttempts` did not exist.
  - After the fix:
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~ResolveNativeVmReadyProbeAttempts" --no-restore -p:UseSharedCompilation=false -m:1` passed `2/2`.
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" --no-restore -p:UseSharedCompilation=false -m:1` passed `136/136`.

### Next action

- Publish and deploy the OS-specific VM readiness build to `10.24.0.27`.
- Re-run `python artifacts\teamlab_windows_vm_accept_runner.py`.
- If full Windows topology still fails, collect retained VM evidence for multi-NIC/router behavior before changing the deployment algorithm.

### Long deploy request cancellation defect - 2026-07-05 21:43 CST

- Status: fixed locally, pending final deployment and acceptance rerun.
- Evidence:
  - After the 6-minute Windows readiness fix was deployed, `python artifacts\teamlab_windows_vm_accept_runner.py` failed at the client layer with `Read timed out (read timeout=180)`.
  - Server state showed runtime `26` still in status `Deploying`, with no runtime asset records yet and `tl26-vm-core` still running in `virsh list --all`.
  - Logs showed the deployment was still probing `tl26-vm-core` at `27/72`.
- Root cause:
  - `TeamLabAdminController.Deploy` passed the HTTP request cancellation token directly into the long-running deployment service.
  - When the browser/script client timed out or disconnected, deployment was cancelled mid-flow before `FailNativeDeploymentAsync` could perform normal cleanup.
- Fix:
  - `TeamLabAdminController.Deploy` now uses an operation token linked to `IHostApplicationLifetime.ApplicationStopping`, not to client disconnect.
  - The operation token deliberately has no fixed controller-level timeout. Each VM already has an OS-specific bounded readiness window, and full topology duration scales with the number and type of VM assets.
  - Plan, destroy, and events endpoints still use request cancellation because they are short operations or explicit cleanup calls.
- Regression verification:
  - Red test first failed because `CreateDeployOperationToken` did not exist.
  - After the fix:
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~CreateDeployOperationToken|FullyQualifiedName~TeamLabAdminControllerTests" --no-restore -p:UseSharedCompilation=false -m:1` passed `5/5`.
- A temporary 10-minute controller-level timeout was tested and rejected:
  - It allowed the request to survive client disconnects, but a four-VM Windows topology exceeded 10 minutes while sequentially booting VMs.
  - Runtime `27` had already recorded `core` and `data`, and `entry` had a DHCP lease, proving the fixed total timeout was the wrong boundary.
  - The timeout was removed and `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" --no-restore -p:UseSharedCompilation=false -m:1` passed `137/137`.

### Next action

- Deploy the controller cancellation fix without a fixed total operation timeout.
- Re-run Windows VM acceptance with a client timeout high enough for all sequential Windows VM boots.
- If the topology reaches post-VM connectivity probing and then fails, validate whether Windows guest firewall blocks ICMP before changing TeamLab network logic.

### Windows VM post-deploy ICMP probe false failure - 2026-07-05 22:25 CST

- Status: fixed locally, pending publish/deploy and rerun.
- Failed acceptance artifact:
  - `artifacts/teamlab-windows-vm-accept-report-0705220651.json`.
  - Game ID: `57`, team ID: `58`, runtime ID: `28`.
- Evidence:
  - Four Windows VMs were created successfully through the standard VM template flow.
  - `tl28-vm-core`, `tl28-vm-data`, `tl28-vm-entry`, and `tl28-vm-router` all reached VM readiness.
  - `dnsmasq` leases existed across all three runtime networks:
    - `10.180.27.3` for `vm-entry`.
    - `10.180.27.19` for `vm-core`.
    - `10.180.27.35` for `vm-data`.
    - `10.180.27.4`, `10.180.27.20`, and `10.180.27.36` for the three router NICs.
  - Runtime asset rows were already recorded with `Status=Running` for the VM assets.
  - The final deploy endpoint returned HTTP 400 only after the generic post-deploy probe ran `ip netns exec tlr28 ping -c 1 -W 2 10.180.27.19`.
- Root cause:
  - The post-deploy runtime connectivity probe treated every asset IP as an ICMP target.
  - Windows Server 2022 can obtain DHCP and is reachable at L2/L3 from the runtime network, but its default firewall does not reply to ICMP echo.
  - This made a successfully deployed Windows VM topology look failed and triggered cleanup.
- Fix:
  - `TeamLabDeploymentService.BuildNativeProbeTargets` now excludes Windows VM interfaces from generic ICMP post-deploy probes.
  - Docker assets and Linux VM assets are still included in the post-deploy connectivity probe.
  - Windows VM correctness remains enforced by the earlier OS-specific readiness stage, which validates VM status plus matching DHCP/lease IP for the planned interface.
- Regression verification:
  - Red test:
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~BuildNativeProbeTargets" --no-restore -p:UseSharedCompilation=false -m:1` failed because Windows VM IPs were still included.
  - After the fix:
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~BuildNativeProbeTargets" --no-restore -p:UseSharedCompilation=false -m:1` passed `1/1`.
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" --no-restore -p:UseSharedCompilation=false -m:1` passed `137/137`.

### Next action

- Publish and deploy the Windows post-probe fix to `10.24.0.27`.
- Rerun `python artifacts\teamlab_windows_vm_accept_runner.py`.

### Windows-only topology without ICMP probe target - 2026-07-05 22:43 CST

- Status: fixed locally, pending publish/deploy and acceptance rerun.
- Failed acceptance artifact:
  - `artifacts/teamlab-windows-vm-accept-report-0705222825.json`.
  - Game ID: `58`, team ID: `59`, runtime ID: `29`.
- Evidence:
  - Four Windows VMs were created successfully and reached VM readiness:
    - `tl29-vm-core`.
    - `tl29-vm-data`.
    - `tl29-vm-entry`.
    - `tl29-vm-router`.
  - Three runtime networks were created:
    - `net-entry`: `10.180.28.0/28`, gateway `10.180.28.1`.
    - `net-core`: `10.180.28.16/28`, gateway `10.180.28.17`.
    - `net-data`: `10.180.28.32/28`, gateway `10.180.28.33`.
  - DHCP leases matched the planned VM addresses:
    - `vm-entry`: `10.180.28.3`.
    - `vm-core`: `10.180.28.19`.
    - `vm-data`: `10.180.28.35`.
  - Public UDP mapping was synced:
    - `publicUdpPort`: `32000`.
    - `workerWireGuardPort`: `42000`.
    - `workerTunnelIp`: `10.24.0.27`.
  - Deployment still returned HTTP 400 with:
    - `TeamLab probe target is unavailable in the native asset plan.`
- Root cause:
  - The previous Windows ICMP false-failure fix correctly removed Windows VM interfaces from `BuildNativeProbeTargets`.
  - For a topology containing only Windows VMs, this leaves no ICMP-compatible post-deploy probe target.
  - The deployment flow still treated an empty probe target list as a fatal plan error, even though Windows VM readiness had already validated VM running state plus DHCP/lease IP.
- Fix:
  - Added `TeamLabDeploymentService.ShouldRunNativeConnectivityProbe`.
  - Docker and Linux VM topologies still require the existing ICMP runtime connectivity probe.
  - Windows-only VM topologies now skip the final ICMP probe after DHCP readiness and record an explicit runtime event.
  - Empty/invalid published plans are still rejected earlier by asset plan validation, so this does not allow an empty topology to run.
- Regression verification:
  - Red test:
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~ShouldRunNativeConnectivityProbe" --no-restore -p:UseSharedCompilation=false -m:1` initially failed because the helper did not exist.
  - After the fix:
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~ShouldRunNativeConnectivityProbe|FullyQualifiedName~BuildNativeProbeTargets" --no-restore -p:UseSharedCompilation=false -m:1` passed `3/3`.
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab" --no-restore -p:UseSharedCompilation=false -m:1` passed `139/139`.

### Next action

- Publish and deploy the Windows-only probe policy fix to `10.24.0.27`.
- Rerun `python artifacts\teamlab_windows_vm_accept_runner.py`.
- If post-deploy platform status reaches `Running` but the local acceptance script fails on Windows ICMP pings, adjust the script acceptance rule to validate Windows VM runtime via DB status, DHCP leases, `virsh` running state, and VPN material rather than ICMP echo.

### Windows VM multi-network acceptance result - 2026-07-05 23:18 CST

- Status: passed on deployed server `10.24.0.27`.
- Deployed build:
  - `artifacts/publish-1024-windows-vm-no-probe-target-fix-20260705224730.tar.gz`.
  - `gzctf.service` active.
  - `gzctf-agent.service` active.
  - `http://127.0.0.1:8080/` returned HTTP 200 during deploy health check.
- Full deployment evidence:
  - First rerun artifact: `artifacts/teamlab-windows-vm-accept-report-0705224927.json`.
    - Runtime `30` reached `status=5`, `isOpenToPlayers=true`.
    - Public UDP mapping synced: `32000 -> 10.24.0.27:42000`.
    - The old acceptance script then failed only on `router to Windows VM ping`, which is expected for Windows guest firewall behavior.
  - Second rerun artifact: `artifacts/teamlab-windows-vm-accept-report-0705230305.json`.
    - Runtime `31` reached `status=5`, `isOpenToPlayers=true`.
    - Three runtime networks were created:
      - `net-entry`: `10.180.30.0/28`, gateway `10.180.30.1`.
      - `net-core`: `10.180.30.16/28`, gateway `10.180.30.17`.
      - `net-data`: `10.180.30.32/28`, gateway `10.180.30.33`.
    - Four Windows VMs were running:
      - `tl31-vm-entry`: `10.180.30.3`.
      - `tl31-vm-core`: `10.180.30.19`.
      - `tl31-vm-data`: `10.180.30.35`.
      - `tl31-vm-router`: `10.180.30.4` plus secondary router NIC leases on `10.180.30.20` and `10.180.30.36`.
    - The script then failed because the new DHCP check incorrectly looked for leases in the `dnsmasq` process command line instead of the real dnsmasq files.
- Acceptance script correction:
  - `artifacts/teamlab_windows_vm_accept_runner.py` now reads:
    - `/run/gzctf-teamlab/tldns{runtimeId}*/dhcp-hosts`.
    - `/run/gzctf-teamlab/tldns{runtimeId}*/leases`.
  - Windows runtime verification no longer uses ICMP.
  - It validates:
    - runtime `Running` and open to players.
    - 3 runtime networks.
    - 4 running VM assets.
    - each VM is present in `virsh list --all`.
    - each VM IP falls inside its runtime network CIDR.
    - each VM MAC/IP is present in DHCP host/lease evidence.
    - router namespace has gateway addresses and routes for each runtime network.
    - player workspace exposes 4 score items.
    - VPN config returns a valid `.conf` filename.
- Final targeted verification:
  - Existing runtime `31` was verified and destroyed with the corrected script.
  - Report: `artifacts/teamlab-windows-vm-accept-report-0705231728.json`.
  - Result:
    - `verify Windows VM runtime`: passed.
    - `destroy Windows VM runtime`: passed.

### Current conclusion

- Windows VM TeamLab deployment through the standard VM template flow is working on `10.24.0.27`.
- Multi-network allocation, dnsmasq DHCP/DNS, WireGuard/public UDP mapping, runtime DB state, player workspace, and destroy cleanup all passed acceptance for Windows VM assets.
- Windows guest ICMP is intentionally not an acceptance requirement.

### Topology line rendering and action button fix - 2026-07-06

- Issue:
  - The TeamLab admin topology canvas showed nodes and network zones, but no connecting lines.
  - After stopping deployed environments, deployment action buttons could remain in an unusable or misleading state.
- Root cause:
  - `PenetrationService.AddEdgesToConfig` used `SourceId` / `TargetId` first to resolve visual node endpoints.
  - For Network-scoped route policies, `SourceId` / `TargetId` are network IDs, so node lookup failed and `SourceNodeId` / `TargetNodeId` were persisted as `0`.
  - The ReactFlow canvas only rendered edges with valid endpoint IDs, so saved Network-scoped route policies had no drawable line.
- Fix:
  - Preserve visual node endpoints independently from policy scope IDs when saving `PenetrationEdge`.
  - Add frontend fallback rendering for older saved Network-scoped edges whose visual node endpoints were already lost.
  - Add hidden handles to network zone nodes so fallback Network-to-Network rendering can create a valid ReactFlow path.
  - Gate deploy/cancel/stop buttons by deployment status: cancel only while deploying, stop only while running/partial, deploy available again after stop/failure/publish.
- Regression verification:
  - Added `PenetrationServiceTopologyMappingTests.ApplyModelToConfig_PreservesVisualNodeEndpointsForNetworkScopedRouteEdges`.
  - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~PenetrationServiceTopologyMappingTests|FullyQualifiedName~TeamLabPublishedTopologyServiceTests|FullyQualifiedName~BuildRuntimeRouteMatrix" --no-restore -p:UseSharedCompilation=false -m:1` passed `5/5`.
  - `pnpm check` passed in `src/GZCTF/ClientApp`.

### Player VPN route isolation fix - 2026-07-06 18:14 +08:00

- Issue:
  - A player connected through WireGuard could directly access every runtime subnet, for example entry 10.180.33.3, app 10.180.33.19, and data 10.180.33.35.
  - This violated the intended multi-level penetration model where the player should enter only the entry subnet and pivot through compromised/router assets for later subnets.
- Root cause:
  - Native TeamLab deployment generated WireGuard client AllowedIPs from all plan.Networks CIDRs.
  - Router namespace enabled IP forwarding, so player traffic to every runtime CIDR was routable immediately.
- Fix:
  - Added TeamLabDeploymentService.BuildPlayerNetworkAccess.
  - Player VPN now exposes only the entry network: first IsEntry network, falling back to the first topology network by OrderIndex when legacy TeamLab snapshots have no entry flag.
  - Non-entry runtime CIDRs are passed to the agent as blocked player CIDRs.
  - Agent configures namespace-local FORWARD ACL rules on the WireGuard interface: allow entry CIDR, reject non-entry CIDRs.
  - WorkerNode TeamLab health now requires iptables; one-click node registration installs iptables with base packages.
- Verification:
  - dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabCommandBuilderTests.ConfigureWireGuardAsync_DryRunBuildsPeerCommand|FullyQualifiedName~TeamLabDeploymentServiceTests.BuildPlayerNetworkAccess" --no-restore -p:UseSharedCompilation=false -m:1 passed 3/3.
  - dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabCommandBuilderTests|FullyQualifiedName~TeamLabDeploymentServiceTests|FullyQualifiedName~TeamLabAssetPlanServiceTests|FullyQualifiedName~TeamLabWireGuardServiceTests|FullyQualifiedName~TeamLabPlanServiceTests|FullyQualifiedName~PenetrationServiceTopologyMappingTests" --no-restore -p:UseSharedCompilation=false -m:1 passed 98/98.
- Next:
  - Run frontend type check, publish, deploy to 10.24.0.27, restart the current TeamLab runtime, then verify from player client:
    - entry subnet URL is reachable.
    - non-entry subnet URLs are not directly reachable from the player client.
    - non-entry subnets remain reachable from the intended pivot/router asset path.

### Capacity release concurrency fix after runtime destroy - 2026-07-06 19:23 +08:00

- Issue:
  - Destroying the current runtime through `POST /api/admin/teamlab/games/61/teams/1/destroy` returned HTTP 500.
  - Runtime resources were physically cleaned: containers and namespace were removed and runtime `34` became closed/cleaned, but the API failed at the final capacity-release step.
- Root cause:
  - `FleetCapacityReservationService.ReleaseAsync` updated `WorkerNode.CurrentContainers` / `CurrentVms` through a tracked EF entity with an `xmin` concurrency token.
  - A concurrent WorkerNode update, such as heartbeat or node metric refresh, can change the same row between load and save.
  - Release is an idempotent cleanup-side operation, but the old implementation propagated `DbUpdateConcurrencyException`, so cleanup success still surfaced as an internal server error.
- Fix:
  - `ReleaseAsync` now normalizes slot counts, skips zero releases, and retries up to three times on `DbUpdateConcurrencyException`.
  - Each retry clears tracked state and reloads the node, then reapplies clamped counter release.
  - Missing nodes remain a no-op because there is no remaining capacity counter to restore.
- Regression verification:
  - Added `FleetCapacityReservationServiceTests.ReleaseAsync_RetriesTrackedReleaseAfterConcurrencyConflict`.
  - Red test first failed with `DbUpdateConcurrencyException : simulated capacity counter conflict`.
  - After fix:
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~ReleaseAsync_RetriesTrackedReleaseAfterConcurrencyConflict" --no-restore -p:UseSharedCompilation=false -m:1` passed `1/1`.
    - `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~FleetCapacityReservationServiceTests|FullyQualifiedName~DeploymentQueueServiceTests|FullyQualifiedName~FleetVmServiceTests|FullyQualifiedName~TeamLabDeploymentServiceTests|FullyQualifiedName~TeamLabCommandBuilderTests|FullyQualifiedName~TeamLabAssetPlanServiceTests|FullyQualifiedName~TeamLabWireGuardServiceTests|FullyQualifiedName~TeamLabPlanServiceTests|FullyQualifiedName~PenetrationServiceTopologyMappingTests" --no-restore -p:UseSharedCompilation=false -m:1` passed `110/110`.
- Next:
  - Run frontend check, publish, deploy to `10.24.0.27`, then re-run destroy/deploy and verify WireGuard entry-only route exposure plus namespace ACL rules.

### Deployment and post-deploy isolation verification - 2026-07-06 19:36 +08:00

- Deployed package:
  - Local publish directory: `artifacts/publish-1024-teamlab-isolation-capacity-20260706`.
  - Uploaded archive: `/tmp/publish-1024-teamlab-isolation-capacity-20260706.tar.gz`.
  - SHA256 on server: `5711592fb87850a7a1863dfcabcff0b4ab4ad1eca434f206d2b6179ca7e8dce0`.
- Server health after deployment:
  - `gzctf.service`: active.
  - `gzctf-agent.service`: active.
  - `http://127.0.0.1:8080/`: HTTP 200.
  - `10.24.0.27:8080`: reachable from the WireGuard client.
- TeamLab redeploy verification:
  - Admin account used for API verification: `tlcha06164303`.
  - `POST /api/admin/teamlab/games/61/teams/1/deploy` returned HTTP 200 and `success=true`.
  - Runtime `34` reused and reached:
    - `Status=5`.
    - `IsOpenToPlayers=true`.
    - `NetworkPrefix=10.180.33.0/24`.
    - public UDP mapping `203.195.157.191:32003 -> 10.24.0.27:42003`.
- Player route exposure verification:
  - Runtime networks:
    - `net-edge`: `10.180.33.0/28`.
    - `net-app`: `10.180.33.16/28`.
    - `net-data`: `10.180.33.32/28`.
    - `net-ops`: `10.180.33.48/28`.
  - Player VPN peer `AllowedIPs`: `10.180.33.0/28` only.
  - Verified that `net-app`, `net-data`, and `net-ops` do not appear in the player peer route list.
- Router namespace ACL verification:
  - `ip netns exec tlr34 iptables -S FORWARD` contains:
    - `ACCEPT` from `tlwg34` to `10.180.33.0/28`.
    - `REJECT` from `tlwg34` to `10.180.33.16/28`.
    - `REJECT` from `tlwg34` to `10.180.33.32/28`.
    - `REJECT` from `tlwg34` to `10.180.33.48/28`.
  - `ip netns exec tlr34 wg show tlwg34` shows server listen port `42003` and player peer `allowed ips: 10.180.33.2/32`.
- Current conclusion:
  - The previous direct access defect is fixed for newly deployed/redeployed runtime `34`: the player config only routes the entry subnet, and namespace ACL rejects manual player routes to non-entry subnets.
  - The prior destroy-side capacity-release 500 is covered by tests and should no longer surface as an API failure when WorkerNode heartbeat/metrics update the same row concurrently.
