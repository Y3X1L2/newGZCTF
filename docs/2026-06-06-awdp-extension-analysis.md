# AWDP 扩展接口对接分析文档

> 版本：v1.0
> 日期：2026-06-06
> 目标：在现有 AWD 模式基础上，新增 AWDP 比赛形式，最小侵入性扩展

---

## 一、AWDP 核心差异总结

| 维度 | AWD (现有) | AWDP (新增) |
|------|-----------|-------------|
| **队伍间关系** | 互为攻防，直接攻击其他队伍靶机 | 队伍间互不干扰，独立环境 |
| **攻击方式** | 直接攻击其他队伍服务获取 flag | 类似解题赛，对平台提供的题目环境发起攻击 |
| **防御方式** | 无修补机制 | 上传修补包 (tar.gz)，平台自动验证 |
| **修补验证** | 无 | Checker(功能) + Exp(漏洞) 双重验证 |
| **计分维度** | 攻击分 + SLA - 防守失分 (零和) | 攻击分 + 修补分 + SLA - 异常扣分 (非零和) |
| **轮次内阶段** | 单一阶段 (攻击) | 双阶段 (攻击 + 修补) |
| **重置机制** | 管理员手动重置 | 选手自助重置 (有限次数) |
| **一键恢复** | 无 | 修补异常时可一键恢复 (有限次数) |
| **题目状态** | UP/DOWN/MUMBLE | 六种状态 (未攻击/已攻击/未防御/已防御/防御异常/防御失败) |

---

## 二、现有 AWD 架构完整梳理

### 2.1 后端数据模型

#### GameType 枚举 (`src/GZCTF/Utils/Enums.cs:191`)

```csharp
[JsonConverter(typeof(JsonStringEnumConverter<GameType>))]
public enum GameType : byte
{
    [Description("Jeopardy")]  Jeopardy = 0,
    [Description("AWD")]       AWD = 1,
    [Description("Theory")]    Theory = 2,
    [Description("Mixed")]     Mixed = 3
    // 需新增: [Description("AWDP")] AWDP = 4
}
```

#### Game 实体 (`src/GZCTF/Models/Data/Game.cs`)

核心字段：`Id`, `Title`, `GameType`, `StartTimeUtc`, `EndTimeUtc`, `ContainerCountLimit`, `PracticeMode`

导航属性：`GameEvents`, `GameNotices`, `Submissions`, `Challenges`, `Participations`, `Teams`, `Divisions`

#### AWD 专用模型 (5 个实体)

| 模型 | 文件 | 核心字段 | 关联 |
|------|------|----------|------|
| **AwdService** | `Models/Data/AwdService.cs` | Id, GameId, Name, ImageName, ExposePort, CheckerScript, CheckerEntrypoint, OriginalScore, AttackPoints, SlaPoints, MaxAttackPerRound, RoundDurationMinutes, TotalRounds | Game, Instances, Rounds |
| **AwdServiceInstance** | `Models/Data/AwdServiceInstance.cs` | Id, ServiceId, TeamId, ContainerId, NetworkName, IsRunning, CreatedAt | Service, Team, Container |
| **AwdRound** | `Models/Data/AwdRound.cs` | Id, GameId, RoundNumber, StartTime, EndTime, Status (Preparing/Running/Finished) | Game, Flags, CheckerTasks |
| **AwdFlag** | `Models/Data/AwdFlag.cs` | Id, RoundId, ServiceId, TeamId, FlagValue, IsSubmitted, FirstSubmittedAt | Round, Service, Team |
| **AwdCheckerTask** | `Models/Data/AwdCheckerTask.cs` | Id, RoundId, ServiceId, TeamId, Status (OK/Mumble/Down/Corrupt), Message, ExecutedAt | Round, Service, Team |

#### AppDbContext 注册 (`Models/AppDbContext.cs:55-59`)

```csharp
public DbSet<AwdService> AwdServices { get; set; }
public DbSet<AwdServiceInstance> AwdServiceInstances { get; set; }
public DbSet<AwdRound> AwdRounds { get; set; }
public DbSet<AwdFlag> AwdFlags { get; set; }
public DbSet<AwdCheckerTask> AwdCheckerTasks { get; set; }
```

### 2.2 后端控制器

#### AwdAdminController (`Controllers/AwdAdminController.cs`)

