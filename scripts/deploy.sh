#!/bin/bash
# 一键部署脚本 — 在 Git Bash 终端中手动执行
# 用法: bash scripts/deploy.sh
set -e

HOST="${YINYU_DEPLOY_HOST:?Set YINYU_DEPLOY_HOST}"
USER="${YINYU_DEPLOY_USER:-ubuntu}"
REMOTE_ROOT="${YINYU_REMOTE_ROOT:-/home/$USER/yinyu-ctf-platform}"
BASE="$REMOTE_ROOT/src/GZCTF"
LOCAL_DLL="${YINYU_LOCAL_DLL:-D:/newGZ/yinyu-ctf-platform/src/GZCTF/bin/Release/net10.0/GZCTF.dll}"
LOCAL_BUILD="${YINYU_LOCAL_BUILD:-D:/newGZ/yinyu-ctf-platform/src/GZCTF/ClientApp/build}"

echo "=== YINYU CTF Deploy ==="
echo ""

echo "[1/4] Stopping server..."
ssh $USER@$HOST "sudo pkill -9 -f GZCTF 2>/dev/null; sleep 1; echo 'stopped'"
echo ""

echo "[2/4] Uploading backend DLL..."
scp "$LOCAL_DLL" $USER@$HOST:$BASE/bin/Release/net10.0/GZCTF.dll
echo "done"
echo ""

echo "[3/4] Uploading frontend..."
scp "$LOCAL_BUILD/index.html" $USER@$HOST:$BASE/wwwroot/index.html
ssh $USER@$HOST "mkdir -p $BASE/wwwroot/static"
scp "$LOCAL_BUILD/static/"* $USER@$HOST:$BASE/wwwroot/static/
echo "done"
echo ""

echo "[4/4] Starting server..."
ssh $USER@$HOST "cd $BASE && \
  ASPNETCORE_URLS=http://0.0.0.0:8080 \
  ASPNETCORE_ENVIRONMENT=Production \
  YES_I_KNOW_FILES_ARE_NOT_PERSISTED_GO_AHEAD_PLEASE=1 \
  nohup /usr/local/share/dotnet/dotnet $BASE/bin/Release/net10.0/GZCTF.dll \
  > /tmp/gzctf.log 2>&1 &"
sleep 3
echo "done"
echo ""

echo "=== Checking ==="
ssh $USER@$HOST "ps aux | grep GZCTF | grep -v grep | head -1"
echo ""
echo "=== DEPLOY COMPLETE ==="
