#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  cat <<'EOF'
Prepare a Linux server to be used as a GZCTF remote worker node.

Usage:
  sudo bash scripts/prepare-agent-node.sh [options]

Options:
  --docker              Install and enable Docker support. Enabled by default.
  --no-docker           Skip Docker installation.
  --kvm                 Install and enable KVM/libvirt support. Enabled by default.
  --no-kvm              Skip KVM/libvirt installation.
  --dotnet              Install .NET / ASP.NET Core runtime 10. Enabled by default.
  --no-dotnet           Skip .NET runtime installation.
  --images-dir PATH     KVM image directory. Default: /var/lib/gzctf/images
  --check-only          Do not install packages; only print current status.
  -h, --help            Show this help.

After this script succeeds, add the server from the GZCTF admin node page.
The platform will deploy and register gzctf-agent over SSH.
EOF
}

need_docker=1
need_kvm=1
need_dotnet=1
check_only=0
images_dir="/var/lib/gzctf/images"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --docker) need_docker=1 ;;
    --no-docker) need_docker=0 ;;
    --kvm) need_kvm=1 ;;
    --no-kvm) need_kvm=0 ;;
    --dotnet) need_dotnet=1 ;;
    --no-dotnet) need_dotnet=0 ;;
    --images-dir)
      [[ $# -ge 2 ]] || { echo "--images-dir requires a path" >&2; exit 2; }
      images_dir="$2"
      shift
      ;;
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

require_supported_os() {
  if [[ ! -r /etc/os-release ]]; then
    echo "Cannot detect OS. This script supports Debian/Ubuntu style systems." >&2
    exit 1
  fi

  # shellcheck disable=SC1091
  . /etc/os-release
  case "${ID:-}" in
    ubuntu|debian) ;;
    *)
      case "${ID_LIKE:-}" in
        *debian*) ;;
        *)
          echo "Unsupported OS: ${PRETTY_NAME:-unknown}. Use Debian/Ubuntu or install prerequisites manually." >&2
          exit 1
          ;;
      esac
      ;;
  esac

  if ! command -v apt-get >/dev/null 2>&1; then
    echo "apt-get is required." >&2
    exit 1
  fi
}

require_root() {
  if [[ "$(id -u)" -ne 0 ]]; then
    echo "Please run as root, for example: sudo bash $0" >&2
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

ensure_microsoft_dotnet_repo_if_needed() {
  if apt-cache show aspnetcore-runtime-10.0 >/dev/null 2>&1; then
    return
  fi

  if [[ "$check_only" -eq 1 ]]; then
    warn "aspnetcore-runtime-10.0 is not available in current apt sources."
    return
  fi

  # shellcheck disable=SC1091
  . /etc/os-release
  local repo_pkg="/tmp/packages-microsoft-prod.deb"
  local repo_url="https://packages.microsoft.com/config/${ID}/${VERSION_ID}/packages-microsoft-prod.deb"

  log "Adding Microsoft package repository for .NET runtime: ${repo_url}"
  apt-get update
  apt-get install -y ca-certificates wget
  if ! wget -q -O "$repo_pkg" "$repo_url"; then
    echo "Failed to download Microsoft package repository. Install .NET 10 runtime manually." >&2
    exit 1
  fi
  dpkg -i "$repo_pkg"
  rm -f "$repo_pkg"
  apt-get update
}

enable_service_if_exists() {
  local service="$1"
  if systemctl list-unit-files | awk '{print $1}' | grep -Fxq "${service}.service"; then
    systemctl enable --now "$service"
    return 0
  fi
  return 1
}

install_docker() {
  if command -v docker >/dev/null 2>&1; then
    log "Docker already installed: $(docker --version 2>/dev/null || true)"
  else
    log "Installing Docker"
    apt_install docker.io
  fi

  if [[ "$check_only" -eq 0 ]]; then
    enable_service_if_exists docker || warn "docker.service was not found by systemd"
  fi
}

install_dotnet() {
  if command -v dotnet >/dev/null 2>&1 \
    && dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft.AspNetCore.App 10\.'; then
    log ".NET 10 ASP.NET Core runtime already installed"
    return
  fi

  log "Installing .NET 10 runtime"
  ensure_microsoft_dotnet_repo_if_needed
  apt_install aspnetcore-runtime-10.0 dotnet-runtime-10.0
}

install_kvm() {
  if command -v virsh >/dev/null 2>&1 \
    && command -v virt-install >/dev/null 2>&1 \
    && command -v qemu-img >/dev/null 2>&1; then
    log "KVM/libvirt tools already installed"
  else
    log "Installing KVM/libvirt packages"
    apt_install \
      libvirt-daemon-system \
      libvirt-clients \
      virtinst \
      qemu-system-x86 \
      qemu-utils \
      bridge-utils
  fi

  if [[ "$check_only" -eq 0 ]]; then
    enable_service_if_exists libvirtd \
      || enable_service_if_exists libvirt-daemon \
      || warn "libvirt service was not found by systemd"

    if command -v virsh >/dev/null 2>&1; then
      virsh net-start default >/dev/null 2>&1 || true
      virsh net-autostart default >/dev/null 2>&1 || true
    fi

    mkdir -p "$images_dir"
    chmod 755 "$(dirname "$images_dir")" "$images_dir"
  fi
}

print_status() {
  log "Status summary"

  if command -v docker >/dev/null 2>&1; then
    echo "Docker: $(docker --version 2>/dev/null || echo installed)"
    echo "Docker service: $(systemctl is-active docker 2>/dev/null || echo unknown)"
  else
    echo "Docker: missing"
  fi

  if command -v dotnet >/dev/null 2>&1; then
    echo "dotnet: $(command -v dotnet)"
    dotnet --list-runtimes 2>/dev/null | sed 's/^/  runtime: /' || true
  else
    echo "dotnet: missing"
  fi

  if command -v virsh >/dev/null 2>&1; then
    echo "virsh: $(virsh --version 2>/dev/null || echo installed)"
    echo "libvirt service: $(systemctl is-active libvirtd 2>/dev/null || systemctl is-active libvirt-daemon 2>/dev/null || echo unknown)"
    virsh net-info default 2>/dev/null | sed 's/^/  /' || true
  else
    echo "virsh: missing"
  fi

  if [[ -e /dev/kvm ]]; then
    echo "KVM device: present"
  else
    echo "KVM device: missing"
  fi

  if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -qi active; then
    warn "ufw is active. Allow the agent port and challenge public ports as needed."
  fi
}

main() {
  require_supported_os
  require_root

  if [[ "$need_docker" -eq 1 ]]; then install_docker; fi
  if [[ "$need_dotnet" -eq 1 ]]; then install_dotnet; fi
  if [[ "$need_kvm" -eq 1 ]]; then install_kvm; fi

  print_status
  log "Node prerequisites are ready. Add this server in the GZCTF admin node page."
}

main "$@"
