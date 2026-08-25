# TeamLab 商业化组网底座 —— 复审与开发进度

> 本文件只记录**已核实事实**、finding 状态、开发批次、测试证据和阻塞项。不写流水账。
> 任务书：`docs/commercialization/reviews/teamlab-commercial-foundation-agent-brief.md`

---

## 1. 基线事实（第一手核实）

| 项 | 值 | 核实方式 |
| --- | --- | --- |
| HEAD commit | `23b614b5c08c6b4c9fda148ad101cdf74ac2221e` | `git rev-parse HEAD` |
| 分支 | `codex/phase-09-teamlab-networking` | `git rev-parse --abbrev-ref HEAD` |
| 工作区 | 干净（仅任务书本身未跟踪） | `git status --short` |
| 与任务书参考提交关系 | **完全一致**，代码未在任务书编写后继续演进 | 对比 commit hash |
| 最近 TeamLab 提交 | `23b614b` feat: complete TeamLab vNext orchestration controls | `git log` |
| 迁移总数 | 98 个（不含 Designer/Snapshot） | `ls src/GZCTF/Migrations` |
| 代码索引 | 1978 文件 / 39936 节点 / 106175 边（CodeGraph） | `codegraph_status` |

### 1.1 证据分级声明

本轮审查**只能提供代码级证据**。任务书 §2 的证据优先级中，第 1-4 级（真实运行行为、
基础设施事实、数据库/队列/事件当前状态、发布产物）需要双 Worker 环境，尚未执行。
因此本文件中所有结论按下列标记区分，**不得混用**：

- `[代码可证]` —— 读代码即可确定的逻辑缺陷、竞态窗口、缺失约束。
- `[需运行验证]` —— 依赖真实 Linux 网络事实、时序或节点行为才能确认。
- `[已运行验证]` —— 已在多节点环境取得证据（本轮暂无）。

**旧审查报告状态一律不继承。** `docs/commercialization/phase-09-*.md` 与
`docs/commercialization/reviews/phase-09-*.md` 中标记为"已修复/已关闭"的条目，
本轮全部重新按当前代码核验。

---

## 2. 模块清单与规模（已核实）

### 2.1 主服务控制面 `src/GZCTF/Modules/TeamLab`（103 个 C# 文件）

| 分层 | 关键文件 | 行数 |
| --- | --- | --- |
| Application | `TeamLabShardDeploymentService.cs` | 608 |
| Application | `TeamLabRuntimePlanner.cs` | 596 |
| Application | `TeamLabRuntimeOrchestrator.cs` | 534 |
| Application | `TeamLabAccessGrantService.cs` | 322 |
| Application | `TeamLabRuntimeCleanupService.cs` | 305 |
| Application | `TeamLabFabricLinkAllocator.cs` | 129 |
| Infrastructure | `AgentTeamLabNodeExecutor.cs` | 1192 |
| Domain | `Runtime/TeamLabRuntimeInfrastructure.cs` | 84 |
| Domain | `TeamLabNetworkLease.cs` | 15 |

其余分层构成：`Api` 6 个 controller（`TeamLabAdminRuntimeController` 54 符号最大）、
`Contracts` 8 个、`Domain` 20 个、`Infrastructure/Persistence` 11 个 EF 配置。

### 2.2 Agent 数据面 `src/GZCTF.Agent`

| 文件 | 大小/行数 | 职责 |
| --- | --- | --- |
| `Services/KvmService.cs` | 1281 行 | VM domain 生命周期 |
| `Services/TeamLabNetworkService.cs` | 1266 行 | bridge/ns/veth/TAP/dnsmasq/route |
| `Services/DockerService.cs` | 1140 行 | 容器生命周期 |
| `Services/TeamLab/TeamLabFabricService.cs` | 23 KB | WireGuard Fabric |
| `Services/TeamLab/TeamLabFirewallService.cs` | 16 KB | nftables/iptables |
| `Services/TeamLab/TeamLabContainerNetworkFinalizeService.cs` | 11 KB | 容器入网收尾 |
| `Services/TeamLab/TeamLabBridgeService.cs` | 7.6 KB | bridge 管理 |
| `Services/TeamLab/TeamLabFabricRouteStore.cs` | 4.8 KB | Fabric 路由持久化 |
| `Services/TeamLab/TeamLabRouterService.cs` | 3.3 KB | 路由器 namespace |
| `Services/TeamLab/TeamLabRuntimeGenerationStore.cs` | 2.9 KB | generation fencing |
| `Services/TeamLab/TeamLabCommandExecutor.cs` | 1.5 KB | 命令封装 |
| `Services/AgentOperationGate.cs` | 51 行 | 并发预算 |
| `Services/ImageTransferSingleFlight.cs` | 24 行 | 镜像传输去重 |

GuestControl：`GuestEnrollmentStore.cs` (23 KB)、`GuestCertificateAuthority.cs` (11.9 KB)、
`GuestConfigDriveBuilder.cs` (11 KB)、`GuestManagementNetworkService.cs` (3.8 KB)、
`GuestEventIngestor.cs` (2.6 KB)。

**规模观察**：`AgentOperationGate` 仅 51 行、`ImageTransferSingleFlight` 仅 24 行，
与它们承担的"Docker/VM/网络/镜像传输四类独立并发预算"（任务书 §5.3）职责严重不匹配，
已列为重点审查对象。

---

## 3. 网络配置基线与容量天花板 `[代码可证]`

来源：`src/GZCTF/Models/Internal/Configs.cs:598-623`

```
Enable                      = false   （默认关闭 WorkerNode 网络变更）
DryRun                      = false
RuntimeNetworkBaseCidr      = 10.180.0.0/16
FabricLinkPool              = 100.64.0.0/16
TeamSubnetPrefixLength      = 24
PublicUdpPortStart/End      = 32000 / 32999
WorkerWireGuardPortStart/End= 42000 / 42999
BridgePrefix                = "tl"
RouterNamespacePrefix       = "tlr"
WireGuardInterfacePrefix    = "tlwg"
RecoveryGraceSeconds        = 30
EnableStatelessAutoRecovery = false
```

### 3.1 由配置直接推算出的规模上限

| 资源池 | 容量 | 约束来源 | 商业化影响 |
| --- | --- | --- | --- |
| 队伍网段 | **256 个 /24** | `10.180.0.0/16` ÷ `/24` | 一个 3 网段拓扑占 3 个 → **并发 runtime ≈ 85** |
| Fabric /30 链路 | 16384 条 | `100.64.0.0/16` ÷ `/30` | 充裕 |
| 玩家公网 UDP 入口 | **1000 个** | `32000-32999` | **并发 runtime 硬上限 1000** |
| Worker WireGuard 端口 | 1000 个 | `42000-42999` | 每节点 1000，充裕 |

**结论**：三个池子的容量模型互不匹配（85 vs 16384 vs 1000），说明未做统一推算。
队伍网段池是**最紧的瓶颈**，且 `/16 + /24` 的组合无法通过配置调优突破一个数量级。
这是商业化规模的结构性约束，已列入开发批次评估。

### 3.2 默认安全姿态（正面事实）

`Enable = false` + `DryRun` + `EnableStatelessAutoRecovery = false` 表明数据面变更
默认关闭、自动恢复默认关闭，符合任务书 §4.9「自动恢复必须先证明差异」的保守姿态。

---

## 4. TeamLab 相关迁移清单（已核实）

按时间顺序，反映底座演进路径：

| 迁移 | 说明 |
| --- | --- |
| `20260703075914_AddTeamLabNetworkControlPlane` | 组网控制面建表 |
| `20260703081025_AddWorkerNodeTeamLabNetworkFields` | 节点网络字段 |
| `20260703130700_AddTeamLabVpnPeerSecrets` | VPN peer 秘密 |
| `20260703155323_AddTeamLabRuntimeAssetFacts` | 资产事实 |
| `20260703164737_AddTeamLabRuntimeAssetInterfaceSummary` | 网卡摘要 |
| `20260707155640_AddTeamLabMultinodeFabricRuntime` | 多节点 Fabric |
| `20260711144502_AddIndependentTeamLabFoundation` | **底座独立化** |
| `20260711170329_RemovePenetrationTopologyRuntimeCompatibility` | **移除 Penetration 兼容层** |
| `20260712053756_PersistTeamLabFlowCursor` | 流量游标持久化 |
| `20260712054103_CompleteTeamLabRuntimeReliability` | 可靠性 |
| `20260713014106/014659/015237_*PhaseSixRuntimeSchedulingConcurrency` | 调度并发（expand/backfill/contract 三段式） |
| `20260713152015_HardenPhaseSevenRuntimeIdentity` | 运行身份加固 |
| `20260714021514_HardenExternalTeamLabApiContract` | 外部 API 契约 |
| `20260714091222/091420_*PhaseNineTeamLabNetworking` | 组网（expand/backfill） |
| `20260714170850_AddTeamLabAssetExecutionState` | 资产执行状态 |
| `20260714180834_AddTeamLabTrafficEvidencePersistence` | 流量证据持久化 |
| `20260725093155_AddTeamLabRuntimeCreationIdempotency` | **创建幂等** |
| `20260725110234_AddTeamLabRollouts` | **Rollout（最新）** |

**正面事实**：Phase 6 与 Phase 9 均采用 expand/backfill/contract 三段式迁移，
符合在线迁移最佳实践。

---

## 5. 已独立核实的边界残留 `[代码可证]`

### 5.1 `UsePenetrationFabric` 遗留命名仍在活跃分支判断中

违反任务书 §4.16「不保留无价值兼容层、废弃数据库字段或重复状态源」。

事实链：
- `src/GZCTF.Agent/Models/ContainerModels.cs:29` —— 字段仍存在于 Agent 契约。
- `src/GZCTF.Agent/Services/DockerService.cs:34` —— `fabricManagementNetwork = request.UsePenetrationFabric && request.PublishPort`
- `src/GZCTF.Agent/Services/DockerService.cs:35` —— `isolatedHostNetwork = request.UseHostNetworkNone || request.UsePenetrationFabric`
- `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs:521` —— TeamLab 已显式置 `UsePenetrationFabric = false`
- `src/GZCTF/Models/Internal/ContainerConfig.cs:128` —— 平台侧字段仍在
- `src/GZCTF/Services/Container/Manager/KubernetesManager.cs:10` —— 仍实现 `IPenetrationFabricManager`

