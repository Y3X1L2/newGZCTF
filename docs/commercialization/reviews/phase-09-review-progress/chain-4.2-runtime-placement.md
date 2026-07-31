# 链路 4.2 Runtime 创建/排队/物理放置 审查结果

- 审查人：独立代码审查 sub-agent
- 审查日期：2026-07-21
- 规范文档：`docs/commercialization/phase-09-teamlab-networking-independent-code-review.md` 第 4.2 节
- 代码仓库：`D:/newgz/newGZCTF-main/`（.NET / C#）

## 审查范围与覆盖

本链路覆盖 Runtime 从 API 入口到物理放置完成的全过程，包含以下已实际打开并阅读的文件：

### 核心创建/排队/放置文件
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimePlanner.cs`（CreateAsync、CreatePlannedRuntimeAsync、ResetAsync 主体）
- `src/GZCTF/Modules/TeamLab/Infrastructure/EfTeamLabRuntimeOperationSubmissionStore.cs`（SubmitAsync 含 advisory lock 与幂等回退）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOperationApplicationService.cs`（SubmitAsync 计算 RequestHash、ResolveResource）
- `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs`（ExecuteAsync 链接 operation↔ticket、WaitForTicketAsync）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`（PlanAndEnqueueAsync、ResetAndEnqueueAsync、ExecuteQueuedResetAsync 检查点状态机）
- `src/GZCTF/Modules/Runtime/Application/TeamLabPhysicalPlacementService.cs`（BindAndReserveAsync、Place 算法、ApplyCompletedGenerationCredits）
- `src/GZCTF/Modules/Runtime/Application/RuntimeSchedulingService.cs`（TryClaimAsync、ReserveCapacityAsync 路由）
- `src/GZCTF/Modules/Runtime/Application/RuntimeExecutionService.cs`（ExecuteAsync、ConfirmCapacityAsync）
- `src/GZCTF/Modules/Runtime/Application/RuntimeQueueSelector.cs`（公平调度、per-owner 并发限制、subject 过滤）
- `src/GZCTF/Modules/Runtime/Application/NodeCapacitySnapshotService.cs`（容量快照聚合）
- `src/GZCTF/Modules/Runtime/Application/NodeEligibilityEvaluator.cs`（Docker/KVM 独立能力检查）

### 排队与容量基础设施
- `src/GZCTF/Services/Fleet/DeploymentQueueService.cs`（EnqueueAsync 含 `pg_advisory_xact_lock` 与 subject 复用）
- `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs`（TryReserveAsync / TryReserveBatchAsync / ConfirmAsync / ReleaseAsync）
- `src/GZCTF/Services/Fleet/TeamLabCapacityFacts.cs`（按 generation 聚合 shard/asset）
- `src/GZCTF/Infrastructure/Concurrency/RedisDistributedLeaseProvider.cs`（`fleet:scheduler` 租约自动续约）
- `src/GZCTF/Models/Data/DeploymentQueueTicket.cs`（BuildSubjectConcurrencyKey、ActiveIdentity）

### 实体与配置
- `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeAggregate.cs`（Runtime/Shard/Network/Asset）
- `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRuntimeEntityConfigurations.cs`（`(CreatedById, ExternalReference)` 唯一索引，过滤 `ExternalReference IS NOT NULL`）

### 辅助文件
- `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs`、`TeamLabAdminRuntimeController.cs`（API 入口，要求 `Idempotency-Key`）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`（DAG 并行部署、失败传播）
- `src/GZCTF/Modules/Runtime/Application/RuntimeAdmissionPolicy.cs`、`NodeDispatchLimiter.cs`
- `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeSchedulingWorker.cs`、`RuntimeExecutionWorker.cs`
- `src/GZCTF/Modules/Runtime/Infrastructure/RedisDeploymentQueueWakeup.cs`、`PollingDeploymentQueueWakeup.cs`
- `src/GZCTF/Models/Data/WorkerNode.cs`、`FleetCapacityReservation.cs`

覆盖结论：第 4.2 节列出的所有不变量、第 6 节并发矩阵四象限均已通过实际阅读源码进行验证。

## Findings 汇总

