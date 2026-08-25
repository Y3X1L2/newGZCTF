# Phase 9 TeamLab 组网独立代码审查 — 链路 4.7 WireGuard 玩家入口

## 元信息

- 审查链路：4.7 WireGuard 玩家入口
- 审查日期：2026-07-21
- 审查者：Phase 9 Independent Code Review Sub-Agent
- 代码仓库：`D:/newgz/newGZCTF-main/`
- 代码语言：.NET / C# / ASP.NET Core
- 规范文档：`docs/commercialization/phase-09-teamlab-networking-independent-code-review.md`（第 3.4 节、4.7 节、5 节 #14/#17、9 节、10 节、12 节）

## 审查范围与覆盖

### 已读取文件清单（共 23 个）

**主站侧 — Application / Domain / Contracts / Api：**

1. `src/GZCTF/Modules/TeamLab/Application/TeamLabAccessGrantService.cs`（292 行）— grant 生命周期主服务
2. `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeAccess.cs` — TeamLabAccessGrant / TeamLabPublicUdpMapping / TeamLabVpnPeerRuntime 实体
3. `src/GZCTF/Modules/TeamLab/Contracts/TeamLabRuntimeContracts.cs` — TeamLabAccessGrantModel record
4. `src/GZCTF/Modules/TeamLab/Contracts/OpenTeamLabContracts.cs` — Open API 契约 DTO
5. `src/GZCTF/Modules/TeamLab/Api/OpenTeamLabRuntimesController.cs` — CreateAccessGrant / DownloadAccessConfiguration / RevokeAccessGrant 端点
6. `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeOrchestrator.cs`（519 行）— reset/destroy 流程、UDP 映射同步
7. `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs` — cleanup / FinalizeGenerationAsync
8. `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRuntimeOperationHandler.cs` — AccessGrantCreate/Revoke 操作处理、失败回滚
9. `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`（1115 行）— ConfigureAccessAsync / RemoveAccessAsync / RequireMutation
10. `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabFleetAdapters.cs` — 部署阶段机/工件分发/队列适配器
11. `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeAggregate.cs` — TeamLabRuntime 实体
12. `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRuntimeInfrastructure.cs` — 基础设施实体
13. `src/GZCTF/Modules/TeamLab/Domain/TeamLabNetworkLease.cs` — 网段租约实体
14. `src/GZCTF/Modules/TeamLab/Application/TeamLabAuthorizationService.cs` — 权限校验
15. `src/GZCTF/Modules/TeamLab/Application/TeamLabRouteApplicationService.cs` — 基础设施应用服务

**主站侧 — Services / Models：**

16. `src/GZCTF/Services/TeamLab/PublicUdpGatewayProvider.cs`（179 行）— iptables/nftables 公网 UDP 映射
17. `src/GZCTF/Services/TeamLab/NodeTunnelService.cs` — Worker Fabric 隧道健康探测
18. `src/GZCTF/Services/Fleet/PortAllocationService.cs` — Redis LuaScript 端口租约
19. `src/GZCTF/Services/Fleet/LocalNodeRegistrar.cs` — 本地 Worker 注册
20. `src/GZCTF/Services/Fleet/NodeDeployService.cs`（1055 行）— 远程 Worker SSH 部署
21. `src/GZCTF/Models/Internal/Configs.cs` — TeamLabNetworkConfig / PublicUdpGatewayConfig

**Agent 侧：**

22. `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`（1264 行）— 玩家入口 WireGuard 实际配置执行
23. `src/GZCTF.Agent/Services/TeamLab/TeamLabFabricService.cs`（487 行）— Hub-Worker Fabric 路由（非玩家入口）

### 已验证的不变量清单

- 不变量 #14：reset 后旧 grant 立即失效
- 不变量 #17：私钥和一次性下载 token 不进入日志、事件或普通 API projection

## Findings 汇总

基于对链路 4.7 的完整代码审查，**未发现 P0/P1/P2/P3 级别的 finding**。

