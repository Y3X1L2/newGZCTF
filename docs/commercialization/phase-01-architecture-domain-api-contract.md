# Phase 1 Architecture, Domain and API Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立可执行的模块化单体边界、正式 scoped API token 认证和一条可恢复的外部镜像 API 纵向链路，为 Phase 2-14 提供稳定契约。

**Architecture:** 保留单主站进程和单 PostgreSQL 数据库，以模块目录、application contracts、架构测试和统一外部 API 基础约束依赖。外部 API 使用独立 Bearer authentication scheme，不再把“有效 token”作为管理员旁路；镜像导入作为第一个真实 use case，完整经过权限、幂等、持久化 operation、审计和恢复。Phase 1 不拆微服务，不提前实现题目池、TeamLab 商业闭环或全量 Controller 搬迁。

**Tech Stack:** .NET 10、ASP.NET Core Authentication/Authorization、EF Core 10、PostgreSQL、Redis、NSwag、OpenTelemetry、NetArchTest.Rules 1.3.2、xUnit、Testcontainers.PostgreSql、Testcontainers.Redis。

---

## 0. 完成后的可观察效果

- 教师和出题人可以创建自己的受限 token，创建响应只显示一次 secret。
- 缺少 scope 的 token 无法进入对应 API；token 不能调用管理员内部 API。
- `/api/open/v1/images/docker-references` 返回可轮询 operation，相同 Idempotency-Key 不重复导入。
- Docker archive 导入不再使用裸 `Task.Run`；服务重启后 operation 可恢复。
- `ImageTemplate` 有明确创建者，课程通过 binding 引用模板，删除课程不删除模板主副本。
- OpenAPI contract 有独立快照，破坏性修改会在 CI 失败。
- 架构测试阻止已迁移模块重新依赖 Controller DTO、其他模块实体或 AgentClient。

## Task 1: 建立模块目录和架构门禁

**Files:**
- Modify: `src/Directory.Packages.props`
- Modify: `src/GZCTF.Test/GZCTF.Test.csproj`
- Create: `src/GZCTF.Test/UnitTests/Architecture/ArchitectureDependencyTests.cs`
- Create: `src/GZCTF/Modules/ModuleAssemblyMarker.cs`
- Create: `src/GZCTF/Composition/ModuleRegistration.cs`
- Modify: `docs/commercialization/module-boundary-map.md`

- [ ] **Step 1: 添加架构测试依赖**

`src/Directory.Packages.props`：

```xml
<PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
```

`src/GZCTF.Test/GZCTF.Test.csproj`：

```xml
<PackageReference Include="NetArchTest.Rules" />
```

- [ ] **Step 2: 写初始失败门禁**

```csharp
using NetArchTest.Rules;

public class ArchitectureDependencyTests
{
    [Fact]
    public void ExternalApiControllers_DoNotDependOnPersistenceOrAgent()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("GZCTF.Modules", StringComparison.Ordinal) == true)
            .Where(type => type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(controllers);

        var forbidden = new[] { typeof(AppDbContext), typeof(AgentClient) };
        var violations = controllers
            .SelectMany(type => type.GetConstructors()
                .SelectMany(ctor => ctor.GetParameters()
                    .Where(parameter => forbidden.Contains(parameter.ParameterType))
                    .Select(parameter => $"{type.FullName} -> {parameter.ParameterType.FullName}")))
            .ToArray();
        Assert.Empty(violations);
    }

    [Fact]
    public void DomainNamespaces_DoNotDependOnFrameworks()
    {
        var domainTypes = typeof(Program).Assembly.GetTypes()
            .Where(type => type.Namespace?.Contains(".Domain", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.NotEmpty(domainTypes);

        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().ResideInNamespaceMatching(@"GZCTF\.Modules\..*\.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore",
                "StackExchange.Redis", "Docker.DotNet")
            .GetResult();

        Assert.True(result.IsSuccessful,
            string.Join(", ", result.FailingTypes.Select(type => type.FullName)));
    }
}
```