| 编号 | 等级 | 标题 | 位置 |
|------|------|------|------|
| 4.2.1 | P2 | 同 externalReference、不同 idempotency key 的并发 Create 触发 500 而非稳定 409/复用 | `TeamLabRuntimePlanner.cs` L167-L172；`EfTeamLabRuntimeOperationSubmissionStore.cs` L23-L29 |
| 4.2.2 | P2 | TeamLab 部署期间容量被双重计数（Active 预留 + teamLabFacts），导致不必要的跨节点放置/等待 | `NodeCapacitySnapshotService.cs` L20、L63-L81；`RuntimeExecutionService.cs` L220-L221 |

---

### Finding 4.2.1 — 同 externalReference 并发 Create 抛 500 而非稳定 409/复用

**等级**：P2（设计正确性：API 应在并发情况下返回稳定语义）

**违反的不变量**：第 5 节"API idempotency key/operation/ticket/runtime 对应关系"——相同业务意图的并发提交应在数据库唯一约束保护下产生稳定结果（409 冲突或复用现有 runtime），不应以未捕获的 `DbUpdateException` 形式上抛为 500。

**根因（双重缺陷）**：

1. `EfTeamLabRuntimeOperationSubmissionStore.SubmitAsync` 仅在 `ResourceId: not null` 时获取 advisory lock。Create 操作 `ResolveResource` 返回 `ResourceId = null`，因此两个并发 Create **不获取任何行级锁**，`FindExistingAsync` 双重检查也无法阻拦对方：
   ```
   EfTeamLabRuntimeOperationSubmissionStore.cs L23-L29:
   if (transaction is not null && context.Database.IsNpgsql() && submission is
       { ResourceType: "teamlab-runtime", ResourceId: not null })
   {
       var resourceLock = $"{submission.ResourceType}:{submission.ResourceId}";
       await context.Database.ExecuteSqlInterpolatedAsync(
           $"SELECT pg_advisory_xact_lock(hashtextextended({resourceLock}, 0))", cancellationToken);
   }
   ```

2. `TeamLabRuntimePlanner.CreatePlannedRuntimeAsync` 的 `catch` 仅匹配 `ExclusionViolation`（地址池耗尽），未匹配 `(CreatedById, ExternalReference)` 唯一索引违反时抛出的 `UniqueViolation`：
   ```
   TeamLabRuntimePlanner.cs L167-L172:
   catch (DbUpdateException exception) when (
       exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
   {
       throw new TeamLabApiContractException(
           "address_pool_exhausted", "Concurrent address allocation exhausted an address pool.", 409);
   }
   ```

**两个 owner / 具体时序**：
- Owner A：API token `T_A`、idempotency key `K_A`、externalReference `REF-1`
- Owner B：同一用户的不同 API token `T_B`、idempotency key `K_B`（与 `K_A` 不同）、**相同** externalReference `REF-1`

时序：
1. T1：A 的 `SubmitAsync` 通过 `FindExistingAsync`（无命中），未取 advisory lock，进入 `CreatePlannedRuntimeAsync`，`SaveChangesAsync` 提交 runtime 行（`ExternalReference = REF-1`）。
2. T2：B 的 `SubmitAsync` 与 A 并发，`FindExistingAsync` 同样无命中（A 尚未提交或 B 在 A 提交前读取），未取 advisory lock，进入 `CreatePlannedRuntimeAsync`。
3. T3：A 先提交事务成功，runtime 行落库。
4. T4：B 调用 `context.TeamLabRuntimes.Add(runtime)` + `SaveChangesAsync`，触发 `(CreatedById, ExternalReference)` 唯一约束，抛 `DbUpdateException`，`InnerException.SqlState = 23505 (UniqueViolation)`。
5. T5：`CreatePlannedRuntimeAsync` 的 `when` 子句不匹配 `UniqueViolation`，异常逃逸到 `TeamLabRuntimeOperationHandler`，最终以 500 返回给 B 的客户端。

**影响**：
- 客户端 B 收到 500，无法判断 runtime 是否已创建，可能重试（idempotency key 不同会再次失败）或放弃。
- 与规范第 4.2 节"同 externalReference 应返回 409 冲突或复用现有 runtime"的设计契约不一致。
- 注意：数据库一致性**未被破坏**——唯一索引正确阻止了重复 runtime 行，仅是错误语义未对齐。

