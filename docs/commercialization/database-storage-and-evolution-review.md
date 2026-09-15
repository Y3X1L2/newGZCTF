# 数据库存储、增长与演进审查

审查日期：2026-09-15；源码基线：`4bef3770`（从最新 `origin/main` 创建任务 worktree）。

## 1. 判断

当前适合继续使用 PostgreSQL 单库的模块化单体。已有附件对象存储、镜像存储、统一运行队列、时序分区和聚合机制，基础方向合理。现在需要优先治理的是清理可靠性、增长速度、模块写入边界和迁移可信度。

不能仅凭表多认定数据库太大：本次没有连接生产或测试服务器采集当前容量、慢查询和负载。历史备份文件大小是某时点的逻辑备份大小，不能推算现网表、索引、TOAST、WAL 或磁盘占用，也不能把 Registry/VM 磁盘占用归因于 PostgreSQL。

“越做越难改”的担心有源码依据：部分模块仍直接共享上下文，映射分散在中心上下文和模块配置中，历史迁移又存在来源缺口。这些问题应逐项收敛；现在全面合表、拆数据库或改成微服务，会先增加迁移和一致性成本。

## 2. 当前存储形式

| 存储 | 保存的内容 | 当前入口 | 治理重点 |
| --- | --- | --- | --- |
| PostgreSQL | 用户/权限、赛事、课程、题目、答卷/提交、资产元数据、节点、队列、运行状态、审计和观测数据 | `src/GZCTF/Models/AppDbContext.cs`、`src/GZCTF/Extensions/Startup/DatabaseExtension.cs` | 事务、索引、迁移、聚合、保留与恢复 |
| PostgreSQL JSONB / 文本 | TeamLab 草稿配置、不可变 release、执行计划、部分操作结果；另有序列化列表和加密 payload | `Modules/TeamLab/Infrastructure/Persistence/`、`AppDbContext.cs` 中 converter | 区分结构化查询字段与版本快照，约束大小/版本；不把所有领域信息塞进 JSON |
| Redis | 缓存、协调/租约、流量及节点高频数据缓冲 | `Modules/Runtime/Infrastructure/RedisNodeLiveStateStore.cs`、`NodeMetricPersistenceWorker.cs` | 保持可重放、确认与幂等，业务事实最终进入 PostgreSQL |
| disk / S3 / MinIO | 附件等二进制对象；数据库 `Files` 保存 hash、文件名、大小等元数据，存储子目录由 hash 计算 | `Storage/StorageProviderFactory.cs`、`Storage/LocalBlobStorage.cs`、`Storage/S3BlobStorage.cs`、`Repositories/BlobRepository.cs` | 引用保护、共享文件目录、失败补偿和孤儿对象回收 |
| Registry / 镜像主存储 / WorkerNode | Docker 层、VM 模板、实例磁盘、抓包与远程操作审计文件等执行面文件 | `Storage/ImageStorage.cs`、TeamLab capture/remote-session 路径、Agent | 按各自资源生命周期统计；不随数据库表清理直接删文件 |

上表源文件省略的共同前缀为 `src/GZCTF/`。本地/S3 是代码支持的 provider，不代表本次确认了现网配置。`LocalFile.Location` 标记为 `[NotMapped]`，不是 `Files` 的数据库列；它由 hash 前四位组成两级子目录，实际根目录由存储配置决定。

数据库主要使用关系表、外键和索引；并非整个产品存成一个 JSON 大对象。`Files` 按内容 SHA-256 查重，题目克隆的附件元数据可以引用同一对象，所以业务快照数量和二进制份数不能画等号。TeamLab release/执行计划保留独立快照是恢复和确定性清理的需要，不能为节省几行元数据改成读取随时会变的草稿。

## 3. 结构规模与耦合证据

以下是源码静态统计，不是运行数据库 catalog 统计：

| 项目 | 基线结果 | 统计口径与含义 |
| --- | ---: | --- |
| 快照逻辑表 | 152 | `Migrations/AppDbContextModelSnapshot.cs` 中不同 `ToTable("...")` 名称；包含连接表/Identity，不含物理子分区、迁移历史及旧库遗留表 |
| TeamLab 前缀表 | 43 | 上述 152 张的子集；拓扑、发布、执行、访问、观测承担不同事实，不据此前缀认定模块所有权 |
| Training / Theory / Awdp 前缀表 | 17 / 7 / 8 | 同一静态口径；其他 77 张包含身份、CTF、练习、内容、运行和审计等 |
| 中心上下文 | 1,346 行，143 个显式 DbSet | 不含 inherited Identity DbSet；长度是维护信号，不是性能指标 |
| 模块 EF 配置 | 88 处 `IEntityTypeConfiguration<` | 已存在可继续使用的模块映射模式，不必再创建一套持久化框架 |
| Controller 中上下文引用 | 18 个文件 | 统计 `Controllers/**/*.cs` 中 `AppDbContext`；有引用不自动等于违规，需审查实际用法 |
| JSONB 属性映射 | 23 处 | 快照中 `HasColumnType("jsonb")`；不包含存为 text/varchar 的序列化结构 |
| 迁移元数据 | 138 个不同迁移 ID | 基线源码中带 `[Migration]` 的 ID，head `20260908111521_TeamLabDeviceObservation`；不是 9 月 8 日生产历史的 134 条 |

