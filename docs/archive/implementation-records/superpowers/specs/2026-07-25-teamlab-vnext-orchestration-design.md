# TeamLab vNext 组网编排前端设计

## 1. 目标

本设计在现有 TeamLab 组网底座之上建设统一的 vNext 管理端与选手端体验。前端负责把拓扑草稿、不可变 Release、运行时、比赛绑定、部署进度和流量证据组织成清晰的产品流程，不重写调度、Fabric、Agent、Docker、VM、镜像传输或流量采集实现。

目标包括：

- 提供可复用的场景库，而不是在每场比赛中重复绘制拓扑。
- 使用显式交换机、路由器、Docker、Linux VM 和 Windows VM 完成低成本编排。
- 完整覆盖当前 TeamLab topology schema v2 能力，并通过渐进式配置控制复杂度。
- 接入平台统一日志、部署队列、节点、镜像、operation 和权限模型。
- 管理大型场景时保持稳定交互和可观测状态，不依赖页面刷新。
- 管理端和选手端共享同一运行时事实，不复制部署逻辑。
- 迁移完成后删除旧 Penetration 编排前端，不长期维护双轨实现。

### 1.1 参考产品原则

本设计参考 Dify、n8n 和 Tines 的编排方法，但不复制其 AI 或自动化业务模型：

- Dify：采用“拖入、连接、配置”的低成本操作，区分可变草稿、调试验证和显式发布。
- n8n：把工作流定义与每次执行记录分离，运行错误准确落到节点和阶段。
- Tines：面向安全运营使用动作语言、右侧检查器和可复用 Storyboard，减少用户直接编辑内部数据结构。

TeamLab 对这些原则的映射是：场景草稿对应 topology，发布版本对应 Release，执行记录对应 runtime/operation；交换机、路由器和计算资产保持真实网络语义，不把通用流程节点生搬到组网画布。

### 1.2 与既有控制面设计的关系

`2026-07-22-teamlab-commercial-control-plane-design.md` 定义了商业化控制面的长期方向。本设计收敛其前端表达，并坚持以当前代码为事实：尚未落地的 Scenario、Rollout 或细化生命周期不得在 UI 中伪装为可用能力。实施时若相关控制面已经由其他计划落地，应通过 adapter 消费现有契约；若尚未落地，只补完成当前前端闭环所必需的管理控制面，不建立平行执行实现。

## 2. 代码事实与边界

### 2.1 已实现底座

当前代码已具备：

- `TeamLabTopology` 草稿、revision 乐观并发和编辑器坐标。
- schema v2 的 `ManagedSwitch`、`ManagedRouter`、网段、资产、网卡、连接方向和依赖 DAG。
- Docker、Linux VM、Windows VM、多网卡和路由资产。
- Bootstrap Profile、健康检查、发布时预制和端点观测策略。
- 不可变 Release、确定性 Plan、Runtime、Shard、Reset、Destroy 和 Access Grant。
- 多节点放置、L3 Fabric、WireGuard、DHCP/DNS、流量摘要、流量路径和按需 PCAP。
- Penetration 比赛绑定、目标、Release 激活、批量部署、停止、队伍重建和清理。
- vNext Shell、设计 token、共享交互组件、日志流、部署队列、节点、镜像和实例页面。

### 2.2 当前前端现状

- `moduleRegistry.ts` 已登记 `/admin/teamlab`，但尚未标记为已实现。
- `VNextApp.tsx` 尚无 TeamLab 路由，访问时会落入 `AdminPendingPage`。
- 旧入口最终指向约 513 行的 `PenetrationAdminPage.tsx`，同一组件混合 API、画布、表单、发布、比赛和运行状态。
- 旧页面依赖 Mantine、旧视觉组件和全局样式，不能导入 vNext。
- 旧画布没有以稳定产品心智完整表达 schema v2 的交换机和路由器。

### 2.3 当前契约缺口

