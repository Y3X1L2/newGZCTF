# TeamLab 多节点 Fabric 开发进度

更新时间：2026-07-08

当前基线：
- 分支：main
- 基线提交：d076df6b feat: finalize TeamLab mixed CIDR deployment flow
- 当前目标：实现 TeamLab L3 路由型多节点 Fabric、分片运行时、流量观测、VM 剪贴板与编排管理端成熟化。
- 执行原则：按大模块闭环开发；阶段内运行单元测试和静态检查；阶段末统一验收，避免小粒度反复红绿灯。

## 架构约束

- 一个队伍环境仍对应一个 `TeamLabRuntime`。
- 一个 `TeamLabRuntime` 可拆成多个 `TeamLabRuntimeShard`，分布到多个 WorkerNode。
- 一个网段默认归属一个 WorkerNode；同一网段内资产默认同节点部署。
- 跨节点只做 L3 路由型 Fabric，首版不做 VXLAN/OVS 大二层。
- 不做端口级 ACL。
- 不恢复攻击图、迷雾、题目拓扑、公网入口目标等旧设计。
- 公网服务器只承担选手 WireGuard UDP 入口映射，不参与内部东西向流量。
- WorkerNode 通过中心 Hub 路由加入 TeamLab Fabric。
- `TeamLabRuntime.WorkerNodeId` 仅作为主 shard/兼容展示字段，真实归属以 shard/network/asset 的 `WorkerNodeId` 为准。

## Phase 1：管理端编排结构优化

状态：基础完成，仍需后续 UI 细节打磨。

已完成：
- 保留现有画布能力，没有重写编辑器。
- 左侧任务区改为“拓扑设计”，动作收敛为：添加网段、添加资产、连接访问路径、一键生成、校验发布。
- 右侧任务区改为：资产配置、连通关系、发布与运行。
- “发布与运行”已抽出为 `TeamLabRuntimeObservability.tsx`，不继续把运行观测堆进 `Penetration.tsx`。
- 运行区展示队伍环境、shard/WorkerNode、网段/资产事实、抓包任务、兼容节点、路由和部署事件。

待后续审查：
- 前端文案仍需逐页做最终清理，但当前没有恢复攻击图/迷雾/公网入口目标。

## Phase 2：节点版本、能力与 Fabric 基础

状态：后端完成，前端节点卡片已展示关键能力。

已完成：
- WorkerNode 记录 Agent 版本、TeamLab 协议版本、Fabric IP/状态、工具能力摘要。
- Agent `/api/teamlab/status` 返回 AgentVersion、ProtocolVersion=2，以及 Docker/KVM/WireGuard/tcpdump/dumpcap/nft/iptables 能力。
- 心跳持久化 Agent/Fabric/能力摘要。
- 节点能力不足时不会进入 TeamLab 多节点调度候选。
- 节点管理卡片展示 TeamLab 隧道状态、隧道地址、握手时间、配置版本、端口池和资源容量。

## Phase 3：多节点分片调度算法

状态：后端完成。

已完成：
- 新增 `TeamLabRuntimeShard`。
- `TeamLabRuntimeNetwork` / `TeamLabRuntimeAsset` 记录 `ShardId` 和 `WorkerNodeId`。
- `TeamLabShardPlanner` 以网段为最小放置单元，支持单节点、多节点拆分、单网段资源超限失败。
- `FleetCapacityReservationService.TryReserveBatchAsync` 支持批量原子预留。
- TeamLab 队列按 runtime shard 聚合容量，不再强制单节点 gate。
- `TeamLabPlanService` 可从发布快照生成 shard/network/asset 计划事实；旧 scheduled runtime 缺 shard 时会重新规划。

## Phase 4：多节点部署、重置、销毁闭环

状态：后端主链路完成，需在真实多 WorkerNode 环境继续做端到端验收。

