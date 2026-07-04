# TeamLab full-chain test progress

## Scope

Target server: `10.24.0.27`

Objective: verify the currently deployed TeamLab / VPN / VM multi-segment module end to end, collect every error with trigger path and raw evidence, fix newly found base-function defects first, then create and validate a Docker-only TeamLab test range through the normal platform template/orchestration flow.

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
