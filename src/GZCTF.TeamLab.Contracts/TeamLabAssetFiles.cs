using System.Buffers;

namespace GZCTF.TeamLab.Contracts;

public sealed record TeamLabFileEntry(string Name, string Kind, long Size);
public sealed record TeamLabHostInterface(string Name, string MacAddress, bool LinkUp, IReadOnlyList<string> Addresses);
public sealed record TeamLabServiceForwardRequest(
    Guid AccessId, int RuntimeId, int Generation, string Protocol, int ListenPort,
    string TargetAddress, int TargetPort);
public sealed record TeamLabFileResult(IReadOnlyList<TeamLabFileEntry>? Entries = null, byte[]? Content = null, string? HostKeySha256 = null);
public sealed record TeamLabVmFileRequest(string DomainName, int Generation, Guid NativeId, string GuestAddress,
    int Port, string Username, string Credential, string Operation, string Path, byte[]? Content = null,
    bool Overwrite = false, string? HostKeySha256 = null, string? DestinationPath = null, bool Recursive = false)
{
    public override string ToString() => "VM SFTP request (credentials omitted)";
}
public sealed record TeamLabContainerFileRequest(
    int RuntimeId, int Generation, string ContainerId, string Operation, string Path,
    byte[]? Content = null, bool Overwrite = false, string? DestinationPath = null, bool Recursive = false);

public static class TeamLabFileLimits
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public const long DefaultMaxTransferBytes = 1024L * 1024 * 1024;
    public const int MaxEntries = 1000;
    public const string StagingDirectory = ".gzctf-upload-staging";

    public static bool IsValidPath(string? path) => path is { Length: > 0 and <= 1024 } &&
        path.StartsWith('/') && !path.Contains('\0') && !path.Contains('\\') &&
        !path.Split('/').Any(part => part is "." or ".." or StagingDirectory);

    public static async Task CopyAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        TimeSpan idleTimeout,
        CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long total = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token)
                    .AsTask().WaitAsync(idleTimeout, token);
                if (read == 0) return;
                total += read;
                if (total > maxBytes) throw new IOException("File exceeds the transfer limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read), token)
                    .AsTask().WaitAsync(idleTimeout, token);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
