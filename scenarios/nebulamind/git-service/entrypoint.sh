#!/bin/sh
set -e

# git-service 入口脚本
# 职责：
# 1. 注入 E1 Flag（Git 仓库泄露低权限凭据）
# 2. 初始化 3 个裸仓库（console-api、doc-worker、infra-playbooks）并填充真实历史提交
# 3. console-api 第 3 个提交添加 .env.example.old（含 E1 Flag），后续提交删除该文件
# 4. 启动 Python Flask 服务（Git HTTP + Web 界面）

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all \
    NM_GIT_SERVICE_URL \
    NM_CUSTOMER_DB_HOST \
    NM_OBJECT_STORE_URL \
    NM_CACHE_BROKER_HOST \
    NM_AI_CONSOLE_API_URL \
    NM_DOCUMENT_WORKER_URL \
    NM_PORTAL_WEB_URL \
    NM_CI_RUNNER_URL

READONLY_DATABASE_URL="postgresql://readonly:readonly_password_2026@$NM_CUSTOMER_DB_HOST:5432/nebulamind"
GIT_SERVICE_URL="$NM_GIT_SERVICE_URL"
OBJECT_STORE_ENDPOINT="$NM_OBJECT_STORE_URL"
CACHE_BROKER_URL="redis://$NM_CACHE_BROKER_HOST:6379/2"
AI_CONSOLE_API_URL="$NM_AI_CONSOLE_API_URL"
CI_RUNNER_URL="$NM_CI_RUNNER_URL"

# E1: Git 仓库泄露低权限凭据
# Flag 注入到 console-api 仓库历史中的 .env.example.old 文件
FLAG_E1="$(get_flag 'FLAG_GIT_CONFIG_SECRET' 'flag{e1_git_config_secret_placeholder}')"

REPO_ROOT="/srv/git/nebulamind"
mkdir -p "$REPO_ROOT"

# Git 提交者配置（所有仓库历史使用同一身份，模拟 DevOps 团队）
export GIT_AUTHOR_NAME="NebulaMind DevOps"
export GIT_AUTHOR_EMAIL="devops@nebulamind.ai"
export GIT_COMMITTER_NAME="NebulaMind DevOps"
export GIT_COMMITTER_EMAIL="devops@nebulamind.ai"

# 提交辅助函数：将指定内容写入文件并提交
# 用法: commit_with_date "commit message" "2026-01-10T09:00:00"
commit_with_date() {
    msg="$1"
    date="$2"
    GIT_AUTHOR_DATE="$date" GIT_COMMITTER_DATE="$date" \
        git commit -q -m "$msg"
}

