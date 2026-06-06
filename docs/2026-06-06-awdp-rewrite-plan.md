# AWDP 完全重写方案 — 解耦与重构指南

> 版本：v1.0
> 日期：2026-06-06
> 前置文档：`docs/2026-06-06-awdp-extension-analysis.md`
> 核验状态：3 agent 交叉验证通过，修正 3 处遗漏 + 2 个 bug

---

## 一、旧 AWD 代码完整清单（核验后修正版）

### 1.1 AWD 专属文件（14 个，可整体删除）

| # | 文件 | 行数 | 职责 |
|---|------|------|------|
| 1 | `Models/Data/AwdService.cs` | 34 | AWD 服务实体 |
| 2 | `Models/Data/AwdServiceInstance.cs` | 22 | 每队容器实例实体 |
| 3 | `Models/Data/AwdRound.cs` | 27 | 轮次实体 + AwdRoundStatus 枚举 |
| 4 | `Models/Data/AwdFlag.cs` | 24 | 每轮每队每服务 Flag 实体 |
| 5 | `Models/Data/AwdCheckerTask.cs` | 30 | Checker 执行结果实体 + CheckerStatus 枚举 |
| 6 | `Models/Request/Game/AwdServiceModels.cs` | 95 | 9 个请求/响应模型 |
| 7 | `Services/AwdRoundService.cs` | 240 | 轮次生命周期 (Singleton+IHostedService) |
| 8 | `Services/AwdInstanceService.cs` | 120 | 容器实例管理 |
| 9 | `Services/AwdCheckerService.cs` | 91 | Checker 脚本执行 |
| 10 | `Services/AwdScoreService.cs` | 140 | 计分逻辑 |
| 11 | `Repositories/Interface/IAwdRepository.cs` | 18 | 仓库接口 |
| 12 | `Repositories/AwdRepository.cs` | ~150 | 仓库实现 |
| 13 | `Controllers/AwdAdminController.cs` | 300 | 管理 API (9 个端点) |
| 14 | `Controllers/AwdPlayerController.cs` | 288 | 选手 API (5 个端点) |

### 1.2 前端 AWD 专属文件（4 个，可整体删除）

| # | 文件 | 职责 |
|---|------|------|
| 1 | `ClientApp/src/Api/AwdApi.ts` | AWD API 客户端 + 类型定义 |
| 2 | `ClientApp/src/pages/games/[id]/Awd.tsx` | AWD 选手面板 |
| 3 | `ClientApp/src/pages/admin/games/[id]/AwdServices.tsx` | AWD 管理面板 |
| 4 | `ClientApp/src/pages/admin/games/[id]/awd-services.tsx` | re-export |

### 1.3 数据库迁移文件（2 个，需保留但不修改）

| # | 文件 | 说明 |
|---|------|------|
| 1 | `Migrations/20260604164000_AddAwdModeSupport.cs` | AWD 表创建迁移 |
| 2 | `Migrations/20260604164000_AddAwdModeSupport.Designer.cs` | 迁移设计文件 |
| 3 | `Migrations/AppDbContextModelSnapshot.cs` | 模型快照（含 AWD 实体定义） |

---

## 二、旧 AWD 与项目的耦合点全景图（核验后修正版）

### 2.1 后端耦合点（8 处）

