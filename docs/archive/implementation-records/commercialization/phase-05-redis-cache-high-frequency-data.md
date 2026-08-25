# Phase 5 Redis Cache and High-Frequency Data Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Redis 收敛为可审计、可降级的缓存、协调和高频缓冲底座，在不改变 PostgreSQL 事实归属的前提下降低排行榜、节点心跳和 TeamLab 流量对数据库的瞬时压力。

**Architecture:** 全站只创建一个受 DI 管理的 `ConnectionMultiplexer`，Redis 用途严格分为 cache、lock、lease、stream、SignalR 五类。业务事实、部署队列和 operation 始终在 PostgreSQL；缓存使用 typed policy 和 projection revision 防止陈旧结果掩盖事实；TeamLab flow 与节点 metrics 使用有界 Redis Stream/Hash 缓冲并批量落库。Redis 不可用时，缓存旁路、部署队列继续 PostgreSQL 轮询、流量进入有界本机缓冲、节点指标直接批量写库；公网端口租约和多实例分布式互斥 fail closed。

**Tech Stack:** .NET 10、Microsoft.Extensions.Caching.Hybrid、StackExchange.Redis、SignalR Redis backplane、PostgreSQL 17、OpenTelemetry、xUnit、Testcontainers.Redis、Testcontainers.PostgreSql。

---

## Implementation Progress

### 2026-07-12 启动基线

- 实施基线为 `b45eb9b`，Phase 4 数据库治理已闭环并推送至 `origin/main`；本阶段不部署、不连接或修改生产服务器。
- 当前代码事实复核确认：Redis 连接仍由框架注册、`RedisDistributedLock`、`PortAllocationService` 和 API rate-limit store 分别创建；旧 `CacheMaker`/`CacheHelper` 双层缓存仍在；TeamLab flow 仍同步逐批查重落库；部署队列事实已在 PostgreSQL。
- 按用户要求将 11 个任务合并为五个大单元实施和验收：Redis 底座与投影缓存、租约与队列协调、节点与 TeamLab 高频缓冲、迁移与运维、并发故障与全量退出门禁。大单元内部集中修改，完成后才运行对应验证。
- 计划中的文件路径按 Phase 2/3/4 合并后的当前模块边界调整；不恢复已删除旧模块，不保留新旧缓存或锁实现的长期兼容双轨。
- 当前状态：大单元 1 进行中，正在建立单一 Redis connection、typed cache policy、projection revision 和核心投影失效边界。

### 2026-07-12 大单元 1-2 进度

- 大单元 1 已完成：框架缓存、SignalR 和运行时组件共享异步单例 Redis provider；旧 `CacheMaker`、cache channel、handler 和 `CacheHelper` 已删除；排行榜、理论、培训及公共投影进入 typed policy catalog。
- `AppDbContext` 在 PostgreSQL 业务保存事务内递增 scoreboard、theory 和 training projection revision；cache key 同时包含全局与资源 revision，Redis 失效失败不会重新命中旧事实。
- 大单元 1 集中门禁通过：solution build 0 warning/0 error，Redis runtime 与 policy 专项 15/15。
- 大单元 2 已完成生产代码：旧 `IDistributedLockService`、`LocalSemaphoreLock` 和 `RedisDistributedLock` 已删除；新 owner lease 支持续租、lease-lost、compare-owner renew/release 和 distributed fail-closed。
- 公网端口租约增加持久 `PublicPortLeaseId`，分配、重建、Nginx 同步、刷新和销毁均按 port + owner 比较；缺 owner 的旧路径拒绝无条件释放。
- 部署队列继续以 PostgreSQL ticket 为唯一事实，Redis wake-up 仅降低领取延迟；通知丢失时 1-5 秒退避轮询继续恢复。大单元 2 集中 build 为 0 warning/0 error。
- 当前进入大单元 3：节点 live state、metric batch persistence 与 TeamLab flow stream/batch ingest。

### 2026-07-12 大单元 3-4 进度

- 大单元 3 已完成：节点身份/capability 与 live metric 分离；Agent 心跳携带单调 sequence 和 observed time；Redis latest hash 拒绝旧序列，metric stream 由固定 group 批量聚合为一分钟样本并写入 PostgreSQL。调度与节点管理读取 live state，Redis 缺失时回退 PostgreSQL checkpoint。
- TeamLab flow collector 已改为有界 Redis Stream + PostgreSQL binary COPY 批写；查询只读 PostgreSQL，consumer 支持 pending reclaim，写库后才 ACK，Redis 故障时进入 DropOldest 本地缓冲并记录 dropped telemetry。
- 大单元 3 集中验证通过：solution build 0 warning/0 error；节点、调度、heartbeat、flow fingerprint/batching/buffer 专项 59/59。
- 大单元 4 migration `CompletePhaseFiveRedisGovernance` 已生成并验证：新增 projection revision、节点分钟指标、live checkpoint、public port owner lease；历史公网容器、节点 checkpoint、scoreboard/theory/training revision 已回填。
- EF 模型一致性检查通过；隔离 PostgreSQL/Redis 集成验证 2/2，覆盖 migration 回填和跨 consumer pending reclaim。首次 Testcontainers 执行仅因 Docker Desktop 无法拉取 Ryuk 失败；使用已缓存镜像并禁用 Ryuk 后业务验证通过，容器由测试显式清理。
- 当前进入大单元 5：运维脚本、基准、全量门禁和独立质量审查。本阶段仍未部署或连接生产服务器。