# ===========================================================================
# console-api 仓库（E1 题目目标）
# ===========================================================================
init_console_api() {
    bare="$REPO_ROOT/console-api.git"
    [ -d "$bare" ] && { echo "[git-service] console-api already initialized"; return 0; }

    git init --bare -q "$bare"
    git --git-dir="$bare" config gitweb.description "NebulaMind Console API - Internal admin console backend"
    work=$(mktemp -d)
    cd "$work"
    git init -q -b main

    # --- Commit 1: 初始脚手架 ---
    cp /app/repos/console-api/README.md .
    cp /app/repos/console-api/.gitignore .
    cat > requirements.txt <<'REQ'
flask>=3.0,<4.0
psycopg2-binary>=2.9,<3.0
redis>=5.0,<6.0
PyJWT>=2.8,<3.0
gunicorn>=21.0,<23.0
REQ
    cat > package.json <<'PKG'
{
  "name": "nebulamind-console-api",
  "version": "2026.06.1",
  "description": "NebulaMind Console API - Internal admin console backend",
  "main": "app.py",
  "scripts": {
    "start": "gunicorn -w 4 -b 0.0.0.0:8080 app:app",
    "dev": "python app.py",
    "test": "pytest -v"
  },
  "author": "NebulaMind DevOps <devops@nebulamind.ai>",
  "license": "Proprietary"
}
PKG
    git add -A
    commit_with_date "feat: initial commit - scaffold console-api service" "2026-01-10T09:00:00"

    # --- Commit 2: 文档 ---
    mkdir -p docs
    cat > docs/architecture.md <<'ARCH'
# Console API Architecture

## Components

- **Auth Module**: JWT-based authentication with role-based access control
- **Tenant Manager**: Multi-tenant isolation and resource management
- **Knowledge Base**: Document storage and retrieval with embeddings
- **Audit Logger**: Append-only audit trail for all admin operations
- **GraphQL Gateway**: Unified query interface for console data

## Network

- Listens on port 8080
- Connects to PostgreSQL (__NM_CUSTOMER_DB_HOST__:5432) and Redis (__NM_CACHE_BROKER_HOST__:6379)
- Internal endpoints accessible from Business zone only

## Security Notes

- JWT secret must be rotated quarterly
- Tenant isolation enforced at query level
- Audit logs exported via /api/v1/admin/audit/export
ARCH
    nm_replace_file docs/architecture.md "__NM_CUSTOMER_DB_HOST__" "$NM_CUSTOMER_DB_HOST"
    nm_replace_file docs/architecture.md "__NM_CACHE_BROKER_HOST__" "$NM_CACHE_BROKER_HOST"
    git add -A
    commit_with_date "docs: add API documentation and architecture notes" "2026-01-12T14:30:00"

    # --- Commit 3: 添加 .env.example.old（E1 FLAG 在此注入）---
    cat > .env.example.old <<ENVEOF
# NebulaMind Console API - Development Environment (DEPRECATED)
# This file is kept for reference only. Use vault for secrets.
# Generated: 2026-01-15
# WARNING: Contains hardcoded dev credentials - do not use in production

# Database
DATABASE_URL=$READONLY_DATABASE_URL
DATABASE_POOL_SIZE=10
DATABASE_TIMEOUT=30

# JWT Authentication
JWT_SECRET=nebulamind-dev-secret-2026
JWT_ISSUER=nebulamind-console-api
JWT_ALGORITHM=HS256
JWT_EXPIRY=3600

# Object Storage
OBJECT_STORE_ENDPOINT=$OBJECT_STORE_ENDPOINT
OBJECT_STORE_BUCKET=nebulamind-console
OBJECT_STORE_ACCESS_KEY=minioadmin
OBJECT_STORE_SECRET_KEY=minioadmin

# CI/CD
CI_PROJECT=nebulamind-console-api
CI_RUNNER_URL=$CI_RUNNER_URL
CI_PIPELINE_URL=$CI_RUNNER_URL/nebulamind/console-api
CI_API_URL=$CI_RUNNER_URL/api/v1

# Internal Services
GIT_SERVICE_URL=$GIT_SERVICE_URL
CACHE_BROKER_URL=$CACHE_BROKER_URL
AI_CONSOLE_API_URL=$AI_CONSOLE_API_URL

# Feature Flags
FEATURE_GRAPHQL_INTROSPECTION=true
FEATURE_AUDIT_LOG_EXPORT=true

# Security
FLAG=$FLAG_E1
ENVEOF
    git add -A
    commit_with_date "chore: add .env.example.old with dev credentials" "2026-01-15T11:20:00"

    # --- Commit 4: 租户管理 ---
    mkdir -p src/middleware src/routes
    cat > src/app.py <<'APPEOF'
"""NebulaMind Console API - Main Application."""
from flask import Flask
from src.routes.tenant import tenant_bp
from src.routes.auth import auth_bp
from src.middleware.audit import AuditMiddleware

app = Flask(__name__)
app.register_blueprint(tenant_bp, url_prefix="/api/v1/tenant")
app.register_blueprint(auth_bp, url_prefix="/api/v1/auth")
app.wsgi_app = AuditMiddleware(app.wsgi_app)

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8080)
APPEOF
    cat > src/routes/tenant.py <<'TENEOF'
