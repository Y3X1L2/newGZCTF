# NebulaMind customer-db

NebulaMind 客户与标注数据库服务，渗透靶场场景 Data 安全域组件。基于 PostgreSQL 16，承载客户、合同、标注任务、模型版本、API Key、审计日志、安全发现、内部导出、受监管训练记录等业务数据。

## 服务规格

| 项 | 值 |
| --- | --- |
| 服务名 | `customer-db` |
| 安全域 | Data |
| 端口 | `5432`（PostgreSQL） |
| 镜像基础 | `postgres:16-alpine` |
| 资源限制 | 1C / 768M（由平台容器编排层强制） |
| 健康检查 | `pg_isready -U postgres` |
| 默认数据库 | `nebulamind` |

## 构建与运行

构建上下文为 `scenarios/nebulamind/` 目录（与其它 NebulaMind 服务一致）：

```bash
docker build -f customer-db/Dockerfile -t nebulamind-customer-db:latest scenarios/nebulamind/
```

运行（Flag 通过环境变量注入，镜像内不硬编码）：

```bash
docker run -d --name customer-db \
  -p 5432:5432 \
  -e GZCTF_FLAG_FLAG_DB_READONLY_CUSTOMERS='flag{f1_demo}' \
  -e GZCTF_FLAG_FLAG_DB_PRIVESC_FUNCTION='flag{f2_demo}' \
  -e GZCTF_FLAG_FLAG_DB_CORE_CUSTOMER_DATA='flag{f3_demo}' \
  -e NM_DB_ADMIN_PASSWORD='nm_admin_dev_2026' \
  nebulamind-customer-db:latest
```

> Flag 环境变量命名规则：`GZCTF_FLAG_<TITLE>`，其中 `TITLE` 由 score item 的 `Title` 字段转大写、非字母数字转下划线得到。本服务三个 score item 的 Title 分别为 `FLAG_DB_READONLY_CUSTOMERS`、`FLAG_DB_PRIVESC_FUNCTION`、`FLAG_DB_CORE_CUSTOMER_DATA`，故对应环境变量为 `GZCTF_FLAG_FLAG_DB_READONLY_CUSTOMERS` 等。`_shared/scripts/flag.sh` 的 `get_flag` 会自动解析。

## 数据库账号

| 角色 | 用户名 | 密码 | 权限 | 题目用途 |
| --- | --- | --- | --- | --- |
| 超级用户 | `postgres` | `nm_postgres_dev_2026`（`POSTGRES_PASSWORD`，可覆盖） | 全部 | 镜像默认 |
| 管理员 | `admin` | `nm_admin_dev_2026`（`NM_DB_ADMIN_PASSWORD`，可覆盖） | 全部业务表 + 受监管记录 | F3 链路终点 |
| 应用账号 | `app_user` | `nm_app_user_dev_2026` | 常规业务表读写 + 受监管记录 | 业务服务 |
| 只读账号 | `readonly` | `readonly_password_2026` | 8 张常规业务表 SELECT + `export_internal_data` EXECUTE | F1 入口 / F2 利用 |

连接串示例：

```
postgresql://readonly:readonly_password_2026@$NM_CUSTOMER_DB_HOST:5432/nebulamind
postgresql://admin:nm_admin_dev_2026@$NM_CUSTOMER_DB_HOST:5432/nebulamind
```

## 表结构

| 表 | 行数 | readonly 可 SELECT | 说明 |
| --- | --- | --- | --- |
| `customers` | 30 | 是 | 客户表（金融/医疗/教育/零售/制造/能源） |
| `contracts` | 30 | 是 | 合同表（合同号、金额、签订/到期日期） |
| `datasets` | 25 | 是 | 数据集表（类型、行数、敏感级别） |
| `label_tasks` | 55 | 是 | 标注任务表（任务名、状态、标注员、进度） |
| `model_versions` | 20 | 是 | 模型版本表（版本号、数据集、评估指标、状态） |
| `api_keys` | 25 | 是 | API Key 表（key 名、掩码 key、租户、scope） |
| `audit_logs` | 80 | 是 | 审计日志表（事件类型、操作者、IP、详情、严重级别） |
| `security_findings` | 12 | 是 | 安全发现表（**F1 Flag 所在**） |
| `internal_exports` | 12 | **否** | 内部导出表（F2 函数读取，**F2 Flag 所在**） |
| `regulated_model_training_records` | 12 | **否** | 受监管模型训练记录表（仅 admin/app_user，**F3 Flag 所在**） |

总数据量 301 行。

## 题目与 Flag 注入位置

