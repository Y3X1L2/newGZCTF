# TeamLab 修复候选二次审查与实机验收报告

- **日期**：2026-08-12
- **审查对象**：修复提交 `823ef61 → b87c747`（+14,620 行 / 71 文件）、部署后提交 `1265978 / 6ad8c57 / b90005d`（未部署）、线上部署 `teamlab-hpa-v2-enabled-20260812`
- **方法**：5 路并行静态审查（逐函数逐字段，同 08-11 标准）+ 双节点实机全链路复测（同标准：边界/性能/观测）
- **结论**：上轮 5 项 P0 全部真修复、24 项 P1 中 21 项真修复；但修复候选**新增 2 项潜伏 P0**（快照列宽、Redis 流静默裁剪），且**线上部署存在 2 项实机 P0**（新建运行时 500、快照列宽蓄势待发），**当前任何新建 TeamLab 运行时均失败，V2 不可启用、不可交付**。全程未修改任何代码。

---

## 1. 审查基线与环境实况

### 1.1 代码基线

| 项 | 事实 |
|---|---|
| 修复范围 | `823ef61..b87c747`（71 文件 +14,620/-1,010 行）+ 部署后 `b87c747..HEAD`（Agent 同步二阶段等 6 文件） |
| 本地 HEAD | `b90005d`；工作树另有 12 个未提交文件（data-plane 闸门修复、OvsdbJsonRpcClient 重写），与已部署产物不一致 |
| 部署记录 | 交接文档记录部署 `b87c747`；**线上实况已切换至 `teamlab-hpa-v2-enabled-20260812`**（18:11 激活），主站/Agent 二进制 SHA 一致（agent=`02d82dc3...`） |

### 1.2 实机环境（与交接文档的差异，以线上实测为准）

| 交接文档陈述 | 线上实况（08-12 19:xx 实测） |
|---|---|
| OVS/OVN 未安装 | **两节点均已安装**：`ovs-vsctl/ovn-nbctl/ovn-sbctl/ovn-controller` 3.7.1/26.03.0，`ovn-central/ovn-controller/openvswitch-switch` 均 active |
| `EnableExecutionPlanV2=false` | **Agent 配置两节点均为 `true`**；主站 release 名为 `v2-enabled` |
| 125 未完成同步 | **已同步**：125 agent SHA=`02d82dc3...` 与 118 一致，节点 `IsSchedulable=true` |
| 能力清单 | **两节点均完整上报** `teamlab.execution-plan.v2 + teamlab.ovs-ovn.v1 + teamlab.libvirt.native.v1`（08-12 19:0x 期间能力 hash 多次变更后收敛；审查期间读到 125 瞬时缺能力为清单更新中的中间态） |
| V2 执行接口 | **两节点 `execution-plan/apply|cleanup` 空体请求均返回 `400 request.invalid`**（非关闭时的 404），证明 Provider 已启用 |
| OVN 数据面 | SB 注册 2 chassis（10.250.0.1/.118、10.250.0.2/.125，geneve encap）；`br-int` + `ovn-1a3f88-0` geneve 隧道就绪；逻辑资源 0（现网 runtime 119 为旧路径资源，无 OVN 残留） |
| 观测管线 | 近 30 分钟主站/Agent 日志 **0 条 dropped/backpressure**；60 秒采样 `droppedRecords` 增量 **0/s**；PG 已持久化流量 440,899 条 |
| 快照表 | `"TeamLabExecutionPlanSnapshots"` 已建（0 行），**`PlanDigest` 列 = `character varying(64)`**（见 P0-6 实锤） |

---

## 2. 静态审查结论（5 个审查面合并）

### 2.1 上轮 P0 修复判定（全部经代码复核）

