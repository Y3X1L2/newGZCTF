# 链路 4.9 Reset/Destroy/恢复 审查结果

## 审查范围与覆盖

本审查覆盖 Phase 9 TeamLab 组网的【链路 4.9 Reset、Destroy 和恢复】，对照规范文档 `docs/commercialization/phase-09-teamlab-networking-independent-code-review.md` 第 3.7 节（事件驱动 readiness 与恢复）、第 4.9 节（Reset/Destroy/Recovery 检查项）、第 5 节不变量 #1/#2/#3/#6/#14/#15/#16、第 6 节并发矩阵相关场景、第 12 节 finding 输出规范。

### 主站已审查文件（完整阅读）

| 文件 | 关键范围 |
| --- | --- |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs` | `ResetAndEnqueueAsync`、`ExecuteQueuedResetAsync`、`DestroyAndEnqueueAsync`、`ExecuteQueuedDestroyAsync`、`ExecuteQueuedAsync` |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs` | `CleanupAsync`、`HasPendingSideEffectsAsync`、`FinalizeGenerationAsync`、`BuildCleanupRequest` |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeRecoveryPolicy.cs` | `CanResumeExistingGeneration`、`CanRebuildMissingAsset`、`CanReplayInfrastructure` |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOperationApplicationService.cs` | 操作提交、idempotency key、payload 保护 |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs` | `StartCollectorsAsync`、`StopCollectorsAsync`（no-op）|
| `src/GZCTF/Modules/TeamLab/Application/TeamLabCaptureCoordinator.cs` | `ProcessPendingAsync`、`ProcessJobAsync`、`ExpireAsync` |
| `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs` | 操作派发、`WaitForTicketAsync` 轮询 |
| `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationResultProvider.cs` | 结果提供器 |
| `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs` | `CleanupShardAsync`、`DestroyAssetAsync` |
| `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabFleetAdapters.cs` | `TeamLabRuntimeQueue` 包装 `DeploymentQueueService` |
| `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimePrimitives.cs` | `TeamLabResetCheckpoint`、`TeamLabResetCheckpointFacts` |
| `src/GZCTF/Modules/Runtime/Application/RuntimeFactReconciliationService.cs` | `ReconcileAsync`、`LoadActiveTeamLabLifecycleOwnersAsync`、`InspectTeamLabResetTicketAsync`、`InspectTeamLabControlTicketAsync`、`ReleaseTicketCapacityAsync` |
| `src/GZCTF/Modules/Runtime/Application/RuntimeSignalService.cs` | 信号 ingest、序列单调性、generation fence |
| `src/GZCTF/Modules/Runtime/Application/RuntimeAdmissionPolicy.cs` | 同 owner 队列限制 |
| `src/GZCTF/Modules/Runtime/Infrastructure/RuntimeRecoveryWorker.cs` | 后台 reconciliation，`PostgresGovernanceLease` |
| `src/GZCTF/Services/Fleet/DeploymentQueueService.cs` | `EnqueueAsync`、subject advisory lock、Create 重用/非 Create 取消非 Running ticket |
| `src/GZCTF/Services/Fleet/FleetCapacityReservationService.cs` | 容量预留/确认/释放、Active 30 分钟过期 |
| `src/GZCTF/Services/Fleet/TeamLabCapacityFacts.cs` | shard 槽位加载（按当前 generation）|
| `src/GZCTF/Services/Fleet/NodeCapacitySnapshotService.cs` | 仅 `Active` 预留计入容量 |
| `src/GZCTF/Services/Fleet/ImageDistributionService.cs` | `ReleaseTeamLabRuntimeReferencesAsync`、`ReconcileReferencesAsync`、`CleanupUnreferencedAsync` |
| `src/GZCTF/Modules/Runtime/Domain/ImageDistributionReference.cs` | `ImageDistributionReferenceKey`（per-runtime）|

### Agent 侧已审查文件（完整阅读）

| 文件 | 关键范围 |
| --- | --- |
| `src/GZCTF.Agent/Services/TeamLabNetworkService.cs` | `CleanupAsync`（行 670-772）、generation fence、`ownsSharedResources` |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabRuntimeGenerationStore.cs` | 原子 active generation 持久化、`ClearIfActiveAsync` |
| `src/GZCTF.Agent/Services/RuntimeSignals/AgentRuntimeSignalJournal.cs` | append-only JSONL、`FileOptions.WriteThrough`、1MB 上限 |
| `src/GZCTF.Agent/Services/RuntimeSignals/AgentRuntimeSignalPublisher.cs` | 有界 channel、DropOldest、16 路并行 |
| `src/GZCTF.Agent/Services/Vm/AgentOperationReceiptStore.cs` | SHA-256 request hash、幂等 result.json、`AgentResourceLock` |
| `src/GZCTF.Agent/Services/AgentOperationGate.cs` | 按类别信号量 |
| `src/GZCTF.Agent/Services/AgentResourceLock.cs` | 引用计数命名锁 |
| `src/GZCTF.Agent/Services/KvmService.cs` | `DestroyVmAsync`、`CleanupVmArtifacts` |
| `src/GZCTF.Agent/Controllers/RuntimeController.cs` | Agent runtime endpoint |

