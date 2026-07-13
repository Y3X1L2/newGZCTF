# 商业化总纲审计进度记录

更新时间：2026-07-13

## 2026-07-13 Phase 7 计划编写与代码事实基线

- Phase 7 大单元 2 已完成：原始日志采用 timer-or-batch flush、失败保留、缓冲保护和退出 drain；数据库与 SignalR 统一结构化映射，日志查询支持 correlation/event/node/resource。
- 已新增 `OperationalCorrelation` 和教师/管理员 mutation audit filter，排除 heartbeat、外部 API 和普通用户流量；专项累计 `13/13` 通过。
- Phase 7 大单元 1 已完成：统一 event/error taxonomy、追加式 `OperationalEvent`、同事务 writer、敏感 detail allowlist、查询模型、事件索引和 180 天 retention 已落地。
- 已生成 Phase 7 Expand/Backfill/Contract 三段迁移；Backfill 仅为活动 ticket、image distribution、TeamLab runtime 建立 snapshot 基线，Contract 对缺失基线 fail closed。
- 大单元 1 集中门禁通过：事件/保留策略专项测试 `10/10`，EF 模型与迁移一致。
- Phase 7 以提交 `4d4c6fe8d082ee74ea5f63ade2e9ecbbd576875d` 为基线，当前分支为 `codex/phase-07-observability-audit-recovery`，本阶段尚未部署生产服务器。
- 已确认原始 `LogModel` 缺 trace/correlation/event/error/resource/node 维度；`DatabaseSink` 的低流量 flush 和退出 drain 不可靠，不能作为审计唯一事实。
- 已确认 `DeploymentQueueTicket`、`ImageDistributionRecord` 和 TeamLab event 分别承载当前状态或局部历史，缺少统一追加式事件、错误分类和 correlation timeline。
- 已确认 OpenTelemetry 框架 instrumentation 已存在，但运行队列、Agent、镜像、节点、TeamLab 和恢复缺业务 meter/span；`GZCTF.Cache` ActivitySource 尚未注册。
- 已确认 Phase 6 stale ticket recovery 只核对数据库状态，Agent 已具备 Docker label 和 KVM generation metadata，却没有 GZCTF-managed runtime inventory API。
- Phase 7 冻结为四类职责：当前状态事实、追加式 `OperationalEvent`、原始 `LogModel`、OpenTelemetry。恢复只读取数据库当前事实和 Agent inventory，不从日志或事件反推状态。
- 详细实施计划已写入 `docs/commercialization/phase-07-observability-audit-recovery.md`，稳定事件和错误分类已写入 `docs/commercialization/event-taxonomy.md`。开发按七个大单元推进，只在大单元边界集中验证，最终执行一次独立质量审查。

## 2026-07-13 Phase 6 代码开发完成

- `DeploymentQueueTicket` 已成为 Docker、培训/练习、AWDP、管理员测试容器、VM 和 TeamLab 的唯一运行任务事实；旧 `DeploymentTarget/FleetManager/WeightedScheduler` 活动实现和旧 API route 已删除。
- 调度与执行已拆分，owner 公平选择、队伍/个人并发配额、PostgreSQL owner admission lock、节点容量快照、原子 reservation、claim owner 校验、stale recovery 和 Scheduled/Running reservation 续租已闭环。
- Stop/Destroy/Reset 不再吞掉运行中的 Create，也不直接取消 Running 并释放容量；同 subject 控制任务等待前序运行任务终态后进入独立 control lane。
- TeamLab Reset 使用加密 ticket payload 排队完成清理、重规划、多节点 late binding、原子预留和重建；TeamLab runtime 镜像引用、Pull/Cleanup 互斥和 active TeamLab VM 防误删已完成。
- Agent capability hash 已排除动态时间，heartbeat 使用缓存的真实 binary SHA-256；KVM domain 通过 libvirt description 恢复 generation sidecar，避免部分成功后永久 identity conflict。
- 独立 agent 审查报告的 12 项问题已逐项核实并全部关闭。最终门禁：Release build 0 warning/0 error，单元 437/437，PostgreSQL 集成 227/227，前端 locale/strict/architecture/production build/artifact/bundle 全通过，EF 无 pending model changes，Git whitespace 和活动旧架构扫描通过。
- Phase 6 代码开发完成，未部署生产。500-owner/300-create、双主站故障接管和目标节点真实吞吐保留为预发布专用环境容量签收，不以本地开发机数字替代。

## 2026-07-13 Phase 6 大单元 1/2 实施

- 统一 ticket 控制面、scheduling/execution worker 拆分、owner-safe reservation、容量事实快照、公平 selector 和 TeamLab late binding 主体已实现。
- 解决方案编译为 0 warning / 0 error，Runtime 控制面集中测试 7/7 通过，包含 Docker 容量 `3+1` 双节点 TeamLab physical assignment。
- Agent capability 与分类并发、镜像异步分发、前端阶段反馈、旧 scheduler/counter 清理、三段迁移、真实并发验收和独立质量审查尚未完成；Phase 6 保持进行中。

## 2026-07-13 Phase 6 并发方案压实

- 已继续核对 `QueueManager`、`QueueProcessingService`、`NodeExecutionGate`、Agent `DockerService/KvmService/ImageController`、`AgentClient` 和 TeamLab 部署链路；本次仍只修改计划文档，未修改业务代码、未生成迁移、未部署或连接服务器。
- 已确认当前 scheduler 会等待整批真实 deployment 完成后才进入下一轮；主站每节点统一 gate 默认 2，TeamLab 绕过该 gate；Agent KVM 全局 `VirtInstallGate = 1`；Docker create 隐式 pull；VM `.part` 和 Docker pull 均缺 single-flight；AgentClient 统一 10 分钟 timeout。
- Phase 6 已冻结为统一控制面：PostgreSQL ticket 唯一事实、独立 scheduling/execution worker、owner-safe 原子 reservation、operation-aware dispatch、Agent 分类型最终 gate、资源级幂等和镜像 single-flight。不引入第二套持久消息队列或通用求解器。
- Agent 自动并发边界已写入协议：Docker create `clamp(cpu/2, 2, 8)`、VM create 1/2、Docker/VM image transfer 2/1、TeamLab network 4、control 2；配置可覆盖，feature 缺失时对应 limit 为 0。
- TeamLab 执行冻结为镜像准备与 network apply 并行、route 按相关 network、asset 按自身 image 和 order group、probe 有界并行、rollback 走独立 control lane；不同节点聚合吞吐，不设置跨节点全局 gate。
- 已同步补充并发故障验收：慢 execution 不阻塞调度、control 不被 create backlog 阻塞、同镜像 20 并发只传输一次、相同 generation create 收敛、VM 达到且不超过 gate、双节点 TeamLab 流水线 trace 和销毁无残留。