即：TeamLab 已迁移到 `UseHostNetworkNone`，但 `UsePenetrationFabric` 仍作为
**第二条并行的网络模式判定路径**存在于 Agent 的 Docker 分支中，构成重复状态源。
`20260711170329_RemovePenetrationTopologyRuntimeCompatibility` 只清理了数据库，
未清理 Agent 契约与命名。

---

## 6. 审查执行状态

| 维度（任务书 §5） | 状态 | 确认 finding |
| --- | --- | --- |
| 5.1 架构分层与模块边界 | ✅ 完成（补跑） | 7 |
| 5.2 拓扑校验、发布与场景库 | ✅ 完成 | 6 |
| 5.3 调度、容量与高并发 | ✅ 完成（补跑） | 7 |
| 5.4 Fabric/路由/DHCP/DNS/玩家入口【核心】 | ✅ 完成 | 7（含 1 × P0） |
| 5.5 Docker / Linux VM / Windows VM | ✅ 完成（补跑） | 7（含 **2 × P0**） |
| 5.6 镜像存储、预分发与清理 | ✅ **完成并修复已确认缺陷** | 8 |
| 5.7 Runtime/Rollout 生命周期与 generation fencing | ✅ 完成 | 4 |
| 5.8 可观测性、审计与恢复 | ✅ 完成（补跑） | 7 |
| 5.9 权限、安全与多租户隔离 | ✅ 完成 | 6（含 1 × P0） |
| 5.10 前端架构、表现与流畅度 | ✅ 完成 | 7 |
| 外部 API 契约与底座独立性 | ✅ 完成并修复生命周期绕过与核心反向依赖 | 2 |
| Agent 数据面幂等性与命令封装 | ✅ 完成 | 6 |
| **VM 启动链路延迟专项**（用户报告） | ✅ 完成 | 7 |

**审查完整度：13 / 13 维度。** 镜像链路已完成专项复核；Open API 已核对三个
`OpenTeamLab*Controller` 的 scope、资源级授权、幂等操作、rollout 生命周期边界和底座依赖。
真实双 Worker 故障注入仍属于运行验收，不等同于代码审查缺口。

审查方法：每维度独立审查 → 逐条对抗性验证（默认假设 finding 不成立，
只有实际读码确认可触发才保留）→ 汇总排序。被推翻的 finding 记入 §8 以免重复调查。

---

## 7. Finding 台账

> 待审查完成后填入。格式：严重级别 / 触发条件 / 代码位置 / 运行影响 / 根因 / 最小修复方向 / 验证方法 / 证据等级。

### 7.1 已确认

#### F-001 [P2] 节点清单集合 null 处理不一致，Agent 部分响应导致部署期 NRE ✅ 已修复

- **证据等级**：`[代码可证]`
- **位置**：`src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs:50-52`
- **触发条件**：Agent 返回的 runtime inventory JSON 省略 `containers` 或 `vms` 字段
  （空响应体、部分响应、协议版本不匹配、反序列化为 null），或整体响应为 null。
- **根因**：三个集合的 null 处理不一致——作者为 `TeamLabResources` 写了 `?? []`，
  证明他知道集合可能为 null，但 `Containers` 与 `Vms` 未加保护：

  ```csharp
  inventory.Containers.Select(Map).ToArray(),                 // 无保护
  inventory.Vms.Select(Map).ToArray(),                        // 无保护
  (inventory.TeamLabResources ?? []).Select(Map).ToArray(),   // 有保护
  ```

- **运行影响**：`TeamLabShardDeploymentService.VerifyRuntimeInventoryAsync:208` 解引用时抛
  `NullReferenceException`，表现为**不可解释的 500**——无错误码、无 category、
  无 retryable 标志、无 correlation ID。违反 §4.13「每个自动动作必须有错误码和可查询状态」
  与 §5.8「稳定机器错误码」。一次 Agent 抖动被呈现为不可诊断故障而非可重试失败。
  影响范围限于发起部署的该 runtime，不波及其他比赛或普通实例。
- **修复**：区分两种语义——**整体响应为 null 是协议故障**，抛
  `TeamLabRuntimeExecutionException` 并带 WorkerNode 标识；**单个集合为 null 是空事实**，
  按 `?? []` 处理。若把协议故障也当作空清单，部署验证会去指责资产缺失，
  而真正原因是节点不可达，这会误导排障方向。
- **验证**：主服务 Release 构建 0 错误；需补一个「Agent 返回省略字段的清单」的契约测试
  （见 §10.2 覆盖缺口）。

#### F-002 [P2] `VmGuestAgentService` 每次 QGA RPC 启动一个 virsh 进程 ✅ 部分修复

详见 §8.3 与 §8.3.1。stage 填充已改为单次 tar 传输；libvirt 连接复用、Windows 批量化、
stage/target 双写、payload 走 stdin、artifact 改挂载盘 等 5 项未实施，见 §8.3.2。

#### F-003 [P2] `RebootAndWaitAsync` 用「ping 必须失败」推断重启，快速重启反而超时硬失败

详见 §8.4。未修复。

#### F-004 [P1] 基线测试本就不是绿的：`DeployAsync_ScenarioArtifactUsesResolvedTemplateAndSkipsPublishBootstrap` 失败

- **证据等级**：`[代码可证]`（已用 `git stash` 在干净 HEAD 上复现）
- **位置**：`src/GZCTF.Test/UnitTests/TeamLab/TeamLabDeploymentOrchestrationTests.cs:156`
- **事实**：该测试在**未施加本轮任何改动的干净 HEAD 上同样失败**（0 通过 / 1 失败），
  因此不是回归。同套件其余 159 个测试通过。
- **根因**：该测试未对 `executor.GetRuntimeInventoryAsync` 建立 Moq 桩
  （同文件 205/274/303/343 行的其他测试都建立了），松散模式返回 null，
  于 `VerifyRuntimeInventoryAsync:208` 触发 NRE。属测试装配缺口，
  但它同时暴露了 F-001 的生产侧 null 风险。
- **影响**：任务书 §9.1 要求 TeamLab 测试通过，当前基线**不满足**该门槛。
- **修复** ✅：为该测试补上缺失的 inventory 桩，返回一个与其创建的资产相匹配的清单
  （VM `StableName = "scenario-vm"`、generation 取 `runtime.Generation`、state `running`），
  使部署走完验证阶段并抵达原有的 scenario 模板断言。
  **刻意没有**在生产代码里加 null 容忍来让它变绿——那会掩盖 F-001 要区分的协议故障语义。
- **验证** ✅：TeamLab 套件 **160 / 160 通过**。
- **印证 F-001 的分层判断**：F-001 的生产侧修复**并未**让本测试变绿，因为该测试直接
  mock `ITeamLabNodeExecutor` 接口，绕过了被修的具体实现 `AgentTeamLabNodeExecutor`。
  二者是不同层面的缺陷，这一事实反过来确认了 F-001 不是测试假象。

---

## 7A 独立审查结果（13 维度 + 逐条对抗性验证）

### 7A.0 统计与方法

13 个维度并行独立审查，每条 finding 交给一个**独立怀疑者** agent 做对抗性验证
（默认判定不成立，只有实际读码确认可触发才保留）。

| 结果 | 数量 |
| --- | --- |
| 已确认（通过对抗性验证） | **43** |
| 已推翻 | 6 |
| 未做对抗性验证（每维度只验证前 7 条） | 21 |

确认项按级别：**P0 × 2、P1 × 14、P2 × 18、P3 × 9**。
按维度：fabric-routing 7、vm-boot-latency 7、frontend 7、security-tenancy 6、
topology-release 6、agent-idempotency 6、lifecycle-statemachine 4。

**执行缺口（必须记录）**：13 个维度中 **6 个因 API 连接中断未完成**——
`arch-boundary`、`scheduling-capacity`、`compute-assets`、`image-distribution`、
`observability`、`external-api`，汇总 agent 亦失败。已重新发起补跑，
**本文件当前不覆盖这 6 个维度**，不得据此宣称审查完整。

---

### 7A.0b 补跑后的最终统计（11 / 13 维度）

补跑（`resumeFromRunId`）后完成 `arch-boundary`、`scheduling-capacity`、`compute-assets`、
`observability` 四个维度；`external-api`、`image-distribution` 与汇总 agent 仍失败。

| 结果 | 首轮（7 维度） | 补跑后（11 维度） |
| --- | --- | --- |
| 已确认 | 43 | **71** |
| 已推翻 | 6 | 6 |
| 未做对抗性验证 | 21 | 41 |

级别分布：**P0 × 2（新）、P1 × 26、P2 × 27、P3 × 16**。

**关于 P0 的重要变化**：首轮的地址池 P0（两个维度收敛）在补跑中**不再出现在确认清单**。
原因是补跑 agent 读到的是**已修复后的代码**——独立怀疑者在当前代码上已无法复现该漏洞，
构成对 §7A.1 修复的外部验证。同时补跑产出一条指向 `TeamLabReservedAddressSpace.cs:61`
（本次新建文件）的 P2：**「节点 Fabric 覆盖网地址未纳入保留网段，租户地址池可覆盖
`WorkerNode.TeamLabFabricIp`」**——这正是 §7A.1「仍需的纵深防御 #3」中我明确标注为
未实施的动态节点事实排除。P0 已降级为**有记录的 P2 残留**，未被遗漏。

---

### 7A.0c 本轮已实施修复汇总

