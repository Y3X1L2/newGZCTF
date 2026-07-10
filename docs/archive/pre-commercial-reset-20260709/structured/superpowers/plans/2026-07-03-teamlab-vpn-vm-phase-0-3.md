# TeamLab VPN VM Phase 0-3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付多级网段渗透 TeamLab 重构的 Phase 0-3：冻结旧链路回归、建立 TeamLab 控制面模型、节点 TeamLabNetwork 能力、基础设施 WireGuard 隧道、公网 UDP Gateway 抽象、单 WorkerNode Linux bridge 数据面闭环。

**Architecture:** 主服务器只做控制面，WorkerNode 承担 TeamLab 数据面，公网服务器作为薄 UDP Gateway。第一批实现先建立可关闭的 TeamLabNetwork 能力开关和 dry-run/命令构建器，再接入真实 Linux bridge、router namespace 和 WireGuard 入口，避免影响普通 CTF/AWDP/VM/Nginx-Redis TCP 链路。

**Tech Stack:** ASP.NET Core / EF Core / PostgreSQL / GZCTF Agent / Linux WireGuard / nftables or iptables / Linux bridge / xUnit / React + Mantine.

---

## Scope

本计划只覆盖 `docs/pentest-vpn-vm-phase-plan.md` 的 Phase 0-3。

进入范围：

- 现有链路回归基线文档与最小测试。
- TeamLab 控制面数据模型、状态机、事件和公网 UDP 映射事实源。
- WorkerNode 的 TeamLabNetwork 能力字段、节点启用入口、调度过滤。
- Agent 上 WireGuard/bridge/ip/nftables 命令构建、dry-run、状态探测接口。
- 主服务 `NodeTunnelService`、`PublicUdpGatewayProvider`、`TeamLabPlanService`、`TeamLabDeploymentService` 的 Phase 0-3 最小实现。
- 单 WorkerNode 上两个队伍 TeamLab 的 bridge/router/WireGuard 资源可创建、探测、销毁。

不进入范围：Docker 资产迁移到 TeamLab、VM bridge 接入、DHCP/DNS、AD 编排、完整管理画布升级、选手端正式 VPN 页面。这些依赖 Phase 0-3 的数据面闭环，单独写 Phase 4-8 计划。

## File Map

Backend model and migrations:

- Create: `src/GZCTF/Models/Data/TeamLabEntities.cs` for TeamLab runtime, runtime network, runtime asset, runtime interface, VPN peer, UDP mapping, and events.
- Modify: `src/GZCTF/Models/Data/WorkerNode.cs` to add TeamLabNetwork capability/status fields.
- Modify: `src/GZCTF/Models/AppDbContext.cs` to add DbSets, indexes, relationships.
- Create: `src/GZCTF/Migrations/<timestamp>_AddTeamLabNetworkControlPlane.cs` using EF migration tooling.
- Modify: `src/GZCTF/Models/Internal/Configs.cs` to add `TeamLabNetworkConfig` and `PublicUdpGatewayConfig`.
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs` to register new services and configs.

Backend services and controllers:

- Create: `src/GZCTF/Services/TeamLab/TeamLabStateMachine.cs` for legal state transitions.
- Create: `src/GZCTF/Services/TeamLab/TeamLabPlanService.cs` for IPAM, capacity, UDP, and node planning.
- Create: `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs` for Phase 3 deploy/probe/destroy orchestration.
- Create: `src/GZCTF/Services/TeamLab/NodeTunnelService.cs` for WorkerNode infra tunnel provisioning and probes.
- Create: `src/GZCTF/Services/TeamLab/PublicUdpGatewayProvider.cs` for public UDP mapping sync abstraction and local dry-run implementation.
- Create: `src/GZCTF/Controllers/TeamLabAdminController.cs` for admin deploy/cleanup/status endpoints.
- Modify: `src/GZCTF/Controllers/NodesController.cs` for TeamLabNetwork status and enable action.
- Modify: `src/GZCTF/Services/Fleet/WeightedScheduler.cs` for TeamLab capability filtering.
- Modify: `src/GZCTF/Services/Fleet/AgentClient.cs` for Agent TeamLab endpoints.

Agent:

- Create: `src/GZCTF.Agent/Models/TeamLabModels.cs` for tunnel, bridge, router, WireGuard, and probe request/response DTOs.
- Create: `src/GZCTF.Agent/Services/TeamLabCommandRunner.cs` for bounded shell command execution.
- Create: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs` for command builders and dry-run execution.
- Create: `src/GZCTF.Agent/Controllers/TeamLabController.cs` for status, enable, bridge/router/wireguard/probe/cleanup endpoints.
- Modify: `src/GZCTF.Agent/Models/AgentConfig.cs` to add TeamLab local config.
- Modify: `src/GZCTF.Agent/Program.cs` to register the Agent TeamLab service.

Frontend:

- Modify: `src/GZCTF/ClientApp/src/components/admin/NodeCard.tsx` to display TeamLabNetwork capability/status without mixing Docker TCP port pool.
- Modify: `src/GZCTF/ClientApp/src/pages/admin/nodes/Index.tsx` to add enable TeamLabNetwork action and UDP channel display.
- Create: `src/GZCTF/ClientApp/src/utils/TeamLabApi.ts` for typed admin calls.

Tests and docs:

- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabStateMachineTests.cs`.
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlanServiceTests.cs`.
- Create: `src/GZCTF.Test/UnitTests/TeamLab/PublicUdpGatewayProviderTests.cs`.
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommandBuilderTests.cs`.
- Modify: `src/GZCTF.Test/UnitTests/Fleet/WeightedSchedulerTests.cs`.
- Modify: `src/GZCTF.Test/UnitTests/Fleet/NodesControllerTests.cs`.
- Create: `docs/teamlab-phase0-baseline.md`.
- Create: `docs/teamlab-phase0-3-operator-runbook.md`.

## Task 0: Baseline Freeze

**Files:**
- Create: `docs/teamlab-phase0-baseline.md`
- Modify: none

- [ ] **Step 1: Record existing critical flows**

Add `docs/teamlab-phase0-baseline.md` with this structure:

```markdown
# TeamLab Phase 0 Baseline

## Existing Flows That Must Not Regress

