# TeamLab 管理员远程运维 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 TeamLab 队伍资产提供受权限控制、短时有效且完整审计的网页容器终端、SSH 和 RDP 运维入口。

**Architecture:** 主平台建立运维会话并完成权限、凭据和审计控制；计算节点只创建绑定到指定运行环境、资产和端口的短时转发或容器终端；Guacamole 承载 SSH/RDP 网页会话。运维服务通过 TeamLab 应用接口查询资产位置，不修改场景编译、调度和网络主流程。

**Tech Stack:** .NET 10、ASP.NET Core、EF Core/PostgreSQL、ASP.NET Data Protection、Docker.DotNet、Apache Guacamole、React、TypeScript、SWR、Mantine、xterm.js。

---

## 文件结构

### 主服务

- `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRemoteAccess.cs`：运行凭据、运维会话和审计文件实体。
- `src/GZCTF/Modules/TeamLab/Contracts/TeamLabRemoteAccessContracts.cs`：管理接口请求和响应模型。
- `src/GZCTF/Modules/TeamLab/Application/ITeamLabRemoteAccessNodeGateway.cs`：主服务到节点的远程访问边界。
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRemoteAccessService.cs`：创建、续期、结束和恢复会话。
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRemoteCredentialService.cs`：生成、加密、读取和销毁运行凭据。
- `src/GZCTF/Modules/TeamLab/Application/TeamLabRemoteAccessAuthorizationService.cs`：管理员、所有者和单独授权检查。
- `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabRemoteAccessGateway.cs`：节点接口实现。
- `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRemoteSessionWorker.cs`：超时回收和失联会话收敛。
- `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRemoteAuditStore.cs`：终端记录和 RDP 录像登记、校验与下载。
- `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRemoteAccessController.cs`：管理端远程运维接口。
- `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRemoteAccessEntityConfigurations.cs`：实体映射和索引。

### 镜像和比赛适配

- `src/GZCTF/Modules/Content/Domain/ImageTemplateRemoteAccess.cs`：镜像运维配置和加密凭据。
- `src/GZCTF/Modules/Content/Application/ImageRemoteAccessService.cs`：配置保存和真实连接验证。
- `src/GZCTF/Modules/Content/Infrastructure/Persistence/ImageTemplateEntityConfiguration.cs`：镜像配置关系。
- `src/GZCTF/Controllers/ImageTemplateController.cs`：镜像运维配置管理接口。
- `src/GZCTF/Modules/Penetration/Domain/PenetrationTeamLabOperatorGrant.cs`：比赛级查看和进入授权。
- `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs`：向 TeamLab 返回管理权限。
- `src/GZCTF/Modules/Penetration/Api/PenetrationTeamLabOperatorController.cs`：单独授权管理接口。

### 来宾和计算节点

- `src/GZCTF.GuestControl.Contracts/GuestControlProtocol.cs`：平台自动创建运维账号的来宾意图。
- `src/GZCTF.GuestSupervisor/Lifecycle/GuestRemoteAccessProvisioner.cs`：Linux/Windows 本地账号配置。
- `src/GZCTF.Agent/Services/RemoteAccess/RemoteAccessRelayService.cs`：SSH/RDP 通用短时转发。
- `src/GZCTF.Agent/Services/RemoteAccess/ContainerTerminalService.cs`：Docker 交互终端。
- `src/GZCTF.Agent/Services/RemoteAccess/RemoteAccessSourcePolicy.cs`：可信来源限制。
- `src/GZCTF.Agent/Controllers/RemoteAccessController.cs`：节点内部转发和终端接口。
- `src/GZCTF/Services/Fleet/AgentClient.cs`：主服务节点客户端。
- `src/GZCTF/Services/GuacamoleRemoteSessionService.cs`：临时连接、受限用户和录像参数。

### 前端