"""Tenant management routes."""
from flask import Blueprint, request, jsonify
from src.middleware.auth import require_role

tenant_bp = Blueprint("tenant", __name__)

@tenant_bp.route("/list", methods=["GET"])
@require_role("admin")
def list_tenants():
    return jsonify({"tenants": [{"id": 1, "name": "tenant_001"}]})

@tenant_bp.route("/<int:tenant_id>", methods=["GET"])
@require_role("admin")
def get_tenant(tenant_id):
    return jsonify({"id": tenant_id, "name": f"tenant_{tenant_id:03d}"})
TENEOF
    git add -A
    commit_with_date "feat: implement tenant management endpoints" "2026-01-20T10:00:00"

    # --- Commit 5: JWT 中间件 ---
    cat > src/middleware/auth.py <<'AUTHEOF'
"""JWT authentication middleware."""
import jwt
from functools import wraps
from flask import request, jsonify

JWT_SECRET = "nebulamind-dev-secret-2026"
JWT_ALGORITHM = "HS256"

def require_role(role):
    def decorator(f):
        @wraps(f)
        def wrapper(*args, **kwargs):
            token = request.headers.get("Authorization", "").replace("Bearer ", "")
            if not token:
                return jsonify({"error": "unauthorized"}), 401
            try:
                payload = jwt.decode(token, JWT_SECRET, algorithms=[JWT_ALGORITHM])
                if role not in payload.get("roles", []):
                    return jsonify({"error": "forbidden"}), 403
            except jwt.InvalidTokenError:
                return jsonify({"error": "invalid token"}), 401
            return f(*args, **kwargs)
        return wrapper
    return decorator
AUTHEOF
    git add -A
    commit_with_date "refactor: extract JWT auth middleware" "2026-01-25T16:00:00"

    # --- Commit 6: 删除 .env.example.old（FLAG 文件从 HEAD 移除，但历史保留）---
    git rm -q .env.example.old
    commit_with_date "chore: remove stale .env.example.old (use vault for secrets)" "2026-02-05T16:00:00"

    # --- Commit 7: GraphQL ---
    cat > src/routes/graphql.py <<'GQLEOF'
"""GraphQL endpoint with introspection enabled."""
from flask import Blueprint, request, jsonify

graphql_bp = Blueprint("graphql", __name__)

@graphql_bp.route("/graphql", methods=["POST"])
def graphql_endpoint():
    query = request.json.get("query", "")
    # Introspection is enabled for development convenience
    return jsonify({"data": {}, "extensions": {"introspection": True}})
GQLEOF
    cat > .env.example <<'ENVEOF'
# NebulaMind Console API - Environment Configuration
# Copy to .env and fill in values from vault

DATABASE_URL=$READONLY_DATABASE_URL
JWT_SECRET=<from-vault:secret/nebulamind/jwt-secret>
OBJECT_STORE_ENDPOINT=$OBJECT_STORE_ENDPOINT
CI_PROJECT=nebulamind-console-api
GIT_SERVICE_URL=$GIT_SERVICE_URL
CACHE_BROKER_URL=$CACHE_BROKER_URL
ENVEOF
    git add -A
    commit_with_date "feat: add GraphQL endpoint with introspection" "2026-02-10T14:00:00"

    # --- Commit 8: 审计日志修复 ---
    cat > src/middleware/audit.py <<'AUDITEOF'
"""Audit logging middleware - records all admin operations."""
import json
import time
from datetime import datetime

