# YINYU 当前开发状态

更新时间：2026-09-01

本文件只记录已经核对过的当前事实、已知缺口和下一任务入口。历史计划、阶段审查和现场流水放在 `docs/archive/implementation-records/`，不得用来判断当前代码或服务器状态。

## 1. 基线

| 项目 | 当前事实 |
| --- | --- |
| 仓库 | `https://github.com/Y3X1L2/newGZCTF.git` |
| 稳定分支 | `main` |
| 运行代码基线 | 发布标识 `stable-20260831`，提交 `81a6e02b7dbe3d1f12094b606e5b3a93fd86de0c`；纯文档提交可以在该标签之后进入 `main` |
| 当前开发基线 | `main`；Phase 09 TeamLab networking 合并提交为 `1a390432b1135da055a5a8488575fd10015f0bbd`；新任务从最新 `origin/main` 创建 `codex/<task-name>` 功能分支 |
| 正式工作区 | `D:\Work\newGZCTF` |
| 工作树结构 | 单一主仓库、单一 worktree；不依赖其他本地目录 |
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

1. 自主练习已进入 `main`，但仍需在目标生产数据库备份或副本上完成迁移、真实实例、发布、回滚和内容运营验收。
2. Phase 09 TeamLab networking 代码已进入 `main`，但 10.24 稳定环境仍运行 `stable-20260831`，新增迁移和运行能力尚未发布；双 Worker 故障接管、长期流量留存、复杂服务注入、规模并发和完整跨节点场景仍需真实环境签收。
3. Windows VM 仅按比赛场景支持；平台使用镜像内固定 RDP 账号，不要求普通比赛使用 Cloudbase-Init。仍需对合格镜像完成双实例、RDP/Guacamole、剪贴板、隔离和销毁清理验收。
4. AWDP 的真实攻击、修补、异常恢复和安全软件干扰场景由授权测试人员按 `docs/yinyu-awdp-manual-acceptance.md` 手工执行。
5. 统一认证对接方的门户源码不在本仓库；平台保留 Portal SSO 适配，跨网联调需在目标环境验证。
6. 10.24 数据库历史曾包含源码中不存在的 `20260815012026_AddExerciseCreatorTracking`，当前源码最新可见迁移为 `20260816192540_TeamLabCapabilityClosure`；任何生产迁移或数据库开发前必须在副本完成迁移历史和 schema 对比，禁止直接改生产迁移表。

## 5. 已验证环境事实

- 2026-08-25 核对 10.24 环境：活动 release 的 manifest 标记提交为 `d2cf79b`，但实际 `GZCTF.dll` 摘要与 manifest 不一致，说明该环境曾进行后端制品热替换；该 release 不作为开发基线。
- 同日发现活动 release 的 `files` 错误指向旧 release 的私有目录，导致数据库仍有记录、shared 中也存在实体文件，但 `/assets/*` 返回 404。在线链接已原子修正为 `/opt/gzctf/shared/files`，发布脚本已固定 shared 路径并增加回归断言。
- Game 23 共核对 31 个本地附件，shared 中缺失数为 0；题目 76 附件和示例 `challenge.md` 均从客户端返回 200，内容长度与 SHA-256 正确。
- 2026-08-31 已复核统一发布：10.24 的 `release-manifest.json.gitCommit` 等于 `stable-20260831` 所指提交，manifest 内主站和 Agent 文件摘要与磁盘一致，`publish/files` 指向 shared，主站与 Agent 无重启循环。
- 2026-09-01 已将 Phase 09 TeamLab networking 合并提交 `1a390432b1135da055a5a8488575fd10015f0bbd` 推入 `main`；本地 Release build、905 项后端单元测试、275 项前端测试、前端生产构建和 OpenAPI 生成契约测试通过。完整集成测试因本机 Docker Desktop 无法启动而未完成。
- 同日只读复核 10.24：主站与 Agent 服务为 `active/running`，首页、健康端点和公开 OpenAPI 返回 200；运行前端 SHA 仍为 `81a6e02b7dbe3d1f12094b606e5b3a93fd86de0c`，公开 OpenAPI 为 69 条路径，尚未包含本次新增的 connectors、resource-pools 和 device-packages 路由。
- 203 公网网关的 Nginx、WireGuard、动态 port-map timer 与 9091/18080 业务独立；本次只更新网关同步器所需配置，不重启或改动 9091/18080 进程。

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

1. 同步远端并确认当前分支、工作树和 HEAD。
2. 阅读本文件、`docs/README.md`、`AGENTS.md` 以及任务涉及模块的现行契约。
3. 先从源码、真实路由、API 和测试确认事实，不引用归档文档中的旧路径或旧状态。
4. 代码、测试和必要文档在同一提交中闭环；部署时记录备份、发布物、冒烟和回滚信息。
5. 任务结束后只更新本文件的当前事实，不追加聊天流水。