- 路由前缀：`api/admin/awd`
- 权限：`[RequireAdmin]`
- 依赖注入：`AppDbContext`, `IAwdRepository`, `AwdInstanceService`, `AwdRoundService`, `IGameRepository`

| HTTP | 路由 | 方法 | 说明 |
|------|------|------|------|
| GET | `games/{gameId}/services` | GetServices | 获取服务列表 |
| POST | `games/{gameId}/services` | CreateService | 创建服务 (检查 GameType) |
| PUT | `services/{serviceId}` | UpdateService | 更新服务 |
| DELETE | `services/{serviceId}` | DeleteService | 删除服务 |
| POST | `games/{gameId}/start` | StartGame | 启动比赛 (检查 GameType) |
| POST | `games/{gameId}/stop` | StopGame | 停止比赛 |
| POST | `instances/{instanceId}/reset` | ResetInstance | 重置实例 |
| GET | `games/{gameId}/instances` | GetInstances | 获取实例列表 |
| GET | `games/{gameId}/status` | GetGameStatus | 获取比赛状态 |

**关键检查点**：`CreateService` 和 `StartGame` 都检查 `game.GameType is not GameType.AWD and not GameType.Mixed`，AWDP 需要加入此检查。

#### AwdPlayerController (`Controllers/AwdPlayerController.cs`)

- 路由前缀：`api/awd`
- 权限：`[RequireUser]`
- 依赖注入：`AppDbContext`, `IAwdRepository`, `AwdScoreService`, `AwdRoundService`, `IGameRepository`, `IParticipationRepository`, `UserManager<UserInfo>`

| HTTP | 路由 | 方法 | 说明 |
|------|------|------|------|
| GET | `games/{gameId}/status` | GetGameStatus | 获取比赛状态 |
| GET | `games/{gameId}/instances` | GetMyInstances | 获取己方实例 |
| POST | `games/{gameId}/submit` | SubmitFlag | 提交 Flag (检查 GameType) |
| GET | `games/{gameId}/scoreboard` | GetScoreboard | 获取排行榜 |
| GET | `games/{gameId}/attack-logs` | GetAttackLogs | 获取攻击日志 |

### 2.3 后端服务层

#### AwdRoundService (`Services/AwdRoundService.cs`) — Singleton + IHostedService

**游戏主循环** (`RunGameLoop`):
1. 创建实例 (`AwdInstanceService.CreateInstancesForGame`)
2. 循环每轮：
   - 创建 Round
   - 生成 Flag (每队每服务一个)
   - 注入 Flag (重置容器)
   - 广播 SignalR (`MonitorHub`)
   - 运行 Checker (`AwdCheckerService.RunCheckerForRound`)
   - 等待轮次时长
   - 计算分数 (`AwdScoreService.CalculateRoundScores`)

#### AwdInstanceService (`Services/AwdInstanceService.cs`) — Scoped

- `CreateInstancesForGame(Game)` — 为所有参赛队伍创建容器实例 + 隔离网络
- `DestroyInstancesForGame(int gameId)` — 销毁所有实例
- `ResetInstance(int instanceId, string? newFlag)` — 销毁旧容器并重建

#### AwdCheckerService (`Services/AwdCheckerService.cs`) — Scoped

- `RunCheckerForRound(...)` — 对每轮每个服务每个队伍执行 Checker 脚本
- `ExecuteChecker(...)` — 通过 `Process.Start` 调用 Python，30 秒超时

#### AwdScoreService (`Services/AwdScoreService.cs`) — Scoped

- `CalculateRoundScores(...)` — SLA 分 + 防守扣分，写入 Submission
- `RecordFlagSubmission(...)` — 记录攻击得分，标记 Flag 已提交，创建 FirstSolve，发布 GameEvent

### 2.4 后端仓库层

#### IAwdRepository (`Repositories/Interface/IAwdRepository.cs`)

