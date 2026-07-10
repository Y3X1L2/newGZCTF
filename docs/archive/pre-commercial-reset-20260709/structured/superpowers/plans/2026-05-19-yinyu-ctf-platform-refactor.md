# YINYU CTF平台 全栈重构实施计划 v2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 YINYU CTF平台 从当前的多体系验证混乱、单机 VM 管理、无分布式能力的状态重构为统一评分引擎、多 hypervisor 抽象、分布式智慧调度、安全合规的 CTF 场景化实战平台

**Architecture:** 采用 Provider 抽象模式统一 VM/容器管理（`IVirtualMachineProvider` / `IContainerManager` 并行），通过 `UnifiedScoringEngine` 统一七大验证体系，`FleetManager` + `RemoteAgent` 实现分布式智慧调度与队列，`GamePhaseController` 支持比赛阶段粒度控制。所有变更保持向后兼容，新功能通过 v1 API 扩展而非修改原有端点语义

**Tech Stack:** .NET 10.0 / ASP.NET Core / EF Core 10 / PostgreSQL / React 19 + TypeScript + Vite + pnpm / Docker / Guacamole / libvirt / KVM

---

## 完整比赛场景覆盖清单（更新 v2）

```
场景 1  ─ 传统 CTF StaticAttachment        选手下载附件 → 本地解题 → 提交 Flag                    (FlagChecker)
场景 2  ─ 传统 CTF StaticContainer          选手启动 Docker → 访问容器 → 提交 Flag                  (FlagChecker + Docker)
场景 3  ─ 传统 CTF DynamicAttachment        自动分发团队专属附件 → 提交 Flag                        (FlagChecker)
场景 4  ─ 传统 CTF DynamicContainer         自动创建团队专属容器 → 注入动态 Flag → 提交 Flag         (FlagChecker + Docker)
场景 5  ─ Scenario 多阶段                   选手创建实例 → 接续完成阶段 → 提交多种类型答案           (ScoringEngine + Docker/K8s/VM)
场景 6  ─ Scenario 练习模式                 同上但无分数                                            (ScoringEngine)
场景 7  ─ IR Linux（SSH）                  选手注册时段 → 创建实例 → SSH 登入 → 完成多项检查点       (ScoringEngine + Docker Agent)
场景 8  ─ IR Windows（RDP）                选手注册时段 → 创建实例 → RDP 登入 → 完成多项检查点       (ScoringEngine + KVM/Guacamole)
场景 9  ─ IR 自动验证（AutoCommand）        后台 SSH → 执行命令 → 匹配输出 → 自动计分                (ScoringEngine)
场景10  ─ IR 自动验证（AutoScript）         后台执行脚本 → 含内部 API 校验 → 自动计分                (ScoringEngine)
场景11  ─ 多 Flag 混合提交                  管理员配置提交类型（Flag/Writeup/IP/Credential/File）      (ScoringEngine + ScoringRule)
场景12  ─ IR 文件提交（病毒样本）            选手完成任务 → 上传样本文件 → 管理员或自动校验           (ScoringEngine + FileUpload)
场景13  ─ Writeup 人工审核                   选手提交 → 列队待审 → 管理员审核 → 评分                  (ScoringEngine + ManualReview)
场景14  ─ 分数衰减                          多次提交按配置衰减（None/Half/Linear）                   (ScoringEngine + ScoreDecay)
场景15  ─ 排行榜实时更新                     每次得分后通过 SignalR 广播                              (LeaderboardService + SignalR)
场景16  ─ 管理员批量操作                     创建/更新/删除 IR/Scenario → 批量导入导出                 (GameExportImport)
场景17  ─ Windows VM 本地镜像导入            从服务器本地路径导入 VM 镜像（qcow2/ova/vmdk 目录）       (ImageStorage + KvmProvider)
场景18  ─ Windows VM 生命周期                KVM 模板克隆 → 启动 → IP 轮询 → RDP 连接 → 计分 → 销毁   (KvmProvider + Guacamole)
场景19  ─ 分布式智慧调度                      节点负载监控 → 自动选择最优节点 → 创建题目环境           (FleetManager + WeightedScheduler)
场景20  ─ 多实例端口管理                      单节点多容器/多 VM 用端口区分 → 自动分配 + 回收          (PortManager)
场景21  ─ 排队机制                            全部节点过载 → 请求入队 → 释放资源 → 自动出队           (QueueManager)
场景22  ─ 服务器压力监控                       实时 CPU/内存/容器数/VM 数 → WebSocket → 管理面板         (HealthCheckService + SignalR)
场景23  ─ 游戏阶段控制                         管理员控制比赛阶段：CTF 开 / IR 关 / Scenario 关         (GamePhase)
场景24  ─ 并发安全                            多队伍同时提交 Flag / 同时开启容器 → 无冲突             (ConcurrencyLockManager)
场景25  ─ 负载转移                            服务器过载 → 创建暂停 → 排队等待 → 转移至轻载节点       (AutoTransfer)
```

---

## 文件结构设计（更新 v2）

### Phase 1 — 统一评分引擎 + 可配置提交类型（覆盖场景 1-15, 11, 12）

**Create:**
```
src/GZCTF/Services/Scoring/UnifiedScoringEngine.cs            ← 统一评分引擎
src/GZCTF/Services/Scoring/IVerificationStrategy.cs            ← 验证策略接口
src/GZCTF/Services/Scoring/FlagHashVerification.cs             ← SHA256 哈希比对策略
src/GZCTF/Services/Scoring/RegexVerification.cs                ← 正则匹配策略
src/GZCTF/Services/Scoring/ScriptVerification.cs               ← 外部脚本执行策略（修复 AutoScript）
src/GZCTF/Services/Scoring/CommandVerification.cs               ← 远程命令执行策略（AutoCommand）
src/GZCTF/Services/Scoring/ManualReviewVerification.cs          ← 人工审核策略
src/GZCTF/Services/Scoring/ScoreDecayCalculator.cs              ← 衰减计算器（单一体）
src/GZCTF/Services/Scoring/FileVerificationService.cs           ← 文件提交验证（病毒样本等）
src/GZCTF/Models/Data/ChallengeSubmissionType.cs                ← 题目配置的提交类型白名单
src/GZCTF/Models/Request/Edit/ChallengeSubmissionTypeModel.cs   ← 新增/编辑提交类型 API 模型
tests/GZCTF.Test/UnitTests/Scoring/UnifiedScoringEngineTests.cs
tests/GZCTF.Test/UnitTests/Scoring/VerificationStrategyTests.cs
tests/GZCTF.Test/UnitTests/Scoring/ScoreDecayTests.cs
tests/GZCTF.Test/UnitTests/Scoring/FileVerificationTests.cs
tests/GZCTF.Integration.Test/Tests/Scoring/ScoringPipelineTests.cs
```

**Modify:**
```
src/GZCTF/Services/ScoringService.cs                          ← ★CRITICAL-6 FIX★ CalculateTotalScoreAsync 只读 Submission.Score 已衰减值，不再二次应用衰减。删除内联 ApplyScoreDecay 私有方法。ScoreDecay 仅在 UnifiedScoringEngine 写入 Submission 时执行一次
src/GZCTF/Services/CheckpointVerificationService.cs           ← 委托 ScoringEngine
src/GZCTF/Services/FlagChecker.cs                             ← 委托 ScoringEngine（传统 CTF 路径）
src/GZCTF/Controllers/SubmissionController.cs                 ← 委托 ScoringOrchestrator
src/GZCTF/Controllers/IRChallengeController.cs                ← 提交时写 Submission 记录 + 文件上传支持
src/GZCTF/Controllers/ScenarioController.cs                   ← 提交时写 Submission 记录
src/GZCTF/Controllers/EditController.cs                       ← IR/Scenario 创建时管理提交类型白名单
src/GZCTF/Models/Data/Challenge.cs                            ← 新增 ChallengeSubmissionType 导航属性
src/GZCTF/ClientApp/src/pages/admin/ir-challenges/new.tsx     ← 提交类型配置 UI
src/GZCTF/ClientApp/src/pages/admin/scenarios/new.tsx         ← 同上
tests/e2e/multi-flag-submission.spec.ts                       ← E2E 测试
tests/e2e/ir-file-submission.spec.ts                          ← E2E 测试
```

### Phase 2 — VM Provider + Docker 容器管理（覆盖场景 7,8,17,18）

> 重点是：本地 Windows VM 导入管理（`D:\wkdb-winserver2012-挖矿病毒模拟`）+ Docker 容器支持 + Guacamole RDP 接入

