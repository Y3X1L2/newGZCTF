# TeamLab Unified Scheduling Baseline

## Development Verification

Date: 2026-07-24

Scope: unified ordinary/TeamLab capacity accounting, deterministic TeamLab placement, per-node dispatch budgets, and durable reset/destroy lifecycle ownership.

| Gate | Result |
| --- | --- |
| Runtime and TeamLab unit slice | 253/253 passed |
| PostgreSQL database and Open API integration slice | 39/39 passed (37 initial passes plus 2 focused corrections) |
| Open API production document contract | Passed after regenerating the committed contract |
| Integration project Release build | 0 errors |
| Release solution build | 0 errors |

## Capacity Accounting

- Ordinary Docker/VM tickets and TeamLab shards reserve the same CPU, memory, storage, Docker-slot, and VM-slot ledger.
- Availability is calculated from total capacity minus current facts, active reservations, and safety margin.
- Docker eligibility is independent from KVM eligibility; a Docker-only node remains available to ordinary and TeamLab Docker workloads.
- TeamLab reserves every shard vector in one transaction after revalidating the latest capacity snapshot. A failed multi-group placement persists no shard assignment and no reservation.

## Dispatch Budgets

Agent actions are limited by worker node and operation category rather than by one whole-node lock.

| Category | Platform safety cap |
| --- | ---: |
| Docker create | 16 |
| VM create | 4 |
| TeamLab network mutation | 4 |
| Probe/read | 16 |
| Cleanup | 4 |

Manifest limits may lower these values. The focused concurrency gate observed a maximum of 2 concurrent Docker creates for a configured limit of 2, allowed different nodes to execute concurrently, and allowed independent categories on one node to overlap.

## Placement Baseline

Fixture: 8 worker nodes, 32 unsplittable networks, and 128 Docker assets.

| Run | Elapsed |
| --- | ---: |
| First process run | 578.0 ms |
| Second process run | 26.0 ms |

Deterministic placement hash for both runs:

```text
408ad0adbf1d4059d462b7a7d3404f062cec4b0fd9275120c336c3ecff60995a
```

The local algorithm gate is 2 seconds. This is a deterministic development regression baseline, not a production capacity claim.

## Lifecycle Guarantees

- Reset quota is reserved before queue submission under a runtime-scoped PostgreSQL transaction lock.
- Pending, running, succeeded, and scenario-caused failed resets consume user quota; administrator resets, cancelled resets, and infrastructure failures do not.
- Destroy uses one durable operation identity per game/team binding. The binding remains `Destroying` until the runtime is factually destroyed and is then projected to `Destroyed`.
- Repeated destroy submissions reuse the same operation identity, so an enqueue interruption can be resumed without losing cleanup ownership.

## Production Sign-off

Run multi-main claim, burst admission, and real multi-node Agent tests on the target deployment topology before a production capacity declaration. Record p50/p95/p99 placement and admission latency, database lock waits, reservation oversell count, per-category Agent concurrency, reset/destroy recovery, and cleanup residue. Do not extrapolate those values from this local development baseline.
