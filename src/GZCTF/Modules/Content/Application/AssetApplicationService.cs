using GZCTF.Models;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Contracts;
using GZCTF.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace GZCTF.Modules.Content.Application;

public sealed class AssetApplicationService(AppDbContext context, IBlobRepository blobs)
{
    public async Task<AssetDescriptor> UploadAsync(IFormFile file, string? filename,
        Guid creatorId, CancellationToken cancellationToken)
    {
        var asset = await blobs.CreateOrUpdateBlob(file, filename, cancellationToken, creatorId);
        return await ToDescriptorAsync(asset, cancellationToken);
    }

    public async Task<AssetDescriptor?> FindAsync(string hash, CancellationToken cancellationToken)
    {
        var asset = await blobs.GetBlobByHash(hash, cancellationToken);
        return asset is null ? null : await ToDescriptorAsync(asset, cancellationToken);
    }

    public async Task<AssetDeleteStatus> DeleteAsync(string hash, CancellationToken cancellationToken)
    {
        var asset = await blobs.GetBlobByHash(hash, cancellationToken);
        if (asset is null)
            return AssetDeleteStatus.NotFound;

        if (await context.Attachments.AnyAsync(attachment => attachment.LocalFileId == asset.Id, cancellationToken))
            return AssetDeleteStatus.InUse;

        return await blobs.DeleteBlob(asset, cancellationToken) switch
        {
            TaskStatus.Success => AssetDeleteStatus.Success,
            TaskStatus.NotFound => AssetDeleteStatus.NotFound,
            _ => AssetDeleteStatus.Failed
        };
    }

    async Task<AssetDescriptor> ToDescriptorAsync(LocalFile asset, CancellationToken cancellationToken)
    {
        var creatorUserName = asset.CreatedById is { } creatorId
            ? await context.Users.Where(user => user.Id == creatorId)
                .Select(user => user.UserName).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new AssetDescriptor(asset.Hash, asset.Name, asset.FileSize, asset.Url(), creatorUserName);
    }
}
