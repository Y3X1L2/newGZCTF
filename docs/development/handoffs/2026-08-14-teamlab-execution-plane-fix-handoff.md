# TeamLab 执行模型决策逻辑修复交接（2026-08-14）

- 交接对象：负责 TeamLab 执行面实机测试与代码质量审查的后续 agent。
- 上游依据：`docs/development/handoffs/2026-08-14-teamlab-execution-plane-acceptance-report.md`。
- 工作分支：`codex/teamlab-high-performance-a`。
- 用户约束：不做补丁式修复；不静默降级；V2 是默认执行模型；V1 只保留为显式迁移模式；避免低效重复测试；最终由测试 agent 完成实机验收。

## 根因

旧实现把“是否启用 V2”作为布尔配置，并在 `TryApplyExecutionPlansAsync` 返回 `false` 时静默走 V1：

- 能力不足、节点缺失、secrets 不支持等原因全部吞掉，无日志、无审计、无状态标记。
- runtime 不落库执行模型，清理侧只能靠 `UsesExecutionPlanV2` 等派生判断。
- 配置可空，DI 或配置断裂时静默退化到 V1。
- Agent 端仍保留两套执行入口，主站无法确认实际走了哪条路径。

## 修复内容

### 1. 执行模型成为显式契约

- 新增共享契约 `GZCTF.TeamLab.Contracts.TeamLabExecutionModel`：`V1=0`、`V2=1`。
- 主站 `TeamLabNetworkConfig.ExecutionModel` 默认 `V2`，并增加 `ValidateOnStart` 校验，非法枚举值启动即失败。
- Agent `AgentTeamLabConfig.ExecutionModel` 同样改为显式枚举，默认 `V2`。
- Agent 同步契约 `TeamLabDataPlaneSyncConfiguration` 与 `AgentMaintenanceService` 均传/写枚举。

### 2. runtime 持久化执行模型

- `TeamLabRuntime.ExecutionModel` 默认 `V2`，实体配置转字符串、长度 16；不配置数据库默认值，确保显式 `V1` 写入不被数据库默认覆盖。
- 新迁移 `20260814000000_AddTeamLabRuntimeExecutionModel`：
  - `ALTER TABLE "TeamLabRuntimes" ADD COLUMN IF NOT EXISTS "ExecutionModel" character varying(16) NOT NULL DEFAULT 'V2';`
  - Down 为 `DROP COLUMN IF EXISTS`。
- 新迁移 `20260814010000_RemoveTeamLabRuntimeExecutionModelDefault`：
  - Up 为 `ALTER TABLE "TeamLabRuntimes" ALTER COLUMN "ExecutionModel" DROP DEFAULT;`，避免 EF 因枚举 CLR 默认值 `V1` 与数据库默认 `V2` 不一致而产生 sentinel 告警，并确保显式 V1 迁移模式真实落库。
- `AppDbContextModelSnapshot` 已同步：补 `IsScenarioBuild`、`ExecutionModel`，并将 `TeamLabExecutionPlanSnapshots.PlanDigest` 修正为 `varchar(96)`。
- 已用 `dotnet ef migrations has-pending-model-changes` 验证模型无漂移。
- `TeamLabRuntimePlanner` 在创建和 reset 时把 `_network.ExecutionModel` 写入 runtime。

### 3. 部署决策不再静默降级

- `TeamLabShardDeploymentService.DeployAsync` 按 `runtime.ExecutionModel` 显式 switch：V2 走执行计划，V1 走显式 legacy 路径，非法值抛错。
- 删除 `TryApplyExecutionPlansAsync` 的 `return false` 静默分支，改为 `ApplyExecutionPlansAsync`，失败直接抛明确异常。
- V2 能力检查使用 `AgentCapabilityEvaluator.MissingFeatures(node, requiredFeatures)`，异常和 warning 日志均包含节点名与缺失 feature。
- V2 需要 `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1`、`teamlab.artifact-cache.v2`；含 VM 再加 `teamlab.libvirt.native.v1`；含 Docker 再加 Docker 能力。
- V2 不支持用户 secrets 时在创建/reset 阶段返回 422，部署阶段再防御一次；不再回落 V1。
- 审计事件 detail 增加 `executionModel`，V1/V2 成功路径可区分。

