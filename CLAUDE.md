# newGZCTF 全栈重构开发规范

> 本文件为团队核心规范，涵盖 C2C 协同协议、全栈重构计划、架构决策、TDD 测试纪律与代码审查标准。
> **跟进计划:** `docs/superpowers/plans/2026-05-19-newGZCTF-refactor.md`
> **TDD 规范:** `docs/superpowers/plans/2026-05-19-tdd-supplement.md`
> **代码库分析:** `docs/codebase-analysis.md`
> **深度审查报告:** `docs/superpowers/reviews/phase1-3-critical-review.md`、`docs/superpowers/reviews/phase4-8-critical-review.md`、`docs/superpowers/reviews/tdd-supplement-critical-review.md`

---

## 项目状态

```
当前阶段: Phase 3 ✅ 完整完成 → 进入 Phase 4
当前分支: 001-ctf-scenario-engine（主）
工作区: 
  .worktrees/phase1-scoring      (Phase 1: 25/25 ✅)
  .worktrees/phase2-vm-docker    (Phase 2: 186/186 ✅)
  .worktrees/phase3-deploy       (Phase 3: 162/162 ✅)
测试服务器: 203.195.157.191 (Ubuntu 22.04)

Phase 2 产出（最终）: 18 Create + 8 Modify，186 测试
Phase 3 产出（最终）: 17 Create + 3 Modify，162 测试
一键部署: scripts/one-click-deploy.py（Python, 填入IP/账号/密码）
```

---

---

## 零、开发工作流（强制执行）

> **Phase 1 验证通过的工作流，后续所有 Phase 必须严格遵守。**

### 0.1 核心原则

```
1. TDD 优先 —— 先写失败测试（RED），再最小实现（GREEN），最后重构（BLUE）
2. 测试必须运行通过 —— 每轮实现后立即在本地或测试服务器上跑测试
3. 隔离工作区 —— 每个 Phase 在独立 git worktree 中开发
4. Subagent 执行 —— 每个 Task 派发独立 agent 实现，控制器做 review
5. 每 Phase 完成后合入主分支
```

### 0.2 标准 Phase 执行流

```
┌──────────────────────────────────────────────────────────────┐
│ Phase 启动                                                    │
│   ├── 读计划 → 提取所有 Task 文本和上下文 → 创建 TodoWrite        │
│   ├── 创建 git worktree + feature 分支                         │
│   └── 确保开发环境 ready（dotnet/node 等）                       │
├──────────────────────────────────────────────────────────────┤
│ Per Task 循环                                                 │
│   ├── 1. 派发 implementer subagent（提供完整 task 文本 + 路径）   │
│   │      · agent 读现有代码 → 创建文件 → 写测试 → 写实现          │
│   │      · agent 自检：编译通过？测试全绿？                       │
│   │      · agent 报告: DONE / DONE_WITH_CONCERNS / BLOCKED     │
│   ├── 2. 控制器处理 agent 结果                                  │
│   │      · DONE → 进步骤 3                                     │
│   │      · DONE_WITH_CONCERNS → 审查顾虑 → 修复 → 再次验证      │
│   │      · BLOCKED → 提供上下文 → 重试 或 拆解 task              │
│   ├── 3. 控制器运行测试验证                                     │
│   │      · dotnet test --filter "FullyQualifiedName~Task"      │
│   │      · 必须 0 失败，0 跳过                                   │
│   ├── 4. 修复编译错误和测试失败                                  │
│   │      · 缺失 using → 立即补充                                │
│   │      · API 不兼容 → 按项目已有模式调整                        │
│   │      · 重复声明 → 检查外部作用域                              │
│   ├── 5. Commit（TDD 规范格式）                                  │
│   │      · [TDD-RED] test(scope): description                  │
│   │      · [TDD-GREEN] feat(scope): description                │
│   │      · [TDD-BLUE] fix(scope): description                  │
│   └── 6. 标记 task 完成 → 进入下一 task                          │
├──────────────────────────────────────────────────────────────┤
│ Phase 验收                                                    │
│   ├── dotnet test 全部通过（0 失败）                             │
│   ├── git log 确认所有提交格式正确                               │
│   ├── CLAUDE.md 更新进度                                        │
│   └── 进入下一 Phase                                            │
└──────────────────────────────────────────────────────────────┘
```

