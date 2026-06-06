#!/usr/bin/env python3
""" newGZCTF 一键部署 + 测试脚本 (Windows/Linux 通用)
用法: python one-click-deploy.py <IP> <USER> <PASSWORD>
示例: python one-click-deploy.py 203.195.157.191 ubuntu "Fisher(1^"
"""
import sys, os, paramiko, subprocess, tempfile, time, json

def log(msg): print(f"\033[32m[{time.strftime('%H:%M:%S')}]\033[0m {msg}")
def warn(msg): print(f"\033[33m[WARN]\033[0m {msg}")
def die(msg): print(f"\033[31m[ERROR]\033[0m {msg}"); sys.exit(1)

if len(sys.argv) < 4:
    die("用法: python one-click-deploy.py <IP> <USER> <PASSWORD>")

IP, USER, PASS = sys.argv[1], sys.argv[2], sys.argv[3]
HOME = f"/home/{USER}"
PROJECT_DIR = f"{HOME}/newGZCTF"

# ============================================================
log("STEP 0: 安装 paramiko")
# ============================================================
try:
    import paramiko
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "paramiko", "-q"])
    import paramiko

def ssh_run(ssh, cmd, timeout=300):
    """非交互执行远程命令，返回 (stdout, stderr, exit_code)"""
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=timeout)
    return stdout.read().decode(), stderr.read().decode(), stdout.channel.recv_exit_status()

def scp_put(ssh, local_path, remote_path):
    sftp = ssh.open_sftp()
    sftp.put(local_path, remote_path)
    sftp.close()

# ============================================================
log("STEP 1: 连接服务器")
# ============================================================
ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
try:
    ssh.connect(IP, username=USER, password=PASS, timeout=15)
    log(f"连接成功: {IP}")
except Exception as e:
    die(f"无法连接 {IP}: {e}")

# ============================================================
log("STEP 2: 安装基础依赖 (.NET / Docker / KVM)")
# ============================================================
deps_script = r"""
set -e
echo ">>> .NET SDK 10.0 <<<"
if ! command -v dotnet &>/dev/null; then
    wget -q https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/local/share/dotnet
    sudo ln -sf /usr/local/share/dotnet/dotnet /usr/local/bin/dotnet
fi
echo "DOTNET: $(dotnet --version)"

echo ">>> Docker <<<"
if ! command -v docker &>/dev/null; then
    curl -fsSL https://get.docker.com | sudo bash
    sudo usermod -aG docker $USER
fi
echo "DOCKER: $(docker --version)"

echo ">>> Docker Compose <<<"
sudo apt update -qq && sudo apt install -y -qq docker-compose-v2 2>/dev/null || true

echo ">>> PostgreSQL 测试 DB <<<"
docker rm -f gzctf-test-db 2>/dev/null || true
docker run -d --name gzctf-test-db --restart unless-stopped \
    -e POSTGRES_DB=gzctf_test -e POSTGRES_USER=testuser -e POSTGRES_PASSWORD=testpass \
    -p 5433:5432 postgres:16-alpine

echo ">>> Redis <<<"
docker rm -f gzctf-test-redis 2>/dev/null || true
docker run -d --name gzctf-test-redis --restart unless-stopped \
    -p 6380:6379 redis:7-alpine

echo ">>> Guacd <<<"
docker rm -f gzctf-test-guacd 2>/dev/null || true
docker run -d --name gzctf-test-guacd --restart unless-stopped \
    -p 4822:4822 guacamole/guacd

mkdir -p /var/lib/gzctf-test/images
echo ">>> 基础依赖完成 <<<"
"""
out, err, code = ssh_run(ssh, deps_script, timeout=600)
log("依赖安装完成")
if code != 0: warn(f"部分失败: {err[:200]}")

# ============================================================
log("STEP 3: 打包并上传代码")
# ============================================================
PROJECT_ROOT = os.getcwd()
tgz_path = "/tmp/newGZCTF-code.tar.gz"

# 打包
import tarfile, io
buf = io.BytesIO()
with tarfile.open(fileobj=buf, mode='w:gz') as tar:
    for root, dirs, files in os.walk(PROJECT_ROOT):
        dirs[:] = [d for d in dirs if d not in ('.git','.worktrees','node_modules','obj','bin','.nuget')]
        for f in files:
            full = os.path.join(root, f)
            arc = os.path.relpath(full, PROJECT_ROOT)
            try:
                tar.add(full, arc)
            except (FileNotFoundError, OSError):
                pass

log(f"代码打包: {buf.tell()//1024//1024}MB")
ssh.exec_command(f"mkdir -p {PROJECT_DIR}")