### 2026-07-12 大单元 5 与开发闭环

- 运维交付已完成：Redis 部署/恢复 runbook、keyspace/TTL/stream pending 检查脚本、k6 合成负载和可复现基准记录模板已纳入仓库；Redis 检查脚本已在隔离 Redis 7 容器验证。
- 首轮独立质量审查发现 11 项；最终复核又识别出本地租约状态删除竞态、HybridCache 合并等待者的 factory 异常放大、`XAUTOCLAIM` 深层 pending 游标未推进三项更深并发边界。上述问题均已修复：本地 lease 状态按持有者和等待者引用计数，factory 异常向全部合并调用者传播且只执行一次，flow reclaim 按 consumer 保存 Redis `NextStartId`。
- `QueueManager` 现以 PostgreSQL 条件更新 CAS 领取 ticket，容量预留继续由 owner lease 保护；新增真实 PostgreSQL 双 worker 并发测试，证明同一 ticket 只有一个领取者。所有受分布式 lease 保护的长操作均把 `LeaseLost` 合并到业务取消令牌，cron job 也可在 leader lease 丢失时停止。
- 最终集中门禁通过：solution build `0 warning / 0 error`；单元测试 `508/508`；PostgreSQL/Redis 完整集成测试 `226/226`；前端 strict TypeScript、EF pending-model、旧 Redis/cache/lock 残留扫描和 `git diff --check` 全部通过。最终复核新增补丁另通过并发租约/缓存 `3/3` 和 TeamLab 深层 pending reclaim `2/2` 专项验证。
- Phase 5 代码开发完成，未部署、未连接或修改生产服务器。专用双主站环境的 k6 容量数字和 60 秒基础设施断网演练仍属于部署环境验收；本机未安装 k6，基准文档不得记录虚构结果，该项在预发布环境执行后补充证据。

---

## 0. 当前代码事实与冻结决策

- `AppBuilderExtensions` 同时配置 `IDistributedCache` 与 SignalR backplane；`RedisDistributedLock` 和 `PortAllocationService` 又分别同步 `ConnectionMultiplexer.Connect`，形成多连接、多故障语义和启动阻塞。
- `CacheHelper` 自行组合 L1/L2，`CacheMaker` 通过 Channel 后台生成 Scoreboard、RecentGames 和 GameList；invalidations 散落在 `FlagChecker`、AWDP、配置和 cron job 中。
- `RedisDistributedLock` 在未配置 Redis 时使用进程内 lock；这只适用于单实例开发。已配置但不可达会启动失败，lock 丢失后调用方没有可观察 lease-lost 状态。
- `PortAllocationService.ReleaseScript` 无 owner 校验，`ReserveExistingPortAsync` 可覆盖 owner；Redis 短暂错误后可能释放或覆盖其他实例端口租约。
- `CronJobService` 使用普通 distributed cache key 模拟 cron lock，缺少 owner token 和原子释放。
- `DeploymentQueueTicket`、`ApiOperation` 和 runtime 是 PostgreSQL 事实。Phase 5 只增加 wake-up，不得创建 Redis queue 或用 Redis list/stream 替换现有队列。
- TeamLab flow 当前逐样本查重并同步落库；Phase 4 已提供 fingerprint、时间分区和聚合表，Phase 5 才能安全改为 stream + batch persistence。
- 节点 capabilities/version/schedulable 是持久事实；CPU、内存、slots、heartbeat freshness 是 live state。两类数据必须分开，不能因为 Redis 清空而丢失节点注册信息。
- 使用 `Microsoft.Extensions.Caching.Hybrid` 替换自研 `CacheMaker` 的 L1/L2 和 stampede control，不再维护平行缓存框架。
- 不引入专用消息中间件、不引入 Redis Cluster 专属协议、不引入 RedLock 多 Redis 部署。当前单 Redis 的互斥安全依赖 owner token、续租可见性和 PostgreSQL 唯一约束；Phase 14 压测不达标时再评估基础设施升级。

## 1. Redis 用途与故障语义

| 用途 | 数据 | Redis 不可用 | PostgreSQL 关系 |
| --- | --- | --- | --- |
| cache | Scoreboard、配置、比赛列表、课程/理论统计 | bypass cache，直接查事实 | PostgreSQL 是事实 |
| lock | 短事务协调、governance 之外的跨实例互斥 | distributed mode fail closed；single-node dev 可 local | 数据库约束仍是最终保护 |
| lease | Nginx 公网端口、短期资源占用 | fail closed，不做跨实例本地扫描 | 容器/映射事实用于 reconcile |
| stream | TeamLab flow、节点 metric samples | 有界 local buffer；flow 可丢弃并计数，node metrics 直接批写 | consumer 批量落库 |
| SignalR | 跨主站实例推送 | 单实例连接仍工作，多实例 readiness degraded | 不保存业务事实 |
| wake-up | 部署队列新 ticket 通知 | PostgreSQL polling 继续工作 | ticket 始终在 PostgreSQL |

## Task 1: 建立 Redis 治理基线、失败测试和失效矩阵

