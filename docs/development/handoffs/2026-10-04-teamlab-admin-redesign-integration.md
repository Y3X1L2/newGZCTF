# TeamLab 管理后台与受管网络集成交接

## 任务目标

- 用户目标：在独立工作树将已部署验证的受管网络分支与 TeamLab 管理后台重设计集成，解决冲突、完成门禁并提供可审查分支。
- 明确不做：不合并 `main`，不部署主站或 Agent，不修改生产数据库、Registry、网关、原工作区、其它工作树或保留的 Lab2 四机。
- 验收标准：两条来源分支历史均保留；逐网卡网络语义和管理员完整工作流不回退；前后端、迁移和多视口门禁通过。

## 基线

- 起始分支：`origin/main`
- 当前任务分支：`codex/teamlab-admin-redesign-integration`
- Worktree 路径：`C:\Users\Cloud\.codex\worktrees\ec6c\newGZCTF`
- 起始提交：`bc3599aaac03d951369e49f5e269e984e645459b`
- 远端 `origin/main`：任务开始及完成前最近核对均为 `bc3599aaac03d951369e49f5e269e984e645459b`
- 合并来源：`f36a491d8afdcc6eeab94806c7126c7e74ce0995`、`bff10f0f2057a71656e4e71546b6758487db5b0d`
- 涉及环境：仅本地工作树、PostgreSQL 16 Testcontainers 和本地 Vite fixture；未连接生产服务器。

## 当前状态

- `in progress`：本地实现和验证完成，待推送分支并创建审查 PR。
- 证据状态：`VERIFIED`
- 已完成：两次正常 merge、冲突处理、生成 API 刷新、代码/契约审计、全量门禁、迁移分支专项和多视口浏览器验证。
- 正在进行：提交交接并推送远端。
- 尚未完成：生产部署和集成候选的真实 VM/文件/远程会话复验；这些操作需要用户另行明确批准。

## 技术结论

- 根因或架构判断：两条分支都基于 `bc3599aa` 并共同包含 `374bda24`，但随后独立修改 TeamLab 契约、生成 API、前端 adapter、运行更新服务和模型快照；受管网络未进入 `main`，必须先合并网络基线再合并重设计。
- 采用方案：正常 merge 保留历史；生成 API 从合并后 TestServer 契约重新生成；资产执行定义统一通过 `TeamLabTopologyV1Normalizer.ToModel` 转换并补齐受管网络字段。
- 放弃方案及原因：没有选择任一分支整文件覆盖，因为会分别丢失重设计交互或逐网卡网络语义；没有手工拼接生成 API。
- 关键不变量：`Dhcp=0`、`Preconfigured=1` 不重解释；VM 模式和逐网卡接口名/DNS/默认网关/路由完整往返；主网卡、DNS 归属和默认路由保持独立；界面不自动增加跨网段连接；异步受理不显示为执行成功。

## 改动范围

- 文件/模块：合并两条来源分支；冲突修正集中在状态文档、生成 API、镜像目录、运行事件面板、运行资产更新服务和拓扑 normalizer；新增三条集成回归测试。
- 数据库迁移：保留 `20260930164900_AddSystemLogResourceIndex` 与 `20261001145237_AddTeamLabManagedGuestNetwork`；没有新增迁移。
- API 契约：合并后 TestServer 导出内部 OpenAPI，使用仓库 `swagger-typescript-api` 模板重新生成 `src/generated/Api.ts`。
- 前端交互：保留重设计工作区，并补齐镜像目录联合映射、来宾网络阶段展示及资产替换的逐网卡字段保护。
- 部署/基础设施：无修改。

## 验证证据

| 验证项 | 命令或步骤 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build src/GZCTF.slnx -c Release` | 通过，0 错误；保留既有 NuGet 中危漏洞告警 |
| 单元测试 | `dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build` | 1284/1284 通过 |
| 集成测试 | `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj -c Release --no-build` | 304/304 通过 |
| 迁移专项 | 模拟较新受管网络迁移已记录、较早日志索引迁移缺失 | PostgreSQL 16 通过；索引补建、5 个网络列保留、无 pending migration |
| 前端 | `pnpm build` | locale、lint、类型、架构、113 文件/376 测试、Vite、制品与体积预算通过 |
| 定向前端 | TeamLab adapter/editor/runtime/resource 10 文件 | 59/59 通过；另有新增镜像目录和事件面板测试进入全量计数 |
| 浏览器 | 本地 Vite + Playwright fixture，390/1366/1920/2560，日夜与键盘 | 无横向溢出或控制台错误；Tab 焦点、Enter 主题切换和 reduced-motion 通过 |
| 通用 | `git diff --check` | 通过 |
| 真实环境 | 未执行 | 本任务无部署授权；不复写 2026-10-03 已有网络现场证据 |

首次 `dotnet build --no-restore` 因新工作树缺少 AppHost `project.assets.json` 失败；按标准命令还原后通过，不计为代码失败。

## 提交与部署

- 集成计划提交：`0fcaf65a`
- 受管网络 merge：`cb13cd45`
- 管理后台 merge 与冲突修正：`263928a6`
- 推送分支：待本交接提交后推送 `codex/teamlab-admin-redesign-integration`
- 是否已合并 `main`：否
- 是否已删除任务分支和 worktree：否，保留供审查
- 部署环境：未部署
- 发布物/备份：未创建；不复用历史发布物冒充当前候选
- 冒烟结果：本地 fixture 页面通过，不是生产冒烟
- 回滚方式：未部署，无生产回滚操作；代码审查阶段可在本任务分支继续修正

## 风险与后续

- 已知风险：日志资源索引在约 3580 万行历史分区上的生产创建可能锁写，部署前必须用新鲜备份副本测量并安排维护窗口。
- 待办事项：集成候选尚未做真实镜像登记、文件往返、SSH/RDP/VNC、服务端口续租、生命周期和 VM 网络复验；容量上报误读根盘问题仍是独立缺口。
- 下一位接手者第一步：审查集成 PR 的冲突修正和迁移顺序；如用户要求部署，先准备候选身份、备份、副本迁移、锁表评估和回退方案，再请求本次部署确认。
