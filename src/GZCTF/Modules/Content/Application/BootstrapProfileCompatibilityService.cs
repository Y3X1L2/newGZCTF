using System.Text.Json;
using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.GuestControl.Contracts;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Application;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Application;

public sealed class BootstrapProfileCompatibilityService(AppDbContext context)
{
    public async Task<IReadOnlyList<BootstrapProfileVersion>> ValidateReleaseAsync(
        TeamLabExecutionTopology definition,
        CancellationToken cancellationToken)
    {
        var assets = definition.Assets.Where(item => item.Bootstrap is not null).ToArray();
        var vmAssets = definition.Assets.Where(item => item.Kind == TeamLabAssetKind.Vm).ToArray();
        if (assets.Length == 0 && vmAssets.Length == 0) return [];
        var profileIds = assets.Select(item => item.Bootstrap!.ProfileId).Distinct().ToArray();
        var versions = await context.BootstrapProfileVersions.AsNoTracking().Include(item => item.Profile)
            .Where(item => profileIds.Contains(item.Profile.PublicId) &&
                           item.Status == BootstrapProfileVersionStatus.Ready &&
                           item.Profile.Status == BootstrapProfileStatus.Active)
            .ToArrayAsync(cancellationToken);
        var templateIds = assets.Concat(vmAssets).Select(item => item.ImageTemplateId).Distinct().ToArray();
        var templates = await context.ImageTemplates.AsNoTracking()
            .Where(item => templateIds.Contains(item.Id) && item.Status == ImageStatus.Ready)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var certifications = await context.ImageTemplateCapabilityCertifications.AsNoTracking()
            .Where(item => templateIds.Contains(item.ImageTemplateId) &&
                           item.Status == ImageTemplateCertificationStatus.Certified)
            .ToArrayAsync(cancellationToken);
        foreach (var asset in vmAssets)
        {
            if (!templates.TryGetValue(asset.ImageTemplateId, out var template) ||
                string.IsNullOrWhiteSpace(template.ImageHash))
                throw Conflict($"Image template {asset.ImageTemplateId} is not ready for VM validation.");
            var requiresGuestControl = asset.Bootstrap is not null ||
                                       asset.EndpointObservation != TeamLabEndpointObservationMode.Disabled;
            if (!requiresGuestControl)
                continue;
            if (template.VmRuntimeMode == VmRuntimeMode.Opaque)
                throw Conflict(
                    $"Opaque VM template {template.Name} cannot provide bootstrap or process telemetry.");
            var currentCertification = certifications.FirstOrDefault(item =>
                IsCurrentManagedCertification(item, template));
            if (currentCertification is null)
                throw Conflict(
                    $"Image template {template.Name} has no current managed guest-control certification.");
            var certified = (JsonSerializer.Deserialize<string[]>(currentCertification.CapabilitiesJson) ?? [])
                .ToHashSet(StringComparer.Ordinal);
            var required = template.OSType == OSType.Windows
                ? new[]
                {
                    ImageTemplateCapabilityIds.WindowsCloudbaseInit,
                    ImageTemplateCapabilityIds.NetworkE1000E,
                    ImageTemplateCapabilityIds.GuestSupervisor,
                    ImageTemplateCapabilityIds.VmPreparedImage
                }
                : new[]
                {
                    ImageTemplateCapabilityIds.LinuxCloudInitNoCloud,
                    ImageTemplateCapabilityIds.NetworkVirtio,
                    ImageTemplateCapabilityIds.GuestSupervisor,
                    ImageTemplateCapabilityIds.VmPreparedImage
                };
            var missing = required.Where(item => !certified.Contains(item)).ToArray();
            if (missing.Length > 0)
                throw Conflict(
                    $"Image template {template.Name} lacks required TeamLab VM capabilities: {string.Join(", ", missing)}.");
        }
        var usedVersions = new List<BootstrapProfileVersion>();
        foreach (var asset in assets)
        {
            var reference = asset.Bootstrap!;
            var profileVersion = versions.SingleOrDefault(item =>
                item.Profile.PublicId == reference.ProfileId && item.Version == reference.Version)
                ?? throw Conflict(
                    $"Bootstrap profile {reference.ProfileId:D} version {reference.Version} is not ready.");
            if (!templates.TryGetValue(asset.ImageTemplateId, out var template) ||
                string.IsNullOrWhiteSpace(template.ImageHash))
                throw Conflict($"Image template {asset.ImageTemplateId} is not ready for bootstrap validation.");
            var manifest = BootstrapProfileApplicationService.ParseAndValidateManifest(profileVersion.ManifestJson);
            if (!manifest.AssetKinds.Contains(asset.Kind))
                throw Conflict(
                    $"Bootstrap profile {reference.ProfileId:D} v{reference.Version} does not support {asset.Kind} assets.");
            if (!manifest.OperatingSystems.Contains(template.OSType))
                throw Conflict(
                    $"Bootstrap profile {reference.ProfileId:D} v{reference.Version} does not support {template.OSType} templates.");
            var certified = CertifiedCapabilities(certifications, template);
            var missing = manifest.RequiredTemplateCapabilities.Where(item => !certified.Contains(item)).ToArray();
            if (missing.Length > 0)
                throw Conflict(
                    $"Image template {template.Name} lacks required certified capabilities: {string.Join(", ", missing)}.");
            ValidateParameters(reference.Parameters, manifest, asset.Key);
            usedVersions.Add(profileVersion);
        }
        return usedVersions.DistinctBy(item => item.Id).OrderBy(item => item.Id).ToArray();
    }

