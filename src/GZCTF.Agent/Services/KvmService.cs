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
    private static readonly ConcurrentDictionary<string, RdpProxy> RdpProxies = new();
    private const int RdpProxyPortStart = 46000;
    private const int RdpProxyPortCount = 10000;

    public KvmService(IOptions<KvmConfig> config, ILogger<KvmService> logger)
    { _config = config.Value; _logger = logger; }

    public async Task<CreateVmResponse?> CreateVmAsync(CreateVmRequest request, CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(request.VmName))
            throw new ArgumentException("Invalid VM name", nameof(request.VmName));

        var templatePath = request.TemplateId.HasValue
            ? Path.Combine(_config.ImageStoragePath, $"{request.TemplateId}.qcow2")
            : "";
        var vmPath = Path.Combine(_config.ImageStoragePath, $"{request.VmName}.qcow2");

        Directory.CreateDirectory(_config.ImageStoragePath);

        await RunCommandAsync($"virsh destroy {ShellEscape(request.VmName)} 2>/dev/null || true", token,
            throwOnError: false);
        await RunCommandAsync($"virsh undefine {ShellEscape(request.VmName)} --remove-all-storage 2>/dev/null || true",
            token, throwOnError: false);
        if (File.Exists(vmPath))
            File.Delete(vmPath);

        if (!string.IsNullOrEmpty(templatePath) && File.Exists(templatePath))
            await RunCommandAsync($"qemu-img create -f qcow2 -b {ShellEscape(templatePath)} -F qcow2 {ShellEscape(vmPath)}", token);
        else
            await RunCommandAsync($"qemu-img create -f qcow2 {ShellEscape(vmPath)} 20G", token);

        await RunCommandAsync(
            $"virt-install --name {ShellEscape(request.VmName)} --memory {request.Memory} --vcpus {request.Cpu} " +
            $"--disk path={ShellEscape(vmPath)} --osinfo detect=on,require=off --import --noautoconsole " +
            "--network network=default,model=e1000e --graphics vnc,listen=0.0.0.0", token);

        var state = (await RunCommandAsync($"virsh domstate {ShellEscape(request.VmName)}", token)).Trim();
        if (!state.Equals("running", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"VM {request.VmName} was created but is not running (state: {state})");

        return new CreateVmResponse
        {
            VmName = request.VmName,
            Status = "Running",
            VncAddress = await GetVncAddressAsync(request.VmName, token)
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

    public async Task<string?> GetIpAddressAsync(string vmName, CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(vmName))
            return null;

        var result = await RunCommandAsync($"virsh domifaddr {ShellEscape(vmName)} --source agent 2>/dev/null",
            token, throwOnError: false);
        var ip = ParseFirstNonLoopbackIp(result);
        if (ip is not null)
            return ip;

        result = await RunCommandAsync($"virsh domifaddr {ShellEscape(vmName)} 2>/dev/null",
            token, throwOnError: false);
        ip = ParseFirstNonLoopbackIp(result);
        if (ip is not null)
            return ip;

        var macResult = await RunCommandAsync($"virsh domiflist {ShellEscape(vmName)} 2>/dev/null",
            token, throwOnError: false);
        var mac = ParseMacAddress(macResult);
        if (mac is null)
            return null;

        var leases = await RunCommandAsync("virsh net-dhcp-leases default 2>/dev/null",
            token, throwOnError: false);
        ip = ParseIpFromDhcpLeases(leases, mac);
        if (ip is not null)
            return ip;

        var neighbours = await RunCommandAsync("ip neigh show dev virbr0 2>/dev/null",
            token, throwOnError: false);
        return ParseIpFromNeighborTable(neighbours, mac);
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
                if (!part.Contains('/') || part.StartsWith("127.") || part.StartsWith("::1") ||
                    part.StartsWith("fe80:"))
                    continue;

                var ip = part.Split('/')[0];
                if (System.Net.IPAddress.TryParse(ip, out var address) &&
                    address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip;
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

    private static string? ParseIpFromDhcpLeases(string leaseOutput, string macAddress)
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
