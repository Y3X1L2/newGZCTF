import argparse
import base64
import os
import paramiko
import sys


sys.stdout.reconfigure(encoding="utf-8")

COMMANDS = {
    "runtime-final": """journalctl -u gzctf.service --since '2026-07-18 17:28:00' --no-pager 2>/dev/null | grep -B 35 -A 90 -Ei '019f748e-c828-7d8b-adfb-28db2cf881c2|019f748e-c8b6-7886-b8d6-5d0825982fd4|Runtime ticket .* failed' | tail -320
""",
    "runtime-agent": """journalctl -u gzctf-agent.service --since '2026-07-18 16:39:00' --no-pager 2>/dev/null | grep -Ei '019f7462-146e-739a-8f69-f41a7fff0da7|teamlab|vm|container|failed|error' | tail -260
""",
    "db-docker-templates": """docker exec gzctf-postgres psql -U postgres -d gzctf -P pager=off -c 'select "Id", "Name", "RegistryUrl", "ImageHash", "Description", "VmRuntimeMode", "VmNetworkMode" from "ImageTemplates" where "Id" in (21,22,34,69) order by "Id";'
""",
    "linux-image-network": """set -eu
command -v virt-cat || true
command -v guestfish || true
for path in /etc/netplan/50-cloud-init.yaml /etc/cloud/cloud.cfg.d/99-disable-network-config.cfg /var/lib/cloud/instance/obj.pkl; do
  echo ===$path===
  virt-cat -a /var/lib/gzctf/images/34.qcow2 "$path" 2>&1 | head -80 || true
done
""",
    "runtime-network-facts": """echo ===DOMAINS===
virsh list --all
for domain in $(virsh list --all --name | grep '^tl' || true); do
  echo ===DOMAIN:$domain===
  virsh domiflist "$domain" || true
  virsh qemu-agent-command "$domain" --timeout 3 '{"execute":"guest-network-get-interfaces"}' 2>&1 | head -c 4000 || true
  echo
done
echo ===DHCP===
find /run/gzctf-teamlab -type f \( -name dhcp-hosts -o -name leases -o -name dnsmasq.log \) -print -exec sh -c 'echo ---$1; tail -80 "$1"' sh {} \; 2>/dev/null || true
echo ===NAMESPACES===
ip netns list || true
echo ===LINKS===
ip -br link | grep -E 'tl|tap|vnet' || true
echo ===FDB===
bridge fdb show | grep -E 'tap|vnet|tl' || true
""",
    "main": """echo HOST=$(hostname)
systemctl is-active gzctf.service
docker ps --format '{{.Names}} {{.Image}} {{.Status}}'
find /opt/gzctf/publish /var/lib/gzctf/image-factory -maxdepth 4 -type f \
  \( -iname '*.json' -o -iname '*.yml' -o -iname '*.yaml' -o -iname '*.xml' -o -iname '*.ps1' -o -iname '*.txt' \) \
  -print0 2>/dev/null | xargs -0 grep -nEi \
  'administrator|player|password|credential|winrm|cloudbase' 2>/dev/null | head -200
""",
    "worker": """echo HOST=$(hostname)
systemctl is-active gzctf-agent.service
ls -lh /var/lib/gzctf/images | tail -40
virsh list --all
for domain in $(virsh list --all --name); do
  echo ===DOMAIN:$domain===
  virsh qemu-agent-command "$domain" '{"execute":"guest-info"}' 2>&1 | head -c 500
  echo
done
find /var/lib/gzctf/image-factory /var/lib/gzctf/images -maxdepth 3 -type f \
  \( -iname '*.json' -o -iname '*.xml' -o -iname '*.ps1' -o -iname '*.txt' \) \
  -print0 2>/dev/null | xargs -0 grep -nEi \
  'administrator|player|password|credential|winrm|cloudbase' 2>/dev/null | head -200
""",
    "factory-qga": """set -eu
domain=gzprep-qga-probe
overlay=/tmp/gzctf-qga-probe.qcow2
source=/var/lib/gzctf/images/phase9-win2022-qga-ready.qcow2
virsh destroy "$domain" >/dev/null 2>&1 || true
virsh undefine "$domain" >/dev/null 2>&1 || true
rm -f "$overlay"
test -s "$source"
qemu-img create -q -f qcow2 -F qcow2 -b "$source" "$overlay"
chmod 666 "$overlay"
virt-install --name "$domain" --memory 4096 --vcpus 2 --cpu host-passthrough \
  --disk path="$overlay",format=qcow2 \
  --network none \
  --channel unix,target.type=virtio,target.name=org.qemu.guest_agent.0 \
  --osinfo detect=on,require=off --import --noautoconsole --graphics none
started=$(date +%s)
ready=0
for attempt in $(seq 1 180); do
  if result=$(virsh qemu-agent-command "$domain" --timeout 5 '{"execute":"guest-ping"}' 2>/dev/null); then
    elapsed=$(($(date +%s)-started))
    echo QGA_READY_SECONDS=$elapsed
    echo "$result"
    virsh qemu-agent-command "$domain" --timeout 5 '{"execute":"guest-info"}' 2>/dev/null | head -c 1000 || true
    echo
    ready=1
    break
  fi
  sleep 2
done
virsh destroy "$domain" >/dev/null 2>&1 || true
virsh undefine "$domain" >/dev/null 2>&1 || true
rm -f "$overlay"
test "$ready" -eq 1
""",
    "qga-status": """virsh domstate gzprep-qga-probe
virsh qemu-agent-command gzprep-qga-probe --timeout 5 '{"execute":"guest-ping"}'
virsh qemu-agent-command gzprep-qga-probe --timeout 5 '{"execute":"guest-info"}' | head -c 1000
echo
""",
    "db-meta": """systemctl show gzctf.service --property=Environment --no-pager
systemctl cat gzctf.service
ls -la /opt/gzctf/publish/appsettings*.json
for file in /opt/gzctf/publish/appsettings*.json; do echo ===$file===; sed -n '1,160p' "$file"; done
""",
    "db-vm": """docker exec gzctf-postgres psql -U postgres -d gzctf -P pager=off -c '\\dt' | grep -Ei 'image|vm|prepar|instance'
docker exec gzctf-postgres psql -U postgres -d gzctf -P pager=off -c 'select "Id", "Name", "OSType", "ImageType", "ImageHash", "OriginalArchiveName", "Description", "LocalFilePath", "RegistryUrl" from "ImageTemplates" where "Id" in (1,34,35,69,71) order by "Id";'
docker exec gzctf-postgres psql -U postgres -d gzctf -P pager=off -c '\\d "VmInstances"'
docker exec gzctf-postgres psql -U postgres -d gzctf -P pager=off -c 'select * from "VmInstances" order by "Id" desc limit 30;'
docker exec gzctf-postgres psql -U postgres -d gzctf -P pager=off -c 'select "OperationId", "SourceImageTemplateId", "Mode", "WorkerNodeId", "DerivedImageTemplateId", "CreatedAt" from "VmImagePreparationJobs" order by "CreatedAt" desc limit 20;'
""",
    "agent-meta": """systemctl cat gzctf-agent.service
systemctl show gzctf-agent.service --property=ExecStart,WorkingDirectory --no-pager
sha256sum /usr/local/bin/gzctf-agent
ls -lh /tmp/gzctf-agent.* 2>/dev/null || true
""",
    "release-status": """systemctl is-active gzctf.service
curl -fsS --max-time 5 http://127.0.0.1:8080/ >/dev/null && echo HTTP_OK
python3 -c 'import json; print(json.load(open("/opt/gzctf/publish/release-manifest.json"))["releaseId"])'
sha256sum /opt/gzctf/publish/GZCTF.dll /opt/gzctf/publish/agent/gzctf-agent /usr/local/bin/gzctf-agent
ls -lh /opt/gzctf/incoming 2>/dev/null || true
""",
    "db-nodes": """docker exec gzctf-postgres psql -U postgres -d gzctf -P pager=off -c 'select "Id", "Name", "HostAddress", "Status", "IsSchedulable", "CurrentVms", "MaxVms", "AgentVersion", "AgentBinarySha256", "AgentUpdateState", "AgentUpdateLastError", "CapabilityManifestJson" from "WorkerNodes" order by "Name";'
""",
    "db-preparation": """docker exec gzctf-postgres psql -U postgres -d gzctf -P pager=off -c 'select "OperationId", "SourceImageTemplateId", "WorkerNodeId", "DerivedImageTemplateId", "CreatedAt" from "VmImagePreparationJobs" order by "CreatedAt" desc limit 5;'
docker exec gzctf-postgres psql -U postgres -d gzctf -P pager=off -c 'select "ImageTemplateId", "WorkerNodeId", "Status", "LastCheckedAt", "ErrorMessage", "ImageHash" from "ImageDistributionRecords" where "ImageTemplateId" = 69 order by "LastCheckedAt" desc;'
""",
    "prep-status": """ls -lh /var/lib/gzctf/images/69.qcow2 /var/lib/gzctf/image-factory/*/overlay.qcow2 2>/dev/null || true
virsh list --all | grep -E 'gzprep|Id|---' || true
journalctl -u gzctf-agent.service --since '20 minutes ago' --no-pager 2>/dev/null | grep -Ei 'image|download|artifact|prepare|qga|error|fail' | tail -120
""",
    "prep-logs": """echo ===AGENT===
journalctl -u gzctf-agent.service --since '2026-07-17 18:15:00' --until '2026-07-17 18:30:00' --no-pager 2>/dev/null | tail -320
echo ===MAIN===
journalctl -u gzctf.service --since '2026-07-17 18:15:00' --until '2026-07-17 18:30:00' --no-pager 2>/dev/null | tail -220
""",
    "cache-69": """set -eu
source=/var/lib/gzctf/images/phase9-win2022-qga-ready_e956b734.qcow2
target=/var/lib/gzctf/images/69.qcow2
rm -f "$target"
cp --reflink=auto "$source" "$target"
test "$(sha256sum "$target" | cut -d' ' -f1)" = da53a9b9eb89f14060797b93a3dd4fd2d2a779abbda04d88fa13a45c488778d7
ls -lh "$target"
""",
    "prep-qga": """domain=$(virsh list --name | grep '^gzprep-' | head -1)
test -n "$domain"
echo "$domain"
virsh qemu-agent-command "$domain" --timeout 5 '{"execute":"guest-ping"}'
virsh qemu-agent-command "$domain" --timeout 5 '{"execute":"guest-info"}' | head -c 1000
echo
""",
    "iso-layout": """set -eu
root=/tmp/gzctf-iso-layout
rm -rf "$root"
mkdir -p "$root/staging"
printf '{}\n' > "$root/staging/manifest.json"
printf 'Write-Host ok\n' > "$root/staging/install.ps1"
xorriso -as mkisofs -quiet -J -R -V GZFACTORY -o "$root/test.iso" "$root/staging"
xorriso -indev "$root/test.iso" -find / -exec lsdl
isoinfo -J -f -i "$root/test.iso"
rm -rf "$root"
""",
    "cleanup-prep-1801": """set -eu
domain=gzprep-019f6f8638177d7eb0f7db1a
virsh destroy "$domain" >/dev/null 2>&1 || true
virsh undefine "$domain" >/dev/null 2>&1 || true
rm -rf /var/lib/gzctf/image-factory/019f6f8638177d7eb0f7db1a468c939c
rm -f /var/lib/gzctf/image-factory/dhcp-hosts/019f6f8638177d7eb0f7db1a468c939c.conf
test ! -e /var/lib/gzctf/image-factory/019f6f8638177d7eb0f7db1a468c939c
echo cleaned
""",
}


