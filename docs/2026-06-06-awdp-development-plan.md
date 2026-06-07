# AWDP 重建分模块开发计划

> 版本：v1.0
> 日期：2026-06-06
> 原则：小模块 → 开发 → 多轮质检 → 修复优化 → 下一模块

---

## 开发顺序总览

```
M1 数据模型层        ← 基础，所有模块依赖
  ↓
M2 数据库连接层      ← AppDbContext + DI + EF 配置
  ↓ dotnet build ✓
M3 仓库层            ← 数据访问接口与实现
  ↓ dotnet build ✓
M4 服务层-容器管理    ← AwdpInstanceService (复用 IContainerManager)
  ↓ dotnet build ✓
M5 服务层-轮次引擎    ← AwdpRoundService (核心游戏循环)
  ↓ dotnet build ✓
M6 服务层-验证与计分  ← AwdpCheckerService + AwdpScoreService
  ↓ dotnet build ✓
M7 控制器层          ← Admin + Player API
  ↓ dotnet build ✓ + Swagger 验证
M8 排行榜集成        ← GenScoreboard 接入 AWDP 分数
  ↓ dotnet build ✓
M9 前端 API 客户端   ← TypeScript 类型 + API 调用
  ↓ pnpm build ✓
M10 前端管理员面板    ← AwdpServices 管理页面
  ↓ pnpm build ✓ + 浏览器验证
M11 前端选手面板      ← Awdp 选手页面
  ↓ pnpm build ✓ + 浏览器验证
M12 SignalR 实时推送  ← 轮次/状态变化实时通知
  ↓ dotnet build ✓ + pnpm build ✓
M13 集成验证          ← 全流程 E2E 验证
```

---

## M1: 数据模型层

### 目标
创建 AWDP 全部数据实体，建立完整的类型系统。

### 交付物
| 文件 | 说明 |
|------|------|
| `Models/Data/AwdpService.cs` | AWDP 服务/题目定义 |
| `Models/Data/AwdpServiceInstance.cs` | 每队容器实例 |
| `Models/Data/AwdpRound.cs` | 轮次（含双阶段） |
| `Models/Data/AwdpFlag.cs` | 每轮每队每服务 Flag |
| `Models/Data/AwdpCheckerTask.cs` | Checker/Exp 执行结果 |
| `Models/Data/AwdpPatchSubmission.cs` | 修补包提交记录 |
| `Models/Data/AwdpResetRecord.cs` | 重置记录 |
| `Models/Data/AwdpRecoveryRecord.cs` | 一键恢复记录 |
| `Utils/Enums.cs` | 新增 AwdpRoundStatus、AwdpPatchStatus 枚举 |
| `Models/Request/Game/AwdpServiceModels.cs` | 请求/响应模型 |

### 质量标准
- [ ] 所有实体有 `[Key]` 标注
- [ ] 导航属性正确配置
- [ ] 枚举使用 `[JsonConverter(typeof(JsonStringEnumConverter<>))]`
- [ ] 命名遵循项目规范（PascalCase）
- [ ] XML 文档注释完整

### 验收
```
dotnet build --no-restore → 0 错误
```

---

## M2: 数据库连接层

### 目标
将模型注册到 AppDbContext，配置 EF 关系，注册 DI 服务。

### 交付物
| 文件 | 修改内容 |
|------|----------|
| `Models/AppDbContext.cs` | 新增 8 个 DbSet + EF 关系配置 |
| `Program.cs` | 新增 DI 注册（Repository + Services） |

### EF 配置要点
```
AwdpServiceInstance:
  - ContainerId → Column("ContainerId1") (避免与 Container 表冲突)
  - Service → Cascade delete
  - Team → Cascade delete
  - Container → SetNull delete

AwdpRound:
  - Game → Cascade delete

AwdpFlag:
  - Round → Cascade delete
  - Service → Cascade delete
  - Team → Cascade delete

AwdpCheckerTask:
  - Round → Cascade delete
  - Service → Cascade delete
  - Team → Cascade delete

AwdpPatchSubmission:
  - Round → Cascade delete
  - Service → Cascade delete
  - Team → Cascade delete
```

### DI 注册
```csharp
// 仓库
builder.Services.AddScoped<IAwdpRepository, AwdpRepository>();

// 服务
builder.Services.AddScoped<AwdpInstanceService>();
builder.Services.AddScoped<AwdpCheckerService>();
builder.Services.AddScoped<AwdpScoreService>();
builder.Services.AddScoped<AwdpPatchService>();
builder.Services.AddSingleton<AwdpRoundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AwdpRoundService>());
```