```csharp
public interface IAwdRepository : IRepository
{
    Task<AwdService?> GetService(int serviceId, CancellationToken token = default);
    Task<AwdService[]> GetServicesByGame(int gameId, CancellationToken token = default);
    Task<AwdServiceInstance?> GetInstance(int instanceId, CancellationToken token = default);
    Task<AwdServiceInstance[]> GetInstancesByGame(int gameId, CancellationToken token = default);
    Task<AwdRound?> GetCurrentRound(int gameId, CancellationToken token = default);
    Task<AwdRound[]> GetRoundsByGame(int gameId, CancellationToken token = default);
    Task<AwdFlag?> GetFlag(int roundId, int serviceId, int teamId, CancellationToken token = default);
    Task<AwdFlag?> GetFlagByValue(string flagValue, CancellationToken token = default);
    Task<AwdCheckerTask[]> GetCheckerTasksByRound(int roundId, CancellationToken token = default);
    Task UpdateFlagSubmitted(int flagId, CancellationToken token = default);
    Task CreateRound(AwdRound round, CancellationToken token = default);
    Task CreateFlags(IEnumerable<AwdFlag> flags, CancellationToken token = default);
    Task CreateCheckerTasks(IEnumerable<AwdCheckerTask> tasks, CancellationToken token = default);
}
```

### 2.5 请求/响应模型 (`Models/Request/Game/AwdServiceModels.cs`)

- `AwdServiceCreateModel` / `AwdServiceUpdateModel` — 服务 CRUD
- `AwdServiceViewModel` — 服务视图
- `AwdSubmitModel` — Flag 提交 (flag, targetTeamId?, serviceId?)
- `AwdGameStatusModel` — 比赛状态 (gameId, currentRound, roundStartTime, roundDurationMinutes, status)
- `TeamServiceStatus` — 队伍服务状态
- `AwdScoreboardItem` — 排行榜条目 (rank, teamId, teamName, ctfScore, awdScore, totalScore, attackScore, slaScore, defenseLost)
- `AwdAttackLogItem` — 攻击日志

### 2.6 DI 注册 (`Program.cs:48-53`)

```csharp
builder.Services.AddScoped<IAwdRepository, AwdRepository>();
builder.Services.AddScoped<AwdInstanceService>();
builder.Services.AddScoped<AwdCheckerService>();
builder.Services.AddScoped<AwdScoreService>();
builder.Services.AddSingleton<AwdRoundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AwdRoundService>());
```

### 2.7 SignalR 实时通信

- Hub: `MonitorHub` (`Hubs/MonitorHub.cs`)
- 接口: `IMonitorClient` (`Hubs/Clients/IMonitorClient.cs`)
- 方法: `ReceivedAwdRoundChange(AwdGameStatusModel)`, `ReceivedAwdServiceStatusChange(AwdServiceStatusModel)`

---

## 三、前端架构完整梳理

### 3.1 路由系统

使用 `vite-plugin-pages` 文件系统路由，路由从 `src/pages/` 目录自动生成。

**竞赛相关路由：**

| 路由 | 文件 | 说明 |
|------|------|------|
| `/games` | `pages/games/Index.tsx` | 竞赛列表大厅 |
| `/games/:id` | `pages/games/[id]/Index.tsx` | 竞赛详情 |
| `/games/:id/challenges` | `pages/games/[id]/Challenges.tsx` | Jeopardy 题目面板 |
| `/games/:id/awd` | `pages/games/[id]/Awd.tsx` | AWD 选手面板 |
| `/games/:id/scoreboard` | `pages/games/[id]/Scoreboard.tsx` | 排行榜 |
| `/admin/games/:id/info` | `pages/admin/games/[id]/Info.tsx` | 竞赛信息编辑 |
| `/admin/games/:id/awd-services` | `pages/admin/games/[id]/AwdServices.tsx` | AWD 服务管理 |

### 3.2 关键组件

#### WithGameTab (`components/WithGameTab.tsx`) — 选手侧 Tab 导航

```typescript
const isAwdGame = game?.gameType === GameType.AWD || game?.gameType === GameType.Mixed
// AWD tab 仅当 isAwdGame 时显示
```

**需要修改**：添加 `GameType.AWDP` 到 `isAwdGame` 判断。

#### WithGameEditTab (`components/admin/WithGameEditTab.tsx`) — 管理侧 Tab 导航

```typescript
const isAwdGame = game?.gameType === GameType.AWD || game?.gameType === GameType.Mixed
// awd-services tab 仅当 isAwdGame 时显示
```

**需要修改**：添加 `GameType.AWDP` 到 `isAwdGame` 判断。

#### Info.tsx (`pages/admin/games/[id]/Info.tsx`) — GameType 选择器

```typescript
data={[
  { value: GameType.Jeopardy, label: 'Jeopardy' },
  { value: GameType.AWD, label: 'AWD' },
  { value: GameType.Theory, label: 'Theory' },
  { value: GameType.Mixed, label: 'Mixed' },
]}
```

