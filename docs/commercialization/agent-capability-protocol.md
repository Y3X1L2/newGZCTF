# Agent Capability Protocol

## 1. Purpose

本文冻结 GZCTF 主站与 `GZCTF.Agent` 之间的节点能力协商协议，供 Phase 6 调度、节点注册、Agent 同步、镜像分发和 TeamLab 多节点运行共同使用。

协议目标：

- 调度按具体能力判断，不使用 `protocolVersion >= N` 表达所有功能。
- Docker、KVM、VM cloud-init、TeamLab Fabric、WireGuard、flow、PCAP、镜像下载和自更新独立声明。
- Agent 版本、二进制摘要、静态能力、执行限制、主机事实和 TeamLab 运行健康分开表达。
- 旧 Agent、依赖缺失、能力回报过期和同步未完成均产生稳定、可读、可审计的不可调度原因。
- feature ID 和 JSON schema 可以独立演进，不要求主站与所有 WorkerNode 同时替换。

## 2. Current Code Evidence

- `src/GZCTF.Agent/Services/AgentCapabilityService.cs` 生成 schema `1` manifest、稳定 feature set、host facts 和分类 execution limits。
- `src/GZCTF.Agent/Controllers/StatusController.cs` 与 heartbeat 共用同一 manifest 生成器，并缓存计算 Agent binary SHA-256。
- `src/GZCTF/Services/Fleet/AgentCapabilityEvaluator.cs` 只按 schema 和 required feature subset 判断能力；Normalize 排序 feature，并从持久化 JSON/hash 排除动态 `ObservedAt`。
- `src/GZCTF/Controllers/NodesController.cs` 仅在 capability hash 变化时更新 manifest/coarse capability；旧 Agent 上报空 binary hash 时保留主站已知摘要。
- `src/GZCTF/Modules/Runtime/Application/NodeEligibilityEvaluator.cs` 将静态 feature、动态健康、Docker/KVM 独立槽位和过载门禁组合为可读不可调度原因。
- `src/GZCTF/Modules/Runtime/Application/NodeDispatchLimiter.cs` 与 Agent 本机 operation gate 分别提供主站 dispatch 限流和节点最终保护。
- `20260713014106/14659/15237` 三段迁移新增 manifest 字段、回填旧 Agent 版本事实并删除旧 protocol/capability 列。

活动业务代码不保留 `protocolVersion >= N` 判断；历史 migration 中的旧字段只用于升级数据回填。

## 3. Protocol Layers

Agent 协议分为四层：

| 层 | 含义 | 是否参与调度 |
| --- | --- | --- |
| Manifest schema | `/api/status` JSON 的结构版本 | 只决定能否解析 |
| Feature set | Agent 能执行的具体稳定合约 | 是，required subset 必须满足 |
| Host facts / execution limits | 主机硬件事实和本机并发保护 | 是，用于校验和 dispatch 限制 |
| Runtime health | heartbeat、TeamLab tunnel、Fabric、Registry 可达性 | 是，按对应 workload 动态判断 |

禁止把四层再次压缩为一个 `Available` 布尔值或全局 protocol number。

## 4. Status Endpoint

### 4.1 Endpoint

```http
GET /api/status
Authorization: Bearer <node-auth-token>
Accept: application/json
```

Agent status endpoint 继续使用节点 auth token。未认证请求返回 `401`；不得公开主机能力、路径、二进制摘要或依赖诊断。

### 4.2 Response contract

```csharp
public sealed record AgentStatusResponse(
    string AgentVersion,
    string? BinarySha256,
    int ManifestSchemaVersion,
    IReadOnlySet<string> Features,
    AgentExecutionLimits ExecutionLimits,
    AgentHostFacts Host,
    DateTimeOffset ObservedAt);

public sealed record AgentExecutionLimits(
    int DockerCreates,
    int VmCreates,
    int DockerImageTransfers,
    int VmImageTransfers,
    int TeamLabNetworkOperations,
    int ControlOperations);

public sealed record AgentHostFacts(
    int LogicalCpuCount,
    long TotalMemoryMiB,
    long AvailableMemoryMiB,
    long ImageStorageTotalMiB,
    long ImageStorageAvailableMiB,
    bool KvmDevice,
    bool CpuVirtualization);
```

