# NebulaMind 渗透靶场 — 项目交接文档

> **文档目的**：为接手此项目的工程师提供完整的全链路说明和 Docker 文件分布，使其无需阅读其他 AI 的工作记录即可理解、构建、部署、维护此渗透靶场场景。
>
> **场景路径**：`scenarios/nebulamind/`
> **设计文档**：`docs/pentest-ai-enterprise-scenario-design.md`
> **审查记录**：`docs/nebulamind-review-findings.md`

---

## 一、项目概述

NebulaMind AI Corp 是一个企业级渗透测试靶场场景，模拟一家 AI 公司的完整内部网络。场景包含 **12 个 Docker 服务**、**21 个 Flag**、**5 个安全域**，覆盖从外网打点到内网深度渗透的完整攻击链路。

### 1.1 核心特征

- **无 docker-compose.yml**：部署由 GZCTF 渗透编排平台动态管理，所有跨服务地址通过 `NM_*` 环境变量注入
- **Flag 动态注入**：Flag 通过平台环境变量 (`GZCTF_FLAG_*`) 注入，镜像内不硬编码
- **多级网段隔离**：Public → DMZ → Business → Operations → Data，逐级渗透
- **D3 命令注入为核心枢纽**：14/21 个 Flag 依赖 D3 RCE 作为唯一入口

### 1.2 技术栈

| 组件 | 技术 |
|------|------|
| Web 服务 | Python 3.11 + Flask |
| 网关 | Nginx 1.27 |
| 数据库 | PostgreSQL 16 |
| 缓存 | Redis 7.2 |
| 对象存储 | MinIO |
| Git 服务 | Python + git CLI |
| CI 服务 | Python + Flask |
| 密钥服务 | Python (Vault mock) |
| 模型仓库 | Python + Flask |

---

## 二、文件分布总览

### 2.1 完整目录树

```
scenarios/nebulamind/
│
├── _shared/                              # 共享资源（所有服务 COPY 引用）
│   ├── assets/
│   │   └── nebulamind.css                # 共享企业品牌样式（深色主题）
│   └── scripts/
│       ├── flag.sh                       # Shell 版 Flag 读取助手
│       ├── flag.py                       # Python 版 Flag 读取助手
│       └── runtime-env.sh                # 运行时环境变量校验助手
│
├── edge-gateway/                         # [Public] 外部入口网关
│   ├── Dockerfile                        # 基于 nginx:1.27-alpine
│   ├── entrypoint.sh                     # 注入 A1 Flag 到 build-info.json
│   ├── nginx.conf                        # Nginx 主配置（worker 以 nmapp 运行）
│   ├── default.conf                      # 80 端口配置（代理 portal-web + support-upload）
│   ├── mirror.conf                       # 8080 端口配置（镜像下载站）
│   ├── build-info.json                   # 构建信息模板（运行时由 entrypoint 覆盖）
│   └── healthz.html                      # 健康检查页面
│
├── portal-web/                           # [DMZ] 企业官网
│   ├── Dockerfile                        # 基于 python:3.11-alpine
│   ├── entrypoint.sh                     # 渲染 NM_* 占位符
│   ├── app.py                            # Flask 应用（首页/产品/客户/新闻/登录）
│   ├── static/
│   │   ├── css/portal.css
│   │   ├── js/app.js                     # 含 tenant_001 线索（A3 sourcemap 目标）
│   │   ├── js/app.js.map                 # Source Map 文件（A3 Flag 所在）
│   │   └── resources/
│   │       ├── robots.txt                # 指向 /resources/archive/（A2 入口）
│   │       └── archive/
│   │           ├── customer-cases-summary.html
│   │           └── nebulamind-whitepaper-v1.html  # A2 Flag 在 HTML 注释中
│   └── templates/                        # 6 个页面模板
│
├── support-upload/                       # [DMZ] 客户支持上传中心
│   ├── Dockerfile                        # 基于 python:3.11-alpine
│   ├── entrypoint.sh                     # 注入 B1 Flag 到文件、B2 Flag 到 worker.yml
│   ├── app.py                            # Flask 应用（上传/下载/解析转发）
│   ├── config/
│   │   └── worker.yml                    # 队列配置（B2 Flag + 内部地址线索）
│   ├── requirements.txt
│   ├── static/css/upload.css
│   └── templates/                        # 3 个页面模板
│
├── document-worker/                      # [Business] 文档解析 Worker
│   ├── Dockerfile                        # 基于 python:3.11-alpine，内置假 convert 脚本
│   ├── entrypoint.sh                     # 注入 B3/D3 Flag + 创建 service-account.json
│   ├── app.py                            # Flask 应用（SSRF + 命令注入）
│   ├── config/
│   │   └── worker.yml                    # Worker 配置（含内部端点地址）
│   ├── requirements.txt
│   └── static/css/worker.css
│
├── ai-console-api/                       # [Business] AI 控制台 API
│   ├── Dockerfile                        # 基于 python:3.11-alpine
│   ├── entrypoint.sh                     # 注入 C1/C2/C3 Flag 环境变量
│   ├── app.py                            # Flask 应用（IDOR + JWT + GraphQL）
│   ├── seed_data.py                      # 知识库/审计日志/集成密钥种子数据
│   ├── requirements.txt
│   ├── static/css/console.css
│   └── templates/
│       ├── index.html                    # /docs 页面（含 viewer/viewer123 凭据）
│       └── graphiql.html                 # GraphQL IDE（含示例查询）
│
├── cache-broker/                         # [Business] Redis 任务队列
│   ├── Dockerfile                        # 基于 redis:7.2-alpine
│   ├── entrypoint.sh                     # 启动 Redis → 注入种子数据 → 重启
│   ├── init.lua                          # 种子数据脚本（D1 Flag 在 task_003 中）
│   └── redis.conf                        # Redis 配置（无密码、禁用危险命令）
│
├── git-service/                          # [Operations] 内部 Git 服务
│   ├── Dockerfile                        # 基于 python:3.11-alpine
│   ├── entrypoint.sh                     # 创建 3 个仓库 + 提交历史（886 行）
│   ├── app.py                            # Flask Git Web UI
│   ├── repos/                            # 预置仓库目录（运行时由 entrypoint 初始化 git）
│   │   ├── console-api/                  # 仓库 1（.env.example.old 含 E1 Flag）
│   │   ├── doc-worker/                   # 仓库 2
│   │   └── infra-playbooks/              # 仓库 3
│   ├── requirements.txt
│   ├── static/css/git.css
│   └── templates/                        # 6 个页面模板
│
├── ci-runner/                            # [Operations] CI 构建节点
│   ├── Dockerfile                        # 基于 python:3.11-alpine
│   ├── entrypoint.sh                     # 注入 E2/E3 Flag + 创建 vault-credentials.json
│   ├── app.py                            # Flask CI Web UI（变量泄露 + 构建注入）
│   ├── config/
│   │   ├── nebulaci-console-api.yml      # CI 配置（console-api 项目）
│   │   └── nebulaci-doc-worker.yml       # CI 配置（doc-worker 项目）
│   ├── requirements.txt
│   ├── static/css/ci.css
│   └── templates/                        # 3 个页面模板
│
├── customer-db/                          # [Data] PostgreSQL 数据库
│   ├── Dockerfile                        # 基于 postgres:16-alpine
│   ├── entrypoint.sh                     # 注入 F1/F2/F3/G3 Flag + admin 密码到 SQL
│   ├── init/
│   │   ├── 01-schema.sql                 # 表结构定义（10+ 张表）
│   │   ├── 02-seed-data.sql              # 种子数据（300+ 行，含 Flag 占位符）
│   │   ├── 03-functions.sql              # SECURITY DEFINER 函数（F2 漏洞）
│   │   └── 04-permissions.sql            # 角色权限分配（F1/F2 漏洞设计）
│   └── README.md                         # 本服务说明文档
│
├── object-store/                         # [Data] MinIO 对象存储
│   ├── Dockerfile                        # 基于 minio/minio + 多阶段构建
│   ├── entrypoint.sh                     # 启动 MinIO → 初始化 bucket → 前台运行
│   ├── init-buckets.sh                   # 创建 bucket/用户/策略/上传种子文件
│   ├── healthcheck.sh                    # MinIO 健康检查脚本
│   ├── policies/
│   │   ├── admin-policy.json             # 管理员策略
│   │   └── low-priv-policy.json          # 低权限策略（D2 漏洞设计）
│   ├── seed/
│   │   ├── tenant-summary-2026.csv       # D2 Flag + DB 表名线索
│   │   ├── recommendation-v4-private-train.log  # G3 供应链审计日志
│   │   ├── recommendation-v4-private.json       # 模型 manifest
│   │   ├── classifier-v2-public.json             # 公开模型 manifest
│   │   └── model-cards-README.md
│   └── README.md
│
├── secrets-vault/                        # [Data] Vault mock 密钥服务
│   ├── Dockerfile                        # 基于 python:3.11-alpine
│   ├── entrypoint.sh                     # 注入 G1 Flag 到 secrets.json
│   ├── app.py                            # Flask Vault mock（POLICY_PATHS 鉴权）
│   ├── policies/
│   │   ├── bootstrap.json                # Bootstrap 策略（G1 漏洞设计）
│   │   ├── ci-reader.json                # CI 只读策略
│   │   └── model-admin.json              # 模型管理员策略
│   ├── seed/
│   │   └── secrets.json                  # 密钥种子数据（含 G1 Flag + admin token）
│   ├── requirements.txt
│   ├── static/css/vault.css
│   └── templates/index.html
│
├── model-registry/                       # [Data] 模型仓库
│   ├── Dockerfile                        # 基于 python:3.11-alpine
│   ├── entrypoint.sh                     # 注入 G2 Flag 到 models.json
│   ├── app.py                            # Flask 模型仓库（越权下载 G2）
│   ├── seed/
│   │   └── models.json                   # 模型 manifest（G2 Flag + G3 审计线索）
│   ├── requirements.txt
│   ├── static/css/registry.css
│   └── templates/                        # 2 个页面模板
│
├── .dockerignore
└── README.md                             # 场景总说明文档
```