已完成：
- 部署按 shard 分发 bridge/router/DHCP/asset/probe 到对应 WorkerNode。
- WireGuard 只配置在 entry shard 节点。
- 部署流程已调用 `/api/teamlab/fabric/apply`，按 shard route plan 幂等应用跨节点 L3 路由。
- shard 记录 `RouteVersion`，运行区可展示。
- asset runtime facts 补齐 `WorkerNodeId`。
- 销毁按 shard/network/asset 的 WorkerNodeId 分组清理容器、VM 和网络资源。
- 清理由已记录事实和发布计划双来源构造资源名，降低半失败残留风险。

待真实环境验收：
- 两个及以上 WorkerNode 的三网段环境：入口网段在节点 A，业务/数据网段在节点 B。
- 选手 WireGuard 只能直达入口网段；通过路由节点后访问下一层；未连线网段不可直连。
- 重置后 IP、DNS、flag、路由、玩家入口保持语义一致。
- 销毁后所有节点无 TeamLab bridge、namespace、route、capture 进程和临时文件残留。

## Phase 5：流量观测与 VM 剪贴板

状态：平台与 Agent 基础完成；PCAP 按需抓包、下载闭环和默认轻量流量元数据采集已接入；仍需真实多节点环境验证采集进程、bridge 点位和销毁残留。

已完成：
- Agent 新增 Fabric/capture API：
  - `POST /api/teamlab/fabric/apply`
  - `POST /api/teamlab/capture/start`
  - `POST /api/teamlab/capture/stop`
  - `POST /api/teamlab/capture/status`
  - `GET /api/teamlab/capture/{runtimeId}/{jobId}/download`
- Agent capture 文件固定在 `/run/gzctf-teamlab/capture-{runtimeId}-{jobId}/capture.pcap`。
- 平台 `TeamLabTrafficCaptureService` 已接入创建 job、定位 shard/network WorkerNode、调用 Agent、更新状态、写事件。
- `TeamLabAdminController` 已接入 list/start/stop/status/download。
- 前端运行区已展示抓包任务，并支持开启、停止、刷新和下载 PCAP。
- VM 剪贴板/Guacamole 生命周期清理已接入 `GuacamoleService` 与 `FleetVmService`。
- Agent 新增默认轻量流量元数据 API：
  - `POST /api/teamlab/flows/start`
  - `POST /api/teamlab/flows/stop`
  - `POST /api/teamlab/flows/snapshot`
- 默认元数据 collector 使用 `tcpdump -l -tttt -nn -q` 在 TeamLab bridge 上采样文本摘要，不做透明代理，不改变扫描、UDP、ICMP、TLS 或漏洞利用链路。
- collector 文件固定在 `/run/gzctf-teamlab/flow-{runtimeId}-{networkKey}/flow.log`，PID 固定在同目录 `flow.pid`；启动命令与 PID 写入在同一 shell 中完成，停止/销毁可精确清理。
- 平台新增 `TeamLabTrafficFlowService`，负责启动/停止 collector、拉取 snapshot、解析入库到 `TeamLabTrafficFlow`，并限制单 runtime 存储样本数量。
- 部署流程在记录 shard/network 运行事实后自动启动每个网段的 flow collector；销毁流程先停止 collector，再清理 bridge、router、DHCP/DNS、VM/Docker 等资源。
- 管理端运行区新增“刷新元数据”和最近五元组/协议/字节/时间摘要展示；刷新由管理员显式触发，不引入不可观测后台轮询。
- PCAP 下载目前采用平台到 Agent 的安全代理，不暴露 WorkerNode 本地路径。

待真实环境验收：
- 在 2 个 WorkerNode 上确认每个 TeamLab bridge 均存在对应 `flow.pid` 和 `flow.log`。
- 选手通过 WireGuard 访问入口网段、经路由节点访问下一层后，管理端刷新元数据能看到对应五元组。
- 销毁后所有节点无 `flow-*` 采集进程残留。

## 当前验证记录

