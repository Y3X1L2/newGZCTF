# 10.24 TeamLab 历史测试配置清理与 Phase 09 发布

更新时间：2026-09-03

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
- 运行基线：`stable-20260831` / `81a6e02b7dbe3d1f12094b606e5b3a93fd86de0c`
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
| `BLOCKED` | 拓扑 `qqqtest1` 仍有 runtime ID 65/66 处于 `Scheduled`，以及两张 pending TeamLab 队列票据；不在授权清理范围内，生产 migration 和 release 切换停止。 |
| `NOT_RUN` | 生产 migration、原子 release 切换、登录/附件/节点/Docker/TeamLab 冒烟均未执行。 |
| `OPERATOR_ONLY` | 维护窗口内 pending `qqqtest1` runtime 的归属确认和处置、生产 migration、release 切换、真实 Docker/TeamLab 验收与回滚决策。 |

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

## 发布包与阻塞

- 本地发布包：`teamlab-phase09-d90e2d1b-20260903T0809Z`；manifest Git SHA `d90e2d1b65cca693d500a9ee4fb21f9bed6026aa`；archive SHA-256 `0c5f717230416eabec441d2d0fdef997ebd2a5f1692332ae8ae2d2228c5af70e`。
- 发布包中的 glibc `efbundle` 不可在 Alpine 副本中运行，独立生成的 musl 验证 bundle 成功完成副本验证；这不影响 Ubuntu 生产 release 的 bundle。
- 当前生产 release 仍为 `stable-20260831`，没有上传、切换或执行生产 migration。
- `qqqtest1` 的两条 pending runtime/queue 是本任务唯一发布阻塞项。不得假定其可删除，也不得将其加入本次清理集合。

## 回滚点

- 迁移前：原子软链接切回当前稳定 release，保留本次清理后发布前备份。
- 如破坏性 migration 已应用且应用回退不兼容：停止写入，使用 `teamlab-release-pre-migration-20260903T080343Z` 的完整 PostgreSQL 与 shared files 备份恢复；不执行 EF `Down`、不手改 migration history。

## 下一步

由维护窗口负责人确认 `qqqtest1` 的两条 pending runtime/queue 的业务归属并通过平台生命周期收敛到终态；重新核对队列为空后，才可按现有备份和发布包执行生产 migration 与原子 release 切换。