**Files:**
- Create: `src/GZCTF.Test/UnitTests/Cache/RedisUsageBoundaryTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Cache/RedisFailureModeTests.cs`
- Create: `src/GZCTF.Integration.Test/Base/RedisIntegrationFixture.cs`
- Modify: `src/Directory.Packages.props`
- Modify: `src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj`
- Create: `docs/commercialization/cache-invalidation-map.md`
- Modify: `docs/platform-commercialization-audit-progress.md`

- [ ] **Step 1: 增加 Redis Testcontainer**

在集中包管理增加 `Testcontainers.Redis` 4.11.0，fixture 使用固定 test key prefix 和独立 database number；每个测试清理自身 prefix，不执行无边界 `FLUSHALL`。

- [ ] **Step 2: 写单连接和禁止旁路失败测试**

架构测试扫描生产程序集，除 `RedisConnectionProvider` 和 SignalR registration 外禁止调用 `ConnectionMultiplexer.Connect/ConnectAsync`；禁止业务模块直接拼接 `gzctf:` key；禁止 `DeploymentQueueService` 使用 Redis list/stream 作为 ticket store。

测试逐行读取生产 C# 文件，定位 `ConnectionMultiplexer.Connect`、`ConnectAsync` 和直接 `GetDatabase` 调用；只允许 `Infrastructure/Cache/RedisConnectionProvider.cs` 和框架 registration adapter 命中。测试 helper 在同一测试文件中解析 repository root、排除 `bin/obj`，忽略注释后输出命中文件和行号，不增加 Roslyn 依赖或未定义的 IL helper。

- [ ] **Step 3: 写故障语义失败测试**

覆盖：cache failure 旁路到数据库；distributed lock/port lease 在 production distributed mode fail closed；queue wake-up 失败后 polling 仍领取 PostgreSQL ticket；SignalR backplane 失败呈 degraded；flow local buffer 满后只丢 telemetry 并递增 dropped metric。

- [ ] **Step 4: 冻结失效矩阵**

`cache-invalidation-map.md` 必须列出每个缓存 projection 的 key dimensions、revision owner、TTL、最大可接受陈旧时间、所有 mutation trigger、Redis 故障动作和测试。未登记的缓存不得上线。

- [ ] **Step 5: 运行失败测试并提交**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~RedisUsageBoundaryTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~RedisFailureModeTests
git add src/Directory.Packages.props src/GZCTF.Test src/GZCTF.Integration.Test docs/commercialization/cache-invalidation-map.md docs/platform-commercialization-audit-progress.md
git commit -m "test: establish redis governance contracts"
```

Expected: 新 provider、typed cache 和故障策略尚未实现的断言失败；Redis/PostgreSQL fixtures 本身健康。

## Task 2: 建立单一 Redis connection、用途隔离和 health model

**Files:**
- Create: `src/GZCTF/Infrastructure/Cache/RedisRuntimeOptions.cs`
- Create: `src/GZCTF/Infrastructure/Cache/RedisConnectionProvider.cs`
- Create: `src/GZCTF/Infrastructure/Cache/RedisKeyspace.cs`
- Create: `src/GZCTF/Infrastructure/Cache/RedisRuntimeState.cs`
- Create: `src/GZCTF/Infrastructure/Cache/RedisHealthCheck.cs`
- Create: `src/GZCTF/Infrastructure/Cache/RedisTelemetry.cs`
- Modify: `src/GZCTF/Extensions/Startup/AppBuilderExtensions.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Modify: `src/GZCTF/Extensions/Startup/TelemetryExtension.cs`
- Modify: `src/GZCTF/Models/Internal/Configs.cs`
- Modify: `src/GZCTF.Test/UnitTests/Cache/RedisUsageBoundaryTests.cs`

- [ ] **Step 1: 写 options 验证测试**

`Mode` 固定为 `Disabled | SingleInstance | Distributed`。Distributed 要求连接串、key prefix、connect timeout 和 operation timeout；prefix 只允许小写字母、数字、短横线，禁止空格和 `{}`。生产多主站实例不得使用 SingleInstance。

- [ ] **Step 2: 实现异步单例 provider**

```csharp
public interface IRedisConnectionProvider
{
    bool IsConfigured { get; }
    RedisRuntimeMode Mode { get; }
    ValueTask<IConnectionMultiplexer?> GetAsync(CancellationToken token);
}
```

provider 使用 `Lazy<Task<IConnectionMultiplexer>>` 和 `ConnectAsync`，设置 `AbortOnConnectFail=false`、有限 connect/async timeout、client name 和 reconnect event；DI 负责 async disposal。不得在构造函数同步连接网络。

- [ ] **Step 3: 建立版本化 keyspace**

所有 key 通过 `RedisKeyspace` 生成，格式固定为 `<prefix>:v1:<purpose>:<resource>`。同一 runtime/game 需要 Lua 原子操作时使用明确 hash tag，例如 `gzctf:v1:lease:port:{public}:30042`；不得把用户名、team name、token、Flag 或 IP 明文放进 key。

- [ ] **Step 4: 让 distributed cache、SignalR、lock、lease 共用 provider**

`AddStackExchangeRedisCache` 和 SignalR Redis options 使用 provider 创建的 connection；如框架 registration 不能直接复用实例，只允许由 `RedisConnectionProvider` 暴露 connection factory，禁止再次解析连接串。Redis disabled 时注册 memory-only HybridCache secondary omission，SignalR 不注册 backplane。