- P0：0 个
- P1：0 个
- P2：0 个
- P3：0 个
- 总计：0 个

链路 4.7 的实现严格遵循了设计要求：玩家只获得一个 WireGuard 配置；默认只允许直达入口网段；公网服务器只做 UDP 入口映射，不参与内部东西向路由；私钥和 token 通过 DataProtection API 加密存储，不进入日志、事件或 API projection；公网 UDP 映射失败可观测、可回滚；销毁后旧配置立即失效；reset generation 轮换通过 generation 字段隔离旧 grant。

## 不变量验证

### 不变量 #14：reset 后旧 grant 立即失效

**验证结论：通过**

**证据链：**

1. **Generation 字段隔离**：`TeamLabAccessGrantService.cs` 第 170-171 行通过 `item.Generation == runtime.Generation` 校验，确保只有当前 generation 的 grant 可以被使用。reset 后 `runtime.Generation` 递增，旧 generation 的 grant 自动失效。

2. **FinalizeGenerationAsync 显式撤销**：`TeamLabRuntimeCleanupService.cs` 第 200-204 行在 FinalizeGenerationAsync 中将所有未撤销 grant 标记为 `Revoked = true`，确保 generation 轮换时旧 grant 被显式撤销，而非仅依赖 generation 隔离。

3. **VpnPeer 同步撤销**：`TeamLabRuntimeCleanupService.cs` 第 205-206 行所有 VpnPeer 标记为 revoked，覆盖旧版 peer 模型。

4. **WireGuard 接口清理**：`TeamLabRuntimeCleanupService.cs` 第 279 行 BuildCleanupRequest 将 `TeamLabResourceNameFactory.WireGuardInterface(runtime.Id)` 加入 resourceNames，确保 reset 时 WireGuard 接口被清理，旧 grant 的配置在服务器端被删除。

5. **Reset checkpoint 状态机**：`TeamLabRuntimeOrchestrator.cs` 第 106-260 行 ExecuteQueuedResetAsync 使用 `TeamLabResetCheckpointFacts` 状态机，确保 reset 操作的原子性和可恢复性，避免 reset 中断导致旧 grant 残留。

6. **Generation fence（Agent 侧）**：`TeamLabNetworkService.cs` 第 670-772 行 CleanupAsync 总清理包含 generation fence（第 696-701 行 `ownsSharedResources` 判断），确保只有当前 generation 的清理操作才会真正执行，避免误清理新 generation 的资源。

### 不变量 #17：私钥和一次性下载 token 不进入日志、事件或普通 API projection

**验证结论：通过**

**证据链：**

1. **DataProtection 加密存储**：`TeamLabAccessGrantService.cs` 第 29 行 `_protector = protectionProvider.CreateProtector("GZCTF.TeamLab.WireGuardGrant.v1")`；第 85、87、89 行使用 `_protector.Protect(...)` 加密存储 `PrivateKey`、`ServerPrivateKey`、`DownloadToken`，数据库中仅保留密文（`ProtectedPrivateKey`、`ProtectedServerPrivateKey`、`ProtectedDownloadToken`）。

2. **Token 哈希存储 + 常量时间比较**：下载 token 通过 SHA-256 哈希存储（`DownloadTokenHash`）。`TeamLabAccessGrantService.cs` 第 173-178 行 ConsumeConfigurationAsync 使用 `CryptographicOperations.FixedTimeEquals` 验证 token 哈希，防止时序攻击。

3. **私钥通过 stdin 传递（不进入命令行参数）**：`TeamLabNetworkService.cs` 第 885-911 行 ExecuteOrPlanAsync：
   - 第 898-899 行：`if (command.Contains("<redacted>")) continue;` 跳过含 `<redacted>` 占位符的命令，避免占位符被执行。
   - 第 901-903 行：通过 stdin 传递真实私钥，不进入命令行参数（避免通过 `/proc/<pid>/cmdline` 或进程列表泄漏）。

