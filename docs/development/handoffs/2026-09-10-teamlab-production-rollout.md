# 2026-09-10 TeamLab 生产发布交接

## 任务目标

- 检查并同步最新 `origin/main`，构建包含主站、前端、迁移器和 Agent 的完整发布包。
- 将候选部署到生产主站 `10.24.0.27`，同步该主机上的 Agent，保留业务数据库、共享附件和可用回退版本。
- 不更新 `.30`、`.31` Worker，不执行远端 Agent 统一或业务数据清理。

## 基线

- 任务分支：`codex/teamlab-production-rollout-20260910`
- 发布提交：`a8438f676a747876312ed5e5b3475270efb27f91`
- 版本关系：正常合并 TeamLab 功能基线 `b8e2baffe0505fad952efb47eab889a297c9b837` 与当时最新 `origin/main` `e10097ef8dcc98b76ea3477ebc5d6c1b5d19af65`。
- 旧发布：`/opt/gzctf/releases/pr9-converged-ab2bd54b7e16d454e3a8f54960c4bb4689047c4a-20260908/publish`
- 新发布：`/opt/gzctf/releases/teamlab-production-a8438f676a747876312ed5e5b3475270efb27f91-20260910/publish`

## 当前状态

- 状态：`complete`
- 证据状态：`VERIFIED`
- 主站与本机 Agent 已通过同一 release 原子切换并稳定运行。
- `.30`、`.31` Worker 未更新，继续使用已知兼容版本。

## 发布物与备份

- 发布归档：`teamlab-production-a8438f676a747876312ed5e5b3475270efb27f91-20260910.tar.gz`
- 归档 SHA-256：`82c89d170d240897d837379819d2008a3a659a9f901c038e37e65212d447d62d`
- 发布 manifest：999 个文件，Git 提交字段与发布提交一致；生产目录逐文件长度和 SHA-256 校验无错误。
- 新鲜备份：`/opt/gzctf/backups/teamlab-a8438f6-pre-20260910T033757Z`
- 数据库 custom dump：798,411,581 字节；共享文件归档：315,754,151 字节，共 882 个文件。
- 数据库备份 SHA-256：`efadcbf283529a69f7194c516929a4275d896748891c5ca4c3a2cb55d9b47aef`；共享文件归档 SHA-256：`e84e7638570be4fd95f877ba32c3c86a565d4ab33ce3a8c56efa771050c7549f`。
- 数据库 dump 已通过 `pg_restore -l` 检查，并恢复到独立 PostgreSQL 16 容器和独立 volume；临时容器、volume 和迁移验证目录均已清理。
- `publish.previous` 指向旧 PR9 release，旧 release、旧备份和共享附件均保留。

## 迁移验证

- 生产原迁移：134 条，head `20260816192540_TeamLabCapabilityClosure`。
- 候选迁移：140 条，head `20260908111521_TeamLabDeviceObservation`。
- 6 条新增 TeamLab 迁移先在生产备份副本执行成功，再在维护窗口应用到生产。
- 新增迁移只增加远程审计就绪时间、远程会话创建阶段、资产电源意图、SFTP 主机身份、链路策略代次和设备观测字段；远程会话迁移包含兼容性回填。
- 副本迁移前后用户、战队、赛事、课程、练习、理论、AWDP、镜像、节点、TeamLab 等既有业务表记录数保持一致。`pg_stat_user_tables` 的 4 个高写入日志/治理表估算值在 `ANALYZE` 后变化，不是实际删改；迁移历史按预期增加 6 条。

## 验证证据

| 验证项 | 结果 |
| --- | --- |
| Git | 发布提交已推送到 `origin/codex/teamlab-production-rollout-20260910`；工作树在文档提交前干净 |
| 后端构建 | `dotnet build src/GZCTF.slnx -c Release` 成功，0 错误、35 个既有警告 |
| 后端单测 | 1121/1123 通过；2 条既有 TeamLab 调度观察点计数断言仍失败，没有新增失败 |
| 集成测试 | 本地 Docker Desktop 当时不可用，289 项中 287 项受 `docker_engine` 环境阻塞、2 项通过；真实生产迁移改由隔离 PostgreSQL 副本验证 |
| 前端 | locale、lint、TypeScript、架构检查、102 个测试文件/326 条用例、生产构建与包体预算全部通过 |
| 服务 | `gzctf.service`、`gzctf-agent.service` 均 active/running，`NRestarts=0`，实际进程路径指向新 release |
| HTTP | 首页 200、`/api/Config` 200、未认证 `/api/exercise` 401、`3001/healthz` 200/Degraded |
| 数据 | 用户 172、战队 76、赛事 22、比赛题目 110、课程 29、练习 617、理论试卷 4、AWDP 服务 10、Files 217、Attachments 208、镜像 462、节点 3、TeamLab 运行时 8、运行资产 47 |
| 附件 | 已知附件通过 HTTP 返回 253,892 字节，SHA-256 为 `291b802a28935b082fb3d46a5a2358c167a6f9912e21ef14ccc097a5b34981d6` |
| 节点 | `.27`、`.30`、`.31` 均为 Online 且可调度，心跳新鲜；`.27` Agent 摘要为 `76c8273e...` |
| 日志 | 启动稳定后 5 分钟内主站和 Agent 无新增 error/exception/failure |

## 已知边界

- `3001/healthz` 仍为 Degraded，原因包括两条历史 Theory migration 来源缺口；本次没有伪造历史或放宽健康门禁。
- 数据保留任务仍存在既有 PostgreSQL `42601` 语法错误；本次发布前仍观察到，发布后稳定观察窗口内没有新增，但根因尚未修复。
- Agent 启动早于主站完成监听时产生过一次短暂 heartbeat connection refused，主站就绪后自行恢复，没有持续错误。
- 本次没有更新或重启 `.30`、`.31`，也没有执行需要新增业务数据的 Docker、VM、AWDP 或 TeamLab 生产链路测试。

## 回退

如出现与本版本相关的严重回归，停止主站与本机 Agent，将 `/opt/gzctf/publish` 原子切回当前 `publish.previous` 指向的旧 PR9 release，再启动两个服务并复核首页、节点、队列和附件。数据库已完成前向加列迁移；如确认旧应用与新结构不兼容，使用本次新鲜 custom dump 恢复，而不是删除迁移记录或业务数据。