**需要修改**：添加 `{ value: GameType.AWDP, label: 'AWDP' }` 选项。

### 3.3 API 层

#### AwdApi.ts (`Api/AwdApi.ts`)

包含 `awdAdminApi` 和 `awdPlayerApi` 两组 API，共 14 个接口方法。

#### 主 API (`Api.ts`，Swagger 自动生成)

包含 `api.game`、`api.edit`、`api.admin` 等命名空间，覆盖竞赛 CRUD、Flag 提交、容器管理等。

### 3.4 前端类型定义 (`Api.ts`)

```typescript
export enum GameType {
  Jeopardy = "Jeopardy",
  AWD = "AWD",
  Theory = "Theory",
  Mixed = "Mixed",
  // 需新增: AWDP = "AWDP"
}
```

---

## 四、AWDP 扩展改造清单

### 4.1 需要新增的内容

#### 4.1.1 后端新增模型

| 模型 | 用途 | 核心字段 |
|------|------|----------|
| **AwdpPatchSubmission** | 修补包提交记录 | Id, RoundId, ServiceId, TeamId, PatchFileHash, SubmittedAt, CheckerResult, ExpResult, FinalStatus |
| **AwdpResetRecord** | 重置记录 | Id, ServiceId, TeamId, ResetAt, ResetType (Player/Admin) |
| **AwdpRecoveryRecord** | 一键恢复记录 | Id, ServiceId, TeamId, RecoveryAt |

**说明**：修补包验证结果可直接扩展 `AwdCheckerTask`，新增 `AwdpPatchSubmission` 记录修补包提交和验证的完整流程。

#### 4.1.2 后端新增枚举

```csharp
// 修补包验证状态
public enum AwdpPatchStatus
{
    Pending,        // 等待验证
    CheckerFailed,  // Checker 失败 (服务异常)
    ExpSucceeded,   // Exp 成功 (漏洞未修补)
    ExpFailed,      // Exp 失败 (修补成功)
    Timeout         // 验证超时
}

// AWDP 题目状态 (选手视角)
public enum AwdpChallengeStatus
{
    Unattacked,     // 未攻击
    Attacked,       // 已攻击
    Undefended,     // 未防御
    Defended,       // 已防御
    DefenseAbnormal,// 防御异常
    DefenseFailed   // 防御失败
}
```

#### 4.1.3 后端新增服务

| 服务 | 职责 | 复用程度 |
|------|------|----------|
| **AwdpPatchService** | 修补包上传、解压、执行 update.sh、运行 Checker + Exp 验证 | 部分复用 AwdCheckerService |
| **AwdpScoreService** | AWDP 计分 (攻击分 + 修补分 + SLA - 异常扣分) | 部分复用 AwdScoreService |
| **AwdpRoundService** | AWDP 轮次管理 (双阶段: 攻击 + 修补) | 大量复用 AwdRoundService |

#### 4.1.4 后端新增控制器

| 控制器 | 路由 | 说明 |
|--------|------|------|
| **AwdpAdminController** | `api/admin/awdp` | AWDP 管理 API (复用大部分 AwdAdminController 逻辑) |
| **AwdpPlayerController** | `api/awdp` | AWDP 选手 API (新增修补包上传、重置、一键恢复等) |

#### 4.1.5 前端新增页面/组件

| 组件 | 路由 | 说明 |
|------|------|------|
| **Awdp.tsx** | `/games/:id/awdp` | AWDP 选手面板 (在 Awd.tsx 基础上增加修补相关 UI) |
| **AwdpServices.tsx** | `/admin/games/:id/awdp-services` | AWDP 管理面板 (增加修补包管理、Exp 脚本配置等) |
| **AwdpPatchUpload** | 组件 | 修补包上传组件 |
| **AwdpChallengeStatus** | 组件 | AWDP 六种题目状态展示组件 |
| **AwdpScoreboard** | 组件 | AWDP 专用排行榜 (增加修补分列) |

### 4.2 需要修改的内容 (最小侵入)

#### 4.2.1 后端修改

