# TeamLab 商业化组网控制面设计

## 1. 目标与范围

本设计面向 TeamLab 组网底座的商业化控制能力。现有代码已经具备拓扑 release、单队 runtime、多节点 shard、L3 Fabric、Docker/VM 混合资产、WireGuard 入口、镜像制品和流量观测基础，也已完成单个混合环境的运行验证。本轮不推翻这些数据面能力，而是在其上建立可复用、可外部调用的场景管理、批量编排、精细生命周期和高并发控制面。

目标包括：

- 支持一个大型场景包含数十个网段、最多 128 个资产，并可按节点能力进行多节点分片。
- 支持一个比赛或外部租户管理至少 500 个独立环境目标。
- 支持场景校验、封存、版本化入库、提前分发和重复使用。
- 支持环境创建、接入开关、计算挂起、恢复、重置、排空和销毁。
- 支持每名选手一个独立 VPN 设备会话。
- 与普通比赛容器、普通 VM、培训环境和镜像任务共用统一资源账本和节点执行预算。
- 通过稳定 Open API 供比赛模块及外部系统使用，TeamLab 底座不依赖比赛领域实体。

不在本设计中引入 Kubernetes、Temporal、跨节点 VM 实时迁移或内存暖池。现有节点、Agent、Docker、libvirt、WireGuard、Registry 和运行队列继续作为执行基础。

## 2. 代码审查结论

以下结论基于 2026-07-22 当前工作区代码，包括尚未提交的 Phase 9 改动。

### 2.1 P0：批量部署共享 DbContext

`src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs:83` 的 `DeployGameAsync` 在同一个 scoped `AppDbContext` 上使用 `Task.WhenAll` 并行调用 `DeployTeamAsync`。后者执行多次查询和 `SaveChangesAsync`。EF Core `DbContext` 不支持并发访问，多队部署可能直接出现 second operation 错误。

应由持久化 Rollout 协调器提交 target；每个 target 在独立 DI scope 和独立 `AppDbContext` 中执行。不能通过将当前循环改为无限串行来替代批次编排。

### 2.2 P0：分片执行绕过节点预算

`src/GZCTF/Modules/Runtime/Application/RuntimeExecutionService.cs:137` 只按 ticket 的单个 `TargetNodeId` 进入 `NodeDispatchLimiter`。TeamLab runtime 实际可跨多个 WorkerNode，而 `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs:127` 在每个 runtime 内创建固定容量 16 的局部 semaphore。

多个 runtime 可同时向同一节点发送 Docker、VM、网络和磁盘操作。局部 semaphore 必须删除；所有 Agent 动作按真实目标节点和操作类别进入全局 limiter。

### 2.3 P1：资源预留不反映真实规格

`src/GZCTF/Modules/Runtime/Application/TeamLabPhysicalPlacementService.cs:200` 仅写入 `DockerSlots` 和 `VmSlots`。拓扑资产的 CPU、内存、磁盘和构建临时空间未进入预留模型。不同性能节点和不同规格 VM 被当成相同槽位，容易在启动前超卖。

容量模型必须同时考虑实际占用、活跃预留和安全余量，并覆盖 CPU、内存、磁盘、Docker/VM 槽位、镜像传输、磁盘 IO 和节点操作并发。

### 2.4 P1：控制操作全平台串行

`src/GZCTF/Modules/Runtime/Application/RuntimeExecutionService.cs:399` 将所有非 Create 操作归为 `Control`。控制面 ticket 使用空节点 ID 时会共享同一个默认容量为 1 的 gate。比赛结束时大量 reset/destroy 会逐个执行，资源和网络长期不能释放。

控制操作必须先解析其真实目标节点，再按节点并行。相同 runtime 的互斥由 subject concurrency 保证，不应通过全平台单槽实现。

### 2.5 P1：销毁绑定提前删除

`src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs:171` 提交销毁后立即删除 `PenetrationTeamRuntimeBinding`。若后台清理失败，管理端失去 game/team 到残留 runtime 的直接关系。

绑定应保留到 runtime 达到 `Destroyed`，随后转为历史关系。`CleanupPending` 必须仍可从比赛和队伍定位。

### 2.6 P1：生命周期控制不完整

`src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeOperationJob.cs:3` 只有 Create、Reset、Destroy、拓扑、接入和抓包操作。虽然 runtime 枚举存在 `Stopped`，但没有对应挂起和恢复执行链。

