# TeamLab Permissions, Access, And Experience Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reusable permission groups and resource roles, replace single-peer VPN grants with per-user access sessions, and expose clear administrator and player experiences for scenario and rollout operations.

**Architecture:** Identity owns generic groups and resource role bindings; TeamLab contributes resource hierarchy and authorization adapters. Runtime access sessions allocate one WireGuard peer per user and one active device maximum. Frontend views consume projection APIs and shared design tokens without embedding orchestration logic or page-local style systems.

**Tech Stack:** .NET 10, ASP.NET Core authorization, EF Core/PostgreSQL, WireGuard, React/TypeScript, SWR, Mantine/shared frontend tokens, generated OpenAPI client, xUnit, Playwright.

---

## Task 1: Add Generic Permission Groups And Resource Roles

**Files:**
- Create: `src/GZCTF/Modules/Identity/Domain/Access/AccessGroup.cs`
- Create: `src/GZCTF/Modules/Identity/Domain/Access/AccessGroupMember.cs`
- Create: `src/GZCTF/Modules/Identity/Domain/Access/ResourceRoleBinding.cs`
- Create: `src/GZCTF/Modules/Identity/Domain/Access/ResourceRole.cs`
- Create: `src/GZCTF/Modules/Identity/Infrastructure/Persistence/AccessControlEntityConfigurations.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Migrations/20260722160000_AddResourceAccessGroups.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/ResourceAccessGroupMigrationTests.cs`

- [ ] **Step 1: Add membership and binding constraint tests**

```csharp
[Fact]
public async Task GroupMembership_AndResourceBinding_AreUnique()
{
    await using var context = await fixture.CreateMigratedContextAsync();
    var group = AccessFixture.Group();
    context.AddRange(group,
        AccessFixture.Member(group, fixture.UserId),
        AccessFixture.Member(group, fixture.UserId));

    await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
}
```

- [ ] **Step 2: Define generic access entities**

```csharp
public enum ResourceRole : byte
{
    Owner = 0,
    Editor = 1,
    Publisher = 2,
    Operator = 3,
    Observer = 4,
    Auditor = 5,
    Player = 6
}

public enum AccessSubjectKind : byte
{
    User = 0,
    Group = 1
}

public sealed class ResourceRoleBinding
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public AccessSubjectKind SubjectKind { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public ResourceRole Role { get; set; }
    public Guid GrantedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

Support User and Group subjects. Service access remains represented by the API token's issuing user plus token resource grants; do not create a second service-account authentication system.

- [ ] **Step 3: Configure uniqueness and deletion rules**

Use unique membership `(GroupId, UserId)` and unique binding `(SubjectKind, SubjectId, ResourceType, ResourceId, Role)`. Deleting a group removes its memberships and bindings but never deletes resources.

- [ ] **Step 4: Run migration tests and commit**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter FullyQualifiedName~ResourceAccessGroupMigrationTests
git add -- src/GZCTF/Modules/Identity/Domain/Access src/GZCTF/Modules/Identity/Infrastructure/Persistence/AccessControlEntityConfigurations.cs src/GZCTF/Models/AppDbContext.cs src/GZCTF/Migrations src/GZCTF.Integration.Test/Tests/Database/ResourceAccessGroupMigrationTests.cs
git commit -m "feat: add generic resource access groups"
```

Expected: PASS.

## Task 2: Implement Resource Authorization And Token Ceiling

**Files:**
- Create: `src/GZCTF/Modules/Identity/Application/ResourceAuthorization/IResourceAuthorizationService.cs`
- Create: `src/GZCTF/Modules/Identity/Application/ResourceAuthorization/ResourceAuthorizationService.cs`
- Create: `src/GZCTF/Modules/Identity/Application/ResourceAuthorization/IResourceHierarchyProvider.cs`
- Modify: `src/GZCTF/Modules/Identity/Application/ApiScopeAuthorizationHandler.cs`
- Modify: `src/GZCTF/Modules/Identity/Application/ApiTokenIssuer.cs`
- Modify: `src/GZCTF/Modules/Identity/IdentityModuleRegistration.cs`
- Create: `src/GZCTF.Test/UnitTests/Security/ResourceAuthorizationTests.cs`

- [ ] **Step 1: Add authorization matrix tests**

Cover direct user binding, group membership, inherited parent resource, admin override, token scope, token resource grant, revoked membership, and prevention of token privilege escalation.