**Create:**
```
src/GZCTF/Services/Vm/IVirtualMachineProvider.cs               ← VM 供应商接口
src/GZCTF/Services/Vm/KvmProvider.cs                           ← KVM/libvirt 实现（重构 VmManager）
src/GZCTF/Services/Vm/HyperVProvider.cs                        ← Hyper-V 实现（Windows 宿主机）
src/GZCTF/Services/Vm/VmOperationResult.cs                     ← 操作结果模型
src/GZCTF/Services/Vm/VmConnectionInfo.cs                      ← 连接信息模型（RDP/VNC/SSH）
src/GZCTF/Models/Data/VmInstance.cs                            ← VM 实例持久化记录
src/GZCTF/Services/Vm/LocalImageImporter.cs                    ← ★核心★ 本地路径镜像导入（支持 D:\wkdb-winserver2012-挖矿病毒模拟）
src/GZCTF/Services/Docker/DockerImageBuilder.cs                ← Docker 容器镜像自构建
src/GZCTF/Services/Docker/DockerComposeDeployer.cs             ← Docker Compose 一键部署
src/GZCTF/Models/Data/DockerImage.cs                           ← Docker 镜像管理实体
src/GZCTF/Models/Request/Admin/DockerImageModel.cs             ← Docker 镜像 API 模型
src/GZCTF/Controllers/DockerController.cs                      ← Docker 管理 API
src/GZCTF/ClientApp/src/pages/admin/DockerImages/Index.tsx     ← Docker 镜像管理面板
tests/GZCTF.Test/UnitTests/Vm/KvmProviderTests.cs
tests/GZCTF.Test/UnitTests/Vm/LocalImageImporterTests.cs
tests/GZCTF.Test/UnitTests/Docker/ImageBuilderTests.cs
tests/GZCTF.Integration.Test/Tests/Vm/VmLifecycleTests.cs
tests/e2e/vm-image-import.spec.ts
tests/e2e/windows-rdp-challenge.spec.ts
tests/e2e/docker-deploy-cleanup.spec.ts
```

**Modify:**
```
src/GZCTF/Services/VmManager.cs                                ← 重构为 KvmProvider 委托
src/GZCTF/Services/EnvironmentService.cs                       ← 通过 Provider 接口 + Guacamole RDP
src/GZCTF/Services/GuacamoleProxy.cs                           ← RDP 连接参数化 + 动态密码
src/GZCTF/Controllers/IRChallengeController.cs                 ← ★CRITICAL-2 FIX★ GuacamoleProxy 签名同步
src/GZCTF/Controllers/ImageTemplateController.cs               ← 新增本地路径导入端点
src/GZCTF/Models/Internal/KvmSettings.cs                       ← 扩展为 VmSettings
src/GZCTF/Models/Data/ImageTemplate.cs                         ← 新增 FileSystemPath + ContainsMalware 字段
src/GZCTF/Storage/ImageStorage.cs                              ← 扩展本地路径导入支持
src/GZCTF/Migrations/*_AddVmInstance.cs
src/GZCTF/Migrations/*_AddDockerImage.cs
```

### Phase 3 — 部署管理与面板 · 一键部署/清理（覆盖场景 19-22,25）

> 核心：管理面板一键部署/一键清理 + 节点调度 + 排队 + 实时监控

**Create:**
```
src/GZCTF/Models/Data/WorkerNode.cs                            ← 工作节点实体
src/GZCTF/Models/Data/DeploymentTarget.cs                      ← 部署目标（Agent 轮询）
src/GZCTF/Models/Data/DeploymentQueue.cs                       ← 部署队列
src/GZCTF/Models/Request/Admin/NodeModels.cs                   ← 节点管理 API 模型
src/GZCTF/Repositories/Interface/INodeRepository.cs
src/GZCTF/Repositories/NodeRepository.cs
src/GZCTF/Services/Fleet/FleetManager.cs                       ← 节点调度管理主入口
src/GZCTF/Services/Fleet/WeightedScheduler.cs                  ← 加权智能调度器
src/GZCTF/Services/Fleet/QueueManager.cs                       ← ★排队管理器★
src/GZCTF/Services/Fleet/HealthCheckService.cs                 ← 健康检查后台
src/GZCTF/Services/Fleet/ImageDistributionService.cs           ← 镜像分发
src/GZCTF/Services/Fleet/AutoTransferService.cs                ← ★自动转移★
src/GZCTF/Services/Fleet/PortCapacityTracker.cs                ← 端口容量追踪
src/GZCTF/Services/Fleet/RedisDistributedLock.cs               ← Redis 分布式锁
src/GZCTF/Controllers/NodesController.cs                       ← 节点管理 API
src/GZCTF/ClientApp/src/pages/admin/Nodes/Index.tsx            ← ★一键操作面板★
src/GZCTF/ClientApp/src/pages/admin/Nodes/[id]/Detail.tsx      ← 节点详情
src/GZCTF/ClientApp/src/pages/admin/Queue/Index.tsx            ← 队列管理面板
src/GZCTF/ClientApp/src/pages/admin/Dashboard/Index.tsx        ← ★一键部署/清理管理仪表盘★
src/GZCTF/ClientApp/src/components/admin/NodeCard.tsx
src/GZCTF/ClientApp/src/components/admin/QueueCard.tsx
src/GZCTF/ClientApp/src/components/admin/DeployButton.tsx      ← 一键部署按钮
src/GZCTF/ClientApp/src/components/admin/CleanupButton.tsx     ← 一键清理按钮
src/GZCTF/ClientApp/src/hooks/useNodes.ts
src/GZCTF/ClientApp/src/hooks/useDeploy.ts
tests/GZCTF.Test/UnitTests/Fleet/WeightedSchedulerTests.cs
tests/GZCTF.Test/UnitTests/Fleet/QueueManagerTests.cs
tests/GZCTF.Test/UnitTests/Fleet/PortCapacityTrackerTests.cs
tests/GZCTF.Integration.Test/Tests/Fleet/NodeManagementTests.cs
tests/e2e/node-management.spec.ts
tests/e2e/one-click-deploy-cleanup.spec.ts           ← ★核心 E2E★
```

**Modify:**
```
src/GZCTF/Models/AppDbContext.cs                                ← 新增 DbSet
src/GZCTF/Services/Container/ContainerServiceExtension.cs      ← 调度器选择节点
src/GZCTF/Services/ContainerOrchestrator.cs                    ← 接收节点参数 + 端口分配
src/GZCTF/Services/EnvironmentService.cs                        ← 调度器选择节点
src/GZCTF/Migrations/*_AddFleetEntities.cs
```

> **Agent 项目（独立进程，部署在工作节点上）：**
> 新建 `src/GZCTF.Agent/`：
> - `src/GZCTF.Agent/GZCTF.Agent.csproj`
> - `src/GZCTF.Agent/Program.cs`
> - `src/GZCTF.Agent/AgentService.cs`
> - `src/GZCTF.Agent/HeartbeatReporter.cs`
> - `src/GZCTF.Agent/PortAllocator.cs`
> - `src/GZCTF.Agent/Commands/DockerCommandHandler.cs` — 容器部署/销毁
> - `src/GZCTF.Agent/Commands/VmCommandHandler.cs` — VM 部署/销毁
> - `src/GZCTF.Agent/Commands/ImageCommandHandler.cs` — 镜像缓存
> - `src/GZCTF.Agent/appsettings.Template.json`

### Phase 4 — 游戏阶段控制（覆盖场景 23）

**Create:**
```
src/GZCTF/Models/Data/GamePhase.cs
src/GZCTF/Models/Request/Game/GamePhaseModel.cs
src/GZCTF/Controllers/GamePhaseController.cs
src/GZCTF/ClientApp/src/pages/admin/games/[id]/Phases.tsx
src/GZCTF/ClientApp/src/components/admin/PhaseCard.tsx
```

**Modify:**
```
src/GZCTF/Services/GamePhaseService.cs
src/GZCTF/Controllers/IRChallengeController.cs                  ← GamePhase check (IR)
src/GZCTF/Controllers/ScenarioController.cs                     ← GamePhase check (Scenario)
src/GZCTF/Controllers/SubmissionController.cs                   ← GamePhase check (CTF)
src/GZCTF/Controllers/GameController.cs                         ← Phase management API
tests/GZCTF.Integration.Test/Tests/Game/GamePhaseTests.cs
tests/e2e/game-phase-switch.spec.ts
```

### Phase 5 — 数据模型并发加固（覆盖场景 24）

**Modify:**
```
src/GZCTF/Models/AppDbContext.cs                                ← FK 约束 + 索引 + 并发令牌
src/GZCTF/Models/Data/Submission.cs                             ← ConcurrencyToken + Status index
src/GZCTF/Models/Data/Container.cs                              ← FK to GameInstance + Status index
src/GZCTF/Models/Data/FlagContext.cs                            ← Cascade delete + CHECK constraint
src/GZCTF/Models/Data/ScenarioEntities.cs                       ← ConcurrencyToken + 反向导航
src/GZCTF/Models/Data/IREntities.cs                             ← 反向导航 + ConcurrencyToken
src/GZCTF/Models/Data/UserParticipation.cs                      ← 字段→属性修复
src/GZCTF/Models/Data/StageDependency.cs                        ← 正规化 PrerequisiteStageIds
src/GZCTF/Migrations/*_Phase5DataConcurrency.cs
tests/GZCTF.Integration.Test/Tests/Database/DataIntegrityTests.cs
```

