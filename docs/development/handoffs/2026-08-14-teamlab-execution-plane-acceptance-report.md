# TeamLab 执行面线上验收测试报告（2026-08-14）

- **日期**：2026-08-14
- **环境**：`10.0.7.118:8080`
- **部署版本**：`teamlab-hpa-fix-20260813-5`（Agent SHA-256 `c4835cb5...`，与主站 bundled 一致）
- **迁移头**：`20260812113416_AlignTeamLabExecutionRuntimeSchema`
- **节点**：118（Local Server，10.250.0.1）、125（worker-10.0.7.125，10.250.0.2），双节点 V2 能力稳定上报（6/6 采样），隧道/Fabric 均 Healthy
- **测试方式**：平台 HTTP API（admin cookie + open v1 API token），全程未修改任何现有比赛、运行时、VM、容器

---

## 1. 测试结果总览

| 组 | 用例 | 结果 | 证据 |
| --- | --- | --- | --- |
| 基线 | release 激活 / 双服务 active / 首页 200 | ✅ | 实测 |
| P0-A | 新建 TeamLab runtime（此前 100% 500） | ✅ 修复闭环 | 3 次创建均 202 + 部署 ready |
| 能力 | 118/125 V2 能力（execution-plan.v2 / ovs-ovn.v1） | ✅ 稳定 | 连续 6/6 采样均为 True，exec-op=1 |
| 部署 | Docker + Linux VM + Windows VM 双节点 | ✅ | runtime `019ffbdb-927c-77a6-b54c-36690c0eb935`，32~79 秒 ready |
| 生命周期 | pause → 全资产 Paused(8) | ✅ | open v1 API，operation 异步正常 |
| 生命周期 | resume → ready(5) | ✅ | |
| 生命周期 | reset → ready（新部署覆盖，gen=1） | ✅ | |
| 生命周期 | destroy → ready→destroying→destroyed(10) | ✅ | 约 10 秒 |
| 幂等 | destroy 缺 Idempotency-Key → 400 契约校验 | ✅ | 契约生效 |
| 清理 | DB 终态 / 队列 / Agent inventory / 快照 | ✅ | 3 个测试 runtime 均 Destroyed、0 非终态资产、队列 0 pending、容器/VM 无残留 |
| **V2 执行** | execution-plan/apply 路径 | ❌ **未触发** | 4 次创建（含带/不带 overlays）全部静默走 V1（Agent 日志均为 `shards/apply`，快照表 0 行） |

---

## 2. 关键缺陷（测试暴露的代码设计问题，按严重度）

### P0-1 V2 执行路径不可达且不可诊断（静默降级）

`TeamLabShardDeploymentService.DeployAsync`（`src/GZCTF/Modules/TeamLab/Application/TeamLabShardDeploymentService.cs`）：

```csharp
if (networkConfig.EnableExecutionPlanV2 &&
    await TryApplyExecutionPlansAsync(...))   // return false 时无任何日志
{
    executionPlanApplied = true; ...
}
else
{
    ... // 静默走 V1 legacy（shards/apply）
}
```

- `TryApplyExecutionPlansAsync` 存在多个静默失败点（`return false`），**全部无日志、无审计事件、无状态标记**
- 部署事件统一记录 "Runtime deployment completed successfully"，**V1 成功与 V2 成功在审计上完全同形**；`subStages` 不输出执行模型
- **实测影响**：4 次创建 runtime，系统未给出任何信号告知走了 V1；只能通过快照表 0 行 + Agent 日志 `shards/apply` 反推。任何验收、回滚决策、故障排查都建立在猜测之上

### P0-2 降级原因与影响面不成比例

```csharp
if (overlays.Values.Any(overlay => overlay.Secrets is { Count: > 0 }))
    return false;   // 整个 runtime 回落 V1
```

- 一个资产的一个 secret，导致**整个 runtime（所有 VM/Docker/网络）**放弃 V2、退回 legacy 网络路径
- 实测：默认创建（带 overlays）走 V1；显式传 `overlays: []` 仍走 V1 且无任何提示说明原因

### P1-3 执行决策依赖滞后的缓存事实

- 节点能力判定（`TryApplyExecutionPlansAsync` 内）读 DB `CapabilityManifestJson`（heartbeat 异步写入），非部署时刻实况
- `AgentCapabilityEvaluator.Supports` 返回 `bool`，不说明缺哪个 feature、哪个节点——诊断信息被吞掉

