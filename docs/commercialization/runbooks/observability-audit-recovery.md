# 可观测性、审计与恢复手册

## 1. 适用范围与事实源

本 runbook 适用于结构化 `OperationalEvent`、原始 `LogModel`、部署队列、镜像分发、节点/Agent、Docker、KVM、TeamLab 和运行事实恢复。

- PostgreSQL runtime、ticket、reservation、image distribution 和 TeamLab 实体是当前状态事实。
- `OperationalEvent` 是追加式生命周期与审计历史，不参与状态恢复决策。
- `LogModel` 是原始系统日志，不替代结构化事件。
- OpenTelemetry metrics/traces 用于实时趋势和跨服务耗时，不作为业务事实。
- Redis 只负责缓存、协调和 wake-up；恢复不得从 Redis、日志文本或 event history 反推当前状态。

## 2. 生产遥测配置

主站只在 `Telemetry` 至少启用一个 exporter 时注册业务 metrics/traces。Prometheus `/metrics` 与 `/healthz` 只监听配置的 `MetricPort`，不得暴露到公网。

```json
{
  "Telemetry": {
    "Prometheus": {
      "Enable": true,
      "TotalNameSuffixForCounters": true
    },
    "OpenTelemetry": {
      "Enable": true,
      "Protocol": "Grpc",
      "EndpointUri": "http://otel-collector.internal:4317"
    },
    "AzureMonitor": {
      "Enable": false,
      "ConnectionString": null
    },
    "Console": {
      "Enable": false
    }
  },
  "DataRetention": {
    "SystemLogDays": 30,
    "OperationalEventDays": 180,
    "DeploymentTicketDays": 180,
    "TeamLabEventDays": 180,
    "DeleteBatchSize": 1000,
    "IntervalMinutes": 60,
    "StartupDelaySeconds": 90
  }
}
```

生产要求：

- OTLP collector、Prometheus 和主站使用内网地址与访问控制。
- collector 不得导出 request/response body、flag、token、密码、WireGuard 私钥、Registry 凭据、cloud-init userdata 或 shell 命令全文。
- 多主站实例使用相同 service name/version 和时钟源；trace 采样策略在 collector 统一配置。
- Console exporter 只用于本地诊断，生产关闭。

## 3. 运维排查流程

1. 打开 `/admin/logs` 的“事件时间线”，按时间、事件域、结果和错误分类缩小范围。
2. 从部署队列、节点或镜像页面进入“关联时间线”，保留 correlation、worker node 或 image template 范围。
3. 点击事件行查看结构化明细、关联事件数、涉及领域和节点；失败事件必须有 `errorCategory`、`errorCode` 和 `retryable`。
4. 需要框架异常正文时切换到“系统日志”，继续使用同一 correlation、节点、ticket 或资源范围。
5. 怀疑服务重启、节点资源丢失或状态漂移时查看“恢复漂移”，确认 recovery decision 后再执行重置、销毁或节点修复。
6. 管理端信息不足时才使用本节 SQL；禁止先 SSH 修改数据库或 Agent 资源。

常用管理 API：

```text
GET /api/admin/operations/events?correlationId=<uuid>&count=50
GET /api/admin/operations/events?workerNodeId=<uuid>&outcome=Failed&count=50
GET /api/admin/operations/events?imageTemplateId=<id>&domain=image&count=50
GET /api/admin/operations/recovery?count=50
GET /api/admin/operations/correlations/<uuid>
GET /api/admin/logs?correlationId=<uuid>&count=50
GET /api/v1/deployment-queue?pageSize=20
```

所有列表使用 cursor；禁止用深 OFFSET 扫描生产历史。

## 4. 关联契约

- 部署任务使用 ticket ID 作为稳定 correlation ID。
- 镜像分发使用 distribution record correlation。
- 管理 mutation 使用 operation correlation。
- queue enqueue 持久化 W3C `traceparent`/`tracestate`，调度和执行 worker 建立 consumer span。
- Agent 调用传播 `traceparent` 与 `X-GZCTF-Correlation-Id`；Agent 错误返回稳定 category/code，不返回匿名异常正文。
- 同一状态迁移只写一个 canonical `OperationalEvent`；派生日志不允许再次驱动业务状态。

