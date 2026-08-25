# Phase 9 TeamLab 组网独立代码审查 — 链路 4.3 Shard 网络、路由和 Fabric

- 审查日期：2026-07-21
- 审查范围：链路 4.3（Shard 网络、路由和 Fabric）
- 审查规范：`docs/commercialization/phase-09-teamlab-networking-independent-code-review.md`
- 代码仓库：`D:/newgz/newGZCTF-main/`（.NET / C#）
- 审查人：独立 sub-agent

---

## 1. 审查范围与覆盖

### 1.1 已实际打开并读取的主站代码文件

| 文件 | 关键关注点 |
| --- | --- |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabFabricLinkAllocator.cs` | /30 link pool 分配器（`LinkPrefixLength = 30`，从 `_config.FabricLinkPool` 读取） |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabResourceNameFactory.cs` | `FabricHostInterface(runtimeId)` / `FabricNamespaceInterface(runtimeId)` 仅按 runtimeId 命名（不带 shardId/generation） |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabInfrastructurePorts.cs` | 端口分配 |
| `src/GZCTF/Modules/TeamLab/Domain/TeamLabInfrastructurePrimitives.cs` | 基础设施领域原语 |
| `src/GZCTF/Modules/TeamLab/Domain/TeamLabNetworkLease.cs` | 网络租约领域模型 |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOverlayService.cs` | runtime overlay 编排 |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeProjectionService.cs` | runtime 投影 |
| `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs` | 主站→Agent 调用入口（1123 行） |
| `src/GZCTF/Modules/TeamLab/Application/TeamLabRouteApplicationService.cs` | `BuildForwardPolicies`（L267-L278）生成有向 forward policies；调用 `TeamLabReachabilityCompiler.Compile` 区分 FromTo/Bidirectional |
| `src/GZCTF/Modules/TeamLab/Application/Validation/TeamLabReachabilityCompiler.cs` | FromTo 仅 `Pair(from,to)`；Bidirectional 同时 `Pair(to,from)` |
| `src/GZCTF/Models/Internal/Configs.cs` | `TeamLabNetworkConfig`（L592-L617）：默认 `FabricLinkPool="169.254.0.0/16"`、`RuntimeNetworkBaseCidr="10.180.0.0/16"` |

### 1.2 已实际打开并读取的 Agent 端代码文件

| 文件 | 关键关注点 |
| --- | --- |
| `src/GZCTF.Agent/Controllers/TeamLabController.cs` | HTTP 入口 |
| `src/GZCTF.Agent/Services/TeamLabNetworkService.cs` | 1264 行核心服务；`ApplyInfrastructureAsync`（L100-L205）顺序：bridge→router→dnsmasq→fabric；`CleanupAsync`（L670-L772）`ownsSharedResources` 逻辑（L698-L713） |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabBridgeService.cs` | bridge + dnsmasq；dnsmasq 后台启动（L54）；readiness 检查 150×0.1s=15s（L59-L72） |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabRouterService.cs` | 破坏性重建 netns（先 kill pids + delete netns，再 add） |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabFirewallService.cs` | runtime chain `policy drop`（L172）+ `ct state established,related accept`（L174）；fabric chain `policy accept`（L225）但有 TLR chain 兜底；chain 名带 generation（L286-L289） |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabFabricService.cs` | `ApplyAsync`（L35-L93）：veth 对+路由→runtime firewall→fabric firewall→peer routes；host route 未指定 dev（L69）；`BuildDesiredAllowedIps`（L395-L425）要求 peer AllowedIPs 包含 gateway /32 |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabFabricRouteStore.cs` | 原子文件操作 |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabRuntimeGenerationStore.cs` | 原子 `File.Move` + flush to disk |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabContainerNetworkFinalizeService.cs` | 容器网络 finalize |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabNetworkPrimitives.cs` | 校验 + ShellQuote |
| `src/GZCTF.Agent/Services/TeamLab/TeamLabCommandExecutor.cs` | 命令执行器 |
| `src/GZCTF.Agent/Services/TeamLabCommandRunner.cs` | shell 命令运行器 |
| `src/GZCTF.Agent/Models/TeamLabModels.cs` | `AgentTeamLabConfig`（`FabricInterfaceName="gzctf-fabric"`，`FabricMtu=1420`）；`TeamLabFabricApplyRequest` |

### 1.3 已验证的不变量清单（规范第 5 节）

- 不变量 #1（创建顺序与幂等所有权）：已检查
- 不变量 #2（generation-based 资源所有权）：已检查（发现 P1 违反）
- 不变量 #3（未声明连接不可达）：已检查
- 不变量 #4（有向连接方向 + 返回流量）：已检查
- 不变量 #5（reset 后网络语义稳定）：已检查
- 不变量 #6（delayed cleanup 不破坏新 generation）：已检查（发现 P1 违反）
- 不变量 #7（Hub/Worker/UDP gateway 职责无隐式旁路）：已检查

---

## 2. Findings 汇总

| ID | 严重性 | 文件:行号 | 所属链路 | 不变量 |
| --- | --- | --- | --- | --- |
| F-4.3-01 | P1 | `src/GZCTF.Agent/Services/TeamLabNetworkService.cs:698-713` | 4.3 delayed cleanup | #2 / #6 |
| F-4.3-02 | P2 | `src/GZCTF.Agent/Services/TeamLab/TeamLabBridgeService.cs:54-72` | 4.3 dnsmasq 健康度 | — |
| F-4.3-03 | P3 | `src/GZCTF/Models/Internal/Configs.cs:606` | 4.3 Fabric link pool | — |
| F-4.3-04 | P3 | `src/GZCTF.Agent/Services/TeamLab/TeamLabFabricService.cs:69` | 4.3 host route | — |
| F-4.3-05 | P3 | `src/GZCTF.Agent/Services/TeamLab/TeamLabFabricService.cs:406-418` | 4.3 WireGuard AllowedIPs | — |

### F-4.3-01 [P1] CleanupAsync 在 active generation 文件丢失时可能误删新 generation 共享资源

- **严重性**：P1
- **文件与精确行号**：`src/GZCTF.Agent/Services/TeamLabNetworkService.cs` 第 698-713 行
- **所属链路**：4.3 delayed cleanup 不破坏新 generation
- **被破坏的不变量**：#2（generation-based 资源所有权）、#6（delayed cleanup 不破坏新 generation）

**触发条件**：

`CleanupAsync` 接收到一个针对旧 generation 的清理请求（例如 generation=5），但此时：

1. `generationStore.ReadAsync` 因文件损坏/丢失返回 `null`（`activeGeneration is null`），且
2. 旧 generation 的 desired state 文件 `ResolveDesiredStatePath(runtimeId, 5)` 仍然存在（清理前的正常状态），且
3. 当前实际 active generation 已经是新 generation（例如 generation=6）。

**实际影响**：

代码路径（L698-L701）：

```csharp
var ownsSharedResources = activeGeneration?.Generation == request.Generation ||
                          activeGeneration is null &&
                          (request.DryRun ||
                           File.Exists(ResolveDesiredStatePath(request.RuntimeId, request.Generation)));
