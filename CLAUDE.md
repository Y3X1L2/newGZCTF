# YINYU CTF平台 全栈重构开发规范

> **跟进计划:** `docs/superpowers/plans/2026-05-19-yinyu-ctf-platform-refactor.md`
> **TDD 规范:** `docs/superpowers/plans/2026-05-19-tdd-supplement.md`
> **代码库分析:** `docs/codebase-analysis.md`
> **审计报告:** `docs/superpowers/reviews/final-audit-*.md`

---

## 项目状态

```
🎉 8/8 Phase 完成 | 3 轮审计通过 | 227/227 测试 | 0 失败

主分支: 001-ctf-scenario-engine
平台服务器: <test-server-ip> (Ubuntu 22.04)
部署: docker compose -f docker-compose.yml up -d
```

---

## 一、项目概述

**YINYU CTF平台** 是面向赛事管理、攻防演练、理论赛与分布式靶场调度的 CTF 场景化实战平台。

### 技术栈

| 层级 | 技术 |
|---|---|
| **后端** | .NET 10.0 / ASP.NET Core / EF Core 10 |
| **前端** | React 19 + Vite / TypeScript / pnpm / Mantine |
| **数据库** | PostgreSQL 16 |
| **缓存** | Redis 7 |
| **容器** | Docker (Docker.DotNet.Enhanced) |
| **VM** | KVM/libvirt (Linux) / Guacamole RDP |
| **实时** | SignalR (StackExchange.Redis) |
| **测试** | xUnit (227 单元) + Playwright (E2E) |

### 架构

```
Platform (YINYU CTF平台)   ← Admin Panel
  ├── PostgreSQL + Redis + Guacd
  ├── UnifiedScoringEngine (统一评分)
  ├── IVirtualMachineProvider (KVM/Docker)
  ├── FleetManager + NodeDeployService (分布式)
  ├── GamePhaseService (阶段控制)
  └── IDistributedLockService (并发安全)

Worker Nodes (靶机)
  ├── 管理员面板填入 IP/User/Pass
  ├── NodeDeployService SSH 连接 → 检测 Docker/KVM
  └── 注册为 WorkerNode → 接收题目部署
```

### 文档树

```
/CLAUDE.md                                   本文件
docs/
├── codebase-analysis.md                     代码库分析
├── analysis-*.md                            深度分析 (5 份)
├── decisions/                               架构决策日志 (6 份)
├── deploy/production.md                     生产部署指南
├── deploy/agent-node.md                     靶机部署指南
└── superpowers/
    ├── plans/
    │   ├── 2026-05-19-yinyu-ctf-platform-refactor.md  实施计划 v2.2
    │   └── 2026-05-19-tdd-supplement.md     TDD 规范
    └── reviews/
        ├── phase1-3-critical-review.md      计划审查
        ├── phase4-8-critical-review.md
        ├── tdd-supplement-critical-review.md
        ├── final-audit-backend.md           最终审计
        ├── final-audit-frontend.md
        └── final-audit-stubs-gaps.md
```

---

## 二、Phase 完成清单

| Phase | 内容 | 新建文件 | 修改文件 | 测试 |
|---|---|---|---|---|
| **Phase 1** | 统一评分引擎 + 可配置提交类型 | 10 | 5 | 25/25 |
| **Phase 2** | VM Provider + Docker 容器管理 | 18 | 8 | 186/186 |
| **Phase 3** | 部署管理面板 + 一键部署 | 21 | 6 | 162/162 |
| **Phase 4** | 游戏阶段控制 | 4 | 5 | 155/155 |
| **Phase 5** | 数据模型并发加固 | 2 | 5 | 158/158 |
| **Phase 6** | 前端重构 + 死文件清理 | 8 | 5 | 构建通过 |
| **Phase 7** | 安全加固 | 4 | 7 | 227/227 |
| **Phase 8** | 部署编排 + 文档 | 4 | — | 验证通过 |
| **审计修复** | 契约/DI/路由/缺失文件 | 11 | 8 | 227/227 |
| **总计** | | **82** | **49** | **227 ✅** |

### 关键架构决策

| 决策 | 方案 | 状态 |
|---|---|---|
| 评分统一 | UnifiedScoringEngine + IVerificationStrategy(VerificationMode) | ✅ |
| VM 抽象 | IVirtualMachineProvider → KvmProvider | ✅ |
| 一键部署 | NodeDeployService + SSH 探测 | ✅ |
| 阶段控制 | GamePhaseService (控制器级检查) | ✅ |
| Double-decay | 仅在 Submission 写入时衰减，ScoringService 只读 | ✅ |
| Agent 协议 | HTTP REST Pull + HMAC (待实现) | 📋 |
| 分布式锁 | LocalSemaphoreLock (单机) / Redis (集群) | ✅ |