## Findings 汇总

| 编号 | 标题 | 严重性 | 链路 |
| --- | --- | --- | --- |
| 4.9.1 | Destroy/Reset 不立即清理对象存储 capture 分段，销毁谎报成功 | P1 | 4.9 Destroy/Reset → capture 清理 |

### Finding 4.9.1: Destroy/Reset 不立即清理对象存储 capture 分段，销毁谎报成功

**严重性**：P1

**精确文件和行号**：

- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs#L211-L227`（`FinalizeGenerationAsync` 将 capture job 与 active segment 标记为 `Failed`，但未调用 `ExpireAsync`、未删除对象存储 segment）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs#L145-L149`（`HasPendingSideEffectsAsync` 仅检查 active segment；segment 被置为 `Failed` 后该检查返回 false，destroy 因此可被标记完成）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabTrafficApplicationService.cs#L29-L30`（`StopCollectorsAsync` 为 no-op，未触发任何 collector/segment 清理）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabCaptureCoordinator.cs#L23-L34`（`ProcessPendingAsync` 查询不包含 `Failed` 且 `ExpiresAt > now` 的 job；这些 job 的对象存储 segment 不会被回收）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs#L412-L417`（`ExecuteQueuedDestroyAsync` 在 `CleanupAsync` 成功后直接 `FinalizeGenerationAsync` 并将 runtime 标记为 `Destroyed`，但从未调用 `captureCoordinator.ExpireAsync`）
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs#L172-L177`（`ExecuteQueuedResetAsync` 在 `CleaningPreviousGeneration` 阶段调用 `CleanupAsync`，同样不触发对象存储清理）

**所属端到端链路**：4.9 Reset/Destroy/恢复 → capture 清理

**触发条件**：

1. runtime 存在尚未过期（`ExpiresAt > now`）的 `TeamLabTrafficCaptureJob`，且其包含 `Captured`/`Uploading`/`Pending`/`Running`/`Stopping` 状态的 segment（即已上传或正在上传对象存储）。
2. 用户提交 Reset 或 Destroy 操作，且所有 shard 的 agent 侧清理（容器/VM/namespace/firewall/lease）均成功。

**实际影响**：