### 4. 清理与投影

- 清理按 runtime 持久化的 `ExecutionModel` 分支：V2 只使用已持久化执行计划快照，V1 只走显式 legacy 清理。
- 移除了 `BackfillMissingExecutionPlanSnapshotsAsync`、`UsesExecutionPlanV2`、`EnableExecutionPlanV2` 等旧派生判断。
- 投影模型和 Open API 增加 `ExecutionModel`。

### 5. 测试适配

- 部署与清理单元测试显式声明 `ExecutionModel`；V1 清理测试不再依赖“默认 V2”误入 V2 分支。
- 定向验证通过：`TeamLabDeploymentOrchestrationTests` + `TeamLabExecutionLifecycleTests` 18 用例、`PenetrationTeamLabLifecycleTests` + `NodesControllerTests` + `TeamLabExecutionPlanV2Tests` + `TeamLabCleanupOwnershipTests` + `TeamLabVmArtifactSafetyTests` 57 用例，全部通过。
- `dotnet build src/GZCTF.slnx -c Release` 通过，`git diff --check` 通过。


## 部署记录（2026-08-14 release 2）

- 环境：`10.0.7.118`，用户 `whoami`，服务 `gzctf.service` / `gzctf-agent.service`，入口 `http://127.0.0.1:8080/`。
- 发布物：`teamlab-execution-model-fix-20260814-2.tar.gz`，SHA-256 `e1fc98af06e04fa3977920a8755ac836aa6231b7275e7be4dc514eb7df6f081b`。
- 软链：`/opt/gzctf/publish -> /opt/gzctf/releases/teamlab-execution-model-fix-20260814-2/publish`。
- 数据库迁移头：`20260814010000_RemoveTeamLabRuntimeExecutionModelDefault`；`TeamLabRuntimes.ExecutionModel` 列默认值已移除（`column_default` 为空）。
- 服务状态：`gzctf.service`、`gzctf-agent.service` 均 active，首页 HTTP 200；release 2 服务自 2026-08-14 02:21:32 启动后日志无 ExecutionModel sentinel 告警（旧日志中的告警来自 release 1）。
- Agent：118 本机 `/usr/local/bin/gzctf-agent` SHA-256 `1372a8a4972ae7c2d03d3e583a9fad1df4f3d5edab385a16db08d25dd8d8c57d`，与发布包内 agent 二进制一致。
- 配置：主站与 Agent 使用 `ExecutionModel=V2`（已从 `EnableExecutionPlanV2=true` 迁移）。
- 125 状态：本机代码修复与 118 发布已完成；125 Agent 同步与能力上报需要测试 agent 通过节点管理 `sync-agent` 继续，V2 双节点验收前必须确认 125 心跳能力包含 `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1` 与 `teamlab.artifact-cache.v2`。
## 测试 agent 验收清单

1. 数据库升级：从现有迁移链执行 `20260812113416`、`20260814000000`、`20260814010000`，确认 `TeamLabRuntimes.ExecutionModel` 无数据库默认值（`column_default` 为空），显式 `V1` 写入可真实落库，旧行不回填错误。
2. 配置校验：`TeamLabNetworkConfig.ExecutionModel` 填非法值启动应失败；缺配置默认 V2。
3. V2 可达：双节点具备 V2 能力时创建 runtime 必须走 `execution-plan/apply`，快照表有行，审计 detail 有 `executionModel=V2`。
4. fail-fast：任意节点缺 V2 feature 时创建/部署应明确报错，包含节点名和缺失 feature，不得回落 V1。
5. secrets：V2 下带用户 secrets 创建应 422；`GZCTF_SENSOR_*` 平台注入密钥不受影响。
6. 清理：V2 runtime 销毁必须基于快照，V1 runtime 销毁仍走显式 legacy 清理；结束后快照、队列、容器、VM、网络无残留。
7. Agent 同步：118 Agent 已与 release 2 一致；125 通过主站节点管理 `sync-agent` 同步后，确认 Agent 配置 `ExecutionModel` 与心跳能力清单一致，且包含 V2 所需 feature。


