# Phase 1 API Contract Compliance Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan in large reviewable units. Do not run a red/green cycle for every method; validate each completed unit once, then run the full contract suite at the end.

**Goal:** Bring the current `/api/open/v1` surface and the post-Phase-1 module boundaries back into full compliance with the Phase 1 authentication, idempotency, operation, error, audit, pagination, OpenAPI and architecture contracts.

**Architecture:** Preserve the existing scoped token and `ApiOperation` foundation. Use durable `ApiOperation` jobs for commands with Agent or deployment side effects, and one shared synchronous idempotency receipt for atomic database-only writes. Split external TeamLab DTOs from administrator projections so public responses cannot expose UI metadata, runtime-native identifiers or raw infrastructure errors.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10, PostgreSQL, Redis, NSwag, xUnit, Testcontainers.PostgreSql, Testcontainers.Redis.

## Implementation Status (2026-07-14)

- Completed the external v1 contract correction in place: all TeamLab mutations now require a validated `Idempotency-Key`, submit the existing durable `ApiOperation`, and return `202 Accepted`.
- Completed durable fact recovery for topology create/update/delete/publish, WireGuard grant create/revoke, and capture start/stop.
- Completed public DTO separation for topology editor metadata, runtime-native resource IDs, raw runtime errors, and raw capture errors.
- Completed opaque cursor pagination for public topology, release, and runtime-event collections.
- Completed TeamLab audit resource resolution, creator/admin/explicit-grant operation access, safe operation failures, EF migration, and regenerated OpenAPI v1 snapshot.
- Verification passed: backend unit tests `483/483`, OpenAPI tests `11/11`, TeamLab migration integration test `1/1`, and EF pending-model check reported no drift.
- The broader Application/Fleet persistence-boundary refactor in Large Unit F remains architectural follow-up; it was intentionally not mixed into this focused external contract correction.

---

## 1. Confirmed Contract Gaps

### P0: Idempotency is declared but not implemented

- `OpenTeamLabTopologiesController` discards `Idempotency-Key` for topology create, update, delete and publish.
- `OpenTeamLabRuntimesController.CreateAccessGrant` discards the key. A retry can generate new WireGuard keys and revoke the first grant.
- `OpenTeamLabTrafficController.StopCapture` discards the key.
- access-grant revoke has no `Idempotency-Key` parameter despite the TeamLab contract requiring it for every write.
- capture start uses a separate identity `(runtimeId, generation, keyHash)` instead of Phase 1 `(apiTokenId, routeKey, idempotencyKey)`, reports `idempotency_key_reused` instead of `idempotency_conflict`, and does not update `ExternalApiAuditContext`.

### P0: Public responses expose internal facts

- `TeamLabRuntimeAssetProjectionModel.RuntimeResourceId` exposes Docker/libvirt runtime-native identifiers.
- runtime, shard, asset and capture DTOs return raw `LastError` strings.
- `TeamLabRuntimeOperationHandler` stores raw deployment queue errors in public `ApiOperation.ErrorDetail`.
- TeamLab Agent failures are wrapped in `TeamLabApiContractException` with raw `Agent` messages, which the external exception middleware returns as ProblemDetails `detail`.

### P0: External and administrator DTOs are coupled

- public topology requests and responses contain `TeamLabTopologyEditorModel` coordinates used by the administrator canvas.
- public runtime and administrator runtime APIs share the same projection DTO, which caused internal resource IDs and diagnostic errors to enter the public contract.
- the committed OpenAPI snapshot exposes `runtimeResourceId` and `editor`.

### P1: OpenAPI does not describe actual HTTP behavior

- TeamLab actions lack explicit `ProducesResponseType` metadata.
- runtime create/reset/destroy are implemented as `202 Accepted`, but the committed OpenAPI declares `200 application/octet-stream`.
- topology create/publish, access-grant create and capture create return `201`, while the snapshot declares `200`.
- topology/access-grant deletes return `204`, while the snapshot declares `200`.
- stable `application/problem+json` responses and error codes are not declared.
- the OpenAPI idempotency test only checks the two image import routes and cannot detect TeamLab omissions.

### P1: Audit and pagination are incomplete

- `ExternalApiRequestAuditMiddleware.ResolveResource` only understands game, image and operation routes; TeamLab topology/runtime/capture requests are stored without resource identity.
- `GET /api/open/v1/operations/{id}` only accepts the exact creating token ID; it does not implement the documented creator-user, explicitly granted token or administrator access policy.
- topology list and release list are unbounded arrays.
- runtime events return a bare array, use a maximum of 200 and do not return `items + nextCursor`.
- the public API guide contains image and challenge examples but no TeamLab authentication, idempotency, polling, error or pagination examples.

### P2: Phase 1 module-boundary enforcement has drifted

- `ITeamLabRuntimeApplicationService` exposes `GZCTF.Services.Fleet` types from an Application contract.
- several TeamLab/Runtime/Penetration Application services directly depend on `AppDbContext` or concrete Fleet services instead of module ports.
- the architecture test only scans controllers under `GZCTF.Modules`; new root controllers can bypass it.
- `DeploymentQueueController` is a new root controller that directly injects `AppDbContext`.