| 编号 | 缺陷 | 状态 | 测试 |
| --- | --- | --- | --- |
| P0-1 | 拓扑地址池未排除平台保留网段，可覆盖宿主路由 | ✅ 控制面已修，纵深防御待补 | 7 例 |
| P0-2 | KVM VNC 控制台监听 `0.0.0.0`，无密码无 TLS | ✅ 已修（两处） | 1 例 |
| P0-3 | 无认证 RDP 转发监听 `0.0.0.0`，心跳持续重建 | ✅ 已修 | 6 例 |
| P1-12 | apply 半失败致 cleanup 静默跳过删除却返回成功 | ✅ 已修（根因） | 5 例 |
| P1-13 | veth 名截断碰撞，多网络拓扑必然错连 | ✅ 已修 | 4 例 |
| P1-14 | 镜像 backing file 无引用校验，跨比赛不可逆损坏磁盘链 | ✅ 已修（两侧） | 5 例 |
| F-001 | 节点清单集合 null 处理不一致致部署期 NRE | ✅ 已修 | — |
| F-004 | 基线测试本就是红的 | ✅ 已修，基线转绿 | — |
| F-002 | VM 启动链路 QGA 逐文件传输 | ✅ 部分修（stage 批量化） | 1 例 |
| §12.1 | 地址池白名单**全面强制**（用户已决策） | ✅ 已实施 | 12 例 |
| P1-8 | generation 错挂 Create 票据致容量重复计数 | ✅ 已修（编译器强制） | 编译期 |
| P1-5 / P1-10 | 公网 UDP 与 Worker WireGuard 端口只分配不回收 | ✅ 已修 | 待补 |
| P1-1 | Release 网络租约依赖可变草稿 | ✅ 已改为绑定不可变 Release + NetworkKey | 全量测试 |
| P1-2 | BakeAtPublish 失败后无法重新提交 | ✅ 精确清理后按新 generation 恢复 | 全量测试 |
| P1-3 | Router/Fabric apply 破坏式重建 | ✅ 已改为所有权标记驱动的原位收敛 | Agent 构建 |
| P1-4 | 运行中 generation replay 失败误销毁 | ✅ 运行代重放失败不执行整代回滚 | 全量测试 |
| P1-6 | Managed VM runtime signal 无界等待 | ✅ 事件驱动等待增加契约计算失败边界 | 全量测试 |
| P1-7 | stale ticket 忽略活跃 claim | ✅ 仅回收 claim 已失效票据 | 全量测试 |
| P1-9 | Deferred 票据永久占用串行槽位 | ✅ 退回 Pending 并设置退避，2 小时后明确失败 | 全量测试 |
| P1-11 | Open API 绕过比赛 rollout reset/destroy | ✅ 入队与执行双重生命周期护栏 | 全量测试 |
| P2 | TeamLab 授权核心直接依赖 Penetration/Game | ✅ 改为比赛适配器授权扩展点 | 全量测试 |
| P2 | 镜像引用新增与清理竞态 | ✅ 分布式锁内重读所有权并安全清理 | 全量测试 |
| P2 | Agent 资源锁引用计数竞态 | ✅ 使用可退役条目和取消安全引用计数 | Agent 构建 |
| P2 | 管理端发布丢弃 ScenarioOverlays | ✅ 管理端与 Open API 共用完整发布契约 | 全量测试 |
| P2 | 隐式交换机键碰撞及空保存改变哈希 | ✅ 预留显式键并保留隐式交换机事实 | 前端定向测试 |
| P2 | 重复创建玩家授权静默踢掉队友 | ✅ 分布式串行；复用未消费授权，换钥要求先显式撤销 | 全量测试 |

**当前验证**：后端全量 **732 / 732 通过**；前端 **205 / 205 通过**；
主服务与 Agent Release 构建均 0 警告、0 错误；前端生产构建与 bundle budget 通过。

#### §12.1 地址策略已落地：保留网段强制排除，可选平台地址白名单

用户指示「连不上就直接全面强制」。SOCKS5 中转经测试**上游不通**
（本地 2222 在监听，但到 `10.0.7.118:22` 的 CONNECT 未建立），无法查询现网数据，
因此按指示实施**选项 C 全面强制**。

- `TeamLabReservedAddressSpace` 重命名为 **`TeamLabAddressPolicy`**：原名只覆盖「排除」语义，
  现在它同时承担「包含」判定，沿用旧名会误导后人。
- 新增 `IsWithinAllowedPools`，校验器新增 `address_pool_out_of_platform_range` 中文 issue。
- 允许范围取自 `TeamLabNetworkConfig.RuntimeNetworkBaseCidr`，默认留空，避免未经迁移即破坏
  已发布的 RFC1918 混合地址场景；部署方完成地址治理后可显式启用白名单。
- 平台、宿主、Fabric、Docker 和显式配置的 `ReservedCidrs` 始终强制排除，不受白名单是否启用影响。
- 配置不可解析时记录无效来源并保持强制排除规则，不能因一项可选包含约束关闭核心隔离。

#### P1-8 已修复 ✅ —— 用编译器消除整类缺陷

`TeamLabQueueRequest.Generation` 原有默认值 `1`。四个入队点中，Reset 与 Destroy 传了正确值，
而 **Create 与 Scenario Bake 两处省略**，静默落回 1——但 Create 请求在命中已有
`ExternalReference` 时会被 planner 转成 generation 自增的 Reset，于是票据 generation 与
运行事实脱钩，容量快照按错误 generation 扣减，同一负载被计两次，**节点 Available 被系统性
低估，波及普通实例与其他比赛**。

**修复**：把 `Generation` 提到必填位（移到 `Operation` 之前），删除默认值，
让编译器强制每个入队点显式提供。`PlanAndEnqueueAsync` 本就已从数据库重读 runtime，
真实 generation 就在手边，只是没传。这样修的是**整类缺陷**而非一个实例——
今后新增入队点无法再遗漏。

**待补**：destroy→同 ExternalReference 重建的 generation 传播集成测试。
现有测试项目无 orchestrator 脚手架，搭建成本高于收益；编译器强制已阻止遗漏。

#### P1-5 / P1-10 已修复 ✅ —— 两条同源 finding 一并关闭

- **端口回收**：新增 `LiveUdpMappings()`，占用集合只统计**仍存在的 runtime**
  （`Status != Destroyed`）。原实现取全表无生命周期过滤，已销毁 runtime 永久占用公网端口，
  累计创建超过端口区间容量（默认 1000）后所有新 TeamLab runtime 无法调度。
  **未新增 `ReleasedAt` 列**——runtime 状态已是权威事实，加列等于制造第二个状态源。
- **错误语义**：端口耗尽由 `InvalidOperationException` 改为
  `FleetCapacityReservationResult.Failed("public_udp_port_exhausted: ...")`。
  容量耗尽是**容量结果不是意外故障**；抛异常会逃逸出 per-ticket 边界。

**P1-10 的另一半已修复**：调度批次按票据隔离异常；失败票据释放 claim、清理
ChangeTracker 并记录事件，后续票据继续调度；取消信号仍向上传播。

---

### 7A.1a P0（全平台 VM 数据面）—— ✅ **两条均已修复**

这两条不限于 TeamLab，对**普通比赛 VM 实例同样成立**。

#### P0-2 所有 KVM 虚拟机的 VNC 控制台监听 `0.0.0.0` 且无密码、无 TLS

- **位置**：`src/GZCTF.Agent/Services/Vm/VmDomainBuilder.cs:34`
- **证据**：域定义硬编码 `"--graphics vnc,listen=0.0.0.0"`，libvirt 从 5900 起顺序分配端口。
- **触发**：任何一次 VM 创建（TeamLab 资产、普通 `VmInstance`、镜像认证 conformance VM）。
  Agent 侧只有 `GuestManagementNetworkService` 建了一张 policy accept 的 inet 表且只对管理桥
  `iifname` 生效，**没有任何规则限制宿主 5900+ 入站**；节点部署脚本也无 ufw/nft/iptables INPUT 配置。
- **影响**：跨租户越权 + 秘密泄露。攻击面包括 (a) 同机群运维网内任意主机；
  (b) **任何能路由到 Worker 宿主 IP 的 TeamLab 工作负载**——注意 `RoutingEnabled` 资产带
  `NET_ADMIN`（`AgentTeamLabNodeExecutor.cs:530-531`）。VNC 提供开机前 BIOS/引导阶段与
  OS 登录后的完整键鼠与屏幕，可直接读取 Managed VM 内
  `/opt/gzctf/runtime/secrets/*`、`config.json`（含 `enrollmentToken`、
  `workerServerCertificateSha256`）与 flag，并可任意写入。
- **修复** ✅：`VmDomainBuilder.cs:34` 与 `KvmProvider.cs:257`（主服务本地 KVM 模式，
  审查未提及、由我方补充发现的**第二处**）均改为 `listen=127.0.0.1`。
  **验证过零功能影响**：`GetVncPortAsync` 与 `GetConnectionInfoAsync` 全仓库无调用方
  （死代码），实际控制台协议是 RDP（`KvmProvider.cs:157 Protocol = "rdp"`），
  因此 VNC 从未被远程消费。**未采用加 VNC 密码**——密码需下发且 VNC 明文协议无前向保密。
- **测试**：`VmConsoleExposureTests.VirtInstallArguments_KeepVncOffEveryNonLoopbackInterface`
  断言域参数不含 `0.0.0.0`。

#### P0-3 Agent 为所有运行中 VM 在 `0.0.0.0` 开启无认证 RDP 明文转发，心跳持续重建

- **位置**：`src/GZCTF.Agent/Services/KvmService.cs:567`
- **证据**：`new TcpListener(IPAddress.Any, port)`；`RdpProxy.HandleClientAsync` 对任何 accept
  到的连接直接 `ConnectAsync(TargetIp, 3389)` 并双向 `CopyToAsync`，
  **无认证、无来源 ACL、无速率限制**。
- **触发**：`HeartbeatWorker` 每个心跳周期调用 `RestoreRdpProxiesAsync`，
  对 `virsh list --name` 返回的**每个运行域**（含所有比赛、所有队伍、所有 TeamLab runtime 的 VM）
  无条件 `EnsureRdpProxyAsync`。
- **影响**：跨租户网络越权，**完全绕过 TeamLab 入口授权模型**。
  玩家入口本应只有 `TeamLabAccessGrantService` 发的 WireGuard peer +
  `PlayerAllowedCidrs/PlayerBlockedCidrs` 约束；该代理在宿主侧另开了一条
  **不受 AccessGrant、不受 generation、不受 Revoked 控制**的路径。
  `vmName` 为 `tl{runtimeId}-{assetKey}`（`AgentTeamLabNodeExecutor.cs:664`），
  对同队玩家可见，端口可直接算出；即便不可见，46000-55999 仅需 1 万次探测。
  **撤销 AccessGrant 或置 `IsOpenToPlayers=false` 都不会关闭该监听**，
  只有 `DestroyVmAsync`/Replace 路径才 `StopRdpProxyAsync`。
