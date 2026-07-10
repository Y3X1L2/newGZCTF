# Phase 4 攻击图服务深度审查报告
## 一、审查范围
文件 路径 攻击图服务 d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationAttackGraphService.cs 渗透服务 d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationService.cs 玩家控制器 d:\newgz\newGZCTF-main\src\GZCTF\Controllers\PenetrationPlayerController.cs 攻击图模型 d:\newgz\newGZCTF-main\src\GZCTF\Models\Request\Game\PenetrationModels.cs

## 二、架构总览
100 %

## 三、实现完整性核查
### 1. PenetrationAttackGraphService 完整实现 1.1 攻击图构建（PolicyAction == Allow && IsRouteHint == true）
文件 : PenetrationAttackGraphService.cs:38-45

审查结论 : 实现正确。按 Priority 升序、 Id 升序排序，保证多路径场景下边遍历顺序确定。
 1.2 BFS 计算入口最短深度
文件 : PenetrationAttackGraphService.cs:169-200

审查结论 : 算法正确，是标准 BFS 最短路径变体（基于松弛）。

- 入口节点深度为 0
- 使用 saved <= nextDepth 跳过已找到更短或等长路径的节点
- 找到更短路径时会更新并重新入队（正确处理负权边场景，本场景无边权为负）
- 注意 : 同一节点可能被入队多次（发现更短路径时），但 saved <= nextDepth 检查保证终止性 1.3 多路径 OR 解锁逻辑
文件 : PenetrationAttackGraphService.cs:54-74

审查结论 : OR 解锁逻辑正确。

- 任一前置节点完成即可解锁后继（ accessibleNodeIds.Add 是 OR 语义）
- 不要求所有前驱完成，符合"多路径 OR 解锁"设计 1.4 环路通过集合收敛避免递归
审查结论 : 正确。

- 使用 while (changed) 循环 + HashSet.Add 返回值检测变化
- HashSet.Add 对已存在元素返回 false ，保证集合单调增长且收敛
- 环路中节点只会被首次加入时触发 changed = true ，二次访问不再传播
- 无递归调用，无栈溢出风险 1.5 断链节点保持 Hidden
文件 : PenetrationAttackGraphService.cs:220-228

审查结论 : 正确。不可达节点（不在任何集合中）返回 Hidden 。
 1.6 Checkpoint 完成判定
文件 : PenetrationAttackGraphService.cs:202-211

审查结论 : 实现正确，符合需求：

- 有 checkpoint : 仅需完成全部 checkpoint 即算节点完成（ blockers = checkpoints ）
- 无 checkpoint : 需完成全部可见得分项（ blockers = visibleItems ）
- 空节点透传 : visibleItems.Length == 0 时返回 true ，已可达的空节点自动完成，可透传路由到后继 1.7 Hidden 节点信息脱敏
文件 : PenetrationAttackGraphService.cs:94-114

审查结论 : 脱敏完整。Hidden 节点：

- Id = 0 （不泄露真实 ID）
- TopologyKey 替换为 fog-depth-{depth}-{orderIndex} （不泄露真实拓扑键）
- DisplayName = "未知区域"
- Description = null
- ScoreSummary 为空对象
- PositionX/Y 使用基于深度/序号的占位坐标
潜在信息泄露点 : IsEntry = node.IsEntry 和 IsCheckpointCompleted = completedNodeIds.Contains(node.Id) 对 Hidden 节点也会返回真实值。但 Hidden 节点不可能 completed（ completedNodeIds 是 accessibleNodeIds 的子集），所以 IsCheckpointCompleted 恒为 false 。 IsEntry 对 Hidden 节点泄露入口标记，但入口节点默认 Accessible（深度 0），不会是 Hidden，所以实际无泄露。

### 2. 攻击图模型
文件 : PenetrationModels.cs:383-430

模型 行号 字段完整性 PenetrationAttackGraphModel 383-395 GameId/TeamId/PublishedVersion/统计字段/Nodes/Edges ✅ PenetrationAttackNodeModel 397-411 Id/TopologyKey/DisplayName/Description/Depth/Status/ScoreSummary/PositionX/PositionY/IsEntry/IsCheckpointCompleted/RuntimeStatus ✅ PenetrationAttackEdgeModel 423-430 Id/SourceNodeKey/TargetNodeKey/Status/Label ✅ PenetrationFogState 6-12 Hidden=0/Revealed=1/Accessible=2/Completed=3 ✅ PenetrationAttackScoreSummaryModel 413-421 Total/Solved/CheckpointTotal/CheckpointSolved/TotalScore/SolvedScore ✅

审查结论 : 模型定义完整，与需求一致。 PenetrationAttackEdgeModel 使用 SourceNodeKey / TargetNodeKey （拓扑键）而非 Source / Target （ID），与 Hidden 节点脱敏策略一致。

### 3. 缓存策略 3.1 缓存 Key 构成
文件 : PenetrationAttackGraphService.cs:213-218

审查结论 : Key 构成完整，包含：

- GameId / TeamId / PublishedVersion （业务维度）
- Status （环境状态）
- statusStamp （UpdatedAt/CreatedAt 时间戳）
- solvedFingerprint （已解决得分项拓扑键有序拼接） 3.2 TTL 策略
文件 : PenetrationAttackGraphService.cs:9-13

审查结论 : 符合"TTL 5 分钟兜底"需求。同时设置 45 秒滑动过期，避免空闲缓存长期驻留。
 3.3 失效场景验证
场景 失效机制 验证 提交 Flag solvedFingerprint 变化 → Key 变化 ✅ Submit 后新 Key 自动 miss 重置环境 Status + UpdatedAt 变化（ PenetrationService.cs:1305-1306 ） ✅ 停止环境 Status = Stopped + UpdatedAt 变化（ PenetrationService.cs:2147-2148 ） ✅ 重新部署 Status + UpdatedAt 变化（ PenetrationService.cs:1490-1492 ） ✅ 部署失败 Status = Failed + UpdatedAt 变化（ PenetrationService.cs:1328-1330 ） ✅

审查结论 : 失效策略正确，无需显式 Remove ，通过 Key 变化自然失效。旧缓存条目由 TTL 回收。

### 4. GetWorkspace 内嵌 AttackGraph 与 Hidden 脱敏
文件 : PenetrationService.cs:606-709

审查结论 :

- ✅ AttackGraph 内嵌在 PenetrationWorkspaceModel 中（ PenetrationModels.cs:235 ）
- ✅ Workspace 的 Nodes 列表过滤掉 Hidden 节点（ attackNodeByKey 仅含非 Hidden）
- ✅ AttackGraph.Nodes 包含 Hidden 节点的占位信息（Id=0, DisplayName="未知区域"），不泄露真实信息
- ✅ ScoreItems 仅对 accessibleNodeKeys （Accessible/Completed）节点返回（ PenetrationService.cs:669-684 ）
- ✅ IpAddress 仅对入口节点返回（ PenetrationService.cs:662 ）
### 5. Submit 攻击图可达性校验
文件 : PenetrationService.cs:746-751

