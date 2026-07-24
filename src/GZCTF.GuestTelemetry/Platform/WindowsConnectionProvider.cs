using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using GZCTF.GuestTelemetry.Contracts;

namespace GZCTF.GuestTelemetry.Platform;

public sealed class WindowsConnectionProvider : IConnectionProvider
{
    private const int AfInet = 2;
    private const uint ErrorInsufficientBuffer = 122;

    public Task<IReadOnlyList<ConnectionSnapshot>> ReadAsync(CancellationToken cancellationToken)
    {
        List<ConnectionSnapshot> output = [];
        ReadTcp(output, cancellationToken);
        ReadUdp(output, cancellationToken);
        return Task.FromResult<IReadOnlyList<ConnectionSnapshot>>(output);
    }

    private static void ReadTcp(List<ConnectionSnapshot> output, CancellationToken cancellationToken)
    {
        var buffer = ReadTable((IntPtr pointer, ref int size) =>
            GetExtendedTcpTable(pointer, ref size, true, AfInet, TcpTableClass.OwnerPidAll, 0));
        if (buffer == IntPtr.Zero) return;
        try
        {
            var count = Marshal.ReadInt32(buffer);
            var offset = sizeof(uint);
            var size = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(buffer + offset + index * size);
                if (!TryProcess((int)row.OwningPid, out var process)) continue;
                output.Add(new ConnectionSnapshot(
                    process,
                    new SensorEndpoint(Address(row.LocalAddress), Port(row.LocalPort), "TCP"),
                    new SensorEndpoint(Address(row.RemoteAddress), Port(row.RemotePort), "TCP")));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ReadUdp(List<ConnectionSnapshot> output, CancellationToken cancellationToken)
    {
        var buffer = ReadTable((IntPtr pointer, ref int size) =>
            GetExtendedUdpTable(pointer, ref size, true, AfInet, UdpTableClass.OwnerPid, 0));
        if (buffer == IntPtr.Zero) return;
        try
        {
            var count = Marshal.ReadInt32(buffer);
            var offset = sizeof(uint);
            var size = Marshal.SizeOf<MibUdpRowOwnerPid>();
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(buffer + offset + index * size);
                if (!TryProcess((int)row.OwningPid, out var process)) continue;
                output.Add(new ConnectionSnapshot(
                    process,
                    new SensorEndpoint(Address(row.LocalAddress), Port(row.LocalPort), "UDP"),
                    new SensorEndpoint("0.0.0.0", null, "UDP")));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr ReadTable(TableReader read)
    {
        var size = 0;
        if (read(IntPtr.Zero, ref size) != ErrorInsufficientBuffer || size <= 0) return IntPtr.Zero;
        var buffer = Marshal.AllocHGlobal(size);
        if (read(buffer, ref size) == 0) return buffer;
        Marshal.FreeHGlobal(buffer);
        return IntPtr.Zero;
    }

    private static bool TryProcess(int processId, out SensorProcessIdentity identity)
    {
        identity = default!;
        try
        {
            using var process = Process.GetProcessById(processId);
            var name = process.ProcessName;
            identity = new SensorProcessIdentity(
                processId,
                name.Length <= 128 ? name : name[..128],
                process.StartTime.ToUniversalTime());
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string Address(uint value) => new IPAddress(value).ToString();

    private static int Port(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes[..2]);
    }

    private delegate uint TableReader(IntPtr pointer, ref int size);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr table,
        ref int size,
        bool order,
        int addressFamily,
        TcpTableClass tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr table,
        ref int size,
        bool order,
        int addressFamily,
        UdpTableClass tableClass,
        uint reserved);

    private enum TcpTableClass
    {
        OwnerPidAll = 5
    }

    private enum UdpTableClass
    {
        OwnerPid = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint OwningPid;
    }
}