## 2026-07-12 Phase 6 计划编写

- 已基于 `20cf2a3f1fecb81f986a381de0d825e41d4746aa` 审计当前运行控制面；本次只编写计划，未修改业务代码、未生成迁移、未部署或连接生产服务器。
- 已确认 `DeploymentQueueTicket + DeploymentTarget + FleetManager` 形成双轨任务事实；`DeploymentQueueViewService` 仍合并两套数据并解析 payload，属于 Phase 6 必须原子删除的旧架构。
- 已确认当前 QueueManager 只扫描最早 20 个 Pending ticket，缺 owner 公平、backoff、结构化 blocked reason 和队伍/个人并发创建上限。
- 已确认 WorkerNode mutable reserved counters 无 owner，TeamLab 在入队前按容量快照绑定 WorkerNode，执行时又绕过节点 gate；高并发下存在过期计划、重复争抢和恢复反推复杂度。
- 已确认 Agent 与主站仍以硬编码 `protocolVersion = 3` 和 `TeamLabCapabilitiesJson` 判断能力；Phase 6 将改为具体 feature set，并保持 Docker、KVM、Fabric、抓包、cloud-init、镜像和自更新独立。
- 已确认镜像分发事实和引用基础可复用，但当前全节点串行传输、模板主状态与节点缓存状态耦合、引用种类和新节点 reconcile 不完整。
- 详细计划已写入 `docs/commercialization/phase-06-runtime-scheduling-concurrency.md`，能力协商前置契约已写入 `docs/commercialization/agent-capability-protocol.md`。冻结方向为 PostgreSQL 唯一事实队列、Redis wake-up/lease、owner-safe reservation rows、owner-aware 公平选择、TeamLab late binding、feature-set 能力协商、异步镜像预分发和无刷新阶段反馈；等待用户确认后再实施。

## 2026-07-12 Phase 5 实施启动

- Phase 5 以提交 `b45eb9b` 为基线，目标是将 Redis 收敛为单连接、可审计、可降级的缓存、协调和高频数据缓冲底座，PostgreSQL 继续作为业务事实来源。
- 已重新核实旧 `CacheMaker`/`CacheHelper`、分散 Redis connection、无 owner 端口 lease、节点 heartbeat 持久/实时状态混合、TeamLab flow 同步落库和 PostgreSQL queue polling 等当前实现。
- 实施按五个大单元推进，只在大单元完成后集中验证；完成前执行一次独立 agent 质量审查并集中修复确认项。
- 当前进入大单元 1：Redis 连接治理、typed HybridCache、projection revision 与核心投影接入。本阶段不部署生产。
- Phase 5 大单元 1-2 已完成生产实现：单 Redis provider、typed HybridCache、事务内 projection revision、owner lease、端口归属和 PostgreSQL queue wake-up 已闭环；两次大单元集中 build 均达到 0 warning/0 error，缓存专项 15/15。
- 当前进入大单元 3：节点 live state/metrics 与 TeamLab flow 高频缓冲。本阶段仍未部署或连接生产服务器。
- Phase 5 大单元 3 已完成并通过 59 项专项验证：实时节点状态优先于 PostgreSQL checkpoint，旧序列不会覆盖 metric，也不会阻断 capability/version 持久事实更新；TeamLab flow 已切换为有界 stream、pending reclaim、binary COPY 批写和 PostgreSQL-only 查询。
- Phase 5 大单元 4 已完成：`CompletePhaseFiveRedisGovernance` migration 回填端口 owner、投影 revision 和节点 checkpoint；EF pending-model 检查通过；PostgreSQL migration 与 Redis pending recovery 集成验证 2/2。
- Phase 5 大单元 5 已完成代码闭环：runbook、keyspace/stream 检查脚本、k6 workload 和基准模板已落库；独立 agent 首轮与最终复核确认的队列 CAS、缓存恢复/factory、lease-lost、本地 lease 竞态、flow batching/reclaim cursor、Nginx owner 问题已全部修复。
- 最终门禁通过：build 0 warning/0 error，单元测试 508/508，真实 PostgreSQL/Redis 集成测试 226/226，前端 strict TypeScript、EF 模型一致性、旧实现残留扫描、Git whitespace 均通过；复核补丁另通过并发租约/缓存 3/3、TeamLab 深层 pending reclaim 2/2。Phase 5 代码开发完成，仍未部署生产；专用双主站 k6 容量与基础设施故障演练保留为预发布环境验收证据。

## 目标

本文件记录 `platform-commercialization-master-plan.md` 重写前的结构审计事实、发现和写作进度，防止上下文压缩导致信息丢失。最终交付文件仍是 `docs/platform-commercialization-master-plan.md`。

## 已确认输入

- 用户需求文件：`d:\Downloads\平台后续开发需求.md`。
- 当前仓库：`D:\newgz\newGZCTF-main`。
- 当前分支：`main`。
- CodeGraph 索引规模：1011 个文件、27262 个节点、79249 条边。
- 总纲目标：商业级网络安全综合演练平台，面向长期运营和高峰几百队在线场景。

## 已确认硬性要求

- 总纲开头必须先呈现项目结构、代码结构、数据库结构和运行链路。
- 阶段编排必须体现紧迫性、前置依赖、并行边界、清理顺序和开发收益。
- 数据库治理、Redis 缓存、高并发调度、审计日志必须拆成独立阶段。
- 前端整体设计语言将重构；除首页大改外，其他页面总体布局保持稳定。
- 前端重构必须遵循 react-best-practices 原则。
- 前端验收必须达到高度组件化和模块化，设计语言通过公共组件层和全局样式层切换，页面内不得散落视觉样式。
- TeamLab 组网能力属于最高紧迫度，必须前置架构底座解耦，不能拖到后期才解除与 Penetration 超大服务和页面的绑定。
- 总纲必须补足平台整体架构解耦和模块化设计，不能只列业务需求。
- 后续文档调整必须以当前代码为唯一事实来源。
- 旧 IR/Scenario 独立模块、旧培训体系、失效页面和旧组网概念不能长期兼容保留。
- 文档不得使用降低确定性的泛化表述、占坑标记和未完成标记。

## 已确认项目结构事实

- 主站项目：`src/GZCTF`。
- Agent 项目：`src/GZCTF.Agent`。
- AppHost 项目：`src/GZCTF.AppHost`。
- 单元测试：`src/GZCTF.Test`。
- 集成测试：`src/GZCTF.Integration.Test`。
- 前端项目：`src/GZCTF/ClientApp`。
- 主站发布流程会构建前端并发布 Agent 单文件到 `agent/gzctf-agent`。