JSON 使用 `camelCase`，feature 集合按 ordinal 升序返回，所有容量为非负整数。`ObservedAt` 使用 UTC ISO-8601。

示例：

```json
{
  "agentVersion": "6.0.0",
  "binarySha256": "d95f0a5c1d5d6c88a0fdabf72f6589b50946d168c1655ea84d2ee51f0bb5427b",
  "manifestSchemaVersion": 1,
  "features": [
    "image.docker.pull.v1",
    "image.vm.download.v1",
    "maintenance.self-update.v1",
    "runtime.docker.v1",
    "runtime.kvm.v1",
    "runtime.vm.cloud-init.v1",
    "teamlab.fabric.l3.v1",
    "teamlab.flow.v1",
    "teamlab.pcap.v1",
    "teamlab.wireguard.v1"
  ],
  "executionLimits": {
    "dockerCreates": 8,
    "vmCreates": 2,
    "dockerImageTransfers": 2,
    "vmImageTransfers": 1,
    "teamLabNetworkOperations": 4,
    "controlOperations": 2
  },
  "host": {
    "logicalCpuCount": 16,
    "totalMemoryMiB": 32768,
    "availableMemoryMiB": 24576,
    "imageStorageTotalMiB": 524288,
    "imageStorageAvailableMiB": 401532,
    "kvmDevice": true,
    "cpuVirtualization": true
  },
  "observedAt": "2026-07-12T14:30:00Z"
}
```

## 5. Manifest Schema Version

- Phase 6 当前 schema 为 `1`。
- schema 只描述 JSON transport 结构，不描述 Docker、KVM 或 TeamLab 功能等级。
- 主站支持的 schema 集合首版为 `{1}`。未知 schema 返回 `agent_manifest_schema_unsupported`，节点保留注册事实但不参与运行调度。
- 新增 optional JSON 字段不提升 schema；删除字段、改变字段类型或改变必填语义时发布新 schema。
- 同一 Agent 可以只返回一个 schema。主站升级顺序必须先支持新 schema，再发布使用新 schema 的 Agent。

## 6. Feature Catalog

### 6.1 Feature naming

格式固定为：

```text
<domain>.<capability>.v<contract-revision>
```

- 只允许小写 ASCII 字母、数字、点和短横线。
- feature ID 是稳定合约，不是展示文案。
- 兼容增强保留原 ID；破坏请求/响应、执行语义或安全边界时发布新 revision。
- Agent 可以同时声明旧/新 feature，主站按 workload 的 required set 判断。

### 6.2 Phase 6 feature set

| Feature ID | Agent 必须满足 | 主站使用场景 |
| --- | --- | --- |
| `runtime.docker.v1` | Docker daemon 可用，create/inspect/destroy 合约健康 | 普通 Docker、培训、AWDP、TeamLab Docker |
| `runtime.kvm.v1` | `virsh`、libvirt system connection、`/dev/kvm`、CPU virtualization 均可用 | Linux/Windows VM、TeamLab VM |
| `runtime.vm.cloud-init.v1` | seed ISO 工具和 VM cloud-init request 合约可用 | Linux VM 动态注入 |
| `image.docker.pull.v1` | 能从配置的内网 Registry pull、inspect、delete | Docker 预分发和启动兜底 |
| `image.vm.download.v1` | 能下载内网 VM artifact、校验 sha256、原子替换和删除 | VM 预分发和启动兜底 |
| `teamlab.fabric.l3.v1` | `ip`、namespace/route、`iptables` 或 `nft` 的 L3 Fabric 合约可用 | TeamLab shard、跨节点路由 |
| `teamlab.wireguard.v1` | `wg` 和 WireGuard interface 合约可用 | 玩家入口和 Worker tunnel |
| `teamlab.flow.v1` | flow collector 命令和增量 cursor 合约可用 | 默认流量元数据 |
| `teamlab.pcap.v1` | `tcpdump` 或 `dumpcap`，时长/大小限制和状态合约可用 | 按需 PCAP |
| `maintenance.self-update.v1` | hash 校验、临时文件、原子替换、systemd restart 合约可用 | 节点“同步最新版本” |

