# Critical Review: TDD Supplement Plan (2026-05-19-tdd-supplement.md)

> Review conducted against the actual codebase at `D:\newGZ\YINYU CTF平台`

---

## 1. Test Fixture Compatibility with Existing GZCTFApplicationFactory

### 1.1 GZCTFTestFixture Conflicts

The plan creates `GZCTFTestFixture` as an abstract base inheriting `IClassFixture<GZCTFApplicationFactory>` + `IAsyncLifetime`. The existing `IntegrationTestCollection` is a `[CollectionDefinition]` that uses the same `GZCTFApplicationFactory`. The problem:

- **Existing tests** (e.g., `BasicApiTests`, `ScoreboardCalculationTests`) use `[Collection(nameof(IntegrationTestCollection))]`, which means ALL tests in the collection share a single factory instance and therefore a single PostgreSQL container.
- **GZCTFTestFixture** calls `Factory.ResetDatabaseAsync()` in `InitializeAsync()` (every test class). This would wipe the database while other test classes in the same collection are executing, causing spurious failures.
- **Mitigation needed**: Either (a) make GZCTFTestFixture use its own collection (not IntegrationTestCollection), requiring a second factory instance and second PostgreSQL container, or (b) remove `ResetDatabaseAsync()` from InitializeAsync and make tests responsible for their own data isolation.

### 1.2 ResetDatabaseAsync() Does Not Exist

The plan calls `Factory.ResetDatabaseAsync()` in `GZCTFTestFixture.InitializeAsync()`. **No such method exists on `GZCTFApplicationFactory`.** The existing factory has no database reset mechanism at all -- it relies on each test class creating its own data via `TestDataSeeder` (which uses SemaphoreSlim for shared games) and relying on data isolation by test design, not by database reset.

To implement `ResetDatabaseAsync()`, the factory would need either:
- A Respawn-based checkpoint/reset mechanism (Respawn package not in the project)
- EF Core `Database.EnsureDeleted()` + re-migration (slow, ~10s per reset)
- A custom SQL-based TRUNCATE approach

### 1.3 No Parallel Execution Safety

The existing `[CollectionDefinition(nameof(IntegrationTestCollection))]` serializes all tests in the collection (xUnit default). However, the plan introduces a **new** `GZCTFTestFixture` pattern that would presumably live in a separate collection. If any GZCTFTestFixture tests and IntegrationTestCollection tests ever run in the same process, the `ResetDatabaseAsync()` calls will collide.

The plan's claim that "tests are all independent" (checklist item in section XI) is unverifiable unless all tests share the same single-threaded collection. Tests that modify shared DB state and rely on `ResetDatabaseAsync()` for isolation can only run one-at-a-time per process.

---

## 2. Missing Package Dependencies

### 2.1 Respawn (Not Present)

The plan states "Use Respawn library to do fast database reset" in the GZCTFTestFixture comments. Respawn (`Respawn`) is NOT a dependency in either test project's `.csproj` or in `Directory.Packages.props`. It needs to be added as a NuGet dependency for both GZCTF.Test and GZCTF.Integration.Test (or at minimum the integration test project).

### 2.2 Moq (Not Present as Direct Dependency)

The plan's unit tests (e.g., `WeightedSchedulerTests`, `QueueManagerTests`) use `Mock.Of<INodeRepository>()` and `Mock.Of<WeightedScheduler>()`. `Moq` is NOT a direct package reference in `src/GZCTF.Test/GZCTF.Test.csproj`. It appears only as a transitive dependency in the pnpm lockfile (frontend), not as a .NET test dependency. All GZCTF.Test unit tests must either:

- Add the `Moq` package reference, or
- Rewrite tests to use hand-written stubs/fakes

The existing unit tests in `GZCTF.Test` do not use mocking frameworks; they test static utilities (Codec, CryptoUtils, SignatureTest, etc.) which have no external dependencies. The plan's tests are the first to require mocking.

### 2.3 Testcontainers Packages (Already Present)

`Testcontainers.PostgreSql`, `Testcontainers.K3s`, `Testcontainers.Minio` are already in `Directory.Packages.props` and the integration test project. This is fine.

---

## 3. Entities and Services That Do Not Exist Yet