- `ExecuteQueuedDestroyAsync` 在 `CleanupAsync` 成功后调用 `FinalizeGenerationAsync`，将 runtime 标记为 `Destroyed`；但对象存储中的 capture segment（`ObjectPath` 指向的 S3/MinIO 对象）未被删除。
- `ExecuteQueuedResetAsync` 在前一代清理阶段同样仅标记 `Failed`，前一代 capture 对象存储 segment 残留至 `ExpiresAt`。
- `TeamLabCaptureCoordinator.ProcessPendingAsync` 每 2 秒轮询一次，但查询条件（行 23-34）只覆盖 `ExpiresAt <= now`、`Running`、`Stopping` 或含 `Captured`/`Uploading` segment 的 job；被 `FinalizeGenerationAsync` 标记为 `Failed` 的 job（segment 也为 `Failed`）且 `ExpiresAt > now` 时不会进入处理队列，对象存储对象继续计费并占用存储。
- `HasPendingSideEffectsAsync` 在 `Failed` 后返回 false，使 destroy 路径误判“无残留副作用”，runtime.Status 被置为 `Destroyed`。
- 跨队伍数据泄露面：被销毁的 runtime 的 capture 对象仍可通过对象存储 key 访问（取决于对象存储授权策略），且生命周期超出 runtime 销毁时刻。

**被破坏的不变量**：

- #15（destroy 完成意味着所有节点和存储上的运行资源都已清理）：runtime.Status = Destroyed 时，对象存储 capture segment 仍然存在。
- 并发矩阵「capture 上传中断 | segment 可恢复或明确失败，destroy 可完全清理」：destroy 并未「完全清理」capture segment。
- P1 定义中的「销毁谎报成功」直接命中。

**根因**：

`FinalizeGenerationAsync` 在 `TeamLabRuntimeCleanupService.cs` 行 211-227 只修改数据库状态（job/segment.Status = Failed），未编排对象存储删除；`TeamLabTrafficApplicationService.StopCollectorsAsync` 是 no-op（行 29-30），未填补该缺口；`ExecuteQueuedDestroyAsync`/`ExecuteQueuedResetAsync` 均未调用 `TeamLabCaptureCoordinator.ExpireAsync`。`ProcessPendingAsync` 的查询谓词将 `Failed + 未过期` 的 job 排除在外，导致这些 job 的对象存储 segment 在 `ExpiresAt` 之前永远无法被回收。

这不是单纯的延迟清理，而是「destroy 成功」与「对象存储资源实际回收」之间的语义断裂：destroy 在数据库中被标记为完成，但存储层并未完成。

**最小且架构正确的修复方向**：

在 `FinalizeGenerationAsync` 中标记 capture 为 `Failed` 之前，先调用 `TeamLabCaptureCoordinator.ExpireAsync`（或等效地直接通过 `TeamLabCaptureArtifactStore.DeleteAsync` 删除 `segment.ObjectPath`）。具体顺序：

1. `CleanupAsync` 在调用 `FinalizeGenerationAsync` 前，对当前 generation 的所有 active capture job 调用 `captureCoordinator.ExpireAsync(job, now, cancellationToken)`，确保对象存储 segment 被删除；
2. 若 `ExpireAsync` 因对象存储不可用而将 segment 标记为 `CleanupPending`，`CleanupAsync` 应将其视为 cleanup 失败，runtime 进入 `CleanupPending` 状态而非 `Destroyed`；
3. `HasPendingSideEffectsAsync` 行 145-149 应额外检查 `segment.Status == TeamLabTrafficCaptureSegmentStatus.CleanupPending`，确保 destroy 在对象存储清理未完成时不会谎报成功。

不应依赖 `ProcessPendingAsync` 的后台轮询作为唯一回收路径——destroy 完成的语义必须与存储状态一致。

**修复后的验证方式**：

1. 单元测试：构造一个含 `Captured` segment 的 capture job，调用 `CleanupAsync`，断言 `artifacts.DeleteAsync` 被调用、`segment.ObjectPath == null`、runtime.Status 为 `Destroyed` 当且仅当所有 segment 到达 `Expired`。
2. 集成测试：在真实对象存储（MinIO）中放置 capture 对象，提交 Destroy，断言 destroy 完成后对象不再存在于 bucket。
3. 故障注入测试：让 `artifacts.DeleteAsync` 抛异常，断言 runtime 进入 `CleanupPending` 而非 `Destroyed`，且后续 `ProcessPendingAsync` 能继续重试。