### Phase 6 — 前端重构（覆盖全场景）

**Create:**
```
src/GZCTF/ClientApp/src/types/ir.ts
src/GZCTF/ClientApp/src/types/scenario.ts
src/GZCTF/ClientApp/src/types/submission.ts
src/GZCTF/ClientApp/src/types/node.ts
src/GZCTF/ClientApp/src/types/game-phase.ts
src/GZCTF/ClientApp/src/api/v1/irChallenges.ts
src/GZCTF/ClientApp/src/api/v1/scenarios.ts
src/GZCTF/ClientApp/src/api/v1/submissions.ts
src/GZCTF/ClientApp/src/api/v1/imageTemplates.ts
src/GZCTF/ClientApp/src/hooks/useIRChallenge.ts
src/GZCTF/ClientApp/src/hooks/useScenario.ts
src/GZCTF/ClientApp/src/hooks/useSubmission.ts
src/GZCTF/ClientApp/src/hooks/useNodes.ts
src/GZCTF/ClientApp/src/hooks/useGamePhase.ts
src/GZCTF/ClientApp/src/pages/admin/Images/Index.tsx
src/GZCTF/ClientApp/src/components/admin/ImageUploadModal.tsx
```

**Remove:**
```
src/GZCTF/ClientApp/src/pages/admin/IRChallengeCreate.tsx
src/GZCTF/ClientApp/src/pages/admin/IRChallengeList.tsx
src/GZCTF/ClientApp/src/pages/admin/ScenarioCreate.tsx
src/GZCTF/ClientApp/src/pages/admin/ScenarioList.tsx
```

**Modify:**
```
src/GZCTF/ClientApp/src/pages/game/IRChallengePlayer.tsx       ← SWR hooks + GuacamoleDesktop 嵌入
src/GZCTF/ClientApp/src/pages/game/ScenarioPlayer.tsx          ← SWR hooks + MultiType 集成
src/GZCTF/ClientApp/src/components/scenario/MultiTypeSubmission.tsx ← File upload 支持
src/GZCTF/ClientApp/src/pages/admin/SubmissionReview.tsx       ← XSS 修复 + 文件预览
src/GZCTF/ClientApp/src/pages/admin/ir-challenges/new.tsx      ← 可配置提交类型 + i18n
src/GZCTF/ClientApp/src/pages/admin/scenarios/new.tsx          ← 可配置提交类型 + i18n
src/GZCTF/ClientApp/src/pages/admin/Dashboard/Index.tsx        ← 仪表盘集成（一键部署/清理入口）
src/GZCTF/ClientApp/src/components/admin/DeployButton.tsx
src/GZCTF/ClientApp/src/components/admin/CleanupButton.tsx
```

### Phase 7 — 安全加固（覆盖全场景）

> 安全放在最后——在所有功能就绪后再统一加固，避免前期迭代重复修复安全测试

**Modify:**
```
src/GZCTF/Middlewares/RateLimiter.cs                              ← Flag 提交端点限流
src/GZCTF/Controllers/SubmissionController.cs                     ← [EnableRateLimiting("Submit")]
src/GZCTF/Controllers/IRChallengeController.cs                    ← [EnableRateLimiting("Submit")]
src/GZCTF/Controllers/ScenarioController.cs                       ← [EnableRateLimiting("Submit")]
src/GZCTF/Services/VmManager.cs                                   ← 参数注入防御 + VNC 认证
src/GZCTF/Services/GuacamoleProxy.cs                              ← 动态密码替代硬编码
src/GZCTF/ClientApp/src/pages/admin/SubmissionReview.tsx          ← XSS 修复
src/GZCTF/Models/Request/Game/IRChallengeModels.cs                ← AccessDetails 脱敏
src/GZCTF/Services/Concurrency/IDistributedLockService.cs         ← 分布式锁接口
src/GZCTF/Services/Concurrency/LocalSemaphoreLock.cs             ← 单机实现
tests/GZCTF.Test/UnitTests/Middleware/RateLimitTests.cs
tests/GZCTF.Test/UnitTests/Concurrency/LockManagerTests.cs
tests/GZCTF.Test/UnitTests/Vm/VmSecurityTests.cs
```

### Phase 8 — 部署编排 + 开发规范

**Create:**
```
src/GZCTF/ClientApp/src/types/ir.ts
src/GZCTF/ClientApp/src/types/scenario.ts
src/GZCTF/ClientApp/src/types/submission.ts
src/GZCTF/ClientApp/src/types/node.ts
src/GZCTF/ClientApp/src/types/game-phase.ts
src/GZCTF/ClientApp/src/api/v1/irChallenges.ts
src/GZCTF/ClientApp/src/api/v1/scenarios.ts
src/GZCTF/ClientApp/src/api/v1/submissions.ts
src/GZCTF/ClientApp/src/api/v1/imageTemplates.ts
src/GZCTF/ClientApp/src/hooks/useIRChallenge.ts
src/GZCTF/ClientApp/src/hooks/useScenario.ts
src/GZCTF/ClientApp/src/hooks/useSubmission.ts
src/GZCTF/ClientApp/src/hooks/useNodes.ts
src/GZCTF/ClientApp/src/hooks/useGamePhase.ts
src/GZCTF/ClientApp/src/pages/admin/Images/Index.tsx
src/GZCTF/ClientApp/src/components/admin/ImageUploadModal.tsx
```

**Remove:**
```
src/GZCTF/ClientApp/src/pages/admin/IRChallengeCreate.tsx
src/GZCTF/ClientApp/src/pages/admin/IRChallengeList.tsx
src/GZCTF/ClientApp/src/pages/admin/ScenarioCreate.tsx
src/GZCTF/ClientApp/src/pages/admin/ScenarioList.tsx
```

**Modify:**
```
src/GZCTF/ClientApp/src/pages/game/IRChallengePlayer.tsx       ← SWR hooks + GuacamoleDesktop 嵌入
src/GZCTF/ClientApp/src/pages/game/ScenarioPlayer.tsx          ← SWR hooks + MultiType 集成
src/GZCTF/ClientApp/src/components/scenario/MultiTypeSubmission.tsx ← File upload 支持
src/GZCTF/ClientApp/src/pages/admin/SubmissionReview.tsx       ← XSS 修复 + 文件预览
src/GZCTF/ClientApp/src/pages/admin/ir-challenges/new.tsx      ← 可配置提交类型 + i18n
src/GZCTF/ClientApp/src/pages/admin/scenarios/new.tsx          ← 可配置提交类型 + i18n
```
```


docs/deploy/production.md
docs/deploy/agent-node.md
docs/development-standards.md
docs/api/overview.md
```

---

# Phase 1: 统一评分引擎 ⚡

> 所有比赛场景的计分必须写 Submission 表（否则不入排行榜）。提交类型由管理员在后台配置，不再硬编码。

### Task 1.1: ScoreDecayCalculator（单一体，消除 double-decay）

**Files:**
- Create: `src/GZCTF/Services/Scoring/ScoreDecayCalculator.cs`
- Test: `tests/GZCTF.Test/UnitTests/Scoring/ScoreDecayTests.cs`

```csharp
// ScoreDecayCalculator.cs — 全系统唯一的衰减实现
public static class ScoreDecayCalculator
{
    public static int Apply(int baseScore, int attemptIndex, ScoreDecay decay)
    {
        if (attemptIndex < 0) return baseScore;
        if (baseScore <= 0) return 0;
        return decay switch
        {
            ScoreDecay.None => baseScore,
            ScoreDecay.Half => attemptIndex == 0
                ? baseScore
                : baseScore / (1 << attemptIndex),
            ScoreDecay.Linear => Math.Max(0, baseScore - attemptIndex * 10),
            _ => baseScore
        };
    }
}
```

测试要点：
- `DecayNone_ReturnsBaseScore_OnAnyAttempt`
- `DecayHalf_ReturnsFullOnFirstAttempt_HalfOnSecond`
- `DecayLinear_Decrements_By10PerAttempt_MinZero`
- **`ApplyDecay_IsIdempotent_WhenCalledWithAlreadyDecayedScore`**（double-decay bug 验证——已衰减的分传入应返回不变）

### Task 1.2: 验证策略接口 + 实现

