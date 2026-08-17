# TeamLab B 阶段（功能控制面）能力资源实现报告

> 日期：2026-08-17
>
> 分支：`codex/teamlab-capability-closure-b`（基于 `codex/phase-09-teamlab-networking` @ `d56cafc`）
>
> 设计依据：`docs/superpowers/specs/2026-08-11-teamlab-foundation-performance-and-capability-upgrade-design.md` 第 14/15/17 节（工作流 B：商业资源池与第四层能力）
>
> 边界遵守：`docs/development/handoffs/2026-08-15-yinyu-platform-total-handoff.md` 第 3/9 节。未触碰 Agent 执行面、ImageDistributionService 物理回收细节与任何带未提交改动的协作者文件。

## 1. 范围结论

工作流 B 的既有能力（topology/release/preparation/rollout/runtime/remote/traffic/capture/webhook/scopes）已在此前轮次落地。本分支补齐设计第 12 节确认缺失的第四层产品对象与资源池投影，即 B 阶段唯一"待建设"部分：

| 能力 | 本轮状态 |
| --- | --- |
| 设备包目录（§15.1） | 已实现：注册/查询/启停/归档 + 外部只读 API |
| 链路和网络策略（§15.2） | 已实现：8 类策略、按类参数校验、声明式应用/手工恢复/定时恢复/审计留痕 |
| 现场连接器（§15.3） | 已实现：登记/健康/归档 + 独占租约生命周期 |
| 资源池投影（§14） | 已实现：计算节点/模板/节点缓存（含用途引用计数）只读投影 |
| 缓存用途引用（§7.2） | A 阶段已在 HPA 分支落地（`ImageDistributionReference` kinds），本轮只在资源池中消费其计数 |

## 2. 交付物

### 2.1 领域与持久化

- `Domain/TeamLabDevicePackage.cs`：不可变 `(Name, Version)` 目录项；OCI/VM 制品引用、digest、资源需求、端口、参数 schema、健康声明、协议事件类型。JSON 一律存规范化形式，语义相等即字符串相等。
- `Domain/TeamLabConnector.cs` + `TeamLabConnectorLease.cs`：连接器（6 种类型、scope 授权边界、默认独占、显式共享容量）与租约。独占性由数据库过滤唯一索引物理保证：
  - `(ConnectorId, RuntimeId) WHERE "ReleasedAt" IS NULL`：同一对活动租约唯一（幂等占用的物理兜底）。
  - `(ConnectorId, Slot) WHERE "ReleasedAt" IS NULL`：容量不超过声明值（并发竞争失败方得到稳定 409，不超卖）。
- `Domain/TeamLabLinkPolicy.cs`：runtime + network/asset 范围的策略实体，状态机 `Active/Recovered/Failed`，恢复来源 `Scheduled/Manual/RuntimeDestroyed`。`(RuntimeId, NetworkKey, AssetKey, Kind) WHERE Status=1` 唯一索引保证同一链路同类别只有一条活动策略（声明式 upsert 的物理兜底）。
- 迁移 `20260816182635_AddTeamLabCapabilityResources`：4 张表、上述索引、FK 全部 Restrict（runtime 销毁不级联删除审计事实，由 Worker 显式收敛）。

### 2.2 应用服务

- `TeamLabDevicePackageService`：注册校验（slug、版本字符集、sha256 digest、端口/协议去重、健康声明与参数 schema 形状、非负资源），`(Name,Version)` 冲突 409；列表游标分页 + 名称过滤；归档后对外 404。
- `TeamLabConnectorService`：登记（scope 必须存在）、健康上报、归档（有活动租约 409）；`Acquire` 幂等（同对返回既有租约）、槽位分配、竞态映射为 `connector_occupied`；`Release` 幂等；`ReleaseRuntimeLeasesAsync` 为销毁路径的批量释放入口。不可达连接器拒绝分配；`CleanupPending/Destroying/Destroyed` 运行时拒绝占用。
- `TeamLabLinkPolicyService`：`Apply` = 校验 runtime 活性 + 当前 generation 网络/资产归属 + 按类参数规范化 → 活动策略幂等 upsert（同参数幂等、异参数 409 `link_policy_conflict`、并发竞争收敛到胜者）；`Recover`（手工，幂等）；`RecoverDueAsync`/`CloseDestroyedRuntimePoliciesAsync`（Worker 用的有界批处理 set-based 收敛）。
- `TeamLabResourcePoolService`：计算节点（能力/容量/负载/fabric 状态，不含 HostAddress）、模板（不含 RegistryUrl/Auth/LocalFilePath）、节点缓存（分发状态 + 活动用途引用计数）。序列化层测试断言执行面地址与凭据不出现在响应中。
- `TeamLabCapabilityResourceValidation`：共享校验/规范化助手（slug、文本、版本、digest、JSON 规范化、数字/端口/枚举/CIDR/地址、int 游标）。

