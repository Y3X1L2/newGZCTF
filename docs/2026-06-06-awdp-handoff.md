# AWDP 开发 Handoff 文档

> 日期：2026-06-06
> 分支：`awdp-rewrite`
> 目标：在 newGZCTF 平台完全重写 AWDP (Attack with Defense Plus) 比赛模式

---

## 一、项目背景

newGZCTF 是基于 GZCTF v1.8.3 二次开发的 CTF 场景化实战平台。本次任务是在原有基础上新增 AWDP 比赛形式，采用**完全重写**策略（非在旧 AWD 代码上修补）。

### AWDP 核心特征
- 队伍间互不干扰，独立环境（非互攻）
- 双阶段轮次：攻击阶段 + 修补阶段
- 修补包验证：Checker(功能) + Exp(漏洞) 双重验证
- 计分：攻击分 + 修补分 + SLA 分 - 异常扣分（非零和）
- 选手自助重置 + 一键恢复

### 设计文档
- `docs/2026-06-06-awdp-mode-design.md` — AWDP 竞赛形式调研
- `docs/2026-06-06-awdp-extension-analysis.md` — 接口对接分析
- `docs/2026-06-06-awdp-rewrite-plan.md` — 完全重写与解耦方案
- `docs/2026-06-06-awdp-development-plan.md` — 分模块开发计划（含质量保障体系）

---

## 二、当前进度

### 已完成

| 阶段 | 状态 | 说明 |
|------|------|------|
| Phase 1: 解耦 | ✅ 完成 | `GameType.AWD` → `GameType.AWDP`，所有引用已更新 |
| Phase 2: 删除旧代码 | ✅ 完成 | 14 个后端 + 4 个前端 AWD 专属文件已删除 |
| Phase 2: 全面清理 | ✅ 完成 | AppDbContext、Program.cs、IMonitorClient、GameRepository、ScoreboardModel、前端组件全部清理 |
| M1: 数据模型层 | ✅ 完成 | 8 个实体 + 13 个请求/响应模型 + 5 个枚举 + Limits 常量 |

### 待开发（M2-M13）

| 模块 | 内容 | 依赖 |
|------|------|------|
| **M2** | 数据库连接层（AppDbContext + DI） | M1 |
| **M3** | 仓库层（IAwdpRepository） | M2 |
| **M4** | 容器管理服务（AwdpInstanceService） | M3 |
| **M5** | 轮次引擎（AwdpRoundService） | M4 |
| **M6** | 验证与计分（AwdpCheckerService + AwdpScoreService + AwdpPatchService） | M5 |
| **M7** | 控制器层（Admin + Player API） | M6 |
| **M8** | 排行榜集成（GenScoreboard） | M7 |
| **M9** | 前端 API 客户端（TypeScript 类型 + API） | M7 |
| **M10** | 前端管理员面板 | M9 |
| **M11** | 前端选手面板 | M9 |
| **M12** | SignalR 实时推送 | M7 |
| **M13** | 集成验证 | 全部 |

---

## 三、已完成代码清单

### 3.1 新增文件（M1 交付物）

**数据模型（8 个）：**
- `src/GZCTF/Models/Data/AwdpService.cs` — AWDP 服务定义（含 Checker/Exp 双验证配置、分数配置、轮次配置、重置/恢复配置）
- `src/GZCTF/Models/Data/AwdpServiceInstance.cs` — 每队容器实例
- `src/GZCTF/Models/Data/AwdpRound.cs` — 轮次（含 AttackPhase/PatchPhase 双阶段时间戳）
- `src/GZCTF/Models/Data/AwdpFlag.cs` — 每轮每队每服务 Flag
- `src/GZCTF/Models/Data/AwdpCheckerTask.cs` — Checker 执行结果
- `src/GZCTF/Models/Data/AwdpPatchSubmission.cs` — 修补包提交记录
- `src/GZCTF/Models/Data/AwdpResetRecord.cs` — 重置记录
- `src/GZCTF/Models/Data/AwdpRecoveryRecord.cs` — 一键恢复记录