- 2026-07-08 节点注册依赖修复：
  - 10.24.0.31 现场状态：Docker/.NET/Agent 已运行；`virsh`、`virt-install`、`qemu-img`、`wg`、`genisoimage/xorriso/cloud-localds` 缺失。
  - 10.24.0.31 KVM 硬件状态：系统运行在 KVM VM 内，内核存在 `kvm*.ko` 模块，但 `/proc/cpuinfo` 无 `vmx/svm`，`/dev/kvm` 不存在；需要外层宿主启用 nested virtualization/CPU passthrough 后才能承载 VM 资产。
  - 修复：一键注册 bootstrap 的 apt KVM 包从 `qemu-kvm` 改为 Ubuntu 26.04 可安装的 `qemu-system-x86`，不再吞掉 apt KVM 安装失败；自动安装 `wireguard-tools`、`nftables`、`tcpdump`、`genisoimage`、`xorriso`、`cloud-image-utils`、`dnsmasq-base` 等 TeamLab VPN/抓包/cloud-init seed ISO 依赖。
  - 修复：Agent TeamLab 能力报告新增 `kvmDevice` 与 `cpuVirtualization`，用于区分 libvirt 工具存在和宿主是否真正暴露 KVM；TeamLab 网络可用性仍只由 `ip`、`wg`、`iptables/nftables` 判断。
  - 修复：远程一键注册和本地节点注册的 KVM 能力判定必须同时满足 `/dev/kvm`、CPU `vmx/svm` flag、`virsh -c qemu:///system list`，避免 10.24.0.31 这类“libvirt 可装但未开启嵌套虚拟化”的节点被误标为 KVM 可调度。
  - 同步：`scripts/prepare-agent-node.sh` 与 `docs/node-deployment/setup-gzctf-worker-node.sh` 已补齐同类依赖和状态输出。
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~NodesControllerTests.BuildBootstrapScript_InstallsDistributedDependencies|FullyQualifiedName~NodesControllerTests.BuildKvmCapabilityCheckScript_RequiresHardwareVirtualizationAndLibvirt|FullyQualifiedName~TeamLabCommandBuilderTests.GetStatusAsync_ReturnsVersionsAndToolCapabilities" -p:UseSharedCompilation=false -m:1`
  - 结果：3/3 通过。
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~NodesControllerTests|FullyQualifiedName~TeamLabCommandBuilderTests" -p:UseSharedCompilation=false -m:1`
  - 结果：61/61 通过。
- `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore -p:UseSharedCompilation=false`
  - 结果：通过，0 警告/0 错误。
- `dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false`
  - 结果：通过，0 警告/0 错误。
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabTrafficCaptureServiceTests.DownloadCaptureAsync|FullyQualifiedName~TeamLabCommandBuilderTests.ResolveCaptureFilePath"`
  - 结果：4/4 通过。
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabAdminControllerTests|FullyQualifiedName~TeamLabTrafficCaptureServiceTests|FullyQualifiedName~TeamLabEnvironmentProjectionTests"`
  - 结果：12/12 通过。
- `pnpm exec tsc -p tsconfig.app.json --noEmit`
  - 结果：通过。
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLab"`
  - 结果：217/217 通过。
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~FleetCapacityReservationServiceTests|FullyQualifiedName~DeploymentQueueManagerTests|FullyQualifiedName~DeploymentQueueServiceTests|FullyQualifiedName~NodesControllerTests|FullyQualifiedName~WeightedSchedulerTests|FullyQualifiedName~FleetVmServiceTests|FullyQualifiedName~GuacamoleServiceTests"`
  - 结果：79/79 通过。
- `dotnet build src/GZCTF/GZCTF.csproj --no-restore`
  - 结果：通过。
- `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore`
  - 结果：通过。