- **修复** ✅：把「谁能连」与「何时存在」两个问题分开处理。
  1. **授权**：新增 `RdpProxyAccessPolicy`，在 accept 路径按源地址白名单放行，
     不合规连接直接关闭并记录可诊断的 warning。**fail-closed**——
     未配置来源时只接受回环，并在启动时告警提示配置项。
     白名单来源为 `Kvm:RdpProxyAllowedSources`（IP 或 CIDR）加上 Agent 上报的平台地址；
     **只认字面地址不认主机名**，否则等于让 DNS 决定谁能进租户控制台。
  2. **生命周期**：从心跳移除 `RestoreRdpProxiesAsync` 并**删除该方法**（已无调用方）。
     代理改为仅经由已认证的 `VmController:66/94` 按需创建，随 VM 销毁而停止。
- **未采用「绑回环」**：`VmReadyService.cs:250` 需要 `node.HostAddress + RdpPort`
  供 Guacamole 使用，绑回环会直接砍掉远程控制台能力。
  白名单在不破坏功能的前提下达成同等隔离——玩家进不了白名单，
  因此「撤销 AccessGrant 关不掉监听」不再构成安全洞。
- **测试**：`VmConsoleExposureTests` 6 例，覆盖 fail-closed 默认、平台地址放行、
  CIDR 与单机匹配、IPv4-mapped IPv6 对等体、主机名不授权、不可解析条目上报。

---

### 7A.1 P0（首轮，跨租户破坏）

#### P0-1 拓扑地址池未排除平台保留网段，可覆盖 WorkerNode 宿主路由并打断整节点 ✅ 已修复（控制面）

**两个独立维度（`fabric-routing`、`security-tenancy`）各自收敛到同一根因**，
两个验证者分别通读了全部相关文件后均确认成立。跨维度独立收敛是最强的证据形式。

- **位置**：`src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabTopologyStructureValidator.cs:60`
- **数据面落点**：`src/GZCTF.Agent/Services/TeamLab/TeamLabFabricService.cs:69-74`
  —— `ip route replace {TargetCidr} via {GatewayIp} dev {hostInterface}`，
  **无 `ip netns exec` 前缀，即宿主 root namespace**。
- **触发**：仅需 Teacher 级场景所有者（或持 `teamlab.topologies:write` 的 token），
  把 `networks[i].addressPool.poolCidr` 设为与基础设施重叠的 RFC1918 段，
  例如 `172.17.0.0/16`（Docker 默认桥）、节点管理 LAN `192.168.1.0/24`、
  或平台/数据库所在 10.x 段。**不需要 Admin。**
- **影响（三层）**：
  1. **破坏其他比赛与普通实例**——该节点上常规 GZCTF Docker 容器宿主侧可达性被劫持。
  2. **网络越权**——发往平台内网的宿主报文被导入玩家可达的 TeamLab fabric，
     玩家在靶场内即可接收/应答，形成跨租户越权。
  3. **不可逆**——清理只 `ip route del` 同一 CIDR，**被覆盖的原始宿主路由不会恢复**，
     需人工修复；节点心跳可能中断被判 Offline。
- **根因**：地址平面信任边界缺失。校验只有「在 RFC1918 内」+「同拓扑内不重叠」两条。
  而 `TeamLabNetworkConfig.RuntimeNetworkBaseCidr`(10.180.0.0/16) 与
  `TeamSubnetPrefixLength` **在全仓库没有任何读取点，是死配置**（两个验证者独立 grep 确认）。
  Agent 侧 `TeamLabNetworkPrimitives.cs:13` 的 `ValidateCidr` 只校验 prefix 1..32，
  无保留网段拒绝，最后一道防线也是空的。

  > **本文件早期结论的修正**：§3.1 曾据此配置推算「全平台仅 256 个 /24、并发 runtime 上限约 85」。
  > 该结论**错误**——配置无消费点，天花板不存在；真实形态是租户可自由声明整个 RFC1918 空间。
  > 教训：只读配置定义会得出与实际相反的结论，必须确认消费点。

- **已实施修复（控制面，无破坏性）**：
  - 新增 `Application/Validation/TeamLabReservedAddressSpace.cs` —— 保留地址空间一等概念，
    含内建范围（`172.17.0.0/16` docker0）、`ForPlatform()` 合并配置项与 Fabric 链路池，
    `TryFindConflict` 用**相交**（而非包含）判定，因为跨界的池同样会产生遮蔽宿主的路由。
  - `TeamLabNetworkConfig` 新增 `ReservedCidrs`（站点相关：节点管理 LAN、额外 Docker 池、
    存储/数据库网段），并在 `TeamLabModuleRegistration` 用工厂注册**真正消费**它——
    避免重复制造死配置。
  - 校验器新增 `address_pool_reserved` 中文可定位 issue。
  - `TeamLabTopologyValidator` 的 reserved 参数**默认取 PlatformDefaults**，
    调用方遗漏时仍然拦截，而不是静默放行。
- **刻意未实施（需你决策）**：两个 agent 都建议「强制 pool 必须落在
  `RuntimeNetworkBaseCidr` 内」。该改动会让**所有现存不在 10.180/16 内的拓扑立即失效**，
  属任务书 §7 要求暂停确认的「重大产品语义选择」。见 §12。
- **仍需的纵深防御（未实施）**：
  1. Agent 侧 `ValidateInfrastructureRequest` 增加保留前缀拒绝表（由 capability/配置下发）。
  2. **宿主级 `ip route replace` 改为 check-then-write**：先 `ip route show exact`
     确认无非本平台管理的既有路由，否则报 `route_conflict`。这是最有力的一招，
     因为它基于节点上的**实际事实**而非控制面推断，能挡住所有来源的路由冲突。
  3. 动态排除各 WorkerNode 的 `TeamLabTunnelIp`/`TeamLabFabricIp` 所在网段
     （需把节点事实传入校验器，已预留参数路径）。
- **验证**：新增 `TeamLabReservedAddressSpaceTests`（5 例）+
  `TeamLabFoundationTopologyTests` 两例端到端校验，共 **13 / 13 通过**；
  既有拓扑测试无误报。真实节点上「越界拓扑被拒 + 宿主路由表逐条未变」待多节点环境验证。

---

### 7A.2 P1（阻断生产、错误销毁、容量失真、无法恢复）—— 14 条

均为 `[代码可证]`，**全部未修复**，构成开发第一批。

| # | 维度 | 缺陷 | 位置 |
| --- | --- | --- | --- |
| P1-1 | topology-release | 不可变 Release 的网段目录仍依赖**可变草稿表**，发布后草稿新增/改名网段会让历史 Release 永久无法实例化；完整性检查用「数量相等」而非「key 集合包含」，既误报无害新增又漏报致命改名；reset 路径抛未捕获异常 | `TeamLabRuntimePlanner.cs:158` |
| P1-2 | topology-release | 场景预制（BakeAtPublish）失败后**不可恢复**：烘焙 runtime 的 `externalReference` 被永久占用，重试分支是死代码；因重复发布会复用旧 Release，管理员无法绕过 | `TeamLabRuntimePlanner.cs:118` |
| P1-3 | fabric-routing | 基础设施重放**无条件删除重建 router namespace**，销毁玩家 WireGuard 入口与 TLA 访问链，且无重新下发路径；探针不检查 wg 接口与 TLA 规则，故「已达成」判定看不到这次破坏 | `Agent/Services/TeamLab/TeamLabRouterService.cs:15` |
| P1-4 | fabric-routing | 部署失败回滚对「已运行 generation 的重放」同样执行**整代清理**，把一次网络漂移升级为对运行中队伍环境的错误销毁；绕过了 `CanRebuildMissingAsset` 对有状态资产的保护 | `TeamLabRuntimeOrchestrator.cs:354` |
| P1-5 | fabric-routing | 玩家公网 UDP 端口与节点 WireGuard 端口**只分配不回收**，`usedPublic` 取全表且不过滤生命周期，已销毁 runtime 永久占用端口；耗尽后抛 `InvalidOperationException`（非契约异常） | `TeamLabPhysicalPlacementService.cs:857` |
| P1-6 | vm-boot-latency | 托管 VM 的就绪/引导/健康等待**完全无界**（`WaitForAsync` 无 timeout 重载），GuestSupervisor 路径无宿主侧看门狗；来宾不启动则永久挂起，容量预留被持续续期**压缩同节点其他比赛可用容量** | `AgentTeamLabNodeExecutor.cs:766` |
| P1-7 | vm-boot-latency | 15 分钟 stale-ticket 回收**不检查活跃 claim**，而合法长时启动（Windows + 需重启 profile）常态超过 15 分钟，会在启动中途提前结单/重放（启动时间翻倍），并直接篡改在飞 DAG 的 `asset.ExecutionStage` | `RuntimeFactReconciliationService.cs:63` |
| P1-8 | lifecycle | destroy 后同 `ExternalReference` 重建会把 generation≥2 的 runtime 挂到 **Generation=1 的 Create 票据**上，容量快照按错误 generation 扣减导致**同一负载被计两次**，节点 Available 被系统性低估，波及普通实例与其他比赛 | `TeamLabRuntimeOrchestrator.cs:52` |
| P1-9 | lifecycle | Deferred 票据永远停在 `Running` 且无尝试上限，配合 `SubjectConcurrencyKey` 串行化把该 runtime 的 destroy/reset **永久锁在队列外**，资源无法回收 | `RuntimeFactReconciliationService.cs:1384` |
| P1-10 | lifecycle | `TeamLabPublicUdpMappings` 端口 destroy 后从不回收，耗尽时异常**逃逸出 per-ticket 边界**，`SchedulePendingAsync` 无 try/catch，中断**整个平台的调度批次** | `TeamLabPhysicalPlacementService.cs:866` |
| P1-11 | security | 外部 API 的 reset/destroy 对「已绑定比赛的队伍 runtime」**无任何护栏**，场景所有者 token 可越过 rollout 状态机与 `MaxResetCount` 摧毁某队环境，并连带关闭整场比赛玩家访问 | `Api/OpenTeamLabRuntimesController.cs:76` |
| P1-12 | agent-idempotency | 基础设施 apply 部分失败时**不写 generation 归属标记**（写入点在成功分支之后），后续 cleanup 因 `desiredStateExists=false` 跳过 bridge/netns/dnsmasq 删除**却返回成功**，泄漏对 inventory 完全不可见的网络资源 | `Agent/Services/TeamLabNetworkService.cs:703` |
| ~~P1-13~~ ✅ | agent-idempotency | router veth 接口名用**字符串截断而非哈希**，namespace 名长度≥14 时碰撞：==15 直接创建失败；==14 且多网络时 index=1 会删掉 index=0 刚建的 veth 对 | `Agent/Services/TeamLab/TeamLabRouterService.cs:22` |
| ~~P1-14~~ ✅ | agent-idempotency | Agent VM 镜像缓存删除端点**无 backing-file 引用校验**、不过 `AgentOperationGate`；主服务守卫又漏掉 `Stopped` 状态实例 | `Agent/Controllers/ImageController.cs:176` |

