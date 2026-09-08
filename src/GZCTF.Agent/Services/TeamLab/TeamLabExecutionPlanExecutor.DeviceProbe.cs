using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GZCTF.TeamLab.Contracts.Execution;

namespace GZCTF.Agent.Services.TeamLab;

public sealed partial class TeamLabExecutionPlanExecutor
{
    public async Task<TeamLabDeviceObservation> ProbeDeviceAsync(TeamLabDeviceProbeRequest request, CancellationToken token)
    {
        TeamLabDeviceObservation Result(string status, string? error = null) => new(status, DateTimeOffset.UtcNow, error);
        var plan = request.Plan;
        if (!plan.IsValid(out _) || plan.Assets.SingleOrDefault(item => item.AssetKey == request.AssetKey) is not { } asset)
            return Result("unavailable", "device.invalid_plan");
        using var executionLock = await executionLocks.AcquireAsync((plan.RuntimeId, plan.Generation, plan.ShardKey), token);
        try
        {
            var actual = (await ReadInventoryAsync(plan, token)).SingleOrDefault(item => item.AssetKey == asset.AssetKey);
            if (actual is null || actual.ResourceId != request.ResourceId || actual.NativeIdentity != request.NativeIdentity)
                return Result("unavailable", "device.identity_changed");
            if (!actual.State.Equals("running", StringComparison.OrdinalIgnoreCase)) return Result("stopped");
            if (asset.Device is not { HealthProtocol: not null, HealthPort: not null } device) return Result("not-configured");
            var check = asset.HealthChecks.Single(item => item.Protocol == device.HealthProtocol && item.Port == device.HealthPort && item.Path == device.HealthPath);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var body = string.Empty;
            if (asset.Kind == "docker")
            {
                var pid = await docker.GetContainerPidAsync(actual.ResourceId, deadline.Token);
                if (pid <= 0) return Result("unavailable", "device.process_missing");
                if (check.Protocol == "tcp") await RunContainerHealthProbeAsync(pid, check, deadline.Token);
                else body = await ReadContainerHealthBodyAsync(pid, check, deadline.Token);
            }
            else body = await RunVmHealthProbeAsync(check, deadline.Token);
            var after = (await ReadInventoryAsync(plan, token)).SingleOrDefault(item => item.AssetKey == asset.AssetKey);
            if (after != actual) return Result("unavailable", "device.identity_changed");
            if (device.ProtocolEventTypes is not { Count: > 0 } types) return Result("healthy");
            return ParseProtocolCounters(body, types, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or SocketException or HttpRequestException or
            JsonException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return Result("unhealthy", "device.probe_failed");
        }
    }

    static async Task<string> RunVmHealthProbeAsync(TeamLabHealthCheckV2 check, CancellationToken token)
    {
        if (check.Protocol == "tcp")
        {
            using var client = new TcpClient();
            await client.ConnectAsync(check.Host, check.Port, token);
            return string.Empty;
        }
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var http = new HttpClient(handler);
        using var response = await http.GetAsync(new UriBuilder("http", check.Host, check.Port, check.Path ?? "/").Uri,
            HttpCompletionOption.ResponseHeadersRead, token);
        if ((int)response.StatusCode is < 200 or >= 400) throw new IOException("Device HTTP probe failed.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await ReadBoundedAsync(stream, token);
    }

    internal static TeamLabDeviceObservation ParseProtocolCounters(string body, IReadOnlyList<string> types, DateTimeOffset now)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (!root.TryGetProperty("bootId", out var boot) || !Guid.TryParse(boot.GetString(), out var bootId) ||
                !root.TryGetProperty("counters", out var counters) || counters.ValueKind != JsonValueKind.Object)
                return new("unhealthy", now, "device.telemetry_invalid");
            var values = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var type in types)
            {
                if (!counters.TryGetProperty(type, out var value) || !value.TryGetInt64(out var count) || count < 0)
                    return new("unhealthy", now, "device.telemetry_invalid");
                values.Add(type, count);
            }
            return new("healthy", now, BootId: bootId.ToString("D"), ProtocolCounters: values);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return new("unhealthy", now, "device.telemetry_invalid");
        }
    }

    static async Task<string> ReadContainerHealthBodyAsync(long pid, TeamLabHealthCheckV2 check, CancellationToken token)
    {
        using var process = new Process { StartInfo = new("nsenter")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        } };
        foreach (var value in new[] { "-t", pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "-n", "curl",
            "--silent", "--fail", "--noproxy", "*", "--max-time", "10", "--max-filesize", "8192",
            new UriBuilder("http", check.Host, check.Port, check.Path ?? "/").Uri.AbsoluteUri })
            process.StartInfo.ArgumentList.Add(value);
        process.Start();
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(token);
            var body = await ReadBoundedAsync(process.StandardOutput.BaseStream, token);
            await process.WaitForExitAsync(token);
            await stderr;
            if (process.ExitCode != 0) throw new IOException("Device HTTP probe failed.");
            return body;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[8193];
        var used = 0;
        while (used < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(used), token);
            if (count == 0) return Encoding.UTF8.GetString(buffer, 0, used);
            used += count;
        }
        throw new IOException("Device telemetry exceeds its size limit.");
    }
}
