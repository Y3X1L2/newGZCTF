# Phase 7 Observability, Audit, and Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将系统日志、运行生命周期、镜像分发、节点、Agent、TeamLab、容器和 VM 收敛为可关联、可查询、可恢复的商业级观测控制面，使常见故障无需 SSH 登录服务器即可定位。

**Architecture:** PostgreSQL 保存追加式 `OperationalEvent` 作为结构化审计和生命周期历史，现有 `LogModel` 继续承载原始系统日志，`DeploymentQueueTicket`、`ImageDistributionRecord` 和 runtime 实体继续作为当前状态事实。运行链路使用稳定 correlation id 和 W3C trace context 串联，OpenTelemetry 负责 metrics/tracing，恢复协调器以数据库事实和 Agent 资源清单做周期核对，不引入第二套消息队列或外部日志事实源。

**Tech Stack:** .NET 10、EF Core 10、PostgreSQL 17、Serilog、OpenTelemetry、ASP.NET Core filters/middleware/HostedService、React 19、SWR、Mantine、GZCTF.Agent、xUnit、Testcontainers.PostgreSql。

---

## Implementation Progress

### 2026-07-13 计划编写基线

- 代码基线为 `4d4c6fe8d082ee74ea5f63ade2e9ecbbd576875d`，Phase 6 已将 Docker、培训/练习、AWDP、管理员测试容器、VM 和 TeamLab 收敛到统一 `DeploymentQueueTicket` 控制面。
- `LogModel` 只保存时间、级别、logger、状态、IP、用户名、消息和异常，无法按 correlation、ticket、节点、镜像、比赛、课程、题目或资源检索。
- `DatabaseSink` 在少于 50 条日志时依赖下一次事件唤醒，进程退出时不保证刷盘，不能承担可靠审计。
- 队列、镜像和 TeamLab 分别保存当前状态或局部事件，缺少统一事件分类、错误分类和跨模块关联。
- OpenTelemetry 已接入框架 instrumentation，但自定义业务 meter 只覆盖数据库治理与 Redis；`GZCTF.Cache` ActivitySource 未注册，运行队列、Agent、镜像、节点和恢复无业务 span。
- `RecoverStaleCreatingTicketsAsync` 只依据数据库中的容器、VM 和 TeamLab 状态决定完成、重放或失败，没有读取 Agent 上真实 Docker/KVM 资源清单。
- `/admin/logs`、`/admin/queue`、节点和镜像状态彼此独立，无法按同一 correlation 联动排障。
- 本阶段不部署生产服务器。按大单元开发和集中测试，全部实现后执行一次独立质量审查。

### 2026-07-13 大单元 1 完成

- 已新增稳定 event code、severity、outcome、error category 和 detail allowlist，业务代码只能使用集中常量。
- 已新增追加式 `OperationalEvent`、同事务 `IOperationalEventWriter`、结构化 system log scope、查询模型和数据库索引。
- 已为 `LogModel`、`DeploymentQueueTicket`、`ImageDistributionRecord` 预留 correlation、trace 和 typed error 字段。
- 已将 operational event 纳入 Phase 4 retention catalog，默认保留 180 天。
- 已生成 `ExpandPhaseSevenObservabilityAuditRecovery`、`BackfillPhaseSevenObservabilityAuditRecovery`、`ContractPhaseSevenObservabilityAuditRecovery` 三段迁移；Backfill 只导入活动 ticket、image distribution 和 TeamLab runtime snapshot，Contract fail closed。
- 集中门禁：事件/保留策略专项测试 `10/10`，EF `No changes have been made to the model since the last migration.`。

### 2026-07-13 大单元 2 完成

- `DatabaseSink` 已改为定时或批量 flush、失败批次保留、有限重试、最大缓冲保护和退出 drain；低流量日志不再依赖下一条日志触发。
- 数据库 sink 与 SignalR sink 统一复用 `LogModelFactory`，trace/correlation/event/error/node/ticket/resource 字段不再重复解析。
- 系统日志 API 已支持 correlation、logger、event code、node 和 resource 精确过滤，并继续使用 cursor 分页。
- 已新增 scoped `OperationalCorrelation` 和教师/管理员 mutation audit filter；普通 GET、heartbeat、外部 API 和非管理用户流量不进入该通用审计。
- 集中门禁：Phase 7 observability 与 retention 专项测试 `13/13`。

### 2026-07-13 大单元 3 完成