### 2.3 API

- 开放 API（`/api/open/v1/teamlab`，均为 API-token + scope 授权）：
  - `GET device-packages`、`GET device-packages/{id}`（`teamlab.device-packages:read`）
  - `GET connectors`、`GET connectors/{id}`、`POST connectors/{id}/leases`、`POST connectors/{id}/leases/release`（占用/释放要求 runtime 所在 scope 可写）
  - `POST link-policies`、`GET link-policies?runtimeId=`、`POST link-policies/{id}/recover`（策略 scope 随 runtime）
  - `GET resource-pools`、`GET resource-pools/node-cache`（`teamlab.resource-pools:read`）
- 管理 API（`api/admin/teamlab`，`[RequireAdmin]` cookie）：设备包注册/启停/归档；连接器登记/健康/撤销租约/归档。
- 新增 6 个 scope 常量并入 `ApiTokenScopes`；`TeamLabScopeAuthorizationService` 增加 `RequireLinkPolicyScopeAsync`（策略未找到 → 404，不泄露存在性）。
- 错误契约统一 `TeamLabApiContractException` → problem+json 稳定 code，与既有开放 API 语义一致。

### 2.4 生命周期 Worker

`TeamLabCapabilityResourceWorker`（30 秒周期，批上限 200，全部幂等 set-based）：

1. 释放已销毁 runtime 的连接器活动租约（`RuntimeDestroyed`）。
2. 恢复到期的定时恢复策略（`Scheduled`）。
3. 关闭已销毁 runtime 的活动策略（`RuntimeDestroyed`）。

失败仅记录错误日志，不中断下一轮；进程中断无副作用（无部分状态）。

## 3. 验证证据

| 门禁 | 结果 |
| --- | --- |
| `dotnet build src/GZCTF.slnx -c Release` | 0 错误 0 警告 |
| 新增单测（4 个文件，27 例） | 27/27 通过 |
| `dotnet test GZCTF.Test`（全量） | 837/837 通过 |
| TeamLab 定向（`FullyQualifiedName~TeamLab`） | 293/293 通过 |
| NSwag 对 `JsonElement?` 契约的文档生成 | 独立探针项目验证：`/openapi/open-v1.json` 200，schema 属性存在（NSwag 14.6.3，net10.0 TestServer） |
| `git diff --check` | 通过（无空白错误） |

集成测试（Testcontainers/OpenAPI 契约快照）本机 Docker 不可用未运行，按交接书 §7.2 不作为通过证据。`docs/commercialization/openapi/open-v1.json` 快照为运行时转储产物，新增端点为纯增量（非破坏），快照刷新随下次部署/契约验证轮次完成。

## 4. 测试覆盖要点

- 设备包：注册往返（规范化元数据）、同版本 409/新版本成功、digest 格式拒绝、tcp 健康缺端口 422、名称过滤 + 游标翻页、停用/归档后外部 404。
- 连接器：跨 runtime 独占 + 同 runtime 幂等、释放后再分配、共享容量边界（第 3 个 409）、不可达/销毁 runtime 拒绝、scope 绑定对其他 scope 隐身（列表与单体均 404/不含）、有租约时归档 409、占用投影。
- 链路策略：参数规范化存储 + 同参数幂等、异参数需先恢复、6 组非法参数 422（含 access-rule/nat/link-break）、access-rule/nat 可选字段接受、未知网络/资产 422、过去时间 RecoverAt 422、销毁 runtime 409、状态过滤与非法过滤值。
- 资源池：节点/模板投影正确且序列化结果不含 HostAddress/RegistryUrl/RegistryAuth；节点缓存含分发状态与引用计数。

