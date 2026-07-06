using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public class KvmService
{
    private readonly KvmConfig _config;
    private readonly ILogger<KvmService> _logger;

    private static readonly Regex SafeNamePattern = new(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled);
    private static readonly Regex SafeMacPattern = new(@"^([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$", RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, RdpProxy> RdpProxies = new();
    private const int RdpProxyPortStart = 46000;
    private const int RdpProxyPortCount = 10000;

    public KvmService(IOptions<KvmConfig> config, ILogger<KvmService> logger)
    { _config = config.Value; _logger = logger; }

    public async Task<CreateVmResponse?> CreateVmAsync(CreateVmRequest request, CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(request.VmName))
            throw new ArgumentException("Invalid VM name", nameof(request.VmName));

        var templatePath = ResolveTemplatePath(request);
        var vmPath = Path.Combine(_config.ImageStoragePath, $"{request.VmName}.qcow2");

        Directory.CreateDirectory(_config.ImageStoragePath);

        await RunCommandAsync($"virsh destroy {ShellEscape(request.VmName)} 2>/dev/null || true", token,
            throwOnError: false);
        await RunCommandAsync($"virsh undefine {ShellEscape(request.VmName)} --remove-all-storage 2>/dev/null || true",
            token, throwOnError: false);
        if (File.Exists(vmPath))
            File.Delete(vmPath);

        if (!string.IsNullOrEmpty(templatePath))
            await RunCommandAsync($"qemu-img create -f qcow2 -b {ShellEscape(templatePath)} -F qcow2 {ShellEscape(vmPath)}", token);
        else
            await RunCommandAsync($"qemu-img create -f qcow2 {ShellEscape(vmPath)} 20G", token);

        await RunCommandAsync(
            $"virt-install --name {ShellEscape(request.VmName)} --memory {request.Memory} --vcpus {request.Cpu} " +
            $"--disk path={ShellEscape(vmPath)} --osinfo detect=on,require=off --import --noautoconsole " +
            $"{BuildVirtInstallNetworkArguments(request)} --graphics vnc,listen=0.0.0.0", token);

        var state = (await RunCommandAsync($"virsh domstate {ShellEscape(request.VmName)}", token)).Trim();
        if (!state.Equals("running", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"VM {request.VmName} was created but is not running (state: {state})");

        return new CreateVmResponse
        {
            VmName = request.VmName,
            Status = "Running",
            VncAddress = await GetVncAddressAsync(request.VmName, token),
            Interfaces = request.Interfaces
        };
    }

    public async Task DestroyVmAsync(string vmName, CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(vmName)) return;
        await StopRdpProxyAsync(vmName);
        await RunCommandAsync($"virsh destroy {ShellEscape(vmName)} 2>/dev/null || true", token);
        await RunCommandAsync($"virsh undefine {ShellEscape(vmName)} --remove-all-storage 2>/dev/null || true", token);
    }

    public async Task<int> GetVmCountAsync(CancellationToken token)
    {
        var result = await RunCommandAsync("virsh list --name 2>/dev/null", token, throwOnError: false);
        return result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public async Task RestoreRdpProxiesAsync(CancellationToken token)
    {
        var result = await RunCommandAsync("virsh list --name 2>/dev/null", token, throwOnError: false);
        foreach (var vmName in result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            token.ThrowIfCancellationRequested();
            if (!SafeNamePattern.IsMatch(vmName) || RdpProxies.ContainsKey(vmName))
                continue;

            var ip = await GetIpAddressAsync(vmName, token);
            if (string.IsNullOrEmpty(ip))
                continue;

            await EnsureRdpProxyAsync(vmName, ip, token);
        }
    }

    public async Task<string?> GetIpAddressAsync(string vmName, CancellationToken token,
        IReadOnlyList<VmNetworkInterfaceRequest>? interfaces = null)
    {
        var result = await GetIpAddressWithDiagnosticAsync(vmName, token, interfaces);
        return result.IpAddress;
    }

    public async Task<VmIpLookupResult> GetIpAddressWithDiagnosticAsync(string vmName, CancellationToken token,
        IReadOnlyList<VmNetworkInterfaceRequest>? interfaces = null)
    {
        if (!SafeNamePattern.IsMatch(vmName))
            return new VmIpLookupResult(null, "Invalid VM name.");

        var diagnostics = new List<string>();

        var result = await RunCommandAsync($"virsh domifaddr {ShellEscape(vmName)} --source agent 2>/dev/null",
            token, throwOnError: false);
        diagnostics.Add(SummarizeCommand("domifaddr-agent", result));
        var ip = ParseFirstNonLoopbackIp(result);
        if (ip is not null)
            return new VmIpLookupResult(ip, "Matched virsh domifaddr --source agent.");

        result = await RunCommandAsync($"virsh domifaddr {ShellEscape(vmName)} 2>/dev/null",
            token, throwOnError: false);
        diagnostics.Add(SummarizeCommand("domifaddr", result));
        ip = ParseFirstNonLoopbackIp(result);
        if (ip is not null)
            return new VmIpLookupResult(ip, "Matched virsh domifaddr.");

        if (interfaces is { Count: > 0 })
        {
            foreach (var iface in interfaces.Where(i =>
                         !string.IsNullOrWhiteSpace(i.BridgeName) &&
                         !string.IsNullOrWhiteSpace(i.MacAddress)))
            {
                if (!SafeNamePattern.IsMatch(iface.BridgeName) || !SafeMacPattern.IsMatch(iface.MacAddress!))
                    continue;

                var bridgeNeighbours = await RunCommandAsync($"ip neigh show dev {ShellEscape(iface.BridgeName)} 2>/dev/null",
                    token, throwOnError: false);
                diagnostics.Add(SummarizeCommand($"neigh:{iface.BridgeName}:{iface.MacAddress}", bridgeNeighbours));
                ip = ParseIpFromNeighborTable(bridgeNeighbours, iface.MacAddress!);
                if (ip is not null)
                    return new VmIpLookupResult(ip, $"Matched bridge neighbor table {iface.BridgeName}.");

                var leaseLookup = ParseIpFromTeamLabLeaseFiles(iface.MacAddress!, iface.BridgeName);
                if (!string.IsNullOrWhiteSpace(leaseLookup.Diagnostic))
                    diagnostics.Add(leaseLookup.Diagnostic);
                ip = leaseLookup.IpAddress;
                if (ip is not null)
                    return new VmIpLookupResult(ip, $"Matched TeamLab dnsmasq lease for {iface.BridgeName}.");
            }

            return new VmIpLookupResult(null, string.Join(" | ", diagnostics.Where(d => !string.IsNullOrWhiteSpace(d))));
        }

        var macResult = await RunCommandAsync($"virsh domiflist {ShellEscape(vmName)} 2>/dev/null",
            token, throwOnError: false);
        diagnostics.Add(SummarizeCommand("domiflist", macResult));
        var mac = ParseMacAddress(macResult);
        if (mac is null)
            return new VmIpLookupResult(null, string.Join(" | ", diagnostics));

        var leases = await RunCommandAsync("virsh net-dhcp-leases default 2>/dev/null",
            token, throwOnError: false);
        diagnostics.Add(SummarizeCommand("default-dhcp-leases", leases));
        ip = ParseIpFromDhcpLeases(leases, mac);
        if (ip is not null)
            return new VmIpLookupResult(ip, "Matched default libvirt DHCP leases.");

        var neighbours = await RunCommandAsync("ip neigh show dev virbr0 2>/dev/null",
            token, throwOnError: false);
        diagnostics.Add(SummarizeCommand("virbr0-neigh", neighbours));
        ip = ParseIpFromNeighborTable(neighbours, mac);
        return new VmIpLookupResult(ip,
            ip is null ? string.Join(" | ", diagnostics) : "Matched virbr0 neighbor table.");
    }

    public Task<int?> EnsureRdpProxyAsync(string vmName, string ipAddress, CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(vmName) || !IPAddress.TryParse(ipAddress, out var ip))
            return Task.FromResult<int?>(null);

        if (RdpProxies.TryGetValue(vmName, out var existing) && existing.TargetIp.Equals(ip))
            return Task.FromResult<int?>(existing.Port);

        if (RdpProxies.TryRemove(vmName, out existing))
            existing.Stop();

        var preferredPort = GetPreferredRdpProxyPort(vmName);
        for (var offset = 0; offset < RdpProxyPortCount; offset++)
        {
            token.ThrowIfCancellationRequested();
            var port = RdpProxyPortStart + ((preferredPort - RdpProxyPortStart + offset) % RdpProxyPortCount);
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start(64);
                var proxy = new RdpProxy(vmName, ip, port, listener, _logger);
                if (RdpProxies.TryAdd(vmName, proxy))
                {
                    _ = proxy.RunAsync();
                    _logger.LogInformation("RDP proxy for VM {VmName}: 0.0.0.0:{Port} -> {Ip}:3389",
                        vmName, port, ipAddress);
                    return Task.FromResult<int?>(port);
                }

                listener.Stop();
            }
            catch (SocketException)
            {
                continue;
            }
        }

        _logger.LogWarning("No available RDP proxy port for VM {VmName}", vmName);
        return Task.FromResult<int?>(null);
    }

    private async Task<string> GetVncAddressAsync(string vmName, CancellationToken token)
    {
        var result = await RunCommandAsync($"virsh vncdisplay {ShellEscape(vmName)} 2>/dev/null",
            token, throwOnError: false);
        return result.Trim();
    }

    private static Task StopRdpProxyAsync(string vmName)
    {
        if (RdpProxies.TryRemove(vmName, out var proxy))
            proxy.Stop();

        return Task.CompletedTask;
    }

    private static string ShellEscape(string arg) => $"'{arg.Replace("'", "'\\''")}'";

    internal string ResolveTemplatePath(CreateVmRequest request)
    {
        var storageRoot = Path.GetFullPath(_config.ImageStoragePath);
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.TemplatePath))
            candidates.Add(request.TemplatePath);
        if (request.TemplateId.HasValue)
            candidates.Add(Path.Combine(storageRoot, $"{request.TemplateId.Value}.qcow2"));

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (!fullPath.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !string.Equals(fullPath, storageRoot, StringComparison.Ordinal))
                throw new InvalidOperationException("VM template path must be inside the configured image storage directory.");

            if (File.Exists(fullPath))
                return fullPath;
        }

        if (request.TemplateId.HasValue || !string.IsNullOrWhiteSpace(request.TemplatePath))
            throw new FileNotFoundException(
                $"VM template image was not found. TemplateId={request.TemplateId?.ToString() ?? "<none>"}, TemplatePath={request.TemplatePath ?? "<none>"}");

        return string.Empty;
    }

    internal static string BuildVirtInstallNetworkArguments(CreateVmRequest request)
    {
        if (request.Interfaces.Count == 0)
            return "--network network=default,model=e1000e";

        return string.Join(' ', request.Interfaces.Select(BuildVirtInstallNetworkArgument));
    }

    internal static string[] BuildTeamLabVmIpProbeCommands(CreateVmRequest request) =>
        request.Interfaces
            .Where(i => !string.IsNullOrWhiteSpace(i.BridgeName) && !string.IsNullOrWhiteSpace(i.MacAddress))
            .Select(i => $"ip neigh show dev {i.BridgeName} 2>/dev/null")
            .ToArray();

    private static string BuildVirtInstallNetworkArgument(VmNetworkInterfaceRequest iface)
    {
        if (!SafeNamePattern.IsMatch(iface.BridgeName))
            throw new ArgumentException("Invalid VM bridge name.", nameof(iface.BridgeName));

        var model = string.IsNullOrWhiteSpace(iface.Model) ? "e1000e" : iface.Model.Trim();
        if (!SafeNamePattern.IsMatch(model))
            throw new ArgumentException("Invalid VM network model.", nameof(iface.Model));

        var mac = string.Empty;
        if (!string.IsNullOrWhiteSpace(iface.MacAddress))
        {
            if (!SafeMacPattern.IsMatch(iface.MacAddress))
                throw new ArgumentException("Invalid VM MAC address.", nameof(iface.MacAddress));

            mac = $",mac={iface.MacAddress.ToLowerInvariant()}";
        }

        return $"--network bridge={iface.BridgeName},model={model}{mac}";
    }

    private async Task<string> RunCommandAsync(string cmd, CancellationToken token, bool throwOnError = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{cmd}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is null) return "";
        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        var stderrTask = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            if (throwOnError)
            {
                _logger.LogError("Command failed ({ExitCode}): {Command}\n{Error}",
                    process.ExitCode, cmd, message);
                throw new InvalidOperationException(message);
            }

            _logger.LogDebug("Command exited with {ExitCode}: {Command}\n{Error}",
                process.ExitCode, cmd, message);
        }

        return stdout;
    }

    private static string? ParseFirstNonLoopbackIp(string output)
    {
        foreach (var line in output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var candidate = part.Trim(',', ';');
                if (candidate.Contains('/'))
                    candidate = candidate.Split('/')[0];

                if (candidate.Count(ch => ch == '.') != 3 ||
                    candidate.StartsWith("127.") || candidate.StartsWith("169.254.") ||
                    candidate.StartsWith("::1") || candidate.StartsWith("fe80:"))
                    continue;

                if (System.Net.IPAddress.TryParse(candidate, out var address) &&
                    address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return candidate;
            }
        }

        return null;
    }

    private static string? ParseMacAddress(string output)
    {
        foreach (var line in output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.Contains(':') && part.Split(':').Length == 6 && part.Length <= 17)
                    return part.ToLowerInvariant();
            }
        }

        return null;
    }

    internal static string? ParseIpFromDhcpLeases(string leaseOutput, string macAddress)
    {
        var macLower = macAddress.ToLowerInvariant();
        foreach (var line in leaseOutput.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.ToLowerInvariant().Contains(macLower))
                continue;

            var ip = ParseFirstNonLoopbackIp(line);
            if (ip is not null)
                return ip;
        }

        return null;
    }

    private static VmIpLookupResult ParseIpFromTeamLabLeaseFiles(string macAddress, string bridgeName)
    {
        const string root = "/run/gzctf-teamlab";
        if (!Directory.Exists(root))
            return new VmIpLookupResult(null, "TeamLab lease root does not exist.");

        var preferred = TryBuildTeamLabDhcpServiceName(bridgeName);
        var directories = Directory.EnumerateDirectories(root)
            .OrderBy(path => string.Equals(Path.GetFileName(path), preferred, StringComparison.Ordinal) ? 0 : 1)
            .ToArray();
        var checkedFiles = new List<string>();

        foreach (var directory in directories)
        {
            var leaseFile = Path.Combine(directory, "leases");
            if (!File.Exists(leaseFile))
                continue;

            var content = File.ReadAllText(leaseFile);
            checkedFiles.Add($"{Path.GetFileName(directory)}:{content.Trim().Replace('\n', ';')}");
            var ip = ParseIpFromDhcpLeases(content, macAddress);
            if (ip is not null)
                return new VmIpLookupResult(ip, $"Checked TeamLab lease files: {string.Join(" || ", checkedFiles)}");
        }

        return new VmIpLookupResult(null, checkedFiles.Count == 0
            ? $"No TeamLab lease files found. preferred={preferred ?? "<none>"} dirs={string.Join(',', directories.Select(Path.GetFileName))}"
            : $"No matching TeamLab lease. mac={macAddress} preferred={preferred ?? "<none>"} leases={string.Join(" || ", checkedFiles)}");
    }

    private static string SummarizeCommand(string name, string output)
    {
        var summary = output.Trim();
        if (summary.Length > 500)
            summary = summary[..500] + "...";
        return string.IsNullOrWhiteSpace(summary) ? $"{name}=<empty>" : $"{name}={summary.Replace('\n', ';')}";
    }

    private static string? TryBuildTeamLabDhcpServiceName(string bridgeName)
    {
        var match = Regex.Match(bridgeName, @"^tl(?<runtime>\d+)-(?<network>[a-zA-Z0-9\-]+)$");
        if (!match.Success)
            return null;

        var service = $"tldns{match.Groups["runtime"].Value}{match.Groups["network"].Value}";
        return service.Length <= 15 ? service : service[..15];
    }

    private static string? ParseIpFromNeighborTable(string output, string macAddress)
    {
        var macLower = macAddress.ToLowerInvariant();
        foreach (var line in output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.ToLowerInvariant().Contains(macLower))
                continue;

            var candidate = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (candidate is not null
                && IPAddress.TryParse(candidate, out var address)
                && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return candidate;
        }

        return null;
    }

    private static int GetPreferredRdpProxyPort(string vmName)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in vmName)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return RdpProxyPortStart + (int)(hash % RdpProxyPortCount);
        }
    }

    private sealed class RdpProxy
    {
        private readonly string _vmName;
        private readonly TcpListener _listener;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new();

        public IPAddress TargetIp { get; }
        public int Port { get; }

        public RdpProxy(string vmName, IPAddress targetIp, int port, TcpListener listener, ILogger logger)
        {
            _vmName = vmName;
            TargetIp = targetIp;
            Port = port;
            _listener = listener;
            _logger = logger;
        }

        public async Task RunAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = HandleClientAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RDP proxy for VM {VmName} stopped unexpectedly", _vmName);
            }
        }

        public void Stop()
        {
            try { _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            _cts.Dispose();
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using var clientSocket = client;
            try
            {
                using var target = new TcpClient();
                await target.ConnectAsync(TargetIp, 3389, _cts.Token);

                await using var clientStream = clientSocket.GetStream();
                await using var targetStream = target.GetStream();
                var upstream = clientStream.CopyToAsync(targetStream, _cts.Token);
                var downstream = targetStream.CopyToAsync(clientStream, _cts.Token);
                await Task.WhenAny(upstream, downstream);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "RDP proxy client handling failed for VM {VmName}", _vmName);
            }
        }
    }
}

public sealed record VmIpLookupResult(string? IpAddress, string? Diagnostic);