## 不变量验证

### #1 同一 runtime/generation/asset 的创建只能有一个有效 owner

- **证据**：`DeploymentQueueService.EnqueueAsync` 行 114-115 对 `subjectKey` 调用 `pg_advisory_xact_lock(hashtextextended(...))`，对同一 subject 的所有 ticket 入队串行化。Create 操作额外对 `runtime-owner-admission` key 加 advisory lock（行 119-121）。`DeploymentQueueTicket.BuildActiveIdentity` 配合 `ActiveStatuses` 过滤（行 126-128）确保同一 identity 的 active ticket 只能复用，不会创建副本。
- **结论**：✓ 满足。

### #2 旧 generation 的命令、信号和清理不能修改当前 generation

- **证据**：
  - Agent 侧 `TeamLabNetworkService.CleanupAsync` 行 698-701：`ownsSharedResources = activeGeneration?.Generation == request.Generation`，仅当 active generation 与请求 generation 一致时才执行 dnsmasq kill、namespace 删除、link 删除、fabric peer route 删除；否则只删除 generation 专属目录（行 714）。
  - `TeamLabRuntimeGenerationStore.ClearIfActiveAsync`（行 59-65）使用 generation fence，仅在 active generation 匹配时清除。
  - `RuntimeSignalService` 行 55-56 对信号应用 generation fence，行 57-62 验证 owned asset。
  - `TeamLabResetCheckpointFacts` 与 `FinalizeGenerationAsync` 均按 generation 过滤资产/lease/grant。
- **结论**：✓ 满足。

### #3 所有 Agent mutation 必须幂等，并绑定稳定 operation identity

- **证据**：
  - `AgentOperationReceiptStore` 行 26-27 为每个 operation 持有 `AgentResourceLock`，行 72-109 使用规范化 JSON 序列化计算 SHA-256 request hash，持久化 `result.json`；相同 hash 的重放直接返回已存储结果。
  - `TeamLabRuntimeOperationApplicationService` 行 79 计算 `payloadHash = "sha256:" + SHA256(payload)`，作为 idempotency key 的一部分。
  - `AgentRuntimeSignalJournal` 行 46 使用 `FileOptions.WriteThrough`，行 96-108 的 `AcknowledgeAsync` 使用原子 `File.Move`。
  - `AgentOperationGate` 按类别（DockerCreate/VmCreate 等）限流，确保 mutation 串行。
- **结论**：✓ 满足。

### #6 多节点容量必须原子预留，不允许部分预留后继续部署

- **证据**：
  - `FleetCapacityReservationService.TryReserveAsync` 行 96-98 通过 `AcquireSchedulerLeaseAsync` 获取分布式 lease，整个预留-保存为原子事务。
  - `TryReserveBatchAsync` 行 159-161 同样持有 lease，行 181-200 对所有 node 逐一校验容量，任一不足则整体失败（行 197-199 返回 `Failed`）。
  - `ReconcileReservedAsync` 行 260-266 仅过期 `Active` 且 `ExpiresAt <= now` 的预留；`RenewActiveTicketReservationsAsync` 行 232-258 仅对 `Scheduled`/`Running` ticket 续期。
  - `NodeCapacitySnapshotService` 行 72-73 仅将 `CapacityReservationStatus.Active` 计入容量，`Confirmed` 不计入（因为实际容器/VM 已直接计入）。
- **结论**：✓ 满足。

### #14 reset 不改变玩家可见网络语义；旧 grant 必须失效