### 验收
```
dotnet build --no-restore → 0 错误
```

---

## M3: 仓库层

### 目标
实现 AWDP 数据访问层，提供清晰的查询接口。

### 交付物
| 文件 | 说明 |
|------|------|
| `Repositories/Interface/IAwdpRepository.cs` | 仓库接口 |
| `Repositories/AwdpRepository.cs` | 仓库实现 |

### 接口设计
```csharp
public interface IAwdpRepository : IRepository
{
    // 服务管理
    Task<AwdpService?> GetService(int serviceId, CancellationToken token = default);
    Task<AwdpService[]> GetServicesByGame(int gameId, CancellationToken token = default);

    // 实例管理
    Task<AwdpServiceInstance?> GetInstance(int instanceId, CancellationToken token = default);
    Task<AwdpServiceInstance[]> GetInstancesByGame(int gameId, CancellationToken token = default);
    Task<AwdpServiceInstance?> GetInstanceByTeamAndService(int teamId, int serviceId, CancellationToken token = default);

    // 轮次管理
    Task<AwdpRound?> GetCurrentRound(int gameId, CancellationToken token = default);
    Task<AwdpRound[]> GetRoundsByGame(int gameId, CancellationToken token = default);

    // Flag 管理
    Task<AwdpFlag?> GetFlag(int roundId, int serviceId, int teamId, CancellationToken token = default);
    Task<AwdpFlag?> GetFlagByValue(string flagValue, CancellationToken token = default);

    // Checker 任务
    Task<AwdpCheckerTask[]> GetCheckerTasksByRound(int roundId, CancellationToken token = default);
    Task<AwdpCheckerTask?> GetCheckerTask(int roundId, int serviceId, int teamId, CancellationToken token = default);

    // 修补包
    Task<AwdpPatchSubmission?> GetPatchSubmission(int roundId, int serviceId, int teamId, CancellationToken token = default);
    Task<AwdpPatchSubmission[]> GetPatchSubmissionsByRound(int roundId, CancellationToken token = default);

    // 重置/恢复记录
    Task<int> GetResetCount(int serviceId, int teamId, CancellationToken token = default);
    Task<int> GetRecoveryCount(int serviceId, int teamId, CancellationToken token = default);

    // 批量操作
    Task CreateRound(AwdpRound round, CancellationToken token = default);
    Task CreateFlags(IEnumerable<AwdpFlag> flags, CancellationToken token = default);
    Task CreateCheckerTasks(IEnumerable<AwdpCheckerTask> tasks, CancellationToken token = default);
    Task UpdateFlagSubmitted(int flagId, CancellationToken token = default);
}
```

### 质量标准
- [ ] 所有查询使用 `AsNoTracking()` 优化读取
- [ ] 导航属性使用 `Include()` 正确加载
- [ ] 方法签名与接口完全一致
- [ ] 无 N+1 查询问题

### 验收
```
dotnet build --no-restore → 0 错误
```

---

## M4: 服务层 — 容器管理

### 目标
实现 AWDP 容器实例的创建、销毁、重置、修补包注入。

### 交付物
| 文件 | 说明 |
|------|------|
| `Services/AwdpInstanceService.cs` | 容器实例管理服务 |

### 核心方法
```csharp
public class AwdpInstanceService
{
    // 为比赛创建所有实例（每队每服务一个容器）
    Task CreateInstancesForGame(Game game, CancellationToken token);

    // 销毁比赛所有实例
    Task DestroyInstancesForGame(int gameId, CancellationToken token);

    // 重置实例（选手自助重置，消耗重置次数）
    Task<bool> ResetInstance(int instanceId, string? newFlag, CancellationToken token);

    // 一键恢复（修补异常后恢复到上一个正常状态）
    Task<bool> RecoverInstance(int instanceId, CancellationToken token);

    // 注入修补包到临时环境并验证
    Task<AwdpPatchSubmission> ApplyPatchAndVerify(
        int instanceId, Stream patchStream, string patchHash,
        AwdpService service, string currentFlag,
        CancellationToken token);
}
```