```csharp
// IVerificationStrategy.cs
// ★CRITICAL-4 FIX★ 调度键从 SubmissionType 改为 VerificationMode
// 原因: SubmissionType (Flag/Writeup/IP) 和 VerificationMode (AutoExact/AutoRegex) 是正交轴。
// 同一个 Flag 提交可以配置 AutoExact 或 AutoRegex，不能按 SubmissionType 统一调度。
// 正确做法: Engine 根据 ScoringRule.VerificationMode 选择策略，而非 SubmissionType。
public interface IVerificationStrategy
{
    /// <summary>
    /// 验证提交的答案。Engine 根据 ScoringRule.VerificationMode 选择策略。
    /// </summary>
    Task<VerificationResult> VerifyAsync(string answer, ScoringRule rule, AppDbContext context, CancellationToken token);
    VerificationMode HandledMode { get; }  // 此策略处理的验证模式
}
```

实现 5 个策略（按 VerificationMode 划分）:
1. `FlagHashVerification : IVerificationStrategy` → `HandledMode = VerificationMode.AutoExact`
2. `RegexVerification : IVerificationStrategy` → `HandledMode = VerificationMode.AutoRegex`
3. `CommandVerification : IVerificationStrategy` → `HandledMode = ...`（IR 的 AutoCommand 映射到内部验证）
4. `ScriptVerification : IVerificationStrategy` → `HandledMode = VerificationMode.AutoScript`
5. `ManualReviewVerification : IVerificationStrategy` → `HandledMode = VerificationMode.ManualReview`

**新增**: `FileVerificationService` — 用于 IR 挑战的病毒样本文件提交，校验文件哈希或属性

### Task 1.3: UnifiedScoringEngine + 提交类型白名单

```csharp
// Challenge.cs — 新增提交类型白名单
public class Challenge
{
    // ... 现有字段 ...
    
    /// <summary>
    /// 管理员配置的允许提交类型（Flag, Writeup, IP, Credential, Custom）。
    /// 为空时使用默认类型（Flag 仅用于向后兼容）。
    /// </summary>
    public List<ChallengeSubmissionType> SubmissionTypes { get; set; } = [];
}

// ChallengeSubmissionType.cs
// ★CRITICAL-6 FIX★ MaxAttempts & ScoreDecay 别存在此处，统一走 ScoringRule。
// ChallengeSubmissionType 仅做 UI 展示配置（标签/文件类型/顺序），不再重复计分字段。
public class ChallengeSubmissionType
{
    [Key] public int Id { get; set; }
    public int ChallengeId { get; set; }
    public ScoringSubmissionType SubmissionType { get; set; } = ScoringSubmissionType.Flag;
    public int OrderIndex { get; set; }        // 提交表单中的顺序
    public string? Label { get; set; }          // 显示名称（如"病毒样本"）
    public bool RequireFile { get; set; }       // 是否需要文件上传
    public string? AcceptedFileExtensions { get; set; }  // ".exe,.dll,.pcap"
    public int MaxFileSize { get; set; }        // 文件大小上限 (MB)
    // 计分字段统一走 ScoringRule.MaxAttempts / ScoringRule.ScoreDecay
    public bool IsActive { get; set; } = true;  // 当前是否接受提交
}
```

核心引擎 `ProcessSubmissionAsync` 流程：
1. 检查 `ChallengeSubmissionType` 有没有配置该提交类型（拒绝未配置的类型）
2. 如果 `RequireFile` → 调用 `FileVerificationService`
3. 查找匹配的 `ScoringRule` 
4. 检查尝试次数限制
5. 调用 `IVerificationStrategy` 验证
6. 调用 `ScoreDecayCalculator.Apply` 衰减（唯一一处）
7. 写 `Submission` 记录（确保排行榜可见）
8. 通过 SignalR 广播排行榜更新

### Task 1.4: 修复 AutoScript 存根 + IR/Scenario 写 Submission

**修复**: `CheckpointVerificationService.cs:260-284` → 委托 `ScriptVerification`
**修复**: `IRChallengeController.cs:571-578` → 完成后调用 `_scoringEngine.RecordIRCheckpointCompletionAsync()`
**修复**: `ScenarioController.cs:540-555` → 完成后调用 `_scoringEngine.RecordStageCompletionAsync()`

### Task 1.5: 接受测�试

```bash
cd src && dotnet test --filter "FullyQualifiedName~Scoring" -v n
```
Expected: ALL PASS

---

# Phase 7: 安全加固 🔒

### Task 2.1: 提交端点速率限制
`[EnableRateLimiting(Name = "FlagSubmission")]` 添加到:
- `SubmissionController.CreateSubmission`
- `SubmissionController.UploadWriteup`
- `IRChallengeController.SubmitCheckpoint`
- `ScenarioController.SubmitStageFlag`

策略: 每分钟 10 次 / 用户

### Task 2.2: 参数注入防御
`VmManager.RunCommandAsync` 中的文件名参数做白名单校验:
```csharp
private static string SanitizeVmName(string name)
{
    if (!Regex.IsMatch(name, @"^[a-zA-Z0-9_\-]{1,64}$"))
        throw new VmOperationException($"Invalid VM name: {name}");
    return name;
}
```

### Task 2.3: Guacamole 动态密码
```csharp
// 删除硬编码 "password"，改为:
parameters = new
{
    hostname = host,
    port,
    username = sessionUsername,
    password = sessionPassword,    // 每场 session 随机生成
    ignoreCert = "true",
    security = "nla",              // 从 "any" 提升为 "nla"
    // ...
};
```

### Task 2.4: 前端 XSS + 响应脱敏
- `SubmissionReview.tsx`: `dangerouslySetInnerHTML` → `<MarkdownRenderer>`
- `IRInstanceDetailModel`: 从 `AccessDetails` 过滤 `SshPasswordHash`、`GuacamoleToken`

### Task 2.5: 分布式锁基础
```csharp
// ConcurrencyLockService.cs
public class ConcurrencyLockService
{
    private readonly SemaphoreSlim _localLock = new(1, 1);
    // 在单机模式下用 SemaphoreSlim，Fleet 模式下扩展为 Redis 分布式锁
    
    /// <summary>
    /// 对 (challengeId, userId) 加锁，防止并发提交冲突。
    /// </summary>
    public async Task<IDisposable> AcquireSubmissionLockAsync(int challengeId, Guid userId)
    {
        await _localLock.WaitAsync();
        return new LockReleaser(_localLock, challengeId, userId);
    }
}
```

---

# Phase 2: VM Provider + Docker 容器管理 💻

### Task 3.1: IVirtualMachineProvider 接口

```csharp
public interface IVirtualMachineProvider
{
    string ProviderName { get; }           // "KVM"
    RemoteProtocol DefaultProtocol { get; } // Rdp

    Task<VmOperationResult> CreateFromTemplateAsync(string templatePath, string vmName, CancellationToken token);
    Task<VmOperationResult> StartAsync(string vmName, CancellationToken token);
    Task<VmOperationResult> ShutdownAsync(string vmName, CancellationToken token);
    Task<VmOperationResult> DestroyAsync(string vmName, CancellationToken token);
    Task<VmOperationResult> CreateSnapshotAsync(string vmName, string snapshotName, CancellationToken token);
    Task<VmOperationResult> SnapshotRevertAsync(string vmName, CancellationToken token);
    Task<VmConnectionInfo> GetConnectionInfoAsync(string vmName, CancellationToken token);
    Task<string?> GetIpAddressAsync(string vmName, CancellationToken token);
    Task<bool> IsRunningAsync(string vmName, CancellationToken token);
}
```

### Task 3.2: KvmProvider（重构自 VmManager）

关键改进:
- 启动 **创建快照**（原有代码缺少此步骤）
- IP 轮询: 每 5 秒查一次 `domifaddr`，最长 120 秒超时（替代 30 秒硬编码等待）
- 销毁后: `virsh undefine` + 磁盘清理
- VM 名持久化到 `VmInstance`（不再从模式推断）
- Windows 模板 XML 使用 virtio-win 驱动 + QEMU Guest Agent

```csharp
// 启动新 VM —— 包含快照创建步骤
async Task StartAndSnapshotAsync(string vmName, CancellationToken token)
{
    await StartAsync(vmName, token);
    await WaitForBootAsync(vmName, token);         // 轮询 IP，最多 120s
    await CreateSnapshotAsync(vmName, "clean", token);  // ✨ 新增：创建快照
}
```

### Task 3.3: 本地镜像导入（`LocalImageImporter`）

**支持从服务器本地路径导入 VM 镜像，比如 `D:\wkdb-winserver2012-挖矿病毒模拟`**