### 0.3 Git 工作区规范

```bash
# 每个 Phase 开始时:
project_root=$(git rev-parse --show-toplevel)
git worktree add "$project_root/.worktrees/phase<N>-<name>" -b feature/phase<N>-<name>
cd "$project_root/.worktrees/phase<N>-<name>"

# 每个 Phase 完成时:
# 选项 A: 合入主分支 (git merge feature/phase<N>-<name>)
# 选项 B: 保持独立分支，多个 Phase 后批量合入
```

### 0.4 提交信息格式（强制）

```
[TDD-RED]   test(<scope>): <description>       ← 先写失败测试
[TDD-GREEN] feat(<scope>): <description>       ← 最小实现通过测试
[TDD-BLUE]  fix(<scope>): <description>        ← 修复/重构，测试仍绿

scope: scoring|security|vm|fleet|phase|model|ui|ci|e2e
```

### 0.5 测试执行标准

```
每个 Task 完成后:
  dotnet test <project> --filter "FullyQualifiedName~<TaskPrefix>" --no-restore
  
输出必须满足:
  "通过! - 失败: 0，通过: N，已跳过: 0，总计: N"

禁止:
  · 测试跳过 (! 或 Skip="reason")
  · 忽略编译警告（导致测试未编译）
  · 测试引用不存在的方法/类型
  · 在当前环境未安装 SDK 的情况下声称测试通过
```

### 0.6 Phase 1 实战记录

```
Phase 1: 统一评分引擎
  工作区: .worktrees/phase1-scoring
  分支: feature/phase1-scoring-engine

  Task 1.1  ScoreDecayCalculator         agent → DONE_WITH_CONCERNS → 修复 idempotency test → 8/8 ✅
  Task 1.2  IVerificationStrategy + 5    agent → DONE → 6/6 ✅
  Task 1.3  UnifiedScoringEngine         agent → DONE → 2/2 ✅
  Task 1.4  控制器接入 + AutoScript 修复  agent → DONE → 编译错误(重复变量+缺失using) → 修复 → 全部 ✅

  编译器问题修复记录（避免重复）:
    · .NET 10: ILogger<T> 需要 Microsoft.Extensions.Logging 包
    · .NET 10: NullLogger.Instance 不实现 ILogger<T> → 测试中用 null!
    · xUnit Fact/Theory 需要 using Xunit;
    · async Task 需要 using System.Threading.Tasks;
    · CancellationToken 需要 using System.Threading;
    · 文件级命名空间声明不需要大括号
    · 变量不要在外层 scope 重复声明

  最终: 25/25 测试通过（0 失败，593ms）
  代码: +726/-56 行，18 个文件，5 个提交
```

### 0.7 禁止事项

```
· 跳过测试直接写实现代码
· 在测试运行前声称任务完成
· 在没有 dotnet SDK 的环境声称"编译应该没问题"
· 修改生产代码而不更新对应测试
· 在同一个 task 中混合多个不相关的更改
· 跳过 subagent 的顾虑而不解释原因
· 保留已知会失败的测试（Apply_IsIdempotent 教训）
· 在 .NET 10 项目中使用 ILogger<T> 而不验证 NuGet 包兼容性
```

---

## 一、项目概述

**newGZCTF** 基于 GZCTF v1.8.3 二次开发，目标是 CTF 场景化实战平台——支持 Windows/Linux 多靶机类型、多 Flag 混合提交、分布式多节点管理。

### 技术栈

| 层级 | 技术 |
|---|---|
| **后端** | .NET 10.0 / ASP.NET Core / C# |
| **前端** | React 19 + Vite / TypeScript / pnpm / Mantine |
| **数据库** | PostgreSQL 16 (Npgsql + EF Core 10) |
| **容器** | Docker (Docker.DotNet.Enhanced) + Kubernetes |
| **VM** | KVM/libvirt (Linux) / Guacamole RDP |
| **实时** | SignalR (StackExchange.Redis) |
| **缓存** | Redis 7 |
| **测试** | xUnit / Playwright / Respawn / TestContainers |

