# Phase 3 TeamLab Foundation Decoupling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 TeamLab 从 Penetration 比赛内部能力改造成拥有独立拓扑、发布版本、runtime 和外部 API 的组网基座，同时保留 Penetration 赛制作为标准调用方。

**Architecture:** TeamLab 使用可变 topology draft、不可变 release、确定性 plan 和 runtime facts 四层模型。Penetration 只保留 objective、submission、scoreboard、reset policy 和 TeamLab binding；动态 Flag 通过 runtime overlay 注入。平台内部调用 application contracts，外部调用 `/api/open/v1/teamlab`，二者共用 Phase 1 operation、现有 DeploymentQueueService 和同一执行服务。Phase 3 完成模型与 API 的彻底切换；Phase 4-8 强化共享底座；Phase 9 在不改核心契约的前提下完成 Windows、多节点故障、全流量和容量闭环。

**Tech Stack:** .NET 10、EF Core 10、PostgreSQL、Redis、NSwag、现有 Fleet/DeploymentQueue、Docker、KVM/libvirt、WireGuard、React 19、TypeScript 6、xUnit、Testcontainers.PostgreSql。

---

## 执行进度（2026-07-11）

- 当前分支：`codex/phase-3-teamlab-foundation`，基线为 `c83d3135`。
- Phase 2 由其他协作者并行开发；Phase 3 不重写视觉语言，只处理 TeamLab/Penetration 契约边界和必要调用改造。
- 验收节奏调整为四个大单元：独立领域基座、runtime 编排、Penetration/API 接入、contract migration 与最终验收。每个大单元末集中测试，不执行逐小步骤红灯/绿灯循环。
- 已核实当前事实：TeamLab application module 尚不存在；`TeamLabRuntime` 仍以 `GameId + TeamId` 标识；`TeamLabDeploymentService` 仍直接生成 Penetration Flag；`PenetrationService` 与 `TeamLabDeploymentService` 分别约 160 KB 和 119 KB；前端 `PenetrationApi.ts` 仍混合玩法与 TeamLab runtime 契约。
- 大单元 1：已完成并通过聚合验证。已新增独立 topology/network/asset/interface/connection/release/network lease 模型、Penetration objective/binding 模型、canonical release codec、RFC1918 validator、乐观 revision、能力感知 plan 和 expand migration；旧 runtime 已补 PublicId、release、generation、entry shard、access grant 和 secret envelope 扩展字段。验证结果：`TeamLabFoundation*` 6/6 通过，生产与测试项目 0 编译错误。
- 大单元 2：已完成新运行链路并通过聚合验证。TeamLab 队列身份只依赖 runtime；新增 planner、orchestrator、shard deployment、route application、cleanup、projection、Agent executor adapter、generation reset、加密 operation payload 和一次性 WireGuard access grant。验证结果：`TeamLabRuntimeFoundation*` 4/4 通过，生产与测试项目 0 编译错误。
- 大单元 3：已完成 Penetration 标准调用方、内外 API、流量采集、动态 Flag runtime overlay、一次性 VPN grant、管理端四任务区、玩家端和大屏数据源切换。`TeamLabApi.ts` 与 `PenetrationApi.ts` 已拆分，前端 strict check 通过；生产后端曾在该检查点以 0 warning / 0 error 通过。
- 大单元 4 contract migration：进行中。已删除旧 Penetration topology/environment/runtime node/runtime route 实体、DbSet、服务、控制器、DTO 和对应旧实现测试；`TeamLabRuntime` 已去除 GameId、TeamId、WorkerNodeId、PublishedVersion、NetworkPrefix，节点资源与部署队列通过 `PenetrationTeamRuntimeBinding` 投影比赛上下文。
- 数据库收缩迁移已生成：`20260711170329_RemovePenetrationTopologyRuntimeCompatibility`。迁移在删除旧表前校验 active environment binding、submission objective、runtime release/entry/network/asset facts，并把 reset record 从旧 environment ID 显式重映射到 runtime ID；不使用空 UUID 或 objective 0 掩盖脏数据。
- 大单元 4 contract migration 与生成物已完成：PostgreSQL 16 contract migration 2/2、后端单元测试 475/475、OpenAPI snapshot/comparator、前端 strict build 和 `git diff --check` 曾在最终审查前全部通过；`open-v1.json`、`ClientApp/src/Api.ts` 与 acceptance runbook 已生成。
- 最终质量审查已由单个 Agent 完成并确认 9 项有效缺陷。2026-07-12 已完成代码修复：Destroyed runtime 可进入新 generation；Penetration runtime owner 与操作者解耦；reset 显式采用 active release；访问授权可重新签发且历史空 token grant 在迁移中撤销；管理 runtime/traffic/capture 增加对象级授权；容量按 current-generation shard facts 预留、释放和 reconcile；connection graph 同时约束单节点与多节点路由；flow 改为 Agent 增量游标；capture 接入幂等键和真实进程完成状态。
- 新增可靠性迁移 `20260712053756_PersistTeamLabFlowCursor` 与 `20260712054103_CompleteTeamLabRuntimeReliability`，持久化 network flow cursor、flow source cursor 与 capture idempotency facts。当前解决方案编译为 0 warning / 0 error；首次全量测试暴露 5 个同源容量恢复断点，已按 shard 归属事实修复。

---

## 0. 当前代码事实