**为何 submission store 的 UniqueViolation 回退路径不能兜底**：
`EfTeamLabRuntimeOperationSubmissionStore` L72-L79 确实有 `UniqueViolation` 回退（捕获后重新 `FindExistingAsync` 并复用），但该路径捕获的是 `ApiOperations` 表的唯一约束（`(ApiTokenId, RouteKey, IdempotencyKey)`），而 `TeamLabRuntimes` 表的 `UniqueViolation` 发生在 `TeamLabRuntimePlanner.CreatePlannedRuntimeAsync` 内层事务里，**异常从 planner 直接抛出**，submission store 的 `try/catch` 已经在更外层无法兜住。

**建议修复方向**（不在本审查范围内执行）：
- 方案 A（推荐）：在 `TeamLabRuntimePlanner.CreatePlannedRuntimeAsync` 的 `catch` 中追加 `UniqueViolation` 分支，重新查询 runtime 并按 `existing.Status`/`CreateRequestHash` 走 `Reset`/`external_reference_conflict`/复用三路，与 `CreateAsync` L42-L60 的现有逻辑保持一致。
- 方案 B：在 `EfTeamLabRuntimeOperationSubmissionStore.SubmitAsync` 中，对 `ResourceId is null` 的 Create 也获取一个 `teamlab-runtime-external:{createdById}:{externalReference}` 形式的 advisory lock，将并发 Create 串行化。注意需在 `CreateAsync` 入口前提供 externalReference 给 submission。

---

### Finding 4.2.2 — TeamLab 部署期间容量被双重计数，导致不必要的跨节点放置与等待

**等级**：P2（资源利用率退化，非数据损坏；多分钟级 VM 部署期间放大影响）

**违反的不变量**：第 5 节"放置算法须同时考虑 current/reserved/building 资源"——`building` 与 `reserved` 不应同时计入同一份容量。

**根因**：

1. `NodeCapacitySnapshotService.LoadAsync` 同时查询两份重叠的数据：
   ```
   L63-L70: teamLabFacts 计入 status ∈ {Deploying, Probing, Running} 的 TeamLabRuntimeAsset
   L71-L81: reservations 计入 status = Active 的 FleetCapacityReservation
   ```
   TeamLab 资产在 `Deploying`/`Probing` 期间，对应的 `FleetCapacityReservation` 仍处于 `Active`（仅在 `RuntimeExecutionService.ConfirmCapacityAsync` 成功后才转 `Confirmed` 并不再计入 Active 聚合，失败时由 `MarkFailedAsync` 释放）。因此同一份部署中的资产在快照中被计入两次。

2. `NodeCapacitySnapshot.AllocatedDocker := CurrentDocker + ReservedDocker`（L20）直接将两者相加：
   ```
   L20:  public int AllocatedDocker => CurrentDocker + ReservedDocker;
   ```
   其中 `CurrentDocker = Math.Max(LiveDocker, FactDocker)`，`FactDocker` 已包含 teamLabFacts 中的 Deploying 资产；`ReservedDocker` 来自 Active reservations，又包含同一批资产的预留。

3. `ConfirmCapacityAsync` 仅在 ticket 成功分支被调用，**未在执行开始时调用**：
   ```
   RuntimeExecutionService.cs L220-L221:
   if (RequiresCapacityReservation(ticket))
       await ConfirmCapacityAsync(context, capacity, ticket, token);
   ```
   该语句位于 `if (result.Success)` 块内（L196-L226），意味着从 ticket 被 scheduling（reservation 创建为 Active）到执行成功（reservation 转 Confirmed）之间，reservation 始终是 Active 状态。

**两个 owner / 具体时序**：
- Owner A：正在节点 N1 部署一个含 10 个 Docker 资产的 TeamLab runtime（VM 镜像拉取 + bootstrap，典型耗时 3-8 分钟）。
- Owner B：另一个团队同时提交一个新的 TeamLab Create，需要 5 个 Docker 槽位。

时序：
1. T0：A 的 `BindAndReserveAsync` 在 N1 创建 `FleetCapacityReservation(Active, DockerSlots=10)`。
2. T1：A 的资产开始部署，状态进入 `Deploying`，被 `teamLabFacts` 查询计入 `FactDocker`。
3. T2：B 调用 `BindAndReserveAsync`，`Place` 算法读取 `NodeCapacitySnapshot`：N1 的 `FactDocker = 10`、`ReservedDocker = 10`、`AllocatedDocker = 20`。
4. T3：若 N1 总容量为 20，B 实际可用 10 但算法认为 0 可用 → 触发跨节点放置（增加 cross-node edges，违反"单节点优先"设计目标）。
5. T4：若集群只剩 N1 有容量，B 会被错误地判定为"容量不足"而排队等待，直到 A 部署完成。