- 已注册 `GZCTF.Runtime`、`GZCTF.AgentClient`、`GZCTF.Operations` meters，以及 runtime、Agent、image distribution、TeamLab、cache ActivitySource；指标标签只使用 operation、result、stage、workload、error category 等有限集合。
- 已新增运行队列和节点状态快照 worker，按数据库 checkpoint 与 `INodeLiveStateStore` 汇总 queue depth、online、schedulable 和 overloaded，不把 ticket、队伍或节点 ID 写入 metric label。
- `OperationalCorrelation` 已改为基于 `AsyncLocal` 的 ambient context；所有 Agent HTTP 调用统一传播 `X-GZCTF-Correlation-Id`，建立业务 span，并记录稳定 operation、耗时、结果与 typed error category。
- Agent 已新增统一 correlation/error middleware 和稳定错误响应；认证、模型校验、Docker、KVM、镜像、TeamLab 与维护接口不再返回匿名异常正文，且 Agent 不引入 OTLP exporter。
- 主站 `AgentClientException` 已携带 `OperationalError`，非成功响应优先读取 Agent typed error；节点缺失、超时、传输失败、协议空响应、镜像校验失败均有稳定 category/code/retryable。
- 集中门禁：Release solution build `0` warning / `0` error；Phase 7 observability/Agent contract 专项测试 `21/21`。

## 0. Phase Boundary

### 0.1 Must Complete

- 建立唯一结构化 `OperationalEvent` 事实，不为队列、镜像、节点和 VM 分别创建平行审计表。
- 保留 `LogModel` 原始系统日志，补齐可靠刷盘、结构化字段和 correlation 查询。
- 冻结 `event-taxonomy.md` 中的 event code、outcome、severity 和 error category。
- 队列 enqueue、调度、阻塞、执行、延期、停止、重置、销毁、失败、取消和恢复全部产生事件。
- 镜像预分发、兜底拉取、校验、ready、失败、清理和 reconcile 全部产生事件。
- 节点注册、能力变化、上线/离线、可调度变化、Agent 同步和关键健康变化产生事件；正常 heartbeat 不逐次落事件。
- TeamLab 通过统一 recorder 同时写 TeamLab event 和 `OperationalEvent`。
- AgentClient 调用建立 span，传播 `traceparent` 和 `X-GZCTF-Correlation-Id`，统一记录耗时、节点、操作、结果和错误分类。
- 服务启动和周期恢复核对 PostgreSQL 当前状态、claim/reservation 与 Agent 资源事实。
- 管理端可按 correlation、时间、事件域、结果、错误分类和业务对象过滤，并从队列、节点和镜像跳转到关联时间线。
- 日志、事件、metrics 和 tracing 不输出 flag、token、WireGuard 私钥、密码、完整 userdata、镜像认证信息或未脱敏 Agent response body。

### 0.2 Explicitly Out of Scope

- 不引入 Elasticsearch、Loki、Kafka、RabbitMQ 或新的持久消息队列；OTLP 继续连接外部 collector。
- 不通过解析日志文本恢复业务状态。
- 不把普通 GET、heartbeat 或 metrics scrape 全量写成数据库事件。
- 不自动重建已经成功运行但随后在节点上丢失的环境；先修正为 degraded/failed 并提供标准重置入口。只对 Phase 6 已证明幂等的执行中 claim 过期进行自动重放。
- 不实现 TeamLab PCAP 数据面采集和流量分析；Phase 7 只记录抓包任务生命周期。
- 不重做全局视觉语言；排障页面使用 Phase 2 公共组件和全局样式层。
- 不保存任意请求/响应正文、shell 命令全文、cloud-init userdata 或异常对象序列化。

## 1. Current Code Facts

### 1.1 Raw Logs Are Not Audit Facts

- `src/GZCTF/Extensions/DatabaseSinkExtension.cs` 使用进程内队列和独立长任务。
- 当前 flush 依赖 batch 或后续 signal，低流量日志可长期不落库。
- `Dispose` 直接取消 token，没有 drain 和有界等待。
- `LogModel`、`LogMessageModel` 和 SignalR 消息没有 trace/correlation/event/error/resource/node 维度。

结论：修复 sink 可靠性并扩展结构化字段，但原始日志不能替代事件事实。

### 1.2 Runtime State Has No Unified History

