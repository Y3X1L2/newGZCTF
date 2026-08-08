# TeamLab Control-Plane Hardening Implementation Plan

**Goal:** Close the lifecycle, remote-access, observability, recovery, and management-operation gaps found in the TeamLab audit without changing the Fabric data plane.

**Architecture:** Keep the existing queue, runtime generation fencing, Agent identity validation, Redis stream and rollout coordinator. Add small policy checks at their existing application boundaries. Management HTTP remains an adapter over the same application operations used by the public API.

## Scope

1. Put rollout lifecycle ownership enforcement in the runtime application layer. Direct reset/destroy rejects rollout-managed runtimes; the rollout coordinator calls a dedicated, ownership-verifying internal lifecycle command.
2. Add one remote-session policy with fixed limits: one active session per actor/asset/protocol, five active sessions per actor, and one hundred relay sessions per node. Cleanup remains in `Ending` until relay and Guacamole deletion both complete.
3. Return a runtime-generation observation completeness summary from traffic flow/path pages using the already persisted observation dropped counters.
4. Add an administrator-only, inventory-proven recovery cleanup command for a runtime generation whose Agent ownership marker is lost. It must not infer ownership or delete unknown resources.
5. Route management reset/destroy/access-grant/capture mutations through the existing `TeamLabRuntimeOperationApplicationService`, preserving admin response models while gaining idempotency and operation audit identity.

## Files And Responsibilities

- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`: enforce direct lifecycle ownership and provide rollout-only execution.
- `src/GZCTF/Modules/TeamLab/Application/Rollouts/TeamLabRolloutCoordinator.cs`: use the rollout lifecycle command.
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRemoteAccessService.cs`: apply fixed session policy and retryable cleanup semantics.
- `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRemoteAccessEntityConfigurations.cs`: add the active-session lookup indexes needed by the policy.
- `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs` and `Contracts/TeamLabTrafficContracts.cs`: expose persisted completeness facts.
- `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRuntimeController.cs`: submit mutation operations through the shared operation layer.
- focused TeamLab unit/integration tests plus regenerated OpenAPI/client contracts if a public contract changes.

## Verification

- A rollout-managed runtime rejects direct management and open-API reset/destroy; rollout target cleanup still succeeds.
- Parallel remote session creation cannot exceed the policy and a failed relay/Guacamole cleanup remains retryable.
- A dropped observation causes both flow and path APIs to report degraded completeness for the active generation.
- Missing Agent ownership state requires explicit administrator recovery and never deletes a newer generation.
- Admin mutation retries reuse an operation through their idempotency key, with a single deployment ticket.

## Progress (2026-08-01)

- Completed: direct reset and destroy now reject rollout-managed runtimes in the application layer. The rollout coordinator uses an explicit target-verified destroy command.
- Completed: remote session creation is serialized only for its database reservation. PostgreSQL enforces one active session per operator, asset, and protocol; operator and node caps are checked while short transaction advisory locks are held. Failed creation removes both the relay and Guacamole resources. A session is marked ended only after cleanup succeeds.
- Completed: flow and path responses include the persisted observation-loss summary, and the management UI presents the resulting Chinese completeness state.
- Deliberately not implemented: treating management browser requests as external API-token operations would forge an API-token audit identity. Management endpoints continue to use the same application services, lifecycle guard, deployment queue, and audit events; a browser-native idempotency store should be designed separately if management-request deduplication becomes a product requirement.
- Deliberately not implemented: automatic recovery cleanup after an Agent ownership marker is lost. The current Agent refuses shared-network cleanup in this case, which is the correct fail-closed behavior. Any future administrator recovery command must remain explicit and inventory-proven.
- Verified locally: Release build completed with zero warnings and errors; unit tests passed `759/759`; EF reports no pending model changes; vNext TypeScript strict check and lint passed.
- Pending environment verification: regenerate `docs/commercialization/openapi/open-v1.json` through `scripts/verify-openapi-contract.ps1` on CI or a machine with Docker/Testcontainers. The local host has no Docker engine, so the integration-test OpenAPI host cannot start. The change is additive: flow/path pages now include `completeness`.