```
┌─────────────────────────────────────────────────────────────┐
│                    项目核心框架                               │
│                                                             │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐   │
│  │ AppDbContext  │    │  Program.cs  │    │  Enums.cs    │   │
│  │  5 DbSet     │    │  6 行 DI     │    │  GameType    │   │
│  │  1 EF Config │    │              │    │  EventType   │   │
│  └──────┬───────┘    └──────┬───────┘    │  ChallengeT  │   │
│         │                   │            │  IsAwdSvc()  │   │
│         │                   │            └──────┬───────┘   │
│         ▼                   ▼                   ▼           │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              AWD 专属代码层 (14 个文件)                │   │
│  │  Controllers → Services → Repositories → Models      │   │
│  └──────────────────────────────────────────────────────┘   │
│         │                   │                               │
│         ▼                   ▼                               │
│  ┌──────────────┐    ┌──────────────┐                       │
│  │ GameRepo     │    │ IMonitorClient│                      │
│  │ GenScoreboard│    │  2 个方法     │                      │
│  │  26 行 AWD   │    │              │                       │
│  └──────────────┘    └──────────────┘                       │
│         │                                                   │
│         ▼                                                   │
│  ┌──────────────┐                                           │
│  │ScoreboardModel│                                          │
│  │ AwdScore 字段 │                                          │
│  │ Score 计算    │                                          │
│  └──────────────┘                                           │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 前端耦合点（6 处）

```
┌─────────────────────────────────────────────────────────────┐
│                    前端核心框架                               │
│                                                             │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐   │
│  │  Api.ts      │    │ WithGameTab  │    │WithGameEdit  │   │
│  │  GameType    │    │  isAwdGame   │    │  isAwdGame   │   │
│  │  awdScore    │    │  行65        │    │  行46        │   │
│  └──────────────┘    └──────────────┘    └──────────────┘   │
│                                                             │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐   │
│  │Scoreboard    │    │Scoreboard    │    │GameCreate    │   │
│  │Table.tsx     │    │ItemModal.tsx │    │Modal.tsx     │   │
│  │ AWD 列 行120 │    │ awd 行175    │    │ AWD 选项     │   │
│  └──────────────┘    └──────────────┘    └──────────────┘   │
│                                                             │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐   │
│  │ Info.tsx     │    │ 4 个 locale  │    │screenDemo    │   │
│  │ AWD 选项     │    │ 77 个 key    │    │Data.ts       │   │
│  └──────────────┘    └──────────────┘    └──────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 2.3 耦合点详细清单（含行号）

| # | 耦合点 | 文件:行号 | 耦合类型 | 解耦难度 |
|---|--------|-----------|----------|----------|
| **后端** | | | | |
| B1 | AppDbContext 5 DbSet | `AppDbContext.cs:55-59` | 数据层注册 | 中 — 需迁移 |
| B2 | AppDbContext EF 配置 | `AppDbContext.cs:352-371` | 数据层配置 | 中 — 需迁移 |
| B3 | DI 注册 | `Program.cs:48-53` | 服务注册 | 低 — 直接替换 |
| B4 | GameType.AWD 枚举值 | `Enums.cs:203` | 类型定义 | 低 — 新增值 |
| B5 | EventType.Awd* (6个值) | `Enums.cs:252-277` | 事件类型 | 低 — 新增值 |
| B6 | ChallengeType.AWDService | `Enums.cs:384` | 题目类型 | 低 — 新增值 |
| B7 | IsAwdService() 扩展方法 | `Enums.cs:424` | 工具方法 | 低 — 新增方法 |
| B8 | GameRepository.GenScoreboard | `GameRepository.cs:596-621` | 排行榜计算 | 中 — 需重写逻辑 |
| B9 | ScoreboardModel.AwdScore | `ScoreboardModel.cs:226,232` | 数据模型 | 低 — 字段可保留/重命名 |
| B10 | IMonitorClient 2个方法 | `IMonitorClient.cs:20,25` | SignalR 接口 | 低 — 新增方法 |
| **前端** | | | | |
| F1 | GameType.AWD 枚举 | `Api.ts:98` | 类型定义 | 低 — 新增值 |
| F2 | awdScore 字段 | `Api.ts` (ScoreboardItem) | 类型定义 | 低 — 字段可保留/重命名 |
| F3 | isAwdGame 判断 | `WithGameTab.tsx:65` | 条件渲染 | 低 — 一行修改 |
| F4 | isAwdGame 判断 | `WithGameEditTab.tsx:46` | 条件渲染 | 低 — 一行修改 |
| F5 | GameType 选项 | `Info.tsx:243` | 下拉选项 | 低 — 一行新增 |
| F6 | GameType 选项 | `GameCreateModal.tsx:111` | 下拉选项 | 低 — 一行新增 |
| F7 | AWD 排行榜列 | `ScoreboardTable.tsx:120,209` | 表格列 | 低 — 可保留/重命名 |
| F8 | AWD 排行榜详情 | `ScoreboardItemModal.tsx:175,178` | 弹窗展示 | 低 — 可保留/重命名 |
| F9 | 77 个 i18n key | 4 个 locale 文件 | 国际化 | 低 — 直接替换 |
| F10 | demo 数据 | `screenDemoData.ts:278` | 演示数据 | 低 — 一行修改 |

---

## 三、重写策略：三阶段解耦法

### 3.1 核心原则