- 2026-07-08 真实部署验收阻断修复：
  - 现象：E2E runtime 51 规划为 3 个 shard 后部署失败，Agent 返回 `argument "tl51-net-data" is wrong: Device does not exist`。
  - 根因：`TeamLabShardPlanner` 之前按“单网段包含所有连接资产”建 placement group，多网卡路由资产会同时进入多个 shard；部署时该资产需要挂载所有接口，导致某个节点尝试 attach 到远端 shard 才存在的 bridge。
  - 修复：多网卡资产作为放置约束，将其涉及的所有网段合并为同一个 placement group；最终 shard 的 network keys 和 asset keys 去重，保证单个资产只归属一个 shard，且资产所有接口所需 bridge 都在本地 shard 内。
  - 新增回归：`TeamLabShardPlannerTests.PlanShards_KeepsMultiInterfaceAssetsOnOneShardWithTheirNetworks`，覆盖 entry/data 双网卡 router 不得跨 shard 重复放置。
  - 红灯验证：旧实现下该测试失败，`router` 同时出现在两个匹配 shard。
  - 绿灯验证：`dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabShardPlannerTests.PlanShards_KeepsMultiInterfaceAssetsOnOneShardWithTheirNetworks" -p:UseSharedCompilation=false -m:1`
    - 结果：1/1 通过。
  - 关键回归：`dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabShardPlannerTests|FullyQualifiedName~TeamLabPlanServiceTests.PlanRuntimeAsync_PersistsShardPlanAndUsesEntryShardAsCompatibilityNode|FullyQualifiedName~TeamLabPlanServiceTests.PlanRuntimeAsync_RebuildsScheduledRuntimeWhenShardPlanIsMissing|FullyQualifiedName~DeploymentQueueManagerTests.ProcessPendingAsync_ReservesTeamLabTicketAcrossRuntimeShards|FullyQualifiedName~TeamLabDeploymentServiceTests.TryReserveTeamLabCapacityAsync_ReservesShardSlotsOnTheirWorkerNodes|FullyQualifiedName~TeamLabDeploymentServiceTests.DeployQueuedRuntimeAsync_DeploysEachShardOnItsPlannedWorkerNode" -p:UseSharedCompilation=false -m:1`
    - 结果：10/10 通过。

## 当前风险

- 真实多节点 Fabric 端到端仍需要部署环境验收；静态和单元测试无法替代实际路由、WG、bridge、namespace 和抓包进程验证。
- 默认流量元数据采集已完成代码闭环，但仍需真实 WorkerNode 验证 `tcpdump` 权限、bridge 采样点和销毁残留。
- `result.txt` 是未跟踪临时文件，不纳入提交。

## 2026-07-08 调度能力语义与 27/31 验收准备

本轮根因：真实 E2E 脚本期望多节点 shard，但实际 runtime 53 被规划为单 shard 并落到 10.24.0.30。问题不是部署接口本身失败，而是能力语义和验收条件不一致：
- `TeamLabShardPlanner` 曾临时使用 `CanHostTeamLab` 过滤候选节点，该语义要求 Docker+KVM 同时具备，不适合作为纯 Docker shard 的候选过滤。
- 正确语义应分层：Fabric/数据面健康是 TeamLab 基础能力；Docker shard 只要求 Docker+Fabric；VM shard 才要求 KVM+Fabric。
- 旧验收脚本把 10.24.0.30 纳入本轮 27/31 验收，并把每节点容器上限设为 4；当前拓扑只有 4 个 Docker 资产，单节点容量足够时调度器按设计会生成单 shard。

已修复：
- `WeightedScheduler` 新增 `CanHostTeamLabFabric`、`CanHostTeamLabDocker`、`CanHostTeamLabVm` 和 `GetTeamLabAssetHostUnschedulableReason`，保留旧 `CanHostTeamLab` 作为 Docker+KVM 完整能力兼容语义。
- `TeamLabShardPlanner` 候选节点改为先过滤 TeamLab Fabric/数据面健康，再在 `CanPlace` 中按资产类型判断 Docker/KVM 能力和容量。
- 节点 API 返回 `canHostTeamLabFabric`、`canHostTeamLabDocker`、`canHostTeamLabVm`，并在 `unschedulableByCapability` / `schedulableCapabilities` 中区分 TeamLabNetwork、TeamLabDocker、TeamLabVm。
- 节点卡片前端使用分层能力判断 TeamLab 状态，避免 Docker-only 但 TeamLab Docker 可用的节点被展示成完全不可用。
- `artifacts/teamlab_multinode_accept_runner.py` 限定本轮验收节点为 10.24.0.27 和 10.24.0.31；将 MaxContainers 强制为 3，使 4 个 Docker 资产必须拆分到至少 2 个 shard；脚本健康判断使用分层能力字段；Docker 资产数量断言修正为 4。

