# TeamLab 高性能执行面（Workflow A）审查与实机验收报告

- **日期**：2026-08-11
- **分支**：`codex/teamlab-high-performance-a`
- **审查范围**：共享契约提交 `5550f3a`、执行面提交 `d09fa8d`（+3386 行，43 文件）
- **方法**：5 路并行静态审查（逐函数逐字段，含调用链与上游源码核对）+ 双节点实机只读侦察 + 边界/性能/观测管线实机测试
- **结论**：V2 执行面存在 4 项 P0 阻断缺陷，当前不可验收；生产平台并发抗性弱、边界输入处理不严谨、观测管线持续丢数据。全程未修改任何代码。

---

## 目录

1. 审查范围与方法
2. 环境实况（服务器侦察）
3. 静态审查汇总（P0/P1/P2 合并清单）
4. 实机测试证据
5. 运维遗留与建议
6. 结论与验收判定
- 附录 A：审查面 A —— 资源边界与销毁（完整发现 + 逐函数清单）
- 附录 B：审查面 B —— 网络实现（完整发现 + 逐函数清单）
- 附录 C：审查面 C —— VM 与缓存（完整发现 + 逐函数清单）
- 附录 D：审查面 D —— 契约、并发与恢复（完整发现 + 逐函数/字段清单）
- 附录 E：审查面 E —— 全量代码质量（完整发现 + 逐函数清单）
- 附录 F：实机测试原始数据

---

## 1. 审查范围与方法

### 1.1 审查基线

- 共享契约：`5550f3a feat(teamlab): freeze execution plan contracts`
- 执行面：`d09fa8d feat(teamlab): add high-performance execution plane`
- 范围文件（43 个）：`src/GZCTF.TeamLab.Contracts/Execution/*`、`src/GZCTF.Agent/Services/TeamLab/*`、`Services/Vm/*`、`Controllers/TeamLabController.cs`、`ImageController.cs`、`Services/AgentCapabilityService.cs`、`DockerService.cs`、`AgentOperationGate.cs`、`Observation/ObservationPointRegistry.cs`、`src/GZCTF/Modules/TeamLab/*`、`Modules/Runtime/*`、`Modules/Fleet/*`、`Services/Fleet/AgentClient.cs`、`Models/Internal/Configs.cs`、测试 `TeamLabExecutionPlanV2Tests.cs`、`ImageDistributionServiceTests.cs`

### 1.2 审查方法

- 5 个并行审查 Agent 按审查面分工（A 资源边界与销毁 / B 网络实现 / C VM 与缓存 / D 契约并发恢复 / E 全量代码质量），全部只读，未修改任何文件。
- 关键 P0 由主审查人二次读码复核：OVS 命名（SHA-256 哈希实算）、libvirt 回调（源码精读）、多 router 策略（源码精读）、镜像命名（三方比对）。
- 实机测试全部非破坏：API 只读 + 边界输入 + 受限并发压测；未创建/删除生产资源，未修改配置。
- 测试期间清理了 3 个长期空转的 `dotnet-dump analyze` 进程（属运维清理，非代码改动）。

---

## 2. 环境实况（服务器侦察）

### 2.1 节点拓扑

| 节点 | 角色 | 服务 |
|---|---|---|
| 10.0.7.118 | 主站 + Agent + 基础设施 | gzctf:8080、gzctf-agent:5001、Docker、libvirt/KVM、PostgreSQL、Redis、registry:5000、Guacamole:8081/guacd:4822 |
| 10.0.7.125 | 纯 Agent 节点 | gzctf-agent:5001、Docker、libvirt/KVM |

- 平台节点数据（`/api/v1/nodes`）：双节点 `status=1` 在线、可调度，能力清单含 `teamlab.artifact-cache.v2 / infrastructure.v2 / observation.v2 / wireguard.v1` 等 24 项。
- Agent 限流清单（manifest）：`dockerCreates=2, vmCreates=1, dockerImageTransfers=2, vmImageTransfers=1, teamLabNetworkOperations=4, controlOperations=2, teamLabExecutionOperations=0(.118)/1(.125), artifactCleanupOperations=1`。
- **关键约束**：两节点能力清单均**不含** `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1`、`teamlab.libvirt.native.v1`；两节点均未安装 OVS/OVN（`/var/run/openvswitch` 不存在）。当前线上运行的是生产构建（非本分支），**V2 未部署、默认关闭**。

### 2.2 活跃业务状态

- 平台共 15 场比赛、多个 TeamLab topology/scope。
- 存在一个 **ready 状态的 Trial Runtime**（1 Docker + 2 VM，3 网段，运行在 worker-10.0.7.125），全链路事件可见（planning → fabric → scheduling → deploy → infrastructure → network → route → create → bootstrap → guestready），从创建到 ready 约 **89 秒**。
- 存在失败 Runtime 的**真实错误证据**：
  - `Runtime inventory validation failed: docker: runtime resource state is exited`（容器创建后退出，被 inventory 校验拦截，失败分类为 `operation.unclassified_failure` 并给出恢复动作 `rebuild_runtime / drain_runtime`）
  - `linux-vm:bootstrap: VM bootstrap did not complete: guest_bootstrap_step_failed (bootEpoch=1, guestSequence=5, nativeVmId=...)`（VM 引导链在第 5 步失败，失败后资源清理、fabric 租约释放均正常完成）

### 2.3 服务器卫生问题

- **3 个 `dotnet-dump analyze` 进程自 2026-08-08 起空转 3+ 天**（各占用 ~96% CPU，4 核节点 3 核被烧），load average 4.35，CPU idle 3.9%。已按运维指令清理，清理后 load 降至 3.48、CPU idle 65%。该问题直接放大了平台并发下的性能劣化（见 §4.3）。
- `/tmp/dump.dmp` 866MB 核心转储残留（清理进程时的分析对象，ELF x86-64 core file，来自 `/opt/gzctf/publish/GZCTF`），占用 `/tmp` 空间，是否删除待确认。
- `.118` 存在 1 台关机 VM `tl97-ad-dc`；`.125` 有 qemu 进程运行 4 周+（内存 4GB、CPU 116%），疑似长期遗留 VM，需确认是否仍被引用。
- `.118` swap 使用 3.9GB/4GB，内存压力大（11GB 总量）。
- WireGuard `gzctf-fabric`（10.250.0.1 ↔ .125:39560）实时握手正常（17s 前）；`wg-tlproxy`（10.252.0.1 → 203.195.157.191:51820）45s 前握手、累计 9.79GiB 收 607MiB 发（公网入口隧道活跃）。

---

## 3. 静态审查汇总（P0/P1/P2 合并清单）

> 注：本节为全部审查面的合并视图；各审查面的完整发现、验证过程与逐函数清单见附录 A–E。

### 3.1 P0（5 项，3 项经主审查人二次复核）

| # | 审查面 | 发现 | 核心位置 |
|---|---|---|---|
| P0-1 | A/B | OVS 端口清理名称与创建名称不一致 → 每次 V2 cleanup 静默泄漏 OVS Port/Interface（哈希实算复现） | `TeamLabExecutionPlanExecutor.cs:299-304` vs `LinuxNetworkAttachmentService.cs:22-24,66-71` |
| P0-2 | C | libvirt 事件回调释放 libvirt 持有的 domain 指针 → 原生 Use-After-Free，可崩溃 Agent | `LibvirtNativeInterop.cs:169-179` |
| P0-3 | C | VM base 镜像文件名三方不一致 → V2 VM 永远无法启动 | `LibvirtTeamLabProvider.cs:193-198` vs `ImageController.cs:105-107,471-482` |
| P0-4 | B/E | 多 router + ForwardPolicies → 第 2 个 router 引用的 named-uuid 未定义 → OVN 事务必然失败 | `TeamLabOvnNetworkProvider.cs:93-95` vs `:258-259` |
| P0-5 | C | VM base 镜像替换路径无 backing-chain 闸门：模板 hash 变化时运行中 VM 的 backing 被原地换掉 | `ImageController.cs:110-135` |

### 3.2 P1（合并 24 项）

| # | 审查面 | 发现 | 核心位置 |
|---|---|---|---|
| P1-1 | A | 失败节点补偿仅依赖 Agent 自身；Agent 崩溃 + V2 标记未落盘 → V2 VM/OVN/OVS 资源永久泄漏，旧路径 fallback 无法清理 | `TeamLabShardDeploymentService.cs:282-297,81`；`TeamLabRuntimeCleanupService.cs:89-93,363-374` |
| P1-2 | A | 主站 `VerifyRuntimeInventoryAsync` 与 Agent V2 VM inventory 源不一致 → 含 VM 计划部署永久卡死（KvmService 只认 `gzctf-generation=` description，V2 XML 无 description） | `TeamLabShardDeploymentService.cs:506-523`；`KvmService.cs:346-363,483-487`；`LibvirtTeamLabProvider.cs:230-247` |
| P1-3 | A/C | apply 后 inventory 读取异常逃逸补偿 → 已建资源泄漏且无 journal | `TeamLabExecutionPlanExecutor.cs:126-137` |
| P1-4 | A | OVSDB JSON-RPC 无内部超时 → 无限等待（有界性完全依赖调用方） | `OvsdbJsonRpcClient.cs:111-155`；`LinuxNetworkAttachmentService.cs:88` |
| P1-5 | B | OVSDB 客户端无超时 + 全 Agent 单一信号量串行化 → OVN 仲裁丢失挂死整个节点网络平面；每次操作新建连接，50 资产≈200 次串行往返 | `OvsdbJsonRpcClient.cs:12,25-28,67,144-155`；`Program.cs:51` |
| P1-6 | B | OVN "already applied" 快路径只核对 switch+port，不核对 router/DHCP/ACL/route/policy → 半态时静默缺资源 | `TeamLabOvnNetworkProvider.cs:45-59` |
| P1-7 | B | ForwardPolicies 只挂到 `Routers.Take(1)`，多 router 时策略不对称（与 P0-4 同源，写入侧证据） | `TeamLabOvnNetworkProvider.cs:93-95` |
| P1-8 | B | ACL/静态路由无去重 → 同名 uuid-name 导致 apply 永久失败 | `TeamLabOvnNetworkProvider.cs:384-385` |
| P1-9 | D | 同身份不同 digest 不拒绝覆盖：journal 键不含 digest，仅 OVN 层偶然保护，Docker/VM 复用路径不校验 digest | `TeamLabExecutionPlanExecutor.cs:39-49`；`DockerService.cs:156-173`；`LibvirtTeamLabProvider.cs:44-64,205-207` |
| P1-10 | D | V2 Docker 资产资源规格与 FLAG/环境变量不生效（默认 64MB/1CPU，无 secrets/env/flag） | `TeamLabExecutionPlanExecutor.cs:309-324`；`ContainerModels.cs:15-16` |
| P1-11 | D | 失败分片无补偿兜底触发：残留与 OVN 部分状态死锁（部分端口 → 同计划重试永远失败） | `TeamLabShardDeploymentService.cs:284-297`；`TeamLabOvnNetworkProvider.cs:45-61` |
| P1-12 | D/E | 健康检查意图缺端口 → 编译器产出 Port=0 → 整个计划被 IsValid 拒绝 | `TeamLabExecutionPlanCompiler.cs:99`；`TeamLabExecutionPlanV2.cs:58` |
| P1-13 | D | DHCP 静态绑定与 DNS 记录计入 digest 但执行端不落地 → digest 与执行效果漂移 | `TeamLabOvnNetworkProvider.cs:225-241`；`TeamLabExecutionPlanV2.cs:47` |
| P1-14 | C | backing-chain 引用检查对 TeamLab overlay 目录不可见 → base 可被删/被换（同 P0-5） | `VmImageBackingChainInspector.cs:36`；`ImageController.cs:118-134,228-249` |
| P1-15 | C | 24h 保留期删除"仍活跃"release 的引用 → 多日赛事缓存被清 | `ImageDistributionService.cs:255,273-277` |
| P1-16 | C | `EnsureCacheRemoved` 失败被标记为不可重试，与 VM 侧 5 分钟重试语义不一致 | `ImageDistributionService.cs:1249-1255,1229-1247` |
| P1-17 | C | `qemu-img create` 无超时且持全局锁 → 节点全部 VM 操作永久挂起 | `LibvirtTeamLabProvider.cs:279-296,39` |
| P1-18 | C | 跨网络 PortKey 重名 → MAC 取错/重复 | `LibvirtTeamLabProvider.cs:252-255` |
| P1-19 | E | 计划内资产串行执行（并行度=1）× AgentClient 60s 截止 → M/L 规模必然超时 | `TeamLabExecutionPlanExecutor.cs:81-85,203`；`AgentClient.cs:21,521` |
| P1-20 | E | 计划声明 DHCP 租约/DNS 但 OVN provider 从不物化（同 P1-13） | `TeamLabOvnNetworkProvider.cs:212-241` |
| P1-21 | E | V2 失败不回落到 V1（同 P1-12，主站侧无 fallback） | `TeamLabExecutionPlanCompiler.cs:96-100` |
| P1-22 | D | `NormalizeDigest` 对无前缀 digest 会 Substring 越界（调用流被前置拦截，静态公共方法脆弱） | `TeamLabExecutionPlanV2.cs:131-138` |
| P1-23 | D | 事件契约 `Detail["message"]` 携带异常原文（socket 路径/JSON-RPC/docker/libvirt 消息）；Stage/Outcome 自由字符串 | `TeamLabExecutionEventV2.cs:9-13`；`TeamLabExecutionPlanExecutor.cs:452-456,106` |
| P1-24 | A | V2 cleanup 重编译依赖 WorkerNode 行存在与 Fabric 租约未释放 → 已 Destroyed 的 shard 编译恒失败，runtime 永久 CleanupPending | `TeamLabRouteApplicationService.cs:173-174,213-217`；`TeamLabRuntimeCleanupService.cs:75-87` |