## 已确认数据库事实

- `AppDbContext` 维护 105 个 `DbSet`。
- `OnModelCreating` 约 1990 行，集中承载大量实体关系、唯一索引和删除行为。
- 数据实体覆盖站点配置、用户队伍、CTF、练习、培训、理论题、AWDP、镜像、节点、部署队列、VM、TeamLab、Penetration、旧 IR/Scenario。
- 新旧培训实体并存：旧 `TrainingDirection/TrainingModule` 与新 `TrainingCourse` 体系同时存在。
- TeamLab runtime 已有 shard、network、asset、traffic flow、capture job 数据模型。
- 旧 IR/Scenario 独立实体和迁移快照仍存在。
- `DeploymentQueueTicket` 已存在 active identity 过滤唯一索引，队列具备“同一运行对象不重复创建活动任务”的数据库边界。
- TeamLab runtime 已存在 `(GameId, TeamId)` 唯一语义，shard/network/asset 均有运行时归属字段，适合继续做 runtime 事实表。
- 当前 `EnvironmentType` 仍以 `WindowsVM` 表达 VM 类环境，Linux VM SSH 需要拆出 OS 类型和访问协议。

## 已确认架构热点

- `PenetrationService.cs`、`TeamLabDeploymentService.cs`、`TrainingCourseAdminController.cs`、`Penetration.tsx`、课程详情页、节点管理页属于重点拆分对象。
- TeamLab runtime 已具备分片模型，但部署链路仍与 Penetration 编辑模型耦合。
- 普通部署队列具备并行执行、容量预留和 Redis 锁；TeamLab 路径仍需要统一纳入同一套运行底座。
- 前端存在超大 API 文件、超大页面、超大样式文件，后续风格重构必须先建立组件与数据请求边界。
- 当前控制器共 27 个，行数集中在 `TrainingCourseAdminController.cs`、`GameController.cs`、`EditController.cs`、`NodesController.cs`、`TrainingCourseController.cs`、`AccountController.cs`。
- 服务层行数集中在 `PenetrationService.cs`、`TeamLabDeploymentService.cs`、`AgentClient.cs`、`NodeDeployService.cs`、`DockerManager.cs`、`DockerImageRegistryService.cs`、`FleetContainerManager.cs`。
- Agent 行数集中在 `TeamLabNetworkService.cs`、`DockerService.cs`、`KvmService.cs`、`ImageController.cs`。
- 前端超大页面集中在课程详情、渗透编排、节点管理、Teams、镜像管理、题目编辑、理论试卷、AWDP 服务。
- e2e 测试仍包含 `ir-challenge.spec.ts`、`scenario-create.spec.ts`、`scenario-play.spec.ts`、`topology-editor.spec.ts`，与清理旧 IR/Scenario 的产品方向冲突。

## 已确认运行时事实

- `ServicesExtension` 注册了 CacheHelper、QueueManager、QueueProcessingService、FleetHealthCheckService、ImageDistributionReconcileService、PortLeaseRefreshService、NginxSyncService、VmReadyService、LocalNodeRegistrar、LocalNodeMetricsService。
- Redis 用于分布式缓存、SignalR backplane、Fleet 分布式锁和端口分配；无 Redis 时存在内存缓存或本地锁 fallback，但 Fleet 模式下 Redis 不可达会影响并发安全。
- `QueueManager`、`FleetCapacityReservationService`、`NodeExecutionGate`、`DeploymentExecutionService` 构成普通环境创建的并发运行底座。
- `ImageDistributionService` 和 `ImageDistributionRecord` 已形成镜像预分发基础，后续要完善引用释放、节点缓存清理和启动兜底阶段展示。
- Agent 已具备 Docker、KVM、TeamLab 网络、镜像下载、状态、维护接口，后续要把能力协商和版本控制从硬编码升级为协议能力表。

## 已确认前端事实

- 前端使用 Vite、React 19、Mantine 9、SWR、vite-plugin-pages。
- 前端 build 先执行 locale 校验和 TypeScript strict check，再执行 Vite build。
- 路由由 `src/pages/**/*.tsx` 自动生成，复杂页面容易把业务状态堆在页面文件中。
- 已使用 SWR，但课程详情、组网管理、节点管理仍需拆分 hook、组件和延迟渲染边界。
- 视觉重构需要先建立设计系统和组件层，避免在每个页面重复修样式。
- `ClientApp/src/components` 当前 145 个文件、24762 行；其中 `YinyuReactBits.tsx` 1520 行，`useCTFScreenData.ts` 1070 行，`GridScan.tsx` 925 行，`useScreenData.ts` 881 行。
- `ClientApp/src/styles` 当前 54 个文件、20453 行；其中 `YinyuRefinement.css` 9001 行，`YinyuDesignLab.css` 2822 行，`YinyuTheme.css` 1960 行。
- 页面、组件和样式中同时存在全局 `yy-*` class、CSS module、inline style、Mantine classNames，说明当前样式入口分散，不能低成本切换全局设计语言。
- Phase 2 的验收标准必须从“页面变好看”改为“样式控制权收敛到设计 token、Mantine theme、公共组件、组件级样式模块”。

## 2026-07-10 组网与整体架构复核

- `Services/TeamLab` 当前 11 个文件、4830 行；`Services/Fleet` 当前 32 个文件、7941 行。
- `TeamLabDeploymentService.cs` 2451 行，仍同时承载部署、分片、路由、资产、镜像、VM init、事实记录、清理和容量释放。
- `TeamLabPublishedTopologyService`、`TeamLabAssetPlanService`、`TeamLabPlanService`、`TeamLabDeploymentService` 仍直接使用 `PenetrationConfig`、`PenetrationNode`、`PenetrationNetwork`、`PenetrationEdge`。
- `PenetrationService.cs` 3447 行，仍同时承载编辑模型、发布快照、计划、运行摘要、运行路由、计分和兼容状态。
- TeamLab 已有 `TeamLabRuntime`、`TeamLabRuntimeShard`、`TeamLabRuntimeNetwork`、`TeamLabRuntimeAsset`、`TeamLabTrafficFlow`、`TeamLabTrafficCaptureJob`，说明运行事实模型已成形；短板是拓扑/发布/计划/部署仍绑定 Penetration 编辑模型。
- 总体架构必须按展示层、API 层、应用服务层、领域服务层、运行编排层、数据缓存层、Agent 执行层、观测审计层拆分职责。

## 2026-07-10 总纲规划流复审