```csharp
// LocalImageImporter.cs
public class LocalImageImporter
{
    /// <summary>
    /// 从本地目录/文件导入 VM 镜像。
    /// 支持：
    ///   - 单文件：.qcow2, .ova, .vmdk, .img
    ///   - 目录：包含 .qcow2/.vmdk 文件的目录（自动扫描）
    ///   - VM 包：包含 VM 描述文件的完整目录
    /// 导入过程：
    ///   1. 复制或硬链接到 ImageStoragePath
    ///   2. 自动检测 OS Type（文件名含 windows → Windows）
    ///   3. 自动检测 ImageType（扩展名决定）
    ///   4. 注册 ImageTemplate 记录到 DB
    ///   5. 如果是OVA/vmdk 且当前使用 KVM → 转换为 qcow2
    /// </summary>
    public async Task<ImageTemplate> ImportFromLocalPathAsync(
        string localPath, string? displayName = null, CancellationToken token = default)
    {
        // 验证路径存在
        // 检测是文件还是目录
        // 检测格式并转换（如需）
        // Step 5: 计算 SHA256 hash (用于分发时缓存命中校验)
        // Step 6: 注册 ImageTemplate 记录到 DB
        // Step 7: [REVIEW FIX] 导入完成后自动触发 ImageDistributionService.DistributeToCapableNodesAsync
        //         确保部署时工作节点已持有镜像，避免首次部署时才发现未同步
        return template;
    }
}
```

`ImageTemplateController` 新增端点:
```
POST /api/v1/image-templates/import  ← { localPath, displayName? }
```

### Task 3.4: Linux 服务器运行 Windows VM

要点（在部署文档中记录，非代码改动）:
- 确保 KVM 主机支持 VT-x/AMD-V 嵌套虚拟化
- 确保 KVM 主机安装 `virt-install`, `virtio-win` 驱动包
- Windows VM 需 QEMU Guest Agent 用于 IP 获取
- KVM 网络: `nat` 或 `bridge` 模式（`bridge` 更适合比赛场景）
- VmManager.GenerateDomainXml 已有 Hyper-V enlightenments 配置，已验证

### Task 3.5: Guacamole RDP 接入 EnvironmentService

```csharp
// EnvironmentService.cs — Windows VM 创建路径
if (isWindows)
{
    var vmResult = await vmProvider.CreateFromTemplateAsync(template.LocalFilePath, vmName, token);
    await vmProvider.StartAsync(vmName, token);
    var connInfo = await vmProvider.GetConnectionInfoAsync(vmName, token);

    // 通过 Guacamole 创建 RDP 连接（替代直接暴露 VNC）
    var (connectionId, guacToken) = await guacamoleProxy.CreateConnectionAsync(
        vmName,
        connInfo.IP ?? "127.0.0.1",
        connInfo.RdpPort ?? 3389,
        sessionUsername: $"player_{instanceId:N}".Truncate(16),
        sessionPassword: Codec.RandomPassword(16));

    var accessDetails = new Dictionary<string, object?>
    {
        ["GuacamoleConnectionId"] = connectionId,
        ["AccessUrl"] = guacamoleProxy.GetConnectionUrl(connectionId, guacToken),
        ["OsType"] = "Windows",
        ["ResetCount"] = 0
    };
}
```

### Task 3.6: VmPortManager（VM 端口管理）

```csharp
public class VmPortManager
{
    private readonly object _lock = new();
    private readonly HashSet<int> _allocatedPorts = [];
    private const int BasePort = 5900;   // VNC start
    private const int RdpBase = 3389;     // RDP (通常用 Guacd 不占用)

    public int AllocateVncPort()
    {
        lock (_lock)
        {
            for (int port = BasePort; port < BasePort + 1000; port++)
            {
                if (!_allocatedPorts.Contains(port))
                {
                    _allocatedPorts.Add(port);
                    return port;
                }
            }
            throw new VmOperationException("No available VNC ports");
        }
    }

    public void ReleasePort(int port)
    {
        lock (_lock) _allocatedPorts.Remove(port);
    }
}
```

---

# Phase 3: 部署管理与面板 🚀

### Task 4.0: Agent ↔ 管理端通信协议（★CRITICAL-1 FIX★）

> **问题:** 原计划未定义主服务器与 Agent 之间的通信协议，导致 ContainerOrchestrator 接收"目标节点"后无法实际下发指令。
> **解决方案:** Agent 通过 HTTP REST 主动轮询管理端取指令（Pull Model），不要求管理端主动推送到 Agent（避开防火墙 NAT 问题）。

**协议设计:**
```
协议: HTTPS + HMAC-SHA256 签名
模式: Pull（Agent 定期轮询 /api/v1/nodes/{id}/targets）
认证: 每个 Agent 预配置 {NodeId, AuthToken}，每次请求携带 Authorization: HMAC-SHA256 {NodeId}:{Signature}
加密: TLS 1.3（生产），自签证书（测试）

通信流:
  管理端（主节点）                        Agent（工作节点）
  ──────────────                         ──────────────
  FleetManager.QueueDeployment()
    → DB 写入 DeploymentTarget 记录
    → 返回 Accepted（202）
                                           │ GET /api/v1/nodes/{id}/targets（每 5s 轮询）
                                           │ ← 返回待处理 Target 列表
                                           ├── 本地执行（Docker/VM）
                                           ├── PUT /api/v1/nodes/{id}/targets/{tid}/status
                                           │    body: { status: "running", port: 32768, ... }
                                           ├── 操作完成/失败
                                           ├── PUT status: "completed" / "failed"
                                           │
  FleetManager.PollCompletion() ← 不需要
  Agent 调用 PUT 更新 Target 状态后，
  管理端通过 SignalR 推送给前端
```

**HTTP 端点（管理端暴露给 Agent）:**
```
GET    /api/v1/nodes/{id}/targets?status=pending  ← Agent 轮询待处理指令
PUT    /api/v1/nodes/{id}/targets/{tid}/status     ← Agent 回报指令执行状态
POST   /api/v1/nodes/{id}/heartbeat                ← Agent 心跳 + 负载报告
PUT    /api/v1/nodes/{id}/image-status              ← Agent 回报镜像缓存状态
```

**DeploymentTarget 实体（存储待执行的部署指令）:**
```csharp
public class DeploymentTarget
{
    [Key] public Guid Id { get; set; }
    public Guid TargetNodeId { get; set; }        // 目标节点
    public TargetType Type { get; set; }          // Docker / VM
    public TargetAction Action { get; set; }      // Create / Start / Destroy / SnapshotRevert
    public string Payload { get; set; }           // JSON: 操作参数
    public TargetStatus Status { get; set; } = TargetStatus.Pending;
    public int? ResultPort { get; set; }          // Agent 分配的端口号
    public string? ResultHost { get; set; }       // Agent 上报的访问地址
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum TargetStatus : byte { Pending = 0, Running = 1, Completed = 2, Failed = 3, Cancelled = 4 }
```

**Agent 主循环伪代码:**
```
loop:
    targets = GET /api/v1/nodes/{nodeId}/targets?status=pending
    for each target in targets:
        PUT /api/v1/nodes/{nodeId}/targets/{target.id}/status { status: "running" }
        try:
            result = execute_locally(target)
            PUT status: "completed", port: result.port, host: result.host
        catch:
            PUT status: "failed", error: ex.message
    // 心跳
    POST /api/v1/nodes/{nodeId}/heartbeat { cpu, memory, containers, vms, ports }
    sleep 5s
```

**安全性:**
- HMAC 签名: `Base64(HMAC-SHA256(AuthToken, "{method}:{path}:{timestamp}:{body}"))`
- 重放保护: 每个请求带 `X-GZCTF-Timestamp`，偏差超过 30s 拒绝
- Token 轮换: Agent 注册时下发 Token，支持定期轮换

### Task 4.1: WorkerNode 模型

```csharp
public class WorkerNode
{
    [Key] public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string HostAddress { get; set; } = string.Empty;

    // 类型能力
    public NodeCapability Capabilities { get; set; } = NodeCapability.Docker;
    // 用于组合位标志：Docker=1, Kvm=2, HyperV=4

    // 实时负载（由 Agent 心跳报告）
    public float CpuLoad { get; set; }           // 0.0 ~ 1.0
    public float MemoryLoad { get; set; }        // 0.0 ~ 1.0
    public int CurrentContainers { get; set; }
    public int MaxContainers { get; set; } = 20;
    public int CurrentVms { get; set; }
    public int MaxVms { get; set; } = 5;

    // 状态
    public NodeStatus Status { get; set; } = NodeStatus.Unknown;
    public DateTimeOffset? LastHeartbeat { get; set; }

    // 网络
    public string? Labels { get; set; }          // JSON: {"region":"cn-east","location":"机房A"}

    public uint ConcurrencyToken { get; set; }
}

[Flags]
public enum NodeCapability : byte
{
    None = 0,
    Docker = 1,
    Kvm = 2,
    // HyperV = 4  (保留)
}
```