若同一 ticket 的事件出现多个 correlation，视为协议回归；先停止相关发布，再核对 enqueue、worker activity 和 AgentClient header 传播。

## 5. 指标与初始告警

业务 metrics：

| Metric | Labels | Purpose |
| --- | --- | --- |
| `gzctf_runtime_queue_depth` | 固定 status/stage/workload | 队列深度与阻塞趋势 |
| `gzctf_worker_nodes` | 固定 status/capability | 在线、可调度、过载节点数 |
| `gzctf_runtime_transitions_total` | workload/stage/outcome | 生命周期结果 |
| `gzctf_runtime_stage_duration_seconds` | workload/stage | 阶段耗时 |
| `gzctf_runtime_recovery_decisions_total` | decision/workload | completed、safe replay、deferred、fail closed |
| `gzctf_agent_calls_total` | operation/result | Agent 调用量 |
| `gzctf_agent_call_failures_total` | operation/error.category | Agent 失败分类 |
| `gzctf_agent_call_duration_seconds` | operation/result | Agent 调用耗时 |
| `gzctf_operational_events_total` | event.code/outcome | 结构化事件写入 |
| `gzctf_system_log_buffered` | 无 | 内存日志缓冲 |
| `gzctf_system_log_dropped_total` | 无 | 缓冲溢出丢弃 |
| `gzctf_system_log_flush_failures_total` | 无 | 数据库刷盘失败 |

初始告警规则：

- `gzctf_system_log_dropped_total` 任意增长：Critical。
- `gzctf_system_log_flush_failures_total` 5 分钟内连续增长：Warning；同时缓冲超过 5000：Critical。
- `gzctf_system_log_buffered` 持续 2 分钟超过 1000：Warning；超过 5000：Critical。
- Agent 调用量至少 20 次时，任一 operation 5 分钟失败率超过 5%：Warning；超过 20%：Critical。
- `recovery.run.failed` 任意出现：Critical。
- `recovery.identity.conflict`、`recovery.resource.missing` 任意出现：Warning，并创建人工核对任务。
- 主站健康但 3 分钟没有 `recovery.run.succeeded`：Warning；多主部署只要求一个 lease owner 产生成功事件。
- `failed_closed` decision 任意增长：Warning；同一 workload 连续增长升级为 Critical。
- Pending/Blocked queue depth 连续增长 10 分钟且可调度节点数不变：Warning，进入运行容量手册。

延迟分位阈值必须使用目标节点和 Registry 的预发布基准冻结；不得用开发机数据替代生产阈值。

## 6. 系统日志写入端故障

数据库日志 sink 每 2 秒或达到 50 条触发 flush，单批最多 500 条，缓冲上限 10000 条，退出等待 5 秒 drain。

1. 检查 PostgreSQL readiness、连接池和写入延迟。
2. 观察 buffered、flush failures 和 dropped 三个指标。
3. 数据库恢复后确认 buffered 下降到 0，flush failures 停止增长。
4. dropped 增长表示最旧日志已被丢弃；不得伪造缺失日志。使用 `OperationalEvent`、ticket 和 runtime current facts 补充事故时间线。
5. 不通过提高缓冲上限掩盖持续数据库故障；先修复数据库吞吐或 collector 策略。

## 7. 运行恢复契约

`RuntimeRecoveryWorker`：

- 启动后立即执行，之后每 1 分钟运行一次。
- 使用 PostgreSQL session advisory lease `0x475A435446524543`；多主同时运行时只有一个 owner。
- ticket stale 阈值为 15 分钟。
- inventory 仅读取 `ManagedBy=GZCTF` 且具有有效 generation 的 Docker，以及带 `gzctf-generation=` metadata 的 KVM domain。
- Agent 必须声明 `runtime.inventory.v1`；Docker 和 KVM inventory 独立判断。

