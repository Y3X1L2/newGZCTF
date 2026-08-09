using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.GuestControl.Contracts;

namespace GZCTF.GuestSupervisor.Lifecycle;

internal static class GuestPackageContract
{
    private const int MaxArchiveEntries = 1024;
    private const long MaxExpandedBytes = 256L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static GuestPackageManifest VerifyManifest(GuestServicePackageDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.ManifestJson) || string.IsNullOrWhiteSpace(descriptor.SigningPublicKeyPem))
            throw new InvalidDataException("guest_bootstrap_manifest_missing");
        byte[] signature;
        try { signature = Convert.FromBase64String(descriptor.ManifestSignature); }
        catch (FormatException) { throw new InvalidDataException("guest_bootstrap_signature_invalid"); }
        using var key = ECDsa.Create();
        try { key.ImportFromPem(descriptor.SigningPublicKeyPem); }
        catch (Exception exception) { throw new InvalidDataException("guest_bootstrap_signing_key_invalid", exception); }
        if (!key.VerifyData(Encoding.UTF8.GetBytes(descriptor.ManifestJson), signature, HashAlgorithmName.SHA256))
            throw new InvalidDataException("guest_bootstrap_signature_invalid");
        var manifest = JsonSerializer.Deserialize<GuestPackageManifest>(descriptor.ManifestJson, JsonOptions)
                       ?? throw new InvalidDataException("guest_bootstrap_manifest_invalid");
        if (manifest.OperatingSystems is null || manifest.Parameters is null || manifest.Files is null ||
            manifest.Steps is null || manifest.HealthChecks is null ||
            manifest.SchemaVersion != 1 || manifest.MaxReboots is < 0 or > 8 || manifest.Steps.Length == 0 ||
            manifest.Steps.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != manifest.Steps.Length ||
            manifest.OperatingSystems.All(item => !string.Equals(item,
                OperatingSystem.IsWindows() ? "Windows" : "Linux", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("guest_bootstrap_manifest_invalid");
        if (manifest.Files.Length > MaxArchiveEntries || manifest.Steps.Length > 64 || manifest.HealthChecks.Length > 64)
            throw new InvalidDataException("guest_bootstrap_manifest_bounds_exceeded");
        foreach (var file in manifest.Files)
        {
            ValidateRelativePath(file.SourcePath);
            ValidateGuestPath(file.TargetPath);
        }
        foreach (var step in manifest.Steps)
        {
            ValidateRelativePath(step.Entrypoint);
            if (!string.Equals(step.RunAs, "system", StringComparison.OrdinalIgnoreCase) ||
                step.TimeoutSeconds is < 1 or > 3600 ||
                step.Reboot is not ("None" or "IfRequested" or "Required"))
                throw new InvalidDataException("guest_bootstrap_step_invalid");
        }
        return manifest;
    }

    public static void ValidateValues(
        GuestPackageManifest manifest,
        IReadOnlyDictionary<string, string> values)
    {
        var declared = manifest.Parameters.ToDictionary(item => item.Key, StringComparer.Ordinal);
        if (values.Keys.Any(item => !declared.ContainsKey(item)))
            throw new InvalidDataException("guest_bootstrap_parameter_not_declared");
        foreach (var parameter in manifest.Parameters)
        {
            if (!values.TryGetValue(parameter.Key, out var value))
            {
                if (parameter.Required && parameter.DefaultValue is null)
                    throw new InvalidDataException($"guest_bootstrap_parameter_missing:{parameter.Key}");
                continue;
            }
            var validType = string.Equals(parameter.Type, "String", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(parameter.Type, "Integer", StringComparison.OrdinalIgnoreCase) && long.TryParse(value, out _) ||
                            string.Equals(parameter.Type, "Boolean", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out _);
            if (!validType) throw new InvalidDataException($"guest_bootstrap_parameter_invalid:{parameter.Key}");
        }
    }

    public static async Task ExtractArchiveAsync(
        string archive,
        string root,
        CancellationToken cancellationToken)
    {
        var entries = 0;
        long expanded = 0;
        await using var input = File.OpenRead(archive);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entries > MaxArchiveEntries ||
                entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.Directory))
                throw new InvalidDataException("guest_bootstrap_archive_invalid");
            var relative = NormalizeArchiveEntryName(entry.Name, entry.EntryType);
            if (relative is null)
                continue;
            var target = SafeCombine(root, relative);
            if (entry.EntryType == TarEntryType.Directory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            if (entry.DataStream is null) throw new InvalidDataException("guest_bootstrap_archive_entry_empty");
            await entry.DataStream.CopyToAsync(output, cancellationToken);
            expanded += output.Length;
            if (expanded > MaxExpandedBytes) throw new InvalidDataException("guest_bootstrap_archive_too_large");
        }
    }

    public static string NormalizeDigest(string digest)
    {
        var value = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : digest;
        if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("guest_artifact_digest_invalid");
        return value.ToLowerInvariant();
    }

    public static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root,
            NormalizeRelativePath(relative).Replace('/', Path.DirectorySeparatorChar)));
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : throw new InvalidDataException("guest_bootstrap_path_escape");
    }

    public static UnixFileMode ParseUnixMode(string mode)
    {
        if (mode.Length != 4 || mode[0] != '0' || mode.Skip(1).Any(item => item is < '0' or > '7'))
            throw new InvalidDataException("guest_bootstrap_file_mode_invalid");
        return (UnixFileMode)Convert.ToInt32(mode, 8);
    }

    private static string NormalizeRelativePath(string path)
    {
        ValidateRelativePath(path);
        return path.Replace('\\', '/');
    }

    // POSIX tar producers commonly prefix archive entries with "./" and emit
    // a root-directory entry.  This is archive syntax, not a user-supplied path.
    private static string? NormalizeArchiveEntryName(string name, TarEntryType entryType)
    {
        var value = name.Replace('\\', '/').TrimEnd('/');
        if (value == ".")
        {
            if (entryType == TarEntryType.Directory)
                return null;
            throw new InvalidDataException("guest_bootstrap_relative_path_invalid");
        }
        while (value.StartsWith("./", StringComparison.Ordinal))
            value = value[2..];
        if (value.Length == 0)
        {
            if (entryType == TarEntryType.Directory)
                return null;
            throw new InvalidDataException("guest_bootstrap_relative_path_invalid");
        }
        return NormalizeRelativePath(value);
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
            path.Split(['/', '\\'], StringSplitOptions.None).Any(item => item is "" or "." or ".."))
            throw new InvalidDataException("guest_bootstrap_relative_path_invalid");
    }

    private static void ValidateGuestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.Contains("..", StringComparison.Ordinal) || path.Contains("guest-supervisor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("guest_bootstrap_target_path_invalid");
    }
}

internal sealed record GuestPackageManifest(
    int SchemaVersion,
    string[] OperatingSystems,
    string[] AssetKinds,
    string[] RequiredTemplateCapabilities,
    GuestPackageParameter[] Parameters,
    GuestPackageFile[] Files,
    GuestPackageStep[] Steps,
    GuestPackageHealthCheck[] HealthChecks,
    int MaxReboots);

internal sealed record GuestPackageParameter(string Key, string Type, bool Required, bool Secret, string? DefaultValue);
internal sealed record GuestPackageFile(string SourcePath, string TargetPath, string Mode, bool Template);
internal sealed record GuestPackageStep(string Id, string Entrypoint, int TimeoutSeconds, string RunAs, string Reboot);
internal sealed record GuestPackageHealthCheck(string Id, string Kind, string Target, int TimeoutSeconds, int Attempts);