- **证据**：
  - `FinalizeGenerationAsync` 行 200-204 将指定 generation 的未撤销 `AccessGrants` 全部 `Revoked = true`、`RevokedAt = now`。
  - 行 205-206 将所有 `VpnPeers`（未撤销的）`Revoked = true`。
  - 行 207-208 调用 `TeamLabRuntimeOverlayService.Consume(envelope)` 消费 secret envelope。
  - 行 209-210 将 `PublicUdpMapping.IsSynced = false`（若 mapping generation 匹配）。
  - Reset 流程在 `CleaningPreviousGeneration` 阶段调用 `CleanupAsync`（行 172-173），通过 `FinalizeGenerationAsync` 撤销旧 grant；新 generation 通过 `PlanningNextGeneration`/`ReservingNextGeneration`/`DeployingNextGeneration` 重建。
  - 玩家可见网络语义（地址/端口/路由）由 topology release 决定，Reset 不改变 topology release，只切换 generation。
- **结论**：✓ 满足。

### #15 destroy 完成意味着所有节点和存储上的运行资源都已清理

- **证据**：见 **Finding 4.9.1**。`ExecuteQueuedDestroyAsync` 在 `CleanupAsync` 成功后调用 `FinalizeGenerationAsync` 标记 runtime 为 `Destroyed`，但对象存储 capture segment 未被删除。
- **结论**：✗ 违反（P1）。

### #16 失败必须保留 correlation、阶段、节点、资产和稳定错误码

- **证据**：
  - `TeamLabRuntimeOrchestrator.FailAsync` 记录 `eventCode`（如 `OperationalEventCodes.TeamLab.ResetFailed`/`DestroyFailed`）、`stage`（如 "reset"）、`cleanupPending` 标志，并写入 `OperationalEvent`。
  - `TeamLabRuntimeCleanupService.CleanupAsync` 行 84-89 在失败时记录 `OperationalError`（含 `OperationalErrorCategory.Network`、`OperationalErrorCodes.NetworkOperationFailed`、`Operation: "teamlab.cleanup"`、`Retryable: true`）。
  - `ExecuteQueuedResetAsync` 行 175-176、197-203、208-213、225-226 在每个 checkpoint 失败时记录具体 stage 与 message。
  - `DeploymentQueueService` 通过 `OperationalCorrelation` 在 ticket 复用时 `Begin(existing.Id)` 保留 correlation scope。
- **结论**：✓ 满足。

## 并发矩阵逐项验证

### 场景：Create 中提交 Reset/Destroy — subject 顺序明确，不能提前释放容量

- **证据**：
  - `DeploymentQueueService.EnqueueAsync` 行 108-115 对 `subjectKey` 加 advisory lock，串行化同一 subject 的所有 ticket。
  - 行 149-154 加载同 subject 的 active ticket，按 `Running` 优先、`CreatedAt` 升序排序。
  - 行 157-172：Create 复用同 subject 的 active ticket；非 Create（Reset/Destroy）在行 174-177 取消非 `Running` 的同 subject ticket，但 `Running` ticket 继续执行。
  - 容量预留 `Active` 状态在 ticket 进入 `Scheduled`/`Running` 后由 `RenewActiveTicketReservationsAsync` 续期；`FailClosed` 时 `ReleaseTicketCapacityAsync` 释放。
- **结论**：✓ 满足。subject 顺序由 advisory lock 保证，Running ticket 不会被取消，容量在真实失败前不释放。

### 场景：主站在 reset checkpoint 后重启 — 从持久化 checkpoint 恢复，不重建已完成阶段