必须分别建立接入开关和计算挂起语义，避免一个“暂停”同时承担维护、封网和资源冻结。

### 2.7 P1：VPN 授权无法支持团队成员

`src/GZCTF/Modules/TeamLab/Application/TeamLabAccessGrantService.cs:73` 固定分配入口网段 Host 2，并在 `:126` 撤销当前 generation 的全部旧授权。授权没有用户和设备身份。

应改为每名成员一个有效设备会话，独立地址、密钥、到期和握手状态；替换设备必须显式撤销旧会话并记录审计。

### 2.8 P1：缺少比赛级 Rollout

当前只有 topology/release、单队 runtime、`PenetrationGameLabBinding` 和 `PenetrationTeamRuntimeBinding`。比赛部署仍是枚举队伍后直接提交，没有容量预检、制品预分发、验证批次、波次、失败阈值、暂停继续、聚合进度和排空。

该缺口不能继续堆入 runtime；需要独立且比赛无关的 Rollout 编排层。

### 2.9 P2：重置次数存在竞态

`src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs:139` 先统计、再提交、再插入记录，没有原子约束。并发请求可能突破上限，基础设施失败也会消耗重置次数。

重置额度应通过带唯一约束的 reservation 记录原子占用，并区分 requested、succeeded、scenario-failed 和 infrastructure-failed。基础设施失败应自动退还额度。

### 2.10 P2：队列公平键没有传递队伍身份

`src/GZCTF/Modules/Runtime/Application/RuntimeQueueSelector.cs:42` 已能按 `OwnerTeamId` 公平调度，但 `src/GZCTF/Modules/TeamLab/Application/ITeamLabRuntimeApplicationService.cs:7` 没有接收 team/tenant concurrency identity。比赛批量部署最终仍以管理员或拓扑所有者身份入队。

通用 runtime 请求应携带 `TenantKey`、`FairnessKey` 和 `SubjectConcurrencyKey`。比赛适配器把队伍映射为 fairness key，外部调用方使用其租户和 target identity。

### 2.11 P2：预制制品尚未形成场景库

`src/GZCTF/Modules/TeamLab/Application/TeamLabScenarioBakeService.cs:29` 已支持发布时创建验证 runtime 并提交不可变 VM 制品，可作为场景入库基础。但当前缺少场景身份、不可变场景版本、验证结果、人工批准、完整 manifest、生命周期、权限、节点分发就绪和引用清理。

### 2.12 P2：放置优化缺少计算预算

`src/GZCTF/Modules/Runtime/Application/TeamLabPhysicalPlacementService.cs:585` 先贪心放置，再进行最多 `groups.Count` 轮改进；每轮反复计算所有跨节点边。几十个网段仍可使用，但需要明确时间预算、基准和提前终止，不能在高并发调度事务内无界运行。

### 2.13 P2：管理端与选手端投影不足

当前投影能展示 runtime 基本状态、重置次数和目标，但缺少镜像分发、批次波次、排队位置、资产级阶段、维护状态、VPN 设备、连接自检、销毁进度和残留核验。管理员无法从比赛、场景、队伍和节点四个维度定位问题。

## 3. 模块边界

采用三个单向依赖边界：

### 3.1 TeamLab.Foundation

负责场景、release、runtime、shard、network、asset、access session、traffic evidence 和生命周期。该模块不引用 Game、Participation、Team 或培训领域实体。

### 3.2 TeamLab.Orchestration

负责 Rollout、target、容量预测、制品分发计划、验证批次、波次、批量控制和聚合投影。它通过 TeamLab Foundation 应用端口管理 runtime，通过 Runtime Scheduling Core 获取容量和节点执行能力。

### 3.3 Penetration.Adapter

负责把比赛、队伍、成员、重置额度和比赛时间映射为通用场景、Rollout、target 和 access subject。比赛模块不得直接写 TeamLab runtime、queue 或制品表。

外部系统和比赛适配器必须调用相同应用服务。底层模块不为内部调用提供绕过权限、幂等、审计或资源准入的旁路。

## 4. 权限模型

现有身份模块只有平台角色、资源所有者和 API Token resource grant。新增平台级资源授权能力，不在 TeamLab 内复制用户体系：

- `AccessGroup`：权限组。
- `AccessGroupMember`：用户或服务账号成员。
- `ResourceRoleBinding`：把用户、权限组或服务账号绑定到资源。
- 资源类型：`scenario`、`scenario-version`、`rollout`、`runtime`、`traffic-evidence`。
- 角色：`Owner`、`Editor`、`Publisher`、`Operator`、`Observer`、`Auditor`。