- `TeamLabRuntime`、Shard、Network、Asset、TrafficFlow 和 CaptureJob 已存在，运行事实基础可复用。
- `TeamLabPublishedTopologyService` 把发布 JSON 反序列化成 `PenetrationConfigModel`，再构造临时 `PenetrationConfig`。
- `TeamLabAssetPlanService`、`TeamLabPlanService` 和 `TeamLabDeploymentService` 直接接收 Penetration entity。
- `TeamLabDeploymentService` 已接入 `DeploymentQueueService`，不能新增平行队列。
- `DeploymentQueueTicket.BuildActiveIdentity` 对 TeamLab 仍要求 GameId、OwnerTeamId 和 RuntimeId。
- `TeamLabRuntime` 仍以 `(GameId, TeamId)` 唯一，并保留单个 `WorkerNodeId`，与多 shard 事实冲突。
- `PenetrationService` 同时负责 topology CRUD、发布、计划、部署协调、旧环境兼容、目标、提交和计分。
- `PenetrationScoreItem` 由 topology node 拥有，导致 TeamLab 拓扑携带比赛玩法。
- `PenetrationTeamEnvironment/RuntimeNode/RuntimeRoute/DeploymentEvent` 与 TeamLab runtime facts 重复。
- 前端 `Api/PenetrationApi.ts` 同时声明 topology、runtime、traffic、VPN、objective 和 scoreboard 类型。

## 1. 目标数据模型

### 1.1 TeamLab 所有对象

```text
TeamLabTopology
  -> TeamLabTopologyNetwork
  -> TeamLabTopologyAsset
       -> TeamLabTopologyInterface
  -> TeamLabTopologyConnection
  -> TeamLabTopologyRelease (immutable canonical snapshot)
       -> TeamLabRuntime
            -> TeamLabNetworkLease
            -> TeamLabRuntimeShard
            -> TeamLabRuntimeNetwork
            -> TeamLabRuntimeAsset
            -> TeamLabAccessGrant
            -> TeamLabRuntimeSecretEnvelope
            -> TeamLabEvent
            -> TeamLabTrafficFlow
            -> TeamLabTrafficCaptureJob
```

### 1.2 Penetration 所有对象

```text
PenetrationGameLabBinding(GameId -> TopologyId)
PenetrationObjective(GameId, TopologyAssetKey, score/flag/prerequisites)
PenetrationTeamRuntimeBinding(GameId, TeamId -> RuntimeId)
PenetrationSubmission(ObjectiveId)
PenetrationResetRecord(RuntimeId, UserId)
```

TeamLab 表不得包含 GameId、TeamId、ParticipationId、score、Flag rule 或 prerequisite objective。Penetration 表不得保存 bridge、namespace、WorkerNode route 或 WireGuard 私钥。

## Task 1: 建立解耦失败测试和 API contract tests

**Files:**
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabFoundationBoundaryTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Api/OpenTeamLabContractTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/TeamLabFoundationMigrationTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/Architecture/ArchitectureDependencyTests.cs`
- Modify: `docs/platform-commercialization-audit-progress.md`

- [ ] **Step 1: 写 TeamLab 不依赖 Penetration 的失败测试**

```csharp
[Fact]
public void TeamLabServices_DoNotReferencePenetrationDomainOrDtos()
{
    var result = Types.InAssembly(typeof(Program).Assembly)
        .That().ResideInNamespace("GZCTF.Modules.TeamLab", true)
        .ShouldNot().HaveDependencyOnAny(
            "GZCTF.Modules.Penetration",
            "GZCTF.Models.Request.Game")
        .GetResult();

    Assert.True(result.IsSuccessful,
        string.Join(", ", result.FailingTypes.Select(type => type.FullName)));

    var forbiddenTypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "PenetrationConfig", "PenetrationNode", "PenetrationNetwork",
        "PenetrationInterface", "PenetrationEdge", "PenetrationConfigModel"
    };
    var referencedTypes = typeof(Program).Assembly.GetTypes()
        .Where(type => type.Namespace?.StartsWith("GZCTF.Modules.TeamLab", StringComparison.Ordinal) == true)
        .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Static |
                                           BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .Concat(type.GetProperties().Select(property => property.PropertyType))
            .Concat(type.GetMethods().SelectMany(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType))))
        .Select(type => Nullable.GetUnderlyingType(type) ?? type)
        .Select(type => type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() ?? type : type)
        .Where(type => forbiddenTypeNames.Contains(type.Name))
        .Select(type => type.FullName)
        .Distinct()
        .ToArray();
    Assert.Empty(referencedTypes);
}

[Fact]
public void TeamLabRuntime_DoesNotExposeGameTeamOrSingleWorkerOwnership()
{
    var properties = typeof(TeamLabRuntime).GetProperties().Select(item => item.Name).ToHashSet();
    Assert.DoesNotContain("GameId", properties);
    Assert.DoesNotContain("TeamId", properties);
    Assert.DoesNotContain("WorkerNodeId", properties);
    Assert.Contains("PublicId", properties);
    Assert.Contains("TopologyReleaseId", properties);
    Assert.Contains("EntryShardId", properties);
}

