# Phase 4 Database Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 PostgreSQL 建设为可支撑长期运营和数百支队伍高峰比赛的可靠事实层，补齐核心查询索引、分区、聚合、保留、清理和迁移验收闭环。

**Architecture:** 业务事实继续保存在 PostgreSQL；只有经数据量和查询基准证明的高频追加表使用原生时间分区。业务实体由模块 Infrastructure persistence configuration 映射，查询通过模块 query service/repository 执行；生命周期由显式策略目录和 PostgreSQL 单实例治理 worker 维护，不依赖 Redis 才能保证正确性。原始高频数据在保留期内可查，长期趋势由聚合事实保留；Submission、Participation、课程进度、理论答题和 AWDP 赛事实体不做无条件定时删除。

**Tech Stack:** .NET 10、EF Core 10、Npgsql、PostgreSQL 17、pg_trgm、原生 range partition、xUnit、Testcontainers.PostgreSql、PowerShell。

---

## 0. 当前代码事实与冻结决策

- 当前 `AppDbContext` 有 105 个 `DbSet`，`OnModelCreating` 约 1990 行；Phase 0、1、3 会先删除旧模型并迁入模块化配置，Phase 4 必须在该结果上继续，不能恢复实体 attribute 和中心化超大映射。
- `SubmissionRepository` 的列表查询按 `SubmitTimeUtc` 排序但现有索引不覆盖 `GameId + SubmitTimeUtc`、`ChallengeId + SubmitTimeUtc` 和 `TeamId + SubmitTimeUtc`。
- `Participation` 有 `(TeamId, GameId)` 普通索引，但业务语义要求一个队伍在一个比赛只有一条 Participation，目标必须是唯一约束。
- `TheoryQuestionBankItem` 只有 `(Type, BankName)`；tag 仍不是正式关系，关键词查询无法形成稳定索引契约。
- `DeploymentQueueTicket` 已有 active identity partial unique index，但历史列表、节点状态和回收查询缺少统一的时间游标索引。
- `ImageDistributionRecord.References` 当前是 JSON 列表；它既承担引用事实又维护 `ReferenceCount`，并发释放容易漂移。Phase 4 将引用拆成关系表，计数改为查询/投影，不保留双写 JSON。
- `TeamLabTrafficFlowService` 当前逐样本执行 `Any` 去重并逐批直接写数据库，再按 runtime 保留 5000 条；这一算法随流量线性退化。Phase 4 先提供唯一指纹、时间分区和批量落库目标，Phase 5 再接 Redis Stream 缓冲。
- `LogRepository` 和部署队列历史使用 offset pagination；深页会放大扫描。日志、队列历史和流量查询统一改为稳定时间游标。
- PostgreSQL 是所有业务状态、部署状态、操作状态和可恢复事实的唯一来源。分区、清理和聚合任务使用 PostgreSQL advisory lock；Phase 4 不依赖尚未治理完成的 Redis。
- 首轮只对 `Logs`、`TeamLabTrafficFlows` 两个已确认高频追加表实施原生时间分区。`DeploymentQueueTickets`、`ApiOperations`、`TeamLabEvents` 先使用复合/partial index 和分批清理；基准未证明必要前不得扩大分区范围。
- 默认保留值写入配置并可调整：原始系统日志 30 天、TeamLab 原始 flow 7 天、TeamLab 5 分钟聚合 180 天、终态部署队列 180 天、终态外部 API operation 90 天、TeamLab 运行事件 180 天、治理运行记录 365 天。PCAP 文件生命周期由 Phase 9 管理。

## 1. 目标数据分类

| 类别 | 数据集 | 删除语义 |
| --- | --- | --- |
| 核心业务事实 | Participation、Submission、FirstSolve、课程进度、理论答题、AWDP round/flag/checker/patch | 随拥有者显式删除或归档，不执行按时间自动清理 |
| 当前状态事实 | WorkerNode、ImageDistributionRecord、DeploymentQueueTicket active rows、TeamLab runtime 当前 generation | 必须可恢复；不能仅存在于缓存 |
| 操作历史 | terminal DeploymentQueueTicket、ApiOperation、TeamLabEvent、SystemLog | 按策略保留，分批清理并记录治理运行 |
| 高频原始观测 | TeamLabTrafficFlow、后续节点指标 | 短期时间分区，先聚合后删除原始分区 |
| 长期聚合 | TeamLabTrafficFlowAggregate、OperationalLogAggregate、DeploymentLifecycleAggregate | 按较长周期保留，供趋势和容量分析 |