权限语义：

- `Owner` 管理资源和授权。
- `Editor` 编辑场景草稿。
- `Publisher` 执行校验、批准、入库和下线。
- `Operator` 部署、接入控制、挂起、恢复、重置和销毁。
- `Observer` 查看状态、日志和拓扑。
- `Auditor` 查看及导出流量和审计证据。

API Token 必须同时满足 scope、resource grant 和签发者实际权限。比赛选手通过 `RuntimeSubjectBinding` 获得指定 runtime 的 Player 权限，不获得管理角色。TeamLab 只依赖 `IResourceAuthorizationService`，不直接查询权限组表。

## 5. 场景库模型

### 5.1 TeamLabScenario

表示可复用场景身份，包含所有者、显示信息、权限和当前推荐版本，不包含可变拓扑内容。

### 5.2 TeamLabScenarioVersion

表示不可变场景版本，绑定拓扑 release、制品 manifest、所需节点能力、资源规格、验证规则和允许的运行时变量。

状态为：

`Draft -> Validating -> Ready -> Retired`

只有 Ready 可被 Rollout 使用。任何内容修改都生成新版本；已绑定比赛的旧版本继续有效，只有 Retired 且无引用后才允许清理节点缓存。

### 5.3 ScenarioValidationRun

记录隔离验证 runtime、自动校验结果、人工批准、制品 digest、节点兼容性、操作日志和失败证据。

入库流程为：

`验证部署 -> 自动校验 -> 人工批准 -> 关闭接入 -> 一致性处理 -> 资产封存 -> manifest 签名 -> Registry 入库 -> 可移植性验证 -> Ready`

场景 manifest 固定记录：

- 拓扑和 release hash。
- 每个 Docker/VM 制品 digest。
- 网段、路由、基础设施和观测点定义。
- 资产依赖顺序。
- Bootstrap 和允许覆盖的变量。
- 资源需求与节点能力。
- 校验规则、结果和签发信息。

比赛运行时只注入允许变化的 flag、临时账号、队伍身份、VPN 和其他运行参数，不重新执行 AD 安装、服务部署或镜像改造。

## 6. Rollout 模型

### 6.1 TeamLabRollout

表示一次场景批量交付，不包含比赛专有字段。核心字段包括：

- `ScenarioVersionId`
- `ExternalReference`
- 调用方租户和权限范围
- 目标数量
- 验证批次大小、波次大小和失败阈值
- 并发预算
- 期望接入状态
- 聚合进度和最后错误

状态为：

`Draft -> CapacityChecking -> Distributing -> Verifying -> RollingOut -> Ready -> Draining -> Completed`

异常状态为 `Blocked`、`Failed` 和 `CleanupPending`。暂停 Rollout 只停止提交后续 target，不中断已进入节点执行的操作。

### 6.2 TeamLabRolloutTarget

表示一个队伍或外部租户的一套环境，保存 external subject、runtime ID、波次、变量摘要、接入策略和聚合阶段。target 不复制 runtime 底层事实。

Rollout 使用提前部署：

`场景解析 -> 制品可用性校验 -> 目标节点预分发 -> 节点校验 -> 验证批次 -> 分波部署 -> 就绪待开放`

开赛时只批量开放接入，不集中创建数百套环境。新增队伍只提交增量 target。

## 7. Runtime 精细生命周期

保留现有 runtime、shard、network 和 asset，拆分三个正交状态：

- 部署：`Queued/Distributing/Provisioning/Verifying/Ready/Failed/Destroying/Destroyed`
- 接入：`Closed/Opening/Open/Closing`
- 计算：`Running/Suspending/Suspended/Resuming`

独立操作语义：

- Close/Open Access：关闭或开放 VPN，工作负载继续运行。
- Suspend/Resume Workloads：暂停容器进程、挂起 VM，并保留内存和磁盘状态。
- Reset Runtime：销毁当前 generation 并从相同场景版本重建整套环境。
- Restart Asset：重启单个资产。
- Rebuild Asset：从场景制品重建单个资产；依赖受影响时先展示影响范围。
- Drain/Destroy：停止新操作、关闭接入并精确清理全部资源。

选手只能重置整套环境。管理员可以重启或重建单资产，但单资产成功不能覆盖整套 runtime 的失败状态。

## 8. Access Session