审查结论 : 可达性校验已接入。

- ✅ 提交前构建攻击图，校验目标节点状态为 Accessible 或 Completed
- ✅ 使用 Build 而非 GetOrBuild ，避免缓存污染（提交前后状态不同）
- ✅ 提交成功后构建 attackGraphAfter ，计算解锁节点数并推送 SignalR 更新（ PenetrationService.cs:820-830 ）
### 6. 独立攻击图接口
文件 : PenetrationPlayerController.cs:43-56

审查结论 : 独立接口已实现，路由为 GET /api/pentest/games/{gameId}/attack-graph （控制器路由前缀需确认，但端点路径符合需求）。

## 四、发现的问题
编号 严重度 问题 位置 建议 1 Major GetAttackGraph 调用 GetWorkspace 构建完整 Workspace（含 Nodes/Policies/EntryPoints）仅为返回 AttackGraph ，产生不必要的数据加载与映射开销 PenetrationService.cs:712-717 抽取独立的 BuildAttackGraph 方法，仅加载攻击图所需数据 2 Minor attackGraphChanged 在 accepted 时被无条件设为 true （ PenetrationService.cs:827 ），即使 unlockedNodeCount == 0 也返回 GraphChanged=true ，与字段语义不符 PenetrationService.cs:827 改为 attackGraphChanged = unlockedNodeCount > 0 或对比前后图指纹 3 Minor IsScoreItemAccessible 方法为死代码，全仓库无调用方 PenetrationAttackGraphService.cs:157-167 删除该方法，或将其接入 Submit 校验以替代内联逻辑 4 Minor GetOrBuild 非原子操作（check-then-set），高并发下可能多次构建相同图 PenetrationAttackGraphService.cs:21-26 使用 ConcurrentDictionary + LazyCache 模式，或按 Key 加锁 5 Info BFS 中发现更短路径时节点会被重复入队，虽保证正确性但在最坏情况下复杂度退化 PenetrationAttackGraphService.cs:194-195 可引入 inQueue 标记避免重复入队，但当前规模下影响可忽略 6 Info revealedNodeIds 逻辑会将"已完成节点的不可达邻居"标记为 Revealed（ PenetrationAttackGraphService.cs:77-85 ），可能泄露拓扑结构 PenetrationAttackGraphService.cs:77-85 确认这是"完成节点后揭示相邻边"的设计意图；若非预期，应仅对 accessible 节点的边做 reveal

## 五、问题详情
### 问题 1: GetAttackGraph 效率问题（Major）
文件 : d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationService.cs:712-717

GetWorkspace （ PenetrationService.cs:580-710 ）会构建完整的 PenetrationWorkspaceModel ，包括：

- EntryPoints （遍历 RuntimeNodes + Containers）
- Nodes （含 ScoreItems、Interfaces 映射）
- Policies （遍历 Edges 并匹配 Nodes）
- attempts 字典（额外 DB 查询， PenetrationService.cs:598-604 ）
而 GetAttackGraph 仅返回 workspace?.AttackGraph ，上述全部计算被丢弃。独立攻击图接口应直接调用 penetrationAttackGraphService.GetOrBuild 并仅加载必要的 solved 集合。

### 问题 2: attackGraphChanged 语义不准（Minor）
文件 : d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationService.cs:814-840

当玩家提交一个 非 checkpoint 的得分项时，节点未完成、无新节点解锁，但 AttackGraphChanged 仍返回 true 。这会导致前端误判为图变更并触发不必要的刷新。同时 PublishAttackGraphUpdate （ PenetrationService.cs:1249-1266 ）中 GraphChanged = true 也是硬编码，同样存在问题。

### 问题 3: 死代码 IsScoreItemAccessible（Minor）
文件 : d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationAttackGraphService.cs:157-167

全仓库搜索 IsScoreItemAccessible 仅在此处定义，无任何调用方。Submit 方法（ PenetrationService.cs:746-751 ）使用内联逻辑做可达性校验，未复用此方法。建议删除以减少维护成本，或重构 Submit 复用此方法。

### 问题 4: GetOrBuild 竞态条件（Minor）
文件 : d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationAttackGraphService.cs:20-27

多线程并发 miss 时会重复执行 Build （含 BFS + 集合收敛循环），浪费 CPU。由于 Build 是纯函数无副作用，结果正确性不受影响，但高并发场景下效率降低。

## 六、总结
### 实现完整性
需求项 状态 说明 按 Allow && IsRouteHint 构建攻击图 ✅ PenetrationAttackGraphService.cs:38-45 BFS 入口最短深度 ✅ BuildDepths 方法，标准 BFS 松弛实现 多路径 OR 解锁 ✅ HashSet.Add 语义，任一前驱完成即解锁 环路集合收敛 ✅ while(changed) + HashSet.Add 返回值 断链节点 Hidden ✅ ResolveStatus 默认返回 Hidden Checkpoint 完成判定 ✅ 有 checkpoint 仅需完成 checkpoint，无则全部可见项 空节点透传 ✅ visibleItems.Length == 0 返回 true 缓存 (GameId,TeamId,Version) ✅ Key 包含三维 + 状态指纹 TTL 5 分钟兜底 ✅ AbsoluteExpirationRelativeToNow = 5min 提交/重置/停止/重部署失效 ✅ Key 含 Status + UpdatedAt + solvedFingerprint GetWorkspace 内嵌 AttackGraph ✅ PenetrationWorkspaceModel.AttackGraph Hidden 节点脱敏 ✅ Id=0, Name="未知区域", ScoreSummary 空 Submit 可达性校验 ✅ 校验节点 Accessible/Completed 独立 attack-graph 接口 ✅ GET /api/pentest/games/{gameId}/attack-graph

### 整体评价
Phase 4 攻击图服务的核心算法（BFS、OR 解锁、集合收敛、checkpoint 判定）实现 正确且符合需求 。缓存策略通过 Key 指纹设计巧妙实现隐式失效，无需显式清理。Hidden 节点脱敏全面，无真实信息泄露。

主要改进点集中在 效率优化 （问题 1、4）和 语义准确性 （问题 2），以及 代码整洁度 （问题 3）。建议优先处理问题 1（独立攻击图接口的冗余构建）和问题 2（ attackGraphChanged 语义），其余为低优先级。





我已完整阅读了 Phase 3 访问策略模型和可达性编译器的实现。下面是详细的审查发现。

------

## 一、意图推断

