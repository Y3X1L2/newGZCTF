# TeamLab 全流程审计记录

更新时间：2026-08-02

本文件记录基于当前工作树源码已证实的问题，不把计划、历史文档或未验证推测写成事实。本轮不实施修复。

## 审计范围

设计、校验、发布、镜像准备、试运行、比赛批量运行、运维访问、流量观测、重置、销毁、镜像回收，以及相应的管理端和开放 API。

## 第一轮已证实问题

### P1 发布页将“可规划”误展示为“可创建试运行”

- 证据：`TeamLabAdminQueryService.GetReleaseReadinessAsync` 统计了镜像的 Ready、Pending、Pulling 和 Failed 数量，但只把“无可调度节点”和规划告警加入 `blockers`；`ReleaseReadinessPanel` 仅按 `readiness.ready` 启用试运行。
- 影响：镜像尚在传输或已失败时，用户仍看到“可创建试运行”，创建后才进入长时间等待或失败。
- 正确方向：将可规划、镜像准备中、可立即启动和准备失败分开表达；试运行以持久化的实际放置计划及其节点镜像状态作为依据。

### P1 发布预热引用永久绑定拓扑，节点缓存不能自然回收

- 证据：`TeamLabReleaseImagePreparationService.QueueAsync` 使用 `ImageDistributionReferenceKey.TeamLabTopology(release.TopologyId)`；`ImageDistributionService.ReconcileReferencesAsync` 只要拓扑行仍存在就保留该引用。
- 影响：已经没有试运行、比赛 rollout 或活动运行时的镜像仍会长期占用节点磁盘。
- 正确方向：由 runtime、活动 rollout 或明确配置的预热保留策略持有引用。发布预热只保留短期 claim，实际运行开始后转交给 runtime/rollout claim。

### P1 创建运行时与创建部署队列票据不是同一原子操作

- 证据：`TeamLabRuntimeOrchestrator.PlanAndEnqueueAsync` 先调用 `planner.CreateAsync` 写入 runtime，再调用 `queue.EnqueueAsync`。复用幂等请求时立即返回，不核对或补建队列票据。
- 影响：在两步之间发生进程中断、取消或 admission 失败时，运行时会永久停留在 Planning/Pending，重复请求无法恢复。
- 正确方向：将 runtime 与对应 ticket 放入同一事务，或采用持久化 outbox；幂等复用时必须验证当前 generation 的票据事实并补偿缺失任务。

### P1 运行页没有投影统一队列事实，部署阶段可以彼此覆盖

- 证据：`TeamLabRuntimeProjectionService.GetAsync` 不返回当前 `DeploymentQueueTicket`；`TeamLabShardDeploymentService` 启动镜像确保任务后立即更新为网络阶段，而镜像任务仍可能执行。
- 影响：界面可能将网络、节点、镜像和资源状态展示为互相矛盾的阶段，运营人员无法确认正在等待什么、能否恢复或应查看哪个节点。
- 正确方向：运行时投影关联当前 generation 的 operation/ticket，明确返回队列位置、阶段、失败码和子任务；镜像准备、网络部署和资产启动以独立子阶段表达。

### P1 对外可见的 Stopped 状态没有对应暂停/恢复闭环

- 证据：领域枚举和前端展示 `Stopped`，但当前没有暂停/恢复 API、队列操作或界面控制入口。
- 影响：比赛运营无法暂时关停并保留现场；状态枚举会误导使用者和维护者。
- 正确方向：实现经统一队列、可审计、可恢复的暂停/恢复；若近期不提供该能力，应移除该状态和相关 UI 语义。

### P2 管理端控制操作没有统一进入 Operation 契约

- 证据：管理端 reset、destroy、start/stop capture 直接调用应用服务，没有开放 API 所要求的 `Idempotency-Key` 和 `ApiOperation` 生命周期。
- 影响：同一控制能力在管理端和开放 API 的审计、幂等和错误反馈行为不同。
- 正确方向：管理端同样提交 Operation，由页面观察 operation/ticket，而不是在 Controller 直接执行业务控制。

## 第二轮审计清单

1. 用户任务与权限边界：超管、管理员、比赛所有者、被授权运维人员、选手和自动化令牌。
2. 生命周期：设计、校验、发布、预热、试运行、rollout、暂停、恢复、销毁和归档。
3. 前后端契约：操作、幂等、授权、审计、实时进度和失败恢复。
4. 运行与运维：模板访问配置、节点分发、目标可达性、SSH/RDP、会话回收、日志、流量和抓包。
5. 产品体验：术语、中文、说明、列表、筛选、分页、空态、错误态、响应式与键盘操作。
6. 架构收敛：删除旧错误兼容路径，避免为边缘情况增加第二套控制面或状态源。

## 第二轮已证实问题

### P1 被单独授予资产查看/操作权限的人员无法完成完整运维任务

- 证据：`TeamLabRemoteAccessAuthorizationService` 对非所有者可从 provider 获得 `ViewAssets` 或 `OperateAssets`，`TeamLabRemoteAccessService` 也按该权限创建会话；但运行时详情 API `TeamLabAdminRuntimeController.Get` 使用 `TeamLabAuthorizationService.RequireRuntimeManagerAsync`。运行时列表同样只返回拓扑所有者或管理员的数据。
- 影响：产品已经存在“单独授予运维资产访问”的权限概念，但被授予人员通常不能读取运行时详情、资产列表、状态、错误和日志，也无法自然进入前端资产运维页面。权限授予在后端会话接口和实际用户任务之间断裂。
- 正确方向：明确三层权限并统一使用：查看运行事实、查看资产运维、操作资产运维。详情页与只读运行事实按第一层授权；SSH/RDP/终端按第二、三层授权；重置、销毁、开放选手入口等生命周期控制仍只给运行时管理者。

### P1 预热/镜像传输进度被单一部署阶段覆盖，无法解释实际等待原因