### 复用点
- `IContainerManager.CreateContainerAsync` — 容器创建
- `IContainerManager.DestroyContainerAsync` — 容器销毁
- `ContainerOrchestrator.CreateIsolatedNetwork` — 网络隔离
- `GZCTF_FLAG` 环境变量注入 — Flag 注入

### 验收
```
dotnet build --no-restore → 0 错误
```

---

## M5: 服务层 — 轮次引擎

### 目标
实现 AWDP 核心游戏循环，支持双阶段（攻击+修补）轮次。

### 交付物
| 文件 | 说明 |
|------|------|
| `Services/AwdpRoundService.cs` | 轮次生命周期管理（Singleton + IHostedService） |

### 核心流程
```
RunGameLoop(Game game, int startRound = 1):
  1. CreateInstancesForGame (首轮)
  2. FOR each round:
     a. 创建 AwdpRound (Status=AttackPhase)
     b. 生成 Flag (每队每服务)
     c. 注入 Flag (ResetInstance)
     d. SignalR 广播: 攻击阶段开始
     e. 执行 Checker (AwdpCheckerService)
     f. 等待 AttackPhaseMinutes
     g. 轮次状态 → PatchPhase
     h. SignalR 广播: 修补阶段开始
     i. 等待 PatchPhaseMinutes
     j. 计算分数 (AwdpScoreService)
     k. 轮次状态 → Finished
  3. 清理游戏状态
```

### 质量标准
- [ ] 使用 `ConcurrentDictionary<int, AwdpGameState>` 管理多比赛并发
- [ ] 应用启动时从数据库恢复进行中的比赛
- [ ] 异常不会导致整个服务崩溃
- [ ] 每个阶段通过 SignalR 实时通知前端

### 验收
```
dotnet build --no-restore → 0 错误
```

---

## M6: 服务层 — 验证与计分

### 目标
实现 Checker/Exp 双重验证和 AWDP 计分逻辑。

### 交付物
| 文件 | 说明 |
|------|------|
| `Services/AwdpCheckerService.cs` | Checker + Exp 执行 |
| `Services/AwdpScoreService.cs` | 计分逻辑 |
| `Services/AwdpPatchService.cs` | 修补包处理 |

### Checker 验证流程
```
RunCheckerForRound(round, services, participations):
  FOR each service × participation:
    1. 执行 Checker 脚本 → CheckerStatus (OK/Mumble/Down/Corrupt)
    2. 保存 AwdpCheckerTask
```

### Exp 验证流程（修补阶段）
```
RunExpForPatch(patchSubmission, service, instance):
  1. 创建临时容器（从原始镜像）
  2. 解压修补包，执行 update.sh
  3. 运行 Checker → 失败则 CheckerFailed
  4. 运行 Exp → 成功则 ExpSucceeded（漏洞未修补），失败则 ExpFailed（修补成功）
  5. 销毁临时容器
  6. 返回 AwdpPatchStatus
```

### 计分逻辑
```
CalculateRoundScores(round, game):
  FOR each service × participation:
    - SLA 分: Checker OK → +SlaPoints
    - 攻击分: 通过 RecordFlagSubmission 即时写入
    - 修补分: PatchStatus.ExpFailed → +PatchPoints
    - 异常扣分: PatchStatus.CheckerFailed → -ServiceAbnormalPenalty
    - 所有分数写入 Submission 表
```

### 验收
```
dotnet build --no-restore → 0 错误
```

---

## M7: 控制器层

### 目标
暴露完整的 Admin 和 Player REST API。

### 交付物
| 文件 | 说明 |
|------|------|
| `Controllers/AwdpAdminController.cs` | 管理 API (路由: `api/admin/awdp`) |
| `Controllers/AwdpPlayerController.cs` | 选手 API (路由: `api/awdp`) |

### Admin API 端点
| HTTP | 路由 | 方法 | 说明 |
|------|------|------|------|
| GET | `games/{gameId}/services` | GetServices | 获取服务列表 |
| POST | `games/{gameId}/services` | CreateService | 创建服务 |
| PUT | `services/{serviceId}` | UpdateService | 更新服务 |
| DELETE | `services/{serviceId}` | DeleteService | 删除服务 |
| POST | `games/{gameId}/start` | StartGame | 启动比赛 |
| POST | `games/{gameId}/stop` | StopGame | 停止比赛 |
| POST | `instances/{instanceId}/reset` | ResetInstance | 管理员重置实例 |
| GET | `games/{gameId}/instances` | GetInstances | 获取实例列表 |
| GET | `games/{gameId}/status` | GetGameStatus | 获取比赛状态 |
| GET | `games/{gameId}/patches` | GetPatchSubmissions | 获取修补包提交记录 |