- Normal CTF Docker: create, public TCP proxy, destroy.
- AWDP: service scheduling and scoring remain on existing path.
- VM/KVM: current libvirt default NAT and Guacamole management path remain usable.
- Existing penetration Docker fabric: existing games remain readable and deployable until migrated.
- Nginx/Redis TCP proxy: remains separate from TeamLab WireGuard UDP entry.

## Regression Commands

- dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Fleet"
- dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Vm"
- pnpm --dir src/GZCTF/ClientApp check

## Manual Server Smoke Checks

- Create one normal Docker challenge container and verify public TCP entry.
- Destroy the container and verify Nginx/Redis mapping is released.
- Create one current VM challenge and verify Guacamole URL still resolves.
- Open one existing penetration game and verify it does not require TeamLab fields.
```

- [ ] **Step 2: Run baseline tests**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Fleet"
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~Vm"
pnpm --dir src/GZCTF/ClientApp check
```

Expected: existing tests pass. If a pre-existing unrelated failure appears, record exact test name and error in `docs/teamlab-phase0-baseline.md` under `Known Pre-existing Failures` before modifying code.

- [ ] **Step 3: Commit baseline doc**

```powershell
git add docs/teamlab-phase0-baseline.md
git commit -m "docs: freeze TeamLab phase 0 baseline"
```

## Task 1: TeamLab Model and State Machine Tests