本阶段审查核心表的范围固定覆盖 Submission、Participation、GameChallenge、ExerciseChallenge、TrainingCourse 与进度、Theory 题库与答题、WorkerNode、DeploymentQueueTicket、ImageDistributionRecord、TeamLabTrafficFlow 和 AWDP。查询验收逐项覆盖排行榜、提交查询、队伍状态、课程进度、理论题检索、节点队列和流量查询；任一类别不得只增加索引而缺少调用路径验证。

## Task 1: 建立数据库治理基线、失败测试和查询清单

**Files:**
- Create: `src/GZCTF.Test/UnitTests/Architecture/DatabaseGovernanceBoundaryTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/DatabaseIndexContractTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/DatabaseRetentionPolicyTests.cs`
- Create: `scripts/database/capture-query-plans.ps1`
- Create: `docs/commercialization/database-index-and-lifecycle-audit.md`
- Modify: `docs/platform-commercialization-audit-progress.md`

- [ ] **Step 1: 写禁止新增 EF attribute 和中心映射回流的失败测试**

测试扫描 `GZCTF.Modules.*.Domain`，禁止依赖 `Microsoft.EntityFrameworkCore` 和 `System.ComponentModel.DataAnnotations.Schema`；同时断言 `AppDbContext.OnModelCreating` 只调用 `ApplyConfigurationsFromAssembly` 和 Identity 基础配置，不再出现本计划治理实体的 `builder.Entity<...>` 块。

```csharp
[Fact]
public void ModuleDomain_DoesNotContainPersistenceAttributes()
{
    var result = Types.InAssembly(typeof(Program).Assembly)
        .That().ResideInNamespaceMatching("GZCTF.Modules.*.Domain")
        .ShouldNot().HaveDependencyOnAny(
            "Microsoft.EntityFrameworkCore",
            "System.ComponentModel.DataAnnotations.Schema")
        .GetResult();

    Assert.True(result.IsSuccessful,
        string.Join(", ", result.FailingTypes.Select(type => type.FullName)));
}
```

- [ ] **Step 2: 写真实 PostgreSQL 索引契约测试**

`DatabaseIndexContractTests` 从 `pg_indexes` 和 `pg_constraint` 查询实际迁移结果，至少先断言下列目标尚不存在而失败：Participation 唯一约束、Submission 三条时间查询索引、queue terminal 游标索引、ImageDistributionReference 唯一约束、Theory tag 关系索引、Logs 和 TeamLabTrafficFlows 分区父表。

```csharp
var definitions = await context.Database
    .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'public'")
    .ToArrayAsync();

Assert.Contains(definitions, sql =>
    sql.Contains("Participations", StringComparison.Ordinal) &&
    sql.Contains("GameId", StringComparison.Ordinal) &&
    sql.Contains("TeamId", StringComparison.Ordinal) &&
    sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 3: 固化审计文档和基准数据规模**

在 `database-index-and-lifecycle-audit.md` 写入每个数据集的拥有模块、当前表、预计高峰写入、主查询、目标索引、保留策略、删除负责人和验收 SQL。基准固定包含：500 支队伍、每队 30 个普通题实例、单场 300 万条 Submission、1000 万条 TeamLab raw flow、500 万条系统日志、20 万条部署历史、10 万名课程学员进度。

- [ ] **Step 4: 建立可重复 EXPLAIN 脚本**

`capture-query-plans.ps1` 接受 `-ConnectionString` 和 `-OutputPath`，用 `psql -X -v ON_ERROR_STOP=1` 执行版本化 SQL；每条查询使用 `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)`，输出不得包含 Answer、token、Flag 或用户密码。脚本缺少 `psql` 或目标数据库不是专用基准库时立即退出。

- [ ] **Step 5: 运行失败测试并提交**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter "FullyQualifiedName~DatabaseIndexContractTests|FullyQualifiedName~DatabaseRetentionPolicyTests"
git add src/GZCTF.Test src/GZCTF.Integration.Test scripts/database docs/commercialization/database-index-and-lifecycle-audit.md docs/platform-commercialization-audit-progress.md
git commit -m "test: establish database governance baseline"
```

