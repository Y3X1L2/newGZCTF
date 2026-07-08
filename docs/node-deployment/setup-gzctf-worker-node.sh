#!/usr/bin/env bash
set -Eeuo pipefail

usage() {
  cat <<'EOF'
Prepare a Linux server as a GZCTF worker node.

Usage:
  sudo bash setup-gzctf-worker-node.sh [options]

Options:
  --images-dir PATH           Local VM image cache. Default: /var/lib/gzctf/images
  --nfs-source HOST:/EXPORT   Optional shared image repository source.
  --repo-dir PATH             Mount point for --nfs-source. Default: /mnt/gzctf-image-repo
  --registry-mirror URL       Docker registry mirror. Can be repeated.
  --insecure-registry HOST    Docker insecure registry. Can be repeated.
  --no-docker                 Skip Docker installation/configuration.
  --no-dotnet                 Skip .NET runtime installation.
  --no-kvm                    Skip KVM/libvirt installation.
  --check-only                Print checks without installing packages.
  -h, --help                  Show this help.

Examples:
  sudo bash setup-gzctf-worker-node.sh

  sudo bash setup-gzctf-worker-node.sh \
    --insecure-registry 10.0.7.120:5000 \
    --nfs-source 10.24.110.110:/data/nfs-pve/gzctf-images \
    --repo-dir /mnt/gzctf-image-repo
EOF
}