## 2. Version Decision Gate

Before changing public DTO shapes or pagination responses, inspect production `ExternalApiRequestAudits` and issued token scopes for `/api/open/v1/teamlab` usage.

- If no external TeamLab client has been used, correct the pre-commercial v1 contract in place and replace the committed snapshot once.
- If a real external client exists, keep v1 response shapes stable, apply security/idempotency fixes that are behavior-compatible, and publish the cleaned DTO and pagination contract under `/api/open/v2/teamlab`.
- Do not preserve the leaked fields in v2 as compatibility aliases.

## 3. Large Unit A: Contract Gate and Regression Coverage

**Files:**

- Modify: `src/GZCTF.Integration.Test/Tests/Api/OpenApiTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Api/OpenTeamLabApiContractTests.cs`
- Create: `src/GZCTF.Test/UnitTests/Architecture/ExternalApiArchitectureTests.cs`

- [ ] Enumerate every `/api/open/v1` operation from the generated document and assert that every TeamLab write action requires `Idempotency-Key`; explicitly exempt read-only POST actions `validate` and `plan`.
- [ ] Assert the real success code and response schema for every TeamLab endpoint.
- [ ] Assert every declared external error response uses `application/problem+json` with `code` and `traceId`.
- [ ] Add authenticated integration coverage for topology create/update/delete/publish, runtime create/reset/destroy, access grant and capture commands.
- [ ] Add replay tests: same key/same body returns the same operation or resource; same key/different body returns `409 idempotency_conflict`.
- [ ] Add schema assertions that public contracts do not contain `runtimeResourceId`, editor coordinates, WorkerNode identity, bridge/namespace names or raw error detail.
- [ ] Expand the architecture test to select all controllers with `/api/open/v1` routes regardless of namespace and reject `AppDbContext`, `AgentClient` and Fleet implementation types.

Run after the unit:

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter "FullyQualifiedName~OpenApiTests|FullyQualifiedName~OpenTeamLabApiContractTests"
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~ExternalApiArchitectureTests"
```

## 4. Large Unit B: One Idempotency Foundation

**Files:**

- Modify: `src/GZCTF/Modules/Audit/Application/IdempotencyService.cs`
- Create: `src/GZCTF/Modules/Audit/Application/ExternalIdempotencyKey.cs`
- Create: `src/GZCTF/Modules/Audit/Domain/ExternalApiCommandReceipt.cs`
- Create: `src/GZCTF/Modules/Audit/Application/IExternalApiCommandReceiptStore.cs`
- Create: `src/GZCTF/Modules/Audit/Infrastructure/EfExternalApiCommandReceiptStore.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Modules/Audit/AuditModuleRegistration.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOperationApplicationService.cs`

- [ ] Move the `1-128` ASCII key validation into `ExternalIdempotencyKey.Normalize` and use it from image, challenge and TeamLab paths; delete duplicate validators after all callers migrate.
- [ ] Add one durable synchronous receipt keyed by `(ApiTokenId, RouteKey, IdempotencyKey)` with request hash, status code, location, resource type/id and safe response JSON.
- [ ] Execute topology create/update/delete/publish and receipt persistence in one PostgreSQL transaction.
- [ ] On exact replay, return the saved status/location/body without re-running validation or mutation.
- [ ] On hash mismatch, return `409 idempotency_conflict`.
- [ ] Record operation/receipt identity and replay state in `ExternalApiAuditContext`.
- [ ] Do not store overlay secrets, WireGuard keys, capture files, Authorization headers or Agent errors in the generic receipt.

Expected synchronous semantics:

```text
POST topology      -> 201 + Location
PUT topology       -> 200
DELETE topology    -> 204
POST release       -> 201 + Location
```

## 5. Large Unit C: Durable TeamLab Side-Effect Operations

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabAccessOperationJob.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabCaptureOperationJob.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabAccessOperationHandler.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabCaptureOperationHandler.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabAccessOperationResultProvider.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabCaptureOperationResultProvider.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTrafficController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAccessGrantService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs`
- Modify: `src/GZCTF/Models/Data/TeamLabEntities.cs`

- [ ] Submit access-grant create/revoke and capture start/stop through the existing persistent `ApiOperation` worker.
- [ ] Return `202 Accepted` and the original operation on retries.
- [ ] Store access keys and the one-time download token only in a module-owned Data Protection payload; clear it after successful materialization or terminal failure.
- [ ] Make handlers fact-aware: if the target grant/capture already reflects the operation, complete without repeating Agent side effects.
- [ ] Link capture/access jobs to runtime public ID, generation and resource public ID.
- [ ] Remove `TeamLabTrafficCaptureJob.IdempotencyKeyHash` and `RequestHash` after migration; do not retain a second idempotency system.
- [ ] Ensure Agent and deployment failures become stable public codes while full diagnostics remain only in operational events/logs.

## 6. Large Unit D: Separate Public Contracts from Internal Projections

**Files:**

- Create: `src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabTopologyContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabRuntimeContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Contracts/AdminTeamLabProjectionContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTrafficController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminTopologyController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRuntimeController.cs`