- 证据：`TeamLabShardDeploymentService` 在创建 `PrepareImageAsync` 任务后立即设置为网络部署阶段；镜像准备任务只在后续执行资产节点时等待。`TeamLabDeploymentStageMachine` 为每个 ticket 只保存一个 `Stage` 和一条 `StageMessage`。
- 影响：镜像下载、网络建立和资产启动并行时，前端只能看到最后一次写入的阶段，无法说明“哪个镜像正在到哪个节点、是否可以继续、失败是否可恢复”。
- 正确方向：不增加第二套队列；为同一 ticket 增加结构化子任务进度，或直接将现有分发记录按 runtime/ticket 关联后投影给前端。页面展示独立的镜像准备、网络、资产和探测区域。

### P2 场景编辑器把面向产品用户的概念直接暴露为内部术语和技术字段

- 证据：`NetworkInterfacesEditor` 提供“主机偏移”“排序”两个可编辑数值，但没有解释地址含义、保留地址、何时应修改；`AssetInspector` 直接暴露 `Digest`、`Bootstrap`、`Profile ID`、`endpoint observation`、`bakeAtPublish` 等概念。
- 影响：用户可以创建图，但难以判断输入什么、平台会如何执行、失败后要改哪里。中英混杂和无说明会直接提高场景配置错误率，不是单纯视觉问题。
- 正确方向：将基础编排和专家配置分层。常规用户只配置资产、网络、连接、启动入口和健康检查；地址偏移、资产排序、镜像 digest、服务注入标识和观测策略进入明确标注影响与默认值的高级区。展示文字统一中文，保留 SSH、RDP、HTTP 等行业缩写时附中文用途说明。

### P2 运行时列表把内部状态码作为主要信息展示，且缺少可读的运行名称

- 证据：`TeamLabRuntimesPage` 将 `runtime.stage` 直接放在实例主列；该值由 `TeamLabAdminQueryService.Stage` 返回 `pending`、`queued`、`deploying` 等内部枚举字符串。列表没有场景名称、试运行用途或明确的当前操作说明。
- 影响：运营人员面对多个试运行时只能看 GUID 和英文状态码，无法快速定位“哪个版本、谁创建、正在做什么、为什么失败”。
- 正确方向：列表应优先显示场景名、发布版本、创建者、当前阶段中文标签、失败摘要和进入详情；内部 ID 放入次级信息或复制操作，不作为主体。

### P2 场景生命周期没有归档语义

- 证据：当前 TeamLab 的场景查询只按草稿、已发布、运行、失败过滤；未发现场景归档状态、归档入口或归档后的预热/权限/列表行为。
- 影响：历史演练场景、已结束比赛场景与活跃可创建场景混在同一对象模型中。删除受 release 限制时，运营端缺少安全下线方式。
- 正确方向：增加最小归档状态而非复制场景。归档后禁止新建试运行/新 rollout，保留 release、审计和历史运行读取；释放非活动预热引用，并提供明确恢复或永久删除规则。

### P1 校验和运行失败信息没有中文展示契约

- 证据：TeamLab 应用层大量 `TeamLabApiContractException` 直接携带英文 message，例如 `TeamLabRuntimePlanner` 的地址池耗尽、host offset 越界，`TeamLabRemoteAccessService` 的会话限制与连接失败，`TeamLabRolloutApplicationService` 的发布控制失败。前端统一通过 `errorMessage` 展示接口错误，没有一层按错误码转换为中文的 TeamLab adapter。
- 影响：用户已明确要求校验完全中文，但发布、试运行、运维和流量操作仍会直接看到英文技术错误。更严重的是同一错误在管理端与开放 API 的表达没有稳定区分。
- 正确方向：API 保留稳定 machine code 和英文开发者说明；管理端按 code 映射为中文操作文案，并在可恢复错误中提供下一步。校验结果应返回字段/对象定位和中文原因，而不依赖异常字符串。

### P2 试运行列表缺少运营定位所需的上下文

- 证据：`TeamLabAdminQueryService.ListTrialRuntimesAsync` 只投影 releaseId、状态、时间和错误；`TeamLabRuntimesPage` 主列展示内部 stage 与 GUID。
- 影响：试运行积累后，管理者无法按场景名称、发布版本、人、当前节点、失败原因或访问状态快速定位；只能逐个进入详情页排查。
- 正确方向：保持游标分页，但为摘要加入场景名称、发布版本号、创建者、当前 ticket/阶段、失败摘要和入口状态；提供状态、版本、创建者与失败状态筛选。

### P1 比赛 rollout 目标失败后没有可达的恢复状态转换

- 证据：`TeamLabRolloutCoordinator` 将 provision 异常目标改为 `Failed`，并把 rollout 改为 `Blocked`；后续 provision 批次只选择 `Pending` 或 `Provisioning` 目标。`RequestPreparationAsync` 可以重新将 rollout 标为 Preparing，但不会把 Failed target 转回可 provision 状态。
- 影响：单队运行环境部署失败后，管理员即使修复镜像、节点容量或网络，也不能通过现有 rollout 控制恢复该队；rollout 会永久卡在 Blocked，文案却要求“rebuild or clean”。
- 正确方向：明确且唯一的恢复动作，例如“重建失败目标”或“重建本次 rollout”。该动作必须创建新的 Operation、保留失败证据、先清理旧 generation，再将目标转换为 Pending；不能让通用的 prepare 操作隐式改变失败状态。

## 第二轮审计结论

### 已确认的边界

- 静态模板运维账号配置、远程会话授权、会话时限、节点并发限制和结束回收已有实现基础。
- 镜像分发 worker 按节点和镜像类型使用既有 `NodeDispatchLimiter` 限流，不需要为 TeamLab 再建立一套传输队列。
- 旧 VM 工厂不再是 TeamLab 运行主链路；Agent 中的旧配置删除是升级清理，不构成运行时双轨。
- 场景编辑器已有画布连线、自动排版、撤销/复制快捷键、基础分页和无障碍标签的实现基础。

### 尚不能作为完成依据的验收