#### P1-12 已修复 ✅ —— 但审查建议的修复方案被部分推翻

**审查建议的两步**：(1) 在第一条变更命令前写入所有权意图；(2) 把
`ownsSharedResources` 改为 `activeGeneration is null || activeGeneration.Generation == request.Generation`。

**第 (2) 步是错的，未采纳。** 实施时发现仓库里有两个**刻意编写的既有测试**固定了相反的不变量：

- `TeamLabCommandBuilderTests.CleanupAsync_MissingActiveGenerationDoesNotInferSharedResourceOwnership`
  —— 无标记且无 desired-state 时，cleanup 成功但**断言不出现** `ip link delete` /
  `ip netns delete` / fabric 路由删除。
- `TeamLabCommandBuilderTests.CleanupAsync_MissingActiveGenerationWithDesiredStateFailsClosed`
  —— 无标记但有 desired-state 时 fail closed。

即原作者的立场是明确的：**无法证明所有权时，绝不触碰共享名资源**。而资源名是
`(runtimeId, key)` 级、跨 generation 复用，所以这条保守立场正是防止跨代破坏的关键。
采纳第 (2) 步会削弱它，属安全回退。

**实际采用的修复**：只做第 (1) 步。早写标记后，半成品 generation **本身就有所有权证明**，
cleanup 自然会删除它的资源——泄漏被根治，不变量毫发无损。这比审查建议的方案更正确。
早写标记还有一个附带收益：它保证**任何共享名资源被创建之前标记已存在**，
使 fencing 判定本身更可信。

**改动**：
- `Agent/Services/TeamLabNetworkService.cs` —— 在第一条变更命令前 `generationStore.WriteAsync`
  （仅在真正执行时，dry-run 不写）。
- 把原先内联的三态判定抽为纯函数 `ResolveCleanupOwnership`，
  新增 `TeamLabCleanupOwnership` 枚举（Refuse / OwnsSharedResources / SharedResourcesNotOwned），
  **语义与原实现完全一致**，仅使其显式且可测试。

**已知残留（诚实记录）**：本次修复**只对新发生的半成品生效**。修复前已产生的、
标记缺失的半成品 generation，其 bridge/netns/dnsmasq 仍不会被自动清理。
这类历史残留应通过显式的「强制清理」运维操作处理，而不是靠放宽默认安全规则，
否则等于用一个跨代破坏风险换一个泄漏。

**测试**：新增 `TeamLabCleanupOwnershipTests`（5 例，含 fail-closed 与双向 fencing）；
TeamLab + Runtime 全套 **263 / 263 通过**，原有两个不变量测试继续通过。

#### P1-13 已修复 ✅ —— 顺带收敛了一处重复实现

`TrimInterfaceName` 原为纯截断。namespace 长 14 时 `{ns}h0` 与 `{ns}h1` 都截成 `{ns}h`
（**index 被截掉**），于是 `TeamLabRouterService.cs:24` 中 index=1 的 `ip link delete`
会删掉 index=0 刚建好的 veth 对；长 15 时 host 与 peer 同名，`ip link add` 直接失败。

**修复**：改为「超长即哈希」，**复用主服务 `TeamLabResourceNameFactory.LinuxName`
完全相同的算法**（前 8 字符 + `-` + sha256 前 6 位），保证两侧对同一输入派生同一名字。
另在 `TeamLabRouterService.Validate` 增加派生名两两不重复的前置断言，
使任何残留碰撞成为显式错误而非内核层错连。

**顺带修复**：`TrimInterfaceName` 原有**两份实现**（`TeamLabNetworkPrimitives.cs:32` 与
`TeamLabNetworkService.cs:1166` 私有副本），只改一处仍会漂移；已收敛为单一真源。

**测试**：`TeamLabInterfaceNamingTests` 4 例，含 ns 长度 14/15 的唯一性、
确定性、以及**主服务与 Agent 派生同名**的一致性断言。

#### P1-14 已修复 ✅ —— 两侧同时收口

- **Agent 侧**：新增 `VmImageBackingChainInspector`，删除前用
  `qemu-img info --output=json` 枚举存储目录内的 overlay，比对
  `full-backing-filename`/`backing-filename`。存在引用则返回
  `image.vm.cache_in_use`；**无法确定引用关系时抛错拒绝删除**（fail-closed）——
  在不可逆删除上按不完整信息放行是不可接受的。端点同时纳入
  `AgentOperationGate.Control`。
- **主服务侧**：`HasActiveVmUsingTemplateAsync` 的 VmInstance 子句由正向枚举
  `Creating || Running` 改为 `!= Destroyed`。原实现与**同一方法内**的 TeamLab 子句
  （已是 `!= Destroyed`）语义不一致，漏掉 `Stopped` 与 `Error`——
  而 Stopped VM 的 overlay 依然引用着 backing file。
- **测试**：`VmImageBackingChainTests` 5 例，含「输出不可解析时必须抛错而非报告无引用」。

### 7A.3 P2（商业闭环、并发瓶颈、契约与体验）—— 18 条

| 维度 | 缺陷 | 位置 |
| --- | --- | --- |
| topology-release | 管理端发布端点丢弃 `ScenarioOverlays` 且不触发场景预制，管理台发布的 BakeAtPublish 场景永远无法实例化 | `Api/TeamLabAdminTopologyController.cs:92` |
| topology-release | 前端把交换机键与资产键放进同一命名空间，服务端唯一性校验不覆盖隐式交换机键 | `ClientApp/.../model/topologyMapper.ts:116` |
| topology-release | 隐式交换机被物化成显式条目且名称不同，一次「空保存」即改变 contentHash，导致重复发布产生新版本并**强制场景 VM 重新烘焙** | `ClientApp/.../model/topologyCompiler.ts:96` |
| fabric-routing | router namespace 只有 forward 钩子、**没有 input 策略**，玩家与被隔离网段资产可直接访问所有未连接网段的网关服务（DHCP/DNS/WireGuard 监听） | `Agent/.../TeamLabFirewallService.cs:172` |
| vm-boot-latency | **VmCreate 并发默认 1**，overlay/ISO/virt-install 全部串行化，最后一台 VM 被推迟 (N-1) 个 create 周期，且与普通比赛 VM 共用同一闸门 | `Agent/Services/AgentCapabilityService.cs:71` |
| vm-boot-latency | 健康探测用「固定次数 × 1 秒 sleep」表达就绪，被 Probe 并发闸门（默认 2）放大成分钟级，既拖慢就绪又撞上 15 分钟 stale 回收 | `AgentTeamLabNodeExecutor.cs:885` |
| lifecycle | Rollout drain 把 `CleanupPending` 目标**永久排除**在销毁重试外，rollout 永远无法 Completed、镜像分发引用永久泄漏并每 tick 空转 | `Rollouts/TeamLabRolloutCoordinator.cs:220` |
| security | 玩家创建 WireGuard grant 接口**无速率限制**，且每次调用无条件重建服务端 WireGuard 接口并撤销本队全部旧 grant——任一队员可反复踢掉队友，并对 worker node 施加不受限的 root 级网络变更 | `Controllers/PenetrationPlayerController.cs:43` |
| security | TeamLab 核心授权服务**直接查询 Penetration 绑定表与 Games 表**，违反底座独立性，授权规则只对 penetration 一种比赛成立 | `Application/TeamLabAuthorizationService.cs:38` |
| frontend | React Flow 画布每次选择变化**重建全部节点/边对象**，memo 全部失效，且每次重建做 O(节点×连接) 的 NIC 计数 | `.../editor/canvas/TeamLabCanvas.tsx:117` |
| frontend | 检查器**每敲一个字符**提交一次撤销历史并整文档重建，单字段编辑即冲掉整个撤销栈 | `.../editor/inspector/AssetInspector.tsx:36` |
| frontend | `KeyValueEditor` 编辑键名时重排对象键顺序并用**数组下标做 key**，正在输入的行会错位到另一条记录，导致环境变量/Bootstrap 参数被改错 | `.../editor/inspector/InspectorFields.tsx:244` |
| frontend | 专注模式用 `createPortal` 切换渲染位置，导致整个画布子树**卸载重挂**：缩放/平移位置与局部状态全部丢失 | `.../editor/TeamLabDesignPage.tsx:351` |
| frontend | cleanup-pending 是前端**运维死角**：两处 UI 同时禁用销毁/清理，而后端本就允许重试且无自动恢复，容量预留与网络租约无法释放 | `.../runtimes/TeamLabRuntimeDetailPage.tsx:81` |
| frontend | 选手 WireGuard 授权只存组件内存，刷新即消失；再次点击会吊销上一份并轮换密钥，**静默踢掉选手正在使用的 VPN**，下载链接一次性不可重取 | `.../games/teamlab/PlayerAccessPanel.tsx:9` |
| agent-idempotency | 基础设施 replay 或节点重启后玩家 WireGuard 接入**永不重建**，但 runtime 仍标记 `IsOpenToPlayers=true`、grant 未 Revoked——**状态机对外撒谎** | `Application/TeamLabAccessGrantService.cs:120` |
| agent-idempotency | router namespace 无条件删除重建，任何局部漂移修复都会摧毁该 runtime 全部网络与隧道；apply 语义上不是幂等收敛而是**破坏式重放** | `Agent/.../TeamLabRouterService.cs:14` |
| agent-idempotency | `AgentResourceLock` 引用计数与字典移除存在竞态，导致同一 runtime/VM/容器互斥失效；等待取消时引用计数还会泄漏 | `Agent/Services/AgentResourceLock.cs:31` |

### 7A.4 P3 —— 9 条