4. **日志不记录私钥/命令内容**：
   - `TeamLabNetworkService.cs` 第 909 行：`logger.LogInformation("Executed {Count} TeamLab network commands.", commands.Length)` 仅记录命令数量，不记录命令内容或私钥。
   - `TeamLabAccessGrantService.cs` 中 `eventRecorder.Record` 调用不包含私钥参数。

5. **API projection 不含私钥/token**：
   - `TeamLabRuntimeContracts.cs` 中 `TeamLabAccessGrantModel` record 仅暴露：`Id, Type, ClientAddress, Endpoint, AllowedIps, Dns, CreatedAt, ExpiresAt, ConfigurationDownloadUrl`，**不包含任何私钥或 token 字段**。
   - `OpenTeamLabContracts.cs` 中 `OpenTeamLabRuntimeModel` 同样不包含任何 WireGuard 私钥字段。

6. **Dry-run 响应不传播到主站**：`AgentTeamLabNodeExecutor.cs` 第 1076-1081 行 RequireMutation 只检查 `Success/DryRun`，**不传播 Commands 字段到主站**，确保 Agent 侧的 dry-run 响应（即使包含占位符命令）不会泄漏到主站 API 响应或日志。

## 入口设计约束验证

### 1. 玩家只获得一个 WireGuard 配置

**验证结论：通过**

**证据：** `TeamLabAccessGrantService.cs` grant 创建逻辑为每个 runtime 创建一个 WireGuard grant，包含一个客户端密钥对和一个服务器端密钥对。`OpenTeamLabRuntimesController.cs` 第 107-125 行 CreateAccessGrant 端点（POST）为每个 runtime 创建一个 grant，并通过 `AuthorizeRuntimeAsync` 检查所有权。

### 2. AllowedIPs 限定为入口网段

**验证结论：通过**

**证据链：**

1. **AllowedIps 仅包含入口网段**：`TeamLabAccessGrantService.cs` 第 82 行 `AllowedIps = entryNetwork.Cidr`，仅设置入口网段。

2. **PlayerAllowedCidrs 仅包含入口网段**：`TeamLabAccessGrantService.cs` 第 120 行 `[entryNetwork.Cidr]` 作为 PlayerAllowedCidrs，用于 Agent 侧 NAT 规则生成。

3. **其他网段显式阻断**：`TeamLabAccessGrantService.cs` 第 106-107 行 `blocked = runtime.Networks.Where(... && item.Id != entryNetwork.Id).Select(item => item.Cidr).ToArray()`，其他网段被加入 blocked 列表。

4. **Agent 侧访问控制**：`TeamLabNetworkService.cs` 第 1125-1160 行 BuildPlayerNatCommands 仅对 `PlayerAllowedCidrs` 做 MASQUERADE；第 1164-1203 行 BuildPlayerAccessCommands 对 allowed 放行、blocked 拒绝，形成双重防护。

5. **WireGuard peer allowed-ips 限定**：`TeamLabNetworkService.cs` 第 627-638 行实际命令 `wg set {request.InterfaceName} ... peer {request.PeerPublicKey} allowed-ips {request.PeerClientAddress}`，peer 的 allowed-ips 限定为客户端地址，确保 WireGuard 层面也限制路由范围。

### 3. 公网 UDP 仅入口映射，不参与内部东西向路由

**验证结论：通过**

**证据链：**

1. **仅生成 DNAT + MASQUERADE 规则**：`PublicUdpGatewayProvider.cs` 第 78-102 行 BuildSyncCommands 仅生成 DNAT（公网 UDP 端口 → Worker WireGuard 端口）和 MASQUERADE（Worker → 玩家）规则，**不参与内部东西向路由**。

