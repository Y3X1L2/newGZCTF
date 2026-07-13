# Phase 6 Runtime Scheduling and Concurrency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将普通 Docker、VM、培训、AWDP 和 TeamLab 收敛到同一套可公平排队、可原子预留、可能力协商、可恢复且可观察的运行控制面，使控制面能够支撑 300-500 支队伍在线并充分释放 WorkerNode 的真实部署能力。

**Architecture:** PostgreSQL 的 `DeploymentQueueTicket` 继续作为唯一任务事实，Redis 只承担 wake-up 和短期调度互斥；删除 `DeploymentTarget`、`FleetManager` 和双轨状态拼接。控制面拆成只负责快速选择、绑定和原子预留的 scheduling worker，以及独立领取已调度任务并执行长耗时操作的 execution worker，慢 VM 和大镜像不能阻塞后续任务调度。调度以 Docker/VM 独立槽位为硬容量，以实时 CPU/内存负载为过载保护和确定性排序输入；TeamLab 网络组在队列原子预留时绑定节点。Agent 通过版本化 capability feature set 声明能力，并以 Docker create、VM create、Docker/VM 镜像传输、TeamLab 网络和控制操作的独立 gate 承担节点最终并发保护。

**Tech Stack:** .NET 10、EF Core 10、PostgreSQL 17、Redis 7、ASP.NET Core HostedService、OpenTelemetry、React 19、SWR、GZCTF.Agent、xUnit、Testcontainers.PostgreSql、Testcontainers.Redis、k6。

---

## Implementation Progress

### 2026-07-12 计划编写基线

- 代码基线为 `20cf2a3f1fecb81f986a381de0d825e41d4746aa`，本地 `main`、`origin/main` 与远端 GitHub `main` 一致，工作树在计划编写前干净。
- Phase 0-5 已闭环；Phase 5 已冻结 PostgreSQL 事实队列、Redis wake-up、owner-safe lease、节点 live state 和 TeamLab 流量批写契约。
- 本文编写阶段只审计代码和冻结实施计划，不修改业务代码、不生成迁移、不连接或部署服务器。
- 当前状态：Phase 6 初版详细计划和 Agent capability 前置协议已编写。

### 2026-07-13 并发架构压实

- 根据用户对低等待、高并发和多节点吞吐的要求，继续审计 Queue/Agent/Image/TeamLab 真实执行链路并完成计划修订。
- 已将调度和执行物理解耦，增加分类型节点 gate、镜像 single-flight、容器/VM generation 幂等、分类 deadline 和 TeamLab 有界并行流水线。
- 当前状态：计划、协议、总纲口径和进度记录已同步，等待用户确认后进入 Phase 6 业务代码实施。

### 2026-07-13 大单元 1/2 实施进度

- 已将 `DeploymentQueueTicket` 扩展为 workload、operation、stage、generation、subject、blocked reason、retry 和 execution claim 的唯一运行任务事实。
- 已拆分 `RuntimeSchedulingWorker` 与 `RuntimeExecutionWorker`；scheduler 只做 admission、节点绑定和 reservation，不等待 Agent 长操作。
- 已删除生产代码中的 `DeploymentTarget`、`FleetManager`、`QueueManager`、`QueueProcessingService`、旧 target 日志/payload 状态拼接和统一节点 gate；旧 API route 已替换为 `/api/v1/deployment-queue`。
- 已建立 `(DeploymentQueueTicketId, WorkerNodeId)` owner-safe reservation，并引入 `NodeCapacitySnapshotService`，容量取 `max(Agent live count, PostgreSQL active facts) + active reservation`。
- 已建立 `RuntimeQueueSelector`：控制操作优先、owner 轮转、队伍/用户 create concurrency 配额和无容量指数退避。
- TeamLab runtime 创建/重置已改为只编译 logical network groups；`TeamLabPhysicalPlacementService` 在 scheduler lease 内完成 physical node assignment、shard 创建、入口映射和多节点原子 reservation。
- 集中门禁结果：`dotnet build src/GZCTF.slnx -c Release --no-restore` 为 0 warning / 0 error；`RuntimeControlPlaneTests` 7/7 通过，覆盖原子 claim、调度/执行隔离、公平配额、容量快照和 Docker 容量 `3+1` 的双节点 TeamLab late binding。
- 尚未完成：删除 `WorkerNode` 旧 reserved 映射与 `WeightedScheduler`；Agent feature manifest/分类 gate/single-flight；异步镜像 worker；前端阶段反馈；Expand/Backfill/Contract migration；真实并发基准和独立质量审查。Phase 6 当前不得标记完成。

### 2026-07-13 大单元 3/4 实施进度

- Agent capability manifest、分类 execution gate、镜像 single-flight、Docker/VM generation 幂等和同步后 manifest/hash 确认已完成；活动代码不再使用整数协议版本判断能力。
- 镜像分发已拆为数据库 Pending 记录与独立 `ImageDistributionWorker`：跨节点并行、同节点按 Agent manifest 对 Docker/VM 传输分别限流、claim 可过期恢复、失败指数退避，节点失败不再污染 Registry 主副本状态。
- 启动兜底只等待目标节点分发记录，并向当前 deployment ticket 写入 `ImagePreparing`、`ImagePulling`、`ImageVerifying`；Docker/VM create 内不再隐式下载镜像。
- TeamLab 已形成镜像准备与 shard network apply 重叠、资产按自身镜像依赖等待、order group 顺序不越过、probe 有界并行的部署流水线。
- 管理端部署队列已切换 `/api/v1/deployment-queue`，展示 scheduling/scheduled、镜像/创建阶段和可读请求对象；活动任务 1.5 秒轮询，终态自动降频。
- 大单元 4 集中门禁：前端 `pnpm check` 通过；镜像分发与 TeamLab 相关测试 84/84 通过。
- 尚未完成：生命周期 control ticket 全收口、stale Running 事实恢复、三段数据库迁移、全量并发门禁、单次独立质量审查。Phase 6 当前不得标记完成。

### 2026-07-13 Phase 6 代码开发完成

- 普通比赛 Docker、培训/练习 Docker、AWDP、管理员测试容器、VM 和 TeamLab 已统一进入 `DeploymentQueueTicket` 控制面；Create、Extend、Stop、Reset、Destroy 使用同一 subject 并按前序运行事实串行，不再直接取消 Running ticket 或提前释放容量。
- `RuntimeSchedulingWorker` 与 `RuntimeExecutionWorker` 已物理解耦。PostgreSQL claim 校验 owner，stale Running 由生产 worker 周期恢复；Scheduled/Running reservation 周期续租，长镜像和慢 VM 不会因固定 30 分钟 TTL 被错误释放。
- TeamLab Reset 已改为排队执行：加密 reset payload 写入 ticket，前序 Create 完成后依次清理、重规划、late binding、原子多节点预留和重建。TeamLab 镜像按 runtime 持久引用，Pull 期间 Cleanup 不得抢占。
- Agent capability manifest 已稳定化：feature set、execution limit、host facts 和 runtime health 分层；hash 不包含变化时间，heartbeat 上报真实 binary SHA-256，旧 Agent 的空摘要不会清空主站已知值。
- Agent Docker/VM 创建、镜像传输和 TeamLab 网络使用分类 gate；镜像 single-flight、KVM generation libvirt metadata 恢复、owner admission advisory lock 和 challenge test container 异步队列链路均已闭环。
- 三段迁移已完成：`ExpandPhaseSixRuntimeSchedulingConcurrency`、`BackfillPhaseSixRuntimeSchedulingConcurrency`、`ContractPhaseSixRuntimeSchedulingConcurrency`。Expand 包含加密 operation payload 字段；Backfill/Contract 保持 fail-closed 校验，不保留旧双轨运行代码。
- 独立质量审查确认的 12 项问题全部修复：运行中 Create 吞控制任务、TeamLab Reset 旁路、Running 取消超卖、stale recovery 无生产调用、claim owner 覆盖、dispatch limiter 死锁、reservation 过期、TeamLab 镜像误删、Pull/Cleanup 竞态、capability hash 抖动、KVM generation 部分成功和 owner 队列上限竞态。
- 最终门禁：Release build `0 warning / 0 error`；单元测试 `437/437`；PostgreSQL/Testcontainers 集成测试 `227/227`；前端 locale、strict TypeScript、architecture、production build、artifact manifest 和 bundle budget 全部通过；EF 无 pending model changes；`git diff --check` 无 whitespace error；活动源码旧 route/type/protocol 扫描为零。
- Phase 6 **代码开发完成**，本阶段未部署、未连接或修改生产服务器。专用双主站和目标硬件上的 500-owner/300-create 性能阈值仍是预发布容量签收，不使用本开发机结果冒充商业容量结论。

---

## 0. 阶段边界

### 0.1 本阶段必须完成