验证：
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabShardPlannerTests|FullyQualifiedName~WeightedSchedulerTests|FullyQualifiedName~NodesControllerTests" -p:UseSharedCompilation=false -m:1`
  - 结果：57/57 通过。
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1`
  - 结果：222/222 通过。
- `pnpm exec tsc -p tsconfig.app.json --noEmit`
  - 结果：通过。

下一步：发布部署到 10.24.0.27，确认 27/31 agent 健康后运行 `artifacts/teamlab_multinode_accept_runner.py`，目标是覆盖多 shard 分布、host 资源、跨节点路由、选手黑盒视图、flag 提交、flow metadata、PCAP、reset/destroy 清理。

## 2026-07-08 Fabric namespace uplink 修复

真实 E2E 失败点：`runtime 54` 多节点部署已经生成 2 个 shard，宿主 root namespace 中存在跨节点 `ip route replace <remote-cidr> via <remote-fabric-ip>`，但队伍 router namespace `tlr54` 内执行 `ping 10.180.53.51` 返回 `Network is unreachable`。

根因确认：部署流程调用 `/api/teamlab/fabric/apply` 时只把远端 shard CIDR 下发为宿主路由；`CreateTeamLabRouterAsync` 创建的 `tlrXX` namespace 没有到宿主 root namespace 的 Fabric 出口。因此 namespace 内跨节点流量无法进入宿主 Fabric 路由表。

修复内容：
- `TeamLabFabricApplyRequest` 扩展并保持旧 host-only 调用兼容：`NamespaceName`、`NamespaceHostAddressCidr`、`NamespacePeerAddressCidr`、`LocalRoutes`、`Routes`。
- Agent `ApplyFabricAsync` 在存在 `NamespaceName` 时创建/重建 `tlrf{runtimeId}` <-> `tlrf{runtimeId}n` veth uplink。
- uplink 使用 `169.254.0.0/16` 内按 runtime 派生的 `/30` 地址；宿主侧 `.1/30`，namespace 侧 `.2/30`，路由命令显式绑定 `dev`。
- 宿主 root namespace：本 shard 本地 CIDR 走 namespace peer；远端 CIDR 继续走远端 Worker Fabric IP。
- router namespace：远端 CIDR 走宿主 uplink 地址，解决 namespace 内 `Network is unreachable`。
- cleanup 资源名补充 `tlrf{runtimeId}`，删除 host-side veth 时连带清理 namespace peer。
- 部署服务下发完整 Fabric request，包含本地回程路由和远端 namespace 出口路由。

本地验证：
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabCommandBuilderTests.ApplyFabricAsync_DryRunBuildsNamespaceUplinkAndRoutes" -p:UseSharedCompilation=false -m:1`：1/1 通过。
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabCommandBuilderTests|FullyQualifiedName~TeamLabDeploymentServiceTests.DeployQueuedRuntimeAsync_DeploysEachShardOnItsPlannedWorkerNode|FullyQualifiedName~TeamLabDeploymentServiceTests.BuildNativeCleanupResourceNames" -p:UseSharedCompilation=false -m:1`：36/36 通过。
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1`：223/223 通过。
- `pnpm exec tsc -p tsconfig.app.json --noEmit`：通过。
- `git diff --check`：仅换行格式提示，无 whitespace error。

下一步：发布到 10.24.0.27 后重新运行 `artifacts/teamlab_multinode_accept_runner.py`，重点观察 cross-node reachability、player workspace、VPN entry-only、flag 提交、flow metadata、PCAP、reset/destroy cleanup。

## 2026-07-08 Protocol v3 与部署前回归

真实 E2E runtime 55 继续失败的根因已经收敛为 10.24.0.31 仍运行旧 Agent：10.24.0.27 已创建 `tlrf55` namespace uplink 和本地/远端路由，但 10.24.0.31 不存在 `tlrf55`，说明远端 shard 未应用 namespace uplink 能力。

修复策略：
- Agent TeamLab protocol version 从 v2 提升到 v3，v3 表示支持 Fabric namespace uplink。
- 调度器拒绝 `TeamLabProtocolVersion < 3` 的节点参与 TeamLab 调度，避免旧 Agent 被误判为可用。
- 新增/更新协议兼容测试，确认 protocol v2 节点会给出明确不可调度原因。

部署前验证：
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~WeightedSchedulerTests|FullyQualifiedName~NodesControllerTests|FullyQualifiedName~TeamLabCommandBuilderTests.GetStatusAsync_ReturnsVersionsAndToolCapabilities" -p:UseSharedCompilation=false -m:1`
  - 结果：52/52 通过。
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1`
  - 结果：224/224 通过。
- `pnpm exec tsc -p tsconfig.app.json --noEmit`（工作目录 `src/GZCTF/ClientApp`）
  - 结果：通过。
- `git diff --check`
  - 结果：仅 CRLF/LF 提示，无 whitespace error。

下一步：
- 重新发布主平台到 10.24.0.27。
- 同步更新 10.24.0.31 的 `gzctf-agent`，确认 `/api/teamlab/status` 返回 protocol v3。
- 运行真实多节点 E2E，覆盖跨节点路由、选手黑盒视图、flag 提交、flow metadata、PCAP、reset 和 destroy cleanup。

## 2026-07-08 Cross-node packet loss root cause

E2E runtime 56 在 27/31 均升级到 protocol v3 后，失败形态从 `Network is unreachable` 变为跨节点 ping `100% packet loss`：
- runtime 56 已拆为 2 个 shard。
- 27/31 均已创建 `tlrf56` namespace uplink。
- 但两台节点的 uplink 均使用同一组 `169.254.0.225/30` 与 `169.254.0.226/30`。
- 从 router namespace 主动发起跨节点探测时，Linux 会选择 uplink 地址作为源地址，远端节点没有正确回程语义。

根因：
- uplink `/30` 地址只按 `runtimeId` 派生，未包含 shard/worker 维度，导致同一 runtime 的不同 worker 使用重复 link-local 地址。
- namespace 到远端 CIDR 的路由未指定本 shard 的业务网关源地址，跨节点从 router namespace 发起的健康探测和管理面验证容易走 169.254 源地址，回程不可达。

修复：
- `TeamLabStaticRouteRequest` 增加可选 `SourceIp`。
- `TeamLabDeploymentService` 按 `runtimeId + worker ordinal` 为每个 shard 派生唯一 `/30` uplink 地址。
- namespace 远端路由增加 `src <local gateway ip>`，优先使用 entry 网络网关，否则使用 topologyKey 排序后的第一个本地网络网关。
- Agent `ApplyFabricAsync` 校验 `SourceIp` 并在 namespace 路由命令中输出 `src` 子句。

验证：
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabCommandBuilderTests.ApplyFabricAsync_DryRunBuildsNamespaceUplinkAndRoutes|FullyQualifiedName~TeamLabDeploymentServiceTests.DeployQueuedRuntimeAsync_DeploysEachShardOnItsPlannedWorkerNode" -p:UseSharedCompilation=false -m:1`
  - 结果：2/2 通过。
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1`
  - 结果：224/224 通过。
- `pnpm exec tsc -p tsconfig.app.json --noEmit`（工作目录 `src/GZCTF/ClientApp`）
  - 结果：通过。

下一步：重新发布 10.24.0.27 和 10.24.0.31 Agent，重跑真实 E2E，确认 cross-node reachability 进入后续 player workspace / flag / flow / PCAP / reset / destroy 验收阶段。

## 2026-07-08 Host FORWARD policy fix

重新发布 route-src 后，runtime 57 仍出现跨节点 ping `100% packet loss`。现场抓包显示：
- 10.24.0.31 的 `tlrf57` 能看到 `192.168.77.1 > 10.180.56.51` 的 ICMP echo request。
- 10.24.0.27 没看到对应报文。
- 10.24.0.31 宿主 `FORWARD` policy 为 `DROP`，且只有 Docker/Libvirt 链，无 TeamLab Fabric 放行规则。

根因确认：
- 跨节点报文已经从 router namespace 进入 Worker root namespace。
- 但 Worker 宿主默认 FORWARD DROP，TeamLab Fabric 没有写入专用放行规则，导致报文无法从 `tlrfXX` 转发到 Worker 间 Fabric/内网接口。

修复：
- Agent `ApplyFabricAsync` 幂等创建 `TEAMLAB-FABRIC` 链。
- 幂等将 `TEAMLAB-FABRIC` 插入 `FORWARD` 链头部。
- 使用 `-m comment --comment gzctf-teamlab-runtime-<runtimeId>` 标记 runtime 规则。
- 按当前 shard 的 local/remote routes 放行 `tlrf<runtimeId>` 进出方向。
- `CleanupAsync` 增加按 runtime comment 删除 `TEAMLAB-FABRIC` 规则，避免运行时规则残留。

验证：
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabCommandBuilderTests.ApplyFabricAsync_DryRunBuildsNamespaceUplinkAndRoutes|FullyQualifiedName~TeamLabCommandBuilderTests.CleanupAsync_DryRunRemovesRuntimeFabricForwardRules" -p:UseSharedCompilation=false -m:1`
  - 结果：2/2 通过。