| 文件 | 修改内容 | 影响范围 |
|------|----------|----------|
| `Utils/Enums.cs` | GameType 枚举新增 `AWDP = 4` | 低 — 纯新增值 |
| `Models/Data/AwdService.cs` | 新增 `ExpScript`, `ExpEntrypoint`, `PatchPoints`, `MaxResetCount`, `MaxRecoveryCount`, `ServiceAbnormalPenalty` 字段 | 低 — 可选字段，不影响现有 AWD |
| `Models/AppDbContext.cs` | 注册新 DbSet (`AwdpPatchSubmissions`, `AwdpResetRecords`, `AwdpRecoveryRecords`) | 低 — 纯新增 |
| `Models/Request/Game/AwdServiceModels.cs` | 新增 AWDP 专用请求/响应模型 | 低 — 纯新增 |
| `Controllers/AwdAdminController.cs` | GameType 检查加入 `AWDP` | 极低 — 一行修改 |
| `Controllers/AwdPlayerController.cs` | GameType 检查加入 `AWDP` | 极低 — 一行修改 |
| `Hubs/Clients/IMonitorClient.cs` | 新增 AWDP 专用 SignalR 方法 | 低 — 纯新增 |
| `Program.cs` | 注册新服务 | 低 — 纯新增 |

#### 4.2.2 前端修改

| 文件 | 修改内容 | 影响范围 |
|------|----------|----------|
| `Api.ts` | GameType 枚举新增 `AWDP = "AWDP"` | 极低 — 纯新增值 |
| `Api/AwdApi.ts` | 新增 AWDP 专用 API 方法和类型 | 低 — 纯新增 |
| `components/WithGameTab.tsx` | `isAwdGame` 判断加入 `GameType.AWDP` | 极低 — 一行修改 |
| `components/admin/WithGameEditTab.tsx` | `isAwdGame` 判断加入 `GameType.AWDP` | 极低 — 一行修改 |
| `pages/admin/games/[id]/Info.tsx` | GameType 选择器加入 AWDP 选项 | 极低 — 一行修改 |

---

## 五、AWDP 与 AWD 的复用分析

### 5.1 可直接复用的部分 (无需修改)

| 组件 | 复用方式 |
|------|----------|
| **AwdServiceInstance** 模型 | AWDP 每队每服务仍需独立容器实例 |
| **AwdRound** 模型 | AWDP 仍使用轮次制 |
| **AwdFlag** 模型 | AWDP 仍需每轮每队每服务生成 Flag |
| **AwdCheckerTask** 模型 | AWDP 的 Checker 验证可复用 |
| **AwdInstanceService** | 容器创建/销毁/重置逻辑完全复用 |
| **ContainerOrchestrator** | 网络隔离逻辑完全复用 |
| **IContainerManager** | 容器管理接口完全复用 |
| **IGameRepository** | 竞赛基础 CRUD 完全复用 |
| **IParticipationRepository** | 参与管理完全复用 |
| **SignalR MonitorHub** | 实时推送基础设施完全复用 |

### 5.2 需要扩展的部分 (在现有基础上添加)

| 组件 | 扩展内容 |
|------|----------|
| **AwdService** 模型 | 新增 Exp 脚本、修补分、重置次数等字段 |
| **AwdRoundService** | 轮次内增加修补阶段 (攻击阶段结束后进入修补阶段) |
| **AwdCheckerService** | 增加 Exp 执行能力 (验证漏洞是否被修补) |
| **AwdScoreService** | 增加修补分计算、异常扣分逻辑 |
| **AwdAdminController** | 增加修补包管理、Exp 脚本配置等端点 |
| **AwdPlayerController** | 增加修补包上传、自助重置、一键恢复等端点 |

### 5.3 需要新增的部分

| 组件 | 说明 |
|------|------|
| **AwdpPatchService** | 修补包处理核心服务 |
| **AwdpPatchSubmission** 模型 | 修补包提交记录 |
| **AwdpResetRecord** 模型 | 重置记录 |
| **AwdpRecoveryRecord** 模型 | 恢复记录 |
| **Awdp.tsx** 页面 | AWDP 选手面板 |
| **AwdpServices.tsx** 页面 | AWDP 管理面板 |
| **AwdpPatchUpload** 组件 | 修补包上传组件 |

---

## 六、AWDP 计分逻辑设计

### 6.1 计分维度