**Files:**
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabStateMachineTests.cs`
- Create: `src/GZCTF/Services/TeamLab/TeamLabStateMachine.cs`
- Create: `src/GZCTF/Models/Data/TeamLabEntities.cs`

- [ ] **Step 1: Write failing state-machine tests**

Create `src/GZCTF.Test/UnitTests/TeamLab/TeamLabStateMachineTests.cs`:

```csharp
using GZCTF.Models.Data;
using GZCTF.Services.TeamLab;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabStateMachineTests
{
    [Theory]
    [InlineData(TeamLabRuntimeStatus.Pending, TeamLabRuntimeStatus.Planning)]
    [InlineData(TeamLabRuntimeStatus.Planning, TeamLabRuntimeStatus.Scheduled)]
    [InlineData(TeamLabRuntimeStatus.Scheduled, TeamLabRuntimeStatus.Deploying)]
    [InlineData(TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.Probing)]
    [InlineData(TeamLabRuntimeStatus.Probing, TeamLabRuntimeStatus.Running)]
    [InlineData(TeamLabRuntimeStatus.Running, TeamLabRuntimeStatus.Destroying)]
    [InlineData(TeamLabRuntimeStatus.Destroying, TeamLabRuntimeStatus.Destroyed)]
    public void CanTransition_AllowsExpectedRuntimePath(TeamLabRuntimeStatus from, TeamLabRuntimeStatus to)
    {
        Assert.True(TeamLabStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void CanTransition_DisallowsPartialSuccessToRunning()
    {
        Assert.False(TeamLabStateMachine.CanTransition(TeamLabRuntimeStatus.Failed, TeamLabRuntimeStatus.Running));
        Assert.False(TeamLabStateMachine.CanTransition(TeamLabRuntimeStatus.CleanupPending, TeamLabRuntimeStatus.Running));
    }
}
```

- [ ] **Step 2: Run test and confirm failure**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabStateMachineTests"
```

Expected: FAIL because `TeamLabRuntimeStatus` and `TeamLabStateMachine` do not exist.

- [ ] **Step 3: Implement model enums and state machine**

Create `src/GZCTF/Models/Data/TeamLabEntities.cs` with at least these enums:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Models.Data;

public enum TeamLabRuntimeStatus : byte
{
    Pending = 0,
    Planning = 1,
    Scheduled = 2,
    Deploying = 3,
    Probing = 4,
    Running = 5,
    Failed = 6,
    CleanupPending = 7,
    Stopped = 8,
    Destroying = 9,
    Destroyed = 10
}

public enum TeamLabResourceKind : byte
{
    Docker = 0,
    Vm = 1,
    RouterNamespace = 2,
    DhcpDnsService = 3,
    WireGuard = 4,
    PublicUdpMapping = 5
}

public enum TeamLabEventLevel : byte
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}
```

Create `src/GZCTF/Services/TeamLab/TeamLabStateMachine.cs`:

```csharp
using GZCTF.Models.Data;

namespace GZCTF.Services.TeamLab;

public static class TeamLabStateMachine
{
    private static readonly HashSet<(TeamLabRuntimeStatus From, TeamLabRuntimeStatus To)> Allowed =
    [
        (TeamLabRuntimeStatus.Pending, TeamLabRuntimeStatus.Planning),
        (TeamLabRuntimeStatus.Stopped, TeamLabRuntimeStatus.Planning),
        (TeamLabRuntimeStatus.Failed, TeamLabRuntimeStatus.Planning),
        (TeamLabRuntimeStatus.Destroyed, TeamLabRuntimeStatus.Planning),
        (TeamLabRuntimeStatus.Planning, TeamLabRuntimeStatus.Scheduled),
        (TeamLabRuntimeStatus.Planning, TeamLabRuntimeStatus.Failed),
        (TeamLabRuntimeStatus.Scheduled, TeamLabRuntimeStatus.Deploying),
        (TeamLabRuntimeStatus.Scheduled, TeamLabRuntimeStatus.Failed),
        (TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.Probing),
        (TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.Failed),
        (TeamLabRuntimeStatus.Deploying, TeamLabRuntimeStatus.CleanupPending),
        (TeamLabRuntimeStatus.Probing, TeamLabRuntimeStatus.Running),
        (TeamLabRuntimeStatus.Probing, TeamLabRuntimeStatus.Failed),
        (TeamLabRuntimeStatus.Running, TeamLabRuntimeStatus.Stopped),
        (TeamLabRuntimeStatus.Running, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.Failed, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.CleanupPending, TeamLabRuntimeStatus.Destroying),
        (TeamLabRuntimeStatus.Destroying, TeamLabRuntimeStatus.Destroyed),
        (TeamLabRuntimeStatus.Destroying, TeamLabRuntimeStatus.CleanupPending)
    ];

    public static bool CanTransition(TeamLabRuntimeStatus from, TeamLabRuntimeStatus to) =>
        from == to || Allowed.Contains((from, to));
}
```

- [ ] **Step 4: Run test and commit**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabStateMachineTests"
```

Expected: PASS.

Commit:

```powershell
git add src/GZCTF/Models/Data/TeamLabEntities.cs src/GZCTF/Services/TeamLab/TeamLabStateMachine.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabStateMachineTests.cs
git commit -m "feat: add TeamLab runtime state machine"
```

## Task 2: Persistent TeamLab Control Plane Entities

**Files:**
- Modify: `src/GZCTF/Models/Data/TeamLabEntities.cs`
- Modify: `src/GZCTF/Models/AppDbContext.cs`
- Create: `src/GZCTF/Migrations/<timestamp>_AddTeamLabNetworkControlPlane.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabModelTests.cs`

- [ ] **Step 1: Write failing model tests**

Create `TeamLabModelTests.cs`:

```csharp
using GZCTF.Models.Data;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabModelTests
{
    [Fact]
    public void TeamLabRuntime_DefaultsAreSafe()
    {
        var runtime = new TeamLabRuntime { GameId = 1, TeamId = 2 };

        Assert.Equal(TeamLabRuntimeStatus.Pending, runtime.Status);
        Assert.Equal(string.Empty, runtime.NetworkPrefix);
        Assert.False(runtime.IsOpenToPlayers);
    }

    [Fact]
    public void PublicUdpMapping_DefaultsAreUnsynced()
    {
        var mapping = new TeamLabPublicUdpMapping { PublicUdpPort = 32000 };

        Assert.False(mapping.IsSynced);
        Assert.Equal(0, mapping.RuleVersion);
    }
}
```

Run and expect compile failure.

- [ ] **Step 2: Add entity classes**

Extend `TeamLabEntities.cs` with these minimum classes:

```csharp
[Index(nameof(GameId), nameof(TeamId), IsUnique = true)]
[Index(nameof(WorkerNodeId))]
public class TeamLabRuntime
{
    [Key] public int Id { get; set; }
    public int GameId { get; set; }
    public int TeamId { get; set; }
    public int PublishedVersion { get; set; }
    public Guid? WorkerNodeId { get; set; }
    [MaxLength(64)] public string NetworkPrefix { get; set; } = string.Empty;
    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;
    public bool IsOpenToPlayers { get; set; }
    [MaxLength(1024)] public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public Game Game { get; set; } = null!;
    public Team Team { get; set; } = null!;
    public WorkerNode? WorkerNode { get; set; }
    public List<TeamLabRuntimeNetwork> Networks { get; set; } = [];
    public List<TeamLabRuntimeAsset> Assets { get; set; } = [];
    public List<TeamLabEvent> Events { get; set; } = [];
}

public class TeamLabRuntimeNetwork
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    [MaxLength(64)] public string TopologyKey { get; set; } = string.Empty;
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    [MaxLength(64)] public string Cidr { get; set; } = string.Empty;
    [MaxLength(64)] public string GatewayIp { get; set; } = string.Empty;
    [MaxLength(128)] public string BridgeName { get; set; } = string.Empty;
    public TeamLabRuntime Runtime { get; set; } = null!;
}

public class TeamLabRuntimeAsset
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    public TeamLabResourceKind Kind { get; set; }
    [MaxLength(64)] public string TopologyKey { get; set; } = string.Empty;
    [MaxLength(128)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string? RuntimeResourceId { get; set; }
    public TeamLabRuntimeStatus Status { get; set; } = TeamLabRuntimeStatus.Pending;
    [MaxLength(1024)] public string? LastError { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
}

public class TeamLabVpnPeerRuntime
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    [MaxLength(64)] public string ClientAddress { get; set; } = string.Empty;
    [MaxLength(256)] public string Endpoint { get; set; } = string.Empty;
    [MaxLength(256)] public string AllowedIPs { get; set; } = string.Empty;
    [MaxLength(64)] public string Dns { get; set; } = string.Empty;
    [MaxLength(128)] public string PublicKey { get; set; } = string.Empty;
    public int ConfigVersion { get; set; } = 1;
    public bool Revoked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRuntime Runtime { get; set; } = null!;
}

public class TeamLabPublicUdpMapping
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    public int PublicUdpPort { get; set; }
    [MaxLength(64)] public string WorkerTunnelIp { get; set; } = string.Empty;
    public int WorkerWireGuardPort { get; set; }
    public int RuleVersion { get; set; }
    public bool IsSynced { get; set; }
    [MaxLength(1024)] public string? LastSyncError { get; set; }
    public TeamLabRuntime Runtime { get; set; } = null!;
}

public class TeamLabEvent
{
    [Key] public int Id { get; set; }
    public int RuntimeId { get; set; }
    [MaxLength(64)] public string Stage { get; set; } = string.Empty;
    public TeamLabEventLevel Level { get; set; } = TeamLabEventLevel.Info;
    [MaxLength(256)] public string Message { get; set; } = string.Empty;
    [MaxLength(128)] public string? ObjectType { get; set; }
    [MaxLength(128)] public string? ObjectId { get; set; }
    [MaxLength(1024)] public string? Detail { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public TeamLabRuntime Runtime { get; set; } = null!;
}
```

- [ ] **Step 3: Wire DbContext**

Add DbSets in `AppDbContext`:

```csharp
public DbSet<TeamLabRuntime> TeamLabRuntimes => Set<TeamLabRuntime>();
public DbSet<TeamLabRuntimeNetwork> TeamLabRuntimeNetworks => Set<TeamLabRuntimeNetwork>();
public DbSet<TeamLabRuntimeAsset> TeamLabRuntimeAssets => Set<TeamLabRuntimeAsset>();
public DbSet<TeamLabVpnPeerRuntime> TeamLabVpnPeerRuntimes => Set<TeamLabVpnPeerRuntime>();
public DbSet<TeamLabPublicUdpMapping> TeamLabPublicUdpMappings => Set<TeamLabPublicUdpMapping>();
public DbSet<TeamLabEvent> TeamLabEvents => Set<TeamLabEvent>();
```

In `OnModelCreating`, configure cascade delete from runtime to child rows, and restrict WorkerNode delete to avoid accidental cascade deletion.

- [ ] **Step 4: Generate migration and test**

Run:

```powershell
dotnet ef migrations add AddTeamLabNetworkControlPlane --project src/GZCTF/GZCTF.csproj --startup-project src/GZCTF/GZCTF.csproj
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabModelTests"
dotnet build src/GZCTF/GZCTF.csproj --no-restore
```

Expected: tests and build pass.

- [ ] **Step 5: Commit**

```powershell
git add src/GZCTF/Models/Data/TeamLabEntities.cs src/GZCTF/Models/AppDbContext.cs src/GZCTF/Migrations src/GZCTF.Test/UnitTests/TeamLab/TeamLabModelTests.cs
git commit -m "feat: persist TeamLab control plane models"
```

## Task 3: WorkerNode TeamLabNetwork Capability and Scheduling Gate

**Files:**
- Modify: `src/GZCTF/Models/Data/WorkerNode.cs`
- Modify: `src/GZCTF/Services/Fleet/WeightedScheduler.cs`
- Modify: `src/GZCTF/Controllers/NodesController.cs`
- Modify: `src/GZCTF.Test/UnitTests/Fleet/WeightedSchedulerTests.cs`
- Modify: `src/GZCTF.Test/UnitTests/Fleet/NodesControllerTests.cs`

- [ ] **Step 1: Write failing scheduler tests**

Append to `WeightedSchedulerTests.cs`:

```csharp
[Fact]
public void SelectOptimalNode_RequiresTeamLabNetworkCapabilityForTeamLab()
{
    var node = new WorkerNode
    {
        Id = Guid.NewGuid(),
        Capabilities = NodeCapability.Docker | NodeCapability.Kvm,
        Status = NodeStatus.Online,
        IsLocal = true,
        IsSchedulable = true,
        TeamLabNetworkEnabled = false
    };

    Assert.Null(WeightedScheduler.SelectOptimalTeamLabNode([node]));

    node.TeamLabNetworkEnabled = true;
    node.TeamLabTunnelStatus = TeamLabTunnelStatus.Healthy;
    Assert.Equal(node.Id, WeightedScheduler.SelectOptimalTeamLabNode([node])?.Id);
}
```

Run and expect compile failure.

- [ ] **Step 2: Add WorkerNode fields and enums**

Add to `WorkerNode.cs`:

```csharp
public bool TeamLabNetworkEnabled { get; set; }
public TeamLabTunnelStatus TeamLabTunnelStatus { get; set; } = TeamLabTunnelStatus.Disabled;
[MaxLength(64)] public string? TeamLabTunnelIp { get; set; }
public DateTimeOffset? TeamLabTunnelLastHandshake { get; set; }
[MaxLength(1024)] public string? TeamLabTunnelLastError { get; set; }
public int TeamLabTunnelConfigVersion { get; set; }

public enum TeamLabTunnelStatus : byte
{
    Disabled = 0,
    Pending = 1,
    Healthy = 2,
    Degraded = 3,
    Failed = 4
}
```

- [ ] **Step 3: Add scheduler gate**

Add to `WeightedScheduler.cs`:

```csharp
public static WorkerNode? SelectOptimalTeamLabNode(IEnumerable<WorkerNode> nodes) =>
    SelectOptimalNode(nodes.Where(CanHostTeamLab), NodeCapability.Docker | NodeCapability.Kvm);

public static bool CanHostTeamLab(WorkerNode node) =>
    node.TeamLabNetworkEnabled &&
    node.TeamLabTunnelStatus == TeamLabTunnelStatus.Healthy &&
    CanHost(node, NodeCapability.Docker | NodeCapability.Kvm);
```

If `WeightedScheduler` currently does not expose compatible methods, add these methods without changing existing Docker/KVM behavior.

- [ ] **Step 4: Return fields from node APIs**

Modify `NodesController.List` and `Detail` anonymous responses to include:

```csharp
node.TeamLabNetworkEnabled,
node.TeamLabTunnelStatus,
node.TeamLabTunnelIp,
node.TeamLabTunnelLastHandshake,
node.TeamLabTunnelLastError,
node.TeamLabTunnelConfigVersion,
CanHostTeamLab = WeightedScheduler.CanHostTeamLab(node)
```

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~WeightedSchedulerTests|FullyQualifiedName~NodesControllerTests"
dotnet build src/GZCTF/GZCTF.csproj --no-restore
```

Commit:

```powershell
git add src/GZCTF/Models/Data/WorkerNode.cs src/GZCTF/Services/Fleet/WeightedScheduler.cs src/GZCTF/Controllers/NodesController.cs src/GZCTF.Test/UnitTests/Fleet
git commit -m "feat: add TeamLab node capability gate"
```

## Task 4: TeamLab Configuration and Dependency Registration

**Files:**
- Modify: `src/GZCTF/Models/Internal/Configs.cs`
- Modify: `src/GZCTF/Extensions/Startup/ServicesExtension.cs`
- Modify: `src/GZCTF/appsettings.Template.json`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabConfigTests.cs`

- [ ] **Step 1: Add config tests**

Create `TeamLabConfigTests.cs`:

```csharp
using GZCTF.Models.Internal;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabConfigTests
{
    [Fact]
    public void TeamLabNetworkConfig_DefaultsKeepFeatureDisabled()
    {
        var config = new TeamLabNetworkConfig();

        Assert.False(config.Enable);
        Assert.Equal(32000, config.PublicUdpPortStart);
        Assert.Equal(32999, config.PublicUdpPortEnd);
        Assert.Equal("10.250.0.0/16", config.InfrastructureTunnelCidr);
    }
}
```

- [ ] **Step 2: Implement config classes**

Add to `Configs.cs`:

```csharp
public class TeamLabNetworkConfig
{
    public bool Enable { get; set; }
    public string PublicEndpointHost { get; set; } = string.Empty;
    public int PublicUdpPortStart { get; set; } = 32000;
    public int PublicUdpPortEnd { get; set; } = 32999;
    public string InfrastructureTunnelCidr { get; set; } = "10.250.0.0/16";
    public int WorkerWireGuardPortStart { get; set; } = 42000;
    public int WorkerWireGuardPortEnd { get; set; } = 42999;
    public bool DryRun { get; set; } = true;
}

public class PublicUdpGatewayConfig
{
    public bool Enable { get; set; }
    public string Provider { get; set; } = "dry-run";
    public string ApplyCommand { get; set; } = string.Empty;
    public string RulesDirectory { get; set; } = "/etc/gzctf/teamlab-udp";
}
```

Register in `AddServiceConfigurations`:

```csharp
builder.AddConfig<TeamLabNetworkConfig>();
builder.AddConfig<PublicUdpGatewayConfig>();
```

Register services in `AddCustomServices`:

```csharp
builder.Services.AddScoped<NodeTunnelService>();
builder.Services.AddScoped<TeamLabPlanService>();
builder.Services.AddScoped<TeamLabDeploymentService>();
builder.Services.AddSingleton<IPublicUdpGatewayProvider, PublicUdpGatewayProvider>();
```

- [ ] **Step 3: Add appsettings template section**

Add disabled-by-default sections:

```json
"TeamLabNetworkConfig": {
  "Enable": false,
  "PublicEndpointHost": "",
  "PublicUdpPortStart": 32000,
  "PublicUdpPortEnd": 32999,
  "InfrastructureTunnelCidr": "10.250.0.0/16",
  "WorkerWireGuardPortStart": 42000,
  "WorkerWireGuardPortEnd": 42999,
  "DryRun": true
},
"PublicUdpGatewayConfig": {
  "Enable": false,
  "Provider": "dry-run",
  "ApplyCommand": "",
  "RulesDirectory": "/etc/gzctf/teamlab-udp"
}
```

- [ ] **Step 4: Test and commit**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabConfigTests"
dotnet build src/GZCTF/GZCTF.csproj --no-restore
```

Commit:

```powershell
git add src/GZCTF/Models/Internal/Configs.cs src/GZCTF/Extensions/Startup/ServicesExtension.cs src/GZCTF/appsettings.Template.json src/GZCTF.Test/UnitTests/TeamLab/TeamLabConfigTests.cs
git commit -m "feat: configure TeamLab network feature flag"
```

## Task 5: Agent TeamLab Command Builders and Dry-Run Endpoints

**Files:**
- Create: `src/GZCTF.Agent/Models/TeamLabModels.cs`
- Create: `src/GZCTF.Agent/Services/TeamLabCommandRunner.cs`
- Create: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
- Create: `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- Modify: `src/GZCTF.Agent/Program.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommandBuilderTests.cs`

- [ ] **Step 1: Write command builder tests**

Create `TeamLabCommandBuilderTests.cs`:

```csharp
using GZCTF.Agent.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class TeamLabCommandBuilderTests
{
    [Fact]
    public void BuildBridgeName_IsShortAndTraceable()
    {
        var name = TeamLabNetworkService.BuildBridgeName(123, "dmz");

        Assert.StartsWith("tl123-", name);
        Assert.True(name.Length <= 15);
    }

    [Fact]
    public void BuildCreateBridgeCommands_DoNotContainUserShellInjection()
    {
        var commands = TeamLabNetworkService.BuildCreateBridgeCommands("tl123-dmz", "10.60.1.0/28");

        Assert.All(commands, command => Assert.DoesNotContain(";", command));
        Assert.Contains(commands, command => command.Contains("ip link add"));
    }
}
```

- [ ] **Step 2: Implement DTOs**

Create `TeamLabModels.cs`:

```csharp
namespace GZCTF.Agent.Models;

public record TeamLabStatusResponse(bool WireGuardInstalled, bool IpCommandAvailable, bool NftAvailable, bool IptablesAvailable);
public record TeamLabDryRunResponse(bool Success, string Message, string[] Commands);
public record TeamLabBridgeRequest(int RuntimeId, string NetworkKey, string Cidr, bool DryRun = true);
public record TeamLabRouterRequest(int RuntimeId, string RouterName, string[] BridgeNames, bool DryRun = true);
public record TeamLabWireGuardRequest(int RuntimeId, int ListenPort, string AddressCidr, string PeerPublicKey, string AllowedIps, bool DryRun = true);
public record TeamLabCleanupRequest(int RuntimeId, string[] ResourceNames, bool DryRun = true);
```

- [ ] **Step 3: Implement command builders with dry-run first**

Create `TeamLabNetworkService.cs` with static command builders and instance methods that return commands without executing when `DryRun` is true.

Key rules:

- Validate names with `^[a-zA-Z0-9_.-]+$`.
- Validate CIDR with `IPNetwork` or strict parser.
- Never concatenate unvalidated user values.
- Keep Linux resource names <= 15 chars when they are interface/bridge names.

- [ ] **Step 4: Add Agent controller**

Create endpoints:

```csharp
[ApiController]
[Route("api/teamlab")]
public class TeamLabController(TeamLabNetworkService service) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken token) => Ok(await service.GetStatusAsync(token));

    [HttpPost("bridges")]
    public async Task<IActionResult> CreateBridge([FromBody] TeamLabBridgeRequest request, CancellationToken token) =>
        Ok(await service.CreateBridgeAsync(request, token));

    [HttpPost("routers")]
    public async Task<IActionResult> CreateRouter([FromBody] TeamLabRouterRequest request, CancellationToken token) =>
        Ok(await service.CreateRouterAsync(request, token));

    [HttpPost("wireguard")]
    public async Task<IActionResult> ConfigureWireGuard([FromBody] TeamLabWireGuardRequest request, CancellationToken token) =>
        Ok(await service.ConfigureWireGuardAsync(request, token));

    [HttpPost("cleanup")]
    public async Task<IActionResult> Cleanup([FromBody] TeamLabCleanupRequest request, CancellationToken token) =>
        Ok(await service.CleanupAsync(request, token));
}
```

- [ ] **Step 5: Register service and test**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabCommandBuilderTests"
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore
```

Commit:

```powershell
git add src/GZCTF.Agent/Models/TeamLabModels.cs src/GZCTF.Agent/Services/TeamLabCommandRunner.cs src/GZCTF.Agent/Services/TeamLabNetworkService.cs src/GZCTF.Agent/Controllers/TeamLabController.cs src/GZCTF.Agent/Program.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabCommandBuilderTests.cs
git commit -m "feat: add TeamLab agent dry-run endpoints"
```

## Task 6: Main Server Agent Client, NodeTunnelService, and Public UDP Provider

**Files:**
- Modify: `src/GZCTF/Services/Fleet/AgentClient.cs`
- Create: `src/GZCTF/Services/TeamLab/NodeTunnelService.cs`
- Create: `src/GZCTF/Services/TeamLab/PublicUdpGatewayProvider.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/PublicUdpGatewayProviderTests.cs`

- [ ] **Step 1: Write UDP provider tests**

Create `PublicUdpGatewayProviderTests.cs`:

```csharp
using GZCTF.Models.Data;
using GZCTF.Models.Internal;
using GZCTF.Services.TeamLab;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GZCTF.Test.UnitTests.TeamLab;

public class PublicUdpGatewayProviderTests
{
    [Fact]
    public async Task DryRunProvider_MarksRuleUnsyncedButReturnsCommands()
    {
        var provider = new PublicUdpGatewayProvider(
            Options.Create(new PublicUdpGatewayConfig { Enable = false, Provider = "dry-run" }),
            NullLogger<PublicUdpGatewayProvider>.Instance);

        var mapping = new TeamLabPublicUdpMapping
        {
            PublicUdpPort = 32001,
            WorkerTunnelIp = "10.250.0.10",
            WorkerWireGuardPort = 42001
        };

        var result = await provider.SyncMappingAsync(mapping, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("32001", string.Join('\n', result.Commands));
        Assert.False(mapping.IsSynced);
    }
}
```

- [ ] **Step 2: Add AgentClient methods**

Add typed methods:

```csharp
public async Task<TeamLabStatusResponse?> GetTeamLabStatusAsync(Guid nodeId, CancellationToken token)
public async Task<TeamLabDryRunResponse?> CreateTeamLabBridgeAsync(Guid nodeId, TeamLabBridgeRequest request, CancellationToken token)
public async Task<TeamLabDryRunResponse?> CreateTeamLabRouterAsync(Guid nodeId, TeamLabRouterRequest request, CancellationToken token)
public async Task<TeamLabDryRunResponse?> ConfigureTeamLabWireGuardAsync(Guid nodeId, TeamLabWireGuardRequest request, CancellationToken token)
public async Task<TeamLabDryRunResponse?> CleanupTeamLabAsync(Guid nodeId, TeamLabCleanupRequest request, CancellationToken token)
```

Use the same `BuildClient` auth path as container/VM APIs.

- [ ] **Step 3: Implement provider abstraction**

Create `PublicUdpGatewayProvider.cs`:

```csharp
public interface IPublicUdpGatewayProvider
{
    Task<PublicUdpGatewaySyncResult> SyncMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token);
    Task<PublicUdpGatewaySyncResult> RemoveMappingAsync(TeamLabPublicUdpMapping mapping, CancellationToken token);
}

public sealed record PublicUdpGatewaySyncResult(bool Success, string Message, string[] Commands);
```

For Phase 0-3 first pass, implement command generation and dry-run. Real command execution must remain behind `PublicUdpGatewayConfig.Enable == true && Provider == "nftables"` or `"iptables"`.

- [ ] **Step 4: Implement NodeTunnelService**

Create service methods:

```csharp
public Task<TeamLabNodeProbeResult> ProbeNodeAsync(WorkerNode node, CancellationToken token)
public Task<TeamLabNodeEnableResult> EnableDryRunAsync(WorkerNode node, CancellationToken token)
public Task<TeamLabNodeEnableResult> MarkHealthyAsync(WorkerNode node, string tunnelIp, CancellationToken token)
```

Phase 0-3 first pass may not create real infra WireGuard keys yet; it must use Agent status endpoint and explicit admin action to mark healthy only after probe. Do not auto-enable all nodes.

- [ ] **Step 5: Test and commit**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~PublicUdpGatewayProviderTests"
dotnet build src/GZCTF/GZCTF.csproj --no-restore
```

Commit:

```powershell
git add src/GZCTF/Services/Fleet/AgentClient.cs src/GZCTF/Services/TeamLab/NodeTunnelService.cs src/GZCTF/Services/TeamLab/PublicUdpGatewayProvider.cs src/GZCTF.Test/UnitTests/TeamLab/PublicUdpGatewayProviderTests.cs
git commit -m "feat: add TeamLab tunnel and UDP gateway services"
```

## Task 7: TeamLab Planning Service

**Files:**
- Create: `src/GZCTF/Services/TeamLab/TeamLabPlanService.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlanServiceTests.cs`

- [ ] **Step 1: Write planning tests**

Create tests for these cases:

```csharp
[Fact]
public void PlanTeamRuntime_RejectsWhenNoHealthyTeamLabNode()
{
    var nodes = new[]
    {
        new WorkerNode { Status = NodeStatus.Online, IsLocal = true, IsSchedulable = true, TeamLabNetworkEnabled = false }
    };

    var result = TeamLabPlanService.SelectNode(nodes);

    Assert.False(result.Success);
    Assert.Contains("TeamLabNetwork", result.Message);
}

[Fact]
public void AllocatePublicUdpPort_UsesConfiguredRangeAndSkipsUsedPorts()
{
    var port = TeamLabPlanService.AllocatePublicUdpPort(32000, 32003, [32000, 32001]);

    Assert.Equal(32002, port);
}
```

- [ ] **Step 2: Implement pure planning helpers first**

Create pure static methods before DB orchestration:

```csharp
public static TeamLabPlanNodeResult SelectNode(IEnumerable<WorkerNode> nodes)
public static int? AllocatePublicUdpPort(int start, int end, IReadOnlySet<int> usedPorts)
public static string BuildRuntimeResourcePrefix(int runtimeId) => $"tl{runtimeId}";
```

The instance method should later load healthy nodes, used UDP ports, and create/advance a `TeamLabRuntime` to `Scheduled` only when all required resources have a plan.

- [ ] **Step 3: Add DB-backed planning method**

Add:

```csharp
public Task<TeamLabPlanResult> PlanRuntimeAsync(int gameId, int teamId, CancellationToken token)
```

Behavior:

- Create `TeamLabRuntime` if missing.
- Refuse if existing runtime is `Running`, `Deploying`, `Probing`, or `Destroying`.
- Select only nodes passing `WeightedScheduler.CanHostTeamLab`.
- Allocate one UDP port from `TeamLabNetworkConfig.PublicUdpPortStart/End` excluding `TeamLabPublicUdpMappings`.
- Write one `TeamLabEvent` for plan success or failure.

- [ ] **Step 4: Test and commit**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabPlanServiceTests"
dotnet build src/GZCTF/GZCTF.csproj --no-restore
```

Commit:

```powershell
git add src/GZCTF/Services/TeamLab/TeamLabPlanService.cs src/GZCTF.Test/UnitTests/TeamLab/TeamLabPlanServiceTests.cs
git commit -m "feat: add TeamLab planning service"
```

## Task 8: Admin APIs for Node Enablement and Runtime Operations

**Files:**
- Modify: `src/GZCTF/Controllers/NodesController.cs`
- Create: `src/GZCTF/Controllers/TeamLabAdminController.cs`
- Modify: `src/GZCTF.Test/UnitTests/Fleet/NodesControllerTests.cs`

- [ ] **Step 1: Add request/response models**

In `NodesController.cs` or a new request model file, define:

```csharp
public class EnableTeamLabNetworkRequest
{
    public bool DryRun { get; set; } = true;
    public string? TunnelIp { get; set; }
}
```

- [ ] **Step 2: Add node enable endpoint**

Add:

```csharp
[HttpPost("{id:guid}/teamlab/enable")]
[RequireAdmin]
public async Task<IActionResult> EnableTeamLabNetwork(Guid id, [FromBody] EnableTeamLabNetworkRequest request)
{
    var node = await _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == id, HttpContext.RequestAborted);
    if (node is null) return NotFound();
    var service = HttpContext.RequestServices.GetRequiredService<NodeTunnelService>();
    var result = request.DryRun
        ? await service.EnableDryRunAsync(node, HttpContext.RequestAborted)
        : await service.MarkHealthyAsync(node, request.TunnelIp ?? string.Empty, HttpContext.RequestAborted);
    return result.Success ? Ok(result) : BadRequest(result);
}
```

- [ ] **Step 3: Add TeamLab admin controller**

Create endpoints:

```csharp
[ApiController]
[Route("api/admin/teamlab/games/{gameId:int}")]
public class TeamLabAdminController(TeamLabPlanService planService, TeamLabDeploymentService deploymentService) : ControllerBase
{
    [HttpPost("teams/{teamId:int}/plan")]
    [RequireAdmin]
    public async Task<IActionResult> Plan(int gameId, int teamId, CancellationToken token) =>
        Ok(await planService.PlanRuntimeAsync(gameId, teamId, token));