### 3.3 P2（合并 27 项）

| # | 审查面 | 发现 | 核心位置 |
|---|---|---|---|
| P2-1 | A | executor `executionLocks` 按 (runtime,generation,shard) 无界增长，永不回收 | `TeamLabExecutionPlanExecutor.cs:26,34-35` |
| P2-2 | A | journal 无界增长且重启即失（未文档化） | `TeamLabExecutionEventJournal.cs:8` |
| P2-3 | A/D/E | 死代码/冗余：`eventArray` 死赋值（executor:126）；`TeamLabOvnApplyResult.Digest` 从未消费（`TeamLabOvnNetworkProvider.cs:410-415`）；`NormalizeDigest` 越界分支不可达 | executor:126,138；provider:410-415；`TeamLabExecutionPlanV2.cs:131-138` |
| P2-4 | A | 同代不同 digest 的 re-apply 会销毁现有容器（"拒绝覆盖"语义过激进） | executor:244-273；`DockerService.cs:165-170` |
| P2-5 | B | OVSDB 响应 `id` 从不与请求 id 比对 | `OvsdbJsonRpcClient.cs:67` |
| P2-6 | B | 每次请求重开 socket + 握手 + N+1 select，50 资产≈200 次串行往返（性能） | `OvsdbJsonRpcClient.cs:28,144-155`；provider:34-61 |
| P2-7 | B | `WaitForExitAsync` 无命令级超时，`nsenter`/`ip` 挂起无限阻塞该资产 | `LinuxNetworkAttachmentService.cs:88` |
| P2-8 | B | OVS Port/Interface delete 无 external_ids 所有权校验 | `TeamLabOvsAttachmentProvider.cs:139-150` |
| P2-9 | B | bridge mutate delete 不验证端口确实挂在 br-int，匹配 0 行静默通过 | `TeamLabOvsAttachmentProvider.cs:131-138` |
| P2-10 | B | ACL/policy match 字符串直接插值，无 CIDR/协议字符集校验 | `TeamLabOvnNetworkProvider.cs:338,344-357` |
| P2-11 | B | `Networks.Count==0` 直接返回成功，声明了 router 的 control 被静默跳过 | `TeamLabOvnNetworkProvider.cs:28-29` |
| P2-12 | B | lease_time 硬编码 3600，无配置面 | `TeamLabOvnNetworkProvider.cs:225-241` |
| P2-13 | D | `IsValid` 格式校验缺口簇：MAC 格式、DHCP 租约内容、DNS 记录、Route.CIDR/NextHop、Policy CIDR、NetworkControl.RouterNamespace、RouteVersion==0、NetworkIntent.Kind 值域均未校验 | `TeamLabExecutionPlanV2.cs:41-105` |
| P2-14 | D | 主站限流配置缓存永久化：`_dispatchLimits.GetOrAdd` 永不失效，`CancellationToken.None` | `AgentTeamLabNodeExecutor.cs:37,1137-1145,1156-1165` |
| P2-15 | D | gate 容量在首次创建后固化，manifest 变更后限流参数不生效 | `NodeDispatchLimiter.cs:62,78` |
| P2-16 | D | apply 与 cleanup 共享 TeamLabExecution 门（容量 1），长 apply 阻塞 cleanup | `AgentTeamLabNodeExecutor.cs:44,55` |
| P2-17 | D | V2 资产并发对主站账本不可见 → 与 legacy 突发并发时估算容量可能超卖（Agent 默认串行兜底） | `NodeDispatchLimiter.cs:20-52`；executor:84 |
| P2-18 | D | 死字段与不可达回退：`TeamLabArtifactReferenceV2` 零消费；`NetworkControl.Fabric/RouteVersion/RouterNamespace` 未消费仍计入 digest；`TeamLabNetworkRouteV2.PortKey` 恒空串；`NetworkIntent.Kind` 恒 "switch"；`PortKey/AssetKey` 的 `??` 回退不可达；`LibvirtTeamLabProvider.cs:255` 回退 MAC `52:54:00:00:00:01` 为危险死回退 | compiler:105-108,117-133,159,173-185 |
| P2-19 | D | 重复 DTO 约 20 组：`AgentClient.cs:1946-2314` 与 Agent `TeamLabModels.cs` 整段重复；`AgentClient.cs:1669-1830` 与 `VmModels.cs` 重复；`AgentCommandResult`、`AgentImageCacheCleanupResult` 等 | `AgentClient.cs`（单文件 2314 行） |
| P2-20 | D | record IP 前缀剥离不一致：DHCP/DNS 记录经 `AddressWithoutPrefix`，端口 IP 用原始值 → `/24` 前缀进入 OVN `addresses` 导致 transact 失败 | compiler:43 vs :62,66 |
| P2-21 | D | `Gateway()` 边界：`prefix=0` 时掩码全 1，`prefix=32` 时溢出回绕到 0 | `AgentTeamLabNodeExecutor.cs:1169-1176` |
| P2-22 | D | `Math.Abs(int.MinValue)` 溢出：`StableChallengeId` 哈希恰为 `int.MinValue` 时抛 OverflowException（同一 assetKey 确定命中则永远失败） | executor:468-470 |
| P2-23 | D | 事件排序冗余：`ConcurrentQueue` + 三次 `OrderBy(OccurredAt)`（同刻度排序不稳定） | executor:126,138,160,236 |
| P2-24 | C | libvirt 事件常量除 lifecycle 外全是死代码，`DomainEventResumed=7` 错误（真实 4）；`EventRegisterDefaultImpl` 在 `ConnectOpen` 之后调用违反文档顺序；`StopAsync` 空实现；无 `virGetLastError` 错误码映射 | `LibvirtNativeInterop.cs:11-15,84-110,128-137,181-185` |
| P2-25 | C | `GetConnection()` 非原子懒初始化，冷启动并发可双开连接；Provider 无 IDisposable，连接进程期不关闭 | `LibvirtTeamLabProvider.cs:142-143` |
| P2-26 | C | Docker 清理分支 `if (!string.IsNullOrWhiteSpace(image))`：模板行被删后跳过 Agent 删除但仍删分发记录，违反"Present=false 才删"不变量 | `ImageDistributionService.cs:985-991` |
| P2-27 | C | `CleanupTemplateForDeletionAsync` 的 claim 检查与清理间存在竞态窗口 → 孤立缓存 | `ImageDistributionService.cs:296-332` |

### 3.4 P3（代码质量，来自审查面 E）

| # | 发现 | 位置 |
|---|---|---|
| P3-1 | `eventArray` 第一次 OrderBy 后立即被覆盖，死计算 | `TeamLabExecutionPlanExecutor.cs:126,138` |
| P3-2 | `TeamLabArtifactReferenceV2`/`plan.Artifacts` 全链路无人消费（仅参与 digest），死载荷 | compiler:105-108 + 契约 |
| P3-3 | `NodeDispatchLimiter` 的 `ArtifactCleanup` 枚举成员无调用者，死限流分支 | `NodeDispatchLimiter.cs:17,32,44`；`AgentOperationGate.cs:13,33` |
| P3-4 | `TeamLabOvnApplyResult.Digest` 无人读取 | `TeamLabOvnNetworkProvider.cs:410-415` |
| P3-5 | `StableName` 第三参 `network` 恒为 ""，死参数 | `LinuxNetworkAttachmentService.cs:66-71,23-24` |
| P3-6 | `HasNativeLibvirt`：`TryLoad` 成功后 `Release` 短路不执行 → 句柄泄漏（一次性检测，影响小） | `AgentCapabilityService.cs:121-131` |
| P3-7 | `GetInventory` 在 `lifecycleLock` 外调 `GetConnection()`，懒初始化竞态（双连接/泄漏）；`EnsureRunningAsync` 内同类 | `LibvirtTeamLabProvider.cs:142,145-164` |
| P3-8 | `[FromBody]` 为 null 时 `request.Plan` NRE→500，缺空体守卫 | `TeamLabController.cs:36,47` |
| P3-9 | `_dispatchLimits` 一次性缓存，能力重同步后限流永不刷新 | `AgentTeamLabNodeExecutor.cs:37,1137-1145` |
| P3-10 | `["row"]` 缩进错位（可读性） | `TeamLabOvnNetworkProvider.cs:207` |
| P3-11 | DomainName/sanitize 逻辑两处重复实现（含 48 字符截断），改一处漏一处 | compiler:189-197 vs `LibvirtTeamLabProvider.cs:210-220` |
| P3-12 | `Where`/进程启动（`RunAsync`/`SucceedsAsync`）四处重复，可收敛 | `OvsdbJsonRpcClient.cs:390`；`TeamLabOvsAttachmentProvider.cs:161-162`；`LibvirtTeamLabProvider.cs:279-296`；`LinuxNetworkAttachmentService.cs:73-110` |
| P3-13 | `Plan()` 假 digest 使测试仅依赖"错误检查先于 digest 检查"的顺序，脆弱 | `TeamLabExecutionPlanV2Tests.cs:150-160` |
| P3-14 | `imagePreparation` 以 `runtime.Shards.Single(...)` 每资产查找，O(N²)；任务在 capability 判定前创建，失败仅靠 catch 兜底 | `TeamLabShardDeploymentService.cs:47-56` |

### 3.5 已验证通过项（合并）

**A 面（资源边界与销毁）**
1. Generation 围栏：所有命名含 generation（OVN `TeamLabOvnNaming.cs:9-13`、OVS 接口名、Docker label `GZCTF.Generation`、libvirt domain/overlay `LibvirtTeamLabProvider.cs:183,210-211`），删除均按名（即按代）过滤；跨代重放 digest 冲突被拒（provider:42-43）；旧路径 `BuildCleanupRequest` 全部按 `runtime.Generation` 过滤。
2. 补偿精确性：主站只补偿本次成功分片（`TeamLabShardDeploymentService.cs:288-289`）；Agent apply 失败/取消均自补偿且 2 分钟有界（executor:110-124,140-161）；cleanup 全程幂等（docker destroy 幂等、OVN/OVS 按名删不存在即 no-op、veth delete allowFailure）；apply/cleanup 共用 per-shard `SemaphoreSlim` 串行化，无并发双删路径。
3. 无固定 sleep / 无自动重试掩盖：健康检查 10s 界（executor:24,410-450）、`ExecuteContainerCommandAsync` 10s 界（`DockerService.cs:400-456`）、主站 HTTP 60s 界（`AgentClient.cs:21,521`）、补偿 2min 界。
4. 状态恢复不从日志重建：Agent 重启后 journal 为空，恢复路径是幂等重放（OVN AlreadyApplied、docker 复用、VM `EnsureRunningAsync` 幂等续跑），状态全部来自数据库/确定性命名/inventory；`TeamLabRuntimeGenerationStore` 的 active-generation.json 只服务旧路径。
5. 限流与超卖：`NodeDispatchLimiter.cs:37-50` `TeamLabExecution` safetyCap=1（每节点串行），与 Agent 侧执行锁一致，未发现隐蔽队列。

**B 面（网络实现）**
6. 无 Shell 主路径：OVN/OVS 全部经 OVSDB JSON-RPC；`LinuxNetworkAttachmentService` 用 `Process.ArgumentList`（`UseShellExecute=false`）；全 Agent grep `ovn-nbctl|ovs-vsctl|ovsdb-tool` 0 命中。`TeamLabNetworkService.cs:729-739,782-784` 存在 WireGuard shell 拼接但属 V2 关闭时的旧共享入口，且名称经 `ValidateLinuxName` 校验。
7. OVSDB 事务原子性：apply/remove/OVS attach 均为单次 `transact`（RFC 7047 §4.1.3 all-or-nothing）；`OvsdbJsonRpcClient.cs:77-82` 正确扫描逐操作 error。
8. named-uuid 前向引用合法性（对照 OVS 上游 `lib/ovsdb-data.c`、`ovsdb/execution.c`）：`MutateRouter` 先于 `MutateRouterPort/MutateStaticRoute/MutateRouterPolicy`、`MutateNetwork` 先于 `MutatePort` 的顺序合法；仅两次 insert 同名才报 `duplicate uuid-name`。
9. monitor/重连一致性：客户端未实现 monitor（无泄漏面）；无持久会话，每次调用全新连接。
10. 命名稳定性：OVN 逻辑名嵌 32-hex runtime 与 generation；OVSDB 行名 128-bit SHA-256；veth 名 15 字节符合 IFNAMSIZ。跨代误删需 128-bit 碰撞，实际不可能（P0-1 是名字不一致而非碰撞）。
11. 收敛证据：存在性判定全部为真实 OVSDB select，零固定 sleep、零日志猜测。局限：OVN SB chassis binding 与 vswitchd 实际绑定无确认，唯一来宾门是容器内 touch 信号 + TCP/HTTP 健康检查（executor:353-368,402-450），首轮探测可能与 vswitchd 绑定异步竞态。
12. 越权接入：veth 仅经 OVSDB 写入获批集成桥；`ExistingUuid`（`TeamLabOvsAttachmentProvider.cs:175-188`）强制 external_ids 三字段所有权围栏，异主直接抛错拒改。
13. WireGuard 仍为共享玩家入口，executor 对 WG 零调用。
14. 日志安全（网络面）：失败路径仅记录异常消息与 OVSDB error 对象，无 token/私钥；WG 私钥路径 `<redacted>`。

