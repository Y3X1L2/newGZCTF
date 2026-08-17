using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Models.Data;
using GZCTF.Modules.Runtime.Contracts;

namespace GZCTF.Services.Fleet;

public static class AgentCapabilityEvaluator
{
    public const int SupportedManifestSchema = 1;


    public static bool Supports(WorkerNode node, params string[] features) =>
        MissingFeatures(node, features).Length == 0;

    public static string[] MissingFeatures(WorkerNode node, params string[] features)
    {
        var manifest = Parse(node.CapabilityManifestJson);
        if (manifest is null || manifest.ManifestSchemaVersion != SupportedManifestSchema)
            return features;
        var available = manifest.Features.ToHashSet(StringComparer.Ordinal);
        return features.Where(feature => !available.Contains(feature)).ToArray();
    }
    public static AgentCapabilityManifest? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentCapabilityManifest>(json,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static (string Json, string Hash, NodeCapability Capabilities) Normalize(AgentCapabilityManifest manifest)
    {
        if (manifest.ManifestSchemaVersion != SupportedManifestSchema)
            throw new InvalidOperationException(
                $"Unsupported Agent capability manifest schema {manifest.ManifestSchemaVersion}.");
        var normalized = manifest with
        {
            Features = manifest.Features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            ObservedAt = DateTimeOffset.UnixEpoch
        };
        var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var capabilities = NodeCapability.None;
        if (normalized.Features.Contains(AgentFeatureIds.Docker, StringComparer.Ordinal))
            capabilities |= NodeCapability.Docker;
        if (normalized.Features.Contains(AgentFeatureIds.Kvm, StringComparer.Ordinal))
            capabilities |= NodeCapability.Kvm;
        return (json, hash, capabilities);
    }
}