- [ ] **Step 3: 运行门禁确认目标目录尚未建立完整实现**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ArchitectureDependencyTests
```

Expected: 至少一个测试 FAIL，或因目标模块没有真实类型而触发显式 `Assert.NotEmpty` 失败。

- [ ] **Step 4: 建立组合根，不创建空业务层**

`ModuleRegistration.AddPlatformModules` 只注册 Phase 1 已实现的 Identity、Audit operation 和 Content image slice。其他模块由后续 Phase 在迁移真实代码时加入。

```csharp
public static class ModuleRegistration
{
    public static IServiceCollection AddPlatformModules(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddIdentityModule(configuration);
        services.AddApiOperations(configuration);
        services.AddContentImageModule(configuration);
        return services;
    }
}
```

- [ ] **Step 5: 提交架构门禁**

```powershell
git add src/Directory.Packages.props src/GZCTF.Test/GZCTF.Test.csproj src/GZCTF.Test/UnitTests/Architecture src/GZCTF/Modules src/GZCTF/Composition docs/commercialization/module-boundary-map.md
git commit -m "test: enforce modular monolith boundaries"
```

## Task 2: 原子切换 scoped API token、认证授权和管理入口

**Files:**
- Modify: `src/Directory.Packages.props`
- Modify: `src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj`
- Delete: `src/GZCTF/Models/Data/ApiToken.cs`
- Create: `src/GZCTF/Modules/Identity/Domain/ApiToken.cs`
- Create: `src/GZCTF/Modules/Identity/Domain/ApiTokenScopeGrant.cs`
- Create: `src/GZCTF/Modules/Identity/Domain/ApiTokenResourceGrant.cs`
- Create: `src/GZCTF/Modules/Identity/Application/ApiTokenIssuer.cs`
- Create: `src/GZCTF/Modules/Identity/Application/ApiTokenValidator.cs`
- Create: `src/GZCTF/Modules/Identity/Infrastructure/ApiTokenSecretHasher.cs`
- Delete: `src/GZCTF/Repositories/Interface/IApiTokenRepository.cs`
- Delete: `src/GZCTF/Repositories/ApiTokenRepository.cs`
- Create: `src/GZCTF/Modules/Identity/Application/IApiTokenStore.cs`
- Create: `src/GZCTF/Modules/Identity/Infrastructure/EfApiTokenStore.cs`
- Create: `src/GZCTF/Modules/Identity/Infrastructure/Persistence/ApiTokenEntityConfiguration.cs`
- Delete: `src/GZCTF/Services/Token/TokenService.cs`
- Delete: `src/GZCTF/Services/Token/ITokenService.cs`
- Delete: `src/GZCTF/Controllers/ApiTokenController.cs`
- Create: `src/GZCTF/Modules/Identity/Api/ApiTokensController.cs`
- Create: `src/GZCTF/Modules/Identity/Infrastructure/ApiTokenAuthenticationHandler.cs`
- Create: `src/GZCTF/Modules/Identity/Infrastructure/ApiTokenRateLimitMiddleware.cs`
- Create: `src/GZCTF/Modules/Identity/Application/ApiScopeRequirement.cs`
- Create: `src/GZCTF/Modules/Identity/Application/ApiScopeAuthorizationHandler.cs`
- Create: `src/GZCTF/Modules/Identity/Application/ActorContext.cs`
- Modify: `src/GZCTF/Models/Internal/Configs.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Models/Request/Admin/ApiTokenModel.cs`
- Modify: `src/GZCTF/Services/Config/IConfigService.cs`
- Modify: `src/GZCTF/Services/Config/ConfigService.cs`
- Modify: `src/GZCTF/Extensions/Startup/IdentityExtension.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Modify: `src/GZCTF/Middlewares/PrivilegeAuthentication.cs`
- Modify: `src/GZCTF/Utils/ContextHelper.cs`
- Modify: `src/GZCTF/Utils/JsonSerializerContext.cs`
- Modify: `src/GZCTF/Controllers/InternalController.cs`
- Modify: `src/GZCTF/Hubs/MonitorHub.cs`
- Create: `src/GZCTF/Migrations/20260710110000_AddScopedApiTokens.cs`
- Create: `src/GZCTF/Migrations/20260710110000_AddScopedApiTokens.Designer.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Create: `src/GZCTF.Test/UnitTests/Security/ApiTokenIssuerTests.cs`
- Create: `src/GZCTF.Test/UnitTests/Controllers/ApiTokenControllerTests.cs`
- Modify: `src/GZCTF.Integration.Test/Base/GZCTFApplicationFactory.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Api/Fixtures/ScopedApiProbeController.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Api/ScopedApiTokenAuthenticationTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/ScopedApiTokenMigrationTests.cs`
- Create: `src/GZCTF/ClientApp/src/pages/account/Tokens.tsx`
- Create: `src/GZCTF/ClientApp/src/pages/admin/tokens.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/account/Profile.tsx`
- Modify: `src/GZCTF/ClientApp/src/Api.ts`

- [ ] **Step 1: 写 token 签发和校验失败测试**

```csharp
[Fact]
public async Task IssueAsync_ReturnsSecretOnceAndStoresOnlyDigest()
{
    var result = await issuer.IssueAsync(actor, new IssueApiTokenCommand(
        "image publisher", ["images:write", "operations:read"], [], 60, expiresAt), token);

    Assert.StartsWith($"gzctf_pat_{result.Token.Id:N}.", result.PlainTextToken);
    Assert.NotEmpty(result.Token.SecretHash);
    Assert.DoesNotContain(result.PlainTextToken, JsonSerializer.Serialize(result.Token));
}

