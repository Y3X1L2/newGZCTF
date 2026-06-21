#!/bin/sh
set -e

# init-buckets.sh - MinIO bucket 初始化脚本
# 职责：
# 1. 配置 mc alias
# 2. 创建 bucket（public-model-artifacts、customer-private、model-registry、ci-artifacts）
# 3. 创建用户（nm-low-priv 低权限、nm-admin 高权限）
# 4. 创建并附加 IAM 策略
# 5. 上传种子文件到各 bucket
#
# 环境变量：
#   MINIO_ROOT_USER      - MinIO root 用户名
#   MINIO_ROOT_PASSWORD  - MinIO root 密码
#   PROCESSED_DIR        - 已处理（含 Flag 替换）的种子文件目录

ALIAS="nmlocal"
ENDPOINT="http://127.0.0.1:9000"
ROOT_USER="${MINIO_ROOT_USER:-nm-root-admin}"
ROOT_PASSWORD="${MINIO_ROOT_PASSWORD:-nm-root-admin-secret-2026}"
SEED_DIR="${PROCESSED_DIR:-/opt/nebulamind/seed}"
POLICY_DIR="/opt/nebulamind/policies"

# 低权限用户凭据（D2 题目入口：选手通过其他途径获取此凭据后可访问 public-model-artifacts）
LOW_PRIV_ACCESS_KEY="nm-low-priv-key"
LOW_PRIV_SECRET_KEY="nm-low-priv-secret-2026"

# 高权限用户凭据（可访问所有 bucket）
ADMIN_ACCESS_KEY="AKIA-NEBULA-ADMIN-2026"
ADMIN_SECRET_KEY="nm-admin-secret-2026"

echo "[init-buckets] configuring mc alias '$ALIAS' -> $ENDPOINT"
mc alias set "$ALIAS" "$ENDPOINT" "$ROOT_USER" "$ROOT_PASSWORD"

# ============================================================
# 1. 创建 bucket
# ============================================================
echo "[init-buckets] creating buckets..."
mc mb -p "$ALIAS/public-model-artifacts" 2>/dev/null || echo "[init-buckets] bucket public-model-artifacts already exists"
mc mb -p "$ALIAS/customer-private" 2>/dev/null || echo "[init-buckets] bucket customer-private already exists"
mc mb -p "$ALIAS/model-registry" 2>/dev/null || echo "[init-buckets] bucket model-registry already exists"
mc mb -p "$ALIAS/ci-artifacts" 2>/dev/null || echo "[init-buckets] bucket ci-artifacts already exists"

# ============================================================
# 2. 创建 IAM 策略
# ============================================================
echo "[init-buckets] creating IAM policies..."
# 创建低权限策略（如已存在则尝试更新）
mc admin policy create "$ALIAS" low-priv "$POLICY_DIR/low-priv-policy.json" 2>/dev/null || {
    echo "[init-buckets] policy low-priv already exists, updating..."
    mc admin policy update "$ALIAS" low-priv "$POLICY_DIR/low-priv-policy.json" 2>/dev/null || true
}

# 创建管理员策略（如已存在则尝试更新）
mc admin policy create "$ALIAS" nm-admin-full "$POLICY_DIR/admin-policy.json" 2>/dev/null || {
    echo "[init-buckets] policy nm-admin-full already exists, updating..."
    mc admin policy update "$ALIAS" nm-admin-full "$POLICY_DIR/admin-policy.json" 2>/dev/null || true
}

# ============================================================
# 3. 创建用户
# ============================================================
echo "[init-buckets] creating users..."
# 低权限用户：只能访问 public-model-artifacts
mc admin user add "$ALIAS" "$LOW_PRIV_ACCESS_KEY" "$LOW_PRIV_SECRET_KEY" 2>/dev/null || \
    echo "[init-buckets] user $LOW_PRIV_ACCESS_KEY already exists"

# 高权限用户：可访问所有 bucket
mc admin user add "$ALIAS" "$ADMIN_ACCESS_KEY" "$ADMIN_SECRET_KEY" 2>/dev/null || \
    echo "[init-buckets] user $ADMIN_ACCESS_KEY already exists"

# ============================================================
# 4. 附加策略到用户
# ============================================================
echo "[init-buckets] attaching policies to users..."
# 附加策略：优先使用 attach（新版 mc），回退到 set（旧版 mc）
mc admin policy attach "$ALIAS" low-priv --user "$LOW_PRIV_ACCESS_KEY" 2>/dev/null || \
    mc admin policy set "$ALIAS" low-priv user="$LOW_PRIV_ACCESS_KEY" 2>/dev/null || true

mc admin policy attach "$ALIAS" nm-admin-full --user "$ADMIN_ACCESS_KEY" 2>/dev/null || \
    mc admin policy set "$ALIAS" nm-admin-full user="$ADMIN_ACCESS_KEY" 2>/dev/null || true

# ============================================================
# 5. 上传种子文件
# ============================================================
echo "[init-buckets] uploading seed files..."

# ---------- public-model-artifacts ----------
# D2 Flag 载体：误放的租户摘要导出 CSV（含 Flag 和数据库表名线索）
mc cp "$SEED_DIR/tenant-summary-2026.csv" "$ALIAS/public-model-artifacts/exports/tenant-summary-2026.csv"

