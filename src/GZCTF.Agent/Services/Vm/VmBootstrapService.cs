using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.RuntimeSignals;

namespace GZCTF.Agent.Services.Vm;

public sealed partial class VmBootstrapService(
    VmGuestAgentService guest,
    ILogger<VmBootstrapService> logger,
    AgentRuntimeSignalPublisher? signals = null)
{
    internal const string WindowsPowerShellPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    private const string WindowsIcaclsPath = @"C:\Windows\System32\icacls.exe";
    private const int MaxArchiveEntries = 1024;
    private const long MaxExpandedBytes = 256L * 1024 * 1024;
    private const int StageExtractTimeoutSeconds = 300;
    private const string InventoryRoot = "/var/lib/gzctf/bootstrap-state";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<VmBootstrapApplyResponse> ApplyAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ApplyCoreAsync(vmName, request, cancellationToken);
        }
        catch (VmBootstrapCommandException exception)
        {
            return Failed(
                exception.Stage,
                exception.Message,
                exception.ErrorCode,
                exception.StepId,
                exception.Category,
                exception.ExitCode);
        }
    }

    private async Task<VmBootstrapApplyResponse> ApplyCoreAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        CancellationToken cancellationToken)
    {
        var ready = await guest.WaitReadyAsync(vmName, TimeSpan.FromMinutes(3), cancellationToken);
        if (!ready.Ready)
            return Failed("guest-ready", ready.Message);

        if (request.OsType == VmInitOsType.Windows)
            await ConfigureWindowsNetworkAsync(vmName, request.Interfaces, cancellationToken);

        var markerPath = MarkerPath(request.OsType);
        await WriteGuestTextAsync(vmName, request.OsType, markerPath,
            $"{request.RuntimeId}:{request.Generation}:{request.AssetKey}", "0600", cancellationToken);

        if (request.ProfileId is null)
        {
            var response = new VmBootstrapApplyResponse(
                true, "guest-ready", "Guest control is ready; no bootstrap profile was requested.", 0, [], []);
            await WriteInventoryStateAsync(vmName, request, "guest-ready", null, cancellationToken);
            return response;
        }
        if (request.ProfileVersion is null or <= 0 || string.IsNullOrWhiteSpace(request.ArtifactDigest) ||
            request.ArtifactSize is null or <= 0 || string.IsNullOrWhiteSpace(request.ManifestJson))
            return Failed("profile-validate", "Bootstrap profile identity is incomplete.");

        var manifest = ParseManifest(request.ManifestJson, request.OsType);
        var values = ResolveValues(manifest, request.Parameters, request.Secrets);
        var artifactDigest = NormalizeDigest(request.ArtifactDigest);
        var artifactPath = ResolveArtifactPath(
            request.ProfileId.Value, request.ProfileVersion.Value, request.ArtifactDigest, request.ArtifactSize.Value);
        var hostStage = await ExtractArtifactAsync(
            vmName, request.Generation, artifactPath, request.ArtifactDigest, cancellationToken);
        var guestStage = GuestStagePath(request.OsType, request.ArtifactDigest);
        var files = Directory.GetFiles(hostStage, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();
        var templatedSources = manifest.Files.Where(item => item.Template)
            .Select(item => NormalizeArtifactPath(item.SourcePath)).ToHashSet(StringComparer.Ordinal);

        var stagePlan = new List<GuestFileEntry>(files.Length);
        foreach (var file in files)
        {
            var relative = NormalizeArtifactPath(Path.GetRelativePath(hostStage, file));
            var content = await File.ReadAllBytesAsync(file, cancellationToken);
            if (templatedSources.Contains(relative))
                content = RenderTemplate(content, values);
            stagePlan.Add(new GuestFileEntry(relative, content, "0600"));
        }

        await PopulateGuestStageAsync(vmName, request.OsType, guestStage, stagePlan, cancellationToken);

        foreach (var file in manifest.Files)
        {
            var source = Path.Combine(hostStage, NormalizeArtifactPath(file.SourcePath)
                .Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
                throw new InvalidOperationException($"Bootstrap artifact file '{file.SourcePath}' is missing.");
            var content = await File.ReadAllBytesAsync(source, cancellationToken);
            if (file.Template)
                content = RenderTemplate(content, values);
            await WriteGuestBytesAsync(
                vmName, request.OsType, file.TargetPath, content, file.Mode, cancellationToken);
        }

        await WriteRuntimeConfigurationAsync(vmName, request, values, cancellationToken);

        var completedSteps = new List<string>();
        var rebootCount = 0;
        foreach (var step in manifest.Steps)
        {
            var checkpointPath = StepCheckpointPath(request.OsType, artifactDigest, step.Id);
            var checkpoint = await ReadStepCheckpointAsync(
                vmName, checkpointPath, request, artifactDigest, step.Id, cancellationToken);
            if (checkpoint is not null)
            {
                if (checkpoint.RebootRequired)
                {
                    rebootCount++;
                    if (rebootCount > manifest.MaxReboots)
                        throw new InvalidOperationException("Bootstrap profile exceeded its declared reboot limit.");
                    if (!checkpoint.RebootCompleted)
                    {
                        await EmitRebootAsync(
                            vmName, request, AgentRuntimeSignalStage.Rebooting,
                            AgentRuntimeSignalOutcome.Started, cancellationToken);
                        await guest.RebootAndWaitAsync(vmName, TimeSpan.FromMinutes(5), cancellationToken);
                        await EmitRebootAsync(
                            vmName, request, AgentRuntimeSignalStage.GuestReadyAfterReboot,
                            AgentRuntimeSignalOutcome.Ready, cancellationToken);
                        await VerifyMarkerAsync(vmName, request, markerPath, cancellationToken);
                        checkpoint = checkpoint with
                        {
                            RebootCompleted = true,
                            CompletedAt = DateTimeOffset.UtcNow
                        };
                        await WriteStepCheckpointAsync(
                            vmName, request.OsType, checkpointPath, checkpoint, cancellationToken);
                    }
                }

                completedSteps.Add(step.Id);
                continue;
            }

            var entrypoint = CombineGuestPath(request.OsType, guestStage, step.Entrypoint);
            if (request.OsType == VmInitOsType.Linux)
                await RunRequiredAsync(vmName, new VmGuestCommandRequest(
                    $"prepare-{step.Id}", "/usr/bin/chmod", ["0700", entrypoint], 30), cancellationToken);
            var result = await guest.ExecuteAsync(vmName,
                BuildStepCommand(request.OsType, step, entrypoint, request.RuntimeId, request.Generation),
                cancellationToken);
            var rebootRequested = result.ExitCode is 194 or 3010;
            if (!result.Success && !rebootRequested)
                throw new VmBootstrapCommandException(
                    "step-execute",
                    "bootstrap_step_failed",
                    step.Id,
                    result.Category,
                    result.ExitCode,
                    $"Bootstrap step '{step.Id}' failed.");
            completedSteps.Add(step.Id);

            var mustReboot = step.Reboot.Equals("Required", StringComparison.OrdinalIgnoreCase) ||
                             step.Reboot.Equals("IfRequested", StringComparison.OrdinalIgnoreCase) && rebootRequested;
            checkpoint = new AgentBootstrapStepCheckpoint(
                request.RuntimeId,
                request.Generation,
                request.AssetKey,
                request.ProfileId.Value,
                request.ProfileVersion.Value,
                artifactDigest,
                step.Id,
                mustReboot,
                !mustReboot,
                DateTimeOffset.UtcNow);
            await WriteStepCheckpointAsync(
                vmName, request.OsType, checkpointPath, checkpoint, cancellationToken);
            if (mustReboot)
            {
                rebootCount++;
                if (rebootCount > manifest.MaxReboots)
                    throw new InvalidOperationException("Bootstrap profile exceeded its declared reboot limit.");
                await EmitRebootAsync(
                    vmName, request, AgentRuntimeSignalStage.Rebooting,
                    AgentRuntimeSignalOutcome.Started, cancellationToken);
                await guest.RebootAndWaitAsync(vmName, TimeSpan.FromMinutes(5), cancellationToken);
                await EmitRebootAsync(
                    vmName, request, AgentRuntimeSignalStage.GuestReadyAfterReboot,
                    AgentRuntimeSignalOutcome.Ready, cancellationToken);
                await VerifyMarkerAsync(vmName, request, markerPath, cancellationToken);
                checkpoint = checkpoint with
                {
                    RebootCompleted = true,
                    CompletedAt = DateTimeOffset.UtcNow
                };
                await WriteStepCheckpointAsync(
                    vmName, request.OsType, checkpointPath, checkpoint, cancellationToken);
            }
        }

        var passedHealthChecks = request.RunHealthChecks
            ? await RunHealthChecksAsync(vmName, request, manifest, guestStage, values, cancellationToken)
            : [];

        logger.LogInformation(
            "VM bootstrap completed: VM={VmName}, Runtime={RuntimeId}, Generation={Generation}, Profile={ProfileId}, Reboots={RebootCount}",
            vmName, request.RuntimeId, request.Generation, request.ProfileId, rebootCount);
        var completed = new VmBootstrapApplyResponse(
            true, "health", "VM bootstrap profile completed successfully.", rebootCount,
            completedSteps, passedHealthChecks);
        await WriteInventoryStateAsync(vmName, request, "ready", artifactDigest, cancellationToken);
        return completed;
    }

    public async Task<VmBootstrapApplyResponse> CheckHealthAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        CancellationToken cancellationToken)
    {
        var ready = await guest.WaitReadyAsync(vmName, TimeSpan.FromMinutes(3), cancellationToken);
        if (!ready.Ready)
            return Failed("guest-ready", ready.Message);
        if (request.ProfileId is null || string.IsNullOrWhiteSpace(request.ManifestJson) ||
            string.IsNullOrWhiteSpace(request.ArtifactDigest))
        {
            var response = new VmBootstrapApplyResponse(
                true, "health", "No bootstrap health contract was requested.", 0, [], []);
            await WriteInventoryStateAsync(vmName, request, "healthy", null, cancellationToken);
            return response;
        }
        var manifest = ParseManifest(request.ManifestJson, request.OsType);
        var values = ResolveValues(manifest, request.Parameters, request.Secrets);
        var guestStage = GuestStagePath(request.OsType, request.ArtifactDigest);
        var passed = await RunHealthChecksAsync(
            vmName, request, manifest, guestStage, values, cancellationToken);
        var completed = new VmBootstrapApplyResponse(
            true, "health", "VM bootstrap health checks completed successfully.", 0, [], passed);
        await WriteInventoryStateAsync(
            vmName, request, "healthy", NormalizeDigest(request.ArtifactDigest), cancellationToken);
        return completed;
    }

    private async Task EmitRebootAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        AgentRuntimeSignalStage stage,
        AgentRuntimeSignalOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (signals is null || request.OperationId is not { } operationId || operationId == Guid.Empty)
            return;
        await signals.AppendAsync(new AgentRuntimeSignalDraft(
            operationId,
            request.RuntimeId,
            request.Generation,
            "vm",
            vmName,
            stage,
            outcome), cancellationToken);
    }

    public async Task<IReadOnlyList<RuntimeInventoryResource>> SnapshotInventoryAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(InventoryRoot)) return [];
        List<RuntimeInventoryResource> resources = [];
        foreach (var path in Directory.EnumerateFiles(
                     InventoryRoot, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var state = await JsonSerializer.DeserializeAsync<BootstrapInventoryState>(
                    stream, JsonOptions, cancellationToken);
                if (state is null) continue;
                resources.Add(new RuntimeInventoryResource(
                    state.VmName,
                    state.AssetKey,
                    state.Generation,
                    state.State,
                    null,
                    "bootstrap-execution",
                    state.RuntimeId,
                    state.ArtifactDigest));
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Failed to read VM bootstrap inventory state.");
            }
        }
        return resources;
    }

    public Task CleanupGenerationAsync(int runtimeId, int generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(InventoryRoot, $"runtime-{runtimeId}", $"generation-{generation}");
        if (Directory.Exists(path)) Directory.Delete(path, true);
        var runtimePath = Path.GetDirectoryName(path)!;
        if (Directory.Exists(runtimePath) && !Directory.EnumerateFileSystemEntries(runtimePath).Any())
            Directory.Delete(runtimePath);
        return Task.CompletedTask;
    }

    private static async Task WriteInventoryStateAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        string state,
        string? artifactDigest,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(
            InventoryRoot, $"runtime-{request.RuntimeId}", $"generation-{request.Generation}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{request.AssetKey}.json");
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        var value = new BootstrapInventoryState(
            request.RuntimeId,
            request.Generation,
            request.AssetKey,
            vmName,
            state,
            artifactDigest,
            DateTimeOffset.UtcNow);
        try
        {
            await using var stream = File.Create(temporary);
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<VmCapabilityProbeResponse> ProbeAsync(
        string vmName,
        VmCapabilityProbeRequest request,
        CancellationToken cancellationToken)
    {
        var verified = new List<string>();
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal);
        var ready = await guest.WaitReadyAsync(
            vmName, TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 30, 600)), cancellationToken);
        if (!ready.Ready)
            return new VmCapabilityProbeResponse(false, [], evidence, "guest_qga_unavailable", ready.Message);

        foreach (var capability in request.Capabilities
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(ProbeOrder)
                     .ThenBy(item => item, StringComparer.Ordinal))
        {
            switch (capability)
            {
                case "guest.qga.v1":
                case "guest.virtio-serial.v1":
                    verified.Add(capability);
                    evidence[capability] = ready.Version ?? "ready";
                    break;
                case "windows.powershell.v1" when request.OsType == VmInitOsType.Windows:
                {
                    var result = await guest.ExecuteAsync(vmName, new VmGuestCommandRequest(
                        "probe-powershell", WindowsPowerShellPath,
                        ["-NoProfile", "-NonInteractive", "-Command", "$PSVersionTable.PSVersion.ToString()"], 180),
                        cancellationToken);
                    if (!result.Success)
                        return ProbeFailed(verified, evidence, "windows_powershell_unavailable", result.Category);
                    verified.Add(capability);
                    evidence[capability] = (result.StandardOutput ?? "available").Trim();
                    break;
                }
                case "linux.cloud-init.nocloud.v1" when request.OsType == VmInitOsType.Linux:
                case "windows.cloudbase-init.v1" when request.OsType == VmInitOsType.Windows:
                    if (string.IsNullOrWhiteSpace(request.ExpectedMarkerPath) ||
                        string.IsNullOrWhiteSpace(request.ExpectedMarkerValue))
                        return ProbeFailed(verified, evidence, "guest_init_marker_missing",
                            "Guest-init certification requires an expected marker.");
                    try
                    {
                        var marker = await WaitForExpectedMarkerAsync(
                            vmName,
                            request.ExpectedMarkerPath,
                            TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds, 30, 600)),
                            cancellationToken);
                        if (!marker.Equals(request.ExpectedMarkerValue, StringComparison.Ordinal))
                            return ProbeFailed(verified, evidence, "guest_init_marker_mismatch",
                                "Guest-init marker did not match the requested value.");
                        verified.Add(capability);
                        evidence[capability] = "marker-verified";
                    }
                    catch (Exception exception)
                    {
                        return ProbeFailed(verified, evidence, "guest_init_marker_unreadable", exception.Message);
                    }
                    break;
                case "network.virtio.v1":
                case "network.e1000e.v1":
                    verified.Add(capability);
                    evidence[capability] = "domain-booted";
                    break;
                case "bootstrap.firstboot.v1":
                {
                    var probePath = request.OsType == VmInitOsType.Windows
                        ? @"C:\ProgramData\GZCTF\Certification\firstboot-probe.ps1"
                        : "/var/lib/gzctf-certification/firstboot-probe.sh";
                    var content = request.OsType == VmInitOsType.Windows
                        ? "Write-Output 'gzctf-firstboot-ok'\r\n"
                        : "#!/bin/sh\nprintf 'gzctf-firstboot-ok\\n'\n";
                    await WriteGuestTextAsync(
                        vmName, request.OsType, probePath, content, "0700", cancellationToken);
                    var result = await guest.ExecuteAsync(vmName,
                        BuildEntrypointCommand(request.OsType, "probe-firstboot", probePath, 120),
                        cancellationToken);
                    if (!result.Success ||
                        !(result.StandardOutput ?? string.Empty).Contains(
                            "gzctf-firstboot-ok", StringComparison.Ordinal))
                        return ProbeFailed(verified, evidence, "bootstrap_firstboot_unavailable", result.Category);
                    verified.Add(capability);
                    evidence[capability] = "write-execute-verified";
                    break;
                }
                default:
                    return ProbeFailed(verified, evidence, "capability_probe_unsupported",
                        $"Capability '{capability}' has no controlled probe implementation.");
            }
        }

        return new VmCapabilityProbeResponse(true, verified, evidence, null, null);
    }

    internal static int ProbeOrder(string capability) => capability switch
    {
        "guest.qga.v1" or "guest.virtio-serial.v1" => 0,
        "windows.powershell.v1" => 1,
        "network.virtio.v1" or "network.e1000e.v1" => 2,
        "linux.cloud-init.nocloud.v1" or "windows.cloudbase-init.v1" => 3,
        "bootstrap.firstboot.v1" => 4,
        _ => 10
    };

    async Task<string> WaitForExpectedMarkerAsync(
        string vmName,
        string markerPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        InvalidOperationException? lastMissingFile = null;
        while (!deadline.IsCancellationRequested)
        {
            try
            {
                return Encoding.UTF8.GetString(await guest.ReadFileAsync(
                    vmName, markerPath, 4096, deadline.Token)).Trim();
            }
            catch (InvalidOperationException exception) when (IsMissingGuestFileError(exception.Message))
            {
                lastMissingFile = exception;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Guest-init marker did not appear before the certification deadline.",
            lastMissingFile);
    }

    async Task ConfigureWindowsNetworkAsync(
        string vmName,
        IReadOnlyList<VmNetworkInterfaceRequest> interfaces,
        CancellationToken cancellationToken)
    {
        if (interfaces.Count == 0) return;
        await RunPowerShellAsync(
            vmName, "configure-network", BuildWindowsNetworkScript(interfaces), 180, cancellationToken);
    }

    internal static string BuildWindowsNetworkScript(IReadOnlyList<VmNetworkInterfaceRequest> interfaces)
    {
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(interfaces, JsonOptions));
        return $$"""
            $ErrorActionPreference = 'Stop'
            $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{payload}}'))
            $items = $json | ConvertFrom-Json
            foreach ($item in $items) {
              $mac = $item.macAddress.Replace(':','-').ToUpperInvariant()
              $adapter = Get-NetAdapter | Where-Object { $_.MacAddress -eq $mac } | Select-Object -First 1
              if ($null -eq $adapter) { throw "Adapter not found for MAC $mac" }
              Set-NetIPInterface -InterfaceIndex $adapter.ifIndex -Dhcp Disabled -AddressFamily IPv4 -ErrorAction SilentlyContinue
              Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue
              $args = @{ InterfaceIndex=$adapter.ifIndex; IPAddress=$item.ipAddress; PrefixLength=[int]$item.prefixLength }
              if ($item.isPrimary -and $item.gateway) { $args.DefaultGateway = $item.gateway }
              New-NetIPAddress @args | Out-Null
              if ($item.dnsServers.Count -gt 0) {
                Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses $item.dnsServers
              }
              foreach ($route in $item.routes) {
                $parts = $route -split ' via '
                if ($parts.Count -ne 2) { throw "Invalid route '$route'" }
                $existingRoutes = Get-NetRoute -InterfaceIndex $adapter.ifIndex -PolicyStore PersistentStore -ErrorAction Stop |
                  Where-Object { $_.DestinationPrefix -eq $parts[0] -and $_.NextHop -eq $parts[1] }
                $existingRoutes | Remove-NetRoute -Confirm:$false -ErrorAction Stop
                $routeArgs = @{
                  InterfaceIndex = $adapter.ifIndex
                  DestinationPrefix = $parts[0]
                  NextHop = $parts[1]
                  PolicyStore = 'PersistentStore'
                }
                New-NetRoute @routeArgs -ErrorAction Stop | Out-Null
              }
            }
            """;
    }

    async Task WriteRuntimeConfigurationAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var publicValues = values.Where(item => !request.Secrets.ContainsKey(item.Key))
            .ToDictionary(StringComparer.Ordinal);
        var runtime = JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.RuntimeId,
            request.Generation,
            request.AssetKey,
            Parameters = publicValues
        }, JsonOptions);
        var secrets = JsonSerializer.SerializeToUtf8Bytes(request.Secrets, JsonOptions);
        var root = RuntimeRoot(request.OsType);
        await WriteGuestBytesAsync(vmName, request.OsType, CombineGuestPath(request.OsType, root, "runtime.json"),
            runtime, "0600", cancellationToken);
        await WriteGuestBytesAsync(vmName, request.OsType, CombineGuestPath(request.OsType, root, "secrets.json"),
            secrets, "0600", cancellationToken);
        if (request.OsType == VmInitOsType.Linux)
        {
            await WriteGuestBytesAsync(vmName, request.OsType,
                CombineGuestPath(request.OsType, root, "env"),
                BuildLinuxEnvironmentFile(values), "0600", cancellationToken);
        }
    }

    async Task RunHealthCheckAsync(
        string vmName,
        VmInitOsType osType,
        AgentBootstrapHealthCheck check,
        string guestStage,
        IReadOnlyDictionary<string, string> values,
        string? primaryIp,
        CancellationToken cancellationToken)
    {
        var target = RenderTemplate(check.Target, values);
        Exception? lastError = null;
        for (var attempt = 0; attempt < check.Attempts; attempt++)
        {
            try
            {
                switch (check.Kind.ToLowerInvariant())
                {
                    case "tcp":
                    case "http":
                    {
                        var result = await guest.ExecuteAsync(vmName,
                            BuildNetworkHealthCommand(
                                osType, check.Id, check.Kind, target, primaryIp, check.TimeoutSeconds),
                            cancellationToken);
                        if (!result.Success)
                            throw new InvalidOperationException(
                                $"Guest network health probe failed ({result.Category}, exit={result.ExitCode?.ToString() ?? "none"}).");
                        return;
                    }
                    case "entrypoint":
                    {
                        var entrypoint = CombineGuestPath(osType, guestStage, check.Target);
                        var result = await guest.ExecuteAsync(vmName,
                            BuildEntrypointCommand(osType, $"health-{check.Id}", entrypoint, check.TimeoutSeconds),
                            cancellationToken);
                        if (!result.Success) throw new InvalidOperationException(result.Category);
                        return;
                    }
                    default:
                        throw new InvalidOperationException($"Unsupported health check kind '{check.Kind}'.");
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = exception is OperationCanceledException
                    ? new TimeoutException(
                        $"Health check '{check.Id}' timed out after {check.TimeoutSeconds} second(s).",
                        exception)
                    : exception;
                if (attempt + 1 < check.Attempts)
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        throw new InvalidOperationException($"Health check '{check.Id}' failed: {lastError?.Message}");
    }

    async Task<IReadOnlyList<string>> RunHealthChecksAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        AgentBootstrapManifest manifest,
        string guestStage,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var passed = new List<string>(manifest.HealthChecks.Count);
        foreach (var check in manifest.HealthChecks)
        {
            await RunHealthCheckAsync(
                vmName, request.OsType, check, guestStage, values,
                request.Interfaces.FirstOrDefault(item => item.IsPrimary)?.IpAddress,
                cancellationToken);
            passed.Add(check.Id);
        }
        return passed;
    }

    async Task VerifyMarkerAsync(
        string vmName,
        VmBootstrapApplyRequest request,
        string markerPath,
        CancellationToken cancellationToken)
    {
        var expected = $"{request.RuntimeId}:{request.Generation}:{request.AssetKey}";
        var marker = Encoding.UTF8.GetString(await guest.ReadFileAsync(
            vmName, markerPath, 4096, cancellationToken)).Trim();
        if (!marker.Equals(expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Guest runtime generation marker changed across reboot.");
    }

    async Task<string> ExtractArtifactAsync(
        string vmName,
        int generation,
        string artifactPath,
        string digest,
        CancellationToken cancellationToken)
    {
        var stage = Path.Combine("/var/lib/gzctf/vm-runtime", vmName, $"generation-{generation}", digest);
        if (Directory.Exists(stage)) Directory.Delete(stage, true);
        Directory.CreateDirectory(stage);
        var root = Path.GetFullPath(stage) + Path.DirectorySeparatorChar;
        await using var file = File.OpenRead(artifactPath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip, leaveOpen: false);
        var entries = 0;
        long expanded = 0;
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            entries++;
            if (entries > MaxArchiveEntries)
                throw new InvalidOperationException("Bootstrap artifact contains too many entries.");
            if (entry.EntryType is TarEntryType.Directory) continue;
            if (entry.EntryType is not TarEntryType.RegularFile and not TarEntryType.V7RegularFile)
                throw new InvalidOperationException("Bootstrap artifact contains unsupported link or device entries.");
            var relative = NormalizeArtifactPath(entry.Name);
            var destination = Path.GetFullPath(Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException("Bootstrap artifact path escaped its staging directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous);
            if (entry.DataStream is not null)
                await entry.DataStream.CopyToAsync(output, cancellationToken);
            expanded += output.Length;
            if (expanded > MaxExpandedBytes)
                throw new InvalidOperationException("Bootstrap artifact expanded size exceeds the safety limit.");
        }
        return stage;
    }

    static string ResolveArtifactPath(Guid profileId, int version, string digestValue, long expectedSize)
    {
        var digest = NormalizeDigest(digestValue);
        var path = Path.Combine("/var/lib/gzctf/bootstrap-profiles", profileId.ToString("N"),
            version.ToString(), $"{digest}.tar.gz");
        if (!File.Exists(path))
            throw new FileNotFoundException("Bootstrap artifact was not distributed to this node.", path);
        if (new FileInfo(path).Length != expectedSize)
            throw new InvalidOperationException("Bootstrap artifact size does not match the certified version.");
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!actual.Equals(digest, StringComparison.Ordinal))
            throw new InvalidOperationException("Bootstrap artifact digest verification failed.");
        return path;
    }

    async Task WriteGuestTextAsync(
        string vmName,
        VmInitOsType osType,
        string path,
        string value,
        string mode,
        CancellationToken cancellationToken) =>
        await WriteGuestBytesAsync(vmName, osType, path, Encoding.UTF8.GetBytes(value), mode, cancellationToken);

    /// <summary>
    /// Materializes the bootstrap stage inside the guest. One tar transfer replaces the per-file
    /// sequence (mkdir, open, write, flush, close, chmod), whose seven QGA round trips per file —
    /// each a separate virsh process spawn — dominate bootstrap time for profiles built from many
    /// small scripts. Falls back once to per-file writes, logging why, when the guest cannot
    /// extract the archive.
    /// </summary>
    async Task PopulateGuestStageAsync(
        string vmName,
        VmInitOsType osType,
        string guestStage,
        IReadOnlyList<GuestFileEntry> plan,
        CancellationToken cancellationToken)
    {
        var entries = GuestFileBatch.Deduplicate(plan);
        if (entries.Count == 0)
            return;

        if (osType == VmInitOsType.Linux && entries.Count > 1)
        {
            try
            {
                await PopulateGuestStageBatchedAsync(vmName, guestStage, entries, cancellationToken);
                return;
            }
            catch (InvalidOperationException exception)
            {
                logger.LogWarning(exception,
                    "Batched guest stage transfer unavailable, using per-file writes: VM={VmName}, Files={FileCount}",
                    vmName, entries.Count);
            }
        }

        foreach (var entry in entries)
            await WriteGuestBytesAsync(vmName, osType,
                CombineGuestPath(osType, guestStage, entry.GuestPath), entry.Content, entry.Mode,
                cancellationToken);
    }

    async Task PopulateGuestStageBatchedAsync(
        string vmName,
        string guestStage,
        IReadOnlyList<GuestFileEntry> entries,
        CancellationToken cancellationToken)
    {
        var archive = GuestFileBatch.BuildTarArchive(entries);
        var archivePath = CombineGuestPath(VmInitOsType.Linux, guestStage, ".bootstrap-stage.tar");
        await EnsureGuestDirectoryAsync(vmName, VmInitOsType.Linux, guestStage, cancellationToken);
        try
        {
            await using (var stream = new MemoryStream(archive, writable: false))
                await guest.WriteFileAsync(vmName, archivePath, stream, cancellationToken);
            await RunRequiredAsync(vmName, new VmGuestCommandRequest(
                "stage-extract", "/usr/bin/tar",
                ["-xpf", archivePath, "-C", guestStage, "--no-same-owner"],
                StageExtractTimeoutSeconds), cancellationToken);
        }
        finally
        {
            await guest.ExecuteAsync(vmName, new VmGuestCommandRequest(
                "stage-cleanup", "/usr/bin/rm", ["-f", archivePath], 30), CancellationToken.None);
        }

        logger.LogInformation(
            "Guest stage populated in one transfer: VM={VmName}, Files={FileCount}, Bytes={ArchiveBytes}",
            vmName, entries.Count, archive.Length);
    }

    async Task WriteGuestBytesAsync(
        string vmName,
        VmInitOsType osType,
        string path,
        byte[] content,
        string mode,
        CancellationToken cancellationToken)
    {
        await EnsureGuestDirectoryAsync(vmName, osType, GuestDirectoryName(osType, path), cancellationToken);
        await using var stream = new MemoryStream(content, writable: false);
        await guest.WriteFileAsync(vmName, path, stream, cancellationToken);
        if (osType == VmInitOsType.Linux)
            await RunRequiredAsync(vmName,
                new VmGuestCommandRequest("chmod", "/usr/bin/chmod", [mode, path], 30), cancellationToken);
        else if (mode == "0600")
            await RunRequiredAsync(vmName,
                new VmGuestCommandRequest("protect-file", WindowsIcaclsPath,
                    [path, "/inheritance:r", "/grant:r", "*S-1-5-18:(F)", "*S-1-5-32-544:(F)"], 30),
                cancellationToken);
    }

    async Task EnsureGuestDirectoryAsync(
        string vmName,
        VmInitOsType osType,
        string path,
        CancellationToken cancellationToken)
    {
        if (osType == VmInitOsType.Linux)
        {
            await RunRequiredAsync(vmName,
                new VmGuestCommandRequest("mkdir", "/usr/bin/install", ["-d", "-m", "0755", path], 30),
                cancellationToken);
            return;
        }
        var escaped = path.Replace("'", "''", StringComparison.Ordinal);
        await RunPowerShellAsync(vmName, "mkdir", $"New-Item -ItemType Directory -Force -Path '{escaped}' | Out-Null",
            30, cancellationToken);
    }

    async Task RunPowerShellAsync(
        string vmName,
        string stepId,
        string script,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        await RunRequiredAsync(vmName, new VmGuestCommandRequest(
            stepId, WindowsPowerShellPath,
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded],
            timeoutSeconds), cancellationToken);
    }

    async Task RunRequiredAsync(
        string vmName,
        VmGuestCommandRequest command,
        CancellationToken cancellationToken)
    {
        var result = await guest.ExecuteAsync(vmName, command, cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException(
                $"Guest operation '{command.StepId}' failed ({result.Category}, exit={result.ExitCode?.ToString() ?? "none"}).");
    }

    static VmGuestCommandRequest BuildStepCommand(
        VmInitOsType osType,
        AgentBootstrapStep step,
        string entrypoint,
        int runtimeId,
        int generation)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GZCTF_RUNTIME_ID"] = runtimeId.ToString(),
            ["GZCTF_RUNTIME_GENERATION"] = generation.ToString(),
            ["GZCTF_RUNTIME_CONFIG"] = CombineGuestPath(osType, RuntimeRoot(osType), "runtime.json"),
            ["GZCTF_RUNTIME_SECRETS"] = CombineGuestPath(osType, RuntimeRoot(osType), "secrets.json")
        };
        return osType == VmInitOsType.Windows
            ? new VmGuestCommandRequest(step.Id, WindowsPowerShellPath,
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", entrypoint],
                step.TimeoutSeconds, environment)
            : new VmGuestCommandRequest(step.Id, entrypoint, [], step.TimeoutSeconds, environment);
    }

    static VmGuestCommandRequest BuildEntrypointCommand(
        VmInitOsType osType,
        string stepId,
        string entrypoint,
        int timeoutSeconds) => osType == VmInitOsType.Windows
        ? new VmGuestCommandRequest(stepId, WindowsPowerShellPath,
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", entrypoint], timeoutSeconds)
        : new VmGuestCommandRequest(stepId, entrypoint, [], timeoutSeconds);

    internal static VmGuestCommandRequest BuildNetworkHealthCommand(
        VmInitOsType osType,
        string checkId,
        string kind,
        string target,
        string? primaryIp,
        int timeoutSeconds)
    {
        var stepId = $"health-{NormalizeStepId(checkId)}";
        var timeout = Math.Clamp(timeoutSeconds, 1, 300);
        if (kind.Equals("Tcp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(primaryIp) || !int.TryParse(target, out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("TCP health check requires a primary IP and numeric port.");
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GZCTF_HEALTH_HOST"] = primaryIp,
                ["GZCTF_HEALTH_PORT"] = port.ToString()
            };
            return osType == VmInitOsType.Windows
                ? new VmGuestCommandRequest(stepId, WindowsPowerShellPath,
                    ["-NoProfile", "-NonInteractive", "-Command",
                        "$c=[Net.Sockets.TcpClient]::new();try{$c.Connect($env:GZCTF_HEALTH_HOST,[int]$env:GZCTF_HEALTH_PORT)}finally{$c.Dispose()}"],
                    timeout, environment)
                : new VmGuestCommandRequest(stepId, "/bin/bash",
                    ["-c", "exec 3<>/dev/tcp/${GZCTF_HEALTH_HOST}/${GZCTF_HEALTH_PORT}"],
                    timeout, environment);
        }

        if (!kind.Equals("Http", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported network health check kind '{kind}'.");
        var renderedTarget = target.Replace("${PRIMARY_IP}", primaryIp, StringComparison.Ordinal);
        if (!Uri.TryCreate(renderedTarget, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("HTTP health check requires an absolute HTTP or HTTPS URI.");
        var httpEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GZCTF_HEALTH_URI"] = uri.AbsoluteUri,
            ["GZCTF_HEALTH_TIMEOUT"] = timeout.ToString()
        };
        return osType == VmInitOsType.Windows
            ? new VmGuestCommandRequest(stepId, WindowsPowerShellPath,
                ["-NoProfile", "-NonInteractive", "-Command",
                    "$r=Invoke-WebRequest -UseBasicParsing -Uri $env:GZCTF_HEALTH_URI -TimeoutSec ([int]$env:GZCTF_HEALTH_TIMEOUT);if([int]$r.StatusCode -lt 200 -or [int]$r.StatusCode -ge 400){exit 1}"],
                timeout, httpEnvironment)
            : new VmGuestCommandRequest(stepId, "/usr/bin/python3",
                ["-c", "import os,urllib.request; r=urllib.request.urlopen(os.environ['GZCTF_HEALTH_URI'], timeout=float(os.environ['GZCTF_HEALTH_TIMEOUT'])); raise SystemExit(0 if 200 <= r.status < 400 else 1)"],
                timeout, httpEnvironment);
    }

    internal static byte[] BuildLinuxEnvironmentFile(IReadOnlyDictionary<string, string> values)
    {
        var environment = new StringBuilder();
        foreach (var item in values.OrderBy(item => item.Key, StringComparer.Ordinal))
            environment.Append(item.Key).Append("='")
                .Append(item.Value.Replace("'", "'\\''", StringComparison.Ordinal)).Append("'\n");
        return Encoding.UTF8.GetBytes(environment.ToString());
    }

    static AgentBootstrapManifest ParseManifest(string json, VmInitOsType osType)
    {
        var manifest = JsonSerializer.Deserialize<AgentBootstrapManifest>(json, JsonOptions)
                       ?? throw new InvalidOperationException("Bootstrap manifest is empty.");
        if (manifest.SchemaVersion != 1 || manifest.MaxReboots is < 0 or > 3)
            throw new InvalidOperationException("Bootstrap manifest version or reboot limit is invalid.");
        if (!manifest.OperatingSystems.Contains(osType.ToString(), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Bootstrap profile does not support the guest operating system.");
        if (manifest.Steps.Any(item => !item.RunAs.Equals("system", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Bootstrap steps must use the platform identity 'system'.");
        if (manifest.Steps.Select(item => NormalizeStepId(item.Id)).Distinct(StringComparer.Ordinal).Count() !=
            manifest.Steps.Count)
            throw new InvalidOperationException("Bootstrap step IDs must be unique.");
        foreach (var path in manifest.Files.Select(item => item.SourcePath)
                     .Concat(manifest.Steps.Select(item => item.Entrypoint))
                     .Concat(manifest.HealthChecks.Where(item => item.Kind.Equals("Entrypoint", StringComparison.OrdinalIgnoreCase))
                         .Select(item => item.Target)))
            NormalizeArtifactPath(path);
        return manifest;
    }

    async Task<AgentBootstrapStepCheckpoint?> ReadStepCheckpointAsync(
        string vmName,
        string path,
        VmBootstrapApplyRequest request,
        string artifactDigest,
        string stepId,
        CancellationToken cancellationToken)
    {
        byte[] payload;
        try
        {
            payload = await guest.ReadFileAsync(vmName, path, 4096, cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsMissingGuestFileError(exception.Message))
        {
            return null;
        }

        var checkpoint = JsonSerializer.Deserialize<AgentBootstrapStepCheckpoint>(payload, JsonOptions)
                         ?? throw new InvalidOperationException("Bootstrap step checkpoint is empty.");
        if (checkpoint.RuntimeId != request.RuntimeId || checkpoint.Generation != request.Generation ||
            !string.Equals(checkpoint.AssetKey, request.AssetKey, StringComparison.Ordinal) ||
            checkpoint.ProfileId != request.ProfileId || checkpoint.ProfileVersion != request.ProfileVersion ||
            !string.Equals(checkpoint.ArtifactDigest, artifactDigest, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.StepId, stepId, StringComparison.Ordinal))
            throw new InvalidOperationException("Bootstrap step checkpoint identity does not match the runtime request.");
        return checkpoint;
    }

    async Task WriteStepCheckpointAsync(
        string vmName,
        VmInitOsType osType,
        string path,
        AgentBootstrapStepCheckpoint checkpoint,
        CancellationToken cancellationToken) =>
        await WriteGuestBytesAsync(
            vmName,
            osType,
            path,
            JsonSerializer.SerializeToUtf8Bytes(checkpoint, JsonOptions),
            "0600",
            cancellationToken);

    static IReadOnlyDictionary<string, string> ResolveValues(
        AgentBootstrapManifest manifest,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> secrets)
    {
        var declared = manifest.Parameters.ToDictionary(item => item.Key, StringComparer.Ordinal);
        if (parameters.Keys.Concat(secrets.Keys).Any(key => !declared.ContainsKey(key)))
            throw new InvalidOperationException("Bootstrap values contain an undeclared parameter.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in manifest.Parameters)
        {
            var source = item.Secret ? secrets : parameters;
            var value = source.GetValueOrDefault(item.Key) ?? item.DefaultValue;
            if (item.Required && string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Bootstrap parameter '{item.Key}' is required.");
            if (value is null) continue;
            if (item.Type.Equals("Integer", StringComparison.OrdinalIgnoreCase) && !int.TryParse(value, out _) ||
                item.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) && !bool.TryParse(value, out _))
                throw new InvalidOperationException($"Bootstrap parameter '{item.Key}' has an invalid type.");
            values[item.Key] = value;
        }
        return values;
    }

    internal static byte[] RenderTemplate(byte[] input, IReadOnlyDictionary<string, string> values)
    {
        var text = new UTF8Encoding(false, true).GetString(input);
        return Encoding.UTF8.GetBytes(RenderTemplate(text, values));
    }

    internal static string RenderTemplate(string input, IReadOnlyDictionary<string, string> values) =>
        Placeholder().Replace(input, match => values.TryGetValue(match.Groups[1].Value, out var value)
            ? value
            : throw new InvalidOperationException($"Bootstrap template parameter '{match.Groups[1].Value}' is missing."));

    internal static string NormalizeArtifactPath(string value)
    {
        var normalized = value.Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 512 ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidOperationException("Bootstrap artifact path is invalid.");
        return normalized;
    }

    internal static string NormalizeStepId(string value)
    {
        var normalized = value.Trim();
        if (!BootstrapStepId().IsMatch(normalized))
            throw new InvalidOperationException("Bootstrap step ID is invalid.");
        return normalized;
    }

    internal static bool IsMissingGuestFileError(string value) =>
        value.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("cannot find the path", StringComparison.OrdinalIgnoreCase);

    static string NormalizeDigest(string value)
    {
        var digest = value.Trim().ToLowerInvariant();
        if (digest.StartsWith("sha256:", StringComparison.Ordinal)) digest = digest[7..];
        if (digest.Length != 64 || digest.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("Bootstrap artifact digest is invalid.");
        return digest;
    }

    static string RuntimeRoot(VmInitOsType osType) =>
        osType == VmInitOsType.Windows ? @"C:\ProgramData\GZCTF\Runtime" : "/opt/gzctf/runtime";

    static string MarkerPath(VmInitOsType osType) =>
        CombineGuestPath(osType, RuntimeRoot(osType), "generation");

    static string GuestStagePath(VmInitOsType osType, string digest) =>
        CombineGuestPath(osType, RuntimeRoot(osType), $"bootstrap/{NormalizeDigest(digest)}");

    static string StepCheckpointPath(VmInitOsType osType, string digest, string stepId) =>
        CombineGuestPath(
            osType,
            GuestStagePath(osType, digest),
            $"checkpoints/{NormalizeStepId(stepId)}.json");

    static string CombineGuestPath(VmInitOsType osType, string root, string relative)
    {
        relative = relative.Replace('\\', '/').TrimStart('/');
        return osType == VmInitOsType.Windows
            ? root.TrimEnd('\\') + "\\" + relative.Replace('/', '\\')
            : root.TrimEnd('/') + "/" + relative;
    }

    static string GuestDirectoryName(VmInitOsType osType, string path)
    {
        var separator = osType == VmInitOsType.Windows ? '\\' : '/';
        var index = path.LastIndexOf(separator);
        return index > 0 ? path[..index] : throw new InvalidOperationException("Guest target path has no parent.");
    }

    static VmBootstrapApplyResponse Failed(
        string stage,
        string message,
        string? errorCode = null,
        string? failedStep = null,
        string? failureCategory = null,
        int? exitCode = null) =>
        new(false, stage, message, 0, [], [], errorCode, failedStep, failureCategory, exitCode);

    private sealed class VmBootstrapCommandException(
        string stage,
        string errorCode,
        string stepId,
        string category,
        int? exitCode,
        string message) : InvalidOperationException(message)
    {
        public string Stage { get; } = stage;
        public string ErrorCode { get; } = errorCode;
        public string StepId { get; } = stepId;
        public string Category { get; } = category;
        public int? ExitCode { get; } = exitCode;
    }

    static VmCapabilityProbeResponse ProbeFailed(
        IReadOnlyList<string> verified,
        IReadOnlyDictionary<string, string> evidence,
        string code,
        string detail) => new(false, verified, evidence, code, detail);

    private sealed record AgentBootstrapManifest(
        int SchemaVersion,
        IReadOnlyList<string> OperatingSystems,
        IReadOnlyList<AgentBootstrapParameter> Parameters,
        IReadOnlyList<AgentBootstrapFile> Files,
        IReadOnlyList<AgentBootstrapStep> Steps,
        IReadOnlyList<AgentBootstrapHealthCheck> HealthChecks,
        int MaxReboots);

    private sealed record AgentBootstrapParameter(
        string Key,
        string Type,
        bool Required,
        bool Secret,
        string? DefaultValue);

    private sealed record AgentBootstrapFile(
        string SourcePath,
        string TargetPath,
        string Mode,
        bool Template);

    private sealed record AgentBootstrapStep(
        string Id,
        string Entrypoint,
        int TimeoutSeconds,
        string RunAs,
        string Reboot);

    private sealed record AgentBootstrapHealthCheck(
        string Id,
        string Kind,
        string Target,
        int TimeoutSeconds,
        int Attempts);

    private sealed record AgentBootstrapStepCheckpoint(
        int RuntimeId,
        int Generation,
        string AssetKey,
        Guid ProfileId,
        int ProfileVersion,
        string ArtifactDigest,
        string StepId,
        bool RebootRequired,
        bool RebootCompleted,
        DateTimeOffset CompletedAt);

    private sealed record BootstrapInventoryState(
        int RuntimeId,
        int Generation,
        string AssetKey,
        string VmName,
        string State,
        string? ArtifactDigest,
        DateTimeOffset UpdatedAt);

    [GeneratedRegex(@"\$\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex BootstrapStepId();
}