- **证据**：
  - `TeamLabResetCheckpointFacts`（`TeamLabRuntimePrimitives.cs`）将 checkpoint 作为 `TeamLabEvent` 持久化（`ObjectType="reset-checkpoint"`），4 个值：`CleaningPreviousGeneration` → `PlanningNextGeneration` → `ReservingNextGeneration` → `DeployingNextGeneration`。
  - `ExecuteQueuedResetAsync` 行 131 读取 `TeamLabResetCheckpointFacts.Get(runtime, ticketId)`，行 137-162 处理 `checkpoint is null` 的初始状态，行 164-190、192-219、221-237、239-240 分别处理 4 个 checkpoint 阶段，每个阶段完成后 `Record` 下一 checkpoint 并 `SaveChangesAsync`。
  - 已完成阶段（如 `CleaningPreviousGeneration` 已完成、`runtime.Status == Destroyed`）在行 166-167 跳过，不会重复执行 cleanup。
  - `RuntimeFactReconciliationService.InspectTeamLabResetTicketAsync`（行 841-889）在 reconciliation 时根据 checkpoint 决定 `Completed`/`SafeReplay`/`Deferred`/`FailClosed`。
- **结论**：✓ 满足。checkpoint 持久化到 PostgreSQL，重启后从最近 checkpoint 继续。

### 场景：旧 cleanup 延迟到新 generation — generation fence 阻止删除当前资源

- **证据**：
  - `TeamLabNetworkService.CleanupAsync` 行 698-701：`ownsSharedResources = activeGeneration?.Generation == request.Generation`。若 active generation 已切换到新 generation，`ownsSharedResources = false`，仅删除旧 generation 专属目录（行 714），不执行 dnsmasq kill、namespace 删除、link 删除、fabric peer route 删除。
  - 行 715 尝试 `rmdir runtimeDirectory`，但通过 `test -n "$(find ... -print -quit)"` 检查目录非空时保留。
  - 行 762-763：`generationStore.ClearIfActiveAsync` 仅在 active generation 匹配时清除，避免删除新 generation 的 active 记录。
  - `TeamLabRuntimeGenerationStore.ClearIfActiveAsync`（行 59-65）使用原子 `File.Move` + generation 比较。
- **结论**：✓ 满足。旧 generation 的延迟 cleanup 不会删除新 generation 的共享资源。

### 场景：capture 上传中断 — segment 可恢复或明确失败，destroy 可完全清理

- **证据**：见 **Finding 4.9.1**。`TeamLabCaptureCoordinator.ExpireAsync`（行 160-232）实现了对象存储 segment 删除逻辑（行 183-196 调用 `artifacts.DeleteAsync`），但 `ExecuteQueuedDestroyAsync` 与 `ExecuteQueuedResetAsync` 均未调用 `ExpireAsync`；`FinalizeGenerationAsync` 仅标记 `Failed`。
- **结论**：✗ 违反（P1）。destroy 不能完全清理 capture segment，segment 在 `ExpiresAt` 之前残留于对象存储。

## 残留检查覆盖验证

下表对照规范第 4.9 节「最终残留检查覆盖 container、domain、overlay、ISO、bridge、namespace、veth、route、firewall、WireGuard、capture、lease 和 distribution claim」共 13 类资源。