- 双 Worker 并发处理同一 rollout 的故障注入。
- 多比赛、多队伍批量 rollout 下的容量、公平性、镜像传输和销毁压力验证。
- 节点离线、镜像 Registry 不可达、主站重启和 Agent 重启后的完整恢复演练。
- 被单独授予资产查看或操作权限的用户，从入口、详情、会话到审计记录的真实浏览器验收。
- 390、1366、1920、2560 宽度下复杂拓扑、长错误、长资产名、长日志和筛选分页的视觉验收。

## 第二轮新增记录：产品可用性与底座闭环

以下条目来自当前工作树源码与管理端前端，不包含基于截图的臆测。截图只用于补充真实使用场景：大型工控拓扑需要同时看清分区、设备关系和所选资产的配置，而不是仅能把节点放上画布。

### P1 获得“查看资产”或“操作资产”授权的运维人员没有完整、可发现的进入路径

- 证据：`TeamLabRemoteAccessAuthorizationService` 和 `PenetrationTeamLabRemoteAccessAuthorizationProvider` 已经表达 `ViewAssets`、`OperateAssets` 两类细粒度授权；`TeamLabAdminRemoteAccessController` 也按该授权创建远程会话。反之，`TeamLabAdminRuntimeController.List` 只查询管理员或场景所有者，`Get`、日志、事件、流量、抓包和 WireGuard 授权均调用 `RequireRuntimeManagerAsync`。`TeamLabRuntimesPage` 又以该列表作为详情和“资产运维”入口。
- 用户影响：被单独授予某比赛运维资产访问权的人，可能在知道 asset/runtime ID 时调用远程会话接口，却不能从产品界面找到运行实例、查看状态与失败原因、确认目标资产是否已启动，也无法将远程操作和事件/流量证据关联。这不是“权限更严”的问题，而是同一已授予职责在产品路径中断裂。
- 正确方向：明确且复用三层能力：`查看运行状态`、`查看资产运维信息`、`执行资产运维操作`。运行时列表和详情按第一层过滤；远程可用性和只读资产信息按第二层；SSH/RDP/终端会话按第三层。重置、销毁、开放选手入口和比赛 rollout 仍只允许运行管理者。不得为此新增第二套授权表或旁路 Controller。

### P1 管理端的长时间控制操作绕过统一 Operation 契约，浏览器重试会产生与开放 API 不同的行为

- 证据：`TeamLabAdminRuntimeController` 的试运行创建要求幂等键，但直接调用 `PlanAndEnqueueAsync`；reset、destroy、WireGuard 授权、抓包启停均直接调用应用服务，除试运行外没有 `Idempotency-Key` 和 `ApiOperation`。同一能力在 `OpenTeamLabRuntimesController`、`OpenTeamLabTrafficController` 中已通过 `TeamLabRuntimeOperationApplicationService` 提交 Operation。
- 用户影响：管理端请求超时、刷新或双击后，产品无法提供统一的“该操作是否已经受理、当前进度、可否安全再次提交”答案；审计、失败状态和开放 API 使用方也会出现两套语义。对比赛环境的销毁、重置、抓包这类不可逆或高成本动作尤其危险。
- 正确方向：管理端和开放 API 共用同一个 command/operation 入口；前端只观察 operation 与关联 deployment ticket。HTTP 身份认证和 API token 可以不同，但业务幂等、审计、进度、失败码和恢复动作不能不同。

### P1 单次运行的“事件、日志、流量、抓包”没有统一的运行批次上下文，排障无法闭环

- 证据：运行投影仅返回 runtime、分片、网络和资产，未带关联 ticket/operation。`RuntimeEventPanel` 仅展示最新 30 条；`useRuntimeLogs` 固定读取前 100 条且没有游标；流量页面有独立游标和 generation 过滤，但日志页面没有对 generation、部署 ticket、资产或分片的可见关联。前端把运行事件、日志、流量分成标签，却没有从失败资产或部署阶段跳到对应证据。
- 用户影响：大型场景下，管理员看见“部署中/失败”时无法回答是哪个发布版本、哪个运行批次、哪个节点、哪个资产、哪一步导致失败，只能在不同页面手工猜测。日志数量一旦超过 100，早期的关键失败还会从管理端消失。
- 正确方向：以 runtime generation 为单一排障上下文，在投影中关联当前 Operation 和 DeploymentQueueTicket；事件、日志、流量、抓包均可按 generation、阶段、分片、资产筛选，并保留游标分页。界面保留独立视图，但需要从运行总览和失败资产一键带入筛选条件。不要复制日志或建立第二套监控存储。

### P1 画布已具备框选样式，但实际禁用了拖拽框选，和大型拓扑的核心任务相冲突

- 证据：`TeamLabCanvas.tsx` 设置了 `multiSelectionKeyCode`、`selectionKeyCode`，CSS 也定义了 `.react-flow__selection` 的框选样式；但 React Flow 同时被明确配置为 `selectionOnDrag={false}`，因此用户无法直接拖拽框选多个资产。现有多选只能依赖辅助键逐一点击或“全选”，不适合工控等密集分区场景。
- 用户影响：无法以最常见方式选择一个生产线/安全区中的多个设备进行移动、复制、删除或检查；用户会把鼠标拖拽误认为画布移动，操作成本随节点数线性上升。
- 正确方向：以“空白处拖动框选，拖到节点时移动节点，Space/中键拖动画布”为明确交互契约，并使 React Flow 配置与其一致。保留已有快捷键和自动排版，不增加另一套选择工具。

### P1 配置检查器缺少独立滚动容器，复杂资产配置会把画布与配置面板一起锁死

