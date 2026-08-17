# TeamLab 组网底座代码审查交接

日期：2026-08-14
交接对象：负责对 TeamLab 执行面代码进行独立代码审查并直接修复的 agent。
本文件是审查入口：先读本文，再按“必读文档”顺序阅读；审查结论和修复必须落到代码，最后写一份汇报文档。

## 1. 项目简介

本项目是一个 CTF / 培训 / 商业化比赛平台（YINYU / GZCTF 衍生的模块化单体），组网模块 TeamLab 是平台内独立的商业化组网底座：

- 主站（`src/GZCTF`）：负责权限、比赛、拓扑、版本、计划、容量、调度、operation、审计和期望状态。
- Agent（`src/GZCTF.Agent`）：独立执行面，只执行已经校验的本机操作，不读取比赛、计分、权限实体。
- 网络数据面：OVN/OVS 承担逻辑网络、DHCP/DNS/路由/ACL；WireGuard 承担玩家授权入口。
- VM 生命周期：原生 libvirt API（不再依赖 virsh/virt-install 文本解析）。
- Docker：Docker Engine API。
- 事实来源：PostgreSQL 保存业务与运行事实；Redis 只承担缓存、协调和高频缓冲；`DeploymentQueueTicket` 是统一部署队列，不建立第二套队列。

仓库根目录必须阅读：

- `AGENTS.md`：全仓协作规范、事实优先级、部署规则、验证门禁。
- `docs/development/current-state.md`：当前部署与验收事实（以它为准，不要以过时文档为准）。
- `README.md`：项目概览。
- `docs/platform-commercialization-master-plan.md`：商业化总纲。

## 2. 代码边界

主站与 Agent 通过共享契约 `src/GZCTF.TeamLab.Contracts/` 交互。契约包含：

- `Execution/TeamLabExecutionPlanV2.cs`：不可变执行计划（runtime、generation、shard、network digest、plan digest、network owner、网络、资产、观测点、控制意图），自带完整校验。
- `Execution/TeamLabExecutionEventV2.cs`：执行事件（阶段、结果、错误分类、脱敏详情）。
- `Execution/TeamLabExecutionIdentityV2.cs`：稳定资源身份（host veth、VM TAP、domain、overlay 等命名规则）。
- `TeamLabExecutionModel.cs`：显式执行模型枚举（V1 / V2）。

调用方向固定：

```text
HTTP/Frontend -> Contracts -> Application -> Domain -> Infrastructure ports
Business Application -> Runtime Application -> Fleet/VM/TeamLab ports -> AgentClient -> Agent
```

- Controller 只做协议、授权、用例调用和 HTTP 映射，不编排 Agent 命令。
- Application 负责事务边界与用例编排，不拼接 shell。
- Agent 只执行本机已校验操作，不读取比赛/权限实体。
- 禁止 TeamLab 模块依赖 Penetration 实体或比赛计分服务。
- 前端通过公开契约与 feature adapter 交互，本工作流不修改前端。

## 3. 底座设计需求（审查时以此为准）

### 3.1 设计文档（必读）

- 总体商业化设计：`docs/superpowers/specs/2026-07-22-teamlab-commercial-control-plane-design.md`
- 组网网络商业化设计：`docs/superpowers/specs/2026-07-14-phase-09-teamlab-networking-commercialization-design.md`
- 高性能执行面设计：`docs/superpowers/specs/2026-08-11-teamlab-foundation-performance-and-capability-upgrade-design.md`
- 模块边界：`docs/commercialization/module-boundary-map.md`
- 外部 API 标准：`docs/commercialization/external-api-standard.md`
- 执行面进度与约束：`docs/development/teamlab-high-performance-execution-progress.md`（注意：该文件部分描述停留在“EnableExecutionPlanV2 默认关闭”阶段，与当前代码不一致；当前事实以 `current-state.md` 与代码为准）
- 最近验收报告与修复交接：`docs/development/handoffs/2026-08-14-teamlab-execution-plane-acceptance-report.md`、`docs/development/handoffs/2026-08-14-teamlab-execution-plane-fix-handoff.md`、`docs/development/handoffs/2026-08-14-teamlab-ovn-attach-fix-handoff.md`

设计基线摘要（必须遵守，不得为了“简单”破坏）：

