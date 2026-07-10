# 平台商业化升级总体总纲

日期：2026-07-10

依据：`d:\Downloads\平台后续开发需求.md`、当前 `main` 分支源码、CodeGraph 索引、当前数据库模型、当前前端工程、当前 Agent 工程、当前测试工程。

用途：本文件作为后续多人开发的总纲。它定义当前项目结构、数据库结构、运行链路、架构分层、技术债、解耦主线、阶段顺序、并行边界和阶段文档清单。每个阶段必须单独编写设计文档和实施计划。

## 1. 审计基线

- 当前仓库路径：`D:\newgz\newGZCTF-main`。
- 当前分支：`main`。
- CodeGraph 索引：1011 个文件、27262 个代码节点、79249 条关系边。
- 后端主站：ASP.NET Core、EF Core、Identity、SignalR、PostgreSQL、Redis。
- 前端：Vite、React 19、Mantine 9、SWR、vite-plugin-pages、React Router 7、Three.js、ECharts。
- 节点执行面：`GZCTF.Agent`，负责 Docker、KVM、TeamLab 网络、镜像下载、状态上报和维护操作。
- 商业化容量基线：平台侧按 300-500 支队伍高峰在线进行队列、调度、缓存、日志和前端交互设计；真实环境启动容量由 WorkerNode 的 CPU、内存、磁盘、网络、镜像缓存和 KVM 能力决定。
- `GZCTF.csproj` 当前通过 `PublishFrontend` 和 `PublishFleetAgent` 联动构建前端与 Agent；源码目录已经分离，制品和发布流程仍绑定主站。
- `ApiTokenController` 当前使用 `[RequireAdmin]`，token 模型没有 scope/permission；出题人个人受限 token 尚未成立。
- OpenTelemetry metrics、tracing、Redis instrumentation、health check、Prometheus endpoint 已经存在，商业观测应扩展现有底座。

本文使用三类陈述：带代码路径和数量的是当前事实；写入架构章节的是目标决策；写入阶段验收的是退出门槛。三类陈述不得混用。

## 2. 当前代码结构

### 2.1 解决方案结构

| 路径 | 职责 | 当前事实 |
| --- | --- | --- |
| `src/GZCTF` | 主站后端、前端发布宿主、数据库模型、业务服务、HTTP API、SignalR | 发布流程会构建前端并发布 Agent 单文件到 `agent/gzctf-agent`。 |
| `src/GZCTF/ClientApp` | React 前端工程 | 自动文件路由，手写 API 工具与生成 API 类型并存。 |
| `src/GZCTF.Agent` | WorkerNode 执行面 | Docker、KVM、TeamLab 网络、镜像、状态、维护接口集中在 Agent 内。 |
| `src/GZCTF.Test` | 单元测试 | Fleet、TeamLab、VM、镜像、Guacamole、Transfer、TrainingCourseAccessPolicy 已有测试。 |
| `src/GZCTF.Integration.Test` | 集成测试 | 普通 CTF、计分、导入导出、认证、仓储层覆盖扎实。 |
| `src/GZCTF.AppHost` | 本地编排辅助 | 支持本地开发依赖编排。 |
| `tests/e2e` | Playwright e2e | Phase 0 已删除旧 IR/Scenario 与旧拓扑编辑器用例，当前保留提交计分用例。 |

### 2.2 主站后端结构

| 目录 | 职责 | 商业化判断 |
| --- | --- | --- |
| `Controllers` | HTTP API 入口 | 25 个控制器，课程、比赛、节点、题目编辑控制器承载业务逻辑过多。 |
| `Models/Data` | EF Core 实体 | Phase 0 已清除旧 IR/Scenario 与旧培训实体；当前覆盖课程、TeamLab、Penetration、Fleet、VM 和既有比赛域。 |
| `Models/Request` | DTO、请求模型、响应模型 | 外部 API 化前需要统一鉴权、重复调用语义、错误、任务状态、审计字段。 |
| `Services` | 业务服务 | Fleet、TeamLab、VM、Container、Transfer、Cache、AWDP 已有子域，多个服务文件超过 700 行。 |
| `Repositories` | 仓储封装 | 普通 CTF 主链路成熟，自研模块仍有 Controller 直接组织复杂查询。 |
| `Extensions` | 启动注册、缓存、SignalR、遥测、存储、服务注册 | Redis、队列、后台服务、Nginx、端口租约、节点注册集中在启动扩展。 |
| `Migrations` | EF 迁移 | 历史 migration 保留数据库演进事实；当前快照只包含 Phase 0 目标模型。 |
| `Hubs` | SignalR 推送 | 当前主要服务比赛事件、日志、实时状态。 |

当前最大控制器：

- `TrainingCourseAdminController.cs`：2203 行。
- `GameController.cs`：1920 行。
- `EditController.cs`：1252 行。
- `TrainingCourseController.cs`：1191 行。
- `NodesController.cs`：1183 行。
- `AdminController.cs`：952 行。
- `AccountController.cs`：759 行。

当前最大服务：

- `PenetrationService.cs`：3447 行。
- `TeamLabDeploymentService.cs`：2451 行。
- `AgentClient.cs`：1050 行。
- `NodeDeployService.cs`：973 行。
- `DockerManager.cs`：960 行。
- `DockerImageRegistryService.cs`：834 行。
- `FleetContainerManager.cs`：707 行。

### 2.3 Agent 结构

| 文件 | 行数 | 职责 |
| --- | ---: | --- |
| `TeamLabNetworkService.cs` | 1005 | bridge、router namespace、WireGuard、Fabric、抓包清理。 |
| `DockerService.cs` | 920 | Docker 镜像拉取、容器创建、网络、清理。 |
| `KvmService.cs` | 745 | libvirt、qemu-img、virt-install、cloud-init、VM 生命周期。 |
| `ImageController.cs` | 328 | Docker pull、VM 镜像下载、镜像状态。 |
| `TeamLabController.cs` | 103 | Fabric、shard、capture 状态控制入口。 |
| `VmController.cs` | 66 | VM 创建、停止、销毁入口。 |
| `StatusController.cs` | 26 | Agent 状态与能力上报。 |

Agent 当前具备商业化基础，但能力协商和版本协议仍需规范化。Docker 能力、KVM 能力、TeamLab 网络能力、抓包能力必须分开判断；缺 KVM 不得影响 Docker 组网和 Docker 普通部署。

### 2.4 前端结构

| 位置 | 职责 | 当前事实 |
| --- | --- | --- |
| `src/pages` | 文件路由页面 | `vite-plugin-pages` 自动生成路由，复杂业务继续堆在页面文件。 |
| `src/components` | 公共组件 | 145 个文件、24830 行；存在业务组件、展示组件、动效组件、数据 hook 混放。 |
| `src/hooks` | 前端 hooks | 8 个文件、388 行；业务域 hook 不完整。 |
| `src/Api.ts` | 生成 API 客户端 | 当前 6613 行，后端 OpenAPI 变更会直接扩大前端类型影响面。 |
| `src/utils` | 领域 API、缓存、消息、格式化 | `TrainingApi.ts` 961 行，学员组 API 已拆为独立文件。 |
| `src/styles` | 全局样式、CSS module、页面样式 | 54 个文件、20453 行；`YinyuRefinement.css` 9001 行。 |

当前最大页面：

- `training/courses/[courseId]/Index.tsx`：2386 行。
- `admin/games/[id]/Penetration.tsx`：2196 行。
- `Teams.tsx`：1146 行。
- `admin/nodes/Index.tsx`：1112 行。
- `admin/images/Index.tsx`：859 行。
- `admin/games/[id]/challenges/[challengeId]/index.tsx`：858 行。
- `admin/games/[id]/TheoryPaper.tsx`：840 行。

