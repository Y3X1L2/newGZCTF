using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GZCTF.Modules.Audit.Application;
using GZCTF.Modules.Audit.Domain;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Application;

public sealed class AssetApplicationService(
    AppDbContext context,
    IBlobRepository blobs,
    IdempotencyService idempotency)
{
    public const string UploadOperationKind = "asset.upload";
    public const string UploadRouteKey = "POST:/api/open/v1/assets";
    public const long MaxUploadSize = 1024L * 1024 * 1024;

    public async Task<AssetUploadResult> UploadAsync(
        IFormFile file,
        string? filename,
        Guid apiTokenId,
        Guid actorUserId,
        string idempotencyKey,
        string contentDigest,
        CancellationToken cancellationToken)
    {
        var key = ExternalIdempotencyKey.Normalize(idempotencyKey);
        var expectedDigest = ParseContentDigest(contentDigest);
        var name = NormalizeName(filename ?? file.FileName);
        if (file.Length is <= 0 or > MaxUploadSize)
            throw new AssetApiContractException("asset_size_invalid", "An asset must contain 1 byte to 1 GiB.", 400);

        await using var content = BufferHelper.GetTempStream(file.Length);
        await file.CopyToAsync(content, cancellationToken);
        if (content.Length != file.Length)
            throw new AssetApiContractException("asset_size_invalid", "The uploaded file length is invalid.", 400);
        content.Position = 0;
        var digest = await SHA256.HashDataAsync(content, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(expectedDigest, digest))
            throw new AssetApiContractException("asset_digest_mismatch", "Content-Digest does not match the file.", 400);

        var hash = Convert.ToHexStringLower(digest);
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Hash = hash, Name = name, Size = content.Length }))));

        // Commit the completed operation with the blob; the async worker must never see a pending upload.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (context.Database.IsNpgsql())
        {
            var lockKey = "blob:" + hash;
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        }
        var result = await idempotency.BeginAsync(apiTokenId, actorUserId, UploadOperationKind,
            UploadRouteKey, key, requestHash, cancellationToken);
        if (result.Reused)
        {
            if (result.Operation.Status != ApiOperationStatus.Succeeded || result.Operation.ResourceId != hash)
                throw new AssetApiContractException("asset_upload_incomplete", "The previous upload has not completed.", 409);
            var original = await blobs.GetBlobByHash(hash, cancellationToken)
                ?? throw new AssetApiContractException("asset_gone", "The previously uploaded asset no longer exists.", 410);
            await transaction.CommitAsync(cancellationToken);
            return new AssetUploadResult(ToDescriptor(original), result.Operation.Id, true);
        }

        // An upload is not an attachment reference. Existing content keeps its original name and reference count.
        var asset = await blobs.GetBlobByHash(hash, cancellationToken);
        if (asset is null)
        {
            content.Position = 0;
            asset = await blobs.CreateOrUpdateBlobFromStream(name, content, cancellationToken);
        }
        var operation = result.Operation;
        operation.Status = ApiOperationStatus.Succeeded;
        operation.Stage = "completed";
        operation.ResourceType = "asset";
        operation.ResourceId = asset.Hash;
        operation.CurrentProgress = operation.TotalProgress = content.Length;
        operation.StartedAt = operation.CreatedAt;
        operation.UpdatedAt = DateTimeOffset.UtcNow;
        operation.CompletedAt = operation.UpdatedAt;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AssetUploadResult(ToDescriptor(asset), operation.Id, false);
    }

    public Task<bool> CanAccessAsync(Guid actorUserId, string hash, CancellationToken cancellationToken) =>
        !IsValidHash(hash)
            ? Task.FromResult(false)
            : context.ApiOperations.AsNoTracking().AnyAsync(operation =>
                operation.ActorUserId == actorUserId && operation.Kind == UploadOperationKind &&
                operation.Status == ApiOperationStatus.Succeeded && operation.ResourceType == "asset" &&
                operation.ResourceId == hash, cancellationToken);

    public async Task<AssetDescriptor?> FindAccessibleAsync(
        string hash, Guid actorUserId, bool hasExplicitGrant, CancellationToken cancellationToken)
    {
        if (!IsValidHash(hash) ||
            !hasExplicitGrant && !await CanAccessAsync(actorUserId, hash, cancellationToken))
            return null;
        var asset = await blobs.GetBlobByHash(hash, cancellationToken);
        return asset is null ? null : ToDescriptor(asset);
    }

    public static bool IsValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    static AssetDescriptor ToDescriptor(LocalFile asset) => new(asset.Hash, asset.Name, asset.FileSize,
        $"/assets/{asset.Hash}/{Uri.EscapeDataString(asset.Name)}");

    static string NormalizeName(string filename)
    {
        var name = filename.Trim();
        if (name.Length is < 1 or > 255 || name.Any(character => char.IsControl(character) || character is '/' or '\\'))
            throw new AssetApiContractException("asset_name_invalid", "The asset filename is invalid.", 400);
        return name;
    }

    static byte[] ParseContentDigest(string contentDigest)
    {
        const string prefix = "sha-256=:";
        var value = contentDigest?.Trim() ?? string.Empty;
        if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith(':'))
        {
            try
            {
                var digest = Convert.FromBase64String(value[prefix.Length..^1]);
                if (digest.Length == 32)
                    return digest;
            }
            catch (FormatException) { }
        }
        throw new AssetApiContractException("asset_digest_invalid", "Content-Digest must contain sha-256=:base64:.", 400);
    }
}
