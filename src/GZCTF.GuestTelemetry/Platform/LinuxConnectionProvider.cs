using System.Diagnostics;
using System.Globalization;
using System.Net;
using GZCTF.GuestTelemetry.Contracts;

namespace GZCTF.GuestTelemetry.Platform;

public sealed class LinuxConnectionProvider : IConnectionProvider
{
    private const int MaxConnections = 20_000;
    private const int MaxProcesses = 4_096;
    private const int MaxFileDescriptors = 100_000;

    public Task<IReadOnlyList<ConnectionSnapshot>> ReadAsync(CancellationToken cancellationToken)
    {
        var sockets = ReadSocketTables(cancellationToken);
        var processes = ResolveProcesses(sockets.Select(item => item.Inode).ToHashSet(), cancellationToken);
        IReadOnlyList<ConnectionSnapshot> result = sockets
            .Where(item => processes.ContainsKey(item.Inode))
            .Select(item => new ConnectionSnapshot(
                processes[item.Inode],
                new SensorEndpoint(item.LocalAddress, item.LocalPort, item.Protocol),
                new SensorEndpoint(item.RemoteAddress, item.RemotePort, item.Protocol)))
            .Take(MaxConnections)
            .ToArray();
        return Task.FromResult(result);
    }

    private static IReadOnlyList<LinuxSocket> ReadSocketTables(CancellationToken cancellationToken)
    {
        List<LinuxSocket> sockets = [];
        ReadTable("/proc/net/tcp", "TCP", sockets, cancellationToken);
        ReadTable("/proc/net/udp", "UDP", sockets, cancellationToken);
        return sockets;
    }

    private static void ReadTable(
        string path,
        string protocol,
        List<LinuxSocket> output,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Count >= MaxConnections) return;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10 || !TryEndpoint(fields[1], out var localAddress, out var localPort) ||
                !TryEndpoint(fields[2], out var remoteAddress, out var remotePort) ||
                !ulong.TryParse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture, out var inode))
                continue;
            output.Add(new LinuxSocket(
                protocol, localAddress, localPort, remoteAddress, remotePort, inode));
        }
    }

    private static Dictionary<ulong, SensorProcessIdentity> ResolveProcesses(
        IReadOnlySet<ulong> targetInodes,
        CancellationToken cancellationToken)
    {
        Dictionary<ulong, SensorProcessIdentity> output = [];
        if (targetInodes.Count == 0 || !Directory.Exists("/proc")) return output;
        var descriptorCount = 0;
        foreach (var processDirectory in Directory.EnumerateDirectories("/proc")
                     .Where(path => int.TryParse(Path.GetFileName(path), out _))
                     .Order(StringComparer.Ordinal)
                     .Take(MaxProcesses))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileName(processDirectory), out var processId)) continue;
            SensorProcessIdentity? identity = null;
            try
            {
                using var process = Process.GetProcessById(processId);
                identity = new SensorProcessIdentity(
                    processId,
                    process.ProcessName.Length <= 128 ? process.ProcessName : process.ProcessName[..128],
                    process.StartTime.ToUniversalTime());
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                continue;
            }
            var fdDirectory = Path.Combine(processDirectory, "fd");
            if (!Directory.Exists(fdDirectory)) continue;
            try
            {
                foreach (var descriptor in Directory.EnumerateFiles(fdDirectory))
                {
                    if (++descriptorCount > MaxFileDescriptors) return output;
                    var target = File.ResolveLinkTarget(descriptor, false)?.Name;
                    if (target is null || !TrySocketInode(target, out var inode) || !targetInodes.Contains(inode))
                        continue;
                    output.TryAdd(inode, identity);
                    if (output.Count == targetInodes.Count) return output;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                // Processes may exit or deny access while the snapshot is being built.
            }
        }
        return output;
    }

    internal static bool TryEndpoint(string value, out string address, out int port)
    {
        address = string.Empty;
        port = 0;
        var parts = value.Split(':', 2);
        if (parts.Length != 2 || parts[0].Length != 8 ||
            !uint.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rawAddress) ||
            !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out port))
            return false;
        address = new IPAddress(BitConverter.GetBytes(rawAddress)).ToString();
        return true;
    }

    private static bool TrySocketInode(string value, out ulong inode)
    {
        inode = 0;
        return value.StartsWith("socket:[", StringComparison.Ordinal) && value.EndsWith(']') &&
               ulong.TryParse(value.AsSpan(8, value.Length - 9), out inode);
    }

    private sealed record LinuxSocket(
        string Protocol,
        string LocalAddress,
        int LocalPort,
        string RemoteAddress,
        int RemotePort,
        ulong Inode);
}