| 维度 | 计算方式 | 写入方式 |
|------|----------|----------|
| **攻击分** | 提交正确 flag 获得 `AttackPoints` | 复用 AwdScoreService.RecordFlagSubmission |
| **修补分** | 修补包验证通过 (Checker OK + Exp 失败) 获得 `PatchPoints` | 新增 AwdpScoreService.RecordPatchSuccess |
| **SLA 分** | 每轮 Checker OK 获得 `SlaPoints` | 复用 AwdScoreService.CalculateRoundScores |
| **异常扣分** | 修补导致服务异常 (Checker 失败) 扣除 `ServiceAbnormalPenalty` | 新增 AwdpScoreService.RecordPatchFailure |

### 6.2 总分公式

```
总分 = 攻击分 + 修补分 + SLA 分 - 异常扣分
```

所有分数通过 `Submission` 表记录，`ChallengeId` 指向 `AwdService.Id`，通过 `Answer` 字段前缀区分类型：
- `ATK-{ServiceName}-R{RoundNum}` — 攻击分
- `PATCH-{ServiceName}-R{RoundNum}` — 修补分
- `SLA-{ServiceName}-R{RoundNum}` — SLA 分
- `PENALTY-{ServiceName}-R{RoundNum}` — 异常扣分

---

## 七、AWDP 轮次流程设计

### 7.1 单轮流程

```
轮次开始
  │
  ├─ 1. 生成 Flag + 注入容器 (复用 AwdRoundService)
  │
  ├─ 2. 攻击阶段 (Duration: AttackPhaseMinutes)
  │    ├─ 选手访问靶机环境
  │    ├─ 提交 flag 获取攻击分
  │    └─ 广播 SignalR 状态更新
  │
  ├─ 3. 修补阶段 (Duration: PatchPhaseMinutes)
  │    ├─ 选手下载题目附件/源码
  │    ├─ 本地修补漏洞
  │    ├─ 制作修补包 (update.tar.gz)
  │    ├─ 上传修补包
  │    ├─ 点击"申请判定"
  │    └─ 平台验证:
  │         ├─ 创建临时环境
  │         ├─ 解压执行 update.sh
  │         ├─ 运行 Checker → 失败则扣异常分
  │         └─ 运行 Exp → 成功则修补失败，失败则修补成功
  │
  └─ 4. 轮次结算
       ├─ 计算攻击分
       ├─ 计算修补分
       ├─ 计算 SLA 分
       └─ 计算异常扣分
```

### 7.2 AwdService 新增字段

```csharp
// 修补相关
public string? ExpScript { get; set; }           // Exp 脚本内容
public string? ExpEntrypoint { get; set; } = "python exp.py";  // Exp 入口
public int PatchPoints { get; set; } = 100;      // 修补成功得分
public int ServiceAbnormalPenalty { get; set; } = 200;  // 服务异常扣分

// 重置/恢复相关
public int MaxResetCount { get; set; } = 10;     // 最大重置次数
public int MaxRecoveryCount { get; set; } = 5;   // 最大一键恢复次数

// 轮次阶段
public int AttackPhaseMinutes { get; set; } = 15; // 攻击阶段时长
public int PatchPhaseMinutes { get; set; } = 10;  // 修补阶段时长
```

---

## 八、关键接口对接点清单

### 8.1 后端接口对接

| 对接点 | 文件:行号 | 当前逻辑 | AWDP 改造 |
|--------|-----------|----------|-----------|
| GameType 检查 | `AwdAdminController.cs:79` | `not AWD and not Mixed` | 加入 `not AWDP` |
| GameType 检查 | `AwdAdminController.cs:197` | `not AWD and not Mixed` | 加入 `not AWDP` |
| GameType 检查 | `AwdPlayerController.cs:125` | `not AWD and not Mixed` | 加入 `not AWDP` |
| 轮次循环 | `AwdRoundService.cs:103-227` | 单阶段循环 | 改为双阶段 (攻击+修补) |
| 分数计算 | `AwdScoreService.cs:13-73` | SLA + 防守扣分 | 加入修补分 + 异常扣分 |
| Checker 执行 | `AwdCheckerService.cs:44-91` | 仅执行 Checker | 加入 Exp 执行 |
| 容器重置 | `AwdInstanceService.cs:91-119` | 销毁+重建 | 复用，增加修补包注入 |

### 8.2 前端接口对接

