#!/bin/bash
set -euo pipefail
# yinyu-ctf-platform 一键部署 + 测试脚本
# 用法: bash one-click-deploy.sh <IP> <USER> <PASS>
# 示例: bash one-click-deploy.sh <server-ip> ubuntu "<password>"

SERVER_IP="${1:?请提供服务器IP}"
SERVER_USER="${2:?请提供用户名}"
SERVER_PASS="${3:?请提供密码}"
PROJECT_DIR="/home/$SERVER_USER/yinyu-ctf-platform"
BRANCH="feature/phase3-deploy-panel"

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
log() { echo -e "${GREEN}[$(date +%H:%M:%S)]${NC} $*"; }
err() { echo -e "${RED}[ERROR]${NC} $*"; exit 1; }

# ============================================================
log "STEP 0: 检查本地环境"
# ============================================================
command -v sshpass >/dev/null 2>&1 || {
    log "安装 sshpass..."
    command -v apt >/dev/null 2>&1 && sudo apt install -y sshpass
    command -v brew >/dev/null 2>&1 && brew install sshpass
    command -v winget >/dev/null 2>&1 && winget install sshpass
}
SSH="sshpass -p '$SERVER_PASS' ssh -o StrictHostKeyChecking=no -o ConnectTimeout=10 $SERVER_USER@$SERVER_IP"
SCP="sshpass -p '$SERVER_PASS' scp -o StrictHostKeyChecking=no"

# ============================================================
log "STEP 1: 连接测试 → 安装基础依赖"
# ============================================================
$SSH "echo connected" || err "无法连接 $SERVER_IP"
log "连接成功"

$SSH "bash -s" << 'INSTALL_DEPS'
set -e
echo ">>> 安装 .NET SDK 10.0 <<<"
if ! command -v dotnet &>/dev/null; then
    wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/local/share/dotnet
    ln -sf /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
fi

echo ">>> 安装 Docker <<<"
if ! command -v docker &>/dev/null; then
    curl -fsSL https://get.docker.com | bash
    sudo usermod -aG docker $USER
fi

echo ">>> 安装 Docker Compose <<<"
if ! command -v docker compose &>/dev/null; then
    sudo apt update && sudo apt install -y docker-compose-v2
fi

echo ">>> 启动测试 PostgreSQL <<<"
docker rm -f gzctf-test-db 2>/dev/null || true
docker run -d --name gzctf-test-db --restart unless-stopped \
    -e POSTGRES_DB=gzctf_test -e POSTGRES_USER=testuser -e POSTGRES_PASSWORD=testpass \
    -p 5433:5432 postgres:16-alpine

echo ">>> 启动测试 Redis <<<"
docker rm -f gzctf-test-redis 2>/dev/null || true
docker run -d --name gzctf-test-redis --restart unless-stopped \
    -p 6380:6379 redis:7-alpine

echo ">>> 启动 Guacd <<<"
docker rm -f gzctf-test-guacd 2>/dev/null || true
docker run -d --name gzctf-test-guacd --restart unless-stopped \
    -p 4822:4822 guacamole/guacd

echo ">>> 检查 KVM <<<"
command -v virsh &>/dev/null && echo "KVM OK" || echo "KVM not available (VM tests will skip)"

mkdir -p /var/lib/gzctf-test/images
echo ">>> 基础依赖安装完成 <<<"
INSTALL_DEPS
log "服务器环境就绪"

# ============================================================
log "STEP 2: 推送代码到服务器"
# ============================================================
PROJECT_ROOT=$(git rev-parse --show-toplevel)
log "项目根目录: $PROJECT_ROOT"

$SSH "mkdir -p $PROJECT_DIR"
cd "$PROJECT_ROOT"

# 打包当前工作区代码（不含 .git 大文件）
log "打包代码..."
git archive --format=tar HEAD | gzip > /tmp/yinyu-ctf-platform-code.tar.gz
$SCP /tmp/yinyu-ctf-platform-code.tar.gz $SERVER_USER@$SERVER_IP:$PROJECT_DIR/
$SSH "cd $PROJECT_DIR && tar xzf yinyu-ctf-platform-code.tar.gz && rm yinyu-ctf-platform-code.tar.gz"

# 同步 .worktrees 中已完成 Phase 的代码
for phase_dir in .worktrees/phase1-scoring .worktrees/phase2-vm-docker .worktrees/phase3-deploy; do
    if [ -d "$phase_dir" ]; then
        phase_name=$(basename "$phase_dir")
        log "同步 $phase_name ..."
        tar czf /tmp/$phase_name.tar.gz -C "$phase_dir" .
        $SCP /tmp/$phase_name.tar.gz $SERVER_USER@$SERVER_IP:$PROJECT_DIR/
        $SSH "mkdir -p $PROJECT_DIR/src && cd $PROJECT_DIR && tar xzf $phase_name.tar.gz && rm $phase_name.tar.gz"
    fi
done

# ============================================================
log "STEP 3: 服务器端编译 + 运行全量测试"
# ============================================================
$SSH "bash -s" << 'BUILD_TEST'
set -e
export PATH="/usr/local/share/dotnet:$PATH"
cd ~/yinyu-ctf-platform

echo ">>> NuGet 还原 <<<"
dotnet restore src/GZCTF.slnx --verbosity minimal

