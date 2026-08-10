# TeamLab 外部控制面 全链路验收记录（2026-08-06）

> 计划：`docs/development/test-plans/2026-08-06-teamlab-control-plane-e2e.md`
> 环境：`10.0.7.118:8080`；部署版本：`teamlab-rollout-control-fix3-20260806-135407`（gitCommit 9ae39d45）
> 场景：拓扑 `019f68d5-...`（Phase 9 Mixed Docker Linux Windows AD Acceptance），release **V29**（`019fc0f4-...`），4 资产（ad-dc WinVM / core-portal Docker / entry-edge Docker / linux-service LinuxVM）+ 4 网段
> 认证：admin 创建 API token（resources: `teamlab-scope:00000000-0000-7000-8000-000000000001`，即内置 platform scope）

## 1. 测试结果汇总

| 组 | 用例 | 结果 | 证据 |
| --- | --- | --- | --- |
| A | 环境基线（服务/迁移/镜像契约） | ✅ | 迁移头 `20260805050703`；40 镜像 `remoteAccessProtocol` 全为 null/小写字符串 |
| B | Scope 列表/越权 404/未认证 401 | ✅ | 无授权 token 查询返回 404（同不存在资源） |
| C1 | 同 key 同 body ×2 | ✅ | 返回同一 operation `019fd59b-e9da...`（202） |
| C2 | 同 key 异 body | ✅ | `409 idempotency_conflict`（含 traceId） |
| C3 | 并发同 key ×5 | ✅ | 5/5 返回同一 operation，DB 仅 1 行 |
| C4 | 并发异 key 同 externalReference ×3 | ✅ | 1 成功 + 2 `rollout_reference_conflict`（DB 唯一约束兜底） |
| C5 | 无 Idempotency-Key | ✅ | `400 validation_failed` |
| E | 生命周期：create→prepare→rebuild→open-access→drain→archive | ✅ | 见 §2 时序 |
| F1 | pause 门控 | ✅ | `PauseRequested=true`；已运行 target 保持（协调暂停语义） |
| F1b | paused 下 open-access | ✅ | operation 终态 `rollout_paused`（"打开访问前请先恢复 rollout"） |
| F2 | 重复 pause | ✅ | 幂等 completed |
| F3/F8 | resume / 重复 resume | ✅ | 恢复协调，无副作用 |
| F5 | paused 下 drain | ✅ | drain 清除 pause 并完成销毁 |
| G1 | 并发 pause+resume | ✅ | 终态收敛为 pauseRequested=true（二选一），无 500 |
| G5 | Ready 稳态收敛 | ✅ | Revision 60s 稳定（4→4，UpdatedAt 不变） |
| H3 | prepare 完成后再部署重启 | ✅ | 重启后 runtime/target/rollout 状态保持 |
| L1 | 未 drain 归档 | ✅ | `rollout_not_drained`（即使 target 仅 Pending） |
| L2 | drain 后清理 | ✅ | runtime Destroyed、票据 Succeeded、队列 0、内存回落 |
| M | V29 端到端（2 Docker + 2 VM 真实拉起） | ✅ | runtime 96/97/98 全量资产部署成功、销毁成功 |

## 2. 关键时序证据

- **幂等链**：create×2 同一 operation → prepare（首个版本被 P1 bug 阻断，修复后）→ target-a Ready（~2 分钟）→ open-access 在 blocked 下 operation 终态 `rollout_not_ready`（不打开）→ rebuild target-b → 全 Ready → open-access → 全 AccessOpen（~10s）→ pause → paused 下 open-access `rollout_paused` → resume → drain（runtime 依次 Destroyed，~12s）→ rollout `completed` → archive → `archived`
- **并发链**：5×同 key = 1 operation；3×异 key = 1 rollout + 2 显式失败（`rollout_reference_conflict`），错误为中文（"rollout 的外部 reference 已被使用"）
- **重启链**：服务重启（sudo systemctl restart）→ 运行中 runtime 保持 status=5（running），target/rollout 不变

## 3. 发现并已修复的问题

### P1（阻断级，已修复并部署 fix3）
**Rollout prepare 完全不可用**：coordinator 内部提交 target 幂等键 `"teamlab-rollout-target:{PublicId:D}"` 含冒号 `:`，违反 `ExternalIdempotencyKey`（仅允许字母数字 `-_.`）→ 每个 target 进入 Failed："Idempotency-Key must contain 1-128 ASCII"。
修复：`TeamLabExternalRolloutProvider.cs` 改为 `teamlab-rollout-target-{PublicId:N}`。
验证：修复后 prepare/rebuild 均成功拉起真实运行时（含 2 个 VM）。

### P2（功能缺口，需产品决策）
**target `Paused` 状态不可达**：`TeamLabRolloutTargetStatus.Paused=8` 定义为 runtime-fact 投影，但：
- rollout 级 pause 只设置 `PauseRequested` 门控（不暂停 workload，已运行 target 保持运行）
- runtime 级 pause API 对 rollout 托管 runtime 返回 `409 runtime_managed_by_rollout`
- 管理端无 runtime pause 端点
→ 无任何路径触达 runtime Paused → target 永不到 Paused。
**选项 A**：rollout pause 联动暂停各 target workload（Agent-backed，符合计划 Task 4 状态机 `...AccessOpen -> Paused -> Draining...`）；**选项 B**：确认"纯协调暂停"为最终语义，移除或文档化 Paused=8。