- [ ] **Step 5: 实现 health 和 metrics**

health 分别报告 connection、cache、backplane、stream consumer lag；SingleInstance 开发缺 Redis 为 healthy，Distributed 连接不可达为 readiness unhealthy。metrics labels 只允许 purpose/status，不使用 key 或资源 ID。

- [ ] **Step 6: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~RedisUsageBoundaryTests
git add src/GZCTF/Infrastructure/Cache src/GZCTF/Extensions/Startup src/GZCTF/Models/Internal/Configs.cs src/GZCTF.Test
git commit -m "refactor: centralize redis runtime connection"
```

Expected: PASS；生产代码只有 provider 持有连接创建职责。

## Task 3: 用 HybridCache 和 typed policy 替换 CacheMaker

**Files:**
- Modify: `src/Directory.Packages.props`
- Modify: `src/GZCTF/GZCTF.csproj`
- Create: `src/GZCTF/Infrastructure/Cache/CachePolicy.cs`
- Create: `src/GZCTF/Infrastructure/Cache/CachePolicyCatalog.cs`
- Create: `src/GZCTF/Infrastructure/Cache/PlatformCache.cs`
- Create: `src/GZCTF/Infrastructure/Cache/ProjectionRevision.cs`
- Create: `src/GZCTF/Infrastructure/Cache/ProjectionRevisionStore.cs`
- Create: `src/GZCTF/Infrastructure/Persistence/ProjectionRevisionEntityConfiguration.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Delete: `src/GZCTF/Services/Cache/CacheMaker.cs`
- Delete: `src/GZCTF/Services/Cache/Handlers/ScoreboardCacheHandler.cs`
- Delete: `src/GZCTF/Services/Cache/Handlers/GameListCacheHandler.cs`
- Delete: `src/GZCTF/Services/Cache/Handlers/RecentGamesCacheHandler.cs`
- Modify: `src/GZCTF/Services/Cache/CacheHelper.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Create: `src/GZCTF.Test/UnitTests/Cache/CachePolicyCatalogTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Cache/ProjectionRevisionCacheTests.cs`

- [ ] **Step 1: 增加 HybridCache 并写 policy 失败测试**

集中包版本使用 `Microsoft.Extensions.Caching.Hybrid` 10.0.5。测试要求每个 policy 有固定 name、schema version、local TTL、distributed TTL、maximum stale、size limit 和 consistency mode；distributed TTL 必须大于等于 local TTL。

- [ ] **Step 2: 实现 typed cache facade**

```csharp
public interface IPlatformCache
{
    ValueTask<T> GetOrCreateAsync<T>(
        CachePolicy policy,
        string resourceKey,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken token);

    ValueTask RemoveAsync(CachePolicy policy, string resourceKey, CancellationToken token);
}
```

`PlatformCache` 统一 schema-version key、HybridCache 调用、OpenTelemetry、Redis circuit state 和 bypass。业务代码不能接收 `IDistributedCache`、`IDatabase` 或 HybridCache。

- [ ] **Step 3: 实现数据库 projection revision**

`ProjectionRevision` 复合主键为 `(Projection, ResourceKey)`，字段为 `Version bigint, UpdatedAt`。mutation 在同一 PostgreSQL transaction 使用 upsert 增加 version；高一致性 cache key 包含读取到的 version。Redis invalidation 失败不会命中旧 key，代价只是一次新的 cache fill。

- [ ] **Step 4: 保留薄兼容面并在本任务内删完旧框架**

先让 `CacheHelper` 内部委托 `IPlatformCache`，迁移全部调用方后删除 channel writer、CacheRequest、CacheKey.UpdateLock、LastUpdateTime 和 hosted CacheMaker registration。任务提交前不得同时保留两套 cache generation 路径。

- [ ] **Step 5: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~CachePolicyCatalogTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~ProjectionRevisionCacheTests
git add src/Directory.Packages.props src/GZCTF src/GZCTF.Test src/GZCTF.Integration.Test
git commit -m "refactor: replace custom cache maker with hybrid cache"
```

Expected: PASS；`CacheMaker` 和 handler 目录不存在，旧 cache request channel 不再注册。

## Task 4: 接入排行榜、配置、课程和理论 projection revision

**Files:**
- Create: `src/GZCTF/Modules/Ctf/Application/ScoreboardProjectionService.cs`
- Create: `src/GZCTF/Modules/Training/Application/TrainingStatisticsProjectionService.cs`
- Create: `src/GZCTF/Modules/Theory/Application/TheoryStatisticsProjectionService.cs`
- Create: `src/GZCTF/Modules/Content/Application/PublicCatalogProjectionService.cs`
- Modify: `src/GZCTF/Repositories/SubmissionRepository.cs`
- Modify: `src/GZCTF/Repositories/ParticipationRepository.cs`
- Modify: `src/GZCTF/Repositories/TeamRepository.cs`
- Modify: `src/GZCTF/Services/FlagChecker.cs`
- Modify: `src/GZCTF/Services/AwdpRoundService.cs`
- Modify: `src/GZCTF/Services/AwdpCheckerService.cs`
- Modify: `src/GZCTF/Services/Config/ConfigService.cs`
- Modify: `src/GZCTF/Controllers/TrainingCourseController.cs`
- Modify: `src/GZCTF/Controllers/TheoryPlayerController.cs`
- Modify: `src/GZCTF/Extensions/HandlerExtension.cs`
- Modify: `src/GZCTF/Services/CronJob/RuntimeCronJobs.cs`
- Delete: `src/GZCTF/Services/Cache/CacheHelper.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Cache/CacheInvalidationFlowTests.cs`

