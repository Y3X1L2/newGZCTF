-- NebulaMind customer-db 业务函数
-- F2 题目：PostgreSQL SECURITY DEFINER 函数权限提升
--
-- 漏洞设计：
--   export_internal_data(target_table) 声明为 SECURITY DEFINER，
--   以函数创建者（postgres 超级用户）身份执行。
--   readonly 用户虽无权直接 SELECT internal_exports，
--   但被授予了该函数的 EXECUTE 权限（见 04-permissions.sql），
--   因此可通过调用函数读取 internal_exports 全表数据（含 F2 Flag）。
--
-- 注意：这是业务权限提升，不是数据库主机 RCE。

\set ON_ERROR_STOP on

CREATE OR REPLACE FUNCTION export_internal_data(target_table text)
RETURNS SETOF internal_exports
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
BEGIN
    -- 报表导出辅助函数：根据 target_table 返回对应导出记录。
    -- 预期仅返回调用者有权访问的业务表导出。
    -- 漏洞：函数以 postgres 身份执行，未校验调用者对目标表的访问权限；
    --       当 target_table 指向 internal 相关导出时，直接返回 internal_exports 全表，
    --       导致 readonly 用户可读取其无权访问的内部导出数据。
    IF target_table IS NULL OR target_table = '' OR target_table ILIKE '%internal%' THEN
        RETURN QUERY SELECT * FROM internal_exports;
    ELSE
        -- 非内部表：仅返回 export_name 匹配的导出（看起来是正常报表行为）
        RETURN QUERY
        SELECT * FROM internal_exports
        WHERE export_name ILIKE '%' || target_table || '%';
    END IF;
END;
$$;

-- 函数注释（便于 DBA 审计时看到设计意图）
COMMENT ON FUNCTION export_internal_data(text) IS
    '报表导出辅助函数。SECURITY DEFINER。根据 target_table 返回 internal_exports 中匹配的导出记录。仅限内部报表服务调用。';