- `dotnet test src\GZCTF.Test\GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLab" -p:UseSharedCompilation=false -m:1`
  - 结果：225/225 通过。
- `pnpm exec tsc -p tsconfig.app.json --noEmit`（工作目录 `src/GZCTF/ClientApp`）
  - 结果：通过。

下一步：发布包含 `TEAMLAB-FABRIC` ACL 的新包，重新运行真实 E2E。
## 2026-07-08 fabric-forward-acl deployment status

当前状态：
- 已构建发布包：`artifacts\publish-10240027-teamlab-multinode-20260708-fabric-forward-acl.tar.gz`。
- 首次部署 10.24.0.27 在 staging 解压阶段失败，错误为 `tar: ./agent/gzctf-agent: Wrote only ... bytes`。
- 根因确认：10.24.0.27 根分区 100% 满，历史 `/opt/gzctf/publish.backup*` 发布备份堆积；首次失败发生在替换 `/opt/gzctf/publish` 前，未覆盖线上发布目录。
- 已恢复 10.24.0.27 服务：`gzctf.service=active`、`gzctf-agent.service=active`、`http://127.0.0.1:8080/` 返回 200。
- 已清理旧发布备份，仅保留最近 5 份 `publish.backup*`，根分区从 100% 降至约 80%，剩余约 24G。
- 已重新部署 10.24.0.27 主平台和本机 Agent，发布后 HTTP 200。
- 已同步 10.24.0.31 Worker Agent 到同一发布包内的 `gzctf-agent`，`sha256=27a6eecee6778154216bd515c6b7390516d479fcf77bfaa0a849f8a1f1b14941`，服务 active，心跳返回 200。

已验证：
- 10.24.0.27：`gzctf.service=active`、`gzctf-agent.service=active`、平台根路径 HTTP 200、根分区约 81%。
- 10.24.0.31：`gzctf-agent.service=active`，二进制 hash 与本地发布包一致，根分区约 8%。
- 两台 Agent 本地未带 token 调用 `/api/teamlab/status` 返回 401 `Invalid auth token`，符合接口鉴权预期；Agent heartbeat 已确认 200。

下一步：
- 运行 `artifacts/teamlab_multinode_accept_runner.py` 做真实多节点 E2E。
- 验收重点：多 shard 分布、跨节点 L3 reachability、玩家黑盒工作区、VPN 只暴露入口网段、flag 提交、flow metadata、PCAP 下载、reset、destroy cleanup。