1. **先解耦，再删除，最后重建** — 不要一步到位，分阶段确保每步可验证
2. **数据库优先** — 先处理数据层，再处理服务层，最后处理表现层
3. **向后兼容** — 重写过程中保持 `dotnet build` 和 `dotnet test` 始终通过
4. **枚举值复用** — `GameType.AWD=1` 改为 `GameType.AWDP=1`（值不变，名称变），避免数据库迁移

### 3.2 阶段划分

```
阶段 0: 准备工作 (无代码变更)
  └─ 创建 worktree 分支，确保基线测试通过

阶段 1: 解耦 — 从共享文件中剥离 AWD 引用 (最小侵入)
  ├─ Step 1.1: 处理 Enums.cs — GameType.AWD → AWDP，保留 EventType
  ├─ Step 1.2: 处理 ScoreboardModel — AwdScore 字段保留（AWDP 复用）
  ├─ Step 1.3: 处理 GameRepository.GenScoreboard — 保留逻辑，修改条件
  ├─ Step 1.4: 处理 IMonitorClient — 保留方法，修改语义
  ├─ Step 1.5: 处理前端 Api.ts — GameType.AWD → AWDP
  ├─ Step 1.6: 处理前端 isAwdGame 判断 — 加入 AWDP
  ├─ Step 1.7: 处理前端 GameType 选项 — 加入 AWDP
  ├─ Step 1.8: 处理前端 i18n — AWD key → AWDP key
  └─ 验证: dotnet build + dotnet test + 前端 build

阶段 2: 删除旧 AWD 专属代码 (干净切割)
  ├─ Step 2.1: 删除旧 AWD 专属文件 (14 个后端 + 4 个前端)
  ├─ Step 2.2: 清理 AppDbContext — 删除旧 DbSet 和 EF 配置
  ├─ Step 2.3: 清理 Program.cs — 删除旧 DI 注册
  ├─ Step 2.4: 创建新迁移 — 删除旧 AWD 表
  └─ 验证: dotnet build + dotnet test

阶段 3: 重建 AWDP 全新实现
  ├─ Step 3.1: 新增 AWDP 数据模型
  ├─ Step 3.2: 新增 AppDbContext 注册 + EF 配置
  ├─ Step 3.3: 新增 DI 注册
  ├─ Step 3.4: 新增 AWDP 服务层
  ├─ Step 3.5: 新增 AWDP 控制器
  ├─ Step 3.6: 新增 AWDP 前端页面
  ├─ Step 3.7: 新增 i18n key
  ├─ Step 3.8: 创建新迁移 — 创建 AWDP 表
  └─ 验证: dotnet build + dotnet test + 前端 build + E2E
```

---

## 四、各阶段详细步骤

### 阶段 0: 准备工作

```
1. git worktree add .worktrees/awdp-rewrite -b awdp-rewrite
2. cd .worktrees/awdp-rewrite
3. dotnet test → 确认 227/227 通过
4. cd ClientApp && pnpm build → 确认前端构建通过
```

### 阶段 1: 解耦 — 共享文件修改

#### Step 1.1: Enums.cs

**修改内容：**
```csharp
// 旧:
[Description("AWD")]
AWD = 1,

// 新:
[Description("AWDP")]
AWDP = 1,
```

**保留不变：**
- `EventType.AwdFlagSubmit=20` ~ `AwdAttackSuccess=25` — AWDP 复用这些事件类型
- `ChallengeType.AWDService = 0b10000` — AWDP 复用此题目类型
- `IsAwdService()` 扩展方法 — AWDP 复用

**影响范围：** 所有引用 `GameType.AWD` 的代码需要改为 `GameType.AWDP`

#### Step 1.2: ScoreboardModel.cs

**保留不变：**
- `AwdScore` 字段 — AWDP 排行榜同样需要显示攻防分
- `Score => CtfScore + AwdScore` — 计算逻辑不变

**理由：** 字段名 `AwdScore` 在 AWDP 上下文中仍然语义合理（AWD 的分数），且前端已绑定此字段名。

#### Step 1.3: GameRepository.GenScoreboard

**修改内容：**
```csharp
// 旧:
if (game.GameType is GameType.AWD or GameType.Mixed)

// 新:
if (game.GameType is GameType.AWDP or GameType.Mixed)
```

**逻辑不变：** 仍通过 `AwdServices` 表查询，AWDP 复用同一张表。

#### Step 1.4: IMonitorClient.cs