`TeamLabAccessSession` 按 `runtime + user + device` 管理独立 WireGuard peer：

- 独立客户端地址、公钥和一次性配置下载。
- 用户、设备、创建时间、到期、撤销原因和最后握手。
- 每名用户最多一个有效设备。
- 新设备替换旧设备时显式提示、撤销旧 peer 并写入审计事件。
- 管理员可按用户或设备撤销，不影响同队其他成员。

选手连接自检覆盖 VPN 握手、入口地址、DNS 和入口服务，不泄露 WorkerNode 或内部控制面信息。

## 9. 统一资源调度

TeamLab 不建立独立容量账本。普通比赛容器、普通 VM、培训环境、TeamLab runtime、镜像构建和镜像分发全部进入 Runtime Scheduling Core。

节点可用量统一为：

`节点总容量 - 实际占用 - 活跃预留 - 系统安全余量`

统一资源维度包括：

- CPU units
- memory MiB
- persistent/ephemeral disk MiB
- Docker slots
- VM slots
- image transfer concurrency
- disk IO budget
- network/control operation concurrency

Rollout 全量容量计算只是预测，不长期锁住全部目标资源。实际预留按验证批次和部署波次建立带期限租约。普通容器与 TeamLab Docker 共用 Docker 创建预算；普通 VM 与 TeamLab VM 共用 KVM、内存和磁盘预算；镜像分发也计入传输和磁盘预算。

TeamLab 只增加以下放置约束：

- 网段是最小放置单元。
- 同一网段资产默认同节点。
- 节点必须满足资产能力。
- 优先减少跨节点链路。
- 入口网段优先选择健康节点。

节点能力独立判断。缺少 KVM 只排除 VM，不影响 Docker、网络和镜像分发。现有普通容器/VM 请求通过适配器转换为统一 `WorkloadRequest`，不改变现有业务 API。

## 10. 高并发执行

### 10.1 节点执行预算

删除 runtime 内部固定并发。所有 Agent 动作按真实 WorkerNode 和类别进入全局 `NodeDispatchLimiter`：

- Docker/VM image transfer
- Docker create
- VM create
- TeamLab network apply
- readiness probe
- suspend/resume
- destroy/cleanup

多个 Rollout、普通比赛和 TeamLab runtime 共享同一节点预算。预算来自节点能力 manifest，可由平台设置安全上限。

### 10.2 队列公平性

每个请求携带：

- `TenantKey`：资源和配额所属租户。
- `FairnessKey`：比赛队伍或外部 target。
- `SubjectConcurrencyKey`：需要串行变更的 runtime/asset。

Create 按 fairness key 轮转。正在运行环境的关闭接入、故障隔离和销毁优先于普通创建，但所有优先级都使用等待时间老化，防止低优先级永久饥饿。

### 10.3 数据库访问

Rollout coordinator 分页领取 target。每个 target 使用独立 DI scope 和 DbContext。禁止共享 scoped context 执行 `Task.WhenAll`。批量查询使用索引和聚合，不把所有 pending owner 读入内存。

### 10.4 放置算法预算

保留确定性贪心和有限局部改进。增加最大改进轮次、墙钟预算、无改进提前退出和指标记录。放置计算在数据库事务外完成，使用带版本的容量快照；提交时在调度锁下重新验证并原子预留。

## 11. 操作一致性与恢复

所有创建、分发、开放接入、关闭接入、挂起、恢复、重置和销毁都生成持久化 Operation：

- 写操作必须提供 idempotency key。
- 同一 subject 的互斥由 subject concurrency key 保证。
- generation fence 防止旧任务修改新 generation。
- 每个 Agent 动作记录 operation ID、资源身份和 receipt。
- 数据库状态与队列提交使用事务或 outbox 边界。
- 服务重启后根据数据库 desired state 和 Agent inventory 恢复。

错误使用稳定代码分类：

- `capacity_blocked`
- `artifact_unavailable`
- `artifact_distribution_failed`
- `node_unavailable`
- `network_apply_failed`
- `asset_create_failed`
- `readiness_failed`
- `access_apply_failed`
- `cleanup_incomplete`

比赛开始前的节点故障可以重新调度重建。比赛进行中先隔离故障节点、关闭受影响 runtime 接入并生成影响清单；有状态 VM 只有管理员确认后才迁移重建。明确标记为无状态的 Docker 可配置自动恢复。

## 12. 销毁与保留

比赛结束流程为：

