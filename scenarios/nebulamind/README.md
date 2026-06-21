# NebulaMind AI Corp 内网安全评估 - Docker 场景实现

> 基于 [pentest-ai-enterprise-scenario-design.md](../../docs/pentest-ai-enterprise-scenario-design.md) 设计文档实现的完整企业级渗透靶场场景。
> 包含 12 个 Docker 服务、21 个 Flag、5 个安全域，覆盖外网打点、凭据收集、横向移动、服务利用、数据访问和内网突破完整链路。

## 目录结构

```
scenarios/nebulamind/
├── _shared/                     # 共享资源（所有服务复用）
│   ├── assets/nebulamind.css    # 共享企业品牌样式（深色主题）
│   ├── scripts/flag.sh          # Shell 版 Flag 读取助手
│   ├── scripts/flag.py          # Python 版 Flag 读取助手
│   └── .dockerignore
├── edge-gateway/                # Public - 外部入口网关（nginx）
├── portal-web/                  # DMZ - 企业官网（Python）
├── support-upload/              # DMZ - 客户支持上传中心（Flask）
├── document-worker/             # Business - 文档解析 Worker（Flask）
├── ai-console-api/              # Business - AI 控制台 API（Flask + GraphQL）
├── cache-broker/                # Business - Redis 任务队列
├── git-service/                 # Operations - 内部 Git 服务（Flask + git）
├── ci-runner/                   # Operations - CI 构建节点（Flask）
├── customer-db/                 # Data - PostgreSQL 数据库
├── object-store/                # Data - MinIO 对象存储
├── secrets-vault/               # Data - Vault mock 密钥服务（Flask）
├── model-registry/              # Data - 模型仓库（Flask）
└── README.md                    # 本文档
```

## 服务清单

| 服务 | 安全域 | 端口 | 镜像基础 | 资源 | Flag 数 |
|---|---|---|---|---|---|
| edge-gateway | Public | 80, 8080 | nginx:1.27-alpine | 0.25C/128M | 1 |
| portal-web | DMZ | 8080 | python:3.11-alpine | 0.5C/256M | 2 |
| support-upload | DMZ | 8080 | python:3.11-alpine | 0.5C/256M | 2 |
| document-worker | Business | 8080 | python:3.11-alpine | 0.75C/512M | 2 |
| ai-console-api | Business | 8080 | python:3.11-alpine | 1C/512M | 3 |
| cache-broker | Business | 6379 | redis:7.2-alpine | 0.25C/128M | 1 |
| git-service | Operations | 3000 | python:3.11-alpine | 1C/512M | 1 |
| ci-runner | Operations | 8080 | python:3.11-alpine | 1C/512M | 2 |
| customer-db | Data | 5432 | postgres:16-alpine | 1C/768M | 3 |
| object-store | Data | 9000, 9001 | minio/minio | 0.75C/512M | 1 |
| secrets-vault | Data | 8200 | python:3.11-alpine | 0.5C/256M | 1 |
| model-registry | Data | 8080 | python:3.11-alpine | 0.5C/256M | 2 |

**总计**：12 个服务，21 个 Flag，单队资源约 7.5C/4.5G

## 网络拓扑

```
选手 → edge-gateway (Public)
         ├─→ portal-web (DMZ)
         └─→ support-upload (DMZ) → document-worker (Business)
                                      └─→ ai-console-api (Business)
                                            ├─→ cache-broker (Business)
                                            ├─→ customer-db (Data)
                                            └─→ git-service (Operations) → ci-runner (Operations)
                                                                                  ├─→ secrets-vault (Data)
                                                                                  └─→ object-store (Data)
                                                                                        └─→ model-registry (Data)
```

## Flag 注入机制

所有 Flag 通过平台环境变量注入，镜像内不硬编码：

- `GZCTF_FLAG_<TITLE>`：其中 TITLE 由 score item 的 Title 字段转大写、非字母数字转下划线得到
- `GZCTF_FLAG`：第一个 Flag（兜底）

每个服务的 `entrypoint.sh` 在启动时读取环境变量并注入到对应位置。共享助手：
- Shell：`. /_shared/scripts/flag.sh; get_flag 'FLAG_NAME'`
- Python：`from flag import get_flag; get_flag('FLAG_NAME')`

## Flag 清单与解题路径

### 阶段 A：外网发现与公开信息

