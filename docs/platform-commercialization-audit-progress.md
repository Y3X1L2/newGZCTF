# 商业化总纲审计进度记录

更新时间：2026-07-11

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