**影响**：
- 单节点优先放置策略被错误绕过，cluster 内 cross-node edges 增多，影响 fabric link 占用与延迟。
- 多分钟级 VM 部署期间，其他无关 runtime 的部署可能被无谓阻塞。
- 注意：**不会导致超卖**——`ConfirmCapacityAsync` 在成功后会让 reservation 不再计入 Active，最终状态一致；问题在于"进行中"窗口期的可用容量被低估。

**为何 `teamLabFacts` 与 `reservations` 不能简单去掉其一**：
- 去掉 `teamLabFacts`：非 TeamLab 路径创建的容器实例不在此查询中，且 `Containers`/`VmInstances` 表（L51-L62）才是非 TeamLab 的实时事实来源——但 TeamLab 资产在 `RuntimeResourceId` 字段未被 `Containers` 表追踪时会出现遗漏。
- 去掉 `reservations`：已调度但未开始执行的 ticket（Scheduled 状态）的预留将无法体现，可能被后续 placement 重复占用。
- 正确修复方向是让 `reservations` 查询排除"已经被 teamLabFacts 计入的资产对应的 reservation"，或在 `teamLabFacts` 中排除"已有 Active reservation 的资产"。具体修复方案超出本审查范围。

**建议修复方向**（不在本审查范围内执行）：
- 方案 A：在 `NodeCapacitySnapshotService.LoadAsync` 中，对 `reservations` 查询追加 `&& item.DeploymentQueueTicketId == null || !TeamLabRuntimeAssets.Any(a => a.WorkerNodeId == item.WorkerNodeId && a.Status == Deploying/Probing/Running && ...)` 类似的去重条件，避免同一 ticket 的 reservation 与 asset 同时计入。
- 方案 B：在 `RuntimeExecutionService.ExecuteAsync` 进入执行分支（claim 成功后）时立即调用一个"PromoteReservationToFact"路径，将对应 reservation 从 Active 移除，依赖 `teamLabFacts` 单独计入。注意失败回滚需要恢复 reservation。

---

## 并发矩阵逐项验证

按规范第 6 节四象限逐项验证，结论如下：

### 矩阵 1：两个团队同时部署 TeamLab runtime

**结论**：✅ 一致性正确，公平性通过设计保证。

**证据**：
- `RuntimeQueueSelector.SelectAsync` L24-L32：通过 `SubjectConcurrencyKey` 过滤阻止同一 runtime 并行调度，但**不阻止不同 runtime 并行**。
- L42-L55：按 `OwnerKey(teamId, userId)` 分组，按 `activeCounts` 升序选择，保证不同 owner 间公平轮转。
- L68-L71：`MaxConcurrentCreatesPerTeam` / `MaxConcurrentCreatesPerUser` 限制单 owner 并发上限。
- `TeamLabPhysicalPlacementService.BindAndReserveAsync` L51-L52：所有 placement 通过 `fleet:scheduler` 全局 lease 串行化。
- `FleetCapacityReservationService`（TryReserveAsync/TryReserveBatchAsync/ConfirmAsync/ReleaseAsync/RenewAsync）均获取同一 `fleet:scheduler` lease。
- 网络租约分配通过 `teamlab:network-lease-allocation` 全局 advisory lock 串行化。

### 矩阵 2：同一 runtime 的重复 Create

**结论**：⚠️ 部分场景有问题（见 Finding 4.2.1）；幂等命中场景正确。

**证据**：
- ✅ **相同 idempotency key**：`EfTeamLabRuntimeOperationSubmissionStore.FindExistingAsync` 按 `(ApiTokenId, RouteKey, IdempotencyKey)` 命中现有 operation，返回 `Reuse(existing, ...)`，不会重复创建 runtime。
- ✅ **相同 externalReference、相同 requestHash、runtime 已 Destroyed**：`TeamLabRuntimePlanner.CreateAsync` L47-L53 走 `ResetAsync` 路径，串行复用 runtime。
- ✅ **相同 externalReference、相同 requestHash、runtime 未 Destroyed**：L59 返回 `Reused=true`。
- ✅ **相同 externalReference、不同 requestHash（串行）**：L54-L58 抛 `external_reference_conflict` 409。
- ❌ **相同 externalReference、不同 idempotency key（并发）**：见 Finding 4.2.1，抛 500 而非稳定 409/复用。

