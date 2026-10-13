# TeamLab workload storage capacity candidate (2026-10-07)

## Goal And Baseline

- Goal: let ordinary Docker and VM scheduling, TeamLab placement, and release readiness use the storage available to the requested workload after the .27 VM data volume expansion.
- Base: `origin/main 7a1f554d`; integration branch: `codex/workload-storage-integration` in an isolated worktree.
- Scope: Agent host facts, main runtime capacity evaluation, TeamLab planning/readiness, generated internal API client, focused tests and contract documentation. No database migration, queue replacement, instance lifecycle operation, or production deployment.

## Verified Environment

- Read-only .27 checks found the root filesystem with about 70 GiB free and `/dev/sdb1` (ext4) with about 590 GiB total and 180 GiB free. Docker's `DockerRootDir` is `/var/lib/docker` on the root filesystem. The configured KVM image directory and TeamLab runtime directory are bind-mounted to the expanded data filesystem. A separate read-only PVE check found its pool about 71% allocated with roughly 126.6 GiB unallocated; no PVE configuration was changed.
- Main and Agent remained active on their prior releases. The user confirmed the earlier failed instance was synchronized and cleaned up; this task did not repeat cleanup or establish that all OVN leftovers are absent.
- The candidate code and artifacts have not been deployed or exercised through a real four-VM creation on .27.

## Implementation

- Agent schema 1 gains optional `AvailableDockerStorageBytes`. An old Agent omits it (`null`); a new Agent reports a non-null free-space measurement, or `0` when Docker root cannot be measured. Docker daemon info supplies `DockerRootDir`; filesystem readings use the complete configured path rather than its lexical root.
- Main's node detail response exposes the new optional nullable host fact. The internal OpenAPI was exported through `OpenApiDocumentationTests.Development_ExportsInternalContract` and the repository's `swagger-typescript-api` template regenerated `ClientApp/src/generated/Api.ts`; the reviewed generated diff adds only `availableDockerStorageBytes?: number | null`.
- VM storage uses `Kvm.ImageStoragePath`. When the Agent declares `TeamLabExecutionPlan`, it conservatively takes the smaller free space of that path and `TeamLab.RuntimeStateRoot`. Ordinary KVM remains independent of an absent TeamLab directory.
- `NodeCapacitySnapshot.AvailableFor(requested)` and `Fits(requested)` select Docker storage for Docker-only requests and VM storage for VM-only requests. A mixed request uses the smaller budget for its aggregate storage demand. All active `FleetCapacityReservation.StorageMiB` values are subtracted from both budgets. These are conservative accounting rules, not precise per-filesystem reservations; mixed or simultaneous cross-kind workloads can be rejected despite physical headroom.
- Fleet reservation and physical TeamLab placement use the same eligibility evaluator. TeamLab release planning uses the workload-specific budget and treats measured zero as unavailable. Admin readiness diagnoses network groups against the corresponding snapshot, including storage, slots, CPU/memory and declared guest-network features.

## Verification And Artifacts

| Check | Result |
| --- | --- |
| .NET Release solution build | Passed on integrated source |
| .NET unit tests | 1311/1311 on full rerun; an unrelated Windows environment inheritance test failed once and passed in isolation before the successful rerun |
| PostgreSQL/Testcontainers integration | Passed: 305/305, no skips |
| Internal OpenAPI schema | Lightweight TestServer export and nullable Docker host-fact assertion passed (1/1) after the full backend suite |
| Frontend `pnpm build` gate | Passed: locale, lint, type, architecture, 118 files / 398 tests, Vite and bundle budget |
| Real .27 workload | Not run; candidate is not deployed |

The release candidate will be built outside the repository at `D:\Work\YINYU-TeamLab-Workload-Storage-20261007` after final source verification. Its archive will contain `publish/GZCTF.dll`, `publish/agent/gzctf-agent`, and `publish/wwwroot/frontend-manifest.json`; hashes and source identity must be checked on the final archive before review.

## Handoff

- Review and merge the PR only after checking its final branch head, full gate results, archive integrity and unchanged production scope.
- Any later rollout needs fresh backup, release directory, atomic switch, rollback path and real Docker/VM/TeamLab acceptance under `docs/operations/vnext-maintenance-window-rollout.md`.
- Keep the root/data disk distinction visible during deployment: publishing only Main would leave the old Agent host fact until the Agent is upgraded. Keep old Agent `null` fallback compatibility during staged rollout.
- Browser viewport review at 390, 1366, 1920 and 2560 pixels remains for the authenticated release screen; the automated frontend gate covers code, interaction tests and build, but no production GUI was changed or checked in this task.