- 当前总纲已经覆盖功能域和技术基础，但阶段列表缺少交付波次、关键路径、阶段准入、阶段退出、迁移切换和旧代码删除门槛。
- `GZCTF.csproj` 的 `PublishFrontend` 与 `PublishFleetAgent` 会在主站发布时联动构建前端和 Agent；当前是源码目录分离，不是独立制品和独立发布契约。
- `ApiTokenController` 当前使用 `[RequireAdmin]`，`ApiToken` 未提供 scope/permission 模型；现状不能满足出题人创建个人受限 token 并调用批量导入接口。
- 当前已有 OpenTelemetry metrics、tracing、Redis instrumentation、health check 和 Prometheus endpoint；观测阶段应复用现有底座，不应只规划日志页面。
- `DeploymentQueueService` 已具备 active identity 去重、排队位置、取消、陈旧 Creating 恢复和容量释放；调度重构应围绕统一任务语义扩展，不能另建平行队列。
- 总纲需要明确四条开发主线：架构治理主线、运行控制面主线、TeamLab 主线、产品体验主线。
- Phase 编号必须表达真实优先级：VM 抽象和 TeamLab 商业闭环应早于题目池、练习、培训和 AWDP。
- 临时迁移适配器只能存在于同一交付波次，波次退出前必须删除；禁止形成永久双轨。
- 每个阶段需要统一退出门槛：代码边界、数据迁移、自动测试、真实环境验收、观测接入、运维文档、旧代码删除全部完成。

## 当前写作状态

- 进度记录文件已建立。

- 控制器、服务、实体、Agent、前端路由、测试和遗留模块已完成总纲级审计。
- `docs/platform-commercialization-master-plan.md` 已整体重写。
- 已完成禁用泛化词扫描，当前无命中。
- 已完成文档标题结构检查，当前覆盖审计基线、代码结构、数据库结构、功能链路、技术债、商业化目标、解耦主线、阶段编排、并行边界、开发规范和阶段文档清单。
- 已完成 UTF-8 读取检查，中文显示正常。
- 2026-07-10 追加需求已完成代码事实复核。
- `docs/platform-commercialization-master-plan.md` 已更新：新增整体架构分层，强化前端全局样式层验收，将 TeamLab 拆为 Phase 3 架构底座前置解耦和 Phase 9 商业化闭环。
- 已完成禁用泛化词扫描，当前无命中。
- 已完成 Phase 引用、标题结构和 diff 空白检查。
- 总纲规划流已升级为 Wave A-E、TeamLab 关键路径、阶段准入/退出、技术路线分支、迁移删除规则、需求覆盖矩阵和统一 SLI。

## 2026-07-10 Phase 0、1、3 计划编写

- 用户已确认采用“实质迁移后彻底切换”的路线，不接受仅写规范、永久适配器或长期双轨。
- Phase 0 计划必须处理真实遗留：IR/Scenario 当前无有效 Controller 和前端入口，但实体、DbSet、模型快照和旧 e2e 仍存在；旧 `TrainingDirection/TrainingModule` 仍有完整 Controller、前端 API 和页面，必须迁移数据后删除。
- Phase 1 计划必须交付可执行架构底座：正式 API token authentication scheme、scope/action/resource 权限、外部版本化 API、统一错误、幂等、异步操作、审计、OpenAPI 契约检查和架构依赖门禁。
- Phase 1 必须以镜像模板上传或注册作为真实纵向参考链路，禁止只创建空接口和说明文档；批量题目生命周期留给 Phase 10。
- Phase 3 按“基座与产品分离”设计：Penetration 只保留赛制、目标、计分和提交；TeamLab 独立拥有拓扑、发布版本、计划、runtime 和执行 API。
- Phase 3 交付可供外部平台调用的 TeamLab 控制面；Phase 9 在该控制面上完成多节点、Windows、全流量观测、恢复和商业级容量验收。
- 当前开始编写总纲列出的 Phase 0、1、3 主计划及其配套术语、模块边界和 API 合约文档。
- 已完成 `docs/commercialization/phase-00-baseline-cleanup.md` 和 `domain-glossary.md`：旧培训采用 root module -> course、module -> chapter 的可校验迁移，IR/Scenario 只备份审计后清退，历史 EF migration 保留。
- 已完成 `phase-01-architecture-domain-api-contract.md`、`external-api-standard.md` 和 `module-boundary-map.md`：采用模块化单体、opaque scoped token、正式 authentication scheme、ProblemDetails、幂等、持久化 operation、镜像 API 参考链路和架构/OpenAPI 门禁。
- 已完成 `phase-03-teamlab-foundation-decoupling.md` 和 `teamlab-api-foundation-contract.md`：TeamLab 独立拥有 topology/release/plan/runtime；Penetration 只拥有 objective/submission/score/binding；外部与内部调用共用 application contracts、ApiOperation 和 DeploymentQueueTicket。
- Phase 3 新增关键地址模型：topology 保存 RFC1918 address pool、runtime prefix 和 host offset；实际 CIDR 通过 PostgreSQL `cidr` active lease GiST exclusion constraint 分配，防止不同 runtime 的 Fabric route 冲突。
- 已补充 Phase 0、1、3 的维护窗口、数据库备份、应用/数据库联合回滚和不可逆 contract migration 边界。
- 已将强化后的 Phase 1 与 Phase 3 关键方向同步回 `platform-commercialization-master-plan.md`。
- 最终原子性审计已修正 Phase 0：数据库迁移、旧后端删除和 EF snapshot 必须在同一可构建提交切换；理论多次尝试、阅读百分比和章节完成策略均有目标字段，禁止静默丢失。
- 最终原子性审计已修正 Phase 1：token 领域、认证授权、管理 API 和前端管理入口在同一纵向任务切换；旧 TokenService、Repository、权限旁路和 restore 同步删除，不产生跨任务编译断点。
- Phase 1 operation 已补充 PostgreSQL lease claim、handler registry 和模块 durable job；镜像导入通过 `ImageImportJob` 在主站重启后恢复，通用 operation 表不保存业务 secret。
- Domain/Infrastructure 边界已复核：Domain 不使用 EF Core attribute；主键、索引、`cidr` 和 PostgreSQL exclusion constraint 均归 Infrastructure persistence configuration。
- Phase 3 已补充 `ITeamLabNodeExecutor` 端口和 Agent adapter，application service 不直接依赖 AgentClient；外部 HTTP 与内部 Penetration adapter 共用同一 application service、operation、queue 和 runtime facts。
- Phase 3 reset 语义已冻结为稳定 runtime public ID、递增 generation、旧 grant 撤销、历史 facts 保留；overlay 使用持久化 Data Protection key ring 加密并在注入完成后清除密文。
- Phase 3 runtime entity namespace 搬迁与全部 C# 调用方更新被合并为同一 orchestration 任务，避免 expand migration 后出现不可构建中间状态。
- 总纲交叉引用已修正：旧培训只在 Phase 0 迁移一次；Phase 10 复用 Phase 1 Identity contracts，不重新引用已删除的 TokenService；Phase 3 退出必须删除旧组网双轨和两个超大服务。
- 三份主计划已完成文件路径状态机检查、占位符扫描、Markdown code fence 检查和 `git diff --check`；当前无缺失路径、重复创建、占位符、不平衡代码块或空白错误。
- Phase 0、1、3 计划编写与最终自审完成，等待用户选择执行方式；本轮未实施代码、未提交、未推送、未部署。