### Player API 端点
| HTTP | 路由 | 方法 | 说明 |
|------|------|------|------|
| GET | `games/{gameId}/status` | GetGameStatus | 获取比赛状态 |
| GET | `games/{gameId}/instances` | GetMyInstances | 获取己方实例 |
| POST | `games/{gameId}/submit` | SubmitFlag | 提交 Flag |
| POST | `games/{gameId}/patch` | SubmitPatch | 上传修补包 |
| POST | `instances/{instanceId}/reset` | ResetInstance | 选手自助重置 |
| POST | `instances/{instanceId}/recover` | RecoverInstance | 一键恢复 |
| GET | `games/{gameId}/scoreboard` | GetScoreboard | 获取排行榜 |
| GET | `games/{gameId}/attack-logs` | GetAttackLogs | 获取攻击日志 |
| GET | `games/{gameId}/patch-status` | GetPatchStatus | 获取修补状态 |

### 质量标准
- [ ] 所有端点有 `[ProducesResponseType]` 标注
- [ ] GameType 检查: `GameType.AWDP or GameType.Mixed`
- [ ] 权限标注: Admin 用 `[RequireAdmin]`，Player 用 `[RequireUser]`
- [ ] 速率限制: Flag 提交用 `[EnableRateLimiting("Submit")]`
- [ ] 输入验证: 使用 `[Required]` 和模型验证

### 验收
```
dotnet build --no-restore → 0 错误
Swagger UI 可查看所有端点
```

---

## M8: 排行榜集成

### 目标
将 AWDP 分数接入项目原生排行榜系统。

### 交付物
| 文件 | 修改内容 |
|------|----------|
| `Models/Request/Game/ScoreboardModel.cs` | 恢复 AwdScore 字段 |
| `Repositories/GameRepository.cs` | 恢复 GenScoreboard 中的 AWDP 分数计算 |

### 实现
```csharp
// ScoreboardModel.cs
public int AwdScore { get; set; }
public int Score => CtfScore + AwdScore;

// GameRepository.cs GenScoreboard()
if (game.GameType is GameType.AWDP or GameType.Mixed)
{
    var awdpServices = await Context.AwdpServices
        .AsNoTracking()
        .Where(s => s.GameId == game.Id)
        .Select(s => s.Id)
        .ToListAsync(token);

    if (awdpServices.Count > 0)
    {
        var awdpSubmissions = await Context.Submissions
            .AsNoTracking()
            .Where(s => s.GameId == game.Id
                        && awdpServices.Contains(s.ChallengeId)
                        && s.Status == AnswerResult.Accepted)
            .GroupBy(s => s.TeamId)
            .Select(g => new { TeamId = g.Key, Score = g.Sum(s => s.Score) })
            .ToDictionaryAsync(x => x.TeamId, x => x.Score, token);

        foreach (var item in items.Values)
        {
            item.AwdScore = awdpSubmissions.GetValueOrDefault(item.Id, 0);
        }
    }
}
```

### 验收
```
dotnet build --no-restore → 0 错误
```

---

## M9: 前端 API 客户端

### 目标
创建 TypeScript 类型定义和 API 调用方法。

### 交付物
| 文件 | 说明 |
|------|------|
| `ClientApp/src/Api/AwdpApi.ts` | AWDP API 客户端 + 类型定义 |
| `ClientApp/src/Api.ts` | 恢复 awdScore 字段 + EventType 枚举 |

### 类型定义
```typescript
export enum AwdpRoundStatus {
  AttackPhase = 'AttackPhase',
  PatchPhase = 'PatchPhase',
  Finished = 'Finished',
}

export enum AwdpPatchStatus {
  Pending = 'Pending',
  CheckerFailed = 'CheckerFailed',
  ExpSucceeded = 'ExpSucceeded',
  ExpFailed = 'ExpFailed',
  Timeout = 'Timeout',
}

export interface AwdpServiceCreateModel { ... }
export interface AwdpServiceViewModel { ... }
export interface AwdpGameStatusModel { ... }
export interface AwdpSubmitModel { ... }
export interface AwdpPatchSubmitModel { ... }
export interface AwdpTeamServiceStatus { ... }
export interface AwdpScoreboardItem { ... }
export interface AwdpAttackLogItem { ... }
export interface AwdpPatchStatusItem { ... }
```

