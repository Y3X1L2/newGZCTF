#!/bin/sh
set -e

# document-worker 入口脚本
# 职责：
# 1. 注入 B3 Flag（SSRF metadata 泄露）- 由 app.py 通过环境变量读取
# 2. 注入 D3 Flag（命令注入）到 /opt/nebulamind/worker.flag
# 3. 创建 /opt/nebulamind/service-account.json（服务账号凭据，用于后续题目链）
# 4. 启动 Python Flask 服务

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_DOCUMENT_WORKER_URL NM_DOCUMENT_WORKER_HOST NM_CACHE_BROKER_HOST NM_AI_CONSOLE_API_URL NM_AI_CONSOLE_API_HOST

# B3: SSRF metadata 泄露 - Flag 通过环境变量传给 app.py
# app.py 通过 get_flag('FLAG_WORKER_SSRF_METADATA') 读取
FLAG_B3="$(get_flag 'FLAG_WORKER_SSRF_METADATA' 'flag{b3_ssrf_metadata_placeholder}')"
export GZCTF_FLAG_FLAG_WORKER_SSRF_METADATA="$FLAG_B3"

# D3: 命令注入 - Flag 注入到 /opt/nebulamind/worker.flag
# 选手通过 profile 参数注入 $(cat /opt/nebulamind/worker.flag) 读取此文件
FLAG_D3="$(get_flag 'FLAG_WORKER_COMMAND_INJECTION' 'flag{d3_command_injection_placeholder}')"
printf '%s\n' "$FLAG_D3" > /opt/nebulamind/worker.flag
chmod 0644 /opt/nebulamind/worker.flag

# 创建服务账号凭据文件（用于后续题目链 - 选手拿到此凭据可访问其他服务）
# 内容是靶场内部身份线索，刻意避开云厂商私钥字段形态。
cat > /opt/nebulamind/service-account.json <<'EOF'
{
  "type": "internal_service_identity",
  "project_id": "nebulamind-prod-cn",
  "identity_id": "nm-doc-worker-svc-2026",
  "identity_name": "doc-worker-svc",
  "credential_hint": "This lab identity intentionally has no cloud private key. Use the JWT material below for the next service.",
  "token_endpoint": "https://oauth2.nebulamind.internal/token",
  "scope": "https://www.nebulamind.internal/auth/kb.read https://www.nebulamind.internal/auth/console.admin",
  "jwt_secret_candidate": "nebulamind-dev-secret-2026",
  "jwt_issuer": "nebulamind-console-api",
  "jwt_algorithm": "HS256",
  "console_api_url": "__NM_AI_CONSOLE_API_URL__",
  "console_api_endpoints": [
    "/api/v1/auth/login",
    "/api/v1/admin/audit/export",
    "/graphql"
  ],
  "note": "dev/staging internal identity - contains JWT secret candidate for console API integration testing"
}
EOF
chmod 0644 /opt/nebulamind/service-account.json

python3 - <<'PYEOF'
import os

paths = [
    "/app/config/worker.yml",
    "/opt/nebulamind/service-account.json",
]
replacements = {
    "__NM_DOCUMENT_WORKER_URL__": os.environ["NM_DOCUMENT_WORKER_URL"].rstrip("/"),
    "__NM_DOCUMENT_WORKER_HOST__": os.environ["NM_DOCUMENT_WORKER_HOST"],
    "__NM_CACHE_BROKER_HOST__": os.environ["NM_CACHE_BROKER_HOST"],
    "__NM_AI_CONSOLE_API_URL__": os.environ["NM_AI_CONSOLE_API_URL"].rstrip("/"),
}
for path in paths:
    with open(path, "r", encoding="utf-8") as f:
        content = f.read()
    for placeholder, value in replacements.items():
        content = content.replace(placeholder, value)
    with open(path, "w", encoding="utf-8") as f:
        f.write(content)
PYEOF

echo "[document-worker] B3 flag available via env (FLAG_WORKER_SSRF_METADATA)"
echo "[document-worker] D3 flag injected into /opt/nebulamind/worker.flag"
echo "[document-worker] service-account.json created at /opt/nebulamind/"
echo "[document-worker] starting Flask server on port 8080..."

exec "$@"