- `TeamLabTopologyEditorModel` 只保存 Networks 和 Assets 坐标，不能保存 Infrastructure 坐标。
- 管理端场景列表仅返回基础摘要，没有分页、所有者、规模、最新 Release 和引用摘要。
- 管理端 runtime API 缺少试运行创建、列表、重置、销毁、访问授权和 traffic path 的会话入口。
- 镜像分发有持久化记录和执行器，但没有面向场景 Release 的聚合就绪查询。
- 比赛 adapter 有立即 deploy/stop，但没有持久化的提前部署计划和聚合生命周期投影。
- 普通 CTF 编辑端当前只要求 Teacher，没有执行 `Game.OwnerId` 校验；TeamLab 使用创建者或 `Role == Admin`，两者语义不一致且后者漏掉 SuperAdmin。
- TeamLab runtime 事件没有专用 SignalR 推送；日志已有推送，runtime 阶段需通过增量事件查询进行事实校准。

### 2.4 不得改动

本轮不得改变：

- TeamLab topology 的网络语义。
- 分片调度和容量算法。
- Runtime、Shard、Fabric、WireGuard、DHCP/DNS。
- Agent 命令和 Docker/VM 创建链路。
- OCI Registry、镜像分发执行和流量采集数据面。
- `/api/open/v1/teamlab` 的资源身份、错误码和幂等语义。

必要的后端工作仅限平台管理控制面投影、会话 API、所有者授权和编辑器元数据。

## 3. 产品信息架构

采用双层工作台。

### 3.1 独立场景库

路由前缀为 `/admin/teamlab`：

- `/admin/teamlab`：场景库。
- `/admin/teamlab/:topologyId/design`：设计。
- `/admin/teamlab/:topologyId/releases`：发布。
- `/admin/teamlab/:topologyId/runtimes`：运行。
- `/admin/teamlab/:topologyId/runtimes/:runtimeId`：运行详情。

场景库中的场景草稿由 `TeamLabTopology` 承载，发布版本由 `TeamLabTopologyRelease` 承载，不再新增同义场景实体。列表以高信息密度表格呈现场景名称、所有者、网络和资产规模、最新 Release、校验状态、试运行状态、引用比赛数和更新时间。

### 3.2 比赛内接入

渗透或混合比赛增加“组网场景”页，只负责：

- 选择场景和不可变 Release。
- 绑定比赛目标和允许的运行时 overlay。
- 查看能力预检和镜像准备。
- 设置提前部署时间。
- 管理队伍环境和比赛结束清理。

比赛内不提供重复的完整拓扑编辑器。

### 3.3 场景详情壳层

场景详情包含三个独立视图：

- 设计：节点库、画布、检查器和校验抽屉。
- 发布：Release、Plan、镜像就绪和试运行记录。
- 运行：试运行或正式运行实例、日志、分片、流量和 PCAP。

设计状态在视图切换时保持；发布和运行区域按路由懒加载。

## 4. 编排心智模型

### 4.1 设备导向

用户主要操作设备、网卡和连接，不直接操作底层 key：

- 拖入交换机时创建 `ManagedSwitch` 和绑定网段。
- 资产连接交换机时创建网卡。
- 路由器连接交换机时创建路由接口和网段关系。
- Linux VM 和 Windows VM 是不同画布节点，底层根据镜像能力编译为 VM 资产。
- 多网卡资产启用路由后可作为 `ViaAssetKey` 路由资产。

交换机承载网段，避免同时向用户展示“网段节点”和“交换机节点”两个重复概念。网段 CIDR、运行前缀和入口属性在交换机检查器中配置。

### 4.2 连线语义

- 交换机端口不编号、不限数量，只表达网络成员关系。
- 资产连入交换机时自动创建网卡，并建议合法的空闲 host offset。
- 首张网卡默认主网卡；连接更多交换机形成多网卡。
- 路由器连接两个网段后默认双向可达。
- 多网段路由器默认让已连接网段互通，检查器可按网段对关闭关系或调整方向。
- 拖线时只高亮合法目标，非法连接不能落下并显示具体原因。
- 点击连线只显示方向和关系，不暴露 `FromNetworkKey`、`ViaNodeKey` 等字段。

### 4.3 三类关系