## 5. 设计取舍说明

1. **同步写而非 ApiOperation**：scope 归档、租约占用/释放、策略应用/恢复都是单一事务内完成的控制面状态变更（无跨节点执行等待），按外部 API 标准仅异步写需要 `Idempotency-Key`；这些操作用自然幂等语义（同对租约/同参数策略）替代幂等键，行为可预测且不会产生悬挂 operation。
2. **数据面执行归工作流 A**：链路策略与连接器挂载的 Agent/OVS 执行（netem/mirror/物理接入）属共享执行契约范围。本轮交付控制面对象、期望状态与审计事实；`TeamLabExecutionPlanV2` 携带策略/连接器需要按设计 §3.3 冻结流程单独小提交并经双方确认，不在本分支越界实现。
3. **runtime 创建/销毁编排不侵入**：`TeamLabRuntimeOrchestrator` 带协作者未提交改动，本分支不改它。销毁闭环由 Worker 依据 runtime 终态收敛（数据库事实驱动），创建侧集成（拓扑引用连接器/设备包）留待编排器文件干净后的增量小提交。
4. **审计载体**：策略/租约实体自带完整时间线（applied/recovered/origin、acquired/released/reason），事件仅在需要跨资源检索时才进 `TeamLabEvent`，避免双事实。

## 6. 后续（不阻塞本轮）

1. 契约快照 `open-v1.json` 刷新 + 前端生成类型审 diff（需可运行环境）。
2. `TeamLabExecutionPlanV2` 增量携带 link policy / connector 挂载（A/B 双方确认的冻结契约变更）。
3. 编排器集成：runtime create 时按拓扑声明 acquire 连接器（失败 → validation 阶段错误），destroy 直接调用 `ReleaseRuntimeLeasesAsync`。
4. vNext 前端面板（设备包目录、连接器管理、策略控制、资源池视图）与浏览器验收。
5. 集成测试补 ExecuteUpdate 路径（Worker 收敛）与 PostgreSQL 前向迁移验证（Testcontainers 可用时）。

---

# 第二轮：深度检查与闭环完善（2026-08-17 追加）

> 对照知名组网平台基线与设计文档 §13–§18，对 B 部分做功能支持性审计后补齐的增量。两轮合计提交 `5696c82` + `feat(teamlab): close capability lifecycle and external API gaps`。

## 1. 审计结论（对照设计 §17 外部 API 资源表）

| 资源 | 审计前 | 本轮动作 |
| --- | --- | --- |
| control scopes | 缺 archive（仅 admin） | 开放 `POST scopes/{id}/archive`（幂等，管理员 token） |
| topologies | 缺 clone / diff | 开放 `POST topologies/{id}/clone`（重校验镜像/设备包/连接器引用，失败返回稳定错误码）；diff 留待作者 UI 轮次 |
| releases | 缺 archive | 实体增加 `IsArchived/ArchivedAt`，开放归档动词；规划器拒绝从已归档版本创建 runtime（409 `release_archived`），历史与在跑 runtime 不受影响 |
| preparations | 缺 cancel/release | 未实现（准备语义属 A 阶段用途引用账本，跨工作流；列入后续） |
| rollouts / runtimes | 完整 | 无动作 |
| resource pools / device packages / connectors / link policies | 第一轮已建 | 增强：流量 port 结构化筛选、抓包列表、链路策略管理面 |
| remote sessions | 开放面缺失 | 新增 `teamlab.remote-sessions:read/write` scope 与四个端点：可用性批量查询、创建、查询、关闭；connect/终端代理保留在浏览器面（外部平台编排会话，不转发终端） |
| observations | events/traffic/paths/pcap 已有 | 抓包补 `GET runtimes/{id}/captures` 游标列表；协议事件上报依赖设备包运行时（后续） |

## 2. 生命周期闭环（工控 / 协议模拟 / 虚实结合）