Expected: 目标索引、分区和 retention catalog 断言失败；测试基础设施本身无连接或编译错误。

## Task 2: 拆出模块 persistence configuration 并建立业务唯一约束

**Files:**
- Create: `src/GZCTF/Modules/Ctf/Infrastructure/Persistence/CtfQueryEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/Training/Infrastructure/Persistence/TrainingProgressEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/Theory/Infrastructure/Persistence/TheoryEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/Persistence/RuntimeHistoryEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/Awdp/Infrastructure/Persistence/AwdpEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/Audit/Infrastructure/Persistence/SystemLogEntityConfiguration.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Models/Data/Submission.cs`
- Modify: `src/GZCTF/Models/Data/Participation.cs`
- Modify: `src/GZCTF/Models/Data/DeploymentQueueTicket.cs`
- Modify: `src/GZCTF/Models/Data/ImageDistributionRecord.cs`
- Modify: `src/GZCTF/Models/Data/TheoryExam.cs`
- Modify: `src/GZCTF/Models/Data/AwdpRound.cs`
- Modify: `src/GZCTF/Models/Data/LogModel.cs`
- Modify: `src/GZCTF.Test/UnitTests/Architecture/DatabaseGovernanceBoundaryTests.cs`

- [ ] **Step 1: 移除目标实体的 `[Index]` 和 persistence attribute**

保留尚未迁入模块 Domain 的 `[Key]`、`[Required]` 和输入长度注解只作为当前模型约束；索引、关系、delete behavior、enum conversion、PostgreSQL 类型和 generated column 全部进入 `IEntityTypeConfiguration<T>`。不得在配置类中调用其他模块 repository。

- [ ] **Step 2: 建立 Participation 和核心业务索引**

`CtfQueryEntityConfigurations` 固定配置：

```csharp
builder.HasIndex(item => new { item.GameId, item.TeamId })
    .IsUnique()
    .HasDatabaseName("UX_Participations_Game_Team");
builder.HasIndex(item => new { item.GameId, item.Status, item.DivisionId, item.TeamId })
    .HasDatabaseName("IX_Participations_Game_Status_Division_Team");

builder.HasIndex(item => new { item.GameId, item.SubmitTimeUtc, item.Id })
    .IsDescending(false, true, true)
    .HasDatabaseName("IX_Submissions_Game_Time_Id");
builder.HasIndex(item => new { item.ChallengeId, item.SubmitTimeUtc, item.Id })
    .IsDescending(false, true, true)
    .HasDatabaseName("IX_Submissions_Challenge_Time_Id");
builder.HasIndex(item => new { item.TeamId, item.SubmitTimeUtc, item.Id })
    .IsDescending(false, true, true)
    .HasDatabaseName("IX_Submissions_Team_Time_Id");
builder.HasIndex(item => new { item.ParticipationId, item.ChallengeId })
    .HasDatabaseName("IX_Submissions_Participation_Challenge");
```

unchecked Flag 查询增加 `(Status, SubmitTimeUtc, Id)` partial index；partial predicate 必须使用迁移生成后的实际 enum 存储值，不在 C# 中手写猜测数值。

- [ ] **Step 3: 建立课程、理论、AWDP 和运行索引**

固定唯一关系：`TrainingCourseProgress(CourseId, UserId)`、`TrainingChapterProgress(ChapterId, UserId)`、`TheoryAnswerSheet(UserId, GameId, AttemptNumber)`、`AwdpRound(GameId, RoundNumber)`、`ImageDistributionRecord(ImageTemplateId, WorkerNodeId)`。部署历史使用 `(Status, CompletedAt DESC, Id DESC)` partial index覆盖 terminal 状态，节点队列使用 `(TargetNodeId, Status, CreatedAt, Id)`。

- [ ] **Step 4: 缩减 AppDbContext**

`OnModelCreating` 保留 Identity 基类调用、共享 converter 注册和：

```csharp
builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
```

同一实体只能有一个主配置文件；Phase 1/3 已创建的配置需要合并索引，不新增第二个互相覆盖的 configuration。