- [ ] **Step 1: 写跨 mutation 失效失败测试**

覆盖下列完整链路：正确/错误提交、人工审核改分、队伍改名、Participation division/status 变化、比赛状态和 scoring rule 修改、AWDP round/checker 变化、课程进度/题目绑定变化、理论 draft/submit/recalculate、平台配置更新。每个 mutation 提交后读取 projection，必须得到新 revision 和新事实。

- [ ] **Step 2: 固定 projection policy**

- Scoreboard：revision-consistent，L1 2 秒，L2 30 秒；revision 由 game 维度拥有。
- ClientConfig/Index/Favicon：tag invalidation，L1 30 秒，L2 10 分钟。
- GameList/RecentGames：tag invalidation，L1 5 秒，L2 60 秒。
- TrainingStatistics：revision-consistent，L1 5 秒，L2 60 秒；resource 为 course ID。
- TheoryStatistics：revision-consistent，L1 5 秒，L2 60 秒；resource 为 paper/game ID。

- [ ] **Step 3: mutation 与 revision 同事务**

Submission/score/participation/AWDP mutation 在保存事实的同一 transaction bump game scoreboard revision；队伍改名查询该队伍全部 Participation 的 distinct GameId 并批量 bump。课程和理论写入分别 bump course/paper projection。禁止先删除 Redis key 再提交数据库。

- [ ] **Step 4: 删除 CacheHelper 最后调用和 cron refresh**

读取路径直接调用 projection service；不再定时全量预热全部 scoreboard。比赛进行中由第一次并发请求触发 HybridCache 单航班生成；关键 mutation 可在提交后发 best-effort warm-up，但 warm-up 失败不影响业务响应。

- [ ] **Step 5: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~CacheInvalidationFlowTests
rg -n "CacheHelper|CacheMaker|CacheRequest|CacheKey\.UpdateLock|CacheKey\.LastUpdateTime" src/GZCTF --glob '*.cs'
git add src/GZCTF src/GZCTF.Integration.Test
git commit -m "feat: make cached projections revision consistent"
```

Expected: 测试 PASS；`rg` 无生产代码命中。

## Task 5: 重写分布式 lock 与公网端口 owner lease

**Files:**
- Create: `src/GZCTF/Infrastructure/Concurrency/IDistributedLease.cs`
- Create: `src/GZCTF/Infrastructure/Concurrency/IDistributedLeaseProvider.cs`
- Create: `src/GZCTF/Infrastructure/Concurrency/RedisDistributedLeaseProvider.cs`
- Create: `src/GZCTF/Infrastructure/Concurrency/LocalDevelopmentLeaseProvider.cs`
- Delete: `src/GZCTF/Services/Fleet/RedisDistributedLock.cs`
- Delete: `src/GZCTF/Services/Concurrency/IDistributedLockService.cs`
- Modify: `src/GZCTF/Services/Fleet/PortAllocationService.cs`
- Modify: `src/GZCTF/Services/Fleet/IPortAllocationService.cs`
- Modify: `src/GZCTF/Services/Fleet/PortLeaseRefreshService.cs`
- Modify: `src/GZCTF/Services/CronJob/CronJobService.cs`
- Modify: `src/GZCTF/Services/Fleet/QueueManager.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Create: `src/GZCTF.Test/UnitTests/Concurrency/DistributedLeaseTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Cache/PortLeaseOwnershipTests.cs`

- [ ] **Step 1: 写 owner 安全失败测试**

覆盖：错误 owner 不能 release/refresh；旧 lease 过期后原 owner 不能删除新 owner lease；续租失败设置 `LeaseLost`；Distributed 模式断开 Redis 后 acquire 失败；SingleInstance 模式可以使用 local provider。

- [ ] **Step 2: 定义 async lease 合约**

```csharp
public interface IDistributedLease : IAsyncDisposable
{
    string Resource { get; }
    string OwnerToken { get; }
    CancellationToken LeaseLost { get; }
    ValueTask<bool> RenewAsync(CancellationToken token);
}
```

lease acquire 使用 `SET key owner NX PX`；renew/release 使用 compare-owner Lua。调用方不得持有 lease 执行 VM 镜像下载、容器创建或其他分钟级网络操作。

- [ ] **Step 3: 端口 lease 使用不可猜 owner token**

allocate 返回 `PortLease(Port, OwnerToken, ExpiresAt)`；容器映射事实保存 owner token 的 SHA-256 或独立 lease ID，不保存可复用明文 token。release 和 reconcile 必须同时匹配 port 与 owner。`ReserveExistingPortAsync` 改为 compare-or-create，存在其他 owner 时报告冲突并 fail closed。

- [ ] **Step 4: cron、容量预留与队列领取使用正确并发合约**