- [ ] Remove editor coordinates from public topology request/response models; keep editor metadata only in administrator contracts.
- [ ] Remove runtime-native IDs and raw error strings from public runtime/capture models.
- [ ] Expose only stable public error data such as `errorCode`, `stage` and `retryable`; keep diagnostic message, node and native resource identity in admin projections.
- [ ] Ensure operation result providers serialize public DTOs, never admin projections.
- [ ] Preserve public UUID identity for topology, release, runtime, shard, grant and capture.

## 7. Large Unit E: Pagination, Audit and Error Safety

**Files:**

- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs`
- Modify: `src/GZCTF/Modules/Audit/Infrastructure/ExternalApiRequestAuditMiddleware.cs`
- Modify: `src/GZCTF/Modules/Audit/Api/OperationsController.cs`
- Modify: `src/GZCTF/Modules/Audit/Application/ApiOperationService.cs`
- Modify: `src/GZCTF/Modules/Audit/Application/IApiOperationStore.cs`
- Modify: `src/GZCTF/Modules/Audit/Infrastructure/EfApiOperationStore.cs`
- Modify: `src/GZCTF/Infrastructure/Api/ExternalApiExceptionHandler.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs`

- [ ] Change topology, release and runtime-event lists to opaque cursor pagination with default `50`, maximum `100`, `items` and `nextCursor`.
- [ ] Use deterministic ordering with the stable public ID or event ID as the final tie-breaker.
- [ ] Resolve TeamLab audit resources as topology, release, runtime, access-grant or capture using route values.
- [ ] Store the stable public error code in audit records and the linked operation/receipt ID when present.
- [ ] Replace exact-token-only operation lookup with an access policy that permits the creating token, the creator user, an explicitly granted operation token or an administrator token while preserving `404` for unauthorized callers.
- [ ] Replace raw Agent/deployment messages in public ProblemDetails and `ApiOperation.ErrorDetail` with stable safe messages.
- [ ] Preserve full diagnostics in `OperationalEvent`, structured logs and administrator-only views.

## 8. Large Unit F: Restore Module Boundaries

**Files:**

- Modify: `src/GZCTF/Modules/TeamLab/Application/ITeamLabRuntimeApplicationService.cs`
- Create: `src/GZCTF/Modules/Runtime/Contracts/RuntimeQueueContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabExecutionContracts.cs`
- Move/refactor: `src/GZCTF/Controllers/DeploymentQueueController.cs`
- Modify: `src/GZCTF.Test/UnitTests/Architecture/ArchitectureDependencyTests.cs`

- [ ] Split the public runtime command/query contract from the queue-worker execution contract.
- [ ] Replace `GZCTF.Services.Fleet` types in TeamLab Application interfaces with Runtime/TeamLab contract records.
- [ ] Move the deployment queue API into the Runtime module and place database queries behind a query service.
- [ ] Expand architecture tests so new root controllers cannot bypass module rules.
- [ ] Add a staged rule preventing new Application-layer references to `AppDbContext`; migrate API-facing TeamLab services first, then enable the strict assembly-wide rule after remaining Runtime/Penetration services receive ports.

## 9. Large Unit G: OpenAPI, Documentation and Final Verification

**Files:**

- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTrafficController.cs`
- Modify: `docs/commercialization/open-api-v1-guide.md`
- Modify: `docs/commercialization/external-api-standard.md`
- Modify: `docs/commercialization/teamlab-api-foundation-contract.md`
- Regenerate: `docs/commercialization/openapi/open-v1.json`

- [ ] Declare exact success and ProblemDetails responses on every TeamLab action.
- [ ] Document every TeamLab scope, stable error code, idempotency rule, operation polling sequence and cursor example.
- [ ] Regenerate the OpenAPI snapshot only after runtime behavior and tests agree.
- [ ] Run the compatibility comparator; if the version gate selected v2, keep v1 snapshot unchanged and add an independent v2 snapshot.

Final verification:

```powershell
dotnet build GZCTF.sln -c Release
dotnet test src/GZCTF.Test/GZCTF.Test.csproj
$env:TESTCONTAINERS_RYUK_DISABLED='true'; dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj
dotnet ef migrations has-pending-model-changes --project src/GZCTF --startup-project src/GZCTF
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp build
pwsh scripts/verify-openapi-contract.ps1
git diff --check
```

## 10. Completion Criteria

- Every TeamLab external write has real durable idempotency; no controller discards the key.
- Agent/deployment side effects are recoverable after service restart and do not duplicate on replay.
- public contracts contain no UI coordinates, node/native resource identity or raw infrastructure errors.
- all external errors are stable ProblemDetails and every request has complete audit resource identity.
- all large lists follow the Phase 1 cursor contract.
- OpenAPI describes the real status codes and schemas, and the gate verifies every external mutation rather than two hard-coded routes.
- external API controllers and module contracts do not depend on persistence or Fleet implementation types.
- API guide examples are sufficient for a third-party caller to authenticate, submit, retry, poll and diagnose TeamLab operations without reading source code.