### 文档树

```
/CLAUDE.md                                    本文件（核心规范）
docs/
├── codebase-analysis.md                      代码库全面分析报告
├── analysis-vm-infra.md                      VM 基础设施深度分析
├── analysis-scoring.md                       评分管线深度分析
├── analysis-api.md                           API 表面深度分析
├── analysis-datamodel.md                     数据模型深度分析
├── analysis-frontend.md                      前端 UI 深度分析
├── development-standards.md                  开发规范
├── decisions/                                架构决策日志
└── superpowers/
    └── plans/
        ├── 2026-05-19-newGZCTF-refactor.md   全栈重构实施计划 v2.1
        ├── 2026-05-19-tdd-supplement.md      完整 TDD 测试驱动规范
        └── reviews/
            ├── phase1-3-critical-review.md   评分/安全/VM 计划审查
            ├── phase4-8-critical-review.md   分布式/阶段/数据/前端审查
            └── tdd-supplement-critical-review.md  TDD 补充审查
```

---

## 二、C2C MCP 协同规范

### 2.1 节点角色

- **主节点 (Lead)**：需求分析、架构设计、任务拆分、进度把控
- **从节点 (Follow)**：具体实现、测试编写、文档同步

### 2.2 信息同步协议

```
同步类型        触发时机                    工具                    时效要求
───────────    ─────────────────────       ─────────────────      ────────
初始对齐        新任务启动 / 新节点加入       c2c.set_plan()         立即
进度广播        自己开始 / 完成一个任务       c2c.ask_peer()         5分钟内
变更通知        文档 / 计划 / 代码变更        c2c.ask_peer()         立即
同步轮询        超过 30 分钟无消息           c2c.sync_with_peer()   30分钟阀值
计划审查        进入新 Phase 前              提交审查文档             双方确认后
```

### 2.3 Git 协作规范

**分支策略：**
- `main` — 稳定分支，仅通过 PR 合入
- `feature/scoring-engine` — Phase 1 评分引擎
- `feature/vm-provider` — Phase 3 VM 抽象
- `feature/fleet-manager` — Phase 4 分布式调度
- `feature/game-phase` — Phase 5 阶段控制
- `fix/*` — 修复分支

**提交信息格式（TDD 强制前缀）：**
```
[TDD-RED] <type>(<scope>): <description>       # 先写失败测试
[TDD-GREEN] <type>(<scope>): <description>     # 最小实现通过测试
[TDD-BLUE] <type>(<scope>): <description>      # 重构，测试仍绿

# type: feat|fix|refactor|test|docs|chore
# scope: scoring|security|vm|fleet|phase|model|ui|ci
# 示例: [TDD-GREEN] feat(scoring): implement ScoreDecayCalculator
```

**每个 Phase 初始时：** 当前节点发布审查文档 → 对方审阅完毕 → 双方确认后开始编码

---

## 三、重构计划（v2.1）与审查发现

完整实施计划见 `docs/superpowers/plans/2026-05-19-newGZCTF-refactor.md`

### 8 个 Phase（按实施顺序）

| Phase | 内容 | 时间估计 | 理由 |
|---|---|---|---|
| **Phase 1** | 统一评分引擎 + 可配置提交类型 | 2-3 天 | 基础，不锁定其他进度 |
| **Phase 2** | VM Provider + Docker 容器管理 | 4-5 天 | 核心目标：本地 VM + Docker 一键部署 |
| **Phase 3** | 部署管理与面板（一键部署/清理/排队/监控） | 5-6 天 | 与 Phase 2 形成完整管理链路 |
| **Phase 4** | 游戏阶段控制 | 1 天 | 管理比赛流程 |
| **Phase 5** | 数据模型并发加固 | 1-2 天 | 功能稳定后再加固 |
| **Phase 6** | 前端重构 + 管理面板 | 2-3 天 | UI 与功能同步推进 |
| **Phase 7** | 安全加固 | 1 天 | 最后统一做，避免多次修复 |
| **Phase 8** | 部署编排 + 开发规范 | 1-2 天 | 收尾文档 |