### Task 4.2: WeightedScheduler（加权智能调度）

```csharp
public class WeightedScheduler
{
    private readonly INodeRepository _nodeRepo;
    private readonly ILogger<WeightedScheduler> _logger;

    /// <summary>
    /// 为请求选择最优节点。
    /// 
    /// 评分公式（越高越优先）:
    ///   score = 1000 * (1 - cpuLoad) 
    ///         + 500 * (1 - memoryLoad)
    ///         + 200 * (1 - currentContainers / maxContainers)
    ///         + 200 * (1 - currentVms / maxVms)
    ///         - 100 * (如果节点当前处理请求数 > 阈值)
    /// 
    /// 返回最优节点 ID。如所有节点负载 > 90% 则返回 null（触发排队）。
    /// </summary>
    public async Task<Guid?> SelectOptimalNodeAsync(
        NodeCapability required, CancellationToken token)
    {
        var nodes = await _nodeRepo.GetOnlineNodesAsync(token);
        if (nodes.Count == 0) return null;

        var scored = nodes
            .Where(n => (n.Capabilities & required) == required)
            .Select(n => new
            {
                Node = n,
                Score = 1000f * (1 - n.CpuLoad)
                      + 500f * (1 - n.MemoryLoad)
                      + 200f * (1 - (float)n.CurrentContainers / Math.Max(n.MaxContainers, 1))
                      + 200f * (1 - (float)n.CurrentVms / Math.Max(n.MaxVms, 1))
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scored.FirstOrDefault();
        if (best is null) return null;

        // 如果最优节点负载超过 90%，返回 null 触发排队
        if (best.Score < 200) return null;

        return best.Node.Id;
    }
}
```

### Task 4.3: QueueManager（排队机制）

```csharp
public class QueueManager
{
    private readonly ConcurrentQueue<DeploymentRequest> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly INodeRepository _nodeRepo;
    private readonly WeightedScheduler _scheduler;

    public QueueManager(INodeRepository nodeRepo, WeightedScheduler scheduler)
    {
        _nodeRepo = nodeRepo;
        _scheduler = scheduler;
        _ = ProcessQueueAsync();  // 背景处理
    }

    /// <summary>
    /// 入队请求。返回队列位置。
    /// </summary>
    public async Task<QueuePosition> EnqueueAsync(DeploymentRequest request)
    {
        _queue.Enqueue(request);
        _signal.Release();
        return new QueuePosition(request.RequestId, _queue.Count);
    }

    /// <summary>
    /// 查询队列状态。返回位置和预计等待时间。
    /// </summary>
    public QueueStatus GetQueueStatus(Guid requestId)
    {
        var position = _queue.ToArray()
            .Select((r, i) => new { r.RequestId, Index = i })
            .FirstOrDefault(x => x.RequestId == requestId);

        return new QueueStatus
        {
            Position = position?.Index + 1,
            Ahead = position?.Index,
            EstimatedWaitSeconds = (position?.Index ?? 0) * 120  // 假设每项 2 分钟
        };
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            await _signal.WaitAsync();
            while (_queue.TryDequeue(out var request))
            {
                var nodeId = await _scheduler.SelectOptimalNodeAsync(
                    request.RequiredCapability, CancellationToken.None);

                if (nodeId is null)
                {
                    // 仍无可用节点 → 放回队列，等 30 秒再试
                    _queue.Enqueue(request);
                    await Task.Delay(30_000);
                    _signal.Release();
                    break;
                }

                // 在目标节点执行
                await ExecuteOnNodeAsync(request, nodeId.Value);
            }
        }
    }
}
```

### Task 4.4: HealthCheckService（实时状态监控）

```csharp
public class HealthCheckService : BackgroundService
{
    private const int HeartbeatTimeout = 120;  // 秒

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var offlineNodes = await _nodeRepo.MarkStaleNodesOfflineAsync(
                TimeSpan.FromSeconds(HeartbeatTimeout), stoppingToken);

            if (offlineNodes > 0)
                _logger.LogWarning("Marked {Count} node(s) as offline (no heartbeat)", offlineNodes);

            // 通过 SignalR 向管理面板广播节点状态
            var allNodes = await _nodeRepo.GetAllNodesAsync(stoppingToken);
            await _hubContext.Clients.Group("admin")
                .SendAsync("NodesStatusUpdated", allNodes, stoppingToken);

            await Task.Delay(30_000, stoppingToken);
        }
    }
}
```

### Task 4.5: PortCapacityTracker（端口容量追踪 —— 主节点视角）

> **关键决策：端口分配由 Agent 本地执行，主节点仅追踪容量。**
> 原因：端口在 Agent 所在物理机绑定，如果主节点分配后 Agent 绑定失败，需要复杂回滚。
> Agent 本地分配 → 上报已用端口数/剩余容量 → 主节点据此调度。

```csharp
/// <summary>
/// 主节点端口的容量追踪器。
/// 不分配具体端口 — 只根据上报的容量做调度决策。
/// 实际端口分配在 Agent 本地执行（PortAllocator）。
/// </summary>
public class PortCapacityTracker
{
    private readonly ConcurrentDictionary<Guid, NodePortCapacity> _capacities = new();

    /// <summary>
    /// 更新节点的端口容量信息（由 Agent 心跳上报）。
    /// called by HeartbeatService when Agent reports current port usage.
    /// </summary>
    public void UpdateCapacity(Guid nodeId, int totalPorts, int usedPorts)
    {
        _capacities[nodeId] = new NodePortCapacity
        {
            TotalPorts = totalPorts,
            UsedPorts = usedPorts,
            AvailablePorts = totalPorts - usedPorts,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 检查节点是否有足够的端口余量。
    /// Docker: 每个容器需 1 个端口（32768-60999 共 28231 个，通常不缺）
    /// VM: 每个 VM 需 1 个 VNC 端口（对于 VM 部署，KVM 通过 Guacd 转发，不占独立端口）
    /// </summary>
    public bool HasCapacity(Guid nodeId, int requiredPorts)
    {
        return _capacities.TryGetValue(nodeId, out var cap)
            && cap.AvailablePorts >= requiredPorts;
    }

    public NodePortCapacity? GetCapacity(Guid nodeId)
        => _capacities.TryGetValue(nodeId, out var cap) ? cap : null;
}

public class NodePortCapacity
{
    public int TotalPorts { get; init; }
    public int UsedPorts { get; init; }
    public int AvailablePorts { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}
```

```csharp
// Agent 端口的端口分配器（在 Agent 进程中运行）
// src/GZCTF.Agent/PortAllocator.cs
public class AgentPortAllocator
{
    private readonly object _lock = new();
    private readonly HashSet<int> _allocated = [];

    /// <summary>
    /// 在 Agent 本地分配端口并立即检查可用性。
    /// Docker 主机端口范围: 32768-60999
    /// VM VNC 通过 Guacd 转发不占用主机端口。
    /// 分配后通过心跳上报给主节点 PortCapacityTracker。
    /// </summary>
    public int AllocatePort(PortRange range)
    {
        lock (_lock)
        {
            for (int port = range.Start; port <= range.End; port++)
            {
                if (_allocated.Add(port))
                {
                    // 检查端口是否真可用（TCP 探测）
                    if (IsPortAvailable(port))
                        return port;
                    _allocated.Remove(port);
                }
            }
        }
        throw new PortExhaustedException($"No available ports in {range.Start}-{range.End}");
    }

    public void ReleasePort(int port)
    {
        lock (_lock) _allocated.Remove(port);
    }

    public (int total, int used) GetUsage() => (28231, _allocated.Count);

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp);
            socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
            return true;
        }
        catch { return false; }
    }
}
```

### Task 4.6: 统一分布式锁（Phase 2 定义接口 + Phase 4 Redis 实现）

> **关键决策：** Phase 2 定义 `IDistributedLockService` 接口 + 单机 `LocalSemaphoreLock` 实现。
> Phase 4 新增 `RedisDistributedLock` 实现同一接口，通过 DI 替换。两个 Phase 不存在代码重复。

```csharp
// Phase 2 定义（src/GZCTF/Services/Concurrency/IDistributedLockService.cs）
/// <summary>
/// 分布式锁接口。单机模式用 LocalSemaphoreLock，分布式用 RedisDistributedLock。
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// 获取命名锁。返回 IDisposable，using 块结束时自动释放。
    /// </summary>
    /// <param name="key">锁键。建议格式: "{entity}:{id}:{userId}"</param>
    /// <param name="timeout">超时时间，超时抛 LockAcquisitionException</param>
    /// <returns>释放锁用的 disposable，调用方用 using 包裹</returns>
    Task<IDisposable> AcquireAsync(string key, TimeSpan? timeout = null);
}
```