当前最大组件和样式：

- `components/yinyu/YinyuReactBits.tsx`：1520 行。
- `components/ctf-screen/useCTFScreenData.ts`：1070 行。
- `components/reactbits-original/GridScan.tsx`：925 行。
- `components/screen/useScreenData.ts`：881 行。
- `styles/YinyuRefinement.css`：9001 行。
- `styles/YinyuDesignLab.css`：2822 行。
- `styles/YinyuTheme.css`：1960 行。

前端商业化重构必须前置。首页会大改，其他页面的总体布局保持稳定，但视觉语言、组件层、数据请求层、响应式规则必须统一。当前页面、组件和样式中同时存在全局 `yy-*` class、CSS module、inline style、Mantine `classNames`，说明样式控制权分散，无法通过公共组件层低成本切换全局设计语言。

前后端分离的目标不是简单移动目录。前端必须形成独立构建制品、版本化 API 契约、独立质量检查和独立发布流水线；生产部署仍可通过同一域名和反向代理提供服务。

### 2.5 测试结构

| 测试区 | 当前覆盖 |
| --- | --- |
| `src/GZCTF.Test/UnitTests/TeamLab` | TeamLab 部署、命令构建、资产计划、流量抓包、计划服务、分片规划、模型契约。 |
| `src/GZCTF.Test/UnitTests/Fleet` | 节点控制器、部署队列、容量预留、调度器、VM、镜像分发、端口租约。 |
| `src/GZCTF.Test/UnitTests/Transfer` | 导入导出、权限、题目迁移。 |
| `src/GZCTF.Test/UnitTests/Training` | 课程访问策略。 |
| `src/GZCTF.Integration.Test` | 普通比赛、计分、参赛、导入导出、认证、仓储。 |
| `tests/e2e` | 当前仅保留提交计分用例；Phase 0 已删除验证废弃产品概念的用例。 |

后续验收不能只依赖单元测试。商业化验收必须包含真实 Docker、Linux VM、Windows VM、TeamLab 多网段、多节点、镜像预分发、Redis、数据库写入和前端交互。

## 3. 当前数据库结构

`AppDbContext` 当前维护 88 个 `DbSet`，文件共 1745 行。数据库已经覆盖平台全部核心域，但领域边界和生命周期策略需要重构。

### 3.1 实体分组

| 分组 | 当前实体 |
| --- | --- |
| 站点与审计 | `Posts`、`Configs`、`Logs`、`Files`、`Attachments`、`DataProtectionKeys` |
| 用户、队伍、分组 | `Teams`、`TeamJoinRequests`、`StudentGroups`、`StudentGroupMembers`、`StudentGroupManagers`、`UserParticipations` |
| 普通 CTF | `Games`、`Divisions`、`CheatInfo`、`Containers`、`GameEvents`、`Submissions`、`GameNotices`、`FlagContexts`、`Participations`、`GameInstances`、`GameChallenges`、`FirstSolves`、`GamePhases` |
| 练习 | `ExerciseInstances`、`ExerciseChallenges`、`ExerciseDependencies` |
| API 与 token | `ApiTokens` |
| 镜像、节点、部署 | `ImageTemplates`、`ImageDistributionRecords`、`DockerRegistryMigrationTasks`、`DockerRegistryMigrationItems`、`VmInstances`、`WorkerNodes`、`DeploymentTargets`、`DeploymentQueueTickets` |
| 理论题 | `TheoryQuestionBankItems`、`TheoryPapers`、`TheoryPaperQuestions`、`TheoryAnswerSheets`、`TheorySubmissionAnswers` |
| AWDP | `AwdpServices`、`AwdpServiceInstances`、`AwdpRounds`、`AwdpFlags`、`AwdpCheckerTasks`、`AwdpPatchSubmissions`、`AwdpResetRecords`、`AwdpRecoveryRecords` |
| Penetration 编辑与旧运行 | `PenetrationConfigs`、`PenetrationPublishedSnapshots`、`PenetrationNetworks`、`PenetrationNodes`、`PenetrationInterfaces`、`PenetrationEdges`、`PenetrationScoreItems`、`PenetrationTeamEnvironments`、`PenetrationDeploymentEvents`、`PenetrationRuntimeNodes`、`PenetrationRuntimeRoutes`、`PenetrationSubmissions`、`PenetrationResetRecords` |
| 课程培训 | `TrainingCourses`、`TrainingCourseTeachers`、`TrainingCourseEnrollments`、`TrainingCourseChapters`、`TrainingCourseResources`、`TrainingCourseChallenges`、`TrainingCourseChapterChallenges`、`TrainingCourseSubmissions`、`TrainingCourseProgresses`、`TrainingCheckIns`、`TrainingChapterProgresses`、`TrainingCourseTheoryQuestions`、`TrainingCourseChapterTheoryPapers`、`TrainingCourseChapterTheoryQuestions`、`TrainingCourseChapterTheorySheets`、`TrainingCourseChapterTheoryAnswers` |
| TeamLab 运行时 | `TeamLabRuntimes`、`TeamLabRuntimeShards`、`TeamLabRuntimeNetworks`、`TeamLabRuntimeAssets`、`TeamLabVpnPeerRuntimes`、`TeamLabPublicUdpMappings`、`TeamLabEvents`、`TeamLabTrafficFlows`、`TeamLabTrafficCaptureJobs` |

### 3.2 关键实体关系

- `Game` 与 `Team` 通过 `Participation` 建立参赛关系。
- `GameChallenge` 继承 `Challenge`，承载比赛题目配置、Flag、附件、容器和 VM 模板入口。
- `ExerciseChallenge` 继承 `Challenge`，承载常态练习题基础，但正式练习入口尚未闭环。
- `GameInstance` 和 `ExerciseInstance` 继承 `Instance`，承载运行实例关系。
- `ImageDistributionRecord` 以 `ImageTemplateId + WorkerNodeId` 形成节点缓存事实。
- `DeploymentQueueTicket` 通过 active identity 过滤唯一索引避免同一活动对象重复进入创建流程。
- `TeamLabRuntime` 以 `GameId + TeamId` 建立队伍环境唯一事实。
- `TeamLabRuntimeShard` 以 `RuntimeId + WorkerNodeId` 建立多节点分片事实。
- `TeamLabRuntimeNetwork` 以 `RuntimeId + TopologyKey` 建立运行时网段事实。
- `TeamLabPublicUdpMapping` 约束 runtime 和公网 UDP 端口唯一。
- `TheoryQuestionBankItem` 当前以类型和题库名为主要检索入口，tag 尚未进入正式模型。
- `TrainingCourse` 是课程培训唯一运行聚合；旧课程树只存在于历史 migration 和 Phase 0 迁移验证中。

### 3.3 数据库风险

- `AppDbContext` 仍同时承载 CTF、课程、Penetration、TeamLab、AWDP、Fleet 和 VM，单文件配置集中导致迁移审查成本高。
- 历史 migration 包含已删除模型，数据库升级链必须保留，但运行时模型、当前快照和业务 API 不得重新引用这些类型。
- 高频数据表缺少完整生命周期策略：TeamLab 流量、部署队列、节点指标、日志、AWDP 轮次数据必须按数据量设计索引、聚合、保留和归档。
- VM 抽象不足，`EnvironmentType.WindowsVM` 阻碍 Linux SSH、Windows RDP、TeamLab VM 统一建模。

## 4. 当前整体架构分层

商业化升级不采用直接拆微服务的路线。当前最优路径是保留模块化单体和 Agent 执行面，先在代码内建立严格层级，再按模块边界拆文件、拆服务、拆 API。

