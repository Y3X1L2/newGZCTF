# TeamLab 组网底座代码审查报告

日期：2026-08-14
审查分支：`codex/teamlab-high-performance-a`（HEAD `0bb43f6`，已推送 `origin/codex/teamlab-high-performance-a`）
审查范围：交接文档第 4 节列出的主站、Agent、共享契约与对应单测，外加执行模型迁移与 Redis 流量摄取链路的定向核查。

## 1. 结论摘要

- 发现 **1 个确定性阻断项并已修复**：`RedisTeamLabTrafficIngestor.BufferLocally` 把「本地缓冲（Deferred）」样本错误报告为「已接受」，破坏持久化契约并导致单测失败。
- 发现 **1 个残留风险（未修复）**：`OvsdbJsonRpcClientTests.Client_ResetsTimedOutSessionBeforeNextTransactionUsesIt` 是计时型测试，在并行全量运行时偶发失败、单类隔离运行稳定通过。
- 交接文档第 3.2 节列出的 **6 个已知设计张力全部评估完毕**，均给出证据，本轮**不做代码改动**（结论见第 5 节）。

## 2. 验证结果

| 门禁 | 结果 |
| --- | --- |
| `git diff --check` | 通过 |
| `dotnet build src/GZCTF.slnx -c Release` | 0 错误；27 条既有警告（xUnit1031、CS9113、SSH.NET NU1903 高危依赖，均为存量） |
| `dotnet test src/GZCTF.Test -c Release --filter "FullyQualifiedName~TeamLab"` | 301 用例；修复前 1 失败（`RedisUnavailable_DefersInsteadOfAcknowledgingVolatileMemory`），修复后该用例通过；剩余 1 个偶发失败见第 4 节 |

修复后定向复验：`TeamLabTrafficFingerprintTests` 相关用例通过；`OvsdbJsonRpcClientTests` 单类隔离运行 6/6 通过（并行全量运行时偶发失败）。

## 3. 已修复阻断项

### 3.1 Blocker — `RedisTeamLabTrafficIngestor.BufferLocally` 把 Deferred 样本报告为 AcceptedCount

- **严重度**：Blocker（破坏不可变持久化契约，单测红）
- **文件/行号**：`src/GZCTF/Modules/TeamLab/Infrastructure/RedisTeamLabTrafficIngestor.cs`，`BufferLocally` 方法（约 276–284 行）
- **根因**：commit `8f45599` 用 `BufferLocally(batches, firstBatch)` 替换旧的 `Deferred()` 时，把返回值第 1 个字段（`AcceptedCount`）从旧的 `0` 改成了 `pending.Length`。旧的 `Deferred()` 返回 `new TeamLabTrafficEnqueueResult(0, 0, 0, true)`；替换后变成 `(pending.Length, batches.Count - firstBatch, dropped, true)`。
- **影响**：`AcceptedCount` 的契约含义是「已持久接受进 Redis 流」的数量。Redis 不可用（`connection is null`）或追加失败时，样本只进入本地易失缓冲，并未持久接受；此时返回 `AcceptedCount > 0` 会向任何直接消费该字段的调用方谎报「已持久接受」。下游 `TeamLabTrafficApplicationService` 因 `!enqueue.Deferred` 短路暂未受影响，但契约被破坏，且 `TeamLabTrafficFingerprintTests.RedisUnavailable_DefersInsteadOfAcknowledgingVolatileMemory` 断言 `AcceptedCount == 0` 直接失败。
- **修复**：`BufferLocally` 返回 `AcceptedCount = 0`，保留 `BatchCount`（待缓冲批次数）、`DroppedCount` 与 `Deferred=true`，并加注释说明语义。
- **是否补丁式**：否。这是恢复 commit 之前的正确契约语义，不是延长等待或吞错误。

## 4. 残留风险（未修复，记录）

### 4.1 P2 — `OvsdbJsonRpcClientTests.Client_ResetsTimedOutSessionBeforeNextTransactionUsesIt` 计时型偶发失败

- **严重度**：P2（测试不稳定，非产品缺陷）
- **文件/行号**：`src/GZCTF.Test/UnitTests/TeamLab/OvsdbJsonRpcClientTests.cs:78`；被测 `src/GZCTF.Agent/Services/TeamLab/OvsdbJsonRpcClient.cs`
- **证据**：全量 `~TeamLab` 并行运行连续 2 次失败（`TaskCanceledException` 位于 `OvsdbJsonRpcClient.cs:240` 的第二次事务 `WriteJsonAsync`，即测试第 89 行）；单类隔离运行 2/2 通过。
- **根因**：该测试用 `transactionTimeout = 100ms`，配合服务端 `Task.Delay(250ms)`、客户端 `Task.Delay(300ms)` 的固定延迟。并行全量运行时线程池争用/GC 停顿使第二次事务的 100ms 预算不足，导致写请求被 `CancelAfter` 取消。生产 `OvsdbJsonRpcClient` 默认 `transactionTimeout = 15s`，因此这不是产品缺陷，而是测试计时设计问题。
- **建议**：改为确定性协调（服务端在接受/关闭连接时通过信号握手通知测试，替代固定延迟），避免以「延长等待」的方式打补丁（交接文档明确禁止延长等待）。
- **不在本轮修复的原因**：该测试不在交接文档第 4.3 节列出的审查单测清单内；正确修复需要重构测试时序，风险与收益不成比例，故记录为残留风险交由后续测试 agent 处理。