The TDD plan assumes the existence of several entities and services that are only defined in the companion refactor plan (`2026-05-19-yinyu-ctf-platform-refactor.md`). These have NOT been created yet. The tests will fail at compile time, not test logic time.

### 3.1 Missing Entities (Cannot Compile)

| Entity | Referenced In Tests | Status |
|---|---|---|
| `ChallengeSubmissionType` | Test #11 | **Does not exist in any .cs file anywhere in src/.** Defined only in the refactor plan. |
| `GamePhase` | Tests #38-41 | **Does not exist in any .cs file.** Defined only in the refactor plan. |
| `WorkerNode` | Tests #26, #28-31 | Referenced by the plan but not checked in the codebase. Likely new. |
| `NodeCapability`, `NodeStatus` | Tests #28-31 | Enum types not found in current codebase. |
| `PortRange`, `PortExhaustedException` | Tests #34-37 | Not in current codebase. |

### 3.2 Missing Services (Cannot Compile)

| Service | Referenced In Tests | Status |
|---|---|---|
| `UnifiedScoringEngine` | Tests #9, #11, #82 | **Does not exist.** Only `ScoringService` exists in the codebase. |
| `ScoreDecayCalculator` | Tests #1A, #1D, #81 | **Does not exist.** Decay logic is a private method in `ScoringService.ApplyScoreDecay()`. |
| `FlagHashVerification` | Tests #1A | **Does not exist.** |
| `RegexVerification` | Test #4-5 | **Does not exist.** |
| `ScriptVerification` | Tests #6-8 | **Does not exist.** |
| `IVerificationStrategy` | (implied) | **Does not exist.** |
| `IVirtualMachineProvider` | Tests #19-24 | **Does not exist.** Only concrete `VmManager` exists. |
| `KvmProvider` | Tests #15-17, #19-24 | **Does not exist.** `VmManager` is not an abstraction. |
| `LocalImageImporter` | Tests #25-27 | **Does not exist.** |
| `WeightedScheduler` | Tests #28-31 | **Does not exist.** |
| `QueueManager` | Tests #32-33 | **Does not exist.** |
| `PortCapacityTracker` | Tests #34-35 | **Does not exist.** |
| `AgentPortAllocator` | Tests #36-37 | **Does not exist.** |
| `IDistributedLockService` | Test #82-83 | **Does not exist.** (In refactor plan's Phase 2). |
| `LocalSemaphoreLock` | Test #83 | **Does not exist.** |
| `SubmissionCreateRequest` | Test #11, #82 | **Does not exist.** |
| `ScoreDecay` (enum) | Test #1D | **Exists** in `ScoringRule.cs`. OK. |
| `ScoringSubmissionType` | Tests throughout | **Exists** in `ScoringRule.cs`. OK. |

### 3.3 Existing Types That The Tests Expect But Work Differently

| Test Expectation | Reality |
|---|---|
| `Submission` has `AttemptNumber`, `Score` | Both exist. OK. |
| `Submission.UserId` is `Guid` | It's `Guid?`. Test #9 seeds with `Guid.NewGuid()`. Works but nullable means null safety needed. |
| `Submission.TeamId` is `int` (not nullable) | Yes. OK. |
| `Submission.ParticipationId` is `int` (not nullable) | Yes. OK. |
| `ScoringRule.ExpectedAnswerHash` exists | Yes. OK. |
| `ScoringRule.VerificationConfig` exists | Yes. OK. |
| `GameChallenge.Type` supports `ChallengeType.IRChallenge` | Yes. OK. |
| `IRCheckpoint` has `ChallengeId`, `OrderIndex`, `Description`, `Score`, `IsRequired`, `VerificationType` | Yes. OK. |

---

## 4. Test #9 -- IR Checkpoint Completion Critical Issue

**Test #9** (`RecordIRCheckpointCompletion_WritesSubmissionRecord`) calls:

```csharp
await engine.RecordIRCheckpointCompletionAsync(
    challenge.Id, checkpoint.Id, Guid.NewGuid(),
    game.Id, team.Id, part.Id, CancellationToken.None);
```

**Problem 1**: The `UnifiedScoringEngine` does not exist, so `RecordIRCheckpointCompletionAsync` does not exist. Its signature accepts `challengeId, checkpointId, userId, gameId, teamId, partId`.