## 2026-07-10 Phase 4、5 计划编写

- 已完成 `docs/commercialization/phase-04-database-governance.md` 和 `database-index-and-lifecycle-audit.md`。
- Phase 4 冻结 PostgreSQL 事实边界：首轮只对已确认高频追加的 `Logs` 和 `TeamLabTrafficFlows` 做原生时间分区，其他 operational history 使用复合/partial index 和受限批量清理，不无依据扩大分区范围。
- Phase 4 补齐核心查询索引、Participation/课程进度/理论尝试/镜像引用唯一约束、Theory tag 正式关系、镜像引用 JSON 关系化、时间游标、聚合事实、治理运行记录和 expand-migrate-contract 迁移。
- 自动保留策略只覆盖明确登记的 operational data；Submission、Participation、课程进度、理论答题和 AWDP 比赛事实明确禁止按隐式时间默认值删除。
- 已完成 `docs/commercialization/phase-05-redis-cache-high-frequency-data.md` 和 `cache-invalidation-map.md`。
- Phase 5 将 Redis 用途固定为 cache、lock、lease、stream、SignalR 和可丢失 queue wake-up；DeploymentQueueTicket、ApiOperation、runtime 和全部业务事实继续由 PostgreSQL 持有。
- Phase 5 使用单一异步 Redis connection provider、HybridCache、PostgreSQL projection revision、owner-safe lease、节点 live state、TeamLab flow Stream/batch persistence，删除自研 CacheMaker、散落 cache request channel 和旧 RedisDistributedLock。
- Redis 故障语义已冻结：缓存旁路；部署队列继续 PostgreSQL polling；TeamLab flow 进入有界本机缓冲；节点 metrics 进入批量数据库 fallback；公网端口和 production distributed lock fail closed。
- 已修正跨 Phase 路径：Phase 5 使用 Phase 3 搬迁后的 `Modules/TeamLab/Infrastructure/NodeTunnelService.cs`；部署队列前端使用现有 `pages/admin/queue/Index.tsx`。
- 最终自审已修正镜像引用旧 JSON 类型清退、旧 `IDistributedLockService` 接口删除和节点指标生命周期登记三处跨任务缺口。
- Phase 0、1、3、4、5 的文件路径状态机检查通过；Phase 4/5 主计划与配套文档已通过总纲需求词覆盖、占位符、代码块、UTF-8、尾随空白和 Git whitespace 检查。
- 本轮只编写计划和配套契约，未实施 Phase 4/5 代码、未提交、未推送、未部署。

## 2026-07-11 Phase 3 实施

- 当前分支 `codex/phase-3-teamlab-foundation`，基线 `c83d3135`；Phase 2 由另一协作者并行实施，当前工作树未混入其改动。
- 独立 TeamLab topology/release/plan/runtime 基座、新 runtime 编排链路、Penetration adapter、内外 API、流量采集和前端契约切换已经完成。
- TeamLab application 不直接依赖 AgentClient；节点动作统一通过 `ITeamLabNodeExecutor`，Penetration 只保留 objective、submission、scoreboard、reset policy 和 TeamLab binding。
- 旧 `PenetrationService`、旧 `TeamLabDeploymentService` 及 topology snapshot、environment、runtime node、runtime route 双轨已经从生产源码删除，不保留永久兼容层。
- `TeamLabRuntime` 已删除 GameId、TeamId、WorkerNodeId、PublishedVersion 和 NetworkPrefix；节点管理与部署队列按 binding 投影比赛/队伍显示，独立 TeamLab runtime 不携带比赛语义。
- contract migration 已生成并增加破坏性 DDL 前置校验；reset record 通过 game/team binding 从 environment ID 重映射到 runtime ID，缺失 release/objective/runtime facts 时迁移中止。
- 当前生产项目编译结果为 0 warning / 0 error。下一检查点为新契约测试、PostgreSQL migration 集成测试、OpenAPI、前端 build、acceptance runbook 和一次独立质量审查。

## 2026-07-10 Phase 0 实施

