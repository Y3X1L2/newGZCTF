# YINYU 当前开发状态

文档整理日期：2026-09-16
最近一次生产核验：2026-09-16 06:00 UTC（北京时间 14:00，课程修复发布专项）

本文件仅保留最新已知基线、功能边界、未解决事项和接手入口。本次核验覆盖课程修复发布及相关服务；其他模块现场验收以对应记录为准，不能代替下一次操作前的检查。长期协作规则见 [AGENTS.md](../../AGENTS.md)。

## 课程实例修复已部署（2026-09-16）

课程 32 / 章节 69 的实例覆盖和缺失 Flag 已修复并发布。原 main 开发修复位于 `codex/training-instance-flag-fix`；部署为维护分支 `codex/training-instance-flag-release` 的 `eaac7f2684755f6471b684c2432e6e18dea3eb92`，基于现场版本回移同一修复，无数据库迁移、前端或 Agent 更新。

正式 API 已确认 627、629、630 三个实例同时运行，入口分别为 `203.195.157.191:30001`、`:30002`、`:30000`。629 的缺失 Flag 关联已由平台创建流程修复；三容器环境变量与各自数据库值匹配，只记录布尔结果。独立停止并重建 629 后另外两个实例保持不变，最终三个实例留给用户继续测试。未解题、未读取题目 Flag 文件、未提交 Flag。

主站、Agent、三节点、队列、镜像和共享附件核验通过；健康端点仍为既有 Degraded。维护分支 Release 构建和课程定向 22 项单测通过，全量单测 1139/1141，两个既有 TeamLab 断言失败保留记录。新鲜备份已完整恢复验证；详见 [发布记录](handoffs/2026-09-16-training-hotfix-rollout.md)和 [开发交接](handoffs/2026-09-16-training-instance-flag-fix.md)。

## 1. 基线

| 项目 | 最新已知事实 |
| --- | --- |
| 仓库 / 开发分支 | `https://github.com/Y3X1L2/newGZCTF.git` / `main`；开发从最新 `origin/main` 创建任务分支 |
| 固定发布标签 | `stable-20260908` → `ab2bd54b7e16d454e3a8f54960c4bb4689047c4a`；已推送 annotated tag，不移动标签 |
| 主站发布 | `10.24.0.27` 已发布课程修复 `eaac7f2684755f6471b684c2432e6e18dea3eb92`，目录 `/opt/gzctf/releases/training-instance-flag-eaac7f2684755f6471b684c2432e6e18dea3eb92-20260916/publish`；只替换主站 DLL，1210 个文件 manifest 已校验，Agent 与前端沿用原制品 |
| 持久化附件 | `/opt/gzctf/publish/files` → `/opt/gzctf/shared/files` |
| 数据库 | 140 条迁移历史，head `20260908111521_TeamLabDeviceObservation`；本次无迁移，新鲜备份已完整恢复验证 |
| 应用回退 | `/opt/gzctf/publish.previous` → `/opt/gzctf/releases/agent-fabric-dd2f464da2bf-20260910` |
| 备份 | `/opt/gzctf/backups/training-eaac7f26-pre-20260916T052014Z`；数据库及附件备份摘要已核对，数据库已在隔离容器完整恢复，临时恢复库已清理 |
| 执行节点 | 2026-09-16 06:00 UTC 核验三节点均 Online、可调度且心跳新鲜；本次没有更新或重启 Agent |
| 健康状态 | 指标端口 `3001/healthz` 为 HTTP 200 / Degraded；业务端口 `8080/healthz` 按契约返回 404；降级原因见未解决事项 |
| 技术栈 | .NET 10、ASP.NET Core、EF Core、PostgreSQL、Redis、React 19、TypeScript、Vite、pnpm |

版本关系以实时 `git fetch origin --prune`、`git status` 和 `git log` 为准。需要复现该次生产源码时使用固定标签；`main` 后续是否仅有文档变化，应重新比较，不能长期假定。发布包摘要、回退边界和验收依据集中在 [稳定基线说明](handoffs/2026-09-08-stable-baseline.md)。