- [ ] **Step 5: 运行架构测试并提交**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~DatabaseGovernanceBoundaryTests
git add src/GZCTF/Modules src/GZCTF/Models src/GZCTF.Test/UnitTests/Architecture/DatabaseGovernanceBoundaryTests.cs
git commit -m "refactor: modularize persistence configuration"
```

Expected: PASS；`AppDbContext` 不再拥有上述实体的映射细节。

## Task 3: 规范 Theory tag 与镜像分发引用事实

**Files:**
- Create: `src/GZCTF/Modules/Theory/Domain/TheoryQuestionTag.cs`
- Create: `src/GZCTF/Modules/Theory/Domain/TheoryQuestionTagBinding.cs`
- Create: `src/GZCTF/Modules/Theory/Application/TheoryQuestionCatalog.cs`
- Create: `src/GZCTF/Modules/Runtime/Domain/ImageDistributionReference.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/Persistence/ImageDistributionReferenceEntityConfiguration.cs`
- Modify: `src/GZCTF/Models/Data/ImageDistributionRecord.cs`
- Modify: `src/GZCTF/Modules/Theory/Infrastructure/Persistence/TheoryEntityConfigurations.cs`
- Modify: `src/GZCTF/Modules/Runtime/Infrastructure/Persistence/RuntimeHistoryEntityConfigurations.cs`
- Modify: `src/GZCTF/Services/Fleet/ImageDistributionService.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF.Test/UnitTests/Theory/TheoryTagNormalizationTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/ImageDistributionReferenceConcurrencyTests.cs`

- [ ] **Step 1: 写 tag 规范化和引用并发失败测试**

tag 规则固定为 Unicode trim、连续空白合并、`ToUpperInvariant` 唯一键、显示名保留；空 tag 和超过 64 字符 tag 拒绝。并发添加同一 `(RecordId, Kind, ResourceId)` 引用只产生一行；并发释放不能出现负数或误删其他比赛/课程引用。

- [ ] **Step 2: 实现 Theory tag 正式关系**

`TheoryQuestionTag` 字段固定为 `Id, DisplayName, NormalizedName, CreatedAt`；binding 复合主键为 `(QuestionId, TagId)`。`TheoryQuestionCatalog.SearchAsync` 使用类型、tag IDs、更新时间游标和 limit，标题/题库关键词使用 `pg_trgm` GIN 索引；不对 AnswerIndexes 建全文索引。

- [ ] **Step 3: 将镜像引用从 JSON 拆为关系表**

`ImageDistributionReference` 字段固定为 `Id, DistributionRecordId, Kind, ResourceId, CreatedAt`，唯一约束 `(DistributionRecordId, Kind, ResourceId)`。从 `Models/Data/ImageDistributionRecord.cs` 删除旧 record、`References` JSON 属性、`ReferenceCount` 和对应 enum 声明，enum 与新实体只在 Runtime Domain 声明一次；投影模型通过关系表 count 返回引用数量。引用添加使用 `INSERT ... ON CONFLICT DO NOTHING`，释放使用精确条件 `DELETE`。

- [ ] **Step 4: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~TheoryTagNormalizationTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~ImageDistributionReferenceConcurrencyTests
git add src/GZCTF/Modules src/GZCTF/Services/Fleet/ImageDistributionService.cs src/GZCTF/Models/AppDbContext.cs src/GZCTF.Test src/GZCTF.Integration.Test
git commit -m "refactor: normalize tags and image references"
```

Expected: PASS；并发引用事实由数据库唯一约束保护。

## Task 4: 建立 retention policy catalog 和治理运行事实

**Files:**
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/DataRetentionOptions.cs`
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/DataSetRetentionPolicy.cs`
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/DataRetentionPolicyCatalog.cs`
- Create: `src/GZCTF/Modules/Audit/Domain/DataGovernanceRun.cs`
- Create: `src/GZCTF/Modules/Audit/Infrastructure/Persistence/DataGovernanceRunEntityConfiguration.cs`
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/DataGovernanceMetrics.cs`
- Modify: `src/GZCTF/Models/Internal/Configs.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Composition/ModuleRegistration.cs`
- Create: `src/GZCTF.Test/UnitTests/Persistence/DataRetentionPolicyCatalogTests.cs`

- [ ] **Step 1: 写策略完整性失败测试**

测试要求每个自动治理数据集都有唯一 name、owner module、raw retention、aggregate retention、partition grain、batch size、archive action 和 failure mode；核心业务事实必须显式标记 `OwnerManaged`，不能因未注册而使用隐式默认删除。