```csharp
[Theory]
[InlineData(ResourceRole.Operator, ResourceAction.RuntimeDestroy, true)]
[InlineData(ResourceRole.Observer, ResourceAction.RuntimeDestroy, false)]
[InlineData(ResourceRole.Auditor, ResourceAction.TrafficExport, true)]
public async Task RoleMatrix_EnforcesDeclaredActions(
    ResourceRole role, ResourceAction action, bool allowed)
{
    Assert.Equal(allowed, await fixture.IsAllowedAsync(role, action));
}
```

- [ ] **Step 2: Define the authorization port**

```csharp
public interface IResourceAuthorizationService
{
    Task RequireAsync(
        ActorContext actor,
        ResourceKey resource,
        ResourceAction action,
        CancellationToken token);
    Task<bool> IsAllowedAsync(
        ActorContext actor,
        ResourceKey resource,
        ResourceAction action,
        CancellationToken token);
}
```

Define the complete action vocabulary in the same task:

```csharp
public enum ResourceAction : byte
{
    Read = 0,
    Edit = 1,
    Publish = 2,
    ManagePermissions = 3,
    RolloutOperate = 4,
    RuntimeOperate = 5,
    RuntimeDestroy = 6,
    AccessUse = 7,
    TrafficRead = 8,
    TrafficExport = 9
}
```

- [ ] **Step 3: Resolve direct and group roles with parent resources**

Each module registers an `IResourceHierarchyProvider`. Authorization loads direct bindings plus parent keys, computes the highest applicable role, and applies a fixed action matrix. Cache only positive membership/binding lookups with projection revision invalidation.

- [ ] **Step 4: Enforce API token ceilings**

An API token request must pass normal actor authorization, required scope, and token resource grant. Issuance may grant only resources and actions the issuer currently holds. Membership or binding revocation invalidates token authorization immediately through projection revision.

- [ ] **Step 5: Run security tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~ResourceAuthorizationTests
git add -- src/GZCTF/Modules/Identity/Application/ResourceAuthorization src/GZCTF/Modules/Identity/Application/ApiScopeAuthorizationHandler.cs src/GZCTF/Modules/Identity/Application/ApiTokenIssuer.cs src/GZCTF/Modules/Identity/IdentityModuleRegistration.cs src/GZCTF.Test/UnitTests/Security/ResourceAuthorizationTests.cs
git commit -m "feat: enforce resource role authorization"
```

Expected: PASS.

## Task 3: Bind TeamLab Resource Hierarchy To Generic Authorization

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabResourceHierarchyProvider.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAuthorizationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabScenariosController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRolloutsController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTrafficController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs`
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabResourceAuthorizationTests.cs`

- [ ] **Step 1: Add TeamLab hierarchy tests**

Assert Scenario roles apply to scenario versions, Rollout roles apply to targets and owned runtimes, standalone runtime owner bindings apply only to that runtime, and Auditor access does not permit runtime mutation.

- [ ] **Step 2: Implement hierarchy resolution**

```csharp
public Task<IReadOnlyList<ResourceKey>> GetParentsAsync(
    ResourceKey resource, CancellationToken token) => resource.Type switch
{
    "scenario-version" => ScenarioParentsAsync(resource, token),
    "rollout-target" => RolloutParentsAsync(resource, token),
    "runtime" => RuntimeParentsAsync(resource, token),
    "traffic-evidence" => TrafficParentsAsync(resource, token),
    _ => Task.FromResult<IReadOnlyList<ResourceKey>>([])
};
```

- [ ] **Step 3: Replace owner/admin-only checks**

All TeamLab Open API controllers call `IResourceAuthorizationService`. Keep platform Admin override in the generic service; do not duplicate role comparisons in controllers.

- [ ] **Step 4: Synchronize competition members as Player bindings**

When the Penetration adapter creates or updates a rollout target, it reconciles accepted team members into user `ResourceRoleBinding` rows with Player role on the target runtime. Team membership removal revokes the binding; it does not destroy the runtime or affect other members. TeamLab receives only resource and user identities and never queries competition tables.

- [ ] **Step 5: Run TeamLab authorization tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabResourceAuthorizationTests
git add -- src/GZCTF/Modules/TeamLab/Application/TeamLabResourceHierarchyProvider.cs src/GZCTF/Modules/TeamLab/Application/TeamLabAuthorizationService.cs src/GZCTF/Modules/TeamLab/Api src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabResourceAuthorizationTests.cs
git commit -m "feat: authorize TeamLab resources through role bindings"
```