1. **拓扑资产绑定**：`TeamLabTopologyAsset` 增加 `DevicePackageId`（FK Restrict）、`DevicePackageParametersJson`（规范化 JSON）、`ConnectorId`（公共 ID 引用，无 FK——归档连接器不破坏历史版本可读性）。贯穿契约（v1 不携带，v2 携带）、编解码（发布冻结 `DevicePackageDigest`，同镜像 digest 模式）、结构校验（参数必须为 JSON 对象、无包不带参）、应用校验（包存在/启用/未归档/资产类型匹配、连接器存在/未归档）、规划器（重校验 digest 漂移 → 409）与 runtime 资产投影。
2. **连接器租约进规划事务**：`PlanGenerationAsync` 内获取租约——独占竞争失败即创建失败（稳定 `connector_occupied`），后续规划失败随事务回滚；reset 复用同 runtime 幂等语义。
3. **销毁同步收敛**：`FinalizeGenerationAsync` 在 `markRuntimeDestroyed` 时同步释放连接器租约（`RuntimeDestroyed`）并关闭活动链路策略（`RuntimeDestroyed` 恢复来源）；代际重置保持租约与策略（真实接入不因重建丢失）。Worker 仍是崩溃恢复兜底。

## 3. 前端（产品化落地）

- **组网资源页**（`/admin/teamlab/resources`，导航"资源"组）：设备包目录 / 现场连接器 / 节点制品缓存三个分区，游标分页 + 详情抽屉 + 登记对话框（设备包登记只登记外部制品引用，明确"平台不制作镜像内容"；连接器登记强调"场景只引用 ID"）。生命周期操作（启停/归档/健康标记）带确认对话框；列表断言不含执行面地址。
- **运行时详情"链路策略"页签**：策略列表（参数摘要、状态、恢复来源）、应用对话框（类型模板化参数、限定网段/资产、定时恢复分钟）与手工恢复。
- **编辑器资产检查器"扩展能力"区**：设备包选择（按资产类型过滤、显示不可用占位）+ 参数 JSON（绑定才显示）+ 现场连接器选择；两者均可为空，普通镜像资产界面不变。模型字段为可选，兼容协作者进行中的节点创建改动。
- 遵守 vNext 边界：feature adapter（contracts/parsers/api 工厂 + SWR keys）、CSS Module + 语义 token、无 inline style、TSX ≤650 行、中文硬编码与既有页一致。

## 4. 验证证据（第二轮）

| 门禁 | 结果 |
| --- | --- |
| `dotnet build src/GZCTF.slnx -c Release` | 0 警告 0 错误 |
| `dotnet test GZCTF.Test` | **846/846 通过**（新增 CapabilityBinding 5 例 + ExternalApiClosure 4 例；含恢复协作者 WIP 后其 `DraftEditorLayoutUpdate` 用例回归通过） |
| `pnpm build`（locales/lint/tsc/architecture/test/vite/预算） | exit 0 |
| `pnpm test` | 248/248 通过（新增资源页 4 例、链路策略面板 2 例、presentation 3 例） |
| 协作者 WIP | 3 个文件先备份至 `.artifacts/collab-wip/` 并 stash，实施后恢复；仅 `TeamLabReleaseCodec.DecodeExecution` 一处冲突，按双方意图合并（其 `Deserialize<T>` 助手 + 我的设备 digest 贯穿），恢复后全量测试证明共存无回归 |

## 5. 迁移

- `20260816190544_BindTeamLabCapabilityResources`：拓扑资产与 runtime 资产的绑定列 + 索引 + 设备包 FK。
- `20260816192540_TeamLabCapabilityClosure`：release 归档列。

## 6. 剩余（明确不在本轮）

1. 链路策略/连接器挂载进入 `TeamLabExecutionPlanV2`（冻结契约变更，需 A/B 双方确认）。
2. preparations cancel/release、拓扑版本 diff、协议事件上报与外部查询。
3. `open-v1.json` 契约快照刷新（需可运行环境；本轮新增均为纯增量端点）。
4. 真机验收：双节点链路损伤实际生效、连接器独占竞争、克隆/归档全链路（测试侧）。