    [HttpPost("teams/{teamId:int}/deploy")]
    [RequireAdmin]
    public async Task<IActionResult> Deploy(int gameId, int teamId, CancellationToken token) =>
        Ok(await deploymentService.DeployRuntimeAsync(gameId, teamId, token));

    [HttpPost("teams/{teamId:int}/destroy")]
    [RequireAdmin]
    public async Task<IActionResult> Destroy(int gameId, int teamId, CancellationToken token) =>
        Ok(await deploymentService.DestroyRuntimeAsync(gameId, teamId, token));

    [HttpGet("teams/{teamId:int}/events")]
    [RequireAdmin]
    public async Task<IActionResult> Events(int gameId, int teamId, CancellationToken token) =>
        Ok(await deploymentService.GetEventsAsync(gameId, teamId, token));
}
```

- [ ] **Step 4: Test and commit**

Run:

```powershell
dotnet build src/GZCTF/GZCTF.csproj --no-restore
```

Commit:

```powershell
git add src/GZCTF/Controllers/NodesController.cs src/GZCTF/Controllers/TeamLabAdminController.cs src/GZCTF.Test/UnitTests/Fleet/NodesControllerTests.cs
git commit -m "feat: expose TeamLab admin control APIs"
```

## Task 9: Phase 3 Deployment Service and Data Plane Dry-Run to Real Switch

**Files:**
- Create: `src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs`
- Modify: `src/GZCTF.Agent/Services/TeamLabNetworkService.cs`
- Modify: `src/GZCTF.Agent/Controllers/TeamLabController.cs`
- Create: `src/GZCTF.Test/UnitTests/TeamLab/TeamLabDeploymentServiceTests.cs`

- [ ] **Step 1: Write deployment service tests**

Test state behavior without executing Linux commands:

```csharp
[Fact]
public void DeploymentPlan_UsesTraceableLinuxResourceNames()
{
    var names = TeamLabDeploymentService.BuildResourceNames(runtimeId: 123, networkKeys: ["dmz", "data"]);

    Assert.All(names.Bridges, name => Assert.True(name.Length <= 15));
    Assert.Contains(names.Bridges, name => name.StartsWith("tl123-"));
}
```

- [ ] **Step 2: Implement deployment orchestration with dry-run default**

`DeployRuntimeAsync` should:

1. Load runtime and mapping.
2. Refuse if runtime is already `Running`, `Deploying`, `Probing`, or `Destroying`.
3. Transition to `Deploying`.
4. Create one entry LabNetwork bridge and one internal LabNetwork bridge for Phase 3 smoke.
5. Create one router namespace connected to both bridges.
6. Configure WorkerNode local WireGuard endpoint through Agent dry-run or real mode based on config.
7. Sync public UDP mapping through provider dry-run or real mode.
8. Transition to `Probing`.
9. Run Agent probe.
10. Transition to `Running` only if probe succeeds.

On any failure, write `TeamLabEvent`, set `LastError`, and transition to `Failed` or `CleanupPending`.

- [ ] **Step 3: Add real execution guard**

Agent commands must execute only when both conditions hold:

```csharp
TeamLabNetworkConfig.Enable == true && request.DryRun == false
```

Otherwise return command list and success=false or success=true according to dry-run semantics; do not mutate OS state.

- [ ] **Step 4: Test and commit**

Run:

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLabDeploymentServiceTests|FullyQualifiedName~TeamLabCommandBuilderTests"
dotnet build src/GZCTF/GZCTF.csproj --no-restore
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore
```