处理矩阵：

| Finding | Platform action | Operator action |
| --- | --- | --- |
| matching | 确认事实，必要时完成 stale ticket | 核对事件后无需处理 |
| missing on online supported node | current fact 修正为 Destroyed/Error/Failed | 使用标准 reset/recreate；不手工改 running |
| identity conflict | fail closed，不覆盖、不重建 | 核对 generation、resource identity 和模板；确认归属后人工清理 |
| node offline | deferred，不判定资源丢失 | 恢复节点/Agent 后等待下一轮 reconcile |
| inventory unsupported | deferred | 同步 Agent 至支持 `runtime.inventory.v1` 的版本 |
| orphan | 仅写一次 `recovery.orphan.observed`，不自动销毁 | 核对是否为旧 generation 或人工资源，确认后走明确清理流程 |

已成功后丢失的环境不自动重建。只有尚未完成、稳定 identity/generation 可证明幂等的 ticket 才进入 safe replay。

## 8. 恢复诊断 SQL

最近恢复运行：

```sql
SELECT "CorrelationId", "EventCode", "Outcome", "ErrorCategory", "ErrorCode",
       "Message", "DetailJson", "OccurredAt"
FROM "OperationalEvents"
WHERE "EventCode" LIKE 'recovery.%'
   OR "EventCode" = 'agent.inventory.unavailable'
ORDER BY "OccurredAt" DESC, "Id" DESC
LIMIT 200;
```

按 correlation 查看完整链路：

```sql
SELECT "OccurredAt", "EventCode", "Outcome", "ErrorCategory", "ErrorCode",
       "WorkerNodeId", "DeploymentTicketId", "SubjectDisplayName",
       "ResourceDisplayName", "Message"
FROM "OperationalEvents"
WHERE "CorrelationId" = '<uuid>'
ORDER BY "OccurredAt", "Id";
```

stale active ticket：

```sql
SELECT "Id", "Kind", "Operation", "Status", "Stage", "TargetNodeId",
       "Generation", "ActiveIdentity", "ClaimOwner", "ClaimExpiresAt",
       "ErrorCategory", "ErrorCode", "CreatedAt", "StartedAt"
FROM "DeploymentQueueTickets"
WHERE "Status" IN (1, 2, 3)
  AND COALESCE("StartedAt", "CreatedAt") < now() - interval '15 minutes'
ORDER BY "CreatedAt", "Id";
```

SQL 只用于核对。禁止直接删除 orphan、修改 generation 或把失败环境改回 Running。

## 9. 保留与证据

默认保留：system logs 30 天、operational events 180 天、deployment tickets 180 天、TeamLab events 180 天。删除由治理 worker 限批执行。

事故证据至少包含：

- correlation ID、trace ID、event code、error category/code；
- ticket、node、template、runtime、VM 的显示名称和 ID；
- 事件时间线、系统日志范围、recovery summary；
- 相关 metrics 时间窗和 collector trace URL；
- 应用版本、Agent version/capability hash、数据库 migration head；
- 操作员执行的 reset、destroy、Agent sync 或节点下线动作。

导出前再次检查敏感字段。不得把 flag、token、密码、私钥、Registry auth、完整 userdata、ProtectedPayload 或未脱敏 Agent body 写入事故附件。

## 10. 发布与回滚

1. 备份数据库并确认 PITR 可用。
2. 应用 Expand/Backfill/Contract migrations；Backfill 只导入活动 ticket、image distribution 和 TeamLab runtime snapshot。
3. 滚动升级主站；确认 metrics、OTLP、`/admin/logs` 和 recovery lease owner。
4. 滚动同步 Agent；旧 Agent 可继续承载不需要 inventory 的普通调用，但恢复会标记 unsupported，不得误判 missing。
5. 观察至少两个 recovery interval，确认无 unexpected conflict、missing 或 orphan。
6. 回滚应用前确认旧二进制仍兼容当前 schema；禁止用手工 destructive SQL 回退。需要回退 schema 时使用 PITR。