[Fact]
public void TeamLabApplication_DependsOnNodeExecutorPortNotAgentClient()
{
    var offenders = typeof(Program).Assembly.GetTypes()
        .Where(type => type.Namespace?.StartsWith("GZCTF.Modules.TeamLab.Application", StringComparison.Ordinal) == true)
        .Where(type => type.GetConstructors()
            .SelectMany(ctor => ctor.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(AgentClient)))
        .Select(type => type.FullName)
        .ToArray();

    Assert.Empty(offenders);
}
```

- [ ] **Step 2: 写外部无比赛依赖测试**

```csharp
[Fact]
public async Task ExternalClient_CanPublishAndPlanWithoutGameOrTeam()
{
    var token = await IssueTokenAsync(
        "teamlab.topologies:write", "teamlab.topologies:read", "teamlab.runtimes:read");
    var topology = await CreateTopologyAsync(token, TwoNetworkDockerTopology());
    var release = await PublishAsync(token, topology.Id, topology.Revision);
    var plan = await PlanAsync(token, topology.Id, release.Id);

    Assert.NotEmpty(plan.Shards);
    Assert.DoesNotContain("gameId", plan.RawJson, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("teamId", plan.RawJson, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: 运行测试确认当前失败**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~TeamLabFoundationBoundaryTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~OpenTeamLabContractTests
```

Expected: FAIL，当前 TeamLab 使用 PenetrationConfig 且外部 endpoint 不存在。

- [ ] **Step 4: 提交边界测试**

```powershell
git add src/GZCTF.Test src/GZCTF.Integration.Test docs/platform-commercialization-audit-progress.md
git commit -m "test: define independent teamlab foundation contract"
```

## Task 2: Expand 独立 TeamLab topology/release 模型

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopology.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopologyRelease.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTopologyPrimitives.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabNetworkLease.cs`
- Modify: `src/GZCTF/Models/Data/TeamLabEntities.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabTopologyEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRuntimeEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/Penetration/Infrastructure/Persistence/PenetrationEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeSecretProtector.cs`
- Create: `src/GZCTF/Modules/Penetration/Domain/PenetrationObjective.cs`
- Create: `src/GZCTF/Modules/Penetration/Domain/PenetrationTeamLabBindings.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Migrations/20260710130000_AddIndependentTeamLabFoundation.cs`
- Create: `src/GZCTF/Migrations/20260710130000_AddIndependentTeamLabFoundation.Designer.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `src/GZCTF.Integration.Test/Tests/Database/TeamLabFoundationMigrationTests.cs`

- [ ] **Step 1: 写 expand migration 数据保持测试**

测试必须播种一场已发布 Penetration 比赛、两网段、Docker+VM 资产、连接、目标、submission 和运行中的 TeamLabRuntime；应用 expand migration 后旧链路仍可读，新 topology/binding/objective/release 数据数量一致。

migration fixture 先迁移到 `20260710130000_AddIndependentTeamLabFoundation` 的前一 migration，再使用参数化 SQL 和 test-only legacy record 播种；Task 8 删除生产旧实体后，该测试不得通过引用旧 entity 维持编译。

```csharp
[Fact]
public async Task ExpandMigration_CopiesTopologyObjectivesAndBindingsWithoutChangingRuntime()
{
    var seed = await SeedPublishedPenetrationRuntimeAsync();
    await ApplyMigrationAsync("20260710130000_AddIndependentTeamLabFoundation");

    Assert.Equal(seed.NetworkCount, await context.TeamLabTopologyNetworks.CountAsync());
    Assert.Equal(seed.AssetCount, await context.TeamLabTopologyAssets.CountAsync());
    Assert.Equal(seed.ObjectiveCount, await context.PenetrationObjectives.CountAsync());
    Assert.Single(await context.PenetrationGameLabBindings.ToListAsync());
    Assert.Single(await context.PenetrationTeamRuntimeBindings.ToListAsync());
}
```

- [ ] **Step 2: 定义 topology 聚合**

核心字段：

```csharp
public class TeamLabTopology
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public Guid? OwnerUserId { get; set; }
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    public int Revision { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<TeamLabTopologyNetwork> Networks { get; set; } = [];
    public List<TeamLabTopologyAsset> Assets { get; set; } = [];
    public List<TeamLabTopologyConnection> Connections { get; set; } = [];
    public List<TeamLabTopologyRelease> Releases { get; set; } = [];
}

public class TeamLabTopologyRelease
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public int TopologyId { get; set; }
    public int Version { get; set; }
    public int SourceRevision { get; set; }
    public int SchemaVersion { get; set; }
    public string CanonicalJson { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public Guid? PublishedById { get; set; }
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

Connection 只保存 `FromNetworkKey, ToNetworkKey, ViaAssetKey`。不得迁入 protocol、port range、allow/deny 和 enforcement mode。

Infrastructure 配置 `(TopologyId, Version)` 以及 `(TopologyId, SourceRevision, ContentHash)` 唯一索引。release 的 Guid `Id` 同时作为内部主键和公开 ID，runtime 的 `TopologyReleaseId` 直接引用该键；Topology 仍使用内部 int 主键和独立 PublicId。迁移 release 允许 PublishedById 为空以承接当前 nullable `PenetrationPublishedSnapshot.CreatedBy`，所有新 API 发布必须写入 actor user ID。

Topology network 保存 `AddressPoolCidr` 和 `RuntimePrefixLength`，interface 保存 `HostOffset`。实际 CIDR/IP 只存在于 `TeamLabNetworkLease`、`TeamLabRuntimeNetwork` 和 `TeamLabRuntimeAsset`。`TeamLabNetworkLease.AllocatedCidr` 使用 PostgreSQL `cidr` 类型，并建立 active lease GiST exclusion constraint，禁止不同 topology、release、runtime 或主站实例分配任何重叠网段。

Migration 约束固定为：

```sql
ALTER TABLE "TeamLabNetworkLeases"
ADD CONSTRAINT "EX_TeamLabNetworkLeases_ActiveCidr"
EXCLUDE USING gist ("AllocatedCidr" inet_ops WITH &&)
WHERE ("ReleasedAt" IS NULL);
```

TeamLab/Penetration Domain 类型不得引用 EF Core；主键、关系、索引、`cidr` 列类型和 exclusion constraint 由三个 Infrastructure persistence configuration 及 migration 配置。`TeamLabNetworkLease.AllocatedCidr` 的 CLR 类型使用当前 Npgsql provider 支持的 `System.Net.IPNetwork`，application contract 对外仍使用规范 CIDR string。

- [ ] **Step 3: 调整 runtime 身份**

为 current runtime 增加：

```csharp
public Guid PublicId { get; set; } = Guid.CreateVersion7();
public Guid? TopologyReleaseId { get; set; }
public Guid? CreatedById { get; set; }
public int Generation { get; set; } = 1;
public string? ExternalReference { get; set; }
public string CreateRequestHash { get; set; } = string.Empty;
public int? EntryShardId { get; set; }
```

expand 阶段只在当前 `TeamLabEntities.cs` 中增加字段和新实体，不先改 runtime 类型 namespace；否则现有 Deployment/Fleet/Controller 调用方会跨任务失去编译。`TopologyReleaseId` 暂时可空，使尚未迁移的创建路径仍可运行；新 TeamLab application service 从 Task 3 起必须赋值，Task 8 contract migration 校验后改为非空。runtime 类型的文件/namespace 拆分在 Task 4 与全部引用更新原子完成。

将当前 `TeamLabVpnPeerRuntime` 数据迁入 `TeamLabAccessGrant`，grant type 首版固定为 WireGuard；将 `TeamLabPublicUdpMapping` 保留为 runtime 内部基础设施事实。为 `TeamLabRuntimeShard`、`TeamLabAccessGrant` 和 `TeamLabTrafficCaptureJob` 增加 `PublicId`。NetworkLease、Shard、RuntimeNetwork、RuntimeAsset、AccessGrant、Event、TrafficFlow 和 CaptureJob 都增加 `Generation`；当前 `(RuntimeId, TopologyKey)`、`(RuntimeId, WorkerNodeId)` 唯一约束相应包含 Generation。RuntimeNetwork/RuntimeAsset 对外使用 topology key，不暴露内部整数主键；TrafficFlow 使用 cursor，不暴露 long ID。

`TeamLabRuntimeSecretEnvelope` 只保存 `RuntimeId, Generation, ProtectedPayload, PayloadHash, CreatedAt, ConsumedAt, ExpiresAt`。`TeamLabRuntimeSecretProtector` 复用当前持久化到 PostgreSQL 的 ASP.NET Core Data Protection key ring，使用独立 purpose `GZCTF.TeamLab.RuntimeOverlay.v1`；日志、event 和 operation 不得保存明文 overlay。所有 shard 完成注入后立即清除密文 payload，只保留 hash 和时间事实。

expand 阶段暂不删除 GameId/TeamId/WorkerNodeId/NetworkPrefix，contract migration 删除。`EntryShardId` 从 entry network 所属 shard 推导，不使用第一个节点作为隐式主节点；混合地址族的实际网段从 network leases 查询，不再使用单个 runtime NetworkPrefix。

- [ ] **Step 4: 迁移 topology 和玩法目标**

- `PenetrationConfig/Network/Node/Interface` 复制到 TeamLab topology 表；旧 CIDR 转换为 address pool，旧静态 IP 转换为相对 host offset。无法提供两个 runtime 容量的旧 pool 必须在发布前扩容，不能静默复用 CIDR。
- 只把可执行 route edge 转换成 connection；旧 port ACL 字段不进入目标表。
- `PenetrationScoreItem` 复制到 `PenetrationObjective`，使用 `Node.TopologyKey` 写入 `TopologyAssetKey`。
- submission 的 ObjectiveId 在 expand 阶段与旧 ScoreItemId 并存并回填。
- Game 建立 `PenetrationGameLabBinding`；已有 TeamLab runtime 建立 team runtime binding。
- 当前 published version 生成 canonical TeamLab release；runtime 回填 release ID。
- 当前 VPN peer 迁入 access grant，保留过期、撤销和受保护私钥事实；不得重新生成已发放配置。

- [ ] **Step 5: 运行 expand migration test**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~TeamLabFoundationMigrationTests
```

Expected: expand case PASS，旧表尚存在。

- [ ] **Step 6: 提交 expand 模型**

```powershell
git add src/GZCTF src/GZCTF.Integration.Test/Tests/Database/TeamLabFoundationMigrationTests.cs
git commit -m "refactor: add independent teamlab topology and release model"
```

## Task 3: 实现 topology、validation、release 和 plan application contracts

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabTopologyContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabPlanContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/ITeamLabTopologyApplicationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyApplicationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabTopologyValidator.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabReleaseCodec.cs`
- Move: `src/GZCTF/Services/TeamLab/TeamLabAssetPlanService.cs` -> `src/GZCTF/Modules/TeamLab/Application/TeamLabAssetPlanner.cs`
- Move: `src/GZCTF/Services/TeamLab/TeamLabShardPlanner.cs` -> `src/GZCTF/Modules/TeamLab/Application/TeamLabShardPlanner.cs`
- Delete: `src/GZCTF/Services/TeamLab/TeamLabPublishedTopologyService.cs`
- Move: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabAssetPlanServiceTests.cs` -> `src/GZCTF.Test/UnitTests/TeamLab/TeamLabAssetPlannerTests.cs`
- Move: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPublishedTopologyServiceTests.cs` -> `src/GZCTF.Test/UnitTests/TeamLab/TeamLabReleaseServiceTests.cs`

- [ ] **Step 1: 将现有 planner tests 改为 TeamLab release 输入**

```csharp
[Fact]
public void BuildPlan_ConsumesReleaseWithoutPenetrationEntities()
{
    var release = TeamLabReleaseFixture.TwoNetworkPools(
        entryPool: "10.40.0.0/16", corePool: "192.168.0.0/16", runtimePrefixLength: 24);
    var result = planner.Preview(release, templates, nodeSnapshot);

    Assert.True(result.Success);
    Assert.Equal(2, result.Networks.Count);
    Assert.DoesNotContain(result.GetType().AssemblyQualifiedName!, "Penetration");
}
```

- [ ] **Step 2: 实现 canonical release codec**

规范化顺序固定为 network key、asset key、interface key、connection key；JSON options 固定 camelCase、无缩进、忽略 null。hash 只覆盖 schema version 和 canonical topology，不覆盖 publisher/time。

- [ ] **Step 3: 实现 topology revision 乐观并发**

PUT command 必须携带 revision，并在单条 UPDATE 的 WHERE 中匹配；受影响行数为 0 时抛出 `topology_revision_conflict`。保存和发布都经过同一个 validator。

- [ ] **Step 4: 删除旧 TeamLabPublishedTopologyService**

`TeamLabReleaseCodec` 直接返回 TeamLab release snapshot。删除构造临时 Penetration entity 的代码和对应兼容测试。

- [ ] **Step 5: 运行 topology/planner tests**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabAssetPlanner|FullyQualifiedName~TeamLabReleaseService|FullyQualifiedName~TeamLabShardPlanner"
```

Expected: PASS，测试代码不再实例化 PenetrationConfig/Node/Network/Edge。

- [ ] **Step 6: 提交 topology application**

```powershell
git add src/GZCTF/Modules/TeamLab src/GZCTF/Services/TeamLab src/GZCTF.Test/UnitTests/TeamLab
git commit -m "refactor: make teamlab planning consume immutable releases"
```

## Task 4: 拆分 runtime orchestration 并泛化部署队列身份

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabRuntimeContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/ITeamLabRuntimeApplicationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimePlanner.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOperationHandler.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRouteApplicationService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOverlayService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/ITeamLabNodeExecutor.cs`
- Delete: `src/GZCTF/Models/Data/TeamLabEntities.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntime.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabAccessGrant.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntimeSecretEnvelope.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTraffic.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRuntimeEntityConfigurations.cs`
- Move: `src/GZCTF/Services/TeamLab/TeamLabStateMachine.cs` -> `src/GZCTF/Modules/TeamLab/Domain/TeamLabStateMachine.cs`
- Move: `src/GZCTF/Services/TeamLab/TeamLabWireGuardService.cs` -> `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabWireGuardService.cs`
- Move: `src/GZCTF/Services/TeamLab/TeamLabTrafficFlowService.cs` -> `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficFlowService.cs`
- Move: `src/GZCTF/Services/TeamLab/TeamLabTrafficCaptureService.cs` -> `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficCaptureService.cs`
- Move: `src/GZCTF/Services/TeamLab/PublicUdpGatewayProvider.cs` -> `src/GZCTF/Modules/TeamLab/Infrastructure/PublicUdpGatewayProvider.cs`
- Move: `src/GZCTF/Services/TeamLab/NodeTunnelService.cs` -> `src/GZCTF/Modules/TeamLab/Infrastructure/NodeTunnelService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- Delete: `src/GZCTF/Services/TeamLab/TeamLabPlanService.cs`
- Delete: `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueModels.cs`
- Modify: `src/GZCTF/Models/Data/DeploymentQueueTicket.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Models/Request/Game/PenetrationModels.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Modify: `src/GZCTF/Controllers/InternalController.cs`
- Modify: `src/GZCTF/Controllers/NodesController.cs`
- Modify: `src/GZCTF/Controllers/PenetrationPlayerController.cs`
- Modify: `src/GZCTF/Controllers/TeamLabAdminController.cs`
- Modify: `src/GZCTF/Services/PenetrationService.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentExecutionService.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueService.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueViewService.cs`
- Modify: `src/GZCTF/Services/Fleet/LocalNodeMetricsService.cs`
- Modify: `src/GZCTF/Services/Fleet/QueueManager.cs`
- Move: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabDeploymentServiceTests.cs` -> `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRuntimeOrchestratorTests.cs`
- Move: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlanServiceTests.cs` -> `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRuntimeProjectionServiceTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabTrafficCaptureServiceTests.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabTrafficFlowServiceTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabWireGuardServiceTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabStateMachineTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabEnvironmentProjectionTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabAdminControllerTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/PublicUdpGatewayProviderTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPenetrationUxContractTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabInternalControllerTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabModelTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/Fleet/DeploymentQueueServiceTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/Fleet/FleetCapacityReservationServiceTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/Fleet/NodeModelTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/Fleet/NodesControllerTests.cs`

- [ ] **Step 1: 写队列身份失败测试**

```csharp
[Fact]
public void TeamLabActiveIdentity_DependsOnlyOnRuntimeIdentity()
{
    var request = DeploymentQueueRequest.TeamLab(runtimeId: 42, dockerSlots: 2, vmSlots: 1);
    Assert.Equal("teamlab-runtime:42", DeploymentQueueTicket.BuildActiveIdentity(request));
    Assert.Null(request.GameId);
    Assert.Null(request.OwnerTeamId);
}
```

- [ ] **Step 2: 泛化 queue request**

TeamLab request 只要求 RuntimeId 和 slots。为通用 ticket 增加 `SubjectType, SubjectPublicId, SubjectDisplayName, ResourceDisplayName` 展示快照；TeamLab application 填 runtime/topology 名称，Penetration adapter 可在入队 command 中提供“比赛 / 队伍”显示上下文。`DeploymentQueueViewService` 只读这些通用字段，不反向查询 Penetration binding。GameId、OwnerTeamId 对 TeamLab 必须为空，不能参与 active identity、dispatch 或队列查询。

- [ ] **Step 3: 拆分 orchestration**

职责固定：

- RuntimePlanner：release -> network/asset/shard plan 和容量需求；
- RuntimeOrchestrator：状态机、operation、队列和事务协调；
- RuntimeOperationHandler：处理 `teamlab.runtime.create/reset/destroy` 三种 Phase 1 durable operation，并按 operation ID 恢复执行；
- ShardDeploymentService：单 shard 网络和资产创建；
- RouteApplicationService：本地和 Fabric L3 route；
- RuntimeCleanupService：按事实并行清理和残留确认；
- RuntimeProjectionService：API/admin/player query projection。
- RuntimeOverlayService：校验、保护、按 generation 解密注入并在全部 shard 确认后消费 overlay。

`ITeamLabNodeExecutor` 固定暴露 shard apply/cleanup、asset create/destroy、route apply、capture control 和 fact probe，不暴露 Agent request type、HTTP path 或 shell command。端口使用 TeamLab contract DTO；`AgentTeamLabNodeExecutor` 负责映射成 Agent request，是唯一允许依赖现有 `AgentClient` 的适配器。RuntimeOrchestrator、ShardDeploymentService、RouteApplicationService 和 CleanupService 只依赖该端口。应用层不得包含 Agent shell request builder、HTTP 重试或协议版本判断。

RuntimeOperationHandler 只负责幂等建立/定位 runtime 和唯一 DeploymentQueueTicket，并把 ticket ID 写入 ApiOperation；它不得绕过队列直接部署。已有 active/terminal ticket 时恢复关联而不是重复入队。DeploymentExecutionService 调用 RuntimeOrchestrator 执行，orchestrator 在 ticket 终态同一事务更新 runtime 和 ApiOperation；两套状态不能由轮询复制。

本步骤同时把 `TeamLabEntities.cs` 拆到四个 TeamLab Domain 文件并一次更新上述全部 C# 调用方；提交内不得存在 `GZCTF.Models.Data.TeamLab*` 类型。Fleet 和 Controller 只能使用 TeamLab contracts/projection，不能通过改 namespace 后继续持有 runtime entity。

RuntimePlanner 的 create 路径必须在 PostgreSQL transaction 中申请每个 network lease；所有 lease 成功后才写 Scheduled。任一 pool 耗尽时回滚本次全部 lease并返回 `address_pool_exhausted`。plan preview 只计算候选网段，不写 lease。overlay 在同一事务中写入受保护 envelope，不能进入 queue ticket payload。

reset 保持 runtime PublicId 和 ExternalReference 不变：先把当前 generation 全部资源清理到终态，确认节点无残留并释放 lease，再原子递增 Generation、申请新 lease、创建新 generation facts 和新 queue ticket。projection 只返回当前 generation，历史 generation 事实和 event 保留审计；旧 access grant 必须撤销。相同 creator + external reference 的重复 create 在请求 hash 一致时返回既有 runtime，hash 不一致返回 `external_reference_conflict`。

- [ ] **Step 4: 保留现有并发和失败回滚行为**

迁移当前 tests，覆盖同一 shard 内依赖顺序、独立 Docker 并发创建、多 shard 并行、一个 asset 失败时精确清理和容量释放。新增 reset 测试，断言 PublicId 不变、Generation 递增、旧 grant 撤销、旧 generation facts 终态保留且 projection 只返回新 generation。禁止为了拆文件降低当前功能。

- [ ] **Step 5: 运行队列和部署 tests**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~DeploymentQueueServiceTests|FullyQualifiedName~TeamLabRuntimeOrchestratorTests|FullyQualifiedName~TeamLabRuntimeProjectionServiceTests|FullyQualifiedName~TeamLabStateMachineTests"
```

Expected: PASS。

- [ ] **Step 6: 提交 runtime 拆分**

```powershell
git add src/GZCTF src/GZCTF.Test
git commit -m "refactor: split teamlab runtime orchestration"
```

## Task 5: 将 Penetration 改成 TeamLab 标准调用方

**Files:**
- Create: `src/GZCTF/Modules/Penetration/Application/PenetrationObjectiveService.cs`
- Create: `src/GZCTF/Modules/Penetration/Application/PenetrationWorkspaceService.cs`
- Create: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs`
- Create: `src/GZCTF/Modules/Penetration/Contracts/PenetrationContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabImageTemplateReferenceProvider.cs`
- Delete: `src/GZCTF/Modules/Penetration/Infrastructure/PenetrationImageTemplateReferenceProvider.cs`
- Delete: `src/GZCTF/Services/PenetrationService.cs`
- Modify: `src/GZCTF/Controllers/PenetrationAdminController.cs`
- Modify: `src/GZCTF/Controllers/PenetrationPlayerController.cs`
- Modify: `src/GZCTF/Hubs/Clients/IUserClient.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/PenetrationServiceTopologyMappingTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlayerWorkspaceContractTests.cs`

- [ ] **Step 1: 写 adapter contract tests**

```csharp
[Fact]
public async Task DeployTeam_CreatesRuntimeThroughTeamLabContractAndStoresBinding()
{
    await adapter.DeployAsync(gameId, teamId, actor, ct);

    teamLab.Verify(service => service.CreateRuntimeAsync(
        It.Is<CreateTeamLabRuntimeCommand>(command => command.ReleaseId == releaseId),
        actor, ct), Times.Once);
    Assert.True(await context.PenetrationTeamRuntimeBindings
        .AnyAsync(item => item.GameId == gameId && item.TeamId == teamId, ct));
}

[Fact]
public async Task Workspace_CombinesObjectivesWithRuntimeProjection()
{
    var workspace = await service.GetWorkspaceAsync(gameId, teamId, userId, ct);
    Assert.Equal(runtime.PublicId, workspace.RuntimeId);
    Assert.Equal(objective.TopologyAssetKey, workspace.Objectives.Single().AssetKey);
}
```

- [ ] **Step 2: 将 topology 管理移出 PenetrationService**

删除 `GetOrCreateConfig`、SaveConfig、Validate、Plan、Publish 和 TeamLab runtime summary/route 构造。Penetration 管理 Controller 通过 binding 获取 topology public ID，再调用 TeamLab application contract。

- [ ] **Step 3: 将动态 Flag 变成 overlay**

ObjectiveService 计算每队 Flag，构造按 `TopologyAssetKey` 分组的 secret overlay。TeamLab 只接收 secret map 并注入 asset，不引用 PenetrationObjective。

- [ ] **Step 4: 保留玩法服务**

Penetration 模块保留 objective CRUD、prerequisite、submit rate limit、Flag 校验、submission、scoreboard 和 reset policy。`PenetrationService.cs` 拆完后删除原文件，由模块 application services 取代。

同时把 Phase 1 的镜像引用 provider 从旧 Penetration topology 查询切换为 TeamLab release/runtime 查询；切换后删除 Penetration provider，Content 聚合器接口保持不变。

- [ ] **Step 5: 运行 Penetration tests**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Penetration|FullyQualifiedName~TeamLabPlayerWorkspace"
```

Expected: PASS，Penetration tests 只 mock TeamLab contracts，不实例化 TeamLab infrastructure service。

- [ ] **Step 6: 提交标准调用方**

```powershell
git add src/GZCTF src/GZCTF.Test
git commit -m "refactor: make penetration consume teamlab contracts"
```

## Task 6: 实现外部和内部 TeamLab API

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTopologiesController.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabTrafficController.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminTopologyController.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRuntimeController.cs`
- Delete: `src/GZCTF/Controllers/TeamLabAdminController.cs`
- Modify: `src/GZCTF/Controllers/InternalController.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Modify: `docs/commercialization/openapi/open-v1.json`
- Modify: `src/GZCTF.Integration.Test/Tests/Api/OpenTeamLabContractTests.cs`

- [ ] **Step 1: 实现 capabilities 和 topology API**

严格按 `teamlab-api-foundation-contract.md` 的 endpoint、scope、revision 和错误码实现。Capabilities 来自已注册 asset handlers 和节点能力聚合，不能用 `protocolVersion > 3` 判断。

- [ ] **Step 2: 实现 runtime API**

create/reset/destroy 使用 Phase 1 Idempotency-Key 和 ApiOperation；operation 映射到同一 DeploymentQueueTicket。外部 API 只使用 public UUID，application service 内部解析整数 runtime ID。

- [ ] **Step 3: 调整公网 UDP 查询**

`InternalController.BuildTeamLabUdpMappings` 只返回 public runtime ID、public UDP port、worker tunnel IP、worker WireGuard port 和 rule version。GameId/TeamId 由公网网关不需要，必须从 machine response 删除。

- [ ] **Step 4: 运行 API contract tests**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~OpenTeamLabContractTests
pwsh scripts/verify-openapi-contract.ps1
```

Expected: PASS。

- [ ] **Step 5: 提交 API 基座**

```powershell
git add src/GZCTF src/GZCTF.Integration.Test docs/commercialization/openapi/open-v1.json
git commit -m "feat: expose independent teamlab control plane api"
```

## Task 7: 拆分前端 TeamLab 与 Penetration 契约

**Files:**
- Create: `src/GZCTF/ClientApp/src/Api/TeamLabApi.ts`
- Modify: `src/GZCTF/ClientApp/src/Api/PenetrationApi.ts`
- Rename: `src/GZCTF/ClientApp/src/utils/TeamLabApi.ts` -> `src/GZCTF/ClientApp/src/utils/NodeTeamLabApi.ts`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/games/[id]/Penetration.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/games/[id]/Penetration.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/games/[id]/TeamLabRuntimeObservability.tsx`
- Delete: `src/GZCTF/ClientApp/src/pages/admin/games/[id]/pentest.tsx`
- Create: `tests/e2e/teamlab-foundation.spec.ts`

- [ ] **Step 1: 分离 TypeScript contract**

`TeamLabApi.ts` 只包含 topology/release/plan/runtime/shard/network/asset/access/traffic/capture。`PenetrationApi.ts` 只包含 objective/workspace/submit/scoreboard/reset 和 binding projection。删除 `PenetrationPolicyScope/Protocol/PortRange/EnforcementMode`。

- [ ] **Step 2: 管理页改用 TeamLab topology API**

现有页面布局和画布交互保持不变；保存、校验、发布和 plan 改调 TeamLab API，objective 编辑使用 Penetration API。页面不能继续提交一个混合 `PenetrationConfigModel`。

- [ ] **Step 3: 玩家页改用组合 workspace**

玩家页面仍展示题目和 VPN 入口；workspace DTO 由 Penetration projection 提供 objectives 和 TeamLab runtime public status，不暴露旧 environment/runtime node 类型。

- [ ] **Step 4: 删除重复 route wrapper**

删除仅 re-export `./Penetration` 的 `pentest.tsx`，保留导航实际使用的单一路由；同步更新所有链接。

- [ ] **Step 5: 运行前端和 e2e**

```powershell
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp build
pnpm exec playwright test tests/e2e/teamlab-foundation.spec.ts
```

Expected: TypeScript/build 退出码 0；e2e 完成创建 topology、发布、plan 和查看 runtime。

- [ ] **Step 6: 提交前端契约切分**

```powershell
git add src/GZCTF/ClientApp tests/e2e/teamlab-foundation.spec.ts
git commit -m "refactor: separate teamlab and penetration frontend contracts"
```

## Task 8: Contract migration 删除 Penetration 组网和重复 runtime

**Files:**
- Create: `src/GZCTF/Migrations/20260710150000_RemovePenetrationTopologyRuntimeCompatibility.cs`
- Create: `src/GZCTF/Migrations/20260710150000_RemovePenetrationTopologyRuntimeCompatibility.Designer.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Models/Data/PenetrationEntities.cs`
- Modify: `src/GZCTF.Integration.Test/Tests/Database/TeamLabFoundationMigrationTests.cs`

- [ ] **Step 1: 写 contract migration 不变量**

```csharp
[Fact]
public async Task ContractMigration_RemovesCompatibilityTablesAndPreservesGameplayAndRuntime()
{
    await ApplyAllPhaseThreeMigrationsAsync();

    Assert.False(await TableExistsAsync("PenetrationConfigs"));
    Assert.False(await TableExistsAsync("PenetrationTeamEnvironments"));
    Assert.False(await TableExistsAsync("PenetrationRuntimeNodes"));
    Assert.False(await TableExistsAsync("PenetrationRuntimeRoutes"));
    Assert.Equal(seed.SubmissionCount, await context.PenetrationSubmissions.CountAsync());
    Assert.Equal(seed.RuntimeCount, await context.TeamLabRuntimes.CountAsync());
    Assert.All(await context.TeamLabRuntimes.ToListAsync(), runtime =>
        Assert.NotEqual(Guid.Empty, runtime.TopologyReleaseId));
}
```

- [ ] **Step 2: 执行切换前数据库检查**

每个 active PenetrationTeamEnvironment 必须有唯一 TeamLabRuntime binding；每个 submission 必须有 ObjectiveId；每个 runtime 必须有 release、entry shard、network 和 asset facts。任一缺失让 migration 抛异常回滚。

- [ ] **Step 3: 删除旧表和字段**

删除 Penetration topology config/network/node/interface/edge/published snapshot、旧 environment/runtime node/runtime route/deployment event 和旧 score item 表。TeamLabRuntime 删除 GameId、TeamId、WorkerNodeId、PublishedVersion、NetworkPrefix；将已回填的 `TopologyReleaseId` 改为非空，发布身份只保留 release ID，entry 节点只保留 EntryShardId，地址事实只保留 network lease 和 runtime network。

- [ ] **Step 4: 删除 compatibility code**

删除 `SyncCompatibilityEnvironmentAsync`、旧 runtime projection、旧 route matrix DTO、旧 Penetration topology DTO 和所有双读/双写。不得保留 fallback 查询旧表。

- [ ] **Step 5: 运行 migration 和边界 tests**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~TeamLabFoundationMigrationTests
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter FullyQualifiedName~TeamLabFoundationBoundaryTests
```

Expected: PASS。

- [ ] **Step 6: 提交 contract 清理**

```powershell
git add src/GZCTF src/GZCTF.Test src/GZCTF.Integration.Test
git commit -m "refactor: remove penetration topology runtime compatibility"
```

## Task 9: 外部基座和 Penetration 双纵向验收

**Files:**
- Create: `docs/commercialization/runbooks/teamlab-foundation-acceptance.md`
- Modify: `docs/platform-commercialization-audit-progress.md`

- [ ] **Step 1: 运行全量自动测试**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj
pnpm --dir src/GZCTF/ClientApp build
pwsh scripts/verify-openapi-contract.ps1
git diff --check
```

Expected: 全部退出码为 0。

- [ ] **Step 2: 验收外部独立调用**

使用不关联 Game/Team 的 token 完成两条真实链路：

1. 混合 RFC1918 两网段 Docker topology：create -> save -> validate -> release -> plan -> runtime create -> operation poll -> access grant -> HTTP 服务访问 -> traffic query -> runtime destroy。
2. Linux VM topology：发布 cloud-init 模板，创建 runtime，验证静态 IP、DNS、路由、服务健康、SSH access endpoint 和销毁后的 qcow2 overlay/seed ISO 清理。

- [ ] **Step 3: 验收 Penetration adapter**

创建 Penetration 比赛并绑定同一 TeamLab topology；为两支队伍部署 runtime，提交动态 Flag，重置其中一队，确认另一队 runtime、score 和访问授权不受影响。

- [ ] **Step 4: 检查节点残留**

runtime destroy 后检查所有参与节点：容器、VM、bridge、router namespace、WireGuard interface、route、capture process、seed ISO 和 staging file 均无该 runtime public ID 对应资源。

- [ ] **Step 5: 检查源码边界**

```powershell
rg -n "Penetration(Config|Node|Network|Edge|Interface|ScoreItem)|SyncCompatibilityEnvironment" src/GZCTF/Modules/TeamLab src/GZCTF/Services/TeamLab
rg -n "GameId|TeamId|WorkerNodeId|NetworkPrefix" src/GZCTF/Modules/TeamLab/Domain/TeamLabRuntime.cs
```

Expected: 两条命令均无命中。

- [ ] **Step 6: 记录验收并提交**

```powershell
git add docs/commercialization/runbooks/teamlab-foundation-acceptance.md docs/platform-commercialization-audit-progress.md
git commit -m "docs: record teamlab foundation acceptance"
```

## Phase 3 退出门槛

- 外部调用不需要 Game、Team、Participation 或 Penetration DTO。
- TeamLab topology/release/runtime 表和服务不引用 Penetration entity。
- Penetration 通过 application contract 和 binding 使用 TeamLab，目标和 Flag 不进入 topology release。
- runtime create/reset/destroy 使用统一 ApiOperation 和 DeploymentQueueTicket。
- `TeamLabDeploymentService` 和 `PenetrationService` 超大混合服务已删除，职责按本计划拆分。
- 旧 Penetration topology/runtime 表、DTO、Controller 路由和 compatibility sync 已删除。
- 外部 Docker/Linux 纵向链路与平台 Penetration 纵向链路均通过真实环境验收。
- Phase 4-9 只能扩展数据治理、调度、VM、流量和 SLI，不重新定义 TeamLab 核心身份与资源边界。

## 切换与回滚

1. 切换前进入维护模式，禁止 topology 保存/发布、runtime 创建/重置/销毁和 capture 创建；等待 TeamLab DeploymentQueueTicket 全部进入终态。
2. 记录所有 active runtime、shard、network lease、asset、WireGuard mapping 和节点资源事实，执行 PostgreSQL backup。
3. 依次应用 expand migration、数据校验、contract migration和新制品；contract migration 的前置 SQL 必须确认每个 active runtime 已有 release、entry shard、binding 和完整 asset facts。
4. 新制品启动后先运行外部独立 topology/plan dry execution，再验证现有 active runtime 查询和 Penetration workspace，最后解除维护模式。
5. expand 或 contract migration 事务失败时继续运行旧制品；contract 成功后出现应用失败时，必须恢复数据库 backup、部署旧制品并按切换前节点事实核对资源，不能仅回滚二进制。
6. 解除维护模式后产生的新 release/runtime 不存在旧模型表达，禁止回滚到旧制品；此时只能前滚修复或恢复到维护窗口备份并明确丢弃窗口后的写入。