def run(host: str, command: str, password: str, use_sudo: bool = True) -> None:
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(
        host,
        username="whoami",
        password=password,
        timeout=8,
        look_for_keys=False,
        allow_agent=False,
    )
    payload = base64.b64encode(command.encode()).decode()
    remote = f"bash -c 'echo {payload} | base64 -d | bash'"
    if use_sudo:
        remote = "sudo -k; sudo -S -p '' " + remote
    stdin, stdout, stderr = client.exec_command(remote, timeout=720)
    if use_sudo:
        stdin.write(password + "\n")
    stdin.flush()
    stdin.channel.shutdown_write()
    output = stdout.read().decode("utf-8", "replace")
    exit_code = stdout.channel.recv_exit_status()
    print(f"EXIT={exit_code}")
    print(output)
    error = stderr.read().decode("utf-8", "replace")
    if error.strip():
        print(error)
    client.close()


parser = argparse.ArgumentParser()
parser.add_argument("host")
parser.add_argument("preset", choices=COMMANDS)
args = parser.parse_args()
password = os.environ.get("GZCTF_SSH_PASSWORD")
if not password:
    raise SystemExit("GZCTF_SSH_PASSWORD is required")
run(args.host, COMMANDS[args.preset], password, args.preset not in {"runtime-final", "db-docker-templates", "linux-image-network", "factory-qga", "qga-status", "db-meta", "db-vm", "agent-meta", "release-status", "db-nodes", "db-preparation", "prep-status", "prep-logs", "cache-69", "prep-qga", "iso-layout", "cleanup-prep-1801"})
