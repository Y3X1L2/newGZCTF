# Phase 7 Observability Baseline

## Development Verification

Date: 2026-07-13

Environment:

- Windows 11, Intel Core Ultra 9 285H, 16 logical CPUs, 31.5 GB RAM.
- .NET SDK 10.0.300 and Node.js workspace runtime.
- PostgreSQL integration uses isolated `postgres:16-alpine`; Testcontainers Ryuk is disabled and the suite disposes resources.
- This workstation result is a correctness/build baseline, not a production capacity claim.

Final Phase 7 development results:

| Gate | Result |
| --- | --- |
| Release solution build | 0 warnings, 0 errors |
| Unit tests | 483/483 passed |
| PostgreSQL/Redis integration tests | 227/227 passed |
| Final affected observability/recovery/runtime-control tests | 41/41 passed |
| PostgreSQL migration/advisory lease | 1/1 passed |
| Frontend production build | locale, strict TypeScript, architecture, Vite, artifact manifest and bundle budget passed |
| Event query | stable `(OccurredAt, Id)` cursor, count capped at 200 |
| Correlation summary | event count, time range, failure, domains, nodes and bounded timeline |
| Log sink | 2-second or 50-item flush, 500-item max batch, 10000-item bounded buffer, 5-second shutdown drain |
| Recovery | one-minute cycle, 15-minute stale threshold, PostgreSQL advisory single owner |
| Runtime control identity | persistent generation for Docker/VM, libvirt UUID for VM, Agent-side destroy preconditions |
| EF model | no pending model changes after `20260713152015_HardenPhaseSevenRuntimeIdentity` |
| Independent review | one review completed; all ten confirmed findings closed |

The full integration gate initially exposed two pre-existing real-container fixture races: cleanup returned after queue admission without waiting for the container fact to become `Destroyed`. Both test flows now wait for the durable terminal fact; the focused real-container tests pass `2/2` and the subsequent full suite passes `227/227`.

## Commercial Pre-production Sign-off

Run against the target PostgreSQL/Redis topology, OTLP collector, Registry and representative WorkerNodes. Preserve raw benchmark output with the release artifact.

### Event and Query Workload

- Seed at least 10 million operational events across 180 days.
- Include queue, image, node, Agent, Docker, KVM, TeamLab, recovery and audit domains.
- Include hot correlations with 200+ events and deleted business objects that rely on display snapshots.
- Measure event append transaction latency and event query p50/p95/p99 for correlation, node, ticket, template, domain/outcome and recovery filters.
- Acceptance target: indexed 50-item queries p95 below 500 ms and p99 below 1 s under representative concurrent runtime traffic; no sequential per-event name query.

### Logging Workload

- Produce steady low-volume logs to prove the 2-second flush path.
- Produce a controlled burst above 10000 messages while PostgreSQL writes are delayed.
- Measure buffered peak, flush latency, flush failures, dropped count and recovery time after database restoration.
- Acceptance target: no dropped logs during the declared supported burst envelope; any forced overflow increments the dropped metric exactly and does not deadlock shutdown.

### Trace and Agent Workload

- Run at least 300 concurrent create/control requests across two main-server instances and multiple Agent nodes.
- Verify one correlation from HTTP request through queue, scheduler, executor and Agent span.
- Measure Agent call duration and failure rate by stable operation; confirm IDs never appear as metric labels.
- Kill one main instance during active work and confirm persisted trace context continues on the surviving worker.

### Recovery Workload

- Run two main-server instances and prove one advisory lease owner per cycle.
- Inject matching, missing, identity conflict, offline, unsupported and orphan facts for Docker and KVM independently.
- Verify repeated cycles are idempotent, offline nodes are not marked missing, orphan resources are not destroyed and successful lost environments are not auto-rebuilt.
- Acceptance target: owner failover within two recovery intervals; no duplicate correction/replay/orphan event for the same stable fact.

### Admin Experience

- Validate queue, node and image deep links with production-size data.
- Measure initial timeline and next-page render time, local scrolling and detail drawer response.
- Confirm common image, node, Agent, Docker/KVM and recovery failures can be diagnosed without SSH.

Record dataset size, concurrent users, p50/p95/p99, PostgreSQL CPU/IO/locks, collector queue/drop, event write rate, query plan, Agent latency, recovery decisions and UI timings. Missing target-environment measurements remain release evidence gaps and must not be replaced with estimates.