含：镜像模板类校验无法定位到具体资产且一次只报一条（`TeamLabTopologyApplicationService.cs:327`）；
下发顺序存在「已开启转发但尚无 drop 策略链」的窗口，未连线网段在此窗口内完全互通
（`Agent/Services/TeamLabNetworkService.cs:157`）；每个网段的 dnsmasq 被下发**整个 runtime 的全量 DNS 记录**，
泄露未连接网段编址且多网卡解析不确定（`TeamLabRouteApplicationService.cs:224`）；
DAG 批次用 `Task.WhenAll` 形成硬屏障，总启动时长退化为「各阶段最慢资产之和」，
最慢的一台 Windows VM 拖住所有容器与快启 VM。

---

### 7A.5 已推翻（6 条，记录以免重复调查）

| 原级别 | 原结论 | 推翻理由摘要 |
| --- | --- | --- |
| P2 | 发布未冻结 Bootstrap 制品 digest，规划阶段解析不到静默置 null | `BootstrapDigest` 仅在「本无 Bootstrap」或「BakeAtPublish 已烘焙进镜像」两种情形为 null，描述的触发路径不成立 |
| P2 | Rollout 的 `AccessOpen` 与真实访问事实脱钩 | 代码事实基本正确但语义与影响判断错误 |
| P2 | 访问撤销不幂等，`Shards.Single` 在 reset/节点离线时抛 500 | 验证者逐字读完 `TeamLabAccessGrantService` 全文等 7 处后判定已有防护 |
| P2 | Stop 被静默映射为 Destroy | 普通实例走**完全相同**的合并语义，非 TeamLab 特有缺陷 |
| P2 | 三个外部控制器完全不执行资源级授权 | 代码观察成立但 `AuthorizeRuntimeAsync` 等已提供所有权校验 |
| P2 | 清理后删除 `active-generation.json` 使 fencing token 失效 | 描述的触发路径已被现有代码封堵，且其根因与修复逻辑自相矛盾 |

### 7A.6 未完成对抗性验证（21 条）—— **不得当作事实**

每维度只对前 7 条（按严重级别）做对抗性验证，以下 21 条**仅为单一 agent 的未验证主张**。

**已由我方独立验证并推翻 1 条**：

> ~~`VmBootstrapService.cs:112` 旧 QGA bootstrap 数据面已无任何主服务调用者，是死路径~~
> —— **错误**。实测调用链完整且活跃：
> `TeamLabShardDeploymentService.cs:331` → `AgentTeamLabNodeExecutor.cs:855 ApplyBootstrapAsync`
> → `AgentClient.cs:818` POST `/api/vms/{vmName}/bootstrap/apply`
> → Agent `VmController.cs:151` → `bootstrap.ApplyAsync`。
> **若误信此条会导致删除生产运行中的代码路径。** 这是保留「未验证」分区的直接理由。

其余 20 条待验证主张（择要）：Fabric /30 链路池缺少与 network lease 对等的活跃
`AllocatedCidr` 唯一约束、存在重复分配竞态（`TeamLabFabricLinkAllocator.cs:25`）；
清理只按名字匹配无归属标签或 generation 校验，而资源名是 8 字符前缀 + 摘要截断，
存在跨 runtime 误删可能（`TeamLabResourceNameFactory.cs:20`）；公网 UDP 网关规则删除失败
只写日志仍返回成功（`PublicUdpGatewayProvider.cs:67`）；启动瀑布无法分阶段度量
（`TeamLabFleetAdapters.cs:23`）；心跳每 30 秒对每台运行中 VM 发起 QGA 级探测，
与启动链争抢 virsh（`HeartbeatWorker.cs:40`）；`CleanupAsync` 的异常白名单过窄，
非白名单异常穿透使 runtime 停在 `Destroying` 而非可恢复的 `CleanupPending`
（`TeamLabRuntimeCleanupService.cs:61`）；命令封装并未真正集中——三套 shell 执行实现、
两套 ShellQuote、两套 Enable/DryRun 门控，且是否真正执行由字符串哨兵 `"<redacted>"` 决定
（`TeamLabNetworkService.cs:887`）。

---

## 8. VM 启动链路延迟专项（用户明确报告）

### 8.1 已核实的等待常量清单 `[代码可证]`

用户报告「VM 启动链路花费时间过长」。以下为 grep 确认的等待点，
**注意超时值是上限而非实际耗时**，真正成本需区分结构性等待与超时保护：

| 位置 | 常量 | 性质 |
| --- | --- | --- |
| `src/GZCTF.Agent/Services/Vm/VmBootstrapService.cs:33` | `WaitReadyAsync` 3 分钟 | 首次就绪等待 |
| `VmBootstrapService.cs:112` | `RebootAndWaitAsync` 5 分钟 | **bootstrap 中重启 #1** |
| `VmBootstrapService.cs:167` | `RebootAndWaitAsync` 5 分钟 | **bootstrap 中重启 #2** |
| `VmBootstrapService.cs:201` | `WaitReadyAsync` 3 分钟 | 重启后再次就绪等待 |
| `VmBootstrapService.cs:453` | `Task.Delay` 固定 1 秒 | 固定等待 |
| `VmBootstrapService.cs:594` | `Task.Delay` 固定 2 秒 | 固定等待 |
| `Vm/VmGuestAgentService.cs:11` | `PollInterval` 500 ms | QGA 轮询 |
| `Vm/VmRuntimeReadinessCoordinator.cs:16` | `ProbeWindow` 8 秒 | 探测窗口 |
| `Vm/VmRuntimeReadinessCoordinator.cs:17` | `RetryDelay` 2 秒 | 重试间隔 |
| `Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs:900` | `Task.Delay` 1 秒 | **主服务侧轮询** |
| `AgentTeamLabNodeExecutor.cs:924` | `Task.Delay` 1 秒 | **主服务侧轮询** |
| `Application/TeamLabScenarioBakeService.cs:27` | `StatePollInterval` 2 秒 | 烘焙状态轮询 |
| `Agent/Services/Vm/VmScenarioArtifactService.cs:68` | `WaitForShutdownAsync` 3 分钟 | 制品关机等待 |
| `Agent/Services/DockerService.cs:809` | `systemctl restart docker` 60 秒 | **节点级全局副作用** |

### 8.2 已推翻的初始假设（记录以免重复调查）

**假设**：`VmBootstrapService.cs:112` 与 `:167` 是两次无条件 VM 重启，构成 2-6 分钟固有成本。

**推翻依据**（读 `VmBootstrapService.cs:95-180` 全文）：
- `:112` 是**恢复路径**——仅当 checkpoint 已持久化 `RebootRequired=true` 且
  `RebootCompleted=false`（Agent 在发起重启前崩溃）时执行，属正确的可恢复设计。
- `:167` 是**正常路径**——仅当 step 声明 `Reboot=Required`，或声明 `IfRequested`
  且 step 退出码为 194/3010（systemd / `ERROR_SUCCESS_REBOOT_REQUIRED`）时执行。
- 两者共同受 `manifest.MaxReboots` 上限约束（`:105`、`:162`），超限抛错。

**结论**：重启是 **profile 声明驱动**且 checkpoint 可恢复的，不是缺陷。
这印证了任务书 §2 的方法论——**仅凭常量 grep 推断会系统性误判**，必须读完整控制流。

### 8.3 已确认的真实主因：QGA 通道 = 每次 RPC 一个 virsh 进程 `[代码可证]`

**严重级别**：P2（明显性能瓶颈，直接对应用户报告）
**位置**：`src/GZCTF.Agent/Services/Vm/VmGuestAgentService.cs:266-307` (`RunVirshAsync`)
**协同位置**：`VmGuestAgentService.cs:13` (`FileChunkSize = 48 * 1024`)、
`VmBootstrapService.cs:68-89`（双写循环）

**根因**：`RunVirshAsync` 对**每一次 QGA RPC** 都 `Process.Start` 一个新的 `virsh
qemu-agent-command` 进程。写入一个文件的固定成本为
`guest-file-open` + `ceil(size / 48KB)` × `guest-file-write` + `guest-file-flush`
+ `guest-file-close`，即 `N+3` 次进程启动（含 fork/exec 与 libvirt 客户端连接 libvirtd）。

**放大效应**：`VmBootstrapService.cs:68-76` 把 artifact 内**所有**文件写入 guest 的
stage 目录；`:78-89` 又把 `manifest.Files` 声明的文件写入各自 `TargetPath`。
凡是既在 artifact 包内、又在 `manifest.Files` 中声明的文件，**经 QGA 传输两遍**。

**成本模型（已修正）**：读完 `WriteGuestBytesAsync:698-717` 后，先前的「每 MB 43 次」
模型被推翻。真实开销由**每文件固定成本**主导，而非每字节：

单个文件的完整 QGA 调用序列 —
1. `EnsureGuestDirectoryAsync` → `guest-exec` + `guest-exec-status` = **2 次 virsh**
   （且**每个文件都独立调一次 mkdir**，50 个文件进同一目录就执行 50 次 mkdir）
2. `guest-file-open` / `guest-file-flush` / `guest-file-close` = **3 次**
3. Linux `chmod` 或 Windows `icacls`（mode 0600）= **2 次**

即 **7 次固定 + ceil(size / 48KB) 次数据传输**。

| 场景 | 文件数 | virsh 进程数 | 纯进程启动开销 @15ms |
| --- | --- | --- | --- |
| 200 个小脚本/配置（典型 bootstrap 包，仅几 MB） | 200 | ~1600 | **~24 秒** |
| 500 个小文件 | 500 | ~4000 | **~60 秒** |
| 单个 100 MB 大文件 | 1 | ~2140 | ~32 秒 |

**关键结论**：成本与**文件数量**强相关，与包体积关系不大。典型 bootstrap profile 是
"几百个小脚本 + 配置"，恰好落在最坏区间。这部分**完全是平台开销**，与来宾 OS 启动
和业务服务初始化无关——正是任务书 §9.4 要求单独区分的那部分。

**附加脆弱性**：`VmGuestAgentService.cs:285` 将 48 KB chunk 的 base64（约 64 KB）
**作为命令行参数**传给 virsh。Linux `MAX_ARG_STRLEN` 为 128 KB，当前值已用掉一半，
若后续调大 `FileChunkSize` 会直接触发 `E2BIG`。

