# TeamLab 商业化组网底座独立复审与自主开发任务书

## 1. 任务目标

你将接手 GZCTF/YINYU 平台的 TeamLab 组网模块。你的任务不是只运行现有测试，也不是只修复已知问题，而是：

1. 以当前代码和实际运行事实为准，重新建立对 TeamLab 全链路的准确理解。
2. 对组网模块进行生产级、端到端 code review，覆盖稳定性、正确性、并发能力、工程质量、功能完整度和前端产品体验。
3. 对照成熟商业化组网平台，找出会阻止 TeamLab 成为通用商业底座的结构性缺口。
4. 自主设计后续开发项，按风险和收益排序后直接实施，形成可维护、可复用、可审计的闭环。
5. 用本地测试、契约测试和真实多节点全链路证据证明结果，而不是以“代码看起来正确”作为完成标准。

最终目标是让 TeamLab 成为可供比赛、培训、练习、外部 API 和后续产品模块复用的独立组网底座，而不是某个比赛页面中的专用功能。

## 2. 代码基线与事实原则

编写本任务书时的参考分支为 `codex/phase-09-teamlab-networking`，参考提交为 `23b614b5c08c6b4c9fda148ad101cdf74ac2221e`。开始工作时必须重新读取实际 `HEAD`、远程分支和工作区状态；若代码已继续演进，以最新代码为准。

以下文档只能帮助定位背景，不能代替代码审查，也不能作为当前通过结论：

- `docs/platform-commercialization-master-plan.md`
- `docs/commercialization/phase-03-teamlab-foundation-decoupling.md`
- `docs/commercialization/teamlab-api-foundation-contract.md`
- `docs/commercialization/phase-06-runtime-scheduling-concurrency.md`
- `docs/commercialization/phase-07-observability-audit-recovery.md`
- `docs/commercialization/phase-09-teamlab-networking-commercialization.md`
- `docs/commercialization/phase-09-teamlab-networking-independent-code-review.md`
- `docs/commercialization/reviews/phase-09-teamlab-networking-independent-review.md`
- `docs/superpowers/specs/2026-07-22-teamlab-commercial-control-plane-design.md`
- `docs/superpowers/specs/2026-07-25-teamlab-vnext-orchestration-design.md`

证据优先级固定为：

1. 当前真实运行行为。
2. Agent、宿主机网络、Docker、libvirt、WireGuard 和数据存储事实。
3. 数据库当前状态、队列、事件、日志和指标。
4. 当前发布产物及进程配置。
5. 当前代码和迁移。
6. 测试与文档。

旧审查报告中标记为已关闭的问题必须抽查当前实现，不能直接继承“已修复”状态。文档与代码冲突时修正文档，不得为迁就旧文档扭曲实现。

## 3. 项目内容与技术结构

### 3.1 平台定位

该项目是商业化网络安全综合演练平台，主要包含：

- 常规 CTF 比赛、队伍、题目、提交、排行榜和环境实例。
- TeamLab 多节点网络靶场与综合渗透场景。
- 培训、练习、理论考试和课程管理。
- Docker、KVM/QEMU 虚拟机、镜像模板和节点资源调度。
- 统一身份、权限组、外部 API、部署队列、系统日志和运行观测。
- 平台主服务、Worker Agent、内部镜像 Registry、玩家 VPN 接入和远程访问。

主要技术栈为 ASP.NET Core/.NET、Entity Framework Core、PostgreSQL、Redis、React 19、TypeScript、Vite、SWR、React Flow，以及 Linux 上的 Docker、libvirt/KVM、network namespace、bridge、dnsmasq、nftables/iptables、WireGuard 和抓包工具。

### 3.2 TeamLab 控制面

平台主服务负责：