### 4.1 目标分层

| 层级 | 目标职责 | 当前问题 |
| --- | --- | --- |
| 前端展示层 | 页面容器、路由、布局、公共组件、设计 token、数据 hook | 页面容器内仍混合样式、数据请求、业务计算和弹窗状态。 |
| API 合约层 | Controller、DTO、权限、错误模型、任务 ID、审计字段 | 多个 Controller 承载业务流程和查询组装。 |
| 应用服务层 | 面向用例的事务脚本，组织领域服务、仓储、队列和审计 | 课程、组网、节点、题目编辑入口存在跨域逻辑堆叠。 |
| 领域服务层 | 计分、题目资产、课程、理论、VM、TeamLab 拓扑、AWDP 轮次 | TeamLab 和 Penetration 模型仍强绑定。 |
| 运行编排层 | Fleet 调度、部署队列、容量预留、镜像分发、VM/Docker 生命周期 | TeamLab runtime 未完全纳入统一运行底座。 |
| 数据缓存层 | EF Core 事实表、Redis 缓存、分布式锁、高频缓冲、归档 | 高频数据、缓存失效、表生命周期需要单独治理。 |
| Agent 执行层 | Docker、KVM、WireGuard、bridge、router、抓包、镜像缓存 | Agent 能力协议要从版本判断升级为能力表。 |
| 观测审计层 | 系统日志、部署队列、TeamLab 事件、镜像分发记录、故障恢复 | 事件覆盖和对象可读性不足。 |

### 4.2 模块边界

| 模块 | 拥有对象 | 禁止穿透 |
| --- | --- | --- |
| CTF 比赛 | Game、Participation、Submission、GameChallenge、Scoreboard | 不直接管理节点命令和 Agent 调用。 |
| 内容资产 | QuestionPool、ImageTemplate、Attachment、FlagContext、导入导出任务 | 不直接写比赛提交和课程进度。 |
| 练习 | ExerciseChallenge、ExerciseInstance、练习进度 | 不复用比赛参赛关系表达练习状态。 |
| 培训课程 | TrainingCourse、章节、资源、报名、课程题、学习进度 | 不恢复已删除的旧课程聚合。 |
| 理论题 | 题库、tag、试卷、答题卡、答案 | 不把 tag 塞进题库名。 |
| VM | VmInstance、VM 模板、OS 类型、访问协议、访问端点 | 不用 `WindowsVM` 表达全部 VM。 |
| Fleet 运行底座 | WorkerNode、DeploymentQueueTicket、ImageDistributionRecord、容量预留 | 不把 TeamLab 特例绕过队列、日志和镜像分发。 |
| TeamLab | 拓扑、发布版本、runtime、shard、network、asset、traffic、capture | 不继续以 Penetration 编辑实体作为唯一运行输入。 |
| AWDP | 服务、实例、轮次、Flag、Checker、Patch、重置、恢复 | 不把 3D 态势混入普通 CTF 或培训。 |
| 审计运维 | Logs、队列视图、运行事件、节点状态、恢复任务 | 不只展示内部 ID。 |

### 4.3 架构验收原则

- 新模块必须先写清所属层级、拥有对象、外部接口、依赖服务和事件输出。
- 新代码不能把业务流程继续堆进 Controller、页面文件或 Agent 命令拼接处。
- 新页面不能绕过公共组件层直接写视觉样式。
- 新运行能力不能绕过部署队列、镜像分发、容量预留、日志和恢复事实。
- 新数据库表必须有查询路径、索引、保留周期和清理策略。
- 前端、主站、Agent 必须形成独立制品和兼容矩阵，禁止依赖同一次本地构建掩盖接口破坏。

## 5. 当前功能链路

### 5.1 普通 CTF 链路

管理员在 `EditController` 和 `GameController` 管理比赛、题目、Flag、附件、容器、VM、阶段和计分。选手通过比赛页面加入队伍、开启环境、提交 Flag。普通 CTF 主链路来自开源基础，功能成熟，后续原则是稳定接口、补审计、补性能，不推倒重写。

### 5.2 环境部署链路

普通环境部署链路为：前端发起创建 -> 控制器创建 `DeploymentQueueTicket` -> `QueueManager` 取队列 -> `FleetCapacityReservationService` 做容量预留 -> `WeightedScheduler` 选择节点 -> `DeploymentExecutionService` 执行 -> `FleetContainerManager` 或 `FleetVmService` 调 Agent -> Agent 执行 Docker 或 KVM -> 状态写回队列和运行实例。

当前基础具备 active identity 去重、排队位置、取消、陈旧 Creating 恢复、并行执行、节点执行 gate、Redis 分布式锁、current + reserved 预留。后续应扩展这套统一任务模型，不另建平行队列；缺口是阶段可读性、能力协商、TeamLab 纳入统一运行底座、镜像预分发和跨节点失败恢复。

### 5.3 镜像与存储链路

Docker 模板以 `10.24.0.28:5000` 作为 Registry 主副本，Agent 创建容器时支持缺失自动 pull。VM 模板已引入 Registry artifact 和节点下载链路，`ImageDistributionRecord` 记录节点缓存事实。商业化闭环还需要在比赛、题目、课程引用阶段预分发，在比赛结束和引用释放后清理节点缓存，启动阶段只做校验和兜底拉取。

### 5.4 VM 链路

主站通过 `FleetVmService` 和 `AgentClient` 调用 Agent `KvmService`。Agent 使用 libvirt、qemu-img、virt-install 和 cloud-init seed ISO 创建 Linux/Windows VM。Windows 主要走 Guacamole RDP，Linux SSH 还没有成为正式访问协议。VM 生命周期必须统一为镜像准备、创建、启动探测、访问端点、延期、停止、销毁、清理。

### 5.5 TeamLab 组网链路

当前组网从 `PenetrationConfig` 编辑和发布拓扑读取，`TeamLabDeploymentService` 负责 runtime、shard、network、asset、WireGuard、Fabric、路由、镜像准备、部署、事件和清理。数据模型已经支持 runtime 分片和流量采集，但服务和前端页面过大，仍需要拆出可复用 API 底座。组网目标是 WireGuard 玩家入口、L3 Fabric、多网段、Docker/VM 混编、多节点部署、流量元数据和按需 PCAP。

代码事实显示：`TeamLabPublishedTopologyService`、`TeamLabAssetPlanService`、`TeamLabPlanService`、`TeamLabDeploymentService` 仍直接使用 `PenetrationConfig`、`PenetrationNode`、`PenetrationNetwork`、`PenetrationEdge`。因此 TeamLab 必须前置做架构底座解耦，不能拖到后期才处理。

### 5.6 培训与理论链路

课程体系覆盖课程、教师、报名、章节、资源、课程题、章节题、提交、进度、签到和章节理论测试。Phase 0 已删除旧培训 Controller、页面、实体、路由和枚举；理论题已有题库、试卷、答题卡和提交答案，但 tag 模型和索引需要成为正式结构。

### 5.7 AWDP 链路

AWDP 已有服务、服务实例、轮次、Flag、Checker、Patch、重置、恢复和计分数据。3D 大屏态势感知应绑定真实攻击和修复事件：红色流线表达攻击，服务或柱体同色发光表达修复。该能力只在 AWDP 模式启用。

### 5.8 日志与审计链路

当前系统日志、部署队列、TeamLab 事件、镜像分发记录均存在。商业化缺口是覆盖面、对象可读性、错误分层和恢复事实不足。部署队列必须显示队伍或用户、比赛、题目、模板、节点名称、镜像类型、操作阶段和失败原因。

### 5.9 外部 API 与 token 链路