**保留不变：**
- `ReceivedAwdRoundChange(AwdGameStatusModel)` — AWDP 复用
- `ReceivedAwdServiceStatusChange(AwdServiceStatusModel)` — AWDP 复用

**理由：** 方法签名和参数类型不变，AWDP 的轮次变化和服务状态变化语义相同。

#### Step 1.5: 前端 Api.ts

**修改内容：**
```typescript
// 旧:
export enum GameType {
  AWD = "AWD",
}

// 新:
export enum GameType {
  AWDP = "AWDP",
}
```

**注意：** 此文件由 Swagger 自动生成，重写后需要重新生成。手动修改仅作为过渡。

#### Step 1.6: 前端 isAwdGame 判断

**WithGameTab.tsx:65 和 WithGameEditTab.tsx:46：**
```typescript
// 旧:
const isAwdGame = game?.gameType === GameType.AWD || game?.gameType === GameType.Mixed

// 新:
const isAwdGame = game?.gameType === GameType.AWDP || game?.gameType === GameType.Mixed
```

#### Step 1.7: 前端 GameType 选项

**Info.tsx:243 和 GameCreateModal.tsx:111：**
```typescript
// 旧:
{ value: GameType.AWD, label: 'AWD' },

// 新:
{ value: GameType.AWDP, label: 'AWDP' },
```

#### Step 1.8: 前端 i18n

**4 个 locale 文件中所有 `game.awd.*` 和 `admin.awd.*` key：**
- 方案 A：保留 key 名不变（`game.awd.*`），只修改显示文本（"AWD 攻防" → "AWDP 攻防"）
- 方案 B：重命名 key（`game.awdp.*`），需要同步修改所有代码引用

**推荐方案 A：** 最小侵入，保留 key 名，只修改显示文本。AWD 和 AWDP 的 UI 功能语义相同。

---

### 阶段 2: 删除旧 AWD 专属代码

#### Step 2.1: 删除文件

**后端（14 个）：**
```
rm src/GZCTF/Models/Data/AwdService.cs
rm src/GZCTF/Models/Data/AwdServiceInstance.cs
rm src/GZCTF/Models/Data/AwdRound.cs
rm src/GZCTF/Models/Data/AwdFlag.cs
rm src/GZCTF/Models/Data/AwdCheckerTask.cs
rm src/GZCTF/Models/Request/Game/AwdServiceModels.cs
rm src/GZCTF/Services/AwdRoundService.cs
rm src/GZCTF/Services/AwdInstanceService.cs
rm src/GZCTF/Services/AwdCheckerService.cs
rm src/GZCTF/Services/AwdScoreService.cs
rm src/GZCTF/Repositories/Interface/IAwdRepository.cs
rm src/GZCTF/Repositories/AwdRepository.cs
rm src/GZCTF/Controllers/AwdAdminController.cs
rm src/GZCTF/Controllers/AwdPlayerController.cs
```

**前端（4 个）：**
```
rm ClientApp/src/Api/AwdApi.ts
rm ClientApp/src/pages/games/[id]/Awd.tsx
rm ClientApp/src/pages/admin/games/[id]/AwdServices.tsx
rm ClientApp/src/pages/admin/games/[id]/awd-services.tsx
```

#### Step 2.2: 清理 AppDbContext

**删除行 55-59：**
```csharp
// 删除:
public DbSet<AwdService> AwdServices { get; set; } = null!;
public DbSet<AwdServiceInstance> AwdServiceInstances { get; set; } = null!;
public DbSet<AwdRound> AwdRounds { get; set; } = null!;
public DbSet<AwdFlag> AwdFlags { get; set; } = null!;
public DbSet<AwdCheckerTask> AwdCheckerTasks { get; set; } = null!;
```

**删除行 352-371：**
```csharp
// 删除整个 Entity<AwdServiceInstance> 配置块
```

#### Step 2.3: 清理 Program.cs

**删除行 48-53：**
```csharp
// 删除:
builder.Services.AddScoped<IAwdRepository, AwdRepository>();
builder.Services.AddScoped<AwdInstanceService>();
builder.Services.AddScoped<AwdCheckerService>();
builder.Services.AddScoped<AwdScoreService>();
builder.Services.AddSingleton<AwdRoundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AwdRoundService>());
```

#### Step 2.4: 创建新迁移

```bash
dotnet ef migrations add RemoveAwdModeSupport
```

此迁移将删除 5 张 AWD 表。**注意：** 如果生产环境有 AWD 数据，需要先备份。

---