### 2.2 文件统计

| 类别 | 文件数 | 说明 |
|------|--------|------|
| Dockerfile | 12 | 每个服务一个 |
| entrypoint.sh | 12 | 每个服务一个（Flag 注入 + 配置渲染） |
| Python 应用代码 | 8 | edge-gateway/portal-web 无 .py（nginx/纯静态） |
| Shell 脚本 | 5 | init-buckets.sh / healthcheck.sh / 3 个共享脚本 |
| SQL 文件 | 4 | customer-db/init/ |
| 配置文件 | 8 | nginx.conf / redis.conf / worker.yml / policies / configs |
| 种子数据 | 7 | JSON / CSV / LOG 文件 |
| HTML 模板 | 21 | 各服务 templates/ |
| 静态资源 | 8 | CSS / JS / Source Map |
| **总计** | **~85** | 不含 .dockerignore / README |

---

## 三、服务架构

### 3.1 服务清单

| # | 服务 | 安全域 | 端口 | Docker 基础镜像 | 资源限制 | Flag 数 | Flag ID |
|---|------|--------|------|----------------|----------|---------|---------|
| 1 | edge-gateway | Public | 80, 8080 | `nginx:1.27-alpine` | 0.25C/128M | 1 | A1 |
| 2 | portal-web | DMZ | 8080 | `python:3.11-alpine` | 0.5C/256M | 2 | A2, A3 |
| 3 | support-upload | DMZ | 8080 | `python:3.11-alpine` | 0.5C/256M | 2 | B1, B2 |
| 4 | document-worker | Business | 8080 | `python:3.11-alpine` | 0.75C/512M | 2 | B3, D3 |
| 5 | ai-console-api | Business | 8080 | `python:3.11-alpine` | 1C/512M | 3 | C1, C2, C3 |
| 6 | cache-broker | Business | 6379 | `redis:7.2-alpine` | 0.25C/128M | 1 | D1 |
| 7 | git-service | Operations | 3000 | `python:3.11-alpine` | 1C/512M | 1 | E1 |
| 8 | ci-runner | Operations | 8080 | `python:3.11-alpine` | 1C/512M | 2 | E2, E3 |
| 9 | customer-db | Data | 5432 | `postgres:16-alpine` | 1C/768M | 4 | F1, F2, F3, G3 |
| 10 | object-store | Data | 9000, 9001 | `minio/minio` | 0.75C/512M | 1 | D2 |
| 11 | secrets-vault | Data | 8200 | `python:3.11-alpine` | 0.5C/256M | 1 | G1 |
| 12 | model-registry | Data | 8080 | `python:3.11-alpine` | 0.5C/256M | 2 | G2, (G3 线索) |

**总计**：12 服务，21 Flag，单队资源约 7.5C/4.5G

### 3.2 网络拓扑

```
选手 → edge-gateway (Public, 80/8080)
         ├─→ portal-web (DMZ, 8080)
         └─→ support-upload (DMZ, 8080) → document-worker (Business, 8080)
                                          └─→ ai-console-api (Business, 8080)
                                                ├─→ cache-broker (Business, 6379)
                                                ├─→ customer-db (Data, 5432)
                                                └─→ git-service (Operations, 3000) → ci-runner (Operations, 8080)
                                                                                      ├─→ secrets-vault (Data, 8200)
                                                                                      └─→ object-store (Data, 9000)
                                                                                            └─→ model-registry (Data, 8080)
```

### 3.3 选手入口

- **唯一入口**：edge-gateway 的 80 和 8080 端口
- **80 端口**：企业官网（portal-web）+ 客户上传中心（support-upload，路径 `/support/`）
- **8080 端口**：镜像下载站（含 `/status/build-info` 构建信息）
- 其他所有服务的端口不对选手直接暴露，需通过 SSRF/RCE/凭据链路间接访问

---

## 四、Docker 构建说明

### 4.1 构建上下文

**关键**：所有服务的 Dockerfile 构建上下文为 `scenarios/nebulamind/` 目录（不是服务子目录），因为需要 `COPY _shared/` 共享资源。

### 4.2 构建命令

```bash
# 在项目根目录执行

# 构建单个服务（以 edge-gateway 为例）
docker build -t nebulamind-edge-gateway:2026.06 \
  -f scenarios/nebulamind/edge-gateway/Dockerfile \
  scenarios/nebulamind/

# 构建所有服务
for svc in edge-gateway portal-web support-upload document-worker \
           ai-console-api cache-broker git-service ci-runner \
           customer-db object-store secrets-vault model-registry; do
    docker build -t nebulamind-${svc}:2026.06 \
      -f scenarios/nebulamind/${svc}/Dockerfile \
      scenarios/nebulamind/
done
```

### 4.3 各服务 Dockerfile 特点

