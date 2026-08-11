using System.Diagnostics;
using System.Text.Json;

namespace GZCTF.Agent.Services.Vm;

/// <summary>Why a cached VM template may not be deleted.</summary>
public sealed record VmImageBackingReference(string OverlayPath, string BackingPath);

/// <summary>
/// Answers whether a cached VM template is still the backing file of a running or stopped VM's
/// overlay. The reference lives only in qcow2 metadata, so the disk chain is the only ground truth;
/// deleting a backing file makes every overlay built on it permanently unusable, across games.
/// </summary>
public sealed class VmImageBackingChainInspector(ILogger<VmImageBackingChainInspector> logger)
{
    static readonly TimeSpan InspectTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Returns the overlays backed by any of <paramref name="candidatePaths" />.
    /// Throws when the chain cannot be established: refusing an irreversible delete on incomplete
    /// information is the only safe outcome.
    /// </summary>
    public async Task<IReadOnlyList<VmImageBackingReference>> FindReferencesAsync(
        IReadOnlyCollection<string> searchRoots,
        IReadOnlyCollection<string> candidatePaths,
        CancellationToken token)
    {
        if (candidatePaths.Count == 0)
            return [];

        var candidates = candidatePaths
            .Select(path => Path.GetFullPath(path))
            .ToHashSet(StringComparer.Ordinal);
        var references = new List<VmImageBackingReference>();

        foreach (var root in searchRoots.Where(Directory.Exists).Distinct(StringComparer.Ordinal))
        foreach (var overlay in Directory.EnumerateFiles(root, "*.qcow2", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var overlayPath = Path.GetFullPath(overlay);
            if (candidates.Contains(overlayPath))
                continue;

            var backing = await ReadBackingFileAsync(overlayPath, token);
            if (backing is null)
                continue;
            var resolved = Path.GetFullPath(Path.IsPathRooted(backing)
                ? backing
                : Path.Combine(Path.GetDirectoryName(overlayPath)!, backing));
            if (candidates.Contains(resolved))
                references.Add(new VmImageBackingReference(overlayPath, resolved));
        }

        return references;
    }

    async Task<string?> ReadBackingFileAsync(string overlayPath, CancellationToken token)
    {
        var info = new ProcessStartInfo
        {
            FileName = "qemu-img",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("info");
        info.ArgumentList.Add("--output=json");
        info.ArgumentList.Add(overlayPath);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(InspectTimeout);
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException(
                                "qemu-img is unavailable, so VM image references cannot be verified.");
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // Exited between the check and the kill request.
            }

            throw new InvalidOperationException(
                $"Timed out inspecting the backing chain of '{overlayPath}'.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Unable to inspect '{overlayPath}': {(await stderr).Trim()}");

        return ParseBackingFile(await stdout, logger, overlayPath);
    }

    internal static string? ParseBackingFile(string json, ILogger? logger = null, string? overlayPath = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            // full-backing-filename is already resolved against the overlay's directory; prefer it.
            foreach (var property in new[] { "full-backing-filename", "backing-filename" })
            {
                if (root.TryGetProperty(property, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString();
            }

            return null;
        }
        catch (JsonException exception)
        {
            logger?.LogWarning(exception, "Unreadable qemu-img output for {Overlay}", overlayPath);
            throw new InvalidOperationException(
                $"Unable to parse the backing chain of '{overlayPath}'.", exception);
        }
    }
}