- 网络连接：实线，编译为网卡、Infrastructure interface 或 topology connection。
- 启动依赖：虚线，编译为 dependency DAG。
- 运行流量：只读高亮层，来自 traffic flow/path，不写入拓扑。

三类关系使用独立 edge type，不允许互相转换。

### 4.4 渐进式配置

默认配置只展示：

- 名称。
- 镜像。
- CPU、内存和存储。
- 所属交换机和地址。
- 健康检查。

高级配置包含：

- 多网卡、主网卡和路由能力。
- 环境变量和启动命令。
- 依赖条件。
- Bootstrap Profile 和参数。
- 无状态、发布时预制和端点观测。

Bootstrap 不是网络设备。它显示为资产检查器配置和节点标记；依赖关系通过可切换虚线展示。

## 5. 编辑工作台

### 5.1 布局

- 顶部：返回、场景名称、保存状态、撤销、重做、校验、试运行和发布。
- 左侧：可搜索节点库，分类为网络基础设施和计算资产。
- 中央：React Flow 画布、缩放、适配视图、小地图和专注模式。
- 右侧：当前选中对象检查器。
- 底部：校验、操作进度和试运行结果抽屉。

宽屏显示完整工作台；中等宽度将左右面板改为抽屉；手机端只读，不提供拖拽编辑。

### 5.2 草稿、撤销和保存

- 所有文档变更表示为纯命令。
- 拖动过程只更新临时位置，drag stop 才提交历史并触发保存。
- 自动保存使用短延迟合并和 revision 乐观并发。
- 顶部显示未保存、保存中、已保存和冲突。
- 冲突时保留本地快照，禁止静默覆盖。
- 撤销和重做只作用于草稿，不修改 Release。
- 删除节点是原子命令，先显示将被移除的网卡、连接和依赖。

### 5.3 快捷键

- `Ctrl/Cmd + Z`：撤销。
- `Ctrl/Cmd + Shift + Z` 或 `Ctrl + Y`：重做。
- `Ctrl/Cmd + C / V`：复制和粘贴节点及内部关系。
- `Ctrl/Cmd + D`：快速复制。
- `Delete / Backspace`：删除。
- `Ctrl/Cmd + A`：全选画布对象。
- `Ctrl/Cmd + S`：立即保存。
- 方向键微调位置，`Shift` 加速移动。

输入框聚焦时不拦截文本编辑快捷键。复制操作生成新 key 和偏移坐标，只保留复制集合内部的合法关系。

### 5.4 校验定位

前端仅做即时结构提示，服务端 `validate` 是权威结果。每条校验问题必须映射到节点、连线或字段；点击问题后定位、选中并打开对应检查器。

## 6. 视觉与动效

- 使用 vNext 全局 token，不创建 TeamLab 私有主题。
- TSX 只导入 CSS Module；第三方 React Flow 基础 CSS 在应用入口引入一次。
- 节点使用中性表面，以语义色区分交换机、路由器、Docker、Linux VM、Windows VM和异常。
- 不使用渐变背景、装饰光球、卡片嵌套或营销式大标题。
- 拖入、吸附、连线、选中和面板切换使用短时淡入与位移。
- 部署动画只跟随真实阶段，不运行伪进度条。
- 大型场景不动画化全部边，只高亮选中关系或有限流量窗口。
- 支持 `prefers-reduced-motion`，状态始终同时使用文字或图标。

所有图标按钮使用 Lucide 图标、tooltip 和无障碍名称。

## 7. 发布与试运行

### 7.1 发布门禁

- 发布前必须通过服务端结构、地址、接口、镜像能力、依赖、路由和资源计划校验。
- 校验失败不能发布。
- 试运行是可选的高成本验证，不强制每次小改动执行。
- 发布生成不可变 Release；草稿后续变化不影响已绑定比赛。

### 7.2 试运行

试运行创建一个临时 runtime，展示：

- 排队与资源预留。
- 镜像就绪。
- 网络创建。
- 资产创建。
- 健康检查。
- 访问开放。
- 清理。

试运行完成后自动提交销毁。失败时保留 operation、runtime events 和日志引用，不保留无法识别的临时前端状态。