- 已按 `executing-plans` 流程创建隔离工作区 `D:\newgz\newGZCTF-phase0`，分支为 `codex/phase-0-baseline-cleanup`；`main` 保持不变。
- 依赖恢复完成；后端基线单元测试 577 项全部通过，前端 TypeScript strict check 通过。
- 基线后端编译有 17 条既有 nullable warning，Phase 0 不得新增编译告警。
- 代码复核确认 `TrainingCourseController.BuildOverview` 仍读取旧 `TrainingModuleProgresses` 和 `TrainingModules`，该耦合已纳入 Task 2 的唯一目标模型切换范围。
- 当前开始 Task 1：建立遗留 runtime/API 边界失败测试、PostgreSQL 数据前置审计脚本和迁移集成测试骨架。
- 本轮尚未部署，尚未连接或修改生产数据库。
- Task 1 红灯已确认：`LegacySurfaceRemovalTests` 两项均因遗留类型和旧 route root 存在而失败；`LegacyTrainingMigrationTests` 因 Phase 0 migration 尚未注册而失败，失败原因与规格一致。
- 活动代码复核补充 `TimeSlot` 和 `ScoringRule`：两者除 `AppDbContext` 外只被 IR/Scenario 实体引用，已纳入 Task 2 清退范围，避免数据库和 runtime 留下半套 Scenario 基础设施。
- `scripts/migrations/phase-00-legacy-data-audit.sql` 已覆盖旧表行数、可见性冲突、模板绑定缺口、slug 冲突、理论快照映射和核心孤儿关系检查。
- Task 1 完成，开始 Task 2 的目标模型字段、迁移守恒和后端 contract 切换。
- Task 2 已删除旧 IR/Scenario/Training runtime 类型、DbSet、Controller 和 DTO；旧培训不与新课程合并运行，只通过一次性 contract migration 迁入 `TrainingCourse` 聚合。
- migration 集成测试已在 PostgreSQL 16 Testcontainers 上通过，验证父子章节、组报名、实践提交、理论试卷、两次作答、答案快照和 37% 阅读进度守恒，并确认全部旧表删除。
- `TrainingCourseController.BuildOverview`、提交次数限制、学习详情和章节完成判断已切换为课程事实；当前运行时源码扫描不再包含旧实体引用，历史 EF migration 按升级链要求保留。
- 差异审查修正理论 attempt 状态机：读取已提交答卷不再隐式创建重做记录，重做改为显式 API；章节完成策略已进入新课程章节编辑合约。
- EF `has-pending-model-changes` 通过；专项后端测试 7 项通过；当前编译 13 条既有 nullable warning，低于 17 条基线且无新增警告。
- Task 2 全量后端门禁通过：582 项单元测试和 PostgreSQL migration 集成测试全部通过；Task 2 已完成原子提交，下一步进入 Task 3 前端旧培训清理。
- 本轮仍未部署，未连接或修改生产数据库。
- Task 3 已删除旧 `trainingApi`、旧方向/模块/可见性/理论 session DTO、旧管理员培训页、两个旧学员模块页和四个废弃 e2e。
- `StudentGroup` 是课程与用户管理仍在使用的通用能力，已从旧 `trainingAdminApi` 拆到独立 `StudentGroupApi`；没有迁移旧模块管理逻辑，也没有保留失效 route。
- 新课程 UI 已补齐章节完成策略、理论重做和答案显示策略；课程概览字段从旧 `theory*Modules` 术语切换为 assessment 口径。
- Task 3 验证通过：locale JSON、TypeScript strict、后端构建、遗留面测试、活动源码旧培训 route/类型扫描和 `git diff --check` 均通过。
- Task 4 已完成活动文档与术语冻结：乱码 code point gate 零命中，运行时源码、Agent 与活动 e2e 的禁用类型和 `dry-run` 占位零命中。
- 总纲已按代码事实更新为 25 个 Controller、88 个 DbSet；课程培训只保留 `TrainingCourse` 运行聚合，历史 migration 继续作为升级证据。
- `PublicUdpGatewayConfig.Provider` 的无效占位默认值已改为正式 `nftables` provider；历史 Phase 注释改为职责说明，不改变运行逻辑；TeamLab 网关专项测试 38 项通过。
- Task 4 质量复审补强了防回流门禁：文本扫描改为大小写不敏感并覆盖旧 DTO/UI 字段、控制器名和 API 子路由，反射测试只允许当前两个课程 route root，并继续精确禁止 `Stage` 等已删除 runtime 类型。
- Task 5 初次生成幂等 migration 脚本并完成本地验收；全分支终审发现 Random 计划和历史题目快照缺口后，该验收被重新打开。
- PostgreSQL 迁移守恒测试首次被本地缺失 `testcontainers/ryuk:0.14.0` 且 Docker Desktop 直连 Docker Hub 超时阻断；确认测试自身显式 dispose 后，仅对测试进程禁用 Ryuk，未修改项目代码掩盖环境问题。
- 初次后端全量单元测试和前端 locale、strict TypeScript、production build 通过，共转换 4810 个模块。
- EF `has-pending-model-changes` 通过；当前 snapshot 和 production bundle 的旧表、旧类型、旧 API/DTO 字符串门禁均为零命中；构建后工作树没有生成资产漂移。
- Phase 0 开发实施与本地验收完成。生产数据库备份、审计 SQL、恢复演练和 contract migration 应用仍是部署门禁，本轮未部署、未连接或修改生产数据库。
- Phase 0 全分支终审未通过：旧 Random 理论计划不写 `TheoryTrainingPlanQuestions`，当前 contract migration 会因历史 session question 无映射而回滚；历史题目快照也未独立于当前题库保存。
- 已补两个 PostgreSQL 回归用例并确认红灯：有历史随机作答时 migration 抛出 `Phase 0 found an unmapped legacy theory answer snapshot`；无历史作答的随机计划迁移后缺少可冻结的活动题目结构。原手工计划迁移用例继续通过。
- 修复边界已冻结：随机计划冻结为确定性静态试卷；历史 session question 迁为归档 paper question，并将题干、正文、选项、正确答案、分值和顺序快照写入 answer；当前创建、编辑、计分和展示只消费非归档题，历史答卷消费自身快照。
- 终审修复绿灯：Manual 计划、已有 Random session、未开始 Random 计划三项 PostgreSQL migration 用例全部通过；历史答卷展示与活动/归档题隔离专项 7 项通过，EF 无 pending model changes，编译保持 13 条既有 warning。
- Random 与历史快照修复后的首轮幂等脚本为 316732 字节，SHA-256 为 `9B5C0C34AA4F1480C1FA83E06FA00372CB90D8F597E03C460F6175BCED015FD6`；后端全量单元测试 585 项、前端 production build、runtime/bundle/snapshot 遗留面和 `git diff --check` 全部通过。
- 当前总纲事实为 25 个 Controller、88 个 DbSet、`AppDbContext` 1761 行；本轮仍未部署、未连接或修改生产数据库。
- 独立迁移复审发现已发布、未启动的 Random 计划在题库候选为 0 时会迁成可直接通过的空卷；新增 `ContractMigration_RejectsPublishedUnstartedRandomPlanWithoutCandidates` 后先确认迁移未失败的红灯，再在 contract migration 中加入事务内前置校验。
- 修复后 Manual、已有 Random session、正常未启动 Random、已发布 Random 零候选拒绝四项 PostgreSQL 迁移用例全部通过；新幂等脚本为 317440 字节，SHA-256 为 `59A0EB29BFF0C30FBEFC512F175BE8E9F161A9FA0100E83A98644D62A8775BC9`。
- 两位独立 reviewer 均已给出 `APPROVED`：原终审核实 Random/历史快照/显式 retry 三项缺口关闭；迁移质量复审核实零候选校验位置、条件、事务回滚和回归测试正确。Phase 0 未发现剩余阻断或重要问题。

## 2026-07-11 Phase 1 实施进度