- `DeploymentQueueTicket` 成为 Docker、VM、培训、AWDP、TeamLab 创建、延期、停止、重置和销毁的唯一运行任务。
- 删除 `DeploymentTarget`、`FleetManager`、`DeploymentQueueStateAccessor` 及其 payload 解析、双列表合并和兼容测试。
- 建立 owner-aware 公平排队、队伍/个人并发创建上限、节点过载保护和无超卖容量预留。
- 将快速调度与长耗时执行拆成独立 worker，使新的可运行任务不被正在拉取镜像、启动 VM 或等待探测的任务阻塞。
- 将 TeamLab 的逻辑网络组放置、节点选择、多节点容量预留和执行限制纳入统一 Fleet 调度。
- Agent 使用 capability feature set 声明 Docker、KVM、Fabric、WireGuard、抓包、cloud-init、镜像下载和自更新能力。
- 镜像引用触发异步预分发；启动时只校验目标节点缓存并执行可观察的兜底拉取。
- Agent 对相同镜像传输和相同运行资源创建执行 single-flight/幂等收敛，不允许重复 pull、争用 `.part` 文件或重试时破坏已经成功的 VM。
- 队列状态对选手端和管理端统一展示排队人数、阻塞原因、镜像阶段、创建阶段和终态。
- 节点同步完成后等待 capability manifest 回报；同步成功不能只表示二进制下载成功。

### 0.2 本阶段明确不做

- 不引入 RabbitMQ、Kafka、Redis list/stream 作为部署任务事实；只有基准证明 PostgreSQL 队列不达标时才重新决策。
- 不引入机器学习、历史失败率预测、动态权重自学习或通用 bin-packing 求解器。
- 不做跨节点单网段拆分；一个 TeamLab 网络组仍是最小放置单元。
- 不把 CPU/内存请求建成预测性硬预留。Docker/VM 槽位是硬容量；CPU/内存实时负载只参与过载保护和排序。镜像自身的 CPU/内存限制仍由 Docker/KVM 执行。
- 不在本阶段完成 Linux SSH/Windows RDP 统一访问抽象；该工作属于 Phase 8。
- 不在本阶段完成完整审计事件、跨服务 trace 和故障恢复工作台；Phase 6 冻结阶段/错误契约，Phase 7 落不可变事件和排障聚合。
- 不恢复 Phase 0 已删除的旧 IR、Scenario、旧培训或旧 Penetration 运行模型。

## 1. 当前代码事实

### 1.1 队列存在双轨

- `src/GZCTF/Models/Data/DeploymentQueueTicket.cs` 保存活动任务身份、owner、Docker/VM 槽位和粗粒度状态。
- `src/GZCTF/Models/Data/DeploymentTarget.cs` 同时保存第二套类型、动作、payload、节点、结果和状态。
- `src/GZCTF/Services/Fleet/FleetManager.cs` 先直接预留容量并创建 `DeploymentTarget`，无节点时再补建 `DeploymentQueueTicket`；同一操作因此可能有两个状态源。
- `src/GZCTF/Services/Fleet/DeploymentQueueViewService.cs` 同时查询 ticket 和 orphan target，再解析 JSON payload 推断用户、队伍、题目、镜像和动作。
- `FleetContainerManager`、`FleetVmService`、`ContainerRepository`、`GameController` 和 `TrainingCourseController` 仍有直接创建/销毁路径；TeamLab destroy 由 operation handler 直接调用 runtime cleanup，不进入队列。
- 当前 `DeploymentQueueKind` 只表达四种创建对象，不能准确表达培训、AWDP、延期、停止、重置和销毁。

结论：Phase 6 必须原子替换双轨，不能继续给两个模型补同步逻辑。

### 1.2 当前领取和公平性不足

- `QueueManager.ProcessPendingAsync` 每轮只按 `CreatedAt` 读取前 20 个 Pending ticket。
- 无容量的 ticket 会立即回到 Pending，但没有 `NotBeforeAt`、attempt/backoff 或结构化 blocked reason；同一批旧任务会被反复扫描。
- `GetQueuePositionAsync` 只按同一种 `Kind` 统计更早任务，和实际能力、owner 公平顺序及控制类操作优先级不一致。
- PostgreSQL CAS 已保证同一 ticket 只能由一个 worker 领取；Phase 6 应保留该事实，不另造 Redis queue。
- 队伍和个人没有统一并发创建上限；一个 owner 的突发请求可以占据批次窗口。

### 1.3 当前容量模型可并发但事实不精确

- `FleetCapacityReservationService` 在 `fleet:scheduler` owner lease 内更新 `WorkerNode.ReservedContainers/ReservedVms`，并使用 `Current + Reserved` 防止瞬时超卖。
- `ReservedContainers/ReservedVms` 不是 owner 级事实；取消、超时和多分片释放只能根据 ticket、target 和 TeamLab asset 反推。
- `ReconcileReservedAsync` 同时读取 `DeploymentTarget`、ticket 和 TeamLab shard，说明计数器已经承担了超出自身表达能力的恢复职责。
- 当前 `WeightedScheduler` 和容量服务各维护一套近似评分公式，存在策略漂移。
- 评分使用百分比余量，没有稳定的绝对 headroom tie-break；同负载比例的大节点和小节点区分不足。
- CPU/内存负载被计分，但没有明确的过载拒绝阈值；节点可能在内存紧张时仍因槽位有余量被选中。

### 1.4 TeamLab 在入队前绑定节点

- `TeamLabRuntimePlanner.LoadPlanningNodesAsync` 在创建 runtime 时读取 `Max - Current - Reserved` 快照。
- `TeamLabAssetPlanner` 先尝试单节点放置，再按多网卡连通网络组做多节点贪心放置；网络组边界正确且应保留。
- `PlanGenerationAsync` 在入队前创建带 `WorkerNodeId` 的 shard/network/asset，并分配入口 UDP mapping。
- 队列随后才调用 `FleetCapacityReservationService.TryReserveBatchAsync`。高并发下，多个 runtime 可以基于同一旧快照选中相同节点，再进入容量失败/回队循环。
- TeamLab ticket 在 `QueueManager.ExecuteReservedTicketAsync` 中明确绕过 `NodeExecutionGate`；同一 runtime 的同 order asset 通过 `Task.WhenAll` 无节点级执行限制。

结论：逻辑网络组可以预先编译，物理 WorkerNode 必须在调度 lease 内完成 late binding 和原子预留。

### 1.5 能力协商仍由魔法版本驱动

- Agent `/api/status` 和 `/api/teamlab/status` 都返回硬编码 `protocolVersion = 3`。
- `WeightedScheduler.GetTeamLabDataPlaneUnschedulableReason` 直接判断 `TeamLabProtocolVersion < 3`。
- Docker、KVM 粗能力已独立，但 WireGuard、Fabric、抓包、cloud-init、镜像下载和自更新仍主要塞在 `TeamLabCapabilitiesJson` 中，缺少稳定 feature ID。
- `WorkerNode.TeamLabAgentVersion` 实际承载整个 Agent 版本；命名和所有权错误。
- 主站和 Agent 各自复制 `TeamLabStatusResponse`，没有自动化的序列化契约门禁。

### 1.6 镜像主副本闭环存在，预分发执行方式不足

- `ImageDistributionRecord + ImageDistributionReference` 已记录模板、节点、hash、状态和引用；Phase 4 已为其建立唯一约束和 advisory transaction lock。
- `DistributeTemplateAsync` 当前在一个请求中按节点串行 pull/download；大 VM 镜像会长时间占用业务请求。
- `ImageTemplate.Status` 会因任一节点分发失败进入 Error，把“存储主副本有效性”和“节点缓存可用性”混在一起。
- 引用类型只有 Game 和 TrainingCourse，缺 Exercise、TeamLab release 和节点新注册后的 active-reference 补分发。
- 启动兜底已存在，但普通 Docker、VM 和 TeamLab 的阶段、错误和重试语义不统一。

### 1.7 当前执行并发会放大用户等待

- `QueueManager.ProcessPendingAsync` 先从最早 20 个 Pending ticket 中完成 claim/预留，再通过 `Task.WhenAll` 执行整批真实部署；`QueueProcessingService` 必须等待整批结束后才开始下一轮调度。一个慢 VM、镜像兜底拉取或启动探测会延迟后续任务获得节点。
- 主站 `NodeExecutionGate` 只有每节点统一 `MaxConcurrentOperationsPerNode = 2`，Docker、VM、镜像、网络和销毁共享一个粗粒度限制；TeamLab ticket 又明确绕过该 gate，既限制了可并行操作，也没有覆盖 TeamLab 突发。
- Agent `DockerService.CreateContainerAsync` 在镜像缺失时隐式 pull 后重试 create；多个并发请求缺同一镜像时会重复传输。相同 Docker network 的 inspect/create 和同一确定性容器名也缺少 keyed lock。
- Agent `KvmService` 使用全局静态 `VirtInstallGate = 1`，所有 VM 的 `virt-install` 串行；创建前无条件 destroy/undefine 同名 VM，调用方超时重试可能破坏实际已经成功的实例。
- VM 镜像下载使用固定 `{templateId}.qcow2.part`，同模板并发下载会争用同一临时文件；Docker pull 同样没有 single-flight。当前 AgentClient 统一使用 10 分钟 timeout，无法区分快速状态、创建、控制和长镜像传输。
- TeamLab 已能并行 apply 不同 shard network、并行创建同一 order group 资产，但镜像 ensure 仍可能按资产重复等待，probe 逐资产串行，失败后的取消和 cleanup 缺少统一并发边界。