cron 和容量预留使用 owner lease provider，lease 丢失时取消受保护操作。QueueManager 不使用粗粒度全局锁，ticket 通过 PostgreSQL `Pending -> Assigned` 条件更新 CAS 原子领取；领取者中断后由超时 claim 回收，下次 polling 恢复。

- [ ] **Step 5: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~DistributedLeaseTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~PortLeaseOwnershipTests
git add src/GZCTF src/GZCTF.Test src/GZCTF.Integration.Test
git commit -m "fix: enforce ownership for redis leases"
```

Expected: PASS；无 owner 的 Redis DEL/SET overwrite 路径不存在。

## Task 6: 建立节点 live state 与 metrics 批量落库

**Files:**
- Create: `src/GZCTF/Modules/Runtime/Domain/WorkerNodeMetricSample.cs`
- Create: `src/GZCTF/Modules/Runtime/Contracts/NodeLiveStateContracts.cs`
- Create: `src/GZCTF/Modules/Runtime/Application/INodeLiveStateStore.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/RedisNodeLiveStateStore.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/PostgresNodeLiveStateFallback.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/NodeMetricPersistenceWorker.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/Persistence/WorkerNodeMetricEntityConfiguration.cs`
- Modify: `src/GZCTF/Infrastructure/Persistence/Governance/DataRetentionPolicyCatalog.cs`
- Modify: `docs/commercialization/database-index-and-lifecycle-audit.md`
- Modify: `src/GZCTF/Controllers/NodesController.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/NodeTunnelService.cs`
- Modify: `src/GZCTF/Services/Fleet/LocalNodeMetricsService.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Modify: `src/GZCTF/Composition/ModuleRegistration.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Cache/NodeLiveStateBufferTests.cs`

- [ ] **Step 1: 写 capability 与 live metric 分离失败测试**

注册/版本/capabilities/schedulable 变化立即写 WorkerNode；普通 heartbeat 的 CPU、内存、Docker/VM slots 写 latest hash 和 metric stream。Redis 清空后节点身份仍存在；Redis 不可用时 heartbeat 通过 bounded batch fallback 更新 PostgreSQL，不逐请求创建 DbContext transaction 风暴。

- [ ] **Step 2: 定义 live state TTL 和序列**

每个 heartbeat 携带单调 `Sequence` 和 Agent `ObservedAt`；store 只接受更大的 sequence。latest hash TTL 为 heartbeat interval 的 4 倍，liveness 判断同时使用 server receive time，禁止仅信任 Agent 时钟。

- [ ] **Step 3: 批量持久化 metrics**

worker 每 2 秒或 500 samples 批量 upsert 最新 WorkerNode checkpoint，并写 1 分钟 `WorkerNodeMetricSample` 聚合；同一 node/window 只保留一个事实。该数据集加入 Phase 4 retention catalog，默认保留 180 天，不保存每次 heartbeat 原始样本。capability 变化绕过缓冲立即持久化并发审计事件。

- [ ] **Step 4: 定义降级读取**

调度 query 首先读 Redis latest；Redis 不可用或 key 缺失时读 PostgreSQL checkpoint，并依据 `ReceivedAt` 判断 stale。stale 节点不可调度但节点注册记录不删除。Phase 6 只能依赖 `INodeLiveStateStore`，不得自行读 Redis。

- [ ] **Step 5: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~NodeLiveStateBufferTests
git add src/GZCTF/Modules/Runtime src/GZCTF/Controllers/NodesController.cs src/GZCTF/Services src/GZCTF/Models/AppDbContext.cs src/GZCTF/Composition src/GZCTF.Integration.Test
git commit -m "feat: buffer node live state and metrics"
```

Expected: PASS；节点身份与实时指标的事实边界清晰。

## Task 7: 将 TeamLab flow 改为 Redis Stream + PostgreSQL 批写

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Application/ITeamLabTrafficIngestor.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/RedisTeamLabTrafficIngestor.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabTrafficLocalBuffer.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabTrafficPersistenceWorker.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/PostgresTeamLabTrafficBatchWriter.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficFlowService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- Modify: `src/GZCTF/Composition/ModuleRegistration.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabTrafficFingerprintTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Cache/TeamLabTrafficStreamTests.cs`

- [ ] **Step 1: 写去重、pending recovery 和 bounded fallback 失败测试**

覆盖：同一 fingerprint 重放只落一条；consumer 在写库后、ACK 前崩溃可重领 pending 且不重复；不同 generation 不去重；Redis 断开时 local channel 有界；channel 满时丢最旧 telemetry 并记录 runtime/network dropped count，不阻塞环境运行。

- [ ] **Step 2: 定义 stream envelope**

字段固定为 `schemaVersion, runtimeId, generation, shardId, networkId, workerNodeId, capturedAt, fingerprint, sourceIp, sourcePort, destinationIp, destinationPort, protocol, packets, bytes`。单条不得含 payload；批量 ingest 限制 1000 samples 和 1 MiB，超过返回分批结果而不是分配无限内存。

- [ ] **Step 3: 实现 consumer group**

stream 使用固定 consumer group；worker 优先 reclaim 超时 pending，并为 pending 预留批次配额，避免持续新流量导致旧消息饥饿。每批最多 1000 条或等待 200ms；成功批写后 ACK，失败保留 pending 并指数退避。stream 使用近似 MAXLEN 保护内存，trim 前保证 pending 不被删除。

- [ ] **Step 4: 实现 PostgreSQL 批写**

batch writer 使用 Npgsql binary COPY 到 session temp staging table，再执行 `INSERT ... ON CONFLICT DO NOTHING` 写 Phase 4 分区父表；同一 transaction 完成。禁止每个 sample `Any` 或 `SaveChanges`。日志只记录 batch count、runtime 和错误码，不记录 IP 明细数组。

- [ ] **Step 5: 查询只读 PostgreSQL 事实**

近期 flow API 继续读取 PostgreSQL partition/aggregate；允许显示最大 2 秒 ingest lag，不从 Redis stream 拼接临时结果，避免两个来源排序和去重。

- [ ] **Step 6: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter FullyQualifiedName~TeamLabTrafficFingerprintTests
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~TeamLabTrafficStreamTests
git add src/GZCTF/Modules/TeamLab src/GZCTF/Composition src/GZCTF.Test src/GZCTF.Integration.Test
git commit -m "perf: batch teamlab traffic through redis stream"
```