| ID | Flag 名 | 分值 | 服务 | 漏洞 | 解题路径 |
|---|---|---|---|---|---|
| A1 | FLAG_PUBLIC_DISCOVERY | 50 | edge-gateway | 端口扫描、构建信息泄露 | 扫描发现 8080 端口 → 访问资源镜像站 → 页脚发现 `/status/build-info` → 读取 JSON 获得 Flag |
| A2 | FLAG_PORTAL_HIDDEN_DOCS | 80 | portal-web | 隐藏目录 | 访问 `/robots.txt` → 发现 `/resources/archive/` → 浏览目录 → 打开 `nebulamind-whitepaper-v1.html` → 查看源码 HTML 注释获得 Flag |
| A3 | FLAG_PORTAL_SOURCEMAP | 120 | portal-web | Source Map 泄露 | 查看页面源码 → 发现 `app.js` → 发现 `sourceMappingURL=app.js.map` → 请求 `app.js.map` → 解析 JSON `sourcesContent` → 找到 Flag 和 `tenant_001` 线索 |

### 阶段 B：DMZ 服务利用

| ID | Flag 名 | 分值 | 服务 | 漏洞 | 解题路径 |
|---|---|---|---|---|---|
| B1 | FLAG_UPLOAD_MIME_BYPASS | 150 | support-upload | MIME 校验绕过 | 访问 `/support/` 上传中心 → 上传 `.phar.jpg`（MIME 伪装为 image/jpeg）→ 获取 taskId → `GET /api/tasks/{taskId}` 查看解析日志获得 Flag |
| B2 | FLAG_UPLOAD_PATH_TRAVERSAL | 180 | support-upload | 路径穿越 | `GET /download?file=../config/worker.yml` → 读取 worker.yml → 获得 Flag、worker token 与平台注入的 Redis/Worker/API 地址线索 |
| B3 | FLAG_WORKER_SSRF_METADATA | 220 | document-worker | SSRF | 通过 B2 获得 worker token → 对平台注入的 `NM_AI_CONSOLE_API_URL` 目标发起 `POST /api/parse-url` SSRF → 响应 metadata 中包含 Flag |

### 阶段 C：业务后台与身份越权

| ID | Flag 名 | 分值 | 服务 | 漏洞 | 解题路径 |
|---|---|---|---|---|---|
| C1 | FLAG_API_TENANT_IDOR | 180 | ai-console-api | IDOR | 从 A3 sourcemap 获得 `tenant_001` → `GET /api/v1/knowledge-bases?tenantId=tenant_001` → 枚举到 id=17 废弃知识库 → description 字段含 Flag |
| C2 | FLAG_API_JWT_ROLE | 240 | ai-console-api | JWT 弱密钥 | 从 document-worker 的 service-account.json 获得 `jwt_secret_candidate` → 用 `nebulamind-dev-secret-2026` 伪造 `role=operator` JWT → `GET /api/v1/admin/audit/export` → 审计日志含 Flag 和 git-service 地址 |
| C3 | FLAG_API_GRAPHQL_AUDIT | 260 | ai-console-api | GraphQL introspection | `POST /graphql` 查询 introspection → 发现 `integrationSecrets(masked:false)` → 用 operator JWT 查询 → 返回对象存储 bucket、Git URL、低权限 key 和 Flag |

### 阶段 D：对象存储、缓存与异步任务

| ID | Flag 名 | 分值 | 服务 | 漏洞 | 解题路径 |
|---|---|---|---|---|---|
| D1 | FLAG_REDIS_QUEUE_INFO | 220 | cache-broker | 未鉴权 Redis | 从 B2 worker.yml 获得平台注入的 `NM_CACHE_BROKER_HOST:6379` → 连接 Redis（无密码）→ `KEYS task:result:*` → `GET task:result:task_003` → metadata 中含 Flag |
| D2 | FLAG_OBJECT_BUCKET_POLICY | 240 | object-store | Bucket 权限错误 | 从 C3 获得低权限 access key → 用 `nm-low-priv-key` 列出 `public-model-artifacts` → 下载 `exports/tenant-summary-2026.csv` → 第 110 行含 Flag 和数据库表名线索 |
| D3 | FLAG_WORKER_COMMAND_INJECTION | 320 | document-worker | 命令注入 | 通过队列注入任务 → `POST /api/parse {profile: "$(cat /opt/nebulamind/worker.flag)"}` → 转换日志中输出 Flag |

### 阶段 E：Git 与 CI/CD