### 8.3.1 已实施：stage 填充改为单次 tar 传输 ✅

**改动**
- 新增 `src/GZCTF.Agent/Services/Vm/GuestFileBatch.cs` —— 纯函数，把多个文件打成
  一个确定性 tar（按路径排序、固定 mtime = Unix epoch，保证同输入同字节，
  满足 §4.3 确定性要求），**权限位写入 tar 条目本身**。
- `VmBootstrapService.cs` 新增 `PopulateGuestStageAsync` / `PopulateGuestStageBatchedAsync`，
  原 stage 填充循环改为构建计划后一次性传输 + 来宾侧 `tar -xpf -C <guestStage> --no-same-owner`。

**收益**：`N 文件 × 7 次固定调用` → **约 8 次固定调用**。
200 个小文件场景从 ~1600 次 virsh 进程启动降到 ~8 次。

**顺带解决的安全问题**：tar 在展开瞬间原子地应用权限位，消除了原「文件已创建、
chmod 尚未执行」的窗口——`secrets.json` 等 0600 文件在该窗口内是默认 umask 权限。
这也是我**没有**采用「批量 chmod」方案的原因：那会扩大该窗口，属安全回退。

**范围收紧的理由（安全）**：只批量化 stage 填充（原 loop 1），**不含**
`manifest.Files` → `TargetPath` 的写入（原 loop 2）。因为 `TargetPath` 是
**profile 可控的绝对路径**，以 root 在 `/` 展开带 `-p` 的 tar 会引入路径穿越与
符号链接跟随风险，而逐文件写入没有这个攻击面。stage 条目路径则已由
`ExtractArtifactAsync:657` 校验。收益本就集中在 loop 1（铺整个 artifact 的几百个文件），
loop 2 通常只有个位数文件。

**失败行为**：仅 Linux 且文件数 > 1 时走批量路径；捕获 `InvalidOperationException`
后**一次性**回退到原逐文件写入并记录 warning（非无条件重试，符合 §4.8）。
来宾缺 `tar` 时自动降级，不阻断 bootstrap。

**观测**：成功时记录 `Files` 与 `ArchiveBytes`；回退时记录 warning 含 VM 名与文件数。

**回滚**：删除 `PopulateGuestStageAsync` 调用改回原循环即可，无数据迁移、无契约变更。

**测试**：`src/GZCTF.Test/UnitTests/Runtime/VmGuestFileBatchTests.cs` —— 断言归档
携带全部文件且权限位正确保留（先确认 RED：CS0246/CS0103，再实现至通过 1/1）。

### 8.3.2 未实施（需更大设计决策，列入批次待评审）

1. **复用 libvirt 连接**：改用 libvirt .NET 绑定或常驻 QMP socket，把「每 RPC 一进程」
   降为「每 VM 一连接」。这是量级级收益，也是彻底根治，但涉及 Agent 依赖与连接生命周期
   管理，需单独设计。
2. **Windows 批量化**：Windows 路径未批量化。tar 条目无法干净表达盘符，
   且 `icacls` 语义与 chmod 不同。需单独设计（可考虑 zip + `Expand-Archive`）。
3. **消除 stage/target 双写**：仍存在。`manifest.Files` 声明的文件既进 stage 又进
   `TargetPath`。安全的做法是传输一次后在**来宾内**复制，而非第二次跨 QGA 传输。
4. **payload 改走 stdin**：解除 `MAX_ARG_STRLEN` 约束后可显著增大 chunk。
5. **大文件走 config drive / 虚拟磁盘**：bootstrap artifact 属发布期已知内容，
   应在 domain 定义阶段挂载，而非运行期逐字节推送。这是架构上最正确的方向。

**仍需的验证方法**：为 QGA 通道增加进程启动计数与阶段耗时指标（当前**无法**分别度量
平台开销/镜像准备/网络创建/domain启动/来宾OS/业务初始化，这本身是 §9.4 的缺口），
在真实节点上记录修复前后的 virsh 调用次数与 wall-clock。

### 8.4 `RebootAndWaitAsync` 的快速重启误判 `[代码可证]` `[需运行验证]`

**严重级别**：P2
**位置**：`src/GZCTF.Agent/Services/Vm/VmGuestAgentService.cs:202-216`

**问题**：重启检测依赖「`guest-ping` 必须失败一次」来推断 QGA 已断开。
若来宾重启足够快、或 `virsh --timeout 30` 在重启窗口内始终返回成功，
`disconnected` 永远为 `false`，循环会**耗尽整个 5 分钟 deadline**，
随后抛出 `Guest reboot did not disconnect the QGA session.` ——
即**重启越快，越可能在 5 分钟后硬失败**。

**根因**：用「观测到 ping 间隙」这一副作用推断重启事实，而非验证重启本身。
违反任务书 §4.8「就绪状态必须由事实或事件驱动」。

**修复方向**：改为验证重启事实而非断连副作用——比对重启前后的来宾
boot id / 启动时间戳（Linux `/proc/sys/kernel/random/boot_id`、Windows
`LastBootUpTime`），或订阅 libvirt domain 生命周期事件。
`VmBootstrapService.cs:116/171` 已有 `VerifyMarkerAsync`，可扩展为 boot-id 比对。

**验证方法**：注入一个重启极快的来宾（最小 Linux + 快速 QGA 启动），
断言 `RebootAndWaitAsync` 在 boot id 变化后立即返回而非等满 deadline。

### 8.5 待专项审查补齐

完整启动瀑布（平台调度 / 镜像准备 / 网络创建 / domain 定义与启动 / 来宾 OS /
业务初始化 分别度量）、串行与并行判定、主服务侧 1 秒轮询的请求放大分析，
待 `vm-boot-latency` 专项维度完成后并入。

---

## 9. 开发批次

1. **安全与所有权边界**：地址策略、VNC/RDP 暴露、generation ownership、cleanup fail-closed。
2. **生命周期与调度**：不可变 Release、预制恢复、claim/stale/deferred、端口回收、单票据隔离。
3. **数据面与制品闭环**：非破坏式 Fabric 收敛、INPUT 隔离、镜像锁/引用/清理、VM signal 边界。
4. **操作体验与验证**：画布性能、撤销语义、cleanup 恢复、玩家授权反馈、集中质量门槛。

四批代码均已完成；真实双 Worker 故障注入和流量证据留在部署验收阶段执行。

---

## 10. 测试证据

### 10.1 本地质量门槛（任务书 §9.1）

| 项 | 状态 |
| --- | --- |
| 主服务 Release 构建 | ✅ 0 警告、0 错误 |
| Agent Release 构建 | ✅ 0 警告、0 错误 |
| 后端全量测试 | ✅ **732 / 732**（关闭 Coverlet 报告生成，仅关闭覆盖率采集，不跳过测试） |
| 前端生产质量门槛 | ✅ locale、lint、TypeScript strict、架构检查、**205 / 205**、Vite build、bundle budget |
| 新增重点覆盖 | cleanup ownership、地址策略、接口命名、VNC/RDP、backing chain、Guest 批量传输、画布和运维恢复 |
| 迁移一致性 / `git diff --check` | 收口检查见本节最终记录 |

### 10.2 现有测试覆盖基线（已核实）

单元测试 102 个文件、集成测试 65 个文件。TeamLab 直接相关：

**`src/GZCTF.Test/UnitTests/TeamLab/`（20 个）**
`TeamLabFabricLinkAllocatorTests`、`TeamLabRouteIsolationTests`、`TeamLabAccessGrantTests`、
`TeamLabCommandBuilderTests`、`TeamLabDeploymentOrchestrationTests`、`TeamLabFoundationBoundaryTests`、
`TeamLabFoundationTopologyTests`、`TeamLabTopologyV2Tests`、`TeamLabVmNetworkTests`、
`TeamLabRuntimeFoundationTests`、`TeamLabObservationTests`、`TeamLabCaptureTests`、
`TeamLabTrafficFingerprintTests`、`TeamLabAdminContractTests`、`TeamLabInternalControllerTests`、
`TeamLabPlayerWorkspaceContractTests`、`TeamLabCompetitionSubmissionTests`、
`PublicUdpGatewayProviderTests`、`PenetrationObjectiveSecurityTests`、`PenetrationTeamLabLifecycleTests`

**`src/GZCTF.Test/UnitTests/Runtime/` 相关**
`TeamLabPlacementCapacityTests`、`UnifiedCapacityAccountingTests`、`NodeDispatchBudgetTests`、
`AgentCapabilityContractTests`、`RuntimeFactReconciliationTests`、`GuestSupervisorTests`、
`GuestControlContractTests`、`GuestManagementControlPlaneTests`、`AgentOperationReceiptStoreTests`

**`src/GZCTF.Integration.Test/` 相关（5 个）**
`PhaseNineTeamLabMigrationTests`、`TeamLabContractMigrationTests`、`TeamLabCapturePersistenceTests`、
`TeamLabTrafficPersistenceTests`、`TeamLabTrafficStreamTests`

**已识别的覆盖缺口**：`VmBootstrapService` 与 `VmGuestAgentService`（即 §8.3/§8.4 两条
finding 所在文件）**没有直接的单元测试**。QGA 传输次数、双写行为与重启检测均无断言保护，
这解释了为何这类性能与时序缺陷能长期存留。修复时必须同步补测试。

**命名残留**：`PenetrationObjectiveSecurityTests`、`PenetrationTeamLabLifecycleTests`
仍以 Penetration 命名，与 §5.1 的 `UsePenetrationFabric` 残留同源。

### 10.3 多节点全链路（任务书 §9.2）

**状态：未执行。** 需要用户指定的双 Worker 环境。

---

## 11. 阻塞项

| 阻塞项 | 影响 | 需要什么 |
| --- | --- | --- |
| SOCKS5 中转上游不通 | §9.2 多节点全链路验收、§9.3 并发与故障验证、所有 `[需运行验证]` 结论无法闭环；也无法查询现网地址池分布 | 已测：本地 `127.0.0.1:2222` 在监听，但到 `10.0.7.118:22` 的 SOCKS5 CONNECT 未建立（上游立即断开）。需确认代理凭据、目标主机 SSH 是否开放，或改用其他通道 |
| 真实双 Worker 故障注入未执行 | Registry 中断、节点离线恢复和 Linux 网络数据面尚无本轮运行证据 | 在部署验收阶段使用隔离场景执行，不影响本地代码质量结论 |