**C 面（VM 与缓存）**
15. P/Invoke 签名：17 个 DllImport 的 `CallingConvention.Cdecl`、参数/返回类型与 libvirt 头文件一致；`LPStr` 在 Linux 下 UTF-8 安全；无 `SetLastError`/`ExceptionInfo` 泄漏；`StringBuilder(37)` 匹配 `VIR_UUID_STRING_BUFLEN`；5 参生命周期回调 ABI 兼容。
16. Generation 围栏（VM 侧）：domain 名与编译器逐字节一致；overlay 路径含 generation；UUID SHA1(runtime:generation:assetKey) v5 与 `virDomainGetUUIDString` 输出一致；`EnsureRunningAsync` 先验 ResourceId/DomainIdentity 再验 UUID；`Destroy`/`GetInventory` 仅按代派生名查找。NVRAM：XML 未配置 UEFI，系统从未创建 NVRAM 文件，`UndefineNvram` 标志为空操作（"无 NVRAM"而非"已清理"，无跨代风险）。
17. 零引用判定：`CleanupRecordAsync` 在事务 + `pg_advisory_xact_lock` 内重载并复核引用（`:973-981`）；`QueueCleanup` 对活动 claim 的 Pulling 记录拒排（`:858-882`）；重复释放幂等（`:921-926`）。
18. `Present=false` 才删记录：Agent 删除后返回真实 inventory（`ImageController.cs:60-61,258-261`），主站 `EnsureCacheRemoved` 仅 `IsClean` 才移除记录（`AgentClient.cs:1908`）；VM 占用时主站进入 `CleanupPending+Retryable` 等待（`:995-1016`）；测试 `ProcessClaimedAsync_CleanupKeepsRecordWhenAgentInventoryStillHasCache` 覆盖残留保留语义。
19. 模板库 OCI 主制品：清理路径只调 Agent 缓存删除端点，全链路无对 vmRegistry OCI 制品或 Registry manifest 的删除调用 → 主制品不会被 runtime cleanup 误删；`ArtifactVerification` 引用在认证 job Pending/Running 时保持（`:233-240,266-268`）。
20. Docker 删除语义：`Force=false`（`DockerService.cs:810`），被容器引用时 Docker 409 → 抛错保留记录，不强制删。
21. 磁盘原子性：`TeamLabRuntimeGenerationStore.WriteAsync` 临时文件 + Flush(true) + Move 原子写，`ClearIfActiveAsync` 先比对 generation 再删（`:59-65`）。

**D 面（契约、并发与恢复）**
22. digest 确定性：编译输入有序且确定（资产按 TopologyKey 排序 `TeamLabShardDeploymentService.cs:458`；records 按 Hostname+IpAddress 排序 `TeamLabRouteApplicationService.cs:243-259`；ShardKey InvariantCulture `:473`）；digest 对 PlanDigest 自排除后序列化（`TeamLabExecutionPlanV2.cs:107-108`），编译端 `ComputeDigest`（compiler:150-152）与校验端算法一致；计划无字典/枚举，相同输入同 digest。generation 与 ShardKey 均计入 digest 且进入全部稳定命名，不同 generation/分片互不认。
23. 重复提交（同 digest）：executor:39-46 先查 journal，命中后重读实时 inventory，全部资产 running 才返回 `AlreadyApplied=true + 收敛 inventory`；不收敛则 Remove 后重放，靠 OVN already-applied、Docker 按名复用、VM 按名+稳定 UUID resume 幂等收敛。Agent 重启后重放同路径收敛，不从日志重建。
24. 字段完整性（IsValid）：资产键黑名单（:69-78）、端口↔资产引用完整性（:81-87）、网络键唯一（:34）、附件引用的网络/端口必须存在（:61-63）、digest 自校验（:107-113）——防默认值执行的关键闸已具备。
25. 节点分类限流与 DeploymentQueueTicket：未新建第二队列；V2 与 legacy 同走单一 runtime 级 `DeploymentQueueTicket`（`TeamLabRuntimeOperationHandler.cs:234-251`），票内再经 `NodeDispatchLimiter` 分类门；`TeamLabExecution` 门容量 1 且安全上限封顶，manifest 缺失时默认 1（`:50 GetValueOrDefault(1)`）。
26. Redis/进程通知只是唤醒：`RuntimeSignalService.WaitForCoreAsync`（`RuntimeSignalService.cs:145-166`）每轮先查 PostgreSQL `AgentRuntimeSignals`，再 `wakeup.WaitAsync(1s)`；顺序/幂等/防重放有唯一约束 + PayloadHash 冲突检测（:65-105）。Agent 信号 journal 落盘（`AgentRuntimeSignalPublisher.cs:23-30`），Channel 仅唤醒，重启后 journal 重放（:74,105）。
27. 事件契约不泄露：`TeamLabExecutionPlanV2` 只含逻辑键/CIDR/MAC/digest，无 OVN 表、libvirt XML、Docker 对象、shell 命令。

**E 面（代码质量）**
28. Secret 泄漏逐条审计：范围内 30+ 处日志/异常仅含 runtime/generation/asset/node/template/image/hash/端口/MAC/IP；`OvsdbJsonRpcClient.cs:70` Debug 级打印 OVSDB error payload 含事务操作回显（MAC/IP）但无凭据；WireGuard 私钥只在请求体（`AgentTeamLabNodeExecutor.cs:392`）不外泄日志。
29. 补偿不继承取消：`CleanupCoreAsync` 用 2 分钟独立 CTS，docker finally 用 `CancellationToken.None`（executor:114,147,377）。
30. 镜像缓存清理以 inventory 为准：`EnsureCacheRemoved` 残留即抛错保留记录（`ImageDistributionService.cs:1249-1257`），Agent 404 → `Clean`。
31. 职责边界：Controller 仅做授权/路由/映射，不直接编排 Docker/OVN/libvirt（TeamLabController 委托 executor）。
32. 旧路径隔离：`EnableExecutionPlanV2=false` 时新端点 404、能力不含 V2 feature、主站走 V1。

---

## 4. 实机测试证据

### 4.1 全链路（ready Runtime 019fea65-bbeb-74c3-ba8a-ff3de04dddbf）

- 1 Docker + 2 VM（linux/windows）、3 网段（10.80.1.0/24、172.20.1.0/24、192.168.81.0/24），单分片部署于 worker-10.0.7.125。
- 事件链：planning → fabric → scheduling → deploy → infrastructure → network → route → create(3) → bootstrap → guestready → **ready，全程 89 秒**（1786343963 → 1786344053）。
- 资产 inventory：docker 容器 hash id、linux-vm `tl119-linux-vm`、windows-vm `tl119-wi-d413f0`，全部 `status=5 ready`。
- 流量观测实时产出真实 DNS/ARP 流（VM↔网关，`172.20.1.1:53 ↔ 172.20.1.20`），`openForAccess=true`。
- 失败 Runtime 真实错误被正确分类（`operation.unclassified_failure` + recovery actions + subStages），inventory 校验机制（`docker: runtime resource state is exited`）与 VM bootstrap 失败后的资源清理、fabric 租约释放均工作正常。

### 4.2 边界测试

| 用例 | 结果 |
|---|---|
| 无 cookie / 垃圾 cookie 访问管理 API（nodes/runtimes） | 401 ✓ |
| 畸形 GUID 路由（`/api/v1/nodes/not-a-guid`、`/not-a-guid/resources`、`/api/admin/teamlab/runtimes/not-a-guid/events`、`/api/v1/deployment-queue/not-a-guid`） | **200 + SPA HTML**（路由不匹配落入前端兜底，客户端无法区分，监控误判）✗ |
| 合法但不存在 GUID | 404 JSON ✓ |
| 分页参数 `count=-1/0/abc/99999`、`skip=-5`、`count=` + 30 个 9 | **全部 200，无校验** ✗ |
| 畸形登录体（空 JSON/坏 JSON/缺字段/`null`/字符串） | 规范 400 校验错误 ✓ |
| 速率限制 | **无**：60 连发 `/api/info` 全 200，无 429 ✗ |

### 4.3 性能测试（真实登录会话，40 并发）

| 接口 | 基线（串行） | 40 并发 p50 | 40 并发 p95 | 40 并发 max |
|---|---|---|---|---|
| login | 415ms | 3,822ms | 3,856ms | 4,029ms |
| /api/v1/nodes | 147ms | 465ms | 2,455ms | 2,924ms |
| /api/admin/teamlab/runtimes | 135ms | 371ms | 13,599ms | 53,609ms |
| /api/v1/deployment-queue | 146ms | 13,602ms | 53,969ms | 54,153ms |
| traffic/flows | 229ms | 41,400ms | 41,981ms | 54,988ms |

- 60 并发首次压测（脚本登录顺序缺陷，请求全部未认证）同样出现 **401 响应耗时 30 秒** 的现象（queue/flows/topologies/scopes p50≈30s，login 30.9s/200），佐证瓶颈在服务端而非认证逻辑。
- 100 并发混合压测（cookie 构造缺陷导致部分 401）中 nodes/topologies 出现 p95≈30s、max 41s，随后登录接口出现 30s 读超时。
- 服务器侧证据：PostgreSQL 仅 2 个活跃查询、37 个 idle 连接（池未满）、Redis 24MB/3 客户端正常、主站日志无 500/异常 → **瓶颈为 CPU 饱和 + 线程池饥饿**：测试期间 4 核节点被 3 个 `dotnet-dump` 僵尸进程占用 3 核（load 4.35），平台无并发保护（无速率限制、无请求队列上限），40 并发即出现 13-54 秒延迟。

### 4.4 观测管线

- `traffic/paths`：`completeness.complete=false, droppedRecords=2,125,088`
- 10 分钟复测：`droppedRecords` 增至 **2,157,883**（+32,795，持续 ~55 条/秒丢弃）
- 主站日志每 ~5 秒出现 `Traffic observation dropped records because of backpressure or local capture loss`；`EfOperationalEventWriter` 与 `PostgresTeamLabTrafficBatchWriter`（received=6~18 inserted）高频写。
- 3 轮 × 5s 分页拉取可稳定取得 300 条 flow 记录（分页游标工作正常），但完整性标记始终 false。
- **结论**：观测管线长期过载丢数据，"流量路径全量可回放"验收项不成立；根因与 §4.3 相同的资源竞争 + 观测写放大。

### 4.5 代码级验证

- `dotnet test` 定向执行 `TeamLabExecutionPlanV2Tests` + `TeamLabCleanupOwnershipTests` + `TeamLabInterfaceNamingTests`：**20/20 通过**，0 失败，退出码 0。
- OVS 命名不一致已用 SHA-256 实算复现：identity `12345678-1234-1234-1234-123456789012:5:web:net0` → 创建侧 `tlh116dcbd78c1f`、清理侧 `tlh2e6563155043`。
- 主站 4 个关键 API 基线实测（admin cookie）：login 415ms、nodes 147ms、runtimes 135ms、queue 146ms、flows 229ms、topologies 150ms、scopes 115ms。

---

## 5. 运维遗留与建议

1. `/tmp/dump.dmp`（866MB）与 `.125` 长期运行的 qemu 实例（4 周+，内存 4GB、CPU 116%）需确认引用后处置。
2. 生产平台并发保护缺失：建议在验收前评估速率限制/请求队列上限；先清理僵尸进程再复测基线（本次压测受其放大，清理后需重新测量以获取干净基线）。
3. 边界输入处理：SPA 兜底吞掉未知 `/api/*` 路由（200 HTML）、分页参数零校验，建议修复并纳入监控。
4. 平台侧 `teamLabTunnelLastHandshake` 指标与 WG 实际握手不一致（API 显示 33 天前，`wg show` 显示 17 秒前）——Agent 上报的握手时间戳指标疑似陈旧/口径错误，建议核查。
5. 观测管线丢包需优先定位写放大路径（EfOperationalEventWriter 逐批写 PG 与 backpressure 丢包），建议在 V2 验收前给出丢包率基线。

---

## 6. 结论与验收判定

### 6.1 V2 执行面（本分支）

**不可验收**。5 项 P0 阻断（§3.1）：

1. P0-1 OVS 端口清理名称错配 → 每次 cleanup 静默泄漏 OVS Port/Interface；
2. P0-2 libvirt 事件回调 UAF → Agent 崩溃风险；
3. P0-3 VM base 镜像命名三方不一致 → V2 VM 必失败；
4. P0-4 多 router + ForwardPolicies → OVN 事务必失败；
5. P0-5 VM base 镜像替换无 backing 闸门 → 运行中 VM 磁盘被换。

P1 中"同身份不同 digest 拒绝覆盖"（P1-9）、"失败分片收敛"（P1-11）、"规模超时"（P1-19）三条直接违反交接文档验收标准；"V2 VM 必然失败"（P1-2）与 P0-3 叠加使含 VM 计划不可用。**建议修复后再进入独立节点实机矩阵**；实机矩阵需按交接文档在具备 OVS/OVN 的独立验收节点执行（当前双节点不具备条件，生产环境不允许装包）。

### 6.2 生产平台（当前线上构建）

- 功能链路可用，错误分类与恢复动作机制工作正常；
- 并发抗性弱（40 并发即 13-54s 延迟，受僵尸进程放大）；
- 边界输入不严谨（200 HTML、参数零校验、无速率限制）；
- 观测管线持续丢数据（~55 条/秒）。

---

# 附录 A：审查面 A —— 资源边界与销毁

