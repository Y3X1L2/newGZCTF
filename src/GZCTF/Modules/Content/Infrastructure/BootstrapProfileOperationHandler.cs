using System.Security.Cryptography;
using System.Text;
using GZCTF.Models;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Modules.Content.Domain;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Infrastructure;

public sealed class BootstrapProfileOperationHandler(
    AppDbContext context,
    ApiOperationService operations,
    BootstrapProfileArtifactService artifacts,
    BootstrapProfileDistributionService distribution,
    IEnumerable<IBootstrapProfileReferenceProvider> referenceProviders) : IApiOperationHandler
{
    public string Kind => BootstrapProfileApplicationService.OperationKind;

    public async Task ExecuteAsync(Guid operationId, string leaseOwner, CancellationToken cancellationToken)
    {
        var job = await context.BootstrapProfileOperationJobs.SingleOrDefaultAsync(
            item => item.OperationId == operationId, cancellationToken)
            ?? throw new ApiOperationTerminalException(
                "bootstrap_profile_job_not_found", "Bootstrap profile operation job was not found.");
        switch (job.Action)
        {
            case BootstrapProfileOperationAction.Create:
                await CreateAsync(job, operationId, leaseOwner, cancellationToken);
                break;
            case BootstrapProfileOperationAction.PublishVersion:
                await PublishAsync(job, operationId, leaseOwner, cancellationToken);
                break;
            case BootstrapProfileOperationAction.Delete:
                await DeleteAsync(job, operationId, leaseOwner, cancellationToken);
                break;
            default:
                throw new ApiOperationTerminalException(
                    "bootstrap_profile_action_invalid", "Bootstrap profile operation action is invalid.");
        }
    }

    public async Task OnTerminalFailureAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var path = await context.BootstrapProfileOperationJobs.AsNoTracking()
            .Where(item => item.OperationId == operationId)
            .Select(item => item.StagedArtifactPath)
            .SingleOrDefaultAsync(cancellationToken);
        await artifacts.DeleteStagedAsync(path, cancellationToken);
    }

    private async Task CreateAsync(
        BootstrapProfileOperationJob job,
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var existing = await context.BootstrapProfiles.SingleOrDefaultAsync(
            item => item.PublicId == job.ProfilePublicId, cancellationToken);
        if (existing is null)
        {
            if (string.IsNullOrWhiteSpace(job.Name))
                throw new ApiOperationTerminalException(
                    "bootstrap_profile_name_invalid", "Bootstrap profile name is missing.");
            existing = new BootstrapProfile
            {
                PublicId = job.ProfilePublicId,
                Name = job.Name,
                Description = job.Description,
                CreatedById = job.ActorUserId
            };
            context.BootstrapProfiles.Add(existing);
            await context.SaveChangesAsync(cancellationToken);
        }
        await RequireLeaseAsync(operationId, leaseOwner, "profile-created", 1, 1,
            existing.PublicId.ToString("D"), cancellationToken);
    }

    private async Task PublishAsync(
        BootstrapProfileOperationJob job,
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.ManifestJson) || string.IsNullOrWhiteSpace(job.ArtifactDigest))
            throw new ApiOperationTerminalException(
                "bootstrap_profile_publish_invalid", "Bootstrap profile publish job is incomplete.");
        var profile = await context.BootstrapProfiles.SingleOrDefaultAsync(
            item => item.PublicId == job.ProfilePublicId && item.Status == BootstrapProfileStatus.Active,
            cancellationToken)
            ?? throw new ApiOperationTerminalException(
                "bootstrap_profile_not_found", "Bootstrap profile was not found.");
        await RequireCanManageAsync(profile, job.ActorUserId, cancellationToken);
        var manifest = BootstrapProfileApplicationService.ParseAndValidateManifest(job.ManifestJson);
        var canonicalManifest = BootstrapProfileApplicationService.SerializeManifest(manifest);
        var manifestDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalManifest)));
        var (manifestSignature, signingPublicKeyPem) = SignManifest(canonicalManifest);
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"BootstrapProfiles\" WHERE \"Id\" = {profile.Id} FOR UPDATE",
                cancellationToken);
        if (!job.Version.HasValue)
            job.Version = (await context.BootstrapProfileVersions
                .Where(item => item.ProfileId == profile.Id)
                .MaxAsync(item => (int?)item.Version, cancellationToken) ?? 0) + 1;
        var existing = await context.BootstrapProfileVersions.Include(item => item.Profile)
            .SingleOrDefaultAsync(item => item.ProfileId == profile.Id && item.Version == job.Version.Value,
                cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ArtifactDigest, job.ArtifactDigest, StringComparison.Ordinal) ||
                !string.Equals(existing.ManifestDigest, manifestDigest, StringComparison.Ordinal))
                throw new ApiOperationTerminalException(
                    "bootstrap_profile_version_conflict",
                    "Bootstrap profile version already exists with different content.");
        }
        else
        {
            var plannedReference = artifacts.BuildReference(
                job.ProfilePublicId, job.Version.Value, job.ArtifactDigest, job.ArtifactSize);
            existing = new BootstrapProfileVersion
            {
                ProfileId = profile.Id,
                Version = job.Version.Value,
                Status = BootstrapProfileVersionStatus.Publishing,
                ManifestJson = canonicalManifest,
                ManifestDigest = manifestDigest,
                ManifestSignature = manifestSignature,
                SigningPublicKeyPem = signingPublicKeyPem,
                ArtifactDigest = OciArtifactRegistryClient.NormalizeDigest(job.ArtifactDigest),
                ArtifactSize = job.ArtifactSize,
                RegistryAddress = plannedReference.RegistryAddress,
                RegistryRepository = plannedReference.Repository,
                RegistryTag = plannedReference.Tag,
                CreatedById = job.ActorUserId
            };
            context.BootstrapProfileVersions.Add(existing);
        }
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        if (existing.Status != BootstrapProfileVersionStatus.Ready)
        {
            await RequireLeaseAsync(operationId, leaseOwner, "artifact-publishing", 1, 3, existing.Id.ToString(),
                cancellationToken);
            var reference = await artifacts.PublishAsync(job, cancellationToken);
            existing.Status = BootstrapProfileVersionStatus.Ready;
            existing.RegistryAddress = reference.RegistryAddress;
            existing.RegistryRepository = reference.Repository;
            existing.RegistryTag = reference.Tag;
            existing.ErrorMessage = null;
            profile.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        await artifacts.DeleteStagedAsync(job.StagedArtifactPath, CancellationToken.None);
        await RequireLeaseAsync(operationId, leaseOwner, "artifact-ready", 2, 3, existing.Id.ToString(),
            cancellationToken);
        var records = await distribution.QueueAndDistributeAsync(existing.Id, cancellationToken);
        var failed = records.FirstOrDefault(item => item.Status == BootstrapProfileDistributionStatus.Failed);
        if (failed is not null)
            throw new InvalidOperationException(failed.ErrorMessage ?? "Bootstrap artifact distribution failed.");
        await RequireLeaseAsync(operationId, leaseOwner, "artifact-distributed", 3, 3, existing.Id.ToString(),
            cancellationToken);
    }

    internal static (string Signature, string PublicKeyPem) SignManifest(string canonicalManifest)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = key.SignData(
            Encoding.UTF8.GetBytes(canonicalManifest),
            HashAlgorithmName.SHA256);
        return (Convert.ToBase64String(signature), key.ExportSubjectPublicKeyInfoPem());
    }

    private async Task DeleteAsync(
        BootstrapProfileOperationJob job,
        Guid operationId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        var profile = await context.BootstrapProfiles.Include(item => item.Versions)
            .SingleOrDefaultAsync(item => item.PublicId == job.ProfilePublicId, cancellationToken)
            ?? throw new ApiOperationTerminalException(
                "bootstrap_profile_not_found", "Bootstrap profile was not found.");
        await RequireCanManageAsync(profile, job.ActorUserId, cancellationToken);
        if (profile.Status == BootstrapProfileStatus.Deleted)
        {
            await RequireLeaseAsync(operationId, leaseOwner, "profile-deleted", 1, 1,
                profile.PublicId.ToString("D"), cancellationToken);
            return;
        }
        var references = new List<BootstrapProfileReference>();
        foreach (var provider in referenceProviders)
            references.AddRange(await provider.GetReferencesAsync(profile.PublicId, cancellationToken));
        if (references.Count > 0)
            throw new ApiOperationTerminalException(
                "bootstrap_profile_in_use",
                "Bootstrap profile is referenced by TeamLab topology assets or releases.");
        profile.Status = BootstrapProfileStatus.Deleting;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        foreach (var version in profile.Versions.OrderBy(item => item.Version))
        {
            await distribution.DeleteVersionCachesAsync(version, cancellationToken);
            await artifacts.DeletePublishedAsync(version, cancellationToken);
        }
        profile.Status = BootstrapProfileStatus.Deleted;
        profile.DeletedAt = DateTimeOffset.UtcNow;
        profile.UpdatedAt = profile.DeletedAt;
        await context.SaveChangesAsync(cancellationToken);
        await RequireLeaseAsync(operationId, leaseOwner, "profile-deleted", 1, 1,
            profile.PublicId.ToString("D"), cancellationToken);
    }

    private async Task RequireLeaseAsync(
        Guid operationId,
        string leaseOwner,
        string stage,
        long current,
        long total,
        string? resourceId,
        CancellationToken cancellationToken)
    {
        if (!await operations.UpdateProgressAsync(
                operationId, leaseOwner, stage, current, total,
                "bootstrap-profile", resourceId, null, cancellationToken))
            throw new OperationCanceledException("Bootstrap profile operation lease was lost.", cancellationToken);
    }

    private async Task RequireCanManageAsync(
        BootstrapProfile profile,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (profile.CreatedById == actorUserId) return;
        var role = await context.Users.AsNoTracking()
            .Where(item => item.Id == actorUserId)
            .Select(item => (Role?)item.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role >= Role.Admin) return;
        throw new ApiOperationTerminalException(
            "bootstrap_profile_forbidden", "The bootstrap profile is owned by another user.");
    }
}