## 5. 六个已知设计张力评估（本轮不改代码）

1. **V1/V2 双路径（`TeamLabShardDeploymentService.DeployAsync`）**：V1 主路径按设计基线 #8「切换验收完成前必须保留」处理，通过显式 `runtime.ExecutionModel` 枚举门控，`default` 分支抛错，无静默降级。执行模型默认 V2（`Configs.cs:611`、`TeamLabRuntimeAggregate.cs:23`、DB 迁移三连：加列默认 V2 → 去默认 → 回填 legacy 为 V1）。**删除 V1 主路径应在切换验收签收后单独进行，本轮不删。**
2. **`TeamLabExecutionModelPolicy` 过小**：13 行静态类，仅 `FindUnsupportedSecretKey`，在 `TeamLabRuntimePlanner`（422 校验）与 `TeamLabShardDeploymentService`（部署前抛错）两处复用。职责单一，但类名「Policy」偏大；可内联但非阻断，且两处复用说明内联反而重复，故保留。
3. **`TeamLabExecutionPlanV2.IsValid` 超长组合校验**：功能正确——`PlanDigest` 由编译器以 `"sha256:"` 前缀生成（`TeamLabExecutionPlanCompiler.cs:172`），与 `IsDigest` 的前缀判定一致，digest 校验闭环；逐项规则覆盖身份、唯一键、CIDR/MAC/IP/主机名/健康检查/路由/策略/玩家网关/控制意图。可读性欠佳，但**没有重复校验、没有为拆而拆**，按交接文档要求不改。
4. **幂等提前返回（`TeamLabExecutionPlanExecutor.ApplyAsync`）**：仅当 journal 存在且 inventory **全部** running 时返回 `AlreadyApplied`；否则 `journal.Remove` 后重放，网络 apply 命中「已存在」幂等分支。这是自愈而非掩盖失败，与补偿清理、重复提交收敛不冲突。正确，不改。
5. **VM TAP 闭环（`LibvirtTeamLabProvider.NetworkInterface`）**：VM XML 用 `interface type="bridge"` + `virtualport type="openvswitch"` + `parameters interfaceid = TeamLabOvnNaming.LogicalPortName(...)`，这是 libvirt 标准 OVN 接入机制——libvirt 自建 TAP 并写入 OVS `external_ids.iface-id`，与 `TeamLabOvsAttachmentProvider` 的容器侧 `iface-id` 命名一致。**显式再调 `ovs.AttachAsync` 会重复建 Port/Interface，当前实现正确，不加复杂度。**（实机验证仍由测试 agent 负责，见残留风险。）
6. **network owner 串行化（`ApplyExecutionPlansAsync` 第 365–377 行）**：owner shard 先 apply、其余按 `shard.Id` 顺序，符合「全局网络先收敛」依赖——非 owner 的 `TeamLabOvnNetworkProvider.ApplyAsync` 走 `AllResourcesPresentAsync` 校验，必须等 owner 先落地 NB。每 shard 内部资产在 Agent 侧 `MaxDegreeOfParallelism` 有界并行。串行化是正确性要求而非无谓瓶颈，不改。

## 6. 其他边界核查（无问题）

- **解耦**：主站与 Agent 仅通过 `GZCTF.TeamLab.Contracts` 交互；`TeamLabController` 的 `execution-plan/*` 端点在 `ExecutionModel != V2` 时返回 404（fail-closed，符合基线 #6）；清理使用 `Control` 门类，不被长 apply 阻塞。
- **OVN 单事务（基线 #3）**：`OvsdbJsonRpcClient.TransactAsync` 单次 `transact` 提交全部 NB 操作，含 named-uuid 跨引用；所有权删除按 runtime/generation/plan-digest（`OvsdbJsonCodec.OwnedWhere`）。
- **迁移闭环**：`20260814000000/…10000/…20000` 三连迁移与模型快照一致，回填只把无 V2 快照的存量行标为 V1，不静默回填 V2。
- **清理分叉**：`TeamLabRuntimeCleanupService` 以「是否有 V2 计划快照」为唯一依据选择 V2 清理路径，缺失快照走 inventory 路径，不因缺快照卡死运行时。

## 7. 未执行的验证（如实记录）

- PostgreSQL Testcontainers 迁移升级/回滚验证未跑（本机 Docker Engine 不可用）。
- 实机 OVN/OVS、KVM/VM TAP、Docker 网络、多节点、并发与故障注入矩阵由另一测试 agent 负责，本审查不以此替代。

## 8. 已改动文件

- `src/GZCTF/Modules/TeamLab/Infrastructure/RedisTeamLabTrafficIngestor.cs`：`BufferLocally` 修正 `AcceptedCount`（见 3.1）。