**审查范围**：`TeamLabShardDeploymentService.cs`、`TeamLabRuntimeCleanupService.cs`、`TeamLabExecutionPlanExecutor.cs`、`TeamLabExecutionEventJournal.cs` + 交叉验证 OVN/OVS provider、libvirt provider、DockerService、KvmService、AgentClient、NodeDispatchLimiter、编译器与命名工具。

## A.1 审查要点逐条结论

| 审查要点 | 结论 |
|---|---|
| generation N 不操作 generation N+1 | 通过：所有命名含 generation，删除均按名过滤；跨代重放 digest 冲突被拒 |
| 补偿只清理本次成功计划，不双重清理 | 通过：主站只补偿 `results.Where(Success)`；Agent 补偿 2min 有界；cleanup 幂等 |
| V2 标记未持久化时 Agent 自身补偿收敛 | **不成立**：失败节点清理完全托付 Agent 进程内补偿，Agent 崩溃后 V2 VM/OVN/OVS 永久泄漏（P1-1） |
| 无无限等待 | **部分不成立**：OVSDB 客户端无内部超时（P1-4）、`WaitForExitAsync` 无命令超时、executor:380 finally 用 `CancellationToken.None` |

## A.2 完整发现

**P0-A1（= 全局 P0-1）OVS 端口/接口清理名称与创建名称不一致 → 每次 V2 cleanup 静默泄漏**
- 证据：executor `StableHostInterface`（:299-304，哈希输入无尾冒号）vs Linux 侧 `StableName`（:66-71，哈希输入 `$"{asset}:{network}"` 带尾冒号，network 为空串）。实测同 identity 产出 `tlh116dcbd78c1f` vs `tlh2e6563155043`。
- 触发：任何含 Docker 资产且带网络 attach 的 V2 apply → cleanup（正常销毁、失败补偿、取消补偿三条路径均走 executor:215/381）。
- 后果：`ovs.RemoveAsync` 按错名查 Port 得 0 行 → 只执行对不存在名字的 delete（no-op）→ 返回 Success；veth 被正确删除，但 OVS `Port`/`Interface` 行与 bridge `ports` 成员永久留存，主站与 Agent 均不可见；同代重试复用 identity，跨代永久堆积；残留 Port 保留 `iface-id`，同名 veth 再现时会被 vswitchd 自动绑定（越权附着面）。

**P1-A1（= 全局 P1-1）失败节点补偿仅依赖 Agent 自身**
- 证据：`TeamLabShardDeploymentService.cs:282-297` 补偿只针对 `Success` 分片；标记在全部 apply 成功后一次性 `SaveChanges`（:81）；`TeamLabRuntimeCleanupService.cs:89-93` marker 缺失走旧路径 `CleanupShardAsync`，旧路径 VM 确定性名 `tl{runtimeId}-{key}`（:363-374）与 V2 domain 名不匹配。
- 触发：多节点部分成功 + 某节点 Agent 崩溃；主站 crash 于 agent-apply 成功与 DB 落盘之间；`ReadInventoryAsync` 抛异常后无补偿。

**P1-A2（= 全局 P1-2）主站 inventory 校验与 Agent V2 VM inventory 源不一致**
- 证据：`VerifyRuntimeInventoryAsync`（:506-523）VM 分支用 agent runtime inventory 的 `Vms.StableName`；而 `RuntimeController.cs:26-28` 的 `vms` 只来自 `KvmService`（:346-363,483-487，要求 `virsh desc` 含 `gzctf-generation=` 标记）；V2 domain XML 无 description 元素（`LibvirtTeamLabProvider.cs:230-247`）。
- 后果：V2 VM 资产在 inventory 校验必然报 missing → 在 apply 全部成功、标记已持久化后抛异常（:138）；重试返回 AlreadyApplied 仍校验失败 → runtime 永久卡 NetworkApplying，资源全部在跑但部署永不收敛（资源可被 destroy 的 V2 cleanup 清理，属收敛性缺陷非泄漏）。

**P1-A3（= 全局 P1-3）apply 后 inventory 读取异常不触发补偿**
- 证据：`ApplyCoreAsync` :127 `ReadInventoryAsync` 不在 try/catch 内；:199 cleanup 前快照同理。docker/libvirt 瞬时不可用时异常逃逸，跳过 :140-161 补偿块；主站记失败且不再补偿（P1-A1），已建资源残留至 runtime 销毁。

**P1-A4（= 全局 P1-4）OVSDB JSON-RPC 无内部超时**
- 证据：`ConnectAsync`（:111-142）无超时；`ReadLineAsync` 阻塞至 token 取消（:144-155）。OVN NB / OVS 本地端挂起时 apply/cleanup 只靠主站 60s HTTP 边界 + 补偿 2min 边界收敛；`TeamLabContainerNetworkFinalizeService`、`LinuxNetworkAttachmentService.RunAsync`（:88 `WaitForExitAsync` 无内部超时）同理；`ApplyDockerAsync` finally 用 `CancellationToken.None` 的 `ip link delete`（executor:380）理论可无限阻塞。

**P2-A1（= 全局 P2-1）`executionLocks` 无界增长**：按 (runtime,generation,shard) 建的 `SemaphoreSlim` 永不回收（executor:26,34-35）。
**P2-A2（= 全局 P2-2）journal 无界增长且重启即失**：条目仅在 cleanup/失败时删除（`TeamLabExecutionEventJournal.cs:8`）；被遗弃 runtime 条目常驻内存；Agent 重启后全部丢失（设计选择但未文档化，与 P1-A1 叠加成泄漏窗口）。
**P2-A3（= 全局 P2-3）死代码/冗余**：executor:126 `eventArray` 首次赋值被 :138 立即覆盖；`TeamLabOvnApplyResult.Digest`（provider:410-415）从未消费；`NormalizeDigest` 越界分支不可达（被 `IsDigest` 前置守卫）。
**P2-A4（= 全局 P2-4）V2 cleanup 重编译依赖 WorkerNode 行存在与 Fabric 租约未释放**：`TeamLabRouteApplicationService.cs:173-174`（`links.Count != shards.Length` 抛异常）、`:213-217`（节点必须存在且有 `TeamLabFabricIp`）。节点记录被删或 cleanup 在 finalize（租约已释放）后被重试 → V2 cleanup 编译恒失败 → runtime 永久 `CleanupPending`，不回落旧路径（`TeamLabRuntimeCleanupService.cs:75-87` 直接返回 Failed）。
**P2-A5（= 全局 P2-4）同代不同 digest 的 re-apply 会销毁现有容器**：executor:244-273 + `DockerService.cs:165-170`，`runtime_identity_conflict` → apply 失败 → 补偿按 AssetKey+generation 从 inventory 匹配并销毁该容器（generation 围栏内）。符合"同身份不同 digest 拒绝覆盖"的只是失败结果，资源被销毁后再重建，策略需确认。

## A.3 逐函数清单

| 函数 | 职责 | 边界缺陷 | 评分(1-5) |
|---|---|---|---|
| `TeamLabShardDeploymentService.DeployAsync` | V1/V2 编排与状态推进 | V2 校验失败后 runtime 卡 NetworkApplied（P1-A2）；标记落盘与 apply 非原子（P1-A1） | 3 |
| `TryApplyExecutionPlansAsync` | 能力门+并发 apply+补偿 | 失败节点不补偿，收敛依赖 Agent 进程内补偿（P1-A1） | 3 |
| `ApplyExecutionPlanAsync` | 单节点 apply 包装 | catch-all 吞异常为失败结果（可接受） | 4 |
| `CompensateExecutionPlansAsync` | 成功节点补偿 | 2min 有界、幂等，无误；但不含失败节点 | 4 |
| `CompileExecutionPlansAsync`(×2) | 确定性计划编译 | 依赖 fabric 租约/节点行存在（P2-A4） | 4 |
| `NormalizeImageDigest` | digest 归一化 | 无 | 5 |
| `VerifyRuntimeInventoryAsync` | 主站侧 inventory 校验 | 与 Agent V2 VM inventory 源不一致（P1-A2）；VM 分支必失败 | 2 |
| `BuildAssetRequest`/`PrepareImageAsync`/`ObserveImagePreparationAsync` | 请求构建/预热 | 预热失败被吞但保留主失败（合理） | 4 |
| `ExecuteNodeAsync`/`ApplyNodeSuccess`/DAG 循环 | V1 资产执行 | 失败后已建资产无 V1 补偿（遗留路径，由 runtime cleanup 兜底） | 3 |
| `TeamLabRuntimeCleanupService.CleanupAsync` | V2 标记识别+重编译+fallback | fallback 不能清 V2 VM/OVN/OVS（P1-A1）；编译失败即失败不回落（P2-A4） | 3 |
| `UsesExecutionPlanV2` | 标记精确识别 | 无 | 5 |
| `FinalizeGenerationAsync` | 全面销毁+租约释放 | 依赖租约存在；重试语义脆弱（P2-A4） | 4 |
| `BuildCleanupRequest`/`ContainerNamePrefix` | 旧路径精确匹配 | 与 V2 VM 命名脱节（P1-A1 一部分） | 3 |
| `TeamLabExecutionPlanExecutor.ApplyAsync` | journal+幂等重放 | journal 进程内易失（P2-A2） | 4 |
| `ApplyCoreAsync` | apply 主流程 | 后 apply inventory 读失败不补偿（P1-A3）；死语句 :126（P2-A3） | 3 |
| `CleanupAsync`/`CleanupCoreAsync` | 计划 cleanup | **OVS 名称不匹配导致端口泄漏（P0-A1）** | 2 |
| `CleanupAssetAsync` | 单资产销毁 | generation 围栏正确 | 4 |
| `ReadInventoryAsync` | Agent 侧 inventory | 与主站源不一致（P1-A2） | 3 |
| `StableHostInterface` | OVS 清理名计算 | 与 attach 名不一致（P0-A1 根因） | 1 |
| `ApplyDockerAsync` | docker 资产 apply | finally 用 `CancellationToken.None`（P1-A4 理论风险）；幂等良好 | 4 |
| `ApplyVmAsync`/`RunHealthChecksAsync` | VM apply/健康检查 | 健康检查 10s 有界，正确 | 4 |
| `TeamLabExecutionEventJournal` | 事件/响应缓存 | 无界增长、重启易失（P2-A2） | 3 |
| `TeamLabOvnNetworkProvider.ApplyAsync/RemoveAsync` | OVN 逻辑资源 | 命名含代、digest 冲突拒绝、事务原子，无缺陷 | 5 |
| `TeamLabOvsAttachmentProvider.AttachAsync/RemoveAsync` | OVS 端口管理 | identity 校验严格；泄漏由上层名字错配引入 | 4 |
| `OvsdbJsonRpcClient` | OVSDB JSON-RPC | 无内部超时（P1-A4） | 2 |
| `LinuxNetworkAttachmentService` | veth attach/remove | 命名与 executor 不一致（P0-A1 另一半）；`WaitForExitAsync` 无界 | 3 |

---

# 附录 B：审查面 B —— 网络实现

**审查范围**：`OvsdbJsonRpcClient.cs`、`TeamLabOvnNetworkProvider.cs`、`TeamLabOvsAttachmentProvider.cs`、`TeamLabOvnNaming.cs`、`LinuxNetworkAttachmentService.cs` + 调用方 executor、契约 IsValid。用 OVS 上游源码（`ovsdb/execution.c`、`lib/ovsdb-data.c`、`ovsdb/transaction.c`）核对 named-uuid 前向引用与事务语义。

## B.1 审查要点逐条结论

| 审查要点 | 结论 |
|---|---|
| 无字符串拼接 Shell 主路径 | 通过：全部 OVSDB JSON-RPC；`Process.ArgumentList` 无 shell；grep 0 命中（WireGuard 拼接属旧路径且经 `ValidateLinuxName`） |
| OVSDB 事务原子/超时/monitor/identity | 事务原子 ✓；超时 ✗（P1-B1）；monitor 未实现（无泄漏面）；响应 id 不校验（P2-B1） |
| 稳定命名含 runtime+generation，长度/字符集合规 | 通过：OVN 逻辑名嵌 32-hex runtime+generation；OVSDB 行名 128-bit SHA-256；veth 名 15 字节 IFNAMSIZ 合规。但 P0-1 创建/清理名字不一致绕过了稳定性保证 |
| 收敛证据来自 OVSDB/inventory 非等待 | 通过（附局限）：全为真实 select；局限是 OVN SB chassis binding 无确认，首轮健康探测可能与 vswitchd 绑定异步竞态 |
| Docker veth/VM TAP 只接获批端口 | 通过：veth 仅经 OVSDB 写入 br-int；`ExistingUuid` 强制 external_ids 三字段所有权围栏 |
| WireGuard 仍是共享入口 | 通过：executor 对 WG 零调用 |

## B.2 完整发现

**P0-B1（= 全局 P0-1）** 同 A 面 P0-A1（创建/清理命名不一致导致 OVS 残留），修复建议：收敛单一共享命名函数；`ovs.RemoveAsync` 对 Port/Interface delete 的 `count==0` 且事务前已确认本计划存在该资源时返回失败而非静默成功。

**P1-B1（= 全局 P1-5）OVSDB 客户端无超时 + 全 Agent 单一信号量串行化**
- 证据：`ReadLineAsync(token)` 只响应取消（:144-155）；`transact` 无 timeout 成员；`OvsdbJsonRpcClient` 为 Singleton（`Program.cs:51`），`transactionLock` 为实例级 `SemaphoreSlim(1,1)` —— 全部 runtime 的全部 OVN/OVS 请求串行。
- 触发：OVN NB 为 raft 集群，2/3 仲裁丢失时 `transact` 挂起等待法定人数（RFC 7047 无服务端默认超时）；持锁事务永远阻塞，所有 V2 网络操作排队挂死。
- 建议：`TransactAsync` 内用 linked CTS 加固定超时（如 30s），超时置 `SocketException` 释放锁；锁粒度至少按 endpoint 拆分。