`关闭全部接入 -> 冻结新操作 -> 保存最终审计状态 -> 停止抓包 -> 停止资产 -> 删除资产 -> 删除路由和网络 -> 释放地址与端口 -> 释放容量 -> 释放制品引用 -> 节点事实核验 -> Tombstone`

要求：

- 比赛绑定在 runtime 到达 Destroyed 后转为历史关系。
- 清理失败保留绑定、节点和资源身份并进入 CleanupPending。
- Reconcile 只处理已记录资源，不做模糊名称删除。
- 场景 Registry 主制品不随比赛结束删除。
- 节点缓存只有在无场景、Rollout 和运行环境引用后才可清理。
- 流量摘要、操作记录和审计证据按治理策略保留；PCAP 和运行磁盘不默认长期保留。
- Tombstone 保留 release、外部 target、耗时、结果和审计引用。

## 13. 管理端与选手端

### 13.1 管理端

场景库视图：草稿、验证环境、自动校验、人工批准、版本、制品和节点分发。

Rollout 视图：容量预测、预分发、验证批次、波次、失败阈值、暂停继续、接入控制、排空和销毁进度。

队伍环境视图：分片、网段、资产、节点、依赖、操作时间线、VPN 会话、流量证据、实际资源和预留。

节点视图：统一工作负载占用、预留、执行预算、队列、排空、镜像缓存和残留核验。

所有阶段都展示完成数、进行中、失败数、阻塞原因、目标节点和最近事件。页面只读取聚合投影，不在请求内扫描完整事件和资产集合。

### 13.2 选手端

- 显示排队、镜像准备、创建、验证、就绪、维护、重置和结束状态。
- VPN 创建立即显示 operation 进度。
- 每人一台设备，可查看最后握手、一次性下载、替换和撤销。
- 提供连接自检。
- 重置前显示剩余次数和影响。
- 基础设施失败不扣重置次数。
- 关闭接入显示维护或比赛结束，不显示为环境故障。

## 14. 外部 API

所有写操作返回 `202 Accepted` 和 operation ID，要求 `Idempotency-Key`。资源权限由 scope、resource grant 和 role binding 共同判断。

### 14.1 场景

- `GET/POST /api/open/v1/teamlab/scenarios`
- `GET/PATCH/DELETE /api/open/v1/teamlab/scenarios/{id}`
- `POST /api/open/v1/teamlab/scenarios/{id}/versions`
- `GET /api/open/v1/teamlab/scenario-versions/{id}`
- `POST /api/open/v1/teamlab/scenario-versions/{id}/validate`
- `POST /api/open/v1/teamlab/scenario-validations/{id}/approve`
- `POST /api/open/v1/teamlab/scenario-versions/{id}/retire`
- `GET /api/open/v1/teamlab/scenario-versions/{id}/distribution`

### 14.2 Rollout

- `GET/POST /api/open/v1/teamlab/rollouts`
- `GET /api/open/v1/teamlab/rollouts/{id}`
- `POST /api/open/v1/teamlab/rollouts/{id}/capacity-check`
- `POST /api/open/v1/teamlab/rollouts/{id}/prepare`
- `POST /api/open/v1/teamlab/rollouts/{id}/start`
- `POST /api/open/v1/teamlab/rollouts/{id}/pause`
- `POST /api/open/v1/teamlab/rollouts/{id}/resume`
- `POST /api/open/v1/teamlab/rollouts/{id}/open-access`
- `POST /api/open/v1/teamlab/rollouts/{id}/close-access`
- `POST /api/open/v1/teamlab/rollouts/{id}/suspend`
- `POST /api/open/v1/teamlab/rollouts/{id}/resume-workloads`
- `DELETE /api/open/v1/teamlab/rollouts/{id}`
- `GET /api/open/v1/teamlab/rollouts/{id}/targets`

### 14.3 Runtime

- `GET /api/open/v1/teamlab/runtimes/{id}`
- `POST /api/open/v1/teamlab/runtimes/{id}/open-access`
- `POST /api/open/v1/teamlab/runtimes/{id}/close-access`
- `POST /api/open/v1/teamlab/runtimes/{id}/suspend`
- `POST /api/open/v1/teamlab/runtimes/{id}/resume`
- `POST /api/open/v1/teamlab/runtimes/{id}/reset`
- `DELETE /api/open/v1/teamlab/runtimes/{id}`
- `POST /api/open/v1/teamlab/runtimes/{id}/assets/{assetKey}/restart`
- `POST /api/open/v1/teamlab/runtimes/{id}/assets/{assetKey}/rebuild`
- `GET/POST /api/open/v1/teamlab/runtimes/{id}/access-sessions`
- `DELETE /api/open/v1/teamlab/runtimes/{id}/access-sessions/{sessionId}`