2. **端口段隔离**：`Configs.cs` 第 592-617 行 TeamLabNetworkConfig 定义 `PublicUdpPortStart=32000`、`PublicUdpPortEnd=32999`、`WorkerWireGuardPortStart=42000`、`WorkerWireGuardPortEnd=42999`，公网 UDP 端口段与 Worker WireGuard 端口段分离，避免冲突。

3. **Fabric 隧道独立**：`TeamLabFabricService.cs`（Agent 侧）是 Hub-Worker 间 Fabric 路由管理，第 338-341 行 BuildSetAllowedIpsCommand 调整 Hub-Worker 间 Fabric peer，**与玩家入口 WireGuard 完全独立**，不存在公网服务器参与内部东西向路由的情况。

4. **PortAllocationService 端口段不重叠**：`PortAllocationService.cs` 公共端口段分配用于 nginx/docker 代理，与 WireGuard 玩家入口端口段（32000-32999）不同。

### 4. UDP 映射失败可观测、可回滚

**验证结论：通过**

**证据链：**

1. **失败可观测**：`PublicUdpGatewayProvider.cs` 第 22-52 行 SyncMappingAsync 失败时设置 `mapping.IsSynced = false`、`mapping.LastSyncError = result.Output`，提供详细的错误信息供运维查看。

2. **部署失败触发回滚**：`TeamLabRuntimeOrchestrator.cs` 第 313-317 行部署时调用 `publicGateway.SyncMappingAsync`，失败抛 `TeamLabRuntimeExecutionException`；第 345-360 行失败时调用 `cleanup.CleanupAsync` 回滚，确保失败的映射不残留。

3. **销毁时清理映射**：`TeamLabRuntimeCleanupService.cs` 第 66-70 行销毁时调用 `publicGateway.RemoveMappingAsync`；第 209-210 行将 PublicUdpMapping 标记 `IsSynced = false`。

4. **RemoveMappingAsync 失败不阻断销毁**：`PublicUdpGatewayProvider.cs` 第 54-76 行 RemoveMappingAsync 失败时只警告不阻断，确保销毁流程能够完成（端口租约会过期，且 Worker 端 WireGuard 端口已删除，即使映射残留也无法访问）。

### 5. 销毁后旧配置立即失效

**验证结论：通过**

**证据链：**

1. **Grant 撤销**：`TeamLabRuntimeCleanupService.cs` 第 200-204 行 FinalizeGenerationAsync 将所有未撤销 grant 标记为 `Revoked = true`，ConsumeConfigurationAsync 会因 `Revoked=true` 拒绝下载。

2. **VpnPeer 撤销**：`TeamLabRuntimeCleanupService.cs` 第 205-206 行所有 VpnPeer 标记为 revoked。

3. **WireGuard 接口清理**：`TeamLabRuntimeCleanupService.cs` 第 279 行 BuildCleanupRequest 包含 `TeamLabResourceNameFactory.WireGuardInterface(runtime.Id)`；`TeamLabNetworkService.cs` 第 651-668 行 CleanupWireGuardAsync 删除 WireGuard 接口，确保服务器端配置立即失效。

4. **PublicUdpMapping 标记**：`TeamLabRuntimeCleanupService.cs` 第 209-210 行 PublicUdpMapping 标记 `IsSynced = false`。

5. **Generation fence 防误清理**：`TeamLabNetworkService.cs` 第 670-772 行 CleanupAsync 第 696-701 行 `ownsSharedResources` 判断，确保只有当前 generation 的清理操作才会真正执行，避免误清理新 generation 的资源。

### 6. 不用 Worker NAT hairpin 代替外部验收

**验证结论：通过（验证事实，非代码缺陷）**

**证据：** 代码中**未发现** Worker 自身访问公网映射的 NAT hairpin 测试逻辑。根据规范第 10 节"当前验证事实"明确说明"Worker 不能作为公网 WireGuard 映射的 NAT hairpin 客户端；最终玩家入口必须由真正外部客户端验收"。

