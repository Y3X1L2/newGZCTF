"""Deploy newGZCTF to server, build, test, start."""
import paramiko, os, sys

IP = "203.195.157.191"
USER = "ubuntu"
PASS = "Fisher(1^"
ARCHIVE = r"C:\Users\87701\AppData\Local\Temp\gzctf-deploy.tar.gz"

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect(IP, username=USER, password=PASS, timeout=10)
print(f"[1/5] 上传代码...")
sftp = ssh.open_sftp()
sftp.put(ARCHIVE, "/home/ubuntu/gzctf.tar.gz")
sftp.close()

def run(cmd, timeout=300):
    _, out, err = ssh.exec_command(cmd, timeout=timeout)
    return out.read().decode(), err.read().decode()

print("[2/5] 解压...")
out, _ = run("rm -rf /home/ubuntu/newGZCTF && mkdir -p /home/ubuntu/newGZCTF && cd /home/ubuntu/newGZCTF && tar xzf ../gzctf.tar.gz && rm ../gzctf.tar.gz && echo OK")
print(out.strip())

print("[3/5] 编译...")
out, err = run("export PATH=/usr/local/share/dotnet:\$PATH && cd /home/ubuntu/newGZCTF && dotnet restore src/GZCTF.slnx --verbosity minimal 2>&1 | tail -3 && dotnet build src/GZCTF.slnx -c Release --no-restore 2>&1 | tail -5", timeout=600)
print(out[-500:] if len(out) > 500 else out)
if err: print("ERR:", err[-200:])

print("[4/5] 测试...")
out, err = run("export PATH=/usr/local/share/dotnet:\$PATH && cd /home/ubuntu/newGZCTF && dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore 2>&1 | tail -5", timeout=300)
print(out.strip())
if err: print("ERR:", err[-200:])

print("[5/5] 启动平台...")
out, _ = run("cd /home/ubuntu/newGZCTF && docker compose -f docker-compose.yml up -d --build 2>&1 | tail -10", timeout=600)
print(out.strip())

# Verify
out, _ = run("sleep 3 && curl -s http://localhost:8080/api/info 2>&1 | head -5", timeout=10)
print("API response:", out.strip()[:200])

ssh.close()
print("\n部署完成!")
