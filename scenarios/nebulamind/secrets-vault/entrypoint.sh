#!/bin/sh
set -e

# secrets-vault 入口脚本
# 职责：
# 1. 读取 G1 Flag（FLAG_VAULT_POLICY_BYPASS）环境变量
# 2. 处理 seed/secrets.json 中的 Flag 占位符（sed 替换）
# 3. 导出 Flag 环境变量供 app.py 读取（双保险）
# 4. 启动 Python Flask 服务
#
# Flag 注入位置：
#   G1 (FLAG_VAULT_POLICY_BYPASS) -> secret/data/nebulamind/model-registry 的 flag 字段
#       seed/secrets.json 中 __NM_FLAG_G1__ 占位符由 sed 替换为真实 Flag
#
# G1 题目链路：
#   选手通过 E3（CI Runner 任务注入）读取 ci-runner 的 /opt/nebulamind/vault-credentials.json
#   获取 bootstrap token (s.bootstrap-nebulamind-2026)
#   然后用此 token 访问 GET /v1/secret/data/nebulamind/model-registry
#   返回结果中包含 G1 Flag 和 model_registry_admin_token（用于 G2）

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_CUSTOMER_DB_HOST NM_MODEL_REGISTRY_URL NM_CI_RUNNER_URL NM_OBJECT_STORE_URL NM_OBJECT_STORE_CONSOLE_URL

# G1: Vault Bootstrap Token 滥用 - Flag 注入到 secret/data/nebulamind/model-registry
FLAG_G1="$(get_flag 'FLAG_VAULT_POLICY_BYPASS' 'flag{g1_vault_policy_bypass_placeholder}')"
# 导出供 app.py 的 get_flag() 读取（双保险：app.py 也会在内存中替换占位符）
# 注意：变量名必须为 GZCTF_FLAG_<NAME>（单 FLAG_ 前缀），避免双前缀导致 app.py 找不到变量。
export GZCTF_FLAG_VAULT_POLICY_BYPASS="$FLAG_G1"

echo "[secrets-vault] G1 flag available via env (FLAG_VAULT_POLICY_BYPASS)"
echo "[secrets-vault] processing seed/secrets.json with flag placeholder..."

# ============================================================
# 处理种子文件：替换 Flag 占位符
# ============================================================
SEED_DIR="/app/seed"
PROCESSED_DIR="/opt/nebulamind/seed-processed"
mkdir -p "$PROCESSED_DIR"

# 转义 sed 替换串中的特殊字符（\ & /），避免破坏替换
esc() {
    printf '%s' "$1" | sed -e 's/[\\&]/\\&/g' -e 's|/|\\/|g'
}

FLAG_G1_ESC="$(esc "$FLAG_G1")"

# 处理 secrets.json（替换 G1 Flag 占位符）
sed "s/__NM_FLAG_G1__/$FLAG_G1_ESC/g" "$SEED_DIR/secrets.json" > "$PROCESSED_DIR/secrets.json"
python3 - <<'PYEOF'
import os

path = "/opt/nebulamind/seed-processed/secrets.json"
replacements = {
    "__NM_CUSTOMER_DB_HOST__": os.environ["NM_CUSTOMER_DB_HOST"],
    "__NM_MODEL_REGISTRY_URL__": os.environ["NM_MODEL_REGISTRY_URL"].rstrip("/"),
    "__NM_CI_RUNNER_URL__": os.environ["NM_CI_RUNNER_URL"].rstrip("/"),
    "__NM_OBJECT_STORE_URL__": os.environ["NM_OBJECT_STORE_URL"].rstrip("/"),
    "__NM_OBJECT_STORE_CONSOLE_URL__": os.environ["NM_OBJECT_STORE_CONSOLE_URL"].rstrip("/"),
}
with open(path, "r", encoding="utf-8") as f:
    content = f.read()
for placeholder, value in replacements.items():
    content = content.replace(placeholder, value)
with open(path, "w", encoding="utf-8") as f:
    f.write(content)
PYEOF
chmod 0644 "$PROCESSED_DIR/secrets.json"
echo "[secrets-vault] processed secrets.json with G1 flag -> $PROCESSED_DIR/secrets.json"

# ============================================================
# 启动 Flask 服务
# ============================================================
echo "[secrets-vault] starting Flask Vault mock on port 8200..."
echo "[secrets-vault] bootstrap token: s.bootstrap-nebulamind-2026 (full access, G1 entry)"
echo "[secrets-vault] ci-reader token: s.ci-reader-2026-readonly (ci-config only)"
echo "[secrets-vault] model-admin token: s.model-admin-2026-registry (model-registry only)"
echo "[secrets-vault] G1 flag in: secret/data/nebulamind/model-registry (flag field)"

exec "$@"
