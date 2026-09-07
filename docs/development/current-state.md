# YINYU 当前开发状态

更新时间：2026-09-07

本文件只记录已经核对过的当前事实、已知缺口和下一任务入口。历史计划、阶段审查和现场流水放在 `docs/archive/implementation-records/`，不得用来判断当前代码或服务器状态。

## 1. 基线

| 项目 | 当前事实 |
| --- | --- |
| 仓库 | `https://github.com/Y3X1L2/newGZCTF.git` |
| 稳定分支 | `main` |
| 当前生产基线 | release `practice-validation-9eef8ac12c626672081e81fadbde39946e7d2237`，提交 `9eef8ac12c626672081e81fadbde39946e7d2237`，数据库 migration head `20260816192540_TeamLabCapabilityClosure` |
| 应用回退基线 | `/opt/gzctf/publish.previous` 指向 release `docker-provisioning-inventory-3e5526dc-20260904T093342Z`，提交 `3e5526dc1ce336ac5545faacd49a9c0d1ec7ab58`；本次发布前完整备份见本文件第 5 节 |
| 当前开发基线 | `main`；Phase 09 TeamLab networking、迁移恢复和 Game 23 Docker provisioning 修复均已合入；新任务从最新 `origin/main` 创建 `codex/<task-name>` 功能分支 |
| 正式工作区 | 本次工作机为 `D:\newGZCTF` |
| 工作树结构 | 本次工作机只保留一个活动 worktree，分支为 `codex/practice-deployment-validation`；并行任务按 `AGENTS.md` 使用独立 worktree，不将服务器目录作为代码基线 |
| 技术栈 | .NET 10、ASP.NET Core、EF Core、PostgreSQL、Redis、React 19、TypeScript、Vite、pnpm |

开始新任务必须重新执行 `git fetch origin --prune`、读取 `git status` 和 `git log`。本表中的 SHA 不替代实时 Git 状态。

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

```text
浏览器
  -> 主站 Contracts / Application / Domain
       -> PostgreSQL：业务和运行状态事实
       -> Redis：缓存、租约、协调和高频缓冲
       -> Runtime / Fleet / VM / TeamLab ports
            -> AgentClient -> GZCTF.Agent -> Docker / KVM / 网络工具
```

- Controller 只处理协议、授权、用例调用和 HTTP 映射。
- 跨模块读取使用公开 query contract，写入使用 application command。
- Docker、VM、培训、AWDP 和 TeamLab 运行任务共用 `DeploymentQueueTicket`。
- Agent 只执行已校验的本机操作，不读取比赛、课程、计分或权限实体。
- 运行恢复以数据库事实和 Agent inventory 为依据，不从日志文本反推业务状态。

## 4. 已知缺口

这些事项不能在文档中写成“已上线”或“已签收”：

1. 自主练习已进入 `main`；分支候选 `9eef8ac` 已完成隔离验证并切入 `10.24.0.27` 生产。真实 Docker 练习实例、浏览器登录态、回滚演练和内容运营验收仍未执行，不能将页面/健康检查等同于完整运行链签收。
2. Phase 09 TeamLab networking 已在 10.24 前向迁移，相关修复包含在当前生产 `9eef8ac`；Game 23 Docker 创建、入口和销毁链路已实测。双 Worker 故障接管、长期流量留存、复杂服务注入、规模并发和完整跨节点 TeamLab 场景仍需现场签收。
3. Windows VM 仅按比赛场景支持；平台使用镜像内固定 RDP 账号，不要求普通比赛使用 Cloudbase-Init。仍需对合格镜像完成双实例、RDP/Guacamole、剪贴板、隔离和销毁清理验收。
4. AWDP 的真实攻击、修补、异常恢复和安全软件干扰场景由授权测试人员按 `docs/yinyu-awdp-manual-acceptance.md` 手工执行。
5. 统一认证对接方的门户源码不在本仓库；平台保留 Portal SSO 适配，跨网联调需在目标环境验证。
6. `main` 已恢复经历史 DLL 证实的 `20260814075023_AddAssetAndChallengeOwnership` 与 `20260815012026_AddExerciseCreatorTracking`；`20260604165857_AddTheoryExamEntities`、`20260604193010_SyncTheoryExam` 仍未在源码、可达 Git 历史或保留 DLL 中恢复，禁止伪造。生产已通过 TeamLab 生命周期销毁经授权的 `qqqtest1` 两条测试 runtime，并完成 Phase 09 前向发布；今后数据迁移仍必须先在新鲜生产备份副本验证。