- Phase 1 Task 1-8 实现完成；未部署、未连接或修改生产数据库。
- scoped API token 已原子替换旧 token：独立 authentication scheme、scope/resource grant、Redis 配额、撤销和一次性 secret 展示均已落地；旧 `TokenService`、管理员 token 旁路和 restore 入口已删除。
- 外部 `/api/open/v1` 已统一使用 ProblemDetails、Idempotency-Key、持久化 `ApiOperation` 和独立 OpenAPI 文档；镜像 reference/archive 导入不使用 fire-and-forget，服务重启后可恢复。
- `ApiOperation` claim 保留 `FOR UPDATE SKIP LOCKED`；claim、renew、progress、complete、retry/fail 已统一改为 PostgreSQL `CURRENT_TIMESTAMP` 与相对 duration，消除多主机时钟偏差。
- `ImageTemplate` 已成为带创建者的全局资产，课程通过 binding 引用；培训 Docker 导入复用统一应用服务，不保留第二套 archive/reference pipeline。
- 镜像导入状态闭环为 `Importing -> Ready/Error`；零可调度节点、Agent timeout、节点分发失败均形成明确失败，成功重试恢复 `Ready`。
- 模板删除先在 Serializable 事务中持久化 `Deleting` 意图，再清理节点缓存、内部 Docker/VM OCI 主副本和 VM 本地源文件；任一清理失败保留意图、错误和分发事实，后台 reconciler 幂等恢复。
- 模板引用门禁已覆盖 CTF、练习、课程、Penetration 当前拓扑和发布快照；损坏发布快照 fail-closed，避免误删仍可能被部署的模板。
- OpenAPI comparator 已覆盖 security schemes、OAuth flows/scopes 和 root/path/operation security；26 个 breaking 与 11 个 additive 自测通过。
- 当前门禁：生产项目构建 0 warning / 0 error；单元测试 629/629；全量集成测试 220/220；前端 locale、strict TypeScript 和 production build 通过；EF 无 pending model changes；OpenAPI 26 个 breaking 与 11 个 additive comparator 自测、旧快照兼容比较和当前快照验证通过。
- Task 7 的 7 项终审缺口全部关闭：Docker 子进程取消终止进程树；模板删除意图可恢复；失效比赛/课程分发引用自动 reconcile；外部镜像 GET/DELETE 与 `images:delete` scope 落地；Registry 来源拒绝私网、回环和链路本地地址；operation 身份字段建立外键；所有外部 API 请求写入独立脱敏审计表。
- 外部 DELETE 已验证课程引用时返回 `409 application/problem+json` 和稳定 `asset_in_use`，解绑后返回 204；私网 Registry 引用返回 `422 image_reference_forbidden`。
- 最终增量迁移为 `20260711072936_CompletePhaseOneDurabilityAndAudit`；Phase 1 本轮不部署生产环境。
- 总纲复核发现原 Task 1-7 只交付镜像纵向 API，没有交付总纲明确要求的题目单个/批量 API；该缺口已作为 Task 8 关闭。
- 新增 `/api/open/v1/games/{gameId}/challenges` 单题导入、1-100 题原子批量导入、游标分页、详情、单题删除和批量删除；全部写操作返回可恢复 `ApiOperation`，结果包含调用方 `externalId` 到平台题目 ID 的稳定映射。
- 比赛新增 `OwnerId` 事实；教师只能签发自己比赛的具体 grant，管理员才能签发比赛或全局通配 grant。旧比赛没有所有者时由管理员签发具体授权，不做猜测性数据回填。
- 题目删除已闭环活动队列取消、节点执行门排空、Docker/VM/测试环境销毁确认、附件和 Flag 清理、计分缓存刷新及镜像引用重建，避免数据库级联掩盖节点孤儿资源。
- Docker 题目绑定已注册 Ready 的全局 `ImageTemplate`；分发不再使用无主伪模板，节点状态和比赛引用进入 `ImageDistributionRecord`，后台 reconcile 可继续处理失败事实。
- Task 8 数据迁移为 `20260711115423_CompletePhaseOneChallengeApi`，新增比赛所有者字段和持久化 `ChallengeMutationJobs`。
- 完整调用文档为 `docs/commercialization/open-api-v1-guide.md`；机器契约 `docs/commercialization/openapi/open-v1.json` 已由真实运行时 OpenAPI 生成并包含全部新增路径。
- 按本轮约束只执行一次独立静态质量审查；确认的授权越界、运行资源孤儿、operation 终态、Docker 分发事实、附件清理、契约缺失、空值/非法枚举和 VM 模板 N+1 共 8 项问题均已直接修复。EF migration 构建成功，OpenAPI 契约生成专项 1/1 通过；未重复执行全量门禁。

## 2026-07-12 Phase 3 最终收口

- 单次独立质量审查确认的 9 项问题已全部关闭：Destroyed runtime 重建、稳定 runtime owner、active release reset、grant 重签、对象授权、多分片容量、connection 路由隔离、flow 增量游标、capture 幂等和自动完成。
- TeamLab 容量事实按 current generation 的 shard/assets 批量读取；预留、确认、取消、stale recovery 和 reconcile 不再把多分片总量归到入口节点，也不产生按票据 N+1 查询。
- 单节点和多节点 router namespace 均应用完整源/目标网段允许矩阵；未声明 connection 的网段被拒绝，远端 Fabric 只下发允许路径。
- 流量元数据使用 Agent 字节游标增量读取，network cursor 独立持久化；PCAP 使用真实 PID 状态完成，外部创建抓包持久化 Idempotency-Key，dumpcap 文件上限单位已修正。
- 最终门禁：解决方案 0 warning / 0 error；单元测试 476/476；PostgreSQL 16 contract migration 2/2；EF 无 pending model changes；OpenAPI 26 breaking/11 additive comparator、自身快照和兼容比较通过；前端 locale、strict TypeScript、production build 通过。
- 全量集成测试的 TeamLab、迁移和 OpenAPI 均通过；仅发现 2 个 Phase 1 resource-grant 测试夹具在签发前缺少管理员授权事实，修正夹具后对应 2/2 通过。Phase 3 未部署、未连接或修改生产服务器。

## 2026-07-12 Phase 0-3 主线整合

- 将远端 `main` 的 Phase 2 提交 `ba11a8ce` 合入 Phase 3 提交 `efda66e2`，共同基线为 Phase 1 提交 `c83d3135`；Phase 0、1、2、3 由此进入同一提交历史。
- 冲突按最终模块边界解决：保留 Phase 2 的生成 API 隔离、全局 token/theme、公共组件、页面组件化、前端架构与 bundle 门禁；保留 Phase 3 的独立 TeamLab topology/release/runtime 契约和 Penetration adapter。
- Penetration 管理入口继续作为薄路由，Phase 3 实现迁入 `components/topology/penetration`；节点管理保留 Phase 2 的 `NodesPage`、`NodeResourcePanel`、`AddNodeModal` 拆分，并切换到 Phase 3 的 `NodeTeamLabApi`。
- 删除 Phase 2 基于旧 Penetration topology DTO 的无引用 `penetrationTopologyModel.tsx`，未恢复 Phase 3 已清理的旧 UX 契约测试或 compatibility API；对应架构预算同步收紧到当前组件边界。
- 整合验证通过：solution build 0 warning / 0 error，单元测试 476/476，PostgreSQL 全量集成测试 222/222，前端 locale/strict TypeScript/架构扫描/production build/bundle budget 全部通过，EF 无 pending model changes，OpenAPI 26 breaking/11 additive 自测及基线兼容性通过。
- 本次仅完成代码合并与主线同步，未部署、未连接或修改生产服务器。

