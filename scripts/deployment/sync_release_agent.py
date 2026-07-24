import argparse
import base64
import hashlib
import os
from pathlib import Path

import paramiko


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def connect(host: str, password: str) -> paramiko.SSHClient:
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(
        host,
        username="whoami",
        password=password,
        timeout=10,
        look_for_keys=False,
        allow_agent=False,
    )
    return client


def run_root(client: paramiko.SSHClient, command: str, password: str) -> str:
    payload = base64.b64encode(command.encode()).decode()
    stdin, stdout, stderr = client.exec_command(
        f"sudo -S -p '' bash -c 'echo {payload} | base64 -d | bash'", timeout=90
    )
    stdin.write(password + "\n")
    stdin.flush()
    output = stdout.read().decode("utf-8", "replace")
    error = stderr.read().decode("utf-8", "replace")
    if stdout.channel.recv_exit_status() != 0:
        raise RuntimeError(error or output)
    return output


parser = argparse.ArgumentParser()
parser.add_argument("binary", type=Path)
parser.add_argument("hosts", nargs="+")
args = parser.parse_args()
binary = args.binary.resolve()
password = os.environ.get("GZCTF_SSH_PASSWORD")
if not password:
    raise SystemExit("GZCTF_SSH_PASSWORD is required")
expected = sha256(binary)

for host in args.hosts:
    client = connect(host, password)
    remote = f"/tmp/gzctf-agent.{expected[:16]}"
    sftp = client.open_sftp()
    try:
        sftp.put(str(binary), remote)
    finally:
        sftp.close()
    command = (
        f"test \"$(sha256sum {remote} | cut -d' ' -f1)\" = {expected} && "
        "cp -a /usr/local/bin/gzctf-agent /usr/local/bin/gzctf-agent.pre-phase9-qga && "
        f"install -m 0755 {remote} /usr/local/bin/gzctf-agent && rm -f {remote} && "
        "systemctl restart gzctf-agent.service && "
        "for i in $(seq 1 20); do systemctl is-active --quiet gzctf-agent.service && break; sleep 1; done && "
        f"test \"$(sha256sum /usr/local/bin/gzctf-agent | cut -d' ' -f1)\" = {expected} && "
        "systemctl is-active gzctf-agent.service"
    )
    print(host, run_root(client, command, password).strip(), expected)
    client.close()
