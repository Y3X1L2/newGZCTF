-- NebulaMind customer-db 权限分配
-- 创建业务角色并分配表/函数权限。
-- 该脚本由官方 postgres entrypoint 在 01-schema/02-seed-data/03-functions 之后执行。
--
-- 角色设计：
--   postgres  : 超级用户（镜像默认，POSTGRES_USER），拥有全部权限。
--   admin     : 管理员账号，可访问全部业务表（含 regulated_model_training_records）。
--               密码由环境变量 NM_DB_ADMIN_PASSWORD 注入（F3 链路：选手需经 Vault/CI 高权限变量获取）。
--   app_user  : 应用账号，可读写常规业务表，可访问 regulated_model_training_records。
--   readonly  : 只读账号（F1 入口），密码 readonly_password_2026。
--               可 SELECT 8 张常规业务表，但无权访问 internal_exports 与 regulated_model_training_records。
--               被错误授予 export_internal_data 函数的 EXECUTE 权限（F2 漏洞）。

\set ON_ERROR_STOP on

-- ============ 创建角色 ============
-- readonly：F1 入口凭据（凭据从对象存储或 CI 变量泄露）
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'readonly') THEN
        CREATE ROLE readonly WITH LOGIN PASSWORD 'readonly_password_2026';
    ELSE
        ALTER ROLE readonly WITH LOGIN PASSWORD 'readonly_password_2026';
    END IF;
END $$;

-- app_user：应用服务账号
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user') THEN
        CREATE ROLE app_user WITH LOGIN PASSWORD 'nm_app_user_dev_2026';
    ELSE
        ALTER ROLE app_user WITH LOGIN PASSWORD 'nm_app_user_dev_2026';
    END IF;
END $$;

-- admin：管理员账号，密码由 __NM_DB_ADMIN_PASSWORD__ 占位符注入（entrypoint.sh 替换）
DO $$
DECLARE
    admin_pw text := '__NM_DB_ADMIN_PASSWORD__';
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'admin') THEN
        EXECUTE format('CREATE ROLE admin WITH LOGIN PASSWORD %L', admin_pw);
    ELSE
        EXECUTE format('ALTER ROLE admin WITH LOGIN PASSWORD %L', admin_pw);
    END IF;
END $$;

-- ============ 收紧默认权限 ============
-- 撤销 PUBLIC 对受限表的任何潜在访问，确保只有显式授权角色可访问
REVOKE ALL ON internal_exports FROM PUBLIC;
REVOKE ALL ON regulated_model_training_records FROM PUBLIC;

-- ============ readonly 权限：8 张常规业务表 SELECT ============
GRANT CONNECT ON DATABASE nebulamind TO readonly;
GRANT USAGE ON SCHEMA public TO readonly;

GRANT SELECT ON customers, contracts, datasets, label_tasks,
                model_versions, api_keys, audit_logs, security_findings
TO readonly;

-- 显式拒绝 readonly 访问受限表（默认即无权限，此处显式声明以表明设计意图）
REVOKE ALL ON internal_exports FROM readonly;
REVOKE ALL ON regulated_model_training_records FROM readonly;

-- 撤销 PUBLIC 对 SECURITY DEFINER 函数的默认 EXECUTE 权限
-- PostgreSQL 默认将函数 EXECUTE 授予 PUBLIC，不撤销则任何角色均可调用
REVOKE EXECUTE ON FUNCTION export_internal_data(text) FROM PUBLIC;

-- F2 漏洞：错误地将 SECURITY DEFINER 函数的 EXECUTE 授予 readonly
-- readonly 虽不能直接 SELECT internal_exports，但可通过函数读取其数据
-- （仅 readonly 被错误授权，其他低权限角色无此权限）
GRANT EXECUTE ON FUNCTION export_internal_data(text) TO readonly;

-- ============ app_user 权限：常规业务表读写 + 受监管记录访问 ============
GRANT CONNECT ON DATABASE nebulamind TO app_user;
GRANT USAGE, CREATE ON SCHEMA public TO app_user;

GRANT SELECT, INSERT, UPDATE, DELETE ON customers, contracts, datasets, label_tasks,
                                          model_versions, api_keys, audit_logs,
                                          security_findings, internal_exports,
                                          regulated_model_training_records
TO app_user;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO app_user;

-- ============ admin 权限：全部业务表（含 F3 受监管记录） ============
GRANT CONNECT ON DATABASE nebulamind TO admin;
GRANT USAGE, CREATE ON SCHEMA public TO admin;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO admin;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO admin;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO admin;

-- ============ 默认权限（未来新建表） ============
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO readonly;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO admin;