- `src/GZCTF/ClientApp/src/vnext/features/admin/images/ImageRemoteAccessForm.tsx`：镜像运维账号配置。
- `src/GZCTF/ClientApp/src/vnext/features/admin/games/teamlab/TeamLabOperatorGrants.tsx`：比赛授权管理。
- `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/RuntimeRemoteAccessPanel.tsx`：队伍资产运维入口。
- `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/RemoteSessionDialog.tsx`：SSH/RDP 会话容器。
- `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/ContainerTerminal.tsx`：容器网页终端。
- `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/RemoteSessionAuditDrawer.tsx`：审计查看。
- `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/api/teamlabRemoteAccessApi.ts`：远程运维 API 封装。
- `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/api/teamlabRemoteAccessContracts.ts`：前端契约。

## Task 1：持久化模型和基础契约

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Domain/Runtime/TeamLabRemoteAccess.cs`
- Create: `src/GZCTF/Modules/Content/Domain/ImageTemplateRemoteAccess.cs`
- Create: `src/GZCTF/Modules/Penetration/Domain/PenetrationTeamLabOperatorGrant.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/Persistence/TeamLabRemoteAccessEntityConfigurations.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Migrations/20260727150000_AddTeamLabRemoteOperations.cs`
- Modify: `src/GZCTF/Migrations/AppDbContextModelSnapshot.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRemoteAccessDomainTests.cs`
- Test: `src/GZCTF.Integration.Test/Tests/Database/TeamLabRemoteAccessPersistenceTests.cs`

- [ ] **Step 1: 定义最小状态模型**

```csharp
public enum TeamLabRemoteProtocol : byte { ContainerTerminal = 1, Ssh = 2, Rdp = 3 }
public enum TeamLabRemoteSessionStatus : byte { Creating = 1, Ready = 2, Connected = 3, Ending = 4, Ended = 5, Failed = 6 }
public enum RemoteCredentialMode : byte { PlatformGenerated = 1, ExistingAccount = 2 }
[Flags]
public enum TeamLabOperatorPermission : byte { None = 0, ViewAssets = 1, OperateAssets = 2 }

public sealed class TeamLabRemoteSession
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.CreateVersion7();
    public int RuntimeId { get; set; }
    public int Generation { get; set; }
    public int RuntimeAssetId { get; set; }
    public Guid WorkerNodeId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public TeamLabRemoteProtocol Protocol { get; set; }
    public TeamLabRemoteSessionStatus Status { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? RelayId { get; set; }
    public string? GuacamoleConnectionId { get; set; }
    public string? GuacamoleUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? EndReason { get; set; }
    public Guid CorrelationId { get; set; }
}
```

运行凭据实体必须包含 `RuntimeId + Generation + RuntimeAssetId` 唯一索引和加密后的密码或私钥；会话表不得保存凭据。镜像运维配置与 `ImageTemplate` 一对一；比赛授权使用 `GameId + UserId` 唯一索引。

- [ ] **Step 2: 添加数据库约束**

迁移必须包含：会话公共编号唯一索引、活动会话到期索引、运行凭据唯一索引、比赛授权唯一索引、所有运行环境和用户外键。运行环境删除时级联删除运行凭据，审计会话保留并将资源关系设为受限删除，防止审计记录被意外清空。

- [ ] **Step 3: 验证模型**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~TeamLabRemoteAccessDomainTests" -p:CollectCoverage=false
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter "FullyQualifiedName~TeamLabRemoteAccessPersistenceTests" -p:CollectCoverage=false
dotnet ef migrations has-pending-model-changes --project src/GZCTF/GZCTF.csproj --startup-project src/GZCTF/GZCTF.csproj --no-build
```

Expected: 领域约束测试通过，迁移可应用，EF 报告无待生成模型变化。

## Task 2：权限和单独授权

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Application/ITeamLabRemoteAccessAuthorizationProvider.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRemoteAccessAuthorizationService.cs`
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabAdapter.cs`
- Create: `src/GZCTF/Modules/Penetration/Api/PenetrationTeamLabOperatorController.cs`
- Modify: `src/GZCTF/Modules/Penetration/PenetrationModuleRegistration.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRemoteAccessAuthorizationTests.cs`
- Test: `src/GZCTF.Integration.Test/Tests/Api/PenetrationTeamLabOperatorApiTests.cs`

- [ ] **Step 1: 扩展解耦授权契约**

