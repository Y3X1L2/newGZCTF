using GZCTF.Infrastructure.Persistence.Queries;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.TeamLab.Application;

/// <summary>
/// Publishes the bootstrap service-profile catalog to external callers. It exposes only
/// manifest-derived metadata; script bodies, artifact paths, signatures and secret values
/// never cross this boundary.
/// </summary>
public sealed class TeamLabServiceProfileCatalogService(AppDbContext context)
{
    public async Task<TeamLabServiceProfilePageModel> ListAsync(
        string? after,
        int limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, 100);
        var cursor = DecodeCursor(after);
        var profiles = await context.BootstrapProfileVersions.AsNoTracking()
            .Where(item => item.Status == BootstrapProfileVersionStatus.Ready &&
                           item.Profile.Status == BootstrapProfileStatus.Active)
            .Select(item => new
            {
                item.Profile.PublicId,
                item.Profile.Name,
                item.Profile.Description,
                item.Version,
                UpdatedAt = item.Profile.UpdatedAt ?? item.Profile.CreatedAt,
                item.ManifestJson
            })
            .ToArrayAsync(cancellationToken);
        var latest = profiles
            .GroupBy(item => item.PublicId)
            .Select(group => new
            {
                Latest = group.OrderByDescending(item => item.Version).First(),
                AvailableVersions = group.Select(item => item.Version).OrderByDescending(version => version).ToArray()
            })
            .OrderByDescending(item => item.Latest.UpdatedAt)
            .ThenByDescending(item => item.Latest.PublicId)
            .ToArray();
        var filtered = cursor is { } value
            ? latest.Where(item => item.Latest.UpdatedAt < value.Time ||
                                   item.Latest.UpdatedAt == value.Time && item.Latest.PublicId.CompareTo(value.Id) < 0)
                .ToArray()
            : latest;
        var page = filtered.Take(take).Select(item => ToSummary(item.Latest.PublicId, item.Latest.Name,
            item.Latest.Description, item.Latest.Version, item.AvailableVersions, item.Latest.UpdatedAt,
            item.Latest.ManifestJson)).ToArray();
        var next = filtered.Length > take
            ? new GuidTimeCursor(page[^1].UpdatedAt, page[^1].Id).Encode()
            : null;
        return new TeamLabServiceProfilePageModel(page, next);
    }

    public async Task<TeamLabServiceProfileDetailModel> GetAsync(
        Guid profileId,
        int? version,
        CancellationToken cancellationToken)
    {
        var query = context.BootstrapProfileVersions.AsNoTracking()
            .Where(item => item.Profile.PublicId == profileId &&
                           item.Status == BootstrapProfileVersionStatus.Ready &&
                           item.Profile.Status == BootstrapProfileStatus.Active);
        if (version is { } requestedVersion)
            query = query.Where(item => item.Version == requestedVersion);
        else
            query = query.OrderByDescending(item => item.Version);
        var found = await query
            .Select(item => new
            {
                item.Profile.PublicId,
                item.Profile.Name,
                item.Profile.Description,
                item.Version,
                item.Profile.UpdatedAt,
                ProfileCreatedAt = item.Profile.CreatedAt,
                item.CreatedAt,
                item.ManifestJson
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (found is null)
            throw new TeamLabApiContractException("service_profile_not_found", "未找到可用的服务目录条目。", 404);
        var availableVersions = await context.BootstrapProfileVersions.AsNoTracking()
            .Where(item => item.Profile.PublicId == profileId &&
                           item.Status == BootstrapProfileVersionStatus.Ready &&
                           item.Profile.Status == BootstrapProfileStatus.Active)
            .Select(item => item.Version)
            .OrderByDescending(item => item)
            .ToArrayAsync(cancellationToken);
        return ToDetail(found.PublicId, found.Name, found.Description, found.Version, availableVersions,
            found.UpdatedAt ?? found.ProfileCreatedAt, found.CreatedAt, found.ManifestJson);
    }

    private static TeamLabServiceProfileSummaryModel ToSummary(
        Guid profileId,
        string name,
        string? description,
        int version,
        IReadOnlyList<int> availableVersions,
        DateTimeOffset updatedAt,
        string manifestJson)
    {
        var manifest = BootstrapProfileApplicationService.ParseAndValidateManifest(manifestJson);
        return new TeamLabServiceProfileSummaryModel(
            profileId,
            version,
            availableVersions,
            name,
            description,
            manifest.AssetKinds.OrderBy(kind => kind).ToArray(),
            updatedAt,
            name,
            name,
            description,
            "published");
    }

    private static TeamLabServiceProfileDetailModel ToDetail(
        Guid profileId,
        string name,
        string? description,
        int version,
        IReadOnlyList<int> availableVersions,
        DateTimeOffset updatedAt,
        DateTimeOffset publishedAt,
        string manifestJson)
    {
        var manifest = BootstrapProfileApplicationService.ParseAndValidateManifest(manifestJson);
        var parameters = manifest.Parameters
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new TeamLabServiceProfileParameterModel(
                item.Key,
                item.Type.ToString(),
                item.Required,
                item.Secret,
                item.Secret ? null : item.DefaultValue,
                item.Secret ? null : item.DefaultValue))
            .ToArray();
        var phase = manifest.Steps.Count > 0
            ? "install"
            : manifest.HealthChecks.Count > 0 ? "verify" : "provision";
        return new TeamLabServiceProfileDetailModel(
            profileId,
            version,
            availableVersions,
            name,
            description,
            updatedAt,
            manifest.AssetKinds.OrderBy(kind => kind).ToArray(),
            parameters,
            new TeamLabServiceProfileExecutionModel(
                manifest.Steps.Count,
                manifest.HealthChecks.Count,
                manifest.MaxReboots,
                phase),
            "published",
            null,
            "runtime-overlay",
            publishedAt,
            name,
            name,
            description,
            null);
    }

    private static GuidTimeCursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return GuidTimeCursor.Decode(value);
        }
        catch (InvalidTimeCursorException)
        {
            throw new TeamLabApiContractException("service_profile_cursor_invalid", "服务目录分页游标无效。", 400);
        }
    }
}