当前课程修复发布基于已运行的维护分支 `79c37823`，回移原 main 分支修复后形成 `eaac7f26`，已推送但未合并 main。原前端、Agent 和依赖保持现场版本；源码与继承制品身份见本次发布 manifest 和发布记录。

## 2. 当前功能边界

### 已存在的代码能力

- **CTF 赛事**：赛事、战队、题目、附件、静态/动态 Flag、Docker、KVM/Windows VM、提交、计分和榜单。
- **理论考试**：题库、组卷、单选/多选/判断、草稿、最终提交、成绩和答案回顾。
- **培训课程**：课程、共同教师、报名审核、章节、资源、实验、课后理论、学习进度和学员详情。
- **自主练习**：`/practice`、题库浏览、筛选、来源导入、附件、多 Flag、Docker 实例、提交、统计和后台题库管理；实例继续复用 `DeploymentQueueTicket`。
- **AWDP**：服务、轮次、Checker、攻击、修补、重置、恢复、停止、计分和日志；真实攻击/修补流程按人工验收文档执行。
- **运行底座**：Docker/KVM 节点、镜像模板、镜像导入与分发、容量预留、统一部署队列、实例、事件、日志和恢复。
- **TeamLab**：场景草稿、校验、不可变发布版本、试运行、混合资产、执行计划 V2、OVN/OVS 数据面、访问授权、远程运维、链路策略、连接器、设备包、资源池、流量和抓包基础能力。
- **身份与通用管理**：本地登录、Portal SSO、用户、战队、学员组、系统设置、个人主页和主要管理页面。

### 前端事实

正式前端入口位于 `src/GZCTF/ClientApp/src/vnext`。路由注册以以下文件为准：

- `src/GZCTF/ClientApp/src/vnext/app/VNextApp.tsx`
- `src/GZCTF/ClientApp/src/vnext/app/shell/moduleRegistry.ts`

新增页面必须使用 vNext 壳层、feature API adapter、CSS Module 和语义 Token；未实现能力显示真实空态，不加载旧页面套壳，不伪造数据。

## 3. 运行架构

主站为模块化单体，Agent 为独立执行面；PostgreSQL 保存业务和运行事实，Redis 承载缓存、协调和高频缓冲。Docker、VM、培训、AWDP 和 TeamLab 共用 `DeploymentQueueTicket`。

依赖方向、模块所有权和前端边界遵循 [AGENTS.md](../../AGENTS.md) 与 [模块边界图](../commercialization/module-boundary-map.md)，不在状态文档重复维护另一套规则。

## 4. 未解决事项

- **数据保留 SQL**：`teamlab-flow` 分区清理存在 PostgreSQL `42601` 语法错误，最近核验仍按小时发生；需要独立修复及定向回归，不能用删除生产数据绕过。
- **指标并发**：2026-09-08 观察到一次指标持久化 `DbUpdateConcurrencyException`，后续心跳/指标恢复；仍需并发和失败批次重试回归，不能将自动恢复等同于根因已修复。
- **迁移来源**：`20260604165857_AddTheoryExamEntities`、`20260604193010_SyncTheoryExam` 的来源仍未恢复，导致 134 条数据库历史与 132 条可发现迁移存在差异。另有 `20260802023000_RemoveDestroyedTeamLabUdpMappings.cs` 缺少迁移元数据，不能按源码文件数认定 bundle 会执行。禁止伪造或删除历史；后续迁移须在新鲜生产备份副本验证。详见 [迁移交接](handoffs/2026-09-02-migration-drift-reconciliation.md) 与 [发布核验](handoffs/2026-09-08-pr9-production-rollout.md)。
- **依赖与存储回收**：SSH.NET 已知依赖风险和 Blob 自动 GC 缺口仍未处理；风险范围见 [PR 审计](handoffs/2026-09-07-pr9-review-merge.md)。
- **节点一致性与持久性**：远端 Worker Agent 仍为兼容的混合版本；如需统一，应另开维护任务逐节点同步。已安装的 `.31` 网桥恢复机制尚未完成整机重启验收。
- **自主练习与回退**：核心业务测试已完成并由用户确认；内容运营验收和真实应用回退演练仍未完成。
- **TeamLab**：双 Worker 故障接管、长期流量、复杂服务注入、规模并发和完整跨节点场景仍需现场签收；Fabric Disabled 不代表组网验收通过。
- **Windows VM**：当前仅按比赛场景支持，仍需合格镜像双实例、RDP/Guacamole、剪贴板、隔离与销毁清理验收。
- **AWDP / Portal SSO**：AWDP 真实攻击、修补和异常恢复按 [人工验收手册](../yinyu-awdp-manual-acceptance.md) 执行；门户源码不在本仓库，SSO 跨网联调仍需目标环境验证。

