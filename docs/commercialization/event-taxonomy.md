# Platform Operational Event Taxonomy

## 1. Purpose

本文冻结 Phase 7 使用的结构化事件、结果、错误和关联规范。事件用于审计、生命周期解释和管理端排障，不替代当前状态表，不作为恢复决策源，也不保存业务 secret。

## 2. Event Shape

每条事件必须包含：

- `eventCode`：稳定的小写点分标识；
- `occurredAt`：UTC；
- `correlationId`：UUID；
- `severity`：Debug、Information、Warning、Error、Critical；
- `outcome`：Started、Pending、Blocked、Succeeded、Failed、Cancelled、Recovered、Observed；
- `message`：管理员可读摘要；
- 至少一个 subject/resource 维度；
- 失败事件必须包含 `errorCategory`、`errorCode` 和 `retryable`。

可选维度：

- trace id；
- actor user；
- owner user/team；
- game、course、challenge、template；
- worker node、deployment ticket、TeamLab runtime、VM instance；
- subject/resource id 和 display snapshot；
- 已脱敏的 detail 白名单。

## 3. Naming Rules

event code 格式为 `domain.subject.action` 或 `domain.subject.stage.result`。

规则：

- code 是 API 和查询契约，发布后不能改变语义；
- display message 可本地化，不参与程序判断；
- 不在 code 中放 ID、节点名、镜像名或版本号；
- retry 不创建 `*.started.v2` 一类临时代码，attempt 放 detail；
- 同一状态变化只产生一个 canonical event，system log 由 event writer 派生；
- event code 常量集中维护，禁止在业务代码中散落字符串。

## 4. Outcomes

| Outcome | Meaning |
| --- | --- |
| Started | 操作已开始并拥有执行权 |
| Pending | 已接受，等待调度、依赖或外部结果 |
| Blocked | 暂时不能执行，原因可恢复 |
| Succeeded | 操作成功终止 |
| Failed | 操作失败终止或 fail closed |
| Cancelled | 在未执行或允许取消的边界终止 |
| Recovered | 恢复流程确认、重放或修正了状态 |
| Observed | 只读观测到事实变化，不代表平台执行动作 |

## 5. Severity

| Severity | Use |
| --- | --- |
| Debug | 不落默认管理时间线的开发诊断 |
| Information | 正常生命周期和管理动作 |
| Warning | 可恢复阻塞、降级、重试、节点短时不可用 |
| Error | 操作失败、状态漂移、资源缺失、协议错误 |
| Critical | 数据一致性破坏、恢复无法继续、广域基础设施故障 |

## 6. Error Categories

| Category | Stable code examples | Retry default |
| --- | --- | --- |
| Authorization | `auth.forbidden`、`auth.scope_missing` | false |
| Validation | `request.invalid`、`runtime.identity_missing` | false |
| Conflict | `runtime.identity_conflict`、`operation.duplicate` | false |
| Scheduling | `runtime.no_eligible_node`、`runtime.assignment_stale` | true |
| Capacity | `runtime.capacity_exhausted`、`runtime.owner_limit` | true |
| ImageRegistry | `image.registry_unreachable`、`image.artifact_missing` | true |
| ImageTransfer | `image.transfer_timeout`、`image.digest_mismatch` | depends |
| NodeUnavailable | `node.offline`、`node.heartbeat_stale` | true |
| AgentProtocol | `agent.feature_missing`、`agent.response_invalid` | false |
| AgentTransport | `agent.timeout`、`agent.connection_failed` | true |
| Docker | `docker.create_failed`、`docker.destroy_failed` | depends |
| Kvm | `kvm.unavailable`、`kvm.create_failed` | depends |
| Network | `network.apply_failed`、`network.route_failed` | depends |
| HealthCheck | `health.probe_timeout`、`health.service_unready` | true |
| Storage | `storage.unavailable`、`storage.file_missing` | depends |
| Database | `database.unavailable`、`database.concurrency_conflict` | true |
| Cache | `cache.unavailable`、`cache.lease_lost` | true |
| Unknown | `operation.unclassified_failure` | false |