```csharp
public interface ITeamLabRemoteAccessAuthorizationProvider
{
    Task<TeamLabOperatorPermission> GetRemoteAccessPermissionsAsync(
        int runtimeId,
        Guid actorUserId,
        CancellationToken cancellationToken);
}
```

管理员角色在主服务统一授予 `ViewAssets | OperateAssets`；适配器只处理比赛所有者和单独授权。比赛所有者返回全部权限，其他用户读取 `PenetrationTeamLabOperatorGrant`。不得修改现有 `ITeamLabRuntimeManagerAuthorizationProvider`，避免远程运维授权意外授予环境重置、销毁或开放 API 管理权限。

- [ ] **Step 2: 增加授权管理接口**

```text
GET    /api/admin/pentest/games/{gameId}/teamlab/operators
PUT    /api/admin/pentest/games/{gameId}/teamlab/operators/{userId}
DELETE /api/admin/pentest/games/{gameId}/teamlab/operators/{userId}
```

只有管理员或比赛所有者能修改授权。`PUT` 接收两个布尔值 `viewAssets` 和 `operateAssets`；服务端必须保证 `operateAssets=true` 时同时保存 `viewAssets=true`。

- [ ] **Step 3: 验证越权边界**

覆盖管理员、比赛所有者、仅查看授权、进入授权、无授权、其他比赛授权以及篡改 `runtimeId` 六类情况。

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~TeamLabRemoteAccessAuthorizationTests" -p:CollectCoverage=false
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter "FullyQualifiedName~PenetrationTeamLabOperatorApiTests" -p:CollectCoverage=false
```

Expected: 仅管理员、所有者和被授予相应权限的用户可以查看或进入资产。

## Task 3：镜像运维配置和运行凭据

**Files:**
- Create: `src/GZCTF/Modules/Content/Application/ImageRemoteAccessService.cs`
- Modify: `src/GZCTF/Controllers/ImageTemplateController.cs`
- Modify: `src/GZCTF/Models/Data/ImageTemplate.cs`
- Modify: `src/GZCTF/Modules/Content/Infrastructure/Persistence/ImageTemplateEntityConfiguration.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRemoteCredentialService.cs`
- Modify: `src/GZCTF.GuestControl.Contracts/GuestControlProtocol.cs`
- Create: `src/GZCTF.GuestSupervisor/Lifecycle/GuestRemoteAccessProvisioner.cs`
- Modify: `src/GZCTF.GuestSupervisor/Lifecycle/GuestLifecycleEngine.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabNodeExecutor.cs`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRemoteCredentialTests.cs`
- Test: `src/GZCTF.Test/UnitTests/Runtime/GuestRemoteAccessProvisionerTests.cs`

- [ ] **Step 1: 增加镜像运维配置接口**

```text
GET   /api/v1/image-templates/{id}/remote-access
PATCH /api/v1/image-templates/{id}/remote-access
POST  /api/v1/image-templates/{id}/remote-access/test
```

`PATCH` 接收 `enabled`、`protocol`、`port`、`username`、`credentialMode`、`credentialKind` 以及可选的新密码或私钥。读取接口只返回 `hasCredential`，不返回加密字段或明文。

- [ ] **Step 2: 使用平台现有密钥体系保护凭据**

```csharp
public sealed class TeamLabRemoteCredentialService(IDataProtectionProvider protection)
{
    readonly IDataProtector _protector = protection.CreateProtector("teamlab.remote-access.v1");

    public string Protect(string value) => _protector.Protect(value);
    public string Unprotect(string value) => _protector.Unprotect(value);
}
```

所有日志只记录模板编号、运行环境、资产和凭据方式，不记录用户名以外的秘密。更新配置时，未提交新密码或私钥表示保留原秘密，不能误清空。

- [ ] **Step 3: 实现平台自动创建账号**

扩展来宾意图：

```csharp
public sealed record GuestRemoteAccessIntent(
    string Protocol,
    string Username,
    string? AuthorizedPublicKey,
    string? ProtectedPasswordSecretName);
```

