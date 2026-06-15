#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  cat <<'EOF'
Prepare an Ubuntu server as the GZCTF internal Docker image registry.

Usage:
  sudo bash setup-gzctf-image-registry.sh [options]

Options:
  --host HOST                 Registry host/IP used by GZCTF, for example 10.24.1.130.
                              Default: auto-detect first non-loopback IPv4 address.
  --port PORT                 Registry listen port. Default: 5000.
  --data-dir PATH             Registry storage directory. Default: /var/lib/gzctf-registry.
  --container-name NAME       Docker container name. Default: gzctf-registry.
  --backend auto|docker|apt   Registry backend. Default: auto.
                              docker uses registry:2 container; apt uses Ubuntu docker-registry.
  --allow-cidr CIDR           Allow registry port through ufw for CIDR. Can be repeated.
  --registry-mirror URL       Docker registry mirror for this server. Can be repeated.
  --configure-local-insecure  Add HOST:PORT to this server's Docker insecure registries.
  --no-docker-install         Do not install Docker automatically.
  --no-ufw                    Do not configure ufw rules.
  --check-only                Print planned changes without applying them.
  -h, --help                  Show this help.

Examples:
  sudo bash setup-gzctf-image-registry.sh --host 10.24.1.130 --allow-cidr 10.24.0.0/16

  sudo bash setup-gzctf-image-registry.sh \
    --host registry.ctf.lan \
    --port 5000 \
    --data-dir /data/gzctf-registry \
    --allow-cidr 10.24.0.0/16 \
    --configure-local-insecure
EOF
}