具体异常是否 retryable 由 classifier 根据操作幂等性、HTTP status 和调用阶段决定，不能只由 category 决定。

## 7. Runtime Queue Events

| Event code | Outcome |
| --- | --- |
| `runtime.ticket.enqueued` | Pending |
| `runtime.ticket.duplicate` | Observed |
| `runtime.ticket.cancelled` | Cancelled |
| `runtime.admission.blocked` | Blocked |
| `runtime.admission.accepted` | Pending |
| `runtime.scheduling.started` | Started |
| `runtime.scheduling.blocked` | Blocked |
| `runtime.scheduling.assigned` | Succeeded |
| `runtime.execution.started` | Started |
| `runtime.execution.succeeded` | Succeeded |
| `runtime.execution.failed` | Failed |
| `runtime.execution.replay_queued` | Recovered |
| `runtime.execution.claim_recovered` | Recovered |
| `runtime.execution.failed_closed` | Failed |
| `runtime.control.extend.started` | Started |
| `runtime.control.stop.started` | Started |
| `runtime.control.reset.started` | Started |
| `runtime.control.destroy.started` | Started |
| `runtime.rollback.started` | Started |
| `runtime.rollback.succeeded` | Succeeded |
| `runtime.rollback.failed` | Failed |

## 8. Capacity Events

| Event code | Outcome |
| --- | --- |
| `runtime.capacity.reserved` | Succeeded |
| `runtime.capacity.blocked` | Blocked |
| `runtime.capacity.confirmed` | Succeeded |
| `runtime.capacity.released` | Succeeded |
| `runtime.capacity.expired` | Recovered |
| `runtime.capacity.reconciled` | Recovered |
| `runtime.capacity.conflict` | Failed |

## 9. Image Distribution Events

| Event code | Outcome |
| --- | --- |
| `image.distribution.queued` | Pending |
| `image.distribution.claimed` | Started |
| `image.transfer.started` | Started |
| `image.transfer.succeeded` | Succeeded |
| `image.verify.started` | Started |
| `image.verify.succeeded` | Succeeded |
| `image.distribution.ready` | Succeeded |
| `image.distribution.retry_queued` | Pending |
| `image.distribution.failed` | Failed |
| `image.cleanup.queued` | Pending |
| `image.cleanup.started` | Started |
| `image.cleanup.succeeded` | Succeeded |
| `image.cleanup.failed` | Failed |
| `image.reference.attached` | Succeeded |
| `image.reference.released` | Succeeded |
| `image.reconcile.corrected` | Recovered |

## 10. Node and Agent Events

| Event code | Outcome |
| --- | --- |
| `node.registration.started` | Started |
| `node.registration.succeeded` | Succeeded |
| `node.registration.failed` | Failed |
| `node.deregistered` | Succeeded |
| `node.online` | Observed |
| `node.offline` | Observed |
| `node.capability.changed` | Observed |
| `node.schedulable.enabled` | Observed |
| `node.schedulable.disabled` | Observed |
| `node.health.degraded` | Observed |
| `node.health.recovered` | Recovered |
| `agent.sync.started` | Started |
| `agent.sync.succeeded` | Succeeded |
| `agent.sync.failed` | Failed |
| `agent.call.failed` | Failed |
| `agent.inventory.unavailable` | Blocked |

normal heartbeat、status poll 和成功的普通 Agent call 只进入 span/metric，不逐条写 durable event。

## 11. Container and VM Events

| Event code | Outcome |
| --- | --- |
| `container.create.started` | Started |
| `container.create.succeeded` | Succeeded |
| `container.create.failed` | Failed |
| `container.stop.succeeded` | Succeeded |
| `container.destroy.succeeded` | Succeeded |
| `container.destroy.failed` | Failed |
| `vm.create.started` | Started |
| `vm.create.succeeded` | Succeeded |
| `vm.create.failed` | Failed |
| `vm.boot.probe_started` | Started |
| `vm.boot.ready` | Succeeded |
| `vm.boot.failed` | Failed |
| `vm.stop.succeeded` | Succeeded |
| `vm.destroy.succeeded` | Succeeded |
| `vm.destroy.failed` | Failed |
| `vm.access.opened` | Succeeded |
| `vm.access.failed` | Failed |

