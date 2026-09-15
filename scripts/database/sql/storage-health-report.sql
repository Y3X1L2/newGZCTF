-- PostgreSQL 16+. Run with psql -X -v ON_ERROR_STOP=1 -f this-file.sql.
-- Catalog/statistics only: no business rows, query text, connection strings or DDL.
BEGIN READ ONLY;
SET LOCAL statement_timeout = '15s';
SET LOCAL lock_timeout = '1s';
SET LOCAL search_path = pg_catalog;

WITH physical AS (
    SELECT c.oid, c.relnamespace, c.relname,
           COALESCE(pg_partition_root(c.oid)::oid, c.oid) AS root_oid,
           pg_table_size(c.oid) AS table_bytes,
           pg_indexes_size(c.oid) AS index_bytes,
           pg_total_relation_size(c.oid) AS total_bytes,
           s.n_live_tup, s.n_dead_tup, s.n_tup_ins, s.n_tup_upd, s.n_tup_del,
           s.last_autovacuum, s.last_autoanalyze
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    LEFT JOIN pg_stat_user_tables s ON s.relid = c.oid
    WHERE c.relkind IN ('r', 'm')
      AND n.nspname <> 'information_schema' AND n.nspname !~ '^pg_'
), logical AS (
    SELECT n.nspname AS schema_name, root.relname AS table_name,
           count(*) AS physical_relations,
           sum(p.table_bytes) AS table_bytes_including_toast,
           sum(p.index_bytes) AS index_bytes,
           sum(p.total_bytes) AS total_bytes,
           sum(p.n_live_tup) AS estimated_live_rows,
           sum(p.n_dead_tup) AS estimated_dead_rows,
           sum(p.n_tup_ins) AS inserts_since_stats_reset,
           sum(p.n_tup_upd) AS updates_since_stats_reset,
           sum(p.n_tup_del) AS deletes_since_stats_reset
    FROM physical p
    JOIN pg_class root ON root.oid = p.root_oid
    JOIN pg_namespace n ON n.oid = root.relnamespace
    GROUP BY n.nspname, root.relname
), partitions AS (
    SELECT pn.nspname AS schema_name, parent.relname AS parent_table,
           child.relname AS partition_name,
           pg_get_expr(child.relpartbound, child.oid) AS bounds,
           p.total_bytes, p.n_live_tup AS estimated_live_rows,
           p.n_dead_tup AS estimated_dead_rows,
           p.last_autovacuum, p.last_autoanalyze
    FROM pg_inherits i
    JOIN pg_class parent ON parent.oid = i.inhparent
    JOIN pg_namespace pn ON pn.oid = parent.relnamespace
    JOIN pg_class child ON child.oid = i.inhrelid
    LEFT JOIN physical p ON p.oid = child.oid
    WHERE parent.relkind = 'p' AND pn.nspname !~ '^pg_'
), largest_indexes AS (
    SELECT n.nspname AS schema_name, t.relname AS table_name,
           c.relname AS index_name, pg_relation_size(c.oid) AS bytes,
           s.idx_scan, i.indisunique, i.indisprimary, i.indisvalid
    FROM pg_index i
    JOIN pg_class c ON c.oid = i.indexrelid
    JOIN pg_class t ON t.oid = i.indrelid
    JOIN pg_namespace n ON n.oid = t.relnamespace
    LEFT JOIN pg_stat_user_indexes s ON s.indexrelid = c.oid
    WHERE c.relkind = 'i' AND n.nspname !~ '^pg_'
      AND n.nspname <> 'information_schema'
    ORDER BY bytes DESC, n.nspname, c.relname LIMIT 30
)
SELECT jsonb_pretty(jsonb_build_object(
    'captured_at_utc', to_char(clock_timestamp() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
    'server_version', current_setting('server_version'),
    'transaction_read_only', current_setting('transaction_read_only'),
    'database_bytes', pg_database_size(current_database()),
    'stats_reset', (SELECT stats_reset FROM pg_stat_database WHERE datname = current_database()),
    'logical_tables', COALESCE((SELECT jsonb_agg(to_jsonb(l) ORDER BY l.total_bytes DESC, l.schema_name, l.table_name)
                               FROM logical l), '[]'::jsonb),
    'partitions', COALESCE((SELECT jsonb_agg(to_jsonb(p) ORDER BY p.schema_name, p.parent_table, p.partition_name)
                           FROM partitions p), '[]'::jsonb),
    'largest_indexes', COALESCE((SELECT jsonb_agg(to_jsonb(i) ORDER BY i.bytes DESC, i.schema_name, i.index_name)
                                FROM largest_indexes i), '[]'::jsonb)
));
ROLLBACK;