| 原编号 | 声称修复 | 判定 | 证据 |
|---|---|---|---|
| P0-1 OVS 清理命名错配 | 单一共享命名函数 | **真修复** | `LinuxNetworkAttachmentService.HostInterfaceName/PeerInterfaceName`（:62-70），executor 清理/补偿均调用（executor:226,403），旧 `StableHostInterface` 删除；哈希输入收敛 `{runtime}:{generation}:{assetKey}:{networkKey}` 无尾冒号 |
| P0-2 libvirt 事件回调 UAF | 无消费者事件循环整体删除 | **真修复** | `DomainLifecycleCallback/RegisterDomainEvent/EventDispatcher` 全删（`LibvirtNativeInterop.cs:1-53`）；`DomainFree` 仅作用于自有句柄（provider:68,91,124,158）；全仓 grep 0 残留 |
| P0-3 VM base 镜像三方命名 | 统一为 TemplateId | **真修复**（配置耦合残留） | 服务端 `AgentClient.cs:1278-1288`→下载 `ImageController.cs:110-111`→Provider `LibvirtTeamLabProvider.cs:162-167` 三方键一致；残留：`kvm.ImageStoragePath` 改配后下载与消费路径分裂；`{hash}.qcow2` 第三形态仅删缓存用 |
| P0-4 多 router ForwardPolicies | 遍历全部 router | **真修复** | `TeamLabOvnNetworkProvider.cs:89-92` 全 router；`AllResourcesPresentAsync`（:170-207）对全部期望行（含 policy/static_route）做存在+digest 校验 |
| P0-5 base 替换无 backing 闸门 | 双根递归扫描+替换前检查 | **真修复**（TOCTOU 残留） | `ImageController.cs:122-139,233-255` + `VmImageBackingChainInspector.cs:36-37` 纳入 RuntimeStateRoot；残留：扫描与 `File.Move` 无锁窗口；校验只比路径不比内容 digest（见 P1 新发现） |

### 2.2 上轮 P1/P2 修复判定（对照表节选）

- **P1 真修复（21 项）**：失败分片全量补偿（含失败分片幂等 cleanup）、计划快照 apply 前持久化+清理用快照（不再依赖节点行/租约）、VM 稳定 UUID+计划摘要、overlay backing 校验、qemu-img 2 分钟有界+kill、执行锁/事件 journal 有界回收（`KeyedSemaphoreRegistry` 引用计数）、健康检查端口编译期三层拒绝、digest 四层围栏（journal/Docker 标签+镜像串/VM XML digest/服务端快照冲突）、Docker 资源规格生效、不同 digest 不再误杀旧容器（digest 标签围栏）、OVSDB 连接复用+15s 上限+greeting+响应 id 校验、OVS 删除所有权校验（`OwnedWhere`+`RequireDeleteCount`+`BridgeContainsPort`）、OVN 快路径全资源校验、ACL/route 去重、DHCP/DNS 物化（`MutateDns`+server_mac+`AllResourcesPresentAsync` 计入）、backing 扫描纳入 overlay、24h 误删修复、EnsureCacheRemoved 可重试、限流缓存 1 分钟 TTL、gate 动态容量、cleanup 走 Control 门、跨网络 PortKey 组内限定、`Math.Abs`/`Gateway`/`NormalizeDigest` 边界、事件异常脱敏（`summary`+`FailureMessage`）、空请求体守卫、`52:54:00:00:00:01` 死回退删除、重复 DTO 部分收敛、迁移原子 claim、规模超时（`ExecutionPlanDeadline` 按资产推导）。
- **部分修复（2 项）**：P1-10 FLAG/env —— V2 契约无 secrets 字段，含 secrets 的 overlay **整体回落 V1**（`TeamLabShardDeploymentService.cs:282-286`，文档化功能边界）；P1-14 backing —— 路径层真修复，**内容 digest 未与计划绑定**（`HasExpectedBackingAsync` 只比 backing-filename 路径，`ResolveBaseImage` 只 `File.Exists`，从不校验 `asset.ImageDigest` 与实际文件 SHA-256）。
- **未修复（1 项）**：P2-12 lease_time 硬编码 3600（仅提为命名常量，无配置面）。

### 2.3 新增潜伏 P0（代码级，合入前必须修复）

**P0-6（快照）`PlanDigest` 列宽 64 vs 计划摘要 71 字符 → PostgreSQL 上首次 V2 部署必失败**
- `TeamLabExecutionPlanCompiler.cs:148` 产出 `"sha256:"+64hex` = 71 字符；实体 `[MaxLength(64)]`（`TeamLabRuntimeAggregate.cs:57`）；迁移 `character varying(64)`；`TeamLabShardDeploymentService.cs:324` 原样写入。
- **线上实锤**：`information_schema.columns` 显示生产库该列即 `character varying(64)`。V2 apply 时 `SaveChangesAsync` 抛 `22001 value_too_long`，runtime 卡 Deploying，且异常不在 orchestrator 捕获清单内 → 泛型 500。单元测试跑 SQLite 无法暴露。
- 修复：列扩 `varchar(96)` 或落盘前 `NormalizeDigest` 为裸 64hex；补 PostgreSQL 真库迁移验证。