- 拓扑草稿、结构校验、发布版本和确定性执行计划。
- Runtime、generation、shard、network、asset 和 access grant 生命周期。
- 节点能力过滤、资源容量预留、物理放置和部署队列。
- Docker/VM 镜像解析、内部存储、节点分发和引用释放。
- 多节点 rollout、提前镜像准备、队伍环境创建、重置、销毁和恢复。
- 运行事件、系统日志、流量元数据、路径查询、按需 PCAP 和清理状态。
- 比赛、权限、目标和 TeamLab 独立底座之间的应用层适配。

核心代码集中在：

- `src/GZCTF/Modules/TeamLab/Domain`
- `src/GZCTF/Modules/TeamLab/Application`
- `src/GZCTF/Modules/TeamLab/Infrastructure`
- `src/GZCTF/Modules/TeamLab/Api`
- `src/GZCTF/Modules/Runtime`
- `src/GZCTF/Modules/Content`
- `src/GZCTF/Services/Fleet`
- `src/GZCTF/Modules/Penetration`

重点入口包括 `TeamLabTopologyApplicationService`、`TeamLabRuntimePlanner`、`TeamLabRuntimeOrchestrator`、`TeamLabShardDeploymentService`、`TeamLabRuntimeCleanupService`、`TeamLabRolloutCoordinator`、镜像分发服务、流量采集协调器和 TeamLab API controllers。

### 3.3 Worker Agent 数据面

Agent 负责在目标节点幂等地落实平台计划：

- 创建和清理 bridge、router namespace、veth、TAP、DHCP/DNS、路由和防火墙规则。
- 配置 Worker 间 Fabric/WireGuard 和网段路由。
- 创建 Docker 容器、Linux VM 和 Windows VM，并接入一张或多张网卡。
- 拉取 Docker 镜像和 VM OCI artifact，维护节点本地缓存。
- 生成配置盘、启动 domain、收集来宾和基础设施就绪事实。
- 捕获流量、生成元数据或 PCAP，并执行精确清理。
- 上报 Agent 版本、协议版本、能力清单、执行限流和节点事实。

重点代码位于：