可复查命令（PowerShell，在仓库根目录）：

```powershell
(Get-Content src/GZCTF/Models/AppDbContext.cs).Count
(rg 'public DbSet<' src/GZCTF/Models/AppDbContext.cs | Measure-Object).Count
(rg -l AppDbContext src/GZCTF/Controllers -g '*.cs' | Measure-Object).Count
rg '\.ToTable\(' src/GZCTF/Migrations/AppDbContextModelSnapshot.cs
rg '\[Migration\(' src/GZCTF/Migrations -g '*.cs'
```

需要收敛的具体位置：

- `AppDbContext.OnModelCreating` 先应用模块配置，后面仍直接配置课程、题目和部分 TeamLab 运行实体。同一模型变更容易需要在多处查找。应按 owner 逐批搬到既有模块配置类，每批验证最终模型/DDL 不变。
- `TrainingCourseAdminController` 直接读取课程、镜像、文件，组织事务和写入。以后调整资产归属或课程结构，容易连带修改 Controller。应逐用例移入 Training application service，通过 Content query contract 做引用查询。
- `BlobRepository.HasReferencesAsync` 集中枚举附件、课程资源、视频、封面、writeup、头像等引用。新业务增加引用后若忘记登记，会让删除判断失去完整性。短期扩展现有引用契约和并发回归；不要立即用一张无外键的通用 `(type,id)` 表替换现有引用。
- `DatabaseExtension` 显式忽略 `PendingModelChangesWarning`。不能把“应用能启动”当作模型和快照完全一致；也不能直接取消忽略造成部署启动失败。先在独立门禁检测并解释差异，再决定何时收紧运行配置。

## 4. 数据增长：已有能力与缺口

### 4.1 当前默认值

以 `Infrastructure/Persistence/Governance/DataRetentionOptions.cs`、`DataRetentionPolicyCatalog.cs`、`DataRetentionExecutor.cs` 为准；配置覆盖值需要在目标环境核验。

| 数据集 | 默认保留 | 实现与限制 |
| --- | --- | --- |
| Logs | 原始 30 天，小时聚合 180 天 | 月分区，完整过期分区才可删除；实际原始窗口可能超过 30 天 |
| TeamLabTrafficFlows | 原始 30 天，五分钟聚合 180 天 | 日分区；必须有聚合证明并通过运行窗口检查；旧审计文档的 7 天已过期 |
| TeamLab observations / paths / capture | 7 / 30 / 7 天 | observations/paths 分批清理；capture 先标记 CleanupPending，由节点协调文件删除，不能以标记成功声称磁盘已回收 |
| 队列终态 / API operation | 180 / 90 天 | 有终态/引用条件，不能对活动任务统一按时间删除 |
| TeamLab event / operational event / 节点分钟指标 | 180 天 | TeamLab event 还限制 runtime 状态与旧 generation；保留天数不是“到期必删”的承诺 |
| 治理运行记录 | 365 天 | 终态分批清理，也需要观测其自身增长 |
| 参赛、提交、成绩、课程进度、理论答卷、AWDP 比赛事实 | 业务 owner 管理 | 不纳入默认时间清理；历史归档必须保持查分/追溯所需关联 |

### 4.2 已定位问题与处理顺序