## 2026-07-12 Phase 4 实施启动

- 实施基线为 `fe58ca95`，`main` 与 `origin/main` 一致且工作树干净。
- 已对照总纲、Phase 4 实施计划、数据库索引与生命周期审计和当前代码重新核实范围；计划目标仍成立，但文件路径按 Phase 2/3 合并后的当前模块结构执行，不恢复旧服务或兼容 DTO。
- Phase 4 按五个大单元推进：模型与生命周期基座、高频数据治理、稳定游标查询、迁移与基准、全量验收与独立质量审查。
- 当前进入大单元 1：模块化 persistence、核心索引与唯一约束、Theory tag 正式关系、镜像引用关系化、retention catalog 和治理运行事实。
- 验证按大单元集中执行，不对每个小改动反复运行全量测试；本阶段不部署、不连接或修改生产服务器。
- Phase 4 大单元 1 已完成：目标 persistence mapping 已迁入 Ctf/Training/Theory/Runtime/Awdp/Audit 模块；Participation、课程进度、理论答题、AWDP、部署队列和镜像分发的唯一约束与主查询索引已进入 EF model。
- Theory tag 已成为规范化实体与关系，提供 trim/空白合并/大写唯一键、标签筛选和管理 API；镜像分发引用已删除 JSON 与 `ReferenceCount` 双事实，改用关系表唯一约束和精确释放。
- retention catalog 显式登记 owner-managed 核心事实和自动治理数据集，配置通过 `ValidateOnStart`；`DataGovernanceRun` 作为后续 worker 的可恢复审计事实。
- 大单元 1 集中验证通过：solution build 0 warning/0 error，数据库边界、retention、Theory tag 和镜像分发专项 17/17。当前进入大单元 2：分区、聚合、保留清理 worker 与治理指标。
- Phase 4 大单元 2 代码完成：Logs 按月、TeamLab raw flow 按日的固定分区定义已建立；flow 写入生成规范化 RFC1918 前缀和 SHA-256 指纹；日志、flow 与部署生命周期聚合使用幂等 upsert。
- 治理 worker 使用 PostgreSQL session advisory lease，固定执行分区准备、闭窗聚合、聚合事实校验门控、过期分区 drop 和终态 `SKIP LOCKED` 限批清理；核心业务事实不在 cleaner 的可达路径中。
- 治理运行写入 `DataGovernanceRun`，错误正文限制 2048 字符；metrics 只使用固定 data set/operation 标签。solution build 0 warning/0 error。
- 分区路由和 worker 恢复的真实 PostgreSQL 集成验收依赖大单元 4 的 expand-migrate-contract schema，已明确合并到该单元集中执行；当前进入大单元 3 游标查询链路。
- Phase 4 大单元 3 已完成：日志使用 `(TimeUtc, Id)`、提交使用 `(SubmitTimeUtc, Id)`、部署队列使用 `(CreatedAt, Guid)`、TeamLab flow 使用 `(CapturedAt, Id)` 稳定游标；非法游标返回明确 `invalid_cursor`。
- 管理端日志、部署队列和比赛提交监控通过 cursor 栈保留上一页/下一页体验，不再请求总数或计算深 OFFSET；大屏数据 hook 已同步消费 `items` 响应。
- 大单元 3 集中验证通过：solution build 0 warning/0 error，游标与队列专项 6/6，前端 strict TypeScript 通过。当前进入大单元 4 schema migration、PostgreSQL 集成验收、查询计划和 runbook。
- Phase 4 大单元 4 已完成：迁移重写为真正的 Expand/Backfill/Contract。Expand 不删除旧事实；Backfill 建立关系事实和时间分区影子表并校验；Contract 在维护锁窗复制增量、验证 count/checksum、原子切换并清理旧 JSON/旧表。Down 明确要求备份/PITR，不提供有损反向压缩。
- PostgreSQL 16 迁移验收 1/1 通过：覆盖重复 Participation 拒绝与修正后恢复、旧镜像 JSON 双引用、长题库迁移 tag、跨月 Logs、跨日 TeamLab flow、分区路由、唯一约束、聚合幂等、advisory lease、终态清理和“无聚合证明不删分区”。
- 查询计划基线已建立 CI/Commercial 两档确定性合成数据；全新数据库 latest migration 后播种 42.5 万条 CI 事实，Submission、Participation、课程进度、Theory tag、部署队列、Logs、TeamLab flow 共 7/7 JSON plan contract 通过，Logs/flow 均只访问命中分区。
- 迁移与治理 runbook 已覆盖并发索引预建、维护窗口、磁盘/WAL、default partition、失败重试、治理指标和 PITR 恢复。当前进入大单元 5 全量门禁和一次独立质量审查；仍未部署、未连接或修改生产服务器。
- Phase 4 大单元 5 已完成。独立质量审查确认的 8 项问题已全部关闭：首次治理只按 retention 起点聚合导致旧分区误删、镜像分发记录创建与最后引用释放竞态、Backfill/Contract 窗口 Theory tag 丢失、非 UTC session 分区边界漂移、日志与部署聚合无限增长、TeamLab/queue 终态清理条件不足、分区审计行数与迁移 checksum 证据不足、CI 查询计划未留存 artifact。
- 分区删除现执行候选分区完整重聚合，日志核对 count，TeamLab flow 核对 flow/packet/byte，总量通过后在分区写锁内复核 source rows、分区级治理证明和 runtime terminal window；删除与 `PartitionName/RowsDeleted` 审计在同一事务提交。
- 镜像模板/节点使用 PostgreSQL advisory transaction lock 串行化记录创建、引用增加和最后引用释放；Contract 锁窗内补跑 Theory tag 增量回填，Backfill/Contract 强制 UTC，迁移 checksum 覆盖完整 JSON row 内容。
- 最终门禁通过：solution build 0 warning/0 error，单元测试 488/488，PostgreSQL 集成测试 223/223，前端 strict TypeScript 通过，EF 无 pending model changes，query-plan contract 7/7，`git diff --check` 通过。
- 真实 PostgreSQL 16 PITR 演练通过：WAL archive/base backup 后执行 Phase 4 Contract，恢复至 `2026-07-12 10:28:44.597477+00`；migration head 回到 Phase 3，升级前标记计数 1、升级后标记计数 0。演练脚本为 `scripts/database/rehearse-pitr.ps1`，临时容器和 volume 已自动清理。
- Phase 4 已闭环完成；本阶段未部署、未连接或修改生产服务器。