Commit:

```powershell
git add src/GZCTF/Services/TeamLab/TeamLabDeploymentService.cs src/GZCTF.Agent/Services/TeamLabNetworkService.cs src/GZCTF.Agent/Controllers/TeamLabController.cs src/GZCTF.Test/UnitTests/TeamLab
git commit -m "feat: add TeamLab phase 3 deployment orchestration"
```

## Task 10: Frontend Node TeamLab Status and Enable Action

**Files:**
- Modify: `src/GZCTF/ClientApp/src/components/admin/NodeCard.tsx`
- Modify: `src/GZCTF/ClientApp/src/pages/admin/nodes/Index.tsx`
- Create: `src/GZCTF/ClientApp/src/utils/TeamLabApi.ts`

- [ ] **Step 1: Extend NodeInfo type**

Add fields:

```ts
teamLabNetworkEnabled?: boolean
teamLabTunnelStatus?: string | number
teamLabTunnelIp?: string | null
teamLabTunnelLastHandshake?: string | null
teamLabTunnelLastError?: string | null
teamLabTunnelConfigVersion?: number
canHostTeamLab?: boolean
```

- [ ] **Step 2: Add API helper**

Create `TeamLabApi.ts`:

```ts
export async function enableTeamLabNetwork(nodeId: string, dryRun = true, tunnelIp?: string) {
  const res = await fetch(`/api/v1/nodes/${nodeId}/teamlab/enable`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ dryRun, tunnelIp: tunnelIp ?? null }),
  })
  const body = await res.json().catch(() => ({}))
  if (!res.ok) throw new Error(body.message || '启用 VPN 靶场网络失败')
  return body
}
```