结论：本阶段不增加第二套消息队列或复杂自适应算法。吞吐提升来自拆开 scheduling/execution、按资源类别有界并行、相同资源 single-flight、创建幂等和多节点流水线；PostgreSQL 仍是唯一持久任务事实。

## 2. 冻结架构决策

### 2.1 唯一任务事实

`DeploymentQueueTicket` 扩展为完整运行操作事实：

```csharp
public enum RuntimeWorkloadKind : byte
{
    GameContainer = 1,
    ExerciseContainer = 2,
    TrainingContainer = 3,
    AwdpContainer = 4,
    ChallengeTestContainer = 5,
    VirtualMachine = 6,
    TeamLabRuntime = 7
}

public enum RuntimeOperationKind : byte
{
    Create = 1,
    Extend = 2,
    Stop = 3,
    Reset = 4,
    Destroy = 5
}

public enum DeploymentQueueTicketStatus : byte
{
    Pending = 0,
    Scheduling = 1,
    Scheduled = 2,
    Running = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6
}
```

ticket 保存结构化 resource identity，不保存 Flag、token、密码、WireGuard 私钥或完整 cloud-init。执行 handler 根据 subject ID 重新读取当前事实；不存在依赖 JSON payload 恢复业务的路径。

`ActiveIdentity` 表达同一 operation 请求的幂等性；新增 `SubjectConcurrencyKey` 表达同一运行对象的生命周期串行化。所有 active status 对 `SubjectConcurrencyKey` 使用 partial unique index，禁止同一 VM、容器实例或 TeamLab runtime 同时执行 Create/Reset/Destroy。提交 Destroy/Reset 时，应用服务必须在一个 transaction 中取消尚未执行的 Create、释放其 reservation，再创建 control ticket。

操作语义固定为：

- `Create`：从无运行资源进入可用状态。
- `Extend`：只更新当前运行实例的到期时间，不创建新资源。
- `Stop`：选手或教师主动释放运行资源，保留业务对象，后续允许再次 Create。
- `Reset`：销毁当前 generation 并以同一业务对象创建新 generation。
- `Destroy`：管理员、删除流程或治理任务执行终态资源清理；完成后不能由旧 subject 自动重启。

### 2.2 队列阶段契约

```csharp
public enum DeploymentStage : byte
{
    Queued = 0,
    AdmissionChecking = 1,
    CapacityWaiting = 2,
    ImagePreparing = 3,
    ImagePulling = 4,
    ImageVerifying = 5,
    NodeExecutionWaiting = 6,
    ContainerCreating = 7,
    VmCreating = 8,
    RuntimeNetworkApplying = 9,
    RuntimeAssetsCreating = 10,
    BootProbing = 11,
    AccessOpening = 12,
    Extending = 13,
    Stopping = 14,
    Destroying = 15,
    RollingBack = 16,
    Ready = 17,
    Failed = 18,
    Cancelled = 19
}
```

- Phase 6 在 ticket 保存当前 stage、stage message、blocked reason、attempt 和 retry time。
- Phase 7 复用同一 enum 写不可变 stage events；禁止 Phase 7 重新定义第二套名称。
- control 操作（Extend/Stop/Reset/Destroy）不等待创建容量，不受 owner create quota 限制，并高于 create 调度优先级。

### 2.3 简化容量模型

- Docker 和 VM 槽位是唯一硬预留维度，二者独立；缺 KVM 不影响 Docker。
- `MaxContainers`、`MaxVms` 由节点管理员按硬件和镜像负载校准。
- CPU/内存 live load 只做：数据合法性校验、过载拒绝、节点排序。默认拒绝阈值为 CPU `0.95`、Memory `0.92`，均可配置。
- 不创建预测 CPU/memory reservation，不根据短期负载自动修改节点 Max 值。
- 单个 VM/Docker 的资源限制仍在执行前校验并下发 Agent；明显超过主机硬件的单实例直接失败，不进入无限排队。

### 2.4 owner-safe 容量事实

新增 `FleetCapacityReservation`：

```csharp
public sealed class FleetCapacityReservation
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid DeploymentQueueTicketId { get; set; }
    public Guid WorkerNodeId { get; set; }
    public int DockerSlots { get; set; }
    public int VmSlots { get; set; }
    public CapacityReservationStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}
```

- 唯一键 `(DeploymentQueueTicketId, WorkerNodeId)`。
- active reservation 由 ticket owner 精确释放；不再对节点全局计数做盲减。
- 当前占用取 `max(Agent live count, PostgreSQL active resource facts)`，再加 active reservation。
- 多节点 TeamLab 在一个 PostgreSQL transaction 中创建全部 reservation；任一节点不满足时零行提交。
- 删除 `WorkerNode.ReservedContainers/ReservedVms`。节点 API 的 reserved 值由 active reservation 聚合返回，不保留双事实。

### 2.5 确定性调度

调度顺序固定为：

1. 校验 ticket 仍可执行和 owner 并发上限。
2. 根据 workload 计算 required feature set、Docker/VM slots 和 image templates。
3. 过滤离线、心跳过期、关闭调度、能力不足、镜像类型不支持和过载节点。
4. 合并 live count、平台 active facts 和 active reservations。
5. 普通任务选择能容纳请求的最高分节点；TeamLab 先尝试单节点，再按网络组做多节点放置。
6. 评分固定使用 live CPU、live memory、请求后的槽位利用率、有限绝对 headroom；最后按 node name、node ID 稳定排序。
7. 在同一 scheduler lease 和 PostgreSQL transaction 内写 assignment + reservation。

不使用随机数。相同节点快照和任务输入必须生成相同计划。

### 2.6 公平排队和配额

- owner key 优先使用 TeamId，否则使用 UserId；系统维护任务使用独立 system owner。
- 默认 `MaxConcurrentCreatesPerTeam = 4`、`MaxConcurrentCreatesPerUser = 2`、`MaxQueuedCreatesPerOwner = 32`，均由 `RuntimeSchedulingOptions` 配置。一个 TeamLab runtime 只占一个 owner create 名额，其内部资产并行由节点能力限制，不按资产重复扣 owner quota。
- 同一 active identity 继续由 PostgreSQL partial unique index 去重。
- 每轮从 eligible window 中按 owner 当前 Running 数升序、ticket age 降序选择；一轮每个 owner 最多领取一个 create，再进入下一轮。
- 无容量 ticket 写 `BlockedReasonCode` 和指数退避 `NotBeforeAt`，不会持续占据前 20 个扫描窗口。
- 控制操作始终先于 create；不同 owner 的 create 不因一个 owner 的无容量任务被全局阻塞。
- 本阶段不增加全局“运行实例总数”产品配额。比赛/课程/练习仍保留一题一实例等业务规则；Phase 6 管理的是并发创建和节点容量，不擅自改变产品许可。

### 2.7 capability feature set

Agent `/api/status` 返回：

```csharp
public sealed record AgentStatusResponse(
    string AgentVersion,
    string? BinarySha256,
    int ManifestSchemaVersion,
    IReadOnlySet<string> Features,
    AgentExecutionLimits ExecutionLimits,
    AgentHostFacts Host,
    DateTimeOffset ObservedAt);
```

首版 feature ID 固定为：

```text
runtime.docker.v1
runtime.kvm.v1
runtime.vm.cloud-init.v1
image.docker.pull.v1
image.vm.download.v1
teamlab.fabric.l3.v1
teamlab.wireguard.v1
teamlab.flow.v1
teamlab.pcap.v1
maintenance.self-update.v1
```

- feature ID 表达具体合约能力；破坏性变化发布新 ID，不提升一个全局 minimum protocol。
- `ManifestSchemaVersion` 只控制 manifest JSON 解析，不表达业务能力。
- `NodeCapability.Docker/Kvm` 保留为 feature set 的可查询投影，不再由 TeamLab JSON 推断。
- `TeamLabTunnelStatus/FabricStatus` 是运行健康，不是静态能力；TeamLab 调度同时要求 feature 和健康状态。
- 删除 `TeamLabProtocolVersion`、`TeamLabCapabilitiesJson`、`TeamLabAgentVersion`；替换为通用 `AgentVersion`、`AgentBinarySha256`、`CapabilityManifestJson`、`CapabilityHash`、`CapabilityObservedAt`。

