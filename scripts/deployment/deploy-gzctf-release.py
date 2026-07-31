#!/usr/bin/env python3
import argparse
import hashlib
import os
from pathlib import Path
import shlex
import sys
from urllib.parse import unquote, urlparse

import paramiko


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def create_socks5_socket(proxy_url: str, host: str, port: int):
    try:
        import socks
    except ImportError as exception:
        raise RuntimeError("SOCKS5 deployment requires PySocks (install with: python -m pip install PySocks)") from exception

    parsed = urlparse(proxy_url)
    if parsed.scheme.lower() != "socks5" or not parsed.hostname or not parsed.port:
        raise ValueError("--socks5 must be a socks5://[username:password@]host:port URL")
    socket = socks.socksocket()
    socket.set_proxy(
        socks.SOCKS5,
        parsed.hostname,
        parsed.port,
        username=unquote(parsed.username) if parsed.username else None,
        password=unquote(parsed.password) if parsed.password else None,
    )
    socket.settimeout(20)
    socket.connect((host, port))
    return socket


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("archive", type=Path)
    parser.add_argument("--host", default=os.environ.get("GZCTF_DEPLOY_HOST"))
    parser.add_argument("--user", default=os.environ.get("GZCTF_DEPLOY_USER", "whoami"))
    parser.add_argument("--password", default=os.environ.get("GZCTF_DEPLOY_PASSWORD"))
    parser.add_argument("--key-file", type=Path, default=os.environ.get("GZCTF_DEPLOY_KEY_FILE"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("GZCTF_DEPLOY_PORT", "22")))
    parser.add_argument("--socks5", default=os.environ.get("GZCTF_DEPLOY_SOCKS5"))
    parser.add_argument("--release-id")
    parser.add_argument("--plan-only", action="store_true")
    args = parser.parse_args()

    archive = args.archive.resolve()
    if not archive.is_file():
        parser.error(f"archive does not exist: {archive}")
    release_id = args.release_id or archive.name.removesuffix(".tar.gz")
    digest = sha256(archive)
    remote = f"/opt/gzctf/incoming/{release_id}.tar.gz"
    print(f"release={release_id} archive={archive} sha256={digest} remote={remote}")
    if args.plan_only:
        return 0
    if not args.host or not args.password:
        parser.error("host and sudo password are required through arguments or GZCTF_DEPLOY_* environment variables")
    if args.port not in range(1, 65536):
        parser.error("port must be between 1 and 65535")
    if args.key_file and not args.key_file.is_file():
        parser.error(f"key file does not exist: {args.key_file}")

    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    proxy_socket = create_socks5_socket(args.socks5, args.host, args.port) if args.socks5 else None
    client.connect(
        args.host,
        port=args.port,
        username=args.user,
        password=args.password,
        key_filename=str(args.key_file) if args.key_file else None,
        sock=proxy_socket,
        timeout=20,
        banner_timeout=20,
        auth_timeout=20,
        look_for_keys=False,
        allow_agent=False,
    )
    prepare = "sudo -S -p '' install -d -m 0750 -o " + shlex.quote(args.user) + \
              " -g " + shlex.quote(args.user) + " /opt/gzctf/incoming"
    stdin, stdout, stderr = client.exec_command(prepare, timeout=20)
    stdin.write(args.password + "\n")
    stdin.flush()
    if stdout.channel.recv_exit_status() != 0:
        raise RuntimeError(stderr.read().decode("utf-8", "replace"))

    sftp = client.open_sftp()
    try:
        try:
            offset = sftp.stat(remote).st_size
        except FileNotFoundError:
            offset = 0
        if offset > archive.stat().st_size:
            sftp.remove(remote)
            offset = 0
        with archive.open("rb") as source, sftp.open(remote, "ab" if offset else "wb") as target:
            target.set_pipelined(True)
            source.seek(offset)
            while chunk := source.read(1024 * 1024):
                target.write(chunk)
    finally:
        sftp.close()

    local_script = Path(__file__).with_name("activate-gzctf-release.sh")
    remote_script = "/tmp/activate-gzctf-release.sh"
    sftp = client.open_sftp()
    try:
        sftp.put(str(local_script), remote_script)
    finally:
        sftp.close()
    command = "sudo -S -p '' bash " + " ".join(map(shlex.quote, [
        remote_script, release_id, remote, digest
    ]))
    stdin, stdout, stderr = client.exec_command(command, timeout=180)
    stdin.write(args.password + "\n")
    stdin.flush()
    out = stdout.read().decode("utf-8", "replace")
    err = stderr.read().decode("utf-8", "replace")
    status = stdout.channel.recv_exit_status()
    client.close()
    if out.strip():
        print(out.strip())
    if status != 0:
        print(err.strip(), file=sys.stderr)
        return status
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
