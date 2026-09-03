# 10.24 数据库迁移漂移恢复与验证

更新时间：2026-09-03

## 任务目标

- 恢复并验证生产数据库 `20260815012026_AddExerciseCreatorTracking` 的真实来源、DDL、数据操作和迁移链位置。
- 在隔离 PostgreSQL 副本验证从生产备份恢复、当前 `main` 模型对比、缺失迁移复现和后续 TeamLab migration 前向升级。
- 形成保留生产业务数据的前向升级与回滚方案。

明确不做：

- 不对生产 `gzctf` 数据库、`__EFMigrationsHistory` 或任何业务表执行写操作。
- 不部署、切换或修改 10.24 release、服务、节点、Registry、网关或 TeamLab runtime。
- 不删除生产或备份中的数据；旧 TeamLab 测试数据只在隔离副本中分析，任何清理由后续单独授权任务处理。
- 不将数据库 dump、业务行、连接串、密码、token、Cookie、私钥或 Flag 加入仓库或交接文件。

## 基线

- 起始分支：`main`
- 任务分支：`codex/migration-drift-reconciliation`
- worktree：`D:\Work\newGZCTF-migration-drift`
- 起始提交：`3c15e9a7f7de92c00e4ef2d2df569d3d7f5584dd`
- 稳定运行标签：`stable-20260831` / `81a6e02b7dbe3d1f12094b606e5b3a93fd86de0c`
- 恢复提交：`d9211241` (`fix(migrations): restore creator tracking history`)
- 推送分支：`origin/codex/migration-drift-reconciliation`
- 是否合并 `main`：否，本阶段只完成恢复与验证。
- 生产范围：`10.24.0.27`，只读访问；备份目录 `/opt/gzctf/backups/stable-20260831-pre/`
- 涉及模块：EF Core migrations、`AppDbContext`、Exercise、TeamLab、发布/数据库治理运行手册。

## 当前状态

| 状态 | 事实 |
| --- | --- |
| `VERIFIED` | 生产备份与生产库均有 124 条迁移历史，末端为 `20260815012026_AddExerciseCreatorTracking`。 |
| `IMPLEMENTED` | 已在本任务分支补回经历史 DLL 反编译确认的 `20260814075023_AddAssetAndChallengeOwnership` 和 `20260815012026_AddExerciseCreatorTracking`。 |
| `VERIFIED` | 当前源码迁移末端为 `20260816192540_TeamLabCapabilityClosure`。 |
| `VERIFIED` | 10.24 运行代码仍为 `stable-20260831`，不能作为当前源码或迁移基线。 |
| `VERIFIED` | 已从历史 `GZCTF.dll` 反射并反编译两条恢复 migration；二进制、反编译文本、schema-only dump 与逻辑备份只保留在本机非仓库证据目录。 |
| `VERIFIED` | 已只读导出生产迁移历史、schema-only dump、Exercise 相关表/字段/索引/约束，并与恢复副本核对。 |
| `VERIFIED` | PostgreSQL 16 隔离副本成功恢复生产备份；空候选从零完整执行 migration bundle；生产备份候选成功前向至当前 TeamLab migration 末端。 |
| `BLOCKED` | 生产历史另有源码和保留 DLL 均未恢复的 `20260604165857_AddTheoryExamEntities`、`20260604193010_SyncTheoryExam`；其旧表仍存在，但不阻断当前 bundle 在副本中的前向升级。 |
| `BLOCKED` | 当前 TeamLab 前向 migration 会删除旧表和字段；虽然被删除的两张表在备份副本中均为 0 行，仍有 6 个非空 `EnvironmentJson` 和 4 个 `RoutingEnabled=true` 资产，不能在生产直接执行。 |
| `OPERATOR_ONLY` | 任何生产迁移、release 切换、服务重启、节点/网关操作和真实 TeamLab 运行验收。 |

## 验证路径

1. 生产侧只执行文件元数据、数据库只读查询和导出；所有 dump、DLL、bundle 和反编译产物落在非仓库证据目录。
2. 使用历史 DLL 的 PE metadata 定位 migration 类型，再用 ILSpy 反编译 `Up`/`Down`，不加载或执行历史主站程序集。
3. 用带 `gzctf.reconciliation` 标签、`network=none`、无发布端口、只读 dump 挂载的 PostgreSQL 16 容器恢复备份并执行 bundle；所有副本、volume 和临时 bundle 已删除。
4. 对生产原状、前向候选和空候选分别导出 catalog，比较 columns、indexes、constraints 与 migration history。

## 缺失 Migration 真实来源与 DDL

### `20260814075023_AddAssetAndChallengeOwnership`

- 真实来源：历史 release `asset-owner-backfill-20260814-0845` 中的 `GZCTF.dll`；SHA-256 `aaa10bd962852fd8ae5e1ab84b4e7d4f4292eeccb21a482b0487df5f1c6f9594`。
- `Up`：为 `GameChallenges`、`Files`、`ExerciseChallenges` 各新增 nullable `CreatedById uuid`；为三列建立普通索引；各自建立到 `AspNetUsers(Id)` 的 `ON DELETE SET NULL` 外键。
- `Down`：按外键、索引、列顺序删除上述对象。
- 数据回填：无。migration 不包含 `UPDATE`、`INSERT`、`DELETE` 或默认创建者推断。