| 对接点 | 文件:行号 | 当前逻辑 | AWDP 改造 |
|--------|-----------|----------|-----------|
| GameType 枚举 | `Api.ts:96-101` | 4 个值 | 加入 `AWDP = "AWDP"` |
| 选手 Tab | `WithGameTab.tsx:65` | `isAwdGame` 判断 | 加入 `GameType.AWDP` |
| 管理 Tab | `WithGameEditTab.tsx:46` | `isAwdGame` 判断 | 加入 `GameType.AWDP` |
| GameType 选择器 | `Info.tsx:241-246` | 4 个选项 | 加入 AWDP 选项 |
| AWD API | `AwdApi.ts` | 14 个接口 | 新增 AWDP 专用接口 |

---

## 九、数据库迁移计划

### 9.1 新增表

```sql
-- 修补包提交记录
CREATE TABLE "AwdpPatchSubmissions" (
    "Id" SERIAL PRIMARY KEY,
    "RoundId" INTEGER NOT NULL REFERENCES "AwdRounds"("Id"),
    "ServiceId" INTEGER NOT NULL REFERENCES "AwdServices"("Id"),
    "TeamId" INTEGER NOT NULL REFERENCES "Teams"("Id"),
    "PatchFileHash" VARCHAR(128) NOT NULL,
    "SubmittedAt" TIMESTAMPTZ NOT NULL,
    "CheckerResult" INTEGER NOT NULL DEFAULT 0,
    "ExpResult" INTEGER NOT NULL DEFAULT 0,
    "FinalStatus" INTEGER NOT NULL DEFAULT 0,
    "Message" TEXT
);

-- 重置记录
CREATE TABLE "AwdpResetRecords" (
    "Id" SERIAL PRIMARY KEY,
    "ServiceId" INTEGER NOT NULL REFERENCES "AwdServices"("Id"),
    "TeamId" INTEGER NOT NULL REFERENCES "Teams"("Id"),
    "ResetAt" TIMESTAMPTZ NOT NULL,
    "ResetType" INTEGER NOT NULL DEFAULT 0
);

-- 一键恢复记录
CREATE TABLE "AwdpRecoveryRecords" (
    "Id" SERIAL PRIMARY KEY,
    "ServiceId" INTEGER NOT NULL REFERENCES "AwdServices"("Id"),
    "TeamId" INTEGER NOT NULL REFERENCES "Teams"("Id"),
    "RecoveryAt" TIMESTAMPTZ NOT NULL
);
```

### 9.2 修改表

```sql
-- AwdService 新增字段
ALTER TABLE "AwdServices" ADD COLUMN "ExpScript" TEXT;
ALTER TABLE "AwdServices" ADD COLUMN "ExpEntrypoint" VARCHAR(256) DEFAULT 'python exp.py';
ALTER TABLE "AwdServices" ADD COLUMN "PatchPoints" INTEGER DEFAULT 100;
ALTER TABLE "AwdServices" ADD COLUMN "ServiceAbnormalPenalty" INTEGER DEFAULT 200;
ALTER TABLE "AwdServices" ADD COLUMN "MaxResetCount" INTEGER DEFAULT 10;
ALTER TABLE "AwdServices" ADD COLUMN "MaxRecoveryCount" INTEGER DEFAULT 5;
ALTER TABLE "AwdServices" ADD COLUMN "AttackPhaseMinutes" INTEGER DEFAULT 15;
ALTER TABLE "AwdServices" ADD COLUMN "PatchPhaseMinutes" INTEGER DEFAULT 10;
```

---

## 十、实施优先级建议

### P0 — 核心框架 (必须)

1. GameType 枚举新增 AWDP
2. AwdService 模型扩展 (新增字段)
3. 前端 GameType 判断修改 (3 处)
4. AwdpRoundService 轮次双阶段循环
5. AwdpPatchService 修补包验证核心逻辑

### P1 — 核心功能 (必须)

6. AwdpAdminController 管理 API
7. AwdpPlayerController 选手 API (修补包上传、重置、恢复)
8. AwdpScoreService 计分逻辑
9. Awdp.tsx 选手面板
10. AwdpServices.tsx 管理面板

### P2 — 增强功能 (建议)

11. AWDP 专用排行榜组件
12. 修补包上传组件
13. 六种题目状态展示组件
14. SignalR 实时推送 AWDP 专用事件
15. 数据库迁移脚本

---

**文档完成时间**: 2026-06-06
**多 agent 交叉验证**: 4 个 agent 并行梳理，结果交叉验证无矛盾