### 2.8 镜像预分发

- `ImageTemplate.Ready` 只表示存储服务器主副本和 hash 已验证；单节点分发失败不把模板主状态改成 Error。
- 引用变更只 upsert `ImageDistributionRecord` 为 Pending 并立即返回；后台 worker claim record 后执行传输。
- 不同节点并行；同节点 Docker pull 和 VM artifact download 使用独立限制，默认分别为 2 和 1。相同镜像在节点内只允许一个真实传输，其他请求共享结果。
- Ready 且 hash 一致直接跳过；hash 变化创建同一 record 的新 attempt，不重复保留旧缓存事实。
- 新增引用种类 `Exercise`、`TeamLabRelease`；Game、TrainingCourse 继续使用现有关系。
- 节点首次上报新 capability 时，reconciler 为所有 active reference 补建该节点记录。
- 创建任务优先选择 required images 全部 Ready 的节点；无 Ready 节点但存在可用节点时允许 assignment，ticket 显示 ImagePreparing/Pulling/Verifying 并等待兜底完成。

### 2.9 调度与执行解耦

`RuntimeSchedulingWorker` 和 `RuntimeExecutionWorker` 消费同一张 PostgreSQL ticket 表，但职责严格分离：

1. scheduling worker 批量 claim `Pending` ticket，完成 admission、snapshot、节点选择、TeamLab physical assignment 和 reservation，提交为 `Scheduled` 后立即处理下一批；禁止在该 worker 中调用 Agent。
2. execution worker 使用有界执行池 claim `Scheduled` ticket，转换为 `Running` 后调用 handler；create dispatch ceiling 根据当前可调度节点上报的 DockerCreates/VmCreates 总和计算，并受 `MaxConcurrentAgentCalls` 默认 256 的上限保护。长镜像、VM create、boot probe 只占 execution worker，不占 scheduler。
3. control lane 与 create lane 分开领取，保留独立 execution worker 配额；Stop/Destroy/rollback 不会因 create backlog 无执行线程。
4. 多主站唯一领取依赖 PostgreSQL compare-and-set/`SKIP LOCKED`；Redis wake-up 丢失只增加最多一个 polling interval 的延迟，不改变正确性。
5. `fleet:scheduler` owner-safe lease 只覆盖批量 snapshot、assignment 和 reservation transaction，不覆盖 Agent 调用。lease 续约失败时本批 assignment 回滚，不启用本地旁路。
6. execution claim 写 `ClaimOwner/ClaimExpiresAt` 并定期续约；进程退出后由其他实例回收。恢复前先按资源 identity 向 Agent inspect，不能盲目重放 create。

主站 `NodeDispatchLimiter` 仅根据 Agent manifest 避免向单节点瞬间堆积过多 HTTP 请求；真正的跨主站最终限制由 Agent gate 和资源幂等保证，不把进程内 semaphore 当作分布式事实。

### 2.10 节点并发、single-flight 与幂等边界

Agent 将操作拆为相互独立的资源类别：

| Category | 自动默认值 | permit 覆盖范围 |
| --- | --- | --- |
| DockerCreates | `clamp(logicalCpu / 2, 2, 8)` | network ensure、container create/start/attach；不包含镜像 pull |
| VmCreates | CPU >= 16 为 2，否则 1 | overlay、cloud-init seed、virt-install；boot probe 不长期持有 permit |
| DockerImageTransfers | 2 | Docker pull/inspect |
| VmImageTransfers | 1 | VM artifact download、sha256、原子替换 |
| TeamLabNetworkOperations | 4 | bridge、namespace、route、Fabric apply/cleanup |
| ControlOperations | 2 | stop、destroy、rollback、缓存清理的保留通道 |

- 配置可以覆盖自动值；feature 不存在时对应 limit 为 0，feature 存在时必须至少为 1。非法组合使该 feature unhealthy，不默默改成无限制。
- Docker 按 normalized digest/reference、VM 按 `templateId + sha256` single-flight。共享传输使用服务级 timeout/cancellation；单个等待请求取消只停止等待，不能取消其他任务共享的传输。
- Docker network 按 network name、容器按确定性 identity/generation 加 keyed lock；同 generation 已运行时返回现有事实，规格冲突返回稳定 `runtime_identity_conflict`。
- VM 按 VM name/generation 加 keyed lock；相同 generation 的 domain 已存在时 inspect 并返回，不得先 destroy。只有显式 Reset/Destroy 才清理旧 domain 和 overlay。
- VM Ready cache 保存已验证 digest sidecar；文件大小、mtime 和 sidecar 一致时不重复计算整块大文件 hash。`.part` 仅由 single-flight owner 写入，失败保留可安全续传状态，完成后原子替换。
- Docker/VM create 不再隐式拉镜像；镜像未就绪返回稳定 `image_not_ready`，由独立 ImagePreparing/Pulling/Verifying 阶段处理。
- AgentClient 按操作使用 deadline：status/heartbeat 5 秒、network/control 60 秒、Docker create 3 分钟、VM create 5 分钟；镜像传输使用进度停滞和总时长双 deadline，不使用统一 10 分钟 HttpClient timeout。
- 只有资源级幂等落地后，主站才允许对连接建立失败、连接重置和无响应体中断做至多一次瞬时重试；明确 4xx、业务错误和已返回响应的操作不自动重试。

### 2.11 TeamLab 多节点部署流水线

1. physical assignment 和原子 reservation 完成后，同时启动所有唯一镜像的准备任务与所有 shard network apply。
2. route/Fabric apply 只等待相关 shard network ready，不等待无关镜像；入口开放必须等待入口网络、路由和入口资产健康。
3. 每个 asset 只等待自身镜像 ready；同一 order group 内按目标节点 Agent limit 并行，不同节点自然聚合吞吐。下一 order group 只等待其依赖组终态。
4. boot/service probe 使用有界并行，不占用 VM/Docker create permit；单资产探测失败保留节点、资产和探测阶段证据。
5. 任一必要资产失败时停止派发后续依赖组，等待已启动任务完成或取消，再通过 ControlOperations 通道并行 rollback 已创建 shard。
6. Reset/Destroy 对所有 shard 并行下发，按 runtime generation 幂等；最终 residual scan 确认 bridge、namespace、route、container、VM、capture 和临时文件均已清理。

## 3. 文件结构与职责

### 3.1 Runtime queue/domain

- Create: `src/GZCTF/Modules/Runtime/Domain/DeploymentQueueTicket.cs`
  - 拥有 workload、operation、stage、owner、subject、retry 和终态。
- Create: `src/GZCTF/Modules/Runtime/Domain/FleetCapacityReservation.cs`
  - 拥有 ticket-node 槽位预留事实。
- Create: `src/GZCTF/Modules/Runtime/Contracts/DeploymentQueueContracts.cs`
  - 对外状态、阶段、blocked reason 和分页 DTO。
- Create: `src/GZCTF/Modules/Runtime/Application/RuntimeAdmissionPolicy.cs`
  - active identity、owner create quota 和可执行性校验。
- Create: `src/GZCTF/Modules/Runtime/Application/RuntimeWorkloadResolver.cs`
  - 从结构化 subject 解析 required features、slots、images 和 handler key。
- Create: `src/GZCTF/Modules/Runtime/Application/RuntimeOperationDispatcher.cs`
  - 按 workload + operation 路由到小型 handler。
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeSchedulingWorker.cs`
  - 批量 claim Pending、执行原子 assignment/reservation，不调用 Agent。
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeExecutionWorker.cs`
  - 按节点 manifest 聚合额度运行有界执行池，领取 Scheduled ticket、续约 claim 并调用 handler。
- Create: `src/GZCTF/Modules/Runtime/Application/Handlers/*RuntimeOperationHandler.cs`
  - 适配现有容器、VM、TeamLab 生命周期服务。
- Delete: `src/GZCTF/Models/Data/DeploymentTarget.cs`
- Delete: `src/GZCTF/Services/Fleet/FleetManager.cs`
- Delete: `src/GZCTF/Services/Fleet/DeploymentTargetLogHelper.cs`
- Delete: `src/GZCTF/Services/Fleet/DeploymentQueueStateAccessor.cs`
- Delete: `src/GZCTF/Services/Fleet/QueueProcessingService.cs`

### 3.2 Scheduling/capacity

- Create: `src/GZCTF/Modules/Runtime/Application/NodeCapacitySnapshotService.cs`
  - 一次查询合并 live state、active facts、reservations 和 image readiness。
- Create: `src/GZCTF/Modules/Runtime/Application/RuntimeScheduler.cs`
  - 普通任务和 TeamLab placement 共用候选过滤和 scoring。