**P1-B2（= 全局 P1-6）"already applied" 快路径只核对 switch+port**
- 证据：`:56` `portsPresent == plan.Networks.Sum(...)` 即判 AlreadyApplied；无任何 `Logical_Router`/`DHCP_Options`/`ACL`/`Logical_Router_Static_Route`/`Logical_Router_Policy` 的 select。部分检测（:60-61）也只查 switch。
- 触发：OVN 库被外部工具部分清理；计划模型未来允许多 shard 交叉引用。

**P1-B3（= 全局 P1-7）ForwardPolicies 只挂到 `Routers.Take(1)`**
- 证据：`:93-95` `foreach policy foreach router in control.Routers.Take(1)`；`RemoveAsync`（:126-127）对**所有** router 删除策略（名称含 router.Key），天然不对称。
- 建议：确认契约意图（若单边缘 router 是设计约束，应在 `IsValid` 显式拒绝多 router+ForwardPolicies；否则遍历全部 router）。

**P1-B4（= 全局 P1-8）ACL/静态路由无去重 → 同名 uuid-name 导致 apply 永久失败**
- 证据：`AclName` 键 `(source,dest,proto,port,allow)`、`StaticRouteName` 键 `(dest,nexthop)`；同键两条 → 同一事务内两个 insert 用同一 `uuid-name` → ovsdb-server 报 `duplicate uuid-name`（`ovsdb/execution.c` `ovsdb_execute_insert`）。`IsValid` 对 policies/routes 无去重。
- 建议：`IsValid` 增加 (network, ACL 键) 与 (router, network, dest, nexthop) 唯一性校验，或 apply 前按名去重。

**P2-B1（= 全局 P2-5）响应 id 不与请求 id 比对**：收到乱序/杂散消息会被误当响应（当前每次新连接、无 monitor，风险低）。
**P2-B2（= 全局 P2-6）每次请求重开 socket+握手，N+1 select**：apply 前每网络+每端口一次独立连接，50 资产×4 端口 ≈ 200 次串行往返。
**P2-B3（= 全局 P2-7）`WaitForExitAsync` 无命令级超时**：`nsenter`/`ip` 挂起无限阻塞该资产。
**P2-B4（= 全局 P2-8）Port/Interface delete 无 external_ids 所有权校验**：纯靠 64-bit 哈希名隔离（可接受，但建议行存在时校验后删）。
**P2-B5（= 全局 P2-9）bridge mutate delete 不验证端口确实挂在 br-int**：匹配 0 行静默通过。
**P2-B6（= 全局 P2-10）ACL/policy match 字符串直接插值 `policy.Protocol/Port/SourceCidr`**：`IsValid` 仅查非空未校验 CIDR/协议字符集，畸形值注入无效 match（非 shell，无代码执行风险）。
**P2-B7（= 全局 P2-11）`Networks.Count==0` 直接返回成功**：若 `NetworkControl` 声明了 router 则被静默跳过。
**P2-B8（= 全局 P2-12）lease_time 硬编码 3600**：无配置面。
**P2-B9（= 全局 P3-5）`StableName` 第三参 `network` 恒为 ""**：死参数（也是 P0-B1 根因之一）。

## B.3 逐函数清单

| 函数 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|
| `OvsdbJsonRpcClient.TransactAsync` | OVSDB 事务执行 | 无超时；全局锁串行；每次新连接；响应 id 不校验 | 3 |
| `OvsdbJsonRpcClient.SelectAsync` | 只读查询封装 | 全列返回、往返开销叠加 | 3 |
| `OvsdbJsonRpcClient.ConnectAsync` | unix/tcp 端点连接 | 无连接超时；IPv6 探测冗余但正确 | 3 |
| `OvsdbJsonRpcClient.ReadJsonAsync` | 读 JSON 行 | 无超时 | 3 |
| `TeamLabOvnNetworkProvider.ApplyAsync` | OVN 计划创建 | N+1 查询；快路径证据不全(P1-B2)；Take(1)(P1-B3)；空网络短路(P2-B7) | 3 |
| `TeamLabOvnNetworkProvider.RemoveAsync` | OVN 计划清理 | 名称幂等删除无所有权复核（可接受） | 4 |
| `MutateNetwork/MutatePort/MutateDhcpOptions/MutateRouter/MutateRouterPort/MutateRouterSwitchPort/MutateAcl/MutateStaticRoute/MutateRouterPolicy` | OVSDB 行构造 | 顺序与 named-uuid 正确；ACL/route 键无去重(P1-B4)；match 字符串无校验(P2-B6) | 4 |
| `RouterMac` | 确定性 MAC | — | 4 |
| `TeamLabOvnNaming.LogicalNetworkName/LogicalPortName/SafeKey` | 逻辑命名 | 长度 70+ 字符（OVN 允许，iface-id ≤255 合规） | 4 |
| `TeamLabOvsAttachmentProvider.AttachAsync` | veth→br-int 绑定 | 3 次预查询；桥存在性检查冗余 | 3 |
| `TeamLabOvsAttachmentProvider.RemoveAsync` | OVS 绑定解除 | 名字不一致导致静默空删（P0-B1 载体）；delete 无所有权校验(P2-B4)；mutate 无验证(P2-B5) | 3 |
| `TeamLabOvsAttachmentProvider.ExistingUuid` | 所有权围栏 | — | 4 |
| `LinuxNetworkAttachmentService.AttachContainerAsync` | veth 创建+netns 接入 | 幂等短路不验证归属；命名函数重复(P0-B1 根因) | 3 |
| `LinuxNetworkAttachmentService.RemoveContainerAttachmentAsync` | veth 删除 | — | 4 |
| `LinuxNetworkAttachmentService.RunAsync/SucceedsAsync` | 无 shell 执行 | 无命令超时(P2-B3)；与 SucceedsAsync 逻辑重复 | 3 |
| `TeamLabExecutionPlanExecutor.StableHostInterface` | OVS 清理名计算 | 与 attach 侧命名不一致，P0 泄漏根因 | 2 |

---

# 附录 C：审查面 C —— VM 与缓存

**审查范围**：`LibvirtNativeInterop.cs`、`LibvirtTeamLabProvider.cs`、`ImageDistributionService.cs`、`ImageController.cs`、`ImageDistributionReference.cs`、`ImageDistributionServiceTests.cs` + 执行链证据（Executor、Compiler、AgentClient、BackingChainInspector、DockerService、契约 IsValid、Journal、GenerationStore）。

## C.1 审查要点逐条结论

| 审查要点 | 结论 |
|---|---|
| P/Invoke 签名/内存/错误码 | 签名 ✓、内存 ✓（除 P0-C2 回调）、错误码映射 ✗（所有失败折叠为空泛消息） |
| VM destroy/undefine/overlay/NVRAM 同代围栏 | 通过（domain/overlay/UUID 均含 generation；无 UEFI 故无 NVRAM 文件） |
| 缓存删除后 inventory 确认 | 通过（Present=false 才删记录；残留保留失败语义） |
| 模板库 OCI 主制品不被删 | 通过（无 Registry 删除调用） |
| overlay 被引用时清理保持失败/等待 | **部分不成立**：主站 DB 闸门 ✓，但 Agent 物理 backing 检查盲区（P1-C3），替换路径无闸门（P0-C5） |

## C.2 完整发现

**P0-C1（= 全局 P0-2）libvirt 事件回调 UAF**（见 §3.1 P0-2 全文）。
**P0-C2（= 全局 P0-3）VM base 镜像文件命名三方不一致**（见 §3.1 P0-3 全文）。
**P0-C3（= 全局 P0-5）backing-chain 引用检查对 TeamLab overlay 目录不可见 → base 可被删/被换**
- 证据：`FindReferencesAsync` 只扫 `storagePath`（`/var/lib/gzctf/images`）顶层 `*.qcow2`（`VmImageBackingChainInspector.cs:36`）；TeamLab overlay 位于 `RuntimeStateRoot/{runtime}/{generation}/{assetKey}.qcow2`（`LibvirtTeamLabProvider.cs:202-204`），不在扫描范围。
- 后果：(a) 主站 DB 与物理状态不一致后缓存清理可删掉运行中 VM 的 base；(b) `DownloadVmImageCoreAsync`（:110-135）在 hash 变化时无条件 `File.Delete(destPath)` 并重下，**该路径没有任何主站 DB 闸门**——模板内容更新时，本节点仍在运行的 TeamLab VM 的 backing 被原地换掉，停着的 VM 永久不可启动、运行中 VM 磁盘读取到新镜像数据。

**P1-C1（= 全局 P1-3）最终 inventory 读取异常逃逸补偿**（同 A 面 P1-A3）。
**P1-C2（= 全局 P1-17）`qemu-img create` 无超时且持全局锁**
- 证据：`process.WaitForExit()` 无超时（:292-295）；`CreateOverlay` 在持有 `lifecycleLock`（:39 获取、:94 释放）期间调用；qemu-img 因存储卡死/NFS 挂起时该节点所有 VM 的 EnsureRunning/ChangeState/Destroy 无限阻塞。对照 `VmImageBackingChainInspector.cs:16,70-94` 有 20s 超时+kill。
- 建议：加 30s 超时 + `process.Kill(true)` + 异常转 `Failed("compute")`；或 CreateOverlay 移出全局锁按 runtime 粒度。

**P1-C3（= 全局 P1-14）backing-chain 盲区（同 P0-C3，属 P0/P1 双标）**：`FindReferencesAsync` 仅 TopDirectoryOnly 顶层扫描。
**P1-C4（= 全局 P1-18）跨网络 PortKey 重名 → MAC 取错/重复**
- 证据：`NetworkInterface` 按 `PortKey` 在**全部网络**的端口中 `FirstOrDefault`（:252-255）；`IsValid` 只校验 `(networkKey, portKey)` 二元组唯一（`TeamLabExecutionPlanV2.cs:41-44`），不校验全局 PortKey 唯一。兜底 `"52:54:00:00:00:01"`（:255）进一步放大碰撞面。多网络同接口键（如两个 switch 都是 `eth0`）是常态而非异常。
**P1-C5（= 全局 P1-15）24h 保留期删除仍活跃 release 的引用**
- 证据：`ReconcileReferencesAsync` 对 `CreatedAt < UtcNow-24h` 的 `TeamLabRelease` 引用一律判 invalid 并删除（:255,273-277），即使该 release 在 `activeReleaseIds` 中；`CreatedAt` 只在重新分发时刷新（`AddReferenceAsync` 的 `DO UPDATE SET CreatedAt=CURRENT_TIMESTAMP`，:1137-1146）。多日赛事/多日预热的 release 在 24h 后节点缓存引用全部被清（TeamLab VM 受 `HasActiveVmUsingTemplateAsync` 保护不删，Docker 与未运行 VM 缓存会删）。
**P1-C6（= 全局 P1-16）`EnsureCacheRemoved` 失败被标记为不可重试**
- 证据：inventory 仍 Present 时抛 `InvalidOperationException` → `IsDistributionFailure` true → `ImageFailure.Retryable`（`exception is HttpRequestException or IOException or TimeoutException`）= false。同一"缓存被占用"语义，VM 路径用 `Retryable=true`+5 分钟（:1003），Docker/VM 物理残留路径永久失败；收敛仅依赖周期 `CleanupUnreferencedAsync` 重新排队。测试 `ImageDistributionServiceTests.cs:366-367` 固化了该错误分类。
- 附加：`ImageController.cs:258-259` 把 `.part` 残留计入 Present → 永远无法 IsClean。

**P2-C1（= 全局 P2-24）libvirt 互操作层质量簇**：事件常量除 lifecycle 外全死代码且 `DomainEventResumed=7` 错误（真实 `VIR_DOMAIN_EVENT_RESUMED=4`，7 是 PMSUSPENDED）；`EventRegisterDefaultImpl()` 在 `ConnectOpen` 之后调用违反 libvirt 文档注册顺序；`connection is null` 无重试；`StopAsync` 空实现；`Dispose` 未 DeregisterAny；全部 DllImport 无 `virGetLastError`/`virGetLastErrorCode` 映射；`DomainDestroy` 返回被忽略（provider:132）。
**P2-C2（= 全局 P2-25）`GetConnection()` 非原子懒初始化**：`GetInventory`/`Destroy` 不持 `lifecycleLock`，冷启动并发可双开连接泄漏一个；Provider 无 IDisposable，连接进程期永不关闭。
**P2-C3（= 全局 P3-6）`ChangeStateAsync` 与 `DestroyAsync` 无任何调用者**：executor 用同步 `Destroy`，grep 证实 → 死代码（异步/同步双实现并存）。
**P2-C4（= 全局 P3-7）`CreateOverlay` 对已存在 overlay 直接复用，不校验可读性/backing 指向**：残留 0 字节或损坏 overlay 导致启动神秘失败。
**P2-C5**：全局 `lifecycleLock` 把所有 runtime 的 VM 生命周期串行化，20-50 资产节点成吞吐瓶颈（libvirt 句柄本身线程安全，锁只需保护懒连接与 qemu-img）。
**P2-C6**：已存在 shutoff domain 直接 `DomainCreate` 复用旧 XML——同代计划变更 memory/cpu 时重放忽略新配置。
**P2-C7（= 全局 P2-26）Docker 清理分支 `if (!string.IsNullOrWhiteSpace(image))`**：模板行被删后 `record.ImageTemplate` 为 null → 跳过 Agent 删除但仍删分发记录，违反"Present=false 才删"不变量（正常删除流程受 `CleanupTemplateForDeletionAsync` 保护，属残留风险）。
**P2-C8（= 全局 P2-27）`CleanupTemplateForDeletionAsync` claim 竞态窗口**：claim 在检查后被取走时清理中止、模板已删而拉取在途 → 孤立缓存。
**P2-C9**：`ReleaseReferenceAsync` 每次释放对候选记录逐条开事务 + advisory lock，大规模释放 N 次往返（性能）。
**P2-C10**：测试覆盖缺口：无"Agent inventory Present=false → 记录删除"快乐路径断言；无 `HasActiveVmUsingTemplateAsync` 阻塞/重试用例；无 Docker 清理用例（`RecordingAgentClient` 未覆写 `DeleteDockerImageWithInventoryAsync`）；无 24h release 截止用例；无"CleanupPending 期间引用回填 → 中止清理"用例。

