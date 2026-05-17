# Implementation Plan: CTF 场景化实战平台

**Branch**: `001-ctf-scenario-engine` | **Date**: 2026-05-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-ctf-scenario-engine/spec.md`

## Summary

基于 GZCTF 平台（ASP.NET Core + React）进行二次开发，扩展三大核心能力：多阶段真实攻击场景引擎、应急响应（IR）挑战模块、以及多元提交与综合评分系统。技术方案复用 GZCTF 现有 Game/Challenge 架构，将 Scenario 和 IRChallenge 作为 Challenge 实体的新子类型进行扩展。Linux 靶机通过 Docker 容器部署，Windows 靶机通过 KVM/QEMU + libvirt 虚拟化运行，选手通过 Web 桌面代理（Guacamole/noVNC）访问 Windows IR 环境，攻击场景中选手需自行搭建内网隧道渗透内部靶机。单机部署采用预约分时制管理资源。

## Technical Context

**Language/Version**: C# (.NET 9+，与 GZCTF 保持一致)；TypeScript 6.0 (前端)

**Primary Dependencies**:
  - Backend: ASP.NET Core、Entity Framework Core、SignalR、libvirt-net (KVM API 封装)、SSH.NET (SSH 管理)
  - Frontend: React 19、Mantine UI v9、Tailwind CSS 4、React Router v7、SWR (数据请求)、@microsoft/signalr (实时通信)、ECharts/Recharts (图表)
  - Infrastructure: Docker + Docker Compose (Linux 靶机容器编排)、KVM/QEMU + libvirt (Windows 靶机虚拟化)、Apache Guacamole (Web 桌面代理)

**Storage**: PostgreSQL（扩展现有 GZCTF 数据库，新增场景/IR/提交相关表）；Redis（缓存 + 实时状态）；本地文件存储（VM 磁盘镜像 .qcow2/.ova）

**Testing**: 
  - Backend: xUnit (GZCTF 现有测试框架)
  - Frontend E2E: Playwright（Constitution Principle II 强制要求）

**Target Platform**: Linux 服务器（单机部署，Ubuntu 22.04+ / Debian 12+），需支持 KVM 硬件虚拟化

**Project Type**: Web 应用（frontend + backend），在现有 GZCTF 单体 ASP.NET Core 项目基础上扩展

**Performance Goals**:
  - 场景阶段解锁延迟 < 5 秒
  - IR 检查点自动验证 < 30 秒
  - 环境重置 < 60 秒
  - 拓扑图渲染 < 2 秒 (100 节点内)
  - 单一时段支持 20 个活跃并发环境

**Constraints**:
  - 单机部署，资源受限（需通过预约分时制管理资源）
  - Windows VM 需 KVM 硬件虚拟化支持
  - 磁盘镜像上传上限 50GB
  - 必须兼容 GZCTF 现有数据模型和 API 契约，不可破坏已有功能
  - 所有新前端页面必须使用 Mantine UI v9 + Tailwind CSS 4，与 GZCTF 现有 UI 风格一致

**Scale/Scope**:
  - 200+ 注册选手（预约分时制，单时段 ≤ 20 活跃）
  - 25 条功能需求 (FR-001 ~ FR-025)
  - 4 个用户故事 (US1~US4)
  - 8 个核心实体

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|----------|
| I. 生产级完整交付 | ✅ PASS | 所有 FR 覆盖异常处理（FR-009 环境重置）、边界验证（FR-013 权重校验）、空/加载/错误状态提示；Edge Cases 覆盖 10 种边界场景 |
| II. 强制 E2E 测试 (Playwright) | ✅ PASS | Testing 字段明确 Playwright 用于前端 E2E；每个 US 定义了 Independent Test；测试覆盖 Happy Path + Edge Cases + 视觉完整性 |
| III. 架构一致性与平滑扩展 | ✅ PASS | Scenario/IRChallenge 作为 GZCTF Challenge 子类型扩展（非新建平行体系）；复用现有 Game/用户/RBAC/排行榜；数据模型通过 EF Core Migration 演进 |
| IV. 弹性集成与异步处理 | ✅ PASS | VM 生命周期管理 (libvirt) 含超时/重试；环境创建/销毁采用异步任务模型；外部依赖（Docker/libvirt/Guacamole）故障隔离 |
| V. 安全底线与可观测性 | ✅ PASS | FR-018 遵循现有 RBAC；FR-017 审计日志；FR-010 Shell 命令日志；FR-005 操作时间线；SC-006 可观测性反馈 |
| VI. 规范化版本控制 | ✅ PASS | 当前工作于 001-ctf-scenario-engine 特性分支；Plan 完成后将执行原子化提交 |
| VII. 中文本地化 | ✅ PASS | 所有 Spec/Plan 文档中文撰写；前端 UI 文本支持中文（GZCTF 已有 i18next 国际化框架） |

**Gate Result**: ALL PASS — 无违规项，无需填写 Complexity Tracking。

## Project Structure

### Documentation (this feature)

```text
specs/001-ctf-scenario-engine/
├── plan.md              # 本文件
├── research.md          # Phase 0 技术调研
├── data-model.md        # Phase 1 数据模型设计
├── quickstart.md        # Phase 1 快速启动指南
├── contracts/           # Phase 1 API 契约
└── tasks.md             # Phase 2 (/speckit-tasks 生成)
```

### Source Code (基于 GZCTF 现有结构扩展)

```text
src/
├── GZCTF/                          # 主 ASP.NET Core 项目（扩展）
│   ├── ClientApp/                  # React 前端（扩展）
│   │   └── src/
│   │       ├── components/
│   │       │   ├── scenario/       # [NEW] 场景相关组件
│   │       │   ├── ir/             # [NEW] IR 挑战相关组件
│   │       │   └── topology/       # [NEW] 拓扑可视化组件
│   │       ├── pages/
│   │       │   ├── admin/          # [EXTEND] 管理后台（场景/IR 管理页）
│   │       │   └── game/           # [EXTEND] 选手挑战页（场景/IR 界面）
│   │       └── services/           # [EXTEND] API 客户端
│   ├── Controllers/
│   │   ├── ScenarioController.cs   # [NEW] 场景管理 API
│   │   ├── IRChallengeController.cs# [NEW] IR 题目管理 API
│   │   └── SubmissionController.cs # [EXTEND] 扩展提交 API
│   ├── Models/
│   │   ├── Scenario.cs             # [NEW] 场景 + 阶段实体
│   │   ├── IRChallenge.cs          # [NEW] IR 题目 + 检查点实体
│   │   └── Submission.cs           # [EXTEND] 扩展提交模型
│   ├── Services/
│   │   ├── VmManager.cs            # [NEW] KVM/libvirt VM 生命周期管理
│   │   ├── ContainerOrchestrator.cs# [EXTEND] Docker 容器编排扩展
│   │   ├── EnvironmentService.cs   # [NEW] 环境副本创建/销毁/重置
│   │   ├── ScoringService.cs       # [NEW] 多维评分引擎
│   │   └── GuacamoleProxy.cs       # [NEW] Apache Guacamole 集成代理
│   ├── Hubs/
│   │   └── ScenarioHub.cs          # [NEW] SignalR 实时通知（阶段解锁、状态更新）
│   ├── Migrations/                 # [EXTEND] EF Core 数据库迁移
│   ├── Repositories/               # [EXTEND] 新增仓储实现
│   └── Storage/
│       └── ImageStorage.cs         # [NEW] VM 磁盘镜像本地存储管理
└── GZCTF.Test/                     # 单元测试（扩展）
    ├── Services/
    │   ├── VmManagerTests.cs
    │   └── ScoringServiceTests.cs
    └── ...

tests/                              # [NEW] 前端 E2E 测试
└── e2e/
    ├── scenario-create.spec.ts     # 场景创建流程测试
    ├── scenario-play.spec.ts       # 选手挑战流程测试
    ├── ir-challenge.spec.ts        # IR 挑战流程测试
    └── submission-scoring.spec.ts  # 提交与评分测试
```

**Structure Decision**: 选择 Option 2 (Web application) 结构，直接在 GZCTF 的 `src/GZCTF/` 项目内扩展。不创建独立的 backend/frontend 顶层目录，与 GZCTF 现有单体架构保持一致。新增的 E2E 测试独立放置在仓库根目录的 `tests/e2e/` 下，与 GZCTF 现有 `src/GZCTF.Test` 和 `src/GZCTF.Integration.Test` 测试项目区分开。

## Complexity Tracking

> Constitution Check 全部通过，无违规项需要论证。
