# 运行调度与恢复手册

## 适用范围

本手册适用于统一运行控制面：`DeploymentQueueTickets`、`FleetCapacityReservations`、`ImageDistributionRecords`、WorkerNode capability manifest 和 TeamLab runtime/shard。

## 正常事实

- PostgreSQL ticket 是唯一任务事实；Redis 只负责 wake-up 和 lease。
- `Pending -> Scheduling -> Scheduled -> Running -> terminal` 是唯一创建状态链。
- Stop/Destroy/Reset 是独立 control ticket；禁止直接把 Running Create 改为 Cancelled。
- 节点占用按 `max(Agent live count, PostgreSQL active facts) + active reservations` 计算。
- `Scheduled` 和 `Running` ticket 的 reservation 每分钟续租；执行 claim 同时续租 owner 和容量。

## 队列排查

```sql
SELECT "Id", "Kind", "Operation", "Status", "Stage", "TargetNodeId",
       "SubjectConcurrencyKey", "ClaimOwner", "ClaimExpiresAt", "BlockedReasonCode",
       "StageMessage", "ErrorMessage", "CreatedAt", "StartedAt"
FROM "DeploymentQueueTickets"
WHERE "Status" IN (0, 1, 2, 3)
ORDER BY "CreatedAt", "Id";
```

- `Pending/CapacityWaiting`: 检查节点 capability、live state、current facts 和 active reservation。
- `Scheduled/NodeExecutionWaiting`: 检查 Agent execution limit；不得手工释放 reservation。
- `Running` 且 claim 过期：production worker 每分钟按 runtime facts 恢复；Create 只在稳定 identity/generation 可重放时回到 Scheduled。
- Extend/Reset 无法证明完成时 fail closed；Stop/Destroy 可安全重放。

## 容量排查

```sql
SELECT "WorkerNodeId", "Status", sum("DockerSlots") AS docker,
       sum("VmSlots") AS vm, min("ExpiresAt") AS earliest_expiry
FROM "FleetCapacityReservations"
WHERE "Status" = 0
GROUP BY "WorkerNodeId", "Status";
```

- 有效 Scheduled/Running ticket 的 reservation 不应过期。
- terminal ticket 仍有 Active reservation 时，先确认 execution terminal commit 是否完成，再运行 reconcile；不要直接改 WorkerNode 计数。
- TeamLab 必须按全部 current-generation shard/node 核对，不得只看入口节点。

## 镜像分发

- `Pending/Pulling/Ready/Failed/CleanupPending` 是节点缓存状态，不修改 Registry 主副本 Ready 状态。
- Pulling 且 claim 未过期时，释放最后引用不会抢占为 Cleanup；下一轮 reconcile 在传输终态后处理。
- TeamLab 镜像引用使用 runtime ID；只有 runtime Destroy 成功后释放。
- VM cleanup 前同时检查普通 `VmInstances` 和 TeamLab VM assets。

## Agent 能力

- capability hash 不应随 heartbeat 时间变化。
- binary SHA-256 为空的旧 Agent 不得清空主站已知摘要。
- Docker-only 节点缺 KVM 仍可调度 Docker；VM 只要求 `runtime.kvm.v1`。
- manifest schema 不支持、required feature 缺失或对应 execution limit 为 0 时 fail closed，并显示具体 reason code。

## TeamLab 重置与销毁

- Reset ticket 等待同 subject 前序 Create 结束。
- Reset executor 顺序：cleanup current generation -> planner reset -> physical placement/reservation -> deploy。
- Destroy executor 完成所有 shard cleanup 后释放 TeamLab runtime 镜像引用。
- cleanup 失败时 runtime 进入 `CleanupPending`，不得继续规划下一 generation。

## 迁移发布

1. 应用 Expand，确认新列和 reservation 表存在。
2. 应用 Backfill；active orphan target 会使迁移 fail closed。
3. 检查 active identity、reservation orphan 和 capability 数据。
4. 备份/PITR 就绪后应用 Contract，删除旧 target、reserved counter 和整数协议列。
5. Contract 后不能回滚旧应用连接新 schema；恢复使用 PITR。
