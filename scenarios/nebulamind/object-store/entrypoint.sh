#!/usr/bin/env bash
set -euo pipefail

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_CUSTOMER_DB_HOST NM_MODEL_REGISTRY_URL NM_OBJECT_STORE_URL

SEED_DIR="/opt/nebulamind/seed"
PROCESSED_DIR="/opt/nebulamind/seed-processed"
INIT_MARKER="/data/.nebulamind-initialized"

FLAG_D2="$(get_flag 'FLAG_OBJECT_BUCKET_POLICY' 'flag{d2_object_bucket_policy_placeholder}')"
MODEL_REGISTRY_URL="${NM_MODEL_REGISTRY_URL%/}"
OBJECT_STORE_URL="${NM_OBJECT_STORE_URL%/}"

render_seed() {
    local src="$1"
    local dst="$2"
    local content
    content="$(cat "$src")"
    content="${content//__NM_FLAG_D2__/$FLAG_D2}"
    content="${content//__NM_CUSTOMER_DB_HOST__/$NM_CUSTOMER_DB_HOST}"
    content="${content//__NM_MODEL_REGISTRY_URL__/$MODEL_REGISTRY_URL}"
    content="${content//__NM_OBJECT_STORE_URL__/$OBJECT_STORE_URL}"
    printf '%s\n' "$content" > "$dst"
}

prepare_seed_files() {
    mkdir -p "$PROCESSED_DIR"
    render_seed "$SEED_DIR/tenant-summary-2026.csv" "$PROCESSED_DIR/tenant-summary-2026.csv"
    render_seed "$SEED_DIR/recommendation-v4-private-train.log" "$PROCESSED_DIR/recommendation-v4-private-train.log"
    render_seed "$SEED_DIR/recommendation-v4-private.json" "$PROCESSED_DIR/recommendation-v4-private.json"
    render_seed "$SEED_DIR/classifier-v2-public.json" "$PROCESSED_DIR/classifier-v2-public.json"
    render_seed "$SEED_DIR/model-cards-README.md" "$PROCESSED_DIR/model-cards-README.md"
}

wait_for_minio() {
    local i
    for i in $(seq 1 60); do
        if mc alias set hc http://127.0.0.1:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null 2>&1 &&
           mc admin info hc >/dev/null 2>&1; then
            return 0
        fi

        if command -v curl >/dev/null 2>&1 &&
           curl -sf http://127.0.0.1:9000/minio/health/live >/dev/null 2>&1; then
            return 0
        fi

        sleep 1
    done
    return 1
}

cleanup_background_minio() {
    if [ -n "${MINIO_PID:-}" ]; then
        kill "$MINIO_PID" 2>/dev/null || true
        wait "$MINIO_PID" 2>/dev/null || true
    fi
}

echo "[object-store] rendering platform-assigned service addresses and flags"
prepare_seed_files

export MINIO_ROOT_USER="${MINIO_ROOT_USER:-nm-root-admin}"
export MINIO_ROOT_PASSWORD="${MINIO_ROOT_PASSWORD:-nm-root-admin-secret-2026}"
mkdir -p /data

if [ -f "$INIT_MARKER" ]; then
    echo "[object-store] data directory already initialized, skipping bucket setup"
    exec minio server /data --console-address ":9001"
fi

echo "[object-store] starting MinIO in background for initialization"
minio server /data --console-address ":9001" >/tmp/minio-init.log 2>&1 &
MINIO_PID=$!
trap cleanup_background_minio EXIT

if ! wait_for_minio; then
    echo "[object-store] ERROR: MinIO did not become healthy within 60s" >&2
    cat /tmp/minio-init.log >&2 || true
    exit 1
fi

echo "[object-store] MinIO is healthy, initializing buckets"
export PROCESSED_DIR
/init-buckets.sh

touch "$INIT_MARKER"
echo "[object-store] initialization marker created at $INIT_MARKER"

cleanup_background_minio
MINIO_PID=""
trap - EXIT
sleep 2

echo "[object-store] starting MinIO in foreground"
echo "[object-store] listening on 0.0.0.0:9000 (S3 API) and 0.0.0.0:9001 (Console)"
echo "[object-store] D2 flag in: public-model-artifacts/exports/tenant-summary-2026.csv"
echo "[object-store] G3 clues in: public-model-artifacts/training-logs/recommendation-v4-private-train.log"

exec minio server /data --console-address ":9001"
