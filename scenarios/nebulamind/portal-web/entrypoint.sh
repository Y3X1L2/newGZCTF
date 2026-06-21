#!/bin/sh
set -e

# portal-web 入口脚本
# 职责：
# 1. 注入 A2 Flag 到旧版白皮书 HTML 注释中
# 2. 注入 A3 Flag 到 Source Map 的 sourcesContent 注释中
# 3. 启动 Python HTTP 服务

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_AI_CONSOLE_API_URL NM_AI_CONSOLE_API_HOST

# A2: 隐藏目录与历史白皮书
FLAG_A2="$(get_flag 'FLAG_PORTAL_HIDDEN_DOCS' 'flag{a2_hidden_docs_placeholder}')"

# A3: 前端 Source Map 泄露
FLAG_A3="$(get_flag 'FLAG_PORTAL_SOURCEMAP' 'flag{a3_sourcemap_placeholder}')"

# 使用 Python 进行安全的占位符替换（避免 sed 对 flag 中特殊字符的转义问题）
export FLAG_A2
export FLAG_A3
export NM_AI_CONSOLE_API_URL
export NM_AI_CONSOLE_API_HOST

python3 - <<'PYEOF'
import os

flag_a2 = os.environ["FLAG_A2"]
flag_a3 = os.environ["FLAG_A3"]
console_api_url = os.environ["NM_AI_CONSOLE_API_URL"].rstrip("/")
console_api_host = os.environ["NM_AI_CONSOLE_API_HOST"]

# A2: 替换白皮书中的占位符
whitepaper_path = "/app/static/resources/archive/nebulamind-whitepaper-v1.html"
with open(whitepaper_path, "r", encoding="utf-8") as f:
    content = f.read()
content = content.replace("__NM_FLAG_A2__", flag_a2)
content = content.replace("__NM_AI_CONSOLE_API_HOST__", console_api_host)
with open(whitepaper_path, "w", encoding="utf-8") as f:
    f.write(content)

# A3: 替换 Source Map 中的占位符
sourcemap_path = "/app/static/js/app.js.map"
with open(sourcemap_path, "r", encoding="utf-8") as f:
    content = f.read()
content = content.replace("__NM_FLAG_A3__", flag_a3)
content = content.replace("__NM_AI_CONSOLE_API_HOST__", console_api_host)
content = content.replace("Console bootstrap endpoint: /api/v1/console/session/bootstrap",
                          f"Console bootstrap endpoint: {console_api_url}/api/v1/console/session/bootstrap")
with open(sourcemap_path, "w", encoding="utf-8") as f:
    f.write(content)

bundle_path = "/app/static/js/app.js"
with open(bundle_path, "r", encoding="utf-8") as f:
    content = f.read()
content = content.replace("__NM_AI_CONSOLE_API_HOST__", console_api_host)
with open(bundle_path, "w", encoding="utf-8") as f:
    f.write(content)

print("[portal-web] A2 flag injected into whitepaper HTML comment")
print("[portal-web] A3 flag and console API runtime clues rendered")
PYEOF

echo "[portal-web] starting HTTP server on port 8080..."

exec "$@"