- 证据：`TeamLabDesignPage.module.css` 将编辑器页面、workspace 都约束为 `min-height: 0` 和 `overflow: hidden`；`TeamLabDesignPage` 中的右侧 `TeamLabInspector` 是 workspace 的固定 aside。当前样式没有为该 aside/检查器建立 `overflow-y: auto` 的滚动上下文。资产检查器包含镜像、网络接口、资源、服务注入、健康检查、观测和高级配置等多个可展开区域。
- 用户影响：用户在大型资产配置中不能稳定滚动到下方设置，或只能通过整页滚动导致画布失去可用高度。截图反映的“右侧鼠标无法滚动”符合这一代码路径。
- 正确方向：画布保持独立可平移缩放，左侧资产库和右侧检查器各自建立可滚动容器，标题与主要动作可固定。该修复是现有三栏布局的职责补全，不需要增加抽屉、浮窗或第二个编辑页面。

### P1 服务注入、高级配置和观测策略直接暴露内部实现字段，配置行为不可预测

- 证据：`AssetInspector.tsx` 将 Bootstrap、Profile ID、镜像 digest、endpoint observation、`bakeAtPublish` 等内部概念直接放到资产配置；`NetworkInterfacesEditor.tsx` 直接要求填写“主机偏移”“排序”。尽管代码已有 `AdvancedSection`，但基础流程没有先把“用户需要决定什么、平台自动决定什么、什么时候需要高级配置”表达清楚。验证错误和运行错误仍大量直接传递英文 `TeamLabApiContractException` message。
- 用户影响：非底座开发者无法区分“必要配置”“可选优化”“仅构建场景时使用的参数”，容易把内部标识当成业务输入。错误后也无法判断应改连接、镜像、节点容量还是服务注入。
- 正确方向：以用户目标分层：基础层只配置资产、镜像、网络连接、启动入口与健康检查；高级层才显示地址偏移、优先级、注入模板、构建时预制和观测策略，并为每项写明默认行为、影响范围及何时启用。内部 ID/digest 只读且不作为主要表单字段。中文界面以稳定错误码映射中文说明和下一步，开放 API 继续提供机器码和开发者说明。

### P2 运行日志和事件视图不具备大型场景的可检索、可回溯能力

- 证据：`useRuntimeLogs.ts` 把 `logLimit` 固定为 100 且永远传递 `after = null`；`RuntimeLogPanel.tsx` 在浏览器侧排序该有限集合。`RuntimeEventPanel.tsx` 同样在前端截断为 30 条，未提供“更多”或历史游标。流量视图已正确使用游标分页，形成明显不一致。
- 用户影响：长时间部署或比赛运行后，用户无法回看最早的失败、节点离线或镜像分发事件；通过关键字筛选也只能检索当前 100 条，不满足运营排障需求。
- 正确方向：复用已有日志时间游标与运行事件游标，统一提供服务端筛选和“上一页/下一页”，默认显示最近页并明确“已加载范围”。不应把整段日志拉到浏览器或引入新日志系统。

### P2 运行时、发布版本和设计界面仍以内部英文状态码和标识符为主要信息

- 证据：`TeamLabAdminQueryService.Stage` 和 `TeamLabRuntimeProjectionService.Stage` 返回 `pending`、`planning`、`queued`、`deploying` 等字符串；`TeamLabRuntimesPage.tsx` 将其与 GUID 置于主列；运行详情顶部显示 release GUID。页面 eyebrow 同时出现 `RUNTIME CONTROL`、`TRIAL RUNTIMES`、`FLOW METADATA`、`PERSISTED EVENTS` 等英文。
- 用户影响：运营人员无法快速知道“哪个场景、哪个发布版本、谁创建、正在做什么、为什么失败”；中英混杂也违背已提出的中文校验与可读性要求。
- 正确方向：API 保留稳定枚举和 ID；管理端统一通过 presentation adapter 映射为中文阶段、场景名称、发布版本号、创建者、失败摘要和可执行下一步。英文术语只保留 SSH、RDP、HTTP、TCP/UDP 等行业缩写，并辅以中文用途。

### P2 远程运维面板对每个运行资产并发发起可用性请求，失败时静默清空状态

- 证据：`RuntimeRemoteAccessPanel.tsx` 在每次 runtime/generation/asset 数组变化后对所有运行资产执行 `Promise.all(getAvailability)`；任一请求失败即将整个 `availability` 重置为空 map。该请求量随大型场景资产数量增长，且用户无法分辨“不可用”“仍在检查”“权限不足”“网络请求失败”。
- 用户影响：几十个资产会造成瞬时请求峰值；一个短暂错误就隐藏全部资产的可用性说明，用户只能看到不可靠的“进入运维”按钮或空状态。
- 正确方向：由运行详情投影一次返回已授权资产的可用性摘要，或采用受限并发的逐项请求并保留单项失败状态；将检查中、不可用及其中文原因明确显示。不得让浏览器以未受限 fan-out 充当调度器。

### P2 API 底座只覆盖单个 runtime 的通用生命周期，比赛 rollout 的状态与恢复能力没有等价的开放契约

- 证据：`OpenTeamLabRuntimesController` 覆盖创建、读取、重置、销毁、选手 WireGuard 和流量/抓包；比赛按队伍批量创建、预热、开放入口、drain、失败目标重建由 `PenetrationTeamLabAdapter` 与 `TeamLabRolloutApplicationService` 承担，当前未发现等价的 `api/open/v1` rollout 控制器或契约。
- 用户影响：第三方业务模块若要将 TeamLab 当作底座，只能使用单 runtime 接口，无法以相同契约驱动“一个场景在多个主体上预热、开放、结束、释放”的核心运营流程，最终会绕过模块或重复编排。
- 正确方向：把 rollout 视为 TeamLab 底座的一等 application contract，暴露最小的创建/查询/准备/开放/结束/失败目标重建与 Operation 查询能力；比赛模块只提供目标主体和自身权限适配，不复制 TeamLab 生命周期。此项是架构能力缺口，后续独立设计，不能先塞进管理端 Controller。

### P1 TeamLab 管理查询直接依赖 Penetration 数据表，底座已出现反向业务耦合