### 6.3 Derived coarse capabilities

`WorkerNode.Capabilities` 保留为数据库查询投影：

```csharp
NodeCapability.Docker <=> features contains "runtime.docker.v1"
NodeCapability.Kvm    <=> features contains "runtime.kvm.v1"
```

投影只能由 `AgentCapabilityEvaluator` 生成。Controller、heartbeat 和 TeamLab service 不得各自维护转换函数。

## 7. Workload Requirement Matrix

| Workload | Required feature set | Required runtime health |
| --- | --- | --- |
| 普通 Docker create | `runtime.docker.v1`, `image.docker.pull.v1` | heartbeat fresh、Docker 可调度 |
| 普通 VM create | `runtime.kvm.v1`, `image.vm.download.v1` | heartbeat fresh、KVM 可调度 |
| Linux VM cloud-init | 普通 VM + `runtime.vm.cloud-init.v1` | 同上 |
| TeamLab Docker shard | `runtime.docker.v1`, `image.docker.pull.v1`, `teamlab.fabric.l3.v1` | Fabric healthy；入口 shard 另需 WireGuard healthy |
| TeamLab VM shard | `runtime.kvm.v1`, `image.vm.download.v1`, `teamlab.fabric.l3.v1` | Fabric healthy；入口 shard 另需 WireGuard healthy |
| TeamLab player entry | `teamlab.fabric.l3.v1`, `teamlab.wireguard.v1` | tunnel/fabric healthy、UDP mapping 可分配 |
| Flow metadata | `teamlab.flow.v1` | 对应 runtime network running |
| PCAP | `teamlab.pcap.v1` | 对应 capture point running |
| Agent sync | `maintenance.self-update.v1` | heartbeat fresh；维护接口认证通过 |

Docker-only TeamLab 不要求 `runtime.kvm.v1`。非入口 TeamLab shard 不要求 WireGuard player-entry feature。

## 8. Runtime Health

静态 feature 不能替代动态健康：

- `heartbeatFresh`: 节点 live state 未超过 `WorkerNode.DefaultHeartbeatTimeout`。
- `dockerSchedulable`: Docker feature 存在，节点调度开关开启，Docker execution limit 大于 0。
- `kvmSchedulable`: KVM feature 存在，KVM execution limit 大于 0。
- `fabricHealthy`: `TeamLabNetworkEnabled` 且 `TeamLabFabricStatus == Healthy`。
- `wireGuardHealthy`: tunnel status healthy、tunnel IP 有效、最近配置已应用。
- `storageReachable`: 最近镜像传输结果；只影响需要拉取的节点，不使 Ready cache 失效。

节点详情 API 返回静态 feature 和动态 health 的独立结果。UI 不显示笼统“VPN 可调度”来掩盖具体缺失项。

## 9. Heartbeat Contract

heartbeat 保存两类数据：

```csharp
public sealed record AgentHeartbeatRequest(
    Guid NodeId,
    long Sequence,
    DateTimeOffset ObservedAt,
    NodeLiveMetricReport Metrics,
    AgentCapabilityManifest Manifest,
    TeamLabRuntimeHealthReport TeamLabHealth);
```

- `Metrics` 进入 Phase 5 Redis live state/分钟 checkpoint。
- `Manifest` 是低频持久事实；只有 `CapabilityHash` 变化时更新 PostgreSQL manifest 和 coarse projection。
- `TeamLabHealth` 是动态运行健康，不能修改静态 feature set。
- metric sequence 过期时拒绝旧 metric，但仍允许更新更高可信度的 manifest/hash 和 Agent version；沿用 Phase 5 已修复语义。
- `ObservedAt` 与主站 receive time 同时保存；调度 freshness 使用主站 receive time，避免节点时钟漂移使节点永久在线。

## 10. Execution Limits

Agent 本机 operation gate 是最终保护：

| Limit | 默认值 | 作用 |
| --- | --- | --- |
| DockerCreates | `clamp(logicalCpu / 2, 2, 8)` | network ensure、container create/start/attach；不包含 pull 和长期 probe |
| VmCreates | CPU >= 16 时 2，否则 1 | overlay、seed、virt-install；boot probe 不长期持有 permit |
| DockerImageTransfers | 2 | Docker pull/inspect |
| VmImageTransfers | 1 | VM artifact download、sha256、原子替换 |
| TeamLabNetworkOperations | 4 | shard/network/route/Fabric apply 与 cleanup |
| ControlOperations | 2 | stop、destroy、rollback、缓存清理的独立保留通道 |

