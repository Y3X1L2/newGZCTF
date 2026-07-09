# Logging Coverage Progress

## Scope

This document tracks the July 2026 logging coverage pass for deployment queue, TeamLab runtime, and training workflows.

## Findings

- Admin log page already reads Serilog database logs through `AdminController` / `LogRepository` and receives live events through SignalR.
- Deployment queue records were persisted in `DeploymentTargets`, but key state transitions were mostly plain `LogInformation` entries without `Status`, which made them hard to audit from the admin log board.
- Fleet Docker/VM create paths updated `DeploymentTarget.Status`, but completed/failed/cancelled states were not consistently mirrored into `SystemLog`.
- TeamLab kept detailed `TeamLabEvent` rows, but plan/deploy/destroy lifecycle events were not consistently mirrored into the system log board.
- Training group and legacy training module management already had broad logging coverage. Course management had many write endpoints without system logs.

## Implemented

- Added `DeploymentTargetLogHelper` to format safe deployment target log messages without serializing `Payload`.
- Added deployment queue logs for queued, assigned, creating, completed, failed, and cancelled target transitions.
- Added Fleet Docker logs for scheduled/preferred node creation paths, proxy setup failures, successful deployment, and failure paths.
- Added Fleet VM logs for creation start/completion/failure and VM destroy start/success/failure.
- Added manual deployment target cancel logs in `DeploymentTargetsController`.
- Added TeamLab system logs for plan success/failure, deploy start/success/failure, destroy start/success, and cleanup-pending.
- Added missing training direction update log.
- Added training course management logs for draft/publish/archive flow, enrollment review, teacher changes, chapter/resource/theory paper/question/template/challenge changes.
- Added training learner logs for check-in, enrollment/cancel, chapter completion, chapter theory submission, course container renewal/destruction.
- Added `DeploymentQueueViewService` so the deployment queue page now aggregates queue tickets and historical deployment targets into readable rows: owner/team or user, game, challenge, image/template, target node name/host, type, action, status, capacity slots, queue position, result, and error.
- Added Docker lifecycle deployment target rows for container destruction and lifetime extension; extension operations are displayed as `延期 ...` instead of a misleading generic start action.
- Added VM destruction deployment target rows with node, challenge, owner, result, success, and failure state.
- Added system logs for deployment queue ticket creation/reuse/cancel/recovery/assignment/start/completion/failure.
- Added system logs for worker node register/update/deregister, TeamLab network check/enable, and Agent self-sync request/failure.
- Expanded node resource visibility to include TeamLab runtime assets alongside containers, VMs, and legacy penetration resources.
- Updated the admin node page to use stable node sorting and silent polling so heartbeat refresh does not force full-page reloads or reorder cards unexpectedly.
- Updated the deployment queue page columns so request identity, image/template, node name, type, action, status, resource slots, and errors are directly readable.

## Deliberately Not Logged

- Read-only list/detail endpoints are not logged to avoid noise.
- Training answer drafts are not logged because they are frequent and contain learning state rather than an auditable administrative action.
- Flag answers, training answers, registry auth, WireGuard private keys/config, container environment variables, and deployment payloads are not written to logs.
- Low-level readiness probes remain regular service logs or TeamLab runtime events rather than admin `SystemLog` entries.

## Verification

- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~DeploymentTargetTests --no-restore -p:UseSharedCompilation=false -m:1`
  - Result: pass, 4 tests.
- `dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false`
  - Result: pass.
  - Existing warnings remain: obsolete `VmManager` references and nullable EF value-converter warnings.
- `dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeploymentTargetTests|FullyQualifiedName~Fleet|FullyQualifiedName~TeamLab" --no-restore -p:UseSharedCompilation=false -m:1`
  - Result: pass, 190 tests.
- `git diff --check -- <logging-related files>`
  - Result: pass.
- `dotnet build src/GZCTF/GZCTF.csproj --no-restore -p:UseSharedCompilation=false`
  - Result: pass on 2026-07-08.
  - Existing warnings remain: EF nullable value-converter warnings and one pre-existing nullable warning in `TeamController`.
- `pnpm check` from `src/GZCTF/ClientApp`
  - Result: pass on 2026-07-08.
