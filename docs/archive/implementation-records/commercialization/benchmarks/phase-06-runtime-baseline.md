# Phase 6 Runtime Baseline

## Development Verification

Date: 2026-07-13

Environment:

- Windows 11, Intel Core Ultra 9 285H, 16 logical CPUs, 31.5 GB RAM.
- .NET SDK 10.0.300.
- Docker Engine 29.4.0.
- PostgreSQL integration tests use isolated `postgres:16-alpine`; Ryuk is disabled and test resources are disposed by the suite.

Results:

| Gate | Result |
| --- | --- |
| Release solution build | 0 warnings, 0 errors |
| Unit tests | 437/437 passed |
| PostgreSQL/Testcontainers integration | 227/227 passed |
| Frontend production build | locale, strict TypeScript, architecture, Vite, artifact manifest and bundle budget passed |
| EF model | no pending model changes |
| TeamLab placement | Docker capacity 3 + 1 produces two shards and atomic reservations in focused control-plane coverage |
| Multi-worker claim | PostgreSQL CAS allows one scheduling owner for the same ticket |
| Image transfer | single-flight and per-node Docker/VM limits covered by focused tests |

## Commercial Capacity Sign-off

The development workstation result is not a production capacity claim. Before release, run on the target PostgreSQL/Redis topology and representative WorkerNodes:

- 500 owners with at least 300 concurrent create requests.
- Two main-server scheduling workers, including one-worker termination and stale claim recovery.
- Slow VM/image backlog while new tickets continue reaching Scheduled.
- Control operations during saturated create lanes.
- Same-image 20-way ensure, multi-node transfers and Agent limit saturation.
- Real dual-node TeamLab Docker and Linux VM environments, reset and residue-free destroy.

Record p50/p95/p99, database lock waits, duplicate claims, reservation oversell, owner distribution, Agent concurrency and cleanup residue here. Do not fill missing production measurements with estimates.