class AuditMiddleware:
    def __init__(self, app):
        self.app = app

    def __call__(self, environ, start_response):
        method = environ.get("REQUEST_METHOD", "")
        path = environ.get("PATH_INFO", "")
        if method in ("POST", "PUT", "DELETE", "PATCH") and "/api/v1/admin/" in path:
            self._log_audit(method, path, environ)
        return self.app(environ, start_response)

    def _log_audit(self, method, path, environ):
        entry = {
            "timestamp": datetime.utcnow().isoformat() + "Z",
            "method": method,
            "path": path,
            "remote_addr": environ.get("REMOTE_ADDR", ""),
            "user_agent": environ.get("HTTP_USER_AGENT", ""),
        }
        # Sanitize: ensure tenant isolation in audit export
        print(json.dumps(entry), flush=True)
AUDITEOF
    git add -A
    commit_with_date "fix: sanitize audit log export for tenant isolation" "2026-02-15T11:30:00"

    # --- Commit 9: README 更新 ---
    cat > DEPLOY.md <<'DEPLOYEOF'
# Deployment Guide

## Build

```bash
docker build -t nebulamind-console-api .
```

## Deploy

Deployed via CI/CD pipeline. See infra-playbooks repository.

## Health Check

```bash
curl __NM_AI_CONSOLE_API_URL__/healthz
```

## Configuration

All secrets must be loaded from vault. Do not commit .env files.
DEPLOYEOF
    nm_replace_file DEPLOY.md "__NM_AI_CONSOLE_API_URL__" "$AI_CONSOLE_API_URL"
    git add -A
    commit_with_date "docs: update README with deployment instructions" "2026-03-01T09:00:00"

    # 推送到裸仓库
    git remote add origin "$bare"
    git push -q origin main

    cd /
    rm -rf "$work"
    echo "[git-service] console-api initialized (9 commits, E1 flag in history)"
}