- [ ] **Step 2: 实现强类型策略目录**

```csharp
public sealed record DataSetRetentionPolicy(
    string Name,
    string OwnerModule,
    DataLifecycleMode Mode,
    TimeSpan? RawRetention,
    TimeSpan? AggregateRetention,
    PartitionGrain PartitionGrain,
    int DeleteBatchSize);
```

catalog 固定注册 `system-log`、`teamlab-flow`、`teamlab-flow-aggregate`、`deployment-ticket`、`api-operation`、`teamlab-event`、`governance-run`。启动时使用 `ValidateOnStart` 检查 raw retention 为正、aggregate retention 大于 raw retention、batch size 介于 100 和 20000。

- [ ] **Step 3: 建立治理运行审计**

`DataGovernanceRun` 字段固定为 `Id, DataSet, Operation, Status, LeaseOwner, Cutoff, RowsRead, RowsAggregated, RowsDeleted, PartitionName, ErrorCode, ErrorDetail, StartedAt, CompletedAt`。错误详情限制 2048 字符，禁止写入原始日志正文或流量 payload。

- [ ] **Step 4: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~DataRetentionPolicyCatalogTests
git add src/GZCTF/Infrastructure/Persistence/Governance src/GZCTF/Modules/Audit src/GZCTF/Models src/GZCTF/Composition src/GZCTF.Test
git commit -m "feat: define database lifecycle policies"
```

Expected: PASS；未登记自动删除策略时应用启动失败，不会静默删除数据。

## Task 5: 实施 Logs 与 TeamLabTrafficFlows 时间分区和长期聚合

**Files:**
- Create: `src/GZCTF/Modules/Audit/Domain/OperationalLogAggregate.cs`
- Create: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTrafficFlowAggregate.cs`
- Create: `src/GZCTF/Modules/Audit/Domain/DeploymentLifecycleAggregate.cs`
- Create: `src/GZCTF/Modules/Audit/Infrastructure/Persistence/OperationalAggregateEntityConfigurations.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabTrafficAggregateEntityConfiguration.cs`
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/PostgresPartitionManager.cs`
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/OperationalAggregationService.cs`
- Modify: `src/GZCTF/Modules/Audit/Infrastructure/Persistence/SystemLogEntityConfiguration.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRuntimeEntityConfigurations.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Domain/TeamLabTraffic.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/PartitionRoutingTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/OperationalAggregationTests.cs`

- [ ] **Step 1: 写分区路由和聚合幂等失败测试**

测试插入跨三个月的 Log 和跨三天的 flow，验证记录进入对应 child partition；重复运行同一时间窗口聚合结果不翻倍；没有未来分区时 manager 创建当前分区和后续两个分区。

- [ ] **Step 2: 定义分区键和幂等指纹**

`LogModel` 使用 `(TimeUtc, Id)` 复合主键，`Id` 改为 `long` identity；`TeamLabTrafficFlow` 使用 `(CapturedAt, Id)` 复合主键并新增 `Generation`、`Fingerprint bytea`。唯一约束使用 `(CapturedAt, RuntimeId, Generation, Fingerprint)`，Fingerprint 是规范化五元组、network、采集时间和 byte counter 的 SHA-256，不包含密钥或 payload。

- [ ] **Step 3: 实现聚合事实**

flow 以 5 分钟、runtime、generation、shard、network、协议、source/destination RFC1918 prefix 聚合 `FlowCount, PacketCount, Bytes`；日志以 1 小时、level、logger 聚合 count；部署生命周期以 1 天、kind、terminal status、node 聚合 count 和 duration percentile 输入。聚合 upsert 使用完整维度唯一键，重复窗口执行覆盖同一事实。

- [ ] **Step 4: 实现安全分区管理器**

`PostgresPartitionManager` 只接受 catalog 中的固定数据集，不接受任意表名；用 `pg_advisory_xact_lock` 保证多实例单次 DDL，使用 `CREATE TABLE IF NOT EXISTS ... PARTITION OF ... FOR VALUES FROM ... TO ...` 创建 UTC 边界分区。分区名由固定前缀和 UTC 日期组成并通过正则校验。