## 2026-08-14 release 4 部署与 OVN 清理修复

- release `teamlab-execution-model-fix-20260814-4` 已原子部署到 118：
  - 发布包 SHA-256 `55c0495b7828ff21f953d6bf6726d30aa6bda18366fbd71aaaa597883b0d3ddf`
  - Agent SHA-256 `8e1edf33b4a462bbda94013f902ffb2c0c7d1f08d7bcf7caf68cae33511eb88d`
  - 软链 `/opt/gzctf/publish -> /opt/gzctf/releases/teamlab-execution-model-fix-20260814-4/publish`
  - `gzctf.service`、`gzctf-agent.service` active，首页 HTTP 200；无新增迁移，数据库已最新。
- 125 已通过平台 `sync-agent` 同步至 release 4，节点 API 显示 `agentBinarySha256=8e1edf...`，能力清单包含 `teamlab.execution-plan.v2`、`teamlab.ovs-ovn.v1`。
- runtime 131（`019ffe05-780c-7a29-8945-6aa7ac700aa7`）已成功销毁：Status=10 Destroyed、LastError 清空、快照 0 行、OVN `gzctf` 资源 0 行。
- 存量分布：V1=122（含 1 Ready、36 Active、85 Destroyed）、V2=2（Scheduled=105、Destroyed=131）；存量 V1 runtime 未被触碰，迁移不再把存量行回填为 V2。

### OVN 清理根因与修复

- 根因：`TeamLabOvnNetworkProvider.RemoveAsync` 对没有 `name` 列的表（`Logical_Router_Static_Route`、`Logical_Router_Policy`、`DHCP_Options`、`DNS`）按 `name` 删除，导致 OVSDB 事务解析失败，V2 清理永久卡死；同时 `external_ids/options/records` 用 JSON 对象编码，不符合 OVSDB `["map", [["k","v"],...]]` 编码，插入也会失败。
- 修复：
  - 新增 `OvsdbJsonCodec`：`Map(...)`、`GetMapValue(...)`、`OwnedWhere(plan)` 按 runtime + generation + plan digest 生成 ownership 条件。
  - `TeamLabOvnNetworkProvider` 拆分 `BuildApplyOperations` / `BuildRemoveOperations`；所有 map 字段改为 OVSDB map 编码；清理按 ownership 删除相关表，不再依赖 `name` 列；Identity 与 digest 读取走 codec。
  - `TeamLabOvsAttachmentProvider` external_ids 改 map 编码，清理计数允许 0/1，残留幂等收敛。
  - `OvsdbJsonRpcClient` 操作级错误带表名/索引/error/details；transport failure 只重连一次。
- 验证：`TeamLabOvnNetworkProviderTests` 2/2、`OvsdbJsonRpcClientTests` 6/6、Agent Release build 通过。用户要求不重复低效测试，实机矩阵交给测试 agent。

## 2026-08-14 release 5：OVN named-uuid 与观测读取修复

### 测试 agent 报告的两个阻塞项

1. 125 上 `execution-plan/apply` 约百毫秒失败，主站分类为 `network operation did not complete`。
2. 125 上 `observations/read` 持续 400 invalid cursor，主站日志持续出现 Agent call failed。

### OVN apply 根因

- 125 release 4 日志显示 `OVSDB transaction operation 1 failed: syntax error (named-uuid string is not a valid <id>)`。
- OVSDB 要求 `uuid-name` / `named-uuid` 是合法 UUID；旧实现使用 `gzctf_router_xxx`、`gzctf_dhcp_xxx` 等字符串，导致事务语法错误，而错误被包装成不明确的 `OVN transaction failed.`。
- 修复：
  - `TeamLabOvnNetworkProvider` 新增 `StableUuid(...)`，基于 SHA256 派生稳定 UUID，所有 insert 的 `uuid-name` 与 References/NamedUuid 统一使用 UUID。
  - `AllResourcesPresentAsync` 改为按表统计 ownership 行数，不再依赖不存在的 `name` 列。
  - apply/cleanup 异常透传真实 OVSDB 原因。
  - `TeamLabOvsAttachmentProvider` 的 named-uuid 同样改为合法 UUID，并透传真实错误。