本次审查的代码实现了 Phase 3 渗透测试平台的"运行期网络可达性编译器"：将拓扑配置中的访问策略（PenetrationEdge）编译为可在 fabric 网络上执行的网络级显式路由（PenetrationRuntimeRoute），并通过路由节点（多网卡 JumpHost/Bastion/FirewallRouter）连接不同安全域。意图是让 RuntimeRoute/Both 模式的策略真正影响运行期网络可达性，而 HintOnly 仅作为题目拓扑提示。

------

## 二、关键变更概览（Mermaid）

**业务流程：策略编译与路由应用**

**技术流程：fabric 路由应用时序**

```
100%
```

------

## 三、八项审查点逐项核对

### 1. PenetrationEdge 新增 EnforcementMode、Priority 字段 — 已实现

文件：d:\newgz\newGZCTF-main\src\GZCTF\Models\Data\PenetrationEntities.cs

- 第 68-74 行：PenetrationEnforcementMode 枚举（HintOnly=0, RuntimeRoute=1, Both=2）
- 第 368-370 行：PenetrationEdge.EnforcementMode（默认 HintOnly）、Priority（默认 100）
- d:\newgz\newGZCTF-main\src\GZCTF\Models\AppDbContext.cs 第 824-830 行：EF 配置字符串转换 + 默认值
- 迁移 20260619060822_AddPenetrationRuntimeRoutes.cs 第 22-35 行：新增列

### 2. PenetrationNode 新增 AllowRouting 字段 — 已实现

- PenetrationEntities.cs 第 271 行：public bool AllowRouting { get; set; }
- 迁移第 15-20 行新增列；第 37-41 行数据迁移：将 JumpHost/Bastion/FirewallRouter 节点自动置为 AllowRouting=true
- IsRouteCapableNode（第 2761-2762 行）以 AllowRouting || NodeType in {JumpHost, Bastion, FirewallRouter} 判定路由能力

### 3. PenetrationRuntimeRoute 实体定义 — 已实现

PenetrationEntities.cs 第 545-596 行，字段完整覆盖需求：

- 路由编译结果：Status（PenetrationRouteStatus）、CommandSummary、Message
- 路由节点：RouteNodeKey、RouteNodeName
- 源/目标网段：SourceNetworkName、TargetNetworkName、SourceCidr、TargetCidr
- 网关：GatewayIp
- 执行摘要：CommandSummary
- 应用状态：Status、AppliedAt
- 失败原因：Message
- 索引：EnvironmentId、EdgeTopologyKey

### 4. 可达性编译器输入/输出 — 已实现

核心方法：CompileRuntimeRoutes（PenetrationService.cs 第 2626-2722 行）

- 输入：PenetrationConfig（安全域/节点/接口/IPAM/访问策略）+ IReadOnlyList<RuntimeInterfacePlan>
- 输出：List<RuntimeRoutePlan>，包含路由节点、源/目标接口、源/目标路由接口、端点接口、CommandSummary
- fabric bridge/veth 设置：AttachRuntimeFabricInterfaces（第 2886-2922 行）+ BuildFabricHostInterfaceName/BuildFabricContainerInterfaceName（第 2984-2990 行）
- 下一跳：GatewayIp = route.SourceRouteInterface?.IpAddress（第 2806 行）
- 清理标签：DestroyEnvironment 中 context.PenetrationRuntimeRoutes.RemoveRange（第 2145 行）

### 5. 支持四种策略类型 — 已实现

ResolvePolicyNetworks（第 2727-2741 行）按 PenetrationPolicyScope（Node=0, Network=1）解析：

- node->node：SourceKind=Node, TargetKind=Node
- node->network：SourceKind=Node, TargetKind=Network
- network->node：SourceKind=Network, TargetKind=Node
- network->network：SourceKind=Network, TargetKind=Network
- 第 2656-2660 行 distinctPairs 笛卡尔积处理多网络组合

### 6. 先计算网络级可达性图，再检查多网卡路由节点 — 已实现

- 第 2648-2649 行：先解析源/目标网络列表
- 第 2656-2660 行：计算跨安全域网络对（Source.Network.Id != Target.Network.Id）
- 第 2671 行：对每对网络调用 FindRouteNode 查找同时连接两个安全域的路由节点
- FindRouteNode（第 2743-2759 行）：筛选 IsRouteCapableNode 且同时拥有两个网络安全卡的节点

### 7. Deny 首版只表示不生成可达路由 — 已实现

- 第 2641-2646 行：Deny 策略生成 HintOnly 计划，消息明确"Deny 在首版表示不生成可达路由；平台不承诺端口级或包级阻断"
- 第 2397-2398 行：验证时对 RequiresRuntimeRoute + Deny 给出警告
- 第 2418 行：全局警告说明首版不做单向 ACL、协议/端口级防火墙

### 8. 策略状态枚举 — 已实现

PenetrationEntities.cs 第 76-84 行：PenetrationRouteStatus（HintOnly=0, RoutePlanned=1, RouteApplied=2, RouteFailed=3, Unsupported=4），五种状态齐全。

------

## 四、审查发现的问题