| 服务 | 构建特点 |
|------|----------|
| edge-gateway | nginx 基础镜像，COPY nginx.conf + default.conf + mirror.conf，entrypoint 渲染 `__NM_*__` 占位符并生成 build-info.json |
| portal-web | python:3.11-alpine，COPY app.py + static/ + templates/，entrypoint 渲染 NM_* 到 app.js 和 HTML |
| support-upload | python:3.11-alpine，COPY app.py + config/worker.yml，entrypoint 注入 B1 Flag 到文件、B2 Flag 到 worker.yml |
| document-worker | python:3.11-alpine，**额外创建假 `/usr/local/bin/convert` 脚本**（D3 命令注入目标），entrypoint 创建 `/opt/nebulamind/service-account.json` |
| ai-console-api | python:3.11-alpine，COPY app.py + seed_data.py + templates/graphiql.html，entrypoint 导出 C1/C2/C3 Flag 环境变量 |
| cache-broker | redis:7.2-alpine，COPY redis.conf + init.lua，entrypoint 先后台启动 Redis 注入种子数据再前台运行 |
| git-service | python:3.11-alpine + git，entrypoint.sh **886 行**，创建 3 个仓库各 9 次提交，历史中含 .env.example.old |
| ci-runner | python:3.11-alpine，entrypoint 创建 `/opt/nebulamind/vault-credentials.json`（含 bootstrap token） |
| customer-db | postgres:16-alpine，COPY init/*.sql 到 `/docker-entrypoint-initdb.d/`，entrypoint 用 sed 替换 Flag 和密码占位符 |
| object-store | **多阶段构建**：builder 阶段下载 mc + curl-static，运行阶段基于 minio/minio，entrypoint 先后台启动 MinIO 初始化 bucket 再前台运行 |
| secrets-vault | python:3.11-alpine，COPY app.py + policies/ + seed/，entrypoint 用 sed 替换 G1 Flag 占位符 |
| model-registry | python:3.11-alpine，COPY app.py + seed/models.json，entrypoint 用 sed 替换 G2 Flag 占位符 |

### 4.4 共享资源机制

所有服务通过 `COPY _shared/` 引用共享资源：

```
_shared/
├── assets/nebulamind.css       # 统一企业品牌样式
└── scripts/
    ├── flag.sh                 # Shell: get_flag / write_flag_file
    ├── flag.py                 # Python: get_flag / write_flag_file
    └── runtime-env.sh          # nm_require_all / nm_render_required_placeholders
```

---

## 五、Flag 注入机制

### 5.1 平台注入规则

GZCTF 平台将 Flag 作为环境变量注入到每个容器：

- 环境变量名：`GZCTF_FLAG_<TITLE>`
- `TITLE` 由 score item 的 Title 字段转大写、非字母数字转下划线得到
- 兜底变量：`GZCTF_FLAG`（第一个 Flag）

### 5.2 Flag 读取助手

#### Shell 版 (`_shared/scripts/flag.sh`)

```sh
# 读取 Flag（自动剥离输入中的 FLAG_ 前缀）
get_flag 'FLAG_PUBLIC_DISCOVERY' 'flag{default}'
# → 查找环境变量 GZCTF_FLAG_PUBLIC_DISCOVERY

# 写入 Flag 到文件
write_flag_file '/path/to/flag.txt' 'FLAG_PUBLIC_DISCOVERY' 0644
```

**关键逻辑**：`flag_normalize_name()` 会剥离输入参数中的 `FLAG_` 前缀，然后查找 `GZCTF_FLAG_< stripped_name >`。因此 `get_flag 'FLAG_PUBLIC_DISCOVERY'` 查找 `GZCTF_FLAG_PUBLIC_DISCOVERY`。

#### Python 版 (`_shared/scripts/flag.py`)

```python
from flag import get_flag
flag = get_flag('FLAG_PUBLIC_DISCOVERY', 'flag{default}')
# → 查找环境变量 GZCTF_FLAG_PUBLIC_DISCOVERY
```

### 5.3 各服务 Flag 注入方式

| 服务 | Flag | 注入方式 | 注入位置 |
|------|------|----------|----------|
| edge-gateway | A1 | entrypoint.sh 生成 build-info.json | `/app/build-info.json` 的 metadata.flag 字段 |
| portal-web | A2 | entrypoint.sh sed 替换 | `nebulamind-whitepaper-v1.html` HTML 注释 |
| portal-web | A3 | entrypoint.sh sed 替换 | `app.js.map` 的 sourcesContent |
| support-upload | B1 | entrypoint.sh 写入文件 | `/app/config/b1_flag.txt` |
| support-upload | B2 | entrypoint.sh sed 替换 | `/app/config/worker.yml` 的 diagnostic.test_flag |
| document-worker | B3 | entrypoint.sh 导出环境变量 | `GZCTF_FLAG_WORKER_SSRF_METADATA` → app.py 读取 |
| document-worker | D3 | entrypoint.sh 写入文件 | `/opt/nebulamind/worker.flag` |
| ai-console-api | C1 | app.py 运行时读取环境变量 | 知识库 id=17 的 description |
| ai-console-api | C2 | app.py 运行时读取环境变量 | 审计日志 git.sync 事件 metadata |
| ai-console-api | C3 | app.py 运行时读取环境变量 | 集成密钥 flag 字段 |
| cache-broker | D1 | init.lua 运行时读取参数 | Redis key `task:result:task_003` 的 metadata.debug.envSnapshot.INTERNAL_QUEUE_TOKEN |
| object-store | D2 | entrypoint.sh sed 替换 | `tenant-summary-2026.csv` 第 110 行 |
| git-service | E1 | entrypoint.sh git 提交 | console-api 仓库历史中的 `.env.example.old` |
| ci-runner | E2 | entrypoint.sh 导出环境变量 | `GZCTF_FLAG_CI_VARIABLE_LEAK` → app.py 读取 |
| ci-runner | E3 | entrypoint.sh 写入文件 | `/opt/nebulamind/ci.flag` |
| customer-db | F1 | entrypoint.sh sed 替换 | `security_findings` 表 id=7 的 finding_details |
| customer-db | F2 | entrypoint.sh sed 替换 | `internal_exports` 表的 data_payload |
| customer-db | F3 | entrypoint.sh sed 替换 | `regulated_model_training_records` id=6 的 compliance_audit |
| customer-db | G3 | entrypoint.sh sed 替换 | `regulated_model_training_records` id=13 的 compliance_audit |
| secrets-vault | G1 | entrypoint.sh sed 替换 | `secrets.json` 的 model-registry secret flag 字段 |
| model-registry | G2 | entrypoint.sh sed 替换 | `models.json` 的 recommendation-v4-private v4 manifest flag 字段 |

### 5.4 关键凭据文件（非 Flag，用于链路串联）

| 文件 | 所在容器 | 路径 | 内容 | 获取方式 |
|------|----------|------|------|----------|
| service-account.json | document-worker | `/opt/nebulamind/service-account.json` | JWT 密钥候选值 `nebulamind-dev-secret-2026` | D3 RCE: `$(cat /opt/nebulamind/service-account.json)` |
| worker.yml | support-upload | `/app/config/worker.yml` | worker token, Redis 地址, AI Console URL | B2 路径穿越: `/download?file=../config/worker.yml` |
| vault-credentials.json | ci-runner | `/opt/nebulamind/vault-credentials.json` | Vault bootstrap token `s.bootstrap-nebulamind-2026` | E3 RCE: `$(cat /opt/nebulamind/vault-credentials.json)` |
| .env.example.old | git-service | git 历史（console-api 仓库） | CI_RUNNER_URL, DATABASE_URL | E1: `git show <sha>:.env.example.old` |

---

## 六、NM_* 环境变量矩阵

所有跨服务地址由 GZCTF 平台注入 `NM_*` 环境变量。服务启动时使用 `runtime-env.sh` 的 `nm_require_all` 校验必需变量，缺失则 fail fast。

| 服务 | 必需平台变量 |
|------|-------------|
| edge-gateway | `NM_PORTAL_WEB_URL`, `NM_SUPPORT_UPLOAD_URL` |
| portal-web | `NM_AI_CONSOLE_API_URL`, `NM_AI_CONSOLE_API_HOST` |
| support-upload | `NM_DOCUMENT_WORKER_URL`, `NM_DOCUMENT_WORKER_HOST`, `NM_CACHE_BROKER_HOST`, `NM_AI_CONSOLE_API_URL` |
| document-worker | `NM_DOCUMENT_WORKER_URL`, `NM_DOCUMENT_WORKER_HOST`, `NM_CACHE_BROKER_HOST`, `NM_AI_CONSOLE_API_URL`, `NM_AI_CONSOLE_API_HOST` |
| ai-console-api | `NM_GIT_SERVICE_URL`, `NM_OBJECT_STORE_URL` |
| cache-broker | `NM_CACHE_BROKER_HOST` |
| git-service | `NM_GIT_SERVICE_URL`, `NM_CUSTOMER_DB_HOST`, `NM_OBJECT_STORE_URL`, `NM_CACHE_BROKER_HOST`, `NM_AI_CONSOLE_API_URL`, `NM_DOCUMENT_WORKER_URL`, `NM_PORTAL_WEB_URL`, `NM_CI_RUNNER_URL` |
| ci-runner | `NM_GIT_SERVICE_URL`, `NM_CUSTOMER_DB_HOST`, `NM_CACHE_BROKER_HOST`, `NM_SECRETS_VAULT_URL` |
| customer-db | 可选 `NM_DB_ADMIN_PASSWORD`；Flag 通过 `GZCTF_FLAG_*` 注入 |
| object-store | `NM_CUSTOMER_DB_HOST`, `NM_MODEL_REGISTRY_URL`, `NM_OBJECT_STORE_URL` |
| secrets-vault | `NM_CUSTOMER_DB_HOST`, `NM_MODEL_REGISTRY_URL`, `NM_CI_RUNNER_URL`, `NM_OBJECT_STORE_URL`, `NM_OBJECT_STORE_CONSOLE_URL` |
| model-registry | `NM_MODEL_REGISTRY_URL`, `NM_CUSTOMER_DB_HOST`, `NM_OBJECT_STORE_URL` |

**变量类型**：
- `*_URL`：完整 HTTP 地址，如 `http://10.0.1.5:8080`
- `*_HOST`：仅 IP/主机名，如 `10.0.1.5`

---

## 七、全链路说明（21 个 Flag）

### 7.0 链路依赖总图

```
A1-A3 (外网发现) → tenant_001
  ↓
B1-B2 (DMZ 利用) → worker token, Redis 地址, AI Console URL
  ↓
B3 (SSRF) → /internal/metadata, /api/v1/knowledge-bases (C1)
  ↓
D3 (命令注入) → RCE on document-worker ←← 核心枢纽（14 个 Flag 的唯一入口）
  ├──→ 读 service-account.json → jwt_secret_candidate → 伪造 JWT
  │     ↓
  │   C2 (JWT) → /api/v1/admin/audit/export → Git URL + Object Store URL
  │     ↓
  │   C3 (GraphQL) → lowPrivAccessKey + lowPrivSecretKey
  │     ↓
  │   D2 (对象存储) → tenant-summary-2026.csv → DB 表名线索
  │     ↓
  │   D1 (Redis) → redis-cli 直连 → 队列任务结果
  ├──→ git clone git-service → E1 (.env.example.old) → CI_RUNNER_URL
  │     ↓
  │   E2 (CI 变量) → 高权限 key + Vault bootstrap token
  │     ↓
  │   E3 (CI 注入) → vault-credentials.json → bootstrap token
  │     ↓
  │   G1 (Vault) → model-registry secret → G1 Flag + G2 admin token
  │     ↓                                        ↓ (bootstrap 不能读 db-credentials)
  │   G2 (模型仓库) → recommendation-v4-private manifest
  │     ↓
  │   supply_chain_audit → audit-2026-013
  │     ↓
  │   D2 (训练日志) → Supply Chain Audit Final 段落 → id=13
  │     ↓
  │   F1/F2 (DB 只读 + 提权) → admin 凭据
  │     ↓
  │   F3 (核心数据) → regulated_model_training_records id=6 → F3 Flag
  │     ↓
  │   G3 (终局) → regulated_model_training_records id=13 → G3 Flag
  └──→ psql customer-db → F1/F2/F3/G3
```

### 7.1 阶段 A：外网发现与公开信息（A1-A3）

| ID | 分值 | 服务 | 漏洞类型 |
|----|------|------|----------|
| A1 | 50 | edge-gateway | 构建信息泄露 |
| A2 | 80 | portal-web | 隐藏目录 |
| A3 | 120 | portal-web | Source Map 泄露 |

#### A1: 构建信息泄露

- **入口**：edge-gateway 8080 端口（镜像站）
- **路径**：扫描发现 8080 → 访问镜像站 → 页脚发现 `/status/build-info` 链接 → 读取 JSON
- **Flag 位置**：`/app/build-info.json` 的 `metadata.flag` 字段
- **代码位置**：`edge-gateway/entrypoint.sh` 第 12-53 行（生成 build-info.json）
- **关键 payload**：`GET http://target:8080/status/build-info`

#### A2: 隐藏目录

- **入口**：edge-gateway 80 端口（企业官网）
- **路径**：访问 `/robots.txt` → 发现 `Disallow: /resources/archive/` → 浏览目录 → 打开 `nebulamind-whitepaper-v1.html` → 查看源码 HTML 注释
- **Flag 位置**：`portal-web/static/resources/archive/nebulamind-whitepaper-v1.html` 的 HTML 注释中
- **代码位置**：`portal-web/static/robots.txt` + `portal-web/entrypoint.sh`（sed 替换 `__NM_FLAG_A2__`）

#### A3: Source Map 泄露

- **入口**：edge-gateway 80 端口
- **路径**：查看页面源码 → 发现 `app.js` 引用 → 发现 `//# sourceMappingURL=app.js.map` → 请求 `app.js.map` → 解析 JSON 的 `sourcesContent` 字段
- **Flag 位置**：`portal-web/static/js/app.js.map` 的 `sourcesContent` 数组中
- **关键线索**：同时泄露 `tenant_001`（C1 IDOR 入口）
- **代码位置**：`portal-web/entrypoint.sh`（sed 替换 `__NM_FLAG_A3__`）

### 7.2 阶段 B：DMZ 服务利用（B1-B3）

| ID | 分值 | 服务 | 漏洞类型 |
|----|------|------|----------|
| B1 | 150 | support-upload | MIME 校验绕过 |
| B2 | 180 | support-upload | 路径穿越 |
| B3 | 220 | document-worker | SSRF |

#### B1: 文件上传 MIME 绕过

- **入口**：`http://target/support/`（经 edge-gateway 代理到 support-upload）
- **路径**：上传文件，文件名 `.phar.jpg`，MIME 类型设为 `image/jpeg` → 后端仅检查 MIME 不检查扩展名 → 上传成功 → 获取 taskId → `GET /api/tasks/{taskId}` 查看解析日志
- **Flag 位置**：解析日志中的 "Diagnostic token" 字段（从 `/app/config/b1_flag.txt` 读取）
- **代码位置**：`support-upload/app.py` 上传端点 + `read_b1_flag()` 函数
- **关键 payload**：
  ```
  POST /api/upload
  Content-Type: multipart/form-data
  文件名: test.phar.jpg
  MIME: image/jpeg
  ```

#### B2: 路径穿越

- **入口**：support-upload `/download` 端点
- **路径**：`GET /download?file=../config/worker.yml` → `os.path.normpath` 误用导致穿越 → 读取 worker.yml
- **Flag 位置**：`/app/config/worker.yml` 的 `diagnostic.test_flag` 字段
- **关键线索**：同时泄露 worker token、Redis 地址 (`NM_CACHE_BROKER_HOST:6379`)、AI Console URL
- **代码位置**：`support-upload/app.py` `/download` 端点
- **关键 payload**：`GET /download?file=../config/worker.yml`

#### B3: SSRF

- **入口**：support-upload `/api/parse-url` 端点（转发到 document-worker `/api/parse`）
- **路径**：从 B2 worker.yml 获得 `NM_AI_CONSOLE_API_URL` → POST `/api/parse-url` 请求解析该内部 URL → document-worker fetch 该 URL → 响应 metadata 中包含 B3 Flag
- **Flag 位置**：document-worker `/api/parse` 响应的 `metadata.internal_trace.internal_flag` 字段
- **前提**：需要 B2 获得 worker token（`/api/queue/stats` 需要，但 `/api/parse` 不需要）
- **代码位置**：`support-upload/app.py` `/api/parse-url` + `document-worker/app.py` `/api/parse` + `build_trace_metadata()` + `read_b3_flag()`
- **关键 payload**：
  ```json
  POST /api/parse-url
  {"url": "http://<NM_AI_CONSOLE_API_URL>/internal/metadata"}
  ```

### 7.3 阶段 C：业务后台与身份越权（C1-C3）

| ID | 分值 | 服务 | 漏洞类型 |
|----|------|------|----------|
| C1 | 180 | ai-console-api | IDOR |
| C2 | 240 | ai-console-api | JWT 弱密钥 |
| C3 | 260 | ai-console-api | GraphQL introspection |

> **关键依赖**：C2/C3 需要 JWT secret，而 JWT secret 只能通过 D3 RCE 读取 `service-account.json` 获得。因此 **C2/C3 依赖 D3**。

#### C1: IDOR

- **入口**：ai-console-api `/api/v1/knowledge-bases` 端点
- **路径**：从 A3 sourcemap 获得 `tenant_001` → `GET /api/v1/knowledge-bases?tenantId=tenant_001` → 枚举知识库 → id=17 废弃知识库的 description 含 Flag
- **Flag 位置**：ai-console-api 内存中 KNOWLEDGE_BASES 列表 id=17 的 description
- **无需鉴权**：该端点不验证 JWT
- **代码位置**：`ai-console-api/app.py` knowledge-bases 端点 + `build_knowledge_bases()`
- **也可通过 B3 SSRF 获取**：`POST /api/parse-url {"url": "http://<ai-console-api>/api/v1/knowledge-bases?tenantId=tenant_001"}`

#### C2: JWT 弱密钥

- **前提**：D3 RCE 读取 `/opt/nebulamind/service-account.json` → 获得 `jwt_secret_candidate: "nebulamind-dev-secret-2026"`
- **路径**：用该密钥伪造 `role=operator` 的 JWT → `GET /api/v1/admin/audit/export` → 审计日志中含 Flag
- **Flag 位置**：审计日志中 `git.sync` 事件的 metadata 字段
- **关键线索**：审计日志同时返回平台注入的 `internalServices.gitService`（Git URL）和 `internalServices.objectStore`（对象存储 URL）
- **代码位置**：`ai-console-api/app.py` `create_jwt()` / `verify_jwt()` / `audit_export()` 端点
- **JWT payload**：
  ```json
  {"sub": "attacker", "role": "operator", "tenant": "tenant_001", "iss": "nebulamind-console-api", "aud": "nebulamind-internal"}
  ```

#### C3: GraphQL introspection

- **前提**：同 C2（需要 operator JWT）
- **路径**：`POST /graphql` 查询 introspection → 发现 `integrationSecrets(masked:false)` → 用 operator JWT 查询 → 返回对象存储 bucket、Git URL、低权限 access key/secret key 和 Flag
- **Flag 位置**：integration secrets 的 `flag` 字段
- **关键线索**：同时返回 `lowPrivAccessKey`、`lowPrivSecretKey`（D2 入口）、`gitServiceUrl`（E1 入口）、`objectStoreBucket`
- **代码位置**：`ai-console-api/app.py` GraphQL schema + `build_integration_secrets()` + `seed_data.py` INTEGRATION_SECRETS

### 7.4 阶段 D：对象存储、缓存与异步任务（D1-D3）

| ID | 分值 | 服务 | 漏洞类型 |
|----|------|------|----------|
| D1 | 220 | cache-broker | 未鉴权 Redis |
| D2 | 240 | object-store | Bucket 权限错误 |
| D3 | 320 | document-worker | 命令注入 |

> **D3 是核心枢纽**：D3 RCE 是通往 14 个后续 Flag 的唯一非循环入口。

#### D1: 未鉴权 Redis

- **前提**：D3 RCE 后在 document-worker 容器内执行 `redis-cli`
- **路径**：从 B2 worker.yml 获得 `NM_CACHE_BROKER_HOST:6379` → `redis-cli -h <host> -p 6379`（无密码）→ `KEYS task:result:*` → `GET task:result:task_003` → JSON 中 metadata.debug.envSnapshot.INTERNAL_QUEUE_TOKEN 含 Flag
- **Flag 位置**：Redis key `task:result:task_003` 的 JSON metadata 中
- **代码位置**：`cache-broker/init.lua`（种子数据注入）
- **执行方式**：D3 RCE: `$(redis-cli -h <host> -p 6379 GET task:result:task_003)`

#### D2: Bucket 权限错误

- **前提**：C3 GraphQL 获得 `lowPrivAccessKey` + `lowPrivSecretKey`
- **路径**：用低权限 key 配置 mc/aws-cli → 列出 `public-model-artifacts` bucket → 下载 `exports/tenant-summary-2026.csv` → 第 110 行含 Flag
- **Flag 位置**：`object-store/seed/tenant-summary-2026.csv` 第 110 行
- **关键线索**：CSV 同时含数据库表名线索（F3 入口）
- **代码位置**：`object-store/seed/tenant-summary-2026.csv` + `object-store/init-buckets.sh`
- **凭据**：`nm-low-priv-key` / `nm-low-priv-secret-2026`（从 C3 获得）

#### D3: 命令注入 ★核心枢纽

- **入口**：support-upload `/api/parse-url` → document-worker `/api/parse`
- **路径**：POST `/api/parse-url` 传入 `profile` 参数 → support-upload 转发给 document-worker → document-worker 执行 `convert --profile {profile}` (shell=True) → profile 过滤仅屏蔽 `& ; |`，不屏蔽 `$()` → 命令注入
- **Flag 位置**：`/opt/nebulamind/worker.flag` 文件
- **代码位置**：`support-upload/app.py` `/api/parse-url`（profile 转发）+ `document-worker/app.py` `sanitize_profile()` / `run_convert()`
- **关键 payload**：
  ```json
  POST /api/parse-url
  {"url": "http://example.com", "profile": "$(cat /opt/nebulamind/worker.flag)"}
  ```
- **注入限制**：非 root 用户、无 Docker socket、无特权、仅在容器内执行
- **沙箱**：`subprocess.run` 的 env 限制为 `PATH=/usr/local/bin:/usr/bin:/bin`，但容器内有 curl/redis-cli/psql/git 等工具

### 7.5 阶段 E：Git 与 CI/CD（E1-E3）

| ID | 分值 | 服务 | 漏洞类型 |
|----|------|------|----------|
| E1 | 180 | git-service | Git 历史泄露 |
| E2 | 260 | ci-runner | CI 变量泄露 |
| E3 | 380 | ci-runner | 构建脚本注入 |

#### E1: Git 历史泄露

- **前提**：C2 审计日志获得 `NM_GIT_SERVICE_URL`；D3 RCE 容器内可执行 `git clone`
- **路径**：`git clone <NM_GIT_SERVICE_URL>/nebulamind/console-api.git` → `git log --all` → 发现早期提交删除了 `.env.example.old` → `git show <sha>:.env.example.old` → 含 Flag
- **Flag 位置**：git 历史中 console-api 仓库的 `.env.example.old` 文件
- **关键线索**：同时泄露 `CI_RUNNER_URL`（E2/E3 入口）、`DATABASE_URL`（F1 入口）
- **代码位置**：`git-service/entrypoint.sh` 第 130-160 行（创建 .env.example.old 并在后续提交中删除）

#### E2: CI 变量泄露

- **前提**：E1 获得 CI_RUNNER_URL；D3 RCE 容器内可 curl ci-runner
- **路径**：`GET <CI_RUNNER_URL>/api/projects/nebulamind-console-api/variables` → masked 变量返回明文 → 含 Flag
- **Flag 位置**：ci-runner 内存中项目变量列表
- **关键线索**：同时泄露 `OBJECT_STORE_ADMIN_KEY`（D2 高权限）、`VAULT_BOOTSTRAP_TOKEN`（G1 入口）
- **代码位置**：`ci-runner/app.py` variables 端点
- **认证**：ci-runner 使用弱认证（前缀匹配 `nm_ci_` 或 `glpat-`）

#### E3: 构建脚本注入

- **前提**：E2 获得认证 token；D3 RCE 容器内可 curl ci-runner
- **路径**：`POST <CI_RUNNER_URL>/api/projects/{id}/trigger` 注入变量 `NM_BUILD_ARGS: "$(cat /opt/nebulamind/ci.flag)"` → 构建脚本 shell=True 执行变量替换 → Flag 出现在构建日志中
- **Flag 位置**：`/opt/nebulamind/ci.flag` 文件（ci-runner 容器内）
- **关键线索**：同时可读取 `/opt/nebulamind/vault-credentials.json`（G1 入口）
- **代码位置**：`ci-runner/app.py` trigger 端点 + `ci-runner/entrypoint.sh`（创建 ci.flag 和 vault-credentials.json）

### 7.6 阶段 F：数据库与核心数据（F1-F3）

| ID | 分值 | 服务 | 漏洞类型 |
|----|------|------|----------|
| F1 | 260 | customer-db | 凭据复用 |
| F2 | 360 | customer-db | SECURITY DEFINER 提权 |
| F3 | 420 | customer-db | 链路终点数据访问 |

#### F1: 凭据复用

- **前提**：从 E2 或 D2 获得只读凭据 `readonly:readonly_password_2026`；D3 RCE 容器内可 psql
- **路径**：`psql -h <NM_CUSTOMER_DB_HOST> -U readonly -d nebulamind` → `SELECT * FROM security_findings` → id=7 的 finding_details 含 Flag
- **Flag 位置**：`security_findings` 表 id=7 的 finding_details
- **代码位置**：`customer-db/init/02-seed-data.sql` + `04-permissions.sql`

#### F2: SECURITY DEFINER 提权

- **前提**：F1 的 readonly 连接
- **路径**：`SELECT * FROM export_internal_data('internal_exports')` → 函数以 postgres（SECURITY DEFINER）身份执行 → 返回 internal_exports 全表数据（readonly 无权直接 SELECT）→ 含 Flag
- **Flag 位置**：`internal_exports` 表的 data_payload 字段
- **代码位置**：`customer-db/init/03-functions.sql`（export_internal_data 函数）+ `04-permissions.sql`（GRANT EXECUTE TO readonly）

#### F3: 链路终点数据访问

- **前提**：从 E2 CI 变量或 G1 Vault 获得 admin 密码；D3 RCE 容器内可 psql
- **路径**：`psql -h <host> -U admin -d nebulamind` → `SELECT compliance_audit FROM regulated_model_training_records WHERE id=6` → 含 Flag
- **Flag 位置**：`regulated_model_training_records` 表 id=6 的 compliance_audit
- **admin 密码来源**：E2 CI 变量中的 `NM_DB_ADMIN_PASSWORD`，或 G1 Vault 的 `nebulamind/db-credentials` secret（但 bootstrap token 不能读此 secret，需专用 token）
- **代码位置**：`customer-db/init/02-seed-data.sql` + `04-permissions.sql`

### 7.7 阶段 G：密钥服务与模型仓库终局（G1-G3）

| ID | 分值 | 服务 | 漏洞类型 |
|----|------|------|----------|
| G1 | 360 | secrets-vault | Bootstrap Token 滥用 |
| G2 | 420 | model-registry | 越权下载 |
| G3 | 500 | model-registry + object-store + customer-db | 多系统关联 |

#### G1: Bootstrap Token 滥用

- **前提**：E3 RCE 读取 `/opt/nebulamind/vault-credentials.json` 获得 `bootstrap_token: "s.bootstrap-nebulamind-2026"`；D3 RCE 容器内可 curl secrets-vault
- **路径**：`GET <NM_SECRETS_VAULT_URL>/v1/secret/data/nebulamind/model-registry` (Header: `X-Vault-Token: s.bootstrap-nebulamind-2026`) → 返回 Flag 和 `model_registry_admin_token`
- **Flag 位置**：secrets-vault 的 `secrets.json` 中 model-registry secret 的 flag 字段
- **关键线索**：同时返回 `model_registry_admin_token: "nm-model-admin-token-2026"`（G2 入口）
- **防短路**：bootstrap token 不能读 `db-credentials`、`object-store`、`jwt-secret`（RESTRICTED_PATHS）
- **代码位置**：`secrets-vault/app.py` POLICY_PATHS + RESTRICTED_PATHS + `secrets-vault/seed/secrets.json`

#### G2: 越权下载

- **前提**：G1 获得 `nm-model-admin-token-2026`；D3 RCE 容器内可 curl model-registry
- **路径**：`GET <NM_MODEL_REGISTRY_URL>/api/v1/models/recommendation-v4-private/versions/v4/manifest` (Header: `Authorization: Bearer nm-model-admin-token-2026`) → manifest 中含 Flag
- **Flag 位置**：`model-registry/seed/models.json` 中 recommendation-v4-private v4 manifest 的 flag 字段
- **关键线索**：manifest 的 compliance 字段包含 `supply_chain_audit.audit_id`（G3 入口）
- **代码位置**：`model-registry/app.py` + `model-registry/seed/models.json`

#### G3: 多系统关联（终局）

- **前提**：G2 manifest + D2 object-store 训练日志 + F3 admin DB 访问
- **路径**（3 源关联）：
  1. G2 manifest 的 `compliance.supply_chain_audit.audit_id` = `audit-2026-013`，`audit_record_id` = 13
  2. D2 object-store 训练日志 `recommendation-v4-private-train.log` 末尾 "Supply Chain Audit (Final)" 段落，引用 `audit-2026-013` 和 `regulated_model_training_records?id=13`
  3. F3 admin 连接 customer-db → `SELECT compliance_audit FROM regulated_model_training_records WHERE id=13` → G3 Flag
- **Flag 位置**：`customer-db/init/02-seed-data.sql` 中 id=13 记录的 compliance_audit（占位符 `__NM_FLAG_G3__`）
- **代码位置**：`model-registry/seed/models.json`（supply_chain_audit 字段）+ `object-store/seed/recommendation-v4-private-train.log`（审计段落）+ `customer-db/init/02-seed-data.sql`（id=13 记录）+ `customer-db/entrypoint.sh`（G3 Flag 注入）
- **设计要点**：G3 与 F3 同表但不同行（id=13 vs id=6），需串联 3 个系统的线索才能定位

---

## 八、跨服务凭据链路

```
A3 (portal-web sourcemap)
  └─→ tenant_001 (C1 IDOR 入口)

B2 (support-upload worker.yml)
  └─→ worker token: nm_worker_token_8f3a9b2e5c1d4a6f
  └─→ Redis 地址: NM_CACHE_BROKER_HOST:6379 (D1 入口)
  └─→ AI Console URL: NM_AI_CONSOLE_API_URL (B3 SSRF 目标)

D3 (document-worker RCE)
  └─→ /opt/nebulamind/service-account.json
      └─→ jwt_secret_candidate: "nebulamind-dev-secret-2026" (C2 入口)
      └─→ console_api_url + endpoints (C2/C3 目标)

C2 (ai-console-api audit export)
  └─→ NM_GIT_SERVICE_URL (E1 入口)
  └─→ NM_OBJECT_STORE_URL (D2 入口)

C3 (GraphQL integrationSecrets)
  └─→ lowPrivAccessKey + lowPrivSecretKey (D2 入口)
  └─→ objectStoreBucket: public-model-artifacts
  └─→ gitServiceUrl (E1 入口)

E1 (git-service .env.example.old)
  └─→ CI_RUNNER_URL (E2/E3 入口)
  └─→ DATABASE_URL (F1 入口)

E2 (ci-runner variables)
  └─→ VAULT_BOOTSTRAP_TOKEN: s.bootstrap-nebulamind-2026 (G1 入口)
  └─→ OBJECT_STORE_ADMIN_KEY (D2 高权限)
  └─→ NM_DB_ADMIN_PASSWORD (F3 入口)

E3 (ci-runner exec)
  └─→ /opt/nebulamind/vault-credentials.json
      └─→ bootstrap_token: s.bootstrap-nebulamind-2026 (G1 入口)

G1 (secrets-vault model-registry secret)
  └─→ model_registry_admin_token: nm-model-admin-token-2026 (G2 入口)
  └─→ G1 Flag

G2 (model-registry manifest)
  └─→ supply_chain_audit.audit_id: audit-2026-013 (G3 入口)
  └─→ supply_chain_audit.audit_record_id: 13 (G3 入口)
```

---

## 九、关键代码位置索引

### 9.1 漏洞代码位置

| Flag | 漏洞代码文件 | 行号范围 | 函数/端点 | 漏洞描述 |
|------|------------|----------|----------|----------|
| A1 | `edge-gateway/entrypoint.sh` | 12-53 | build-info.json 生成 | 构建信息暴露在 8080 端口 |
| A2 | `portal-web/static/robots.txt` | — | — | robots.txt 泄露隐藏目录 |
| A3 | `portal-web/static/js/app.js.map` | — | — | Source Map 未清理 |
| B1 | `support-upload/app.py` | 上传端点 | `/api/upload` | 仅检查 MIME 不检查扩展名 |
| B2 | `support-upload/app.py` | /download 端点 | `/download` | `os.path.normpath` 误用导致路径穿越 |
| B3 | `document-worker/app.py` | 192-274 | `fetch_url()` + `build_trace_metadata()` | SSRF：抓取内部 URL 并泄露 trace metadata |
| C1 | `ai-console-api/app.py` | knowledge-bases 端点 | `/api/v1/knowledge-bases` | tenantId 参数无鉴权校验 |
| C2 | `ai-console-api/app.py` | 74-76, 189-200 | `JWT_SECRET` + `verify_jwt()` | JWT 密钥为弱默认值 `nebulamind-dev-secret-2026` |
| C3 | `ai-console-api/app.py` | GraphQL 端点 | `/graphql` | introspection 未关闭 + `masked:false` 泄露密钥 |
| D1 | `cache-broker/redis.conf` | — | — | Redis 无密码 |
| D2 | `object-store/init-buckets.sh` | — | bucket 策略 | 低权限用户可列出 public-model-artifacts |
| D3 | `document-worker/app.py` | 324-339 | `sanitize_profile()` + `run_convert()` | profile 仅屏蔽 `& ; |`，不屏蔽 `$()` |
| E1 | `git-service/entrypoint.sh` | 130-160 | git 提交历史 | .env.example.old 在历史中未清理 |
| E2 | `ci-runner/app.py` | variables 端点 | `/api/projects/*/variables` | masked 变量返回明文 |
| E3 | `ci-runner/app.py` | trigger 端点 | `/api/projects/*/trigger` | 构建变量注入 shell=True |
| F1 | `customer-db/init/04-permissions.sql` | readonly 角色 | — | readonly 凭据弱密码 |
| F2 | `customer-db/init/03-functions.sql` | 15-36 | `export_internal_data()` | SECURITY DEFINER + GRANT EXECUTE TO readonly |
| F3 | `customer-db/init/04-permissions.sql` | admin 角色 | — | admin 可访问 regulated_model_training_records |
| G1 | `secrets-vault/app.py` | 137-170 | `POLICY_PATHS` + `policy_allows()` | bootstrap token 可读 model-registry secret |
| G2 | `model-registry/app.py` | manifest 端点 | `/api/v1/models/*/manifest` | admin token 可下载私有模型 manifest |
| G3 | 多文件关联 | — | — | 需串联 manifest + 训练日志 + DB 记录 |

### 9.2 Flag 注入代码位置

| Flag | entrypoint.sh | 注入机制 | app.py 读取 |
|------|---------------|----------|-------------|
| A1 | edge-gateway/entrypoint.sh:12 | `get_flag` → 写入 build-info.json | — |
| A2 | portal-web/entrypoint.sh | `nm_replace_file` sed 替换 | — |
| A3 | portal-web/entrypoint.sh | `nm_replace_file` sed 替换 | — |
| B1 | support-upload/entrypoint.sh:17 | `get_flag` → `write_flag_file` | `read_b1_flag()` 读文件 |
| B2 | support-upload/entrypoint.sh:22 | Python sed 替换 worker.yml | — |
| B3 | document-worker/entrypoint.sh:21 | `export GZCTF_FLAG_WORKER_SSRF_METADATA` | `get_flag('FLAG_WORKER_SSRF_METADATA')` |
| C1 | ai-console-api/entrypoint.sh:19 | `export GZCTF_FLAG_API_TENANT_IDOR` | `get_flag('FLAG_API_TENANT_IDOR')` |
| C2 | ai-console-api/entrypoint.sh:24 | `export GZCTF_FLAG_API_JWT_ROLE` | `get_flag('FLAG_API_JWT_ROLE')` |
| C3 | ai-console-api/entrypoint.sh:29 | `export GZCTF_FLAG_API_GRAPHQL_AUDIT` | `get_flag('FLAG_API_GRAPHQL_AUDIT')` |
| D1 | cache-broker/entrypoint.sh:9 | `get_flag` → 传参给 init.lua | init.lua 写入 Redis |
| D2 | object-store/entrypoint.sh:13 | `get_flag` → sed 替换 CSV | — |
| D3 | document-worker/entrypoint.sh:26 | `get_flag` → 写入 worker.flag | — |
| E1 | git-service/entrypoint.sh:33 | `get_flag` → git 提交到历史 | — |
| E2 | ci-runner/entrypoint.sh:25 | `export GZCTF_FLAG_CI_VARIABLE_LEAK` | `get_flag('FLAG_CI_VARIABLE_LEAK')` |
| E3 | ci-runner/entrypoint.sh:32 | `get_flag` → 写入 ci.flag | — |
| F1 | customer-db/entrypoint.sh:26 | `get_flag` → sed 替换 SQL | — |
| F2 | customer-db/entrypoint.sh:27 | `get_flag` → sed 替换 SQL | — |
| F3 | customer-db/entrypoint.sh:28 | `get_flag` → sed 替换 SQL | — |
| G3 | customer-db/entrypoint.sh:29 | `get_flag` → sed 替换 SQL | — |
| G1 | secrets-vault/entrypoint.sh:27 | `get_flag` → sed 替换 secrets.json | app.py 也从环境变量读取 |
| G2 | model-registry/entrypoint.sh:37 | `get_flag` → sed 替换 models.json | app.py 也从环境变量读取 |

---

## 十、修复记录摘要

截至 2026-07-03，共完成 19 个修复项，修改 24 个文件。修复后链路闭环率从 33% 提升至 100%（21/21 Flag 可达）。

### 10.1 修复总览

| 优先级 | 数量 | 影响 |
|--------|------|------|
| P0（Critical） | 4 | 解锁链路核心阻断点 |
| P1（High） | 4 | 修复链路完整性 |
| P2（Medium） | 6 | 提升安全姿态与拟真度 |
| Medium（补充） | 3 | 修复质量问题 |
| P0-3 补充 | 2 | 修复遗漏的双前缀 bug |

### 10.2 关键修复项

| ID | 修复内容 | 影响 |
|----|----------|------|
| P0-1 | support-upload 转发 profile 参数 | 解锁 D3 全链路（14 个 Flag） |
| P0-2 | 注入 G3 Flag（3 源关联） | 解锁 G3 终局题（500 分） |
| P0-3 | 修复 6 个文件中的双 FLAG_ 前缀 bug | 防止 7 个 Flag 变占位符 |
| P0-4 | 删除 JWT 密钥明文日志 | 防止 C2 候选密钥从日志泄露 |
| P1-5 | C3 GraphQL 添加 lowPrivSecretKey | 修复 C3→D2 链路 |
| P1-6 | git-service 注入 NM_CI_RUNNER_URL | 修复 E1→E2 断点 |
| P1-7 | Vault POLICY_PATHS 重构 | 防止 G3 短路 |

### 10.3 修复详情

完整修复记录见 `docs/nebulamind-review-findings.md` §十（修复执行记录）。

---

## 十一、安全边界

### 11.1 容器安全

- ✅ 无特权容器（所有服务非 root 运行，edge-gateway 除外需绑定 80 端口）
- ✅ 无宿主 Docker socket 挂载
- ✅ 无宿主敏感目录挂载
- ✅ CI/RCE 类题目（D3、E3）限制在容器内执行，不逃逸宿主机
- ✅ Flag 不出现在镜像历史层（通过环境变量注入）

### 11.2 RCE 沙箱限制

D3 命令注入的沙箱环境：

| 限制项 | 值 |
|--------|-----|
| 运行用户 | nmapp（非 root） |
| Docker socket | 不可用 |
| 环境变量 | 仅 `PATH=/usr/local/bin:/usr/bin:/bin`, `HOME=/tmp` |
| 可用工具 | curl, redis-cli, psql, git, python3, sh |
| 网络访问 | 取决于平台网络隔离策略 |
| 超时 | 15 秒 |

---

## 十二、构建与部署注意事项

### 12.1 构建注意事项

1. **构建上下文**：必须在 `scenarios/nebulamind/` 目录下构建，不是服务子目录
2. **多阶段构建**：object-store 使用多阶段构建（builder 阶段下载 mc + curl-static）
3. **行尾处理**：所有 entrypoint.sh 在 Dockerfile 中通过 `sed -i 's/\r$//'` 处理 Windows 行尾
4. **权限设置**：所有服务创建 `nmapp` 用户并切换（`USER nmapp`）

### 12.2 部署注意事项

1. **NM_* 变量**：平台必须注入所有 `NM_*` 变量，缺失则服务启动失败（fail fast）
2. **Flag 变量**：平台注入 `GZCTF_FLAG_*` 环境变量
3. **admin 密码**：customer-db 的 admin 密码通过 `NM_DB_ADMIN_PASSWORD` 注入（默认 `nm_admin_dev_2026`）
4. **MinIO 凭据**：object-store 的 root 凭据通过 `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` 注入
5. **初始化顺序**：object-store 和 cache-broker 需要后台启动 → 初始化种子数据 → 前台运行
6. **幂等性**：所有 entrypoint.sh 支持重复执行（object-store 有 INIT_MARKER 检查）

### 12.3 健康检查

| 服务 | 健康检查命令 |
|------|-------------|
| edge-gateway | `curl -fsS http://127.0.0.1/healthz` |
| portal-web | `curl -fsS http://127.0.0.1:8080/healthz` |
| support-upload | `curl -fsS http://127.0.0.1:8080/healthz` |
| document-worker | `curl -fsS http://127.0.0.1:8080/healthz` |
| ai-console-api | `curl -fsS http://127.0.0.1:8080/healthz` |
| cache-broker | `redis-cli ping \| grep -q PONG` |
| git-service | `curl -fsS http://127.0.0.1:3000/healthz` |
| ci-runner | `curl -fsS http://127.0.0.1:8080/healthz` |
| customer-db | `pg_isready -U postgres` |
| object-store | `curl -fsS http://127.0.0.1:9000/minio/health/live` |
| secrets-vault | `curl -fsS http://127.0.0.1:8200/healthz` |
| model-registry | `curl -fsS http://127.0.0.1:8080/healthz` |

---

## 十三、相关文档索引

| 文档 | 路径 | 内容 |
|------|------|------|
| 场景 README | `scenarios/nebulamind/README.md` | 服务清单、Flag 清单、构建说明、部署约束 |
| 设计文档 | `docs/pentest-ai-enterprise-scenario-design.md` | 原始设计规格（878 行） |
| 审查发现 | `docs/nebulamind-review-findings.md` | 全面质量审查记录 + 修复执行记录 |
| 交接文档 | `docs/nebulamind-handoff.md` | 本文档 |
| customer-db README | `scenarios/nebulamind/customer-db/README.md` | 数据库服务说明 |
| object-store README | `scenarios/nebulamind/object-store/README.md` | 对象存储服务说明 |