**P0-7（观测）Redis 流在持久化 worker 停摆时静默裁剪未读条目**
- `RedisTeamLabTrafficIngestor.cs:21-39`：Lua 在 `XPENDING` 为空时无条件 `XTRIM MAXLEN (250000-count)`。worker 停摆 >~76 分钟（@55 条/s）后，每次 append 裁剪**最旧未消费条目**，但返回 count → 游标推进 → Agent 收到 ackThrough 删除 spool 记录 → **数据永久丢失且 DroppedCount/completeness 不反映**（静默）。
- 修复：XTRIM 只裁剪已 ACK 条目，或流满时返回 -1 拒绝 append（Agent spool 兜底并显式计数）+ 监控告警。

### 2.4 新增 P1/P2（修复引入或遗留，节选关键项）

| 级别 | 发现 | 位置 |
|---|---|---|
| P1 | OVSDB 15s 超时路径**不重置会话**：deadline OCE 不在 catch 过滤集 → 下次事务被迟到响应毒化一轮；Reset 与下一事务存在竞态（P1-3） | `OvsdbJsonRpcClient.cs:42-47,110-149` |
| P1 | DNS 物化 `ToDictionary` 对同 hostname 多记录抛 ArgumentException → 逃出 provider/executor → **HTTP 500 无分类失败** | `TeamLabOvnNetworkProvider.cs:287-290`；`TeamLabExecutionPlanV2.cs:82-86` |
| P1 | 迁移未回填存量 V2 runtime：升级前带 `execution-plan-v2/{shardId}` 标记的运行时无快照 → 清理永久 Failed 卡 CleanupPending | `TeamLabRuntimeCleanupService.cs:74-76` |
| P1 | 二阶段同步对**全新 TeamLab 节点构成引导死锁**：数据面收敛依赖隧道 IP，隧道依赖部署，部署依赖可调度 | `AgentFleetUpdateCoordinator.cs:296-303` |
| P1 | `WaitForIdleAsync` 与 DispatchGate 容量 resize 竞态 → SemaphoreFullException/ObjectDisposedException/容量虚增 | `NodeDispatchLimiter.cs:88-103,124-168` |
| P1 | VM base 内容 digest 未与计划绑定（路径层已修，digest 层假修复） | `LibvirtTeamLabProvider.cs:185-222` |
| P1 | 不同 digest 拒绝后无收敛路径：快照冲突永久阻断部署、无操作员恢复手段；Agent 重启后错误分类为"清理失败"而非"身份冲突" | `TeamLabShardDeploymentService.cs:303-330`；`TeamLabExecutionPlanExecutor.cs:268` |
| P2 | 快照表无回收（每个 runtime/generation/shard 永久一行含 PlanJson）；journal 4096 上限可能逐出活跃计划；`PlanDigest["sha256:".Length..]` 魔法切片；VM 域标识不含 ShardKey；Docker 冲突检查 `&&/||` 短路依赖；截断 overlay 复用不校验 format/virtual-size；快照 WorkerNodeId 陈旧（同代换节点清理打错节点）；模板删除 claim `DateTimeOffset.MaxValue` 崩溃后永久卡死；spool 每次 ack 整文件重写（写放大 64MB/s/节点）+ 无界 channel + 恢复重复投递窗口；`RecoverPendingAsync` 用当前能力集自我批准；能力探测 catch 不含 JsonException 可 500；每次同步无条件 `systemctl restart ovn-controller` 数据面抖动；`IsPrimary/CapturePackets` 仍死字段计入 digest | 详见各文件 |
| P3 | `ContainsIdentity` 新死代码；重复 DTO 新增（TeamLabDataPlaneSyncConfig/AgentSyncRequest 等）；apply 响应 Message 仍携带原始 OVSDB error JSON（无凭据，脱敏未完全关闭）；data-plane 命令失败消息仅退出码无 stderr | 详见各文件 |

### 2.5 日志/事件泄漏复查（通过）

新增 `Log*` 与异常构造逐条复核：无 token/密码/私钥/flag/user-data；OVSDB error payload Debug 日志已删除；事件 `Detail` 收敛为 `summary`（失败走 `FailureMessage`）；`TeamLabCommandRunner` 只处理命令文本。**通过**。

### 2.6 新增测试质量