| # | 资源类型 | 清理位置 | 覆盖结论 |
| --- | --- | --- | --- |
| 1 | container | `AgentTeamLabNodeExecutor.CleanupShardAsync` 行 215-220 调用 `DestroyAssetAsync(TeamLabAssetKind.Docker, containerId)` | ✓ |
| 2 | domain (VM) | `AgentTeamLabNodeExecutor.CleanupShardAsync` 行 221-226 调用 `DestroyAssetAsync(TeamLabAssetKind.Vm, vmName)` → `KvmService.DestroyVmAsync`（virsh destroy + undefine --remove-all-storage） | ✓ |
| 3 | overlay (qcow2) | `KvmService.CleanupVmArtifacts` 删除 overlay qcow2 文件 | ✓ |
| 4 | ISO (cloud-init seed) | `KvmService.CleanupVmArtifacts` 删除 cloud-init seed ISO | ✓ |
| 5 | bridge | `TeamLabNetworkService.CleanupAsync` 行 710-711 通过 `ip link delete <name>` 删除（bridge 作为 link 设备；ResourceNames 包含 bridge 名） | ✓ |
| 6 | namespace | `TeamLabNetworkService.CleanupAsync` 行 710 通过 `ip netns delete <name>` 删除 | ✓ |
| 7 | veth | `TeamLabNetworkService.CleanupAsync` 行 711 通过 `ip link delete <name>` 删除（veth 作为 link 设备） | ✓ |
| 8 | route | namespace 删除时其内部路由自动消失；fabric peer route 由 `fabricService.RemovePeerRoutesAsync`（行 732-737）在 `ownsSharedResources` 时删除 | ✓ |
| 9 | firewall | `firewallService.RemoveRuntimePoliciesAsync`（行 717-722）+ `RemoveFabricPoliciesAsync`（行 723-727）+ `VerifyPoliciesRemovedAsync`（行 739-744） | ✓ |
| 10 | WireGuard | WireGuard 接口配置于 router namespace 内（见 `ConfigureAccessAsync` 传入 `RouterNamespace`），namespace 删除时 WireGuard 接口随之消失；peer 撤销由 `FinalizeGenerationAsync` 行 205-206 完成 | ✓ |
| 11 | capture (agent 侧 pcap) | `TeamLabNetworkService.CleanupAsync` 行 756 调用 `pcapService.CleanupGenerationAsync`；对象存储 segment 见 #15 违反 | ⚠ agent 侧 ✓，对象存储 ✗（见 Finding 4.9.1） |
| 12 | lease | `FinalizeGenerationAsync` 行 229-238 释放 `TeamLabNetworkLeases` 与 `TeamLabFabricLinkLeases`（`ReleasedAt = now`） | ✓ |
| 13 | distribution claim | `ExecuteQueuedDestroyAsync` 行 415 调用 `imageDistribution.ReleaseRuntimeAsync(runtime.Id, ...)` → `ReleaseTeamLabRuntimeReferencesAsync` → `ReleaseReferenceAsync(ImageDistributionReferenceKey.TeamLabRuntime(runtimeId))`（per-runtime，非 per-generation）；Reset 不释放（正确，因 runtime 持续到新 generation） | ✓ Destroy；✓ Reset（设计上不释放） |

**覆盖结论**：13 类资源中 12 类完整覆盖；capture 类在 agent 侧覆盖，但对象存储 segment 在 destroy/reset 时未立即清理（Finding 4.9.1）。

## 已检查但确认不是问题的高风险点

### 1. ResetAndEnqueueAsync 未检查 `Destroyed` 状态（行 73-74）

- **位置**：`TeamLabRuntimeOrchestrator.cs#L73-L74`
- **观察**：`ResetAndEnqueueAsync` 只在 `runtime.Status is Destroying or CleanupPending` 时抛 409，未检查 `Destroyed`。
- **结论**：非问题。`ExecuteQueuedResetAsync` 行 166-167 显式处理 `runtime.Status == Destroyed` 的情况：跳过 `CleaningPreviousGeneration` 阶段（因资产已清理），直接进入 `PlanningNextGeneration`。这是「从已销毁状态恢复 runtime」的有意设计，符合 Reset 语义。Reset 的 target generation = `runtime.Generation + 1`（行 90），与 Destroyed 状态兼容。

### 2. Destroy 不释放 per-generation 镜像分发引用

- **位置**：`ImageDistributionService.ReleaseTeamLabRuntimeReferencesAsync` 行 103-104
- **观察**：`ImageDistributionReferenceKey.TeamLabRuntime(runtimeId)` 是 per-runtime 而非 per-generation。
- **结论**：非问题。Destroy 释放整个 runtime 的引用（正确）；Reset 不释放（正确，因为 runtime 持续到新 generation，引用仍有效）。`ReconcileReferencesAsync` 行 173-180 在 runtime.Status == Destroyed 时自动将引用标记为无效，作为兜底。

