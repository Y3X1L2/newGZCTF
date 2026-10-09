using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Application;
using Microsoft.Extensions.Options;
using Microsoft.Win32.SafeHandles;

namespace GZCTF.Modules.Content.Infrastructure;

public interface IVmSourceBackingInspector
{
    Task EnsureUnusedAsync(string sourcePath, string quarantinePath, CancellationToken token);
}

/// <summary>Only removes the captured legacy source, never a numeric Agent cache or directory.</summary>
public sealed class VmSourceMigrationFiles(IOptions<KvmSettings> options, IVmSourceBackingInspector backing)
{
    readonly string _root = Path.GetFullPath(options.Value.ImageStoragePath);
    static readonly Regex NumericCache = new("^[0-9]+$", RegexOptions.CultureInvariant);

    public (string Path, string Identity) Capture(string path)
    {
        var full = ValidatePath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("Legacy source file is unavailable.");
        return (full, Identity(full));
    }

    public async Task VerifyAsync(VmSourceMigrationReference source, string sha256, long size,
        CancellationToken token)
    {
        var full = ValidatePath(source.Path);
        EnsureIdentity(full, source.FileIdentity, allowRename: false);
        await VerifyContentAsync(full, sha256, size, token);
        EnsureIdentity(full, source.FileIdentity, allowRename: false);
    }

    public async Task DeleteAsync(Guid operationId, VmSourceMigrationReference source, string sha256, long size,
        CancellationToken token)
    {
        var path = ValidatePath(source.Path);
        var cleanupId = source.CleanupId ?? operationId;
        var quarantine = Path.Combine(Path.GetDirectoryName(path)!, $".source-migration-{cleanupId:N}.pending-delete");
        await backing.EnsureUnusedAsync(path, quarantine, token);
        if (!File.Exists(quarantine))
        {
            if (!File.Exists(path)) return;
            await VerifyAsync(source, sha256, size, token);
            // Atomic rename prevents deleting a different file inserted at the original name.
            // The deterministic name is recoverable from the existing operation/job after restart.
            File.Move(path, quarantine, overwrite: false);
        }
        try
        {
            if ((File.GetAttributes(quarantine) & FileAttributes.ReparsePoint) != 0)
                throw Rejected("The source cleanup checkpoint is a symbolic link.");
            EnsureIdentity(quarantine, source.FileIdentity, allowRename: true);
            await VerifyContentAsync(quarantine, sha256, size, token);
            await backing.EnsureUnusedAsync(path, quarantine, token);
            EnsureIdentity(quarantine, source.FileIdentity, allowRename: true);
            File.Delete(quarantine);
        }
        catch
        {
            // Preserve both files if a replacement appeared. Never overwrite it during recovery.
            if (!File.Exists(path) && File.Exists(quarantine)) File.Move(quarantine, path, overwrite: false);
            throw;
        }
    }

