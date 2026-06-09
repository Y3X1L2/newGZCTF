"""Deploy YINYU CTF platform to a remote server, build, test, and start it."""
import os
import sys

import paramiko


IP = os.environ.get("YINYU_DEPLOY_HOST")
USER = os.environ.get("YINYU_DEPLOY_USER", "ubuntu")
PASS = os.environ.get("YINYU_DEPLOY_PASS")
REMOTE_ROOT = os.environ.get("YINYU_REMOTE_ROOT", f"/home/{USER}/yinyu-ctf-platform")
REMOTE_ARCHIVE = os.environ.get("YINYU_REMOTE_ARCHIVE", f"/home/{USER}/yinyu-ctf-platform.tar.gz")
ARCHIVE = os.environ.get(
    "YINYU_DEPLOY_ARCHIVE",
    r"C:\Users\87701\AppData\Local\Temp\yinyu-ctf-platform.tar.gz",
)

if not IP or not PASS:
    print("Set YINYU_DEPLOY_HOST and YINYU_DEPLOY_PASS before running this script.", file=sys.stderr)
    sys.exit(2)


ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect(IP, username=USER, password=PASS, timeout=10)


def run(cmd, timeout=300):
    _, out, err = ssh.exec_command(cmd, timeout=timeout)
    return out.read().decode(), err.read().decode()


print("[1/5] Uploading archive...")
sftp = ssh.open_sftp()
sftp.put(ARCHIVE, REMOTE_ARCHIVE)
sftp.close()

print("[2/5] Extracting...")
out, _ = run(
    f"rm -rf {REMOTE_ROOT} && mkdir -p {REMOTE_ROOT} && "
    f"cd {REMOTE_ROOT} && tar xzf {REMOTE_ARCHIVE} && rm {REMOTE_ARCHIVE} && echo OK"
)
print(out.strip())

print("[3/5] Building...")
out, err = run(
    f"export PATH=/usr/local/share/dotnet:$PATH && cd {REMOTE_ROOT} && "
    "dotnet restore src/GZCTF.slnx --verbosity minimal 2>&1 | tail -3 && "
    "dotnet build src/GZCTF.slnx -c Release --no-restore 2>&1 | tail -5",
    timeout=600,
)
print(out[-500:] if len(out) > 500 else out)
if err:
    print("ERR:", err[-200:])

print("[4/5] Testing...")
out, err = run(
    f"export PATH=/usr/local/share/dotnet:$PATH && cd {REMOTE_ROOT} && "
    "dotnet test src/GZCTF.Test/GZCTF.Test.csproj -c Release --no-restore 2>&1 | tail -5",
    timeout=300,
)
print(out.strip())
if err:
    print("ERR:", err[-200:])

print("[5/5] Starting platform...")
out, _ = run(
    f"cd {REMOTE_ROOT} && docker compose -f docker-compose.yml up -d --build 2>&1 | tail -10",
    timeout=600,
)
print(out.strip())

out, _ = run("sleep 3 && curl -s http://localhost:8080/api/info 2>&1 | head -5", timeout=10)
print("API response:", out.strip()[:200])

ssh.close()
print("\nDeploy complete.")