当前 token 由 `ApiTokenController` 创建、撤销和恢复，控制器整体要求管理员权限。`TokenService` 只校验 token 有效期、撤销状态和签名，尚未表达资源范围、动作权限、创建人边界和调用配额。容器上传、题目批量导入、题目创建和销毁要开放给出题人，必须先建立 scoped token、API 版本、重复调用键、异步任务状态、速率限制和审计链路。

## 6. 核心技术债

| 技术债 | 证据 | 影响 | 处理阶段 |
| --- | --- | --- | --- |
| 整体架构边界不稳 | Controller、服务、页面、Agent 均有跨层职责 | 多人开发容易互相覆盖和重复建模 | Phase 1 |
| 练习入口缺失 | `ExerciseController.cs` 只有空壳，模型已有 | 首页和常态训练无法闭环 | Phase 11 |
| 前端样式控制权分散 | 全局 `yy-*`、CSS module、inline style、Mantine `classNames` 并存 | 全局设计语言无法低成本切换 | Phase 2 |
| 前端大页面堆叠 | 多个页面超过 700 行，课程详情和组网页面超过 2000 行 | 风格重构和功能扩展成本高 | Phase 2 |
| TeamLab 与 Penetration 耦合 | TeamLab 计划、资产、路由、发布仍使用 Penetration 实体 | 组网底座难以 API 化和商业化复用 | Phase 3 |
| 数据库生命周期不足 | 高频表缺少保留、归档、分区和聚合策略 | 长期运营后查询和写入压力不可控 | Phase 4 |
| 缓存策略分散 | Redis 覆盖缓存、锁、SignalR、端口分配，高频事件策略未统一 | 高并发下状态漂移和数据库压力风险高 | Phase 5 |
| 调度与能力协商不足 | 仍有协议硬编码和能力耦合缺陷 | Docker、KVM、TeamLab 多节点调度不够稳 | Phase 6 |
| 日志可读性不足 | 队列和日志仍出现内部 ID、泛化错误 | 运维排障成本高 | Phase 7 |
| VM 抽象不足 | `WindowsVM` 承载 VM 语义 | Linux SSH 和 Windows RDP 无法统一 | Phase 8 |
| API token 权限过粗 | token 管理要求管理员，token 没有 scope/permission | 出题人 API 无法按最小权限开放 | Phase 1、Phase 10 |
| 前后端制品耦合 | 主站 csproj 联动构建前端 | API 破坏、前端回滚和独立发布边界不清 | Phase 2 |

## 7. 商业化目标

- 平台能力目标：比赛、练习、培训、理论、AWDP、TeamLab、Docker、Windows VM、Linux VM 均形成完整选手端、管理端、运维端闭环。
- 性能目标：平台调度、数据库、Redis、前端、日志系统不成为 300-500 支队伍高峰在线的瓶颈。
- 可靠性目标：节点离线、镜像缺失、Agent 重启、平台重启、部署失败后能定位、恢复、回滚和重试。
- 可审计目标：权限、配置、课程、题目、模板、节点、队列、环境生命周期、TeamLab 事件、AWDP 事件均有审计记录。
- 可维护目标：新增模块必须有明确领域边界、数据库生命周期、API 契约、前端组件边界、测试边界和运维说明。
- 商业交付目标：安装、升级、节点注册、Agent 同步、镜像分发、备份恢复、日志检索、压测验收均有手册。

### 7.1 统一 SLI 与容量模型

| 维度 | 必须测量的指标 |
| --- | --- |
| API 控制面 | 请求量、p50/p95/p99 延迟、错误率、限流率、权限拒绝率。 |
| 部署队列 | 入队延迟、排队时长、创建时长、失败率、取消率、陈旧任务恢复数。 |
| 调度节点 | 可用槽位、预留槽位、实际占用、节点利用率、过载次数、调度偏斜。 |
| 数据库与 Redis | 关键查询延迟、锁时长、缓存命中率、批写延迟、连接池占用、归档耗时。 |
| 前端 | bundle 体积、LCP、INP、长任务、内存占用、列表滚动帧率、3D 场景帧率。 |
| TeamLab | 计划成功率、分片部署成功率、路由应用成功率、WireGuard 握手成功率、流量采集覆盖率、销毁残留数。 |
| 恢复能力 | 服务恢复时间、数据恢复点、节点掉线恢复时长、失败任务回收时长。 |

每个阶段设计文档必须先记录当前基准，再冻结退出阈值。Phase 14 按 300-500 支队伍在线、所有可调度槽位并发使用、集中提交和集中环境启动四类负载执行验收。环境数量按节点能力缩放，控制面不得先于 WorkerNode 达到瓶颈。

## 8. 解耦主线

### 8.1 整体架构解耦

展示层、API 合约层、应用服务层、领域服务层、运行编排层、数据缓存层、Agent 执行层、观测审计层必须分层。新增模块必须先定义层级归属和拥有对象，再进入代码实现。当前不直接拆微服务，先把模块化单体内部边界做清楚。

### 8.2 内容资产解耦

题目池、比赛题、练习题、课程题、理论题、环境模板、镜像、附件、Flag 必须分清主从关系。环境模板是全局运行资产，课程题和练习题是业务域绑定。课程删除只删除课程域绑定和课程域快照，不删除全局模板和镜像主副本。

### 8.3 运行底座解耦

Docker、KVM、VM 访问、TeamLab 网络、镜像分发、部署队列、节点能力、Agent 协议必须分层。主服务负责计划、状态、审计和调度；Agent 负责本机可重复调用执行。Docker 和 KVM 能力独立判断。

### 8.4 数据与缓存解耦

PostgreSQL 承载强一致业务事实。Redis 承载缓存、分布式锁、短期高频缓冲、SignalR 扩展和队列辅助。流量元数据、节点指标、部署阶段事件必须走聚合、批写、保留和清理策略。

### 8.5 前端工程解耦

页面容器、业务组件、展示组件、API hook、类型适配、样式 token 必须拆开。全局设计语言只允许通过设计 token、Mantine theme、公共组件 props、组件级样式模块、受控 CSS 变量切换。页面文件不得新增视觉样式规则。前端重写必须遵循 react-best-practices：并行请求、SWR 去重缓存、动态导入重组件、延迟渲染非当前 tab、稳定 callback、稳定依赖、memo、列表虚拟化或局部滚动、非紧急更新使用 transition 或 deferred value、禁止在 render 内定义组件。

### 8.6 TeamLab 底座解耦

TeamLab 要从“渗透编排页面能力”升级为平台组网 API 底座。拓扑校验、发布、计划、部署、销毁、重置、状态、流量、PCAP 必须有稳定接口。第一步先把 TeamLab 拓扑模型、发布模型、运行模型从 Penetration 编辑实体中抽离；第二步再做多节点、VM 混编、流量观测和商业化验收。旧攻击图、迷雾、公网目标、端口级 ACL 不进入当前商业化主线。

### 8.7 审计解耦

系统日志记录治理动作。部署队列记录环境生命周期。TeamLab 事件记录组网内部过程。镜像分发记录节点缓存事实。用户审计记录权限与账号动作。所有审计对象必须显示可读名称。

## 9. 阶段编排

### 9.1 关键路径

TeamLab 商业化是最高紧迫主线，关键路径固定为：

`Phase 0 基线清理 → Phase 1 架构/API 契约 → Phase 3 TeamLab 解耦 → Phase 4 数据治理 → Phase 5 Redis 治理 → Phase 6 调度运行底座 → Phase 7 观测恢复 → Phase 8 VM 抽象 → Phase 9 TeamLab 商业闭环 → Phase 14 商业验收`