# G3 终局线索：训练日志（指向 model-registry manifest 和 customer-db 审计记录）
mc cp "$SEED_DIR/recommendation-v4-private-train.log" "$ALIAS/public-model-artifacts/training-logs/recommendation-v4-private-train.log"

# 公开模型二进制（占位文件）
echo "[init-buckets] creating placeholder model binaries..."
printf 'NebulaMind Model Binary - nm-recommendation-v3-public\nFormat: PyTorch StateDict\nParameters: 124832512\nSHA256: e2f1a0b9c8d7e6f5a4b3c2d1e0f9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d3e2f1\n' > /tmp/rec-v3-public.bin
mc cp /tmp/rec-v3-public.bin "$ALIAS/public-model-artifacts/models/recommendation-v3-public.bin"

printf 'NebulaMind Model Binary - nm-classifier-v2-public\nFormat: HuggingFace Transformers\nParameters: 102267648\nSHA256: c5f0a4b3d1e9678230415b6f7e8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6\n' > /tmp/cls-v2-public.bin
mc cp /tmp/cls-v2-public.bin "$ALIAS/public-model-artifacts/models/classifier-v2-public.bin"

# 模型卡片文档
mc cp "$SEED_DIR/model-cards-README.md" "$ALIAS/public-model-artifacts/docs/model-cards/README.md"

# ---------- customer-private ----------
echo "[init-buckets] uploading customer-private seed files..."
# 合同 PDF（占位）
printf 'NebulaMind Contract Export - 2026 Q1\nClassification: Confidential\nContains: 30 active contracts\nGenerated: 2026-04-01\n' > /tmp/contracts.pdf
mc cp /tmp/contracts.pdf "$ALIAS/customer-private/contracts/2026-Q1-contracts.pdf"

# 客户数据导出 JSON（占位）
printf '{"export_name":"customers_full_2026Q1","format":"json","rows":30,"classification":"confidential","generated_at":"2026-04-01T02:00:00+08:00","note":"客户全量导出 2026Q1"}\n' > /tmp/exports.json
mc cp /tmp/exports.json "$ALIAS/customer-private/customer-data/exports.json"

# 数据库备份 SQL（占位）
printf '%s\n' \
    '-- NebulaMind customer-db backup 2026-06' \
    '-- Database: nebulamind' \
    '-- Classification: Restricted' \
    '-- Contains: customers, contracts, security_findings, regulated_model_training_records' \
    '-- Note: This backup is for internal use only.' > /tmp/db-backup.sql
mc cp /tmp/db-backup.sql "$ALIAS/customer-private/backups/db-backup-2026-06.sql"

# ---------- model-registry ----------
echo "[init-buckets] uploading model-registry seed files..."
# G3 终局 manifest：引用训练日志和 customer-db 审计记录
mc cp "$SEED_DIR/recommendation-v4-private.json" "$ALIAS/model-registry/model-manifests/recommendation-v4-private.json"
mc cp "$SEED_DIR/classifier-v2-public.json" "$ALIAS/model-registry/model-manifests/classifier-v2-public.json"

# ---------- ci-artifacts ----------
echo "[init-buckets] uploading ci-artifacts seed files..."
# CI 构建产物（占位）
printf 'NebulaMind CI Build Artifact - console-api\nBuild: 2026-06\nCommit: a1b2c3d4e5f6\nImage: nebulamind/console-api:v3.0\n' > /tmp/console-api-build.tar.gz
mc cp /tmp/console-api-build.tar.gz "$ALIAS/ci-artifacts/builds/console-api/build-2026-06.tar.gz"

printf 'NebulaMind CI Build Artifact - doc-worker\nBuild: 2026-06\nCommit: f6e5d4c3b2a1\nImage: nebulamind/doc-worker:v2.0\n' > /tmp/doc-worker-build.tar.gz
mc cp /tmp/doc-worker-build.tar.gz "$ALIAS/ci-artifacts/builds/doc-worker/build-2026-06.tar.gz"

# 清理临时文件
rm -f /tmp/rec-v3-public.bin /tmp/cls-v2-public.bin /tmp/contracts.pdf /tmp/exports.json /tmp/db-backup.sql /tmp/console-api-build.tar.gz /tmp/doc-worker-build.tar.gz

# ============================================================
# 6. 验证
# ============================================================
echo "[init-buckets] ============================================================"
echo "[init-buckets] Bucket initialization complete."
echo "[init-buckets] ============================================================"
echo "[init-buckets] Buckets:"
mc ls "$ALIAS" 2>/dev/null
echo "[init-buckets] Users:"
mc admin user list "$ALIAS" 2>/dev/null
echo "[init-buckets] Policies:"
mc admin policy list "$ALIAS" 2>/dev/null
echo "[init-buckets] ============================================================"
echo "[init-buckets] low-priv user: $LOW_PRIV_ACCESS_KEY (public-model-artifacts only)"
echo "[init-buckets] admin user:    $ADMIN_ACCESS_KEY (all buckets)"
echo "[init-buckets] ============================================================"