## 5. 验证依据

- 固定发布提交已通过完整 CI、隔离真实 Docker 生命周期及附件引用保护验证；测试数量和运行编号见 [候选交接](handoffs/2026-09-07-pr9-runtime-convergence.md)，不作为未来提交已通过验证的依据。
- 生产发布完成两份数据库备份恢复、制品/进程核验、关键 API 和共享附件核验；用户接手完成业务测试并删除临时题，随后只读确认对应题目、实例和容器记录不存在。详见 [生产发布交接](handoffs/2026-09-08-pr9-production-rollout.md)。
- 2026-09-10 在测试主站 `10.0.7.118` 与执行节点 `10.0.7.125` 完成 H03 主站队列全链路：Docker PLC、libvirt VM、受管网卡连接器和 namespace 模拟现场端点互通，`field` 与 `isolated` 隔离，平台抓包 23,782 字节；端口断开、正式 reset、`ovn-controller` 重启、连接器独占和最终无残留销毁均通过。VM 已使用 NoCloud CIDATA 获得计划静态地址并通过 SSH/QGA 验证。该结果不替代真实现场网卡、具体 PLC 型号或多 Worker 跨宿主认证。
- 2026-09-10 将提交 `a8438f676a747876312ed5e5b3475270efb27f91` 发布到生产主站与本机 Agent。生产备份副本完成 134 → 140 条迁移验证，既有业务表计数保持一致；发布后 999 个 manifest 文件摘要一致，首页、配置 API、认证边界、指标健康端点和已知附件下载通过，三节点在线可调度。已知 `teamlab-flow` 数据保留 SQL `42601` 在新进程中仍会定时复现，除此之外未观察到发布引入的新错误。完整证据见 [本次生产发布交接](handoffs/2026-09-10-teamlab-production-rollout.md)。
- 已修复缺陷、旧版本切换、测试过程、旧计数和会话流水移出当前状态；需追溯时查看 [环境核验历史](../archive/implementation-records/2026-09-08-environment-verification-history.md)。归档不参与当前执行决策。

## 6. 文档入口

| 需要了解什么 | 入口 |
| --- | --- |
| 固定生产基线、发布物与版本关系 | [稳定基线说明](handoffs/2026-09-08-stable-baseline.md) |
| 本次部署、备份、回退及人工验收 | [生产发布交接](handoffs/2026-09-08-pr9-production-rollout.md) |
| 选择功能、接口和专项文档 | [文档导航](../README.md) |
| 开发与模块边界 | [AGENTS.md](../../AGENTS.md)、[模块边界图](../commercialization/module-boundary-map.md) |
| 发布流程 | [生产发布与回滚手册](../operations/vnext-maintenance-window-rollout.md) |

## 7. 新任务起点

1. 核对用户当前目标、权限范围、工作树及最新远端提交；不要继承历史交接中的一次性授权或限制。
2. 优先处理数据保留 SQL 缺陷和指标并发回归，再按任务范围推进专项验收；从最新 `origin/main` 创建独立任务分支。
3. 以当前源码、合同、测试及必要的现场证据确认问题；发布使用新候选、新标签和新鲜备份，不原地覆盖固定基线。
4. 完成后更新本文件的最新基线或未解决事项；修复过程、运行编号、完整测试与部署记录写入对应 handoff，避免再次向本文件追加时间线。
