# Phase 4 数据库治理迁移手册

## 适用范围

本手册用于将 Phase 3 PostgreSQL 数据库升级到 Phase 4。迁移由 `ExpandPhaseFourDatabaseGovernance`、`BackfillPhaseFourDatabaseGovernance`、`ContractPhaseFourDatabaseGovernance` 三段组成。Contract 是前向切换，不提供有损 Down；切换后的恢复方式是备份或 PITR。

## 前置条件

1. PostgreSQL 16 及以上，生产推荐 PostgreSQL 17；账号可创建 `pg_trgm`、`pgcrypto` 扩展。
2. WAL 归档和 PITR 已启用，并完成一次可恢复性验证。
3. 可用磁盘至少为 `Logs + TeamLabTrafficFlows` 当前占用的 2.2 倍，并为索引/WAL 预留额外空间。
4. 应用发布包、migration assembly 和目标 commit 一致；禁止从另一发布目录单独复制 migration DLL。
5. 维护窗口内可停止 GZCTF 写流量和所有旧版本实例。

## 迁移前检查

```sql
SELECT "GameId", "TeamId", count(*)
FROM "Participations"
GROUP BY "GameId", "TeamId"
HAVING count(*) > 1;

SELECT "Id"
FROM "ImageDistributionRecords"
WHERE NOT pg_input_is_valid(COALESCE(NULLIF("References", ''), '[]'), 'jsonb');

SELECT pg_size_pretty(pg_total_relation_size('"Logs"')) AS logs,
       pg_size_pretty(pg_total_relation_size('"TeamLabTrafficFlows"')) AS flows;
```

任何重复 Participation 或非法 JSON 必须先修正并留下审计记录。随后执行全量备份，记录恢复点、备份校验和、当前 migration ID、数据库大小和预计维护窗口。

## 执行步骤

1. 在线执行 Expand：

```powershell
dotnet ef database update 20260712080028_ExpandPhaseFourDatabaseGovernance `
  --project src/GZCTF/GZCTF.csproj --connection $env:GZCTF_DATABASE
```

2. 在线执行 Backfill。它会回填镜像引用、迁移 tag，创建历史/当前/未来分区，复制日志和流量并验证计数。

```powershell
dotnet ef database update 20260712080236_BackfillPhaseFourDatabaseGovernance `
  --project src/GZCTF/GZCTF.csproj --connection $env:GZCTF_DATABASE
```

3. 在事务外预建普通业务索引，避免把大表索引构建压入最终锁窗：

```powershell
psql $env:GZCTF_DATABASE -X -v ON_ERROR_STOP=1 `
  -f scripts/database/sql/phase4-precontract-indexes.sql
```

确认所有预建索引有效：

```sql
SELECT indexrelid::regclass, indisvalid, indisready
FROM pg_index
WHERE indexrelid::regclass::text LIKE '%Phase4%'
   OR indexrelid::regclass::text LIKE '"IX_%'
   OR indexrelid::regclass::text LIKE '"UX_%';
```

4. 进入维护模式，停止全部 GZCTF 实例和外部写入者，确认无长事务：

```sql
SELECT pid, application_name, xact_start, state, query
FROM pg_stat_activity
WHERE datname = current_database() AND xact_start IS NOT NULL
ORDER BY xact_start;
```

5. 执行 Contract。该步骤锁定关键表，复制最后增量，核对 count/checksum，原子切换分区父表，建立 FK，删除旧 JSON 列和旧表。

```powershell
dotnet ef database update 20260712080244_ContractPhaseFourDatabaseGovernance `
  --project src/GZCTF/GZCTF.csproj --connection $env:GZCTF_DATABASE
```

6. 执行迁移后检查并启动一个应用实例观察；确认健康后再恢复其余实例。

## 迁移后检查

```sql
SELECT relname, relkind
FROM pg_class
WHERE oid IN ('"Logs"'::regclass, '"TeamLabTrafficFlows"'::regclass);

SELECT parent.relname AS parent, child.relname AS child,
       pg_get_expr(child.relpartbound, child.oid) AS bounds
FROM pg_inherits
JOIN pg_class parent ON parent.oid = inhparent
JOIN pg_class child ON child.oid = inhrelid
WHERE parent.relname IN ('Logs', 'TeamLabTrafficFlows')
ORDER BY parent.relname, child.relname;

SELECT * FROM "DataGovernanceRuns" ORDER BY "StartedAt" DESC LIMIT 20;
```

必须确认父表 `relkind = 'p'`、default 分区为空、当前和未来两个分区存在、应用无 pending migration、核心业务表迁移前后 count/checksum 一致。

## 失败与恢复

- Expand/Backfill 失败：事务回滚后旧主表仍是事实来源；修复根因后重试。
- Contract 校验失败：事务回滚，旧主表名称和列保持不变；不得手工跳过校验。
- Contract 提交后出现问题：停止写入，使用迁移前备份/PITR 恢复到记录的恢复点，再回切应用版本。
- 禁止通过手工拼回 JSON 或把分区表压回单表进行“逆迁移”。
