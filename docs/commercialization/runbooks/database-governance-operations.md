# 数据库治理运行手册

## 日常观测

治理 worker 使用 PostgreSQL session advisory lock 保证多实例单执行。每次聚合、分区删除和终态清理都写入 `DataGovernanceRuns`，指标统一由 `GZCTF.DatabaseGovernance` meter 暴露。

重点指标：

- `gzctf_db_governance_duration_seconds`：按 `data_set`、`operation` 观察耗时。
- `gzctf_db_governance_rows_total`：聚合或删除行数。
- `gzctf_db_governance_failures_total`：失败计数。
- `gzctf_db_partition_horizon_days`：未来分区覆盖范围。

告警条件：连续两个周期失败、未来分区少于两个、default 分区出现数据、治理租约长期不释放、单批锁等待超过 100ms、待清理数据持续增长三个周期。

## 分区检查

```sql
SELECT parent.relname AS parent, child.relname AS partition,
       pg_get_expr(child.relpartbound, child.oid) AS bounds,
       pg_size_pretty(pg_total_relation_size(child.oid)) AS size
FROM pg_inherits
JOIN pg_class parent ON parent.oid = inhparent
JOIN pg_class child ON child.oid = inhrelid
WHERE parent.relname IN ('Logs', 'TeamLabTrafficFlows')
ORDER BY parent.relname, child.relname;

SELECT (SELECT count(*) FROM "Logs_pdefault") AS default_logs,
       (SELECT count(*) FROM "TeamLabTrafficFlows_pdefault") AS default_flows;
```

default 分区非空时先停止对应写入，创建覆盖范围分区并迁移数据；不得直接删除 default 数据。

## 失败处理

1. 从 `DataGovernanceRuns` 找到最近失败的 `DataSet/Operation/ErrorCode`。
2. 检查 PostgreSQL 锁、磁盘、WAL、扩展和目标分区。
3. 修复根因后等待下一周期或在单实例维护环境触发一次 worker。
4. 确认失败窗口出现新的 `Completed` 记录后，才允许删除对应原始分区。

分区管理器只会删除名称和边界符合固定数据集定义、并且已有成功聚合证明的完整分区。Submission、Participation、课程进度、理论答题和 AWDP 事实不属于自动清理范围。

## 容量与查询退化

每次大版本和索引变更在专用库执行：

```powershell
pwsh scripts/database/capture-query-plans.ps1 `
  -ConnectionString $env:GZCTF_BENCHMARK_DATABASE `
  -OutputPath artifacts/phase-04-query-plans -Profile commercial
pwsh scripts/database/assert-query-plans.ps1 `
  -InputPath artifacts/phase-04-query-plans
```

保存 PostgreSQL 版本、CPU/内存/磁盘、配置、数据规模、p50/p95/p99、shared read/hit、WAL 和索引体积。普通 CI 只验证结构，不用共享 runner 的毫秒结果作为性能结论。

## 恢复演练

每次 retention 或 partition 策略调整前完成：备份、记录恢复点、执行迁移/治理、模拟中断、PITR 恢复、对比核心表 count/checksum。恢复演练失败时不得缩短保留期或扩大自动清理范围。

隔离环境可重复演练：

```powershell
./scripts/database/rehearse-pitr.ps1
```

脚本创建临时 PostgreSQL 16 数据卷、WAL archive 卷和 base backup 卷，将数据库迁移到当前基线前记录事实，再执行当前迁移契约，最后恢复到契约前时间点并验证 migration head、升级前标记存在且升级后标记不存在。默认结束后删除全部临时容器和 volume；失败时输出 PostgreSQL recovery 日志。

2026-07-12 本地演练结果：`passed`；恢复目标 `2026-07-12 10:28:44.597477+00`；migration head `20260712054103_CompleteTeamLabRuntimeReliability`；升级前/后标记计数 `1/0`。