### 矩阵 3：同一 runtime 的 Create 与 Reset/Destroy 并发提交

**结论**：✅ 正确序列化，最终状态一致。

**证据**：
- `DeploymentQueueService.EnqueueAsync` L108-L122：每个 ticket 通过 `pg_advisory_xact_lock` 锁定 `subjectKey`（`teamlab-runtime:{runtimeId}`）和（仅 Create）`runtime-owner-admission:{ownerKey}`。
- L149-L178：当存在同一 subject 的 Active ticket 时：
  - 若新请求是 Create：复用现有 ticket（L157-L172），避免与 Reset/Destroy 并行。
  - 若新请求是 Reset/Destroy：取消该 subject 下所有非 Running 的 ticket（L174-L177），保证 control op 优先。
- `EfTeamLabRuntimeOperationSubmissionStore.SubmitAsync` L23-L29：对 `ResourceId: not null` 的 Reset/Destroy 获取 `teamlab-runtime:{runtimeId}` advisory lock，串行化同 runtime 的 control ops。
- `RuntimeQueueSelector.SelectAsync` L24-L32：`blockedSubjects` 阻止同一 subject 的多个 ticket 被 concurrent scheduling。
- 关于 submission store 中"Create 的 ResourceId 为 null 导致 advisory lock 缺失"：Create 与 Reset/Destroy 之间的顺序由 `DeploymentQueueService` 的 subject lock + ticket 复用/取消机制保证，**最终数据一致**——Reset 总是在 Create 完成后执行，或通过 ticket 取消机制让 Create 让位。submission store 的窄窗口竞态不会破坏数据一致性，因此不作为 finding。

### 矩阵 4：两个 shard 部署中一个成功一个失败

**结论**：✅ 失败正确传播，清理有界。

**证据**：
- `TeamLabShardDeploymentService` 基于 DAG 的并行资产部署使用 `SemaphoreSlim(16)` 限制并发，任一 asset 失败即抛出，传播到 orchestrator。
- `TeamLabRuntimeOrchestrator.ExecuteQueuedResetAsync` L172-L176：`cleanup.CleanupAsync` 失败时调用 `FailAsync(..., cleanupPending: true, ...)`，记录 `cleanupPending` 标志。
- `FailAsync` 在 runtime 上记录 `LastError` 并设置 `Status = Destroying`/`Destroyed`（视清理结果），事件码 `ResetFailed`/`DeploymentFailed`。
- `TeamLabPhysicalPlacementService.ApplyCompletedGenerationCredits`（L303-L333）：对 stale heartbeat 的资产正确按"最近销毁"补偿容量积分，避免已销毁资产持续占用 capacity fact。
- `FleetCapacityReservation` 在 ticket 失败时由 `RuntimeExecutionService.MarkFailedAsync` 触发 `ReleaseAsync`（释放 Active reservation），恢复集群可用容量。

---

## 已检查但确认不是问题的高风险点

以下是审查过程中重点怀疑、但通过实际阅读代码确认正确的高风险点，列出以供后续审查参考：

### 1. 多节点原子预留与回滚
- 位置：`TeamLabPhysicalPlacementService.BindAndReserveAsync` L103-L242
- 验证：所有 shard/network/asset/reservation 的创建包裹在**单一事务**（L103-L105）中，且在 `fleet:scheduler` lease 保护下（L51-L52）执行。任一节点预留失败会触发事务回滚，已创建的 reservation 行不会落库。`transaction.CommitAsync` 仅在 L128、L241 等明确成功路径调用。
- 结论：✅ 满足"多节点原子预留"不变量。

### 2. Docker-only shard 不被 KVM 能力缺失阻塞
- 位置：`NodeEligibilityEvaluator`；`TeamLabPhysicalPlacementService.Place` L532-L584
- 验证：`Required(docker, vm)` 按 `(docker>0 ? Docker : None) | (vm>0 ? Kvm : None)` 按组计算能力位掩码；`RequiredFeatures(group)` 按 group 独立计算基础设施特性需求。`Place` 算法在单节点尝试失败后进入多节点分配（L553-L581），按 group 独立选择节点，Docker-only group 可落在无 KVM 的节点上。
- 结论：✅ 满足"Docker/KVM 能力独立"不变量。