### P3（运维提示，非本计划引入）
**部署重启会登出既有会话**：fix3 部署后，部署前签发的 admin cookie 401（普通重启实验证明 cookie 可跨重启有效，差异可能来自发布激活流程或 key 轮换，未定根因）。建议部署后重新登录；与 DataProtection key 生命周期一并复查。

## 4. 未覆盖/受限项（如实记录）

| 项 | 原因 |
| --- | --- |
| B5/B6 归档 scope 行为 | 内置 platform scope 不可归档，未建独立 scope 归档链路测试 |
| D 组 open v1 topology 创建/布局 digest | 避免修改生产拓扑；管理端既有拓扑契约由前端与修复后 API 覆盖 |
| G6 空 desired 集 | 未构造 0-target rollout（其 Blocked 分支与 C4 失败路径同源） |
| H2 DB 删 ticket 恢复 | 需模拟数据库破坏，风险高，未执行（代码路径存在，建议集成测试覆盖） |
| I 组独立 runtime 创建/reset | rollout 托管路径已覆盖；独立 runtime 消耗资源较大 |
| J 组 remote-access PATCH | 未改动模板 79 现有配置（只读验证契约） |
| K 组真实浏览器交互 | API 契约层验证通过；浏览器端需人工复核（设计器已修好加载） |

## 5. 清理结果

- 5 个测试 rollout（e2e-ctl-*）全部 `archived`；3 个测试 runtime 全部 `Destroyed`；相关票据全部 `Succeeded`
- 测试 API token 已撤销（`RevokedAt` 已写）；测试文件已删除
- 执行中队列 0；测试后无 `e2e-ctl-*` 活跃资源
- 生产拓扑/镜像/节点数据未改动

## 6. 结论

核心链路（幂等、并发唯一约束、rollout 生命周期、pause 门控、drain/archive、服务重启恢复）全部通过，P1 阻断 bug 已修复部署。**P2（Paused 不可达）需产品决策后实施**；P3 部署登出建议复查。前端浏览器交互与 D/I 组剩余项建议在决策后随 P2 一并补测。


---

## 附录 A：P2 修复实施记录（2026-08-06 晚，决策：选项 A——rollout pause 联动冻结 workload）

**决策**：按计划 Task 4 状态机（...AccessOpen -> Paused -> Draining...）与 Task 5（stop workload processes, preserve overlays/network/addresses/generation/access state），rollout 级 pause 必须真实冻结场景（Agent-backed），target Paused 由 runtime-fact 投影呈现。

**部署版本**：teamlab-rollout-control-fix7-20260806-181340（含迁移 20260806085000_MakeApiOperationTokenOptional）

**改动**：
| 文件 | 改动 |
| --- | --- |
| TeamLabRolloutCoordinator.cs | ProcessBatchAsync 放开 pause 过滤（paused rollout 也进协调）；新增 ProcessPauseRequestsAsync（对 Ready/AccessOpen/Provisioning target 提交 RuntimePause operation）与 ProcessResumeRequestsAsync（对 Paused target 提交 RuntimeResume，幂等收敛）；内部幂等键 teamlab-rollout-target-{targetId:N}-pause/resume（合法字符） |
| TeamLabRuntimeOperationApplicationService.cs | 新增 SubmitRolloutTargetLifecycleAsync（ApiTokenId=null、payload 带 RolloutId/RolloutTargetId 受保护标记） |
| TeamLabRuntimeOperationHandler.cs | scope 校验与托管检查对 payload.RolloutId != null 放行；RuntimePause/Resume 分支路由到 PauseRolloutTargetAsync/ResumeRolloutTargetAsync（rollout PublicId→Id 转换） |
| TeamLabRuntimeOrchestrator.cs | 新增 PauseRolloutTargetAsync/ResumeRolloutTargetAsync（RequireRolloutTargetAsync 校验归属）；FailLifecycleAsync 错误事件补 OperationalError（预存 bug：失败事件无 category/code 抛 ArgumentException） |
| ApiOperation.cs + 迁移 MakeApiOperationTokenOptional | ApiTokenId 改 nullable（计划原文 optional scope beside its existing token/user facts）；手工修正迁移（含 [Migration] 特性）与模型快照同步 |
| Agent 运维 | gzctf-agent 同步到 worker 节点 10.0.7.125（此前仅同步主节点，旧 Agent 无 assets/pause 端点 → 404 agent.feature_missing） |

**验证（V29 场景实测）**：
- prepare → ready → pause：target=8(Paused)、runtime=8(Paused)，Agent 双节点收到 assets/pause（200），主站日志 Runtime pause completed.
- API 呈现：status=paused, runtimeStatus=8, runtimeStage=paused ✓
- paused 下 open-access 等命令仍被 rollout_paused 拒绝（回归通过）
- resume：target 回 Ready、runtime 回 Running，主站日志 Runtime resume completed. ✓
- drain/archive 正常；测试资源全部清理

**暴露并修复的预存问题**：FailLifecycleAsync 记录失败事件缺 error（中文化重构遗留）；worker 节点 Agent 未随主站同步（运维流程缺口，建议发布脚本默认同步全部在线节点）。

**遗留**：browser 管理端 paused 状态呈现（前端已含映射，待人工复核）；P3（部署登出）未再排查。
