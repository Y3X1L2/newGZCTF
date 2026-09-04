# 10.24 TeamLab 历史测试配置清理与 Phase 09 发布

更新时间：2026-09-04

## 任务目标

- 在已授权范围内清理 10.24 生产数据库中 10 条可废弃的旧 TeamLab 测试配置。
- 使用新鲜备份与隔离副本重新验证当前 `main` migration bundle。
- 仅在所有验证通过后，按维护窗口手册发布当前 `main` 的 Phase 09 TeamLab 版本。

明确不做：

- 不修改、删除或重建用户、战队、比赛、培训、练习、理论、AWDP、附件、镜像、节点、队列或任何非 TeamLab 数据。
- 不补写 `20260604165857_AddTheoryExamEntities` 或 `20260604193010_SyncTheoryExam`。
- 不修改 203 公网网关，不触碰 9091 或 18080。
- 不手工修改生产 `__EFMigrationsHistory`，不执行 EF `Down`，不对生产执行 `pg_restore`。

## 基线

- 起始提交：`d90e2d1b65cca693d500a9ee4fb21f9bed6026aa`
- 当前任务分支：`codex/teamlab-legacy-cleanup-release`
- worktree：`D:\Work\newGZCTF-teamlab-cleanup-release`
- 生产主站：`10.24.0.27`
- 发布前运行基线：`stable-20260831` / `81a6e02b7dbe3d1f12094b606e5b3a93fd86de0c`
- 当前生产 release：`teamlab-phase09-d90e2d1b-20260903T1228Z` / `d90e2d1b65cca693d500a9ee4fb21f9bed6026aa`
- 恢复 migration 已在 `main`：`20260814075023_AddAssetAndChallengeOwnership`、`20260815012026_AddExerciseCreatorTracking`。

## 授权边界与止损条件

- 已授权：经只读核对确认的 6 条含 `EnvironmentJson` 和 4 条 `RoutingEnabled=true` 的旧 TeamLab 测试配置。
- 清理优先走 TeamLab 生命周期/API；无精确能力时，只在完整事务中按核对后的精确 ID 更新或删除。
- 任一对象存在 active/queued/cleanup-pending runtime、队列票据、非测试引用或范围无法证明时立即停止，不扩大清理。
- 清理前先建立并校验新的生产备份；清理后用该备份在隔离 PostgreSQL 16 容器复验 bundle。

## 当前状态

| 状态 | 事实 |
| --- | --- |
| `VERIFIED` | 迁移恢复分支已正常合入并推送 `main`。 |
| `VERIFIED` | 8 个资产承载的 10 个授权 legacy 配置值已完成只读关联核对；候选 runtime 均为 `Destroyed`，无活动队列、访问授权、远程会话或未完成操作。 |
| `VERIFIED` | 已建立并校验清理前与清理后两份完整生产备份；备份文件不在 Git。 |
| `VERIFIED` | 精确事务已将 6 个 `EnvironmentJson` 清为空对象、4 个 `RoutingEnabled` 设为 false；未删除 TeamLab 行或其他业务数据。 |
| `VERIFIED` | 清理后新鲜备份在无网络、无端口 PostgreSQL 16 容器中成功恢复并执行发布包对应 bundle 至 `20260816192540_TeamLabCapabilityClosure`；核心表计数保持一致。 |
| `VERIFIED` | 获得新增授权后，通过管理员 TeamLab 生命周期销毁 `qqqtest1` 的 runtime 65/66；两条 runtime 都为 `Destroyed`，原 pending create ticket 为 `Cancelled`，对应 destroy ticket 为 `Succeeded`。 |
| `VERIFIED` | `qqqtest1` 不存在非终态 runtime；生产库无 pending TeamLab queue ticket，所有现存 TeamLab runtime 均为 `Destroyed`。 |
| `VERIFIED` | 已建立 runtime 收敛后的新鲜备份，并从其 dump 恢复隔离 PostgreSQL 16 副本；发布包自身的 glibc `efbundle` 在该副本完整前向执行 10 条迁移至 `20260816192540_TeamLabCapabilityClosure`。 |
| `VERIFIED` | 生产 bundle 已前向执行同一 10 条迁移，随后使用独立 release 目录原子切换到 `d90e2d1b`。主站、Agent、PostgreSQL、Redis、公开首页、health、OpenAPI、API docs、附件下载、节点 inventory 和队列均已复核。 |
| `VERIFIED` | 新 release manifest 的 994 个文件摘要与磁盘一致；`/usr/local/bin/gzctf-agent` 摘要与 manifest 中 Agent 条目一致；`publish/files` 仍解析到 `/opt/gzctf/shared/files`。 |
| `NOT_RUN` | 发布后通过已认证浏览器会话验证本地登录/Portal SSO、实际 Docker 创建-入口-销毁，以及新 TeamLab 场景或 runtime 的真实链路。发布窗口后原管理员浏览器会话已不可用，未创建 token、未尝试绕过认证，也未直接写数据库。 |
| `OPERATOR_ONLY` | 使用授权管理员会话完成上述登录、Docker 和新 TeamLab 实例验收；双 Worker 故障接管、长期流量、复杂服务注入、规模并发、Windows VM 和 AWDP 高风险流程继续按各自手册现场验收。 |

## 清理对象与结果

