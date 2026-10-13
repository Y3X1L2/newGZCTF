# Redis 部署与恢复手册

## 适用范围

Redis 用于加速缓存读取、分布式租约、TeamLab 流量接收、节点实时指标、SignalR 广播和部署队列唤醒。PostgreSQL 仍是业务状态、部署任务、注册节点、运行实例和持久化流量的事实源。

## 生产拓扑

- 使用仅允许应用主机访问的 Redis 7.2+ 专用实例。
- 使用 TLS 和专用 ACL 用户，只开放字符串、哈希、stream、发布订阅和 Lua 所需命令组；禁止应用身份执行管理命令。
- 配置 `appendonly yes`、`appendfsync everysec` 和定期 RDB 快照。Redis 备份只能缩短恢复时间，不能替代 PostgreSQL PITR。
- 按实测 stream 峰值和缓存余量设置 `maxmemory`，使用 `maxmemory-policy noeviction`，避免锁、租约和 pending stream 被静默淘汰。
- 同步主机时钟。租约正确性使用 Redis TTL，节点存活使用主站接收时间。

## 应用配置

```json
{
  "RedisRuntime": {
    "Mode": "Distributed",
    "ConnectionString": "redis.internal:6379,user=gzctf,password=secret,ssl=true",
    "KeyPrefix": "gzctf",
    "ClientName": "gzctf-main",
    "ConnectTimeout": "00:00:05",
    "OperationTimeout": "00:00:05",
    "StreamLagWarningThreshold": "00:00:02",
    "ApplicationInstanceCount": 2
  }
}
```

`ConnectionStrings:RedisCache` 仍可作为部署输入，并会规范化到 `RedisRuntime`；生产多实例必须解析为 `Distributed`。`SingleInstance` 只适用于单进程部署，`Disabled` 使用本地缓存/协调，不适用于多实例集群。

## Keyspace 契约

- 格式：`<prefix>:v1:<purpose>:<resource>`。
- purpose 包括 `cache`、`lock`、`lease`、`stream`、`backplane`、`wake-up`。
- 含用户、战队、token、flag 或 IP 的资源身份必须使用 SHA-256 不透明片段。
- 缓存和租约 key 使用 TTL；stream 使用有界 `MAXLEN`，先回收 consumer pending，再把已确认消息作为 trim 候选。
- 协议升级使用新的版本前缀。禁止同时写入两个 keyspace 版本；先部署兼容读端，再切换写端，等待旧 TTL 过期；只有 pending 数为零后才能删除旧 stream。

## 健康检查与告警

监控：

- connection、cache、backplane 和 stream 的 readiness 组件状态；
- 按固定 `purpose/status` 标签统计 Redis 操作失败；
- stream 长度、consumer 延迟和 pending 数；
- 本地降级缓冲丢弃的 TeamLab 流量样本；
- 节点指标降级待处理量；
- Redis 已用内存、碎片率、拒绝连接数和命令延迟。

stream 延迟连续 5 分钟超过 2 秒、pending 持续增长、本地缓冲丢样、重复重连、内存超过 80% 或分布式租约失败时告警。

## Redis 中断

1. Confirm PostgreSQL, application health and deployment ticket processing remain available.
2. Expect cache reads to query PostgreSQL and queue processing to continue through polling.
3. Expect new distributed locks and public port allocations to fail closed. Do not enable local allocation in a multi-instance deployment.
4. TeamLab flow enters a bounded local buffer; node metrics use bounded fallback persistence. Record dropped telemetry if capacity is exceeded.
5. Restore Redis connectivity. Confirm a single reconnect per application instance, consumer group recovery and pending reclaim.
6. Run `scripts/redis/inspect-keyspace.ps1` and `scripts/redis/assert-stream-health.ps1`.
7. Confirm backlog returns below 2 seconds and no duplicate PostgreSQL traffic fingerprints or public ports exist.

## Redis 数据丢失或 Flush

1. Stop only write paths requiring distributed ownership if Redis is not already unavailable; do not stop PostgreSQL-backed reads or queue polling.
2. Start Redis with the same ACL and key prefix.
3. Restart or roll application instances so subscriptions and consumer groups are recreated.
4. Registered nodes, capabilities, deployment tickets and TeamLab runtimes recover from PostgreSQL. Live node metrics repopulate on heartbeat.
5. Cached projections repopulate on demand. Projection revisions remain in PostgreSQL, so pre-flush stale values cannot reappear.
6. Public port leases reconcile from `Container.PublicPort + PublicPortLeaseId`; compare-owner conflicts remain failed closed for operator review.
7. Streams resume from new messages. Data that exceeded the bounded local telemetry buffer is explicitly counted as dropped and is not fabricated.

## 滚动升级与回滚

- 先部署写入 revision 或指标事实所需的数据库迁移，再启动应用二进制。
- 一次滚动一个应用实例；每个实例完成后验证 Redis readiness、SignalR 跨实例投递和 stream consumer 所有权。
- 只有数据库契约仍支持旧版本时才能使用旧二进制回滚。Redis `v1` 数据可丢弃，禁止用破坏性 SQL 将 PostgreSQL 回滚。
- 回滚前停止新的 stream consumer，等待 pending 数归零再切换二进制。队列任务不需要 Redis 迁移，因为事实在 PostgreSQL。

## 事故证据

Capture health output, Redis `INFO memory`, `INFO stats`, stream/group summaries, application Redis metrics, PostgreSQL queue counts and the deployment window. Never include raw cache keys, tokens, flags, flow IP arrays or userdata in incident logs.