| 优先级 | 证据 | 影响 | 建议 / 本次范围 |
| --- | --- | --- | --- |
| P0 | `PostgresPartitionManager.HasOpenRuntimeWindowAsync` 的 C# 原始 SQL 字符串含多余反斜杠 | PostgreSQL `42601`；异常由 executor 向外传播，本周期后续终态和指标清理不能执行 | 本次修复引号并扩展真实 PostgreSQL 回归，保持原聚合证明和运行状态条件 |
| P1 | 多数 cleaner 每次一个 `LIMIT DeleteBatchSize`，executor 每周期调用一次；默认 1,000 行 / 60 分钟 | 单表理论上限约 24,000 行/天，未算故障、锁和耗时；高写入表会越清越积压 | 先采样过期到达速率，再设计每周期时间预算、多批 drain、取消和锁等待门禁；只调大单批会延长锁持有 |
| P1 | `AggregateFlowsAsync` 从聚合表 `Max(BucketStart)` 推进；没有聚合行时回退 30 天，每五分钟运行一次 | 完全无 flow/aggregate 的条件下，单周期约 8,640 个空窗口仍会查询并写治理记录，空库也可能产生额外工作 | 在隔离库复现后加入持久化扫描水位或可靠跳过空范围；须保留迟到写入与分区删除前重聚合，不以 `Max` 直接跳过真实空洞 |
| P1 | `BlobRepository` 先提交元数据删除，再删对象；失败会留下无元数据对象，状态文档仍记录自动 GC 缺口 | 文件存储持续增长，单独清数据库无法回收 | 引入可恢复删除工作记录、grace period、引用复查、重试与审计；按现有 hash 锁协调并发上传/绑定 |
| P1 | 原生产记录两个 Theory migration 来源缺失；源码另有 `20260802023000_RemoveDestroyedTeamLabUdpMappings.cs` 无迁移元数据 | 空库与旧库的 schema 可能不同，“迁移命令成功”不等于可复用安装一致 | 沿用迁移交接，核对备份副本 catalog、空库、当前模型三方差异；保留历史 ID，禁止猜测同名迁移或删除历史 |
| P2 | `NodeMetricPersistenceWorker` 同时更新分钟样本和 WorkerNode 当前投影；曾有并发异常记录 | 节点高频写入与其他状态更新需验证并发语义 | 定向验证事务冲突、stream 确认顺序和幂等累加，再决定原子 upsert / 有界重试；本次未证明或修复其根因 |

这是按影响排序的后续队列，不代表所有缺口都在本次完成。尤其不能将治理 SQL 修复等同于现网清理恢复：部署后还需核对多个周期、积压趋势和真实磁盘变化。

### 4.3 如何判断实际是否太大

新增只读脚本：[`storage-health-report.sql`](../../scripts/database/sql/storage-health-report.sql)。使用 PostgreSQL 16+ 的 catalog/statistics，按逻辑父表汇总物理分区，单独列出分区和最大 30 个索引。只读事务、15 秒 statement timeout、1 秒 lock timeout；不扫描业务正文、不执行 `EXPLAIN ANALYZE`、不采集 SQL 文本或索引表达式。

在已经配置好 libpq service / `.pgpass` 的受控终端执行，输出放仓库外（目录预先创建）：

```powershell
psql -X -q -A -t -v ON_ERROR_STOP=1 -d 'service=gzctf-readonly' `
  -f scripts/database/sql/storage-health-report.sql `
  -o D:/Work/db-evidence/storage-health.json
```

`gzctf-readonly` 是需由运维配置的 service 名称，不是项目自带账号。只读权限不足或超时应保留失败，不提升生产权限自动重试。脚本没有实施任何删除或配置修改。

使用限制：

1. `estimated_live_rows/dead_rows` 是统计估计，dead tuple 数量不是膨胀字节数；不要据此直接 `VACUUM FULL`。
2. 顶层分区父表本身几乎没有数据。脚本汇总叶表的 table/index/TOAST，避免父表看起来为零；详情中的分区不能再加到 logical totals，否则重复计数。没有叶分区的空父表不进入 logical totals。
3. `table_bytes_including_toast` 包含 TOAST 及其内部开销；`database_bytes` 还包含未列出的系统对象，**不包含**实例 WAL 目录、其他数据库、附件和 Registry。
4. `idx_scan=0` 不能作为删索引依据：可能统计刚重置、读流量在副本、业务低频，或索引用于唯一约束。跨快照比较要确认 `stats_reset` 未变化。
5. 连续 7–14 天同一时点采样；另由现有监控记录磁盘空闲、WAL/复制槽保留、备份恢复耗时、治理失败/耗时、API p95/p99 和队列延迟。不要把 catalog 一次采样当成完整性能诊断。

决策时把三个问题分开：业务数据净增长、到期数据是否清得动、查询是否满足并发目标。先处理增长最快的前几张表和实际慢查询。用现有 `scripts/database/capture-query-plans.ps1` / `assert-query-plans.ps1` 在专用基准库比较索引与分区裁剪；取得证据后再加索引或改分页。

## 5. 复用性应该如何建立

### 5.1 复用内容和能力，隔离业务结果

现有 `ImageTemplate` 全局资产、课程 binding、`ExerciseManagementService` 来源导入、附件 hash 和统一 `DeploymentQueueTicket` 可以继续复用。新增业务通过稳定的 command/query 接入，不直接共享另一模块的 DbSet。

例如同一道题用于竞赛、课程和公共练习：