- [ ] **Step 3: Display TeamLab status on NodeCard**

Add a distinct status line, not mixed into Docker port pool:

```tsx
{node.teamLabNetworkEnabled && (
  <Text size="xs" fw={700} c={node.canHostTeamLab ? 'teal' : 'orange'}>
    VPN 靶场网络：{node.canHostTeamLab ? '可调度' : node.teamLabTunnelStatus ?? '未就绪'}
  </Text>
)}
```

- [ ] **Step 4: Add enable action in nodes page**

In `NodeResourcePanel` or selected-node header, add a button:

```tsx
<Button variant="default" onClick={() => enableTeamLabNetwork(node.id, true).then(loadResources)}>
  检查 VPN 靶场网络
</Button>
```

Only show real enable/mark healthy button when backend config exposes non-dry-run capability in a later response. Phase 0-3 UI should make dry-run status explicit.

- [ ] **Step 5: Run frontend checks and commit**

Run:

```powershell
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp build
```

Commit:

```powershell
git add src/GZCTF/ClientApp/src/components/admin/NodeCard.tsx src/GZCTF/ClientApp/src/pages/admin/nodes/Index.tsx src/GZCTF/ClientApp/src/utils/TeamLabApi.ts
git commit -m "feat: show TeamLab node network status"
```