**Problem 2**: The existing `IRChallengeController.SubmitCheckpoint()` does NOT create `Submission` records. It only writes to `IRInstance.CheckpointResults` (a JSON blob in the database). There is no mechanism in the current controller to create a Submission when a checkpoint is completed. The plan assumes the UnifiedScoringEngine will handle this, but:

- The controller has no reference to any scoring engine for checkpoint completion
- The `SubmitCheckpoint` endpoint uses `VerifyManualAnswer()` (a private static method) for validation
- No Submission record is created in the existing flow

**Problem 3**: The test seeds teamId and partId directly into the DB via `SeedTeamAsync` and `SeedParticipationAsync`. These methods do not exist. The existing codebase has `TestDataSeeder.CreateTeamAsync()` and `TestDataSeeder.JoinGameAsync()`, which return `SeededTeam` and `SeededParticipation` with `Id` properties. But the naming is different.

**Problem 4**: Even if the methods existed, the controller endpoint `/api/v1/ir-challenges/instances/{instanceId}/checkpoints/{checkpointId}/submit` doesn't accept `teamId` or `partId` in the URL or body. It derives the user from the JWT token (`_userManager.GetUserAsync(User)`). The test bypasses the controller entirely and calls the engine directly, so it's not testing the actual API flow -- it's testing an internal method that hasn't been designed yet.

---

## 5. ChallengeSubmissionType -- Missing Entity Definition

**Test #11** references `Context.ChallengeSubmissionTypes.Add(new ChallengeSubmissionType {...})`. This requires:

1. A `ChallengeSubmissionType` entity class
2. A `DbSet<ChallengeSubmissionType>` on `AppDbContext`
3. Fluent API configuration in `AppDbContext.OnModelCreating()`
4. A navigation property on `GameChallenge` (refactor plan specifies `public List<ChallengeSubmissionType> SubmissionTypes { get; set; } = [];`)

None of these exist. The refactor plan defines the design but it has not been implemented.

Additionally, the refactor plan states the entity file should be at `src/GZCTF/Models/Data/ChallengeSubmissionType.cs` and `Modify: src/GZCTF/Models/Data/Challenge.cs` for the navigation property. The TDD tests cannot pass until these are implemented.

---

## 6. Unit Test Project Configuration Gaps

### 6.1 No Playwright Config

The plan's CI pipeline and E2E section reference a `playwright.config.ts` at `tests/e2e/config/playwright.config.ts`. **No such file exists.** The existing E2E tests (`tests/e2e/submission-scoring.spec.ts`, etc.) exist but without a Playwright configuration or `package.json` in the `tests/` directory. The plan's E2E infrastructure is from-scratch.

### 6.2 GZCTF.Test Project Has No `ImplicitUsings`

The existing `GZCTF.Test.csproj` does NOT have `<ImplicitUsings>enable</ImplicitUsings>`, unlike the integration test project. The plan's unit tests (e.g., `ScoreDecayTests`, `VmSecurityTests`) use `Assert.Throws`, `Theory`, `InlineData` etc. without explicit `using Xunit;`. These may work because the test SDK auto-includes certain namespaces, but it is inconsistent with the integration test project and may cause confusion.

---

## 7. SeedTeamAsync / SeedParticipationAsync Mismatch

The plan's `GZCTFTestFixture` declares:

```csharp
protected async Task<Team> SeedTeamAsync(int gameId) { ... }
protected async Task<Participation> SeedParticipationAsync(int gameId, int teamId) { ... }
```

But the existing `TestDataSeeder` has:

| Existing Method | Returns | Plan Expects |
|---|---|---|
| `CreateTeamAsync(services, ownerId, name)` | `SeededTeam` | `SeedTeamAsync(gameId)` returning `Team` |
| `JoinGameAsync(services, gameId, teamId, userId)` | `SeededParticipation` | `SeedParticipationAsync(gameId, teamId)` returning `Participation` |

The plan's methods take different parameters, have different return types, and don't exist. The test developer must either:
- Implement these missing methods on GZCTFTestFixture
- Or refactor tests to use existing TestDataSeeder methods

---

## 8. Test Assumptions Not Validated

### 8.1 Rate Limiting (Tests #12-14)