前端基础、内容资产、练习、培训和 AWDP 沿并行产品线推进，不得阻塞 TeamLab 关键路径，也不得绕过共享底座。

### 9.2 交付波次

| 波次 | 阶段 | 交付结果 |
| --- | --- | --- |
| Wave A：边界冻结 | Phase 0-3 | 删除失效概念，冻结模块边界、API 契约、前端承载层和 TeamLab 独立模型。 |
| Wave B：控制面强化 | Phase 4-7 | 完成数据库、Redis、调度队列、观测恢复四个商业运行底座。 |
| Wave C：核心能力闭环 | Phase 8-10 | 完成 VM 统一、TeamLab 商业闭环、内容资产与出题 API。 |
| Wave D：产品体验闭环 | Phase 11-13 | 完成首页练习、培训理论、AWDP 3D。 |
| Wave E：交付验收 | Phase 14 | 完成容量、故障、安全、升级、回滚和运维交付。 |

Phase 编号表示依赖顺序，不表示所有团队串行开发。满足前置依赖和阶段准入后即可并行推进。

### 9.3 阶段准入与退出

阶段准入必须同时满足：上游契约冻结、数据迁移方案可执行、测试环境可用、责任代码范围明确、回滚路径明确。

阶段退出必须同时满足：

- 目标代码边界已经生效，禁止穿透规则有静态检查或测试保护。
- 数据迁移完成，旧表、旧字段、旧路由、旧服务或临时适配器已经删除。
- 单元测试、集成测试、契约测试和该阶段真实环境验收通过。
- 日志、metrics、tracing、健康检查和失败状态已经接入。
- 运维手册、故障定位、回滚步骤和数据恢复步骤可执行。
- 文档与当前代码一致，禁止留下未完成标记和永久双轨。

### Phase 0：基线清理与术语冻结

紧迫性：最高。

状态：实现完成，等待 Phase 0 总体验收和分支集成。

前置依赖：无。

范围：

- 清理旧 IR/Scenario 独立系统入口、实体引用、前端路由、e2e 文件和旧概念文案。
- 明确 IR 只是普通 CTF 题目方向。
- 原子迁移旧培训课程、章节、实践提交、理论尝试和进度到新课程体系，并删除旧运行时代码和表。
- 清理无效兼容壳、乱码文案、历史 phase 文案、废弃页面。
- 冻结核心术语：Challenge、Exercise、TrainingCourse、QuestionPool、ImageTemplate、RuntimeAsset、VmAccessEndpoint、TeamLabRuntime。

代码范围：`Models/Data/IREntities.cs`、`Models/Data/ScenarioEntities.cs`、`Models/Data/Training.cs`、`AppDbContext.cs`、`TrainingController.cs`、`TrainingAdminController.cs`、旧 e2e 文件、旧前端训练页面。

交付文档：`docs/commercialization/phase-00-baseline-cleanup.md`、`docs/commercialization/domain-glossary.md`。

验收重点：旧独立 IR/Scenario 和旧培训不再存在于当前 runtime assembly、EF model、数据库表、API、前端路由和 e2e；迁移数据通过数量与引用校验；active docs 只保留当前有效文档。

### Phase 1：整体架构分层、领域模型与 API 合约

紧迫性：最高。

前置依赖：Phase 0 术语冻结。

范围：

- 定义平台分层架构、模块拥有对象、跨层调用规则和禁止穿透规则。
- 定义内容资产、运行资产、题目池、课程题、练习题、比赛题、环境模板、理论题 tag 的领域模型。
- 定义外部 API 标准：鉴权 token、重复调用键、异步任务 ID、错误模型、审计字段、版本策略。
- 定义 scoped token：创建人、资源范围、动作权限、过期时间、调用配额、撤销、审计。
- 用正式 ASP.NET Core authentication/authorization scheme 替换“有效 token 即可旁路权限”的现有逻辑。
- 以镜像模板注册、上传、异步导入和状态查询作为真实外部 API 纵向参考链路，禁止只建立空契约。
- 建立模块依赖测试和 OpenAPI 兼容门禁，后续阶段不能新增跨模块穿透。
- 收敛 Controller 职责，复杂业务进入应用服务和领域服务。
- 统一课程、练习、比赛之间的引用、克隆、快照和删除规则。

代码范围：`Models/Request`、`Controllers`、`Services`、`Services/Transfer`、`ImageTemplateController`、`ApiTokenController`、题目编辑链路。

交付文档：`docs/commercialization/phase-01-architecture-domain-api-contract.md`、`docs/commercialization/external-api-standard.md`、`docs/commercialization/module-boundary-map.md`。

验收重点：每个模块都有拥有对象、服务边界、API 边界、事件输出和禁止穿透规则；管理员、教师、出题人 token 权限互不越界；API 契约可以支撑批量导入题目、上传容器、创建题目、销毁题目、查询任务状态。

### Phase 2：前端工程底座、全局样式层与视觉语言基础

紧迫性：最高。

前置依赖：Phase 1 的 API 类型边界初版。

范围：

- 建立页面容器、业务组件、展示组件、API hook、类型适配层、样式 token、Mantine theme。
- 统一表格、排行榜、卡片、进度、状态、部署阶段、日志、筛选、空态、图表容器。
- 建立全局样式层：设计 token、语义色、间距、字号、阴影、边框、动效时长、布局密度。
- 清理页面内散落样式入口，禁止页面文件新增视觉 class、inline 视觉样式和私有全局选择器。
- 拆分前端构建制品和主站构建制品，前端只依赖版本化 OpenAPI 契约；生产环境可继续同域部署。
- 建立前端独立检查、构建、回滚和制品版本流程。
- 删除“页面宽度不足”阻断逻辑，改成响应式、横向滚动、分区折叠、只读降级。
- 制定新视觉语言。首页大改，其他页面布局保持稳定。
- 拆分课程详情、组网编排、节点管理三个高风险页面的组件边界。

代码范围：`ClientApp/src/pages`、`ClientApp/src/components`、`ClientApp/src/hooks`、`ClientApp/src/utils`、`ClientApp/src/styles`。

交付文档：`docs/commercialization/phase-02-frontend-foundation.md`、`docs/commercialization/frontend-component-boundary.md`、`docs/commercialization/frontend-style-token-contract.md`。

验收重点：设计语言变更只需要修改 token、theme、公共组件和组件级样式模块；前端可以脱离主站源码独立构建和回滚；新增首页、练习、培训、节点、TeamLab 页面不复制大页面状态；前端性能遵循 react-best-practices。

### Phase 3：TeamLab 组网架构底座前置解耦

紧迫性：最高。

前置依赖：Phase 1 的模块边界和 API 合约。

范围：

- 从 `PenetrationConfig`、`PenetrationNode`、`PenetrationNetwork`、`PenetrationEdge` 中抽离 TeamLab 拓扑输入模型。
- 建立可供外部平台调用的 TeamLab topology、不可变 release、计划、runtime、shard、network、asset 和 operation API 底座。
- 拓扑网络保存地址池、runtime 前缀和主机偏移；实际 CIDR/IP 通过数据库唯一 lease 分配并只写入 runtime facts。
- Penetration 只保留赛制、目标、Flag、提交和计分，通过 binding 和 application contract 调用 TeamLab。
- 把 `PenetrationService` 中的运行计划、运行摘要、路由推导、TeamLab 兼容状态迁出。
- 把 `TeamLabDeploymentService` 拆成发布读取、资产计划、分片计划、部署执行、路由应用、事实记录、清理恢复服务。
- 管理端组网页面先拆清任务区和 API 边界，不在本阶段重写全部交互。
- 保持当前可用部署链路，但禁止新增 Penetration 编辑实体到 TeamLab runtime 的强绑定。
- 若需要迁移适配器，只允许存在于 Wave A；Wave A 退出前必须删除。
- Phase 9 只能扩展多节点、Windows、流量、恢复和容量 SLI，不能重新定义 Phase 3 已冻结的 TeamLab 资源身份和 API 语义。

