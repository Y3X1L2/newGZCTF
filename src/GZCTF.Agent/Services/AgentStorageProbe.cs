using System.Runtime.InteropServices;

namespace GZCTF.Agent.Services;

internal sealed record AgentStorageSnapshot(string FileSystemId, long AvailableBytes);

internal static class AgentStorageProbe
{
    // statvfs follows symlinks and bind mounts. Its fsid, rather than the configured path or
    // mount point, makes image/runtime/Docker aliases share a single capacity account.
    internal static AgentStorageSnapshot Read(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new IOException("The storage directory must be absolute.");
        var directory = Path.GetFullPath(path);
        while (!Directory.Exists(directory))
            directory = Path.GetDirectoryName(directory)
                        ?? throw new IOException("The storage directory is unavailable.");

        if (OperatingSystem.IsLinux() && Environment.Is64BitProcess)
        {
            if (StatVfs(directory, out var facts) != 0)
                throw new IOException("The storage filesystem could not be measured.");
            var fragmentSize = facts.FragmentSize > 0 ? facts.FragmentSize : facts.BlockSize;
            var available = checked(facts.AvailableBlocks * fragmentSize);
            return new AgentStorageSnapshot($"linux:{facts.FileSystemId:x16}",
                available > long.MaxValue ? long.MaxValue : (long)available);
        }

        // Agent execution is Linux; this fallback permits native Windows tests/dev hosts.
        // It deliberately resolves the drive itself, not a per-directory capacity account.
        var root = Path.GetPathRoot(directory) ?? throw new IOException("The storage volume is unavailable.");
        return new AgentStorageSnapshot($"drive:{root.ToUpperInvariant()}",
            Math.Max(0, new DriveInfo(directory).AvailableFreeSpace));
    }

    [DllImport("libc", EntryPoint = "statvfs", SetLastError = true)]
    static extern int StatVfs([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out LinuxStatVfs facts);

    // Linux 64-bit glibc/musl statvfs ABI (112 bytes), shared by x86_64 and aarch64.
    [StructLayout(LayoutKind.Sequential, Size = 112)]
    struct LinuxStatVfs
    {
        public ulong BlockSize;
        public ulong FragmentSize;
        public ulong Blocks;
        public ulong FreeBlocks;
        public ulong AvailableBlocks;
        public ulong Files;
        public ulong FreeFiles;
        public ulong AvailableFiles;
        public ulong FileSystemId;
        public ulong Flags;
        public ulong NameMax;
    }
}