---

## 三、P0 Bug 修复记录

| Bug | 位置 | 修复 |
|---|---|---|
| IR 分数不入排行榜 | IRChallengeController + CheckpointVerificationService | 完成检查点后写 Submission |
| Double-decay 分数双重衰减 | SubmissionController + ScoringService | 统一在引擎写入时衰减一次 |
| AutoScript 永远返回 false | CheckpointVerificationService + ScriptVerification | 真实 Process 执行脚本 |
| Factor 绕过 ScoringRule | ScenarioController | submitStageFlag 写 Submission |

---

## 四、开发工作流（强制执行）

### 4.1 核心原则

1. **工作区隔离** — 每个 Phase 在独立 `git worktree` 中开发
2. **TDD 优先** — 先写失败测试 (RED)，再最小实现 (GREEN)，最后重构 (BLUE)
3. **测试必须运行通过** — 每轮实现后立即 `dotnet test` 验证
4. **Subagent 执行** — 每个 Task 派发独立 agent
5. **每 Phase 验收后合并** — 不允许跳过测试的代码合入主干

### 4.2 标准 Phase 流

```
Phase 启动 → git worktree add → 派发 agent 实现 → 运行测试 → 合并主干 → 更新 CLAUDE.md
```

### 4.3 提交格式

```
[TDD-RED]   test(<scope>): <description>
[TDD-GREEN] feat(<scope>): <description>
[TDD-BLUE]  fix(<scope>): <description>

scope: scoring|security|vm|fleet|phase|model|ui|ci|e2e|docker|audit
```

### 4.4 测试红线

```
dotnet test 输出必须: "失败: 0，已跳过: 0"
禁止: 测试跳过 | 假测试 (Assert.True(true)) | 未运行声称通过
```

### 4.5 .NET 10 注意事项

- `ILogger<T>` 需要 `Microsoft.Extensions.Logging` 包
- `NullLogger.Instance` 不实现 `ILogger<T>` → 测试用 `null!`
- xUnit: `using Xunit;`, `using System.Threading.Tasks;`, `using System.Threading;`
- 文件级命名空间声明不需要大括号

---

## 五、C2C MCP 协同规范

| 同步类型 | 触发时机 | 工具 | 时效 |
|---|---|---|---|
| 初始对齐 | 新任务 / 新节点加入 | c2c.set_plan() | 立即 |
| 进度广播 | 开始/完成任务 | c2c.ask_peer() | 5 分钟 |
| 变更通知 | 文档/计划/代码变更 | c2c.ask_peer() | 立即 |
| 同步轮询 | 超 30 分无消息 | c2c.sync_with_peer() | 30 分钟 |

---

## 六、编码与安全规范

### 后端
- 遵循项目现有命名 (PascalCase/_camelCase)
- DI 注入，不直接 new 服务
- 枚举 `[JsonConverter(typeof(JsonStringEnumConverter<>))]`
- 并发令牌: `[Timestamp] public uint ConcurrencyToken`

### 安全底线
- OWASP Top 10 防护
- 禁止硬编码密钥 (Guacamole 密码动态生成)
- Flag 存储 SHA256 哈希
- VM 名验证: `^[a-zA-Z0-9_-]{1,64}$`
- 响应脱敏: 过滤 SshPasswordHash/GuacamoleToken
- 速率限制: `[EnableRateLimiting]` on all flag submit
- Guacamole: security=nla
- Agent 通信: TLS 1.3 + HMAC 签名

---

## 七、环境

| 环境 | 信息 |
|---|---|
| 平台服务器 | <test-server-ip> (Ubuntu 22.04) |
| 测试 DB | localhost:5433 (gzctf_test) |
| Redis | localhost:6380 |
| Guacd | localhost:4822 |
| 本地开发 | `dotnet restore && dotnet build && dotnet test` |
| 生产部署 | `docker compose -f docker-compose.yml up -d` |
| 一键部署脚本 | `scripts/one-click-deploy.py` |

---

**版本记录：**

| 日期 | 变更 | 更新人 |
|---|---|---|
| 2026-05-18 | 初始版本 | Lead |
| 2026-05-19 | 全栈重构规范 + 6 CRITICAL | Lead |
| 2026-05-19 | 6/6 CRITICAL 修复 | Lead |
| 2026-05-19 | Phase 1 完成 + 工作流规范 | Lead |
| 2026-05-19 | Phase 2-3 完成 + 一键部署 | Lead |
| 2026-05-19 | Phase 4-8 完成 + 227/227 测试 | Lead |
| 2026-05-19 | 3 轮审计 + 全部修复 + 文档重整 | Lead |