## C.3 逐函数清单

| 函数 | 文件:行 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|---|
| `LibvirtConnection.RegisterLifecycleEvents` | LibvirtNativeInterop.cs:169-179 | 订阅 lifecycle 事件 | **P0-C1 UAF**；死代码回调；无配对 Deregister | 1 |
| `LibvirtConnection.Dispose` | LibvirtNativeInterop.cs:181-185 | 关连接 | 回调未注销→连接/线程泄漏；忽略 Close 返回值 | 2 |
| `LibvirtEventDispatcher.StartAsync` | LibvirtNativeInterop.cs:84-110 | 启动事件线程 | 注册顺序错误；无重试 | 2 |
| `LibvirtEventDispatcher.RunEventLoop/StopAsync` | LibvirtNativeInterop.cs:112-128 | 事件循环 | Stop 空实现；线程永不退出 | 2 |
| `LibvirtTeamLabProvider.EnsureRunningAsync` | :24-95 | 幂等 ensure+启动 | 全局锁粒度；CreateOverlay 无校验；复用旧 XML | 3 |
| `LibvirtTeamLabProvider.ChangeStateAsync/DestroyAsync` | :97-140 | 暂停/恢复/销毁 | **死代码**（无调用者） | 2 |
| `LibvirtTeamLabProvider.Destroy` | :166-191 | 按代销毁 domain+overlay | 无锁、忽略 Destroy 返回值；整体代围栏正确 | 3 |
| `LibvirtTeamLabProvider.ResolveBaseImage` | :193-198 | 定位 base 镜像 | **P0-C2 命名与落盘不一致，恒失败** | 1 |
| `LibvirtTeamLabProvider.CreateOverlay` | :200-208 | 创建/复用 overlay | 复用不校验；qemu-img 无超时（P1-C2） | 2 |
| `LibvirtTeamLabProvider.NetworkInterface` | :249-262 | 生成网卡 XML | **P1-C4 跨网络 PortKey 撞车**；兜底 MAC 固定 | 2 |
| `LibvirtTeamLabProvider.StableUuid/MatchesStableUuid` | :222-270 | 稳定 UUID 与校验 | 无缺陷，实现正确 | 5 |
| `ImageDistributionService.CleanupRecordAsync` | :962-1071 | 物理清理+inventory 确认 | P1-C6 重试分类错；Docker 空模板静默删记录（P2-C7） | 3 |
| `ImageDistributionService.ReconcileReferencesAsync` | :138-294 | 引用对账 | **P1-C5 24h 截止误伤活跃 release** | 3 |
| `ImageDistributionService.ReleaseReferenceAsync` | :884-960 | 释放引用+排清理 | 每记录开事务，批量释放 N 次往返 | 3 |
| `ImageDistributionService.CleanupTemplateForDeletionAsync` | :296-332 | 模板删除前置清理 | claim 竞态窗口（P2-C8） | 3 |
| `ImageDistributionService.QueueCleanup/EnsureCacheRemoved` | :858-882,1249-1255 | 排队与验收 | 验收异常被错误分类为不可重试 | 3 |
| `ImageController.DeleteVmImage` | :208-262 | 删 VM 缓存+inventory | backing 检查盲区（P1-C3）；.part 计入 Present | 3 |
| `ImageController.DownloadVmImageCoreAsync` | :102-159 | 下载/替换 VM 缓存 | **替换路径无引用闸门（P0-C3）**；文件命名分歧（P0-C2） | 2 |
| `ImageController.DeleteDockerImage` | :53-62 | 删 Docker 缓存+inventory | 无缺陷（in-use 由 Docker 409 自然拒绝） | 4 |
| `VmImageBackingChainInspector.FindReferencesAsync` | :23-54 | overlay 引用计数 | 仅扫 images 目录，**漏 TeamLab overlay**（P1-C3）；超时处理是亮点 | 3 |
| `ImageDistributionReference(.Key)` | :1-70 | 引用用途枚举 | 别名设计正确，无缺陷 | 5 |
| `ImageDistributionServiceTests` | — | 语义回归 | 覆盖 12 项但缺 P1-C5/P1-C6/快乐路径（P2-C10） | 3 |

---

# 附录 D：审查面 D —— 契约、并发与恢复

**审查范围**：`TeamLabExecutionPlanV2.cs`、`TeamLabExecutionEventV2.cs`、`TeamLabExecutionPlanCompiler.cs`、`NodeDispatchLimiter.cs`、`ITeamLabNodeExecutor.cs`/`AgentTeamLabNodeExecutor.cs`、`AgentClient.cs`、Agent 模型 6 文件、`Configs.cs` + 交叉验证 executor、OVN provider、DockerService、LibvirtProvider、Journal、ShardDeploymentService、RuntimeSignalService、FleetCapacityReservationService。

## D.1 七项审查要点逐条结论

| 要点 | 结论 |
|---|---|
| digest 确定性（相同输入同输出） | 通过：编译输入全排序、ShardKey 计入摘要、两端算法一致。**问题**：digest 覆盖执行端不消费字段（Fabric/Artifacts/租约绑定/Route.PortKey）→ digest 与执行效果漂移（P2-D2） |
| 同 digest 重复提交返回收敛 inventory | 通过：journal 命中后重读实时 inventory，全部 running 才返回 AlreadyApplied+收敛；不收敛则 Remove 后重放幂等收敛 |
| 同身份不同 digest 拒绝覆盖 | **未通过**：journal 的 digest 比对只在命中时生效；唯一闸门是 OVN 的 `gzctf-plan-digest` 冲突检查（仅带网络计划生效）；Docker/VM 层完全不校验（P1-D1） |
| 字段完整性（IsValid） | 关键闸具备（黑名单/引用完整性/键唯一/digest 自校验）；**缺口**：MAC/CIDR/lease/DNS/Route 格式不校验（P2-D1）；Port=0 使合法拓扑整体失效（P1-D4）；host 回退 `127.0.0.1` 打到 Agent 本机 |
| 节点分类限流与 DeploymentQueueTicket | 通过：单一队列、分类门、安全帽、manifest 缺失默认 1。**风险**：限流配置缓存永久化（P2-D3）、gate 容量固化（P2-D4）、apply/cleanup 共享门（P2-D5）、V2 资产并发对账本不可见（P2-D6） |
| Redis/进程通知只唤醒 | 通过：先查 PG 再 WaitAsync(1s)；Agent 信号 journal 落盘重放；V2 恢复以 DB+实态为事实源。**盲区**：失败分片补偿无兜底触发（P1-D2） |
| 事件契约不泄露原始细节 | 通过：只含逻辑键/CIDR/MAC/digest。**问题**：`Detail["message"]` 携带异常原文（P2-D7） |

## D.2 完整发现

**P1-D1（= 全局 P1-9）同身份不同 digest 不拒绝覆盖，静默重放混合旧资源**
- 证据：journal 键不含 digest（`TeamLabExecutionEventJournal.cs:28-38`）；不同 digest 静默落入 `ApplyCoreAsync`；OVN 层 `gzctf-plan-digest` 冲突拒绝（provider:42-43）但仅当计划有网络；Docker 复用只比对 image/generation/runtimeId（容器名指纹不含 digest/runtime，`DockerService.cs:1281-1291`）；VM 层只比对名字+稳定 UUID（`LibvirtTeamLabProvider.cs:50`），`CreateOverlay` 复用旧 backing 不校验基础镜像（:205-207）。
- 触发：模板 ImageHash 变化（重认证）后主站重试同 runtime+generation；零网络计划；digest 变化但镜像相同。
- 建议：executor `ApplyAsync` 开头对 (runtime,generation,shard) 增加持久化 digest 账本；账本存在且 digest 不同 → 返回 `errorCategory="cleanup"` 拒绝；VM/Docker 层增加 overlay/容器标签记录 plan digest 并在复用路径校验。

**P1-D2（= 全局 P1-11）失败分片无补偿兜底触发：残留与 OVN 部分状态死锁**
- 证据：`CompensateExecutionPlansAsync` 只补偿 `Success` 分片（`TeamLabShardDeploymentService.cs:284-297`）；失败分片依赖 executor 内部补偿（executor:142-159），若该补偿失败（节点离线/2min 超时）无任何后续触发：标记只在全部通过后写入（:316-349），legacy fallback 无 resource id 可清。OVN 部分状态（provider:58-61）使同计划重试永远失败，无自动修复/重补偿。
- 建议：主站对失败分片也发起一次 best-effort cleanup（结果只用于日志）；或 Agent 增加按 (runtime,generation) 的 reconciliation 后台任务。

**P1-D3（= 全局 P1-10）V2 Docker 资源规格与 FLAG/环境变量未生效**
- 证据：`ApplyDockerAsync`（executor:309-324）`CreateContainerRequest` 未设置 `MemoryLimit/CPUCount` → 容器以默认 64MB/1CPU 运行（`ContainerModels.cs:15-16`）；计划无 secrets/env/flag 字段 → V2 Docker 资产无 `GZCTF_FLAG`、无 sensor HMAC。VM 路径正常消费（`LibvirtTeamLabProvider.cs:236-238`）。
- 建议：契约增加受控 `Secrets` 字典（或声明"V2 不承载秘密"并在主站校验拒绝），executor 填入 `MemoryLimit/CPUCount`。

**P1-D4（= 全局 P1-12）健康检查意图缺端口 → 编译器产出 Port=0 → 整个计划 IsValid 拒绝**
- 证据：`health.Port ?? 0`（compiler:99）；`check.Port is < 1 or > 65535`（`TeamLabExecutionPlanV2.cs:58`）。拓扑允许 HealthCheckKind 而无 HealthCheckPort（legacy 可容忍，`TeamLabNodeProbeRequest.Port` 可空）。
- 建议：编译器在 Port null 时跳过该项（输出空列表）或给 TCP 默认端口；主站编译前拦截给拓扑校验错误。

**P1-D5（= 全局 P1-13）DHCP 静态绑定与 DNS 记录计入 digest 但执行端不落地**
- 证据：`MutateDhcpOptions`（provider:225-241）只写 server_id/router/lease_time，无 MAC→IP 静态映射；`DhcpLeases`/`DnsRecords`/`DhcpDnsServiceName` 无其他消费者（grep 证实）；端口 `IpAddress=null` 的资产从 OVN DHCP 拿到与计划租约无关的地址；租约计入 digest → 仅租约不同的计划 digest 不同且互判冲突，执行结果却完全相同。
- 建议：OVN 路径用 `Logical_Switch_Port.addresses` 固定 MAC+IP；`IpAddress=null` 端口在编译期解析租约给 IP（失败则编译报错）；DNS 记录若不在 V2 语义内应从契约剔除或落地为 OVN DNS。