| 拓扑 ID | 资产 ID | 清理值 | 发布/runtime/队列结论 |
| --- | --- | --- | --- |
| 33 | 103 | `RoutingEnabled=true -> false` | 2 个 release、2 个 runtime 均 `Destroyed`、无活动票据 |
| 39 | 181 | `RoutingEnabled=true -> false` | 1 个 release、1 个 runtime `Destroyed`、无活动票据 |
| 34 | 298, 299, 302, 304 | `EnvironmentJson -> {}` | 1 个 release、1 个 runtime `Destroyed`、无活动票据 |
| 34 | 305, 306 | `EnvironmentJson -> {}`，`RoutingEnabled=true -> false` | 同上 |

- 三个拓扑仍被历史 Penetration 比赛绑定引用；绑定、比赛、release、runtime、队列和其他业务行均未删除或修改。
- 事务前断言：候选资产总数必须为 8、三个候选拓扑没有非 `Destroyed` runtime、没有活动 TeamLab queue ticket。
- 事务后结果：`updated_environment_json=6`、`updated_routing_enabled=4`、`remaining_legacy_config_assets=0`。

## 备份与副本验证

- 清理前备份：`/opt/gzctf/backups/teamlab-legacy-cleanup-20260903T080006Z`；数据库 dump SHA-256 `ab38761b650fd82f3d45a1adfac16af246261902c4594a45536a8971b2eb45cb`。
- 清理后、发布前回滚点：`/opt/gzctf/backups/teamlab-release-pre-migration-20260903T080343Z`；数据库 dump SHA-256 `b07217281a081ed8842699db3e85bd2ea0f0298a71fcf542430d3ea5a22c64d2`。
- 两份备份均包含 custom PostgreSQL dump、schema/history、shared files、当前 release 和 systemd 摘要；文件大小非零并已写入 `SHA256SUMS`。
- 清理后备份副本 migration history 从 124 条前向至 134 条，末端为 `20260816192540_TeamLabCapabilityClosure`；新 execution plan、connector、device package、link policy 表存在，旧 bootstrap/artifact 表不存在。
- 清理后生产与升级副本的核心计数一致：用户 172、比赛 22、比赛题目 110、练习题 436、课程 29、理论试卷 4、AWDP 服务 10、附件文件 217。

## runtime 收敛、发布与验收

- 已授权销毁的 `qqqtest1` runtime public ID：`01a0237e-89f9-7940-b7c7-7493a0c46f5a`、`01a0237f-0011-7398-910a-0c32d38ef249`。销毁没有删除 topology、release、比赛绑定或其他业务记录。
- runtime 收敛后的发布前备份：`/opt/gzctf/backups/teamlab-runtime-converged-pre-migration-20260903T122601Z`；数据库 dump SHA-256 为 `2996a1a69127c2723df0fc6639d31bba7709ffc64dcc6ff6520d0729a14b8ed0`。其 dump、当前 release、shared files、migration history 和 systemd 摘要均非空；`pg_restore -l` 可读 2,004 个归档条目。
- 新发布包：`teamlab-phase09-d90e2d1b-20260903T1228Z`；archive SHA-256 为 `5f88617220c1e3a75944a9a8d9603ffc0e836b5d504b2011e2d89bd1dfdf06a7`，manifest Git SHA 为 `d90e2d1b65cca693d500a9ee4fb21f9bed6026aa`，共 994 个 manifest 文件。
- 隔离副本从 124 条 migration 前向至 134 条，末端为 `20260816192540_TeamLabCapabilityClosure`。旧 `TeamLabBootstrapExecutions`、`TeamLabReleaseAssetArtifacts` 不存在；`TeamLabExecutionPlanSnapshots`、`TeamLabConnectors`、`TeamLabDevicePackages`、`TeamLabLinkPolicies` 存在。当前源码无 pending model changes。
- 生产 migration 与隔离副本一致：124 -> 134，末端为 `20260816192540_TeamLabCapabilityClosure`。未执行 EF `Down`、手改 `__EFMigrationsHistory` 或生产 `pg_restore`。
- 发布后：主站与 Agent 为 `active`；PostgreSQL 接受连接、Redis 返回 `PONG`；`/`、`/health`、`/openapi/open-v1.json`、`/api-docs/` 返回 200；公开 OpenAPI 中可见 56 条 TeamLab 路径。一个既有附件下载返回 200、15,872 bytes。
- 三个节点均为 online/schedulable；两个远端 Worker 的 TeamLab tunnel 为 Healthy。队列没有 active ticket，也没有 pending TeamLab ticket；保留了一条与本任务无关的历史 Docker failure，未作处理。
- 发布后当前计数为用户 172、比赛 22、比赛题目 110、课程 29、理论试卷 4、AWDP 服务 10、附件 217，均与本次新鲜备份一致。`ExerciseChallenges` 当前为 590，而新鲜备份副本为 446；152 条 ID 大于 446 的新增记录均为公共练习题。此次实际执行的 10 条 TeamLab migration 不对 `ExerciseChallenges` 做插入或数据回填，因此不得将该业务增量归因于 migration，也未对其做任何处置。

## 回滚点

- 当前应用级回退点：`/opt/gzctf/releases/stable-20260831/publish`，已由原子切换保留。数据库迁移已应用，不能只在未知兼容性下回退二进制。
- 数据级最终回滚点：`/opt/gzctf/backups/teamlab-runtime-converged-pre-migration-20260903T122601Z` 的 PostgreSQL 与 shared files 备份。仅在停止写入、评估应用不兼容且得到恢复决策后使用；不执行 EF `Down`、不手改 migration history。

## 下一步

由授权操作人员使用管理员会话完成本地登录/Portal SSO、可清理 Docker 实例创建-入口-销毁，以及新 TeamLab 场景或 runtime 的最小真实验收；结果写入本交接或独立验收记录。不要把本次已发布状态误写为已完成全部 TeamLab 现场能力签收。