### API 方法
```typescript
export const awdpAdminApi = { ... }  // 10 个方法
export const awdpPlayerApi = { ... } // 9 个方法
```

### 验收
```
pnpm build → 无错误
```

---

## M10: 前端管理员面板

### 目标
创建 AWDP 管理页面，支持服务管理、比赛控制、实例监控。

### 交付物
| 文件 | 说明 |
|------|------|
| `ClientApp/src/pages/admin/games/[id]/AwdpServices.tsx` | 管理面板主页面 |
| `ClientApp/src/pages/admin/games/[id]/awdp-services.tsx` | re-export |
| `ClientApp/src/locales/zh-CN/admin.json` | 新增 awdp.* i18n key |
| `ClientApp/src/locales/en-US/admin.json` | 新增 awdp.* i18n key |

### 功能清单
- [ ] 服务列表（CRUD）
- [ ] 服务配置（Checker 脚本、Exp 脚本、分数配置）
- [ ] 比赛启动/停止
- [ ] 实例状态监控
- [ ] 管理员重置实例
- [ ] 修补包提交记录查看
- [ ] 轮次状态显示（攻击阶段/修补阶段）

### 前端路由集成
修改 `WithGameEditTab.tsx`:
```typescript
const isAwdpGame = game?.gameType === GameType.AWDP || game?.gameType === GameType.Mixed
// 条件显示 awdp-services tab
```

### 验收
```
pnpm build → 无错误
浏览器访问 /admin/games/{id}/awdp-services → 页面正常渲染
```

---

## M11: 前端选手面板

### 目标
创建 AWDP 选手页面，支持攻击、修补、重置、恢复。

### 交付物
| 文件 | 说明 |
|------|------|
| `ClientApp/src/pages/games/[id]/Awdp.tsx` | 选手面板主页面 |
| `ClientApp/src/locales/zh-CN/game.json` | 新增 awdp.* i18n key |
| `ClientApp/src/locales/en-US/game.json` | 新增 awdp.* i18n key |

### 功能清单
- [ ] 轮次状态卡片（攻击阶段/修补阶段倒计时）
- [ ] Flag 提交输入框
- [ ] 修补包上传组件
- [ ] 己方实例状态卡片（IP、端口、状态）
- [ ] 自助重置按钮（显示剩余次数）
- [ ] 一键恢复按钮（显示剩余次数）
- [ ] AWDP 排行榜（攻击分 + 修补分 + SLA 分 - 异常扣分）
- [ ] 攻击日志表格
- [ ] 修补状态面板（六种题目状态）

### 前端路由集成
修改 `WithGameTab.tsx`:
```typescript
const isAwdpGame = game?.gameType === GameType.AWDP || game?.gameType === GameType.Mixed
// 条件显示 awdp tab
```

### 验收
```
pnpm build → 无错误
浏览器访问 /games/{id}/awdp → 页面正常渲染
```

---

## M12: SignalR 实时推送

### 目标
实现 AWDP 轮次和状态变化的实时推送。

### 交付物
| 文件 | 修改内容 |
|------|----------|
| `Hubs/Clients/IMonitorClient.cs` | 新增 AWDP SignalR 方法 |

### 新增方法
```csharp
public Task ReceivedAwdpRoundChange(AwdpGameStatusModel status);
public Task ReceivedAwdpServiceStatusChange(AwdpServiceStatusModel status);
public Task ReceivedAwdpPatchResult(AwdpPatchResultModel result);
```

### 验收
```
dotnet build --no-restore → 0 错误
pnpm build → 无错误
```

---

## M13: 集成验证

### 目标
全流程 E2E 验证，确保 AWDP 功能完整可用。

### 验证清单
- [ ] 创建 AWDP 类型比赛
- [ ] 配置 AWDP 服务（Checker + Exp 脚本）
- [ ] 启动比赛，验证轮次循环
- [ ] 选手提交 Flag，验证攻击分
- [ ] 选手上传修补包，验证修补分
- [ ] 修补异常时一键恢复
- [ ] 选手自助重置容器
- [ ] 排行榜正确显示 AWDP 分数
- [ ] SignalR 实时推送正常
- [ ] 管理面板所有功能正常
- [ ] 选手面板所有功能正常

