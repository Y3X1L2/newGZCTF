using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GZCTF.Models.Data;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace GZCTF.Modules.TeamLab.Application;

public sealed partial class TeamLabRuntimeOverlayService(IDataProtectionProvider protectionProvider)
{
    private const string Purpose = "GZCTF.TeamLab.RuntimeOverlay.v1";
    private readonly IDataProtector _protector = protectionProvider.CreateProtector(Purpose);

    public TeamLabRuntimeSecretEnvelope? Protect(
        int runtimeId,
        int generation,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? overlays,
        IReadOnlySet<string> assetKeys)
    {
        if (overlays is null || overlays.Count == 0) return null;
        var normalized = overlays.OrderBy(item => item.AssetKey, StringComparer.Ordinal)
            .Select(item => Normalize(item, assetKeys)).ToArray();
        var payload = JsonSerializer.Serialize(normalized);
        return new TeamLabRuntimeSecretEnvelope
        {
            RuntimeId = runtimeId,
            Generation = generation,
            ProtectedPayload = _protector.Protect(payload),
            PayloadHash = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized)))}",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2)
        };
    }

    public IReadOnlyDictionary<string, TeamLabRuntimeOverlayModel> Unprotect(TeamLabRuntimeSecretEnvelope? envelope)
    {
        if (envelope?.ProtectedPayload is null) return new Dictionary<string, TeamLabRuntimeOverlayModel>();
        if (envelope.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new TeamLabApiContractException("runtime_overlay_expired", "The runtime overlay has expired.", 409);
        try
        {
            var payload = _protector.Unprotect(envelope.ProtectedPayload);
            return (JsonSerializer.Deserialize<TeamLabRuntimeOverlayModel[]>(payload) ?? [])
                .ToDictionary(item => item.AssetKey, StringComparer.Ordinal);
        }
        catch (CryptographicException)
        {
            throw new TeamLabApiContractException("runtime_overlay_invalid", "The runtime overlay cannot be decrypted.", 500);
        }
    }

    public static void Consume(TeamLabRuntimeSecretEnvelope? envelope)
    {
        if (envelope is null) return;
        envelope.ProtectedPayload = null;
        envelope.ConsumedAt = DateTimeOffset.UtcNow;
    }

    private static TeamLabRuntimeOverlayModel Normalize(TeamLabRuntimeOverlayModel overlay, IReadOnlySet<string> assetKeys)
    {
        var assetKey = overlay.AssetKey.Trim();
        if (!assetKeys.Contains(assetKey))
            throw new TeamLabApiContractException("topology_invalid", $"Overlay asset '{assetKey}' does not exist.", 422);
        var environment = NormalizeValues(overlay.Environment, false);
        var secrets = NormalizeValues(overlay.Secrets, true);
        return new TeamLabRuntimeOverlayModel(assetKey, environment, secrets);
    }

    private static IReadOnlyDictionary<string, string>? NormalizeValues(
        IReadOnlyDictionary<string, string>? values,
        bool secret)
    {
        if (values is null || values.Count == 0) return null;
        var normalized = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            var key = pair.Key.Trim();
            if (!EnvironmentKeyRegex().IsMatch(key))
                throw new TeamLabApiContractException("topology_invalid", $"Overlay key '{key}' is invalid.", 422);
            if (pair.Value is null || pair.Value.Length > (secret ? 4096 : 16384))
                throw new TeamLabApiContractException("topology_invalid", $"Overlay value '{key}' is too large.", 422);
            normalized[key] = pair.Value;
        }
        return normalized;
    }

    [GeneratedRegex("^[A-Z_][A-Z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentKeyRegex();
}