代码范围：`PenetrationService.cs`、`TeamLabDeploymentService.cs`、`TeamLabPublishedTopologyService.cs`、`TeamLabAssetPlanService.cs`、`TeamLabPlanService.cs`、`Penetration.tsx`、`PenetrationApi.ts`、TeamLab 控制器。

交付文档：`docs/commercialization/phase-03-teamlab-foundation-decoupling.md`、`docs/commercialization/teamlab-api-foundation-contract.md`。

验收重点：外部调用可在没有 Game/Team/Penetration DTO 的情况下完成 topology、release、plan、Docker/Linux runtime、access、traffic 和 destroy；Penetration 只通过 binding/application contract 调用；旧组网表、兼容同步、`PenetrationService` 和 `TeamLabDeploymentService` 已删除。

### Phase 4：数据库模型、索引与生命周期治理

紧迫性：最高。

前置依赖：Phase 1 领域模型；Phase 3 TeamLab 运行模型初版。

范围：

- 审查核心表：Submission、Participation、GameChallenge、ExerciseChallenge、TrainingCourse、Theory、WorkerNode、DeploymentQueueTicket、ImageDistributionRecord、TeamLabTrafficFlow、AWDP。
- 为排行榜、提交查询、队伍状态、课程进度、理论题检索、节点队列、流量查询制定索引。
- 为大表定义保留周期、归档策略、清理任务、分区策略。
- 所有新增表必须写明预计数据量、读写频率、主查询路径、唯一约束和删除策略。

代码范围：`Models/Data`、`AppDbContext.cs`、`Migrations`、仓储层、核心查询服务。

交付文档：`docs/commercialization/phase-04-database-governance.md`、`docs/commercialization/database-index-and-lifecycle-audit.md`。

验收重点：数据库结构支撑长期运营；旧表清理有迁移脚本；高频表有保留和归档策略。

### Phase 5：Redis、缓存与高频写入治理

紧迫性：最高。

前置依赖：Phase 4 的数据生命周期。

范围：

- 梳理排行榜、配置、节点状态、部署队列、SignalR、流量元数据、理论统计、培训统计的缓存策略。
- 统一 Redis 用途：缓存、分布式锁、短期高频缓冲、队列辅助、SignalR backplane。
- 定义失效策略：提交、队伍改名、课程更新、比赛状态、节点心跳、镜像分发、AWDP 轮次。
- 高频流量和节点指标走 Redis 缓冲、批量落库、聚合查询。

代码范围：`Services/Cache`、`Extensions/Startup/AppBuilderExtensions.cs`、`QueueManager`、`PortAllocationService`、TeamLab 流量服务、AWDP 轮次服务。

交付文档：`docs/commercialization/phase-05-redis-cache-high-frequency-data.md`、`docs/commercialization/cache-invalidation-map.md`。

验收重点：缓存不会掩盖事实表；Redis 不可用时有明确降级策略；高频数据不会逐条阻塞主业务。

### Phase 6：高并发调度、队列与节点运行底座

紧迫性：最高。

前置依赖：Phase 1、Phase 4、Phase 5。

范围：

- 重审调度算法：能力过滤、容量预留、公平排队、节点过载保护、队伍级上限、个人级上限。
- 将协议硬编码升级为能力协商。
- Docker、KVM、TeamLab 网络能力独立判断。
- 镜像预分发在比赛、题目、课程引用阶段触发，启动时只做校验和兜底。
- 部署队列阶段可读化：镜像准备、拉取、校验、容器创建、VM 创建、启动探测、入口开放、延期、停止、销毁、失败回滚。
- TeamLab runtime 进入统一队列、容量预留、镜像分发和节点执行限制。

代码范围：`Services/Fleet`、`NodesController.cs`、`AgentClient.cs`、`NodeDeployService.cs`、`ImageDistributionService.cs`、`DeploymentQueueViewService.cs`、Agent 状态接口。

交付文档：`docs/commercialization/phase-06-runtime-scheduling-concurrency.md`、`docs/commercialization/agent-capability-protocol.md`。

验收重点：多队伍并发启动不会压垮同一节点；资源紧张时队列提示清晰；缺 KVM 节点仍可承载 Docker；节点同步快且可追踪。

### Phase 7：可观测、审计与恢复

紧迫性：高。

前置依赖：Phase 6 的队列阶段。

范围：

- 全模块接入系统日志、部署队列、TeamLab 事件、镜像分发记录。
- 复用现有 OpenTelemetry，补齐业务 metrics、跨服务 tracing、队列 span、Agent 调用 span 和节点健康指标。
- 日志对象显示可读名称：用户、队伍、比赛、课程、题目、模板、节点、镜像。
- 错误模型分层：权限、调度、镜像、节点、Agent、Docker、KVM、网络、应用健康。
- 服务重启后从数据库和节点事实恢复环境状态。
- 管理端排障视图联动队列、节点、镜像、TeamLab、VM、容器和系统日志。

代码范围：`AdminController` 日志接口、`DeploymentQueueViewService`、TeamLab 事件、镜像分发、节点管理、前端日志和队列页面。

交付文档：`docs/commercialization/phase-07-observability-audit-recovery.md`、`docs/commercialization/event-taxonomy.md`。

验收重点：常见失败不需要 SSH 服务器才能定位；部署、销毁、延期、重置和分发全链路可审计；日志、metrics、tracing 能通过同一 correlation id 串联。

### Phase 8：VM 抽象统一与 Linux SSH

紧迫性：高。

前置依赖：Phase 1、Phase 6。

范围：

- 将环境类型升级为 Docker 与 VM 两级模型，VM 下挂 OS 类型和访问协议。
- Linux VM 提供 SSH/WebSSH 入口，Windows VM 提供 RDP/Guacamole 入口。
- Linux VM、Windows VM 使用统一生命周期、统一部署队列、统一审计。
- cloud-init 和 Cloudbase-init 只注入运行时配置，业务服务骨架由镜像维护。

代码范围：`EnvironmentType`、`VmInstance`、`FleetVmService`、`GuacamoleService`、`KvmProvider`、Agent `KvmService`、VM 前端入口。

交付文档：`docs/commercialization/phase-08-vm-access-abstraction-linux-ssh.md`。

验收重点：Linux SSH 与 Windows RDP 同级展示；VM 创建失败能定位镜像、KVM、网络、登录协议阶段。

### Phase 9：TeamLab 组网商业化闭环

紧迫性：最高。

前置依赖：Phase 3、Phase 6、Phase 7、Phase 8。

范围：

- 基于 Phase 3 的 TeamLab API 底座完成多节点 L3 Fabric、混合 RFC1918 网段、WireGuard 入网、跨节点路由。
- Docker、Linux VM、Windows VM 作为拓扑资产接入。
- 元数据默认轻量采集，PCAP 按需开启并设置时间、大小、保留上限。
- 管理端编排页面聚焦拓扑设计、资产配置、连通关系、发布运行、观测排障。
- 完成 Windows VM 组网验收、内网节点间流量观测、重置销毁残留检查。

代码范围：TeamLab 服务族、Agent `TeamLabNetworkService.cs`、Agent `DockerService.cs`、Agent `KvmService.cs`、TeamLab 管理端和选手端页面。

交付文档：`docs/commercialization/phase-09-teamlab-networking-commercialization.md`。