| ID | Flag 名 | 分值 | 服务 | 漏洞 | 解题路径 |
|---|---|---|---|---|---|
| E1 | FLAG_GIT_CONFIG_SECRET | 180 | git-service | Git 历史泄露 | 从 C2 获得平台注入的 `NM_GIT_SERVICE_URL` → `git clone` console-api 仓库 → `git log --all` → `git show <sha>:.env.example.old` → 获得 Flag 和 CI 项目名 |
| E2 | FLAG_CI_VARIABLE_LEAK | 260 | ci-runner | CI 变量泄露 | 从 E1 获得 CI 项目名和 token → `GET /api/projects/nebulamind-console-api/variables` → masked 变量返回明文 → 获得 Flag、OBJECT_STORE_ADMIN_KEY、VAULT_BOOTSTRAP_TOKEN |
| E3 | FLAG_CI_RUNNER_EXEC | 380 | ci-runner | 构建脚本注入 | `POST /api/projects/{id}/trigger` 触发构建 → 注入 `variables: {NM_BUILD_ARGS: "$(cat /opt/nebulamind/ci.flag)"}` → 构建日志输出 Flag |

### 阶段 F：数据库与核心数据

| ID | Flag 名 | 分值 | 服务 | 漏洞 | 解题路径 |
|---|---|---|---|---|---|
| F1 | FLAG_DB_READONLY_CUSTOMERS | 260 | customer-db | 凭据复用 | 从 E2 或 D2 获得只读凭据 `readonly:readonly_password_2026` → 连接 PostgreSQL → `SELECT * FROM security_findings` → id=7 的 finding_details 含 Flag |
| F2 | FLAG_DB_PRIVESC_FUNCTION | 360 | customer-db | SECURITY DEFINER | 用 readonly 连接 → `SELECT * FROM export_internal_data('internal_exports')` → 函数以 postgres 权限执行 → 返回 internal_exports 数据含 Flag |
| F3 | FLAG_DB_CORE_CUSTOMER_DATA | 420 | customer-db | 链路终点数据访问 | 从 E2 获得 admin 密码或从 G1 Vault 获得 → 用 admin 连接 → `SELECT compliance_audit FROM regulated_model_training_records WHERE id=6` → 获得 Flag |

### 阶段 G：密钥服务与模型仓库终局

| ID | Flag 名 | 分值 | 服务 | 漏洞 | 解题路径 |
|---|---|---|---|---|---|
| G1 | FLAG_VAULT_POLICY_BYPASS | 360 | secrets-vault | Bootstrap Token 滥用 | 从 E2 获得 `VAULT_BOOTSTRAP_TOKEN=s.bootstrap-nebulamind-2026` → `GET /v1/secret/data/nebulamind/model-registry` (header: X-Vault-Token) → 返回 Flag 和 `model_registry_admin_token` |
| G2 | FLAG_MODEL_REGISTRY_ADMIN | 420 | model-registry | 越权下载 | 从 G1 获得 `nm-model-admin-token-2026` → `GET /api/v1/models/recommendation-v4-private/versions/v4/manifest` (Authorization: Bearer) → manifest 中含 Flag |
| G3 | FLAG_FINAL_MODEL_SUPPLY_CHAIN | 500 | model-registry + object-store + customer-db | 多系统关联 | 从 G2 manifest 获得 `training_log_path` 和 `audit_id` → 从 object-store 下载训练日志 → 训练日志指向平台注入的 PostgreSQL 目标与 `regulated_model_training_records?id=6` → 查询数据库获得最终 Flag |

**总分**：5110 分（原始）/ 4770 分（上线建议）

## 跨服务凭据链路

以下是解题过程中关键的跨服务凭据传递链路：

```
A3 (portal-web sourcemap)
  └─→ tenant_001 (C1 入口)

B2 (support-upload worker.yml)
  └─→ worker token (document-worker 访问)
  └─→ platform-injected Redis host:6379 (D1 入口)

B3/D3 (document-worker)
  └─→ service-account.json
      └─→ jwt_secret_candidate (C2 入口)

C2 (ai-console-api audit export)
  └─→ platform-injected Git URL (E1 入口)

E1 (git-service .env.example.old)
  └─→ CI_PROJECT=nebulamind-console-api (E2 入口)
  └─→ DATABASE_URL (F1 入口)

E2 (ci-runner variables)
  └─→ OBJECT_STORE_ADMIN_KEY (D2 高权限)
  └─→ VAULT_BOOTSTRAP_TOKEN (G1 入口)
  └─→ FLAG_CI_VARIABLE_LEAK

E3 (ci-runner exec)
  └─→ /opt/nebulamind/vault-credentials.json
      └─→ bootstrap token (G1 入口)

G1 (secrets-vault)
  └─→ model_registry_admin_token (G2 入口)
  └─→ db admin password (F3 入口)

G2 (model-registry manifest)
  └─→ training_log_path (G3 入口)
  └─→ audit_id (G3 入口)
```