[Fact]
public async Task ValidateAsync_RejectsRevokedExpiredOrChangedSecret()
{
    var issued = await IssueTokenAsync();
    Assert.True((await validator.ValidateAsync(issued.PlainTextToken, token)).Succeeded);
    Assert.False((await validator.ValidateAsync(issued.PlainTextToken + "x", token)).Succeeded);

    await store.RevokeAsync(issued.Token.Id, actor.UserId, token);
    Assert.False((await validator.ValidateAsync(issued.PlainTextToken, token)).Succeeded);
}
```

migration integration test 先迁移到 Phase 1 前一版本并用 SQL 播种一条有 creator 和一条无 creator 的旧 token；应用 migration 后断言无 owner 行已删除、有 owner 行已撤销、SecretHash 为 32 bytes 且旧 token 无法认证。

- [ ] **Step 2: 运行测试确认当前 token 格式失败**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ApiTokenIssuerTests
```

Expected: FAIL，当前 `TokenService` 生成 payload.signature 且没有 scope/digest。

- [ ] **Step 3: 扩展实体**

`ApiToken` 目标字段：

```csharp
public class ApiToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public Guid CreatorId { get; set; }
    public byte[] SecretHash { get; set; } = [];
    public int RequestsPerMinute { get; set; } = 60;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public List<ApiTokenScopeGrant> Scopes { get; set; } = [];
    public List<ApiTokenResourceGrant> Resources { get; set; } = [];
}
```

scope 和 resource 使用数据库唯一键：

```csharp
public class ApiTokenScopeGrant
{
    public Guid TokenId { get; set; }
    [MaxLength(128)] public string Scope { get; set; } = string.Empty;
}

public class ApiTokenResourceGrant
{
    public Guid TokenId { get; set; }
    [MaxLength(64)] public string ResourceType { get; set; } = string.Empty;
    [MaxLength(128)] public string ResourceId { get; set; } = string.Empty;
}
```

Domain 类型不得引用 EF Core。复合主键、唯一索引、列长度和关系全部在 `ApiTokenEntityConfiguration` 中通过 `IEntityTypeConfiguration<T>` 配置；`AppDbContext` 只调用模块 configuration assembly scan。

- [ ] **Step 4: 实现 token 格式和 constant-time 校验**

使用 `RandomNumberGenerator.GetBytes(32)` 生成 secret，数据库只保存 SHA-256 digest。secret 具有 256 bit 随机熵，不需要额外全局 pepper；解析 public ID 后单行查询 token，再做 fixed-time digest 比较。

- [ ] **Step 5: 写授权矩阵 integration tests**

```csharp
[Theory]
[InlineData("images:read", "GET", "/test/scopes/images-read", 200)]
[InlineData("images:read", "POST", "/test/scopes/images-write", 403)]
[InlineData("images:write", "POST", "/test/scopes/images-write", 200)]
public async Task ExternalApi_EnforcesScope(string scope, string method, string path, int expected)
{
    var token = await IssueTokenAsync(scope);
    using var request = CreateRequest(method, path, token);
    var response = await client.SendAsync(request);
    Assert.Equal(expected, (int)response.StatusCode);
}

[Fact]
public async Task ValidApiToken_CannotCallAdminController()
{
    var token = await IssueTokenAsync("images:write");
    var response = await SendAsync(HttpMethod.Get, "/api/admin/users", token);
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

`GZCTFApplicationFactory` 通过 MVC ApplicationPart 只在 integration test host 注册 `ScopedApiProbeController`。probe 分别使用 `scope:images:read` 和 `scope:images:write` policy；生产程序集不能增加 probe endpoint。真实镜像 endpoint 的授权矩阵在 Task 5 再验证。

为 integration project 增加 `Testcontainers.Redis` 4.11.0。同一测试类使用真实 Redis fixture 验证第 N+1 次请求返回 429 和 `Retry-After`，停止 Redis 后返回 503；不得用 mock 验证分布式计数语义。

- [ ] **Step 6: 注册正式 scheme、scope policy 和 actor context**

```csharp
services.AddAuthentication()
    .AddScheme<ApiTokenSchemeOptions, ApiTokenAuthenticationHandler>(
        ApiTokenDefaults.Scheme, _ => { });