## Task 11: Phase 0-3 Operator Runbook

**Files:**
- Create: `docs/teamlab-phase0-3-operator-runbook.md`

- [ ] **Step 1: Write runbook**

Create a runbook with these sections:

```markdown
# TeamLab Phase 0-3 Operator Runbook

## Feature Flags

- TeamLabNetworkConfig:Enable=false keeps all data-plane mutation disabled.
- TeamLabNetworkConfig:DryRun=true returns command plans but does not mutate WorkerNode.
- PublicUdpGatewayConfig:Enable=false prevents public UDP rule changes.

## Safe Local Validation

1. Build backend and Agent.
2. Start platform with TeamLab flags disabled.
3. Verify ordinary Docker and VM flows still work.
4. Enable dry-run status check on one node.
5. Confirm node reports commands but no OS resources are created.

## Real Data Plane Validation

1. Enable TeamLabNetworkConfig:Enable=true on an isolated WorkerNode.
2. Keep public UDP provider in dry-run until bridge/router resources pass local probes.
3. Deploy two test TeamLab runtimes on the same WorkerNode.
4. Verify same-team bridge/router reachability.
5. Verify cross-team isolation.
6. Destroy both runtimes and verify no bridge, namespace, WireGuard peer, route, veth, or config files remain.

## Rollback

1. Disable TeamLabNetworkConfig:Enable.
2. Disable node TeamLabNetworkEnabled in node management.
3. Destroy failed TeamLab runtimes.
4. Keep ordinary Docker TCP proxy and VM management untouched.
```