## 构建说明

每个服务的 Dockerfile 构建上下文为 `scenarios/nebulamind/` 目录（不是服务子目录），因为需要 COPY `_shared/` 共享资源。

构建示例（在项目根目录执行）：

```bash
# 构建单个服务（以 edge-gateway 为例）
docker build -t nebulamind-edge-gateway:2026.06 -f scenarios/nebulamind/edge-gateway/Dockerfile scenarios/nebulamind/

# 构建所有服务
for svc in edge-gateway portal-web support-upload document-worker ai-console-api cache-broker git-service ci-runner customer-db object-store secrets-vault model-registry; do
    docker build -t nebulamind-${svc}:2026.06 -f scenarios/nebulamind/${svc}/Dockerfile scenarios/nebulamind/
done
```

## 部署约束

### 动态地址硬约束

- 所有跨服务地址由 GZCTF 渗透编排平台注入 `NM_*` 环境变量，不依赖 Docker 服务名、Compose DNS 或固定 IP
- 镜像启动时使用 `_shared/scripts/runtime-env.sh` 校验必需变量；缺少任一跨服务地址时立即 fail fast
- 选手可见线索（worker.yml、source map、Git 历史、manifest、训练日志、Vault secret）必须渲染为平台实际分配的地址
- Flag 通过环境变量注入，不硬编码到镜像层
- 内网地址只在容器间使用，不作为选手直接访问入口

### 平台注入变量矩阵

| 服务 | 必需平台变量 |
|---|---|
| edge-gateway | `NM_PORTAL_WEB_URL`, `NM_SUPPORT_UPLOAD_URL` |
| portal-web | `NM_AI_CONSOLE_API_URL`, `NM_AI_CONSOLE_API_HOST` |
| support-upload | `NM_DOCUMENT_WORKER_URL`, `NM_DOCUMENT_WORKER_HOST`, `NM_CACHE_BROKER_HOST`, `NM_AI_CONSOLE_API_URL` |
| document-worker | `NM_DOCUMENT_WORKER_URL`, `NM_DOCUMENT_WORKER_HOST`, `NM_CACHE_BROKER_HOST`, `NM_AI_CONSOLE_API_URL`, `NM_AI_CONSOLE_API_HOST` |
| ai-console-api | `NM_GIT_SERVICE_URL`, `NM_OBJECT_STORE_URL` |
| cache-broker | `NM_CACHE_BROKER_HOST` |
| git-service | `NM_GIT_SERVICE_URL`, `NM_CUSTOMER_DB_HOST`, `NM_OBJECT_STORE_URL`, `NM_CACHE_BROKER_HOST`, `NM_AI_CONSOLE_API_URL`, `NM_DOCUMENT_WORKER_URL`, `NM_PORTAL_WEB_URL` |
| ci-runner | `NM_GIT_SERVICE_URL`, `NM_CUSTOMER_DB_HOST`, `NM_CACHE_BROKER_HOST`, `NM_SECRETS_VAULT_URL` |
| customer-db | 可选 `NM_DB_ADMIN_PASSWORD`；Flag 仍通过 `GZCTF_FLAG_*` 注入 |
| object-store | `NM_CUSTOMER_DB_HOST`, `NM_MODEL_REGISTRY_URL`, `NM_OBJECT_STORE_URL` |
| secrets-vault | `NM_CUSTOMER_DB_HOST`, `NM_MODEL_REGISTRY_URL`, `NM_CI_RUNNER_URL`, `NM_OBJECT_STORE_URL`, `NM_OBJECT_STORE_CONSOLE_URL` |
| model-registry | `NM_MODEL_REGISTRY_URL`, `NM_CUSTOMER_DB_HOST`, `NM_OBJECT_STORE_URL` |

一键生成拓扑时，平台应先根据多级网段调度为每个服务分配队伍内网地址，再按上表写入 `NM_*`。例如 URL 类变量使用 `http://<平台分配内网地址>:<服务端口>`，Host 类变量只写 `<平台分配内网地址>`。镜像不提供服务名兜底。

