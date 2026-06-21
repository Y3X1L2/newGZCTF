#!/usr/bin/env bash
set -euo pipefail

if command -v curl >/dev/null 2>&1 &&
   curl -sf http://127.0.0.1:9000/minio/health/live >/dev/null 2>&1; then
    exit 0
fi

export MC_CONFIG_DIR="${MC_CONFIG_DIR:-/tmp/.mc-hc}"
mkdir -p "$MC_CONFIG_DIR" 2>/dev/null || true

if mc alias set hc http://127.0.0.1:9000 "${MINIO_ROOT_USER:-minioadmin}" "${MINIO_ROOT_PASSWORD:-minioadmin}" >/dev/null 2>&1 &&
   mc admin info hc >/dev/null 2>&1; then
    exit 0
fi

exit 1