```

当 `activeGeneration is null` 且 desired state 文件存在时，`ownsSharedResources` 被误判为 `true`，导致 L703-L713 执行共享资源删除：

- L706：kill dnsmasq pid（基于 generation 目录，影响有限）
- L710：`ip netns delete {name}` 删除 router namespace（**按 runtimeId 命名，跨 generation 共享**）
- L711：`ip link delete {name}` 删除 bridge / fabric 接口（**按 runtimeId 命名，跨 generation 共享**）

如果此时新 generation=6 已经在运行（其 ApplyInfrastructureAsync 已完成或正在使用这些共享资源），删除操作会中断新 generation 的网络数据平面。同时 L762-L763 的 `ClearIfActiveAsync` 在 `ownsSharedResources=true` 时也会被触发，可能清空新 generation 的 active generation 记录。

**根因**：

`ownsSharedResources` 的回退分支用"请求 generation 的 desired state 文件是否存在"作为"是否拥有共享资源"的判据，但这是一个必要非充分条件：旧 generation 的 desired state 文件在清理前正常存在，不能证明该 generation 仍是 active。正确的判据应当严格依赖 `generationStore` 的 active generation 记录。

**修复方向**：

1. 当 `activeGeneration is null` 时，不应回退到 desired state 文件存在性检查，而应拒绝清理并返回错误（fail-safe），让主站重新同步 generation 状态后再决策。
2. 或者：将共享资源（router namespace、bridge、fabric 接口）的命名也带上 generation，从根本上消除跨 generation 共享——但这会改变"多 shard 共享 Fabric"的设计语义，需要权衡。
3. 作为最小修复：在 `ownsSharedResources` 的回退分支增加对"没有更新 generation 的 desired state 文件存在"的检查（即 `Directory.EnumerateDirectories(runtimeDirectory)` 中不存在比 `request.Generation` 更大的 generation 目录）。

**验证方式**：

1. 单元测试：模拟 `generationStore.ReadAsync` 返回 `null` 且 desired state 文件存在的场景，断言 `ownsSharedResources=false` 或 CleanupAsync 返回失败。
2. 集成测试：先 apply generation=5，再 apply generation=6，然后强制删除 active generation 文件，最后对 generation=5 发起 cleanup，观察 generation=6 的数据平面是否被破坏。

---

### F-4.3-02 [P2] dnsmasq 后台启动后无持续健康监控，崩溃后依赖主站 reconciliation

- **严重性**：P2
- **文件与精确行号**：`src/GZCTF.Agent/Services/TeamLab/TeamLabBridgeService.cs` 第 54 行（后台启动）、第 59-72 行（一次性 readiness 检查）
- **所属链路**：4.3 dnsmasq 数据面稳定性

**触发条件**：

dnsmasq 在 `ApplyDhcpDnsAsync` 中通过 `&` 后台启动（L54），随后执行 `BuildDnsmasqReadinessCommand`（L59-L72）进行 150 次 ×0.1s = 15s 的一次性 readiness 探测。一旦 readiness 通过，Agent 不再监控 dnsmasq 进程存活。

**实际影响**：

如果 dnsmasq 在 readiness 通过后因配置错误、内存压力、信号等原因崩溃：

1. DHCP/DNS 服务对新加入的容器不可用，但 Agent 不会主动检测和重启。
2. `live state probe`（`TeamLabNetworkService.cs` L131 `ProbeInfrastructureFactsAsync`）只检查接口/路由/firewall chain 是否存在，不检查 dnsmasq 进程是否存活，因此 digest 快速路径可能错误地认为"live facts match desired state"而跳过重建。
3. 必须等主站 reconciliation 触发完整 `ApplyInfrastructureAsync` 且 desired state digest 变化时才会重建 dnsmasq——但若 desired state 未变化，主站走快速路径（L130-L144）也不会重建。

**根因**：

dnsmasq 用 `&` 后台启动而非 systemd/ supervisors 托管，且没有周期性健康探针。`BuildInfrastructureFactProbeCommand` 未将 dnsmasq 进程存活纳入 live state 探测。

**修复方向**：

1. 在 `BuildInfrastructureFactProbeCommand`（`TeamLabNetworkService.cs` L398-L501）中加入对 dnsmasq pid 文件存在性 + `kill -0 $pid` 的检查，使 dnsmasq 崩溃后 live state probe 返回失败，从而触发重建。
2. 或者：将 dnsmasq 改为 systemd-run 临时服务托管，让 init 系统负责重启。
3. 最小修复：在 `ApplyDhcpDnsAsync` 中，若 pid 文件存在且进程存活，跳过重启；否则启动——这已经是部分实现（L52），但需要确保后续 probe 能发现进程死亡。

**验证方式**：

1. 集成测试：apply infrastructure 后，手动 `kill -9` dnsmasq 进程，再次 apply 同一 desired state，断言 dnsmasq 被重启。
2. 验证 `ProbeInfrastructureFactsAsync` 在 dnsmasq 死亡时返回 `Success=false`。

---

### F-4.3-03 [P3] FabricLinkPool 默认值 169.254.0.0/16 与 APIPA 冲突

- **严重性**：P3
- **文件与精确行号**：`src/GZCTF/Models/Internal/Configs.cs` 第 606 行
- **所属链路**：4.3 Fabric link pool 分配

**触发条件**：

`TeamLabNetworkConfig.FabricLinkPool` 默认值为 `"169.254.0.0/16"`。RFC 3927 将 169.254.0.0/16 定义为 APIPA（Automatic Private IP Addressing）链路本地地址，普通主机在 DHCP 失败时会自动从这个段分配地址。

**实际影响**：

1. 当 WorkerNode 主机的某个物理接口 DHCP 失败时，操作系统可能自动分配 169.254.x.x 的 APIPA 地址，与 Fabric /30 link pool 分配的地址冲突。
2. 内核对 169.254.0.0/16 的路由处理有特殊语义（link-local），可能导致 Fabric 路由被错误地限制在单链路范围内。
3. 排查时容易与 APIPA 地址混淆，增加运维负担。

**根因**：

默认值选择未考虑 RFC 3927 的 APIPA 语义保留。

**修复方向**：

1. 将默认值改为非保留段，例如 `100.64.0.0/10`（RFC 6598 CGNAT）或 `240.0.0.0/4`（Class E 保留，目前未分配）的一个子集。
2. 或在文档中明确要求部署时必须配置非默认值，并将默认值留空 + 启动时校验非空。
3. 修复时需同步更新 `TeamLabFabricLinkAllocator` 的容量计算（/16 → /30 容量为 16382 个 link）。

**验证方式**：

1. 部署文档审查：确认生产部署使用了非默认值。
2. 单元测试：`TeamLabFabricLinkAllocator` 在新默认值下能正确分配 /30 link。

---

### F-4.3-04 [P3] Fabric host route 添加未指定 dev，依赖内核路由查找

- **严重性**：P3
- **文件与精确行号**：`src/GZCTF.Agent/Services/TeamLab/TeamLabFabricService.cs` 第 69 行
- **所属链路**：4.3 Fabric host route

**触发条件**：

`ApplyAsync` 在添加 host 路由时（L69）：

```csharp
$"ip route replace {route.TargetCidr} via {route.GatewayIp}"
```

没有指定 `dev {hostInterface}`。对比同函数内其他路由添加：

- L65（localRoutes）：`ip route replace {route.TargetCidr} via {route.GatewayIp} dev {hostInterface}` — 指定了 dev
- L67（remoteRoutes in netns）：`ip netns exec {namespaceName} ip route replace {route.TargetCidr} via {hostAddress} dev {namespaceInterface}` — 指定了 dev

**实际影响**：

1. 当 host 存在多条可达 `route.GatewayIp` 的路径（例如同时有 Fabric 接口和其他管理接口可达同一 gateway），内核可能选择错误的 dev，导致流量不走 Fabric。
2. 在 `gzctf-fabric` 接口 down 或未就绪时，`ip route replace` 可能失败或选择错误 dev，但错误信息不明确。
3. 一致性：同函数内其他路由都指定 dev，唯独这条不指定，增加排查难度。

**根因**：

可能是遗漏，假设 gateway 总是直连在 Fabric 接口上。

**修复方向**：

将 L69 改为：

```csharp
$"ip route replace {route.TargetCidr} via {route.GatewayIp} dev {hostInterface}"
```

与 L65 保持一致。需确认 `hostInterface` 在该作用域可见（从 L51-L52 的 veth 创建逻辑看，`hostInterface` 在作用域内）。

**验证方式**：

1. 集成测试：在 host 上添加一条与 Fabric gateway 冲突的管理路由，apply infrastructure 后用 `ip route get {remoteCidr}` 确认走 `dev gzctf-fabric`。
2. DryRun 模式下检查生成的命令字符串包含 `dev {hostInterface}`。

---

### F-4.3-05 [P3] BuildDesiredAllowedIps 要求 WireGuard peer 初始 AllowedIPs 包含所有 gateway /32

- **严重性**：P3
- **文件与精确行号**：`src/GZCTF.Agent/Services/TeamLab/TeamLabFabricService.cs` 第 406-418 行
- **所属链路**：4.3 WireGuard Fabric peer 路由

**触发条件**：

`BuildDesiredAllowedIps`（L395-L425）在为每条 remote route 选择拥有该 gateway 的 peer 时（L411-L413）：

```csharp
var owners = peers
    .Where(peer => peer.AllowedIps.Contains(gatewayCidr, StringComparer.Ordinal))
    .ToArray();