### 阶段 3: 重建 AWDP 全新实现

#### Step 3.1: 新增数据模型

| 模型 | 说明 |
|------|------|
| `AwdpService` | AWDP 服务定义（扩展 AwdService 字段） |
| `AwdpServiceInstance` | 每队容器实例 |
| `AwdpRound` | 轮次（含双阶段：攻击+修补） |
| `AwdpFlag` | 每轮每队每服务 Flag |
| `AwdpCheckerTask` | Checker/Exp 执行结果 |
| `AwdpPatchSubmission` | 修补包提交记录（新增） |
| `AwdpResetRecord` | 重置记录（新增） |
| `AwdpRecoveryRecord` | 一键恢复记录（新增） |

#### Step 3.2-3.8: 详见 `docs/2026-06-06-awdp-extension-analysis.md`

---

## 五、风险点与应对

### 5.1 数据库迁移风险

| 风险 | 影响 | 应对 |
|------|------|------|
| 生产环境有 AWD 数据 | 删除表会丢失数据 | 先备份，或保留旧表（不删除） |
| 迁移顺序冲突 | 多人开发时迁移冲突 | 使用独立分支，合并后重新生成迁移 |
| EF Core 快照不一致 | 迁移生成错误 | 删除旧迁移重新生成（如果是新项目） |

### 5.2 枚举值变更风险

| 风险 | 影响 | 应对 |
|------|------|------|
| GameType.AWD=1 → AWDP=1 | 数据库中已有的 GameType=1 记录 | 值不变（都是 1），只需改名称，无数据迁移 |
| 前端 Swagger 生成 | Api.ts 中 GameType 枚举值变化 | 重写完成后重新生成 Api.ts |

### 5.3 编译错误风险

| 风险 | 影响 | 应对 |
|------|------|------|
| 删除文件后引用未清理 | 编译失败 | 阶段 2 删除后立即 `dotnet build` 验证 |
| 前端删除文件后路由缺失 | 前端构建失败 | 阶段 2 删除后立即 `pnpm build` 验证 |

### 5.4 测试失败风险

| 风险 | 影响 | 应对 |
|------|------|------|
| 旧 AWD 测试依赖旧模型 | 测试失败 | 删除旧测试，重写新测试 |
| 排行榜计算逻辑变更 | 测试断言失败 | 更新测试断言 |

---

## 六、验证标准

### 6.1 每个 Step 完成后

```
✓ dotnet build 无错误
✓ dotnet test 失败: 0
✓ pnpm build 无错误（前端修改时）
```

### 6.2 阶段 2 完成后

```
✓ 无任何文件引用 AwdService/AwdRound/AwdFlag 等旧模型
✓ Grep "class Awd[^p]" 无结果（确认旧类名不存在）
✓ dotnet build 无错误
✓ dotnet test 失败: 0（AWD 相关测试已删除）
```

### 6.3 阶段 3 完成后

```
✓ AWDP 比赛可创建、配置、启动
✓ AWDP 选手面板可访问
✓ AWDP 管理面板可访问
✓ AWDP 轮次循环正常运行
✓ AWDP 修补包上传和验证正常
✓ AWDP 排行榜正确显示
✓ dotnet test 失败: 0
✓ E2E 测试通过
```

---

## 七、旧代码质量问题记录（重写时避免）

核验发现的旧代码问题，重写时应修正：

| # | 问题 | 位置 | 重写时修正 |
|---|------|------|-----------|
| 1 | `requireAwd` 死字段 | `WithGameTab.tsx` | 删除此属性 |
| 2 | i18n 参数名不匹配 | `Awd.tsx:68` + `game.awd.round` | 参数名改为 `round` |
| 3 | Checker 通过 Process.Start 执行 | `AwdCheckerService.cs:48` | 改为容器内执行或 API 调用 |
| 4 | 排行榜 AWD 分数计算为简化实现 | `AwdPlayerController.cs:274-287` | 使用真实 Checker 数据 |
| 5 | AwdRoundService 直接注入 IServiceProvider | `AwdRoundService.cs:105` | 改为构造函数注入 |

---

**文档完成时间：** 2026-06-06
**核验状态：** 3 agent 交叉验证通过
**遗漏修正：** +AppDbContextModelSnapshot.cs, +IsAwdService(), +ScoreboardItemModal.tsx
**Bug 记录：** requireAwd 死字段, game.awd.round 参数名不匹配