### 7.3 Release 页面

展示版本、content hash、资源计划、能力要求、镜像就绪摘要、试运行记录和比赛引用。已被比赛使用的 Release 不因草稿更新而替换。

## 8. 运行控制与可观测性

### 8.1 运行页

- 总览：环境数、就绪率、部署中、失败、资源占用和节点健康摘要。
- 实例列表：按状态、节点、比赛、队伍和失败阶段筛选。
- 实例详情：阶段时间线、generation、shard、network、asset、access grant 和错误。
- 只读拓扑：复用编辑画布的 node/edge renderer，叠加资产状态、分片边界和流量路径。

### 8.2 管理操作

前端仅展示 capabilities 允许的动作：

- 部署。
- 重置。
- 销毁。
- 关闭或开放访问。
- 重新签发访问。
- 启停 PCAP。

当前底座没有可靠通用暂停/恢复契约，因此首版不提供虚假暂停按钮。未来能力出现后通过 adapter 和 capability 增量接入。

### 8.3 日志与任务

- TeamLab 页面使用 correlation ID、runtime ID、release ID 和队伍过滤系统日志与部署队列。
- 日志复用现有 SignalR 流；runtime 事件使用增量 cursor 轮询，运行期间短间隔、终态低频或停止。
- 节点、镜像和部署任务跳转现有管理详情，不复制页面。
- TeamLab feature 只使用共享查询契约，不导入其他 feature 的页面组件。

### 8.4 流量与 PCAP

- 流量摘要按时间、网段、资产、协议和数据量筛选。
- 路径视图展示 A→B→C 等 observation hop 和证据置信度。
- PCAP 操作明确范围、时间、大小、过期时间和审计状态。
- 下载保持流式，不把 PCAP 载入浏览器内存进行二次组装。

## 9. 比赛生命周期

### 9.1 绑定与准备

- 比赛管理员选择已发布 Release。
- 目标绑定到场景资产。
- 比赛 overlay 只允许声明过的运行时变量和 secret。
- 保存后执行能力预检和镜像就绪检查。
- 管理员设置提前部署时间。

### 9.2 提前部署

- 后台任务为已确认队伍创建环境，比赛开始前保持访问关闭。
- 比赛开始只开放访问，不集中创建全部环境。
- 新增队伍创建增量目标，不重跑全部批次。
- 页面展示目标数、就绪数、失败数、当前阶段和剩余任务数。

### 9.3 结束清理

- 比赛结束先撤销访问，再批量销毁。
- 延期必须在结束前显式修改，不能依赖页面常驻。
- CleanupPending 仍保持比赛、队伍和 runtime 关联，直到事实清理完成。

## 10. 权限

采用普通 CTF 与 TeamLab 共用的简单所有者模型：

- Teacher 可创建比赛和场景，并完整管理自己创建的资源。
- `Role >= Admin` 可管理全部资源。
- 比赛绑定、提前部署和队伍运行操作继承比赛管理权。
- 选手只能访问自己队伍的运行环境。
- 外部 API Token 继续同时受 scope 和资源所有权约束。

不在 TeamLab 内新增独立角色、权限组或授权页面。后端必须抽取共用 owner-or-admin guard，并同时修正普通 CTF 和 TeamLab 当前不一致的判断。

## 11. 前端模块结构

```text
src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/
  api/
  model/
  library/
  editor/
    canvas/
    nodes/
    edges/
    palette/
    inspector/
    validation/
    state/
  releases/
  runtimes/
  shared/
```

规则：

- 页面只组合视图，不直接访问 API。
- `api` 处理传输契约和稳定错误。
- `model` 处理编辑命令、API 映射和拓扑编译。
- 每个节点、检查器、抽屉和表单为独立文件。
- 样式与组件同目录，全部值来自 vNext token。
- 不新增全局状态库；画布、文档、选择和异步保存状态局部化。
- 赛事 TeamLab 页面位于 games feature，通过赛事 adapter 调用，不导入 TeamLab 管理页面。

## 12. 管理控制面补充

### 12.1 编辑器元数据

