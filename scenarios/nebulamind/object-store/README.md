# NebulaMind object-store

NebulaMind 对象存储与模型资源服务，渗透靶场场景 Data 安全域组件。基于 MinIO（S3 兼容对象存储），承载公开模型产物、客户私有数据、模型注册表和 CI 构建产物。

## 服务规格

| 项 | 值 |
| --- | --- |
| 服务名 | `object-store` |
| 安全域 | Data |
| 端口 | `9000`（MinIO S3 API）、`9001`（MinIO Console，可选） |
| 镜像基础 | `minio/minio:RELEASE.2024-12-18T13-15-44Z` |
| 资源限制 | 0.75C / 512M（由平台容器编排层强制） |
| 健康检查 | `curl http://127.0.0.1:9000/minio/health/live` |
| S3 API | `$NM_OBJECT_STORE_URL` |

## 构建与运行

构建上下文为 `scenarios/nebulamind/` 目录（与其它 NebulaMind 服务一致）：

```bash
docker build -f object-store/Dockerfile -t nebulamind-object-store:latest scenarios/nebulamind/
```

运行（Flag 通过环境变量注入，镜像内不硬编码）：

```bash
docker run -d --name object-store \
  -p 9000:9000 -p 9001:9001 \
  -e GZCTF_FLAG_FLAG_OBJECT_BUCKET_POLICY='flag{d2_demo}' \
  nebulamind-object-store:latest
```

> Flag 环境变量命名规则：`GZCTF_FLAG_<TITLE>`，其中 `TITLE` 由 score item 的 `Title` 字段转大写、非字母数字转下划线得到。D2 score item 的 Title 为 `FLAG_OBJECT_BUCKET_POLICY`，故对应环境变量为 `GZCTF_FLAG_FLAG_OBJECT_BUCKET_POLICY`。`_shared/scripts/flag.sh` 的 `get_flag` 会自动解析。

## 访问凭据

| 角色 | Access Key | Secret Key | 权限 | 题目用途 |
| --- | --- | --- | --- | --- |
| Root | `nm-root-admin`（`MINIO_ROOT_USER`，可覆盖） | `nm-root-admin-secret-2026`（`MINIO_ROOT_PASSWORD`，可覆盖） | 全部 | 镜像默认 |
| 低权限用户 | `nm-low-priv-key` | `nm-low-priv-secret-2026` | 仅 `public-model-artifacts`（List + Get） | D2 入口 |
| 管理员用户 | `AKIA-NEBULA-ADMIN-2026` | `nm-admin-secret-2026` | 所有 bucket（全部操作） | 调试/验证 |

连接示例（aws-cli）：

```bash
# 低权限用户（只能访问 public-model-artifacts）
aws --endpoint-url $NM_OBJECT_STORE_URL s3 ls s3://public-model-artifacts/ \
  --access-key nm-low-priv-key --secret-key nm-low-priv-secret-2026

# 低权限用户访问 customer-private 会返回 403 Access Denied
aws --endpoint-url $NM_OBJECT_STORE_URL s3 ls s3://customer-private/ \
  --access-key nm-low-priv-key --secret-key nm-low-priv-secret-2026

# 管理员用户（可访问所有 bucket）
aws --endpoint-url $NM_OBJECT_STORE_URL s3 ls s3://customer-private/ \
  --access-key AKIA-NEBULA-ADMIN-2026 --secret-key nm-admin-secret-2026
```

连接示例（mc）：

```bash
mc alias set nmlocal $NM_OBJECT_STORE_URL nm-low-priv-key nm-low-priv-secret-2026
mc ls nmlocal/public-model-artifacts/
```

## Bucket 列表