## 12. TeamLab Events

| Event code | Outcome |
| --- | --- |
| `teamlab.plan.started` | Started |
| `teamlab.plan.succeeded` | Succeeded |
| `teamlab.placement.succeeded` | Succeeded |
| `teamlab.deploy.started` | Started |
| `teamlab.network.applied` | Succeeded |
| `teamlab.asset.created` | Succeeded |
| `teamlab.asset.create_failed` | Failed |
| `teamlab.route.applied` | Succeeded |
| `teamlab.probe.succeeded` | Succeeded |
| `teamlab.ready` | Succeeded |
| `teamlab.deploy.failed` | Failed |
| `teamlab.reset.queued` | Pending |
| `teamlab.reset.started` | Started |
| `teamlab.reset.succeeded` | Succeeded |
| `teamlab.reset.failed` | Failed |
| `teamlab.destroy.started` | Started |
| `teamlab.destroy.succeeded` | Succeeded |
| `teamlab.destroy.failed` | Failed |
| `teamlab.cleanup.started` | Started |
| `teamlab.cleanup.succeeded` | Succeeded |
| `teamlab.cleanup.failed` | Failed |
| `teamlab.capture.started` | Started |
| `teamlab.capture.stopped` | Succeeded |
| `teamlab.capture.failed` | Failed |
| `teamlab.access.opened` | Succeeded |
| `teamlab.access.revoked` | Succeeded |

## 13. Recovery Events

| Event code | Outcome |
| --- | --- |
| `recovery.run.started` | Started |
| `recovery.run.succeeded` | Succeeded |
| `recovery.run.failed` | Failed |
| `recovery.fact.confirmed` | Recovered |
| `recovery.resource.missing` | Failed |
| `recovery.identity.conflict` | Failed |
| `recovery.ticket.replayed` | Recovered |
| `recovery.state.corrected` | Recovered |
| `recovery.node.unavailable` | Blocked |
| `recovery.inventory.unsupported` | Blocked |
| `recovery.orphan.observed` | Observed |

## 14. Administrative and Security Events

Generic mutation code:

- `audit.admin.mutation.succeeded`；
- `audit.admin.mutation.failed`；
- `audit.external.request`；
- `audit.sensitive.download`；
- `audit.access.vm_opened`；
- `audit.access.pcap_downloaded`。

具体业务模块可增加 `audit.identity.*`、`audit.content.*`、`audit.training.*`、`audit.game.*`，但必须先加入本文件和常量表，不能在 controller 内临时创建 code。

## 15. Detail Allowlist

允许的 detail 键：

- `attempt`、`generation`、`stage`、`operation`、`workload`；
- `httpStatus`、`durationMs`、`queuePosition`；
- `dockerSlots`、`vmSlots`；
- `previousStatus`、`currentStatus`；
- `capability`、`feature`；
- `imageType`、`digestPrefix`、`sizeBytes`；
- `routeCount`、`assetCount`、`shardCount`；
- `decision`、`reasonCode`。

禁止键及不区分大小写变体：

- flag、token、authorization、cookie、password、secret、privateKey；
- wireguardPrivateKey、userdata、cloudInit、registryAuth；
- command、environment、requestBody、responseBody；
- rdpPassword、sshPrivateKey。

禁止值：

- 完整 JWT、Bearer header、WireGuard key、完整 sha256 认证材料；
- 原始异常对象、stack trace、任意上传文件内容。

## 16. Query and Retention

- 默认事件页面按 `OccurredAt desc, Id desc` cursor 分页。
- correlation timeline 按 `OccurredAt asc, Id asc`。
- raw retention 默认 180 天。
- 事件聚合不复制 message/detail，只聚合 event code、outcome、error category、node 和时间桶。
- 删除业务对象不删除事件；display snapshot 保留。
- 任何 retention 清理必须写 `DataGovernanceRun`，不得静默删除。