- `src/GZCTF.Agent/Controllers`
- `src/GZCTF.Agent/Services/TeamLab`
- `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
- `src/GZCTF.Agent/Services/DockerService.cs`
- `src/GZCTF.Agent/Services/KvmService.cs`
- `src/GZCTF.Agent/Services/GuestControl`

### 3.4 网络数据面

当前总体方向是多节点三层路由型 Fabric：一个逻辑网段由一个 Worker shard 承载，跨 Worker 流量通过稳定 Fabric 地址和路由转发，不构建跨节点大二层。玩家通过单一 WireGuard 入口进入授权入口网段，再按拓扑定义经过路由访问其他网段。

Docker、Linux VM、Windows VM 和混合资产必须使用同一拓扑与运行模型。基础联网由宿主机 bridge、namespace、TAP/veth、dnsmasq、路由和防火墙完成，不能把基础可达性依赖于来宾脚本是否成功。来宾注入只负责应用配置、动态参数、健康信号和可选遥测。

### 3.5 镜像与服务边界

内部 Registry 是镜像主副本。Docker 使用 Registry 镜像；VM qcow2 使用 OCI artifact。比赛启动不应成为首次大镜像传输的主要时机，发布/rollout 阶段应提前准备到合格节点，运行时仅校验并在状态明确时执行兜底。

AD 域、数据库、工控协议服务和漏洞业务本身属于镜像、Bootstrap Profile 或场景制品内容，不属于网络底座实现。TeamLab 负责它们所需的网络、DNS、地址、依赖、注入、启动顺序、健康事实、访问、观测和生命周期，但不应在组网模块中硬编码某个 AD/工控产品的安装逻辑。

### 3.6 前端

当前 TeamLab vNext 前端包括：

- 场景库与创建入口。
- 基于 React Flow 的拓扑画布。
- 交换机、路由器、Docker、Linux VM、Windows VM 节点。
- 网络连接、依赖连接、检查器、自动布局、撤销/重做、复制/粘贴和专注模式。
- 中文校验定位、保存冲突、发布状态和试运行入口。
- 比赛内 Release 选择、目标配置、rollout 控制和队伍环境管理。
- 管理端 runtime、分片、阶段、事件、日志、流量、路径和 PCAP 页面。
- 选手端环境状态、目标、访问配置和重置交互。

主要代码位于：

- `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab`
- `src/GZCTF/ClientApp/src/vnext/features/admin/games/teamlab`
- `src/GZCTF/ClientApp/src/vnext/features/games/teamlab`
- `src/GZCTF/ClientApp/src/vnext/features/admin/api`
- `src/GZCTF/ClientApp/src/vnext/shared`

旧 TeamLab/Penetration 前端已在当前分支中清理。不得恢复旧页面、旧全局样式或建立新旧双轨。

## 4. 不可破坏的架构约束

后续审查和开发必须守住以下约束：

1. TeamLab 是独立底座。核心 API、领域对象和 runtime 不得依赖 Game、Team、Penetration DTO；比赛只能通过 adapter/application contract 接入。
2. Topology 草稿、不可变 Release、Plan、Runtime、Generation 和 Rollout 必须是不同概念，不能互相覆盖或由前端状态替代。
3. 同一输入必须生成确定性计划；调度结果的差异必须来自明确节点事实或容量变化。
4. Docker 和 KVM 能力独立判断。缺少 KVM 不能阻断 Docker；各类镜像只分发到具备对应能力的节点。
5. 容量必须同时计算运行事实、预留和在途创建，禁止超卖，也禁止重复计数导致资源长期闲置。
6. 所有写操作必须具备清晰幂等语义；并发相同请求稳定复用，并发冲突返回正式错误而不是 500。
7. generation 是资源所有权 fencing token。旧 generation 的延迟清理绝不能删除新 generation 资源。
8. 就绪状态必须由事实或事件驱动，禁止固定 sleep、盲目加超时、无条件自动重试或“第二次启动成功”逻辑。
9. 自动恢复必须先证明期望状态和真实状态的差异；网络/Fabric 重建不得无意义改变地址、密钥、路由或玩家配置。
10. Destroy 只有在所有节点、存储、队列、捕获和容量引用完成清理后才能成功。失败必须进入可恢复的 cleanup-pending 状态。
11. 镜像主副本、节点缓存、分发记录和运行引用必须闭环。不得依赖平台本地偶然存在的文件。
12. Agent 变更接口必须幂等，命令生成集中封装并沿用 shell escaping、参数白名单和 capability protocol。
13. 每个自动动作必须有 operation、阶段、版本、错误码、日志和可查询状态，不允许无法解释的后台行为。
14. 管理权限应与常规 CTF 的所有者、教师、权限组、Admin、SuperAdmin 语义一致；选手只能访问自己的队伍环境。
15. 外部 API 遵守 Phase 1/3 契约：版本化、资源导向、幂等、标准错误、游标或稳定分页、作用域认证和关联 ID。
16. 不保留无价值兼容层、平行实现、永久 feature flag、废弃数据库字段或重复状态源。
17. 修复应删除根因和错误实现，不能继续叠加补丁分支。
18. 普通 CTF、培训、普通 Docker/VM 实例与 TeamLab 共用容量时必须一致核算，任何模块不能绕过统一预算。

## 5. 全面 Code Review 范围

### 5.1 架构、边界与代码质量

检查：

- Domain、Application、Infrastructure、API 和 frontend adapter 是否真正分层。
- 控制器是否只负责协议转换，是否仍包含调度、网络或业务状态机。
- TeamLab 是否穿透访问其他模块数据库实体，或被比赛模块反向拥有。
- 是否存在超大类、重复状态机、重复 DTO/parser、复制的 create/cleanup 逻辑和隐式全局状态。
- 接口是否表达稳定意图，内部实现是否可替换。
- 命名、错误模型、事件码、状态枚举和注释是否清晰统一。
- 删除的旧前端、旧 API、旧模型和旧迁移后续代码是否仍有引用。

目标不是追求文件数量少，而是每个单元职责单一、依赖方向明确、可以独立测试和替换。发现复杂度时先寻找状态源重复或边界错误，不要立即再加抽象层。

### 5.2 拓扑、校验、发布与场景库

检查完整链路：创建草稿、编辑、自动保存、revision 冲突、校验、发布、不可变版本、重复发布、Release 引用和删除保护。

重点验证：

- 交换机、路由器、网段、网卡、Docker、VM、多网卡、依赖和观测配置能无损往返。
- 前端文档模型与服务端 schema 只存在一套明确映射。
- 校验问题全部中文、可定位、无重复、无错误版本混淆。
- 当前设计、已发布 Release 和比赛绑定版本在 UI 与 API 中语义一致。
- 镜像 digest、Bootstrap 能力、场景 artifact 和资源计划在发布时冻结。
- 场景通过校验、试运行和人工准入后可以进入场景库，被多个比赛直接复用。
- 草稿变更不影响已运行比赛；删除场景不会破坏历史 Release 和运行审计。

### 5.3 调度、容量与高并发

审查节点能力过滤、单节点优先、多 shard 放置、跨节点成本、容量预留、队列、公平性、锁和释放路径。

至少验证：

- 单节点足够时不无意义拆分；不足时按网段/资产约束形成多 shard。
- Docker、VM、网络操作和镜像传输拥有独立且合理的并发预算。
- 同节点多个容器/VM可在预算内并行创建，不被平台串行逻辑拖慢。
- 比赛批量启动时不会全部争抢同一节点，也不会因预留泄漏永久失去容量。
- rollout、普通实例和 TeamLab runtime 使用同一容量事实。
- 主服务多副本或后台 worker 重入时，队列领取、状态推进和 side effect 不重复执行。
- 节点离线、Agent 超时、数据库事务失败和服务重启后，状态可以基于事实恢复。

用真实容量单位和绝对剩余量参与决策，不能只比较负载百分比。不要为了“提高利用率”删除必要预留；应消除重复计数、缩短预留持有时间并保证精确释放。

### 5.4 Fabric、路由、DHCP/DNS 和玩家入口

逐条审查 topology → plan → shard → Agent request → Linux 网络事实 → 玩家访问。

验证：

- RFC1918 混合网段、多网卡、唯一默认路由和未连线隔离。
- 同节点与跨节点 Docker/VM 通信使用一致语义。
- Fabric peer、AllowedIPs、路由归属和转发规则不存在冲突或环路。
- DHCP 按 MAC 稳定分配；DNS 名称在 reset 后保持语义一致。
- dnsmasq、WireGuard 和关键网络进程具备真实 liveness，可安全恢复。
- 玩家只进入授权入口，不能绕过路由节点直接访问未连接网段。
- reset 不改变不应变化的玩家配置、Fabric 地址和稳定资产地址。
- 清理精确匹配 runtime/generation，不影响其他队伍和普通平台资源。

### 5.5 Docker、Linux VM 和 Windows VM

三类资产必须分别审查并完成混合验证：

- Docker 创建、网络门控、启动命令、健康检查、日志、停止和删除。
- Linux VM 的镜像、overlay、config drive、cloud-init/QGA、网络和 SSH 可达性。
- Windows VM 的 VirtIO、QGA/Cloudbase-init、网络、RDP/Guacamole 和重启恢复。
- Opaque/Managed/Scenario 等能力模式以实际代码定义为准，不允许 UI 或发布校验承诺代码未实现的能力。
- 基础网络不能依赖 Guest Supervisor；高级动态注入和进程遥测必须有明确镜像能力契约。
- VM domain、TAP、overlay、ISO、配置盘和临时凭据均有唯一所有权和精确清理。

不要把特定模板启动慢误判为组网错误，也不要用提高固定等待预算掩盖缺失的就绪事件。平台开销、镜像启动时间和服务初始化时间必须分别度量。

### 5.6 镜像存储、预分发和清理

检查 Docker 与 VM 从上传到销毁的完整链路：

- 上传或导入后是否进入唯一内部 Registry 主副本。
- digest、类型、大小和模板引用是否可验证。
- 发布/比赛准备是否按能力节点并行预分发，同节点是否限流。
- 相同 `template + digest + node` 是否去重。
- 启动时已就绪是否直接创建，未就绪是否显示独立“镜像准备”阶段。
- 多比赛共享镜像时引用计数是否正确。
- 比赛结束是否只释放引用，最后一个引用释放且无运行实例时才清理节点缓存。
- Registry 不可达、节点掉线、服务重启和手工删除缓存后 reconcile 是否能恢复事实。
- 存储主副本不会随比赛结束删除，模板删除流程是否具备引用保护。

### 5.7 Runtime、Rollout 与商业生命周期控制

对照成熟控制面审查并补齐以下能力的状态机与操作语义：

- 创建、排队、准备、部署、启动、就绪、访问开放。
- 提前部署、镜像预热、分批 rollout、失败阻断、目标重建和进度聚合。
- 停止、暂停/挂起、恢复、重置、排空、销毁和强制清理。
- 比赛开始、延期、结束、取消和异常中止对环境的影响。
- 场景试运行、验证通过、入库、版本冻结和跨比赛复用。
- 单队操作与比赛级批量操作的权限、并发预算和审计。

“暂停”不能只在前端增加按钮。必须先定义 Docker pause、VM suspend、网络保持、租约、计费/容量、访问授权、超时和服务重启后的恢复语义。若无法形成一致状态机，应明确不实现并提供更可靠的 stop/resume 或 snapshot/restore 方案。

### 5.8 可观测性、审计和恢复

检查每个阶段是否同时具备：

- 用户可读中文阶段与错误。
- 稳定机器错误码、category、retryable 和 correlation/operation ID。
- 部署队列记录请求对象、比赛/队伍/用户、节点名称、具体镜像和操作类型。
- 系统日志记录控制面和管理员动作。
- runtime event 记录 generation、shard、asset、network 和阶段耗时。
- 指标覆盖队列等待、镜像准备、网络创建、资产启动、健康检查、清理、错误率和容量。
- 状态页面可以从数据库事实和 Agent 事实解释当前环境为什么停在某个阶段。

流量观测必须覆盖 A→B、B→C、C→B、B→A 等实际链路，而不是只观察玩家入口。默认元数据采集应轻量；PCAP 必须按范围、时间、大小和保留策略限制，并在 runtime 销毁后完成 Agent 与对象存储双侧清理。

### 5.9 权限、安全与多租户隔离

检查：

- 场景所有者、教师、权限组、Admin、SuperAdmin 与普通 CTF 权限语义一致。
- 玩家只能获取本队 access grant、VPN 配置、目标和流量视图。
- 外部 API token scope 最小化，资源所有权在 Application 层统一执行。
- 拓扑变量、命令、镜像引用、文件路径、网段、DNS 名称和 Agent 参数均有结构化校验。
- shell、PowerShell、cloud-init、配置盘和环境变量中的秘密不进入日志或持久化产物。
- 不可信模板不能伪造 Managed 能力或平台就绪信号。
- 不同队伍的 namespace、bridge、路由、防火墙、镜像引用、捕获和访问令牌严格隔离。

### 5.10 前端架构、表现和流畅度

前端审查不能只看“页面能打开”。必须覆盖：

- 组件是否独立文件、职责清晰、复用共享组件和全局 token。
- 是否存在零散私有样式、旧 Mantine/旧全局 CSS 回流或重复设计语言。
- React 代码遵循 `react-best-practice` 原则：稳定 props、按需加载、避免瀑布请求、避免大范围无效重渲染、列表和画布具备性能预算。
- 大型拓扑下拖拽、框选、缩放、背景平移、连线、自动布局、撤销和复制是否流畅。
- 专注模式不遮挡、不溢出，画布占用足够空间，背景拖动与节点选择意图清晰。
- 自动布局能处理交换机、路由器、多网卡、依赖边和孤立节点，结果确定且可读。
- 校验全中文，点击问题能够定位节点、连线或字段。
- 镜像准备、网络创建、资产创建、健康检查、启动和清理使用真实阶段反馈，不使用伪进度动画。
- 页面刷新、SWR 更新和轮询无感，不重置筛选、滚动、Tab、画布位置或节点顺序。
- 管理员和选手看到的动作、状态和错误与后端 capability/state machine 一致。
- 键盘、焦点、tooltip、颜色对比、reduced motion 和移动端只读体验可用。

保持当前 vNext 风格，不做与任务无关的视觉重写。可参考 Dify 的低成本编排交互、EVE-NG/PNETLab/GNS3 的网络场景心智，但不得照搬其技术限制或界面。

## 6. 与成熟商业化组网平台的能力对标

不要只按现有需求清单打勾。至少按下表逐项判断“已完成、部分完成、缺失、不应实现”，并给出代码和运行证据：

| 能力域 | 商业化目标 |
| --- | --- |
| 场景资产 | 场景草稿、校验、试运行、准入、不可变版本、场景库、跨比赛复用和安全删除 |
| 编排体验 | 设备导向、清晰连线、自动布局、快捷键、冲突保存、中文定位和大型画布性能 |
| 计算资产 | Docker、Linux VM、Windows VM、混合部署、多网卡和能力驱动注入 |
| 网络能力 | 多网段、路由器、交换机、跨 Worker Fabric、入口控制、DNS/DHCP 和隔离 |
| 发布控制 | Plan、容量预检、镜像预分发、试运行、分批 rollout、失败阻断和回滚语义 |
| 比赛运营 | 按队部署、提前部署、批量控制、延期、结束排空、异常恢复和进度聚合 |
| 生命周期 | 创建、停止、暂停/恢复方案、重置、销毁、强制清理、generation fencing |
| 调度并发 | 统一容量、能力过滤、公平队列、并行创建、传输限流、无超卖和无预留泄漏 |
| 可观测性 | 阶段、事件、日志、指标、流量路径、PCAP、错误分类和端到端关联 |
| 开放底座 | 稳定 API、scope、幂等、SDK/OpenAPI、外部调用不依赖比赛内部模型 |
| 多租户安全 | 资源所有权、网络隔离、秘密保护、审计、限额和恶意输入边界 |
| 运维交付 | capability protocol、节点升级、reconcile、备份恢复、容量基线和故障手册 |

对标不是无限增加功能。任何新增项必须证明至少满足以下一项：显著提高稳定性、降低运营成本、减少用户等待、增强场景复用、提升大型比赛承载能力，或补齐外部底座契约。否则不开发。

## 7. 自主开发授权与优先级

完成事实审查后，你有权直接设计和开发高价值改进，不需要等待逐个小点确认。优先级固定为：

1. `P0`：跨租户破坏、数据丢失、网络越权、秘密泄露、不可逆错误。
2. `P1`：会阻断生产运行、错误销毁、容量超卖、状态机失真、无法恢复的稳定性问题。
3. `P2`：商业闭环缺失、高并发瓶颈、明显功能缺口、前后端契约错误和严重体验问题。
4. `P3`：局部维护性、性能和体验优化。

执行规则：

- 先修根因和状态模型，再补 UI；先保证销毁和恢复，再扩大创建能力。
- 独立问题可以并行审查，但共享状态机和迁移必须由一个统一设计收敛。
- 每个开发单元应能单独解释目标、边界、数据迁移、失败行为、观测和回滚。
- 优先复用现有 runtime、operation、queue、event、capability、distribution 和 vNext 组件。
- 新增抽象必须删除真实重复或稳定边界，禁止为“以后可能用”创建框架。
- 发现现行技术方向收益明显低于替代方案时，应提出并实施最小可迁移的重构；不得保留永久双轨。
- 涉及资源身份、外部 API 不兼容变更、不可逆数据迁移或重大产品语义选择时，暂停并向用户确认。

## 8. 推荐执行顺序

### 8.1 建立当前事实

- 记录 commit、分支、数据库迁移、Agent 协议、节点能力和可用测试环境。
- 画出 topology → release → plan → rollout/runtime → shard → Agent → Linux 资源 → event/cleanup 的真实调用链。
- 建立当前功能矩阵和状态机，不从旧计划复制结论。

### 8.2 独立审查

- 按第 5 节逐链路审查。
- Finding 必须包含严重级别、触发条件、代码位置、运行影响、根因、最小正确修复方向和验证方法。
- 不报告纯风格偏好；代码简洁性问题必须说明其导致的维护、正确性或性能风险。

### 8.3 形成开发批次

- 第一批：生产安全、生命周期正确性、幂等和恢复。
- 第二批：调度并发、镜像预分发、rollout 和比赛运营闭环。
- 第三批：外部 API、场景复用、前端控制面和大型画布体验。
- 第四批：容量压测、故障注入、运维材料和商业验收。

批次可根据代码事实调整，但前一批必须降低后一批的实现成本，不能先堆界面后返工状态机。

### 8.4 开发与验证

- 只在大单元边界运行集中测试，不为每个小函数重复全量构建。
- 单元测试覆盖状态机、确定性算法、幂等、权限、清理和纯前端模型。
- 集成测试覆盖数据库约束、队列领取、API 契约和主服务/Agent 协作。
- 真实环境测试必须使用标准发布包，不热替换 DLL，不依赖手工修复节点状态。
- 失败后先定位最早错误事实，只修改一个根因；禁止一边部署一边无计划叠加补丁。

## 9. 验收要求

### 9.1 本地质量门槛

至少完成：

- 主服务和 Agent Release 构建。
- TeamLab、Runtime、Content、Fleet、权限和迁移相关测试。
- 前端 TypeScript、lint、架构检查、TeamLab 测试、生产构建和 bundle budget。
- OpenAPI 兼容检查、迁移一致性检查和 `git diff --check`。
- 删除代码、废弃路由和旧样式引用检查。

### 9.2 多节点全链路

使用用户指定的双 Worker 或更多节点环境，严格控制测试资源，只创建一个覆盖性场景和一个队伍 runtime。场景至少包含：

- 两个以上网段和跨节点分片。
- Docker 服务。
- Linux VM。
- Windows VM。
- 显式交换机和路由器语义。
- 一条需要跨多个资产往返的流量路径。

验证：

- 发布校验、不可变 Release、Plan 和节点放置。
- 镜像已提前分发，启动时不重复下载。
- 玩家 WireGuard、DNS、Docker、Linux、Windows 和跨网段访问。
- 未连接网段隔离、非入口网段不能被直接绕过访问。
- A→B、B→C、C→B、B→A 流量元数据和按需 PCAP。
- reset 后地址、DNS、入口和拓扑语义稳定。
- 比赛结束或 drain 后自动销毁。
- destroy 后所有节点无所属 container、domain、overlay、ISO、namespace、bridge、veth、TAP、route、firewall、dnsmasq、capture、lease、reservation、access grant 和 distribution claim 残留。
- 临时修改的容量、节点配置和测试数据全部恢复或删除。

### 9.3 并发与故障

在不破坏环境的前提下验证：

- 多队伍并发创建的队列、公平性、容量和耗时。
- Docker、VM、镜像传输和网络操作在各自预算内并行。
- 节点离线、Agent 重启、主服务重启、Registry 短暂不可达、创建中取消和清理中断。
- 重复 API 请求、并发 reset/destroy 和旧 generation 延迟回调。
- 恢复后不重复创建、不错误释放、不越权访问、不泄漏预留。

不得通过无限重试把故障测试变成最终成功测试。恢复动作必须有次数上限、明确触发条件、状态变化和审计证据。

### 9.4 性能与体验

分别记录平台调度、网络创建、镜像准备、资产创建、来宾启动和业务健康耗时。启动目标必须排除模板自身不可控的服务初始化时间，同时证明平台没有串行瀑布和固定等待。

前端至少在普通桌面、宽屏和移动只读视口验证：

- 无重叠、截断、布局跳动和强制页面刷新。
- 大型拓扑的缩放、拖动、连线、自动布局和选择保持流畅。
- 真实阶段及时更新，错误可定位，操作后无需手工刷新。
- 所有临时动画、轮询和订阅在组件卸载后正确释放。

## 10. 工作记录与交付物

开始后创建并持续维护：

- `docs/commercialization/reviews/teamlab-commercial-foundation-review-progress.md`

该文件只记录已核实事实、finding 状态、开发批次、测试证据和阻塞项，不写每日流水账。

最终至少交付：

1. 当前架构和功能矩阵。
2. 按严重级别排序的独立审查报告。
3. 商业化能力对标和明确差距。
4. 已实施的设计、代码、迁移和 API 变更。
5. 未实施项及其不实施理由，不得用模糊 TODO 代替。
6. 本地测试结果和真实多节点证据。
7. 性能、并发、故障恢复和残留清理结果。
8. 更新后的 OpenAPI、中文 API 文档、运行手册和必要架构文档。

每个完成结论必须能回答：实现在哪里、由什么事实触发、失败时进入什么状态、如何观测、如何恢复、如何证明不会影响其他比赛和普通实例。

## 11. 禁止事项

- 禁止把旧审查报告当成当前代码事实。
- 禁止未定位根因就修改多个超时、增加重试或扩大资源预算。
- 禁止为通过一次 Demo 写模板特例、IP 特例、OS 版本分支或服务器专用逻辑。
- 禁止热更新单个 DLL/Agent 后声称标准部署可用。
- 禁止创建大量比赛、队伍、容器或 VM；测试完成必须删除测试内容。
- 禁止修改 TeamLab 底座以迎合前端临时状态。
- 禁止在比赛 adapter 中复制 TeamLab 调度、网络、镜像或清理实现。
- 禁止恢复旧前端、旧全局样式和双轨 API。
- 禁止把复杂度、代码量或抽象层数量当作工程质量。
- 禁止在没有运行证据时声明“生产可用”或“彻底闭环”。

## 12. 完成标准

只有同时满足以下条件，才可宣布本任务完成：

- 所有 P0/P1 已关闭并有验证证据。
- P2 中影响商业闭环、并发承载、生命周期和前端关键操作的项目已关闭。
- TeamLab 仍是独立、稳定、版本化、可供外部调用的底座。
- Docker、Linux VM、Windows VM 多节点混合组网通过标准部署全链路验收。
- 提前镜像分发、比赛 rollout、运行控制、流量观测、reset、结束销毁和恢复形成闭环。
- 前端达到清晰、流畅、无感更新、全中文错误和大型拓扑可用的产品水准。
- 普通 CTF、培训、普通 Docker/VM 与 TeamLab 不发生容量、权限或生命周期冲突。
- 测试环境无残留，临时配置已恢复，文档与代码一致。

若某项无法完成，必须保留准确状态、根因、证据和下一步，不得用延长等待、自动重试或隐藏错误代替完成。