services.AddAuthorization(options =>
{
    options.AddPolicy("scope:images:write", policy =>
    {
        policy.AddAuthenticationSchemes(ApiTokenDefaults.Scheme);
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ApiScopeRequirement("images:write"));
    });
});
```

删除 `RequireAdminOrTokenAttribute`、`ContextHelper.HasValidToken` 和 `RequirePrivilegeAttribute.allowToken`。`InternalController` 只允许管理员 Cookie 或专用 Nginx sync token；`MonitorHub` 只允许教师 Cookie。机器接口不能复用用户 API token。

`ApiTokenRateLimitMiddleware` 只处理 `/api/open/v1` 且 actor type 为 api_token 的请求，使用 `INCR` + 首次命中 `EXPIRE` 的原子 Lua script。超额返回 429 和 `Retry-After`；Redis 不可用返回 503，不能退化到每进程计数。Cookie 内部 API 不经过该配额。

- [ ] **Step 7: 一次切换 token 管理 API 和 UI**

管理员可查询全部 token；教师只能列出、创建和撤销自己的 token；学生不能创建外部写 token。删除 restore endpoint，撤销不可逆。创建表单使用 scope 多选、resource grant、过期时间和配额输入；创建成功只展示一次 plaintext secret，关闭后不写入 localStorage、sessionStorage 或 SWR cache。

```csharp
[Fact]
public async Task Teacher_CannotRevokeAnotherUsersToken()
{
    var result = await controller.RevokeToken(otherUsersTokenId, ct);
    Assert.IsType<NotFoundResult>(result);
}
```

- [ ] **Step 8: 处理旧 token**

migration 先输出旧 token 总数和缺失 owner 数；删除 `CreatorId IS NULL` 的无归属旧 token，其余旧 token 设置 `RevokedAt = migration timestamp` 和不可匹配的随机 digest，然后把 CreatorId 改为非空。旧 payload.signature 不兼容且不能恢复。代码检索确认 `GetApiTokenContext` 仅由旧 TokenService 使用，因此同步删除 `ManagedConfig.ApiToken`、`IConfigService.GetApiTokenContext`、`ConfigService.GetApiTokenContext` 和 key regeneration 逻辑。

- [ ] **Step 9: 运行 token、授权和前端 tests**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ApiTokenIssuerTests
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ApiTokenControllerTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~ScopedApiTokenAuthenticationTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~ScopedApiTokenMigrationTests
$server = Start-Process dotnet -ArgumentList 'run --project src/GZCTF/GZCTF.csproj --configuration Debug --no-launch-profile' -WindowStyle Hidden -PassThru
try {
    do { Start-Sleep -Seconds 1 } until ((Invoke-WebRequest http://127.0.0.1:8080/openapi/v1.json -UseBasicParsing).StatusCode -eq 200)
    pnpm --dir src/GZCTF/ClientApp genapi
    pnpm --dir src/GZCTF/ClientApp check
} finally {
    Stop-Process -Id $server.Id -ErrorAction SilentlyContinue
}
```

Expected: 测试、API 类型生成和 TypeScript check 全部成功；生成类型不包含 restore endpoint。

- [ ] **Step 10: 提交 Identity 纵向切换**

```powershell
git add src/Directory.Packages.props src/GZCTF src/GZCTF.Test src/GZCTF.Integration.Test
git commit -m "feat: replace api token bypass with scoped identity"
```

## Task 3: 建立统一 ProblemDetails、幂等和 operation