### P1-4 可空配置 + 默认值 = 配置断裂时静默退化

```csharp
IOptions<TeamLabNetworkConfig>? networkOptions = null)  // TeamLabShardDeploymentService 构造
private readonly TeamLabNetworkConfig networkConfig = networkOptions?.Value ?? new();
```

- `networkOptions` 可空、`EnableExecutionPlanV2` 默认 `false`——DI 或配置任一环断裂时系统**静默退回 V1**，无启动期或运行期警告

### P1-5 V1/V2 双路径并存

- 主站两条执行链（`shards/apply` legacy 与 `execution-plan/apply` V2）交织在同一个 `DeployAsync`；Agent 两套契约；清理侧按 `UsesExecutionPlanV2` 分支 + 反向回填快照（`BackfillMissingExecutionPlanSnapshotsAsync`）
- 复杂度翻倍，测试矩阵翻倍，回归难以发现（被静默降级掩盖）

---

## 3. 改进建议（最佳方向）

### 3.1 V1/V2 版本控制应作为显式配置开关（核心建议）

- **配置模型**：`TeamLabNetworkConfig.ExecutionModel`（或等价命名）作为显式开关，取值 `V1 | V2`，**默认 `V2`**
- **开关语义**：开关决定平台唯一允许的执行路径，**关闭 V2（或显式选 V1）是部署者的明确决策**，而不是运行时故障的兜底

### 3.2 不静默降级

- 关闭 `V2` 时，TeamLab runtime 创建**直接拒绝**（返回明确错误，如"平台未启用 V2 执行模型"），**绝不静默走 V1**
- 过渡期内如需保留 V1，降级必须是**显式的、带原因的、可审计的**：
  - runtime 持久化执行模型字段（`ExecutionModel = V1 | V2`），落库 + 审计事件 + API `subStages` 呈现
  - 任何降级写事件：`"execution-model fallback: node X missing feature Y (reason)"`
  - 删除后快照不可回查的问题：快照表不再清理即删，或删除前写终结事件

### 3.3 能力判定 fail-fast

- `TryApplyExecutionPlansAsync` → `RequireExecutionPlanCapabilitiesAsync`：
  - 不满足时**抛明确异常**（列出缺失 feature 的节点与 feature 名），由上层决定失败或显式降级
  - `Supports` 改为返回 `(bool, string[] missingFeatures)`，或直接抛

### 3.4 配置强校验

- `IOptions<TeamLabNetworkConfig>` **必注入**，`ValidateOnStart` 校验；配置缺失 → **启动失败**，而非运行时静默退化
- `EnableExecutionPlanV2` 默认值删除，改为显式配置

### 3.5 单一路径收敛

- V2 成为唯一执行路径后，删除 `shards/apply` legacy 链、legacy 清理分支、`UsesExecutionPlanV2` 判断、快照反向回填——代码量、Agent 契约、清理逻辑、测试矩阵一次性减半

### 3.6 secrets 与执行模型解耦

- 创建时即根据 release/overlays 是否含 secrets 显式选择执行模型并告知调用方，部署时不再"悄悄换路径"
- 或 V2 契约尽快补齐加密 secrets 投递（短中期）

---

## 4. 结论

- **V1 全链路（Docker/VM 混合双节点、pause/resume/reset/destroy、幂等契约、清理）全部通过**；P0-A（新建 500）修复闭环；125 V2 间歇缺失修复生效
- **V2 执行路径在代码改进前无法验收**：V2 不可达（4/4 静默回落 V1），OVN 组网、计划执行、内容校验、V2 清理矩阵均无法执行
- 根因是**代码设计的静默降级 + 双路径复杂化**，不是环境配置问题；按第 3 节改进后，V2 验收矩阵（正常/并发/故障/清理/观测）即可执行

## 5. 测试遗留与资源

- 测试资源全部清理：3 个测试 runtime 均 Destroyed、队列 0 pending、容器/VM 无残留、测试 token 已撤销（204）
- 未改动任何现有比赛、运行时、VM、容器；runtime 119 等旧路径资源不受影响
- 待 V2 可执行后补测：OVN 收敛、执行计划 apply/cleanup、digest 冲突收敛、多资产多网段（4/20/50）、并发（10/50/100/300）、故障注入、观测回放与残留核对
