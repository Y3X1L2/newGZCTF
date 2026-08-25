# Phase 5 Redis Baseline

## Reproducible Profile

The committed workload targets a dedicated non-production environment with two main-site instances, one Redis 7.2+ instance and PostgreSQL 17. Supply `BASE_URL`, `ADMIN_TOKEN`, `GAME_ID`, matching `NODE_IDS`/`NODE_TOKENS`, and test-only credentials before execution.

`FLOW_INGEST_URL` enables the synthetic 20k flow samples/s feeder (200 HTTP batches/s, 100 samples/batch). It must point to a benchmark-only adapter that forwards the documented sample batch into the Agent snapshot/collector path; it is intentionally not a production public API. `REVISION_MUTATION_URL` and `QUEUE_WAKEUP_URL` optionally enable benchmark-only authenticated adapters for revision mutations and deployment ticket creation. Omitting an optional URL omits that scenario instead of reporting a false pass.

```powershell
k6 run scripts/load/phase-05-redis-load.js
pwsh scripts/redis/inspect-keyspace.ps1 -ConnectionString $env:GZCTF_BENCHMARK_REDIS
pwsh scripts/redis/assert-stream-health.ps1 -ConnectionString $env:GZCTF_BENCHMARK_REDIS
```

Example complete profile:

```powershell
$env:NODE_IDS = "node-guid-1,node-guid-2"
$env:NODE_TOKENS = "node-token-1,node-token-2"
$env:FLOW_INGEST_URL = "http://benchmark-adapter:8090/teamlab/flows"
$env:REVISION_MUTATION_URL = "http://benchmark-adapter:8090/revisions"
$env:QUEUE_WAKEUP_URL = "http://benchmark-adapter:8090/deployment-tickets"
k6 run scripts/load/phase-05-redis-load.js
```

## Functional Gates

- One scoreboard factory execution per `(game, global revision, game revision)` concurrency window.
- Redis-backed heartbeat does not persist one PostgreSQL transaction per request.
- TeamLab flow steady-state ingest lag below 2 seconds and average database batch above 200 samples under the 20k samples/s profile.
- Consumer restart reclaims pending entries without duplicate fingerprints.
- During a 60-second Redis interruption, PostgreSQL deployment tickets remain claimable by polling, cache reads bypass, and new distributed locks/public ports fail closed.
- After Redis recovery, application instances do not create a reconnect storm and stream backlog drains within 5 minutes.
- Cache/lease/lock TTL coverage is 100%; stream length and pending counts stay within configured bounds.

## Evidence Record

Record application commit, instance count, Redis/PostgreSQL versions and configuration, CPU, memory, disk, network, dataset size, k6 summary, keyspace summary, stream summary, PostgreSQL batch metrics and failure drill timestamps here when the dedicated benchmark environment is provisioned. Synthetic scripts are committed now; production capacity claims require this environment-specific evidence.

Development verification on 2026-07-12 completed the full automated suite against isolated PostgreSQL and Redis containers (`508/508` unit and `226/226` integration tests). The Redis inspection scripts were also exercised against an isolated Redis 7 container. No k6 capacity result is recorded because this workstation has no k6 runtime and no dedicated two-instance benchmark environment was supplied; this absence must not be interpreted as a throughput pass or failure.