## 5. 已验证环境事实

- 2026-08-25 核对 10.24 环境：活动 release 的 manifest 标记提交为 `d2cf79b`，但实际 `GZCTF.dll` 摘要与 manifest 不一致，说明该环境曾进行后端制品热替换；该 release 不作为开发基线。
- 同日发现活动 release 的 `files` 错误指向旧 release 的私有目录，导致数据库仍有记录、shared 中也存在实体文件，但 `/assets/*` 返回 404。在线链接已原子修正为 `/opt/gzctf/shared/files`，发布脚本已固定 shared 路径并增加回归断言。
- Game 23 共核对 31 个本地附件，shared 中缺失数为 0；题目 76 附件和示例 `challenge.md` 均从客户端返回 200，内容长度与 SHA-256 正确。
- 2026-08-31 已复核统一发布：10.24 的 `release-manifest.json.gitCommit` 等于 `stable-20260831` 所指提交，manifest 内主站和 Agent 文件摘要与磁盘一致，`publish/files` 指向 shared，主站与 Agent 无重启循环。
- 2026-09-01 已将 Phase 09 TeamLab networking 合并提交 `1a390432b1135da055a5a8488575fd10015f0bbd` 推入 `main`；本地 Release build、905 项后端单元测试、275 项前端测试、前端生产构建和 OpenAPI 生成契约测试通过。完整集成测试因本机 Docker Desktop 无法启动而未完成。
- 同日只读复核 10.24：主站与 Agent 服务为 `active/running`，首页、健康端点和公开 OpenAPI 返回 200；运行前端 SHA 仍为 `81a6e02b7dbe3d1f12094b606e5b3a93fd86de0c`，公开 OpenAPI 为 69 条路径，尚未包含本次新增的 connectors、resource-pools 和 device-packages 路由。
- 2026-09-03 已在 `codex/migration-drift-reconciliation` 从历史 DLL 恢复两条 creator migration，并在 PostgreSQL 16 副本中完成生产备份恢复、空库完整 bundle 和生产备份前向 bundle 验证。详细结论见 `docs/development/handoffs/2026-09-02-migration-drift-reconciliation.md`。
- 同日已在发布前备份 `/opt/gzctf/backups/teamlab-release-pre-migration-20260903T080343Z` 后，以完整事务将 6 个授权 `EnvironmentJson` 配置值清为空对象、4 个授权 `RoutingEnabled` 配置值设为 false；未删除 TeamLab 行、比赛绑定、runtime、队列或其他业务数据。清理后备份在隔离 PostgreSQL 16 容器成功恢复并执行当前 `main` bundle 至 `20260816192540_TeamLabCapabilityClosure`，核心业务表计数保持一致；隔离容器和 volume 已删除。
- 2026-09-03/04：获得追加授权后，`qqqtest1` 的两条测试 runtime 均通过平台生命周期销毁；原 pending create ticket 为 Cancelled，destroy ticket 为 Succeeded。生产库无 pending TeamLab ticket，所有现存 TeamLab runtime 为 Destroyed。
- 同期建立并校验新鲜回滚备份 `/opt/gzctf/backups/teamlab-runtime-converged-pre-migration-20260903T122601Z`；用其隔离副本复跑 `d90e2d1b` 发布包的真实 glibc bundle，成功从 124 条 migration 前向至 134 条和 `20260816192540_TeamLabCapabilityClosure`。生产随后用同一 bundle 执行 10 条前向 TeamLab migration，并原子切换至 `teamlab-phase09-d90e2d1b-20260903T1228Z`。release manifest 的 994 个文件及本机 Agent 摘要均已核对一致，主站、Agent、PostgreSQL、Redis、首页、health、OpenAPI、API docs、共享附件、节点 inventory 和队列均正常。
- 发布后生产的用户 172、比赛 22、比赛题目 110、课程 29、理论试卷 4、AWDP 服务 10、附件 217 与新鲜备份一致。`ExerciseChallenges` 为 590，而新鲜备份为 446；152 条 ID 大于 446 的新增行均为公共练习题，且本次 10 条 TeamLab migration 不向该表插入或回填数据。该业务增量未作处置，不能表述为 migration 导致的数据变化。
- 2026-09-04 已修复 Game 23 / Challenge 19 Docker provisioning 卡死：运行执行改为有界独立 in-flight，execution context 可跨嵌套 scope 传播，镜像引用和 cleanup 退避闭环，前端增加准备态最终超时。生产 ticket `01a06bda-d552-7c35-bd90-8bc21372ac39` 在约 3 秒内完成并调度至 `worker-10.24.0.30`，页面显示入口且 10.24 内网端口返回 200；Stop ticket `01a06be2-5347-7232-bd85-77122ee955b9` 成功，相关测试 Container 行和 Docker 资源均已清理。
- 同次验收发现并修复 Agent 两阶段同步门禁、心跳 `xmin` 与审计保存竞争、TeamLab `Enable` 配置持久化，以及本机 Docker 缺少 Agent inventory 标签的问题。三个节点均为 Online、Stable、schedulable，Agent SHA 前缀均为 `3747f3535da88623`；历史 image cleanup 记录从 301 条收敛为 0。生产本机 TeamLab control-plane 目标仍明确禁用，远端 Fabric 状态因此为 Disabled，不能表述为 TeamLab Fabric 已验收。
- 本次发布前回滚备份为 `/opt/gzctf/backups/agent-sync-pre-0a3e1c63-20260904T080316Z`；custom dump SHA-256 `03f7e38a120dcb586f5095b3cf6e7b1c22d7ebbae37a780ef51e0021d819d088`，`pg_restore -l` 可读 2,041 个条目，134 条 migration。最终核心计数为用户 172、战队 76、比赛 22、比赛题目 110、课程 29、练习题 605、理论试卷 4、AWDP 服务 10、附件 217，与该备份一致。
- 203 公网网关的 Nginx、WireGuard、动态 port-map timer 与 9091/18080 业务独立；本次只更新网关同步器所需配置，不重启或改动 9091/18080 进程。
- 2026-09-06：`codex/practice-deployment-validation` 的运行候选
  `9eef8ac12c626672081e81fadbde39946e7d2237` 在 fork Actions run
  `33979724855` 通过前端 280 项、后端单元 971 项、集成 275 项、迁移模型、
  7 组查询计划、OpenAPI 向后兼容和完整 Linux release 构建。发布 manifest
  精确覆盖 372 个文件，前端与 release SHA 一致；发布脚本已修复 Linux dotfile
  漏记问题。
