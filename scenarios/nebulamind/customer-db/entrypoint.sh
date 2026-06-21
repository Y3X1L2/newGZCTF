#!/bin/sh
set -e

# customer-db 入口脚本
# 职责：
# 1. 读取 F1/F2/F3 Flag 环境变量与 admin 密码
# 2. 处理 /opt/nebulamind/init/*.sql 中的占位符（sed 替换 Flag 与密码）
# 3. 将处理后的 SQL 复制到 /docker-entrypoint-initdb.d/
# 4. 调用官方 postgres docker-entrypoint 启动数据库
#
# Flag 注入位置：
#   F1 (FLAG_DB_READONLY_CUSTOMERS)   -> security_findings.finding_details（占位符 __NM_FLAG_F1__）
#   F2 (FLAG_DB_PRIVESC_FUNCTION)     -> internal_exports.data_payload（占位符 __NM_FLAG_F2__）
#   F3 (FLAG_DB_CORE_CUSTOMER_DATA)   -> regulated_model_training_records.compliance_audit（占位符 __NM_FLAG_F3__）
#   admin 密码                         -> 04-permissions.sql 中 admin 角色密码（占位符 __NM_DB_ADMIN_PASSWORD__）

. /_shared/scripts/flag.sh

FLAG_F1="$(get_flag 'FLAG_DB_READONLY_CUSTOMERS' 'flag{f1_db_readonly_customers_placeholder}')"
FLAG_F2="$(get_flag 'FLAG_DB_PRIVESC_FUNCTION' 'flag{f2_db_privesc_function_placeholder}')"
FLAG_F3="$(get_flag 'FLAG_DB_CORE_CUSTOMER_DATA' 'flag{f3_db_core_customer_data_placeholder}')"

# admin 密码（F3 链路：选手需通过 Vault secret/nebulamind/db-credentials 或 CI 高权限变量获取）
ADMIN_PASSWORD="${NM_DB_ADMIN_PASSWORD:-nm_admin_dev_2026}"

echo "[customer-db] F1 flag available via env (FLAG_DB_READONLY_CUSTOMERS)"
echo "[customer-db] F2 flag available via env (FLAG_DB_PRIVESC_FUNCTION)"
echo "[customer-db] F3 flag available via env (FLAG_DB_CORE_CUSTOMER_DATA)"
echo "[customer-db] processing init SQL with flag placeholders..."

SRC_DIR="/opt/nebulamind/init"
DST_DIR="/docker-entrypoint-initdb.d"
mkdir -p "$DST_DIR"

# 转义 sed 替换串中的特殊字符（\ & /），避免破坏替换
esc() {
    printf '%s' "$1" | sed -e 's/[\\&]/\\&/g' -e 's|/|\\/|g'
}

F1_ESC="$(esc "$FLAG_F1")"
F2_ESC="$(esc "$FLAG_F2")"
F3_ESC="$(esc "$FLAG_F3")"
ADMIN_ESC="$(esc "$ADMIN_PASSWORD")"

for f in "$SRC_DIR"/*.sql; do
    [ -e "$f" ] || continue
    base="$(basename "$f")"
    out="$DST_DIR/$base"
    sed \
        -e "s/__NM_FLAG_F1__/$F1_ESC/g" \
        -e "s/__NM_FLAG_F2__/$F2_ESC/g" \
        -e "s/__NM_FLAG_F3__/$F3_ESC/g" \
        -e "s/__NM_DB_ADMIN_PASSWORD__/$ADMIN_ESC/g" \
        "$f" > "$out"
    chmod 0644 "$out"
    echo "[customer-db] processed $base -> $out"
done

echo "[customer-db] starting PostgreSQL on port 5432..."
echo "[customer-db] readonly creds: readonly / readonly_password_2026 (F1 entry, leak via object store/CI)"
echo "[customer-db] admin creds: admin / <NM_DB_ADMIN_PASSWORD> (F3, leak via Vault/CI high-priv)"

exec docker-entrypoint.sh "$@"