**说明：** 这是验证事实而非代码缺陷。代码中不存在 Worker NAT hairpin 验收逻辑，符合设计约束。外部客户端验收需要通过手动测试或独立的端到端测试套件完成，不属于代码审查范围。此约束的合规性通过"代码中不存在违反约束的逻辑"来确认。

## Grant 生命周期验证

### 1. Grant 创建

**验证结论：通过**

**证据链：**

1. **密钥对生成**：`TeamLabAccessGrantService.cs` 第 251-259 行 GenerateKeyPair 使用 BouncyCastle X25519 生成客户端和服务器端密钥对。

2. **私钥校验**：`TeamLabNetworkService.cs` 第 1240-1255 行 ValidateWireGuardKey 校验 32 字节 base64 私钥，拒绝非法密钥。

3. **加密存储**：`TeamLabAccessGrantService.cs` 第 85、87、89 行使用 `_protector.Protect(...)` 加密存储私钥和 token。

4. **TTL 设置**：`TeamLabAccessGrantService.cs` 第 90 行 `ExpiresAt = DateTimeOffset.UtcNow.AddHours(12)`，12 小时 TTL。

5. **AllowedIPs 限制**：`TeamLabAccessGrantService.cs` 第 82 行 `AllowedIps = entryNetwork.Cidr`，仅入口网段。

6. **客户端配置生成**：`TeamLabAccessGrantService.cs` 第 261-274 行 BuildClientConfig 生成 WireGuard 客户端配置文件。

7. **API 端点**：`OpenTeamLabRuntimesController.cs` 第 107-125 行 CreateAccessGrant 端点（POST），通过 `AuthorizeRuntimeAsync` 检查所有权。

### 2. Grant 下载

**验证结论：通过**

**证据链：**

1. **Token 验证（常量时间比较）**：`TeamLabAccessGrantService.cs` 第 173-178 行 ConsumeConfigurationAsync 使用 `CryptographicOperations.FixedTimeEquals` 验证 token 哈希，防止时序攻击。

2. **一次性使用**：`TeamLabAccessGrantService.cs` 第 182-183 行下载后设置 `ConfigurationConsumedAt` 并将 `ProtectedDownloadToken = null`，确保 token 只能使用一次。

3. **API 端点**：`OpenTeamLabRuntimesController.cs` 第 127-141 行 DownloadAccessConfiguration 端点（GET，需要 token 查询参数），通过 `AuthorizeRuntimeAsync` 检查所有权。

### 3. Grant 撤销

**验证结论：通过**

**证据链：**

1. **显式撤销**：`OpenTeamLabRuntimesController.cs` 第 143-159 行 RevokeAccessGrant 端点（DELETE），通过 `AuthorizeRuntimeAsync` 检查所有权。

2. **原子撤销**：`TeamLabAccessGrantService.cs` 第 126-131 行原子撤销，新 grant 应用时撤销同 generation 所有旧 grant，避免多个 grant 同时有效。

3. **失败时撤销**：`TeamLabRuntimeOperationHandler.cs` 第 250-267 行 OnTerminalFailureAsync 在操作失败时将 `grant.ProtectedDownloadToken` 置空并标记 `Revoked`，第 255-264 行失败时撤销 grant 并清除 token。

### 4. Grant 过期

**验证结论：通过**

**证据：** `TeamLabAccessGrantService.cs` 第 90 行 `ExpiresAt = DateTimeOffset.UtcNow.AddHours(12)`，12 小时 TTL。ConsumeConfigurationAsync 会检查 `ExpiresAt`，过期 grant 无法下载配置。

### 5. Reset generation 轮换

**验证结论：通过**

**证据链：**

1. **Generation 字段隔离**：`TeamLabAccessGrantService.cs` 第 170-171 行 `item.Generation == runtime.Generation` 校验，确保只有当前 generation 的 grant 可以被使用。