# ===========================================================================
# doc-worker 仓库
# ===========================================================================
init_doc_worker() {
    bare="$REPO_ROOT/doc-worker.git"
    [ -d "$bare" ] && { echo "[git-service] doc-worker already initialized"; return 0; }

    git init --bare -q "$bare"
    git --git-dir="$bare" config gitweb.description "NebulaMind Document Worker - Document parsing and conversion worker"
    work=$(mktemp -d)
    cd "$work"
    git init -q -b main

    # --- Commit 1 ---
    cp /app/repos/doc-worker/README.md .
    cat > requirements.txt <<'REQ'
flask>=3.0,<4.0
redis>=5.0,<6.0
Pillow>=10.0,<11.0
REQ
    cat > .gitignore <<'GIEOF'
__pycache__/
*.pyc
.env
.venv/
uploads/
*.log
GIEOF
    git add -A
    commit_with_date "feat: initial commit - document worker scaffold" "2026-01-08T09:00:00"

    # --- Commit 2 ---
    mkdir -p config docs
cat > config/worker.yml <<YMLEOF
service:
  name: document-worker
  version: 2026.06.1
  zone: business
  port: 8080
  consumer_group: document-workers

queue:
  broker:
    host: $NM_CACHE_BROKER_HOST
    port: 6379
    db: 2
  task_types:
    - document-parse
    - document-ocr
    - document-convert

worker:
  concurrency: 4
  poll_interval: 2s
YMLEOF
    cat > docs/worker-design.md <<'DOCEOF'
# Document Worker Design

## Task Flow

1. Task arrives on document-workers queue
2. Worker picks up task, fetches source document
3. Document parsed and converted per profile
4. Result stored in object store
5. Task status updated

## Profiles

- standard: default conversion
- high-quality: slower, better OCR
- ocr-enabled: force OCR pipeline
- fast: skip OCR, minimal processing
DOCEOF
    git add -A
    commit_with_date "docs: add worker configuration documentation" "2026-01-10T14:00:00"

    # --- Commit 3 ---
    cat > app.py <<'APPEOF'
"""NebulaMind Document Worker - Main Application."""
from flask import Flask, request, jsonify
import os

app = Flask(__name__)

@app.route("/healthz")
def healthz():
    return "ok", 200

@app.route("/api/parse", methods=["POST"])
def parse():
    data = request.get_json() or {}
    url = data.get("url", "")
    profile = data.get("profile", "standard")
    # Fetch and parse document
    return jsonify({"status": "completed", "url": url, "profile": profile})

if __name__ == "__main__":
    app.run(host="0.0.0.0", port=8080)
APPEOF
    git add -A
    commit_with_date "feat: implement document parse endpoint" "2026-01-15T10:30:00"

    # --- Commit 4 ---
    mkdir -p src
    cat > src/ocr.py <<'OCREOF'
"""OCR processing module."""
from PIL import Image
import io

def run_ocr(image_bytes, profile="standard"):
    """Run OCR on image bytes. Returns extracted text."""
    img = Image.open(io.BytesIO(image_bytes))
    # Placeholder: real implementation uses tesseract
    return f"[OCR result for {img.size} image, profile={profile}]"
OCREOF
    git add -A
    commit_with_date "feat: add OCR profile support" "2026-01-20T15:00:00"

    # --- Commit 5 ---
cat > src/queue.py <<QUEUEEOF
"""Queue consumer logic."""
import json
import redis

class QueueConsumer:
    def __init__(self, host="$NM_CACHE_BROKER_HOST", port=6379, db=2):
        self.redis = redis.Redis(host=host, port=port, db=db)
        self.group = "document-workers"

    def consume(self, count=1):
        """Consume messages from the queue."""
        results = self.redis.xreadgroup(self.group, "worker-1", {"tasks": ">"}, count=count)
        return [json.loads(m[1]) for m in results]

    def ack(self, message_id):
        """Acknowledge a processed message."""
        self.redis.xack("tasks", self.group, message_id)
QUEUEEOF
    git add -A
    commit_with_date "refactor: extract queue consumer logic" "2026-01-28T11:00:00"

    # --- Commit 6 ---
    cat > src/url_utils.py <<'URLEOF'
"""URL validation and sanitization."""
from urllib.parse import urlparse

def validate_url(url):
    """Validate document URL. Returns (is_valid, error)."""
    if not url:
        return False, "url is required"
    try:
        parsed = urlparse(url)
        if parsed.scheme not in ("http", "https"):
            return False, f"unsupported scheme: {parsed.scheme}"
        if not parsed.hostname:
            return False, "missing hostname"
        return True, None
    except Exception as e:
        return False, str(e)
URLEOF
    git add -A
    commit_with_date "fix: handle malformed document URLs gracefully" "2026-02-01T14:00:00"

    # --- Commit 7 ---
    cat > src/convert.py <<'CONVEOF'
"""Document conversion profiles."""
import subprocess

PROFILES = {
    "standard": {"timeout": 60, "ocr": False},
    "high-quality": {"timeout": 120, "ocr": True},
    "ocr-enabled": {"timeout": 120, "ocr": True},
    "fast": {"timeout": 30, "ocr": False},
}

def convert(input_path, profile="standard"):
    """Convert document using specified profile."""
    config = PROFILES.get(profile, PROFILES["standard"])
    result = subprocess.run(
        ["convert", "--profile", profile, input_path],
        capture_output=True, text=True, timeout=config["timeout"]
    )
    return result.stdout, result.returncode
CONVEOF
    git add -A
    commit_with_date "feat: add conversion profile system" "2026-02-10T16:00:00"

    # --- Commit 8 ---
    cat > requirements.txt <<'REQ'
flask>=3.0,<4.0
redis>=5.0,<6.0
Pillow>=10.2,<11.0
gunicorn>=21.0,<23.0
REQ
    git add -A
    commit_with_date "chore: update dependencies and lock file" "2026-02-20T10:00:00"

    # --- Commit 9 ---
    cat > docs/troubleshooting.md <<'TROEOF'
# Troubleshooting Guide

## Common Issues

### Worker not consuming tasks
- Check Redis connectivity: `redis-cli -h __NM_CACHE_BROKER_HOST__ ping`
- Verify consumer group exists: `XINFO GROUPS tasks`
- Check worker logs for errors

### OCR failing
- Ensure tesseract is installed
- Check image format is supported
- Verify profile timeout is sufficient

### Conversion timeout
- Use 'fast' profile for large documents
- Increase timeout in config/worker.yml
- Check system load
TROEOF
    nm_replace_file docs/troubleshooting.md "__NM_CACHE_BROKER_HOST__" "$NM_CACHE_BROKER_HOST"
    git add -A
    commit_with_date "docs: add troubleshooting guide" "2026-03-05T09:00:00"

    git remote add origin "$bare"
    git push -q origin main

    cd /
    rm -rf "$work"
    echo "[git-service] doc-worker initialized (9 commits)"
}