- `DeploymentQueueTicket` 和 `ImageDistributionRecord` 每次更新覆盖旧 stage/status/error。
- `RuntimeSchedulingService`、`RuntimeExecutionService` 和 `DeploymentQueueService` 只写少量无结构 `SystemLog`。
- `TeamLabEvent` 是局部追加事件，但 event code 和对象字段没有全平台规范。

结论：新增一个结构化追加事件表，状态表继续保存当前事实。

### 1.3 Telemetry Is Infrastructure-only

- `TelemetryExtension` 已注册 ASP.NET Core、HttpClient、EF Core、Redis、Npgsql、AWS、gRPC、runtime 和 process instrumentation。
- 自定义 meter 只有 `DataGovernanceMetrics` 和 `RedisTelemetry`。
- `PlatformCache` 创建 `GZCTF.Cache` ActivitySource，但启动配置没有 `AddSource`。
- Agent 没有 exporter；主站 HttpClient instrumentation 已能建立网络 span，但缺稳定 operation span 和业务标签。

结论：主站增加低基数业务 metrics 和 ActivitySource；Agent 保持轻量，使用 W3C trace header、correlation header 和结构化错误响应接入。

### 1.4 Recovery Does Not Read Node Facts

- Phase 6 已有 stale Running ticket 的 Completed、SafeReplay、FailClosed 决策。
- 当前 inspect 只查询数据库状态。
- Docker Agent 已有 `ManagedBy=GZCTF`、`GZCTF.Generation` label；KVM 已有 libvirt generation description 和 sidecar。
- Agent 尚无平台托管资源 inventory API。

结论：增加只读 inventory 和周期 reconcile，禁止依赖名称猜测或读取宿主机非平台资源。

### 1.5 Troubleshooting Is Fragmented

- 系统日志只支持 level/cursor。
- 部署队列只显示 ticket 当前状态。
- 节点和镜像页面没有 correlation timeline。
- `DeploymentQueueViewService` 已有批量解析业务名称的基础，Phase 7 应复用，不逐行查询。

## 2. Frozen Architecture

### 2.1 Facts and Responsibilities

| Fact | Responsibility | Mutable |
| --- | --- | --- |
| ticket、image record、runtime entity | 当前状态、claim、重试和恢复输入 | 是 |
| `OperationalEvent` | 结构化审计和生命周期历史 | 只追加 |
| `LogModel` | 原始应用和系统日志 | 只追加 |
| OpenTelemetry | 实时 metrics/traces | 非业务事实 |

恢复逻辑只读取当前业务事实和 Agent inventory，不读取事件或日志决定状态。

### 2.2 OperationalEvent

字段冻结为：

- identity：`Id`、`OccurredAt`、`CorrelationId`、`TraceId`；
- semantics：`EventCode`、`Severity`、`Outcome`；
- error：`ErrorCategory`、`ErrorCode`、`Retryable`；
- content：`Message`、`DetailJson`；
- actor/owner：`ActorUserId`、`OwnerUserId`、`OwnerTeamId`；
- business scope：`GameId`、`CourseId`、`ChallengeId`、`ImageTemplateId`；
- runtime scope：`WorkerNodeId`、`DeploymentTicketId`、`TeamLabRuntimeId`、`VmInstanceId`；
- snapshots：`SubjectType`、`SubjectId`、`SubjectDisplayName`、`ResourceType`、`ResourceId`、`ResourceDisplayName`。

约束：

- 部署任务 correlation 使用 ticket id；镜像预分发使用 distribution record id；HTTP 管理动作使用 operation id 或 UUIDv7。
- `TraceId` 只保存 W3C trace id。
- `DetailJson` 只允许白名单键，最大 4096 字符；敏感键拒绝写入。
- display name 保存事件发生时快照，业务对象删除后仍可读。
- 索引只覆盖时间、correlation、ticket、node、event/outcome、team/game/course 和 template 查询。

### 2.3 Writer and Query

- `IOperationalEventWriter.Append` 只向 scoped `AppDbContext` 添加事件，不自行保存；生命周期状态和事件同事务提交。
- `AppendAndSaveAsync` 只供 HTTP filter、AgentClient failure 和后台无现成事务场景使用。
- writer 统一生成 correlation、读取 trace、校验 code、清理 detail、建立 Serilog scope 并输出结构化 system log。
- `OperationalEventQueryService` 使用 cursor 分页和批量名称解析。
- `TeamLabEventRecorder` 双写 TeamLab 局部事件与统一事件，调用方不再直接构造 TeamLab event。

### 2.4 Correlation and Trace

数据流固定为：