Linux 使用固定命令参数创建受限命名的运维用户并写入 `authorized_keys`；Windows 使用系统本地用户接口创建随机管理员账号并启用远程桌面。命令参数使用 `ProcessStartInfo.ArgumentList`，秘密通过来宾秘密存储读取，不拼入 shell 字符串或日志。

- [ ] **Step 4: 将凭据绑定运行资产**

`AgentTeamLabNodeExecutor` 在创建虚拟机前调用凭据服务。自动模式生成每个 `RuntimeId + Generation + AssetId` 独立凭据并写入来宾意图；已有账号模式只引用镜像加密凭据。受管虚拟机优先通过既有来宾管理接口验证，不能为本功能新增网卡；其他资产通过场景地址验证。

- [ ] **Step 5: 验证秘密边界**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~TeamLabRemoteCredentialTests|FullyQualifiedName~GuestRemoteAccessProvisionerTests" -p:CollectCoverage=false
```

Expected: 每个运行资产凭据不同，重置后旧凭据不可复用，接口和日志无明文，Linux/Windows 配置行为幂等。

## Task 4：节点临时转发和容器终端

**Files:**
- Create: `src/GZCTF.Agent/Services/RemoteAccess/RemoteAccessSourcePolicy.cs`
- Create: `src/GZCTF.Agent/Services/RemoteAccess/RemoteAccessRelayService.cs`
- Create: `src/GZCTF.Agent/Services/RemoteAccess/ContainerTerminalService.cs`
- Create: `src/GZCTF.Agent/Models/RemoteAccessModels.cs`
- Create: `src/GZCTF.Agent/Controllers/RemoteAccessController.cs`
- Modify: `src/GZCTF.Agent/Services/KvmService.cs`
- Modify: `src/GZCTF.Agent/Program.cs`
- Test: `src/GZCTF.Test/UnitTests/Vm/RemoteAccessRelayTests.cs`
- Test: `src/GZCTF.Test/UnitTests/Container/ContainerTerminalTests.cs`

- [ ] **Step 1: 将现有 RDP 转发收敛为通用服务**

```csharp
public sealed record CreateRemoteRelayRequest(
    Guid SessionId,
    int RuntimeId,
    int Generation,
    string AssetResourceId,
    string TargetAddress,
    int TargetPort,
    string[] AllowedSources,
    DateTimeOffset ExpiresAt);
```

通用转发必须校验：运行环境代次、资产实际归属、目标地址属于该资产、端口在 1-65535、到期时间不超过平台上限。监听地址使用节点管理地址；无合法可信来源时拒绝创建，不退化到全网监听。

- [ ] **Step 2: 定义节点内部接口**

```text
POST   /api/remote-access/relays
GET    /api/remote-access/relays/{sessionId}
DELETE /api/remote-access/relays/{sessionId}
GET    /api/remote-access/terminals/{sessionId}
```

终端接口只接受 WebSocket 升级。节点根据会话绑定的容器编号创建带 TTY 的 `docker exec`，双向转发标准输入输出，并在断开后停止执行进程。

- [ ] **Step 3: 加入确定性回收**

转发和终端以 `sessionId` 幂等创建。到期、取消令牌、Agent 停止或显式删除都会关闭监听器、客户端连接和 Docker 执行流。不得用自动重试重新打开已经结束的会话。

- [ ] **Step 4: 验证来源限制和资源归属**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~RemoteAccessRelayTests|FullyQualifiedName~ContainerTerminalTests" -p:CollectCoverage=false
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj -c Release --no-restore
```

Expected: 非可信来源、错误代次、错误资产地址和过期会话全部失败关闭；断开后无监听端口和 Docker 执行残留。

## Task 5：Guacamole 和主服务会话编排