| No.  | 问题标题                                                     | 建议                                                         | 代码位置                                                     |
| ---- | ------------------------------------------------------------ | ------------------------------------------------------------ | ------------------------------------------------------------ |
| 1    | CompileRuntimeRoutes 末尾的兜底 Unsupported 分支为死代码，条件逻辑永真不触发 | 该分支条件 plans.All(p => p.Edge.Id != edge.Id \|\| p.Status != Unsupported) 在所有 pair 都已添加 Unsupported 时为 false，导致永不进入。应直接删除或改为 if (!edgePlanned && distinctPairs.Count > 0) 简化 | [PenetrationService.cs:2717-2718](file:///D:/newgz/newGZCTF-main/src/GZCTF/Services/PenetrationService.cs#L2717-L2718) |
| 2    | sourceRouteInterface/targetRouteInterface 使用 First() 选择，未优先 Primary 网卡 | 当路由节点在同一安全域有多个接口时，First() 可能选到非主网卡，应改为优先 IsPrimary 或 IsManagement 排序后再取 | [PenetrationService.cs:2685-2686](file:///D:/newgz/newGZCTF-main/src/GZCTF/Services/PenetrationService.cs#L2685-L2686) |
| 3    | ApplyRuntimeRoutes 先清空再应用，失败时不回滚已删除的路由    | 第 2783-2784 行 RemoveRange + Clear 后若后续 ApplyRoutePlan 抛异常，旧路由已丢失且新路由未应用，环境会处于"无路由"中间态。建议先应用成功再替换，或记录回滚日志 | [PenetrationService.cs:2783-2784](file:///D:/newgz/newGZCTF-main/src/GZCTF/Services/PenetrationService.cs#L2783-L2784) |
| 4    | PenetrationRuntimeRoute 仅靠 EdgeTopologyKey 字符串关联策略，无外键约束 | 拓扑键重命名或重复时会导致路由记录无法关联到策略。建议在应用前校验 EdgeTopologyKey 唯一性，或在 PenetrationEdge 上增加 RuntimeRouteId 反向导航 | [PenetrationEntities.cs:555](file:///D:/newgz/newGZCTF-main/src/GZCTF/Models/Data/PenetrationEntities.cs#L555) |
| 5    | FindRouteNode 路由节点选择仅按 AllowRouting + OrderIndex，未考虑节点健康/负载 | 多个候选路由节点时可能选到负载高或不可达的节点。建议增加运行期健康权重，或在 ApplyRoutePlan 失败时尝试下一个候选路由节点 | [PenetrationService.cs:2755-2758](file:///D:/newgz/newGZCTF-main/src/GZCTF/Services/PenetrationService.cs#L2755-L2758) |
| 6    | Priority 字段仅用于编译排序，未用于同一网络对的策略冲突解决  | 当多条 Allow 策略覆盖同一对网络时，所有策略都会生成路由，可能产生重复路由。建议对同一网络对按 Priority 取最高优先级策略，或在 distinctPairs 上做 Priority 去重 | [PenetrationService.cs:2632](file:///D:/newgz/newGZCTF-main/src/GZCTF/Services/PenetrationService.cs#L2632) |
| 7    | ApplyRoutePlan 中反向路由写入会绕过 Deny 语义                | 第 2874-2881 行无条件为目标端点写入到源网段的路由，即使策略方向是"源->目标"。首版说明已声明"为保证回包写入反向路由"，但与未来 Deny 单向 ACL 扩展冲突，建议在代码注释中明确标注此约束 | [PenetrationService.cs:2874-2881](file:///D:/newgz/newGZCTF-main/src/GZCTF/Services/PenetrationService.cs#L2874-L2881) |
| 8    | ResolveEndpointNodes 对 Network scope 排除路由节点，可能导致网络中仅路由节点时返回空 | 第 2775 行 Where(i => i.Node.Id != routeNodeId) 是合理的（避免路由节点探测自己），但应在前置检查中给出更明确的错误消息，当前第 2694 行消息"源或目标安全域缺少除路由节点以外的可探测端点"已较清晰，可保留 | [PenetrationService.cs:2775](file:///D:/newgz/newGZCTF-main/src/GZCTF/Services/PenetrationService.cs#L2775) |
| 9    | ApplyRuntimeRoutes 第 2813 行 continue 跳过非 RoutePlanned 路由的应用，但仍将其写入 DB | HintOnly/Unsupported 路由会被持久化到 PenetrationRuntimeRoutes 表，占用存储且在 GetTeamEnvironments 中返回。这是设计意图（便于展示），但建议在模型上增加 IsExecutable 派生属性以便前端区分 | [PenetrationService.cs:2811-2814](file:///D:/newgz/newGZCTF-main/src/GZCTF/Services/PenetrationService.cs#L2811-L2814) |
| 10   | 验证阶段 CompileRuntimeRoutes 用 sample teamIndex=0 调用，可能与实际队伍网段分配不一致导致误报 | 第 2410-2411 行 BuildRuntimeInterfaces(config, 0, ...) 使用 0 号队伍采样，若 IPAM 在不同 teamIndex 下行为不同（如 StaticIp 偏移），验证结果可能与实际部署不符。当前 ShiftStaticIp 逻辑一致，风险较低，但建议注释说明 | [PenetrationService.cs:2410-2411](file:///D:/newgz/newGZCTF-main/src/GZCTF/Services/PenetrationService.cs#L2410-L2411) |

------

## 五、关键代码片段（载入性证据）

### 5.1 可达性编译器核心（CompileRuntimeRoutes）

### 5.2 路由节点查找（FindRouteNode）

### 5.3 路由应用与探测（ApplyRoutePlan）

### 5.4 RuntimeRoutePlan 记录定义

------

## 六、审查结论

**整体实现质量：高**。八项审查点全部已实现，设计思路清晰：

- 实体模型完整覆盖了 EnforcementMode、Priority、AllowRouting、PenetrationRuntimeRoute 的所有需求字段
- 可达性编译器正确支持四种策略类型组合，先计算网络级可达性图再查找多网卡路由节点
- Deny 语义符合首版定义（仅不生成路由，不做阻断）
- 策略状态机五态齐全，应用流程包含探测验证

**主要风险点**：

1. 问题 1（死代码）和问题 2（First() 选网卡）是确定的代码质量问题，建议优先修复
2. 问题 3（清空后失败不回滚）是潜在的运行时可靠性问题，在 fabric 应用失败时会导致环境无路由
3. 问题 6（Priority 未用于冲突解决）在复杂拓扑下可能产生重复路由

**相关文件清单**：

- d:\newgz\newGZCTF-main\src\GZCTF\Models\Data\PenetrationEntities.cs（实体定义）
- d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationService.cs（编译器与应用逻辑）
- d:\newgz\newGZCTF-main\src\GZCTF\Models\AppDbContext.cs（EF 配置）
- d:\newgz\newGZCTF-main\src\GZCTF\Models\Request\Game\PenetrationModels.cs（请求/响应模型）
- d:\newgz\newGZCTF-main\src\GZCTF\Migrations\20260619060822_AddPenetrationRuntimeRoutes.cs（迁移）







# Phase 3 Linux bridge/veth Fabric 数据面深度审查报告
## 一、审查范围与证据来源
本次审查覆盖以下核心文件（均为绝对路径）：

- d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\IContainerManager.cs — 接口定义
- d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\DockerManager.cs — 本地 Docker 后端
- d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\KubernetesManager.cs — K8s 后端
- d:\newgz\newGZCTF-main\src\GZCTF\Services\Fleet\FleetContainerManager.cs — Fleet 调度层
- d:\newgz\newGZCTF-main\src\GZCTF\Services\Fleet\AgentClient.cs — Agent HTTP 客户端
- d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationService.cs — 渗透编排服务
- d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationCleanupService.cs — 清理后台服务
- d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\ContainerServiceExtension.cs — DI 注册
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Services\DockerService.cs — Agent 端 fabric 实现
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Controllers\ContainerController.cs — Agent HTTP 端点
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Models\ContainerModels.cs — Agent 数据模型
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Program.cs — Agent 启动入口
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Services\HeartbeatWorker.cs — Agent 心跳服务
- d:\newgz\newGZCTF-main\docs\pentest-commercialization-execution-plan.md — 设计文档
## 二、变更总览（Mermaid 图）
### 2.1 Fabric 数据面业务流
### 2.2 Fabric 清理技术流
100 %

## 三、逐项审查发现
### 1. IPenetrationFabricManager 接口定义
文件 : d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\IContainerManager.cs 第 37-57 行

接口定义了 6 个方法 + 1 个属性：

- IsSupported (属性)
- CreateNetworkAsync(networkName, cidr, token)
- AttachInterfaceAsync(container, spec, token)
- EnableForwardingAsync(container, token)
- ApplyRouteAsync(container, targetCidr, gatewayIp, token)
- ProbeAsync(container, targetIp, token)
- RemoveNetworkAsync(networkName, token)
发现 : 设计文档（第 1052 行）要求 DetachAndCleanupAsync(environmentId/resources, token) ，但实际接口未实现该方法，改为 RemoveNetworkAsync 。这是一个设计偏离，但合理——清理职责被拆分到 IContainerManager.DestroyContainerAsync （容器）和 IPenetrationFabricManager.RemoveNetworkAsync （bridge）两个接口。

辅助类型 :

- PenetrationFabricInterfaceSpec （第 59-67 行）：包含 NetworkName、NetworkCidr、HostInterfaceName、ContainerInterfaceName、IpAddress、PrefixLength、IsPrimary、RemoveDefaultRoute
- PenetrationFabricResult （第 69-87 行）：提供 Success/Failed/Timeout/Unsupported 四种静态工厂
### 2. 本地 Docker 后端实现
文件 : d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\DockerManager.cs

DockerManager 实现了 IPenetrationFabricManager （第 14 行）。核心机制：

- IsSupported （第 35 行）： !_isWindowsDaemon && OperatingSystem.IsLinux() — 仅 Linux 宿主进程支持
- RunHostFabricCommand （第 610-662 行）：通过 System.Diagnostics.Process 启动 sh -c "..." 子进程，带超时（15s）和 CancelAfter ，超时后 Kill(entireProcessTree: true)
- GetContainerPid （第 664-676 行）：通过 Docker API InspectContainerAsync 获取容器 PID
- CreateNetworkAsync （第 412-426 行）： ip link add name {bridge} type bridge; ip link set {bridge} up — 幂等，先检查存在
- AttachInterfaceAsync （第 428-468 行）：完整 veth pair 创建流程，包含 ERR trap 清理（第 446 行）
- EnableForwardingAsync （第 470-487 行）： nsenter -t {pid} -n sh -c 'echo 1 > /proc/sys/net/ipv4/ip_forward'
- ApplyRouteAsync （第 489-506 行）： nsenter -t {pid} -n ip route replace {cidr} via {gw} + grep 验证
- ProbeAsync （第 508-525 行）： nsenter -t {pid} -n ping -c 1 -W 2 {ip} ，超时 8s
正确性确认 : 所有 fabric 操作均通过宿主 ip / nsenter / ping 执行，nsenter 仅切换 network namespace（ -n ），不切换 mount namespace，因此使用宿主的二进制文件，不要求题目镜像内置 iproute2/ping。符合设计文档第 1114 行要求。

### 3. Fleet Agent 后端实现
文件 :

- d:\newgz\newGZCTF-main\src\GZCTF\Services\Fleet\FleetContainerManager.cs 第 287-371 行
- d:\newgz\newGZCTF-main\src\GZCTF\Services\Fleet\AgentClient.cs 第 135-243 行
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Controllers\ContainerController.cs 第 62-109 行
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Services\DockerService.cs 第 307-427 行
调用链 : FleetContainerManager -> AgentClient （HTTP）-> Agent ContainerController -> DockerService

FleetContainerManager 通过 ResolveContainerNode 解析容器所在节点（第 373-381 行），根据 node.IsLocal 分流：

- 本地节点：委托给 _localManager （DockerManager）
- 远端节点：委托给 _agentClient （HTTP 调用 Agent）
AgentClient 通过 PostFabricAsync （第 229-243 行）发送 JSON POST 请求，反序列化为 PenetrationFabricResult 。

Agent 端 DockerService 的实现与 DockerManager 几乎完全一致（相同的 shell 命令、相同的命名函数）。

### 4. Kubernetes 后端返回 unsupported
文件 : d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\KubernetesManager.cs 第 250-273 行

确认 : Kubernetes 后端正确返回 unsupported，符合设计文档第 1109 行要求。DI 注册在 ContainerServiceExtension.cs 第 59 行正确绑定。

### 5. Fabric 模式非入口/非发布节点使用 Docker network none
文件 :

- d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\DockerManager.cs 第 711-720 行
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Services\DockerService.cs 第 28-83 行
DockerManager GetCreateContainerParameters :

Agent DockerService 同样逻辑（第 81-83 行）。

确认 : 符合设计。当 UsePenetrationFabric=true ：

- 入口/发布节点（ PublishPort=true ）：使用 Open 管理网（保证端口映射）
- 普通内网节点（ PublishPort=false ）：使用 none （纯 fabric 隔离）
PenetrationService BuildContainerConfig （第 2607 行）设置 PublishPort = nodePlan.Node.PublishPort || nodePlan.Node.IsEntry ，确保入口节点保留管理网。

### 6. 安全域 bridge 命名规则
文件 :

- d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\DockerManager.cs 第 678-683 行
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Services\DockerService.cs 第 529-534 行
bridge 名称 = yyb + SHA256(networkName) 的前 12 个十六进制字符 = 15 字符。

networkName 格式（ PenetrationService.cs 第 3809-3810 行）：

确认 : 15 字符限制正确（Linux IFNAMSIZ=16，最大 15 字符）。使用稳定哈希保证同一安全域每次部署得到相同 bridge 名。

偏离 : 设计文档第 1041 行要求 yy-pentest-g{game}-t{team}-n{network} 可读命名，但该格式远超 15 字符限制。实现选择了哈希短名以满足 Linux 内核限制，牺牲了可读性。这是合理的技术取舍，但与文档不一致。

### 7. veth 接口命名规则
文件 : d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationService.cs 第 2984-2997 行

`static string BuildFabricHostInterfaceName(int environmentId, int nodeId, int interfaceId) =>
    BuildFabricName("yyp", $"{environmentId}:{nodeId}:{interfaceId}");

static string BuildFabricContainerInterfaceName(RuntimeInterfacePlan iface) =>
    BuildFabricName("yyc", $"{iface.Node.TopologyKey}:{iface.InterfaceId}");`

- host 端： yyp + 12 字符 hash = 15 字符
- container 端： yyc + 12 字符 hash = 15 字符
- peer 端（veth pair 对端）： BuildPeerInterfaceName （DockerManager 第 685-691 行）= p + hostIf 截断，冲突时回退到 yyr + hash
确认 : 所有接口名均在 15 字符限制内。符合设计文档第 1112 行要求。

### 8. bridge 作为二层 fabric，不给宿主 bridge 配置 CIDR
文件 :

- d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\DockerManager.cs 第 412-426 行
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Services\DockerService.cs 第 307-321 行
CreateNetworkAsync 命令：

确认 : 仅创建 bridge 设备并启用， 没有 ip addr add 命令。bridge 作为纯二层交换设备，不配置三层 IP，避免宿主路由污染。符合设计文档第 1113 行要求。

CIDR 仅在 AttachInterfaceAsync 中配置到容器端 veth（第 459 行 nsenter -t {pid} -n ip addr add {ipCidr} dev {containerIf} ）。

### 9. 路由写入和探测使用宿主/Agent 的 ip/nsenter/ping
文件 :

- d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\DockerManager.cs 第 489-525 行
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Services\DockerService.cs 第 381-416 行
- ApplyRouteAsync : nsenter -t {pid} -n ip route replace {targetCidr} via {gatewayIp} + grep 验证
- ProbeAsync : nsenter -t {pid} -n ping -c 1 -W 2 {targetIp}
- EnableForwardingAsync : nsenter -t {pid} -n sh -c 'echo 1 > /proc/sys/net/ipv4/ip_forward'
所有命令均通过 nsenter -t {pid} -n 进入容器网络命名空间，使用宿主二进制（ip/nsenter/ping），不要求题目镜像内置这些工具。命令前均有 command -v ip/nsenter/ping 检查。

确认 : 符合设计文档第 1114 行要求。

NormalizeFabricError （PenetrationService.cs 第 3001-3029 行）将常见错误翻译为可读中文提示，包括缺少 ip/nsenter/ping、权限不足、探测不通等场景。

### 10. 清理时是否按 veth -> bridge -> container 顺序幂等清理
文件 : d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationService.cs 第 2059-2155 行

实际清理顺序 :

1. 先销毁容器 （第 2069-2117 行）： containerManager.DestroyContainerAsync(runtime.Container, token) — 容器 netns 销毁时，容器端 veth 自动消失
2. 后删除网络/bridge （第 2119-2136 行）： RemoveRuntimeNetwork -> penetrationFabricManager.RemoveNetworkAsync -> ip link del {bridge} — bridge 删除时，host 端 veth 作为端口被级联删除
偏离 : 设计文档第 1073 行要求 veth -> bridge -> container 顺序，但实现采用 container -> bridge 顺序，且 没有显式删除 veth 。

幂等性确认 :

- DestroyContainerAsync 使用 Force=true ，捕获 NotFound 异常 — 幂等
- RemoveNetworkAsync 使用 ip link del {bridge} 2>/dev/null || true — 幂等
风险评估 : 实现的顺序在实际 Linux 行为下是可行的（容器销毁清理容器端 veth，bridge 删除清理 host 端 veth），但如果容器销毁失败，bridge 删除仍会执行，可能导致容器残留但网络断开。设计文档的顺序（先 veth 再 bridge 最后 container）可能在容器销毁失败时更安全，但需要额外的 nsenter 操作。

### 11. Agent 启动时是否扫描孤儿 yy-pentest-* bridge/veth
文件 :

- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Program.cs （全文 48 行）
- d:\newgz\newGZCTF-main\src\GZCTF.Agent\Services\HeartbeatWorker.cs （全文 109 行）
发现 : 未实现 。

Program.cs 仅配置 DI、注册认证中间件、映射控制器、启动监听，没有任何孤儿资源扫描逻辑。

HeartbeatWorker.cs 仅发送心跳（CPU/内存/容器数/VM数），不扫描 fabric 资源。

设计文档第 1074 行明确要求：
 Agent 启动时扫描孤儿 yy-pentest-* bridge/veth，并只清理数据库确认为 CleanupPending/Orphaned 的资源，避免误删人工网络。
双重不一致 :

1. 功能未实现 — Agent 启动时不扫描孤儿 bridge/veth
2. 命名模式不匹配 — 文档要求扫描 yy-pentest-* ，但实际 bridge 命名为 yyb{hash} （见第 6 项），即使实现扫描也会找不到
## 四、问题汇总表
序号 问题标题 严重度 建议修复 代码位置 1 Agent 启动孤儿 bridge/veth 扫描未实现 严重 在 Agent Program.cs 或 HostedService 中增加启动时扫描逻辑，按 yyb / yyp / yyc / yyr 前缀枚举 ip link show ，与数据库 CleanupPending/Orphaned 环境比对后清理 d:\newgz\newGZCTF-main\src\GZCTF.Agent\Program.cs 2 bridge 命名模式与设计文档不一致 重要 更新设计文档为 yyb{hash} 实际模式，或在 Agent 孤儿扫描时同时匹配 yyb / yyp / yyc / yyr 前缀 d:\newgz\newGZCTF-main\docs\pentest-commercialization-execution-plan.md 第 1041、1074、1077 行 3 Agent AttachFabricInterfaceAsync 缺少 ERR trap 重要 在 Agent DockerService.cs AttachFabricInterfaceAsync 的 command 中增加 trap '...' ERR ，与 DockerManager.cs 第 446 行保持一致，避免中途失败留下孤儿 veth d:\newgz\newGZCTF-main\src\GZCTF.Agent\Services\DockerService.cs 第 338-358 行 4 清理顺序偏离设计（container->bridge 而非 veth->bridge->container） 中等 评估是否需要在 RemoveNetworkAsync 前增加显式 veth 清理步骤（按 yyp / yyc 前缀枚举并删除），或在文档中确认当前顺序的可接受性 d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationService.cs 第 2069-2136 行 5 FleetContainerManager.IsSupported 恒为 true 中等 改为检查是否存在至少一个 Linux 节点，或在 PenetrationService 部署前增加 fabric 可用性预检，避免 Windows-only fleet 下部署到中途才失败 d:\newgz\newGZCTF-main\src\GZCTF\Services\Fleet\FleetContainerManager.cs 第 266 行 6 FleetContainerManager.CreateNetworkAsync 直接返回 Unsupported 低 当前设计为懒创建（AttachInterfaceAsync 时创建 bridge），可接受。建议在注释中说明此设计意图，避免误用 d:\newgz\newGZCTF-main\src\GZCTF\Services\Fleet\FleetContainerManager.cs 第 287-292 行 7 RemoveNetworkAsync 遍历所有 Online 节点但不处理离线节点 低 离线节点的 bridge 会泄漏。建议记录未清理的节点，待节点上线时由孤儿扫描补齐（依赖问题 1 的实现） d:\newgz\newGZCTF-main\src\GZCTF\Services\Fleet\FleetContainerManager.cs 第 351-371 行 8 fabric 命名/ShellQuote 辅助函数在 3 处重复 低 抽取到共享内部工具类（如 FabricNamingHelper ），供 DockerManager、Agent DockerService、PenetrationService 共用 DockerManager.cs 第 678-704 行、DockerService.cs 第 529-555 行、PenetrationService.cs 第 2992-2999 行 9 BuildPeerInterfaceName 理论碰撞风险 低 当 hostIf 以 p 开头且为 14 字符时， p +hostIf 截断可能等于 hostIf，已有 yyr 回退处理。SHA256 碰撞概率极低，可接受 d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\DockerManager.cs 第 685-691 行 10 DetachAndCleanupAsync 接口未实现 低 设计文档第 1052 行要求该接口，实际用 RemoveNetworkAsync 替代。建议更新文档或补充接口 d:\newgz\newGZCTF-main\src\GZCTF\Services\Container\Manager\IContainerManager.cs 第 37-57 行

## 五、关键代码片段引用
### 5.1 接口定义（IContainerManager.cs 第 37-57 行）
### 5.2 DockerManager AttachInterfaceAsync ERR trap（第 446 行，Agent 版缺失）
DockerManager 有 ERR trap：

Agent DockerService（第 338-358 行） 没有 此 trap，是问题 3 的核心证据。

### 5.3 Fabric 模式 network none 选择（DockerManager.cs 第 711-716 行）
### 5.4 清理顺序证据（PenetrationService.cs 第 2069-2136 行）
先容器（2069-2117），后网络（2119-2136），无显式 veth 清理步骤。

## 六、审查结论
### 已正确实现的部分（符合设计文档）
- IPenetrationFabricManager 接口定义完整（6 方法 + IsSupported）
- 本地 Docker 后端通过宿主 ip/nsenter 执行 fabric 操作
- Fleet Agent 后端通过 HTTP 远程执行相同 fabric 操作
- Kubernetes 后端返回 unsupported
- Fabric 模式非入口/非发布节点使用 Docker network none
- bridge 命名使用稳定哈希短名，15 字符限制
- veth 接口命名 host/container 端，15 字符限制
- bridge 作为二层 fabric，不给宿主 bridge 配置 CIDR
- 路由写入和探测使用宿主/Agent 的 ip/nsenter/ping
- 清理操作幂等（Force=true、 || true ）
### 需要修复的偏离
1. 严重 : Agent 启动孤儿 bridge/veth 扫描未实现（设计文档第 1074 行明确要求）
2. 重要 : bridge 命名模式 yyb{hash} 与文档 yy-pentest-* 不一致，影响孤儿扫描匹配
3. 重要 : Agent 端 AttachFabricInterfaceAsync 缺少 ERR trap，中途失败会留下孤儿 veth
4. 中等 : 清理顺序为 container->bridge，非设计的 veth->bridge->container，且无显式 veth 清理
5. 中等 : FleetContainerManager.IsSupported 恒为 true，无法在部署前检测 Windows-only fleet
### 建议优先级
P0: 问题 1（孤儿扫描）、问题 3（Agent ERR trap）
P1: 问题 2（命名模式对齐）、问题 5（IsSupported 预检）
P2: 问题 4（清理顺序）、问题 7（离线节点泄漏）
P3: 问题 6、8、9、10（文档/重构/低风险）



# Phase 4 选手端迷雾工作台 & SignalR 实时推送 深度审查报告
## 一、审查范围
本次审查覆盖以下文件：

- d:\newgz\newGZCTF-main\src\GZCTF\ClientApp\src\pages\games\[id]\Penetration.tsx （选手端迷雾工作台）
- d:\newgz\newGZCTF-main\src\GZCTF\Hubs\UserHub.cs （SignalR 用户 Hub）
- d:\newgz\newGZCTF-main\src\GZCTF\Hubs\Clients\IUserClient.cs （Hub 客户端接口）
- d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationAttackGraphService.cs （攻击图构建服务）
- d:\newgz\newGZCTF-main\src\GZCTF\Services\PenetrationService.cs （渗透服务，含推送逻辑）
- d:\newgz\newGZCTF-main\src\GZCTF\Controllers\PenetrationPlayerController.cs （选手端控制器）
- d:\newgz\newGZCTF-main\src\GZCTF\Models\Request\Game\PenetrationModels.cs （数据模型）
- d:\newgz\newGZCTF-main\src\GZCTF\ClientApp\src\Api\PenetrationApi.ts （前端 API 定义）
- d:\newgz\newGZCTF-main\src\GZCTF\ClientApp\src\pages\admin\games\[id]\Penetration.tsx （管理端配置）
- d:\newgz\newGZCTF-main\src\GZCTF\ClientApp\src\styles\YinyuRefinement.css （迷雾 CSS 样式）
## 二、变更概览（Mermaid 图）
### 业务流程：选手渗透工作台交互流
### 技术流程：SignalR 推送与数据边界
100 %

## 三、逐项审查结果
### 1. 选手端渗透页面（Penetration.tsx）
审查项 结论 证据位置 从网段/资产列表改为黑盒攻击图 通过 AttackMap 组件（L111-260）按深度分列渲染攻击图节点，不再展示网段/资产列表 迷雾状态展示（4 态） 通过 fogLabel （L37-42）定义 Hidden/Revealed/Accessible/Completed 四态中文标签 Hidden 节点不可点击且 aria-hidden 通过 L208-210： aria-hidden={hidden} 、 tabIndex={hidden ? -1 : 0} 、 disabled={hidden} 可见节点支持键盘聚焦 通过 L209： tabIndex={hidden ? -1 : 0} ，可见节点 tabIndex=0 可聚焦 低成本 CSS 迷雾轮廓（非 WebGL） 通过 YinyuRefinement.css L4073-4124 使用 radial-gradient 、 box-shadow 、 border-color 实现，无 WebGL/Canvas 拖拽 通过 L135-150： onPointerDown/Move/Up/Cancel + setPointerCapture 实现拖拽 缩放 通过 L152-157 zoom() 函数 + L188-191 滚轮缩放，clamp 在 0.72-1.45 重置视角 通过 L177-179： setView({ x: 0, y: 0, scale: 1 }) 小地图 通过 L233-237： yy-pentest-mini-map 按深度渲染条形摘要， aria-hidden="true" 任务面板：点击可访问节点显示题目列表 通过 L513-528： selectedTasks 来自 selectedWorkspaceNode?.scoreItems ，仅可操作节点有数据 已发现攻击路径横向展示 通过 L240-256： yy-pentest-path-strip 横向 flex 布局展示 visibleEdges SignalR 推送后静默刷新 workspace 通过 L316： void load(true) ，silent=true 不触发 loading/error 状态

前端额外防御 ：L119-131 visibleEdges 在客户端再次过滤"源/目标均非 Hidden"，与服务端形成双重防御。

### 2. SignalR 推送
审查项 结论 证据位置 新增渗透队伍私有组 Game_{gameId}_PentestTeam_{teamId} 通过 UserHub.cs L12-13： PenetrationTeamGroupName 静态方法返回该格式 只有已登录且已通过比赛审核的成员加入 通过 UserHub.cs L38-51：先校验 IsAuthenticated ，再查 participation?.Status == ParticipationStatus.Accepted ，才加入队伍组 推送内容只含安全摘要 通过 PenetrationModels.cs L465-476： PenetrationAttackGraphUpdateModel 仅含 GameId/TeamId/PublishedVersion/Accepted/GraphChanged/CompletedNodeCount/VisibleNodeCount/UnlockedNodeCount/Time，无节点名/IP/接口 推送失败不影响提交结果 通过 PenetrationService.cs L1252-1277： PublishAttackGraphUpdate 用 try-catch 包裹，失败仅 LogWarning ；且在 SaveChangesAsync 之后调用（L807），提交已持久化 前端连接 /hub/user?game={gameId} 通过 Penetration.tsx L304： .withUrl('/hub/user?game=${gameId}')

推送触发点 ：PenetrationService.cs L816-831，仅在 accepted=true 时构建 after 图并推送，计算 unlockedNodeCount （after 可见 - before 可见）。

前端处理 ：Penetration.tsx L312-324，校验 update.gameId === gameId 和 update.teamId === teamIdRef.current （双重校验），仅 accepted && unlockedNodeCount > 0 时弹通知。

### 3. 管理端配置入口
审查项 结论 证据位置 节点属性新增"选手端代号" 通过 admin Penetration.tsx L1438-1443： TextInput label="选手端代号" 绑定 playerAlias ，description 提示"留空时平台自动显示为入口目标或目标模块编号" 节点属性新增"选手端说明" 通过 admin Penetration.tsx L1444-1450： Textarea label="选手端说明" 绑定 playerDescription ，description 提示"禁止写入内部 IP、网卡、安全域等管理信息" 得分项新增"作为解锁检查点" 通过 admin Penetration.tsx L1501-1505： Checkbox label="作为解锁检查点" 绑定 item.isCheckpoint 提示"该节点全部 checkpoint 完成后解锁下一跳" 通过 admin Penetration.tsx L1482-1486： YinyuPanel 提示文本"勾选'解锁检查点'后，该节点全部检查点完成才会解锁下一跳；如果一个节点没有检查点，则默认完成该节点所有可见得分项后解锁下一跳"

服务端解锁逻辑验证 ：PenetrationAttackGraphService.cs L202-211 IsNodeCompleted ：有 checkpoint 时要求全部 checkpoint 完成；无 checkpoint 时要求全部可见得分项完成。与管理端提示文本一致。

默认值 ：admin Penetration.tsx L316 defaultScoreItem 中 isCheckpoint: orderIndex === 0 （首个得分项默认为检查点）；L334 defaultNode 中 playerAlias: isEntry ? '入口目标' : '' 。

### 4. 黑盒数据边界
审查项 结论 证据位置 攻击图边数据收紧为"源/目标两端均非 Hidden 才返回" 通过 PenetrationAttackGraphService.cs L116-123： visibleNodeIds 过滤 Status != Hidden && Id > 0 ， graphEdges 要求 visibleNodeIds.Contains(SourceNodeId) && visibleNodeIds.Contains(TargetNodeId) 不返回安全域 通过 PenetrationService.cs L644： Networks = [] （workspace 不返回任何安全域） 不返回 CIDR 通过 同上， Networks = [] ；workspace 网络模型虽有 Cidr 字段但列表为空 不返回隐藏节点真实名称 通过 PenetrationAttackGraphService.cs L103：Hidden 节点 DisplayName = "未知区域" ；L230-236 BuildPlayerDisplayName 优先使用 PlayerAlias ，非真实 Name 不返回隐藏节点 IP 通过 PenetrationService.cs L662： IpAddress = n.IsEntry ? runtime?.IpAddress : null ，且仅对 attackNodeByKey 中的非 Hidden 节点返回；Hidden 节点不在 Nodes 列表中 不返回隐藏节点接口 通过 PenetrationService.cs L668： Interfaces = n.IsEntry ? BuildWorkspaceInterfaces(...) : [] ，仅入口节点返回接口；Hidden 节点不在列表中 不返回隐藏题目项 通过 PenetrationService.cs L669-684： ScoreItems = canOperate ? ... : [] ， canOperate 仅对 Accessible/Completed 节点为 true；Revealed 节点也返回空 ScoreItems

额外脱敏 ：

- Hidden 节点 Id = 0 （L101）、 Description = null （L104）、 ScoreSummary 为空对象（L107）、 PositionX/Y 使用深度推算的占位值（L108-109）
- workspace 中 PenetrationWorkspaceNodeModel.Name 取自 graphNode.DisplayName （L659），非真实 n.Name
- workspace 中 NetworkId = 0 （L657），不暴露真实安全域归属
## 四、审查结论
✅ 未发现关键问题。代码实现完整满足 Phase 4 全部审查要求。

所有 4 大类、共 21 项审查点均通过验证：

1. 选手端迷雾工作台 （9 项）：黑盒攻击图渲染、4 态迷雾、Hidden 节点无障碍隔离、键盘聚焦、CSS 迷雾轮廓、拖拽/缩放/重置/小地图、任务面板、攻击路径横向展示、SignalR 静默刷新——全部实现。
2. SignalR 推送 （5 项）：私有组命名规范、登录+审核双重校验、推送内容仅含安全摘要、推送失败容错不影响提交、前端连接路径——全部正确。
3. 管理端配置入口 （4 项）：选手端代号/说明输入、解锁检查点复选框、checkpoint 解锁提示文本——全部就位，且服务端 IsNodeCompleted 逻辑与提示文本一致。
4. 黑盒数据边界 （5 项）：边数据双端非 Hidden 过滤、安全域/CIDR/真实名称/IP/接口/隐藏题目项均不返回——全部收紧，且前端有二次过滤防御。
实现亮点 ：

- 服务端 PenetrationAttackGraphService 与前端 AttackMap 均对边数据做了"双端非 Hidden"过滤，形成纵深防御
- PublishAttackGraphUpdate 在 SaveChangesAsync 之后调用且 try-catch 包裹，确保推送失败不回滚提交
- UserHub 分层加入组：所有连接加入 Game_{gId} 通用组，仅审核通过成员加入 PentestTeam_{teamId} 私有组
- Hidden 节点 Id=0 、 DisplayName="未知区域" 、 Description=null ，即使前端误渲染也不泄露真实信息
- 缓存键包含 solvedScoreItemKeys 指纹，提交后自动失效重建
轻微观察（非问题，无需修复） ：

- Penetration.tsx L188 onWheel 中 event.preventDefault() 在 React 被动监听器下可能无效，但不影响功能（滚轮仍可缩放）
- Penetration.tsx L310 serverTimeoutInMilliseconds = 2 小时 较长，但配合 withAutomaticReconnect() 可接受
- Penetration.tsx L117 visibleDepths 未 memoize，但数据量小，性能影响可忽略