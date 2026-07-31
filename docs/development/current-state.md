# YINYU 当前开发状态

更新时间：2026-07-31

本文件是跨会话的短期状态入口。长期规则见根目录 `AGENTS.md`，完整目标见 `docs/platform-commercialization-master-plan.md`。状态变化后应更新本文件，不通过追加整段聊天记录维护记忆。

## 1. 当前基线

| 项目 | 当前事实 |
| --- | --- |
| 主仓库 | `https://github.com/Y3X1L2/newGZCTF.git` |
| 稳定分支 | `main` |
| 本快照代码基线 | `f56221b1fca46199645e820d358cc30c601228ee` |
| 基线说明 | `fix: repair image import distribution state`；其后的纯文档提交不改变该代码基线 |
| 本机正式工作区 | `D:\Work\newGZCTF` |
| Git 结构 | 独立仓库，`.git` 位于正式工作区，不再依赖 linked worktree |
| 本机历史归档 | `D:\Work\newGZCTF-local-archive`，不属于源码事实 |

当前 HEAD 必须通过 `git rev-parse HEAD` 实时读取。开始新任务前仍须重新 `fetch`，不能假定本表中的代码基线或远端关系永久有效。

## 2. 产品与代码状态

当前平台已形成以下主要能力：

- 普通 CTF：赛事、队伍、题目、附件、静态/动态 Flag、Docker、KVM/Windows VM、计分和榜单。
- AWDP：服务、轮次、Checker、攻击提交、修补、重置、恢复、停止和态势数据。
- 理论考试：题库、试卷、单选/多选/判断、草稿、最终提交、成绩和答题回顾。
- 培训：课程、共同教师、报名审核、章节、资源、课程题目、章节实验、课后理论、学习进度和学员详情。
- 管理端：赛事、理论题库、镜像、节点、队列、实例、日志、用户、战队、学员组和系统设置。
- 运行底座：统一部署队列、容量预留、多节点调度、镜像分发、Agent inventory、结构化事件、恢复和关联日志。
- 身份认证：本地账号和 Portal SSO；统一认证的对方项目不在本仓库内。

vNext 正式路由和实现状态以以下文件为准：

- `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx`
- `src/GZCTF/ClientApp/src/vnext/app/shell/moduleRegistry.ts`

截至本快照，首页、赛事、战队、培训、认证、个人主页、理论及主要通用管理页面已经接入 vNext。以下入口仍未标记为完整实现：

- `/practice`：自主练习产品切片尚未进入稳定主线。
- `/admin/teamlab`：TeamLab 正式管理前端尚未完成；后端基础和历史页面不等于 vNext 产品闭环。

不得用旧页面套壳、mock 数据或隐藏跳转把这两个入口描述成已完成。

## 3. 商业化阶段状态

| 阶段 | 当前结论 | 未闭环部分 |
| --- | --- | --- |
| Phase 0 | 代码、迁移和清理门禁完成 | 生产升级仍遵守数据库备份和迁移门禁 |
| Phase 1 | 模块边界、领域/API 契约和外部题目 API 完成 | 后续模块必须持续遵守边界测试 |
| Phase 2 | vNext 前端底座、Token、组件边界和构建门禁完成 | 页面垂直切片仍按产品模块逐步完善 |
| Phase 3 | TeamLab 独立模型、release/plan/runtime/API 基础代码完成 | 外部与 Penetration 两条真实纵向链路仍需持续验收 |
| Phase 4 | 数据库索引、分区、聚合和生命周期代码完成 | 生产容量和长期保留策略需现场数据验证 |
| Phase 5 | Redis、租约、缓存失效和高频缓冲代码完成 | 双主、k6 和基础设施断网演练属于预发布验收 |
| Phase 6 | 统一队列、调度、容量、并发和 Agent 能力代码完成 | 500-owner/300-create、双主接管和目标硬件吞吐未形成最终签收 |
| Phase 7 | 结构化事件、日志、关联、Agent inventory 和恢复代码完成 | 生产观测链和真实故障恢复仍需持续验收 |
| Phase 8-14 | 尚未按总纲全部完成 | VM 统一、TeamLab 商业闭环、内容资产/出题 API、练习、培训理论增强、AWDP 展示和商业验收 |

阶段文档中的“代码完成”不等于已经通过真实生产容量、故障和多节点验收。

## 4. 近期已完成修复

当前主线包含：

- 动态 Flag 按实例隔离，并清理多 Flag `0` 等历史污染问题。
- 培训动态 Flag 不再暴露管理员测试步骤。
- 过期培训实例恢复和运行引用安全重载。
- 培训理论提交后的答案回顾。
- Windows VM 创建和凭据注入链路稳定化；当前没有合格、已认证的新 Windows 基础镜像。
- 首页编排动画和丝带绘制时序优化。
- 镜像存储导入完成后模板进入 `Ready`，节点分发不再与模板 `Importing` 状态互锁。
- 永久失败的镜像分发任务不再无限自动重试，只有 `Retryable=true` 才重试。

镜像状态修复验收记录：后端全量单元测试 `531/531`、镜像分发专项 `8/8`、PostgreSQL 集成测试 `1/1`。线上 `ClosureChallenge` 已从长期“导入中”恢复为 Ready，并完成三个节点分发。

## 5. 已知环境

| 用途 | 地址/说明 |
| --- | --- |
| 主要平台服务器 | `10.24.0.27:8080` |
| 公网平台入口 | `106.52.207.52:42755` |
| 内网镜像 Registry | `10.24.0.28:5000` |
| 已验证 WorkerNode | `10.24.0.30`、`10.24.0.31` |
| 备用测试服务器 | `10.0.7.118:8080`，是否可用必须在任务开始时重新确认 |
| 本机网络代理 | 必要时使用 `http://127.0.0.1:10808` 访问 GitHub |

不得在本文记录服务器密码、IAM token、Cookie、SSH 私钥或数据库连接串。

生产发布物历史上没有可靠嵌入 Git SHA。虽然 `f56221b` 对应的镜像状态修复已经部署并通过页面/日志验证，下一次发布前仍须现场核对服务、release 目录、迁移头和制品摘要，不能只根据本文推断服务器二进制身份。

## 6. 现行缺口入口

- vNext 已知 API/验收缺口：`docs/yinyu-vnext-deferred-contract-gaps.md`
- 商业化总计划：`docs/platform-commercialization-master-plan.md`
- 商业化阶段进度：`docs/platform-commercialization-audit-progress.md`
- AWDP 人工验收：`docs/yinyu-awdp-manual-acceptance.md`
- Windows VM 简明规范：`docs/operations/windows-vm-quick-deployment-guide.md`
- 生产发布手册：`docs/operations/vnext-maintenance-window-rollout.md`

`docs/yinyu-vnext-production-baseline-20260721.md` 是 2026-07-21 的历史采样，不代表当前服务器仍停留在其中记录的版本。

## 7. 下一任务起点

当前没有正在编辑但未提交的源码任务。新任务应：

1. `git fetch origin --prune` 并确认本地基线。
2. 从 `origin/main` 建立新的 `codex/<task-name>` 分支。
3. 读取对应模块的现行契约和缺口文档。
4. 先证明一条真实端到端链路，再扩大改动范围。
5. 完成代码、测试、文档和必要部署验收后更新本文件。

推荐后续产品方向需要由负责人选择，不应由旧聊天自动延续：

- 完成自主练习模块的独立产品切片。
- 等 TeamLab 队友后端/契约稳定后实现 vNext 管理前端。
- 按 Phase 8-14 继续商业化主线。
- 优先处理线上明确复现的稳定性问题。
