# TeamLab Scenario Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn validated topology releases and immutable Docker/VM artifacts into a reusable, versioned scenario library that competitions and external callers can select without rebuilding the environment.

**Architecture:** A scenario is a stable identity; each scenario version is an immutable manifest that references one topology release and content-addressed artifacts. Validation uses an isolated TeamLab runtime, records automatic evidence, requires an explicit publisher approval, then commits and verifies portable artifacts before setting the version Ready.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, OCI Registry, existing TeamLab runtime and image distribution services, OpenAPI, xUnit, Testcontainers.

---

## Task 1: Add Scenario And Validation Domain Models

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Domain/Scenario/TeamLabScenario.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/Scenario/TeamLabScenarioVersion.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/Scenario/ScenarioValidationRun.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/Scenario/TeamLabScenarioManifest.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabScenarioEntityConfigurations.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Migrations/20260722110000_AddTeamLabScenarioLibrary.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/TeamLabScenarioLibraryMigrationTests.cs`

- [ ] **Step 1: Add persistence invariants to the migration test**

```csharp
[Fact]
public async Task ScenarioVersion_IsImmutableAndUniqueByScenarioVersionNumber()
{
    await using var context = await fixture.CreateMigratedContextAsync();
    var scenario = ScenarioFixture.Create();
    context.AddRange(scenario,
        Version(scenario, number: 1, manifestHash: Hash('a')),
        Version(scenario, number: 1, manifestHash: Hash('b')));

    await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
}
```

- [ ] **Step 2: Define states and aggregates**

```csharp
public enum TeamLabScenarioVersionStatus : byte
{
    Draft = 0,
    Validating = 1,
    Ready = 2,
    Failed = 3,
    Retired = 4
}

public enum ScenarioValidationStatus : byte
{
    Pending = 0,
    Deploying = 1,
    Verifying = 2,
    AwaitingApproval = 3,
    Committing = 4,
    Ready = 5,
    Failed = 6,
    Cancelled = 7
}

