# 课程实例修复发布记录

## 授权与范围

用户明确要求推送并部署课程实例修复，允许停止/销毁课程 32 / 章节 69 的测试实例。验收只检查正式平台 API、实例生命周期、端口、数据库与容器环境变量的匹配布尔结果；不解题、不访问漏洞接口、不提交 Flag、不读取题目中的 Flag 文件。

## 发布基线和制品

- 原开发修复：`1515fb9af702ff885d0fa67ab42acbf117e9a086`，位于从最新 main 创建的 `codex/training-instance-flag-fix`。
- 现场维护分支：`origin/codex/prod-agent-fabric-fix-20260910`，head `79c378236e83006eccd63359a36de1c7bf9bf078`。现场主站 DLL SHA-256 与历史 `dd2f464d` 增量包一致；`79c37823` 只追加 Agent 配置加载修复，没有主站源码变化。
- 部署专用 worktree：`D:/Work/newGZCTF-training-release`，分支 `codex/training-instance-flag-release`。为避免夹带最新 main 的 TeamLab 重构和数据库迁移，从已运行的维护分支回移同一修复；代码提交 `eaac7f2684755f6471b684c2432e6e18dea3eb92` 已推送并回读远端引用。
- 原发布目录：`/opt/gzctf/releases/agent-fabric-dd2f464da2bf-20260910`。
- 当前发布目录：`/opt/gzctf/releases/training-instance-flag-eaac7f2684755f6471b684c2432e6e18dea3eb92-20260916/publish`。
- 候选复制原 release 后只替换 `GZCTF.dll`；前端、Agent、运行依赖、配置和共享附件链接沿用现场版本。1210 个发布文件的新 manifest 明确记录继承来源；逐项对比确认 payload 只有主站 DLL 改变。
- 新主站 DLL SHA-256：`b388ffac04a960fbe21061e4b80fefeda1c2bd014d282d3b509f230118269f8f`。
- manifest SHA-256：`01c5198794c0c50af4ad8d9a57289304c42f4eb4010aad9f5f69c0cd99d0c439`。

## 代码门禁

- 主站及完整 solution Release 构建通过，0 错误；无数据库模型、历史 migration、Agent 或前端源码变更。
- 维护分支全量单测 1139/1141；两个失败为生产基线已有的 TeamLab 调度观察点断言：`TeamLabScheduling_MinimizesManagedRouterCrossNodeEdges`、`TeamLabScheduling_LateBindsThreePlusOneCapacityAcrossTwoNodes`。不能记录为全绿；本次未修改相关代码。
- 培训生命周期与旧 Flag 定向单测 22/22 通过。
- 原 main 开发分支的 1159 项单测和 11 项 PostgreSQL / 真实 Docker 集成测试结果见 [开发交接](2026-09-16-training-instance-flag-fix.md)。

## 备份与切换结果

- 新鲜备份目录：`/opt/gzctf/backups/training-eaac7f26-pre-20260916T052014Z`。
- 数据库 dump：833,819,552 字节，SHA-256 `ba3fa734d1210138159592e37fbe56f13c8a38013901caeddca183e4bbc834db`；catalog 可读。
- 附件归档：317,790,545 字节，SHA-256 `ae95e9dda1c9dc93eeeafc81e48d84341fc50e6aa35a45db37742500e548d53b`。
- 已保存配置备份、服务启动路径及 Nginx 配置归档；凭据仅保留在服务器受限权限备份中，不进入报告、仓库或工具输出。
- 原迁移：140 条，head `20260908111521_TeamLabDeviceObservation`。本次不执行迁移。
- 数据库 dump 已在独立、无网络的 PostgreSQL 16 容器完整恢复，包括数据、索引和约束。恢复结果：172 用户、30 课程、622 练习、217 Files、140 条迁移；迁移头一致。大量历史日志导致恢复耗时较长，期间未停止业务主站；恢复完成后临时容器和匿名卷已删除。
- 2026-09-16 **05:54:09–05:54:21 UTC（北京时间 13:54）** 停止主站、原子切换 `publish` 并启动成功，首页和配置 API 就绪。当前进程 PID 525672 的实际路径指向新 release；本机和远端 Agent 未更新或重启。
- `publish.previous` 已指向原 `agent-fabric-dd2f464da2bf-20260910`，更早 release 和全部备份保留；切换脚本具备启动失败自动回退，但本次没有触发回退。