**Files:**
- Create: `src/GZCTF/Infrastructure/Api/ExternalApiProblemDetails.cs`
- Create: `src/GZCTF/Infrastructure/Api/ExternalApiExceptionHandler.cs`
- Create: `src/GZCTF/Modules/Audit/Domain/ApiOperation.cs`
- Create: `src/GZCTF/Modules/Audit/Application/ApiOperationService.cs`
- Create: `src/GZCTF/Modules/Audit/Application/IdempotencyService.cs`
- Create: `src/GZCTF/Modules/Audit/Application/IApiOperationHandler.cs`
- Create: `src/GZCTF/Modules/Audit/Infrastructure/ApiOperationWorker.cs`
- Create: `src/GZCTF/Modules/Audit/Infrastructure/Persistence/ApiOperationEntityConfiguration.cs`
- Create: `src/GZCTF/Modules/Audit/Contracts/ApiOperationModel.cs`
- Create: `src/GZCTF/Modules/Audit/Api/OperationsController.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Migrations/20260710120000_AddApiOperations.cs`
- Create: `src/GZCTF/Migrations/20260710120000_AddApiOperations.Designer.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `src/GZCTF/Extensions/Startup/AppExtensions.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Create: `src/GZCTF.Test/UnitTests/Services/IdempotencyServiceTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Api/ExternalApiProblemDetailsTests.cs`

- [ ] **Step 1: 写幂等状态机失败测试**

```csharp
[Fact]
public async Task BeginAsync_ReusesSameRequestAndRejectsChangedPayload()
{
    var first = await service.BeginAsync(tokenId, "images.register", "key-001", "hash-a", ct);
    var retry = await service.BeginAsync(tokenId, "images.register", "key-001", "hash-a", ct);
    Assert.Equal(first.Operation.Id, retry.Operation.Id);
    Assert.True(retry.Reused);

    var conflict = await Assert.ThrowsAsync<IdempotencyConflictException>(() =>
        service.BeginAsync(tokenId, "images.register", "key-001", "hash-b", ct));
    Assert.Equal("idempotency_conflict", conflict.Code);
}
```

- [ ] **Step 2: 实现 ApiOperation 数据模型**

字段固定为 `Id, Kind, Status, Stage, ActorUserId, ApiTokenId, RouteKey, IdempotencyKey, RequestHash, ResourceType, ResourceId, DeploymentQueueTicketId, CurrentProgress, TotalProgress, AttemptCount, LeaseOwner, LeaseExpiresAt, NextAttemptAt, ErrorCode, ErrorDetail, CreatedAt, StartedAt, UpdatedAt, CompletedAt`。

数据库唯一索引：

`ApiOperation` Domain 类型只声明上述字段；`ApiOperationEntityConfiguration` 使用 `IEntityTypeConfiguration<ApiOperation>` 配置 `(ApiTokenId, RouteKey, IdempotencyKey)` 唯一索引、状态索引、长度和关系。禁止在 Domain 类型上使用 EF attribute。

- [ ] **Step 3: 实现恢复 worker**

worker 使用 PostgreSQL `FOR UPDATE SKIP LOCKED` 批量 claim Pending、到期重试或 lease 超时的 Running operation，在同一 transaction 写入 Running、LeaseOwner、LeaseExpiresAt 和 AttemptCount 后提交，再调用按 `Kind` 注册的 handler。长任务定期续 lease；只有持有相同 LeaseOwner 的 worker 可写终态。handler 重复执行必须检查目标资源和 request hash，不能重复创建资源。

```csharp
public interface IApiOperationHandler
{
    string Kind { get; }
    Task ExecuteAsync(Guid operationId, CancellationToken token);
}
```

worker 启动时构建唯一 kind registry，重复 kind 直接让应用启动失败。handler 通过 operation ID 读取自己模块的 durable job；通用 operation 表不保存任意业务 JSON 或 secret。

- [ ] **Step 4: 实现统一错误**

外部 API 异常映射到 `application/problem+json`，至少包含 `code` 和当前 Activity trace ID。未知异常返回 `internal_error`，日志保存异常栈但响应不返回内部 detail。