### 验收
```
dotnet build → 0 错误
dotnet test → 0 失败
pnpm build → 无错误
全流程手动验证通过
```

---

## 质量保障体系

### 核心原则

1. **零容忍 TODO/占位/简化实现** — 每个方法必须是完整实现，不允许 "TODO: implement later"、`throw new NotImplementedException()`、返回硬编码假数据等任何形式的偷工减料
2. **每轮都是最高水准** — 自我迭代不是"先写个能跑的再优化"，而是"写完后重新审视，发现更优解法和遗漏点"
3. **多 agent 交叉验证** — 关键模块完成后，派遣独立 agent 从不同角度审查（架构一致性、安全漏洞、性能瓶颈、边界条件）
4. **质量高于速度** — 宁可一个模块花 3 小时做到完美，也不 3 个模块各花 1 小时草草了事
5. **合理加载技能** — 特定阶段必须加载对应技能，借助专业能力提升质量

### 技能加载规范

| 阶段 | 必须加载的技能 | 触发时机 |
|------|--------------|----------|
| **前端页面开发** | `frontend-design` | 开始编写任何前端组件/页面前 |
| **前端页面开发** | `awesome-design-md` | 需要设计系统/主题参考时 |
| **代码质量审查** | `code-review` | 每个模块完成后，Phase 4 交叉验证时 |
| **安全审查** | `security-review` | 涉及认证、授权、输入处理的模块完成后 |
| **实现计划** | `superpowers:writing-plans` | 开始新模块前，规划实现步骤 |
| **测试驱动开发** | `superpowers:test-driven-development` | 编写核心业务逻辑时 |

#### 前端模块开发流程（M9-M12 强制执行）

```
开始前端模块
  │
  ├─ Step 1: 加载 frontend-design skill
  │    └─ 获取设计规范、组件模式、样式指南
  │
  ├─ Step 2: 加载 awesome-design-md skill（如需设计参考）
  │    └─ 获取匹配的设计系统和主题
  │
  ├─ Step 3: 编写组件代码
  │    ├─ 遵循 skill 中的设计规范
  │    ├─ 使用项目现有的 Mantine 组件库
  │    └─ 保持与项目现有页面风格一致
  │
  ├─ Step 4: 加载 code-review skill
  │    └─ 审查组件代码质量
  │
  └─ 修复 → 提交
```

#### 后端模块审查流程（M1-M8 强制执行）

```
完成后端模块
  │
  ├─ Step 1: 加载 code-review skill
  │    └─ 审查代码质量、架构一致性
  │
  ├─ Step 2: 涉及安全相关模块时加载 security-review skill
  │    └─ 审查认证、授权、输入验证、敏感数据处理
  │
  └─ 修复 → 提交
```

### 禁止事项清单

| 禁止 | 说明 |
|------|------|
| `// TODO:` | 不允许任何 TODO 注释 |
| `throw new NotImplementedException()` | 不允许未实现异常 |
| `return null!` | 不允许无意义的 null 返回（除非语义上确实可空） |
| `return []` 作为占位 | 不允许返回空集合作为"后续实现"的占位 |
| `Assert.True(true)` | 不允许假测试 |
| 硬编码假数据 | 不允许 `new Xxx { Id = 1, Name = "test" }` 等测试用数据混入生产代码 |
| 复制粘贴不修改 | 从 AWD 代码复制后必须逐行审查，确保语义正确 |
| 跳过错误处理 | 不允许 `catch (Exception) { }` 空 catch 块 |

### 每模块开发流程