`HTTP Activity -> ticket id -> scheduler span -> execution span -> AgentClient span/header -> Agent log -> image/TeamLab/VM/container event -> admin timeline`。

- ticket 增加 `TraceParent`、`TraceState`，enqueue 时捕获。
- worker 从持久化 trace context 创建 consumer activity；解析失败时建立新 trace 并保留 correlation。
- `DeploymentExecutionContextAccessor` 继续承载 ticket/node/generation，并向 AgentClient 提供 correlation。
- Agent middleware 读取 correlation header，加入 logger scope，并在响应头返回。
- ticket/user/team/game 等高基数 ID 不进入 metrics label，只进入 span 和 event。

### 2.5 Error Model

`OperationalError` 固定包含 category、code、message、retryable，以及可选 HTTP status、node 和 operation。

分类固定为：Authorization、Validation、Conflict、Scheduling、Capacity、ImageRegistry、ImageTransfer、NodeUnavailable、AgentProtocol、AgentTransport、Docker、Kvm、Network、HealthCheck、Storage、Database、Cache、Unknown。

`AgentClientException` 携带 typed error。Agent 非成功响应返回 `AgentErrorResponse`，主站不得通过消息文本猜测错误类别。

### 2.6 Metrics and Spans

meters：

- `GZCTF.Runtime`：queue depth、enqueue、transition、blocked、wait/schedule/execute duration、recovery decision；
- `GZCTF.AgentClient`：call count/duration/failure；
- `GZCTF.Operations`：event write、log buffer/flush、node health、image distribution。

ActivitySources：

- `GZCTF.Runtime`：enqueue、schedule、execute、recover；
- `GZCTF.ImageDistribution`：claim、transfer、verify、cleanup；
- `GZCTF.AgentClient`：operation；
- `GZCTF.TeamLab`：plan、placement、deploy、cleanup；
- `GZCTF.Cache`：注册现有 source。

### 2.7 Recovery

Agent 新增 `GET /api/runtime/inventory`：

- Docker 只返回 `ManagedBy=GZCTF` 的容器；
- VM 只返回带 GZCTF generation marker 的 domain；
- 返回 stable identity、name/id、generation、state；
- 不返回环境变量、flag、command、userdata 或 credentials。

`RuntimeFactReconciliationService`：

1. 启动执行一次，之后按配置周期执行。
2. 使用 PostgreSQL advisory lease 保证多主站单 owner。
3. 按在线节点批量拉 inventory；Agent 不可达时只记录 unavailable，不把资源标为丢失。
4. matching fact 保持；identity conflict fail closed；在线 Agent 明确 missing 时修正数据库状态和遗留 reservation；offline/unsupported 保持 unknown。
5. stale Running ticket 优先使用 Agent fact，数据库状态作为辅助。
6. orphan resource 只记录和展示，本阶段不自动销毁。

## 3. Event Coverage

| Module | Required events |
| --- | --- |
| Runtime queue | enqueue、duplicate、cancel、blocked、scheduled、running、success、failure、replay、recovery |
| Capacity | reserve、confirm、release、expire、reconcile conflict |
| Docker/VM | create、extend、stop、reset、destroy、probe、access open |
| Image distribution | queued、claim、transfer、verify、ready、failure、retry、cleanup |
| Node | register、deregister、online/offline、capability、schedulable、sync、health transition |
| Agent | span/metric for every call；failure/maintenance durable event |
| TeamLab | plan、placement、deploy、route、ready、reset、destroy、capture lifecycle |
| Admin mutations | actor、action、resource、status、error |
| External API | 保留 `ExternalApiRequestAudit`，补 correlation link，不重复正文 |
| Sensitive access | PCAP download、VM access、token、template/file mutation |

## 4. API and UI Contract

### 4.1 Event APIs

`GET /api/v1/operations/events` 支持 cursor/count、时间、correlation、event prefix、severity/outcome/error category、用户/队伍/比赛/课程/题目/模板/节点/ticket/runtime/vm 过滤。

`GET /api/v1/operations/events/{id}` 返回单事件与关联对象。

`GET /api/v1/operations/correlations/{correlationId}` 返回事件时间线、ticket、node、image、TeamLab、VM/container 和同 correlation 系统日志摘要。

所有接口要求 Admin。响应不包含 protected payload、auth token、flag、password、private key 或完整 exception stack。

### 4.2 Existing API Extensions