为 `TeamLabTopologyEditorModel` 增加 Infrastructure 坐标字典。该字段只影响 UI 布局。

### 12.2 场景列表投影

管理列表增加服务器分页、搜索、所有者、规模、最新 Release、引用数和校验摘要。公开 v1 API 不改变。

### 12.3 管理端试运行

补充 session-authenticated 管理入口：runtime 列表、创建、重置、销毁、access grant 和 traffic path。入口复用现有 application service，不建立第二套运行时逻辑。

### 12.4 Release 就绪摘要

服务器聚合 Plan、所需镜像分发记录和最近试运行；前端不进行跨表 N+1 拼装。

### 12.5 比赛准备计划

在比赛适配层持久化准备时间和批次状态，通过现有 operation、部署队列和 TeamLab application port 驱动。不得在 Controller 内启动不可恢复的 `Task.Run`。

### 12.6 授权统一

抽取 owner-or-admin 管理策略，使用 `Role >= Admin`。TeamLab 底座不直接查询比赛权限；Penetration adapter 负责比赛到 TeamLab 调用身份的映射。

## 13. 性能设计

- 支持底座上限内的 32 个网段、128 个资产和相应关系边。
- React Flow 使用稳定 nodeTypes/edgeTypes、memo 节点和仅可见元素渲染。
- 检查器表单只更新选中对象，不重建全图。
- 拖动期间不保存；停止后只提交一次命令。
- 节点库、运行实例和日志列表使用虚拟化或分页。
- React Flow、流量和 PCAP 视图按路由拆包。
- 首次加载使用骨架；后台刷新保留旧数据，不清空页面或改变列表顺序。
- operation 和 runtime 状态可在刷新后恢复。
- 实施遵循 `react-best-practices` skill 原则、现有 architecture check 和 bundle budget。

## 14. 验收

### 14.1 模型与组件

- 节点创建、连接、复制、删除、撤销和重做。
- topology 编译、方向、依赖、Infrastructure 坐标和引用清理。
- revision 冲突和本地快照保留。
- 合法连接过滤、校验定位和快捷键。

### 14.2 大单元流程

- 创建场景 → 编排 → 校验 → 发布 → 试运行 → 观测 → 销毁。
- 绑定 Release → 预检 → 提前部署 → 开放访问 → 队伍重建 → 结束清理。
- 刷新页面后 operation、部署阶段和清理状态仍正确。

### 14.3 性能与视觉

- 32 网段、128 资产场景执行拖动、缩放、框选和配置检查。
- 明暗主题、宽屏、中等宽度和手机只读视图检查。
- 动画不遮挡状态，不造成大场景全局重绘。

### 14.4 工程门禁

- `pnpm lint:check`
- `pnpm check`
- `pnpm check:architecture`
- 相关 Vitest
- bundle budget
- production build
- `git diff --check`

## 15. 迁移与退出

先完成管理契约和 vNext 闭环，再切换入口。切换后删除：

- 旧 `PenetrationAdminPage`。
- 旧 TeamLab 运行观测组件。
- 重复的旧 API 类型和转换逻辑。
- 只服务旧页面的样式和路由。

不保留隐藏旧入口、双写状态或长期兼容层。普通 CTF 容器、VM、节点调度和部署队列必须通过回归验收，确保 TeamLab 前端接入没有改变共享底座行为。

## 16. 画布优先交互补充

- 节点库采用约 64px 的紧凑图标工具轨，保留点击添加、拖拽添加、悬停名称与完全收起能力；五类固定节点不保留低价值搜索框和说明型大卡片。
- 编辑区填充页面可用高度。顶部仅保留连接模式、文档操作与画布级撤销、缩放、适配、面板开关，不重复节点工具。
- 左键拖动节点移动节点；左键拖动空白背景平移画布；`Shift + 左键拖动空白背景`执行框选。
- 滚轮缩放、中键拖动和空格拖动继续可用。空白背景显示抓手状态，节点保持独立的选中和拖动反馈。
- 该交互只修改 vNext 编辑器视图，不改变 topology 文档、编译器、后端 API 或 TeamLab 数据面。