### `20260815012026_AddExerciseCreatorTracking`

- 真实来源：`creator-tracking-20260815-0927` 历史 release 和 `stable-20260831-pre/current-release.tar.gz` 中的 `GZCTF.dll`；历史 release SHA-256 `1e343b03c1049d66da10754c54b5494c606267def897028b2e546633f7fb9870`。
- `Up`：幂等 `ALTER TABLE "ExerciseChallenges" ADD COLUMN IF NOT EXISTS "CreatedById" uuid NULL`，幂等创建 `IX_ExerciseChallenges_CreatedById`，并在外键不存在时添加 `FK_ExerciseChallenges_AspNetUsers_CreatedById`，`ON DELETE SET NULL`。
- `Down`：删除该外键、索引和列。
- 数据回填：无。它是上一条资产所有权 migration 对 `ExerciseChallenges` 的幂等收敛，不会修改既有行。

### 仍未恢复的历史 ID

- 生产历史还包含 `20260604165857_AddTheoryExamEntities` 与 `20260604193010_SyncTheoryExam`。
- 当前源码、可达 Git 历史和服务器保留 DLL 均未找到其 migration 类型或原始 DDL；不能猜测并补写同 ID migration。
- 当前生产/副本中仍存在它们引入的旧 Theory 表：`QuestionBanks`、`Questions`、`TheoryExamConfigs`、`TheoryExamSubmissions`。当前空库迁移链不创建这些旧表，但当前 bundle 对从生产备份恢复的副本不会删除它们。

## Schema 与迁移链结论

- 生产原状有 124 条 migration；恢复分支有 132 条可识别 migration；其差异仅为上述两个旧 Theory ID 与 10 条尚未生产执行的 TeamLab migration。
- 生产 `ExerciseChallenges.CreatedById`、`GameChallenges.CreatedById`、`Files.CreatedById` 以及三条 `AspNetUsers` 外键和索引，均与恢复的 `20260814075023` DDL 一致；`20260815012026` 的幂等 DDL 也与生产结构一致。
- 当前 `AppDbContext` 和最终 model snapshot 不映射这三组 legacy creator 列；补回 migration 的目标是恢复历史 identity/可重放链，不是重新暴露已废弃的 ownership 字段给当前业务模型。
- 空候选从零成功执行 132 条 migration 至 `20260816192540_TeamLabCapabilityClosure`。
- 生产备份候选从 124 条 history 成功前向至 134 条；新增的 10 条 TeamLab migration 全部进入 history。用户、比赛、练习、课程、理论、AWDP、附件等核心表计数在升级前后保持相同。
- 当前前向 DDL 会移除 `TeamLabBootstrapExecutions`、`TeamLabReleaseAssetArtifacts` 及旧 authoring 字段，并新增 execution plans、connector、device package 与 link policy 结构。副本中前两张被删除表均为 0 行，但 `TeamLabTopologyAssets` 有 6 条非空环境配置、4 条启用路由配置，因此生产迁移不是无损操作。

## 前向升级与回滚方案

1. 先审查并合并本分支的两条经证实 migration；不得补写或伪造两个 Theory migration。
2. 在新的生产备份副本上复跑同一 bundle，并将现网 `__EFMigrationsHistory`、schema 与本次证据重新比对。
3. 另开获得明确授权的 TeamLab 数据处置任务：逐项确认 6 个环境配置与 4 个路由配置均为可删除的历史测试数据；通过受控 TeamLab 生命周期/审计流程清理，不直接删除其他业务表或通用资产。
4. 只有旧 TeamLab 配置完成批准、备份并清理，且维护窗口内重新验证备份、shared files、节点与队列状态后，才允许执行完整 bundle 的前向迁移和独立 release 原子切换。
5. 若迁移或冒烟失败，立即停止写入、切回旧 release；对任何已应用的破坏性 migration 使用维护窗口前的新鲜 PostgreSQL 完整备份恢复，禁止执行 EF `Down` 或手改 `__EFMigrationsHistory` 作为回滚。

## 不允许执行的操作

- 不在生产 `gzctf` 中手工 `INSERT`、`UPDATE`、`DELETE` 或删除 `__EFMigrationsHistory`。
- 不对生产运行 `pg_restore`、`DROP TABLE`、`DROP COLUMN`、无审计批量删除或 EF 降级 migration。
- 不因两张旧 TeamLab 表当前为空，就跳过对配置字段、运行时、镜像引用和队列事实的审批。
- 不在缺失 Theory migration DDL 未找到时编造同 ID migration。
- 不在本任务阶段部署 release、执行生产迁移、切换 TeamLab、重启服务、修改节点或网关。

## 下一步

等待代码审查后决定是否合并两条恢复 migration；生产前向升级必须先完成单独授权的 TeamLab 历史数据分类/清理与维护窗口验收。