### 3. 放置算法确定性
- 位置：`TeamLabPhysicalPlacementService.Place` L532-L584、`ImprovePlacement` L586+
- 验证：所有输入显式 `OrderBy`：
  - L547-L548：单节点候选按 `Score desc, Name asc, Id asc`。
  - L571-L575：多节点候选按 `CrossNodeEdges asc, Reused desc, Score desc, Name asc, Id asc`。
  - 输入 `groups`/`edges` 在调用前已排序（见上层调用点）。
- 结论：✅ 给定相同输入必然产生相同结果，满足"算法确定性"不变量。

### 4. Redis lease 续约可靠性
- 位置：`RedisDistributedLeaseProvider.RedisLease.RenewUntilDisposedAsync` L136-L142
- 验证：`PeriodicTimer` 间隔为 `duration / 3`（10s lease → ~3.3s 续约），续约通过 Lua 脚本 `RenewScript` 原子校验 owner 后 `pexpire`。续约失败立即 `MarkLost` 并取消 `LeaseLost` token，`TeamLabPhysicalPlacementService` L53-L54 通过 `LinkedTokenSource` 将其并入取消信号，事务会因 `OperationCanceledException` 回滚。
- 结论：✅ 10s lease 对多分钟级 placement 是安全的，不会因超时丢失锁。

### 5. Reset 容量预留与 Create 一致
- 位置：`TeamLabRuntimeOrchestrator.ExecuteQueuedResetAsync`；`TeamLabPhysicalPlacementService.BindAndReserveAsync` L44-L52
- 验证：Reset 在 `ReservingNextGeneration` checkpoint 调用 `BindAndReserveAsync`，与 Create 走同一 `fleet:scheduler` lease 与同一事务路径。已有 reservation 的 ticket（re-scheduling 场景）在 L56-L101 被正确复用或释放后重新分配。
- 结论：✅ Reset 与 Create 的容量预留语义一致。

### 6. Reset/Destroy 的 subject 序列化
- 位置：`EfTeamLabRuntimeOperationSubmissionStore.SubmitAsync` L23-L29；`DeploymentQueueService.EnqueueAsync` L108-L122
- 验证：Reset/Destroy 的 `ResourceId` 非空，submission store 获取 `teamlab-runtime:{runtimeId}` advisory lock；EnqueueAsync 获取 `pg_advisory_xact_lock(subjectKey)`。两层锁保证同一 runtime 的 control ops 串行。
- 结论：✅ 满足"subject 序列化"不变量。

### 7. ApplyCompletedGenerationCredits 对 stale heartbeat 的补偿
- 位置：`TeamLabPhysicalPlacementService.ApplyCompletedGenerationCredits` L303-L333
- 验证：该方法在 placement 完成后调用，对"上一代已销毁但心跳未更新"的资产按销毁时间补偿 capacity credit，避免旧资产持续占用 `teamLabFacts`。`TeamLabCapacityFacts.LoadAsync` L25-L45 按 `runtime.Generation` 过滤 shard/asset，仅统计当前代。
- 结论：✅ stale heartbeat 不会导致容量泄漏。

### 8. ActiveIdentity 去重与 subject ticket 取消
- 位置：`DeploymentQueueService.EnqueueAsync` L125-L178
- 验证：`BuildActiveIdentity` 按 (kind, operation, owner, game, challenge, runtimeId, generation) 构建唯一身份，Active ticket 复用（L130-L147）。Control op（Reset/Destroy）到达时取消该 subject 下所有非 Running ticket（L174-L177），避免旧 Create 与新 Reset 在队列中并存。
- 结论：✅ 队列层去重与优先级正确。

### 9. ApiOperation 幂等键与 RequestHash
- 位置：`TeamLabRuntimeOperationApplicationService.SubmitAsync`
- 验证：`(ApiTokenId, RouteKey, IdempotencyKey)` 唯一索引保证相同幂等键返回相同 operation；`RequestHash` 由 payload 计算用于检测"相同幂等键不同 payload"的冲突。Create 的 `ResourceId` 在 operation 完成前为 null，由 handler 在 `LinkOperationAsync` 后回填——这是设计预期，不构成问题（Create 的幂等性由 operation 表而非 resource 表保证）。
- 结论：✅ 幂等语义正确，**除 Finding 4.2.1 的并发窗口外**。

