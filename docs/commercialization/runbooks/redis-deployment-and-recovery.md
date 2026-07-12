# Redis Deployment and Recovery Runbook

## Scope

Redis accelerates cache reads, distributed leases, TeamLab traffic ingestion, node live metrics, SignalR fan-out and deployment queue wake-up. PostgreSQL remains the source of truth for business state, deployment tickets, registered nodes, runtimes and persisted traffic.

## Production Topology

- Use a dedicated Redis 7.2+ instance reachable only from application hosts.
- Use TLS and a dedicated ACL user. Permit only the command groups required by strings, hashes, streams, pub/sub and Lua scripts; deny administrative commands to the application identity.
- Configure `appendonly yes`, `appendfsync everysec` and periodic RDB snapshots. Redis backup shortens recovery but never replaces PostgreSQL PITR.
- Configure `maxmemory` from measured peak stream backlog plus cache headroom. Use `maxmemory-policy noeviction`: cache writes may bypass on pressure, but locks, leases and pending stream entries must not be evicted silently.
- Synchronize host clocks. Lease correctness uses Redis TTL, while node liveness uses server receive time.

## Application Configuration

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

`ConnectionStrings:RedisCache` remains accepted as a deployment input and is normalized into `RedisRuntime`; production multi-instance mode must resolve to `Distributed`. `SingleInstance` is only for one-process deployments. `Disabled` uses local cache/coordination and is not valid for multi-instance fleet operation.

## Keyspace Contract

- Format: `<prefix>:v1:<purpose>:<resource>`.
- Purposes: `cache`, `lock`, `lease`, `stream`, `backplane`, `wake-up`.
- Resource identities containing user, team, token, flag or IP data are SHA-256 opaque segments.
- Cache and lease keys have TTL. Streams use bounded `MAXLEN`; consumer pending entries are reclaimed before acknowledged messages become trim candidates.
- Protocol upgrades use a new version prefix. Do not dual-write keyspace versions. Deploy compatible readers, switch writers, then let old TTL keys expire; delete remaining old stream keys only after pending count is zero.

## Health and Alerts

Monitor:

- readiness component status for connection, cache, backplane and stream;
- Redis operation failures by fixed `purpose/status` labels;
- stream length, consumer lag and pending count;
- local fallback dropped TeamLab flow samples;
- node metric fallback backlog;
- Redis used memory, fragmentation, rejected connections and command latency.

Alert when stream lag exceeds 2 seconds for 5 minutes, pending grows continuously, local buffers drop samples, reconnect attempts repeat, memory exceeds 80%, or a distributed lease operation fails.

## Redis Interruption

1. Confirm PostgreSQL, application health and deployment ticket processing remain available.
2. Expect cache reads to query PostgreSQL and queue processing to continue through polling.
3. Expect new distributed locks and public port allocations to fail closed. Do not enable local allocation in a multi-instance deployment.
4. TeamLab flow enters a bounded local buffer; node metrics use bounded fallback persistence. Record dropped telemetry if capacity is exceeded.
5. Restore Redis connectivity. Confirm a single reconnect per application instance, consumer group recovery and pending reclaim.
6. Run `scripts/redis/inspect-keyspace.ps1` and `scripts/redis/assert-stream-health.ps1`.
7. Confirm backlog returns below 2 seconds and no duplicate PostgreSQL traffic fingerprints or public ports exist.

## Redis Data Loss or Flush

1. Stop only write paths requiring distributed ownership if Redis is not already unavailable; do not stop PostgreSQL-backed reads or queue polling.
2. Start Redis with the same ACL and key prefix.
3. Restart or roll application instances so subscriptions and consumer groups are recreated.
4. Registered nodes, capabilities, deployment tickets and TeamLab runtimes recover from PostgreSQL. Live node metrics repopulate on heartbeat.
5. Cached projections repopulate on demand. Projection revisions remain in PostgreSQL, so pre-flush stale values cannot reappear.
6. Public port leases reconcile from `Container.PublicPort + PublicPortLeaseId`; compare-owner conflicts remain failed closed for operator review.
7. Streams resume from new messages. Data that exceeded the bounded local telemetry buffer is explicitly counted as dropped and is not fabricated.

## Rolling Upgrade and Rollback

- Deploy database migration before starting binaries that write revision or metric facts.
- Roll application instances one at a time. Verify Redis readiness, SignalR cross-instance delivery and stream consumer ownership after each instance.
- Rollback may use the previous binary only while its database contract remains supported. Redis `v1` data is disposable; never roll PostgreSQL backward with destructive migration SQL.
- Before rollback, stop new stream consumers, wait for pending count to reach zero, then switch binaries. Queue tickets require no Redis migration because their truth remains PostgreSQL.

## Incident Evidence

Capture health output, Redis `INFO memory`, `INFO stats`, stream/group summaries, application Redis metrics, PostgreSQL queue counts and the deployment window. Never include raw cache keys, tokens, flags, flow IP arrays or userdata in incident logs.