public sealed class TeamLabScenarioVersion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ScenarioId { get; set; }
    public int Version { get; set; }
    public Guid TopologyReleaseId { get; set; }
    public TeamLabScenarioVersionStatus Status { get; set; }
    public string ManifestJson { get; set; } = string.Empty;
    public string ManifestHash { get; set; } = string.Empty;
    public Guid? ApprovedById { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

`TeamLabScenario` owns name, description, owner subject, recommended version, and lifecycle. `ScenarioValidationRun` owns operation ID, validation runtime, automatic result, approval, evidence, start/completion, and failure code.

- [ ] **Step 3: Configure immutability-supporting indexes and relationships**

Use unique indexes on `(ScenarioId, Version)` and `ManifestHash`, restrict deletion of referenced topology releases, and retain validation history when a version is retired.

- [ ] **Step 4: Backfill existing releases safely**

Create one legacy scenario per topology that has a release. Backfilled versions remain Draft unless existing artifact rows are all Ready and the release is not currently bound to an active competition; do not silently claim human approval. Existing release-based runtime paths remain valid until Plan 05 cutover.

- [ ] **Step 5: Run migration tests and commit**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabScenarioLibraryMigrationTests
git add -- src/GZCTF/Modules/TeamLab/Domain/Scenario src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabScenarioEntityConfigurations.cs src/GZCTF/Models/AppDbContext.cs src/GZCTF/Migrations src/GZCTF.Integration.Test/Tests/Database/TeamLabScenarioLibraryMigrationTests.cs
git commit -m "feat: add TeamLab scenario library domain"
```

Expected: PASS.

## Task 2: Build A Canonical Immutable Scenario Manifest

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Application/Scenarios/TeamLabScenarioManifestBuilder.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Scenarios/TeamLabScenarioContracts.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseCodec.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/TeamLabReleaseAssetArtifact.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabReleaseAssetArtifactEntityConfiguration.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabScenarioManifestTests.cs`

- [ ] **Step 1: Add canonicalization tests**

```csharp
[Fact]
public void ManifestHash_IsStableAcrossDictionaryAndArtifactOrdering()
{
    var first = builder.Build(ScenarioInput.Reversed());
    var second = builder.Build(ScenarioInput.Ordered());

    Assert.Equal(first.ManifestHash, second.ManifestHash);
    Assert.Equal(first.CanonicalJson, second.CanonicalJson);
}
```

- [ ] **Step 2: Define the manifest contract**

```csharp
public sealed record TeamLabScenarioManifestModel(
    int SchemaVersion,
    Guid TopologyReleaseId,
    string TopologyHash,
    IReadOnlyList<TeamLabScenarioAssetManifestModel> Assets,
    IReadOnlyList<TeamLabScenarioNetworkManifestModel> Networks,
    IReadOnlyList<TeamLabScenarioDependencyManifestModel> Dependencies,
    IReadOnlyList<string> RequiredFeatures,
    WorkloadResourceVector RequiredResources,
    IReadOnlyList<string> AllowedOverlayKeys,
    IReadOnlyList<TeamLabScenarioValidationRuleModel> ValidationRules);
```

Sort every collection by stable keys, serialize with fixed options, and hash UTF-8 canonical JSON with SHA-256.

- [ ] **Step 3: Bind artifacts to scenario versions**

Add `ScenarioVersionId` to artifact rows while preserving `ReleaseId` for provenance. Enforce one artifact per scenario version and asset key. Artifact references use immutable Registry digest, never mutable tags.

- [ ] **Step 4: Run manifest tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabScenarioManifestTests
git add -- src/GZCTF/Modules/TeamLab/Application/Scenarios src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseCodec.cs src/GZCTF/Modules/TeamLab/Domain/TeamLabReleaseAssetArtifact.cs src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabReleaseAssetArtifactEntityConfiguration.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabScenarioManifestTests.cs
git commit -m "feat: build immutable TeamLab scenario manifests"
```

Expected: PASS.

## Task 3: Create Scenario Drafts From Topology Releases

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Application/Scenarios/ITeamLabScenarioApplicationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Scenarios/TeamLabScenarioApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabScenarioApplicationTests.cs`

- [ ] **Step 1: Add draft version behavior tests**

Assert a release can create one idempotent scenario version, an identical manifest is reused, a changed release creates a new version, and Ready/Retired versions reject mutation.

- [ ] **Step 2: Define the application port**

```csharp
public interface ITeamLabScenarioApplicationService
{
    Task<TeamLabScenarioModel> CreateAsync(
        CreateTeamLabScenarioModel command, ActorContext actor, CancellationToken token);
    Task<TeamLabScenarioVersionModel> CreateVersionAsync(
        Guid scenarioId, CreateScenarioVersionModel command,
        ActorContext actor, string idempotencyKey, CancellationToken token);
    Task<TeamLabScenarioVersionModel> GetVersionAsync(
        Guid versionId, ActorContext actor, CancellationToken token);
    Task RetireAsync(
        Guid versionId, ActorContext actor, string idempotencyKey, CancellationToken token);
}
```

- [ ] **Step 3: Keep topology publish and scenario approval separate**

Topology publishing creates an immutable topology release only. It must not mark a scenario Ready or invoke heavy bake work inline. A scenario version explicitly references the release and enters Draft.

- [ ] **Step 4: Run application tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabScenarioApplicationTests
git add -- src/GZCTF/Modules/TeamLab/Application/Scenarios src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabScenarioApplicationTests.cs
git commit -m "feat: create scenario versions from topology releases"
```

Expected: PASS.

## Task 4: Refactor Scenario Bake Into A Validation Operation

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Application/Scenarios/TeamLabScenarioValidationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Scenarios/TeamLabScenarioValidationRules.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabScenarioBakeService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeOperationJob.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabScenarioValidationTests.cs`

- [ ] **Step 1: Add validation state-machine tests**

```csharp
[Theory]
[InlineData(TeamLabRuntimeStatus.Running, ScenarioValidationStatus.AwaitingApproval)]
[InlineData(TeamLabRuntimeStatus.Failed, ScenarioValidationStatus.Failed)]
public async Task Validation_MapsRuntimeFactsWithoutFixedWaiting(
    TeamLabRuntimeStatus runtimeStatus,
    ScenarioValidationStatus expected)
{
    var run = await fixture.ReconcileAsync(runtimeStatus);
    Assert.Equal(expected, run.Status);
}
```

Also test dependency checks, network probes, artifact presence, and sanitized evidence.

- [ ] **Step 2: Add scenario validation operation kinds**

Add `ScenarioValidate`, `ScenarioApprove`, and `ScenarioRetire` to TeamLab operation handling. Validation submits an isolated runtime through the same scheduler with `TenantKey=teamlab-scenario-validation` and no player access.

- [ ] **Step 3: Reuse bake mechanics behind the new service**

Move runtime creation, overlay validation, artifact commit, and cleanup out of publish handling into `TeamLabScenarioValidationService`. Keep `TeamLabScenarioBakeService` only as a focused artifact committer during migration, then delete it after all callers move.

- [ ] **Step 4: Persist automatic evidence and await explicit approval**

Successful automatic checks set `AwaitingApproval`, not Ready. Record rule ID, outcome, duration, node, asset key, and sanitized evidence. No private key, secret overlay, or protected payload enters evidence JSON.

- [ ] **Step 5: Run validation tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabScenarioValidationTests
git add -- src/GZCTF/Modules/TeamLab/Application/Scenarios src/GZCTF/Modules/TeamLab/Application/TeamLabScenarioBakeService.cs src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeOperationJob.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabScenarioValidationTests.cs
git commit -m "feat: validate TeamLab scenarios through isolated runtimes"
```

Expected: PASS.

## Task 5: Approve, Commit, Verify, And Register Portable Artifacts

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Application/Scenarios/TeamLabScenarioValidationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Scenarios/TeamLabScenarioArtifactService.cs`
- Modify: `src/GZCTF/Modules/Content/Infrastructure/OciArtifactRegistryClient.cs`
- Modify: `src/GZCTF/Services/Fleet/ImageDistributionCoordinator.cs`
- Modify: `src/GZCTF/Services/Fleet/ImageDistributionService.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabScenarioArtifactTests.cs`

- [ ] **Step 1: Add approval and portability tests**

Cover unauthorized approval, approval before automatic success, digest mismatch, Registry failure, cross-node verification, idempotent reapproval, and cleanup after failure.

- [ ] **Step 2: Commit all assets by immutable digest**

Docker assets resolve to immutable image digests. VM assets are quiesced or cleanly shut down, committed to qcow2 artifacts, checked, uploaded, and referenced by digest. Commit operations use the validation operation ID for exact cleanup.

- [ ] **Step 3: Verify portability before Ready**

When two compatible nodes exist, verify on a node other than the commit node. Otherwise verify a fresh clone on the same node. Validation must prove artifact pull, domain/container create, expected network device, and required readiness contract.

- [ ] **Step 4: Finalize and sign the manifest**

After all artifacts verify, rebuild the manifest with final digests, sign its hash using the platform signing service, set approved identity/time, and transition the version atomically to Ready.

- [ ] **Step 5: Run artifact tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabScenarioArtifactTests
git add -- src/GZCTF/Modules/TeamLab/Application/Scenarios src/GZCTF/Modules/Content/Infrastructure/OciArtifactRegistryClient.cs src/GZCTF/Services/Fleet/ImageDistributionCoordinator.cs src/GZCTF/Services/Fleet/ImageDistributionService.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabScenarioArtifactTests.cs
git commit -m "feat: approve and register portable scenario artifacts"
```

Expected: PASS.

## Task 6: Publish Scenario Open API And Distribution Projection

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabScenariosController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Scenarios/TeamLabScenarioProjectionService.cs`
- Modify: `src/GZCTF/Modules/Identity/Application/ApiTokenScopes.cs`
- Modify: `docs/commercialization/openapi/open-v1.json`
- Modify: `docs/commercialization/open-api-v1-guide.md`
- Create: `src/GZCTF.Integration.Test/Tests/Api/OpenTeamLabScenarioApiTests.cs`

- [ ] **Step 1: Add contract tests for every scenario route**

Test create/list/get, create version, validate, approve, retire, distribution status, idempotency replay, missing scope, wrong resource grant, and immutable Ready version behavior.

- [ ] **Step 2: Implement the exact routes**

Implement these routes. All writes require `Idempotency-Key`, return `202 Accepted` plus `ApiOperationModel`, and expose stable error codes.

- `GET/POST /api/open/v1/teamlab/scenarios`
- `GET/PATCH/DELETE /api/open/v1/teamlab/scenarios/{id}`
- `POST /api/open/v1/teamlab/scenarios/{id}/versions`
- `GET /api/open/v1/teamlab/scenario-versions/{id}`
- `POST /api/open/v1/teamlab/scenario-versions/{id}/validate`
- `POST /api/open/v1/teamlab/scenario-validations/{id}/approve`
- `POST /api/open/v1/teamlab/scenario-versions/{id}/retire`
- `GET /api/open/v1/teamlab/scenario-versions/{id}/distribution`

```csharp
[HttpPost("scenario-versions/{versionId:guid}/validate")]
[Authorize(Policy = "scope:" + ApiTokenScopes.TeamLabScenariosWrite)]
public async Task<IActionResult> Validate(
    Guid versionId,
    [FromHeader(Name = "Idempotency-Key"), Required] string idempotencyKey,
    CancellationToken token)
```

- [ ] **Step 3: Build a read projection for distribution**

Return artifact digest, compatible node count, ready node count, pending count, failed count, and per-node sanitized status. Do not start distribution from a GET request.

- [ ] **Step 4: Regenerate OpenAPI and run API tests**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~OpenTeamLabScenarioApiTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~OpenApiDocumentationTests
```

- [ ] **Step 5: Commit**

```powershell
git add -- src/GZCTF/Modules/TeamLab/Api/OpenTeamLabScenariosController.cs src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs src/GZCTF/Modules/TeamLab/Application/Scenarios/TeamLabScenarioProjectionService.cs src/GZCTF/Modules/Identity/Application/ApiTokenScopes.cs docs/commercialization/openapi/open-v1.json docs/commercialization/open-api-v1-guide.md src/GZCTF.Integration.Test/Tests/Api/OpenTeamLabScenarioApiTests.cs
git commit -m "feat: expose TeamLab scenario library API"
```

## Task 7: Scenario Library Acceptance Gate

**Files:**
- Create: `docs/commercialization/runbooks/teamlab-scenario-validation-and-release.md`
- Create: `docs/commercialization/benchmarks/teamlab-scenario-library-baseline.md`
- Modify: `docs/commercialization/phase-09-teamlab-networking-commercialization.md`

- [ ] **Step 1: Run TeamLab scenario and artifact test slices once**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabScenario|FullyQualifiedName~ImageDistribution"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter "FullyQualifiedName~TeamLabScenario|FullyQualifiedName~OpenApi"
```

- [ ] **Step 2: Run one Release build and diff check**

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
git diff --check
```

- [ ] **Step 3: Record operational evidence**

Document one Docker plus Linux VM plus Windows VM scenario validation, immutable digests, cross-node verification, approval identity, Registry presence, validation runtime cleanup, and repeated version reuse.

- [ ] **Step 4: Commit the module documentation**

```powershell
git add -- docs/commercialization/runbooks/teamlab-scenario-validation-and-release.md docs/commercialization/benchmarks/teamlab-scenario-library-baseline.md docs/commercialization/phase-09-teamlab-networking-commercialization.md
git commit -m "docs: add TeamLab scenario operations runbook"
```

Expected: all commands PASS before Plan 03 starts.
