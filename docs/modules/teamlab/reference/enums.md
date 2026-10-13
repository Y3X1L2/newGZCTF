# TeamLab：枚举、常量与错误码

本页按业务主题整理，保留原手册章节编号便于查找。先读 [模块入口](../README.md)；字段与调用约定见 [API 约定](api-conventions.md)，数值见 [枚举速查](enums.md)。

本文描述仓库契约，不证明当前现场全部可用。部署和真实链路验收见 [当前状态](../../../current-state.md)；接口以对应 Controller、Contracts 和生成 OpenAPI 为准。

## 15. 枚举与常量速查

HTTP JSON 中未标注字符串的枚举使用数字值。以下汇总本文用到的枚举。

### 15.1 资产与拓扑

| 枚举 | 数值 | 含义 |
| --- | --- | --- |
| `TeamLabAssetKind` | 0 | Docker |
|  | 1 | VM |
| `TeamLabHealthCheckKind` | 0 | TCP |
|  | 1 | HTTP |
| `TeamLabInfrastructureKind` | 0 | ManagedSwitch |
|  | 1 | ManagedRouter |
| `TeamLabConnectionDirection` | 0 | FromTo |
|  | 1 | Bidirectional |

### 15.2 运行时

| 枚举 | 数值 | 含义 |
| --- | --- | --- |
| `TeamLabRuntimeStatus` | 0 | Pending |
|  | 1 | Planning |
|  | 2 | Scheduled |
|  | 3 | Deploying |
|  | 4 | Probing |
|  | 5 | Running |
|  | 6 | Failed |
|  | 7 | CleanupPending |
|  | 8 | Paused |
|  | 9 | Destroying |
|  | 10 | Destroyed |
|  | 11 | Stopped |
| `TeamLabEventLevel` | 0 | Info |
|  | 1 | Success |
|  | 2 | Warning |
|  | 3 | Error |
| `TeamLabTrafficCaptureStatus` | 0 | Pending |
|  | 1 | Running |
|  | 2 | Stopping |
|  | 3 | Completed |
|  | 4 | Failed |
|  | 5 | Expired |
|  | 6 | CleanupPending |

Runtime 枚举依据 [当前源码](../../../../src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimePrimitives.cs)，Stopped=11 用于资产停止事实，与 Paused=8、Destroyed=10 区别。ApiOperation 则只有 0–3，见 [operation 契约](../../audit/reference/operations.md)。

### 15.3 观测与流量

| 枚举 | 数值 | 含义 |
| --- | --- | --- |
| `TeamLabObservationPointKind` | 0 | NetworkBridge |
|  | 1 | RouterFragment |
|  | 2 | FabricUplink |
|  | 3 | WorkloadEndpoint |
| `TeamLabTrafficEvidenceKind` | 0 | Packet |
| `TeamLabPathConfidence` | 0 | PacketExact |

> 注意：`confidence` 查询参数和响应字段均使用字符串 `packet-exact`。

### 15.4 远程会话（字符串序列化）

| 枚举 | 序列化值 |
| --- | --- |
| `TeamLabRemoteProtocol` | `containerTerminal`、`ssh`、`rdp`、`vnc` |
| `TeamLabRemoteSessionStatus` | `creating`、`ready`、`connected`、`ending`、`ended`、`failed` |

### 15.5 字符串枚举（请求/响应直接使用字符串）

| 类型 | 取值 |
| --- | --- |
| 链路策略 `kind` | `access-rule`、`nat`、`bandwidth-limit`、`latency`、`jitter`、`packet-loss`、`duplication`、`link-break` |
| 链路策略 `status` | `active`、`recovered`、`failed` |
| 连接器 `kind` | `managed-nic`、`vlan`、`segment`、`serial`、`usb-gateway`、`dedicated-network` |
| 连接器 `health` | `unknown`、`healthy`、`degraded`、`unreachable` |
| 租约 `releaseReason` | `none`、`manual-release`、`runtime-destroyed`、`admin-revoked`、`node-lost` |
| 设备包 `artifactKind` | `oci-image`、`vm-image` |
| 镜像准备 `state` | `onDemand`、`readyToStart`、`blocked` |

### 15.6 常见稳定错误码

| code | 含义 |
| --- | --- |
| `idempotency_conflict` | 幂等键重复但请求体不同 |
| `topology_revision_conflict` | 修订号过期 |
| `topology_invalid` | 校验未通过 |
| `scope_not_found` | 控制范围不可见/不存在 |
| `scope_archived` | 控制范围已归档，不能写 |
| `runtime_operation_in_progress` | 已有生命周期操作在跑 |
| `runtime_managed_by_rollout` | rollout 管理中的运行时不可直连操作 |
| `resume_blocked` | 恢复被阻止 |
| `rollout_not_drained` | 未排空就归档 |
| `rollout_target_not_found` | target 不存在 |
| `rollout_target_not_ready` | target 尚无运行中 runtime |
| `link_policy_parameters_invalid` | 链路策略参数不合法 |
| `traffic_filter_invalid` | 流量筛选参数不合法 |
| `connector_health_invalid` | 连接器健康值无效 |
| `device_package_parameter_schema_invalid` | 设备包参数 schema 不合法 |

---