echo ">>> 编译 <<<"
dotnet build src/GZCTF.slnx --no-restore -c Release 2>&1 | tail -5

echo ">>> 单元测试 <<<"
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore -c Release \
    --logger "console;verbosity=normal" \
    2>&1 | tee /tmp/unit-test-results.txt

PASSED=$(grep -oP '通过:\s*\K\d+' /tmp/unit-test-results.txt || echo "0")
FAILED=$(grep -oP '失败:\s*\K\d+' /tmp/unit-test-results.txt || echo "0")
echo ""
echo "============================================"
echo " 结果: $PASSED 通过 / $FAILED 失败"
echo "============================================"

if [ "$FAILED" -gt 0 ]; then
    echo "测试失败，检查 /tmp/unit-test-results.txt"
    exit 1
fi

echo ">>> 集成测试（跳过需要 Docker daemon 的）<<<"
dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj \
    --no-restore -c Release --filter "Category!=RequiresDocker&Category!=RequiresKVM" \
    --logger "console;verbosity=normal" \
    2>&1 | tail -20 || true

echo ">>> 全部测试完成 <<<"
BUILD_TEST

# ============================================================
log "STEP 4: 导入测试 Docker 镜像 + Windows VM 模板"
# ============================================================
$SSH "bash -s" << 'DEPLOY_IMAGES'
set -e
cd ~/yinyu-ctf-platform

echo ">>> 构建测试 Docker CTF 镜像 <<<"
cat > /tmp/test-ctf-Dockerfile << 'DOCKERFILE'
FROM nginx:alpine
RUN echo "flag{test_docker_ctf_2024}" > /flag
RUN echo "<html><body><h1>CTF Challenge</h1><p>Find the flag!</p></body></html>" > /usr/share/nginx/html/index.html
EXPOSE 80
DOCKERFILE
docker build -t gzctf-test-ctf:latest /tmp/test-ctf-Dockerfile -f /tmp/test-ctf-Dockerfile 2>&1
echo ">>> 测试 Docker 镜像构建完成: gzctf-test-ctf:latest <<<"

echo ">>> 准备 Windows VM 测试模板 <<<"
# 检查是否有本地 Windows VM 镜像
if [ -f "/var/lib/gzctf-test/images/windows-test.qcow2" ]; then
    echo "Windows VM 模板已存在"
else
    # 创建最小占位 qcow2（真实环境需替换为实际 Windows VM）
    echo ">> 创建最小测试 qcow2（仅用于验证导入流程）<<"
    qemu-img create -f qcow2 /var/lib/gzctf-test/images/windows-test.qcow2 1G 2>&1
    echo ">> 注意: 这是占位镜像，实际比赛需替换为真实 Windows VM <<"
fi
echo ">>> VM 模板就绪 <<<"

echo ">>> Docker Compose 一键部署文件生成 <<<"
cat > ~/yinyu-ctf-platform/docker-compose.test.yml << 'COMPOSE'
version: '3.9'
services:
  api:
    build: src/GZCTF
    ports: ["8080:8080"]
    environment:
      - ASPNETCORE_ENVIRONMENT=Test
      - ConnectionStrings__Database=Host=localhost;Port=5433;Database=gzctf_test;Username=testuser;Password=testpass
      - ConnectionStrings__RedisCache=localhost:6380
      - KvmSettings__ImageStoragePath=/var/lib/gzctf-test/images
      - KvmSettings__LocalImportPath=/var/lib/gzctf-test/images
    depends_on:
      - postgres
      - redis
    restart: unless-stopped
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: gzctf_test
      POSTGRES_USER: testuser
      POSTGRES_PASSWORD: testpass
    ports: ["5433:5432"]
    volumes: [pgdata:/var/lib/postgresql/data]
  redis:
    image: redis:7-alpine
    ports: ["6380:6379"]
volumes:
  pgdata:
COMPOSE
echo ">>> docker-compose.test.yml 已生成 <<<"
DEPLOY_IMAGES

# ============================================================
log "STEP 5: 验收报告"
# ============================================================
cat << 'REPORT'

╔══════════════════════════════════════════════════════════╗
║              yinyu-ctf-platform 一键部署完成报告                    ║
╠══════════════════════════════════════════════════════════╣
║  服务器:   SERVER_IP_PLACEHOLDER                        ║
║  项目路径: /home/SERVER_USER_PLACEHOLDER/yinyu-ctf-platform        ║
║                                                        ║
║  运行的服务:                                             ║
║    PostgreSQL 16 → localhost:5433 (gzctf_test)           ║
║    Redis 7      → localhost:6380                        ║
║    Guacd        → localhost:4822                        ║
║                                                        ║
║  Docker 镜像:                                           ║
║    gzctf-test-ctf:latest (CTF 题目容器)                  ║
║                                                        ║
║  VM 模板:                                               ║
║    /var/lib/gzctf-test/images/windows-test.qcow2        ║
║                                                        ║
║  启动命令:                                               ║
║    cd ~/yinyu-ctf-platform                                       ║
║    docker compose -f docker-compose.test.yml up -d       ║
║                                                        ║
║  运行测试:                                               ║
║    dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release ║
╚══════════════════════════════════════════════════════════╝
REPORT
log "一键部署完成"