### 关键审查发现（开始实施前必须修复）

以下 6 项 CRITICAL 问题来自 3 轮深度计划审查：

#### CRITICAL-1: Agent ↔ 管理端通信协议未定义

**问题**: Phase 4 计划创建 `GZCTF.Agent` 独立进程，但**完全没有定义通信协议**。当前 `DockerProvider`/`DockerManager`/`ContainerOrchestrator` 三者共享一个 `DockerClient` 单例（`ContainerServiceExtension.cs:44`），`FleetManager` 选择远程节点后不知道通过什么渠道下发指令。

**修复方案**: Agent 与管理端通过 HTTP REST API + HMAC 签名通信，管理端暴露一套 `/api/v1/nodes/{id}/commands` 端点接收 Agent 轮询取指令，Agent 本地执行后回调结果。

**涉及文件**: 需要新增通信协议设计文档，修改 `ContainerServiceExtension.cs` 和 `FleetManager.cs`

#### CRITICAL-2: GuacamoleProxy 方法签名变更未同步

**问题**: `IRChallengeController.cs:420` 调用 `GuacamoleProxy.CreateConnectionAsync(vmName, vmIp, vncPort)`，但计划将其改为 5 参数，直接编译失败。同时该控制器直接注入 `VmManager`，计划未列为修改目标。

**修复方案**: Phase 3 必须将 `IRChallengeController.cs` 列为 `Modify` 目标，更新其 `CreateEnvironmentAsync` 方法

#### CRITICAL-3: GamePhaseMiddleware 无法提取 gameId

**问题**: `IRChallengeController` 路由 `/api/v1/ir-challenges` 和 `ScenarioController` 路由 `/api/v1/scenarios` 中**不包含 gameId**，中间件无法从 URL 判断当前请求属于哪个游戏

**修复方案**: 不在中间件层做 gameId 提取，改成在控制器端通过 `GameChallenge.GameId` 查数据库检查阶段状态，或中间件只处理 `/api/game/{id}` 路径

#### CRITICAL-4: Strategy 模式调度键错误

**问题**: 计划用 `ScoringSubmissionType` 作为策略调度键，但 `SubmissionType`（Flag/Writeup/IP/Credential）和 `VerificationMode`（AutoExact/AutoRegex/AutoScript）是正交轴。同一个 Flag 提交可以配置 AutoExact 或 AutoRegex，按 SubmissionType 调度无法区分。

**修复方案**: `IVerificationStrategy` 接口 `VerifyAsync` 改为接受 `(string answer, ScoringRule rule, ...)` 参数，由调用方传入 `VerificationMode` 选择策略

#### CRITICAL-5: 测试基础设施不兼容

**问题**: 
- TDD 补充的 `GZCTFTestFixture` 会与现有 `IntegrationTestCollection` 共享同一个 `GZCTFApplicationFactory`（同一个 PostgreSQL container），`ResetDatabaseAsync` 会破坏其他测试状态
- Moq 不是测试项目的直接依赖
- Respawn 不是依赖
- `CreateAuthenticatedClient` 使用请求头认证但实际上不认证
- 现有测试工厂配置 `DisableRateLimit=true` 导致速率限制测试永远不触发 429

**修复方案**: TDD 补充文档需要重新设计测试基础设施，使用 `TestContainers` 独立数据库实例，添加 `Moq`/`Respawn` 依赖，通过 `WebApplicationFactory` 自定义认证 handler

#### CRITICAL-6: double-decay 是架构问题，非算法问题

**问题**: `ScoreDecayCalculator` 作为 `static` 类不能解决 double-decay——真正的根本原因是 `SubmissionController` 在创建时衰减一次、`ScoringService` 在统计时再衰减一次。即使使用同一个 `static` 方法，两处还是都会调用它。

