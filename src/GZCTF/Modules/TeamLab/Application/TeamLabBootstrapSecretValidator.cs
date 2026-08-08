using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Validates that every required secret bootstrap parameter is supplied through the
/// runtime create/reset overlay channel before deployment starts. Secret parameters
/// can never be stored in topology JSON (the publish validator rejects them), so the
/// overlay is the only legal supply path and this check moves the failure from
/// mid-deployment to command admission.
/// </summary>
public sealed class TeamLabBootstrapSecretValidator(AppDbContext context)
{
    public async Task<IReadOnlyList<TeamLabRequiredRuntimeSecretModel>> GetRequiredSecretsAsync(
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        var release = await context.TeamLabTopologyReleases.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == releaseId, cancellationToken);
        if (release is null) return [];

        var execution = TeamLabReleaseCodec.DecodeExecution(release.SchemaVersion, release.CanonicalJson);
        var references = execution.Assets
            .Where(item => item.Bootstrap is not null)
            .Select(item => new { item.Key, item.Name, item.Bootstrap!.ProfileId, item.Bootstrap.Version })
            .ToArray();
        if (references.Length == 0) return [];

        var profileIds = references.Select(item => item.ProfileId).Distinct().ToArray();
        var versions = await context.BootstrapProfileVersions.AsNoTracking()
            .Include(item => item.Profile)
            .Where(item => profileIds.Contains(item.Profile.PublicId) &&
                           item.Status == BootstrapProfileVersionStatus.Ready &&
                           item.Profile.Status == BootstrapProfileStatus.Active)
            .ToArrayAsync(cancellationToken);

        return references.SelectMany(reference =>
            {
                var version = versions.FirstOrDefault(item =>
                    item.Profile.PublicId == reference.ProfileId && item.Version == reference.Version);
                if (version is null) return [];
                var manifest = BootstrapProfileApplicationService.ParseAndValidateManifest(version.ManifestJson);
                return manifest.Parameters
                    .Where(item => item.Required && item.Secret)
                    .Select(item => new TeamLabRequiredRuntimeSecretModel(reference.Key, reference.Name, item.Key));
            })
            .OrderBy(item => item.AssetKey, StringComparer.Ordinal)
            .ThenBy(item => item.ParameterKey, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task RequireAsync(
        Guid releaseId,
        IReadOnlyList<TeamLabRuntimeOverlayModel>? overlays,
        CancellationToken cancellationToken)
    {
        var required = await GetRequiredSecretsAsync(releaseId, cancellationToken);
        if (required.Count == 0) return;
        var overlayByAsset = overlays?
            .ToDictionary(item => item.AssetKey, StringComparer.Ordinal) ?? new Dictionary<string, TeamLabRuntimeOverlayModel>();

        foreach (var group in required.GroupBy(item => item.AssetKey, StringComparer.Ordinal))
        {
            var supplied = overlayByAsset.TryGetValue(group.Key, out var overlay)
                ? overlay.Secrets ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();
            var missing = group.Select(item => item.ParameterKey).Where(key => !supplied.ContainsKey(key)).ToArray();
            if (missing.Length == 0)
                continue;
            throw new TeamLabApiContractException(
                "bootstrap_secret_required",
                $"资产 {group.First().AssetName} 缺少运行时密钥参数 {string.Join(", ", missing)}，" +
                "请在运行时创建/重置请求的 overlays.secrets 中提供（密钥参数不允许写入拓扑 JSON）。",
                422);
        }
    }
}