```csharp
// Phase 2 实现（src/GZCTF/Services/Concurrency/LocalSemaphoreLock.cs）
public class LocalSemaphoreLock : IDistributedLockService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key, TimeSpan? timeout = null)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(timeout ?? TimeSpan.FromSeconds(30)))
            throw new LockAcquisitionException($"Lock timeout: {key}");
        return new SemaphoreReleaser(semaphore);
    }

    private class SemaphoreReleaser(SemaphoreSlim s) : IDisposable
    {
        public void Dispose() => s.Release();
    }
}
```

```csharp
// Phase 4 扩展（src/GZCTF/Services/Fleet/RedisDistributedLock.cs）
/// <summary>
/// 基于 Redis RedLock 算法的分布式锁实现。
/// 当 RunMode=Fleet 时通过 DI 替换 LocalSemaphoreLock。
/// </summary>
public class RedisDistributedLock : IDistributedLockService
{
    private readonly IConnectionMultiplexer _redis;
    private static readonly string AcquireScript = """
        if redis.call('SET', KEYS[1], ARGV[1], 'NX', 'PX', ARGV[2]) then
            return 1
        else
            return 0
        end
        """;

    public async Task<IDisposable> AcquireAsync(string key, TimeSpan? timeout = null)
    {
        var lockKey = $"gzctf:lock:{key}";
        var token = Guid.NewGuid().ToString("N");
        var expireMs = (int)(timeout ?? TimeSpan.FromSeconds(30)).TotalMilliseconds;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var db = _redis.GetDatabase();
            var acquired = await db.ScriptEvaluateAsync(
                AcquireScript, new RedisKey[] { lockKey },
                new RedisValue[] { token, expireMs });

            if ((int)acquired == 1)
                return new RedisLockReleaser(db, lockKey, token);

            await Task.Delay(50); // retry
        }

        throw new LockAcquisitionException($"Redis lock timeout: {key}");
    }

    private class RedisLockReleaser : IDisposable
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;
        private static readonly string ReleaseScript = """
            if redis.call('GET', KEYS[1]) == ARGV[1] then
                return redis.call('DEL', KEYS[1])
            else
                return 0
            end
            """;

        public RedisLockReleaser(IDatabase db, string key, string token)
        { _db = db; _key = key; _token = token; }

        public void Dispose()
        {
            _db.ScriptEvaluate(ReleaseScript, new RedisKey[] { _key },
                new RedisValue[] { _token });
        }
    }
}
```

```csharp
// DI 注册（Program.cs or ServicesExtension.cs）
if (builder.Configuration.GetValue<string>("RunMode") == "Fleet")
    builder.Services.AddSingleton<IDistributedLockService, RedisDistributedLock>();
else
    builder.Services.AddSingleton<IDistributedLockService, LocalSemaphoreLock>();
```

### Task 4.7: NodesController API

```
POST   /api/v1/nodes                        ← 注册节点（Admin）
DELETE /api/v1/nodes/{id}                    ← 注销节点（Admin）
GET    /api/v1/nodes                         ← 节点列表（含实时负载）
GET    /api/v1/nodes/{id}                    ← 节点详情（含端口使用）
POST   /api/v1/nodes/{id}/heartbeat          ← Agent 心跳
POST   /api/v1/nodes/{id}/command            ← 向节点发送指令
GET    /api/v1/nodes/{id}/logs               ← 节点日志
GET    /api/v1/queue                         ← 队列状态
GET    /api/v1/queue/{requestId}             ← 单个请求队列状态
```

### Task 4.8: 前端监控面板

`/admin/nodes` 页面:
- 节点卡片网格：绿色(在线) / 红色(离线) / 黄色(繁忙)
- 每卡: 节点名、IP、CPU 负载条、内存负载条、容器数/最大、VM 数/最大、最后心跳时间
- 操作: 注销、查看详情

`/admin/queue` 页面:
- 队列列表: 请求 ID、类型（Docker/VM）、状态（排队中/处理中/已完成）、等待时间
- 清空队列、手动优先级调整

`/admin/nodes/{id}` 详情:
- 实时负载曲线
- 当前运行的容器/VM 列表
- 端口分配表
- 日志流（WebSocket）

---

# Phase 4: 游戏阶段控制 🎮

### Task 5.1: GamePhase 模型

```csharp
public class GamePhase
{
    [Key] public int Id { get; set; }
    public int GameId { get; set; }

    /// <summary>阶段名称，如"热身赛"、"初赛"、"决赛"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>阶段开始时间</summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>阶段结束时间</summary>
    public DateTimeOffset EndTime { get; set; }

    /// <summary>本阶段是否允许传统 CTF 类型</summary>
    public bool CTFEnabled { get; set; } = true;

    /// <summary>本阶段是否允许应急响应（IR）类型</summary>
    public bool IREnabled { get; set; } = true;

    /// <summary>本阶段是否允许多阶段场景（Scenario）类型</summary>
    public bool ScenarioEnabled { get; set; } = true;

    /// <summary>本阶段的安全策略（JSON）</summary>
    public string? SecurityPolicy { get; set; }

    [ForeignKey(nameof(GameId))]
    public Game? Game { get; set; }
}
```

### Task 5.2: 阶段控制器层面检查（★CRITICAL-3 FIX★）

> **原方案问题**: 中间件无法从 `/api/v1/ir-challenges`、`/api/v1/scenarios` 等 URL 提取 gameId。
> **修正方案**: 不用全局中间件。在控制器/Service 层通过 `GameChallenge.GameId` 查 DB 获取当前 Game 的阶段，
> 仅在需要时检查。`Modify` 列表增加 `IRChallengeController`、`ScenarioController`、`SubmissionController`。

```csharp
// src/GZCTF/Services/GamePhaseService.cs — 控制器层调用的阶段检查服务
public class GamePhaseService
{
    private readonly AppDbContext _context;
    private readonly ILogger<GamePhaseService> _logger;

    /// <summary>
    /// 检查指定 gameId 的当前阶段是否允许给定类型的操作。
    /// 查 Game.Phases 中 StartTime <= now <= EndTime 的活跃阶段。
    /// </summary>
    public async Task<PhaseCheckResult> CheckAsync(int gameId, PhaseRequiredType requiredType, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var activePhase = await _context.GamePhases
            .Where(p => p.GameId == gameId && p.StartTime <= now && p.EndTime >= now)
            .FirstOrDefaultAsync(token);

        if (activePhase is null)
            return PhaseCheckResult.NoActivePhase;

        return requiredType switch
        {
            PhaseRequiredType.CTF => activePhase.CTFEnabled
                ? PhaseCheckResult.Allowed : PhaseCheckResult.DisabledByPhase,
            PhaseRequiredType.IR => activePhase.IREnabled
                ? PhaseCheckResult.Allowed : PhaseCheckResult.DisabledByPhase,
            PhaseRequiredType.Scenario => activePhase.ScenarioEnabled
                ? PhaseCheckResult.Allowed : PhaseCheckResult.DisabledByPhase,
            _ => PhaseCheckResult.Allowed
        };
    }
}

public enum PhaseRequiredType { CTF, IR, Scenario }
public enum PhaseCheckResult { Allowed, DisabledByPhase, NoActivePhase }
```

**调用方式（各控制器中）：**
```csharp
// IRChallengeController 各动作前:
var phaseCheck = await _phaseService.CheckAsync(gameId, PhaseRequiredType.IR, token);
if (phaseCheck != PhaseCheckResult.Allowed)
    return Forbid();  // 403
```

**涉及修改的控制器列表（★CRITICAL-3 新增）:**
```
src/GZCTF/Controllers/IRChallengeController.cs    ← 所有创建/提交动作前加阶段检查
src/GZCTF/Controllers/ScenarioController.cs       ← 同上
src/GZCTF/Controllers/SubmissionController.cs     ← 同上（传统 CTF 路径）
src/GZCTF/Controllers/GameController.cs           ← GET 场景下加阶段过滤
```

### Task 5.3: 管理面板

`/admin/games/{id}/phases` 页面:
- 阶段列表（时间线显示）
- 每个阶段的可开关: CTF / IR / Scenario
- 拖拽调整阶段顺序
- 临时暂停所有实例（紧急操作按钮）

---

# Phase 5: 数据模型并发加固 🗄️

### Task 6.1: FK + 约束

- Container → GameInstance/ExerciseInstance: `OnDelete(SetNull)` + FK
- FlagContext → GameChallenge/ExerciseChallenge: `OnDelete(Cascade)` + CHECK 单父约束
- UserParticipation.Participation: 字段→属性修复

### Task 6.2: 并发令牌（xmin 配置文档）

> **REVIEW FIX** — 必须统一使用 PostgreSQL xmin 作为并发令牌，不混用 rowversion/byte[]。