## 发布后核验

核验时间：2026-09-16 06:00 UTC（北京时间 14:00）。

| 项目 | 结果 |
| --- | --- |
| 页面与 API | 首页、`/api/Config`、课程章节页面及管理员课程 API 均 200；匿名 `/api/exercise` 保持 401 |
| 服务 / 节点 | 主站、本机 Agent active；`.27`、`.30`、`.31` 均 Online、可调度且心跳新鲜 |
| 数据库 / 队列 | 140 条迁移、head 不变；活跃部署票据为 0 |
| 镜像 | 三题使用的模板 481、482、483 均 Ready |
| 共享附件 | 328,781 字节 PNG 经 HTTP 回读，与发布前长度和 SHA-256 一致；`files` 仍指向 `/opt/gzctf/shared/files` |
| 健康与日志 | `3001/healthz` 返回 200/Degraded，保留原有降级状态；新进程从启动至本次核验的 journal 未匹配到异常类型记录 |
| 三实例 | 正式 API 创建或复用 627、629、630，均在 `.30` Running；入口均 Ready，三条公网 TCP 连接成功 |
| 注入修复 | 629 的旧实例从无 Flag 关联修复为实例 Flag 记录 626；三容器 `GZCTF_FLAG` 均与各自数据库值匹配，只记录布尔结果，不输出值或摘要 |
| 单独停止 / 重建 | 正式 API 停止 629 后，627/630 的容器记录保持不变；重建 629 后三实例再次同时运行，Flag 记录保持有效 |

最终保留给用户继续测试的实例：

| 实验 | 题目 ID | 入口 | 容器记录 ID |
| --- | --- | --- | --- |
| ez_rce_1 | 627 | `203.195.157.191:30001` | `01a0a8c8-46b9-733d-ad69-06b96b1dff0a` |
| ez_rce_2 | 629 | `203.195.157.191:30002` | `01a0a8ca-fdf8-7db1-8211-9e43f4b0a921` |
| ez_rce_3 | 630 | `203.195.157.191:30000` | `01a0a8bd-5a69-76c2-a60f-5702039b14a1` |

切换时已运行的 630（05:42:56 UTC 启动）被复用，没有被新建 627/629 驱逐。629 停止后重建再次取得空闲端口 30002 属于正常端口回收；同时存活的三个实例没有端口冲突。

没有访问题目的漏洞接口、读取题目 Flag 文件、解题或提交 Flag。也没有执行全量集成、前端重建、SSO 联调、VM、AWDP 或 TeamLab 实机回归；本次变更不涉及这些模块。维护基线既有两项 TeamLab 单测失败及健康 Degraded 仍需独立处理。

## 交接与回退

代码已推送到 `origin/codex/training-instance-flag-release`；原 main 开发修复分支保留。未合并 main，两个 worktree 均保留用于审查。发布证据 JSON 和执行记录保存于上述服务器备份目录，不包含明文 Flag 或登录凭据。

如需回退，停止 `gzctf`，将 `/opt/gzctf/publish` 原子切回 `/opt/gzctf/releases/agent-fabric-dd2f464da2bf-20260910`，再启动主站并检查服务、配置 API 和实例。由于没有数据库迁移，应用回退不需要降级数据库；切回旧版本会重新出现旧课程额度行为，回退期间应暂停新建课程实例。未经独立授权不恢复数据库覆盖本次发布后的业务数据。