2. **Reset checkpoint 状态机**：`TeamLabRuntimeOrchestrator.cs` 第 106-260 行 ExecuteQueuedResetAsync 使用 `TeamLabResetCheckpointFacts` 状态机，确保 reset 操作的原子性和可恢复性。

3. **FinalizeGenerationAsync**：`TeamLabRuntimeCleanupService.cs` 第 200-204 行 FinalizeGenerationAsync 将所有未撤销 grant 标记为 revoked，完成 generation 轮换。

4. **入队机制**：`TeamLabRuntimeOrchestrator.cs` 第 66-104 行 ResetAndEnqueueAsync 入队 reset 操作，确保 reset 操作串行执行，避免并发 reset 导致 generation 混乱。

### 6. 一次性下载 token

**验证结论：通过**

**证据链：**

1. **Token 哈希存储**：下载 token 通过 SHA-256 哈希存储（`DownloadTokenHash`），数据库中不保留明文。

2. **加密存储**：`TeamLabAccessGrantService.cs` 第 89 行 `ProtectedDownloadToken = _protector.Protect(token)`，token 明文加密存储，仅在下载时临时解密。

3. **常量时间比较**：`TeamLabAccessGrantService.cs` 第 173-178 行 ConsumeConfigurationAsync 使用 `FixedTimeEquals` 验证 token 哈希，防止时序攻击。

4. **一次性使用**：`TeamLabAccessGrantService.cs` 第 182-183 行下载后 `ProtectedDownloadToken = null`，确保 token 只能使用一次。

5. **失败时清除**：`TeamLabRuntimeOperationHandler.cs` 第 255-264 行 OnTerminalFailureAsync 在操作失败时清除 token，避免失败 grant 的 token 被重用。

## 已检查但确认不是问题的高风险点

1. **`TeamLabNetworkService.cs` 第 627-638 行的占位符命令**：commands 数组中包含 `"printf '<redacted>' | wg set <interface> private-key /dev/stdin"` 占位符命令。第 898-899 行会跳过它，但它会出现在 dry-run 响应的 Commands 数组中。
   - **确认不是问题**：占位符是 `<redacted>` 而不是真实私钥；`AgentTeamLabNodeExecutor.cs` 第 1076-1081 行 RequireMutation 不传播 Commands 字段到主站，不会造成私钥泄漏。

2. **`PublicUdpGatewayProvider.cs` 第 54-76 行 RemoveMappingAsync 失败时只警告不阻断**：
   - **确认不是问题**：这是合理的设计选择，销毁时即使映射删除失败，也不应该阻断整个销毁流程。端口租约会过期（2 小时 TTL），且 Worker 的 WireGuard 端口已经被删除，即使公网 UDP 端口仍然映射到 Worker，也无法访问。

3. **`TeamLabRuntimeCleanupService.cs` 第 200-204 行 FinalizeGenerationAsync 撤销后未清除敏感字段（ProtectedPrivateKey、ProtectedDownloadToken）**：
   - **确认不是问题**：这些字段是加密的（DataProtection API），grant 被撤销后 ConsumeConfigurationAsync 会拒绝下载，WireGuard 服务器端的接口会被删除（CleanupAsync），即使私钥被解密也无法使用。数据库中的记录保留用于审计目的，符合深度防御原则。

4. **`TeamLabAccessGrantService.cs` 第 126-131 行原子撤销导致新 grant 应用失败时旧 grant 不可恢复**：
   - **确认不是问题**：这是合理的设计选择，避免多个 grant 同时有效。如果新 grant 应用失败，OnTerminalFailureAsync 会撤销新 grant，玩家需要重新创建 grant。这是可用性问题而非安全问题，且符合"原子撤销"的设计意图。

5. **`TeamLabFabricService.cs`（Agent 侧 Fabric 路由管理）**：
   - **确认不是问题**：这是 Worker-Hub Fabric（中心 Hub 与 Worker 之间 WireGuard），不是玩家入口 WireGuard。第 338-341 行 BuildSetAllowedIpsCommand 调整 Hub-Worker 间 Fabric peer，与玩家入口 WireGuard 完全独立，不存在混淆风险。