- [ ] **Step 5: 运行单元与 integration tests**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~IdempotencyServiceTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~ExternalApiProblemDetailsTests
```

Expected: PASS。

- [ ] **Step 6: 提交 API 基础**

```powershell
git add src/GZCTF src/GZCTF.Test src/GZCTF.Integration.Test
git commit -m "feat: add recoverable external api operations"
```

## Task 4: 修正 ImageTemplate 所有权和课程引用

**Files:**
- Modify: `src/GZCTF/Models/Data/ImageTemplate.cs`
- Create: `src/GZCTF/Modules/Content/Contracts/ImageTemplateContracts.cs`
- Create: `src/GZCTF/Modules/Content/Contracts/IImageTemplateReferenceProvider.cs`
- Create: `src/GZCTF/Modules/Training/Domain/TrainingCourseImageTemplateBinding.cs`
- Create: `src/GZCTF/Modules/Content/Infrastructure/Persistence/ImageTemplateEntityConfiguration.cs`
- Create: `src/GZCTF/Modules/Training/Infrastructure/Persistence/TrainingImageTemplateBindingEntityConfiguration.cs`
- Create: `src/GZCTF/Modules/Content/Application/IImageTemplateCatalog.cs`
- Create: `src/GZCTF/Modules/Content/Application/ImageTemplateReferenceService.cs`
- Create: `src/GZCTF/Modules/Ctf/Infrastructure/CtfImageTemplateReferenceProvider.cs`
- Create: `src/GZCTF/Modules/Exercise/Infrastructure/ExerciseImageTemplateReferenceProvider.cs`
- Create: `src/GZCTF/Modules/Training/Infrastructure/TrainingImageTemplateReferenceProvider.cs`
- Create: `src/GZCTF/Modules/Penetration/Infrastructure/PenetrationImageTemplateReferenceProvider.cs`
- Create: `src/GZCTF/Modules/Training/Application/TrainingCourseDeletionService.cs`
- Modify: `src/GZCTF/Controllers/TrainingCourseAdminController.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Migrations/20260710123000_DecoupleImageTemplateOwnership.cs`
- Create: `src/GZCTF/Migrations/20260710123000_DecoupleImageTemplateOwnership.Designer.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Create: `src/GZCTF.Test/UnitTests/Models/ImageTemplateOwnershipTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/ImageTemplateOwnershipMigrationTests.cs`

- [ ] **Step 1: 写删除和课程隔离失败测试**

```csharp
[Fact]
public async Task DeleteCourse_RemovesBindingButPreservesGlobalImageTemplate()
{
    var template = await SeedTemplateAsync(ownerId);
    var course = await SeedCourseWithTemplateBindingAsync(template.Id);

    await courseService.DeleteAsync(course.Id, actor, ct);

    Assert.NotNull(await context.ImageTemplates.FindAsync([template.Id], ct));
    Assert.False(await context.TrainingCourseImageTemplateBindings
        .AnyAsync(item => item.CourseId == course.Id, ct));
}

[Fact]
public async Task DeleteTemplate_RejectsAnyActiveBusinessReference()
{
    var result = await references.CanDeleteAsync(templateId, actor, ct);
    Assert.False(result.Allowed);
    Assert.Contains(result.References, item => item.Module == "Training");
}

[Fact]
public async Task DeleteCourse_RemovesOwnedChallengeSnapshotAndPreservesItsImageTemplate()
{
    var template = await SeedTemplateAsync(ownerId);
    var course = await SeedCourseAsync();
    var challenge = await SeedCourseOwnedExerciseAsync(course.Id, template.Id);

    await courseService.DeleteAsync(course.Id, actor, ct);

    Assert.Null(await context.ExerciseChallenges.FindAsync([challenge.Id], ct));
    Assert.NotNull(await context.ImageTemplates.FindAsync([template.Id], ct));
}
```

- [ ] **Step 2: 迁移直接所有权字段**

为 ImageTemplate 增加 `CreatedById`，把现有 `TrainingCourseId` 值迁入显式 binding 后删除该字段。历史 `CreatedById = null` 表示系统级模板，仅管理员可删除；新上传必须记录 actor。保留 `ExerciseChallenge.TrainingCourseId` 的课程快照所有权语义，课程删除服务必须先删除快照及其运行事实，再删除课程，但不得删除快照引用的 ImageTemplate。

`ImageTemplateOwnershipMigrationTests` 在上一 migration 播种课程直属模板、共享模板和无课程模板，应用 migration 后断言 binding 数量与旧非空 TrainingCourseId 数量一致、所有 ImageTemplate 仍存在且当前 model 不再包含 TrainingCourseId。

- [ ] **Step 3: 通过消费方 provider 统一引用检查**

`ImageTemplateReferenceService` 只依赖 `IEnumerable<IImageTemplateReferenceProvider>`。Ctf、Exercise、Training 和当前 Penetration topology 各自实现 provider 并查询自己拥有的表，返回可读引用列表；Content 不得直接引用这些模块的 entity 或 DbSet。Phase 3 用 TeamLab provider 替换 Penetration topology provider，并在同一阶段删除旧 provider。

