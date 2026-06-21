#!/bin/sh
set -e

# ai-console-api 入口脚本
# 职责：
# 1. 注入 C1 Flag（IDOR）- 通过环境变量传给 app.py，注入到 id=17 知识库 description
# 2. 注入 C2 Flag（JWT 角色提升）- 通过环境变量传给 app.py，注入到审计日志 git.sync 事件
# 3. 注入 C3 Flag（GraphQL）- 通过环境变量传给 app.py，注入到 integration secrets flag 字段
# 4. 启动 Python Flask 服务

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_GIT_SERVICE_URL NM_OBJECT_STORE_URL

# C1: IDOR - Flag 通过环境变量传给 app.py
# app.py 通过 get_flag('FLAG_API_TENANT_IDOR') 读取，注入到 id=17 废弃知识库的 description
FLAG_C1="$(get_flag 'FLAG_API_TENANT_IDOR' 'flag{c1_tenant_idor_placeholder}')"
export GZCTF_FLAG_FLAG_API_TENANT_IDOR="$FLAG_C1"

# C2: JWT 弱密钥角色提升 - Flag 通过环境变量传给 app.py
# app.py 通过 get_flag('FLAG_API_JWT_ROLE') 读取，注入到审计日志 git.sync 事件 metadata
FLAG_C2="$(get_flag 'FLAG_API_JWT_ROLE' 'flag{c2_jwt_role_placeholder}')"
export GZCTF_FLAG_FLAG_API_JWT_ROLE="$FLAG_C2"

# C3: GraphQL introspection - Flag 通过环境变量传给 app.py
# app.py 通过 get_flag('FLAG_API_GRAPHQL_AUDIT') 读取，注入到 integration secrets flag 字段
FLAG_C3="$(get_flag 'FLAG_API_GRAPHQL_AUDIT' 'flag{c3_graphql_audit_placeholder}')"
export GZCTF_FLAG_FLAG_API_GRAPHQL_AUDIT="$FLAG_C3"

echo "[ai-console-api] C1 flag available via env (FLAG_API_TENANT_IDOR)"
echo "[ai-console-api] C2 flag available via env (FLAG_API_JWT_ROLE)"
echo "[ai-console-api] C3 flag available via env (FLAG_API_GRAPHQL_AUDIT)"
echo "[ai-console-api] JWT secret: nebulamind-dev-secret-2026 (weak dev secret)"
echo "[ai-console-api] starting Flask server on port 8080..."

exec "$@"