- [ ] **Step 5: 运行 PostgreSQL 集成测试并提交**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter "FullyQualifiedName~PartitionRoutingTests|FullyQualifiedName~OperationalAggregationTests"
git add src/GZCTF/Modules src/GZCTF/Infrastructure/Persistence/Governance src/GZCTF/Models/AppDbContext.cs src/GZCTF.Integration.Test
git commit -m "feat: partition and aggregate operational data"
```

Expected: PASS；重复聚合、跨边界路由和并发分区创建均稳定。

## Task 6: 实现可恢复的聚合、保留和清理 worker

**Files:**
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/DataGovernanceWorker.cs`
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/DataRetentionExecutor.cs`
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/PostgresGovernanceLease.cs`
- Create: `src/GZCTF/Infrastructure/Persistence/Governance/TerminalHistoryCleaner.cs`
- Modify: `src/GZCTF/Composition/ModuleRegistration.cs`
- Modify: `src/GZCTF/Extensions/Startup/TelemetryExtension.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/DataGovernanceWorkerTests.cs`

- [ ] **Step 1: 写崩溃恢复和先聚合后删除失败测试**

覆盖：两个 worker 同时启动只允许一个执行；聚合失败时原始分区不删除；进程在聚合成功、删除前终止后可重跑；active queue ticket、active ApiOperation、运行中 TeamLab event 永不进入时间清理；每批删除不超过策略 batch size。

- [ ] **Step 2: 实现 worker 状态机**

顺序固定为 `AcquireLease -> EnsurePartitions -> AggregateClosedWindows -> VerifyAggregate -> DeleteOrDropExpiredRaw -> CleanTerminalRows -> RecordRun`。每一步更新 `DataGovernanceRun`；取消和异常保留失败事实，下次按 window 和唯一聚合键重试。

- [ ] **Step 3: 实现低锁清理**

分区表只在完整 UTC window 已聚合且超过 retention 时 drop child partition；非分区表使用 `SELECT Id ... ORDER BY CompletedAt, Id LIMIT @batch FOR UPDATE SKIP LOCKED` 后批量删除，批次间释放事务并接受 cancellation。禁止单条 `DELETE WHERE CompletedAt < cutoff` 扫描全部历史。

- [ ] **Step 4: 暴露治理 metrics**

至少记录 `gzctf_db_governance_duration_seconds`、`gzctf_db_governance_rows_total`、`gzctf_db_governance_failures_total`、`gzctf_db_partition_horizon_days`；labels 只允许固定 data set 和 operation，禁止 runtime/team/user ID 进入 metric label。

- [ ] **Step 5: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~DataGovernanceWorkerTests
git add src/GZCTF/Infrastructure/Persistence/Governance src/GZCTF/Composition src/GZCTF/Extensions/Startup/TelemetryExtension.cs src/GZCTF.Integration.Test
git commit -m "feat: automate database retention governance"
```

Expected: PASS；worker 中断不会导致未聚合数据丢失。

## Task 7: 将高频列表切换为稳定游标和窄投影

**Files:**
- Create: `src/GZCTF/Infrastructure/Persistence/Queries/TimeCursor.cs`
- Modify: `src/GZCTF/Repositories/SubmissionRepository.cs`
- Modify: `src/GZCTF/Repositories/Interface/ISubmissionRepository.cs`
- Modify: `src/GZCTF/Repositories/LogRepository.cs`
- Modify: `src/GZCTF/Repositories/Interface/ILogRepository.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueViewService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficFlowService.cs`
- Modify: `src/GZCTF/Models/Request/Admin/LogMessageModel.cs`
- Modify: `src/GZCTF/ClientApp/src/Api.ts`
- Modify: `src/GZCTF/ClientApp/src/Api/TeamLabApi.ts`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/Logs.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/queue/Index.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/games/[id]/TeamLabRuntimeObservability.tsx`
- Create: `src/GZCTF.Integration.Test/Tests/Database/KeysetPaginationTests.cs`

- [ ] **Step 1: 写同时间戳稳定分页失败测试**

插入 300 条相同 timestamp 的记录，按 `(time DESC, id DESC)` 获取连续页面，断言无重复、无遗漏；在读取第二页前插入新记录，旧游标结果仍稳定。

- [ ] **Step 2: 实现通用时间游标**

```csharp
public readonly record struct TimeCursor(DateTimeOffset Time, long Id)
{
    public string Encode() => WebEncoders.Base64UrlEncode(
        Encoding.UTF8.GetBytes($"{Time.UtcTicks}:{Id}"));
}
```