- 同一候选在 `10.24.0.27` 的无路由 network namespace 中完成隔离验收：空库
  bundle 生成 132 条实际可发现迁移，主站以 `www-data` 且无 Docker/libvirt/KVM
  文件描述符启动，首页、health、OpenAPI、注册登录、权限拒绝、资产上传幂等/
  冲突、练习导入 operation 和附件摘要回读通过。经 SHA256SUMS 验证的
  `agent-sync-pre-0a3e1c63-20260904T080316Z` 备份副本保持 134 条历史迁移，
  候选 bundle 报告无待应用迁移，迁移前后用户 172、战队 76、赛事 22、赛题
  110、课程 29、练习 605、理论试卷 4、AWDP 服务 10、文件 217、镜像 456
  均不变。
- `20260802023000_RemoveDestroyedTeamLabUdpMappings.cs` 缺少 Designer/迁移元数据，
  不属于上述 132 条 bundle migration；不能把文件存在误报为可执行迁移。严格
  隔离下内部 `/api/Exercise` 会因 Controller 构造时初始化 Docker provider 而在
  无 socket 条件返回 500，因此生产副本上的既有练习列表与真实 Docker/KVM/
  TeamLab 执行链仍为 `NOT_RUN`。本次未挂生产 Docker socket，未切换生产；所有
  测试进程、容器、volume、release 和目录已删除，随后生产 release、PID、服务
  重启次数、HTTP 状态、迁移与核心计数复核不变。