**P2-D1（= 全局 P2-13）IsValid 格式校验缺口簇**（`TeamLabExecutionPlanV2.cs:41-105`）：未校验 MAC 格式（:83）、DHCP 租约 MAC/IP/hostname 内容（:47）、DNS 记录内容（:150-159）、`Route.DestinationCidr`/`NextHop`、Policy/ForwardPolicy 的 CIDR 格式（:100-101,193-198）、`Cidr` 合法性/网段重叠（:46）、`NetworkControl.RouterNamespace` 非空（:162）、`RouteVersion==0`（:90 仅拒负数）、`NetworkIntent.Kind` 值域（:143）。错误延迟到 OVN transact 报错（provider:105-110 归为 network 失败），或 ACL match 注入畸形表达式。
**P2-D2（= 全局 P2-18）死字段与不可达回退（digest 敏感性）**：`TeamLabArtifactReferenceV2`（compiler:105-108）执行端零消费；`NetworkControl.Fabric`/`RouteVersion`/`RouterNamespace`（compiler:117-133）V2 OVN 路径零消费但仍计入 digest；`TeamLabNetworkRouteV2.PortKey` 恒 ""（compiler:159）且 OVN 用 NextHop 判断；`NetworkIntent.Kind` 恒 "switch"；`PortKey`/`AssetKey` 的 `??` 回退（compiler:173-185）与 Where 谓词同源不可达；`LibvirtTeamLabProvider.cs:255` 回退 MAC `52:54:00:00:00:01` 是**危险死回退**（契约演进后会给多个 VM 同一 MAC）。
**P2-D3（= 全局 P2-14）主站限流配置缓存永久化**：`_dispatchLimits.GetOrAdd(workerNodeId, LoadDispatchLimitsAsync)` 永不失效（`AgentTeamLabNodeExecutor.cs:37,1137-1145,1156-1165`），`CancellationToken.None` 无 TTL；Agent 升级/改 manifest 后主站限流不生效直到重启。
**P2-D4（= 全局 P2-15）gate 容量在首次创建后固化**：`GetOrAdd` 已存在 gate 时忽略新的 `normalizedLimit`（`NodeDispatchLimiter.cs:62,78`）。
**P2-D5（= 全局 P2-16）apply 与 cleanup 共享 TeamLabExecution 门**：长 apply 阻塞同节点其他分片/其他 runtime 的 cleanup 最多一个 apply 时长。
**P2-D6（= 全局 P2-17）V2 资产并发对主站账本不可见**：`DeploymentQueueTicket`/`FleetCapacityReservations` 只看到 runtime 级 ticket；与 legacy 突发并发时按节点估算容量可能超卖（目前靠 Agent 默认串行兜底）。
**P2-D7（= 全局 P2-23）事件契约原文泄露与自由字符串**：`Detail["message"]` 携带异常原文（OVSDB socket 路径/JSON-RPC 错误、libvirt/qemu-img 输出、Docker 消息、镜像引用，executor:106）；`Stage/Outcome/ErrorCategory/ErrorCode` 自由字符串，拼写漂移风险。
**P2-D8（= 全局 P2-22）`NormalizeDigest` 无前缀 digest 会 Substring 越界**（`TeamLabExecutionPlanV2.cs:131-138`）：`marker<0` 且非 `sha256:` 前缀时越界抛 `ArgumentOutOfRangeException`。当前被 `IsDigest`（:119-129）先行拦截，静态公共方法脆弱。
**P2-D9（= 全局 P2-20）record IP 前缀剥离不一致**：DHCP/DNS 记录 IP 经 `AddressWithoutPrefix`（compiler:62,66），端口 IP（:43）用原始 `record.IpAddress`；含 `/24` 前缀时 OVN `addresses` 收到 `"mac ip/24"` → transact 失败。
**P2-D10（= 全局 P2-21）`Gateway()` 边界**（`AgentTeamLabNodeExecutor.cs:1169-1176`）：C# `uint` 移位计数按 5 位取模，`prefix=0` 时 `32-0=32` → 位移 0 → 掩码全 1 → gateway=net+1（可能等于已分配地址）；`prefix=32` 时 gateway=raw+1 溢出回绕到 0。建议用 `System.Net.IPNetwork` 并拒绝 /31、/32。
**P2-D11（= 全局 P2-22）`Math.Abs(int.MinValue)` 溢出**（executor:468-470）：哈希恰为 `int.MinValue` 时抛 `OverflowException`（概率 1/2³²，同一 assetKey 确定命中则永远失败）。同文件其它处用 `& int.MaxValue`（`AgentTeamLabNodeExecutor.cs:1168`）应统一。
**P2-D12（= 全局 P2-23）事件排序冗余**：`ConcurrentQueue` + 三次 `OrderBy(OccurredAt)`（executor:126,138,160,236），OccurredAt 同为 `UtcNow`，同刻度排序不确定。
**P2-D13（= 全局 P2-19）重复 DTO 约 20 组**：`AgentClient.cs:1946-2314` 与 Agent `TeamLabModels.cs` 整段重复（TeamLabStatusResponse/TeamLabDryRunResponse/TeamLabInfrastructureApplyRequest/TeamLabObservationRecord/TeamLabCaptureResponse 等）；`AgentClient.cs:1669-1830`（AgentCreateVmRequest/AgentVmInitConfig/AgentVmGuestControlConfig/AgentVmNetworkInterfaceRequest/AgentVmBootstrapApplyRequest 等）与 `VmModels.cs:17-97,225-251` 重复；`AgentCommandResult`（AgentClient:1831 vs `ContainerModels.cs:72`）；`AgentImageCacheCleanupResult`/`AgentVmImageDownloadResult` 与 `ImageCacheCleanupResponse`/`DownloadVmImageResponse` 重复。`AgentClient.cs` 单文件 2314 行。

## D.3 逐函数/逐字段清单

### `TeamLabExecutionPlanV2.cs`
| 函数/字段 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|
| `IsValid` | 契约完整性闸 | 格式校验缺口（MAC/CIDR/lease）；依赖 NormalizeDigest 无越界保护；O(路由×网络) 查询可接受 | 4 |
| `ComputeDigest`/digest 自校验 | 防篡改+确定性 | 覆盖执行端不消费字段 → digest 与执行效果漂移 | 3 |
| `IsDigest` | digest 形态校验 | 正确，含 @sha256:/sha256:/裸 hex 三分支 | 4 |
| `NormalizeDigest` | 归一化 | 无前缀输入 Substring 越界（调用流被前置拦截） | 2 |
| `TeamLabNetworkIntentV2` | 网络意图 | `Kind` 无校验且执行端不消费；`Cidr` 无格式/重叠校验 | 3 |
| `TeamLabDhcpLeaseV2` | DHCP 静态绑定 | 内容不校验；**执行端不落地**（仅计入 digest） | 2 |
| `TeamLabDnsRecordV2` | DNS 记录 | 同 lease：不校验、不落地 | 2 |
| `TeamLabNetworkControlIntentV2` | 路由控制意图 | `RouterNamespace`/`RouteVersion` 未消费却计入 digest | 3 |
| `TeamLabNetworkRouteV2.PortKey` | 路由端口 | 恒空串，执行端用 NextHop；死字段 | 2 |
| `TeamLabArtifactReferenceV2` | 制品引用 | 执行端零消费；死 DTO | 2 |
| `TeamLabHealthCheckV2` | 健康检查 | `Port=0` 导致整计划无效；host 回退 127.0.0.1 | 3 |

### `TeamLabExecutionEventV2.cs`
| 函数/字段 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|
| `Stage/Outcome/ErrorCategory` | 分类 | 自由字符串无值域约束 | 3 |
| `Detail` | 补充信息 | 携带异常原文（socket 路径/docker/libvirt 消息） | 3 |
| 请求/响应封装 | 传输 | ApplyResponse 无 digest 回执校验字段；结构正确 | 4 |

### `TeamLabExecutionPlanCompiler.cs`
| 函数/字段 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|
| `Compile` | 意图→计划 | `health.Port ?? 0`（P1-D4）；端口 IP 前缀剥离不一致（P2-D9）；Artifacts 无意义 | 3 |
| `Routes` | 织物路由展平 | 每个 switch 全量附 Fabric 路由；`PortKey=""` 死字段 | 3 |
| `Policies` | 转发策略过滤 | 正确（按 cidr 相等过滤） | 4 |
| `PortKey/AssetKey` | record→端口映射 | `??` 回退不可达（死代码） | 2 |
| `StableDomainName` | VM 稳定名 | 生成围栏正确；对 assetKey 截 48 合理 | 5 |

### `NodeDispatchLimiter.cs`
| 函数/字段 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|
| `Resolve` | 限流解析 | 语义正确、封顶安全；`GetValueOrDefault(1)` 对显式 0 收敛为 1 | 4 |
| `RunAsync` | 门控执行 | gate 容量固化（P2-D4）；`_gates` 不回收 | 3 |
| `WaitForIdleAsync` | 排空等待 | 正确；调用面窄（仅 ChallengeMutation） | 4 |

### `AgentTeamLabNodeExecutor.cs`（ITeamLabNodeExecutor 实现）
| 函数/字段 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|
| `ApplyExecutionPlanAsync/CleanupExecutionPlanAsync` | V2 入口 | 共享 TeamLabExecution 门（P2-D5） | 3 |
| `DispatchAsync` | 限流包装 | `_dispatchLimits` 永久缓存（P2-D3） | 3 |
| `LoadDispatchLimitsAsync` | 读取 manifest | `CancellationToken.None` 无失效 | 3 |
| `CreateContainerAsync`(legacy) | 容器创建 | legacy 保留路径；契约/执行面边界清晰 | 4 |
| `Gateway` | 网关计算 | /0、/32、/31 边界错误（P2-D10） | 2 |
| `StableId` | 稳定整数 | `& int.MaxValue` 安全（对照 P2-D11） | 4 |
| `ProbeAssetHealthAsync` | 健康探测 | 30/120 次 1s 轮询为固定等待（交接文档要求删除固定 sleep 类代码的残余） | 3 |
| 各 RequireMutation/ToCaptureResult | 响应规范化 | 正确、冗余分支少 | 4 |

### `AgentClient.cs`
| 函数/字段 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|
| `ApplyTeamLabExecutionPlanAsync/Cleanup...` | V2 传输 | 通过泛型 PostTeamLabAsync，60s 超时；响应为空静默返回 default（调用方降级为失败，可接受） | 4 |
| `BuildClient` | HTTP 构造 | 每次新建但由工厂管理；Timeout=Infinite + 调用点 deadline，模式一致 | 4 |
| `SendIdempotentAsync` | 单次重试 | 只重试一次、150ms，明确有界；好 | 5 |
| `ReadTeamLabResponseAsync` | 错误归一 | 非 2xx 吞掉并返回 default——掩盖真实错误，依赖调用方归类 | 3 |
| 1946-2314 行 legacy TeamLab DTO 块 | 传输契约 | 与 Agent TeamLabModels.cs 整段重复（P2-D13） | 2 |

### `TeamLabExecutionPlanExecutor.cs`（Agent，交叉验证）
| 函数/字段 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|
| `ApplyAsync` | 幂等入口 | 不同 digest 不拒绝（P1-D1）；journal+inventory 收敛逻辑正确 | 3 |
| `ApplyCoreAsync` | 计划执行 | 补偿独立 2min 超时正确；Docker 规格未生效（P1-D3）；事件三次重排序（P2-D12） | 3 |
| `CleanupCoreAsync` | 计划清理 | 按计划名围栏；`resource_remains` 事件逐条正确 | 4 |
| `ApplyDockerAsync` | Docker 资产 | 网络门-启动顺序正确；缺 Memory/CPU/Flag/env；finally 补偿正确 | 3 |
| `ApplyVmAsync` | VM 资产 | 委托 libvirt，正确 | 4 |
| `RunHealthChecksAsync` | 健康检查 | 正确、有 deadline | 5 |
| `StableChallengeId` | 稳定 ID | `Math.Abs(int.MinValue)` 溢出（P2-D11） | 2 |

### Agent 模型文件（TeamLabModels/AgentCapability/RuntimeInventory/Container/Vm/AgentConfig）
| 函数/字段 | 职责 | 缺陷 | 优雅度 |
|---|---|---|---|
| `AgentExecutionLimits` | 能力限流 | TeamLabExecution 默认 1，安全 | 4 |
| `RuntimeInventoryResource` | 库存事实 | 字段完整（含 DesiredStateDigest/AssetKey） | 5 |
| `CreateContainerRequest` | 容器创建 | `AssetKey` 已加入 label 但容器名不含 runtime/generation/digest（P1-D1 根因之一） | 3 |
| `CreateVmRequest` 系列 | VM 创建 | 与主站 `AgentCreateVmRequest` 重复 | 2 |
| `AgentConfig.ExecutionLimits` | Agent 侧限流 | 与主站 capability manifest 双源，需保持同步 | 3 |

---

# 附录 E：审查面 E —— 全量代码质量

**审查范围**：d09fa8d + 5550f3a 两提交全部涉及文件。

## E.1 完整发现

**P1-E1（= 全局 P0-4）多 router OVN 事务必然失败：ForwardPolicy 只写入 `Routers.Take(1)`**
- 证据：写入侧 :93-95 只对第一个 router 插入 `Logical_Router_Policy`；`MutateRouter`（:258-259）为**每个** router 生成 `policies = References(...RouterPolicyName(plan, router, policy))`（哈希含 `router.Key`，:386）。≥2 个 router 时第 2 个引用的 named-uuid 未定义 → OVSDB 事务报错 → 分片 apply 永远失败在 network 阶段。`RemoveAsync`（:124-127）却对称地为全部 router 删除策略。单一 router 时恰好掩盖。

**P1-E2（= 全局 P1-19）计划内资产串行执行 × 60s HTTP 截止 → M/L 规模必然超时**
- 证据：`MaxDegreeOfParallelism = agent.ExecutionLimits.TeamLabExecutionOperations ?? 1`（executor:81-85,203），Agent 与主站默认均为 1，主站安全帽也是 1；每个资产 = 创建 + 健康检查（每项上限 10s）。主站 `PostTeamLabAsync` 默认 `TeamLabRequestTimeout = 60s`（`AgentClient.cs:21,521`），apply/cleanup 未传自定义超时。20 资产即大概率触发 `TaskCanceledException` → 主站判失败并补偿，与 Agent 仍执行的 apply 抢锁竞态（补偿等锁最终收敛，但时间成本翻倍）。

**P1-E3（= 全局 P1-12）`health.Port ?? 0` → 健康检查端口缺失使整个计划验证失败，且 V2 失败不回落到 V1**
- 证据：compiler:96-100 → `Port=0` → `IsValid`（`TeamLabExecutionPlanV2.cs:58`）拒绝整个计划；主站 `TryApplyExecutionPlansAsync` 无 V1 fallback（V1 路径端口可为 null）。

**P1-E4（= 全局 P1-20）计划声明 DHCP 租约/DNS，但 OVN provider 从不物化**
- 证据：`MutateDhcpOptions`（:225-241）只写 server_id/router/lease_time；`DhcpDnsServiceName`/`DnsRecords` 在 provider 中零引用（grep 证实）。DHCP 模式 VM 将拿到 OVN 动态分配的地址而非计划租约。