**Files:**
- Create: `src/GZCTF/Services/GuacamoleRemoteSessionService.cs`
- Modify: `src/GZCTF/Models/Internal/GuacamoleSettings.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/ITeamLabRemoteAccessNodeGateway.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/AgentTeamLabRemoteAccessGateway.cs`
- Create: `src/GZCTF/Modules/TeamLab/Application/TeamLabRemoteAccessService.cs`
- Create: `src/GZCTF/Modules/TeamLab/Contracts/TeamLabRemoteAccessContracts.cs`
- Create: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRemoteAccessController.cs`
- Modify: `src/GZCTF/Services/Fleet/AgentClient.cs`
- Modify: `src/GZCTF/Modules/TeamLab/TeamLabModuleRegistration.cs`
- Test: `src/GZCTF.Test/UnitTests/Services/GuacamoleRemoteSessionServiceTests.cs`
- Test: `src/GZCTF.Integration.Test/Tests/Api/TeamLabAdminRemoteAccessApiTests.cs`

- [ ] **Step 1: 创建受限的 Guacamole 临时身份**

不得调用现有 `GetAuthenticatedConnectionUrlAsync`，因为它会把 Guacamole 管理令牌附加到浏览器 URL。新服务应：

1. 创建带录像参数的临时 SSH/RDP 连接。
2. 创建随机临时 Guacamole 用户。
3. 只授予该用户读取和使用当前连接的权限。
4. 使用临时用户换取受限令牌。
5. 会话结束时删除临时用户和连接。

RDP 连接保留剪贴板配置；SSH 连接使用密码或私钥。日志不得写入连接参数中的凭据。

- [ ] **Step 2: 实现应用服务事务边界**

```csharp
public interface ITeamLabRemoteAccessService
{
    Task<TeamLabRemoteSessionModel> CreateAsync(Guid runtimeId, int assetId, Guid actorId, string reason, CancellationToken token);
    Task<TeamLabRemoteSessionModel> GetAsync(Guid sessionId, Guid actorId, CancellationToken token);
    Task<TeamLabRemoteConnectModel> ConnectAsync(Guid sessionId, Guid actorId, CancellationToken token);
    Task<TeamLabRemoteSessionModel> ExtendAsync(Guid sessionId, Guid actorId, CancellationToken token);
    Task EndAsync(Guid sessionId, Guid actorId, string reason, CancellationToken token);
}
```

创建顺序固定为：保存 `Creating` 会话、节点创建转发、Guacamole 创建临时身份、更新为 `Ready`。任一步失败都按已创建资源的事实反向清理，并将会话置为 `Failed`，不能只回滚数据库而遗留节点监听器。

- [ ] **Step 3: 提供管理接口**

```text
GET    /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-access
POST   /api/admin/teamlab/runtimes/{runtimeId}/assets/{assetId}/remote-sessions
GET    /api/admin/teamlab/remote-sessions/{sessionId}
GET    /api/admin/teamlab/remote-sessions/{sessionId}/connect
GET    /api/admin/teamlab/remote-sessions/{sessionId}/terminal
POST   /api/admin/teamlab/remote-sessions/{sessionId}/extend
DELETE /api/admin/teamlab/remote-sessions/{sessionId}
```

创建请求必须包含 4-500 字符的访问原因。连接地址只能由已授权用户获取，且每次获取重新检查会话状态和权限。`terminal` 只允许容器会话进行 WebSocket 升级，主平台复用现有 WebSocket 代理边界将帧转发给指定 Agent，不允许浏览器直接连接 Agent。

- [ ] **Step 4: 验证会话创建失败补偿**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~GuacamoleRemoteSessionServiceTests" -p:CollectCoverage=false
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --filter "FullyQualifiedName~TeamLabAdminRemoteAccessApiTests" -p:CollectCoverage=false
```

Expected: 浏览器只得到受限临时令牌；节点失败、Guacamole 失败和数据库失败均不遗留可访问连接。

## Task 6：审计、录像和生命周期回收

**Files:**
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRemoteAuditStore.cs`
- Create: `src/GZCTF/Modules/TeamLab/Infrastructure/TeamLabRemoteSessionWorker.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRemoteAccessService.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Application/TeamLabRuntimeCleanupService.cs`
- Modify: `src/GZCTF/Modules/Penetration/Application/PenetrationTeamLabLifecycleObserver.cs`
- Modify: `src/GZCTF/Modules/Audit/Domain/OperationalEventCodes.cs`
- Modify: `src/GZCTF/Modules/TeamLab/Api/TeamLabAdminRemoteAccessController.cs`
- Modify: `docker-compose.yml`
- Modify: `docker-compose.dev.yml`
- Test: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabRemoteSessionLifecycleTests.cs`

- [ ] **Step 1: 固定审计文件路径和内容**

Guacamole 和主服务共享只用于远程会话的录像目录。路径由服务端生成：

```text
files/teamlab-remote-audit/{yyyy}/{MM}/{sessionId}/
```

SSH 使用终端文本记录，RDP 使用 Guacamole 录像，容器终端由主服务记录双向文本流。文件完成后计算 SHA-256、大小和到期时间，再登记为审计文件；临时文件使用原子重命名发布。

- [ ] **Step 2: 增加审计查询接口**

```text
GET /api/admin/teamlab/remote-sessions/{sessionId}/audit
GET /api/admin/teamlab/remote-sessions/{sessionId}/recording
```

管理员和比赛所有者可以读取权限范围内的全部审计；单独授权人员只能读取自己创建的会话审计。下载行为写入系统审计，响应使用流式下载，不将录像完整读入内存。

- [ ] **Step 3: 联动环境生命周期**

环境重置、销毁、比赛关闭访问、比赛回收和授权撤销必须先结束相关会话。会话回收失败时记录明确事件，但不得阻断运行环境的安全销毁；节点清理仍按运行环境代次删除转发。

- [ ] **Step 4: 实现后台收敛**

后台任务只处理已到期或状态不一致的会话：关闭节点转发、删除 Guacamole 临时身份、登记现有录像并更新结束原因。不自动重建会话，不延长会话，不重试已经确定失败的登录。

- [ ] **Step 5: 验证生命周期**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --filter "FullyQualifiedName~TeamLabRemoteSessionLifecycleTests" -p:CollectCoverage=false
```

Expected: 超时、重置、销毁、关闭访问和权限撤销都会终止会话，保留完整审计，并且无监听端口和临时 Guacamole 身份残留。

## Task 7：镜像配置和比赛授权界面

**Files:**
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/api/imageTemplateAdminApi.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/images/ImageRemoteAccessForm.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/images/ImageActionDialog.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/games/teamlab/TeamLabOperatorGrants.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/games/teamlab/AdminGameTeamLabPage.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/games/teamlab/teamlabGameAdminApi.test.ts`
- Test: `src/GZCTF/ClientApp/src/vnext/features/admin/images/ImageRemoteAccessForm.test.tsx`
- Test: `src/GZCTF/ClientApp/src/vnext/features/admin/games/teamlab/TeamLabOperatorGrants.test.tsx`

- [ ] **Step 1: 增加镜像运维表单**

表单只展示“平台自动创建账号”和“使用镜像已有账号”。选择已有账号时显示协议、端口、用户名、密码或私钥一次性输入；保存后只显示“已配置”，不回填秘密。

- [ ] **Step 2: 增加真实连接测试反馈**

测试按钮提交后台任务，并展示“正在检查、可用、端口不可达、认证失败”四类稳定状态。不要在前端猜测镜像类型或操作系统能力。

- [ ] **Step 3: 增加比赛授权管理**

复用现有用户搜索结果，列表显示头像、名称、“查看运行资产”和“进入资产运维”两个开关。开启进入权限时自动开启查看权限；关闭查看权限时同时关闭进入权限。

- [ ] **Step 4: 前端单元验证**

Run:

```powershell
pnpm --dir src/GZCTF/ClientApp vitest run src/vnext/features/admin/images/ImageRemoteAccessForm.test.tsx src/vnext/features/admin/games/teamlab/TeamLabOperatorGrants.test.tsx
```

Expected: 秘密不回显，权限联动正确，失败状态中文可读。

## Task 8：运行资产会话和审计界面