### 14.4 观测

- `GET /api/open/v1/operations/{id}`
- `GET /api/open/v1/teamlab/rollouts/{id}/events`
- `GET /api/open/v1/teamlab/runtimes/{id}/events`
- 复用既有流量摘要、流量路径和 PCAP API。

比赛模块通过同一应用契约创建 Rollout 和 target，不直接操作 TeamLab 表。现有 topology、release 和 runtime API 在迁移期保持行为兼容，但旧的比赛批量部署旁路在适配器切换后删除，不长期保留双轨实现。

## 15. 容量与验收基线

控制面必须满足：

- 一个 Rollout 管理至少 500 个 target。
- 单 runtime 支持 32 个网段、128 个资产；更高上限通过配置和基准后开放。
- 500 个 target 的容量预测和批次生成不存在 O(N^2) 数据库查询。
- 同一输入产生确定性放置结果。
- 200 个混合创建请求下，普通容器、普通 VM、培训和 TeamLab 均不超卖、不永久饥饿。
- 主服务重启后 30 秒内恢复待执行 operation 协调。
- 批量关闭 500 个 target 接入时，控制面立即受理并按节点并行执行。
- 进度查询读取投影表，不扫描完整资产和事件历史。
- 流量采集背压不阻塞 runtime 创建和网络控制。
- destroy 后数据库、Agent inventory、Docker、libvirt、namespace、route、firewall、capture、lease 和 reservation 一致。

验收场景包括：

- 128 资产大型混合环境的确定性放置和分片。
- 500 target 的预分发、验证批次、波次、暂停继续和批量结束。
- TeamLab 与普通比赛容器、普通 VM 同时创建的资源竞争。
- 节点容量耗尽、节点掉线、Agent 版本不兼容和 Registry 不可达。
- 主服务在分发、创建、开放接入和销毁阶段重启。
- 并发重置额度、VPN 设备替换和权限组越权检查。
- 销毁失败进入 CleanupPending 后的事实协调和最终清理。

## 16. 实施顺序

### 阶段 A：并发正确性

- 移除共享 DbContext 的批量并发。
- 修复绑定提前删除。
- 原子化重置额度。
- 解除控制操作全平台串行。
- 将 TeamLab 内部 Agent 调用接入真实节点 limiter。

### 阶段 B：统一调度核心

- 建立统一 `WorkloadRequest` 和资源维度。
- 扩展节点容量快照与 reservation。
- 为普通容器、普通 VM 和 TeamLab 接入同一账本。
- 引入 tenant/fairness/subject concurrency key。
- 为放置算法增加计算预算和基准。

### 阶段 C：场景库

- 建立 Scenario、ScenarioVersion 和 ValidationRun。
- 封装验证、人工批准、manifest、Registry 入库和分发。
- 建立不可变版本、权限、引用和清理策略。

### 阶段 D：Rollout

- 实现容量预检、制品预分发、验证批次、波次和失败阈值。
- 实现暂停继续、增量 target、聚合投影和比赛结束排空。
- 将 Penetration 适配器迁移到通用 Rollout。

### 阶段 E：Runtime 生命周期

- 实现接入开关、计算挂起/恢复。
- 实现单资产重启/重建和整套重置。
- 完成 operation、generation fence、receipt 和 recovery。

### 阶段 F：权限与用户体验

- 接入通用权限组和资源角色。
- 建立每用户单设备 Access Session。
- 完成管理端四层视图和选手端状态、自检、重置体验。

### 阶段 G：外部契约与容量验收

- 完成 OpenAPI、中文 Swagger 和操作指导。
- 删除旧比赛批量部署旁路。
- 完成大型环境、500 target、混合业务竞争、故障恢复和批量销毁验收。

阶段按依赖顺序执行。不得长期保留两套资源账本、节点限流、Rollout 或生命周期实现。

## 17. 设计判定

本设计选择“轻量 Rollout + 单队 Runtime 状态机 + 场景库 + 统一调度核心”，而不是继续扩展比赛适配器或引入外部通用编排系统。该路线复用已经运行的 TeamLab 数据面，同时补足商业比赛所需的批量控制、资源治理、权限、用户体验和外部调用能力。