1. 主站提交经过校验的不可变执行计划；Agent 批量执行本节点任务。
2. 正确性来自事务、唯一身份、运行代次、期望状态和真实 inventory，不来自增加等待、重复探测或无界重试。
3. 一个 runtime/generation 的网络变更在一个 OVN Northbound 事务中提交；通过配置版本和 chassis 收敛事实确认网络生效。
4. 原生 libvirt 承担 VM define/start/pause/resume/destroy/undefine；来宾就绪只使用已声明信号或明确端口健康检查。
5. 制品缓存按明确用途引用（Runtime / CompetitionPreparation / Rollout / ArtifactVerification），发布版本只保存依赖，不永久占用节点缓存；模板库主制品不因运行时销毁被删除。
6. 不允许静默降级：V2 执行模型是默认；能力不足、节点缺失、secrets 不支持必须显式报错并落审计。
7. 不新增第二套队列、工作流引擎或状态机；继续使用现有统一部署队列、容量账本和公开控制面。
8. 旧 bridge/router namespace/dnsmasq 路径在切换验收完成前必须保留；切换后删除旧主路径，不长期双轨。
9. Agent 只执行已校验的本机操作；平台不制作、不改造模板，镜像职责留在模板库/外部流水线。
10. 不新增公开 TeamLab API；`/api/open/v1` 资源契约不因本工作流改变。

### 3.2 已知设计张力（本轮重点审）

用户明确要求重点审查：代码是否简洁高效、是否过度设计、判定是否过于复杂、是否有为兼容旧错误路径保留的冗余分支。

以下是当前实现里最需要严格自审的区域：

- `TeamLabShardDeploymentService`（主站）同时保留 V2 `execution-plan/apply` 和 V1 legacy `shards/apply` 两条执行链。设计上过渡期允许 V1，但用户明确质疑：**为什么 V1 旧路径还在？执行模型判断是否已经变成累赘？** 请结合“切换策略”判断是否应删除 V1 主路径、清理分支和快照反向回填，而不是继续维护双轨。
- `TeamLabExecutionModelPolicy` 目前只承担“发现非平台密钥”，却以“Policy”命名；请判断这类小职责是否值得独立类，还是应内联到调用处。
- `TeamLabExecutionPlanV2.IsValid` 是一段很长的组合布尔校验，逐项规则混杂在表达式中；请判断是否清晰可维护，是否应拆成带原因的小校验，但**不要为拆而拆、不要重复校验**。
- Agent `TeamLabExecutionPlanExecutor.ApplyAsync` 有“已有 journal + inventory 全部 running 则幂等返回”的提前返回路径；请判断它是否与补偿清理、重复提交收敛语义冲突，是否掩盖了真实失败。
- VM 网络接入：VM XML 已声明 TAP 与 OVS interfaceid，但 `ApplyVmAsync` 尚未显式调用 OVS attachment。请判断 libvirt 自动创建 TAP 是否足以闭环，显式接入是否会造成重复创建，不要无依据增加复杂度。
- 主站网络 owner shard 先 apply、其余 shard 顺序 apply 的串行化：确认它是否符合“全局网络先收敛”的依赖，以及是否会对多节点性能形成不必要瓶颈。

## 4. 需要审查的代码范围

### 4.1 主站 TeamLab 执行面

- `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`（V2/V1 分支、计划编译、快照持久化、apply 与补偿清理）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabExecutionPlanCompiler.cs`（网络/资产/观测点编译、network digest、玩家网关）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabExecutionModelPolicy.cs`
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimePlanner.cs`（runtime 规划与执行模型选择）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`（生命周期编排）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs`（V1/V2 清理分支）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRouteApplicationService.cs`（基础设施意图构建）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabResourceNameFactory.cs`
- `src/GZCTF/Modules/TeamLab/Application/ITeamLabNodeExecutor.cs`
- `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- `src/GZCTF/Services/Fleet/AgentClient.cs`（Agent 请求/响应契约）

### 4.2 Agent 执行面

