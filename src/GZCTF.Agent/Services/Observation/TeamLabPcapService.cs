using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GZCTF.Agent.Models;
using Microsoft.Extensions.Options;

namespace GZCTF.Agent.Services.Observation;

public sealed class TeamLabPcapService(
    ObservationPointRegistry registry,
    PcapSegmentUploader uploader,
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
            return new TeamLabCaptureResponse(
                true, true, "Capture command plan returned without execution.", request.SegmentId, null, 0, false,
                null, false, BuildCommandPlan(request, registrations));

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
            var startInfo = BuildStartInfo(request, registrations, filePath);
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
        var state = await LoadAsync(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId,
            cancellationToken);
        if (state is null)
            return Failed(request.SegmentId, "Capture segment was not found.", false);
        if (state.Status != PcapSegmentStateStatus.Running)
            return ToResponse(state, "Capture segment is not running.");
        StopOwnedProcess(state);
        state = await FinalizeAsync(state, cancellationToken);
        return ToResponse(state, "Capture segment stopped.");
    }

    public async Task<TeamLabCaptureResponse> StatusAsync(
        TeamLabCaptureStatusRequest request,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request.RuntimeId, request.Generation, request.CaptureId, request.SegmentId);
        if (validation is not null)
            return Failed(request.SegmentId, validation, request.DryRun);
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
        if (!CommandExists("tcpdump") || interfaces.Count != 1)
            throw new InvalidOperationException(
                "dumpcap is required for multi-interface capture and tcpdump is unavailable as a fallback.");
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
        fallback.ArgumentList.Add("-w");
        fallback.ArgumentList.Add(filePath);
        fallback.ArgumentList.Add("-C");
        fallback.ArgumentList.Add(Math.Max(1, (request.MaxBytes + 1024 * 1024 - 1) / (1024 * 1024)).ToString());
        fallback.ArgumentList.Add("-W");
        fallback.ArgumentList.Add("1");
        return fallback;
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
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            logger.LogDebug(exception, "Capture process {ProcessId} was already absent.", pid);
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