- 证据：`TeamLabAdminQueryService` 直接查询 `PenetrationGameLabBindings` 和 `PenetrationTeamRuntimeBindings`，以统计比赛引用并从场景摘要、运行时列表、发布就绪度中排除比赛运行时。`Penetration` 模块已经通过 `ITeamLabRolloutTargetProvider`、`ITeamLabRuntimeManagerAuthorizationProvider` 和远程运维授权 provider 向 TeamLab 提供适配能力，但 TeamLab 反向穿透了 Penetration 的 EF 实体。
- 用户影响：新业务模块接入 TeamLab 时，场景/运行时的分类、列表和删除规则不能自然复用，必须修改 TeamLab 查询并理解比赛表结构；TeamLab 也无法作为独立底座部署或演进，模块边界被破坏。
- 正确方向：由 TeamLab 在 runtime/release/rollout 中表达通用的 `owner kind`、`external reference` 或 runtime usage contract；外部模块通过公开 query provider 返回“引用摘要/可见性”，而不是让 TeamLab 读取外部 DbSet。比赛模块继续拥有比赛、队伍、参赛资格和权限组事实。

### P1 多 Worker 下 rollout 缺少按 rollout 的独占处理，可能重复编排同一批目标

- 证据：`TeamLabRolloutCoordinator.ProcessBatchAsync` 仅查询需要处理的 rollout ID 后直接进入 `ProcessOneAsync`；没有使用 `IDistributedLeaseProvider` 或数据库 claim。`ProcessOneAsync` 会挑选 `Pending/Provisioning` target，逐个将其改为 `Provisioning` 后调用 provider 创建运行时。`TeamLabRollout.Revision` 是并发令牌，但 `TeamLabRolloutTarget` 没有独立并发 claim，且开始 provision 前没有原子地取得目标所有权。
- 用户影响：高峰期部署或服务横向扩容时，两个 Worker 可以同时读到同一 rollout/target，并分别发起创建、镜像准备或销毁请求。即使后续数据库并发异常能够记录，也已经可能产生重复运行时、重复容量预留或难以解释的队列任务。
- 正确方向：复用已有分布式租约，以 rollout public ID 为锁粒度将一次协调过程串行化；或在数据库中对 target 做原子 claim。二选一即可，优先租约，因为同一 rollout 本身已是统一状态机。保留目标批次并发，不为每个 target 再建立额外队列。

### P1 比赛端 prepare/open/close/drain/rebuild/cleanup 与 TeamLab Operation 及统一队列观察脱节

- 证据：`PenetrationAdminController` 的 `/deploy`、`/stop`、`/teamlab/prepare`、访问开关、drain、单队重建和清理直接调用 `PenetrationTeamLabAdapter`，只返回即时 `TeamLabRolloutModel` 或 `RequestResponse`。这些接口没有幂等键、`ApiOperation`、统一 operation URL 或结构化失败码；`ExecuteTeamLabAsync` 又将 `TeamLabApiContractException.Message` 直接包装成字符串响应。
- 用户影响：比赛所有者无法可靠区分“请求已受理但还在进行”“网络请求重试”“本次状态改变失败”；前端也只能轮询游戏绑定/目标列表，不能用一条可审计操作追踪发布、预热、开放、结束及其失败恢复。
- 正确方向：Penetration Controller 只完成比赛 ownership 授权和“将比赛主体适配成 rollout target provider”；随后提交 TeamLab rollout Operation。所有长期动作返回同一种 operation reference，前端用其与 rollout target 分页状态观察进度。避免把 rollout 再包装成 Penetration 专属队列。

### P2 选手侧只有 WireGuard 配置领取入口，缺少可解释的环境状态投影

- 证据：`PenetrationPlayerController` 在环境入口关闭时返回“环境未向选手开放”，在 workspace 不存在时返回“环境未部署”；玩家可创建并下载 WireGuard 配置。当前审计范围未发现将 rollout target 的准备、运行、失败、关闭、销毁状态转换为选手可理解中文状态的 player-facing contract。
- 用户影响：比赛开始后，选手面对“配置领取失败”“环境未部署”无法知道是等待镜像传输、该队环境仍在创建、比赛尚未开放，还是该队部署失败；运营人员也会收到大量无法自助定位的咨询。
- 正确方向：由 Penetration 使用 TeamLab rollout/runtime 投影提供只读、去敏的选手环境状态：未开放、准备中、可连接、暂不可用、已结束，并在失败时给出不暴露节点和内部网络的中文提示。选手不获得资产、日志、流量或运维能力。

## 第三轮新增记录：大型场景编排与外部底座能力

### P1 网段不是画布中的一等可视化区域，大型拓扑无法按网络分区阅读

- 证据：后端定义有 `TeamLabTopologyNetworkModel`，编辑器持久化模型也有 `TeamLabTopologyEditorModel.Networks` 的位置属性；但 `TeamLabCanvas.tsx` 只将 switch、router、Docker、Linux VM、Windows VM 映射为 React Flow node。`TopologyNodeShell` 和 `NetworkEdge` 仅绘制单个节点及连线，没有将某网段渲染成可命名、可折叠、可框选的区域，也没有把接口成员关系投影为“资产属于哪个区域”。
- 用户影响：多网段的工控、办公网、生产网、安全区、管理区场景在画布上退化为散点和交叉连线。用户不能先看清网络分区，再看区内设备、交换机和跨区路由，阅读和维护成本会在几十个资产后急剧上升。
- 正确方向：将现有 network key 作为唯一事实，不新增平行网络模型；在编辑器中把每个网段渲染为可命名区域，区域内呈现相连资产与交换机，路由器和跨网段链路位于区域边界。区域的坐标、大小、折叠状态写入既有 `TeamLabTopologyEditorModel.Networks`。自动排版应先排区域、再排区域内节点、最后排跨区域连线。运行语义仍以拓扑定义和连接关系为准，区域仅是可持久化的编排视图。

### P1 画布缺少大型场景最小操作集，已有快捷键不足以完成分区编排