- `src/GZCTF.Agent/Services/TeamLab/TeamLabExecutionPlanExecutor.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabOvnNetworkProvider.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabOvsAttachmentProvider.cs`
- `src/GZCTF.Agent/Services/TeamLab/OvsdbJsonRpcClient.cs`
- `src/GZCTF.Agent/Services/TeamLab/OvsdbJsonCodec.cs`
- `src/GZCTF.Agent/Services/TeamLab/TeamLabOvnNaming.cs`
- `src/GZCTF.Agent/Services/TeamLab/LinuxNetworkAttachmentService.cs`
- `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`（V1/V2 WireGuard 分支）
- `src/GZCTF.Agent/Services/Vm/LibvirtTeamLabProvider.cs`
- `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- `src/GZCTF.Agent/Models/TeamLabModels.cs`

### 4.3 共享契约与测试

- `src/GZCTF.TeamLab.Contracts/Execution/TeamLabExecutionPlanV2.cs`
- `src/GZCTF.TeamLab.Contracts/Execution/TeamLabExecutionEventV2.cs`
- `src/GZCTF.TeamLab.Contracts/Execution/TeamLabExecutionIdentityV2.cs`
- `src/GZCTF.Test/UnitTests/TeamLab/TeamLabOvnNetworkProviderTests.cs`
- `src/GZCTF.Test/UnitTests/TeamLab/TeamLabExecutionPlanV2Tests.cs`
- `src/GZCTF.Test/UnitTests/TeamLab/TeamLabInterfaceNamingTests.cs`
- `src/GZCTF.Test/UnitTests/TeamLab/TeamLabExecutionLifecycleTests.cs`
- `src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommandBuilderTests.cs`

## 5. 审查要求（按优先级）

1. **功能与逻辑闭环**：从用户视角确认 V2 全链路（创建、镜像准备、网络 apply、Docker/VM 创建、健康检查、观测、暂停/恢复/销毁、清理）每个状态可解释、可恢复，失败有明确中文提示；不能靠刷新或猜状态。
2. **代码质量**：先问自己“这个功能是否真的有用、是否过度设计、能否更简洁稳定”，再动手。优先消除：重复分支、为兼容旧路径保留的冗余、可空配置兜底、静默降级、单函数过长、判定表达式过复杂、重复校验。
3. **解耦与边界**：确认主站与 Agent 只通过共享契约交互；Agent 不读业务实体；主站不直接拼接 shell；TeamLab 不依赖 Penetration/计分。
4. **安全性**：越权、身份/代次围栏、并发重复提交、创建销毁冲突、资源回收、事件游标、密钥与日志脱敏。
5. **性能**：不靠等待/重试；批量并发有界；网络一次事务；清理可重入；大场景（多网段、多资产、多节点）不出现命令风暴。
6. **文档一致性**：如果代码与设计文档冲突，以真实运行行为和当前 `main` 源码为准；发现文档过期要顺手修正或记录。

## 6. 修复与验证要求

- 直接修改代码时保持职责边界：主站/Agent/契约按上述范围；不修改 `/api/open/v1` 公开契约、前端、模板制作流水线。
- 禁止补丁式修复（延长等待、吞错误、临时兼容分支）。
- 改动后至少执行：

```powershell
git diff --check
dotnet build src/GZCTF.slnx -c Release
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~TeamLab"
```

如果改动涉及 Agent 或契约，还要保证 `src/GZCTF.Agent` 构建通过。实机验收（OVN/OVS、KVM、多节点）由另一个测试 agent 负责，本审查不以此替代。

- 审查汇报文档必须写清楚：每个问题（严重度、文件/行号、根因、建议修复）、已修复项、未修复项、验证结果、残留风险。不要只给结论不给证据。

## 7. 当前分支与部署事实

- 工作分支：`codex/teamlab-high-performance-a`（HEAD `8a5113d`，已推送 `origin/codex/teamlab-high-performance-a`）。
- 最近发布：`teamlab-ovn-attach-fix-20260814-9` 已部署 118（softlink 指向该 release，Agent SHA `3625447c...`）；125 尚未执行 release 9 的 `sync-agent`。
- 环境：118（`10.0.7.118`，用户 `whoami`，有 sudo）；125（`10.0.7.125`）Agent 需通过平台节点管理 `sync-agent` 同步。
- 不要动现有生产比赛、场景、运行时、VM、容器；所有验证使用独立命名资源。

## 8. 输出

审查完成后，把结果写入：

`docs/development/handoffs/2026-08-14-teamlab-code-review-report.md`

并在 `docs/development/current-state.md` 增加/更新对应结论。报告必须能被下一个 agent 直接用来继续修复或验收。