# ===========================================================================
# infra-playbooks 仓库
# ===========================================================================
init_infra_playbooks() {
    bare="$REPO_ROOT/infra-playbooks.git"
    [ -d "$bare" ] && { echo "[git-service] infra-playbooks already initialized"; return 0; }

    git init --bare -q "$bare"
    git --git-dir="$bare" config gitweb.description "NebulaMind Infrastructure Playbooks - Ansible deployment and configuration"
    work=$(mktemp -d)
    cd "$work"
    git init -q -b main

    # --- Commit 1 ---
    cp /app/repos/infra-playbooks/README.md .
    cat > .gitignore <<'GIEOF'
*.retry
*.log
.vault_pass
vault-password.txt
__pycache__/
*.pyc
GIEOF
    cat > ansible.cfg <<'CFGEOF'
[defaults]
inventory = inventory/
host_key_checking = False
retry_files_enabled = False
roles_path = roles/
stdout_callback = yaml
timeout = 30
CFGEOF
    git add -A
    commit_with_date "feat: initial commit - ansible playbook scaffold" "2026-01-05T09:00:00"

    # --- Commit 2 ---
    mkdir -p docs
    cat > docs/infrastructure-overview.md <<'OVEEOF'
# NebulaMind Infrastructure Overview

## Zones

- **DMZ**: Public-facing services (portal-web, edge-gateway)
- **Business**: Internal application services (ai-console-api, document-worker, cache-broker)
- **Operations**: Infrastructure services (git-service, monitoring)

## Networks

- DMZ segment: assigned by GZCTF penetration orchestrator
- Business segment: assigned by GZCTF penetration orchestrator
- Operations segment: assigned by GZCTF penetration orchestrator
- Data segment: assigned by GZCTF penetration orchestrator

## Services

| Service | Zone | Runtime endpoint |
|---------|------|------------------|
| portal-web | DMZ | __NM_PORTAL_WEB_URL__ |
| edge-gateway | DMZ | platform public entry |
| ai-console-api | Business | __NM_AI_CONSOLE_API_URL__ |
| document-worker | Business | __NM_DOCUMENT_WORKER_URL__ |
| cache-broker | Business | __NM_CACHE_BROKER_HOST__:6379 |
| git-service | Operations | __NM_GIT_SERVICE_URL__ |
OVEEOF
    nm_replace_file docs/infrastructure-overview.md "__NM_PORTAL_WEB_URL__" "$NM_PORTAL_WEB_URL"
    nm_replace_file docs/infrastructure-overview.md "__NM_AI_CONSOLE_API_URL__" "$AI_CONSOLE_API_URL"
    nm_replace_file docs/infrastructure-overview.md "__NM_DOCUMENT_WORKER_URL__" "$NM_DOCUMENT_WORKER_URL"
    nm_replace_file docs/infrastructure-overview.md "__NM_CACHE_BROKER_HOST__" "$NM_CACHE_BROKER_HOST"
    nm_replace_file docs/infrastructure-overview.md "__NM_GIT_SERVICE_URL__" "$GIT_SERVICE_URL"
    git add -A
    commit_with_date "docs: add infrastructure overview" "2026-01-08T14:00:00"

    # --- Commit 3 ---
    mkdir -p playbooks roles/console-api/tasks roles/console-api/templates
    cat > playbooks/deploy-console-api.yml <<'PBEOF'
---
- name: Deploy NebulaMind Console API
  hosts: console-api
  become: yes
  roles:
    - console-api
  vars:
    app_version: "2026.06.1"
    app_port: 8080
PBEOF
    cat > roles/console-api/tasks/main.yml <<'TASKEOF'
---
- name: Pull console-api image
  docker_image:
    name: "nebulamind/console-api:{{ app_version }}"
    source: pull

- name: Start console-api container
  docker_container:
    name: ai-console-api
    image: "nebulamind/console-api:{{ app_version }}"
    ports:
      - "8080:8080"
    networks:
      - name: biz-core
    env:
      DATABASE_URL: "{{ vault_db_url }}"
      JWT_SECRET: "{{ vault_jwt_secret }}"
    restart_policy: unless-stopped
TASKEOF
    git add -A
    commit_with_date "feat: add deployment playbook for console-api" "2026-01-12T10:00:00"

    # --- Commit 4 ---
    mkdir -p roles/cache-broker/tasks roles/cache-broker/templates
    cat > playbooks/deploy-cache-broker.yml <<'PBEOF'
---
- name: Deploy NebulaMind Cache Broker
  hosts: cache-broker
  become: yes
  roles:
    - cache-broker
  vars:
    redis_version: "7.2"
    redis_port: 6379
PBEOF
    cat > roles/cache-broker/tasks/main.yml <<'TASKEOF'
---
- name: Start Redis container
  docker_container:
    name: cache-broker
    image: "redis:{{ redis_version }}-alpine"
    ports:
      - "6379:6379"
    command: redis-server /usr/local/etc/redis/redis.conf
    volumes:
      - redis-data:/data
    networks:
      - name: biz-core
    restart_policy: unless-stopped
TASKEOF
    git add -A
    commit_with_date "feat: add cache-broker setup role" "2026-01-18T15:00:00"

    # --- Commit 5 ---
    mkdir -p group_vars
    cat > group_vars/all.yml <<'ALLVAREOF'
---
# Common variables for all environments
nebulamind_version: "2026.06.1"
timezone: "Asia/Shanghai"

# Network segments
networks:
  dmz_service: "dmz-service"
  biz_core: "biz-core"
  ops_net: "ops-net"

# Platform-injected runtime endpoints
runtime_endpoints:
  console_api: "__NM_AI_CONSOLE_API_URL__"
  doc_worker: "__NM_DOCUMENT_WORKER_URL__"
  cache_broker: "__NM_CACHE_BROKER_HOST__"
  git_service: "__NM_GIT_SERVICE_URL__"
  portal_web: "__NM_PORTAL_WEB_URL__"
  edge_gateway: "platform-public-entry"
ALLVAREOF
    nm_replace_file group_vars/all.yml "__NM_AI_CONSOLE_API_URL__" "$AI_CONSOLE_API_URL"
    nm_replace_file group_vars/all.yml "__NM_DOCUMENT_WORKER_URL__" "$NM_DOCUMENT_WORKER_URL"
    nm_replace_file group_vars/all.yml "__NM_CACHE_BROKER_HOST__" "$NM_CACHE_BROKER_HOST"
    nm_replace_file group_vars/all.yml "__NM_GIT_SERVICE_URL__" "$GIT_SERVICE_URL"
    nm_replace_file group_vars/all.yml "__NM_PORTAL_WEB_URL__" "$NM_PORTAL_WEB_URL"
    git add -A
    commit_with_date "refactor: extract common variables" "2026-01-22T11:00:00"

    # --- Commit 6 ---
    cat > roles/console-api/tasks/main.yml <<'TASKEOF'
---
- name: Pull console-api image
  docker_image:
    name: "nebulamind/console-api:{{ app_version }}"
    source: pull

- name: Start console-api container
  docker_container:
    name: ai-console-api
    image: "nebulamind/console-api:{{ app_version }}"
    ports:
      - "8080:8080"
    networks:
      - name: "{{ networks.biz_core }}"
    env:
      DATABASE_URL: "{{ vault_db_url }}"
      JWT_SECRET: "{{ vault_jwt_secret }}"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/healthz"]
      interval: 30s
      timeout: 5s
      retries: 3
    restart_policy: unless-stopped
TASKEOF
    git add -A
    commit_with_date "fix: correct health check URLs in deployment" "2026-01-28T14:00:00"

    # --- Commit 7 ---
    mkdir -p roles/monitoring/tasks roles/monitoring/templates
    cat > playbooks/setup-monitoring.yml <<'PBEOF'
---
- name: Setup monitoring and alerting
  hosts: monitoring
  become: yes
  roles:
    - monitoring
  vars:
    alert_email: "ops@nebulamind.ai"
PBEOF
    cat > roles/monitoring/tasks/main.yml <<'TASKEOF'
---
- name: Deploy Prometheus
  docker_container:
    name: prometheus
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml
    networks:
      - name: "{{ networks.ops_net }}"
    restart_policy: unless-stopped

- name: Deploy Grafana
  docker_container:
    name: grafana
    image: grafana/grafana:latest
    ports:
      - "3001:3000"
    networks:
      - name: "{{ networks.ops_net }}"
    restart_policy: unless-stopped
TASKEOF
    git add -A
    commit_with_date "feat: add monitoring and alerting rules" "2026-02-05T10:00:00"

    # --- Commit 8 ---
    cat > docs/key-rotation.md <<'KEYEOF'
# Deployment Key Rotation

## Schedule

- Deployment keys rotated quarterly
- JWT secrets rotated quarterly
- Database passwords rotated semi-annually

## Procedure

1. Generate new key in vault
2. Update service configuration
3. Rolling restart of affected services
4. Verify health checks pass
5. Revoke old key after 24h grace period

## Current Keys (Q1 2026)

- console-api JWT: vault://secret/nebulamind/jwt-secret
- worker token: vault://secret/nebulamind/worker-tokens
- git deploy key: vault://secret/nebulamind/git-deploy-key
KEYEOF
    git add -A
    commit_with_date "chore: rotate deployment keys" "2026-02-12T16:00:00"

    # --- Commit 9 ---
    cat > docs/disaster-recovery.md <<'DREOF'
# Disaster Recovery Runbook

## RPO and RTO

- RPO (Recovery Point Objective): 1 hour
- RTO (Recovery Time Objective): 4 hours

## Backup Strategy

- PostgreSQL: daily snapshots + WAL archiving
- Redis: RDB snapshots every 6 hours
- Object storage: cross-region replication

## Recovery Procedures

### Database Failure
1. Provision new PostgreSQL instance
2. Restore from latest snapshot
3. Replay WAL to recovery point
4. Update service configuration
5. Restart affected services

### Redis Failure
1. Provision new Redis instance
2. Restore from latest RDB
3. Update worker configuration
4. Restart workers

### Full Region Failure
1. Activate DR region
2. Restore all services from backups
3. Update DNS to point to DR region
4. Verify all health checks
DREOF
    git add -A
    commit_with_date "docs: add disaster recovery runbook" "2026-02-20T09:00:00"

    git remote add origin "$bare"
    git push -q origin main

    cd /
    rm -rf "$work"
    echo "[git-service] infra-playbooks initialized (9 commits)"
}

# ===========================================================================
# 初始化所有仓库
# ===========================================================================
echo "[git-service] initializing repositories..."
init_console_api
init_doc_worker
init_infra_playbooks
echo "[git-service] all repositories initialized"

echo "[git-service] E1 flag injected into console-api history (.env.example.old @ commit 3)"
echo "[git-service] starting Flask server on port 3000..."

exec "$@"