- 证据：`TeamLabCanvas.tsx` 已具备拖拽节点、缩放、平移、连线、自动排版、缩略图和撤销/复制；但设置 `selectionOnDrag={false}`，没有套索/框选，没有选择网段区域，没有“聚焦某网段/仅显示相邻链路”，也没有多选后的批量移动/归属提示。`TeamLabInspector` 多选后仅显示节点数、连接数及内部 key 列表。
- 用户影响：用户无法以“选择一个生产区，检查它与安全区之间的路由，再调整一组设备”的方式工作；即使框选问题单独修复，也仍缺少对区域、连接关系和选中集合的理解反馈。
- 正确方向：以最小交互闭环补齐，而非增加工具栏堆砌：空白拖动框选，点击区域选中该网段资产，双击区域聚焦，画布工具栏提供“全部视图/当前区域/自动排版”。多选检查器显示共同网段、跨网段连接数、资源总量和冲突提示，但不在第一版引入批量修改资源、镜像或注入配置。这样可覆盖阅读、选择、移动和排障四个高频任务。

### P1 服务注入没有面向用户的发现、选择和解释链路，当前只是 Profile ID 输入框

- 证据：`BootstrapEditor.tsx` 的“服务注入”启用后直接要求输入 `Profile ID`、版本和键值参数；没有 profile 目录查询、用途摘要、支持的镜像/系统、公开参数说明、示例配置或“何时使用”的帮助入口。开放拓扑 contract 也只是 `TeamLabBootstrapReferenceModel` 数据引用。
- 用户影响：用户既不知道可选服务包有哪些，也不知道服务包会在何时执行、是否改变镜像、哪些参数是必填、发生失败应看哪里。对于 AD、工控协议服务、日志代理等使用场景，这会把“减少改镜像成本”的能力变成高门槛内部配置。
- 正确方向：服务注入保持“引用已签名 profile + 参数”的简单底层模型，但提供 profile catalog query contract；编辑器以可搜索选择器显示名称、用途、支持范围、版本、公开参数和最小示例。每个高级项提供可点击的中文说明，说明默认行为、影响阶段、适用场景和失败证据入口。文档和 UI 共用同一 profile 元数据，不能维护两套说明。

### P1 开放拓扑 API 丢失编辑器布局契约，第三方无法保存或复现可读的场景编排

- 证据：内部 `CreateTeamLabTopologyModel`、`UpdateTeamLabTopologyModel` 和 `TeamLabTopologyDetailModel` 含 `TeamLabTopologyEditorModel Editor`；但 `OpenCreateTeamLabTopologyModel` 与 `OpenUpdateTeamLabTopologyModel` 不含 `Editor`，`OpenTeamLabTopologyDetailModel` 也不返回 `Editor`。因此 `/api/open/v1` 客户端只能创建逻辑拓扑，不能读取或写入画布区域、节点位置、折叠状态等编排信息。
- 用户影响：第三方平台、模板库或自动化工具无法生成与管理端一致的场景视图；导入后只能依赖管理端重新排版，无法实现可追溯、可复现的场景资产交付。
- 正确方向：将 `Editor` 作为公开拓扑 contract 的可选、版本化 presentation payload，和逻辑定义一起做乐观并发控制。运行时忽略它，验证器不依赖它，第三方可选择不传。不要把 React Flow、CSS、前端内部类型暴露到 API。

### P1 API 底座缺少镜像准备、分发状态、服务包目录与 rollout 的完整外部契约

- 证据：管理端已有 release readiness 与 `/images/prepare` 操作，内部已有 `TeamLabReleaseImagePreparationService`、`ImageDistributionRecord`、`ITeamLabRolloutApplicationService`；开放 API 仅覆盖拓扑、release、单 runtime、流量和抓包。`TeamLabCapabilitiesModel` 也只能表达总体 feature flag，不能回答某 release 在哪些节点已准备、一个服务注入 profile 是否可用、一个批量 rollout 当前哪些主体成功或失败。
- 用户影响：外部平台无法在创建比赛/课程/演练前主动准备镜像、等待明确进度、发现某模板缺少可调度节点，也不能驱动多主体批量环境生命周期。它只能自行轮询单实例并猜测，失去“底座”价值。
- 正确方向：将底座 API 补为四个清晰资源：场景与发布版本、镜像准备状态、服务注入目录、批量 rollout。每个异步写操作统一返回 Operation；每个查询带稳定 ID、状态、阶段、失败码、可否重试和游标分页。底座不暴露节点私网细节、镜像凭据或平台内部队列表，但可以暴露去敏的节点能力/准备数量和 placement 摘要。

### P2 高级配置缺少一致的“帮助、默认值、示例、影响范围”产品契约

- 证据：`AssetInspector.tsx`、`NetworkInterfacesEditor.tsx`、`BootstrapEditor.tsx`、`ObservationEditor.tsx` 多数只提供标签和输入控件。`InspectorFields` 支持 `hint`，但关键字段没有形成统一的帮助内容模型；页面中还存在 `Profile ID`、`INSPECTOR`、`RUNTIME READINESS` 等直接暴露的英文术语。
- 用户影响：用户需要从字段名反推平台行为，尤其难以理解主机偏移、排序、发布时预制、端点观测、服务注入参数的实际影响。配置错误变成事后校验失败，而不是输入时可理解地避免。
- 正确方向：定义可复用的字段帮助元数据：中文名称、简短解释、默认值、影响阶段、示例、详细说明链接。基础字段默认展开；高级字段默认收起但可直接查看说明。帮助图标只在存在实质说明时出现，避免满屏提示符号；说明内容必须来自 contract/profile metadata 或版本化产品文档，不能散落在组件内。

### P1 Open API 将实际运行失败压缩为通用且固定“不可重试”的错误，自动化无法做正确恢复