Expected: PASS.

## Task 4: Replace Access Grants With Per-User Access Sessions

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeAccess.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRuntimeEntityConfigurations.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabAccessGrantService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabRuntimeContracts.cs`
- Create: `src/GZCTF/Migrations/20260722170000_AddTeamLabAccessSessions.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabAccessGrantTests.cs`

- [ ] **Step 1: Add access-session behavior tests**

Cover two team members concurrently connected, one active device per user, deterministic free-address allocation, replacement revocation, one-time download, runtime access closed, expiration, and generation reset.

```csharp
[Fact]
public async Task Sessions_AllowDifferentUsersWithoutEvictingEachOther()
{
    var first = await fixture.CreateSessionAsync(fixture.UserA, "laptop-a");
    var second = await fixture.CreateSessionAsync(fixture.UserB, "laptop-b");

    Assert.NotEqual(first.ClientAddress, second.ClientAddress);
    Assert.False(fixture.Session(first.Id).Revoked);
    Assert.False(fixture.Session(second.Id).Revoked);
}
```

- [ ] **Step 2: Add user and device identity**

Rename the domain concept to `TeamLabAccessSession`. Add `UserId`, protected device fingerprint, display name, `LastHandshakeAt`, revoke reason, and applied configuration version. Use a partial unique index for one active session per `(RuntimeId, Generation, UserId)` and a unique active client address.

- [ ] **Step 3: Allocate addresses from an explicit access pool**

Reserve gateway, router, infrastructure, broadcast, and existing session addresses. Allocate the first free host in deterministic order inside a database transaction guarded by the runtime subject lock. Return `access_pool_exhausted` rather than reusing an address.

- [ ] **Step 4: Replace only the same user's previous device**

Creating a new device revokes and removes only that user's old peer. Other users remain active. Store private key encrypted and expose it only through the existing one-time download token.

- [ ] **Step 5: Migrate existing grants**

Existing grants without user identity are revoked during migration and remain audit history. Do not guess ownership or silently reactivate them.

- [ ] **Step 6: Run access tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabAccessGrantTests
git add -- src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeAccess.cs src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRuntimeEntityConfigurations.cs src/GZCTF/Modules/TeamLab/Application/TeamLabAccessGrantService.cs src/GZCTF/Modules/TeamLab/Contracts/TeamLabRuntimeContracts.cs src/GZCTF/Migrations src/GZCTF.Test/UnitTests/TeamLab/TeamLabAccessGrantTests.cs
git commit -m "feat: add per-user TeamLab access sessions"
```

Expected: PASS.

## Task 5: Support Multiple WireGuard Peers And Connection Health

**Files:**
- Modify: `src/GZCTF/Modules/TeamLab/Application/ITeamLabNodeExecutor.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- Modify: `src/GZCTF.Agent/Models/TeamLabModels.cs`
- Modify: `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- Modify: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/Runtimes/TeamLabAccessHealthService.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabAccessSessionNodeTests.cs`

- [ ] **Step 1: Add peer reconciliation tests**

Assert applying a second peer preserves the first, revoking one preserves others, repeated apply is idempotent, close access disables all peers, and open access restores active sessions from desired state.

- [ ] **Step 2: Change Agent access commands from replace-all to reconcile-set**

```csharp
public sealed record TeamLabAccessDesiredState(
    int RuntimeId,
    int Generation,
    string InterfaceName,
    bool Enabled,
    IReadOnlyList<TeamLabAccessPeerModel> Peers);
```

Agent computes add/update/remove against the current interface. It must not recreate WireGuard keys or addresses when desired state is unchanged.

- [ ] **Step 3: Persist handshake health**

Read `wg show ... latest-handshakes` through structured parsing, map public keys to sessions, and update `LastHandshakeAt` through a bounded background projection. Never log private keys.

- [ ] **Step 4: Implement player-safe connection checks**

Return session state, last handshake, entry IP reachability, DNS probe, and entry service result. Do not return node ID, router namespace, internal management IP, or shell output.

- [ ] **Step 5: Run node access tests and commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabAccessSessionNodeTests
git add -- src/GZCTF/Modules/TeamLab/Application/ITeamLabNodeExecutor.cs src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs src/GZCTF.Agent/Models/TeamLabModels.cs src/GZCTF.Agent/Controllers/TeamLabController.cs src/GZCTF.Agent/Services/TeamLabNetworkService.cs src/GZCTF/Modules/TeamLab/Application/Runtimes/TeamLabAccessHealthService.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabAccessSessionNodeTests.cs
git commit -m "feat: reconcile TeamLab WireGuard peer sessions"
```