### 安全边界

- 无特权容器
- 无宿主 Docker socket 挂载
- 无宿主敏感目录挂载
- CI/RCE 类题目（D3、E3）限制在容器内执行，不逃逸宿主机
- 网络扫描范围由平台网络隔离限制在队伍环境内

### 平台对接

镜像构建后，在 GZCTF 平台注册为 ImageTemplate，然后在渗透编排中创建拓扑节点引用对应模板。平台会：
1. 为每队分配独立 Worker 节点
2. 生成独立 Docker 网络和 IP
3. 注入动态 Flag 环境变量（`GZCTF_FLAG_<TITLE>`）
4. 按“平台注入变量矩阵”注入每个服务所需 `NM_*`
5. 发布入口端口（仅 edge-gateway）

## 健康检查

| 服务 | 健康检查命令 |
|---|---|
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

## 验收清单

### 功能验收
- [x] 每支队伍部署后获得不同 Flag（平台动态注入）
- [x] 每支队伍部署后网络互相隔离（平台网络隔离）
- [x] 选手入口显示 Worker 节点入口地址（平台发布 edge-gateway 端口）
- [x] 选手只通过入口 IP 开始（edge-gateway 80/8080）
- [x] 重置环境后 Flag 与数据库记录一致（entrypoint 幂等）

### 动态地址验收
- [x] Dockerfile/entrypoint/seed 中不存在写死 `10.0.7.`、`10.48.`、`127.0.0.1:5000` 作为业务依赖
- [x] 跨服务通信和选手线索均由平台注入的 `NM_*` 渲染
- [x] 缺失必需 `NM_*` 时镜像启动失败，不回退到 Docker 服务名
- [x] 镜像内部不直接访问主平台服务 IP

### 题目质量验收
- [x] 每个 Flag 都有明确漏洞路径
- [x] 每个漏洞都有业务解释
- [x] 简单题、中等题、高级题分布合理
- [x] 不重复考点：上传、目录、SSRF、JWT、IDOR、GraphQL、对象存储、Git、CI、DB、Vault、模型仓库各有侧重
- [x] 高分题必须依赖前置线索
- [x] 终局题需要多系统关联（G3 串联 model-registry + object-store + customer-db）

### 安全验收
- [x] 无特权容器
- [x] 无宿主 Docker socket
- [x] 无宿主敏感目录挂载
- [x] CI/RCE 类题目不能逃逸容器（D3、E3 限制在容器内）
- [x] Flag 不出现在镜像历史层（通过环境变量注入）

## 设计要点

### 真实感
- 企业官网有首页、产品页、客户案例、新闻、下载中心、登录入口
- AI 控制台有知识库、API Key、审计日志、GraphQL
- 数据库包含 300+ 行真实结构化数据（客户、合同、标注任务、模型版本、API key、审计日志）
- 对象存储包含真实文件（CSV、训练日志、model manifest）
- Git 服务包含 3 个仓库，每个 9 次提交，历史中有真实差异
- CI 服务有项目列表、构建状态、构建日志

### 业务线索串联
- 官网 JS 泄露控制台 API 路径（A3 → C1）
- 上传服务日志泄露 worker 队列名称（B2 → D1）
- 控制台配置泄露 Git 内部地址（C2 → E1）
- Git CI 变量泄露对象存储凭据和 Vault token（E2 → D2/G1）
- 对象存储中模型配置泄露平台分配的数据库目标（D2 → F1）
- Vault 中模型仓库凭据（G1 → G2）
- 模型 manifest 引用训练日志和审计记录（G2 → G3）

## 已知限制

1. **未提供构建脚本和拓扑配置生成**：本次实现仅包含 Dockerfile 和服务文件，镜像构建和拓扑注册由管理员手动完成
2. **未提供 docker-compose.yml**：实际部署由 GZCTF 平台渗透编排模块管理，不使用 compose
3. **未提供 smoke test 脚本**：需管理员手动验证每个 Flag 的预期利用路径
4. **MinIO 版本固定**：使用 `RELEASE.2024-12-18T13-15-44Z`，生产环境应定期更新

## 相关文档

- [场景设计文档](../../docs/pentest-ai-enterprise-scenario-design.md)
- [渗透赛制执行计划](../../docs/pentest-multisegment-execution-plan.md)
- [全面审计报告](../../docs/pentest-full-audit-report.md)
