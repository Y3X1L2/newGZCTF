using System.Text.Json;
using System.Text.Json.Serialization;

namespace GZCTF.TeamLab.Contracts.Execution;

/// <summary>Public device parameters only. Secrets belong to the separate runtime secret channel.</summary>
public sealed record TeamLabDeviceExecutionV2(Guid PackageId, string Name, string Version, string ArtifactDigest,
    string ParametersJson, string? HealthProtocol = null, int? HealthPort = null, string? HealthPath = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? HealthIntervalSeconds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ProtocolEventTypes = null)
{
    public bool IsValid(string imageDigest)
    {
        if (PackageId == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 96 ||
            string.IsNullOrWhiteSpace(Version) || Version.Length > 64 ||
            !string.Equals(ArtifactDigest, imageDigest, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(ParametersJson) || ParametersJson.Length > 2048 ||
            HealthProtocol is not (null or "tcp" or "http") ||
            HealthIntervalSeconds is < 1 or > 3600 ||
            ProtocolEventTypes is { Count: > 32 } ||
            HealthProtocol is not null && HealthPort is not (> 0 and <= 65535) ||
            HealthProtocol == "http" && (HealthPath is null || !HealthPath.StartsWith('/') || HealthPath.Length > 256))
            return false;
        try { using var document = JsonDocument.Parse(ParametersJson); return document.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }
}