**请求/响应模型（1 个文件，13 个类型）：**
- `src/GZCTF/Models/Request/Game/AwdpServiceModels.cs`
  - `AwdpServiceCreateModel` / `AwdpServiceUpdateModel` / `AwdpServiceViewModel`
  - `AwdpSubmitModel` / `AwdpGameStatusModel`
  - `AwdpTeamServiceStatus` / `AwdpScoreboardItem` / `AwdpAttackLogItem`
  - `AwdpPatchStatusItem` / `AwdpServiceStatusModel` / `AwdpPatchResultModel`

### 3.2 修改的文件

- `src/GZCTF/Utils/Enums.cs` — 新增枚举：`CheckerStatus`、`AwdpRoundStatus`、`AwdpPatchStatus`、`AwdpChallengeStatus`、`AwdpResetType`；新增 `EventType.Awdp*=20-26`（7 个值）
- `src/GZCTF/Models/Limits.cs` — 新增常量：`MaxServiceNameLength`、`MaxImageNameLength`、`MaxScriptLength`、`MaxEntrypointLength`、`MaxNetworkNameLength`

### 3.3 删除的文件（旧 AWD 代码）

**后端（14 个）：**
- `Models/Data/AwdService.cs`、`AwdServiceInstance.cs`、`AwdRound.cs`、`AwdFlag.cs`、`AwdCheckerTask.cs`
- `Models/Request/Game/AwdServiceModels.cs`
- `Services/AwdRoundService.cs`、`AwdInstanceService.cs`、`AwdCheckerService.cs`、`AwdScoreService.cs`
- `Repositories/Interface/IAwdRepository.cs`、`Repositories/AwdRepository.cs`
- `Controllers/AwdAdminController.cs`、`AwdPlayerController.cs`

**前端（4 个）：**
- `ClientApp/src/Api/AwdApi.ts`
- `ClientApp/src/pages/games/[id]/Awd.tsx`
- `ClientApp/src/pages/admin/games/[id]/AwdServices.tsx`、`awd-services.tsx`

### 3.4 清理的耦合点

| 文件 | 清理内容 |
|------|----------|
| `Models/AppDbContext.cs` | 删除 5 个 DbSet + 1 个 EF 配置块 |
| `Program.cs` | 删除 6 行 DI 注册 |
| `Hubs/Clients/IMonitorClient.cs` | 删除 2 个 AWD SignalR 方法 |
| `Repositories/GameRepository.cs` | 删除 26 行 AWD 排行榜计算（保留 TODO 占位） |
| `Models/Request/Game/ScoreboardModel.cs` | 删除 `AwdScore` 字段，`Score` 改为仅 `CtfScore` |
| `ClientApp/src/Api.ts` | 删除 `GameType.AWD` → `GameType.AWDP`，删除 `awdScore` 字段 |
| `ClientApp/src/components/WithGameTab.tsx` | `isAwdGame` 判断改为 `GameType.AWDP` |
| `ClientApp/src/components/admin/WithGameEditTab.tsx` | 同上 |
| `ClientApp/src/components/admin/GameCreateModal.tsx` | GameType 选项改为 AWDP |
| `ClientApp/src/pages/admin/games/[id]/Info.tsx` | 同上 |
| `ClientApp/src/components/ScoreboardTable.tsx` | 删除 AWD 列 |
| `ClientApp/src/components/ScoreboardItemModal.tsx` | 删除 AWD 分数展示 |
| `ClientApp/src/utils/screenDemoData.ts` | 删除 `awdScore` |
| 4 个 locale 文件 | AWD 显示文本改为 AWDP |

---

## 四、技术栈与架构

### 4.1 技术栈

| 层级 | 技术 |
|------|------|
| 后端 | .NET 10.0 / ASP.NET Core / EF Core 10 |
| 前端 | React 19 + Vite / TypeScript / pnpm / Mantine |
| 数据库 | PostgreSQL 16 |
| 缓存 | Redis 7 |
| 容器 | Docker (Docker.DotNet.Enhanced) |
| 实时 | SignalR (StackExchange.Redis) |

### 4.2 项目结构