- queue DTO 增加 correlation、error category/code、retryable、event count。
- node DTO 增加最近健康变化、最近错误和最近 correlation。
- image distribution DTO 增加最近 event/error/correlation。
- system log API 增加 correlation、logger、event code、node/resource 过滤。

### 4.3 Troubleshooting Center

保留 `/admin/logs` 路径并拆成：

- 事件时间线：默认入口；
- 部署队列：复用现有队列事实；
- 系统日志：保留 SignalR 和 cursor；
- 恢复与漂移：只显示 recovery/orphan/conflict/unavailable。

页面容器、filters、timeline、detail drawer、log table 和 hooks 分文件；使用 SWR、cursor、局部滚动和稳定查询参数。queue/node/image 页面只增加“查看排障”动作，不复制 timeline。页面文件不新增零散视觉 CSS。

## 5. Data Lifecycle and Migration

- Expand：创建 `OperationalEvents`，扩展 `LogModel`、ticket、image record 的结构化字段和索引。
- Backfill：只为仍活跃 ticket/image/runtime 生成 `*.snapshot.imported` 基线事件，不解析历史日志伪造事件。
- Contract：删除被 recorder 替代的 TeamLab event helper 和旧错误字符串分支，不删除 `TeamLabEvents`。
- `OperationalEvents` 进入 Phase 4 retention catalog，默认 raw retention 180 天，可配置延长。
- `LogModel` 保持现有分区和聚合策略，新字段同步到新分区定义。
- 业务实体删除后，事件保留 ID 和 display snapshot；不能因事件外键阻止删除。
- Backfill 可重复执行；Contract 对无法映射的活动事实 fail closed。

## 6. Large-unit Implementation Plan

### Task 1: Taxonomy, Event Fact, and Migration

**Files:**

- Create: `docs/commercialization/event-taxonomy.md`
- Create: `src/GZCTF/Modules/Audit/Domain/OperationalEvent.cs`
- Create: `src/GZCTF/Modules/Audit/Contracts/OperationalEventModels.cs`
- Create: `src/GZCTF/Modules/Audit/Application/IOperationalEventWriter.cs`
- Create: `src/GZCTF/Modules/Audit/Infrastructure/EfOperationalEventWriter.cs`
- Create: `src/GZCTF/Modules/Audit/Infrastructure/Persistence/OperationalEventEntityConfiguration.cs`
- Modify: `AppDbContext.cs`、`AuditModuleRegistration.cs`、retention catalog。
- Test: event contract、sanitization、migration、retention。

- [x] Implement stable enums, codes, limits and sensitive-key policy.
- [x] Implement append-only entity and same-unit-of-work writer.
- [x] Add indexes, retention and Expand/Backfill/Contract migrations.
- [x] Run one concentrated event/database gate.

### Task 2: Reliable Logs and Correlation

**Files:**

- Modify: `LogModel.cs`、`DatabaseSinkExtension.cs`、`LogHelper.cs`、`LogMessageModel.cs`、`LogRepository.cs`。
- Create: `OperationalCorrelation.cs`、`AdminMutationAuditFilter.cs`。
- Modify: startup registration.
- Test: low-volume flush、shutdown drain、redaction、filter coverage。

- [x] Add trace/correlation/event/error/resource/node fields.
- [x] Implement timer-or-batch flush, bounded retry and graceful drain.
- [x] Add correlation scope and authenticated mutation audit; exclude heartbeat and existing external API audit.
- [x] Run one concentrated logging/audit gate.

### Task 3: Telemetry and Typed Agent Errors

**Files:**

- Create: `Infrastructure/Telemetry/PlatformTelemetry.cs`、`RuntimeTelemetrySnapshotWorker.cs`。
- Modify: `TelemetryExtension.cs`。
- Create: `Modules/Audit/Contracts/OperationalError.cs`。
- Modify: `AgentClient.cs`。
- Create: Agent error models and correlation middleware.
- Modify: Agent Program and controllers.
- Test: error mapping、headers、metrics and span contracts。

- [x] Register bounded metrics and all custom ActivitySources.
- [x] Centralize Agent send/deadline/retry/span/correlation/error mapping.
- [x] Add Agent correlation middleware and uniform error body without Agent exporter dependency.
- [x] Run one concentrated telemetry/Agent contract gate.

### Task 4: Lifecycle Event Integration

**Files:**

