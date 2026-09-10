using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using GZCTF.TeamLab.Contracts;

namespace GZCTF.Agent.Services.TeamLab;

// All path resolution is relative to an already-open container root. openat2 rejects
// mount crossings and links atomically; no realpath/check-then-open sequence is used.
internal sealed class LinuxContainerFileStore : IDisposable
{
    const int DirectoryFlag = 0x10000;
    const int CloseOnExec = 0x80000;
    const int PathFlag = 0x200000;
    const int NoFollow = 0x20000;
    const int DirectoryMode = 0x4000;
    const int RegularMode = 0x8000;
    readonly SafeFileHandle root;

    public static LinuxContainerFileStore ForProcess(long pid, string containerId)
    {
        using var process = Handle(Open($"/proc/{pid}", PathFlag | DirectoryFlag | CloseOnExec));
        var processPath = $"/proc/self/fd/{process.DangerousGetHandle()}";
        if (!File.ReadAllText($"{processPath}/cgroup").Contains(containerId, StringComparison.Ordinal))
            throw new IOException("The Agent cannot verify the container process namespace.");
        return new LinuxContainerFileStore($"{processPath}/root");
    }

    public LinuxContainerFileStore(string rootPath)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
            throw new IOException("Container file operations require Linux openat2 support.");
        root = Handle(Open(rootPath, PathFlag | DirectoryFlag | CloseOnExec));
    }

    public async Task<TeamLabFileResult> ExecuteAsync(TeamLabContainerFileRequest request, CancellationToken token)
    {
        if (!TeamLabFileLimits.IsValidPath(request.Path)) throw new IOException("Invalid container path.");
        token.ThrowIfCancellationRequested();
        if (request.Operation == "list")
        {
            using var directory = Resolve(root, request.Path, DirectoryFlag);
            CleanupStaging(directory, token);
            var entries = new List<TeamLabFileEntry>();
            foreach (var entry in Directory.EnumerateFileSystemEntries($"/proc/self/fd/{directory.DangerousGetHandle()}"))
            {
                token.ThrowIfCancellationRequested();
                if (entries.Count >= TeamLabFileLimits.MaxEntries)
                    throw new IOException("Directory exceeds the entry limit; choose a smaller directory.");
                var name = Path.GetFileName(entry);
                if (name == TeamLabFileLimits.StagingDirectory) continue;
                try
                {
                    using var child = Resolve(directory, name, PathFlag | NoFollow);
                    var stat = Stat(child);
                    entries.Add(new(name, (stat.Mode & 0xf000) switch
                    {
                        DirectoryMode => "directory", RegularMode => "file", _ => "restricted"
                    }, checked((long)stat.Size)));
                }
                catch (IOException) { entries.Add(new(name, "restricted", 0)); }
            }
            return new(entries.OrderBy(item => item.Kind != "directory").ThenBy(item => item.Name, StringComparer.Ordinal).ToArray());
        }
        if (request.Operation == "download")
        {
            using var file = Resolve(root, request.Path, PathFlag);
            var stat = Stat(file);
            if ((stat.Mode & 0xf000) != RegularMode || stat.Size > TeamLabFileLimits.MaxBytes)
                throw new IOException("Only bounded regular files can be downloaded.");
            using var stream = File.OpenRead($"/proc/self/fd/{file.DangerousGetHandle()}");
            using var result = new MemoryStream();
            var buffer = new byte[64 * 1024];
            int count;
            while ((count = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (result.Length + count > TeamLabFileLimits.MaxBytes) throw new IOException("File exceeds the transfer limit.");
                await result.WriteAsync(buffer.AsMemory(0, count), token);
            }
            return new(Content: result.ToArray());
        }
        var normalized = request.Path.TrimEnd('/');
        if (normalized.Length == 0) throw new IOException("The container root cannot be modified.");
        var separator = normalized.LastIndexOf('/');
        using var parent = Resolve(root, separator == 0 ? "/" : normalized[..separator], DirectoryFlag);
        var basename = normalized[(separator + 1)..];
        if (request.Operation == "delete")
        {
            using var target = Resolve(parent, basename, PathFlag | NoFollow);
            var stat = Stat(target);
            if ((stat.Mode & 0xf000) is not (DirectoryMode or RegularMode))
                throw new IOException("Special files and links cannot be deleted here.");
            Check(UnlinkAt(parent, basename, (stat.Mode & 0xf000) == DirectoryMode ? 0x200 : 0));
            return new();
        }
        if (request.Operation != "upload" || request.Content is not { Length: <= TeamLabFileLimits.MaxBytes } content)
            throw new IOException("Invalid file operation or transfer size.");
        Statx? previous = null;
        if (request.Overwrite)
        {
            try
            {
                using var destination = Resolve(parent, basename, PathFlag | NoFollow);
                previous = Stat(destination);
                if ((previous.Value.Mode & 0xf000) != RegularMode)
                    throw new IOException("Only existing regular files can be overwritten.");
            }
            catch (NativeFileException error) when (error.ErrorNumber == 2) { }
        }
        if (MakeDirectoryAt(parent, TeamLabFileLimits.StagingDirectory, 0x1c0) < 0 && Marshal.GetLastPInvokeError() != 17) throw Error();
        CleanupStaging(parent, token);
        using var staging = Resolve(parent, TeamLabFileLimits.StagingDirectory, DirectoryFlag);
        var temporary = Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(Resolve(staging, temporary, 1 | 0x40 | 0x80, 0x180), FileAccess.Write))
            {
                await output.WriteAsync(content, token);
                if (previous is { } original)
                {
                    Check(ChangeOwner(output.SafeFileHandle, original.Uid, original.Gid));
                    Check(ChangeMode(output.SafeFileHandle, (uint)(original.Mode & 0x1ff)));
                }
                else Check(ChangeMode(output.SafeFileHandle, 0x1a4)); // 0644; never inherit setuid/setgid bits.
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            // rename replaces the directory entry, never follows an existing destination link.
            Check(RenameAt2(staging, temporary, parent, basename, request.Overwrite ? 0u : 1u));
            return new();
        }
        finally
        {
            UnlinkAt(staging, temporary, 0);
            UnlinkAt(parent, TeamLabFileLimits.StagingDirectory, 0x200);
        }
    }

    static void CleanupStaging(SafeFileHandle parent, CancellationToken token)
    {
        SafeFileHandle staging;
        try { staging = Resolve(parent, TeamLabFileLimits.StagingDirectory, DirectoryFlag); }
        catch (IOException) { return; }
        using (staging)
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
            foreach (var entry in Directory.EnumerateFileSystemEntries($"/proc/self/fd/{staging.DangerousGetHandle()}").Take(TeamLabFileLimits.MaxEntries))
            {
                token.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (!Guid.TryParseExact(name, "N", out _)) continue;
                using var file = Resolve(staging, name, PathFlag | NoFollow);
                var stat = Stat(file);
                if ((stat.Mode & 0xf000) == RegularMode && stat.ModifiedSeconds < cutoff) Check(UnlinkAt(staging, name, 0));
            }
        }
    }

    static SafeFileHandle Resolve(SafeFileHandle directory, string path, int flags, ulong mode = 0)
    {
        var how = new OpenHow { Flags = (ulong)(flags | CloseOnExec), Mode = mode, Resolve = 0x01 | 0x02 | 0x04 | 0x10 };
        return Handle(Syscall(437, directory, path, ref how, (nuint)Marshal.SizeOf<OpenHow>()));
    }

    static SafeFileHandle Handle(long fd)
    {
        if (fd < 0) throw Error();
        return new SafeFileHandle((nint)fd, ownsHandle: true);
    }
    static Statx Stat(SafeFileHandle handle)
    {
        Check(StatxCall(handle, "", 0x1000, 0x7ff, out var result));
        return result;
    }
    static void Check(int result) { if (result < 0) throw Error(); }
    static IOException Error() => new NativeFileException(Marshal.GetLastPInvokeError());
    internal sealed class NativeFileException(int errorNumber) : IOException($"Container file operation failed (errno {errorNumber}).")
    {
        public int ErrorNumber { get; } = errorNumber;
    }
    public void Dispose() => root.Dispose();

    [StructLayout(LayoutKind.Sequential)] struct OpenHow { public ulong Flags, Mode, Resolve; }
    [StructLayout(LayoutKind.Explicit, Size = 256)] struct Statx
    {
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(24)] public uint Gid;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(112)] public long ModifiedSeconds;
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "fchown", SetLastError = true)] static extern int ChangeOwner(SafeFileHandle fd, uint uid, uint gid);
    [DllImport("libc", EntryPoint = "fchmod", SetLastError = true)] static extern int ChangeMode(SafeFileHandle fd, uint mode);
    [DllImport("libc", EntryPoint = "syscall", SetLastError = true)] static extern long Syscall(long number, SafeFileHandle fd, string path, ref OpenHow how, nuint size);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] static extern int StatxCall(SafeFileHandle fd, string path, int flags, uint mask, out Statx result);
    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)] static extern int UnlinkAt(SafeFileHandle fd, string path, int flags);
    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)] static extern int MakeDirectoryAt(SafeFileHandle fd, string path, uint mode);
    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)] static extern int RenameAt2(SafeFileHandle oldFd, string oldPath, SafeFileHandle newFd, string newPath, uint flags);
}