**P2-E1（= 全局 P3-6）死代码：`LibvirtTeamLabProvider.ChangeStateAsync`/`DestroyAsync` 无调用者**：执行器使用同步 `libvirt.Destroy(plan, asset)`（executor:261）。pause/resume 意图在 V2 无入口。
**P2-E2（= 全局 P2-6）每次 OVSDB 操作新建 socket 连接 + 全局串行锁**：每次 `SelectAsync` 新建连接并握手（greeting + echo）；apply 中对每个 network/port 各做一次 select，50 资产约 100+ 次建连；`transactionLock` 使本地 OVS 与 OVN NB 全局互斥。
**P2-E3（= 全局 P2-1/P2-2）journal 与 executionLocks 无界增长**：每个成功计划在 `plans` 字典永久驻留（含全部 Events/Inventory）；`executionLocks` 每 identity 一个 SemaphoreSlim 永不回收。
**P2-E4（= 全局 P2-26）Docker 门依赖镜像内 `sh` 与 `/tmp`，且容器先启动后接网（busy-wait 门）**
- 证据：门实现为镜像入口 `while [ ! -f /tmp/.gzctf-teamlab-network-ready ]; do sleep 0.05; done`（`DockerService.cs:626-634`）；无 `sh` 的镜像必失败；释放文件失败则容器 20Hz 空转。`StartImmediately=true` 容器在 veth 接入前已启动（NetworkMode=None 无网，语义安全但占 CPU）。
- 建议：`docker create` 不启动 + exec 后 `start` 替代 busy-wait 门。
**P2-E5（= 全局 P1-10）V2 资产不含任何 secrets/flag/cloud-init，与 V1 语义不平移**：V1 注入 `FLAG`/`GZCTF_SENSOR_*` 与 guest control；V2 计划契约刻意不含 secrets（正确），但 Agent 直接不再注入 → 依赖环境变量的场景在 V2 下静默缺参。建议主站按 `Asset.Secrets` 非空显式拒绝 V2。
**P2-E6（= 全局 P2-23 另一面）同节点多分片时 inventory 串扰**：inventory 只按 RuntimeId+Generation 过滤（executor:288-293），不按 ShardKey；同一节点承载该 runtime 两个分片时，分片 A 的 apply/cleanup 会看到分片 B 的资产 → cleanup 因 `resource_remains` 误报失败。建议 docker label 增加 `GZCTF.ShardKey`。
**P2-E7（= 全局 P2-24 另一面）apply 用 tag 引用，计划承诺的不可变 digest 未校验**：`Image = asset.ImageReference ?? asset.ImageDigest`（executor:313）；`ImageDigest` 写入计划并参与 digest 校验，但实际创建走 tag；标签被移动时跑的不是计划承诺的内容。建议创建前按 `ImageDigest` 做 `InspectImageAsync` 比对。
**P2-E8（= 全局 P1-24）V2 分片在 `planCompilationError`/标记缺失时的静默旧路径回退**：标记缺失时回退旧路径——经 inventory（RuntimeId label）仍可收敛 V2 容器，但 OVS/OVN 资源不在 `BuildCleanupRequest` 资源名清单内 → V2 独有 OVSDB 资源残留。建议标记缺失时同样调用 `CleanupExecutionPlanAsync`（计划可从 release 重编译），仅编译失败才回退。

**P3-E1~E14**：见 §3.4 表（P3-1 死计算 / P3-2 死载荷 / P3-3 死限流分支 / P3-4 死返回值 / P3-5 死参数 / P3-6 句柄泄漏 / P3-7 懒初始化竞态 / P3-8 NRE / P3-9 缓存不刷新 / P3-10 缩进 / P3-11 重复实现 / P3-12 重复进程启动 / P3-13 测试脆弱 / P3-14 O(N²)）。

## E.2 已验证项（通过）

1. **Secret 泄漏（逐条审计全部 Log\* 与异常构造）**：范围内 30+ 处日志/异常仅含 runtime/generation/asset/node/template/image/hash/端口/MAC/IP；`OvsdbJsonRpcClient.cs:70` Debug 级打印 OVSDB error payload 含事务操作回显（含 MAC/IP）但无凭据；WireGuard 私钥只在请求体（`AgentTeamLabNodeExecutor.cs:392`）不外泄日志。**通过**。
2. **Generation 围栏**：容器（`GZCTF.Generation` label + `DestroyContainerAsync`/`StartContainerAsync` 校验）、VM（DomainName/overlay 路径均含 generation，`EnsureRunningAsync:34-37` 拒绝不匹配 ResourceId）、OVN 名（`TeamLabOvnNaming`）、OVS external_ids 校验（`ExistingUuid` 拒绝他 runtime）——**通过**。
3. **补偿不继承取消**：`CleanupCoreAsync` 用 2 分钟独立 CTS，docker finally 用 `CancellationToken.None`（executor:114,147,377）——**通过**。
4. **digest 确定性闭环**：compiler `ComputeDigest` 与 `IsValid` 重算逻辑一致；测试 `Compiler_ProducesStableDigestAndCompleteNetworkControl` 双编译相等；不同 digest 同 identity 在 OVN 层以 digest 冲突拒绝（provider:42-43）——**通过**。
5. **镜像缓存清理以 inventory 为准**：`EnsureCacheRemoved` 残留即抛错保留记录（`ImageDistributionService.cs:1249-1257`），Agent 404 → `Clean`；新测试 `ProcessClaimedAsync_CleanupKeepsRecordWhenAgentInventoryStillHasCache` 覆盖——**通过**。
6. **无固定 sleep/无界轮询**：Agent V2 新代码无 `Task.Delay` 重试循环；健康检查均带 10s 上限；唯一忙等是镜像内门（见 P2-E4）。
7. **职责边界**：Controller 仅做授权/路由/映射，不直接编排 Docker/OVN/libvirt（TeamLabController 委托 executor）——**通过**。
8. **旧路径隔离**：`EnableExecutionPlanV2=false` 时新端点 404、能力不含 V2 feature、主站走 V1——**通过**。

## E.3 逐函数质量清单

| 函数 | 文件 | 圈复杂度 | 低效/过度健壮问题 | 优雅度(1-5) |
|---|---|---|---|---|
| `IsValid` | TeamLabExecutionPlanV2.cs:19 | ~14 | 多遍枚举+3 处 ToHashSet，可读性差 | 2 |
| `ApplyAsync` | Executor:28 | 4 | journal 键不含 digest（靠 digest 相等性兜底） | 3 |
| `ApplyCoreAsync` | Executor:54 | 9 | 重复 OrderBy、事件/状态混合 | 3 |
| `CleanupCoreAsync` | Executor:194 | 7 | 资产×附件双层串行循环 | 3 |
| `ApplyDockerAsync` | Executor:306 | 8 | finally 清理可能抛新异常掩盖原异常 | 3 |
| `RunHealthChecksAsync` | Executor:402 | 6 | 每次 CreateClient 不释放 | 3 |
| `ApplyAsync`(OVN) | OvnProvider:19 | 8 | **Take(1) bug(P1-E1)**、每端口一次 select | 2 |
| `RemoveAsync`(OVN) | OvnProvider:114 | 5 | 与 apply 不对称 | 3 |
| `AttachAsync` | OvsProvider:15 | 6 | — | 4 |
| `TransactAsync` | OvsdbJsonRpcClient:14 | 5 | 每操作新建连接(P2-E2) | 3 |
| `AttachContainerAsync` | LinuxNet:10 | 4 | RunAsync/SucceedsAsync 重复 | 3 |
| `EnsureRunningAsync` | LibvirtProvider:24 | 8 | 无锁懒连接(P3-7) | 3 |
| `Destroy` | LibvirtProvider:166 | 5 | 与 DestroyAsync 重复 | 3 |
| `DeployAsync` | ShardDeployment:33 | 10 | 双路径 V1/V2 混排，函数过长(200+行) | 2 |
| `TryApplyExecutionPlansAsync` | ShardDeployment:243 | 8 | — | 3 |
| `Compile` | PlanCompiler:17 | 8 | PortKey/AssetKey 每记录 O(N²) | 3 |
| `CleanupAsync` | RuntimeCleanup:31 | 8 | 标记缺失静默回退(P2-E8) | 3 |
| `DispatchAsync` | AgentNodeExecutor:1131 | 2 | 限流缓存永不过期(P3-9) | 4 |
| `Resolve` | NodeDispatchLimitPolicy:22 | 8 | ArtifactCleanup 死分支(P3-3) | 3 |
| `EnsureCacheRemoved` | ImageDistribution:1249 | 1 | — | 5 |

---

# 附录 F：实机测试原始数据

## F.1 压测 1（100 并发 × 5/worker × 6 端点，cookie 复制方案有缺陷，部分 401）

- 总请求 3000，墙钟 112.8s，吞吐 27 req/s。
- nodes: err=304/500, p50=221ms, p95=30,210ms, max=41,002ms
- runtimes: err=500/500, p50=99ms, p95=531ms, max=4,862ms
- queue: err=500/500, p50=99ms, p95=693ms, max=3,487ms
- flows: err=494/500, p50=99ms, p95=699ms, max=43,925ms
- topologies: err=353/500, p50=148ms, p95=30,774ms, max=42,185ms
- scopes: err=500/500, p50=99ms, p95=532ms, max=2,609ms
- 注：runtimes/queue/scopes 的 401 快速返回（99ms）与 nodes/topologies 的 30-40s 延迟并存，说明服务端在并发下出现明显排队；该轮受脚本 cookie 分隔符缺陷影响，仅作为趋势参考，以 F.3 为准。

## F.2 压测 2（60 并发，登录顺序错误，请求全部未认证 401）

- nodes: 401×60, p50=209ms
- runtimes: 401×60, p50=107ms, p95=29,569ms（401 也要 29 秒）
- queue: 401×59 + ReadTimeout×1, p50=29,519ms, max=30,341ms
- flows: 401×60, p50=30,202ms, max=30,322ms
- topologies: 401×60, p50=30,018ms
- scopes: 401×59 + ReadTimeout×1, p50=30,011ms
- login: 200×60, p50=30,955ms, p95=33,108ms, max=33,215ms
- **结论**：60 并发下即使是 401 也需 30 秒返回；最终登录（200）同样 31 秒——瓶颈在服务端，与认证逻辑无关。

## F.3 压测 3（40 并发，真实登录 + 4 个认证读取，最终有效数据）

| 接口 | n | err | p50 | p95 | max |
|---|---|---|---|---|---|
| login | 40 | 0 | 3,822ms | 3,856ms | 4,029ms |
| nodes | 40 | 0 | 465ms | 2,455ms | 2,924ms |
| runtimes | 40 | 0 | 371ms | 13,599ms | 53,609ms |
| queue | 40 | 0 | 13,602ms | 53,969ms | 54,153ms |
| flows | 40 | 0 | 41,400ms | 41,981ms | 54,988ms |

基线（串行）：login 415ms、nodes 147ms、runtimes 135ms、queue 146ms、flows 229ms。

## F.4 边界测试明细

- 401（无 cookie/垃圾 cookie）：`/api/v1/nodes`、`/api/admin/teamlab/runtimes` ✓
- 200+HTML（畸形 GUID 落入 SPA 兜底）：`/api/v1/nodes/not-a-guid`、`/api/v1/nodes/not-a-guid/resources`、`/api/admin/teamlab/runtimes/not-a-guid/events`、`/api/v1/deployment-queue/not-a-guid`
- 404 JSON（合法但不存在 GUID）：`/api/admin/teamlab/runtimes/00000000-0000-0000-0000-000000000000/events`
- 200（参数未校验）：`count=99999`、`count=-1`、`count=0`、`count=abc`、`skip=-5`、`count=999999999999999999999999999999`
- 400（畸形登录体）：`{}`、`{"userName":}`、`{"userName":"x"}`、`{"password":"y"}`、`null`、`"str"`
- 速率限制：60 连发 `/api/info` 全 200，无 429

## F.5 服务器侧证据（压测期间/后）

- PostgreSQL：2 active / 37 idle 连接；唯一活跃查询为 runtime inventory 容量扫描（`LastScannedAt` DISTINCT 查询）；`pg_stat_statements` 未启用。
- Redis：24.29MB，3 clients，无 maxmemory。
- 主站日志：压测期间无 500/异常/线程池饥饿日志；持续出现 `EfOperationalEventWriter: Traffic observation dropped records because of backpressure or local capture loss`（约每 5 秒一次）与 `PostgresTeamLabTrafficBatchWriter: received=6~18, inserted=6~18`。
- 主站运行态：`load 4.35`（4 核）、3 个 dotnet-dump 进程各 ~96% CPU（已清理）、postgres 多个 ~14-16% CPU、GZCTF 7.1% CPU。

## F.6 观测管线

- `traffic/paths`：`completeness.complete=false, droppedRecords=2,125,088`（10 分钟前）
- 复测：`droppedRecords=2,157,883`（+32,795，~55 条/秒丢弃）
- 3 轮 × 5s 分页拉取 300 条 flow 记录成功（分页游标工作正常）
- 主站与 Agent 之间观测数据以 1 秒间隔 POST `/api/teamlab/observations/read` 传输（~6ms RTT 正常）

## F.7 代码级验证

- 定向测试（`TeamLabExecutionPlanV2Tests` + `TeamLabCleanupOwnershipTests` + `TeamLabInterfaceNamingTests`）：**20/20 通过**，退出码 0。
- OVS 命名不一致哈希实算：identity `12345678-1234-1234-1234-123456789012:5:web:net0` → attach `tlh116dcbd78c1f` vs cleanup `tlh2e6563155043`。

---

（报告完）
