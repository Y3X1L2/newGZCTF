import argparse
import base64
import hashlib
import os
from pathlib import Path

import paramiko


parser = argparse.ArgumentParser()
parser.add_argument("dll", type=Path)
parser.add_argument("--host", default="10.0.7.118")
args = parser.parse_args()
dll = args.dll.resolve()
password = os.environ.get("GZCTF_SSH_PASSWORD")
if not password:
    raise SystemExit("GZCTF_SSH_PASSWORD is required")
digest = hashlib.sha256(dll.read_bytes()).hexdigest()
remote = f"/tmp/GZCTF.{digest[:16]}.dll"

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
client.connect(
    args.host,
    username="whoami",
    password=password,
    timeout=10,
    look_for_keys=False,
    allow_agent=False,
)
sftp = client.open_sftp()
try:
    sftp.put(str(dll), remote)
finally:
    sftp.close()

command = (
    f"test \"$(sha256sum {remote} | cut -d' ' -f1)\" = {digest} && "
    "cp -a /opt/gzctf/publish/GZCTF.dll /opt/gzctf/publish/GZCTF.dll.pre-api-docs && "
    f"install -m 0644 {remote} /opt/gzctf/publish/GZCTF.dll && rm -f {remote} && "
    "systemctl restart gzctf.service && "
    "for i in $(seq 1 30); do "
    "systemctl is-active --quiet gzctf.service && "
    "curl -fsS --max-time 3 http://127.0.0.1:8080/api-docs >/dev/null && break; sleep 1; done && "
    f"test \"$(sha256sum /opt/gzctf/publish/GZCTF.dll | cut -d' ' -f1)\" = {digest} && "
    "systemctl is-active gzctf.service"
)
payload = base64.b64encode(command.encode()).decode()
stdin, stdout, stderr = client.exec_command(
    f"sudo -S -p '' bash -c 'echo {payload} | base64 -d | bash'", timeout=60
)
stdin.write(password + "\n")
stdin.flush()
output = stdout.read().decode("utf-8", "replace")
error = stderr.read().decode("utf-8", "replace")
status = stdout.channel.recv_exit_status()
client.close()
print(output.strip())
if status != 0:
    raise SystemExit(error or status)
print(digest)