---

## 12. 待用户确认项

按任务书 §7「涉及资源身份、外部 API 不兼容变更、不可逆数据迁移或重大产品语义选择时，暂停并向用户确认」。

### 12.1 地址白名单启用时机

当前生产安全边界已经闭合：基础设施和保留网段始终拒绝，`RuntimeNetworkBaseCidr` 作为
额外的平台地址白名单默认留空。启用白名单前必须先盘点并迁移现有拓扑和历史 Release；
这属于部署配置决策，不需要保留两套运行代码。

### 12.2 已排除的候选

- ~~队伍网段池 256 个 /24 的固定规模天花板~~ —— **该固定天花板不存在**；
  实际上限由部署方启用的允许范围、前缀和租约事实共同决定。

---

## 13. 镜像存储、预分发与清理专项审查（2026-07-27）

### 13.1 已核实架构

- VM 主副本由内部 OCI Registry 保存；平台本地文件只用于历史模板首次补推，Agent 下载优先使用
  Registry artifact，并校验 digest 与大小。
- Docker 平台镜像上传后写入内部 Registry，节点通过 Agent pull；VM 与 Docker 分别按 KVM/Docker
  能力筛选节点，互不依赖。
- 分发记录以 `template + node` 唯一，`ImageHash` 参与 Ready 命中判断；引用使用独立唯一行表示
  Game、TrainingCourse、TeamLabRuntime、ImageCertification 和 TeamLabRollout 所有权。
- 比赛结束、课程删除/解绑、runtime 销毁和 rollout 完成均有引用释放入口；后台 reconcile 修正
  陈旧引用并排队清理。Docker 删除使用 `Force=false`，VM 删除同时受数据库运行事实与 Agent
  qcow2 backing-chain 双层保护。

### 13.2 已确认并修复

1. **清理与新引用存在竞态**：清理现在与引用增删共用 `template + node` 分布式锁，删除前重新读取
   操作和引用事实；引用已恢复时取消清理，避免刚被新比赛引用的缓存遭删除。
2. **Agent 拉取与删除可能并发**：Docker/VM 拉取、发布和删除现在按镜像身份共用 Agent 资源锁；
   VM 缓存 digest 变化时，替换前也执行 backing-chain 检查。
3. **不可重试错误被无限重试**：Worker 只重新 claim `Retryable=true` 的失败记录；claim CAS 同时校验
   operation 与原状态，避免陈旧候选覆盖新状态。
4. **活动 VM 阻塞形成高频空转**：清理阻塞写入稳定错误码、可重试标志和 5 分钟下一检查时间，
   不再每 2 秒重复请求 Agent。
5. **直接 Docker 镜像引用被误清理**：reconcile 现在把无 `ImageTemplateId` 的比赛 Docker 引用解析回
   已注册模板，不再把仍被比赛使用的缓存判定为陈旧。
6. **容量为 0 的节点仍接收镜像**：分发筛选同时检查 `MaxContainers`/`MaxVms`，可独立关闭对应
   工作负载预分发，缺 KVM 不影响 Docker。
7. **结束比赛扫描随历史数据线性增长**：reconcile 只查询仍持有镜像引用且已结束的比赛，不再扫描
   全部历史比赛；单次失败被记录后保留后台服务，下一周期继续。
8. **rollout 清理卡死导致引用泄漏**：`CleanupPending` 目标重新进入幂等 destroy 队列；全部目标
   Destroyed 后才完成 rollout 并释放预分发引用。

### 13.3 验证

- `dotnet build src/GZCTF/GZCTF.csproj -c Release --no-restore`：通过，0 警告、0 错误。
- `dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj -c Release --no-restore`：通过，0 警告、0 错误。
- 后端全量测试：732 / 732 通过，覆盖镜像分发、Runtime、TeamLab、权限与迁移契约。

### 13.4 未处理项

- 历史或管理员直接登记的外部 Docker `RegistryUrl` 仍可绕过内部 Registry 直接拉取；若产品要求
  “所有 Docker 制品强制单一主副本”，需单独设计导入迁移与发布阻断，不能在 reconcile 中静默改写。
- 外部 API 契约与底座独立性维度仍未完成，不属于本次镜像专项。
- 真实 Registry 中断、节点离线恢复、跨进程清理竞态仍需在双 Worker 环境做故障注入验收。

---

## 14. Open API 与底座独立性收口（2026-07-27）

- 三个 Open 控制器均使用 scope policy，并在读取或操作 runtime/topology 前执行资源级授权。
- 所有写操作进入统一 `ApiOperation + Idempotency-Key` 链路，不在 Controller 内直接修改资源。
- 比赛 rollout 管理的 runtime 禁止从通用 Open API 直接 reset/destroy；护栏同时位于请求入口和
  后台 operation handler，避免排队后状态变化造成绕过。
- TeamLab 核心不再查询 `PenetrationTeamRuntimeBindings` 或 `Games`；比赛适配器通过
  `ITeamLabRuntimeManagerAuthorizationProvider` 提供管理授权，课程和外部系统可独立扩展。
- Open API 本轮未引入破坏性路由或响应模型变化；新增冲突使用稳定错误码
  `runtime_managed_by_rollout` 和 HTTP 409。

---

## 15. 管理员远程运维（进行中，2026-07-30）

- 已建立独立于运行环境管理权限的运维授权边界：管理员可访问全部资产，比赛所有者可访问所属比赛，其他人员必须获得单独的查看或运维授权。
- Windows RDP 与 Linux SSH 已使用相同的短期会话机制：平台核验资产、创建节点内部临时转发、创建只允许访问单一资产的 Guacamole 临时用户，并在会话关闭、超时、重置或销毁时回收。
- 运行详情已加入“资产运维”入口。管理员选择运行中的资产并填写原因后才能建立连接，原因、操作人、目标资产、协议、代次以及创建、连接、结束事件都会写入统一的 TeamLab 审计流。
- 镜像详情中已提供运维入口配置：容器固定使用网页终端，Linux 使用 SSH，Windows 使用 RDP；既可使用镜像已有账号，也可为已认证的托管虚拟机选择“平台为每个运行环境生成独立账号”。账户密码始终加密保存，查询接口仅返回“已配置”状态，不返回密码或私钥。
- 平台生成账号通过既有 Guest Supervisor 的受保护秘密通道下发，不增加来宾网卡或对外端口；Linux 创建 SSH 运维账号，Windows 创建本地 RDP 管理账号并开启既有远程桌面规则。Opaque 镜像不会被强制改造。
- 容器交互终端已完成平台代理链路：浏览器仅连接主服务，主服务通过节点认证转发 WebSocket；Agent 会以运行环境 ID、代次和容器 ID 验证归属后才执行交互 shell。平台自动创建来宾运维账户以及终端/桌面录制仍待完成，尚未部署验收。
- 本轮已验证：主服务 Release 构建、Agent Release 构建、前端 TypeScript strict 检查均通过，均为 0 错误。

### 15.1 本次收口（2026-07-30）

- SSH 运维凭据现在可区分密码和 PEM 私钥，私钥会作为 Guacamole SSH 的 `private-key` 参数传递，不再被错误当作密码。
- Windows 自动运维账号通过固定 SID `S-1-5-32-555` 加入远程桌面用户组，并按 `TermService` 服务启用对应防火墙规则，不依赖英文系统显示名称。
- 镜像运维端口由配置页面、会话服务、节点临时中继统一使用；管理员配置非标准 SSH/RDP 端口后，不会再退回固定的 22 或 3389。
- 镜像运维配置的前端 API 已改为执行运行时契约校验，不再直接使用 TypeScript 类型断言。
- 本地集中验证已通过：主服务、Agent、Guest Supervisor Release 构建均为 0 warning/0 error；前端 `pnpm check` 通过；`git diff --check` 通过。

### 15.2 待实机验收

- 使用一套 Docker、托管 Linux VM、Windows VM 场景分别验证网页终端、网页 SSH、网页 RDP。
- 验证 Guacamole 的临时连接和临时用户创建、一次性链接、关闭回收，以及主服务到节点临时中继的来源限制。
- 验证运行环境 reset/destroy 后会话、中继和运行时凭据均不可继续使用。
- 当前部署入口阻塞：2026-07-30 直连 `10.0.7.118:22` 超时，提供的 SOCKS5 `106.52.207.52:42891` 也无法建立 TCP 连接。需要可达的 SSH/FRP 映射后执行，不应通过猜测端口绕过。

### 15.3 可部署发布物与入口复核（2026-07-30）

- 已生成完整发布包：`artifacts/releases/teamlab-remote-access-20260730-r3/teamlab-remote-access-20260730-r3.tar.gz`，SHA-256 为 `b2ae43a061adba2e89759e17c5c4913a0cb5f8d03af4e156715cfcdd73818a29`。
- 发布清单包含 `GZCTF.dll`、`agent/gzctf-agent`、Linux/Windows 两套 Guest Supervisor，不能出现只更新主服务而节点和来宾协议仍旧的半部署状态。
- `scripts/deployment/deploy-gzctf-release.py` 已支持 SSH 私钥、可选端口和 SOCKS5；仍要求 sudo 密码用于原子激活、健康检查和失败回滚。
- 本次正式连接在 SOCKS5 CONNECT 到 `10.0.7.118:22` 时超时，发生在上传之前；因此主服务、数据库、Agent、镜像和运行环境均未被本次操作修改。

### 15.4 比赛内运维授权界面与发布包（2026-07-30）
- 比赛 TeamLab 管理页已补齐独立的“资产运维授权”区域：可搜索用户，授予“仅查看”或“可进入运维”，并可撤销已授予的权限。管理员和比赛所有者的既有权限不由此处修改。
- 前端授权 API 采用运行时响应解析，覆盖授权列表、保存与删除三个接口；新增的契约测试已通过。
- 已重新生成完整发布包：`artifacts/releases/teamlab-remote-access-20260730-r6/teamlab-remote-access-20260730-r6.tar.gz`，SHA-256 为 `1a0bc854b98f34e7ce23fa3e3f104853a9e02fb4756077b8fdf2f6bb90d6fa66`。
- 发布包生成过程已通过前端语言校验、lint、TypeScript strict、前端架构检查、74 个测试文件/206 个测试、生产构建及产物预算检查。