| Bucket | 用途 | 低权限可访问 | 说明 |
| --- | --- | --- | --- |
| `public-model-artifacts` | 公开模型产物 | 是（List + Get） | **D2 Flag 载体所在**，含误放的租户摘要 CSV |
| `customer-private` | 客户私有数据 | **否**（403） | 合同、客户数据导出、数据库备份 |
| `model-registry` | 模型注册表 | **否**（403） | 模型 manifest（G3 终局线索） |
| `ci-artifacts` | CI 构建产物 | **否**（403） | 构建包、部署产物 |

## Bucket 内容

### public-model-artifacts

| 路径 | 说明 |
| --- | --- |
| `exports/tenant-summary-2026.csv` | **D2 Flag 载体**：误放的租户摘要导出，110 行，NM-T-0109 行含 Flag |
| `models/recommendation-v3-public.bin` | 公开推荐模型二进制（占位） |
| `models/classifier-v2-public.bin` | 公开分类模型二进制（占位） |
| `training-logs/recommendation-v4-private-train.log` | **G3 终局线索**：训练日志，含 audit_id 指向 customer-db |
| `docs/model-cards/README.md` | 模型卡片文档 |

### customer-private

| 路径 | 说明 |
| --- | --- |
| `contracts/2026-Q1-contracts.pdf` | 合同导出（占位） |
| `customer-data/exports.json` | 客户数据导出（占位） |
| `backups/db-backup-2026-06.sql` | 数据库备份（占位） |

### model-registry

| 路径 | 说明 |
| --- | --- |
| `model-manifests/recommendation-v4-private.json` | **G3 manifest**：引用训练日志和 customer-db 审计记录 |
| `model-manifests/classifier-v2-public.json` | 分类模型 manifest |

### ci-artifacts

| 路径 | 说明 |
| --- | --- |
| `builds/console-api/build-2026-06.tar.gz` | console-api 构建产物（占位） |
| `builds/doc-worker/build-2026-06.tar.gz` | doc-worker 构建产物（占位） |

## 题目与 Flag 注入位置

Flag 通过环境变量注入，由 `entrypoint.sh` 用 `sed` 替换种子文件中的占位符，处理后的文件上传到对应 bucket。

| Flag | 占位符 | 注入位置 | 漏洞 | 利用方式 |
| --- | --- | --- | --- | --- |
| D2 `FLAG_OBJECT_BUCKET_POLICY`（240 分） | `__NM_FLAG_D2__` | `public-model-artifacts/exports/tenant-summary-2026.csv`（NM-T-0109 行 compliance_note 列） | 对象存储 Bucket 权限错误 | 低权限用户列出/下载 public-model-artifacts，在误放的 CSV 中发现 Flag |
| G3 `FLAG_FINAL_MODEL_SUPPLY_CHAIN`（500 分） | — | **不在 object-store**，在 `customer-db` 的 `regulated_model_training_records` 表（id=6，compliance_audit 字段） | 多系统关联 | 见下方 G3 链路 |

### D2 解题链

1. 通过其他途径（CI 变量、配置文件、SSRF 等）获取低权限 access key：`nm-low-priv-key` / `nm-low-priv-secret-2026`。
2. 用低权限凭据连接 MinIO S3 API（`$NM_OBJECT_STORE_URL`）。
3. 列出 `public-model-artifacts` bucket，发现 `exports/tenant-summary-2026.csv`（误放的内部导出）。
4. 下载 CSV，在 NM-T-0109 行的 `compliance_note` 列发现 D2 Flag。
5. CSV 中还包含数据库表名线索（`customers contracts security_findings`），指向 `customer-db` 的 F1 链路。
6. 低权限用户尝试访问 `customer-private` bucket 会返回 403 Access Denied（权限策略正确拦截）。

### G3 终局链路（object-store 部分）

G3 是终局题目，需要关联多个系统。object-store 提供训练日志文件作为线索：