Expected: PASS；生产 flow ingest 无逐样本数据库查询。

## Task 8: 增加 PostgreSQL queue wake-up 并整合 SignalR backplane

**Files:**
- Create: `src/GZCTF/Modules/Runtime/Application/IDeploymentQueueWakeup.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/RedisDeploymentQueueWakeup.cs`
- Create: `src/GZCTF/Modules/Runtime/Infrastructure/PollingDeploymentQueueWakeup.cs`
- Modify: `src/GZCTF/Services/Fleet/DeploymentQueueService.cs`
- Modify: `src/GZCTF/Services/Fleet/QueueProcessingService.cs`
- Modify: `src/GZCTF/Extensions/Startup/AppBuilderExtensions.cs`
- Modify: `src/GZCTF/Composition/ModuleRegistration.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Cache/DeploymentQueueWakeupTests.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Cache/SignalRBackplaneTests.cs`

- [ ] **Step 1: 写通知丢失仍恢复的失败测试**

创建 PostgreSQL ticket 后模拟 Redis publish 丢失，processor 必须在 polling 上限内领取；重复通知不得重复执行 ticket；两个主站实例只允许一个 claim 成功。

- [ ] **Step 2: 实现 wake-up hint**

ticket transaction 提交后 publish 固定小消息 `{ ticketId }`；processor 收到只触发一次 claim loop，不信任消息 payload 的状态/节点/资源。无消息时每 1 秒 polling；连续空轮询指数退避到 5 秒，收到通知立即复位。

- [ ] **Step 3: 验证 SignalR 跨实例**

两个 test server 共用 Redis backplane，一个实例发布比赛/队列事件，另一个实例连接的 client 收到一次。Redis 断开时本实例 client 仍能收到本地事件，readiness 报告 backplane degraded。

- [ ] **Step 4: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter "FullyQualifiedName~DeploymentQueueWakeupTests|FullyQualifiedName~SignalRBackplaneTests"
git add src/GZCTF/Modules/Runtime src/GZCTF/Services/Fleet src/GZCTF/Extensions/Startup/AppBuilderExtensions.cs src/GZCTF/Composition src/GZCTF.Integration.Test
git commit -m "perf: wake postgres deployment queue through redis"
```

Expected: PASS；Redis 只降低领取延迟，不决定 ticket 真相。

## Task 9: 编写 projection revision 与 metric schema migration

**Files:**
- Create: `src/GZCTF/Migrations/20260710170000_AddRedisGovernanceFacts.cs`
- Create: `src/GZCTF/Migrations/20260710170000_AddRedisGovernanceFacts.Designer.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Create: `src/GZCTF.Integration.Test/Tests/Database/RedisGovernanceMigrationTests.cs`
- Create: `docs/commercialization/runbooks/redis-deployment-and-recovery.md`

- [ ] **Step 1: 写迁移和 Redis 清空恢复失败测试**

从 Phase 4 latest schema 播种比赛、课程、理论、节点和 runtime，迁移后验证 revision 初值、metric relation 和索引；随后清空测试 Redis，重启主站，验证业务事实、队列、节点注册和 runtime 仍可恢复。

- [ ] **Step 2: 迁移 revision 与 metric facts**

创建 `ProjectionRevisions`、`WorkerNodeMetricSamples` 和必要索引；为已有 active game/course/paper 建立 version 1。Redis 本身无 schema migration，key prefix 的 `v1` 是协议版本；升级时新旧 prefix 不双写，部署切换后旧 prefix 由 TTL 自然过期或受限清理脚本删除。

- [ ] **Step 3: 编写运维手册**

包含 Redis 规格、持久化模式、memory policy、TLS/ACL、备份边界、keyspace 版本、stream lag、consumer pending、连接故障、清空恢复、滚动升级和回退。明确 Redis 备份不能替代 PostgreSQL 备份。

- [ ] **Step 4: 运行测试并提交**

```powershell
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter FullyQualifiedName~RedisGovernanceMigrationTests
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj
git add src/GZCTF/Migrations src/GZCTF.Integration.Test/Tests/Database/RedisGovernanceMigrationTests.cs docs/commercialization/runbooks/redis-deployment-and-recovery.md
git commit -m "feat: persist redis coordination facts"
```

Expected: PASS；输出 `No changes have been made to the model since the last migration.`。