**Files:**
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/api/teamlabRemoteAccessContracts.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/api/teamlabRemoteAccessApi.ts`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/RuntimeRemoteAccessPanel.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/RemoteSessionDialog.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/ContainerTerminal.tsx`
- Create: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/RemoteSessionAuditDrawer.tsx`
- Modify: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/TeamLabRuntimeDetailPage.tsx`
- Modify: `src/GZCTF/ClientApp/package.json`
- Modify: `src/GZCTF/ClientApp/pnpm-lock.yaml`
- Test: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/RuntimeRemoteAccessPanel.test.tsx`
- Test: `src/GZCTF/ClientApp/src/vnext/features/admin/teamlab/runtimes/ContainerTerminal.test.tsx`

- [ ] **Step 1: 增加资产运维状态**

资产表按后端事实显示：未配置、正在检查、SSH 可用、RDP 可用、连接失败、资产未运行和正在运维。没有进入权限时不显示进入按钮。

- [ ] **Step 2: 创建会话交互**

用户必须填写访问原因后才能创建会话。SSH/RDP 在受控对话框中打开一次性连接地址；对话框显示比赛、队伍、资产、协议和剩余时间，并提供续期与断开。

- [ ] **Step 3: 实现容器终端**

引入 `@xterm/xterm` 和 fit addon。WebSocket 只连接主平台会话地址，不直接连接 Agent。组件卸载、浏览器离线或服务端结束消息都会关闭终端并停止发送输入。

- [ ] **Step 4: 增加审计查看**

审计抽屉显示访问者、原因、时间、结束原因和文件状态。SSH/容器文本使用独立滚动区；RDP 录像使用受控下载或回放入口，不在列表中预加载大文件。

- [ ] **Step 5: 前端大单元验证**

Run:

```powershell
pnpm --dir src/GZCTF/ClientApp vitest run src/vnext/features/admin/teamlab/runtimes/RuntimeRemoteAccessPanel.test.tsx src/vnext/features/admin/teamlab/runtimes/ContainerTerminal.test.tsx
pnpm --dir src/GZCTF/ClientApp check
```

Expected: 权限、状态、会话倒计时、断开、终端清理和审计按需加载全部通过，生产构建预算不退化。

## Task 9：集中验收和文档同步

**Files:**
- Modify: `docs/commercialization/teamlab-networking-current-progress.md`
- Create: `docs/commercialization/runbooks/teamlab-admin-remote-access-acceptance.md`
- Do not modify: `docs/commercialization/openapi/open-v1.json`，本计划不增加外部远程运维接口。

- [ ] **Step 1: 后端集中验证**

```powershell
dotnet build src/GZCTF/GZCTF.csproj -c Release --no-restore
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj -c Release --no-restore
dotnet build src/GZCTF.GuestSupervisor/GZCTF.GuestSupervisor.csproj -c Release --no-restore
dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build -p:CollectCoverage=false
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-build -p:CollectCoverage=false
```

Expected: 构建 0 错误，全部功能测试通过。

- [ ] **Step 2: 前端集中验证**

```powershell
pnpm --dir src/GZCTF/ClientApp check
```

Expected: 本地化、代码检查、类型检查、架构检查、测试、生产构建和包体预算全部通过。

- [ ] **Step 3: 部署环境验收**

只创建一套隔离验收环境，包含一个容器、一个 Linux 虚拟机和一个 Windows 虚拟机。验证：

1. 管理员、比赛所有者、仅查看用户、进入授权用户和无权限用户。
2. 容器终端、SSH、RDP 和剪贴板。
3. 浏览器网络记录中没有密码、私钥、Agent 地址和转发端口。
4. 选手 VPN 无法访问节点转发地址。
5. SSH/容器文本和 RDP 录像完整生成。
6. 主动断开、超时、权限撤销、重置和销毁均关闭会话。
7. 两台 Worker 上无验收会话监听器、Docker exec、临时 Guacamole 用户和连接残留。

- [ ] **Step 4: 更新说明和验收证据**

进度文档使用甲方可读语言增加管理员远程运维能力；验收手册记录会话编号、资产放置、权限结果、连接结果、审计文件校验值和清理证据，不记录任何凭据。

- [ ] **Step 5: 最终一致性检查**

```powershell
git diff --check
git status --short
```

Expected: 无空白错误；只包含计划内源代码、迁移、测试、前端依赖和文档。