`TrainingCourseDeletionService` 先让 Training 模块删除课程拥有的 ExerciseChallenge snapshot 和 binding，再调用 Content catalog 删除检查；它不得直接删除 ImageTemplate。Controller 只做权限和 command 映射。

- [ ] **Step 4: 运行 ownership tests**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~ImageTemplateOwnershipTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~ImageTemplateOwnershipMigrationTests
```

Expected: PASS。

- [ ] **Step 5: 提交内容资产边界**

```powershell
git add src/GZCTF src/GZCTF.Test/UnitTests/Models/ImageTemplateOwnershipTests.cs
git commit -m "refactor: make image templates global referenced assets"
```

## Task 5: 打通外部镜像 API 参考链路

**Files:**
- Create: `src/GZCTF/Modules/Content/Contracts/ImageImportContracts.cs`
- Create: `src/GZCTF/Modules/Content/Domain/ImageImportJob.cs`
- Create: `src/GZCTF/Modules/Content/Application/ImageImportApplicationService.cs`
- Create: `src/GZCTF/Modules/Content/Infrastructure/ImageImportOperationHandler.cs`
- Create: `src/GZCTF/Modules/Content/Infrastructure/Persistence/ImageImportJobEntityConfiguration.cs`
- Create: `src/GZCTF/Modules/Content/Api/OpenImagesController.cs`
- Modify: `src/GZCTF/Controllers/ImageTemplateController.cs`
- Modify: `src/GZCTF/Services/Fleet/ImageDistributionService.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Migrations/20260710124000_AddImageImportJobs.cs`
- Create: `src/GZCTF/Migrations/20260710124000_AddImageImportJobs.Designer.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Api/OpenImageApiTests.cs`

- [ ] **Step 1: 写完整 API integration tests**

覆盖 `202 + operation`、幂等复用、scope 拒绝、所有权删除和服务恢复：

```csharp
[Fact]
public async Task RegisterDockerReference_IsIdempotentAndAudited()
{
    var token = await IssueTokenAsync("images:write", "operations:read");
    var first = await PostReferenceAsync(token, "image-key-001", request);
    var second = await PostReferenceAsync(token, "image-key-001", request);

    Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
    Assert.Equal((await first.ReadOperation()).Id, (await second.ReadOperation()).Id);
    Assert.Single(await context.ApiOperations.Where(item => item.Kind == "image.import").ToListAsync());
}
```

- [ ] **Step 2: 持久化可恢复的导入作业**

`ImageImportJob` 以 `OperationId` 为主键，固定保存 `SourceKind, SourceReference, StagedPath, OriginalFileName, ContentLength, ExpectedDigest, RequestedTemplateKind, RequestedName, CreatedAt`。不得保存第三方 Registry 明文凭据；文件上传先原子写入主站 staging 目录，提交 job 和 ApiOperation 后才返回 202。

`ImageImportOperationHandler` 只通过 OperationId 加载 job。主站重启后可继续校验 staging 文件、导入 Registry、创建或定位 ImageTemplate，并触发预分发；终态删除 staging 文件。缺失或摘要不匹配返回稳定 `image_staging_invalid`，不能重新接受客户端请求体。

- [ ] **Step 3: 将 Controller 逻辑迁入 application service**

`OpenImagesController` 只读取 actor、scope、Idempotency-Key 和 DTO。现有 `ImageTemplateController` 的浏览器接口调用同一个 `ImageImportApplicationService`，不能保留第二套 Docker import 实现。

- [ ] **Step 4: 移除裸 Task.Run**

删除 `ImageTemplateController.RegisterDocker` 和 `QueueDistribution` 中的 fire-and-forget `Task.Run`。operation handler 负责 Registry 导入、模板写入和 `ImageDistributionService` 调用，异常写入 operation 和 ImageTemplate 状态。

- [ ] **Step 5: 运行外部 API tests**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~OpenImageApiTests
```

Expected: PASS。

- [ ] **Step 6: 提交参考链路**

```powershell
git add src/GZCTF src/GZCTF.Integration.Test/Tests/Api/OpenImageApiTests.cs
git commit -m "feat: expose recoverable scoped image import api"
```

## Task 6: 固定 OpenAPI 和 API 兼容门禁