- Create: `src/GZCTF/Modules/Runtime/Application/RuntimeQueueSelector.cs`
  - owner-aware eligible window、quota、backoff 和稳定顺序。
- Create: `src/GZCTF/Modules/Runtime/Application/NodeEligibilityEvaluator.cs`
  - feature、健康、过载和容量拒绝原因。
- Create: `src/GZCTF/Modules/Runtime/Application/NodeDispatchLimiter.cs`
  - 按节点和 operation category 使用 manifest limit 控制主站派发突发；不承担持久容量事实。
- Modify: `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs`
  - 收缩为 reservation repository/transaction service；删除节点可变 reserved 计数。
- Delete: `src/GZCTF/Services/Fleet/QueueManager.cs`
  - 由 scheduling/execution workers 取代，禁止继续保留“调度并等待整批真实执行”的兼容路径。
- Delete: `src/GZCTF/Services/Fleet/NodeExecutionGate.cs`
  - 由 operation-aware `NodeDispatchLimiter` 和 Agent 最终 gate 取代。
- Delete: `src/GZCTF/Services/Fleet/WeightedScheduler.cs`
  - 评分和能力判断进入 Runtime 模块，避免普通/TeamLab 两套策略。
- Delete: `src/GZCTF/Services/Fleet/TeamLabCapacityFacts.cs`
  - TeamLab reservation 直接由 reservation rows 表达。

### 3.3 Agent capability and execution