```text
内容资产 / 不可变版本（后续逐步补齐的统一概念）
  ├─ 比赛题目快照 → 比赛参与 / 提交 / 得分
  ├─ 课程拥有的题目快照 → 课程报名 / 学习进度 / 提交
  └─ 公共练习定义 → 用户练习实例 / 提交
       共用附件对象、镜像目录、运行 command/query 与统一队列
```

每个绑定记录来源 ID、版本/摘要、导入时间和拥有者；各业务保存发布时必要快照，源题更新不自动改变正在进行的比赛或历史答卷。动态 Flag、实例状态、权限和成绩继续隔离。版本化内容是演进建议，不表示目前已实现完整统一资产版本库。

### 5.2 先建立逻辑 owner，再考虑物理 schema

每张新表要有一张短数据契约，至少回答：

| 项目 | 必填内容 |
| --- | --- |
| Owner 与稳定 ID | 哪个模块唯一负责写入、ID 是否允许外部引用 |
| 查询与事务 | command/query 入口、实际过滤/排序、预计峰值行数/写入频率、并发策略 |
| 引用和删除 | 外键、跨模块引用检查、终态条件、归档/保留、文件生命周期 |
| 结构与安全 | 必要唯一约束、敏感字段、JSON schema/version/大小限制 |
| 演进与验收 | expand/backfill/contract 步骤、失败续跑、恢复路径、测试负责人 |

本阶段保留单一 `AppDbContext` 和有序迁移链。按模块抽取 `IEntityTypeConfiguration`、application service 和公开契约，先做到一张表有一个写入 owner。可在未来用 `identity/content/ctf/training/runtime/teamlab/audit` 等 schema 改善可读性与权限；移动 schema 不会减少数据量，且会影响大量裸 SQL、分区和发布脚本，应在稳定边界后单独迁移。

### 5.3 跨产品复用

如果“复用”还包括安装到另一客户或开发新产品，应优先交付版本化导入/导出格式、Open API、内容 manifest、镜像摘要、依赖校验与幂等导入。业务行的内部自增 ID 不应直接当跨环境资产 ID。导出不夹带用户、成绩、凭据、Flag 或运行状态。

新安装验证空库完整迁移；升级验证真实备份副本前向迁移。不要复制生产数据库当种子，也不要先把整个 EF 实体层发布成公共库：共享实体会让消费者依赖内部列和导航关系。只有稳定且无 EF 依赖的 contract/value object 才适合独立复用。

## 6. 安全演进路线与验收

| 阶段 | 可独立评审的工作 | 验收标准 |
| --- | --- | --- |
| A：恢复增长治理 | 本次 SQL 修复与回归；部署另行安排；采集只读容量基线 | 未完成聚合不删、活动运行不删、允许关闭的运行可删、重复执行无误；上线后治理连续成功且积压不再增加 |
| B：提高治理吞吐 | 空窗口扫描水位、有界多批清理、失败重试和 Blob 删除工作记录，分任务提交 | 过期到达速率低于可持续清理速率；迟到写入保留；文件引用并发/失败恢复通过；记录 IO/WAL/锁等待 |
| C：降低修改半径 | 按业务用例移出 Controller 写库；模块化 EF 配置；新表契约登记 | 每批模型与 schema diff 为零或有独立前向 migration；既有 HTTP 行为、事务与权限回归通过 |
| D：可靠迁移和内容复用 | 迁移来源追溯、schema 三方对账、版本化内容导入/绑定 | 空安装与旧库升级均可重放；差异有解释；改源题不改变历史结果；恢复演练可用 |
| E：按测量扩容 | 先查询投影/索引/游标与分区，再评估读副本或独立观测存储 | 以 p95/p99、写入速率、恢复时间和硬件成本证明收益；业务事务与唯一事实源不受破坏 |

改列或拆表统一使用 expand → 有界、可重入 backfill → 核对计数/摘要/约束 → 切读写 → 后续独立 contract。不要让迁移长期锁住大表，不修改历史 migration，不把有损 `Down` 当作生产恢复办法。保留期调整、破坏性 DDL、归档删除和文件 GC 都需在备份恢复验证后按正式维护流程实施。

新分支应逐批交付。第一阶段的成功标准是“知道增长来自哪里且清理可靠”，随后是“改一个模块只影响其 owner 与公开契约”；这些比减少表的绝对数量更能控制后续成本。

## 7. 本次证据与遗留

构建、回归、提交和部署状态见[任务交接](../development/handoffs/2026-09-15-database-maintainability-review.md)。当前生产容量、PITR 恢复时间、清理吞吐和模型漂移未在本次完成现场核验；本报告中的改进路线不是已发布功能。
