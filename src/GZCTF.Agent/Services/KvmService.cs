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
    private readonly AgentResourceLock _resourceLock;

    private static readonly Regex SafeNamePattern = new(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled);
    private static readonly Regex SafeMacPattern = new(@"^([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$", RegexOptions.Compiled);
    private static readonly Regex SafeInterfacePattern = new(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, RdpProxy> RdpProxies = new();
    private const int RdpProxyPortStart = 46000;
    private const int RdpProxyPortCount = 10000;

    public KvmService(IOptions<KvmConfig> config, AgentResourceLock resourceLock, ILogger<KvmService> logger)
    { _config = config.Value; _resourceLock = resourceLock; _logger = logger; }

    public async Task<CreateVmResponse?> CreateVmAsync(CreateVmRequest request, CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(request.VmName))
            throw new ArgumentException("Invalid VM name", nameof(request.VmName));

        await using var identityLock = await _resourceLock.AcquireAsync($"vm:{request.VmName}", token);

        var templatePath = ResolveTemplatePath(request);
        var vmPath = Path.Combine(_config.ImageStoragePath, $"{request.VmName}.qcow2");
        var generationPath = Path.Combine(_config.ImageStoragePath, $"{request.VmName}.generation");

        Directory.CreateDirectory(_config.ImageStoragePath);

        var domainId = (await RunCommandAsync($"virsh domuuid {ShellEscape(request.VmName)} 2>/dev/null", token,
            throwOnError: false)).Trim();
        var recordedGeneration = await ReadGenerationAsync(generationPath, token);
        if (!string.IsNullOrWhiteSpace(domainId))
        {
            recordedGeneration ??= await ReadDomainGenerationAsync(request.VmName, token);
            if (recordedGeneration != request.Generation)
                throw new InvalidOperationException(
                    $"runtime_identity_conflict: VM {request.VmName} exists with generation {recordedGeneration?.ToString() ?? "unknown"}.");
            if (!File.Exists(generationPath))
                await File.WriteAllTextAsync(generationPath, Math.Max(1, request.Generation).ToString(), token);
            var existingState = (await RunCommandAsync($"virsh domstate {ShellEscape(request.VmName)}", token,
                throwOnError: false)).Trim();
            if (!existingState.Equals("running", StringComparison.OrdinalIgnoreCase))
                await RunCommandAsync($"virsh start {ShellEscape(request.VmName)}", token);
            return new CreateVmResponse
            {
                VmName = request.VmName,
                NativeId = domainId,
                Generation = Math.Max(1, request.Generation),
                Status = "Running",
                VncAddress = await GetVncAddressAsync(request.VmName, token),
                Interfaces = request.Interfaces
            };
        }
        if (File.Exists(vmPath))
        {
            if (recordedGeneration is not null && recordedGeneration != request.Generation)
                throw new InvalidOperationException(
                    $"runtime_identity_conflict: VM overlay {request.VmName} belongs to generation {recordedGeneration}.");
            File.Delete(vmPath);
        }

        if (!string.IsNullOrEmpty(templatePath))
            await RunCommandAsync($"qemu-img create -f qcow2 -b {ShellEscape(templatePath)} -F qcow2 {ShellEscape(vmPath)}", token);
        else
            await RunCommandAsync($"qemu-img create -f qcow2 {ShellEscape(vmPath)} 20G", token);

        CloudInitSeedFiles? cloudInitFiles = null;
        var cloudInitArgs = string.Empty;
        if (request.CloudInit?.Enabled == true)
        {
            cloudInitFiles = await WriteCloudInitSeedFilesAsync(request, token);
            var directCloudInit = await SupportsVirtInstallCloudInitAsync(token);
            if (!directCloudInit)
                await CreateCloudInitSeedIsoAsync(cloudInitFiles, token);
            cloudInitArgs = BuildVirtInstallCloudInitArguments(cloudInitFiles, directCloudInit) + " ";
        }

        await RunCommandAsync(
            $"virt-install --name {ShellEscape(request.VmName)} --memory {request.Memory} --vcpus {request.Cpu} " +
            $"--metadata description={ShellEscape($"gzctf-generation={Math.Max(1, request.Generation)}")} " +
            $"--disk path={ShellEscape(vmPath)} --osinfo detect=on,require=off --import --noautoconsole " +
            $"{cloudInitArgs}{BuildVirtInstallNetworkArguments(request)} --graphics vnc,listen=0.0.0.0", token);

        var state = (await RunCommandAsync($"virsh domstate {ShellEscape(request.VmName)}", token)).Trim();
        if (!state.Equals("running", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"VM {request.VmName} was created but is not running (state: {state})");
        await File.WriteAllTextAsync(generationPath, Math.Max(1, request.Generation).ToString(), token);
        domainId = (await RunCommandAsync(
            $"virsh domuuid {ShellEscape(request.VmName)} 2>/dev/null", token, throwOnError: false)).Trim();
        if (string.IsNullOrWhiteSpace(domainId))
            throw new InvalidOperationException($"VM {request.VmName} has no stable libvirt domain identity.");

        return new CreateVmResponse
        {
            VmName = request.VmName,
            NativeId = domainId,
            Generation = Math.Max(1, request.Generation),
            Status = "Running",
            VncAddress = await GetVncAddressAsync(request.VmName, token),
            Interfaces = request.Interfaces
        };
    }

    public async Task DestroyVmAsync(
        string vmName,
        int? expectedGeneration,
        string? expectedNativeId,
        CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(vmName)) return;
        await using var identityLock = await _resourceLock.AcquireAsync($"vm:{vmName}", token);
        var nativeId = (await RunCommandAsync(
            $"virsh domuuid {ShellEscape(vmName)} 2>/dev/null", token, throwOnError: false)).Trim();
        if (string.IsNullOrWhiteSpace(nativeId))
            return;
        var generation = await ReadDomainGenerationAsync(vmName, token);
        if (expectedGeneration is { } requiredGeneration && generation != requiredGeneration)
            throw new AgentOperationException(
                "Conflict", "runtime.identity_conflict",
                $"VM {vmName} generation does not match the requested runtime identity.", false,
                StatusCodes.Status409Conflict);
        if (!string.IsNullOrWhiteSpace(expectedNativeId) &&
            !nativeId.Equals(expectedNativeId, StringComparison.OrdinalIgnoreCase))
            throw new AgentOperationException(
                "Conflict", "runtime.identity_conflict",
                $"VM {vmName} native identity does not match the requested runtime identity.", false,
                StatusCodes.Status409Conflict);
        await StopRdpProxyAsync(vmName);
        await RunCommandAsync($"virsh destroy {ShellEscape(vmName)} 2>/dev/null || true", token);
        await RunCommandAsync($"virsh undefine {ShellEscape(vmName)} --remove-all-storage 2>/dev/null || true", token);
        CleanupCloudInitSeed(vmName);
        var generationPath = Path.Combine(_config.ImageStoragePath, $"{vmName}.generation");
        if (File.Exists(generationPath)) File.Delete(generationPath);
    }

    static async Task<int?> ReadGenerationAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return null;
        var value = await File.ReadAllTextAsync(path, token);
        return int.TryParse(value.Trim(), out var generation) ? generation : null;
    }

    async Task<int?> ReadDomainGenerationAsync(string vmName, CancellationToken token)
    {
        var description = await RunCommandAsync(
            $"virsh desc {ShellEscape(vmName)} 2>/dev/null", token, throwOnError: false);
        return ParseDomainGeneration(description);
    }

    internal static int? ParseDomainGeneration(string description)
    {
        const string marker = "gzctf-generation=";
        var index = description.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return null;
        var value = description[(index + marker.Length)..]
            .Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return int.TryParse(value, out var generation) ? generation : null;
    }

    public async Task<int> GetVmCountAsync(CancellationToken token)
    {
        var result = await RunCommandAsync("virsh list --name 2>/dev/null", token, throwOnError: false);
        return result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public async Task<IReadOnlyList<RuntimeInventoryResource>> GetManagedRuntimeInventoryAsync(
        CancellationToken token)
    {
        var result = await RunCommandAsync("virsh list --all --name 2>/dev/null", token, throwOnError: false);
        List<RuntimeInventoryResource> inventory = [];
        foreach (var vmName in result.Split('\n',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            token.ThrowIfCancellationRequested();
            if (!SafeNamePattern.IsMatch(vmName))
                continue;
            var generation = await ReadDomainGenerationAsync(vmName, token);
            if (generation is null or < 1)
                continue;
            var domainId = (await RunCommandAsync(
                $"virsh domuuid {ShellEscape(vmName)} 2>/dev/null", token, throwOnError: false)).Trim();
            if (string.IsNullOrWhiteSpace(domainId))
                continue;
            var state = (await RunCommandAsync(
                $"virsh domstate {ShellEscape(vmName)} 2>/dev/null", token, throwOnError: false)).Trim();
            inventory.Add(new RuntimeInventoryResource(
                domainId,
                vmName,
                generation.Value,
                string.IsNullOrWhiteSpace(state) ? "unknown" : state));
        }

        return inventory.OrderBy(item => item.StableName, StringComparer.Ordinal).ToArray();
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

    internal static string BuildCloudInitNetworkConfig(CreateVmRequest request)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("version: 2");
        builder.AppendLine("ethernets:");

        var index = 0;
        foreach (var iface in request.Interfaces.Where(HasStaticCloudInitNetworkIntent))
        {
            var macAddress = iface.MacAddress!;
            if (!SafeMacPattern.IsMatch(macAddress))
                throw new ArgumentException("Invalid VM MAC address.", nameof(iface.MacAddress));

            var name = string.IsNullOrWhiteSpace(iface.InterfaceName) ? $"eth{index}" : iface.InterfaceName.Trim();
            if (!SafeInterfacePattern.IsMatch(name))
                throw new ArgumentException("Invalid VM interface name.", nameof(iface.InterfaceName));

            if (!IsValidIpv4(iface.IpAddress))
                throw new ArgumentException("Invalid VM IP address.", nameof(iface.IpAddress));

            if (iface.PrefixLength is < 1 or > 32)
                throw new ArgumentException("Invalid VM IP prefix length.", nameof(iface.PrefixLength));

            builder.AppendLine($"  {name}:");
            builder.AppendLine("    match:");
            builder.AppendLine($"      macaddress: \"{macAddress.ToLowerInvariant()}\"");
            builder.AppendLine($"    set-name: {name}");
            builder.AppendLine("    dhcp4: false");
            builder.AppendLine("    dhcp6: false");
            builder.AppendLine($"    addresses: [{iface.IpAddress}/{iface.PrefixLength}]");

            if (!string.IsNullOrWhiteSpace(iface.Gateway))
            {
                if (!IsValidIpv4(iface.Gateway))
                    throw new ArgumentException("Invalid VM gateway address.", nameof(iface.Gateway));
                builder.AppendLine($"    gateway4: {iface.Gateway}");
            }

            var dns = iface.DnsServers
                .Where(server => !string.IsNullOrWhiteSpace(server))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (dns.Any(server => !IsValidIpv4(server)))
                throw new ArgumentException("Invalid VM DNS server address.", nameof(iface.DnsServers));

            if (dns.Length > 0)
            {
                builder.AppendLine("    nameservers:");
                builder.AppendLine($"      addresses: [{string.Join(", ", dns)}]");
            }

            var routes = iface.Routes
                .Select(ParseRoute)
                .Cast<(string To, string Via)>()
                .ToArray();
            if (routes.Length > 0)
            {
                builder.AppendLine("    routes:");
                foreach (var route in routes)
                {
                    builder.AppendLine($"      - to: {route.To}");
                    builder.AppendLine($"        via: {route.Via}");
                }
            }

            index++;
        }

        return builder.ToString();
    }

    internal static string BuildVirtInstallCloudInitArguments(CloudInitSeedFiles files, bool useDirectCloudInit)
    {
        return useDirectCloudInit
            ? "--cloud-init " +
              $"user-data={ShellEscape(files.UserDataPath)},meta-data={ShellEscape(files.MetaDataPath)},network-config={ShellEscape(files.NetworkConfigPath)}"
            : $"--disk path={ShellEscape(files.IsoPath)},device=cdrom";
    }

    private async Task<CloudInitSeedFiles> WriteCloudInitSeedFilesAsync(CreateVmRequest request,
        CancellationToken token)
    {
        var root = GetCloudInitSeedDirectory(request.VmName);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var files = new CloudInitSeedFiles(
            Path.Combine(root, "user-data"),
            Path.Combine(root, "meta-data"),
            Path.Combine(root, "network-config"),
            Path.Combine(root, "seed.iso"));

        var cloudInit = request.CloudInit!;
        await File.WriteAllTextAsync(files.UserDataPath, cloudInit.UserData, token);
        await File.WriteAllTextAsync(files.MetaDataPath, cloudInit.MetaData, token);
        await File.WriteAllTextAsync(files.NetworkConfigPath,
            string.IsNullOrWhiteSpace(cloudInit.NetworkConfig)
                ? BuildCloudInitNetworkConfig(request)
                : cloudInit.NetworkConfig,
            token);
        if (!OperatingSystem.IsWindows())
        {
            var sensitiveMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(files.UserDataPath, sensitiveMode);
            File.SetUnixFileMode(files.MetaDataPath, sensitiveMode);
            File.SetUnixFileMode(files.NetworkConfigPath, sensitiveMode);
        }
        return files;
    }

    private async Task<bool> SupportsVirtInstallCloudInitAsync(CancellationToken token)
    {
        var result = await RunCommandAsync("virt-install --help 2>/dev/null | grep -q -- '--cloud-init' && echo yes || echo no",
            token, throwOnError: false);
        return result.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private async Task CreateCloudInitSeedIsoAsync(CloudInitSeedFiles files, CancellationToken token)
    {
        var dir = Path.GetDirectoryName(files.UserDataPath)!;
        var genisoimage = await RunCommandAsync("command -v genisoimage || command -v mkisofs || command -v xorriso",
            token, throwOnError: false);
        var tool = genisoimage.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tool))
            throw new InvalidOperationException("cloud-init seed ISO requires genisoimage, mkisofs, or xorriso.");

        if (tool.EndsWith("xorriso", StringComparison.Ordinal))
        {
            await RunCommandAsync(
                $"{ShellEscape(tool)} -as mkisofs -output {ShellEscape(files.IsoPath)} -volid CIDATA -joliet -rock " +
                "-graft-points " +
                $"user-data={ShellEscape(Path.Combine(dir, "user-data"))} " +
                $"meta-data={ShellEscape(Path.Combine(dir, "meta-data"))} " +
                $"network-config={ShellEscape(Path.Combine(dir, "network-config"))}",
                token);
            return;
        }

        await RunCommandAsync(
            $"{ShellEscape(tool)} -output {ShellEscape(files.IsoPath)} -volid CIDATA -joliet -rock " +
            "-graft-points " +
            $"user-data={ShellEscape(Path.Combine(dir, "user-data"))} " +
            $"meta-data={ShellEscape(Path.Combine(dir, "meta-data"))} " +
            $"network-config={ShellEscape(Path.Combine(dir, "network-config"))}",
            token);
    }

    private static (string To, string Via)? ParseRoute(string route)
    {
        var parts = route.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !string.Equals(parts[1], "via", StringComparison.OrdinalIgnoreCase) ||
            !IsValidIpv4Cidr(parts[0]) || !IsValidIpv4(parts[2]))
            throw new ArgumentException("Invalid VM static route.", nameof(route));

        return (parts[0], parts[2]);
    }

    private static bool HasStaticCloudInitNetworkIntent(VmNetworkInterfaceRequest iface) =>
        !string.IsNullOrWhiteSpace(iface.MacAddress) ||
        !string.IsNullOrWhiteSpace(iface.IpAddress) ||
        iface.PrefixLength.HasValue ||
        !string.IsNullOrWhiteSpace(iface.Gateway) ||
        iface.DnsServers.Count > 0 ||
        iface.Routes.Count > 0;

    private static bool IsValidIpv4(string? value) =>
        IPAddress.TryParse(value, out var address) &&
        address.AddressFamily == AddressFamily.InterNetwork;

    private static bool IsValidIpv4Cidr(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 &&
               IsValidIpv4(parts[0]) &&
               int.TryParse(parts[1], out var prefix) &&
               prefix is >= 1 and <= 32;
    }

    private string GetCloudInitSeedDirectory(string vmName) =>
        Path.Combine(_config.ImageStoragePath, "cloud-init", vmName);

    private void CleanupCloudInitSeed(string vmName)
    {
        var root = GetCloudInitSeedDirectory(vmName);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
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