## Task 10: 执行并发、故障和内存基准

**Files:**
- Create: `scripts/load/phase-05-redis-load.js`
- Create: `scripts/redis/inspect-keyspace.ps1`
- Create: `scripts/redis/assert-stream-health.ps1`
- Create: `docs/commercialization/benchmarks/phase-05-redis-baseline.md`
- Modify: `.github/workflows/quality.yml`

- [ ] **Step 1: 建立 k6 workload**

workload 同时模拟 500 支队伍读取排行榜、1000 个节点 heartbeat、每秒 2 万条 flow sample、并发提交导致 revision bump、部署 ticket wake-up。使用合成数据和专用环境，不连接生产。

- [ ] **Step 2: 固定功能性门槛**

- 排行榜 factory 每个 `(game, revision)` 并发窗口只执行一次。
- Redis 正常时 node heartbeat 不逐条写 PostgreSQL。
- flow consumer 稳态 lag 小于 2 秒，数据库 batch 平均大于 200 条。
- 重启 consumer 后 pending 全部恢复，无 fingerprint 重复。
- Redis 中断 60 秒期间部署 ticket 不丢失，cache 读取旁路，端口新分配 fail closed。
- 恢复 Redis 后不发生 reconnect storm，stream backlog 在 5 分钟内清空。

- [ ] **Step 3: 检查 keyspace 和内存上限**

脚本按 purpose 输出 key count、TTL 覆盖率、memory bytes、stream length/pending；禁止输出 key 中的资源明文。所有 cache key 必须有 TTL；lease key 必须有 TTL；stream 必须有 MAXLEN；无 TTL 的 Redis 事实 key 视为失败。

- [ ] **Step 4: 运行基准并提交**

```powershell
k6 run scripts/load/phase-05-redis-load.js
pwsh scripts/redis/inspect-keyspace.ps1 -ConnectionString $env:GZCTF_BENCHMARK_REDIS
pwsh scripts/redis/assert-stream-health.ps1 -ConnectionString $env:GZCTF_BENCHMARK_REDIS
git add scripts/load scripts/redis docs/commercialization/benchmarks/phase-05-redis-baseline.md .github/workflows/quality.yml
git commit -m "test: benchmark redis runtime governance"
```

Expected: 功能门槛全部通过；文档记录主站实例数、Redis 配置、PostgreSQL 配置、硬件和测试数据量。

## Task 11: Phase 5 全量验收与退出

**Files:**
- Modify: `docs/commercialization/cache-invalidation-map.md`
- Modify: `docs/platform-commercialization-audit-progress.md`
- Modify: `docs/commercialization/runbooks/redis-deployment-and-recovery.md`

- [ ] **Step 1: 运行全量自动检查**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj
pnpm --dir src/GZCTF/ClientApp check
rg -n "ConnectionMultiplexer\.(Connect|ConnectAsync)|CacheMaker|CacheRequest|redis\.call\('DEL'" src/GZCTF --glob '*.cs'
git diff --check
```

Expected: 测试和检查退出码为 0；连接创建只命中 provider，Lua DEL 均有 owner 比较或是受限 cache 清理。

- [ ] **Step 2: 真实故障演练**

在双主站、独立 Redis、PostgreSQL 环境执行：Redis 断网 60 秒、Redis 重启、consumer 重启、主站滚动重启、stream pending reclaim、端口 lease 过期、SignalR backplane 恢复。确认业务事实不丢失、端口不重复、queue 不重复执行、缓存不返回跨 revision 结果。

- [ ] **Step 3: 做阶段双重审查**

规格审查逐项对照总纲 Phase 5、失效矩阵和本计划；代码质量审查重点检查直接 Redis 访问、无 TTL key、无限 stream、缓存双轨、无 owner lease、逐事件数据库写、Redis queue 旁路和高基数 metrics。发现项全部修复并重跑相关门禁。

- [ ] **Step 4: 更新进度并提交**

```powershell
git add docs/commercialization docs/platform-commercialization-audit-progress.md
git commit -m "docs: complete phase 5 redis governance"
```

## Phase 5 退出门槛

- 全站 Redis 连接生命周期由一个 provider 管理，无同步构造连接和业务旁路。
- 缓存、lock、lease、stream、SignalR、queue wake-up 有独立 keyspace、TTL 和故障语义。
- `CacheMaker`、旧 cache request channel、散落 invalidation 和旧 `RedisDistributedLock` 已删除。
- 排行榜、课程统计和理论统计三类高一致性 projection 使用 PostgreSQL revision，Redis 失效失败不会命中旧事实。
- 公网端口和分布式 lease 使用 owner compare renew/release；Redis 不可用时不回退到跨实例不安全路径。
- 节点 live state 与持久 capabilities 分离，Redis 清空不丢节点注册。
- TeamLab flow 通过有界 stream/batch 落库，查询只读 PostgreSQL，生产链路无逐样本 `Any/SaveChanges`。
- 部署队列仍以 PostgreSQL ticket 为唯一事实，Redis 只提供可丢失 wake-up。
- 真实断网、重启、pending recovery 和双实例 SignalR 演练通过。
- Phase 6 可只依赖 `INodeLiveStateStore`、`IDistributedLeaseProvider` 和 PostgreSQL queue contracts 开始调度重构，不直接操作 Redis。
