#!/bin/sh
set -e

# model-registry 入口脚本
# 职责：
# 1. 读取 G2 Flag（FLAG_MODEL_REGISTRY_ADMIN）环境变量
# 2. 处理 seed/models.json 中的 Flag 占位符（sed 替换）
# 3. 导出 Flag 环境变量供 app.py 读取（双保险）
# 4. 启动 Python Flask 服务
#
# Flag 注入位置：
#   G2 (FLAG_MODEL_REGISTRY_ADMIN) -> recommendation-v4-private v4 manifest 的 flag 字段
#       seed/models.json 中 __NM_FLAG_G2__ 占位符由 sed 替换为真实 Flag
#
# G2 题目链路：
#   选手通过 G1（secrets-vault）获取 secret/data/nebulamind/model-registry
#   其中包含 model_registry_admin_token = nm-model-admin-token-2026
#   选手用此 admin token 访问 GET /api/v1/models/recommendation-v4-private/versions/v4/manifest
#   返回的 manifest 中 flag 字段为 G2 Flag
#   用普通 viewer token 访问返回 403
#
# G3 终局链路（model-registry 部分）：
#   recommendation-v4-private 的 manifest 中 compliance 字段包含：
#     - training_log_path: s3://public-model-artifacts/training-logs/recommendation-v4-private-train.log
#     - audit_id: audit-2026-006
#     - audit_record_location: postgresql://<platform-db-host>:5432/nebulamind/regulated_model_training_records?id=6
#   选手从 manifest 找到训练日志路径 -> 从 object-store 下载训练日志
#   -> 训练日志中有 audit_id -> 查询 customer-db 的 regulated_model_training_records 表 id=6
#   -> 获得 G3 Flag（在 customer-db，不在本服务）

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_MODEL_REGISTRY_URL NM_CUSTOMER_DB_HOST NM_OBJECT_STORE_URL

# G2: Model Registry Admin Token Abuse - Flag 注入到 recommendation-v4-private manifest
FLAG_G2="$(get_flag 'FLAG_MODEL_REGISTRY_ADMIN' 'flag{g2_model_registry_admin_placeholder}')"
# 导出供 app.py 的 get_flag() 读取（双保险：app.py 也会在内存中替换占位符）
export GZCTF_FLAG_FLAG_MODEL_REGISTRY_ADMIN="$FLAG_G2"

echo "[model-registry] G2 flag available via env (FLAG_MODEL_REGISTRY_ADMIN)"
echo "[model-registry] processing seed/models.json with flag placeholder..."

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

FLAG_G2_ESC="$(esc "$FLAG_G2")"

# 处理 models.json（替换 G2 Flag 占位符）
sed "s/__NM_FLAG_G2__/$FLAG_G2_ESC/g" "$SEED_DIR/models.json" > "$PROCESSED_DIR/models.json"
python3 - <<'PYEOF'
import os

path = "/opt/nebulamind/seed-processed/models.json"
replacements = {
    "__NM_MODEL_REGISTRY_URL__": os.environ["NM_MODEL_REGISTRY_URL"].rstrip("/"),
    "__NM_CUSTOMER_DB_HOST__": os.environ["NM_CUSTOMER_DB_HOST"],
    "__NM_OBJECT_STORE_URL__": os.environ["NM_OBJECT_STORE_URL"].rstrip("/"),
}
with open(path, "r", encoding="utf-8") as f:
    content = f.read()
for placeholder, value in replacements.items():
    content = content.replace(placeholder, value)
with open(path, "w", encoding="utf-8") as f:
    f.write(content)
PYEOF
chmod 0644 "$PROCESSED_DIR/models.json"
echo "[model-registry] processed models.json with G2 flag -> $PROCESSED_DIR/models.json"

# ============================================================
# 启动 Flask 服务
# ============================================================
echo "[model-registry] starting Flask model registry on port 8080..."
echo "[model-registry] admin token (from Vault): nm-model-admin-token-2026 (G2 entry)"
echo "[model-registry] viewer login: viewer/viewer123 (public models only)"
echo "[model-registry] model-admin login: model-admin/nm-admin-2026-registry (all models)"
echo "[model-registry] G2 flag in: recommendation-v4-private v4 manifest (flag field)"
echo "[model-registry] G3 clues in: recommendation-v4-private v4 manifest (compliance field)"

exec "$@"