    private static HashSet<string> CertifiedCapabilities(
        IEnumerable<ImageTemplateCapabilityCertification> certifications,
        ImageTemplate template) => certifications
        .Where(item => IsCurrentManagedCertification(item, template))
        .SelectMany(item => JsonSerializer.Deserialize<string[]>(item.CapabilitiesJson) ?? [])
        .ToHashSet(StringComparer.Ordinal);

    internal static bool IsCurrentManagedCertification(
        ImageTemplateCapabilityCertification certification,
        ImageTemplate template)
    {
        if (template.VmRuntimeMode == VmRuntimeMode.Opaque ||
            template.VmArtifactStatus != VmArtifactStatus.Ready ||
            template.VmRuntimeMode == VmRuntimeMode.Managed && template.PreparedArtifactId is null ||
            certification.ImageTemplateId != template.Id ||
            !string.Equals(certification.ImageHash, template.ImageHash, StringComparison.Ordinal) ||
            certification.Status != ImageTemplateCertificationStatus.Certified ||
            !string.Equals(certification.ProbeKind, "controlled-probe", StringComparison.Ordinal) ||
            certification.PreparationContractVersion != GuestControlProtocol.PreparationContractVersion ||
            certification.GuestProtocolVersion != GuestControlProtocol.SchemaVersion)
            return false;

        var capabilities = (JsonSerializer.Deserialize<string[]>(certification.CapabilitiesJson) ?? [])
            .ToHashSet(StringComparer.Ordinal);
        return capabilities.Contains(ImageTemplateCapabilityIds.GuestSupervisor) &&
               capabilities.Contains(ImageTemplateCapabilityIds.VmPreparedImage);
    }

    private static void ValidateParameters(
        IReadOnlyDictionary<string, string> values,
        BootstrapProfileManifest manifest,
        string assetKey)
    {
        var definitions = manifest.Parameters.ToDictionary(item => item.Key, StringComparer.Ordinal);
        foreach (var key in values.Keys)
        {
            if (!definitions.TryGetValue(key, out var definition))
                throw Conflict($"Asset '{assetKey}' supplies unknown bootstrap parameter '{key}'.");
            if (definition.Secret)
                throw Conflict(
                    $"Asset '{assetKey}' cannot store secret bootstrap parameter '{key}' in topology JSON.");
            ValidateValue(key, values[key], definition.Type, assetKey);
        }
        var missing = manifest.Parameters.Where(item => item.Required && !item.Secret && item.DefaultValue is null &&
                                                         !values.ContainsKey(item.Key))
            .Select(item => item.Key).ToArray();
        if (missing.Length > 0)
            throw Conflict($"Asset '{assetKey}' is missing bootstrap parameters: {string.Join(", ", missing)}.");
    }

    private static void ValidateValue(
        string key,
        string value,
        BootstrapParameterType type,
        string assetKey)
    {
        var valid = type switch
        {
            BootstrapParameterType.String => value.Length <= 4096,
            BootstrapParameterType.Integer => long.TryParse(value, out _),
            BootstrapParameterType.Boolean => bool.TryParse(value, out _),
            _ => false
        };
        if (!valid) throw Conflict($"Asset '{assetKey}' parameter '{key}' has an invalid value.");
    }

    private static TeamLabApiContractException Conflict(string message) =>
        new("bootstrap_profile_incompatible", message, 409);
}