Expected: PASS.

## Task 6: Publish Permission Group And Access Session APIs

**Files:**
- Create: `src/GZCTF/Modules/Identity/Api/OpenAccessGroupsController.cs`
- Create: `src/GZCTF/Modules/Identity/Application/ResourceAuthorization/AccessGroupApplicationService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs`
- Modify: `docs/commercialization/openapi/open-v1.json`
- Create: `src/GZCTF.Integration.Test/Tests/Api/OpenAccessGroupApiTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Api/OpenTeamLabAccessSessionApiTests.cs`

- [ ] **Step 1: Add API contract tests**

Cover group create/update/delete, membership add/remove, role bind/unbind, prevention of last-owner removal, access-session list/create/revoke, one-time download, one-device replacement, player scope, and rate limit.

- [ ] **Step 2: Implement generic access-control routes**

Implement:

- `GET/POST /api/open/v1/access-groups`
- `GET/PATCH/DELETE /api/open/v1/access-groups/{id}`
- `GET/POST /api/open/v1/access-groups/{id}/members`
- `DELETE /api/open/v1/access-groups/{id}/members/{userId}`
- `GET/POST /api/open/v1/resource-role-bindings`
- `DELETE /api/open/v1/resource-role-bindings/{id}`

Writes require idempotency keys and produce audit events.

- [ ] **Step 3: Implement runtime access-session routes**

Implement:

- `GET/POST /api/open/v1/teamlab/runtimes/{id}/access-sessions`
- `DELETE /api/open/v1/teamlab/runtimes/{id}/access-sessions/{sessionId}`
- `GET /api/open/v1/teamlab/runtimes/{id}/access-sessions/{sessionId}/configuration`
- `GET /api/open/v1/teamlab/runtimes/{id}/access-sessions/{sessionId}/health`

The authenticated user ID comes from `ActorContext`; clients cannot submit another user's ID unless the actor has Operator permission.

- [ ] **Step 4: Regenerate and verify OpenAPI**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter "FullyQualifiedName~OpenAccessGroupApiTests|FullyQualifiedName~OpenTeamLabAccessSessionApiTests|FullyQualifiedName~OpenApiDocumentationTests"
```

- [ ] **Step 5: Commit**

```powershell
git add -- src/GZCTF/Modules/Identity/Api/OpenAccessGroupsController.cs src/GZCTF/Modules/Identity/Application/ResourceAuthorization/AccessGroupApplicationService.cs src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs docs/commercialization/openapi/open-v1.json src/GZCTF.Integration.Test/Tests/Api/OpenAccessGroupApiTests.cs src/GZCTF.Integration.Test/Tests/Api/OpenTeamLabAccessSessionApiTests.cs
git commit -m "feat: expose resource groups and access sessions"
```

Expected: PASS.

## Task 7: Build Focused Administrator Views

**Files:**
- Create: `src/GZCTF/ClientApp/src/components/teamlab/admin/ScenarioLibraryPanel.tsx`
- Create: `src/GZCTF/ClientApp/src/components/teamlab/admin/RolloutControlPanel.tsx`
- Create: `src/GZCTF/ClientApp/src/components/teamlab/admin/RolloutTargetTable.tsx`
- Create: `src/GZCTF/ClientApp/src/components/teamlab/admin/RuntimeDetailDrawer.tsx`
- Create: `src/GZCTF/ClientApp/src/components/teamlab/admin/NodeBudgetSummary.tsx`
- Create: `src/GZCTF/ClientApp/src/components/teamlab/admin/PermissionGroupEditor.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/games/[id]/Penetration.tsx`
- Modify: `src/GZCTF/ClientApp/src/generated/Api.ts`
- Create: `src/GZCTF/ClientApp/src/components/teamlab/admin/__tests__/RolloutControlPanel.test.tsx`

- [ ] **Step 1: Add interaction tests for critical controls**

Test capacity blocked display, image distribution progress, canary/wave progress, pause/resume, access close/open, suspend/resume, target filtering, destroy confirmation, and cleanup pending details.

- [ ] **Step 2: Split the existing admin page into task views**

The page composes Scenario, Rollout, Team Environments, and Node Capacity tabs. It contains routing and authorization only; each focused component owns its query and command surface.

- [ ] **Step 3: Use projection polling without page refresh**

Use SWR keys by resource ID, stable callbacks, memoized table columns, and paginated/virtualized target rows. Mutations optimistically show the accepted operation and then revalidate operation plus projection. Do not reload the browser page.

- [ ] **Step 4: Keep styles in the shared design layer**

Use existing theme tokens and shared status/progress components. Do not add page-local color systems, inline layout constants, or duplicated cards. Follow the repository's React best-practice requirement for memoization, data fetching, lazy tab rendering, and bundle boundaries.

- [ ] **Step 5: Run frontend checks and commit**

```powershell
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp test --run RolloutControlPanel
git add -- src/GZCTF/ClientApp/src/components/teamlab/admin src/GZCTF/ClientApp/src/pages/admin/games/[id]/Penetration.tsx src/GZCTF/ClientApp/src/generated/Api.ts
git commit -m "feat: add TeamLab rollout administration views"
```

Expected: PASS.

## Task 8: Build The Player Environment Workspace

**Files:**
- Create: `src/GZCTF/ClientApp/src/components/teamlab/player/EnvironmentStage.tsx`
- Create: `src/GZCTF/ClientApp/src/components/teamlab/player/VpnDevicePanel.tsx`
- Create: `src/GZCTF/ClientApp/src/components/teamlab/player/ConnectionHealth.tsx`
- Create: `src/GZCTF/ClientApp/src/components/teamlab/player/EnvironmentResetDialog.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/games/[id]/Penetration.tsx`
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationWorkspaceService.cs`
- Modify: `src/GZCTF/Modules/Penetration/Contracts/PenetrationContracts.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlayerWorkspaceContractTests.cs`

- [ ] **Step 1: Add player contract tests**

Assert queue/preparation/provisioning/ready/maintenance/reset/end stages, one-device session model, connection health, reset quota, infrastructure-failure refund, and absence of WorkerNode/internal management data.

- [ ] **Step 2: Extend the player projection**

Return rollout target stage, runtime lifecycle states, current operation summary, maintenance message, active device summary, connection health, reset quota, and objectives. Keep privileged errors sanitized.

- [ ] **Step 3: Compose focused player components**

Render stage first, then VPN device and self-check when access is available. Reset displays remaining quota and operation progress. Maintenance/closed access is distinct from failure. Use SWR revalidation and no full page refresh.

- [ ] **Step 4: Run backend and frontend checks, then commit**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter FullyQualifiedName~TeamLabPlayerWorkspaceContractTests
pnpm --dir src/GZCTF/ClientApp check
git add -- src/GZCTF/ClientApp/src/components/teamlab/player src/GZCTF/ClientApp/src/pages/games/[id]/Penetration.tsx src/GZCTF/Modules/Penetration/Application/PenetrationWorkspaceService.cs src/GZCTF/Modules/Penetration/Contracts/PenetrationContracts.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlayerWorkspaceContractTests.cs
git commit -m "feat: add TeamLab player environment workspace"
```

Expected: PASS.

## Task 9: Permissions And Access Acceptance Gate

**Files:**
- Create: `docs/commercialization/runbooks/teamlab-permissions-and-player-access.md`
- Modify: `docs/commercialization/open-api-v1-guide.md`
- Modify: `docs/commercialization/teamlab-api-foundation-contract.md`

- [ ] **Step 1: Run security, access, API, and player slices once**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore --filter "FullyQualifiedName~ResourceAuthorization|FullyQualifiedName~TeamLabAccess|FullyQualifiedName~TeamLabPlayer"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --no-restore --filter "FullyQualifiedName~AccessGroup|FullyQualifiedName~AccessSession|FullyQualifiedName~OpenApi"
```

- [ ] **Step 2: Run frontend checks and visual interaction tests once**

```powershell
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp test --run
```

Use Playwright at desktop and mobile widths to verify no overlapping controls, stable target table dimensions, immediate operation feedback, VPN replacement confirmation, and maintenance states.

- [ ] **Step 3: Run one Release build and diff check**

```powershell
dotnet build src/GZCTF.slnx -c Release --no-restore
git diff --check
```

- [ ] **Step 4: Document and commit operational behavior**

```powershell
git add -- docs/commercialization/runbooks/teamlab-permissions-and-player-access.md docs/commercialization/open-api-v1-guide.md docs/commercialization/teamlab-api-foundation-contract.md
git commit -m "docs: add TeamLab permissions and access runbook"
```

Expected: all commands PASS before Plan 05 starts.
