# Redis 缓存与失效矩阵

版本：1.0

主责阶段：Phase 5

适用范围：主站 cache、lock、lease、stream、SignalR backplane、deployment wake-up

## 1. 不变量

1. PostgreSQL 是业务事实；Redis 清空后平台必须能从 PostgreSQL 和 Agent runtime facts 恢复。
2. 业务模块只能调用 `IPlatformCache`、`IDistributedLeaseProvider`、`INodeLiveStateStore`、`ITeamLabTrafficIngestor` 和 `IDeploymentQueueWakeup`，不能接收 `IDatabase`。
3. 所有 cache/lease key 有 TTL，所有 stream 有 MAXLEN 和 pending recovery。
4. 高一致性 projection 的 key 包含 PostgreSQL revision；失效失败只造成 cache miss，不允许继续命中旧 revision。
5. Redis 通知可丢失。任何依赖通知的流程必须有 PostgreSQL polling/reconcile。

## 2. 缓存策略矩阵

| Projection | Resource key | 一致性 | L1/L2 TTL | Mutation trigger | Redis 故障 |
| --- | --- | --- | --- | --- | --- |
| Scoreboard | `gameId + projectionRevision` | revision consistent | 2s / 30s | Submission、manual review、Participation、team rename、scoring rule、AWDP round/checker | 直接生成 PostgreSQL projection |
| ClientConfig | config revision/tag | tag + short stale | 30s / 10m | ConfigService successful commit | 直接读 Config 表 |
| Index/Favicon | content digest/tag | tag + immutable digest | 30s / 10m | 平台名称、logo、首页内容 successful commit | 直接读文件/Config 事实 |
| GameList | catalog tag | bounded stale | 5s / 60s | game create/update/delete/publish/status | 直接查询数据库 |
| RecentGames | catalog tag | bounded stale | 5s / 60s | game lifecycle/status | 直接查询数据库 |
| TrainingStatistics | `courseId + projectionRevision` | revision consistent | 5s / 60s | enrollment、progress、submission、chapter/resource/binding、theory result | 直接生成 PostgreSQL projection |
| TheoryStatistics | `paperOrGameId + projectionRevision` | revision consistent | 5s / 60s | draft、submit、recalculate、paper publish/question change | 直接生成 PostgreSQL projection |

`ImageDistributionRecord`、DeploymentQueueTicket、ApiOperation、TeamLab runtime 状态和节点 schedulable/capabilities 不进入通用 cache：这些管理查询直接读取 PostgreSQL 当前事实。节点瞬时 CPU、内存、slot 和 heartbeat freshness 通过 `INodeLiveStateStore` 读取，不套用业务 projection cache，因此不存在镜像分发或节点状态的 cache invalidation 旁路。

## 3. Scoreboard revision 触发闭环

| 写入 | revision owner | 事务要求 |
| --- | --- | --- |
| 新 Submission/Flag 判定 | Game | 保存 Submission 与 bump 同事务 |
| 人工审核和改分 | Game | 更新 score/status 与 bump 同事务 |
| Participation 审核、Division、禁赛 | Game | Participation mutation 与 bump 同事务 |
| Team rename | 该 Team 参与过的 distinct Game | Team rename 与批量 bump 同事务 |
| ScoringRule、GamePhase、TimeSlot | Game | rule mutation 与 bump 同事务 |
| AWDP round、Flag、Checker 结果 | Game | AWDP fact 与 bump 同事务 |

禁止在数据库提交前删除 Redis key。best-effort warm-up 只能在提交后执行，失败不回滚业务事实。

## 4. Redis 非缓存用途

| Purpose | Key/resource | TTL/Bound | 所有者保护 | 恢复来源 |
| --- | --- | --- | --- | --- |
| distributed lock | fixed resource name | finite lease | random owner token + compare renew/release | PostgreSQL constraint/retry |
| public port lease | public port | finite lease + refresh | lease ID/owner token | active container/public mapping reconcile |
| node latest state | node public ID | 4 x heartbeat interval | monotonic sequence | WorkerNode checkpoint + next heartbeat |
| node metric stream | schema envelope | MAXLEN + consumer group | event fingerprint | metric aggregate/checkpoint |
| TeamLab flow stream | runtime/generation sample | MAXLEN + pending reclaim | fingerprint unique in PostgreSQL | Agent next snapshot + PostgreSQL partitions |
| deployment wake-up | ticket ID hint | pub/sub, no retention | PostgreSQL claim | PostgreSQL polling |
| SignalR backplane | framework channel | framework managed | authenticated Redis connection | local hub + client reconnect |

## 5. 故障模式

- Redis cache 不可用：打开 cache bypass circuit，直接查询 PostgreSQL；恢复后按当前 revision 重新填充。
- Redis lock/port lease 不可用：Distributed 模式 fail closed；不扫描本机端口假装跨实例安全。
- Redis flow stream 不可用：写有界本机 channel；满时 drop oldest telemetry 并记录 dropped count，不阻塞 TeamLab runtime。
- Redis node stream 不可用：heartbeat 进入有界批量 PostgreSQL fallback；能力/version 变化始终直接持久化。
- Redis deployment wake-up 不可用：processor 继续 PostgreSQL polling。
- Redis SignalR backplane 不可用：本实例连接继续服务，多实例 readiness degraded，客户端按现有 reconnect 恢复。

## 6. Keyspace 规则

格式：`<configured-prefix>:v1:<purpose>:<opaque-resource>`。

- purpose 固定枚举，不接收用户输入。
- resource 使用数字 ID、public UUID 或 SHA-256 后的稳定摘要。
- 需要 Lua 原子操作的单资源 key 使用受控 hash tag。
- key 不包含 team name、user name、token、Flag、密码、WireGuard key、完整 IP 或 userdata。
- cache schema 不兼容时递增 key schema version，不双写旧版；旧 key 由 TTL 淘汰。

## 7. 验收查询

- 所有 cache key TTL 大于 0。
- 所有 lease key TTL 大于 0。
- stream length 小于配置 MAXLEN，pending 最老消息年龄低于告警阈值。
- `ProjectionRevision` mutation test 覆盖矩阵中的每个 trigger。
- 清空 Redis 后，PostgreSQL ticket/runtime/submission/course/node identity 数量和状态不变。
- 业务程序集除 Infrastructure adapter 外不引用 StackExchange.Redis。
