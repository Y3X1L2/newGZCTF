# 数据库存储与可维护性审查

## 任务目标

梳理当前存储形式、增长来源、结构耦合和复用边界，提供按收益与风险排序的改进路线及可重复的容量核验入口。验收以当前源码证据和本地验证为准；不把表数或历史备份大小当作现网容量结论。

## 基线

- 任务分支：`codex/database-maintainability-review`
- Worktree：`D:/Work/newGZCTF-database-review`
- 起始 `origin/main`：`4bef3770`（TeamLab stable v2 合并）
- 环境：本地源码和隔离测试；本任务没有部署指令。

## 执行范围

1. 核对 EF 模型、迁移、模块映射、对象存储和 Redis 职责。
2. 追踪高增长数据的写入、聚合、清理与查询契约；核对现行文档偏差。
3. 形成分阶段改进路线，补充只读容量诊断入口；根据证据决定是否需要运行代码修改。
4. 执行最小充分验证、完善交接，提交并推送任务分支。

## 当前状态

`complete`：源码审查、SQL 缺陷修复、只读诊断脚本和 PostgreSQL 定向验证已完成。证据状态为 `VERIFIED`（本地）；全量单测保留两项既有失败，生产验证为 `NOT_RUN`。详细结论见[存储与演进审查](../../commercialization/database-storage-and-evolution-review.md)。

## 技术结论

- PostgreSQL 单库的模块化单体继续保留；附件对象、镜像和节点文件应与数据库容量分开统计。基线模型快照映射 152 张逻辑表，不能据此认定现网数据库过大。
- `HasOpenRuntimeWindowAsync` 使用 C# 原始字符串但仍保留 `\"`，发送到 PostgreSQL 的 SQL 带反斜杠。修复前用真实 PostgreSQL 16 复现 `42601: syntax error at or near "\"`，堆栈落在该方法。
- 本次只修复上述 SQL 引号，不改变聚合证明、运行状态集合或清理时间条件；不新增迁移、不修改历史 migration。
- 每小时单批清理的吞吐、空聚合窗口重复扫描、Blob GC、迁移来源和模块写入边界分别作为后续任务登记，不以全面合表或拆库掩盖。

## 改动范围

- `PostgresPartitionManager.cs`：修复运行窗口检查 SQL。
- `DatabaseGovernanceMigrationTests.cs`：在既有前向迁移/聚合回归中补充 flow 分区清理。覆盖没有聚合证明不删、Running runtime 不删、Destroyed runtime 删除、删除审计行数及重复执行。
- `scripts/database/sql/storage-health-report.sql` 与 `DatabaseStorageReportTests.cs`：只读 catalog 诊断；覆盖空库、跨 schema 同名表、分区体积汇总不重复、业务数据保留和输出不含正文。项目文件将实际 SQL 复制为测试资源，避免测试另一份脚本。
- 数据库演进报告、索引/生命周期基线、治理运行手册、文档导航和 current-state 同步。TeamLab flow 默认原始保留天数从文档旧值 7 纠正为当前源码的 30，运行配置未修改。
- API / EF 模型 / 前端契约：无变更。

## 验证证据

| 验证项 | 命令或步骤 | 结果 |
| --- | --- | --- |
| 最终 Release 构建 | `dotnet build src/GZCTF.slnx -c Release --no-restore` | 通过，0 error；4 个现存 warning（依赖告警和 TeamLab nullable） |
| 原缺陷复现 | 扩展治理测试、使用修复前源码编译并运行 | PostgreSQL `42601`，真实执行到运行窗口检查 |
| 最终全量单测 | `dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-build --no-restore` | 1,121 通过、2 失败，共 1,123；基线与最终结果一致 |
| PostgreSQL 定向集成 | `DatabaseGovernanceMigrationTests`、`DatabaseStorageReportTests`、`RedisGovernanceMigrationTests` | 4/4 通过，PostgreSQL 16 Testcontainers，25 秒 |
| 文档与通用门禁 | `git diff --check`、新增/修改文档相对链接检查 | 通过 |
| 生产/测试服务器容量与恢复 | 当前体积、增长率、慢查询、WAL、备份副本/PITR | NOT_RUN；本次只运行本地合成数据验证 |
| 前端 / 全量集成 | 前端完整门禁、整个集成项目 | NOT_RUN；无前端变更，集成按本次数据库影响范围选择 |

两条全量单测失败均来自 `RuntimeControlPlaneTests`：

- `TeamLabScheduling_MinimizesManagedRouterCrossNodeEdges`
- `TeamLabScheduling_LateBindsThreePlusOneCapacityAcrossTwoNodes`

它们在修改生产源码前已经失败，最终复跑同样失败；本任务没有修改调度逻辑。不能将分支整体标为全量门禁通过。

本地日志和 TRX 位于仓库外 `D:/Work/database-review-*.log`、`D:/Work/database-review-test-results/`。初始 Docker 未就绪，随后遇到本机失效 socket；只对确认失效的 socket 作同目录带 `database-review-stale` 后缀的备份移动，未重置 Docker 数据。用户手动启动 Docker Desktop 后确认 Engine 29.5.2，真实 PostgreSQL 回归得以执行。基础设施未就绪的早期失败不当作 SQL 复现证据。

## 提交与部署

- 最终提交：本交接所在提交，包含修复、诊断、测试和报告；可用 `git log -1 --format=%H -- docs/development/handoffs/2026-09-15-database-maintainability-review.md` 精确定位。
- 推送分支：`codex/database-maintainability-review`。
- 合并 `main`：否；保留分支及 worktree 等待审查。
- 部署：未部署，未修改生产或测试服务器数据库；没有发布物、生产备份或上线冒烟结果。
- 回退：此次无迁移。将来发布仍须按正式维护流程备份并保留旧 release；本地测试不能替代上线验收。

## 风险与后续

1. 下一位接手者先 fetch 核对任务分支和最新 main；生产 SQL 修复尚未发布，必须继续把线上治理失败视为未验收。
2. 在受控只读连接采集容量，至少跨多个治理周期，确认热表、索引/TOAST、实际 retention 覆盖、过期到达速率与清理吞吐。
3. 按报告阶段 B 拆分空窗口扫描与有界多批清理任务，补迟到写入、锁等待、取消和恢复回归；Blob GC 需要独立文件引用/补偿闭环。
4. 迁移来源缺口、指标并发和两个既有调度测试失败保留；本次不宣称其根因已修复。
5. 复用工作先收拢表 owner、application command/query、模块配置和不可变内容版本，继续保持动态 Flag、实例状态与各业务结果隔离。