- 自动值由 Agent 启动时根据 host facts 计算，配置可逐项覆盖。feature 不存在时对应 limit 为 0；feature 存在时必须为 `>= 1`。非法组合使对应 readiness unhealthy，不静默改成无限制。
- 不同 operation category 使用独立 semaphore，VM 镜像下载不会阻塞 Docker pull，镜像传输不会占用 container/VM create permit。
- control 操作必须走独立 permit，不能与 create 共用队列，也不能被 create backlog 永久阻塞。
- VM create permit 在 `virt-install` 返回并确认 domain 存在后释放；boot/service probe 由主站有界并行，不用慢启动长期串行后续 VM create。
- 主站读取 limit 用于 dispatch 批次估算；即使多个主站实例同时调用，Agent gate 仍保证本机上限。

### 10.1 Single-flight and identity locks

- Docker image key 为解析后的 normalized registry reference；可获得 digest 时使用 digest。同 key 只有一个真实 pull，等待者共享结果。
- VM image key 为 `templateId + expectedSha256`。同 key 只有一个 `.part` owner；完成后写 digest sidecar 并原子替换目标 qcow2。
- 共享传输使用 Agent 服务级 deadline；单个 HTTP 请求取消只取消等待，不传播到共享任务。
- Docker network 以 network name 加 keyed lock，container 以稳定 identity/generation 加 keyed lock；VM 以 VM name/generation 加 keyed lock。
- 相同 generation 已存在且规格一致时 create 返回 `alreadyExists = true`；规格不一致返回 `runtime_identity_conflict`。create 不得先无条件删除已有 container/domain。
- Docker/VM create 遇到本地镜像缺失返回 `image_not_ready`，不得在 create 内隐式执行 pull/download。

### 10.2 Request deadlines and retry boundary

| Operation | Default deadline |
| --- | --- |
| status / heartbeat probe | 5 seconds |
| network / control | 60 seconds |
| Docker create | 3 minutes |
| VM create | 5 minutes |
| image transfer | 2 hours total and 2 minutes without progress |

主站不再设置统一 10 分钟 HttpClient timeout。只有 create/cleanup 已具备 identity inspect 幂等语义后，连接建立失败、连接重置或响应读取中断才能自动重试一次；明确业务错误、4xx、hash mismatch 和 capacity error 不重试。

## 11. Persistence Model

`WorkerNode` 保存：

```csharp
string? AgentVersion
string? AgentBinarySha256
int CapabilityManifestSchemaVersion
string CapabilityManifestJson
string CapabilityHash
DateTimeOffset? CapabilityObservedAt
```

同时保存由 manifest 导出的 `NodeCapability` bit flag。以下旧字段在 Phase 6 Contract 删除：

```text
TeamLabAgentVersion
TeamLabProtocolVersion
TeamLabCapabilitiesJson
```

manifest JSON 不用于任意 ad-hoc 查询；调度通过 evaluator 的 typed snapshot 和 coarse projection。raw JSON 用于审计、节点详情和未知 optional feature 前向兼容。

## 12. Agent Sync Contract

同步状态机：

```text
Requested
  -> HashChecking
  -> AlreadyCurrent | Downloading
  -> Verifying
  -> Replacing
  -> Restarting
  -> WaitingForHeartbeat
  -> CapabilityConfirmed
  -> Succeeded | Failed
```

- 主站先读取当前 binary sha256；一致时不下载、不替换、不重启。
- 下载到临时文件，sha256 一致后原子替换。
- systemd restart 后必须收到新 binary hash 的 heartbeat。
- 随后验证当前主站要求的基础 features；缺失 feature 返回 `agent_sync_capability_missing`，不能标记同步成功。
- 同步不自动安装 Docker/KVM/TeamLab OS 依赖；节点注册 bootstrap 负责“缺什么装什么”，sync 只更新 Agent 制品和配置。
- 同一节点只允许一个 active sync，重复请求返回同一 operation。