| 测试 | 强度 | 缺口 |
|---|---|---|
| `OvsdbJsonRpcClientTests`（greeting+id 校验、断线重连） | 强 | 不覆盖超时后会话毒化、半条响应 |
| `TeamLabLifecycleTests`（事件循环不存在、锁回收、journal 容量） | 中 | 表面型断言居多；不测 WaitAsync 取消路径 |
| `TeamLabInterfaceNamingTests`（attach=cleanup 命名） | **弱（假阳性候选）** | 只测单函数确定性 `f(x)==f(x)`，无法发现两函数回归（真实保障是删除旧函数） |
| `TeamLabObservationTests`（spool ack、33k 不丢） | 强 | 不覆盖 spool 重写竞态/重启去重 |
| `ImageDistributionServiceTests` 新增 | 强 | 无 24h 回归锁、无 MaxValue claim 崩溃恢复用例 |
| `NodesControllerTests`（双阶段同步） | 中 | 未断言 data-plane 失败→cordon、无 V2 闸门场景 |

---

## 3. 实机测试结果（同标准复测，08-12 19:1x-19:4x）

### 3.1 交接信息复核（用户交接项逐条验证）

| 交接陈述 | 实测 | 判定 |
|---|---|---|
| 118 已切换 `teamlab-hpa-v2-enabled-20260812` | 激活 release = `teamlab-hpa-v2-enabled-20260812/publish` | ✓ |
| 主站 EnableExecutionPlanV2=True | Agent 配置两节点 `true`；主站侧以 release 承载 | ✓ |
| 118/125 均通过平台同步接收该开关 | 两节点 `AgentUpdateState=Stable`、SHA 一致、可调度 | ✓ |
| 两节点具备 V2/OVS-OVN 能力 | 两节点完整上报 `execution-plan.v2/ovs-ovn.v1/libvirt.native.v1` | ✓（能力清单更新中的中间态曾被误读，最终收敛一致） |
| 两端 V2 接口 400 request.invalid 而非 404 | 两节点 × apply/cleanup 空体 → `400 {"code":"request.invalid"}` | ✓ |
| 后续新建运行时会走 V2 路径 | **被阻断**：试运行创建 → `500`（见 3.2） | ✗ |

### 3.2 线上 P0（实机阻断，新增）

**P0-A 新建 TeamLab 运行时 100% 500（V1/V2 全路径不可用）**
- 实测：`POST /api/admin/teamlab/runtimes/trials`（Idempotency-Key + 既有 release `019fe773...`）→ **500** `internal_error`。
- 根因（日志实锤）：`23502: null value in column "IsScenarioBuild" of relation "TeamLabRuntimes" violates not-null constraint`。
- 归因：线上库应用了**另一分支**的迁移 `20260811200000_RestoreTeamLabRuntimeScenarioBuild`（恢复 IsScenarioBuild NOT NULL 列）与 `20260812003000_RestoreTeamLabRuntimeAssetExecutionColumns`，但部署的主站二进制（本分支 `codex/teamlab-high-performance-a`）的 EF 模型**不含该属性**（本地分支仅含 `20260811150352_PersistTeamLabExecutionPlanSnapshots`，`IsScenarioBuild` 于 08-10 迁移中被删除且本分支未恢复）→ EF INSERT 缺列 → NOT NULL 违约。**跨分支迁移集合不一致 + 部署时未做 schema-代码对齐校验**。
- 影响：**当前线上无法创建任何 TeamLab 运行时**（试运行、比赛运行时、外部创建全部 500）。本次测试创建失败未留脏数据（1 小时窗口内 0 新建行，队列无新票据）。
- 修复：以"部署目标分支的迁移集合"为准对齐生产库（应用/回滚恢复迁移到一致状态），并在发布门禁中加入"部署前 schema diff 校验"。

**P0-B 快照 `PlanDigest` varchar(64) 线上蓄势待发**
- 线上 `"TeamLabExecutionPlanSnapshots"."PlanDigest"` 列即 `character varying(64)`。P0-A 修复后，V2 首次 apply 将命中 P0-6（71 字符写入 64 列 → 22001）。两者叠加使 V2 无法启用。

### 3.3 边界测试（复测）

| 用例 | 结果 | 对比首轮 |
|---|---|---|
| 畸形 GUID 路由（3 例） | **200 + SPA HTML** | 未修复 |
| 分页参数 `count=-1/abc/99999` | **200 无校验** | 未修复 |
| 速率限制 60 连发 | 全 200 无 429 | 未修复 |
| `logs?count=5000` | 400 校验 | 正常 |

### 3.4 性能测试（复测）

**基线（串行）**：nodes 129ms、runtimes 257ms、queue 659ms、flows 591ms、topologies 369ms。