- 2026-09-06 生产维护窗口：在确认活动部署票据、容器、VM 和 TeamLab runtime
  均为 0 后，停止主站与 Agent，于服务器保存发布前备份
  `/opt/gzctf/backups/practice-deployment-pre-9eef8ac-20260906T032027Z`。备份总计
  1,359,448,361 bytes，数据库 dump 776,051,891 bytes、2,063 个 catalog 条目，
  SHA-256 为 `fa62f3473dc3693fb23a1be4ae1c0285e6800289b3a2670160b6c9904d26284d`；
  同时保存 schema、迁移历史、核心计数、旧 release、共享文件、应用配置、
  systemd 和 Nginx，`SHA256SUMS` 全部通过且 catalog 包含 DataProtectionKeys。
- 同一窗口中候选 bundle 报告 `No migrations were applied`，迁移保持 134/head
  `20260816192540_TeamLabCapabilityClosure`，备份前后核心计数一致；随后原子切换
  `/opt/gzctf/publish` 至 `practice-validation-9eef8ac.../publish`，旧 `3e5526dc`
  release 保留在 `/opt/gzctf/publish.previous`。服务停止于 `03:20:27Z`，新 release
  于 `03:26:46Z` 健康，维护窗口约 6 分 19 秒。
- 发布后主站 PID 36118、Agent PID 36120，均 active/running、NRestarts 0；主站、
  登录页、练习路由、配置、OpenAPI、API docs、health、metrics 和共享附件摘要检查
  通过。manifest 372 个文件，主站/Agent/前端摘要与提交 `9eef8ac` 一致；数据库仍为
  用户 172、战队 76、赛事 22、赛题 110、课程 29、练习 605、理论试卷 4、AWDP
  服务 10、文件 217、镜像 456，活动票据和镜像工作均为 0，journal 无 error。
- 节点现场状态为 Local Server 与 `worker-10.24.0.30` 在线可调度；
  `worker-10.24.0.31` 心跳已中断约 25.9 小时，早于本次发布，不归因于切换。
  browser-harness 因现有 Edge 未允许 remote debugging 而无法附着，未改用其他
  浏览器工具；因此真实登录和 Docker 练习实例仍为 `NOT_RUN`。本次只切换
  `10.24.0.27` 主站与本机 Agent，未同步远端 Worker Agent。

## 6. 当前有用文档

- 总体架构和目标：[平台架构与产品总纲](../platform-commercialization-master-plan.md)
- 文档入口：[文档导航](../README.md)
- AI 交接：[AI 开发与交接规范](../development/ai-development-playbook.md)
- 模块边界：[模块边界图](../commercialization/module-boundary-map.md)
- 外部接口：[Open API v1 指南](../commercialization/open-api-v1-guide.md)
- 生产发布：[生产发布与回滚手册](../operations/vnext-maintenance-window-rollout.md)
- Windows VM：[简明部署指南](../operations/windows-vm-quick-deployment-guide.md)
- AWDP：[人工验收指南](../yinyu-awdp-manual-acceptance.md)
- TeamLab：[功能说明](../commercialization/teamlab-networking-feature-guide.md)

## 7. 新任务起点

练习模块增量见 [2026-09-05 整理记录](handoffs/2026-09-05-practice-consolidation.md)。用户于 2026-09-07 改为 PR 交付，已从 `csc-dsc:codex/practice-deployment-validation` 向 `Y3X1L2/newGZCTF:main` 创建 [PR #9](https://github.com/Y3X1L2/newGZCTF/pull/9)；旧 PR #8 保持关闭，本轮不合并、不重新部署。当前交接入口为 [分支与服务器验证交接](handoffs/2026-09-05-pr8-branch-deployment-handoff.md)：原 11 项失败、隔离部署和 `10.24.0.27` 生产切换已闭环；下一步应审查 PR、确认 `.31` 节点并补真实登录和 Docker 练习实例验收，再决定是否同步远端 Agent。不得把仍为 `NOT_RUN` 的执行面写成已签收。

1. 同步远端并确认当前分支、工作树和 HEAD。
2. 阅读本文件、`docs/README.md`、`AGENTS.md` 以及任务涉及模块的现行契约。
3. 先从源码、真实路由、API 和测试确认事实，不引用归档文档中的旧路径或旧状态。
4. 代码、测试和必要文档在同一提交中闭环；部署时记录备份、发布物、冒烟和回滚信息。
5. 任务结束后只更新本文件的当前事实，不追加聊天流水。
