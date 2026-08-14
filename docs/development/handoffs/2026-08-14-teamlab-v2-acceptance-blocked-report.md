# TeamLab V2 验收阻塞报告（2026-08-14，release 5）

- **日期**：2026-08-14
- **环境**：`10.0.7.118:8080`，release `teamlab-ovn-observation-fix-20260814-5`
- **Agent SHA（进程实况）**：`9d9444bfc84fbad173ec0938dc5d426df4b067b0c93c695951706d6bfd168489`（118 与 125 一致）
- **双节点能力**：`execution-plan.v2` / `ovs-ovn.v1` / `artifact-cache.v2` / `libvirt.native.v1` 齐全，可调度
- **测试方式**：admin API 创建 trial runtime（`overlays: []`），E2E release `019fe773-7565-7f3f-a250-ea0252d4ce02`（3 资产：Docker + Linux VM + Windows VM，3 网段）

---

## 1. 测试结论

**V2 部署仍被阻塞**：`execution-plan/apply` 在 network 阶段失败（约 300ms 内），主站仅显示分类消息 "The network operation did not complete."。这是 release 3→4→5 三轮修复后**仍然存在**的同一现象（release 4 起 apply 已真实到达 Agent，不再静默降级）。

| 轮次 | release | apply 结果 | 现象 |
| --- | --- | --- | --- |
| 1 | `teamlab-execution-model-fix-20260814-2` | 静默走 V1（能力判定失败） | 无 V2 调用 |
| 2 | `teamlab-execution-model-fix-20260814-4` | V2 apply 秒败（network） | 分类消息无细节 |
| 3 | `teamlab-ovn-observation-fix-20260814-5` | V2 apply 秒败（network） | 分类消息无细节，真实错误在 125 Agent 日志 |

---

## 2. 我的测试方法自省（用户要求核实）

### 2.1 已自验证、确认无误的项
- **Agent 二进制实况**：直接调 125/118 的 `/api/status`，两节点进程实际 SHA 均为 `9d9444bf`（release 5），非主站 DB 缓存值。
- **探针链路**：向 125/118 直接 POST `execution-plan/apply`，两节点行为一致（伪造 plan 均被 digest 围栏拒绝，返回 `validation_failed`）——证明 Agent 校验链正常工作、两节点无行为差异。
- **digest 围栏**：手工构造的 plan 无法通过 `plan.IsValid()` 的 digest 一致性校验（`TeamLabExecutionPlanV2.cs` 末尾），证明防篡改生效（这是设计，不是故障）。
- **清理**：3 个失败测试 runtime 均被补偿清理（0 活跃资产、OVN 0 残留、快照 0）。
- **基础设施**：WireGuard 隧道健康（handshake 5s 内）、ping 10.250.0.2 通、OVN NB/SB 监听 `10.250.0.1:6641/6642`、双 chassis 注册、br-int/geneve 正常。

### 2.2 我的测试局限（可能影响判断的点）
1. **只用了单个 release**（`019fe773`，E2E 20260809，revision 29）。该 release 发布于 V2 成熟之前，其拓扑配置（DHCP/DNS/路由）可能与 V2 编译/OVN 应用存在兼容性问题。**尚未用其他 release（如 V29 `019fc0f4-...`、Two-Worker release）或新建最小拓扑验证**——这是当前最大的未覆盖变量。
2. **无法构造合法探针**：digest 围栏要求 plan 摘要必须与序列化内容一致，手工构造无法通过；因此无法用最小 plan 隔离"平台编译问题"与"Agent OVN 应用问题"。
3. **无法读取 125 的 Agent 日志**（SSH 凭据不同），真实 OVSDB 错误始终不可见。

---

## 3. 发现的代码缺陷（报错不详细）

### P1-1 主站丢弃 Agent 透传的真实错误（本轮核心问题）
- **Agent 侧已透传**：release 5 的 `TeamLabOvnNetworkProvider` 返回 `Failed("network", $"OVN transaction failed: {Trim(exception.Message)}")`，真实 OVSDB 错误在 apply 响应的**事件 detail** 中。
- **主站侧丢弃**：`TeamLabShardDeploymentService.ApplyExecutionPlanAsync`（约 439 行）只取 `response.Message`（Agent executor 的 `FailureMessage(stage)` 分类消息 "The network operation did not complete."），**不记录、不呈现响应事件数组中的 detail**。
- **结果**：任何测试者/运维在无 125 本机日志权限的情况下，**永远看不到真实错误**。三轮修复中测试者只能反复猜测，这正是"报错不详细"的直接代码根源。
- **修复建议**：主站在 apply 失败时，把响应 `events[].detail.summary`（含真实 OVSDB 错误）写入 `runtime.LastError` 与票据 `ErrorMessage`，并打日志。

### P1-2 主站日志缺 apply 失败的事件 detail
- 主站日志只输出 `RuntimeExecutionService: Deployment execution failed: ... error=TeamLab cleanup failed.`（或 apply 失败），无 Agent 事件明细。

### 观察项
- E2E release 的 `TeamLabRuntimeSecretEnvelopes` 曾有 1 行（早期创建时），说明该 release 资产可能带平台注入密钥（`GZCTF_SENSOR_*`）——V2 路径对这些密钥的处理是否符合预期，需修复方确认（验收清单第 5 项：平台注入密钥不受影响）。

---

## 4. 给修复方的最小行动项

1. **取 125 Agent 日志**：`journalctl -u gzctf-agent`，在 apply 时刻（2026-08-14 06:28:59 UTC / 14:28:59 CST，runtime `019ffef5-9457-751f-bbe9-2d79368d3565`）查 `TeamLab network apply failed` 日志行，其 `{Message}` 即真实 OVSDB 错误。
2. **修 P1-1**：主站保留并展示 Agent 透传的真实错误（事件 detail → LastError/票据/日志）。
3. **测试方补充验证**：换 V29 release / 新建最小拓扑（1 网段 1 Docker）重测，隔离 release 拓扑兼容性问题。

---

## 5. 测试资源与清理

- 3 个失败测试 runtime：`019ffe86`、`019ffe9b`、`019ffef5`，均已失败并补偿清理（Status=6，0 活跃资产，OVN 0 残留，快照 0）。
- 存量 runtime 119（V1，READY）未受影响；队列无新增 pending。
- 测试 token 未创建（admin cookie 全程足够）。

---

## 6. 修复方处置结果（2026-08-14）

- 根因已定位：`TeamLabOvnNetworkProvider.StableUuid` 生成的 `uuid-name` 以数字开头，不符合 OVSDB `<id>` 语法，OVN Northbound 事务报 `Type mismatch for member 'uuid-name'`。修复为 `gzctf_{kind}_{32位hex}`（kind 中的 `-` 替换为 `_`），并修正单测从 `Guid.TryParse` 改为 OVSDB `<id>` 语法校验。
- 报错链路已闭环：Agent 事件 `Detail.summary` 与 apply/cleanup 顶层 `Message` 保留真实错误；主站 apply/补偿清理失败时优先取事件 detail，写入失败消息并记录日志。删除泛化 `FailureMessage` 映射，不新增错误分类机制。
- 本地 Release build 通过；`TeamLabOvnNetworkProviderTests` 3/3 通过。
- 待办：部署 118/125 新 Agent，复验 `execution-plan/apply` 真实通过后继续 V2 全链路验收。