```
src/GZCTF/
├── Controllers/          # API 控制器
├── Models/
│   ├── Data/             # EF Core 实体
│   ├── Request/Game/     # 请求/响应模型
│   ├── Internal/         # 内部配置模型
│   └── AppDbContext.cs   # EF Core DbContext
├── Services/             # 业务服务
├── Repositories/         # 数据访问层
│   └── Interface/        # 仓库接口
├── Hubs/Clients/         # SignalR 接口
├── Utils/Enums.cs        # 枚举定义
├── Models/Limits.cs      # 常量定义
└── Program.cs            # DI 注册

src/GZCTF/ClientApp/
├── src/
│   ├── Api.ts            # Swagger 自动生成的 API 客户端
│   ├── Api/              # 手写的 API 客户端
│   ├── pages/            # 文件系统路由页面
│   │   ├── games/[id]/   # 选手页面
│   │   └── admin/games/[id]/  # 管理页面
│   ├── components/       # 共用组件
│   └── locales/          # i18n 翻译文件
```

### 4.3 关键架构模式

**DI 注册（Program.cs）：**
```csharp
// 仓库层：Scoped
builder.Services.AddScoped<IAwdpRepository, AwdpRepository>();

// 服务层：Scoped（除 RoundService）
builder.Services.AddScoped<AwdpInstanceService>();
builder.Services.AddScoped<AwdpCheckerService>();
builder.Services.AddScoped<AwdpScoreService>();
builder.Services.AddScoped<AwdpPatchService>();

// 轮次引擎：Singleton + HostedService
builder.Services.AddSingleton<AwdpRoundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AwdpRoundService>());
```

**EF 配置（AppDbContext.OnModelCreating）：**
- 外键关系使用 `HasOne/WithMany`
- 删除行为：父子关系用 `Cascade`，容器引用用 `SetNull`
- `ContainerId` 映射到 `ContainerId1` 列（避免与 Container 表冲突）

**容器管理复用：**
- `IContainerManager.CreateContainerAsync` — 创建容器
- `IContainerManager.DestroyContainerAsync` — 销毁容器
- `ContainerOrchestrator.CreateIsolatedNetwork` — 创建隔离网络
- Flag 通过 `GZCTF_FLAG` 环境变量注入

**前端路由：**
- 使用 `vite-plugin-pages` 文件系统路由
- 路由从 `src/pages/` 目录结构自动生成
- `/games/:id/awdp` → `pages/games/[id]/Awdp.tsx`
- `/admin/games/:id/awdp-services` → `pages/admin/games/[id]/AwdpServices.tsx`

---

## 五、质量保障体系

### 5.1 核心原则

1. **零容忍 TODO/占位/简化实现** — 每个方法必须是完整实现
2. **每轮都是最高水准** — 自我迭代是重新审视和优化，不是先写个大概再修
3. **多 agent 交叉验证** — 关键模块完成后派遣独立 agent 审查
4. **质量高于速度** — 宁可一个模块花 3 小时做到完美

### 5.2 每模块开发流程

```
Phase 1: 完整实现（零 TODO、零占位）
Phase 2: 自我审视（逐行审查异常处理、资源释放、并发安全、N+1 查询）
Phase 3: 编译验证（dotnet build / pnpm build → 0 错误）
Phase 4: 多 agent 交叉验证（8 个维度、63 项检查点）
Phase 5: 自我审视（第二轮，从新视角审查修复后的代码）
Phase 6: 最终验证（编译 + 测试）
```

### 5.3 交叉验证检查清单（8 个 Agent）

| Agent | 维度 | 检查项数 |
|-------|------|---------|
| A | 架构一致性 | 7 项 |
| B | 安全审计 | 7 项 |
| C | 逻辑完整性 | 7 项 |
| D | 集成质量 | 7 项 |
| E | 前端质量 | 13 项 |
| F | 数据库迁移 | 7 项 |
| G | API 设计 | 7 项 |
| H | 性能与资源 | 8 项 |

详细检查清单见 `docs/2026-06-06-awdp-development-plan.md` 的"质量保障体系"章节。

### 5.4 禁止事项

| 禁止 | 说明 |
|------|------|
| `// TODO:` | 不允许任何 TODO 注释 |
| `throw new NotImplementedException()` | 不允许未实现异常 |
| `return null!` | 不允许无意义的 null 返回 |
| `return []` 作为占位 | 不允许返回空集合作为占位 |
| 硬编码假数据 | 不允许测试数据混入生产代码 |
| 复制粘贴不修改 | 从旧代码复制后必须逐行审查 |
| 跳过错误处理 | 不允许空 catch 块 |

---

## 六、Skill 位置与使用规范

### 6.1 必须加载的 Skill

