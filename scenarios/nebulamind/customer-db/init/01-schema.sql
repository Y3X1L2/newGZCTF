-- NebulaMind customer-db schema
-- 在 nebulamind 数据库的 public schema 中创建客户与标注业务表。
-- 该脚本由官方 postgres entrypoint 在首次初始化时执行（以 postgres 超级用户身份）。

\set ON_ERROR_STOP on

CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ============ customers：客户表 ============
CREATE TABLE customers (
    id            SERIAL PRIMARY KEY,
    customer_code TEXT NOT NULL UNIQUE,
    company_name  TEXT NOT NULL,
    industry      TEXT NOT NULL,
    contact_name  TEXT,
    contact_email TEXT,
    contact_phone TEXT,
    region        TEXT,
    tier          TEXT NOT NULL DEFAULT 'standard',
    status        TEXT NOT NULL DEFAULT 'active',
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ============ contracts：合同表 ============
CREATE TABLE contracts (
    id            SERIAL PRIMARY KEY,
    contract_no   TEXT NOT NULL UNIQUE,
    customer_id   INTEGER NOT NULL REFERENCES customers(id),
    contract_type TEXT NOT NULL,
    amount        NUMERIC(14,2) NOT NULL,
    currency      TEXT NOT NULL DEFAULT 'CNY',
    signed_date   DATE NOT NULL,
    start_date    DATE NOT NULL,
    end_date      DATE NOT NULL,
    status        TEXT NOT NULL DEFAULT 'active',
    sales_owner   TEXT,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ============ datasets：数据集表 ============
CREATE TABLE datasets (
    id           SERIAL PRIMARY KEY,
    dataset_code TEXT NOT NULL UNIQUE,
    name         TEXT NOT NULL,
    data_type    TEXT NOT NULL,
    row_count    BIGINT NOT NULL DEFAULT 0,
    size_mb      INTEGER NOT NULL DEFAULT 0,
    sensitivity  TEXT NOT NULL DEFAULT 'internal',
    owner_team   TEXT,
    status       TEXT NOT NULL DEFAULT 'active',
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ============ label_tasks：标注任务表 ============
CREATE TABLE label_tasks (
    id               SERIAL PRIMARY KEY,
    task_code        TEXT NOT NULL UNIQUE,
    dataset_id       INTEGER NOT NULL REFERENCES datasets(id),
    customer_id      INTEGER NOT NULL REFERENCES customers(id),
    task_name        TEXT NOT NULL,
    label_type       TEXT NOT NULL,
    assignee         TEXT,
    total_items      INTEGER NOT NULL DEFAULT 0,
    completed_items  INTEGER NOT NULL DEFAULT 0,
    progress         NUMERIC(5,2) NOT NULL DEFAULT 0,
    status           TEXT NOT NULL DEFAULT 'pending',
    deadline         DATE,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ============ model_versions：模型版本表 ============
CREATE TABLE model_versions (
    id          SERIAL PRIMARY KEY,
    model_name  TEXT NOT NULL,
    version     TEXT NOT NULL,
    dataset_id  INTEGER REFERENCES datasets(id),
    framework   TEXT,
    accuracy    NUMERIC(6,4),
    f1_score    NUMERIC(6,4),
    status      TEXT NOT NULL DEFAULT 'training',
    trained_by  TEXT,
    trained_at  TIMESTAMPTZ,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (model_name, version)
);

-- ============ api_keys：API Key 表 ============
CREATE TABLE api_keys (
    id           SERIAL PRIMARY KEY,
    key_name     TEXT NOT NULL,
    key_prefix   TEXT NOT NULL,
    masked_key   TEXT NOT NULL,
    tenant       TEXT NOT NULL,
    scope        TEXT NOT NULL DEFAULT 'read',
    created_by   TEXT,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_used_at TIMESTAMPTZ,
    status       TEXT NOT NULL DEFAULT 'active'
);

-- ============ audit_logs：审计日志表 ============
CREATE TABLE audit_logs (
    id         SERIAL PRIMARY KEY,
    event_type TEXT NOT NULL,
    actor      TEXT NOT NULL,
    actor_ip   TEXT,
    resource   TEXT,
    action     TEXT,
    detail     TEXT,
    severity   TEXT NOT NULL DEFAULT 'info',
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ============ security_findings：安全发现表（F1 Flag 所在） ============
CREATE TABLE security_findings (
    id              SERIAL PRIMARY KEY,
    finding_code    TEXT NOT NULL UNIQUE,
    title           TEXT NOT NULL,
    severity        TEXT NOT NULL,
    status          TEXT NOT NULL DEFAULT 'open',
    category        TEXT NOT NULL,
    finding_details TEXT,
    reporter        TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ============ internal_exports：内部导出表（F2 函数读取，readonly 无权直接 SELECT） ============
CREATE TABLE internal_exports (
    id            SERIAL PRIMARY KEY,
    export_name   TEXT NOT NULL,
    exported_by   TEXT NOT NULL,
    export_type   TEXT NOT NULL DEFAULT 'full',
    target_table  TEXT NOT NULL,
    row_count     INTEGER NOT NULL DEFAULT 0,
    data_payload  TEXT,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at    TIMESTAMPTZ
);

-- ============ regulated_model_training_records：受监管模型训练记录表（F3 Flag 所在，仅 admin/app_user 可访问） ============
CREATE TABLE regulated_model_training_records (
    id                 SERIAL PRIMARY KEY,
    model_name         TEXT NOT NULL,
    version            TEXT NOT NULL,
    customer_id        INTEGER NOT NULL REFERENCES customers(id),
    training_dataset   TEXT NOT NULL,
    data_classification TEXT NOT NULL DEFAULT 'restricted',
    regulator          TEXT NOT NULL,
    compliance_status  TEXT NOT NULL DEFAULT 'pending',
    compliance_audit   TEXT,
    reviewed_by        TEXT,
    trained_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_at         TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- 索引：常用查询路径
CREATE INDEX idx_contracts_customer_id     ON contracts(customer_id);
CREATE INDEX idx_label_tasks_customer_id   ON label_tasks(customer_id);
CREATE INDEX idx_label_tasks_dataset_id    ON label_tasks(dataset_id);
CREATE INDEX idx_model_versions_dataset_id ON model_versions(dataset_id);
CREATE INDEX idx_audit_logs_created_at     ON audit_logs(created_at);
CREATE INDEX idx_audit_logs_event_type     ON audit_logs(event_type);
CREATE INDEX idx_security_findings_status  ON security_findings(status);
CREATE INDEX idx_internal_exports_created  ON internal_exports(created_at);