**修复方案**: `ScoringService.CalculateTotalScoreAsync` 改为**读取 `Submission.Score` 上的已衰减值**，不再重新应用衰减。ScoreDecay 只在 **写入 Submission 时**（`UnifiedScoringEngine.ProcessSubmissionAsync`）应用一次。

### Phase 实施前提条件

**每个 Phase 开始前** 必须满足以下检查清单：

- [ ] 上一 Phase 所有 [TDD-GREEN] 测试在 CI 上通过
- [ ] 本 Phase 的审查发现已记录为决策日志
- [ ] 涉及文件清单已与协作节点确认
- [ ] 部署变更（如有）已在测试服务器验证

---

## 四、TDD 测试纪律

TDD 规范详见 `docs/superpowers/plans/2026-05-19-tdd-supplement.md`

### 红线标准

```
每次提交:
  [TDD-RED]   ← 测试先，预期 FAIL
  [TDD-GREEN] ← 最小实现，预期 PASS
  [TDD-BLUE]  ← 重构优化，测试仍 PASS

每个 Phase:
  全部 83+ RED 测试 → GREEN → 集成测试green → 进入下一 Phase

禁止:
  · 提交中存在未解决的 RED 测试
  · 跳过测试 (Skip="reason")
  · 修改测试而不提交对应的 GREEN 实现
```

### 测试金字塔

```
UNIT (Phase 1-6)
├── ScoreDecayTests              — 20+ 边界用例，参数化矩阵
├── VerificationStrategyTests    — 8 种策略，15+ 场景
├── VmSecurityTests              — 注入防御
├── WeightedSchedulerTests       — 4 种调度场景
├── QueueManagerTests            — 排队/出队
├── PortCapacityTrackerTests     — 容量追踪
├── AgentPortAllocatorTests      — 端口分配/释放
├── IRChallengeModelTests        — 敏感信息脱敏
└── CONCURRENT 100+ per commit   ← TDD 强制要求

INTEGRATION (Phase 1-6)
├── ScoringEngineIntegrationTests — 11 个集成场景
├── RateLimitTests               — 限流验证
├── VmLifecycleTests             — VM 全生命周期（需要 KVM）
├── LocalImageImporterTests      — 导入/分发
├── GamePhaseTests               — 阶段状态
└── DataIntegrityTests           — FK/约束/xmin

E2E (Phase 7)
├── scoring-engine.spec.ts       — 8 个关键流
├── vm-lifecycle.spec.ts         — 6 个场景
├── fleet-scheduling.spec.ts     — 7 个场景
├── concurrency.spec.ts          — 5 个并发场景
├── game-phase.spec.ts           — 4 个阶段场景
├── performance.spec.ts          — 6 个性能基线
└── TOTAL 36 E2E scenarios
```

---

## 五、架构决策记录

所有架构决策在 `/docs/decisions/YYYY-MM-DD-topic.md` 记录

### 活跃决策

（暂无——全部 6 项 CRITICAL 已修复，见下方决策日志）

| 决策 | 日期 | 状态 |
|---|---|---|
| [Agent通信协议选择](./docs/decisions/2026-05-19-agent-protocol.md) | 2026-05-19 | ✅ HTTP REST Pull + HMAC |
| [GuacamoleProxy 签名变更](./docs/decisions/2026-05-19-guacamole-signature.md) | 2026-05-19 | ✅ Phase 3 同步 IRChallengeController |
| [GamePhase 架构方案](./docs/decisions/2026-05-19-gamephase-arch.md) | 2026-05-19 | ✅ 控制器层面检查替代中间件 |
| [Strategy 调度键设计](./docs/decisions/2026-05-19-strategy-key.md) | 2026-05-19 | ✅ VerificationMode 替代 SubmissionType |
| [测试基础设施选型](./docs/decisions/2026-05-19-test-infra.md) | 2026-05-19 | ✅ 独立 TestContainers + 真认证 + Moq/Respawn |
| [Double-decay 修复架构](./docs/decisions/2026-05-19-double-decay.md) | 2026-05-19 | ✅ 只在写入时衰减一次 ScoringService 读已衰减值 |

