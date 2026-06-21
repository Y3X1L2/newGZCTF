#!/bin/sh
set -e

# support-upload 入口脚本
# 职责：
# 1. 注入 B1 Flag 到 /app/config/b1_flag.txt（app.py 读取此文件生成解析报告）
# 2. 注入 B2 Flag 到 /app/config/worker.yml（路径穿越目标文件）
# 3. 启动 Python Flask 服务

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_DOCUMENT_WORKER_URL NM_DOCUMENT_WORKER_HOST NM_CACHE_BROKER_HOST NM_AI_CONSOLE_API_URL

# B1: 文件上传 MIME 绕过 - Flag 注入到 b1_flag.txt
# app.py 读取此文件，将 Flag 写入解析日志的"诊断令牌"字段
FLAG_B1="$(get_flag 'FLAG_UPLOAD_MIME_BYPASS' 'flag{b1_mime_bypass_placeholder}')"
printf '%s\n' "$FLAG_B1" > /app/config/b1_flag.txt
chmod 644 /app/config/b1_flag.txt

# B2: 路径穿越读取 worker.yml - Flag 注入到 worker.yml 占位符
FLAG_B2="$(get_flag 'FLAG_UPLOAD_PATH_TRAVERSAL' 'flag{b2_path_traversal_placeholder}')"
export FLAG_B2

python3 - <<'PYEOF'
import os

flag_b2 = os.environ["FLAG_B2"]
config_path = "/app/config/worker.yml"
replacements = {
    "__NM_DOCUMENT_WORKER_URL__": os.environ["NM_DOCUMENT_WORKER_URL"].rstrip("/"),
    "__NM_DOCUMENT_WORKER_HOST__": os.environ["NM_DOCUMENT_WORKER_HOST"],
    "__NM_CACHE_BROKER_HOST__": os.environ["NM_CACHE_BROKER_HOST"],
    "__NM_AI_CONSOLE_API_URL__": os.environ["NM_AI_CONSOLE_API_URL"].rstrip("/"),
}

with open(config_path, "r", encoding="utf-8") as f:
    content = f.read()

content = content.replace("__NM_FLAG_B2__", flag_b2)
for placeholder, value in replacements.items():
    content = content.replace(placeholder, value)

with open(config_path, "w", encoding="utf-8") as f:
    f.write(content)

print("[support-upload] B1 flag injected into /app/config/b1_flag.txt")
print("[support-upload] B2 flag injected into /app/config/worker.yml")
PYEOF

echo "[support-upload] starting Flask server on port 8080..."

exec "$@"