| 阶段 | Skill 名称 | Skill 路径 |
|------|-----------|-----------|
| **前端开发** | `frontend-design` | `superpowers:frontend-design` |
| **前端开发** | `awesome-design-md` | `awesome-design-md:awesome-design-md` |
| **代码审查** | `code-review` | `code-review:code-review` |
| **安全审查** | `security-review` | `security-review` |
| **计划编写** | `writing-plans` | `superpowers:writing-plans` |
| **TDD** | `test-driven-development` | `superpowers:test-driven-development` |
| **子 agent 驱动开发** | `subagent-driven-development` | `superpowers:subagent-driven-development` |

### 6.2 Skill 使用时机

```
后端模块开发：
  开始前 → 加载 superpowers:writing-plans（规划实现步骤）
  完成后 → 加载 code-review:code-review（代码质量审查）
  涉及安全 → 加载 security-review（安全审计）

前端模块开发：
  开始前 → 加载 superpowers:frontend-design（设计规范）
  需要参考 → 加载 awesome-design-md:awesome-design-md（设计系统）
  完成后 → 加载 code-review:code-review（代码质量审查）
```

---

## 七、下一步开发指南（M2）

### M2: 数据库连接层

**目标：** 将 M1 创建的模型注册到 AppDbContext，配置 EF 关系，注册 DI 服务。

**交付物：**
1. 修改 `Models/AppDbContext.cs` — 新增 8 个 DbSet + EF 关系配置
2. 修改 `Program.cs` — 新增 DI 注册

**EF 配置要点：**
```csharp
// AppDbContext.cs OnModelCreating 中新增：

builder.Entity<AwdpServiceInstance>(entity =>
{
    entity.Property(e => e.ContainerId)
        .HasColumnName("ContainerId1");

    entity.HasOne(e => e.Container)
        .WithMany()
        .HasForeignKey(e => e.ContainerId)
        .OnDelete(DeleteBehavior.SetNull);

    entity.HasOne(e => e.Service)
        .WithMany(e => e.Instances)
        .HasForeignKey(e => e.ServiceId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(e => e.Team)
        .WithMany()
        .HasForeignKey(e => e.TeamId)
        .OnDelete(DeleteBehavior.Cascade);
});

// 类似地配置 AwdpRound, AwdpFlag, AwdpCheckerTask, AwdpPatchSubmission, AwdpResetRecord, AwdpRecoveryRecord
```

**DI 注册（Program.cs）：**
```csharp
// 在 builder.AddCustomServices(); 之后添加：
builder.Services.AddScoped<IAwdpRepository, AwdpRepository>();
builder.Services.AddScoped<AwdpInstanceService>();
builder.Services.AddScoped<AwdpCheckerService>();
builder.Services.AddScoped<AwdpScoreService>();
builder.Services.AddScoped<AwdpPatchService>();
builder.Services.AddSingleton<AwdpRoundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AwdpRoundService>());
```

**验收标准：**
- `dotnet build --no-restore` → 0 错误
- 所有 DbSet 在 AppDbContext 中注册
- 所有 EF 关系配置正确（外键、删除行为、列映射）
- 所有 DI 服务注册正确（Scoped vs Singleton）

---

## 八、环境与命令

### 8.1 开发环境

| 环境 | 信息 |
|------|------|
| .NET SDK | 10.0.300 |
| Node.js | 前端构建需要 |
| 数据库 | PostgreSQL 16（本地测试用 localhost:5433） |
| Redis | localhost:6380 |

### 8.2 常用命令

```bash
# 后端构建
cd src/GZCTF && dotnet restore && dotnet build --no-restore

# 后端测试
cd src/GZCTF && dotnet test

# 前端构建
cd src/GZCTF/ClientApp && pnpm install && pnpm build

# Git 操作
git checkout awdp-rewrite
git add -A && git commit -m "feat(awdp): <description>"
```

### 8.3 分支信息

- 当前分支：`awdp-rewrite`
- 远程仓库：`https://github.com/Y3X1L2/newGZCTF.git`
- 基线提交：`814d45c` (Initial commit: existing project state before AWDP rewrite)

---

**Handoff 完成时间：** 2026-06-06
**当前状态：** M1 完成，M2 待开发
**编译状态：** `dotnet build` 0 错误