**40 并发（login + 4 读端点，组合负载）**：

| 端点 | 首轮 40 并发 | 次轮 40 并发 |
|---|---|---|
| login | p50 3,822ms / max 4,029ms | p50 **2,245ms** / max 2,651ms |
| nodes | p50 465ms | p50 **418ms** |
| runtimes | p50 371ms / p95 13.6s | **37/40 ReadTimeout(45s)**（组合负载下平台级崩溃） |
| queue | p50 13,602ms | p50 **3,178ms** / max 3,575ms |
| flows | p50 41,400ms | p50 **1,195ms** / p95 9,615ms |

**端点隔离测试**：runtimes 单独 @5/10/20 并发 → 0 错误、p50 245-271ms。40 并发组合下的超时属**全平台瞬时线程池/DB 竞争崩溃**（登录风暴 40× 写放大 + 读并发叠加），非单接口缺陷。

**结论**：僵尸进程清理 + 观测修复带来显著改善（queue/flows/login 提升 3-35 倍），但 **40 并发组合负载下平台仍会崩溃到 45s+ 超时**（首轮 40 并发即 13-54s 的问题方向未根除，仅缓解）；并发容量约 ≤20 会话健康。

### 3.5 观测管线（复测）

- 60 秒采样：`droppedRecords` 2,371,187 → 2,371,187（**增量 0/s**）；主站/Agent 日志 30 分钟 0 dropped/backpressure。
- **spool 修复线上生效**：当前无新丢弃；PG 已持久化流量 440,899 条；flows 分页 30 秒取 600 条工作正常。
- 累计 `droppedRecords=2,371,187` 为修复前（runtime 119 自 08-10 运行以来）的历史值，`completeness=false` 持续显示属历史累计口径，建议加"修复后窗口"指标。

---

## 4. 结论与验收判定

### 4.1 修复工程质量

- 上轮 **5 项 P0 全部真修复**（OVS 命名、libvirt UAF、VM 命名、多 router、backing 闸门），24 项 P1 中 21 项真修复、2 项部分修复（FLAG 为文档化设计排除、backing 内容 digest 未绑定）、1 项未修复（lease_time）。修复方向正确、工程质量高，OVSDB 上游事务语义未被破坏。
- 但引入 **2 项潜伏 P0**（快照列宽 P0-6、Redis XTRIM 静默裁剪 P0-7）与多项 P1/P2（OVSDB 超时毒化、DNS 500、迁移回填缺失、新节点同步死锁、gate 竞态、digest 拒绝无收敛路径）。

### 4.2 线上部署状态

- **用户交接的核心事实全部核实为真**（v2-enabled 激活、双节点 V2 能力、400/404 行为、OVS/OVN 就绪）。
- **但线上存在 2 项部署级 P0，导致"后续新建 TeamLab 运行时会走 V2 路径"这一交接预期不成立**：
  1. **P0-A**：跨分支迁移打架 → 新建运行时 100% 500（`IsScenarioBuild` NOT NULL），V1/V2 全路径不可用；
  2. **P0-B**：快照表 `PlanDigest varchar(64)`（代码写 71 字符）→ V2 首次 apply 必失败。

### 4.3 判定

- **V2 不可启用**：即使 P0-A 修复，P0-B/P0-6 会立即阻断首次 apply；观测 P0-7 在 worker 停摆 >76 分钟后静默丢数，违反"全量可回放"验收项。
- **当前 release 不可交付**：新建 TeamLab 运行时 500 为线上可复现阻断；边界输入问题（200 HTML/参数零校验/无限流）未在修复范围。
- **必须修复清单**（优先级）：① P0-A 迁移对齐 + 部署 schema diff 门禁；② P0-B/P0-6 快照列宽（PG 真库验证迁移）；③ P0-7 XTRIM 语义；④ 工作树 data-plane 闸门/能力开关脱钩修复合入（P1-25/26）；⑤ OVSDB 超时 Reset、DNS 重名 500、迁移回填、新节点同步死锁、gate 竞态。之后再按交接矩阵在独立节点做实机验收。

### 4.4 测试遗留

- 本轮创建尝试未留脏数据（0 新 runtime 行、队列无新票据）；runtime 119（ready）未受影响；OVN 逻辑资源 0、br-int 仅 geneve 端口无残留。
- 未执行的矩阵项（同首轮）：真实 V2 apply/cleanup 链路（被 P0-A 阻断）、4/20/50 资产规模、故障注入、浏览器验收——需修复 P0-A/P0-B 后执行。