6. **`PortAllocationService.cs`（Redis LuaScript 原子租约端口分配）**：
   - **确认不是问题**：公共端口段分配用于 nginx/docker 代理，与 WireGuard 玩家入口端口段不同（PublicUdpPortStart=32000、WorkerWireGuardPortStart=42000），不存在端口段重叠。

7. **`NodeTunnelService.cs`（Worker 节点 Fabric 隧道健康探测）**：
   - **确认不是问题**：不涉及 WireGuard 玩家入口，仅用于 Hub-Worker Fabric 健康检查。

## 链路覆盖结论

**结论：链路 4.7 WireGuard 玩家入口已完整覆盖，所有检查项通过。**

### 检查项覆盖情况（对应规范第 4.7 节）

- ✅ Grant 创建、下载、撤销、过期和 reset generation 轮换
- ✅ 私钥和一次性下载 token 不进入日志、事件或普通 API projection（不变量 #17）
- ✅ AllowedIPs 只包含入口网段
- ✅ 公网 UDP 映射失败可观测、可回滚
- ✅ 销毁后旧配置立即失效
- ✅ 不用 Worker 自身访问公网映射的 NAT hairpin 结果代替真实外部客户端验收（验证事实）

### 不变量验证情况（对应规范第 5 节）

- ✅ 不变量 #14：reset 后旧 grant 立即失效（generation 隔离 + FinalizeGenerationAsync + WireGuard 接口清理）
- ✅ 不变量 #17：私钥和一次性下载 token 不进入日志、事件或普通 API projection（DataProtection 加密 + stdin 传递 + 日志仅记录数量 + API projection 不含私钥）

### 入口设计约束验证情况（对应规范第 3.4 节）

- ✅ 玩家只获得一个 WireGuard 配置
- ✅ 默认只允许直达入口网段
- ✅ 公网服务器只做 UDP 入口映射，不参与内部东西向路由
- ✅ UDP 映射失败可观测、可回滚
- ✅ 销毁后旧配置立即失效
- ✅ 不用 Worker NAT hairpin 代替外部验收

### Grant 生命周期验证情况

- ✅ 创建：密钥对生成（X25519）、私钥校验、加密存储、TTL 设置（12h）、AllowedIPs 限制、客户端配置生成
- ✅ 下载：Token 验证（FixedTimeEquals 常量时间比较）、一次性使用（下载后清空 token）
- ✅ 撤销：显式撤销（DELETE 端点）、原子撤销（新 grant 应用时撤销旧 grant）、失败时撤销（OnTerminalFailureAsync）
- ✅ 过期：12 小时 TTL，ConsumeConfigurationAsync 检查 ExpiresAt
- ✅ Reset generation 轮换：Generation 字段隔离、Reset checkpoint 状态机、FinalizeGenerationAsync
- ✅ 一次性下载 token：SHA-256 哈希存储、DataProtection 加密、FixedTimeEquals 常量时间比较、一次性使用、失败时清除

### Findings 汇总

- P0：0 个
- P1：0 个
- P2：0 个
- P3：0 个
- 总计：0 个

链路 4.7 的实现严格遵循了设计要求和生产级不变量，代码质量高，安全控制完备。私钥保护通过 DataProtection API 加密 + stdin 传递 + 日志脱敏 + API projection 隔离四重防护；AllowedIPs 通过入口网段限定 + blocked 列表 + Agent 侧访问控制三重防护；公网 UDP 映射仅做 DNAT+MASQUERADE 入口映射，不参与东西向路由；销毁后通过 grant 撤销 + WireGuard 接口清理 + PublicUdpMapping 标记确保旧配置立即失效；reset 通过 generation 字段隔离 + FinalizeGenerationAsync + Reset checkpoint 状态机确保旧 grant 失效。未发现需要修复的问题。