```
模块开发
  │
  ├─ Phase 1: 完整实现
  │    ├─ 编写所有代码，确保零 TODO、零占位
  │    ├─ 每个方法都有完整的业务逻辑
  │    ├─ 每个异常路径都有明确的处理
  │    └─ 每个边界条件都有覆盖
  │
  ├─ Phase 2: 自我审视（第一轮）
  │    ├─ 逐行审查自己的代码
  │    ├─ 检查项：
  │    │    ├─ 是否有未处理的异常路径？
  │    │    ├─ 是否有资源泄漏（未 dispose 的 DbContext scope）？
  │    │    ├─ 是否有并发安全问题（竞态条件、死锁）？
  │    │    ├─ 是否有 N+1 查询？
  │    │    ├─ 是否有不必要的内存分配？
  │    │    ├─ 命名是否与项目现有风格一致？
  │    │    ├─ 注释是否准确且必要？
  │    │    └─ 是否有安全漏洞（注入、越权、信息泄露）？
  │    └─ 修复发现的所有问题
  │
  ├─ Phase 3: 编译与静态检查
  │    ├─ dotnet build → 0 错误、0 警告（或仅保留已知的无关警告）
  │    ├─ pnpm build → 0 错误（前端模块）
  │    └─ 检查编译输出，确认无隐式转换、未使用变量等
  │
  ├─ Phase 4: 多 agent 交叉验证
  │    ├─ 派遣独立 agent 审查：
  │    │    ├─ Agent A: 架构一致性 — 是否遵循项目现有模式？
  │    │    ├─ Agent B: 安全审计 — OWASP Top 10 检查
  │    │    ├─ Agent C: 逻辑完整性 — 边界条件、错误路径、并发
  │    │    └─ Agent D: 集成质量 — DI、EF、API 契约、前后端类型匹配
  │    ├─ 收集所有 agent 反馈
  │    └─ 逐一修复确认的问题
  │
  ├─ Phase 5: 自我审视（第二轮）
  │    ├─ 重新阅读修复后的代码
  │    ├─ 从"第一次看到这段代码"的视角审查
  │    ├─ 检查修复是否引入了新问题
  │    └─ 优化代码结构和可读性
  │
  ├─ Phase 6: 最终验证
  │    ├─ dotnet build → 0 错误
  │    ├─ dotnet test → 0 失败（如果有相关测试）
  │    ├─ pnpm build → 0 错误（前端模块）
  │    └─ 确认所有 agent 反馈已处理
  │
  └─ 提交 → 进入下一模块
```

### Phase 4 交叉验证检查清单

#### Agent A: 架构一致性
- [ ] 是否遵循项目的 Repository 模式？
- [ ] 是否遵循项目的 DI 注册方式（Scoped vs Singleton）？
- [ ] 是否遵循项目的命名规范（PascalCase 类名、_camelCase 私有字段）？
- [ ] 是否遵循项目的异常处理模式（RequestResponse 包装）？
- [ ] 是否遵循项目的路由命名模式（`api/admin/awdp`、`api/awdp`）？
- [ ] 是否遵循项目的权限标注模式（`[RequireAdmin]`、`[RequireUser]`）？
- [ ] 是否遵循项目的 EF 配置模式（Fluent API in OnModelCreating）？

#### Agent B: 安全审计
- [ ] 所有用户输入是否经过验证？
- [ ] 是否有 SQL 注入风险（原始 SQL 拼接）？
- [ ] 是否有越权访问风险（未检查 TeamId/GameId 匹配）？
- [ ] 是否有信息泄露风险（错误消息暴露内部细节）？
- [ ] 是否有 DoS 风险（无限制的批量操作、无超时的长时间运行）？
- [ ] 敏感数据（密码、token）是否正确处理（不返回给前端、不记录日志）？
- [ ] 文件上传（修补包）是否有大小限制和类型验证？

#### Agent C: 逻辑完整性
- [ ] 所有 async 方法是否正确使用 CancellationToken？
- [ ] 所有数据库操作是否在事务中（多表写入时）？
- [ ] 所有 IDisposable 资源是否正确释放？
- [ ] 并发场景是否安全（同一比赛的多个轮次、同一队伍的多次提交）？
- [ ] 边界条件是否处理（空列表、null 值、超出范围的索引）？
- [ ] 错误恢复是否正确（容器创建失败、网络超时、脚本执行失败）？
- [ ] 状态机是否完整（所有状态转换路径都有处理）？

#### Agent D: 集成质量
- [ ] DI 注册是否在 Program.cs 中完成？
- [ ] EF DbSet 是否在 AppDbContext 中注册？
- [ ] EF 关系配置是否在 OnModelCreating 中完成？
- [ ] API 返回类型是否与前端 TypeScript 类型匹配？
- [ ] 枚举值是否使用 `[JsonConverter(typeof(JsonStringEnumConverter<>))]`？
- [ ] SignalR 方法签名是否与前端调用匹配？
- [ ] 排行榜集成是否正确写入 Submission 表？

### Phase 4 交叉验证检查清单（续）

