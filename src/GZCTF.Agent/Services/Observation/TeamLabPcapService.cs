using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Agent.Models;
using GZCTF.Agent.Services.TeamLab;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.Observation;

public sealed class TeamLabPcapService(
    ObservationPointRegistry registry,
    PcapSegmentUploader uploader,
    TeamLabCommandExecutor executor,
    IOptions<AgentTeamLabConfig> options,
    ILogger<TeamLabPcapService> logger)
{
    private const string CaptureRoot = "/var/lib/gzctf/captures";
    private readonly AgentTeamLabConfig _config = options.Value;
    private readonly ConcurrentDictionary<Guid, Process> _processes = [];
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = [];
    private readonly ConcurrentDictionary<Guid, Task> _monitors = [];
    private readonly ConcurrentDictionary<Guid, byte> _deleting = [];

    public async Task<TeamLabCaptureResponse> StartAsync(
        TeamLabCaptureStartRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId);
        if (validation is not null)
            return Failed(request.SegmentId, validation, request.DryRun);
        if (request.MaxSeconds is < 1 or > 86_400 || request.MaxBytes is < 1024 or > 10L * 1024 * 1024 * 1024)
            return Failed(request.SegmentId, "Capture limits are invalid.", request.DryRun);
        var registrations = registry.Snapshot()
            .Where(item => item.RuntimeId == request.RuntimeId && item.Generation == request.Generation &&
                           item.PublicId == request.ObservationPointId)
            .Select(item => item.InterfaceName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (registrations.Length == 0)
            return Failed(request.SegmentId, "Observation point is not registered on this WorkerNode.", request.DryRun);
        if (_config.DryRun || request.DryRun || !_config.Enable)
        {
            var (planPort, planMirror) = TeamLabCaptureMirror.Names(request.SegmentId);
            var plan = TeamLabCaptureMirror.BuildSetupCommands(
                    _config.OvsIntegrationBridgeName, registrations[0], planPort, planMirror, request.RuntimeId)
                .Concat(BuildCommandPlan(request, registrations))
                .ToArray();
            return new TeamLabCaptureResponse(
                true, true, "Capture command plan returned without execution.", request.SegmentId, null, 0, false,
                null, false, plan);
        }

        var gate = _gates.GetOrAdd(request.SegmentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_deleting.ContainsKey(request.SegmentId))
                return Failed(request.SegmentId, "Capture segment deletion is in progress.", request.DryRun);
            var state = await LoadAsync(request, cancellationToken);
            if (state is { Uploaded: true })
                return ToResponse(state, "Capture segment is already uploaded.");
            if (state is { Status: PcapSegmentStateStatus.Captured } && File.Exists(state.FilePath))
                return ToResponse(state, "Capture segment is already complete.");
            if (state is { Status: PcapSegmentStateStatus.Running } && IsRunning(state))
                return ToResponse(state, "Capture segment is already running.");
            if (state is { Status: PcapSegmentStateStatus.Running })
            {
                state = await FinalizeAsync(state, cancellationToken);
                if (state.Status == PcapSegmentStateStatus.Captured)
                    return ToResponse(state, "Capture segment completed before the start retry.");
            }
            if (state is not null && File.Exists(state.FilePath) &&
                new FileInfo(state.FilePath).Length > 0)
            {
                state = await FinalizeAsync(state, cancellationToken);
                return ToResponse(state, "Existing capture evidence was recovered before the start retry.");
            }

            var directory = SegmentDirectory(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId);
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, "capture.pcapng");
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
            {
                state = new PcapSegmentState(
                    request.RuntimeId,
                    request.Generation,
                    request.CaptureId,
                    request.SegmentId,
                    request.ObservationPointId,
                    registrations,
                    filePath,
                    null,
                    null,
                    PcapSegmentStateStatus.Running,
                    0,
                    null,
                    false,
                    DateTimeOffset.UtcNow,
                    null,
                    null);
                state = await FinalizeAsync(state, cancellationToken);
                return ToResponse(state, "Unindexed capture evidence was recovered before the start retry.");
            }
            if (File.Exists(filePath)) File.Delete(filePath);
            // OVS Kernel Datapath Megaflow fast path bypasses per-veth AF_PACKET capture,
            // so capturing directly on the workload veth yields empty files once flows are
            // cached. Instead set up an OVS mirror from the workload port (source veth) to a
            // dedicated internal capture port in the host netns, then capture on that port.
            var (capturePort, mirrorName) = TeamLabCaptureMirror.Names(request.SegmentId);
            var mirrorResult = await executor.ExecuteAsync(
                TeamLabCaptureMirror.BuildSetupCommands(
                    _config.OvsIntegrationBridgeName, registrations[0], capturePort, mirrorName, request.RuntimeId),
                requestDryRun: false,
                cancellationToken);
            if (!mirrorResult.Success)
                return Failed(request.SegmentId,
                    "OVS capture mirror setup failed: " + mirrorResult.Message, request.DryRun);
            var startInfo = BuildStartInfo(request, [capturePort], filePath);
            var process = Process.Start(startInfo)
                          ?? throw new InvalidOperationException("Unable to start packet capture process.");
            var started = DateTimeOffset.UtcNow;
            state = new PcapSegmentState(
                request.RuntimeId,
                request.Generation,
                request.CaptureId,
                request.SegmentId,
                request.ObservationPointId,
                registrations,
                filePath,
                process.Id,
                ReadProcessStartTicks(process.Id),
                PcapSegmentStateStatus.Running,
                0,
                null,
                false,
                started,
                null,
                null);
            _processes[request.SegmentId] = process;
            await SaveAsync(state, cancellationToken);
            _monitors[request.SegmentId] = MonitorAsync(process, state);
            return ToResponse(state, "Capture segment started.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(exception, "TeamLab capture segment {SegmentId} failed to start.", request.SegmentId);
            return Failed(request.SegmentId, "Capture segment could not be started.", request.DryRun);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TeamLabCaptureResponse> StopAsync(
        TeamLabCaptureStopRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId);
        if (validation is not null)
            return Failed(request.SegmentId, validation, request.DryRun);
        if (_config.DryRun || request.DryRun || !_config.Enable)
            return new TeamLabCaptureResponse(
                true, true, "Capture stop plan returned without execution.", request.SegmentId, null, 0, false,
                null, false, []);
        var gate = _gates.GetOrAdd(request.SegmentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId,
                cancellationToken);
            if (state is null)
                return Failed(request.SegmentId, "Capture segment was not found.", false);
            if (state.Status != PcapSegmentStateStatus.Running)
                return ToResponse(state, "Capture segment is not running.");
            StopOwnedProcess(state);
            state = await FinalizeAsync(state, cancellationToken);
            await TeardownMirrorAsync(request.SegmentId, cancellationToken);
            return ToResponse(state, "Capture segment stopped.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TeamLabCaptureResponse> StatusAsync(
        TeamLabCaptureStatusRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId);
        if (validation is not null)
            return Failed(request.SegmentId, validation, request.DryRun);
        var gate = _gates.GetOrAdd(request.SegmentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId,
                cancellationToken);
            if (state is null)
                return Failed(request.SegmentId, "Capture segment was not found.", request.DryRun);
            if (state.Status == PcapSegmentStateStatus.Running && !IsRunning(state))
                state = await FinalizeAsync(state, cancellationToken);
            return ToResponse(state, state.Status == PcapSegmentStateStatus.Running
                ? "Capture segment is running."
                : "Capture segment is complete.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TeamLabCaptureResponse> UploadAsync(
        TeamLabCaptureUploadRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId);
        if (validation is not null)
            return Failed(request.SegmentId, validation, request.DryRun);
        if (_config.DryRun || request.DryRun || !_config.Enable)
            return new TeamLabCaptureResponse(
                true, true, "Capture upload plan returned without execution.", request.SegmentId, null, 0, false,
                null, false, []);
        var gate = _gates.GetOrAdd(request.SegmentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId,
                cancellationToken);
            if (state is null || state.Status == PcapSegmentStateStatus.Running)
                return Failed(request.SegmentId, "Capture segment is not ready for upload.", false);
            if (state.Uploaded)
                return ToResponse(state, "Capture segment is already uploaded.");
            if (string.IsNullOrWhiteSpace(state.Sha256) || !File.Exists(state.FilePath))
                return Failed(request.SegmentId, "Capture segment file is unavailable.", false);
            state = state with { Status = PcapSegmentStateStatus.Uploading, LastError = null };
            await SaveAsync(state, cancellationToken);
            var result = await uploader.UploadAsync(
                request.UploadPath, request.UploadToken, state.FilePath, state.Sha256, request.MaxBytes,
                cancellationToken);
            if (!result.Success)
            {
                state = state with { Status = PcapSegmentStateStatus.Captured, LastError = result.Message };
                await SaveAsync(state, cancellationToken);
                return ToResponse(state, result.Message, success: false);
            }
            state = state with
            {
                Status = PcapSegmentStateStatus.Uploaded,
                Uploaded = true,
                UploadedAt = DateTimeOffset.UtcNow,
                LastError = null
            };
            await SaveAsync(state, cancellationToken);
            File.Delete(state.FilePath);
            return ToResponse(state, result.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<TeamLabCaptureResponse> DeleteAsync(
        TeamLabCaptureDeleteRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId);
        if (validation is not null)
            return Failed(request.SegmentId, validation, request.DryRun);
        if (_config.DryRun || request.DryRun || !_config.Enable)
            return new TeamLabCaptureResponse(
                true, true, "Capture delete plan returned without execution.", request.SegmentId, null, 0, false,
                null, false, []);
        var gate = _gates.GetOrAdd(request.SegmentId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            _deleting[request.SegmentId] = 0;
            var state = await LoadAsync(
                request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId, cancellationToken);
            if (state is not null) StopOwnedProcess(state);
            if (_monitors.TryGetValue(request.SegmentId, out var monitor))
                await monitor.WaitAsync(cancellationToken);
            _monitors.TryRemove(request.SegmentId, out _);
            _processes.TryRemove(request.SegmentId, out var process);
            process?.Dispose();
            await TeardownMirrorAsync(request.SegmentId, cancellationToken);
            var directory = SegmentDirectory(
                request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            return new TeamLabCaptureResponse(
                true, false, "Capture segment deleted.", request.SegmentId, null, 0, false, null, false, []);
        }
        finally
        {
            _deleting.TryRemove(request.SegmentId, out _);
            gate.Release();
        }
    }

    public async Task CleanupGenerationAsync(int runtimeId, int generation, CancellationToken cancellationToken)
    {
        var generationRoot = Path.Combine(CaptureRoot, $"runtime-{runtimeId}", $"generation-{generation}");
        foreach (var statePath in Directory.Exists(generationRoot)
                     ? Directory.EnumerateFiles(generationRoot, "state.json", SearchOption.AllDirectories)
                     : [])
        {
            try
            {
                var state = await ReadStateAsync(statePath, cancellationToken);
                if (state is not null)
                    await DeleteAsync(new TeamLabCaptureDeleteRequest(
                        state.RuntimeId,
                        state.Generation,
                        state.CaptureId,
                        state.SegmentId,
                        false), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Failed to inspect capture state during generation cleanup.");
            }
        }
        if (Directory.Exists(generationRoot)) Directory.Delete(generationRoot, true);
    }

    public async Task<IReadOnlyList<RuntimeInventoryResource>> SnapshotInventoryAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(CaptureRoot)) return [];
        List<RuntimeInventoryResource> resources = [];
        foreach (var statePath in Directory.EnumerateFiles(
                     CaptureRoot, "state.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var state = await ReadStateAsync(statePath, cancellationToken);
                if (state is null) continue;
                resources.Add(new RuntimeInventoryResource(
                    state.FilePath,
                    state.SegmentId.ToString("D"),
                    state.Generation,
                    state.Status.ToString().ToLowerInvariant(),
                    null,
                    "pcap-segment",
                    state.RuntimeId,
                    state.Sha256));
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Failed to read TeamLab capture inventory state.");
            }
        }
        return resources;
    }

    private async Task MonitorAsync(Process process, PcapSegmentState state)
    {
        try
        {
            await process.WaitForExitAsync();
            if (!_deleting.ContainsKey(state.SegmentId))
                await FinalizeAsync(state, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Capture segment {SegmentId} finalization failed.", state.SegmentId);
        }
        finally
        {
            _processes.TryRemove(state.SegmentId, out _);
            process.Dispose();
        }
    }

    private async Task<PcapSegmentState> FinalizeAsync(
        PcapSegmentState state,
        CancellationToken cancellationToken)
    {
        var bytes = File.Exists(state.FilePath) ? new FileInfo(state.FilePath).Length : 0;
        string? digest = null;
        if (bytes > 0)
        {
            await using var stream = File.OpenRead(state.FilePath);
            digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }
        state = state with
        {
            ProcessId = null,
            ProcessStartTicks = null,
            Status = bytes > 0 ? PcapSegmentStateStatus.Captured : PcapSegmentStateStatus.Failed,
            CapturedBytes = bytes,
            Sha256 = digest,
            CompletedAt = DateTimeOffset.UtcNow,
            LastError = bytes > 0 ? null : "Capture process produced no packet file."
        };
        if (!_deleting.ContainsKey(state.SegmentId))
            await SaveAsync(state, cancellationToken);
        return state;
    }

    private static ProcessStartInfo BuildStartInfo(
        TeamLabCaptureStartRequest request,
        IReadOnlyList<string> interfaces,
        string filePath)
    {
        // Prefer tcpdump (-Z root keeps it capturing/writing as the service account), which
        // is the capture tool verified to see traffic on OVS-managed veths. dumpcap on the
        // deployed nodes drops to an unprivileged user after opening the device (no working
        // -Z override on this build) so it cannot open the output file and yields empty
        // pcapng files; keep it only as a fallback when tcpdump is absent.
        if (CommandExists("tcpdump"))
        {
            if (interfaces.Count != 1)
                throw new InvalidOperationException(
                    "Multiple capture interfaces require per-segment capture; a single interface is expected here.");
            var fallback = BaseStartInfo("timeout");
            fallback.ArgumentList.Add("--signal=INT");
            fallback.ArgumentList.Add(request.MaxSeconds.ToString());
            fallback.ArgumentList.Add("tcpdump");
            fallback.ArgumentList.Add("-Z");
            fallback.ArgumentList.Add("root");
            fallback.ArgumentList.Add("-i");
            fallback.ArgumentList.Add(interfaces[0]);
            fallback.ArgumentList.Add("-s");
            fallback.ArgumentList.Add("0");
            fallback.ArgumentList.Add("-U");
            fallback.ArgumentList.Add("-B");
            fallback.ArgumentList.Add("8192");
            fallback.ArgumentList.Add("-w");
            fallback.ArgumentList.Add(filePath);
            return fallback;
        }
        if (CommandExists("dumpcap"))
        {
            var info = BaseStartInfo("dumpcap");
            info.ArgumentList.Add("-q");
            foreach (var interfaceName in interfaces)
            {
                info.ArgumentList.Add("-i");
                info.ArgumentList.Add(interfaceName);
            }
            info.ArgumentList.Add("-a");
            info.ArgumentList.Add($"duration:{request.MaxSeconds}");
            info.ArgumentList.Add("-a");
            info.ArgumentList.Add($"filesize:{Math.Max(1, (request.MaxBytes + 1023) / 1024)}");
            info.ArgumentList.Add("-w");
            info.ArgumentList.Add(filePath);
            return info;
        }
        throw new InvalidOperationException(
            "Neither tcpdump nor dumpcap is available for packet capture on this WorkerNode.");
    }

    private static ProcessStartInfo BaseStartInfo(string fileName) => new()
    {
        FileName = fileName,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = false,
        RedirectStandardError = false
    };

    private static string[] BuildCommandPlan(
        TeamLabCaptureStartRequest request,
        IReadOnlyList<string> interfaces) =>
        [$"capture segment {request.SegmentId:D} on {interfaces.Count} managed interface(s) for {request.MaxSeconds}s/{request.MaxBytes} bytes"];

    private static bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, command)));
    }

    private bool IsRunning(PcapSegmentState state)
    {
        if (state.ProcessId is not { } pid || state.ProcessStartTicks is not { } expected) return false;
        try
        {
            return ReadProcessStartTicks(pid) == expected && !Process.GetProcessById(pid).HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void StopOwnedProcess(PcapSegmentState state)
    {
        if (!IsRunning(state) || state.ProcessId is not { } pid) return;
        try
        {
            var process = _processes.GetValueOrDefault(state.SegmentId) ?? Process.GetProcessById(pid);
            // dumpcap/tcpdump buffer pcapng blocks in memory and only flush them when the
            // capture is closed cleanly. SIGKILL on stop previously left header-only
            // (232-byte) segment files even though frames were captured. Send SIGINT first
            // so the tool finalizes and flushes the archive, then hard-kill only as a
            // fallback if it does not exit promptly.
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    Process.Start(new ProcessStartInfo("kill")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        ArgumentList = { "-INT", pid.ToString() }
                    })?.Dispose();
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    logger.LogDebug(exception, "Failed to send SIGINT to capture process {ProcessId}.", pid);
                }
                if (process.WaitForExit(2000))
                {
                    // pcapng finalized and flushed; nothing more to do.
                    return;
                }
            }
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            logger.LogDebug(exception, "Capture process {ProcessId} was already absent.", pid);
        }
    }

    /// <summary>
    /// Removes the OVS capture mirror + internal capture port for a segment. The
    /// desired-state naming is derived only from the segment id so teardown is
    /// idempotent even if the capture never started (mirror/port may not exist).
    /// </summary>
    private async Task TeardownMirrorAsync(
        Guid segmentId,
        CancellationToken cancellationToken)
    {
        var (capturePort, mirrorName) = TeamLabCaptureMirror.Names(segmentId);
        try
        {
            await executor.ExecuteAsync(
                TeamLabCaptureMirror.BuildTeardownCommands(_config.OvsIntegrationBridgeName, capturePort, mirrorName),
                requestDryRun: false,
                token: cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Failed to tear down capture mirror for segment {SegmentId}.", segmentId);
        }
    }

    private static long ReadProcessStartTicks(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var close = stat.LastIndexOf(')');
        var fields = stat[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(fields[19]);
    }

    private static string? Validate(int runtimeId, int generation, Guid captureId, Guid segmentId) =>
        runtimeId <= 0 || generation <= 0 || captureId == Guid.Empty || segmentId == Guid.Empty
            ? "Capture identity is invalid."
            : null;

    private async Task<PcapSegmentState?> LoadAsync(
        TeamLabCaptureStartRequest request,
        CancellationToken cancellationToken) =>
        await LoadAsync(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId,
            cancellationToken);

    private static async Task<PcapSegmentState?> LoadAsync(
        int runtimeId,
        int generation,
        Guid captureId,
        Guid segmentId,
        CancellationToken cancellationToken)
    {
        var path = StatePath(runtimeId, generation, captureId, segmentId);
        return File.Exists(path) ? await ReadStateAsync(path, cancellationToken) : null;
    }

    private static async Task<PcapSegmentState?> ReadStateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PcapSegmentState>(stream, cancellationToken: cancellationToken);
    }

    private static async Task SaveAsync(PcapSegmentState state, CancellationToken cancellationToken)
    {
        var path = StatePath(state.RuntimeId, state.Generation, state.CaptureId, state.SegmentId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, state, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string SegmentDirectory(int runtimeId, int generation, Guid captureId, Guid segmentId) =>
        Path.Combine(CaptureRoot, $"runtime-{runtimeId}", $"generation-{generation}",
            $"capture-{captureId:N}", $"segment-{segmentId:N}");

    private static string StatePath(int runtimeId, int generation, Guid captureId, Guid segmentId) =>
        Path.Combine(SegmentDirectory(runtimeId, generation, captureId, segmentId), "state.json");

    private static TeamLabCaptureResponse Failed(Guid segmentId, string message, bool dryRun) =>
        new(false, dryRun, message, segmentId, null, 0, false, null, false, []);

    private static TeamLabCaptureResponse ToResponse(
        PcapSegmentState state,
        string message,
        bool success = true) =>
        new(success, false, message, state.SegmentId, state.FilePath, state.CapturedBytes,
            state.Status == PcapSegmentStateStatus.Running, state.Sha256, state.Uploaded, []);

    /// <summary>
    /// Desired-state for the per-segment OVS capture mirror. The OVS kernel
    /// datapath fast path (Megaflow) bypasses AF_PACKET capture on the runtime
    /// workload veths once a flow is cached, so a per-veth tcpdump silently
    /// yields empty pcapngs. The reliable mechanism is an OVS Mirror that copies
    /// packets at datapath-action time from the workload port (src+dst) to a
    /// dedicated internal port in the host netns; tcpdump then captures on that
    /// internal port. The mirror and capture-port names derive only from the
    /// segment id so setup/teardown are idempotent.
    /// </summary>
    internal static class TeamLabCaptureMirror
    {
        public static (string CapturePort, string MirrorName) Names(Guid segmentId)
        {
            var token = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(segmentId.ToString("N"))));
            return ($"gzcp{token[..10]}", $"gzctf-m{token[..10]}");
        }

        public static string[] BuildSetupCommands(
            string bridge,
            string sourceInterface,
            string capturePort,
            string mirrorName,
            int runtimeId)
        {
            return
            [
                $"ovs-vsctl --if-exists destroy Mirror {mirrorName}",
                $"ovs-vsctl --if-exists del-port {bridge} {capturePort}",
                $"ovs-vsctl --may-exist add-port {bridge} {capturePort} -- set Interface {capturePort} type=internal -- set Interface {capturePort} external-ids:gzctf-capture={runtimeId}",
                $"ip link set {capturePort} up",
                $"ovs-vsctl --id=@src get Port {sourceInterface} _uuid -- --id=@out get Port {capturePort} _uuid -- --id=@m create Mirror name={mirrorName} select-src-port=@src select-dst-port=@src output-port=@out -- set Bridge {bridge} mirrors=@m"
            ];
        }

        public static string[] BuildTeardownCommands(string bridge, string capturePort, string mirrorName) =>
        [
            $"ovs-vsctl --if-exists destroy Mirror {mirrorName}",
            $"ovs-vsctl --if-exists del-port {bridge} {capturePort}"
        ];
    }

    private enum PcapSegmentStateStatus : byte
    {
        Running = 0,
        Captured = 1,
        Uploading = 2,
        Uploaded = 3,
        Failed = 4
    }

    private sealed record PcapSegmentState(
        int RuntimeId,
        int Generation,
        Guid CaptureId,
        Guid SegmentId,
        Guid ObservationPointId,
        IReadOnlyList<string> Interfaces,
        string FilePath,
        int? ProcessId,
        long? ProcessStartTicks,
        PcapSegmentStateStatus Status,
        long CapturedBytes,
        string? Sha256,
        bool Uploaded,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? UploadedAt,
        string? LastError = null);
}