The plan's rate limit tests assume:
- There is a functional rate limiter on `/api/v1/submissions` allowing 10 requests/min per user
- The `DisableRateLimit` setting in the factory is set to `false` (it is currently set to `"true"`)

Currently, `GZCTFApplicationFactory` sets `DisableRateLimit = "true"`. The rate limit tests would all pass trivially (no 429 ever returned) unless this configuration is removed or overridden. The plan does not mention changing this setting.

Additionally, "different user" isolation for rate limiting (Test #14) requires the rate limiter to key by authenticated user, which may not work when using `X-Test-Role` / `X-Test-UserId` headers (the plan's `CreateAuthenticatedClient` mechanism). The existing authentication flow uses actual ASP.NET Identity login via `/api/Account/Login`. The plan's `CreateAuthenticatedClient()` adds test headers, but the application must have middleware to recognize these and authenticate the user. No such middleware exists in the codebase.

### 8.2 IR Instance Access Details Sanitization (Test #18)

The test calls `IRInstanceDetailModel.SanitizeAccessDetails()`. This method does not exist on `IRInstanceDetailModel`. The existing `IRInstanceDetailModel` is defined in `src/GZCTF/Models/Request/Game/IRChallengeModels.cs` (not shown in full, but from the refactor plan context, it's a data transfer object, not a sanitization service).

### 8.3 KVM Test Skipping (Tests #19-24)

The plan uses `[Trait("Category", "RequiresKVM")]` which is supported by xUnit. The CI pipeline filters with `--filter "Category!=RequiresKVM"`. This mechanism will work, but:

- There is **no CI pipeline** (`.github/workflows/tdd-pipeline.yml` does not exist)
- The CI config hardcodes test server credentials that likely need to be GitHub secrets
- The plan expects `dotnet test src/GZCTF.Test -c Release` but the project path is different -- the test projects are at `src/GZCTF.Test/GZCTF.Test.csproj` and `src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj`

### 8.4 Concurrent Submission Safety (Test #66, #82)

Test #82 fires 1000 concurrent submissions without any distributed locking mechanism. The plan assumes `IDistributedLockService` (from refactor plan Phase 2) exists, but it does not. Without a lock, concurrent submissions for the same flag/team/challenge race condition is NOT handled. The test may produce flaky results: sometimes `accepted > 0`, sometimes 0 or inconsistent values.

---

## 9. CI Pipeline Issues

### 9.1 No CI Exists

No `.github/workflows/*.yml` files exist in the repository. The plan's CI pipeline must be created from scratch.

### 9.2 Hardcoded Credentials

The CI pipeline plan hardcodes database credentials in the YAML:

```yaml
ConnectionStrings__Database: "Host=<test-server-ip>;Port=5433;Database=gzctf_test;Username=testuser;Password=testpass"
ConnectionStrings__RedisCache: "<test-server-ip>:6380"
```

These must be stored as GitHub secrets, not in the committed workflow file. The plan shows them in plain text.

### 9.3 Project Path Discrepancy

The CI runs `dotnet test src/GZCTF.Test` and `dotnet test src/GZCTF.Integration.Test`. Both are correct relative to the solution root. But `tests/Perf` in the perf-tests job does not exist as a `.csproj` -- no `tests/Perf/` directory or project file exists in the codebase.

### 9.4 Sequential Job Dependencies

The CI is configured as:
```
unit-tests -> integration-tests -> e2e-tests -> perf-tests -> security-tests
```

This is purely sequential. Each stage depends on the previous. This means:
- Integration tests wait for unit tests (reasonable)
- E2E tests wait for integration tests (reasonable if they share the same server)
- Performance tests wait for E2E (unnecessary -- perf should run in parallel)
- Security tests wait for perf (unnecessary)

Total CI time would be the sum of all stages. For E2E tests with VM lifecycle tests that take 120+ seconds each, this pipeline could take 30+ minutes.

---

## 10. Encrypted Test Server Credentials

The plan's `TestConfig.cs` hardcodes:

- `ServerHost = "<test-server-ip>"`
- `DbPassword = "testpass"`
- `RedisHost = "<test-server-ip>:6380"`
- `KvmUri = "qemu:///system"`
- `TestVmTemplate = "/var/lib/gzctf-test/images/windows-server-2012-test.qcow2"`

**Risks:**
- The password `testpass` is weak and hardcoded
- `qemu:///system` requires root/libvirt access from within the test process
- The VM template path assumes a specific file exists on the test server
- These values would be committed to the repository
- The test server `<test-server-ip>` may not be accessible from CI runners

The existing integration test infrastructure solves this elegantly with Testcontainers -- it spins up a local PostgreSQL container per test run. The plan should follow the same pattern for test isolation.

---

## 11. Parallel Execution Analysis

### 11.1 Unit Tests (GZCTF.Test)

The plan's unit tests (ScoreDecayTests, VerificationStrategyTests, VmSecurityTests, WeightedSchedulerTests, etc.) are pure logic tests with no shared state. They are safe to run in parallel.

**However**, the existing GZCTF.Test project does not configure parallelization. xUnit by default runs tests within a single test class sequentially but multiple test classes in parallel. This is fine for these tests.

### 11.2 Integration Tests (GZCTF.Integration.Test)

The existing collection-based serialization ([CollectionDefinition]) ensures no parallel execution. The plan's GZCTFTestFixture tests should also be in a non-parallel collection if they share the same database. The plan does not specify collection definitions for the new tests.

If the plan's tests are added to the existing `IntegrationTestCollection`, they will be serialized (safe). If they are put in a separate collection, they will run in parallel with existing IntegrationTestCollection tests and potentially conflict (unsafe).

### 11.3 E2E Tests

The Playwright config specifies `workers: 2` for parallel E2E execution. This is feasible if the backend supports it. The `webServer` config in the plan's Playwright config runs `dotnet run --project src/GZCTF --urls http://localhost:8080`, which is a single server. Multiple E2E workers sharing one server instance may cause state conflicts if tests modify shared data (games, teams, submissions).

---

## 12. Summary of Critical Blockers

### Blockers That Prevent Compilation

1. **ChallengeSubmissionType entity does not exist** -- Test #11 cannot compile.
2. **GamePhase entity does not exist** -- Tests #38-41 cannot compile.
3. **UnifiedScoringEngine does not exist** -- Tests #9, #11, #82 cannot compile.
4. **ScoreDecayCalculator does not exist** -- Tests #1A, #1D, #81 cannot compile.
5. **IVerificationStrategy / FlagHashVerification / RegexVerification / ScriptVerification do not exist** -- Tests #1-8 cannot compile.
6. **IVirtualMachineProvider / KvmProvider do not exist** -- Tests #15-24 cannot compile.
7. **WeightedScheduler / QueueManager / PortCapacityTracker / AgentPortAllocator do not exist** -- Tests #28-37 cannot compile.
8. **LocalImageImporter does not exist** -- Tests #25-27 cannot compile.
9. **IDistributedLockService / LocalSemaphoreLock do not exist** -- Tests #82-83 cannot compile.
10. **SubmissionCreateRequest does not exist** -- Tests #11, #82 cannot compile.

### Blockers That Cause Runtime Failures

11. **Moq not referenced** -- All tests using `Mock.Of<>` or `Mock<>` will fail to compile.
12. **Respawn not referenced** -- `ResetDatabaseAsync()` cannot be implemented without it (or an alternative).
13. **ResetDatabaseAsync() not implemented** -- `GZCTFTestFixture.InitializeAsync()` calls a non-existent method.
14. **SeedTeamAsync / SeedParticipationAsync not implemented** -- Test #9 calls non-existent methods.
15. **DisableRateLimit = "true"** -- Rate limit tests (#12-14) will pass trivially (never get 429).
16. **CreateAuthenticatedClient mechanism does not exist** -- Auth headers are not wired into the middleware pipeline.

### Design-Level Concerns

17. **Test #9 bypasses controller and tests internal engine method** -- Not an API-level test; tests an unimplemented internal interface.
18. **Hardcoded credentials in TestConfig** -- Security risk; not compatible with the existing Testcontainers pattern.
19. **No CI pipeline exists** -- The YAML must be created and credentials externalized to secrets.
20. **Integration test parallel safety** -- If GZCTFTestFixture tests are in a different collection from existing IntegrationTestCollection tests, database state conflicts are guaranteed.
21. **ChallengeSubmissionType's DB configuration unspecified** -- The plan does not define FK constraints, indexes, or cascade behavior needed for the AppDbContext configuration.
