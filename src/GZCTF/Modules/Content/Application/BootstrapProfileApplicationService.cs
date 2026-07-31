using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.Identity.Application;
using GZCTF.Modules.TeamLab.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GZCTF.Modules.Content.Application;

public sealed partial class BootstrapProfileApplicationService(
    AppDbContext context,
    IBootstrapProfileArtifactStagingService artifacts,
    ExternalApiAuditContext auditContext)
{
    public const string OperationKind = "bootstrap-profile.mutate";
    public const string CreateRoute = "POST:/api/open/v1/bootstrap-profiles";
    public const string PublishRoute = "POST:/api/open/v1/bootstrap-profiles/{profileId}/versions";
    public const string DeleteRoute = "DELETE:/api/open/v1/bootstrap-profiles/{profileId}";
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public Task<IdempotencyBeginResult> SubmitCreateAsync(
        Guid apiTokenId,
        ActorContext actor,
        string idempotencyKey,
        BootstrapProfileCreateModel model,
        CancellationToken cancellationToken)
    {
        var name = model.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new BootstrapProfileContractException("bootstrap_profile_name_invalid", "Name is required.", 400);
        var profileId = Guid.CreateVersion7();
        var job = new BootstrapProfileOperationJob
        {
            Action = BootstrapProfileOperationAction.Create,
            ProfilePublicId = profileId,
            Name = name,
            Description = NormalizeDescription(model.Description),
            ActorUserId = RequireActor(actor)
        };
        return SubmitAsync(apiTokenId, actor, CreateRoute, idempotencyKey,
            Hash(new { name, description = job.Description }), job, null, cancellationToken);
    }

    public async Task<IdempotencyBeginResult> SubmitVersionAsync(
        Guid apiTokenId,
        ActorContext actor,
        Guid profileId,
        string idempotencyKey,
        BootstrapProfileVersionUploadModel model,
        CancellationToken cancellationToken)
    {
        var actorId = RequireActor(actor);
        var profile = await context.BootstrapProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == profileId && item.Status == BootstrapProfileStatus.Active,
                cancellationToken)
            ?? throw new BootstrapProfileContractException(
                "bootstrap_profile_not_found", "Bootstrap profile was not found.", 404);
        EnsureCanManage(profile, actor, actorId);
        var manifest = ParseAndValidateManifest(model.Manifest);
        await using var source = model.Artifact.OpenReadStream();
        var staged = await artifacts.StageAsync(source, model.Artifact.FileName, model.Artifact.Length,
            model.ExpectedDigest, cancellationToken);
        try
        {
            var version = model.Version;
            if (version is <= 0)
                throw new BootstrapProfileContractException(
                    "bootstrap_profile_version_invalid", "Version must be positive.", 400);
            var manifestJson = SerializeManifest(manifest);
            var job = new BootstrapProfileOperationJob
            {
                Action = BootstrapProfileOperationAction.PublishVersion,
                ProfilePublicId = profileId,
                Version = version,
                ManifestJson = manifestJson,
                StagedArtifactPath = staged.Path,
                ArtifactDigest = staged.Digest,
                ArtifactSize = staged.Size,
                ActorUserId = actorId
            };
            return await SubmitAsync(apiTokenId, actor, PublishRoute, idempotencyKey,
                Hash(new { profileId, requestedVersion = version, manifest = manifestJson, artifact = staged.Digest }),
                job, staged.Path, cancellationToken);
        }
        catch
        {
            await artifacts.DeleteStagedAsync(staged.Path, CancellationToken.None);
            throw;
        }
    }

    public async Task<IdempotencyBeginResult> SubmitDeleteAsync(
        Guid apiTokenId,
        ActorContext actor,
        Guid profileId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorId = RequireActor(actor);
        var profile = await context.BootstrapProfiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.PublicId == profileId, cancellationToken)
            ?? throw new BootstrapProfileContractException(
                "bootstrap_profile_not_found", "Bootstrap profile was not found.", 404);
        EnsureCanManage(profile, actor, actorId);
        var job = new BootstrapProfileOperationJob
        {
            Action = BootstrapProfileOperationAction.Delete,
            ProfilePublicId = profileId,
            ActorUserId = actorId
        };
        return await SubmitAsync(apiTokenId, actor, DeleteRoute, idempotencyKey,
            Hash(new { profileId }), job, null, cancellationToken);
    }

    public async Task<BootstrapProfileCursorPage> ListAsync(
        int limit,
        string? after,
        CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 100);
        var cursor = DecodeCursor(after);
        var query = context.BootstrapProfiles.AsNoTracking()
            .Where(item => item.Status != BootstrapProfileStatus.Deleted);
        if (cursor is not null)
            query = query.Where(item => item.CreatedAt < cursor.Value.CreatedAt ||
                                        item.CreatedAt == cursor.Value.CreatedAt && item.PublicId.CompareTo(cursor.Value.Id) < 0);
        var items = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.PublicId)
            .Take(limit + 1)
            .Select(item => new
            {
                Entity = item,
                Latest = item.Versions.Where(version => version.Status == BootstrapProfileVersionStatus.Ready)
                    .Max(version => (int?)version.Version)
            }).ToArrayAsync(cancellationToken);
        var page = items.Take(limit).Select(item => ToModel(item.Entity, item.Latest)).ToArray();
        var next = items.Length > limit
            ? EncodeCursor(items[limit - 1].Entity.CreatedAt, items[limit - 1].Entity.PublicId)
            : null;
        return new BootstrapProfileCursorPage(page, next);
    }

    public async Task<BootstrapProfileModel?> GetAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var item = await context.BootstrapProfiles.AsNoTracking()
            .Where(profile => profile.PublicId == profileId && profile.Status != BootstrapProfileStatus.Deleted)
            .Select(profile => new
            {
                Entity = profile,
                Latest = profile.Versions.Where(version => version.Status == BootstrapProfileVersionStatus.Ready)
                    .Max(version => (int?)version.Version)
            }).SingleOrDefaultAsync(cancellationToken);
        return item is null ? null : ToModel(item.Entity, item.Latest);
    }

    public async Task<BootstrapProfileVersionModel?> GetVersionAsync(
        Guid profileId,
        int version,
        CancellationToken cancellationToken)
    {
        var entity = await context.BootstrapProfileVersions.AsNoTracking().Include(item => item.Profile)
            .SingleOrDefaultAsync(item => item.Profile.PublicId == profileId && item.Version == version &&
                                          item.Status == BootstrapProfileVersionStatus.Ready &&
                                          item.Profile.Status != BootstrapProfileStatus.Deleted,
                cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public static BootstrapProfileManifest ParseAndValidateManifest(string json)
    {
        BootstrapProfileManifest manifest;
        try
        {
            var document = JsonSerializer.Deserialize<BootstrapProfileManifestDocument>(json, JsonOptions)
                           ?? throw new JsonException("Manifest is empty.");
            manifest = new BootstrapProfileManifest(
                document.SchemaVersion,
                (document.OperatingSystems ?? []).ToHashSet(),
                (document.AssetKinds ?? []).ToHashSet(),
                (document.RequiredTemplateCapabilities ?? []).ToHashSet(StringComparer.Ordinal),
                document.Parameters ?? [],
                document.Files ?? [],
                document.Steps ?? [],
                document.HealthChecks ?? [],
                document.MaxReboots);
        }
        catch (JsonException exception)
        {
            throw new BootstrapProfileContractException(
                "bootstrap_manifest_invalid", $"Bootstrap manifest is invalid: {exception.Message}", 400);
        }
        if (manifest.SchemaVersion != 1)
            throw Invalid("Only bootstrap manifest schemaVersion 1 is supported.");
        if (manifest.OperatingSystems.Count is 0 or > 2 || manifest.AssetKinds.Count is 0 or > 2)
            throw Invalid("OperatingSystems and AssetKinds must not be empty.");
        if (manifest.AssetKinds.Any(kind => kind != TeamLabAssetKind.Vm))
            throw Invalid("Bootstrap profiles currently support VM assets only.");
        if (manifest.RequiredTemplateCapabilities.Any(item => !ImageTemplateCapabilityIds.All.Contains(item)))
            throw Invalid("Manifest contains an unknown template capability.");
        if (manifest.MaxReboots is < 0 or > 3)
            throw Invalid("MaxReboots must be between 0 and 3.");
        if (manifest.Parameters.Count > 128 || manifest.Files.Count > 256 ||
            manifest.Steps.Count > 64 || manifest.HealthChecks.Count > 32)
            throw Invalid("Manifest collection limits were exceeded.");
        EnsureUnique(manifest.Parameters.Select(item => item.Key), "parameter keys");
        EnsureUnique(manifest.Steps.Select(item => item.Id), "step ids");
        EnsureUnique(manifest.HealthChecks.Select(item => item.Id), "health check ids");
        foreach (var item in manifest.Parameters)
        {
            if (!KeyRegex().IsMatch(item.Key)) throw Invalid($"Parameter key '{item.Key}' is invalid.");
            if (item.Secret && item.DefaultValue is not null)
                throw Invalid($"Secret parameter '{item.Key}' cannot define a default value.");
        }
        foreach (var item in manifest.Files)
        {
            ValidateArtifactPath(item.SourcePath, nameof(item.SourcePath));
            if (!IsGuestAbsolutePath(item.TargetPath)) throw Invalid($"Target path '{item.TargetPath}' is not absolute.");
            if (!ModeRegex().IsMatch(item.Mode)) throw Invalid($"File mode '{item.Mode}' is invalid.");
        }
        foreach (var item in manifest.Steps)
        {
            if (!KeyRegex().IsMatch(item.Id)) throw Invalid($"Step id '{item.Id}' is invalid.");
            ValidateArtifactPath(item.Entrypoint, nameof(item.Entrypoint));
            if (item.TimeoutSeconds is < 1 or > 3600) throw Invalid($"Step '{item.Id}' timeout is invalid.");
            if (!string.Equals(item.RunAs, "system", StringComparison.OrdinalIgnoreCase))
                throw Invalid($"Step '{item.Id}' runAs must be the platform identity 'system'.");
        }
        foreach (var item in manifest.HealthChecks)
        {
            if (!KeyRegex().IsMatch(item.Id)) throw Invalid($"Health check id '{item.Id}' is invalid.");
            if (string.IsNullOrWhiteSpace(item.Target) || item.Target.Length > 512 ||
                item.TimeoutSeconds is < 1 or > 300 || item.Attempts is < 1 or > 120)
                throw Invalid($"Health check '{item.Id}' is invalid.");
            if (item.Kind == BootstrapHealthCheckKind.Entrypoint)
                ValidateArtifactPath(item.Target, nameof(item.Target));
        }
        return manifest;
    }

    public static string SerializeManifest(BootstrapProfileManifest manifest) =>
        JsonSerializer.Serialize(Normalize(manifest), JsonOptions);

    public static BootstrapProfileVersionModel ToModel(BootstrapProfileVersion entity) => new(
        entity.Profile.PublicId,
        entity.Version,
        entity.Status,
        entity.ManifestDigest,
        entity.ArtifactDigest,
        entity.ArtifactSize,
        ParseAndValidateManifest(entity.ManifestJson),
        entity.CreatedAt);

    private async Task<IdempotencyBeginResult> SubmitAsync(
        Guid apiTokenId,
        ActorContext actor,
        string route,
        string idempotencyKey,
        string requestHash,
        BootstrapProfileOperationJob job,
        string? newStagedPath,
        CancellationToken cancellationToken)
    {
        var actorId = RequireActor(actor);
        var normalizedKey = ExternalIdempotencyKey.Normalize(idempotencyKey);
        var existing = await FindOperationAsync(apiTokenId, route, normalizedKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new IdempotencyConflictException();
            if (newStagedPath is not null) await artifacts.DeleteStagedAsync(newStagedPath, cancellationToken);
            auditContext.SetOperation(existing.Id, true);
            return new IdempotencyBeginResult(existing, true);
        }
        var now = DateTimeOffset.UtcNow;
        var operation = new ApiOperation
        {
            Kind = OperationKind,
            ActorUserId = actorId,
            ApiTokenId = apiTokenId,
            RouteKey = route,
            IdempotencyKey = normalizedKey,
            RequestHash = requestHash,
            CreatedAt = now,
            UpdatedAt = now
        };
        job.OperationId = operation.Id;
        context.AddRange(operation, job);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            context.ChangeTracker.Clear();
            existing = await FindOperationAsync(apiTokenId, route, normalizedKey, cancellationToken);
            if (existing is null) throw;
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw new IdempotencyConflictException();
            if (newStagedPath is not null) await artifacts.DeleteStagedAsync(newStagedPath, cancellationToken);
            auditContext.SetOperation(existing.Id, true);
            return new IdempotencyBeginResult(existing, true);
        }
        auditContext.SetOperation(operation.Id, false);
        return new IdempotencyBeginResult(operation, false);
    }

    private Task<ApiOperation?> FindOperationAsync(Guid tokenId, string route, string key,
        CancellationToken cancellationToken) => context.ApiOperations.AsNoTracking().SingleOrDefaultAsync(item =>
        item.ApiTokenId == tokenId && item.RouteKey == route && item.IdempotencyKey == key, cancellationToken);

    private static BootstrapProfileManifest Normalize(BootstrapProfileManifest manifest) => manifest with
    {
        OperatingSystems = manifest.OperatingSystems.Order().ToHashSet(),
        AssetKinds = manifest.AssetKinds.Order().ToHashSet(),
        RequiredTemplateCapabilities = manifest.RequiredTemplateCapabilities.Order(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
        Parameters = manifest.Parameters.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray(),
        Files = manifest.Files.OrderBy(item => item.TargetPath, StringComparer.Ordinal).ToArray(),
        Steps = manifest.Steps.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
        HealthChecks = manifest.HealthChecks.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray()
    };

    private static BootstrapProfileModel ToModel(BootstrapProfile item, int? latest) => new(
        item.PublicId, item.Name, item.Description, item.Status, latest, item.CreatedAt, item.UpdatedAt);

    private static Guid RequireActor(ActorContext actor) => actor.UserId ??
        throw new BootstrapProfileContractException("authentication_required", "Authentication is required.", 401);

    private static void EnsureCanManage(BootstrapProfile profile, ActorContext actor, Guid actorId)
    {
        if (profile.CreatedById == actorId || actor.Role >= Role.Admin) return;
        throw new BootstrapProfileContractException(
            "bootstrap_profile_forbidden", "The bootstrap profile is owned by another user.", 403);
    }

    private static string? NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));

    private static void EnsureUnique(IEnumerable<string> values, string field)
    {
        var items = values.ToArray();
        if (items.Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw Invalid($"Bootstrap manifest {field} must be unique.");
    }

    private static void ValidateArtifactPath(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || Path.IsPathRooted(value) ||
            value.Split('/', '\\').Any(segment => segment is "" or "." or ".."))
            throw Invalid($"{field} '{value}' is not a safe artifact-relative path.");
    }

    private static bool IsGuestAbsolutePath(string value) =>
        value.StartsWith("/", StringComparison.Ordinal) || WindowsPathRegex().IsMatch(value);

    private static BootstrapProfileContractException Invalid(string message) =>
        new("bootstrap_manifest_invalid", message, 400);

    private static string EncodeCursor(DateTimeOffset time, Guid id) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"{time.UtcTicks}:{id:N}"));

    private static (DateTimeOffset CreatedAt, Guid Id)? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split(':');
            if (parts.Length == 2 && long.TryParse(parts[0], out var ticks) && Guid.TryParseExact(parts[1], "N", out var id))
                return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (FormatException) { }
        throw new BootstrapProfileContractException("cursor_invalid", "Pagination cursor is invalid.", 400);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [GeneratedRegex("^[a-z][a-zA-Z0-9_.-]{0,62}$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex("^0[0-7]{3}$")]
    private static partial Regex ModeRegex();

    [GeneratedRegex("^[a-zA-Z]:\\\\")]
    private static partial Regex WindowsPathRegex();

    private sealed record BootstrapProfileManifestDocument(
        int SchemaVersion,
        OSType[]? OperatingSystems,
        TeamLabAssetKind[]? AssetKinds,
        string[]? RequiredTemplateCapabilities,
        BootstrapParameterDefinition[]? Parameters,
        BootstrapFileDefinition[]? Files,
        BootstrapStepDefinition[]? Steps,
        BootstrapHealthCheckDefinition[]? HealthChecks,
        int MaxReboots);
}

public sealed class BootstrapProfileContractException(string code, string message, int statusCode)
    : ApiContractException(code, message, statusCode);