### observations/read 根因

- 旧 `ReadObservations` 先调用 `AcknowledgeAsync`，其中 `WaitForPersistenceAsync` 无限等待磁盘持久化；主站把 `acknowledgeThroughSequence` 推进到最新游标后，只要存在尚未落盘的记录，读请求就会卡到超时，Agent 返回 400 invalid cursor。
- 修复为持久化水位模型：
  - `ObservationBatchSpool.Read(...)` 返回记录并额外返回 `PersistedThrough`。
  - Agent/主站 DTO 同步增加 `PersistedThroughSequence`。
  - 主站只把 `sequence <= PersistedThroughSequence` 的记录视为可确认，未持久化记录返回但不推进游标，下轮重试。
  - 删除无限等待持久化及相关死代码；`AcknowledgeAsync` 只做已落盘水位内的内存释放与确认文件写入。
  - 已知取舍：Agent 进程崩溃时，已返回但未落盘的记录可能丢失；当前写入者先刷新落盘，collector 后读，实际窗口很小，后续如需更严格语义再按“确认水位不得超过持久化水位”补齐。

### 部署事实

- release `teamlab-ovn-observation-fix-20260814-5` 已原子部署到 118：
  - 发布包 SHA-256 `d2c0ad03beb81ffe9acd0e658430c3551b87e02ac7bc0335c62d76f5a9d4b298`
  - 初始 Agent SHA-256 `c00f629eb3671b3e6ddf65f059e025a5cab35813fbc497a34155fe9f0c049e18`
  - 软链 `/opt/gzctf/publish -> /opt/gzctf/releases/teamlab-ovn-observation-fix-20260814-5/publish`
  - `gzctf.service`、`gzctf-agent.service` active，首页 HTTP 200，无新增迁移。
- 118、125 已通过平台 `/sync-agent` 同步到 release 5，随后追加 Agent 增量修复（runtime-signal 409 收敛），两节点最终 Agent SHA-256 `9d9444bfc84fbad173ec0938dc5d426df4b067b0c93c695951706d6bfd168489`。
- 125 实测：
  - `observations/read` 稳定 200，耗时约 2ms；
  - 新 Agent 启动后旧冲突信号被记录一次并确认，ack 文件存在，`Runtime signal delivery failed` 不再重复。

### 新增 Agent 增量修复：runtime-signal 409 收敛

- 现象：125 上旧 runtime 信号 journal 与主库同一 operation/sequence 的 payload 不同，主站返回 409；旧 Agent 把 409 当普通失败无限重试，每 2 秒刷一条错误日志。
- 修复：`AgentRuntimeSignalPublisher` 对 `409 Conflict` 视为确定性终态，记录服务端原因并确认该序列，不再重试；其他失败仍按原逻辑保留待重试。
- 验证：`AgentRuntimeSignalJournalTests` 4/4 通过（含新增 `PublishPendingAsync_AcknowledgesTerminalConflict`），Agent Release build 通过。

### 测试 agent 继续验收

- V2 双节点正常链路：单/双节点 Docker、Linux VM、Windows VM，4/20/50 资产，4/8 网段。
- OVN 收敛与清理：apply/cleanup 后无 OVN、快照、容器、VM、overlay 残留。
- 观测链路：TCP/UDP/ICMP 元数据、spool 恢复、游标连续性、多轮采集无 400。
- 生命周期：预热零传输、pause/resume/reset/destroy、存量 V1 runtime 不受影响。
- 高危并发：重复提交、digest 冲突、Agent 重启、并发 300 请求、创建中断、故障注入。
- 验收结论需以页面/API 真实调用、日志与清理核对为准，不能只以接口 200 判定完成。