- 证据：内部 `TeamLabRuntimeProjectionModel`、分片和资产均保存 `LastError`，队列和 Operation 也有真实失败事实；但 `OpenTeamLabContractMapper.Failure` 对运行、分片、资产、抓包一律生成固定的 `teamlab_runtime_failed`、`teamlab_shard_failed`、`teamlab_asset_failed` 或 `teamlab_capture_failed`，并固定 `Retryable = false`。错误消息没有进入开放失败模型，也没有关联 Operation/ticket。
- 用户影响：第三方无法区分镜像尚在传输、节点容量不足、网络配置错误、来宾探测失败、清理待恢复等不同结果，无法据此正确提示用户或调用明确的恢复操作。将所有失败标为不可重试也会迫使接入方依赖非公开日志或盲目重建。
- 正确方向：公开失败模型应传递稳定的 machine code、阶段、`retryable`、去敏中文/英文开发者说明、关联 operation ID 和可用恢复动作标识。不得暴露节点私网地址、密钥、完整命令和敏感配置。管理端可基于同一 code 映射中文操作建议，不再解析异常文本。

## 第四轮设计自审：外部稳定 API、并发与故障闭环

本节是对拟定改造方向的反向推演，不将尚未实现的机制写成当前事实。目标是避免以后以“补重试、加超时、再建队列”处理本可由清晰边界解决的问题。

### 1. 所有权与权限边界

- 每个公开资源必须归属 `scope`，而不是直接归属内部用户、比赛或课程实体。scope 是 API token 的授权边界；`externalReference` 只是调用方的业务关联键，不能用于授权。
- 所有读取、写入、事件查询、下载和 webhook 管理均先按 scope 查询，再按资源 ID 查询。对不属于 scope 的资源统一返回不可发现结果，避免通过 403/404 差异枚举其他租户资源。
- 外部主体只是 rollout target 的稳定字符串和展示名；它不继承平台用户权限，不可直接获得 SSH/RDP、日志、流量或管理端页面能力。
- 管理端、比赛所有者、被授权运维人员、选手与 API token 使用同一资源授权服务，不能各自解释 ownership。

### 2. 幂等、并发与原子性

- 每个异步 command 都以 `(scope, route, idempotency-key)` 去重，并保存规范化请求体 hash、operation ID、资源版本和终态结果。相同 key + 不同请求体固定返回 `idempotency_conflict`；客户端遇到网络中断只可携带原 key 查询/重发，不能生成新 key 猜测状态。
- topology 更新使用 revision 乐观并发；release 以不可变 source revision 建立。编辑视图与逻辑定义同属 topology revision，但镜像准备缓存键只使用逻辑 release digest，纯布局调整不触发镜像重传。
- runtime、deployment ticket 和 operation job 必须同事务写入，或由持久化 outbox 保证最终创建。创建命令重放时先检查该 operation 是否已有关联 runtime/ticket，缺失时只补缺失的一项。
- 每个 rollout 用一个既有分布式租约串行协调；租约失效立即停止提交后续 target，不得继续执行。数据库 revision 是第二道防线，不替代租约。
- target 的创建、重建、暂停、恢复、销毁均使用 generation。任何旧 generation 的 Agent 回报、日志、流量、清理都不能覆盖新 generation 状态。

### 3. 批量 rollout 的部分失败语义

- 准备、部署和清理可以按固定批次并行；同一 rollout 不并发协调，同一 target 不并发执行两次。
- 默认策略为“全部目标就绪后才开放选手访问”。少数目标失败时 rollout 进入 `Blocked`，已经就绪的目标保持隔离但不开放；管理员或外部平台只能选择“重建失败主体”“从本次 rollout 移除主体”“结束本次 rollout”之一。
- 不允许通用 prepare 隐式重试失败主体，也不允许自动销毁已就绪主体掩盖部分失败。每个恢复动作生成新 Operation，保留旧失败证据。
- 外部主体同步删除采用两步：先标记“不再期望”，再由调用方明确 drain 或在其声明的结束策略触发清理。不得因为一次临时同步缺失直接销毁现场。

### 4. 暂停、恢复、销毁与资源预留

- pause 只对 `Running` runtime 生效：停止工作负载，但保留网络、磁盘 overlay、地址、generation、访问授权状态和运行事实。pause 命令幂等，重复提交返回同一终态。
- 为保证 resume 可预测，暂停后保留“恢复容量预留”而不是让其他 workload 任意吃掉原有容量；它不占实际 CPU 时间，但在调度账本中占据已承诺的 CPU/内存槽位。否则“暂停成功、恢复失败”会成为常态。
- resume 不重新拉镜像、不重新规划网段、不改变地址；仅在预留存在且节点 inventory 一致时启动工作负载。节点不可用时进入明确 `resume_blocked`，只能迁移/重建或等待节点恢复，不能悄悄换节点。
- destroy/drain 先关闭访问与远程会话，再停止抓包和观测，再删除运行资源，最后释放 capacity reservation 与镜像 claim。每一步可重入，完成项不再重复执行。

### 5. 事件、Webhook 与客户端可观察性

- Operation 是命令事实；runtime/rollout/release 是资源事实；事件只是不可变投影，不能由事件反推资源状态。
- 外部 API 提供游标事件流作为完整可靠路径。webhook 只是通知优化，语义为至少一次、可能重复、不同资源间不保证总顺序；payload 必须含 event ID、resource version、occurredAt、事件类型和去敏资源链接。
- webhook 使用签名、可轮换密钥、HTTPS 校验、投递超时、指数退避和有限保留；订阅地址不得访问内网、环回或 link-local 地址，防止 SSRF。重放按 event ID 范围执行，不重新执行业务命令。
- 管理端和外部客户端展示的进度都来自同一 Operation + ticket + resource projection。不能用浏览器本地定时器猜测完成，也不能以单一 stage 覆盖多个并行子阶段。

### 6. 失败模型、限制与兼容性