验收重点：TeamLab 可作为底座 API 调用；多节点 Docker 和 VM 混编可用；Windows VM 纳入组网验收；内网节点间流量可观测。

### Phase 10：内容资产、题目池与容器上传 API

紧迫性：高。

前置依赖：Phase 1、Phase 4、Phase 6。

范围：

- 建立题目池，支持从环境模板导入题目。
- 比赛题、练习题、课程题支持引用或快照。
- 容器上传、Docker 模板注册、题目批量创建、销毁、状态查询形成正式 API。
- 出题人使用个人 scoped token，权限限定到模板、题目和任务动作。
- 理论题 tag 进入正式模型，支持索引和筛选。

代码范围：题目编辑链路、Content image application contracts、Phase 1 scoped Identity contracts、`Transfer` 服务、理论题服务、课程题绑定服务。

交付文档：`docs/commercialization/phase-10-content-assets-question-pool-api.md`。

验收重点：外部出题工具可通过个人受限 token 完成批量导入；越权动作被拒绝并写入审计；课程删除不影响平台模板；理论题 tag 查询具备索引。

### Phase 11：首页重构与练习模块

紧迫性：高。

前置依赖：Phase 2、Phase 10。

范围：

- 首页从比赛列表中心转为平台导航和介绍中心。
- 首页承接比赛、练习、培训、公告、平台能力入口。
- 练习成为一级模块，支持分类、tag、难度、题目列表、环境启动、提交、进度、统计。
- 练习题复用内容资产和运行底座，成绩、进度、实例与比赛和课程隔离。
- 首页动画有性能预算，不能压低首屏交互性能。

代码范围：`ClientApp/src/pages/Index.tsx`、新练习页面、`ExerciseController`、`ExerciseChallenge`、`ExerciseInstance`、公共题目组件。

交付文档：`docs/commercialization/phase-11-homepage-exercise-module.md`。

验收重点：首页定位清晰；练习全链路可用；前端复用 Phase 2 组件，不新增超大页面。

### Phase 12：培训与理论产品化

紧迫性：中高。

前置依赖：Phase 2、Phase 10。

范围：

- 在 Phase 0 已完成的数据迁移和唯一课程模型上推进产品能力，不恢复已删除的旧课程聚合。
- 课程审核、免审、教师、学员、课程删除、归档隐藏、学习状态、理论测试得分闭环。
- 培训题目接入题目池和内容资产隔离规则。
- 理论题 tag、索引、题库筛选和章节理论测试复用统一理论模型。
- 培训详情页按前端组件边界拆分，重组件按需加载，列表和 Drawer 不截断。

代码范围：`TrainingCourseAdminController.cs`、`TrainingCourseController.cs`、课程前端、理论题前端。

交付文档：`docs/commercialization/phase-12-training-theory-productization.md`。

验收重点：课程管理符合教师和管理员权限；课程题和理论测试数据口径统一；不得恢复 Phase 0 已删除入口。

### Phase 13：AWDP 3D 态势感知

紧迫性：中。

前置依赖：Phase 2，AWDP 数据语义稳定。

范围：

- 3D 态势只在 AWDP 模式启用。
- 红色流线表达攻击，服务或柱体同色发光表达修复。
- 动画绑定 AWDP 轮次、攻击、修复、Checker、Patch、得分数据。
- WebGL/WebGPU 技术路线以兼容性、性能和维护成本为评估基线。
- 大屏组件接入稳定数据接口，不能依赖页面临时状态。

代码范围：AWDP 控制器和服务、AWDP 前端、大屏组件、Three.js 场景。

交付文档：`docs/commercialization/phase-13-awdp-3d-visualization.md`。

验收重点：语义清晰、流畅、数据真实、不会影响普通 CTF 和培训。

### Phase 14：商业化全链路验收与运维交付

紧迫性：最终交付必需。

前置依赖：Phase 0-13。

范围：

- 功能验收：普通 CTF、练习、培训、理论、Docker、Linux VM、Windows VM、TeamLab、AWDP。
- 性能验收：数据库查询、Redis 命中、队列并发、节点调度、镜像分发、前端加载、前端交互。
- 高峰演练：多队伍同时启动和销毁环境，资源紧张排队提示，节点故障恢复。
- 故障演练：Redis 中断、数据库连接耗尽、Agent 离线、节点重启、镜像仓库不可达、部署中断、流量采集进程退出。
- 容量验收：按统一 SLI 记录 300-500 支队伍在线和全部可调度槽位并发使用结果。
- 运维验收：部署、升级、备份、恢复、节点注册、Agent 同步、存储服务器、日志检索。
- 安全验收：权限、API token、文件上传、镜像导入、Agent 命令边界、审计追踪。

交付文档：`docs/commercialization/phase-14-commercial-acceptance-runbook.md`。

验收重点：平台达到可部署、可运营、可审计、可压测、可交付状态。

## 10. 并行边界

可并行：

- Phase 1 与 Phase 2 可并行，API 类型稳定后再合并前端业务 hook。
- Phase 3 可在 Phase 1 模块边界冻结后立即启动，与 Phase 2 并行。
- Phase 4 与 Phase 5 可并行审计，Phase 5 实现必须引用 Phase 4 冻结后的数据生命周期。
- Phase 6 与 Phase 7 可并行，队列阶段名称和事件分类要共同冻结。
- Phase 8 VM 与 Phase 10 内容资产可并行，二者都使用 Phase 6 运行底座。
- Phase 11、Phase 12、Phase 13 在各自前置依赖完成后可并行。
- Phase 13 的视觉概念验证可在 Phase 2 后启动，数据接入必须在 AWDP 接口稳定后完成。

不可抢跑：

- 练习模块不能早于前端底座和内容资产模型。
- Linux SSH 不能早于 VM 抽象。
- TeamLab 多节点、Windows VM、流量观测闭环不能绕过 TeamLab 架构底座、运行底座、VM 抽象和可观测模型。
- 题目池不能绕过 scoped token、课程/比赛/练习隔离规则和全局模板所有权。
- Phase 0 已完成旧体系清理；后续阶段不得恢复已删除实体、路由、页面或兼容 DTO。

## 11. 开发规范

### 11.1 后端规范

- Controller 只做鉴权、参数校验、DTO 转换和响应。
- 复杂业务进入应用服务和领域服务，服务按能力域命名。
- 新 API 必须有鉴权、重复调用语义、错误模型、审计字段和版本策略。
- 新异步任务必须有状态机、事件、失败原因、恢复策略。
- Agent 命令必须集中封装和转义，禁止散落 shell 拼接。
- 不新增空实现、假数据和永久兼容壳。

### 11.2 数据库与缓存规范

- 新表必须定义唯一约束、索引、查询路径、数据量基线、保留周期和删除策略。
- 高频写入必须说明 Redis 缓冲、批写、聚合、归档方案。
- Redis 缓存必须写明失效触发点。
- 缓存不得破坏事实表恢复能力。

### 11.3 前端规范

- 新页面必须拆分页面容器、业务组件、展示组件和 API hook。
- 公共样式进入设计系统，不逐页复制。
- 页面文件不得新增视觉样式规则。
- 数据请求避免瀑布，独立请求并行发起。
- 使用 SWR 进行去重、缓存和刷新。
- 重组件动态导入，非当前 tab 延迟渲染。
- 大表格、大列表、日志、流量、题库使用分页、虚拟化或局部滚动。
- 使用 memo、稳定 callback、稳定依赖降低重渲染。
- 动画、3D、图表必须有性能预算。

### 11.4 Agent 与运维规范

