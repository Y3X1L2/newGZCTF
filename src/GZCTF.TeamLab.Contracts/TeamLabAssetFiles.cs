namespace GZCTF.TeamLab.Contracts;

public sealed record TeamLabFileEntry(string Name, string Kind, long Size);
public sealed record TeamLabFileResult(IReadOnlyList<TeamLabFileEntry>? Entries = null, byte[]? Content = null, string? HostKeySha256 = null);
public sealed record TeamLabVmFileRequest(string DomainName, int Generation, Guid NativeId, string GuestAddress,
    int Port, string Username, string Credential, string Operation, string Path, byte[]? Content = null,
    bool Overwrite = false, string? HostKeySha256 = null)
{
    public override string ToString() => "VM SFTP request (credentials omitted)";
}
public sealed record TeamLabContainerFileRequest(
    int RuntimeId, int Generation, string ContainerId, string Operation, string Path,
    byte[]? Content = null, bool Overwrite = false);

public static class TeamLabFileLimits
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public const int MaxEntries = 1000;
    public const string StagingDirectory = ".gzctf-upload-staging";

    public static bool IsValidPath(string? path) => path is { Length: > 0 and <= 1024 } &&
        path.StartsWith('/') && !path.Contains('\0') && !path.Contains('\\') &&
        !path.Split('/').Any(part => part is "." or ".." or StagingDirectory);
}