images_dir="/var/lib/gzctf/images"
repo_dir="/mnt/gzctf-image-repo"
nfs_source=""
need_docker=1
need_dotnet=1
need_kvm=1
check_only=0
registry_mirrors=()
insecure_registries=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --images-dir)
      [[ $# -ge 2 ]] || { echo "--images-dir requires a path" >&2; exit 2; }
      images_dir="$2"; shift
      ;;
    --nfs-source)
      [[ $# -ge 2 ]] || { echo "--nfs-source requires HOST:/EXPORT" >&2; exit 2; }
      nfs_source="$2"; shift
      ;;
    --repo-dir)
      [[ $# -ge 2 ]] || { echo "--repo-dir requires a path" >&2; exit 2; }
      repo_dir="$2"; shift
      ;;
    --registry-mirror)
      [[ $# -ge 2 ]] || { echo "--registry-mirror requires a URL" >&2; exit 2; }
      registry_mirrors+=("$2"); shift
      ;;
    --insecure-registry)
      [[ $# -ge 2 ]] || { echo "--insecure-registry requires HOST[:PORT]" >&2; exit 2; }
      insecure_registries+=("$2"); shift
      ;;
    --no-docker) need_docker=0 ;;
    --no-dotnet) need_dotnet=0 ;;
    --no-kvm) need_kvm=0 ;;
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

load_os_release() {
  if [[ ! -r /etc/os-release ]]; then
    echo "Cannot read /etc/os-release" >&2
    exit 1
  fi

  # shellcheck disable=SC1091
  . /etc/os-release
  if ! command -v apt-get >/dev/null 2>&1; then
    echo "This script expects a Debian/Ubuntu style system with apt-get." >&2
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

enable_service() {
  local unit="$1"
  if [[ "$check_only" -eq 1 ]]; then
    log "check-only: would enable service ${unit}"
    return
  fi

  systemctl enable --now "$unit" >/dev/null 2>&1 || warn "failed to enable ${unit}"
}

install_docker() {
  if command -v docker >/dev/null 2>&1; then
    log "Docker exists: $(docker --version 2>/dev/null || true)"
  else
    log "Installing Docker"
    apt_install docker.io
  fi

  configure_docker_daemon
  enable_service docker
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

configure_docker_daemon() {
  if [[ "${#registry_mirrors[@]}" -eq 0 && "${#insecure_registries[@]}" -eq 0 ]]; then
    return
  fi

  if [[ "$check_only" -eq 1 ]]; then
    log "check-only: would write /etc/docker/daemon.json"
    return
  fi

  mkdir -p /etc/docker
  if [[ -f /etc/docker/daemon.json ]]; then
    cp -a /etc/docker/daemon.json "/etc/docker/daemon.json.bak.$(date +%Y%m%d%H%M%S)"
  fi

  {
    printf '{\n'
    local wrote=0
    if [[ "${#registry_mirrors[@]}" -gt 0 ]]; then
      printf '  "registry-mirrors": '
      json_array "${registry_mirrors[@]}"
      wrote=1
    fi
    if [[ "${#insecure_registries[@]}" -gt 0 ]]; then
      [[ "$wrote" -eq 0 ]] || printf ',\n'
      printf '  "insecure-registries": '
      json_array "${insecure_registries[@]}"
    fi
    printf '\n}\n'
  } > /etc/docker/daemon.json

  systemctl restart docker >/dev/null 2>&1 || warn "docker restart failed"
}

ensure_microsoft_repo() {
  if apt-cache show aspnetcore-runtime-10.0 >/dev/null 2>&1; then
    return
  fi

  if [[ "$check_only" -eq 1 ]]; then
    warn "aspnetcore-runtime-10.0 is not available in current apt sources"
    return
  fi

  # shellcheck disable=SC1091
  . /etc/os-release
  local repo_pkg="/tmp/packages-microsoft-prod.deb"
  local repo_url="https://packages.microsoft.com/config/${ID}/${VERSION_ID}/packages-microsoft-prod.deb"

  log "Adding Microsoft package repository: ${repo_url}"
  apt-get update
  apt-get install -y ca-certificates wget
  wget -q -O "$repo_pkg" "$repo_url"
  dpkg -i "$repo_pkg"
  rm -f "$repo_pkg"
  apt-get update
}

install_dotnet() {
  if command -v dotnet >/dev/null 2>&1 \
    && dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft.AspNetCore.App 10\.'; then
    log ".NET 10 ASP.NET Core runtime exists"
    return
  fi

  log "Installing .NET 10 runtime"
  ensure_microsoft_repo
  apt_install aspnetcore-runtime-10.0 dotnet-runtime-10.0
}

install_kvm() {
  if command -v virsh >/dev/null 2>&1 \
    && command -v virt-install >/dev/null 2>&1 \
    && command -v qemu-img >/dev/null 2>&1; then
    log "KVM/libvirt tools exist"
  else
    log "Installing KVM/libvirt packages"
    apt_install libvirt-daemon-system libvirt-clients virtinst qemu-system-x86 qemu-utils bridge-utils \
      dnsmasq-base genisoimage xorriso cloud-image-utils
  fi

  enable_service libvirtd || true
  if command -v virsh >/dev/null 2>&1 && [[ "$check_only" -eq 0 ]]; then
    virsh net-start default >/dev/null 2>&1 || true
    virsh net-autostart default >/dev/null 2>&1 || true
  fi
}

install_teamlab_network_tools() {
  log "Installing TeamLab VPN/network tools"
  apt_install wireguard-tools nftables iptables tcpdump dnsmasq-base
}

configure_image_dirs() {
  if [[ "$check_only" -eq 1 ]]; then
    log "check-only: would create ${images_dir}"
  else
    mkdir -p "$images_dir"
    chmod 755 "$(dirname "$images_dir")" "$images_dir"
  fi

  if [[ -z "$nfs_source" ]]; then
    return
  fi

  log "Configuring shared image repository mount: ${nfs_source} -> ${repo_dir}"
  apt_install nfs-common

  if [[ "$check_only" -eq 1 ]]; then
    return
  fi

  mkdir -p "$repo_dir"
  local fstab_line="${nfs_source} ${repo_dir} nfs4 defaults,_netdev,nofail,vers=4.2 0 0"
  if ! grep -Fq " ${repo_dir} " /etc/fstab; then
    cp -a /etc/fstab "/etc/fstab.bak.$(date +%Y%m%d%H%M%S)"
    printf '%s\n' "$fstab_line" >> /etc/fstab
  fi
  mount "$repo_dir" || warn "failed to mount ${repo_dir}; check NFS export ACL"
}

print_status() {
  log "Status summary"
  command -v docker >/dev/null 2>&1 && docker --version || echo "Docker: missing"
  command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes | sed 's/^/dotnet runtime: /' || echo "dotnet: missing"
  command -v virsh >/dev/null 2>&1 && virsh --version | sed 's/^/virsh: /' || echo "virsh: missing"
  [[ -e /dev/kvm ]] && echo "KVM device: present" || echo "KVM device: missing"
  grep -Eq '(^flags|^Features).* (vmx|svm)( |$)' /proc/cpuinfo 2>/dev/null \
    && echo "CPU virtualization flag: present" || echo "CPU virtualization flag: missing"
  command -v wg >/dev/null 2>&1 && wg --version | sed 's/^/WireGuard: /' || echo "WireGuard: missing"
  command -v tcpdump >/dev/null 2>&1 && tcpdump --version | head -n 1 | sed 's/^/tcpdump: /' || echo "tcpdump: missing"
  if command -v genisoimage >/dev/null 2>&1 || command -v xorriso >/dev/null 2>&1; then
    echo "cloud-init seed ISO tool: present"
  else
    echo "cloud-init seed ISO tool: missing"
  fi
  echo "Local image cache: ${images_dir}"
  if [[ -n "$nfs_source" ]]; then
    echo "Shared image repository: ${repo_dir}"
    mountpoint -q "$repo_dir" && echo "Repository mount: active" || echo "Repository mount: inactive"
  fi
}

main() {
  require_root
  load_os_release

  [[ "$need_docker" -eq 1 ]] && install_docker
  [[ "$need_dotnet" -eq 1 ]] && install_dotnet
  [[ "$need_kvm" -eq 1 ]] && install_kvm
  install_teamlab_network_tools
  configure_image_dirs
  print_status

  log "Ready. Add this server from GZCTF admin node page."
}

main "$@"