- Agent 接口必须支持重复调用且结果稳定。
- 能力上报必须分开表达 Docker、KVM、WireGuard、抓包、VM cloud-init、维护接口。
- 节点同步必须具备版本、sha256、跳过逻辑和错误回传。
- 镜像下载必须校验摘要，失败写入队列和系统日志。
- 运维脚本不得破坏 `files` 持久化目录。

### 11.5 安全与权限规范

- 外部 token 必须绑定创建人、scope、资源边界、动作权限、过期时间和调用配额。
- 管理员、教师、出题人、选手的服务端权限必须由统一策略实现，前端隐藏按钮不能作为权限控制。
- 文件上传、镜像导入、Agent 命令、PCAP 下载和 VM 访问端点必须写入审计。
- 日志、metrics、tracing 禁止输出 Flag、token、WireGuard 私钥、密码和完整 userdata。
- 外部 API 必须接入速率限制、重复调用保护和契约测试。

### 11.6 测试规范

- 纯算法和状态机使用单元测试，数据库关系和事务使用集成测试，主站与 Agent 使用契约测试。
- 用户主流程使用 e2e，Docker、VM、TeamLab 使用真实环境验收。
- 调度、Redis、数据库、流量写入和前端渲染必须建立可重复基准测试。
- 每个 Wave 结束执行一次模块级综合验收，避免把验证切成低价值的小步骤。
- 缺陷修复必须补能阻止同类回归的最小测试，不堆砌无业务价值用例。

### 11.7 文档规范

- active docs 只保留当前总纲、阶段设计、接口规范、运维手册。
- 每个阶段文档必须引用本总纲。
- 阶段文档必须包含目标、现状证据、范围、非范围、依赖、数据模型、API、前端影响、测试验收、回滚策略。
- 历史计划、临时验收、旧审查报告进入归档目录。

## 12. 架构决策与迁移治理

### 12.1 技术路线分支

| 决策点 | 当前采用路线 | 切换条件 |
| --- | --- | --- |
| 主站架构 | 模块化单体 + 独立 Agent 执行面 | 某模块需要独立扩缩容、独立发布且模块边界已通过 Phase 1 验收时，才评估拆服务。 |
| 部署队列 | PostgreSQL 事实队列 + Redis 分布式锁 + 现有 QueueManager | Phase 6 基准证明目标吞吐、故障恢复或跨实例协调无法达标时，才引入专用消息中间件。 |
| TeamLab 网络 | WireGuard 玩家入口 + L3 Fabric | 产品明确要求同一二层广播域、二层协议训练或跨节点二层设备仿真时，才评估 VXLAN/OVS。 |
| 高频流量存储 | Redis 缓冲 + PostgreSQL 聚合事实 | Phase 4 基准证明目标保留周期和查询 SLA 无法由 PostgreSQL 分区方案满足时，才引入列式或时序存储。 |
| 前端样式 | design token + Mantine theme + 公共组件 + CSS module | 不切回单一超大 CSS，不允许页面私有视觉体系。 |
| 前端部署 | 独立制品、独立流水线、生产同域反向代理 | 需要独立 CDN、跨域发布或多前端消费同一 API 时，再拆部署域名。 |
| 外部 API | 版本化 REST + scoped token + 异步任务 | 实际调用场景需要事件订阅时，在稳定事件模型上增加 webhook；不复制业务接口。 |
| VM | KVM/libvirt + cloud-init/Cloudbase-init + 协议化访问端点 | 不为单个镜像增加 VM 特判。 |

### 12.2 数据迁移与旧代码删除

- 数据结构变更采用 expand → migrate → contract：先增加新结构，再执行可校验迁移，最后删除旧结构。
- 临时 adapter、双读或双写只能存在于同一 Wave，必须登记删除点并在 Wave 退出前移除。
- 每次迁移必须包含前置检查、备份、批次进度、校验摘要、失败恢复和回滚步骤。
- 删除课程、比赛、模板、镜像、runtime 时必须按所有权和引用关系执行，不使用级联删除替代业务规则。
- 旧 API、旧路由、旧表和旧前端入口退出时同步删除测试、文案、类型和迁移兼容代码。

### 12.3 版本与发布规则

- 主站、前端、Agent 分别生成版本和制品摘要，发布记录保存三者兼容矩阵。
- Agent 协议使用 capability negotiation，不用单个硬编码版本阈值表达功能支持。
- 外部 API 使用稳定主版本；破坏性变更必须发布新主版本和一次性迁移工具。
- 数据库迁移先在副本或验收环境执行，再进入生产发布。
- 发布失败时允许应用制品回滚；涉及 contract 阶段删除的数据结构变更只能通过已验证恢复流程回滚。

## 13. 需求覆盖矩阵

| 用户需求 | 主责阶段 | 闭环结果 |
| --- | --- | --- |
| 练习模块与首页重构 | Phase 2、10、11 | 内容资产先成立，首页和练习共用组件、运行底座与独立进度模型。 |
| Linux VM SSH | Phase 6、8 | VM 类型、OS、访问协议和生命周期统一。 |
| TeamLab 组网商业化 | Phase 3-9 | 独立 API、多节点、Docker/VM、WireGuard、流量、观测、恢复全部闭环。 |
| 删除宽度阻断页 | Phase 2 | 全站响应式、横向滚动和标准布局组件替代阻断。 |
| 容器上传和题目批量 API | Phase 1、10 | scoped token、版本化 API、异步任务、审计和重复调用保护。 |
| 全平台模块化解耦 | Phase 0、1，贯穿全部 Wave | 模块拥有对象、分层调用、禁止穿透和退出门槛统一。 |
| 题目池与课程隔离 | Phase 4、10、12 | 全局模板与课程绑定分离，删除课程不破坏全局资产。 |
| 前端组件化和设计语言切换 | Phase 2，贯穿 Phase 9-13 | 独立制品、全局 token/theme、公共组件、页面无私有视觉规则。 |
| 理论题 tag 与索引 | Phase 4、10、12 | tag 成为正式实体或关系，写入数据库索引和筛选 API。 |
| AWDP 3D 态势 | Phase 2、13 | 攻击/修复语义绑定真实事件，动效具备性能预算。 |
| 长期运营和大型比赛 | Phase 4-7、14 | 数据、缓存、调度、观测、故障、升级、备份和压测闭环。 |

## 14. 阶段文档清单

| 阶段 | 文档路径 |
| --- | --- |
| Phase 0 | `docs/commercialization/phase-00-baseline-cleanup.md` |
| Phase 1 | `docs/commercialization/phase-01-architecture-domain-api-contract.md` |
| Phase 2 | `docs/commercialization/phase-02-frontend-foundation.md` |
| Phase 3 | `docs/commercialization/phase-03-teamlab-foundation-decoupling.md` |
| Phase 4 | `docs/commercialization/phase-04-database-governance.md` |
| Phase 5 | `docs/commercialization/phase-05-redis-cache-high-frequency-data.md` |
| Phase 6 | `docs/commercialization/phase-06-runtime-scheduling-concurrency.md` |
| Phase 7 | `docs/commercialization/phase-07-observability-audit-recovery.md` |
| Phase 8 | `docs/commercialization/phase-08-vm-access-abstraction-linux-ssh.md` |
| Phase 9 | `docs/commercialization/phase-09-teamlab-networking-commercialization.md` |
| Phase 10 | `docs/commercialization/phase-10-content-assets-question-pool-api.md` |
| Phase 11 | `docs/commercialization/phase-11-homepage-exercise-module.md` |
| Phase 12 | `docs/commercialization/phase-12-training-theory-productization.md` |
| Phase 13 | `docs/commercialization/phase-13-awdp-3d-visualization.md` |
| Phase 14 | `docs/commercialization/phase-14-commercial-acceptance-runbook.md` |
