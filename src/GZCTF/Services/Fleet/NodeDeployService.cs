using System.Text.RegularExpressions;
using System.Text.Json;
using GZCTF.Models.Data;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;

namespace GZCTF.Services.Fleet;

public class NodeDeployService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly ILogger<NodeDeployService> _logger;

    public NodeDeployService(AppDbContext context, IConfiguration config, ILogger<NodeDeployService> logger)
    { _context = context; _config = config; _logger = logger; }

    public async Task<NodeDeployResult> DeployToServerAsync(
        string hostAddress, string username, string password,
        string? nodeName = null, CancellationToken token = default,
        string? serverUrlOverride = null)
    {
        hostAddress = hostAddress.Trim();
        username = username.Trim();
        nodeName = string.IsNullOrWhiteSpace(nodeName) ? null : nodeName.Trim();

        if (!SafeHostPattern.IsMatch(hostAddress))
            throw new ArgumentException("Host contains invalid characters.", nameof(hostAddress));
        if (!SafeUserPattern.IsMatch(username))
            throw new ArgumentException("User contains invalid characters.", nameof(username));

        var node = await _context.WorkerNodes
            .FirstOrDefaultAsync(n => !n.IsLocal && n.HostAddress == hostAddress, token);
        var createdNode = node is null;

        node ??= new WorkerNode
        {
            HostAddress = hostAddress,
            AuthToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
        };

        node.Name = nodeName ?? NullIfWhiteSpace(node.Name) ?? hostAddress;
        node.HostAddress = hostAddress;
        node.AuthToken = NullIfWhiteSpace(node.AuthToken)
                         ?? Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        if (createdNode)
            node.Capabilities = NodeCapability.None;
        node.Status = NodeStatus.Unknown;
        node.LastHeartbeat = null;

        if (createdNode)
            _context.WorkerNodes.Add(node);

        await _context.SaveChangesAsync(token);

        _logger.LogInformation("Deploying to node {NodeId} at {Host}", node.Id, hostAddress);

        var deployStartedAt = DateTimeOffset.UtcNow;

        SshClient? ssh = null;
        var sudo = string.Empty;
        var remoteInstallStarted = false;

        try
        {
            ssh = new SshClient(hostAddress, username, password);
            await Task.Run(() => ssh.Connect(), token);
            sudo = DetectPrivilegePrefix(ssh);

            RunChecked(ssh, BuildBootstrapScript(GetInternalDockerRegistry()), "Bootstrap node dependencies");

            var caps = DetectCapabilities(ssh, sudo);

            if (caps == NodeCapability.None)
            {
                await MarkDeployFailedAsync(node.Id, createdNode, token);
                ssh.Disconnect();
                return new NodeDeployResult
                {
                    Success = false, NodeId = node.Id,
                    Message = "No usable Docker or KVM capability detected after automatic installation. Check virtualization support and package manager access on the target server."
                };
            }

            await _context.WorkerNodes
                .Where(n => n.Id == node.Id)
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(n => n.Capabilities, caps), token);
            node.Capabilities = caps;

            var serverUrl = ResolveServerUrl(_config, serverUrlOverride);
            var dotnetRoot = DetectDotnetRoot(ssh);

            var configJson = BuildAgentConfigJson(serverUrl, node);
            WriteRemoteFile(ssh, sudo, "/etc/gzctf-agent/appsettings.json", configJson,
                "Write agent configuration");
            remoteInstallStarted = true;

            var agentUrl = $"{serverUrl.TrimEnd('/')}/api/agent/download";
            RunChecked(ssh, BuildAgentInstallScript(agentUrl, node.Id, sudo), "Install agent binary");

            WriteRemoteFile(ssh, sudo, "/etc/systemd/system/gzctf-agent.service",
                BuildAgentServiceContent(dotnetRoot), "Write agent systemd unit");

            RunDiagnostic(ssh, BuildAgentStartScript(sudo), "Start agent service");
            RunDiagnostic(ssh, BuildAgentVerifyScript(sudo, node.AuthToken, node.AgentPort),
                "Verify agent API");

            ssh.Disconnect();

            await WaitForHeartbeatAsync(node, deployStartedAt, token);

            _logger.LogInformation("Node {NodeId} deployed: caps={Caps}", node.Id, caps);

            return new NodeDeployResult
            {
                Success = true, NodeId = node.Id, NodeName = node.Name,
                Capabilities = caps, Message = $"Deployment succeeded, detected capabilities: {caps}"
            };
        }
        catch (Exception ex)
        {
            var liveNode = await GetLiveRegisteredNodeAsync(node.Id, deployStartedAt, token);
            if (liveNode is not null)
            {
                _logger.LogWarning(ex,
                    "Deploy step failed after node {NodeId} already sent heartbeat; treating registration as successful",
                    node.Id);

                return new NodeDeployResult
                {
                    Success = true, NodeId = liveNode.Id, NodeName = liveNode.Name,
                    Capabilities = liveNode.Capabilities,
                    Message = $"Deployment succeeded, detected capabilities: {liveNode.Capabilities}"
                };
            }

            if (remoteInstallStarted && ssh is { IsConnected: true })
                TryRollbackRemoteAgent(ssh, sudo);

            _logger.LogError(ex, "Deploy failed for node {NodeId}", node.Id);
            await MarkDeployFailedAsync(node.Id, createdNode, token);

            return new NodeDeployResult
            {
                Success = false, NodeId = node.Id,
                Message = $"Connection failed: {ex.Message}"
            };
        }
        finally
        {
            ssh?.Dispose();
        }
    }

    internal static string ResolveServerUrl(IConfiguration config, string? requestBaseUrl = null)
    {
        var publicUrl = config["Agent:ServerPublicUrl"];
        if (!string.IsNullOrWhiteSpace(publicUrl))
            return publicUrl.TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(requestBaseUrl) && IsReachableServerUrl(requestBaseUrl))
            return requestBaseUrl.TrimEnd('/');

        var urls = config["Urls"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        var firstUrl = urls.FirstOrDefault();
        var publicEntry = config["ContainerProvider:PublicEntry"];

        if (!string.IsNullOrWhiteSpace(publicEntry))
        {
            var scheme = "http";
            var port = "8080";

            if (!string.IsNullOrWhiteSpace(firstUrl) && Uri.TryCreate(firstUrl, UriKind.Absolute, out var boundUri))
            {
                scheme = boundUri.Scheme;
                if (!boundUri.IsDefaultPort)
                    port = boundUri.Port.ToString();
            }

            var entry = publicEntry.Trim().TrimEnd('/');
            var hasScheme = entry.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            || entry.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            return hasScheme ? entry : $"{scheme}://{entry}:{port}";
        }

        var routableUrl = urls.FirstOrDefault(IsRoutableServerUrl);
        if (!string.IsNullOrWhiteSpace(routableUrl))
            return routableUrl.TrimEnd('/');

        return "http://localhost:8080";
    }

    internal static bool IsRoutableServerUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        return !string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(host, "::", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(host, "[::]", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(host, "+", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(host, "*", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReachableServerUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host;
        return !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
               && host != "127.0.0.1"
               && host != "::1"
               && host != "0.0.0.0"
               && host != "[::]";
    }

    internal string? GetInternalDockerRegistry()
    {
        var address = _config["DockerRegistrySettings:Address"];
        if (string.IsNullOrWhiteSpace(address))
            return null;

        var value = address.Trim().TrimEnd('/');
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            value = value["http://".Length..];
        else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            value = value["https://".Length..];

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    internal static string BuildBootstrapScript(string? internalDockerRegistry = null) =>
        $$"""
set -euo pipefail

INTERNAL_DOCKER_REGISTRY={{BashQuote(internalDockerRegistry ?? string.Empty)}}

need_cmd() {
  command -v "$1" >/dev/null 2>&1
}

run_sudo() {
  if [ "$(id -u)" = "0" ]; then
    "$@"
  else
    sudo -n "$@"
  fi
}

detect_pm() {
  if need_cmd apt-get; then echo apt; return; fi
  if need_cmd dnf; then echo dnf; return; fi
  if need_cmd yum; then echo yum; return; fi
  if need_cmd zypper; then echo zypper; return; fi
  if need_cmd pacman; then echo pacman; return; fi
  echo unknown
}

pm="$(detect_pm)"
if [ "$pm" = "unknown" ]; then
  echo "Unsupported Linux package manager. Supported: apt, dnf, yum, zypper, pacman." >&2
  exit 2
fi

install_pkgs() {
  case "$pm" in
    apt)
      export DEBIAN_FRONTEND=noninteractive
      run_sudo apt-get update -y
      run_sudo apt-get install -y --no-install-recommends "$@"
      ;;
    dnf)
      run_sudo dnf install -y "$@"
      ;;
    yum)
      run_sudo yum install -y "$@"
      ;;
    zypper)
      run_sudo zypper --non-interactive install -y "$@"
      ;;
    pacman)
      run_sudo pacman -Sy --noconfirm --needed "$@"
      ;;
  esac
}

try_install_pkgs() {
  install_pkgs "$@" >/dev/null 2>&1
}

install_apt_pkg_fallback() {
  pkg="$1"
  wget -q "https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb || \
    wget -q "https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb" -O /tmp/packages-microsoft-prod.deb
  run_sudo dpkg -i /tmp/packages-microsoft-prod.deb >/dev/null
  rm -f /tmp/packages-microsoft-prod.deb
  run_sudo apt-get update -y
  run_sudo apt-get install -y --no-install-recommends "$pkg"
}

install_base() {
  case "$pm" in
    apt) install_pkgs ca-certificates curl wget gnupg lsb-release iproute2 procps tar gzip coreutils python3 ;;
    dnf|yum) install_pkgs ca-certificates curl wget gnupg2 iproute procps-ng tar gzip coreutils python3 ;;
    zypper) install_pkgs ca-certificates curl wget gpg2 iproute2 procps tar gzip coreutils python3 ;;
    pacman) install_pkgs ca-certificates curl wget gnupg iproute2 procps-ng tar gzip coreutils python ;;
  esac
}

install_docker_from_get_script() {
  curl -fsSL https://get.docker.com -o /tmp/get-docker.sh
  run_sudo sh /tmp/get-docker.sh
  rm -f /tmp/get-docker.sh
}

install_docker_from_official_apt() {
  . /etc/os-release 2>/dev/null || true
  distro="${ID:-ubuntu}"
  codename="${VERSION_CODENAME:-}"

  if [ -z "$codename" ] && need_cmd lsb_release; then
    codename="$(lsb_release -cs)"
  fi

  case "$distro" in
    ubuntu|debian) ;;
    *) return 1 ;;
  esac

  [ -n "$codename" ] || return 1

  install_pkgs ca-certificates curl gnupg
  run_sudo install -m 0755 -d /etc/apt/keyrings
  curl -fsSL "https://download.docker.com/linux/${distro}/gpg" -o /tmp/docker.asc
  run_sudo install -m 0644 /tmp/docker.asc /etc/apt/keyrings/docker.asc
  rm -f /tmp/docker.asc
  arch="$(dpkg --print-architecture)"
  echo "deb [arch=${arch} signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/${distro} ${codename} stable" | \
    run_sudo tee /etc/apt/sources.list.d/docker.list >/dev/null
  run_sudo apt-get update -y
  run_sudo apt-get install -y --no-install-recommends docker-ce docker-ce-cli containerd.io
  try_install_pkgs docker-buildx-plugin docker-compose-plugin docker-model-plugin || true
}

install_docker() {
  if need_cmd docker && run_sudo docker info >/dev/null 2>&1; then
    return
  fi

  case "$pm" in
    apt)
      if ! need_cmd docker; then
        install_pkgs docker.io || install_docker_from_official_apt || install_docker_from_get_script
      fi
      try_install_pkgs docker-compose-plugin docker-buildx-plugin docker-model-plugin || true
      ;;
    dnf|yum)
      if ! need_cmd docker; then
        try_install_pkgs docker docker-cli containerd || \
          try_install_pkgs moby-engine || install_docker_from_get_script
      fi
      try_install_pkgs docker-compose-plugin docker-buildx-plugin docker-model-plugin || true
      ;;
    zypper)
      if ! need_cmd docker; then
        install_pkgs docker
      fi
      try_install_pkgs docker-compose || true
      ;;
    pacman)
      if ! need_cmd docker; then
        install_pkgs docker
      fi
      try_install_pkgs docker-compose || true
      ;;
  esac

  if need_cmd systemctl; then
    run_sudo systemctl enable --now docker >/dev/null 2>&1 || true
    run_sudo systemctl restart docker >/dev/null 2>&1 || true
  elif need_cmd service; then
    run_sudo service docker start >/dev/null 2>&1 || true
  fi

  if ! run_sudo docker info >/dev/null 2>&1; then
    echo "Docker installed but daemon is not healthy." >&2
    exit 3
  fi
}

configure_docker_registry() {
  registry="${INTERNAL_DOCKER_REGISTRY:-}"
  if [ -z "$registry" ] || ! need_cmd docker; then
    return
  fi

  run_sudo mkdir -p /etc/docker
  if [ -f /etc/docker/daemon.json ]; then
    run_sudo cp -a /etc/docker/daemon.json "/etc/docker/daemon.json.bak.$(date +%Y%m%d%H%M%S)" || true
  fi

  tmp="/tmp/gzctf-docker-daemon-$$.json"
  if need_cmd python3; then
    run_sudo python3 - "$registry" <<'PY' > "$tmp"
import json
import os
import sys

path = "/etc/docker/daemon.json"
registry = sys.argv[1].strip()
data = {}

try:
    if os.path.exists(path) and os.path.getsize(path) > 0:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
except Exception:
    data = {}

registries = data.get("insecure-registries")
if not isinstance(registries, list):
    registries = []
if registry and registry not in registries:
    registries.append(registry)
data["insecure-registries"] = registries

print(json.dumps(data, indent=2, ensure_ascii=False))
PY
  else
    cat > "$tmp" <<EOF
{
  "insecure-registries": ["$registry"]
}
EOF
  fi

  run_sudo install -m 0644 "$tmp" /etc/docker/daemon.json
  rm -f "$tmp"

  if need_cmd systemctl; then
    run_sudo systemctl restart docker >/dev/null 2>&1 || true
  elif need_cmd service; then
    run_sudo service docker restart >/dev/null 2>&1 || true
  fi

  if ! run_sudo docker info >/dev/null 2>&1; then
    echo "Docker daemon is not healthy after registry configuration." >&2
    exit 3
  fi
}

install_kvm() {
  case "$pm" in
    apt)
      install_pkgs qemu-kvm qemu-utils libvirt-daemon-system libvirt-clients virtinst dnsmasq-base bridge-utils || true
      ;;
    dnf|yum)
      install_pkgs qemu-kvm libvirt virt-install virt-manager libvirt-daemon-config-network libvirt-daemon-kvm qemu-img dnsmasq || true
      ;;
    zypper)
      install_pkgs qemu-kvm libvirt libvirt-client virt-install qemu-tools dnsmasq || true
      ;;
    pacman)
      install_pkgs qemu-full libvirt virt-install dnsmasq bridge-utils || true
      ;;
  esac

  if need_cmd systemctl; then
    run_sudo systemctl enable --now libvirtd >/dev/null 2>&1 || \
      run_sudo systemctl enable --now virtqemud >/dev/null 2>&1 || true
    run_sudo systemctl enable --now virtlogd >/dev/null 2>&1 || true
  fi

  if need_cmd virsh; then
    run_sudo virsh net-info default >/dev/null 2>&1 || run_sudo virsh net-define /usr/share/libvirt/networks/default.xml >/dev/null 2>&1 || true
    run_sudo virsh net-start default >/dev/null 2>&1 || true
    run_sudo virsh net-autostart default >/dev/null 2>&1 || true
  fi

  run_sudo mkdir -p /var/lib/gzctf/images /var/lib/libvirt/images
  run_sudo chmod 755 /var/lib/gzctf /var/lib/gzctf/images 2>/dev/null || true
}

install_dotnet_runtime() {
  if need_cmd dotnet && dotnet --list-runtimes 2>/dev/null | grep -Eq 'Microsoft\.AspNetCore\.App (10\.|[1-9][1-9]\.)'; then
    return
  fi

  case "$pm" in
    apt)
      try_install_pkgs dotnet-runtime-10.0 aspnetcore-runtime-10.0 || \
        install_apt_pkg_fallback aspnetcore-runtime-10.0 || true
      ;;
    dnf|yum)
      try_install_pkgs dotnet-runtime-10.0 aspnetcore-runtime-10.0 || true
      ;;
    zypper)
      try_install_pkgs dotnet-runtime-10.0 aspnetcore-runtime-10.0 || true
      ;;
    pacman)
      try_install_pkgs dotnet-runtime aspnet-runtime || true
      ;;
  esac

  if ! need_cmd dotnet || ! dotnet --list-runtimes 2>/dev/null | grep -Eq 'Microsoft\.AspNetCore\.App (10\.|[1-9][1-9]\.)'; then
    dotnet_dir="/usr/local/share/dotnet"
    run_sudo mkdir -p "$dotnet_dir"
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    run_sudo /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir "$dotnet_dir" --no-path || true
    run_sudo ln -sf "$dotnet_dir/dotnet" /usr/local/bin/dotnet
    rm -f /tmp/dotnet-install.sh
  fi

  if ! need_cmd dotnet || ! dotnet --list-runtimes 2>/dev/null | grep -Eq 'Microsoft\.AspNetCore\.App (10\.|[1-9][1-9]\.)'; then
    echo "ASP.NET Core runtime 10.0 was not installed; continuing because the packaged agent is self-contained." >&2
  fi
}

install_base
install_docker
configure_docker_registry
install_kvm
install_dotnet_runtime

echo "Docker: $(docker --version 2>/dev/null || echo unavailable)"
echo "Docker registry: ${INTERNAL_DOCKER_REGISTRY:-not configured}"
echo "Virsh: $(virsh --version 2>/dev/null || echo unavailable)"
echo "Dotnet: $(dotnet --version 2>/dev/null || echo unavailable)"
""";

    private static NodeCapability DetectCapabilities(SshClient ssh, string sudo)
    {
        var caps = NodeCapability.None;

        var dockerCheck = ssh.RunCommand($$"""
bash -lc 'if command -v docker >/dev/null 2>&1 && {{sudo}} docker info >/dev/null 2>&1; then echo DOCKER_OK; else echo NO_DOCKER; fi'
""");
        if (dockerCheck.Result.Contains("DOCKER_OK"))
            caps |= NodeCapability.Docker;

        var kvmCheck = ssh.RunCommand($$"""
bash -lc 'if command -v virsh >/dev/null 2>&1 && {{sudo}} virsh -c qemu:///system list >/dev/null 2>&1; then echo KVM_OK; else echo NO_KVM; fi'
""");
        if (kvmCheck.Result.Contains("KVM_OK"))
            caps |= NodeCapability.Kvm;

        return caps;
    }

    internal static string BuildAgentConfigJson(string serverUrl, WorkerNode node) =>
        JsonSerializer.Serialize(new
        {
            Agent = new
            {
                ServerUrl = serverUrl.TrimEnd('/'),
                NodeId = node.Id,
                node.AuthToken,
                ListenPort = node.AgentPort,
                HeartbeatIntervalSeconds = 30
            }
        }, new JsonSerializerOptions { WriteIndented = true });

    internal static string BuildAgentServiceContent(string dotnetRoot) => $$"""
[Unit]
Description=YINYU CTF Agent
After=network.target docker.service
Wants=docker.service

[Service]
Environment=DOTNET_ROOT={{dotnetRoot}}
Environment=DOTNET_ROOT_X64={{dotnetRoot}}
Environment=PATH={{dotnetRoot}}:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
ExecStart=/usr/local/bin/gzctf-agent
WorkingDirectory=/etc/gzctf-agent
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
""";

    internal static string BuildAgentStartScript(string sudo) => $$"""
{{sudo}} systemctl daemon-reload
{{sudo}} systemctl enable gzctf-agent >/dev/null 2>&1 || true
{{sudo}} systemctl stop gzctf-agent >/dev/null 2>&1 || true
for pid in $(pgrep -f '(^|/)(gzctf-agent|GZCTF.Agent|manual-agent)( |$)' || true); do
  {{sudo}} kill "$pid" >/dev/null 2>&1 || true
done
sleep 1
restart_status=0
restart_output="$({{sudo}} systemctl restart gzctf-agent 2>&1)" || restart_status=$?
for i in $(seq 1 20); do
  if {{sudo}} systemctl is-active --quiet gzctf-agent; then
    exit 0
  fi
  sleep 1
done
systemctl --no-pager --full status gzctf-agent.service >&2 || true
{{sudo}} journalctl -u gzctf-agent.service -n 80 --no-pager >&2 || true
echo "systemctl restart exited with ${restart_status}: ${restart_output}" >&2
echo "Agent service did not become active" >&2
exit 1
""";

    internal static string BuildAgentVerifyScript(string sudo, string authToken, int agentPort) => $$"""
for i in $(seq 1 30); do
  if command -v curl >/dev/null 2>&1; then
    if curl -fsS -H {{BashQuote($"Authorization: Bearer {authToken}")}} http://127.0.0.1:{{agentPort}}/api/status >/dev/null; then
      exit 0
    fi
  elif command -v wget >/dev/null 2>&1; then
    if wget -q -O - --header={{BashQuote($"Authorization: Bearer {authToken}")}} http://127.0.0.1:{{agentPort}}/api/status >/dev/null; then
      exit 0
    fi
  fi
  sleep 1
done
systemctl --no-pager --full status gzctf-agent.service >&2 || true
{{sudo}} journalctl -u gzctf-agent.service -n 80 --no-pager >&2 || true
echo "Agent status endpoint did not become healthy" >&2
exit 1
""";

    private void TryRollbackRemoteAgent(SshClient ssh, string sudo)
    {
        try
        {
            RunChecked(ssh, $$"""
{{sudo}} systemctl disable --now gzctf-agent >/dev/null 2>&1 || true
{{sudo}} rm -f /etc/systemd/system/gzctf-agent.service /usr/local/bin/gzctf-agent || true
{{sudo}} rm -rf /etc/gzctf-agent || true
{{sudo}} systemctl daemon-reload >/dev/null 2>&1 || true
""", "Rollback agent installation");
        }
        catch (Exception rollbackEx)
        {
            _logger.LogWarning(rollbackEx, "Failed to roll back agent installation");
        }
    }

    private async Task<WorkerNode?> GetLiveRegisteredNodeAsync(Guid nodeId, DateTimeOffset deployStartedAt,
        CancellationToken token)
    {
        var liveNode = await _context.WorkerNodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == nodeId, token);

        if (liveNode is null)
            return null;

        return liveNode.Status == NodeStatus.Online
               && liveNode.Capabilities != NodeCapability.None
               && liveNode.LastHeartbeat.HasValue
               && liveNode.LastHeartbeat.Value >= deployStartedAt
            ? liveNode
            : null;
    }

    private async Task MarkDeployFailedAsync(Guid nodeId, bool createdNode, CancellationToken token)
    {
        if (createdNode)
        {
            await _context.WorkerNodes.Where(n => n.Id == nodeId).ExecuteDeleteAsync(token);
            return;
        }

        var node = await _context.WorkerNodes.FirstOrDefaultAsync(n => n.Id == nodeId, token);
        if (node is null)
            return;

        node.Status = NodeStatus.Error;
        node.Capabilities = NodeCapability.None;
        node.LastHeartbeat = null;
        await _context.SaveChangesAsync(token);
    }

    private async Task WaitForHeartbeatAsync(WorkerNode node, DateTimeOffset deployStartedAt,
        CancellationToken token)
    {
        for (var i = 0; i < 20; i++)
        {
            await _context.Entry(node).ReloadAsync(token);
            if (node.Status == NodeStatus.Online
                && node.LastHeartbeat.HasValue
                && node.LastHeartbeat.Value >= deployStartedAt)
                return;

            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }

        throw new InvalidOperationException("Agent service started, but no heartbeat was received by the platform.");
    }

    private static string DetectPrivilegePrefix(SshClient ssh)
    {
        var id = RunChecked(ssh, "id -u", "Detect remote user id").Result.Trim();
        if (id == "0")
            return string.Empty;

        var sudo = ssh.RunCommand("sudo -n true 2>/dev/null");
        if (sudo.ExitStatus == 0)
            return "sudo -n";

        throw new InvalidOperationException("Target user must be root or have passwordless sudo privileges to install the agent service.");
    }

    private static string DetectDotnetRoot(SshClient ssh)
    {
        var command = RunChecked(ssh, BuildDotnetRootDetectScript(), "Detect .NET runtime");

        return command.Result.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim()
               ?? "/usr/local/share/dotnet";
    }

    internal static string BuildDotnetRootDetectScript() =>
        "for p in \"$(command -v dotnet 2>/dev/null || true)\" /usr/share/dotnet/dotnet /usr/local/share/dotnet/dotnet /usr/bin/dotnet; do " +
        "[ -n \"$p\" ] && [ -x \"$p\" ] || continue; " +
        "resolved=\"$(readlink -f \"$p\" 2>/dev/null || printf \"%s\\n\" \"$p\")\"; " +
        "dirname \"$resolved\"; " +
        "break; " +
        "done";

    internal static string BuildAgentInstallScript(string agentUrl, Guid nodeId, string sudo) =>
        NormalizeShellScript($$"""
tmp="/tmp/gzctf-agent-{{nodeId:N}}"
rm -f "$tmp"
download_status=127
command -v wget >/dev/null 2>&1 && wget -q -O "$tmp" {{BashQuote(agentUrl)}} && download_status=0
[ "$download_status" -eq 0 ] || { command -v curl >/dev/null 2>&1 && curl -fsSL {{BashQuote(agentUrl)}} -o "$tmp" && download_status=0; }
[ "$download_status" -eq 0 ] || { echo "wget or curl is required to download the agent" >&2; exit 127; }
test -s "$tmp"
chmod +x "$tmp"
{{sudo}} install -m 0755 "$tmp" /usr/local/bin/gzctf-agent
rm -f "$tmp"
{{sudo}} test -x /usr/local/bin/gzctf-agent
""");

    private static void WriteRemoteFile(SshClient ssh, string sudo, string remotePath, string content, string step)
    {
        var tmp = $"/tmp/gzctf-agent-{Guid.NewGuid():N}";
        RunChecked(ssh, $$"""
cat > {{BashQuote(tmp)}} <<'GZCTFEOF'
{{content}}
GZCTFEOF
{{sudo}} mkdir -p {{BashQuote(Path.GetDirectoryName(remotePath) ?? "/")}}
{{sudo}} install -m 0644 {{BashQuote(tmp)}} {{BashQuote(remotePath)}}
rm -f {{BashQuote(tmp)}}
""", step);
    }

    private static SshCommand RunChecked(SshClient ssh, string command, string step)
    {
        var script = $"set -euo pipefail\n{NormalizeShellScript(command)}";
        var result = ssh.RunCommand($"bash -lc {BashQuote(script)}");
        if (result.ExitStatus == 0)
            return result;

        var output = string.Join('\n',
            new[] { result.Error, result.Result }.Where(s => !string.IsNullOrWhiteSpace(s)))
            .Trim();
        if (output.Length > 800)
            output = output[..800];

        throw new InvalidOperationException($"{step} failed: {output}");
    }

    private void RunDiagnostic(SshClient ssh, string command, string step)
    {
        try
        {
            RunChecked(ssh, command, step);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex,
                "{Step} did not complete; continuing until platform heartbeat confirms registration",
                step);
        }
    }

    internal static string NormalizeShellScript(string command) =>
        command.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string BashQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static readonly Regex SafeHostPattern =
        new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-.]*[a-zA-Z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex SafeUserPattern =
        new(@"^[a-z_][a-z0-9_-]*$", RegexOptions.Compiled);
}

public class NodeDeployResult
{
    public bool Success { get; set; }
    public Guid NodeId { get; set; }
    public string? NodeName { get; set; }
    public NodeCapability Capabilities { get; set; }
    public string Message { get; set; } = string.Empty;
}