- [ ] **Step 2: Commit runbook**

```powershell
git add docs/teamlab-phase0-3-operator-runbook.md
git commit -m "docs: add TeamLab phase 0-3 runbook"
```

## Task 12: Final Verification Gate for Phase 0-3

**Files:**
- No planned code changes.

- [ ] **Step 1: Run targeted tests**

```powershell
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --filter "FullyQualifiedName~TeamLab|FullyQualifiedName~Fleet"
```

Expected: PASS.

- [ ] **Step 2: Run backend builds**

```powershell
dotnet build src/GZCTF/GZCTF.csproj --no-restore
dotnet build src/GZCTF.Agent/GZCTF.Agent.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 3: Run frontend checks**

```powershell
pnpm --dir src/GZCTF/ClientApp check
pnpm --dir src/GZCTF/ClientApp build
```

Expected: PASS.

- [ ] **Step 4: Run static cleanup checks**

```powershell
git diff --check
rg -n "端口级 ACL|拓扑迷雾|攻击图|公网目标|入口目标" src/GZCTF src/GZCTF.Agent src/GZCTF/ClientApp/src docs/teamlab-phase0-3-operator-runbook.md
```

Expected: `git diff --check` passes. `rg` may find historical docs or existing old player pages, but new TeamLab UI/API must not claim endpoint-target or port-level ACL behavior.

- [ ] **Step 5: Commit verification note if needed**

If verification required a doc update, commit it:

```powershell
git add docs/teamlab-phase0-3-operator-runbook.md docs/teamlab-phase0-baseline.md
git commit -m "docs: record TeamLab phase 0-3 verification"
```

If no doc update was needed, do not create an empty commit.

## Self-Review Checklist

- Phase 0 covered by Task 0 and Task 12.
- Phase 1 covered by Tasks 1-4, 6-8, and 10.
- Phase 2 covered by Tasks 5-6 and the runbook validation gates.
- Phase 3 covered by Task 9 and the real data-plane validation section.
- Ordinary Docker TCP proxy remains separate from TeamLab UDP; no task changes `PortAllocationService` or `NginxSyncService` behavior.
- VM/KVM default path remains untouched in Phase 0-3; VM bridge integration is explicitly outside this plan.
- All OS-mutating Agent operations are gated by `Enable=true` and non-dry-run request.
- Public UDP rule mutation is gated by `PublicUdpGatewayConfig.Enable=true`.
- Frontend only exposes dry-run or actual backend state; it does not present unimplemented TeamLab deployment as production ready.