#### Agent E: 前端质量（前端模块适用）
- [ ] TypeScript 类型安全：无 `any` 类型，所有 props/state 有明确类型
- [ ] 组件结构：合理拆分，单一职责，不超 300 行的巨型组件
- [ ] Hook 使用：`useEffect` 有正确的依赖数组和 cleanup 函数
- [ ] 内存泄漏防护：定时器、订阅、AbortController 在 unmount 时清理
- [ ] 加载/错误/空状态：每个数据驱动的 UI 都有 loading、error、empty 三种状态处理
- [ ] API 错误处理：所有 API 调用都有 try/catch，使用 `showErrorMsg` 展示
- [ ] i18n 完整性：所有用户可见文本使用 `t('key')`，无硬编码中文/英文
- [ ] i18n key 一致性：代码中使用的 key 在 4 个 locale 文件中都有定义
- [ ] i18n 参数匹配：`t('key', { param })` 的参数名与 locale 模板 `{{param}}` 一致
- [ ] 响应式布局：使用 Mantine 的 `span={{ base: 12, md: 6 }}` 等响应式断点
- [ ] 可访问性：交互元素有 `aria-label`，表单有 `label` 关联
- [ ] 无死代码：未使用的 import、state、变量全部删除
- [ ] 无硬编码魔法数字：端口、超时、分页大小等使用常量或配置

#### Agent F: 数据库与迁移质量
- [ ] 迁移 Up/Down 对称：每个 Up 操作都有对应的 Down 回滚
- [ ] 索引设计：外键字段有索引，高频查询字段有索引
- [ ] 列类型正确：`string` 用 `varchar(n)` 而非 `text`（除大文本字段）
- [ ] 默认值合理：`int` 字段有默认值，`bool` 字段有默认值
- [ ] 级联删除正确：父子关系的删除行为符合业务语义
- [ ] 唯一约束：需要唯一性的字段组合有唯一索引
- [ ] 可空性正确：可空引用类型标注与 EF 配置一致

#### Agent G: API 设计质量
- [ ] RESTful 语义：GET 幂等、POST 创建/操作、PUT 更新、DELETE 删除
- [ ] HTTP 状态码正确：200 成功、201 创建、204 删除成功、400 客户端错误、404 未找到、403 无权限
- [ ] 错误响应格式统一：使用 `RequestResponse` 包装
- [ ] 分页支持：列表端点有 `count`/`skip` 参数
- [ ] 限流标注：高频端点（Flag 提交、容器操作）有 `[EnableRateLimiting]`
- [ ] 响应脱敏：不返回 `SshPasswordHash`、`GuacamoleToken` 等敏感字段
- [ ] Swagger 文档：所有端点有 `<summary>`、`<param>`、`<response>` XML 注释

#### Agent H: 性能与资源质量
- [ ] 无 N+1 查询：使用 `Include()` 或投影避免循环内数据库查询
- [ ] `AsNoTracking()` 只读查询正确使用
- [ ] 异步方法全程 `await`，无 `.Result` / `.Wait()` 阻塞
- [ ] 大量数据操作使用批量操作而非逐条处理
- [ ] `CancellationToken` 贯穿所有异步调用链
- [ ] `IServiceScope` 在后台服务中正确创建和释放
- [ ] 无不必要的 `ToList()` / `ToArray()` 提前物化
- [ ] 字符串拼接使用插值或 `StringBuilder`，非循环内 `+`

### 自我迭代的正确理解

**错误理解（禁止）：**
> "先写个能编译通过的版本，下一轮再优化"
> "先用简化实现占位，后续再补全"
> "先不处理边界情况，等测试发现再修"
> "前端先写个大概样式，下一轮再调"
> "先不写错误处理，等联调发现再加"

**正确理解（要求）：**
> "写完后重新审视：这个方法的异常处理是否完整？这个查询是否有 N+1 问题？这个并发场景是否安全？"
> "写完后重新审视：是否有更优雅的实现方式？是否可以复用项目中已有的工具方法？"
> "写完后重新审视：从攻击者视角，这个接口是否安全？从运维视角，这个日志是否足够排查问题？"
> "写完后重新审视：这个组件的 loading/error/empty 状态是否都有处理？定时器是否在 unmount 时清理？i18n key 是否都有定义？"
> "写完后重新审视：这个迁移的 Up/Down 是否对称？索引是否足够？级联删除是否正确？"
> "写完后重新审视：这个 API 的错误响应格式是否与项目一致？分页是否支持？限流是否配置？"

---

**文档完成时间：** 2026-06-06
