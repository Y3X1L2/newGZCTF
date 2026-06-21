#!/bin/sh
set -e

# ci-runner 入口脚本
# 职责：
# 1. 注入 E2 Flag（CI 变量泄露）- 通过环境变量传给 app.py
# 2. 注入 E3 Flag（CI Runner 任务注入）到 /opt/nebulamind/ci.flag
# 3. 创建 /opt/nebulamind/vault-credentials.json（Vault 服务凭据，用于 G1）
# 4. 启动 Python Flask 服务
#
# 安全说明：
# - E3 的命令注入仅在容器内执行（非 root、无 Docker socket、无特权）
# - 选手通过变量注入 $(cat /opt/nebulamind/ci.flag) 读取 E3 Flag
# - 选手通过变量注入 $(cat /opt/nebulamind/vault-credentials.json) 读取 Vault 凭据

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_GIT_SERVICE_URL NM_CUSTOMER_DB_HOST NM_CACHE_BROKER_HOST NM_SECRETS_VAULT_URL

# E2: CI 变量泄露 - Flag 通过环境变量传给 app.py
# app.py 通过 get_flag('FLAG_CI_VARIABLE_LEAK') 读取
# 此 Flag 出现在 /api/projects/{id}/variables 接口的返回中（masked 变量明文泄露）
FLAG_E2="$(get_flag 'FLAG_CI_VARIABLE_LEAK' 'flag{e2_ci_variable_leak_placeholder}')"
export GZCTF_FLAG_FLAG_CI_VARIABLE_LEAK="$FLAG_E2"

# E3: CI Runner 任务注入 - Flag 注入到 /opt/nebulamind/ci.flag
# 选手通过 POST /api/projects/{id}/trigger 注入变量
# 变量值中的 $(cat /opt/nebulamind/ci.flag) 会被 shell 执行
# Flag 出现在构建日志中
FLAG_E3="$(get_flag 'FLAG_CI_RUNNER_EXEC' 'flag{e3_ci_runner_exec_placeholder}')"
printf '%s\n' "$FLAG_E3" > /opt/nebulamind/ci.flag
chmod 0644 /opt/nebulamind/ci.flag

# 创建 Vault 服务凭据文件（用于 G1 题目链）
# 选手通过 E3 命令注入读取此文件，获取 Vault bootstrap token
# 然后用此 token 访问 secrets-vault 服务获取 G1 Flag
cat > /opt/nebulamind/vault-credentials.json <<EOF
{
  "vault_addr": "$NM_SECRETS_VAULT_URL",
  "bootstrap_token": "s.bootstrap-nebulamind-2026",
  "role_id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "secret_id": "b2c3d4e5-f6a7-8901-bcde-f12345678901",
  "namespace": "nebulamind/",
  "mount_path": "secret",
  "secret_paths": [
    "secret/data/nebulamind/jwt-secret",
    "secret/data/nebulamind/db-credentials",
    "secret/data/nebulamind/object-store",
    "secret/data/nebulamind/model-registry",
    "secret/data/nebulamind/ci-config"
  ],
  "note": "Vault bootstrap token for CI runner service account. Use 'vault login -method=token token=s.bootstrap-nebulamind-2026' to authenticate. WARNING: This token has bootstrap policy with full read/write access to all secrets.",
  "created_at": "2026-06-15T00:00:00Z",
  "expires_at": "2026-09-15T00:00:00Z",
  "owner": "platform-ops@nebulamind.ai"
}
EOF
chmod 0644 /opt/nebulamind/vault-credentials.json

echo "[ci-runner] E2 flag available via env (FLAG_CI_VARIABLE_LEAK)"
echo "[ci-runner] E3 flag injected into /opt/nebulamind/ci.flag"
echo "[ci-runner] vault-credentials.json created at /opt/nebulamind/"
echo "[ci-runner] starting Flask server on port 8080..."

exec "$@"