if (owners.Length != 1)
    return (false, null, owners.Length == 0
        ? $"No WireGuard Fabric peer owns gateway {gatewayCidr}."
        : $"Multiple WireGuard Fabric peers own gateway {gatewayCidr}.");
```

要求 `peer.AllowedIps`（peer 的**当前** AllowedIPs）必须包含 `route.GatewayIp/32`。如果 peer 的 AllowedIPs 尚未初始化为包含所有可能的 gateway /32，此函数会返回错误。

**实际影响**：

1. 初始化顺序敏感：peers 的 AllowedIPs 必须先于 routes 声明被配置为包含所有 gateway /32，否则首次 apply 失败。
2. 当新增一个 shard（新 gateway）时，必须先更新所有现有 peer 的 AllowedIPs 以包含新 gateway /32，再声明到新 shard 的 route——否则 `BuildDesiredAllowedIps` 报错 "No WireGuard Fabric peer owns gateway"。
3. 排查困难：错误消息只说 "No peer owns gateway X.X.X.X"，不提示需要先更新哪些 peer 的 AllowedIPs。

**根因**：

`BuildDesiredAllowedIps` 用 peer 的**当前** AllowedIPs 作为 gateway 归属判据，而不是用一个独立的"peer → gateway"映射。这导致 gateway 归属判据依赖于 AllowedIPs 的初始化顺序。

**修复方向**：

1. 在 `TeamLabFabricApplyRequest` 中为每个 peer 显式声明其 gateway IP（而不是从 AllowedIPs 推断），用这个声明作为 gateway 归属判据。
2. 或者：放宽校验，当 `owners.Length == 0` 时，自动将 gateway /32 加入"当前 AllowedIPs 包含 gateway 所在子网"的 peer（但需要明确子网归属语义）。
3. 最小修复：在错误消息中列出所有已知 peer 的 AllowedIPs，辅助排查。

**验证方式**：

1. 单元测试：peer 的 AllowedIPs 不包含 gateway /32 时，断言返回的错误消息。
2. 集成测试：模拟新增 shard 场景，先声明 route 再更新 peer AllowedIPs，观察是否报错；按正确顺序（先 peer 后 route）操作应成功。

---

## 3. 适配性反模式检查（规范第 7 节）

| 反模式 ID | 反模式名称 | 检查结果 | 证据 |
| --- | --- | --- | --- |
| A-01 | 主站硬编码 Agent 端实现细节（如 shell 命令拼接） | ✅ 通过 | 主站 `TeamLabRouteApplicationService` 只生成 intent（`TeamLabNodeRouteIntent`、`TeamLabForwardPolicyRequest`），shell 命令拼接在 Agent 端 `TeamLabFabricService` / `TeamLabFirewallService` 完成 |
| A-02 | Agent 端持有跨 generation 的全局可变状态而不通过 generationStore | ✅ 通过 | `TeamLabRuntimeGenerationStore` 使用原子 `File.Move` + flush，所有跨 generation 状态经其协调；`TeamLabFabricRouteStore` 也用原子文件操作 |
| A-03 | 共享资源命名不带 generation 导致跨 generation 冲突 | ⚠️ 设计内但需关注 | `TeamLabResourceNameFactory.FabricHostInterface(runtimeId)` 不带 generation（设计如此，支持多 shard 共享 Fabric）；但 `TeamLabFirewallService` chain 名带 generation（L286-L289），`TeamLabFabricRouteStore` 按 RuntimeId+Generation 存储。共享命名是设计选择，非反模式，但与 F-4.3-01 叠加放大风险 |
| A-04 | 用文件存在性代替权威状态判据 | ❌ 违反（见 F-4.3-01） | `TeamLabNetworkService.cs` L698-L701 用 desired state 文件存在性作为 `ownsSharedResources` 的回退判据，违反"generationStore 是 active generation 的唯一权威"原则 |
| A-05 | 破坏性重建（delete+recreate）替代幂等 update 导致数据面中断 | ⚠️ 部分违反 | `TeamLabRouterService`（L14-L15）对 netns 用 delete+recreate；`TeamLabFabricService`（L51-L52）对 veth 对用 delete+recreate。在 reset 场景下这是设计意图，但在普通 apply 场景会中断数据面。建议对非 reset 场景改用 `ip link set` 幂等更新 |

---

## 4. 已检查但确认不是问题的高风险点

### 4.1 Firewall chain 命名带 generation，reset 后不会误删新 generation 链

- **检查点**：`TeamLabFirewallService.cs` L286-L289
- **结论**：✅ 不是问题。chain 名 `TLR{runtimeId:X}G{generation:X}`、`TLA{...}`、`TLM{...}`、`TLF{...}` 都带 generation，`RemoveRuntimePoliciesAsync` / `RemoveFabricPoliciesAsync` 按 runtimeId+generation 精确删除，不会误删新 generation 的链。

### 4.2 ReplaceRuntimeDeclaration 按 RuntimeId 替换（非 RuntimeId+Generation）

- **检查点**：`TeamLabFabricService.cs` L357-L372
- **结论**：✅ 不是问题（在 F-4.3-01 不触发的前提下）。`ReplaceRuntimeDeclaration` 按 RuntimeId 替换整个 runtime 的 declaration，看似跨 generation，但实际上：
  1. `ApplyAsync` 在 `ApplyInfrastructureAsync` L108 持有 `runtimeLock`，串行化同 runtime 的所有 generation 操作。
  2. `ApplyInfrastructureAsync` L118-L121 拒绝旧 generation 请求（`activeGeneration?.Generation > request.Generation` 时返回失败）。
  3. 因此同 runtime 不会有并发的新旧 generation Apply。
  4. delayed cleanup（`CleanupAsync`）也持有同一 `runtimeLock`，不会与 Apply 并发。
  - 风险仅在于 F-4.3-01 的 `ownsSharedResources` 误判场景，已单列。

### 4.3 ManagedCidrs 不会破坏新 generation

- **检查点**：`TeamLabFabricService.cs` L401 `managedCidrs = state.ManagedCidrs.ToHashSet`
- **结论**：✅ 不是问题。`BuildDesiredAllowedIps` 用 `managedCidrs` 从 peer 的 AllowedIPs 中**排除**本 runtime 管理的 cidr（L404），避免本 runtime 的子网被通告给其他 peer。`state` 来自 `TeamLabFabricRouteStore` 的当前 generation 快照，按 RuntimeId+Generation 加载，不会跨 generation 污染。

### 4.4 Fabric chain policy accept 有 TLR chain 兜底

- **检查点**：`TeamLabFirewallService.cs` L225（fabric chain `policy accept`）、L172（runtime chain `policy drop`）
- **结论**：✅ 不是问题。Fabric chain 在 priority -50（更早），但只负责 MSS clamping和显式 accept；runtime chain 在 priority 0（更晚），`policy drop` + 显式 forward policies 是真正的访问控制门。未声明的跨网段流量会被 runtime chain drop，不会因 fabric chain policy accept 而旁路。`ct state established,related accept`（L174）只放行返回流量，符合有向连接语义。

### 4.5 Live state probe 不会因接口名不带 generation 而误判

- **检查点**：`TeamLabNetworkService.cs` L398-L501 `BuildInfrastructureFactProbeCommand`、`TeamLabResourceNameFactory.FabricHostInterface(runtimeId)`
- **结论**：✅ 不是问题。probe 命令检查接口/路由/firewall chain 是否存在，接口名按 runtimeId 命名（不带 generation）是设计意图（共享资源）。probe 只判断"是否存在"，不判断"是否属于当前 generation"，因此不会因接口名不带 generation 而误判为"不存在"。firewall chain 名带 generation，probe 按 generation 精确检查。

### 4.6 多 shard 共享 Fabric 语义（同 Worker 同 runtime 只有一个 shard）

- **检查点**：`TeamLabRouteApplicationService.cs` L23-L24（`shards.Where(item => item.Generation == runtime.Generation)`）、`TeamLabResourceNameFactory.FabricHostInterface(runtimeId)`
- **结论**：✅ 不是问题。Fabric 接口名按 runtimeId 命名，看似允许多 shard 共享。但 `TeamLabRouteApplicationService.ApplyAsync` 对同 runtime 的所有 shards 并行调用 `ApplyShardInfrastructureAsync`（L59-L62），每个 shard 发到不同的 WorkerNode（`shard.WorkerNodeId`）。同一 WorkerNode 上同一 runtime 只有一个 shard（由调度保证），因此 Fabric 接口实际不会跨 shard 共享——命名按 runtimeId 是为了简洁，实际语义等价于按 (runtimeId, workerNodeId)。

### 4.7 有向连接方向 + 返回流量

- **检查点**：`TeamLabReachabilityCompiler.cs`（FromTo 单向 Pair）、`TeamLabRouteApplicationService.BuildForwardPolicies`（L267-L278）、`TeamLabFirewallService.cs` L174（`ct state established,related accept`）
- **结论**：✅ 不是问题。FromTo 只生成 `Pair(from, to)`，BuildForwardPolicies 据此生成单向 forward policy（源→目的 accept）。反向流量（目的→源）不被显式 allow，但被 `ct state established,related accept` 放行（因为是 established 连接的返回包）。符合有向连接语义。Bidirectional 同时生成 `Pair(from,to)` 和 `Pair(to,from)`，双向都 accept。

### 4.8 原子文件操作 + flush to disk

- **检查点**：`TeamLabRuntimeGenerationStore.cs`、`TeamLabFabricRouteStore.cs`
- **结论**：✅ 不是问题。两处都用 `File.Move`（原子 rename）+ `fsync`（flush to disk），符合规范第 5 节"原子文件操作"要求。

---

## 5. 链路覆盖结论（规范第 4.3 节 7 个关键点）

| 关键点 | 结论 | 说明 |
| --- | --- | --- |
| 4.3-a 创建顺序与幂等所有权 | ✅ 通过 | bridge→router→dnsmasq→fabric 顺序（L100-L205），持有 `runtimeLock` 串行化；bridge 幂等（`ip link show \|\| ip link add`），router/fabric 破坏性重建（设计意图，受 lock 保护） |
| 4.3-b 多 shard 共享 Fabric 语义 | ✅ 通过 | Fabric 接口按 runtimeId 命名，同 Worker 同 runtime 只有一个 shard（调度保证），实际不跨 shard 共享 |
| 4.3-c 未声明连接不可达 | ✅ 通过 | runtime chain `policy drop`（L172）+ 显式 forward policies，fail-closed；未声明的源-目的对被 drop |
| 4.3-d 有向连接方向 + 返回流量 | ✅ 通过 | FromTo 单向 Pair，BuildForwardPolicies 单向 accept；返回流量靠 `ct state established,related accept`（L174） |
| 4.3-e reset 后网络语义稳定 | ✅ 通过 | firewall chain 名带 generation（L286-L289），reset 不会误删新 generation 链；router namespace/bridge 不带 generation 但 reset 是中断性操作（设计意图） |
| 4.3-f delayed cleanup 不破坏新 generation | ❌ 不通过（F-4.3-01） | `ownsSharedResources` 在 active generation 文件丢失时回退到 desired state 文件存在性检查，可能误删新 generation 共享资源 |
| 4.3-g Hub/Worker/UDP gateway 职责无隐式旁路 | ✅ 通过 | Fabric chain policy accept（L225）有 TLR chain `policy drop`（L172）兜底；公网 UDP gateway 只做端口映射，不参与东西向路由；WireGuard Fabric 只承载 WorkerNode 间路由，玩家只获得一个 WireGuard 配置（直达入口网段） |

---

## 6. 总结

### 6.1 Findings 统计

- 总数：5
- P1：1（F-4.3-01）
- P2：1（F-4.3-02）
- P3：3（F-4.3-03、F-4.3-04、F-4.3-05）

### 6.2 适配性反模式检查结果

- A-01 主站硬编码 Agent 实现：✅ 通过
- A-02 跨 generation 全局可变状态：✅ 通过
- A-03 共享资源命名不带 generation：⚠️ 设计内（与 F-4.3-01 叠加放大风险）
- A-04 文件存在性代替权威状态：❌ 违反（F-4.3-01）
- A-05 破坏性重建替代幂等 update：⚠️ 部分违反（reset 场景设计意图，普通 apply 场景中断数据面）

### 6.3 链路覆盖结论

- 7 个关键点中 6 个通过，1 个不通过（4.3-f delayed cleanup，因 F-4.3-01）。
- 关键风险：F-4.3-01 在 active generation 文件丢失的边缘场景下可能破坏新 generation 数据平面，建议优先修复。
- 其余 finding（P2/P3）均为健壮性/可运维性改进，不阻塞链路 4.3 的功能正确性。