decode 必须校验长度、UTC ticks 范围和正 ID，错误返回 `invalid_cursor`，不回退到第一页。

- [ ] **Step 3: 列表查询使用窄投影**

Submission、Log、queue、flow 查询都先 `AsNoTracking`、应用 tenant/game/runtime 过滤、应用 `(time < cursor.Time || time == cursor.Time && id < cursor.Id)`、排序、`Take(limit + 1)`，最后直接投影 DTO。禁止先 Include 完整实体图再分页。

- [ ] **Step 4: 更新前端分页契约**

响应固定为 `{ items, nextCursor }`。页面保留“上一页/下一页”体验，通过 cursor 栈返回上一页；不再根据总行数执行高成本 COUNT。实时刷新只重置到第一页，不改变节点或行的稳定 key。

- [ ] **Step 5: 运行后端与前端检查并提交**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~KeysetPaginationTests
pnpm --dir src/GZCTF/ClientApp check
git add src/GZCTF/Infrastructure/Persistence/Queries src/GZCTF/Repositories src/GZCTF/Services/Fleet src/GZCTF/Modules/TeamLab src/GZCTF/Models/Request src/GZCTF/ClientApp src/GZCTF.Integration.Test
git commit -m "perf: use keyset pagination for operational history"
```

Expected: PASS；深页不再生成 OFFSET 扫描。

## Task 8: 编写 expand-migrate-contract 数据迁移

**Files:**
- Create: `src/GZCTF/Migrations/20260710160000_ExpandDatabaseGovernance.cs`
- Create: `src/GZCTF/Migrations/20260710160000_ExpandDatabaseGovernance.Designer.cs`
- Create: `src/GZCTF/Migrations/20260710161000_BackfillDatabaseGovernance.cs`
- Create: `src/GZCTF/Migrations/20260710161000_BackfillDatabaseGovernance.Designer.cs`
- Create: `src/GZCTF/Migrations/20260710162000_ContractDatabaseGovernance.cs`
- Create: `src/GZCTF/Migrations/20260710162000_ContractDatabaseGovernance.Designer.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/DatabaseGovernanceMigrationTests.cs`
- Create: `docs/commercialization/runbooks/database-governance-migration.md`

- [ ] **Step 1: 写旧 schema 到目标 schema 的迁移测试**

测试先迁移到 Phase 3 最后 migration，再用参数化 SQL 播种重复 Participation 候选、旧镜像 JSON references、跨月日志和 flow、理论题 bank 数据；随后迁移到 latest，校验行数、关系数、聚合 checksum 和唯一约束。生产程序集不得保留旧实体只为迁移测试服务。

- [ ] **Step 2: Expand 新表、索引和扩展**

Expand 创建 `pg_trgm`、tag/reference/aggregate/governance 表、分区影子表和允许 `CREATE INDEX CONCURRENTLY` 的独立运维 SQL。事务内 migration 不执行 concurrently；大生产索引由 runbook 在维护窗口前执行并验证 `pg_index.indisvalid`。

- [ ] **Step 3: 分批回填并校验**

镜像 JSON reference 使用 `jsonb_to_recordset` 去重回填；tag 从现有 BankName 只生成明确的 `bank:<normalized>` 迁移 tag，不猜测业务标签；Logs/flow 按 UTC window 复制到影子分区表。每批记录 source count、target count 和 checksum。

- [ ] **Step 4: Contract 原子切换**

维护模式下停止高频写入，完成最后增量复制，验证 checksum，原子 rename 分区影子表，删除旧 JSON reference 列和冗余索引，更新 model snapshot。若切换前失败，继续使用旧表；切换后回滚使用数据库备份恢复，不编写反向压缩到 JSON 的有损 Down migration。

- [ ] **Step 5: 运行迁移测试并提交**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~DatabaseGovernanceMigrationTests
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj
git add src/GZCTF/Migrations src/GZCTF.Integration.Test/Tests/Database/DatabaseGovernanceMigrationTests.cs docs/commercialization/runbooks/database-governance-migration.md
git commit -m "feat: migrate database governance schema"
```

Expected: PASS；输出 `No changes have been made to the model since the last migration.`。

## Task 9: 执行查询计划、容量和退化基准