host=""
port="5000"
data_dir="/var/lib/gzctf-registry"
container_name="gzctf-registry"
backend="auto"
install_docker=1
configure_ufw=1
configure_local_insecure=0
check_only=0
allow_cidrs=()
registry_mirrors=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --host)
      [[ $# -ge 2 ]] || { echo "--host requires a value" >&2; exit 2; }
      host="$2"; shift
      ;;
    --port)
      [[ $# -ge 2 ]] || { echo "--port requires a value" >&2; exit 2; }
      port="$2"; shift
      ;;
    --data-dir)
      [[ $# -ge 2 ]] || { echo "--data-dir requires a path" >&2; exit 2; }
      data_dir="$2"; shift
      ;;
    --container-name)
      [[ $# -ge 2 ]] || { echo "--container-name requires a value" >&2; exit 2; }
      container_name="$2"; shift
      ;;
    --backend)
      [[ $# -ge 2 ]] || { echo "--backend requires auto, docker, or apt" >&2; exit 2; }
      backend="$2"; shift
      if [[ "$backend" != "auto" && "$backend" != "docker" && "$backend" != "apt" ]]; then
        echo "--backend must be auto, docker, or apt" >&2
        exit 2
      fi
      ;;
    --allow-cidr)
      [[ $# -ge 2 ]] || { echo "--allow-cidr requires CIDR" >&2; exit 2; }
      allow_cidrs+=("$2"); shift
      ;;
    --registry-mirror)
      [[ $# -ge 2 ]] || { echo "--registry-mirror requires URL" >&2; exit 2; }
      registry_mirrors+=("$2"); shift
      ;;
    --configure-local-insecure) configure_local_insecure=1 ;;
    --no-docker-install) install_docker=0 ;;
    --no-ufw) configure_ufw=0 ;;
    --check-only) check_only=1 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

log() {
  printf '[%s] %s\n' "$(date '+%F %T')" "$*"
}

warn() {
  printf '[%s] WARN: %s\n' "$(date '+%F %T')" "$*" >&2
}

require_root() {
  if [[ "$(id -u)" -ne 0 ]]; then
    echo "Run as root, for example: sudo bash $0" >&2
    exit 1
  fi
}

detect_host() {
  if [[ -n "$host" ]]; then
    return
  fi

  host="$(hostname -I 2>/dev/null | tr ' ' '\n' | grep -E '^[0-9]+\.' | grep -v '^127\.' | head -n 1 || true)"
  if [[ -z "$host" ]]; then
    echo "Cannot auto-detect host IP. Please pass --host HOST." >&2
    exit 2
  fi
}

validate_port() {
  if ! [[ "$port" =~ ^[0-9]+$ ]] || (( port < 1 || port > 65535 )); then
    echo "--port must be a TCP port from 1 to 65535" >&2
    exit 2
  fi
}

load_os_release() {
  if [[ ! -r /etc/os-release ]]; then
    echo "Cannot read /etc/os-release" >&2
    exit 1
  fi

  # shellcheck disable=SC1091
  . /etc/os-release
  if ! command -v apt-get >/dev/null 2>&1; then
    echo "This script expects Ubuntu/Debian with apt-get." >&2
    exit 1
  fi
}

apt_install() {
  if [[ "$check_only" -eq 1 ]]; then
    log "check-only: would install packages: $*"
    return
  fi

  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  apt-get install -y "$@"
}

json_array() {
  local first=1
  printf '['
  for item in "$@"; do
    [[ "$first" -eq 1 ]] || printf ','
    first=0
    printf '"%s"' "${item//\"/\\\"}"
  done
  printf ']'
}

write_docker_daemon_config() {
  local insecure_ref="$host:$port"
  local need_write=0

  if [[ "${#registry_mirrors[@]}" -gt 0 || "$configure_local_insecure" -eq 1 ]]; then
    need_write=1
  fi

  if [[ "$need_write" -eq 0 ]]; then
    return
  fi

  if [[ "$check_only" -eq 1 ]]; then
    log "check-only: would update /etc/docker/daemon.json"
    return
  fi

  mkdir -p /etc/docker
  if [[ -f /etc/docker/daemon.json ]]; then
    cp -a /etc/docker/daemon.json "/etc/docker/daemon.json.bak.$(date +%Y%m%d%H%M%S)"
  fi

  python3 - "$insecure_ref" "$configure_local_insecure" "${registry_mirrors[@]}" <<'PY'
import json
import sys
from pathlib import Path

path = Path("/etc/docker/daemon.json")
registry = sys.argv[1]
configure_insecure = sys.argv[2] == "1"
mirrors = sys.argv[3:]

data = {}
if path.exists() and path.read_text().strip():
    data = json.loads(path.read_text())

if mirrors:
    existing = data.get("registry-mirrors") or []
    for mirror in mirrors:
        if mirror not in existing:
            existing.append(mirror)
    data["registry-mirrors"] = existing

if configure_insecure:
    existing = data.get("insecure-registries") or []
    if registry not in existing:
        existing.append(registry)
    data["insecure-registries"] = existing

path.write_text(json.dumps(data, indent=2) + "\n")
PY
}

install_or_configure_docker() {
  if command -v docker >/dev/null 2>&1; then
    log "Docker exists: $(docker --version 2>/dev/null || true)"
  elif [[ "$install_docker" -eq 1 ]]; then
    log "Installing Docker"
    apt_install docker.io
  else
    echo "Docker is not installed and --no-docker-install was set." >&2
    exit 1
  fi

  write_docker_daemon_config

  if [[ "$check_only" -eq 1 ]]; then
    log "check-only: would enable and restart docker"
    return
  fi

  systemctl enable --now docker
  systemctl restart docker
}

write_registry_config() {
  if [[ "$check_only" -eq 1 ]]; then
    log "check-only: would create $data_dir/config.yml"
    return
  fi

  mkdir -p "$data_dir"
  chmod 0755 "$data_dir"
  cat > "$data_dir/config.yml" <<EOF
version: 0.1
log:
  fields:
    service: gzctf-registry
storage:
  filesystem:
    rootdirectory: /var/lib/registry
  delete:
    enabled: true
http:
  addr: :5000
EOF
}

run_registry_container() {
  local image="registry:2"

  if [[ "$check_only" -eq 1 ]]; then
    log "check-only: would run $image on 0.0.0.0:$port with storage $data_dir"
    return
  fi

  docker pull "$image"
  if docker ps -a --format '{{.Names}}' | grep -qx "$container_name"; then
    log "Replacing existing container $container_name"
    docker rm -f "$container_name"
  fi

  docker run -d \
    --name "$container_name" \
    --restart unless-stopped \
    -p "0.0.0.0:$port:5000" \
    -v "$data_dir/registry:/var/lib/registry" \
    -v "$data_dir/config.yml:/etc/docker/registry/config.yml:ro" \
    "$image"
}

run_registry_apt_service() {
  if [[ "$check_only" -eq 1 ]]; then
    log "check-only: would install docker-registry apt package and listen on 0.0.0.0:$port"
    return
  fi

  log "Installing Ubuntu docker-registry package"
  apt_install docker-registry

  if docker ps -a --format '{{.Names}}' | grep -qx "$container_name"; then
    warn "Stopping existing container backend $container_name before starting apt registry service"
    docker rm -f "$container_name" || true
  fi

  mkdir -p "$data_dir/registry" /etc/docker/registry
  if [[ -f /etc/docker/registry/config.yml ]]; then
    cp -a /etc/docker/registry/config.yml "/etc/docker/registry/config.yml.bak.$(date +%Y%m%d%H%M%S)"
  fi

  cat > /etc/docker/registry/config.yml <<EOF
version: 0.1
log:
  fields:
    service: gzctf-registry
storage:
  filesystem:
    rootdirectory: $data_dir/registry
  delete:
    enabled: true
http:
  addr: 0.0.0.0:$port
  headers:
    X-Content-Type-Options: [nosniff]
EOF

  if id docker-registry >/dev/null 2>&1; then
    chown -R docker-registry:docker-registry "$data_dir"
    chmod -R u+rwX,g+rwX,o-rwx "$data_dir"
  else
    warn "User docker-registry not found; leaving $data_dir owned by current user"
  fi

  systemctl daemon-reload
  systemctl enable --now docker-registry
  systemctl restart docker-registry
}

run_registry_backend() {
  case "$backend" in
    docker)
      write_registry_config
      run_registry_container
      ;;
    apt)
      run_registry_apt_service
      ;;
    auto)
      write_registry_config
      if run_registry_container; then
        return
      fi

      warn "registry:2 container startup failed, falling back to Ubuntu docker-registry package"
      run_registry_apt_service
      ;;
  esac
}

configure_firewall() {
  if [[ "$configure_ufw" -eq 0 ]]; then
    return
  fi

  if ! command -v ufw >/dev/null 2>&1; then
    apt_install ufw
  fi

  if [[ "$check_only" -eq 1 ]]; then
    if [[ "${#allow_cidrs[@]}" -eq 0 ]]; then
      log "check-only: would not add ufw allow rules because --allow-cidr was not provided"
    else
      for cidr in "${allow_cidrs[@]}"; do
        log "check-only: would allow tcp/$port from $cidr"
      done
    fi
    return
  fi

  for cidr in "${allow_cidrs[@]}"; do
    ufw allow from "$cidr" to any port "$port" proto tcp comment "GZCTF image registry" || true
  done
}

verify_registry() {
  if [[ "$check_only" -eq 1 ]]; then
    return
  fi

  log "Verifying registry API"
  curl -fsS "http://127.0.0.1:$port/v2/" >/dev/null
  curl -fsS "http://$host:$port/v2/" >/dev/null || warn "Cannot verify registry through http://$host:$port/v2/ from this server"
}

print_next_steps() {
  local registry_ref="$host:$port"

  cat <<EOF

Registry is ready: http://$registry_ref/v2/

Set this on the GZCTF platform server appsettings.json:

  "DockerRegistrySettings": {
    "Address": "$registry_ref",
    "Namespace": "ctf",
    "MaxUploadSizeGb": 10
  }

Configure every GZCTF platform/worker Docker daemon that needs to pull HTTP images:

  /etc/docker/daemon.json
  {
    "insecure-registries": ["$registry_ref"]
  }

Then restart Docker and the related GZCTF service/agent:

  sudo systemctl restart docker
  sudo systemctl restart gzctf        # platform server
  sudo systemctl restart gzctf-agent  # worker node

Smoke test from platform and worker nodes:

  docker pull hello-world:latest
  docker tag hello-world:latest $registry_ref/ctf/smoke/hello-world:latest
  docker push $registry_ref/ctf/smoke/hello-world:latest
  docker pull $registry_ref/ctf/smoke/hello-world:latest
EOF
}

main() {
  require_root
  load_os_release
  detect_host
  validate_port

  log "Registry host: $host"
  log "Registry port: $port"
  log "Registry data: $data_dir"

  install_or_configure_docker
  run_registry_backend
  configure_firewall
  verify_registry
  print_next_steps
}

main "$@"