- Create: `src/GZCTF/Modules/Runtime/Contracts/AgentCapabilityContracts.cs`
- Create: `src/GZCTF/Services/Fleet/AgentCapabilityEvaluator.cs`
- Create: `src/GZCTF.Agent/Services/AgentCapabilityService.cs`
- Create: `src/GZCTF.Agent/Services/AgentOperationGate.cs`
- Create: `src/GZCTF.Agent/Services/AgentResourceLock.cs`
- Create: `src/GZCTF.Agent/Services/ImageTransferSingleFlight.cs`
- Modify: `src/GZCTF.Agent/Controllers/StatusController.cs`
- Modify: `src/GZCTF.Agent/Controllers/ContainerController.cs`
- Modify: `src/GZCTF.Agent/Controllers/VmController.cs`
- Modify: `src/GZCTF.Agent/Controllers/ImageController.cs`
- Modify: `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- Modify: `src/GZCTF.Agent/Services/HeartbeatWorker.cs`
- Modify: `src/GZCTF/Controllers/NodesController.cs`
- Modify: `src/GZCTF/Services/Fleet/AgentClient.cs`
- Modify: `src/GZCTF/Services/Fleet/NodeDeployService.cs`
- Delete: `src/GZCTF/Services/Fleet/WorkerNodeCapabilityHelper.cs`

主站和 Agent 当前是独立项目，不能直接引用主站业务程序集。实施时在两个项目分别保存 wire DTO，并用 `AgentCapabilityContractTests` 对固定 JSON fixture 做双向序列化验证；不为少量 transport DTO 新建大而空的 shared project。

### 3.4 Image distribution

- Create: `src/GZCTF/Modules/Runtime/Application/ImageDistributionCoordinator.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/ImageDistributionWorker.cs`
- Create: `src/GZCTF/Modules/Runtime/Contracts/ImageDistributionContracts.cs`
- Modify: `src/GZCTF/Services/Fleet/ImageDistributionService.cs`
  - 只保留单 record 传输、校验和清理执行；删除同步全节点 orchestration。
- Modify: `src/GZCTF/Services/Fleet/ImageDistributionReconcileService.cs`
- Modify: `src/GZCTF/Modules/Runtime/Domain/ImageDistributionReference.cs`
- Modify: `src/GZCTF/Modules/Ctf/Infrastructure/ChallengeMutationOperationHandler.cs`
- Modify: `src/GZCTF/Modules/Training/Infrastructure/*`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs`

### 3.5 TeamLab execution pipeline

- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`
  - 镜像、network、route、asset order group 和 probe 组成依赖明确的有界并行流水线。
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`
  - 统一成功、取消、rollback 和 residual scan。
- Modify: `src/GZCTF.Agent/Services/DockerService.cs`
  - 确定性 create、network keyed lock、禁止隐式 pull。
- Modify: `src/GZCTF.Agent/Services/KvmService.cs`
  - VM keyed lock、幂等 inspect/create、移除全局 `VirtInstallGate = 1` 和无条件 destroy。
- Create: `src/GZCTF.Agent/Services/VmImageDownloadService.cs`
  - VM single-flight、可续传 part owner、digest sidecar 和原子替换。

### 3.6 Frontend

- Create: `src/GZCTF/ClientApp/src/hooks/useRuntimeOperation.ts`
- Create: `src/GZCTF/ClientApp/src/components/runtime/RuntimeOperationProgress.tsx`
- Create: `src/GZCTF/ClientApp/src/components/runtime/RuntimeOperationProgress.module.css`
- Modify: `src/GZCTF/ClientApp/src/components/InstanceEntry.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/VmInstanceEntry.tsx`
- Modify: `src/GZCTF/ClientApp/src/components/topology/penetration/TeamLabRuntimeObservability.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/queue/Index.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/nodes/Index.tsx`
- Regenerate: `src/GZCTF/ClientApp/src/generated/Api.ts`

页面不得新增私有视觉体系；进度、状态、错误和节点能力必须复用 Phase 2 公共组件/token，并遵循 react-best-practices。

## Task 1: 建立 Phase 6 边界测试和可重复基准

**Files:**
- Create: `src/GZCTF.Test/UnitTests/Runtime/RuntimeControlPlaneBoundaryTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Runtime/RuntimeQueueConcurrencyTests.cs`
- Create: `scripts/runtime/run-scheduler-benchmark.ps1`
- Create: `scripts/runtime/k6-runtime-admission.js`
- Create: `docs/commercialization/benchmarks/phase-06-runtime-baseline.md`
- Modify: `docs/platform-commercialization-audit-progress.md`

- [ ] **Step 1: 固化旧双轨失败断言**

架构测试扫描生产程序集并要求阶段退出时不存在 `DeploymentTarget`、`FleetManager`、`DeploymentQueueStateAccessor`；Controller、Repository、TeamLab application 不得直接调用 Agent create/destroy，必须经 runtime operation handler 或 TeamLab node executor adapter。

- [ ] **Step 2: 固化调度与执行不变量**

测试定义以下不可放宽断言：同 active identity 只有一个 active ticket；两个 scheduling worker 不能重复 claim；scheduler 不调用 Agent 且不等待 execution；两个 execution worker 不能重复执行；同一批 reservation 不超出 MaxContainers/MaxVms；TeamLab batch reservation 全成或全败；Docker-only 不要求 KVM；能力必须按 feature set 评估；相同输入的 assignment 顺序一致。

- [ ] **Step 3: 记录当前基准而不伪造数字**

`run-scheduler-benchmark.ps1` 创建隔离 PostgreSQL/Redis，播种 4 个节点和 500 个 owner、300 个 pending tickets，记录 enqueue、claim、reserve 和 queue-position p50/p95/p99。`phase-06-runtime-baseline.md` 只填写真实执行结果；工具缺失时写明“未执行”和环境，不填估算吞吐。

- [ ] **Step 4: 大单元基线验证**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~RuntimeControlPlaneBoundaryTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~RuntimeQueueConcurrencyTests
```

Expected: 新架构断言因旧双轨、魔法 protocol 和 owner-less reservation 存在而失败；fixture、PostgreSQL 和 Redis 本身健康。

## Task 2: 原子替换 DeploymentTarget 双轨

**Files:**
- Create/modify Runtime queue/domain files in section 3.1
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueService.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueViewService.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentExecutionService.cs`
- Modify: `src/GZCTF/Services/Fleet/FleetContainerManager.cs`
- Modify: `src/GZCTF/Services/Fleet/FleetVmService.cs`
- Modify: `src/GZCTF/Repositories/ContainerRepository.cs`
- Modify: `src/GZCTF/Controllers/GameController.cs`
- Modify: `src/GZCTF/Controllers/TrainingCourseController.cs`
- Modify: `src/GZCTF/Services/AwdpInstanceService.cs`
- Delete old files listed in section 3.1
- Test: `src/GZCTF.Test/UnitTests/Runtime/RuntimeOperationDispatcherTests.cs`
- Test: `src/GZCTF.Integration.Test/Tests/Runtime/RuntimeWorkerIsolationTests.cs`

- [ ] **Step 1: 扩展 ticket 为结构化生命周期任务**

增加 `WorkloadKind`、`OperationKind`、`Stage`、`BlockedReasonCode`、`StageMessage`、`AttemptCount`、`NotBeforeAt`、`ClaimOwner`、`ClaimExpiresAt`、`SubjectConcurrencyKey`、subject/resource display 字段。active identity 使用 `workload:operation:subject:generation`；subject concurrency key 使用 `workload:subject`。Create/Reset 防重，Destroy 重复调用复用同一 active ticket，terminal 后允许新 generation。

- [ ] **Step 2: 拆分 dispatcher handler**

`DeploymentExecutionService` 只保留 registry dispatch；Game/Exercise/Training/AWDP/TestContainer/VM/TeamLab handler 各自负责加载 subject、调用现有领域服务和映射稳定错误码。handler 不自行选择节点、不自行预留、不创建第二张任务表。

- [ ] **Step 3: 所有生命周期入口只提交 ticket**

创建、延期、停止、重置、销毁 Controller/Repository 先提交或复用 ticket，立即返回统一 `DeploymentQueueStatusModel`。Cron cleanup 和管理员批量销毁也提交 system-owner control ticket；不能因为是后台任务绕过队列。

- [ ] **Step 4: 拆开 scheduling 和 execution worker**

`RuntimeSchedulingWorker` 只把 Pending 推进到 Scheduled；`RuntimeExecutionWorker` 以所有可调度节点的 DockerCreates/VmCreates 上报值之和作为 create dispatch 需求，并受 `MaxConcurrentAgentCalls = 256` 上限和 node dispatch limit 双重约束。control lane 至少保留 2 个独立执行名额，不计入被 create 占满的额度。stale Scheduling 可直接回 Pending；stale Running 必须先 inspect 资源 identity 再决定完成、继续探测或安全重试。

- [ ] **Step 5: 删除双轨生产代码**

删除 `DeploymentTarget` DbSet、entity configuration、payload parser、target controller 和 view union，同时删除 `QueueManager`、`QueueProcessingService` 和 `NodeExecutionGate`。`/api/admin/DeploymentTargets` 保留现有路由名称只会形成无价值兼容，因此同步删除；管理端切换到版本化 queue API 后不得保留旧 route。

- [ ] **Step 6: 大单元集中验证**

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~RuntimeOperationDispatcherTests|FullyQualifiedName~DeploymentQueueServiceTests|FullyQualifiedName~RuntimeControlPlaneBoundaryTests"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~RuntimeWorkerIsolationTests
```

Expected: build 0 warning/0 error；所有运行入口只生成 ticket；慢 execution 不阻塞 scheduler 继续生成 Scheduled ticket；control lane 可在 create backlog 中执行；旧双轨和旧 worker/gate 类型扫描为零。

## Task 3: 建立 Agent capability manifest 和执行限制

**Files:**
- Create/modify files in section 3.3
- Modify: `src/GZCTF/Models/Data/WorkerNode.cs`
- Modify: `src/GZCTF/Modules/Runtime/Contracts/NodeLiveStateContracts.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/AgentCapabilityContractTests.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/NodesControllerTests.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/NodeDeployServiceTests.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/AgentConcurrencyContractTests.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommandBuilderTests.cs`

- [ ] **Step 1: 实现 feature manifest**

Agent 根据命令、设备和配置生成稳定 feature set；KVM 必须同时满足 `virsh + /dev/kvm + CPU virtualization`。Fabric 必须满足 `ip + wg + (iptables | nft)`。pcap 与 flow 分别按 `tcpdump/dumpcap` 能力声明，不用一个 TeamLab Available 布尔值覆盖全部功能。

- [ ] **Step 2: 主站按 feature 子集判断**

`AgentCapabilityEvaluator` 接收 `RequiredFeatures` 并返回稳定 reason code + 可读 message。普通 Docker、普通 KVM、TeamLab Docker、TeamLab VM、capture、cloud-init、自更新分别评估；任何路径不得读取 protocol minimum。

- [ ] **Step 3: Agent 本机执行 gate**

`AgentOperationGate` 使用 section 2.10 的六类独立 semaphore 和自动默认值。VM create permit 在 `virt-install` 完成后释放，boot probe 不持有；Docker/VM 镜像互不阻塞；ControlOperations 保留独立 permit。配置覆盖值与 manifest 上报值必须一致，不能只展示不执行。

- [ ] **Step 4: 资源幂等和 single-flight**

`DockerService` 按 network、container identity/generation 加 keyed lock，删除 NotFound 隐式 pull。`KvmService` 按 VM identity/generation inspect/create，删除静态全局 `VirtInstallGate` 和 create 开始的无条件 destroy。`ImageTransferSingleFlight` 分别按 Docker normalized reference 和 VM template/hash 合并请求；共享任务结束后从字典删除，等待者取消不传播到共享任务。VM 下载从 Controller 抽入 `VmImageDownloadService`，以 digest sidecar、resume part 和 atomic move 闭环。

- [ ] **Step 5: AgentClient deadline 与有限重试**

删除 `BuildClient` 内统一 10 分钟 timeout。按 status、control/network、Docker create、VM create、image transfer 构造 request deadline；image transfer 同时要求 progress heartbeat，停滞超时后才取消。仅在 inspect 可证明操作幂等后，对连接建立失败/重置最多重试一次；响应业务失败不重试。

- [ ] **Step 6: 同步闭环**

节点同步流程比较 binary sha256；一致时跳过下载。发生替换时等待新 heartbeat 的 binary hash 和 required feature set，超时返回结构化失败。节点管理显示“二进制已同步 / 能力已回报 / 缺失 feature”，不把 HTTP 200 当作完整成功。

- [ ] **Step 7: 大单元集中验证**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~AgentCapabilityContractTests|FullyQualifiedName~AgentConcurrencyContractTests|FullyQualifiedName~NodesControllerTests|FullyQualifiedName~NodeDeployServiceTests|FullyQualifiedName~AgentDockerServiceTests|FullyQualifiedName~TeamLabCommandBuilderTests"
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj -c Release --no-restore
```

Expected: fixture 双向序列化一致；缺 KVM 的 Docker 节点仍可调度 Docker/Fabric；缺具体 feature 只影响对应能力；相同 Docker/VM 镜像只有一个真实传输；相同 resource create 幂等；Docker、VM、镜像、网络和 control gate 互不串行且各自不超过上报 limit。

## Task 4: owner-safe capacity snapshot 和原子 reservation

**Files:**
- Create/modify files in section 3.2
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Modules/Runtime/Infrastructure/Persistence/RuntimeHistoryEntityConfigurations.cs`
- Modify: `src/GZCTF/Modules/Runtime/Infrastructure/RedisNodeLiveStateStore.cs`
- Test: `src/GZCTF.Test/UnitTests/Runtime/NodeCapacitySnapshotTests.cs`
- Test: `src/GZCTF.Integration.Test/Tests/Runtime/FleetReservationConcurrencyTests.cs`

- [ ] **Step 1: 建立一次性 snapshot 查询**

单次调度批次预加载节点 identity、live state、active DataContainer、active VmInstance、current-generation TeamLab asset、active reservations 和 required image readiness。禁止按 ticket 或 shard N+1 查询。

- [ ] **Step 2: 实现 reservation transaction**

在 `fleet:scheduler` owner lease 内开启 PostgreSQL transaction，重新验证 ticket claim 和节点 snapshot，按 node ID 顺序写 reservation。多节点任一失败 rollback 全部 rows；重复调度同一 ticket 命中唯一键并读取原 reservation，不重复占用。

- [ ] **Step 3: 统一释放和 reconcile**

Cancel、execution failure、rollback、terminal success、stale claim 和 startup reconcile 都按 ticket ID 更新 reservation。success 后由 active runtime fact 接管容量，reservation 才释放；capacity snapshot 使用 `max(agent live, active facts)` 消除 heartbeat 延迟空窗。

- [ ] **Step 4: 删除 mutable reserved counters**

删除 `WorkerNode.ReservedContainers/ReservedVms` 和 `FleetManager.Reserve/Confirm/Release*`。节点列表通过 grouped reservation query 返回 reserved projection。

- [ ] **Step 5: 大单元集中验证**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~NodeCapacitySnapshotTests|FullyQualifiedName~FleetCapacityReservationServiceTests"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~FleetReservationConcurrencyTests
```

Expected: 100 个并发 reservation 对小容量节点零超卖；取消只释放自身 rows；多节点 reservation 没有半提交；heartbeat 延迟不产生容量空窗。

## Task 5: 公平 selector、确定性 scheduler 和 TeamLab late binding

**Files:**
- Create/modify scheduler files in section 3.2
- Modify: `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeSchedulingWorker.cs`
- Modify: `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeExecutionWorker.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAssetPlanner.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimePlanner.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeOperationJob.cs`
- Test: `src/GZCTF.Test/UnitTests/Runtime/RuntimeQueueSelectorTests.cs`
- Test: `src/GZCTF.Test/UnitTests/Runtime/RuntimeSchedulerTests.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRuntimeSchedulingTests.cs`
- Test: `src/GZCTF.Integration.Test/Tests/Runtime/RuntimeQueueConcurrencyTests.cs`

- [ ] **Step 1: owner-aware eligible selection**

selector 查询 `Pending && NotBeforeAt <= database CURRENT_TIMESTAMP`，按 control/create 分 lane；create 按 active owner count、CreatedAt、Id 稳定选择。无容量写稳定 blocked code 和 capped exponential backoff；有新 heartbeat、capacity release 或 image-ready wake-up 时清除相关 ticket 的等待时间。

scheduling worker 每次在短 lease 内连续处理 eligible batches，直到窗口为空或达到单轮时间预算；每个成功任务只推进到 Scheduled。execution worker pool 独立 claim Scheduled，禁止 scheduling worker await 任何 Agent、镜像或 probe 操作。

- [ ] **Step 2: 单一 eligibility/scoring**

普通和 TeamLab 共用 `NodeEligibilityEvaluator`。score 只包含有限四项：CPU load、memory load、post-placement slot ratio、bounded absolute headroom。删除 `WeightedScheduler` 和 TeamLab 私有 score。

- [ ] **Step 3: TeamLab logical plan 与 physical assignment 分离**

`TeamLabRuntimePlanner` 只编译 network groups、地址 lease、asset/interface intent，不写 WorkerNodeId、不创建 UDP mapping。每个逻辑网络组先创建 `WorkerNodeId = null` 的 pending shard，network/asset 只绑定 shard；只有 Planning/Queued 状态允许空节点。Queue scheduler 在真实 snapshot 上生成 assignment，事务内填充 shard/network/asset node binding、entry mapping 和 reservations，随后将 runtime 从 Planning/Queued 转为 Scheduled；Scheduled 及以后状态出现空 WorkerNodeId 必须 fail closed。

- [ ] **Step 4: TeamLab 建立依赖明确的并行流水线**

assignment 后镜像准备与 shard network apply 同时启动；route 只等待相关 network，每个 asset 只等待自身镜像，同一 order group 按节点限制并行，probe 有界并行且不占 create permit。不同节点不设全局 gate。失败时停止后续依赖组，等待/取消在途任务，再通过独立 control gate 并行清理已创建 shard并执行 residual scan。

- [ ] **Step 5: 大单元集中验证**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~RuntimeQueueSelectorTests|FullyQualifiedName~RuntimeSchedulerTests|FullyQualifiedName~TeamLabRuntimeSchedulingTests|FullyQualifiedName~TeamLabRuntimeFoundationTests"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~RuntimeQueueConcurrencyTests
```

Expected: owner burst 不垄断 batch；慢 VM execution 不阻塞后续 scheduler；相同输入 assignment 完全一致；Docker capacity 3 + 1 的两个节点可原子放置 4-slot TeamLab；镜像与网络阶段可重叠、不同节点并行、同节点遵守 Agent limit；任一节点不足时零 reservation、runtime 保持 queued。

## Task 6: 异步镜像预分发和启动兜底

**Files:**
- Create/modify image files in section 3.4
- Modify: `src/GZCTF/Modules/Runtime/Infrastructure/Persistence/RuntimeHistoryEntityConfigurations.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/ImageDistributionServiceTests.cs`
- Test: `src/GZCTF.Integration.Test/Tests/Runtime/ImageDistributionWorkerTests.cs`

- [ ] **Step 1: 把引用更新和传输解耦**

Game challenge、Exercise、Training binding 和 TeamLab release 保存时，在同一业务 transaction 写 reference intent；transaction commit 后 wake worker。业务保存不等待所有节点传输，分发失败只落 record error。

- [ ] **Step 2: PostgreSQL claim + per-node transfer limit**

worker 使用 `FOR UPDATE SKIP LOCKED` claim Pending/Failed-due record，不同节点并行；同节点 Docker transfer 最多 2、VM transfer 最多 1。Agent 对相同 image key single-flight，因此预分发与启动兜底并发命中时只发生一次真实传输。attempt、NextAttemptAt、LastErrorCode 和 progress timestamp 持久化；服务重启可恢复 Pulling stale record。

- [ ] **Step 3: 启动时选择 Ready locality 并兜底**

scheduler 优先 required template 全 Ready 节点；若目标节点缺失，则 ticket 按 ImagePreparing -> ImagePulling -> ImageVerifying 更新，等待同一 distribution record。Docker 和 VM 使用同一 stage contract，底层仍分别调用 pull 与 OCI artifact download。

- [ ] **Step 4: 引用释放和新节点 reconcile**

比赛结束、课程解绑、Exercise 删除、TeamLab release 无 runtime/reference 后释放引用。零引用且无 active instance 才进入 cleanup。节点新增对应 feature 时，为全部 active references 创建该节点 record；节点禁用调度不立即删除缓存。

- [ ] **Step 5: 大单元集中验证**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~ImageDistributionServiceTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~ImageDistributionWorkerTests
```

Expected: 同模板同节点只传输一次；不同节点并行；同节点 Docker 最多 2 个 pull、VM 最多 1 个 artifact download 且互不阻塞；单个等待者取消不终止共享传输；共享引用释放不误删；节点分发失败不破坏模板 Ready 主状态。

## Task 7: 队列阶段、节点能力和无刷新前端反馈

**Files:**
- Create/modify frontend files in section 3.5
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueViewService.cs`
- Modify: `src/GZCTF/Controllers/NodesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabRuntimeContracts.cs`
- Test: `src/GZCTF.Test/UnitTests/Fleet/DeploymentQueueViewServiceTests.cs`

- [ ] **Step 1: 统一 status DTO**

DTO 返回 operation、workload、stage、stageMessage、blockedReasonCode/message、queuePosition、peopleAhead、owner label、resource label、target node、image templates 和 timestamps。对玩家隐藏内部 node host、Agent error body 和 sensitive payload；管理员视图显示可读 node/template 名称。

- [ ] **Step 2: 启动点击立即反馈**

Create API 返回 ticket 后，`useRuntimeOperation` 立即把本地状态置为 Queued 并开始 SWR polling；无需刷新页面即可进入动画。polling 在 terminal 后停止，tab hidden 时降频，多个组件共享同一 key 去重请求。

- [ ] **Step 3: 独立展示镜像与创建阶段**

`RuntimeOperationProgress` 按 stage 显示镜像准备、拉取、校验、节点执行等待、容器/VM 创建、启动探测和 ready。不得把 image pulling 文案混入 vm-creating；不得新增页面级散落样式。

- [ ] **Step 4: 节点管理无 raw JSON**

节点页显示普通 Docker、KVM、Fabric、WireGuard、flow、pcap、cloud-init、self-update feature 和 execution limit；不可调度原因来自服务端 reason code。SWR 保持节点顺序稳定，不做整页强制刷新。

- [ ] **Step 5: 大单元集中验证**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~DeploymentQueueViewServiceTests
pnpm --dir src/GZCTF/ClientApp check
```

Expected: 后端 DTO 无 raw payload；前端 locale/strict/build 通过；创建后不刷新即可看到 queued -> image -> create -> ready。

## Task 8: contract migration、恢复、容量基准和阶段退出

**Files:**
- Create: `src/GZCTF/Migrations/<timestamp>_ExpandPhaseSixRuntimeControlPlane.cs`
- Create: `src/GZCTF/Migrations/<timestamp>_BackfillPhaseSixRuntimeControlPlane.cs`
- Create: `src/GZCTF/Migrations/<timestamp>_ContractPhaseSixRuntimeControlPlane.cs`
- Modify: `docs/commercialization/agent-capability-protocol.md`
- Create: `docs/commercialization/runbooks/runtime-scheduling-and-recovery.md`
- Modify: `docs/commercialization/benchmarks/phase-06-runtime-baseline.md`
- Modify: `docs/platform-commercialization-audit-progress.md`

- [ ] **Step 1: Expand**

新增 ticket operation/stage/retry/claim 字段、reservation 表、Agent manifest 字段和必要索引。Expand 不删除 DeploymentTarget/旧 reserved 字段，允许 backfill 校验。

- [ ] **Step 2: Backfill**

对已有 ticket 复制 linked target 的 action、node、result、error 和时间；对 orphan target 创建 terminal ticket。payload 只用于迁移时解析结构化 identity，不复制 Flag/token/password。无法解析的 active target 使迁移 fail closed；terminal 无法解析记录为 system maintenance subject 并保留审计 ID。

- [ ] **Step 3: Contract**

校验 active target=0、每个 active ticket identity 唯一、reservation 无孤儿、WorkerNode manifest 可解析后，删除 target FK/table、旧 protocol/capability JSON、reserved counters 和旧索引。代码、API、前端类型和测试同步删除旧概念，不保留双读 adapter。

- [ ] **Step 4: 核对并更新能力协议实现结果**

按实际实现更新 `agent-capability-protocol.md` 的代码路径、最终 feature catalog、schema fixture hash 和真实节点验收结果。不得在实现阶段擅自改变已冻结 feature 语义；需要破坏性调整时先修改文档并重新确认。

- [ ] **Step 5: 真实并发与故障验收**

隔离环境执行：

- 500 owner 入队，至少 300 个 create burst，零重复 claim、零超卖、owner 分布无单 owner 批次垄断。
- 两个主站 worker 同时消费 PostgreSQL queue；停止一个 worker 后另一个回收 stale claim。
- 让 32 个 execution worker 持续执行慢 VM/mock image transfer，同时继续入队 100 个新任务；scheduling worker 必须继续推进到 Scheduled，不能等待前一批 execution 结束。
- 在 create backlog 占满 create worker 时提交 Stop/Destroy；control lane 必须立即获得独立 worker 和 Agent control permit。
- 对同一 Docker image 和同一 VM template/hash 各发起 20 个并发 ensure；每类只有一个真实传输，取消 5 个等待者不影响剩余请求完成。
- 对同一 container generation 和 VM generation 并发发起 create，并模拟响应返回前连接中断；最终各只有一个实例，重试不执行 VM destroy/undefine。
- 在 16 logical CPU 节点同时创建 4 个 VM，实测 `virt-install` 并发不超过 2 且能达到 2；慢 boot probe 不阻塞后续 VM 进入 create。
- Docker capacity `3 + 1` 节点部署四资产多网段 TeamLab，必须形成两个 shard 并可销毁无残留。
- TeamLab 验收 trace 必须证明 image prepare 与 network apply 时间区间有重叠、两个节点资产同时创建、order group 依赖未被越过、失败 rollback 使用 control lane。
- Docker-only 节点缺 KVM 仍可接收普通 Docker 和 TeamLab Docker；VM ticket 保留 CapacityWaiting/CapabilityUnavailable。
- 删除目标节点缓存后，ticket 显示 image stages，Agent 从存储服务器恢复并继续创建。
- Redis wake-up 中断时 PostgreSQL polling 继续；scheduler lease 不可用时 fail closed，不产生本地并发调度旁路。

- [ ] **Step 6: 冻结退出阈值**

在同一验收硬件记录实际结果，最低控制面门槛：

- 500 次 enqueue 中 0 duplicate active identity、0 HTTP 5xx。
- 300 ticket planning/claim 全批处理时间不超过 5 秒，不包含真实镜像下载和 Agent 创建时间。
- scheduler/reservation 数据库操作 p95 不高于 250 ms。
- 有容量 ticket 从 enqueue 到 Scheduled 的 p95 不高于 2 秒；前端在 API 返回后 1 秒内显示 Queued/当前 stage。
- execution backlog 不降低 scheduling worker 的持续推进能力；基准期间 Scheduled 数必须继续增长，不能出现等待整批 execution 结束的平台期。
- 任一节点 `current facts + active reservations` 不超过 MaxContainers/MaxVms。
- owner create concurrency 不超过配置值，control operation 不被 create backlog 阻塞。
- Agent 每类并发不超过 manifest limit；有足够独立请求时 Docker/VM create 能达到对应 limit，不因全局 gate 被压成 1。
- 同 image key 真实传输次数为 1；不同节点传输并行，同节点 Docker/VM transfer 分别遵守 2/1 限制。
- 所有失败 ticket 有稳定 error code、stage 和可读 message。

如果专用验收硬件无法达到阈值，必须先用 query plan、lock wait 和 trace 证明瓶颈；不能直接以“换消息中间件”替代定位。

- [ ] **Step 7: 单次独立质量审查**

派发一个独立 agent，只审查 Phase 6 diff：队列唯一事实、reservation ownership、multi-main claim、TeamLab late binding、capability feature、镜像引用、敏感信息和旧代码删除。确认问题后主 agent 直接修复；不再反复派发多轮无目的审查。

- [ ] **Step 8: 最终集中门禁**

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-build
pnpm --dir src/GZCTF/ClientApp check
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj --startup-project src/GZCTF/GZCTF.csproj
git diff --check
```

Expected: build 0 warning/0 error；全量单元/集成/前端/EF/diff 门禁通过；旧类型、旧 route、魔法 protocol 和 reserved mutable counter 扫描为零。

## 4. 数据库索引和生命周期

### 4.1 DeploymentQueueTickets

- active identity partial unique index。
- subject concurrency partial unique index，覆盖全部 active status。
- claim index：`(Status, NotBeforeAt, CreatedAt, Id)` where Pending。
- execution claim index：`(Status, TargetNodeId, CreatedAt, Id)` where Scheduled。
- owner fairness index：`(OwnerTeamId, Status, CreatedAt, Id)` 与 `(OwnerUserId, Status, CreatedAt, Id)`。
- node running index：`(TargetNodeId, Status, StartedAt, Id)`。
- terminal retention 继续使用 Phase 4 `(Status, CompletedAt, Id)` partial index 和聚合/清理策略。

### 4.2 FleetCapacityReservations

- unique `(DeploymentQueueTicketId, WorkerNodeId)`。
- active capacity index `(WorkerNodeId, Status, ExpiresAt)`。
- ticket release index `(DeploymentQueueTicketId, Status)`。
- reservation terminal rows保留 7 天用于恢复核对，随后由 Phase 4 governance job 清理；长期容量趋势写 aggregate，不永久保留逐次 reservation。

### 4.3 ImageDistributionRecords

- 保留 `(ImageTemplateId, WorkerNodeId)` unique。
- worker claim index `(Status, NextAttemptAt, WorkerNodeId, Id)`。
- reference 唯一键继续为 `(DistributionRecordId, Kind, ResourceId)`。
- transfer attempt/error 随 record 保留；Phase 7 如需完整历史，写独立 audit event，不在 record 无限追加 JSON。

## 5. 错误码

稳定错误码至少包含：

```text
owner_queue_limit_reached
owner_concurrency_limit_reached
node_capability_unavailable
node_overloaded
node_capacity_exhausted
image_distribution_pending
image_distribution_failed
image_not_ready
reservation_conflict
reservation_lost
execution_claim_lost
subject_no_longer_deployable
agent_contract_invalid
agent_execution_timeout
runtime_identity_conflict
runtime_partial_failure
runtime_rollback_incomplete
```

错误正文不得包含 Agent auth token、Registry auth、Flag、WireGuard 私钥、完整 userdata 或 shell command。管理员能看到 node/template/subject 可读名和 correlation ID；选手只看到可行动的阶段和原因。

## 6. 回滚策略

- 应用回滚允许回到 Phase 5 制品，但只能在 Expand 或 Backfill 阶段；Contract 删除旧表后不能用旧应用连接新 schema。
- Expand/Backfill migration 必须可重入并保留迁移校验表；失败后修复数据并继续，不手工跳过校验。
- Contract 前执行数据库备份/PITR 检查。Contract 后数据回滚通过 PITR，不提供把新 ticket 再压回含敏感 payload 的 DeploymentTarget 反向迁移。
- Agent capability rollout 先发布 Agent，再启用主站 feature requirement。缺 manifest 节点标记不可调度，不猜测旧能力。
- 镜像分发 worker 可单独停止；停止时编辑保存和已 Ready 节点启动仍可用，缺缓存启动进入明确等待，不回退到不可观察的同步请求。

## 7. Phase 6 完成定义

只有同时满足以下条件才可宣布完成：

- 业务代码只存在一套 DeploymentQueueTicket 任务事实。
- 普通 Docker、VM、培训、AWDP、TeamLab 的 create/control 操作全部接入统一 dispatcher。
- PostgreSQL/Redis 多主站并发验证无重复 claim、无超卖、无 owner starvation 批次垄断。
- scheduling 与 execution 已物理解耦；慢 VM/镜像/probe 不阻塞新任务分配，control lane 不被 create backlog 阻塞。
- TeamLab physical node assignment 在 reservation transaction 内完成，多节点全成或全败。
- Agent 调度完全使用 feature set；不存在 `protocol >= N` 业务判断。
- Agent 按操作类别执行真实并发限制；Docker/VM 创建幂等，镜像 single-flight，连接中断重试不产生重复资源或破坏成功 VM。
- 镜像预分发异步、跨节点并行、节点内按 Docker/VM 分别限流、同镜像去重且可恢复；启动阶段和错误可见。
- TeamLab 镜像、网络、路由、资产和 probe 使用依赖明确的有界并行流水线，Reset/Destroy/rollback 可并行且无残留。
- 前端无需刷新即可显示排队、镜像、创建、探测和终态。
- 迁移、runbook、capability protocol、benchmark 和总进度文档完整。
- 旧类型、旧 API、旧前端适配、旧测试和旧迁移兼容代码已从 active code 删除。
- 单次独立审查问题全部关闭，最终集中门禁和真实双节点验收通过。
