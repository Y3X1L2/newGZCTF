using System.Formats.Tar;

namespace GZCTF.Agent.Services.Vm;

/// <summary>
/// One file to materialize inside a guest. <paramref name="GuestPath" /> is interpreted
/// relative to the extraction root chosen by the caller.
/// </summary>
internal readonly record struct GuestFileEntry(string GuestPath, byte[] Content, string Mode);

/// <summary>
/// Packs guest files into a single archive so one transfer replaces per-file QGA round trips.
/// Every QGA call costs a full <c>virsh</c> process spawn, and the per-file sequence
/// (mkdir, open, write, flush, close, chmod) costs seven of them before any payload moves,
/// which dominates bootstrap time for profiles made of many small scripts.
/// Modes travel inside the archive so extraction applies them atomically; a follow-up chmod
/// would leave secrets readable between creation and permission fixup.
/// </summary>
internal static class GuestFileBatch
{
    /// <summary>Fixed timestamp keeps archives byte-identical for identical input.</summary>
    static readonly DateTimeOffset StableTimestamp = DateTimeOffset.UnixEpoch;

    const UnixFileMode FallbackMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Collapses duplicate guest paths keeping the last write, preserving the
    /// overwrite semantics of sequential per-file writes.
    /// </summary>
    internal static IReadOnlyList<GuestFileEntry> Deduplicate(IReadOnlyList<GuestFileEntry> entries)
    {
        var ordered = new Dictionary<string, GuestFileEntry>(StringComparer.Ordinal);
        var sequence = new List<string>(entries.Count);
        foreach (var entry in entries)
        {
            var key = Normalize(entry.GuestPath);
            if (!ordered.ContainsKey(key))
                sequence.Add(key);
            ordered[key] = entry with { GuestPath = key };
        }

        return sequence.Select(key => ordered[key]).ToArray();
    }

    /// <summary>
    /// Builds an uncompressed tar carrying each entry's content and mode.
    /// Entries are ordered by path so the archive is deterministic.
    /// </summary>
    internal static byte[] BuildTarArchive(IReadOnlyList<GuestFileEntry> entries)
    {
        using var buffer = new MemoryStream();
        using (var writer = new TarWriter(buffer, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var entry in entries.OrderBy(item => Normalize(item.GuestPath), StringComparer.Ordinal))
            {
                var tarEntry = new PaxTarEntry(TarEntryType.RegularFile, Normalize(entry.GuestPath))
                {
                    Mode = ParseMode(entry.Mode),
                    ModificationTime = StableTimestamp,
                    DataStream = new MemoryStream(entry.Content, writable: false)
                };
                writer.WriteEntry(tarEntry);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Converts an octal permission string such as <c>0600</c> into a file mode.
    /// The POSIX permission bits line up with <see cref="UnixFileMode" /> numerically.
    /// </summary>
    internal static UnixFileMode ParseMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return FallbackMode;
        try
        {
            return (UnixFileMode)(Convert.ToInt32(mode.Trim(), 8) & 0xFFF);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            return FallbackMode;
        }
    }

    static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
