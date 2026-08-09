using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.Vm;

namespace GZCTF.Agent.Services.Observation;

public sealed partial class EndpointSensorChannelService(
    ObservationBatchSpool spool,
    DockerService docker,
    VmGuestAgentService guest,
    ILogger<EndpointSensorChannelService> logger) : IAsyncDisposable
{
    private const int MaxEventBytes = 16 * 1024;
    internal const string InjectionVolumeLabel = "GZCTFINIT";
    internal const string LinuxSensorFileName = "gzctf-endpoint-sensor";
    internal const string WindowsSensorFileName = "gzctf-endpoint-sensor.exe";
    internal const string LinuxSensorPath = "/opt/gzctf/endpoint-sensor/linux-x64/" + LinuxSensorFileName;
    internal const string WindowsSensorPath = "/opt/gzctf/endpoint-sensor/win-x64/" + WindowsSensorFileName;
    private const string WindowsTaskSchedulerPath = @"C:\Windows\System32\schtasks.exe";
    private readonly ConcurrentDictionary<string, SensorRegistration> _registrations =
        new(StringComparer.Ordinal);
    private long _rejectedCount;
    private string? _lastRejectedCode;

    internal EndpointSensorChannelService(
        ObservationBatchSpool spool,
        ILogger<EndpointSensorChannelService> logger)
        : this(spool, null!, null!, logger)
    {
    }

    public TeamLabEndpointSensorResponse Register(TeamLabEndpointSensorRegistrationRequest request)
    {
        if (request.RuntimeId <= 0 || request.Generation <= 0 || request.SensorVersion != 1 ||
            !Guid.TryParse(request.RuntimePublicId, out var runtimePublicId) ||
            !SafeToken().IsMatch(request.AssetKey) || !SafeToken().IsMatch(request.RuntimeResourceId))
            return new TeamLabEndpointSensorResponse(false, "Endpoint sensor registration is invalid.");
        byte[] key;
        try
        {
            key = Convert.FromBase64String(request.HmacKeyBase64);
        }
        catch (FormatException)
        {
            return new TeamLabEndpointSensorResponse(false, "Endpoint sensor credential is invalid.");
        }
        if (key.Length < 32)
            return new TeamLabEndpointSensorResponse(false, "Endpoint sensor credential is too short.");
        var identity = Identity(request.RuntimeId, request.Generation, request.AssetKey);
        Remove(request.RuntimeId, request.Generation, request.AssetKey);
        var path = request.Mode == TeamLabEndpointSensorChannelMode.Vm
            ? VmSocketPath(request.RuntimeResourceId, request.Generation)
            : DockerSocketPath(request.RuntimeId, request.Generation, request.AssetKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) File.Delete(path);
        var registration = new SensorRegistration(
            request.RuntimeId,
            runtimePublicId,
            request.Generation,
            request.AssetKey,
            request.Mode,
            path,
            key,
            new CancellationTokenSource());
        if (request.Mode == TeamLabEndpointSensorChannelMode.Docker)
        {
            registration.Listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            registration.Listener.Bind(new UnixDomainSocketEndPoint(path));
            registration.Listener.Listen(1);
        }
        _registrations[identity] = registration;
        registration.Worker = Task.Run(() => RunAsync(registration), CancellationToken.None);
        return new TeamLabEndpointSensorResponse(
            true,
            "Endpoint sensor channel registered.",
            request.Mode == TeamLabEndpointSensorChannelMode.Docker ? $"unix://{path}" : path);
    }

    public TeamLabEndpointSensorResponse Remove(int runtimeId, int generation, string assetKey)
    {
        if (!_registrations.TryRemove(Identity(runtimeId, generation, assetKey), out var registration))
            return new TeamLabEndpointSensorResponse(true, "Endpoint sensor channel was already absent.");
        StopHostProcess(registration);
        registration.Cancellation.Cancel();
        registration.Listener?.Dispose();
        if (registration.Mode == TeamLabEndpointSensorChannelMode.Docker && File.Exists(registration.Path))
            File.Delete(registration.Path);
        _ = (registration.Worker ?? Task.CompletedTask).ContinueWith(
            _ =>
            {
                registration.Cancellation.Dispose();
                CryptographicOperations.ZeroMemory(registration.Key);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return new TeamLabEndpointSensorResponse(true, "Endpoint sensor channel removed.");
    }

    public async Task<TeamLabEndpointSensorResponse> StartAsync(
        TeamLabEndpointSensorStartRequest request,
        CancellationToken cancellationToken)
    {
        if (!_registrations.TryGetValue(Identity(request.RuntimeId, request.Generation, request.AssetKey),
                out var registration))
            return new TeamLabEndpointSensorResponse(false, "Endpoint sensor channel is not registered.");
        if (registration.Mode != request.Mode || !SafeToken().IsMatch(request.RuntimeResourceId))
            return new TeamLabEndpointSensorResponse(false, "Endpoint sensor runtime identity is invalid.");

        await registration.StartGate.WaitAsync(cancellationToken);
        try
        {
            registration.RuntimeResourceId = request.RuntimeResourceId;
            var result = request.Mode == TeamLabEndpointSensorChannelMode.Docker
                ? await StartDockerAsync(registration, cancellationToken)
                : await StartVmAsync(registration, request.OsType, cancellationToken);
            registration.Started = result.Success;
            registration.LastError = result.Success ? null : result.Message;
            return result;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            registration.Started = false;
            registration.LastError = Trim(exception.Message);
            logger.LogWarning(exception,
                "Endpoint sensor start failed: runtime={RuntimeId}, generation={Generation}, asset={AssetKey}.",
                registration.RuntimeId, registration.Generation, registration.AssetKey);
            return new TeamLabEndpointSensorResponse(false, "Endpoint sensor could not be started.");
        }
        finally
        {
            registration.StartGate.Release();
        }
    }

    public IReadOnlyList<RuntimeInventoryResource> SnapshotInventory() =>
        _registrations.Values
            .OrderBy(item => item.RuntimeId)
            .ThenBy(item => item.Generation)
            .ThenBy(item => item.AssetKey, StringComparer.Ordinal)
            .Select(item => new RuntimeInventoryResource(
                item.RuntimeResourceId ?? item.Path,
                item.AssetKey,
                item.Generation,
                item.Started && item.Worker is { IsCompleted: false } ? "running" : "registered",
                null,
                "endpoint-sensor",
                item.RuntimeId))
            .ToArray();

    internal bool AcceptLine(
        SensorRegistration registration,
        ReadOnlySpan<byte> payload,
        DateTimeOffset now,
        out string code)
    {
        code = "sensor_payload_invalid";
        if (payload.Length is 0 or > MaxEventBytes) return false;
        EndpointSensorEvent? value;
        try
        {
            value = JsonSerializer.Deserialize<EndpointSensorEvent>(payload);
        }
        catch (JsonException)
        {
            return false;
        }
        if (value is null) return false;
        lock (registration.SequenceLock)
        {
            var verification = EndpointSensorAuthenticator.Verify(
                value,
                registration.RuntimePublicId,
                registration.Generation,
                registration.AssetKey,
                registration.LastSequence,
                registration.Key,
                now);
            code = verification.Code;
            if (!verification.Success || verification.ProcessIdentityHash is null ||
                verification.FlowFingerprint is null)
            {
                Interlocked.Increment(ref _rejectedCount);
                Volatile.Write(ref _lastRejectedCode, code);
                return false;
            }
            registration.LastSequence = value.Sequence;
            spool.AppendEndpoint(
                registration.RuntimeId,
                registration.Generation,
                registration.AssetKey,
                value.ObservedAt,
                value.Local.Address,
                value.Local.Port,
                value.Remote.Address,
                value.Remote.Port,
                value.Local.Protocol,
                verification.FlowFingerprint,
                verification.ProcessIdentityHash,
                value.Kind.ToString().ToLowerInvariant());
            return true;
        }
    }

    public (long Count, string? LastCode) RejectionSnapshot() =>
        (Interlocked.Read(ref _rejectedCount), Volatile.Read(ref _lastRejectedCode));

    public async ValueTask DisposeAsync()
    {
        var registrations = _registrations.Values.ToArray();
        _registrations.Clear();
        foreach (var registration in registrations) registration.Cancellation.Cancel();
        await Task.WhenAll(registrations.Select(item => item.Worker ?? Task.CompletedTask));
        foreach (var registration in registrations)
        {
            StopHostProcess(registration);
            registration.Cancellation.Dispose();
            registration.StartGate.Dispose();
            registration.Listener?.Dispose();
            CryptographicOperations.ZeroMemory(registration.Key);
        }
    }

    public static string VmSocketPath(string vmName, int generation) =>
        SocketPath($"vm:{vmName}:{generation}");

    public static string DockerSocketPath(int runtimeId, int generation, string assetKey) =>
        SocketPath($"docker:{runtimeId}:{generation}:{assetKey}");

    private async Task RunAsync(SensorRegistration registration)
    {
        try
        {
            if (registration.Mode == TeamLabEndpointSensorChannelMode.Docker)
                await RunListenerAsync(registration);
            else
                await RunConnectorAsync(registration);
        }
        catch (OperationCanceledException) when (registration.Cancellation.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (registration.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception,
                "Endpoint sensor channel failed: runtime={RuntimeId}, generation={Generation}, asset={AssetKey}.",
                registration.RuntimeId, registration.Generation, registration.AssetKey);
        }
    }

    private async Task RunListenerAsync(SensorRegistration registration)
    {
        var listener = registration.Listener ??
                       throw new InvalidOperationException("Docker sensor listener was not initialized.");
        while (!registration.Cancellation.IsCancellationRequested)
        {
            using var client = await listener.AcceptAsync(registration.Cancellation.Token);
            await using var stream = new NetworkStream(client, ownsSocket: false);
            await ReadStreamAsync(registration, stream);
        }
    }

    private async Task RunConnectorAsync(SensorRegistration registration)
    {
        while (!registration.Cancellation.IsCancellationRequested)
        {
            try
            {
                using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await client.ConnectAsync(
                    new UnixDomainSocketEndPoint(registration.Path), registration.Cancellation.Token);
                await using var stream = new NetworkStream(client, ownsSocket: false);
                await ReadStreamAsync(registration, stream);
            }
            catch (Exception exception) when (
                (exception is IOException or SocketException) && !registration.Cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), registration.Cancellation.Token);
            }
        }
    }

    private async Task<TeamLabEndpointSensorResponse> StartDockerAsync(
        SensorRegistration registration,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(LinuxSensorPath))
            return new TeamLabEndpointSensorResponse(false, "Linux endpoint sensor artifact is unavailable.");
        var containerId = registration.RuntimeResourceId!;
        var containerPid = await docker.GetContainerPidAsync(containerId, cancellationToken);
        if (containerPid <= 0)
            return new TeamLabEndpointSensorResponse(false, "Container process namespace is unavailable.");

        StopHostProcess(registration);
        StopOrphanProcess(registration);
        var startInfo = new ProcessStartInfo
        {
            FileName = "nsenter",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(containerPid.ToString());
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(LinuxSensorPath);
        ApplyEnvironment(startInfo.Environment, registration, $"unix://{registration.Path}");
        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("Unable to launch Docker endpoint sensor.");
        _ = DrainAsync(process.StandardOutput);
        _ = DrainAsync(process.StandardError);
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        if (process.HasExited)
        {
            process.Dispose();
            return new TeamLabEndpointSensorResponse(false, "Docker endpoint sensor exited during startup.");
        }

        registration.HostProcess = process;
        registration.PidFile = SensorPidPath(registration);
        Directory.CreateDirectory(Path.GetDirectoryName(registration.PidFile)!);
        await File.WriteAllTextAsync(registration.PidFile, process.Id.ToString(), cancellationToken);
        return new TeamLabEndpointSensorResponse(true, "Docker endpoint sensor started.", registration.Path);
    }

    private async Task<TeamLabEndpointSensorResponse> StartVmAsync(
        SensorRegistration registration,
        VmInitOsType? osType,
        CancellationToken cancellationToken)
    {
        if (osType is null)
            return new TeamLabEndpointSensorResponse(false, "VM endpoint sensor OS type is missing.");
        return osType == VmInitOsType.Windows
            ? await StartWindowsVmAsync(registration, cancellationToken)
            : await StartLinuxVmAsync(registration, cancellationToken);
    }

    private async Task<TeamLabEndpointSensorResponse> StartLinuxVmAsync(
        SensorRegistration registration,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(LinuxSensorPath))
            return new TeamLabEndpointSensorResponse(false, "Linux endpoint sensor artifact is unavailable.");
        var vmName = registration.RuntimeResourceId!;
        const string targetDirectory = "/opt/gzctf/endpoint-sensor";
        const string targetBinary = targetDirectory + "/gzctf-endpoint-sensor";
        const string environmentPath = "/etc/gzctf/endpoint-sensor.env";
        const string unitPath = "/etc/systemd/system/gzctf-endpoint-sensor.service";
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-mkdir", "/usr/bin/install", ["-d", "-m", "0755", targetDirectory, "/etc/gzctf"], 30),
            cancellationToken);
        await InstallLinuxSensorFromMediaAsync(vmName, targetBinary, cancellationToken);
        await WriteGuestTextAsync(vmName, environmentPath,
            BuildEnvironmentFile(registration, "/dev/virtio-ports/org.gzctf.sensor.0"), cancellationToken);
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-env-mode", "/usr/bin/chmod", ["0600", environmentPath], 30), cancellationToken);
        await WriteGuestTextAsync(vmName, unitPath, $$"""
            [Unit]
            Description=GZCTF Endpoint Sensor
            After=network-online.target

            [Service]
            Type=simple
            EnvironmentFile={{environmentPath}}
            ExecStart={{targetBinary}}
            Restart=always
            RestartSec=2
            NoNewPrivileges=true
            PrivateTmp=true

            [Install]
            WantedBy=multi-user.target
            """, cancellationToken);
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-enable", "/usr/bin/systemctl", ["daemon-reload"], 30), cancellationToken);
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-start", "/usr/bin/systemctl", ["enable", "--now", "gzctf-endpoint-sensor.service"], 60),
            cancellationToken);
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-health", "/usr/bin/systemctl", ["is-active", "gzctf-endpoint-sensor.service"], 30),
            cancellationToken);
        return new TeamLabEndpointSensorResponse(true, "Linux VM endpoint sensor started.", registration.Path);
    }

    private async Task<TeamLabEndpointSensorResponse> StartWindowsVmAsync(
        SensorRegistration registration,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(WindowsSensorPath))
            return new TeamLabEndpointSensorResponse(false, "Windows endpoint sensor artifact is unavailable.");
        var vmName = registration.RuntimeResourceId!;
        const string targetDirectory = @"C:\ProgramData\GZCTF\EndpointSensor";
        const string targetBinary = targetDirectory + @"\gzctf-endpoint-sensor.exe";
        const string launcherPath = targetDirectory + @"\start-sensor.ps1";
        var taskName = $@"\GZCTF\EndpointSensor-{SensorIdentityDigest(registration)}";
        await ExecutePowerShellAsync(vmName, "sensor-mkdir",
            $"New-Item -ItemType Directory -Force -Path '{PowerShell(targetDirectory)}' | Out-Null",
            cancellationToken);
        await ExecutePowerShellAsync(vmName, "sensor-copy",
            $$"""
            $drive = Get-CimInstance Win32_LogicalDisk | Where-Object { $_.DriveType -eq 5 -and $_.VolumeName -eq '{{InjectionVolumeLabel}}' } | Select-Object -First 1
            if ($null -eq $drive) { throw 'GZCTF injection media is unavailable.' }
            Copy-Item -Force -Path "$($drive.DeviceID)\{{WindowsSensorFileName}}" -Destination '{{PowerShell(targetBinary)}}'
            """,
            cancellationToken);
        await WriteGuestTextAsync(vmName, launcherPath, BuildPowerShellLauncher(registration), cancellationToken);
        await ExecutePowerShellAsync(vmName, "sensor-protect",
            $"icacls.exe '{PowerShell(targetDirectory)}' /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' | Out-Null",
            cancellationToken);
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-task-create", WindowsTaskSchedulerPath,
            ["/Create", "/TN", taskName, "/SC", "ONSTART", "/RU", "SYSTEM", "/RL", "HIGHEST", "/TR",
                $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{launcherPath}\"", "/F"],
            60), cancellationToken);
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-task-run", WindowsTaskSchedulerPath, ["/Run", "/TN", taskName], 30), cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-task-health", WindowsTaskSchedulerPath, ["/Query", "/TN", taskName], 30), cancellationToken);
        return new TeamLabEndpointSensorResponse(true, "Windows VM endpoint sensor started.", registration.Path);
    }

    private async Task InstallLinuxSensorFromMediaAsync(
        string vmName,
        string targetBinary,
        CancellationToken cancellationToken)
    {
        const string mountPath = "/run/gzctf-injection";
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-media-mkdir", "/usr/bin/install", ["-d", "-m", "0755", mountPath], 30),
            cancellationToken);
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            "sensor-media-mount", "/usr/bin/mount",
            ["-o", "ro", $"/dev/disk/by-label/{InjectionVolumeLabel}", mountPath], 30), cancellationToken);
        try
        {
            await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
                "sensor-copy", "/usr/bin/install",
                ["-m", "0755", $"{mountPath}/{LinuxSensorFileName}", targetBinary], 30), cancellationToken);
        }
        finally
        {
            await guest.ExecuteAsync(vmName,
                new VmGuestCommandRequest("sensor-media-unmount", "/usr/bin/umount", [mountPath], 30),
                CancellationToken.None);
        }
    }

    private async Task WriteGuestTextAsync(
        string vmName,
        string guestPath,
        string content,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await guest.WriteFileAsync(vmName, guestPath, stream, cancellationToken);
    }

    private async Task ExecutePowerShellAsync(
        string vmName,
        string stepId,
        string script,
        CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        await ExecuteRequiredAsync(vmName, new VmGuestCommandRequest(
            stepId, VmBootstrapService.WindowsPowerShellPath,
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded], 60),
            cancellationToken);
    }

    private async Task ExecuteRequiredAsync(
        string vmName,
        VmGuestCommandRequest request,
        CancellationToken cancellationToken)
    {
        var result = await guest.ExecuteAsync(vmName, request, cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException(
                $"Endpoint sensor guest step '{request.StepId}' failed ({result.Category}).");
    }

    private static string BuildEnvironmentFile(SensorRegistration registration, string channel) =>
        string.Join('\n', EnvironmentValues(registration, channel).Select(item => $"{item.Key}={item.Value}")) + "\n";

    private static string BuildPowerShellLauncher(SensorRegistration registration)
    {
        var output = new StringBuilder();
        foreach (var pair in EnvironmentValues(registration, @"\\.\Global\org.gzctf.sensor.0"))
            output.AppendLine($"$env:{pair.Key} = '{PowerShell(pair.Value)}'");
        output.AppendLine("& 'C:\\ProgramData\\GZCTF\\EndpointSensor\\gzctf-endpoint-sensor.exe'");
        return output.ToString();
    }

    private static IReadOnlyDictionary<string, string> EnvironmentValues(
        SensorRegistration registration,
        string channel) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GZCTF_SENSOR_RUNTIME_PUBLIC_ID"] = registration.RuntimePublicId.ToString("D"),
        ["GZCTF_SENSOR_GENERATION"] = registration.Generation.ToString(),
        ["GZCTF_SENSOR_ASSET_KEY"] = registration.AssetKey,
        ["GZCTF_SENSOR_CHANNEL"] = channel,
        ["GZCTF_SENSOR_HMAC"] = Convert.ToBase64String(registration.Key)
    };

    private static void ApplyEnvironment(
        IDictionary<string, string?> environment,
        SensorRegistration registration,
        string channel)
    {
        foreach (var pair in EnvironmentValues(registration, channel))
            environment[pair.Key] = pair.Value;
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is not null)
            {
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void StopHostProcess(SensorRegistration registration)
    {
        var process = registration.HostProcess;
        registration.HostProcess = null;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            process.Dispose();
        }
        if (registration.PidFile is not null && File.Exists(registration.PidFile))
            File.Delete(registration.PidFile);
        registration.PidFile = null;
        registration.Started = false;
    }

    private static void StopOrphanProcess(SensorRegistration registration)
    {
        var path = SensorPidPath(registration);
        if (!File.Exists(path) || !int.TryParse(File.ReadAllText(path), out var pid)) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            var executable = $"/proc/{pid}/exe";
            var target = File.ResolveLinkTarget(executable, false)?.FullName;
            if (string.Equals(target, LinuxSensorPath, StringComparison.Ordinal))
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
        }
        File.Delete(path);
    }

    private static string SensorPidPath(SensorRegistration registration) =>
        $"/run/gzctf-sensor/{SensorIdentityDigest(registration)}.pid";

    private static string SensorIdentityDigest(SensorRegistration registration) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            Identity(registration.RuntimeId, registration.Generation, registration.AssetKey))))[..16];

    private static string PowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);
    private static string Trim(string value) => value.Length <= 512 ? value : value[..512];

    private async Task ReadStreamAsync(SensorRegistration registration, Stream stream)
    {
        var buffer = new byte[4096];
        var line = new MemoryStream(MaxEventBytes);
        while (!registration.Cancellation.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, registration.Cancellation.Token);
            if (read == 0) return;
            for (var index = 0; index < read; index++)
            {
                if (buffer[index] == (byte)'\n')
                {
                    AcceptLine(registration, line.GetBuffer().AsSpan(0, (int)line.Length), DateTimeOffset.UtcNow, out _);
                    line.SetLength(0);
                    continue;
                }
                if (line.Length >= MaxEventBytes)
                {
                    line.SetLength(0);
                    continue;
                }
                line.WriteByte(buffer[index]);
            }
        }
    }

    private static string Identity(int runtimeId, int generation, string assetKey) =>
        $"{runtimeId}:{generation}:{assetKey}";

    private static string SocketPath(string identity)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..24];
        return $"/run/gzctf-sensor/{digest}.sock";
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeToken();

    internal sealed class SensorRegistration(
        int runtimeId,
        Guid runtimePublicId,
        int generation,
        string assetKey,
        TeamLabEndpointSensorChannelMode mode,
        string path,
        byte[] key,
        CancellationTokenSource cancellation)
    {
        public int RuntimeId { get; } = runtimeId;
        public Guid RuntimePublicId { get; } = runtimePublicId;
        public int Generation { get; } = generation;
        public string AssetKey { get; } = assetKey;
        public TeamLabEndpointSensorChannelMode Mode { get; } = mode;
        public string Path { get; } = path;
        public byte[] Key { get; } = key;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public object SequenceLock { get; } = new();
        public SemaphoreSlim StartGate { get; } = new(1, 1);
        public long LastSequence { get; set; }
        public Task? Worker { get; set; }
        public Socket? Listener { get; set; }
        public string? RuntimeResourceId { get; set; }
        public Process? HostProcess { get; set; }
        public string? PidFile { get; set; }
        public bool Started { get; set; }
        public string? LastError { get; set; }
    }
}