    string ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw Rejected("Legacy source path must be absolute.");
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison) ||
            !string.Equals(Path.GetExtension(full), ".qcow2", StringComparison.OrdinalIgnoreCase) ||
            NumericCache.IsMatch(Path.GetFileNameWithoutExtension(full)))
            throw Rejected("Only a nonnumeric qcow2 legacy source inside the configured image directory may be migrated.");
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw Rejected("Legacy source symbolic links are not accepted.");
        if (OperatingSystem.IsLinux())
        {
            var actual = File.Exists(full) ? RealPath(full) : RealPath(Path.GetDirectoryName(full)!);
            var root = RealPath(_root).TrimEnd('/');
            if (!actual.StartsWith(root + "/", StringComparison.Ordinal))
                throw Rejected("Legacy source resolves outside the configured image directory.");
        }
        else
        {
            var directory = Path.GetDirectoryName(full);
            while (directory is not null && directory.Length >= _root.Length)
            {
                if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw Rejected("Legacy source directory reparse points are not accepted on this host.");
                if (string.Equals(directory, _root, comparison)) break;
                directory = Path.GetDirectoryName(directory);
            }
        }
        return full;
    }

    static async Task VerifyContentAsync(string path, string sha256, long size, CancellationToken token)
    {
        if (new FileInfo(path).Length != size) throw Rejected("Legacy source size differs from its captured identity.");
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
        if (!string.Equals(hash, OciArtifactRegistryClient.NormalizeDigest(sha256), StringComparison.Ordinal))
            throw Rejected("Legacy source SHA-256 differs from its captured identity.");
    }

    static void EnsureIdentity(string path, string expected, bool allowRename)
    {
        var actual = Identity(path);
        // rename/recovery must not invalidate the stable device/inode/size/mtime identity.
        if (allowRename && expected.StartsWith("linux:", StringComparison.Ordinal))
        {
            actual = string.Join(':', actual.Split(':').Take(7));
            expected = string.Join(':', expected.Split(':').Take(7));
        }
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw Rejected("Legacy source file was replaced or modified after capture.");
    }

    static string Identity(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            if (StatX(-100, path, 0x100, 0x7ff, out var facts) != 0 || (facts.Mode & 0xf000) != 0x8000)
                throw Rejected("Legacy source must be an observable regular file.");
            return $"linux:{facts.DeviceMajor:x}:{facts.DeviceMinor:x}:{facts.Inode:x}:{facts.Size:x}:" +
                   $"{facts.ModifiedSeconds:x}:{facts.ModifiedNanoseconds:x}";
        }
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!GetFileInformationByHandle(handle, out var info)) throw Rejected("Legacy file identity is unavailable.");
        return $"windows:{info.VolumeSerial:x}:{info.FileIndexHigh:x}:{info.FileIndexLow:x}:" +
               $"{info.SizeHigh:x}:{info.SizeLow:x}:{info.WriteHigh:x}:{info.WriteLow:x}";
    }

    internal static bool SameEntry(string left, string right)
    {
        left = Path.GetFullPath(left);
        right = Path.GetFullPath(right);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(left, right, comparison)) return true;
        if (File.Exists(left) && File.Exists(right) && string.Equals(Identity(left), Identity(right), StringComparison.Ordinal))
            return true;
        // A bind mount has a different path but the same parent directory entry. This also
        // protects a missing original pathname after a crash between quarantine and deletion.
        if (OperatingSystem.IsLinux() && string.Equals(Path.GetFileName(left), Path.GetFileName(right), StringComparison.Ordinal) &&
            StatX(-100, Path.GetDirectoryName(left)!, 0, 0x7ff, out var a) == 0 &&
            StatX(-100, Path.GetDirectoryName(right)!, 0, 0x7ff, out var b) == 0 &&
            a.DeviceMajor == b.DeviceMajor && a.DeviceMinor == b.DeviceMinor && a.Inode == b.Inode)
            return true;
        var link = File.ResolveLinkTarget(right, returnFinalTarget: false);
        return link is not null && string.Equals(left, Path.GetFullPath(link.FullName), comparison);
    }

    internal static string DirectoryIdentity(string path)
    {
        if (OperatingSystem.IsLinux() && StatX(-100, path, 0, 0x7ff, out var facts) == 0)
            return $"linux-dir:{facts.DeviceMajor:x}:{facts.DeviceMinor:x}:{facts.Inode:x}";
        return Path.GetFullPath(path);
    }

    static string RealPath(string path)
    {
        var pointer = ResolveRealPath(path, IntPtr.Zero);
        if (pointer == IntPtr.Zero) throw Rejected("Legacy source directory cannot be resolved.");
        try { return Marshal.PtrToStringUTF8(pointer)!; }
        finally { Free(pointer); }
    }

    static ApiOperationTerminalException Rejected(string message) => new("source_migration_file_identity_invalid", message);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    static extern int StatX(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out StatXFacts facts);
    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    static extern IntPtr ResolveRealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);
    [DllImport("libc", EntryPoint = "free")]
    static extern void Free(IntPtr pointer);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out WindowsFileFacts facts);

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    struct StatXFacts
    {
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(96)] public long ChangedSeconds;
        [FieldOffset(104)] public uint ChangedNanoseconds;
        [FieldOffset(112)] public long ModifiedSeconds;
        [FieldOffset(120)] public uint ModifiedNanoseconds;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }
    [StructLayout(LayoutKind.Explicit, Size = 52)]
    struct WindowsFileFacts
    {
        [FieldOffset(20)] public uint WriteLow;
        [FieldOffset(24)] public uint WriteHigh;
        [FieldOffset(28)] public uint VolumeSerial;
        [FieldOffset(32)] public uint SizeHigh;
        [FieldOffset(36)] public uint SizeLow;
        [FieldOffset(44)] public uint FileIndexHigh;
        [FieldOffset(48)] public uint FileIndexLow;
    }
}