## 13. Error Contract

| Code | 含义 |
| --- | --- |
| `agent_manifest_missing` | 节点尚未上报 capability manifest |
| `agent_manifest_schema_unsupported` | 主站无法解析 schema |
| `agent_manifest_invalid` | feature、limit、host fact 不合法 |
| `agent_feature_missing` | workload 所需 feature 缺失 |
| `agent_heartbeat_stale` | 节点运行健康过期 |
| `agent_execution_limit_invalid` | Agent gate 配置无效 |
| `image_not_ready` | create 所需节点本地镜像尚未验证就绪 |
| `runtime_identity_conflict` | 相同运行 identity/generation 已存在但规格不一致 |
| `agent_sync_hash_mismatch` | 下载制品摘要不匹配 |
| `agent_sync_heartbeat_timeout` | 重启后未收到新 heartbeat |
| `agent_sync_capability_missing` | 新 Agent 未回报主站所需基础能力 |

错误响应包含 `code`、`message`、`nodeId`、`missingFeatures` 和 correlation ID；不返回 auth token、远端 sudo 密码、完整 shell、Registry 凭据或配置正文。

## 14. Rollout and Compatibility

Phase 6 部署顺序：

1. 主站先支持 schema 1、feature evaluator 和新旧字段 Expand schema，但保持新调度未启用。
2. 通过节点同步或重新注册发布新 Agent，等待 manifest 回报。
3. 所有参与调度节点 manifest 可解析后启用 feature-based scheduler。
4. Backfill `WorkerNode.Capabilities` 和通用 Agent 字段。
5. Contract 删除旧 protocol/TeamLab capability 字段和代码判断。

旧 Agent 不进行猜测兼容：未上报 manifest 的节点保留在线和管理能力，但标记 `agent_manifest_missing`，不参与新运行调度。普通容器不能因为“历史上可能支持 Docker”绕过 feature 门禁。

## 15. Security Requirements

- status、heartbeat、sync 和维护接口必须使用节点身份认证。
- capability 只陈述能力，不接受主站下发任意命令或任意 feature 名执行。
- Agent endpoint 仍使用 typed request、输入校验和集中 command builder/shell escape。
- `BinarySha256`、feature、host facts 可以写审计；auth token、sudo password、WireGuard private key、Flag 和 userdata 禁止进入日志。
- capability manifest 最大 16 KiB，feature 数最大 128，单个 feature 最大 96 ASCII 字符，防止异常节点放大数据库和日志。
- 主站只接受已注册 NodeId 的 heartbeat，NodeId 与 auth token 必须匹配。

## 16. Verification

自动测试必须覆盖：

- 主站和 Agent 对固定 schema 1 JSON fixture 双向序列化一致。
- unknown optional feature 被保留但不影响已知 feature。
- unknown schema fail closed。
- Docker-only 节点缺 KVM 时仍满足 Docker 和 TeamLab Docker；VM 被拒绝。
- KVM 只有 `virsh`、但缺 `/dev/kvm` 或 CPU virtualization 时不声明 `runtime.kvm.v1`。
- TeamLab Fabric 存在但 WireGuard 缺失时可承载非入口 shard，不可承载 player entry。
- metric sequence stale 不覆盖 live metric，但新 capability hash 仍能持久化。
- binary hash 一致时 sync 不下载、不重启；hash 变化后必须等到新 heartbeat + required features 才成功。
- Agent gate 在多个并发请求下分别遵守 Docker create、VM create、Docker image、VM image、network 和 control limit，control cleanup 可在 create backlog 中执行。
- 同一 Docker/VM 镜像的 20 个并发请求只产生一次真实传输；取消一个等待者不影响其他等待者。
- 相同 container/VM generation 的并发 create 收敛为一个资源；连接中断后的单次重试不会删除已成功 VM。
- 生产代码中 `TeamLabProtocolVersion`、`protocolVersion <`、`protocolVersion >=` 和 `TeamLabCapabilitiesJson` 命中为零。

真实节点验收必须在 Docker-only 和 Docker+KVM 节点各执行一次：读取 status、心跳落库、节点页展示、普通调度、TeamLab 调度和同步闭环。
