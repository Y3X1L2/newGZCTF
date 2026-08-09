using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Observation;
using GZCTF.Agent.Services.Vm;
using GZCTF.Agent.Services.GuestControl;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services;

public class KvmService
{
    internal enum VmCreateDisposition
    {
        Create,
        Reuse,
        Replace,
        Conflict
    }

    private readonly KvmConfig _config;
    private readonly ILogger<KvmService> _logger;
    private readonly AgentResourceLock _resourceLock;
    private readonly GuestEnrollmentStore? _guestEnrollmentStore;
    private readonly RdpProxyAccessPolicy _consoleAccessPolicy;
    private readonly GuestManagementConfig _guestManagement;

    private static readonly Regex SafeNamePattern = new(@"^[a-zA-Z0-9_\-]+$", RegexOptions.Compiled);
    private static readonly Regex SafeMacPattern = new(@"^([0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}$", RegexOptions.Compiled);
    private static readonly Regex SafeInterfacePattern = new(@"^[a-zA-Z0-9_\-\.]+$", RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, RdpProxy> RdpProxies = new();
    private const int RdpProxyPortStart = 46000;
    private const int RdpProxyPortCount = 10000;

    public KvmService(
        IOptions<KvmConfig> config,
        AgentResourceLock resourceLock,
        ILogger<KvmService> logger,
        GuestEnrollmentStore? guestEnrollmentStore = null,
        IOptions<AgentConfig>? agentConfig = null)
    {
        _config = config.Value;
        _resourceLock = resourceLock;
        _logger = logger;
        _guestEnrollmentStore = guestEnrollmentStore;
        _guestManagement = agentConfig?.Value.GuestManagement ?? new GuestManagementConfig { Enabled = false };
        _consoleAccessPolicy = RdpProxyAccessPolicy.Create(
            _config.RdpProxyAllowedSources, agentConfig?.Value.ServerUrl);
        foreach (var invalid in _consoleAccessPolicy.InvalidSources)
            _logger.LogError(
                "Ignoring unparsable Kvm:RdpProxyAllowedSources entry {Source}; it grants nothing", invalid);
        if (_consoleAccessPolicy.LoopbackOnly)
            _logger.LogWarning(
                "Console proxy accepts loopback only. Set Kvm:RdpProxyAllowedSources to the platform address, otherwise remote console will be refused.");
    }

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
        var domainExists = !string.IsNullOrWhiteSpace(domainId);
        var domainGeneration = domainExists
            ? await ReadDomainGenerationAsync(request.VmName, token)
            : null;
        var sidecarGeneration = await ReadGenerationAsync(generationPath, token);
        var requestedGeneration = Math.Max(1, request.Generation);
        var disposition = EvaluateCreateDisposition(
            domainExists,
            domainGeneration,
            sidecarGeneration,
            File.Exists(vmPath),
            requestedGeneration);

        if (disposition == VmCreateDisposition.Conflict)
            throw new InvalidOperationException(
                $"runtime_identity_conflict: VM {request.VmName} identity is inconsistent or belongs to an unknown/future generation " +
                $"(domain={domainGeneration?.ToString() ?? "unknown"}, sidecar={sidecarGeneration?.ToString() ?? "unknown"}, requested={requestedGeneration}).");

        if (disposition == VmCreateDisposition.Replace)
        {
            if (_guestEnrollmentStore is not null && domainGeneration is { } replacedGeneration)
                await _guestEnrollmentStore.RevokeVmAsync(
                    request.VmName, replacedGeneration, domainId, token);
            await StopRdpProxyAsync(request.VmName);
            if (domainExists)
            {
                await RunCommandAsync($"virsh destroy {ShellEscape(request.VmName)} 2>/dev/null || true", token);
                await RunCommandAsync(
                    $"virsh undefine {ShellEscape(request.VmName)} --remove-all-storage 2>/dev/null || true", token);
            }
            CleanupVmArtifacts(request.VmName);
            domainId = string.Empty;
            domainExists = false;
            domainGeneration = null;
            sidecarGeneration = null;
        }

        if (disposition == VmCreateDisposition.Reuse)
        {
            var conflict = GetIdentityConflict(
                request.VmName,
                domainId,
                domainGeneration,
                sidecarGeneration,
                requestedGeneration,
                expectedNativeId: null,
                allowMissingSidecar: true);
            if (conflict is not null)
                throw new InvalidOperationException(
                    $"runtime_identity_conflict: {conflict}");
            if (sidecarGeneration is null)
            {
                await File.WriteAllTextAsync(generationPath, requestedGeneration.ToString(), token);
                sidecarGeneration = requestedGeneration;
            }
            var existingState = (await RunCommandAsync($"virsh domstate {ShellEscape(request.VmName)}", token,
                throwOnError: false)).Trim();
            if (!existingState.Equals("running", StringComparison.OrdinalIgnoreCase))
                await RunCommandAsync($"virsh start {ShellEscape(request.VmName)}", token);
            await ConfigureManagementPortIsolationAsync(request, token);
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
            File.Delete(vmPath);

        if (!string.IsNullOrEmpty(templatePath))
            await RunCommandAsync($"qemu-img create -f qcow2 -b {ShellEscape(templatePath)} -F qcow2 {ShellEscape(vmPath)}", token);
        else
            await RunCommandAsync($"qemu-img create -f qcow2 {ShellEscape(vmPath)} 20G", token);

        try
        {
            var mediaArguments = new List<string>();
            if (request.CloudInit?.Enabled == true && request.GuestSupervisor is null)
            {
                var cloudInitFiles = await WriteCloudInitSeedFilesAsync(request, token);
                var directCloudInit = await SupportsVirtInstallCloudInitAsync(token);
                if (!directCloudInit)
                    await CreateCloudInitSeedIsoAsync(cloudInitFiles, token);
                mediaArguments.Add(BuildVirtInstallCloudInitArguments(cloudInitFiles, directCloudInit));
            }
            if (request.GuestSupervisor is not null)
            {
                var configDrive = GuestConfigDriveBuilder.Build(
                    request,
                    Path.Combine(GetRuntimeInjectionDirectory(request.VmName), "guest-config"));
                await CreateIsoAsync(
                    configDrive.IsoPath,
                    configDrive.VolumeLabel,
                    configDrive.Files,
                    token);
                mediaArguments.Add(
                    $"--disk path={ShellEscape(configDrive.IsoPath)},device=cdrom,readonly=on");
            }
            if (request.GuestControl.EndpointSensorChannel)
                mediaArguments.Add(await CreateEndpointSensorInjectionIsoAsync(request, token));

            var domainArguments = VmDomainBuilder.BuildVirtInstallArguments(
                request, vmPath, string.Join(' ', mediaArguments));
            await RunCommandAsync($"virt-install {domainArguments}", token);

            var state = (await RunCommandAsync($"virsh domstate {ShellEscape(request.VmName)}", token)).Trim();
            if (!state.Equals("running", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"VM {request.VmName} was created but is not running (state: {state})");
            await File.WriteAllTextAsync(generationPath, Math.Max(1, request.Generation).ToString(), token);
            domainId = (await RunCommandAsync(
                $"virsh domuuid {ShellEscape(request.VmName)} 2>/dev/null", token, throwOnError: false)).Trim();
            var createdDomainGeneration = string.IsNullOrWhiteSpace(domainId)
                ? null
                : await ReadDomainGenerationAsync(request.VmName, token);
            var createdSidecarGeneration = await ReadGenerationAsync(generationPath, token);
            var createdConflict = GetIdentityConflict(
                request.VmName,
                domainId,
                createdDomainGeneration,
                createdSidecarGeneration,
                requestedGeneration,
                expectedNativeId: null);
            if (createdConflict is not null)
                throw new InvalidOperationException($"runtime_identity_conflict: {createdConflict}");
            await ConfigureManagementPortIsolationAsync(request, token);

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
        catch
        {
            await RunCommandAsync(
                $"virsh destroy {ShellEscape(request.VmName)} 2>/dev/null || true", CancellationToken.None,
                throwOnError: false);
            await RunCommandAsync(
                $"virsh undefine {ShellEscape(request.VmName)} --remove-all-storage 2>/dev/null || true",
                CancellationToken.None, throwOnError: false);
            CleanupVmArtifacts(request.VmName);
            throw;
        }
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
        var generationPath = Path.Combine(_config.ImageStoragePath, $"{vmName}.generation");
        var sidecarGeneration = await ReadGenerationAsync(generationPath, token);
        if (string.IsNullOrWhiteSpace(nativeId))
        {
            var overlayPath = Path.Combine(_config.ImageStoragePath, $"{vmName}.qcow2");
            if (!File.Exists(overlayPath) && sidecarGeneration is null)
            {
                if (_guestEnrollmentStore is not null && expectedGeneration is { } preparedGeneration)
                    await _guestEnrollmentStore.RevokeVmAsync(
                        vmName, Math.Max(1, preparedGeneration), expectedNativeId, token);
                return;
            }
            if (sidecarGeneration is null or < 1 ||
                expectedGeneration is { } requestedGeneration && sidecarGeneration > Math.Max(1, requestedGeneration))
                throw new AgentOperationException(
                    "Conflict", "runtime.identity_conflict",
                    $"VM {vmName} orphaned artifacts have an unknown or future generation.", false,
                    StatusCodes.Status409Conflict);
            if (_guestEnrollmentStore is not null && sidecarGeneration is { } orphanGeneration)
                await _guestEnrollmentStore.RevokeVmAsync(vmName, orphanGeneration, null, token);
            CleanupVmArtifacts(vmName);
            return;
        }
        var domainGeneration = await ReadDomainGenerationAsync(vmName, token);
        var conflict = GetIdentityConflict(
            vmName, nativeId, domainGeneration, sidecarGeneration, expectedGeneration, expectedNativeId);
        if (conflict is not null)
            throw new AgentOperationException(
                "Conflict", "runtime.identity_conflict",
                conflict, false,
                StatusCodes.Status409Conflict);
        await StopRdpProxyAsync(vmName);
        await RunCommandAsync($"virsh destroy {ShellEscape(vmName)} 2>/dev/null || true", token);
        await RunCommandAsync($"virsh undefine {ShellEscape(vmName)} --remove-all-storage 2>/dev/null || true", token);
        if (_guestEnrollmentStore is not null)
            await _guestEnrollmentStore.RevokeVmAsync(
                vmName, domainGeneration!.Value, nativeId, token);
        CleanupVmArtifacts(vmName);
    }

    public Task<bool> SuspendVmAsync(string vmName, int expectedGeneration, CancellationToken token) =>
        ExecuteWithIdentityAsync(
            vmName,
            expectedGeneration,
            null,
            async identityToken =>
            {
                await RunCommandAsync($"virsh suspend {ShellEscape(vmName)}", identityToken);
                return true;
            },
            token);

    public Task<bool> ResumeVmAsync(string vmName, int expectedGeneration, CancellationToken token) =>
        ExecuteWithIdentityAsync(
            vmName,
            expectedGeneration,
            null,
            async identityToken =>
            {
                await RunCommandAsync($"virsh resume {ShellEscape(vmName)}", identityToken);
                return true;
            },
            token);

    public async Task<bool> WaitForCleanShutdownAsync(
        string vmName,
        int timeoutSeconds,
        CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(vmName))
            throw new ArgumentException("Invalid VM name", nameof(vmName));
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 1800);
        if (await IsDomainShutOffAsync(vmName, token))
            return true;
        try
        {
            await RunCommandAsync(
                $"LC_ALL=C virsh event --domain {ShellEscape(vmName)} --event lifecycle --loop --timeout {timeoutSeconds} " +
                "| awk '/Stopped|Shutdown|shut off/{found=1; exit} END{exit found?0:1}'",
                token);
        }
        catch (InvalidOperationException)
        {
        }
        return await IsDomainShutOffAsync(vmName, token);
    }

    async Task<bool> IsDomainShutOffAsync(string vmName, CancellationToken token)
    {
        var state = (await RunCommandAsync(
            $"LC_ALL=C virsh domstate {ShellEscape(vmName)} 2>/dev/null",
            token,
            throwOnError: false)).Trim();
        return state.Contains("shut off", StringComparison.OrdinalIgnoreCase);
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

    internal static bool IsStaleGeneration(int? existingGeneration, int requestedGeneration) =>
        existingGeneration is { } existing && existing >= 1 && existing < Math.Max(1, requestedGeneration);

    internal static VmCreateDisposition EvaluateCreateDisposition(
        bool domainExists,
        int? domainGeneration,
        int? sidecarGeneration,
        bool overlayExists,
        int requestedGeneration)
    {
        requestedGeneration = Math.Max(1, requestedGeneration);
        if (domainExists)
        {
            if (domainGeneration is null or < 1)
                return VmCreateDisposition.Conflict;
            if (sidecarGeneration is { } sidecar && sidecar != domainGeneration)
                return VmCreateDisposition.Conflict;
            if (domainGeneration < requestedGeneration)
                return VmCreateDisposition.Replace;
            return domainGeneration == requestedGeneration
                ? VmCreateDisposition.Reuse
                : VmCreateDisposition.Conflict;
        }

        if (sidecarGeneration is { } orphanGeneration)
        {
            if (orphanGeneration < 1 || orphanGeneration > requestedGeneration)
                return VmCreateDisposition.Conflict;
            return VmCreateDisposition.Replace;
        }

        return overlayExists
            ? VmCreateDisposition.Conflict
            : VmCreateDisposition.Create;
    }

    internal static string? GetIdentityConflict(
        string vmName,
        string? nativeId,
        int? domainGeneration,
        int? sidecarGeneration,
        int? expectedGeneration,
        string? expectedNativeId,
        bool allowMissingSidecar = false)
    {
        if (string.IsNullOrWhiteSpace(nativeId))
            return $"VM {vmName} has no libvirt native identity.";
        if (domainGeneration is null or < 1)
            return $"VM {vmName} has no managed domain generation.";
        if ((sidecarGeneration is null && !allowMissingSidecar) || sidecarGeneration is < 1)
            return $"VM {vmName} has no managed sidecar generation.";
        if (sidecarGeneration is { } actualSidecarGeneration && domainGeneration != actualSidecarGeneration)
            return $"VM {vmName} domain generation {domainGeneration} does not match sidecar generation {sidecarGeneration}.";
        if (expectedGeneration is { } requiredGeneration && domainGeneration != Math.Max(1, requiredGeneration))
            return $"VM {vmName} generation does not match the requested runtime identity.";

        var stableNativeId = VmDomainBuilder.BuildStableDomainId(vmName, domainGeneration.Value).ToString("D");
        if (!nativeId.Equals(stableNativeId, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(expectedNativeId) &&
            !nativeId.Equals(expectedNativeId.Trim(), StringComparison.OrdinalIgnoreCase))
            return $"VM {vmName} native identity does not match the requested runtime identity.";
        return null;
    }

    public async Task<T> ExecuteWithIdentityAsync<T>(
        string vmName,
        int? expectedGeneration,
        string? expectedNativeId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(vmName))
            throw new AgentOperationException(
                "Conflict", "runtime.identity_conflict", "VM name is invalid.", false,
                StatusCodes.Status409Conflict);

        await using var identityLock = await _resourceLock.AcquireAsync($"vm:{vmName}", token);
        var nativeId = (await RunCommandAsync(
            $"virsh domuuid {ShellEscape(vmName)} 2>/dev/null", token, throwOnError: false)).Trim();
        var domainGeneration = string.IsNullOrWhiteSpace(nativeId)
            ? null
            : await ReadDomainGenerationAsync(vmName, token);
        var sidecarGeneration = await ReadGenerationAsync(
            Path.Combine(_config.ImageStoragePath, $"{vmName}.generation"), token);
        var conflict = GetIdentityConflict(
            vmName, nativeId, domainGeneration, sidecarGeneration, expectedGeneration, expectedNativeId);
        if (conflict is not null)
            throw new AgentOperationException(
                "Conflict", "runtime.identity_conflict", conflict, false,
                StatusCodes.Status409Conflict);

        return await action(token);
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

    // A bulk "restore every running VM's console proxy" pass used to run on every heartbeat, which
    // meant every VM on the node permanently carried an open forwarder regardless of whether anyone
    // had asked for a console. Proxies are now created only through the authenticated VmController
    // endpoints, on demand, and torn down with the VM.

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
        var ip = ParsePreferredInterfaceIp(result, interfaces);
        if (ip is not null)
            return new VmIpLookupResult(ip, "Matched virsh domifaddr --source agent.");

        result = await RunCommandAsync($"virsh domifaddr {ShellEscape(vmName)} 2>/dev/null",
            token, throwOnError: false);
        diagnostics.Add(SummarizeCommand("domifaddr", result));
        ip = ParsePreferredInterfaceIp(result, interfaces);
        if (ip is not null)
            return new VmIpLookupResult(ip, "Matched virsh domifaddr.");

        if (interfaces is { Count: > 0 })
        {
            foreach (var iface in PreferredInterfaces(interfaces).Where(i =>
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

    internal static async Task<bool> IsTcpPortReadyAsync(
        string ipAddress,
        int targetPort,
        CancellationToken token)
    {
        if (!IPAddress.TryParse(ipAddress, out var ip) || targetPort is < 1 or > 65535)
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var client = new TcpClient(ip.AddressFamily);
            await client.ConnectAsync(ip, targetPort, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public Task<int?> EnsureRdpProxyAsync(
        string vmName,
        string ipAddress,
        int targetPort,
        CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(vmName) ||
            !IPAddress.TryParse(ipAddress, out var ip) ||
            targetPort is < 1 or > 65535)
            return Task.FromResult<int?>(null);

        if (RdpProxies.TryGetValue(vmName, out var existing) &&
            existing.TargetIp.Equals(ip) &&
            existing.TargetPort == targetPort)
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
                var proxy = new RdpProxy(
                    vmName, ip, targetPort, port, listener, _logger, _consoleAccessPolicy);
                if (RdpProxies.TryAdd(vmName, proxy))
                {
                    _ = proxy.RunAsync();
                    _logger.LogInformation(
                        "Console proxy for VM {VmName}: :{Port} -> {Ip}:{TargetPort}, accepting {Sources}",
                        vmName, port, ipAddress, targetPort,
                        _consoleAccessPolicy.LoopbackOnly ? "loopback only" : "configured platform sources");
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

    public async Task<VmIpLookupResult> GetManagementIpAddressWithDiagnosticAsync(
        string vmName,
        CancellationToken token)
    {
        if (!SafeNamePattern.IsMatch(vmName))
            return new VmIpLookupResult(null, "Invalid VM name.");
        if (!_guestManagement.Enabled || !IPAddress.TryParse(_guestManagement.HostAddress, out var hostAddress) ||
            hostAddress.AddressFamily != AddressFamily.InterNetwork ||
            _guestManagement.PrefixLength is < 1 or > 32)
            return new VmIpLookupResult(null, "Guest management network is unavailable.");

        var output = await RunCommandAsync(
            $"virsh domifaddr {ShellEscape(vmName)} --source agent 2>/dev/null",
            token,
            throwOnError: false);
        var managementAddress = ParseAddressInSubnet(output, hostAddress, _guestManagement.PrefixLength);
        return new VmIpLookupResult(
            managementAddress,
            managementAddress is null
                ? $"No guest management address found in {_guestManagement.HostAddress}/{_guestManagement.PrefixLength}."
                : "Matched the guest management address through QEMU Guest Agent.");
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
        var interfaces = request.Interfaces.ToList();
        if (request.ManagementInterface is { } management)
            interfaces.Add(new VmNetworkInterfaceRequest
            {
                BridgeName = management.BridgeName,
                MacAddress = management.MacAddress,
                Model = management.Model,
                InterfaceName = "gzmgmt0",
                IpAddress = management.IpAddress,
                PrefixLength = management.PrefixLength
            });
        if (interfaces.Count == 0)
        {
            var model = string.IsNullOrWhiteSpace(request.DefaultNetworkModel)
                ? "e1000e"
                : request.DefaultNetworkModel.Trim();
            if (!SafeNamePattern.IsMatch(model))
                throw new ArgumentException("Invalid default VM network model.", nameof(request.DefaultNetworkModel));
            return $"--network network=default,model={model}";
        }

        return string.Join(' ', interfaces.Select(BuildVirtInstallNetworkArgument));
    }

    internal static string? BuildManagementPortIsolationCommand(CreateVmRequest request)
    {
        if (request.ManagementInterface is not { } management) return null;
        if (!SafeNamePattern.IsMatch(request.VmName) || !SafeNamePattern.IsMatch(management.BridgeName))
            throw new ArgumentException("Invalid VM management interface identity.", nameof(request));
        return $"tap=$(virsh domiflist {ShellEscape(request.VmName)} | " +
               $"awk '$3 == \"{management.BridgeName}\" {{ print $1; exit }}'); " +
               "test -n \"$tap\" && bridge link set dev \"$tap\" isolated on";
    }

    private async Task ConfigureManagementPortIsolationAsync(
        CreateVmRequest request,
        CancellationToken cancellationToken)
    {
        var command = BuildManagementPortIsolationCommand(request);
        if (command is not null) await RunCommandAsync(command, cancellationToken);
    }

    internal static string BuildVirtInstallBootAndDiskArguments(CreateVmRequest request, string vmPath)
    {
        var disk = $"--disk path={ShellEscape(vmPath)}";
        return request.CloudInit?.OsType == VmInitOsType.Windows
            ? $"--machine q35 --boot uefi --events on_reboot=restart {disk},bus=sata"
            : disk;
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

        var target = string.Empty;
        if (!string.IsNullOrWhiteSpace(iface.HostInterfaceName))
        {
            var hostInterfaceName = iface.HostInterfaceName.Trim();
            if (!SafeInterfacePattern.IsMatch(hostInterfaceName) || hostInterfaceName.Length > 15)
                throw new ArgumentException("Invalid VM host interface name.", nameof(iface.HostInterfaceName));
            target = $",target.dev={hostInterfaceName}";
        }

        return $"--network bridge={iface.BridgeName},model={model}{mac}{target}";
    }

    internal static string BuildCloudInitNetworkConfig(CreateVmRequest request)
    {
        var interfaces = request.CloudInit?.NetworkMode == VmInitNetworkMode.Preconfigured
            ? new List<VmNetworkInterfaceRequest>()
            : request.Interfaces.Where(iface => !string.IsNullOrWhiteSpace(iface.MacAddress)).ToList();
        if (request.ManagementInterface is { } management)
            interfaces.Add(new VmNetworkInterfaceRequest
            {
                BridgeName = management.BridgeName,
                MacAddress = management.MacAddress,
                Model = management.Model,
                InterfaceName = "gzmgmt0",
                IpAddress = management.IpAddress,
                PrefixLength = management.PrefixLength
            });
        if (interfaces.Count == 0)
            return "version: 2\nethernets: {}\n";

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("version: 2");
        builder.AppendLine("ethernets:");

        var index = 0;
        foreach (var iface in interfaces)
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
            var useDhcp = request.CloudInit?.NetworkMode == VmInitNetworkMode.Dhcp &&
                          !string.Equals(name, "gzmgmt0", StringComparison.Ordinal);
            if (useDhcp)
            {
                builder.AppendLine("    dhcp4: true");
                builder.AppendLine("    dhcp6: false");
                builder.AppendLine("    optional: true");
                AppendRoutes(builder, iface);
                index++;
                continue;
            }
            builder.AppendLine("    dhcp4: false");
            builder.AppendLine("    dhcp6: false");
            builder.AppendLine($"    addresses: [{iface.IpAddress}/{iface.PrefixLength}]");

            if (iface.IsPrimary && !string.IsNullOrWhiteSpace(iface.Gateway))
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

            AppendRoutes(builder, iface);

            index++;
        }

        return builder.ToString();
    }

    private static void AppendRoutes(System.Text.StringBuilder builder, VmNetworkInterfaceRequest iface)
    {
        var routes = iface.Routes
            .Select(ParseRoute)
            .Cast<(string To, string Via)>()
            .ToArray();
        if (routes.Length == 0) return;
        builder.AppendLine("    routes:");
        foreach (var route in routes)
        {
            builder.AppendLine($"      - to: {route.To}");
            builder.AppendLine($"        via: {route.Via}");
        }
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
        await CreateIsoAsync(
            files.IsoPath,
            "CIDATA",
            [
                ("user-data", Path.Combine(dir, "user-data")),
                ("meta-data", Path.Combine(dir, "meta-data")),
                ("network-config", Path.Combine(dir, "network-config"))
            ],
            token);
    }

    private async Task<string> CreateEndpointSensorInjectionIsoAsync(
        CreateVmRequest request,
        CancellationToken token)
    {
        var osType = request.GuestControl.OsType
                     ?? throw new InvalidOperationException("Endpoint sensor injection requires a VM OS type.");
        var sourcePath = osType == VmInitOsType.Windows
            ? EndpointSensorChannelService.WindowsSensorPath
            : EndpointSensorChannelService.LinuxSensorPath;
        var fileName = osType == VmInitOsType.Windows
            ? EndpointSensorChannelService.WindowsSensorFileName
            : EndpointSensorChannelService.LinuxSensorFileName;
        if (!File.Exists(sourcePath))
            throw new InvalidOperationException($"Endpoint sensor artifact is unavailable for {osType}.");

        var root = Path.Combine(GetRuntimeInjectionDirectory(request.VmName), "endpoint-sensor");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        var isoPath = Path.Combine(root, "endpoint-sensor.iso");
        await CreateIsoAsync(
            isoPath,
            EndpointSensorChannelService.InjectionVolumeLabel,
            [(fileName, sourcePath)],
            token);
        return $"--disk path={ShellEscape(isoPath)},device=cdrom,readonly=on";
    }

    private async Task CreateIsoAsync(
        string outputPath,
        string volumeLabel,
        IReadOnlyList<(string TargetName, string SourcePath)> files,
        CancellationToken token)
    {
        var resolved = await RunCommandAsync(
            "command -v genisoimage || command -v mkisofs || command -v xorriso",
            token,
            throwOnError: false);
        var tool = resolved.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tool))
            throw new InvalidOperationException("VM injection media requires genisoimage, mkisofs, or xorriso.");
        var grafts = string.Join(' ', files.Select(file =>
            $"{file.TargetName}={ShellEscape(file.SourcePath)}"));
        var compatibility = tool.EndsWith("xorriso", StringComparison.Ordinal) ? "-as mkisofs " : string.Empty;
        await RunCommandAsync(
            $"{ShellEscape(tool)} {compatibility}-output {ShellEscape(outputPath)} " +
            $"-volid {ShellEscape(volumeLabel)} -joliet -rock -graft-points {grafts}",
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

    private string GetRuntimeInjectionDirectory(string vmName) =>
        Path.Combine(_config.ImageStoragePath, "runtime-injection", vmName);

    private void CleanupCloudInitSeed(string vmName)
    {
        var root = GetCloudInitSeedDirectory(vmName);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private void CleanupVmArtifacts(string vmName)
    {
        CleanupCloudInitSeed(vmName);
        var injectionRoot = GetRuntimeInjectionDirectory(vmName);
        if (Directory.Exists(injectionRoot)) Directory.Delete(injectionRoot, recursive: true);
        var overlayPath = Path.Combine(_config.ImageStoragePath, $"{vmName}.qcow2");
        if (File.Exists(overlayPath)) File.Delete(overlayPath);
        var generationPath = Path.Combine(_config.ImageStoragePath, $"{vmName}.generation");
        if (File.Exists(generationPath)) File.Delete(generationPath);
        var bootstrapPath = Path.Combine("/var/lib/gzctf/vm-runtime", vmName);
        if (Directory.Exists(bootstrapPath)) Directory.Delete(bootstrapPath, recursive: true);
    }

    private async Task<string> RunCommandAsync(string cmd, CancellationToken token, bool throwOnError = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(cmd);
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

    private static string? ParseAddressInSubnet(string output, IPAddress networkAddress, int prefixLength)
    {
        var network = ToUInt32(networkAddress);
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        foreach (var line in output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        foreach (var part in line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = part.Trim(',', ';');
            if (candidate.Contains('/')) candidate = candidate.Split('/')[0];
            if (!IPAddress.TryParse(candidate, out var address) ||
                address.AddressFamily != AddressFamily.InterNetwork ||
                (ToUInt32(address) & mask) != (network & mask))
                continue;
            return address.ToString();
        }

        return null;
    }

    internal static string? ParsePreferredInterfaceIp(
        string output,
        IReadOnlyList<VmNetworkInterfaceRequest>? interfaces)
    {
        if (interfaces is not { Count: > 0 })
            return ParseFirstNonLoopbackIp(output);

        var lines = output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var preferredInterfaces = PreferredInterfaces(interfaces);
        foreach (var iface in preferredInterfaces)
        {
            if (string.IsNullOrWhiteSpace(iface.MacAddress) || !SafeMacPattern.IsMatch(iface.MacAddress))
                continue;
            var macAddress = iface.MacAddress.ToLowerInvariant();
            foreach (var line in lines)
            {
                if (!line.Contains(macAddress, StringComparison.OrdinalIgnoreCase))
                    continue;
                var ip = ParseFirstNonLoopbackIp(line);
                if (ip is not null &&
                    (string.IsNullOrWhiteSpace(iface.IpAddress) ||
                     ip.Equals(iface.IpAddress, StringComparison.Ordinal)))
                    return ip;
            }
        }

        foreach (var iface in preferredInterfaces)
        {
            if (string.IsNullOrWhiteSpace(iface.IpAddress))
                continue;
            foreach (var line in lines)
            {
                var ip = ParseFirstNonLoopbackIp(line);
                if (ip?.Equals(iface.IpAddress, StringComparison.Ordinal) == true)
                    return ip;
            }
        }

        return null;
    }

    private static IReadOnlyList<VmNetworkInterfaceRequest> PreferredInterfaces(
        IReadOnlyList<VmNetworkInterfaceRequest> interfaces)
    {
        var primary = interfaces.Where(item => item.IsPrimary).ToArray();
        return primary.Length > 0 ? primary : interfaces;
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
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
        private readonly RdpProxyAccessPolicy _accessPolicy;
        private readonly CancellationTokenSource _cts = new();

        public IPAddress TargetIp { get; }
        public int TargetPort { get; }
        public int Port { get; }

        public RdpProxy(
            string vmName,
            IPAddress targetIp,
            int targetPort,
            int port,
            TcpListener listener,
            ILogger logger,
            RdpProxyAccessPolicy accessPolicy)
        {
            _vmName = vmName;
            TargetIp = targetIp;
            TargetPort = targetPort;
            Port = port;
            _listener = listener;
            _logger = logger;
            _accessPolicy = accessPolicy;
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
            // The forwarded stream reaches a tenant VM's RDP port with no protocol-level
            // authentication of its own, so the peer address is the only thing that can be checked
            // before bytes cross into the guest.
            var peer = (clientSocket.Client.RemoteEndPoint as IPEndPoint)?.Address;
            if (!_accessPolicy.IsAllowed(peer))
            {
                _logger.LogWarning(
                    "Rejected console proxy connection from {Peer} for VM {VmName}; add the source to Kvm:RdpProxyAllowedSources if it is the platform",
                    peer?.ToString() ?? "unknown", _vmName);
                return;
            }

            try
            {
                using var target = new TcpClient();
                await target.ConnectAsync(TargetIp, TargetPort, _cts.Token);

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