Flag 通过环境变量注入，由 `entrypoint.sh` 用 `sed` 替换 `init/*.sql` 中的占位符，处理后的 SQL 复制到 `/docker-entrypoint-initdb.d/` 由官方 postgres entrypoint 在首次初始化时执行。

| Flag | 占位符 | 注入位置（表.字段） | 漏洞 | 利用方式 |
| --- | --- | --- | --- | --- |
| F1 `FLAG_DB_READONLY_CUSTOMERS`（260 分） | `__NM_FLAG_F1__` | `security_findings.finding_details`（id=7） | 数据库只读凭据复用 | 以 `readonly` 登录后 `SELECT * FROM security_findings` |
| F2 `FLAG_DB_PRIVESC_FUNCTION`（360 分） | `__NM_FLAG_F2__` | `internal_exports.data_payload`（id=3） | SECURITY DEFINER 函数权限错误 | `SELECT * FROM export_internal_data('internal_exports');` |
| F3 `FLAG_DB_CORE_CUSTOMER_DATA`（420 分） | `__NM_FLAG_F3__` | `regulated_model_training_records.compliance_audit`（id=6） | 链路终点数据访问 | 以 `admin` 登录后 `SELECT compliance_audit FROM regulated_model_training_records` |

### F1 解题链

1. 通过对象存储配置或 CI 变量（`ci-runner` 服务的项目变量）获取 `readonly` 账号凭据。
2. 以 `readonly / readonly_password_2026` 连接平台注入的 `$NM_CUSTOMER_DB_HOST:5432/nebulamind`。
3. `SELECT * FROM security_findings;` → `finding_details` 字段含 F1 Flag。

### F2 解题链

1. 以 `readonly` 登录（同 F1）。
2. `readonly` 无权直接 `SELECT internal_exports`，但被错误授予 `export_internal_data(text)` 函数的 `EXECUTE` 权限。
3. 该函数声明为 `SECURITY DEFINER`，以 `postgres` 身份执行，返回 `internal_exports` 全表数据。
4. `SELECT * FROM export_internal_data('internal_exports');` → `data_payload` 字段含 F2 Flag。
5. 这是业务权限提升，**不是**数据库主机 RCE。

### F3 解题链

1. 通过 `ci-runner` 的命令注入（E3）读取 `/opt/nebulamind/vault-credentials.json`，获取 Vault bootstrap token。
2. 用 token 访问 `secrets-vault` 服务，读取 `secret/nebulamind/db-credentials` 获取 `admin` 数据库密码（`NM_DB_ADMIN_PASSWORD`）。
3. 以 `admin` 登录 `customer-db`。
4. `SELECT compliance_audit FROM regulated_model_training_records;` → `compliance_audit` 字段含 F3 Flag。
5. `readonly` 无权访问该表，必须获取更高权限账号。

## 文件清单

```
customer-db/
├── Dockerfile              # 基于 postgres:16-alpine，EXPOSE 5432，健康检查 pg_isready
├── entrypoint.sh           # 包装官方 entrypoint，注入 F1/F2/F3 Flag 与 admin 密码
├── init/
│   ├── 01-schema.sql       # 建表（10 张表 + 索引）
│   ├── 02-seed-data.sql    # 种子数据（301 行，含 Flag 占位符）
│   ├── 03-functions.sql    # SECURITY DEFINER 函数 export_internal_data（F2）
│   └── 04-permissions.sql  # 角色创建与权限分配（含 admin 密码占位符）
└── README.md               # 本文件
```

## 设计约束

- 镜像内**不硬编码**任何 Flag，全部通过环境变量注入。
- 不写死任何 IP/域名；连接串中的主机由平台通过 `NM_CUSTOMER_DB_HOST` 注入，镜像和文档不依赖 Docker 服务名解析。
- F2 为 `SECURITY DEFINER` 函数权限提升，非数据库主机 RCE。
- F3 需更高权限账号（`admin`），`readonly` 不可达。
- `internal_exports` 与 `regulated_model_training_records` 对 `readonly` 显式 `REVOKE`，且对 `PUBLIC` 收紧默认权限。

## 跨服务一致性提示

`ci-runner` 现有 CI 变量中 `DATABASE_URL` 的 readonly 密码为占位值 `password`，与本服务的 `readonly_password_2026` 不一致。场景联调时需将 `ci-runner` 的 `DATABASE_URL` 变量更新为 `postgresql://readonly:readonly_password_2026@$NM_CUSTOMER_DB_HOST:5432/nebulamind`，或通过对象存储配置暴露真实 readonly 凭据。`admin` 密码应与 `secrets-vault` 的 `secret/nebulamind/db-credentials` 保持一致。