### 10. Runtime 队列 claim 原子性
- 位置：`RuntimeSchedulingService.TryClaimAsync`
- 验证：通过 `ExecuteUpdateAsync` 原子地将 ticket 从 `Pending` 改为 `Scheduling` 并设置 `ClaimOwner`/`ClaimExpiresAt`，依赖 EF Core 的乐观并发控制。多 worker 实例下只有一个 claim 成功。
- 结论：✅ 调度层 claim 原子。

---

## 链路覆盖结论

| 子链路 | 覆盖状态 | 关键文件 | 结论 |
|--------|----------|----------|------|
| API 入口与幂等键 | ✅ 完整 | `OpenTeamLabRuntimesController.cs`、`TeamLabAdminRuntimeController.cs`、`TeamLabRuntimeOperationApplicationService.cs` | 幂等键路由正确，除 Finding 4.2.1 外 |
| Submission store 与 advisory lock | ✅ 完整 | `EfTeamLabRuntimeOperationSubmissionStore.cs` | Create 路径缺锁见 4.2.1；Reset/Destroy 正确 |
| Runtime planner Create/Reset | ✅ 完整 | `TeamLabRuntimePlanner.cs` | catch 分支缺陷见 4.2.1；其余正确 |
| 部署队列入队与 subject 序列化 | ✅ 完整 | `DeploymentQueueService.cs` | 两个 advisory lock 正确，subject 复用/取消正确 |
| 队列选择与公平调度 | ✅ 完整 | `RuntimeQueueSelector.cs` | per-owner 限制与 subject 过滤正确 |
| 物理放置算法 | ✅ 完整 | `TeamLabPhysicalPlacementService.cs` L532-L584 | 单节点优先 + 多节点 cross-edge 最小化，确定性正确 |
| 多节点原子预留 | ✅ 完整 | `TeamLabPhysicalPlacementService.cs` L103-L242 | 单事务 + lease 保护，正确 |
| 容量快照聚合 | ✅ 完整 | `NodeCapacitySnapshotService.cs` | 双重计数见 4.2.2；其余聚合正确 |
| 容量预留生命周期 | ✅ 完整 | `FleetCapacityReservationService.cs`、`RuntimeExecutionService.cs` | Confirm 时机问题见 4.2.2；Release/Renew 正确 |
| Reset 检查点状态机 | ✅ 完整 | `TeamLabRuntimeOrchestrator.cs` L106-L260 | 四阶段正确，cleanup-on-failure 有界 |
| Shard 部署失败传播 | ✅ 完整 | `TeamLabShardDeploymentService.cs`、`TeamLabRuntimeOrchestrator.cs` | DAG 并行 + 失败抛出 + cleanup 正确 |
| Redis lease 续约 | ✅ 完整 | `RedisDistributedLeaseProvider.cs` | duration/3 续约 + LeaseLost 传播正确 |
| TeamLab 容量事实 | ✅ 完整 | `TeamLabCapacityFacts.cs` | 按 generation 过滤，stale heartbeat 由 ApplyCompletedGenerationCredits 补偿 |

**总体覆盖结论**：链路 4.2 的所有关键路径已通过实际阅读源码完成审查。发现 2 个 P2 findings（4.2.1、4.2.2），均为设计正确性问题（API 语义、资源利用率），不影响数据一致性。其余 10 个高风险点经逐项验证确认正确。

**对稳定功能的影响评估**：
- Finding 4.2.1 修复仅扩展 `catch` 分支，不改变 Create 的正常路径，不影响稳定功能。
- Finding 4.2.2 修复需谨慎，需同时考虑"已调度未执行"与"执行中"两类 reservation 的区分，避免引入超卖风险。建议优先级低于 4.2.1。

**并发矩阵验证结果**：4 象限中 3 象限完全通过（矩阵 1、3、4），矩阵 2 在"同 externalReference、不同 idempotency key 并发 Create"子场景存在 Finding 4.2.1 描述的问题，其余子场景通过。