1. 在 `public-model-artifacts` bucket 中发现 `training-logs/recommendation-v4-private-train.log`。
2. 训练日志中包含关键线索：
   - `audit_id: audit-2026-006`
   - `audit_record_location: postgresql://$NM_CUSTOMER_DB_HOST:5432/nebulamind/regulated_model_training_records?id=6`
   - `audit_record_table: regulated_model_training_records`
   - `audit_record_id: 6`
   - `audit_field: compliance_audit`
   - `manifest_location: $NM_MODEL_REGISTRY_URL/api/v1/models/recommendation-v4-private/versions/v4/manifest`
3. 在 `model-registry` bucket 中找到 `model-manifests/recommendation-v4-private.json`（需高权限或 root 凭据）。
4. manifest 中的 `compliance` 字段确认审计记录位置：`postgresql://$NM_CUSTOMER_DB_HOST:5432/nebulamind/regulated_model_training_records?id=6`。
5. 用 `admin` 账号连接 `customer-db`，查询 `regulated_model_training_records` 表 id=6 的 `compliance_audit` 字段，获得 G3 Flag。

> **注意**：G3 的 Flag（`FLAG_FINAL_MODEL_SUPPLY_CHAIN`）实际存储在 `customer-db` 服务中，不在 object-store。object-store 只提供训练日志和 manifest 作为线索链路的一部分。

## 文件清单

```
object-store/
├── Dockerfile                          # 多阶段构建：Alpine 下载 mc + 静态 curl，minio 运行
├── entrypoint.sh                       # Flag 注入 + MinIO 启动 + bucket 初始化编排
├── init-buckets.sh                     # mc 命令：创建 bucket、用户、策略、上传种子
├── healthcheck.sh                      # 健康检查脚本（curl 优先，mc 回退）
├── policies/
│   ├── low-priv-policy.json            # 低权限策略：仅 public-model-artifacts List+Get
│   └── admin-policy.json               # 管理员策略：所有 bucket 全部操作
├── seed/
│   ├── tenant-summary-2026.csv         # D2 Flag 载体（110 行，含 __NM_FLAG_D2__ 占位符）
│   ├── recommendation-v4-private-train.log  # G3 训练日志（含 audit_id 线索）
│   ├── recommendation-v4-private.json  # G3 model manifest
│   ├── classifier-v2-public.json       # 分类模型 manifest
│   └── model-cards-README.md           # 模型卡片文档
└── README.md                           # 本文件
```

## 设计约束

- 镜像内**不硬编码**任何 Flag，全部通过环境变量注入。
- 不写死任何 IP/域名；S3 API、模型仓库和数据库目标由平台注入 `NM_OBJECT_STORE_URL`、`NM_MODEL_REGISTRY_URL`、`NM_CUSTOMER_DB_HOST` 后渲染。
- MinIO 真实运行，支持完整 S3 API（ListBucket、GetObject、PutObject 等）。
- 低权限用户 `nm-low-priv` 的 IAM 策略显式 Deny 访问 `customer-private`、`model-registry`、`ci-artifacts`。
- 高权限用户 `nm-admin` 可访问所有 bucket。
- D2 Flag 在误放的 CSV 中（权限配置错误的体现：内部导出不应出现在公开 bucket）。
- G3 的 Flag 不在 object-store，训练日志只提供线索指向 customer-db。

## 跨服务一致性提示

- D2 的 CSV 中 `compliance_note` 列引用了数据库表名 `customers contracts security_findings`，与 `customer-db` 服务的表结构一致。
- G3 训练日志中的 `audit_id=audit-2026-006` 对应 `customer-db` 中 `regulated_model_training_records` 表 id=6 的记录（`nm-medical-seg v2.0.0`，联影医疗，compliance_audit 字段含 F3/G3 Flag）。
- G3 manifest 中的 `audit_record_location` 指向 `postgresql://$NM_CUSTOMER_DB_HOST:5432/nebulamind/regulated_model_training_records?id=6`。
- 低权限 access key `nm-low-priv-key` 应通过其他服务（如 `ci-runner` 的 CI 变量、`support-upload` 的配置文件）泄露给选手，不在 object-store 直接暴露。