- Modify: queue、scheduler、executor、capacity、image distribution、node controller。
- Create: `TeamLabEventRecorder.cs`。
- Modify: TeamLab planner/orchestrator/placement/cleanup/access-grant.
- Test: runtime、image、node、TeamLab lifecycle events.

- [ ] Persist producer trace context and queue/capacity events atomically.
- [ ] Emit image and node transition events without heartbeat noise.
- [ ] Replace scattered TeamLab event construction with recorder.
- [ ] Add lifecycle spans and metrics.
- [ ] Run one concentrated Create/Extend/Stop/Reset/Destroy/failure/recovery gate.

### Task 5: Agent Inventory and Fact-based Recovery

**Files:**

- Create: Agent inventory models/controller.
- Modify: Agent Docker/KVM services.
- Modify: `AgentClient.cs`。
- Create: `RuntimeFactReconciliationService.cs`、`RuntimeRecoveryWorker.cs`。
- Modify: queue recovery and startup registration.
- Test: inventory filtering、matching、missing、conflict、offline、unsupported、dual-main lease。

- [ ] Implement GZCTF-managed Docker/KVM inventory.
- [ ] Add single-owner startup/periodic recovery.
- [ ] Reconcile stale tickets, active facts, reservations and orphan reports.
- [ ] Emit idempotent recovery events and metrics.
- [ ] Run one concentrated recovery gate.

### Task 6: Query API and Troubleshooting UI

**Files:**

- Create: event query service and controller.
- Move: deployment queue controller out of `NodesController.cs` without changing route.
- Modify: queue view、Admin log API、LogRepository.
- Create: `ClientApp/src/components/admin/observability/`。
- Refactor: `pages/admin/Logs.tsx`。
- Modify: queue/node/image pages, generated API and locales.
- Test: query projections、pagination、filters、frontend components.

- [ ] Implement cursor queries, batch name resolution and correlation summary without N+1.
- [ ] Expose redacted Admin APIs.
- [ ] Build timeline, filters, detail drawer, raw logs and recovery view.
- [ ] Add deep links from queue/node/image.
- [ ] Run one concentrated API/frontend production gate.

### Task 7: Runbook, Final Gate, and Review

**Files:**

- Create: `docs/commercialization/runbooks/observability-audit-recovery.md`。
- Create: `docs/commercialization/benchmarks/phase-07-observability-baseline.md`。
- Modify: progress and master plan status.

- [ ] Document correlation search, errors, recovery, metrics, alerts, retention and incident workflow.
- [ ] Run one consolidated backend gate.
- [ ] Run one consolidated frontend gate.
- [ ] Dispatch one independent quality-review agent for the complete Phase 7 diff.
- [ ] Verify findings, fix confirmed issues in one batch, then rerun affected gate and final full gate once.
- [ ] Record exact evidence and unresolved external deployment evidence.

## 7. Concentrated Acceptance

### Event and Audit

- State and event commit atomically.
- Retry does not create contradictory terminal events.
- Admin mutation records actor, action, result and correlation.
- Deleted resources retain display snapshots.
- sanitizer blocks secrets and truncates oversized detail.

### Correlation and Telemetry

- HTTP -> ticket -> scheduling -> execution -> Agent uses one correlation.
- persisted trace context survives service restart.
- Agent receives and returns correlation header.
- metrics have bounded labels; IDs never become labels.
- all custom ActivitySources are registered.

### Recovery

- matching Agent resource completes stale create.
- absent resource with stable identity replays safely.
- identity mismatch fails closed.
- online Agent missing resource corrects DB state.
- offline Agent causes no false deletion.
- dual main has one recovery owner.
- repeated reconcile is idempotent.

### Admin Experience

- one timeline shows queue、node、image、TeamLab、VM/container and logs.
- names are primary and raw IDs remain available.
- cursor paging and local scrolling work.
- opening details does not reload the page or reorder nodes.

## 8. Exit Criteria

Phase 7 is code-complete only when:

- coverage matrix lifecycle transitions all have structured events;
- system logs flush reliably and carry correlation/error/resource dimensions;
- Agent calls have typed errors, spans, metrics and propagated correlation;
- recovery reads Agent inventory and safely handles offline/missing/conflict;
- admin timeline locates common failures without SSH;
- migrations and retention are complete;
- independent review has no unresolved correctness or architecture finding;
- Release build、unit、integration、frontend production gates、EF consistency and sensitive-data scans pass.

External collector deployment、production alert rules and target-environment incident drills remain deployment/Phase 14 evidence and cannot be inferred from local development.