**Files:**
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Create: `docs/commercialization/openapi/open-v1.json`
- Create: `scripts/verify-openapi-contract.ps1`
- Modify: `src/GZCTF.Integration.Test/Tests/Api/OpenApiTests.cs`
- Create: `.github/workflows/quality.yml`

- [ ] **Step 1: 将外部 API 输出为独立 NSwag document**

内部 API 保留当前 document；新增 `open-v1` document，只包含 `/api/open/v1` 路由和 `GzctfApiToken` security scheme。

- [ ] **Step 2: 写 contract snapshot test**

```csharp
[Fact]
public async Task OpenV1Document_MatchesCommittedContract()
{
    var current = await client.GetStringAsync("/openapi/open-v1.json");
    var expected = await File.ReadAllTextAsync(ContractPath);
    Assert.Equal(NormalizeOpenApi(expected), NormalizeOpenApi(current));
}
```

- [ ] **Step 3: 实现破坏性变更脚本**

脚本比较 path、method、required parameter、required property、response status 和 schema type。删除或收紧任何 v1 项目时退出 1；新增可选项时更新 snapshot。

- [ ] **Step 4: 运行契约测试**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~OpenApiTests
pwsh scripts/verify-openapi-contract.ps1
```

Expected: PASS，脚本退出码 0。

- [ ] **Step 5: 提交 OpenAPI 门禁**

```powershell
git add .github/workflows/quality.yml docs/commercialization/openapi scripts src/GZCTF src/GZCTF.Integration.Test
git commit -m "test: lock external api v1 contract"
```

## Task 7: Phase 1 总体验收

**Files:**
- Modify: `docs/platform-commercialization-audit-progress.md`

- [ ] **Step 1: 运行架构、安全和 API tests**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Architecture|FullyQualifiedName~ApiToken|FullyQualifiedName~Idempotency|FullyQualifiedName~ImageTemplateOwnership"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter "FullyQualifiedName~ScopedApiToken|FullyQualifiedName~ExternalApi|FullyQualifiedName~OpenImageApi|FullyQualifiedName~OpenApi"
```

Expected: 全部 PASS。

- [ ] **Step 2: 运行全量构建**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj
pnpm --dir src/GZCTF/ClientApp build
git diff --check
```

Expected: 全部退出码为 0。

- [ ] **Step 3: 执行真实 API 验收**

1. 教师创建仅含 `images:write operations:read` 的 token。
2. 注册 `10.24.0.28:5000` 中的 Docker 镜像，确认返回 202。
3. 重复同一 Idempotency-Key，确认 operation ID 不变。
4. 重启主站进程，确认 Pending/Running operation 恢复并完成。
5. 使用 token 请求管理员用户 API，确认 401/403。
6. 删除仍被课程引用的模板，确认 `409 asset_in_use`。

- [ ] **Step 4: 更新进度并提交**

```powershell
git add docs/platform-commercialization-audit-progress.md
git commit -m "docs: record phase one acceptance"
```

## Phase 1 退出门槛

- API token 通过 ASP.NET Core authentication/authorization 生效，不存在 `HasValidToken` 管理员旁路。
- token scope、resource grant、配额、撤销和审计均有 PostgreSQL/Redis 事实与自动测试。
- 外部 API 使用 `/api/open/v1`、ProblemDetails、Idempotency-Key 和持久化 operation。
- 镜像导入参考链路可在服务重启后恢复，不包含 fire-and-forget `Task.Run`。
- ImageTemplate 是全局资产，课程使用 binding，删除规则通过测试。
- module boundary 和 OpenAPI contract 在 CI 中可执行。
- Phase 3 可以直接复用 actor、scope、operation、错误和架构边界，不再设计第二套 API 基础。

## 切换与回滚

1. 先部署 additive schema 和新认证代码，但在配置中关闭 `/api/open/v1` 写接口；执行 token、operation 和 ImageTemplate ownership 数据校验。
2. 创建新的 scoped token 验收账号，验证认证、配额、幂等和镜像 reference operation 后，再开启外部写接口。
3. 开启时统一撤销旧 payload.signature token，不允许两种 token validation 长期并行。
4. 新外部 API 尚未承载业务写入时可以直接回滚应用；产生新 operation 或模板后，回滚必须先停止 worker、导出 operation 状态并恢复 Phase 1 前数据库 backup。
5. Cookie 登录和现有普通 CTF API 不改变 authentication scheme；回滚验收必须覆盖登录、比赛访问和普通容器启动。