# 通过 SFTP 传文件
sftp = ssh.open_sftp()
import stat
for root, dirs, files in os.walk(PROJECT_ROOT):
    dirs[:] = [d for d in dirs if d not in ('.git','.worktrees','node_modules','obj','bin','.nuget')]
    remote_root = os.path.join(PROJECT_DIR, os.path.relpath(root, PROJECT_ROOT))
    for d in dirs:
        try: sftp.mkdir(os.path.join(remote_root, d))
        except: pass
    for f in files:
        local = os.path.join(root, f)
        remote = os.path.join(remote_root, f)
        try:
            sftp.put(local, remote)
        except (FileNotFoundError, OSError):
            pass
sftp.close()
log("代码上传完成")

# ============================================================
log("STEP 4: 服务器端编译 + 全量测试")
# ============================================================
test_script = f"""
set -e
export PATH="/usr/local/share/dotnet:$PATH"
cd {PROJECT_DIR}

echo ">>> NuGet 还原 <<<"
dotnet restore src/GZCTF.slnx --verbosity minimal 2>&1 | tail -3

echo ">>> 编译 <<<"
dotnet build src/GZCTF.slnx --no-restore -c Release 2>&1 | tail -5

echo ">>> 单元测试 <<<"
dotnet test src/GZCTF.Test/GZCTF.Test.csproj --no-restore -c Release \
    --logger "console;verbosity=normal" 2>&1 | tee /tmp/unit-results.txt

PASSED=$(grep -oP '通过:\\s*\\K\\d+' /tmp/unit-results.txt || echo "0")
FAILED=$(grep -oP '失败:\\s*\\K\\d+' /tmp/unit-results.txt || echo "0")
echo "============================================"
echo " 单元测试: $PASSED 通过 / $FAILED 失败"
echo "============================================"
if [ "$FAILED" -gt 0 ]; then exit 1; fi
"""
out, err, code = ssh_run(ssh, test_script, timeout=900)
print(out[-2000:] if len(out) > 2000 else out)
if code != 0:
    warn(f"测试有失败: {err[:500]}")

# ============================================================
log("STEP 5: 导入测试 Docker 镜像 + VM 模板")
# ============================================================
image_script = f"""
set -e
cd {PROJECT_DIR}

echo ">>> 构建测试 Docker CTF 镜像 <<<"
docker build -t gzctf-test-ctf:latest - <<'DOCKERFILE'
FROM nginx:alpine
RUN echo "flag{{test_docker_ctf_2024}}" > /flag
RUN echo "<html><body><h1>CTF Challenge</h1><p>Find the flag!</p></body></html>" > /usr/share/nginx/html/index.html
EXPOSE 80
DOCKERFILE
echo ">>> Docker 镜像: gzctf-test-ctf:latest <<<"

echo ">>> 准备 Windows VM 测试模板 <<<"
if ! command -v qemu-img &>/dev/null; then
    sudo apt install -y -qq qemu-utils
fi
if [ ! -f "/var/lib/gzctf-test/images/windows-test.qcow2" ]; then
    qemu-img create -f qcow2 /var/lib/gzctf-test/images/windows-test.qcow2 1G
    echo ">>> 测试 VM 模板已创建 <<<"
fi

echo ">>> 生成 Docker Compose 编排 <<<"
cat > {PROJECT_DIR}/docker-compose.test.yml << 'COMPOSE'
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
    depends_on: [postgres, redis]
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
"""
out, err, code = ssh_run(ssh, image_script, timeout=300)
log("测试环境就绪")

# ============================================================
log("STEP 6: 验收报告")
# ============================================================
# 获取测试结果
out, _, _ = ssh_run(ssh, "cat /tmp/unit-results.txt 2>/dev/null | tail -3 || echo '待运行'")
print(f"""
╔══════════════════════════════════════════════════════════╗
║           newGZCTF 一键部署完成报告                       ║
╠══════════════════════════════════════════════════════════╣
║  服务器:   {IP:<45} ║
║  项目路径: {PROJECT_DIR:<43} ║
║                                                        ║
║  运行的服务:                                             ║
║    PostgreSQL → {IP}:5433 (gzctf_test)          ║
║    Redis      → {IP}:6380                       ║
║    Guacd      → {IP}:4822                       ║
║                                                        ║
║  Docker 镜像: gzctf-test-ctf:latest                     ║
║  VM 模板:    /var/lib/gzctf-test/images/               ║
║                                                        ║
║  启动: docker compose -f docker-compose.test.yml up -d   ║
║  测试: dotnet test src/GZCTF.Test/GZCTF.Test.csproj     ║
╚══════════════════════════════════════════════════════════╝
""")

# 清理
ssh.close()
log("一键部署完成")