### 3. `StopCollectorsAsync` 为 no-op

- **位置**：`TeamLabTrafficApplicationService.cs#L29-L30`
- **观察**：`StopCollectorsAsync` 返回 `Task.CompletedTask`，不执行任何操作。
- **结论**：这是 Finding 4.9.1 的根因之一（capture 停止依赖 `FinalizeGenerationAsync` 标记 Failed，但对象存储删除无编排）。作为独立风险点，它与 Finding 4.9.1 合并，不单独开 finding。

### 4. Reset 在 `CleaningPreviousGeneration` 失败时设置 `cleanupPending: true`

- **位置**：`TeamLabRuntimeOrchestrator.cs#L175-L176`
- **观察**：`FailAsync(..., cleanupPending: true, ...)` 将 runtime 置于 cleanup pending 状态，等待 reconciliation。
- **结论**：非问题。reconciliation 通过 `InspectTeamLabResetTicketAsync`（行 841-889）根据 checkpoint 与 runtime 状态决定 `SafeReplay`/`Deferred`/`FailClosed`，能在后续 worker 周期中恢复。

### 5. `HasPendingSideEffectsAsync` 检查 image distribution references（行 158-160）

- **位置**：`TeamLabRuntimeCleanupService.cs#L158-L160`
- **观察**：检查 `ImageDistributionReferences` 是否存在 `Kind == TeamLabRuntime && ResourceId == runtime.Id` 的引用。
- **结论**：非问题。这是 destroy 完成前的最后一道防线，确保 `ReleaseTeamLabRuntimeReferencesAsync` 已执行。但该检查不影响 capture segment（capture segment 检查在行 145-149，仅覆盖 active 状态），与 Finding 4.9.1 一致。

### 6. Reconciliation 排除活动生命周期 owner

- **位置**：`RuntimeFactReconciliationService.LoadActiveTeamLabLifecycleOwnersAsync` 行 211-224
- **观察**：加载具有 active Create/Reset/Destroy ticket 的 runtime，reconciliation 不对这些 runtime 进行 fact 纠正。
- **结论**：非问题。避免 reconciliation 与活动 lifecycle owner 对抗，符合规范第 4.9 节「reconciliation 是否不会与活动生命周期 owner 对抗」。

### 7. `ExecuteQueuedDestroyAsync` 不检查 `Destroyed` 之前的 ticket 状态

- **位置**：`TeamLabRuntimeOrchestrator.cs#L395-L396`
- **观察**：若 runtime 已 `Destroyed`，直接返回 `Ok("Runtime is already destroyed.")`，idempotent。
- **结论**：非问题。这是幂等 destroy 的正确实现，重复 destroy 请求不会重复清理。

## 链路覆盖结论

- **审查覆盖**：4.9 链路 Reviewed。已完整阅读主站 21 个文件 + Agent 侧 9 个文件，覆盖 Reset/Destroy/恢复的全部关键路径。
- **Findings**：1 个 P1（Finding 4.9.1）。
- **不变量**：#1/#2/#3/#6/#14/#16 满足；#15 违反（P1）。
- **并发矩阵**：4 项中 3 项满足，1 项（capture 上传中断 / destroy 完全清理）违反。
- **残留检查**：13 类资源中 12 类完整覆盖；capture 类在对象存储层未在 destroy/reset 时立即清理。
- **生产准入**：因存在 P1，链路 4.9 阻塞生产准入（`BLOCKED`），直至 Finding 4.9.1 修复并验证。

**关键修复优先级**：Finding 4.9.1 必须在生产发布前关闭。修复方向是在 `CleanupAsync`/`FinalizeGenerationAsync` 中编排 `TeamLabCaptureCoordinator.ExpireAsync`，并在 `HasPendingSideEffectsAsync` 中检查 `CleanupPending` segment，确保 destroy 完成的语义与对象存储状态一致。
