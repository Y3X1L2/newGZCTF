using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models.Data;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed record PreparedImageConformancePackage(
    Guid ProfileId,
    int Version,
    string ManifestJson,
    string ManifestSignature,
    string SigningPublicKeyPem,
    string ArtifactDigest,
    byte[] Artifact);

public sealed class PreparedImageConformancePackageFactory
{
    private static readonly Guid ProfileId = new("a80e2a31-c720-5eb7-8b2c-f4c87f12ce24");

    public PreparedImageConformancePackage Create(OSType osType)
    {
        var windows = osType == OSType.Windows;
        var rebootName = windows ? "reboot.ps1" : "reboot.sh";
        var shutdownName = windows ? "shutdown.ps1" : "shutdown.sh";
        var manifest = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            operatingSystems = new[] { osType.ToString() },
            assetKinds = new[] { "Vm" },
            requiredTemplateCapabilities = Array.Empty<string>(),
            parameters = Array.Empty<object>(),
            files = Array.Empty<object>(),
            steps = new[]
            {
                new { id = "controlled-reboot", entrypoint = rebootName, timeoutSeconds = 30, runAs = "system", reboot = "Required" },
                new { id = "clean-shutdown", entrypoint = shutdownName, timeoutSeconds = 30, runAs = "system", reboot = "None" }
            },
            healthChecks = Array.Empty<object>(),
            maxReboots = 1
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var signed = BootstrapProfileOperationHandler.SignManifest(manifest);
        var artifact = CreateArchive(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [rebootName] = windows ? "exit 3010\r\n" : "#!/bin/sh\nexit 194\n",
            [shutdownName] = windows
                ? "Start-Process shutdown.exe -ArgumentList '/s /t 15 /f'\r\nexit 0\r\n"
                : "#!/bin/sh\nsystemd-run --unit=gzctf-conformance-poweroff --on-active=15s /usr/bin/systemctl poweroff\nexit 0\n"
        });
        var digest = Convert.ToHexStringLower(SHA256.HashData(artifact));
        return new PreparedImageConformancePackage(
            ProfileId,
            1,
            manifest,
            signed.Signature,
            signed.PublicKeyPem,
            digest,
            artifact);
    }

    private static byte[] CreateArchive(IReadOnlyDictionary<string, string> files)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new TarWriter(gzip, leaveOpen: true))
        {
            foreach (var (name, content) in files.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(bytes, writable: false),
                    Mode = (UnixFileMode)Convert.ToInt32("755", 8)
                };
                writer.WriteEntry(entry);
            }
        }
        return output.ToArray();
    }
}