```csharp
// AppDbContext.cs — xmin 并发令牌映射（EF Core 10）
// 所有需要并发保护的实体统一使用此模式:

modelBuilder.Entity<Submission>(entity =>
{
    // Map xmin system column as concurrency token
    entity.Property<uint>("xmin")
          .HasColumnType("xid")
          .IsRowVersion()
          .HasColumnName("xmin")
          .ValueGeneratedOnAddOrUpdate()
          .IsConcurrencyToken();

    // Use shadow property for optimistic concurrency
    entity.UseXminAsConcurrencyToken();
});
```

**并发令牌覆盖清单:**
- `Submission.xmin` — 并发提交分数覆盖防护
- `Container.xmin` — 并发销毁/创建冲突防护
- `FlagContext.xmin` — `IsOccupied` 字段保护
- `ScenarioInstance.xmin` — 并发阶段状态更新防护
- `WorkerNode.xmin` — 节点状态更新防护

### Task 6.3: 索引

- `Submission.Status` (FILLFACTOR=90) — 排行榜频繁过滤 Accepted
- `Container.Status` — 清理服务轮询
- `IRInstance.EnvironmentStatus` — 后台验证服务查询

### Task 6.4: JSON 迁移

- `Stage.PrerequisiteStageIds` → `StageDependency` 关联表
- `Stage.EnvironmentImageIds` → `StageImageTemplate` 关联表

---

# Phase 6: 前端重构 🎨

详见 v1 内容，主要变更：
- 所有新功能强类型化（`types/*.ts`）
- SWR hooks 替代裸 fetch()
- GuacamoleDesktop 嵌入 IR Player
- i18n 覆盖
- 死文件删除
- 镜像管理面板
- 节点监控面板
- 队列监控面板
- 游戏阶段控制面板
- ★REVIEW FIX★ 全局 ErrorBoundary 组件 + 骨架屏 (Skeleton) + 空状态 (EmptyState)
  - `src/GZCTF/ClientApp/src/components/AppErrorBoundary.tsx`
  - `src/GZCTF/ClientApp/src/components/SkeletonCard.tsx`
  - `src/GZCTF/ClientApp/src/components/EmptyState.tsx`
  - 所有页面 catch 块替换为 `notifications.show()`（不再静默吞异常）

---

# Phase 8: 部署 + 规范 📋

### docker-compose.yml
```
services:
  db:        postgres:16-alpine
  redis:     redis:7-alpine
  guacd:     guacamole/guacd
  api:       YINYU CTF平台 后端
  spa:       nginx + 前端静态文件
volumes:     postgres_data, image_storage
```

### docker-compose.agent.yml
```
agent:
  build: src/GZCTF.Agent
  volumes: /var/run/docker.sock  (控制主机 Docker)
           /var/lib/libvirt       (控制主机 libvirt)
```

### 开发规范文档
Git 规范、代码审查清单、TDD 三层测试规范、API 设计规范、安全检查清单详见 `docs/development-standards.md`

---

## E2E 测试矩阵（更新）

| 场景 | 文件 | 关键验证点 |
|---|---|---|
| IR + 文件提交 | `ir-file-submission.spec.ts` | 选手上传样本文件 → 管理员审核 → 计分 |
| 多 Flag | `multi-flag-submission.spec.ts` | Flag + Writeup + IP 三类型, 分数衰减 |
| VM 镜像导入 | `vm-image-import.spec.ts` | 从本地路径导入 → 创建 ImageTemplate → 列表可见 |
| Windows RDP | `windows-rdp-challenge.spec.ts` | VM 启动 → Guacamole 连接 URL 生成 |
| 节点管理 | `node-management.spec.ts` | 注册 → 心跳 → 调度 → 队列 → 故障转移 |
| 自动调度 | `auto-scaling.spec.ts` | 第 21 个容器请求进入队列而非崩溃 |
| 游戏阶段 | `game-phase-switch.spec.ts` | IR 关闭后 IR 提交返回 403 |
| 并发提交 | `concurrent-submission.spec.ts` | 10 线程同时提交，无数据竞争 |
| Scenario 完整流 | `scenario-full-lifecycle.spec.ts` | 创建 → 发布 → 多阶段 → 排行榜 |
| IR 完整流 | `ir-full-lifecycle.spec.ts` | 注册时段 → 创建 → 检查点 → 重置 → 再完成 |

---

## 安全验收清单

- [ ] 所有 Flag 提交端点 10次/分钟限流
- [ ] Guacamole 源码中无硬编码密码
- [ ] VNC 端口仅在 localhost 监听（通过 Guacd 转发访问者）
- [ ] 前端无 `dangerouslySetInnerHTML`
- [ ] IRInstance.AccessDetails 响应中无密码/Token
- [ ] VmManager 拒绝 shell 特殊字符的 VM 名
- [ ] Agent ↔ 管理端通信 HMAC 签名
- [ ] 并发 10 线程提交同一个 Flag 无数据竞争
- [ ] 20 个容器请求在 5 节点集群中均匀分布
- [ ] 全部节点负载 > 90% 时第 21 个请求入队而非崩溃
- [ ] ★REVIEW FIX★ 导入的含恶意软件 IR VM 镜像必须使用 isolated 网络模式运行（禁止访问外网）
- [ ] ★REVIEW FIX★ IR VM 镜像导入后在 ImageTemplate 中标注 contains_malware=true 及风险提示
- [ ] Agent ↔ 管理节点通信使用 TLS 1.3（非仅 HMAC + HTTP 明文）
- [ ] guacd 端口仅对 API 和 Agent 监听，不直接暴露于公网

---

## v2 → v2.2 变更摘要

### Phase 顺序调整（用户反馈）

| 旧顺序 | 新顺序 | 调整理由 |
|---|---|---|
| Phase 2 安全加固 | **Phase 7 安全加固** | 安全放最后做，避免反复修复 |
| Phase 3 VM Provider | **Phase 2 VM + Docker容器管理** | 核心目标提前 |
| Phase 4 分布式调度 | **Phase 3 部署管理与面板** | 与 Phase 2 形成管理链路 |
| Phase 5 游戏阶段 | **Phase 4 游戏阶段** | 保持 |
| Phase 6 数据模型 | **Phase 5 数据模型** | 保持 |
| Phase 7 前端重构 | **Phase 6 前端重构** | 保持 |
| — | **Phase 7 安全加固**（新增） | 最后统一加固 |
| Phase 8 部署 | Phase 8 部署 | 收尾 |

### 功能的对应调整

| 用户需求 | 计划变更 | 所在 Phase |
|---|---|---|
| 多 Flag 由后台配置 | ChallengeSubmissionType 实体 + 管理 UI | Phase 1 |
| Windows VM 本地导入 | LocalImageImporter（`D:\wkdb-winserver2012-` 等） | Phase 2 |
| Docker 容器管理 | DockerImageBuilder + DockerComposeDeployer + 管理面板 | Phase 2 |
| 一键部署/清理 | DeployButton/CleanupButton + 管理仪表盘 | Phase 3 |
| 智能负载调度 | WeightedScheduler + 评分公式 | Phase 3 |
| 排队机制 | QueueManager + 自动出队 | Phase 3 |
| 服务器状态监控 | HealthCheckService + WebSocket + 面板 | Phase 3 |
| 负载转移自动触发 | AutoTransferService | Phase 3 |
| 比赛阶段控制 | GamePhase + GamePhaseService | Phase 4 |
| 并发安全 | IDistributedLockService 统一接口 + DB xmin | Phase 5 |
| 一键清理环境 | CleanupService + 管理面板入口 | Phase 3 |

## v2.1 → v2.2 审查修正变更

| 审查问题 | 修正内容 | 影响 Phase |
|---|---|---|
| Phase 顺序不合理 | 安全移到末位，VM+Docker 前置 | 全 Phase |
| 缺少容器管理 | 新增 DockerImageBuilder/DockerComposeDeployer | Phase 2 |
| 缺少一键部署/清理 | 新增 DeployButton/CleanupButton + Dashboard 面板 | Phase 3 |
| 缺少 Docker 管理面板 | 新增 DockerController + DockerImages 管理页 | Phase 2 |
| PortManager 分布式不一致 | 端口下沉到 Agent，主节点仅追踪容量 | Phase 2 + 3 |
| ConcurrencyLockService 重复 | 统一 IDistributedLockService 接口 | Phase 5 + 7 |
| 镜像导入后未分发 | LocalImageImporter → 自动触发 ImageDistributionService | Phase 2 |
| 镜像无缓存 | Agent 端 SHA256 hash 缓存 | Phase 3 |
| xmin 配置 | 补充 EF Core 映射代码 | Phase 6 |
| IR VM 恶意软件安全 | contains_malware + isolated network | Phase 7 |
| 前端错误处理 | AppErrorBoundary + SkeletonCard + EmptyState | Phase 6 |