**Files:**
- Create: `scripts/database/sql/seed-commercial-baseline.sql`
- Create: `scripts/database/sql/query-plan-contracts.sql`
- Create: `scripts/database/assert-query-plans.ps1`
- Create: `docs/commercialization/benchmarks/phase-04-database-baseline.md`
- Modify: `.github/workflows/quality.yml`
- Modify: `docs/commercialization/database-index-and-lifecycle-audit.md`

- [ ] **Step 1: 建立脱敏确定性种子**

SQL 只生成合成 UUID、名称、IP 和时间，不读取生产数据。种子规模分 `ci` 与 `commercial` 两档；CI 验证索引选择，commercial 档在专用 PostgreSQL 上验证目标容量。

- [ ] **Step 2: 固定主查询计划门槛**

CI 门禁检查 Submission、Participation、课程进度、理论 tag、queue、flow 和 log 主查询不出现无过滤大表 `Seq Scan`；commercial 基准记录 p50/p95/p99，不把机器相关毫秒值硬编码进普通 CI。商业环境目标：排行榜事实查询 p95 小于 500ms、历史页 p95 小于 300ms、单批 1000 flow 落库小于 250ms、治理批次锁等待小于 100ms。

- [ ] **Step 3: 验证分区裁剪和索引体积**

每个时间查询的 JSON plan 必须只访问命中 window 的 child partitions；记录 table/index bytes、dead tuples、autovacuum 时间和 WAL 增量。任何单个新增索引超过被索引表大小的 80% 必须在审计文档中证明其查询收益，否则删除。

- [ ] **Step 4: 运行基准并提交**

```powershell
pwsh scripts/database/capture-query-plans.ps1 -ConnectionString $env:GZCTF_BENCHMARK_DATABASE -OutputPath artifacts/phase-04-query-plans
pwsh scripts/database/assert-query-plans.ps1 -InputPath artifacts/phase-04-query-plans
git add scripts/database docs/commercialization/benchmarks/phase-04-database-baseline.md docs/commercialization/database-index-and-lifecycle-audit.md .github/workflows/quality.yml
git commit -m "test: enforce database query plan contracts"
```

Expected: 所有 plan contract 通过；benchmark 文档记录硬件、PostgreSQL 配置、数据规模和结果。

## Task 10: Phase 4 全量验收与退出

**Files:**
- Create: `docs/commercialization/runbooks/database-governance-operations.md`
- Modify: `docs/platform-commercialization-audit-progress.md`
- Modify: `docs/commercialization/database-index-and-lifecycle-audit.md`

- [ ] **Step 1: 运行全量自动检查**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj
pnpm --dir src/GZCTF/ClientApp check
git diff --check
```

Expected: 全部退出码为 0。

- [ ] **Step 2: 做真实恢复演练**

在专用验收库执行备份、迁移、写入、分区创建、聚合、清理、中断恢复和 point-in-time restore。对比核心业务表 count/checksum，确认 Submission、课程进度、理论答题和 AWDP 历史未被 retention worker 触碰。

- [ ] **Step 3: 做阶段双重审查**

规格审查逐项对照总纲 Phase 4、数据治理审计和本计划；代码质量审查重点检查重复索引、未限定 delete、offset 深分页、跨模块直接 DbContext 查询、EF attribute 回流和 migration 不可恢复切换。发现项全部修复并重跑相关门禁。

- [ ] **Step 4: 更新进度并提交**

```powershell
git add docs/commercialization docs/platform-commercialization-audit-progress.md
git commit -m "docs: complete phase 4 database governance"
```

## Phase 4 退出门槛

- 核心业务查询有真实 PostgreSQL 索引和 query plan 证据，不以实体 attribute 数量代替验证。
- Participation、课程进度、理论尝试、镜像引用和 active queue identity 的唯一性由数据库约束保护。
- Logs 与 TeamLabTrafficFlows 完成时间分区、聚合、保留和中断恢复。
- 自动治理数据集均有明确策略；核心业务事实明确禁止定时删除。
- 高频列表使用稳定游标和窄投影，不存在无限 count 或深 OFFSET。
- 迁移有前置检查、校验摘要、维护窗口、恢复路径和真实演练记录。
- `AppDbContext.OnModelCreating` 不再重新聚合模块映射细节。
- Phase 5 可以在固定的数据生命周期、聚合事实和查询契约上接入 Redis，不重新定义 PostgreSQL 事实模型。