---

## 六、开发环境与资源

### 本地开发

```bash
# 后端
dotnet restore src/GZCTF.slnx
dotnet build src/GZCTF.slnx

# 前端
cd src/GZCTF/ClientApp
pnpm install
pnpm run dev -- --host

# 数据库 (PostgreSQL + Redis)
docker compose up -d db redis
```

### 测试服务器

```
主机: 203.195.157.191
用户: ubuntu
端口: 5433 (PostgreSQL) / 6380 (Redis) / 4822 (Guacd)
```

### Windows 靶机资源

- `D:\wkdb-winserver2012-挖矿病毒模拟` — 本地 IR VM 镜像
- 部署测试需通过 `LocalImageImporter.ImportFromLocalPathAsync()` 导入

### 版本信息

- 当前版本: 1.8.3
- .NET SDK: 10.0
- 许可证: AGPLv3（带 GZCTF 附加条款）

---

## 七、编码与安全规范

### 后端 (C#)

- 遵循项目现有命名规范（PascalCase 方法/属性，_camelCase 私有字段）
- Controller 返回 `Task<IActionResult>` 或 `Task<ActionResult<T>>`
- 数据库操作通过 Repository 模式
- 新模型配置 EF Core Fluent API（`AppDbContext.cs`）
- 枚举使用 `[JsonConverter(typeof(JsonStringEnumConverter<>))]`
- **并发令牌统一使用 xmin**（`[Timestamp] public uint ConcurrencyToken`）
- **依赖注入不直接 new 服务**，使用 constructor injection

### 安全底线（强制）

- OWASP Top 10 防护
- 禁止硬编码密钥与凭据（Guacamole 密码需动态生成）
- 用户输入必须验证（后端 + 前端双重）
- Flag 存储必须加密或哈希（SHA256）
- 容器隔离检查网络模式设置
- **VM 注入防御**: VM 名必须通过 `SanitizeVmName` 校验（只允许 `[a-zA-Z0-9_-]`）
- **响应脱敏**: `IRInstance.AccessDetails` 返回前过滤 `SshPasswordHash`、`GuacamoleToken`
- **速率限制**: 所有 Flag/答案提交端点必须加 `[EnableRateLimiting("Submit")]`
- **Agent 通信**: HMAC 签名 + TLS 1.3

---

## 八、审查与修正状态

2026-05-19：3 轮深度计划审查完成，**6/6 CRITICAL 问题已修复**（对应 6 个决策日志）。

| CRITICAL | 问题 | 修正 |
|---|---|---|
| 1 | Agent 通信协议未定义 | Phase 4 Task 0 新增 HTTP REST Pull + HMAC 协议设计 |
| 2 | GuacamoleProxy 签名变更未同步 | Phase 3 Modify 加入 IRChallengeController |
| 3 | GamePhaseMiddleware 无法提取 gameId | 改为控制器层 GamePhaseService + Phase 5 Modify 加入 3 控制器 |
| 4 | Strategy 调度键选错维度 | HandledType → HandledMode (VerificationMode) |
| 5 | 测试基础设施不兼容 | IsolatedTestFixture + TestContainers + 真认证 + Moq/Respawn |
| 6 | double-decay 是架构问题 | ScoringService 只读已衰减值；MaxAttempts/ScoreDecay 合并到 ScoringRule |

**可进入 Phase 1 实施。**

---

**版本记录：**

| 日期 | 变更内容 | 更新人 |
|---|---|---|
| 2026-05-18 | 初始版本 | Lead |
| 2026-05-19 | 更新为全栈重构规范，加入 6 项 CRITICAL 审查发现 | Lead |
| 2026-05-19 | 完成 6/6 CRITICAL 审查修复，计划 v2.2 — 6 个决策日志、Agent 协议设计、TDD 测试基建重写 | Lead |
| 2026-05-19 | Phase 1 完成 — 25/25 测试通过，新增第零章"开发工作流"，记录 TDD+Subagent+隔离工作区流程 | Lead |