- 每个失败必须有：稳定 code、资源类型/ID、阶段、是否可重试、推荐恢复 action、去敏说明、关联 operation ID。日志和诊断只在获得运维权限的读取接口中提供。
- `retryable=true` 只表示相同 command 可以在依赖恢复后再次提交，不表示服务器自动无限重试；自动重试仅限已证明幂等且短暂的基础设施调用，并应受统一策略限制。
- API v1 只允许添加可选字段和新 endpoint；字段不改义、不复用枚举值。不能兼容的 topology schema 由 capabilities 明确拒绝，不以静默降级处理。
- 分页 cursor 固定排序键和 scope；资源在翻页期间变化时允许看到新版本，但每条记录必须带 revision/version，客户端不能假设列表是快照。
- 为每个 scope 施加 topology、资产、并发 rollout、运行时、抓包、事件读取、webhook 订阅的配额和限流。配额错误必须是可识别的 contract failure，不能在队列中无期限等待。

### 7. 外部 API 最小闭环校验

外部平台不访问管理端页面、不持有内部数据库权限的前提下，必须能完成以下完整链路：

1. 查询 capabilities、镜像目录和服务包目录。
2. 创建/更新带可选 editor 布局的场景，处理 revision 冲突，读取中文无关的稳定校验码。
3. 发布不可变版本，查询放置计划和镜像准备状态，等待或订阅准备结果。
4. 创建 rollout，增量同步主体，按 Operation 观察批量部署和单主体失败。
5. 按策略开放/关闭主体访问，读取去敏运行状态，创建允许范围内的访问配置。
6. 查询经授权的日志、流量、抓包和运行事件，按 generation/asset/stage/时间游标定位问题。
7. 暂停、恢复、重建失败主体、drain、归档和删除，并确认镜像 claim、会话、抓包、容量和运行资源均已释放。

任一步若需要读取 Penetration 实体、访问 Agent、解析异常文本、刷新管理端页面或猜测队列状态，说明底座契约仍不完整。

## 第二轮权限与状态机事实矩阵

### 已实现的角色边界

| 角色 | 当前能够完成 | 当前断点 |
| --- | --- | --- |
| 超级管理员/管理员 | 查看和管理所有场景、试运行、运行时，创建远程会话，控制访问授权、抓包、重置和销毁。 | 管理端长时间操作未统一进入 Operation，重复点击或网络中断后的状态解释不一致。 |
| 场景所有者 | 管理自己的草稿、发布版本与试运行；对自己运行时具备完整运维能力。 | 比赛绑定后的运行时与纯试运行的入口、恢复语义不同，界面没有显式说明归属和控制边界。 |
| 比赛所有者 | `PenetrationTeamLabAdapter` 作为运行时管理授权 provider，使比赛所有者可管理已绑定的运行时；远程访问 provider 也授予完整资产运维能力。 | 该能力只从比赛模块间接提供，尚无独立 TeamLab rollout contract 供其他业务模块等价接入。 |
| 被授权运维人员 | 后端可按 `ViewAssets` / `OperateAssets` 判断并建立允许的 SSH、RDP 或容器终端会话。 | 不能稳定从运行时列表进入详情、查看部署原因、日志、流量和抓包；无法先观察后操作。 |
| 选手 | 比赛 rollout 打开访问后，可取得自己的环境访问入口；TeamLab 运行资产不直接向选手提供运维账号。 | 当前审计范围内未发现选手侧对“环境准备中、已就绪、已关闭、失败后应联系谁”的统一运行态投影，需要随比赛页验收。 |
| 开放 API token | 可通过 scope 调用拓扑、单运行时、流量与抓包等 Open API，写操作已使用 `Idempotency-Key` 和 `ApiOperation`。 | 没有等价 rollout contract；且接口说明以英文为主，不能直接服务中文运营端。 |

### 生命周期状态机：当前入口和断点

| 阶段 | 当前入口/事实源 | 已确认断点 |
| --- | --- | --- |
| 设计与保存 | 管理端草稿编辑器；Open API 通过 `TopologyCreate/Update` Operation。 | 管理端与开放 API 的异步语义不统一；画布多选与检查器滚动不满足大拓扑编辑。 |
| 校验 | 管理端和 Open API 均可调用拓扑校验。 | 错误码到中文操作说明未闭环；面向用户的字段定位与“如何修复”不足。 |
| 发布 | Open API 发布进入 `TopologyPublish` Operation；管理端可发布不可变 release。 | 发布就绪度将“可放置”混同于“镜像已准备好”，试运行入口可能过早可用。 |
| 镜像准备 | `TeamLabReleaseImagePreparationService` 和既有分发 worker 负责。 | 单 ticket 阶段无法表达传输、网络与资产启动的并行进度；预热引用绑定拓扑导致缓存无法自然回收。 |
| 试运行 | 管理端创建 `TeamLabRuntime` 并入统一部署队列。 | runtime 创建与 ticket 入队不原子；管理端未统一使用 Operation；运行列表缺少可识别上下文。 |
| 正式运行 | 比赛模块通过 rollout 为各队创建并管理运行时。 | 失败 target 进入 `Failed` 后没有显式且可达的“重建失败目标”状态转换；底座 API 缺少 rollout 生命周期。 |
| 暂停与恢复 | `Stopped` 枚举与 UI 状态存在。 | 没有对应的暂停/恢复 command、队列执行、审计或界面入口。 |
| 销毁与清理 | 运行时 destroy 进入统一队列；rollout 也有 drain/cleanup 逻辑。 | 管理端 destroy 不满足同一 Operation/幂等契约；未在运行投影中统一说明清理进度与失败恢复。 |
| 归档 | 未发现场景归档实体状态或入口。 | 历史场景、已结束比赛场景和可继续创建的新场景缺乏安全下线语义。 |

## 后续审计焦点

第三轮只继续核实，不修改实现：

1. TeamLab 与 Penetration 的比赛绑定、权限组、选手入口和 rollout 生命周期是否存在越权、状态漂移或资源回收遗漏。
2. API 底座是否能让外部模块只依赖 contracts/application ports，而不依赖 Penetration 实体、Controller 或管理端 DTO。
3. 大型场景下前端画布、资产列表、远程会话、日志/流量分页和实时刷新是否存在请求风暴、可访问性或状态误导。
4. OpenAPI JSON、中文 HTML 文档和实际 Controller 是否遗漏已交付接口或将内部字段误标为用户输入。
